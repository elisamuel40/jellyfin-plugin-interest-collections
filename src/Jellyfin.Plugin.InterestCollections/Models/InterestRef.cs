using System;

namespace Jellyfin.Plugin.InterestCollections.Models;

/// <summary>
/// An interest attached to a title: the canonical name that becomes a Jellyfin tag, plus the
/// taxonomy metadata used to filter it.
/// </summary>
public sealed class InterestRef : IEquatable<InterestRef>
{
    /// <summary>
    /// Gets the canonical key. For taxonomy entries this is the IMDb interest id
    /// (<c>in0000182</c>); for interests the taxonomy does not know, it is a slug derived from
    /// the name, prefixed with <c>x:</c>.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the canonical display name written to Jellyfin.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the taxonomy category, or <see langword="null"/> for interests outside the taxonomy.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is the genre-level interest of its category.
    /// </summary>
    public bool IsGenreLevel { get; init; }

    /// <inheritdoc />
    public bool Equals(InterestRef? other)
        => other is not null && string.Equals(Key, other.Key, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as InterestRef);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);

    /// <inheritdoc />
    public override string ToString() => Name;
}
