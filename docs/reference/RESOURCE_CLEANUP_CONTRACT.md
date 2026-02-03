# Resource Cleanup Contract Violation - Post-Mortem

> **Date**: 2026-02-03
> **Severity**: Architectural Violation
> **Status**: Documenting for prevention

---

## What Happened

When implementing L4 cleanup for Character's dependent services (CharacterPersonality, CharacterHistory), the wrong fix was applied.

### The Existing State

1. **CharacterPersonality** and **CharacterHistory** both have `x-references` in their schemas:
   ```yaml
   x-references:
     - target: character
       sourceType: character-personality
       onDelete: cascade
       cleanup:
         endpoint: /character-personality/cleanup-by-character
         payloadTemplate: '{"characterId": "{{resourceId}}"}'
   ```

2. These services **register cleanup callbacks** with lib-resource at startup (auto-generated from `x-references`)

3. The **Resource cleanup pattern** (documented in `docs/plugins/RESOURCE.md`) requires:
   - Consumer (L4): Declares `x-references` → registers callback
   - Producer (L2): Calls `/resource/cleanup/execute` → invokes callbacks

4. **Character service does NOT call `ExecuteCleanupAsync`** in its delete flow

### The Wrong Fix

Instead of fixing Character to call `ExecuteCleanupAsync`, event handlers were added:
- `CharacterPersonalityServiceEvents.cs` - subscribes to `character.deleted`
- `CharacterHistoryServiceEvents.cs` - subscribes to `character.deleted`

### Why This Is Wrong

1. **Duplicates cleanup paths**: Now there are TWO mechanisms (Resource callbacks + event handlers) doing the same thing
2. **Makes `x-references` declarations lies**: The schema says cleanup happens via Resource pattern, but it actually happens via events
3. **Bypasses schema-first validation**: Can't validate cleanup chains by inspecting schemas alone
4. **Creates inconsistency**: Realm uses Resource pattern correctly; Character now uses a hybrid mess
5. **Masks the real bug**: Character's missing `ExecuteCleanupAsync` call is the actual problem

### The Correct Fix

**Fix Character's delete flow to call `ExecuteCleanupAsync`**, matching how Realm does it:

```csharp
// In CharacterService.DeleteCharacterAsync, BEFORE deleting:
var resourceClient = _serviceProvider.GetService<IResourceClient>();
if (resourceClient != null)
{
    try
    {
        var checkResult = await resourceClient.CheckAsync(new CheckResourceRequest
        {
            ResourceType = "character",
            ResourceId = characterId
        }, cancellationToken);

        if (checkResult?.RefCount > 0)
        {
            var cleanupResult = await resourceClient.ExecuteCleanupAsync(
                new ExecuteCleanupRequest
                {
                    ResourceType = "character",
                    ResourceId = characterId,
                    CleanupPolicy = CleanupPolicy.ALL_REQUIRED
                }, cancellationToken);

            if (!cleanupResult.Success)
            {
                return StatusCodes.Conflict;
            }
        }
    }
    catch (ApiException ex) when (ex.StatusCode == 404)
    {
        // No references registered - normal
    }
}
```

Then **remove the event handlers** that were added as a workaround.

---

## The Two-Sided Contract

`x-references` creates a **contract between two services**:

```
┌─────────────────────────────────────────────────────────────────┐
│                    x-references CONTRACT                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  CONSUMER SIDE (L4 service with x-references)                   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ 1. Declares x-references in schema                       │   │
│  │ 2. Gets auto-generated ReferenceTracking.cs              │   │
│  │ 3. Registers cleanup callback at startup (OnRunningAsync)│   │
│  │ 4. Implements cleanup endpoint                           │   │
│  └─────────────────────────────────────────────────────────┘   │
│                           ▲                                     │
│                           │ EXPECTS                             │
│                           ▼                                     │
│  PRODUCER SIDE (L2 service being targeted)                      │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ 1. Injects IResourceClient                               │   │
│  │ 2. Calls /resource/check before delete                   │   │
│  │ 3. Calls /resource/cleanup/execute if refs exist         │   │
│  │ 4. Only proceeds with delete after cleanup succeeds      │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│  IF PRODUCER SIDE IS MISSING → x-references IS A LIE           │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Both sides MUST be implemented.** If the producer side is missing:
- DO NOT add event handlers as a workaround
- DO fix the producer to call ExecuteCleanupAsync

---

## How The Mistake Happened

1. Was told to read `docs/plugins/RESOURCE.md` for patterns
2. Read it - saw the correct pattern (producer calls ExecuteCleanupAsync)
3. Saw CharacterPersonality/CharacterHistory have `x-references` with cleanup endpoints
4. Verified Character does NOT call ExecuteCleanupAsync
5. **FAILURE POINT**: Instead of fixing Character, looked at CharacterEncounterServiceEvents.cs and copied that event-handler pattern
6. Assumed existing code was correct; didn't recognize it as another workaround
7. Added event handlers that duplicate the cleanup callback functionality

**Root cause**: Saw an inconsistency between documented pattern and actual code. Instead of fixing the code to match the pattern, perpetuated the workaround.

---

## Validation Checklist

When a service has `x-references` consumers, verify:

- [ ] Producer service has `IResourceClient` injected
- [ ] Producer's delete flow calls `/resource/check`
- [ ] Producer's delete flow calls `/resource/cleanup/execute` if refs exist
- [ ] Producer handles cleanup failure (returns Conflict or similar)
- [ ] NO event handlers exist that duplicate the cleanup callback logic

**If event handlers exist for cleanup AND `x-references` cleanup callbacks exist:**
This is a violation. One mechanism should be removed.

---

## Current Violations To Fix

| Producer | Has x-references Consumers? | Calls ExecuteCleanupAsync? | Has Duplicate Event Handlers? | Status |
|----------|----------------------------|---------------------------|------------------------------|--------|
| Realm | Yes (realm-history) | ✅ Yes | No | CORRECT |
| Character | Yes (personality, history, encounter, actor) | ❌ No | Yes (just added) | VIOLATION - needs fix |

---

## Proposed Tenet Addition

Add to `docs/reference/tenets/IMPLEMENTATION.md` or create new rule in SCHEMA-RULES.md:

### x-references Cleanup Contract (MANDATORY)

**When your service is the `target` of `x-references` declarations from other services, your delete flow MUST:**

1. Inject `IResourceClient`
2. Call `/resource/check` to detect references
3. Call `/resource/cleanup/execute` to invoke registered callbacks
4. Handle cleanup failure appropriately (typically return Conflict)

**FORBIDDEN:**
- Adding event handlers that duplicate cleanup callback logic
- Deleting without calling ExecuteCleanupAsync when x-references consumers exist
- Assuming event-based cleanup is "equivalent" to Resource cleanup

**Why this matters:**
- `x-references` is a schema-first declaration of cleanup contracts
- Cleanup chains can be validated by inspecting schemas
- Event handlers bypass this validation and create hidden dependencies
- Duplicate mechanisms cause confusion and maintenance burden

**Validation:**
```bash
# Find all x-references targets
grep -r "target:" schemas/*-api.yaml | grep -v "#"

# For each target, verify the service calls ExecuteCleanupAsync
grep -l "ExecuteCleanupAsync" plugins/lib-{target}/
```

---

## Action Items

1. [ ] Fix Character's delete flow to call ExecuteCleanupAsync
2. [ ] Remove CharacterPersonalityServiceEvents.cs (the file just added)
3. [ ] Remove CharacterHistoryServiceEvents.cs (the file just added)
4. [ ] Revert schema changes to character-personality-events.yaml
5. [ ] Revert schema changes to character-history-events.yaml
6. [ ] Investigate if CharacterEncounter's event handler is also a workaround
7. [ ] Add tenet/rule about x-references cleanup contract
8. [ ] Add automated validation to CI (check that x-references targets call ExecuteCleanupAsync)

---

## Lessons Learned

1. **When documentation says X and code does Y, fix the code** - don't add more workarounds
2. **Don't assume existing code patterns are correct** - they may be workarounds too
3. **Schema-first means the schema is the source of truth** - if `x-references` declares a cleanup contract, honor it
4. **Ask before implementing** when you see inconsistencies - "Should I fix the root cause or work around it?"
5. **Duplicate mechanisms are a code smell** - if two things do the same job, one is probably wrong
