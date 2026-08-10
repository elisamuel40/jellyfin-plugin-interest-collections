using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.InterestCollections.Configuration;

/// <summary>
/// Plugin settings, persisted by Jellyfin as XML. Only XML-serializable member types are used,
/// so multi-valued settings are stored as newline-delimited text rather than dictionaries.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the metadata source used to look up interests.
    /// </summary>
    public InterestProviderKind Provider { get; set; } = InterestProviderKind.ImdbGraphQl;

    /// <summary>
    /// Gets or sets the API key for providers that require one. Never written to the log.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an override for the provider endpoint. Empty means "use the built-in default".
    /// </summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether movies are processed.
    /// </summary>
    public bool ProcessMovies { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether series are processed.
    /// </summary>
    public bool ProcessSeries { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether individual episodes are processed. Off by default:
    /// per-episode interests add a large amount of metadata noise for very little browsing value.
    /// </summary>
    public bool ProcessEpisodes { get; set; }

    /// <summary>
    /// Gets or sets the library folder identifiers to restrict processing to, one GUID per line.
    /// Empty means every library is processed.
    /// </summary>
    public string IncludedLibraries { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether accepted interests are written to Jellyfin tags.
    /// </summary>
    public bool WriteTags { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the Tags metadata field is locked after writing,
    /// which stops a later metadata refresh from discarding the interests. Off by default because
    /// it also blocks manual tag edits from other sources.
    /// </summary>
    public bool LockTagsField { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether collections are created and maintained.
    /// </summary>
    public bool ManageCollections { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum number of titles an interest needs before it earns a collection.
    /// </summary>
    public int MinimumTitlesPerCollection { get; set; } = 3;

    /// <summary>
    /// Gets or sets a value indicating whether managed collections that fall below the minimum are
    /// deleted. Off by default: deleting is destructive and the threshold can be changed at will.
    /// </summary>
    public bool RemoveCollectionsBelowMinimum { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether newly qualifying titles join existing collections.
    /// </summary>
    public bool AddNewTitlesToCollections { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional prefix applied to the names of managed collections.
    /// </summary>
    public string CollectionNamePrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the interest category names that are excluded, one per line.
    /// </summary>
    public string ExcludedCategories { get; set; } = "Language";

    /// <summary>
    /// Gets or sets a value indicating whether genre-level interests (Drama, Crime, Thriller and
    /// the other names shared with their category) are dropped. On by default because Jellyfin
    /// already exposes those as genres.
    /// </summary>
    public bool ExcludeGenreLevelInterests { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether franchise interests are eligible for collections.
    /// Franchise interests are always eligible as tags when the Franchise category is enabled.
    /// </summary>
    public bool AllowFranchiseCollections { get; set; } = true;

    /// <summary>
    /// Gets or sets interests that are never applied, one per line. Matching is case-insensitive.
    /// </summary>
    public string IgnoredInterests { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets regular expressions that reject a matching interest, one per line.
    /// Invalid patterns are reported on the configuration page and ignored at runtime.
    /// </summary>
    public string BlockedPatterns { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets alias rules in "Alias = Canonical" form, one per line.
    /// </summary>
    public string InterestAliases { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether an interest equal to the title's own name is
    /// rejected. IMDb returns franchise interests named after the title itself.
    /// </summary>
    public bool RejectInterestMatchingTitle { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of concurrent provider requests.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 3;

    /// <summary>
    /// Gets or sets the minimum delay between provider requests, in milliseconds.
    /// </summary>
    public int RequestDelayMilliseconds { get; set; } = 250;

    /// <summary>
    /// Gets or sets the per-request HTTP timeout, in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets how many times a failed request is retried before giving up.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets how long a successful provider response stays valid, in days.
    /// </summary>
    public int CacheExpirationDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets how long a "nothing found" result stays valid, in days. Kept short so that
    /// titles which gain interests later are picked up again reasonably soon.
    /// </summary>
    public int NegativeCacheExpirationDays { get; set; } = 3;

    /// <summary>
    /// Gets or sets a value indicating whether processing runs in dry-run mode, computing every
    /// change and writing nothing to Jellyfin.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether newly added or updated items are processed
    /// automatically, without waiting for the scheduled task.
    /// </summary>
    public bool ProcessOnLibraryEvents { get; set; } = true;

    /// <summary>
    /// Gets or sets how long library events are batched before processing, in seconds. Debouncing
    /// avoids re-processing an item repeatedly while Jellyfin is still fetching its metadata.
    /// </summary>
    public int EventDebounceSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets interests the administrator disabled by hand, one canonical name per line.
    /// Maintained by the Interest Manager page.
    /// </summary>
    public string DisabledInterests { get; set; } = string.Empty;

    /// <summary>
    /// Returns the settings that affect the outcome of processing, as a stable fingerprint.
    /// Items whose stored fingerprint differs are reprocessed on the next run.
    /// </summary>
    /// <returns>A fingerprint of every outcome-affecting setting.</returns>
    public string GetProcessingFingerprint()
    {
        return string.Join(
            '|',
            (int)Provider,
            WriteTags,
            ExcludeGenreLevelInterests,
            RejectInterestMatchingTitle,
            Normalize(ExcludedCategories),
            Normalize(IgnoredInterests),
            Normalize(BlockedPatterns),
            Normalize(InterestAliases),
            Normalize(DisabledInterests));

        static string Normalize(string value)
            => value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }
}
