# Player-Driven World Changes: Divine Orchestration Through Narrative Agency

> **Type**: Vision Document
> **Status**: Aspirational
> **Created**: 2025-12-01
> **Last Updated**: 2026-03-09
> **North Stars**: #1, #2, #5
> **Related Plugins**: Divine, Storyline, Quest, Contract, Gardener, Puppetmaster, Actor, Faction, Hearsay, Obligation, Disposition, Collection, Item, Inventory, Currency, Worldstate, Seed, Agency

## Summary

Describes the divine orchestration pattern where gods create conditions for permanent world changes and let characters carry them through via the Storyline, Quest, and Contract pipeline, rather than issuing direct decrees. Players and NPCs participate equally in quest chains whose Contract prebound callbacks enact lasting world state changes such as founding traditions, establishing trade routes, and creating organizations. Requires zero new services or code changes, relying entirely on authored ABML behaviors over existing service composition. The pattern is aspirational, pending full implementation of the Divine, Storyline, and Gardener services.

---

## Background

CULTURAL-EMERGENCE.md describes how divine actors observe emergent conditions and crystallize them into cultural norms. That document presents the **divine decree** model: god observes, god acts, world changes. This document describes the inverse and complementary pattern: **divine orchestration**, where gods create the *conditions for change* and let characters -- NPCs and players alike -- be the ones who carry it through.

The difference is not in the outcome. The fishing village still gets its harvest festival. The coming-of-age custom still emerges. The cultural norm still gets planted. The difference is in the **path**: instead of the divine actor directly calling `faction/norm/create` and `hearsay/inject_belief`, it commissions a **Storyline** scenario whose quest chain, when completed by in-world characters, triggers those same service calls as **Contract prebound API callbacks**. The world change is asynchronous with the divine decision, time-limited, player-influenceable, and potentially competitive.

This produces three things the decree model cannot:

1. **Player agency over permanent world state**: Players experience themselves as the cause of lasting change, not observers of divine fiat
2. **Narrative memory**: The quest chain generates Character History entries, Character Encounter records, and Hearsay propagation that become content flywheel input -- the story of *how* the festival was founded is itself a story that future characters can reference
3. **Emergent variation**: When multiple competing approaches are launched simultaneously, the outcome is genuinely unpredictable -- the divine actor sets the goal but does not control which path wins

**Zero new services, zero code changes to Bannou.** The entire pattern is a behavioral difference in how divine actors use the existing Storyline, Quest, and Contract pipeline.

---

## The Core Principle: Decree vs. Orchestration

Every permanent world change that a divine actor can make through direct service calls can *also* be made through a quest chain whose completion callbacks make those same calls. The question is not capability but **narrative richness**.

### The Spectrum

```
DECREE ◄──────────────────────────────────────────────► ORCHESTRATION

God acts directly.              God sets conditions.
Instant effect.                 Asynchronous, time-limited.
No character involvement.       Characters carry the change.
No player agency.               Player choices shape outcome.
No narrative generated.         Rich narrative generated.
No content flywheel input.      Deep content flywheel input.
Appropriate for:                Appropriate for:
  - Background corrections        - Culturally significant changes
  - Emergency interventions        - Founding customs and traditions
  - Invisible maintenance          - Economic shifts
  - Conditions no one notices      - Political transitions
                                   - Anything players should remember
```

A divine actor's ABML behavior decides *where on the spectrum* each change falls. Not every cultural norm needs a quest chain. A minor adjustment to mourning customs after demographic shifts can be a quiet decree. But the founding of a settlement's signature festival, the establishment of a new trade route, the rise of a craft guild -- these are narrative events that characters should live through.

### The Decision Logic

```yaml
# Divine actor decides: decree or orchestrate?
flow: evaluate_change_delivery
  inputs:
    change_significance: float    # How impactful is this change?
    player_proximity: bool        # Are player characters nearby/involved?
    narrative_potential: float     # How interesting is the journey to this change?
    time_pressure: float          # How urgently is this change needed?

  decide:
    # High significance + player proximity + narrative potential = orchestrate
    IF change_significance > 0.7
       AND narrative_potential > 0.5
       AND time_pressure < 0.8
    THEN orchestrate_via_storyline

    # Low significance or high urgency = decree
    ELSE decree_directly

    # Middle ground: orchestrate but with NPC-only participants
    # (change happens through characters, but not player-facing quests)
    IF change_significance > 0.4
       AND player_proximity == false
       AND narrative_potential > 0.3
    THEN orchestrate_via_npc_only
```

The third option -- NPC-only orchestration -- is important. Not every narratively interesting change needs player involvement. A divine actor can commission a Storyline scenario with NPC-only participant archetypes, and the resulting quest chain plays out entirely among NPCs. Players in the area might witness it, hear about it through Hearsay, or discover its effects later. The change still generates narrative memory and content flywheel input even without direct player participation.

---

## Part 1: The Orchestration Pipeline

### Step-by-Step: From Divine Intent to World Change

```
1. Divine Actor                    Observes conditions, decides change is needed
        │
        ▼
2. Storyline Compose               POST /storyline/compose
        │                          Goal: "establish harvest festival"
        │                          Context: settlement data, available NPCs,
        │                          player characters in area
        ▼
3. Storyline Plan                  GOAP generates narrative arc
        │                          Phases, participant archetypes, mutations
        │                          Continuation points for adaptive replanning
        ▼
4. Quest Creation                  POST /quest/definition/create
        │                          Objectives map to Contract milestones
        │                          Reward: cultural participation, community standing
        │                          Prebound callbacks: the actual world change
        ▼
5. Quest Available                 Characters (NPC and player) can accept
        │                          Multiple quests may compete
        │                          Gardener routes to player via garden events
        ▼
6. Quest Execution                 Characters pursue objectives
        │                          Player choices shape specifics
        │                          NPC actors pursue autonomously
        ▼
7. Contract Completion             Milestone conditions met
        │                          Prebound API callbacks fire:
        │                            - faction/norm/create
        │                            - hearsay/inject_belief
        │                            - item/template/create (ceremonial items)
        │                            - collection/entry-template/create
        ▼
8. World Changed                   Cultural norm exists
                                   Characters remember HOW it happened
                                   Content flywheel has new input
```

The critical insight is **step 7**: the world change is a Contract prebound callback. This means:

- The change only happens if the quest succeeds
- The change is atomic (Contract ensures all callbacks fire or none do)
- The change is traceable (Contract tracks who completed it, when, how)
- The change is time-limited (Contract can have a deadline milestone)
- The divine actor does not need to monitor progress -- Contract handles lifecycle

### The Divine Actor's Reduced Role

In the orchestration model, the divine actor's job simplifies to:

1. **Recognize** that conditions warrant a change (same as decree model)
2. **Commission** a Storyline scenario with the change embedded as completion effect
3. **Walk away** -- Storyline, Quest, Contract, and the characters handle the rest

The divine actor can optionally:
- Inject favorable perceptions into nearby NPCs to increase engagement with the quest
- Adjust the scenario parameters if the first attempt fails or times out
- Commission a competing scenario if the first one stalls
- Observe and appreciate the outcome (divine actors have aesthetic preferences)

But it does not need to micromanage. The Contract state machine is the execution guarantee.

---

## Part 2: The Competing Quests Pattern

### The Flower Girl vs. The Fishmonger

A divine actor (Silvanus, god of nature and growth) decides the fishing village needs a seasonal celebration. Instead of decreeing one, Silvanus commissions a Storyline scenario with the goal "settlement establishes seasonal celebration." Storyline's GOAP planner evaluates the settlement and generates **multiple viable approaches** based on available NPCs and resources:

**Quest A: "Petals on the Tide"**
- **Participant**: Lily, the flower girl who tends the meadow above the cliffs
- **Narrative**: Lily has been trying to find a market for her flowers in a village that values fish above all else. Her quest chain involves convincing the village elder, gathering rare coastal blooms, and organizing a trial run of a "Flower Tide Festival" where wreaths are cast into the sea at the summer solstice
- **Completion callback**: `faction/norm/create` with parameters: seasonal_celebration, flower_tide, summer_solstice, community_funded

**Quest B: "The Silver Catch"**
- **Participant**: Old Gareth, the retired fishing captain who misses the communal spirit of fleet expeditions
- **Narrative**: Gareth proposes a competitive fishing tournament followed by a communal feast. His quest chain involves securing sponsorship from the merchant guild, building a trophy (a silver fish), and organizing the first annual Silver Catch
- **Completion callback**: `faction/norm/create` with parameters: seasonal_celebration, silver_catch, autumn_equinox, guild_sponsored

**Quest C (player-facing, if applicable)**:
- **Participant**: Player character who lives in or is visiting the settlement
- **Narrative**: Gardener routes awareness of "the village could use a celebration" through guardian spirit impressions. The player can approach either Lily or Gareth (or neither, or propose something else through the quest system's open objectives)
- **Completion callback**: whichever approach the player champions

### Resolution Mechanics

These quests are **independent Contract instances** with the same high-level goal but different milestone chains. Resolution follows naturally from Contract's existing behavior:

**First-to-complete wins**: The divine actor's Storyline scenario includes a **mutex condition** -- a shared Faction norm slot that can only be filled once. The first Contract whose completion callback successfully calls `faction/norm/create` for that slot succeeds. Subsequent contracts that attempt to fill the already-occupied slot receive a Conflict response, triggering their failure/abandonment path.

**Graceful losing**: The "losing" quest's Contract enters its abandonment flow. The characters involved have their own emotional responses (Disposition: disappointment, determination, acceptance). These responses become Character Encounter records and Hearsay propagation. Old Gareth tells anyone who'll listen that "we should have done the fishing tournament" -- this is new content, not wasted content.

**Long-term consequences of the losing path**: The losing proposal doesn't vanish. It becomes:
- A Hearsay belief: "Gareth wanted a fishing tournament but Lily's flower festival won"
- A potential future quest seed: years later, Gareth's grandson proposes reviving the Silver Catch as a secondary tradition
- Character History entries for everyone involved
- Content flywheel input: the compressed archives of participants include the memory of the competition

**Player influence on NPC quests**: Even if the player doesn't take a quest themselves, they can influence the outcome by:
- Helping one NPC gather materials (contributing to their Contract milestones)
- Buying flowers from Lily (increasing her economic viability narrative)
- Telling stories about festivals they've seen elsewhere (Hearsay injection that shapes NPC motivation)
- Doing nothing (the NPCs resolve it among themselves, and the player witnesses it)

### The Beauty of Not Controlling the Outcome

The divine actor (Silvanus) has an aesthetic preference. Silvanus probably *wants* the flower festival -- it aligns with nature and growth. But Silvanus commissioned a Storyline, not a decree. If Old Gareth's fishing tournament wins because Gareth is more charismatic, has better connections, or a player helped him -- Silvanus accepts the outcome. The world got its celebration. The nature god's preference didn't override the village's organic social dynamics.

This is the purest expression of "Emergent Over Authored" (North Star #5). The divine actor authored the *opportunity for change*. The characters authored the *specific change*. Neither controlled the full outcome.

---

## Part 3: Player-Driven Permanent Changes

### The Principle

> **If NPCs can drive all permanent world changes, players must be able to drive all permanent world changes too.**

This is not symmetry for its own sake. It is the fundamental requirement for players to feel that their actions matter in a living world. If NPCs found cities, establish customs, create trade routes, and shape culture -- but players can only observe these changes -- the world is a museum, not a home.

The orchestration pipeline makes this possible without any special "player override" system. Players participate in the same quest/contract system that NPCs do. Their completion of a Contract fires the same prebound callbacks. The world does not know or care whether the entity that completed the quest was a player-controlled character or an autonomous NPC.

### Categories of Player-Drivable Permanent Changes

| Change Category | Divine Orchestration Mechanism | Player's Role |
|---|---|---|
| **Found a custom/tradition** | Storyline → Quest with norm-creation callback | Complete the quest chain that establishes the tradition |
| **Establish a trade route** | Storyline → Quest with Transit connection-creation callback | Forge the path, negotiate with distant settlement |
| **Create a guild/organization** | Storyline → Quest with Faction-creation and Organization-creation callbacks | Recruit members, secure charter, choose governance |
| **Build significant infrastructure** | Storyline → Quest with Workshop blueprint-activation and Utility network-creation callbacks | Gather materials, organize labor, choose design |
| **Shift political power** | Storyline → Quest with Faction governance-change callbacks | Campaign, negotiate alliances, resolve conflicts |
| **Discover/name a location** | Storyline → Quest with Location-creation and Hearsay identity-injection callbacks | Explore, survive the journey, choose the name |
| **Establish a sanctuary** | Storyline → Quest with Divine blessing-grant and Status resonance callbacks | Perform the ritual, defend the site, maintain the vigil |
| **Trigger economic transformation** | Storyline → Quest with Workshop blueprint-seeding and Trade route-creation callbacks | Introduce new techniques, secure materials, train workers |
| **Resolve inter-faction conflict** | Storyline → Quest with Faction political-connection and Relationship-creation callbacks | Negotiate, mediate, or conquer |
| **Create a legendary item** | Storyline → Quest with Item template-creation and Affix definition callbacks | Forge it, imbue it, name it |

Every row follows the same pattern: the divine actor commissions a Storyline scenario with the permanent change embedded as Contract completion callbacks. The player (or NPC) completes the quest. The callbacks fire. The world changes.

### The Gardener's Role

Gardener is the player-side counterpart to Puppetmaster. When a divine actor commissions a Storyline scenario with player-eligible participant archetypes, **Gardener is what routes awareness of the opportunity to the player**:

1. Divine actor commissions Storyline scenario
2. Storyline generates quest with participant archetypes including "player character matching [criteria]"
3. Gardener's god-actor (the same divine actor, or a coordinating one) detects that a player character in its garden matches the archetype
4. Gardener routes awareness through the guardian spirit: a sense of opportunity, a pull toward the quest giver, an environmental cue (the meadow seems especially beautiful today -- Silvanus nudging)
5. Player follows the pull (or doesn't -- agency is never forced)
6. Player encounters the quest giver NPC and the quest becomes available

The **progressive agency** gradient applies here. A new guardian spirit might perceive only a vague pull ("something interesting is happening near the cliffs"). An experienced spirit might perceive the full context ("Lily is organizing a flower festival and needs help gathering coastal blooms"). The quest is the same. The player's awareness of it scales with their spirit's growth.

### The Director's Role

The Director service (L4) provides a complementary channel: the development team can observe divine orchestrations in progress and choose to enhance them. If the flower-vs-fishing competition is happening in a heavily populated area, a director can:

- **Observe**: Tap into the divine actor's perception stream to see the orchestration playing out
- **Steer**: Adjust the competing NPCs' GOAP priorities to make the competition more dramatic (without taking over their autonomy)
- **Broadcast**: Coordinate which players are routed awareness of the event via Gardener, ensuring the right audience witnesses it

The Director never bypasses the quest/contract system. Every action goes through the same pipelines. But it provides the development team with the ability to curate live moments when organically interesting orchestrations emerge.

---

## Part 4: Narrative Memory and the Content Flywheel

### Why Orchestration Produces Richer History

When a cultural norm is established by divine decree:
- The norm exists
- No one remembers how it started
- No Character History entries are generated
- No Character Encounter records are created
- No Hearsay about the founding propagates
- The content flywheel receives nothing

When the same norm is established through orchestration:
- The norm exists (same outcome)
- Lily remembers proposing the Flower Tide Festival (Character History: "founded_tradition")
- Gareth remembers losing the competition (Character Encounter: "cultural_rivalry" with Lily)
- The village elder remembers approving Lily's proposal (Character Encounter: "cultural_decision")
- The player remembers gathering coastal blooms at dawn (player memory + Character History)
- Hearsay propagates: "The Flower Tide Festival was Lily's idea" -- with attribution, with opinion, with distortion over time
- When Lily eventually dies, her compressed archive includes "founded the Flower Tide Festival" as a high-significance life event
- Storyline can compose future quests from that archive: "the ghost of Lily asks you to ensure her festival survives", "Gareth's grandson wants to add a fishing tournament to the existing festival"
- The festival's founding story becomes oral tradition, subject to Hearsay distortion: "they say a spirit guided a young flower girl to create our most beloved tradition" -- which is *true*, but the Hearsay system doesn't know that

### The Compounding Effect

Each orchestrated change is a content flywheel investment:

```
Orchestrated Change (Year 1)
  ├── Character History entries (participants remember)
  ├── Character Encounter records (social dynamics)
  ├── Hearsay propagation (cultural memory)
  ├── Faction norm (the actual change)
  └── Quest completion records (traceable to individuals)
        │
        ▼
Content Flywheel (Year 2+)
  ├── Archives of participants include the founding
  ├── Storyline composes sequel quests from archives
  ├── Hearsay distorts founding story into legend/myth
  ├── New characters discover the tradition and react
  └── The tradition itself may spawn further orchestrations
        │
        ▼
Cultural Depth (Year 5+)
  ├── "The Flower Tide Festival has been held for 200 years"
  ├── "Founded by Lily the flower girl in the time of Elder Arun"
  ├── "Some say a divine spirit guided her hand" (true, via Hearsay distortion)
  ├── Gareth's fishing tournament was eventually added as a second day
  ├── The festival is now the settlement's defining cultural event
  └── Visitors from other settlements know about it (inter-settlement Hearsay)
```

A single orchestrated change in Year 1 is still generating narrative content in Year 5. A thousand orchestrated changes across dozens of settlements produce a world with genuine cultural history that was *lived*, not scripted. This is the content flywheel operating at its most powerful -- not just recycling death archives, but building layers of cultural memory that make the world feel ancient and real.

---

## Part 5: Failure, Delay, and Unintended Consequences

### What Happens When the Quest Fails

Not every orchestrated change succeeds. Contracts have deadlines. NPCs have competing priorities. Players wander off. This is not a bug -- it is the system working correctly.

**Timeout**: The divine actor's Storyline scenario included a time limit (e.g., "establish a celebration before the next solstice"). If no quest completes before the deadline, all competing Contracts expire through their abandonment paths. The divine actor re-evaluates: are conditions still right? Should it try again with different participants? A different approach? Or has the moment passed?

**Active failure**: A quest participant fails a critical milestone. The Contract enters its breach handling path. The participant's failure is recorded as Character History. Other participants' quests continue unaffected (they are independent Contracts).

**Abandonment**: A player accepts the quest and then never pursues it. The Contract's inactivity timeout triggers. The quest returns to the available pool. An NPC might pick it up. Or it might expire entirely.

**Partial success**: Some milestones complete but not all. The Contract tracks partial progress. A future Storyline scenario can reference the partial attempt -- "Lily tried to start a festival once before but couldn't get enough support. This time, with the new merchant guild's backing..."

### Unintended Consequences

Because the outcome is not predetermined, orchestrated changes can produce results the divine actor didn't expect:

- **The player chooses neither option** and proposes something completely different through open quest objectives. The Contract's prebound callbacks fire with parameters the divine actor didn't predict. The village gets a boat-racing festival instead of flowers or fishing.

- **A competing faction interferes**. A rival settlement's merchants sabotage Lily's flower gathering, turning a cultural quest into a political conflict. The divine actor observes this as new narrative input.

- **The custom takes on a life of its own**. The Flower Tide Festival grows beyond what Silvanus intended -- other gods invest blessings, merchants turn it into a trade fair, a romantic tradition emerges. The divine actor's "seasonal celebration" became a multi-layered cultural institution through emergent elaboration.

- **The change is rejected**. NPCs with high `${personality.traditionalism}` resist the new custom. Obligation enforcement fails because social pressure isn't strong enough. The norm exists formally but isn't practiced. This is a form of cultural failure that generates its own narrative -- "they passed the law but no one follows it."

Every one of these outcomes feeds the content flywheel. Failure and unintended consequences are *more* narratively interesting than clean success. The divine actor's willingness to relinquish control is what makes the world feel alive.

---

## Part 6: The Dual-Agency Gradient

### Divine Actors Choose Their Level of Control

The spectrum from decree to orchestration is not binary. Divine actors choose their level of involvement along a continuous gradient:

| Level | Mechanism | Divine Control | Character Agency | Narrative Generated |
|---|---|---|---|---|
| **Full Decree** | Direct service calls | Total | None | None |
| **Guided Decree** | Direct calls + Hearsay explanation | Total | Aware but passive | Minimal |
| **Soft Orchestration** | Storyline with single predetermined quest | High (one path) | Execute the path | Moderate |
| **Open Orchestration** | Storyline with multiple competing quests | Moderate (sets options) | Choose among options | High |
| **Seed Orchestration** | Inject favorable conditions only, let characters self-organize | Low (nudge only) | Self-directed | Very high |
| **Pure Observation** | Watch and record, intervene only on failure | None | Total | Maximum |

**Different gods gravitate toward different levels**:

- **Moira / Fate**: Prefers soft orchestration. Fate has a plan. Characters carry it out. The outcome is often what Moira intended, but the journey generates narrative.
- **Silvanus / Forest**: Prefers seed orchestration. Nature doesn't dictate -- it provides conditions and lets life find its own way.
- **Ares / War**: Prefers open orchestration. Competition is the point. Multiple paths, one winner, the conflict itself is the value.
- **Hermes / Commerce**: Prefers open orchestration with economic incentives. The quest that generates the most trade value wins.
- **Thanatos / Death**: Prefers pure observation for most things, full decree only for death-related corrections. Death watches; death records; death rarely interferes.
- **Typhon / Monsters**: Prefers soft orchestration with danger. The quest involves surviving something. Success is earned through hardship.

### Players Never Know Which Level They're In

This is perhaps the most important design principle: **the player cannot tell the difference between open orchestration and pure emergence**. When Lily approaches the player about her flower festival idea, the player doesn't know whether:

- A divine actor specifically commissioned this quest
- A divine actor created favorable conditions and Lily self-organized
- Lily had the idea entirely on her own through her own Disposition drives
- The player's guardian spirit nudged them toward Lily through Gardener

And it doesn't matter. The experience is the same: a character with a dream asks for help, and the player decides whether to give it. The machinery behind the scenes -- Storyline, Quest, Contract, Divine, Gardener -- is invisible. The player sees a flower girl with an idea and a village that might change.

---

## Part 7: Service Composition (No New Services Required)

The entire orchestration pattern composes from existing services:

| Step | Service | API / Mechanism |
|---|---|---|
| Recognize change needed | Actor (L2) | Divine actor's ABML variable evaluation |
| Decide orchestration level | Actor (L2) | GOAP planning in divine actor's behavior |
| Commission narrative | Storyline (L4) | `POST /storyline/compose` with change-goal metadata |
| Generate quest(s) | Quest (L2) over Contract (L1) | `POST /quest/definition/create` with prebound callbacks |
| Route to player | Gardener (L4) | Garden event routing through guardian spirit |
| NPC participation | Actor (L2) + Puppetmaster (L4) | NPC actors accept and pursue quests via GOAP |
| Quest execution | Contract (L1) | Milestone progression, condition tracking |
| World change (on completion) | Contract prebound callbacks | `faction/norm/create`, `hearsay/inject_belief`, etc. |
| Competing quest mutex | Faction (L4) | First-to-fill norm slot wins |
| Failed quest cleanup | Contract (L1) | Abandonment path, breach handling |
| Memory recording | Character History (L4), Character Encounter (L4) | Automatic from quest participation |
| Cultural propagation | Hearsay (L4) | Callback on quest completion + organic spread |
| Player awareness | Gardener (L4) + Agency (L4) | Progressive awareness based on spirit growth |
| Live curation | Director (L4) | Observe/steer/broadcast for development team |

### The Prebound Callback Pattern

The key enabling mechanism is Contract's **prebound API execution** -- the ability to register service calls that fire automatically when a milestone is reached. This is not new; it is how Quest already distributes rewards and how Escrow releases assets. The only difference is what the callbacks *do*:

**Traditional quest callbacks**: Grant XP (Seed growth record), give items (Item instance creation), reward currency (Currency credit).

**World-change callbacks**: Create Faction norms, inject Hearsay beliefs, create Item templates (not instances -- the template itself is new to the world), create Collection entry templates, modify Location properties, establish Transit connections, create Organization entities.

The distinction is scope, not mechanism. A quest reward that gives the player 50 gold and a quest completion that establishes a settlement festival use the same Contract prebound callback system. The festival quest just has *more important* callbacks.

### Storyline's Role: The Narrative Planner

Storyline's GOAP planner is what transforms "this settlement needs a celebration" into a playable narrative arc:

**Input** (from divine actor):
- Goal: "establish seasonal celebration at location X"
- Context: settlement identity, available NPCs, player characters in area, existing cultural norms, divine aesthetic preference
- Constraints: time limit, minimum narrative significance, participant archetypes

**Output** (Storyline plan):
- Narrative phases with continuation points
- Participant archetypes matched to available characters
- Quest definitions with milestone chains
- Prebound callbacks encoding the world change
- Competing quest variants (if open orchestration)
- Failure/timeout handling paths

Storyline's lazy phase evaluation means only the current phase is generated. If conditions change mid-quest (a war breaks out, a key NPC dies), the next phase regenerates against live world state. The quest adapts to the world, not the other way around.

---

## Part 8: The Symmetry Principle

### NPCs Can Do Everything Players Can Do

This is already true in Bannou's architecture. NPC actors run the same ABML behaviors, pursue the same GOAP goals, accept the same quests via the same Contract system, and their actions fire the same service calls. An NPC completing a world-change quest produces identical callbacks to a player completing it.

### Players Can Do Everything NPCs Can Do

This document ensures the converse is also true. If an NPC's quest completion can establish a cultural norm, found a guild, create a trade route, or name a location -- then a player's quest completion can do the same. The Contract system does not distinguish between the two.

### The Asymmetry: Awareness and Choice

Where players and NPCs differ is not in capability but in **awareness and choice**:

- **NPCs** accept quests based on GOAP evaluation of their goals and world state. They don't agonize over the decision. They don't consider alternatives they weren't offered. They execute efficiently.
- **Players** are routed awareness through Gardener, perceive the opportunity through their guardian spirit's progressive lens, make deliberate choices about involvement, and can innovate beyond the offered options.

The player's agency advantage is not mechanical power. It is **the ability to care about the outcome**. An NPC founds a festival because their GOAP planner determined it was the optimal action. A player founds a festival because they wanted their fishing village to have something beautiful. The callbacks are identical. The meaning is not.

---

## Conclusion

Divine actors are not diminished by orchestrating through narrative rather than decreeing directly. They are elevated -- from administrators to storytellers. The god that commissions a Storyline scenario and watches characters struggle, compete, fail, and ultimately succeed in transforming their world is doing something more interesting than the god that simply inserts a database row.

Players are not diminished by operating within divinely-orchestrated scenarios. They are elevated -- from quest consumers to world shapers. The player who helps Lily establish the Flower Tide Festival has permanently changed the game world, and the world will remember that they did.

The services do not know the difference. Contract fires callbacks. Faction creates norms. Hearsay propagates beliefs. Character History records events. The content flywheel spins. Whether the catalyst was a divine decree, an NPC's ambition, or a player's choice -- the world changes, and the change generates more world.

No new plugins. No code changes. Just a different question in the divine actor's ABML behavior: not "should this change happen?" but "who should carry this change into being?"

---

*For the cultural patterns that this orchestration delivers, see [CULTURAL-EMERGENCE.md](CULTURAL-EMERGENCE.md). For the divine actor system, see [DIVINE.md](../plugins/DIVINE.md). For the content flywheel, see [COMPRESSION-GAMEPLAY-PATTERNS.md](COMPRESSION-GAMEPLAY-PATTERNS.md). For the Storyline system, see [STORY-SYSTEM.md](../guides/STORY-SYSTEM.md). For the orchestration bootstrap, see [ORCHESTRATION-PATTERNS.md](../reference/ORCHESTRATION-PATTERNS.md).*
