# Storyline SDK Theoretical Foundations

> **Status**: Research Complete - Foundation for Implementation
> **Purpose**: Theoretical frameworks and design decisions NOT captured in YAML schemas
> **Related**: `GAP_ANALYSIS.md`, `narrative-state.yaml`, `story-grid-genres.yaml`, `emotional-arcs.yaml`, `save-the-cat-beats.yaml`, `propp-functions.yaml`
> **Target SDKs**: `sdks/storyline-theory/`, `sdks/storyline-storyteller/`

This document captures theoretical foundations, design decisions, and implementation patterns that supplement the authoritative YAML schemas. The YAML files define **what** the narrative frameworks contain; this document explains **how** to use them together.

---

## Table of Contents

1. [Narrative Theory Not in YAML](#narrative-theory-not-in-yaml)
   - Greimas' Actantial Model (6 Actants)
   - Plot Units (Wendy Lehnert, 1981)
   - Fabula vs. Sjuzhet (Russian Formalism)
   - Kernels vs. Satellites (Barthes/Chatman)
2. [Design Decisions](#design-decisions)
   - Multi-Phase Action Effects
   - Template Phase Definitions
   - Intent Execution Model
3. [Story Action Library](#story-action-library)
   - ConflictActions
   - RelationshipActions
   - MysteryActions
   - ResolutionActions
4. [Archive Extraction Patterns](#archive-extraction-patterns)
   - WorldState Mapping
   - Kernel Extraction Algorithm
5. [Computational Precedents](#computational-precedents)
   - Facade/ABL Drama Management
   - Versu/Re-Praxis Exclusion Logic
6. [SDK Architecture](#sdk-architecture)
   - Layer Mapping (Music → Storyline)
   - Implementation Insights from Reference Repositories

---

## Narrative Theory Not in YAML

The following narrative frameworks complement those already captured in YAML schemas.

### Greimas' Actantial Model (6 Actants)

**Source**: A.J. Greimas, *Structural Semantics* (1966)

Abstract character roles independent of specific characters. Unlike Propp's 7 dramatis personae (which are character TYPES), Greimas' actants are FUNCTIONAL POSITIONS in a narrative grammar.

| Axis | Actant 1 | Actant 2 | Description |
|------|----------|----------|-------------|
| **Desire** | Subject | Object | Who wants what |
| **Communication** | Sender | Receiver | Who initiates the quest, who benefits |
| **Power** | Helper | Opponent | Who assists, who opposes |

**Key Insight**: Multiple characters can fill one actant role; one character can fill multiple actant roles. This enables flexible character configuration:

```
Example: Star Wars (Episode IV)
  Subject:   Luke Skywalker
  Object:    Destroy Death Star / Save Leia
  Sender:    Obi-Wan Kenobi (initiates quest)
  Receiver:  Rebellion (benefits from success)
  Helper:    Han Solo, R2-D2, C-3PO
  Opponent:  Darth Vader, Stormtroopers

Example: Multiple actants on one character
  Obi-Wan is both Sender AND Helper
  Luke is both Subject AND Receiver (benefits from his own quest)
```

**SDK Use Case**: Character role abstraction for template instantiation. When composing a storyline, assign actant roles to available characters before determining specific narrative functions.

**Relationship to Propp**: Propp's dramatis personae (Villain, Dispatcher, Helper, Donor, Hero, Princess, False Hero) can be mapped to Greimas' actants, but Greimas is more abstract and allows role-sharing.

---

### Plot Units (Wendy Lehnert, 1981)

**Source**: Wendy Lehnert, "Plot Units and Narrative Summarization" (1981)

Affect-based configurations representing emotional patterns in narrative:

```
Positive (+)  ─────┐
                   ├───► Resolution
Negative (-)  ─────┘
     │
Mental (M) ────────► Goal/Intention
```

**Core Primitives**:
- **Positive Event (+)**: Good thing happens to character
- **Negative Event (-)**: Bad thing happens to character
- **Mental State (M)**: Character forms goal or intention

**Key Insight**: Plot units **overlap** when narratives are cohesive. Graph topology reveals which events are central vs. peripheral. Connected components of plot units identify narrative threads.

**Primitive Plot Unit Configurations**:
| Name | Pattern | Meaning |
|------|---------|---------|
| Success | M → + | Goal achieved |
| Failure | M → - | Goal thwarted |
| Motivation | - → M | Negative event creates goal |
| Change of Mind | M → M | Goal shifts |
| Perseverance | - → M | Despite setback, continue |

**SDK Use Case**:
1. **Narrative summarization**: Identify essential events vs. elaboration
2. **Kernel identification**: Events with many plot unit connections = kernels
3. **Adaptation decisions**: When compressing archives, preserve plot unit hubs

---

### Fabula vs. Sjuzhet (Russian Formalism)

**Source**: Russian Formalists (Shklovsky, Tomashevsky); elaborated by Chatman as Story vs. Discourse

| Concept | Definition | Example |
|---------|------------|---------|
| **Fabula** | Chronological sequence of events (what happened) | Murder → Investigation → Arrest |
| **Sjuzhet** | Artistic arrangement (how it's told) | Body found → Flashbacks → Reveal |

**Chatman's Extension** (Story and Discourse, 1978):
- **Story**: Content (events, existents/characters, settings)
- **Discourse**: Expression (narration, temporal ordering, focalization)

**SDK Use Case**: The storyline composer operates at the FABULA level - generating chronological event sequences. The presentation layer (game client, actor behaviors) handles SJUZHET - deciding when and how to reveal information to players.

```csharp
// SDK operates at fabula level
var fabula = new Fabula
{
    Events = [
        new Event("Murder", t: 0),
        new Event("Investigation", t: 1),
        new Event("Arrest", t: 2)
    ]
};

// Presentation layer transforms to sjuzhet
var sjuzhet = new Sjuzhet
{
    Presentation = [
        new Reveal("Body found", shows: "Murder aftermath"),
        new Reveal("Flashback", shows: "Investigation clue"),
        new Reveal("Confrontation", shows: "Arrest")
    ]
};
```

---

### Kernels vs. Satellites (Barthes/Chatman)

**Source**: Roland Barthes, "Introduction to the Structural Analysis of Narratives" (1966); Seymour Chatman, *Story and Discourse* (1978)

| Type | Definition | Deletability | Function |
|------|------------|--------------|----------|
| **Kernels** (Cardinal Functions) | Branching points where choices occur | Cannot delete without altering story logic | Drive narrative forward |
| **Satellites** (Catalyzers) | Elaboration and texture | Can be freely deleted | Embellish kernels |

**Practical Application**: When compressing or adapting stories, kernels MUST be preserved; satellites MAY be changed or removed.

**SDK Use Case**:
1. **Archive compression**: Kernels go to permanent storage; satellites can be summarized
2. **Story potential scoring**: Events with kernel characteristics (choices, consequences) score higher
3. **Cross-archive analysis**: Shared kernels indicate narrative connections

**Kernel Identification Heuristics**:
```yaml
kernel_indicators:
  - Satisfies a Propp function (especially VILLAINY, VICTORY, RECOGNITION)
  - Matches a Story Grid obligatory scene
  - Has high plot unit connectivity (Lehnert)
  - Involves irreversible state change
  - Represents character choice with consequences

satellite_indicators:
  - Descriptive or atmospheric
  - Could be moved or removed without breaking causality
  - Elaborates on a kernel without adding new branching
```

---

## Design Decisions

The following architectural decisions guide SDK implementation.

### Multi-Phase Action Effects

**Decision**: Use atomic action sequences; GOAP handles sequencing naturally.

**Problem**: Some story beats have multi-phase effects (e.g., tension spikes during confrontation, then resolves). How do we model this?

**Rejected Approach**: Complex effect objects with "then" clauses
```csharp
// INVALID - complex deferred effects
NarrativeStateEffect = new() { Tension = +0.3, then = -0.4 }
```

**Accepted Approach**: Chained atomic actions
```csharp
// VALID - atomic actions, GOAP plans the sequence
ConfrontationBegin  → { Tension = +0.3 }  → chains to → ConfrontationResolve → { Tension = -0.4 }
```

**Rationale**:
1. Aligns with how GOAP already works - the planner sees action sequences, not delayed effects
2. Each action can have its own preconditions (ConfrontationResolve requires ConfrontationBegin)
3. World state changes between actions can affect planning
4. Simpler to test and debug

**Implementation Pattern**:
```csharp
public static IStoryAction ConfrontationBegin => new StoryAction
{
    Id = "confrontation_begin",
    Preconditions = { ("antagonist.known", true), ("protagonist.ready", true) },
    Effects = { ("confrontation.in_progress", true) },
    NarrativeStateEffect = new() { Tension = +0.3, Urgency = +0.2 },
    ChainedAction = "confrontation_resolve"  // GOAP will plan this next
};

public static IStoryAction ConfrontationResolve => new StoryAction
{
    Id = "confrontation_resolve",
    Preconditions = { ("confrontation.in_progress", true) },
    Effects = { ("confrontation.in_progress", false), ("confrontation.occurred", true) },
    NarrativeStateEffect = new() { Tension = -0.4, Urgency = -0.3 }
};
```

---

### Template Phase Definitions

**Decision**: Hybrid approach - milestone-based phases with timing constraints.

**Problem**: How do we define story phases? Pure milestones give unpredictable pacing; pure timing ignores story content.

**Rejected Approaches**:
1. **Pure milestones**: Phase ends when condition X is met (e.g., "villain revealed"). Problem: Could happen at 10% or 90% of story.
2. **Pure timing**: Phase ends at exactly 25% mark. Problem: Ignores whether story content supports transition.

**Accepted Approach**: Hybrid constraints - phases defined by state-based entry/exit conditions WITH min/max duration limits.

```csharp
new StoryPhase("Pursuit")
{
    // Timing constraints
    MinDuration = 0.20,     // At least 20% of story
    MaxDuration = 0.45,     // At most 45%
    TargetPosition = 0.35,  // Ideally center at 35%

    // State-based exit
    ExitCondition = state => state.Get("confrontation.proximity") > 0.8,

    // What happens if timing expires before condition?
    TimeoutBehavior = TimeoutBehavior.ForceTransition
}

public enum TimeoutBehavior
{
    ForceTransition,    // Move to next phase regardless
    ExtendWithWarning,  // Continue but flag pacing issue
    InsertBridgeContent // Generate content to satisfy exit condition
}
```

**Rationale**: Ensures pacing while allowing content-driven transitions. Most stories will naturally hit exit conditions within timing windows; edge cases are handled gracefully.

---

### Intent Execution Model

**Decision**: Follow Actor/Behavior data access patterns per `ACTOR_DATA_ACCESS_PATTERNS.md`.

The storyline composer outputs **intents** (what should happen) rather than directly modifying game state. Intents are executed by the appropriate services.

| Intent Type | Execution Pattern | Example |
|-------------|-------------------|---------|
| **Read operations** (get character traits, history) | Variable Providers (cached, 5-min TTL) | `await _characterProvider.GetTraitsAsync(id)` |
| **Write operations** (create character, modify inventory) | API calls via lib-mesh | `await _characterClient.UpdateAsync(request)` |
| **Real-time state** | Event subscriptions | Subscribe to `character.updated` |

**Intent-to-Service Mapping**:
```csharp
// CharacterIntent execution
public async Task ExecuteAsync(CharacterIntent intent, CancellationToken ct)
{
    var request = intent.ToCharacterRequest();
    await _characterClient.UpdateAsync(request);  // lib-mesh handles routing
}

// QuestIntent execution (uses lib-contract for milestone-based progression)
public async Task ExecuteAsync(QuestIntent intent, CancellationToken ct)
{
    var request = intent.ToContractRequest();
    await _contractClient.CreateFromTemplateAsync(request);
}

// BehaviorIntent execution (generates ABML fragment)
public async Task ExecuteAsync(BehaviorIntent intent, CancellationToken ct)
{
    var abml = _abmlCompiler.CompileFromIntent(intent);
    await _actorClient.UpdateBehaviorAsync(intent.ActorId, abml);
}
```

**Rationale**: Consistent with existing Bannou patterns. No special execution model needed - storyline intents map directly to existing service APIs.

---

## Story Action Library

Story actions parallel music-storyteller's action categories. Each action has preconditions, effects, cost, and NarrativeState delta.

### ConflictActions

Actions that introduce and escalate opposition.

```csharp
public static class ConflictActions
{
    public static IStoryAction IntroduceAntagonist => new StoryAction
    {
        Id = "introduce_antagonist",
        Preconditions = { ("antagonist.known", false) },
        Effects = {
            ("antagonist.known", true),
            ("tension", +0.2)
        },
        Cost = 1.0,
        NarrativeStateEffect = new() {
            Tension = +0.15,
            Stakes = +0.1,
            Mystery = +0.2
        }
    };

    public static IStoryAction EscalateConflict => new StoryAction
    {
        Id = "escalate_conflict",
        Preconditions = { ("conflict.active", true) },
        Effects = {
            ("tension", +0.15),
            ("stakes", +0.1)
        },
        Cost = 1.0,
        NarrativeStateEffect = new() {
            Tension = +0.15,
            Stakes = +0.1,
            Urgency = +0.1
        }
    };

    public static IStoryAction BetrayalReveal => new StoryAction
    {
        Id = "betrayal_reveal",
        Preconditions = {
            ("trusted_character.exists", true),
            ("betrayal.hidden", true)
        },
        Effects = {
            ("betrayal.hidden", false),
            ("tension", +0.3),
            ("intimacy", -0.2)
        },
        Cost = 2.0,  // High impact, use sparingly
        NarrativeStateEffect = new() {
            Tension = +0.25,
            Stakes = +0.15,
            Hope = -0.2,
            Intimacy = -0.15
        }
    };
}
```

### RelationshipActions

Actions that build and modify character bonds.

```csharp
public static class RelationshipActions
{
    public static IStoryAction FormAlliance => new StoryAction
    {
        Id = "form_alliance",
        Preconditions = { ("characters.compatible", true) },
        Effects = {
            ("alliance.formed", true),
            ("intimacy", +0.15)
        },
        Cost = 1.0,
        NarrativeStateEffect = new() {
            Intimacy = +0.15,
            Hope = +0.1
        }
    };

    public static IStoryAction SharedOrdeal => new StoryAction
    {
        Id = "shared_ordeal",
        Preconditions = {
            ("danger.present", true),
            ("characters.together", true)
        },
        Effects = {
            ("intimacy", +0.2),
            ("trust", +0.15)
        },
        Cost = 1.5,
        NarrativeStateEffect = new() {
            Intimacy = +0.2,
            Tension = +0.1,
            Hope = +0.05
        }
    };

    public static IStoryAction RevealConnection => new StoryAction
    {
        Id = "reveal_connection",
        Preconditions = {
            ("hidden_connection.exists", true)
        },
        Effects = {
            ("hidden_connection.revealed", true),
            ("mystery", -0.15)
        },
        Cost = 1.2,
        NarrativeStateEffect = new() {
            Mystery = -0.15,
            Intimacy = +0.1
        }
    };

    public static IStoryAction DestroyTrust => new StoryAction
    {
        Id = "destroy_trust",
        Preconditions = {
            ("trust.exists", true),
            ("justification.exists", true)
        },
        Effects = {
            ("trust.exists", false),
            ("intimacy", -0.3)
        },
        Cost = 2.5,  // Major irreversible action
        NarrativeStateEffect = new() {
            Intimacy = -0.25,
            Hope = -0.1,
            Tension = +0.15
        }
    };
}
```

### MysteryActions

Actions for information revelation and concealment.

```csharp
public static class MysteryActions
{
    public static IStoryAction PlantClue => new StoryAction
    {
        Id = "plant_clue",
        Preconditions = { ("mystery.active", true) },
        Effects = {
            ("clue.available", true),
            ("mystery", -0.1)
        },
        Cost = 0.5,
        NarrativeStateEffect = new() {
            Mystery = -0.1,
            Hope = +0.05
        }
    };

    public static IStoryAction RevealSecret => new StoryAction
    {
        Id = "reveal_secret",
        Preconditions = {
            ("secret.hidden", true),
            ("revelation.justified", true)
        },
        Effects = {
            ("secret.hidden", false),
            ("mystery", -0.2),
            ("tension", +0.1)
        },
        Cost = 1.5,
        NarrativeStateEffect = new() {
            Mystery = -0.2,
            Tension = +0.1,
            Stakes = +0.05
        }
    };

    public static IStoryAction RedHerring => new StoryAction
    {
        Id = "red_herring",
        Preconditions = { ("mystery.active", true) },
        Effects = { ("false_lead.planted", true) },
        Cost = 0.8,
        NarrativeStateEffect = new() {
            Mystery = +0.1,
            Urgency = +0.05
        }
    };

    public static IStoryAction ConnectDots => new StoryAction
    {
        Id = "connect_dots",
        Preconditions = {
            ("clues.count", ">=", 3)
        },
        Effects = {
            ("pattern.recognized", true),
            ("mystery", -0.3)
        },
        Cost = 1.0,
        NarrativeStateEffect = new() {
            Mystery = -0.3,
            Hope = +0.15
        }
    };
}
```

### ResolutionActions

Actions that move toward story conclusion. Note: Multi-phase effects use chained atomic actions per design decision above.

```csharp
public static class ResolutionActions
{
    // Confrontation is a two-action sequence
    public static IStoryAction ConfrontationBegin => new StoryAction
    {
        Id = "confrontation_begin",
        Preconditions = {
            ("antagonist.known", true),
            ("protagonist.ready", true)
        },
        Effects = { ("confrontation.in_progress", true) },
        Cost = 1.5,
        NarrativeStateEffect = new() {
            Tension = +0.3,
            Urgency = +0.2
        },
        ChainedAction = "confrontation_resolve"
    };

    public static IStoryAction ConfrontationResolve => new StoryAction
    {
        Id = "confrontation_resolve",
        Preconditions = { ("confrontation.in_progress", true) },
        Effects = {
            ("confrontation.in_progress", false),
            ("confrontation.occurred", true)
        },
        Cost = 0.5,
        NarrativeStateEffect = new() {
            Tension = -0.4,
            Urgency = -0.3
        }
    };

    public static IStoryAction Sacrifice => new StoryAction
    {
        Id = "sacrifice",
        Preconditions = {
            ("character.willing", true),
            ("sacrifice.meaningful", true)
        },
        Effects = {
            ("sacrifice.made", true),
            ("stakes.resolved", true)
        },
        Cost = 3.0,  // Major story beat
        NarrativeStateEffect = new() {
            Tension = -0.2,
            Intimacy = +0.3,
            Hope = +0.2
        }
    };

    public static IStoryAction Justice => new StoryAction
    {
        Id = "justice",
        Preconditions = {
            ("crime.committed", true),
            ("perpetrator.identified", true)
        },
        Effects = {
            ("justice.served", true)
        },
        Cost = 1.5,
        NarrativeStateEffect = new() {
            Tension = -0.3,
            Hope = +0.25
        }
    };

    public static IStoryAction Restoration => new StoryAction
    {
        Id = "restoration",
        Preconditions = {
            ("damage.occurred", true),
            ("healing.possible", true)
        },
        Effects = {
            ("restoration.complete", true)
        },
        Cost = 1.2,
        NarrativeStateEffect = new() {
            Hope = +0.3,
            Intimacy = +0.1
        }
    };
}
```

### TransformationActions

Actions representing character growth and change.

```csharp
public static class TransformationActions
{
    public static IStoryAction CharacterGrowth => new StoryAction
    {
        Id = "character_growth",
        Preconditions = {
            ("challenge.faced", true),
            ("lesson.available", true)
        },
        Effects = {
            ("character.transformed", true)
        },
        Cost = 2.0,
        NarrativeStateEffect = new() {
            Hope = +0.2
        }
    };

    public static IStoryAction Revelation => new StoryAction
    {
        Id = "revelation",
        Preconditions = {
            ("truth.hidden", true),
            ("moment.right", true)
        },
        Effects = {
            ("truth.revealed", true),
            ("worldview.shifted", true)
        },
        Cost = 2.5,
        NarrativeStateEffect = new() {
            Mystery = -0.4,
            Hope = +0.1
        }
    };
}
```

---

## Archive Extraction Patterns

Converting compressed archives to GOAP-compatible WorldState.

### WorldState Mapping

```csharp
public class ArchiveExtractor
{
    /// <summary>
    /// Convert compressed character archive to GOAP-compatible WorldState.
    /// Uses the archive's compressed data entries keyed by contributing service.
    /// </summary>
    public WorldState ExtractWorldState(ResourceArchive archive)
    {
        var worldState = new WorldState();

        // Extract from character-base (always present)
        var baseData = archive.GetEntry<CharacterCompressData>("character");
        worldState.Set("protagonist.name", baseData.Name);
        worldState.Set("protagonist.species", baseData.Species);
        worldState.Set("protagonist.death_cause", baseData.DeathCause);
        worldState.Set("protagonist.alive", false);  // Archived = dead

        // Extract from character-personality (if present)
        var personality = archive.GetEntry<PersonalityCompressData>("character-personality");
        if (personality?.HasPersonality == true)
        {
            // Bipolar trait axes (-1 to +1)
            worldState.Set("protagonist.confrontational", personality.Personality.Confrontational);
            worldState.Set("protagonist.loyal", personality.Personality.Loyal);
            worldState.Set("protagonist.vengeful", personality.Personality.Vengeful);
            worldState.Set("protagonist.trusting", personality.Personality.Trusting);
            worldState.Set("protagonist.risk_taking", personality.Personality.RiskTaking);
            // ... additional trait axes
        }

        // Extract from character-history (if present)
        var history = archive.GetEntry<HistoryCompressData>("character-history");
        if (history != null)
        {
            // Historical participations
            foreach (var participation in history.Participations)
            {
                worldState.Set($"history.{participation.EventCode}.participated", true);
                worldState.Set($"history.{participation.EventCode}.role", participation.Role);
                worldState.Set($"history.{participation.EventCode}.significance", participation.Significance);
            }

            // Backstory elements (if present)
            if (history.HasBackstory)
            {
                worldState.Set("backstory.origin", history.Backstory.Origin);
                worldState.Set("backstory.occupation", history.Backstory.Occupation);
                worldState.Set("backstory.trauma", history.Backstory.Trauma);
                worldState.Set("backstory.fears", history.Backstory.Fears);
                worldState.Set("backstory.goals", history.Backstory.Goals);
            }
        }

        // Extract from character-encounter (if present)
        var encounters = archive.GetEntry<EncounterCompressData>("character-encounter");
        if (encounters != null)
        {
            worldState.Set("encounter.count", encounters.EncounterCount);
            worldState.Set("encounter.positive_ratio", CalculatePositiveRatio(encounters));

            // Aggregate sentiment per relationship
            foreach (var group in encounters.Perspectives.GroupBy(p => p.OtherCharacterId))
            {
                var avgSentiment = group.Average(p => p.SentimentShift);
                worldState.Set($"relationship.{group.Key}.sentiment", avgSentiment);
                worldState.Set($"relationship.{group.Key}.encounter_count", group.Count());
            }
        }

        return worldState;
    }

    private double CalculatePositiveRatio(EncounterCompressData encounters)
    {
        if (encounters.EncounterCount == 0) return 0.5;
        var positive = encounters.Perspectives.Count(p => p.SentimentShift > 0);
        return (double)positive / encounters.EncounterCount;
    }
}
```

### Kernel Extraction Algorithm

Identifying essential events (kernels) from archive data for story seeding.

```csharp
public class KernelExtractor
{
    /// <summary>
    /// Identify kernels (essential events) from archive for story seeding.
    /// Kernels are events that:
    /// - Represent branching points/choices
    /// - Have high significance scores
    /// - Cannot be removed without breaking narrative causality
    /// </summary>
    public List<Kernel> ExtractKernels(ResourceArchive archive)
    {
        var kernels = new List<Kernel>();

        // Death is ALWAYS a kernel for archived characters
        var baseData = archive.GetEntry<CharacterCompressData>("character");
        kernels.Add(new Kernel
        {
            Type = KernelType.Death,
            Significance = 1.0,  // Maximum significance
            Data = new {
                Cause = baseData.DeathCause,
                Location = baseData.DeathLocation
            }
        });

        // High-significance historical participations are kernels
        var history = archive.GetEntry<HistoryCompressData>("character-history");
        if (history != null)
        {
            foreach (var p in history.Participations.Where(p => p.Significance > 0.7))
            {
                kernels.Add(new Kernel
                {
                    Type = KernelType.HistoricalEvent,
                    Significance = p.Significance,
                    Data = new {
                        Event = p.EventCode,
                        Role = p.Role
                    }
                });
            }

            // Traumatic backstory elements are kernels
            if (history.HasBackstory && !string.IsNullOrEmpty(history.Backstory.Trauma))
            {
                kernels.Add(new Kernel
                {
                    Type = KernelType.Trauma,
                    Significance = 0.8,
                    Data = new {
                        Trauma = history.Backstory.Trauma
                    }
                });
            }

            // Unfinished goals create narrative hooks
            if (history.HasBackstory && history.Backstory.Goals?.Any() == true)
            {
                kernels.Add(new Kernel
                {
                    Type = KernelType.UnfinishedBusiness,
                    Significance = 0.75,
                    Data = new {
                        Goals = history.Backstory.Goals
                    }
                });
            }
        }

        // High-impact negative encounters are kernels (potential grudge storylines)
        var encounters = archive.GetEntry<EncounterCompressData>("character-encounter");
        if (encounters != null)
        {
            var conflictEncounters = encounters.Encounters
                .Where(e => e.Perspectives.Any(p =>
                    p.SentimentShift < -0.5 && p.EmotionalImpact > 0.7));

            foreach (var e in conflictEncounters)
            {
                kernels.Add(new Kernel
                {
                    Type = KernelType.Conflict,
                    Significance = 0.85,
                    Data = new {
                        EncounterId = e.EncounterId,
                        OtherCharacters = e.Perspectives.Select(p => p.OtherCharacterId)
                    }
                });
            }

            // Deep positive relationships are kernels (legacy, protection)
            var bondedRelationships = encounters.Perspectives
                .GroupBy(p => p.OtherCharacterId)
                .Where(g => g.Average(p => p.SentimentShift) > 0.6 && g.Count() > 5);

            foreach (var group in bondedRelationships)
            {
                kernels.Add(new Kernel
                {
                    Type = KernelType.DeepBond,
                    Significance = 0.7,
                    Data = new {
                        BondedCharacterId = group.Key,
                        AverageSentiment = group.Average(p => p.SentimentShift)
                    }
                });
            }
        }

        return kernels.OrderByDescending(k => k.Significance).ToList();
    }
}

public class Kernel
{
    public KernelType Type { get; set; }
    public double Significance { get; set; }  // 0-1 scale
    public object Data { get; set; }
}

public enum KernelType
{
    Death,
    HistoricalEvent,
    Trauma,
    UnfinishedBusiness,
    Conflict,
    DeepBond
}
```

---

## Computational Precedents

### Facade/ABL Drama Management

**Source**: Michael Mateas & Andrew Stern, "Structuring Content in the Façade Interactive Drama Architecture" (2005)

Facade pioneered **beat-based drama management** for interactive narrative.

**Architecture**:
- **Behaviors**: Small programs performing dramatic action
- **Beats**: Collections of 10-100 behaviors for specific situations
- **Drama Manager**: Sequences beats based on story state (27 beats total)

**Key Innovation**: Beat-based drama management with Aristotelian dramatic arc. The drama manager maintains global story state and selects beats to achieve target emotional trajectory.

**Relevance to Storyline SDKs**:
1. The drama manager pattern aligns with Regional Watchers calling the storyline composer
2. ABL's sequential/parallel behavior trees influenced ABML design
3. Beat selection based on story state = GOAP action selection based on NarrativeState

**Pattern Application**:
```
Facade Pattern:          Bannou Equivalent:
─────────────────        ─────────────────────────
Behaviors         →      ABML behavior fragments
Beats             →      Story phases/scenes
Drama Manager     →      Regional Watcher + Storyline Composer
Story State       →      NarrativeState (10 Life Value spectrums)
```

### Versu/Re-Praxis Exclusion Logic

**Source**: Richard Evans & Emily Short, "Versu—A Simulationist Storytelling System" (2013)

**Reference Repository**: `~/repos/re-praxis` (C# implementation)

Re-Praxis demonstrates cardinality-enforced state management using exclusion logic.

**Core Pattern**: Use cardinality operators for WorldState constraints
- `.` (dot) = MANY (additive)
- `!` (exclamation) = ONE (exclusive, replaces previous value)

```csharp
var db = new PraxisDatabase();

// Character can only be in ONE location (exclusive)
db.Insert("character.bob.location!kitchen");
db.Insert("character.bob.location!bedroom");  // Replaces kitchen

// Character can have MANY friends (additive)
db.Insert("character.bob.friends.alice");
db.Insert("character.bob.friends.carol");  // Adds to friends

// Query with pattern matching
var query = db.Query("character.bob.location!?where");
// Returns: [{ where: "bedroom" }]

var alliesQuery = db.Query("character.bob.friends.?ally");
// Returns: [{ ally: "alice" }, { ally: "carol" }]
```

**Relevance to Storyline SDKs**:
1. Cardinality enforcement ensures GOAP preconditions work correctly
2. A character can only have ONE current goal, but MANY relationships
3. Query syntax with variables (`?var`) enables pattern matching on story state

**Pattern Application for NarrativeWorldState**:
```csharp
public class NarrativeWorldState : PraxisDatabase
{
    // Exclusive: Character has one current goal
    public void SetGoal(string character, string goal) =>
        Insert($"character.{character}.goal!{goal}");

    // Additive: Character accumulates relationships
    public void AddRelationship(string character, string other, string type) =>
        Insert($"character.{character}.relationships.{other}.{type}");

    // Query: Find all allies
    public IEnumerable<string> GetAlliesOf(string character) =>
        Query($"character.{character}.relationships.?ally.ally")
            .Select(r => r["ally"]);
}
```

---

## SDK Architecture

### Layer Mapping (Music → Storyline)

The music SDK provides a proven architecture pattern. Below is the conceptual mapping for storyline equivalents.

| Music Concept | Storyline Equivalent | Academic Basis |
|---------------|---------------------|----------------|
| `PitchClass`, `Interval` | `NarrativeFunction`, `StoryAtom` | Propp's 31 functions |
| `Chord`, `Scale` | `Scene`, `Sequence` | McKee's hierarchy |
| `HarmonicFunction` | `ActantRole` | Greimas' 6 actants |
| `Cadence` | `ResolutionPattern` | Genre-specific endings |
| `Voice` | `CharacterThread` | Dramatica throughlines |
| `EmotionalState` (6D) | `NarrativeState` (10D) | Story Grid Life Value spectrums |
| `ListenerModel` (ITPRA) | `AudienceModel` | Genre expectations |
| `MechanismState` (BRECVEMA) | `EngagementState` | Active narrative hooks |
| `NarrativeTemplate` | `StoryArcTemplate` | Hero's Journey, Save the Cat |
| `NarrativePhase` | `StoryPhase` | Act structure |
| `CompositionIntent` | `StorylineIntent` | Concrete actions to execute |
| `GOAPPlanner` | Same (reuse from lib-behavior) | A* for action sequencing |

### Implementation Insights from Reference Repositories

#### 1. NCP Storyform Structure (from `~/repos/narrative-context-protocol`)

The Narrative Context Protocol provides JSON schemas for storyforms that inform cross-framework mapping.

**Perspective Mapping to Throughlines**:
```typescript
const perspectives = {
  "i": "main_character",      // Internal journey
  "you": "obstacle_character", // Challenge to MC worldview
  "we": "objective_story",    // External plot
  "they": "subjective_story"  // MC-OC relationship
};
```

**Story Dynamics with Vectors**:
```typescript
const dynamics = {
  "driver": { value: "action" | "decision", vector: 1 | -1 },
  "limit": { value: "timelock" | "optionlock", vector: 1 | -1 },
  "outcome": { value: "success" | "failure", vector: 1 | -1 },
  "judgment": { value: "good" | "bad", vector: 1 | -1 }
};
```

**Adoption Strategy**: Use throughline structure for story validation; use `custom_appreciation_namespace` for cross-framework mapping (see `GAP_ANALYSIS.md`).

#### 2. Three-Act Propp Generation (from `~/repos/propper`)

The propper Go implementation demonstrates a compositional approach to Propp function sequencing.

```go
// Generation algorithm (simplified)
func GenerateNarrative(seed int64) []Function {
    rng := rand.New(rand.NewSource(seed))
    narrative := []Function{}

    // Phase 1: Preparation (optional functions 1-7)
    for _, f := range preparationFunctions {
        if rng.Float64() < f.probability {
            narrative = append(narrative, f)
        }
    }

    // Phase 2: Complication (8 mandatory, 9-11 optional)
    narrative = append(narrative, villanyOrLack)  // Always required

    // Phase 3: Return (constraints: wedding requires victory)
    // Ordered return functions with dependencies

    return narrative
}
```

**Adoption Strategy**: Use probabilistic function selection with seed for reproducibility. Seed value enables deterministic plan generation for caching and replay.

#### 3. Beat Percentage Calculation (from `~/repos/beatsheet`)

The beatsheet JavaScript implementation provides two percentage strategies.

```javascript
// BS2 (Blake Snyder 2) breakpoints - screenplays
const bs2 = [0, 0.01, 0.05, 0.10, 0.20, 0.22, 0.50, 0.75, 0.80, 0.99, 1.0];

// Fiction (novel) breakpoints - longer form
const fiction = [0, 0.01, 0.03, 0.10, 0.20, 0.22, 0.50, 0.68, 0.77, 0.99, 1.0];

function calculateBeat(totalLength, beatIndex, strategy) {
    const breakpoints = strategy === 'bs2' ? bs2 : fiction;
    return Math.floor(totalLength * breakpoints[beatIndex]);
}
```

**Adoption Strategy**: Support multiple timing strategies; default to `fiction` for longer-form game narratives. Already captured in `save-the-cat-beats.yaml`.

#### 4. SVD Arc Classification (from `~/repos/core-stories`)

The core-stories Python/Jupyter notebooks show arc classification via SVD mode coefficients.

```python
def classify_arc(sentiment_timeseries):
    # Mean-center the timeseries
    centered = sentiment_timeseries - np.mean(sentiment_timeseries)

    # Project onto pre-computed SVD modes
    mode1_coef = np.dot(centered, MODE1_VECTOR)
    mode2_coef = np.dot(centered, MODE2_VECTOR)

    # Classify by dominant mode
    if abs(mode1_coef) > abs(mode2_coef):
        return "rags_to_riches" if mode1_coef > 0 else "tragedy"
    else:
        return "man_in_hole" if mode2_coef > 0 else "icarus"

    # Complex arcs (cinderella, oedipus) require mode combination analysis
```

**Adoption Strategy**: Pre-compute mode vectors; classify generated storylines to ensure arc compliance. Mode vectors are documented in `emotional-arcs.yaml`.

---

## Implementation Complexity Assessment

| Component | Complexity | Reference Implementation | Notes |
|-----------|------------|-------------------------|-------|
| Propp Functions | Low | `propper` (Go) | Direct port to C#; already in YAML |
| Save the Cat Beats | Low | `beatsheet` (JS) | Simple percentage math; already in YAML |
| Story Grid Genres | Low | Schema only | Configuration data; already in YAML |
| Emotional Arcs | Medium | `core-stories` (Python) | Port SVD classification; already in YAML |
| NCP/Dramatica | High | `narrative-context-protocol` | Selective adoption for cross-framework mapping |
| Exclusion Logic | Medium | `re-praxis` (C#) | Already C#; adapt patterns for WorldState |
| GOAP Integration | Medium | lib-behavior exists | Reuse with story action space |

**Conclusion**: Less work than music-analysis because:
1. Multiple working implementations exist to reference
2. Narrative structures are more enumerable than music theory
3. We can adopt selectively (skip full Dramatica quad algebra)
4. GOAP infrastructure already exists in lib-behavior

---

## References

### Primary Sources Implemented in YAML

| Source | Citation | YAML Schema |
|--------|----------|-------------|
| Propp | V. Propp, *Morphology of the Folktale* (1928) | `propp-functions.yaml` |
| Save the Cat | B. Snyder, *Save the Cat!* (2005) | `save-the-cat-beats.yaml` |
| Story Grid | S. Coyne, *The Story Grid* (2015) | `story-grid-genres.yaml` |
| Reagan et al. | "Emotional arcs of stories" (2016) | `emotional-arcs.yaml` |
| Story Grid Four Core | S. Coyne, storygrid.com (2015-2020) | `narrative-state.yaml` |

### Secondary Sources (This Document)

| Source | Citation | Section |
|--------|----------|---------|
| Greimas | A.J. Greimas, *Structural Semantics* (1966) | Actantial Model |
| Lehnert | W. Lehnert, "Plot Units and Narrative Summarization" (1981) | Plot Units |
| Barthes/Chatman | R. Barthes (1966), S. Chatman (1978) | Kernels vs. Satellites, Fabula/Sjuzhet |
| Mateas & Stern | "Façade Interactive Drama Architecture" (2005) | Computational Precedents |
| Evans & Short | "Versu—A Simulationist Storytelling System" (2013) | Re-Praxis Patterns |

### Reference Repositories

| Repository | Location | Language | Use Case |
|------------|----------|----------|----------|
| `narrative-context-protocol` | `~/repos/narrative-context-protocol` | JSON/TS | Cross-framework mapping hub |
| `re-praxis` | `~/repos/re-praxis` | C# | Exclusion logic patterns |
| `propper` | `~/repos/propper` | Go | Propp generation algorithm |
| `beatsheet` | `~/repos/beatsheet` | JS | Beat percentage calculation |
| `core-stories` | `~/repos/core-stories` | Python | SVD arc classification |

---

*This document supplements the authoritative YAML schemas. For gap analysis and implementation phases, see `GAP_ANALYSIS.md`.*
