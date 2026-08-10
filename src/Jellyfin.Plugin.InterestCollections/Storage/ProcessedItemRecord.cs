using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.InterestCollections.Storage;

/// <summary>
/// What the plugin did to one library item the last time it processed it.
/// </summary>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class ProcessedItemRecord
{
    /// <summary>
    /// Gets the tags this plugin wrote. Only these may ever be removed again; every other tag on
    /// the item belongs to someone else.
    /// </summary>
    public IList<string> AppliedTags { get; } = [];

    /// <summary>
    /// Gets or sets the fingerprint of the settings in force when the item was processed. A
    /// different fingerprint means the outcome could change, so the item is reprocessed.
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the item was last processed successfully.
    /// </summary>
    public DateTimeOffset ProcessedAt { get; set; }

    /// <summary>
    /// Gets or sets when the last provider failure for this item happened, so a title whose lookup
    /// failed is retried on the next run instead of being treated as done.
    /// </summary>
    public DateTimeOffset? LastFailureAt { get; set; }
}
