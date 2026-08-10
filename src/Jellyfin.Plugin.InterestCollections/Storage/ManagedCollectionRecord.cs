using System;

namespace Jellyfin.Plugin.InterestCollections.Storage;

/// <summary>
/// A collection this plugin created and is therefore allowed to modify.
/// </summary>
public sealed class ManagedCollectionRecord
{
    /// <summary>
    /// Gets or sets the BoxSet id, as a string so the file stays human-readable.
    /// </summary>
    public string BoxSetId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the interest name the collection was created for.
    /// </summary>
    public string InterestName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the collection name as written to Jellyfin, including any configured prefix.
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the collection was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
