using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.InterestCollections.Models;
using Jellyfin.Plugin.InterestCollections.Services;

namespace Jellyfin.Plugin.InterestCollections.Providers;

/// <summary>
/// Derives interests from the genres and tags Jellyfin already holds, without any network access.
/// </summary>
/// <remarks>
/// Recall is modest by design — it can only surface what other metadata providers already wrote —
/// but it needs no credentials, raises no licensing questions, and gives the test suite a provider
/// with entirely predictable output.
/// </remarks>
public sealed class LocalRulesInterestProvider : IInterestProvider
{
    private readonly InterestTaxonomy _taxonomy;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalRulesInterestProvider"/> class.
    /// </summary>
    /// <param name="taxonomy">The interest taxonomy.</param>
    /// <exception cref="ArgumentNullException"><paramref name="taxonomy"/> is null.</exception>
    public LocalRulesInterestProvider(InterestTaxonomy taxonomy)
    {
        ArgumentNullException.ThrowIfNull(taxonomy);
        _taxonomy = taxonomy;
    }

    /// <inheritdoc />
    public string Id => "local-rules";

    /// <inheritdoc />
    public string Name => "Local rules (offline)";

    /// <inheritdoc />
    public int ResultVersion => 1;

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    public Task<ProviderResult> GetInterestsAsync(
        MediaIdentity media,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        cancellationToken.ThrowIfCancellationRequested();

        var results = new List<InterestRef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in Enumerate(media))
        {
            if (!_taxonomy.TryGetByName(candidate, out var definition) || definition is null)
            {
                continue;
            }

            var resolved = _taxonomy.Resolve(definition.Id, definition.Name);
            if (resolved is not null && seen.Add(resolved.Key))
            {
                results.Add(resolved);
            }
        }

        return Task.FromResult(ProviderResult.Success(results));
    }

    /// <inheritdoc />
    public Task<ProviderTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ProviderTestResult.Ok(string.Format(
            CultureInfo.InvariantCulture,
            "Ready. Works offline against the bundled taxonomy of {0} interests.",
            _taxonomy.All.Count)));
    }

    /// <summary>
    /// Yields every string worth testing against the taxonomy for one item.
    /// </summary>
    /// <param name="media">The item.</param>
    /// <returns>The candidate strings.</returns>
    private static IEnumerable<string> Enumerate(MediaIdentity media)
    {
        foreach (var genre in media.Genres)
        {
            yield return genre;
        }

        foreach (var tag in media.Tags)
        {
            yield return tag;
        }
    }
}
