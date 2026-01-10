// =============================================================================
// Personality Variable Provider
// Provides personality data for ABML expressions via ${personality.*} paths.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.CharacterPersonality;

namespace BeyondImmersion.BannouService.Actor.Runtime;

/// <summary>
/// Provides personality data for ABML expressions.
/// Supports paths like ${personality.openness}, ${personality.traits.AGGRESSION}, etc.
/// </summary>
public sealed class PersonalityProvider : IVariableProvider
{
    private readonly Dictionary<string, float> _traits;
    private readonly int _version;

    /// <inheritdoc/>
    public string Name => "personality";

    /// <summary>
    /// Creates a new personality provider with the given traits.
    /// </summary>
    /// <param name="personality">The personality response, or null for empty provider.</param>
    public PersonalityProvider(PersonalityResponse? personality)
    {
        _traits = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        _version = personality?.Version ?? 0;

        if (personality?.Traits != null)
        {
            foreach (var trait in personality.Traits)
            {
                // Store both as lowercase and original enum name for flexible access
                _traits[trait.Axis.ToString()] = trait.Value;
                _traits[trait.Axis.ToString().ToLowerInvariant()] = trait.Value;
            }
        }
    }

    /// <inheritdoc/>
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();

        var firstSegment = path[0];

        // Handle ${personality.version}
        if (firstSegment.Equals("version", StringComparison.OrdinalIgnoreCase))
        {
            return _version;
        }

        // Handle ${personality.traits.*} or ${personality.traits}
        if (firstSegment.Equals("traits", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Length == 1) return _traits;
            var traitName = path[1];
            return _traits.TryGetValue(traitName, out var value) ? value : 0.0f;
        }

        // Handle direct trait access: ${personality.OPENNESS} or ${personality.openness}
        if (_traits.TryGetValue(firstSegment, out var traitValue))
        {
            return traitValue;
        }

        // Not found
        return null;
    }

    /// <inheritdoc/>
    public object? GetRootValue()
    {
        return new Dictionary<string, object?>
        {
            ["traits"] = _traits,
            ["version"] = _version
        };
    }

    /// <inheritdoc/>
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;

        var firstSegment = path[0];
        return firstSegment.Equals("version", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("traits", StringComparison.OrdinalIgnoreCase) ||
                _traits.ContainsKey(firstSegment);
    }
}
