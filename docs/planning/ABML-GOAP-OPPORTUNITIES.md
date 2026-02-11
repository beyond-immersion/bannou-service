# ABML/GOAP Expansion Opportunities

> **Created**: 2026-01-19
> **Last Updated**: 2026-01-23
> **Purpose**: Strategic analysis of future applications for Arcadia Behavior Markup Language (ABML) and Goal-Oriented Action Planning (GOAP)
> **Scope**: New services, SDKs, and system integrations that leverage behavioral intelligence

This document identifies and analyzes the most promising opportunities for expanding ABML and GOAP beyond their current applications, prioritizing innovation potential, alignment with THE_DREAM vision, and developer ecosystem value.

---

## Executive Summary

Bannou's ABML/GOAP infrastructure represents a **general-purpose behavioral intelligence layer** that currently powers NPC cognition, combat choreography, and music composition. The same architectural patterns—goal-oriented planning, intent-based outputs, personality-weighted decisions, and streaming composition—can transform numerous other game systems from static to dynamic.

This analysis identifies **six high-priority expansion opportunities**:

| Priority | Opportunity | Innovation Level | Effort | Impact |
|----------|-------------|------------------|--------|--------|
| 1 | Adaptive Tutorial/Onboarding | ★★★★★ | Medium | High (Developer SDK) |
| 2 | Procedural Quest Generation | ★★★★★ | Medium-High | High (Core Arcadia) |
| 3 | Social Dynamics Engine | ★★★★☆ | Medium | High (NPC Believability) |
| 4 | Faction/Economy Simulation | ★★★★☆ | High | High (Living World) |
| 5 | Cinematography SDK | ★★★★☆ | Low-Medium | Medium (Developer SDK) |
| 6 | Dialogue Evolution System | ★★★☆☆ | Medium | Medium (NPC Depth) |

---

## Part 1: Current ABML/GOAP Usage

### 1.1 What ABML Provides

ABML (Arcadia Behavior Markup Language) is a **YAML-based DSL** for authoring event-driven, stateful action sequences. Key capabilities:

| Capability | Description | Example Use |
|------------|-------------|-------------|
| **Document Types** | behavior, dialogue, cutscene, dialplan, timeline | NPC routines, branching conversations, choreographed sequences |
| **Control Flow** | cond, for_each, repeat, goto, call, branch | Complex branching logic |
| **Channels & Sync** | Parallel execution tracks with emit/wait_for | Multi-participant choreography |
| **Expression Language** | Variables, arithmetic, null-safe navigation | Dynamic decision making |
| **Character Providers** | `${personality.*}`, `${combat.*}`, `${backstory.*}` | Personality-aware behaviors |
| **Handler Extensibility** | Custom action handlers per domain | Game-specific actions |

### 1.2 What GOAP Provides

GOAP (Goal-Oriented Action Planning) provides **A* search over action spaces** to find optimal plans:

| Capability | Description | Example Use |
|------------|-------------|-------------|
| **World State** | Immutable key-value state representation | Current NPC status |
| **Goals** | Desired states with priority (1-100) | "stay_fed", "seek_safety" |
| **Actions** | Preconditions, effects (delta/absolute), cost | "eat_meal", "flee_danger" |
| **Plan Generation** | A* search with configurable depth/timeout | Optimal action sequence |
| **Plan Validation** | Checks preconditions, detects better goals | Runtime replanning |
| **Urgency Scaling** | Adjusts search parameters based on urgency | Fast decisions under stress |

### 1.3 Current Applications

| System | ABML Role | GOAP Role | Status |
|--------|-----------|-----------|--------|
| **NPC Brain Actors** | Cognition pipeline, memory queries, emotion updates | Goal selection, action planning | ✅ Complete |
| **Event Brain Actors** | Combat choreography, QTE orchestration | Option filtering, encounter flow | 🔄 Active Development |
| **Music Storyteller** | Composition structure, phrase sequencing | Narrative arc planning, tension building | ✅ Complete |
| **Cutscene Coordination** | Multi-channel execution, sync points | (Not yet integrated) | ✅ Complete |
| **Dialplans (VoIP)** | Call routing, IVR menus | (Not yet integrated) | 📋 Foundation Ready |
| **Regional Watchers** | God domain logic, event evaluation | Event agent spawning decisions | 📋 Designed |

### 1.4 The Key Insight: Intent-Based Architecture

What makes ABML/GOAP powerful isn't just the planning—it's the **separation of intent from execution**:

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  GOAP Planner   │────►│  ABML Executor  │────►│ Intent Channels │
│  (What to do)   │     │  (How to do it) │     │ (Merged output) │
└─────────────────┘     └─────────────────┘     └─────────────────┘
         │                       │                       │
         ▼                       ▼                       ▼
    Goal weights            Action handlers         Urgency-based
    from personality        per domain              arbitration
```

This architecture enables:
- **Personality influence**: Same goal, different execution based on traits
- **Graceful degradation**: Plans adapt when world state changes
- **Parallel concerns**: Multiple systems contribute to final behavior
- **Domain agnosticism**: Same patterns apply to combat, dialogue, music, tutorials...

---

## Part 2: Expansion Opportunities

### 2.1 Adaptive Tutorial & Onboarding System

**Innovation Level**: ★★★★★ (Highly Novel)
**Effort Estimate**: Medium (2-3 weeks for core SDK)
**Target Users**: Game developers integrating Bannou

#### The Opportunity

Traditional tutorials are linear scripts. Players either skip them (and struggle later) or endure them (boring for experienced players). **GOAP-driven tutorials observe what the player knows and plans what to teach next**.

#### How It Works

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

#### Unique Value Proposition

| Traditional Tutorial | GOAP Tutorial |
|---------------------|---------------|
| Linear sequence | Adaptive to player state |
| Same for all players | Personalized pacing |
| Skip = miss information | Catches up when needed |
| Interrupts gameplay | Woven into natural play |
| Designer-authored steps | Designer-authored goals |

#### SDK Design

```csharp
// Developer API
public class TutorialEngine
{
    // Define what the player should learn
    public void DefineGoal(string goalId, Func<PlayerState, bool> condition, int priority);

    // Define how to teach things
    public void DefineLesson(string lessonId, TutorialAction action, GoapMetadata goap);

    // Update observations
    public void ObserveAction(PlayerAction action);
    public void ObserveSuccess(string actionType);
    public void ObserveFailure(string actionType);

    // Get next tutorial action (if any)
    public TutorialAction? GetNextAction();
}
```

#### Implementation Path

1. Create `BeyondImmersion.Bannou.Tutorial` SDK
2. Define tutorial-specific action handlers
3. Create player state observation pipeline
4. Implement frustration/competence estimation
5. Package with example tutorials

---

### 2.2 Procedural Quest Generation

**Innovation Level**: ★★★★★ (Highly Novel)
**Effort Estimate**: Medium-High (3-4 weeks)
**Target Users**: Arcadia, other Bannou-powered games

#### The Opportunity

Static quests feel repetitive. Procedural quests using random templates feel generic. **GOAP-planned quests use the actual world state, character backstory, and relationship graph to construct quests that feel personal**.

#### How It Works

Quest generation is **inverse GOAP**: instead of planning actions to reach a goal, we **construct a goal that requires interesting actions given the current state**.

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

#### The Quest Planner Architecture

```
Character Data ──────┐
  (backstory, goals) │
                     ▼
World State ────────►┌───────────────┐
  (opportunities)    │ Quest Planner │────► Generated Quest
                     │   (GOAP A*)   │      (objectives, rewards,
Relationship Data ──►└───────────────┘       narrative hooks)
  (tensions, allies)
```

#### Quest Templates as GOAP Actions

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

#### Why This Is Different

| Traditional Proc-Gen | GOAP Quest Generation |
|---------------------|----------------------|
| Random templates | Character-relevant hooks |
| "Kill 10 wolves" | "Wolves threatening your mentor's village" |
| Generic rewards | Rewards that matter to this character |
| No memory | Builds on previous quests |
| Disconnected from world | Uses actual world state |

---

### 2.3 Social Dynamics Engine

**Innovation Level**: ★★★★☆ (Novel Application)
**Effort Estimate**: Medium (2-3 weeks)
**Target Users**: Arcadia, social simulation games

#### The Opportunity

NPC relationships in most games are static (friend/enemy) or simple meters. **A GOAP-driven social system lets NPCs pursue relationship goals**—friendships form because NPCs share interests, rivalries emerge from conflicting goals, romances develop through compatible personalities.

#### How It Works

Each NPC has **social goals** that GOAP plans actions toward:

```yaml
# NPC Social Goals (examples)
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

#### Personality-Driven Social Behavior

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

#### Emergent Relationship Patterns

The system produces emergent social dynamics:

| Pattern | How It Emerges |
|---------|----------------|
| **Friendship** | Compatible personalities, mutual helping, shared experiences |
| **Rivalry** | Conflicting goals, competition for same resources/people |
| **Romance** | High compatibility, escalating intimacy actions, exclusivity goals |
| **Mentorship** | Skill differential, teaching actions, respect goals |
| **Betrayal** | Conflicting loyalty goals, opportunity + low loyalty trait |

---

### 2.4 Faction & Economy Simulation

**Innovation Level**: ★★★★☆ (Novel at This Scale)
**Effort Estimate**: High (4-6 weeks)
**Target Users**: Arcadia living world systems

#### The Opportunity

Most game economies are static or use simple supply/demand curves. **Realm-level GOAP lets factions pursue economic and political goals**, creating emergent trade wars, alliances, and conflicts.

This aligns with the **Regional Watchers / Gods pattern** already designed, expanding it to faction-level simulation.

#### Architecture: Faction Brains

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

#### Faction GOAP Example

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

#### Emergent World Events

Faction GOAP creates emergent events:

| Event | How It Emerges |
|-------|----------------|
| **Trade War** | Two factions pursue same trade route control |
| **Price Spike** | Resource scarcity + faction hoarding |
| **Political Marriage** | Noble houses pursuing alliance goals |
| **Smuggling Rise** | Criminal syndicate exploiting faction conflict |
| **Rebellion** | Peasant faction frustration > threshold |

---

### 2.5 Cinematography SDK

**Innovation Level**: ★★★★☆ (Novel Developer Tool)
**Effort Estimate**: Low-Medium (1-2 weeks)
**Target Users**: Game developers, content creators

#### The Opportunity

The cutscene/choreography system already exists for internal use. **Exposing it as a developer SDK** lets game studios create dynamic camera systems without understanding ABML internals.

#### SDK Design

```csharp
// High-level Cinematography API
public class CinematographyEngine
{
    // Define camera behaviors
    public void RegisterShot(string shotId, CameraShotDefinition definition);

    // Define dramatic rules
    public void SetRule(CinematicRule rule);

    // Execute cinematography
    public CameraInstruction GetCameraForMoment(SceneMoment moment);
}

// Example usage
cinematography.RegisterShot("hero_entrance", new CameraShotDefinition
{
    Type = ShotType.LowAngle,
    FramingTarget = FramingTarget.FullBody,
    MovementStyle = CameraMovement.SlowPush,
    DurationRange = (2.0f, 4.0f)
});

cinematography.SetRule(new CinematicRule
{
    Trigger = "character.power_level > scene_average * 2",
    PreferredShots = ["hero_entrance", "dramatic_reveal"],
    TransitionStyle = TransitionStyle.CrossDissolve
});
```

#### GOAP for Shot Selection

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

---

### 2.6 Dialogue Evolution System

**Innovation Level**: ★★★☆☆ (Extension of Existing)
**Effort Estimate**: Medium (2-3 weeks)
**Target Users**: Arcadia, narrative-heavy games

#### The Opportunity

Current ABML supports `dialogue` document type for branching conversations. **Extending this with GOAP** lets NPCs plan conversation strategies toward relationship goals rather than following static trees.

#### How It Works

Instead of:
```
Player says X → NPC responds with Y
```

We have:
```
NPC has conversation goal → Plans dialogue moves → Executes with personality flavor
```

#### Dialogue GOAP Example

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

#### Personality Influence on Dialogue

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

---

## Part 3: Additional Opportunities (Lower Priority)

### 3.1 Ecosystem/Ecology Simulation

Use GOAP for predator-prey dynamics, resource distribution, and environmental balance. Animals pursue survival goals (eat, drink, shelter, reproduce), creating emergent ecosystem behaviors.

**Effort**: High | **Innovation**: ★★★☆☆ | **Use Case**: Arcadia wildlife

### 3.2 Weather/Climate System

GOAP for weather pattern planning. Weather "goals" create coherent weather narratives (storm building, calm before storm, seasonal transitions) rather than random changes.

**Effort**: Medium | **Innovation**: ★★★☆☆ | **Use Case**: Environmental immersion

### 3.3 Traffic/Crowd Simulation

GOAP for crowd members pursuing daily goals (go to work, shop, socialize), creating realistic city activity without scripted schedules.

**Effort**: High | **Innovation**: ★★★☆☆ | **Use Case**: Urban environments

### 3.4 Puzzle Generation

Inverse GOAP for puzzle design: define solution requirements, generate puzzle that requires specific steps.

**Effort**: Medium | **Innovation**: ★★★★☆ | **Use Case**: Procedural dungeons

### 3.5 Moderation/Community Health

GOAP for automated moderation: goals include "maintain positive community", "identify toxic patterns", "fair enforcement". Plans moderation actions.

**Effort**: Medium | **Innovation**: ★★☆☆☆ | **Use Case**: Online communities

---

## Part 4: SDK Architecture Patterns

### 4.1 Layered SDK Pattern (From MusicTheory/MusicStoryteller)

```
┌─────────────────────────────────────────────┐
│     High-Level SDK (Domain Orchestrator)     │
│  e.g., TutorialEngine, QuestGenerator        │
└─────────────────────┬───────────────────────┘
                      ↓
┌─────────────────────────────────────────────┐
│       Mid-Level SDK (Domain Types)           │
│  e.g., TutorialTypes, QuestTemplates         │
└─────────────────────┬───────────────────────┘
                      ↓
┌─────────────────────────────────────────────┐
│    Core SDK (ABML Runtime + GOAP Engine)     │
│  BeyondImmersion.Bannou.Behavior             │
└─────────────────────────────────────────────┘
```

### 4.2 Recommended Package Structure

```
BeyondImmersion.Bannou.Behavior           # Core ABML + GOAP
BeyondImmersion.Bannou.Behavior.Tutorial  # Tutorial SDK
BeyondImmersion.Bannou.Behavior.Quest     # Quest generation
BeyondImmersion.Bannou.Behavior.Social    # Social dynamics
BeyondImmersion.Bannou.Behavior.Camera    # Cinematography
```

### 4.3 Entry Point Pattern

Each SDK should have a clear, documented entry point:

```csharp
// Good: Single orchestrator class
public sealed class TutorialEngine { }
public sealed class QuestGenerator { }
public sealed class SocialDynamicsEngine { }

// Not: Many classes with unclear relationships
```

---

## Part 5: Implementation Recommendations

### 5.1 Priority Order

Based on impact, effort, and strategic value:

1. **Adaptive Tutorial SDK** (2-3 weeks)
   - Immediate developer appeal
   - Showcases GOAP capabilities
   - Relatively self-contained

2. **Procedural Quest Generation** (3-4 weeks)
   - Core Arcadia value
   - Builds on character data layer
   - High player impact

3. **Cinematography SDK** (1-2 weeks)
   - Extracts existing capability
   - Quick win for developers
   - Marketing value (demos well)

4. **Social Dynamics Engine** (2-3 weeks)
   - Builds on relationship service
   - Enables deep NPC believability
   - Arcadia differentiator

5. **Faction/Economy Simulation** (4-6 weeks)
   - Enables living world vision
   - Complex but transformative
   - Depends on realm-level infrastructure

6. **Dialogue Evolution** (2-3 weeks)
   - Enhances existing dialogue
   - Lower priority but valuable
   - Good for polish phase

### 5.2 Shared Infrastructure Needs

All expansions benefit from:

| Infrastructure | Description | Status |
|---------------|-------------|--------|
| Character Data Layer | Personality, backstory, combat prefs | ✅ Complete |
| Relationship Service | Entity relationships | ✅ Complete |
| Realm Context | Realm-level behavioral state | 📋 Needs schema extension |
| Species Overlays | Species-specific trait modifiers | 📋 Needs schema extension |
| ABML Runtime | Core execution engine | ✅ Complete |
| GOAP Planner | A* planning engine | ✅ Complete |

### 5.3 Integration with Existing Services

| Expansion | Primary Services | Secondary Services |
|-----------|-----------------|-------------------|
| Tutorial | New SDK | Analytics (track competence) |
| Quest Gen | Actor, Character, CharacterHistory | Relationship, Location |
| Social | Character, CharacterPersonality, Relationship | Messaging |
| Faction/Economy | Realm (extended), New FactionService | Analytics, State |
| Cinematography | New SDK | Asset (camera definitions) |
| Dialogue | Behavior, CharacterPersonality | Relationship |

---

## Part 6: The Bigger Picture

### 6.1 Alignment with THE_DREAM

These expansions support THE_DREAM vision of **procedural content that feels authored**:

| THE_DREAM Principle | How These Expansions Support It |
|--------------------|--------------------------------|
| Environment exists independently | Quest/Social systems query actual world state |
| Options from actual capabilities | Tutorial observes actual player abilities |
| Graceful degradation | All systems plan with fallback paths |
| Character agents as oracles | Social/Dialogue systems query personality |
| Invisible directors | Faction brains orchestrate macro-level drama |

### 6.2 The Behavioral Intelligence Layer

Together, these create a **comprehensive behavioral intelligence layer**:

```
┌───────────────────────────────────────────────────────────────┐
│                   Player Experience Layer                      │
│  Tutorials | Quests | Dialogues | Social | Economy | Combat   │
└───────────────────────────────────────────────────────────────┘
                              ↑
┌───────────────────────────────────────────────────────────────┐
│              Behavioral Intelligence Layer (ABML/GOAP)         │
│  - Goal-oriented planning                                      │
│  - Personality-weighted decisions                              │
│  - World-state awareness                                       │
│  - Streaming composition                                       │
│  - Intent-based outputs                                        │
└───────────────────────────────────────────────────────────────┘
                              ↑
┌───────────────────────────────────────────────────────────────┐
│                    Character Data Layer                        │
│  Personality | Backstory | Relationships | Combat Prefs       │
└───────────────────────────────────────────────────────────────┘
                              ↑
┌───────────────────────────────────────────────────────────────┐
│                      World State Layer                         │
│  Realm Context | Faction State | Economy | Geography          │
└───────────────────────────────────────────────────────────────┘
```

Every system—from tutorials teaching a new player to factions waging economic war—uses the same architectural patterns, the same personality influences, the same graceful degradation. This consistency is what makes the world feel alive.

---

## Appendix A: Comparison with Industry Approaches

| System | Traditional Approach | ABML/GOAP Approach |
|--------|---------------------|-------------------|
| **Tutorials** | Linear scripts, skip button | Adaptive to player state |
| **Quests** | Designer-placed quest givers | Generated from character/world state |
| **NPC Social** | Friendship meters | Goal-driven relationship building |
| **Economy** | Supply/demand curves | Faction goals driving markets |
| **Camera** | Pre-authored shots | Goal-based shot selection |
| **Dialogue** | Branching trees | Strategy-planned conversations |

---

## Appendix B: Related Documentation

- [ABML Guide](../guides/ABML.md) - Full ABML specification
- [GOAP Guide](../guides/GOAP.md) - GOAP planning details
- [Actor System](../guides/ACTOR_SYSTEM.md) - Actor execution model
- [Music System](../guides/MUSIC_SYSTEM.md) - Music GOAP patterns
- [#383](https://github.com/beyond-immersion/bannou-service/issues/383) - Watcher-Actor integration (regional watcher pattern)
- [THE_DREAM Vision](~/repos/arcadia-kb/THE_DREAM.md) - Core vision document

---

## Part 7: Path to Quests - Foundational Services Analysis

> **Added**: 2026-01-19
> **Context**: Before building a quest system, we need to identify and build the foundational data services that quests depend on.

### 7.1 The Quest Vision

We want a **GOAP-driven quest system** where:
- Quests are generated from character backstory, world state, and relationships
- Quest objectives adapt to character capabilities and personality
- NPCs remember past encounters and reference them in dialogue/quests
- Quests reward meaningful things (items, currency, reputation, growth)
- Tutorials are "meta-quests" teaching players rather than characters

This vision requires **foundational data services** that don't yet exist.

### 7.2 What We Have vs. What We Need

#### Currently Exists (Can Support Quests)

| Service | Quest Role | Status | Notes |
|---------|-----------|--------|-------|
| **Character** | Quest participants | ✅ Complete | Entity ownership, status tracking |
| **Character-Personality** | Capability proxy, goal weighting | ✅ Complete | 8 traits + combat preferences + evolution |
| **Character-History** | Backstory for quest hooks | ✅ Complete | 9 backstory types, event participation |
| **Relationship** | NPC relationships, quest givers | ✅ Complete | Bidirectional, soft-delete, metadata |
| **Relationship-Type** | Relationship taxonomy | ✅ Complete | Hierarchical types |
| **Species** | Character type context | ✅ Complete | Trait modifiers, realm associations |
| **Realm** | World context | ✅ Complete | Top-level world containers |
| **Realm-History** | World lore | ✅ Complete | Historical events, lore elements |
| **Location** | Places for objectives | ✅ Complete | Hierarchical, realm-partitioned |
| **Achievement** | Progress tracking (proxy) | ✅ Complete | Can track quest completion, prerequisites |
| **Analytics (Glicko-2)** | Skill ratings | ✅ Complete | Numerical skill checks |
| **Save-Load** | Quest state persistence | ✅ Complete | Polymorphic ownership, versioning |
| **State** | Hot quest data | ✅ Complete | Redis/MySQL backends |
| **Messaging** | Quest events | ✅ Complete | Event-driven architecture |
| **Actor/Behavior** | NPC quest behaviors | ✅ Complete | ABML/GOAP execution |

#### Critical Gaps (Blockers)

| Service | Quest Role | Status | Effort | Why Critical |
|---------|-----------|--------|--------|--------------|
| **Inventory/Items** | Quest rewards, quest items | ❌ Missing | 3-5 days | "Collect 10 pelts" needs items; "Reward: Sword" needs inventory |
| **Economy/Currency** | Currency rewards | ❌ Missing | 1-2 weeks | "Reward: 500 gold" needs wallets and transactions |
| **Character-Encounters** | Memorable interactions | ❌ Missing | 3-5 days | "We've met before" dialogue; grudge/alliance triggers |
| **Quest Service** | Core lifecycle | ❌ Missing | 3-5 days | Objective tracking, prerequisites, rewards distribution |

#### Not Required (Can Use Proxies)

| System | Traditional Need | Proxy Available | Notes |
|--------|-----------------|-----------------|-------|
| **Skills/Abilities** | "Requires Fireball spell" | Personality traits + Achievements + Glicko-2 | Trait thresholds, achievement prerequisites, skill ratings |
| **Reputation/Factions** | "Loved by Dwarves" | Relationships with metadata | Store reputation score in relationship metadata |
| **Experience/Leveling** | "Level 10 required" | Achievements + Leaderboards | Achievement tiers, leaderboard rankings |

### 7.3 Deep Dive: Missing Foundational Services

#### 7.3.1 Inventory/Items Service (`lib-inventory`)

**Why It's Critical for Quests:**
- Quest rewards need delivery: "Reward: Sword of Darkness"
- Quest prerequisites need checking: "Required: Healing Potion"
- Quest objectives reference items: "Collect 10 Wolf Pelts"
- Trading/delivery quests need item transfer mechanics

**Proposed Architecture:**
```
lib-inventory
├── Item Definitions (catalog)
│   ├── ItemId, Name, Description
│   ├── Type hierarchy (WEAPON → SWORD, BOW; ARMOR → HELMET)
│   ├── Stackable flag, max stack size
│   ├── Rarity, value, weight
│   └── Metadata (stats, effects)
│
├── Inventory Management
│   ├── Polymorphic ownership (Account, Character, Guild, Location)
│   ├── Slot-based or slot-less storage
│   ├── Add, remove, transfer, consume operations
│   ├── Stack management
│   └── Query (by type, by tag, search)
│
├── Events
│   ├── item.acquired
│   ├── item.consumed
│   ├── item.transferred
│   └── item.destroyed
│
└── Integration Points
    ├── Quest Service → reward items
    ├── Economy Service → item value, trading
    ├── ABML/GOAP → ${inventory.has_item}, inventory goals
    └── Save-Load → persist inventory state
```

**Key Endpoints:**
- `/inventory/add` - Add item to inventory
- `/inventory/remove` - Remove item from inventory
- `/inventory/transfer` - Move item between inventories
- `/inventory/query` - Search inventory by criteria
- `/item/definition/create` - Define new item type
- `/item/definition/list` - List item catalog

**ABML Integration:**
```yaml
# Quest objective checking
- cond:
    - when: "${inventory.count('wolf_pelt') >= 10}"
      then:
        - call: complete_objective

# Reward distribution
- inventory_add:
    target: "${character_id}"
    item: "sword_of_darkness"
    quantity: 1
```

---

#### 7.3.2 Economy/Currency Service (`lib-economy`)

**Why It's Critical for Quests:**
- Standard quest reward: "Earn 500 gold"
- Quest prerequisites: "Costs 100 gold to start"
- Store/vendor integration
- Trading quests

**Proposed Architecture:**
```
lib-economy
├── Wallet Management
│   ├── Multi-currency support (gold, gems, tokens)
│   ├── Polymorphic ownership (Account, Character, Guild)
│   ├── Balance queries
│   └── Currency creation/destruction (admin)
│
├── Transactions
│   ├── Idempotent operations (prevent double-spend)
│   ├── Atomic multi-party transfers
│   ├── Transaction ledger (immutable audit trail)
│   └── Escrow for trades
│
├── Store System (Optional)
│   ├── Catalog with pricing
│   ├── Purchase workflow
│   └── Inventory integration
│
├── Events
│   ├── currency.credited
│   ├── currency.debited
│   ├── transaction.completed
│   └── purchase.completed
│
└── Integration Points
    ├── Quest Service → currency rewards
    ├── Inventory Service → item purchases
    ├── ABML/GOAP → ${wallet.gold}, spending goals
    └── Analytics → economy metrics
```

**Key Endpoints:**
- `/economy/wallet/get` - Get wallet balances
- `/economy/credit` - Add currency to wallet
- `/economy/debit` - Remove currency from wallet
- `/economy/transfer` - Move currency between wallets
- `/economy/transaction/history` - Audit trail

**ABML Integration:**
```yaml
# Quest reward
- economy_credit:
    target: "${character_id}"
    currency: "gold"
    amount: 500
    reason: "quest_completion"

# Purchase check
- cond:
    - when: "${wallet.gold >= 100}"
      then:
        - call: allow_purchase
```

---

#### 7.3.3 Character-Encounters Service (`lib-character-encounter`)

**Why It's Critical for Quests:**
- Triggers special dialogue: "We've met before..."
- Enables grudges/alliances: "You killed my brother!"
- Quest hooks: "The merchant you saved has a job for you"
- NPC memory: Characters remember interactions

**The Gap Character-History Doesn't Fill:**

| Dimension | Character-History | Character-Encounters |
|-----------|-------------------|---------------------|
| **Focus** | What happened TO character | What happened BETWEEN characters |
| **Parties** | Single character + world event | Two+ specific characters |
| **Query** | "Events I participated in" | "Who have I met? How?" |
| **Emotional** | Static backstory elements | Dynamic emotional impact per party |
| **Relationship** | Doesn't track | Directly affects relationships |

**Proposed Architecture:**
```
lib-character-encounter
├── Encounter Recording
│   ├── EncounterId, Timestamp, Location
│   ├── Participants[] (character IDs)
│   ├── EncounterType (combat, dialogue, trade, quest, social)
│   ├── Context (what triggered it)
│   └── Outcome (positive, negative, neutral, memorable)
│
├── Per-Participant Perspective
│   ├── CharacterId
│   ├── Emotional impact (pride, fear, gratitude, anger...)
│   ├── Relationship delta
│   ├── Memory strength (decays over time?)
│   └── Remembered as (short description)
│
├── Queries
│   ├── By character: "Who has Character A encountered?"
│   ├── By pair: "What encounters between A and B?"
│   ├── By type: "All combat encounters for A"
│   └── By recency: "Recent encounters in location X"
│
├── Events
│   ├── encounter.recorded
│   ├── encounter.memory.faded (time decay)
│   └── encounter.referenced (used in dialogue/quest)
│
└── Integration Points
    ├── Quest Service → encounter-triggered quests
    ├── Character-Personality → encounter affects personality evolution
    ├── Relationship Service → encounter affects relationship
    ├── ABML/GOAP → ${encounters.has_met('npc_id')}, grudge goals
    └── Dialogue System → "We've met before" awareness
```

**Key Endpoints:**
- `/character-encounter/record` - Record new encounter
- `/character-encounter/query/by-character` - Get character's encounters
- `/character-encounter/query/between` - Get encounters between two characters
- `/character-encounter/get-perspective` - Get specific character's view

**ABML Integration:**
```yaml
# Check for prior encounter
- cond:
    - when: "${encounters.has_met(npc_id)}"
      then:
        - speak:
            text: "Ah, we meet again! I remember you from ${encounters.last_context(npc_id)}."
    - else:
        - speak:
            text: "I don't believe we've met. I am ${npc_name}."

# Grudge-based quest availability
- cond:
    - when: "${encounters.sentiment_toward(villain_id) < -0.5}"
      then:
        - call: offer_revenge_quest
```

---

#### 7.3.4 Quest Service (`lib-quest`)

**Core Responsibilities:**
1. Quest definition management (schema-first)
2. Quest instance lifecycle (available → accepted → in_progress → completed/failed)
3. Objective state machine (multi-step tracking)
4. Prerequisite validation
5. Reward distribution (items, currency, reputation, encounters)
6. Quest giver communication
7. Abandonment/reset mechanics

**Proposed Architecture:**
```
lib-quest
├── Quest Definitions
│   ├── QuestId, Title, Description
│   ├── Category (main, side, daily, tutorial)
│   ├── Prerequisites (achievements, relationships, encounters, items)
│   ├── Objectives[] (ordered or parallel)
│   ├── Rewards (items, currency, reputation, achievements)
│   ├── Branching (choice points with different outcomes)
│   └── Metadata (level range, estimated time, tags)
│
├── Quest Objectives
│   ├── ObjectiveId, Description
│   ├── Type (kill, collect, deliver, discover, talk_to, escort, craft)
│   ├── Target (entity, item, location)
│   ├── Count (current/required)
│   ├── Optional flag
│   └── Completion conditions (GOAP preconditions)
│
├── Quest Instances
│   ├── InstanceId, QuestId, OwnerId (polymorphic)
│   ├── Status (available, accepted, in_progress, completed, failed, abandoned)
│   ├── Objective progress[]
│   ├── Started/Completed timestamps
│   ├── Choice history (for branching)
│   └── Notes/journal entries
│
├── Events
│   ├── quest.available
│   ├── quest.accepted
│   ├── quest.objective.progressed
│   ├── quest.objective.completed
│   ├── quest.completed
│   ├── quest.failed
│   └── quest.abandoned
│
└── Integration Points
    ├── Inventory → item objectives, item rewards
    ├── Economy → currency rewards
    ├── Relationship → reputation changes, quest giver relationships
    ├── Character-Encounters → encounter-triggered quests, encounter rewards
    ├── Achievement → quest completion achievements
    ├── Character-History → record quest participation
    ├── Save-Load → persist quest state
    ├── ABML/GOAP → quest behaviors, objective automation
    └── Actor → NPC quest giver behaviors
```

**Key Endpoints:**
- `/quest/definition/create` - Define new quest
- `/quest/available` - List quests available to entity
- `/quest/accept` - Accept a quest
- `/quest/progress` - Update objective progress
- `/quest/complete` - Mark quest complete, distribute rewards
- `/quest/abandon` - Abandon quest
- `/quest/active` - List active quests for entity

### 7.4 Implementation Sequence

Based on dependencies, here's the recommended build order:

```
Phase 1: Data Foundation (Week 1-2)
├── lib-inventory (3-5 days)
│   └── Items exist, can be owned, transferred
│
└── lib-economy (1-2 weeks, can parallel)
    └── Currency exists, can be transferred

Phase 2: Relationship Extension (Week 2-3)
└── lib-character-encounter (3-5 days)
    └── Depends on: Character, Relationship
    └── Enables: "We've met before" awareness

Phase 3: Quest Core (Week 3-4)
└── lib-quest (3-5 days)
    └── Depends on: Inventory, Economy, Encounters
    └── Enables: Full quest lifecycle

Phase 4: GOAP Integration (Week 4-5)
└── Quest Generation GOAP flows
    └── Depends on: Quest service, all data services
    └── Enables: Procedurally generated quests
```

### 7.5 Data Flow: Complete Quest Lifecycle

```
1. QUEST GENERATION (GOAP-driven)
   ├── Query character backstory (trauma, goals)
   ├── Query relationships (allies, enemies, mentors)
   ├── Query encounters (memorable interactions)
   ├── Query world state (locations, events)
   └── GOAP plans quest that addresses character concerns
       └── Output: Quest definition tailored to this character

2. QUEST AVAILABILITY
   ├── Check prerequisites
   │   ├── Achievements unlocked?
   │   ├── Relationship requirements met?
   │   ├── Previous quests completed?
   │   └── Items/currency available?
   └── Publish: quest.available event

3. QUEST ACCEPTANCE
   ├── Create quest instance
   ├── Initialize objective tracking
   ├── Update character-encounter (met quest giver)
   └── Publish: quest.accepted event

4. QUEST PROGRESS
   ├── Monitor for objective triggers
   │   ├── Kill objective: Listen for combat events
   │   ├── Collect objective: Monitor inventory changes
   │   ├── Deliver objective: Check item + location
   │   ├── Talk objective: Monitor dialogue completion
   │   └── Custom: GOAP precondition checks
   ├── Update progress
   └── Publish: quest.objective.progressed

5. QUEST COMPLETION
   ├── Validate all objectives complete
   ├── Distribute rewards
   │   ├── Inventory: Add reward items
   │   ├── Economy: Credit currency
   │   ├── Relationship: Update reputation
   │   ├── Achievement: Unlock quest achievement
   │   └── Character-History: Record participation
   ├── Record completion encounter
   ├── Unlock dependent quests
   └── Publish: quest.completed event

6. NPC REACTION (Actor System)
   ├── Quest giver behavior changes
   ├── New dialogue options available
   ├── Relationship sentiment updated
   └── Personality potentially evolved
```

### 7.6 ABML/GOAP Quest Integration Examples

#### Quest Objective as GOAP Goal

```yaml
# NPC pursues quest objective
goals:
  complete_delivery_quest:
    priority: 80
    conditions:
      quest_item_delivered: "== true"

flows:
  travel_to_destination:
    goap:
      preconditions:
        has_quest_item: "== true"
        at_destination: "== false"
      effects:
        at_destination: true
      cost: 5

  hand_over_item:
    goap:
      preconditions:
        at_destination: "== true"
        has_quest_item: "== true"
        recipient_present: "== true"
      effects:
        quest_item_delivered: true
        has_quest_item: false
      cost: 1
```

#### Quest Generation from Backstory

```yaml
# Query character data
context:
  variables:
    backstory:
      source: "service:character-history/get-backstory"
    encounters:
      source: "service:character-encounter/query/by-character"
    relationships:
      source: "service:relationship/list-by-entity"

# Generate quest based on character concerns
flows:
  evaluate_quest_hooks:
    - for_each:
        collection: "${backstory.elements}"
        as: "element"
        do:
          - cond:
              - when: "${element.type == 'TRAUMA' && element.strength > 0.6}"
                then:
                  # Strong trauma = potential revenge/closure quest
                  - call: generate_trauma_quest
              - when: "${element.type == 'GOAL' && element.strength > 0.5}"
                then:
                  # Active goal = advancement quest
                  - call: generate_advancement_quest
```

### 7.7 Summary: Path to Victory (Updated 2026-01-23)

**We want quests. Here's what's involved:**

| Layer | Component | Status | Notes |
|-------|-----------|--------|-------|
| **Data** | Item Definitions + Instances | ✅ **Implemented** | Templates, instances, durability, soulbinding, provenance |
| **Data** | Inventory/Containers | ✅ **Implemented** | 6 constraint models, nesting, equipment slots, grid/weight/volume |
| **Data** | Currency/Wallets | ✅ **Implemented** | Multi-currency, autogain, holds, caps, exchange, full audit |
| **Data** | Contracts/Agreements | ✅ **Implemented** | Reactive milestones, guardians, breach/cure, clause extensibility |
| **Data** | Character-Encounters | ✅ **Implemented** | Multi-participant, per-perspective emotions, memory decay |
| **Orchestration** | Escrow Service | 📋 **Spec Complete** | Full-custody vault, per-party infrastructure, contract-driven finalization |
| **Core** | Quest Service | ❌ Missing | Objective tracking, prerequisites, rewards distribution |
| **Intelligence** | Quest Generation GOAP | ❌ Design only | Procedurally generated quests from character/world state |
| **Foundation** | Character/Personality/History | ✅ Complete | - |
| **Foundation** | Relationships/Species/Realm | ✅ Complete | - |
| **Foundation** | Actor/Behavior/GOAP | ✅ Complete | - |
| **Foundation** | Save-Load/State/Messaging | ✅ Complete | - |

**Remaining effort to quest-ready: lib-quest service (3-5 days) + GOAP integration (1-2 weeks)**

The foundation is now complete. We have the "stuff" layer (items, currency), the "agreements" layer (contracts), and the "memory" layer (encounters). The Quest service is now a thin orchestration layer atop a rich ecosystem.

---

## Part 8: Implementation Analysis - The Foundational Layer (2026-01-23)

> **Context**: Five new services implemented: lib-currency, lib-contract, lib-item, lib-inventory, lib-character-encounter. This section analyzes what was built, how it compares to industry patterns, and what it means for ABML/GOAP integration.

### 8.1 Architecture Summary: What Was Built

```
┌─────────────────────────────────────────────────────────────────────────┐
│                       AGREEMENTS LAYER                                   │
│                                                                          │
│  lib-contract                                                            │
│  ├── Templates (reusable agreement patterns)                             │
│  ├── Instances (active contracts between parties)                        │
│  ├── Milestones (progressive obligation checkpoints)                     │
│  ├── Breaches (failure detection with grace/cure)                        │
│  ├── Guardians (escrow custody, party transfer)                          │
│  └── Clause Types (extensible validation/execution plugins)              │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
                              ↕
┌─────────────────────────────────────────────────────────────────────────┐
│                        VALUE LAYER                                        │
│                                                                          │
│  lib-currency                          lib-item + lib-inventory          │
│  ├── Definitions (precision,           ├── Templates (catalog)            │
│  │   scope, exchange rates)            ├── Instances (durability,         │
│  ├── Wallets (polymorphic,             │   binding, provenance)           │
│  │   multi-currency)                   ├── Containers (6 types:           │
│  ├── Transactions (immutable,          │   slot/weight/grid/vol/...)      │
│  │   idempotent, event-sourced)        ├── Equipment Slots                │
│  ├── Autogain (passive income)         ├── Nesting (bags in bags)         │
│  ├── Holds (pre-auth reserves)         └── Ownership derivation           │
│  └── Caps (earn/wallet/supply)                                            │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
                              ↕
┌─────────────────────────────────────────────────────────────────────────┐
│                        MEMORY LAYER                                       │
│                                                                          │
│  lib-character-encounter                                                 │
│  ├── Shared encounter records (what happened)                            │
│  ├── Per-participant perspectives (how each felt)                        │
│  ├── Memory decay (significance fades over time)                         │
│  ├── Sentiment aggregation (weighted relationship score)                 │
│  └── Lazy processing (decay on access, no background jobs)               │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### 8.2 Industry Comparison & Analysis

#### lib-currency vs. Industry Standard

| Feature | Typical Game Currency | lib-currency |
|---------|----------------------|--------------|
| Multi-currency | Usually 2-3 hardcoded | Unlimited, schema-defined, scoped to realms |
| Transaction history | Often none or limited | Full event-sourced immutable ledger |
| Exchange rates | Fixed or nonexistent | Dynamic base-currency intermediary conversion |
| Earn caps | Hardcoded daily limits | Configurable daily/weekly with reset times |
| Pre-auth holds | Rare (gas station pattern) | Full hold/capture/release lifecycle |
| Autogain/interest | Usually timer-based items | Native simple/compound modes with lazy/task processing |
| Negative balances | Never | Configurable per currency (debt is valid) |
| Supply tracking | Rare | Global supply caps with Gini coefficient analytics |

**Assessment**: This goes well beyond typical game currency systems. The authorization holds pattern comes from payment processing (Stripe, Square), and autogain with compound interest is more fintech than gamedev. The earn cap system prevents inflation without hardcoding limits. The explicit separation from escrow/marketplace concerns shows mature domain modeling.

#### lib-item + lib-inventory vs. Industry Standard

| Feature | Typical Inventory | lib-item + lib-inventory |
|---------|-------------------|--------------------------|
| Item definition | Usually one model | Template (catalog) + Instance (occurrence) split |
| Storage model | Fixed slots | 6 models: slot, weight, grid, slot+weight, volumetric, unlimited |
| Ownership | Direct property | Derived from container (ownership follows placement) |
| Nesting | Rare (bags in bags) | Full nesting with depth limits and weight propagation |
| Quantity | Integer stacks | Three models: unique, discrete (int), continuous (float) |
| Binding | Simple boolean | on_pickup, on_equip, on_use, none |
| Provenance | None | Full origin tracking (loot, quest, craft, trade, purchase, spawn) |
| Equipment | Separate system | Equipment slots are just specialized containers |

**Assessment**: The container-based ownership derivation is elegant. Most systems store `ownerId` on items directly, creating update cascades when items change hands. Here, ownership is implicit from container membership, so transferring items between containers naturally transfers ownership. The six constraint models mean the same service handles Diablo-style grid inventories, Skyrim weight limits, and Tetris-style puzzle placement.

The continuous quantity model is unusual and enables things like "2.5 liters of health potion" or "0.3 kg of gold dust" - materials and fluids that most games handle poorly or not at all.

#### lib-character-encounter vs. Industry Standard

| Feature | Typical NPC Memory | lib-character-encounter |
|---------|-------------------|-------------------------|
| Structure | Per-NPC friendship meters | Multi-participant shared records |
| Perspectives | Same event = same opinion | Each participant has independent emotional response |
| Memory model | Static (never fades) | Configurable decay with refresh on recall |
| Sentiment | Simple +/- integer | Weighted aggregation across all encounters |
| Scalability | Per-relationship pair | O(N) per encounter, efficient pair indexing |
| Character deletion | Often leaks data | Event-driven cleanup of all perspectives |

**Assessment**: The "one record, N perspectives" pattern is the key innovation. Most games either store nothing (NPCs are stateless) or store per-NPC opinion meters (Skyrim's relationship system). Having per-participant perspectives on shared events means two characters can have a combat encounter where one feels respect and the other feels anger. This is the foundation for believable grudges, debts, and alliances.

The lazy memory decay is pragmatic - no background jobs needed, works at any scale, and naturally prioritizes recent encounters in sentiment calculations.

### 8.3 lib-contract: The Standout Innovation

This is the service that warrants detailed analysis. It's genuinely novel in the game systems space.

#### What Makes It Different

Most game systems handle agreements in one of three ways:

1. **Transactional**: Instant exchange (trade window, auction house)
2. **Quest-like**: Linear progression toward reward (fetch quest)
3. **Subscription**: Time-based access (guild membership)

lib-contract unifies all of these into a **single reactive agreement engine**:

```
Traditional Systems:          lib-contract:

Trade Window → instant        Contract with instant milestones
Quest → linear steps          Contract with ordered milestones
Employment → time-based       Contract with recurring payment terms
Alliance → binary state       Contract with mutual obligations
Bounty → conditional          Contract with evidence-based milestone
Apprenticeship → progression  Contract with skill-verification clauses
```

#### The Four Innovations

**1. Contracts as Transferable Assets (Guardian System)**

In most games, agreements are ephemeral metadata. In lib-contract, a contract can be **locked under a guardian's custody**, making it a first-class transferable object:

```
Landlord sells building →
  Guardian (escrow) locks the lease contract →
  Party role "landlord" transfers to buyer →
  Tenants continue undisturbed →
  Contract unlocked under new owner
```

This enables property markets, business acquisitions, and debt trading without per-feature code. The guardian can be any entity type - an escrow service, a court system, a guild leader.

**2. Extensible Clause Types (Plugin-for-Plugins)**

Rather than hardcoding what contracts can validate or execute, the clause type system lets games register arbitrary clause handlers:

```yaml
RegisterClauseType:
  typeCode: "npc_recruitment_bounty"
  category: "both"  # validation + execution
  validationHandler:
    serviceName: "character"
    endpoint: "/character/get"
    # Check character exists and is available
  executionHandler:
    serviceName: "relationship"
    endpoint: "/relationship/create"
    # Create employment relationship on fulfillment
```

This means lib-contract is a **platform** rather than a feature. New contract types emerge from registering new clause handlers without modifying the contract service itself.

**3. Three-Outcome Validation (Transient Failure Awareness)**

Most systems are binary: condition met or not. lib-contract adds a third state:

| Outcome | Meaning | Action |
|---------|---------|--------|
| **Success** | Condition verified | Proceed normally |
| **Permanent Failure** | Condition violated | Trigger breach |
| **Transient Failure** | Service unavailable | Retry later, don't breach |

This is critical for distributed game systems where a validation service might be temporarily unreachable. Without this, network hiccups would trigger false breaches.

**4. Reactive Philosophy with Prebound APIs**

Contracts don't poll or check themselves. External systems tell contracts what happened:

```
Game Server: "Milestone 'deliver_package' completed"
  → Contract advances milestone
  → Calls prebound API: economy/credit (reward)
  → Calls prebound API: relationship/update (reputation)
  → Publishes MilestoneCompletedEvent
  → Next milestone becomes active
```

The **prebound API** pattern (with template variable substitution) means contract templates can define exactly what happens at each stage without the contract service needing to know about currency, inventory, or any other service.

#### Industry Precedents (Where This Overlaps)

| System | Similarity | Difference |
|--------|-----------|------------|
| **Ethereum Smart Contracts** | Self-executing agreements | lib-contract is reactive, not autonomous; no blockchain overhead |
| **SAP Contract Management** | Milestone-based progression | lib-contract is for game entities, not humans; real-time performance |
| **World of Warcraft Guilds** | Role-based membership | lib-contract generalizes to any agreement, not just groups |
| **EVE Online Contracts** | Player-to-player agreements | EVE's are item-exchange focused; lib-contract handles ongoing obligations |
| **Legal Smart Contracts (OpenLaw)** | Template-based, clause-driven | Same architecture; lib-contract applies it to virtual worlds |

The closest real-world analog is probably **Ricardian Contracts** (Ian Grigg, 1996) - human-readable agreements with machine-executable terms. lib-contract implements this concept for game entities, where the "human readable" part is replaced by NPC cognition via ABML.

### 8.4 ABML/GOAP Integration Opportunities (Updated)

With these services in place, the ABML/GOAP integration picture is now much richer:

#### Contracts as GOAP World State

```yaml
# NPC merchant brain - contract-aware goals
goals:
  fulfill_trade_contract:
    priority: 85
    conditions:
      active_contract_milestones_remaining: "== 0"

  negotiate_better_terms:
    priority: 60
    conditions:
      contract_profit_margin: "> 0.2"

flows:
  deliver_contracted_goods:
    goap:
      preconditions:
        has_contracted_items: "== true"
        delivery_location_reachable: "== true"
        active_contract_exists: "== true"
      effects:
        active_contract_milestones_remaining: "-1"
        reputation_with_client: "+0.1"
      cost: 3

  report_breach:
    goap:
      preconditions:
        counterparty_violated_terms: "== true"
        breach_evidence_available: "== true"
      effects:
        contract_breach_filed: true
        relationship_damaged: true
      cost: 8  # High cost - NPCs prefer to work things out
```

#### Encounters Triggering Quest Contracts

```yaml
# After combat encounter with memorable outcome
- cond:
    - when: "${encounters.sentiment_toward(defeated_npc) > 0.3}"
      then:
        # Defeated enemy respects you - offer contract
        - service_call:
            service: "contract"
            endpoint: "/contract/propose"
            data:
              templateId: "employment_bodyguard"
              parties:
                - entityId: "${character_id}"
                  role: "employer"
                - entityId: "${defeated_npc}"
                  role: "employee"
```

#### Inventory-Aware NPC Behavior

```yaml
# Merchant NPC adjusts behavior based on inventory state
- cond:
    - when: "${inventory.count_by_category('weapon') < 3}"
      then:
        # Low stock - prioritize restocking
        - set:
            variable: restock_urgency
            value: 0.9
        - call: seek_supplier
    - when: "${inventory.count_by_category('weapon') > 20}"
      then:
        # Overstocked - lower prices
        - set:
            variable: price_modifier
            value: 0.8
        - call: announce_sale
```

#### Currency-Driven Decision Making

```yaml
# NPC evaluates whether to accept quest based on wallet
- cond:
    - when: "${wallet.gold < 50}"
      then:
        # Desperate - accept any paying work
        - set:
            variable: quest_acceptance_threshold
            value: 0.1
    - when: "${wallet.gold > 1000}"
      then:
        # Wealthy - only interesting quests
        - set:
            variable: quest_acceptance_threshold
            value: 0.8
```

### 8.5 What This Means for the Quest Service

With these five services in place, the quest service becomes surprisingly thin. Most of what a "quest" does is actually handled by the ecosystem:

| Quest Feature | Handled By |
|---------------|------------|
| "Collect 10 wolf pelts" | lib-inventory `hasItems` query |
| "Deliver to location X" | lib-inventory `transferItem` + lib-location |
| "Earn 500 gold reward" | lib-currency `credit` |
| "Receive Sword of Darkness" | lib-item `createInstance` → lib-inventory `addItemToContainer` |
| "NPC remembers you helped" | lib-character-encounter `recordEncounter` |
| "Ongoing employment quest" | lib-contract (milestone-based progression) |
| "Reputation with guild +50" | lib-relationship metadata update |
| "NPC offers follow-up quest" | lib-character-encounter sentiment query → ABML/GOAP |

**The quest service primarily needs to:**
1. Define quest templates (objectives, rewards, prerequisites)
2. Track active quest instances per character
3. Validate prerequisites (query other services)
4. Distribute rewards on completion (call other services)
5. Publish events for NPC reactivity

Many quest patterns can be expressed directly as **contracts**:
- A bounty quest is a contract with a "kill target" milestone
- An escort quest is a contract with "reach destination" milestone
- A crafting quest is a contract with "deliver crafted item" milestone

The question becomes: **do we need a separate lib-quest, or are quests just a specific contract pattern?**

### 8.6 Quests as Contracts: The Convergence Question

Consider a bounty quest expressed as a contract:

```
Template: "bounty_contract"
Terms:
  duration: P7D (7 day deadline)
  paymentSchedule: milestone_based
  terminationPolicy: unilateral_with_notice

Parties:
  - Quest Giver (role: "client")
  - Player Character (role: "contractor")

Milestones:
  1. "accept_bounty" (auto-completed on contract acceptance)
  2. "locate_target" (completed when player enters target location)
  3. "defeat_target" (completed when combat encounter recorded)
  4. "return_evidence" (completed when specific item delivered)

Clauses:
  - type: "currency_transfer" (500 gold on milestone 4)
  - type: "item_transfer" (bounty token on milestone 1)
  - type: "reputation_grant" (guild rep on completion)
```

**This IS a quest, expressed as a contract.** The milestone system provides progression tracking. The clause system provides rewards. The breach system handles failure/timeout. The party system handles quest givers and participants.

**What a dedicated lib-quest would add on top:**
- Quest discovery/availability (what's available to me?)
- Quest log UI integration (list active/completed)
- Categorization (main story, side quest, daily, tutorial)
- Prerequisites beyond contract scope (achievement-gated, backstory-gated)
- GOAP-based procedural generation of quest contracts

This suggests **lib-quest is a thin orchestration layer** that generates and manages contracts rather than reimplementing progression tracking itself.

---

## Part 9: lib-escrow - The Missing Custody Layer (2026-01-23)

> **Context**: lib-escrow provides a critical architectural layer between application logic (trades, quests, markets) and the foundational services (contracts, currency, inventory). See `docs/planning/ECONOMY_CURRENCY_ARCHITECTURE.md` for the full escrow integration plan and foundation completion prerequisites.

### 9.1 The Key Insight: "Contract is Brain, Escrow is Vault"

The spec's core design philosophy:

```
"lib-escrow is a full-custody orchestration layer that sits ABOVE the
foundational asset plugins. It creates its own wallets, containers, and
contract locks to take complete possession of assets during multi-party
agreements."
```

This separation is elegant:

| Component | Responsibility | What It Knows |
|-----------|---------------|---------------|
| **lib-escrow** | Physical custody, consent flows, tokens | How to hold, release, and refund assets |
| **lib-contract** | Terms, conditions, distribution rules | What assets are needed, when to release, how to split |
| **lib-currency** | Currency operations | Nothing about escrow or contracts |
| **lib-inventory** | Item operations | Nothing about escrow or contracts |

### 9.2 How It Works

**Per-Party Infrastructure**: When an escrow is created, it generates dedicated wallets and containers for EACH party:

```
Escrow Agreement (entity owner)
├── Party A Escrow Wallet (created by escrow, owned by escrow)
├── Party A Escrow Container (created by escrow, owned by escrow)
├── Party B Escrow Wallet (created by escrow, owned by escrow)
└── Party B Escrow Container (created by escrow, owned by escrow)
```

This enables:
- **Clean refunds**: Party A's escrow → Party A (no cross-party confusion)
- **Contribution tracking**: Verify each party deposited their share
- **Ownership validation**: Parties can only withdraw from their own escrow

**Contract-Driven Finalization**: When conditions are met:

```
1. Escrow transitions to FINALIZING
2. Calls POST /contract/instance/execute on bound contract
3. Contract handles ALL distribution:
   - Fee clauses: Move fees from escrow wallets to fee recipients
   - Distribution clauses: Move remaining assets to recipients
4. Escrow verifies all escrow wallets/containers are empty
5. Escrow transitions to RELEASED
```

**Template Variables**: The escrow sets template values on the bound contract:

```yaml
templateValues:
  EscrowId: "{{agreementId}}"
  PartyA_EscrowWalletId: "{{partyA.escrowWalletId}}"
  PartyA_EscrowContainerId: "{{partyA.escrowContainerId}}"
  PartyB_WalletId: "{{partyB.walletId}}"  # Destination
  PartyB_ContainerId: "{{partyB.containerId}}"  # Destination
```

The contract's clauses reference these variables, enabling fully dynamic distribution rules.

### 9.3 The Full Plugin Stack

The spec shows the complete dependency hierarchy:

```
┌─────────────────────────────────────────────────────────────┐
│                    APPLICATION LAYER                         │
│  lib-market (auctions)  |  lib-trade (P2P)  |  lib-quest    │
│  (All thin orchestrators that generate escrows)              │
└─────────────────────────────────┬───────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────┐
│                    CUSTODY LAYER                             │
│                                                              │
│                      lib-escrow                              │
│  ├── Full-custody orchestration ("the vault")                │
│  ├── Per-party wallets and containers                        │
│  ├── Token-based consent flows                               │
│  ├── Trust modes (full_consent, initiator_trusted, etc.)     │
│  ├── Periodic asset validation                               │
│  └── Contract-driven finalization                            │
│                                                              │
└─────────────────────────────────┬───────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────┐
│                    LOGIC LAYER                               │
│                                                              │
│                     lib-contract                             │
│  ├── Agreement terms ("the brain")                           │
│  ├── Milestone tracking                                      │
│  ├── Asset requirement clauses (validation)                  │
│  ├── Fee/distribution clauses (execution)                    │
│  └── Prebound API handlers                                   │
│                                                              │
└─────────────────────────────────┬───────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────┐
│                    ASSET LAYER                               │
│                                                              │
│  lib-currency        lib-inventory        lib-item           │
│  (wallets,           (containers,         (templates,        │
│   transfers)          movement)            instances)         │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

### 9.4 Why This Matters for Quests

With lib-escrow in the picture, the quest architecture becomes clearer:

**A quest reward escrow**:
1. Quest service creates escrow with `trustMode: initiator_trusted`
2. Quest giver (NPC) deposits reward gold/items into their escrow container
3. Bound contract defines: "distribute when 'kill_target' milestone completes"
4. Player completes quest objective → contract milestone fulfilled
5. Contract executes → rewards flow from NPC escrow to player inventory
6. Escrow closes, infrastructure cleaned up

**A player trade**:
1. Trade service creates escrow with `trustMode: full_consent`
2. Each party deposits their side into their escrow container
3. Bound contract defines: "swap when both parties consent"
4. Both use release tokens → contract executes cross-distribution
5. Each party receives the other's deposits

**A guild tax collection**:
1. Guild creates escrow for monthly dues with `trustMode: initiator_trusted`
2. Bound contract defines fee clauses (5% to realm treasury, 95% to guild)
3. Members deposit dues → contract executes automatic fee distribution

### 9.5 ABML/GOAP Integration with Escrow

**NPC Trade Negotiation (GOAP + Escrow)**:

```yaml
# NPC Brain decides to trade
goals:
  - id: acquire_rare_item
    conditions:
      - has_rare_item: true
    priority: 75

actions:
  - id: propose_trade
    preconditions:
      - has_item_npc_wants: true
      - known_trader_nearby: true
    effects:
      - has_rare_item: true
      - gold: -500
    cost: 10
    abml_behavior: "trade_negotiation"
```

**Trade Behavior Execution (ABML + Escrow)**:

```yaml
# trade_negotiation.behavior.yaml
document_type: behavior

flows:
  main:
    # Create escrow via Shortcut API
    - shortcut:
        api: escrow.create
        request:
          escrow_type: two_party
          trust_mode: full_consent
          parties:
            - party_id: "${actor.id}"
              party_type: character
              role: depositor_recipient
            - party_id: "${target.id}"
              party_type: character
              role: depositor_recipient
          bound_contract_id: "${contract_templates.simple_trade}"
        response_var: escrow

    # Get our deposit token via shortcut
    - shortcut:
        api: escrow.get_my_token
        request:
          escrow_id: "${escrow.id}"
          token_type: deposit
        response_var: our_token

    # Deposit our side
    - shortcut:
        api: escrow.deposit
        request:
          escrow_id: "${escrow.id}"
          deposit_token: "${our_token.token}"
          assets:
            - asset_type: currency
              currency_code: gold
              amount: 500

    # Wait for other party
    - wait_for:
        event: escrow.fully_funded
        timeout: PT5M
        on_timeout:
          - call: abort_trade

    # Consent to release
    - shortcut:
        api: escrow.consent
        request:
          escrow_id: "${escrow.id}"
          consent_type: release
          release_token: "${release_token}"
```

### 9.6 Status Summary

| Service | Status | Role |
|---------|--------|------|
| lib-escrow | 📋 Spec Complete (v3.0.0) | Custody orchestration, consent flows, vault |
| lib-contract | ✅ Implemented | Agreement logic, distribution rules, brain |
| lib-currency | ✅ Implemented | Currency operations |
| lib-inventory | ✅ Implemented | Item container operations |
| lib-item | ✅ Implemented | Item template/instance |
| lib-quest | ❌ Not Started | Quest discovery, logs, GOAP generation |
| lib-trade | ❌ Not Started | P2P trade UI orchestration |
| lib-market | ❌ Not Started | Auction/marketplace orchestration |

**Implementation Order Recommendation**:
1. ✅ Foundation layer (currency, item, inventory) - DONE
2. ✅ Logic layer (contract) - DONE
3. **lib-escrow** - Next priority (enables everything above it)
4. lib-quest (thin layer over escrow+contract)
5. lib-trade / lib-market (thin layers over escrow)

---

## Appendix C: Service Dependency Graph (Updated 2026-01-23)

```
┌─────────────────────────────────────────────────────────────────────┐
│                    BEHAVIORAL INTELLIGENCE LAYER                     │
│  Actor | Behavior | ABML | GOAP | Music | Cinematography            │
└────────────────────────────────┬────────────────────────────────────┘
                                 │ queries / drives
                                 ▼
┌─────────────────────────────────────────────────────────────────────┐
│                   APPLICATION / THIN ORCHESTRATION                   │
│  lib-quest (❌)    lib-trade (❌)    lib-market (❌)                  │
│  Quest discovery,  P2P trade UI,     Auction/listing               │
│  logs, GOAP gen    negotiation       management                     │
└────────────────────────────────┬────────────────────────────────────┘
                                 │ creates / manages
                                 ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        CUSTODY LAYER                                 │
│  lib-escrow (📋 Spec Complete)                                       │
│  Full custody, per-party wallets/containers, consent tokens,        │
│  trust modes, periodic validation, contract-driven finalization     │
│  "THE VAULT"                                                        │
└────────────────────────────────┬────────────────────────────────────┘
                                 │ delegates logic to
                                 ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        LOGIC LAYER                                   │
│  lib-contract (✅ Implemented)                                       │
│  Templates, instances, milestones, breaches, guardians,             │
│  clause types with prebound API execution                           │
│  "THE BRAIN"                                                        │
└────────────────────────────────┬────────────────────────────────────┘
                                 │ operates on
                                 ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        ASSET LAYER                                   │
│  lib-currency (✅)   lib-item (✅)    lib-inventory (✅)             │
│  Wallets, transfers, Templates,       Containers,                   │
│  holds, autogain     instances        equipment, nesting            │
└────────────────────────────────┬────────────────────────────────────┘
                                 │ + memory layer
                                 ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        MEMORY LAYER                                  │
│  lib-character-encounter (✅ Implemented)                            │
│  Multi-participant records, per-perspective emotions, decay         │
└────────────────────────────────┬────────────────────────────────────┘
                                 │ references / scoped by
                                 ▼
┌─────────────────────────────────────────────────────────────────────┐
│                       ENTITY FOUNDATION LAYER                        │
│  Character | Personality | History | Relationship | Species          │
│  Realm | Realm-History | Location | Account                         │
└────────────────────────────────┬────────────────────────────────────┘
                                 │ persisted / routed by
                                 ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      INFRASTRUCTURE LAYER                            │
│  State | Messaging | Mesh | Connect | Save-Load | Asset             │
└─────────────────────────────────────────────────────────────────────┘
```

### Implementation Status Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Implemented and working |
| 📋 | Spec complete, not yet implemented |
| ❌ | Not started |

---

*This document should be updated as priorities shift and implementations progress.*
