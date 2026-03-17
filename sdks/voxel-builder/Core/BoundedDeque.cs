namespace BeyondImmersion.Bannou.VoxelBuilder.Core;

/// <summary>
/// Bounded LIFO deque: push to top (newest), pop from top (undo), evict from bottom
/// (oldest) when count exceeds MaxDepth. Backed by <see cref="LinkedList{T}"/>
/// for O(1) push/pop/evict. Standard Stack&lt;T&gt; cannot evict from the bottom.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
internal sealed class BoundedDeque<T>
{
    private readonly LinkedList<T> _list = new();

    /// <summary>Maximum number of items before bottom-eviction.</summary>
    public int MaxCapacity { get; }

    /// <summary>Current number of items.</summary>
    public int Count => _list.Count;

    /// <summary>
    /// Creates a new bounded deque with the specified maximum capacity.
    /// </summary>
    /// <param name="maxCapacity">Maximum items before bottom-eviction.</param>
    public BoundedDeque(int maxCapacity)
    {
        MaxCapacity = maxCapacity;
    }

    /// <summary>
    /// Push an item to the top (newest). Evicts the bottom (oldest) item if at capacity.
    /// </summary>
    /// <param name="item">The item to push.</param>
    public void Push(T item)
    {
        _list.AddFirst(item);
        while (_list.Count > MaxCapacity)
            _list.RemoveLast();
    }

    /// <summary>
    /// Pop the top (newest) item.
    /// </summary>
    /// <returns>The top item.</returns>
    /// <exception cref="InvalidOperationException">Thrown if empty.</exception>
    public T Pop()
    {
        if (_list.First == null)
            throw new InvalidOperationException("Deque is empty");

        var value = _list.First.Value;
        _list.RemoveFirst();
        return value;
    }

    /// <summary>
    /// Peek at the top item without removing it.
    /// </summary>
    /// <returns>The top item.</returns>
    /// <exception cref="InvalidOperationException">Thrown if empty.</exception>
    public T Peek()
    {
        if (_list.First == null)
            throw new InvalidOperationException("Deque is empty");
        return _list.First.Value;
    }

    /// <summary>Clear all items.</summary>
    public void Clear() => _list.Clear();

    /// <summary>Whether the deque is empty.</summary>
    public bool IsEmpty => _list.Count == 0;
}
