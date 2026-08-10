using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections.Storage;

/// <summary>
/// Remembers which tags the plugin owns on each item, so it can revise its own work without ever
/// disturbing tags added by the user or by another plugin.
/// </summary>
public sealed class ProcessedItemStore : IDisposable
{
    private readonly JsonStore<ProcessedItemDocument> _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessedItemStore"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The plugin data folder.</param>
    /// <param name="logger">The logger.</param>
    public ProcessedItemStore(string dataFolderPath, ILogger<ProcessedItemStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);
        ArgumentNullException.ThrowIfNull(logger);

        _store = new JsonStore<ProcessedItemDocument>(
            Path.Combine(dataFolderPath, "processed-items.json"),
            logger);
    }

    /// <summary>
    /// Gets the number of items on record.
    /// </summary>
    public int Count => _store.Read().Items.Count;

    /// <summary>
    /// Reads the record for an item.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <returns>The record, or null when the item has never been processed.</returns>
    public ProcessedItemRecord? Get(Guid itemId)
        => _store.Read().Items.TryGetValue(Key(itemId), out var record) ? record : null;

    /// <summary>
    /// Returns a snapshot of every record, for reporting.
    /// </summary>
    /// <returns>The records, keyed by item id.</returns>
    public IReadOnlyDictionary<string, ProcessedItemRecord> Snapshot()
        => new Dictionary<string, ProcessedItemRecord>(_store.Read().Items, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether an item still needs processing.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="fingerprint">The current settings fingerprint.</param>
    /// <returns><see langword="true"/> when the item is new, stale or previously failed.</returns>
    public bool NeedsProcessing(Guid itemId, string fingerprint)
    {
        var record = Get(itemId);

        return record is null
            || record.LastFailureAt is not null
            || !string.Equals(record.Fingerprint, fingerprint, StringComparison.Ordinal);
    }

    /// <summary>
    /// Records a successful pass over an item.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    /// <param name="appliedTags">The tags the plugin now owns on the item.</param>
    /// <param name="fingerprint">The settings fingerprint in force.</param>
    /// <exception cref="ArgumentNullException"><paramref name="appliedTags"/> is null.</exception>
    public void MarkProcessed(Guid itemId, IReadOnlyList<string> appliedTags, string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(appliedTags);

        var record = new ProcessedItemRecord
        {
            Fingerprint = fingerprint,
            ProcessedAt = DateTimeOffset.UtcNow,
            LastFailureAt = null,
        };

        foreach (var tag in appliedTags)
        {
            record.AppliedTags.Add(tag);
        }

        _store.Update(document => document.Items[Key(itemId)] = record);
    }

    /// <summary>
    /// Records that the provider could not answer for an item, leaving any previously applied tags
    /// on record so they are preserved rather than removed.
    /// </summary>
    /// <param name="itemId">The Jellyfin item id.</param>
    public void MarkFailed(Guid itemId) => _store.Update(document =>
    {
        var key = Key(itemId);

        if (document.Items.TryGetValue(key, out var existing))
        {
            existing.LastFailureAt = DateTimeOffset.UtcNow;
            return;
        }

        document.Items[key] = new ProcessedItemRecord { LastFailureAt = DateTimeOffset.UtcNow };
    });

    /// <summary>
    /// Forgets every item, so the next run reprocesses the whole library.
    /// </summary>
    public void Clear() => _store.Update(document => document.Items.Clear());

    /// <summary>
    /// Drops records for items that no longer exist in the library.
    /// </summary>
    /// <param name="liveItemIds">The ids still present.</param>
    /// <returns>The number of records removed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="liveItemIds"/> is null.</exception>
    public int RemoveMissing(IReadOnlySet<Guid> liveItemIds)
    {
        ArgumentNullException.ThrowIfNull(liveItemIds);

        return _store.Update(document =>
        {
            var stale = new List<string>();

            foreach (var key in document.Items.Keys)
            {
                if (!Guid.TryParse(key, out var id) || !liveItemIds.Contains(id))
                {
                    stale.Add(key);
                }
            }

            foreach (var key in stale)
            {
                document.Items.Remove(key);
            }

            return stale.Count;
        });
    }

    /// <inheritdoc />
    public void Dispose() => _store.Dispose();

    private static string Key(Guid itemId) => itemId.ToString("N", CultureInfo.InvariantCulture);
}
