using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.InterestCollections.Storage;

/// <summary>
/// One cached provider answer.
/// </summary>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class CacheEntry
{
    /// <summary>
    /// Gets or sets the provider that produced the answer.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider's result-shape version at the time of writing. An entry written
    /// by an older shape is discarded rather than misread.
    /// </summary>
    public int ProviderVersion { get; set; }

    /// <summary>
    /// Gets or sets when the answer was obtained.
    /// </summary>
    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>
    /// Gets the interest keys returned.
    /// </summary>
    public IList<string> InterestKeys { get; } = [];

    /// <summary>
    /// Gets the interest names returned, parallel to <see cref="InterestKeys"/>. Names are stored
    /// alongside the keys so interests outside the bundled taxonomy survive a restart.
    /// </summary>
    public IList<string> InterestNames { get; } = [];
}
