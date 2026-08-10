using System;
using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.InterestCollections.Services;

/// <summary>
/// Turns the many spellings a provider may return into one canonical form.
/// </summary>
/// <remarks>
/// Two separate transformations are needed. <see cref="MatchKey"/> produces an aggressive
/// comparison key so that <c>psychological thriller</c>, <c>Psychological-Thriller</c> and
/// <c>PSYCHOLOGICAL THRILLER</c> all collide. <see cref="ToDisplayName"/> produces the readable
/// name used when an interest is not part of the bundled taxonomy and therefore has no canonical
/// spelling to fall back on.
/// </remarks>
public static class InterestNormalizer
{
    /// <summary>
    /// The longest all-uppercase word still treated as an acronym rather than as shouting.
    /// </summary>
    private const int AcronymMaxLength = 4;

    /// <summary>
    /// Words that stay lowercase inside a display name unless they lead it.
    /// </summary>
    private static readonly string[] _minorWords =
    [
        "a", "an", "and", "as", "at", "but", "by", "for", "from", "in", "nor", "of", "on",
        "or", "the", "to", "vs", "with",
    ];

    /// <summary>
    /// Builds the case-, accent- and punctuation-insensitive key used to compare two interests.
    /// </summary>
    /// <param name="value">The raw interest text.</param>
    /// <returns>An uppercase, alphanumeric-only key, or an empty string when nothing remains.</returns>
    public static string MatchKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Produces a readable, consistently capitalised name: outer whitespace removed, internal
    /// whitespace collapsed to single spaces, and title casing applied while preserving words that
    /// are already fully uppercase, such as <c>AI</c>.
    /// </summary>
    /// <param name="value">The raw interest text.</param>
    /// <returns>The display name, or an empty string when the input carries no content.</returns>
    public static string ToDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var collapsed = CollapseSeparators(value);
        if (collapsed.Length == 0)
        {
            return string.Empty;
        }

        var words = collapsed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder(collapsed.Length);

        for (var index = 0; index < words.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            builder.Append(CaseWord(words[index], isFirst: index == 0));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds a stable slug for interests that are not part of the bundled taxonomy, so they still
    /// get a durable key that survives spelling differences between providers.
    /// </summary>
    /// <param name="value">The raw interest text.</param>
    /// <returns>A slug of the form <c>x:psychological-thriller</c>.</returns>
    public static string ToSlugKey(string? value)
    {
        var collapsed = CollapseSeparators(value).ToUpperInvariant();
        if (collapsed.Length == 0)
        {
            return string.Empty;
        }

        var decomposed = collapsed.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder("x:", decomposed.Length + 2);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if ((character is ' ' or '-') && builder.Length > 2 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().TrimEnd('-');
    }

    /// <summary>
    /// Replaces separator punctuation with spaces and collapses runs of whitespace.
    /// </summary>
    /// <param name="value">The raw text.</param>
    /// <returns>The collapsed text.</returns>
    private static string CollapseSeparators(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var character in value)
        {
            var isSeparator = char.IsWhiteSpace(character) || character is '_' or '/' or '\\' or '|';

            if (isSeparator)
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Applies title casing to a single word, leaving acronyms and hyphenated compounds intact.
    /// </summary>
    /// <param name="word">The word to case.</param>
    /// <param name="isFirst">Whether the word leads the name.</param>
    /// <returns>The cased word.</returns>
    private static string CaseWord(string word, bool isFirst)
    {
        // Short all-caps words are acronyms such as "AI", "TV" or "NASA" and keep their casing.
        // Longer all-caps words are shouting, not acronyms, and get title cased like the rest.
        if (word.Length > 1 && word.Length <= AcronymMaxLength && IsAcronym(word))
        {
            return word;
        }

        if (!isFirst && Array.Exists(_minorWords, minor => string.Equals(minor, word, StringComparison.OrdinalIgnoreCase)))
        {
            return word.ToLowerInvariant();
        }

        // Hyphenated compounds capitalise only the leading segment, matching IMDb's own style
        // for names such as "Coming-of-Age" and "Hard-boiled Detective".
        var hyphen = word.IndexOf('-', StringComparison.Ordinal);
        if (hyphen > 0)
        {
            return Capitalize(word[..hyphen]) + word[hyphen..].ToLowerInvariant();
        }

        return Capitalize(word);
    }

    /// <summary>
    /// Uppercases the first character and lowercases the rest.
    /// </summary>
    /// <param name="word">The word to capitalise.</param>
    /// <returns>The capitalised word.</returns>
    private static string Capitalize(string word)
        => word.Length switch
        {
            0 => word,
            1 => word.ToUpperInvariant(),
            _ => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant(),
        };

    /// <summary>
    /// Determines whether a word is an acronym: it contains at least one letter and every letter
    /// in it is uppercase.
    /// </summary>
    /// <param name="word">The word to inspect.</param>
    /// <returns><see langword="true"/> when the word reads as an acronym.</returns>
    private static bool IsAcronym(string word)
    {
        var sawLetter = false;

        foreach (var character in word)
        {
            if (!char.IsLetter(character))
            {
                continue;
            }

            if (char.IsLower(character))
            {
                return false;
            }

            sawLetter = true;
        }

        return sawLetter;
    }
}
