using System.Collections.Generic;

namespace Jellyfin.Plugin.InterestCollections.Models;

/// <summary>
/// The outcome of one processing run, shown on the configuration page and written to the log.
/// </summary>
public sealed class RunStatistics
{
    /// <summary>
    /// Gets or sets the number of eligible items found.
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Gets or sets the number of items actually looked at this run.
    /// </summary>
    public int ProcessedItems { get; set; }

    /// <summary>
    /// Gets or sets the number of items skipped for carrying no usable provider id.
    /// </summary>
    public int SkippedWithoutProviderId { get; set; }

    /// <summary>
    /// Gets or sets the number of answers served from the cache.
    /// </summary>
    public int CacheHits { get; set; }

    /// <summary>
    /// Gets or sets the number of requests actually sent to the provider.
    /// </summary>
    public int ProviderRequests { get; set; }

    /// <summary>
    /// Gets or sets the number of items whose lookup failed. Their existing tags were left alone.
    /// </summary>
    public int Errors { get; set; }

    /// <summary>
    /// Gets or sets the number of distinct interests discovered across the library.
    /// </summary>
    public int InterestsDiscovered { get; set; }

    /// <summary>
    /// Gets or sets the number of interests that reached the minimum title count.
    /// </summary>
    public int InterestsQualifying { get; set; }

    /// <summary>
    /// Gets or sets the number of items whose tags changed.
    /// </summary>
    public int ItemsTagged { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the run was a dry run.
    /// </summary>
    public bool WasDryRun { get; set; }

    /// <summary>
    /// Gets or sets when the run finished, as an ISO-8601 string for the configuration page.
    /// </summary>
    public string CompletedAt { get; set; } = string.Empty;

    /// <summary>
    /// Gets the collection changes made, or that a dry run would have made.
    /// </summary>
    public CollectionChange Collections { get; init; } = new();

    /// <summary>
    /// Gets a sample of the tag changes, capped so a dry run over a large library stays readable.
    /// </summary>
    public IList<string> SampleTagChanges { get; } = [];
}
