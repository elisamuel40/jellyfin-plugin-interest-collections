using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.InterestCollections.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections.Storage;

/// <summary>
/// Caches provider answers so the same title is never looked up twice within its lifetime, which
/// is what keeps a full library reconciliation from hammering an external API.
/// </summary>
public sealed class InterestCache : IDisposable
{
    private readonly JsonStore<CacheDocument> _store;
    private readonly Services.InterestTaxonomy _taxonomy;

    /// <summary>
    /// Initializes a new instance of the <see cref="InterestCache"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The plugin data folder.</param>
    /// <param name="taxonomy">The taxonomy used to rehydrate cached interests.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public InterestCache(string dataFolderPath, Services.InterestTaxonomy taxonomy, ILogger<InterestCache> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);
        ArgumentNullException.ThrowIfNull(taxonomy);
        ArgumentNullException.ThrowIfNull(logger);

        _store = new JsonStore<CacheDocument>(Path.Combine(dataFolderPath, "interest-cache.json"), logger);
        _taxonomy = taxonomy;
    }

    /// <summary>
    /// Gets the number of entries currently held.
    /// </summary>
    public int Count => _store.Read().Entries.Count;

    /// <summary>
    /// Reads a still-valid cached answer.
    /// </summary>
    /// <param name="cacheKey">The key produced by <see cref="MediaIdentity.GetCacheKey"/>.</param>
    /// <param name="providerVersion">The current result-shape version of the provider.</param>
    /// <param name="lifetime">How long a non-empty answer stays valid.</param>
    /// <param name="negativeLifetime">How long an empty answer stays valid.</param>
    /// <param name="interests">The cached interests when the entry is usable.</param>
    /// <returns><see langword="true"/> when a live entry was found.</returns>
    public bool TryGet(
        string cacheKey,
        int providerVersion,
        TimeSpan lifetime,
        TimeSpan negativeLifetime,
        out IReadOnlyList<InterestRef> interests)
    {
        interests = [];

        if (!_store.Read().Entries.TryGetValue(cacheKey, out var entry)
            || entry.ProviderVersion != providerVersion)
        {
            return false;
        }

        var age = DateTimeOffset.UtcNow - entry.FetchedAt;
        var allowed = entry.InterestKeys.Count == 0 ? negativeLifetime : lifetime;

        if (age > allowed || age < TimeSpan.Zero)
        {
            return false;
        }

        interests = Rehydrate(entry);
        return true;
    }

    /// <summary>
    /// Stores a provider answer. Only successful lookups are ever cached; a failure must be
    /// retried later rather than remembered.
    /// </summary>
    /// <param name="cacheKey">The cache key.</param>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="providerVersion">The provider's result-shape version.</param>
    /// <param name="interests">The interests returned.</param>
    /// <exception cref="ArgumentNullException"><paramref name="interests"/> is null.</exception>
    public void Set(
        string cacheKey,
        string providerId,
        int providerVersion,
        IReadOnlyList<InterestRef> interests)
    {
        ArgumentNullException.ThrowIfNull(interests);

        var entry = new CacheEntry
        {
            Provider = providerId,
            ProviderVersion = providerVersion,
            FetchedAt = DateTimeOffset.UtcNow,
        };

        foreach (var interest in interests)
        {
            entry.InterestKeys.Add(interest.Key);
            entry.InterestNames.Add(interest.Name);
        }

        _store.Update(document => document.Entries[cacheKey] = entry);
    }

    /// <summary>
    /// Drops every cached answer, forcing the next run to query the provider again.
    /// </summary>
    public void Clear() => _store.Update(document => document.Entries.Clear());

    /// <summary>
    /// Removes entries that are past their lifetime.
    /// </summary>
    /// <param name="lifetime">How long a non-empty answer stays valid.</param>
    /// <param name="negativeLifetime">How long an empty answer stays valid.</param>
    /// <returns>The number of entries removed.</returns>
    public int Prune(TimeSpan lifetime, TimeSpan negativeLifetime) => _store.Update(document =>
    {
        var now = DateTimeOffset.UtcNow;
        var expired = new List<string>();

        foreach (var (key, entry) in document.Entries)
        {
            var allowed = entry.InterestKeys.Count == 0 ? negativeLifetime : lifetime;
            if (now - entry.FetchedAt > allowed)
            {
                expired.Add(key);
            }
        }

        foreach (var key in expired)
        {
            document.Entries.Remove(key);
        }

        return expired.Count;
    });

    /// <inheritdoc />
    public void Dispose() => _store.Dispose();

    /// <summary>
    /// Turns a stored entry back into interest references, preferring the taxonomy's canonical
    /// spelling so a taxonomy update reaches already-cached titles.
    /// </summary>
    /// <param name="entry">The stored entry.</param>
    /// <returns>The rehydrated interests.</returns>
    private List<InterestRef> Rehydrate(CacheEntry entry)
    {
        var results = new List<InterestRef>(entry.InterestKeys.Count);

        for (var index = 0; index < entry.InterestKeys.Count; index++)
        {
            var name = index < entry.InterestNames.Count ? entry.InterestNames[index] : null;
            var resolved = _taxonomy.Resolve(entry.InterestKeys[index], name);

            if (resolved is not null)
            {
                results.Add(resolved);
            }
        }

        return results;
    }
}
