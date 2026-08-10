using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections.Storage;

/// <summary>
/// Tracks which collections belong to the plugin.
/// </summary>
/// <remarks>
/// Ownership is recorded twice, deliberately. Every managed BoxSet is stamped with a provider id
/// in Jellyfin itself, and is also listed here. A collection is only ever modified or deleted when
/// both agree, so neither a lost state file nor a hand-edited collection can lead the plugin to
/// touch something a user created.
/// </remarks>
public sealed class ManagedCollectionStore : IDisposable
{
    private readonly JsonStore<ManagedCollectionDocument> _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagedCollectionStore"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The plugin data folder.</param>
    /// <param name="logger">The logger.</param>
    public ManagedCollectionStore(string dataFolderPath, ILogger<ManagedCollectionStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);
        ArgumentNullException.ThrowIfNull(logger);

        _store = new JsonStore<ManagedCollectionDocument>(
            Path.Combine(dataFolderPath, "managed-collections.json"),
            logger);
    }

    /// <summary>
    /// Gets the number of managed collections on record.
    /// </summary>
    public int Count => _store.Read().Collections.Count;

    /// <summary>
    /// Gets every managed collection, keyed by interest key.
    /// </summary>
    /// <returns>A snapshot of the records.</returns>
    public IReadOnlyDictionary<string, ManagedCollectionRecord> GetAll()
        => new Dictionary<string, ManagedCollectionRecord>(_store.Read().Collections, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the BoxSet id recorded for an interest.
    /// </summary>
    /// <param name="interestKey">The interest key.</param>
    /// <returns>The BoxSet id, or null when the plugin does not manage a collection for it.</returns>
    public Guid? GetBoxSetId(string interestKey)
    {
        if (_store.Read().Collections.TryGetValue(interestKey, out var record)
            && Guid.TryParse(record.BoxSetId, out var id))
        {
            return id;
        }

        return null;
    }

    /// <summary>
    /// Determines whether a BoxSet is on record as belonging to the plugin.
    /// </summary>
    /// <param name="boxSetId">The BoxSet id.</param>
    /// <returns><see langword="true"/> when the plugin recorded creating it.</returns>
    public bool IsManaged(Guid boxSetId)
    {
        var target = boxSetId.ToString("N", CultureInfo.InvariantCulture);

        foreach (var record in _store.Read().Collections.Values)
        {
            if (Guid.TryParse(record.BoxSetId, out var stored)
                && string.Equals(
                    stored.ToString("N", CultureInfo.InvariantCulture),
                    target,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Records a collection the plugin created.
    /// </summary>
    /// <param name="interestKey">The interest key.</param>
    /// <param name="interestName">The interest name.</param>
    /// <param name="collectionName">The collection name written to Jellyfin.</param>
    /// <param name="boxSetId">The created BoxSet id.</param>
    public void Register(string interestKey, string interestName, string collectionName, Guid boxSetId)
        => _store.Update(document => document.Collections[interestKey] = new ManagedCollectionRecord
        {
            BoxSetId = boxSetId.ToString("N", CultureInfo.InvariantCulture),
            InterestName = interestName,
            CollectionName = collectionName,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    /// <summary>
    /// Forgets a collection, after it was deleted or found to be gone.
    /// </summary>
    /// <param name="interestKey">The interest key.</param>
    public void Unregister(string interestKey)
        => _store.Update(document => document.Collections.Remove(interestKey));

    /// <inheritdoc />
    public void Dispose() => _store.Dispose();
}
