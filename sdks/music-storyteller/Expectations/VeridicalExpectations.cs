using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Pitch;

namespace BeyondImmersion.Bannou.MusicStoryteller.Expectations;

/// <summary>
/// Represents a recognized musical pattern from this piece.
/// </summary>
public sealed class RecognizedPattern
{
    /// <summary>
    /// Unique identifier for this pattern.
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>
    /// Type of pattern (melody, harmony, rhythm).
    /// </summary>
    public PatternType Type { get; init; }

    /// <summary>
    /// Bars where this pattern has appeared.
    /// </summary>
    public List<int> Appearances { get; } = [];

    /// <summary>
    /// The pattern content (e.g., interval sequence, chord sequence).
    /// </summary>
    public List<int> Content { get; } = [];

    /// <summary>
    /// Recognition strength (increases with repeated hearings).
    /// </summary>
    public double Strength { get; set; } = 0.5;

    /// <summary>
    /// Expected next element if pattern continues.
    /// </summary>
    public int? ExpectedNext { get; set; }
}

/// <summary>
/// Types of patterns that can be recognized.
/// </summary>
public enum PatternType
{
    /// <summary>Melodic interval pattern.</summary>
    Melodic,

    /// <summary>Harmonic chord pattern.</summary>
    Harmonic,

    /// <summary>Rhythmic pattern.</summary>
    Rhythmic,

    /// <summary>Phrase structure pattern.</summary>
    Structural,

    /// <summary>Theme/motif return.</summary>
    Thematic
}

/// <summary>
/// Veridical expectations based on specific piece memory.
/// Represents episodic memory - what has happened in THIS piece.
/// Source: Huron, D. (2006). Sweet Anticipation, Chapter 12.
/// </summary>
public sealed class VeridicalExpectations
{
    /// <summary>
    /// All recognized patterns from this piece.
    /// </summary>
    public List<RecognizedPattern> Patterns { get; } = [];

    /// <summary>
    /// Bars where the main theme has appeared.
    /// </summary>
    public List<int> MainThemeAppearances { get; } = [];

    /// <summary>
    /// The main theme (if recognized).
    /// </summary>
    public RecognizedPattern? MainTheme { get; private set; }

    /// <summary>
    /// Recognized form structure (e.g., "AABB", "AABA").
    /// </summary>
    public string? RecognizedForm { get; set; }

    /// <summary>
    /// Whether the overall form has been recognized.
    /// </summary>
    public bool FormRecognized { get; set; }

    /// <summary>
    /// Current position in recognized form (e.g., in section A, B).
    /// </summary>
    public char? CurrentSection { get; set; }

    /// <summary>
    /// Expected section after current (based on recognized form).
    /// </summary>
    public char? ExpectedNextSection { get; set; }

    /// <summary>
    /// Key areas visited in this piece.
    /// </summary>
    public List<PitchClass> KeyAreas { get; } = [];

    /// <summary>
    /// Records a pattern occurrence.
    /// </summary>
    /// <param name="patternId">Unique pattern identifier.</param>
    /// <param name="type">Type of pattern.</param>
    /// <param name="bar">Current bar number.</param>
    /// <param name="content">Pattern content.</param>
    public void RecordPattern(string patternId, PatternType type, int bar, IEnumerable<int> content)
    {
        var existing = Patterns.FirstOrDefault(p => p.Id == patternId);

        if (existing != null)
        {
            // Strengthen existing pattern
            existing.Appearances.Add(bar);
            existing.Strength = Math.Min(1.0, existing.Strength + 0.2);
        }
        else
        {
            // New pattern
            var pattern = new RecognizedPattern
            {
                Id = patternId,
                Type = type,
                Strength = 0.5
            };
            pattern.Appearances.Add(bar);
            pattern.Content.AddRange(content);
            Patterns.Add(pattern);
        }
    }

    /// <summary>
    /// Records a main theme appearance.
    /// </summary>
    /// <param name="bar">Bar number.</param>
    /// <param name="intervals">Theme intervals (if not yet recorded).</param>
    public void RecordMainThemeAppearance(int bar, IEnumerable<int>? intervals = null)
    {
        MainThemeAppearances.Add(bar);

        if (MainTheme == null && intervals != null)
        {
            MainTheme = new RecognizedPattern
            {
                Id = "main_theme",
                Type = PatternType.Thematic,
                Strength = 0.7
            };
            MainTheme.Appearances.Add(bar);
            MainTheme.Content.AddRange(intervals);
            Patterns.Add(MainTheme);
        }
        else if (MainTheme != null)
        {
            MainTheme.Appearances.Add(bar);
            MainTheme.Strength = Math.Min(1.0, MainTheme.Strength + 0.15);
        }
    }

    /// <summary>
    /// Gets expected theme return probability based on piece history.
    /// </summary>
    /// <param name="currentBar">Current bar number.</param>
    /// <returns>Probability of theme return (0-1).</returns>
    public double GetThemeReturnProbability(int currentBar)
    {
        if (MainThemeAppearances.Count < 2)
        {
            return 0.1; // Not enough data
        }

        // Calculate average interval between appearances
        var intervals = new List<int>();
        for (var i = 1; i < MainThemeAppearances.Count; i++)
        {
            intervals.Add(MainThemeAppearances[i] - MainThemeAppearances[i - 1]);
        }

        var avgInterval = intervals.Average();
        var barsSinceLastAppearance = currentBar - MainThemeAppearances.Last();

        // Probability increases as we approach the expected return time
        var ratio = barsSinceLastAppearance / avgInterval;

        return Math.Clamp(ratio, 0.0, 1.0);
    }

    /// <summary>
    /// Checks if a sequence matches a known pattern.
    /// </summary>
    /// <param name="sequence">The sequence to check.</param>
    /// <param name="type">Type of pattern to look for.</param>
    /// <returns>Matching pattern and match strength, or null.</returns>
    public (RecognizedPattern pattern, double matchStrength)? FindMatchingPattern(
        IReadOnlyList<int> sequence, PatternType type)
    {
        foreach (var pattern in Patterns.Where(p => p.Type == type))
        {
            var matchStrength = CalculateSequenceMatch(sequence, pattern.Content);
            if (matchStrength > 0.7)
            {
                return (pattern, matchStrength);
            }
        }

        return null;
    }

    /// <summary>
    /// Records form section entry.
    /// </summary>
    /// <param name="section">Section identifier (A, B, C, etc.).</param>
    public void EnterSection(char section)
    {
        CurrentSection = section;

        // Update form recognition
        if (RecognizedForm != null && RecognizedForm.Length > 0)
        {
            var currentIndex = RecognizedForm.LastIndexOf(section);
            if (currentIndex >= 0 && currentIndex < RecognizedForm.Length - 1)
            {
                ExpectedNextSection = RecognizedForm[currentIndex + 1];
            }
        }
    }

    /// <summary>
    /// Attempts to recognize the form based on observed sections.
    /// </summary>
    /// <param name="sectionHistory">Observed section sequence.</param>
    public void RecognizeForm(string sectionHistory)
    {
        // Common forms to match against
        var commonForms = new[] { "AABB", "AABA", "ABAB", "ABAC", "ABBA", "ABC" };

        foreach (var form in commonForms)
        {
            if (MatchesFormPattern(sectionHistory, form))
            {
                RecognizedForm = form;
                FormRecognized = true;
                return;
            }
        }

        // If no match, store the history as the form
        if (sectionHistory.Length >= 4)
        {
            RecognizedForm = sectionHistory;
            FormRecognized = true;
        }
    }

    /// <summary>
    /// Gets the next expected element based on veridical expectations.
    /// </summary>
    /// <param name="recentSequence">Recent elements.</param>
    /// <param name="type">Type of pattern.</param>
    /// <returns>Expected next element and confidence.</returns>
    public (int expected, double confidence)? GetNextExpectation(
        IReadOnlyList<int> recentSequence, PatternType type)
    {
        foreach (var pattern in Patterns.Where(p => p.Type == type).OrderByDescending(p => p.Strength))
        {
            // Check if recent sequence matches start of pattern
            if (IsPrefix(recentSequence, pattern.Content))
            {
                var nextIndex = recentSequence.Count;
                if (nextIndex < pattern.Content.Count)
                {
                    return (pattern.Content[nextIndex], pattern.Strength);
                }
            }
        }

        return null;
    }

    private static double CalculateSequenceMatch(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;

        var minLength = Math.Min(a.Count, b.Count);
        var matches = 0;

        for (var i = 0; i < minLength; i++)
        {
            if (a[i] == b[i]) matches++;
        }

        return (double)matches / minLength;
    }

    private static bool IsPrefix(IReadOnlyList<int> prefix, IReadOnlyList<int> full)
    {
        if (prefix.Count > full.Count) return false;

        for (var i = 0; i < prefix.Count; i++)
        {
            if (prefix[i] != full[i]) return false;
        }

        return true;
    }

    private static bool MatchesFormPattern(string history, string form)
    {
        if (history.Length < form.Length) return false;

        // Check if history could be a repetition of the form
        for (var i = 0; i < history.Length; i++)
        {
            if (history[i] != form[i % form.Length])
            {
                return false;
            }
        }

        return true;
    }
}
