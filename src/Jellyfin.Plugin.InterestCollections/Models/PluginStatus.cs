namespace Jellyfin.Plugin.InterestCollections.Models;

/// <summary>
/// A snapshot of the plugin's current state, shown at the top of the configuration page.
/// </summary>
public sealed class PluginStatus
{
    /// <summary>
    /// Gets the name of the provider in use.
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Gets a value indicating whether that provider has everything it needs to run.
    /// </summary>
    public required bool ProviderConfigured { get; init; }

    /// <summary>
    /// Gets how many provider answers are currently cached.
    /// </summary>
    public required int CachedAnswers { get; init; }

    /// <summary>
    /// Gets the size of the bundled interest taxonomy.
    /// </summary>
    public required int TaxonomySize { get; init; }
}
