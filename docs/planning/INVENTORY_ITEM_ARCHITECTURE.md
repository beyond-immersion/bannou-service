# Inventory and Item System Architecture Planning

> **Created**: 2026-01-22
> **Status**: RESEARCH IN PROGRESS
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

*This section will be populated with findings from research agents...*

### 2.1 Game Inventory Systems

[PENDING: Research agent findings on game inventory implementations]

### 2.2 Fluid and Gas Systems

[PENDING: Research agent findings on fluid/gas handling]

### 2.3 Item Data Modeling Best Practices

[PENDING: Research agent findings on backend item schemas]

### 2.4 ECS Approaches

[PENDING: Research agent findings on ECS item systems]

### 2.5 Commercial BaaS Platforms

[PENDING: Research agent findings on PlayFab, GameSparks, etc.]

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

[TO BE REFINED based on research findings]

### 4.3 API Design

[TO BE REFINED based on research findings]

### 4.4 Events

[TO BE REFINED based on research findings]

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

---

## Appendix A: Research References

[Links and citations from web research]

---

## Appendix B: Comparison Matrix

[Detailed comparison of different approaches]

---

*Document Status: DRAFT - Awaiting research completion*
