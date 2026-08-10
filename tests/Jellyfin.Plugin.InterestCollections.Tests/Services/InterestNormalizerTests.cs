using Jellyfin.Plugin.InterestCollections.Services;
using Xunit;

namespace Jellyfin.Plugin.InterestCollections.Tests.Services;

public class InterestNormalizerTests
{
    [Theory]
    [InlineData("psychological thriller")]
    [InlineData("Psychological-Thriller")]
    [InlineData("PSYCHOLOGICAL THRILLER")]
    [InlineData("  Psychological   Thriller  ")]
    [InlineData("psychological_thriller")]
    [InlineData("Psychological/Thriller")]
    public void MatchKey_CollapsesEverySpellingOfTheSameInterest(string spelling)
    {
        Assert.Equal("PSYCHOLOGICALTHRILLER", InterestNormalizer.MatchKey(spelling));
    }

    [Theory]
    [InlineData("Film Noir", "Pokémon")]
    [InlineData("Drug Crime", "Dark Comedy")]
    public void MatchKey_KeepsDistinctInterestsApart(string first, string second)
    {
        Assert.NotEqual(InterestNormalizer.MatchKey(first), InterestNormalizer.MatchKey(second));
    }

    [Theory]
    [InlineData("Pokémon", "POKEMON")]
    [InlineData("Amélie", "AMELIE")]
    [InlineData("Rashōmon", "RASHOMON")]
    public void MatchKey_StripsDiacriticsSoAccentedSpellingsStillMatch(string value, string expected)
    {
        Assert.Equal(expected, InterestNormalizer.MatchKey(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void MatchKey_ReturnsEmptyForInputWithoutContent(string? value)
    {
        Assert.Equal(string.Empty, InterestNormalizer.MatchKey(value));
    }

    [Theory]
    [InlineData("psychological thriller", "Psychological Thriller")]
    [InlineData("PSYCHOLOGICAL   THRILLER", "Psychological Thriller")]
    [InlineData("  drug crime  ", "Drug Crime")]
    [InlineData("tale of the sea", "Tale of the Sea")]
    [InlineData("coming-of-age", "Coming-of-age")]
    public void ToDisplayName_ProducesConsistentCapitalisation(string raw, string expected)
    {
        Assert.Equal(expected, InterestNormalizer.ToDisplayName(raw));
    }

    [Fact]
    public void ToDisplayName_LeavesAcronymsUntouched()
    {
        Assert.Equal("AI Uprising", InterestNormalizer.ToDisplayName("AI Uprising"));
    }

    [Fact]
    public void ToDisplayName_CapitalisesALeadingMinorWord()
    {
        Assert.Equal("The Heist", InterestNormalizer.ToDisplayName("the heist"));
    }

    [Theory]
    [InlineData("Psychological Thriller", "x:psychological-thriller")]
    [InlineData("Sci-Fi", "x:sci-fi")]
    [InlineData("Pokémon", "x:pokemon")]
    public void ToSlugKey_BuildsAStableKeyForInterestsOutsideTheTaxonomy(string raw, string expected)
    {
        Assert.Equal(expected, InterestNormalizer.ToSlugKey(raw));
    }

    [Fact]
    public void ToSlugKey_IsInsensitiveToSpelling()
    {
        Assert.Equal(
            InterestNormalizer.ToSlugKey("drug  trafficking"),
            InterestNormalizer.ToSlugKey("DRUG_TRAFFICKING"));
    }
}
