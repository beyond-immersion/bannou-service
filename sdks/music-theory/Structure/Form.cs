using System.Text.Json.Serialization;

namespace BeyondImmersion.Bannou.MusicTheory.Structure;

/// <summary>
/// Represents a section label in a musical form.
/// </summary>
public readonly struct Section : IEquatable<Section>
{
    /// <summary>
    /// The section label (e.g., "A", "B", "C").
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; }

    /// <summary>
    /// Variation number (0 = original, 1 = A', 2 = A'', etc.).
    /// </summary>
    [JsonPropertyName("variation")]
    public int Variation { get; }

    /// <summary>
    /// Creates a section with a label.
    /// </summary>
    [JsonConstructor]
    public Section(string label, int variation = 0)
    {
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Variation = variation;
    }

    /// <summary>
    /// Creates a variation of this section.
    /// </summary>
    public Section CreateVariation() => new(Label, Variation + 1);

    public bool Equals(Section other) => Label == other.Label && Variation == other.Variation;
    public override bool Equals(object? obj) => obj is Section other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Label, Variation);

    public static bool operator ==(Section a, Section b) => a.Equals(b);
    public static bool operator !=(Section a, Section b) => !a.Equals(b);

    public override string ToString()
    {
        var primes = new string('\'', Variation);
        return $"{Label}{primes}";
    }

    /// <summary>
    /// Common section labels.
    /// </summary>
    public static Section A => new("A");
    public static Section B => new("B");
    public static Section C => new("C");
    public static Section Intro => new("Intro");
    public static Section Verse => new("Verse");
    public static Section Chorus => new("Chorus");
    public static Section Bridge => new("Bridge");
    public static Section Outro => new("Outro");
}

/// <summary>
/// Represents a musical form as a sequence of sections.
/// </summary>
public sealed class Form
{
    /// <summary>
    /// Name of the form.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; }

    /// <summary>
    /// Sequence of sections.
    /// </summary>
    [JsonPropertyName("sections")]
    public IReadOnlyList<Section> Sections { get; }

    /// <summary>
    /// Default number of bars per section.
    /// </summary>
    [JsonPropertyName("defaultBarsPerSection")]
    public int DefaultBarsPerSection { get; }

    /// <summary>
    /// Creates a form.
    /// </summary>
    [JsonConstructor]
    public Form(string name, IReadOnlyList<Section> sections, int defaultBarsPerSection = 8)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Sections = sections ?? throw new ArgumentNullException(nameof(sections));
        DefaultBarsPerSection = defaultBarsPerSection;
    }

    /// <summary>
    /// Total number of sections.
    /// </summary>
    public int SectionCount => Sections.Count;

    /// <summary>
    /// Total bars using default section length.
    /// </summary>
    public int TotalBars => SectionCount * DefaultBarsPerSection;

    /// <summary>
    /// Gets unique section labels used in this form.
    /// </summary>
    public IEnumerable<string> UniqueLabels => Sections.Select(s => s.Label).Distinct();

    /// <summary>
    /// Common musical forms.
    /// </summary>
    public static class Common
    {
        /// <summary>AABB form (typical Celtic tune)</summary>
        public static Form AABB => new("AABB",
            [Section.A, Section.A, Section.B, Section.B], 8);

        /// <summary>AAB form (blues)</summary>
        public static Form AAB => new("AAB",
            [Section.A, Section.A, Section.B], 4);

        /// <summary>AABA form (32-bar song form)</summary>
        public static Form AABA => new("AABA",
            [Section.A, Section.A, Section.B, Section.A], 8);

        /// <summary>ABAB form (verse-chorus)</summary>
        public static Form ABAB => new("ABAB",
            [Section.A, Section.B, Section.A, Section.B], 8);

        /// <summary>ABAC form (with contrasting return)</summary>
        public static Form ABAC => new("ABAC",
            [Section.A, Section.B, Section.A, Section.C], 8);

        /// <summary>Rondo form (ABACA)</summary>
        public static Form Rondo => new("Rondo",
            [Section.A, Section.B, Section.A, Section.C, Section.A], 8);

        /// <summary>12-bar blues</summary>
        public static Form Blues12Bar => new("12-Bar Blues",
            [Section.A, Section.A, Section.A, Section.A,
             Section.B, Section.B, Section.A, Section.A,
             Section.C, Section.B, Section.A, Section.A], 1);

        /// <summary>Pop song structure</summary>
        public static Form PopSong => new("Pop Song",
            [Section.Intro, Section.Verse, Section.Chorus,
             Section.Verse, Section.Chorus, Section.Bridge,
             Section.Chorus, Section.Outro], 8);
    }

    /// <summary>
    /// Parses a form from a string like "AABB" or "A-A-B-B".
    /// </summary>
    public static Form Parse(string pattern, int defaultBarsPerSection = 8)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("Pattern cannot be empty", nameof(pattern));
        }

        var parts = pattern.Replace("-", "").Replace(" ", "").ToCharArray();
        var sections = parts.Select(c => new Section(c.ToString())).ToList();

        return new Form(pattern, sections, defaultBarsPerSection);
    }

    public override string ToString() => $"{Name}: {string.Join("-", Sections)}";
}

/// <summary>
/// A section instance with specific bar range.
/// </summary>
public sealed class FormSection
{
    /// <summary>
    /// The section definition.
    /// </summary>
    public Section Section { get; }

    /// <summary>
    /// Starting bar (0-based).
    /// </summary>
    public int StartBar { get; }

    /// <summary>
    /// Number of bars.
    /// </summary>
    public int Bars { get; }

    /// <summary>
    /// Ending bar (exclusive).
    /// </summary>
    public int EndBar => StartBar + Bars;

    /// <summary>
    /// Creates a form section.
    /// </summary>
    public FormSection(Section section, int startBar, int bars)
    {
        Section = section;
        StartBar = startBar;
        Bars = bars;
    }

    public override string ToString() => $"{Section} (bars {StartBar + 1}-{EndBar})";
}

/// <summary>
/// Expanded form with concrete bar assignments.
/// </summary>
public sealed class ExpandedForm
{
    /// <summary>
    /// The source form.
    /// </summary>
    public Form Form { get; }

    /// <summary>
    /// Section instances with bar ranges.
    /// </summary>
    public IReadOnlyList<FormSection> Sections { get; }

    /// <summary>
    /// Total bars.
    /// </summary>
    public int TotalBars { get; }

    /// <summary>
    /// Creates an expanded form from a form definition.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <param name="barsPerSection">Optional override for bars per section (null = use form default).</param>
    public ExpandedForm(Form form, int? barsPerSection = null)
    {
        Form = form;
        var barCount = barsPerSection ?? form.DefaultBarsPerSection;

        var sections = new List<FormSection>();
        var currentBar = 0;

        foreach (var section in form.Sections)
        {
            sections.Add(new FormSection(section, currentBar, barCount));
            currentBar += barCount;
        }

        Sections = sections;
        TotalBars = currentBar;
    }

    /// <summary>
    /// Gets the section at a given bar number.
    /// </summary>
    public FormSection? GetSectionAtBar(int bar)
    {
        return Sections.FirstOrDefault(s => bar >= s.StartBar && bar < s.EndBar);
    }

    /// <summary>
    /// Gets all instances of a section label.
    /// </summary>
    public IEnumerable<FormSection> GetSectionsByLabel(string label)
    {
        return Sections.Where(s => s.Section.Label == label);
    }
}
