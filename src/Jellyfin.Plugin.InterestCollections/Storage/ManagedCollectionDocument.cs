using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.InterestCollections.Storage;

/// <summary>
/// The managed-collections file contents.
/// </summary>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class ManagedCollectionDocument
{
    /// <summary>
    /// Gets the records, keyed by interest key.
    /// </summary>
    public Dictionary<string, ManagedCollectionRecord> Collections { get; }
        = StoreKeys.NewDictionary<ManagedCollectionRecord>();
}
