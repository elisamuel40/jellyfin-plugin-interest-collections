using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections.Storage;

/// <summary>
/// Persists one plugin-owned JSON file inside the plugin data folder.
/// </summary>
/// <remarks>
/// Jellyfin 10.11 requires every database access to go through EF Core and describes the plugin
/// database API as highly experimental, so this plugin keeps its own state in plain files instead
/// of touching the server database. Writes go to a temporary file that is then moved into place,
/// so a crash mid-write leaves the previous state intact rather than a truncated file.
/// </remarks>
/// <typeparam name="T">The document type held in the file.</typeparam>
public sealed class JsonStore<T> : IDisposable
    where T : class, new()
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private T? _cached;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonStore{T}"/> class.
    /// </summary>
    /// <param name="filePath">The absolute path of the backing file.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is null.</exception>
    public JsonStore(string filePath, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(logger);

        _filePath = filePath;
        _logger = logger;
    }

    /// <summary>
    /// Reads the document, loading it from disk on first use.
    /// </summary>
    /// <returns>The document, never null.</returns>
    public T Read()
    {
        var cached = Volatile.Read(ref _cached);
        if (cached is not null)
        {
            return cached;
        }

        _mutex.Wait();
        try
        {
            _cached ??= Load();
            return _cached;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>
    /// Mutates the document under a lock and writes it back atomically.
    /// </summary>
    /// <param name="mutate">The mutation to apply.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mutate"/> is null.</exception>
    public void Update(Action<T> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        _mutex.Wait();
        try
        {
            _cached ??= Load();
            mutate(_cached);
            Save(_cached);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>
    /// Mutates the document under a lock and returns a value computed from it.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="mutate">The mutation to apply.</param>
    /// <returns>The value the mutation produced.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mutate"/> is null.</exception>
    public TResult Update<TResult>(Func<T, TResult> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        _mutex.Wait();
        try
        {
            _cached ??= Load();
            var result = mutate(_cached);
            Save(_cached);
            return result;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _mutex.Dispose();

    /// <summary>
    /// Loads the document, falling back to an empty one when the file is missing or unreadable.
    /// </summary>
    /// <returns>The loaded document.</returns>
    private T Load()
    {
        if (!File.Exists(_filePath))
        {
            return new T();
        }

        try
        {
            using var stream = File.OpenRead(_filePath);
            return JsonSerializer.Deserialize<T>(stream, _jsonOptions) ?? new T();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Losing plugin state costs one re-scan; refusing to start costs the administrator a
            // working plugin. Start clean and say so.
            _logger.LogWarning(ex, "Could not read {File}; starting from empty state", _filePath);
            return new T();
        }
    }

    /// <summary>
    /// Writes the document through a temporary file so readers never see a partial write.
    /// </summary>
    /// <param name="document">The document to persist.</param>
    private void Save(T document)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = _filePath + ".tmp";

            using (var stream = File.Create(temporaryPath))
            {
                JsonSerializer.Serialize(stream, document, _jsonOptions);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not write {File}; state was kept in memory only", _filePath);
        }
    }
}
