using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Jellyfin.Plugin.InterestCollections.Configuration;

namespace Jellyfin.Plugin.InterestCollections.Providers;

/// <summary>
/// Resolves the configured provider. Every provider is constructed once and kept, so switching
/// sources on the configuration page takes effect immediately without a server restart.
/// </summary>
public sealed class InterestProviderFactory
{
    private readonly IReadOnlyDictionary<InterestProviderKind, IInterestProvider> _providers;
    private readonly Func<PluginConfiguration> _configurationAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="InterestProviderFactory"/> class.
    /// </summary>
    /// <param name="imdb">The IMDb provider.</param>
    /// <param name="tmdb">The TMDb provider.</param>
    /// <param name="local">The offline provider.</param>
    /// <param name="configurationAccessor">Reads the current plugin configuration.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public InterestProviderFactory(
        ImdbGraphQlInterestProvider imdb,
        TmdbKeywordInterestProvider tmdb,
        LocalRulesInterestProvider local,
        Func<PluginConfiguration> configurationAccessor)
    {
        ArgumentNullException.ThrowIfNull(imdb);
        ArgumentNullException.ThrowIfNull(tmdb);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(configurationAccessor);

        _providers = new ReadOnlyDictionary<InterestProviderKind, IInterestProvider>(
            new Dictionary<InterestProviderKind, IInterestProvider>
            {
                [InterestProviderKind.ImdbGraphQl] = imdb,
                [InterestProviderKind.TmdbKeywords] = tmdb,
                [InterestProviderKind.LocalRules] = local,
            });

        _configurationAccessor = configurationAccessor;
    }

    /// <summary>
    /// Gets every provider the plugin knows about.
    /// </summary>
    public IEnumerable<IInterestProvider> All => _providers.Values;

    /// <summary>
    /// Gets the provider selected in the configuration, falling back to the offline provider when
    /// the stored value is not a provider this build knows about.
    /// </summary>
    /// <returns>The provider to use.</returns>
    public IInterestProvider GetCurrent() => Get(_configurationAccessor().Provider);

    /// <summary>
    /// Gets a specific provider.
    /// </summary>
    /// <param name="kind">The provider to fetch.</param>
    /// <returns>The provider, or the offline provider when the kind is unknown.</returns>
    public IInterestProvider Get(InterestProviderKind kind)
        => _providers.TryGetValue(kind, out var provider)
            ? provider
            : _providers[InterestProviderKind.LocalRules];
}
