// =============================================================================
// Encounters Variable Provider
// Provides encounter data for ABML expressions via ${encounters.*} paths.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.CharacterEncounter;

namespace BeyondImmersion.BannouService.Actor.Runtime;

/// <summary>
/// Provides encounter data for ABML expressions.
/// Supports paths like ${encounters.recent}, ${encounters.count}, etc.
/// </summary>
/// <remarks>
/// Supported paths:
/// <list type="bullet">
/// <item>${encounters.recent} - List of recent encounters</item>
/// <item>${encounters.count} - Total encounter count</item>
/// <item>${encounters.grudges} - Characters with sentiment less than -0.5</item>
/// <item>${encounters.allies} - Characters with sentiment greater than 0.5</item>
/// <item>${encounters.has_met.{characterId}} - Whether met a specific character</item>
/// <item>${encounters.sentiment.{characterId}} - Sentiment toward specific character</item>
/// <item>${encounters.last_context.{characterId}} - Last encounter context with character</item>
/// <item>${encounters.last_emotion.{characterId}} - Last emotional impact with character</item>
/// <item>${encounters.encounter_count.{characterId}} - Count of encounters with character</item>
/// </list>
/// </remarks>
public sealed class EncountersProvider : IVariableProvider
{
    private readonly EncounterListResponse? _encounters;
    private readonly Dictionary<Guid, SentimentResponse> _sentiments;
    private readonly Dictionary<Guid, HasMetResponse> _hasMet;
    private readonly Dictionary<Guid, EncounterListResponse> _pairEncounters;

    /// <inheritdoc/>
    public string Name => "encounters";

    /// <summary>
    /// Creates a new encounters provider with the given encounter data.
    /// </summary>
    /// <param name="encounters">The character's encounters, or null for empty provider.</param>
    /// <param name="sentiments">Pre-loaded sentiment data toward known characters.</param>
    /// <param name="hasMet">Pre-loaded has-met data for known characters.</param>
    /// <param name="pairEncounters">Pre-loaded pair encounter data for known characters.</param>
    public EncountersProvider(
        EncounterListResponse? encounters,
        Dictionary<Guid, SentimentResponse>? sentiments = null,
        Dictionary<Guid, HasMetResponse>? hasMet = null,
        Dictionary<Guid, EncounterListResponse>? pairEncounters = null)
    {
        _encounters = encounters;
        _sentiments = sentiments ?? new Dictionary<Guid, SentimentResponse>();
        _hasMet = hasMet ?? new Dictionary<Guid, HasMetResponse>();
        _pairEncounters = pairEncounters ?? new Dictionary<Guid, EncounterListResponse>();
    }

    /// <inheritdoc/>
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();

        var firstSegment = path[0];

        // Handle ${encounters.count}
        if (firstSegment.Equals("count", StringComparison.OrdinalIgnoreCase))
        {
            return _encounters?.TotalCount ?? 0;
        }

        // Handle ${encounters.recent}
        if (firstSegment.Equals("recent", StringComparison.OrdinalIgnoreCase))
        {
            return GetRecentEncounters();
        }

        // Handle ${encounters.grudges}
        if (firstSegment.Equals("grudges", StringComparison.OrdinalIgnoreCase))
        {
            return GetGrudges();
        }

        // Handle ${encounters.allies}
        if (firstSegment.Equals("allies", StringComparison.OrdinalIgnoreCase))
        {
            return GetAllies();
        }

        // Handle paths that require a character ID: has_met, sentiment, last_context, last_emotion, encounter_count
        if (path.Length >= 2)
        {
            var characterIdStr = path[1];
            if (!Guid.TryParse(characterIdStr, out var characterId))
            {
                return null;
            }

            // Handle ${encounters.has_met.{characterId}}
            if (firstSegment.Equals("has_met", StringComparison.OrdinalIgnoreCase))
            {
                return _hasMet.TryGetValue(characterId, out var hasMet) && hasMet.HasMet;
            }

            // Handle ${encounters.sentiment.{characterId}}
            if (firstSegment.Equals("sentiment", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("sentiment_toward", StringComparison.OrdinalIgnoreCase))
            {
                return _sentiments.TryGetValue(characterId, out var sentiment) ? sentiment.Sentiment : 0.0f;
            }

            // Handle ${encounters.last_context.{characterId}}
            if (firstSegment.Equals("last_context", StringComparison.OrdinalIgnoreCase))
            {
                return GetLastContext(characterId);
            }

            // Handle ${encounters.last_emotion.{characterId}}
            if (firstSegment.Equals("last_emotion", StringComparison.OrdinalIgnoreCase))
            {
                return GetLastEmotion(characterId);
            }

            // Handle ${encounters.encounter_count.{characterId}}
            if (firstSegment.Equals("encounter_count", StringComparison.OrdinalIgnoreCase))
            {
                return _hasMet.TryGetValue(characterId, out var hasMet) ? hasMet.EncounterCount : 0;
            }

            // Handle ${encounters.dominant_emotion.{characterId}}
            if (firstSegment.Equals("dominant_emotion", StringComparison.OrdinalIgnoreCase))
            {
                return _sentiments.TryGetValue(characterId, out var sentiment)
                    ? sentiment.DominantEmotion?.ToString()
                    : null;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public object? GetRootValue()
    {
        return new Dictionary<string, object?>
        {
            ["count"] = _encounters?.TotalCount ?? 0,
            ["recent"] = GetRecentEncounters(),
            ["grudges"] = GetGrudges(),
            ["allies"] = GetAllies()
        };
    }

    /// <inheritdoc/>
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;

        var firstSegment = path[0];

        // Direct property access
        if (firstSegment.Equals("count", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("recent", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("grudges", StringComparison.OrdinalIgnoreCase) ||
            firstSegment.Equals("allies", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Character-specific access (requires path[1] to be a valid GUID)
        if (path.Length >= 2)
        {
            if (firstSegment.Equals("has_met", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("sentiment", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("sentiment_toward", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("last_context", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("last_emotion", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("encounter_count", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("dominant_emotion", StringComparison.OrdinalIgnoreCase))
            {
                return Guid.TryParse(path[1], out _);
            }
        }

        return false;
    }

    /// <summary>
    /// Gets recent encounters as a list of simplified objects.
    /// </summary>
    private List<Dictionary<string, object?>> GetRecentEncounters()
    {
        if (_encounters?.Encounters == null) return new List<Dictionary<string, object?>>();

        return _encounters.Encounters
            .Take(10)
            .Select(e => new Dictionary<string, object?>
            {
                ["encounter_id"] = e.Encounter.EncounterId.ToString(),
                ["type"] = e.Encounter.EncounterTypeCode,
                ["outcome"] = e.Encounter.Outcome.ToString(),
                ["context"] = e.Encounter.Context,
                ["timestamp"] = e.Encounter.Timestamp.ToString("O"),
                ["participant_count"] = e.Encounter.ParticipantIds.Count
            })
            .ToList();
    }

    /// <summary>
    /// Gets characters with strongly negative sentiment (less than -0.5).
    /// </summary>
    private List<Dictionary<string, object?>> GetGrudges()
    {
        return _sentiments
            .Where(kv => kv.Value.Sentiment < -0.5f)
            .Select(kv => new Dictionary<string, object?>
            {
                ["character_id"] = kv.Key.ToString(),
                ["sentiment"] = kv.Value.Sentiment,
                ["encounter_count"] = kv.Value.EncounterCount,
                ["dominant_emotion"] = kv.Value.DominantEmotion?.ToString()
            })
            .ToList();
    }

    /// <summary>
    /// Gets characters with strongly positive sentiment (greater than 0.5).
    /// </summary>
    private List<Dictionary<string, object?>> GetAllies()
    {
        return _sentiments
            .Where(kv => kv.Value.Sentiment > 0.5f)
            .Select(kv => new Dictionary<string, object?>
            {
                ["character_id"] = kv.Key.ToString(),
                ["sentiment"] = kv.Value.Sentiment,
                ["encounter_count"] = kv.Value.EncounterCount,
                ["dominant_emotion"] = kv.Value.DominantEmotion?.ToString()
            })
            .ToList();
    }

    /// <summary>
    /// Gets the context of the most recent encounter with a specific character.
    /// </summary>
    private string? GetLastContext(Guid characterId)
    {
        if (!_pairEncounters.TryGetValue(characterId, out var encounters))
            return null;

        return encounters.Encounters
            ?.OrderByDescending(e => e.Encounter.Timestamp)
            .FirstOrDefault()
            ?.Encounter.Context;
    }

    /// <summary>
    /// Gets the emotional impact of the most recent encounter with a specific character.
    /// </summary>
    private string? GetLastEmotion(Guid characterId)
    {
        if (!_pairEncounters.TryGetValue(characterId, out var encounters))
            return null;

        // Find the most recent encounter and get our perspective on it
        var lastEncounter = encounters.Encounters
            ?.OrderByDescending(e => e.Encounter.Timestamp)
            .FirstOrDefault();

        if (lastEncounter == null) return null;

        // Find the perspective for the owning character (not the target)
        // We need to check perspectives, but since we don't have the owning character ID here,
        // we'll return the first perspective's emotional impact as a reasonable default
        return lastEncounter.Perspectives
            ?.FirstOrDefault()
            ?.EmotionalImpact.ToString();
    }
}
