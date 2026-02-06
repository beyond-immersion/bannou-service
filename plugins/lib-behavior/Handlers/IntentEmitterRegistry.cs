// =============================================================================
// Intent Emitter Registry
// Thread-safe registry for mapping ABML actions to Intent Channel emissions.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;
using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Behavior.Handlers;

/// <summary>
/// Thread-safe registry for intent emitters.
/// </summary>
/// <remarks>
/// <para>
/// Emitters are looked up by action name, then filtered by document type
/// compatibility using the emission context.
/// </para>
/// </remarks>
public sealed class IntentEmitterRegistry : IIntentEmitterRegistry
{
    private readonly ConcurrentDictionary<string, List<IIntentEmitter>> _emittersByAction;
    private readonly ILogger<IntentEmitterRegistry>? _logger;

    /// <summary>
    /// Action names that are forbidden for security reasons.
    /// These will be rejected at registration and lookup time as defense-in-depth.
    /// </summary>
    private static readonly string[] ForbiddenActionNames =
    {
        "service_call",
        "api_call",
        "http_call",
        "mesh_call",
        "invoke_service"
    };

    /// <summary>
    /// Creates a new intent emitter registry.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public IntentEmitterRegistry(ILogger<IntentEmitterRegistry>? logger = null)
    {
        _logger = logger;
        _emittersByAction = new ConcurrentDictionary<string, List<IIntentEmitter>>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if an action name is forbidden for security reasons.
    /// </summary>
    private static bool IsForbiddenAction(string actionName)
    {
        return ForbiddenActionNames.Any(f =>
            string.Equals(f, actionName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    public void Register(IIntentEmitter emitter)
    {
        if (string.IsNullOrEmpty(emitter.ActionName))
        {
            throw new ArgumentException("Emitter action name cannot be null or empty", nameof(emitter));
        }

        if (IsForbiddenAction(emitter.ActionName))
        {
            throw new ArgumentException(
                $"Cannot register emitter for forbidden action '{emitter.ActionName}'. " +
                "Generic service call actions are prohibited for security reasons.",
                nameof(emitter));
        }

        var emitters = _emittersByAction.GetOrAdd(emitter.ActionName, _ => new List<IIntentEmitter>());

        lock (emitters)
        {
            emitters.Add(emitter);
        }

        _logger?.LogDebug(
            "Registered intent emitter for action {ActionName} (document types: {DocumentTypes})",
            emitter.ActionName,
            emitter.SupportedDocumentTypes.Count > 0
                ? string.Join(", ", emitter.SupportedDocumentTypes)
                : "all");
    }

    /// <inheritdoc/>
    public IIntentEmitter? GetEmitter(string actionName, IntentEmissionContext context)
    {
        if (string.IsNullOrEmpty(actionName))
        {
            return null;
        }

        if (IsForbiddenAction(actionName))
        {
            _logger?.LogWarning(
                "Attempted to get emitter for forbidden action {ActionName}",
                actionName);
            return null;
        }

        if (!_emittersByAction.TryGetValue(actionName, out var emitters))
        {
            return null;
        }

        lock (emitters)
        {
            // Find first emitter that can handle this action in context
            foreach (var emitter in emitters)
            {
                if (emitter.CanEmit(actionName, context))
                {
                    return emitter;
                }
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetActionNames()
    {
        return _emittersByAction.Keys.ToList().AsReadOnly();
    }

    /// <inheritdoc/>
    public bool HasEmitter(string actionName)
    {
        if (string.IsNullOrEmpty(actionName))
        {
            return false;
        }

        if (IsForbiddenAction(actionName))
        {
            return false;
        }

        return _emittersByAction.ContainsKey(actionName);
    }

    /// <summary>
    /// Registers built-in core emitters.
    /// </summary>
    public void RegisterCoreEmitters()
    {
        // Movement emitters
        Register(new CoreEmitters.WalkToEmitter());
        Register(new CoreEmitters.RunToEmitter());
        Register(new CoreEmitters.StopEmitter());

        // Combat emitters
        Register(new CoreEmitters.AttackEmitter());
        Register(new CoreEmitters.BlockEmitter());
        Register(new CoreEmitters.DodgeEmitter());

        // Attention emitters
        Register(new CoreEmitters.LookAtEmitter());
        Register(new CoreEmitters.TrackEmitter());

        // Interaction emitters
        Register(new CoreEmitters.UseEmitter());
        Register(new CoreEmitters.PickUpEmitter());
        Register(new CoreEmitters.TalkToEmitter());

        // Expression emitters
        Register(new CoreEmitters.EmoteEmitter());

        // Vocalization emitters
        Register(new CoreEmitters.SpeakEmitter());
        Register(new CoreEmitters.ShoutEmitter());

        // Generic intent emitters
        Register(new CoreEmitters.EmitIntentEmitter());
        Register(new CoreEmitters.MultiEmitEmitter());

        _logger?.LogInformation(
            "Registered {Count} core intent emitters for {ActionCount} actions",
            _emittersByAction.Values.Sum(e => e.Count),
            _emittersByAction.Count);
    }

    /// <summary>
    /// Creates a registry with core emitters registered.
    /// </summary>
    public static IntentEmitterRegistry CreateWithCoreEmitters(ILogger<IntentEmitterRegistry>? logger = null)
    {
        var registry = new IntentEmitterRegistry(logger);
        registry.RegisterCoreEmitters();
        return registry;
    }
}
