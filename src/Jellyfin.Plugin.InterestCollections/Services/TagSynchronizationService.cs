using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.InterestCollections.Configuration;
using Jellyfin.Plugin.InterestCollections.Models;
using Jellyfin.Plugin.InterestCollections.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections.Services;

/// <summary>
/// Writes accepted interests to an item's Jellyfin tags.
/// </summary>
/// <remarks>
/// The plugin only ever removes tags it previously wrote, which are recorded per item in
/// <see cref="ProcessedItemStore"/>. Tags added by the user, by another plugin, or by a metadata
/// provider are copied through untouched. There is no visible tag prefix: the whole point is that
/// the tags read naturally in the Jellyfin UI.
/// </remarks>
public sealed class TagSynchronizationService
{
    private readonly ProcessedItemStore _processedItems;
    private readonly Func<PluginConfiguration> _configurationAccessor;
    private readonly ILogger<TagSynchronizationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TagSynchronizationService"/> class.
    /// </summary>
    /// <param name="processedItems">The store recording which tags the plugin owns.</param>
    /// <param name="configurationAccessor">Reads the current plugin configuration.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public TagSynchronizationService(
        ProcessedItemStore processedItems,
        Func<PluginConfiguration> configurationAccessor,
        ILogger<TagSynchronizationService> logger)
    {
        ArgumentNullException.ThrowIfNull(processedItems);
        ArgumentNullException.ThrowIfNull(configurationAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        _processedItems = processedItems;
        _configurationAccessor = configurationAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Computes the tag change for an item without applying it.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <param name="interests">The interests that survived filtering.</param>
    /// <returns>The change that would be applied.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public TagChange Plan(BaseItem item, IReadOnlyList<InterestRef> interests)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(interests);

        var desired = new List<string>(interests.Count);
        var desiredSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var interest in interests)
        {
            if (desiredSet.Add(interest.Name))
            {
                desired.Add(interest.Name);
            }
        }

        var currentTags = item.Tags ?? [];
        var ownedPreviously = _processedItems.Get(item.Id)?.AppliedTags ?? [];
        var ownedSet = new HashSet<string>(ownedPreviously, StringComparer.OrdinalIgnoreCase);

        var finalTags = new List<string>(currentTags.Length + desired.Count);
        var finalSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();

        foreach (var tag in currentTags)
        {
            // A tag the plugin owns but no longer wants is dropped; anything else is preserved,
            // including a tag the user happens to have written by hand that matches an interest.
            if (ownedSet.Contains(tag) && !desiredSet.Contains(tag))
            {
                removed.Add(tag);
                continue;
            }

            if (finalSet.Add(tag))
            {
                finalTags.Add(tag);
            }
        }

        var added = new List<string>();
        foreach (var tag in desired)
        {
            if (finalSet.Add(tag))
            {
                finalTags.Add(tag);
                added.Add(tag);
            }
        }

        return new TagChange
        {
            ItemId = item.Id,
            ItemName = item.Name ?? string.Empty,
            Added = added,
            Removed = removed,
            FinalTags = finalTags,
            OwnedTags = desired,
        };
    }

    /// <summary>
    /// Applies a planned change to the item and records the new ownership.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <param name="change">The change produced by <see cref="Plan"/>.</param>
    /// <param name="fingerprint">The settings fingerprint in force.</param>
    /// <param name="cancellationToken">Token used to cancel the write.</param>
    /// <returns>A task that completes once the item has been saved.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public async Task ApplyAsync(
        BaseItem item,
        TagChange change,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(change);

        var configuration = _configurationAccessor();

        // Ownership is recorded before the write, not after. UpdateToRepositoryAsync raises
        // ItemUpdated while it runs, and the library-event handler decides whether to queue an
        // item by reading exactly this record. Recording afterwards leaves a window in which the
        // plugin's own write looks like an external change — which on a 227-title library queued
        // 213 items for a pointless second pass.
        _processedItems.MarkProcessed(item.Id, change.OwnedTags, fingerprint);

        if (!change.HasChanges)
        {
            return;
        }

        try
        {
            item.Tags = [.. change.FinalTags];

            if (configuration.LockTagsField)
            {
                LockTagsField(item);
            }

            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug(
                "{Item}: +{Added} tags, -{Removed} tags",
                change.ItemName,
                change.Added.Count,
                change.Removed.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The optimistic record above must not outlive a failed write, or the item would
            // never be retried.
            _processedItems.MarkFailed(item.Id);
            _logger.LogWarning(ex, "Could not save tags for {Item}; it will be retried", change.ItemName);
        }
    }

    /// <summary>
    /// Adds the Tags field to the item's locked fields, so a later metadata refresh cannot discard
    /// the interests. Other locked fields are preserved.
    /// </summary>
    /// <param name="item">The library item.</param>
    private static void LockTagsField(BaseItem item)
    {
        var locked = item.LockedFields ?? [];

        if (Array.IndexOf(locked, MetadataField.Tags) >= 0)
        {
            return;
        }

        item.LockedFields = [.. locked.Append(MetadataField.Tags)];
    }
}
