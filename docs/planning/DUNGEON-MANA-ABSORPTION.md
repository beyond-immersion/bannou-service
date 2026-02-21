# Dungeon Mana Absorption: Cost of Entry

> **Status**: Design
> **Created**: 2026-02-18
> **Author**: Lysander (design) + Claude (analysis)
> **Category**: Dungeon mechanic (behavioral + service integration)
> **Related Services**: Dungeon (L4), Status (L4), Currency (L2), Environment (L4), Worldstate (L2), Actor (L2), Seed (L2), Puppetmaster (L4)
> **Related Docs**: [DUNGEON.md](../plugins/DUNGEON.md), [ACTOR-BOUND-ENTITIES.md](ACTOR-BOUND-ENTITIES.md), [DUNGEON-EXTENSIONS-NOTES.md](DUNGEON-EXTENSIONS-NOTES.md), [ORCHESTRATION-PATTERNS.md](../reference/ORCHESTRATION-PATTERNS.md)
> **Inspiration**: Solo Leveling (gates/dungeon ecology), Dungeon Core LitRPG genre, Arcadia pneuma thermodynamics

---

## Executive Summary

Dungeons continuously sap mana from intruders who cross their domain boundary. An initial burst absorbs a significant portion of the entrant's mana reserves on crossing the threshold (~20%), followed by a slower continuous drain (~1% per game-minute). This creates a natural time limit on dungeon exploration (~80 minutes before depletion), makes dungeon-diving an economically costly activity, and -- critically -- **fuels the dungeon's own mana economy from attempts, not just from deaths**.

The mechanic scales with dungeon intelligence. Dormant dungeons emit a passive absorption field (environmental ward, no targeting). Awakened dungeons can sense individual mana levels and intensify absorption against high-value targets. Ancient dungeons can toggle absorption on demand, suppress it to lure targets deeper, and spike it at critical moments.

The most significant emergent behavior: when a very powerful entity crosses the threshold, 20% of an enormously larger mana pool is a massive windfall. The dungeon's ABML behavior perceives the sudden mana spike and can immediately deploy capabilities that were previously too expensive. **A B-rank gate doesn't "become" S-rank. The dungeon receives fuel and spends it.** The strong fund their own destruction.

---

## The Pneuma Physics

Mana absorption is grounded in Arcadia's pneuma thermodynamics, not arbitrary game math.

### Surface Tension Model

Active pneuma -- the spiritual energy that constitutes a living entity's mana reserves -- has **natural surface tension**. The pneuma shell that defines an entity's spiritual boundary resists extraction the way a soap bubble resists puncture. The dungeon's absorption field works against this surface tension.

**Phase 1: Threshold Breach (Initial Absorption)**

When an entity crosses the dungeon's domain boundary, they pass through an interface between ambient pneuma density (normal world) and the dungeon's concentrated pneuma field. This density gradient creates a pressure differential that pierces the entity's pneuma shell at its weakest point. The initial extraction is rapid -- the shell reforms quickly, but not before a significant portion (~20%) of the entity's mana has been siphoned.

This is analogous to osmotic pressure in biology: two solutions of different concentration separated by a membrane. The dungeon's domain IS the high-concentration solution. Crossing the boundary IS the membrane interaction.

**Phase 2: Continuous Drain (Sustained Absorption)**

After the initial breach, the entity's pneuma shell has reformed but is now operating inside the hostile density gradient. The shell actively resists further extraction, but the dungeon's field exerts constant pressure. A slow, steady drain (~1% per game-minute) represents the ongoing energetic cost of maintaining pneuma shell integrity against the dungeon's field.

**Why 20% / 1%**: These are reference values for design discussion, not final tuning numbers. The actual ratios would be configurable per dungeon (seed capability-gated) and per game service. The key relationships are:
- Initial burst >> continuous drain rate (crossing the threshold is the expensive part)
- Natural defenses stabilize after the breach (the drain rate doesn't accelerate)
- Total viable exploration time is finite but meaningful (~60-120 minutes depending on tuning)

### Species and Individual Variation

Not all entities resist equally. The pneuma surface tension model creates natural variation:

| Factor | Effect on Absorption | Source Service |
|--------|---------------------|---------------|
| **Species pneuma density** | Denser pneuma = stronger shell = less % absorbed | Ethology (L4) behavioral archetype `pneuma_density` axis |
| **Individual mana pool size** | Larger pool = same % but more absolute mana transferred | Character stats / Currency wallet balance |
| **Active resistance (spells/items)** | Mana shield effects increase shell integrity | Status (L4) effects, Item (L2) equipment stats |
| **Environmental attunement** | Entities attuned to the dungeon's element resist less/more | Environment (L4) biome affinity |
| **Prior exposure** | Repeated visits may build tolerance (or sensitivity) | Character-Encounter (L4) dungeon visit history |
| **Divine protection** | Blessings can reinforce pneuma shell | Status (L4) blessing effects |

---

## The Three Emergent Economies

From one mechanic (mana sapping), three distinct gameplay loops emerge. None require dedicated implementation -- they arise from the interaction of absorption with existing systems.

### 1. Time-Limited Dungeon Diving

With 20% initial loss and 1% per minute continuous drain, an adventurer has approximately 80 minutes of exploration before reaching dangerous depletion levels. This creates:

**Strategic party composition**: Do you bring a healer who can slow the drain (Status effects that reinforce pneuma shells), or a DPS who clears faster? A party of five with a dedicated mana shielder can extend exploration time to 120+ minutes. A solo adventurer with no resistance gear has 60 minutes or less.

**Floor prioritization**: The dungeon has multiple floors with different environments and rewards (per DUNGEON-EXTENSIONS-NOTES.md). Do you rush to the boss floor, or explore side passages for loot? Every minute spent exploring is 1% more mana fed to the dungeon. The dungeon's ABML behavior can recognize this trade-off and respond accordingly.

**Escape risk**: The dungeon's cognition pipeline already has intrusion detection at priority 10 (highest). As intruders weaken, the dungeon can recognize the "desperate retreat" phase:

```yaml
# Dungeon ABML: recognize weakening intruders
when:
  condition: "${intruder.mana_percent} < 0.3 AND ${intruder.moving_toward_exit}"
  evaluate:
    - personality_check: "${personality.cruelty}"
    - capability_check: "seal_passage"
  actions:
    - when:
        condition: "${personality.cruelty} > 0.6 AND ${can.seal_passage}"
        action: "seal_passage: { target: nearest_exit_to_intruder }"
    - when:
        condition: "${personality.patience} > 0.7"
        action: "spawn_monster: { type: ambush, location: exit_corridor }"
```

**NPC GOAP integration**: NPC adventurers reasoning about whether to enter a dungeon can factor mana cost into their planning. An NPC with low mana reserves will avoid dungeons with high absorption rates. An NPC alchemist might assess the dungeon's expected loot value against the mana potion cost required to survive, and only enter if the economics work. This is standard GOAP action-cost evaluation -- the absorption rate is just another cost input.

### 2. Expensive but Rewarding Experience

The mana cost creates genuine economic activity around dungeon diving:

**Market for resistance items**: Mana potions, pneuma shell reinforcement enchantments, absorption-resistant armor. NPC crafters, alchemists, and enchanters whose GOAP planners recognize demand and produce supply. Trade routes carrying resistance supplies to dungeon-adjacent towns. Prices that fluctuate based on dungeon activity in the region.

**Risk/reward calculation via GOAP**: NPCs don't need special dungeon logic -- their standard economic GOAP evaluation handles this:

```
Action: "explore_dungeon_thornhold"
  Preconditions: mana_reserves > 50%, has_resistance_gear, party_size >= 3
  Cost: mana_absorption(estimated_time * drain_rate) + resistance_item_cost + risk_of_death
  Expected_Reward: loot_value(dungeon_tier) + experience_gain + genetic_material_sale
  Decision: Cost < Expected_Reward ? accept : reject
```

**Professional dungeon diving as economic activity**: Factions (L4) can regulate dungeon diving through guild charters. Obligation (L4) tracks dungeon diving contracts -- a guild charter might require minimum mana reserves before diving, mandate party composition, or impose insurance requirements via Contract (L1). A guild that loses too many members to reckless dungeon diving faces internal political consequences via Faction governance mechanics.

**Insurance and recovery services**: Contract templates for dungeon diving insurance -- a healer guild that stations a recovery team at the dungeon entrance, ready to extract depleted adventurers for a fee. This is pure NPC GOAP economics: the healer guild evaluates the business opportunity (fee income vs. risk of entering the dungeon to extract) and prices its services accordingly.

### 3. Self-Sustaining Dungeon Ecology

This is the architecturally significant contribution. The existing Dungeon spec's `mana_reserves.harvested` seed growth domain grows from deaths. Mana absorption adds **non-lethal income**:

**Sustainable dungeons**: A dungeon that absorbs from 10 visitors per day who each survive is sustainably fueled. The dungeon doesn't need to kill anyone to grow. This creates a fundamentally different dungeon ecology than the "lethal trap" model.

**Personality-driven economic strategy**: The dungeon's ABML behavior can CHOOSE between "kill them for maximum harvest" and "let them survive and come back tomorrow." This choice is driven by personality traits on the character brain (Stage 3):

| Personality Axis | Low Value Strategy | High Value Strategy |
|-----------------|--------------------|--------------------|
| `${personality.patience}` | Immediate aggression, maximum extraction per visit | Long-term farming, sustainable income, let visitors survive |
| `${personality.greed}` | Conservative defense, protect the core | Aggressive expansion, maximize absorption radius |
| `${personality.cunning}` | Transparent threat, standard absorption | Suppress absorption to lure deep, then spike |
| `${personality.cruelty}` | Efficient defense, minimal suffering | Trap-heavy, torture-maze design, slow the escape |
| `${personality.curiosity}` | Ignore weak visitors, focus on threats | Study all visitors, absorb and observe |

**Domain expansion as capital investment**: A dungeon that invests mana into extending its absorption radius (via `domain_expansion.extension` seed capability) to capture passive mana from nearby travelers is making a capital investment -- spending mana now for greater future income. The cursed ruins that keep spreading are a dungeon that chose the expansion strategy. Its GOAP planner discovered that widening the field, even at reduced per-entity yield, produces more total income than concentrated absorption.

```yaml
# Dungeon ABML: evaluate expansion investment
evaluate_expansion:
  when:
    condition: "${dungeon.mana_income_rate} < ${dungeon.mana_expense_rate} * 1.2"
  compute:
    current_radius: "${seed.domain_expansion.radius}"
    expansion_cost: "${current_radius} * ${config.expansion_cost_multiplier}"
    estimated_passive_targets: "${environment.entity_density_at_boundary} * ${config.expanded_radius_delta}"
    estimated_income_gain: "${estimated_passive_targets} * ${config.passive_absorption_rate}"
    roi_days: "${expansion_cost} / ${estimated_income_gain}"
  decision:
    - when:
        condition: "${roi_days} < 30 AND ${can.domain_expansion}"
        action: "expand_domain: { delta: ${config.expanded_radius_delta} }"
```

**The cursed ruins pattern**: A dungeon whose expansion strategy succeeds gradually transforms the surrounding area. The outermost absorption zone is weak -- travelers feel mildly drained, crops grow poorly, animals are sluggish. The middle zone is noticeable -- extended exposure is dangerous, residents need to leave or acquire resistance. The core zone is lethal to the unprepared. This creates an organic "cursed zone" around the dungeon that grows or shrinks based on the dungeon's strategic decisions and the resistance of the local population.

---

## The Windfall Mechanic: Strong Targets Fund Their Own Destruction

This is the design's most compelling emergent behavior and the mechanical explanation for the Solo Leveling "Red Gate" phenomenon.

### The Math

Consider a dungeon with a 20% initial absorption rate and a current mana reserve of 100 units:

| Entrant | Mana Pool | 20% Absorbed | Dungeon Mana After | Capabilities Unlocked |
|---------|-----------|-------------|--------------------|-----------------------|
| E-rank novice | 50 | 10 | 110 | Nothing new |
| B-rank adventurer | 500 | 100 | 200 | +1 enhanced spawn, basic traps |
| A-rank veteran | 5,000 | 1,000 | 1,100 | Multiple alpha spawns, complex traps, passage sealing, layout shifting |
| S-rank hero | 50,000 | 10,000 | 10,100 | EVERYTHING. Every capability at maximum budget. |

The dungeon doesn't "scale" to the entrant. The dungeon receives fuel and spends it. The B-rank gate produces B-rank defenses against a novice because a novice provides B-rank fuel. The same gate produces S-rank defenses against an S-rank hero because the hero provided S-rank fuel.

### The Dungeon's Reaction

The dungeon's cognition pipeline perceives the windfall in real-time:

```yaml
# Dungeon ABML: react to massive mana influx
perception_filter:
  - event: "domain.mana_absorbed"
    conditions:
      - check: "${event.amount} > ${dungeon.mana_reserves} * 0.5"
    urgency: 0.95  # Near-maximum, bypasses normal attention queue

when:
  condition: "${dungeon.mana_reserves} > ${dungeon.mana_reserves_previous} * 3.0"
  evaluate:
    - threat_level: "extreme"
    - available_budget: "${dungeon.mana_reserves} - ${dungeon.mana_minimum_reserve}"
    - optimal_response: "GOAP_PLAN: eliminate_threat(budget: ${available_budget})"
  actions:
    - spawn_monster: { type: alpha, species: strongest_available, count: max_affordable }
    - activate_trap: { type: all_available, targeting: intruder_with_highest_mana }
    - seal_passage: { target: exit_routes }
    - emit_miasma: { intensity: maximum }  # Increase continuous drain rate
    - communicate_master: { urgency: critical, data: "overwhelming_threat_detected" }
```

An Awakened dungeon (Stage 3, character brain) adds personality-driven nuance to this response:

- A dungeon with high `${personality.cunning}` might NOT deploy everything immediately. It might absorb the windfall, deploy modest initial defenses to give the hero false confidence, and then escalate as the hero ventures deeper. The hero is weakening (continuous drain) while the dungeon is getting richer (stored windfall + ongoing absorption).

- A dungeon with high `${personality.cruelty}` deploys everything at the entrance. The hero walks into maximum force immediately. No subtlety, just overwhelming power funded by the hero's own mana.

- A dungeon with high `${personality.patience}` might let the hero pass through entirely, absorbing the windfall but deploying nothing. The hero clears the dungeon, takes the loot, and leaves. The dungeon now has 10,000 extra mana to invest in long-term growth (domain expansion, better genetic library, deeper floors). The dungeon chose investment over confrontation.

### The Solo Leveling Parallel (Improved)

Solo Leveling's Red Gates are narratively effective but mechanically unexplained. The series offers three non-answers:
1. The System (a transcendent being) engineers dangerous situations to train the protagonist
2. The Monarchs (intelligent antagonists) deliberately create harder gates near powerful hunters
3. Red Gates are random/predetermined with no mechanical trigger

The mana absorption model provides a causal mechanism that is mechanically superior to all three:

| Solo Leveling | Mana Absorption Model |
|--------------|----------------------|
| "Red Gates happen randomly" | Gates have fixed absorption %. The experience scales because the fuel does. |
| "The System makes it harder" | No external scaling authority needed. The dungeon's own economics do the work. |
| "Monarchs target the hero" | No intelligent antagonist needed (though dungeon intelligence enhances the effect). |
| The hero encounters B-rank gate that becomes S-rank | The hero's 20% funded S-rank defenses. The gate was always B-rank. The response was S-rank. |
| Unexplained escalation | Explained by pneuma economics. Cause and effect, not narrative handwaving. |

The additional insight: in Solo Leveling, the protagonist benefits from dungeon deaths via shadow extraction (he IS the entity that profits from dungeon clearing). In Arcadia, the dungeon benefits from the hero's *arrival*, not just their death. The attempt itself is fuel. The dungeon profits whether or not it wins. This creates the asymmetry where **the more powerful an adventurer is, the more they fund the dungeon by merely entering**, regardless of combat outcome.

### The "Ambush Predator" Evolution

Over time, an intelligent dungeon might evolve (via personality trait shifts through CharacterPersonality's experience-driven evolution) toward an "ambush predator" strategy:

1. Deliberately project a weak outward appearance (low-level monsters near the entrance, modest traps)
2. Let assessment teams rank the dungeon as low-tier
3. Wait for a powerful adventurer to enter based on the false assessment
4. Absorb the windfall
5. Deploy overwhelming force from the windfall budget
6. Feed on the result

This is emergent behavior from the interaction of absorption mechanics, personality evolution, and GOAP planning. No service needs to implement "ambush predator dungeons" -- the dungeon's actor discovers this strategy through accumulated experience. A dungeon that tried the honest approach and kept getting cleared might evolve toward cunning after repeated defeats (personality shifts via `${encounters.last_defeat_days}` feeding CharacterPersonality evolution). The content flywheel generates dungeon-specific lore: "Thornhold was once considered a simple training dungeon. Then the A-rank party of Silverwind entered. None returned. The guild's assessment was revised."

---

## Cognitive Staging: Absorption Across Dungeon Intelligence

The absorption mechanic scales naturally across the three cognitive stages from DUNGEON.md:

### Stage 1: Dormant (No Actor)

The absorption field is purely environmental. A passive ward created by the pneuma density gradient of the dungeon's domain. No intelligence, no targeting, no response to windfalls.

- **Initial absorption**: Fixed percentage, environmental constant
- **Continuous drain**: Fixed rate, environmental constant
- **Targeting**: None -- equal absorption for all entities
- **Windfall response**: None -- absorbed mana accumulates in Currency wallet but no actor processes the influx
- **Mechanic**: The dungeon is a hazardous environment, like a radiation zone. Enter at your own risk. The mana feeds the dungeon's passive seed growth in `mana_reserves.ambient`.

This is the vast majority of dungeons in the world. Thousands of dormant cores with passive absorption fields. Most are minor hazards -- the absorption rate is low because the pneuma density gradient is mild. Travelers feel slightly drained when passing through. Locals avoid the area. The mana trickles in.

### Stage 2: Event Brain (Stirring)

The dungeon has an actor but no character identity. Instinct-driven ABML behavior with seed variable providers. The absorption field gains basic reactivity.

- **Initial absorption**: Fixed percentage, but the dungeon can modulate its field intensity (gated by `emit_miasma` capability)
- **Continuous drain**: Adjustable via miasma control -- the dungeon can increase local pneuma density to accelerate drain in specific areas
- **Targeting**: Basic threat detection. The dungeon perceives mana pool sizes of entities in its domain (via intrusion events). It cannot target individuals but can intensify the field in zones where high-mana entities are detected.
- **Windfall response**: Hard-coded escalation in ABML behavior. A massive mana influx triggers a predetermined "maximum defense" response -- spawn all available monsters, activate all traps. No strategic nuance.
- **Mechanic**: The dungeon is a reactive threat. It doesn't plan, but it responds to stimuli with increasing force as its budget allows. An event brain dungeon that receives a windfall is dangerous the way a cornered animal is dangerous -- all-out, no subtlety.

### Stage 3: Character Brain (Awakened / Ancient)

The dungeon has a full character identity with personality, memories, and encounters. The absorption field becomes a strategic tool.

- **Initial absorption**: Variable. The dungeon can **sense individual mana levels before entities enter** (via perception of entities approaching its domain boundary). It can choose to intensify absorption against specific targets. An Ancient dungeon might absorb 30% from an S-rank hero and only 10% from their weak companion -- tactical extraction.
- **Continuous drain**: Fully controllable. The dungeon can create zones of high and low drain within its domain. A trap corridor with intense drain to weaken intruders before a boss room. A "safe zone" with suppressed drain to create false confidence. Drain rate as a weapon.
- **Targeting**: Individual targeting gated by `${personality.*}` and seed capabilities. The dungeon recognizes returning adventurers via `${encounters.*}` and can adjust absorption based on history. "This one defeated me last time. Extract maximum."
- **Windfall response**: GOAP-planned strategic response. The dungeon evaluates the windfall amount, the intruder's capabilities (from encounter history and perceived mana signature), its own available capabilities, and its personality, then constructs an optimal response plan. Different personalities produce different strategies (see [Personality-Driven Economic Strategy](#3-self-sustaining-dungeon-ecology) above).
- **Suppression and spiking**: Ancient dungeons can suppress their absorption field entirely to lure targets deep, then slam it at a chokepoint. The initial 20% becomes 40% because the dungeon held back at the entrance and doubled down when the target was committed.

```yaml
# Ancient dungeon ABML: suppression and spike tactic
when:
  condition: "${personality.cunning} > 0.7 AND ${intruder.detected_power} > ${dungeon.threat_threshold}"
  actions:
    # Phase 1: Suppress absorption at entrance
    - emit_miasma: { intensity: minimal, zone: entrance }
    - memory_note: { type: tactical, content: "luring_high_value_target" }

    # Phase 2: Wait for target to reach depth threshold
    - wait_condition: "${intruder.depth} > ${dungeon.floors} * 0.6"

    # Phase 3: Spike absorption + seal exits
    - emit_miasma: { intensity: maximum, zone: all }
    - seal_passage: { target: all_exit_routes }
    - spawn_monster: { type: alpha, location: between_intruder_and_exit }

    # The intruder now faces maximum drain, sealed exits, and alpha monsters
    # between them and escape. Their own mana funded this.
```

---

## Integration with Existing Dungeon Systems

### Mana Economy

The Dungeon spec already defines a Currency wallet for mana. Absorbed mana credits this wallet directly:

| Mana Source | Existing in DUNGEON.md | New (Absorption) |
|------------|----------------------|-------------------|
| Ambient leyline proximity | Yes (`mana_reserves.ambient` seed growth) | -- |
| Deaths within domain | Yes (`mana_reserves.harvested` seed growth) | -- |
| Monster kills within domain | Yes (+0.1 per kill) | -- |
| Threshold absorption (initial) | -- | Yes (20% of entrant's pool, credits wallet) |
| Continuous absorption (drain) | -- | Yes (1%/min of entrant's pool, credits wallet) |
| Windfall from powerful targets | -- | Yes (emergent from absorption %) |

Absorption income adds a new column to the dungeon's revenue model. The dungeon's GOAP planner can now evaluate "attract more visitors" alongside "kill more intruders" as income strategies.

### Seed Growth

Absorption events contribute to seed growth:

| Growth Event | Domain | Amount | Notes |
|-------------|--------|--------|-------|
| Entity enters domain (threshold absorption) | `mana_reserves.harvested` | +0.2 per entity | Any entity crossing the boundary |
| Sustained absorption (per game-hour) | `mana_reserves.ambient` | +0.05 per entity per hour | Passive income growth |
| Windfall absorption (high-value target) | `mana_reserves.harvested` | +1.0 per windfall event | When absorbed amount exceeds dungeon's current reserves |
| Successful use of absorption for defense | `trap_complexity.magical` | +0.1 | When absorption directly contributed to an intruder defeat |

### "Eliminate Threats at All Costs" Synergy

The existing Dungeon spec describes dungeons that deploy maximum force against existential threats. Mana absorption creates a terrifying positive feedback loop:

1. S-rank adventurer enters. Dungeon absorbs 20% -- a massive mana windfall.
2. Dungeon's threat assessment: `${intruder.detected_power}` is extremely high.
3. Dungeon's GOAP planner: "I now have enough mana to deploy EVERYTHING. This threat must not leave alive."
4. The adventurer's own power funded their destruction.
5. **If the adventurer dies**: the dungeon gets BOTH the absorption mana AND the death harvest (`mana_reserves.harvested` + `genetic_library.{species}` logos).
6. The dungeon is now significantly more powerful than before this encounter.

This creates the narrative where "the strongest adventurers face the most dangerous dungeons" -- not through mysterious scaling, but through cold economic logic. The dungeon was fed a feast and used it.

### Master Bond Interaction

A bonded dungeon master (Pattern A or B) adds tactical depth to absorption:

**Priest bond**: The master can channel their own mana INTO the dungeon voluntarily, supplementing absorption income. A master who meditates at the dungeon core feeds it directly -- no intruders needed. This creates a "tame dungeon" pattern: the master feeds the dungeon, the dungeon protects the master, the dungeon's growth serves the master's interests.

**Paladin bond**: The master can channel the dungeon's absorbed mana back through themselves for combat. When an S-rank hero enters and provides a windfall, the dungeon master receives a temporary power boost from the excess. The master and dungeon share the feast.

**Corrupted bond**: The monster avatar IS the absorption mechanism. In a Corrupted dungeon, the dominated monster patrols the boundaries and actively drains mana from entities it contacts. The absorption is focused through the avatar rather than ambient. Less efficient (smaller area) but more intense (higher per-contact drain).

---

## The "Sustainable Dungeon" Ecology

The most significant gameplay consequence of mana absorption is the emergence of dungeons that don't need to kill anyone.

### The Spectrum

| Dungeon Strategy | Kill Rate | Income Source | Growth Pattern | World Impact |
|-----------------|-----------|---------------|----------------|-------------|
| **Lethal predator** | High | Deaths + absorption | Fast initial, then stagnant (adventurers stop coming) | Fear, avoidance, military response |
| **Challenging gauntlet** | Medium | Balanced kills + absorption | Steady growth from regular challengers | Reputation, guild contracts, adventure economy |
| **Sustainable tourism** | Low | Primarily absorption | Slow but consistent growth | Economic integration, "tamed" dungeon, local industry |
| **Passive hazard** | None | Ambient absorption only | Very slow growth | Cursed zone, avoidance area, gradual expansion |
| **Ambush predator** | Sporadic high | Windfall absorption + rare kills | Burst growth from occasional high-value targets | Deceptive, reputation for false assessments |

A dungeon's strategy is NOT fixed. As its personality evolves through CharacterPersonality's experience-driven trait shifts, its strategy shifts. A lethal predator that keeps getting cleared might evolve toward cunning (ambush predator). A sustainable tourism dungeon whose master dies might shift toward aggression (lethal predator). The strategy is emergent from personality, not assigned.

### The Town-Dungeon Symbiosis

A sustainable tourism dungeon near a settlement creates a local economy:

- **Adventurer's guild** regulates access, maintains a queue, ensures minimum preparation
- **Alchemists** sell mana potions and resistance items (demand scales with dungeon traffic)
- **Healers** station a recovery team at the entrance (extraction service for depleted adventurers)
- **Merchants** buy dungeon loot and sell supplies (trade route to the dungeon settlement)
- **The dungeon** gets steady mana income and grows slowly but consistently
- **The dungeon master** (if bonded) mediates between the dungeon and the settlement

This is the "dungeon town" trope (Dungeon Meshi, DanMachi, Made in Abyss) emergent from economic mechanics. No service needs to implement "dungeon towns" -- the NPC GOAP economy discovers the symbiosis naturally. Hermes/Commerce (regional watcher god) might even facilitate the arrangement through divine economic intervention.

### The Arms Race

A dungeon that grows from sustained absorption eventually becomes too dangerous for its current tier of adventurers. Higher-tier adventurers arrive. They provide more mana per visit. The dungeon grows faster. Even higher-tier adventurers are needed.

This is a natural escalation that mirrors real economic growth cycles. The dungeon's guild assessment rating climbs. The local economy adapts (better gear shops, higher-tier alchemists). The settlement grows. Until one day, the dungeon reaches a phase transition (Ancient threshold at 200.0 seed growth) and becomes qualitatively different -- a living entity with centuries of memory, personality evolution, and strategic depth.

The content flywheel feeds this escalation: every cleared dungeon run, every defeated monster, every near-death experience generates archives. Regional watcher gods compose narratives from these archives. The dungeon's history IS content.

---

## Resistance and Counterplay

The mechanic is compelling only if players have meaningful counterplay. Resistance is modeled through the same pneuma surface tension framework:

### Passive Resistance

| Source | Mechanism | Effect |
|--------|-----------|--------|
| **Species density** | Ethology behavioral archetype provides base pneuma density | Some species naturally resist more (dwarves > elves for physical pneuma density, reversed for magical) |
| **Level/experience** | Higher-level characters have denser pneuma shells from accumulated experience | Veteran adventurers lose less % to the same field |
| **Equipment** | Items with pneuma-reinforcing enchantments | "Dungeon Ward" armor reduces initial absorption by X% |
| **Potions** | Consumable mana shield potions | Temporary resistance boost, timed for dungeon entry |

### Active Resistance

| Source | Mechanism | Effect |
|--------|-----------|--------|
| **Mana shield spells** | Spellcaster maintains an active barrier against the drain | Continuous mana expenditure to resist the drain (fighting drain with drain) |
| **Group resistance aura** | Healer/support character projects a pneuma shell extension | Party-wide drain reduction while the aura is maintained |
| **Divine blessings** | God-actor grants temporary absorption resistance | Status effect: "Blessed of Silvanus" -- nature's pneuma resists dungeon fields |
| **Dungeon master aid** | Bonded master can suppress absorption for specific individuals | The master allows their allies to enter with reduced drain |

### Recovery

| Source | Mechanism | Effect |
|--------|-----------|--------|
| **Mana potions** | Consumable item restores mana | Extends exploration time (but the dungeon absorbs recovered mana too) |
| **Rest zones** | Some dungeons have natural pockets of low pneuma density | Reduced drain rate in specific rooms (dungeon personality determines if these exist) |
| **Exit and re-enter** | Leave the dungeon to recover outside | Resets the continuous drain timer but triggers another threshold absorption on re-entry |
| **Leyline tapping** | If the dungeon is built on a leyline, skilled mages can tap the leyline directly | Risky (the dungeon can sense this and respond), but provides a mana source inside the dungeon |

The "exit and re-enter" counterplay creates an interesting dynamic: leaving costs another 20% on re-entry. A 5-floor dungeon might require multiple exits and re-entries, each one feeding the dungeon. By the final entry, the dungeon has absorbed 60%+ of the adventurer's total mana across three visits. The dungeon profits from persistence as much as from power.

---

## Configuration

All absorption parameters are configurable per dungeon (via seed capability gating) and per game service (via Environment/Dungeon configuration):

| Parameter | Env Var | Default | Purpose |
|-----------|---------|---------|---------|
| `BaseThresholdAbsorptionPercent` | `DUNGEON_BASE_THRESHOLD_ABSORPTION_PERCENT` | `0.20` | Default initial absorption on domain entry |
| `BaseContinuousDrainPerMinute` | `DUNGEON_BASE_CONTINUOUS_DRAIN_PER_MINUTE` | `0.01` | Default continuous drain rate (per game-minute) |
| `MaxThresholdAbsorptionPercent` | `DUNGEON_MAX_THRESHOLD_ABSORPTION_PERCENT` | `0.50` | Maximum initial absorption (Ancient dungeons with targeted extraction) |
| `MaxContinuousDrainPerMinute` | `DUNGEON_MAX_CONTINUOUS_DRAIN_PER_MINUTE` | `0.05` | Maximum continuous drain rate (emit_miasma at maximum) |
| `WindfallThresholdMultiplier` | `DUNGEON_WINDFALL_THRESHOLD_MULTIPLIER` | `3.0` | Mana influx that qualifies as "windfall" (absorbed > current_reserves * multiplier) |
| `PassiveAbsorptionRadius` | `DUNGEON_PASSIVE_ABSORPTION_RADIUS` | `0.0` | Radius beyond domain boundary for passive ambient drain (0 = domain only, >0 = cursed zone) |
| `AbsorptionResistanceFloor` | `DUNGEON_ABSORPTION_RESISTANCE_FLOOR` | `0.50` | Minimum % of mana an entity always retains (prevents full drain; ensures escape remains possible) |

The `AbsorptionResistanceFloor` is a critical safety valve: no entity can be drained below 50% (configurable) of their total mana by absorption alone. This ensures escape is always theoretically possible -- the dungeon must defeat the adventurer through combat, traps, or other means, not through drain alone. The drain creates urgency and funds the dungeon; combat creates the actual lethality.

---

## Seed Capability Gating

Absorption capabilities are gated by the `dungeon_core` seed, following the existing capability rules pattern:

| Capability Code | Domain | Threshold | Formula | Description |
|----------------|--------|-----------|---------|-------------|
| `absorb.passive` | `mana_reserves` | 0.0 | -- | Always available. Passive field at base rates. |
| `absorb.modulate` | `mana_reserves` | 5.0 | linear | Can adjust field intensity via emit_miasma. |
| `absorb.zone` | `mana_reserves` | 15.0 | logarithmic | Can create zones of differential drain within domain. |
| `absorb.sense` | `mana_reserves` | 10.0 | step | Can sense mana pool sizes of entities approaching domain. |
| `absorb.target` | `mana_reserves` | 25.0 | logarithmic | Can target specific individuals for enhanced absorption. |
| `absorb.suppress` | `mana_reserves` | 30.0 | step | Can suppress absorption entirely (lure tactic). |
| `absorb.spike` | `mana_reserves` | 40.0 | step | Can spike absorption rate temporarily (ambush tactic). |
| `absorb.expand` | `domain_expansion` | 20.0 | logarithmic | Can extend passive absorption beyond domain boundary (cursed zone). |

These capabilities layer onto the existing DUNGEON.md capability table. A Dormant dungeon has only `absorb.passive`. An Ancient dungeon with sufficient growth has access to the full absorption toolkit.

---

## Service Integration Summary

| Service | Role in Absorption | Integration Point |
|---------|-------------------|-------------------|
| **Dungeon (L4)** | Owns absorption logic, capabilities, strategy | ABML behavior + dungeon variable providers |
| **Currency (L2)** | Stores absorbed mana in dungeon wallet | `ICurrencyClient.CreditAsync` on absorption events |
| **Status (L4)** | Applies "Mana Drain" effect to intruders; applies resistance effects | Status templates: `DUNGEON_MANA_DRAIN`, `MANA_SHIELD`, `BLESSED_RESISTANCE` |
| **Environment (L4)** | Models pneuma density field within dungeon domain | Climate template integration: `pneuma_density` as environmental axis |
| **Worldstate (L2)** | Provides game-time for drain rate calculation | `${world.elapsed_minutes}` for duration-based drain computation |
| **Seed (L2)** | Gates absorption capabilities; tracks growth from absorption | Existing seed capability system with new `absorb.*` capabilities |
| **Ethology (L4)** | Provides species-level pneuma density for resistance calculation | `${ethology.pneuma_density}` behavioral archetype axis |
| **Character-Encounter (L4)** | Tracks dungeon visit history for returning visitor recognition | Existing encounter records; dungeon recognizes repeat visitors |
| **Item (L2)** | Resistance equipment, mana potions | Standard item stats for pneuma shell reinforcement |
| **Actor (L2)** | Executes dungeon ABML behavior including absorption decisions | Standard Actor runtime with dungeon variable providers |
| **Puppetmaster (L4)** | Loads dungeon ABML behaviors including absorption patterns | Standard behavior document loading |

**No new services required.** The absorption mechanic composes from existing primitives: Currency for mana transfer, Status for drain effects, Environment for field modeling, Seed for capability gating, Actor for strategic decision-making.

---

## Content Flywheel Integration

Mana absorption enriches the content flywheel at every stage:

**Archive density**: Dungeon encounters where the absorption mechanic created urgency -- racing against the drain timer, desperate escapes, windfall-funded ambushes -- produce richer archives than simple "entered dungeon, killed monsters, left" records. The time pressure creates drama that the archive captures.

**Dungeon lore**: "Thornhold was fed a feast when the S-rank hero crossed its threshold" becomes realm history. Regional watcher gods evaluate these archives and compose narratives. Quests emerge: "investigate why Thornhold suddenly became more dangerous" or "escort an alchemist to collect samples from the high-density pneuma field."

**Economic narratives**: The NPC economy that forms around dungeon absorption (potion merchants, resistance gear crafters, healer extraction teams, guild insurance) generates its own economic history. When a dungeon dies or goes dormant, the economic ecosystem collapses -- another flywheel input.

**Dungeon personality evolution**: A dungeon that started as a simple cave and evolved into a cunning ambush predator over decades of absorbed mana has a character arc. Its personality shifts are recorded via CharacterPersonality. Its encounter history with specific adventurers is recorded via CharacterEncounter. When it finally falls, that archive is extraordinarily rich -- a thinking entity with grudges, strategies, and a history of interactions with the world.

---

## Design Considerations

### 1. Absorption vs. Combat Lethality

The `AbsorptionResistanceFloor` (default 50%) ensures absorption alone cannot kill. This is a deliberate design choice: the drain creates urgency and funds the dungeon, but actual killing requires combat, traps, or environmental hazards. A dungeon that relies purely on drain would starve adventurers of mana but never finish them off -- unless it combines drain with monsters or traps. The two systems are complementary, not substitutes.

### 2. PvP Implications

If player characters have mana wallets (via Currency), mana absorption could theoretically drain PvP opponents. This is out of scope for the dungeon mechanic (the absorption field is dungeon-domain-specific, not a combat ability), but the pneuma surface tension model could be extended to PvP contexts via the Status service (a "Mana Siphon" spell that works on the same physics).

### 3. Dungeon Entry Pricing

In a "dungeon town" ecology, the guild or the dungeon master might charge an admission fee separate from the absorption cost. This creates a two-layer cost: explicit fee (Currency transfer) + implicit drain (absorption). The explicit fee funds the guild/master; the implicit drain funds the dungeon. Both are legitimate economic activities.

### 4. Absorption-Immune Entities

Some entities might be immune to absorption: other dungeons (pneuma fields cancel out), divine avatars (too much pneuma density to pierce), or entities with specific enchantments. Immunity is modeled as a Status effect that sets the entity's resistance above 100%, causing the absorption calculation to produce zero drain. The dungeon's actor perceives this (the absorption event fires but reports zero extraction) and can react -- an immune entity in the dungeon is a threat that can't be weakened by the standard mechanism.

### 5. Interaction with Death & Plot Armor

Per the [DEATH-AND-PLOT-ARMOR.md](DEATH-AND-PLOT-ARMOR.md) design: a character with plot armor cannot be killed by dungeon encounters. However, absorption still applies -- the character loses mana and faces genuine resource pressure. Plot armor prevents death, not drain. A character with high plot armor but low mana after absorption is in a unique position: they can't die, but they're essentially powerless. The dungeon can't kill them, but they can't clear the dungeon either. The narrative tension becomes "how do they escape while drained and divine-protected but helpless?"

### 6. Mana Depletion Consequences

What happens when an entity reaches the absorption resistance floor? They can't be drained further, but operating at 50% mana has consequences. This connects to whatever magic/combat system Arcadia implements -- presumably spellcasting becomes limited, physical abilities that draw on pneuma are weakened, and the entity's natural regeneration works against the continuous drain (a slowly losing battle until they leave the domain).

---

## Implementation: No New Services Required

| Component | Existing Service | What's Needed |
|-----------|-----------------|---------------|
| Absorption field | Environment (L4) | `pneuma_density` as environmental condition axis per dungeon domain |
| Drain status effect | Status (L4) | `DUNGEON_MANA_DRAIN` status template (seed data) |
| Resistance effects | Status (L4) | `MANA_SHIELD`, `BLESSED_RESISTANCE` status templates |
| Mana wallet transfer | Currency (L2) | Standard `CreditAsync` / `DebitAsync` on absorption events |
| Absorption capabilities | Seed (L2) | `absorb.*` capability rules on `dungeon_core` seed type |
| Strategic absorption | Actor (L2) ABML | Dungeon behavior patterns for modulation, targeting, suppression |
| Windfall detection | Dungeon variable provider | `${dungeon.mana_reserves}` change detection in ABML |
| Resistance items | Item (L2) | Item templates with pneuma reinforcement stats |
| Mana potions | Item (L2) | Consumable item templates that restore mana (existing pattern) |
| Visit tracking | Character-Encounter (L4) | Standard encounter records for dungeon visits |
| Drain rate timing | Worldstate (L2) | `${world.elapsed_minutes}` for game-time-based drain calculation |
| Species resistance | Ethology (L4) | `pneuma_density` behavioral archetype axis |

**Total new code**: Status templates. Seed capability rules. ABML behavior patterns. Environment configuration for pneuma density. Configuration schema entries. Client-side rendering for drain visualization.

No new services. No new plugins. No hierarchy violations. The architecture composes.

---

*This document describes the design for dungeon mana absorption mechanics. For dungeon core architecture, see [DUNGEON.md](../plugins/DUNGEON.md). For the broader actor-bound entity pattern, see [ACTOR-BOUND-ENTITIES.md](ACTOR-BOUND-ENTITIES.md). For dungeon extensions including Workshop integration and floor systems, see [DUNGEON-EXTENSIONS-NOTES.md](DUNGEON-EXTENSIONS-NOTES.md). For vision context, see [VISION.md](../reference/VISION.md).*
