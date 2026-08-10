using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.InterestCollections.Providers.Http;

/// <summary>
/// JSON helpers shared by the providers.
/// </summary>
public static class HttpContentExtensions
{
    /// <summary>
    /// Reads and deserialises a JSON response body.
    /// </summary>
    /// <typeparam name="T">The type to deserialise into.</typeparam>
    /// <param name="content">The response content.</param>
    /// <param name="options">The serializer options.</param>
    /// <param name="cancellationToken">Token used to cancel the read.</param>
    /// <returns>The deserialised payload, or <see langword="null"/> when the body is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="JsonException">The body is not valid JSON for <typeparamref name="T"/>.</exception>
    public static async Task<T?> ReadFromJsonSafeAsync<T>(
        this HttpContent content,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(content);

        using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer
            .DeserializeAsync<T>(stream, options, cancellationToken)
            .ConfigureAwait(false);
    }
}
