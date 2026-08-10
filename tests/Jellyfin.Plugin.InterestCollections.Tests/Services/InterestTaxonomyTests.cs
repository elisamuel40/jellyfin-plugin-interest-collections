using System.Linq;
using Jellyfin.Plugin.InterestCollections.Services;
using Xunit;

namespace Jellyfin.Plugin.InterestCollections.Tests.Services;

public class InterestTaxonomyTests
{
    private static InterestTaxonomy Taxonomy => InterestTaxonomy.Shared;

    [Fact]
    public void Shared_LoadsTheCompleteBundledTaxonomy()
    {
        Assert.Equal(313, Taxonomy.All.Count);
        Assert.Equal(26, Taxonomy.Categories.Count);
    }

    [Fact]
    public void Shared_HasNoDuplicateInterestIds()
    {
        var ids = Taxonomy.All.Select(definition => definition.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Theory]
    [InlineData("in0000182", "Psychological Thriller", "Thriller")]
    [InlineData("in0000053", "Drug Crime", "Crime")]
    [InlineData("in0000035", "Dark Comedy", "Comedy")]
    [InlineData("in0000183", "Serial Killer", "Thriller")]
    public void TryGetById_ResolvesKnownInterests(string id, string name, string category)
    {
        Assert.True(Taxonomy.TryGetById(id, out var definition));
        Assert.NotNull(definition);
        Assert.Equal(name, definition!.Name);
        Assert.Equal(category, definition.Category);
    }

    [Theory]
    [InlineData("psychological thriller")]
    [InlineData("PSYCHOLOGICAL-THRILLER")]
    public void TryGetByName_IgnoresSpelling(string spelling)
    {
        Assert.True(Taxonomy.TryGetByName(spelling, out var definition));
        Assert.Equal("in0000182", definition!.Id);
    }

    [Fact]
    public void GenreLevelInterestsAreFlagged()
    {
        Assert.True(Taxonomy.TryGetByName("Thriller", out var genreLevel));
        Assert.True(genreLevel!.IsGenreLevel);

        Assert.True(Taxonomy.TryGetByName("Psychological Thriller", out var specific));
        Assert.False(specific!.IsGenreLevel);
    }

    [Fact]
    public void Resolve_PrefersTheIdOverTheSuppliedName()
    {
        var resolved = Taxonomy.Resolve("in0000182", "some drifting provider spelling");

        Assert.NotNull(resolved);
        Assert.Equal("in0000182", resolved!.Key);
        Assert.Equal("Psychological Thriller", resolved.Name);
        Assert.Equal("Thriller", resolved.Category);
    }

    [Fact]
    public void Resolve_FallsBackToTheNameWhenTheIdIsUnknown()
    {
        var resolved = Taxonomy.Resolve(null, "drug crime");

        Assert.NotNull(resolved);
        Assert.Equal("in0000053", resolved!.Key);
        Assert.Equal("Drug Crime", resolved.Name);
    }

    [Fact]
    public void Resolve_KeepsInterestsThatAreNotInTheTaxonomy()
    {
        var resolved = Taxonomy.Resolve(null, "corporate espionage");

        Assert.NotNull(resolved);
        Assert.Equal("x:corporate-espionage", resolved!.Key);
        Assert.Equal("Corporate Espionage", resolved.Name);
        Assert.Null(resolved.Category);
        Assert.False(resolved.IsGenreLevel);
    }

    [Fact]
    public void Resolve_ReturnsNullWhenNothingUsableRemains()
    {
        Assert.Null(Taxonomy.Resolve(null, "   "));
        Assert.Null(Taxonomy.Resolve(null, null));
    }

    [Fact]
    public void InterestRefsWithTheSameKeyAreEqual()
    {
        var first = Taxonomy.Resolve("in0000182", null);
        var second = Taxonomy.Resolve(null, "Psychological-Thriller");

        Assert.Equal(first, second);
    }
}
