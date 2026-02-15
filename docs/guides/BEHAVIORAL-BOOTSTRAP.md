# Behavioral Bootstrap Pattern

> **Version**: 1.0
> **Last Updated**: 2026-02-14
> **Scope**: Cross-cutting architectural pattern for Puppetmaster and Gardener
> **Referenced By**: [PUPPETMASTER.md](../plugins/PUPPETMASTER.md), [GARDENER.md](../plugins/GARDENER.md), [DIVINE.md](../plugins/DIVINE.md)

This document describes how Puppetmaster and Gardener bootstrap autonomous god-actors that serve as the connective tissue between Bannou's disparate services. These god-actors are the "drive belt" of the content flywheel and the player experience orchestration system. They are **not a plugin** -- they are authored ABML behavior documents executed by the Actor runtime.

---

## The Problem: No "Gameplay Loop" Plugin

Bannou's services are deliberately decomposed into orthogonal primitives: Storyline composes narratives, Divine manages deities, Quest tracks objectives, Music generates compositions, Resource compresses archives. Each handles one concern.

But no single service owns the cross-cutting orchestration: "a character dies, their archive becomes a narrative seed, a god evaluates it, commissions a storyline, translates it into quests and NPC spawns." This is the content flywheel described in VISION.md -- and it has no service.

Building a "gameplay orchestrator" plugin would be architecturally wrong because it would:

- **Centralize logic that should vary per deity, per realm, per game**: Moira (Fate) and Ares (War) evaluate the same death differently. A centralized orchestrator can't express this without becoming a god-object.
- **Create a dependency magnet**: Every service would depend on it or be depended upon by it. The service hierarchy would collapse.
- **Make orchestration logic unmodifiable without code changes**: Adding a new god, changing how deaths are processed, or altering the flywheel pathway would require service code changes.
- **Violate "Emergent Over Authored"**: The fifth design principle says content should emerge from autonomous systems interacting, not from scripted triggers. A gameplay loop plugin IS a scripted trigger.

---

## The Solution: God-Actors as Orchestration

Instead of a service, orchestration lives in **ABML behavior documents** executed by the **Actor runtime**. God-actors are long-running actors whose behavior documents contain the logic that connects services together. They call service APIs through the same GOAP planning and action handler system that drives NPC cognition.

This means:
- Orchestration logic is **data** (behavior documents), not **code** (plugin implementation)
- New orchestration patterns are **authored content**, not code changes
- Different gods express different orchestration strategies through different behaviors
- The Actor runtime's existing infrastructure (perception queues, GOAP planning, variable providers, pool scaling) handles all execution concerns

---

## Bootstrap Sequence

### Phase 1: Seeded Behavior Loading

Puppetmaster and Gardener each register **seeded singleton behavior documents** via `ISeededResourceProvider` in the Resource service. These behaviors are loaded into Resource's data stores on startup and are available before any actors spawn.

```
Resource (L1)
├── Seeded: puppetmaster-manager.abml    (singleton manager behavior)
├── Seeded: gardener-manager.abml        (singleton manager behavior)
└── Seeded: god-behavior-templates/      (deity behavior templates)
    ├── moira-fate.abml
    ├── thanatos-death.abml
    ├── silvanus-forest.abml
    ├── ares-war.abml
    ├── typhon-monsters.abml
    └── hermes-commerce.abml
```

### Phase 2: Singleton Manager Actors

On startup, after all plugins have loaded:

1. **Puppetmaster** calls `IActorClient.SpawnAsync` to create a singleton manager actor using the `puppetmaster-manager.abml` behavior document loaded from Resource.
2. **Gardener** calls `IActorClient.SpawnAsync` to create a singleton manager actor using the `gardener-manager.abml` behavior document loaded from Resource.

These manager actors are **long-running singletons** that never exit under normal operation. They run in the Actor pool alongside NPC actors and are the entry point for all orchestration.

The Actor runtime loads behavior documents through the `IBehaviorDocumentProvider` chain. Puppetmaster's provider serves behaviors from three sources in priority order:

1. **Seeded Resource data** (highest priority -- always available)
2. **Asset service** (runtime-uploaded behaviors for hot-reload)
3. **Fallback compiled defaults** (emergency baseline)

### Phase 3: God Initialization via Divine

The Puppetmaster Manager's behavior document executes initialization logic on first tick:

```
Puppetmaster Manager behavior (pseudocode):

on_initialize:
  for each deity_template in seeded_god_behaviors:
    deity = call /divine/deity/get-by-code { code: template.deity_code }
    if deity is null:
      deity = call /divine/deity/create {
        code: template.deity_code,
        name: template.deity_name,
        domains: template.domains,
        personality: template.personality
      }
    register deity in manager state
```

This creates (or retrieves existing) deity entities in Divine. The deity's persistent identity (divinity economy, follower lists, blessing history) lives in Divine. The deity's runtime brain lives in Actor.

### Phase 4: God-Actor Spawning

After initialization, managers spawn individual god-actor instances:

```
Puppetmaster Manager (continued):

on_initialized:
  for each realm in call /realm/list:
    for each deity in registered_deities:
      if deity.domains overlap realm.active_domains:
        actor = call /actor/spawn {
          templateCode: "regional_watcher",
          behaviorDocumentId: deity.behavior_document_id,
          metadata: {
            deityId: deity.id,
            realmId: realm.id,
            role: "puppetmaster"
          }
        }
        register actor in manager state
```

```
Gardener Manager:

on_player_connected(seedId, sessionId):
  # Assign or spawn a gardener god for this player
  god = find_available_gardener_god()
  if god is null:
    god = call /actor/spawn {
      templateCode: "gardener_god",
      behaviorDocumentId: assigned_deity.behavior_document_id,
      metadata: {
        deityId: assigned_deity.id,
        role: "gardener",
        seedId: seedId
      }
    }
  assign god to player session
```

God-actors are distributed across Actor pool nodes. Managers track which gods are running where.

### Phase 5: Steady State Operations

Once bootstrapped:
- **Puppetmaster god-actors** run continuously, perceiving world events, evaluating archives, commissioning narratives, and orchestrating scenarios
- **Gardener god-actors** tend individual player experiences, spawning POIs, managing scenarios, routing spirit influences
- **Manager actors** monitor health, restart failed gods, load-balance across pool nodes, and scale god count based on activity

---

## The Content Flywheel Connection

The content flywheel as described in VISION.md:

```
Player Actions -> History -> Resource (compression) -> Storyline (narrative seeds)
-> Regional Watchers (orchestrated scenarios) -> New Player Experiences -> loop
```

**God-actors are the arrows in this diagram.** Each arrow represents a god's ABML behavior perceiving an event and calling a service API.

### How a Death Becomes a New Quest

| Step | Actor | Action | Service API Called |
|------|-------|--------|-------------------|
| 1 | Character-Lifecycle | Character dies, publishes `lifecycle.death` with `archiveId` | (event published) |
| 2 | Resource | Compresses character data via registered callbacks, publishes `resource.compressed` | (event published) |
| 3 | Moira (Fate god-actor) | Perceives `resource.compressed`, evaluates archive relevance to her domain | None (internal GOAP evaluation) |
| 4 | Moira | Archive is interesting -- calls Storyline to compose a narrative | POST /storyline/compose |
| 5 | Moira | Receives StorylinePlan with phases, actions, entity requirements | (response processing) |
| 6 | Moira | Translates plan Phase 1 into a quest definition | POST /quest/definition/create |
| 7 | Moira | Spawns a ghost NPC from the archive data | POST /actor/spawn |
| 8 | Moira | Creates narrative items referenced in the plan | POST /item/instance/create |
| 9 | Moira | Blesses a nearby character to "discover" the quest | POST /divine/blessing/grant |
| 10 | Player | Encounters the quest, completes objectives, generates new history | (the loop continues) |

### How a God Decides What's Interesting

Each deity has domain preferences encoded in its ABML behavior document. The evaluation is a GOAP precondition check:

```yaml
# moira-fate.abml (simplified)
perception_filter:
  - event: "resource.compressed"
    conditions:
      # Only evaluate character archives (not realm archives)
      - check: "${event.resourceType} == 'character'"
      # Only if the character had sufficient life complexity
      - check: "${event.metadata.participationCount} > 5"
      # Only if Moira has enough divinity to act
      - check: "${divine.divinity_balance} > 100"

actions:
  evaluate_archive:
    preconditions:
      - "archive loaded into working memory"
    effects:
      - "narrative_seed available"
    steps:
      - load_archive: "${event.archiveId}"
      - evaluate_fulfillment: "${archive.fulfillment}"
      - evaluate_unfinished_business: "${archive.drives.unfulfilled}"
      - if fulfillment < 0.3:
          narrative_type: "tragedy"  # Unfulfilled life -> ghost with regrets
      - if fulfillment > 0.8:
          narrative_type: "legacy"   # Fulfilled life -> inspiration for descendants
      - call: "/storyline/compose"
        with:
          seedSources: ["${event.archiveId}"]
          narrativeHints: { type: "${narrative_type}" }
```

The key insight: **different gods evaluate the same death differently**. Thanatos (Death) cares about the manner of death. Silvanus (Forest) cares about the character's relationship with nature. Ares (War) cares about combat accomplishments. The same archive produces different narratives through different god-actors.

---

## The Player Experience Connection

### Garden Tending via God-Actors

Gardener god-actors are the per-player orchestrators. They replace Gardener's current background worker (fixed-interval ticks) with intelligent, personality-driven experience curation.

```
Player connects via WebSocket
    |
    v
Connect establishes session, publishes connect.session.authenticated
    |
    v
Gardener Service creates/restores garden context
    |
    v
Gardener Manager (singleton actor) perceives player connection
    |
    v
Manager assigns a gardener god-actor to this player
    |
    v
Gardener god-actor begins tending:
    - Reads ${seed.*} for guardian spirit capabilities
    - Reads ${spirit.*} from Agency for UX manifest state
    - Decides what POIs to spawn based on deity personality + player profile
    - Monitors player choices, adjusts future offerings
    - Routes spirit influences from player to possessed character
```

### Spirit Influence Routing

When the player sends a spirit nudge (via Agency's influence execution path), the flow is:

```
Player sends influence via WebSocket
    |
    v
Connect routes to Gardener (binary routing, static endpoint)
    |
    v
Gardener validates influence against Agency manifest
    |
    v
Gardener god-actor perceives the influence as a perception event
    |
    v
God-actor decides: forward to character? modify? intercept?
    |
    v
If forwarded: inject as perception into character's Actor via IActorClient
    |
    v
Character's ABML behavior evaluates against ${spirit.*} compliance
    |
    v
Character complies or resists -> result published as event
    |
    v
Gardener god-actor perceives result, adjusts future behavior
```

The god-actor sits in the influence path because gods should be able to:
- **Modulate** spirit nudges (a war god might amplify aggressive nudges)
- **Intercept** inappropriate nudges (a nature god might block environmental destruction)
- **Respond** to nudge patterns (a god notices the player is struggling and adjusts difficulty)

---

## Manager Actor Responsibilities

### Puppetmaster Manager

| Responsibility | Mechanism |
|---------------|-----------|
| Deity initialization | Calls Divine API to create/retrieve deity entities on startup |
| Regional assignment | Ensures every active realm region has assigned god-actors |
| Load balancing | Distributes god-actors across Actor pool nodes (reports to Orchestrator) |
| Health monitoring | Tracks god-actor heartbeats, restarts crashed gods with state recovery from Divine |
| Scaling | Requests pool scaling from Orchestrator when god count exceeds node capacity |
| Behavior hot-reload | Detects updated behavior documents via Asset events, triggers god-actor behavior swap |
| Archive routing | Routes `resource.compressed` events to the most relevant god(s) based on domain matching |

### Gardener Manager

| Responsibility | Mechanism |
|---------------|-----------|
| Player assignment | Assigns gardener god-actors to connected players (may share gods across low-activity players) |
| Session lifecycle | Handles connect (assign god), disconnect (hibernate god), reconnect (resume god) |
| Load balancing | Distributes gardener gods across Actor pool nodes |
| Garden type routing | Different garden types (void, in-game, housing) may use different god behaviors |
| Scaling | Scales gardener god count based on connected player count |
| Influence routing | Routes player spirit nudges to the correct gardener god-actor |
| Manifest coordination | Coordinates with Agency for manifest updates when garden context changes |

---

## Interaction with Divine

| Concern | Owned By | Interaction |
|---------|----------|-------------|
| Deity identity (name, domains, personality) | Divine | Managers call Divine API to create/retrieve |
| God character record | Character (in divine system realm) | Divine creates on deity creation; Actor binds as character brain |
| Deity economy (divinity balance, earning, spending) | Divine (via Currency) | Scoped to divine system realm; god-actors call Divine API |
| Follower management | Divine (via Relationship) | God-actors call Divine API to manage attention slots |
| Blessing orchestration | Divine (via Collection + Status) | God-actors call Divine API to grant blessings |
| Deity ABML behavior | Resource (seeded) / Asset (hot-reload) | Actor loads via IBehaviorDocumentProvider |
| Runtime cognition | Actor | Character brain with full variable provider chain |

The separation: **Divine owns the god's persistent state. Character (system realm) owns the god's entity identity. Actor owns the god's runtime brain. Puppetmaster owns the god's lifecycle management.**

See [God-Actors as Character Brains](#god-actors-as-character-brains-system-realm-pattern) below for the full system realm pattern, and [DIVINE.md: God Characters in System Realms](../plugins/DIVINE.md#god-characters-in-system-realms) for the architectural concept.

---

## Interaction with Agency

| Concern | Direction | Mechanism |
|---------|-----------|-----------|
| UX manifest computation | Agency reads Seed | Agency subscribes to `seed.capability.updated` |
| Manifest delivery | Agency -> Gardener -> Client | Agency publishes `agency.manifest.updated`, Gardener routes via Entity Session Registry |
| Spirit influence validation | Gardener -> Agency | Gardener calls Agency to validate influence against manifest |
| Compliance variables | Agency -> Actor | Agency provides `${spirit.*}` via Variable Provider Factory |
| Influence execution | Agency -> Gardener god -> Actor | Agency validates, Gardener god-actor modulates, Actor injects perception |

---

## God-Actors as Character Brains (System Realm Pattern)

> See [DIVINE.md: God Characters in System Realms](../plugins/DIVINE.md#god-characters-in-system-realms) for the full architectural concept. This section covers the behavioral bootstrap implications.

### The Pattern

Realm's `isSystemType` flag enables a divine system realm (e.g., `PANTHEON`) where every deity has a Character record. This transforms god-actors from **event brain** actors (using `load_snapshot:` for ad-hoc data) into **character brain** actors (with automatic variable provider binding to their own divine character).

The existing bootstrap phases do not structurally change. The divine character record is created as part of deity initialization (Phase 3 -- `/divine/deity/create` internally creates the character), and the Puppetmaster Manager passes `characterId` when spawning god-actors (Phase 4). This is internal to the Divine service's implementation, not a change to the bootstrap sequence itself.

The god-actor's character brain binding is **permanent** -- it is always bound to the god's divine system realm character for the actor's entire lifetime. This gives every god-actor automatic access to `${personality.*}`, `${encounters.*}`, `${backstory.*}`, `${quest.*}`, `${world.*}`, and `${obligations.*}` through the standard Variable Provider Factory chain. God-actors can still use `load_snapshot:` for data about arbitrary mortal entities -- the character brain binding adds self-data, it doesn't remove event brain capabilities.

### Avatar Manifestation in the Flywheel

The divine character pattern enables a new content flywheel pathway via avatar manifestation. Avatars are a **steady-state behavioral decision**, not a bootstrap concern -- the god's ABML behavior decides when to manifest, and the Divine service's avatar API handles the orchestration and economy.

```
God perceives archive (resource.compressed)
    │
    ├── Evaluates relevance to domain (standard flywheel path)
    │
    └── Decides to manifest avatar in physical world
        │
        ▼
    God calls /divine/avatar/manifest
        ├── Divine calculates divinity cost (scales with recency of last avatar death)
        ├── Divine debits divinity from god's wallet
        ├── Divine creates Character in physical realm
        ├── Divine creates Relationship (divine_manifestation)
        ├── Divine spawns Actor for avatar (separate character brain)
        └── Divine registers watch so god perceives avatar events
        │
        ▼
    Avatar lives as a real character in the world
    Players interact with avatar, generate history and encounters
    Avatar may reveal divine nature, grant quests, bestow blessings
        │
        ▼
    Avatar dies (killed by players, sacrifices self, mortal lifespan)
        │
        ├── Death archive enters content flywheel (standard path)
        │   └── Other gods may evaluate this archive
        │
        └── God perceives avatar death via watch system
            ├── Divine updates tracking (lastAvatarDeathAt, clears activeAvatar)
            └── God reacts based on ${personality.*}:
                grief, vengeance quest, quiet resignation
                (spawning a new avatar costs MORE divinity if done too soon)
```

Avatar manifestation is ABML behavior authoring mediated by Divine's avatar API. The economy layer ensures gods can't spam avatars -- divinity costs scale with recency, creating a natural cooldown that the god's GOAP planner must account for when deciding whether to manifest.

### Ownership Table Update

| Concern | Owned By | Interaction |
|---------|----------|-------------|
| Deity identity | Divine | Managers call Divine API to create/retrieve |
| **God character record** | **Character (in divine system realm)** | **Divine creates on deity creation; Actor binds as character brain** |
| **Divine genealogy** | **Relationship** | **Parent/child, spouse, rivalry bonds between god characters** |
| **God personality** | **Character Personality** | **Standard trait axes; god-actor reads via ${personality.*}** |
| **God memories of mortals** | **Character Encounter** | **Standard encounter tracking; god-actor reads via ${encounters.*}** |
| Deity economy | Divine (via Currency) | Scoped to divine system realm |
| Follower management | Divine (via Relationship) | Deity-character follower bonds |
| Blessing orchestration | Divine (via Collection + Status) | God-actors call Divine API |
| Deity ABML behavior | Resource (seeded) / Asset (hot-reload) | Actor loads via IBehaviorDocumentProvider |
| Runtime cognition | Actor | **Character brain with full variable provider chain** |
| **Avatar manifestation** | **Divine (orchestrates Character + Relationship + Actor)** | **God-actor calls `/divine/avatar/manifest`; Divine handles economy, creation, tracking** |
| **Avatar lifecycle** | **Divine (tracking) + Actor (runtime) + watch system (perception)** | **Divine tracks active avatar and death timestamps; god-actor monitors via watch** |

---

## Why This Is Not a Plugin

The behavioral bootstrap pattern is deliberately **not** a service because:

1. **Orchestration logic is authored content.** A god's behavior document is a YAML file, not C# code. Game designers author god behaviors. New flywheel pathways are new behavior documents, not new service endpoints.

2. **The Actor runtime already handles everything.** Perception queues, GOAP planning, variable providers, pool scaling, behavior hot-reload, distributed execution -- all exist. God-actors are just actors with unusual behavior documents.

3. **GOAP is the universal planner.** The same A* search over action spaces that plans NPC combat also plans narrative orchestration. "Should I commission a quest from this archive?" is a GOAP goal evaluation, not a service endpoint.

4. **Independent variation.** Each god has its own behavior document. Moira and Ares produce different orchestration strategies from the same events. A "gameplay loop" plugin would need configuration flags for every variation. Behavior documents express variation naturally through different ABML code.

5. **The Actor is already there.** Actor (L2) already runs 100,000+ concurrent cognitive processes. Adding a few dozen god-actors is negligible overhead. No new infrastructure needed.

---

## ABML Behavior Authoring Notes

### God Behavior Document Structure

God behaviors follow the same ABML structure as NPC behaviors but with different perception sources and action handlers:

| Aspect | NPC Actor | God Actor |
|--------|-----------|-----------|
| Perception sources | Local environment, encounters, social | Global events (deaths, archives, world state changes) |
| Variable providers | `${personality.*}`, `${encounters.*}` | `${divine.*}`, `${spirit.*}`, `${world.*}` |
| Action handlers | Move, attack, speak, craft | Call service APIs, spawn actors, create quests, grant blessings |
| Planning horizon | Seconds to minutes | Hours to days (game-time) |
| Cognitive cycle | 100-500ms | 1-10s (gods think slower, act bigger) |

### Puppetmaster God Behavior Template

A regional watcher god's behavior document typically includes:

- **Domain perception filter**: Which event types this god cares about
- **Archive evaluation logic**: How to score an archive's relevance to this god's domain
- **Narrative commissioning**: When and how to call Storyline
- **Scenario instantiation**: How to translate StorylinePlans into game entities
- **Blessing economy**: When to spend divinity on blessings
- **Environmental orchestration**: Weather manipulation, resource abundance changes (via Environment/Worldstate)
- **NPC guidance**: Injecting perceptions into nearby NPCs to create narrative opportunities

### Gardener God Behavior Template

A per-player gardener god's behavior document typically includes:

- **Player profile reading**: Guardian seed capabilities, Agency manifest, Disposition data
- **POI spawning logic**: What scenarios to offer based on player profile and current garden type
- **Scenario management**: Setup, monitoring, completion, growth awards
- **Spirit influence routing**: Receiving player nudges, modulating/forwarding to character Actor
- **Difficulty adaptation**: Adjusting scenario parameters based on player success/failure patterns
- **Transition management**: When to offer garden type transitions (void -> scenario -> persistent world)

---

## Failure Modes and Recovery

| Failure | Detection | Recovery |
|---------|-----------|----------|
| God-actor crash | Manager heartbeat timeout | Manager respawns god from behavior document; Divine state persists |
| Manager actor crash | Puppetmaster/Gardener plugin health check | Plugin respawns manager; manager re-initializes from Divine state |
| Pool node failure | Orchestrator health monitoring | Actor migrates gods to healthy nodes (requires actor migration, Issue #393) |
| Behavior document corruption | ABML compiler validation | Fallback to last-known-good behavior from Resource seeded data |
| Divine service unavailable | God-actor API call failure | God-actor enters degraded mode: continue perceiving but defer actions requiring Divine |
| Storyline service unavailable | God-actor API call failure | God-actor queues archive evaluation; retries when Storyline recovers |

---

## Scaling Considerations

| Concern | Strategy |
|---------|----------|
| God count | ~6-12 puppetmaster gods per realm (one per deity per realm) + ~1 gardener god per 10-50 connected players |
| Pool distribution | Gods distribute across Actor pool nodes alongside NPC actors |
| Perception throughput | Gods subscribe to filtered event streams (not all events); bounded perception queues apply |
| API call rate | Gods think on 1-10s cycles (much slower than NPC 100-500ms), generating modest API load |
| Archive evaluation | Debounced: gods don't evaluate every death individually; batch evaluation on perception tick |
| Manifest push frequency | Debounced by Agency (500ms default); gods don't trigger manifest push directly |

At the 100,000+ NPC scale target:
- ~50 puppetmaster god-actors (6 deities x 8 realms)
- ~200 gardener god-actors (10,000 concurrent players / 50 players per god)
- Total: ~250 god-actors alongside 100,000 NPC actors = 0.25% overhead

---

*This document describes a cross-cutting architectural pattern, not a service. For the services involved, see [PUPPETMASTER.md](../plugins/PUPPETMASTER.md), [GARDENER.md](../plugins/GARDENER.md), [DIVINE.md](../plugins/DIVINE.md), and [ACTOR.md](../plugins/ACTOR.md). For the UX capability manifest system that gods interact with, see [AGENCY.md](../plugins/AGENCY.md).*
