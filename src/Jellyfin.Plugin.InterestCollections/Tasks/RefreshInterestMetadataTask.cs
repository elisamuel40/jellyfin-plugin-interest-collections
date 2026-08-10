using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.InterestCollections.Models;
using Jellyfin.Plugin.InterestCollections.Services;
using Jellyfin.Plugin.InterestCollections.Storage;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections.Tasks;

/// <summary>
/// Discards the cache and re-queries the provider for the whole library.
/// </summary>
/// <remarks>
/// This is the expensive task — one provider request per title — so it has no default schedule and
/// is meant to be run by hand, or occasionally, when the provider's data is known to have moved on.
/// </remarks>
public sealed class RefreshInterestMetadataTask : IScheduledTask
{
    private readonly InterestProcessingService _processor;
    private readonly InterestCache _cache;
    private readonly ProcessedItemStore _processedItems;
    private readonly ILogger<RefreshInterestMetadataTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshInterestMetadataTask"/> class.
    /// </summary>
    /// <param name="processor">The processing pipeline.</param>
    /// <param name="cache">The provider answer cache.</param>
    /// <param name="processedItems">The per-item state store.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public RefreshInterestMetadataTask(
        InterestProcessingService processor,
        InterestCache cache,
        ProcessedItemStore processedItems,
        ILogger<RefreshInterestMetadataTask> logger)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(processedItems);
        ArgumentNullException.ThrowIfNull(logger);

        _processor = processor;
        _cache = cache;
        _processedItems = processedItems;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Refresh interest metadata from the provider";

    /// <inheritdoc />
    public string Key => "InterestCollectionsRefreshMetadata";

    /// <inheritdoc />
    public string Description =>
        "Clears the cache and queries the interest provider again for every title. This sends one "
        + "request per title, so run it sparingly.";

    /// <inheritdoc />
    public string Category => "Interest Collections";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Clearing the interest cache before a full refresh");
        _cache.Clear();
        _processedItems.Clear();

        return _processor.RunAsync(progress, RunOptions.Default, cancellationToken);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
