// =============================================================================
// Base Intent Emitter
// Common base class for intent emitters.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Extensions;

namespace BeyondImmersion.BannouService.Behavior.Handlers.CoreEmitters;

/// <summary>
/// Base class for intent emitters with common functionality.
/// </summary>
public abstract class BaseIntentEmitter : IIntentEmitter
{
    private readonly HashSet<string> _supportedDocTypes;

    /// <summary>
    /// Creates a new emitter for all document types.
    /// </summary>
    protected BaseIntentEmitter()
    {
        _supportedDocTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a new emitter for specific document types.
    /// </summary>
    /// <param name="supportedDocTypes">The document types this emitter supports.</param>
    protected BaseIntentEmitter(params string[] supportedDocTypes)
    {
        _supportedDocTypes = new HashSet<string>(supportedDocTypes, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public abstract string ActionName { get; }

    /// <inheritdoc/>
    public IReadOnlySet<string> SupportedDocumentTypes => _supportedDocTypes;

    /// <inheritdoc/>
    public virtual bool CanEmit(string actionName, IntentEmissionContext context)
    {
        // Check action name match
        if (!string.Equals(actionName, ActionName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // If no document type restrictions, accept all
        if (_supportedDocTypes.Count == 0)
        {
            return true;
        }

        // Check document type compatibility
        return _supportedDocTypes.Contains(context.DocumentType);
    }

    /// <inheritdoc/>
    public abstract ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(
        IReadOnlyDictionary<string, object> parameters,
        IntentEmissionContext context,
        CancellationToken ct);

    /// <summary>
    /// Gets a required string parameter.
    /// </summary>
    protected static string GetRequiredString(IReadOnlyDictionary<string, object> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value))
        {
            throw new InvalidOperationException($"Required parameter '{name}' not found");
        }

        return value?.ToString() ?? throw new InvalidOperationException($"Parameter '{name}' is null");
    }

    /// <summary>
    /// Gets an optional string parameter.
    /// </summary>
    protected static string? GetOptionalString(IReadOnlyDictionary<string, object> parameters, string name)
    {
        return parameters.TryGetValue(name, out var value) ? value?.ToString() : null;
    }

    /// <summary>
    /// Gets an optional float parameter with default.
    /// </summary>
    protected static float GetOptionalFloat(IReadOnlyDictionary<string, object> parameters, string name, float defaultValue)
    {
        if (!parameters.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        return value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            string s when float.TryParse(s, out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    /// <summary>
    /// Gets an optional Guid parameter.
    /// </summary>
    protected static Guid? GetOptionalGuid(IReadOnlyDictionary<string, object> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value))
        {
            return null;
        }

        return value switch
        {
            Guid g => g,
            string s when Guid.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>
    /// Gets an optional Vector3 parameter.
    /// </summary>
    protected static System.Numerics.Vector3? GetOptionalVector3(IReadOnlyDictionary<string, object> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value))
        {
            return null;
        }

        return value switch
        {
            System.Numerics.Vector3 v => v,
            IList<object> list when list.Count >= 3 => new System.Numerics.Vector3(
                Convert.ToSingle(list[0]),
                Convert.ToSingle(list[1]),
                Convert.ToSingle(list[2])),
            IDictionary<string, object> dict => new System.Numerics.Vector3(
                GetFloatFromDict(dict, "x"),
                GetFloatFromDict(dict, "y"),
                GetFloatFromDict(dict, "z")),
            _ => null,
        };
    }

    private static float GetFloatFromDict(IDictionary<string, object> dict, string key)
    {
        return dict.TryGetValue(key, out var value) ? Convert.ToSingle(value) : 0f;
    }

    /// <summary>
    /// Creates a single emission result.
    /// </summary>
    protected static IReadOnlyList<IntentEmission> SingleEmission(
        string channel,
        string intent,
        float urgency,
        Guid? target = null,
        System.Numerics.Vector3? position = null)
    {
        if (position.HasValue)
        {
            return new[] { IntentEmissionExtensions.CreateWithPosition(channel, intent, urgency, position.Value, target) };
        }
        return new[] { new IntentEmission(channel, intent, urgency, target) };
    }

    /// <summary>
    /// Creates multiple emission results.
    /// </summary>
    protected static IReadOnlyList<IntentEmission> MultipleEmissions(params IntentEmission[] emissions)
    {
        return emissions;
    }

    /// <summary>
    /// Creates an empty emission result.
    /// </summary>
    protected static IReadOnlyList<IntentEmission> NoEmission()
    {
        return Array.Empty<IntentEmission>();
    }
}
