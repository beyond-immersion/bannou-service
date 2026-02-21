# Phase 3: Integration Wiring

> **Status**: Draft
> **Parent Plan**: [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md)
> **Prerequisites**: Phase 2 complete (lib-cinematic plugin operational)
> **Estimated Scope**: ABML behavior documents + plugin configuration + end-to-end validation

---

## Goal

Connect lib-cinematic to the actor/puppetmaster/gardener systems so that cinematics fire in-game from actor decisions, with player interaction gated by progressive agency. This phase is primarily **configuration and behavior authoring**, not new service code.

---

## Integration Points

### 1. Event Brain → lib-cinematic (Actor Integration)

**What**: Event Brain actors (running on L2 Actor runtime) call lib-cinematic endpoints via ABML `service_call` actions to discover and trigger cinematics.

**How**: ABML behavior documents for event brain actors include:

```yaml
# In an event brain behavior document
actions:
  check_for_cinematic:
    type: service_call
    service: cinematic
    endpoint: /cinematic/scenario/find-available
    params:
      participants: ${perception.nearby_characters}
      location_id: ${world.current_location_id}
      capabilities: ${perception.participant_capabilities}
    result_var: available_cinematics

  evaluate_and_select:
    # God-actor GOAP logic: evaluate available scenarios
    # against current dramatic context, participant history,
    # narrative arc position, and make selection decision
    type: goap_evaluate
    input: ${available_cinematics}
    criteria:
      - narrative_fit: ${world.current_arc_position}
      - personality_match: ${personality.dominant_effort}
      - recency: ${encounters.last_cinematic_age}
    result_var: selected_scenario

  trigger_cinematic:
    type: service_call
    service: cinematic
    endpoint: /cinematic/trigger
    params:
      scenario_id: ${selected_scenario.scenario_id}
      participant_bindings: ${selected_scenario.bindings}
      location_id: ${world.current_location_id}
    result_var: cinematic_ref
```

**Key principle**: The event brain's behavior document encodes the **judgment** about when and which cinematic to trigger. lib-cinematic provides the **data** (find-available) and **execution** (trigger). The ABML behavior document is where personality-weighted selection, narrative arc fitting, and dramatic pacing logic live -- not in the plugin.

### 2. Puppetmaster → lib-cinematic (Regional Watcher Integration)

**What**: Regional watchers (divine actors monitoring a region via Puppetmaster) can trigger encounter cinematics when they observe opportunities in their watched area.

**How**: Puppetmaster already manages regional watcher lifecycle. The watcher's ABML behavior document includes cinematic discovery as part of its regional monitoring loop:

```yaml
# In a regional watcher behavior document
monitoring_loop:
  - perceive_region: ${region.entity_events}
  - when:
      condition: "${perception.has_encounter_opportunity}"
      actions:
        - service_call:
            service: cinematic
            endpoint: /cinematic/scenario/find-available
            params:
              participants: ${perception.encounter_participants}
              location_id: ${perception.encounter_location}
        - goap_evaluate:
            # Regional watcher applies its own dramatic sensibility
            criteria:
              - realm_theme_fit: ${world.realm_theme}
              - area_danger_level: ${world.area_danger}
              - time_since_last: ${region.last_cinematic_time}
        - service_call:
            service: cinematic
            endpoint: /cinematic/trigger
```

**Dependency direction**: Puppetmaster (L4) calls lib-cinematic (L4). Both are soft dependencies of each other -- either can function without the other. Regional watchers that include cinematic discovery are only deployed when both services are enabled.

### 3. Gardener → lib-cinematic (Player Experience Integration)

**What**: Gardener orchestrates player experiences. When a cinematic triggers involving a player's associated entities, Gardener needs to:
- Route QTE inputs from the player to the cinematic session
- Manage the player's experience context during the cinematic
- Handle garden-to-garden transitions if the cinematic changes the player's context

**How**: Gardener subscribes to `cinematic.triggered` events. When a cinematic involves a player's entity:

1. Gardener checks if the player's guardian spirit has combat domain agency
2. If yes, Gardener sets up the QTE input routing:
   - Player inputs from the WebSocket connection are forwarded to the cinematic's CutsceneSession
   - `InputWindowManager` handles timeout/default if the player doesn't respond
3. Gardener tracks the cinematic as part of the player's current garden context
4. On `cinematic.completed`, Gardener updates the player's context

**Implementation**: This is primarily event subscription handling in `GardenerServiceEvents.cs`:

```csharp
// In GardenerServiceEvents.cs
[EventSubscription("cinematic.triggered")]
public async Task HandleCinematicTriggeredAsync(CinematicTriggeredEvent evt, CancellationToken ct)
{
    // Check if any participant is a player-associated entity
    // If so, set up QTE routing through the player's WebSocket session
}
```

### 4. Progressive Agency (Spirit Fidelity → QTE Density)

**What**: The guardian spirit's combat domain fidelity determines how many continuation points in a cinematic become interactive QTE windows vs. auto-resolved.

**How**: When lib-cinematic triggers a scenario:

1. Query Agency service (L4, soft dependency) for `${spirit.domain.combat.fidelity}` per participating player
2. Fidelity value (0.0 to 1.0) determines QTE activation threshold:
   - **0.0** (no agency): All continuation points auto-resolve with defaults. Player watches.
   - **0.3** (low): Only critical continuation points become QTEs (climactic moments)
   - **0.7** (medium): Most continuation points become QTEs
   - **1.0** (full): All continuation points become interactive
3. The CinematicScenario's continuation points each have a `priority` (0.0-1.0). Continuation points with priority <= fidelity become active QTEs; others auto-resolve.

**If Agency service is unavailable**: All continuation points activate (no gating). This is graceful degradation -- the system works without progressive agency, it just lacks the gradual reveal.

**Implementation**: In `CinematicService.TriggerCinematicAsync()`:

```csharp
// Soft dependency check
var agencyClient = _serviceProvider.GetService<IAgencyClient>();
float fidelity = 1.0f; // Default: all active

if (agencyClient != null)
{
    // Query spirit fidelity for each player-associated participant
    // Set fidelity per participant
}

// When building CutsceneSession, mark which continuation points are interactive
// based on fidelity threshold vs continuation point priority
```

---

## Required ABML Behavior Documents

Phase 3 requires authoring ABML documents that encode the cinematic discovery and selection logic. These are **content**, not code -- they're loaded via Puppetmaster's dynamic behavior system.

| Document | Actor Type | Purpose |
|----------|-----------|---------|
| `encounter_watcher.abml` | Event Brain | Monitors perception queue for encounter opportunities, calls find-available, evaluates and selects, triggers |
| `divine_regional_watcher.abml` (amendment) | Regional Watcher | Add cinematic discovery to existing regional monitoring loop |
| `cinematic_test_scenario.abml` | Test utility | Simple behavior that triggers a specific scenario for end-to-end testing |

---

## End-to-End Test Scenario

The validation for Phase 3 is an end-to-end scenario that exercises the full flow:

1. **Setup**: Register a hand-authored cinematic scenario in lib-cinematic
2. **Actor perception**: Event brain detects two characters in proximity with matching capabilities
3. **Discovery**: Event brain calls `/cinematic/scenario/find-available` with encounter context
4. **Selection**: Event brain's GOAP evaluates and selects a scenario
5. **Trigger**: Event brain calls `/cinematic/trigger` with participant bindings
6. **Compilation**: lib-cinematic exports → compiles → caches the scenario
7. **Execution**: CinematicRunner loads the compiled BehaviorModel, acquires entity control, starts CutsceneSession
8. **QTE**: A continuation point activates (if spirit fidelity permits), InputWindowManager presents the choice
9. **Resolution**: Player input (or timeout default) selects a branch
10. **Completion**: Cinematic ends, entity control returns, events publish

This scenario should be testable via the HTTP integration test suite (`http-tester/`).

---

## Implementation Steps

### Step 1: Event Subscription Wiring

1. In `CinematicServiceEvents.cs`, subscribe to any events needed for cleanup coordination
2. In `GardenerServiceEvents.cs`, add subscription to `cinematic.triggered` and `cinematic.completed` for player experience routing

### Step 2: QTE Input Routing

1. Define the WebSocket message flow for QTE inputs from client to CutsceneSession
2. Gardener or Connect service routes player inputs to the active CutsceneSession's `SubmitInputAsync`
3. Handle timeout/disconnect gracefully (InputWindowManager already has timeout defaults)

### Step 3: Progressive Agency Integration

1. Add `priority` field to `ContinuationPoint` in cinematic-composer SDK (if not already present)
2. In `CinematicService.TriggerCinematicAsync`, query Agency service for spirit fidelity
3. Mark continuation points as active/inactive based on fidelity threshold
4. Pass the activation map to CutsceneSession creation

### Step 4: Author ABML Behavior Documents

1. Write `encounter_watcher.abml` for event brain cinematic discovery
2. Amend regional watcher behaviors with cinematic discovery actions
3. Write test utility behavior for end-to-end validation

### Step 5: End-to-End Validation

1. Register a test scenario via `/cinematic/scenario/create`
2. Start an actor with the `encounter_watcher.abml` behavior
3. Simulate encounter conditions (two characters with matching capabilities in proximity)
4. Verify the full flow from perception → discovery → selection → trigger → execution → completion
5. Verify events are published at each stage
6. Verify entity control is properly acquired and returned

### Step 6: Event Publishing for Content Flywheel

1. On cinematic completion, publish events that feed:
   - lib-character-encounter: memorable encounter record
   - lib-character-history: character participation in cinematic event
   - lib-realm-history: notable encounter in realm
2. These are standard event publications -- the consuming services subscribe independently

---

## Acceptance Criteria

1. Event brain actors can discover and trigger cinematics via ABML `service_call` actions
2. Regional watchers (via Puppetmaster) can trigger encounter cinematics
3. Gardener routes player QTE inputs to active CutsceneSession
4. Progressive agency gates QTE density based on spirit combat fidelity
5. Agency service absence degrades gracefully (all QTEs active)
6. End-to-end flow works: perception → discovery → selection → trigger → execution → completion
7. Cinematic completion publishes events for the content flywheel (character-encounter, character-history, realm-history)
8. `dotnet build` succeeds for the full solution

---

## Risks

| Risk | Mitigation |
|------|------------|
| QTE input routing through WebSocket is complex | Gardener already handles event routing for player experiences. The pattern exists; this adds a specific message type. |
| ABML behavior documents may need new action types | Existing `service_call` and `goap_evaluate` actions should suffice. If not, the behavior-compiler's action type system is extensible. |
| Agency service not yet implemented | Graceful degradation: all QTEs active. Progressive agency is additive polish, not blocking. |
| Content flywheel events need schema coordination | Define events in cinematic-events.yaml. Consuming services (character-encounter, character-history, realm-history) subscribe independently via their own event subscriptions. |

---

## What Phase 3 Enables

With all four phases complete:
- Designers author cinematic scenarios using the cinematic-composer SDK format
- Scenarios are registered in lib-cinematic via API
- God-actors (event brains, regional watchers) discover matching scenarios when they perceive encounter opportunities
- God-actors select scenarios using their own GOAP reasoning (personality fit, narrative arc, dramatic pacing)
- Cinematics execute with player interaction gated by progressive agency
- Cinematic outcomes feed the content flywheel (encounter memories, character history, realm history)
- The system is ready for procedural generation (Phases 4-5) to produce additional scenarios in the same format

---

*This is the Phase 3 implementation plan. For architectural context, see [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md). For the plugin this integrates, see [Phase 2](CINEMATIC-PHASE-2-PLUGIN.md).*
