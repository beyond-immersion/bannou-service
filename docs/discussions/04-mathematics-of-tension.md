# Part 4: The Mathematics of Tension

> **Series**: [Understanding the Music Storyteller](./00-index-music-storyteller.md) | Part 4 of 7

## Can Tension Be Measured?

When we say a chord progression "builds tension," what exactly do we mean? Can we put a number on it?

**Yes.** Navarro-Cáceres et al. (2020) developed an empirically validated formula that predicts listener-perceived tension with R² = 0.563—outperforming both Lerdahl's classical model and MorpheuS.

---

## The TIS Tension Formula

```
T = 0.402×Dissonance + 0.246×Hierarchical + 0.202×TonalDist + 0.193×VoiceLeading

Where:
  Dissonance     = Psychoacoustic roughness of the chord
  Hierarchical   = Tension from position in functional hierarchy
  TonalDist      = Distance from the tonic in pitch space
  VoiceLeading   = Melodic tension between consecutive chords
```

### The Four Components Visualized

```
Tension Components and Weights
==============================

┌─────────────────────────────────────────────────────────────────────┐
│                                                                     │
│        DISSONANCE                           40.2%                   │
│        ████████████████████████████████████░░░░░░░░░░░░░░░░░░░░░░  │
│        Roughness, beating, interval clashes                        │
│                                                                     │
│        HIERARCHICAL                         24.6%                   │
│        ████████████████████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  │
│        Position in functional tree                                  │
│                                                                     │
│        TONAL DISTANCE                       20.2%                   │
│        ████████████████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  │
│        Distance from tonic                                          │
│                                                                     │
│        VOICE LEADING                        19.3%                   │
│        ███████████████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  │
│        Melodic motion between chords                                │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Key Finding: What Doesn't Matter

The regression analysis **excluded** two components as not statistically significant:
- Chord-to-chord distance (sequential transitions)
- Distance from tonal function (I, IV, V)

This means listeners perceive tension primarily through the chord's intrinsic properties (dissonance, position) rather than its moment-to-moment transitions.

---

## Component Deep Dive

### 1. Dissonance (40.2%)

The largest contributor to tension. Dissonance arises from:
- **Roughness**: Beating between close frequencies
- **Interval content**: Tritones, minor seconds
- **Chord quality**: Diminished > Augmented > Dominant7 > Major

```
Dissonance by Chord Quality
===========================

Chord Type          Dissonance   Visual
────────────────────────────────────────────
Major               0.00         ░░░░░░░░░░
Minor               0.10         █░░░░░░░░░
Dominant 7          0.30         ███░░░░░░░
Major 7             0.25         ██▓░░░░░░░
Minor 7             0.25         ██▓░░░░░░░
Augmented           0.45         ████▓░░░░░
Diminished          0.50         █████░░░░░
Half-dim 7          0.55         █████▓░░░░
Diminished 7        0.60         ██████░░░░
```

### 2. Hierarchical Tension (24.6%)

Based on position in the tonal hierarchy. Recalls [Part 1: Pitch Hierarchy](./01-pitch-hierarchy-tonal-pitch-space.md):

```
Root Stability → Hierarchical Tension
=====================================

Stability Level    Root Examples (in C)    Tension
───────────────────────────────────────────────────
4 (most stable)    C (tonic)               0.00
3                  G (dominant)            0.25
2                  E (mediant)             0.50
1                  D, F, A, B              0.75
0 (chromatic)      C#, D#, F#...           1.00
```

A chord on C (stability 4) contributes 0 hierarchical tension. A chord on F# (stability 0) contributes maximum hierarchical tension.

### 3. Tonal Distance (20.2%)

How far the chord is from the tonic in pitch space. Uses the δ formula from [Part 1](./01-pitch-hierarchy-tonal-pitch-space.md):

```csharp
public static double GetRelativeTension(Chord chord, Scale key)
{
    var tonicChord = Chord.FromScaleDegree(key, 1, false);
    var distance = ChordDistance.Calculate(tonicChord, chord, key);

    // Normalize to 0-1 range (max practical distance ~12)
    return Math.Min(distance / 12.0, 1.0);
}
```

### 4. Voice Leading (19.3%)

Melodic tension between consecutive chords. Smooth voice leading (stepwise motion) produces less tension than leaps:

```
Voice Leading Tension by Root Motion
====================================

Root Motion         Semitones    Tension
────────────────────────────────────────
Same root           0            0.0
Half step           1            0.2
Whole step          2            0.2
Minor third         3            0.4
Major third         4            0.4
Perfect fourth      5            0.3
Tritone             6            0.5
Perfect fifth       7            0.3
```

Note: Perfect intervals (4th, 5th) have lower tension than imperfect ones—they're "expected" voice leading.

---

## Implementation

```csharp
public static class TensionCalculator
{
    public static class Weights
    {
        public const double Dissonance = 0.402;
        public const double Hierarchical = 0.246;
        public const double TonalDistance = 0.202;
        public const double VoiceLeading = 0.193;
    }

    public static double Calculate(
        double dissonance,
        double hierarchicalTension,
        double tonalDistance,
        double voiceLeadingRoughness)
    {
        return Weights.Dissonance * dissonance +
               Weights.Hierarchical * hierarchicalTension +
               Weights.TonalDistance * tonalDistance +
               Weights.VoiceLeading * voiceLeadingRoughness;
    }

    public static double CalculateForChord(Chord chord, Scale key, Chord? previousChord = null)
    {
        var dissonance = CalculateDissonance(chord);
        var hierarchical = CalculateHierarchicalTension(chord, key);
        var tonalDist = ChordDistance.GetRelativeTension(chord, key);
        var voiceLeading = previousChord != null
            ? CalculateVoiceLeadingTension(previousChord, chord)
            : 0.0;

        return Calculate(dissonance, hierarchical, tonalDist, voiceLeading);
    }
}
```

---

## Reference Tension Values

Common harmonic functions have characteristic tension levels:

```
Reference Tension Values
========================

Function                 Tension    Bar
───────────────────────────────────────────────────
Tonic (I)                0.10      █░░░░░░░░░
Subdominant (IV)         0.35      ███▓░░░░░░
Dominant (V)             0.50      █████░░░░░
Secondary Dominant (V/V) 0.60      ██████░░░░
Dominant 7th (V7)        0.65      ██████▓░░░
Diminished 7th           0.80      ████████░░
```

---

## Tension Curves

The system can generate tension trajectories for chord sequences:

```csharp
public static double[] CalculateTensionCurve(IReadOnlyList<Chord> chords, Scale key)
{
    var tensions = new double[chords.Count];

    for (var i = 0; i < chords.Count; i++)
    {
        var previous = i > 0 ? chords[i - 1] : null;
        tensions[i] = CalculateForChord(chords[i], key, previous);
    }

    return tensions;
}
```

### Example: Classic Cadence Pattern

```
I - IV - V7 - I
Tension Curve
=============

Tension
  1.0 ┤
  0.8 ┤
  0.6 ┤           ●────●
  0.4 ┤      ●────┘    │
  0.2 ┤●──────         │
  0.0 ┤                └────●
      └───────────────────────
        I    IV    V7    I

Values: 0.10 → 0.35 → 0.65 → 0.10
```

### Prototypical Tension Curves

The Navarro paper identifies five archetypal tension shapes:

```
Prototypical Tension Curves
===========================

(a) INCREASING               (b) DECREASING
    ────●                    ●────
   ╱                              ╲
  ●                                ●

(c) CONCAVE (Arch)           (d) CONVEX (U-shape)
      ●                      ●     ●
     ╱ ╲                      ╲   ╱
    ●   ●                      ● ●

(e) FLUCTUATING              (f) COMPLEX
  ● ● ● ●                    Various patterns
   ╲╱╲╱
```

These serve as compositional templates. A typical "tension and release" narrative uses the concave (arch) shape.

---

## Resolution Strength

Resolution pleasure correlates with tension drop (from [Part 2: ITPRA](./02-expectation-itpra.md)):

```csharp
public static double CalculateResolutionStrength(double tensionBefore, double tensionAfter)
{
    var drop = tensionBefore - tensionAfter;
    return Math.Clamp(drop, 0.0, 1.0);
}
```

**Example**: V7 (0.65) → I (0.10) = resolution strength of 0.55

---

## Finding Tension Peaks

```csharp
public static List<int> FindTensionPeaks(double[] tensions, double threshold = 0.5)
{
    var peaks = new List<int>();

    for (var i = 1; i < tensions.Length - 1; i++)
    {
        if (tensions[i] > threshold &&
            tensions[i] > tensions[i - 1] &&
            tensions[i] >= tensions[i + 1])
        {
            peaks.Add(i);
        }
    }

    return peaks;
}
```

Peaks are natural climax points in the harmonic narrative.

---

## Connection to Other Frameworks

| Framework | TIS Component | Relationship |
|-----------|---------------|--------------|
| **Lerdahl TPS** | Tonal Distance | TIS automates and validates Lerdahl's formulas |
| **ITPRA** (Huron) | Tension response | TIS quantifies what ITPRA describes qualitatively |
| **BRECVEMA** | Musical Expectancy | Dissonance component also triggers Brainstem Reflex |
| **Cheung Model** | — | Complementary: Cheung = probabilistic, TIS = geometric |

---

## Comparison with Other Models

| Model | Pearson ρ | R² | Approach |
|-------|-----------|-----|----------|
| **TIS (Navarro)** | **0.750** | **0.563** | DFT-based pitch space |
| Lerdahl TPS | 0.677 | 0.458 | Stepped hierarchy |
| MorpheuS | 0.700 | 0.489 | Spiral Array |

TIS outperforms because:
1. **Automated tree construction** via Rohrmeier grammar
2. **Empirically validated weights** from listener experiments
3. **Integration of all four components** in a single formula

---

## Practical Applications

### 1. Target Tension at Specific Points

```csharp
// Find chords that match target tension
public static IEnumerable<Chord> FindChordsWithTension(
    Scale key,
    double targetTension,
    double tolerance = 0.1)
{
    foreach (var degree in Enumerable.Range(1, 7))
    foreach (var quality in new[] { ChordQuality.Major, ChordQuality.Minor, ChordQuality.Dominant7 })
    {
        var chord = Chord.FromScaleDegree(key, degree, quality == ChordQuality.Minor);
        var tension = CalculateForChord(chord, key);

        if (Math.Abs(tension - targetTension) < tolerance)
        {
            yield return chord;
        }
    }
}
```

### 2. Design Tension Arcs

```csharp
// Generate progression matching target curve
public static List<Chord> GenerateProgressionForCurve(
    Scale key,
    double[] targetCurve)
{
    var progression = new List<Chord>();

    foreach (var targetTension in targetCurve)
    {
        var previous = progression.Count > 0 ? progression[^1] : null;
        var candidates = FindChordsWithTension(key, targetTension)
            .OrderBy(c => previous != null
                ? CalculateVoiceLeadingTension(previous, c)
                : 0);

        progression.Add(candidates.First());
    }

    return progression;
}
```

### 3. Balance Components for Effect

```
To maximize different effects:

Immediate Impact (Brainstem Reflex):
  → Maximize DISSONANCE component
  → Use diminished, augmented chords

Structural Tension (Expectancy):
  → Maximize HIERARCHICAL component
  → Use chords far from tonic function

Harmonic Distance (Journey):
  → Maximize TONAL DISTANCE component
  → Modulate to distant keys

Melodic Tension (Voice Leading):
  → Maximize VOICE LEADING component
  → Use large root motions, chromatic bass
```

---

## Academic Sources

### Primary Reference
- **Navarro-Cáceres, M., Caetano, M., Bernardes, G., Sánchez-Barba, M., & Merchán Sánchez-Jara, J.** (2020). "A Computational Model of Tonal Tension Profile of Chord Progressions in the Tonal Interval Space." *Entropy*, 22(11), 1291.

### Related Models
- Lerdahl, F. (2001). *Tonal Pitch Space*. Oxford University Press.
- Herremans, D. & Chew, E. (2016). "MorpheuS: Automatic music generation with recurrent pattern constraints." *IEEE Transactions on Affective Computing*.

### Foundational Theory
- Helmholtz, H. (1863/1954). *On the Sensations of Tone*. Dover.
- Plomp, R. & Levelt, W.J.M. (1965). "Tonal consonance and critical bandwidth." *JASA*, 38, 548-560.

---

## Key Takeaways

1. **Four components, validated weights**: T = 0.402D + 0.246H + 0.202T + 0.193VL
2. **Dissonance dominates**: 40% of perceived tension comes from roughness
3. **Chord-to-chord transitions don't matter much**: Intrinsic properties dominate
4. **Prototypical curves**: Arch, rising, falling, U-shape, fluctuating
5. **Resolution strength = tension drop**: Contrastive valence quantified

---

**Next**: [Part 5: Information Theory in Music](./05-information-theory.md)

---

*This document is part of the [Music Storyteller SDK](https://github.com/BeyondImmersion/bannou) documentation.*
