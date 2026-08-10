using System;
using System.Net.Http;
using Jellyfin.Plugin.InterestCollections.Configuration;
using Jellyfin.Plugin.InterestCollections.Events;
using Jellyfin.Plugin.InterestCollections.Providers;
using Jellyfin.Plugin.InterestCollections.Providers.Http;
using Jellyfin.Plugin.InterestCollections.Services;
using Jellyfin.Plugin.InterestCollections.Storage;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections;

/// <summary>
/// Registers the plugin's services with the server's dependency injection container.
/// </summary>
/// <remarks>
/// Registration happens before plugin assemblies are fully loaded, so configuration is reached
/// through a factory rather than captured: every service reads the settings in force at the moment
/// it runs, which is what lets the configuration page take effect without a server restart.
/// </remarks>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.AddSingleton<Func<PluginConfiguration>>(_ =>
            () => Plugin.Instance?.Configuration ?? new PluginConfiguration());

        serviceCollection.AddSingleton(_ => InterestTaxonomy.Shared);

        serviceCollection.AddSingleton(provider => new InterestCache(
            GetDataFolderPath(),
            provider.GetRequiredService<InterestTaxonomy>(),
            provider.GetRequiredService<ILogger<InterestCache>>()));

        serviceCollection.AddSingleton(provider => new ProcessedItemStore(
            GetDataFolderPath(),
            provider.GetRequiredService<ILogger<ProcessedItemStore>>()));

        serviceCollection.AddSingleton(provider => new ManagedCollectionStore(
            GetDataFolderPath(),
            provider.GetRequiredService<ILogger<ManagedCollectionStore>>()));

        serviceCollection.AddSingleton(provider => new ResilientHttpClient(
            new HttpClient(),
            provider.GetRequiredService<ILogger<ResilientHttpClient>>()));

        serviceCollection.AddSingleton<ImdbGraphQlInterestProvider>();
        serviceCollection.AddSingleton<TmdbKeywordInterestProvider>();
        serviceCollection.AddSingleton<LocalRulesInterestProvider>();
        serviceCollection.AddSingleton<InterestProviderFactory>();

        serviceCollection.AddSingleton<MediaScannerService>();
        serviceCollection.AddSingleton<InterestFilter>();
        serviceCollection.AddSingleton<TagSynchronizationService>();
        serviceCollection.AddSingleton<CollectionSynchronizationService>();
        serviceCollection.AddSingleton<InterestProcessingService>();
        serviceCollection.AddSingleton<InterestStatisticsService>();

        serviceCollection.AddHostedService<LibraryEventHostedService>();
    }

    /// <summary>
    /// Resolves the plugin data folder, falling back to the current directory in the unusual case
    /// where the plugin instance is not available yet.
    /// </summary>
    /// <returns>The folder to store plugin state in.</returns>
    private static string GetDataFolderPath()
        => Plugin.Instance?.DataFolderPath ?? AppContext.BaseDirectory;
}
