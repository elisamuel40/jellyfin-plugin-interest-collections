using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.InterestCollections.Configuration;
using Jellyfin.Plugin.InterestCollections.Models;
using Jellyfin.Plugin.InterestCollections.Storage;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections.Services;

/// <summary>
/// Creates and maintains one Jellyfin collection per qualifying interest.
/// </summary>
/// <remarks>
/// A title belongs to as many interest collections as it has interests; Jellyfin BoxSets link to
/// items rather than owning them, so nothing is duplicated on disk or in the library.
///
/// Safety rests on two independent ownership signals: the plugin stamps each collection it creates
/// with its own provider id inside Jellyfin, and records the same collection in its state file. A
/// collection is only ever modified or deleted when both agree. A collection the administrator
/// created by hand has neither signal and is therefore untouchable, even if it happens to be named
/// exactly like an interest.
/// </remarks>
public sealed class CollectionSynchronizationService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;
    private readonly ManagedCollectionStore _managedCollections;
    private readonly Func<PluginConfiguration> _configurationAccessor;
    private readonly ILogger<CollectionSynchronizationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionSynchronizationService"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="collectionManager">The Jellyfin collection manager.</param>
    /// <param name="managedCollections">The store recording which collections the plugin owns.</param>
    /// <param name="configurationAccessor">Reads the current plugin configuration.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public CollectionSynchronizationService(
        ILibraryManager libraryManager,
        ICollectionManager collectionManager,
        ManagedCollectionStore managedCollections,
        Func<PluginConfiguration> configurationAccessor,
        ILogger<CollectionSynchronizationService> logger)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(collectionManager);
        ArgumentNullException.ThrowIfNull(managedCollections);
        ArgumentNullException.ThrowIfNull(configurationAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
        _managedCollections = managedCollections;
        _configurationAccessor = configurationAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Brings the collections in line with the interest groups computed for the library.
    /// </summary>
    /// <param name="groups">The inverted index of interest to titles.</param>
    /// <param name="dryRun">When true, computes every change without touching Jellyfin.</param>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    /// <returns>A description of what changed, or would have changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="groups"/> is null.</exception>
    public async Task<CollectionChange> SynchronizeAsync(
        IReadOnlyCollection<InterestGroup> groups,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var configuration = _configurationAccessor();
        var change = new CollectionChange();
        var minimum = Math.Max(1, configuration.MinimumTitlesPerCollection);
        var existing = LoadManagedBoxSets();
        var qualifyingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsEligibleForCollection(group, configuration))
            {
                continue;
            }

            if (group.ItemIds.Count < minimum)
            {
                change.BelowMinimum.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} ({1})",
                    group.Interest.Name,
                    group.ItemIds.Count));
                continue;
            }

            qualifyingKeys.Add(group.Interest.Key);
            await SynchronizeGroupAsync(group, existing, change, dryRun, configuration, cancellationToken)
                .ConfigureAwait(false);
        }

        await RemoveCollectionsBelowMinimumAsync(
            existing, qualifyingKeys, change, dryRun, configuration, cancellationToken).ConfigureAwait(false);

        return change;
    }

    /// <summary>
    /// Builds the collection name, applying the configured prefix.
    /// </summary>
    /// <param name="interestName">The interest name.</param>
    /// <param name="configuration">The plugin configuration.</param>
    /// <returns>The collection name.</returns>
    private static string BuildCollectionName(string interestName, PluginConfiguration configuration)
    {
        var prefix = configuration.CollectionNamePrefix;
        return string.IsNullOrWhiteSpace(prefix) ? interestName : prefix.Trim() + " " + interestName;
    }

    /// <summary>
    /// Decides whether an interest may have a collection at all.
    /// </summary>
    /// <param name="group">The interest group.</param>
    /// <param name="configuration">The plugin configuration.</param>
    /// <returns><see langword="true"/> when a collection is allowed.</returns>
    private static bool IsEligibleForCollection(InterestGroup group, PluginConfiguration configuration)
    {
        if (!configuration.AllowFranchiseCollections
            && string.Equals(group.Interest.Category, "Franchise", StringComparison.OrdinalIgnoreCase))
        {
            // Franchise collections overlap with what the TMDb Box Sets plugin already builds, so
            // administrators can keep the tags without duplicating those collections.
            return false;
        }

        return true;
    }

    /// <summary>
    /// Loads every BoxSet that carries this plugin's ownership stamp and is also on record in the
    /// state file. Anything failing either check is treated as somebody else's collection.
    /// </summary>
    /// <returns>The managed BoxSets, keyed by interest key.</returns>
    private Dictionary<string, BoxSet> LoadManagedBoxSets()
    {
        var managed = new Dictionary<string, BoxSet>(StringComparer.OrdinalIgnoreCase);

        var boxSets = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            IsVirtualItem = false,
            Recursive = true,
        });

        foreach (var item in boxSets)
        {
            if (item is not BoxSet boxSet)
            {
                continue;
            }

            var stamp = boxSet.GetProviderId(Plugin.OwnershipProviderKey);
            if (string.IsNullOrWhiteSpace(stamp))
            {
                continue;
            }

            if (!_managedCollections.IsManaged(boxSet.Id))
            {
                _logger.LogWarning(
                    "Collection {Name} carries the plugin stamp but is not in the plugin's records; leaving it alone",
                    boxSet.Name);
                continue;
            }

            managed[stamp] = boxSet;
        }

        return managed;
    }

    /// <summary>
    /// Creates or updates the collection for one qualifying interest.
    /// </summary>
    /// <param name="group">The interest group.</param>
    /// <param name="existing">The managed BoxSets found in the library.</param>
    /// <param name="change">The change record to append to.</param>
    /// <param name="dryRun">Whether to compute without applying.</param>
    /// <param name="configuration">The plugin configuration.</param>
    /// <param name="cancellationToken">Token used to cancel the work.</param>
    /// <returns>A task that completes when the collection is in sync.</returns>
    private async Task SynchronizeGroupAsync(
        InterestGroup group,
        Dictionary<string, BoxSet> existing,
        CollectionChange change,
        bool dryRun,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var collectionName = BuildCollectionName(group.Interest.Name, configuration);

        if (!existing.TryGetValue(group.Interest.Key, out var boxSet))
        {
            change.Created.Add(collectionName);
            change.MembersAdded.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0}: +{1}",
                collectionName,
                group.ItemIds.Count));

            if (dryRun)
            {
                return;
            }

            var created = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = collectionName,
                ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [Plugin.OwnershipProviderKey] = group.Interest.Key,
                },
                ItemIdList = [.. Select(group.ItemIds, id => id.ToString("N", CultureInfo.InvariantCulture))],
            }).ConfigureAwait(false);

            _managedCollections.Register(group.Interest.Key, group.Interest.Name, collectionName, created.Id);
            _logger.LogInformation(
                "Created collection {Collection} with {Count} titles",
                collectionName,
                group.ItemIds.Count);
            return;
        }

        var current = GetMemberIds(boxSet);
        var desired = new HashSet<Guid>(group.ItemIds);

        var toAdd = new List<Guid>();
        foreach (var id in desired)
        {
            if (!current.Contains(id))
            {
                toAdd.Add(id);
            }
        }

        var toRemove = new List<Guid>();
        foreach (var id in current)
        {
            if (!desired.Contains(id))
            {
                toRemove.Add(id);
            }
        }

        if (toAdd.Count > 0 && configuration.AddNewTitlesToCollections)
        {
            change.MembersAdded.Add(string.Format(
                CultureInfo.InvariantCulture, "{0}: +{1}", collectionName, toAdd.Count));

            if (!dryRun)
            {
                await _collectionManager.AddToCollectionAsync(boxSet.Id, toAdd).ConfigureAwait(false);
            }
        }

        if (toRemove.Count > 0)
        {
            change.MembersRemoved.Add(string.Format(
                CultureInfo.InvariantCulture, "{0}: -{1}", collectionName, toRemove.Count));

            if (!dryRun)
            {
                await _collectionManager.RemoveFromCollectionAsync(boxSet.Id, toRemove).ConfigureAwait(false);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Deletes managed collections whose interest no longer qualifies, when the administrator
    /// enabled that. Collections the plugin does not own are never considered.
    /// </summary>
    /// <param name="existing">The managed BoxSets found in the library.</param>
    /// <param name="qualifyingKeys">The interest keys that still qualify.</param>
    /// <param name="change">The change record to append to.</param>
    /// <param name="dryRun">Whether to compute without applying.</param>
    /// <param name="configuration">The plugin configuration.</param>
    /// <param name="cancellationToken">Token used to cancel the work.</param>
    /// <returns>A task that completes when obsolete collections have been handled.</returns>
    private Task RemoveCollectionsBelowMinimumAsync(
        Dictionary<string, BoxSet> existing,
        HashSet<string> qualifyingKeys,
        CollectionChange change,
        bool dryRun,
        PluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!configuration.RemoveCollectionsBelowMinimum)
        {
            return Task.CompletedTask;
        }

        foreach (var (interestKey, boxSet) in existing)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (qualifyingKeys.Contains(interestKey))
            {
                continue;
            }

            change.Deleted.Add(boxSet.Name ?? interestKey);

            if (dryRun)
            {
                continue;
            }

            // Both ownership checks already passed in LoadManagedBoxSets, so this BoxSet is
            // provably the plugin's own.
            _libraryManager.DeleteItem(boxSet, new DeleteOptions { DeleteFileLocation = true }, true);
            _managedCollections.Unregister(interestKey);

            _logger.LogInformation("Removed managed collection {Collection}", boxSet.Name);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads the current membership of a BoxSet.
    /// </summary>
    /// <param name="boxSet">The BoxSet.</param>
    /// <returns>The ids of the items currently linked.</returns>
    private static HashSet<Guid> GetMemberIds(BoxSet boxSet)
    {
        var ids = new HashSet<Guid>();

        foreach (var child in boxSet.GetLinkedChildren())
        {
            ids.Add(child.Id);
        }

        return ids;
    }

    /// <summary>
    /// Projects a list without pulling in LINQ for one call.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="source">The source list.</param>
    /// <param name="selector">The projection.</param>
    /// <returns>The projected list.</returns>
    private static List<TResult> Select<TSource, TResult>(
        IList<TSource> source,
        Func<TSource, TResult> selector)
    {
        var results = new List<TResult>(source.Count);

        foreach (var item in source)
        {
            results.Add(selector(item));
        }

        return results;
    }
}
