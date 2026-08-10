using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.InterestCollections.Configuration;
using Jellyfin.Plugin.InterestCollections.Services;
using Jellyfin.Plugin.InterestCollections.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections.Events;

/// <summary>
/// Classifies newly added or refreshed items without waiting for the scheduled task.
/// </summary>
/// <remarks>
/// Library events arrive in bursts and arrive early: Jellyfin raises ItemAdded before the metadata
/// providers have necessarily filled in the provider ids this plugin needs. Items are therefore
/// queued and processed only after a quiet period, and an item that still has no usable id is
/// simply left for the next scheduled run.
///
/// Writing tags itself raises ItemUpdated, which would otherwise loop forever. The guard is the
/// per-item state: right after a write the item's recorded fingerprint matches the current
/// settings, so the event it triggers finds nothing to do and stops there.
/// </remarks>
public sealed class LibraryEventHostedService : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly InterestProcessingService _processor;
    private readonly ProcessedItemStore _processedItems;
    private readonly Func<PluginConfiguration> _configurationAccessor;
    private readonly ILogger<LibraryEventHostedService> _logger;

    private readonly ConcurrentDictionary<Guid, byte> _pending = new();
    private readonly SemaphoreSlim _drainLock = new(1, 1);

    private CancellationTokenSource? _lifetime;
    private Timer? _debounceTimer;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryEventHostedService"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="processor">The processing pipeline.</param>
    /// <param name="processedItems">The per-item state store.</param>
    /// <param name="configurationAccessor">Reads the current plugin configuration.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public LibraryEventHostedService(
        ILibraryManager libraryManager,
        InterestProcessingService processor,
        ProcessedItemStore processedItems,
        Func<PluginConfiguration> configurationAccessor,
        ILogger<LibraryEventHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(processedItems);
        ArgumentNullException.ThrowIfNull(configurationAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        _libraryManager = libraryManager;
        _processor = processor;
        _processedItems = processedItems;
        _configurationAccessor = configurationAccessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetime = new CancellationTokenSource();
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemUpdated += OnItemChanged;

        _logger.LogDebug("Listening for library changes");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemUpdated -= OnItemChanged;

        if (_debounceTimer is not null)
        {
            await _debounceTimer.DisposeAsync().ConfigureAwait(false);
            _debounceTimer = null;
        }

        if (_lifetime is not null)
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
            _lifetime.Dispose();
            _lifetime = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _debounceTimer?.Dispose();
        _lifetime?.Dispose();
        _drainLock.Dispose();
    }

    /// <summary>
    /// Queues a changed item and restarts the quiet-period timer.
    /// </summary>
    private void OnItemChanged(object? sender, ItemChangeEventArgs eventArgs)
    {
        var configuration = _configurationAccessor();
        if (!configuration.ProcessOnLibraryEvents)
        {
            return;
        }

        var item = eventArgs?.Item;
        if (item is null || !IsEligible(item, configuration))
        {
            return;
        }

        // Already up to date under the current settings — this is almost always the echo of the
        // plugin's own write.
        if (!_processedItems.NeedsProcessing(item.Id, configuration.GetProcessingFingerprint()))
        {
            return;
        }

        _pending[item.Id] = 0;
        RestartDebounce(configuration);
    }

    /// <summary>
    /// Determines whether an item is one of the kinds the plugin was told to process.
    /// </summary>
    private static bool IsEligible(BaseItem item, PluginConfiguration configuration)
        => item switch
        {
            MediaBrowser.Controller.Entities.Movies.Movie => configuration.ProcessMovies,
            MediaBrowser.Controller.Entities.TV.Series => configuration.ProcessSeries,
            MediaBrowser.Controller.Entities.TV.Episode => configuration.ProcessEpisodes,
            _ => false,
        };

    private void RestartDebounce(PluginConfiguration configuration)
    {
        var delay = TimeSpan.FromSeconds(Math.Clamp(configuration.EventDebounceSeconds, 5, 3600));

        if (_debounceTimer is null)
        {
            _debounceTimer = new Timer(_ => _ = DrainAsync(), null, delay, Timeout.InfiniteTimeSpan);
            return;
        }

        _debounceTimer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Processes everything queued during the quiet period.
    /// </summary>
    private async Task DrainAsync()
    {
        var token = _lifetime?.Token ?? CancellationToken.None;
        if (token.IsCancellationRequested)
        {
            return;
        }

        await _drainLock.WaitAsync(token).ConfigureAwait(false);

        try
        {
            var items = new List<BaseItem>();

            foreach (var itemId in _pending.Keys)
            {
                _pending.TryRemove(itemId, out _);

                var item = _libraryManager.GetItemById(itemId);
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            if (items.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Processing {Count} items reported by the library", items.Count);
            await _processor.RunForItemsAsync(items, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The server is shutting down; nothing to report.
        }
#pragma warning disable CA1031 // A background handler must never surface an exception to the server.
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing library changes failed; the scheduled task will retry");
        }
#pragma warning restore CA1031
        finally
        {
            _drainLock.Release();
        }
    }
}
