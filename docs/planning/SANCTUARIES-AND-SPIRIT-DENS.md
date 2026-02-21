# Sanctuaries and Spirit Dens: Sacred Geography of the Living World

> **Status**: Vision Document (design analysis, no implementation)
> **Priority**: High (Living Game Worlds -- North Star #1, Emergent Over Authored -- North Star #5, Content Flywheel -- North Star #2)
> **Related**: `docs/planning/PREDATOR-ECOLOGY-PATTERNS.md`, `docs/planning/MEMENTO-INVENTORIES.md`, `docs/planning/DEATH-AND-PLOT-ARMOR.md`, `docs/planning/ACTOR-BOUND-ENTITIES.md`
> **Services**: Environment, Location, Divine, Seed, Actor, Character, Status, Item, Inventory, Collection, Ethology, Worldstate, Transit
> **Metaphysics**: Logos/Pneuma framework (VISION.md), Memento system (MEMENTO-INVENTORIES.md), Actor-Bound Entity pattern (ACTOR-BOUND-ENTITIES.md)

---

## Executive Summary

Two complementary concepts create sacred geography in the Arcadia game world: **Sanctuaries** (divine non-aggression zones where supernatural peace overrides predator ecology) and **Spirit Dens** (leyline convergence points where accumulated species-logos crystallizes into supernatural exemplar animals surrounded by their kin). Both emerge from the interaction of existing systems -- the predator ecology rules, the memento inventory system, the divine economy, and the leyline/pneuma framework -- requiring zero new services.

Sanctuaries and spirit dens are mechanistically distinct but complementary. Sanctuaries are about **peace** (divine origin, aggression suppression, multi-species coexistence). Spirit dens are about **essence** (natural origin, species concentration, logos crystallization). Where they converge -- a divine sanctuary at a leyline point -- the rarest and most awe-inspiring locations in the game world emerge.

The critical design insight: **these locations gain their power from the contrast with the surrounding world**. The predator ecology research (PREDATOR-ECOLOGY-PATTERNS.md) establishes that the world is governed by intraguild predation, dominance hierarchies, kleptoparasitism, territorial aggression, and the landscape of fear. Sanctuaries and spirit dens are the exceptions that prove the rule. Their rarity and the danger of reaching them is what makes them meaningful.

---

## Part 1: Sanctuaries

### What a Sanctuary Is

A sanctuary is a location where a **divine imprint** in the local pneuma field suppresses aggressive intent, creating a genuine non-aggression zone. Predators and prey coexist peacefully. Wolves drink beside deer. Eagles perch above rabbits. Every creature shows every other creature a respect that exists nowhere else in the natural world.

This is not a behavioral suggestion or a strong modifier. Within the sanctuary core, **violence is impossible**. The pneuma resonance absorbs the spiritual energy required for aggressive intent before it can form into action. A wolf inside a sanctuary still sees the deer, still feels hunger, still has every predator instinct encoded in its ethology -- but the action of lunging, of attacking, never reaches the threshold of execution. The intent dissipates like sound in fog.

This absolute nature is what makes sanctuaries sacred. In a world where the predator ecology rules are otherwise in full force -- where cheetahs lose 15% of kills to kleptoparasites, where wolves kill coyotes on sight, where the landscape of fear shapes every creature's movement -- a place where none of that applies is genuinely miraculous. Players should feel the wrongness of it immediately, and then the wonder.

### How Sanctuaries Form

#### Origin: Divine Acts

Sanctuaries are created when a god performs a significant act that deposits energy into the local pneuma field. The act must be substantial enough to leave a **standing resonance** -- a persistent pattern in the spiritual physics of the area.

Examples of originating acts:
- A god creates an oasis in a desert (Silvanus/Forest manifesting life where none existed)
- A god heals a dying forest after a catastrophic fire (restoration miracle)
- A god intervenes to stop a massacre (Moira/Fate redirecting the course of a battle)
- A god blesses a spring, giving its water healing properties (sacred water source)
- A god manifests physically and the location where their avatar stood retains the imprint

The divine act costs divinity (the god's finite resource from the Divine service economy). The magnitude of the act determines the initial strength and radius of the sanctuary. Larger miracles create larger sanctuaries with slower decay.

#### Maintenance: Three Paths

The divine imprint is not permanent by default. It decays over time (centuries, not years) unless reinforced:

| Maintenance Path | Mechanism | Durability |
|-----------------|-----------|------------|
| **Divine renewal** | The originating god (or another) periodically invests divinity to refresh the imprint | Indefinite while god is active |
| **Shrine keeper consecration** | Shrine keepers consume accumulated mementos at the site, transmuting them into Status effects that reinforce the resonance | Self-sustaining if keepers continue |
| **Worship/pilgrimage** | Ongoing reverence at the site generates emotional mementos of peace and devotion, which naturally reinforce the resonance at a low level | Slow but persistent |
| **None** | The imprint slowly fades | Centuries to full decay |

The **hybrid model** is the most durable: a god created the sanctuary, shrine keepers maintain it with ritual consecration, and ongoing pilgrimage generates the raw memento material for that consecration. A sanctuary maintained by all three paths simultaneously could persist for millennia.

#### Decay and Failure

When maintenance ceases:

```
Phase 1: FULL STRENGTH (0-50% decay)
  Sanctuary functions normally. Core zone is absolute.
  Edge gradient may narrow slightly.

Phase 2: WEAKENING (50-80% decay)
  Edge gradient recedes noticeably. Core zone shrinks.
  Creatures at the edges begin exhibiting normal behavior.
  First predator incidents at the former periphery.
  NPCs and visitors notice: "The peace feels thinner here."

Phase 3: FAILING (80-95% decay)
  Core zone is small -- perhaps just the immediate vicinity of
  the originating miracle (the spring, the tree, the stone).
  Most of the former sanctuary operates under normal ecology.
  Predators hunt in what used to be sacred ground.
  The accumulated mementos of peaceful coexistence are being
  overwritten by mementos of violence.

Phase 4: COLLAPSED (95-100% decay)
  No sanctuary effect remains. Normal ecology resumes.
  All those animals that coexisted peacefully are now
  predator and prey in the same space.
  The transition generates intense combat and death mementos.
  A god-actor perceiving this may interpret it as tragedy
  and commission a storyline from the event.
```

A failing sanctuary is a natural quest hook that the content flywheel generates without authoring: the god-actor perceives the decay, commissions a quest to find someone who can restore it, and the player discovers a once-sacred place being reclaimed by the brutal ecology they've been navigating throughout the game.

### Sanctuary Spatial Structure

```
┌─────────────────────────────────────────────────────────┐
│                    NORMAL ECOLOGY                        │
│  Full predator behavior. Landscape of fear in effect.   │
│  Territorial defense, kleptoparasitism, IGP, hunting.   │
│                                                          │
│    ┌─────────────────────────────────────────────┐      │
│    │              GRADIENT ZONE                    │      │
│    │  Aggression suppression increases inward.     │      │
│    │  Predators become less motivated to hunt.     │      │
│    │  Prey animals become calmer, less vigilant.   │      │
│    │  sanctuary_suppression: 0.0 → 0.8             │      │
│    │                                               │      │
│    │    ┌─────────────────────────────────┐       │      │
│    │    │          CORE ZONE               │       │      │
│    │    │  Violence impossible.             │       │      │
│    │    │  sanctuary_suppression: 1.0       │       │      │
│    │    │  All species coexist.             │       │      │
│    │    │  The originating miracle is here. │       │      │
│    │    │  (spring, tree, stone, clearing)  │       │      │
│    │    └─────────────────────────────────┘       │      │
│    └─────────────────────────────────────────────┘      │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

The gradient zone ensures the transition is smooth (consistent with the "every boundary is a gradient" principle from PLAYER-VISION.md). A predator entering the gradient zone doesn't hit an invisible wall -- it gradually loses hunting motivation, eventually reaching a point where it enters the core voluntarily but without aggressive intent. The predator is still hungry but the hunt action never selects.

### Sanctuary Behavioral Mechanics

In ABML terms, the sanctuary modifies GOAP action costs:

```
// Environment provides sanctuary_suppression per Location (0.0 to 1.0)
// At 1.0 (core), all aggression actions have infinite cost
// At 0.5 (mid-gradient), aggression costs are doubled
// At 0.0 (outside), normal ecology applies

effective_aggression_cost = base_aggression_cost / (1.0 - sanctuary_suppression)
// As suppression approaches 1.0, cost approaches infinity

// Critically: NON-aggressive actions are unaffected
// Drinking, resting, grazing, socializing, traveling through -- all normal cost
// Predators ENTER sanctuaries voluntarily for water, rest, shelter
// They just can't hunt there
```

This means sanctuaries are naturally populated. Animals don't avoid them -- they're drawn to them because the resources (water, shelter, food for herbivores) are present without the risk. The predators come for water and rest. The prey comes because the predators aren't hunting. The multi-species congregation is an emergent result of rational behavior under modified costs, not a scripted gathering.

### What Players Experience at a Sanctuary

A player approaching a sanctuary experiences the transition through the predator ecology they've been learning throughout the game:

1. **Normal wilderness**: Wolf territories, scent marks, territorial responses, prey fleeing at predator approach. The rules from PREDATOR-ECOLOGY-PATTERNS.md in full effect.

2. **Outer gradient**: Something changes. A wolf ahead of them on the trail doesn't react to a nearby deer. A hawk lands near a rabbit and doesn't strike. The player's companion character might comment: "The animals here are... different."

3. **Inner gradient**: Predators and prey actively intermingle. A bear fishing in a stream while deer drink beside it. An eagle perched above a colony of mice. The character's spiritual perception (if developed) might reveal the pneuma resonance -- a visible shimmer or warmth in the air.

4. **Core zone**: The originating miracle is visible. A spring of unnaturally clear water. An ancient tree with luminous bark. A stone that hums with warmth. Around it, animals of every kind coexist in a tableau that contradicts everything the player has learned about how the world works.

The emotional impact depends entirely on the contrast. If the player hasn't experienced real predator ecology -- hasn't seen wolves hunt, hasn't watched hyenas steal kills, hasn't navigated through territorial aggression -- the sanctuary is just a pretty clearing. The predator ecology makes the sanctuary meaningful.

### Sanctuary Rarity

Sanctuaries must be **rare** to be meaningful. Guidelines:

- **Per realm**: 3-7 major sanctuaries (core zone > 1 Location in radius)
- **Minor sanctuaries**: Perhaps 10-20 (core zone is a single Location -- one spring, one tree, one clearing)
- **Failed/fading sanctuaries**: Equal or greater number than active ones. These are quest content.
- **Total**: A realm has fewer than 30 sanctuary-class locations out of potentially thousands of Locations

Rarity is self-enforcing through the divine economy: creating a sanctuary costs significant divinity. Gods don't spend their finite resource frivolously. Each sanctuary represents a moment where a god judged the act worth the cost.

---

## Part 2: Spirit Dens

### What a Spirit Den Is

A spirit den is a location where a **leyline convergence** provides sufficient ambient pneuma energy to crystallize accumulated species-specific logos into a supernatural manifestation: the **spirit animal**. The spirit animal is not a normal member of its species that happens to be impressive -- it is the Platonic ideal of its kind, the concept of the species given form. Around it, many ordinary members of the species naturally congregate.

### The Crystallization Mechanism

Spirit dens form from the interaction of three systems:

#### 1. Leyline Energy (Environment Service)

Leylines are geological features of the pneuma substrate. Where they converge or approach the surface, ambient mana density is high. The Environment service tracks this as a location-level property. Not all high-mana locations become spirit dens -- the energy is necessary but not sufficient.

#### 2. Species Memento Accumulation (Memento Inventories)

Every animal death generates a `DEATH_MEMENTO` at its Location containing species data (species_code, behavioral snapshot, physical traits, cause of death, significance score). Over centuries, a location where a species has thrived accumulates a deep inventory of species-specific death mementos.

The memento pruning system is critical: as the inventory fills, low-significance mementos are pruned first. Over centuries, only the "greatest hits" remain -- the bravest stag who stood against wolves, the doe who led her herd through the worst winter, the ancient buck who lived to extraordinary age. The memento inventory is naturally curated to the most exceptional examples of the species.

#### 3. Undisturbed Accumulation (Time + Seclusion)

The mementos must accumulate **without consumption**. If necromancers, mediums, or shrine keepers regularly visit and consume the animal mementos, the inventory never reaches crystallization density. Spirit dens form in secluded, hard-to-reach places precisely because those places are undisturbed by humanoid interaction. The seclusion is not arbitrary flavor -- it is a mechanistic requirement.

#### The Crystallization Event

When species-specific memento density at a leyline convergence exceeds a threshold, the ambient pneuma energy spontaneously processes the accumulated logos into a standing manifestation:

```
Conditions for spirit den formation:
  1. Location has leyline_proximity > threshold (Environment data)
  2. Location memento inventory contains > N species-specific death mementos
  3. Memento inventory has been undisturbed for > T game-time
  4. Average significance_score of remaining mementos > S (pruning has curated quality)

When all conditions met:
  → Seed created (type: "spirit_animal", species_code, location_id)
  → Seed initial growth derived from memento quality and quantity
  → Conspecific attraction effect activates at Location
  → Spirit den exists
```

The spirit animal is not summoned. No god created it. No necromancer shaped it. It is what happens when the world's own spiritual physics operate on accumulated data without interference. This is the "Emergent Over Authored" principle applied to sacred geography.

### Spirit Animal Progression

Spirit animals follow the **Actor-Bound Entity pattern** (ACTOR-BOUND-ENTITIES.md):

```
Stage 1: DORMANT (seed phase: Dormant)
  The spirit den exists but the spirit animal is not yet manifest.
  Local animals gather in unusual density. The location feels "special"
  to characters with spiritual perception. Hunters notice the game
  is unusually plentiful here. But there is no visible spirit animal.

  Observable signs:
  - Higher than normal animal density of one species
  - Animals appear healthier, more vital than elsewhere
  - Spiritual perception reveals a "warmth" or "pull" at the location

Stage 2: STIRRING (seed phase: Stirring → event brain actor spawned)
  A particularly impressive member of the species appears -- larger,
  more vivid, seemingly ageless. Local NPCs whisper about it.
  "The white stag has been seen again in the deep wood."

  The event brain runs a simplified ABML behavior:
  - ${ethology.*} values at species maximum (all axes at ideal)
  - Uncanny awareness (detects threats at extreme range)
  - Avoids death unnaturally (the leyline sustains it)
  - Appears and disappears (players catch glimpses)

  The kin gathering effect intensifies. Other animals of the species
  are drawn more strongly. The den becomes a dense population center.

Stage 3: AWAKENED (seed phase: Awakened → Character created, Actor binds)
  The spirit animal is now a genuine autonomous entity:
  - Character record in a NATURE system realm
  - Full personality (derived from curated memento data)
  - Memory of every creature that has lived and died in its domain
  - Carries the accumulated species-history of its location

  The spirit animal is visibly supernatural:
  - Luminous (pneuma glow from leyline energy)
  - Larger than any normal member of its species
  - Moves with uncanny grace and awareness
  - Other animals of its species defer to it instinctively

  Variable providers activate:
  - ${personality.*} derived from aggregated memento personality data
  - ${encounters.*} begins accumulating (remembers every visitor)
  - ${backstory.*} synthesized from centuries of species memento data
  - ${ethology.*} at species maximum across all axes

Stage 4: ANCIENT (seed phase: Ancient)
  A spirit animal that has persisted for centuries:
  - Rich inner life, personality evolution
  - Deep memory of the location's ecological history
  - May communicate with characters who have sufficient spiritual
    perception (images, feelings, territorial warnings)
  - Effectively a minor nature deity, but unaligned with any god
  - Its death (if somehow achieved) would be a realm-significant event,
    generate extraordinary mementos, and potentially trigger
    god-actor narrative responses
```

### Spirit Den Spatial Structure

```
┌──────────────────────────────────────────────────────────┐
│                  SURROUNDING ECOLOGY                      │
│  Normal predator territories. The spirit den is INSIDE    │
│  predator territory because secluded places are where     │
│  predators roam. Getting here means navigating through    │
│  the full ecological gauntlet.                            │
│                                                           │
│    ┌──────────────────────────────────────────────┐      │
│    │            APPROACH ZONE                      │      │
│    │  Species density increases. For a deer spirit  │      │
│    │  den: many deer, all healthy, all alert.       │      │
│    │  For a wolf spirit den: pack territory at its  │      │
│    │  most defended. Intrusion response Level 4-5.  │      │
│    │                                                │      │
│    │    ┌──────────────────────────────────┐       │      │
│    │    │      CONGREGATION ZONE            │       │      │
│    │    │  Dense same-species population.    │       │      │
│    │    │  Prey dens: safety in numbers.     │       │      │
│    │    │  Predator dens: pack core area.    │       │      │
│    │    │  NOT a non-aggression zone.        │       │      │
│    │    │  But conspecific tolerance is high. │       │      │
│    │    │                                    │       │      │
│    │    │    ┌────────────────────────┐     │       │      │
│    │    │    │     THE DEN (core)      │     │       │      │
│    │    │    │  The spirit animal.     │     │       │      │
│    │    │    │  High ambient mana.     │     │       │      │
│    │    │    │  Lush vegetation.       │     │       │      │
│    │    │    │  Leyline convergence.   │     │       │      │
│    │    │    │  Pneuma glow visible    │     │       │      │
│    │    │    │  to spiritual percep.   │     │       │      │
│    │    │    └────────────────────────┘     │       │      │
│    │    └──────────────────────────────────┘       │      │
│    └──────────────────────────────────────────────┘      │
│                                                           │
└──────────────────────────────────────────────────────────┘
```

### Predator Spirit Dens vs Prey Spirit Dens

The predator ecology research creates a critical asymmetry:

#### Prey Spirit Den (Deer, Elk, Rabbits)

- **Approach challenge**: Navigate through predator territories to reach the den
- **Congregation zone**: High prey density creates safety through vigilance (the real-world water hole math, amplified). Predators CAN enter but hunting is inefficient -- too many eyes
- **Core atmosphere**: Pastoral. Peaceful. A luminous deer in a sun-dappled glade surrounded by its kin. The contrast with the dangerous journey to get here is the emotional payload
- **Interaction**: The spirit deer does not flee from the player (it fears nothing). A medium can commune with it. A hunter who kills it commits an act of profound ecological disruption
- **Ecological role**: The prey spirit den stabilizes local prey populations. Its conspecific attraction keeps prey density high in the surrounding area, which supports the predator ecology. Destroying a prey spirit den cascades into predator behavioral changes as prey scatter

#### Predator Spirit Den (Wolf, Eagle, Bear)

- **Approach challenge**: Navigate through the SAME species' territory at maximum defense intensity. The spirit wolf's pack treats the approach as a deep core intrusion (Level 5-6 response)
- **Congregation zone**: The pack itself. Multiple family groups of wolves, all at peak behavioral parameters. This is not a safe place -- it is the most dangerous version of wolf territory in the world
- **Core atmosphere**: Intense. Primal. A massive luminous wolf surrounded by its family, all watching the intruder with focused attention. Not hostile by default (the spirit animal has intelligence beyond mere predation) but absolutely not safe
- **Interaction**: Earning the spirit wolf's regard requires demonstrating something -- strength, respect, kinship, or spiritual resonance. The spirit animal's ABML behavior evaluates the visitor
- **Ecological role**: The predator spirit den stabilizes local pack structure. Other wolf packs in the region orient their territories relative to the spirit den's influence. Disrupting a predator spirit den triggers territorial instability across the region

#### The Asymmetry in Gameplay Terms

| Aspect | Prey Spirit Den | Predator Spirit Den |
|--------|----------------|-------------------|
| **Journey danger** | External (predators en route) | Escalating (the species itself) |
| **Arrival atmosphere** | Relief and wonder | Tension and awe |
| **Spirit animal demeanor** | Serene, observing | Evaluating, powerful |
| **Player approach** | Reverence, communion | Proving worth, earning respect |
| **Disruption consequence** | Prey scatter, predators expand | Territory collapse, mesopredator release |
| **Character archetype drawn** | Druids, shamans, mediums, bards | Warriors, rangers, beast-masters |

### Spirit Animal Memento Interaction

Once awakened, a spirit animal gains a natural Medium-like ability over its own den's memento inventory. The spirit deer **knows** every deer that has ever lived and died in its domain because it was crystallized from their logos. It carries the accumulated species-memory of that location.

When a player's medium companion reads the spirit animal, they access not one death memento but the spirit's **synthesis of all of them**:
- The great winters that tested the herd
- The predator shifts -- when wolves arrived, when bears changed territory
- The generation that discovered the hidden meadow
- The ancient doe who lived thirty years when most live twelve
- The battle between two great stags that the entire herd watched

All generated from real simulation data through the memento system. No lore book. No authored history. The spirit animal IS the history, crystallized into living form.

### Spirit Den Formation Ecology

The predator ecology research directly determines WHERE spirit dens form:

**Prey spirit dens** form where prey populations are densest and longest-established. Per the ecology research, healthy prey populations require:
- Sufficient food resources (Environment data: vegetation, water)
- Apex predator presence to suppress mesopredators (mesopredator release research)
- Stable territory structure in surrounding predator populations

This means **healthy predator ecology enables prey spirit den formation**. Remove the wolves, fox populations explode, deer populations crash, deer memento accumulation drops, spirit den weakens. The ecological cascade has a spiritual shadow cascade.

**Predator spirit dens** form where predator populations are most stable and long-established. Per the ecology research:
- Strong pack/pride/clan with multi-generational territory tenure
- Sufficient prey density to sustain the population
- Low interspecific competition (the apex predator in its optimal habitat)
- Leyline convergence in core territory

A wolf spirit den requires wolves that have held the same territory for centuries -- which means stable prey, stable ecology, no major disruptions. Spirit dens are indicators of ecological health.

---

## Part 3: The Convergence Case

### When Sanctuary Meets Spirit Den

The rarest locations in the game world are where a divine sanctuary overlaps with a leyline convergence that hosts spirit dens. This requires:

1. A god performed a miracle at a location that happens to be a leyline convergence
2. The sanctuary's non-aggression zone has persisted long enough for memento accumulation
3. Multiple species have accumulated death mementos in the sanctuary (accelerated by the sanctuary's safety -- more animals live longer here, meaning more significant life-mementos)
4. The leyline energy crystallized spirit animals from multiple species' logos simultaneously

The result: a location where the spirit wolf lies beside the spirit deer, the spirit eagle perches above the spirit rabbit, and a dozen species' supernatural exemplars coexist in divine peace. Each spirit animal carries centuries of its species' memory. The pneuma resonance hums visibly. The vegetation is impossibly lush. The air shimmers.

This is the legendary location that NPCs speak of in whispers. It might exist in each realm -- singular, unique, the place where the divine and the natural achieved perfect harmony. Finding it requires:
- Navigating the full ecological gauntlet of predator territories (PREDATOR-ECOLOGY-PATTERNS.md)
- Sufficient spiritual perception to detect the signs (Agency service progressive disclosure)
- Following clues from NPC accounts, spirit den discoveries, and sanctuary encounters
- A journey that IS the content (North Star #1)

### Zero New Services

Like living weapons (ACTOR-BOUND-ENTITIES.md), the convergence case requires no new Bannou services. It composes entirely from:

| Service | Role |
|---------|------|
| **Environment (L4)** | Leyline proximity data, sanctuary_suppression field, prey_availability pools |
| **Location (L2)** | Spatial anchoring, memento container hosting |
| **Item (L2)** | Memento templates and instances |
| **Inventory (L2)** | Location memento containers |
| **Seed (L2)** | Spirit animal growth progression |
| **Actor (L2)** | Spirit animal behavior execution (event brain -> character brain) |
| **Character (L2)** | Spirit animal identity at Awakened phase (NATURE system realm) |
| **Ethology (L4)** | Species behavioral baselines (spirit animal uses maximum values) |
| **Divine (L4)** | Sanctuary creation by god-actors, divinity economy for maintenance |
| **Status (L4)** | Sanctuary Peace effect, consecration effects from shrine keepers |
| **Collection (L2)** | Spirit animal accumulated species knowledge |
| **Worldstate (L2)** | Game clock for decay timers, seasonal effects on sanctuary/den |
| **Transit (L2)** | Connectivity determining seclusion (spirit dens form where access is difficult) |
| **Agency (L4)** | Progressive spiritual perception gating (what players can see) |

---

## Part 4: The Ecological-Spiritual Geography Loop

### The Compound System

The predator ecology, memento inventories, sanctuaries, and spirit dens create a compound feedback loop:

```
Predator ecology rules determine WHERE deaths happen
    │
    ▼
Deaths generate mementos at those Locations
    │
    ▼
Memento density shapes spiritual geography
    │
    ├──→ At leyline convergences: spirit dens crystallize
    │        │
    │        ▼
    │    Spirit dens stabilize local ecology
    │    (prey dens attract prey, predator dens stabilize territories)
    │        │
    │        ▼
    │    Stabilized ecology produces more consistent memento accumulation
    │        │
    │        └──→ Feedback: spirit dens strengthen over time
    │
    ├──→ At divine miracle sites: sanctuaries form
    │        │
    │        ▼
    │    Sanctuaries create safe zones where animals live longer
    │    (more significant life-mementos accumulate)
    │        │
    │        ▼
    │    Long-lived animals produce higher-significance death mementos
    │        │
    │        └──→ Feedback: sanctuaries accumulate richer spiritual content
    │
    └──→ At buffer zones and territorial boundaries: conflict mementos accumulate
             │
             ▼
         Ancient battlegrounds become spiritually charged wilderness
         (necromancer hotspots, medium pilgrimage destinations)
             │
             ▼
         NPCs interact with these locations, generating new mementos
             │
             └──→ Feedback: the spiritual landscape deepens over world-age
```

### Temporal Depth

Year 1: Sparse mementos. No spirit dens (insufficient accumulation time). Fresh sanctuaries from initial divine acts. Spiritual geography is minimal.

Year 3: First spirit dens begin crystallizing at the strongest leyline convergence points with the densest species populations. Sanctuaries have accumulated layers of peaceful mementos. Ancient battlefields between predator territories have notable spiritual density.

Year 5: Mature spirit dens with Stirring-phase spirit animals ("the white stag has been seen again"). Some sanctuaries maintained by shrine keepers; others beginning to fade (quest content). The spiritual geography is rich enough to support dedicated spiritualist character archetypes (necromancers, mediums, shamans).

Year 10+: Ancient spirit dens with Awakened spirit animals carrying centuries of species memory. The convergence case may exist -- the legendary location where sanctuary and spirit den overlap. Failed sanctuaries have left scars (locations with memento inventories recording the transition from peace to violence). The spiritual dimension of the world is as deep and layered as the physical one.

### Content Flywheel Integration

The spiritual geography feeds both the primary and secondary content flywheels:

**Primary flywheel**: A god-actor perceives a failing sanctuary -> commissions a quest via Storyline -> player journeys to restore it -> the journey through predator ecology is the gameplay -> success or failure generates new mementos -> god-actor perceives the outcome -> new narrative seeds.

**Secondary flywheel (memento)**: Spirit den crystallizes -> NPC shaman discovers it -> shrine keeper consecrates nearby -> bard composes the discovery into a song -> the song becomes a Collection entry -> other NPCs hear it and seek the spirit den -> their journey generates mementos along the route -> those mementos enrich the spiritual geography of the paths leading to the den.

**Cross-pollination**: A dying sanctuary near a leyline convergence fails -> the spirit den that was forming in the safety of the sanctuary is disrupted -> the spirit animal (if Stirring or Awakened) responds -- either fleeing, fighting, or mourning -> god-actors perceive a significant event -> multiple narrative threads emerge simultaneously from a single ecological-spiritual cascade.

---

## Part 5: Gameplay Implications

### Discovery as Progressive Disclosure

Spirit dens and sanctuaries are not marked on maps. They are discovered through the progressive agency system:

| Spirit Agency Level | What the Player Perceives |
|--------------------|--------------------------|
| **Minimal** | Nothing. The location looks like any other forest clearing |
| **Low** | A vague feeling of peace (sanctuary) or vitality (spirit den). The companion character mentions something feels different |
| **Moderate** | Visual pneuma indicators -- shimmer, warmth, glow. Animals behaving unusually. The player can see scent-trail-equivalent spiritual traces |
| **High** | Full spiritual perception. The sanctuary's pneuma resonance is visible as light. The spirit animal is visible in its supernatural form. Mementos at the location can be sensed |
| **Mastery** | The player can perceive the spirit animal's memories, sense the sanctuary's divine origin, read the spiritual history of the location directly |

This means two players visiting the same location have radically different experiences based on their spirit's accumulated spiritual domain experience. One sees a nice clearing; the other sees the convergence of the divine and the natural. Same world, different windows into it -- the UX manifest principle from PLAYER-VISION.md.

### Character Archetypes and Interaction

| Archetype | Sanctuary Interaction | Spirit Den Interaction |
|-----------|----------------------|----------------------|
| **Medium** | Read the mementos of peaceful coexistence; sense the divine origin | Commune with the spirit animal; access centuries of species memory |
| **Necromancer** | Forbidden ground -- consuming sanctuary mementos may damage the sanctuary | Spirit den mementos are rich material; but consuming them weakens the den |
| **Shrine Keeper** | Maintain and reinforce the sanctuary through consecration | Consecrate the spirit den, potentially accelerating the spirit animal's growth |
| **Bard** | Compose from the unique emotional mementos of interspecies peace | Compose from the spirit animal's species history (epic ballads of the white stag) |
| **Hunter/Ranger** | A place to observe animal behavior without the confounds of fear and aggression | Finding the spirit animal is the ultimate tracking challenge; its favor grants supernatural tracking abilities |
| **Druid/Shaman** | The natural meeting place for druidic councils (divine peace + nature) | The spirit animal is a patron/guide; communion grants ethology knowledge and nature magic |
| **Warrior** | A place of rest and recovery (Status effect: enhanced healing in sanctuary) | The spirit predator (wolf, bear, eagle) tests the warrior; earning its respect grants combat insight |

### Ecological Consequences of Disruption

**Sanctuary destroyed** (desecration, divine imprint shattered):
- Immediate: All animals in the sanctuary revert to normal ecology
- Days: Predators begin hunting. Prey animals that were complacent (generations of sanctuary safety) are easy targets
- Weeks: Massive predation event generates intense memento accumulation
- Months: Local prey populations crash. Predators expand territory. Mesopredator release may occur if the sanctuary's stability was supporting apex predator populations nearby
- God-actor response: The originating deity perceives the desecration. Response depends on divine personality -- Silvanus might mourn and attempt restoration, Ares might see an opportunity, Moira might weave the tragedy into narrative

**Spirit den destroyed** (spirit animal killed, leyline disrupted):
- Immediate: Conspecific attraction ceases. Species members begin dispersing
- Days: The congregation zone dissolves. Prey species scatter into surrounding predator territories (easy hunting for predators, bad for prey). Predator species' territorial structure destabilizes
- Weeks: Memento inventory at the location begins decaying without the leyline's preserving effect. The spiritual record of the species at this location erodes
- Months: Local population dynamics shift. If a prey spirit den was destroyed, predators lose a reliable prey concentration and must range further. If a predator spirit den was destroyed, territory vacuum triggers floater dynamics, pack restructuring, potential mesopredator release
- Recovery: A new spirit den at the same leyline could eventually reform, but requires centuries of new memento accumulation. The world remembers -- any spirit that eventually forms will inherit mementos of the previous one's destruction

### Quest Hooks (Emergent, Not Authored)

These emerge naturally from the systems:

- **The Fading Grove**: A god-actor detects sanctuary decay. A quest is commissioned to find a shrine keeper willing to journey there. The shrine keeper NPC needs escort through predator territories. The predator ecology is the gameplay. The destination is the quest reward -- witnessing and restoring the sanctuary
- **The White Stag**: NPCs begin reporting sightings of an unusual deer in the deep forest (a spirit animal at Stirring phase). The player can seek it. Finding it requires reading the landscape of fear -- following deer movement patterns backward through predator territories to the source. The spirit stag evaluates the player's approach
- **The Wolf King's Domain**: A predator spirit den is discovered. A warrior NPC seeks to prove themselves against the spirit wolf. The player can assist, compete, or try to prevent the fight. Killing the spirit wolf destabilizes regional wolf ecology. Earning its respect grants something of far greater value
- **The Poisoned Spring**: A sanctuary's water source is contaminated (by mundane or magical means). The divine imprint is failing because the miracle (the spring) is dying. The contamination source might be another player's mining operation, a dungeon's corruption spreading, or a natural geological shift. The quest is detective work + ecological restoration
- **The Silent Battlefield**: An ancient battle between two wolf packs left dense combat mementos at the territorial boundary. The location is spiritually intense. A necromancer NPC has begun summoning wolf spirit echoes. The spirit echoes fight phantom battles that disturb the living ecology. The player must decide: stop the necromancer, help them complete their research, or find another solution

---

## Part 6: Architectural Notes

### Environment Service Extensions

The Environment service already models ecological resources per Location. Sanctuaries and spirit dens require:

- `leyline_proximity`: Float (0.0-1.0) per Location indicating proximity to leyline convergence points. Geological, changes only on geological timescales. Determines mana availability for spirit crystallization
- `sanctuary_suppression`: Float (0.0-1.0) per Location indicating current sanctuary strength. Computed from divine imprint energy + consecration reinforcement. Decays over time without maintenance. Feeds into ABML as GOAP action cost modifier for aggression actions
- `spirit_den_species`: String code (nullable) per Location indicating which species' spirit den is present. Computed from memento inventory analysis at leyline convergence points

### Location Service Schema Extensions

Per MEMENTO-INVENTORIES.md, Locations gain `mementoContainerId` with creation policies. Spirit dens additionally need:

- The memento container at a spirit den Location should have `creation_policy: explicit` (pre-created when leyline convergence is seeded) and higher capacity limits (these locations accumulate more mementos by design)
- The sanctuary_suppression field on Location enables spatial queries ("find all locations within this sanctuary's gradient zone")

### ABML Integration

Predator behaviors reference sanctuary and spirit den data through existing variable providers:

```
// Sanctuary check in hunt action precondition:
// ${environment.sanctuary_suppression} from Environment variable provider
precondition: ${environment.sanctuary_suppression} < 0.1

// Spirit den conspecific attraction in movement action:
// ${ethology.spatial.territory_fidelity} from Ethology variable provider
// Spirit den locations get a conspecific_attraction bonus in Environment data
movement_weight: base_weight + ${environment.conspecific_attraction} * 0.5

// Spirit animal behavior uses maximized ethology values:
// ${ethology.hunting.pursuit_endurance} is 1.0 for a spirit wolf
// ${ethology.sensory.visual_detection_range} is 1.0 for a spirit eagle
```

### System Realm for Spirit Animals

Spirit animals at Awakened phase get Character records in a **NATURE** system realm (alongside PANTHEON for gods, DUNGEON_CORES for dungeons, SENTIENT_ARMS for living weapons, UNDERWORLD for dead characters, NEXIUS for guardian spirits). This keeps spirit animal character records out of physical realm queries while giving them full access to the L2/L4 character cognitive stack.

---

## Design Principles

1. **Contrast is everything**. Sanctuaries and spirit dens are meaningful only because the rest of the world follows strict ecological rules. The predator ecology must be real and visceral for these exceptions to have emotional impact.

2. **Emergence, not placement**. Spirit dens form from simulation dynamics (memento accumulation + leyline energy). Sanctuaries form from god-actor decisions (divine economy + narrative evaluation). Neither is placed by a designer on a map.

3. **Rarity preserves wonder**. A handful of sanctuaries and spirit dens per realm. Not every forest has a spirit wolf. Not every spring is sacred. The rarity makes discovery a genuine event.

4. **The journey is the content**. Reaching a spirit den or sanctuary requires navigating through the full predator ecology. The ecological gauntlet IS the gameplay. The destination justifies the journey.

5. **Disruption has consequences**. Destroying a sanctuary or spirit den triggers ecological cascades that ripple through predator territories, prey populations, and the spiritual geography. These consequences are visible and lasting. Player choices matter because the simulation remembers.

6. **World age enriches**. Year 1 has sparse spiritual geography. Year 10 has deep, layered, interconnected sacred locations with centuries of memento history. The content flywheel principle applies to geography itself -- the longer the world runs, the richer its sacred places become.

7. **Zero new services**. Both concepts compose entirely from existing Bannou primitives. This validates the platform's composability thesis and means implementation is configuration and behavior authoring, not service development.

---

*This document describes the design vision for sanctuaries and spirit dens. Implementation details (schema extensions, ABML behavior templates, seed type definitions, Environment service ecological resource modeling) belong in the relevant service deep dives and implementation plans. The ecological rules governing the surrounding world are documented in PREDATOR-ECOLOGY-PATTERNS.md. The memento system that underlies spirit den formation is documented in MEMENTO-INVENTORIES.md.*
