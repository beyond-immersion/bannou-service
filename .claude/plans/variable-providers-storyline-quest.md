# Variable Providers: Storyline & Quest

> **Status**: Ready for Implementation
> **Created**: 2026-02-05
> **Resolved**: 2026-02-05
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
    scope.RegisterProvider(new PersonalityProvider(null));
    // ... etc
}
```

**Key insight:** Providers are instantiated per-execution-scope with pre-loaded data. The cache handles async loading; the provider is synchronous.

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
  description: TTL for cached storyline participation data
  env: STORYLINE_CACHE_TTL

QuestCacheTtlMinutes:
  type: integer
  default: 5
  description: TTL for cached quest data
  env: QUEST_CACHE_TTL
```

**Rationale:** 5-minute TTL matches personality/backstory pattern. Storyline and quest state changes via explicit actions, not high-frequency mutations.

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
[EventSubscription("storyline.character.joined")]
public async Task HandleStorylineJoined(StorylineCharacterJoinedEvent evt, CancellationToken ct)
{
    _storylineCache.Invalidate(evt.CharacterId);
}
```

### Error Handling

Since Storyline/Quest clients are hard dependencies (constructor-injected), service unavailability is a deployment configuration error, not a runtime state to handle gracefully.

**API-level errors (expected):**
```csharp
catch (ApiException ex) when (ex.StatusCode == 404)
{
    // Character has no storylines - valid state, return empty list
    return new StorylineListResponse { Storylines = new() };
}
```

**Infrastructure errors (unexpected):**
```csharp
catch (Exception ex)
{
    // Service should be available - this is a real error
    _logger.LogError(ex, "Failed to load storyline data for character {CharacterId}", characterId);
    throw;  // Let it bubble up - don't hide infrastructure failures
}
```

**Stale-if-error:** For transient network issues, cache implementations MAY return stale data if available, but should log at Error level since the service should be reachable.

---

## Registration

### ActorRunner Changes

In `/home/lysander/repos/bannou/plugins/lib-actor/Runtime/ActorRunner.cs`, add to `BuildExecutionScopeAsync()`:

```csharp
// After existing personality/backstory/encounters registration...

// Load storyline participation data
var storylines = await _storylineCache.GetParticipationOrLoadAsync(CharacterId.Value, ct);
scope.RegisterProvider(new StorylineProvider(storylines));

// Load quest data
var quests = await _questCache.GetActiveQuestsOrLoadAsync(CharacterId.Value, ct);
scope.RegisterProvider(new QuestProvider(quests));
```

For non-character actors:
```csharp
scope.RegisterProvider(new StorylineProvider(null));
scope.RegisterProvider(new QuestProvider(null));
```

### DI Registration

In `ActorService.cs` or `ActorServiceRegistration.cs`:

```csharp
services.AddSingleton<IStorylineCache, StorylineCache>();
services.AddSingleton<IQuestCache, QuestCache>();
```

**Singleton lifetime:** Caches are shared across all ActorRunner instances for memory efficiency.

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
// Primary = highest priority (may not be honored by everything, but informational)
_primary = _storylines.OrderByDescending(s => s.Priority).FirstOrDefault();

// Most recent = most recently joined (separate concept)
_mostRecent = _storylines.OrderByDescending(s => s.JoinedAt).FirstOrDefault();
```

**Provider variables:**
- `${storyline.primary.*}` - highest priority storyline
- `${storyline.most_recent.*}` - most recently joined storyline

**Schema requirement:** Storyline participation must include a `priority` field (int, higher = more important).

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
# schemas/actor-configuration.yaml
StorylineCacheTtlMinutes:
  type: integer
  default: 5
  description: TTL for cached storyline participation data. Per IMPLEMENTATION TENETS, all tunables must be configurable.
  env: ACTOR_STORYLINE_CACHE_TTL

QuestCacheTtlMinutes:
  type: integer
  default: 5
  description: TTL for cached quest data
  env: ACTOR_QUEST_CACHE_TTL
```

```csharp
// CORRECT: Use configuration
var ttl = TimeSpan.FromMinutes(_configuration.StorylineCacheTtlMinutes);

// FORBIDDEN: Hardcoded tunable
var ttl = TimeSpan.FromMinutes(5);  // NO - define in config schema
```

**No secondary fallbacks for defaulted properties:**
```csharp
// FORBIDDEN: Schema has default=5, so this is redundant and dangerous
var ttl = _configuration.StorylineCacheTtlMinutes ?? 5;  // NO!

// CORRECT: Schema default compiles into property initializer
var ttl = _configuration.StorylineCacheTtlMinutes;  // Uses schema default if not overridden
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
    // Arrange
    var response = new StorylineListResponse
    {
        Storylines = new List<StorylineParticipation>
        {
            new() { Priority = 10, CurrentPhase = StorylinePhase.Rising }
        }
    };
    var provider = new StorylineProvider(response);

    // Act
    var result = provider.GetValue(new[] { "primary", "phase" }.AsSpan());

    // Assert
    Assert.Equal("Rising", result);  // Or enum string representation
}

[Fact]
public void GetValue_EmptyStorylines_ReturnsZeroActiveCount()
{
    var provider = new StorylineProvider(new StorylineListResponse { Storylines = new() });
    var result = provider.GetValue(new[] { "active_count" }.AsSpan());
    Assert.Equal(0, result);
}
```

**Do NOT use `null!` to bypass NRT:**
```csharp
// FORBIDDEN: Testing impossible scenario
var provider = new StorylineProvider(null!);  // NO - constructor requires non-null
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

- [ ] Cache uses `ConcurrentDictionary`, not `Dictionary`
- [ ] TTL values come from configuration, not hardcoded
- [ ] Event handlers use `IEventConsumer.RegisterHandler`
- [ ] All async methods have `await` (not `return Task.CompletedTask`)
- [ ] Models use `Guid`, `DateTimeOffset`, enums - not strings
- [ ] Nullable fields for optional values - no sentinel values
- [ ] Structured logging with message templates
- [ ] XML documentation on all public types and members
- [ ] No `#pragma warning disable` statements
- [ ] Unit tests don't use `null!` to bypass NRT

---

## Implementation Steps

### Phase 1: Infrastructure (Prerequisites)

- [ ] Verify lib-storyline plugin exists with `/storyline/list-by-character` API
- [ ] Verify lib-quest plugin exists with `/quest/list` API
- [ ] Add cache TTL config to `actor-configuration.yaml`
- [ ] Regenerate configuration: `cd scripts && ./generate-config.sh actor`

### Phase 2: Storyline Provider

- [ ] Create `IStorylineCache.cs` interface
- [ ] Create `StorylineCache.cs` implementation
- [ ] Create `StorylineProvider.cs` implementation (with `primary` and `most_recent`)
- [ ] Register cache in DI (Singleton)
- [ ] Register provider in ActorRunner
- [ ] Add event subscriptions for cache invalidation
- [ ] Write unit tests for provider path resolution
- [ ] Test empty list handling (character has no storylines)

### Phase 3: Quest Provider

- [ ] Create `IQuestCache.cs` interface
- [ ] Create `QuestCache.cs` implementation
- [ ] Create `QuestProvider.cs` implementation
- [ ] Register cache in DI (Singleton)
- [ ] Register provider in ActorRunner
- [ ] Add event subscriptions for cache invalidation
- [ ] Write unit tests for provider path resolution
- [ ] Test empty list handling (character has no quests)

### Phase 4: Integration Testing

- [ ] Test ABML access to `${storyline.*}` variables
- [ ] Test ABML access to `${quest.*}` variables
- [ ] Test `${storyline.primary}` vs `${storyline.most_recent}` ordering
- [ ] Test cache invalidation flow
- [ ] Update ABML documentation with new variable providers

### Phase 5: Documentation

- [ ] Update `docs/guides/ABML.md` with new providers
- [ ] Update `docs/guides/ACTOR_SYSTEM.md` with variable provider list
- [ ] Update `docs/planning/REGIONAL_WATCHERS_BEHAVIOR.md` with Resource plugin snapshot pattern (NOT service_call)
- [ ] Document distinction: providers = self data, Resource snapshots = query others

---

## Appendix: Code Snippets

### StorylineProvider Skeleton

```csharp
public sealed class StorylineProvider : IVariableProvider
{
    private readonly List<StorylineParticipation> _storylines;
    private readonly StorylineParticipation? _primary;     // Highest priority
    private readonly StorylineParticipation? _mostRecent;  // Most recently joined

    public string Name => "storyline";

    public StorylineProvider(StorylineListResponse response)
    {
        _storylines = response.Storylines;
        _primary = _storylines.OrderByDescending(s => s.Priority).FirstOrDefault();
        _mostRecent = _storylines.OrderByDescending(s => s.JoinedAt).FirstOrDefault();
    }

    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();

        var first = path[0];

        return first.ToLowerInvariant() switch
        {
            "is_participant" => _storylines.Count > 0,
            "active_count" => _storylines.Count,
            "active_storylines" => _storylines.Select(s => s.StorylineId.ToString()).ToList(),
            "primary" => path.Length == 1 ? GetStorylineRoot(_primary) : GetStorylineProperty(_primary, path.Slice(1)),
            "most_recent" => path.Length == 1 ? GetStorylineRoot(_mostRecent) : GetStorylineProperty(_mostRecent, path.Slice(1)),
            _ => null
        };
    }

    private object? GetStorylineProperty(StorylineParticipation? storyline, ReadOnlySpan<string> path)
    {
        if (storyline == null || path.Length == 0) return null;

        return path[0].ToLowerInvariant() switch
        {
            "id" => storyline.StorylineId.ToString(),
            "template_code" => storyline.TemplateCode,
            "phase" => storyline.CurrentPhase,
            "role" => storyline.Role,
            "priority" => storyline.Priority,
            "joined_at" => storyline.JoinedAt.ToString("o"),
            _ => null
        };
    }

    // ... GetRootValue, GetStorylineRoot, CanResolve implementations
}
```

### QuestProvider Skeleton

```csharp
public sealed class QuestProvider : IVariableProvider
{
    private readonly List<QuestSummary> _quests;
    private readonly Dictionary<string, QuestSummary> _byCode;

    public string Name => "quest";

    public QuestProvider(QuestListResponse response)
    {
        _quests = response.Quests;
        _byCode = _quests.ToDictionary(q => q.QuestCode, q => q, StringComparer.OrdinalIgnoreCase);
    }

    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();

        var first = path[0];

        return first.ToLowerInvariant() switch
        {
            "active_count" => _quests.Count,
            "has_active" => _quests.Count > 0,
            "codes" => _quests.Select(q => q.QuestCode).ToList(),
            "active_quests" => _quests.Select(QuestToDict).ToList(),
            "by_code" => path.Length > 1 ? GetQuestByCode(path.Slice(1)) : null,
            _ => null
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
            "status" => quest.Status.ToString(),
            "progress" => quest.ProgressPercent,
            "deadline" => quest.Deadline?.ToString("o"),
            "current_objective" => ObjectiveToDict(quest.CurrentObjective),
            _ => null
        };
    }

    // ... helper methods
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
