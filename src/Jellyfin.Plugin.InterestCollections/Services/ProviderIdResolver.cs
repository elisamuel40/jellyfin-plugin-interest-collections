using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.InterestCollections.Services;

/// <summary>
/// Reads the external identifiers Jellyfin already holds for an item.
/// </summary>
/// <remarks>
/// Provider ids are exact; titles are not. "The Office", "Fargo" and "The Killing" all name more
/// than one production, and year-based disambiguation still fails on remakes released in the same
/// year. This plugin therefore matches on ids only, and simply skips items that have none rather
/// than guessing and tagging the wrong title.
/// </remarks>
public static class ProviderIdResolver
{
    /// <summary>
    /// Reads the IMDb identifier, rejecting anything that is not a well-formed <c>tt</c> id.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>The IMDb id, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public static string? GetImdbId(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var value = Clean(item.GetProviderId(MetadataProvider.Imdb));

        return value is not null && IsWellFormedImdbId(value) ? value : null;
    }

    /// <summary>
    /// Reads the TMDb identifier.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>The TMDb id, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public static string? GetTmdbId(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Clean(item.GetProviderId(MetadataProvider.Tmdb));
    }

    /// <summary>
    /// Reads the TVDB identifier.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>The TVDB id, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public static string? GetTvdbId(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Clean(item.GetProviderId(MetadataProvider.Tvdb));
    }

    /// <summary>
    /// Checks the shape of an IMDb id: <c>tt</c> followed by digits.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><see langword="true"/> when the value looks like an IMDb id.</returns>
    public static bool IsWellFormedImdbId(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 3)
        {
            return false;
        }

        if (!value.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var index = 2; index < value.Length; index++)
        {
            if (!char.IsAsciiDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
