# Music Data Strategy: Bootstrapping Style Knowledge

> **Status**: Research Complete, Strategy Defined
> **Created**: 2025-01-17
> **Key Insight**: Substantial pre-analyzed data exists - we should extract patterns, not define from scratch

---

## Executive Summary

Research reveals that while no comprehensive "style rules database" exists, there are massive corpora of analyzed music we can mine:

- **666,000 songs with chord progressions by genre** (Chordonomicon)
- **800,000 Celtic/folk tunes in parseable format** (The Session + ABC Archive)
- **108,000 classical works pre-analyzed** (KernScores)
- **Mature analysis tools** (music21, Partitura, Essentia)

**Strategy**: Extract patterns from existing data → formalize into style rules → validate with human curation.

---

## Part 1: Available Resources

### 1.1 Pre-Analyzed Datasets

#### Chordonomicon (Genre Chord Patterns)
- **Content**: 666,000 songs with chord progressions
- **Metadata**: Genre (37 genres), structural parts, release date (1907-2013)
- **Format**: Symbolic chord progressions
- **Value**: Directly shows what chord patterns define each genre
- **Access**: [Kaggle](https://www.kaggle.com/datasets/henryshan/a-dataset-of-666000-chord-progressions) | [GitHub](https://github.com/spyroskantarelis/chordonomicon)

**What we can extract**:
- Most common progressions per genre
- Chord vocabulary by genre (jazz uses 7ths, folk uses triads)
- Cadence patterns by genre
- Harmonic rhythm (how often chords change)

#### The Session (Celtic/Irish)
- **Content**: ~40,000 verified traditional Irish tunes
- **Format**: ABC notation (text-based, trivially parseable)
- **Value**: Definitive source for Celtic music patterns
- **Access**: [thesession.org](https://thesession.org/)

**What we can extract**:
- Mode distribution (% Dorian, Mixolydian, Major, etc.)
- Common melodic intervals
- Phrase structures (AABB form prevalence)
- Ornament patterns
- Rhythm patterns by tune type (jig, reel, hornpipe)

#### ABC Notation Archive
- **Content**: ~780,000 tunes total
- **Format**: ABC notation
- **Coverage**: Folk/traditional worldwide
- **Access**: [abcnotation.com](https://abcnotation.com/)

#### KernScores (Classical)
- **Content**: 108,703 files, 7.8 million notes
- **Format**: Humdrum **kern (text-based)
- **Value**: Pre-computed harmonic analysis available
- **Access**: [kern.ccarh.org](http://kern.ccarh.org/)

**What we can extract**:
- Voice leading patterns
- Counterpoint rules in practice
- Cadence frequencies
- Modulation patterns

#### Jazzomat (Jazz)
- **Content**: 456 transcribed jazz solos (1925-2009)
- **Annotations**: Chord sequences, phrase segmentation, beat positions, style metadata
- **Value**: Real jazz improvisation patterns with full analysis
- **Access**: [jazzomat.hfm-weimar.de](https://jazzomat.hfm-weimar.de/)

**What we can extract**:
- Melodic patterns over chord changes
- Phrase length distributions
- Note density and rhythmic patterns
- Scale/mode usage over specific chords

#### Lakh MIDI Dataset
- **Content**: 176,581 MIDI files
- **Cross-reference**: 45,129 matched to Million Song Dataset
- **Value**: Large-scale symbolic music for pattern mining
- **Access**: [colinraffel.com/projects/lmd](https://colinraffel.com/projects/lmd/)

---

### 1.2 Analysis Tools

#### music21 (Python) - Primary Tool
```python
from music21 import converter, analysis

# Parse ABC directly
score = converter.parse('irish_tune.abc')

# Extract key
key = score.analyze('key')

# Get chord progression
chords = score.chordify()

# Analyze intervals
intervals = analysis.discrete.MelodicInterval(score)
```

**Capabilities**:
- Reads: MIDI, ABC, MusicXML, Humdrum, MEI
- Analyzes: Key, chords, intervals, counterpoint, form
- Outputs: Structured Python objects

#### Partitura (Python) - Feature Extraction
```python
import partitura as pt

# Load and extract features
score = pt.load_musicxml('piece.xml')
note_array = pt.utils.music.compute_note_array(score)
piano_roll = pt.utils.music.compute_pianoroll(score)
```

**Capabilities**:
- Optimized for bulk feature extraction
- Outputs arrays ready for ML
- Voice separation, pitch spelling

#### Essentia (C++/Python) - Audio Analysis
```python
import essentia.standard as es

# Extract key from audio
audio = es.MonoLoader(filename='song.mp3')()
key, scale, strength = es.KeyExtractor()(audio)

# Extract tempo
bpm, beats, confidence = es.RhythmExtractor2013()(audio)
```

**Capabilities**:
- Key detection, tempo estimation
- Chord detection from audio
- 2x faster than librosa

---

## Part 2: Extraction Strategy

### 2.1 Phase 1: Celtic (The Session)

**Goal**: Define Celtic style configuration from data.

**Pipeline**:
```
The Session (40k ABC files)
    ↓
music21 parsing
    ↓
Extract per tune:
  - Key/mode
  - Tune type (jig/reel/etc)
  - Time signature
  - Melodic intervals
  - Phrase boundaries
  - Note durations
    ↓
Aggregate statistics:
  - Mode distribution
  - Interval frequency
  - Phrase length distribution
  - Rhythm patterns by tune type
    ↓
Formalize into CelticStyle configuration
    ↓
Human validation (does generated music sound Celtic?)
```

**Expected output**:
```yaml
style:
  id: celtic_traditional

  # Derived from data analysis
  modes:
    dorian: 0.35      # 35% of tunes
    mixolydian: 0.28
    major: 0.25
    minor: 0.10
    other: 0.02

  intervals:
    most_common: [unison, step_up, step_down, third_up, third_down]
    leap_frequency: 0.15  # 15% of intervals are leaps

  phrase_structure:
    aabb_form: 0.85  # 85% use AABB
    bars_per_section: 8

  tune_types:
    jig:
      meter: "6/8"
      tempo_range: [100, 130]
      note_density: high
    reel:
      meter: "4/4"
      tempo_range: [100, 130]
      subdivision: eighth_notes
    air:
      meter: "4/4"
      tempo_range: [50, 80]
      rubato: true
```

### 2.2 Phase 2: Genre Chord Patterns (Chordonomicon)

**Goal**: Extract chord progression rules per genre.

**Pipeline**:
```
Chordonomicon (666k songs)
    ↓
Parse chord progressions
    ↓
Group by genre
    ↓
Per genre, extract:
  - Most common progressions
  - Chord vocabulary (which chords used)
  - Average progression length
  - Cadence patterns
  - Chord transition probabilities
    ↓
Formalize into GenreHarmony configurations
```

**Expected output**:
```yaml
genre_harmony:
  jazz:
    common_progressions:
      - pattern: "ii7-V7-Imaj7"
        frequency: 0.45
      - pattern: "iii7-VI7-ii7-V7"
        frequency: 0.20
    chord_vocabulary:
      major_seventh: common
      dominant_seventh: very_common
      minor_seventh: very_common
      diminished: occasional
      triads: rare
    substitutions:
      tritone_sub: common

  folk:
    common_progressions:
      - pattern: "I-IV-V-I"
        frequency: 0.30
      - pattern: "I-V-I"
        frequency: 0.25
    chord_vocabulary:
      major_triad: very_common
      minor_triad: common
      seventh: rare
```

### 2.3 Phase 3: Classical Patterns (KernScores)

**Goal**: Extract counterpoint and voice leading patterns.

**Pipeline**:
```
KernScores (108k files)
    ↓
Humdrum analysis tools OR music21
    ↓
Extract:
  - Voice leading motions
  - Interval usage in counterpoint
  - Cadence patterns by era
  - Modulation targets
    ↓
Formalize into ClassicalRules
```

### 2.4 Phase 4: Jazz Improvisation (Jazzomat)

**Goal**: Extract melodic patterns over harmony.

**Pipeline**:
```
Jazzomat (456 solos with chord annotations)
    ↓
Align melody to chords
    ↓
Extract:
  - Scale choices per chord type
  - Common melodic cells
  - Phrase length patterns
  - Approach note patterns
    ↓
Formalize into JazzMelodyRules
```

---

## Part 3: Technical Implementation

### 3.1 Analysis Service

Create a service/tool for bulk music analysis:

```yaml
# schemas/analysis-api.yaml (internal tool, not game-facing)
paths:
  /analysis/parse-abc:
    post:
      summary: Parse ABC notation file
      requestBody:
        content:
          abc_content: string
      responses:
        200:
          schema:
            key: string
            mode: string
            meter: string
            notes: NoteSequence
            phrases: Phrase[]

  /analysis/extract-style-features:
    post:
      summary: Extract style features from parsed music
      requestBody:
        content:
          parsed_music: ParsedMusic
      responses:
        200:
          schema:
            mode_analysis: ModeAnalysis
            interval_analysis: IntervalAnalysis
            rhythm_analysis: RhythmAnalysis

  /analysis/aggregate-corpus:
    post:
      summary: Aggregate features across a corpus
      requestBody:
        content:
          corpus_id: string
          feature_type: string
      responses:
        200:
          schema:
            statistics: CorpusStatistics
```

### 3.2 Data Storage

Store extracted patterns in our existing state infrastructure:

```yaml
# State stores for music analysis data
music-corpus:
  backend: mysql
  description: "Parsed music files"
  schema:
    id: guid
    source: string  # "the_session", "kernscores", etc.
    format: string  # "abc", "midi", "kern"
    raw_content: text
    parsed_data: json

music-style-statistics:
  backend: mysql
  description: "Aggregated statistics per style"
  schema:
    style_id: string
    statistic_type: string  # "mode_distribution", "interval_frequency"
    data: json
    sample_size: integer
    last_updated: timestamp

music-patterns:
  backend: mysql
  description: "Extracted musical patterns"
  schema:
    id: guid
    pattern_type: string  # "progression", "phrase", "rhythm"
    style_tags: string[]
    pattern_data: json
    frequency: number
    examples: guid[]  # References to source pieces
```

### 3.3 Analysis Pipeline

```python
# Pseudocode for Celtic analysis pipeline

from music21 import converter, analysis
import json

def analyze_celtic_corpus(abc_files: list[str]) -> StyleStatistics:
    results = {
        'modes': Counter(),
        'intervals': Counter(),
        'meters': Counter(),
        'phrase_lengths': [],
    }

    for abc_file in abc_files:
        try:
            score = converter.parse(abc_file)

            # Mode analysis
            key = score.analyze('key')
            results['modes'][key.mode] += 1

            # Interval analysis
            for part in score.parts:
                melody = part.flatten().notes
                for i in range(len(melody) - 1):
                    interval = melody[i+1].pitch.midi - melody[i].pitch.midi
                    results['intervals'][interval] += 1

            # Meter
            results['meters'][str(score.timeSignature)] += 1

        except Exception as e:
            logging.warning(f"Failed to parse {abc_file}: {e}")

    return normalize_to_percentages(results)
```

---

## Part 4: What We Still Need to Build

### Theory Engine (Still Required)

The data gives us **what patterns exist**. We still need:

1. **Pattern Application**: Given "use ii-V-I 45% of the time", generate progressions
2. **Constraint Satisfaction**: Ensure generated music follows voice leading rules
3. **Melody Generation**: Use interval statistics to generate stylistically appropriate melodies
4. **Form Assembly**: Combine sections into complete pieces

The data informs the engine, but doesn't replace it.

### The Workflow

```
┌─────────────────────────────────────────────────────────────────┐
│  OFFLINE: Data Extraction & Analysis                            │
│                                                                  │
│  Corpora (ABC, MIDI, Kern)                                      │
│      ↓                                                          │
│  Analysis Pipeline (music21, custom scripts)                    │
│      ↓                                                          │
│  Style Statistics (stored in DB)                                │
│      ↓                                                          │
│  Human Curation (validate, adjust)                              │
│      ↓                                                          │
│  Style Configurations (YAML definitions)                        │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│  RUNTIME: Music Generation                                       │
│                                                                  │
│  Composition Request                                            │
│      ↓                                                          │
│  Load Style Configuration                                       │
│      ↓                                                          │
│  Theory Engine (uses style rules)                               │
│      ↓                                                          │
│  Generated Music (MIDI-JSON)                                    │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Part 5: Revised Implementation Order

### Phase 0: Data Acquisition (1-2 days)
- Download The Session ABC corpus
- Download Chordonomicon dataset
- Set up storage for raw data

### Phase 1: Analysis Pipeline (3-5 days)
- Build ABC parsing with music21
- Extract key/mode, intervals, meter, phrases
- Aggregate statistics across corpus
- Store results in database

### Phase 2: Celtic Style Definition (2-3 days)
- Analyze aggregated Celtic statistics
- Create CelticStyle configuration from data
- Human review and adjustment

### Phase 3: Theory Engine Core (5-7 days)
- Pitch/Interval/Scale modules
- Progression generator (informed by Chordonomicon)
- Melody generator (informed by interval statistics)
- MIDI-JSON output

### Phase 4: Validation Loop (ongoing)
- Generate Celtic pieces
- Human evaluation: does it sound Celtic?
- Adjust style parameters
- Iterate

### Phase 5: Expand Styles (per style, 3-5 days each)
- Jazz: Chordonomicon + Jazzomat analysis
- Classical: KernScores analysis
- Blues: Chordonomicon blues subset
- etc.

---

## Part 6: Key Insights

### What the Data Tells Us vs What It Doesn't

**Data Tells Us**:
- What progressions are common in a genre
- What modes/scales are used
- What intervals are typical
- What rhythms are characteristic
- Statistical distributions

**Data Doesn't Tell Us**:
- Why certain choices work
- How to make music interesting vs merely correct
- Artistic judgment
- What makes one Celtic tune better than another

**Human Curation Fills the Gap**:
- Review generated output
- Adjust parameters when things sound wrong
- Add rules that statistics miss
- Define aesthetic constraints

### The "Music Genome" Opportunity

Pandora proved this approach works with 450 "genes" per song. Our approach:
- Start with fewer, broader characteristics
- Expand as we learn what matters
- Data-driven discovery of what differentiates styles

---

## Appendix: Resource Links

### Datasets
- Chordonomicon: https://github.com/spyroskantarelis/chordonomicon
- The Session: https://thesession.org/
- ABC Archive: https://abcnotation.com/
- KernScores: http://kern.ccarh.org/
- Jazzomat: https://jazzomat.hfm-weimar.de/
- Lakh MIDI: https://colinraffel.com/projects/lmd/

### Tools
- music21: https://web.mit.edu/music21/
- Partitura: https://github.com/CPJKU/partitura
- Essentia: https://essentia.upf.edu/
- Humdrum: https://www.humdrum.org/

### Research
- Chordonomicon paper: https://arxiv.org/abs/2410.22046
- music21 paper: https://web.mit.edu/music21/doc/about/referenceCorpus.html

---

*This strategy leverages existing work rather than reinventing it. We stand on the shoulders of musicologists who've already done the hard analysis work.*
