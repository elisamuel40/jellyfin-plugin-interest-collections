using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.InterestCollections.Models;

namespace Jellyfin.Plugin.InterestCollections.Services;

/// <summary>
/// The bundled IMDb interest taxonomy: 313 interests across 26 categories, shipped as an embedded
/// resource so the plugin can canonicalise and categorise interests without any network access.
/// </summary>
public sealed class InterestTaxonomy
{
    private const string ResourceName =
        "Jellyfin.Plugin.InterestCollections.Data.imdb-interests.json";

    private static readonly Lazy<InterestTaxonomy> _shared = new(LoadFromEmbeddedResource);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Dictionary<string, InterestDefinition> _byId;
    private readonly Dictionary<string, InterestDefinition> _byMatchKey;
    private readonly ReadOnlyCollection<InterestDefinition> _all;
    private readonly ReadOnlyCollection<string> _categories;

    /// <summary>
    /// Initializes a new instance of the <see cref="InterestTaxonomy"/> class.
    /// </summary>
    /// <param name="definitions">The interests that make up the taxonomy.</param>
    /// <exception cref="ArgumentNullException"><paramref name="definitions"/> is null.</exception>
    public InterestTaxonomy(IEnumerable<InterestDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _byId = new Dictionary<string, InterestDefinition>(StringComparer.OrdinalIgnoreCase);
        _byMatchKey = new Dictionary<string, InterestDefinition>(StringComparer.Ordinal);
        var all = new List<InterestDefinition>();
        var categories = new List<string>();
        var seenCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitions)
        {
            all.Add(definition);
            _byId[definition.Id] = definition;

            // First writer wins: an interest name that appears in two categories keeps the
            // category it was first declared in, which matches IMDb's own ordering.
            var key = InterestNormalizer.MatchKey(definition.Name);
            if (key.Length > 0)
            {
                _byMatchKey.TryAdd(key, definition);
            }

            if (seenCategories.Add(definition.Category))
            {
                categories.Add(definition.Category);
            }
        }

        _all = all.AsReadOnly();
        _categories = categories.AsReadOnly();
    }

    /// <summary>
    /// Gets the taxonomy loaded from the embedded resource.
    /// </summary>
    public static InterestTaxonomy Shared => _shared.Value;

    /// <summary>
    /// Gets every interest in the taxonomy.
    /// </summary>
    public IReadOnlyList<InterestDefinition> All => _all;

    /// <summary>
    /// Gets the category names, in the order IMDb presents them.
    /// </summary>
    public IReadOnlyList<string> Categories => _categories;

    /// <summary>
    /// Looks an interest up by its IMDb identifier.
    /// </summary>
    /// <param name="id">The IMDb interest id, for example <c>in0000182</c>.</param>
    /// <param name="definition">The matching definition when found.</param>
    /// <returns><see langword="true"/> when the id is part of the taxonomy.</returns>
    public bool TryGetById(string? id, out InterestDefinition? definition)
    {
        definition = null;
        return !string.IsNullOrWhiteSpace(id) && _byId.TryGetValue(id, out definition);
    }

    /// <summary>
    /// Looks an interest up by name, ignoring case, accents and punctuation.
    /// </summary>
    /// <param name="name">The interest name in any spelling.</param>
    /// <param name="definition">The matching definition when found.</param>
    /// <returns><see langword="true"/> when the name resolves to a taxonomy entry.</returns>
    public bool TryGetByName(string? name, out InterestDefinition? definition)
    {
        definition = null;
        var key = InterestNormalizer.MatchKey(name);
        return key.Length > 0 && _byMatchKey.TryGetValue(key, out definition);
    }

    /// <summary>
    /// Resolves raw provider output into a canonical interest reference. Entries the taxonomy
    /// recognises adopt its canonical name, category and genre-level flag; anything else keeps a
    /// normalised display name and a derived slug key, so unknown providers still work.
    /// </summary>
    /// <param name="id">The provider's identifier for the interest, when it has one.</param>
    /// <param name="name">The provider's name for the interest.</param>
    /// <returns>The canonical reference, or <see langword="null"/> when nothing usable remains.</returns>
    public InterestRef? Resolve(string? id, string? name)
    {
        if (TryGetById(id, out var byId) && byId is not null)
        {
            return ToRef(byId);
        }

        if (TryGetByName(name, out var byName) && byName is not null)
        {
            return ToRef(byName);
        }

        var displayName = InterestNormalizer.ToDisplayName(name);
        if (displayName.Length == 0)
        {
            return null;
        }

        return new InterestRef
        {
            Key = InterestNormalizer.ToSlugKey(displayName),
            Name = displayName,
            Category = null,
            IsGenreLevel = false,
        };
    }

    private static InterestRef ToRef(InterestDefinition definition) => new()
    {
        Key = definition.Id,
        Name = definition.Name,
        Category = definition.Category,
        IsGenreLevel = definition.IsGenreLevel,
    };

    private static InterestTaxonomy LoadFromEmbeddedResource()
    {
        var assembly = typeof(InterestTaxonomy).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Embedded interest taxonomy '{0}' is missing from {1}.",
                    ResourceName,
                    assembly.FullName));

        return LoadFrom(stream);
    }

    /// <summary>
    /// Reads a taxonomy from a JSON stream in the bundled format.
    /// </summary>
    /// <param name="stream">The JSON stream.</param>
    /// <returns>The parsed taxonomy.</returns>
    /// <exception cref="InvalidOperationException">The document could not be parsed.</exception>
    internal static InterestTaxonomy LoadFrom(Stream stream)
    {
        var document = JsonSerializer.Deserialize<TaxonomyDocument>(stream, _jsonOptions)
            ?? throw new InvalidOperationException("The interest taxonomy document is empty.");

        var definitions = new List<InterestDefinition>();

        foreach (var category in document.Categories)
        {
            foreach (var entry in category.Interests)
            {
                if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                definitions.Add(new InterestDefinition
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    Category = category.Name,
                    IsGenreLevel = entry.GenreLevel,
                });
            }
        }

        return new InterestTaxonomy(definitions);
    }

    private sealed class TaxonomyDocument
    {
        [JsonPropertyName("categories")]
        public IReadOnlyList<CategoryEntry> Categories { get; init; } = [];
    }

    private sealed class CategoryEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("interests")]
        public IReadOnlyList<InterestEntry> Interests { get; init; } = [];
    }

    private sealed class InterestEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("genreLevel")]
        public bool GenreLevel { get; init; }
    }
}
