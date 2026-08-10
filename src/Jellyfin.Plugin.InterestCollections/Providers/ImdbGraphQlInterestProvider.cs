using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
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
/// Reads genuine IMDb "Interests" from IMDb's public GraphQL endpoint — the same structured API
/// the IMDb website itself queries. No HTML is scraped and no API key is needed.
/// </summary>
/// <remarks>
/// IMDb attaches a notice to every response stating that public, commercial or non-private use of
/// this data is not permitted, and that only limited non-commercial use is allowed. A self-hosted
/// Jellyfin server serving its owner falls inside that limit; redistributing the results does not.
/// The plugin therefore caches aggressively and throttles conservatively by default, and the
/// README states this plainly so administrators can make their own call.
/// </remarks>
public sealed class ImdbGraphQlInterestProvider : IInterestProvider
{
    /// <summary>
    /// The endpoint used by default. The cached edge is preferred over the origin because it is
    /// gentler on IMDb and answers identically.
    /// </summary>
    public const string DefaultEndpoint = "https://caching.graphql.imdb.com/";

    private const string InterestsQuery =
        "query InterestsForTitle($id: ID!) { title(id: $id) { id titleText { text } " +
        "interests(first: 50) { edges { node { id primaryText { text } } } } } }";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ResilientHttpClient _httpClient;
    private readonly ILogger<ImdbGraphQlInterestProvider> _logger;
    private readonly InterestTaxonomy _taxonomy;
    private readonly Func<PluginConfiguration> _configurationAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImdbGraphQlInterestProvider"/> class.
    /// </summary>
    /// <param name="httpClient">The throttled HTTP client.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="taxonomy">The interest taxonomy used to canonicalise results.</param>
    /// <param name="configurationAccessor">Reads the current plugin configuration.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public ImdbGraphQlInterestProvider(
        ResilientHttpClient httpClient,
        ILogger<ImdbGraphQlInterestProvider> logger,
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
    public string Id => "imdb-graphql";

    /// <inheritdoc />
    public string Name => "IMDb Interests";

    /// <inheritdoc />
    public int ResultVersion => 1;

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    public async Task<ProviderResult> GetInterestsAsync(
        MediaIdentity media,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);

        if (string.IsNullOrWhiteSpace(media.ImdbId))
        {
            // Without an IMDb id there is nothing to ask for. This is a definitive answer rather
            // than a failure, so it is safe to cache.
            return ProviderResult.Empty;
        }

        var configuration = _configurationAccessor();
        var policy = RequestPolicy.FromConfiguration(configuration);
        var endpoint = ResolveEndpoint(configuration);

        using var response = await _httpClient.SendAsync(
            () => BuildRequest(endpoint, InterestsQuery, media.ImdbId),
            policy,
            cancellationToken).ConfigureAwait(false);

        if (response is null)
        {
            return ProviderResult.Failure("IMDb did not respond");
        }

        if (!response.IsSuccessStatusCode)
        {
            return ProviderResult.Failure(string.Format(
                CultureInfo.InvariantCulture,
                "IMDb returned HTTP {0}",
                (int)response.StatusCode));
        }

        try
        {
            var payload = await response.Content
                .ReadFromJsonSafeAsync<GraphQlResponse>(_jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (payload is null)
            {
                return ProviderResult.Failure("IMDb returned an unreadable response");
            }

            if (payload.Errors is { Count: > 0 })
            {
                return ProviderResult.Failure(string.Format(
                    CultureInfo.InvariantCulture,
                    "IMDb reported an error: {0}",
                    payload.Errors[0].Message ?? "unknown"));
            }

            var title = payload.Data?.Title;
            if (title is null)
            {
                // IMDb knows nothing about this id. Definitive, and worth caching.
                _logger.LogDebug("IMDb has no title for {ImdbId}", media.ImdbId);
                return ProviderResult.Empty;
            }

            return ProviderResult.Success(ExtractInterests(title));
        }
        catch (JsonException ex)
        {
            return ProviderResult.Failure(string.Format(
                CultureInfo.InvariantCulture,
                "IMDb returned malformed JSON: {0}",
                ex.Message));
        }
    }

    /// <inheritdoc />
    public async Task<ProviderTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        // Breaking Bad is a stable, well-populated probe: if it comes back with interests, the
        // endpoint, the headers and the response shape are all still good.
        var probe = new MediaIdentity
        {
            ItemId = Guid.Empty,
            Name = "Breaking Bad",
            Kind = MediaKind.Series,
            ImdbId = "tt0903747",
        };

        var result = await GetInterestsAsync(probe, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return ProviderTestResult.Fail(result.FailureReason ?? "The request failed.");
        }

        if (result.Interests.Count == 0)
        {
            return ProviderTestResult.Fail(
                "IMDb answered but returned no interests for the probe title, which usually means "
                + "the response shape changed. Please open an issue.");
        }

        return ProviderTestResult.Ok(string.Format(
            CultureInfo.InvariantCulture,
            "Connected. IMDb returned {0} interests for the probe title.",
            result.Interests.Count));
    }

    /// <summary>
    /// Builds one GraphQL request. IMDb rejects calls that do not identify a client.
    /// </summary>
    /// <param name="endpoint">The endpoint to call.</param>
    /// <param name="query">The GraphQL query.</param>
    /// <param name="imdbId">The title identifier.</param>
    /// <returns>The request message.</returns>
    private static HttpRequestMessage BuildRequest(Uri endpoint, string query, string imdbId)
    {
        var body = JsonSerializer.Serialize(new
        {
            query,
            variables = new { id = imdbId },
        });

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation("x-imdb-client-name", "imdb-web-next");
        request.Headers.TryAddWithoutValidation("x-imdb-user-country", "US");
        request.Headers.TryAddWithoutValidation("x-imdb-user-language", "en-US");
        request.Headers.TryAddWithoutValidation("Origin", "https://www.imdb.com");
        request.Headers.TryAddWithoutValidation("Referer", "https://www.imdb.com/");

        return request;
    }

    /// <summary>
    /// Resolves the endpoint, falling back to the default when the override is missing or invalid.
    /// </summary>
    /// <param name="configuration">The plugin configuration.</param>
    /// <returns>The endpoint to call.</returns>
    private Uri ResolveEndpoint(PluginConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.ApiBaseUrl)
            && Uri.TryCreate(configuration.ApiBaseUrl, UriKind.Absolute, out var custom)
            && (custom.Scheme == Uri.UriSchemeHttp || custom.Scheme == Uri.UriSchemeHttps))
        {
            return custom;
        }

        if (!string.IsNullOrWhiteSpace(configuration.ApiBaseUrl))
        {
            _logger.LogWarning("Ignoring an invalid API base URL and using the IMDb default instead");
        }

        return new Uri(DefaultEndpoint);
    }

    /// <summary>
    /// Turns the GraphQL payload into canonical interest references.
    /// </summary>
    /// <param name="title">The title node.</param>
    /// <returns>The resolved interests, in the order IMDb returned them, without duplicates.</returns>
    private List<InterestRef> ExtractInterests(TitleNode title)
    {
        var edges = title.Interests?.Edges;
        if (edges is null || edges.Count == 0)
        {
            return [];
        }

        var results = new List<InterestRef>(edges.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var edge in edges)
        {
            var node = edge.Node;
            if (node is null)
            {
                continue;
            }

            var resolved = _taxonomy.Resolve(node.Id, node.PrimaryText?.Text);
            if (resolved is not null && seen.Add(resolved.Key))
            {
                results.Add(resolved);
            }
        }

        return results;
    }

    private sealed class GraphQlResponse
    {
        public GraphQlData? Data { get; init; }

        public IReadOnlyList<GraphQlError>? Errors { get; init; }
    }

    private sealed class GraphQlError
    {
        public string? Message { get; init; }
    }

    private sealed class GraphQlData
    {
        public TitleNode? Title { get; init; }
    }

    private sealed class TitleNode
    {
        public string? Id { get; init; }

        public InterestConnection? Interests { get; init; }
    }

    private sealed class InterestConnection
    {
        public IReadOnlyList<InterestEdge>? Edges { get; init; }
    }

    private sealed class InterestEdge
    {
        public InterestNode? Node { get; init; }
    }

    private sealed class InterestNode
    {
        public string? Id { get; init; }

        public TextValue? PrimaryText { get; init; }
    }

    private sealed class TextValue
    {
        public string? Text { get; init; }
    }
}
