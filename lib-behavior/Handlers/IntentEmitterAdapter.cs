// =============================================================================
// Intent Emitter Adapter
// Bridges lib-behavior emitters with bannou-service interfaces.
// =============================================================================

using BeyondImmersion.BannouService.Behavior.Archetypes;
using CoreBehavior = BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.BannouService.Behavior.Handlers;

/// <summary>
/// Adapter that implements bannou-service IIntentEmitterRegistry using lib-behavior's implementation.
/// </summary>
public sealed class IntentEmitterRegistryAdapter : CoreBehavior.IIntentEmitterRegistry
{
    private readonly IntentEmitterRegistry _inner;
    private readonly IArchetypeRegistry _archetypeRegistry;

    /// <summary>
    /// Creates a new adapter wrapping the given registry.
    /// </summary>
    /// <param name="inner">The lib-behavior registry to wrap.</param>
    /// <param name="archetypeRegistry">Archetype registry for context conversion.</param>
    public IntentEmitterRegistryAdapter(
        IntentEmitterRegistry inner,
        IArchetypeRegistry archetypeRegistry)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _archetypeRegistry = archetypeRegistry ?? throw new ArgumentNullException(nameof(archetypeRegistry));
    }

    /// <inheritdoc/>
    public CoreBehavior.IIntentEmitter? GetEmitter(
        string actionName, CoreBehavior.IntentEmissionContext context)
    {
        // Convert core context to lib-behavior context
        var archetype = ResolveArchetype(context.Archetype);
        var internalContext = new IntentEmissionContext
        {
            EntityId = context.EntityId,
            Archetype = archetype,
            DocumentType = context.DocumentType,
            Data = context.Data
        };

        var emitter = _inner.GetEmitter(actionName, internalContext);
        if (emitter == null)
        {
            return null;
        }

        return new IntentEmitterWrapper(emitter, _archetypeRegistry);
    }

    /// <inheritdoc/>
    public bool HasEmitter(string actionName) => _inner.HasEmitter(actionName);

    /// <inheritdoc/>
    public IReadOnlySet<string> GetActionNames() => _inner.GetActionNames().ToHashSet();

    private ArchetypeDefinition ResolveArchetype(CoreBehavior.IArchetypeDefinition? coreArchetype)
    {
        if (coreArchetype == null)
        {
            return _archetypeRegistry.GetDefaultArchetype();
        }

        // Try to get from registry by ID
        var archetype = _archetypeRegistry.GetArchetype(coreArchetype.Id);
        if (archetype != null)
        {
            return archetype;
        }

        // Fallback to default
        return _archetypeRegistry.GetDefaultArchetype();
    }
}

/// <summary>
/// Wraps a lib-behavior IIntentEmitter to implement bannou-service IIntentEmitter.
/// </summary>
internal sealed class IntentEmitterWrapper : CoreBehavior.IIntentEmitter
{
    private readonly IIntentEmitter _inner;
    private readonly IArchetypeRegistry _archetypeRegistry;

    public IntentEmitterWrapper(IIntentEmitter inner, IArchetypeRegistry archetypeRegistry)
    {
        _inner = inner;
        _archetypeRegistry = archetypeRegistry;
    }

    public string ActionName => _inner.ActionName;

    public IReadOnlySet<string> SupportedDocumentTypes => _inner.SupportedDocumentTypes;

    public bool CanEmit(string actionName, CoreBehavior.IntentEmissionContext context)
    {
        var internalContext = ConvertContext(context);
        return _inner.CanEmit(actionName, internalContext);
    }

    public async ValueTask<IReadOnlyList<CoreBehavior.IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        CoreBehavior.IntentEmissionContext context,
        CancellationToken ct)
    {
        var internalContext = ConvertContext(context);
        var emissions = await _inner.EmitAsync(parameters, internalContext, ct);

        // Convert to core emissions
        return emissions.Select(e => e.ToCore()).ToList();
    }

    private IntentEmissionContext ConvertContext(CoreBehavior.IntentEmissionContext context)
    {
        var archetype = context.Archetype != null
            ? _archetypeRegistry.GetArchetype(context.Archetype.Id) ?? _archetypeRegistry.GetDefaultArchetype()
            : _archetypeRegistry.GetDefaultArchetype();

        return new IntentEmissionContext
        {
            EntityId = context.EntityId,
            Archetype = archetype,
            DocumentType = context.DocumentType,
            Data = context.Data
        };
    }
}
