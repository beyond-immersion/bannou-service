# Music Analysis Results

> **Analysis Date**: January 17, 2026
> **Repository**: `~/repos/music-analysis/`

This document summarizes the results of corpus-scale music analysis performed to bootstrap the music-theory SDK's style configurations with empirically-grounded data.

---

## Overview

Two major datasets were analyzed:

| Dataset | Source | Records | Success Rate | Output Size |
|---------|--------|---------|--------------|-------------|
| The Session | thesession.org | 53,522 tunes | 99.73% | 64 MB |
| Chordonomicon | GitHub | 679,807 songs | 100% | 3.8 GB |

---

## Celtic/Irish Traditional Music Analysis

**Source**: [The Session](https://thesession.org/) - crowd-sourced database of traditional Irish music
**License**: Public Domain (traditional tunes)
**Output Files**:
- `output/celtic_full.json` - Per-tune feature extraction
- `output/celtic_style_full.yaml` - Aggregated style configuration

### Parse Statistics

| Metric | Value |
|--------|-------|
| Total tunes analyzed | 53,522 |
| Successful parses | 53,375 |
| Parse failures | 147 |
| **Success rate** | **99.73%** |

### Mode Distribution

Empirically measured mode usage across the Celtic corpus:

| Mode | Percentage | Count |
|------|------------|-------|
| **Major (Ionian)** | 64.02% | 34,180 |
| **Minor (Aeolian)** | 15.53% | 8,290 |
| **Dorian** | 13.58% | 7,251 |
| **Mixolydian** | 6.87% | 3,669 |

**Key Finding**: The SDK's original Celtic style had Major at 64%, which aligns almost exactly with the empirical data. However, Minor was underweighted (16% original vs 15.5% empirical) and Dorian was slightly overweighted (14% original vs 13.6% empirical).

### Tune Type Distribution

| Tune Type | Count | Percentage | Primary Meter | Avg Notes |
|-----------|-------|------------|---------------|-----------|
| **Reel** | 19,451 | 36.44% | 4/4 | 135.5 |
| **Jig** | 13,529 | 25.35% | 6/8 | 114.5 |
| **Polka** | 4,121 | 7.72% | 2/4 | 88.0 |
| **Waltz** | 3,855 | 7.22% | 3/4 | 119.1 |
| **Hornpipe** | 3,664 | 6.86% | 4/4 | 144.5 |
| **Slip Jig** | 1,908 | 3.57% | 9/8 | 107.9 |
| **March** | 1,691 | 3.17% | 4/4 | 134.0 |
| **Barndance** | 1,698 | 3.18% | 4/4 | 124.6 |
| **Strathspey** | 1,336 | 2.50% | 4/4 | 115.5 |
| **Slide** | 1,144 | 2.14% | 12/8 | 94.5 |
| **Mazurka** | 568 | 1.06% | 3/4 | 105.2 |
| **Three-Two** | 410 | 0.77% | 3/2 | 107.0 |

**Key Finding**: Reels and Jigs together account for 61.8% of all Celtic tunes.

### Interval Distribution

Melodic interval frequency analysis:

| Interval Category | Percentage | Breakdown |
|-------------------|------------|-----------|
| **Steps (2nds)** | 47.69% | Up: 23.46%, Down: 24.23% |
| **Thirds** | 24.77% | Up: 10.11%, Down: 14.66% |
| **Leaps (4th+)** | 17.77% | 4ths: 7.10%, 5ths: 3.55%, Larger: 7.12% |
| **Unisons** | 9.76% | Repeated notes |

**Key Finding**: Celtic melody is predominantly stepwise (47.7%), with descending motion slightly more common than ascending. This confirms the SDK's `IntervalPreferences.Celtic` configuration.

### Rhythm Analysis

| Metric | Value |
|--------|-------|
| **Dominant note duration** | Eighth note (68.92%) |
| **Quarter notes** | 10.07% |
| **Sixteenth notes** | 7.90% |
| **Dotted eighth notes** | 2.54% |
| **Eighth triplets** | 3.05% |

| Structural Metric | Value |
|-------------------|-------|
| Average measures per tune | 21.0 |
| Common phrase lengths | 18, 16, 17 measures |
| Average syncopation ratio | 50.96% |
| On-beat note percentage | 49.85% |

### Pitch Statistics

| Metric | Value |
|--------|-------|
| Average pitch range | 19.8 semitones |
| Minimum range | 0 (single note) |
| Maximum range | 62 semitones (5+ octaves) |
| Average unique pitches | 12.1 per tune |

### Composition Attribution

| Category | Percentage | Count |
|----------|------------|-------|
| **Traditional (anonymous)** | 66.86% | 35,702 |
| **Composed (attributed)** | 33.14% | 17,707 |

**Top 5 Composers**:
1. Turlough O'Carolan
2. Paddy O'Brien
3. Sean Ryan
4. Gian Marco Pietrasanta
5. Ed Reavy

**Total unique composers**: 2,481

### Popularity Metrics

| Metric | Value |
|--------|-------|
| Tunes with popularity data | 39,021 |
| Average tunebook appearances | 325.9 |
| Median tunebook appearances | 88 |
| Maximum tunebook appearances | 7,554 |

---

## Chordonomicon Chord Progression Analysis

**Source**: [Chordonomicon](https://github.com/ology/Chordonomicon) - chord progressions from popular music
**License**: Apache 2.0
**Output Files**:
- `output/chords_full.json` - Per-song chord data
- `output/harmony_styles_full.yaml` - Genre-specific harmony profiles

### Parse Statistics

| Metric | Value |
|--------|-------|
| Total songs analyzed | 679,807 |
| Total chord occurrences | 51,994,634 |
| **Success rate** | **100%** |

### Global Chord Distribution

**Top 25 Most Common Chords**:

| Rank | Chord | Percentage | | Rank | Chord | Percentage |
|------|-------|------------|---|------|-------|------------|
| 1 | G | 13.55% | | 14 | F# | 1.37% |
| 2 | C | 11.17% | | 15 | C#m | 1.17% |
| 3 | D | 10.13% | | 16 | Gm | 1.05% |
| 4 | A | 7.62% | | 17 | Eb | 0.96% |
| 5 | F | 6.47% | | 18 | Cm | 0.77% |
| 6 | Am | 5.60% | | 19 | Ab | 0.69% |
| 7 | E | 5.27% | | 20 | C# | 0.66% |
| 8 | Em | 5.05% | | 21 | G#m | 0.62% |
| 9 | Bm | 2.74% | | 22 | A7 | 0.57% |
| 10 | B | 2.72% | | 23 | Fm | 0.55% |
| 11 | Dm | 2.39% | | 24 | D7 | 0.54% |
| 12 | Bb | 1.96% | | 25 | G# | 0.54% |
| 13 | F#m | 1.69% | | | | |

**Key Finding**: G, C, D, A, and F account for 48.94% of all chord usage. The "cowboy chords" (open position guitar chords) dominate popular music harmony.

### Chord Quality Distribution

| Quality | Percentage |
|---------|------------|
| **Major** | 64.31% |
| **Minor** | 22.52% |
| **Dominant 7th** | 3.35% |
| **Minor 7th** | 2.48% |
| **Major 7th** | 1.30% |
| **Sus4** | 0.70% |
| **Add9** | 0.65% |
| **Sus2** | 0.48% |
| **Slash chords** | ~1.5% |
| **Diminished** | 0.19% |
| **Augmented** | 0.04% |

**Key Finding**: Simple triads (major + minor) account for 86.83% of all chord usage. Extended harmonies (7ths, 9ths, etc.) are relatively rare in popular music.

### Genre Profiles Generated

The analysis produced harmony profiles for 13 genres:

| Genre | Notes |
|-------|-------|
| **Pop** | Bright major-key progressions, I-V-vi-IV common |
| **Rock** | Power chord influenced, more minor modes |
| **Country** | Traditional I-IV-V, Nashville number system |
| **Alternative** | More modal interchange, unconventional progressions |
| **Pop Rock** | Hybrid of pop brightness and rock energy |
| **Punk** | Simple progressions, fast tempo markers |
| **Metal** | Minor modes, diminished usage, power chords |
| **Rap** | Minimal harmony, loop-based progressions |
| **Soul** | Extended chords, ii-V-I influences |
| **Jazz** | Complex harmony, secondary dominants |
| **Reggae** | Offbeat emphasis, simple progressions |
| **Electronic** | Minimal harmony, drone-based |
| **Unknown** | Unclassified songs |

---

## Implications for SDK Configuration

### Celtic Style Updates

The empirical data suggests these refinements to `BuiltInStyles.Celtic`:

```csharp
ModeDistribution = new ModeDistribution
{
    Major = 0.64,      // Confirmed accurate
    Minor = 0.155,     // Was 0.16, slightly lower
    Dorian = 0.136,    // Was 0.14, slightly lower
    Mixolydian = 0.069 // Was 0.06, slightly higher
}
```

### New Tune Types to Add

The analysis revealed tune types not in the current SDK:
- **Slide** (12/8 meter) - 2.14% of corpus
- **Three-Two** (3/2 meter) - 0.77% of corpus
- **Mazurka** (3/4 meter) - 1.06% of corpus
- **Barndance** (4/4 meter) - 3.18% of corpus
- **Strathspey** (4/4 with dotted rhythms) - 2.50% of corpus

### Genre-Specific Harmony Profiles

The Chordonomicon data enables creation of genre-specific `HarmonyStyleDefinition` instances with empirically-measured:
- Common chord progressions per genre
- Secondary dominant probability
- Modal interchange probability
- Typical chord qualities and extensions

---

## File Locations

### Analysis Repository
```
~/repos/music-analysis/
├── output/
│   ├── celtic_full.json          # 64 MB - raw Celtic features
│   ├── celtic_style_full.yaml    # Aggregated Celtic style
│   ├── chords_full.json          # 3.8 GB - raw chord data
│   └── harmony_styles_full.yaml  # Genre harmony profiles
├── checkpoints/
│   ├── checkpoint_*.json         # Celtic checkpoints (10k intervals)
│   └── chords_checkpoint_*.json  # Chord checkpoints (100k intervals)
└── src/
    ├── analyze_tune.py           # Single tune feature extraction
    ├── parse_session.py          # Celtic batch processing
    ├── aggregate_features.py     # Celtic aggregation
    ├── parse_chordonomicon.py    # Chord progression parsing
    └── aggregate_chords.py       # Chord aggregation
```

### Source Data
```
~/repos/thesession-data/csv/
├── tunes.csv                     # ABC notation for all tunes
└── tune_popularity.csv           # Tunebook appearance counts
```

---

## Next Steps

1. **Update `BuiltInStyles.Celtic`** with empirical mode distribution
2. **Add missing tune types** (Slide, Three-Two, Mazurka, Barndance, Strathspey)
3. **Create genre styles** from Chordonomicon data (Pop, Rock, Jazz, etc.)
4. **Validate rhythm patterns** against empirical duration distributions
5. **Import top composers** for attribution in generated compositions

---

*Analysis performed using music21 9.x for ABC parsing and custom Python aggregation scripts.*
