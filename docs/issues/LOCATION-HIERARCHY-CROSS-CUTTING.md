# [Architecture] Location Hierarchy: Cross-Cutting Concerns & Terminology Clarification

## Summary

Location hierarchies are a foundational concept in Bannou that influence multiple systems: contracts, inventories, mapping authority, spatial data, and resource cleanup. However, terminology confusion ("realms within realms"), inverted dependencies (Contract validating Location), and missing implementations (location-bound contracts, ground containers) have created architectural debt. This issue consolidates these concerns and proposes a unified approach.

---

## Part 1: Terminology Clarification

### The Confusion

References to "realms within realms" or "realm hierarchy" have appeared in discussions and issues. This is **architecturally impossible** and stems from conflating two distinct concepts:

### REALM (L2 Game Foundation)

**Definition**: Top-level persistent worlds that are **PEERS with NO hierarchy between them**.

From `docs/plugins/REALM.md`:
> "Realms are peer worlds (e.g., Omega, Arcadia, Fantasia) with no hierarchical relationships between them. Each realm operates as an independent world."

- Realms are a **flat registry** of worlds
- No parent/child relationships between realms
- Examples: Omega, Arcadia, Fantasia
- Each realm has its own species populations, cultural contexts

### LOCATION (L2 Game Foundation)

**Definition**: Hierarchical tree structure **WITHIN a single realm**.

From `docs/plugins/LOCATION.md`:
> "Manages physical places (cities, regions, buildings, rooms, landmarks) within realms as a tree structure with depth tracking."

- Locations form a **tree** with parent-child relationships
- Each location belongs to **exactly one realm**
- Depth is tracked and cascades on parent changes
- Examples: CONTINENT → REGION → CITY → DISTRICT → BUILDING → ROOM

### Visual Representation

```
Realm Registry (FLAT - no hierarchy between realms)
├── Realm: "Omega"
├── Realm: "Arcadia"
│   └── Location Tree (WITHIN this realm only):
│       ├── CONTINENT_VASTORIA (depth=0, root)
│       │   ├── REGION_NORTHERN_HIGHLANDS (depth=1)
│       │   │   ├── CITY_FROSTHOLD (depth=2)
│       │   │   │   ├── DISTRICT_MARKET (depth=3)
│       │   │   │   │   ├── BUILDING_TAVERN (depth=4)
│       │   │   │   │   │   └── ROOM_CELLAR (depth=5)
│       │   │   │   │   └── BUILDING_SMITHY (depth=4)
│       │   │   │   └── DISTRICT_CASTLE (depth=3)
│       │   │   └── LANDMARK_CRYSTAL_LAKE (depth=2)
│       │   └── REGION_SOUTHERN_PLAINS (depth=1)
│       └── CONTINENT_EASTERN (depth=0, another root)
└── Realm: "Fantasia"
    └── (its own location tree)
```

### Key Takeaway

- **Realm hierarchy does NOT exist** (Issue #268 explicitly documents this as a blocking prerequisite)
- **Location hierarchy exists WITHIN each realm**
- Any feature requiring "realm hierarchy" needs architectural discussion first
- Location hierarchy features are well-supported and should be leveraged

---

## Part 2: Contract Service Hierarchy Violation

### The Problem

Contract (L1 App Foundation) directly depends on Location (L2 Game Foundation), violating the service hierarchy.

**Evidence** from `plugins/lib-contract/ContractService.cs:1889-1898`:
```csharp
// Contract (L1) calling Location (L2) - HIERARCHY VIOLATION
var ancestryResponse = await _locationClient.GetLocationAncestorsAsync(
    new GetLocationAncestorsRequest { LocationId = proposedLocationId.Value },
    cancellationToken);
```

### Why This Is Wrong

Contract is trying to **validate** that a proposed action is within a territory defined by the contract. But this inverts the correct relationship:

**Current (Wrong)**:
```
Contract validates → "Is this location in my territory?"
Contract knows about locations and their hierarchy
L1 depends on L2 (VIOLATION)
```

**Correct (Inverted)**:
```
Location defines → "These contracts/rules apply within me"
Location hierarchy determines what contracts cascade to descendants
L2 (or L4) handles the binding, Contract stays generic
```

### The User's Insight

> "Contracts BOUND to locations don't apply at all outside of it. That's not validation within the contract though, that's validation BEFORE the contract - that's smelling more like hierarchical locations defining contracts, the other way around."

This is exactly right. The contract should be a generic agreement structure. The **location** (or an L4 service) should:
1. Store which contracts are bound to it
2. Inherit contracts from ancestor locations
3. Validate proposed actions against all applicable contracts

### Remediation Options

1. **Remove location validation from Contract entirely** - Contract becomes purely agreement storage
2. **Move territory logic to L4 "LocationRules" service** - Subscribes to contract events, handles location binding
3. **Invert to Location-provides-contracts pattern** - Location service exposes bound contracts, callers query Location

**Recommended**: Option 3 with Location storing contract bindings, and an L4 service handling the validation workflow.

---

## Part 3: Location-Bound Contracts (Proposed Pattern)

### Concept

Locations can have contracts "bound" to them. These contracts:
- Apply to all actions within that location
- **Cascade to descendant locations** (children inherit parent's contracts)
- Are checked when any governed action occurs in that location subtree

### Example Use Cases

| Bound Contract | Location | Effect |
|----------------|----------|--------|
| "No Necromancy Zone" | REGION_SACRED_LANDS | All descendants (cities, buildings) inherit this restriction |
| "Merchant Guild Tax" | CITY_TRADEHAVEN | All market transactions in the city pay tax |
| "Royal Hunting Grounds" | FOREST_CROWN | Only nobles with contract can hunt here |
| "Safe Zone" | DISTRICT_TEMPLE | Combat contracts/actions forbidden |

### Data Model Extension

```yaml
# Potential addition to location-api.yaml
LocationModel:
  properties:
    # ... existing fields ...
    boundContractIds:
      type: array
      items:
        type: string
        format: uuid
      description: Contract instances bound directly to this location (not inherited)
```

### Query Pattern

When checking if an action is allowed at a location:
1. Get the location's ancestors (existing endpoint)
2. Collect all `boundContractIds` from the location and all ancestors
3. Check the proposed action against all collected contracts
4. Return allowed/denied with reasons

This keeps Contract generic and respects the service hierarchy.

---

## Part 4: Location Inventories / Ground Containers

### Current State

From `schemas/inventory-api.yaml`, `ContainerOwnerType` already includes `location`:
```yaml
ContainerOwnerType:
  enum:
    - character
    - account
    - location    # ← Already defined!
    - vehicle
    - guild
    - escrow
    - mail
    - other
```

However, **no implementation exists** for location-owned containers.

### Issue #164: Item Removal/Drop Behavior

This issue documents the gap thoroughly:
- Items removed from containers are in "limbo" (ContainerId not cleared)
- No configurable drop behavior
- No ground container concept

### What Should Exist

1. **Ground Containers**: Each location can have an auto-created ground container
2. **Drop Behavior**: Items removed go to the location's ground container
3. **Lifecycle**: Ground containers have TTL, cleanup policies, capacity limits
4. **Events**: `inventory-item.dropped` published for game-specific reactions

### Implementation Path

1. Location service gains optional `groundContainerId` field
2. Inventory service gains `/inventory/drop` endpoint (per Issue #164)
3. Drop behavior respects location hierarchy (item drops to nearest location with ground container)
4. Background cleanup service handles TTL expiration

---

## Part 5: Mapping Authority & regionId

### Current State

From `docs/plugins/MAPPING.md`:
- Mapping uses `regionId` as the spatial authority unit
- Authority granted per `regionId + kind` combination
- **regionId is completely unvalidated** - no relationship to Location service

### Issue #159: Spatial Coordinate System Contract

This issue explicitly calls out:
> "Mapping regionId validated against Location service...Currently regionId is unvalidated."

### The Question

Should `regionId` in Mapping correspond to `locationId` in Location service?

**Options**:
| Option | Description | Trade-offs |
|--------|-------------|------------|
| A | regionId = locationId (validated) | Strong coupling, clear semantics |
| B | regionId is independent (unvalidated) | Flexible but unclear relationship |
| C | regionId validated against a subset of locations | Locations marked as "regions" for mapping |
| D | Create separate Region registry | More infrastructure, clear separation |

**Recommendation**: Option A with validation - `regionId` should be a valid `locationId`, making the authority model explicit.

---

## Part 6: Additional Hierarchy-Influenced Concerns

| Area | Current State | Needed |
|------|---------------|--------|
| **Contract Binding** | Contract validates Location (WRONG) | Location defines bound contracts, inherited by descendants |
| **Ground Containers** | `location` is valid owner type, not implemented | Location-owned containers for dropped items |
| **Mapping Authority** | `regionId` is unvalidated | Define relationship to Location service |
| **Spatial Coordinates** | Location has no coordinates | Optional bounding box per location (Issue #165) |
| **Permission Scoping** | No location-based permissions | Actions allowed/denied based on location hierarchy |
| **Scene-Location Binding** | Scene uses `gameId`, Mapping uses `regionId` | Scenes should reference `locationId` |
| **NPC Jurisdiction** | Actors have no location awareness | Actors bound to locations, authority cascades |
| **Resource Cleanup** | Issue #259 design | Locations as resources have consumers |

---

## Part 7: Related Issues

| Issue | Title | Relationship |
|-------|-------|--------------|
| #159 | Cross-Cutting: Spatial Coordinate System Contract | Mapping/Location/Scene coordinate relationship |
| #164 | Item Removal/Drop Behavior | Location-owned ground containers |
| #165 | Location: Spatial coordinates | Adding optional coordinates to locations |
| #166 | Location: Soft-delete reference tracking | Track what references a location |
| #259 | Resource Lifecycle Management & Cascading Deletion | Location as a resource with consumers |
| #268 | Lore inheritance for realm hierarchy | Documents realm hierarchy DOESN'T EXIST (blocked) |

---

## Part 8: Proposed Implementation Phases

### Phase 1: Terminology & Documentation
- [ ] Update CLAUDE.md with realm vs location clarification
- [ ] Add terminology section to SERVICE_HIERARCHY.md
- [ ] Close/update Issue #268 noting realm hierarchy is not planned

### Phase 2: Contract Hierarchy Violation Fix
- [ ] Remove `ILocationClient` dependency from Contract service
- [ ] Remove `Territory` constraint type from Contract (or make it external)
- [ ] Add `boundContractIds` field to Location model (schema-first)
- [ ] Create `/location/get-bound-contracts` endpoint (returns own + ancestors')
- [ ] Document the location-bound contract pattern

### Phase 3: Location Ground Containers
- [ ] Implement Issue #164 design (drop behavior, ground containers)
- [ ] Add `groundContainerId` to Location model
- [ ] Implement auto-creation of ground containers
- [ ] Add cleanup/TTL policies

### Phase 4: Mapping-Location Integration
- [ ] Decide on regionId ↔ locationId relationship
- [ ] Add validation if adopting Option A
- [ ] Update Mapping documentation
- [ ] Address Issue #159 coordinate system

### Phase 5: Extended Hierarchy Features
- [ ] Location-based permission scoping
- [ ] Scene-Location binding
- [ ] NPC jurisdiction/authority cascading

---

## Labels

`architecture`, `cross-cutting`, `location`, `contract`, `inventory`, `mapping`, `discussion`

---

## Questions for Discussion

1. **Contract territory validation**: Remove entirely, or move to L4 service?
2. **regionId validation**: Should Mapping validate against Location service?
3. **Ground container auto-creation**: On-demand when items dropped, or explicit creation?
4. **Contract inheritance**: Simple list merge, or priority/override semantics?
5. **Coordinate system**: Should Location gain spatial data, or remain purely hierarchical?
