# Narrative Context Protocol - Technical Summary

> **Source**: The Dramatica Co. (Narrative First Inc. + Write Bros. Inc.)
> **Version**: 1.2.0 (Schema)
> **Repository**: ~/repos/narrative-context-protocol
> **Specification**: arXiv 2503.04844
> **Implementation Relevance**: HIGH - provides standardized schema for authorial intent transport across multi-agent systems

## Core Philosophy

The Narrative Context Protocol (NCP) separates narrative design into two complementary layers:

| Layer | Purpose | Contents |
|-------|---------|----------|
| **Subtext** | Deep meaning (author's intent) | Perspectives, players, dynamics, storypoints, storybeats |
| **Storytelling** | Presentation (audience experience) | Overviews, moments, fabric constraints |

This separation allows AI agents to preserve authorial intent while adapting presentation.

## Schema Architecture

```json
{
  "schema_version": "1.2.0",
  "story": {
    "id": "story_<uuid>",
    "title": "string",
    "genre": "string",
    "logline": "string",
    "created_at": "ISO8601",
    "narratives": [
      {
        "subtext": { ... },
        "storytelling": { ... }
      }
    ]
  }
}
```

## The Four Canonical Perspectives

Every complete narrative has four throughlines representing different points of view on the central conflict:

| Perspective | Author POV | Definition | Typical Focus |
|-------------|------------|------------|---------------|
| **Objective Story** | "they" | Central conflict viewed externally | Plot, all characters as chess pieces |
| **Main Character** | "i" | Personal baggage of protagonist | Internal struggle |
| **Influence Character** | "you" | Alternate approach that challenges MC | Catalyst for growth |
| **Relationship Story** | "we" | Growth of key relationship | Emotional heart of story |

## The Nine Core Dynamics

Binary choices that define the story's thematic DNA:

| Dynamic | Options | Definition |
|---------|---------|------------|
| **Main Character Resolve** | change / steadfast | Does MC abandon or hold their worldview? |
| **Influence Character Resolve** | change / steadfast | Does IC continue challenging MC? |
| **Main Character Growth** | stop / start | Quit unhealthy behavior or adopt new one? |
| **Main Character Approach** | do_er / be_er | External action vs internal adjustment? |
| **Problem-Solving Style** | linear / holistic | Cause-effect vs relational reasoning? |
| **Story Limit** | optionlock / timelock | Limited by options or time? |
| **Story Driver** | action / decision | Actions force decisions or vice versa? |
| **Story Outcome** | success / failure | Is the Story Goal achieved? |
| **Story Judgment** | good / bad | Does MC resolve personal baggage? |

**Critical Formula**: `Dynamics × Storypoints = Storybeats`

## Narrative Functions (145 Total)

Engines of conflict that define how characters respond to problems:

### Categories and Examples

| Category | Count | Examples |
|----------|-------|----------|
| Perception & Awareness | ~15 | Aware, Consider, Realize, Preconscious, Subconscious |
| Action & Approach | ~20 | Approach, Attempt, Pursuit, Inaction, Proaction |
| Thought & Analysis | ~15 | Analysis, Logic, Theory, Evaluation, Investigation |
| Emotional/Psychological | ~20 | Feel, Trust, Doubt, Fear, Hope, Temptation |
| Change & Transformation | ~20 | Change, Becoming, Process, Progress, Unending |
| Knowledge & Understanding | ~15 | Knowledge, Learning, Understanding, Truth, Fact |
| Causation & Effect | ~10 | Cause, Effect, Result, Consequence |
| Control & Agency | ~15 | Control, Uncontrolled, Protection, Threat, Support |

## Appreciations of Narrative (979 Total)

Pairing a Perspective with a Narrative Function reveals meaning:

```
Main Character + Symptom = "Main Character Symptom"
Objective Story + Event 1-64 = "Objective Story Event 1" through "64"
Relationship Story + Progression 1-16 = sequential dramatic movements
```

## Storybeat Structure

```yaml
storybeat:
  id: "beat_001"
  scope: "signpost" | "progression" | "event"
  signpost: 1-4  # Major movement grouping
  sequence: integer  # Temporal ordering (required)
  narrative_function: "string"
  summary: "string"
  perspectives: ["perspective_id_1", ...]
```

### Scope Types

| Scope | Definition | Typical Count |
|-------|------------|---------------|
| **Signpost** | Major dramatic movement | 4 per throughline |
| **Progression** | Sequential movement within signpost | 4 per signpost |
| **Event** | Granular narrative beat | 64 total (4×4×4) |

## Moment Structure (Storytelling Layer)

```yaml
moment:
  type: "act" | "scene" | "sequence" | "chapter"
  order: integer
  summary: "string"
  synopsis: "string"
  setting: "string"
  timing: "string"
  fabric:
    - type: "space" | "time"
      limit: integer
  imperatives: ["string", ...]
  storybeats: ["beat_001", "beat_002", ...]
```

## Character Resolve Paths

### Steadfast Resolve Path
```
Introduction of challenging force
    ↓
Reinforcement of commitment
    ↓
Escalation of pressure
    ↓
Final crisis (choice to hold course)
    ↓
Resolution: Character stays committed
```

### Change Resolve Path
```
Initial full conviction in approach
    ↓
Blind spots emerge, cracks show
    ↓
Process of breaking down defenses
    ↓
Recognition of alternative choice
    ↓
Final crisis (choose new path)
    ↓
Resolution: Character changes perspective
```

## ID Patterns

```regex
# Valid ID formats
^(?:[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|(?:story|narrative|beat)_[A-Za-z0-9][A-Za-z0-9_-]*)$

# Normalized labels (snake_case)
^[a-z][a-z0-9_]*$
```

## Custom Framework Mappings

NCP allows mapping canonical elements to other frameworks:

```json
{
  "appreciation": "Main Character Issue",
  "custom_appreciation": "My Custom Term",
  "custom_appreciation_namespace": {
    "hero_journey": "The Call to Adventure",
    "save_the_cat": "Break into Two"
  }
}
```

## Implementation Data Structures

### Perspective

```csharp
public sealed class Perspective
{
    public string Id { get; }
    public AuthorPov AuthorStructuralPov { get; }  // "i", "you", "we", "they"
    public string Summary { get; }
    public StorytellingRef[] Storytelling { get; }
}

public enum AuthorPov
{
    I,      // Main Character
    You,    // Influence Character
    We,     // Relationship Story
    They    // Objective Story
}
```

### Dynamic

```csharp
public sealed class Dynamic
{
    public string Id { get; }
    public string DynamicType { get; }  // e.g., "main_character_resolve"
    public string Vector { get; }       // e.g., "change" or "steadfast"
}
```

### Storypoint

```csharp
public sealed class Storypoint
{
    public string Id { get; }
    public string Appreciation { get; }        // e.g., "Main Character Symptom"
    public string NarrativeFunction { get; }   // e.g., "Doubt"
    public string Illustration { get; }
    public string Summary { get; }
    public string[] PerspectiveIds { get; }
}
```

### Storybeat

```csharp
public sealed class Storybeat
{
    public string Id { get; }
    public StoryScope Scope { get; }
    public int? Signpost { get; }      // 1-4
    public int Sequence { get; }       // Temporal ordering
    public string NarrativeFunction { get; }
    public string Summary { get; }
    public string[] PerspectiveIds { get; }
}

public enum StoryScope
{
    Signpost,
    Progression,
    Event
}
```

## Integration with STORYLINE_COMPOSER

NCP provides:

1. **Four Perspectives** → Map to character archetypes and POV tracking
2. **Nine Dynamics** → Binary story design constraints for GOAP goals
3. **145 Narrative Functions** → Action library for story planning
4. **Storybeat Sequencing** → Temporal ordering for phase generation
5. **Scope Hierarchy** → Signpost > Progression > Event for detail levels
6. **Framework Mappings** → Translate to Propp, Save the Cat, Hero's Journey

### Example: Mapping Dynamics to GOAP Goals

```csharp
// Dynamics define binary story constraints
if (dynamics.StoryOutcome == "success" && dynamics.StoryJudgment == "good")
{
    // Happy ending - MC achieves goal AND resolves personal issue
    goalState.Hope = 0.9;
    goalState.Tension = 0.1;  // Resolved
}
else if (dynamics.StoryOutcome == "failure" && dynamics.StoryJudgment == "bad")
{
    // Tragedy - MC fails AND doesn't resolve personal issue
    goalState.Hope = 0.1;
}
```

### Example: Using Narrative Functions as Actions

```csharp
// Map NCP functions to GOAP actions
var ncpFunction = "Temptation";
var goapAction = new NarrativeAction(
    name: ncpFunction,
    preconditions: new NarrativeStateRange { MinMystery = 0.3 },
    effects: new NarrativeStateDelta { Hope = -0.1, Tension = +0.15 },
    cost: 1.2
);
```

## Validation Requirements

Per JSON Schema (Draft-07):

| Level | Required Fields |
|-------|-----------------|
| Story | id, title, logline, created_at, narratives |
| Narrative | id, title, subtext, storytelling |
| Subtext | perspectives, players, dynamics, storypoints, storybeats |
| Storytelling | overviews, moments |
| Perspective | id, author_structural_pov, summary, storytelling |
| Storybeat | id, scope, sequence, narrative_function, summary, perspectives |

## Key Findings for Storyline SDK

1. **Two-layer architecture** - Separate meaning (subtext) from presentation (storytelling)
2. **Four perspectives mandatory** - Complete stories need OS, MC, IC, RS throughlines
3. **Nine dynamics define outcomes** - Binary constraints for story shape
4. **Sequence is required** - Storybeats must have temporal ordering
5. **Scope hierarchy** - Signpost > Progression > Event for detail granularity
6. **Framework agnostic** - Custom mappings allow translation to any methodology

## References

- The Dramatica Co. (2024). Narrative Context Protocol Specification v1.2.0
- arXiv: 2503.04844
- Phillips, M.A., & Huntley, C. (1993). Dramatica: A New Theory of Story
- narrative-context-protocol: ~/repos/narrative-context-protocol
