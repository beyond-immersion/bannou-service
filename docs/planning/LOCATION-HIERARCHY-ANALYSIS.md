# Location Hierarchy: Long-Term Architecture Analysis

> **Issue**: #274 - Location Hierarchy: Cross-Cutting Concerns
> **Created**: 2026-02-03
> **Purpose**: Deep analysis aligning Issue #274 decisions with THE_DREAM vision and economy architecture
> **Status**: DESIGN ANALYSIS - Requires discussion before implementation

---

## Executive Summary

Issue #274 identifies several cross-cutting concerns around location hierarchy. This analysis evaluates each concern against the long-term vision documents (THE_DREAM.md, ECONOMY_CURRENCY_ARCHITECTURE.md) to ensure architectural decisions serve the 100K+ NPC cinematic combat system, not just immediate needs.

**Key Finding**: Some proposed solutions in Issue #274 are "MVP" approaches that don't align with the long-term architecture. This document identifies which choices are architecturally sound and which need revision.

---

## Part 1: Contract Service Hierarchy Violation

### Current State (WRONG)

Contract (L1) directly calls Location (L2) for territory constraint validation:

```csharp
// plugins/lib-contract/ContractService.cs:1889
var ancestryResponse = await _locationClient.GetLocationAncestorsAsync(...)
```

This is a **clear TENET violation** - L1 services cannot depend on L2 services.

### Issue #274's Proposed Solutions

| Option | Description | Recommendation |
|--------|-------------|----------------|
| 1 | Remove location validation from Contract entirely | **Partially correct** |
| 2 | Move territory logic to L4 "LocationRules" service | Acceptable |
| 3 | Location-provides-contracts pattern | **Best long-term approach** |

### Long-Term Vision Alignment

From THE_DREAM.md Section 5 (Map Service as Context Brain):
> "The Map Service isn't just 'what objects are nearby' - it's the **queryable world state** that Event Agents use to understand context."

And the rich queryable layers include:
> "faction_territory - Political boundaries"

**Insight**: Territory is a **spatial/location concept**, not a contract concept. The Contract service should be a generic agreement primitive. Location (or an L4 service) should know which contracts apply within it.

### Critical Investigation Finding: Two Validation Patterns

Contract service currently uses **two different validation patterns**:

| Pattern | Used By | How It Works | Hierarchy Safe? |
|---------|---------|--------------|-----------------|
| **Handler-based** | Currency, Assets | ServiceNavigator calls registered handler endpoint | ✅ YES |
| **Hard-coded DI** | Territory | Direct `ILocationClient` injection | ❌ NO |

**Currency validation (CORRECT)**:
```csharp
// No ICurrencyClient in constructor - uses ServiceNavigator indirectly
var clauseType = await _stateStoreFactory.GetStore<ClauseTypeModel>(...)
    .GetAsync($"{CLAUSE_TYPE_PREFIX}asset_requirement", ct);

// Handler defines service and endpoint (stored in state, not hard-coded)
var apiDefinition = new PreboundApiDefinition {
    ServiceName = handler.Service,      // "currency"
    Endpoint = handler.Endpoint,         // "/currency/balance/get"
};

// ServiceNavigator resolves and invokes - NO direct DI dependency
var result = await _navigator.ExecutePreboundApiAsync(apiDefinition, context, ct);
```

**Territory validation (WRONG)**:
```csharp
// ILocationClient injected directly in constructor - HIERARCHY VIOLATION
public ContractService(..., ILocationClient locationClient) {
    _locationClient = locationClient;
}

// Hard-coded in CheckContractConstraintAsync switch statement
case ConstraintType.Territory:
    var ancestryResponse = await _locationClient.GetLocationAncestorsAsync(...);
```

### Recommended Architecture

**The Fix**: Territory validation should use the **same handler pattern as currency**.

**Phase 1: Remove ILocationClient from Contract (Immediate)**
- Remove `ILocationClient` from constructor DI
- Remove `Territory` case from hard-coded constraint switch
- Territory becomes a **clause type with handler registration**, not a built-in constraint

**Phase 1.5: Register Territory Handler (Location Service Responsibility)**

Location service should expose a validation endpoint and register it:
```csharp
// Location service startup or seed
await contractClient.RegisterClauseTypeAsync(new RegisterClauseTypeRequest {
    TypeCode = "territory_constraint",
    Description = "Validates entity location against territorial boundaries",
    Category = ClauseCategory.Validation,
    ValidationHandler = new ClauseHandlerDefinition {
        Service = "location",
        Endpoint = "/location/validate-territory",
        RequestMapping = new Dictionary<string, string> {
            ["location_id"] = "$.proposedAction.locationId",
            ["territory_ids"] = "$.customTerms.territoryLocationIds",
            ["mode"] = "$.customTerms.territoryMode"  // exclusive/inclusive
        }
    }
});
```

This respects hierarchy: Location (L2) registers its own handler. Contract (L1) doesn't know about Location - it just invokes registered handlers.

**Phase 2: Location-Contract Binding (L2 Change)**

```yaml
# Extend location-api.yaml
LocationModel:
  properties:
    boundContractIds:
      type: array
      items:
        type: string
        format: uuid
      description: Contracts bound directly to this location (inherited by descendants)

# New endpoint
/location/get-effective-contracts:
  description: Returns all contracts that apply at this location (own + ancestors')
  request:
    locationId: uuid
  response:
    contracts: [{ contractId, boundAtLocationId, inheritanceDepth }]
```

**Phase 3: Validation Workflow (Game/L4 Responsibility)**

```
Game Server or L4 Service:
1. Query: /location/get-effective-contracts
2. For each effective contract:
   a. Query: /contract/instance/get (get contract details)
   b. Check if proposed action violates contract terms
3. Return: allowed/denied with reasons
```

This respects hierarchy: L2 provides data, L4/game consumes it.

---

## Part 2: Mapping regionId and THE_DREAM

### Current State

From MAPPING.md:
> "regionId is completely unvalidated - no relationship to Location service"

### Why This Matters for THE_DREAM

From THE_DREAM.md Section 5.1:
```
Map Layers (queryable by Event Agents):
├── significant_individuals    # Characters above power threshold
├── mana_density              # Magical environment state
├── faction_territory         # Political boundaries  <-- Requires Location!
├── crowd_density             # Where people are gathered
```

Event Brains need to correlate spatial data with named places:
- "Is this fight in the Market Square?" (for crowd density)
- "Are we in Sacred Lands?" (for spell restrictions)
- "Which faction controls this territory?" (for NPC reactions)

If regionId has no relationship to Location, **Event Brains cannot answer these questions**.

### Issue #274's Proposed Options

| Option | Description | Long-Term Viability |
|--------|-------------|---------------------|
| A | regionId = locationId (validated) | **Too restrictive** |
| B | regionId independent (unvalidated) | **Insufficient** for THE_DREAM |
| C | regionId validated against "region" subset | Overcomplicated |
| D | Create separate Region registry | Unnecessary duplication |

### Recommended Architecture

**Option E: Optional locationId correlation on channels**

```yaml
# mapping-api.yaml - CreateChannelRequest
CreateChannelRequest:
  properties:
    regionId:
      type: string
      format: uuid
      description: Spatial region identifier (required, but not validated against Location)
    locationId:
      type: string
      format: uuid
      nullable: true
      description: Optional correlation to Location service for semantic queries
    kind:
      $ref: '#/components/schemas/MapKind'
```

**Why this works**:

1. **Flexibility preserved**: Temporary combat zones don't need a Location
2. **Semantic correlation enabled**: Channels for "The Market Square" can link to locationId
3. **Event Brain queries work**: "What affordances in CITY_FROSTHOLD?" queries channels with that locationId
4. **No validation overhead**: locationId is informational, not enforced

**New Query Endpoint**:

```yaml
/mapping/query/by-location:
  description: Query spatial data by Location hierarchy
  request:
    locationId: uuid
    includeDescendants: boolean  # Query all locations under this one
    kinds: [MapKind]
    bounds: Bounds3D  # Optional spatial filter within the location
  response:
    objects: [MapObject]
```

This bridges the hierarchical (Location) and spatial (Mapping) worlds.

---

## Part 3: Location Spatial Data

### The Question

Issue #274 asks: "Should Location gain spatial data, or remain purely hierarchical?"

### Long-Term Vision Evidence

From THE_DREAM.md Section 6.1:
> "The Map Service provides environmental discovery - the 'what's around' that makes procedural cinematics possible"

And the affordance query example:
```yaml
POST /maps/metadata/list
{
  "mapId": "dungeon-17",
  "bounds": { "minX": 10, "minY": 20, "maxX": 30, "maxY": 40 },
  "objectType": ["throwable", "climbable-surface", "breakable", "hazard"]
}
```

From ECONOMY_CURRENCY_ARCHITECTURE.md Section 7.2 (Trade Routes):
> "Legs have fromLocationId, toLocationId, estimatedDuration, distance"

**Insight**: The system needs to answer:
- "What affordances exist in The Market Square?" (name → spatial)
- "What location is position (100, 50, 20) in?" (spatial → name)

Without spatial data on Location, these queries require expensive joins.

### Recommended Architecture

**Add optional bounding box to Location**:

```yaml
# location-api.yaml
LocationModel:
  properties:
    # ... existing fields ...
    bounds:
      $ref: '#/components/schemas/BoundingBox3D'
      nullable: true
      description: Optional spatial extent. Used for spatial-to-location queries.
    boundsPrecision:
      type: string
      enum: [exact, approximate, none]
      default: none
      description: |
        exact: Bounds precisely define this location
        approximate: Bounds are rough estimate (city limits, etc.)
        none: No spatial data available

BoundingBox3D:
  type: object
  properties:
    minX: { type: number, format: float }
    minY: { type: number, format: float }
    minZ: { type: number, format: float }
    maxX: { type: number, format: float }
    maxY: { type: number, format: float }
    maxZ: { type: number, format: float }
```

**New endpoint for spatial-to-location lookup**:

```yaml
/location/query/by-position:
  description: Find location(s) containing a spatial position
  request:
    position: Position3D
    realmId: uuid
    maxDepth: integer  # How deep in hierarchy to search
  response:
    locations: [{ locationId, name, depth, boundsPrecision }]
```

**Hierarchy rule**: A position is "in" a location if:
1. Within bounds of the location, OR
2. Within bounds of any descendant of the location

### Critical Requirement: "Bigger on the Inside" Effect

Games need to support locations where **child location bounds don't fit within parent bounds**:

**Example**: A magical tower (LocB) sits in a city (LocA)
- LocA (city): defines 1000x1000x1000 world-space bounds
- LocB (tower): occupies 100x100x100 of LocA's space (900-1000 on each axis)
- **Inside** LocB: has its own 1000x1000x1000 coordinate space!

This is common in games with:
- Pocket dimensions
- TARDIS-style spaces
- Instanced dungeons
- Magical bags of holding (container-as-location)

**Schema Extension**:

```yaml
LocationModel:
  properties:
    bounds:
      $ref: '#/components/schemas/BoundingBox3D'
      nullable: true
    boundsPrecision:
      type: string
      enum: [exact, approximate, none]
    coordinateMode:
      type: string
      enum:
        - inherit    # Child uses same coordinate system as parent (1:1 mapping)
        - local      # Child has independent coordinate system (bigger on inside)
        - portal     # Child accessed via portal, no spatial relationship
      default: inherit
      description: |
        inherit: Position in child maps directly to parent coordinate space
        local: Child has independent coordinate system (different scale/size)
        portal: Child is accessed via portal with no direct spatial relationship
    localOrigin:
      $ref: '#/components/schemas/Position3D'
      nullable: true
      description: |
        For 'inherit' mode: offset from parent's origin
        For 'local' mode: entry point position in local coordinates
        For 'portal' mode: unused
```

**Query behavior by mode**:

| Mode | "What location is position X in?" | "What's at position X in LocB?" |
|------|-----------------------------------|--------------------------------|
| `inherit` | Check bounds in parent coordinate space | Transform to parent space, query |
| `local` | Check if X is at entry portal of LocB | Query LocB's local coordinate space |
| `portal` | N/A - must explicitly enter via portal | Query LocB's local coordinate space |

This enables Event Brains to query affordances regardless of coordinate mode, while supporting diverse game designs

This enables Event Brains to query "What named place is this fight in?"

---

## Part 4: Ground Containers and Item Drops

### Current State

From Issue #164 and Issue #274:
- `ContainerOwnerType` includes `location` (schema exists)
- No implementation for location-owned containers
- Items removed from containers are in "limbo"

### Long-Term Vision Evidence

From ECONOMY_CURRENCY_ARCHITECTURE.md Section 7.3 (Shipments):
> "Goods lost during transport should drop somewhere"

From THE_DREAM.md (NPC economic participation):
- NPCs have production/consumption cycles
- Dropped items, loot, stolen goods need to exist in the world

### Recommended Architecture

**Phase 1: Extend Location model**:

```yaml
LocationModel:
  properties:
    groundContainerId:
      type: string
      format: uuid
      nullable: true
      description: Container for items "on the ground" at this location
    groundContainerCreationPolicy:
      type: string
      enum: [on_demand, explicit, inherit_parent, disabled]
      default: on_demand
```

**Phase 2: Implement drop behavior in Inventory**:

Per Issue #164's Option A+B hybrid:
- Per-container `dropBehavior` configuration
- `/inventory/drop` endpoint
- `location_ground` behavior that:
  1. Looks up current character/entity location
  2. Finds nearest location with ground container (walking up hierarchy)
  3. Creates container on-demand if policy allows
  4. Transfers item to that container

**Phase 3: Ground container lifecycle**:

```yaml
GroundContainerConfig:
  type: object
  properties:
    maxCapacity: integer
    itemTtlSeconds: integer  # Items expire after this long
    cleanupPolicy:
      type: string
      enum: [fifo, oldest_first, value_first]
```

Background service periodically:
1. Scans location ground containers
2. Removes expired items
3. Handles overflow per cleanup policy

---

## Part 5: Coordinate System Integration

### The Gap

From Issue #159:
- Scene stores transforms
- Mapping stores Position3D
- Location has no coordinates
- No shared specification

### Long-Term Vision Evidence

From THE_DREAM.md Section 10.1:
> "Object Type Registry: The `allowedObjectTypes` and object schema validation should anticipate affordance tagging."

And Section 5.3 (Event Tap Pattern):
> Event Agents query Map Service for spatial context

Without coordinate system agreement, Scene instantiation into Mapping produces inconsistent results.

### Recommended Architecture

**Create docs/reference/COORDINATE-SYSTEM.md**:

```markdown
# Bannou Spatial Coordinate System

## Coordinate Space
- **Handedness**: Right-handed
- **Up axis**: Y-up
- **Forward axis**: -Z
- **Units**: 1 unit = 1 meter

## Transform Order
- Scale → Rotation → Translation
- Rotation: Quaternion (w, x, y, z) normalized

## Precision
- Position: float32 (sufficient for ~1km at mm precision)
- Rotation: float32 quaternion

## Service Responsibilities
- **Scene**: Stores local transforms, converts to world on instantiation
- **Mapping**: Stores world-space Position3D, indexes by cell
- **Location**: Stores optional bounding box in world coordinates
```

**Update common-api.yaml**:

```yaml
Position3D:
  type: object
  description: Position in world coordinates (meters, Y-up, right-handed)
  properties:
    x: { type: number, format: float }
    y: { type: number, format: float }
    z: { type: number, format: float }

Rotation3D:
  type: object
  description: Rotation as normalized quaternion (w, x, y, z)
  properties:
    w: { type: number, format: float }
    x: { type: number, format: float }
    y: { type: number, format: float }
    z: { type: number, format: float }
```

---

## Part 6: Implementation Phases

### Phase 1: Fix Hierarchy Violation (URGENT)

1. **Remove ILocationClient from Contract**
   - Remove Territory constraint OR
   - Make it external-only (caller validates, Contract stores result)
2. **Update ContractService tests**
3. **Document the pattern** in CLAUDE.md

**Why urgent**: Every day this exists, it sets a bad precedent.

### Phase 2: Location-Contract Binding

1. Add `boundContractIds` to location-api.yaml
2. Implement `/location/get-effective-contracts`
3. Update Location deep dive
4. Game server uses this for territory validation

### Phase 3: Spatial Infrastructure

1. Create COORDINATE-SYSTEM.md
2. Add Position3D/Rotation3D to common-api.yaml
3. Add optional `bounds` to Location
4. Add `/location/query/by-position`

### Phase 4: Mapping-Location Integration

1. Add optional `locationId` to CreateChannelRequest
2. Add `/mapping/query/by-location`
3. Update Mapping deep dive
4. Event Brain queries work

### Phase 5: Ground Containers

1. Add `groundContainerId` and policy to Location
2. Implement `/inventory/drop` per Issue #164
3. Implement ground container lifecycle service
4. Update economy docs

---

## Part 7: Decisions Requiring Discussion

### 1. Territory Constraint Removal Strategy

**Option A**: Remove Territory constraint type entirely
- Contract stays pure agreement primitive
- All territory logic is game/L4 responsibility

**Option B**: Keep Territory constraint, make it external-validated
- Contract stores territory definition (location IDs)
- Caller provides "current location" to validation endpoints
- Contract checks without calling Location service

**Recommendation**: Option A - cleaner architecture, Contract shouldn't know about locations

### 2. Mapping regionId Semantics

**Option A**: Make locationId required on channels
- Strong correlation but inflexible

**Option B**: Make locationId optional (as proposed above)
- Flexible but requires discipline

**Option C**: Validate regionId IS a valid locationId
- Simple but prevents non-location regions

**Recommendation**: Option B - supports both ad-hoc regions and location-correlated regions

### 3. Ground Container Creation

**Option A**: On-demand (created when first item dropped)
- Zero upfront cost
- Risk of orphan containers

**Option B**: Explicit creation required
- More control
- Requires seeding infrastructure

**Option C**: Hybrid with policy per location
- Most flexible
- More complex

**Recommendation**: Option C - different location types have different needs

### 4. Location Bounds Enforcement

**Option A**: Informational only (no validation)
- Flexible but potentially inconsistent

**Option B**: Validated (children must be within parent bounds)
- Enforced hierarchy
- Complex for abstract locations (regions, kingdoms)

**Recommendation**: Option A - bounds are hints, not constraints. Many locations are abstract.

---

## Part 8: Cross-Reference to Vision Documents

| Concern | THE_DREAM Section | Economy Architecture Section | Status |
|---------|------------------|------------------------------|--------|
| Spatial queries for Event Brains | 5.1, 6.1 | - | Blocked by Missing Mapping-Location integration |
| Faction territory awareness | 5.1 | - | Blocked by Missing Location bounds |
| Ground item drops | 10.2 | 7.3 | Blocked by Missing ground containers |
| Trade route legs | - | 7.2 | Works (uses locationId directly) |
| Border crossing | - | 7.4 | Works (realm-level, not location) |
| Contract territory | - | - | **VIOLATED** - needs immediate fix |

---

## Part 9: Summary of Recommendations

### Immediate (This Sprint)
1. **Remove ILocationClient from Contract service** - hierarchy violation
2. **Document realm vs location distinction** in CLAUDE.md and SERVICE_HIERARCHY.md

### Short-Term (Next 2-4 Weeks)
3. **Add boundContractIds to Location** - enables location-bound contracts correctly
4. **Create COORDINATE-SYSTEM.md** - establishes shared spatial semantics
5. **Add optional locationId to Mapping channels** - enables semantic queries

### Medium-Term (Next 1-2 Months)
6. **Add optional bounds to Location** - enables spatial-to-location queries
7. **Implement ground containers** per Issue #164 - enables economy vision
8. **Add /location/query/by-position** - Event Brains can ask "where is this?"

### Future (When Needed)
9. **Full affordance tagging system** - per THE_DREAM Section 10.1
10. **Event Brain location awareness** - THE_DREAM Phase 3

---

## Appendix A: Related Issues

| Issue | Status | Relationship |
|-------|--------|--------------|
| #159 | Open | Spatial coordinate system (addressed in Phase 3) |
| #164 | Open | Item drop behavior (addressed in Phase 5) |
| #165 | Open | Location spatial coordinates (addressed in Phase 3) |
| #166 | Open | Location reference tracking (separate concern) |
| #259 | Open | Resource lifecycle (separate concern) |
| #268 | Open | Realm hierarchy - **CLOSE as "by design"** - realms are FLAT |
| #274 | Open | This issue (parent) |

---

*This analysis should be reviewed before implementing Issue #274's proposed phases.*
