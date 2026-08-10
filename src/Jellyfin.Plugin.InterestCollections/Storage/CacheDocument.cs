using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.InterestCollections.Storage;

/// <summary>
/// The interest cache file's contents.
/// </summary>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class CacheDocument
{
    /// <summary>
    /// Gets the cached entries, keyed by provider and stable media identifier.
    /// </summary>
    public Dictionary<string, CacheEntry> Entries { get; } = StoreKeys.NewDictionary<CacheEntry>();
}
