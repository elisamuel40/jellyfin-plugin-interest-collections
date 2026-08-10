using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.InterestCollections.Configuration;
using Jellyfin.Plugin.InterestCollections.Models;
using Jellyfin.Plugin.InterestCollections.Providers;
using Jellyfin.Plugin.InterestCollections.Providers.Http;
using Jellyfin.Plugin.InterestCollections.Services;
using Jellyfin.Plugin.InterestCollections.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.InterestCollections.Tests.Providers;

public class ImdbGraphQlInterestProviderTests
{
    /// <summary>
    /// The exact shape IMDb's public endpoint returns for Breaking Bad, captured from a live call.
    /// Note the leading franchise interest named after the series itself.
    /// </summary>
    private const string BreakingBadPayload = """
    {"data":{"title":{"id":"tt0903747","titleText":{"text":"Breaking Bad"},
    "interests":{"edges":[
      {"node":{"id":"in0000274","primaryText":{"text":"Breaking Bad"}}},
      {"node":{"id":"in0000035","primaryText":{"text":"Dark Comedy"}}},
      {"node":{"id":"in0000053","primaryText":{"text":"Drug Crime"}}},
      {"node":{"id":"in0000086","primaryText":{"text":"Psychological Drama"}}},
      {"node":{"id":"in0000182","primaryText":{"text":"Psychological Thriller"}}},
      {"node":{"id":"in0000052","primaryText":{"text":"Crime"}}}
    ]}}}}
    """;

    private static MediaIdentity BreakingBad => new()
    {
        ItemId = Guid.NewGuid(),
        Name = "Breaking Bad",
        Kind = MediaKind.Series,
        ImdbId = "tt0903747",
    };

    private static (ImdbGraphQlInterestProvider Provider, StubHttpMessageHandler Handler) Build(
        Action<StubHttpMessageHandler> arrange,
        PluginConfiguration? configuration = null)
    {
        var handler = new StubHttpMessageHandler();
        arrange(handler);

        var settings = configuration ?? new PluginConfiguration
        {
            RequestDelayMilliseconds = 0,
            MaxRetries = 2,
        };

        var client = new ResilientHttpClient(
            new HttpClient(handler),
            NullLogger<ResilientHttpClient>.Instance);

        var provider = new ImdbGraphQlInterestProvider(
            client,
            NullLogger<ImdbGraphQlInterestProvider>.Instance,
            InterestTaxonomy.Shared,
            () => settings);

        return (provider, handler);
    }

    [Fact]
    public async Task GetInterestsAsync_ParsesTheLiveResponseShape()
    {
        var (provider, _) = Build(handler => handler.EnqueueJson(BreakingBadPayload));

        var result = await provider.GetInterestsAsync(BreakingBad, CancellationToken.None);

        Assert.True(result.Succeeded);
        var names = result.Interests.Select(interest => interest.Name).ToList();
        Assert.Contains("Dark Comedy", names);
        Assert.Contains("Drug Crime", names);
        Assert.Contains("Psychological Drama", names);
        Assert.Contains("Psychological Thriller", names);
    }

    [Fact]
    public async Task GetInterestsAsync_CarriesTaxonomyMetadataThrough()
    {
        var (provider, _) = Build(handler => handler.EnqueueJson(BreakingBadPayload));

        var result = await provider.GetInterestsAsync(BreakingBad, CancellationToken.None);

        var genreLevel = result.Interests.Single(interest => interest.Name == "Crime");
        Assert.True(genreLevel.IsGenreLevel);
        Assert.Equal("Crime", genreLevel.Category);

        var specific = result.Interests.Single(interest => interest.Name == "Drug Crime");
        Assert.False(specific.IsGenreLevel);
    }

    [Fact]
    public async Task GetInterestsAsync_SendsTheClientHeadersImdbRequires()
    {
        var (provider, handler) = Build(h => h.EnqueueJson(BreakingBadPayload));

        await provider.GetInterestsAsync(BreakingBad, CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.True(request.Headers.Contains("x-imdb-client-name"));
        Assert.Equal(HttpMethod.Post, request.Method);
    }

    [Fact]
    public async Task GetInterestsAsync_TreatsAMissingImdbIdAsAnEmptyAnswerRatherThanAFailure()
    {
        var (provider, handler) = Build(_ => { });

        var result = await provider.GetInterestsAsync(
            new MediaIdentity { ItemId = Guid.NewGuid(), Name = "Home Video", Kind = MediaKind.Movie },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Interests);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetInterestsAsync_TreatsAnUnknownTitleAsAnEmptyAnswer()
    {
        var (provider, _) = Build(handler => handler.EnqueueJson("""{"data":{"title":null}}"""));

        var result = await provider.GetInterestsAsync(BreakingBad, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Interests);
    }

    [Fact]
    public async Task GetInterestsAsync_ReportsGraphQlErrorsAsFailures()
    {
        var (provider, _) = Build(handler =>
            handler.EnqueueJson("""{"errors":[{"message":"Cannot query field"}]}"""));

        var result = await provider.GetInterestsAsync(BreakingBad, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Cannot query field", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetInterestsAsync_FailsRatherThanReturningEmptyWhenTheServiceIsDown()
    {
        var (provider, _) = Build(handler => handler.AlwaysFailWith(HttpStatusCode.InternalServerError));

        var result = await provider.GetInterestsAsync(BreakingBad, CancellationToken.None);

        // Crucial: a failure must never look like "this title has no interests", because the
        // caller would then strip interests the item legitimately has.
        Assert.False(result.Succeeded);
        Assert.Empty(result.Interests);
    }

    [Fact]
    public async Task GetInterestsAsync_RecoversWhenARetrySucceeds()
    {
        var (provider, handler) = Build(h => h
            .EnqueueStatus(HttpStatusCode.TooManyRequests, TimeSpan.FromMilliseconds(1))
            .EnqueueJson(BreakingBadPayload));

        var result = await provider.GetInterestsAsync(BreakingBad, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.Interests);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetInterestsAsync_DoesNotRetryAClientError()
    {
        var (provider, handler) = Build(h => h.EnqueueStatus(HttpStatusCode.BadRequest));

        var result = await provider.GetInterestsAsync(BreakingBad, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetInterestsAsync_StopsAfterTheConfiguredRetryBudget()
    {
        var settings = new PluginConfiguration { RequestDelayMilliseconds = 0, MaxRetries = 2 };
        var (provider, handler) = Build(
            h => h.AlwaysFailWith(HttpStatusCode.BadGateway),
            settings);

        await provider.GetInterestsAsync(BreakingBad, CancellationToken.None);

        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task GetInterestsAsync_SurvivesMalformedJson()
    {
        var (provider, _) = Build(handler => handler.EnqueueJson("{ this is not json"));

        var result = await provider.GetInterestsAsync(BreakingBad, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetInterestsAsync_SurvivesATransportFailure()
    {
        var (provider, _) = Build(handler => handler
            .EnqueueThrow(new HttpRequestException("connection refused"))
            .EnqueueThrow(new HttpRequestException("connection refused"))
            .EnqueueThrow(new HttpRequestException("connection refused")));

        var result = await provider.GetInterestsAsync(BreakingBad, CancellationToken.None);

        Assert.False(result.Succeeded);
    }
}
