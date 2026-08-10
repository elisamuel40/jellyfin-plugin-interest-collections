using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.InterestCollections.Models;

/// <summary>
/// The kind of library item being classified.
/// </summary>
public enum MediaKind
{
    /// <summary>A movie.</summary>
    Movie = 0,

    /// <summary>A TV series.</summary>
    Series = 1,

    /// <summary>A single episode.</summary>
    Episode = 2,
}

/// <summary>
/// Everything a provider needs to identify one library item, decoupled from Jellyfin's own
/// entity types so providers stay testable without a running server.
/// </summary>
public sealed class MediaIdentity
{
    /// <summary>
    /// Gets the Jellyfin item identifier.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets the item name, used to reject interests named after the title itself.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the kind of item.
    /// </summary>
    public required MediaKind Kind { get; init; }

    /// <summary>
    /// Gets the production year, when known.
    /// </summary>
    public int? ProductionYear { get; init; }

    /// <summary>
    /// Gets the IMDb identifier, for example <c>tt0903747</c>.
    /// </summary>
    public string? ImdbId { get; init; }

    /// <summary>
    /// Gets the TMDb identifier.
    /// </summary>
    public string? TmdbId { get; init; }

    /// <summary>
    /// Gets the TVDB identifier.
    /// </summary>
    public string? TvdbId { get; init; }

    /// <summary>
    /// Gets the genres Jellyfin already holds for the item.
    /// </summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>
    /// Gets the tags currently on the item, including tags this plugin does not own.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the item carries at least one usable provider id.
    /// </summary>
    public bool HasProviderId =>
        !string.IsNullOrWhiteSpace(ImdbId)
        || !string.IsNullOrWhiteSpace(TmdbId)
        || !string.IsNullOrWhiteSpace(TvdbId);

    /// <summary>
    /// Builds the cache key for this item under a given provider. The key is derived from the
    /// most stable identifier available so that renaming a file never invalidates the cache.
    /// </summary>
    /// <param name="providerId">The provider's stable identifier.</param>
    /// <returns>The cache key.</returns>
    public string GetCacheKey(string providerId)
    {
        var identifier =
            !string.IsNullOrWhiteSpace(ImdbId) ? "imdb:" + ImdbId
            : !string.IsNullOrWhiteSpace(TmdbId) ? "tmdb:" + TmdbId
            : !string.IsNullOrWhiteSpace(TvdbId) ? "tvdb:" + TvdbId
            : "item:" + ItemId.ToString("N", CultureInfo.InvariantCulture);

        return string.Concat(providerId, "|", Kind.ToString(), "|", identifier);
    }
}
