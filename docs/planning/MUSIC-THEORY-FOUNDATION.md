# Music Theory Foundation: The Formal Infrastructure

> **Status**: Deep Theoretical Exploration
> **Parent**: HIERARCHICAL-MUSIC-BEHAVIORS.md
> **Created**: 2025-01-17

This document defines the formal music theory infrastructure that underlies all musical generation. The goal is a **style-agnostic** system where Jazz, Baroque, Celtic, Minimalist, or any other style is simply a **configuration** applied to universal primitives.

---

## Design Principles

1. **Theory First, Style Second**: The infrastructure knows music theory. Styles are rule configurations.
2. **Everything is Data**: Scales, chords, progressions, forms - all queryable, all configurable.
3. **Constraints, Not Prescriptions**: The system knows what's *allowed* and what *tends to happen*, not what *must* happen.
4. **Composable Abstractions**: Higher levels build on lower levels without hiding them.
5. **Style Mixing**: Any style can blend with any other through weighted rule combination.

---

## Level 0: Physical Foundation

The absolute ground truth - how sound works.

```yaml
# foundation/physical.schema.yaml
physical:
  frequency:
    type: number
    unit: hertz
    description: "Vibrations per second, determines pitch"
    reference:
      A4: 440.0  # Concert pitch standard (configurable)

  amplitude:
    type: number
    range: [0.0, 1.0]
    description: "Loudness, 0 = silence, 1 = maximum"

  duration:
    type: number
    unit: milliseconds
    description: "How long the sound lasts"

  timbre:
    type: harmonic_series
    description: "Relative strengths of overtones"
    # This is where instrument identity lives
```

---

## Level 1: Pitch Space

How we organize and name pitches.

```yaml
# foundation/pitch.schema.yaml
pitch_class:
  description: "Note name independent of octave"
  type: enum
  values: [C, Cs, D, Ds, E, F, Fs, G, Gs, A, As, B]
  # Cs = C sharp, etc.
  # Enharmonic equivalents (C# = Db) handled by context

  aliases:
    Db: Cs
    Eb: Ds
    Gb: Fs
    Ab: Gs
    Bb: As

pitch:
  description: "Specific pitch = pitch class + octave"
  components:
    pitch_class: pitch_class
    octave: integer  # C4 = middle C

  conversions:
    to_midi:
      formula: "(octave + 1) * 12 + pitch_class.semitone_index"
      # C4 = 60, A4 = 69
    to_frequency:
      formula: "reference_A4 * 2^((midi - 69) / 12)"
      # 12-TET formula

interval:
  description: "Distance between two pitches"
  type: integer
  unit: semitones

  named_intervals:
    unison: 0
    minor_second: 1
    major_second: 2
    minor_third: 3
    major_third: 4
    perfect_fourth: 5
    tritone: 6
    perfect_fifth: 7
    minor_sixth: 8
    major_sixth: 9
    minor_seventh: 10
    major_seventh: 11
    octave: 12
    # Compound intervals continue...
    minor_ninth: 13
    major_ninth: 14
    # etc.

  properties:
    consonance:
      perfect_consonance: [0, 7, 12]       # Unison, P5, P8
      imperfect_consonance: [3, 4, 8, 9]   # 3rds and 6ths
      mild_dissonance: [2, 10]             # 2nds and 7ths
      sharp_dissonance: [1, 6, 11]         # m2, tritone, M7

tuning_system:
  description: "How the octave is divided"

  systems:
    twelve_tet:
      description: "12 tone equal temperament (modern standard)"
      divisions: 12
      ratio_per_step: "2^(1/12)"
      # ~1.05946

    just_intonation:
      description: "Pure frequency ratios"
      ratios:
        unison: "1/1"
        minor_second: "16/15"
        major_second: "9/8"
        minor_third: "6/5"
        major_third: "5/4"
        perfect_fourth: "4/3"
        perfect_fifth: "3/2"
        # etc.

    quarter_tone:
      description: "24 divisions per octave"
      divisions: 24
      ratio_per_step: "2^(1/24)"

    pythagorean:
      description: "Based on pure fifths"
      generator_interval: "3/2"
      # Stack of pure fifths, adjusted for octave
```

---

## Level 2: Collections

How pitches group into scales, modes, and chords.

```yaml
# foundation/collections.schema.yaml
scale:
  description: "Ordered collection of pitch classes with intervallic formula"

  definition:
    root: pitch_class
    intervals: integer[]  # Semitones from root for each degree

  # The fundamental scales (all others derive from these)
  fundamental_scales:
    chromatic:
      intervals: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]
      description: "All 12 pitches"

    major:
      intervals: [0, 2, 4, 5, 7, 9, 11]
      pattern: "W W H W W W H"  # Whole/half step pattern
      degrees: [1, 2, 3, 4, 5, 6, 7]

    natural_minor:
      intervals: [0, 2, 3, 5, 7, 8, 10]
      pattern: "W H W W H W W"
      relative_to: major
      relative_offset: -3  # Minor is 3 semitones below relative major

    harmonic_minor:
      intervals: [0, 2, 3, 5, 7, 8, 11]
      pattern: "W H W W H A H"  # A = augmented second
      description: "Raised 7th for dominant function"

    melodic_minor:
      intervals_ascending: [0, 2, 3, 5, 7, 9, 11]
      intervals_descending: [0, 2, 3, 5, 7, 8, 10]  # = natural minor
      description: "Different ascending vs descending"

    # Pentatonic scales
    major_pentatonic:
      intervals: [0, 2, 4, 7, 9]
      derived_from: major
      omitted_degrees: [4, 7]  # No 4th or 7th

    minor_pentatonic:
      intervals: [0, 3, 5, 7, 10]
      derived_from: natural_minor
      omitted_degrees: [2, 6]

    blues:
      intervals: [0, 3, 5, 6, 7, 10]
      description: "Minor pentatonic + blue note (b5)"
      blue_note: 6  # The tritone

mode:
  description: "Rotation of a scale, starting from different degree"

  modes_of_major:
    ionian:
      degree: 1
      intervals: [0, 2, 4, 5, 7, 9, 11]
      character: "Bright, stable, happy"

    dorian:
      degree: 2
      intervals: [0, 2, 3, 5, 7, 9, 10]
      character: "Minor but with brightness (raised 6th)"
      common_in: [jazz, celtic, folk]

    phrygian:
      degree: 3
      intervals: [0, 1, 3, 5, 7, 8, 10]
      character: "Dark, Spanish/flamenco feel (flat 2nd)"

    lydian:
      degree: 4
      intervals: [0, 2, 4, 6, 7, 9, 11]
      character: "Bright, dreamy, floating (raised 4th)"

    mixolydian:
      degree: 5
      intervals: [0, 2, 4, 5, 7, 9, 10]
      character: "Major but bluesy (flat 7th)"
      common_in: [rock, blues, celtic]

    aeolian:
      degree: 6
      intervals: [0, 2, 3, 5, 7, 8, 10]
      character: "Natural minor - sad, serious"

    locrian:
      degree: 7
      intervals: [0, 1, 3, 5, 6, 8, 10]
      character: "Unstable, diminished (flat 2nd, flat 5th)"
      rarely_used: true

chord:
  description: "Vertical structure of simultaneous pitches"

  definition:
    root: pitch_class
    quality: chord_quality
    extensions: integer[]  # Additional intervals beyond basic triad
    alterations: alteration[]  # Modifications (b5, #9, etc.)

  chord_qualities:
    # Triads (3 notes)
    major:
      intervals: [0, 4, 7]
      symbol: ""  # C = C major
      character: "Stable, happy, bright"

    minor:
      intervals: [0, 3, 7]
      symbol: "m"  # Cm
      character: "Stable, sad, dark"

    diminished:
      intervals: [0, 3, 6]
      symbol: "dim" or "°"
      character: "Unstable, tense, wants to resolve"

    augmented:
      intervals: [0, 4, 8]
      symbol: "aug" or "+"
      character: "Unstable, dreamlike, ambiguous"

    # Seventh chords (4 notes)
    major_seventh:
      intervals: [0, 4, 7, 11]
      symbol: "maj7" or "Δ7"
      character: "Lush, jazzy, sophisticated"

    dominant_seventh:
      intervals: [0, 4, 7, 10]
      symbol: "7"  # C7
      character: "Tension wanting resolution to tonic"

    minor_seventh:
      intervals: [0, 3, 7, 10]
      symbol: "m7"
      character: "Soft, jazzy minor"

    half_diminished:
      intervals: [0, 3, 6, 10]
      symbol: "ø7" or "m7b5"
      character: "Wistful, yearning"

    fully_diminished:
      intervals: [0, 3, 6, 9]
      symbol: "°7"
      character: "Very unstable, symmetrical (all m3rds)"

    # Extended chords (jazz)
    ninth:
      intervals: [0, 4, 7, 10, 14]
      symbol: "9"
      common_in: [jazz, soul, r&b]

    eleventh:
      intervals: [0, 4, 7, 10, 14, 17]
      symbol: "11"
      note: "Usually omit 3rd to avoid clash with 11th"

    thirteenth:
      intervals: [0, 4, 7, 10, 14, 17, 21]
      symbol: "13"
      note: "Usually omit 11th"

voicing:
  description: "How chord tones are arranged in register"

  types:
    close_position:
      description: "All notes within one octave"
      density: high

    open_position:
      description: "Notes spread across multiple octaves"
      density: low

    drop_2:
      description: "Second highest note dropped an octave"
      common_in: [jazz_guitar, jazz_piano]

    drop_3:
      description: "Third highest note dropped an octave"

    shell:
      description: "Root, 3rd, 7th only - minimal voicing"
      common_in: [jazz_comping]

    quartal:
      description: "Built from 4ths instead of 3rds"
      intervals: [0, 5, 10]  # Stacked P4ths
      common_in: [modern_jazz, impressionist]
```

---

## Level 3: Time

How music exists in time.

```yaml
# foundation/time.schema.yaml
beat:
  description: "The fundamental pulse unit"
  type: reference_point

tempo:
  description: "Speed of the beat"
  type: number
  unit: bpm  # Beats per minute

  markings:  # Traditional Italian tempo markings
    grave: [20, 40]
    largo: [40, 60]
    adagio: [60, 80]
    andante: [80, 100]
    moderato: [100, 120]
    allegro: [120, 160]
    vivace: [160, 180]
    presto: [180, 220]

meter:
  description: "Grouping of beats"

  definition:
    numerator: integer    # Beats per measure
    denominator: integer  # Note value that gets one beat

  types:
    simple:
      description: "Each beat divides into 2"
      examples:
        - time_signature: "4/4"
          beats_per_measure: 4
          feel: "Common time, most pop/rock"
        - time_signature: "3/4"
          beats_per_measure: 3
          feel: "Waltz"
        - time_signature: "2/4"
          beats_per_measure: 2
          feel: "March, polka"

    compound:
      description: "Each beat divides into 3"
      examples:
        - time_signature: "6/8"
          beats_per_measure: 2
          subdivisions: 3
          feel: "Two beats, each divided in 3"
        - time_signature: "9/8"
          beats_per_measure: 3
          subdivisions: 3
        - time_signature: "12/8"
          beats_per_measure: 4
          subdivisions: 3
          feel: "Blues shuffle, slow ballads"

    irregular:
      description: "Asymmetric groupings"
      examples:
        - time_signature: "5/4"
          groupings: ["3+2", "2+3"]
          feel: "Lopsided, dramatic"
        - time_signature: "7/8"
          groupings: ["2+2+3", "3+2+2", "2+3+2"]
          feel: "Driving, Balkan"

duration:
  description: "How long a note lasts relative to beat"

  values:
    whole: 4.0          # 4 beats in 4/4
    half: 2.0
    quarter: 1.0        # The "beat" in 4/4
    eighth: 0.5
    sixteenth: 0.25
    thirty_second: 0.125

  modifiers:
    dotted:
      description: "Add half the value"
      multiplier: 1.5
    double_dotted:
      description: "Add 3/4 the value"
      multiplier: 1.75
    triplet:
      description: "3 in the space of 2"
      multiplier: 0.666667

subdivision:
  description: "How beats are felt to divide"

  types:
    straight:
      description: "Even division"
      eighth_notes: [0.0, 0.5]  # Positions within beat

    swing:
      description: "Long-short feel"
      eighth_notes: [0.0, 0.667]  # Roughly triplet feel
      swing_ratio: 2:1  # Configurable
      common_in: [jazz, blues]

    shuffle:
      description: "Heavy swing, usually triplet-based"
      eighth_notes: [0.0, 0.667]
      articulation: "Second note accented"

    laid_back:
      description: "Slightly behind the beat"
      offset: -0.02  # Fraction of beat
      common_in: [hip_hop, neo_soul]

rhythm:
  description: "Pattern of durations and accents"

  representation:
    # Duration sequence (note values)
    durations: number[]
    # Attack points (when notes start)
    onsets: number[]
    # Accent pattern (emphasis)
    accents: number[]  # 0-1 strength

  patterns:
    # Common rhythmic cells
    steady_quarters:
      onsets: [0, 1, 2, 3]
      durations: [1, 1, 1, 1]

    backbeat:
      description: "Emphasis on 2 and 4"
      accents: [0.3, 1.0, 0.3, 1.0]
      common_in: [rock, pop, r&b]

    clave_3_2:
      description: "Afro-Cuban foundational rhythm"
      onsets: [0, 1.5, 2.5, 4, 5]
      accents: [1, 0.8, 0.8, 1, 0.8]

    tresillo:
      description: "3+3+2 pattern"
      onsets: [0, 1.5, 3]
      ubiquitous_in: [latin, afrobeat, pop]
```

---

## Level 4: Motion

How pitches move over time - melody and counterpoint.

```yaml
# foundation/motion.schema.yaml
melodic_motion:
  description: "Types of pitch movement"

  types:
    step:
      description: "Movement by scale degree (2nd)"
      interval_range: [1, 2]  # semitones
      smooth: true

    leap:
      description: "Movement by larger interval (3rd+)"
      interval_range: [3, 12]
      smooth: false

    skip:
      description: "Small leap (3rd or 4th)"
      interval_range: [3, 5]
      character: "Energetic but not jarring"

    jump:
      description: "Large leap (5th+)"
      interval_range: [7, 12]
      character: "Dramatic, needs careful handling"

contour:
  description: "Overall shape of melodic line"

  types:
    ascending:
      direction: up
      character: "Rising energy, tension, hope"

    descending:
      direction: down
      character: "Falling energy, release, melancholy"

    arch:
      shape: [up, peak, down]
      character: "Natural, balanced, sentence-like"
      common_use: "Standard phrase shape"

    inverted_arch:
      shape: [down, valley, up]
      character: "Unusual, can feel yearning"

    wave:
      shape: [oscillating]
      character: "Restless, searching"

    static:
      shape: [level]
      character: "Meditative, drone-like, tension through harmony"

voice_leading:
  description: "How individual voices move between chords"

  principles:
    # These are the fundamental rules
    smallest_motion:
      description: "Prefer stepwise motion"
      priority: high

    contrary_motion:
      description: "Voices move in opposite directions"
      purpose: "Independence of voices"

    avoid_parallel_fifths:
      description: "Don't move two voices in parallel P5ths"
      strength: strong  # Can be relaxed in some styles

    avoid_parallel_octaves:
      description: "Don't move two voices in parallel P8ves"
      strength: strong

    resolve_leading_tone:
      description: "7th degree wants to go to tonic"
      interval: +1 semitone
      strength: strong

    resolve_seventh:
      description: "Chord 7th wants to resolve down"
      interval: -1 or -2 semitones
      strength: moderate

counterpoint:
  description: "Rules for combining melodic lines"

  species:  # Traditional pedagogical categories
    first_species:
      description: "Note against note"
      rhythm: "All same duration"
      consonance: "All consonant intervals on beat"

    second_species:
      description: "Two notes against one"
      rhythm: "2:1 ratio"
      rules:
        - "Strong beat consonant"
        - "Weak beat can be dissonant if passing"

    third_species:
      description: "Four notes against one"
      rhythm: "4:1 ratio"
      rules:
        - "First beat consonant"
        - "Others can be passing/neighboring tones"

    fourth_species:
      description: "Syncopation (suspensions)"
      rhythm: "Tied notes across barline"
      rules:
        - "Dissonance on strong beat via suspension"
        - "Must resolve stepwise down"

    fifth_species:
      description: "Free counterpoint"
      rhythm: "Mixed"
      rules: "Combines all species techniques"

  interval_classification:
    perfect_consonance: [unison, fifth, octave]
    imperfect_consonance: [third, sixth]
    dissonance: [second, seventh, tritone]

  motion_types:
    parallel:
      description: "Same direction, same interval"
      restriction: "Forbidden for P5 and P8"
    similar:
      description: "Same direction, different interval"
      restriction: "Caution approaching perfect consonance"
    contrary:
      description: "Opposite directions"
      preference: "Generally preferred"
    oblique:
      description: "One voice moves, other stays"
      preference: "Good for independence"
```

---

## Level 5: Tension

How music creates and releases tension.

```yaml
# foundation/tension.schema.yaml
consonance_dissonance:
  description: "Stability spectrum of intervals and chords"

  interval_stability:
    # 0 = maximally consonant, 1 = maximally dissonant
    unison: 0.0
    octave: 0.05
    perfect_fifth: 0.1
    perfect_fourth: 0.2  # Contextually consonant
    major_third: 0.25
    minor_third: 0.3
    major_sixth: 0.3
    minor_sixth: 0.35
    major_second: 0.6
    minor_seventh: 0.65
    major_seventh: 0.8
    minor_second: 0.9
    tritone: 0.95

  # Note: These values are style-dependent!
  # Jazz treats 7ths as consonant, Baroque doesn't

harmonic_function:
  description: "Role of chord in progression"

  functions:
    tonic:
      symbol: T
      description: "Home, resolution, stability"
      chords_in_major: [I, vi, iii]
      tension_level: 0.0

    subdominant:
      symbol: S
      description: "Away from home, preparation"
      chords_in_major: [IV, ii]
      tension_level: 0.3

    dominant:
      symbol: D
      description: "Maximum tension, wants tonic"
      chords_in_major: [V, viio]
      tension_level: 0.8
      contains: "Leading tone (wants to resolve up to tonic)"

  substitutions:
    tritone_sub:
      description: "Replace V7 with bII7"
      reason: "Share same tritone"
      common_in: [jazz, bebop]

    relative_substitution:
      description: "Replace chord with relative minor/major"
      examples:
        - original: I
          substitute: vi
        - original: IV
          substitute: ii

resolution_tendencies:
  description: "Where unstable elements want to go"

  scale_degree_tendencies:
    # In major key
    1: { stable: true }
    2: { resolves_to: [1, 3], preference: down }
    3: { stable: true }
    4: { resolves_to: [3], strongly: true }
    5: { stable: true }
    6: { resolves_to: [5, 7], preference: down }
    7: { resolves_to: [1], strongly: true }  # Leading tone!

  chord_tendencies:
    V_to_I:
      strength: very_strong
      description: "The fundamental resolution"
    V_to_vi:
      strength: moderate
      description: "Deceptive cadence"
    ii_to_V:
      strength: strong
      description: "Pre-dominant to dominant"
    IV_to_I:
      strength: moderate
      description: "Plagal cadence"

cadence:
  description: "Harmonic punctuation patterns"

  types:
    authentic:
      progression: V → I
      strength: strong
      finality: high
      subtypes:
        perfect:
          requirements: ["root position V", "root position I", "tonic in soprano"]
        imperfect:
          requirements: ["Any V to I not meeting PAC requirements"]

    half:
      progression: "? → V"
      finality: low
      feeling: "Pause, question, more to come"
      common_approaches: [I, ii, IV, vi]

    plagal:
      progression: IV → I
      finality: medium
      feeling: "Amen cadence, gentle closure"

    deceptive:
      progression: V → vi
      finality: low
      feeling: "Surprise, continuation"

    phrygian:
      progression: iv6 → V (in minor)
      feeling: "Spanish, flamenco"
```

---

## Level 6: Structure

Large-scale organization.

```yaml
# foundation/structure.schema.yaml
motif:
  description: "Smallest recognizable musical idea"

  properties:
    pitch_content: pitch[]
    rhythm: rhythm_pattern
    contour: contour_type
    length: 1-8 notes typically

  transformations:
    transposition:
      description: "Move all pitches by same interval"
      preserves: [rhythm, contour]

    inversion:
      description: "Flip contour (up becomes down)"
      preserves: [rhythm, interval_sizes]

    retrograde:
      description: "Reverse order"
      preserves: [pitch_content]

    augmentation:
      description: "Stretch durations"
      multiplier: number > 1
      preserves: [pitch_content, contour]

    diminution:
      description: "Compress durations"
      multiplier: number < 1
      preserves: [pitch_content, contour]

    sequence:
      description: "Repeat at different pitch level"
      common_intervals: [2nd, 3rd, 4th, 5th]

phrase:
  description: "Complete musical thought"

  properties:
    length: 4-8 bars typically
    cadence: cadence_type  # How it ends
    function: [antecedent, consequent, continuation]

  structure:
    sentence:
      description: "Presentation + continuation + cadence"
      parts:
        presentation: 4 bars (idea + repetition)
        continuation: 4 bars (fragmentation → cadence)
      total: 8 bars typically

    period:
      description: "Antecedent + consequent"
      parts:
        antecedent: "Question phrase, ends half cadence"
        consequent: "Answer phrase, ends authentic cadence"
      relationship: "Similar beginnings, different endings"

form:
  description: "Large-scale organization"

  types:
    strophic:
      pattern: A A A A ...
      description: "Same music, different verses"
      common_in: [folk, hymns, pop_verses]

    binary:
      pattern: A B
      sections: 2
      description: "Two contrasting sections"
      subtypes:
        simple: "A ends in new key, B returns"
        rounded: "A B A' - B includes return of A"

    ternary:
      pattern: A B A
      sections: 3
      description: "Statement, contrast, return"

    rondo:
      pattern: A B A C A [D A]...
      description: "Recurring theme with episodes"

    sonata:
      sections:
        exposition:
          description: "Present themes"
          parts: [first_theme, transition, second_theme, closing]
          keys: [tonic, dominant_or_relative]
        development:
          description: "Explore and transform themes"
          keys: [unstable, modulating]
        recapitulation:
          description: "Return themes, now in tonic"
          parts: [first_theme, transition, second_theme, closing]
          keys: [tonic, tonic]
      optional:
        introduction: before exposition
        coda: after recapitulation

    verse_chorus:
      pattern: [intro, verse, chorus, verse, chorus, bridge, chorus, outro]
      description: "Standard popular song form"

    twelve_bar_blues:
      pattern: |
        I  I  I  I
        IV IV I  I
        V  IV I  V
      bars: 12
      description: "Foundation of blues and rock"

    thirty_two_bar:
      pattern: A A B A
      sections: 4 x 8 bars
      description: "Standard jazz/pop form"
      common_in: [jazz_standards, tin_pan_alley]
```

---

## Style Configuration System

Now the crucial part: styles are CONFIGURATIONS of all the above.

```yaml
# styles/jazz.style.yaml
style:
  id: jazz
  name: "Jazz"
  description: "American art music emphasizing improvisation, swing, and extended harmony"

  # What this style uses
  scales:
    primary: [major, dorian, mixolydian, blues]
    secondary: [melodic_minor, altered, diminished]
    rare: [whole_tone, lydian_dominant]

  modes:
    preferred: [dorian, mixolydian, lydian]
    avoid: [locrian]  # Except on m7b5 chords

  chords:
    minimum_complexity: seventh  # Triads are too simple
    common:
      - major_seventh
      - minor_seventh
      - dominant_seventh
      - half_diminished
    extended:
      - ninth
      - eleventh
      - thirteenth
    alterations:
      - b9, #9  # On dominant chords
      - #11     # On major chords
      - b13     # On dominant chords

  voicings:
    preferred: [drop_2, shell, quartal]
    avoid: [close_position]  # Too dense
    root_in_bass: optional  # Bass often plays root

  progressions:
    fundamental: ii-V-I
    common:
      - "ii-V-I"
      - "iii-VI-ii-V"
      - "I-vi-ii-V"
    substitutions:
      allow: [tritone_sub, relative_sub, chromatic_approach]
      frequency: high

  rhythm:
    subdivision: swing
    swing_ratio: [1.5, 2.0]  # Range of acceptable swing
    syncopation: high

  meter:
    common: ["4/4", "3/4"]
    feel: "Swing eighths unless marked 'even'"

  tempo:
    range: [60, 300]  # Ballad to up-tempo

  voice_leading:
    parallel_fifths: allowed  # Not a concern in jazz
    parallel_octaves: allowed
    smooth_motion: preferred

  consonance:
    # Redefine what's consonant!
    treat_as_consonant: [major_seventh, minor_seventh, ninth]
    tension: [b9, #9, #11, b13]  # These add color

  forms:
    common: [thirty_two_bar, twelve_bar_blues, modal_vamp]

  improvisation:
    expected: true
    over: [melody, harmony, both]

  dynamics:
    range: [pp, ff]
    expression: high_variance

  articulation:
    legato: common
    staccato: for_emphasis
    accent: "On and off beat"
    ghost_notes: yes

---

# styles/baroque.style.yaml
style:
  id: baroque
  name: "Baroque"
  period: "1600-1750"
  description: "Characterized by counterpoint, ornamentation, and terraced dynamics"

  scales:
    primary: [major, natural_minor, harmonic_minor]
    # Modes were not common in Baroque

  chords:
    complexity: triads_and_sevenths
    common: [major, minor, diminished, dominant_seventh]
    # Extended chords are anachronistic

  voicings:
    determined_by: figured_bass
    description: "Bass line with figures indicating intervals above"

  progressions:
    common:
      - "I-IV-V-I"
      - "I-ii-V-I"
      - "Circle of fifths"
    cadences:
      - authentic
      - half
      - phrygian  # In minor

  rhythm:
    subdivision: straight
    common_patterns:
      - walking_bass
      - alberti_bass  # Actually more Classical, but transitional
      - ostinato

  meter:
    common: ["4/4", "3/4", "6/8", "3/8"]

  counterpoint:
    importance: central
    types: [imitation, canon, fugue, invertible]
    voice_leading: strict
    parallel_fifths: forbidden
    parallel_octaves: forbidden

  dynamics:
    type: terraced
    description: "Sudden shifts, not gradual"
    levels: [piano, forte]

  ornamentation:
    types:
      trill:
        starts_on: upper_note  # Baroque convention
      mordent:
        types: [upper, lower]
      turn:
        placement: [on_beat, between_beats]
      appoggiatura:
        duration: "Half the main note"
    frequency: high
    often_improvised: true

  forms:
    common:
      - fugue
      - suite  # Allemande, Courante, Sarabande, Gigue
      - concerto_grosso
      - aria_da_capo
      - chaconne
      - passacaglia

  texture:
    common: [polyphonic, homophonic]
    monophonic: rare

  instrumentation:
    continuo: required  # Harpsichord/organ + cello/bassoon

---

# styles/celtic.style.yaml
style:
  id: celtic
  name: "Celtic/Irish Traditional"
  description: "Modal folk music of Ireland, Scotland, and related traditions"

  scales:
    primary: [major, dorian, mixolydian]
    characteristic: [dorian, mixolydian]  # What makes it "Celtic"
    avoid: [harmonic_minor]  # Too classical

  modes:
    dorian:
      character: "Melancholy but not dark"
      very_common: true
    mixolydian:
      character: "Bright, slightly bluesy"
      very_common: true

  chords:
    complexity: simple
    common: [major, minor]
    rare: [seventh]
    voicings: [open, droning]

  progressions:
    modal_nature: "Often avoids clear V-I"
    common:
      - "I-bVII-I"  # Mixolydian cadence
      - "i-bVII-i"  # Dorian cadence
      - "I-IV-I"
    drone: common  # Pedal tone underneath

  rhythm:
    subdivision: straight
    dance_forms:
      jig:
        meter: "6/8"
        feel: "Compound duple"
        accents: [1, 4]  # Beat 1 and 4 of 6
      reel:
        meter: "4/4"
        feel: "Driving straight eighths"
      hornpipe:
        meter: "4/4"
        feel: "Dotted, heavy"
      slip_jig:
        meter: "9/8"
        feel: "3+3+3"
      polka:
        meter: "2/4"

  ornamentation:
    crucial: true
    types:
      cut:
        description: "Brief upper note grace note"
        duration: very_short
      tip:
        description: "Brief lower note grace note"
      roll:
        description: "Note surrounded by upper/lower graces"
        sequence: [upper, main, lower, main]
      cran:
        description: "Multiple grace notes on repeated pitch"
        instrument_specific: true
      slide:
        description: "Approach from below"

  instrumentation:
    melody: [fiddle, tin_whistle, flute, uilleann_pipes, accordion]
    rhythm: [bodhran, guitar, bouzouki]
    accompaniment: [harp, piano]

  forms:
    tune_structure:
      parts: [A, B]  # Each 8 bars
      repeats: [AA, BB]
      total: 32 bars
    medley: common  # String multiple tunes together
```

---

## The Query Interface

Now the system can answer questions like:

```yaml
# Example queries the infrastructure can answer

query: "What chords are consonant in Jazz?"
answer:
  consonant:
    - major_seventh
    - minor_seventh
    - dominant_seventh_with_extensions
  reason: "Jazz redefines consonance to include 7ths as stable"

query: "What's a valid ii-V-I in C major with jazz voicings?"
answer:
  progression:
    - chord: Dm7
      voicing: [D, C, E, A]  # Shell + 5th
    - chord: G7
      voicing: [G, B, F, Ab]  # Shell + b9
    - chord: Cmaj7
      voicing: [C, E, B, D]  # Shell + 9
  voice_leading_analysis:
    - "C→B→B: Step down then hold"
    - "E→F→E: Neighbor tone"
    - etc.

query: "Generate a Celtic melody in D Dorian, 8 bars"
constraints_applied:
  - scale: D_dorian [D, E, F, G, A, B, C]
  - ornamentation: cuts_and_rolls
  - contour: arch_with_variation
  - phrase_structure: 4+4 with repetition
  - cadence: "ends on D, approached from C (bVII→i)"
```

---

## Integration with LLM Layer

The LLM's job is to translate natural language into queries and configurations:

```
Human: "Something that sounds like a sad Irish tune"

LLM interprets:
- Style: celtic
- Mode: dorian (sad but not dark)
- Tempo: moderate (60-80, not dance tempo)
- Instrumentation: solo_melody (intimate)
- Form: simple_AB
- Ornamentation: heavy (authentic)

LLM generates style configuration:
{
  base_style: "celtic",
  mode_preference: "dorian",
  tempo: 72,
  mood_modifiers: {
    "energy": 0.3,
    "brightness": 0.4,
    "complexity": 0.5
  },
  form: "AB_repeated"
}

System generates using rules.
```

---

## Open Questions for Implementation

### 1. Tuning System Scope
**Question**: Do we need to support non-Western tuning systems (quarter-tones, gamelan scales, maqam)?
- This significantly increases complexity
- Maybe start with 12-TET only, design for extensibility

### 2. Style Granularity
**Question**: How fine-grained should styles be?
- "Jazz" is huge - bebop vs cool vs fusion are very different
- Should we have `jazz.bebop`, `jazz.cool`, etc.?
- Or style inheritance: `bebop extends jazz`?

### 3. Rhythm Complexity
**Question**: How do we handle polyrhythm and metric modulation?
- Some music has multiple simultaneous meters
- African drumming, progressive rock, etc.

### 4. Voice Independence
**Question**: When generating multiple voices, how independent should they be?
- Full counterpoint: each voice has its own melodic logic
- Homophony: voices move together
- Hybrid: melody + accompaniment patterns

### 5. Performance vs Composition
**Question**: Are we generating:
- **Notation** (what to play)
- **Performance** (how to play it, with timing variation)
- Both?

### 6. Real-Time Constraints
**Question**: What needs to generate in real-time vs pre-computed?
- Can full compositions be generated ahead of time?
- Or do we need real-time generation for game responsiveness?

### 7. Validation
**Question**: How do we know if generated music is "good"?
- Technically correct != musically interesting
- Do we need aesthetic evaluation?
- Human-in-the-loop for training/refinement?

### 8. Lyrics/Vocals
**Question**: Do we need to handle vocal music?
- Text setting rules (stress, melisma)
- Vocal range constraints
- This is a whole additional domain

---

## Proposed Implementation Order

1. **Foundation schemas**: Pitch, interval, chord, scale definitions
2. **Style configuration loader**: Parse and apply style rules
3. **Chord/progression generator**: Given style + constraints, generate harmony
4. **Melody generator**: Given style + harmony + constraints, generate melody
5. **Multi-voice coordinator**: Combine melody + harmony + bass
6. **Form engine**: Organize into larger structures
7. **MIDI-JSON output**: Render to playable format
8. **LLM translation layer**: Natural language → configuration

---

*This foundation makes everything else possible. Get this right, and the rest follows.*
