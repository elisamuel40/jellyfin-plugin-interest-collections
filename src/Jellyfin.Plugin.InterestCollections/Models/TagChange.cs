using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.InterestCollections.Models;

/// <summary>
/// The tag change computed for one item: what would be added, what would be removed, and the exact
/// tag set that would result. Dry-run mode reports these without applying them.
/// </summary>
public sealed class TagChange
{
    /// <summary>
    /// Gets the Jellyfin item id.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets the item name, for reporting.
    /// </summary>
    public required string ItemName { get; init; }

    /// <summary>
    /// Gets the tags that would be added.
    /// </summary>
    public required IReadOnlyList<string> Added { get; init; }

    /// <summary>
    /// Gets the tags that would be removed. Only tags the plugin previously wrote appear here.
    /// </summary>
    public required IReadOnlyList<string> Removed { get; init; }

    /// <summary>
    /// Gets the complete tag set the item would end up with, including tags the plugin does not own.
    /// </summary>
    public required IReadOnlyList<string> FinalTags { get; init; }

    /// <summary>
    /// Gets the tags the plugin claims ownership of after this change.
    /// </summary>
    public required IReadOnlyList<string> OwnedTags { get; init; }

    /// <summary>
    /// Gets a value indicating whether anything would actually change.
    /// </summary>
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
}
