using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections.Providers.Http;

/// <summary>
/// Sends provider requests under a concurrency cap, a minimum spacing between calls, a per-attempt
/// timeout and exponential backoff with jitter.
/// </summary>
/// <remarks>
/// Every failure path ends in a returned <see langword="null"/> rather than an exception, except
/// for cancellation requested by the caller. Provider code therefore never has to guard a library
/// scan against a metadata service being down.
/// </remarks>
public sealed class ResilientHttpClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ResilientHttpClient> _logger;
    private readonly SemaphoreSlim _spacingLock = new(1, 1);

    private SemaphoreSlim _concurrencyGate = new(1, 1);
    private int _concurrencyLimit = 1;
    private DateTimeOffset _nextAllowedStart = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResilientHttpClient"/> class.
    /// </summary>
    /// <param name="httpClient">The underlying client.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public ResilientHttpClient(HttpClient httpClient, ILogger<ResilientHttpClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Sends a request, retrying transient failures.
    /// </summary>
    /// <param name="requestFactory">
    /// Builds the request. Called once per attempt, because a request message cannot be reused.
    /// </param>
    /// <param name="policy">The throttling and retry limits.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// The response when one was obtained, or <see langword="null"/> when every attempt failed.
    /// The caller owns the returned response and must dispose it.
    /// </returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled the operation.</exception>
    public async Task<HttpResponseMessage?> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        RequestPolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestFactory);
        ArgumentNullException.ThrowIfNull(policy);

        var gate = GetConcurrencyGate(policy.MaxConcurrency);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            for (var attempt = 0; ; attempt++)
            {
                await WaitForTurnAsync(policy.MinimumDelay, cancellationToken).ConfigureAwait(false);

                var outcome = await TryOnceAsync(requestFactory, policy, cancellationToken)
                    .ConfigureAwait(false);

                if (outcome.Response is not null)
                {
                    return outcome.Response;
                }

                if (attempt >= policy.MaxRetries)
                {
                    _logger.LogWarning(
                        "Giving up after {Attempts} attempts: {Reason}",
                        attempt + 1,
                        outcome.Reason);
                    return null;
                }

                var delay = outcome.RetryAfter ?? GetBackoffDelay(attempt);
                if (delay > policy.MaximumRetryAfter)
                {
                    _logger.LogWarning(
                        "Provider asked to wait {Delay}, which exceeds the limit; abandoning the request",
                        delay);
                    return null;
                }

                _logger.LogDebug(
                    "Attempt {Attempt} failed ({Reason}); retrying in {Delay}",
                    attempt + 1,
                    outcome.Reason,
                    delay);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _spacingLock.Dispose();
        _concurrencyGate.Dispose();
    }

    /// <summary>
    /// Computes the exponential backoff delay for an attempt, with jitter so that a batch of
    /// requests failing together does not retry in lockstep.
    /// </summary>
    /// <param name="attempt">The zero-based attempt number that just failed.</param>
    /// <returns>The delay before the next attempt.</returns>
    private static TimeSpan GetBackoffDelay(int attempt)
    {
        var seconds = Math.Min(Math.Pow(2, attempt), 30);
        var jitter = Random.Shared.NextDouble() * 0.5;
        return TimeSpan.FromSeconds(seconds + jitter);
    }

    /// <summary>
    /// Determines whether a status code is worth retrying.
    /// </summary>
    /// <param name="statusCode">The status code returned.</param>
    /// <returns><see langword="true"/> when another attempt could succeed.</returns>
    private static bool IsRetryable(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests
            || statusCode == HttpStatusCode.RequestTimeout
            || (int)statusCode >= 500;

    /// <summary>
    /// Reads the Retry-After header, accepting both the delay and the date form.
    /// </summary>
    /// <param name="response">The response to inspect.</param>
    /// <returns>The requested delay, or null when the header is absent or unusable.</returns>
    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the concurrency gate, rebuilding it when the configured limit changed.
    /// </summary>
    /// <param name="limit">The configured limit.</param>
    /// <returns>The gate to wait on.</returns>
    private SemaphoreSlim GetConcurrencyGate(int limit)
    {
        var current = Volatile.Read(ref _concurrencyGate);

        if (Volatile.Read(ref _concurrencyLimit) == limit)
        {
            return current;
        }

        // The previous gate is deliberately left undisposed: callers may still be holding it, and
        // SemaphoreSlim only needs disposal once its wait handle has been materialised.
        var replacement = new SemaphoreSlim(limit, limit);
        Volatile.Write(ref _concurrencyGate, replacement);
        Volatile.Write(ref _concurrencyLimit, limit);
        return replacement;
    }

    /// <summary>
    /// Blocks until enough time has passed since the previous request started.
    /// </summary>
    /// <param name="minimumDelay">The minimum spacing between requests.</param>
    /// <param name="cancellationToken">Token used to cancel the wait.</param>
    /// <returns>A task that completes when the caller may proceed.</returns>
    private async Task WaitForTurnAsync(TimeSpan minimumDelay, CancellationToken cancellationToken)
    {
        if (minimumDelay <= TimeSpan.Zero)
        {
            return;
        }

        TimeSpan wait;

        await _spacingLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            wait = _nextAllowedStart > now ? _nextAllowedStart - now : TimeSpan.Zero;
            _nextAllowedStart = (now > _nextAllowedStart ? now : _nextAllowedStart) + minimumDelay;
        }
        finally
        {
            _spacingLock.Release();
        }

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Performs a single attempt.
    /// </summary>
    /// <param name="requestFactory">Builds the request for this attempt.</param>
    /// <param name="policy">The limits in force.</param>
    /// <param name="cancellationToken">Token used to cancel the attempt.</param>
    /// <returns>The attempt outcome.</returns>
    private async Task<Attempt> TryOnceAsync(
        Func<HttpRequestMessage> requestFactory,
        RequestPolicy policy,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(policy.Timeout);

        HttpResponseMessage? response = null;

        try
        {
            using var request = requestFactory();
            response = await _httpClient.SendAsync(request, timeoutSource.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var accepted = response;
                response = null;
                return new Attempt(accepted, null, null);
            }

            var statusCode = response.StatusCode;
            if (!IsRetryable(statusCode))
            {
                var rejected = response;
                response = null;
                return new Attempt(rejected, null, null);
            }

            var retryAfter = ReadRetryAfter(response);
            return new Attempt(
                null,
                string.Format(CultureInfo.InvariantCulture, "HTTP {0}", (int)statusCode),
                retryAfter);
        }
        catch (HttpRequestException ex)
        {
            return new Attempt(null, ex.Message, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new Attempt(null, "request timed out", null);
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>
    /// The result of one attempt: either a response to hand back, or a reason and an optional
    /// server-requested delay.
    /// </summary>
    /// <param name="Response">The response, when the attempt produced one.</param>
    /// <param name="Reason">Why the attempt failed.</param>
    /// <param name="RetryAfter">The delay the server asked for, when it did.</param>
    private sealed record Attempt(HttpResponseMessage? Response, string? Reason, TimeSpan? RetryAfter);
}
