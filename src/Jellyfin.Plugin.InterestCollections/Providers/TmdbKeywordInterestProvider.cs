using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.InterestCollections.Configuration;
using Jellyfin.Plugin.InterestCollections.Models;
using Jellyfin.Plugin.InterestCollections.Providers.Http;
using Jellyfin.Plugin.InterestCollections.Services;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections.Providers;

/// <summary>
/// Derives interests from TMDb keywords. TMDb is an official, documented API with a free key and
/// no usage caveats, which makes it the safe choice for anyone uncomfortable with IMDb's terms.
/// </summary>
/// <remarks>
/// TMDb keywords are far noisier than IMDb interests — they include plot particulars such as
/// "based on novel or book" alongside genuine subgenres. Only keywords that resolve against the
/// bundled taxonomy are kept, which trades recall for precision on purpose.
/// </remarks>
public sealed class TmdbKeywordInterestProvider : IInterestProvider
{
    /// <summary>
    /// The public TMDb API root.
    /// </summary>
    public const string DefaultBaseUrl = "https://api.themoviedb.org/3";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ResilientHttpClient _httpClient;
    private readonly ILogger<TmdbKeywordInterestProvider> _logger;
    private readonly InterestTaxonomy _taxonomy;
    private readonly Func<PluginConfiguration> _configurationAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbKeywordInterestProvider"/> class.
    /// </summary>
    /// <param name="httpClient">The throttled HTTP client.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="taxonomy">The interest taxonomy used to canonicalise keywords.</param>
    /// <param name="configurationAccessor">Reads the current plugin configuration.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public TmdbKeywordInterestProvider(
        ResilientHttpClient httpClient,
        ILogger<TmdbKeywordInterestProvider> logger,
        InterestTaxonomy taxonomy,
        Func<PluginConfiguration> configurationAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(taxonomy);
        ArgumentNullException.ThrowIfNull(configurationAccessor);

        _httpClient = httpClient;
        _logger = logger;
        _taxonomy = taxonomy;
        _configurationAccessor = configurationAccessor;
    }

    /// <inheritdoc />
    public string Id => "tmdb-keywords";

    /// <inheritdoc />
    public string Name => "TMDb Keywords";

    /// <inheritdoc />
    public int ResultVersion => 1;

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configurationAccessor().ApiKey);

    /// <inheritdoc />
    public async Task<ProviderResult> GetInterestsAsync(
        MediaIdentity media,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);

        var configuration = _configurationAccessor();
        var apiKey = configuration.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ProviderResult.Failure("No TMDb API key is configured");
        }

        var policy = RequestPolicy.FromConfiguration(configuration);
        var baseUrl = ResolveBaseUrl(configuration);
        var isSeries = media.Kind != MediaKind.Movie;

        var tmdbId = media.TmdbId;
        if (string.IsNullOrWhiteSpace(tmdbId))
        {
            if (string.IsNullOrWhiteSpace(media.ImdbId))
            {
                return ProviderResult.Empty;
            }

            var lookup = await ResolveTmdbIdAsync(baseUrl, apiKey, media.ImdbId, isSeries, policy, cancellationToken)
                .ConfigureAwait(false);

            if (!lookup.Succeeded)
            {
                return ProviderResult.Failure(lookup.FailureReason!);
            }

            if (string.IsNullOrEmpty(lookup.TmdbId))
            {
                return ProviderResult.Empty;
            }

            tmdbId = lookup.TmdbId;
        }

        var segment = isSeries ? "tv" : "movie";
        var url = new Uri(string.Format(
            CultureInfo.InvariantCulture,
            "{0}/{1}/{2}/keywords?api_key={3}",
            baseUrl,
            segment,
            Uri.EscapeDataString(tmdbId),
            Uri.EscapeDataString(apiKey)));

        using var response = await _httpClient
            .SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), policy, cancellationToken)
            .ConfigureAwait(false);

        if (response is null)
        {
            return ProviderResult.Failure("TMDb did not respond");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ProviderResult.Empty;
        }

        if (!response.IsSuccessStatusCode)
        {
            return ProviderResult.Failure(DescribeStatus(response.StatusCode));
        }

        try
        {
            var payload = await response.Content
                .ReadFromJsonSafeAsync<KeywordsResponse>(_jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (payload is null)
            {
                return ProviderResult.Failure("TMDb returned an unreadable response");
            }

            return ProviderResult.Success(MapKeywords(payload));
        }
        catch (JsonException ex)
        {
            return ProviderResult.Failure(string.Format(
                CultureInfo.InvariantCulture,
                "TMDb returned malformed JSON: {0}",
                ex.Message));
        }
    }

    /// <inheritdoc />
    public async Task<ProviderTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return ProviderTestResult.Fail("Enter a TMDb API key first.");
        }

        var probe = new MediaIdentity
        {
            ItemId = Guid.Empty,
            Name = "Breaking Bad",
            Kind = MediaKind.Series,
            ImdbId = "tt0903747",
        };

        var result = await GetInterestsAsync(probe, cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? ProviderTestResult.Ok(string.Format(
                CultureInfo.InvariantCulture,
                "Connected. TMDb keywords mapped to {0} interests for the probe title.",
                result.Interests.Count))
            : ProviderTestResult.Fail(result.FailureReason ?? "The request failed.");
    }

    /// <summary>
    /// Describes a status code without ever echoing the request, which carries the API key.
    /// </summary>
    /// <param name="statusCode">The status code returned.</param>
    /// <returns>A message safe to log and display.</returns>
    private static string DescribeStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "TMDb rejected the API key",
        HttpStatusCode.Forbidden => "TMDb refused the request",
        _ => string.Format(CultureInfo.InvariantCulture, "TMDb returned HTTP {0}", (int)statusCode),
    };

    /// <summary>
    /// Resolves the API root, falling back to the documented default.
    /// </summary>
    /// <param name="configuration">The plugin configuration.</param>
    /// <returns>The API root, without a trailing slash.</returns>
    private string ResolveBaseUrl(PluginConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.ApiBaseUrl)
            && Uri.TryCreate(configuration.ApiBaseUrl, UriKind.Absolute, out var custom)
            && (custom.Scheme == Uri.UriSchemeHttp || custom.Scheme == Uri.UriSchemeHttps))
        {
            return custom.ToString().TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(configuration.ApiBaseUrl))
        {
            _logger.LogWarning("Ignoring an invalid API base URL and using the TMDb default instead");
        }

        return DefaultBaseUrl;
    }

    /// <summary>
    /// Translates an IMDb id into a TMDb id using TMDb's external-id lookup, so title matching is
    /// never needed.
    /// </summary>
    /// <param name="baseUrl">The API root.</param>
    /// <param name="apiKey">The API key.</param>
    /// <param name="imdbId">The IMDb identifier.</param>
    /// <param name="isSeries">Whether a series is being looked up.</param>
    /// <param name="policy">The request policy.</param>
    /// <param name="cancellationToken">Token used to cancel the lookup.</param>
    /// <returns>The lookup outcome.</returns>
    private async Task<TmdbLookup> ResolveTmdbIdAsync(
        string baseUrl,
        string apiKey,
        string imdbId,
        bool isSeries,
        RequestPolicy policy,
        CancellationToken cancellationToken)
    {
        var url = new Uri(string.Format(
            CultureInfo.InvariantCulture,
            "{0}/find/{1}?external_source=imdb_id&api_key={2}",
            baseUrl,
            Uri.EscapeDataString(imdbId),
            Uri.EscapeDataString(apiKey)));

        using var response = await _httpClient
            .SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), policy, cancellationToken)
            .ConfigureAwait(false);

        if (response is null)
        {
            return TmdbLookup.Failed("TMDb did not respond");
        }

        if (!response.IsSuccessStatusCode)
        {
            return TmdbLookup.Failed(DescribeStatus(response.StatusCode));
        }

        try
        {
            var payload = await response.Content
                .ReadFromJsonSafeAsync<FindResponse>(_jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            var matches = isSeries ? payload?.TvResults : payload?.MovieResults;
            var id = matches is { Count: > 0 } ? matches[0].Id : null;

            return TmdbLookup.Found(id?.ToString(CultureInfo.InvariantCulture));
        }
        catch (JsonException ex)
        {
            return TmdbLookup.Failed(string.Format(
                CultureInfo.InvariantCulture,
                "TMDb returned malformed JSON: {0}",
                ex.Message));
        }
    }

    /// <summary>
    /// Keeps only keywords that resolve to a taxonomy entry, discarding TMDb's long tail of plot
    /// particulars.
    /// </summary>
    /// <param name="payload">The keywords payload.</param>
    /// <returns>The interests recognised.</returns>
    private List<InterestRef> MapKeywords(KeywordsResponse payload)
    {
        // Movies use "keywords", series use "results", for the same shape.
        var keywords = payload.Keywords ?? payload.Results ?? [];
        var results = new List<InterestRef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword.Name)
                || !_taxonomy.TryGetByName(keyword.Name, out var definition)
                || definition is null)
            {
                continue;
            }

            var resolved = _taxonomy.Resolve(definition.Id, definition.Name);
            if (resolved is not null && seen.Add(resolved.Key))
            {
                results.Add(resolved);
            }
        }

        return results;
    }

    private sealed class TmdbLookup
    {
        public bool Succeeded { get; private init; }

        public string? TmdbId { get; private init; }

        public string? FailureReason { get; private init; }

        public static TmdbLookup Found(string? tmdbId)
            => new() { Succeeded = true, TmdbId = tmdbId };

        public static TmdbLookup Failed(string reason)
            => new() { Succeeded = false, FailureReason = reason };
    }

    private sealed class FindResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("movie_results")]
        public IReadOnlyList<FindMatch>? MovieResults { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("tv_results")]
        public IReadOnlyList<FindMatch>? TvResults { get; init; }
    }

    private sealed class FindMatch
    {
        public int? Id { get; init; }
    }

    private sealed class KeywordsResponse
    {
        public IReadOnlyList<Keyword>? Keywords { get; init; }

        public IReadOnlyList<Keyword>? Results { get; init; }
    }

    private sealed class Keyword
    {
        public string? Name { get; init; }
    }
}
