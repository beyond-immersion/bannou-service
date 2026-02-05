// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Archives;

/// <summary>
/// A generic archive bundle that holds entries from various L4 services.
/// The plugin aggregates data from services and populates this bundle;
/// the SDK extracts what it needs using opaque string keys.
/// </summary>
/// <remarks>
/// This decoupling ensures the SDK has no compile-time dependency on plugin types.
/// The SDK defines what keys it understands; unknown keys are silently ignored.
/// This enables forward compatibility when new archive sources are added.
/// </remarks>
public sealed class ArchiveBundle
{
    private readonly Dictionary<string, object> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds an entry to the bundle.
    /// </summary>
    /// <typeparam name="T">The type of data.</typeparam>
    /// <param name="key">The entry key (e.g., "character-personality", "character-encounter").</param>
    /// <param name="data">The data to store.</param>
    public void AddEntry<T>(string key, T data) where T : class
    {
        _entries[key] = data;
    }

    /// <summary>
    /// Attempts to retrieve an entry by key.
    /// </summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <param name="key">The entry key.</param>
    /// <param name="data">The data if found and of correct type; otherwise null.</param>
    /// <returns>True if the entry was found and is of the correct type.</returns>
    public bool TryGetEntry<T>(string key, out T? data) where T : class
    {
        if (_entries.TryGetValue(key, out var obj) && obj is T typed)
        {
            data = typed;
            return true;
        }
        data = null;
        return false;
    }

    /// <summary>
    /// Gets an entry by key, throwing if not found.
    /// </summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <param name="key">The entry key.</param>
    /// <returns>The data.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the key is not found.</exception>
    /// <exception cref="InvalidCastException">Thrown if the data is not of the expected type.</exception>
    public T GetEntry<T>(string key) where T : class
    {
        if (!_entries.TryGetValue(key, out var obj))
        {
            throw new KeyNotFoundException($"Archive entry '{key}' not found.");
        }
        if (obj is not T typed)
        {
            throw new InvalidCastException(
                $"Archive entry '{key}' is {obj.GetType().Name}, not {typeof(T).Name}.");
        }
        return typed;
    }

    /// <summary>
    /// Checks if the bundle contains an entry with the given key.
    /// </summary>
    /// <param name="key">The entry key.</param>
    /// <returns>True if the key exists.</returns>
    public bool ContainsKey(string key) => _entries.ContainsKey(key);

    /// <summary>
    /// Gets all keys in the bundle.
    /// </summary>
    public IEnumerable<string> Keys => _entries.Keys;

    /// <summary>
    /// Gets the count of entries in the bundle.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Removes an entry from the bundle.
    /// </summary>
    /// <param name="key">The entry key.</param>
    /// <returns>True if the entry was removed.</returns>
    public bool RemoveEntry(string key) => _entries.Remove(key);

    /// <summary>
    /// Clears all entries from the bundle.
    /// </summary>
    public void Clear() => _entries.Clear();
}
