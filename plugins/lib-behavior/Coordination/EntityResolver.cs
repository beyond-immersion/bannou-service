// =============================================================================
// Entity Resolver Implementation
// Resolves semantic binding names (hero, villain) to entity IDs in cutscenes.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.Bannou.Behavior.Coordination;

/// <summary>
/// Default implementation of <see cref="IEntityResolver"/>.
/// </summary>
/// <remarks>
/// Resolution priority:
/// <list type="number">
/// <item>Explicit participant bindings</item>
/// <item>Explicit prop bindings</item>
/// <item>Role-based lookup (e.g., "protagonist" → bound participant with that role)</item>
/// <item>Special bindings ("self", "target") from context</item>
/// </list>
/// </remarks>
public sealed class EntityResolver : IEntityResolver
{
    private readonly ILogger<EntityResolver>? _logger;
    private readonly ITelemetryProvider? _telemetryProvider;

    /// <summary>
    /// Special binding names that are resolved from context rather than explicit bindings.
    /// </summary>
    private static readonly HashSet<string> ContextBindings = new(StringComparer.OrdinalIgnoreCase)
    {
        "self",
        "target",
        "current_target",
        "initiator",
        "player"
    };

    /// <summary>
    /// Creates a new entity resolver.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    /// <param name="telemetryProvider">Optional telemetry provider for span instrumentation.</param>
    public EntityResolver(ILogger<EntityResolver>? logger = null, ITelemetryProvider? telemetryProvider = null)
    {
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public async Task<EntityReference?> ResolveAsync(
        string bindingName,
        CutsceneBindings bindings,
        EntityResolutionContext? context = null,
        CancellationToken ct = default)
    {
        using var activity = _telemetryProvider?.StartActivity("bannou.behavior", "EntityResolver.ResolveAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);

        // Yield to ensure proper async pattern per IMPLEMENTATION TENETS
        await Task.Yield();

        var reference = ResolveInternal(bindingName, bindings, context);

        if (reference == null)
        {
            _logger?.LogDebug(
                "Failed to resolve binding '{BindingName}' - not found in participants, props, or context",
                bindingName);
        }
        else
        {
            _logger?.LogTrace(
                "Resolved binding '{BindingName}' to entity {EntityId}",
                bindingName,
                reference.EntityId);
        }

        return reference;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, EntityReference>> ResolveManyAsync(
        IEnumerable<string> bindingNames,
        CutsceneBindings bindings,
        EntityResolutionContext? context = null,
        CancellationToken ct = default)
    {
        using var activity = _telemetryProvider?.StartActivity("bannou.behavior", "EntityResolver.ResolveManyAsync");

        // Yield to ensure proper async pattern per IMPLEMENTATION TENETS
        await Task.Yield();

        var results = new Dictionary<string, EntityReference>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in bindingNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var reference = ResolveInternal(name, bindings, context);
            if (reference != null)
            {
                results[name] = reference;
            }
        }

        _logger?.LogDebug(
            "Resolved {ResolvedCount} of {RequestedCount} bindings",
            results.Count,
            bindingNames.Count());

        return results;
    }

    /// <inheritdoc/>
    public bool CanResolve(string bindingName, CutsceneBindings bindings)
    {
        if (string.IsNullOrWhiteSpace(bindingName))
            return false;


        // Check explicit bindings (case-insensitive)
        if (ContainsCaseInsensitive(bindings.Participants, bindingName))
            return true;

        if (ContainsCaseInsensitive(bindings.Props, bindingName))
            return true;

        // Check role-based resolution (case-insensitive)
        if (TryGetCaseInsensitive(bindings.Roles, bindingName, out var targetBinding))
        {
            return ContainsCaseInsensitive(bindings.Participants, targetBinding) ||
                    ContainsCaseInsensitive(bindings.Props, targetBinding);
        }

        // Context bindings are always potentially resolvable
        // (actual resolution depends on context values)
        if (ContextBindings.Contains(bindingName))
            return true;

        return false;
    }

    /// <summary>
    /// Internal resolution logic.
    /// </summary>
    private EntityReference? ResolveInternal(
        string bindingName,
        CutsceneBindings bindings,
        EntityResolutionContext? context)
    {
        // 1. Check explicit participant bindings (highest priority)
        if (TryGetCaseInsensitive(bindings.Participants, bindingName, out var participant))
        {
            return participant;
        }

        // 2. Check explicit prop bindings
        if (TryGetCaseInsensitive(bindings.Props, bindingName, out var prop))
        {
            return prop;
        }

        // 3. Check role-based resolution
        if (TryGetCaseInsensitive(bindings.Roles, bindingName, out var targetBindingName))
        {
            // Role points to another binding name
            if (TryGetCaseInsensitive(bindings.Participants, targetBindingName, out var roleParticipant))
            {
                return roleParticipant;
            }

            if (TryGetCaseInsensitive(bindings.Props, targetBindingName, out var roleProp))
            {
                return roleProp;
            }
        }

        // 4. Check context-based bindings
        if (context != null)
        {
            var contextResolved = ResolveFromContext(bindingName, context);
            if (contextResolved != null)
            {
                return contextResolved;
            }
        }

        // 5. Check custom bindings (may contain EntityReference directly)
        if (TryGetCaseInsensitive(bindings.Custom, bindingName, out var customValue))
        {
            if (customValue is EntityReference customRef)
            {
                return customRef;
            }

            if (customValue is Guid customGuid)
            {
                return EntityReference.FromId(customGuid);
            }

            if (customValue is string customString && Guid.TryParse(customString, out var parsedGuid))
            {
                return EntityReference.FromId(parsedGuid);
            }
        }

        return null;
    }

    /// <summary>
    /// Case-insensitive dictionary lookup helper.
    /// </summary>
    private static bool TryGetCaseInsensitive<TValue>(
        IReadOnlyDictionary<string, TValue> dictionary,
        string key,
        [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TValue value)
    {
        // Try exact match first (most common case)
        if (dictionary.TryGetValue(key, out var foundValue))
        {
            value = foundValue;
            return true;
        }

        // Fall back to case-insensitive search
        foreach (var kvp in dictionary)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Case-insensitive dictionary key check helper.
    /// </summary>
    private static bool ContainsCaseInsensitive<TValue>(
        IReadOnlyDictionary<string, TValue> dictionary,
        string key)
    {
        if (dictionary.ContainsKey(key))
            return true;

        foreach (var kvp in dictionary)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves special context-based bindings.
    /// </summary>
    private EntityReference? ResolveFromContext(string bindingName, EntityResolutionContext context)
    {
        var normalizedName = bindingName.ToLowerInvariant();

        switch (normalizedName)
        {
            case "self":
                if (context.RequestingEntity.HasValue && context.RequestingEntity.Value != Guid.Empty)
                {
                    return EntityReference.FromId(context.RequestingEntity.Value);
                }
                break;

            case "target":
            case "current_target":
                if (context.Variables?.TryGetValue("target", out var targetValue) == true)
                {
                    return ExtractEntityReference(targetValue);
                }
                if (context.Variables?.TryGetValue("current_target", out var currentTarget) == true)
                {
                    return ExtractEntityReference(currentTarget);
                }
                break;

            case "initiator":
                if (context.Variables?.TryGetValue("initiator", out var initiator) == true)
                {
                    return ExtractEntityReference(initiator);
                }
                break;

            case "player":
                if (context.Variables?.TryGetValue("player", out var player) == true)
                {
                    return ExtractEntityReference(player);
                }
                if (context.Variables?.TryGetValue("player_id", out var playerId) == true)
                {
                    return ExtractEntityReference(playerId);
                }
                break;
        }

        // Try direct variable lookup by binding name
        if (context.Variables?.TryGetValue(bindingName, out var directValue) == true)
        {
            return ExtractEntityReference(directValue);
        }

        return null;
    }

    /// <summary>
    /// Extracts an entity reference from a variable value.
    /// </summary>
    private static EntityReference? ExtractEntityReference(object? value)
    {
        if (value == null)
            return null;

        if (value is EntityReference reference)
            return reference;

        if (value is Guid guid && guid != Guid.Empty)
            return EntityReference.FromId(guid);

        if (value is string str && Guid.TryParse(str, out var parsed) && parsed != Guid.Empty)
            return EntityReference.FromId(parsed);

        // Handle dictionary with id field (common pattern)
        if (value is IDictionary<string, object?> dict)
        {
            if (dict.TryGetValue("id", out var idValue))
            {
                return ExtractEntityReference(idValue);
            }
            if (dict.TryGetValue("entity_id", out var entityIdValue))
            {
                return ExtractEntityReference(entityIdValue);
            }
        }

        return null;
    }
}
