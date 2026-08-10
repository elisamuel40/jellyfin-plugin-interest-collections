using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.InterestCollections.Models;

/// <summary>
/// Every title in the library that carries one interest. This is the inverted index the collection
/// synchronizer works from, built once per run so membership never costs a second library pass.
/// </summary>
public sealed class InterestGroup
{
    /// <summary>
    /// Gets the interest the group is for.
    /// </summary>
    public required InterestRef Interest { get; init; }

    /// <summary>
    /// Gets the ids of the items carrying the interest.
    /// </summary>
    public IList<Guid> ItemIds { get; } = [];
}
