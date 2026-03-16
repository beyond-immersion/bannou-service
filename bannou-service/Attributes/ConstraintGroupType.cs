namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Types of collective constraints that can be applied to configuration property groups.
/// Generated from <c>x-constraint-groups</c> in configuration schemas.
/// </summary>
/// <remarks>
/// <para>
/// Constraint groups enable cross-property validation at startup. Individual properties
/// declare membership via <see cref="ConfigConstraintGroupAttribute"/>, and the group's
/// constraint type and parameters are declared via <see cref="ConfigConstraintGroupDefinitionAttribute"/>
/// on the configuration class.
/// </para>
/// <para>
/// Presence constraints (<see cref="ExactlyOne"/>, <see cref="AtMostOne"/>, <see cref="AllOrNone"/>)
/// operate on nullability — a non-null value means "set". All participating properties must be nullable.
/// </para>
/// <para>
/// Sum constraints (<see cref="SumEquals"/>, <see cref="SumMinimum"/>, <see cref="SumMaximum"/>)
/// operate on numeric values. Null properties contribute 0 to the sum. All participating properties
/// must be numeric (<c>int?</c>, <c>double?</c>, etc.).
/// </para>
/// </remarks>
public enum ConstraintGroupType
{
    /// <summary>
    /// Exactly one property in the group must be non-null.
    /// Use for mutually exclusive configuration where one option is required.
    /// </summary>
    ExactlyOne,

    /// <summary>
    /// Zero or one property in the group may be non-null.
    /// Use for mutually exclusive configuration where all options are optional.
    /// </summary>
    AtMostOne,

    /// <summary>
    /// Either all properties in the group are non-null, or all are null.
    /// Use for co-dependent configuration (e.g., TLS cert + key).
    /// </summary>
    AllOrNone,

    /// <summary>
    /// Property values must sum to the target value within tolerance.
    /// Requires <see cref="ConfigConstraintGroupDefinitionAttribute.Value"/> to be set.
    /// Null properties contribute 0 to the sum.
    /// </summary>
    SumEquals,

    /// <summary>
    /// Property values must sum to at least the target value.
    /// Requires <see cref="ConfigConstraintGroupDefinitionAttribute.Value"/> to be set.
    /// Null properties contribute 0 to the sum.
    /// </summary>
    SumMinimum,

    /// <summary>
    /// Property values must sum to at most the target value.
    /// Requires <see cref="ConfigConstraintGroupDefinitionAttribute.Value"/> to be set.
    /// Null properties contribute 0 to the sum.
    /// </summary>
    SumMaximum
}
