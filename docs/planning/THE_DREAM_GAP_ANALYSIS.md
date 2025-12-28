# THE DREAM - Gap Analysis

> **Status**: ANALYSIS DOCUMENT
> **Created**: 2025-12-28
> **Related**: [THE_DREAM.md](./THE_DREAM.md), [ACTORS_PLUGIN_V2](./UPCOMING_-_ACTORS_PLUGIN_V2.md), [BEHAVIOR_PLUGIN_V2](./UPCOMING_-_BEHAVIOR_PLUGIN_V2.md)

This document analyzes the gaps between what THE_DREAM requires and what the current plugin designs provide.

---

## 1. Executive Summary

THE_DREAM describes a procedural cinematic exchange system requiring these major capabilities:

| Capability | Current Status | Owner Plugin |
|------------|----------------|--------------|
| Event Brain Actor | **Missing** | Actors Plugin |
| Regional Watcher | **Missing** | Actors Plugin |
| Character Agent Query Interface | **Missing** | Behavior Plugin |
| Event Tap Pattern | Partial (personal channels exist) | Actors Plugin |
| Orchestrator Spawning | Mentioned but underdocumented | Actors Plugin |
| Environmental Query Actions | **Missing** | ABML / Map Service |
| Dynamic Option Presentation | **Missing** | ABML / Behavior Plugin |
| Exchange Protocol Primitives | **Missing** | ABML |
| Affordance Binding | **Missing** | ABML / Behavior Plugin |
| VIP Tracking | **Missing** | Actors Plugin |

---

## 2. ACTORS_PLUGIN_V2 Gaps

### 2.1 What's Already There (Reusable)

The Actors Plugin V2 provides solid infrastructure that THE_DREAM can build on:

| Feature | Description | THE_DREAM Usage |
|---------|-------------|-----------------|
| Actor Identity | `{type}:{id}` addressing | `event-brain:exchange-123` |
| Turn-Based Processing | Sequential mailbox processing | Combat state machine |
| State Persistence | lib-state integration | Exchange state, combat logs |
| Event Subscriptions | `SubscribeAsync<T>()` | Tap into character events |
| Personal Channels | `actor.personal.{type}.{id}` | Direct character queries |
| Scheduled Callbacks | `ScheduleOnceAsync()` | Phase timeouts |
| Placement Service | Centralized single-instance guarantee | Event Brain uniqueness |

### 2.2 Missing: Event Brain Actor Type

**Gap**: No schema exists for the Event Brain orchestrator actor.

**Required Schema** (`schemas/actor-types/event-brain.yaml`):

```yaml
x-actor-type:
  name: event-brain
  idle_timeout_seconds: 60  # Short - exchanges don't last long
  mailbox_capacity: 200     # High - combat events come fast
  mailbox_full_mode: drop_oldest

  state:
    $ref: '#/components/schemas/EventBrainState'

  subscriptions:
    # Tap into participant event streams
    - topic_pattern: "actor.combat.*"  # Dynamic subscription

  schedules:
    - name: phase-timeout
      message_type: phase-timeout
      # Scheduled dynamically, not fixed interval

paths:
  /participant-joined:
    post:
      operationId: HandleParticipantJoined
      x-actor-message: participant-joined
      # ...

  /participant-action:
    post:
      operationId: HandleParticipantAction
      x-actor-message: participant-action
      # ...

  /environment-changed:
    post:
      operationId: HandleEnvironmentChanged
      x-actor-message: environment-changed
      # ...

  /query-options:
    post:
      operationId: HandleQueryOptions
      x-actor-message: query-options
      # Request-response for character agent queries

components:
  schemas:
    EventBrainState:
      type: object
      properties:
        exchange_id:
          type: string
        participants:
          type: array
          items:
            $ref: '#/components/schemas/ExchangeParticipant'
        phase:
          type: string
          enum: [setup, action_selection, resolution, aftermath]
        environment_cache:
          $ref: '#/components/schemas/EnvironmentSnapshot'
        combat_log:
          type: array
          items:
            $ref: '#/components/schemas/CombatLogEntry'
        # ...
```

### 2.3 Missing: Regional Watcher Actor Type

**Gap**: No schema for the area-monitoring actor that spawns Event Agents.

**Required Schema** (`schemas/actor-types/regional-watcher.yaml`):

```yaml
x-actor-type:
  name: regional-watcher
  idle_timeout_seconds: 0  # Never deactivates - always watching
  mailbox_capacity: 500

  state:
    $ref: '#/components/schemas/RegionalWatcherState'

  subscriptions:
    - topic: "map.region.{region_id}.updates"  # Dynamic region subscription
    - topic: "character.proximity.*"
    - topic: "character.antagonism.*"

paths:
  /evaluate-situation:
    post:
      operationId: HandleEvaluateSituation
      x-actor-message: evaluate-situation
      # Checks if Event Agent should spawn

  /spawn-event-agent:
    post:
      operationId: HandleSpawnEventAgent
      x-actor-message: spawn-event-agent
      # Requests Orchestrator to spin up Event Agent

components:
  schemas:
    RegionalWatcherState:
      type: object
      properties:
        region_id:
          type: string
        watched_characters:
          type: array
          items:
            type: string
        active_event_agents:
          type: array
          items:
            $ref: '#/components/schemas/EventAgentRef'
        vip_list:
          type: array
          description: Characters that always have Event Agents
          items:
            type: string
        interestingness_thresholds:
          $ref: '#/components/schemas/InterestingnessConfig'
```

### 2.4 Missing: VIP Registry

**Gap**: No mechanism to track which characters should always have Event Agents.

**Design Options**:

1. **Actor State** (Recommended): Regional Watcher maintains VIP list in state
2. **Separate Service**: New `vip-tracking` service
3. **Character Metadata**: Flag on character data

**Recommendation**: Store in Regional Watcher state + query from Character service for initial population.

### 2.5 Missing: Orchestrator Spawning Documentation

**Gap**: The plugin references Orchestrator integration but lacks details on:
- How to request a new Bannou instance with specific plugins
- How to get the unique app-id for the spawned instance
- How to route API calls to that specific instance

**Required Additions to ACTORS_PLUGIN_V2.md**:

```markdown
### Orchestrator Integration for Dynamic Instance Spawning

When an actor needs to spawn a specialized processing instance (e.g., Event Agent):

#### 1. Request Instance via Orchestrator API

```csharp
// Request new instance with specific configuration
var spawnRequest = new SpawnInstanceRequest
{
    InstanceType = "event-agent",
    Plugins = new[] { "lib-event-agent" },
    InitialContext = new Dictionary<string, object>
    {
        ["exchange_id"] = exchangeId,
        ["participants"] = participantIds
    }
};

var spawnResult = await _orchestratorClient.SpawnInstanceAsync(spawnRequest, ct);

// spawnResult contains:
// - InstanceId: unique identifier
// - AppId: app-id for mesh routing (e.g., "event-agent-abc123")
// - Endpoint: direct HTTP endpoint (if needed)
```

#### 2. Route API Calls to Specific Instance

```csharp
// Use app-id in mesh invocation
var response = await _meshClient.InvokeMethodAsync<Request, Response>(
    appId: spawnResult.AppId,  // Target specific instance
    method: "process-exchange",
    request,
    ct);
```

#### 3. Instance Lifecycle

- Instance reports heartbeat to Orchestrator
- Orchestrator tracks instance health
- Instance can self-terminate when work complete
- Orchestrator can force-terminate unhealthy instances
```

### 2.6 Gap: Event Tap Pattern Clarification

**Current State**: Personal channels exist (`actor.personal.{type}.{id}`) but no explicit "tap" pattern.

**What THE_DREAM Needs**: Event Agent subscribes to a character's combat event stream.

**Solution**: This already works via lib-messaging fanout. Document the pattern:

```markdown
### Event Tap Pattern (Character Event Streams)

Characters emit events to their personal topic:

```csharp
// In character agent actor
await MessageBus.PublishAsync(
    $"character.events.{characterId}",
    new CharacterCombatEvent { ... });
```

Event Agents subscribe to character event streams:

```csharp
// In event brain actor - tap into participant streams
foreach (var participant in exchange.Participants)
{
    await SubscribeAsync<CharacterCombatEvent>(
        $"character.events.{participant.CharacterId}",
        ct);
}
```

This uses existing lib-messaging fanout exchange pattern - no new infrastructure needed.
```

---

## 3. BEHAVIOR_PLUGIN_V2 Gaps

### 3.1 What's Already There (Reusable)

| Feature | Description | THE_DREAM Usage |
|---------|-------------|-----------------|
| GOAP Planner | A* search over action space | Combat option evaluation |
| World State | Key-value state representation | Combat context |
| Cognition Pipeline | Perception → Memory → Intention | Perceiving combat events |
| Action Handler Registry | Extensible action execution | Combat actions |
| NPC Brain Actor Integration | Actor + Behavior communication | Character agents |

### 3.2 Missing: Character Agent Combat Query Interface

**Gap**: No API for Event Brain to query character agents for combat options.

THE_DREAM Section 7.4 describes:
> "The Event Brain doesn't compute options alone. It queries character agents who have intimate knowledge of their capabilities, state, and preferences."

**Required API** (additions to `behavior-api.yaml`):

```yaml
paths:
  /agent/query-combat-options:
    post:
      summary: Query combat options from character agent
      description: |
        Event Brain calls this to get combat options from a character agent.
        The agent considers current state, equipment, injuries, emotional state,
        and returns available options with preferences.
      operationId: QueryCombatOptions
      tags:
        - Combat
      x-permissions:
        - role: service
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/QueryCombatOptionsRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/QueryCombatOptionsResponse'

components:
  schemas:
    QueryCombatOptionsRequest:
      type: object
      required:
        - agent_id
        - combat_context
      properties:
        agent_id:
          type: string
          description: Character agent to query
        combat_context:
          $ref: '#/components/schemas/CombatContext'
        nearby_affordances:
          type: array
          description: Environmental objects from Map Service
          items:
            $ref: '#/components/schemas/EnvironmentalAffordance'
        time_pressure:
          type: string
          enum: [low, medium, high, critical]
          description: Urgency level for response

    QueryCombatOptionsResponse:
      type: object
      required:
        - available_options
        - preferred_option
      properties:
        available_options:
          type: array
          description: What CAN this character do right now?
          items:
            $ref: '#/components/schemas/CombatOption'
        preferred_option:
          type: string
          description: What WOULD this character do? (QTE timeout default)
        option_preferences:
          type: object
          description: Scoring adjustments based on personality
          additionalProperties:
            type: number
        confidence:
          type: number
          description: Agent's confidence in the assessment

    CombatOption:
      type: object
      required:
        - id
        - capability
        - label
      properties:
        id:
          type: string
        capability:
          type: string
          description: Base capability (e.g., "throw", "dodge", "strike")
        label:
          type: string
          description: Display text (e.g., "Throw the barrel")
        description:
          type: string
        target:
          type: string
          description: Target entity ID (if applicable)
        object:
          type: string
          description: Environmental object ID (if applicable)
        score:
          type: number
          description: Computed option score
        requires_object_type:
          type: string
          description: Required environmental object type
        range:
          type: number
          description: Maximum range for this option
```

### 3.3 Missing: Combat Context World State Extensions

**Gap**: World state vocabulary doesn't include combat-specific properties.

**Required Extensions** to World State:

```yaml
# Extended world_state_schema for combat
world_state_schema:
  # Standard (existing)
  health: float
  stamina: float
  mana: float

  # Combat context (NEW)
  in_exchange: bool
  exchange_role: string  # attacker | defender | bystander
  opponent_id: string
  opponent_staggered: bool
  opponent_health_percent: float

  # Environmental awareness (NEW)
  has_throwable_nearby: bool
  has_climbable_nearby: bool
  near_ledge: bool
  near_hazard: bool
  falling: bool

  # Combat state (NEW)
  weapon_equipped: string
  weapon_condition: float
  armor_damaged: bool
  injured_limbs: array<string>

  # Emotional state (NEW - affects option preferences)
  emotional_state: string  # calm | angry | fearful | determined
  relationship_to_opponent: float  # -1.0 to 1.0
```

### 3.4 Missing: Dynamic Combat Option Integration with GOAP

**Gap**: GOAP planner evaluates options but doesn't integrate with environmental affordances.

**Required Enhancement**:

```csharp
/// <summary>
/// Extended GOAP planner that incorporates environmental affordances.
/// </summary>
public interface ICombatGoapPlanner : IGoapPlanner
{
    /// <summary>
    /// Generate combat options from capabilities + environment.
    /// </summary>
    Task<List<CombatOption>> GenerateCombatOptionsAsync(
        WorldState currentState,
        List<GoapAction> capabilities,
        List<EnvironmentalAffordance> affordances,
        CancellationToken ct);

    /// <summary>
    /// Score options considering both strategic value and personality.
    /// </summary>
    Task<List<ScoredOption>> ScoreOptionsAsync(
        List<CombatOption> options,
        WorldState combatState,
        PersonalityProfile personality,
        CancellationToken ct);
}
```

---

## 4. ABML Extensions Needed

THE_DREAM Section 8 describes several ABML extensions. Here's the detailed design:

### 4.1 Environmental Query Action

**Gap**: No action to query Map Service for affordances.

```yaml
# New action type: query_environment
- query_environment:
    query_type: affordance_search
    bounds: "${combat_bounds}"
    object_types: ["throwable", "climbable", "breakable", "hazard"]
    max_results: 10
    result_variable: nearby_affordances

# Implementation in action handler registry
```

**Handler Implementation**:

```csharp
public class QueryEnvironmentHandler : IActionHandler
{
    private readonly IMapClient _mapClient;

    public string ActionType => "query_environment";

    public async Task<ActionResult> ExecuteAsync(
        ActionDefinition action,
        ExecutionContext context,
        bool awaitCompletion,
        CancellationToken ct)
    {
        var queryType = context.ResolveString(action.Parameters["query_type"]);
        var bounds = context.ResolveObject<SpatialBounds>(action.Parameters["bounds"]);
        var objectTypes = context.ResolveList<string>(action.Parameters["object_types"]);
        var maxResults = context.ResolveInt(action.Parameters.GetValueOrDefault("max_results", 10));

        var affordances = await _mapClient.QueryAffordancesAsync(
            new AffordanceQueryRequest
            {
                Bounds = bounds,
                ObjectTypes = objectTypes,
                MaxResults = maxResults
            }, ct);

        var resultVar = action.Parameters["result_variable"].ToString();
        context.SetVariable(resultVar!, affordances);

        return new ActionResult { Success = true };
    }
}
```

### 4.2 Dynamic Choice Action

**Gap**: No action for runtime-generated QTE options.

```yaml
# New action type: dynamic_choice
- dynamic_choice:
    prompt: "Combat action:"
    options_source: "${generated_options}"
    timeout: "${option_window_ms}ms"
    timeout_default: "${preferred_option}"

    on_selection:
      - set: { variable: chosen_option, value: "${selection}" }
      - emit: option_chosen

    on_timeout:
      - set: { variable: chosen_option, value: "${timeout_default}" }
      - emit: option_chosen
```

**Handler Implementation**:

```csharp
public class DynamicChoiceHandler : IActionHandler
{
    public string ActionType => "dynamic_choice";

    public async Task<ActionResult> ExecuteAsync(
        ActionDefinition action,
        ExecutionContext context,
        bool awaitCompletion,
        CancellationToken ct)
    {
        var prompt = context.ResolveString(action.Parameters["prompt"]);
        var options = context.ResolveList<CombatOption>(action.Parameters["options_source"]);
        var timeout = context.ResolveDuration(action.Parameters["timeout"]);
        var timeoutDefault = context.ResolveString(action.Parameters["timeout_default"]);

        // Present QTE to participant (via game engine integration)
        var qteRequest = new QteRequest
        {
            Prompt = prompt,
            Options = options.Select(o => new QteOption
            {
                Id = o.Id,
                Label = o.Label,
                Description = o.Description
            }).ToList(),
            TimeoutMs = (int)timeout.TotalMilliseconds,
            DefaultOptionId = timeoutDefault
        };

        // Fire-and-forget to game engine - it handles input capture
        await context.GameEngineClient.PresentQteAsync(qteRequest, ct);

        // Wait for response or timeout
        var selection = await WaitForSelectionOrTimeoutAsync(
            context, timeout, timeoutDefault, ct);

        context.SetVariable("selection", selection);

        // Execute on_selection or on_timeout handlers
        var handlers = selection == timeoutDefault
            ? action.Parameters.GetValueOrDefault("on_timeout") as List<ActionDefinition>
            : action.Parameters.GetValueOrDefault("on_selection") as List<ActionDefinition>;

        if (handlers != null)
        {
            await context.ExecuteActionsAsync(handlers, ct);
        }

        return new ActionResult { Success = true };
    }
}
```

### 4.3 Exchange Protocol Primitives

**Gap**: No primitives for multi-participant exchange coordination.

```yaml
# join_exchange - Declare participant in exchange
- join_exchange:
    exchange_id: "${exchange_id}"
    role: defender
    capabilities: "${self.combat_capabilities}"

# wait_for_phase - Wait for exchange phase
- wait_for_phase:
    phase: action_selection
    timeout: 5s
    on_timeout:
      - log: { message: "Phase timeout, using default" }

# submit_action - Submit action to exchange
- submit_action:
    exchange_id: "${exchange_id}"
    action: "${chosen_option.capability}"
    target: "${chosen_option.target}"
    object: "${chosen_option.object}"

# receive_choreography - Get choreography instructions from Event Brain
- receive_choreography:
    exchange_id: "${exchange_id}"
    result_variable: my_choreography
    # Contains: animation, timing, sync_points, camera_hints
```

### 4.4 Affordance Binding System

**Gap**: No way to connect capabilities to environmental requirements.

This is **schema-level**, defining capability requirements:

```yaml
# In behavior ABML document
capabilities:
  throw_object:
    goap:
      preconditions:
        has_throwable_nearby: true
        stamina: ">= 10"
      effects:
        stamina: "-10"
      cost: 3

    # NEW: Affordance binding
    affordance_requirements:
      object_type: throwable
      object_in_range: 3.0  # meters

    option_generation:
      per_matching_object: true  # Generate one option per nearby throwable
      template:
        label: "Throw {{ object.name }}"
        description: "Hurl {{ object.name }} at {{ target.name }}"
        damage_formula: "${object.mass * 5 + strength / 2}"

  wall_slam:
    goap:
      preconditions:
        opponent_in_grapple_range: true
        stamina: ">= 20"
      effects:
        opponent_staggered: true
        stamina: "-20"
      cost: 5

    affordance_requirements:
      surface_type: solid_wall
      opponent_toward_surface: true  # Spatial relationship
      distance_to_surface: "<= 2.0"

    option_generation:
      template:
        label: "Slam into wall"
        description: "Drive {{ target.name }} into the wall"
```

---

## 5. New Plugins/APIs Needed

### 5.1 Event Agent Plugin (`lib-event-agent`)

**Purpose**: Specialized plugin for Event Brain actors.

**Contents**:
- Event Brain actor type implementation
- Exchange protocol state machine
- Option generation algorithm
- Choreography emission

**Note**: Could be part of lib-actors or separate. Recommend **separate** for:
- Clear ownership
- Testability (can test Event Brain without full actor infrastructure)
- Optional loading (not all deployments need Event Agents)

### 5.2 Regional Watcher Service/Actor

**Purpose**: Monitor regions for interesting situations.

**Implementation Options**:
1. **Actor Type** (Recommended): One Regional Watcher actor per region
2. **Service**: Single service monitoring all regions
3. **Part of Map Service**: Extension to Map Service

**Recommendation**: Actor type - benefits from:
- Independent scaling per region
- State persistence
- Event subscription per region

### 5.3 Combat Query API Extensions

**New endpoints for `behavior-api.yaml`**:

| Endpoint | Purpose |
|----------|---------|
| `POST /agent/query-combat-options` | Get available options from character agent |
| `POST /combat/generate-options` | Generate options from capabilities + affordances |
| `POST /combat/score-options` | Score options with personality weighting |
| `POST /exchange/register-participant` | Register in exchange |
| `POST /exchange/submit-action` | Submit combat action |

### 5.4 Map Service Affordance Extensions

**Required additions to Map Service** (reference UPCOMING_-_MAP_SERVICE.md):

| Endpoint | Purpose |
|----------|---------|
| `POST /maps/affordances/query` | Query affordances in spatial bounds |
| `POST /maps/affordances/subscribe` | Subscribe to affordance changes in region |

**Affordance Schema**:

```yaml
EnvironmentalAffordance:
  type: object
  required:
    - id
    - object_type
    - position
  properties:
    id:
      type: string
    object_type:
      type: string
      enum: [throwable, climbable, breakable, hazard, cover, ledge, grapple_point]
    position:
      $ref: '#/components/schemas/Vector3'
    properties:
      type: object
      additionalProperties: true
      description: |
        Type-specific properties:
        - throwable: mass, fragile
        - climbable: height, grip_quality
        - breakable: health, material
        - hazard: damage_type, damage_per_second
```

---

## 6. Orchestrator Integration Details

### 6.1 Current State

The ACTORS_PLUGIN_V2 mentions orchestrator integration but lacks:
- API details for spawning instances
- App-id routing patterns
- Instance lifecycle management

### 6.2 Required Documentation

**Add to ACTORS_PLUGIN_V2.md**:

```markdown
## Dynamic Instance Spawning via Orchestrator

### Use Cases

1. **Event Agent Spawning**: Regional Watcher spawns Event Agent for interesting situation
2. **Actor Pool Expansion**: Placement service requests more actor nodes
3. **Specialized Processing**: Asset processing, AI inference, etc.

### API Pattern

```csharp
// 1. Request instance from Orchestrator
var request = new SpawnInstanceRequest
{
    InstanceType = "event-agent",
    Plugins = ["lib-event-agent"],
    Environment = new Dictionary<string, string>
    {
        ["EVENT_AGENT_EXCHANGE_ID"] = exchangeId
    },
    InitialMessage = new EventAgentInitMessage
    {
        ExchangeId = exchangeId,
        Participants = participantIds
    }
};

var result = await _orchestratorClient.SpawnInstanceAsync(request, ct);

// result.AppId = "event-agent-abc123"
// result.Endpoint = "http://10.0.1.45:5012"

// 2. Route subsequent calls via app-id
await _meshClient.InvokeMethodAsync<TReq, TResp>(
    appId: result.AppId,  // Routes to specific instance
    method: "some-method",
    request, ct);

// 3. Instance self-terminates when work complete
// OR Orchestrator terminates on unhealthy heartbeat
```

### Instance Registration

Spawned instances register with Orchestrator:

```csharp
// In spawned instance startup
await _orchestratorClient.RegisterInstanceAsync(
    new InstanceRegistration
    {
        AppId = _appId,
        InstanceType = "event-agent",
        Endpoint = _selfEndpoint,
        StartedAt = DateTimeOffset.UtcNow
    }, ct);

// Heartbeat loop
while (!ct.IsCancellationRequested)
{
    await _orchestratorClient.HeartbeatAsync(
        new InstanceHeartbeat { AppId = _appId }, ct);
    await Task.Delay(TimeSpan.FromSeconds(30), ct);
}
```
```

---

## 7. Implementation Priority

### Phase 1: Foundation (Required for THE_DREAM prototype)

| Item | Priority | Effort | Owner |
|------|----------|--------|-------|
| Character Agent Query API | **Critical** | Medium | Behavior Plugin |
| Event Brain Actor Schema | **Critical** | Medium | Actors Plugin |
| Environmental Query Action | **Critical** | Medium | ABML |
| Orchestrator Spawning Docs | High | Low | Actors Plugin |

### Phase 2: Core Exchange System

| Item | Priority | Effort | Owner |
|------|----------|--------|-------|
| Dynamic Choice Action | High | Medium | ABML |
| Exchange Protocol Primitives | High | High | ABML |
| Combat World State Extensions | High | Low | Behavior Plugin |
| Affordance Binding System | Medium | High | ABML |

### Phase 3: Full System

| Item | Priority | Effort | Owner |
|------|----------|--------|-------|
| Regional Watcher Actor | Medium | Medium | Actors Plugin |
| VIP Tracking | Medium | Low | Actors Plugin |
| Map Service Affordances | Medium | Medium | Map Service |
| Event Tap Documentation | Low | Low | Actors Plugin |

---

## 8. Recommendations

### 8.1 ACTORS_PLUGIN_V2 Updates

1. **Add Section**: "Orchestrator Integration for Dynamic Instance Spawning"
2. **Add Section**: "Event Tap Pattern" documenting character event stream subscription
3. **Add Example Schema**: `event-brain.yaml` actor type
4. **Add Example Schema**: `regional-watcher.yaml` actor type

### 8.2 BEHAVIOR_PLUGIN_V2 Updates

1. **Add API Endpoint**: `/agent/query-combat-options`
2. **Extend World State Schema**: Combat-specific properties
3. **Add Interface**: `ICombatGoapPlanner` with affordance integration
4. **Add Section**: "Combat Option Generation" describing THE_DREAM integration

### 8.3 ABML Updates

1. **Add Action Type**: `query_environment`
2. **Add Action Type**: `dynamic_choice`
3. **Add Action Types**: Exchange protocol primitives
4. **Add Schema Feature**: Affordance binding in capability definitions

### 8.4 New Documentation Needed

1. **Event Agent Plugin Design** (`UPCOMING_-_EVENT_AGENT.md`)
2. **Combat Exchange Protocol** (detailed state machine)
3. **Affordance System Design** (Map Service integration)

---

## 9. Open Questions

### 9.1 Event Agent Placement

**Question**: Should Event Agents be:
- A. Regular actors placed by Placement Service?
- B. Dedicated instances spawned by Orchestrator?

**Recommendation**: B (Dedicated instances) because:
- Event Agents have short, intense lifetimes
- May need co-location with participants for latency
- Easier to scale independently

### 9.2 Affordance Source of Truth

**Question**: Where do affordance definitions live?
- A. Map Service (objects tagged with affordances)
- B. Behavior Plugin (capabilities define affordance requirements)
- C. Both (Map tags objects, Behavior queries)

**Recommendation**: C (Both) - Map Service provides "what's there", Behavior Plugin provides "what can I do with it"

### 9.3 QTE Input Path

**Question**: How does player input reach Event Agent?
- A. Via Bannou (player → Connect → Event Agent)
- B. Direct to Game Engine (THE_DREAM Section 4.4)

**Recommendation**: B - Game engine handles input for lowest latency, Event Agent only provides options/defaults

---

*Document Status: ANALYSIS - Informing plugin update priorities*

## Related Documents

- [THE_DREAM.md](./THE_DREAM.md) - Vision document
- [UPCOMING_-_ACTORS_PLUGIN_V2.md](./UPCOMING_-_ACTORS_PLUGIN_V2.md) - Actor infrastructure
- [UPCOMING_-_BEHAVIOR_PLUGIN_V2.md](./UPCOMING_-_BEHAVIOR_PLUGIN_V2.md) - Behavior runtime
- [ABML_V2_DESIGN_PROPOSAL.md](./ABML_V2_DESIGN_PROPOSAL.md) - ABML language spec
