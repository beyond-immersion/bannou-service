using System.Text.Json.Serialization;

namespace BeyondImmersion.Bannou.MusicTheory.Melody;

/// <summary>
/// Motif usage categories for organizing and selecting motifs.
/// </summary>
public enum MotifCategory
{
    /// <summary>Opening/thematic motif for starting sections</summary>
    Opening,

    /// <summary>Ornamental motif (turns, mordents, etc.)</summary>
    Ornament,

    /// <summary>Cadential motif for phrase endings</summary>
    Cadential,

    /// <summary>Transitional/connecting motif</summary>
    Transition,

    /// <summary>Developmental motif for variation</summary>
    Development,

    /// <summary>Rhythmic pattern motif</summary>
    Rhythmic,

    /// <summary>Melodic fragment (scalar, arpeggiated)</summary>
    Melodic
}

/// <summary>
/// A named motif with metadata for cataloging and retrieval.
/// Combines the abstract Motif (intervals + durations) with identification and context.
/// </summary>
public sealed class NamedMotif
{
    /// <summary>
    /// Unique identifier for the motif.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; }

    /// <summary>
    /// Human-readable name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; }

    /// <summary>
    /// The underlying motif data (intervals and durations).
    /// </summary>
    [JsonPropertyName("motif")]
    public Motif Motif { get; }

    /// <summary>
    /// Usage category.
    /// </summary>
    [JsonPropertyName("category")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MotifCategory Category { get; }

    /// <summary>
    /// Associated style ID (null = universal).
    /// </summary>
    [JsonPropertyName("styleId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StyleId { get; }

    /// <summary>
    /// Optional description.
    /// </summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; }

    /// <summary>
    /// Selection weight for random choice (higher = more likely).
    /// </summary>
    [JsonPropertyName("weight")]
    public double Weight { get; }

    /// <summary>
    /// Creates a named motif.
    /// </summary>
    /// <param name="id">Unique identifier.</param>
    /// <param name="name">Display name.</param>
    /// <param name="motif">The underlying motif.</param>
    /// <param name="category">Usage category.</param>
    /// <param name="styleId">Associated style (null = universal).</param>
    /// <param name="description">Optional description.</param>
    /// <param name="weight">Selection weight (default 1.0).</param>
    [JsonConstructor]
    public NamedMotif(
        string id,
        string name,
        Motif motif,
        MotifCategory category,
        string? styleId = null,
        string? description = null,
        double weight = 1.0)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Motif ID cannot be empty", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Motif name cannot be empty", nameof(name));
        }

        Id = id;
        Name = name;
        Motif = motif ?? throw new ArgumentNullException(nameof(motif));
        Category = category;
        StyleId = styleId;
        Description = description;
        Weight = weight > 0 ? weight : 1.0;
    }

    /// <summary>
    /// Creates a named motif from intervals and durations.
    /// </summary>
    public static NamedMotif Create(
        string id,
        string name,
        IReadOnlyList<int> intervals,
        IReadOnlyList<double> durations,
        MotifCategory category,
        string? styleId = null,
        string? description = null,
        double weight = 1.0)
    {
        return new NamedMotif(id, name, new Motif(intervals, durations), category, styleId, description, weight);
    }

    public override string ToString() => $"{Name} ({Category})";
}

/// <summary>
/// A library of named motifs for retrieval and random selection.
/// </summary>
public sealed class MotifLibrary
{
    private readonly Dictionary<string, NamedMotif> _motifsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<NamedMotif>> _motifsByStyle = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<MotifCategory, List<NamedMotif>> _motifsByCategory = new();
    private readonly List<NamedMotif> _universalMotifs = new();
    private readonly Random _random;

    /// <summary>
    /// All motifs in the library.
    /// </summary>
    public IReadOnlyCollection<NamedMotif> All => _motifsById.Values;

    /// <summary>
    /// Number of motifs in the library.
    /// </summary>
    public int Count => _motifsById.Count;

    /// <summary>
    /// Creates an empty motif library.
    /// </summary>
    /// <param name="seed">Optional random seed for reproducibility.</param>
    public MotifLibrary(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Creates a library with initial motifs.
    /// </summary>
    public MotifLibrary(IEnumerable<NamedMotif> motifs, int? seed = null) : this(seed)
    {
        foreach (var motif in motifs)
        {
            Add(motif);
        }
    }

    /// <summary>
    /// Adds a motif to the library.
    /// </summary>
    public void Add(NamedMotif motif)
    {
        ArgumentNullException.ThrowIfNull(motif);

        if (_motifsById.ContainsKey(motif.Id))
        {
            throw new ArgumentException($"Motif with ID '{motif.Id}' already exists", nameof(motif));
        }

        _motifsById[motif.Id] = motif;

        // Index by style
        if (!string.IsNullOrEmpty(motif.StyleId))
        {
            if (!_motifsByStyle.TryGetValue(motif.StyleId, out var styleList))
            {
                styleList = new List<NamedMotif>();
                _motifsByStyle[motif.StyleId] = styleList;
            }
            styleList.Add(motif);
        }
        else
        {
            _universalMotifs.Add(motif);
        }

        // Index by category
        if (!_motifsByCategory.TryGetValue(motif.Category, out var categoryList))
        {
            categoryList = new List<NamedMotif>();
            _motifsByCategory[motif.Category] = categoryList;
        }
        categoryList.Add(motif);
    }

    /// <summary>
    /// Gets a motif by ID.
    /// </summary>
    public NamedMotif? Get(string id)
    {
        return _motifsById.GetValueOrDefault(id);
    }

    /// <summary>
    /// Gets all motifs for a style (including universal motifs).
    /// </summary>
    public IEnumerable<NamedMotif> GetByStyle(string styleId)
    {
        // Return style-specific motifs
        if (_motifsByStyle.TryGetValue(styleId, out var styleMotifs))
        {
            foreach (var motif in styleMotifs)
            {
                yield return motif;
            }
        }

        // Also include universal motifs
        foreach (var motif in _universalMotifs)
        {
            yield return motif;
        }
    }

    /// <summary>
    /// Gets all motifs in a category.
    /// </summary>
    public IEnumerable<NamedMotif> GetByCategory(MotifCategory category)
    {
        return _motifsByCategory.TryGetValue(category, out var list) ? list : Enumerable.Empty<NamedMotif>();
    }

    /// <summary>
    /// Gets motifs matching both style and category.
    /// </summary>
    public IEnumerable<NamedMotif> GetByStyleAndCategory(string styleId, MotifCategory category)
    {
        return GetByStyle(styleId).Where(m => m.Category == category);
    }

    /// <summary>
    /// Selects a random motif weighted by the motif weights.
    /// </summary>
    public NamedMotif? SelectWeighted()
    {
        return SelectWeightedFrom(_motifsById.Values);
    }

    /// <summary>
    /// Selects a random motif from a style (including universal).
    /// </summary>
    public NamedMotif? SelectWeightedByStyle(string styleId)
    {
        return SelectWeightedFrom(GetByStyle(styleId));
    }

    /// <summary>
    /// Selects a random motif from a category.
    /// </summary>
    public NamedMotif? SelectWeightedByCategory(MotifCategory category)
    {
        return SelectWeightedFrom(GetByCategory(category));
    }

    /// <summary>
    /// Selects a random motif matching style and category.
    /// </summary>
    public NamedMotif? SelectWeightedByStyleAndCategory(string styleId, MotifCategory category)
    {
        return SelectWeightedFrom(GetByStyleAndCategory(styleId, category));
    }

    private NamedMotif? SelectWeightedFrom(IEnumerable<NamedMotif> motifs)
    {
        var list = motifs.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var totalWeight = list.Sum(m => m.Weight);
        var target = _random.NextDouble() * totalWeight;
        var cumulative = 0.0;

        foreach (var motif in list)
        {
            cumulative += motif.Weight;
            if (cumulative >= target)
            {
                return motif;
            }
        }

        return list[^1];
    }
}

/// <summary>
/// Built-in motif library with common patterns.
/// </summary>
public static class BuiltInMotifs
{
    private static readonly Lazy<MotifLibrary> _library = new(() =>
    {
        var lib = new MotifLibrary(seed: 42);

        // Universal melodic patterns
        lib.Add(NamedMotif.Create("scale-up", "Scale Ascending",
            [0, 2, 4, 5], [1.0, 1.0, 1.0, 1.0],
            MotifCategory.Melodic,
            description: "Stepwise ascending scale fragment"));

        lib.Add(NamedMotif.Create("scale-down", "Scale Descending",
            [0, -2, -4, -5], [1.0, 1.0, 1.0, 1.0],
            MotifCategory.Melodic,
            description: "Stepwise descending scale fragment"));

        lib.Add(NamedMotif.Create("arpeggio-up", "Arpeggio Ascending",
            [0, 4, 7], [1.0, 1.0, 2.0],
            MotifCategory.Melodic,
            description: "Ascending major triad arpeggio"));

        lib.Add(NamedMotif.Create("arpeggio-down", "Arpeggio Descending",
            [0, -3, -7], [1.0, 1.0, 2.0],
            MotifCategory.Melodic,
            description: "Descending minor triad arpeggio"));

        // Ornamental patterns
        lib.Add(NamedMotif.Create("upper-neighbor", "Upper Neighbor",
            [0, 2, 0], [1.0, 0.5, 1.5],
            MotifCategory.Ornament,
            description: "Upper neighbor tone ornament"));

        lib.Add(NamedMotif.Create("lower-neighbor", "Lower Neighbor",
            [0, -1, 0], [1.0, 0.5, 1.5],
            MotifCategory.Ornament,
            description: "Lower neighbor tone ornament"));

        lib.Add(NamedMotif.Create("turn", "Turn",
            [0, 2, 0, -1, 0], [0.5, 0.5, 0.5, 0.5, 1.0],
            MotifCategory.Ornament,
            description: "Turn figure ornament"));

        lib.Add(NamedMotif.Create("mordent", "Mordent",
            [0, 2, 0], [0.25, 0.25, 0.5],
            MotifCategory.Ornament,
            description: "Quick upper mordent"));

        // Celtic-specific patterns
        lib.Add(NamedMotif.Create("celtic-roll", "Celtic Roll",
            [0, 2, 0, -2, 0], [0.25, 0.25, 0.25, 0.25, 1.0],
            MotifCategory.Ornament, styleId: "celtic",
            description: "Traditional Irish roll ornament"));

        lib.Add(NamedMotif.Create("celtic-triplet", "Celtic Triplet",
            [0, 2, 4], [1.0 / 3, 1.0 / 3, 1.0 / 3],
            MotifCategory.Rhythmic, styleId: "celtic",
            description: "Triplet figure common in jigs"));

        lib.Add(NamedMotif.Create("celtic-leap-step", "Celtic Leap-Step",
            [0, 5, 4, 2], [0.5, 0.5, 0.5, 0.5],
            MotifCategory.Opening, styleId: "celtic",
            description: "Leap up then step down pattern"));

        // Jazz-specific patterns
        lib.Add(NamedMotif.Create("jazz-approach", "Jazz Chromatic Approach",
            [0, 1, 2], [0.5, 0.5, 1.0],
            MotifCategory.Transition, styleId: "jazz",
            description: "Chromatic approach to target note"));

        lib.Add(NamedMotif.Create("jazz-enclosure", "Jazz Enclosure",
            [1, -1, 0], [0.5, 0.5, 1.0],
            MotifCategory.Ornament, styleId: "jazz",
            description: "Target note enclosed by neighbors"));

        lib.Add(NamedMotif.Create("jazz-bebop-scale", "Bebop Scale Fragment",
            [0, 2, 4, 5, 7, 8], [0.5, 0.5, 0.5, 0.5, 0.5, 0.5],
            MotifCategory.Melodic, styleId: "jazz",
            description: "Bebop scale with chromatic passing tone"));

        // Baroque-specific patterns
        lib.Add(NamedMotif.Create("baroque-sequence", "Baroque Sequence",
            [0, -2, 1, -1], [1.0, 1.0, 1.0, 1.0],
            MotifCategory.Development, styleId: "baroque",
            description: "Sequential pattern for development"));

        lib.Add(NamedMotif.Create("baroque-trill-prep", "Baroque Trill Preparation",
            [0, 2, 0, 2, 0], [0.25, 0.25, 0.25, 0.25, 1.0],
            MotifCategory.Ornament, styleId: "baroque",
            description: "Trill with preparation"));

        // Cadential patterns
        lib.Add(NamedMotif.Create("cadence-fall", "Cadential Fall",
            [2, 0], [1.0, 2.0],
            MotifCategory.Cadential,
            description: "Simple falling cadential gesture"));

        lib.Add(NamedMotif.Create("cadence-resolution", "Cadential Resolution",
            [7, 5, 4, 2, 0], [0.5, 0.5, 0.5, 0.5, 2.0],
            MotifCategory.Cadential,
            description: "Extended falling resolution"));

        return lib;
    });

    /// <summary>
    /// Gets the built-in motif library.
    /// </summary>
    public static MotifLibrary Library => _library.Value;

    /// <summary>
    /// Gets a built-in motif by ID.
    /// </summary>
    public static NamedMotif? Get(string id) => Library.Get(id);

    /// <summary>
    /// Gets all Celtic motifs (style-specific + universal).
    /// </summary>
    public static IEnumerable<NamedMotif> Celtic => Library.GetByStyle("celtic");

    /// <summary>
    /// Gets all Jazz motifs (style-specific + universal).
    /// </summary>
    public static IEnumerable<NamedMotif> Jazz => Library.GetByStyle("jazz");

    /// <summary>
    /// Gets all Baroque motifs (style-specific + universal).
    /// </summary>
    public static IEnumerable<NamedMotif> Baroque => Library.GetByStyle("baroque");
}
