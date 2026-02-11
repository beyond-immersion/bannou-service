# Event Actor Resource Access Pattern

> **Status**: Largely Implemented (Phases 1-4 complete)
> **Created**: 2026-02-05
> **Priority**: High
> **Last Updated**: 2026-02-07
> **Related Documents**:
> - `~/.claude/plans/variable-providers-storyline-quest.md` - Variable Providers plan
> - `docs/planning/REGIONAL_WATCHERS_BEHAVIOR.md` - Regional Watchers (Gods) design
> - `docs/plugins/ACTOR.md` - Actor deep dive (data access pattern selection)
> - `docs/reference/SERVICE-HIERARCHY.md` - Variable Provider Factory pattern
> - `docs/plugins/RESOURCE.md` - Resource plugin deep dive

---

## Problem Statement

Regional Watchers (Event Brain actors like "Gods" in Arcadia) need to access data about **other characters** to make narrative decisions. Unlike Character Brain actors that access data about **themselves** via Variable Providers, watchers orchestrate storylines for arbitrary characters they discover through queries.

**The core question**: How should a watcher access a character's personality, history, encounters, and storyline participation when evaluating that character for narrative potential?

---

## Document Context Summary

### From `variable-providers-storyline-quest.md`

The Variable Providers plan establishes that:

1. **Character-based actors** use Variable Providers for self-data:
   - `${personality.aggression}` - from `PersonalityProvider`
   - `${storyline.is_participant}` - from `StorylineProvider`
   - `${quest.active_count}` - from `QuestProvider`

2. **Non-character actors** (watchers) were planned to access other characters via Resource plugin:
   > "Orchestrators access other characters' data via **Resource plugin snapshots**, NOT arbitrary `service_call`."

3. The proposed ABML pattern was a **command**, not a provider:
   ```yaml
   - resource_snapshot:
       entity_type: character
       entity_id: "${candidate.id}"
       include: [storyline_participation, quest_state]
       result_variable: candidate_snapshot

   # Then access via variable
   condition: "${candidate_snapshot.storyline_participation.active_count} == 0"
   ```

### From `REGIONAL_WATCHERS_BEHAVIOR.md`

Regional Watchers (Gods) are long-running Event Brain actors that:

1. **Search for narrative opportunities** using three patterns:
   - Pattern A: Scenarios-First (query scenarios, find matching characters)
   - Pattern B: Characters-First (find characters, query matching scenarios)
   - Pattern C: Event-Triggered (react to events, check triggers)

2. **The actor does the searching, not the plugins** - this is a key architectural principle

3. **Example flows** show watchers querying characters then evaluating them:
   ```yaml
   - api_call:
       service: character
       endpoint: /character/query
       data:
         realmIds: "${my_realms}"
         filters:
           hasRelationships: true
           notInActiveStoryline: true
       result: potential_victims

   # Score each by tragic potential
   - foreach:
       collection: "${potential_victims}"
       as: candidate
       do:
         - set:
             "candidate.tragic_potential": >
               ${score_tragic_potential(candidate)}
   ```

4. **Compression feedback loop** mentions mining archives:
   ```yaml
   - api_call:
       service: resource
       endpoint: /resource/query-archives
       data:
         resourceType: "character"
         realmId: "${my_realm}"
       result: archives
   ```

### Data Access Patterns (from ACTOR.md and SERVICE-HIERARCHY.md)

The established hybrid data access approach:

| Data Type | Pattern | Rationale |
|-----------|---------|-----------|
| Cognition (memories) | Shared datastore | High-frequency, behavior-owned |
| Character traits | Variable Providers | Read-heavy, infrequent changes |
| Game state (currency) | API calls | Authoritative, consistency critical |
| Spatial context | Cached Provider | Coarse-grained, periodic refresh |

Key principles:
- Variable Providers are **synchronous** - data must be pre-loaded
- Providers receive response data in constructor, cache handles async loading
- `IServiceScopeFactory` pattern for Singleton caches accessing Scoped clients

### From `docs/plugins/RESOURCE.md`

The Resource plugin provides three core capabilities:

1. **Reference Tracking** - Track L3/L4 references to L2 resources
2. **Cleanup Coordination** - Coordinate cascading deletes
3. **Hierarchical Compression** - Archive resources with all dependent data

**Key structures:**

```
ResourceArchiveModel / ResourceSnapshotModel {
  ArchiveId / SnapshotId: Guid
  ResourceType: string ("character")
  ResourceId: Guid
  Entries: [
    {
      SourceType: string ("character-personality")
      ServiceName: string
      Data: string (Base64 GZip JSON)
      CollectedAt: DateTimeOffset
    }
  ]
}
```

**Compression callbacks** registered by L4 services:
- `character-base` (priority 0) - core character data
- `character-personality` (priority 10) - traits, combat preferences
- `character-history` (priority 20) - participations, backstory
- `character-encounter` (priority 30) - encounter history

**Snapshots vs Archives:**
- **Snapshots**: Ephemeral, Redis-stored, TTL-based (default 1 hour), for living entities
- **Archives**: Permanent, MySQL-stored, versioned, for dead/compressed entities

---

## The Proposed Correction

Instead of an ABML command (`resource_snapshot:`) that stores results in a variable, create a **Variable Provider** that the Resource plugin provides.

### Why Provider Pattern Is Better

| Aspect | ABML Command | Provider Pattern |
|--------|--------------|------------------|
| Consistency | Different syntax than other data access | Same `${...}` syntax everywhere |
| Expression use | Store in variable, then access | Direct access in conditions |
| Caching | Must implement separately | Follows established cache pattern |
| Composability | Awkward variable nesting | Natural path navigation |
| Existing patterns | New paradigm to learn | Matches personality/backstory |

### The Key Insight: Nested Archives

Archives contain **hierarchical data** because compression callbacks gather data from multiple services:

```
Character Archive
├── character-base (from Character service)
├── character-personality (from CharacterPersonality service)
├── character-history (from CharacterHistory service)
└── character-encounter (from CharacterEncounter service)
```

A provider can expose this hierarchy naturally:
```yaml
${candidate.personality.aggression}
${candidate.history.participations[0].role}
${candidate.encounters.recent_count}
```

The watcher "un-nests" the archive entries and accesses each as if it came from its own specialized provider.

---

## Clarified Design Principles

These principles were established through design discussion and clarify the architectural intent:

### 1. Dynamic IDs, Static Types

Event actor behaviors declare which **resource types** they work with (via templates), but specific **resource IDs** change constantly as the watcher shifts focus.

```yaml
metadata:
  id: god-of-tragedy
  resource_templates:
    - character        # I work with character archives
    - character-history # I also need history data
    - quest            # And quest data
  # But NO static IDs - those are runtime decisions
```

The typical event actor loop:
```
find something interesting
  → make interesting things happen
    → keep watching
      → get bored
        → move on to something else
```

One behavior, continuous loop, changing targets.

### 2. Event Actor Capabilities (Distinct from Character Actors)

| Capability | Character Actor | Event Actor |
|------------|-----------------|-------------|
| Live providers (self-data) | ✅ Yes | ❌ No |
| Archive/snapshot data | ❌ No (uses live) | ✅ Yes |
| Receive events | ✅ Yes | ✅ Yes |
| Communicate with other Actors | Limited | ✅ Primary mechanism |
| Push commands to characters | Via self | Via Actor instances |

**Event actors don't have "live access"** - that's what character actors get because they're bound to one character. Event actors observe and orchestrate via:
- **Events**: Receive domain events (character.died, relationship.formed, etc.)
- **Actor Communication**: Send commands to Actor instances handling specific characters
- **Archive Data**: Read-only snapshots of any character/quest/etc.

### 3. Templates = Type Safety + Filtering

Templates serve three purposes:

**A. Compile-time validation:**
```yaml
# Behavior declares templates
resource_templates: [character, character-personality]

# ABML can only access paths defined in those templates
condition: "${candidate.personality.aggression} > 0.5"  # Valid
condition: "${candidate.history.participations}"        # Error: history not declared
```

**B. Request filtering:**
```csharp
// Resource plugin receives filter
var response = await _resourceClient.GetSnapshotAsync(new GetSnapshotRequest
{
    ResourceType = "character",
    ResourceId = characterId,
    FilterSourceTypes = ["character-base", "character-personality"]  // Only these
});
```

**C. Response optimization:**
- Full snapshot stored in cache (all compression callbacks)
- Response filtered to only requested source types
- Different behaviors can share cached snapshots with different filters

### 4. Hierarchical Filtering in Resource Plugin

The snapshot is stored complete but served filtered:

```
Stored Snapshot (Redis, full):
├── character-base
├── character-personality
├── character-history
├── character-encounter
└── character-storyline (if registered)

Request A (god-of-tragedy):
  filter: [character-base, character-personality, character-history]
  Response: Only those 3 entries

Request B (god-of-monsters):
  filter: [character-base, character-encounter]
  Response: Only those 2 entries

Both use same cached snapshot, different filtered responses.
```

### 5. Event Actor → Character Actor Communication

Event actors orchestrate by sending commands to Actor instances:

```yaml
# Event actor behavior
flows:
  trigger_tragedy:
    # Find the actor instance for this character (if running)
    - actor_command:
        target_character: "${victim.id}"
        command: start_cutscene
        data:
          cutscene_id: "tragedy_revelation"

    # Or trigger a quest
    - actor_command:
        target_character: "${victim.id}"
        command: offer_quest
        data:
          quest_code: "REVENGE_PATH"
```

The character's Actor instance receives and handles these commands, potentially using its live providers to make decisions.

---

## Open Questions

### Q1: How Are Archives/Snapshots Loaded? ✅ CLARIFIED

Variable Providers are **synchronous** (`GetValue` returns `object?`). Data must be pre-loaded before the provider is registered.

**Resolution**: Hybrid approach combining template declaration + explicit loading:

**Static declaration of resource TYPES (not IDs):**
```yaml
metadata:
  id: god-of-tragedy
  resource_templates:
    - character
    - character-personality
    - character-history
```

**Dynamic loading of specific IDs:**
```yaml
- load_resource_snapshot:
    type: character
    id: "${candidate.id}"
    as: candidate  # Provider name

# Now accessible as provider
condition: "${candidate.personality.aggression} > 0.5"
```

**Smart pre-population**: The ActorRunner sees "this behavior uses character templates" and can:
- Pre-warm caches based on recent queries
- Batch-load when multiple IDs are requested
- Make snapshot loads "usually instant" by hitting local cache

The template declaration enables compile-time validation that ABML only accesses declared resource types.

### Q2: Provider Naming and Namespace ✅ CLARIFIED

~~How should the provider expose archive entries?~~

**Resolution**: The `IResourceArchive` interface already provides proper typing and nesting.

**IResourceArchive interface** (`bannou-service/Archives/IResourceArchive.cs`):
```csharp
public interface IResourceArchive
{
    Guid ResourceId { get; }
    string ResourceType { get; }           // "character", "character-personality", etc.
    DateTimeOffset ArchivedAt { get; }
    int SchemaVersion { get; }
    IReadOnlyList<IResourceArchive> NestedArchives { get; }  // Hierarchical!
}
```

**Key insight**: Archives are **hierarchical via `NestedArchives`**, not just flat entries:
```
CharacterArchive (IResourceArchive)
├── ResourceType: "character"
├── [character-specific data]
└── NestedArchives:
    ├── CharacterPersonalityArchive (IResourceArchive)
    │   ├── ResourceType: "character-personality"
    │   └── [personality data]
    ├── CharacterHistoryArchive (IResourceArchive)
    │   ├── ResourceType: "character-history"
    │   └── [history data]
    └── CharacterEncounterArchive (IResourceArchive)
        ├── ResourceType: "character-encounter"
        └── [encounter data]
```

**Namespace = ResourceType**: Each nested archive's `ResourceType` IS the namespace. No prefix stripping needed.

**Provider access pattern**:
```yaml
# Access nested archives by their ResourceType
${candidate.character-personality.aggression}
${candidate.character-history.backstory.origin}

# Or if template defines short aliases (optional convenience)
${candidate.personality.aggression}  # alias for character-personality
```

**Template can define aliases** for convenience:
```yaml
resource_templates:
  - type: character-personality
    alias: personality  # Optional short name for ABML
  - type: character-history
    alias: history
```

**Implementation**: Provider walks `NestedArchives` to build namespace map. If Resource plugin isn't returning `IResourceArchive`, that needs to be fixed first.

### Q3: What About Live State Not in Archives? ✅ CLARIFIED

~~Compression callbacks gather **certain data** for archival. But watchers may need live state.~~

**Resolution**: Event actors **don't get live state access**. That's the fundamental distinction:

| Actor Type | Data Access |
|------------|-------------|
| Character Actor | Live providers (bound to one character) |
| Event Actor | Archive snapshots only (any character, point-in-time) |

**Why this is correct:**
- Event actors are **observers and orchestrators**, not participants
- They make decisions based on **snapshots** (what was true when they looked)
- If they need current state, they communicate with the **Actor instance** for that character
- The Actor instance has live providers and can respond with current state

**For storyline/quest participation**, two approaches:

**A. Register compression callbacks for Storyline/Quest:**
```yaml
# In storyline-api.yaml
x-compression-callback:
  resourceType: character
  sourceType: character-storyline
  compressEndpoint: /storyline/get-compress-data
  priority: 40
```
Snapshots then include storyline participation as an entry.

**B. Query via Actor communication:**
```yaml
# Event actor asks the character's Actor for current storyline state
- actor_query:
    target_character: "${candidate.id}"
    query: get_storyline_participation
    result: candidate_storylines
```

**Recommendation**: Option A for data that event actors commonly need (add compression callbacks for Storyline/Quest). Option B for rare/complex queries.

### Q4: Compression Callback Response Schema ✅ CLARIFIED

Each compression callback returns a different response type. How does the provider navigate these?

**Resolution**: Templates provide type safety at compile time.

**The template system:**

```csharp
// Template definition (compiled, provides type info)
public class CharacterPersonalityTemplate : IResourceTemplate
{
    public string SourceType => "character-personality";

    // Defines valid paths and their types
    public IReadOnlyDictionary<string, Type> Paths => new Dictionary<string, Type>
    {
        ["aggression"] = typeof(float),
        ["traits.AGGRESSION"] = typeof(float),
        ["combatPreferences.preferredStyle"] = typeof(string),
        // ... etc
    };

    // Deserialize with known type
    public object Deserialize(string compressedData)
    {
        var json = Decompress(compressedData);
        return BannouJson.Deserialize<CharacterPersonalityCompressData>(json)!;
    }
}
```

**How this works:**

1. **Behavior declares templates:**
   ```yaml
   resource_templates:
     - character-personality
     - character-history
   ```

2. **Compilation validates paths:**
   ```yaml
   # Valid - path exists in character-personality template
   condition: "${candidate.personality.aggression} > 0.5"

   # Compile error - path doesn't exist
   condition: "${candidate.personality.nonexistent_field}"
   ```

3. **Runtime uses typed deserialization:**
   ```csharp
   var template = _templates["character-personality"];
   var typedData = template.Deserialize(entry.Data);
   // Provider navigates typed object, not raw JSON
   ```

**Remaining question**: Where do templates live?
- Option A: Generated from compression response schemas
- Option B: Hand-written per resource type
- Option C: Discovered from compression callbacks at startup

**Recommendation**: Option A - generate templates from the `GetCompressDataResponse` types defined in each service's API schema.

### Q5: Multiple Character Access in Single Flow ✅ CLARIFIED

A watcher evaluating 10 candidates needs 10 archives. The template + dynamic ID pattern informs this.

**Resolution**: Three complementary fetch modes, configurable per event actor instance:

**Mode 1: Automatic fetch on access (lazy)**
When the behavior accesses data about something the actor doesn't have cached, automatically fetch it (and all supported connected types):
```yaml
# No explicit load needed - access triggers fetch
- cond:
    - when: "${candidate.personality.aggression} > 0.5"  # Auto-fetches if not cached
```
- Pro: Simplest ABML, no boilerplate
- Con: First access may have latency

**Mode 2: Implicit tracking (watch on store)**
When the behavior stores an ID to track (starts "watching" something), prefetch proactively:
```yaml
- set:
    current_target: "${candidate.id}"  # Actor now "tracking" this character
    # ActorRunner detects: "oh, it's storing a characterId, let's prefetch"
```
- Pro: Natural, matches mental model of "watching" something
- Con: Requires ActorRunner to understand variable semantics

**Mode 3: Explicit watch command**
Explicit command to declare intent:
```yaml
- watch:
    type: character
    id: "${candidate.id}"
    as: target  # Provider name

# Now target.* is available and will stay fresh
condition: "${target.personality.aggression} > 0.5"
```
- Pro: Clear intent, predictable behavior
- Con: More verbose

**All three can coexist**, configurable per event actor instance:
```yaml
metadata:
  id: god-of-tragedy
  fetch_mode: auto  # or "explicit" or "track_on_store"
  # Could also be "all" to enable all modes
```

**Prefetch optimization still applies**:
```yaml
# Explicit batch prefetch for known collections
- prefetch:
    type: character
    ids: "${candidates.map(c => c.id)}"

- foreach:
    collection: "${candidates}"
    as: candidate
    do:
      # Guaranteed cache hit regardless of fetch_mode
      condition: "${candidate.personality.aggression} > 0.5"
```

**Implementation note**: The ActorRunner maintains a "watched resources" set per actor instance. Watched resources are kept fresh (re-fetched on TTL expiry or invalidation event).

### Q6: Cache Invalidation and TTL ✅ CLARIFIED

Snapshots are point-in-time. How long should they be cached?

**Resolution**: Hierarchical configuration - global default, per-resource override.

**Configuration hierarchy** (most specific wins):

```yaml
# 1. Global default (actor-configuration.yaml)
ResourceSnapshotDefaultTtlMinutes:
  type: integer
  default: 5
  description: Default TTL for all resource snapshots
  x-env-name: ACTOR_RESOURCE_SNAPSHOT_DEFAULT_TTL_MINUTES

# 2. Per-resource-type override (in resource template definition)
# Example: character-personality-api.yaml
components:
  schemas:
    CharacterPersonalityCompressData:
      x-resource-template: true
      x-template-namespace: personality
      x-snapshot-ttl-minutes: 60  # Personality rarely changes - longer TTL
```

**Resolution order:**
1. If resource template defines `x-snapshot-ttl-minutes`, use that
2. Otherwise, use global `ResourceSnapshotDefaultTtlMinutes`

**Example TTLs by data volatility:**
| Resource Type | Suggested TTL | Rationale |
|---------------|---------------|-----------|
| character-personality | 60 min | Rarely changes, only through gameplay events |
| character-history | 30 min | Changes on major events |
| character-encounter | 10 min | Changes with gameplay interactions |
| character-storyline | 5 min | Active storylines change frequently |
| character-quest | 5 min | Quest progress changes frequently |

**Cache invalidation** (complement to TTL):
- Event-driven invalidation when source data changes
- `resource.compressed` event triggers cache clear for that resource
- Watched resources (Mode 2/3 from Q5) re-fetch on invalidation

**Implementation:**
```csharp
public class ResourceSnapshotCache
{
    private readonly int _defaultTtlMinutes;
    private readonly IResourceTemplateRegistry _templates;

    public TimeSpan GetTtlForResource(string resourceType)
    {
        if (_templates.TryGet(resourceType, out var template)
            && template.SnapshotTtlMinutes.HasValue)
        {
            return TimeSpan.FromMinutes(template.SnapshotTtlMinutes.Value);
        }
        return TimeSpan.FromMinutes(_defaultTtlMinutes);
    }
}
```

### Q7: Error Handling

What happens when snapshot loading fails?

**Scenarios:**
- Resource service unavailable
- Character not found (deleted between query and load)
- Compression callback failed for some entries

**Options:**

**A. Fail the flow:**
```csharp
throw new SnapshotLoadException("Failed to load snapshot for character {id}");
```
- Pro: Clear failure
- Con: One bad character breaks entire watcher tick

**B. Return empty provider:**
```csharp
scope.RegisterProvider(ResourceArchiveProvider.Empty("candidate"));
```
Then `${candidate.personality.aggression}` returns `null`.
- Pro: Graceful degradation
- Con: Silent failures, hard to debug

**C. Mark provider as failed, expose error:**
```yaml
- cond:
    - when: "${candidate._error != null}"
      then:
        - log: "Failed to load candidate: ${candidate._error}"
        - continue  # Skip this candidate
    - otherwise:
        - cond:
            - when: "${candidate.personality.aggression} > 0.5"
```
- Pro: Explicit error handling in behavior
- Con: Boilerplate in every flow

**D. Partial success - available entries only:**
If `character-personality` callback succeeded but `character-history` failed:
```
${candidate.personality.aggression} → 0.7
${candidate.history.participations} → null (with warning log)
```
- Pro: Best-effort data
- Con: Inconsistent state

**Current Recommendation**: Option D with logging, Option C for critical flows.

### Q8: Layer Hierarchy Implications

Resource plugin is L1. It currently uses opaque `resourceType` and `sourceType` strings.

**Concern**: If the ResourceArchiveProvider lives in lib-actor (L4), and it needs to understand compression response schemas from other L4 services (character-personality, character-history), does this create coupling?

**Analysis:**
- Provider deserializes to generic `Dictionary<string, object?>`
- No compile-time dependency on response types
- Path navigation is string-based
- Provider doesn't validate structure, just navigates

**Conclusion**: No layer violation. The provider is a generic JSON navigator that happens to understand the archive entry format (which is defined by L1 Resource plugin).

### Q9: Integration with Existing Providers

Character Brain actors already have:
- `PersonalityProvider` - from PersonalityCache
- `BackstoryProvider` - from PersonalityCache
- `EncountersProvider` - from EncounterCache

Should these be unified with ResourceArchiveProvider?

**Option A: Keep separate:**
- Character Brains: Dedicated providers (current)
- Event Brains: ResourceArchiveProvider (new)

Pro: No breaking changes, optimized paths for each
Con: Two ways to access same data

**Option B: Unify on ResourceArchiveProvider:**
All actors use ResourceArchiveProvider, even for self-data.
- Pro: Consistent access pattern
- Con: Breaking change, potentially less efficient for self-data

**Option C: ResourceArchiveProvider wraps existing providers:**
For self-data, ResourceArchiveProvider delegates to PersonalityProvider etc.
```csharp
if (resourceId == _selfCharacterId && sourceType == "character-personality")
    return _personalityProvider.GetValue(path);
```
- Pro: Unified interface, efficient for self
- Con: Complex provider implementation

**Current Recommendation**: Option A for now, consider Option C as future optimization.

---

## Resource Plugin Enhancement: Filtered Snapshots

The Resource plugin needs enhancement to support filtered snapshot responses.

### Current Snapshot Flow

```
1. Request: POST /resource/snapshot/execute
   { resourceType: "character", resourceId: "abc-123" }

2. Resource service executes ALL compression callbacks for "character":
   - character-base (priority 0)
   - character-personality (priority 10)
   - character-history (priority 20)
   - character-encounter (priority 30)

3. Stores complete snapshot in Redis with TTL

4. Returns snapshotId
```

### Enhanced Snapshot Flow

```
1. Request: POST /resource/snapshot/execute
   {
     resourceType: "character",
     resourceId: "abc-123",
     filterSourceTypes: ["character-base", "character-personality"]  // NEW
   }

2. Resource service:
   a. Check cache for existing snapshot of this resource
   b. If cache miss: execute ALL compression callbacks, store complete snapshot
   c. Filter response to only requested sourceTypes

3. Returns filtered snapshot (only 2 entries, not 4)
```

### API Changes

```yaml
# In resource-api.yaml - enhance existing endpoint
/resource/snapshot/execute:
  requestBody:
    content:
      application/json:
        schema:
          type: object
          properties:
            resourceType:
              type: string
            resourceId:
              type: string
              format: uuid
            filterSourceTypes:     # NEW - optional filter
              type: array
              items:
                type: string
              description: |
                Optional filter to return only specified sourceTypes.
                If omitted, returns all entries.
                Snapshot is still stored complete; filter applies to response only.
            ttlSeconds:
              type: integer
```

### Cache Key Strategy

```
Full snapshot cached:
  Key: snap:{snapshotId}
  Value: Complete ResourceSnapshotModel with all entries

Filter applied at response time:
  - No separate cache for filtered versions
  - Filtering is cheap (iterate entries, include/exclude)
  - Avoids cache explosion from filter permutations
```

### Implementation Notes

```csharp
// In ResourceService.ExecuteSnapshotAsync
public async Task<(StatusCodes, ExecuteSnapshotResponse?)> ExecuteSnapshotAsync(
    ExecuteSnapshotRequest body, CancellationToken ct)
{
    // ... existing logic to create/retrieve snapshot ...

    // NEW: Apply filter if specified
    var responseEntries = body.FilterSourceTypes?.Length > 0
        ? snapshot.Entries.Where(e => body.FilterSourceTypes.Contains(e.SourceType)).ToList()
        : snapshot.Entries;

    return (StatusCodes.OK, new ExecuteSnapshotResponse
    {
        SnapshotId = snapshot.SnapshotId,
        Entries = responseEntries,  // Filtered
        // ... other fields
    });
}
```

---

## Proposed Architecture

### New Components

```
bannou-service/
├── Abml/
│   └── Templates/                        # NEW: Resource templates
│       ├── IResourceTemplate.cs          # Template interface
│       ├── ResourceTemplateRegistry.cs   # Registry for template lookup
│       └── Generated/                    # Auto-generated from compress schemas
│           ├── CharacterBaseTemplate.cs
│           ├── CharacterPersonalityTemplate.cs
│           ├── CharacterHistoryTemplate.cs
│           └── ...

plugins/lib-actor/
├── Caching/
│   ├── IResourceSnapshotCache.cs         # Cache interface
│   └── ResourceSnapshotCache.cs          # Cache with prefetch support
├── Runtime/
│   └── ResourceArchiveProvider.cs        # Provider using templates
└── Abml/
    └── Commands/
        ├── LoadSnapshotCommand.cs        # Single snapshot load
        └── PrefetchSnapshotsCommand.cs   # Batch prefetch hint

plugins/lib-resource/
└── ResourceService.cs                    # Enhanced with filter support
```

### Resource Template Interface

```csharp
/// <summary>
/// Defines the schema for a resource snapshot entry type.
/// Enables compile-time path validation and typed deserialization.
/// </summary>
public interface IResourceTemplate
{
    /// <summary>
    /// The sourceType this template handles (e.g., "character-personality").
    /// </summary>
    string SourceType { get; }

    /// <summary>
    /// Short namespace for ABML paths (e.g., "personality").
    /// </summary>
    string Namespace { get; }

    /// <summary>
    /// Valid paths and their value types for compile-time validation.
    /// </summary>
    IReadOnlyDictionary<string, Type> ValidPaths { get; }

    /// <summary>
    /// Deserialize compressed entry data to typed object.
    /// </summary>
    object Deserialize(string compressedBase64Data);

    /// <summary>
    /// Navigate a path within deserialized data.
    /// </summary>
    object? GetValue(object deserializedData, ReadOnlySpan<string> path);
}
```

### ResourceArchiveProvider (Template-Aware)

```csharp
/// <summary>
/// Provides ABML variable access to a resource snapshot.
/// Uses templates for typed deserialization and path validation.
/// </summary>
public sealed class ResourceArchiveProvider : IVariableProvider
{
    private readonly string _name;
    private readonly Dictionary<string, (IResourceTemplate Template, object Data)> _entries;

    public ResourceArchiveProvider(
        string name,
        ResourceSnapshotModel snapshot,
        IResourceTemplateRegistry templates)
    {
        _name = name;
        _entries = new(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in snapshot.Entries)
        {
            if (templates.TryGet(entry.SourceType, out var template))
            {
                var data = template.Deserialize(entry.Data);
                _entries[template.Namespace] = (template, data);
            }
            // Entries without templates are silently skipped
            // (behavior didn't declare that template, so can't access it)
        }
    }

    public string Name => _name;

    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();

        // First segment is namespace (e.g., "personality")
        if (!_entries.TryGetValue(path[0], out var entry))
            return null;

        if (path.Length == 1)
            return entry.Data;

        // Delegate to template for typed navigation
        return entry.Template.GetValue(entry.Data, path.Slice(1));
    }

    // ... other IVariableProvider methods
}
```

### Behavior Template Declaration

```yaml
metadata:
  id: god-of-tragedy
  type: event_brain
  description: "Seeks tragedy and orchestrates downfall narratives"

  # Declare which resource templates this behavior uses
  # Enables: compile-time path validation, request filtering
  resource_templates:
    - character-base
    - character-personality
    - character-history
    # NOT character-encounter - this behavior doesn't need it

flows:
  evaluate_candidate:
    # Load snapshot (filtered to declared templates)
    - load_snapshot:
        type: character
        id: "${candidate.id}"
        as: current

    # Valid - character-personality template declares this path
    - cond:
        - when: "${current.personality.aggression} > 0.5"
          then:
            - call: high_aggression_path

    # Compile error - character-encounter not in resource_templates
    # - cond:
    #     - when: "${current.encounters.count} > 10"  # ERROR
```

### ABML Commands

```yaml
# Load single snapshot (usually cache hit after first load)
- load_snapshot:
    type: character
    id: "${candidate.id}"
    as: current

# Provider accessible until end of current scope
condition: "${current.personality.aggression} > 0.5"

# Batch prefetch hint (optional optimization)
- prefetch_snapshots:
    type: character
    ids: "${candidates.map(c => c.id)}"

# Subsequent loads are guaranteed cache hits
- foreach:
    collection: "${candidates}"
    as: candidate
    do:
      - load_snapshot:
          type: character
          id: "${candidate.id}"
          as: current
      # ...
```

### Cache Configuration

```yaml
# In actor-configuration.yaml
ResourceSnapshotCacheTtlMinutes:
  type: integer
  default: 5
  description: TTL for cached resource snapshots in actor memory
  x-env-name: ACTOR_RESOURCE_SNAPSHOT_CACHE_TTL_MINUTES

ResourceSnapshotPrefetchBatchSize:
  type: integer
  default: 20
  description: Maximum snapshots to prefetch in a single batch
  x-env-name: ACTOR_RESOURCE_SNAPSHOT_PREFETCH_BATCH_SIZE
```

### Template Generation

Templates are generated from compression response schemas:

```yaml
# In character-personality-api.yaml
/character-personality/get-compress-data:
  responses:
    200:
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/CharacterPersonalityCompressData'

components:
  schemas:
    CharacterPersonalityCompressData:
      type: object
      x-resource-template: true  # NEW: marks this for template generation
      x-template-namespace: personality  # Short name for ABML paths
      properties:
        traits:
          $ref: '#/components/schemas/PersonalityTraits'
        combatPreferences:
          $ref: '#/components/schemas/CombatPreferences'
```

Generation script produces:
```csharp
// Generated/Templates/CharacterPersonalityTemplate.cs
public sealed class CharacterPersonalityTemplate : IResourceTemplate
{
    public string SourceType => "character-personality";
    public string Namespace => "personality";

    public IReadOnlyDictionary<string, Type> ValidPaths { get; } = new Dictionary<string, Type>
    {
        ["traits"] = typeof(PersonalityTraits),
        ["traits.aggression"] = typeof(float),
        ["combatPreferences"] = typeof(CombatPreferences),
        ["combatPreferences.preferredStyle"] = typeof(CombatStyle),
        // ... generated from schema traversal
    };

    public object Deserialize(string compressedBase64Data)
    {
        var json = DecompressGzip(Convert.FromBase64String(compressedBase64Data));
        return BannouJson.Deserialize<CharacterPersonalityCompressData>(json)
            ?? throw new InvalidOperationException("Failed to deserialize personality data");
    }

    public object? GetValue(object data, ReadOnlySpan<string> path)
    {
        var typed = (CharacterPersonalityCompressData)data;
        return path[0] switch
        {
            "traits" => path.Length == 1 ? typed.Traits : GetTraitsValue(typed.Traits, path.Slice(1)),
            "combatPreferences" => path.Length == 1 ? typed.CombatPreferences : GetCombatPrefsValue(...),
            _ => null
        };
    }
}
```

---

## Implementation Phases

### Phase 1: Resource Plugin Enhancement ✅ COMPLETE

- [x] Add `filterSourceTypes` parameter to `/resource/snapshot/execute`
- [x] Add `filterSourceTypes` parameter to `/resource/snapshot/get`
- [x] Implement filter-at-response-time (not filter-at-storage)
- [x] Update resource-api.yaml schema
- [x] Regenerate Resource plugin
- [x] Unit tests for filtered snapshot responses

### Phase 2: Template Infrastructure ✅ COMPLETE

- [x] Define `IResourceTemplate` interface in bannou-service (`bannou-service/ResourceTemplates/`)
- [x] Define `IResourceTemplateRegistry` for template lookup
- [x] Add `x-resource-template` and `x-template-namespace` schema extensions
- [x] Create template generation script (`scripts/generate-resource-templates.py`)
- [x] Generate templates for existing compression callbacks:
  - [x] character-base
  - [x] character-personality
  - [x] character-history
  - [x] character-encounter
- [x] Unit tests for template deserialization and path navigation

### Phase 3: Provider Implementation ✅ COMPLETE

- [x] Implement `ResourceArchiveProvider` (`plugins/lib-puppetmaster/Providers/`)
- [x] Implement `IResourceSnapshotCache` with prefetch support
- [x] Implement `ResourceSnapshotCache` with TTL configuration
- [x] Add configuration properties to puppetmaster-configuration.yaml
- [x] Register cache as Singleton in Puppetmaster service
- [x] Unit tests for provider path resolution

**Note**: Provider lives in lib-puppetmaster (L4), not lib-actor (L2). This is correct per SERVICE-HIERARCHY.md - lib-puppetmaster provides the missing link between behavior execution (lib-actor at L2) and asset service (lib-asset at L3).

### Phase 4: ABML Integration ✅ COMPLETE

- [x] Implement `load_snapshot` ABML command (`plugins/lib-puppetmaster/Handlers/LoadSnapshotHandler.cs`)
- [x] Implement `prefetch_snapshots` ABML command (`plugins/lib-puppetmaster/Handlers/PrefetchSnapshotsHandler.cs`)
- [x] Add `resource_templates` metadata field to behavior schema (`AbmlDocument.Metadata.ResourceTemplates`)
- [x] Implement compile-time path validation against declared templates (in SemanticAnalyzer)
- [x] Integration tests with sample event actor behavior

### Phase 5: Storyline/Quest Compression Callbacks 🔲 TODO

- [ ] Register compression callback for Storyline service
- [ ] Register compression callback for Quest service
- [ ] Generate templates for new compression responses
- [ ] Update sample event actor behaviors to use new data

### Phase 6: Actor Communication 🔲 TODO

- [ ] Design `actor_command` ABML command for event→character actor communication
- [ ] Design `actor_query` ABML command for request/response patterns
- [ ] Implement command routing through Actor service
- [ ] Document communication patterns in event actor guide

### Future Enhancements

- [ ] Automatic prefetch based on foreach body analysis
- [ ] Per-sourceType TTL configuration
- [ ] Compression callback versioning for schema evolution
- [ ] Archive data (permanent MySQL) vs snapshot data (ephemeral Redis) access patterns

---

## Related Work

### Issue #316 (CLOSED) - GOAP Scope Population

Issue #316 implemented MVP GOAP integration in ActorRunner, enabling `trigger_goap_replan:` to work:

- **Goals/Actions**: Extracted from ABML documents via `GoapMetadataConverter`
- **WorldState**: Built from actor feelings, goal parameters, working memory
- **Current Goal**: Looked up from actor's PrimaryGoal string

This is complementary to this document's Event Actor pattern:
- **Character Brain actors**: Use live Variable Providers (self-data) + GOAP with internal state
- **Event Brain actors**: Use `load_snapshot:` for archive data about other characters

### Issue #148 - GoapWorldStateProvider

Issue #148 tracks extending GOAP WorldState with external service data (currency, inventory, relationships). With #316 complete, GOAP works with internal state; #148 is now an enhancement for richer planning.

---

## References

### Implementation References
- [IVariableProvider Interface](/home/lysander/repos/bannou/bannou-service/Abml/Expressions/IVariableProvider.cs)
- [PersonalityProvider Example](/home/lysander/repos/bannou/plugins/lib-actor/Runtime/PersonalityProvider.cs)
- [PersonalityCache Example](/home/lysander/repos/bannou/plugins/lib-actor/Caching/PersonalityCache.cs)
- [ResourceService Implementation](/home/lysander/repos/bannou/plugins/lib-resource/ResourceService.cs)

### Architecture References
- [Variable Providers Plan](~/.claude/plans/variable-providers-storyline-quest.md)
- [Regional Watchers Design](REGIONAL_WATCHERS_BEHAVIOR.md)
- [Actor Deep Dive](../plugins/ACTOR.md) - Data access pattern selection
- [SERVICE-HIERARCHY.md](../reference/SERVICE-HIERARCHY.md) - Variable Provider Factory pattern
- [Resource Plugin Deep Dive](../plugins/RESOURCE.md)

### TENET References
- [TENETS.md](/home/lysander/repos/bannou/docs/reference/TENETS.md)
- [SERVICE-HIERARCHY.md](/home/lysander/repos/bannou/docs/reference/SERVICE-HIERARCHY.md)
