# THE DREAM: Procedural Cinematic Exchanges

> **Status**: VISION DOCUMENT
> **Created**: 2025-12-28
> **Related**: [ABML_V2_DESIGN_PROPOSAL.md](./ABML_V2_DESIGN_PROPOSAL.md), [UPCOMING_-_ACTORS_PLUGIN_V2.md](./UPCOMING_-_ACTORS_PLUGIN_V2.md), [UPCOMING_-_BEHAVIOR_PLUGIN_V2.md](./UPCOMING_-_BEHAVIOR_PLUGIN_V2.md), [UPCOMING_-_MAP_SERVICE.md](./UPCOMING_-_MAP_SERVICE.md)

---

## 1. The Vision

Traditional game development faces an impossible choice: **scripted cinematics** that feel authored but require fixed environments, or **emergent gameplay** that adapts to the world but feels mechanical and unpolished.

**The Dream** is a third path: **procedural cinematic exchanges** - combat sequences that *feel* choreographed, with handshakes and dodging blows and environmental kills, but are actually generated in real-time from what's actually around, not from what a level designer placed specifically for that moment.

### 1.1 The Core Insight

The key innovation is separating three concerns that traditional games conflate:

| Traditional | The Dream |
|-------------|-----------|
| Level designer places "cinematic trigger zone" | Environment exists independently; combat discovers affordances |
| QTE sequence is pre-authored | Options generated from capability + opportunity |
| Fixed camera angles for dramatic moments | Camera finds drama from actual positions |
| Specific objects required for specific moves | Move vocabulary adapts to available objects |

**The bridge**: An invisible orchestrator - the **Event Brain** - that watches the exchange, continuously queries the environment, and dynamically scripts options for participants while maintaining the *illusion* of authored cinematics.

### 1.2 What This Enables

- **Truly Living Worlds**: NPCs can have dramatic, memorable fights anywhere, not just in designated arenas
- **Emergent Drama**: Deus-ex-machina moments when a chandelier happens to be above, or a power conduit is exposed
- **Participant Asymmetry**: Different combatants see different options based on their unique capabilities
- **N-Way Exchanges**: Not limited to duels - multiple participants, bystanders in danger, shifting alliances
- **Contextual Impossibilities**: Low mana means no big spells; falling means grapple-only if grapple points exist
- **Crisis Moments**: Third parties pulled into crossfire, sudden power-ups, environmental collapse

---

## 2. The Character Agent Co-Pilot Pattern

Before diving into architecture, there's a crucial insight that makes THE DREAM not just possible but *elegant*: **every character already has an agent running**.

### 2.1 Dual Agency: Player as Possessor

In Arcadia, players don't control characters directly - they **possess** them as "guardian spirits". The character has their own agent (NPC brain) that:
- Is always running, always perceiving, always computing
- Has intimate knowledge of the character's capabilities, state, and preferences
- Makes decisions when the player doesn't (or can't respond fast enough)
- Maintains the character's personality and behavioral patterns

When a player connects, they take **priority** over the agent's decisions, but the agent doesn't stop - it watches, computes, and waits.

### 2.2 What This Means for Combat Exchanges

The Event Brain doesn't operate alone - it has **helpers**:

```
┌─────────────────────────────────────────────────────────────────┐
│                         EVENT BRAIN                             │
│                   (Exchange Orchestrator)                       │
│                                                                 │
│  "What options are available for Character A?"                  │
│                          │                                      │
│                          ▼                                      │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │              CHARACTER A's AGENT                          │ │
│  │  "I'm low on stamina, my sword arm is injured, but I'm    │ │
│  │   enraged at this opponent. I CAN: defensive stance,      │ │
│  │   desperate lunge, call for help. I PREFER: desperate     │ │
│  │   lunge (rage overrides caution). I WOULD: desperate      │ │
│  │   lunge if player doesn't respond in time."               │ │
│  └───────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

**Key implications**:

| Aspect | Without Character Agent | With Character Agent |
|--------|-------------------------|----------------------|
| Capability discovery | Static database lookup | Dynamic query to agent with full context |
| Option filtering | Generic preconditions | Contextual preferences + current state |
| QTE default | Generic fallback | What THIS character would do |
| Latency compensation | None | Agent pre-computes response |
| Auto-mode transition | Jarring handoff | Seamless - agent was always watching |

### 2.3 The Pre-Computation Advantage

Here's the magic: when the Event Brain presents a QTE to a player, **the character agent has already computed its answer**.

```
Timeline:
─────────────────────────────────────────────────────────────────►
                    │                    │                  │
                    │                    │                  │
              QTE Presented     Agent Response      Timeout
                    │             Ready              Deadline
                    │                    │                  │
                    │    [Player Window] │   [Agent Answer] │
                    │◄──────────────────►│◄────────────────►│
                    │                    │                  │
                    │  Player: "DODGE!"  │  Agent: "lunge"  │
                    │         or         │   (if player     │
                    │  Player: too slow  │   misses window) │
```

The player isn't racing the clock alone - they're racing their character's own decision-making. If they're faster, great. If not, the character acts **in character**.

### 2.4 Personality Through Failure

This creates emergent personality expression in combat:
- **Aggressive character**: QTE timeout → attack anyway
- **Cautious character**: QTE timeout → defensive stance
- **Loyal character**: QTE timeout → protect ally first
- **Panicked character**: QTE timeout → random flailing

The character's nature shows through even when (especially when) the player fails to respond.

---

## 3. Hierarchical Event System

Event Agents aren't just fight coordinators - they're **drama coordinators** for any significant situation. They form a hierarchy from world-scale events down to individual exchanges.

### 3.1 Event Agent Hierarchy

```
┌─────────────────────────────────────────────────────────────────┐
│              WORLD-LEVEL / REGIONAL WATCHER                     │
│         (Placement Service / lib-actors coordination)           │
│                                                                 │
│  Monitors: proximity, antagonism, dramatic potential            │
│  Spawns: Event Agents when interestingness thresholds crossed   │
│                                                                 │
│  Always-on Event Agents for VIPs:                               │
│  - Kings, god avatars, elder dragons                            │
│  - At sufficient power level, EVERYTHING is an event            │
└──────────────────────────────┬──────────────────────────────────┘
                               │
            spawns             │              spawns
       ┌───────────────────────┼───────────────────────────┐
       ▼                       ▼                           ▼
┌─────────────┐      ┌─────────────────┐        ┌─────────────────┐
│  FESTIVAL   │      │    DISASTER     │        │  CONFRONTATION  │
│  DAY EVENT  │      │     EVENT       │        │     EVENT       │
│             │      │                 │        │                 │
│ The day     │      │ Earthquake,     │        │ Two rivals      │
│ itself is   │      │ dragon attack,  │        │ finally meet    │
│ an event    │      │ magical storm   │        │                 │
└──────┬──────┘      └────────┬────────┘        └────────┬────────┘
       │                      │                          │
       │ spawns sub-events    │                          │
       ▼                      ▼                          ▼
┌─────────────┐      ┌─────────────────┐        ┌─────────────────┐
│ Market      │      │ Building        │        │ Cinematic       │
│ Brawl       │      │ Collapse        │        │ Exchange        │
│ Event       │      │ Event           │        │ (Fight)         │
└─────────────┘      └─────────────────┘        └─────────────────┘
```

### 3.2 Interestingness Triggers

Event Agents are spawned when the Regional Watcher detects:

- **Power level proximity**: Matched combatants (interesting fight potential)
- **Antagonism score**: Relationship system indicates hatred/rivalry
- **Environmental drama**: Near hazards, in public, at significant location
- **Story flags**: Characters with narrative significance
- **Player involvement**: Human players make things interesting by default
- **VIP presence**: Some characters ALWAYS have an Event Agent (kings, elder dragons, god avatars)

### 3.3 Spawning via Orchestrator

Event Agent spawning uses the **same pattern as lib-assets** for spinning up specialized processing instances:

```
┌─────────────────────────────────────────────────────────────────┐
│              REGIONAL WATCHER / CONTROL PLANE                   │
│                                                                 │
│  "Interesting situation detected. Spawn Event Agent."          │
│                          │                                      │
│                          ▼                                      │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │                    ORCHESTRATOR API                       │ │
│  │  "Spin up Bannou instance with event-agent plugin only"   │ │
│  └───────────────────────────────────────────────────────────┘ │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
            ┌─────────────────────────────────────┐
            │  New Bannou Instance                │
            │  app-id: event-agent-{unique}       │
            │  plugins: [event-agent]             │
            │                                     │
            │  Control plane makes API calls      │
            │  using the unique app-id            │
            └─────────────────────────────────────┘
```

**Same pattern used for**:
- Asset Service spinning up asset-processing instances
- Actor pool expansion when more slots needed
- Event Agent spawning

**Reference**: See lib-assets and Orchestrator integration for existing implementation (note: not yet fully tested, may have kinks to work out).

### 3.4 Combat Without Event Agents

Fights happen constantly without Event Agents - and that's fine. A level 50 character stomping bandits doesn't need cinematic treatment. Normal combat:

- Handled entirely by game engine/server
- Where Winds Meet / Dark Souls style action combat
- Direct UDP connection for minimal latency
- Character agent provides defaults, occasional overrides
- No Bannou hops required

**Event Agents ENHANCE already-good combat when the situation warrants it.**

---

## 4. Game Engine Integration: The Three-Version Solution

The eternal problem with cinematic combat in multiplayer: **you can't have slow-mo when other players exist**.

The solution: **temporal desync with three coexisting versions of events**.

### 4.1 Three Simultaneous Versions

When an Event Agent initiates a cinematic exchange, the game engine maintains three versions:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         SINGLE BATTLEFIELD                              │
│                   (Three coexisting versions)                           │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ VERSION 1: Normal Battlefield (Non-Participants)                │   │
│  │                                                                 │   │
│  │  - Real-time combat for everyone NOT in cinematic              │   │
│  │  - Cannot interact with cinematic participants                 │   │
│  │  - Sees VERSION 2 (projection) of participants                 │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ VERSION 2: Delayed Projection (What spectators see)             │   │
│  │                                                                 │   │
│  │  - The "ghost" of cinematic participants                       │   │
│  │  - Simplified/delayed animations of the exchange               │   │
│  │  - Collision-disabled with non-participants                    │   │
│  │  - Slightly behind real-time (participants are ahead)          │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ VERSION 3: Real-Time Actual (What participants experience)      │   │
│  │                                                                 │   │
│  │  - The "true" state of the exchange                            │   │
│  │  - Only visible to participants themselves                     │   │
│  │  - Camera breaks away, physics/animations jump ahead           │   │
│  │  - Enables slow-mo decision moments                            │   │
│  └─────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

### 4.2 Temporal Budget: Earning Slow-Mo Time

The key insight: **participants "earn" slow-mo time by fast-forwarding through setup**.

```
Real World Time:  |----1s----|----2s----|----3s----|----4s----|----5s----|

Spectators See:   |--fight---|--fight---|--fight---|--fight---|--fight---|
                  (normal)   (normal)   (normal)   (normal)   (normal)

Participants      |==FAST====|==FAST====|                     |--catch---|
Experience:       (jumped    (ahead)                          (up/sync)
                   ahead)        ↓
                         They've "earned" ~2 seconds of time budget
                                 ↓
                                 |====SLOW-MO QTE MOMENT==========|
                                 (spending time budget on decisions)
```

**Timeline for participant**:
1. Camera breaks away from normal view
2. Physics/animations jump FORWARD 1+ seconds faster than real-time
3. This creates "time budget" - temporal credit
4. During decision moments, time slows down (spending the budget)
5. Player makes QTE choices in perceived slow-mo
6. Catch-up/sync back to real-time when exchange ends

**From spectators' perspective**: They see a slightly simplified, slightly delayed version of the fight. Nothing looks wrong - just less detailed than what participants experience.

### 4.3 Responsibility Split: Event Agent vs Game Engine

| Concern | Owner | Notes |
|---------|-------|-------|
| Deciding cinematic should happen | Event Agent | Based on tapped events, map context |
| What the cinematic beat IS | Event Agent | Options, outcomes, choreography |
| Temporal desync mechanics | Game Engine | Fast-forward, slow-mo, catch-up |
| Rendering three versions | Game Engine | Projection vs actual |
| Collision isolation | Game Engine | Participants can't be interfered with |
| QTE input handling | Game Engine | Low-latency path, Event Agent uninvolved? |
| Presenting options/defaults | Event Agent | Via character agents |
| Outcome resolution | Event Agent | Applies results back to world state |

### 4.4 Game Engine as Reliable Fallback

**Resolved**: Event Agent provides options/defaults to game engine. Game engine handles actual input capture (lowest latency path).

```
Event Agent                          Game Engine
     │                                    │
     │  "Here's a QTE: options A/B/C,     │
     │   defaults, expected duration"     │
     │───────────────────────────────────►│
     │                                    │  ← Game engine can complete
     │                                    │    this QTE independently
     │  "Extension: add option D,         │
     │   new dramatic act available"      │
     │───────────────────────────────────►│  ← If arrives in time: richer QTE
     │                                    │  ← If late/missing: no harm
     │                                    │
     │              (Event Agent offline) │
     │                    ✗               │
     │                                    │  ← Game engine still completes
     │                                    │    QTE with original plan
```

**Key principle**: The game engine is always capable of completing the cinematic with what it was initially given. Event Agent *enriches* but isn't required for completion. This provides:

- **Graceful degradation**: Event Agent crash doesn't break combat
- **Latency tolerance**: Extensions that arrive late are simply not used
- **Extensibility**: Event Agent can push new "dramatic acts" mid-sequence if conditions warrant

---

## 5. Map Service as Context Brain

The Map Service isn't just "what objects are nearby" - it's the **queryable world state** that Event Agents use to understand context.

### 5.1 Rich Queryable Layers

```
Map Layers (queryable by Event Agents):
├── significant_individuals    # Characters above power threshold
├── mana_density              # Magical environment state
├── construction_activity     # What's being built/destroyed
├── hazard_zones              # Active dangers
├── crowd_density             # Where people are gathered
├── faction_territory         # Political boundaries
├── weather_effects           # Environmental conditions
├── story_markers             # Narrative-significant locations
└── [custom per-context]      # Event Agents can request new layers
```

### 5.2 Data Flow: Game Engines → Maps → Event Agents

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Game Engines   │     │   Aggregators   │     │   Map Service   │
│  (Sensory       │────►│   (Reduce       │────►│   (Persisted    │
│   Organs)       │     │    Noise)       │     │    Context)     │
└─────────────────┘     └─────────────────┘     └────────┬────────┘
                                                         │
        Game servers produce tons of raw data            │ queries
        about every individual, every action             │
                                                         ▼
                                                ┌─────────────────┐
                                                │  Event Agents   │
                                                │  (Query for     │
                                                │   context)      │
                                                └─────────────────┘
```

Event Agents don't need to track everything themselves - they query the Map Service for context when they need it, and subscribe to delta events for changes.

### 5.3 The Event Tap Pattern

Event Agents can "tap" specific actors to receive a subset of their events. This uses the **same infrastructure as Connect/client eventing** - RabbitMQ fanout exchange queues with routing_id.

```
                    RabbitMQ Fanout Exchange
                    (routing_id = character-123)
                              │
              ┌───────────────┼───────────────┐
              │               │               │
              ▼               ▼               ▼
       ┌─────────────┐ ┌─────────────┐ ┌─────────────┐
       │  Character  │ │   Player    │ │   Event     │
       │   Agent     │ │   Client    │ │   Agent     │
       │  (NPC side) │ │  (if any)   │ │   (tap)     │
       └─────────────┘ └─────────────┘ └─────────────┘
```

**Key insight**: This isn't a new pattern. When a player "takes over" a character in Arcadia, they're already tapping into the same event stream the character agent was getting. Event Agents are just another subscriber.

**Two tap points available**:
1. **Character agent stream**: Combat-relevant events for that specific character
2. **Player client stream**: More events (other characters they manage, household events, etc.)

**Why tap instead of query?**
- Queries are pull-based, good for context gathering
- Taps are push-based, good for real-time reaction
- Event Agent uses BOTH: queries for broad context, taps for moment-to-moment awareness

**Implementation**: Existing lib-messaging fanout exchange pattern, no new infrastructure needed.

---

## 6. Bannou Service Foundation

The Dream builds on four Bannou systems that, combined, provide everything needed:

```
                    ┌─────────────────────────────────────┐
                    │         THE EVENT BRAIN             │
                    │   (Exchange Orchestrator Actor)     │
                    └──────────────┬──────────────────────┘
                                   │
           ┌───────────────────────┼───────────────────────┐
           │                       │                       │
           ▼                       ▼                       ▼
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   MAP SERVICE   │    │ BEHAVIOR PLUGIN │    │  ACTORS PLUGIN  │
│ (Spatial Query) │    │ (GOAP + ABML)   │    │ (Distribution)  │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         │                       │                       │
         └───────────────────────┴───────────────────────┘
                                 │
                    ┌────────────┴────────────┐
                    │   CHARACTER AGENTS      │
                    │  (Always-On Co-Pilots)  │
                    │                         │
                    │   ┌─────┐   ┌─────┐    │
                    │   │NPC A│   │NPC B│    │
                    │   └──┬──┘   └──┬──┘    │
                    │      │        │        │
                    │   Player?  Player?     │
                    │   (maybe)  (maybe)     │
                    └─────────────────────────┘
```

Note: "Character Agents" are the NPC brains - they're always running whether a player is connected or not. Players *possess* characters, taking priority over agent decisions but not replacing the agent.

### 6.1 Map Service (Spatial Awareness)

The Map Service provides environmental discovery - the "what's around" that makes procedural cinematics possible:

**Affordance Queries**:
```yaml
POST /maps/metadata/list
{
  "mapId": "dungeon-17",
  "bounds": { "minX": 10, "minY": 20, "maxX": 30, "maxY": 40 },
  "objectType": ["throwable", "climbable-surface", "breakable", "hazard"]
}
```

Returns objects with their spatial positions, states, and types. The Event Brain can ask "what throwable objects are between fighter A and B?" or "are there any ledges within grapple range?"

**Real-Time Subscriptions**: Subscribe to spatial regions to detect when environment changes mid-fight (objects destroyed, new hazards appear, terrain collapses).

**Ephemeral Combat Layers**: Write temporary combat effects (blast radius, danger zones) that automatically expire.

### 6.2 Actors Plugin (Distributed State)

The Actor model provides the infrastructure for the Event Brain and participants:

**Event Brain Actor** (`combat-brain:exchange-{id}`):
- Single authoritative instance for the exchange
- Turn-based message processing ensures no race conditions
- State includes: participants, phase, combat log, timing, environmental cache
- Scheduled callbacks for phase timeouts and tick processing

**Participant Actors** (`npc-brain:{id}` or `player-session:{id}`):
- Receive option presentations from Event Brain
- Send decisions back
- Personal channels for direct messaging

**Communication Patterns**:
- Fire-and-forget for non-critical updates (animation hints)
- Request-response for synchronous decision resolution
- Event subscriptions for broadcast state changes

### 6.3 Behavior Plugin (Decision Engine)

GOAP planning evaluates which options each participant can pursue:

**Option Filtering**: Given current combat state (stamina, position, equipped items, cooldowns), GOAP preconditions automatically filter to valid options only.

**Cost Evaluation**: Options can be ranked by strategic value:
- Defensive options when low health
- Aggressive options when opponent is staggered
- Environmental options when positioning is favorable

**Cognition Pipeline**: Perception processing determines what each participant is aware of - you can only dodge what you've perceived.

### 6.4 ABML (Choreography Language)

ABML provides the vocabulary for expressing combat sequences:

**Multi-Channel Execution**: Attacker, defender, camera, audio all run parallel tracks with sync points.

**QTE Framework**: Built-in support for timed input windows with success/failure branching.

**Sync Points**: `emit`/`wait_for` ensure proper timing between participants.

---

## 7. The Event Brain

The Event Brain is the invisible director - it doesn't fight, but it *scripts* the fight in real-time.

### 7.1 Responsibilities

| Responsibility | Implementation |
|----------------|----------------|
| Discover environment | Query Map Service for affordances in combat bounds |
| Track combat state | Authoritative state machine: setup → exchange → resolution |
| Generate options | Build valid option sets from capabilities × opportunities |
| Present choices | Send QTE prompts to participants with timing windows |
| Resolve outcomes | Evaluate choices, apply effects, update state |
| Choreograph result | Emit ABML channel instructions for animation/camera/audio |
| Handle interruptions | Detect and integrate crisis moments (third parties, power-ups) |

### 7.2 Event Brain State

```yaml
EventBrainState:
  exchange_id: string
  participants:
    - actor_address: string           # npc-brain:char-123
      role: attacker | defender | bystander
      capabilities: string[]          # combat_roll, magic_missile, grapple
      current_state: object           # health, stamina, position, etc.

  environment:
    bounds: { minX, minY, maxX, maxY }
    affordances: Affordance[]         # Cached environmental objects
    hazards: Hazard[]                 # Active dangers
    last_query_time: timestamp

  exchange_state:
    phase: setup | action_selection | resolution | aftermath
    current_beat: int                 # Exchange progress
    initiative_order: string[]        # Who acts when
    pending_actions: PendingAction[]  # Queued for resolution

  combat_log: CombatEvent[]           # History for narrative coherence

  timing:
    phase_started: timestamp
    phase_timeout_ms: int
    option_window_ms: int
```

### 7.3 Exchange Protocol

The Event Brain runs a state machine for each combat beat:

```
┌─────────────────────────────────────────────────────────────┐
│                        EXCHANGE BEAT                        │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────┐    ┌──────────────┐    ┌────────────────┐     │
│  │  SETUP  │───►│   OPTIONS    │───►│   RESOLUTION   │     │
│  │         │    │  GENERATION  │    │                │     │
│  └─────────┘    └──────┬───────┘    └───────┬────────┘     │
│       │                │                    │              │
│       ▼                ▼                    ▼              │
│  Query env        Generate &          Resolve actions      │
│  Cache afford.    present QTEs        Apply effects        │
│  Assign init.     Wait for input      Update positions     │
│                   or timeout          Emit choreography    │
│                                                             │
│                          │                                  │
│                          ▼                                  │
│               ┌──────────────────┐                         │
│               │  CONTINUE OR     │                         │
│               │  END EXCHANGE?   │                         │
│               └──────────────────┘                         │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 7.4 Option Generation Algorithm (Character Agent Query Pattern)

This is the core innovation - the Event Brain doesn't compute options alone. It **queries character agents** who have intimate knowledge of their capabilities, state, and preferences.

```
GENERATE_OPTIONS(participant, combat_state, affordances):

  # 1. Query the CHARACTER AGENT - not a static database!
  #    The agent knows current state, equipment, injuries, emotional state, etc.
  agent_response = await participant.agent.QueryCombatOptions({
    combat_context: combat_state,
    nearby_affordances: affordances,
    time_pressure: combat_state.urgency
  })

  # Agent returns three things:
  #   - available_options: What CAN this character do right now?
  #   - preferred_option: What WOULD this character do? (QTE default)
  #   - option_preferences: Scoring adjustments based on personality

  # 2. The agent has already filtered by:
  #    - Current stamina, mana, health
  #    - Equipment state (broken sword = no sword moves)
  #    - Injuries (damaged arm = reduced throw options)
  #    - Emotional state (enraged = aggressive options preferred)
  #    - Relationship to opponent (rival = personal grudge moves available)

  available_options = agent_response.available_options
  preferred_option = agent_response.preferred_option  # <-- QTE timeout default!

  # 3. Match agent capabilities to environmental opportunities
  #    (This part is still Event Brain's responsibility)
  environmental_options = []
  for option in available_options:
    if option.requires_object_type:
      matching_objects = affordances.filter(type=option.requires_object_type)
      for obj in matching_objects:
        if in_range(participant.position, obj.position, option.range):
          environmental_options.append(EnvironmentalOption(option, obj))
    else:
      environmental_options.append(option)

  # 4. Score using BOTH strategic value AND agent preferences
  for option in environmental_options:
    strategic_score = evaluate_strategic_value(option, combat_state)
    personality_score = agent_response.option_preferences.get(option.id, 0)
    option.score = strategic_score + personality_score

  # 5. Select top N for QTE presentation
  final_options = environmental_options.sorted_by_score().take(MAX_OPTIONS)

  # 6. Attach the agent's preferred choice as the timeout default
  return {
    options: final_options,
    timeout_default: preferred_option,  # <-- Character acts IN CHARACTER on timeout
    agent_confidence: agent_response.confidence
  }
```

**The Character Agent Query** is the key insight. Instead of the Event Brain maintaining a centralized capability database, it asks each character's agent:

> "Given this combat situation and these nearby objects, what can you do and what would you do?"

The agent responds with full context:
- **Physical state**: Stamina 30%, mana 80%, sword arm injured
- **Emotional state**: Enraged at this specific opponent
- **Preferences**: Prefers aggressive options when angry
- **Default choice**: "I would do a desperate lunge" (not generic "block")

This makes QTE timeouts **character-appropriate**, not generic fallbacks.

### 7.5 Crisis Moment Detection

The Event Brain monitors for dramatic opportunities:

**Environmental Collapse**: Map subscription detects when terrain changes - incorporate into options.

**Third Party Entry**: Perception events from nearby actors - offer "protect bystander" or "use as shield" options.

**Power Surge**: Game events (magical interference, equipment breaking) - unlock temporary super-moves.

**Near-Death Moments**: Participant health drops critically - offer "last stand" options with dramatic camera.

---

## 8. ABML Extensions for Procedural Exchanges

Current ABML is designed for **authoring** fixed sequences. The Dream requires **runtime generation** and **environmental binding**. These extensions preserve ABML's philosophy while enabling dynamic content.

### 8.1 Environmental Query Actions

New action type that queries Map Service and binds results to variables:

```yaml
- query_environment:
    query_type: affordance_search
    bounds: "${combat_bounds}"
    object_types: ["throwable", "climbable", "breakable"]
    max_results: 10
    result_variable: nearby_affordances

# Use in conditions
- cond:
    - when: "${length(nearby_affordances.throwables) > 0}"
      then:
        - offer_option: throw_object
```

### 8.2 Dynamic Option Presentation

Extension to `choice` action for runtime-generated options:

```yaml
- dynamic_choice:
    prompt: "Combat action:"
    options_source: "${generated_options}"
    timeout: "${option_window_ms}ms"
    on_timeout: defensive_default

    # Each option in options_source has structure:
    # { id, label, description, capability, target?, object?, score }

    on_selection:
      - set: { variable: chosen_option, value: "${selection}" }
      - emit: option_chosen
```

### 8.3 Exchange Protocol Primitives

New primitives for managing multi-participant exchanges:

```yaml
# Declare participant in exchange
- join_exchange:
    exchange_id: "${exchange_id}"
    role: defender
    capabilities: "${self.combat_capabilities}"

# Wait for exchange phase
- wait_for_phase:
    phase: action_selection
    timeout: 5s

# Submit action to exchange
- submit_action:
    exchange_id: "${exchange_id}"
    action: "${chosen_option.capability}"
    target: "${chosen_option.target}"
    object: "${chosen_option.object}"

# Receive choreography instructions from Event Brain
- receive_choreography:
    result_variable: my_choreography
    # Contains: animation, timing, sync_points, camera_hints
```

### 8.4 Affordance Binding

Connect environmental objects to available actions:

```yaml
# Define affordance requirements on capabilities
capabilities:
  throw_object:
    requires:
      object_type: throwable
      object_in_range: 3.0  # meters
    produces_options:
      per_matching_object: true
      option_template:
        label: "Throw {{ object.name }}"
        description: "Hurl {{ object.name }} at opponent"
        damage_base: "${object.mass * 5}"

  wall_slam:
    requires:
      surface_type: solid_wall
      opponent_in_range: 2.0
      opponent_toward_surface: true
    produces_options:
      option_template:
        label: "Slam against wall"
        description: "Drive opponent into {{ surface.name }}"
```

### 8.5 Participant Discovery Channels

Dynamic channel creation based on exchange participants:

```yaml
# Template for exchange ABML
# Channels generated at runtime based on participants

channels_template:
  event_brain:
    - discover_affordances
    - for_each:
        variable: participant
        collection: "${participants}"
        do:
          - generate_options: { for: "${participant.id}" }
          - send_options:
              to: "${participant.channel}"
              options: "${participant.options}"
    - wait_for:
        all_of: "${participants.map(p => p.id + '.option_chosen')}"
        timeout: "${option_window_ms}ms"
    - resolve_exchange_beat
    - emit_choreography

  # Generated dynamically for each participant
  participant_{id}:
    - receive_options
    - present_qte
    - await_input_or_timeout
    - emit: option_chosen
    - receive_choreography
    - execute_choreography
```

---

## 9. Implementation Challenges

These are the hardest problems to solve, roughly ordered by difficulty:

### 9.1 Making Procedural Feel Authored (Highest Difficulty)

**The Problem**: Truly random option selection feels chaotic. Authored cinematics feel polished because every beat is *designed*. How do we bridge this gap?

**Approaches**:
- **Beat Templates**: Pre-authored patterns (parry-riposte, feint-commit, environmental kill) that the Event Brain selects and parameterizes
- **Dramatic Pacing**: State machine that ensures variety - can't have three throws in a row, tension must build
- **Camera Intelligence**: Camera tracks that find drama in actual positions, not fixed angles
- **Outcome Weighting**: Bias toward dramatic outcomes (near-misses, last-second saves) without being predictable

### 9.2 Real-Time Synchronization (High Difficulty)

**The Problem**: Multiple participants on different nodes need sub-second response times for QTE windows to feel responsive.

**Approaches**:
- **Latency Budgeting**: 250ms network + 100ms processing = 350ms minimum; design around this
- **Optimistic Execution**: Start animations before confirmation; correct on mismatch
- **Event Brain Locality**: Place Event Brain on same node as most participants when possible
- **Graceful Degradation**: If response is late, default to contextually sensible option

### 9.3 Environmental Affordance Detection (Medium-High Difficulty)

**The Problem**: Objects in the world need to be understood as "throwable" or "climbable" or "breakable". This requires either extensive tagging or intelligent inference.

**Approaches**:
- **Type Registry**: Object schemas include affordance tags; Map Service validates against registry
- **Physics-Based Inference**: Mass < X and not attached → potentially throwable
- **Proximity Rules**: Within N meters of ledge + falling → grapple available
- **Composite Affordances**: "push opponent into hazard" requires opponent + hazard + relative positioning

### 9.4 Option Explosion (Medium Difficulty)

**The Problem**: With 5 capabilities × 10 environmental objects × 3 opponents = 150 potential options. Can't present all of these.

**Approaches**:
- **Strategic Filtering**: GOAP cost evaluation prunes low-value options
- **Contextual Limits**: Max 3-4 options per QTE prompt
- **Category Balancing**: Ensure mix of offensive/defensive/environmental
- **Cooldown Suppression**: Recently-used options scored lower

### 9.5 Multi-Participant Scaling (Medium Difficulty)

**The Problem**: Duels are tractable. What about 3v3 battles? 10-person brawls?

**Approaches**:
- **Focus Management**: Only some participants are "in exchange" at once; others continue autonomous behavior
- **Sub-Exchange Decomposition**: Large battles decompose into multiple concurrent exchanges
- **Attention Budget**: Event Brain has limited attention; manages complexity
- **Role Specialization**: Bystanders have simpler option sets than primary combatants

### 9.6 Interruption Coherence (Medium Difficulty)

**The Problem**: When a third party joins, or the environment changes, or a power-up appears, how do we integrate without breaking flow?

**Approaches**:
- **Crisis Queue**: Interruptions queued and integrated at beat boundaries
- **Hot Options**: Some interruptions can be injected mid-beat as new options
- **Graceful Pivot**: Current beat completes with modified outcome; next beat incorporates change
- **Dramatic Framing**: Interruptions become part of the narrative ("As you strike, the floor gives way!")

---

## 10. How This Informs Current Development

Understanding THE DREAM provides guidance for our current implementation work:

### 10.1 Map Service Implications

**Object Type Registry**: The `allowedObjectTypes` and object schema validation should anticipate affordance tagging. Consider designing the type system with combat affordances in mind:

```yaml
# Future-proofing object schemas
ThrowableObject:
  x-affordances:
    - type: throwable
      properties:
        damage_multiplier: { source: "mass" }
        range_base: 10

ClimbableSurface:
  x-affordances:
    - type: climbable
      properties:
        reach_required: 2.0
        supports_grapple: true
```

**Spatial Query Performance**: The Event Brain will query frequently during combat. Index design and query optimization matter.

### 10.2 Actors Plugin Implications

**Event Brain Actor Type**: Design actor schema system anticipating coordinator actors with many-to-many participant relationships.

**Message Latency**: For QTE responsiveness, measure and optimize message routing latency. Consider "combat-priority" message classification.

**State Persistence Granularity**: Exchange state needs frequent saves for crash recovery; design persistence patterns accordingly.

### 10.3 Behavior Plugin Implications

**GOAP Precondition Vocabulary**: Extend world state vocabulary anticipating combat context:
```yaml
world_state_schema:
  # Standard
  health: float
  stamina: float

  # Combat context (future)
  in_exchange: bool
  opponent_staggered: bool
  has_throwable_nearby: bool
  near_ledge: bool
  falling: bool
```

**Action Cost Tuning**: GOAP costs should eventually reflect dramatic value, not just efficiency.

### 10.4 ABML Implications

**Extension Points**: Design the action handler system anticipating the extensions described in Section 8. The handler registry should support:
- Async environmental queries
- Dynamic option generation
- Multi-document coordination

**Runtime Generation**: Consider whether compiled ABML documents can be parameterized at runtime, or whether we need a templating layer above ABML.

---

## 11. Development Phases

The Dream is too large for a single implementation push. Here's a phased approach:

### Phase 1: Foundation (Current Work)
- Complete Map Service with object type registry
- Complete Actors Plugin with coordinator patterns
- Complete Behavior Plugin with GOAP execution
- Finalize ABML v2.0 with current feature set

### Phase 2: Static Exchanges
- Implement Event Brain actor type
- Build exchange protocol (setup → selection → resolution)
- Implement option generation from *capability only* (no environmental)
- Test with fixed 1v1 exchanges

### Phase 3: Environmental Awareness
- Add affordance tags to object schemas
- Implement environmental query actions in ABML
- Extend option generation with environmental matching
- Test environmental kills, throws, positioning moves

### Phase 4: Dynamic Generation
- Implement dynamic option presentation
- Add dramatic pacing state machine
- Build camera intelligence system
- Test feeling of "authored" procedural content

### Phase 5: Multi-Participant
- Extend exchange protocol for N participants
- Implement focus management
- Add crisis interruption handling
- Test multi-combatant scenarios

### Phase 6: Polish
- Optimize latency paths
- Tune dramatic weighting
- Build debugging/replay tools
- Content creator tooling

---

## 12. Success Criteria

How do we know when we've achieved THE DREAM?

**Technical Criteria**:
- Event Brain maintains authoritative state across distributed nodes
- Option generation completes within 50ms
- QTE windows feel responsive (perceived latency < 200ms)
- Environmental queries return in < 20ms
- System handles 3+ participants without degradation

**Experience Criteria**:
- Fights feel choreographed despite being procedural
- Environmental elements are regularly incorporated
- Near-misses and dramatic moments occur naturally
- No two fights in the same location feel identical
- Players cannot predict which options will be available

**Content Criteria**:
- New combat capabilities can be added without code changes
- New environmental object types work automatically
- Beat templates are authorable by designers
- System works in arbitrary environments, not just designed arenas

---

## 13. Conclusion

THE DREAM is ambitious but achievable. Three key insights make it so:

1. **Bannou's architecture provides the primitives**: Distributed actors, schema-first services, spatial queries, behavior planning - all the pieces exist or are being built.

2. **The Character Agent Co-Pilot pattern solves the hard problems**: Because every character already has an always-on agent, we get capability discovery, contextual defaults, latency compensation, and personality expression for free.

3. **The Three-Version Solution enables multiplayer slow-mo**: By maintaining temporal desync between participants and spectators, we solve the eternal problem of cinematic time manipulation in multiplayer games.

What's missing is:
- The orchestration layer (Event Brain hierarchy)
- The environmental binding (affordance system)
- The game engine temporal mechanics (three-version rendering)

These are tractable problems. The Event Brain is an actor type we can build. The affordance system extends the Map Service we're already designing. The temporal mechanics are game engine concerns that don't require Bannou infrastructure changes.

This is not a "someday" feature. This is what makes Arcadia's combat system fundamentally different from every other game. Every piece of infrastructure we're building - Map Service, Actors, Behavior, ABML - should be designed with this destination in mind.

The living world isn't just NPCs having schedules. It's NPCs having *memorable moments* - dramatic fights that emerge from circumstance, not script. And when players are involved, they're not controlling puppets - they're possessing characters with their own combat instincts, ready to act when the player can't.

---

*Document Status: VISION - Informing current development priorities*

## Related Documents

- [ABML_V2_DESIGN_PROPOSAL.md](./ABML_V2_DESIGN_PROPOSAL.md) - Behavior markup language
- [UPCOMING_-_ACTORS_PLUGIN_V2.md](./UPCOMING_-_ACTORS_PLUGIN_V2.md) - Distributed actor system
- [UPCOMING_-_BEHAVIOR_PLUGIN_V2.md](./UPCOMING_-_BEHAVIOR_PLUGIN_V2.md) - GOAP and behavior execution
- [UPCOMING_-_MAP_SERVICE.md](./UPCOMING_-_MAP_SERVICE.md) - Spatial awareness and affordances
- [arcadia-kb: Player-Character Dual Agency](~/repos/arcadia-kb/04%20-%20Game%20Systems/Player-Character%20Dual%20Agency%20Training%20System.md) - Guardian spirit possession model
- [arcadia-kb: Distributed Agent Architecture](~/repos/arcadia-kb/05%20-%20NPC%20AI%20Design/Distributed%20Agent%20Architecture.md) - Avatar/Agent separation
