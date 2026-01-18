using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Intent;

/// <summary>
/// Thematic intent expressing desired motif usage and development.
/// </summary>
public sealed class ThematicIntent
{
    /// <summary>
    /// Gets whether to introduce the main motif.
    /// </summary>
    public bool IntroduceMainMotif { get; init; }

    /// <summary>
    /// Gets whether to return/recall the main motif.
    /// </summary>
    public bool ReturnMainMotif { get; init; }

    /// <summary>
    /// Gets the preferred transformation type for motif development.
    /// </summary>
    public MotifTransformationType? TransformationType { get; init; }

    /// <summary>
    /// Gets whether to allow a secondary motif.
    /// </summary>
    public bool AllowSecondaryMotif { get; init; }

    /// <summary>
    /// Gets the required development stage.
    /// </summary>
    public DevelopmentStage? RequiredStage { get; init; }

    /// <summary>
    /// Gets the degree of transformation (0 = exact, 1 = very different).
    /// </summary>
    public double TransformationDegree { get; init; } = 0.5;

    /// <summary>
    /// Gets whether fragmentation is allowed.
    /// </summary>
    public bool AllowFragmentation { get; init; }

    /// <summary>
    /// Gets specific interval relationships to preserve during transformation.
    /// </summary>
    public bool PreserveIntervalContour { get; init; } = true;

    /// <summary>
    /// Gets whether rhythmic profile should be preserved.
    /// </summary>
    public bool PreserveRhythm { get; init; } = true;

    /// <summary>
    /// Creates a default thematic intent (no specific requirements).
    /// </summary>
    public static ThematicIntent Default => new();

    /// <summary>
    /// Creates intent for introducing the main motif.
    /// </summary>
    public static ThematicIntent Introduction => new()
    {
        IntroduceMainMotif = true,
        RequiredStage = DevelopmentStage.Introduction,
        TransformationDegree = 0.0,
        PreserveIntervalContour = true,
        PreserveRhythm = true
    };

    /// <summary>
    /// Creates intent for developing the motif.
    /// </summary>
    public static ThematicIntent Development => new()
    {
        TransformationType = MotifTransformationType.Sequence,
        RequiredStage = DevelopmentStage.Development,
        TransformationDegree = 0.5,
        AllowFragmentation = true
    };

    /// <summary>
    /// Creates intent for recapitulation.
    /// </summary>
    public static ThematicIntent Recapitulation => new()
    {
        ReturnMainMotif = true,
        RequiredStage = DevelopmentStage.Recapitulation,
        TransformationDegree = 0.2,
        PreserveIntervalContour = true,
        PreserveRhythm = true
    };

    /// <summary>
    /// Creates intent for transformation.
    /// </summary>
    /// <param name="type">The transformation type.</param>
    /// <param name="degree">Degree of transformation.</param>
    /// <returns>A transformation intent.</returns>
    public static ThematicIntent Transform(MotifTransformationType type, double degree = 0.5) => new()
    {
        TransformationType = type,
        RequiredStage = DevelopmentStage.Transformation,
        TransformationDegree = degree,
        AllowFragmentation = type == MotifTransformationType.Fragmentation
    };

    /// <summary>
    /// Creates intent for climactic return of the motif.
    /// </summary>
    public static ThematicIntent ClimaticReturn => new()
    {
        ReturnMainMotif = true,
        TransformationType = MotifTransformationType.Augmentation,
        RequiredStage = DevelopmentStage.Climax,
        TransformationDegree = 0.3,
        PreserveIntervalContour = true
    };
}
