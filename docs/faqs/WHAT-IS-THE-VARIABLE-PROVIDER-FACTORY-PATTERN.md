# What Is the Variable Provider Factory Pattern and Why Does It Matter?

> **Short Answer**: It is the architectural mechanism that allows the Actor runtime (Layer 2) to access personality, encounter, history, and quest data from Layer 4 services without depending on them. Without it, either the service hierarchy breaks or NPCs cannot think.

---

## The Problem

The Actor service sits at Layer 2 (Game Foundation). It executes behavior models -- ABML bytecode that drives NPC decision-making. These behavior models contain expressions like:

```
${personality.aggression} > 0.7 AND ${encounters.last_hostile_days} < 3
```

This expression means: "if this character is aggressive AND recently had a hostile encounter." To evaluate it, the Actor runtime needs data from Character Personality (L4) and Character Encounter (L4).

But the service hierarchy is inviolable: **Layer 2 services cannot depend on Layer 4 services.** Dependencies flow downward only. If Actor imported `ICharacterPersonalityClient` or `ICharacterEncounterClient`, it would be a hierarchy violation that the `ServiceHierarchyValidator` would catch at startup and in unit tests.

So you have a contradiction: the behavior runtime NEEDS L4 data to function, but it CANNOT depend on L4 services. The Variable Provider Factory pattern resolves this contradiction.

---

## How It Works

### Step 1: Define the Interface in Shared Code

The `IVariableProviderFactory` and `IVariableProvider` interfaces live in `bannou-service/` -- the shared project that every service references. These interfaces are layer-agnostic: they belong to no specific service.

```csharp
public interface IVariableProviderFactory
{
    string ProviderName { get; }  // e.g., "personality", "encounters"
    Task<IVariableProvider> CreateAsync(Guid characterId, CancellationToken ct);
}

public interface IVariableProvider
{
    string Namespace { get; }     // e.g., "personality" for ${personality.*}
    object? GetVariable(string name);  // e.g., GetVariable("aggression") -> 0.85
}
```

Actor depends on these interfaces. That is allowed -- shared code is not a service.

### Step 2: L4 Services Implement and Register

Each L4 service that wants to provide data to the behavior system implements the factory:

```csharp
// In lib-character-personality (L4)
public class PersonalityProviderFactory : IVariableProviderFactory
{
    public string ProviderName => "personality";

    public async Task<IVariableProvider> CreateAsync(Guid characterId, CancellationToken ct)
    {
        var data = await _cache.GetOrLoadAsync(characterId, ct);
        return new PersonalityProvider(data);
    }
}
```

The L4 service registers this with DI:
```csharp
services.AddSingleton<IVariableProviderFactory, PersonalityProviderFactory>();
```

This is allowed because L4 depends on shared code (downward dependency).

### Step 3: Actor Discovers Providers at Runtime

The Actor service receives all registered providers via DI collection injection:

```csharp
public class ActorRunner
{
    private readonly IEnumerable<IVariableProviderFactory> _providerFactories;

    public ActorRunner(IEnumerable<IVariableProviderFactory> providerFactories, ...)
    {
        _providerFactories = providerFactories;
    }
}
```

When creating an execution scope for an NPC, Actor iterates over whatever factories happen to be registered:

```csharp
foreach (var factory in _providerFactories)
{
    try
    {
        var provider = await factory.CreateAsync(characterId, ct);
        scope.RegisterProvider(provider);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to create {Provider}", factory.ProviderName);
    }
}
```

Actor does not know WHO provides these variables. It does not know whether zero, three, or ten providers are registered. It discovers at runtime what is available.

---

## Why This Matters Architecturally

### The Hierarchy Stays Clean

The dependency graph looks like this:

```
IVariableProviderFactory (shared code, layer-agnostic)
        ^                          ^
        |                          |
   Actor (L2)              Character Personality (L4)
   depends on interface    implements interface
```

There is no arrow from Actor to Character Personality. Actor depends on the interface. Character Personality depends on the interface. Neither depends on the other. The `ServiceHierarchyValidator` passes because the only service client dependencies Actor has point to L0, L1, or L2 services.

### Graceful Degradation Works

If Character Personality is disabled (valid -- it's an optional L4 service), its factory is never registered. Actor discovers zero personality providers and proceeds without `${personality.*}` variables. Behavior expressions that reference personality variables evaluate to null, which ABML handles as a falsy value.

The NPC loses access to personality-driven decisions but continues functioning with whatever other providers ARE available. This is exactly how optional Layer 4 services are supposed to work.

### New Providers Add Without Code Changes

If a future service (say, a Skills service) wants to provide `${skills.*}` variables to the behavior system, it implements `IVariableProviderFactory`, registers it with DI, and Actor discovers it automatically. No changes to Actor's code. No changes to the schema. No regeneration needed.

This extensibility is what makes the pattern powerful at scale. The behavior system's vocabulary grows by adding providers, not by modifying the runtime.

---

## Where Else This Pattern Appears

The Variable Provider Factory is one instance of a broader pattern used throughout Bannou for cross-layer data access:

| Pattern | L2 Consumer | Interface | L4 Implementors |
|---------|-------------|-----------|-----------------|
| Variable Provider Factory | Actor | `IVariableProviderFactory` | Character Personality, Character Encounter, Character History, Quest |
| Prerequisite Provider Factory | Quest | `IPrerequisiteProviderFactory` | Skills, Magic, Achievement (future) |
| Behavior Document Provider | Actor | `IBehaviorDocumentProvider` | Puppetmaster (loads from Asset service) |

All three follow the same structure: L2 defines what it needs as an interface in shared code, L4 implements and registers, L2 discovers via DI collection. The principle is always the same: **the dependency is on the interface (shared), not on the implementation (L4)**.

---

## The Alternative Would Break the Architecture

Without this pattern, the only options are:

1. **Actor depends on L4 clients directly** -- Hierarchy violation. The validator catches it. Even if you bypassed the validator, Actor would crash if any L4 service were disabled, violating the "L4 is optional" guarantee.

2. **Move personality/encounter/history data into L2** -- Now Character (L2) must understand personality evolution, encounter decay, and historical event recording. A foundational entity becomes a god object. Disabling personality features means touching a foundational service. Layer separation loses its meaning.

3. **Duplicate the data** -- L4 services publish personality values to an Actor-owned cache. Now there are two sources of truth. Cache invalidation becomes a cross-layer coordination problem. And Actor still needs to know what data exists (what namespaces, what fields), which is soft-coupling to L4 concepts.

4. **Use events for everything** -- Actor subscribes to personality change events and maintains its own copy. Same duplication problem as #3, plus Actor now processes personality events for 100,000+ NPCs in addition to running their behavior models. The scaling profile is wrong.

The Variable Provider Factory is not a clever abstraction for its own sake. It is the least-bad solution to a real architectural constraint that exists for good reasons.
