using System.Collections.Generic;

namespace Jellyfin.Plugin.InterestCollections.Models;

/// <summary>
/// What the plugin would do, or did, to the collections in one run.
/// </summary>
public sealed class CollectionChange
{
    /// <summary>
    /// Gets the names of collections that would be created.
    /// </summary>
    public IList<string> Created { get; } = [];

    /// <summary>
    /// Gets the names of managed collections that would be deleted for falling below the minimum.
    /// </summary>
    public IList<string> Deleted { get; } = [];

    /// <summary>
    /// Gets a line per collection describing membership additions.
    /// </summary>
    public IList<string> MembersAdded { get; } = [];

    /// <summary>
    /// Gets a line per collection describing membership removals.
    /// </summary>
    public IList<string> MembersRemoved { get; } = [];

    /// <summary>
    /// Gets the names of interests that did not reach the minimum title count.
    /// </summary>
    public IList<string> BelowMinimum { get; } = [];

    /// <summary>
    /// Gets a value indicating whether anything would change.
    /// </summary>
    public bool HasChanges =>
        Created.Count > 0
        || Deleted.Count > 0
        || MembersAdded.Count > 0
        || MembersRemoved.Count > 0;
}
