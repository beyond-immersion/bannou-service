# Why Does the Resource Service Exist at L1 Instead of Letting Services Manage Their Own Cleanup?

> **Short Answer**: Because foundational services (L2) cannot know about the higher-layer services (L3/L4) that reference their entities. Without a layer-agnostic intermediary, you either violate the service hierarchy or accept orphaned data and unsafe deletions.

---

## The Problem Resource Solves

Consider the Character service (L2 Game Foundation). A character can be referenced by:

- **Actor** (L2): An NPC brain running behavior for this character.
- **Character Personality** (L4): Personality traits associated with this character.
- **Character History** (L4): Historical events this character participated in.
- **Character Encounter** (L4): Memorable interactions this character had.

When someone tries to delete a character, the Character service needs to answer: "Is anything still referencing this character? If so, should the deletion be blocked (RESTRICT), should referencing data be cleaned up first (CASCADE), or should references be silently detached (DETACH)?"

The problem is that Character (L2) **cannot depend on** Actor, Character Personality, Character History, or Character Encounter. The service hierarchy forbids L2 from depending on L4. Character cannot inject `ICharacterPersonalityClient` to ask "do you have data for this character?" -- that would be a hierarchy violation.

---

## The Three Bad Alternatives

### Option 1: Character Depends on L4 Services (Hierarchy Violation)

```csharp
// IN CHARACTER SERVICE (L2) -- FORBIDDEN
public class CharacterService
{
    private readonly ICharacterPersonalityClient _personality;  // L4 -- VIOLATION
    private readonly ICharacterEncounterClient _encounters;     // L4 -- VIOLATION

    public async Task<bool> CanDeleteAsync(Guid characterId)
    {
        // Character now knows about every service that references it
        var hasPersonality = await _personality.ExistsAsync(characterId);
        var hasEncounters = await _encounters.QueryByCharacterAsync(characterId);
        // ...and must be updated every time a new L4 service starts referencing characters
    }
}
```

This violates the hierarchy and creates an ever-growing list of dependencies. Every new service that references characters requires modifying the Character service.

### Option 2: Higher-Layer Services Clean Up After the Fact (Orphan Risk)

```csharp
// Character just deletes. L4 services subscribe to character.deleted and clean up.
// Problem: What if Personality's event handler fails? Now we have orphaned personality data.
// Problem: Character has no way to PREVENT deletion when active references exist.
```

This approach works for cleanup (and Bannou uses it as a complement), but it cannot implement RESTRICT semantics. The character is already deleted by the time L4 services learn about it. If the business rule is "cannot delete a character that has an active actor brain," event-driven cleanup is too late.

### Option 3: Every Service Tracks Its Own References (Duplication)

Each L2 service independently implements reference counting for its own entities. Character tracks character references. Realm tracks realm references. Location tracks location references. The reference counting logic, grace period handling, cleanup coordination, and compression pipeline are duplicated across every foundational service.

This is wasteful and error-prone. The machinery is identical -- only the entity types differ.

---

## What Resource Actually Does

Resource is a **layer-agnostic intermediary** at L1 that provides three capabilities:

### 1. Reference Tracking

Higher-layer services call the Resource API directly when they create or remove references to foundational resources:

```
Actor (L2) spawns actor for Character X
    -> Calls: IResourceClient.RegisterReferenceAsync (resourceType: "character", resourceId: X, sourceType: "actor", sourceId: actorId)

Character Personality (L4) creates personality for Character X
    -> Calls: IResourceClient.RegisterReferenceAsync (resourceType: "character", resourceId: X, sourceType: "character-personality", sourceId: personalityId)
```

Resource maintains a set of references per resource using Redis atomic set operations. When Character wants to check if it can be deleted, it calls Resource:

```
Character (L2) -> Resource (L1): /resource/check (resourceType: "character", resourceId: X)
    <- Returns: { references: [{sourceType: "actor", ...}, {sourceType: "character-personality", ...}], count: 2 }
```

Character depends on Resource (L1 -> L1: allowed). Resource depends on nothing above L0. The L4 services depend on Resource (L4 -> L1: allowed). No hierarchy violations anywhere.

### 2. Cleanup Coordination

Services register cleanup callbacks with Resource. When a character deletion proceeds (CASCADE policy), Resource executes the registered callbacks:

```
Actor registers: "When cleaning character references, call /actor/cleanup-by-character"
Character Personality registers: "When cleaning character references, call /character-personality/cleanup-by-character"

Character (L2) -> Resource (L1): /resource/execute-cleanup (resourceType: "character", resourceId: X)
    -> Resource calls: /actor/cleanup-by-character
    -> Resource calls: /character-personality/cleanup-by-character
    -> Resource calls: /character-encounter/delete-by-character
    -> Resource calls: /character-history/delete-all
```

Resource does not know what these callbacks do. It stores endpoint URLs and calls them. The same machinery works for any resource type -- characters, realms, locations, or any future foundational entity.

### 3. Hierarchical Compression

When a character dies in Arcadia, their data does not just get deleted -- it gets compressed into an archive that feeds the content flywheel. Resource centralizes this:

- L4 services register compression callbacks (e.g., "to compress character personality data, call this endpoint").
- Resource orchestrates the compression, collecting data from each registered source.
- The compressed archive is stored in MySQL for long-term durability.
- Storyline (L4) later queries these archives to generate narrative seeds from accumulated play history.

Without centralized compression, each service would need its own archive format, its own compression trigger, and its own storage. Resource provides one archive per resource, one compression pipeline, and one storage backend.

---

## Why L1?

Resource is at L1 (App Foundation) for the same reason Contract is at L1: it provides domain-agnostic infrastructure that all layers need.

- **L2 services** need to check references before deletion (Character checks for L4 references).
- **L3 services** could need it if they ever manage deletable resources.
- **L4 services** publish references and register callbacks.

If Resource were at L2, L3 services could not use it (L3 cannot depend on L2 -- they are separate branches). If Resource were at L4, L2 services could not use it (L2 cannot depend on L4). L1 is the only layer that all other layers can depend on.

Resource uses **opaque string identifiers** for resource types and source types (e.g., `"character"`, `"actor"`, `"character-personality"`) specifically to avoid importing type definitions from higher layers. It has no concept of what a "character" or "actor" is. It tracks references between string-identified entities and calls HTTP endpoints when asked. This opacity is what makes it safe at L1.

---

## The Content Flywheel Connection

Resource is explicitly listed in the Vision document as a critical component of the content flywheel:

```
Player Actions -> Character History / Realm History
    -> Resource Service (compression)
    -> Character dies, archive created
    -> Storyline Composer (narrative seeds from archives)
    -> Regional Watchers / Puppetmaster (orchestrated scenarios)
    -> New Player Experiences
```

Without Resource, there is no centralized compression pipeline. Without centralized compression, there are no unified archives. Without unified archives, Storyline has no data to generate narratives from. Without narratives, the content flywheel stops spinning.

Resource is infrastructure that enables the single most important architectural thesis of the entire platform: more play produces more content, which produces more play. That is why it exists at L1 -- it is foundational to the entire system's purpose, not just to game mechanics.
