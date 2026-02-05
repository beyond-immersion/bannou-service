# Variable Providers: Storyline & Quest

> **Status**: Ready for Implementation
> **Created**: 2026-02-05
> **Resolved**: 2026-02-05
> **TENET Review**: 2026-02-05 (all examples verified compliant)
> **Related**: Quest Plugin, Storyline Plugin, Actor Service
> **Service Hierarchy**: L4 (Game Features) providers accessing L4 services

---

## Research Findings

### 1. IVariableProvider Interface

Located at `/home/lysander/repos/bannou/bannou-service/Abml/Expressions/IVariableProvider.cs`:

```csharp
public interface IVariableProvider
{
    string Name { get; }  // Provider prefix (e.g., "personality", "storyline")
    object? GetValue(ReadOnlySpan<string> path);  // Navigate path from root
    object? GetRootValue();  // Return entire provider data
    bool CanResolve(ReadOnlySpan<string> path);  // Check if path is resolvable
}
```

**Key observations:**
- Providers are **synchronous** - data must be pre-loaded/cached
- `path` is a span of strings representing dotted notation (e.g., `["active_count"]` for `${storyline.active_count}`)
- Return `null` for unresolved paths
- `CanResolve` used for introspection without side effects

### 2. Reference Implementation: PersonalityProvider

Located at `/home/lysander/repos/bannou/plugins/lib-actor/Runtime/PersonalityProvider.cs`:

**Pattern:**
1. Constructor receives response data (pre-fetched)
2. Internal dictionary stores flat key-value pairs
3. Case-insensitive lookups
4. Supports both direct access (`${personality.aggression}`) and nested access (`${personality.traits.AGGRESSION}`)
5. `GetRootValue()` returns structured dictionary for debugging/introspection

### 3. Caching Pattern: PersonalityCache

Located at `/home/lysander/repos/bannou/plugins/lib-actor/Caching/PersonalityCache.cs`:

**Pattern:**
1. Interface + Implementation separation (`IPersonalityCache` + `PersonalityCache`)
2. `ConcurrentDictionary` for thread-safe caching
3. TTL-based expiration via `CachedXxx` record with `ExpiresAt`
4. `IServiceScopeFactory` for creating scoped service clients
5. Stale-if-error fallback (return expired data on API failure)
6. `Invalidate(characterId)` for targeted cache clearing
7. `InvalidateAll()` for full cache flush

### 4. Registration in ActorRunner

Located at `/home/lysander/repos/bannou/plugins/lib-actor/Runtime/ActorRunner.cs` lines 743-766:

```csharp
// In BuildExecutionScopeAsync()
if (CharacterId.HasValue)
{
    var personality = await _personalityCache.GetOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new PersonalityProvider(personality));

    var combatPrefs = await _personalityCache.GetCombatPreferencesOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new CombatPreferencesProvider(combatPrefs));

    var backstory = await _personalityCache.GetBackstoryOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new BackstoryProvider(backstory));

    var encounters = await _encounterCache.GetEncountersOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new EncountersProvider(encounters));
}
else
{
    // Register empty providers for non-character actors
    scope.RegisterProvider(new PersonalityProvider(null));  // NOTE: Legacy pattern - see below
    // ... etc
}
```

**Key insight:** Providers are instantiated per-execution-scope with pre-loaded data. The cache handles async loading; the provider is synchronous.

> **⚠️ TENET Deviation in Existing Code**: The existing PersonalityProvider uses nullable constructor parameter (`PersonalityProvider(null)`). Our new Storyline/Quest providers will NOT follow this pattern - instead, we'll use empty response objects. This avoids nullable parameters and `null!` in tests (T12 compliance). See Registration section below.

### 5. Data Requirements from Planning Documents

From `REGIONAL_WATCHERS_BEHAVIOR.md`:
- Watchers need to check if characters are in active storylines
- Need storyline phase for orchestration decisions
- Variables updated in behavior flows: `watched_characters`, `active_storylines`, `recent_tragedies`

From `QUEST_PLUGIN_ARCHITECTURE.md`:
- Quest tracks objective progress outside Contract
- Quest status: Active, Completed, Failed, Abandoned
- Objective progress: `CurrentCount`, `RequiredCount`, `IsComplete`, `ProgressPercent`
- Quest log for player-facing view

From `ACTOR_DATA_ACCESS_PATTERNS.md`:
- Hybrid approach: cached Variable Providers for read-heavy, infrequent-change data
- 5-minute TTL recommended for storyline/quest data (changes via explicit actions only)
- Variable Providers access pattern recommended for character attribute data

---

## Interface Design

### StorylineProvider (`${storyline.*}`)

| Variable Path | Type | Description |
|---------------|------|-------------|
| `${storyline.is_participant}` | `bool` | Is this character in any active storyline? |
| `${storyline.active_count}` | `int` | Number of active storylines character participates in |
| `${storyline.active_storylines}` | `List<string>` | List of active storyline IDs |
| `${storyline.primary}` | `object?` | Highest priority storyline details |
| `${storyline.primary.id}` | `string` | Primary storyline ID |
| `${storyline.primary.template_code}` | `string` | Story template code (e.g., "FALL_FROM_GRACE") |
| `${storyline.primary.phase}` | `string` | Current phase of primary storyline |
| `${storyline.primary.role}` | `string` | Character's role in primary storyline |
| `${storyline.primary.priority}` | `int` | Priority value (higher = more important) |
| `${storyline.primary.joined_at}` | `string` | ISO 8601 timestamp |
| `${storyline.most_recent}` | `object?` | Most recently joined storyline details |
| `${storyline.most_recent.id}` | `string` | Most recent storyline ID |
| `${storyline.most_recent.template_code}` | `string` | Story template code |
| `${storyline.most_recent.phase}` | `string` | Current phase |
| `${storyline.most_recent.role}` | `string` | Character's role |
| `${storyline.most_recent.priority}` | `int` | Priority value |
| `${storyline.most_recent.joined_at}` | `string` | ISO 8601 timestamp |
| `${storyline.has_role(role)}` | N/A | **Not supported** - providers are synchronous, use conditions |

**Note:** `primary` = highest priority (informational, may not be honored by everything). `most_recent` = most recently joined (temporal). These are often the same storyline but semantically distinct.

```yaml
# ABML usage
condition: "${storyline.is_participant} && ${storyline.primary.role} == 'protagonist'"
```

### QuestProvider (`${quest.*}`)

| Variable Path | Type | Description |
|---------------|------|-------------|
| `${quest.active_count}` | `int` | Number of active quests |
| `${quest.active_quests}` | `List<object>` | List of active quest summaries |
| `${quest.has_active}` | `bool` | Has any active quest |
| `${quest.codes}` | `List<string>` | List of active quest codes |
| `${quest.by_code.QUEST_CODE}` | `object?` | Quest details by code |
| `${quest.by_code.QUEST_CODE.status}` | `string` | Quest status |
| `${quest.by_code.QUEST_CODE.current_objective}` | `object?` | Current objective details |
| `${quest.by_code.QUEST_CODE.progress}` | `float` | Overall progress (0.0-1.0) |
| `${quest.by_code.QUEST_CODE.deadline}` | `string?` | ISO 8601 deadline if applicable |

**Design decision:** Use `by_code` prefix for quest lookup by code since dynamic path segments require special handling.

```yaml
# ABML usage
condition: "${quest.active_count} > 0"
action: "check_quest_objectives"

# Check specific quest
condition: "${quest.by_code.BOUNTY_HUNT_WOLF.progress} >= 0.8"
action: "return_to_questgiver"
```

---

## Implementation Files

### New Files to Create

| File Path | Purpose |
|-----------|---------|
| `plugins/lib-actor/Caching/IStorylineCache.cs` | Cache interface for storyline data |
| `plugins/lib-actor/Caching/StorylineCache.cs` | Cache implementation |
| `plugins/lib-actor/Runtime/StorylineProvider.cs` | Variable provider for `${storyline.*}` |
| `plugins/lib-actor/Caching/IQuestCache.cs` | Cache interface for quest data |
| `plugins/lib-actor/Caching/QuestCache.cs` | Cache implementation |
| `plugins/lib-actor/Runtime/QuestProvider.cs` | Variable provider for `${quest.*}` |

### Files to Modify

| File Path | Changes |
|-----------|---------|
| `plugins/lib-actor/Runtime/ActorRunner.cs` | Register new providers in `BuildExecutionScopeAsync()` |
| `plugins/lib-actor/lib-actor.csproj` | Add references to lib-storyline and lib-quest clients (if needed) |
| `plugins/lib-actor/ActorService.cs` | Register cache services in DI |

### Prerequisite: Service Clients

Generated clients `IStorylineClient` and `IQuestClient` already exist. These are hard dependencies - constructor injection ensures DI container fails at startup if not registered.

**Required API endpoints:**

**Storyline Service API:**
```yaml
/storyline/list-by-character:
  summary: List storylines where character participates
  input:
    characterId: uuid
    status: [active]  # Filter to active only
  output:
    storylines: [StorylineParticipation]  # Must include Priority field
```

**Quest Service API:**
```yaml
/quest/list:
  summary: List character's quests
  input:
    characterId: uuid
    status: [active]  # Filter to active only
  output:
    quests: [QuestSummary]
```

**Schema requirement:** `StorylineParticipation` must include a `priority` field (int, higher = more important) for primary storyline selection.

---

## Cache Strategy

### TTL Configuration

Add to `ActorServiceConfiguration` schema (`schemas/actor-configuration.yaml`):

```yaml
StorylineCacheTtlMinutes:
  type: integer
  default: 5
  description: TTL in minutes for cached storyline participation data
  x-env-name: ACTOR_STORYLINE_CACHE_TTL_MINUTES

QuestCacheTtlMinutes:
  type: integer
  default: 5
  description: TTL in minutes for cached quest data
  x-env-name: ACTOR_QUEST_CACHE_TTL_MINUTES
```

**Rationale:** 5-minute TTL matches personality/backstory pattern. Storyline and quest state changes via explicit actions, not high-frequency mutations.

**Env var naming**: Per SCHEMA-RULES, env vars follow `{SERVICE}_{PROPERTY}` pattern. Property name includes `Minutes` suffix so the unit is clear.

### Cache Invalidation

**Event-driven invalidation** (subscribe to events):

| Event Topic | Cache Action |
|-------------|--------------|
| `storyline.character.joined` | Invalidate storyline cache for character |
| `storyline.character.left` | Invalidate storyline cache for character |
| `storyline.phase.advanced` | Invalidate storyline cache for all participants |
| `quest.accepted` | Invalidate quest cache for character |
| `quest.completed` | Invalidate quest cache for character |
| `quest.failed` | Invalidate quest cache for character |
| `quest.abandoned` | Invalidate quest cache for character |
| `quest.objective.progressed` | Invalidate quest cache for character |

**Implementation:** Add event subscriptions in `ActorServiceEvents.cs`:

```csharp
/// <summary>
/// Invalidates storyline cache when a character joins a storyline.
/// </summary>
[EventSubscription("storyline.character.joined")]
public async Task HandleStorylineJoinedAsync(StorylineCharacterJoinedEvent evt, CancellationToken ct)
{
    _storylineCache.Invalidate(evt.CharacterId);
    _logger.LogDebug("Invalidated storyline cache for character {CharacterId}", evt.CharacterId);
    await Task.CompletedTask;  // Per IMPLEMENTATION TENETS: async methods must have await
}
```

### Error Handling

Since Storyline/Quest clients are hard dependencies (constructor-injected), service unavailability is a deployment configuration error, not a runtime state to handle gracefully. Per SERVICE_HIERARCHY, L4→L4 dependencies within the same feature set are hard dependencies.

**Complete error handling pattern:**
```csharp
public async Task<StorylineListResponse> GetOrLoadAsync(Guid characterId, CancellationToken ct)
{
    // Check cache first
    if (_cache.TryGetValue(characterId, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
    {
        _logger.LogDebug("Cache hit for character {CharacterId}", characterId);
        return cached.Data;
    }

    try
    {
        using var scope = _scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IStorylineClient>();

        var request = new ListByCharacterRequest { CharacterId = characterId, Status = new[] { "active" } };
        var (status, response) = await client.ListByCharacterAsync(request, ct);

        // Handle expected "no data" case - NOT an error
        if (status == StatusCodes.NotFound)
        {
            _logger.LogDebug("No storylines found for character {CharacterId}", characterId);
            return new StorylineListResponse { Storylines = new List<StorylineParticipation>() };
        }

        // Null response with OK status is a programming error
        var data = response ?? throw new InvalidOperationException(
            $"Null response with {status} status for character {characterId}");

        // Cache the result
        var ttl = TimeSpan.FromMinutes(_configuration.StorylineCacheTtlMinutes);
        _cache[characterId] = new CachedStorylineData(data, DateTimeOffset.UtcNow.Add(ttl));

        return data;
    }
    catch (ApiException ex)
    {
        // API error - log with context and rethrow
        _logger.LogWarning(ex, "Storyline API returned {StatusCode} for character {CharacterId}",
            ex.StatusCode, characterId);
        throw;
    }
    catch (Exception ex)
    {
        // Infrastructure error - log at Error level, this should not happen
        _logger.LogError(ex, "Failed to load storyline data for character {CharacterId}", characterId);
        throw;  // Don't hide failures - let caller handle
    }
}
```

**Key points:**
- 404 returns empty list, not throws (character may have no storylines - valid state)
- ApiException logged at Warning (expected API behavior)
- Other exceptions logged at Error (unexpected infrastructure failure)
- No graceful degradation - if Storyline service is down, Actor service should fail
- No `TryPublishErrorAsync` for 404s - only for unexpected failures

---

## Registration

### ActorRunner Changes

In `/home/lysander/repos/bannou/plugins/lib-actor/Runtime/ActorRunner.cs`, add to `BuildExecutionScopeAsync()`:

```csharp
// After existing personality/backstory/encounters registration...

if (CharacterId.HasValue)
{
    // Load storyline participation data for character-based actors
    var storylines = await _storylineCache.GetParticipationOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new StorylineProvider(storylines));

    // Load quest data for character-based actors
    var quests = await _questCache.GetActiveQuestsOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new QuestProvider(quests));
}
else
{
    // Non-character actors (dungeons, watchers) get empty providers
    // Per QUALITY TENETS (T12): Use empty response objects, not null
    scope.RegisterProvider(StorylineProvider.Empty);
    scope.RegisterProvider(QuestProvider.Empty);
}
```

**Note on Empty pattern**: Non-character actors don't have storylines/quests themselves (they orchestrate FOR characters). Instead of nullable constructor parameters (which would require `null!` in tests), we use a static `Empty` property that returns a provider with empty collections. This is compliant with T12 (no `null!` bypass) and T26 (no sentinel values - empty list is semantically correct, not a sentinel).

### DI Registration

In `ActorService.cs` constructor or service registration:

```csharp
// Caches are Singleton - shared across all ActorRunner instances for memory efficiency
// Constructor injection receives: IServiceScopeFactory, ActorServiceConfiguration, ILogger<T>
services.AddSingleton<IStorylineCache, StorylineCache>();
services.AddSingleton<IQuestCache, QuestCache>();
```

**Why Singleton for caches:**
- Memory efficiency - one cache instance shared by all actors
- Thread-safe via `ConcurrentDictionary` (T9)
- Uses `IServiceScopeFactory` to create scoped service clients when needed (T4)
- Receives configuration via constructor injection (T21)

**DI Lifetime compatibility:**
- `ActorService` is Scoped
- `StorylineCache`/`QuestCache` are Singleton
- This is valid: Scoped services CAN inject Singleton dependencies

---

## Resolved Decisions

> **Resolved**: 2026-02-05 - All questions answered, ready for implementation

### 1. Plugin Existence

**Question:** Do lib-storyline and lib-quest plugins exist with the required APIs?

**Status:**
- **lib-storyline**: Exists (~30% complete, needs API additions)
- **lib-quest**: Implementation planned (`sharded-waddling-narwhal.md`)
- **Generated clients**: `IStorylineClient` and `IQuestClient` already exist

**Decision:** ✅ Hard dependency on generated clients. No graceful degradation.

**Rationale:**
- Clients already exist - adding fallback logic would be dead code
- Graceful degradation hides real errors (TENET violation)
- These providers ship WITH the storyline/quest feature set - if you're using them, the services are deployed
- Constructor injection for the clients; DI container fails at startup if not registered

### 2. Character vs Entity ID

**Question:** Should providers support generic `entityId` like encounters, or only `characterId`?

**Decision:** ✅ Use `characterId` only. No generalization needed.

**Rationale:**
- Storylines and quests are fundamentally character concepts in the domain model
- Non-character actors (dungeons, watchers) orchestrate storylines FOR characters, they don't have storylines themselves
- Matches existing personality/backstory pattern
- Can generalize later if domain model evolves

### 3. Non-Character Actors

**Question:** Can non-character actors (e.g., Regional Watchers) access storyline data for OTHER characters?

**Decision:** ✅ Keep providers character-scoped. Orchestrators access other characters' data via **Resource plugin snapshots**, NOT arbitrary `service_call`.

**Rationale:**
- Non-character actors do NOT get `service_call` access to arbitrary services
- The **Resource plugin (L1)** provides the controlled data access interface for obtaining character/entity snapshots
- Resource plugin handles automatic caching, audit trails, and controlled access patterns
- This prevents actors from having carte blanche to call any service

**Data access pattern for orchestrators:**

```yaml
# Regional Watcher behavior - get character snapshot via Resource plugin
- resource_snapshot:
    entity_type: character
    entity_id: "${candidate.id}"
    include:
      - storyline_participation
      - quest_state
    result_variable: candidate_snapshot

# Then access data from snapshot
condition: "${candidate_snapshot.storyline_participation.active_count} == 0"
```

**Documentation requirement:** Update Regional Watchers doc with Resource plugin snapshot pattern, NOT service_call.

### 4. Primary Storyline Selection

**Question:** How do we determine "primary" storyline when character has multiple?

**Decision:** ✅ Primary = highest priority. Add separate `most_recent` for recency.

**Rationale:**
- "Primary" semantically means "most important", not "most recent"
- Priority is meaningful and obvious - the storyline the system considers most important
- Recency is a separate useful data point, so expose both
- Designing for "enhance later" creates cleanup work - do it right the first time

**Implementation:**
```csharp
// In StorylineProvider constructor - fields are nullable per T26 (no sentinels)
private readonly StorylineParticipation? _primary;
private readonly StorylineParticipation? _mostRecent;

public StorylineProvider(StorylineListResponse response)
{
    _storylines = response.Storylines;  // Non-nullable list (may be empty)

    // Primary = highest priority (may not be honored by everything, but informational)
    // FirstOrDefault returns null if list is empty - correct behavior, not a sentinel
    _primary = _storylines.OrderByDescending(s => s.Priority).FirstOrDefault();

    // Most recent = most recently joined (separate concept)
    _mostRecent = _storylines.OrderByDescending(s => s.JoinedAt).FirstOrDefault();
}
```

**Provider variables:**
- `${storyline.primary.*}` - highest priority storyline (null if no storylines)
- `${storyline.most_recent.*}` - most recently joined storyline (null if no storylines)

**Schema requirement:** `StorylineParticipation` must include:
- `priority` field (int, non-nullable, higher = more important)
- `joinedAt` field (DateTimeOffset, non-nullable)

---

## TENET Compliance Guide

> **Reference**: Use category names in code comments, never specific tenet numbers (per TENETS.md Tenet 0).

### FOUNDATION TENETS

#### Infrastructure Libs Pattern (T4)

**Cache implementations MUST use infrastructure abstractions:**

```csharp
// CORRECT: Use IServiceScopeFactory for scoped client access from singleton cache
public class StorylineCache : IStorylineCache
{
    private readonly IServiceScopeFactory _scopeFactory;

    public async Task<StorylineListResponse> GetOrLoadAsync(Guid characterId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IStorylineClient>();
        return await client.ListByCharacterAsync(new ListByCharacterRequest { CharacterId = characterId }, ct);
    }
}

// FORBIDDEN: Direct HTTP calls
await httpClient.GetAsync($"http://storyline/api/list-by-character?characterId={id}");  // NO!
```

**Hard dependencies for L4→L4 within same feature set:**
- `IStorylineClient` and `IQuestClient` are constructor-injected in the cache
- No graceful degradation - these ship together
- DI container fails at startup if clients aren't registered

#### Event-Driven Architecture (T5)

**Cache invalidation events MUST be typed, not anonymous:**

```csharp
// CORRECT: Typed event models from generated schema
[EventSubscription("storyline.character.joined")]
public async Task HandleStorylineJoined(StorylineCharacterJoinedEvent evt, CancellationToken ct)
{
    _storylineCache.Invalidate(evt.CharacterId);
    await Task.CompletedTask;
}

// FORBIDDEN: Anonymous event handling
await _messageBus.PublishAsync("cache.invalidate", new { type = "storyline", id = characterId });  // NO!
```

**Event schema requirement:** Storyline and Quest plugins MUST define the invalidation events in their respective `-events.yaml` schemas.

#### Service Implementation Pattern (T6)

**Event handlers in partial class file:**

```
plugins/lib-actor/
├── ActorService.cs           # Main service (partial class)
├── ActorServiceEvents.cs     # Event handlers (partial class) - add cache invalidation here
└── Caching/
    ├── IStorylineCache.cs
    └── StorylineCache.cs
```

**DI Registration lifetime rules:**
- `ActorService` is Scoped
- `StorylineCache` and `QuestCache` are Singleton (shared across all ActorRunner instances)
- This is valid: Scoped service can consume Singleton dependency

### IMPLEMENTATION TENETS

#### Event Consumer Fan-Out (T3)

**Cache invalidation subscriptions use IEventConsumer:**

```csharp
// In ActorServiceEvents.cs
protected void RegisterEventConsumers(IEventConsumer eventConsumer)
{
    // Existing registrations...

    // Add storyline cache invalidation
    eventConsumer.RegisterHandler<IActorService, StorylineCharacterJoinedEvent>(
        "storyline.character.joined",
        async (svc, evt) => await ((ActorService)svc).HandleStorylineJoinedAsync(evt));

    eventConsumer.RegisterHandler<IActorService, QuestAcceptedEvent>(
        "quest.accepted",
        async (svc, evt) => await ((ActorService)svc).HandleQuestAcceptedAsync(evt));
}
```

#### Error Handling (T7)

**Cache loading distinguishes ApiException from infrastructure errors:**

```csharp
public async Task<StorylineListResponse> GetOrLoadAsync(Guid characterId, CancellationToken ct)
{
    try
    {
        var (status, response) = await _client.ListByCharacterAsync(request, ct);
        if (status == StatusCodes.NotFound)
        {
            // Character has no storylines - valid state
            return new StorylineListResponse { Storylines = new() };
        }
        return response ?? throw new InvalidOperationException("Null response with OK status");
    }
    catch (ApiException ex)
    {
        // Expected API error - propagate with context
        _logger.LogWarning(ex, "Storyline API returned {Status} for character {CharacterId}",
            ex.StatusCode, characterId);
        throw;
    }
    catch (Exception ex)
    {
        // Unexpected infrastructure error - log and rethrow
        _logger.LogError(ex, "Failed to load storyline data for character {CharacterId}", characterId);
        throw;
    }
}
```

**Error events:** Only emit `TryPublishErrorAsync` for unexpected infrastructure failures, NOT for 404s (character has no storylines).

#### Multi-Instance Safety (T9)

**Caches MUST use ConcurrentDictionary:**

```csharp
// CORRECT: Thread-safe cache
private readonly ConcurrentDictionary<Guid, CachedStorylineData> _cache = new();

// FORBIDDEN: Plain dictionary
private readonly Dictionary<Guid, CachedStorylineData> _cache = new();  // NO!
```

**Cache invalidation is safe across instances:** Event-driven invalidation means all instances receive the invalidation event via RabbitMQ fanout.

#### Configuration-First Development (T21)

**TTL configuration MUST be in schema, not hardcoded:**

```yaml
# schemas/actor-configuration.yaml - matches Cache Strategy section above
StorylineCacheTtlMinutes:
  type: integer
  default: 5
  description: TTL in minutes for cached storyline participation data
  x-env-name: ACTOR_STORYLINE_CACHE_TTL_MINUTES

QuestCacheTtlMinutes:
  type: integer
  default: 5
  description: TTL in minutes for cached quest data
  x-env-name: ACTOR_QUEST_CACHE_TTL_MINUTES
```

```csharp
// CORRECT: Use configuration from injected config class
public class StorylineCache : IStorylineCache
{
    private readonly TimeSpan _ttl;

    public StorylineCache(ActorServiceConfiguration configuration, ...)
    {
        _ttl = TimeSpan.FromMinutes(configuration.StorylineCacheTtlMinutes);
    }
}

// FORBIDDEN: Hardcoded tunable anywhere in service code
var ttl = TimeSpan.FromMinutes(5);  // NO - define in config schema
```

**No secondary fallbacks for defaulted properties:**
```csharp
// FORBIDDEN: Schema has default=5, so null-coalesce is redundant and dangerous
var ttl = _configuration.StorylineCacheTtlMinutes ?? 5;  // NO! (also StorylineCacheTtlMinutes is int, not int?)

// CORRECT: Schema default compiles into property initializer - value is always present
var ttl = _configuration.StorylineCacheTtlMinutes;  // Uses schema default if env var not set
```

#### Async Method Pattern (T23)

**All Task-returning methods MUST be async with await:**

```csharp
// CORRECT: async with await
public async Task HandleStorylineJoinedAsync(StorylineCharacterJoinedEvent evt)
{
    _storylineCache.Invalidate(evt.CharacterId);
    await Task.CompletedTask;  // Sync work in async interface
}

// FORBIDDEN: Non-async Task return
public Task HandleStorylineJoinedAsync(StorylineCharacterJoinedEvent evt)
{
    _storylineCache.Invalidate(evt.CharacterId);
    return Task.CompletedTask;  // NO - use async keyword
}
```

#### Type Safety (T25)

**Provider models use proper types, not strings:**

```csharp
// CORRECT: Strong types
public Guid StorylineId { get; set; }
public StorylinePhase CurrentPhase { get; set; }  // Enum
public int Priority { get; set; }
public DateTimeOffset JoinedAt { get; set; }

// FORBIDDEN: String representations
public string StorylineId { get; set; }  // NO - use Guid
public string CurrentPhase { get; set; }  // NO - use enum
public string JoinedAt { get; set; }  // NO - use DateTimeOffset
```

**Provider output may use strings for ABML consumption** - the `GetValue()` return type is `object?` and ABML conditions work with string comparisons. But the underlying model fields must be properly typed.

#### No Sentinel Values (T26)

**Nullable fields for optional data:**

```csharp
// CORRECT: Nullable when value can be absent
public StorylineParticipation? _primary;  // null if no storylines
public StorylineParticipation? _mostRecent;  // null if no storylines
public DateTimeOffset? Deadline { get; set; }  // null if no deadline

// FORBIDDEN: Sentinel values
public Guid PrimaryStorylineId { get; set; }  // Using Guid.Empty for "none" - NO!
public int Priority { get; set; } = -1;  // Using -1 for "no priority" - NO!
```

### QUALITY TENETS

#### Logging Standards (T10)

**Structured logging with message templates:**

```csharp
// CORRECT: Message templates with named placeholders
_logger.LogDebug("Loading storyline data for character {CharacterId}", characterId);
_logger.LogDebug("Cache hit for character {CharacterId}, expires at {ExpiresAt}", characterId, cached.ExpiresAt);
_logger.LogInformation("Invalidating storyline cache for {Count} characters", characterIds.Count);

// FORBIDDEN: String interpolation
_logger.LogDebug($"Loading storyline data for character {characterId}");  // NO!

// FORBIDDEN: Tag prefixes
_logger.LogDebug("[CACHE] Loading storyline data");  // NO!
```

#### Test Integrity (T12)

**Unit tests for provider path resolution:**

```csharp
[Fact]
public void GetValue_PrimaryPhase_ReturnsCurrentPhase()
{
    // Arrange - use real test data, not null!
    var response = new StorylineListResponse
    {
        Storylines = new List<StorylineParticipation>
        {
            new()
            {
                StorylineId = Guid.NewGuid(),  // Proper Guid, not Guid.Empty (T26)
                Priority = 10,
                CurrentPhase = StorylinePhase.Rising,  // Enum, not string (T25)
                JoinedAt = DateTimeOffset.UtcNow,  // DateTimeOffset, not string (T25)
                TemplateCode = "FALL_FROM_GRACE",
                Role = "protagonist"
            }
        }
    };
    var provider = new StorylineProvider(response);

    // Act
    var result = provider.GetValue(new[] { "primary", "phase" }.AsSpan());

    // Assert
    Assert.Equal("Rising", result);  // Enum.ToString() for ABML consumption
}

[Fact]
public void GetValue_EmptyStorylines_ReturnsZeroActiveCount()
{
    // Use Empty pattern, not null - tests valid scenario of "no storylines"
    var result = StorylineProvider.Empty.GetValue(new[] { "active_count" }.AsSpan());
    Assert.Equal(0, result);
}

[Fact]
public void GetValue_EmptyStorylines_PrimaryReturnsNull()
{
    // Empty provider should return null for primary (no storylines = no primary)
    var result = StorylineProvider.Empty.GetValue(new[] { "primary" }.AsSpan());
    Assert.Null(result);  // Null is correct here - it means "no primary storyline exists"
}
```

**Test with Empty pattern, not null!:**
```csharp
// CORRECT: Use static Empty property for "no data" scenarios
var provider = StorylineProvider.Empty;
Assert.Equal(0, provider.GetValue(new[] { "active_count" }.AsSpan()));

// FORBIDDEN: Bypassing NRT with null!
var provider = new StorylineProvider(null!);  // NO - tests impossible scenario
```

#### XML Documentation (T19)

**All public APIs must be documented:**

```csharp
/// <summary>
/// Provides ABML variable access to a character's storyline participation data.
/// </summary>
/// <remarks>
/// Variables available:
/// - ${storyline.is_participant} - bool
/// - ${storyline.active_count} - int
/// - ${storyline.primary.*} - highest priority storyline
/// - ${storyline.most_recent.*} - most recently joined storyline
/// </remarks>
public sealed class StorylineProvider : IVariableProvider
{
    /// <summary>
    /// Creates a new StorylineProvider with the given participation data.
    /// </summary>
    /// <param name="response">The storyline participation response from the Storyline service.</param>
    public StorylineProvider(StorylineListResponse response)
    {
        // ...
    }
}
```

#### Warning Suppression (T22)

**No pragma suppressions - fix warnings instead:**

```csharp
// FORBIDDEN: Suppressing nullability warning
#pragma warning disable CS8602
var phase = storyline.CurrentPhase.ToString();  // NO!
#pragma warning restore CS8602

// CORRECT: Proper null handling
var phase = storyline?.CurrentPhase.ToString() ?? string.Empty;
// Or better - make CurrentPhase non-nullable in the model if it's always present
```

### Code Review Checklist

Before submitting, verify:

**Infrastructure (T4):**
- [ ] Cache uses `ConcurrentDictionary` for mutable state
- [ ] Cache uses `IServiceScopeFactory` for scoped client access from Singleton
- [ ] No direct HTTP calls - uses generated `IStorylineClient`/`IQuestClient`

**Events (T5, T3):**
- [ ] Event handlers use `IEventConsumer.RegisterHandler` for fan-out
- [ ] Events are typed models from schema, not anonymous objects

**Configuration (T21):**
- [ ] TTL values come from `ActorServiceConfiguration`, not hardcoded
- [ ] Config schema uses `x-env-name:` with proper naming (`ACTOR_*_MINUTES`)

**Async (T23):**
- [ ] All async methods have `await` (not `return Task.CompletedTask`)
- [ ] Event handlers end with `await Task.CompletedTask` if sync work only

**Type Safety (T25, T26):**
- [ ] Models use `Guid`, `DateTimeOffset`, enums - not strings
- [ ] Nullable fields for optional values (e.g., `Deadline?`)
- [ ] No sentinel values (`Guid.Empty`, `-1`, empty string for "none")
- [ ] Provider uses static `Empty` property, not nullable constructor

**Quality (T10, T12, T19, T22):**
- [ ] Structured logging with message templates, not interpolation
- [ ] XML documentation on all public types, methods, and parameters
- [ ] No `#pragma warning disable` statements
- [ ] Unit tests use `Empty` pattern, not `null!` to bypass NRT
- [ ] Tests use real data with proper types (Guid.NewGuid(), not Guid.Empty)

---

## Implementation Steps

### Phase 1: Infrastructure (Prerequisites)

- [ ] Verify lib-storyline plugin exists with `/storyline/list-by-character` API
- [ ] Verify lib-quest plugin exists with `/quest/list` API
- [ ] Verify `StorylineParticipation` model has `Priority` field (int, not nullable)
- [ ] Add cache TTL config to `actor-configuration.yaml` (with `x-env-name:` per schema rules)
- [ ] Regenerate configuration: `cd scripts && ./generate-config.sh actor`
- [ ] Verify generated config class has non-nullable `StorylineCacheTtlMinutes` property

### Phase 2: Storyline Provider

- [ ] Create `IStorylineCache.cs` interface with XML docs (T19)
- [ ] Create `StorylineCache.cs`:
  - [ ] `ConcurrentDictionary` for cache storage (T9)
  - [ ] `IServiceScopeFactory` for scoped client access (T4)
  - [ ] TTL from configuration, not hardcoded (T21)
  - [ ] Structured logging with message templates (T10)
- [ ] Create `StorylineProvider.cs`:
  - [ ] Static `Empty` property for non-character actors (T12)
  - [ ] Non-nullable constructor parameter (T26)
  - [ ] `_primary` and `_mostRecent` as nullable fields (T26)
  - [ ] Enum values use `.ToString()` for ABML output
  - [ ] Full XML documentation (T19)
- [ ] Register cache in DI as Singleton
- [ ] Register provider in ActorRunner with `Empty` for non-character actors
- [ ] Add event subscriptions in `ActorServiceEvents.cs` (T3, T5):
  - [ ] Use `IEventConsumer.RegisterHandler`
  - [ ] `async` methods with `await Task.CompletedTask` (T23)
- [ ] Write unit tests:
  - [ ] Use real test data with proper types (T25)
  - [ ] Use `StorylineProvider.Empty`, not `null!` (T12)
  - [ ] Test path resolution for all variable paths

### Phase 3: Quest Provider

- [ ] Create `IQuestCache.cs` interface with XML docs (T19)
- [ ] Create `QuestCache.cs` (same patterns as StorylineCache)
- [ ] Create `QuestProvider.cs`:
  - [ ] Static `Empty` property
  - [ ] `_byCode` as regular Dictionary (readonly after construction, T9 n/a)
  - [ ] `Deadline` as nullable `DateTimeOffset?` (T26)
  - [ ] `Status` enum uses `.ToString()` for ABML
- [ ] Register cache in DI as Singleton
- [ ] Register provider in ActorRunner with `Empty` for non-character actors
- [ ] Add event subscriptions (same patterns as storyline)
- [ ] Write unit tests (same patterns as storyline)

### Phase 4: Integration Testing

- [ ] Test ABML access to `${storyline.*}` variables
- [ ] Test ABML access to `${quest.*}` variables
- [ ] Test `${storyline.primary}` vs `${storyline.most_recent}` ordering correctness
- [ ] Test cache invalidation flow via event publication
- [ ] Verify non-character actors get Empty providers without errors

### Phase 5: Documentation

- [ ] Update `docs/guides/ABML.md` with new providers and variable paths
- [ ] Update `docs/guides/ACTOR_SYSTEM.md` with variable provider list
- [ ] Update `docs/planning/REGIONAL_WATCHERS_BEHAVIOR.md`:
  - [ ] Add Resource plugin snapshot pattern for querying other characters
  - [ ] Remove any `service_call` references
  - [ ] Document: providers = self data, Resource snapshots = query others

---

## Appendix: Code Snippets

### StorylineProvider Skeleton

```csharp
/// <summary>
/// Provides ABML variable access to a character's storyline participation data.
/// </summary>
/// <remarks>
/// <para>Variables available:</para>
/// <list type="bullet">
///   <item><description><c>${storyline.is_participant}</c> - bool: Is character in any active storyline?</description></item>
///   <item><description><c>${storyline.active_count}</c> - int: Number of active storylines</description></item>
///   <item><description><c>${storyline.primary.*}</c> - Highest priority storyline details</description></item>
///   <item><description><c>${storyline.most_recent.*}</c> - Most recently joined storyline details</description></item>
/// </list>
/// </remarks>
public sealed class StorylineProvider : IVariableProvider
{
    /// <summary>
    /// Empty provider for non-character actors. Per QUALITY TENETS (T12): use empty response, not null.
    /// </summary>
    public static StorylineProvider Empty { get; } = new(new StorylineListResponse { Storylines = new List<StorylineParticipation>() });

    private readonly List<StorylineParticipation> _storylines;
    private readonly StorylineParticipation? _primary;     // Highest priority (nullable per T26 - no sentinels)
    private readonly StorylineParticipation? _mostRecent;  // Most recently joined (nullable per T26)

    /// <inheritdoc />
    public string Name => "storyline";

    /// <summary>
    /// Creates a new StorylineProvider with the given participation data.
    /// </summary>
    /// <param name="response">The storyline participation response. Use <see cref="Empty"/> for non-character actors.</param>
    public StorylineProvider(StorylineListResponse response)
    {
        // Per FOUNDATION TENETS: no null-forgiving, response is non-nullable
        _storylines = response.Storylines;
        _primary = _storylines.OrderByDescending(s => s.Priority).FirstOrDefault();
        _mostRecent = _storylines.OrderByDescending(s => s.JoinedAt).FirstOrDefault();
    }

    /// <inheritdoc />
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();

        return path[0].ToLowerInvariant() switch
        {
            "is_participant" => _storylines.Count > 0,
            "active_count" => _storylines.Count,
            "active_storylines" => _storylines.Select(s => s.StorylineId.ToString()).ToList(),
            "primary" => path.Length == 1 ? GetStorylineRoot(_primary) : GetStorylineProperty(_primary, path.Slice(1)),
            "most_recent" => path.Length == 1 ? GetStorylineRoot(_mostRecent) : GetStorylineProperty(_mostRecent, path.Slice(1)),
            _ => null
        };
    }

    /// <inheritdoc />
    public object? GetRootValue()
    {
        return new Dictionary<string, object?>
        {
            ["is_participant"] = _storylines.Count > 0,
            ["active_count"] = _storylines.Count,
            ["active_storylines"] = _storylines.Select(s => s.StorylineId.ToString()).ToList(),
            ["primary"] = GetStorylineRoot(_primary),
            ["most_recent"] = GetStorylineRoot(_mostRecent)
        };
    }

    /// <inheritdoc />
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;

        return path[0].ToLowerInvariant() switch
        {
            "is_participant" or "active_count" or "active_storylines" => true,
            "primary" or "most_recent" => path.Length == 1 || CanResolveStorylineProperty(path.Slice(1)),
            _ => false
        };
    }

    private static bool CanResolveStorylineProperty(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;
        return path[0].ToLowerInvariant() is "id" or "template_code" or "phase" or "role" or "priority" or "joined_at";
    }

    private static Dictionary<string, object?>? GetStorylineRoot(StorylineParticipation? storyline)
    {
        if (storyline == null) return null;

        return new Dictionary<string, object?>
        {
            ["id"] = storyline.StorylineId.ToString(),
            ["template_code"] = storyline.TemplateCode,
            ["phase"] = storyline.CurrentPhase.ToString(),  // Enum to string for ABML
            ["role"] = storyline.Role,
            ["priority"] = storyline.Priority,
            ["joined_at"] = storyline.JoinedAt.ToString("o")
        };
    }

    private static object? GetStorylineProperty(StorylineParticipation? storyline, ReadOnlySpan<string> path)
    {
        if (storyline == null || path.Length == 0) return null;

        return path[0].ToLowerInvariant() switch
        {
            "id" => storyline.StorylineId.ToString(),
            "template_code" => storyline.TemplateCode,
            "phase" => storyline.CurrentPhase.ToString(),  // Enum to string for ABML consumption
            "role" => storyline.Role,
            "priority" => storyline.Priority,
            "joined_at" => storyline.JoinedAt.ToString("o"),
            _ => null
        };
    }
}
```

### QuestProvider Skeleton

```csharp
/// <summary>
/// Provides ABML variable access to a character's active quest data.
/// </summary>
/// <remarks>
/// <para>Variables available:</para>
/// <list type="bullet">
///   <item><description><c>${quest.active_count}</c> - int: Number of active quests</description></item>
///   <item><description><c>${quest.has_active}</c> - bool: Has any active quest?</description></item>
///   <item><description><c>${quest.codes}</c> - List: Active quest codes</description></item>
///   <item><description><c>${quest.by_code.CODE.*}</c> - Quest details by code</description></item>
/// </list>
/// </remarks>
public sealed class QuestProvider : IVariableProvider
{
    /// <summary>
    /// Empty provider for non-character actors. Per QUALITY TENETS (T12): use empty response, not null.
    /// </summary>
    public static QuestProvider Empty { get; } = new(new QuestListResponse { Quests = new List<QuestSummary>() });

    private readonly List<QuestSummary> _quests;
    // Dictionary is readonly after construction - no need for ConcurrentDictionary (T9 applies to mutable state)
    private readonly Dictionary<string, QuestSummary> _byCode;

    /// <inheritdoc />
    public string Name => "quest";

    /// <summary>
    /// Creates a new QuestProvider with the given quest data.
    /// </summary>
    /// <param name="response">The quest list response. Use <see cref="Empty"/> for non-character actors.</param>
    public QuestProvider(QuestListResponse response)
    {
        // Per FOUNDATION TENETS: no null-forgiving, response is non-nullable
        _quests = response.Quests;
        _byCode = _quests.ToDictionary(q => q.QuestCode, q => q, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();

        return path[0].ToLowerInvariant() switch
        {
            "active_count" => _quests.Count,
            "has_active" => _quests.Count > 0,
            "codes" => _quests.Select(q => q.QuestCode).ToList(),
            "active_quests" => _quests.Select(QuestToDict).ToList(),
            "by_code" => path.Length > 1 ? GetQuestByCode(path.Slice(1)) : null,
            _ => null
        };
    }

    /// <inheritdoc />
    public object? GetRootValue()
    {
        return new Dictionary<string, object?>
        {
            ["active_count"] = _quests.Count,
            ["has_active"] = _quests.Count > 0,
            ["codes"] = _quests.Select(q => q.QuestCode).ToList(),
            ["active_quests"] = _quests.Select(QuestToDict).ToList()
        };
    }

    /// <inheritdoc />
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;

        return path[0].ToLowerInvariant() switch
        {
            "active_count" or "has_active" or "codes" or "active_quests" => true,
            "by_code" => path.Length > 1,  // Requires quest code as next segment
            _ => false
        };
    }

    private object? GetQuestByCode(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return null;

        var code = path[0];
        if (!_byCode.TryGetValue(code, out var quest)) return null;

        if (path.Length == 1) return QuestToDict(quest);

        return path[1].ToLowerInvariant() switch
        {
            "status" => quest.Status.ToString(),  // Enum to string for ABML
            "progress" => quest.ProgressPercent,
            "deadline" => quest.Deadline?.ToString("o"),  // Nullable per T26 - deadline may not exist
            "current_objective" => ObjectiveToDict(quest.CurrentObjective),
            _ => null
        };
    }

    private static Dictionary<string, object?> QuestToDict(QuestSummary quest)
    {
        return new Dictionary<string, object?>
        {
            ["code"] = quest.QuestCode,
            ["status"] = quest.Status.ToString(),
            ["progress"] = quest.ProgressPercent,
            ["deadline"] = quest.Deadline?.ToString("o"),  // Nullable - may not have deadline
            ["current_objective"] = ObjectiveToDict(quest.CurrentObjective)
        };
    }

    private static Dictionary<string, object?>? ObjectiveToDict(QuestObjective? objective)
    {
        if (objective == null) return null;  // Nullable per T26

        return new Dictionary<string, object?>
        {
            ["id"] = objective.ObjectiveId.ToString(),
            ["description"] = objective.Description,
            ["current_count"] = objective.CurrentCount,
            ["required_count"] = objective.RequiredCount,
            ["is_complete"] = objective.IsComplete,
            ["progress_percent"] = objective.ProgressPercent
        };
    }
}
```

---

## References

### Implementation References
- [IVariableProvider Interface](/home/lysander/repos/bannou/bannou-service/Abml/Expressions/IVariableProvider.cs)
- [PersonalityProvider Reference](/home/lysander/repos/bannou/plugins/lib-actor/Runtime/PersonalityProvider.cs)
- [PersonalityCache Reference](/home/lysander/repos/bannou/plugins/lib-actor/Caching/PersonalityCache.cs)
- [ActorRunner Registration](/home/lysander/repos/bannou/plugins/lib-actor/Runtime/ActorRunner.cs)

### Architecture References
- [Quest Plugin Architecture](/home/lysander/repos/bannou/docs/planning/QUEST_PLUGIN_ARCHITECTURE.md)
- [Regional Watchers Behavior](/home/lysander/repos/bannou/docs/planning/REGIONAL_WATCHERS_BEHAVIOR.md)
- [Actor Data Access Patterns](/home/lysander/repos/bannou/docs/planning/ACTOR_DATA_ACCESS_PATTERNS.md)

### TENET Documentation
- [TENETS.md](/home/lysander/repos/bannou/docs/reference/TENETS.md) - Master tenet index
- [FOUNDATION.md](/home/lysander/repos/bannou/docs/reference/tenets/FOUNDATION.md) - T4, T5, T6
- [IMPLEMENTATION.md](/home/lysander/repos/bannou/docs/reference/tenets/IMPLEMENTATION.md) - T3, T7, T9, T21, T23, T25, T26
- [QUALITY.md](/home/lysander/repos/bannou/docs/reference/tenets/QUALITY.md) - T10, T12, T19, T22
- [SERVICE_HIERARCHY.md](/home/lysander/repos/bannou/docs/reference/SERVICE_HIERARCHY.md) - Dependency handling patterns
