# Death & Plot Armor System

> **Status**: Design
> **Created**: 2026-02-18
> **Author**: Lysander (design) + Claude (analysis)
> **Category**: Cross-cutting mechanic (behavioral, not plugin)
> **Related Services**: Status (L4), Seed (L2), Divine (L4), Gardener (L4), Puppetmaster (L4), Actor (L2), Character (L2), Character-Encounter (L4), Character-History (L4), Contract (L1), Music (L4), Agency (L4), Connect (L1)
> **Related Plans**: [CINEMATIC-SYSTEM.md](../plans/CINEMATIC-SYSTEM.md), [STORYLINE-COMPOSER-SDK.md](../plans/STORYLINE-COMPOSER-SDK.md)
> **Related Docs**: [ORCHESTRATION-PATTERNS.md](../reference/ORCHESTRATION-PATTERNS.md), [VISION.md](../reference/VISION.md), [PLAYER-VISION.md](../reference/PLAYER-VISION.md)
> **Related arcadia-kb**: Underworld and Soul Currency System, Aspiration-Based Guardian Spirit Progression, Strauss-Howe Generational World Evolution

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

## The Liminal Period: Death as Continued Gameplay

### Schrodinger's Death

The combination of plot armor (god-controlled, invisible to the player), the content flywheel (death feeds future content), generational play (household continuity), and divine fickleness creates a unique psychological state: **the character is simultaneously dead and potentially-not-dead until the player irrevocably commits to moving on**.

This inverts normal game death psychology. In most games, death is a binary state change that the player resents. Here, death is the *beginning* of a dramatic question: "Am I actually done?" And the answer depends on gods who have their own personalities, the character's narrative arc, and the player's performance in what comes next.

The content flywheel feeds this uncertainty from two directions simultaneously:
- **Forward**: Death feeds future story seeds and scenario context (the archive becomes generative input)
- **Present**: Character actions and deaths feed scenarios and content NOW, in real-time (the gods are watching and reacting)

A player whose character falls in a raid cannot be ABSOLUTELY certain they won't be miraculously saved until they've actually started a new character and filled that character slot. And EVEN THEN, plotlines from the previous character may continue generating surprises -- ghost quests, NPC memories, legacy items, and narrative callbacks that make the previous character's story feel unfinished in the best possible way.

### The Three Phases of Death

Death in Arcadia is not an instant. It unfolds across three distinct gameplay phases, each with its own mechanics and emotional purpose.

#### Phase 1: The Fall

The character falls. The "Death of a Hero" cinematic plays (see [Raid Boss Pattern](#real-time-dramatic-fuel-the-raid-boss-pattern) above). Appropriate music. Appropriate text. The player KNOWS this is a death.

**No ambiguity about what happened.** The game plays the death music, gives the death text, shows the death cinematic. This clarity is essential -- the player needs this period to begin processing the loss. The uncertainty that follows is about what happens NEXT, not about whether the character actually died.

This connects to the Underworld system's soul degradation timeline:
- **0 seconds (Death)**: Shell ruptures, logos begin scattering
- **Seconds window**: Resurrection still possible through divine intervention
- **Minutes (Degradation Phase)**: Too degraded for full resurrection; reincarnation possible with partial cached data
- **Cleanup Phase**: Logos become raw data, only unique traits distinguishable

The Fall corresponds to the first moments of this timeline -- the shell has ruptured, but the logos haven't scattered yet. Everything that follows happens within that degradation window.

#### Phase 2: The Last Stand

After the death cinematic completes, the player does NOT get a "return to menu" screen. Instead, the gameplay continues in a transformed state. The guardian spirit -- now unbound from the dying character's shell -- has a brief window of heightened agency before the logos fully scatter.

This is the metaphysical justification: the guardian spirit IS a divine shard of Nexius. In the moment of character death, when the pneuma shell that contained it ruptures, the spirit is momentarily free in a way it never was while bound. That freedom manifests as new gameplay possibilities.

**Spirit Form Assistance**

The guardian spirit briefly manifests near allies as a visible presence. Architecturally clean -- this is just the spirit existing unbound rather than channeled through a character:

- **Temporary buff aura**: Status service grants temporary blessings to nearby allied characters. The spirit's accumulated experience in protective domains determines the buff type and strength.
- **Threat perception**: The spirit can perceive dangers that allies' characters can't see. This manifests as perception injection into allied characters' actors -- the allied character suddenly "senses" an attack coming from behind, or "notices" a weak point in the boss's defense.
- **Influence amplification**: The spirit can guide allies' autonomous NPC actions more effectively. The character co-pilot's influence weight increases for nearby allies, as if the freed spirit is lending its accumulated understanding to bolster their decision-making.

**Temporary Possession**

The most dramatic Last Stand option: possessing uncontrolled characters (NPCs) temporarily. The spirit, freshly unbound, can briefly inhabit an NPC combatant nearby.

This connects directly to the progressive agency system. The spirit has accumulated combat experience across generations -- it carries that agency into an unfamiliar body. The UX reflects this: the player has their full accumulated combat agency applied to the NPC's capabilities, but the NPC's personality resists (Characters Are Independent Entities -- Design Principle #1). A brave NPC warrior might welcome the divine presence. A cowardly merchant being pressed into combat would fight the possession, creating mechanical friction (reduced responsiveness, delayed actions, personality-driven refusals).

The possession is temporary. The NPC's own actor brain reasserts control as the spirit's energy depletes. But during those moments, the player is fighting with someone else's loadout, someone else's capabilities, and someone else's personality -- a disorienting, powerful, brief experience that creates memorable encounters for both the player and the NPC (whose character-encounter service records "was briefly possessed by a guardian spirit during the battle of...").

**The God Variable**

Here is where the uncertainty becomes electric: the gods ARE watching the Last Stand. A god-actor's ABML behavior evaluates the spirit's performance in its post-death actions.

```yaml
# God-actor evaluating post-death spirit performance
when:
  condition: "${spirit.state} == 'unbound' AND ${spirit.post_death.duration} < ${death.degradation_window}"
  evaluate:
    - valor_score: |
        (${spirit.post_death.allies_buffed} * 0.2)
        + (${spirit.post_death.threats_revealed} * 0.3)
        + (${spirit.post_death.possession_combat_score} * 0.3)
        + (${spirit.post_death.allies_saved_from_death} * 0.5)
    - resurrection_cost: "${divine.divinity_current} * 0.3"
    - narrative_value: "${character.archive.density} * ${valor_score}"
    - decision: "${narrative_value} > ${resurrection_cost} AND ${personality.mercy} > 0.4"
  actions:
    - when:
        condition: "${decision}"
        actions:
          - service_call:
              service: divine
              endpoint: /divine/intervention/resurrect
              params:
                character_id: ${character.id}
                intervention_type: "resurrection"
                transformation_severity: "${1.0 - ${valor_score}}"
                # Higher valor = less transformation. Lower valor = "what they return as
                # may not be what they were"
```

The player doesn't know this evaluation is happening. They're just playing their final moments. Trying to help their allies. Coming to terms with death. And MAYBE the music shifts. MAYBE Thanatos whispers "not yet." Or maybe it doesn't, and they pass into the underworld proper -- and THAT'S also gameplay.

The god's decision factors:

| Factor | Weight | Notes |
|--------|--------|-------|
| **Valor score** | High | How well did the spirit perform post-death? |
| **God personality** | High | Merciful gods intervene more; aggressive gods let death stand |
| **Divinity cost** | Medium | Gods have finite resources; spending on resurrection costs future blessings |
| **Narrative value** | High | Is this character's story more interesting alive or dead? |
| **Archive density** | Medium | Characters with rich archives produce better content dead than alive |
| **Active storylines** | Medium | Unfinished narrative arcs make resurrection more likely |
| **Audience** | Low-Medium | Were other players watching? Dramatic resurrections in front of witnesses are more "worth it" to a god |

#### Phase 3: Resolution

The Last Stand ends in one of three ways:

**Resurrection (Changed)**

The god intervenes. The character returns -- but transformed. The "what they return as may not be what they were" principle from the Underworld system applies. Transformation severity scales inversely with the spirit's post-death valor (the gods reward performance with gentler returns):

| Valor | Transformation | Example |
|-------|---------------|---------|
| **High** (> 0.8) | Cosmetic only | A single white streak in the hair. Eyes that glow faintly in darkness. A scar that wasn't there before. |
| **Medium** (0.4-0.8) | Functional | One arm weaker but the other stronger. A new phobia (of the creature that killed them). Night vision but light sensitivity. |
| **Low** (< 0.4) | Significant | Personality axis shift (the character is fundamentally different). Memory gaps. A new compulsion. Changed species traits. |

These transformations are permanent, recorded in character-history ("returned from death, marked by Thanatos"), and become defining character traits. The character's NPC brain incorporates them -- a character who returned with a phobia of dragons will autonomously avoid dragon-related situations unless the spirit overrides.

Resurrection is RARE. The divinity cost is high. Gods who resurrect frequently deplete their domain power and lose influence. This scarcity is what makes the possibility meaningful -- it can't be relied upon, but it can't be ruled out either.

**Underworld Entry**

The spirit's energy depletes and the Last Stand ends. The character's logos enter the underworld -- the inside of the leyline network. This is NOT a game over screen. It is a new gameplay phase with its own mechanics:

- **Aspiration-based pathway selection**: Warriors with combat blessings who died in battle enter Valhalla (battle challenges to preserve memories and earn soul currency). Non-warriors with unfulfilled aspirations enter Purgatory (challenges matching their craft or calling -- a blacksmith faces forging trials, a diplomat faces negotiation puzzles). Fulfilled characters experience immediate peaceful dissipation.
- **Mobile Ethereal Base**: The guardian spirit recreates the living world's home at leyline locations within the underworld. Items sacrificed by living household members on the surface appear in the underworld base.
- **Soul Currency Economy**: The character's accomplishments and memories convert into spendable resources: resurrection (highest cost), blessings for living household members (moderate), artifacts imbued with the character's essence (moderate), spirit shards for trait inheritance (variable).
- **The Orpheus Journey**: In extreme cases, a living household member or ally can attempt to descend into the underworld to rescue the dead character. This is an extremely rare, high-stakes scenario -- the living character risks their own death in the attempt.

The underworld provides meaningful post-death gameplay that can last from minutes to hours depending on the character's aspiration state and the player's engagement.

**Passage**

The logos flow to the guardian spirit. The character's sense of self dissolves. The archive compresses with maximum narrative density -- the entire life, including the Last Stand performance and any underworld gameplay, feeds the content flywheel. Fresh plot armor generates for the next character. The cycle renews.

But the archive of those final moments -- the post-death valor, the allies saved, the desperate last stand, the underworld trials -- feeds the flywheel with exceptional narrative density. Ghost quests, memorial locations, NPC memories, legacy items, and narrative callbacks all emerge from a death that was PLAYED rather than merely suffered.

---

## The Letting Go Problem

The combination of household play, plot armor, divine intervention, resurrection possibility, AND underworld gameplay makes it genuinely difficult to permanently lose a character. This is mostly a feature -- death anxiety is dramatically productive. But it creates a secondary design problem: some players may need to WANT to move on and find that the systems keep pulling them back.

### The Fulfillment Principle (Mechanical Incentive)

The Underworld system already establishes: **more fulfilled in life = more logos flow to guardian spirit = greater household progression**. A character who has completed their aspirations and dies fulfilled provides MORE mechanical benefit than one who clings to life past their narrative peak. Specifically:

- **Completed aspirations**: Full experience points to guardian spirit
- **In-progress aspirations**: Partial credit based on completion percentage
- **Abandoned aspirations**: Minimal credit
- **Failed aspirations**: No penalty, no contribution

Additionally, **heroic death multipliers** apply:

| Death Context | Multiplier | Applicable Aspirations |
|---------------|-----------|----------------------|
| Died protecting others | 2x | Protection, leadership, community |
| Died pursuing knowledge | 2x | Discovery, learning, research |
| Died for community | 2x | Social, leadership, civic |
| Died in glorious combat | 2x | Warrior, honor, martial |

The system incentivizes letting go when the story is complete -- and rewards dying well over dying randomly.

### Retirement (Active Choice)

Characters who achieve their greatest aspirations can **retire** -- stepping back from active play to become autonomous NPCs. They're still alive, still running their NPC brain, still present in the world, but the guardian spirit releases its hold.

- Clean exits produce the best legacy bonuses (pressure for meaningful goals rather than dragging things out)
- The retired character's business, relationships, and property persist as simulation state
- New players encounter them as NPCs with genuine history
- The retired character may appear in future generations as an elder, a mentor, a legend

Retirement is the "letting go" mechanic that doesn't require death at all. The spirit voluntarily moves on because the story is complete.

### Transcendence (The Spirit Consumption)

Beyond retirement, there is a deeper option: **transcendence**. The guardian spirit and the character, after a full life, choose together to merge. The character's sense of self dissolves not into the underworld but directly into the spirit's growing divine consciousness.

This is metaphysically grounded: the guardian spirit IS a fragment of Nexius that feeds on connections and experiences. A fulfilled character whose logos flow freely is essentially being absorbed -- peacefully, willingly. The character's memories, personality traits, and accumulated skills become permanently integrated into the spirit's identity (not just inherited genetically through spirit shards, but deeply woven into the spirit's being).

The mechanical incentive: **transcendence yields more than death**:

| Exit Type | Guardian Spirit Growth | Archive Quality | Soul Currency | Trait Transfer |
|-----------|----------------------|-----------------|---------------|----------------|
| **Random death** | Base | Standard | Standard | Standard shards |
| **Heroic death** | Base + multiplier | High (dramatic context) | High | Enhanced shards |
| **Retirement** | High (fulfilled life) | Full (no degradation) | N/A (still alive) | Living legacy |
| **Transcendence** | Maximum | Perfect (direct integration) | Maximum | Deep integration -- traits become spirit traits, not just inherited |

Transcendence would be a ritual moment -- a cinematic event where the spirit and character acknowledge that this life is complete. Not a button press, but a narrative climax that other characters can witness. "The spirit light gathered around old Aldric as he sat by the fire, and when morning came, only the light remained."

This creates a gradient of exits where the most rewarding path is also the most emotionally satisfying: a life well-lived, concluded on the character's own terms, with the spirit enriched by the full depth of that life.

### The "Cannot Delete Characters" Principle

Characters cannot be deleted by player action. Intentionally getting a character killed is harder than it sounds -- the NPC brain fights to survive, other characters may intervene, and the gods may protect characters they find narratively interesting regardless of the player's intent. This prevents the degenerate strategy of farming character deaths for content and forces engagement with the retirement or transcendence pathways for players who want to move on.

---

## Emergent Death Mechanics

Beyond the core death loop, several emergent mechanics arise naturally from the interaction of existing systems with character death.

### The Witness Mechanic

When a character dies in a group event, other player characters who WITNESS the death gain something beyond an encounter record. The witnessing spirit briefly perceives the dying character's life flashing -- a compressed montage from the archive, filtered through the witness's progressive agency level. A spirit with deep social agency sees the dead character's key relationships. A spirit with combat agency sees their greatest battles.

This serves double duty:
- Emotionally affecting for the witness (they experience a fragment of the dead character's life)
- Seeds the content flywheel from the observer's perspective ("I was there when Aldric fell against the Wyrm of Ashenmoor" becomes THEIR story too)
- Creates asymmetric information -- different witnesses perceive different aspects of the montage based on their own agency domains

### The Inheritance Surprise

When a character dies, their will -- a Contract instance -- executes. Items, property, and relationship responsibilities transfer according to the will's terms (managed by Contract's milestone-based FSM).

The interesting part: what if the dead character had secrets? The death triggers revelation of information that was previously private to that character's actor:

- Debts the household didn't know about (Currency obligations surface)
- A hidden second family (Relationship records become visible to the household)
- An item that was actually stolen (Item provenance revealed, original owner's faction reacts)
- Secret faction membership (Faction records surface, creating new political complications)
- Incomplete quest obligations that now fall to the household (Quest assignments transfer)

These revelations are not scripted -- they emerge from the actual state of the dead character's relationships, contracts, and possessions. A character who lived a simple, honest life reveals nothing surprising. A character who led a complex double life creates a cascade of new plot threads from beyond the grave.

### The Haunting Period

Between death and the underworld, there's a brief window where the character's logos are scattering but haven't fully dissolved (the "seconds" window from the degradation timeline). During this window, the character exists as a ghost -- present at their own death scene, able to perceive but unable to interact with the physical world.

They watch their allies fight on. They watch NPCs react. They see who grieves and who doesn't. They observe the immediate consequences of their absence in real-time, played out by autonomous NPCs who genuinely react based on their relationship history with the deceased.

This is the "come to terms" period. It's the quiet moment in the eye of the storm where:
- The player processes the loss while watching the world continue without their character
- The character's relationships reveal their true depth (which NPCs rush to the body? which continue fighting? which flee?)
- The content flywheel captures the reactions of witnesses, creating rich encounter data
- The guardian spirit, now freed, can observe from a perspective the bound character never had

The Haunting Period is optional -- the player can choose to release at any time and proceed to the Last Stand or directly to the underworld. But players who linger are rewarded with context that enriches the archive and informs future gameplay.

### The Death Echo

Dungeon cores already create pneuma echoes (monsters) from logos memory seeds. The same metaphysical principle applies to dead characters: occasionally, a dead character's logos fragments get caught in a leyline current and echo somewhere unexpected.

The dead character appears as a brief, confused apparition in a random location -- not a ghost quest or a scripted story beat, but a glitch in the metaphysical system. A flickering image of the dead character performing a habitual action (the blacksmith hammering at a phantom anvil, the warrior sparring with shadows, the merchant counting invisible coins). Other characters who encounter it have it recorded via character-encounter. NPCs react based on their relationship to the dead character.

Death echoes are:
- **Rare**: Not every death produces one. Archive density and narrative significance factor in.
- **Emergent**: The location, timing, and manifestation are driven by leyline current simulation, not authored triggers.
- **Ambient**: They don't interact. They don't deliver quests. They exist and then fade. Their purpose is atmospheric -- a reminder that death in Arcadia is transformation, not erasure.
- **Cumulative**: A location near a major leyline confluence, over centuries of world simulation, accumulates death echoes. A battlefield becomes haunted not because a designer placed ghost spawners, but because hundreds of characters died there and some of their logos caught in the current. The world builds its own ghost stories.

---

## The Death Gradient

Death in Arcadia is a gradient, not a wall -- consistent with every other boundary in the game's design:

```
Still Alive
    │
    ▼
Near-Death (plot armor saves -- deus ex machina moments)
    │  The character survives. The archive records the close call.
    │  Plot armor chips. Tension builds.
    │
    ▼
The Fall (death cinematic triggers)
    │  The character dies. Music plays. Text appears.
    │  The player KNOWS this is death.
    │
    ▼
The Haunting (ghost at own death scene -- optional)
    │  Watch the world react. Process the loss.
    │  See who grieves. See who doesn't.
    │
    ▼
The Last Stand (spirit form / possession / ally assistance)
    │  Fight on as a freed spirit. Help allies.
    │  The gods are watching. Performance matters.
    │  Maybe the music shifts. Maybe Thanatos whispers.
    │
    ▼
Resolution
    ├──▶ Resurrection (god intervenes -- changed, marked, different)
    │       The character returns. Not the same. The story continues.
    │
    ├──▶ Underworld (aspiration-based afterlife gameplay)
    │       Valhalla, Purgatory, or peaceful dissipation.
    │       Soul currency. Mobile ethereal base. The Orpheus possibility.
    │
    └──▶ Passage (logos flow to guardian spirit)
            The archive compresses. Maximum narrative density.
            New character. Fresh plot armor. The cycle renews.
            Ghost quests, memorials, NPC memories, legacy items emerge.
            Death echoes may appear in the world.
```

Every stage is gameplay. Every stage has consequences. Every stage feeds the flywheel. The player can never be 100% certain which stage is final until they've passed through it. That uncertainty IS the engagement.

---

## Connection to Underworld System

This document's death mechanics integrate directly with the Underworld and Soul Currency System (documented in arcadia-kb). The key connections:

| This Document | Underworld System | Integration Point |
|--------------|-------------------|-------------------|
| The Fall (Phase 1) | Shell rupture, logos scatter | The death cinematic corresponds to the "0 seconds" moment |
| The Haunting | Ghost state (logos clusters before cleanup) | "Recognizable identity but fading over time" |
| The Last Stand (Phase 2) | Seconds/minutes degradation window | Spirit actions occur within the resurrection-possible window |
| God's Judgment | Divine intervention during degradation | Gods can spend divinity to reform the shell before logos scatter |
| Resurrection (changed) | "What they return as may not be what they were" | Transformation severity from the underworld system |
| Underworld Entry | Aspiration-based afterlife pathways | Valhalla (warriors), Purgatory (unfulfilled), dissipation (fulfilled) |
| Soul Currency | Post-death resource economy | Resurrection, blessings, artifacts, spirit shards |
| Passage | Logos become raw data in cleanup phase | Guardian spirit receives the processed logos |
| Death Echoes | Logos fragments in leyline currents | Pneuma echo mechanics from dungeon system applied to characters |

The degradation timeline governs the pacing of post-death gameplay:

```
0 seconds ─── The Fall ─── Haunting ─── Last Stand ─── God's Judgment
                                                            │
                    ┌───────────────────────────────────────┘
                    │
              Resurrection ◄── (within seconds window)
                    │
              Underworld ◄──── (within minutes window, partial data)
                    │
              Passage ◄──────── (cleanup phase, raw logos only)
```

The speed at which a character transitions through these phases is influenced by:
- **The killing blow's severity**: An instant-death attack scatters logos faster than bleeding out
- **Divine attention**: A god actively watching slows degradation (they're "holding" the logos together)
- **Leyline proximity**: Death near a leyline node preserves logos longer (the network provides structure)
- **Character's spiritual state**: Fulfilled characters dissipate peacefully (faster); unfulfilled characters cling (slower, more painful, but more time for intervention)

---

## Pacing Considerations

All of the post-death phases need careful tempo management. Too fast and the emotional beats don't land; too slow and it becomes tedious.

### Tempo Controllers

| Phase | Duration Target | What Controls Pacing |
|-------|----------------|---------------------|
| **The Fall** | 15-60 seconds | Death cinematic length (authored ABML) |
| **The Haunting** | 0-120 seconds | Player-controlled (can release at any time) |
| **The Last Stand** | 30-180 seconds | Degradation timer + spirit energy depletion |
| **God's Judgment** | 5-15 seconds | God-actor evaluation (async, player unaware) |
| **Underworld Entry** | Variable (minutes to hours) | Aspiration pathway depth; player engagement |
| **Passage** | 10-30 seconds | Transition cinematic to void/new character |

### Emotional Pacing Arc

```
Shock ──── Grief ──── Agency ──── Hope/Acceptance ──── Resolution
(Fall)     (Haunting)  (Last Stand)  (God's Judgment)    (Passage/Return)
```

The music service is critical here. The death music transitions through these emotional states, and the god-actors' ABML behaviors can influence the musical state via client events. A god deciding to resurrect triggers a musical shift from somber to transcendent BEFORE the player knows what's happening -- creating an intuitive sense that something has changed.

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
| Last Stand spirit form | Status (L4) + Actor (L2) | Temporary blessing buffs on nearby allies; perception injection |
| Last Stand possession | Actor (L2) + Connect (L1) | Spirit temporarily binds to NPC actor; session routing update |
| God's post-death eval | God-actor ABML behaviors | Valor scoring + resurrection decision behaviors |
| Resurrection transform | Character (L2) + Status (L4) | Character modification + transformation status effects |
| Haunting ghost state | Actor (L2) | Brief read-only actor state for the dying character |
| Witness montage | Character-Encounter (L4) + Agency (L4) | Archive excerpt filtered by witness's agency domains |
| Inheritance surprise | Contract (L1) + multiple L2 | Will execution via Contract FSM (already exists) |
| Death echoes | Actor (L2) | Ephemeral NPC actors spawned by leyline simulation |
| Underworld gameplay | Multiple (see Underworld system) | Separate design -- see arcadia-kb Underworld document |
| Transcendence ritual | Seed (L2) + Cinematic (L4, future) | Ritual cinematic + enhanced spirit growth calculation |
| Fulfillment multipliers | Seed (L2) + Character-History (L4) | Aspiration completion scoring at death time |
| Death music transitions | Music (L4) + Client events | Musical state changes driven by god-actor decisions |

**Total new code**: Zero services, zero plugins, zero SDKs. Status templates. Scenario metadata fields. ABML behavior documents. Client-side rendering for spirit form, possession, and haunting UX.

That's the architecture talking.

---

*This document describes the design for the death, plot armor, and post-death gameplay systems. For cinematic system architecture, see [CINEMATIC-SYSTEM.md](../plans/CINEMATIC-SYSTEM.md). For vision context, see [VISION.md](../reference/VISION.md) and [PLAYER-VISION.md](../reference/PLAYER-VISION.md). For orchestration patterns, see [ORCHESTRATION-PATTERNS.md](../reference/ORCHESTRATION-PATTERNS.md). For underworld mechanics, see the Underworld and Soul Currency System document in arcadia-kb.*
