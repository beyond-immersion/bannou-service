# Death & Plot Armor System

> **Status**: Design
> **Created**: 2026-02-18
> **Author**: Lysander (design) + Claude (analysis)
> **Category**: Cross-cutting mechanic (behavioral, not plugin)
> **Related Services**: Status (L4), Seed (L2), Divine (L4), Gardener (L4), Puppetmaster (L4), Actor (L2)
> **Related Plans**: [CINEMATIC-SYSTEM.md](../plans/CINEMATIC-SYSTEM.md), [STORYLINE-COMPOSER-SDK.md](../plans/STORYLINE-COMPOSER-SDK.md)
> **Related Docs**: [ORCHESTRATION-PATTERNS.md](../reference/ORCHESTRATION-PATTERNS.md), [VISION.md](../reference/VISION.md), [PLAYER-VISION.md](../reference/PLAYER-VISION.md)

---

## Executive Summary

Plot armor is a **hard numerical value** tracked per character that determines whether that character can die as a result of scenario outcomes, cinematic exchanges, or combat encounters. While the value is above zero, the character **cannot be killed** -- dangerous outcomes are deflected by divine intervention, luck, autonomous reflexes, or other narrative mechanisms. When the value reaches zero, cinematic deaths become possible.

Plot armor is **not** a mechanic embedded in scenario SDKs or plugins. It is a **behavioral constraint** evaluated by god-actors in their ABML behavior documents. Scenarios carry lethality metadata; gods read it alongside the character's plot armor and make decisions. The *ways* in which plot armor protection manifests are literal deus-ex-machina moments -- because it IS the gods controlling it.

This is not a safety net that makes the game easy. It is a **narrative pacing mechanism** that ensures characters die at dramatically appropriate moments rather than randomly. When plot armor finally depletes and a character falls, that death is earned -- preceded by escalating near-misses that built tension, created memorable encounters, and enriched the character's archive for the content flywheel.

---

## The Core Mechanic

### Plot Armor Value

- **Type**: Continuous float, minimum 0.0, no hard maximum (but practically 10.0-100.0 range depending on tuning)
- **Storage**: Status service (L4) -- fits the "items in inventories" entity effects pattern
- **Status template**: `PLOT_ARMOR`, quantity model: continuous, decay: manual (god-actor controlled)
- **Variable exposure**: `${status.plot_armor}` via Status `IVariableProviderFactory`, readable in all ABML expressions

### The Rule

> **While `${status.plot_armor} > 0`, no scenario outcome, cinematic branch, or combat exchange may result in the character's death.**

This rule is **not enforced by any service or SDK**. It is a behavioral convention followed by all god-actors in their ABML behaviors. The gods don't kill characters with plot armor because the gods' behaviors check for it. A scenario with a lethal branch exists in the registry regardless -- the god simply doesn't select it for a protected character, or if a lethal outcome occurs (QTE failure), the god intervenes before death resolves.

### Depletion

Plot armor decreases when characters survive dangerous situations. The god-actor orchestrating the encounter decides the chip amount based on:

- **Danger intensity**: How lethal was the scenario? A near-miss with a raid boss chips more than a tavern brawl.
- **God personality**: Merciful gods chip slowly (Moira). Aggressive gods chip quickly (Ares). The god's `${personality.mercy}` axis influences depletion rate.
- **Narrative position**: If the character is in the climactic phase of an active storyline scenario, the god may preserve armor to protect the arc. If the character has no active storylines and is high-level, depletion accelerates.
- **Player agency**: The guardian spirit's combat fidelity (from Agency service) factors in. A spirit with high combat agency gets less divine protection -- the player can protect themselves.

```yaml
# God-actor behavior for post-encounter plot armor depletion
when:
  condition: "${cinematic.completed} AND ${cinematic.lethality} > 0"
  actions:
    - compute:
        base_chip: "${cinematic.lethality} * 2.0"
        mercy_factor: "1.0 - (${personality.mercy} * 0.5)"
        agency_factor: "${spirit.domain.combat.fidelity} * 0.3"
        final_chip: "${base_chip} * ${mercy_factor} + ${agency_factor}"
    - service_call:
        service: status
        endpoint: /status/effect/modify
        params:
          entity_id: ${character.id}
          template_code: PLOT_ARMOR
          quantity_delta: -${final_chip}
```

### Sources of Plot Armor

| Source | Mechanism | Typical Amount | Notes |
|--------|-----------|---------------|-------|
| **New character** | Gardener grants on character creation | High (full) | Training wheels -- spirit can't handle death yet |
| **Guardian spirit growth** | Seed capability in protective domain | Periodic refresh | Gardener reads `${seed.guardian.protective_influence}` |
| **Divine blessing** | God grants via Divine/Status API | Variable | Moira blesses interesting characters; gods protect favorites |
| **Generational reset** | New generation character gets fresh armor | High (full) | The cycle renews |
| **Fulfillment deficit** | Characters with unfinished business retain more | Passive | The Fulfillment Principle -- the narrative "wants" them to complete their arc |

### What Plot Armor Is NOT

- **Not hitpoints**: Plot armor doesn't interact with combat mechanics. A character with plot armor can still be wounded, knocked unconscious, captured, imprisoned, enslaved, cursed -- anything short of permanent death.
- **Not visible (at first)**: Early-game spirits don't perceive plot armor. As combat agency grows, the player might sense increasing danger -- a subtle UI gradient, a spirit whisper, a feeling of vulnerability. The guardian spirit progressively learns to perceive its own protective influence.
- **Not infinite**: Every dangerous encounter chips it. A character who repeatedly charges into danger will deplete their armor faster than one who lives cautiously. The gods adjust, but they can't prevent depletion entirely.
- **Not per-encounter**: Plot armor persists across encounters, scenarios, and sessions. It's a long-arc resource, not a per-fight shield.

---

## Deus Ex Machina: How Protection Manifests

When a character with plot armor faces a lethal outcome (QTE failure, scenario branch leading to death, instant-death attack), the god-actor intervenes. The intervention style varies by god personality:

| God | Protection Style | Example |
|-----|-----------------|---------|
| **Moira (Fate)** | Fate-weaving -- subtle redirection of causality | The blade glances off bone instead of piercing the heart. A loose cobblestone causes the enemy to stumble at the critical moment. |
| **Ares (War)** | Battle fury -- the character fights beyond mortal limits | Adrenaline surge. The character's body moves on pure instinct, executing a defensive maneuver they didn't know they could do. |
| **Silvanus (Forest)** | Nature intervention -- the environment acts | A beast crashes through the wall. Roots trip the attacker. A branch deflects the arrow. |
| **Thanatos (Death)** | Death rejects the character -- "not yet" | The killing blow connects but the character doesn't die. A cold presence whispers "you are not finished." Deeply unsettling for witnesses. |
| **Typhon (Monsters)** | Creature hesitation -- the monster pauses | The raid boss's attack falters. Pneuma echoes are god-controlled -- the god simply doesn't commit the killing force. |
| **Hermes (Commerce)** | Lucky break -- improbable coincidence | A coin in the pocket deflects the dagger. The poison was slightly diluted. The bridge collapses but a merchant's cart breaks the fall. |

Each manifestation is an ABML behavior fragment that the god-actor selects from its repertoire. The scenario SDK doesn't know about any of this -- it provided a lethal branch, the god-actor intercepted before resolution.

These moments are **memorable encounters** recorded via character-encounter and character-history. They accumulate in the character's archive. When the character finally does die (plot armor depleted), their archive is rich with "the time Fate saved me from the dragon" and "the moment Death itself said not yet." Ghost quests and undead narratives generated from this archive carry genuine weight.

---

## Real-Time Dramatic Fuel: The Raid Boss Pattern

### The Insight

Characters with depleted plot armor don't just *eventually* become content via the flywheel. They become **real-time dramatic fuel** for group events.

### The Scenario

A world raid boss encounter with dozens of players participating:

1. **The raid boss is an Actor** (event brain or character brain) running ABML behavior. It perceives all participants, reads their capabilities, and makes tactical decisions.

2. **Each player gets personalized QTEs** when appropriate. These are not group behaviors -- each player's interaction with the raid is their own cinematic micro-encounter, coordinated by the encounter's event brain actor and the cinematic system. Player A gets a QTE to dodge a tail sweep. Player B gets a QTE to deflect falling debris. These fire based on spatial proximity, character capabilities, and the boss actor's tactical targeting.

3. **The boss reads `${target.status.plot_armor}`** for each participant. Most characters have some armor -- they're protected. The boss's ABML behavior filters them for instant-death targeting.

4. **The boss identifies vulnerable characters**: High-level, experienced, plot armor depleted. These characters are *known to be mortal*. The boss actor's GOAP evaluation specifically targets them for its most devastating attacks.

5. **Instant-death attack sequence**:
   - The boss telegraphs a devastating attack targeting the vulnerable character
   - The character gets a high-stakes QTE (the player's combat fidelity determines QTE complexity via progressive agency)
   - On QTE success: spectacular dodge/block, the character survives, massive dramatic tension
   - On QTE failure: **death cinematic triggers**

6. **"Death of a Hero" cinematic**: A pre-authored cinematic scenario (registered in lib-cinematic) fires. The camera sweeps dramatic. The character's final moments play out. Other participants witness it in real-time. The death is a spectacle -- not a failure screen, but a narrative climax occurring within the group event.

7. **Cascade effects**:
   - Character-encounter records the death for all witnesses (dozens of memorable encounters created simultaneously)
   - Character-history records participation in a world event with death role
   - Realm-history records the heroic death as a realm event
   - The character's archive compresses with maximum narrative density (near-death history + actual heroic death)
   - Content flywheel engages immediately -- god-actors perceiving the death event begin composing narrative responses
   - Other players in the raid feel the stakes viscerally -- that was a real death, of a real character, with real history

### Why This Works Architecturally

- **The boss actor targets intelligently** because it reads variable providers. `${target.status.plot_armor}` is just another variable -- no special system needed.
- **Each player's QTEs are personal** because the cinematic system already supports per-participant continuation points with progressive agency gating. The encounter's event brain fires personalized micro-cinematics via `/cinematic/trigger` for each participant independently.
- **The death cinematic is pre-authored** (or procedurally generated) and registered in lib-cinematic like any other scenario. It's just a scenario with `lethality: 1.0` and a "hero_death" tag. The boss actor or regional watcher calls `/cinematic/trigger` with the dying character as the bound participant.
- **Spectators witness it** because the cinematic system's multi-channel ABML handles camera and environmental tracks alongside the dying character's track. Nearby players' clients receive the cinematic presentation data.
- **No new services needed**. The boss is an Actor. The death is a Cinematic. The armor is a Status. The encounter record is Character-Encounter. Everything composes.

### The Emotional Arc

For the character with no plot armor participating in the raid:

```
Before the raid:
  The player knows (through progressive agency hints) that their character is vulnerable.
  The guardian spirit senses the absence of divine protection.
  Choosing to participate IS the dramatic choice.

During the raid:
  Every attack aimed at the character carries real weight.
  QTE stakes are genuinely high -- failure means death, not inconvenience.
  The player is fully engaged because the consequences are real.
  Other participants may not know which characters are vulnerable,
  creating hidden tension.

The death moment:
  The boss targets the vulnerable hero with a devastating attack.
  The QTE fires -- dodge, block, or counter.
  The player fails (or the god-actor decides not to intervene).
  A cinematic death plays out for all witnesses.
  The character falls. The raid continues. The world remembers.

After the raid:
  The character's archive compresses with full narrative density.
  The guardian spirit receives logos from the fulfilled (or unfulfilled) life.
  A new character inherits memories. Fresh plot armor. The cycle renews.
  Ghost quests, memorials, NPC memories, and legacy items emerge from the archive.
  Players who witnessed the death carry the encounter in their own history.
```

### Group Event Scaling

The pattern scales naturally:

| Event Size | Plot Armor Distribution | Dramatic Density |
|-----------|------------------------|------------------|
| **2-person encounter** | 0-1 vulnerable characters | Intimate -- the death is personal |
| **Small group (5-10)** | 1-2 vulnerable characters | Tactical -- the group must protect vulnerable members |
| **Raid (20-50)** | 3-8 vulnerable characters | Epic -- multiple death moments possible across the encounter |
| **World event (100+)** | 10-20 vulnerable characters | Historic -- mass heroism and sacrifice, realm-shaping event |

The boss actor's ABML behavior adapts targeting based on the vulnerable count. A raid with many unprotected characters faces more frequent instant-death attacks but spread across targets. A raid with one vulnerable hero sees that hero relentlessly targeted -- the entire encounter's tension centers on their survival.

---

## Scenario Lethality Metadata

Both storyline and cinematic scenarios carry lethality metadata so god-actors can make informed targeting decisions:

### Storyline Scenarios (storyline-composer SDK)

```csharp
public sealed class ScenarioMetadata
{
    // ... existing fields ...

    /// <summary>
    /// How likely this scenario is to produce a character death if fully played out.
    /// 0.0 = impossible (social, mystery, crafting scenarios).
    /// 0.5 = possible under specific conditions (combat with escape routes).
    /// 1.0 = guaranteed death if the lethal branch resolves (sacrifice, execution, boss kill).
    /// God-actors read this alongside ${status.plot_armor} to filter scenario selection.
    /// </summary>
    public float Lethality { get; init; }

    /// <summary>
    /// Which phases contain potential lethal outcomes. Empty if lethality is 0.
    /// God-actors use this to know which phase transitions to monitor for death risk.
    /// </summary>
    public IReadOnlyList<string> LethalPhases { get; init; } = [];
}
```

### Cinematic Scenarios (cinematic-composer SDK, Phase 1)

```csharp
public sealed class ScenarioMetadata
{
    // ... existing fields ...

    /// <summary>
    /// How likely this scenario is to produce a character death if fully played out.
    /// God-actors cross-reference with ${status.plot_armor} for targeting decisions.
    /// </summary>
    public float Lethality { get; init; }

    /// <summary>
    /// Which QTE branches lead to lethal outcomes. Keyed by continuation point name.
    /// God-actors can use this to decide whether to auto-resolve lethally or intervene.
    /// </summary>
    public IReadOnlyList<string> LethalBranches { get; init; } = [];
}
```

### How God-Actors Use Lethality

```yaml
# God-actor scenario selection behavior
evaluate_scenarios:
  - for_each: ${available_scenarios}
    as: scenario
    evaluate:
      - when:
          # Character has plot armor -- skip lethal scenarios entirely
          condition: "${target.status.plot_armor} > 0 AND ${scenario.lethality} > 0.5"
          action: skip
      - when:
          # Character has NO plot armor -- lethal scenarios are valid AND preferred
          # for high-drama moments
          condition: "${target.status.plot_armor} == 0 AND ${scenario.lethality} > 0.7"
          action: boost_priority  # God wants dramatic deaths

# Boss actor targeting behavior
select_instant_death_target:
  - filter: ${raid.participants}
    where: "${participant.status.plot_armor} == 0"
    sort_by: "${participant.level} DESC"  # Target the highest-level vulnerable character
    limit: 1
    result_var: death_target
  - when:
      condition: "${death_target} != null"
      actions:
        - service_call:
            service: cinematic
            endpoint: /cinematic/trigger
            params:
              scenario_code: HERO_DEATH_RAID_BOSS
              participant_bindings:
                hero: ${death_target.character_id}
                boss: ${self.character_id}
              location_id: ${world.current_location_id}
```

---

## Progressive Agency Integration

Plot armor intersects with progressive agency (the guardian spirit's accumulated experience) in several ways:

### Early Game (High Armor, Low Agency)

- Full plot armor. The character cannot die.
- Low combat agency. QTEs are simple or auto-resolved.
- The player observes the character surviving dangerous situations without understanding why.
- The spirit is learning. The training wheels are invisible divine protection.

### Mid Game (Depleting Armor, Growing Agency)

- Plot armor is chipping. Near-death events become more frequent.
- Combat agency is growing. The player sees more QTEs, has more influence.
- The player begins to *sense* danger -- the guardian spirit perceives its diminishing protection.
- A subtle UX shift: the spirit "feels" vulnerability increasing. (Agency service provides the capability manifest; the client renders the perception.)
- The player starts to understand that their choices in QTEs have real weight.

### Late Game (No Armor, Full Agency)

- Plot armor is depleted. The character is mortal.
- Full combat agency. Rich QTE interactions, complex timing windows, combo choreography.
- The player knows the stakes. Every dangerous encounter is genuine.
- Death is a real possibility -- and the player has the agency to prevent it through skill.
- When death comes, it's either the player's failure or a narrative choice to accept it.

### The Gradient

This is the progressive agency gradient applied to mortality:

```
Spirit Experience:  Low ─────────────────────────────────── High
Plot Armor:         Full ────────── Depleting ─────────── Zero
Combat Agency:      None ────────── Growing ───────────── Full
Death Risk:         Zero ────────── Increasing ────────── Real
Player Investment:  Curiosity ───── Engagement ─────────── Stakes
```

No walls. No thresholds. A continuous spectrum where protection decreases as capability increases. The player earns the right to die by earning the ability to prevent it.

---

## Relationship to Vision Principles

| Vision Principle | How Plot Armor Serves It |
|-----------------|--------------------------|
| **Living Game Worlds** (North Star #1) | Deaths are dramatic events witnessed by NPCs and players. The world reacts to heroism. |
| **The Content Flywheel** (North Star #2) | Near-death encounters and actual deaths produce rich archives. Each death seeds multiple future narratives. |
| **100K+ Concurrent NPCs** (North Star #3) | NPC characters also have plot armor. Important NPCs deplete slowly; minor NPCs deplete fast. Creates a natural importance hierarchy without manual curation. |
| **Emergent Over Authored** (North Star #5) | Who dies, when, and how is emergent from god-actor decisions, not scripted. |
| **Characters Are Independent Entities** (Design Principle #1) | The character doesn't know about plot armor. Their NPC brain fights to survive regardless. The protection is external -- divine, not internal. |
| **Death Creates, Not Destroys** (Design Principle #4) | Plot armor ensures death happens at narratively rich moments, maximizing the generative value of the archive. |
| **World-State Drives Everything** (Design Principle #3) | Plot armor is world state. Its depletion is driven by simulated events. Its absence enables new world states (heroic deaths, legacy creation). |

---

## Implementation: No New Services Required

| Component | Existing Service | What's Needed |
|-----------|-----------------|---------------|
| Plot armor value | Status (L4) | `PLOT_ARMOR` status template (seed data) |
| Value exposure | Status `IVariableProviderFactory` | Already provides `${status.*}` namespace |
| Armor sources | Seed (L2) + Divine (L4) + Gardener (L4) | God-actor behaviors that grant/refresh armor |
| Depletion logic | God-actor ABML behaviors | Behavior patterns for post-encounter chipping |
| Protection manifestation | God-actor ABML behaviors | Per-god intervention style behaviors |
| Lethality metadata | Scenario definitions | `Lethality` field on `ScenarioMetadata` (both SDKs) |
| Boss targeting | Actor (L2) ABML behaviors | Boss behavior reads `${target.status.plot_armor}` |
| Death cinematics | Cinematic (L4, future) | Pre-authored "hero death" scenario templates |
| Progressive perception | Agency (L4) | Spirit combat fidelity gates danger perception UX |

**Total new code**: Zero services, zero plugins, zero SDKs. One status template definition. One metadata field on two scenario SDKs. ABML behavior documents.

That's the architecture talking.

---

*This document describes the design for the death and plot armor system. For cinematic system architecture, see [CINEMATIC-SYSTEM.md](../plans/CINEMATIC-SYSTEM.md). For vision context, see [VISION.md](../reference/VISION.md) and [PLAYER-VISION.md](../reference/PLAYER-VISION.md). For orchestration patterns, see [ORCHESTRATION-PATTERNS.md](../reference/ORCHESTRATION-PATTERNS.md).*
