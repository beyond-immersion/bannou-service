# Propper Implementation - Technical Summary

> **Source**: Propper (Go implementation of Propp's Morphology)
> **Repository**: ~/repos/propper
> **Based On**: Vladimir Propp, "Morphology of the Folktale" (1928)
> **Exegesis**: Cosma Shalizi (bactra.org/reviews/propp-morphology.html)
> **Implementation Relevance**: HIGH - provides seeded, deterministic narrative generation from structural grammar

## Core Concept

Propper implements Vladimir Propp's 31 narrative functions as a **finite state automaton** that generates folktale outlines through seeded random selection of function variants within a three-act structure.

## The 31 Functions with Variants

### Preparation Phase (Functions 1-7)

| # | Symbol | Name | Variants |
|---|--------|------|----------|
| 1 | β | Absentation | "One member of the family absents from home" |
| 2 | γ | Interdiction | "An interdiction is addressed to the hero" |
| 3 | δ | Violation | "The interdiction is violated" |
| 4 | ε | Reconnaissance | "The villain makes an attempt at reconnaissance" |
| 5 | ζ | Delivery | "The villain receives information about the victim" |
| 6 | η | Trickery | "The villain attempts to possess something" / "...belongings of victim" (2 variants) |
| 7 | θ | Complicity | "The victim submits to deception" |

### Complication Phase (Functions 8-11)

| # | Symbol | Name | Variants |
|---|--------|------|----------|
| 8 | A/a | Villainy/Lack | "The villain causes harm" / "One member lacks something" / "One member desires something" (3 variants) |
| 9 | B | Mediation | Request/command × allowed/dispatched (4 variants) |
| 10 | C | Counteraction | "agrees to counteraction" / "decides upon counteraction" (2 variants) |
| 11 | ↑ | Departure | "The hero leaves home" (1 variant - deterministic) |

### Donor Sequence (Functions 12-14)

| # | Symbol | Name | Variants |
|---|--------|------|----------|
| 12 | D | Testing | tested/interrogated/attacked × magical agent/helper (6 variants) |
| 13 | E | Reaction | "The hero reacts to the actions of the future donor" |
| 14 | F | Acquisition | "The hero acquires the use of a magical agent" |

### Transference & Struggle (Functions 15-19)

| # | Symbol | Name | Variants |
|---|--------|------|----------|
| 15 | G | Guidance | "transferred" / "delivered" / "led" (3 variants) |
| 16 | H | Struggle | "The hero and the villain join in direct combat" |
| 17 | I | Branding | "The hero is branded or marked" |
| 18 | J | Victory | "The villain is defeated" |
| 19 | K | Liquidation | "The initial misfortune or lack is liquidated" |

### Return Sequence (Functions 20-22)

| # | Symbol | Name | Variants |
|---|--------|------|----------|
| 20 | ↓ | Return | "The hero returns" (1 variant - deterministic) |
| 21 | Pr | Pursuit | "The hero is pursued" |
| 22 | Rs | Rescue | "Rescue of the hero from pursuit" |

### Recognition Sequence (Functions 23-31)

| # | Symbol | Name | Variants |
|---|--------|------|----------|
| 23 | o | Arrival | "arrives home unrecognized" / "in another country" (2 variants) |
| 24 | L | Claim | "A false hero presents unfounded claims" |
| 25 | M | Task | "A difficult task is proposed to the hero" |
| 26 | N | Solution | "The task is resolved" |
| 27 | Q | Recognition | "The hero is recognized" |
| 28 | Ex | Exposure | "The false hero or villain is exposed" |
| 29 | T | Transfiguration | "The hero is given a new appearance" |
| 30 | U | Punishment | "The villain is punished" |
| 31 | W | Wedding | "The hero is married and ascends the throne" |

## Three-Act Structure

### Act 1: Exposition & Preparation (Deterministic)

```go
func (p *propper) act1() []string {
    return applyAllFunctions("A", "B", "C", "↑", "D", "E", "F", "G")
}
// Sequence: Villainy → Call → Decision → Departure → Test → Reaction → Gift → Transfer
```

### Act 2: Conflict Resolution (Probabilistic Branching)

Two mutually exclusive paths with 50/50 selection:

**Path A - Struggle Flow:**
```go
func (p *propper) struggle() []string {
    return applyAllFunctions("H", "J", "I", "K", "↓", "Pr", "Rs", "o", "L")
}
// Combat → Victory → Brand → Liquidation → Return → Pursuit → Rescue → Arrival → False Claim
```

**Path B - Task Flow:**
```go
func (p *propper) task() []string {
    return applyAllFunctions("L", "M", "J", "N", "K", "↓", "Pr", "Rs")
}
// False Claim → Task → Brand → Solution → Liquidation → Return → Pursuit → Rescue
```

**Branching Logic:**
```go
func (p *propper) act2() []string {
    return applyAllRules(
        p.struggleOrNil,  // 50% chance: struggle flow or nothing
        p.taskOrNil,      // 50% chance: task flow or nothing
    )
}
// Four possible outcomes: struggle only, task only, both, or neither
```

### Act 3: Resolution (Deterministic)

```go
func (p *propper) act3() []string {
    return applyAllFunctions("Q", "Ex", "T", "U", "W")
}
// Recognition → Exposure → Transformation → Punishment → Wedding
```

## Key Algorithms

### Seeded Random Generation

```go
type propper struct {
    seed int64
    r    *rand.Rand
}

func New(seed int64) Propper {
    if seed == 0 {
        seed = time.Now().UnixNano()
    }
    return &propper{
        seed: seed,
        r:    rand.New(rand.NewSource(seed)),
    }
}
```

**Same seed → identical story every time** (deterministic replay)

### Rule Composition

```go
// Sequential application - all rules in order
func applyAllRules(rules ...func() []string) []string {
    result := []string{}
    for _, rule := range rules {
        result = append(result, rule()...)
    }
    return result
}

// Random selection - pick ONE rule
func applyAnyRule(rules ...func() []string) []string {
    index := p.r.Intn(len(rules))
    return rules[index]()
}

// Function-to-variant mapping
func applyAllFunctions(fns ...string) []string {
    result := []string{}
    for _, fn := range fns {
        variant := choose(functions[fn])  // Random variant selection
        result = append(result, variant)
    }
    return result
}
```

### Variant Selection

```go
func (p *propper) choose(choices []string) string {
    index := p.r.Intn(len(choices))
    return choices[index]
}
// Uniform distribution: 1/N probability for each variant
```

### Deduplication

```go
func (p *propper) Go() {
    var previousLine string
    for _, line := range p.story() {
        if line == previousLine {
            continue  // Skip consecutive duplicates
        }
        println(line)
        previousLine = line
    }
}
```

## Output Format

**Two outputs:**
1. **stderr**: Seed + function codes (e.g., `ABC↑DEFGHJIK↓PrRsoLQExTUW`)
2. **stdout**: Prose narrative (human-readable story outline)

**Example Output:**
```
seed: 1572475950978442000
functions: ABC↑DEFGHJIK↓PrRsoLQExTUW

One member of a family lacks something
Misfortune or lack is made known; the hero is approached with a request; he is allowed to go
The seeker agrees to counteraction
The hero leaves home
The hero is attacked, which prepares the way for receiving a magical agent
The hero reacts to the actions of the future donor
The hero acquires the use of a magical agent
The hero is led to the whereabouts of an object of search
The hero and the villain join in direct combat
The hero is branded or marked
The villain is defeated
The initial misfortune or lack is liquidated
The hero returns
The hero is pursued
Rescue of the hero from pursuit
The hero, unrecognized, in another country
A false hero presents unfounded claims
The hero is recognized
The false hero or villain is exposed
The hero is given a new appearance
The villain is punished
The hero is married and ascends the throne
```

## Numeric Constants

| Entity | Count |
|--------|-------|
| Total essential functions | 24 (A-W, using symbols like ↑↓) |
| Functions with variants | ~15 (1-6 variants each) |
| Less-essential functions (β-θ) | 8 (not yet integrated) |
| Act 1 functions | 8 |
| Act 2 branching outcomes | 4 |
| Act 3 functions | 5 |
| Max variants per function | 6 (function D - testing) |
| Probability distribution | Uniform (1/N) |

## Implementation Data Structures

### Function Map

```go
var functions = map[string][]string{
    "A": {
        "The villain causes harm",
        "One member of a family lacks something",
        "One member of a family desires something",
    },
    "B": {
        "Misfortune or lack is made known; the hero is approached with a request; he is allowed to go",
        "Misfortune or lack is made known; the hero is approached with a request; he is dispatched",
        "Misfortune or lack is made known; the hero is approached with a command; he is allowed to go",
        "Misfortune or lack is made known; the hero is approached with a command; he is dispatched",
    },
    // ... 22 more functions
}
```

### Story State

```go
type StoryState struct {
    Seed           int64
    FunctionCodes  []string  // Sequence of applied function symbols
    ProseLines     []string  // Human-readable narrative
}
```

## Integration with STORYLINE_COMPOSER

Propper provides:

1. **Atomic narrative units** - 31 functions as story building blocks
2. **Variant system** - Multiple concrete realizations per function
3. **Three-act structure** - Deterministic frame with probabilistic middle
4. **Seeded generation** - Reproducible storylines for testing/replay
5. **Function composition** - Rules for combining narrative atoms

### Mapping to StorylineIntent

```csharp
// Map Propp functions to storyline intents
var proppToIntent = new Dictionary<string, StorylineIntent>
{
    ["A"] = new VillainyIntent { Type = "causes_harm" },
    ["B"] = new MediationIntent { Type = "call_to_adventure" },
    ["D"] = new TestingIntent { Type = "donor_test" },
    ["H"] = new CombatIntent { Type = "direct_combat" },
    ["W"] = new ResolutionIntent { Type = "wedding_throne" },
};
```

### Using Propp for Quest Generation

```csharp
public class ProppQuestGenerator
{
    private readonly Random _random;

    public QuestOutline Generate(int seed)
    {
        _random = new Random(seed);

        var outline = new QuestOutline
        {
            Seed = seed,
            Phases = new List<QuestPhase>()
        };

        // Act 1: Setup (deterministic sequence)
        outline.Phases.Add(GeneratePhase("Setup", Act1Functions));

        // Act 2: Conflict (probabilistic branching)
        var path = _random.Next(4);
        switch (path)
        {
            case 0: outline.Phases.Add(GeneratePhase("Struggle", StruggleFunctions)); break;
            case 1: outline.Phases.Add(GeneratePhase("Task", TaskFunctions)); break;
            case 2: // Both
                outline.Phases.Add(GeneratePhase("Struggle", StruggleFunctions));
                outline.Phases.Add(GeneratePhase("Task", TaskFunctions));
                break;
            case 3: // Neither - abbreviated quest
                break;
        }

        // Act 3: Resolution (deterministic sequence)
        outline.Phases.Add(GeneratePhase("Resolution", Act3Functions));

        return outline;
    }
}
```

## Key Findings for Storyline SDK

1. **Functions are atomic** - Cannot be subdivided further
2. **Three-act frame is stable** - Only Act 2 varies
3. **Seeded generation enables replay** - Same seed = same story
4. **Uniform variant selection** - All variants equally likely
5. **Deduplication is important** - Prevents repetitive output
6. **Functions are verb-centric** - Describe actions, not states

## Open Questions from Propper

1. **Em-dash significance** - What does `—` mean in sequences like `ABC↑FH−IK↓LM−NQExUW`?
2. **Less-essential integration** - Where do β-θ functions interpolate?
3. **Concrete sub-types** - Expand "pursued" → "flies after" / "gnaws through tree" / etc.

## References

- Propp, V. (1928). Morphology of the Folktale. (English trans. 1958, 1968)
- Shalizi, C. (2002). Review of Propp's Morphology. bactra.org
- Gervás, P. (2013). Propp's Morphology of the Folk Tale as a Grammar for Generation
- propper: ~/repos/propper
- propper-narrative: ~/repos/propper-narrative (interactive fiction implementation)
