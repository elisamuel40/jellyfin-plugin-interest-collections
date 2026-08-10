using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.InterestCollections.Models;
using Jellyfin.Plugin.InterestCollections.Services;
using Jellyfin.Plugin.InterestCollections.Storage;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.InterestCollections.Tasks;

/// <summary>
/// Reconciles tags and collections across the whole library, forgetting per-item state first so
/// every title is re-evaluated.
/// </summary>
/// <remarks>
/// Cached provider answers are kept, so a full rebuild is cheap in provider requests. Use this
/// after changing filters, aliases or the minimum collection size.
/// </remarks>
public sealed class RebuildInterestCollectionsTask : IScheduledTask
{
    private readonly InterestProcessingService _processor;
    private readonly ProcessedItemStore _processedItems;

    /// <summary>
    /// Initializes a new instance of the <see cref="RebuildInterestCollectionsTask"/> class.
    /// </summary>
    /// <param name="processor">The processing pipeline.</param>
    /// <param name="processedItems">The per-item state store.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public RebuildInterestCollectionsTask(
        InterestProcessingService processor,
        ProcessedItemStore processedItems)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(processedItems);

        _processor = processor;
        _processedItems = processedItems;
    }

    /// <inheritdoc />
    public string Name => "Rebuild interest collections";

    /// <inheritdoc />
    public string Key => "InterestCollectionsRebuild";

    /// <inheritdoc />
    public string Description =>
        "Re-evaluates every title against the current filters and rebuilds the interest "
        + "collections. Cached provider answers are reused, so this costs almost no API requests.";

    /// <inheritdoc />
    public string Category => "Interest Collections";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _processedItems.Clear();
        return _processor.RunAsync(progress, RunOptions.Default, cancellationToken);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
