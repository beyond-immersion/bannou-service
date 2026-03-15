using Microsoft.Extensions.Logging.Abstractions;

namespace BeyondImmersion.BannouService.Tests;

/// <summary>
/// Tests for <see cref="EventBatcher{TEntry}"/> (Mode 1 — accumulating, append-all)
/// and <see cref="DeduplicatingEventBatcher{TKey,TEntry}"/> (Mode 2 — last-write-wins).
/// </summary>
public class EventBatcherTests
{
    private record TestEntry(Guid Id, string Name, DateTimeOffset Timestamp);

    private static EventBatcher<TestEntry> CreateAccumulating(
        out List<(List<TestEntry> Entries, DateTimeOffset WindowStart)> captures)
    {
        var capturedFlushes = new List<(List<TestEntry>, DateTimeOffset)>();
        captures = capturedFlushes;

        return new EventBatcher<TestEntry>(
            async (entries, windowStart, ct) =>
            {
                capturedFlushes.Add((entries, windowStart));
                await Task.CompletedTask;
            },
            e => e.Timestamp,
            NullLogger.Instance);
    }

    private static DeduplicatingEventBatcher<Guid, TestEntry> CreateDeduplicating(
        out List<(List<TestEntry> Entries, DateTimeOffset WindowStart)> captures)
    {
        var capturedFlushes = new List<(List<TestEntry>, DateTimeOffset)>();
        captures = capturedFlushes;

        return new DeduplicatingEventBatcher<Guid, TestEntry>(
            async (entries, windowStart, ct) =>
            {
                capturedFlushes.Add((entries, windowStart));
                await Task.CompletedTask;
            },
            e => e.Timestamp,
            NullLogger.Instance);
    }

    // =========================================================================
    // EventBatcher<TEntry> (Mode 1 — Accumulating)
    // =========================================================================

    [Fact]
    public async Task Accumulating_Add_SingleEntry_AccumulatesForFlush()
    {
        var batcher = CreateAccumulating(out var captures);
        var entry = new TestEntry(Guid.NewGuid(), "item-1", DateTimeOffset.UtcNow);

        batcher.Add(entry);
        await batcher.FlushAsync(CancellationToken.None);

        Assert.Single(captures);
        Assert.Single(captures[0].Entries);
        Assert.Equal("item-1", captures[0].Entries[0].Name);
    }

    [Fact]
    public async Task Accumulating_Add_MultipleEntries_AllPreserved()
    {
        var batcher = CreateAccumulating(out var captures);

        batcher.Add(new TestEntry(Guid.NewGuid(), "a", DateTimeOffset.UtcNow));
        batcher.Add(new TestEntry(Guid.NewGuid(), "b", DateTimeOffset.UtcNow));
        batcher.Add(new TestEntry(Guid.NewGuid(), "c", DateTimeOffset.UtcNow));
        await batcher.FlushAsync(CancellationToken.None);

        Assert.Single(captures);
        Assert.Equal(3, captures[0].Entries.Count);
    }

    [Fact]
    public async Task Accumulating_Add_SameDataTwice_BothPreserved()
    {
        var batcher = CreateAccumulating(out var captures);
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        batcher.Add(new TestEntry(id, "credit-10g", now));
        batcher.Add(new TestEntry(id, "credit-5g", now.AddMilliseconds(1)));
        await batcher.FlushAsync(CancellationToken.None);

        Assert.Single(captures);
        Assert.Equal(2, captures[0].Entries.Count);
        Assert.Contains(captures[0].Entries, e => e.Name == "credit-10g");
        Assert.Contains(captures[0].Entries, e => e.Name == "credit-5g");
    }

    [Fact]
    public async Task Accumulating_AddRange_MultipleEntries_AllAccumulated()
    {
        var batcher = CreateAccumulating(out var captures);
        var entries = new[]
        {
            new TestEntry(Guid.NewGuid(), "batch-1", DateTimeOffset.UtcNow),
            new TestEntry(Guid.NewGuid(), "batch-2", DateTimeOffset.UtcNow),
            new TestEntry(Guid.NewGuid(), "batch-3", DateTimeOffset.UtcNow),
        };

        batcher.AddRange(entries);
        await batcher.FlushAsync(CancellationToken.None);

        Assert.Single(captures);
        Assert.Equal(3, captures[0].Entries.Count);
    }

    [Fact]
    public async Task Accumulating_FlushAsync_EmptyBatcher_NoCallback()
    {
        var batcher = CreateAccumulating(out var captures);

        await batcher.FlushAsync(CancellationToken.None);

        Assert.Empty(captures);
    }

    [Fact]
    public async Task Accumulating_FlushAsync_WithEntries_InvokesCallbackAndClears()
    {
        var batcher = CreateAccumulating(out var captures);

        batcher.Add(new TestEntry(Guid.NewGuid(), "first", DateTimeOffset.UtcNow));
        await batcher.FlushAsync(CancellationToken.None);

        Assert.Single(captures);
        Assert.True(batcher.IsEmpty);
    }

    [Fact]
    public async Task Accumulating_FlushAsync_AtomicDrain_NewAddsGoToNextBatch()
    {
        var batcher = CreateAccumulating(out var captures);

        batcher.Add(new TestEntry(Guid.NewGuid(), "batch-1", DateTimeOffset.UtcNow));
        await batcher.FlushAsync(CancellationToken.None);

        batcher.Add(new TestEntry(Guid.NewGuid(), "batch-2", DateTimeOffset.UtcNow));
        await batcher.FlushAsync(CancellationToken.None);

        Assert.Equal(2, captures.Count);
        Assert.Single(captures[0].Entries);
        Assert.Equal("batch-1", captures[0].Entries[0].Name);
        Assert.Single(captures[1].Entries);
        Assert.Equal("batch-2", captures[1].Entries[0].Name);
    }

    [Fact]
    public async Task Accumulating_FlushAsync_SortsChronologically()
    {
        var batcher = CreateAccumulating(out var captures);
        var now = DateTimeOffset.UtcNow;

        batcher.Add(new TestEntry(Guid.NewGuid(), "third", now.AddSeconds(3)));
        batcher.Add(new TestEntry(Guid.NewGuid(), "first", now.AddSeconds(1)));
        batcher.Add(new TestEntry(Guid.NewGuid(), "second", now.AddSeconds(2)));
        await batcher.FlushAsync(CancellationToken.None);

        Assert.Single(captures);
        Assert.Equal("first", captures[0].Entries[0].Name);
        Assert.Equal("second", captures[0].Entries[1].Name);
        Assert.Equal("third", captures[0].Entries[2].Name);
    }

    [Fact]
    public async Task Accumulating_FlushAsync_ConcurrentAdds_NoLostEntries()
    {
        var batcher = CreateAccumulating(out var captures);
        var entryCount = 1000;

        var tasks = Enumerable.Range(0, entryCount).Select(i =>
            Task.Run(() => batcher.Add(
                new TestEntry(Guid.NewGuid(), $"entry-{i}", DateTimeOffset.UtcNow))));
        await Task.WhenAll(tasks);

        await batcher.FlushAsync(CancellationToken.None);

        Assert.Single(captures);
        Assert.Equal(entryCount, captures[0].Entries.Count);
    }

    // =========================================================================
    // DeduplicatingEventBatcher<TKey, TEntry> (Mode 2 — Last-Write-Wins)
    // =========================================================================

    [Fact]
    public async Task Deduplicating_Add_SingleEntry_AccumulatesForFlush()
    {
        var batcher = CreateDeduplicating(out var captures);
        var id = Guid.NewGuid();

        batcher.Add(id, new TestEntry(id, "item-1", DateTimeOffset.UtcNow));
        await batcher.FlushAsync(CancellationToken.None);

        Assert.Single(captures);
        Assert.Single(captures[0].Entries);
        Assert.Equal("item-1", captures[0].Entries[0].Name);
    }

    [Fact]
    public async Task Deduplicating_Add_SameKey_LastWriteWins()
    {
        var batcher = CreateDeduplicating(out var captures);
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        batcher.Add(id, new TestEntry(id, "durability-90", now));
        batcher.Add(id, new TestEntry(id, "durability-85", now.AddMilliseconds(1)));
        batcher.Add(id, new TestEntry(id, "durability-80", now.AddMilliseconds(2)));
        await batcher.FlushAsync(CancellationToken.None);

        Assert.Single(captures);
        Assert.Single(captures[0].Entries);
        Assert.Equal("durability-80", captures[0].Entries[0].Name);
    }

    [Fact]
    public async Task Deduplicating_Add_DifferentKeys_BothPreserved()
    {
        var batcher = CreateDeduplicating(out var captures);
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        batcher.Add(id1, new TestEntry(id1, "item-a", now));
        batcher.Add(id2, new TestEntry(id2, "item-b", now.AddMilliseconds(1)));
        await batcher.FlushAsync(CancellationToken.None);

        Assert.Single(captures);
        Assert.Equal(2, captures[0].Entries.Count);
    }

    [Fact]
    public async Task Deduplicating_FlushAsync_EmptyBatcher_NoCallback()
    {
        var batcher = CreateDeduplicating(out var captures);

        await batcher.FlushAsync(CancellationToken.None);

        Assert.Empty(captures);
    }

    [Fact]
    public async Task Deduplicating_FlushAsync_WithEntries_InvokesCallbackAndClears()
    {
        var batcher = CreateDeduplicating(out var captures);
        var id = Guid.NewGuid();

        batcher.Add(id, new TestEntry(id, "test", DateTimeOffset.UtcNow));
        await batcher.FlushAsync(CancellationToken.None);

        Assert.Single(captures);
        Assert.True(batcher.IsEmpty);
    }

    [Fact]
    public async Task Deduplicating_FlushAsync_AtomicSwap_NewAddsGoToFreshDictionary()
    {
        var batcher = CreateDeduplicating(out var captures);
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        batcher.Add(id1, new TestEntry(id1, "batch-1", DateTimeOffset.UtcNow));
        await batcher.FlushAsync(CancellationToken.None);

        batcher.Add(id2, new TestEntry(id2, "batch-2", DateTimeOffset.UtcNow));
        await batcher.FlushAsync(CancellationToken.None);

        Assert.Equal(2, captures.Count);
        Assert.Single(captures[0].Entries);
        Assert.Equal("batch-1", captures[0].Entries[0].Name);
        Assert.Single(captures[1].Entries);
        Assert.Equal("batch-2", captures[1].Entries[0].Name);
    }

    [Fact]
    public async Task Deduplicating_FlushAsync_SortsChronologically()
    {
        var batcher = CreateDeduplicating(out var captures);
        var now = DateTimeOffset.UtcNow;

        batcher.Add(Guid.NewGuid(), new TestEntry(Guid.NewGuid(), "third", now.AddSeconds(3)));
        batcher.Add(Guid.NewGuid(), new TestEntry(Guid.NewGuid(), "first", now.AddSeconds(1)));
        batcher.Add(Guid.NewGuid(), new TestEntry(Guid.NewGuid(), "second", now.AddSeconds(2)));
        await batcher.FlushAsync(CancellationToken.None);

        Assert.Single(captures);
        Assert.Equal("first", captures[0].Entries[0].Name);
        Assert.Equal("second", captures[0].Entries[1].Name);
        Assert.Equal("third", captures[0].Entries[2].Name);
    }

    [Fact]
    public async Task Deduplicating_FlushAsync_ConcurrentAdds_NoLostEntries()
    {
        var batcher = CreateDeduplicating(out var captures);
        var entryCount = 1000;

        var tasks = Enumerable.Range(0, entryCount).Select(i =>
        {
            var id = Guid.NewGuid();
            return Task.Run(() => batcher.Add(
                id, new TestEntry(id, $"entry-{i}", DateTimeOffset.UtcNow)));
        });
        await Task.WhenAll(tasks);

        await batcher.FlushAsync(CancellationToken.None);

        Assert.Single(captures);
        Assert.Equal(entryCount, captures[0].Entries.Count);
    }

    // =========================================================================
    // IFlushable contract
    // =========================================================================

    [Fact]
    public void Accumulating_ImplementsIFlushable()
    {
        var batcher = CreateAccumulating(out _);
        Assert.IsAssignableFrom<IFlushable>(batcher);
    }

    [Fact]
    public void Deduplicating_ImplementsIFlushable()
    {
        var batcher = CreateDeduplicating(out _);
        Assert.IsAssignableFrom<IFlushable>(batcher);
    }

    [Fact]
    public void Accumulating_IsEmpty_TrueWhenNew()
    {
        var batcher = CreateAccumulating(out _);
        Assert.True(batcher.IsEmpty);
    }

    [Fact]
    public void Accumulating_IsEmpty_FalseAfterAdd()
    {
        var batcher = CreateAccumulating(out _);
        batcher.Add(new TestEntry(Guid.NewGuid(), "test", DateTimeOffset.UtcNow));
        Assert.False(batcher.IsEmpty);
    }

    [Fact]
    public void Deduplicating_IsEmpty_TrueWhenNew()
    {
        var batcher = CreateDeduplicating(out _);
        Assert.True(batcher.IsEmpty);
    }

    [Fact]
    public void Deduplicating_IsEmpty_FalseAfterAdd()
    {
        var batcher = CreateDeduplicating(out _);
        var id = Guid.NewGuid();
        batcher.Add(id, new TestEntry(id, "test", DateTimeOffset.UtcNow));
        Assert.False(batcher.IsEmpty);
    }

    // =========================================================================
    // Window tracking
    // =========================================================================

    [Fact]
    public async Task Accumulating_FlushAsync_WindowStartReflectsAccumulationPeriod()
    {
        var batcher = CreateAccumulating(out var captures);
        var beforeAdd = DateTimeOffset.UtcNow;

        batcher.Add(new TestEntry(Guid.NewGuid(), "test", DateTimeOffset.UtcNow));
        await batcher.FlushAsync(CancellationToken.None);

        Assert.Single(captures);
        Assert.True(captures[0].WindowStart <= beforeAdd);
    }

    [Fact]
    public async Task Deduplicating_FlushAsync_WindowStartReflectsAccumulationPeriod()
    {
        var batcher = CreateDeduplicating(out var captures);
        var beforeAdd = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        batcher.Add(id, new TestEntry(id, "test", DateTimeOffset.UtcNow));
        await batcher.FlushAsync(CancellationToken.None);

        Assert.Single(captures);
        Assert.True(captures[0].WindowStart <= beforeAdd);
    }
}
