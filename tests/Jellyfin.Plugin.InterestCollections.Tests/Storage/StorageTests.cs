using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.InterestCollections.Models;
using Jellyfin.Plugin.InterestCollections.Services;
using Jellyfin.Plugin.InterestCollections.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.InterestCollections.Tests.Storage;

public class StorageTests : IDisposable
{
    private static readonly TimeSpan _thirtyDays = TimeSpan.FromDays(30);
    private static readonly TimeSpan _threeDays = TimeSpan.FromDays(3);

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "interest-collections-tests",
        Guid.NewGuid().ToString("N"));

    public StorageTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private InterestCache NewCache()
        => new(_folder, InterestTaxonomy.Shared, NullLogger<InterestCache>.Instance);

    private static IReadOnlyList<InterestRef> TwoInterests() =>
    [
        InterestTaxonomy.Shared.Resolve("in0000182", null)!,
        InterestTaxonomy.Shared.Resolve("in0000053", null)!,
    ];

    [Fact]
    public void Cache_RoundTripsInterestsThroughDisk()
    {
        using (var writer = NewCache())
        {
            writer.Set("imdb-graphql|Series|imdb:tt0903747", "imdb-graphql", 1, TwoInterests());
        }

        using var reader = NewCache();
        Assert.True(reader.TryGet(
            "imdb-graphql|Series|imdb:tt0903747", 1, _thirtyDays, _threeDays, out var interests));
        Assert.Equal(2, interests.Count);
        Assert.Contains(interests, interest => interest.Name == "Psychological Thriller");
    }

    [Fact]
    public void Cache_MissesForAnUnknownKey()
    {
        using var cache = NewCache();
        Assert.False(cache.TryGet("nothing", 1, _thirtyDays, _threeDays, out _));
    }

    [Fact]
    public void Cache_InvalidatesEntriesWrittenByAnOlderProviderShape()
    {
        using var cache = NewCache();
        cache.Set("key", "imdb-graphql", 1, TwoInterests());

        Assert.False(cache.TryGet("key", 2, _thirtyDays, _threeDays, out _));
    }

    [Fact]
    public void Cache_ExpiresEntriesPastTheirLifetime()
    {
        using var cache = NewCache();
        cache.Set("key", "imdb-graphql", 1, TwoInterests());

        Assert.False(cache.TryGet("key", 1, TimeSpan.Zero, TimeSpan.Zero, out _));
    }

    [Fact]
    public void Cache_GivesEmptyAnswersTheShorterNegativeLifetime()
    {
        using var cache = NewCache();
        cache.Set("key", "imdb-graphql", 1, []);

        // Well inside the positive lifetime but outside the negative one.
        Assert.False(cache.TryGet("key", 1, _thirtyDays, TimeSpan.Zero, out _));
        Assert.True(cache.TryGet("key", 1, _thirtyDays, _threeDays, out var interests));
        Assert.Empty(interests);
    }

    [Fact]
    public void Cache_PruneRemovesOnlyExpiredEntries()
    {
        using var cache = NewCache();
        cache.Set("live", "imdb-graphql", 1, TwoInterests());
        cache.Set("stale", "imdb-graphql", 1, []);

        var removed = cache.Prune(_thirtyDays, TimeSpan.Zero);

        Assert.Equal(1, removed);
        Assert.True(cache.TryGet("live", 1, _thirtyDays, _threeDays, out _));
    }

    [Fact]
    public void ProcessedItems_RemembersOnlyTheTagsThePluginWrote()
    {
        var itemId = Guid.NewGuid();
        using var store = new ProcessedItemStore(_folder, NullLogger<ProcessedItemStore>.Instance);

        store.MarkProcessed(itemId, ["Drug Crime", "Dark Comedy"], "fingerprint-a");

        var record = store.Get(itemId);
        Assert.NotNull(record);
        Assert.Equal(["Drug Crime", "Dark Comedy"], record!.AppliedTags);
        Assert.DoesNotContain("Favourites", record.AppliedTags);
    }

    [Fact]
    public void ProcessedItems_ReprocessesWhenTheSettingsFingerprintChanges()
    {
        var itemId = Guid.NewGuid();
        using var store = new ProcessedItemStore(_folder, NullLogger<ProcessedItemStore>.Instance);
        store.MarkProcessed(itemId, ["Drug Crime"], "fingerprint-a");

        Assert.False(store.NeedsProcessing(itemId, "fingerprint-a"));
        Assert.True(store.NeedsProcessing(itemId, "fingerprint-b"));
    }

    [Fact]
    public void ProcessedItems_RetriesAnItemWhoseLookupFailed()
    {
        var itemId = Guid.NewGuid();
        using var store = new ProcessedItemStore(_folder, NullLogger<ProcessedItemStore>.Instance);

        store.MarkProcessed(itemId, ["Drug Crime"], "fingerprint-a");
        store.MarkFailed(itemId);

        Assert.True(store.NeedsProcessing(itemId, "fingerprint-a"));

        // The previously applied tags survive the failure so nothing is stripped.
        Assert.Equal(["Drug Crime"], store.Get(itemId)!.AppliedTags);
    }

    [Fact]
    public void ProcessedItems_DropsRecordsForItemsThatLeftTheLibrary()
    {
        var kept = Guid.NewGuid();
        var removed = Guid.NewGuid();
        using var store = new ProcessedItemStore(_folder, NullLogger<ProcessedItemStore>.Instance);
        store.MarkProcessed(kept, ["Drug Crime"], "f");
        store.MarkProcessed(removed, ["Dark Comedy"], "f");

        var dropped = store.RemoveMissing(new HashSet<Guid> { kept });

        Assert.Equal(1, dropped);
        Assert.NotNull(store.Get(kept));
        Assert.Null(store.Get(removed));
    }

    [Fact]
    public void ManagedCollections_TrackOwnershipAcrossRestarts()
    {
        var boxSetId = Guid.NewGuid();

        using (var store = new ManagedCollectionStore(_folder, NullLogger<ManagedCollectionStore>.Instance))
        {
            store.Register("in0000182", "Psychological Thriller", "Psychological Thriller", boxSetId);
        }

        using var reopened = new ManagedCollectionStore(_folder, NullLogger<ManagedCollectionStore>.Instance);
        Assert.Equal(boxSetId, reopened.GetBoxSetId("in0000182"));
        Assert.True(reopened.IsManaged(boxSetId));
        Assert.False(reopened.IsManaged(Guid.NewGuid()));
    }

    [Fact]
    public void ManagedCollections_ForgetAnUnregisteredCollection()
    {
        using var store = new ManagedCollectionStore(_folder, NullLogger<ManagedCollectionStore>.Instance);
        var boxSetId = Guid.NewGuid();
        store.Register("in0000182", "Psychological Thriller", "Psychological Thriller", boxSetId);

        store.Unregister("in0000182");

        Assert.Null(store.GetBoxSetId("in0000182"));
        Assert.False(store.IsManaged(boxSetId));
    }
}
