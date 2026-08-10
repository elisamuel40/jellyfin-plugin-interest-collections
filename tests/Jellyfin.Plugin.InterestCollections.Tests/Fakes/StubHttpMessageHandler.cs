using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.InterestCollections.Tests.Fakes;

/// <summary>
/// Replays a queued sequence of responses and records the requests it received, so provider tests
/// never touch the network.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public int CallCount => Requests.Count;

    public StubHttpMessageHandler EnqueueJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

        return this;
    }

    public StubHttpMessageHandler EnqueueStatus(HttpStatusCode statusCode, TimeSpan? retryAfter = null)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(statusCode);
            if (retryAfter is { } delay)
            {
                response.Headers.RetryAfter =
                    new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
            }

            return response;
        });

        return this;
    }

    public StubHttpMessageHandler EnqueueThrow(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    /// <summary>
    /// Repeats the given status for every remaining call, for tests that exhaust the retry budget.
    /// </summary>
    public StubHttpMessageHandler AlwaysFailWith(HttpStatusCode statusCode)
    {
        for (var index = 0; index < 32; index++)
        {
            EnqueueStatus(statusCode);
        }

        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("The stub handler ran out of queued responses.");
        }

        return Task.FromResult(_responses.Dequeue()(request));
    }
}
