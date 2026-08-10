using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.InterestCollections.Configuration;
using Jellyfin.Plugin.InterestCollections.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections.Services;

/// <summary>
/// Decides which of a provider's interests are worth keeping for a given title.
/// </summary>
/// <remarks>
/// Filtering by taxonomy category is what keeps this manageable. IMDb groups its interests into 26
/// categories, so an administrator can switch off all 30 Language interests, or all 70 Franchise
/// ones, with a single checkbox instead of maintaining an ever-growing blocklist. The remaining
/// rules handle the specific cases categories cannot: an interest named after the title itself,
/// hand-written aliases, and interests the administrator disabled by name.
/// </remarks>
public sealed class InterestFilter
{
    private readonly Func<PluginConfiguration> _configurationAccessor;
    private readonly InterestTaxonomy _taxonomy;
    private readonly ILogger<InterestFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InterestFilter"/> class.
    /// </summary>
    /// <param name="configurationAccessor">Reads the current plugin configuration.</param>
    /// <param name="taxonomy">The interest taxonomy.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public InterestFilter(
        Func<PluginConfiguration> configurationAccessor,
        InterestTaxonomy taxonomy,
        ILogger<InterestFilter> logger)
    {
        ArgumentNullException.ThrowIfNull(configurationAccessor);
        ArgumentNullException.ThrowIfNull(taxonomy);
        ArgumentNullException.ThrowIfNull(logger);

        _configurationAccessor = configurationAccessor;
        _taxonomy = taxonomy;
        _logger = logger;
    }

    /// <summary>
    /// Applies every configured rule to a provider's output.
    /// </summary>
    /// <param name="media">The title the interests belong to.</param>
    /// <param name="interests">The interests the provider returned.</param>
    /// <returns>The interests to apply, deduplicated and in the provider's order.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public IReadOnlyList<InterestRef> Apply(MediaIdentity media, IReadOnlyList<InterestRef> interests)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(interests);

        if (interests.Count == 0)
        {
            return [];
        }

        var configuration = _configurationAccessor();
        var rules = FilterRules.Build(configuration, _logger);
        var titleKey = InterestNormalizer.MatchKey(media.Name);

        var accepted = new List<InterestRef>(interests.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var interest in interests)
        {
            var candidate = ApplyAlias(interest, rules);

            if (!IsAccepted(candidate, rules, titleKey, configuration))
            {
                continue;
            }

            if (seen.Add(candidate.Key))
            {
                accepted.Add(candidate);
            }
        }

        return accepted;
    }

    /// <summary>
    /// Determines whether a single interest survives the rules.
    /// </summary>
    /// <param name="interest">The candidate, after alias mapping.</param>
    /// <param name="rules">The compiled rules.</param>
    /// <param name="titleKey">The match key of the title's own name.</param>
    /// <param name="configuration">The configuration in force.</param>
    /// <returns><see langword="true"/> when the interest should be applied.</returns>
    private static bool IsAccepted(
        InterestRef interest,
        FilterRules rules,
        string titleKey,
        PluginConfiguration configuration)
    {
        var key = InterestNormalizer.MatchKey(interest.Name);

        // IMDb returns a franchise interest named after the title itself — Breaking Bad carries a
        // "Breaking Bad" interest. That is never a useful browsing facet.
        if (configuration.RejectInterestMatchingTitle
            && titleKey.Length > 0
            && string.Equals(key, titleKey, StringComparison.Ordinal))
        {
            return false;
        }

        if (configuration.ExcludeGenreLevelInterests && interest.IsGenreLevel)
        {
            return false;
        }

        if (interest.Category is not null
            && rules.ExcludedCategories.Contains(InterestNormalizer.MatchKey(interest.Category)))
        {
            return false;
        }

        if (rules.IgnoredInterests.Contains(key) || rules.DisabledInterests.Contains(key))
        {
            return false;
        }

        foreach (var pattern in rules.BlockedPatterns)
        {
            if (pattern.IsMatch(interest.Name))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Rewrites an interest through the configured aliases, resolving the target against the
    /// taxonomy so an alias can fold a provider's wording into a canonical interest.
    /// </summary>
    /// <param name="interest">The interest to map.</param>
    /// <param name="rules">The compiled rules.</param>
    /// <returns>The mapped interest, or the original when no alias applies.</returns>
    private InterestRef ApplyAlias(InterestRef interest, FilterRules rules)
    {
        if (rules.Aliases.Count == 0)
        {
            return interest;
        }

        var key = InterestNormalizer.MatchKey(interest.Name);
        if (!rules.Aliases.TryGetValue(key, out var canonicalName))
        {
            return interest;
        }

        return _taxonomy.Resolve(null, canonicalName) ?? interest;
    }

    /// <summary>
    /// The configuration compiled into the form the hot loop needs.
    /// </summary>
    private sealed class FilterRules
    {
        public required HashSet<string> ExcludedCategories { get; init; }

        public required HashSet<string> IgnoredInterests { get; init; }

        public required HashSet<string> DisabledInterests { get; init; }

        public required IReadOnlyDictionary<string, string> Aliases { get; init; }

        public required IReadOnlyList<Regex> BlockedPatterns { get; init; }

        public static FilterRules Build(PluginConfiguration configuration, ILogger logger)
            => new()
            {
                ExcludedCategories = ConfigurationText.ToMatchKeySet(configuration.ExcludedCategories),
                IgnoredInterests = ConfigurationText.ToMatchKeySet(configuration.IgnoredInterests),
                DisabledInterests = ConfigurationText.ToMatchKeySet(configuration.DisabledInterests),
                Aliases = ConfigurationText.ToAliasMap(configuration.InterestAliases),
                BlockedPatterns = CompilePatterns(configuration.BlockedPatterns, logger),
            };

        /// <summary>
        /// Compiles the blocked patterns, skipping any that do not parse. An invalid pattern is an
        /// administrator's typo; it must not stop the rest of the rules from working.
        /// </summary>
        /// <param name="value">The raw configuration value.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>The usable patterns.</returns>
        private static List<Regex> CompilePatterns(string value, ILogger logger)
        {
            var patterns = new List<Regex>();

            foreach (var line in ConfigurationText.ToLines(value))
            {
                try
                {
                    patterns.Add(new Regex(
                        line,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(250)));
                }
                catch (ArgumentException ex)
                {
                    logger.LogWarning("Ignoring invalid blocked pattern {Pattern}: {Reason}", line, ex.Message);
                }
            }

            return patterns;
        }
    }
}
