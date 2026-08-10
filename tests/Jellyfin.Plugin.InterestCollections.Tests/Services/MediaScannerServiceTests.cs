using System;
using System.Collections.Generic;
using Jellyfin.Plugin.InterestCollections.Configuration;
using Jellyfin.Plugin.InterestCollections.Models;
using Jellyfin.Plugin.InterestCollections.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.InterestCollections.Tests.Services;

public class MediaScannerServiceTests
{
    private static MediaScannerService Build(ILibraryManager libraryManager, PluginConfiguration? configuration = null)
        => new(
            libraryManager,
            () => configuration ?? new PluginConfiguration(),
            NullLogger<MediaScannerService>.Instance);

    private static Movie NewMovie(Guid id, string name, string? imdbId = "tt0111161")
    {
        var movie = new Movie { Id = id, Name = name };

        if (imdbId is not null)
        {
            movie.SetProviderId(MetadataProvider.Imdb, imdbId);
        }

        return movie;
    }

    [Fact]
    public void GetEligibleItems_DeduplicatesTitlesReturnedOncePerLibrary()
    {
        // A title reachable through two libraries comes back twice from GetItemList. Left alone
        // that doubles every provider request and inflates the reported counts — which is exactly
        // what a real 227-title library reported as 417 items before this was fixed.
        var shared = Guid.NewGuid();
        var movie = NewMovie(shared, "2001: A Space Odyssey");
        var other = NewMovie(Guid.NewGuid(), "Se7en");

        var libraryManager = Substitute.For<ILibraryManager>();
        libraryManager.GetItemList(Arg.Any<InternalItemsQuery>()).Returns(new List<BaseItem> { movie, other, movie });

        var items = Build(libraryManager).GetEligibleItems();

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void GetEligibleItems_ReturnsNothingWhenEveryMediaTypeIsDisabled()
    {
        var libraryManager = Substitute.For<ILibraryManager>();
        var configuration = new PluginConfiguration
        {
            ProcessMovies = false,
            ProcessSeries = false,
            ProcessEpisodes = false,
        };

        Assert.Empty(Build(libraryManager, configuration).GetEligibleItems());
        libraryManager.DidNotReceive().GetItemList(Arg.Any<InternalItemsQuery>());
    }

    [Fact]
    public void ToIdentity_CarriesTheProviderIdsThroughUnchanged()
    {
        var movie = NewMovie(Guid.NewGuid(), "Se7en", "tt0114369");
        movie.SetProviderId(MetadataProvider.Tmdb, "807");
        movie.ProductionYear = 1995;
        movie.Tags = ["Favourites"];

        var identity = MediaScannerService.ToIdentity(movie);

        Assert.Equal("tt0114369", identity.ImdbId);
        Assert.Equal("807", identity.TmdbId);
        Assert.Equal(1995, identity.ProductionYear);
        Assert.Equal(MediaKind.Movie, identity.Kind);
        Assert.True(identity.HasProviderId);
        Assert.Contains("Favourites", identity.Tags);
    }

    [Fact]
    public void ToIdentity_ReportsAnItemWithoutUsableIdsSoItCanBeSkipped()
    {
        var identity = MediaScannerService.ToIdentity(NewMovie(Guid.NewGuid(), "Home Video", imdbId: null));

        Assert.False(identity.HasProviderId);
        Assert.Null(identity.ImdbId);
    }

    [Fact]
    public void ToIdentity_RejectsAMalformedImdbId()
    {
        var movie = NewMovie(Guid.NewGuid(), "Broken", "not-an-imdb-id");

        Assert.Null(MediaScannerService.ToIdentity(movie).ImdbId);
    }

    [Fact]
    public void ToIdentity_MapsSeriesToTheSeriesKind()
    {
        var series = new Series { Id = Guid.NewGuid(), Name = "Breaking Bad" };
        series.SetProviderId(MetadataProvider.Imdb, "tt0903747");

        Assert.Equal(MediaKind.Series, MediaScannerService.ToIdentity(series).Kind);
    }

    [Fact]
    public void CacheKey_PrefersTheImdbIdSoRenamingAFileDoesNotInvalidateIt()
    {
        var first = MediaScannerService.ToIdentity(NewMovie(Guid.NewGuid(), "Se7en", "tt0114369"));
        var second = MediaScannerService.ToIdentity(NewMovie(Guid.NewGuid(), "Seven", "tt0114369"));

        Assert.Equal(first.GetCacheKey("imdb-graphql"), second.GetCacheKey("imdb-graphql"));
    }

    [Fact]
    public void CacheKey_SeparatesProviders()
    {
        var identity = MediaScannerService.ToIdentity(NewMovie(Guid.NewGuid(), "Se7en", "tt0114369"));

        Assert.NotEqual(identity.GetCacheKey("imdb-graphql"), identity.GetCacheKey("tmdb-keywords"));
    }
}
