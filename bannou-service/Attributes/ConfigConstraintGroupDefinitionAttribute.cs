namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Declares a constraint group on a configuration class. At startup, all properties
/// with a matching <see cref="ConfigConstraintGroupAttribute"/> are collected and
/// validated against this group's constraint type and parameters.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is generated from <c>x-constraint-groups</c> in configuration schemas
/// and validated at startup by <see cref="Configuration.IServiceConfiguration.ValidateConstraintGroups"/>.
/// </para>
/// <para>
/// A configuration class may have multiple group definitions (one per group).
/// Each group must have at least two member properties.
/// </para>
/// <para>
/// Sum constraints (<see cref="ConstraintGroupType.SumEquals"/>, <see cref="ConstraintGroupType.SumMinimum"/>,
/// <see cref="ConstraintGroupType.SumMaximum"/>) require <see cref="Value"/> to be set.
/// Presence constraints (<see cref="ConstraintGroupType.ExactlyOne"/>, <see cref="ConstraintGroupType.AtMostOne"/>,
/// <see cref="ConstraintGroupType.AllOrNone"/>) must not set <see cref="Value"/>.
/// </para>
/// </remarks>
/// <example>
/// Schema:
/// <code>
/// x-constraint-groups:
///   stage-weights:
///     constraint: sum-equals
///     value: 1.0
///     tolerance: 0.0001
///     description: Stage weights must collectively equal 1.0
/// </code>
/// Generated:
/// <code>
/// [ConfigConstraintGroupDefinition("stage-weights", ConstraintGroupType.SumEquals, Value = 1.0, Tolerance = 0.0001)]
/// public partial class CraftServiceConfiguration : BaseServiceConfiguration
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class ConfigConstraintGroupDefinitionAttribute : Attribute
{
    /// <summary>
    /// Default tolerance for floating-point sum-equals comparison.
    /// </summary>
    public const double DefaultTolerance = 0.0001;

    /// <summary>
    /// The constraint group name. Must match <see cref="ConfigConstraintGroupAttribute.GroupName"/>
    /// on at least two properties in the same class.
    /// </summary>
    public string GroupName { get; }

    /// <summary>
    /// The type of collective constraint to enforce.
    /// </summary>
    public ConstraintGroupType Constraint { get; }

    /// <summary>
    /// Target value for sum constraints. Required for <see cref="ConstraintGroupType.SumEquals"/>,
    /// <see cref="ConstraintGroupType.SumMinimum"/>, <see cref="ConstraintGroupType.SumMaximum"/>.
    /// Use <see cref="double.NaN"/> (default) for non-sum constraints.
    /// </summary>
    public double Value { get; set; } = double.NaN;

    /// <summary>
    /// Floating-point comparison tolerance for <see cref="ConstraintGroupType.SumEquals"/>.
    /// Ignored by other constraint types. Default: <see cref="DefaultTolerance"/> (0.0001).
    /// </summary>
    public double Tolerance { get; set; } = DefaultTolerance;

    /// <summary>
    /// Creates a constraint group definition.
    /// </summary>
    /// <param name="groupName">The constraint group name.</param>
    /// <param name="constraint">The constraint type to enforce.</param>
    /// <exception cref="ArgumentNullException">Thrown when groupName is null.</exception>
    /// <exception cref="ArgumentException">Thrown when groupName is empty or whitespace.</exception>
    public ConfigConstraintGroupDefinitionAttribute(string groupName, ConstraintGroupType constraint)
    {
        if (groupName == null)
            throw new ArgumentNullException(nameof(groupName));
        if (string.IsNullOrWhiteSpace(groupName))
            throw new ArgumentException("Group name must not be empty or whitespace.", nameof(groupName));

        GroupName = groupName;
        Constraint = constraint;
    }

    /// <summary>
    /// Returns true if <see cref="Value"/> has been set (is not NaN).
    /// </summary>
    public bool HasValue => !double.IsNaN(Value);

    /// <summary>
    /// Returns true if this is a sum-type constraint (SumEquals, SumMinimum, SumMaximum).
    /// </summary>
    public bool IsSumConstraint => Constraint is ConstraintGroupType.SumEquals
        or ConstraintGroupType.SumMinimum
        or ConstraintGroupType.SumMaximum;

    /// <summary>
    /// Returns true if this is a presence-type constraint (ExactlyOne, AtMostOne, AllOrNone).
    /// </summary>
    public bool IsPresenceConstraint => Constraint is ConstraintGroupType.ExactlyOne
        or ConstraintGroupType.AtMostOne
        or ConstraintGroupType.AllOrNone;

    /// <summary>
    /// Gets a human-readable description of this constraint for error messages.
    /// </summary>
    /// <returns>A string describing the constraint and its parameters.</returns>
    public string GetConstraintDescription()
    {
        return Constraint switch
        {
            ConstraintGroupType.ExactlyOne => "exactly one must be set",
            ConstraintGroupType.AtMostOne => "at most one may be set",
            ConstraintGroupType.AllOrNone => "all must be set or all must be absent",
            ConstraintGroupType.SumEquals => $"must sum to {Value} (tolerance: {Tolerance})",
            ConstraintGroupType.SumMinimum => $"must sum to at least {Value}",
            ConstraintGroupType.SumMaximum => $"must sum to at most {Value}",
            _ => $"unknown constraint: {Constraint}"
        };
    }
}
