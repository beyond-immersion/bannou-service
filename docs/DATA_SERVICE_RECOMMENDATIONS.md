# Data Service Architecture Analysis and Recommendations

**Document Status**: Living Document
**Last Updated**: 2025-12-17
**Author**: Claude Code Architecture Review
**Scope**: Character Service, Relationship Systems, Future Data Services

---

## Recent Architectural Changes (2025-12-17)

### Relationship Service Extraction
Relationships have been **extracted from the Character service** into a standalone **generic Relationship service** that handles entity-to-entity relationships between ANY two entities (not just characters).

**Key Changes**:
- New `schemas/relationship-api.yaml` with EntityType enum (CHARACTER, NPC, MONSTER, ITEM, LOCATION, ORGANIZATION, FACTION, REALM, OTHER)
- Relationships now use `entity1Id/entity1Type` and `entity2Id/entity2Type` instead of `characterId/relatedCharacterId`
- Character service no longer handles relationships

### Deprecation System
Both **RelationshipType** and **Species** services now support a two-step deletion workflow:
1. **Deprecate**: Soft-delete (sets `isDeprecated=true`, prevents new usage)
2. **Merge**: Migrate entities using deprecated type to a target type
3. **Delete**: Optional hard-delete after merge clears all references

**New Endpoints**:
- `/deprecate` - Soft-delete with optional reason
- `/undeprecate` - Restore deprecated entry
- `/merge` - Migrate and optionally delete source

### VOID/NULL Special Entries
Seed data now includes system entries for deletion workflow:
- **VOID**: Merge target for effectively "deleted" entries (like `/dev/null`)
- **UNKNOWN**: Fallback for unspecified types

---

## Executive Summary

This document provides a thorough audit of the current Character service implementation and recommendations for future data services in the Bannou platform. The analysis identifies **critical architectural concerns** around data persistence, performance at scale, and "sharding" implementation that require immediate attention before expanding to additional data services.

### Key Findings

| Area | Current State | Risk Level | Recommendation |
|------|--------------|------------|----------------|
| **Data Persistence** | Redis (ephemeral) | 🔴 CRITICAL | Migrate to MySQL via Dapr state store |
| **"Sharding" Implementation** | Application-level key prefixing | 🟡 MODERATE | Acceptable for now, document limitations |
| **Query Performance at 100k+** | O(n) memory loading + N+1 queries | 🔴 CRITICAL | Implement bulk loading, cursor pagination |
| **Relationship Type System** | ✅ Schema complete with deprecation | 🟢 DONE | Full CRUD + deprecate/merge/VOID workflow |
| **Species System** | ✅ Schema complete with deprecation | 🟢 DONE | Full CRUD + deprecate/merge/VOID workflow |
| **Generic Relationships** | ✅ Entity-agnostic design | 🟢 DONE | Separate service, any entity type |
| **Composite Uniqueness** | Not enforced | 🟡 MODERATE | Add validation layer |

---

## Table of Contents

1. [Current Implementation Analysis](#1-current-implementation-analysis)
2. [Critical Issue: Redis for Persistent Game Data](#2-critical-issue-redis-for-persistent-game-data)
3. [Sharding Analysis: What It Actually Does](#3-sharding-analysis-what-it-actually-does)
4. [Performance Analysis at Scale](#4-performance-analysis-at-scale)
5. [Relationship Type System Design](#5-relationship-type-system-design)
6. [Future Data Services Roadmap](#6-future-data-services-roadmap)
7. [Recommendations and Priority Order](#7-recommendations-and-priority-order)
8. [Implementation Alternatives](#8-implementation-alternatives)

---

## 1. Current Implementation Analysis

### 1.1 Character Service Architecture

**Location**: `lib-character/CharacterService.cs` (1,168 lines)
**Schema**: `schemas/character-api.yaml` (925 lines)
**State Store**: `character-statestore` (Redis)

#### Key Storage Pattern

```
Keys Created:
├── character:{realmId}:{characterId}     → CharacterModel (JSON)
├── character-global-index:{characterId}  → realmId (string)
├── realm-index:{realmId}                 → List<characterId> (JSON array)
├── relationship:{relationshipId}         → RelationshipModel (JSON)
└── character-relationships:{characterId} → List<relationshipId> (JSON array)
```

#### Data Flow

```
CREATE CHARACTER:
1. Generate UUID for characterId
2. Build key: "character:{realmId}:{characterId}"
3. Save to Redis via Dapr SaveStateAsync
4. Add characterId to "realm-index:{realmId}" list
5. Add realmId to "character-global-index:{characterId}"
6. Publish CharacterCreatedEvent + CharacterRealmJoinedEvent

GET CHARACTER (by ID only):
1. Lookup realmId from "character-global-index:{characterId}"
2. Build key: "character:{realmId}:{characterId}"
3. Load character from Redis
(Two round trips required!)

GET CHARACTERS BY REALM:
1. Load ALL characterIds from "realm-index:{realmId}"
2. For EACH characterId:
   - Build key: "character:{realmId}:{characterId}"
   - Load character from Redis (N+1 queries!)
3. Filter in memory
4. Paginate in memory
```

### 1.2 Relationship Storage Architecture

```
CREATE RELATIONSHIP:
1. Verify both characters exist (2 lookups)
2. Generate relationshipId
3. Save to "relationship:{relationshipId}"
4. Add to "character-relationships:{char1}" index
5. Add to "character-relationships:{char2}" index
6. Publish RelationshipCreatedEvent
(No uniqueness check for {char1, char2, typeId}!)

LIST RELATIONSHIPS:
1. Load ALL relationshipIds from "character-relationships:{charId}"
2. For EACH relationshipId:
   - Load relationship from Redis (N+1 queries!)
3. Filter by type/ended status in memory
4. Paginate in memory
```

---

## 2. Critical Issue: Redis for Persistent Game Data

### 2.1 The Problem

**Characters use Redis (`state.redis`) which is fundamentally inappropriate for persistent game world data.**

```yaml
# provisioning/dapr/components/character-statestore.yaml
spec:
  type: state.redis
  version: v1
  metadata:
  - name: redisHost
    value: bannou-redis:6379
```

#### Why This Is Critical

| Aspect | Redis Default | Impact on Characters |
|--------|--------------|---------------------|
| **Persistence** | None (in-memory only) | All characters lost on restart |
| **Durability** | No write-ahead log | Data loss on crash |
| **Recovery** | RDB snapshots (if enabled) | Minutes of lost data possible |
| **Consistency** | Eventual | Concurrent updates may conflict |

**Comparison with Accounts Service**:
```yaml
# provisioning/dapr/components/mysql-accounts-statestore.yaml
spec:
  type: state.mysql  # ✅ Durable, ACID-compliant
```

### 2.2 The Game Design Perspective

From the Arcadia knowledge base:

> "Characters are independent world assets (not owned by accounts)... NPCs continue living whether players are online or offline... Characters maintain consistent personality and goals across sessions."

**Characters are NOT ephemeral session data.** They are:
- Long-lived game world entities
- NPCs with persistent memories and relationships
- Economic participants with transaction history
- Multi-generational family trees

**Redis is designed for**:
- Session tokens (TTL-managed)
- Cache invalidation
- Pub/sub messaging
- Leaderboards (sorted sets)

**Redis is NOT designed for**:
- Primary database for persistent entities
- Complex queries across relationships
- ACID transactions for financial/game economy
- Long-term audit trails

### 2.3 Recommended Migration

**Option A: MySQL via Dapr State Store** (Recommended)
```yaml
# NEW: provisioning/dapr/components/mysql-character-statestore.yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: character-statestore
spec:
  type: state.mysql
  version: v1
  metadata:
  - name: connectionString
    value: "guest:guest@tcp(character-db:3306)/?allowNativePasswords=true"
  - name: schemaName
    value: "characters"
  - name: tableName
    value: "state"
```

**Benefits**:
- ACID compliance for character updates
- Automatic persistence to disk
- Point-in-time recovery possible
- Native SQL queries for complex filtering
- Consistent with Accounts service pattern

**Option B: PostgreSQL** (Alternative)
```yaml
spec:
  type: state.postgresql
  version: v1
```

**Benefits**:
- JSONB for flexible metadata storage
- Better query optimization
- Native UUID support
- Array types for indexes

### 2.4 Migration Complexity Assessment

| Aspect | Effort | Notes |
|--------|--------|-------|
| State store config | Low | Change YAML file |
| Code changes | None | Dapr abstracts storage |
| Data migration | Medium | Export/import existing |
| Testing | Medium | Verify all operations |
| Docker Compose | Low | Add MySQL container |

---

## 3. Sharding Analysis: What It Actually Does

### 3.1 Current "Sharding" Implementation

The implementation uses **application-level key prefixing**, NOT true database sharding.

```csharp
// CharacterService.cs:722-723
private static string BuildCharacterKey(string realmId, string characterId)
    => $"{CHARACTER_KEY_PREFIX}{realmId}:{characterId}";
// Result: "character:omega-realm-123:char-456"
```

#### What This Achieves

✅ **Logical Grouping**: Characters in same realm have co-located keys
✅ **Efficient Realm Queries**: `realm-index:{realmId}` enables fast listing
✅ **Namespace Isolation**: No key collisions between realms
✅ **Clear Data Organization**: Easy to understand key structure

#### What This Does NOT Achieve

❌ **Physical Data Separation**: All data still in single Redis instance
❌ **Horizontal Scaling**: Cannot distribute realms across nodes
❌ **True Sharding**: No consistent hashing or partition routing
❌ **Realm-Level Isolation**: No separate Redis instances per realm

### 3.2 True Sharding Options

**Option A: Redis Cluster** (Limited Sharding)
```
Redis Cluster automatically distributes keys across nodes:
- Keys with same {hashtag} go to same node
- Use: "character:{realmId}:..." for co-location

Limitation: Still single logical database, just distributed
```

**Option B: Dapr Multi-State-Store** (Application Routing)
```yaml
# Different state stores per realm
- name: omega-character-statestore
  type: state.mysql
  connectionString: "...@omega-db..."

- name: arcadia-character-statestore
  type: state.mysql
  connectionString: "...@arcadia-db..."
```

```csharp
// Service routes to appropriate store
var stateStore = GetStateStoreForRealm(realmId);
await _daprClient.SaveStateAsync(stateStore, key, data);
```

**Option C: Vitess/CockroachDB** (True Distributed SQL)
```yaml
spec:
  type: state.cockroachdb
  # Automatic sharding by partition key
```

### 3.3 Current Design Assessment

**Verdict**: The current implementation is **acceptable for development and early production** but will not scale to 100,000+ characters per realm without modification.

**Recommended Path**:
1. Keep current key structure (it's well-designed)
2. Migrate to MySQL for persistence (immediate)
3. Add true sharding when scale requires (future)

---

## 4. Performance Analysis at Scale

### 4.1 Current Query Patterns

#### GetCharactersByRealmInternalAsync - O(n) Memory Loading

```csharp
// CharacterService.cs:765-791
var characterIds = await _daprClient.GetStateAsync<List<string>>(
    CHARACTER_STATE_STORE, realmIndexKey, ...);

// Load ALL characters into memory
foreach (var characterId in characterIds)
{
    var character = await _daprClient.GetStateAsync<CharacterModel>(...);
    // Apply filters in memory
    if (statusFilter.HasValue && character.Status != statusFilter.Value) continue;
}
```

**Problem at 100k Characters**:
- 100,000 character IDs loaded into List<string> (~2-4 MB)
- 100,000 individual GetStateAsync calls (N+1 query pattern)
- All filtering done in memory AFTER loading
- No benefit from database indexes

**Performance Impact**:
| Characters | Memory | Round Trips | Estimated Time |
|-----------|--------|-------------|----------------|
| 1,000 | 40 KB | 1,001 | ~2 seconds |
| 10,000 | 400 KB | 10,001 | ~20 seconds |
| 100,000 | 4 MB | 100,001 | ~3+ minutes |

#### ListRelationshipsAsync - Same N+1 Pattern

```csharp
// CharacterService.cs:538-571
foreach (var relationshipId in relationshipIds)
{
    var relationship = await _daprClient.GetStateAsync<RelationshipModel>(...);
    // Filter in memory
    if (!body.IncludeEnded && relationship.EndedAt.HasValue) continue;
}
```

### 4.2 Solutions

#### Solution A: Bulk State Operations (Immediate)

Dapr supports `GetBulkStateAsync`:

```csharp
// BEFORE: N+1 queries
foreach (var id in characterIds)
{
    var character = await _daprClient.GetStateAsync<CharacterModel>(...);
}

// AFTER: Single bulk query
var keys = characterIds.Select(id => BuildCharacterKey(realmId, id)).ToList();
var bulkResults = await _daprClient.GetBulkStateAsync(CHARACTER_STATE_STORE, keys, ...);
var characters = bulkResults
    .Where(r => r.Value != null)
    .Select(r => JsonSerializer.Deserialize<CharacterModel>(r.Value));
```

**Impact**: Reduces N+1 to 2 queries (index + bulk load)

#### Solution B: Cursor-Based Pagination (Medium-term)

Replace offset pagination with cursor pagination:

```yaml
# Schema change
ListCharactersRequest:
  properties:
    cursor:
      type: string
      nullable: true
      description: Opaque cursor for next page
    limit:
      type: integer
      default: 20
```

```csharp
// Implementation
var startIndex = DecodeCursor(body.Cursor);
var pagedIds = characterIds.Skip(startIndex).Take(body.Limit + 1);
var hasMore = pagedIds.Count() > body.Limit;
var nextCursor = hasMore ? EncodeCursor(startIndex + body.Limit) : null;
```

#### Solution C: Database-Level Filtering (Requires SQL)

With MySQL state store, use Dapr query API:

```csharp
// Dapr supports SQL-like queries on state stores
var query = new Dictionary<string, object>
{
    ["filter"] = new { EQ = new { realmId = body.RealmId } },
    ["sort"] = new[] { new { key = "createdAt", order = "DESC" } },
    ["page"] = new { limit = body.PageSize, token = body.Cursor }
};

var results = await _daprClient.QueryStateAsync<CharacterModel>(
    CHARACTER_STATE_STORE, query);
```

**Note**: Query API requires state store support (MySQL/PostgreSQL/CosmosDB)

---

## 5. Relationship Type System Design

### 5.1 Current State

The schema references `relationshipTypeId` as a foreign key:

```yaml
# character-api.yaml:455-458
relationshipTypeId:
  type: string
  format: uuid
  description: Relationship type ID (foreign key to future RelationshipType service)
```

**But there is no RelationshipType service.** This creates:
- No validation of relationship types
- No way to discover available types
- No hierarchical type support (parent types)
- No uniqueness enforcement for {char1, char2, typeId}

### 5.2 User Requirements

From the original request:

> "Relationship types need to be defined somewhat like network-level enums, a single table which is used as a foreign key in other tables."
>
> "The relationship types to be hierarchical and be able to set 'parent type' so that 'son' also matches 'child' (its parent type)"
>
> "Keys have to be unique by the entire combination of '{char id 1, char id 2, relationship type id}'"

### 5.3 Proposed RelationshipType Service

#### Schema Design

```yaml
# schemas/relationship-type-api.yaml
openapi: 3.0.0
info:
  title: Bannou RelationshipType Service API
  version: 1.0.0

paths:
  /relationship-type/create:
    post:
      summary: Create relationship type
      x-permissions:
        - role: admin
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateRelationshipTypeRequest'

  /relationship-type/get:
    post:
      summary: Get relationship type by ID

  /relationship-type/list:
    post:
      summary: List all relationship types with hierarchy

  /relationship-type/by-parent:
    post:
      summary: Get all child types for a parent type

components:
  schemas:
    CreateRelationshipTypeRequest:
      type: object
      required:
        - name
        - code
      properties:
        name:
          type: string
          description: Display name (e.g., "Son", "Daughter", "Friend")
        code:
          type: string
          description: Unique code identifier (e.g., "SON", "DAUGHTER", "FRIEND")
        parentTypeId:
          type: string
          format: uuid
          nullable: true
          description: Parent type for hierarchy (e.g., "CHILD" is parent of "SON")
        bidirectional:
          type: boolean
          default: false
          description: If true, relationship applies both directions
        inverseTypeId:
          type: string
          format: uuid
          nullable: true
          description: Inverse type (e.g., "PARENT" is inverse of "CHILD")
        metadata:
          type: object
          additionalProperties: true

    RelationshipTypeResponse:
      type: object
      required:
        - typeId
        - name
        - code
      properties:
        typeId:
          type: string
          format: uuid
        name:
          type: string
        code:
          type: string
        parentTypeId:
          type: string
          format: uuid
          nullable: true
        bidirectional:
          type: boolean
        inverseTypeId:
          type: string
          format: uuid
          nullable: true
        childTypes:
          type: array
          items:
            $ref: '#/components/schemas/RelationshipTypeResponse'
          description: Recursive child types (populated on list)
```

#### Example Type Hierarchy

```
FAMILY (abstract root)
├── PARENT
│   ├── MOTHER
│   ├── FATHER
│   └── STEP_PARENT
├── CHILD
│   ├── SON
│   ├── DAUGHTER
│   └── STEP_CHILD
├── SIBLING
│   ├── BROTHER
│   ├── SISTER
│   └── HALF_SIBLING
└── SPOUSE
    ├── HUSBAND
    ├── WIFE
    └── PARTNER

SOCIAL
├── FRIEND
│   ├── CLOSE_FRIEND
│   └── ACQUAINTANCE
├── ENEMY
│   └── RIVAL
└── MENTOR
    └── APPRENTICE

ECONOMIC
├── EMPLOYER
│   └── BUSINESS_OWNER
├── EMPLOYEE
│   └── APPRENTICE
└── TRADE_PARTNER
```

### 5.4 Hierarchy Query Pattern

```csharp
// Check if relationship matches type or any parent type
public async Task<bool> MatchesTypeOrParentAsync(Guid relationshipTypeId, Guid queryTypeId)
{
    if (relationshipTypeId == queryTypeId) return true;

    var type = await GetRelationshipTypeAsync(relationshipTypeId);
    while (type?.ParentTypeId != null)
    {
        if (type.ParentTypeId == queryTypeId) return true;
        type = await GetRelationshipTypeAsync(type.ParentTypeId.Value);
    }
    return false;
}

// Usage: Find all "CHILD" relationships (includes SON, DAUGHTER, etc.)
var childTypeId = GetTypeIdByCode("CHILD");
var relationships = await ListRelationshipsAsync(characterId);
var childRelationships = relationships
    .Where(r => MatchesTypeOrParentAsync(r.RelationshipTypeId, childTypeId).Result);
```

### 5.5 Composite Uniqueness Enforcement

**Current Gap**: No validation that `{char1, char2, typeId}` is unique.

**Solution**: Add uniqueness check in CreateRelationshipAsync:

```csharp
// CharacterService.cs - Enhanced CreateRelationshipAsync
public async Task<(StatusCodes, RelationshipResponse?)> CreateRelationshipAsync(...)
{
    // NEW: Check for duplicate relationship
    var existingRelationships = await GetRelationshipsForCharacterAsync(
        body.CharacterId, cancellationToken);

    var duplicate = existingRelationships.FirstOrDefault(r =>
        r.RelatedCharacterId == body.RelatedCharacterId.ToString() &&
        r.RelationshipTypeId == body.RelationshipTypeId.ToString() &&
        !r.EndedAt.HasValue);  // Active relationships only

    if (duplicate != null)
    {
        _logger.LogWarning("Duplicate relationship attempted: {Char1} - {Type} - {Char2}",
            body.CharacterId, body.RelationshipTypeId, body.RelatedCharacterId);
        return (StatusCodes.Conflict, null);
    }

    // Continue with creation...
}
```

**Alternative**: Composite key in storage
```csharp
// Use composite key that prevents duplicates
var relationshipKey = $"relationship:{char1}:{char2}:{typeId}";
```

---

## 6. Future Data Services Roadmap

### 6.1 Required Data Services for Arcadia

Based on the knowledge base analysis, the following data services are needed:

| Priority | Service | Dependencies | Data Characteristics |
|----------|---------|--------------|---------------------|
| 🔴 P0 | **RelationshipType** | None | Static reference data, cached |
| 🔴 P0 | **Species** | None | Static reference data, cached |
| 🟡 P1 | **Realm** | None | Semi-static, partition key source |
| 🟡 P1 | **Location** | Realm | High-frequency updates, realm-sharded |
| 🟡 P1 | **Trait** | Species | Genetics data, static per species |
| 🟢 P2 | **Memory** | Character | Multi-tiered, event-sourced |
| 🟢 P2 | **Inventory** | Character, Realm | High-frequency updates |
| 🟢 P2 | **Economy** | Character, Realm | Transaction log, audit trail |
| 🔵 P3 | **Behavior** | Character, RelationshipType | YAML DSL storage |
| 🔵 P3 | **Guardian Spirit** | Account, Character | Cross-realm references |

### 6.2 Service Design Patterns

#### Pattern A: Static Reference Data (RelationshipType, Species)

```
Characteristics:
- Created by administrators, rarely changes
- Read-heavy (1000:1 read:write ratio)
- Should be cached aggressively
- Network-level enum pattern

Storage: MySQL (durable) + Redis cache
Pattern: Read-through cache with TTL
```

```csharp
// Caching pattern for static data
public class RelationshipTypeService
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromHours(1);

    public async Task<RelationshipTypeResponse?> GetTypeAsync(Guid typeId)
    {
        var cacheKey = $"rel-type:{typeId}";
        if (!_cache.TryGetValue(cacheKey, out RelationshipTypeModel type))
        {
            type = await _daprClient.GetStateAsync<RelationshipTypeModel>(...);
            if (type != null)
            {
                _cache.Set(cacheKey, type, _cacheDuration);
            }
        }
        return MapToResponse(type);
    }
}
```

#### Pattern B: Realm-Sharded Dynamic Data (Location, Character)

```
Characteristics:
- High volume (100,000+ entities per realm)
- Frequent updates (location updates every second)
- Must query within realm efficiently
- Cross-realm queries rare

Storage: MySQL/PostgreSQL with realm-partitioned tables
Pattern: Realm-based key prefix, bulk operations
```

```csharp
// Key structure
var locationKey = $"location:{realmId}:{entityId}";

// Efficient realm queries
var realmLocations = await _daprClient.QueryStateAsync<LocationModel>(
    LOCATION_STATE_STORE,
    new { filter = new { EQ = new { realmId = targetRealmId } } });
```

#### Pattern C: Event-Sourced Data (Memory, Economy)

```
Characteristics:
- Audit trail required (economic transactions)
- Point-in-time reconstruction needed
- Append-only writes
- Eventual consistency acceptable

Storage: Event store + materialized views
Pattern: CQRS with event sourcing
```

```csharp
// Event sourcing pattern
public async Task RecordTransactionAsync(TransactionEvent txn)
{
    // Append to event log (never deleted)
    await _daprClient.PublishEventAsync("economy-pubsub", "transaction", txn);

    // Update materialized view (can be rebuilt)
    await UpdateAccountBalanceAsync(txn.AccountId, txn.Amount);
}

// Rebuild from events
public async Task RebuildAccountStateAsync(Guid accountId)
{
    var events = await LoadEventsForAccountAsync(accountId);
    var state = events.Aggregate(new AccountState(), ApplyEvent);
    await SaveMaterializedStateAsync(accountId, state);
}
```

### 6.3 Realm Service Design

The Realm service is foundational - it defines the partition key for many other services.

```yaml
# schemas/realm-api.yaml
components:
  schemas:
    RealmResponse:
      properties:
        realmId:
          type: string
          format: uuid
        name:
          type: string
        code:
          type: string
          enum: [omega, arcadia, fantasia]
        status:
          type: string
          enum: [active, maintenance, archived]
        characterCount:
          type: integer
          description: Denormalized count for monitoring
        maxCharacters:
          type: integer
          description: Capacity limit for load balancing
        shardId:
          type: string
          description: Physical database shard identifier
```

### 6.4 Location Service Design

Location updates are the highest-frequency writes in the system.

```yaml
# schemas/location-api.yaml
components:
  schemas:
    LocationUpdateRequest:
      required:
        - entityId
        - realmId
        - position
      properties:
        entityId:
          type: string
          format: uuid
          description: Character or object ID
        realmId:
          type: string
          format: uuid
        position:
          $ref: '#/components/schemas/Vector3'
        rotation:
          $ref: '#/components/schemas/Quaternion'
        velocity:
          $ref: '#/components/schemas/Vector3'
        timestamp:
          type: string
          format: date-time

    Vector3:
      type: object
      properties:
        x: { type: number, format: float }
        y: { type: number, format: float }
        z: { type: number, format: float }
```

**Optimization**: Use Redis for location data (acceptable loss on restart) with periodic MySQL snapshots for "last known position" recovery.

---

## 7. Recommendations and Priority Order

### 7.1 Immediate Actions (Week 1-2)

#### 🔴 CRITICAL: Migrate Character Storage to MySQL

**Why**: Redis data loss is unacceptable for persistent game entities.

**Steps**:
1. Create `provisioning/dapr/components/mysql-character-statestore.yaml`
2. Add `character-db` MySQL container to docker-compose.yml
3. Run migration script to export Redis → MySQL
4. Update test configurations
5. Verify all tests pass

**Effort**: 2-3 days

#### 🔴 CRITICAL: Add Bulk State Operations

**Why**: Current N+1 pattern will timeout at scale.

**Steps**:
1. Replace individual GetStateAsync loops with GetBulkStateAsync
2. Update GetCharactersByRealmInternalAsync
3. Update ListRelationshipsAsync
4. Add performance benchmarks to tests

**Effort**: 1 day

### 7.2 Short-term Actions (Week 3-4)

#### 🟡 Add RelationshipType Service

**Why**: Required for relationship validation and hierarchy queries.

**Steps**:
1. Create `schemas/relationship-type-api.yaml`
2. Run generation script
3. Implement RelationshipTypeService
4. Add parent type hierarchy support
5. Integrate with Character service validation

**Effort**: 3-4 days

#### 🟡 Add Composite Uniqueness Validation

**Why**: Prevent duplicate relationships.

**Steps**:
1. Add duplicate check in CreateRelationshipAsync
2. Consider composite key structure
3. Add unit tests for uniqueness

**Effort**: 1 day

### 7.3 Medium-term Actions (Week 5-8)

#### 🟢 Add Species Service

**Why**: Characters require species for genetics system.

#### 🟢 Add Realm Service

**Why**: Foundation for realm-based sharding.

#### 🟢 Add Cursor Pagination

**Why**: Offset pagination breaks at large scales.

### 7.4 Long-term Actions (Future)

#### 🔵 True Database Sharding

When scale requires, implement:
- Multiple MySQL instances per realm
- Consistent hashing for partition routing
- Cross-shard query federation

#### 🔵 Event Sourcing for Economy

Required for:
- Transaction audit trails
- Economic analytics
- Fraud detection

---

## 8. Implementation Alternatives

### 8.1 Storage Backend Comparison

| Backend | Persistence | Query Capability | Scale | Bannou Fit |
|---------|-------------|------------------|-------|------------|
| **Redis** | ⚠️ Optional | ❌ Limited | ✅ Excellent | Cache/Sessions only |
| **MySQL** | ✅ ACID | ✅ SQL | ✅ Good | ✅ Primary choice |
| **PostgreSQL** | ✅ ACID | ✅ SQL + JSONB | ✅ Good | ✅ Alternative |
| **CockroachDB** | ✅ ACID | ✅ SQL | ✅ Excellent | 🔵 Future scale |
| **MongoDB** | ✅ WiredTiger | ⚠️ Document | ✅ Excellent | ❌ Not Dapr optimized |

### 8.2 Sharding Strategy Comparison

| Strategy | Complexity | Dapr Support | Recommendation |
|----------|-----------|--------------|----------------|
| **Key Prefixing** | Low | ✅ Native | Current approach, keep |
| **Multi-State-Store** | Medium | ✅ Native | Good for realm isolation |
| **Redis Cluster** | Medium | ✅ Native | Not for primary data |
| **Vitess** | High | ⚠️ Custom | Future enterprise scale |
| **CockroachDB** | Medium | ✅ Native | Simplest true sharding |

### 8.3 Decision Matrix

**For Character Service Storage**:
```
                          Redis   MySQL   PostgreSQL
Persistence Required?     ❌ No   ✅ Yes  ✅ Yes
Complex Queries Needed?   ❌ No   ✅ Yes  ✅ Yes
ACID Transactions?        ❌ No   ✅ Yes  ✅ Yes
Existing Dapr Experience? ✅ Yes  ✅ Yes  ⚠️ Some
Team Familiarity?         ?       ?       ?

RECOMMENDATION: MySQL (consistent with Accounts)
```

---

## 9. Final Decisions and Implementation Plan

### 9.1 User Decisions Summary

| Decision Area | Choice | Rationale |
|--------------|--------|-----------|
| **Character Storage** | Migrate to MySQL | Consistent with Accounts pattern, ACID compliance |
| **RelationshipType** | Seed at startup | Simpler than full CRUD, types rarely change |
| **Sharding Approach** | Keep key prefixing, plan for future | Current approach acceptable, design for migration |
| **Next Services** | RelationshipType + Species | Foundation services with few dependencies |

### 9.2 Implementation Phases

#### Phase 1: Character Storage Migration (2-3 days) 🔴 CRITICAL
1. Add character-db MySQL container to docker-compose
2. Create mysql-character-statestore.yaml Dapr component
3. Remove Redis character-statestore.yaml
4. Update MySQL init script for characters database
5. Verify all tests pass with MySQL backend

#### Phase 2: Bulk State Operations (1-2 days) 🔴 HIGH
1. Replace N+1 loops in GetCharactersByRealmInternalAsync
2. Replace N+1 loops in ListRelationshipsAsync
3. Use Dapr GetBulkStateAsync for batch loading
4. Add performance tests with 100+ entities

#### Phase 3: RelationshipType Service (3-4 days) 🟡 HIGH
1. Create schemas/relationship-type-api.yaml
2. Create provisioning/seed-data/relationship-types.yaml
3. Generate and implement lib-relationship-type service
4. Add hierarchy support with parentTypeId
5. Integrate validation into Character service

#### Phase 4: Species Service (2 days) 🟢 MEDIUM
1. Create schemas/species-api.yaml
2. Generate and implement lib-species service
3. Add realm-based filtering
4. Integrate validation into Character service

#### Phase 5: Composite Uniqueness (1 day) 🟢 MEDIUM
1. Add composite key index: relationship-composite:{char1}:{char2}:{typeId}
2. Check uniqueness before creating relationship
3. Clean up index on relationship end/delete

### 9.3 Timeline Summary

| Phase | Duration | Cumulative |
|-------|----------|------------|
| Phase 1 | 2-3 days | Day 3 |
| Phase 2 | 1-2 days | Day 5 |
| Phase 3 | 3-4 days | Day 9 |
| Phase 4 | 2 days | Day 11 |
| Phase 5 | 1 day | Day 12 |

**Total: 9-12 days**

---

## Appendices

### A. Key Files Reference

| File | Purpose |
|------|---------|
| `lib-character/CharacterService.cs` | Main service implementation |
| `schemas/character-api.yaml` | API specification |
| `provisioning/dapr/components/character-statestore.yaml` | State store config |
| `lib-character.tests/` | Unit tests |
| `http-tester/Tests/CharacterTestHandler.cs` | Integration tests |

### B. Performance Benchmarks (To Be Added)

TODO: Add benchmarks after bulk operations implementation.

### C. Migration Scripts (To Be Created)

TODO: Create Redis → MySQL migration script.

---

**Document History**:
- 2025-12-17: Architectural refactoring - Relationship service extraction, deprecation system, VOID/NULL special entries
- 2025-12-16: Initial comprehensive analysis created
