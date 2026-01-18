# Composer Layer: Personality-Driven Music Generation

> **Status**: Planning
> **Last Updated**: January 2026

This document describes the future "Composer" layer - a personality and preference system that sits above the MusicStoryteller SDK to enable creating distinct musical personalities (virtual composers) that generate music with consistent stylistic signatures.

---

## Overview

The current music system has two layers:
1. **MusicTheory** - Low-level primitives (pitches, chords, scales, voice leading)
2. **MusicStoryteller** - Narrative composition (emotional arcs, GOAP planning, intent generation)

The **Composer Layer** adds a third level:
3. **Composer** - Personality-driven generation (preferences, tendencies, stylistic constraints)

```
┌─────────────────────────────────────────────────────────────┐
│                     Composer Layer                           │
│  ┌────────────────┐  ┌────────────────┐  ┌──────────────┐  │
│  │ Personality    │  │ Style Blend    │  │ Preference   │  │
│  │ Definition     │  │ Engine         │  │ Evolution    │  │
│  └────────────────┘  └────────────────┘  └──────────────┘  │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                   MusicStoryteller SDK                       │
│         (Narrative Templates, GOAP, Intent Generation)       │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                     MusicTheory SDK                          │
│              (Pitch, Harmony, Melody, Rhythm)                │
└─────────────────────────────────────────────────────────────┘
```

---

## Composer Personality Model

A Composer represents a persistent musical personality with learned preferences and tendencies.

### Core Personality Dimensions

Based on music psychology research (adapted from Big Five for musical contexts):

| Dimension | Low End | High End | Musical Effect |
|-----------|---------|----------|----------------|
| **Adventurousness** | Conservative | Experimental | Harmonic vocabulary, dissonance tolerance |
| **Emotional Range** | Restrained | Dramatic | Dynamic contrast, tension peaks |
| **Structural Rigor** | Loose/Improvisatory | Strict/Formal | Form adherence, phrase regularity |
| **Rhythmic Energy** | Spacious | Dense | Note density, syncopation |
| **Harmonic Complexity** | Simple | Complex | Chord extensions, secondary dominants |

### Preference Categories

```yaml
composer:
  id: "aria"
  name: "Aria"
  personality:
    adventurousness: 0.3
    emotional_range: 0.7
    structural_rigor: 0.8
    rhythmic_energy: 0.5
    harmonic_complexity: 0.4

  # Style affinities (0-1)
  style_weights:
    celtic: 0.6
    baroque: 0.3
    jazz: 0.1

  # Melodic preferences
  melodic:
    preferred_contours: [arch, descending]
    leap_tolerance: 0.3  # Probability of leaps vs steps
    range_preference: middle  # low/middle/high
    ornament_density: 0.4

  # Harmonic preferences
  harmonic:
    cadence_preferences:
      authentic: 0.6
      half: 0.2
      deceptive: 0.15
      plagal: 0.05
    secondary_dominant_affinity: 0.2
    modal_interchange_affinity: 0.1

  # Rhythmic preferences
  rhythmic:
    preferred_meters: [4/4, 3/4]
    syncopation_affinity: 0.3
    tempo_range: [80, 120]

  # Emotional tendencies
  emotional:
    default_valence: 0.6  # Tends toward positive
    tension_ceiling: 0.8  # Avoids extreme tension
    resolution_urgency: 0.7  # How quickly tension resolves
```

---

## Style Blending Engine

Composers can blend multiple style definitions to create unique hybrid styles.

### Blending Strategies

1. **Weighted Average**: Simple interpolation of style parameters
2. **Alternation**: Switch between styles by phrase or section
3. **Layered**: Different styles for melody vs harmony
4. **Evolution**: Transition between styles over composition duration

```csharp
// Example: Create a Celtic-Jazz blend
var blendedStyle = StyleBlender.Blend(
    new[] { BuiltInStyles.Celtic, BuiltInStyles.Jazz },
    weights: [0.7, 0.3],
    strategy: BlendStrategy.WeightedAverage
);

// Result: Celtic melodic intervals + Jazz harmony complexity
```

### Per-Component Blending

Allow different blend ratios for different musical components:

```yaml
style_blend:
  melody:
    celtic: 0.8
    jazz: 0.2
  harmony:
    celtic: 0.4
    jazz: 0.6
  rhythm:
    celtic: 0.9
    jazz: 0.1
```

---

## Preference Evolution

Composers can evolve their preferences over time based on:
- Feedback signals (listener engagement)
- Contextual adaptation (game events)
- Random drift (creative exploration)

### Evolution Model

```csharp
public interface IComposerEvolution
{
    // Update preferences based on feedback
    void ApplyFeedback(ComposerFeedback feedback);

    // Apply contextual pressure (game event influence)
    void ApplyContext(MusicalContext context);

    // Random exploration within bounds
    void Drift(double magnitude);

    // Reset to baseline personality
    void Reset();
}

public class ComposerFeedback
{
    // Player engagement metrics
    public double AttentionLevel { get; set; }
    public double EmotionalResonance { get; set; }

    // Explicit signals
    public bool LikedComposition { get; set; }
    public bool SkippedEarly { get; set; }
}
```

### Bounded Evolution

Preferences evolve within bounds to maintain personality consistency:

```yaml
evolution_bounds:
  adventurousness:
    min: 0.1
    max: 0.5
    drift_rate: 0.01
  harmonic_complexity:
    min: 0.2
    max: 0.6
    drift_rate: 0.02
```

---

## Integration with Existing Systems

### ABML Behavior Integration

Composers could be ABML behaviors assigned to NPCs:

```yaml
# NPC character definition
character:
  name: "Elara the Bard"
  behaviors:
    music:
      composer_id: "aria"
      triggers:
        - condition: "in_tavern"
          style_override: { energy: 0.7, tempo: 120 }
        - condition: "combat_nearby"
          style_override: { tension: 0.8, tempo: 140 }
```

### Game State Reactivity

Composers respond to game state through the existing EmotionalState system:

```csharp
// Game event affects composer's emotional baseline
composer.ApplyContext(new MusicalContext
{
    GameState = GameState.Battle,
    EmotionalPressure = new EmotionalState
    {
        Tension = 0.7,
        Energy = 0.8
    }
});

// Composition reflects both personality AND context
var result = composer.Compose(request);
```

---

## Proposed API

### Composer Definition

```csharp
public interface IComposer
{
    string Id { get; }
    string Name { get; }
    ComposerPersonality Personality { get; }
    StyleBlend StyleBlend { get; }

    // Core composition (delegates to Storyteller with personality constraints)
    CompositionResult Compose(CompositionRequest request);

    // Personality-aware intent generation
    CompositionIntent GenerateIntent(CompositionState state, NarrativePhase phase);

    // Evolution
    void ApplyFeedback(ComposerFeedback feedback);
    void ApplyContext(MusicalContext context);
}

public class ComposerBuilder
{
    public ComposerBuilder WithPersonality(ComposerPersonality personality);
    public ComposerBuilder WithStyleBlend(StyleBlend blend);
    public ComposerBuilder WithEvolutionBounds(EvolutionBounds bounds);
    public IComposer Build();
}
```

### Service API Extension

```yaml
# New endpoints for Music Service
paths:
  /music/composer/create:
    post:
      summary: Create a new composer personality
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ComposerDefinition'

  /music/composer/generate:
    post:
      summary: Generate composition using specific composer
      requestBody:
        content:
          application/json:
            schema:
              type: object
              properties:
                composerId:
                  type: string
                request:
                  $ref: '#/components/schemas/CompositionRequest'

  /music/composer/evolve:
    post:
      summary: Apply feedback/context to evolve composer
```

---

## Implementation Phases

### Phase 1: Personality Definition
- Define ComposerPersonality schema
- Create personality → constraint mapping
- Integrate with Storyteller's intent generation

### Phase 2: Style Blending
- Implement StyleBlender with weighted average strategy
- Add per-component blend ratios
- Create hybrid style validation

### Phase 3: Evolution System
- Implement feedback processing
- Add bounded drift mechanics
- Create persistence for evolved states

### Phase 4: Game Integration
- ABML behavior type for composer assignment
- Game state → musical context mapping
- Real-time adaptation hooks

---

## Open Questions

1. **Persistence**: How are evolved composer states persisted? Redis? MySQL?
2. **Uniqueness**: Should each NPC have a unique composer, or share composer "templates"?
3. **Performance**: How expensive is personality → constraint translation per composition?
4. **Validation**: How do we validate that blended styles produce coherent music?
5. **Feedback Signals**: What game events provide meaningful musical feedback?

---

## References

- Rentfrow, P. J., & Gosling, S. D. (2003). The Do Re Mi's of Everyday Life: The Structure and Personality Correlates of Music Preferences. *Journal of Personality and Social Psychology*.
- Greenberg, D. M., et al. (2016). Musical Preferences are Linked to Cognitive Styles. *PLOS ONE*.
- Juslin, P. N. (2019). *Musical Emotions Explained*. Oxford University Press. (Chapter on individual differences)
