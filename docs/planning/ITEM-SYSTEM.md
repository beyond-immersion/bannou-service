# Path of Exile Item System Reference

> **Created**: 2026-01-22
> **Purpose**: Reference documentation for PoE's item system as a complexity benchmark
> **Scope**: Informing future lib-affix, lib-crafting, or similar plugins that build on lib-item/lib-inventory
> **Related**: INVENTORY_ITEM_ARCHITECTURE.md (foundation plugins)

---

## Executive Summary

Path of Exile has one of the most complex item systems in gaming, built over 10+ years of iteration. This document captures the system's architecture to ensure our foundation (lib-item + lib-inventory) can support this level of complexity, and to inform future plugins.

### Key Insight for Bannou

PoE's complexity lives **above** basic item/inventory management:
- **lib-item** handles: templates, instances, ownership, basic state
- **lib-inventory** handles: containers, placement, movement
- **Future plugins** handle: affixes, crafting, sockets, influences

Our foundation plugins should be **unopinionated** about what makes items complex - that's for higher-level plugins.

---

## Part 1: Item Base Types and Categories

### Category Hierarchy

PoE organizes items into a strict hierarchy:

```
Item Class (Equipment Category)
└── Item Category (Subclass)
    └── Base Type (Specific item)
        └── Item Instance (Actual drop)
```

**Bannou mapping:**
- Item Class / Category → `ItemTemplate.category` + `ItemTemplate.subcategory`
- Base Type → `ItemTemplate` (one template per base type)
- Item Instance → `ItemInstance`

### Equipment Classes

| Class | Categories | Socket Max | Notes |
|-------|-----------|------------|-------|
| **Weapons** | One-Hand, Two-Hand, Ranged | 3-6 | Determines attack skills usable |
| **Armour** | Body, Helmet, Gloves, Boots, Shield | 4-6 | Defence type based on base |
| **Accessories** | Amulet, Ring, Belt | 0 | No sockets (historically) |
| **Jewels** | Base, Abyss, Cluster | 0 | Socket into passive tree |
| **Flasks** | Life, Mana, Utility, Unique | 0 | Consumable with charges |

### Weapon Categories Detail

**One-Handed Melee:**
| Type | Attributes | Implicit |
|------|------------|----------|
| Claws | DEX/INT | Life/Mana on hit |
| Daggers | DEX/INT | Crit chance |
| Wands | INT | Spell damage |
| One-Hand Swords | STR/DEX | Accuracy |
| Thrusting Swords | DEX | Crit multiplier |
| One-Hand Axes | STR | None typically |
| One-Hand Maces | STR | Stun threshold |
| Sceptres | STR/INT | Elemental damage |
| Rune Daggers | DEX/INT | Spell damage |

**Two-Handed:** Swords, Axes, Maces, Staves, Warstaves

**Ranged:** Bows, Quivers (offhand)

### Armour Defence Types

Base types determine defences by attribute requirement:

| Attribute | Defence Type | Primary Stat |
|-----------|-------------|--------------|
| STR | Armour | Physical damage reduction |
| DEX | Evasion | Chance to evade attacks |
| INT | Energy Shield | Secondary health pool |
| STR/DEX | Armour/Evasion hybrid | Both, lower values |
| STR/INT | Armour/ES hybrid | Both, lower values |
| DEX/INT | Evasion/ES hybrid | Both, lower values |

### Base Type Progression Example (One-Hand Swords)

```
Rusted Sword        → Level 1,  iLvl 1
Copper Sword        → Level 5,  iLvl 5
Sabre               → Level 12, iLvl 12
Cutlass             → Level 22, iLvl 22
Elegant Sword       → Level 37, iLvl 37
Twilight Blade      → Level 51, iLvl 51
Eternal Sword       → Level 66, iLvl 66
Vaal Blade (Vaal)   → Level 64, iLvl 64
```

### Item Level vs Required Level

- **Item Level (iLvl)**: Determines which mods CAN roll (hidden stat)
- **Required Level**: Minimum character level to equip (shown)
- **Base Required Level**: From base type
- **Mod Required Level**: Highest requirement from affixes

```
Final Required Level = max(base_req, highest_affix_req)
```

**Bannou implication:** Need both `ItemTemplate.requiredLevel` and `ItemInstance.effectiveRequiredLevel` (computed from affixes).

### Implicit Modifiers

Built into the base type, generally cannot be changed:

| Base Type | Typical Implicit |
|-----------|-----------------|
| Ruby Ring | +20-30% Fire Resistance |
| Topaz Ring | +20-30% Lightning Resistance |
| Coral Ring | +20-30 Maximum Life |
| Diamond Ring | 20-30% increased Global Crit Chance |
| Elegant Sword | +45 Accuracy Rating |
| Vaal Axe | 20% chance to cause Bleeding |

**Bannou implication:** Implicits are template properties with roll ranges, instantiated on item creation.

---

## Part 2: Affix System (Modifiers)

### Prefix vs Suffix

Every explicit modifier is either a **prefix** or **suffix**:

| Aspect | Prefix | Suffix |
|--------|--------|--------|
| Max on Magic | 1 | 1 |
| Max on Rare | 3 | 3 |
| Total Max | 3 | 3 |
| Examples | +Life, +ES, %Phys Damage | +Resists, Attack Speed, Crit |

**Total: 6 affixes maximum on rare items (3 prefix + 3 suffix)**

### Affix Tier System

Each mod has multiple tiers based on item level:

**Example: "+# to Maximum Life" (Prefix)**

| Tier | Name | Values | iLvl Required | Weighting |
|------|------|--------|---------------|-----------|
| T1 | of the Godslayer | 110-119 | 86 | 200 |
| T2 | of the Titan | 100-109 | 82 | 250 |
| T3 | of the Leviathan | 90-99 | 74 | 400 |
| T4 | of the Colossus | 80-89 | 64 | 800 |
| T5 | of the Giant | 70-79 | 54 | 1000 |
| T6 | of the Gorilla | 60-69 | 44 | 1000 |
| T7 | of the Lion | 50-59 | 36 | 1000 |
| T8 | of the Bear | 40-49 | 30 | 1000 |

**Bannou implication:** Affixes are their own entity type with tier relationships.

### Affix Weighting

Mods have different spawn weights affecting probability:

```
Spawn Probability = (mod_weight) / (sum of all valid mod weights)
```

Weights typically range from:
- **Common mods**: 1000-1500
- **Uncommon mods**: 400-800
- **Rare mods**: 100-250
- **Very rare mods**: 25-50

### Mod Groups (Exclusivity)

Mods belong to **groups** - only one mod per group can appear:

```
Group: "IncreasedLife"
- Tier 1: +110-119 Life
- Tier 2: +100-109 Life
- ...etc

Group: "IncreasedLifeEssence" (Essence-only)
- Deafening Essence of Greed: +95-100 Life (doesn't block normal life)
```

**Key Rule**: Items cannot have two mods from the same group.

**Bannou implication:** Affix definitions need `modGroup` field for exclusivity validation.

### Hybrid Mods

Single affix that grants multiple stats (counts as ONE mod):

**Examples:**
- "Subterranean" prefix: +Life AND +Mana (one prefix slot)
- "of the Order" suffix: +Resists AND +Attributes (one suffix slot)

**Display note:** Game may show these as separate lines but they're one mod.

**Bannou implication:** Affixes can have multiple stat grants, not just one.

### Influence Types and Exclusive Mods

Influenced items can roll special mods unavailable otherwise:

| Influence | Source | Visual | Example Exclusive Mods |
|-----------|--------|--------|----------------------|
| **Shaper** | Shaper guardians | Starfield background | "Nearby enemies have -9% Fire Res" |
| **Elder** | Elder guardians | Tentacle background | "+1.5% Base Crit for Attacks" |
| **Crusader** | Sirus/Conquerors | White symbols | "Enemies Explode on Death" |
| **Redeemer** | Sirus/Conquerors | Blue symbols | "Tailwind on Crit" |
| **Hunter** | Sirus/Conquerors | Green symbols | "+1 to Socketed Gems" |
| **Warlord** | Sirus/Conquerors | Red symbols | "Fire Damage Leeched as Life" |

**Advanced:**
- **Elevated Mods**: Maven's Orb can upgrade influence mods to stronger versions
- **Double Influence**: Awakener's Orb combines two influences on one item

**Bannou implication:** Items need `influences: [string]` array, affixes need `requiredInfluence` field.

---

## Part 3: Item Rarity System

### Rarity Tiers

| Rarity | Color | Prefix Count | Suffix Count | Total Affixes |
|--------|-------|--------------|--------------|---------------|
| **Normal** | White | 0 | 0 | 0 |
| **Magic** | Blue | 0-1 | 0-1 | 1-2 |
| **Rare** | Yellow | 1-3 | 1-3 | 3-6* |
| **Unique** | Orange | Fixed | Fixed | Predetermined |

### Magic Item Naming

- Has a **prefix name** if it has a prefix (e.g., "Heated")
- Has a **suffix name** if it has a suffix (e.g., "of the Bear")
- Full name: "[Prefix] [Base Type] [Suffix]"
- Example: "Heated Coral Ring of the Bear"

### Rare Item Naming

- Always has a randomly generated **two-word name**
- Cannot tell affixes from name
- Name generation uses word lists (e.g., "Death" + "Spiral" = "Death Spiral")

### Unique Items

Completely predetermined stats:
- **Fixed mods**: Always the same (with possible value ranges)
- **Variable rolls**: Values within ranges (e.g., 40-60% increased damage)
- **Cannot be crafted** (mostly)
- May have unique mechanics unavailable elsewhere

**Bannou implication:** Unique items are separate template type with fixed mod definitions.

---

## Part 4: Crafting System

### Currency Items (Core)

| Currency | Effect | Use Case |
|----------|--------|----------|
| **Orb of Transmutation** | Normal → Magic (1-2 mods) | Early game |
| **Orb of Alteration** | Reroll Magic mods | Crafting specific 1-2 mod items |
| **Orb of Augmentation** | Add mod to Magic (if < 2) | Complete magic items |
| **Regal Orb** | Magic → Rare (adds 1 mod) | Transitioning good magic bases |
| **Orb of Alchemy** | Normal → Rare (4+ mods) | Quick rare generation |
| **Chaos Orb** | Reroll Rare completely | Gambling/currency standard |
| **Exalted Orb** | Add 1 mod to Rare (if < 6) | High-end crafting |
| **Orb of Annulment** | Remove 1 random mod | Risky mod removal |
| **Divine Orb** | Reroll mod VALUES (not mods) | Perfect existing items |
| **Mirror of Kalandra** | Duplicate item (mirrored) | Ultimate duplication |

### Bench Crafting

Crafting bench allows **deterministic** mod addition:

- Costs currency
- Adds a **crafted mod** (shown differently in UI)
- **Limit: 1 crafted mod** unless using multimod
- Can only add mods you've unlocked

**Bench Mod Examples:**
```
"+# to Maximum Life" (costs 4 Chaos equivalent)
"Can have up to 3 Crafted Modifiers" (MULTIMOD)
"Prefixes Cannot Be Changed" (META-CRAFT)
"Suffixes Cannot Be Changed" (META-CRAFT)
```

### Meta-Crafting

Special crafted mods that protect other mods:

1. **"Prefixes Cannot Be Changed"**: All prefixes survive rerolls
2. **"Suffixes Cannot Be Changed"**: All suffixes survive rerolls
3. **"Cannot Roll Attack Modifiers"**: Blocks attack mods
4. **"Cannot Roll Caster Modifiers"**: Blocks caster mods

### Fossil Crafting

Fossils modify the **weighting** of mod pools:

| Fossil | Effect |
|--------|--------|
| **Pristine** | More life mods, no defense mods |
| **Scorched** | More fire mods, no cold mods |
| **Frigid** | More cold mods, no fire mods |
| **Aberrant** | More chaos mods, no elemental mods |
| **Jagged** | More phys mods, no chaos mods |
| **Dense** | More ES mods, no life mods |

**Resonators** hold 1-4 fossils, combining effects.

**Fossil-Exclusive Mods**: Some mods ONLY appear with specific fossils.

### Essence Crafting

Essences **guarantee** a specific mod:

| Essence Tier | Result |
|--------------|--------|
| Whispering-Shrieking | Guarantees lower-tier mod |
| Deafening | Guarantees highest tier |

**Essence-Exclusive Mods**: Some essence mods are unavailable otherwise.

### Harvest Crafting

Targeted reforges with some determinism:

| Craft Type | Effect |
|------------|--------|
| **Reforge with X** | Guarantees at least one X mod |
| **Augment X** | Add X mod if open affix |
| **Remove X** | Remove a random X mod |
| **Remove X, Add Y** | Targeted swap |

### Veiled Mods

Items can drop with **veiled** mods:
- Shows "Veiled Prefix" or "Veiled Suffix"
- Must be unveiled at NPC
- Choose from 3 options
- Unlocks those mods for bench crafting

---

## Part 5: Special Item Mechanics

### Corrupted Items

**Vaal Orb** corrupts items:
- Item becomes **corrupted** (cannot be further modified)
- Possible outcomes:
  - No change
  - Reroll into random rare
  - Add/change implicit to Vaal implicit
  - Change socket colors/links/numbers
  - Turn into completely different unique

**Bannou implication:** `ItemInstance.isCorrupted: boolean` - prevents modifications.

### Mirrored Items

- Created by Mirror of Kalandra
- **Cannot be modified** in any way
- Exact duplicate of original
- Original can continue being crafted

**Bannou implication:** `ItemInstance.isMirrored: boolean` - prevents modifications.

### Fractured Items

- Have 1-3 **fractured mods** (shown with special icon)
- Fractured mods **cannot be changed**
- Other mods can be rerolled around them

**Bannou implication:** Per-affix `isFractured: boolean` flag.

### Synthesized Items

- Special **implicit modifiers** not found elsewhere
- Can have multiple implicits (up to 3)
- Examples: "+1 to Level of all Strength Skill Gems", "Onslaught on Kill"

**Bannou implication:** Support for multiple implicits on instances.

### Split Items

- Created by Beast crafting "Split item into two"
- Each copy gets random half of mods
- Item becomes **split** (cannot be split again)

**Bannou implication:** `ItemInstance.isSplit: boolean`

---

## Part 6: Sockets and Links

### Socket Basics

| Property | Rule |
|----------|------|
| **Colors** | Red (STR), Green (DEX), Blue (INT), White (any) |
| **Max Sockets** | Varies by item type and iLvl |
| **Links** | Adjacent sockets can be linked |
| **Skill Gems** | Go in sockets, links share support gems |

### Maximum Sockets by Item Type

| Item Type | Max Sockets | iLvl Required for Max |
|-----------|-------------|----------------------|
| Two-Hand Weapons | 6 | 50 |
| Body Armour | 6 | 50 |
| One-Hand Weapons | 3 | 2 |
| Shields | 3 | 2 |
| Helmets | 4 | 28 |
| Gloves | 4 | 28 |
| Boots | 4 | 28 |

### Socket Coloring

Colors weighted by **attribute requirements**:

```
STR base → more likely Red
DEX base → more likely Green
INT base → more likely Blue
Hybrid → weighted mix
```

### Socket Configuration Notation

Common notation: `R-R-G B-B G`
- Letters = colors (R/G/B/W)
- Dashes = links
- Spaces = unlinked

**Bannou implication:** `ItemInstance.socketConfig: string` or structured array.

---

## Part 7: Item Identification and Filtering

### Item Identification

- Items drop **unidentified** (mods hidden)
- Must use **Scroll of Wisdom** to reveal mods
- Unidentified items can be vendored for better returns

**Bannou implication:** `ItemInstance.isIdentified: boolean`

### Loot Filter System

PoE has powerful **client-side filter language**:

```
Show
    Class "Rings"
    Rarity Rare
    ItemLevel >= 75
    SetTextColor 255 255 0
    SetFontSize 40
    PlayAlertSound 2

Hide
    Class "Rings"
    Rarity Normal
    ItemLevel < 60
```

**Capabilities:**
- Show/Hide items
- Color coding (text, border, background)
- Sound alerts, minimap icons, light beams
- Based on: Class, Base Type, Rarity, iLvl, Sockets, Links, Influenced, etc.

**Bannou implication:** Item query API needs to support all these filter dimensions.

---

## Part 8: Backend Modeling Implications

### Proposed Schema Extensions for lib-affix

```yaml
# Would be a separate plugin building on lib-item
AffixDefinition:
  id: uuid
  code: string                    # "increased_life_t1"
  displayName: string             # "of the Godslayer"

  # Classification
  affixType: AffixType            # prefix, suffix
  modGroup: string                # "IncreasedLife" - exclusivity group
  tier: integer                   # 1-12+

  # Requirements
  requiredItemLevel: integer
  requiredCharacterLevel: integer
  requiredInfluence: string?      # "shaper", "elder", etc.

  # Spawn rules
  spawnWeight: integer            # Base weight (1000 typical)
  spawnTags: object               # {"default": 1000, "amulet": 0}
  validItemClasses: [string]      # ["body_armour", "helmet"]

  # Stat grants (supports hybrid mods)
  statGrants:
    - stat: string                # "maximum_life"
      minValue: integer
      maxValue: integer

  # Generation tags for filtering/fossils
  generationTags: [string]        # ["life", "defenses"]

  # Special flags
  isEssenceMod: boolean
  isVeiledMod: boolean
  isCraftedMod: boolean
  isElevated: boolean

AffixType:
  - prefix
  - suffix
```

### ItemInstance Extensions for Affixes

```yaml
# Extensions to lib-item's ItemInstance
ItemInstance:
  # ... base fields from lib-item ...

  # Rarity (affects affix limits)
  rarity: ItemRarity              # normal, magic, rare, unique
  uniqueDefinitionId: uuid?       # If unique

  # Item level (hidden, affects valid affixes)
  itemLevel: integer

  # Implicit (rolled from template)
  implicitValue: integer?
  synthesisImplicits: [object]?   # For synthesized items

  # Explicit affixes
  affixes:
    - affixDefinitionId: uuid
      rolledValues: [integer]     # Actual rolled values
      isFractured: boolean
      isCrafted: boolean

  # Sockets
  socketConfig: string            # "R-R-G B-B G"

  # Special states
  isCorrupted: boolean
  isMirrored: boolean
  isSplit: boolean
  isIdentified: boolean

  # Influences
  influences: [string]            # ["shaper", "hunter"]

  # Quality
  quality: integer                # 0-30

ItemRarity:
  - normal
  - magic
  - rare
  - unique
```

### Key Queries for Affix System

#### Generate Valid Affix Pool

```sql
SELECT ad.*
FROM affix_definitions ad
WHERE ad.required_item_level <= :item_level
  AND :item_class = ANY(ad.valid_item_classes)
  AND (ad.required_influence IS NULL
       OR ad.required_influence = ANY(:item_influences))
  AND ad.mod_group NOT IN (
      SELECT ad2.mod_group
      FROM affix_definitions ad2
      WHERE ad2.id = ANY(:existing_affix_ids)
  )
  AND ad.affix_type = :desired_type
ORDER BY ad.spawn_weight DESC;
```

#### Weighted Random Selection

```python
def select_random_affix(valid_affixes, fossil_weights=None):
    total_weight = 0
    weighted_pool = []

    for affix in valid_affixes:
        weight = affix.spawn_weight

        # Apply fossil modifiers
        if fossil_weights:
            for tag in affix.generation_tags:
                if tag in fossil_weights:
                    weight = int(weight * fossil_weights[tag])

        if weight > 0:
            total_weight += weight
            weighted_pool.append((affix, weight))

    roll = random.randint(1, total_weight)
    cumulative = 0
    for affix, weight in weighted_pool:
        cumulative += weight
        if roll <= cumulative:
            return affix
```

### Performance Considerations

#### Caching Strategy

```
Redis Cache:
├── affix_pool:{item_class}:{influence}:{ilvl} → Valid affixes (TTL: 1h)
├── base_types:{class} → Base type list (TTL: 24h)
├── unique_items:all → All uniques (TTL: 24h)
└── item:{id}:computed_stats → Computed item stats (invalidate on change)
```

#### Denormalization for Trade Searches

Create materialized view with flattened stats for efficient trade-site-style queries.

---

## Part 9: Future Plugin Architecture

Based on this analysis, the recommended plugin hierarchy:

```
lib-item (foundation)
├── Templates, instances, basic state
├── Quantity models, ownership
└── Foundation for all item types

lib-inventory (foundation)
├── Containers, placement, constraints
├── Movement, transfers
└── Equipment slots

lib-affix (builds on lib-item)
├── Affix definitions and tiers
├── Mod groups and exclusivity
├── Weighted random selection
├── Influence system
└── Events: affix.rolled, affix.removed, affix.fractured

lib-socket (builds on lib-item)
├── Socket configuration
├── Socket coloring (attribute-weighted)
├── Linking mechanics
├── Gem placement
└── Events: socket.colored, socket.linked, gem.socketed

lib-crafting (builds on lib-affix, lib-socket)
├── Currency effects (chaos, exalt, etc.)
├── Bench crafting recipes
├── Fossil/essence/harvest systems
├── Meta-crafting rules
├── Corruption mechanics
└── Events: item.crafted, item.corrupted, item.mirrored

lib-unique (builds on lib-item, lib-affix)
├── Unique item definitions
├── Drop restrictions
├── Special mechanics
└── Events: unique.dropped
```

### What lib-item/lib-inventory Must Support

For PoE-level complexity to be possible, our foundation must support:

| Feature | lib-item | lib-inventory |
|---------|----------|---------------|
| Extensible metadata | `customStats`, `metadata` blobs | - |
| Multiple implicits | `implicits: [object]` | - |
| Rarity tracking | `rarity` field or metadata | - |
| Item level | `itemLevel` field | - |
| Identified state | `isIdentified` flag | - |
| Corruption state | `isCorrupted` flag | - |
| Mirror state | `isMirrored` flag | - |
| Influence tags | `influences: [string]` | - |
| Quality | `quality: integer` | - |
| Socket config | `socketConfig` or metadata | - |
| Variable slots | - | `maxSlots` per container |
| Equipment slots | - | `isEquipmentSlot` flag |

**Good news:** Our current lib-item/lib-inventory design already supports all of these through the `customStats`, `metadata`, and flexible field options.

---

## Appendix A: Glossary

| Term | Definition |
|------|------------|
| **iLvl** | Item Level - hidden stat determining valid affixes |
| **Affix** | Explicit modifier (prefix or suffix) |
| **Implicit** | Built-in mod from base type |
| **Tier** | Affix strength level (T1 = best) |
| **Mod Group** | Exclusivity group (one mod per group) |
| **Influence** | Special item property enabling exclusive mods |
| **Fractured** | Mod locked in place, cannot be changed |
| **Crafted** | Mod added via bench, counts separately |
| **Corrupted** | Item cannot be modified further |
| **Mirrored** | Duplicated item, cannot be modified |

---

## Appendix B: Research Sources

- Path of Exile Wiki (poewiki.net)
- PoEDB (poedb.tw) - datamined affix information
- Craft of Exile (craftofexile.com) - crafting simulator
- Path of Exile Trade site API documentation
- GGG forum posts on item system design

---

*Document Status: REFERENCE COMPLETE*
*Last Updated: 2026-01-22*
*Purpose: Benchmark for future item system complexity*
