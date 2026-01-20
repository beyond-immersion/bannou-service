# ABML/GOAP Expansion Opportunities

> **Created**: 2026-01-19
> **Last Updated**: 2026-01-19
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
- [Regional Watchers](./REGIONAL_WATCHERS_BEHAVIOR.md) - God/watcher pattern
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

### 7.7 Summary: Path to Victory

**We want quests. Here's what's involved:**

| Layer | Component | Status | Effort | Priority |
|-------|-----------|--------|--------|----------|
| **Data** | Inventory/Items | ❌ Missing | 3-5 days | **1st** |
| **Data** | Economy/Currency | ❌ Missing | 1-2 weeks | **2nd** |
| **Data** | Character-Encounters | ❌ Missing | 3-5 days | **3rd** |
| **Core** | Quest Service | ❌ Missing | 3-5 days | **4th** |
| **Intelligence** | Quest Generation GOAP | ❌ Design only | 1-2 weeks | **5th** |
| **Foundation** | Character/Personality/History | ✅ Complete | - | - |
| **Foundation** | Relationships/Species/Realm | ✅ Complete | - | - |
| **Foundation** | Actor/Behavior/GOAP | ✅ Complete | - | - |
| **Foundation** | Save-Load/State/Messaging | ✅ Complete | - | - |

**Total estimated effort to quest-ready: 4-6 weeks**

The good news: Our behavioral intelligence layer (ABML/GOAP) is ready. Our entity foundation (characters, relationships, locations) is ready. We're missing the **"stuff" layer** (items, currency) and the **"memory" layer** (encounters) that make quests meaningful.

Once those foundations exist, the Quest service becomes a relatively thin orchestration layer that leverages everything else.

---

*This document should be updated as priorities shift and implementations progress.*
