namespace BeyondImmersion.BannouService.Mapping.Helpers;

/// <summary>
/// Scores map objects for affordance queries.
/// Extracted from MappingService for improved testability.
/// </summary>
public interface IAffordanceScorer
{
    /// <summary>
    /// Gets the map kinds to search for a given affordance type.
    /// </summary>
    /// <param name="type">The affordance type to query.</param>
    /// <returns>List of map kinds relevant to the affordance type.</returns>
    IReadOnlyList<MapKind> GetKindsForAffordanceType(AffordanceType type);

    /// <summary>
    /// Scores a candidate map object for a given affordance type.
    /// </summary>
    /// <param name="candidate">The map object to score.</param>
    /// <param name="type">The affordance type being queried.</param>
    /// <param name="custom">Optional custom affordance definition.</param>
    /// <param name="actor">Optional actor capabilities that affect scoring.</param>
    /// <returns>Score between 0.0 and 1.0 indicating how well the object matches.</returns>
    double ScoreAffordance(MapObject candidate, AffordanceType type, CustomAffordance? custom, ActorCapabilities? actor);

    /// <summary>
    /// Extracts relevant features from a map object based on affordance type.
    /// </summary>
    /// <param name="candidate">The map object to extract features from.</param>
    /// <param name="type">The affordance type determining which features to extract.</param>
    /// <returns>Dictionary of extracted features, or null if no relevant features found.</returns>
    object? ExtractFeatures(MapObject candidate, AffordanceType type);
}
