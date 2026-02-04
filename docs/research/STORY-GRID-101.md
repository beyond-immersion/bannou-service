# Story Grid 101 - Technical Summary

> **Source**: Shawn Coyne, "Story Grid 101: The Five First Principles" (Story Grid Publishing, 2020)
> **Purpose**: Foundational methodology for story analysis and construction
> **Implementation Relevance**: High - provides hierarchical story unit structure and scene-level analysis patterns

## Five First Principles

### Principle 1: Story Units

Stories are composed of nested units, each containing the next like Russian dolls:

```
Global Story
├── Act (3 standard: Beginning Hook, Middle Build, Ending Payoff)
│   ├── Subplot (amplifies/contrasts theme)
│   │   ├── Sequence (2+ scenes with shared purpose)
│   │   │   ├── Scene (micro unit with value change)
│   │   │   │   └── Beat (smallest: behavior change moment)
```

**Implementation Structure**:
```csharp
public enum StoryUnitType
{
    Beat,       // Moment of behavior change
    Scene,      // Micro unit with value shift
    Sequence,   // 2+ scenes with shared purpose
    Act,        // Permanent life change
    Subplot,    // Amplifies or contrasts theme
    Global      // Complete story arc
}

public class StoryUnit
{
    public StoryUnitType Type { get; set; }
    public string StoryEvent { get; set; }     // One-sentence summary
    public ValueShift ValueChange { get; set; }
    public List<StoryUnit> Children { get; set; }
    public FiveCommandments Commandments { get; set; }
}
```

### Principle 2: Change

All stories are about change. Each unit must produce measurable change:
- **Scene Level**: Value shift from positive to negative (or reverse)
- **Act Level**: Permanent, irreversible change in protagonist
- **Global Level**: Transformation that provides knowledge about how to act

### Principle 3: Universal Human Values

Change occurs on value spectrums. Positive and negative poles with gradations:

| Positive | Negative | Used In |
|----------|----------|---------|
| Life | Death | Action, Thriller, Horror |
| Safety | Danger | Action, War |
| Love | Hate | Love |
| Justice | Injustice | Crime |
| Respect | Disrespect | Performance, Status |
| Knowledge | Ignorance | Worldview |
| Freedom | Subjugation | Western, Society |
| Success | Failure | Status |

### Principle 4: Story Event

One-sentence distillation of scene content. Generated via four questions:

1. **Literal Action**: What are characters physically doing?
2. **Essential Tactic**: What macro behavior toward a human value?
3. **Value Shift**: What changed from beginning to end?
4. **Story Event**: Synthesis sentence combining above

**Scene Analysis Pattern**:
```csharp
public class SceneAnalysis
{
    public string LiteralAction { get; set; }      // Q1: On-the-ground actions
    public string EssentialTactic { get; set; }    // Q2: Macro behavior toward value
    public ValueShift ValueChange { get; set; }    // Q3: Value spectrum movement
    public string StoryEvent { get; set; }         // Q4: Synthesis sentence
}

public class ValueShift
{
    public string Spectrum { get; set; }           // e.g., "Life-Death"
    public double StartValue { get; set; }         // 0-1 normalized
    public double EndValue { get; set; }           // 0-1 normalized
    public string StartLabel { get; set; }         // e.g., "Safe"
    public string EndLabel { get; set; }           // e.g., "In Danger"
}
```

### Principle 5: Five Commandments of Storytelling

Every story unit contains this pattern of change:

```
1. Inciting Incident     → Imbalance created, goal adopted
2. Turning Point (Phere) → Unexpected event turns the value
3. Crisis                → Dilemma forces choice
4. Climax                → Character's decision and action
5. Resolution            → Outcome of the climactic action
```

**Implementation Structure**:
```csharp
public class FiveCommandments
{
    public string IncitingIncident { get; set; }   // What kicks off action
    public string TurningPoint { get; set; }       // The "phere" - ball of chaos
    public CrisisType CrisisType { get; set; }     // Best Bad Choice or Irreconcilable Goods
    public string Crisis { get; set; }             // The dilemma question
    public string Climax { get; set; }             // The decision/action taken
    public string Resolution { get; set; }         // The outcome
}

public enum CrisisType
{
    BestBadChoice,        // Choose between two negative options
    IrreconcilableGoods   // Choose between goods that conflict
}
```

**Phere (Turning Point) Characteristics**:
- Unexpected event that prevents goal achievement via planned path
- Can be action (external event) or revelation (internal realization)
- Creates the crisis by forcing a choice the character didn't anticipate

## Story Grid Tools

### The Foolscap (Global Planning)

Single-page summary answering six core questions:

1. **Global Genre**: External (Action, War, Horror, Crime, Thriller, Western, Love, Performance, Society) + Internal (Status, Morality, Worldview)
2. **Conventions & Obligatory Events**: Genre-required elements and scenes
3. **POV & Narrative Device**: Vantage point and presentation form
4. **Objects of Desire**: Wants (conscious, external) vs Needs (subconscious, internal)
5. **Controlling Idea/Theme**: One-sentence takeaway message
6. **Beginning Hook / Middle Build / Ending Payoff**: Three-act summary

**Data Structure**:
```csharp
public class Foolscap
{
    public Genre ExternalGenre { get; set; }
    public Genre InternalGenre { get; set; }
    public List<string> Conventions { get; set; }
    public List<string> ObligatoryEvents { get; set; }
    public PointOfView POV { get; set; }
    public string NarrativeDevice { get; set; }
    public string ExternalWant { get; set; }
    public string InternalNeed { get; set; }
    public string ControllingIdea { get; set; }
    public ActSummary BeginningHook { get; set; }
    public ActSummary MiddleBuild { get; set; }
    public ActSummary EndingPayoff { get; set; }
}
```

### The Spreadsheet (Scene-Level Tracking)

Scene-by-scene analysis tracking:
- Word count
- Story Event (one-sentence summary)
- Value shift (spectrum + start/end points)
- Turning point type (action or revelation)
- Point of view character
- Time period and duration
- Location
- Onstage and offstage characters

**Reveals through data analysis**:
- Pacing variation (scene length patterns)
- Character appearance frequency
- Value progression over time
- Location usage patterns

### The Infographic (Visual Progression)

Two-axis visualization:
- **X-axis**: Time/scene progression
- **Y-axis**: Value spectrum position for both internal and external genres

Shows the "shape" of the story's emotional journey.

## Story's Boundaries

All stories exist between two fundamental genres:

### Action (On the Ground)
- External boundary of all stories
- Characters ACT to create change in environment
- Motor movements from Point A to Point Z
- Pursuit of external goals

### Worldview (In the Clouds)
- Internal boundary of all stories
- Characters TRANSFORM their understanding
- Cognitive frame breaking when knowledge fails
- Transcendence to higher vantage point

**Key Insight**: Every story contains both:
1. Action component (external problem solving)
2. Worldview component (internal transformation)

## Implementation Recommendations

### Scene Generation

Each scene requires:
1. Five Commandments (structural skeleton)
2. Value shift on relevant spectrum
3. Story Event summary
4. Character tactics linked to human values

### Storyline Planning

Use Foolscap structure for high-level planning:
1. Identify genre (external + internal)
2. List required conventions and obligatory events
3. Define wants (external) and needs (internal)
4. Craft controlling idea
5. Outline three acts with Five Commandments each

### Value Tracking

Maintain scene-by-scene value tracking:
- Primary value spectrum (genre-determined)
- Current position on spectrum
- Direction of movement (positive/negative)
- Cumulative progression toward climax

### Crisis Design

Two crisis types create different tensions:
- **Best Bad Choice**: Both options have negative consequences (tragic tension)
- **Irreconcilable Goods**: Both options are desirable but mutually exclusive (moral tension)

### Turning Point (Phere) Design

Effective turning points:
- Are unexpected by the character
- Prevent the planned approach from working
- Force a crisis decision
- Can be external (action) or internal (revelation)

## Integration with STORYLINE_COMPOSER

Story Grid 101 provides:

1. **Hierarchical story structure** (Beat → Scene → Sequence → Act → Global)
2. **Five Commandments pattern** for scene/act generation
3. **Value spectrum tracking** for narrative state management
4. **Scene analysis questions** for Story Event generation
5. **Crisis types** for dilemma design
6. **Three-act structure** (Hook/Build/Payoff) for pacing
7. **Foolscap template** for storyline planning

The Five Commandments pattern is directly applicable to GOAP action sequencing:
- Inciting Incident → Initial world state perturbation
- Turning Point → Complication that requires re-planning
- Crisis → Decision point requiring character choice
- Climax → Action execution
- Resolution → New world state
