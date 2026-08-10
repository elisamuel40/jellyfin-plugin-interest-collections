using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.InterestCollections.Configuration;
using Jellyfin.Plugin.InterestCollections.Models;
using Jellyfin.Plugin.InterestCollections.Providers;
using Jellyfin.Plugin.InterestCollections.Storage;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections.Services;

/// <summary>
/// Runs the whole pipeline: enumerate, resolve ids, look up interests, normalise, filter, write
/// tags, then reconcile collections.
/// </summary>
/// <remarks>
/// The library is walked exactly once per run. Interests are accumulated into an inverted index as
/// items are processed, so collection membership never needs a second pass, and a library of tens
/// of thousands of items stays linear.
/// </remarks>
public sealed class InterestProcessingService : IDisposable
{
    /// <summary>
    /// How many individual tag changes a dry-run report lists before it stops collecting samples.
    /// </summary>
    private const int MaxSampleTagChanges = 200;

    private readonly MediaScannerService _scanner;
    private readonly InterestProviderFactory _providerFactory;
    private readonly InterestFilter _filter;
    private readonly TagSynchronizationService _tagSynchronizer;
    private readonly CollectionSynchronizationService _collectionSynchronizer;
    private readonly InterestCache _cache;
    private readonly ProcessedItemStore _processedItems;
    private readonly Func<PluginConfiguration> _configurationAccessor;
    private readonly ILogger<InterestProcessingService> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="InterestProcessingService"/> class.
    /// </summary>
    /// <param name="scanner">Enumerates eligible library items.</param>
    /// <param name="providerFactory">Resolves the configured interest provider.</param>
    /// <param name="filter">Applies the filtering rules.</param>
    /// <param name="tagSynchronizer">Writes tags.</param>
    /// <param name="collectionSynchronizer">Maintains collections.</param>
    /// <param name="cache">Caches provider answers.</param>
    /// <param name="processedItems">Tracks per-item state.</param>
    /// <param name="configurationAccessor">Reads the current plugin configuration.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public InterestProcessingService(
        MediaScannerService scanner,
        InterestProviderFactory providerFactory,
        InterestFilter filter,
        TagSynchronizationService tagSynchronizer,
        CollectionSynchronizationService collectionSynchronizer,
        InterestCache cache,
        ProcessedItemStore processedItems,
        Func<PluginConfiguration> configurationAccessor,
        ILogger<InterestProcessingService> logger)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(providerFactory);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(tagSynchronizer);
        ArgumentNullException.ThrowIfNull(collectionSynchronizer);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(processedItems);
        ArgumentNullException.ThrowIfNull(configurationAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        _scanner = scanner;
        _providerFactory = providerFactory;
        _filter = filter;
        _tagSynchronizer = tagSynchronizer;
        _collectionSynchronizer = collectionSynchronizer;
        _cache = cache;
        _processedItems = processedItems;
        _configurationAccessor = configurationAccessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Dispose() => _runLock.Dispose();

    /// <summary>
    /// Processes the library.
    /// </summary>
    /// <param name="progress">Receives progress from 0 to 100.</param>
    /// <param name="options">What this run should do.</param>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    /// <returns>The run statistics.</returns>
    public async Task<RunStatistics> RunAsync(
        IProgress<double>? progress,
        RunOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Runs are serialised: a scheduled task and a library event firing together must not
        // process the same item twice and race each other's writes.
        await _runLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await RunCoreAsync(progress, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _runLock.Release();
        }
    }

    /// <summary>
    /// Processes a specific set of items, used when the library reports additions or updates.
    /// </summary>
    /// <param name="items">The items to process.</param>
    /// <param name="cancellationToken">Token used to cancel the work.</param>
    /// <returns>The run statistics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public async Task<RunStatistics> RunForItemsAsync(
        IReadOnlyList<BaseItem> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        await _runLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var configuration = _configurationAccessor();
            var statistics = new RunStatistics
            {
                TotalItems = items.Count,
                WasDryRun = configuration.DryRun,
            };

            var groups = new Dictionary<string, InterestGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessItemAsync(item, groups, statistics, configuration, false, cancellationToken)
                    .ConfigureAwait(false);
            }

            Finish(statistics, groups);
            return statistics;
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<RunStatistics> RunCoreAsync(
        IProgress<double>? progress,
        RunOptions options,
        CancellationToken cancellationToken)
    {
        var configuration = _configurationAccessor();
        var dryRun = configuration.DryRun || options.ForceDryRun;
        var items = _scanner.GetEligibleItems();

        var statistics = new RunStatistics
        {
            TotalItems = items.Count,
            WasDryRun = dryRun,
        };

        var groups = new Dictionary<string, InterestGroup>(StringComparer.OrdinalIgnoreCase);
        var liveItemIds = new HashSet<Guid>();

        _logger.LogInformation(
            "Processing {Count} items using {Provider}{DryRun}",
            items.Count,
            _providerFactory.GetCurrent().Name,
            dryRun ? " (dry run)" : string.Empty);

        for (var index = 0; index < items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = items[index];
            liveItemIds.Add(item.Id);

            await ProcessItemAsync(item, groups, statistics, configuration, dryRun, cancellationToken)
                .ConfigureAwait(false);

            // Collections are reconciled after the walk, so the item loop owns the first 90%.
            progress?.Report((index + 1) * 90d / Math.Max(1, items.Count));
        }

        if (options.PruneMissingItems)
        {
            _processedItems.RemoveMissing(liveItemIds);
        }

        if (configuration.ManageCollections)
        {
            var change = await _collectionSynchronizer
                .SynchronizeAsync(groups.Values, dryRun, cancellationToken)
                .ConfigureAwait(false);

            CopyCollectionChange(change, statistics);
        }

        progress?.Report(100);
        Finish(statistics, groups);

        _logger.LogInformation(
            "Finished: {Processed}/{Total} items, {Interests} interests, {Qualifying} qualifying, "
            + "{Requests} provider requests, {Hits} cache hits, {Errors} errors",
            statistics.ProcessedItems,
            statistics.TotalItems,
            statistics.InterestsDiscovered,
            statistics.InterestsQualifying,
            statistics.ProviderRequests,
            statistics.CacheHits,
            statistics.Errors);

        return statistics;
    }

    /// <summary>
    /// Runs the pipeline for a single item and folds its interests into the inverted index.
    /// </summary>
    private async Task ProcessItemAsync(
        BaseItem item,
        Dictionary<string, InterestGroup> groups,
        RunStatistics statistics,
        PluginConfiguration configuration,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var identity = MediaScannerService.ToIdentity(item);

        if (!identity.HasProviderId)
        {
            statistics.SkippedWithoutProviderId++;
            return;
        }

        var lookup = await LookupAsync(identity, statistics, configuration, cancellationToken)
            .ConfigureAwait(false);

        if (lookup is null)
        {
            // The provider could not answer. Keep whatever the plugin previously applied so a
            // transient outage never strips an item's interests, and retry on the next run.
            statistics.Errors++;
            _processedItems.MarkFailed(item.Id);
            AddPreviouslyAppliedToGroups(item.Id, groups);
            return;
        }

        statistics.ProcessedItems++;

        var accepted = _filter.Apply(identity, lookup);
        AddToGroups(accepted, item.Id, groups);

        if (!configuration.WriteTags)
        {
            return;
        }

        var change = _tagSynchronizer.Plan(item, accepted);

        if (change.HasChanges)
        {
            statistics.ItemsTagged++;

            if (statistics.SampleTagChanges.Count < MaxSampleTagChanges)
            {
                statistics.SampleTagChanges.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: +[{1}] -[{2}]",
                    change.ItemName,
                    string.Join(", ", change.Added),
                    string.Join(", ", change.Removed)));
            }
        }

        if (!dryRun)
        {
            await _tagSynchronizer
                .ApplyAsync(item, change, configuration.GetProcessingFingerprint(), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns the interests for an item, from the cache when possible.
    /// </summary>
    /// <returns>The interests, or null when the provider could not answer.</returns>
    private async Task<IReadOnlyList<InterestRef>?> LookupAsync(
        MediaIdentity identity,
        RunStatistics statistics,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var provider = _providerFactory.GetCurrent();
        var cacheKey = identity.GetCacheKey(provider.Id);
        var lifetime = TimeSpan.FromDays(Math.Max(1, configuration.CacheExpirationDays));
        var negativeLifetime = TimeSpan.FromDays(Math.Max(1, configuration.NegativeCacheExpirationDays));

        if (_cache.TryGet(cacheKey, provider.ResultVersion, lifetime, negativeLifetime, out var cached))
        {
            statistics.CacheHits++;
            return cached;
        }

        statistics.ProviderRequests++;
        var result = await provider.GetInterestsAsync(identity, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "Lookup failed for {Title}: {Reason}",
                identity.Name,
                result.FailureReason);
            return null;
        }

        _cache.Set(cacheKey, provider.Id, provider.ResultVersion, result.Interests);
        return result.Interests;
    }

    /// <summary>
    /// Folds an item's accepted interests into the inverted index.
    /// </summary>
    private static void AddToGroups(
        IReadOnlyList<InterestRef> interests,
        Guid itemId,
        Dictionary<string, InterestGroup> groups)
    {
        foreach (var interest in interests)
        {
            if (!groups.TryGetValue(interest.Key, out var group))
            {
                group = new InterestGroup { Interest = interest };
                groups[interest.Key] = group;
            }

            group.ItemIds.Add(itemId);
        }
    }

    /// <summary>
    /// Keeps an item in the collections it already belongs to when its lookup failed, using the
    /// tags the plugin previously applied.
    /// </summary>
    private void AddPreviouslyAppliedToGroups(Guid itemId, Dictionary<string, InterestGroup> groups)
    {
        var record = _processedItems.Get(itemId);
        if (record is null)
        {
            return;
        }

        foreach (var tag in record.AppliedTags)
        {
            var resolved = InterestTaxonomy.Shared.Resolve(null, tag);
            if (resolved is null)
            {
                continue;
            }

            if (!groups.TryGetValue(resolved.Key, out var group))
            {
                group = new InterestGroup { Interest = resolved };
                groups[resolved.Key] = group;
            }

            group.ItemIds.Add(itemId);
        }
    }

    private void Finish(RunStatistics statistics, Dictionary<string, InterestGroup> groups)
    {
        var configuration = _configurationAccessor();
        var minimum = Math.Max(1, configuration.MinimumTitlesPerCollection);

        statistics.InterestsDiscovered = groups.Count;
        statistics.CompletedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        var qualifying = 0;
        foreach (var group in groups.Values)
        {
            if (group.ItemIds.Count >= minimum)
            {
                qualifying++;
            }
        }

        statistics.InterestsQualifying = qualifying;
    }

    private static void CopyCollectionChange(CollectionChange source, RunStatistics statistics)
    {
        Copy(source.Created, statistics.Collections.Created);
        Copy(source.Deleted, statistics.Collections.Deleted);
        Copy(source.MembersAdded, statistics.Collections.MembersAdded);
        Copy(source.MembersRemoved, statistics.Collections.MembersRemoved);
        Copy(source.BelowMinimum, statistics.Collections.BelowMinimum);

        static void Copy(IList<string> from, IList<string> to)
        {
            foreach (var value in from)
            {
                to.Add(value);
            }
        }
    }
}
