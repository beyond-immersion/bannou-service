# Player Experience Vision

> **Purpose**: This document captures the player-facing experience design for Arcadia, from first contact through long-term play, including the alpha-to-release deployment strategy. It complements VISION.md (which covers systems and architecture) by describing what the player actually *experiences* and how that experience evolves over time.
>
> **Core Thesis**: Every boundary in Arcadia is a gradient, not a wall. Tutorial to gameplay, idle to active control, one genre to another, testing phase to release -- all are continuous spectrums that the player traverses through accumulated experience.

---

## The Fundamental Principle: Agency Is Earned Context

The single most important design principle for the player experience:

> **The guardian spirit starts as nearly inert. Through accumulated experience, it gains understanding. Understanding manifests as increased control fidelity and richer UX surface area.**

This is not a traditional "unlock" system with thresholds and gates. It is a fluid model where experience grants knowledge, knowledge grants understanding, and understanding grants agency. The more a spirit engages with an aspect of the world, the more it can perceive and influence that aspect.

The character is ALWAYS autonomous. The NPC brain handles all behavior at all times. The player does not "control" a character -- the player gradually learns to *collaborate with* that character's autonomy. The idle case is not a fallback; it is the default state that the player slowly earns the right to modulate.

### How Progressive Agency Manifests

**First Generation**: The spirit can barely influence characters at all. The player watches, nudges with vague directional intent, and learns how the world works by observing autonomous characters living their lives. This IS the tutorial -- not a scripted sequence, but an entire lifetime of limited agency where the player learns by watching.

**Second Generation**: The spirit begins to "communicate" with its characters. Influence becomes more specific -- not just "approach" or "retreat" but identifiable intentions. The character may respond to the spirit's nudges more readily, or resist less when the spirit's suggestion aligns with the character's personality.

**Ongoing Play**: Agency deepens along the specific axes the spirit has invested in. A spirit that has accumulated combat experience across multiple characters and generations begins to perceive combat structure that was always there but invisible: stance options appear, timing windows become visible, combo choreography becomes directable. The character was always capable of these things. The spirit simply couldn't perceive or influence them until it had enough context.

**Cross-Generational Accumulation**: Understanding persists across character lifetimes. A spirit that guided three generations of blacksmiths carries deep crafting agency into any future character who picks up a hammer -- even if that character is new and untrained. The character's skill and the spirit's agency are separate dimensions that combine.

---

## The UX as Progression System

The player's interface itself is a progression system. Not in a gamified "unlock the minimap" sense, but in a *the spirit literally doesn't know how to perceive this yet* sense.

### Domain-Specific UX Expansion

A new spirit possessing a character in combat sees chaos. Perhaps a single "intention" input -- approach, retreat, hold. As the spirit accumulates combat experience, the UX expands:

- Basic combat awareness: directional intent (approach, retreat, flank)
- Stance perception: the spirit can see and suggest defensive/aggressive posture
- Timing windows: dodge/parry/strike opportunities become visible
- Combo choreography: the spirit can direct sequences of actions
- Martial specialization: a monk/martial artist unlocks UX specific to that discipline

The same expansion happens in every domain:

| Domain | Early UX | Mature UX |
|--------|----------|-----------|
| **Combat** | Single intention input (approach/retreat) | Stance selection, timing windows, combo direction, martial style specialization |
| **Crafting** | Watch the character work | Material selection, technique choice, quality targeting, process optimization |
| **Social** | Observe conversations | Tone suggestion, topic steering, negotiation strategy, relationship management |
| **Trade** | Character handles autonomously | Price setting, partner selection, inventory management, market analysis |
| **Exploration** | Directional drift | Route planning, landmark recognition, danger assessment, resource tracking |
| **Magic** | Raw impulse | Stage-by-stage spellcasting control, pneuma management, spell composition |

### "Same Systems, Different Games"

This UX expansion model means that different players experience what feel like fundamentally different games, even though they share the same underlying simulation:

- A combat-focused spirit sees a rich action RPG with deep martial mechanics
- A farming-focused spirit sees detailed soil management, weather prediction, and crop planning
- A social-focused spirit sees a relationship and political simulation
- A crafting-focused spirit sees an authentic process simulation with 37+ real-world procedures
- A monster-raising spirit sees creature bonding, training, and evolution systems

The *world* is identical. The *window into it* varies per spirit. An ARPG, a farming game, and a monster raising sim coexist in the same game space because they are different UX manifests on the same simulation.

### UX Capability Manifest

The server maintains a UX capability manifest per spirit (analogous to the permission capability manifest for service endpoints). This manifest tells the client:

- Which interaction modalities the spirit has unlocked
- At what fidelity level each modality operates
- Which domain-specific modules should be loaded

The client renders accordingly -- the same underlying systems, but the UX surface area exposed varies dramatically per player. The server is authoritative; the client cannot unlock things locally.

### The Omega Meta-Game

In the Omega realm (cyberpunk meta-dashboard), the UX progression system becomes *diegetic*. The player's "full-dive VR machine" has a literal configuration screen where UX modules are allocated to dive sessions. This makes the abstract concept of spirit agency into a tangible in-world interface.

Critical Omega mechanic: **hacking**. Players can use UX modules on characters/seeds that haven't "earned them" -- forcing a spirit-character relationship that hasn't been naturally developed. Consequences:

- The character may resist more strongly (borrowed understanding vs. earned understanding)
- Results may be inferior (the spirit has the interface but not the intuition)
- Social/ethical implications within Omega's cyberpunk narrative
- Potential for both creative exploitation and meaningful failure

This also serves as an explicit configuration layer for experienced players who want to experiment with different UX combinations across their seeds without re-earning everything organically.

---

## The Void: First Contact

### What the Player Experiences

Upon first login, there is no character creation screen, no menu, no tutorial prompt. The player exists as a spirit -- a point of light, a divine shard of Nexius -- floating in a void space. Ambient, minimal. A prompt appears with arrows in cardinal directions, flashing slowly, indicating that movement is possible.

As the spirit begins to drift, things happen:

- **Points of interest spawn in the spirit's path.** These are isolated playgrounds -- self-contained mini-games or demo segments that showcase a function or mechanic. Some appear as visual anomalies in the void, some as sounds or music that can be chased, some as environmental shifts (a patch of ground materializing, a distant light, a faint voice).

- **Interaction generates data.** Everything the spirit does -- movement direction, speed, hesitation, engagement duration, scenario completion, avoidance patterns -- feeds an "event actor" in the service network that coordinates what spawns next. The spirit is unknowingly and knowingly making choices about what kind of game it wants to experience.

- **Scenarios present themselves.** At any point, the spirit may encounter a scenario trigger:
  - **Implicit acceptance**: Doors that spawn in the spirit's path -- entering is accepting, avoiding is declining
  - **Prompted**: A visual/audio cue that asks for acknowledgment before proceeding
  - **Dialog choices**: Explicit options presented with different implications
  - **Forced**: Unavoidable scenarios that pull the spirit in (used sparingly for critical experiences)

- **Scenario drops.** Accepting a scenario drops the spirit from the void into a fully realized game segment -- proper environments, full game mechanics, real NPC interactions. These are isolated instances, not connected to the persistent world (during alpha/beta). The spirit can always "reset" back to the void and begin drifting again.

- **Scenario chaining.** While inside a scenario, paths or choices may lead to *different* scenarios. No two experiences are identical even when making similar initial choices. The event actor coordinates branching based on accumulated spirit data.

### What the Server Experiences

The void is architecturally minimal:

- **Void space = negligible bandwidth.** A position vector, orientation, and ambient trigger state. The Connect service maintains the WebSocket session but transmits almost nothing.
- **Event actor = Puppetmaster-coordinated.** A per-player coordinator (analogous to a Regional Watcher but scoped to one spirit's void instance) decides what to spawn based on accumulated analytics.
- **Points of interest = isolated sandboxes.** Each is a self-contained experience with known resource bounds. Spun up and down dynamically via Orchestrator processing pools.
- **Scenario drops = full game sessions.** Only when a spirit commits to a scenario does the system spin up the full service stack for that experience. Queue management is natural -- the void absorbs wait times gracefully because drifting *is* the experience.

---

## Alpha, Beta, Release: The Gradient

The deployment strategy is not three separate products but a single continuum where the aperture of what scenarios can connect to widens progressively.

### Alpha: Isolated Showcases

**What players see**: The void experience as described above. Points of interest are isolated sandboxes -- mechanic showcases, combat demos, crafting tutorials, social encounters, musical chases. No persistent world state. Every scenario is self-contained.

**What we collect**:
- Movement patterns and drift preferences
- Engagement duration per scenario type
- Scenario completion rates and approach styles
- Choice distributions across dialog/prompted scenarios
- Implicit preference signals (what players chase, what they avoid)

**Load management**: Queue control is natural. The void transmits minimal data. Scenarios are isolated instances spun up on demand. We can balance load by controlling how many scenarios are offered simultaneously, how quickly points of interest spawn, and how many concurrent scenario instances the Orchestrator maintains. Group/shared scenarios (where multiple spirits interact in the same sandbox) can be offered when server capacity allows -- collecting multiplayer interaction data in controlled conditions.

**Spirit metadata begins accumulating.** Alpha testers are building their spirit's early experience profile, which carries forward.

### Beta: Windows Into the World

**What changes**: Some scenarios now connect to *slices* of real realm state. Instead of an isolated sandbox, a scenario drops the spirit into an actual neighborhood of Arcadia -- the market district, a forest outpost, a mining settlement. Real NPCs running real ABML behaviors. Real economy. Real weather and seasons.

**The constraint**: These are limited "drops" -- the spirit enters with a specific objective or scenario context, interacts with the living world for a bounded session, and is ejected back to the void. The world persists between visits, but the spirit only sees curated windows into it.

**The flywheel begins**: Beta player actions generate real history. An NPC remembers the spirit's character. A trade affects prices. A combat encounter leaves a mark. The content flywheel is already spinning -- not at full speed, but turning. This means later beta sessions are enriched by earlier ones, which is the first real demonstration of the "more play produces more content" thesis to players.

**Spirit metadata continues accumulating.** Beta spirits have richer profiles than alpha spirits, with some experience in the actual persistent world.

### Release: The Surprise

**What happens**: Release is not announced as a separate event. It arrives as a new type of experience within the existing void/scenario system. Players who have been drifting through void, playing scenarios, and taking limited drops into the world encounter something different -- a scenario that doesn't end, a door that leads to permanent inhabitation.

**The experience**: All beta and many new players are dropped into the game world in an organized fashion. The void becomes the persistent starting point -- the space between sessions. The spirit now selects one of its growing seeds to drop into, entering the persistent world as a guardian spirit bound to a household and its characters.

**For alpha/beta veterans**: The surprise is that this particular door leads to the real thing. Their accumulated spirit metadata provides subtle enrichment -- not a dramatically different experience, but a slightly more resonant starting point. Perhaps the household they're drawn to has qualities that echo their demonstrated preferences. The same starting scenario as everyone else, just with personal flavor.

**For new players**: Identical to the alpha experience at first. Void, drift, points of interest, scenarios. The progressive system works the same way -- the void is always the entry point, and the path from void to world is always a gradient of escalating engagement.

**What doesn't happen**: No hard cutover. Players mid-scenario aren't forcibly transitioned. The existing void/scenario system continues to function as a login queue and lobby space. Players in the void see the new persistent-world option appear. Those in scenarios finish at their own pace and discover it when they return to the void. Some may find doors or paths within their current scenario that lead to the new world, as a natural branching option.

### Post-Release: The Void Persists

The void never goes away. It remains as:

- **Starting point**: Every session begins here. Orient yourself, choose a seed, drop in.
- **Lobby**: Between active gameplay sessions, a meditative floating space.
- **Mini-game arcade**: Points of interest and scenarios continue to appear. Some players spend significant time here, and that's fine -- they're still generating spirit metadata and contributing to analytics.
- **Decompression space**: A break from the intensity of the living world.
- **Seed selection**: Choose which of your active seeds to enter (up to 3 per account).

---

## The Pair System: Twin Spirits

> Inspired by the Rynax pair bond from *Kurau: Phantom Memory* -- an anime about sentient energy beings that exist exclusively in pairs, experiencing profound incompleteness when separated and symbiotic wholeness when together. The pair bond is existential, not social; it is how these beings *are*, not something they choose.

### The Concept

Before launch, a "pair" starting scenario is introduced. Two players can choose to begin as **twin spirits** -- paired divine shards that are narratively and metaphysically linked from the moment of their creation. This is not a party system or a friend list. It is a deeper bond:

- Twin spirits are fragments of the same shard of Nexius, split into complementary halves
- Their households begin already linked -- neighboring families, shared history, intertwined fates
- The narrative grows into friendly, contentious, or rivalrous directions based on **explicit choices** (not left to chance -- the relationship is too important to be accidentally adversarial)
- Twin spirits can always communicate, bypassing the normal distance-communication systems that require in-game tools or enchantments. This communication is limited at first (impressions, emotions, directional nudges) but grows with the pair's shared experience

### Why Pairs Exist

The pair system addresses the 95% case of cooperative multiplayer without requiring both players to independently grind through the progressive agency system to unlock explicit social mechanics (friend lists, guilds, distance communication -- all of which are managed through in-game functions and tools, not meta-UI).

**The problem**: Two friends buy the game. One has 200 hours, one has 20. The experienced player has unlocked rich social UX; the new player can barely influence their character. Traditional multiplayer would put them in a party. Arcadia's progressive agency model makes that feel wrong -- the new spirit hasn't earned that level of social coordination.

**The solution**: Twin spirits start bonded. Their communication channel exists from the beginning because it's metaphysical, not technological. They don't need to unlock social systems to find each other, coordinate, or share experiences. The pair bond IS the social system for these two spirits, existing outside the normal progression.

### Pair Dynamics

- **Shared void experience**: Twin spirits can drift through the void together, seeing the same points of interest and entering scenarios cooperatively
- **Linked households**: Their characters begin in proximity with established social connections (neighboring families, childhood friends, trade partners, etc.)
- **Complementary growth**: The pair bond encourages but doesn't force specialization. One spirit gravitates toward combat while the other explores crafting, and their households naturally develop complementary roles
- **Narrative branching**: Key moments present the pair with explicit choices about their relationship's direction. Alliance, rivalry, friendly competition, protective guardianship -- all are valid paths, chosen deliberately
- **Communication channel**: The pair bond provides a communication layer that bypasses in-game distance limitations. Early on this is vague (emotional impressions, a sense of the other's general state). With shared experience, it becomes richer (specific messages, coordinated intentions, shared perception in proximity)
- **Independent but linked**: Each spirit has its own seeds, its own households, its own agency progression. The pair bond enriches but doesn't constrain. Twin spirits can play entirely different aspects of the game and their bond still functions

### The Pair Bond vs. Normal Social Systems

| Aspect | Pair Bond | Normal Social |
|--------|-----------|---------------|
| **Availability** | From creation | Earned through progressive agency |
| **Communication** | Innate (metaphysical) | Requires in-game tools/enchantments |
| **Range** | Unlimited (same shard of Nexius) | Limited by in-game distance/technology |
| **Discovery** | Automatic (always know where pair is) | Requires social UX modules |
| **Limit** | One pair per spirit (binary, like Rynax) | Expands with social agency growth |
| **Persistence** | Permanent and unbreakable | Relationships can form and dissolve |

---

## Seeds: Multiple Relationships to the World

Each account supports up to 3 seeds -- distinct relationships between the guardian spirit and the game world. These are not "alt characters" in the traditional sense. Each seed represents a fundamentally different mode of engagement with the simulation.

### Seed Types (Examples)

1. **Standard Guardian Spirit**: The default Arcadia experience. Manage a household across generations. Characters age, marry, have children, die. The content flywheel turns. Progressive agency deepens across lifetimes.

2. **Dungeon Master**: The spirit IS a dungeon. Your "household" is your dungeon ecosystem -- spawned creatures, traps, environmental features, memory manifestations. You perceive adventurers, orchestrate encounters, grow chambers. This maps directly to the Dungeon-as-Actor architecture where dungeon cores run simplified ABML cognition pipelines.

3. **Realm-Specific Variant**: A fundamentally different relationship to a specific realm. Examples:
   - Fantasia (primitive fantasy survival) seed with different UX emphasis
   - Omega (cyberpunk) seed with the diegetic VR configuration meta-game
   - A "merchant dynasty" seed focused purely on economic simulation
   - A "god-shard" seed (mini regional watcher) that perceives and influences at a broader scale than individual characters

### Seed Independence

Each seed:
- Has its own household/entity state
- Exists in any realm (including the same realm as another seed)
- Develops its own UX capability manifest based on engagement
- Contributes to the overall spirit's experience pool (cross-pollination)

The spirit's accumulated understanding crosses seed boundaries. A spirit with deep combat experience from Seed 1 carries that agency into Seed 2's dungeon master mode -- perhaps manifesting as better combat choreography for spawned encounters.

### Void and Seeds

The void serves as the seed selection point. Post-release:

1. Enter void
2. See representations of your active seeds (up to 3)
3. Drop into one -- or drift, play mini-games, explore scenarios
4. Return to void when done, select another seed or log off

---

## The Gradients

Every boundary in Arcadia is a gradient, not a wall:

| Traditional Boundary | Arcadia Gradient |
|---------------------|------------------|
| Character creation screen | Void drift discovery + first generation observation |
| Tutorial | The entire first generation of limited agency |
| Unlocking a feature | Progressive UX expansion through accumulated experience |
| Idle vs. active play | Continuous spectrum of spirit influence over autonomous characters |
| One game genre vs. another | UX manifest shifts based on engagement domain |
| Alpha vs. beta vs. release | Widening aperture of scenario connectivity |
| Solo vs. multiplayer | Pair bonds + progressive social system unlocks |
| Void vs. game world | Scenarios escalate from isolated sandboxes to persistent world windows to full inhabitation |
| One character vs. the next | Spirit understanding persists across generations |
| One seed vs. another | Accumulated experience cross-pollinates |
| Online vs. offline | The world continues; returning reveals organic change, not frozen state |

This gradient philosophy is the player-facing expression of the architectural principle: the same systems, configured differently, producing different experiences. The server has 45+ service plugins loaded based on configuration. The client has UX modules loaded based on spirit capability. The world has scenarios loaded based on deployment phase. All gradients. No walls.

---

## Summary: The Player's Journey

```
First Login (Alpha/Beta/Release)
    │
    ▼
Spirit in Void ──drift──▶ Points of interest spawn
    │                           │
    │                     Implicit choice data accumulates
    │                           │
    ▼                           ▼
Scenarios present ──────▶ Drop into isolated experience
    │                           │
    │                     Play, learn, reset to void
    │                           │
    ▼                           ▼
Spirit gains context ───▶ More complex scenarios offered
    │                           │
    │                     (Beta: scenarios connect to real world)
    │                           │
    ▼                           ▼
First persistent drop ──▶ Guardian spirit binds to household
    │                           │
    │                     First generation: minimal agency
    │                     Watch, nudge, learn
    │                           │
    ▼                           ▼
Second generation ──────▶ Communication begins
    │                     Spirit can influence more directly
    │                           │
    ▼                           ▼
Ongoing play ───────────▶ Progressive agency deepens
    │                     UX expands per domain
    │                     Characters live, age, die
    │                     Content flywheel spins
    │                           │
    ▼                           ▼
Long-term ──────────────▶ Multiple seeds
                          Deep domain mastery
                          Cross-generational understanding
                          The world enriches with age
                          The spirit enriches with experience
```

---

*This document captures the player experience vision as of its writing date. It is a design north star, not a technical specification. For architecture, see BANNOU-DESIGN.md. For systems, see VISION.md. For service hierarchy, see SERVICE-HIERARCHY.md.*
