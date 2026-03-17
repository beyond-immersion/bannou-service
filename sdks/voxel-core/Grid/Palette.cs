namespace BeyondImmersion.Bannou.VoxelCore.Grid;

/// <summary>
/// 256-entry color/material palette shared by all voxels in a grid.
/// Index 0 is always empty (air). Compatible with MagicaVoxel's palette model.
/// The reverse lookup uses the full (Color, MaterialType, Roughness) tuple so that
/// two entries with the same RGB but different materials get separate palette indices.
/// </summary>
public sealed class Palette
{
    private readonly PaletteEntry[] _entries = new PaletteEntry[256];
    private readonly Dictionary<(Color Color, MaterialType Material, float Roughness), byte> _entryIndex = new();
    private int _usedCount;

    /// <summary>
    /// Number of palette entries in use (excluding index 0 which is always empty).
    /// </summary>
    public int UsedCount => _usedCount;

    /// <summary>
    /// Gets the palette entry at the given index.
    /// </summary>
    /// <param name="index">Palette index (0-255).</param>
    /// <returns>The palette entry at that index.</returns>
    public PaletteEntry Get(byte index) => _entries[index];

    /// <summary>
    /// Sets the palette entry at the given index. Updates the reverse lookup.
    /// </summary>
    /// <param name="index">Palette index (1-255). Index 0 cannot be set.</param>
    /// <param name="entry">The palette entry to store.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if index is 0.</exception>
    public void Set(byte index, PaletteEntry entry)
    {
        if (index == 0)
            throw new ArgumentOutOfRangeException(nameof(index), "Index 0 is reserved for empty/air");

        // Remove old reverse entry if overwriting
        var old = _entries[index];
        if (old != PaletteEntry.Empty)
        {
            _entryIndex.Remove((old.Color, old.Material, old.Roughness));
        }

        _entries[index] = entry;
        _entryIndex[(entry.Color, entry.Material, entry.Roughness)] = index;

        if (index > _usedCount)
            _usedCount = index;
    }

    /// <summary>
    /// Returns the index for an existing entry matching the full (Color, MaterialType, Roughness) tuple,
    /// or allocates the next available index if no match exists.
    /// </summary>
    /// <param name="color">RGBA color.</param>
    /// <param name="material">Material type.</param>
    /// <param name="roughness">Surface roughness (0.0-1.0).</param>
    /// <returns>The palette index for this entry.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the palette is full (255 entries used).</exception>
    public byte GetOrAddIndex(Color color, MaterialType material, float roughness = 0.5f)
    {
        var key = (color, material, roughness);
        if (_entryIndex.TryGetValue(key, out var existingIndex))
            return existingIndex;

        if (_usedCount >= 255)
            throw new InvalidOperationException("Palette full: maximum 255 entries (index 0 is reserved for empty)");

        _usedCount++;
        var index = (byte)_usedCount;
        _entries[index] = new PaletteEntry(color, material, roughness);
        _entryIndex[key] = index;
        return index;
    }

    /// <summary>
    /// Checks whether the palette contains an entry with the given color, material, and roughness.
    /// </summary>
    /// <param name="color">RGBA color.</param>
    /// <param name="material">Material type.</param>
    /// <param name="roughness">Surface roughness.</param>
    /// <returns>True if a matching entry exists.</returns>
    public bool Contains(Color color, MaterialType material = MaterialType.Diffuse, float roughness = 0.5f) =>
        _entryIndex.ContainsKey((color, material, roughness));

    /// <summary>
    /// Returns a read-only span over the internal entries array. Index 0 is always empty.
    /// </summary>
    public ReadOnlySpan<PaletteEntry> Entries => _entries.AsSpan();
}
