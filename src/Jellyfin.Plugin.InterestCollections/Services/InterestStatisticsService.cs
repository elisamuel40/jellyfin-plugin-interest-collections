using System;
using System.Collections.Generic;
using Jellyfin.Plugin.InterestCollections.Configuration;
using Jellyfin.Plugin.InterestCollections.Models;
using Jellyfin.Plugin.InterestCollections.Storage;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.InterestCollections.Services;

/// <summary>
/// Summarises what the plugin has applied across the library, for the Interest Manager page.
/// </summary>
/// <remarks>
/// Counts come from the plugin's own per-item records rather than from a library query, so opening
/// the page costs no provider requests and no library scan.
/// </remarks>
public sealed class InterestStatisticsService
{
    private readonly ProcessedItemStore _processedItems;
    private readonly ManagedCollectionStore _managedCollections;
    private readonly InterestTaxonomy _taxonomy;
    private readonly ILibraryManager _libraryManager;
    private readonly Func<PluginConfiguration> _configurationAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="InterestStatisticsService"/> class.
    /// </summary>
    /// <param name="processedItems">The per-item state store.</param>
    /// <param name="managedCollections">The managed collection store.</param>
    /// <param name="taxonomy">The interest taxonomy.</param>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="configurationAccessor">Reads the current plugin configuration.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public InterestStatisticsService(
        ProcessedItemStore processedItems,
        ManagedCollectionStore managedCollections,
        InterestTaxonomy taxonomy,
        ILibraryManager libraryManager,
        Func<PluginConfiguration> configurationAccessor)
    {
        ArgumentNullException.ThrowIfNull(processedItems);
        ArgumentNullException.ThrowIfNull(managedCollections);
        ArgumentNullException.ThrowIfNull(taxonomy);
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(configurationAccessor);

        _processedItems = processedItems;
        _managedCollections = managedCollections;
        _taxonomy = taxonomy;
        _libraryManager = libraryManager;
        _configurationAccessor = configurationAccessor;
    }

    /// <summary>
    /// Builds one row per interest currently applied to the library, ordered by how many titles
    /// carry it.
    /// </summary>
    /// <returns>The summaries.</returns>
    public IReadOnlyList<InterestSummary> GetSummaries()
    {
        var configuration = _configurationAccessor();
        var disabled = ConfigurationText.ToMatchKeySet(configuration.DisabledInterests);
        var minimum = Math.Max(1, configuration.MinimumTitlesPerCollection);
        var counts = CountByInterest();

        var summaries = new List<InterestSummary>(counts.Count);

        foreach (var (key, entry) in counts)
        {
            var isDisabled = disabled.Contains(InterestNormalizer.MatchKey(entry.Name));
            var hasCollection = _managedCollections.GetBoxSetId(key) is not null;

            summaries.Add(new InterestSummary
            {
                Key = key,
                Name = entry.Name,
                Category = entry.Category ?? string.Empty,
                TitleCount = entry.Count,
                HasCollection = hasCollection,
                Status = isDisabled ? "Disabled"
                    : entry.Count < minimum ? "Below minimum"
                    : hasCollection ? "Collection"
                    : "Enabled",
            });
        }

        summaries.Sort((left, right) =>
        {
            var byCount = right.TitleCount.CompareTo(left.TitleCount);
            return byCount != 0 ? byCount : string.CompareOrdinal(left.Name, right.Name);
        });

        return summaries;
    }

    /// <summary>
    /// Lists the titles carrying one interest.
    /// </summary>
    /// <param name="interestKey">The interest key.</param>
    /// <param name="limit">The maximum number of titles to return.</param>
    /// <returns>The title names.</returns>
    public IReadOnlyList<string> GetTitles(string interestKey, int limit = 200)
    {
        var definition = _taxonomy.Resolve(interestKey, null);
        if (definition is null)
        {
            return [];
        }

        var wanted = InterestNormalizer.MatchKey(definition.Name);
        var titles = new List<string>();

        foreach (var (rawId, record) in EnumerateRecords())
        {
            if (titles.Count >= limit)
            {
                break;
            }

            foreach (var tag in record.AppliedTags)
            {
                if (!string.Equals(InterestNormalizer.MatchKey(tag), wanted, StringComparison.Ordinal))
                {
                    continue;
                }

                if (Guid.TryParse(rawId, out var itemId))
                {
                    var item = _libraryManager.GetItemById(itemId);
                    if (item?.Name is { Length: > 0 } name)
                    {
                        titles.Add(name);
                    }
                }

                break;
            }
        }

        titles.Sort(StringComparer.OrdinalIgnoreCase);
        return titles;
    }

    private Dictionary<string, InterestTally> CountByInterest()
    {
        var counts = new Dictionary<string, InterestTally>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, record) in EnumerateRecords())
        {
            foreach (var tag in record.AppliedTags)
            {
                var resolved = _taxonomy.Resolve(null, tag);
                if (resolved is null)
                {
                    continue;
                }

                if (counts.TryGetValue(resolved.Key, out var tally))
                {
                    tally.Count++;
                    continue;
                }

                counts[resolved.Key] = new InterestTally
                {
                    Name = resolved.Name,
                    Category = resolved.Category,
                    Count = 1,
                };
            }
        }

        return counts;
    }

    private IEnumerable<KeyValuePair<string, ProcessedItemRecord>> EnumerateRecords()
        => _processedItems.Snapshot();

    private sealed class InterestTally
    {
        public required string Name { get; init; }

        public required string? Category { get; init; }

        public required int Count { get; set; }
    }
}
