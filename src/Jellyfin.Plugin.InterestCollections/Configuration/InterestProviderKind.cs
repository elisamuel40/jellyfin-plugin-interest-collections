namespace Jellyfin.Plugin.InterestCollections.Configuration;

/// <summary>
/// Identifies the metadata source used to obtain semantic interests.
/// </summary>
public enum InterestProviderKind
{
    /// <summary>
    /// IMDb's public GraphQL endpoint, the only source of genuine IMDb "Interests".
    /// Requires no API key. Personal, non-commercial use only.
    /// </summary>
    ImdbGraphQl = 0,

    /// <summary>
    /// TMDb keywords, mapped onto the bundled interest taxonomy. Requires a free API key.
    /// </summary>
    TmdbKeywords = 1,

    /// <summary>
    /// Offline derivation from the genres and tags Jellyfin already stores. Never performs
    /// network calls; useful as a fallback and for testing.
    /// </summary>
    LocalRules = 2,
}
