# Vladimir Propp's Thirty-One Functions - Technical Summary

> **Source**: Sapna Dogra, "The Thirty-One Functions in Vladimir Propp's Morphology of the Folktale" (Rupkatha Journal, 2017); based on Vladimir Propp's "Morphology of the Folktale" (1928/1968)
> **Purpose**: Structural analysis of narrative through enumerable, sequentially-ordered functions
> **Implementation Relevance**: Critical - provides atomic narrative building blocks with ordering constraints

## Core Concept

Propp analyzed 102 Russian fairy tales and identified that all stories can be decomposed into a limited set of **functions** (character actions defined by their significance to the plot). These functions:

1. Are **limited in number** (exactly 31)
2. Follow a **fixed sequence** (order is always preserved, though functions may be omitted)
3. Are **character-agnostic** (same function can be performed by different character types)
4. Represent **irreducible narrative elements**

## The Thirty-One Functions

### Preparation Phase (Functions 1-7)

| # | Symbol | Name | Definition |
|---|--------|------|------------|
| 0 | α | Initial Situation | Not a function; establishes context (family, prosperity, hero introduced) |
| 1 | β | Absentation | A family member absents themselves from home |
| 2 | γ | Interdiction | A prohibition is given to the hero |
| 3 | δ | Violation | The interdiction is violated |
| 4 | ε | Reconnaissance | The villain attempts to gather information |
| 5 | ζ | Delivery | The villain receives information about the victim |
| 6 | η | Trickery | The villain attempts to deceive the victim |
| 7 | θ | Complicity | The victim is deceived and unwittingly helps the villain |

### Complication Phase (Functions 8-11)

| # | Symbol | Name | Definition |
|---|--------|------|------------|
| 8 | A | Villainy | The villain causes harm or injury to a family member |
| 8a | a | Lack | A family member lacks something or desires something |
| 9 | B | Mediation | Misfortune is made known; hero receives quest/command |
| 10 | C | Counteraction | The hero decides upon counteraction |
| 11 | ↑ | Departure | The hero leaves home |

### Donor Phase (Functions 12-14)

| # | Symbol | Name | Definition |
|---|--------|------|------------|
| 12 | D | First Donor Function | Hero is tested, interrogated, or attacked; prepares for receiving magical agent |
| 13 | E | Hero's Reaction | The hero reacts to the donor's actions |
| 14 | F | Magical Agent | The hero acquires use of a magical agent or helper |

### Quest Phase (Functions 15-19)

| # | Symbol | Name | Definition |
|---|--------|------|------------|
| 15 | G | Guidance | Hero is led to the object of search |
| 16 | H | Struggle | The hero and villain join in direct combat |
| 17 | I | Branding | The hero is marked (wound, ring, scarf given) |
| 18 | J | Victory | The villain is defeated |
| 19 | K | Liquidation | The initial misfortune or lack is resolved |

### Return Phase (Functions 20-22)

| # | Symbol | Name | Definition |
|---|--------|------|------------|
| 20 | ↓ | Return | The hero returns |
| 21 | Pr | Pursuit | The hero is pursued |
| 22 | Rs | Rescue | The hero is rescued from pursuit |

### Recognition Phase (Functions 23-31)

| # | Symbol | Name | Definition |
|---|--------|------|------------|
| 23 | o | Unrecognized Arrival | Hero arrives home or elsewhere, unrecognized |
| 24 | L | Unfounded Claims | A false hero presents unfounded claims |
| 25 | M | Difficult Task | A difficult task is proposed to the hero |
| 26 | N | Solution | The task is resolved |
| 27 | Q | Recognition | The hero is recognized |
| 28 | Ex | Exposure | The false hero or villain is exposed |
| 29 | T | Transfiguration | The hero is given a new appearance |
| 30 | U | Punishment | The villain is punished |
| 31 | W | Wedding | The hero is married and ascends the throne |

## Implementation Data Structures

```csharp
public enum ProppFunction
{
    // Preparation Phase
    InitialSituation,  // α - not a true function
    Absentation,       // β
    Interdiction,      // γ
    Violation,         // δ
    Reconnaissance,    // ε
    Delivery,          // ζ
    Trickery,          // η
    Complicity,        // θ

    // Complication Phase
    Villainy,          // A
    Lack,              // a (alternative to Villainy)
    Mediation,         // B
    Counteraction,     // C
    Departure,         // ↑

    // Donor Phase
    DonorTest,         // D
    HeroReaction,      // E
    MagicalAgent,      // F

    // Quest Phase
    Guidance,          // G
    Struggle,          // H
    Branding,          // I
    Victory,           // J
    Liquidation,       // K

    // Return Phase
    Return,            // ↓
    Pursuit,           // Pr
    Rescue,            // Rs

    // Recognition Phase
    UnrecognizedArrival, // o
    UnfoundedClaims,     // L
    DifficultTask,       // M
    Solution,            // N
    Recognition,         // Q
    Exposure,            // Ex
    Transfiguration,     // T
    Punishment,          // U
    Wedding              // W
}

public class ProppFunctionInstance
{
    public ProppFunction Function { get; set; }
    public int SequenceNumber { get; set; }
    public string Character { get; set; }       // Who performs the function
    public string Target { get; set; }          // Who is affected
    public string Details { get; set; }         // Specific manifestation
}
```

## Seven Spheres of Action (Character Roles)

Propp identified that functions cluster into seven **spheres of action**, each corresponding to a character archetype:

```csharp
public enum SphereOfAction
{
    Villain,      // Performs villainy, combat, pursuit
    Donor,        // Tests hero, provides magical agent
    Helper,       // Aids hero in quest, rescue, transfiguration
    Princess,     // Object of quest, assigns difficult tasks, recognizes hero
    Dispatcher,   // Sends hero on quest (mediation)
    Hero,         // Departs, reacts to donor, marries
    FalseHero     // Makes false claims, fails tasks
}

public class CharacterRole
{
    public SphereOfAction Sphere { get; set; }
    public List<ProppFunction> AssociatedFunctions { get; set; }
}

// Role-Function Mappings:
// Villain: Reconnaissance, Delivery, Trickery, Villainy, Struggle, Pursuit
// Donor: DonorTest, MagicalAgent provision
// Helper: Guidance, Struggle assistance, Rescue, Transfiguration
// Princess: DifficultTask, Recognition, Exposure, Wedding
// Dispatcher: Mediation (sends hero)
// Hero: Counteraction, Departure, HeroReaction, all quest functions
// FalseHero: UnfoundedClaims (exposed by Exposure)
```

### Character-Sphere Distribution Possibilities

1. **One-to-one**: Each sphere corresponds to one character
2. **Multi-sphere character**: One character occupies multiple spheres (e.g., donor who is also helper)
3. **Split sphere**: One sphere is distributed among multiple characters

## Structural Rules

### Sequentiality

Functions must appear in the canonical order. The sequence can be represented as:

```
α → β → γ → δ → ε → ζ → η → θ → A/a → B → C → ↑ → D → E → F → G → H → I → J → K → ↓ → Pr → Rs → o → L → M → N → Q → Ex → T → U → W
```

**Rules**:
- Functions can be **omitted** but never **reordered**
- Some functions form **pairs** (prohibition-violation, struggle-victory, pursuit-rescue)
- Functions may **repeat** (up to 3 times in some tales)
- **Moves**: A tale may have multiple "moves" (complete function sequences), each constituting a full narrative unit

### Function Pairs

Certain functions naturally pair and imply each other:

```csharp
public static readonly (ProppFunction, ProppFunction)[] FunctionPairs = new[]
{
    (ProppFunction.Interdiction, ProppFunction.Violation),
    (ProppFunction.Reconnaissance, ProppFunction.Delivery),
    (ProppFunction.Trickery, ProppFunction.Complicity),
    (ProppFunction.Struggle, ProppFunction.Victory),
    (ProppFunction.Pursuit, ProppFunction.Rescue),
    (ProppFunction.DifficultTask, ProppFunction.Solution),
    (ProppFunction.UnfoundedClaims, ProppFunction.Exposure),
};
```

### Function Groups (Phases)

Functions cluster into logical phases:

```csharp
public enum NarrativePhase
{
    Preparation,   // Functions 1-7: Setup before main action
    Complication,  // Functions 8-11: The problem and departure
    DonorSequence, // Functions 12-14: Acquiring means
    Quest,         // Functions 15-19: Confrontation and victory
    Return,        // Functions 20-22: Journey back
    Recognition    // Functions 23-31: Resolution and reward
}
```

## Computational Story Generation

Propp explicitly described how his morphology enables story generation:

> "In order to create a tale artificially, one may take any A, then one of the possible B's, then a C↑, followed by absolutely any D, then an E, one of the possible F's, then any G, and so on. In doing this, any elements may be dropped, or repeated three times, or repeated in various forms."

### Generation Algorithm

```csharp
public class ProppStoryGenerator
{
    public List<ProppFunctionInstance> GenerateStory(GenerationConstraints constraints)
    {
        var story = new List<ProppFunctionInstance>();

        // Always start with Initial Situation (α)
        story.Add(CreateFunction(ProppFunction.InitialSituation));

        // Select and instantiate functions in order
        foreach (var function in GetCanonicalSequence())
        {
            if (ShouldInclude(function, constraints))
            {
                var instance = InstantiateFunction(function, constraints);
                story.Add(instance);

                // Handle repetition (up to 3x)
                if (ShouldRepeat(function, constraints))
                {
                    story.Add(InstantiateFunction(function, constraints));
                }
            }
        }

        return story;
    }

    // Functions can be instantiated with different characters/details
    // while maintaining the same narrative function
    private ProppFunctionInstance InstantiateFunction(
        ProppFunction function,
        GenerationConstraints constraints)
    {
        // Assign appropriate character from available spheres
        // Fill in specific details based on domain/setting
        // Maintain consistency with previous functions
    }
}
```

### Validation Rules

```csharp
public class ProppValidator
{
    public bool IsValidSequence(List<ProppFunctionInstance> story)
    {
        var lastIndex = -1;
        foreach (var function in story)
        {
            var currentIndex = GetCanonicalIndex(function.Function);

            // Functions must maintain canonical order
            if (currentIndex < lastIndex)
                return false;

            lastIndex = currentIndex;
        }
        return true;
    }

    public bool HasRequiredPairs(List<ProppFunctionInstance> story)
    {
        foreach (var (first, second) in FunctionPairs)
        {
            if (story.Any(f => f.Function == first) &&
                !story.Any(f => f.Function == second))
                return false;
        }
        return true;
    }
}
```

## Integration with STORYLINE_COMPOSER

Propp's functions provide:

1. **Atomic narrative building blocks** - 31 enumerable functions for storyline composition
2. **Sequencing constraints** - Fixed ordering enables validation and planning
3. **Character role templates** - 7 spheres of action for cast requirements
4. **Function pairing** - Logical dependencies between narrative elements
5. **Phase structure** - 6 narrative phases for pacing
6. **Generation blueprint** - Propp's own algorithm for tale generation

### Mapping to GOAP Actions

Propp functions can serve as high-level GOAP actions:

```csharp
// Example: Villainy function as GOAP action
new GOAPAction("Villainy")
    .Requires("villain.has_target", true)
    .Requires("hero.at_home", true)
    .Effects("family.harmed", true)
    .Effects("hero.has_quest", true)
```

### Combining with Other Frameworks

Propp's functions are more granular than Save the Cat beats or Story Grid units:
- A single Propp function (e.g., "Struggle") might span a Save the Cat beat (e.g., "Finale")
- Multiple Propp functions compose a Story Grid scene
- Propp provides the "what happens" while other frameworks provide "why" and "when"
