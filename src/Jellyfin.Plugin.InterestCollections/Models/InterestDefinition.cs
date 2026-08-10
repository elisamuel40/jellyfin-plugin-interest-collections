namespace Jellyfin.Plugin.InterestCollections.Models;

/// <summary>
/// A single entry of the bundled IMDb interest taxonomy.
/// </summary>
public sealed class InterestDefinition
{
    /// <summary>
    /// Gets the stable IMDb identifier, for example <c>in0000182</c> for Psychological Thriller.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the canonical display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the name of the category the interest belongs to, for example <c>Thriller</c>.
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Gets a value indicating whether the interest is the genre-level entry of its category —
    /// an interest whose name equals its category name, such as Drama inside Drama. Those overlap
    /// with Jellyfin's own genres and are excluded by default.
    /// </summary>
    public required bool IsGenreLevel { get; init; }
}
