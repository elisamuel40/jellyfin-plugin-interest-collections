using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.InterestCollections.Configuration;
using Jellyfin.Plugin.InterestCollections.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections.Services;

/// <summary>
/// Enumerates the library items the plugin is configured to classify.
/// </summary>
public sealed class MediaScannerService
{
    private readonly ILibraryManager _libraryManager;
    private readonly Func<PluginConfiguration> _configurationAccessor;
    private readonly ILogger<MediaScannerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaScannerService"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="configurationAccessor">Reads the current plugin configuration.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public MediaScannerService(
        ILibraryManager libraryManager,
        Func<PluginConfiguration> configurationAccessor,
        ILogger<MediaScannerService> logger)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(configurationAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        _libraryManager = libraryManager;
        _configurationAccessor = configurationAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Returns every item eligible for classification, in one library pass.
    /// </summary>
    /// <returns>The eligible items.</returns>
    public IReadOnlyList<BaseItem> GetEligibleItems()
    {
        var configuration = _configurationAccessor();
        var kinds = new List<BaseItemKind>(3);

        if (configuration.ProcessMovies)
        {
            kinds.Add(BaseItemKind.Movie);
        }

        if (configuration.ProcessSeries)
        {
            kinds.Add(BaseItemKind.Series);
        }

        if (configuration.ProcessEpisodes)
        {
            kinds.Add(BaseItemKind.Episode);
        }

        if (kinds.Count == 0)
        {
            _logger.LogInformation("No media types are enabled, so there is nothing to process");
            return [];
        }

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = kinds.ToArray(),
            IsVirtualItem = false,
            Recursive = true,
        });

        var allowedLibraries = ParseLibraryFilter(configuration);
        if (allowedLibraries.Count == 0)
        {
            return items;
        }

        var filtered = new List<BaseItem>(items.Count);
        foreach (var item in items)
        {
            if (IsInAllowedLibrary(item, allowedLibraries))
            {
                filtered.Add(item);
            }
        }

        return filtered;
    }

    /// <summary>
    /// Converts a library item into the provider-facing identity.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>The identity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public static MediaIdentity ToIdentity(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new MediaIdentity
        {
            ItemId = item.Id,
            Name = item.Name ?? string.Empty,
            Kind = ToKind(item),
            ProductionYear = item.ProductionYear,
            ImdbId = ProviderIdResolver.GetImdbId(item),
            TmdbId = ProviderIdResolver.GetTmdbId(item),
            TvdbId = ProviderIdResolver.GetTvdbId(item),
            Genres = item.Genres ?? [],
            Tags = item.Tags ?? [],
        };
    }

    /// <summary>
    /// Maps a Jellyfin item onto the plugin's media kind.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>The kind.</returns>
    private static MediaKind ToKind(BaseItem item) => item.GetBaseItemKind() switch
    {
        BaseItemKind.Movie => MediaKind.Movie,
        BaseItemKind.Episode => MediaKind.Episode,
        _ => MediaKind.Series,
    };

    /// <summary>
    /// Reads the configured library filter.
    /// </summary>
    /// <param name="configuration">The plugin configuration.</param>
    /// <returns>The allowed library ids, empty when every library is allowed.</returns>
    private static HashSet<Guid> ParseLibraryFilter(PluginConfiguration configuration)
    {
        var allowed = new HashSet<Guid>();

        foreach (var line in ConfigurationText.ToLines(configuration.IncludedLibraries))
        {
            if (Guid.TryParse(line, out var id))
            {
                allowed.Add(id);
            }
        }

        return allowed;
    }

    /// <summary>
    /// Determines whether an item sits under one of the allowed libraries.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <param name="allowedLibraries">The allowed library ids.</param>
    /// <returns><see langword="true"/> when the item is in scope.</returns>
    private bool IsInAllowedLibrary(BaseItem item, HashSet<Guid> allowedLibraries)
    {
        foreach (var folder in _libraryManager.GetCollectionFolders(item))
        {
            if (allowedLibraries.Contains(folder.Id))
            {
                return true;
            }
        }

        return false;
    }
}
