// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Arcs;

/// <summary>
/// Loads emotional arc definitions from emotional-arcs.yaml.
/// </summary>
public static class EmotionalArcLoader
{
    private static readonly Lazy<EmotionalArcsData> Data = new(() =>
        YamlLoader.Load<EmotionalArcsData>("emotional-arcs.yaml"));

    private static readonly Lazy<IReadOnlyDictionary<ArcType, EmotionalArc>> ArcsByType = new(BuildArcs);

    /// <summary>
    /// Gets all emotional arc definitions.
    /// </summary>
    public static IReadOnlyCollection<EmotionalArc> All => ArcsByType.Value.Values;

    /// <summary>
    /// Gets an emotional arc by type.
    /// </summary>
    /// <param name="type">The arc type.</param>
    /// <returns>The emotional arc definition.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the arc type is not found.</exception>
    public static EmotionalArc Get(ArcType type)
    {
        return ArcsByType.Value[type];
    }

    /// <summary>
    /// Gets an emotional arc by code string.
    /// </summary>
    /// <param name="code">The arc code (e.g., "MAN_IN_HOLE", "CINDERELLA").</param>
    /// <returns>The emotional arc definition, or null if not found.</returns>
    public static EmotionalArc? GetByCode(string code)
    {
        var normalized = code.ToUpperInvariant().Replace(" ", "_");
        return ArcsByType.Value.Values.FirstOrDefault(a =>
            a.Code.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets arcs that end with a positive direction (protagonist succeeds).
    /// </summary>
    /// <returns>Arcs with positive endings.</returns>
    public static IEnumerable<EmotionalArc> GetPositiveArcs()
    {
        return ArcsByType.Value.Values.Where(a => a.Direction == ArcDirection.Positive);
    }

    /// <summary>
    /// Gets arcs that end with a negative direction (tragedy).
    /// </summary>
    /// <returns>Arcs with negative endings.</returns>
    public static IEnumerable<EmotionalArc> GetNegativeArcs()
    {
        return ArcsByType.Value.Values.Where(a => a.Direction == ArcDirection.Negative);
    }

    /// <summary>
    /// Gets arcs commonly used with a specific genre.
    /// </summary>
    /// <param name="genre">The genre to filter by.</param>
    /// <returns>Arcs associated with the genre.</returns>
    public static IEnumerable<EmotionalArc> GetForGenre(string genre)
    {
        return ArcsByType.Value.Values.Where(a =>
            a.GenreAssociations.Any(g => g.Equals(genre, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyDictionary<ArcType, EmotionalArc> BuildArcs()
    {
        var data = Data.Value;
        var arcs = new Dictionary<ArcType, EmotionalArc>();

        if (data.Arcs == null)
        {
            return arcs;
        }

        foreach (var kvp in data.Arcs)
        {
            var yaml = kvp.Value;
            var type = ParseArcType(yaml.Code);

            var controlPoints = new List<ArcControlPoint>();
            if (yaml.ShapeDefinition?.ControlPoints != null)
            {
                foreach (var cp in yaml.ShapeDefinition.ControlPoints)
                {
                    controlPoints.Add(new ArcControlPoint
                    {
                        Position = cp.Position,
                        Value = cp.Value,
                        Label = cp.Label ?? "",
                        Description = cp.Description
                    });
                }
            }

            var sampledTrajectory = yaml.ShapeDefinition?.SampledTrajectory ?? Array.Empty<double>();

            // Determine direction from trajectory (compare start to end)
            var direction = sampledTrajectory.Length > 1 && sampledTrajectory[^1] > sampledTrajectory[0]
                ? ArcDirection.Positive
                : ArcDirection.Negative;

            arcs[type] = new EmotionalArc
            {
                Type = type,
                Code = yaml.Code,
                Name = yaml.Name ?? kvp.Key,
                Aliases = yaml.Aliases ?? Array.Empty<string>(),
                Pattern = yaml.Pattern ?? "",
                Description = yaml.Description ?? "",
                Direction = direction,
                MathematicalForm = yaml.ShapeDefinition?.MathematicalForm ?? "",
                ControlPoints = controlPoints.ToArray(),
                SampledTrajectory = sampledTrajectory,
                InflectionPoints = yaml.ShapeDefinition?.InflectionPoints ?? 0,
                GenreAssociations = yaml.GenreAssociations ?? Array.Empty<string>(),
                Examples = yaml.Examples ?? Array.Empty<string>()
            };
        }

        return arcs;
    }

    private static ArcType ParseArcType(string code)
    {
        var normalized = code.ToUpperInvariant().Replace("_", "").Replace(" ", "");
        return normalized switch
        {
            "RAGSTORICHIES" or "RAGSTORICHES" => ArcType.RagsToRiches,
            "TRAGEDY" => ArcType.Tragedy,
            "MANINHOLE" or "MANINAHOLE" => ArcType.ManInHole,
            "ICARUS" => ArcType.Icarus,
            "CINDERELLA" => ArcType.Cinderella,
            "OEDIPUS" => ArcType.Oedipus,
            _ => throw new ArgumentException($"Unknown arc code: {code}")
        };
    }

    #region YAML Data Classes

    internal sealed class EmotionalArcsData
    {
        public string? Version { get; set; }
        public string? Source { get; set; }
        public Dictionary<string, ArcYaml>? Arcs { get; set; }
    }

    internal sealed class ArcYaml
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string? Name { get; set; }
        public string[]? Aliases { get; set; }
        public string? Pattern { get; set; }
        public string? Description { get; set; }
        public EmotionalTrajectoryYaml? EmotionalTrajectory { get; set; }
        public ShapeDefinitionYaml? ShapeDefinition { get; set; }
        public string[]? NarrativeCharacteristics { get; set; }
        public string[]? GenreAssociations { get; set; }
        public string[]? Examples { get; set; }
        public int? SvdMode { get; set; }
        public string? Sign { get; set; }
        public int[]? SvdModeCombination { get; set; }
        public string? Note { get; set; }
    }

    internal sealed class EmotionalTrajectoryYaml
    {
        public string? Start { get; set; }
        public string? Middle { get; set; }
        public string? End { get; set; }
        public string? Middle1 { get; set; }
        public string? Middle2 { get; set; }
    }

    internal sealed class ShapeDefinitionYaml
    {
        public string? Type { get; set; }
        public string? MathematicalForm { get; set; }
        public int InflectionPoints { get; set; }
        public double[]? NadirPosition { get; set; }
        public double[]? ApexPosition { get; set; }
        public List<ControlPointYaml>? ControlPoints { get; set; }
        public double[]? SampledTrajectory { get; set; }
        public double[]? FirstPeakPosition { get; set; }
        public double[]? ValleyPosition { get; set; }
        public double? FinalRiseStart { get; set; }
        public double[]? FirstValleyPosition { get; set; }
        public double[]? PeakPosition { get; set; }
        public double? FinalFallStart { get; set; }
    }

    internal sealed class ControlPointYaml
    {
        public double Position { get; set; }
        public double Value { get; set; }
        public string? Label { get; set; }
        public string? Description { get; set; }
    }

    #endregion
}
