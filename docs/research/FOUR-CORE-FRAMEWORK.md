# Four Core Framework - Technical Summary

> **Source**: Shawn Coyne, "The Four Core Framework" (Story Grid Publishing, 2020)
> **Purpose**: Genre classification system mapping human needs to story structures
> **Implementation Relevance**: High - provides enumerable dimensions for narrative state and genre detection

## Core Concept

Every story genre is defined by four interdependent elements that together produce reader catharsis:

1. **Core Need** - The fundamental human need driving protagonist and antagonist
2. **Core Life Value** - A spectrum measuring story progress (e.g., death↔life)
3. **Core Emotion** - The target emotional response in the audience
4. **Core Event** - The climactic scene integrating all three elements

## Implementation Data Structures

### Core Needs Enumeration

Based on Maslow's hierarchy, these are the enumerable needs:

```
Survival        → Physical existence preservation
Safety          → Protection from threats (physical, psychological, societal)
IndividualSovereignty → Personal freedom and autonomy
Connection      → Intimate bonds with others
Esteem          → Recognition from self and others
Recognition     → Acknowledgment by groups/society
Respect         → External validation of worth
SelfActualization → Expressing unique gifts
SelfTranscendence → Contributing beyond self-interest
```

### Core Life Values (Spectrums)

Each spectrum has a positive pole, negative pole, and "negation of negation" (fate worse than the negative):

| Spectrum | Positive | Negative | Negation of Negation |
|----------|----------|----------|----------------------|
| Life/Death | Life | Death | Damnation (fate worse than death) |
| Honor/Dishonor | Honor + Victory | Dishonor | Victory with dishonor misrepresented as honor |
| Justice/Injustice | Justice | Injustice | Tyranny |
| Freedom/Subjugation | Freedom | Subjugation | Slavery with illusion of freedom |
| Love/Hate | Love | Hate | Hate masquerading as love |
| Respect/Shame | Respect | Shame | Hollow praise/false respect |
| Power/Impotence | Power | Impotence | Power through co-option |
| Success/Failure | Success | Failure | Selling out |
| Altruism/Selfishness | Altruism | Selfishness | Selfishness disguised as altruism |
| Wisdom/Ignorance | Wisdom | Ignorance | Willful ignorance/denial |

### Core Emotions Enumeration

```
Excitement      → Thrill from survival conflict
Fear            → Response to existential threat
Intrigue        → "Penny drop" satisfaction when puzzle resolves
Romance         → Connections falling into place as they should
Triumph         → Victory through gift expression
Admiration      → Respect for moral integrity
Pity            → Sorrow for wasted potential
Satisfaction    → Fulfillment from transcendence/growth
Contempt        → Disgust at selfish choices
RighteousIndignation → Anger at injustice
```

### Core Events Enumeration

```
HeroAtMercyOfVillain     → Action, Thriller
BigBattle                → War
VictimAtMercyOfMonster   → Horror
ExposureOfCriminal       → Crime
BigShowdown              → Western
ProofOfLove              → Love
BigPerformance           → Performance
Revolution               → Society
BigChoice                → Status, Morality
CognitiveGrowthOrDegeneration → Worldview
```

## Genre Specifications (Complete Mapping)

### External Genres (Physical Conflict)

| Genre | Core Need | Life Value | Emotion | Core Event |
|-------|-----------|------------|---------|------------|
| **Action** | Survival | Death↔Life | Excitement | Hero at Mercy of Villain |
| **War** | Safety | Dishonor↔Honor | Intrigue | Big Battle |
| **Horror** | Safety | Damnation↔Life | Fear | Victim at Mercy of Monster |
| **Crime** | Safety | Injustice↔Justice | Intrigue | Exposure of Criminal |
| **Thriller** | Safety | Damnation↔Life | Excitement | Hero at Mercy of Villain |
| **Western** | Individual Sovereignty | Subjugation↔Freedom | Intrigue | Big Showdown |

### Internal Genres (Psychological/Relational Conflict)

| Genre | Core Need | Life Value | Emotion | Core Event |
|-------|-----------|------------|---------|------------|
| **Love** | Connection | Hate↔Love | Romance | Proof of Love |
| **Performance** | Esteem | Shame↔Respect | Triumph | Big Performance |
| **Society** | Recognition | Impotence↔Power | Intrigue/Triumph/Indignation | Revolution |
| **Status** | Respect | Failure↔Success | Admiration/Pity | Big Choice |
| **Morality** | Self-Transcendence | Selfishness↔Altruism | Satisfaction/Contempt | Big Choice |
| **Worldview** | Self-Actualization | Ignorance↔Wisdom | Satisfaction/Pity | Cognitive Growth |

## Subgenre Breakdowns

### Horror Subgenres (by monster nature)
- **Uncanny**: Explainable, rational monsters
- **Supernatural**: Metaphysical explanation
- **Ambiguous**: Unexplained mystery

### War Subgenres (by thematic stance)
- **Pro-War**: Honor through sacrifice
- **Anti-War**: Critique of war's costs
- **Kinship**: Focus on warrior bonds

### Love Subgenres (by relationship stage)
- **Obsession**: Driven by desire
- **Courtship**: Driven by commitment need
- **Marriage**: Driven by intimacy need

### Western Subgenres (by protagonist arc)
- **Vengeance**: Outsider rights a wrong
- **Transition**: Member becomes exile
- **Professional**: Operates outside law throughout

### Status Subgenres (by protagonist strength/outcome)
- **Pathetic**: Weak protagonist fails
- **Tragic**: Strong protagonist's mistakes cause failure
- **Sentimental**: Weak protagonist succeeds
- **Admiration**: Strong protagonist refuses compromise, succeeds

### Morality Subgenres (by moral arc)
- **Punitive**: Villain protagonist punished for selfishness
- **Redemption**: Lost protagonist reclaims gift, achieves altruism
- **Testing**: Protagonist wavers before finding right path

### Worldview Subgenres (by belief transformation)
- **Disillusionment**: Unquestioned belief → loss of faith
- **Education**: Meaninglessness → meaning
- **Maturation**: Black/white view → sophisticated gray
- **Revelation**: Missing information discovered → wise decision

## Key Concepts for Implementation

### Luminary Agent vs Shadow Agent

- **Luminary Agent**: Protagonist who must express gifts to resolve story
- **Shadow Agent**: Antagonist representing opposing force
- **Agency-Deprived Victim**: Character whom luminary must save

### Cognitive Frame Breaking

The essence of story transformation: protagonist realizes their current worldview cannot solve the problem and must dismantle/rebuild it. This is distinct from "thinking outside the box" - it requires recognizing the frame itself is inadequate.

### Controlling Idea Pattern

Every genre has a positive and negative controlling idea:
- **Positive**: [Value achieved] when [protagonist action]
- **Negative**: [Value denied] when [protagonist failure]

Examples:
- Action+: "Meaningful life prevails when the luminary overpowers or outwits the villain"
- Action-: "Death results when the protagonist fails to overpower or outwit the villain"
- Love+: "Love triumphs when lovers overcome moral failings and sacrifice for each other"
- Love-: "Love fails when lovers don't evolve beyond shallow desire"

### Genre Boundaries

Action and Worldview form the boundaries of Story:
- **Action**: "On-the-ground" external problem solving
- **Worldview**: "In the clouds" internal transformation

Every story contains both elements - external action and internal worldview shift.

## Implementation Recommendations

### NarrativeState Dimensions

Map to normalized 0-1 floats based on Life Value spectrums:

```csharp
public class NarrativeState
{
    // Life Value spectrums (0 = negative pole, 1 = positive pole)
    public double LifeDeath { get; set; }      // Survival/physical stakes
    public double HonorDishonor { get; set; }  // Collective/moral stakes
    public double JusticeInjustice { get; set; }
    public double FreedomSubjugation { get; set; }
    public double LoveHate { get; set; }
    public double RespectShame { get; set; }
    public double PowerImpotence { get; set; }
    public double SuccessFailure { get; set; }
    public double AltruismSelfishness { get; set; }
    public double WisdomIgnorance { get; set; }
}
```

### Genre Detection

A story's genre can be inferred from which Life Value spectrum has the most variance/stakes:
- High LifeDeath variance + Survival need → Action
- High JusticeInjustice variance + Safety need → Crime
- High LoveHate variance + Connection need → Love

### Core Event Recognition

Core Events are identifiable by structural position (climax) and content:
- All elements at maximum intensity
- Core Need most in jeopardy
- Life Value at extreme of spectrum
- Core Emotion at peak

### Subgenre Selection

Subgenres provide templates for:
- Monster design (Horror)
- Protagonist arc shape (Western, Status)
- Relationship stage focus (Love)
- Moral trajectory (Morality)
- Belief transformation type (Worldview)

## Integration with STORYLINE_COMPOSER

The Four Core Framework provides:

1. **Genre classification** for storyline templates (12 primary genres)
2. **Life Value spectrums** for NarrativeState dimensions
3. **Core Event types** for climax scene planning
4. **Subgenre templates** for story arc variation
5. **Controlling ideas** for thematic validation
6. **Character roles** (luminary, shadow, victim) for cast requirements

The framework's Core Need hierarchy (Maslow-based) aligns with the NarrativeState dimensions proposed in STORYLINE_COMPOSER, enabling GOAP planning toward specific emotional/dramatic targets.
