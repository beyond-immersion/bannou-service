// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Archives;

/// <summary>
/// Generic archive bundle passed from plugin to SDK.
/// The plugin aggregates entries; SDK interprets them.
/// </summary>
public sealed class ArchiveBundle
{
    private readonly Dictionary<string, object> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds an entry to the bundle.
    /// </summary>
    /// <typeparam name="T">The type of data.</typeparam>
    /// <param name="key">The entry key.</param>
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
}
