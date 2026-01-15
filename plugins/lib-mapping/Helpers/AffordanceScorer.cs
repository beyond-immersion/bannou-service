using System.Text.Json;

namespace BeyondImmersion.BannouService.Mapping.Helpers;

/// <summary>
/// Scores map objects for affordance queries.
/// Extracted from MappingService for improved testability.
/// </summary>
public class AffordanceScorer : IAffordanceScorer
{
    /// <inheritdoc/>
    public IReadOnlyList<MapKind> GetKindsForAffordanceType(AffordanceType type)
    {
        return type switch
        {
            AffordanceType.Ambush => new List<MapKind> { MapKind.Static_geometry, MapKind.Dynamic_objects, MapKind.Navigation },
            AffordanceType.Shelter => new List<MapKind> { MapKind.Static_geometry, MapKind.Dynamic_objects },
            AffordanceType.Vista => new List<MapKind> { MapKind.Terrain, MapKind.Static_geometry, MapKind.Points_of_interest },
            AffordanceType.Choke_point => new List<MapKind> { MapKind.Navigation, MapKind.Static_geometry },
            AffordanceType.Gathering_spot => new List<MapKind> { MapKind.Points_of_interest, MapKind.Static_geometry },
            AffordanceType.Dramatic_reveal => new List<MapKind> { MapKind.Points_of_interest, MapKind.Terrain },
            AffordanceType.Hidden_path => new List<MapKind> { MapKind.Navigation, MapKind.Static_geometry },
            AffordanceType.Defensible_position => new List<MapKind> { MapKind.Static_geometry, MapKind.Terrain, MapKind.Navigation },
            AffordanceType.Custom => Enum.GetValues<MapKind>().ToList(),
            _ => Enum.GetValues<MapKind>().ToList()
        };
    }

    /// <inheritdoc/>
    public double ScoreAffordance(MapObject candidate, AffordanceType type, CustomAffordance? custom, ActorCapabilities? actor)
    {
        // Base score from object data
        var score = 0.5;

        if (candidate.Data is JsonElement data && data.ValueKind == JsonValueKind.Object)
        {
            // Check for common affordance-related properties
            if (TryGetJsonDouble(data, "cover_rating", out var cr))
            {
                if (type == AffordanceType.Ambush || type == AffordanceType.Shelter || type == AffordanceType.Defensible_position)
                {
                    score += cr * 0.3;
                }
            }

            if (TryGetJsonDouble(data, "elevation", out var el))
            {
                if (type == AffordanceType.Vista || type == AffordanceType.Dramatic_reveal)
                {
                    score += Math.Min(el / 100.0, 0.3);
                }
            }

            if (TryGetJsonInt(data, "sightlines", out var sl))
            {
                if (type == AffordanceType.Ambush || type == AffordanceType.Vista)
                {
                    score += Math.Min(sl * 0.05, 0.2);
                }
            }
        }

        // Apply actor capability modifiers
        if (actor != null)
        {
            // Size affects cover requirements
            if (type == AffordanceType.Shelter || type == AffordanceType.Ambush)
            {
                score *= actor.Size switch
                {
                    ActorSize.Tiny => 1.2,
                    ActorSize.Small => 1.1,
                    ActorSize.Medium => 1.0,
                    ActorSize.Large => 0.9,
                    ActorSize.Huge => 0.8,
                    _ => 1.0
                };
            }

            // Stealth rating affects ambush scoring
            if (type == AffordanceType.Ambush && actor.StealthRating.HasValue)
            {
                score *= 1.0 + actor.StealthRating.Value * 0.2;
            }
        }

        // Handle custom affordance
        if (type == AffordanceType.Custom && custom != null)
        {
            score = ScoreCustomAffordance(candidate, custom);
        }

        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <inheritdoc/>
    public object? ExtractFeatures(MapObject candidate, AffordanceType type)
    {
        var features = new Dictionary<string, object>();

        if (candidate.Data is JsonElement data && data.ValueKind == JsonValueKind.Object)
        {
            // Extract relevant features based on affordance type
            var relevantKeys = type switch
            {
                AffordanceType.Ambush => new[] { "cover_rating", "sightlines", "concealment" },
                AffordanceType.Shelter => new[] { "cover_rating", "protection", "capacity" },
                AffordanceType.Vista => new[] { "elevation", "visibility_range", "sightlines" },
                AffordanceType.Choke_point => new[] { "width", "defensibility", "exit_count" },
                AffordanceType.Gathering_spot => new[] { "capacity", "comfort", "accessibility" },
                AffordanceType.Dramatic_reveal => new[] { "elevation", "view_target", "approach_direction" },
                AffordanceType.Hidden_path => new[] { "concealment", "width", "traversability" },
                AffordanceType.Defensible_position => new[] { "cover_rating", "sightlines", "exit_count", "elevation" },
                _ => Array.Empty<string>()
            };

            foreach (var key in relevantKeys)
            {
                if (data.TryGetProperty(key, out var value))
                {
                    // Extract the actual value from JsonElement
                    object? extractedValue = value.ValueKind switch
                    {
                        JsonValueKind.Number when value.TryGetDouble(out var d) => d,
                        JsonValueKind.String => value.GetString(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => value.ToString()
                    };
                    if (extractedValue != null)
                    {
                        features[key] = extractedValue;
                    }
                }
            }
        }

        features["objectType"] = candidate.ObjectType;
        return features.Count > 1 ? features : null;
    }

    /// <summary>
    /// Scores a map object against a custom affordance definition.
    /// </summary>
    private static double ScoreCustomAffordance(MapObject candidate, CustomAffordance custom)
    {
        var score = 0.5;

        var hasData = candidate.Data is JsonElement data && data.ValueKind == JsonValueKind.Object;

        // Check required criteria
        if (custom.Requires is JsonElement requires && requires.ValueKind == JsonValueKind.Object)
        {
            foreach (var req in requires.EnumerateObject())
            {
                if (req.Name == "objectTypes" && req.Value.ValueKind == JsonValueKind.Array)
                {
                    var matchesType = false;
                    foreach (var typeVal in req.Value.EnumerateArray())
                    {
                        if (typeVal.ValueKind == JsonValueKind.String &&
                            typeVal.GetString() == candidate.ObjectType)
                        {
                            matchesType = true;
                            break;
                        }
                    }
                    if (!matchesType) return 0.0;
                }
                else if (hasData)
                {
                    var dataElement = (JsonElement)candidate.Data!;
                    if (dataElement.TryGetProperty(req.Name, out var candidateValue))
                    {
                        if (req.Value.ValueKind == JsonValueKind.Object &&
                            req.Value.TryGetProperty("min", out var minProp) &&
                            minProp.TryGetDouble(out var minVal))
                        {
                            if (candidateValue.TryGetDouble(out var candVal) && candVal < minVal)
                            {
                                return 0.0;
                            }
                        }
                    }
                }
            }
        }

        // Apply preferences (boost but don't require)
        if (hasData && custom.Prefers is JsonElement prefers && prefers.ValueKind == JsonValueKind.Object)
        {
            var dataElement = (JsonElement)candidate.Data!;
            foreach (var pref in prefers.EnumerateObject())
            {
                if (dataElement.TryGetProperty(pref.Name, out _))
                {
                    score += 0.1;
                }
            }
        }

        // Apply exclusions
        if (hasData && custom.Excludes is JsonElement excludes && excludes.ValueKind == JsonValueKind.Object)
        {
            var dataElement = (JsonElement)candidate.Data!;
            foreach (var excl in excludes.EnumerateObject())
            {
                if (dataElement.TryGetProperty(excl.Name, out _))
                {
                    return 0.0;
                }
            }
        }

        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>
    /// Tries to get a double value from a JSON element property.
    /// </summary>
    private static bool TryGetJsonDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out value))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Tries to get an integer value from a JSON element property.
    /// </summary>
    private static bool TryGetJsonInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value))
            {
                return true;
            }
        }
        return false;
    }
}
