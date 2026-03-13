# Information Economy: Knowledge as Commodity in Living Worlds

> **Type**: Design
> **Status**: Aspirational
> **Created**: 2026-03-13
> **Last Updated**: 2026-03-13
> **North Stars**: #1, #2, #5
> **Related Plugins**: Lexicon, Hearsay, Disposition, Collection, Chat, Currency, Escrow, Contract, Trade, Market, Item, Inventory, Mapping, Actor, Seed, Transit
> **Prerequisites**: Lexicon, Hearsay, and Disposition services (all aspirational); Trade and Market services (aspirational)

## Summary

Describes how information becomes a first-class economic commodity through the composition of existing Bannou primitives, requiring zero new plugins. Reified as physical "itemized contract" items that inject Hearsay beliefs on consumption, information follows the same physical logistics as any tradeable good -- it must be discovered, recorded, transported, and can be stolen, forged, or destroyed. The discovery tier model from Lexicon creates natural expertise differentials that GOAP can price, causing NPC party composition to include non-combat specialists (cartographers, zoologists, archaeologists) when the economic math favors hiring experts over self-development. Markets for information emerge organically when characters discover valuable knowledge and other characters develop drives to acquire it.

---

## Problem Statement

Traditional game economies trade physical goods: weapons, armor, potions, raw materials. Information -- what things are, where things are, how things work -- is either free (available in a wiki) or gated behind quest progress (talk to NPC X to "learn" Y). Neither model produces interesting economic dynamics because information has no scarcity, no logistics, and no variable quality.

Arcadia's epistemic pipeline (Lexicon, Collection, Hearsay, Disposition) already models information with properties that physical goods have: scarcity (discovery tiers), fidelity (Hearsay confidence levels), perishability (belief decay), and variable quality (expertise-gated interpretation). What is missing is the **economic reasoning bridge** -- the mechanism by which NPCs treat knowledge acquisition and sale as a first-class economic activity rather than a free social byproduct of trust relationships.

This matters for three reasons:

1. **Party composition becomes interesting.** If GOAP can price information, parties include non-combat specialists when the economic math works: "This dungeon has inscriptions worth 500 gold if we can read them. A historian can read them. Hiring one costs 200 gold. Net profit: 300 gold." The classic "all damage dealers" party emerges only when combat loot exceeds information value -- not as the default.

2. **Markets emerge from discovery.** A realm has no concept of necromancy until a necromancer exists. The necromancer demonstrates power. Hearsay propagates. Disposition drives shift. NPCs develop interest in necromantic knowledge. Demand creates a market. Scarcity drives expeditions. No game designer authored this market -- it emerged from one character discovering something.

3. **The content flywheel gains a knowledge dimension.** Dead characters' maps, research journals, and expertise become valuable artifacts. A historian NPC seeks "the maps of Theron the Explorer" not because a designer wrote that quest, but because their `master_cartography` drive assigns high value to the compressed knowledge in those mementos.

---

## The Epistemic Pipeline

Four services form a complete knowledge lifecycle. Information flows from ground truth through discovery, transmission, and motivation:

```
GROUND TRUTH              DISCOVERY GATE           BELIEF LAYER              MOTIVATION
Lexicon (L4)              Collection (L2)          Hearsay (L4)              Disposition (L4)
what things ARE            what you've SEEN         what you've HEARD         what you WANT to know

  entries                    discoveryLevel           beliefs with              drives with
  traits                     per character            confidence &              urgency =
  categories                 per entry                distortion                intensity *
  associations                                                                  (1 - satisfaction) *
  strategies                                          6 channels:               (1 + frustration)
                                                      DirectObservation
          gated by                                    OfficialDecree            explore, master_craft,
          Collection ──────►  unlocks new             TrustedContact            gain_wealth, curiosity
          discovery level     Lexicon tiers            SocialContact
                                                      Rumor                     unsatisfied drives
                              Collection ──events──►  CulturalOsmosis           increase GOAP priority
                              advancement                                       for knowledge-seeking
                              triggers tier                                     actions
                              visibility             beliefs feed into
                                                     Disposition synthesis
                                                     (hearsay weight: 0.25)
```

The cycle: Lexicon defines what CAN be known. Collection tracks what each character HAS discovered. Hearsay transmits knowledge between characters with fidelity loss. Disposition converts knowledge gaps into motivational drives. Drives motivate information-seeking behavior. Information-seeking creates new Collection advancement. The cycle repeats.

### Discovery Expertise Differentials

From the CRYPTIC-TRAILS design, Lexicon associations have discovery tiers that gate visibility:

```
effective_tier = base_tier - floor(seed_domain_growth / threshold)
```

A master historian (skill offset -2) sees tier-4 associations at only tier 2 of evidence. An unskilled character needs ALL the evidence. This creates natural **expertise value** -- the gap between what a specialist and novice can derive from the same evidence is the basis for information having economic value.

---

## Proposed Solution: Itemized Contract Items

### The Core Mechanism

Information is reified as physical items -- scrolls, maps, research journals, field guides, intelligence reports. These are **itemized contract items**: Item instances with embedded Contract rules that fire on consumption.

```
ITEMIZED CONTRACT ITEM
======================
Item Template:  "Research Scroll" (category: information, quantity model: unique)
Item Instance:  specific scroll with specific content
Contract:       prebound callback on consumption
                 action: RecordBelief (Hearsay API)
                 payload: structured beliefs in Lexicon vocabulary
Inspection:     Lexicon-language description of contents
                 (gated by inspector's Collection discovery level)
```

When a character **consumes** the item:
1. The Contract prebound callback fires
2. Hearsay's `RecordBelief` is called for the consumer
3. Beliefs are injected at the `TrustedContact` channel (confidence 0.5-0.7)
4. The item is consumed (removed from inventory)

When a character **inspects** the item (before purchase):
1. The Lexicon-language content description is returned
2. **Gated by the inspector's Collection discovery level** -- a master sees full detail, a novice sees only broad categories
3. This IS the CRYPTIC-TRAILS appraisal dynamic -- expertise determines how well you can evaluate what you're buying

### Why Physical Items

Grounding information in physical form follows Arcadia's thermodynamic principles and creates rich emergent dynamics:

| Property | Physical Information | Implication |
|----------|---------------------|-------------|
| **Scarcity** | Each scroll is a unique item instance | Cannot be infinitely duplicated for free |
| **Logistics** | Must be carried in inventory, transported via Transit | Geography matters -- remote discoveries are harder to monetize |
| **Risk** | Can be stolen, destroyed, lost in a dungeon | Information acquisition is genuinely dangerous |
| **Forgery** | A deceptive NPC writes false beliefs into a scroll | Trust and reputation matter |
| **Copying** | A scribe reads and rewrites at reduced fidelity (lower confidence beliefs, fewer details) | Mass-produced copies are cheaper but less accurate |
| **Decay** | Beliefs injected by consumption still undergo Hearsay confidence decay | Old information becomes stale -- ongoing demand for fresh intelligence |
| **Inspection asymmetry** | Expert sees full content, novice sees vague summary | Creates secondary market for appraisal services |

### Itemized Contracts as a Fundamental Category

This pattern extends beyond information. An itemized contract item is any Item instance whose consumption triggers a Contract prebound callback:

| Item Type | Consumption Effect | Contract Callback |
|-----------|-------------------|-------------------|
| **Information scroll** | Injects Hearsay beliefs | `RecordBelief` |
| **Recipe book** | Grants Collection entry (recipe unlock) | `GrantEntry` |
| **Skill manual** | Advances Seed growth in a domain | `ContributeGrowth` |
| **Enchanted potion** | Applies Status effect | Status API |
| **Deed of ownership** | Transfers location/property ownership | Location/Permission API |
| **Teaching certificate** | Unlocks License board node | License API |

The information economy rides for free on the general "consumable contract item" pattern. Trade, Market, and Escrow handle these items identically to any other tradeable good.

---

## Information Specialist Roles

With discovery expertise differentials and physical information items, specialist roles emerge from GOAP economic reasoning -- not from game designer authorship.

### How GOAP Reasons About Information Value

An NPC planning a dungeon expedition encounters a decision:

```
GOAP Action: "Hire historian for expedition"
  Preconditions: historian available, can afford fee
  Effects: +inscription_reading_capability, -200 gold

GOAP Action: "Develop history skill myself"
  Preconditions: Seed growth opportunity exists
  Effects: +inscription_reading_capability (after weeks of game-time)
  Cost: time investment, opportunity cost of not earning during training

GOAP Action: "Ignore inscriptions, focus on combat loot"
  Preconditions: none
  Effects: miss inscription value, +combat loot only
```

When inscription value (500 gold) exceeds historian fee (200 gold), GOAP selects "hire historian." The party now includes a non-combat specialist because the math works, not because a designer scripted it.

### Emergent Specialist Roles

Each role is a composition of existing primitives -- a Seed growth profile, Collection discovery focus, and ABML behavior templates:

| Role | Seed Domain | Collection Type | Economic Activity |
|------|-------------|-----------------|-------------------|
| **Cartographer** | `discovery.cartography` | Geographic surveys | Produces map scrolls from Mapping data; sells route information via Transit discovery |
| **Zoologist** | `discovery.fauna` | Bestiary entries | Produces creature field guides; identifies weaknesses (Lexicon strategies) for combat parties |
| **Archaeologist** | `discovery.history` | Historical artifacts | Reads inscriptions at higher tiers; identifies valuable artifacts that others miss |
| **Herbalist** | `discovery.flora` | Plant compendium | Identifies medicinal/alchemical plants; produces ingredient guides for crafters |
| **Appraiser** | `discovery.artifacts` | Item analysis | Reveals hidden item affixes at higher tiers; commands fees for pre-purchase evaluation |
| **Scout** | `discovery.terrain` | Terrain surveys | Maps dungeon layouts; identifies environmental hazards and safe routes |
| **Linguist** | `discovery.languages` | Script translations | Reads ancient texts; translates between cultural Lexicon vocabularies |
| **Spy/Intelligence Broker** | `discovery.social` | Character dossiers | Compiles Hearsay about specific individuals; sells character intelligence |

Every role uses the same mechanism: Seed growth provides the skill offset that lowers effective discovery tiers, Collection tracks what has been discovered, and the specialist produces itemized contract items containing their findings.

---

## Market Creation From Discovery

Markets for information emerge organically through the Hearsay-Disposition feedback loop:

```
1. CHARACTER DISCOVERS SOMETHING
   Collection advancement in a domain
   e.g., a necromancer discovers how to raise the dead

2. DEMONSTRATION OF VALUE
   Character uses knowledge visibly
   Events flow to Character History, Realm History

3. HEARSAY PROPAGATION
   Nearby NPCs: high-confidence DirectObservation beliefs
   Medium distance: TrustedContact beliefs (reduced detail)
   Far away: Rumor beliefs (distorted, vague)
   Eventually: CulturalOsmosis (everyone vaguely knows)

4. DISPOSITION SHIFT
   NPCs with relevant drives (gain_power, master_craft, curiosity)
   develop interest based on personality + hearsay + encounters
   Drive urgency increases: intensity * (1 - satisfaction) * (1 + frustration)

5. DEMAND EMERGES
   Multiple NPCs now WANT this knowledge
   GOAP assigns economic value to knowledge acquisition
   Willingness-to-pay scales with drive urgency

6. SUPPLY RESPONDS
   Characters with the knowledge produce information items
   Characters with partial knowledge offer cheaper, lower-fidelity items
   Teaching sessions (Contract-governed Chat interactions) are offered

7. MARKET DISCOVERS PRICE
   Market service finds clearing price based on supply/demand
   Price reflects: discovery difficulty, information fidelity, current demand

8. SECONDARY EFFECTS
   Scarcity drives expeditions to discover more
   High prices attract competitors and copiers
   God-actors (Hermes) may orchestrate trade routes for knowledge
   Factions may impose information-sharing or hoarding norms
   Monopolists form knowledge guilds to control supply
```

No game designer authors this progression. It emerges from the interaction of Hearsay propagation, Disposition drives, and GOAP economic planning.

### The Necromancer Scenario

A concrete example of organic market creation:

1. A character discovers necromancy (Collection: arcane lore advancement)
2. They raise the dead publicly (events to Realm History)
3. Hearsay: NPCs near the event believe "necromancy exists and works" (high confidence). Distant NPCs believe "someone up north is doing something with the dead" (low confidence, distorted)
4. Disposition: NPCs with high `gain_power` drives develop `master_necromancy` as an aspirational drive. NPCs with high `protect_community` drives develop fear-based opposition
5. NPCs wanting necromantic knowledge seek information items about it. None exist yet -- the discoverer is a monopolist
6. The necromancer (or associates) can produce necromantic instruction scrolls and sell them at monopoly prices
7. As more necromancers emerge, they need specific material resources -- creating demand in adjacent markets
8. Counter-movements emerge: anti-necromancy factions develop norms against it, creating information suppression dynamics
9. The tension between information freedom and information control becomes a driver of factional politics

The realm's concept of necromancy didn't exist until a necromancer made it real. The market for necromantic knowledge didn't exist until demand emerged from Disposition drives. The political conflict around necromancy didn't exist until factions formed positions based on Hearsay-influenced Disposition. All emergent.

---

## Party Composition Economics

The most visible gameplay consequence: adventure parties include non-combat specialists when the economic math favors it.

### The Value Calculation

For any expedition, GOAP evaluates the marginal economic contribution of each potential party member:

```
Party slot value = expected_income_from_specialist - specialist_fee

Historian:  500 gold (inscription value) - 200 gold (fee) = +300 gold
Fighter:    300 gold (combat loot share) - 150 gold (fee) = +150 gold
Cartographer: 400 gold (map sales) - 250 gold (fee) = +150 gold
Herbalist:  200 gold (plant identification) - 100 gold (fee) = +100 gold
```

A five-person party choosing between a fifth fighter (+150 gold) and a historian (+300 gold) selects the historian. The party composition: 3 fighters, 1 healer, 1 historian. Not because the game said "bring a historian" but because the GOAP planner computed that inscriptions are worth more than additional combat power.

### Dynamic Rebalancing

Party composition shifts based on world state:

- **Known dungeon with inscriptions**: historian is valuable
- **Unexplored wilderness**: cartographer is valuable
- **Monster-infested region**: zoologist is valuable (creature weakness strategies)
- **Ancient ruins**: archaeologist is valuable
- **Safe trade route**: all specialists, minimal combat
- **War zone**: all combat, specialists too risky to bring
- **Post-battle aftermath**: scavengers, appraisers, herbalists (different value profile)

The "correct" party composition is never static. It varies by destination, world state, and what information is currently scarce or valuable.

### Escort Dynamics

When high-value gathering (information, resources, or both) exceeds combat loot in expected value, the party structure inverts: combat specialists are *escorts* for gatherers, not the other way around. This mirrors real-world expedition economics -- soldiers guarding archaeologists, mercenaries protecting trade caravans. The transition from "fighters with a healer" to "specialists with bodyguards" happens naturally as NPCs discover that information and resources in a region are more valuable than what you can kill there.

---

## The Content Flywheel's Knowledge Dimension

The standard content flywheel: play -> history -> compression -> narrative -> new play.

Information economics adds a parallel loop:

```
KNOWLEDGE FLYWHEEL
==================
Discovery ──► Knowledge items produced ──► Items enter economy
                                                    │
                                                    ▼
                                           Trade / Sale / Theft
                                                    │
                                                    ▼
                                           New characters learn
                                                    │
                                                    ▼
                                           New drives emerge
                                           (master this, explore that)
                                                    │
                                                    ▼
                                           New expeditions launched
                                           (to discover more)
                                                    │
                                                    ▼
                                           More discovery ──loops back──►
```

### Dead Characters as Knowledge Sources

When a character dies, their compressed archive (via lib-resource) includes their Collection state -- everything they discovered. This knowledge manifests as:

- **Memento items** at locations they explored (per MEMENTO-INVENTORIES design)
- **Physical journals/maps** in their inventory (lootable by whoever finds the body)
- **Hearsay legacy** -- NPCs who knew them retain beliefs about what they knew
- **Quest seeds** -- god-actors compose "recover the lost maps of X" quests from archive data

A dead cartographer's maps become artifacts. A deceased alchemist's recipe journal is a treasure. The information someone accumulated over a lifetime doesn't vanish -- it enters the economy as physical items and social memory, creating new opportunities for those who find and use it.

---

## Service Composition

### Implemented Services (available today)

| Service | Role in Information Economy |
|---------|----------------------------|
| **Collection (L2)** | Discovery ledger -- tracks what each character has found, at what tier |
| **Item (L2)** | Physical form of information -- scroll templates, map templates, journal templates |
| **Inventory (L2)** | Container for information items -- personal inventory, library shelves |
| **Currency (L2)** | Payment for information -- wallets, transfers, holds |
| **Contract (L1)** | Binding agreements for information exchange -- milestones, consent, prebound callbacks |
| **Escrow (L4)** | Atomic exchange -- gold for information item, multi-party custody |
| **Seed (L2)** | Expertise tracking -- discovery domain growth provides skill offsets |
| **Chat (L1)** | Communication channel for teaching sessions (Contract-governed rooms) |
| **Mapping (L4)** | Cartographic data -- spatial authority channels, affordance queries |
| **Transit (L2)** | Route discovery -- geographic connectivity revealed by explorers |
| **Character Encounter (L4)** | Memory of information exchanges -- who taught you, who you taught |
| **Character History (L4)** | Backstory entries from information-related events |
| **Realm History (L4)** | Realm-level records of major discoveries |
| **Actor (L2)** | GOAP planning -- economic reasoning about information value |
| **Faction (L4)** | Information-sharing norms -- guild rules about knowledge monopolies |
| **Obligation (L4)** | GOAP cost for violating information norms -- NDAs, guild secrecy oaths |
| **Storyline (L4)** | Narrative generation from knowledge-related archives |

### Aspirational Services (required for full vision)

| Service | Role in Information Economy |
|---------|----------------------------|
| **Lexicon (L4)** | Knowledge ontology -- defines what CAN be known, with discovery tiers on traits/associations/strategies |
| **Hearsay (L4)** | Belief propagation -- how information travels between characters with fidelity loss |
| **Disposition (L4)** | Motivation engine -- drives that create demand for information (curiosity, mastery, wealth) |
| **Trade (L4)** | Information logistics -- moving knowledge items across distances over game-time |
| **Market (L4)** | Price discovery -- clearing prices for information based on supply and demand |

### Missing Variable Providers

Two implemented services need ABML variable providers for NPCs to reason about their knowledge state:

| Service | Planned Provider | Purpose |
|---------|-----------------|---------|
| **Collection** | `${collection.*}` | "What have I discovered? What discovery level am I at? What's left to find?" |
| **Mapping** | `${mapping.*}` | "What regions have I mapped? What spatial data do I have authority over?" |

Three aspirational services have designed providers that are critical:

| Service | Planned Provider | Purpose |
|---------|-----------------|---------|
| **Lexicon** | `${lexicon.ENTRY.*}` | "What do I know about this entity? What strategies work against it?" |
| **Hearsay** | `${hearsay.*}` | "What do I believe? How confident am I? What have I heard?" |
| **Disposition** | `${disposition.*}` | "What drives me? How urgently do I want this knowledge?" |

---

## ABML Behavior Templates Needed

The economic reasoning happens in ABML behavior content, not service code. These templates would be authored as seed data:

### Information Producer Templates

```yaml
# Cartographer: produces map scrolls from exploration
goals:
  - name: produce_valuable_maps
    priority: "${disposition.drive.gain_wealth.urgency} * 0.8"
    conditions:
      - "${seed.discovery.cartography.level} > 2"

actions:
  - name: survey_region
    preconditions:
      - "${mapping.authority.current} != null"
      - "${inventory.has_space} == true"
    effects:
      mapping_data_collected: true
    cost: "${transit.travel_time_to_unexplored} + 10"

  - name: produce_map_scroll
    preconditions:
      - mapping_data_collected: true
    effects:
      has_map_item: true
    # Contract prebound: creates itemized contract item with Hearsay payload
```

### Information Consumer Templates

```yaml
# Knowledge seeker: evaluates buy-vs-develop decision
goals:
  - name: acquire_knowledge
    priority: "${disposition.drive.master_craft.urgency}"

actions:
  - name: buy_information_item
    preconditions:
      - "${currency.balance.gold} >= item_price"
      - information_item_available: true
    effects:
      knowledge_acquired: true
    cost: "item_price"

  - name: hire_specialist
    preconditions:
      - specialist_available: true
      - "${currency.balance.gold} >= specialist_fee"
    effects:
      knowledge_acquired: true
    cost: "specialist_fee"

  - name: develop_skill_myself
    preconditions:
      - "${seed.discovery.relevant_domain.level} < target_level"
    effects:
      knowledge_acquired: true
    cost: "estimated_training_time * opportunity_cost_per_hour"
```

These templates are illustrative, not prescriptive -- the actual ABML syntax will follow the ABML guide conventions.

---

## Open Questions

### A. Information Pricing Model

How does an NPC determine the price of information? Unlike physical goods, information has high discovery cost but zero marginal reproduction cost. Options:

1. **Discovery cost**: price reflects time, danger, and Seed investment to obtain the knowledge
2. **Scarcity premium**: price increases when fewer NPCs possess the knowledge (requires some awareness of how many others know)
3. **Demand-driven**: price scales with how many NPCs have active drives for this knowledge
4. **Utility-based**: price reflects how much value the knowledge provides (savings, unlocked opportunities)
5. **All four, personality-weighted**: different NPCs price differently based on their economic reasoning style

The most likely answer is (5) -- a greedy NPC prices by scarcity, a generous one prices by cost, an opportunist prices by demand. This requires no special infrastructure; personality traits already modulate GOAP action costs.

### B. Teaching Sessions vs. Item Exchange

Two modes of information transfer exist:

1. **Item exchange**: physical scroll changes hands (atomic, verifiable, one-time)
2. **Teaching session**: Contract-governed Chat room interaction (richer, multi-step, ongoing)

Teaching sessions are higher-bandwidth (CHARACTER-COMMUNICATION describes 10 structured beliefs per conversation for a master teacher) but require both parties to be co-located and spend game-time. Items are portable but lower-bandwidth. Both should coexist -- the choice is economic (time vs. convenience vs. fidelity).

### C. Information Wants to Be Free

Once one NPC learns something and shares freely, the information's scarcity value collapses. Several natural constraints prevent instant information commoditization:

- **Hearsay fidelity loss**: rumor-channel information is low-confidence, unreliable
- **Tier gating**: only experts transmit high-tier knowledge; novice-to-novice caps at tier 2
- **Trust networks**: NPCs share freely only within Disposition trust thresholds
- **Belief decay**: Hearsay background worker decays unreinforced beliefs, requiring periodic refresh
- **Physical scarcity**: scrolls are unique items that don't self-replicate
- **Copying degradation**: scribes produce copies at reduced fidelity based on their own skill

Together, these create natural information scarcity gradients that prevent instant price collapse without artificial gates.

### D. Hearsay Saturation as Market Signal

When Hearsay saturation reaches a threshold at a location (enough NPCs believe the same claim), it could serve as a market signal that reduces information value there. "Everyone in this town already knows about the wolves" means wolf intelligence has no value at this location -- but might still be valuable in the next town over. This creates geographic arbitrage for information traders.

### E. God-Actor Information Manipulation

Divine actors (Regional Watchers) could manipulate information markets:
- **Hermes** orchestrates trade routes for knowledge items between settlements
- **Athena** values and promotes scholarly pursuits, increasing information demand
- **A trickster god** injects misinformation through forged scrolls or manipulated Hearsay
- **A war god** might suppress information about enemy weaknesses to prolong conflict

These are authored ABML behaviors, not service features.

---

## Related Documents

- **[CRYPTIC-TRAILS](CRYPTIC-TRAILS.md)** -- Discovery tier model, skill-gated hypothesis generation, investigation template types, and the appraisal economy that this document generalizes
- **[CHARACTER-COMMUNICATION](../guides/CHARACTER-COMMUNICATION.md)** -- Lexicon-shaped social interaction, teaching mechanics, the social variable provider, and structured NPC communication that serves as the information transmission layer
- **[MEMENTO-INVENTORIES](MEMENTO-INVENTORIES.md)** -- Location-bound memory items that represent stored knowledge; the physical manifestation of information in the game world
- **[CULTURAL-EMERGENCE](CULTURAL-EMERGENCE.md)** -- How divine actors crystallize emergent material conditions (including knowledge patterns) into cultural identity and customs
- **[Economy System Guide](../guides/ECONOMY-SYSTEM.md)** -- Foundation economic architecture that information items plug into
- **Lexicon Deep Dive** (`docs/plugins/LEXICON.md`) -- Full ontology design, discovery tiers, trait inheritance, strategy links
- **Hearsay Deep Dive** (`docs/plugins/HEARSAY.md`) -- Belief propagation model, six channels, confidence mechanics, distortion
- **Disposition Deep Dive** (`docs/plugins/DISPOSITION.md`) -- Drive system, feeling synthesis, urgency formula, motivation engine
- **Collection Deep Dive** (`docs/plugins/COLLECTION.md`) -- Content unlock tracking, discovery levels, DI listener pattern

### GitHub Issues

- **#454** -- Lexicon room type for NPC communication
- **#427** -- Economy Layer: Market, Trade & Taxation Services
- **#616** -- Disposition: Scale strategy for 100K+ NPC feelings/drives
