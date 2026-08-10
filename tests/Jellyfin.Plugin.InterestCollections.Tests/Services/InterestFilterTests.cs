using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.InterestCollections.Configuration;
using Jellyfin.Plugin.InterestCollections.Models;
using Jellyfin.Plugin.InterestCollections.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.InterestCollections.Tests.Services;

public class InterestFilterTests
{
    private static readonly InterestTaxonomy _taxonomy = InterestTaxonomy.Shared;

    private static InterestFilter Build(PluginConfiguration configuration)
        => new(() => configuration, _taxonomy, NullLogger<InterestFilter>.Instance);

    private static PluginConfiguration Permissive() => new()
    {
        ExcludedCategories = string.Empty,
        ExcludeGenreLevelInterests = false,
        RejectInterestMatchingTitle = false,
    };

    private static MediaIdentity Title(string name) => new()
    {
        ItemId = Guid.NewGuid(),
        Name = name,
        Kind = MediaKind.Series,
        ImdbId = "tt0903747",
    };

    private static IReadOnlyList<InterestRef> Interests(params string[] names)
        => names.Select(name => _taxonomy.Resolve(null, name)!).ToList();

    private static List<string> Names(IReadOnlyList<InterestRef> interests)
        => interests.Select(interest => interest.Name).ToList();

    [Fact]
    public void Apply_KeepsEverythingWhenNothingIsFilteredOut()
    {
        var filter = Build(Permissive());

        var result = filter.Apply(
            Title("Breaking Bad"),
            Interests("Dark Comedy", "Drug Crime", "Psychological Thriller"));

        Assert.Equal(["Dark Comedy", "Drug Crime", "Psychological Thriller"], Names(result));
    }

    [Fact]
    public void Apply_DropsAnInterestNamedAfterTheTitleItself()
    {
        // IMDb genuinely returns a "Breaking Bad" franchise interest for Breaking Bad.
        var configuration = Permissive();
        configuration.RejectInterestMatchingTitle = true;

        var result = Build(configuration).Apply(
            Title("Breaking Bad"),
            Interests("Breaking Bad", "Drug Crime"));

        Assert.Equal(["Drug Crime"], Names(result));
    }

    [Fact]
    public void Apply_DropsGenreLevelInterestsThatDuplicateJellyfinGenres()
    {
        var configuration = Permissive();
        configuration.ExcludeGenreLevelInterests = true;

        var result = Build(configuration).Apply(
            Title("Se7en"),
            Interests("Crime", "Drama", "Thriller", "Serial Killer"));

        Assert.Equal(["Serial Killer"], Names(result));
    }

    [Fact]
    public void Apply_DropsExcludedCategories()
    {
        var configuration = Permissive();
        configuration.ExcludedCategories = "Language\nFranchise";

        var result = Build(configuration).Apply(
            Title("Parasite"),
            Interests("Korean", "Star Wars", "Dark Comedy"));

        Assert.Equal(["Dark Comedy"], Names(result));
    }

    [Fact]
    public void Apply_HonoursTheIgnoreList()
    {
        var configuration = Permissive();
        configuration.IgnoredInterests = "dark comedy\n  TRAGEDY  ";

        var result = Build(configuration).Apply(
            Title("Breaking Bad"),
            Interests("Dark Comedy", "Tragedy", "Drug Crime"));

        Assert.Equal(["Drug Crime"], Names(result));
    }

    [Fact]
    public void Apply_HonoursBlockedPatterns()
    {
        var configuration = Permissive();
        configuration.BlockedPatterns = "^Holiday";

        var result = Build(configuration).Apply(
            Title("Die Hard"),
            Interests("Holiday Comedy", "Action Epic"));

        Assert.Equal(["Action Epic"], Names(result));
    }

    [Fact]
    public void Apply_IgnoresAnInvalidPatternInsteadOfFailing()
    {
        var configuration = Permissive();
        configuration.BlockedPatterns = "([unclosed\n^Holiday";

        var result = Build(configuration).Apply(
            Title("Die Hard"),
            Interests("Holiday Comedy", "Action Epic"));

        Assert.Equal(["Action Epic"], Names(result));
    }

    [Fact]
    public void Apply_MapsAliasesOntoCanonicalInterests()
    {
        var configuration = Permissive();
        configuration.InterestAliases = "Drug Trafficking = Drug Crime\nNarcotics Trade = Drug Crime";

        var result = Build(configuration).Apply(
            Title("Narcos"),
            Interests("Drug Trafficking", "Narcotics Trade"));

        // Both aliases fold into one canonical interest, and the duplicate collapses.
        Assert.Equal(["Drug Crime"], Names(result));
        Assert.Equal("in0000053", result[0].Key);
    }

    [Fact]
    public void Apply_HonoursInterestsDisabledFromTheInterestManager()
    {
        var configuration = Permissive();
        configuration.DisabledInterests = "Psychological Thriller";

        var result = Build(configuration).Apply(
            Title("Se7en"),
            Interests("Psychological Thriller", "Serial Killer"));

        Assert.Equal(["Serial Killer"], Names(result));
    }

    [Fact]
    public void Apply_RemovesDuplicatesThatDifferOnlyInSpelling()
    {
        var result = Build(Permissive()).Apply(
            Title("Se7en"),
            Interests("Serial Killer", "serial-killer", "SERIAL KILLER"));

        Assert.Single(result);
    }

    [Fact]
    public void Apply_ReturnsNothingForAnEmptyProviderAnswer()
    {
        Assert.Empty(Build(Permissive()).Apply(Title("Se7en"), []));
    }

    [Fact]
    public void Apply_UsesTheDefaultConfigurationSensibly()
    {
        // The shipped defaults: Language excluded, genre-level interests dropped, title-name
        // interests rejected.
        var result = Build(new PluginConfiguration()).Apply(
            Title("Breaking Bad"),
            Interests("Breaking Bad", "Dark Comedy", "Drug Crime", "Crime", "Drama", "Spanish"));

        Assert.Equal(["Dark Comedy", "Drug Crime"], Names(result));
    }
}
