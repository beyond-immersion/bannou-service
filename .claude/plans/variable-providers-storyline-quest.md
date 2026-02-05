# Variable Providers: Storyline & Quest

> **Status**: Planning
> **Created**: 2026-02-05
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
| `${storyline.primary}` | `object?` | Primary/most recent storyline details |
| `${storyline.primary.id}` | `string` | Primary storyline ID |
| `${storyline.primary.template_code}` | `string` | Story template code (e.g., "FALL_FROM_GRACE") |
| `${storyline.primary.phase}` | `string` | Current phase of primary storyline |
| `${storyline.primary.role}` | `string` | Character's role in primary storyline |
| `${storyline.primary.started_at}` | `string` | ISO 8601 timestamp |
| `${storyline.has_role(role)}` | N/A | **Not supported** - providers are synchronous, use conditions |

**Note:** Function-style access (`has_role(role)`) is not directly supported by the `IVariableProvider` interface. Instead, expose data that ABML conditions can filter:

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

The Storyline and Quest plugins must exist and expose APIs that the cache can call:

**Storyline Service API (needed):**
```yaml
/storyline/list-by-character:
  summary: List storylines where character participates
  input:
    characterId: uuid
    status: [active]  # Filter to active only
  output:
    storylines: [StorylineParticipation]
```

**Quest Service API (needed):**
```yaml
/quest/list:
  summary: List character's quests
  input:
    characterId: uuid
    status: [active]  # Filter to active only
  output:
    quests: [QuestSummary]
```

**If these APIs don't exist yet:** The cache implementations should gracefully handle `ApiException` with 404 (service not available) and return empty/null data. This allows Actor service to work even when Storyline/Quest plugins are not deployed.

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

### Graceful Degradation

When Storyline/Quest services are unavailable:

1. **On cache miss + API failure:** Return empty/null data (not crash)
2. **On cache hit but stale + API failure:** Return stale data (better than nothing)
3. **Log warnings** but don't emit error events (optional service)

```csharp
catch (ApiException ex) when (ex.StatusCode == 404)
{
    _logger.LogDebug("Storyline service returned 404 for character {CharacterId}", characterId);
    return null;  // Character has no storylines - valid state
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to load storyline data for character {CharacterId}", characterId);
    return cached?.Data;  // Return stale if available
}
```

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
- **lib-storyline**: Exists but ~30% complete (has GOAP plan composition, missing Scenario APIs, instances, Variable Provider support)
- **lib-quest**: Does not exist yet (implementation plan in `sharded-waddling-narwhal.md`)

**Decision:** ✅ Implement providers now with graceful degradation. Wire to real clients once APIs exist. Providers can ship before services are complete.

**Implementation:**
- Cache returns `null` when client unavailable
- Provider returns `is_participant = false`, `active_count = 0`, etc. for null data
- Log at Debug level (not Warning) since this is expected for optional services

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

**Decision:** ✅ Keep providers character-scoped. Orchestrators use `service_call` for querying other characters.

**Rationale:**
This aligns with the passive vs active architecture:
- **Providers** = MY data (self-referential, cached)
- **service_call** = QUERY the world (any character, real-time)

**Documentation requirement:** Add to ABML docs and Regional Watchers doc:

```yaml
# Regional Watcher behavior - query storyline for candidate character
- service_call:
    service: storyline
    method: list-by-character
    parameters:
      characterId: "${candidate.id}"
      status: ["active"]
    result_variable: candidate_storylines
```

### 4. Primary Storyline Selection

**Question:** How do we determine "primary" storyline when character has multiple?

**Decision:** ✅ Most recently joined (sort by `joined_at` descending, take first).

**Rationale:**
- Simple and deterministic (no ambiguity or extra state)
- Matches cognitive intuition ("top of mind" is what you just got into)
- No additional schema fields required
- Can enhance with priority/urgency sorting later if needed

**Implementation:**
```csharp
_primary = _storylines.OrderByDescending(s => s.JoinedAt).FirstOrDefault();
```

**Future enhancement:** Consider adding `last_phase_transition_at` for "most recently active" sorting if "most recently joined" proves insufficient.

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
- [ ] Create `StorylineProvider.cs` implementation
- [ ] Register cache in DI
- [ ] Register provider in ActorRunner
- [ ] Add event subscriptions for cache invalidation
- [ ] Write unit tests for provider path resolution
- [ ] Test empty/null handling for graceful degradation

### Phase 3: Quest Provider

- [ ] Create `IQuestCache.cs` interface
- [ ] Create `QuestCache.cs` implementation
- [ ] Create `QuestProvider.cs` implementation
- [ ] Register cache in DI
- [ ] Register provider in ActorRunner
- [ ] Add event subscriptions for cache invalidation
- [ ] Write unit tests for provider path resolution
- [ ] Test empty/null handling for graceful degradation

### Phase 4: Integration Testing

- [ ] Test ABML access to `${storyline.*}` variables
- [ ] Test ABML access to `${quest.*}` variables
- [ ] Test cache invalidation flow
- [ ] Test graceful degradation when services unavailable
- [ ] Update ABML documentation with new variable providers

### Phase 5: Documentation

- [ ] Update `docs/guides/ABML.md` with new providers
- [ ] Update `docs/guides/ACTOR_SYSTEM.md` with variable provider list
- [ ] Add examples to `docs/planning/REGIONAL_WATCHERS_BEHAVIOR.md`

---

## Appendix: Code Snippets

### StorylineProvider Skeleton

```csharp
public sealed class StorylineProvider : IVariableProvider
{
    private readonly List<StorylineParticipation> _storylines;
    private readonly StorylineParticipation? _primary;

    public string Name => "storyline";

    public StorylineProvider(StorylineListResponse? response)
    {
        _storylines = response?.Storylines ?? new List<StorylineParticipation>();
        _primary = _storylines.OrderByDescending(s => s.JoinedAt).FirstOrDefault();
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
            "primary" => path.Length == 1 ? GetPrimaryRoot() : GetPrimaryProperty(path.Slice(1)),
            _ => null
        };
    }

    private object? GetPrimaryProperty(ReadOnlySpan<string> path)
    {
        if (_primary == null || path.Length == 0) return null;

        return path[0].ToLowerInvariant() switch
        {
            "id" => _primary.StorylineId.ToString(),
            "template_code" => _primary.TemplateCode,
            "phase" => _primary.CurrentPhase,
            "role" => _primary.Role,
            "started_at" => _primary.JoinedAt.ToString("o"),
            _ => null
        };
    }

    // ... GetRootValue, CanResolve implementations
}
```

### QuestProvider Skeleton

```csharp
public sealed class QuestProvider : IVariableProvider
{
    private readonly List<QuestSummary> _quests;
    private readonly Dictionary<string, QuestSummary> _byCode;

    public string Name => "quest";

    public QuestProvider(QuestListResponse? response)
    {
        _quests = response?.Quests ?? new List<QuestSummary>();
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

- [IVariableProvider Interface](/home/lysander/repos/bannou/bannou-service/Abml/Expressions/IVariableProvider.cs)
- [PersonalityProvider Reference](/home/lysander/repos/bannou/plugins/lib-actor/Runtime/PersonalityProvider.cs)
- [PersonalityCache Reference](/home/lysander/repos/bannou/plugins/lib-actor/Caching/PersonalityCache.cs)
- [ActorRunner Registration](/home/lysander/repos/bannou/plugins/lib-actor/Runtime/ActorRunner.cs)
- [Quest Plugin Architecture](/home/lysander/repos/bannou/docs/planning/QUEST_PLUGIN_ARCHITECTURE.md)
- [Regional Watchers Behavior](/home/lysander/repos/bannou/docs/planning/REGIONAL_WATCHERS_BEHAVIOR.md)
- [Actor Data Access Patterns](/home/lysander/repos/bannou/docs/planning/ACTOR_DATA_ACCESS_PATTERNS.md)
