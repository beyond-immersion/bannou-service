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
    public static EmotionalArc Get(ArcType type)
    {
        return ArcsByType.Value[type];
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
                        Label = cp.Label ?? ""
                    });
                }
            }

            var sampledTrajectory = yaml.ShapeDefinition?.SampledTrajectory ?? Array.Empty<double>();

            var direction = sampledTrajectory.Length > 1 && sampledTrajectory[^1] > sampledTrajectory[0]
                ? ArcDirection.Positive
                : ArcDirection.Negative;

            arcs[type] = new EmotionalArc
            {
                Type = type,
                ShapePattern = yaml.ShapeDefinition?.Type ?? "",
                Direction = direction,
                MathematicalForm = yaml.ShapeDefinition?.MathematicalForm ?? "",
                ControlPoints = controlPoints.ToArray(),
                SampledTrajectory = sampledTrajectory
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
        public ShapeDefinitionYaml? ShapeDefinition { get; set; }
    }

    internal sealed class ShapeDefinitionYaml
    {
        public string? Type { get; set; }
        public string? MathematicalForm { get; set; }
        public List<ControlPointYaml>? ControlPoints { get; set; }
        public double[]? SampledTrajectory { get; set; }
    }

    internal sealed class ControlPointYaml
    {
        public double Position { get; set; }
        public double Value { get; set; }
        public string? Label { get; set; }
    }

    #endregion
}
