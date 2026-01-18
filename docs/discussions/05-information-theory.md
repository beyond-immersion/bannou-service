# Part 5: Information Theory in Music

> **Series**: [Understanding the Music Storyteller](./00-index-music-storyteller.md) | Part 5 of 7

## Music as Information

When you hear a C major chord followed by a G7, your brain is doing something remarkable: it's predicting what comes next. Based on years of musical exposure, it assigns probabilities to possible continuations—and C major is overwhelmingly likely.

**Information theory** provides the mathematics to quantify this prediction process. It lets us measure:
- **Entropy**: How uncertain you are *before* an event
- **Information Content**: How surprised you are *after* an event
- **Expectation**: The statistical regularities your brain has learned

---

## The Fundamental Equations

### Information Content (Surprise)

```
IC(event) = -log₂ P(event | context)

Where:
  P(event | context) = probability of this event given what came before
  IC = Information Content in bits
```

**Interpretation**:
- IC of 0 bits = P(event) = 100% (completely expected)
- IC of 1 bit = P(event) = 50% (coin flip)
- IC of 3 bits = P(event) = 12.5% (moderately surprising)
- IC of 6 bits = P(event) = 1.6% (very surprising)

### Entropy (Uncertainty)

```
H = -Σ p(eᵢ) × log₂ p(eᵢ)

Where:
  p(eᵢ) = probability of each possible event
  H = Entropy in bits
```

**Interpretation**:
- H = 0 = one outcome has 100% probability (certainty)
- H = 1 = two equally likely outcomes
- H = log₂(n) = n equally likely outcomes (maximum uncertainty)

---

## Visualizing IC and Entropy

```
Information Content vs Probability
==================================

IC (bits)
│
6 ┤                                          ●
  │                                      ●
5 ┤                                  ●
  │                              ●
4 ┤                          ●
  │                      ●
3 ┤                  ●
  │              ●
2 ┤          ●
  │       ●
1 ┤    ●
  │  ●
0 ┼●─────────────────────────────────────────
  0%   10%   20%   30%   40%   50%   60%  100%
                   Probability

IC = -log₂(P)
```

```
Entropy Example: Two Outcomes
=============================

H (bits)
│
1.0 ┤              ●●●●●
    │           ●●       ●●
    │         ●             ●
0.8 ┤        ●               ●
    │       ●                 ●
0.6 ┤      ●                   ●
    │     ●                     ●
0.4 ┤    ●                       ●
    │   ●                         ●
0.2 ┤  ●                           ●
    │ ●                             ●
0.0 ┼●─────────────────────────────────●
    0%   20%   40%   50%   60%   80%  100%
         Probability of outcome A

Peak entropy = 50/50 split = 1.0 bits
Minimum entropy = 0% or 100% = 0.0 bits
```

---

## Four Types of Expectation

The listener maintains four parallel expectation systems (from [Part 2: ITPRA](./02-expectation-itpra.md)):

```
Four Expectation Types
======================

┌────────────────────────────────────────────────────────────────┐
│                      MEMORY TYPE                              │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│   SEMANTIC (long-term)        EPISODIC (life events)          │
│   ┌──────────────────┐        ┌──────────────────┐            │
│   │    SCHEMATIC     │        │    VERIDICAL     │            │
│   │                  │        │                  │            │
│   │ "V usually → I"  │        │ "This piece has  │            │
│   │ "Phrases are 4   │        │  that surprise   │            │
│   │  bars long"      │        │  in bar 23"      │            │
│   └──────────────────┘        └──────────────────┘            │
│                                                                │
│   SHORT-TERM (3-5 sec)        WORKING (active thought)        │
│   ┌──────────────────┐        ┌──────────────────┐            │
│   │    DYNAMIC       │        │    CONSCIOUS     │            │
│   │                  │        │                  │            │
│   │ "This piece uses │        │ "I bet this goes │            │
│   │  a lot of I-vi-  │        │  to the minor"   │            │
│   │  IV-V loops"     │        │                  │            │
│   └──────────────────┘        └──────────────────┘            │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

### Implementation: The Four Expectation Classes

```csharp
/// Schematic: Style-based conventions (semantic memory)
public sealed class SchematicExpectations
{
    public double AuthenticCadenceProbability { get; set; } = 0.7;
    public double TonicResolutionProbability { get; set; } = 0.8;
    public int ExpectedPhraseLength { get; set; } = 4;

    public void LoadStyleDefaults(string styleId)
    {
        AuthenticCadenceProbability = styleId switch
        {
            "celtic" => 0.8,
            "jazz" => 0.6,
            "baroque" => 0.75,
            _ => 0.7
        };
    }
}

/// Veridical: This specific piece (episodic memory)
public sealed class VeridicalExpectations
{
    public List<string> RecognizedPatterns { get; } = [];
    public List<int> MainThemeAppearances { get; } = [];
    public bool FormRecognized { get; set; }
}

/// Dynamic: Patterns in this listening (short-term memory)
public sealed class DynamicExpectations
{
    public List<int> RecentIntervals { get; } = new(25);  // ~10-25 events
    public bool PatternDetected { get; set; }
    public int PatternLength { get; set; }
}

/// Conscious: Active predictions
public sealed class ConsciousExpectations
{
    public bool ExpectingCadence { get; set; }
    public string? ExpectedHarmony { get; set; }
    public int BarsUntilExpected { get; set; }
}
```

---

## Reference IC Values

```
IC Reference Values
===================

Event Type                 Probability    IC (bits)    Interpretation
────────────────────────────────────────────────────────────────────
V → I resolution           ~40%           1.3          Very expected
V → vi deceptive           ~10%           3.3          Surprising
Stepwise melody            ~35%           1.5          Expected
Leap (P5 or greater)       ~5%            4.3          Surprising
Secondary dominant         ~3%            5.0          Very surprising
Chromatic chord            ~1%            6.6          Shocking

┌─────────────────────────────────────────────────────────────────┐
│ IC = 0        1        2        3        4        5        6    │
│     │────────│────────│────────│────────│────────│────────│    │
│     ●        ●                  ●                  ●             │
│  Certain  Common   Expected   Moderate  Uncommon  Rare   V.Rare│
│                                                                 │
│         "Prediction Effect"      "Surprise Effect"              │
│         Pleasure from            Pleasure from                  │
│         being right              contrast                       │
└─────────────────────────────────────────────────────────────────┘
```

---

## Implementation: Information Content

```csharp
public static class InformationContent
{
    /// Calculates IC from probability: IC = -log₂(p)
    public static double Calculate(double probability)
    {
        if (probability <= 0) return double.MaxValue;  // Impossible = infinite surprise
        if (probability >= 1) return 0;                // Certain = no surprise

        return -Math.Log2(probability);
    }

    /// IC for melodic intervals
    public static double CalculateForInterval(int interval)
    {
        var probability = GetIntervalProbability(interval);
        return Calculate(probability);
    }

    /// IC for harmonic progressions
    public static double CalculateForHarmony(int fromDegree, int toDegree)
    {
        var probability = GetHarmonicTransitionProbability(fromDegree, toDegree);
        return Calculate(probability);
    }

    /// Normalize IC to 0-1 scale
    public static double NormalizeSurprise(double ic)
    {
        // Typical IC range is 0-6 bits
        return 1.0 - 1.0 / (1.0 + ic / 3.0);
    }
}
```

---

## Transition Probabilities

The system uses empirical transition matrices:

### Melodic Interval Distribution

```
Interval Distribution (Western tonal music)
===========================================

Interval    Semitones    Probability    IC
────────────────────────────────────────────
Unison      0            20%            2.3
Minor 2nd   1            18%            2.5
Major 2nd   2            22%            2.2
Minor 3rd   3            12%            3.1
Major 3rd   4            10%            3.3
Perfect 4th 5            7%             3.8
Tritone     6            2%             5.6
Perfect 5th 7            5%             4.3
Minor 6th   8            2%             5.6
Major 6th   9            1%             6.6
Minor 7th   10           0.5%           7.6
Major 7th   11           0.3%           8.4
Octave      12           1%             6.6
```

### Harmonic Transition Matrix

```
Harmonic Transitions (Common Practice Period)
=============================================

From →   I     ii    iii   IV    V     vi    vii°
─────────────────────────────────────────────────
I        8%    14%   5%    31%   38%   10%   2%
ii       4%    -     1%    2%    87%   5%    1%
iii      4%    4%    -     51%   8%    32%   1%
IV       17%   19%   2%    -     49%   5%    8%
V        79%   3%    1%    1%    -     15%   1%
vi       5%    44%   2%    27%   18%   -     4%
vii°     86%   1%    0%    1%    2%    9%    -

Key insight: V → I at 79% is extremely predictable (IC ≈ 0.34 bits)
             V → vi at 15% is surprising (IC ≈ 2.74 bits)
```

---

## The Listener Model

The system tracks listener cognitive state:

```csharp
public sealed class ListenerModel
{
    /// Current attention level (0-1)
    public double Attention { get; set; } = 0.8;

    /// How much surprise is acceptable
    public double SurpriseBudget { get; set; } = 0.5;

    /// Whether resolution is expected
    public bool ExpectedResolution { get; set; }

    /// Recent prediction accuracy (rolling average)
    public double PredictionAccuracy { get; set; } = 0.7;

    /// Recent surprise accumulation
    public double RecentSurprises { get; set; }

    /// Current engagement = (Attention + Accuracy) / 2
    public double Engagement => (Attention + PredictionAccuracy) / 2.0;

    /// Four expectation types
    public SchematicExpectations Schematic { get; } = new();
    public VeridicalExpectations Veridical { get; } = new();
    public DynamicExpectations Dynamic { get; } = new();
    public ConsciousExpectations Conscious { get; } = new();
}
```

### Processing Musical Events

```csharp
public void ProcessEvent(double surprise, bool wasResolution, double tensionBefore)
{
    EventsProcessed++;

    // Update prediction accuracy (rolling average)
    var accuracy = 1.0 - surprise;
    PredictionAccuracy = PredictionAccuracy * 0.9 + accuracy * 0.1;

    // Attention: boosted by surprise, decays with predictability
    if (surprise > 0.5)
    {
        Attention = Math.Min(1.0, Attention + surprise * 0.2);
        RecentSurprises += surprise;
    }
    else
    {
        Attention = Math.Max(0.3, Attention - 0.02);
    }

    // Handle resolution (contrastive valence)
    if (wasResolution && ExpectedResolution)
    {
        var pleasure = tensionBefore * 0.8;  // Scales with prior tension
        AccumulatedPleasure += pleasure;
        ExpectedResolution = false;
    }

    // Adjust surprise budget based on recent history
    if (RecentSurprises > 1.5)
    {
        SurpriseBudget = Math.Max(0.2, SurpriseBudget - 0.1);  // Too much
    }
    else if (RecentSurprises < 0.3 && EventsProcessed > 10)
    {
        SurpriseBudget = Math.Min(0.8, SurpriseBudget + 0.05);  // Too little
    }
}
```

---

## The Optimal Surprise Zone

From the Cheung model ([Part 2](./02-expectation-itpra.md)), pleasure peaks at:
- **Low uncertainty + High surprise**: Unexpected in predictable context
- **High uncertainty + Low surprise**: Resolution in ambiguous context

```
Optimal Surprise Zone
=====================

Surprise
  High │
       │      ┌─────────────────┐
       │      │  PLEASURE ZONE  │
       │      │  (deceptive     │
       │      │   cadence,      │
       │      │   chromatic     │
       │      │   chord)        │
       │      └─────────────────┘
  Med  │────────────────────────────────
       │      ┌─────────────────┐
       │      │   BORING ZONE   │
       │      │   (predictable  │
       │      │    but nothing  │
       │      │    interesting) │
       │      └─────────────────┘
  Low  │────────────────────────────────
       │      ┌─────────────────┐
       │      │  PLEASURE ZONE  │
       │      │  (resolution,   │
       │      │   confirmation) │
       │      └─────────────────┘
       └──────────────────────────────→
             Low        Med       High
                     Uncertainty
```

---

## Dynamic Pattern Detection

The system detects repeating patterns in recent events:

```csharp
private void DetectPattern()
{
    if (RecentIntervals.Count < 6) return;

    // Check for patterns of length 2-4
    for (var len = 2; len <= 4; len++)
    {
        if (RecentIntervals.Count >= len * 2)
        {
            var isPattern = true;
            for (var i = 0; i < len; i++)
            {
                if (RecentIntervals[^(len + 1 + i)] != RecentIntervals[^(1 + i)])
                {
                    isPattern = false;
                    break;
                }
            }
            if (isPattern)
            {
                PatternDetected = true;
                PatternLength = len;
                return;
            }
        }
    }
    PatternDetected = false;
}
```

**Why this matters**: When a pattern is detected, the listener's dynamic expectations increase probability of pattern continuation—violating the pattern becomes MORE surprising.

---

## Connection to Other Frameworks

| Framework | Information Theory Role |
|-----------|------------------------|
| **ITPRA** (Huron) | P response = reaction to IC value |
| **TPS** (Lerdahl) | Higher δ → higher IC (less probable) |
| **BRECVEMA** | Musical Expectancy mechanism |
| **Cheung Model** | Pleasure = f(Entropy × IC) |
| **IDyOM** | Computational implementation of statistical learning |

---

## Academic Sources

### Primary Reference
- **Pearce, M.T.** (2005). *The Construction and Evaluation of Statistical Models of Melodic Structure in Music Perception and Composition*. PhD Thesis, City University London.

### Information Theory Foundations
- Shannon, C.E. (1948). "A Mathematical Theory of Communication." *Bell System Technical Journal*.
- Meyer, L.B. (1957). "Meaning in music and information theory." *Journal of Aesthetics and Art Criticism*.

### IDyOM Implementation
- Pearce, M.T. & Wiggins, G.A. (2012). "Auditory Expectation: The Information Dynamics of Music Perception and Cognition." *Topics in Cognitive Science*.

### Empirical Validation
- Cheung, V.K.M., et al. (2019). "Uncertainty and Surprise Jointly Predict Musical Pleasure." *Current Biology*.

---

## Key Takeaways

1. **IC = -log₂(P)**: Surprise is quantifiable
2. **Four expectation types**: Schematic, veridical, dynamic, conscious
3. **Optimal surprise zone**: Not too predictable, not too chaotic
4. **Transition matrices**: Empirical probabilities from corpus analysis
5. **Listener adaptation**: Surprise budget adjusts based on recent history

---

**Next**: [Part 6: GOAP - Planning Musical Narratives](./06-goap-planning.md)

---

*This document is part of the [Music Storyteller SDK](https://github.com/BeyondImmersion/bannou) documentation.*
