namespace Jellyfin.Plugin.InterestCollections.Models;

/// <summary>
/// One row of the Interest Manager: what an interest is, how much of the library carries it, and
/// what the plugin is currently doing with it.
/// </summary>
public sealed class InterestSummary
{
    /// <summary>
    /// Gets the canonical key.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the canonical display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the taxonomy category, or an empty string when the interest is outside the taxonomy.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Gets how many titles currently carry the interest.
    /// </summary>
    public required int TitleCount { get; init; }

    /// <summary>
    /// Gets the status shown in the table: <c>Enabled</c>, <c>Disabled</c>, <c>Below minimum</c>
    /// or <c>Collection</c>.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Gets a value indicating whether a managed collection currently exists for this interest.
    /// </summary>
    public required bool HasCollection { get; init; }
}
