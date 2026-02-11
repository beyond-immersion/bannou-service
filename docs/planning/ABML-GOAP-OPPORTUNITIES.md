# ABML/GOAP Expansion Opportunities

> **Created**: 2026-01-19
> **Last Updated**: 2026-02-11
> **Purpose**: Future applications for ABML and GOAP beyond current NPC cognition, combat choreography, and music composition
> **Scope**: New services, SDKs, and system integrations that leverage behavioral intelligence
> **Prerequisites**: All foundational services are implemented (currency, item, inventory, contract, escrow, character-encounter, quest, storyline). These opportunities build on that foundation.

This document identifies expansion opportunities for ABML/GOAP into new domains. Each requires Bannou service changes (new schemas, new services, or extensions to existing ones) before the game design can be realized.

For current ABML/GOAP architecture, see: [ABML Guide](../guides/ABML.md), [Behavior System Guide](../guides/BEHAVIOR-SYSTEM.md), [Behavior Deep Dive](../plugins/BEHAVIOR.md), [Actor Deep Dive](../plugins/ACTOR.md).

---

## Summary

| # | Opportunity | What It Needs From Bannou | Status |
|---|-------------|--------------------------|--------|
| 1 | [Adaptive Tutorial/Onboarding](#1-adaptive-tutorial--onboarding-system) | New SDK or service; player state observation pipeline | Design only |
| 2 | [Procedural Quest Generation](#2-procedural-quest-generation) | Quest template system; GOAP integration in Quest/Storyline | Design only |
| 3 | [Social Dynamics Engine](#3-social-dynamics-engine) | ABML behavior patterns; possible Relationship schema extensions | Design only |
| 4 | [Faction/Economy Simulation](#4-faction--economy-simulation) | Faction service or realm-level actor patterns; Currency/Relationship extensions | Design only |
| 5 | [Cinematography SDK](#5-cinematography-sdk) | New SDK wrapping existing cutscene infrastructure | Design only |
| 6 | [Dialogue Evolution System](#6-dialogue-evolution-system) | GOAP integration with ABML dialogue document type | Design only |
| 7+ | [Additional Ideas](#additional-opportunities) | Varies | Sketches only |

---

## 1. Adaptive Tutorial & Onboarding System

**Innovation Level**: ★★★★★ (Highly Novel)
**Target Users**: Game developers integrating Bannou

### The Opportunity

Traditional tutorials are linear scripts. Players either skip them (and struggle later) or endure them (boring for experienced players). **GOAP-driven tutorials observe what the player knows and plan what to teach next**.

### How It Works

```yaml
# Tutorial GOAP World State
player_knows_movement: false
player_knows_combat: false
player_knows_inventory: false
player_attempted_action_count: 0
player_failed_action_count: 0
player_frustration_estimate: 0.0
tutorial_time_elapsed: 0

# Tutorial Goals
goals:
  teach_basics:
    priority: 100
    conditions:
      player_knows_movement: "== true"
      player_knows_combat: "== true"
      player_knows_inventory: "== true"

  prevent_frustration:
    priority: 90  # Can interrupt teaching
    conditions:
      player_frustration_estimate: "< 0.7"

# Tutorial Actions (what the system can do)
flows:
  show_movement_hint:
    goap:
      preconditions:
        player_knows_movement: "== false"
        player_attempted_action_count: "> 3"  # They tried something
      effects:
        player_knows_movement: true
      cost: 2  # Low cost - prefer gentle hints

  show_movement_overlay:
    goap:
      preconditions:
        player_knows_movement: "== false"
        player_failed_action_count: "> 5"  # They're struggling
      effects:
        player_knows_movement: true
      cost: 5  # Higher cost - more intrusive

  reduce_difficulty_temporarily:
    goap:
      preconditions:
        player_frustration_estimate: "> 0.7"
      effects:
        player_frustration_estimate: "-0.3"
      cost: 10  # Last resort
```

### What Makes This Different

| Traditional Tutorial | GOAP Tutorial |
|---------------------|---------------|
| Linear sequence | Adaptive to player state |
| Same for all players | Personalized pacing |
| Skip = miss information | Catches up when needed |
| Interrupts gameplay | Woven into natural play |
| Designer-authored steps | Designer-authored goals |

### What Bannou Needs

- **Player state observation pipeline**: Something that ingests player actions and updates a GOAP world state (action counts, failure counts, frustration estimate). Could be an Analytics extension or a new lightweight service.
- **Tutorial action handlers**: ABML action handlers for tutorial-specific actions (show hint, show overlay, reduce difficulty). These would be client-side handlers similar to how the cinematic interpreter handles cutscene actions.
- **Frustration/competence estimation**: Algorithm to estimate player state from behavioral signals. Could live in Analytics or a dedicated SDK.

### Arcadia Integration

In Arcadia, the tutorial IS the first generation of a guardian spirit's life. The spirit has minimal agency and learns by watching its character live autonomously. A GOAP-driven tutorial system would control what the spirit can perceive and influence, expanding the UX surface area as the spirit demonstrates understanding -- aligning with the progressive agency model from [PLAYER-VISION.md](~/repos/arcadia-kb/PLAYER-VISION.md).

---

## 2. Procedural Quest Generation

**Innovation Level**: ★★★★★ (Highly Novel)
**Target Users**: Arcadia, other Bannou-powered games

### The Opportunity

Static quests feel repetitive. Procedural quests using random templates feel generic. **GOAP-planned quests use the actual world state, character backstory, and relationship graph to construct quests that feel personal**.

Quest generation is **inverse GOAP**: instead of planning actions to reach a goal, we **construct a goal that requires interesting actions given the current state**.

### How It Works

```yaml
# Quest Generation Process
# 1. Query character backstory for hooks
backstory_hooks:
  - type: TRAUMA
    content: "Witnessed family killed by bandits"
    strength: 0.8
  - type: GOAL
    content: "Become a master blacksmith"
    strength: 0.6

# 2. Query relationships for tension
relationships:
  - entity: "merchant_guild_leader"
    type: "rival"
    sentiment: -0.6
  - entity: "village_elder"
    type: "mentor"
    sentiment: 0.8

# 3. Query world state for opportunities
world_state:
  bandit_camp_nearby: true
  blacksmith_needs_rare_ore: true
  merchant_caravan_arriving: true

# 4. GOAP generates quest that connects hooks
generated_quest:
  title: "Echoes of the Past"
  summary: "Bandit activity near the village reminds you of darker days..."
  objectives:
    - "Investigate the bandit camp (backstory: TRAUMA)"
    - "Recover stolen blacksmith supplies (goal: blacksmithing)"
    - "Decide: bring bandits to justice or seek revenge"

  # Quest resolves multiple character concerns
  goap_effects:
    trauma_closure: "+0.2"  # Partial healing
    blacksmith_reputation: "+0.3"
    village_standing: "+0.2"
```

### Quest Templates as GOAP Actions

```yaml
# Quest templates define what kinds of quests exist
quest_templates:
  revenge_arc:
    preconditions:
      has_trauma_backstory: "== true"
      trauma_source_reachable: "== true"
    effects:
      trauma_closure: "+0.3"
      character_growth: "+0.2"
    cost: 3  # Medium engagement

  professional_advancement:
    preconditions:
      has_goal_backstory: "== true"
      relevant_opportunity_exists: "== true"
    effects:
      goal_progress: "+0.2"
      skill_reputation: "+0.1"
    cost: 2  # Lower barrier

  relationship_test:
    preconditions:
      has_strong_relationship: "== true"
      relationship_under_stress: "== true"
    effects:
      relationship_strength: "+0.2 or -0.3"  # Branching outcome
      character_definition: "+0.1"
    cost: 4  # Higher stakes
```

### What Makes This Different

| Traditional Proc-Gen | GOAP Quest Generation |
|---------------------|----------------------|
| Random templates | Character-relevant hooks |
| "Kill 10 wolves" | "Wolves threatening your mentor's village" |
| Generic rewards | Rewards that matter to this character |
| No memory | Builds on previous quests |
| Disconnected from world | Uses actual world state |

### What Bannou Needs

The data layer is complete (Character History provides backstory hooks, Relationship provides tension, Character Encounter provides memory, Quest provides lifecycle). What's missing:

- **Quest template registry**: A way to define quest archetypes as GOAP actions (the `quest_templates` above). Could be an extension to Quest's existing template system or a new endpoint.
- **World state aggregation**: Something that assembles a GOAP world state from multiple service queries (backstory + relationships + world state + location). Could live in Puppetmaster (which already does multi-service data aggregation for Event Brains) or in Quest itself.
- **Storyline integration**: The Storyline service already does seeded narrative generation from archives. Quest generation could feed INTO Storyline (generate a quest, then use Storyline to flesh out the narrative arc) or could use Storyline's composer SDK directly.

### Related Issues

- [#385](https://github.com/BeyondImmersion/bannou-service/issues/385) - Archive-to-Storyline Feedback Pipeline (content flywheel, adjacent concept)

---

## 3. Social Dynamics Engine

**Innovation Level**: ★★★★☆ (Novel Application)
**Target Users**: Arcadia, social simulation games

### The Opportunity

NPC relationships in most games are static (friend/enemy) or simple meters. **A GOAP-driven social system lets NPCs pursue relationship goals** -- friendships form because NPCs share interests, rivalries emerge from conflicting goals, romances develop through compatible personalities.

### How It Works

Each NPC has **social goals** that GOAP plans actions toward:

```yaml
# NPC Social Goals
goals:
  find_friend:
    priority: 60
    conditions:
      friendship_count: ">= 1"

  impress_authority:
    priority: 70
    conditions:
      authority_opinion: "> 0.5"

  resolve_conflict:
    priority: 80  # Higher priority when active
    conditions:
      active_conflicts: "== 0"

# Social Actions (what NPCs can do)
flows:
  give_compliment:
    goap:
      preconditions:
        target_present: "== true"
        relationship_sentiment: "> -0.3"  # Not enemies
      effects:
        relationship_sentiment: "+0.05"
        target_opinion: "+0.03"
      cost: 1  # Cheap, low-impact

  offer_help:
    goap:
      preconditions:
        target_has_need: "== true"
        can_fulfill_need: "== true"
      effects:
        relationship_sentiment: "+0.15"
        relationship_trust: "+0.1"
      cost: 3  # Requires investment

  share_secret:
    goap:
      preconditions:
        relationship_trust: "> 0.6"
        has_secret_to_share: "== true"
      effects:
        relationship_intimacy: "+0.2"
        vulnerability: "+0.1"  # Risk
      cost: 5  # High investment, high reward

  challenge_rival:
    goap:
      preconditions:
        rivalry_active: "== true"
        confidence: "> 0.5"
      effects:
        rivalry_intensity: "+0.2"  # Escalates
        social_standing: "+0.1 or -0.2"  # Win or lose
      cost: 4
```

### Personality-Driven Social Behavior

Character personality traits weight social goal priorities:

```yaml
# High EXTRAVERSION
- friendship goals have higher priority
- "give_compliment" actions have lower cost
- "share_secret" has lower trust threshold

# High AGGRESSION
- rivalry goals have higher priority
- "challenge_rival" has lower cost
- conflict resolution has higher cost

# High LOYALTY
- existing relationship goals have higher priority
- "betray" actions have extremely high cost
- "defend_friend" becomes available
```

### Emergent Relationship Patterns

| Pattern | How It Emerges |
|---------|----------------|
| **Friendship** | Compatible personalities, mutual helping, shared experiences |
| **Rivalry** | Conflicting goals, competition for same resources/people |
| **Romance** | High compatibility, escalating intimacy actions, exclusivity goals |
| **Mentorship** | Skill differential, teaching actions, respect goals |
| **Betrayal** | Conflicting loyalty goals, opportunity + low loyalty trait |

### What Bannou Needs

The infrastructure mostly exists: Character Personality provides trait data, Character Encounter tracks interactions, Relationship tracks bonds, Actor executes ABML behaviors. What's missing:

- **Social GOAP world state provider**: A Variable Provider Factory implementation that aggregates social data (relationship sentiments, encounter history, nearby NPCs with needs) into GOAP world state. Would be a new `IVariableProviderFactory` implementation, likely in a new or existing L4 service.
- **Social action handlers**: ABML action handlers for social actions (compliment, help, share_secret). These would call existing service APIs (Relationship for sentiment updates, Character Encounter for recording interactions).
- **Relationship schema extensions**: Current Relationship service tracks bonds but may need extensions for trust, intimacy, rivalry intensity as separate dimensions beyond the single sentiment score.
- **Need/opportunity detection**: NPCs need to perceive that another NPC "has a need" they can fulfill. This requires either an event-based system or a query against nearby NPCs' GOAP world states.

---

## 4. Faction & Economy Simulation

**Innovation Level**: ★★★★☆ (Novel at This Scale)
**Target Users**: Arcadia living world systems

### The Opportunity

Most game economies are static or use simple supply/demand curves. **Realm-level GOAP lets factions pursue economic and political goals**, creating emergent trade wars, alliances, and conflicts. Aligns with the Regional Watchers / Gods pattern already designed.

### Architecture: Faction Brains

```
┌─────────────────────────────────────────────────────────────┐
│                    Realm-Level Simulation                    │
│                                                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐       │
│  │ Merchant     │  │ Noble        │  │ Criminal     │       │
│  │ Guild Brain  │  │ House Brain  │  │ Syndicate    │       │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘       │
│         │                 │                 │                │
│         ▼                 ▼                 ▼                │
│  ┌─────────────────────────────────────────────────────┐    │
│  │              Realm Economic State                    │    │
│  │  - Resource prices                                   │    │
│  │  - Trade route status                               │    │
│  │  - Faction territories                              │    │
│  │  - Political relationships                          │    │
│  └─────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

### Faction GOAP Example

```yaml
# Merchant Guild Goals
goals:
  maximize_profit:
    priority: 90
    conditions:
      treasury_growth: "> 0.1"  # 10% growth

  control_trade_routes:
    priority: 70
    conditions:
      controlled_routes: ">= 3"

  maintain_peace:
    priority: 60  # Merchants prefer stability
    conditions:
      active_wars: "== 0"

# Merchant Guild Actions
flows:
  establish_trade_post:
    goap:
      preconditions:
        gold_reserves: "> 1000"
        target_region_uncontested: "== true"
      effects:
        controlled_routes: "+1"
        gold_reserves: "-800"
        monthly_income: "+50"
      cost: 3

  bribe_noble:
    goap:
      preconditions:
        gold_reserves: "> 500"
        target_noble_corruptible: "== true"
      effects:
        noble_favor: "+0.3"
        political_influence: "+0.1"
        gold_reserves: "-500"
      cost: 5

  fund_mercenaries:
    goap:
      preconditions:
        gold_reserves: "> 2000"
        military_threat_exists: "== true"
      effects:
        military_strength: "+1"
        gold_reserves: "-1500"
      cost: 8  # Expensive, last resort
```

### Emergent World Events

| Event | How It Emerges |
|-------|----------------|
| **Trade War** | Two factions pursue same trade route control |
| **Price Spike** | Resource scarcity + faction hoarding |
| **Political Marriage** | Noble houses pursuing alliance goals |
| **Smuggling Rise** | Criminal syndicate exploiting faction conflict |
| **Rebellion** | Peasant faction frustration > threshold |

### What Bannou Needs

This is the most infrastructure-heavy opportunity:

- **Faction entity model**: Factions don't exist as a service concept. Could be modeled as Actors with a "faction_brain" type, using the existing Actor pool infrastructure. Or could be a new service. Factions need: member rosters, treasury (Currency wallets), territory claims (Location references), political relationships (Relationship service).
- **Realm economic state aggregation**: A way to query realm-level economic data (total currency in circulation, resource prices, trade volume). Currency has some of this (global supply analytics -- see [#211](https://github.com/BeyondImmersion/bannou-service/issues/211)) but not realm-scoped.
- **Faction GOAP world state provider**: Variable Provider Factory for faction-level data (treasury, territory, political standing). Would feed into faction brain actors.
- **Regional Watcher integration**: Faction brains should interact with Regional Watchers (Puppetmaster). Hermes/Commerce god could manipulate faction dynamics through narrative events ("divine economic intervention").

---

## 5. Cinematography SDK

**Innovation Level**: ★★★★☆ (Novel Developer Tool)
**Target Users**: Game developers, content creators

### The Opportunity

The cutscene/choreography system already exists in Behavior (ABML `cutscene` document type with continuation points). **Exposing it as a developer SDK** lets game studios create dynamic camera systems without understanding ABML internals.

### GOAP for Shot Selection

```yaml
# Camera Goals (what makes a good shot)
goals:
  maintain_continuity:
    priority: 90
    conditions:
      axis_violation: "== false"

  show_action:
    priority: 80
    conditions:
      action_visible: "== true"

  create_drama:
    priority: 60
    conditions:
      dramatic_tension: "> 0.5"

# Camera Actions (available shots)
flows:
  wide_establishing:
    goap:
      preconditions:
        scene_changed: "== true"
      effects:
        spatial_context: true
        dramatic_tension: "-0.1"  # Wide shots reduce tension
      cost: 2

  close_up_reaction:
    goap:
      preconditions:
        emotional_beat: "== true"
        character_face_visible: "== true"
      effects:
        emotional_connection: "+0.2"
        dramatic_tension: "+0.1"
      cost: 1

  dutch_angle:
    goap:
      preconditions:
        scene_unstable: "== true"
        dramatic_tension: "> 0.7"
      effects:
        unease: "+0.2"
        dramatic_tension: "+0.1"
      cost: 3  # Use sparingly
```

### What Bannou Needs

- **SDK package**: A `BeyondImmersion.Bannou.Behavior.Camera` package wrapping the GOAP planner with cinematography-specific types (ShotType, FramingTarget, CameraMovement, CinematicRule). Follows the MusicTheory/MusicStoryteller layered SDK pattern.
- **Scene moment data model**: A way to describe what's happening in a scene (who's present, what actions are occurring, emotional beats) so the GOAP planner can select shots. Could extend the existing Event Brain resource snapshot pattern.
- **Client-side integration**: The SDK would run client-side (or server-side with results pushed to client). Needs to integrate with the streaming composition system already in Behavior.

---

## 6. Dialogue Evolution System

**Innovation Level**: ★★★☆☆ (Extension of Existing)
**Target Users**: Arcadia, narrative-heavy games

### The Opportunity

ABML already supports `dialogue` document type for branching conversations. **Extending this with GOAP** lets NPCs plan conversation strategies toward relationship goals rather than following static trees.

Instead of `Player says X -> NPC responds with Y`, NPCs have conversation goals and plan dialogue moves to achieve them.

### Dialogue GOAP Example

```yaml
# Merchant NPC conversation goals
goals:
  make_sale:
    priority: 80
    conditions:
      customer_purchased: "== true"

  build_rapport:
    priority: 60
    conditions:
      relationship_improved: "== true"

  learn_information:
    priority: 50
    conditions:
      customer_revealed_info: "== true"

# Dialogue moves (actions)
flows:
  compliment_appearance:
    goap:
      preconditions:
        conversation_started: "== true"
        compliment_given: "== false"
      effects:
        rapport: "+0.1"
        customer_mood: "+0.1"
      cost: 1

  mention_discount:
    goap:
      preconditions:
        product_discussed: "== true"
        customer_hesitant: "== true"
      effects:
        purchase_likelihood: "+0.2"
        profit_margin: "-0.1"
      cost: 3

  ask_about_travels:
    goap:
      preconditions:
        customer_is_traveler: "== true"
        rapport: "> 0.3"
      effects:
        customer_revealed_info: true
        rapport: "+0.05"
      cost: 2

  hard_sell:
    goap:
      preconditions:
        patience_remaining: "> 0.5"
        product_discussed: "== true"
      effects:
        purchase_likelihood: "+0.3"
        customer_mood: "-0.2"
        rapport: "-0.1"
      cost: 5  # Aggressive, use carefully
```

### Personality Influence on Dialogue

```yaml
# High EXTRAVERSION merchant
- conversation goals have higher priority
- "ask_about_travels" has lower cost
- longer conversations before giving up

# High HONESTY merchant
- "mention_discount" only if genuine
- "hard_sell" has extremely high cost (feels wrong)
- "compliment_appearance" only if true

# High AGGRESSION merchant
- "hard_sell" has lower cost
- shorter patience threshold
- more direct conversation moves
```

### What Bannou Needs

- **Conversation state tracking**: A way to maintain GOAP world state across a conversation (rapport level, topics discussed, emotional state). Could be ephemeral state in the Actor's memory during an encounter, or a new lightweight state model.
- **Dialogue action handlers**: ABML action handlers that map dialogue moves to actual text/speech output. This is where LLM integration or template-based text generation would connect -- the GOAP decides WHAT to say (compliment, ask about travels, hard sell), and the text generation layer decides HOW to say it.
- **Encounter integration**: Conversations should automatically record as Character Encounters with per-participant emotional impact, feeding back into future social dynamics.
- **GOAP WorldState provider for conversations**: Issue [#148](https://github.com/BeyondImmersion/bannou-service/issues/148) (Extend GOAP WorldState with external service data) is directly relevant here.

---

## Additional Opportunities

Lower-priority ideas that need further development before they're actionable:

### Ecosystem/Ecology Simulation

Use GOAP for predator-prey dynamics, resource distribution, and environmental balance. Animals pursue survival goals (eat, drink, shelter, reproduce), creating emergent ecosystem behaviors. Would need: animal entity model (likely Actor with ecology behaviors), spatial awareness (Mapping integration), population tracking.

### Weather/Climate System

GOAP for weather pattern planning. Weather "goals" create coherent weather narratives (storm building, calm before storm, seasonal transitions) rather than random changes. Would need: weather state model, realm-scoped weather service or Puppetmaster extension, location-based weather queries.

### Traffic/Crowd Simulation

GOAP for crowd members pursuing daily goals (go to work, shop, socialize), creating realistic city activity without scripted schedules. Would need: schedule/routine ABML behaviors, location awareness, activity tracking. Could be a specialization of Actor with lightweight "crowd NPC" behaviors.

### Puzzle Generation

Inverse GOAP for puzzle design: define solution requirements, generate puzzle that requires specific steps. Similar to quest generation but for spatial/logic puzzles. Would need: puzzle state model, Scene service integration for spatial layout, validation system.

### Moderation/Community Health

GOAP for automated moderation: goals include "maintain positive community", "identify toxic patterns", "fair enforcement". Plans moderation actions. Would need: Chat service integration, behavioral signal analysis, graduated response system.

---

## Related Documentation

- [ABML Guide](../guides/ABML.md) - Full ABML specification
- [Behavior System Guide](../guides/BEHAVIOR-SYSTEM.md) - Actor execution model and GOAP planning
- [Music System Guide](../guides/MUSIC-SYSTEM.md) - Music GOAP patterns (established SDK pattern)
- [Behavior Deep Dive](../plugins/BEHAVIOR.md) - Compiler, planner, and runtime internals
- [Actor Deep Dive](../plugins/ACTOR.md) - Variable Provider Factory, pool deployment
- [Quest Deep Dive](../plugins/QUEST.md) - Quest-as-contract architecture
- [Storyline Deep Dive](../plugins/STORYLINE.md) - Seeded narrative generation
- [Puppetmaster Deep Dive](../plugins/PUPPETMASTER.md) - Regional watchers, dynamic behavior loading
- [Regional Watchers Planning](REGIONAL-WATCHERS-BEHAVIOR.md) - God/watcher pattern

---

*This document should be updated as opportunities are expanded, prototyped, or promoted to implementation plans.*
