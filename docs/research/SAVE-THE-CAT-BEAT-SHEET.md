# Save the Cat Beat Sheet - Technical Summary

> **Source**: Blake Snyder's "Save the Cat!" methodology, comprehensive breakdown from Kindlepreneur
> **Purpose**: 15-beat story structure framework with precise timing markers
> **Implementation Relevance**: High - provides percentage-based beat placement for procedural story generation

## Core Concept

The Save the Cat Beat Sheet divides a story into **15 sequential beats** with specific percentage markers indicating when each should occur. This provides a deterministic framework for pacing storyline generation.

## The 15 Beats (Complete Specification)

### Act One: Setup (0% - 20%)

| Beat | % | Name | Function |
|------|---|------|----------|
| 1 | 0-1% | **Opening Image** | Snapshot of protagonist's "before" state |
| 2 | ~5% | **Theme Stated** | Subtle hint of story's deeper truth |
| 3 | 1-10% | **Setup** | Introduce hero, stakes, flaws, supporting cast |
| 4 | ~10% | **Catalyst** | Inciting incident that changes everything |
| 5 | 10-20% | **Debate** | Hero hesitates, wrestling with doubts |
| 6 | 20% | **Break Into Two** | Hero commits, enters "new world" |

### Act Two: Confrontation (20% - 80%)

| Beat | % | Name | Function |
|------|---|------|----------|
| 7 | ~22% | **B Story** | Secondary plot deepens theme via relationship |
| 8 | 20-50% | **Fun and Games** | "Promise of the premise" - exploring the hook |
| 9 | 50% | **Midpoint** | Major twist: false victory, false defeat, or stakes raise |
| 10 | 50-75% | **Bad Guys Close In** | External threats and internal doubts collide |
| 11 | ~75% | **All Is Lost** | Something "dies" - hope fades |
| 12 | 75-80% | **Dark Night of the Soul** | Hero reflects, faces who they must become |

### Act Three: Resolution (80% - 100%)

| Beat | % | Name | Function |
|------|---|------|----------|
| 13 | ~80% | **Break Into Three** | Epiphany sparks renewed resolve |
| 14 | 80-99% | **Finale** | Climax - protagonist applies lessons to confront conflict |
| 15 | 99-100% | **Final Image** | Mirror of Opening Image showing transformation |

## Implementation Data Structures

```csharp
public enum BeatType
{
    OpeningImage,
    ThemeStated,
    Setup,
    Catalyst,
    Debate,
    BreakIntoTwo,
    BStory,
    FunAndGames,
    Midpoint,
    BadGuysCloseIn,
    AllIsLost,
    DarkNightOfSoul,
    BreakIntoThree,
    Finale,
    FinalImage
}

public class Beat
{
    public BeatType Type { get; set; }
    public double StartPercent { get; set; }
    public double EndPercent { get; set; }
    public string Function { get; set; }
    public int Act { get; set; }  // 1, 2, or 3
}

public class BeatSheet
{
    public static readonly Beat[] Beats = new[]
    {
        new Beat { Type = BeatType.OpeningImage, StartPercent = 0, EndPercent = 0.01, Act = 1 },
        new Beat { Type = BeatType.ThemeStated, StartPercent = 0.05, EndPercent = 0.05, Act = 1 },
        new Beat { Type = BeatType.Setup, StartPercent = 0.01, EndPercent = 0.10, Act = 1 },
        new Beat { Type = BeatType.Catalyst, StartPercent = 0.10, EndPercent = 0.10, Act = 1 },
        new Beat { Type = BeatType.Debate, StartPercent = 0.10, EndPercent = 0.20, Act = 1 },
        new Beat { Type = BeatType.BreakIntoTwo, StartPercent = 0.20, EndPercent = 0.20, Act = 1 },
        new Beat { Type = BeatType.BStory, StartPercent = 0.22, EndPercent = 0.22, Act = 2 },
        new Beat { Type = BeatType.FunAndGames, StartPercent = 0.20, EndPercent = 0.50, Act = 2 },
        new Beat { Type = BeatType.Midpoint, StartPercent = 0.50, EndPercent = 0.50, Act = 2 },
        new Beat { Type = BeatType.BadGuysCloseIn, StartPercent = 0.50, EndPercent = 0.75, Act = 2 },
        new Beat { Type = BeatType.AllIsLost, StartPercent = 0.75, EndPercent = 0.75, Act = 2 },
        new Beat { Type = BeatType.DarkNightOfSoul, StartPercent = 0.75, EndPercent = 0.80, Act = 2 },
        new Beat { Type = BeatType.BreakIntoThree, StartPercent = 0.80, EndPercent = 0.80, Act = 3 },
        new Beat { Type = BeatType.Finale, StartPercent = 0.80, EndPercent = 0.99, Act = 3 },
        new Beat { Type = BeatType.FinalImage, StartPercent = 0.99, EndPercent = 1.00, Act = 3 },
    };
}
```

## Key Beat Details

### Midpoint Types

The Midpoint (~50%) is critical and comes in two flavors:

```csharp
public enum MidpointType
{
    FalseVictory,   // Protagonist seems to achieve goal, but it's illusion
    FalseDefeat,    // Protagonist suffers crushing setback
    StakesShift     // Rules of the game change entirely
}
```

**False Victory**: Goal appears achieved, but deeper problems hidden
- Example: Gatsby reunites with Daisy, but cracks show

**False Defeat**: Rock bottom moment that sparks growth
- Example: Fellowship loses Gandalf in Moria

**Stakes Shift**: Fundamental change in story nature
- Example: Titanic hits iceberg - love story becomes survival

### All Is Lost: Death Types

Something "dies" at 75%, but death can be:

```csharp
public enum DeathType
{
    LiteralDeath,      // Character actually dies
    PlanCollapse,      // Strategy/scheme fails completely
    BeliefDeath,       // Core belief is shattered
    RelationshipDeath, // Bond is severed
    InnocenceDeath,    // Worldview is destroyed
    IdentityDeath      // Old self must die for new to emerge
}
```

### Opening/Final Image Contrast

The Final Image must mirror the Opening Image to show transformation:

```csharp
public class ImagePair
{
    public string OpeningState { get; set; }   // "Before" snapshot
    public string FinalState { get; set; }     // "After" snapshot
    public string Transformation { get; set; } // What changed
}

// Example:
// Opening: Red in prison, hopeless
// Final: Red walking free on beach
// Transformation: Despair → Hope fulfilled
```

## Beat Functions by Category

### Exposition Beats (Information Delivery)
- **Opening Image**: Establish protagonist's status quo
- **Theme Stated**: Plant thematic seed
- **Setup**: Deliver necessary context

### Transition Beats (State Changes)
- **Catalyst**: Push from ordinary to extraordinary
- **Break Into Two**: Cross threshold into new world
- **Midpoint**: Shift from reactive to proactive (or reverse)
- **Break Into Three**: Shift from despair to resolve

### Pressure Beats (Tension Building)
- **Debate**: Internal resistance to call
- **Bad Guys Close In**: External/internal forces converge
- **Dark Night of Soul**: Maximum internal pressure

### Release Beats (Tension Resolution)
- **Fun and Games**: Explore promise of premise
- **All Is Lost**: Release of false hope
- **Finale**: Ultimate confrontation and resolution
- **Final Image**: Emotional closure

## Narrative State Progression

The beat sheet implies specific narrative state progressions:

```
Opening Image    → Stability (low tension, clear status quo)
Catalyst         → Disruption (tension spike)
Debate           → Uncertainty (tension oscillating)
Break Into Two   → Commitment (tension directed)
Fun and Games    → Exploration (rising but controlled tension)
Midpoint         → Pivot (tension reframed)
Bad Guys Close In → Pressure (accelerating tension)
All Is Lost      → Collapse (maximum despair)
Dark Night       → Reflection (internalized tension)
Break Into Three → Resolution-seeking (tension channeled)
Finale           → Climax (maximum external tension, resolution)
Final Image      → New Equilibrium (tension released, changed state)
```

## B Story Integration

The B Story (secondary plot) serves specific functions:

1. **Theme Carrier**: Often where theme is most explicitly explored
2. **Relationship Development**: Usually involves mentor, love interest, or ally
3. **Lesson Source**: Provides insights protagonist needs for finale
4. **Pacing Tool**: Offers relief from main plot intensity

**Timing**: Introduced ~22%, pays off in Break Into Three

## Implementation Recommendations

### Beat-Based Planning

Use percentage markers to allocate storyline phases:

```csharp
public class StorylinePlan
{
    public double TotalDuration { get; set; }  // In arbitrary units

    public double GetBeatStartTime(BeatType beat)
    {
        var beatDef = BeatSheet.Beats.First(b => b.Type == beat);
        return TotalDuration * beatDef.StartPercent;
    }

    public double GetBeatEndTime(BeatType beat)
    {
        var beatDef = BeatSheet.Beats.First(b => b.Type == beat);
        return TotalDuration * beatDef.EndPercent;
    }
}
```

### Narrative State Targets by Beat

Each beat implies target narrative state:

```csharp
public class BeatTarget
{
    public BeatType Beat { get; set; }
    public NarrativeState TargetState { get; set; }
}

// Examples:
// AllIsLost → Hope: 0.1, Tension: 0.9, Stakes: 0.9
// FinalImage → Hope: 0.9 (positive) or 0.1 (tragic), Tension: 0.1
```

### Validation Rules

- Opening Image and Final Image must contrast
- Catalyst must occur before Break Into Two
- Midpoint must actually shift stakes/direction
- All Is Lost must precede Dark Night of Soul
- Theme Stated should connect to Finale resolution

## Integration with STORYLINE_COMPOSER

Save the Cat provides:

1. **Percentage-based beat timing** for storyline phase planning
2. **Midpoint types** (false victory/defeat) for act 2 structure
3. **Death types** for All Is Lost beat design
4. **B Story pattern** for secondary relationship arcs
5. **Image contrast** requirement for arc validation
6. **Narrative state targets** per beat for GOAP planning

The 15-beat structure maps directly to storyline phases, with percentage markers providing deterministic pacing for procedurally generated narratives.
