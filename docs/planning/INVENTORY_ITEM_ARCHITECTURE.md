# Inventory and Item System Architecture Planning

> **Created**: 2026-01-22
> **Audited**: 2026-01-22 (TENETS compliance verified)
> **Status**: ARCHITECTURE COMPLETE - Ready for Implementation
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

**DECISION**: Option A/C hybrid - equipment slots ARE containers with special flags.

#### 3.7.1 Equipment as Containers Pattern

```yaml
Container:
  constraintModel: slot_only
  maxSlots: 1                      # Equipment slots hold exactly one item
  containerType: "equipment_slot"  # Game-defined type
  isEquipmentSlot: boolean         # Flag for future lib-equipment to use
  equipmentSlotName: string?       # "main_hand", "helmet", "ring_left", etc.
```

**Benefits of this approach:**

1. **Unified model**: Equipping/unequipping = moving items between containers
2. **Event reuse**: `inventory-item.moved` covers equip/unequip actions
3. **Constraint reuse**: Equipment slots can use filtering (`allowedCategories: ["weapon"]`)
4. **Future-proof**: `isEquipmentSlot` flag allows future lib-equipment to build on top

**Example equipment setup:**

```yaml
# Character's equipment slots (created when character is created)
main_hand_slot:
  ownerId: {characterId}
  ownerType: character
  containerType: "equipment_slot"
  constraintModel: slot_only
  maxSlots: 1
  isEquipmentSlot: true
  equipmentSlotName: "main_hand"
  allowedCategories: ["weapon"]

helmet_slot:
  ownerId: {characterId}
  ownerType: character
  containerType: "equipment_slot"
  constraintModel: slot_only
  maxSlots: 1
  isEquipmentSlot: true
  equipmentSlotName: "helmet"
  allowedCategories: ["armor"]
  allowedTags: ["head_slot"]
```

**Equipping flow:**
1. Client calls `/inventory/move` with `targetContainerId: helmet_slot.id`
2. Backend validates item against slot constraints (category, tags)
3. On success, publishes `inventory-item.moved` event
4. Game logic interprets this as "equipped" because target is equipment slot

**Future lib-equipment integration:**
- lib-equipment can query containers where `isEquipmentSlot: true`
- Can add equipment-specific events (`equipment.item.equipped`, `equipment.item.unequipped`)
- Can manage stat calculations, set bonuses, etc.
- lib-inventory provides the mechanical foundation

### 3.8 Nested Containers (Bags in Bags)

Many games support putting containers inside containers (bags in inventory):

**Depth considerations:**
- **Unlimited nesting**: Dangerous - could cause performance issues, infinite loops
- **No nesting**: Too restrictive - many games need at least one level (bag in inventory)
- **Fixed depth**: Reasonable default (e.g., 3 levels max)
- **Game-configurable**: Best - let each game set their own limit

**DECISION**: Game-configurable depth with default max of 3.

#### 3.8.1 Constraint Propagation Analysis

**Key insight**: Different constraint types propagate differently through the hierarchy.

| Constraint Type | Propagates? | Rationale |
|-----------------|-------------|-----------|
| **Weight** | YES | Physics: mass of contents adds to container mass |
| **Volume** | NO | Child uses a footprint in parent; internal volume is separate |
| **Slots** | NO | Child occupies slots in parent; child's slots are internal |
| **Grid** | NO | Child has footprint in parent grid; internal grid is separate |

**Weight is the ONLY truly transitive physical property.**

#### 3.8.2 Weight Contribution Model

Not all containers contribute weight the same way. Magic items, dimensional bags, etc. may have special rules:

```yaml
WeightContribution:
  - none              # Parent ignores entirely (bag of holding, magical storage)
  - self_only         # Only container's empty weight counts (cargo hold with anti-grav)
  - self_plus_contents # Container + all recursive contents (DEFAULT - realistic)
```

#### 3.8.3 Container Nesting Schema

```yaml
Container:
  # Parent relationship
  parentContainerId: uuid?        # Immediate parent (null = root container)
  nestingDepth: integer           # 0 = root, computed on placement

  # Nesting permissions
  canContainContainers: boolean   # Can this container hold other containers?
  maxNestingDepth: integer?       # null = inherit from global default (3)

  # Physical properties
  selfWeight: decimal             # Empty container weight
  weightContribution: WeightContribution  # How this container's weight propagates

  # Footprint (how this container appears in parent)
  slotCost: integer               # Slots used in slot-based parent (default 1)
  gridWidth: integer?             # Space in grid-based parent
  gridHeight: integer?
  volume: decimal?                # Space in volumetric parent

  # Cached totals (denormalized for performance)
  contentsWeight: decimal         # Weight of all direct contents
  totalWeight: decimal            # selfWeight + contentsWeight (recursive)
```

#### 3.8.4 Validation Walk Algorithm

When adding an item to a nested container, validation must walk up the hierarchy:

```csharp
async Task<ValidationResult> ValidateAddItem(Guid containerId, ItemInstance item)
{
    var container = await _containerStore.GetAsync(containerId);
    var template = await _templateCache.GetAsync(item.TemplateId);

    // Step 1: Check immediate container constraints
    var immediateResult = ValidateImmediateConstraints(container, template, item.Quantity);
    if (!immediateResult.Success) return immediateResult;

    // Step 2: Walk up for weight propagation only
    var itemWeight = template.Weight * item.Quantity;
    var current = container;

    while (current.ParentContainerId.HasValue)
    {
        var parent = await _containerStore.GetAsync(current.ParentContainerId.Value);

        // Check if this container contributes weight
        if (current.WeightContribution == WeightContribution.SelfPlusContents)
        {
            // Check parent's weight limit
            if (parent.MaxWeight.HasValue)
            {
                var newParentWeight = parent.TotalWeight + itemWeight;
                if (newParentWeight > parent.MaxWeight)
                {
                    return ValidationResult.Fail(
                        $"Adding item would exceed weight limit in ancestor container {parent.Id}");
                }
            }
        }
        else if (current.WeightContribution == WeightContribution.None)
        {
            // Magical container: stop propagation entirely
            break;
        }
        // WeightContribution.SelfOnly: item weight doesn't propagate, continue checking

        current = parent;
    }

    return ValidationResult.Success();
}
```

#### 3.8.5 Weight Recalculation

After any add/remove/move operation, update cached weights:

```csharp
async Task RecalculateWeightsUpward(Container container)
{
    var current = container;
    while (current != null)
    {
        // Recalculate contents weight from direct children
        current.ContentsWeight = await CalculateDirectContentsWeight(current.Id);
        current.TotalWeight = current.SelfWeight + current.ContentsWeight;

        await _containerStore.SaveAsync(current.Id, current);

        // Walk up if this container contributes to parent
        if (current.ParentContainerId.HasValue &&
            current.WeightContribution == WeightContribution.SelfPlusContents)
        {
            current = await _containerStore.GetAsync(current.ParentContainerId.Value);
        }
        else
        {
            break;
        }
    }
}

async Task<decimal> CalculateDirectContentsWeight(Guid containerId)
{
    var itemsWeight = await _instanceStore.SumWeightByContainer(containerId);
    var childContainers = await _containerStore.GetByParent(containerId);

    var containerWeight = childContainers
        .Where(c => c.WeightContribution == WeightContribution.SelfPlusContents)
        .Sum(c => c.TotalWeight)
        + childContainers
        .Where(c => c.WeightContribution == WeightContribution.SelfOnly)
        .Sum(c => c.SelfWeight);
    // WeightContribution.None containers contribute 0

    return itemsWeight + containerWeight;
}
```

#### 3.8.6 Cross-Constraint Scenarios

**Scenario: Fluid container in slot-based inventory**
- Fluid container uses `slotCost: 1` in parent
- Parent's slots don't care about fluid container's internal volume
- Weight propagates based on `weightContribution`

**Scenario: Backpack in grid-based inventory (Tarkov)**
- Backpack has `gridWidth: 2, gridHeight: 3` footprint in parent
- Backpack's internal grid (`gridWidth: 4, gridHeight: 4`) is separate
- Weight propagates normally

**Scenario: Bag of Holding (magical)**
- `weightContribution: none` - parent sees no weight
- Parent only sees the bag's footprint (`slotCost: 1`)
- Items inside don't affect parent's weight limit

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

**DECISION**: Two plugins with clear responsibilities

| Plugin | Responsibility |
|--------|----------------|
| **lib-item** | Item templates, item instances, item identity, durability, enchantments, binding |
| **lib-inventory** | Containers, capacity management, item placement, movement, transfers, constraint validation |

**Rationale**:
- Items can exist outside containers (on ground, in trade, in mail)
- Containers are a separate concept from items themselves
- Clear separation of concerns
- Items have their own rich lifecycle (created, modified, bound, destroyed)
- lib-market and lib-craft interact with lib-item for templates/instances

#### 4.1.1 lib-item Responsibilities

- **Template CRUD**: Create, read, update, deprecate item definitions
- **Instance lifecycle**: Create instances, track modifications, handle destruction
- **Item state**: Durability, enchantments, custom stats, binding status
- **Item queries**: Find by template, by owner, by properties
- **Item events**: Template changes, instance state changes

#### 4.1.2 lib-inventory Responsibilities

- **Container CRUD**: Create, configure, delete containers
- **Spatial management**: Slot allocation, grid placement, weight tracking
- **Constraint validation**: Check capacity, allowed categories, nesting depth
- **Movement operations**: Add, remove, move, transfer items between containers
- **Container queries**: Find containers by owner, check available space
- **Container events**: Placement changes, capacity alerts

### 4.2 Core Schemas

**IMPORTANT - TENETS Compliance:**
- All schema properties MUST have `description` fields (T1)
- Use `x-permissions` array format with valid roles: anonymous, user, developer, admin (T13)
- Schemas MUST include NRT compliance note in `info.description`
- Event topics use kebab-case for multi-word entities: `item-template.created` not `item.template.created` (T5)
- Lifecycle events MUST use `x-lifecycle` pattern, never manual definition (T5)
- All schemas must specify `servers: [{ url: http://localhost:5012 }]` (T1)

#### ItemTemplate Schema (Refined)
```yaml
# Full schema will be in schemas/item-api.yaml
# This shows the model structure - actual schema requires descriptions on all properties

ItemTemplate:
  type: object
  required: [templateId, code, gameId, name, category, quantityModel, maxStackSize, scope]
  properties:
    templateId:
      type: string
      format: uuid
      description: Unique identifier for the item template

    # Core identification (indexed, queryable)
    code:
      type: string
      description: Unique code within the game (e.g., "iron_sword", "health_potion")
    gameId:
      type: string
      description: Game service this template belongs to
    name:
      type: string
      description: Human-readable display name
    description:
      type: string
      nullable: true
      description: Optional detailed description

    # Classification (indexed, queryable)
    category:
      $ref: '#/components/schemas/ItemCategory'
    subcategory:
      type: string
      nullable: true
      description: Game-defined subcategory (e.g., "sword", "helmet")
    tags:
      type: array
      items:
        type: string
      description: Flexible filtering tags
    rarity:
      $ref: '#/components/schemas/ItemRarity'

    # Quantity behavior
    quantityModel:
      $ref: '#/components/schemas/QuantityModel'
    maxStackSize:
      type: integer
      description: Maximum stack size (1 for unique items)
    unitOfMeasure:
      type: string
      nullable: true
      description: Unit for continuous quantities (e.g., "liters", "kg")

    # Physical properties (follows lib-currency precision pattern)
    weightPrecision:
      $ref: '#/components/schemas/WeightPrecision'
    weight:
      type: number
      format: double
      nullable: true
      description: Weight value (interpreted per weightPrecision)
    volume:
      type: number
      format: double
      nullable: true
      description: Volume for volumetric inventories
    gridWidth:
      type: integer
      nullable: true
      description: Width in grid-based inventories
    gridHeight:
      type: integer
      nullable: true
      description: Height in grid-based inventories
    canRotate:
      type: boolean
      nullable: true
      description: Whether item can be rotated in grid

    # Value and trading
    baseValue:
      type: number
      format: double
      nullable: true
      description: Reference price for vendors/markets
    tradeable:
      type: boolean
      description: Whether item can be traded/auctioned
    destroyable:
      type: boolean
      description: Whether item can be destroyed/discarded

    # Binding
    soulboundType:
      $ref: '#/components/schemas/SoulboundType'

    # Durability (optional)
    hasDurability:
      type: boolean
      description: Whether item has durability tracking
    maxDurability:
      type: integer
      nullable: true
      description: Maximum durability value

    # Scope (follows lib-currency pattern)
    scope:
      $ref: '#/components/schemas/ItemScope'
    availableRealms:
      type: array
      items:
        type: string
        format: uuid
      nullable: true
      description: Realms where this template is available (if scope is realm_specific or multi_realm)

    # Game-specific data (opaque to backend)
    stats:
      type: object
      nullable: true
      description: Game-defined stats (e.g., attack, defense)
    effects:
      type: object
      nullable: true
      description: Game-defined effects (e.g., on_use, on_equip)
    requirements:
      type: object
      nullable: true
      description: Game-defined requirements (e.g., level, strength)
    display:
      type: object
      nullable: true
      description: Display properties (e.g., iconId, modelId)
    metadata:
      type: object
      nullable: true
      description: Any other game-specific data

    # Lifecycle
    isActive:
      type: boolean
      description: Whether template is currently active
    createdAt:
      type: string
      format: date-time
      description: Template creation timestamp
    updatedAt:
      type: string
      format: date-time
      description: Last update timestamp

# Enums
ItemCategory:
  type: string
  enum:
    - weapon
    - armor
    - accessory
    - consumable
    - material
    - container
    - quest
    - currency_like  # Fungible items that act like currency
    - misc
    - custom         # Game-defined
  description: Item classification category

QuantityModel:
  type: string
  enum:
    - discrete      # Integer quantities (arrows, potions)
    - continuous    # Decimal quantities (water, fuel)
    - unique        # Always 1, non-stackable
  description: How quantities are tracked for this item type

ItemRarity:
  type: string
  enum:
    - common
    - uncommon
    - rare
    - epic
    - legendary
    - custom
  description: Item rarity tier

SoulboundType:
  type: string
  enum:
    - none
    - on_pickup
    - on_equip
    - on_use
  description: When item becomes bound to a character

ItemScope:
  type: string
  enum:
    - global          # Available in all realms
    - realm_specific  # Available only in specific realms
    - multi_realm     # Available in a subset of realms
  description: Realm availability scope (consistent with lib-currency)

WeightPrecision:
  type: string
  enum:
    - integer     # Whole units only
    - decimal_1   # One decimal place
    - decimal_2   # Two decimal places (recommended default)
    - decimal_3   # Three decimal places
  description: Precision for weight values (consistent with lib-currency)
```

#### ItemInstance Schema
```yaml
ItemInstance:
  type: object
  required: [instanceId, templateId, containerId, realmId, quantity, originType, createdAt]
  properties:
    instanceId:
      type: string
      format: uuid
      description: Unique identifier for this item instance
    templateId:
      type: string
      format: uuid
      description: Reference to the item template

    # Location - items ALWAYS belong to a container (ownership derived from container)
    containerId:
      type: string
      format: uuid
      description: Container holding this item (ownership derived from container's owner)
    slotIndex:
      type: integer
      nullable: true
      description: Slot position in slot-based containers
    slotX:
      type: integer
      nullable: true
      description: X position in grid-based containers
    slotY:
      type: integer
      nullable: true
      description: Y position in grid-based containers
    rotated:
      type: boolean
      nullable: true
      description: Whether item is rotated 90 degrees in grid

    # Realm
    realmId:
      type: string
      format: uuid
      description: Realm this instance exists in

    # Quantity (uses decimal for unified handling)
    quantity:
      type: number
      format: double
      description: Item quantity (1 for unique, integer for discrete, decimal for continuous)

    # Instance state
    currentDurability:
      type: integer
      nullable: true
      description: Current durability (if template has durability)
    boundToId:
      type: string
      format: uuid
      nullable: true
      description: Character ID this item is soulbound to
    boundAt:
      type: string
      format: date-time
      nullable: true
      description: When item was bound

    # Modifications (game-specific)
    customStats:
      type: object
      nullable: true
      description: Instance-specific stat modifications (enchantments, etc.)
    customName:
      type: string
      nullable: true
      description: Player-assigned custom name
    instanceMetadata:
      type: object
      nullable: true
      description: Any other instance-specific data

    # Audit trail
    originType:
      $ref: '#/components/schemas/ItemOriginType'
    originId:
      type: string
      format: uuid
      nullable: true
      description: Source entity ID (quest ID, creature ID, etc.)
    createdAt:
      type: string
      format: date-time
      description: Instance creation timestamp
    modifiedAt:
      type: string
      format: date-time
      nullable: true
      description: Last modification timestamp

ItemOriginType:
  type: string
  enum:
    - loot            # Dropped from creature/container
    - quest           # Quest reward
    - craft           # Crafted by player
    - trade           # Received in trade
    - purchase        # Bought from vendor
    - spawn           # System-spawned (admin, event)
    - other           # Game-defined origin
  description: How this item instance was created
```

#### Container Schema
```yaml
Container:
  type: object
  required: [containerId, ownerId, ownerType, containerType, constraintModel]
  properties:
    containerId:
      type: string
      format: uuid
      description: Unique identifier for this container

    # Ownership
    ownerId:
      type: string
      format: uuid
      description: ID of the entity that owns this container
    ownerType:
      $ref: '#/components/schemas/ContainerOwnerType'

    # Type and behavior
    containerType:
      type: string
      description: Game-defined type (e.g., "inventory", "bank", "equipment_slot")
    constraintModel:
      $ref: '#/components/schemas/ContainerConstraintModel'

    # Equipment slot support
    isEquipmentSlot:
      type: boolean
      description: Whether this container is an equipment slot
    equipmentSlotName:
      type: string
      nullable: true
      description: Equipment slot name (e.g., "main_hand", "helmet") if isEquipmentSlot

    # Slot constraints
    maxSlots:
      type: integer
      nullable: true
      description: Maximum number of slots (for slot-based containers)
    usedSlots:
      type: integer
      nullable: true
      description: Current used slots (denormalized)

    # Weight constraints
    maxWeight:
      type: number
      format: double
      nullable: true
      description: Maximum weight capacity

    # Grid constraints (internal dimensions)
    gridWidth:
      type: integer
      nullable: true
      description: Internal grid width (for grid containers)
    gridHeight:
      type: integer
      nullable: true
      description: Internal grid height (for grid containers)

    # Volume constraints
    maxVolume:
      type: number
      format: double
      nullable: true
      description: Maximum volume capacity
    currentVolume:
      type: number
      format: double
      nullable: true
      description: Current volume used (denormalized)

    # Nesting and weight propagation
    parentContainerId:
      type: string
      format: uuid
      nullable: true
      description: Parent container ID (null for root containers)
    nestingDepth:
      type: integer
      description: Depth in container hierarchy (0 = root)
    canContainContainers:
      type: boolean
      description: Whether this container can hold other containers
    maxNestingDepth:
      type: integer
      nullable: true
      description: Maximum nesting depth allowed (null = use global default)
    selfWeight:
      type: number
      format: double
      description: Empty container weight
    weightContribution:
      $ref: '#/components/schemas/WeightContribution'

    # Footprint in parent container
    slotCost:
      type: integer
      description: Slots used in slot-based parent (default 1)
    parentGridWidth:
      type: integer
      nullable: true
      description: Width footprint in grid-based parent
    parentGridHeight:
      type: integer
      nullable: true
      description: Height footprint in grid-based parent
    parentVolume:
      type: number
      format: double
      nullable: true
      description: Volume footprint in volumetric parent

    # Cached totals
    contentsWeight:
      type: number
      format: double
      description: Weight of direct contents (denormalized)
    totalWeight:
      type: number
      format: double
      description: Total weight including self (denormalized)

    # Filtering
    allowedCategories:
      type: array
      items:
        type: string
      nullable: true
      description: Allowed item categories (null = all allowed)
    forbiddenCategories:
      type: array
      items:
        type: string
      nullable: true
      description: Forbidden item categories
    allowedTags:
      type: array
      items:
        type: string
      nullable: true
      description: Required item tags for placement

    # Realm
    realmId:
      type: string
      format: uuid
      nullable: true
      description: Realm this container belongs to (null = account-level storage)

    # Game-specific
    tags:
      type: array
      items:
        type: string
      description: Container tags for filtering
    metadata:
      type: object
      nullable: true
      description: Game-specific container data

    # Lifecycle
    createdAt:
      type: string
      format: date-time
      description: Container creation timestamp
    modifiedAt:
      type: string
      format: date-time
      nullable: true
      description: Last modification timestamp

ContainerConstraintModel:
  type: string
  enum:
    - slot_only           # maxSlots only, items take 1 slot each
    - weight_only         # maxWeight only, no slot limit
    - slot_and_weight     # Both constraints apply
    - grid                # 2D grid with item dimensions
    - volumetric          # 3D volume (rare, complex)
    - unlimited           # No constraints (admin/debug)
  description: Container capacity constraint type

WeightContribution:
  type: string
  enum:
    - none                # Parent ignores entirely (magical storage)
    - self_only           # Only container's empty weight counts
    - self_plus_contents  # Container + all recursive contents (DEFAULT)
  description: How container weight propagates to parent

ContainerOwnerType:
  type: string
  enum:
    - character           # Player or NPC
    - account             # Account-level storage (cross-character)
    - location            # World containers (chests, items on ground)
    - vehicle             # Vehicle storage
    - guild               # Guild bank, shared storage
    - escrow              # Trade/market escrow
    - mail                # Mail system
    - other               # Game-defined owner types
  description: Type of entity that owns this container
```

### 4.3 API Design

Following Bannou's POST-only pattern with proper `x-permissions` format:

```yaml
# ═══════════════════════════════════════════════════════════
# lib-item API (schemas/item-api.yaml)
# ═══════════════════════════════════════════════════════════

openapi: 3.0.3
info:
  title: Item Service API
  version: 1.0.0
  description: |
    Item template and instance management service.

    **NRT Compliance**: AI agents MUST review docs/NRT-SCHEMA-RULES.md before
    modifying this schema. Optional reference types require `nullable: true`.

servers:
  - url: http://localhost:5012
    description: Bannou service endpoint

paths:
  /item/template/create:
    post:
      operationId: createItemTemplate
      tags: [Item Template]
      summary: Create a new item template
      description: Creates a new item definition for a game
      x-permissions:
        - role: developer
          states: {}
      # ... request/response bodies

  /item/template/get:
    post:
      operationId: getItemTemplate
      tags: [Item Template]
      summary: Get item template by ID or code
      x-permissions:
        - role: user
          states: {}

  /item/template/list:
    post:
      operationId: listItemTemplates
      tags: [Item Template]
      summary: List item templates with filters
      x-permissions:
        - role: user
          states: {}

  /item/template/update:
    post:
      operationId: updateItemTemplate
      tags: [Item Template]
      summary: Update item template mutable fields
      x-permissions:
        - role: developer
          states: {}

  /item/template/deprecate:
    post:
      operationId: deprecateItemTemplate
      tags: [Item Template]
      summary: Deprecate an item template
      x-permissions:
        - role: admin
          states: {}

  /item/instance/create:
    post:
      operationId: createItemInstance
      tags: [Item Instance]
      summary: Create a new item instance
      description: Creates a new item from a template
      x-permissions:
        - role: developer
          states: {}

  /item/instance/get:
    post:
      operationId: getItemInstance
      tags: [Item Instance]
      summary: Get item instance by ID
      x-permissions:
        - role: user
          states: {}

  /item/instance/modify:
    post:
      operationId: modifyItemInstance
      tags: [Item Instance]
      summary: Modify item instance state
      x-permissions:
        - role: developer
          states: {}

  /item/instance/bind:
    post:
      operationId: bindItemInstance
      tags: [Item Instance]
      summary: Bind item to character
      x-permissions:
        - role: developer
          states: {}

  /item/instance/destroy:
    post:
      operationId: destroyItemInstance
      tags: [Item Instance]
      summary: Destroy item instance
      x-permissions:
        - role: developer
          states: {}

# ═══════════════════════════════════════════════════════════
# lib-inventory API (schemas/inventory-api.yaml)
# ═══════════════════════════════════════════════════════════

  /inventory/container/create:
    post:
      operationId: createContainer
      tags: [Container]
      summary: Create a new container
      x-permissions:
        - role: developer
          states: {}

  /inventory/container/get:
    post:
      operationId: getContainer
      tags: [Container]
      summary: Get container with contents
      x-permissions:
        - role: user
          states: {}

  /inventory/container/get-or-create:
    post:
      operationId: getOrCreateContainer
      tags: [Container]
      summary: Get container or create if not exists (lazy creation pattern)
      description: |
        Enables lazy container creation for character inventories.
        If container doesn't exist for the owner/type combo, creates it.
      x-permissions:
        - role: developer
          states: {}

  /inventory/container/list:
    post:
      operationId: listContainers
      tags: [Container]
      summary: List containers for owner
      x-permissions:
        - role: user
          states: {}

  /inventory/container/delete:
    post:
      operationId: deleteContainer
      tags: [Container]
      summary: Delete container
      x-permissions:
        - role: admin
          states: {}

  /inventory/add:
    post:
      operationId: addItemToContainer
      tags: [Inventory Operations]
      summary: Add items to container
      description: Add items (create instances or stack onto existing)
      x-permissions:
        - role: developer
          states: {}

  /inventory/remove:
    post:
      operationId: removeItemFromContainer
      tags: [Inventory Operations]
      summary: Remove items from container
      x-permissions:
        - role: developer
          states: {}

  /inventory/move:
    post:
      operationId: moveItem
      tags: [Inventory Operations]
      summary: Move item to different slot or container
      x-permissions:
        - role: user
          states: {}

  /inventory/transfer:
    post:
      operationId: transferItem
      tags: [Inventory Operations]
      summary: Transfer item ownership
      x-permissions:
        - role: developer
          states: {}

  /inventory/split:
    post:
      operationId: splitStack
      tags: [Inventory Operations]
      summary: Split stack into two
      x-permissions:
        - role: user
          states: {}

  /inventory/merge:
    post:
      operationId: mergeStacks
      tags: [Inventory Operations]
      summary: Merge two stacks
      x-permissions:
        - role: user
          states: {}

  /inventory/query:
    post:
      operationId: queryItems
      tags: [Inventory Queries]
      summary: Find items across containers
      x-permissions:
        - role: user
          states: {}

  /inventory/count:
    post:
      operationId: countItems
      tags: [Inventory Queries]
      summary: Count items of a template
      x-permissions:
        - role: user
          states: {}

  /inventory/has:
    post:
      operationId: hasItems
      tags: [Inventory Queries]
      summary: Check if entity has required items
      x-permissions:
        - role: user
          states: {}

  /inventory/find-space:
    post:
      operationId: findSpace
      tags: [Inventory Queries]
      summary: Find where item would fit
      x-permissions:
        - role: user
          states: {}
```

### 4.4 Events

Events use kebab-case for multi-word entity names (per T5 topic naming convention).

#### 4.4.1 lib-item Events Schema (`schemas/item-events.yaml`)

```yaml
openapi: 3.0.3
info:
  title: Item Service Events
  version: 1.0.0
  description: |
    Event models for Item service pub/sub via MassTransit.
    Lifecycle events are auto-generated from x-lifecycle definition.

    **Topic Naming**: Uses kebab-case for multi-word entities (item-template, item-instance).

  x-event-subscriptions: []

  x-event-publications:
    # Template lifecycle (auto-generated from x-lifecycle)
    - topic: item-template.created
      event: ItemTemplateCreatedEvent
      description: Published when a new item template is created
    - topic: item-template.updated
      event: ItemTemplateUpdatedEvent
      description: Published when an item template is updated
    - topic: item-template.deprecated
      event: ItemTemplateDeprecatedEvent
      description: Published when an item template is deprecated

    # Instance lifecycle (auto-generated from x-lifecycle)
    - topic: item-instance.created
      event: ItemInstanceCreatedEvent
      description: Published when a new item instance is created
    - topic: item-instance.modified
      event: ItemInstanceModifiedEvent
      description: Published when an item instance is modified
    - topic: item-instance.destroyed
      event: ItemInstanceDestroyedEvent
      description: Published when an item instance is destroyed

    # Binding events
    - topic: item-instance.bound
      event: ItemInstanceBoundEvent
      description: Published when an item is bound to a character
    - topic: item-instance.unbound
      event: ItemInstanceUnboundEvent
      description: Published when an item binding is removed

# Lifecycle events auto-generated from x-lifecycle
x-lifecycle:
  ItemTemplate:
    model:
      templateId: { type: string, format: uuid, primary: true, required: true, description: "Template ID" }
      code: { type: string, required: true, description: "Unique item code" }
      gameId: { type: string, required: true, description: "Game service ID" }
      name: { type: string, required: true, description: "Display name" }
      category: { type: string, required: true, description: "Item category" }
      scope: { type: string, required: true, description: "Realm scope" }
      isActive: { type: boolean, required: true, description: "Whether active" }
    sensitive: []

  ItemInstance:
    model:
      instanceId: { type: string, format: uuid, primary: true, required: true, description: "Instance ID" }
      templateId: { type: string, format: uuid, required: true, description: "Template reference" }
      containerId: { type: string, format: uuid, required: true, description: "Container holding item" }
      realmId: { type: string, format: uuid, required: true, description: "Realm ID" }
      quantity: { type: number, required: true, description: "Item quantity" }
    sensitive: []

# Non-lifecycle events defined in components/schemas
components:
  schemas:
    ItemInstanceBoundEvent:
      type: object
      required: [eventId, timestamp, instanceId, templateId, characterId, bindType]
      properties:
        eventId:
          type: string
          format: uuid
          description: Unique event identifier
        timestamp:
          type: string
          format: date-time
          description: Event timestamp
        instanceId:
          type: string
          format: uuid
          description: Bound item instance ID
        templateId:
          type: string
          format: uuid
          description: Item template ID
        characterId:
          type: string
          format: uuid
          description: Character the item is bound to
        bindType:
          type: string
          description: Type of binding (on_pickup, on_equip, on_use)

    ItemInstanceUnboundEvent:
      type: object
      required: [eventId, timestamp, instanceId, templateId, previousCharacterId, reason]
      properties:
        eventId:
          type: string
          format: uuid
          description: Unique event identifier
        timestamp:
          type: string
          format: date-time
          description: Event timestamp
        instanceId:
          type: string
          format: uuid
          description: Unbound item instance ID
        templateId:
          type: string
          format: uuid
          description: Item template ID
        previousCharacterId:
          type: string
          format: uuid
          description: Character the item was bound to
        reason:
          type: string
          description: Reason for unbinding (admin, expiration, transfer_override)
```

#### 4.4.2 lib-inventory Events Schema (`schemas/inventory-events.yaml`)

```yaml
openapi: 3.0.3
info:
  title: Inventory Service Events
  version: 1.0.0
  description: |
    Event models for Inventory service pub/sub via MassTransit.
    Lifecycle events are auto-generated from x-lifecycle definition.

    **Topic Naming**: Uses kebab-case for multi-word entities (inventory-container, inventory-item).

  x-event-subscriptions: []

  x-event-publications:
    # Container lifecycle (auto-generated from x-lifecycle)
    - topic: inventory-container.created
      event: InventoryContainerCreatedEvent
      description: Published when a container is created
    - topic: inventory-container.modified
      event: InventoryContainerModifiedEvent
      description: Published when a container is modified
    - topic: inventory-container.deleted
      event: InventoryContainerDeletedEvent
      description: Published when a container is deleted

    # Item placement events
    - topic: inventory-item.placed
      event: InventoryItemPlacedEvent
      description: Published when an item is placed in a container
    - topic: inventory-item.removed
      event: InventoryItemRemovedEvent
      description: Published when an item is removed from a container
    - topic: inventory-item.moved
      event: InventoryItemMovedEvent
      description: Published when an item moves between slots/containers
    - topic: inventory-item.transferred
      event: InventoryItemTransferredEvent
      description: Published when item ownership transfers

    # Stack events
    - topic: inventory-item.stacked
      event: InventoryItemStackedEvent
      description: Published when items are stacked together
    - topic: inventory-item.split
      event: InventoryItemSplitEvent
      description: Published when a stack is split

    # Capacity events
    - topic: inventory-container.full
      event: InventoryContainerFullEvent
      description: Published when container reaches capacity
    - topic: inventory.weight-exceeded
      event: InventoryWeightExceededEvent
      description: Published when weight limit is exceeded
    - topic: inventory.nesting-limit
      event: InventoryNestingLimitEvent
      description: Published when nesting depth limit is reached

x-lifecycle:
  InventoryContainer:
    model:
      containerId: { type: string, format: uuid, primary: true, required: true, description: "Container ID" }
      ownerId: { type: string, format: uuid, required: true, description: "Owner entity ID" }
      ownerType: { type: string, required: true, description: "Owner type" }
      containerType: { type: string, required: true, description: "Container type" }
      constraintModel: { type: string, required: true, description: "Constraint model" }
      isEquipmentSlot: { type: boolean, required: true, description: "Whether equipment slot" }
    sensitive: []

components:
  schemas:
    # Non-lifecycle event schemas here...
    # (Full definitions omitted for brevity - each event has full property definitions)
```

#### 4.4.3 Event Flow Examples

**Equipping an item (via inventory-item.moved):**
```
1. Client: POST /inventory/move { instanceId, targetContainerId: helmet_slot.id }
2. lib-inventory validates slot constraints
3. lib-inventory publishes:
   - inventory-item.removed (from backpack)
   - inventory-item.moved (backpack → helmet_slot)
4. lib-equipment (future) listens for moves where toContainerType == "equipment_slot"
5. Game calculates stat changes
```

**Crafting creates item (cross-plugin):**
```
1. lib-craft calls lib-item /item/instance/create
2. lib-item publishes: item-instance.created
3. lib-craft calls lib-inventory /inventory/add
4. lib-inventory publishes: inventory-item.placed
5. Client receives both events, updates UI
```

**Item dropped on ground:**
```
1. Client: POST /inventory/move { instanceId, targetContainerId: location's ground container }
2. lib-inventory validates move (container constraints, etc.)
3. lib-inventory publishes: inventory-item.moved
4. Item now has containerId: location's ground container (ownership derived from container)
```

---

## Part 5: Resolved Architectural Decisions

### 5.1 Decisions Summary

| Decision | Resolution | Rationale |
|----------|------------|-----------|
| **Plugin structure** | Two plugins: lib-item + lib-inventory | Items have rich lifecycle deserving own events. Containers are spatial organization concern. |
| **Grid inventory** | Include in initial implementation | All features in single session; no phasing. |
| **Fluid handling** | Continuous quantities via `quantityModel: continuous` | Simple approach covers most use cases. |
| **Equipment system** | Equipment slots = containers with `maxSlots: 1` and `isEquipmentSlot: true` | Reuses inventory mechanics. Future lib-equipment can layer on top. |
| **Nested containers** | Game-configurable depth, default max 3 | Weight is only transitive property. |
| **Weight propagation** | `WeightContribution` enum: none, self_only, self_plus_contents | Handles magical bags, cargo holds, realistic physics. |
| **State store backend** | Redis cache + MySQL persistence | High-frequency access needs cache; persistence needs MySQL for queries. |
| **Template scope** | Follow lib-currency pattern (global, realm_specific, multi_realm) | Consistency across services. |
| **Weight/volume precision** | Follow lib-currency precision enum pattern | Consistency across services. |
| **Character inventory creation** | Lazy creation via `/inventory/container/get-or-create` | Container doesn't exist until accessed, then auto-created. |
| **Items on ground** | Location owns a container; items placed in location's container | `ownerType: location`, `ownerId: locationId`, `containerId: location's ground container`. |

### 5.2 Edge Cases and Handling

| Edge Case | Handling |
|-----------|----------|
| Item stacking across containers | Auto-merge on transfer when same template and capacity allows |
| Container nesting limits | `maxNestingDepth` per container, validated on placement |
| Weight exceeds limit during nesting | Validation walk checks all ancestors before allowing placement |
| Magical container in weighted parent | `weightContribution: none` stops propagation entirely |
| Item deletion cascade | Container delete specifies `itemHandling`: destroy, transfer, or error |
| Ownership transfers (trades) | Atomic transaction via future lib-market with escrow containers |
| Realm-crossing items | Account-level containers (`realmId: null`) for cross-realm storage |
| Template deprecation | Items keep old template reference, migration API available |
| Concurrent modifications | Optimistic locking via container version/ETag |
| Stack overflow | Return `overflowQuantity`, partial success allowed |
| Fluid in slot container | Fluid container uses `slotCost: 1` footprint; internal volume separate |
| Backpack in grid inventory | Backpack has footprint (`parentGridWidth/Height`) separate from internal grid |

### 5.3 Deferred for Future Consideration

| Topic | Rationale |
|-------|-----------|
| **lib-fluid** (pressure/flow simulation) | Only needed if games require pipe networks, pressure physics. Current continuous quantities handle storage. |
| **lib-equipment** (stat bonuses, set effects) | Mechanical foundation (equipment slots as containers) is ready. Higher-level equipment logic deferred. |
| **Cross-game trading** | Entities not realm-specific, so mechanically possible. Policy/balance concerns remain game-specific. |
| **Procedural item generation** | Template system supports it. Procedural generation logic belongs in game code, not inventory service. |

---

## Part 6: State Stores and Configuration

### 6.1 State Store Definitions (for `schemas/state-stores.yaml`)

```yaml
# ═══════════════════════════════════════════════════════════
# lib-item State Stores
# ═══════════════════════════════════════════════════════════

item-template-cache:
  backend: redis
  prefix: "item:tpl"
  service: Item
  purpose: Template lookup cache (global, aggressive caching)

item-template-store:
  backend: mysql
  service: Item
  purpose: Item template definitions (persistent, queryable)

item-instance-cache:
  backend: redis
  prefix: "item:inst"
  service: Item
  purpose: Hot item instance data for active gameplay

item-instance-store:
  backend: mysql
  service: Item
  purpose: Item instances (persistent, realm-partitioned)

# ═══════════════════════════════════════════════════════════
# lib-inventory State Stores
# ═══════════════════════════════════════════════════════════

inventory-container-cache:
  backend: redis
  prefix: "inv:cont"
  service: Inventory
  purpose: Container state and item list cache

inventory-container-store:
  backend: mysql
  service: Inventory
  purpose: Container definitions (persistent)

inventory-lock:
  backend: redis
  prefix: "inv:lock"
  service: Inventory
  purpose: Distributed locks for concurrent modifications
```

### 6.2 Configuration Schemas

#### lib-item Configuration (`schemas/item-configuration.yaml`)

```yaml
openapi: 3.0.3
info:
  title: Item Service Configuration
  version: 1.0.0

x-service-configuration:
  properties:
    DefaultMaxStackSize:
      type: integer
      default: 99
      env: ITEM_DEFAULT_MAX_STACK_SIZE
      description: Default max stack size for new templates

    DefaultWeightPrecision:
      type: string
      default: decimal_2
      env: ITEM_DEFAULT_WEIGHT_PRECISION
      description: Default weight precision for new templates

    TemplateCacheTtlSeconds:
      type: integer
      default: 3600
      env: ITEM_TEMPLATE_CACHE_TTL_SECONDS
      description: TTL for template cache entries

    InstanceCacheTtlSeconds:
      type: integer
      default: 900
      env: ITEM_INSTANCE_CACHE_TTL_SECONDS
      description: TTL for instance cache entries
```

#### lib-inventory Configuration (`schemas/inventory-configuration.yaml`)

```yaml
openapi: 3.0.3
info:
  title: Inventory Service Configuration
  version: 1.0.0

x-service-configuration:
  properties:
    DefaultMaxNestingDepth:
      type: integer
      default: 3
      env: INVENTORY_DEFAULT_MAX_NESTING_DEPTH
      description: Default maximum nesting depth for containers

    DefaultWeightContribution:
      type: string
      default: self_plus_contents
      env: INVENTORY_DEFAULT_WEIGHT_CONTRIBUTION
      description: Default weight contribution mode for containers

    ContainerCacheTtlSeconds:
      type: integer
      default: 300
      env: INVENTORY_CONTAINER_CACHE_TTL_SECONDS
      description: TTL for container cache entries

    LockTimeoutSeconds:
      type: integer
      default: 30
      env: INVENTORY_LOCK_TIMEOUT_SECONDS
      description: Timeout for container modification locks

    EnableLazyContainerCreation:
      type: boolean
      default: true
      env: INVENTORY_ENABLE_LAZY_CONTAINER_CREATION
      description: Whether to enable lazy container creation for characters
```

---

## Part 7: Implementation Considerations

### 7.1 Performance

- **Item templates**: Read-heavy, cache aggressively in Redis
- **Container queries**: Frequent, index by owner in MySQL
- **Grid placement**: Complex, client-side with server validation
- **Bulk operations**: Batch API for multi-item operations

### 7.2 Scalability

- **Realm-partition** item instances in MySQL
- **Global replicate** item templates
- **Redis cluster** for hot containers
- **Read replicas** for query-heavy workloads

### 7.3 Implementation Order

Both lib-item and lib-inventory implemented together with full feature set:

**1. lib-item (implement first)**
   - Item template CRUD and caching
   - Item instance lifecycle (create, modify, destroy)
   - Binding system (soulbound)
   - Durability tracking
   - All quantity models (discrete, continuous, unique)
   - Template deprecation handling

**2. lib-inventory (implement second, depends on lib-item)**
   - Container CRUD with all constraint models
   - Slot-based containers
   - Weight-based containers with propagation
   - Grid-based containers with rotation
   - Volumetric containers
   - Nested container support with WeightContribution
   - Equipment slots as containers
   - Movement, transfer, split, merge operations
   - Validation walk algorithm
   - Lazy container creation via get-or-create

**3. Integration**
   - Event wiring between plugins
   - Cross-plugin test scenarios
   - Documentation and examples

### 7.4 Cross-Plugin Integration

```
┌─────────────────────────────────────────────────────────────┐
│                        lib-character                         │
│  (Character exists; inventory created lazily on access)      │
└─────────────────┬───────────────────────────────────────────┘
                  │ accesses containers via get-or-create
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

1. **Character inventory accessed** → lib-inventory get-or-create creates containers on demand
2. **Quest completed** → lib-quest calls `/inventory/add` with origin
3. **Item crafted** → lib-craft consumes inputs, creates outputs
4. **Item sold** → lib-market escrows item, lib-currency handles payment

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
  quantityModel: unique
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
  isEquipmentSlot: true
  equipmentSlotName: "main_hand"
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

*Document Status: ARCHITECTURE COMPLETE - TENETS AUDIT PASSED*
*Last Updated: 2026-01-22*
*Research Sources: 5 parallel web search agents covering game systems, fluid mechanics, data modeling, ECS patterns, and commercial BaaS platforms*

---

## Implementation Checklist

### lib-item Schema (schemas/item-api.yaml)
- [ ] ItemTemplate model with all fields and descriptions
- [ ] ItemInstance model with all fields and descriptions
- [ ] QuantityModel enum
- [ ] ItemRarity enum
- [ ] SoulboundType enum
- [ ] ItemCategory enum
- [ ] ItemScope enum (consistent with CurrencyScope)
- [ ] WeightPrecision enum (consistent with CurrencyPrecision)
- [ ] ItemOriginType enum
- [ ] Template CRUD endpoints with x-permissions
- [ ] Instance lifecycle endpoints with x-permissions
- [ ] Binding endpoints with x-permissions
- [ ] Query endpoints with x-permissions
- [ ] Lifecycle events via x-lifecycle
- [ ] State stores added to state-stores.yaml
- [ ] Configuration schema (item-configuration.yaml)

### lib-inventory Schema (schemas/inventory-api.yaml)
- [ ] Container model with nesting support and descriptions
- [ ] ContainerConstraintModel enum
- [ ] WeightContribution enum
- [ ] ContainerOwnerType enum
- [ ] Container CRUD endpoints with x-permissions
- [ ] get-or-create endpoint for lazy creation
- [ ] Add/Remove/Move/Transfer endpoints with x-permissions
- [ ] Split/Merge endpoints with x-permissions
- [ ] Query endpoints (find-space, has, count) with x-permissions
- [ ] Lifecycle events via x-lifecycle
- [ ] State stores added to state-stores.yaml
- [ ] Configuration schema (inventory-configuration.yaml)

### Implementation (after schema generation)
- [ ] lib-item service implementation
- [ ] lib-inventory service implementation
- [ ] Validation walk algorithm
- [ ] Weight recalculation
- [ ] Grid placement validation
- [ ] Lazy container creation
- [ ] Unit tests
- [ ] HTTP integration tests
