using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.InterestCollections.Models;
using Jellyfin.Plugin.InterestCollections.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections.Tasks;

/// <summary>
/// Classifies library items that have not been processed yet, or whose settings changed.
/// </summary>
/// <remarks>
/// This is the everyday task. Items already processed under the current settings are served from
/// the cache, so a nightly run over a large library costs almost no provider requests.
/// </remarks>
public sealed class ScanNewMediaTask : IScheduledTask
{
    private readonly InterestProcessingService _processor;
    private readonly ILogger<ScanNewMediaTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScanNewMediaTask"/> class.
    /// </summary>
    /// <param name="processor">The processing pipeline.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public ScanNewMediaTask(InterestProcessingService processor, ILogger<ScanNewMediaTask> logger)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(logger);

        _processor = processor;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Scan new media for interests";

    /// <inheritdoc />
    public string Key => "InterestCollectionsScanNewMedia";

    /// <inheritdoc />
    public string Description =>
        "Looks up interests for movies and shows that have not been classified yet and writes them "
        + "as tags.";

    /// <inheritdoc />
    public string Category => "Interest Collections";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            await _processor.RunAsync(progress, RunOptions.Default, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Interest scan cancelled");
            throw;
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(24).Ticks,
        },
    ];
}
