using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.InterestCollections.Services;

/// <summary>
/// Parses the newline-delimited text fields the configuration page uses. Jellyfin persists plugin
/// settings as XML, so lists and maps are stored as text rather than as collections.
/// </summary>
public static class ConfigurationText
{
    private static readonly char[] _lineSeparators = ['\r', '\n'];

    /// <summary>
    /// Splits a text field into trimmed, non-empty lines.
    /// </summary>
    /// <param name="value">The raw field value.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> ToLines(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var lines = value.Split(_lineSeparators, StringSplitOptions.RemoveEmptyEntries);
        var results = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                results.Add(trimmed);
            }
        }

        return results;
    }

    /// <summary>
    /// Parses a text field of <c>Alias = Canonical</c> lines into a lookup keyed by the alias's
    /// match key, so aliases match regardless of spelling.
    /// </summary>
    /// <param name="value">The raw field value.</param>
    /// <returns>The alias to canonical-name map.</returns>
    public static IReadOnlyDictionary<string, string> ToAliasMap(string? value)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in ToLines(value))
        {
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            var alias = InterestNormalizer.MatchKey(line[..separator]);
            var canonical = line[(separator + 1)..].Trim();

            if (alias.Length > 0 && canonical.Length > 0)
            {
                map[alias] = canonical;
            }
        }

        return map;
    }

    /// <summary>
    /// Parses a text field into a set of match keys, for case- and punctuation-insensitive
    /// membership tests.
    /// </summary>
    /// <param name="value">The raw field value.</param>
    /// <returns>The set of match keys.</returns>
    public static HashSet<string> ToMatchKeySet(string? value)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in ToLines(value))
        {
            var key = InterestNormalizer.MatchKey(line);
            if (key.Length > 0)
            {
                set.Add(key);
            }
        }

        return set;
    }
}
