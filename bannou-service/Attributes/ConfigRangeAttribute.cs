namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Specifies numeric range constraints for a configuration property.
/// Matches OpenAPI 3.0 validation keywords: minimum, maximum, exclusiveMinimum, exclusiveMaximum.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is generated from OpenAPI schema validation keywords and validated
/// at startup by <see cref="Configuration.IServiceConfiguration.ValidateNumericRanges"/>.
/// </para>
/// <para>
/// Unlike <see cref="System.ComponentModel.DataAnnotations.RangeAttribute"/>, this attribute
/// supports exclusive bounds (value must be strictly greater/less than the bound).
/// </para>
/// <para>
/// Use <see cref="NoMinimum"/> and <see cref="NoMaximum"/> sentinel values to indicate
/// unbounded ranges, as C# doesn't allow nullable types in attribute parameters.
/// </para>
/// </remarks>
/// <example>
/// Schema:
/// <code>
/// RtpEnginePort:
///   type: integer
///   minimum: 1
///   maximum: 65535
/// </code>
/// Generated:
/// <code>
/// [ConfigRange(Minimum = 1, Maximum = 65535)]
/// public int RtpEnginePort { get; set; } = 22222;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class ConfigRangeAttribute : Attribute, IServiceAttribute
{
    /// <summary>
    /// Sentinel value indicating no minimum bound. Use in attribute: <c>Minimum = ConfigRangeAttribute.NoMinimum</c>
    /// </summary>
    public const double NoMinimum = double.NegativeInfinity;

    /// <summary>
    /// Sentinel value indicating no maximum bound. Use in attribute: <c>Maximum = ConfigRangeAttribute.NoMaximum</c>
    /// </summary>
    public const double NoMaximum = double.PositiveInfinity;

    /// <summary>
    /// The minimum allowed value (inclusive unless <see cref="ExclusiveMinimum"/> is true).
    /// Use <see cref="NoMinimum"/> (double.NegativeInfinity) for no lower bound.
    /// </summary>
    public double Minimum { get; set; } = NoMinimum;

    /// <summary>
    /// The maximum allowed value (inclusive unless <see cref="ExclusiveMaximum"/> is true).
    /// Use <see cref="NoMaximum"/> (double.PositiveInfinity) for no upper bound.
    /// </summary>
    public double Maximum { get; set; } = NoMaximum;

    /// <summary>
    /// If true, the value must be strictly greater than <see cref="Minimum"/> (not equal).
    /// Matches OpenAPI 3.0 exclusiveMinimum: true behavior.
    /// </summary>
    public bool ExclusiveMinimum { get; set; }

    /// <summary>
    /// If true, the value must be strictly less than <see cref="Maximum"/> (not equal).
    /// Matches OpenAPI 3.0 exclusiveMaximum: true behavior.
    /// </summary>
    public bool ExclusiveMaximum { get; set; }

    /// <summary>
    /// Creates a range constraint with no bounds (use named properties to set bounds).
    /// </summary>
    public ConfigRangeAttribute()
    {
    }

    /// <summary>
    /// Creates a range constraint with inclusive minimum and maximum bounds.
    /// </summary>
    /// <param name="minimum">The minimum allowed value (inclusive).</param>
    /// <param name="maximum">The maximum allowed value (inclusive).</param>
    public ConfigRangeAttribute(double minimum, double maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>
    /// Returns true if a minimum bound is defined (not equal to <see cref="NoMinimum"/>).
    /// </summary>
    public bool HasMinimum => !double.IsNegativeInfinity(Minimum);

    /// <summary>
    /// Returns true if a maximum bound is defined (not equal to <see cref="NoMaximum"/>).
    /// </summary>
    public bool HasMaximum => !double.IsPositiveInfinity(Maximum);

    /// <summary>
    /// Validates whether the given value satisfies this range constraint.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <returns>True if the value is within the allowed range; otherwise, false.</returns>
    public bool IsValid(double value)
    {
        if (HasMinimum)
        {
            if (ExclusiveMinimum)
            {
                if (value <= Minimum)
                    return false;
            }
            else
            {
                if (value < Minimum)
                    return false;
            }
        }

        if (HasMaximum)
        {
            if (ExclusiveMaximum)
            {
                if (value >= Maximum)
                    return false;
            }
            else
            {
                if (value > Maximum)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets a human-readable description of this range constraint.
    /// </summary>
    /// <returns>A string describing the allowed range.</returns>
    public string GetRangeDescription()
    {
        var minBracket = ExclusiveMinimum ? "(" : "[";
        var maxBracket = ExclusiveMaximum ? ")" : "]";
        var minValue = HasMinimum ? Minimum.ToString() : "-∞";
        var maxValue = HasMaximum ? Maximum.ToString() : "∞";
        return $"{minBracket}{minValue}, {maxValue}{maxBracket}";
    }
}
