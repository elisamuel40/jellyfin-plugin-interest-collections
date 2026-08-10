using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.InterestCollections.Storage;

/// <summary>
/// The processed-items file contents.
/// </summary>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class ProcessedItemDocument
{
    /// <summary>
    /// Gets the records, keyed by Jellyfin item id.
    /// </summary>
    public Dictionary<string, ProcessedItemRecord> Items { get; }
        = StoreKeys.NewDictionary<ProcessedItemRecord>();
}
