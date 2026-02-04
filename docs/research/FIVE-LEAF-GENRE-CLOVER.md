# Five Leaf Genre Clover - Technical Summary

> **Source**: Story Grid "Five Leaf Genre Clover" framework
> **Purpose**: Complete genre classification system with five orthogonal dimensions
> **Implementation Relevance**: High - provides enumerable dimensions for comprehensive story classification

## Overview

Story Grid defines genre as a **five-dimensional classification system**. Every story can be classified along all five dimensions (the "Five Leaf Clover"):

1. **Content Genre** - The emotional experience (12 types)
2. **Structure Genre** - Who/what changes (3 types)
3. **Reality Genre** - How real the world is (4 types)
4. **Time Genre** - Duration and pacing (3 types)
5. **Style Genre** - Tone and medium (2 categories + 8 mediums)

## Implementation Data Structures

```csharp
public class FiveLeafGenreClassification
{
    // Leaf 1: Content Genre (see STORY-GRID-GENRES.md for full details)
    public Genre PrimaryContentGenre { get; set; }     // External genre
    public Genre? SecondaryContentGenre { get; set; }  // Internal genre (optional)
    public Subgenre? Subgenre { get; set; }

    // Leaf 2: Structure Genre
    public StructureGenre Structure { get; set; }

    // Leaf 3: Reality Genre
    public RealityGenre Reality { get; set; }
    public RealitySubgenre? RealitySubgenre { get; set; }

    // Leaf 4: Time Genre
    public TimeGenre TimeForm { get; set; }
    public TimeSpan? StoryDuration { get; set; }  // In-story time span

    // Leaf 5: Style Genre
    public StyleCategory Style { get; set; }
    public StyleMedium Medium { get; set; }
}
```

---

## Leaf 1: Content Genre

Covered in detail in [STORY-GRID-GENRES.md](STORY-GRID-GENRES.md).

**Summary**: 12 content genres organized into External (9) and Internal (3):

| External | Internal |
|----------|----------|
| Action, War, Horror, Crime, Thriller, Western, Love, Performance, Society | Status, Morality, Worldview |

Every complete story typically combines one external genre with one internal genre.

---

## Leaf 2: Structure Genre

**Question**: Who or what does the change of the story affect?

```csharp
public enum StructureGenre
{
    ArchPlot,   // Single protagonist change
    MiniPlot,   // System/multiple protagonist change
    AntiPlot    // No coherent change (experimental)
}
```

### Arch-Plot

- **Focus**: Single active protagonist pursuing an object of desire
- **Antagonism**: Primarily external forces
- **Ending**: "Closed" - absolute and irreversible change
- **Examples**: *The Firm*, *Pride and Prejudice*, *The Hobbit*

**Characteristics**:
- Clear protagonist with clear goal
- Cause-and-effect chain of events
- Rising action toward single climax
- Definitive resolution

### Mini-Plot

- **Focus**: System change shown through multiple POV characters
- **Structure**: Each character follows their own mini arch-plot
- **Effect**: Shows how interconnected nodes transform a whole system
- **Examples**: *Ragtime*, *Game of Thrones*, *Lord of the Rings*

**Characteristics**:
- Multiple protagonists as "nodes" in a system
- Parallel storylines that intersect
- Emergent change at system level
- May have multiple climaxes

### Anti-Plot

- **Focus**: Rebellion against formal storytelling structure
- **Causality**: Absent or inverted
- **Change**: No coherent transformation
- **Examples**: *Waiting for Godot*, *No Exit*

**Characteristics**:
- Deliberately breaks storytelling rules
- Events may be random or circular
- Characters don't grow or change
- World makes no sense by design

---

## Leaf 3: Reality Genre

**Question**: How much must the reader suspend disbelief?

```csharp
public enum RealityGenre
{
    Absurdism,    // Chaos end - anything can happen
    Factualism,   // Complexity - based on historical record
    Realism,      // Complexity - could happen in real life
    Fantasy       // Order end - requires systematic rules
}

public enum RealitySubgenre
{
    // Fantasy subtypes
    FantasyHuman,     // Anthropomorphic, highlighting human patterns
    FantasyMagical,   // Magic systems with mastery requirements
    ScienceFiction,   // Imagined technology available to all

    // Fantasy > Magical subcategories
    EpicFantasy,
    PortalFantasy,
    DarkFantasy,
    GrimdarkFantasy,
    UrbanFantasy,

    // Fantasy > Science Fiction subcategories
    AlternateHistory,
    Cyberpunk,
    HardScience,
    MilitarySF,
    PostApocalyptic,
    RomanticSF,
    SoftScience,
    SpaceOpera
}
```

### Chaos → Complexity → Order Spectrum

| Genre | Position | Certainty Level |
|-------|----------|-----------------|
| Absurdism | Chaos | Anything can happen anytime for any/no reason |
| Factualism | Complexity | Based on real historical events |
| Realism | Complexity | Could happen in real life (imagined) |
| Fantasy | Order | Systematic rules that must be understood |

### Fantasy Subtypes

**Human Fantasy**: Fantastical elements highlighting human behavior patterns
- Example: *Animal Farm* (anthropomorphic), *Groundhog Day* (fantastical premise)

**Magical Fantasy**: Magic laws mastered by certain characters
- Nostalgic, often medieval settings
- Magic requires earning/training
- Subtypes: Epic, Portal, Dark, Grimdark, Urban

**Science Fiction**: Imagined technologies distributed to all
- Futuristic, utopian striving
- Technology accessible without special requirements
- Subtypes: Alternate History, Cyberpunk, Hard/Soft Science, Military, Post-Apocalyptic, Space Opera

---

## Leaf 4: Time Genre

**Question**: How long does the story take to consume, and how long does it last in-story?

```csharp
public enum TimeGenre
{
    ShortForm,   // Short stories, short films, individual scenes
    MediumForm,  // Novellas, TV episodes, one-act plays
    LongForm     // Novels, feature films, multi-act plays
}

public class TimeSpecification
{
    public TimeGenre Form { get; set; }
    public TimeSpan? ConsumptionDuration { get; set; }  // Reading/viewing time
    public TimeSpan? StoryDuration { get; set; }        // In-story elapsed time
}
```

### Two Dimensions of Time

1. **Reader/Viewer Experience** (consumption time):
   - Short Form: Minutes to an hour
   - Medium Form: One to several hours
   - Long Form: Many hours to days

2. **In-Story Duration** (simulation time):
   - *Ulysses* (265,000 words) covers a single day
   - *A Rose for Emily* (3,720 words) spans generations

**Implementation Note**: These dimensions are independent. A short story can span centuries; a long novel can cover minutes.

---

## Leaf 5: Style Genre

**Question**: What tone and form does the story take?

```csharp
public enum StyleCategory
{
    Drama,   // Solemnity; truth and pain experienced directly
    Comedy   // Humor; jokes to avoid truthful emotion/pain
}

public enum StyleMedium
{
    Documentary,  // Fact-based or mockumentary tone
    Musical,      // Characters break into song
    Dance,        // Movement-based (ballet, martial arts)
    Literary,     // "High art" sensibility
    Theatrical,   // Qualities of live theater
    Cinematic,    // Qualities of film
    Epistolary,   // Letters/written communication
    Animation     // Series of images simulating movement
}

public enum LiterarySubtype
{
    Poetry,       // Metrical verse
    Minimalism,   // Sparse, short fiction
    Meta,         // Self-referential stories about stories
    PostModern    // Fragmented, subversive of structure
}
```

### Drama vs Comedy

| Drama | Comedy |
|-------|--------|
| Solemn tone | Funny tone |
| Characters face reality directly | Characters use humor to deflect |
| Pain experienced truthfully | Pain avoided through jokes |
| Emotions are fulfilling | Emotions are deflected |

### Medium Types

| Medium | Characteristics | Examples |
|--------|-----------------|----------|
| Documentary | Fact-based, can be "mockumentary" | *Battle of Algiers*, *Spinal Tap* |
| Musical | Characters sing | *Hamilton*, *Hairspray* |
| Dance | Movement-driven | *Swan Lake*, *Crouching Tiger* |
| Literary | "High art" sensibility | *Ulysses*, *Pale Fire* |
| Theatrical | Theater qualities | *Our Town*, *Midsummer Night's Dream* |
| Cinematic | Film qualities | *The Matrix*, *A Fish Called Wanda* |
| Epistolary | Letter/document format | *Frankenstein*, *Dracula* |
| Animation | Simulated movement images | *The Incredibles*, *Iron Giant* |

---

## Complete Classification Examples

### Example 1: *The Silence of the Lambs*
```csharp
new FiveLeafGenreClassification
{
    PrimaryContentGenre = Genre.Thriller,
    SecondaryContentGenre = Genre.Worldview,
    Subgenre = Subgenre.SerialKiller,
    Structure = StructureGenre.ArchPlot,
    Reality = RealityGenre.Realism,
    TimeForm = TimeGenre.LongForm,
    Style = StyleCategory.Drama,
    Medium = StyleMedium.Cinematic
}
```

### Example 2: *Lord of the Rings*
```csharp
new FiveLeafGenreClassification
{
    PrimaryContentGenre = Genre.Action,
    SecondaryContentGenre = Genre.Morality,
    Subgenre = Subgenre.Savior,
    Structure = StructureGenre.MiniPlot,
    Reality = RealityGenre.Fantasy,
    RealitySubgenre = RealitySubgenre.EpicFantasy,
    TimeForm = TimeGenre.LongForm,
    Style = StyleCategory.Drama,
    Medium = StyleMedium.Literary
}
```

### Example 3: *Groundhog Day*
```csharp
new FiveLeafGenreClassification
{
    PrimaryContentGenre = Genre.Love,
    SecondaryContentGenre = Genre.Worldview,
    Subgenre = Subgenre.Courtship,
    Structure = StructureGenre.ArchPlot,
    Reality = RealityGenre.Fantasy,
    RealitySubgenre = RealitySubgenre.FantasyHuman,
    TimeForm = TimeGenre.LongForm,
    Style = StyleCategory.Comedy,
    Medium = StyleMedium.Cinematic
}
```

---

## Integration with STORYLINE_COMPOSER

The Five Leaf Genre Clover provides:

1. **Content Genre** → Core emotional targets and obligatory moments
2. **Structure Genre** → Protagonist configuration (single vs multiple)
3. **Reality Genre** → World rules and suspension of disbelief constraints
4. **Time Genre** → Pacing and duration parameters
5. **Style Genre** → Tone and presentation mode

### Story Configuration Schema

```csharp
public class StoryConfiguration
{
    // Genre specification
    public FiveLeafGenreClassification Genres { get; set; }

    // Derived from Content Genre
    public CoreNeed CoreNeed { get; set; }
    public ValueSpectrum CoreValue { get; set; }
    public CoreEmotion TargetEmotion { get; set; }
    public CoreEvent RequiredClimax { get; set; }
    public List<string> Conventions { get; set; }
    public List<string> ObligatoryMoments { get; set; }

    // Derived from Structure Genre
    public int ProtagonistCount { get; set; }
    public bool RequiresCausalChain { get; set; }
    public bool RequiresClosedEnding { get; set; }

    // Derived from Reality Genre
    public bool RequiresMagicSystem { get; set; }
    public bool RequiresTechnologySystem { get; set; }
    public double SuspensionOfDisbeliefLevel { get; set; }  // 0-1

    // Derived from Time Genre
    public TimeSpan TargetDuration { get; set; }
    public TimeSpan InStoryTimespan { get; set; }

    // Derived from Style Genre
    public bool IsComedy { get; set; }
    public bool AllowsMusicalSequences { get; set; }
}
```

The complete genre classification enables precise story template selection and validation of generated storylines against genre expectations.
