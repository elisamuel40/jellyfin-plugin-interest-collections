using System.Collections.Generic;

namespace Jellyfin.Plugin.InterestCollections.Models;

/// <summary>
/// The outcome of one provider lookup.
/// </summary>
/// <remarks>
/// The distinction between "found nothing" and "could not ask" matters: a successful lookup that
/// returns no interests can be cached and is authoritative, while a failure must never be treated
/// as an empty result, because that would silently discard interests the item already has.
/// </remarks>
public sealed class ProviderResult
{
    private static readonly ProviderResult _empty = new()
    {
        Succeeded = true,
        Interests = [],
    };

    /// <summary>
    /// Gets a value indicating whether the provider was actually able to answer.
    /// </summary>
    public required bool Succeeded { get; init; }

    /// <summary>
    /// Gets the interests returned, already resolved against the taxonomy.
    /// </summary>
    public IReadOnlyList<InterestRef> Interests { get; init; } = [];

    /// <summary>
    /// Gets the reason the lookup failed, for logging. Never contains credentials.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Gets a successful result carrying no interests.
    /// </summary>
    public static ProviderResult Empty => _empty;

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="interests">The interests found.</param>
    /// <returns>The result.</returns>
    public static ProviderResult Success(IReadOnlyList<InterestRef> interests)
        => new() { Succeeded = true, Interests = interests };

    /// <summary>
    /// Creates a failed result. Callers must leave existing metadata untouched when they see one.
    /// </summary>
    /// <param name="reason">A short, credential-free description of what went wrong.</param>
    /// <returns>The result.</returns>
    public static ProviderResult Failure(string reason)
        => new() { Succeeded = false, Interests = [], FailureReason = reason };
}
