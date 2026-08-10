using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.InterestCollections.Models;

namespace Jellyfin.Plugin.InterestCollections.Providers;

/// <summary>
/// A source of semantic interests for a library item.
/// </summary>
/// <remarks>
/// Implementations must never throw for an expected failure — a timeout, a rate limit, a missing
/// title — and must return <see cref="ProviderResult.Failure"/> instead, so that a provider being
/// unavailable can never break a Jellyfin library scan.
/// </remarks>
public interface IInterestProvider
{
    /// <summary>
    /// Gets the stable identifier used in cache keys and logs.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the human-readable provider name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a version marker for the provider's output shape. Bumping it invalidates every cached
    /// entry written by earlier versions.
    /// </summary>
    int ResultVersion { get; }

    /// <summary>
    /// Gets a value indicating whether the provider is configured well enough to be used.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Looks up the interests for one item.
    /// </summary>
    /// <param name="media">The item to classify.</param>
    /// <param name="cancellationToken">Token used to cancel the lookup.</param>
    /// <returns>The lookup outcome.</returns>
    Task<ProviderResult> GetInterestsAsync(MediaIdentity media, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies that the provider can reach its backend with the current settings, for the
    /// "Test Connection" button on the configuration page.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the check.</param>
    /// <returns>A message describing the outcome, suitable for display to an administrator.</returns>
    Task<ProviderTestResult> TestConnectionAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The outcome of a connection test.
/// </summary>
public sealed class ProviderTestResult
{
    /// <summary>
    /// Gets a value indicating whether the provider answered as expected.
    /// </summary>
    public required bool Succeeded { get; init; }

    /// <summary>
    /// Gets a message for the administrator. Never contains credentials.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Creates a successful outcome.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <returns>The outcome.</returns>
    public static ProviderTestResult Ok(string message)
        => new() { Succeeded = true, Message = message };

    /// <summary>
    /// Creates a failed outcome.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <returns>The outcome.</returns>
    public static ProviderTestResult Fail(string message)
        => new() { Succeeded = false, Message = message };
}
