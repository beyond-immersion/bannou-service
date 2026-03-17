using BeyondImmersion.Bannou.VoxelBuilder.Core;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelBuilder.Tests.Core;

/// <summary>
/// Unit tests for the <see cref="BoundedDeque{T}"/> internal bounded LIFO deque.
/// </summary>
public class BoundedDequeTests
{
    [Fact]
    public void NewDeque_IsEmpty()
    {
        var deque = new BoundedDeque<int>(10);
        Assert.True(deque.IsEmpty);
        Assert.Equal(0, deque.Count);
    }

    [Fact]
    public void Push_IncreasesCount()
    {
        var deque = new BoundedDeque<int>(10);
        deque.Push(42);
        Assert.False(deque.IsEmpty);
        Assert.Equal(1, deque.Count);
    }

    [Fact]
    public void Pop_ReturnsLastPushed()
    {
        var deque = new BoundedDeque<int>(10);
        deque.Push(1);
        deque.Push(2);
        deque.Push(3);

        Assert.Equal(3, deque.Pop());
        Assert.Equal(2, deque.Pop());
        Assert.Equal(1, deque.Pop());
    }

    [Fact]
    public void Pop_DecreasesCount()
    {
        var deque = new BoundedDeque<int>(10);
        deque.Push(1);
        deque.Push(2);
        deque.Pop();
        Assert.Equal(1, deque.Count);
    }

    [Fact]
    public void Pop_WhenEmpty_ThrowsInvalidOperation()
    {
        var deque = new BoundedDeque<int>(10);
        Assert.Throws<InvalidOperationException>(() => deque.Pop());
    }

    [Fact]
    public void Peek_ReturnsTopWithoutRemoving()
    {
        var deque = new BoundedDeque<int>(10);
        deque.Push(42);
        Assert.Equal(42, deque.Peek());
        Assert.Equal(1, deque.Count);
    }

    [Fact]
    public void Peek_WhenEmpty_ThrowsInvalidOperation()
    {
        var deque = new BoundedDeque<int>(10);
        Assert.Throws<InvalidOperationException>(() => deque.Peek());
    }

    [Fact]
    public void Push_AtCapacity_EvictsOldest()
    {
        var deque = new BoundedDeque<int>(3);
        deque.Push(1);
        deque.Push(2);
        deque.Push(3);
        Assert.Equal(3, deque.Count);

        deque.Push(4);
        Assert.Equal(3, deque.Count);

        // Oldest (1) was evicted; 4, 3, 2 remain
        Assert.Equal(4, deque.Pop());
        Assert.Equal(3, deque.Pop());
        Assert.Equal(2, deque.Pop());
        Assert.True(deque.IsEmpty);
    }

    [Fact]
    public void Push_MultiplePastCapacity_EvictsMultiple()
    {
        var deque = new BoundedDeque<int>(2);
        deque.Push(1);
        deque.Push(2);
        deque.Push(3);
        deque.Push(4);
        deque.Push(5);

        Assert.Equal(2, deque.Count);
        Assert.Equal(5, deque.Pop());
        Assert.Equal(4, deque.Pop());
    }

    [Fact]
    public void Clear_EmptiesDeque()
    {
        var deque = new BoundedDeque<int>(10);
        deque.Push(1);
        deque.Push(2);
        deque.Push(3);
        deque.Clear();

        Assert.True(deque.IsEmpty);
        Assert.Equal(0, deque.Count);
    }

    [Fact]
    public void MaxCapacity_IsPreserved()
    {
        var deque = new BoundedDeque<int>(42);
        Assert.Equal(42, deque.MaxCapacity);
    }

    [Fact]
    public void Push_CapacityOne_KeepsOnlyLatest()
    {
        var deque = new BoundedDeque<int>(1);
        deque.Push(1);
        deque.Push(2);
        deque.Push(3);

        Assert.Equal(1, deque.Count);
        Assert.Equal(3, deque.Pop());
    }
}
