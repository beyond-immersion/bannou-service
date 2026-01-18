# Part 1: Pitch Hierarchy - Lerdahl's Tonal Pitch Space

> **Series**: [Understanding the Music Storyteller](./00-index-music-storyteller.md) | Part 1 of 7

## Why Does the Tonic Feel Like "Home"?

When you hear a melody end on the tonic note, it feels *resolved*. When it ends on the leading tone, you feel an almost physical *pull* toward that resolution. This isn't mystical—it's the result of a hierarchical mental representation that listeners build through exposure to tonal music.

**Fred Lerdahl's Tonal Pitch Space** (TPS, 2001) provides the mathematical framework that quantifies these intuitions. Rather than treating all twelve pitch classes as equal, TPS models the *psychological distance* between pitches, chords, and keys—distances that directly correlate with perceived tension and stability.

---

## The Basic Space: A 5-Level Hierarchy

The core data structure of TPS is the **basic space**—a hierarchical representation where some pitches are more "stable" (or less deeply embedded) than others.

```
The Basic Space in C Major
============================

Level a (root):       C                                    ← Most stable (weight: 4)
                      │
Level b (fifth):      C───────────────────G                ← Weight: 3
                      │                   │
Level c (triad):      C─────────E─────────G                ← Weight: 2
                      │         │         │
Level d (diatonic):   C    D    E    F    G    A    B      ← Weight: 1
                      │    │    │    │    │    │    │
Level e (chromatic):  C C# D D# E  F F# G G# A A# B        ← Least stable (weight: 0)
```

### What the Levels Mean

| Level | Name | Contents | Stability | Musical Role |
|-------|------|----------|-----------|--------------|
| **a** | Root | Just the tonic | 4 (highest) | The "home" pitch |
| **b** | Fifth | Root + perfect fifth | 3 | Primary harmonic support |
| **c** | Triadic | Root + 3rd + 5th | 2 | Chord tones |
| **d** | Diatonic | All scale degrees | 1 | "In key" pitches |
| **e** | Chromatic | All 12 pitch classes | 0 | "Outside" pitches |

**Key insight**: More stable pitches *persist* through all levels below them. The root (C) appears at every level; chromatic notes (C#, D#, etc.) only appear at level e.

### The Pyramid Visualization

```
                    ┌───┐
                    │ C │                    Level a: Root
                    └─┬─┘
                  ┌───┴───┐
                  │ C   G │                  Level b: Fifth
                  └───┬───┘
              ┌───────┼───────┐
              │   C   E   G   │              Level c: Triad
              └───────┬───────┘
      ┌───────────────┼───────────────┐
      │ C   D   E   F   G   A   B     │      Level d: Diatonic
      └───────────────┬───────────────┘
┌─────────────────────┴─────────────────────┐
│ C C# D D# E  F F# G G# A A# B             │  Level e: Chromatic
└───────────────────────────────────────────┘
```

The **depth** of a pitch (how many levels contain it) determines its stability:
- C appears in 5 levels → depth 5 → maximum stability
- G appears in 4 levels → depth 4
- E appears in 3 levels → depth 3
- D, F, A, B appear in 2 levels → depth 2
- C#, D#, F#, G#, A# appear in 1 level → depth 1 → minimum stability

---

## Implementation: The BasicSpace Class

Here's how we implement this hierarchy:

```csharp
public class BasicSpace
{
    // Level a: Root only (the tonic or chord root)
    public IReadOnlySet<int> RootLevel { get; }      // e.g., {0}

    // Level b: Fifth (root + perfect fifth)
    public IReadOnlySet<int> FifthLevel { get; }     // e.g., {0, 7}

    // Level c: Triadic (root + third + fifth)
    public IReadOnlySet<int> TriadicLevel { get; }   // e.g., {0, 4, 7}

    // Level d: Diatonic scale degrees
    public IReadOnlySet<int> DiatonicLevel { get; }  // e.g., {0, 2, 4, 5, 7, 9, 11}

    // Level e: Full chromatic (all 12 pitch classes)
    public IReadOnlySet<int> ChromaticLevel { get; } // {0, 1, 2, ..., 11}

    /// Gets stability level (0-4) for a pitch class
    public int GetStabilityLevel(int pitchClass)
    {
        pitchClass = ((pitchClass % 12) + 12) % 12;

        if (RootLevel.Contains(pitchClass)) return 4;
        if (FifthLevel.Contains(pitchClass)) return 3;
        if (TriadicLevel.Contains(pitchClass)) return 2;
        if (DiatonicLevel.Contains(pitchClass)) return 1;
        return 0; // Chromatic only
    }
}
```

The stability values (4, 3, 2, 1, 0) directly feed into tension and attraction calculations.

---

## Chord Distance: δ = i + j + k

How "far apart" are two chords? TPS provides a precise formula:

```
δ(x → y) = i + j + k

Where:
  i = Circle of fifths distance between roots
  j = Number of distinct pitch classes in y's diatonic not in x's
  k = Number of non-common chord tones
```

### The Three Components Explained

**i (Circle of Fifths Distance)**: How many steps around the circle of fifths?

```
Circle of Fifths
================
                    C
                   / \
                 F     G
                /       \
              Bb         D
             /             \
           Eb               A
             \             /
              Ab         E
                \       /
                 Db   B
                   \ /
                   F#/Gb

C → G: 1 step     C → D: 2 steps     C → F#: 6 steps (maximum)
```

**j (Diatonic Distinctiveness)**: How different are the scales?
- C major to G major: 1 different note (F# instead of F) → j = 1
- C major to D major: 2 different notes (F#, C#) → j = 2

**k (Non-Common Chord Tones)**: How different are the actual chords?
- C major (C-E-G) to G major (G-B-D): Different notes are B, D → k = 2

### Pre-Computed Distance Table

For chords within a single key (i = 0), here are the distances:

```
Chord Distance from Tonic (δ from I)
====================================

         ┌─────────────────────────────┐
         │  Chord        δ    j    k   │
         ├─────────────────────────────┤
         │  I (tonic)    0    0    0   │
         │  V            5    0    2   │  ← Dominant
         │  IV           5    0    2   │  ← Subdominant
         │  vi           7    0    2   │  ← Relative minor
         │  iii          7    0    2   │
         │  ii           8    0    3   │  ← Pre-dominant
         │  vii°         8    0    3   │  ← Leading tone chord
         └─────────────────────────────┘
```

Notice the symmetry: V and IV are equidistant from I, and vi and iii are equidistant.

### Implementation

```csharp
public static class ChordDistance
{
    /// Calculates the full chord distance δ(x→y) = i + j + k
    public static int Calculate(BasicSpace from, BasicSpace to)
    {
        var i = CircleOfFifthsDistance(from.Root, to.Root);
        var j = CalculateDistinctPitchClasses(from, to);
        var k = CalculateNonCommonTones(from, to);

        return i + j + k;
    }

    /// Circle of fifths distance (0-6, since 7 = -5)
    public static int CircleOfFifthsDistance(PitchClass from, PitchClass to)
    {
        var fromFifths = SemitonesToFifths((int)from);
        var toFifths = SemitonesToFifths((int)to);

        var distance = Math.Abs(toFifths - fromFifths);
        return Math.Min(distance, 12 - distance);
    }
}
```

---

## Melodic Attraction: α = (s₂/s₁) × (1/n²)

Why does B feel like it "wants" to resolve to C? TPS quantifies this with the **melodic attraction** formula:

```
α(p₁ → p₂) = (s₂/s₁) × (1/n²)

Where:
  s₁ = stability of source pitch
  s₂ = stability of target pitch
  n  = distance in semitones
```

### What the Formula Captures

**Stability ratio (s₂/s₁)**:
- Unstable → Stable has ratio > 1 (strong attraction)
- Stable → Unstable has ratio < 1 (weak attraction)

**Inverse square distance (1/n²)**:
- Closer pitches attract more strongly
- This models the "field-like" behavior of melodic pull

### Example Calculations (in C Major)

| Motion | s₁ | s₂ | n | α | Interpretation |
|--------|----|----|---|---|----------------|
| B → C (leading tone) | 1 | 4 | 1 | **4.00** | Very strong resolution |
| F → E | 1 | 2 | 1 | **2.00** | Strong resolution |
| D → C | 1 | 4 | 2 | **1.00** | Moderate resolution |
| G → C | 3 | 4 | 5 | **0.05** | Weak (leap, similar stability) |
| C → B | 4 | 1 | 1 | **0.25** | Very weak (wrong direction) |

**Key insight**: The attraction is *asymmetric*. B is strongly attracted to C (α = 4.0), but C is weakly attracted to B (α = 0.25). This asymmetry explains why unstable→stable feels more "right" than stable→unstable.

### The Attraction Asymmetry Ratio

```
B→C attraction / C→B attraction = 4.0 / 0.25 = 16×

The leading tone is 16 times more attracted to the tonic
than the tonic is to the leading tone.
```

### Implementation

```csharp
public static class MelodicAttraction
{
    /// α = (s₂/s₁) × (1/n²)
    public static double Calculate(int sourcePitch, int targetPitch, BasicSpace context)
    {
        if (sourcePitch == targetPitch) return 0.0;

        var s1 = context.GetStabilityLevel(sourcePitch % 12);
        var s2 = context.GetStabilityLevel(targetPitch % 12);
        var n = Math.Abs(targetPitch - sourcePitch);

        if (s1 == 0) s1 = 1; // Prevent division by zero

        return (s2 / (double)s1) * (1.0 / (n * n));
    }

    /// Find which diatonic pitch has strongest attraction from source
    public static (int targetPitchClass, double attraction) GetStrongestAttraction(
        int sourcePitch, BasicSpace context, int octave = 4)
    {
        var attractions = CalculateAllAttractions(sourcePitch, context, octave);
        var strongest = attractions.MaxBy(kvp => kvp.Value);
        return (strongest.Key, strongest.Value);
    }
}
```

---

## Practical Applications

### 1. Voice Leading Optimization

TPS attraction values guide voice leading decisions:

```csharp
// Calculate voice leading smoothness
public static double CalculateVoiceLeadingRoughness(IEnumerable<int> intervals, BasicSpace context)
{
    var roughness = 0.0;
    foreach (var interval in intervals)
    {
        roughness += Math.Abs(interval) switch
        {
            0 => 0.0,   // No motion
            1 => 0.1,   // Half step - very smooth
            2 => 0.15,  // Whole step - smooth
            3 => 0.25,  // Minor third - moderate
            >= 5 => 0.5 // Larger - rough (unless perfect fifth)
        };
    }
    return roughness;
}
```

### 2. Harmonic Tension Curves

Chord distances directly feed into tension calculations:

```csharp
// Relative tension from tonic
public static double GetRelativeTension(Chord chord, Scale key)
{
    var tonicChord = Chord.FromScaleDegree(key, 1, false);
    var distance = ChordDistance.Calculate(tonicChord, chord, key);

    // Normalize to 0-1 range
    return Math.Min(distance / 12.0, 1.0);
}
```

### 3. Expectation-Based Melody Generation

The system can predict "expected" resolutions:

```csharp
// Given a source pitch, what resolution is most expected?
var (expectedTarget, expectation) = MelodicAttraction.GetStrongestAttraction(
    sourcePitch: 71,  // B4
    context: cMajorSpace,
    octave: 4
);
// expectedTarget = 0 (C), expectation = 4.0
```

---

## Connection to Other Frameworks

TPS provides the **"how much"** of musical tension—precise numerical values. It integrates with:

| Framework | What It Provides | How TPS Connects |
|-----------|------------------|------------------|
| **ITPRA** (Huron) | *Why* expectation creates emotion | TPS δ correlates with prediction probability |
| **IDyOM** (Pearce) | Statistical expectation | Higher δ ≈ lower P ≈ higher Information Content |
| **BRECVEMA** (Juslin) | Emotion routing pathways | Surface dissonance → Brain Stem Reflex |
| **Navarro TIS** | Validated tension weights | Uses TPS components with empirical coefficients |

The tension formula from Navarro-Cáceres et al. (2020) combines TPS components:

```
T = 0.402×Dissonance + 0.246×Hierarchical + 0.202×TonalDist + 0.193×VoiceLeading
```

See [Part 4: The Mathematics of Tension](./04-mathematics-of-tension.md) for the full model.

---

## Academic Sources

### Primary Reference
- **Lerdahl, F.** (2001). *Tonal Pitch Space*. Oxford University Press.
  - Chapter 2: The Basic Space and Chord Distance (pp. 42-83)
  - Chapter 4: Melodic Attraction (pp. 161-189)

### Related Works
- Lerdahl, F. & Jackendoff, R. (1983). *A Generative Theory of Tonal Music*. MIT Press.
- Krumhansl, C.L. (1990). *Cognitive Foundations of Musical Pitch*. Oxford University Press.
- Bharucha, J.J. (1984). "Anchoring effects in music: The resolution of dissonance." *Cognitive Psychology*, 16(4), 485-518.

### Computational Extensions
- Navarro-Cáceres, M., et al. (2020). "A computational model of tonal tension." *Journal of Mathematics and Music*, 14(3), 239-254.

---

## Key Takeaways

1. **Pitch stability is hierarchical**: Root > Fifth > Triad > Diatonic > Chromatic
2. **Chord distance δ = i + j + k**: Three components capture root, scale, and chord differences
3. **Attraction is asymmetric**: Unstable pitches are pulled toward stable ones, not vice versa
4. **The formulas are precise**: These aren't metaphors—they're computationally verified against listener data

---

**Next**: [Part 2: Musical Expectation - Why Surprise Feels Good](./02-expectation-itpra.md)

---

*This document is part of the [Music Storyteller SDK](https://github.com/BeyondImmersion/bannou) documentation.*
