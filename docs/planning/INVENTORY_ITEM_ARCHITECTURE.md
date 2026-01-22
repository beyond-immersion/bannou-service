# Inventory and Item System Architecture Planning

> **Created**: 2026-01-22
> **Status**: RESEARCH COMPLETE - Ready for Review
> **Purpose**: Comprehensive architecture design for lib-inventory/lib-item
> **Related**: ECONOMY_CURRENCY_ARCHITECTURE.md (see lib-currency for economic context)

---

## Executive Summary

This document captures research and architectural decisions for Bannou's inventory and item systems. The goal is to create a **generic, game-agnostic** inventory service that can support multiple game types without being opinionated about what items are or how inventories are structured.

### Core Design Principle

Following the lib-scene pattern, we aim for **structural genericity with semantic flexibility**:
- The backend provides **structure** (containers, slots, items, quantities)
- Games provide **semantics** (what an item means, how it behaves)
- Metadata/annotations allow arbitrary game-specific data

---

## Part 1: Research Findings

### 1.1 Inventory Model Survey

Different games use fundamentally different inventory paradigms:

| Model | Games | Key Characteristics |
|-------|-------|---------------------|
| **Slot-based** | WoW, Diablo, Classic RPGs | Fixed number of slots, items occupy 1 slot each |
| **Grid-based** | Resident Evil 4, Tarkov, STALKER | 2D grid, items have width×height |
| **Weight-based** | Elder Scrolls, Fallout | Unlimited slots, weight limit per container |
| **Volumetric** | Space Engineers, Stationeers | 3D volume constraints, realistic physics |
| **Hybrid: Slot+Weight** | Many modern RPGs | Fixed slots AND weight limits |
| **Hybrid: Grid+Weight** | Escape from Tarkov | Grid positioning AND weight penalties |
| **Stack-only** | Minecraft, Terraria | Items are just template+quantity, no unique instances |

### 1.2 Item Identity Models

| Model | Description | Use Cases |
|-------|-------------|-----------|
| **Fungible** | Items are interchangeable (just template + quantity) | Crafting materials, currency-like items |
| **Unique Instance** | Every item has its own ID and can have unique state | Weapons with durability, enchanted gear |
| **Hybrid** | Some items fungible, others unique | Most games |

### 1.3 Key Architectural Questions

1. **Should we know what items ARE?**
   - Option A: Fully structured (category, stats, effects as typed fields)
   - Option B: Template ID + opaque JSON blob
   - Option C: Hybrid (minimal typed fields + extensible metadata)

2. **One plugin or multiple?**
   - Option A: Single lib-inventory handles everything
   - Option B: lib-item (templates/instances) + lib-inventory (containers)
   - Option C: lib-item + lib-inventory + lib-fluid (for continuous quantities)

3. **How to unify inventory models?**
   - Can slot/grid/weight/volume be abstracted into one system?
   - Or should containers declare their constraint type?

---

## Part 2: External Research (Web Search Results)

*Research completed 2026-01-22 via parallel web search agents.*

### 2.1 Game Inventory Systems

**Industry Survey Results:**

| Game | Inventory Model | Key Insight |
|------|-----------------|-------------|
| **World of Warcraft** | Slot-based, DB-driven | Normalized schema with template/instance pattern, DB2 compression for efficiency |
| **EVE Online** | Enterprise-scale | 60M+ queries/day, AMD EPYC 7742 (2TB RAM, 128 cores), Redis service discovery |
| **RuneScape** | 28-slot constraint | Intentionally limited - economic driver (bank trips, market activity) |
| **Minecraft** | Stack-based | 64 items/stack max, ender chest cross-dimension storage |
| **Skyrim/Fallout** | Weight-based | Base 300 lbs + stamina bonus, encumbrance penalties |
| **Diablo II/Tarkov** | Grid-based | Items have width×height, tetris-style placement, rotation support |
| **Factorio** | Hand-crafting discouraged | Personal inventory for logistics only, automation is the goal |

**Database Schema Patterns:**

```sql
-- Template Pattern (most games use this)
Items_Template (id, name, weight, max_durability, max_stack_size, attack_power)
Character_Inventory (id, character_id, template_id, quantity, current_durability, slot_position)

-- Hybrid Approach (stackable + unique)
Item_Stack (inventory_id, template_id, quantity)  -- For fungible items
Item_Instance (uuid, owner_id, template_id, durability, modifiers)  -- For unique items
```

**Key Design Recommendations:**
- **Stacking**: Batch multiple identical items into single database row with quantity
- **Unique IDs**: Assign UUID only when item needs individual tracking (trade, durability)
- **Containers**: Support nesting with recursive queries and depth limits
- **Performance**: Redis cache for active players, lazy-load nested contents

### 2.2 Fluid and Gas Systems

**Game Implementation Survey:**

| Game | Fluid Model | Storage Pattern | Physics Simulation |
|------|-------------|-----------------|-------------------|
| **Oxygen Not Included** | Packet-based | 1 packet/pipe tile | Basic pressure (density-based floating) |
| **Factorio** | Percentage equilibration | Connected tanks equalize | Pump pressure for flow direction |
| **Satisfactory** | Linear storage | 1.327 m³/meter pipe | None (flow rate limits only) |
| **Space Engineers** | Dual: bulk + bottles | Large tanks continuous, bottles discrete | Simple fill/consume |
| **Stationeers** | Molar physics | Pascals = (moles × temp) | Full PVT simulation with phase changes |
| **Minecraft** | Block state | Depth 0-7 per block | Flow spreading (7 blocks max) |

**Architectural Approaches:**

1. **Continuous Decimals** (Factorio, Satisfactory): Store float/decimal quantities
   - Pro: Simple model, direct physical representation
   - Con: Precision issues, harder to discretize

2. **Packet-Based** (Oxygen Not Included): Discrete units in pipes
   - Pro: Deterministic, prevents mixing
   - Con: Limited throughput, more overhead

3. **Molar/Physics** (Stationeers): Track moles + temperature → derive pressure
   - Pro: Rich simulation, phase transitions
   - Con: Computational overhead, steep learning curve

**Recommendation for Bannou:**
- **Short-term**: Continuous quantities as decimal in existing item system (simple)
- **Long-term**: Separate lib-fluid only if games need pressure/flow simulation
- **Bridge**: Barrel pattern (discrete items containing fluid) for transport to standard inventory

### 2.3 Item Data Modeling Best Practices

**Backend Knowledge Distribution:**

| Backend MUST Know | Backend Should NOT Compute |
|-------------------|---------------------------|
| Item identifiers & ownership | Display presentation (icons, colors) |
| Inventory counts (prevent cheating) | Animation states, visual effects |
| Restricted properties (damage, rarity) | Client-side physics |
| Audit trail (fraud detection) | Localized descriptions |

**Recommended Hybrid Schema:**

```json
{
  "baseProperties": {        // Typed, indexed, queryable
    "name": "string",
    "rarity": "enum",
    "weight": "decimal",
    "maxStackSize": "int",
    "isDeprecated": "boolean"
  },
  "customProperties": {      // JSON blob, validated but flexible
    "$schema": "uri",        // Reference to game-specific schema
    "stats": {...},
    "effects": {...}
  }
}
```

**Schema Evolution Best Practices:**

1. **Deprecation Phase**: Mark `deprecated: true`, keep code working
2. **Migration Window**: Serve both old/new formats (2-3 updates)
3. **Legacy Support**: Backend still returns deprecated if owned
4. **Removal**: Delete after 3-6 months notice, migrate to "Legacy Item" collection

**Query Optimization:**
- Always filter by `gameId` early (partition data)
- Composite indexes: `(ownerId, gameId, itemType)`, `(rarity, itemType, gameId)`
- Full-text index on `name` and `description`
- Redis Sorted Sets for hot item lookups and leaderboards

### 2.4 ECS Approaches

**ECS for Items: Core Principles**

Instead of deep class hierarchies (Weapon → Sword → EnchantedSword), compose items from components:

```
Common Item Components:
├── Stackable       (maxStack, currentCount)
├── Equippable      (slot type, bonuses)
├── Consumable      (effect, uses remaining)
├── Container       (can hold items, capacity)
├── Valuable        (gold worth, rarity)
├── Durable         (current/max durability)
├── Enchanted       (magic effects)
├── Physical        (weight, dimensions, material)
└── Temporal        (creation time, expiration)
```

**Framework Implementations:**

| Framework | Approach | Best For |
|-----------|----------|----------|
| **Unity DOTS** | Archetype-based, Job System | AAA performance (Overwatch 2, Fortnite physics) |
| **Bevy ECS** (Rust) | Sparse sets, relationship support | Indie, data-oriented design |
| **Flecs** (C/C++) | First-class relationships | Complex entity graphs |
| **Unreal** | Component-based (not pure ECS) | Traditional OOP with composition |

**Storage Architecture Comparison:**

| Characteristic | Archetype (Dense) | Sparse Set |
|----------------|-------------------|------------|
| Add/Remove Components | Expensive | Cheap (O(1)) |
| Iteration Performance | Excellent (cache-friendly) | Good |
| Memory Usage | Efficient | Can be wasteful |
| Backend Database | Pre-defined schemas | Dynamic tables |

**Backend ECS Pattern:**

```sql
-- Items table (entities)
CREATE TABLE Items (id UUID PRIMARY KEY, archetype_code VARCHAR(50));

-- Components stored separately (sparse)
CREATE TABLE ComponentData (
  item_id UUID,
  component_name VARCHAR(50),
  data JSON,
  UNIQUE(item_id, component_name)
);
CREATE INDEX idx_component_query ON ComponentData(component_name, item_id);
```

**Recommendation for Bannou**: Archetype approach fits better because:
- Items rarely change component composition after creation
- Inventory queries are frequent (UI updates)
- Server doesn't modify item structure at runtime

### 2.5 Commercial BaaS Platforms

**Platform Architecture Comparison:**

| Platform | Schema Model | Stacking | Custom Data | Limitation |
|----------|--------------|----------|-------------|------------|
| **PlayFab** | Catalog + DisplayProperties | Stack-based (StackId) | JSON blob | 10k items/inventory, properties only at creation |
| **Beamable** | Federated + InventoryUpdateBuilder | Instance-based | Dict<string,string> | String-only properties |
| **Unity Economy** | Resource-based (currencies + items) | Virtual containers | custom_data JSON | Schema-defined queries |
| **Nakama/Hiro** | Gameplay-first | Category-based limits | string + numeric props | Separate property types |
| **AccelByte** | Slot + Quantity | Multiple storage types | customAttributes + serverCustomAttributes | No composition support |

**Common Design Patterns:**

1. **Catalog-Driven**: All platforms require catalog definition before instantiation
2. **Stack Model**: Most use stack-based stacking to reduce database rows
3. **JSON Custom Data**: Universal pattern for game-specific properties
4. **Event Publishing**: All publish inventory change events

**Platform Limitations Discovered:**

| Platform | Critical Gap |
|----------|--------------|
| PlayFab | Properties can only be set on NEW stacks; update requires separate call |
| GameSparks | NO runtime creation - virtual goods must be pre-configured (legacy) |
| Beamable | String-only properties, no server-only layer |
| Nakama | Dual property types (string + numeric) create complexity |
| AccelByte | No composition/containers support |

**What NO Platform Supports Well:**
- True polymorphism (interface-based item hierarchies)
- Nested objects in properties
- Cross-game trading/inventory sharing
- ACID transactions across services
- Event ordering guarantees
- Automatic schema validation

**Opportunities for Bannou Differentiation:**
1. First-class polymorphism with item interfaces
2. Automatic schema validation with constraints
3. True cross-game inventory (account-level storage)
4. Composition support (items containing items)
5. Event ordering with distributed ledger option

---

## Part 3: Architectural Analysis

### 3.1 Following the lib-scene Pattern

lib-scene solved similar problems with hierarchical 3D data. Key patterns:

1. **Typed enums with escape hatches**: `NodeType` has `custom`, `SceneType` has `other`
2. **Annotations for consumer data**: `additionalProperties: true` JSON objects
3. **Tags for flexible filtering**: Array of strings, game defines meanings
4. **Validation rules per game**: Games register their own validation

**Application to Inventory:**
- Item categories can have `custom` type
- Item templates can have `metadata: object` for game-specific data
- Items can have `tags: [string]` for flexible filtering
- Games can register item validation rules

### 3.2 The Template/Instance Pattern

Already established in ECONOMY_CURRENCY_ARCHITECTURE. This is industry standard:

```
ItemTemplate (shared, cached)     ItemInstance (per-occurrence)
├── id: uuid                      ├── id: uuid
├── code: "iron_sword"            ├── templateId: uuid (→ template)
├── name: "Iron Sword"            ├── ownerId: uuid
├── category: weapon              ├── containerId: uuid
├── baseStats: {...}              ├── slotIndex: int
├── maxStackSize: 1               ├── quantity: int
├── weight: 2.5                   ├── durability: int
└── metadata: {...}               ├── customStats: {...}
                                  └── metadata: {...}
```

### 3.3 Container Abstraction

**Proposal**: Containers have a **constraint model** that defines how capacity works:

```yaml
ContainerConstraintModel:
  type: enum
    - slot_only        # maxSlots, items take 1 slot each
    - weight_only      # maxWeight, no slot limit
    - slot_and_weight  # both constraints apply
    - grid             # 2D grid with item dimensions
    - volumetric       # 3D volume (rare, complex)
    - unlimited        # no constraints (admin/debug)
```

**Container Schema:**
```yaml
Container:
  id: uuid
  ownerId: uuid
  ownerType: enum  # character, location, vehicle, chest...

  # Constraint configuration
  constraintModel: ContainerConstraintModel

  # Slot constraints (if applicable)
  maxSlots: integer?
  usedSlots: integer?

  # Weight constraints (if applicable)
  maxWeight: decimal?
  currentWeight: decimal?

  # Grid constraints (if applicable)
  gridWidth: integer?
  gridHeight: integer?

  # Volumetric constraints (if applicable)
  maxVolume: decimal?
  currentVolume: decimal?

  # Filtering
  allowedCategories: [string]?  # null = all allowed
  forbiddenCategories: [string]?
  tags: [string]

  metadata: object  # Game-specific data
```

### 3.4 Grid-Based Inventory Support

For grid-based inventories (Resident Evil 4, Tarkov), items need dimensions:

```yaml
# On ItemTemplate
gridWidth: integer?   # null for non-grid games
gridHeight: integer?
canRotate: boolean?   # Can item be rotated 90 degrees?

# On ItemInstance (in grid containers)
slotX: integer?       # Instead of slotIndex
slotY: integer?
rotated: boolean?
```

**Considerations:**
- Grid placement validation is complex (collision detection)
- Auto-placement algorithms needed (tetris-style fitting)
- Could be delegated to client with server validation

### 3.5 Fluid/Gas Handling

**Key Question**: Are fluids items, or a separate system?

**Analysis:**

| Approach | Pros | Cons |
|----------|------|------|
| **Fluids as items** | Single system, simpler | Awkward for continuous quantities |
| **Separate lib-fluid** | Clean separation, specialized | More complexity, integration burden |
| **Hybrid** | Containers can hold items OR fluid | Reasonable compromise |

**Recommendation**: Start with **fluids as stackable items with decimal quantities**.

```yaml
# Special handling for fluid items:
ItemTemplate:
  quantityType: enum
    - discrete    # Normal integer quantities (99 arrows)
    - continuous  # Decimal quantities (2.5 liters of water)

  # For continuous quantities
  unitOfMeasure: string?    # "liters", "kg", "m³"
  maxQuantityPerContainer: decimal?
```

**If games need true fluid simulation** (pipes, pressure, mixing), that's a separate runtime concern beyond inventory storage. The inventory just stores "this tank contains 50L of water".

### 3.6 Item Data: Typed vs Blob

**Recommendation**: Minimal typed fields + extensible metadata (Option C)

**Typed fields** (backend understands these for queries/validation):
- `code`: string (unique identifier)
- `name`: string (display name)
- `category`: enum with custom option
- `stackable`: boolean
- `maxStackSize`: integer
- `weight`: decimal (null if not applicable)
- `rarity`: enum (for common queries)
- `tags`: [string]

**Blob fields** (backend stores but doesn't interpret):
- `stats`: object (attack, defense, etc.)
- `effects`: object (on_use, on_equip, etc.)
- `display`: object (icon, model, color, etc.)
- `metadata`: object (anything else)

This allows:
- Backend can query by category, rarity, tags
- Games have full flexibility for stats/effects/behavior
- No schema changes needed when games add new item properties

### 3.7 Equipment Systems Analysis

Equipment (worn items) is a common pattern that intersects with inventory:

**Option A: Equipment as special containers**
```yaml
Container:
  containerType: equipment_slot
  slotName: "main_hand"  # helmet, chest, main_hand, off_hand, etc.
  maxSlots: 1            # Equipment slots hold one item
```
- Pro: No new concepts, reuses container system
- Con: Equipment slots aren't really "containers" conceptually

**Option B: Separate equipment tracking**
```yaml
EquipmentSlot:
  characterId: uuid
  slotName: string       # "main_hand", "helmet", etc.
  itemInstanceId: uuid?  # Currently equipped item
```
- Pro: Cleaner model, clear semantics
- Con: Duplicates container logic, more complexity

**Option C: Game-defined container types**
- Container type is just a string
- Games define what "equipment_helmet" means
- Backend doesn't care about semantics
- Pro: Maximum flexibility
- Con: No built-in equipment queries

**Recommendation**: Option C - let games define their own container types. The backend provides the mechanics; games provide the semantics. An equipment slot is just a container with `maxSlots: 1` and `containerType: "equipment_main_hand"`.

### 3.8 Nested Containers (Bags in Bags)

Many games support putting containers inside containers (bags in inventory):

**Depth considerations:**
- **Unlimited nesting**: Dangerous - could cause performance issues, infinite loops
- **No nesting**: Too restrictive - many games need at least one level (bag in inventory)
- **Fixed depth**: Reasonable default (e.g., 3 levels max)
- **Game-configurable**: Best - let each game set their own limit

**Proposed approach:**
```yaml
Container:
  # Nesting control
  canContainContainers: boolean    # Can this container hold other containers?
  maxNestingDepth: integer?        # null = inherit from parent or global default
  currentNestingDepth: integer     # Computed, for validation
```

**Weight propagation:**
When a bag is placed in an inventory, does the bag's weight count toward the inventory's limit?
- Option A: Containers have no weight themselves
- Option B: Container weight = own weight + contents weight
- Option C: Game-configurable per container type

**Recommendation**: Option B with game-configurable. By default, nested container weight includes contents.

### 3.9 Comparison with lib-save-load and lib-state

Both existing plugins handle generic data storage. Lessons learned:

**lib-save-load approach:**
- Data is base64-encoded bytes (`type: string, format: byte`)
- Completely opaque to the backend
- Tags and metadata for categorization
- Pro: Maximum flexibility
- Con: No querying on data content

**lib-state approach:**
- Data is JSON object (`type: object`)
- Supports JSON path queries
- Pro: Can search within data
- Con: Still mostly opaque

**lib-inventory should be in between:**
- Core fields are typed (for queries)
- Extension fields are JSON blobs (for flexibility)
- This matches the lib-scene pattern for `annotations` and `metadata`

### 3.10 Quantity Models Deep Dive

**Integer quantities (most games):**
- Simple: 1 iron ore, 99 arrows, 5 potions
- Natural for discrete items
- Stack limits as integers (maxStackSize: 99)

**Decimal quantities (survival/simulation):**
- 2.5 liters of water
- 0.75 kg of flour
- Required for realistic resource management

**Proposal: Unified quantity field with type flag:**
```yaml
ItemTemplate:
  quantityModel: enum
    - discrete        # Integer quantities (default)
    - continuous      # Decimal quantities
    - unique          # Always quantity = 1, non-stackable

ItemInstance:
  # For discrete/continuous
  quantity: decimal   # Use decimal for both, just display as int for discrete

  # Alternatively, separate fields:
  discreteQuantity: integer?    # For discrete items
  continuousQuantity: decimal?  # For continuous items
```

**Recommendation**: Single `quantity: decimal` field. Games display as integer when appropriate. Simpler schema, unified handling.

### 3.11 Item Instance Identity Patterns

When do items get their own instance ID vs. being pure stacks?

**Pattern A: Always instance (WoW, Diablo)**
- Every item in the game has a unique instance ID
- Even stackable items might have IDs (for durability tracking)
- Pro: Consistent model, full tracking
- Con: More database rows, more IDs to manage

**Pattern B: Template-only for fungible (Path of Exile currency)**
- Fungible items have no instance, just template + quantity
- Only items with state get instances
- Pro: Fewer records, simpler for basic items
- Con: Two different models to handle

**Pattern C: Lazy instantiation**
- Items start as template + quantity
- Instance created only when needed (modification, trade logging)
- Pro: Best of both worlds
- Con: Complex transitions, race conditions

**Recommendation**: Pattern A with optimization. Always use instances, but:
- Stackable items share most state (just track quantity)
- Template caching reduces overhead
- Consistent model simplifies all code paths

---

## Part 4: Proposed Architecture

### 4.1 Plugin Structure

**Recommendation**: Two plugins with clear responsibilities

| Plugin | Responsibility |
|--------|----------------|
| **lib-item** | Item templates, item instances, item state |
| **lib-inventory** | Containers, capacity management, item placement |

**Rationale**:
- Items can exist outside containers (on ground, in trade, in mail)
- Containers are a separate concept from items themselves
- Clear separation of concerns
- lib-market and lib-craft interact with lib-item for templates

**Alternative considered**: Single lib-inventory plugin
- Simpler, fewer cross-plugin dependencies
- But conflates two distinct concepts
- DECISION PENDING based on team discussion

### 4.2 Core Schemas

#### ItemTemplate Schema (Refined)
```yaml
ItemTemplate:
  id: uuid

  # Core identification (indexed, queryable)
  code: string              # "iron_sword", "health_potion" - unique within game
  gameId: string            # Game service this template belongs to
  name: string              # Display name
  description: string?

  # Classification (indexed, queryable)
  category: ItemCategory    # weapon, armor, consumable, material, container, etc.
  subcategory: string?      # "sword", "helmet" - game-defined
  tags: [string]            # Flexible filtering
  rarity: ItemRarity        # common, uncommon, rare, epic, legendary

  # Quantity behavior
  quantityModel: QuantityModel  # discrete, continuous, unique
  maxStackSize: integer         # 1 for unique, 99/999 for stackable
  unitOfMeasure: string?        # "liters", "kg" for continuous

  # Physical properties
  weight: decimal?          # For weight-based inventories
  volume: decimal?          # For volumetric inventories
  gridWidth: integer?       # For grid inventories
  gridHeight: integer?      # For grid inventories
  canRotate: boolean?       # Can be rotated in grid

  # Value and trading
  baseValue: decimal?       # Reference price for vendors/markets
  tradeable: boolean        # Can be traded/auctioned
  destroyable: boolean      # Can be destroyed/discarded

  # Binding
  soulboundType: SoulboundType  # none, on_pickup, on_equip, on_use

  # Durability (optional)
  hasDurability: boolean
  maxDurability: integer?

  # Realm scoping
  realmScope: RealmScope    # global, realm_specific
  availableRealms: [uuid]?  # If realm_specific

  # Game-specific data (opaque to backend)
  stats: object?            # { attack: 25, defense: 0 }
  effects: object?          # { on_use: "heal", amount: 50 }
  requirements: object?     # { level: 10, strength: 15 }
  display: object?          # { iconId: uuid, modelId: uuid }
  metadata: object?         # Anything else

  # Lifecycle
  isActive: boolean
  createdAt: timestamp
  updatedAt: timestamp

# Enums
ItemCategory:
  - weapon
  - armor
  - accessory
  - consumable
  - material
  - container
  - quest
  - currency_like  # Fungible items that act like currency
  - misc
  - custom        # Game-defined

QuantityModel:
  - discrete      # Integer quantities (arrows, potions)
  - continuous    # Decimal quantities (water, fuel)
  - unique        # Always 1, non-stackable

ItemRarity:
  - common
  - uncommon
  - rare
  - epic
  - legendary
  - custom

SoulboundType:
  - none
  - on_pickup
  - on_equip
  - on_use
```

#### ItemInstance Schema
```yaml
ItemInstance:
  id: uuid
  templateId: uuid

  # Ownership (polymorphic)
  ownerId: uuid
  ownerType: OwnerType      # character, container, ground, escrow, mail

  # Location in container
  containerId: uuid?
  slotIndex: integer?       # For slot-based
  slotX: integer?           # For grid-based
  slotY: integer?           # For grid-based
  rotated: boolean?         # For grid rotation

  # Realm
  realmId: uuid

  # Quantity (uses decimal for unified handling)
  quantity: decimal         # 1 for unique, 99 for stack, 2.5 for continuous

  # Instance state
  currentDurability: integer?
  boundToId: uuid?          # Character ID if soulbound
  boundAt: timestamp?

  # Modifications (game-specific)
  customStats: object?      # Enchantments, modifications
  customName: string?       # Player-renamed items
  instanceMetadata: object? # Any other instance-specific data

  # Audit trail
  originType: ItemOriginType  # loot, quest, craft, trade, spawn
  originId: uuid?             # Quest ID, creature ID, etc.
  createdAt: timestamp
  modifiedAt: timestamp
```

#### Container Schema
```yaml
Container:
  id: uuid

  # Ownership
  ownerId: uuid
  ownerType: ContainerOwnerType  # character, location, vehicle, chest, npc

  # Type and behavior
  containerType: string     # Game-defined: "inventory", "bank", "equipment_slot", etc.
  constraintModel: ContainerConstraintModel

  # Slot constraints
  maxSlots: integer?
  usedSlots: integer?       # Denormalized

  # Weight constraints
  maxWeight: decimal?
  currentWeight: decimal?   # Denormalized

  # Grid constraints
  gridWidth: integer?
  gridHeight: integer?

  # Volume constraints
  maxVolume: decimal?
  currentVolume: decimal?   # Denormalized

  # Nesting
  parentContainerId: uuid?  # If nested
  canContainContainers: boolean
  maxNestingDepth: integer?

  # Filtering
  allowedCategories: [string]?
  forbiddenCategories: [string]?
  allowedTags: [string]?

  # Realm
  realmId: uuid

  # Game-specific
  tags: [string]
  metadata: object?

  # Lifecycle
  createdAt: timestamp
  modifiedAt: timestamp

ContainerConstraintModel:
  - slot_only
  - weight_only
  - slot_and_weight
  - grid
  - volumetric
  - unlimited
```

### 4.3 API Design

Following Bannou's POST-only pattern:

```yaml
# ═══════════════════════════════════════════════════════════
# ITEM TEMPLATE MANAGEMENT (lib-item or lib-inventory)
# ═══════════════════════════════════════════════════════════

/item/template/create:
  access: developer
  request: { gameId, code, name, category, ... }
  response: { template }

/item/template/get:
  access: user
  request: { templateId | code, gameId }
  response: { template }

/item/template/list:
  access: user
  request: { gameId, category?, tags?, rarity?, search?, offset, limit }
  response: { templates[], totalCount }

/item/template/update:
  access: developer
  request: { templateId, ...changes }
  response: { template }

/item/template/deprecate:
  access: admin
  request: { templateId, migrationTargetId? }
  response: { deprecated: boolean }

# ═══════════════════════════════════════════════════════════
# CONTAINER MANAGEMENT
# ═══════════════════════════════════════════════════════════

/inventory/container/create:
  access: authenticated
  request: { ownerId, ownerType, containerType, constraintModel, ... }
  response: { container }

/inventory/container/get:
  access: user
  request: { containerId | { ownerId, ownerType, containerType } }
  response: { container, items[] }

/inventory/container/list:
  access: user
  request: { ownerId, ownerType }
  response: { containers[] }

/inventory/container/delete:
  access: admin
  request: { containerId, itemHandling: "destroy" | "transfer" | "error" }
  response: { deleted, itemsAffected }

# ═══════════════════════════════════════════════════════════
# ITEM INSTANCE OPERATIONS
# ═══════════════════════════════════════════════════════════

/inventory/add:
  access: authenticated
  description: Add items to a container (create instances or stack)
  request:
    containerId: uuid
    templateId: uuid
    quantity: decimal
    originType: ItemOriginType
    originId: uuid?
    customStats: object?
    slotIndex: integer?     # null for auto-placement
    slotX: integer?         # For grid containers
    slotY: integer?
  response: { instance, stacked: boolean, overflowQuantity: decimal? }
  errors: [ CONTAINER_FULL, WEIGHT_EXCEEDED, ITEM_NOT_ALLOWED ]

/inventory/remove:
  access: authenticated
  description: Remove items from container (reduce quantity or delete)
  request:
    instanceId: uuid
    quantity: decimal
    reason: RemovalReason   # consumed, destroyed, transferred, sold, dropped
  response: { removed, remainingQuantity }

/inventory/move:
  access: authenticated
  description: Move item to different slot or container
  request:
    instanceId: uuid
    targetContainerId: uuid
    targetSlotIndex: integer?
    targetSlotX: integer?
    targetSlotY: integer?
    rotated: boolean?
  response: { instance }
  errors: [ CONTAINER_FULL, SLOT_OCCUPIED, INCOMPATIBLE_CONTAINER ]

/inventory/transfer:
  access: authenticated
  description: Transfer item ownership (trade, mail, drop)
  request:
    instanceId: uuid
    quantity: decimal
    targetOwnerId: uuid
    targetOwnerType: OwnerType
    targetContainerId: uuid?  # null = auto-select or create
  response: { sourceInstance?, targetInstance }
  errors: [ NOT_TRADEABLE, SOULBOUND, TARGET_FULL ]

/inventory/split:
  access: user
  description: Split a stack into two stacks
  request:
    instanceId: uuid
    quantity: decimal       # Amount to split off
  response: { originalInstance, newInstance }

/inventory/merge:
  access: user
  description: Merge two stacks of same template
  request:
    sourceInstanceId: uuid
    targetInstanceId: uuid
  response: { instance, sourceDestroyed: boolean }

# ═══════════════════════════════════════════════════════════
# QUERIES
# ═══════════════════════════════════════════════════════════

/inventory/query:
  access: user
  description: Find items across containers
  request:
    ownerId: uuid
    ownerType: OwnerType
    templateId: uuid?
    category: string?
    tags: [string]?
    includeNested: boolean?   # Search nested containers
  response: { instances[] }

/inventory/count:
  access: user
  description: Count items of a template
  request:
    ownerId: uuid
    ownerType: OwnerType
    templateId: uuid
    includeNested: boolean?
  response: { totalQuantity }

/inventory/has:
  access: authenticated
  description: Check if entity has enough items (for crafting, quests)
  request:
    ownerId: uuid
    ownerType: OwnerType
    requirements: [{ templateId, quantity }]
  response: { hasAll: boolean, missing: [{ templateId, required, actual }] }

/inventory/find-space:
  access: user
  description: Check where an item would fit
  request:
    containerId: uuid
    templateId: uuid
    quantity: decimal
  response: { canFit: boolean, suggestedSlot: { index?, x?, y?, rotated? }? }

# ═══════════════════════════════════════════════════════════
# INSTANCE MODIFICATIONS
# ═══════════════════════════════════════════════════════════

/inventory/modify:
  access: authenticated
  description: Modify item instance state (durability, stats, name)
  request:
    instanceId: uuid
    modifications:
      durabilityDelta: integer?
      customStats: object?
      customName: string?
      metadata: object?
  response: { instance }

/inventory/bind:
  access: authenticated
  description: Bind an item to a character
  request:
    instanceId: uuid
    characterId: uuid
    bindType: SoulboundType
  response: { instance }
```

### 4.4 Events

```yaml
# Template events
item.template.created:
  templateId: uuid
  code: string
  gameId: string
  category: string

item.template.updated:
  templateId: uuid
  changes: object

item.template.deprecated:
  templateId: uuid
  migrationTargetId: uuid?

# Instance events
inventory.item.added:
  instanceId: uuid
  containerId: uuid
  templateId: uuid
  quantity: decimal
  originType: string
  originId: uuid?

inventory.item.removed:
  instanceId: uuid
  containerId: uuid
  templateId: uuid
  quantity: decimal
  reason: string
  remainingQuantity: decimal

inventory.item.moved:
  instanceId: uuid
  fromContainerId: uuid
  toContainerId: uuid
  fromSlot: { index?, x?, y? }
  toSlot: { index?, x?, y? }

inventory.item.transferred:
  instanceId: uuid
  fromOwnerId: uuid
  fromOwnerType: string
  toOwnerId: uuid
  toOwnerType: string
  quantity: decimal

inventory.item.stacked:
  sourceInstanceId: uuid
  targetInstanceId: uuid
  quantity: decimal
  sourceDestroyed: boolean

inventory.item.modified:
  instanceId: uuid
  changes: object

inventory.item.bound:
  instanceId: uuid
  characterId: uuid
  bindType: string

# Container events
inventory.container.created:
  containerId: uuid
  ownerId: uuid
  ownerType: string
  containerType: string

inventory.container.full:
  containerId: uuid
  ownerId: uuid

inventory.container.deleted:
  containerId: uuid
  itemsAffected: integer
```

---

## Part 5: Open Questions

### 5.1 Critical Decisions Needed

1. **One plugin or two?**
   - lib-item + lib-inventory (cleaner separation)
   - lib-inventory only (simpler)

2. **Grid inventory support priority?**
   - MVP: slot/weight only
   - Phase 2: add grid support
   - Or: grid from day one?

3. **Fluid handling?**
   - Continuous quantity items (simple)
   - Separate lib-fluid (complex, defer?)

4. **Equipment system?**
   - Part of lib-inventory?
   - Separate lib-equipment?
   - Game-specific (just use container types)?

5. **Nested containers (bags in bags)?**
   - Allow arbitrary nesting?
   - Fixed depth limit?
   - Game-configurable?

### 5.2 Edge Cases to Consider

- **Item stacking across containers**: Auto-merge when transferring?
- **Container within container limits**: Max nesting depth?
- **Item deletion cascade**: What happens to items when container deleted?
- **Ownership transfers**: Atomic transactions for trades?
- **Realm-crossing items**: Can items move between realms?
- **Version migration**: How to handle item template changes?

---

## Part 6: Implementation Considerations

### 6.1 Performance

- Item templates are read-heavy: aggressive caching
- Container queries are frequent: index by owner
- Grid placement validation: can be complex, consider client-side with server validation

### 6.2 Scalability

- Realm-partition item instances
- Global item templates (replicated)
- Redis cache for hot containers

### 6.3 Integration Points

- **lib-currency**: Items have value, market integration
- **lib-market**: Auctions, trading
- **lib-craft**: Recipes consume/produce items
- **lib-character**: Character inventories
- **lib-location**: Location-based containers (chests)

### 4.5 State Stores

```yaml
# ═══════════════════════════════════════════════════════════
# REDIS (Hot data, caching)
# ═══════════════════════════════════════════════════════════

item-template-cache:
  backend: redis
  prefix: "item:tpl"
  purpose: Template lookup cache (global, replicated)
  ttl: 1 hour (refreshed on access)

item-instance-cache:
  backend: redis
  prefix: "item:inst"
  purpose: Hot item instance data
  ttl: 15 minutes

container-cache:
  backend: redis
  prefix: "inv:cont"
  purpose: Container state + item list
  ttl: 5 minutes

container-lock:
  backend: redis
  prefix: "inv:lock"
  purpose: Optimistic locking for concurrent modifications
  ttl: 30 seconds

# ═══════════════════════════════════════════════════════════
# MYSQL (Persistence, queries)
# ═══════════════════════════════════════════════════════════

item-template-store:
  backend: mysql
  table: item_templates
  purpose: Item definitions (global)
  indexes:
    - code, gameId (unique)
    - category
    - tags (JSON)
    - rarity

item-instance-store:
  backend: mysql
  table: item_instances
  purpose: Item instances (realm-partitioned)
  partition: realmId
  indexes:
    - templateId
    - ownerId, ownerType
    - containerId
    - originType, originId

container-store:
  backend: mysql
  table: containers
  purpose: Container definitions
  indexes:
    - ownerId, ownerType
    - containerType
    - parentContainerId
```

### 4.6 Cross-Plugin Integration

```
┌─────────────────────────────────────────────────────────────┐
│                        lib-character                         │
│  (Character has inventory containers)                        │
└─────────────────┬───────────────────────────────────────────┘
                  │ creates containers for
                  ▼
┌─────────────────────────────────────────────────────────────┐
│                       lib-inventory                          │
│  (Containers, capacity, placement)                           │
└─────────────────┬───────────────────────────────────────────┘
                  │ manages instances of
                  ▼
┌─────────────────────────────────────────────────────────────┐
│                         lib-item                             │
│  (Templates, instances, item state)                          │
└─────────────────┬───────────────────────────────────────────┘
                  │
        ┌─────────┴─────────┬─────────────────┐
        ▼                   ▼                 ▼
┌───────────────┐   ┌───────────────┐   ┌───────────────┐
│  lib-market   │   │  lib-craft    │   │  lib-quest    │
│  (trading)    │   │  (recipes)    │   │  (rewards)    │
└───────────────┘   └───────────────┘   └───────────────┘
```

**Event flows:**

1. **Character created** → lib-character creates default containers
2. **Quest completed** → lib-quest calls `/inventory/add` with origin
3. **Item crafted** → lib-craft consumes inputs, creates outputs
4. **Item sold** → lib-market escrows item, lib-currency handles payment

---

## Part 5: Open Questions

### 5.1 Critical Decisions Needed

1. **One plugin or two?**
   - lib-item + lib-inventory (cleaner separation)
   - lib-inventory only (simpler)
   - **Leaning toward**: Single lib-inventory for MVP, split later if needed

2. **Grid inventory support priority?**
   - MVP: slot/weight only
   - Phase 2: add grid support
   - **Recommendation**: MVP slot/weight, grid in Phase 2

3. **Fluid handling?**
   - Continuous quantity items (simple)
   - Separate lib-fluid (complex, defer?)
   - **Recommendation**: Continuous quantities in lib-inventory, defer lib-fluid

4. **Equipment system?**
   - Part of lib-inventory (via container types)
   - **Recommendation**: Equipment = containers with maxSlots=1 and game-defined types

5. **Nested containers (bags in bags)?**
   - Game-configurable depth limit
   - **Recommendation**: Default max depth 3, configurable per container type

### 5.2 Edge Cases to Consider

| Edge Case | Proposed Handling |
|-----------|-------------------|
| Item stacking across containers | Auto-merge when capacity allows |
| Container within container limits | Configurable maxNestingDepth |
| Item deletion cascade | Soft-delete with recovery option |
| Ownership transfers (trades) | Atomic transaction via lib-market |
| Realm-crossing items | Requires explicit transfer API |
| Template version migration | Deprecated templates map to replacements |
| Concurrent modifications | Optimistic locking with ETags |
| Stack overflow | Return overflow quantity, don't fail |

---

## Part 6: Implementation Considerations

### 6.1 Performance

- **Item templates**: Read-heavy, cache aggressively (1h TTL)
- **Container queries**: Frequent, index by owner
- **Grid placement**: Complex, client-side with server validation
- **Bulk operations**: Batch API for multi-item operations

### 6.2 Scalability

- **Realm-partition** item instances
- **Global replicate** item templates
- **Redis cluster** for hot containers
- **Read replicas** for query-heavy workloads

### 6.3 Migration Strategy

1. **Phase 1: MVP** (parallel with lib-currency)
   - Slot-based and weight-based containers
   - Basic template/instance pattern
   - Core CRUD operations

2. **Phase 2: Grid Support**
   - Grid-based containers
   - Tetris-style placement
   - Rotation support

3. **Phase 3: Advanced Features**
   - Continuous quantities (fluids)
   - Equipment integration
   - Cross-realm transfers

---

## Appendix A: Research References

*Web search findings from research agents (2026-01-22):*

### Game Inventory Systems
- [Inventory Systems in Games - Out of Games](https://outof.games/news/6699-inventory-systems-in-games-lost-in-the-grid/)
- [Database Structure Guide - OwnedCore](https://www.ownedcore.com/forums/world-of-warcraft/world-of-warcraft-emulator-servers/wow-emu-guides-tutorials/59936-database-structure-guide.html/)
- [DB2 - wowdev](https://wowdev.wiki/DB2)
- [EVE Online Architecture - High Scalability](https://highscalability.com/eve-online-architecture/)
- [A History of EVE Database Server Hardware](https://www.eveonline.com/news/view/a-history-of-eve-database-server-hardware)
- [RuneScape Inventory Wiki](https://runescape.fandom.com/wiki/Inventory)
- [Skyrim Carry Weight - UESP Wiki](https://en.uesp.net/wiki/Skyrim_talk:Carry_Weight)

### Fluid and Gas Systems
- [Oxygen Not Included Wiki - Fluid Mechanics](https://oxygennotincluded.wiki.gg/wiki/Fluid_Mechanics)
- [Factorio Wiki - Fluid System](https://wiki.factorio.com/Fluid_system)
- [Factorio Blog - Fluids 2.0](https://factorio.com/blog/post/fff-416)
- [Satisfactory Wiki - Pipelines](https://satisfactory.wiki.gg/wiki/Pipelines)
- [Space Engineers Wiki - Hydrogen Tank](https://spaceengineers.wiki.gg/wiki/Hydrogen_Tank)
- [Stationeers Wiki - Pressure/Volume/Temperature](https://stationeers-wiki.com/Pressure,_Volume,_Quantity,_and_Temperature)
- [Minecraft Wiki - Fluid](https://minecraft.wiki/w/Fluid)

### Item Data Modeling
- [Steam Inventory Schema Documentation](https://partner.steamgames.com/doc/features/inventory/schema)
- [PlayFab Catalog Overview](https://learn.microsoft.com/en-us/gaming/playfab/economy-monetization/economy-v2/catalog/catalog-overview)
- [Beamable Managed Inventory Service](https://beamable.com/blog/introducing-beamables-managed-inventory-service)
- [Evolveum Schema Deprecation](https://docs.evolveum.com/midpoint/devel/design/schema-cleanup-4.8/deprecated-items/)
- [Zalando RESTful API Guidelines](https://opensource.zalando.com/restful-api-guidelines/)

### ECS Approaches
- [Entity Component System - Wikipedia](https://en.wikipedia.org/wiki/Entity_component_system)
- [ECS FAQ - SanderMertens/ecs-faq](https://github.com/SanderMertens/ecs-faq)
- [Unity DOTS/ECS](https://unity.com/ecs)
- [Bevy ECS Getting Started](https://bevy.org/learn/quick-start/getting-started/ecs/)
- [Flecs Inventory System Example](https://github.com/SanderMertens/flecs)
- [Building an ECS - Storage in Pictures](https://ajmmertens.medium.com/building-an-ecs-storage-in-pictures-642b8bfd6e04)
- [Archetypal vs Sparse Set ECS Performance](https://diglib.eg.org/bitstreams/766b72a4-70ae-4e8e-935b-949d589ed962/download)

### Commercial BaaS Platforms
- [PlayFab Inventory APIs](https://learn.microsoft.com/en-us/gaming/playfab/economy-monetization/economy-v2/inventory/)
- [PlayFab Inventory Stacks](https://learn.microsoft.com/en-us/gaming/playfab/features/economy-v2/inventory/stacks)
- [Beamable Inventory Documentation](https://docs.beamable.com/docs/inventory-feature-overview)
- [Unity Gaming Services Economy](https://docs.unity.com/ugs/en-us/manual/economy/manual)
- [Nakama/Hiro Inventory System](https://heroiclabs.com/docs/hiro/concepts/inventory/)
- [AccelByte Inventory Management](https://docs.accelbyte.io/gaming-services/services/storage/inventory/)
- [Cross-Platform Game Development - AccelByte](https://accelbyte.io/blog/cross-platform-game-development-demystified)

---

## Appendix B: Comparison Matrix

### Inventory Model Comparison

| Feature | Slot-Only | Weight-Only | Slot+Weight | Grid | Volumetric |
|---------|-----------|-------------|-------------|------|------------|
| Implementation complexity | Low | Low | Medium | High | Very High |
| Player intuition | High | Medium | Medium | High | Low |
| Strategic depth | Low | Medium | Medium | High | Medium |
| Suitable games | Casual RPGs | Survival | Action RPGs | Horror/Tactical | Sim/Engineering |
| Backend cost | Low | Low | Low | Medium | High |
| Client complexity | Low | Low | Low | High | Very High |

### Data Model Comparison

| Approach | Flexibility | Query Support | Schema Evolution | Complexity |
|----------|-------------|---------------|------------------|------------|
| Fully typed | Low | Excellent | Hard (migrations) | Low |
| Opaque blob | Maximum | None | Easy (client handles) | Low |
| Hybrid (recommended) | High | Good (typed fields) | Medium | Medium |

### Commercial Platform Comparison

| Platform | Item Schema | Inventory Model | Extensibility | Pricing Model |
|----------|-------------|-----------------|---------------|---------------|
| PlayFab | Catalog-based | Slot-only | Limited JSON | Per-call |
| Unity Economy | Flexible JSON | Virtual containers | High | Per-MAU |
| Nakama | Key-value storage | Game-defined | Maximum | Self-hosted |
| **Bannou (proposed)** | Hybrid | Multi-model | High | Self-hosted |

---

## Appendix C: Game-Specific Examples

### Example 1: Traditional MMO (WoW-like)

```yaml
# Template
iron_sword:
  code: "iron_sword"
  category: weapon
  subcategory: sword
  rarity: common
  stackable: false
  maxStackSize: 1
  weight: 3.5
  baseValue: 25
  stats: { attack: 15, speed: 1.2 }
  requirements: { level: 5, strength: 10 }

# Container setup
character_inventory:
  containerType: "inventory"
  constraintModel: slot_and_weight
  maxSlots: 20
  maxWeight: 100

character_bank:
  containerType: "bank"
  constraintModel: slot_only
  maxSlots: 100

equipment_main_hand:
  containerType: "equipment_slot"
  constraintModel: slot_only
  maxSlots: 1
  allowedCategories: ["weapon"]
```

### Example 2: Survival Game (Rust-like)

```yaml
# Template
water:
  code: "water"
  category: consumable
  quantityModel: continuous
  unitOfMeasure: "ml"
  maxStackSize: 1000
  weight: 0.001  # per ml
  effects: { on_use: "hydrate", amount_per_ml: 0.5 }

# Container
water_bottle:
  containerType: "fluid_container"
  constraintModel: volumetric
  maxVolume: 500
  allowedCategories: ["consumable"]
  allowedTags: ["liquid"]
```

### Example 3: Tactical Shooter (Tarkov-like)

```yaml
# Template
rifle_magazine:
  code: "ak_30rd_mag"
  category: equipment
  subcategory: magazine
  gridWidth: 1
  gridHeight: 2
  canRotate: true
  weight: 0.35
  metadata:
    compatibleWeapons: ["ak74", "ak103"]
    ammoCapacity: 30

# Container
player_backpack:
  containerType: "backpack"
  constraintModel: grid
  gridWidth: 6
  gridHeight: 8
  maxWeight: 25
```

---

*Document Status: RESEARCH COMPLETE - Ready for review and decision-making*
*Last Updated: 2026-01-22*
*Research Sources: 5 parallel web search agents covering game systems, fluid mechanics, data modeling, ECS patterns, and commercial BaaS platforms*
