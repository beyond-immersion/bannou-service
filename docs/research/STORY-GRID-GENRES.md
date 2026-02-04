# Story Grid Content Genres - Technical Summary

> **Source**: Story Grid genre guides (Action, Crime, Horror, Love, Thriller, War, Western, Society, Performance, Status, Morality, Worldview)
> **Purpose**: Genre-specific requirements (Four Core, Conventions, Obligatory Moments, Subgenres)
> **Implementation Relevance**: Critical - provides complete genre templates for storyline generation

## Overview

Story Grid defines 12 Content Genres organized into two categories:
- **External Genres** (9): Conflict from forces outside the protagonist
- **Internal Genres** (3): Conflict from within the protagonist

Every complete story typically combines an external genre with an internal genre.

## Genre Master Table

| Genre | Core Need | Core Value | Core Emotion | Core Event |
|-------|-----------|------------|--------------|------------|
| **EXTERNAL** |
| Action | Survival | Death → Life | Excitement | Hero at Mercy of Villain |
| War | Safety | Dishonor → Honor | Intrigue | Big Battle |
| Horror | Safety | Damnation → Life | Fear | Victim at Mercy of Monster |
| Crime | Safety | Injustice → Justice | Intrigue | Exposure of Criminal |
| Thriller | Safety | Damnation → Life | Excitement | Hero at Mercy of Villain |
| Western | Individual Sovereignty | Subjugation → Freedom | Intrigue | Big Showdown |
| Love | Connection | Hate → Love | Romance | Proof of Love |
| Performance | Esteem | Shame → Respect | Triumph | Big Performance |
| Society | Recognition | Impotence → Power | Intrigue/Triumph | Revolution |
| **INTERNAL** |
| Status | Respect | Failure → Success | Admiration/Pity | Big Choice |
| Morality | Self-Transcendence | Selfishness → Altruism | Satisfaction/Contempt | Big Choice |
| Worldview | Self-Actualization | Ignorance → Wisdom | Satisfaction/Pity | Cognitive Shift |

## Implementation Data Structures

### Core Enumerations

```csharp
public enum Genre
{
    // External
    Action,
    War,
    Horror,
    Crime,
    Thriller,
    Western,
    Love,
    Performance,
    Society,
    // Internal
    Status,
    Morality,
    Worldview
}

public enum CoreNeed
{
    Survival,
    Safety,
    IndividualSovereignty,
    Connection,
    Esteem,
    Recognition,
    Respect,
    SelfTranscendence,
    SelfActualization
}

public enum CoreEmotion
{
    Excitement,
    Fear,
    Intrigue,
    Romance,
    Triumph,
    Admiration,
    Pity,
    Satisfaction,
    Contempt,
    RighteousIndignation
}

public enum CoreEvent
{
    HeroAtMercyOfVillain,     // Action, Thriller
    BigBattle,                 // War
    VictimAtMercyOfMonster,   // Horror
    ExposureOfCriminal,       // Crime
    BigShowdown,              // Western
    ProofOfLove,              // Love
    BigPerformance,           // Performance
    Revolution,               // Society
    BigChoice,                // Status, Morality
    CognitiveShift            // Worldview
}
```

### Genre Specification Structure

```csharp
public class GenreSpecification
{
    public Genre Genre { get; set; }
    public bool IsExternal { get; set; }
    public CoreNeed CoreNeed { get; set; }
    public ValueSpectrum CoreValue { get; set; }
    public CoreEmotion[] CoreEmotions { get; set; }
    public CoreEvent CoreEvent { get; set; }
    public string ControllingIdea { get; set; }
    public string UnderlyingQuestion { get; set; }
    public List<string> Conventions { get; set; }
    public List<string> ObligatoryMoments { get; set; }
    public List<Subgenre> Subgenres { get; set; }
}

public class ValueSpectrum
{
    public string NegativePole { get; set; }      // e.g., "Death"
    public string PositivePole { get; set; }      // e.g., "Life"
    public string NegationOfNegation { get; set; } // e.g., "Damnation" (fate worse than death)
}
```

---

## External Genre Specifications

### ACTION

**Controlling Idea**: Life is preserved when the protagonist makes a sacrifice to overpower or outwit their antagonists. Death results when the protagonist lacks courage to sacrifice.

**Underlying Question**: How do I overcome powerful external forces intent on killing me and others?

**Conventions (9)**:
1. Intense, disturbed setting
2. Dueling hierarchies (growth vs power/dominance)
3. Hero sets out on journey/faces challenge from villain
4. Villain more powerful than hero and victim
5. Victim requires hero to save them
6. Speech in praise of the villain
7. Deadline/clock
8. Set-piece sequences (show protagonist strengths/weaknesses)
9. Fast-paced, exciting plot with extreme situations

**Obligatory Moments (8)**:
1. Inciting attack or threat by villain
2. Hero sidesteps responsibility
3. Forced to leave ordinary world, hero lashes out
4. Protagonist discovers antagonist's MacGuffin
5. Hero's initial strategy fails
6. All is lost moment - hero must change approach
7. **Hero at Mercy of Villain** (Core Event)
8. Hero's sacrifice is rewarded

**Subgenres**:
- **Adventure (Person vs Nature)**: Labyrinth, Monster, Environment, Doomsday
- **Duel (Person vs Person)**: Revenge, Hunted, Machiavellian, Collision
- **Epic (Person vs State)**: Rebellion, Conspiracy, Vigilante, Savior
- **Clock (Person vs Time)**: Ransom, Countdown, Holdout, Fate

---

### WAR

**Controlling Idea**: War derives meaning from noble love and self-sacrifice of warriors. It lacks meaning when leaders corrupt warriors' sacrifices.

**Underlying Question**: How do we secure our group's survival while maintaining our humanity?

**Conventions (5)**:
1. Central character with offshoot characters embodying their traits
2. Big canvas (wide scope external OR internal landscape)
3. Overwhelming odds - protagonists substantially outnumbered
4. Clear "point of no return" moment - acceptance of death's inevitability
5. Sacrifice for kinship moment

**Obligatory Moments (8)**:
1. Inciting attack
2. Protagonists deny responsibility to respond
3. Forced to respond, protagonists lash out per power hierarchy
4. Each avatar learns antagonist's object of desire
5. Initial strategy to outmaneuver antagonist fails
6. All is lost moment - must change approach
7. **Big Battle Scene** (Core Event)
8. Protagonists rewarded with satisfaction (extrapersonal/interpersonal/intrapersonal)

**Subgenres**:
- **Pro-War**: Honor through sacrifice
- **Anti-War**: Critique of war's costs
- **Kinship**: Focus on warrior bonds

---

### HORROR

**Controlling Idea**: Life is preserved when ordinary person overpowers or outwits a monster, facing the limits of human courage. Death or damnation results when we cannot muster courage.

**Underlying Question**: How do we secure safety when victimized by a manifestation of our deepest fears?

**Conventions (6)**:
1. Conventional settings within fantastical worlds
2. Labyrinths - claustrophobic settings that conceal dangers
3. Monster cannot be reasoned with - possessed by evil
4. Perpetual discomfort - random attacks
5. Mask power of monster - progressive revelation
6. Sadomasochistic flip-flop - experience monster's power while empathizing with victims

**Obligatory Moments (6)**:
1. Inciting attack by monster
2. Non-heroic protagonist thrown out of stasis - conscious desire to save own life
3. Speech in praise of the monster
4. Protagonist becomes final victim after "kill-off" scenes
5. **Victim at Mercy of Monster** (Core Event)
6. False ending - there must be two endings

**Subgenres** (by monster nature):
- **Uncanny**: Rational/explainable monsters (psychopaths, aliens)
- **Supernatural**: Spirit world monsters (vampires, ghosts)
- **Ambiguous**: Unexplained source of evil

---

### CRIME

**Controlling Idea**: Justice prevails when protagonist overpowers/outwits antagonist to reveal truth. Tyranny reigns when perpetrator outwits investigator.

**Underlying Question**: How do you expose defectors from society's norms and punish wrongdoing?

**Conventions (5)**:
1. MacGuffin - antagonist's object of desire
2. Investigative red herrings
3. Making it personal - antagonist needs protagonist
4. Clock - limited time to act
5. Subgenre-specific conventions

**Obligatory Moments (6)**:
1. Inciting crime with victims
2. Speech in praise of the villain
3. Discovering and understanding antagonist's MacGuffin
4. Progressively complicated following of clues
5. **Exposure of Criminal** (Core Event)
6. Brought to justice or escapes justice

**Subgenres**:
- **Murder Mystery**: Master Detective, Cozy, Historical, Noir/Hardboiled, Paranormal, Police Procedural
- **Other**: Organized Crime, Caper, Courtroom, Newsroom, Espionage, Prison

---

### THRILLER

**Controlling Idea**: Life is preserved when protagonist succeeds in unleashing unique gift. Death or damnation triumphs when they fail.

**Underlying Question**: How do we deal with ever-present, often incomprehensible forces of evil?

**Conventions (4)**:
1. MacGuffin - villain's object of desire
2. Investigative red herrings
3. Making it personal - villain wants to make it painful
4. Clock - limited time to act

**Obligatory Moments (5)**:
1. Inciting crime indicative of master villain (multiple victims)
2. Speech in praise of the villain
3. Protagonist becomes the victim
4. **Hero at Mercy of Villain** (Core Event)
5. False ending - there must be two endings

**Subgenres** (by protagonist domain):
- Serial Killer, Medical, Legal, Psychological, Espionage
- Child in Jeopardy, Military, Political, Journalism, Financial
- Woman in Jeopardy, Hitchcock

---

### WESTERN/EASTERN

**Controlling Idea**: Justice prevails when uncompromising individual sacrifices for good of others. Tyranny reigns if individual is betrayed by those they defend.

**Underlying Question**: Is the autonomous individual dangerous to law and order, or necessary to protect the powerless?

**Conventions (6+)**:
1. Harsh, hostile, wide-open landscape
2. Hero, victim, and villain
3. MacGuffin - villain's object of desire
4. Hero's object is to stop villain and save victim
5. Hero operates outside the law
6. Wide power divide between hero and villain
7. Speech in praise of the villain
8. Subgenre-specific conventions

**Obligatory Moments (8)**:
1. Inciting attack by villain or environment
2. Hero sidesteps responsibility
3. Forced to leave ordinary world, hero lashes out
4. Protagonist discovers antagonist's MacGuffin
5. Hero's initial strategy fails
6. All is lost moment
7. **Big Showdown** (Core Event)
8. Hero's sacrifice rewarded (rides off or joins community)

**Subgenres**:
- **Vengeance**: Stranger comes to right a wrong
- **Transition**: Hero moves from insider to exile
- **Professional**: Mercenary/law enforcement doing a job

---

### LOVE

**Controlling Idea**: Love triumphs when lovers overcome moral failings or sacrifice for one another. Love fails when lovers don't evolve beyond desire.

**Underlying Question**: How do we navigate love's emotional minefield?

**Conventions (8)**:
1. Triangle - rival or personal ethic
2. Helpers and harmers
3. Ordered and chaotic approaches (opposites attract)
4. External need driving actions
5. Opposing forces (societal or internal)
6. Secrets (four types: society→couple, couple→society, between lovers, self-deception)
7. Rituals of intimacy
8. Moral weight - inability to love = moral failing

**Obligatory Moments (6)**:
1. Lovers meet
2. First kiss or intimate connection
3. Confession of love
4. Lovers break up
5. **Proof of Love** (Core Event) - sacrifice without promise of reward
6. Lovers reunite (or not)

**Subgenres** (by psychological driver):
- **Obsession** (Desire): Intoxicating passion, often tragic
- **Courtship** (Commitment): Finding a mate, "happily ever after"
- **Marriage** (Intimacy): Committed relationship at crossroads

---

### PERFORMANCE

**Controlling Idea**: We gain respect when we commit to expressing gifts unconditionally. Shame results when we hold back for fear.

**Underlying Question**: Will protagonist express unique gifts despite difficulties?

**Conventions (6)**:
1. Strong mentor figure
2. Training - practice to gain/recover skills
3. Explicit all is lost moment
4. Mentor recovers moral compass or betrays protagonist
5. Wide power divide between antagonist and protagonist
6. Win-but-lose or lose-but-win ending

**Obligatory Moments (8)**:
1. Inciting performance opportunity
2. Protagonist sidesteps responsibility
3. Forced to perform, protagonist lashes out
4. Discovers antagonist's object of desire
5. Initial strategy fails
6. All is lost moment
7. **Big Performance** (Core Event)
8. Rewarded at one or more levels (external, interpersonal, internal)

**Subgenres** (by domain):
- **Sports**: Competition, athletic achievement
- **Business/Profession**: Career, deal-making
- **Art**: Visual arts, creation
- **Performing Arts**: Music, theater, dance

---

### SOCIETY

**Controlling Idea**: We gain power when we expose hypocrisy of tyrants. Tyrants beat back revolutions by co-opting underclass leaders.

**Underlying Question**: What do we do in the face of tyranny?

**Conventions (6)**:
1. Central character with offshoot characters
2. Big canvas (wide scope or internal landscape)
3. Clear revolutionary point of no return
4. Vanquished doomed to exile
5. Large power divide
6. Paradoxical win-but-lose or lose-but-win ending

**Obligatory Moments (8)**:
1. Threat/opportunity for reigning power incites protagonist
2. Protagonist denies responsibility
3. Forced to respond, lashes out per power hierarchy
4. Each avatar learns antagonist's object of desire
5. Initial strategy fails
6. All is lost moment
7. **Revolution** (Core Event) - power changes hands
8. Protagonist rewarded at some level

**Subgenres**:
- **Single Protagonist** (Arch-plot): Individual vs tyranny
- **Multiple Protagonists** (Mini-plot): Group vs tyranny
- **By Relationship**: First Party (family), Second Party (tribe), Third Party (mass group)

---

## Internal Genre Specifications

### STATUS

**Controlling Idea**: Staying true to one's values defines success. Selling out results in failure.

**Underlying Question**: Will protagonist achieve society's success or embrace their own definition?

**Conventions (6)**:
1. Strong mentor figure
2. Big social problem as subtext
3. Shapeshifters as hypocrites
4. Herald/Threshold Guardian is a fellow striver who sold out
5. Clear point of no return
6. Paradoxical win-but-lose or lose-but-win ending

**Obligatory Moments (8)**:
1. Inciting opportunity or challenge
2. Protagonist leaves home to seek fortune
3. Forced to adapt, relies on old habits, suffers humiliation
4. Learns external antagonist's object of desire
5. Initial strategy fails
6. All is lost - must change definition of success
7. **Big Choice** (Core Event) - attain status or reject world
8. Saved or lost based on choice

**Subgenres** (by protagonist strength/outcome):
- **Pathetic**: Weak protagonist fails
- **Tragic**: Strong protagonist's mistake causes failure
- **Sentimental**: Weak protagonist succeeds against odds
- **Admiration**: Principled protagonist rises without compromise

---

### MORALITY

**Controlling Idea**: We transcend selfishness when we share gifts for benefit of others (prescriptive). We are damned when we selfishly withhold gifts (cautionary).

**Underlying Question**: When given chance to behave selfishly or altruistically, which will protagonist choose?

**Conventions (5)**:
1. Despicable protagonist begins at their worst
2. Spiritual mentor or sidekick
3. Seemingly impossible external conflict
4. Ghosts from past torment protagonist
5. Aid from unexpected sources

**Obligatory Moments (5)**:
1. Shock upsets hibernating authentic self
2. Protagonist expresses inner darkness, refuses call to change
3. All is lost - recovers inner moral code or chooses immoral path
4. Actively sacrifices self (prescriptive) or remains selfish (cautionary)
5. Faces literal/metaphorical death - gains self-respect or loses meaning

**Subgenres**:
- **Punitive**: Unsympathetic protagonist suffers deserved misfortune
- **Redemption**: Protagonist begins wrong, ends by making better choice
- **Testing (Triumph)**: Strong protagonist wavers but remains steadfast
- **Testing (Surrender)**: Strong protagonist suffers loss, resigns to weakness

---

### WORLDVIEW

**Controlling Idea**: We gain wisdom when we share gifts with imperfect world (prescriptive). We descend into meaninglessness when we fail to mature past black/white view (cautionary).

**Underlying Question**: How can we solve problems we don't yet understand?

**Conventions (5)**:
1. Mentor figure
2. Big social problem as subtext
3. Shapeshifters as hypocrites
4. Clear point of no return
5. Paradoxical win-but-lose or lose-but-win ending

**Obligatory Moments (8)**:
1. Inciting opportunity or challenge
2. Protagonist denies responsibility
3. Forced, protagonist lashes out against requirement to change
4. Learns antagonist's external object of desire
5. Initial strategy fails
6. All is lost - must vary from black/white worldview
7. Climax - gifts expressed as acceptance of imperfect world
8. Loss of ignorance rewarded with deeper understanding

**Subgenres** (by transformation type):
- **Maturation**: Naivete → Sophistication
- **Disillusionment**: Belief → Loss of faith
- **Education**: Meaninglessness → Meaning
- **Revelation**: Ignorance → Wisdom (missing information discovered)

---

## Common Patterns Across Genres

### Universal Obligatory Moment Pattern

Most genres share this approximate structure:

1. **Inciting Incident** (attack, opportunity, or threat)
2. **Initial Denial** (protagonist sidesteps responsibility)
3. **Forced Engagement** (protagonist lashes out)
4. **Discovery** (learns antagonist's object of desire)
5. **Failed Strategy** (initial approach fails)
6. **All Is Lost** (must change approach)
7. **Core Event** (genre-specific climax)
8. **Resolution** (reward or punishment)

### Genre Validation Rules

```csharp
public class GenreValidator
{
    public bool HasRequiredConventions(Story story, Genre genre)
    {
        var spec = GetGenreSpecification(genre);
        return spec.Conventions.All(conv =>
            story.HasConvention(conv));
    }

    public bool HasObligatoryMoments(Story story, Genre genre)
    {
        var spec = GetGenreSpecification(genre);
        return spec.ObligatoryMoments.All(moment =>
            story.HasMoment(moment));
    }

    public bool HasCoreEvent(Story story, Genre genre)
    {
        var spec = GetGenreSpecification(genre);
        return story.Climax.Type == spec.CoreEvent;
    }
}
```

## Integration with STORYLINE_COMPOSER

Genre specifications provide:

1. **Template selection** based on desired emotional response
2. **Required conventions** as storyline constraints
3. **Obligatory moments** as required beat targets
4. **Core event types** for climax planning
5. **Subgenre variation** for story arc customization
6. **Underlying questions** for thematic coherence

Each genre's Four Core elements (Need, Value, Emotion, Event) directly inform narrative state dimensions for GOAP planning.
