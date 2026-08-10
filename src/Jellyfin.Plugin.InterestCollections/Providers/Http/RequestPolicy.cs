using System;
using Jellyfin.Plugin.InterestCollections.Configuration;

namespace Jellyfin.Plugin.InterestCollections.Providers.Http;

/// <summary>
/// The throttling and retry limits applied to outgoing provider requests.
/// </summary>
public sealed class RequestPolicy
{
    /// <summary>
    /// Gets the maximum number of requests in flight at once.
    /// </summary>
    public required int MaxConcurrency { get; init; }

    /// <summary>
    /// Gets the minimum spacing between the start of two requests.
    /// </summary>
    public required TimeSpan MinimumDelay { get; init; }

    /// <summary>
    /// Gets the per-attempt timeout.
    /// </summary>
    public required TimeSpan Timeout { get; init; }

    /// <summary>
    /// Gets how many times a retryable failure is retried before giving up.
    /// </summary>
    public required int MaxRetries { get; init; }

    /// <summary>
    /// Gets the longest a server-supplied Retry-After delay is honoured before the request is
    /// abandoned instead. Prevents a misbehaving server from stalling a library scan for hours.
    /// </summary>
    public TimeSpan MaximumRetryAfter { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Builds a policy from the plugin configuration, clamping every value to a sane range so a
    /// mistyped setting cannot flood a provider or hang a scan.
    /// </summary>
    /// <param name="configuration">The plugin configuration.</param>
    /// <returns>The resulting policy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    public static RequestPolicy FromConfiguration(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new RequestPolicy
        {
            MaxConcurrency = Math.Clamp(configuration.MaxConcurrentRequests, 1, 16),
            MinimumDelay = TimeSpan.FromMilliseconds(Math.Clamp(configuration.RequestDelayMilliseconds, 0, 10_000)),
            Timeout = TimeSpan.FromSeconds(Math.Clamp(configuration.RequestTimeoutSeconds, 5, 300)),
            MaxRetries = Math.Clamp(configuration.MaxRetries, 0, 10),
        };
    }
}
