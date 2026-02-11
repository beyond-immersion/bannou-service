# How Do NPCs Actually Think? (The ABML/GOAP Behavior Stack)

> **Short Answer**: NPCs run a 5-stage cognition pipeline (perceive, appraise, remember, evaluate goals, form intentions) powered by two complementary systems: ABML (Arcadia Behavior Markup Language) for reactive scripted behaviors compiled to portable bytecode, and GOAP (Goal-Oriented Action Planning) for dynamic goal-seeking through A* search over action spaces. The Actor service (L2) executes the bytecode; the Behavior service (L4) compiles it and runs the planner.

---

## The Two Systems and Why Both Exist

### ABML: Reactive Behavior

ABML is a YAML-based domain-specific language for defining NPC behavior. A behavior document describes what an NPC does in response to stimuli: "if threatened and aggressive, draw weapon and confront; if threatened and timid, flee; if idle and hungry, seek food."

The Behavior service compiles ABML documents through a multi-phase compiler into portable stack-based bytecode. This bytecode runs on both the server-side ActorRunner (L2) and client-side SDKs. The dual-target compilation means NPC behavior can be predicted client-side for responsive animation without waiting for server confirmation.

ABML handles the **reactive** dimension of NPC behavior: what to do right now given current perceptions and internal state. It excels at moment-to-moment decision-making with low latency.

### GOAP: Strategic Planning

GOAP (Goal-Oriented Action Planning) uses A* search to find action sequences that transform the current world state into a desired goal state. An NPC who wants to eat finds the plan: go to market, buy food, return home, cook, eat. If the market is closed, the planner finds an alternative: go to garden, harvest vegetables, return home, cook, eat.

The Behavior service runs the GOAP planner. It defines action spaces (available actions with preconditions and effects) and goal states (desired world conditions). The planner searches for the cheapest action sequence that achieves the goal.

GOAP handles the **strategic** dimension of NPC behavior: what to pursue over minutes, hours, or days. It excels at multi-step planning that adapts to changing circumstances.

### Why Both

Pure ABML would produce NPCs that react intelligently to the immediate situation but never pursue long-term goals. A guard would respond well to threats but never think "I should patrol the eastern wall because there were reports of intruders yesterday."

Pure GOAP would produce NPCs that pursue goals strategically but respond sluggishly to moment-to-moment events. A merchant planning a trade route would not react quickly enough to a pickpocket.

The combination means NPCs have both reflexes (ABML) and intentions (GOAP). The cognition pipeline integrates them: GOAP sets the current goal and planned action sequence, ABML evaluates immediate perceptions and can interrupt the plan if something more urgent arises.

---

## The 5-Stage Cognition Pipeline

Every active NPC runs this pipeline on every decision tick (100-500ms):

### Stage 1: Perception

The NPC's bounded perception queue receives events from the world: another character entered the area, a sound was heard, an item appeared, a threat was detected. The perception queue has urgency filtering -- a shout of "Fire!" jumps ahead of ambient market chatter.

Perception is bounded because 100,000 NPCs cannot each process unlimited event streams. Each NPC has a configurable perception window (nearby events only) and a priority queue that drops low-urgency events when full.

### Stage 2: Appraisal

Each perceived event is appraised based on the NPC's internal state. This is where the Variable Provider Factory provides its data. The appraisal evaluates:

- **Personality** (`${personality.aggression}`, `${personality.curiosity}`): How does this character's nature affect their reaction?
- **Encounters** (`${encounters.sentiment_toward_TARGET}`): Do I have history with the entity involved in this event?
- **Backstory** (`${backstory.trauma_count}`): Does this event trigger historical associations?
- **Quest state** (`${quest.active_objective}`): Is this event relevant to my current objectives?

Appraisal converts raw perception events into emotionally and contextually weighted stimuli.

### Stage 3: Memory

Appraised events update the NPC's working memory. Significant events may be committed to long-term storage via Character Encounter (if the interaction was memorable) or Character History (if the event was historically significant).

Memory also provides context for ongoing situations: "I have been in this conversation for 3 exchanges" or "I started fleeing 10 seconds ago and am now safe."

### Stage 4: Goal Evaluation

The NPC's current goals are re-evaluated against the updated world model. GOAP replans if:

- The current plan's preconditions are no longer met (the market closed while I was walking there)
- A higher-priority goal has emerged (I was going to trade, but now I am being attacked)
- The current goal has been achieved (I wanted food, I have food)

Goal evaluation does not necessarily change the plan every tick. If the current plan is still valid and no higher-priority goal exists, the NPC continues executing.

### Stage 5: Intention Formation

The NPC commits to a specific action for this tick. This is the output of the cognition pipeline: a concrete behavior to execute. ABML bytecode evaluates the current state and selected goal to determine the exact action (move to location, speak dialogue, attack target, use item, wait).

The intention is sent to the game world for execution. The Actor service publishes the behavioral output, and game clients animate accordingly.

---

## The Compilation and Execution Split

There is a deliberate architectural separation between **compiling** behavior and **executing** behavior:

| Concern | Service | Layer |
|---------|---------|-------|
| Compiling ABML to bytecode | Behavior | L4 |
| GOAP planning | Behavior | L4 |
| Executing bytecode | Actor | L2 |
| Dynamic behavior loading | Puppetmaster | L4 |

This split exists because:

- **Actor (L2) is foundational.** It must always be running when NPCs exist. It is the runtime -- it interprets bytecode, manages perception queues, and coordinates the cognition pipeline.
- **Behavior (L4) is optional.** It compiles ABML and runs the planner. In a deployment that only needs simple NPCs with pre-compiled behaviors, Behavior could be disabled. Actor would still execute pre-compiled bytecode.
- **Puppetmaster (L4) bridges the gap.** Actor (L2) cannot depend on Asset (L3) for loading behavior documents -- that would be a hierarchy violation. Puppetmaster implements `IBehaviorDocumentProvider`, loading behaviors from the Asset service and making them available to Actor through the provider pattern.

This means the system supports multiple complexity tiers:

- **Minimal**: Actor runs pre-compiled bytecode with no runtime compilation or dynamic loading.
- **Standard**: Behavior compiles ABML on demand and Actor executes the results.
- **Full**: Puppetmaster dynamically loads and hot-reloads behaviors, Behavior compiles and plans, Actor executes with full cognition pipeline.

---

## Scaling to 100,000+ NPCs

The architecture is designed for this specific target:

- **Zero-allocation bytecode VM**: The Actor runtime executes ABML bytecode without heap allocations during the hot loop. The VM operates on a pre-allocated stack.
- **Bounded perception**: Each NPC processes only nearby events with urgency filtering, preventing the perception system from scaling with the total event volume of the world.
- **Variable Provider caching**: L4 providers cache personality, encounter, and history data. The Actor runtime reads from these caches, not from service calls on every tick.
- **Pool deployment modes**: Actors can be distributed across processing pools managed by the Orchestrator. When load increases, new pools spin up. The Actor service supports local, pool-per-type, shared-pool, and auto-scale modes.
- **Direct RabbitMQ event delivery**: NPC perception events are delivered via lib-messaging, not HTTP calls. This provides the throughput needed for 100,000+ entities receiving events continuously.

The Behavior service's GOAP planner is the most computationally expensive component, but it runs infrequently (replanning happens when goals change, not every tick). ABML bytecode execution is the hot path, and it runs on the zero-allocation VM.

---

## GOAP Beyond NPCs

GOAP is Arcadia's universal planner. The same A* search over action spaces is used for:

- **NPC behavior** (Actor + Behavior): "How do I achieve my goal given available actions?"
- **Narrative generation** (Storyline): "What sequence of narrative phases transforms this archive into a compelling story?"
- **Music composition** (Music): "What sequence of harmonic techniques transforms the current emotional state into the target emotion?"
- **Combat choreography** (Event Brain via Actor): "What sequence of moves creates a cinematic exchange given these two characters' capabilities?"

One planning paradigm means one set of improvements benefits every system. A better cost heuristic for GOAP planning makes NPCs more efficient, narratives more coherent, compositions more sophisticated, and combat more cinematic simultaneously.

This is not accidental. It is a deliberate design principle: emergent behavior from general systems rather than scripted behavior from special-case systems.
