// =============================================================================
// Combat Preferences Variable Provider
// Provides combat preferences data for ABML expressions via ${combat.*} paths.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.CharacterPersonality;

namespace BeyondImmersion.BannouService.Actor.Runtime;

/// <summary>
/// Provides combat preferences data for ABML expressions.
/// Supports paths like ${combat.style}, ${combat.riskTolerance}, etc.
/// </summary>
public sealed class CombatPreferencesProvider : IVariableProvider
{
    private readonly Dictionary<string, object?> _preferences;

    /// <inheritdoc/>
    public string Name => "combat";

    /// <summary>
    /// Creates a new combat preferences provider.
    /// </summary>
    /// <param name="combatPrefs">The combat preferences response, or null for defaults.</param>
    public CombatPreferencesProvider(CombatPreferencesResponse? combatPrefs)
    {
        var prefs = combatPrefs?.Preferences;
        _preferences = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            // Provide defaults if no preferences exist
            ["style"] = prefs?.Style.ToString().ToLowerInvariant() ?? "balanced",
            ["preferredRange"] = prefs?.PreferredRange.ToString().ToLowerInvariant() ?? "medium",
            ["groupRole"] = prefs?.GroupRole.ToString().ToLowerInvariant() ?? "balanced",
            ["riskTolerance"] = prefs?.RiskTolerance ?? 0.5f,
            ["retreatThreshold"] = prefs?.RetreatThreshold ?? 0.2f,
            ["protectAllies"] = prefs?.ProtectAllies ?? false
        };
    }

    /// <inheritdoc/>
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();

        var firstSegment = path[0];

        // Handle property access: ${combat.style}, ${combat.riskTolerance}, etc.
        if (_preferences.TryGetValue(firstSegment, out var value))
        {
            return value;
        }

        return null;
    }

    /// <inheritdoc/>
    public object? GetRootValue()
    {
        return _preferences;
    }

    /// <inheritdoc/>
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;
        return _preferences.ContainsKey(path[0]);
    }
}
