namespace Jellyfin.Plugin.InterestCollections.Models;

/// <summary>
/// One taxonomy category, for the category filter checkboxes.
/// </summary>
public sealed class CategorySummary
{
    /// <summary>
    /// Gets the category name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets how many interests the category holds.
    /// </summary>
    public required int InterestCount { get; init; }
}
