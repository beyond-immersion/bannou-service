namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Specifies that a numeric configuration property must be a multiple of a given value.
/// Matches OpenAPI 3.0 validation keyword: multipleOf.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is generated from OpenAPI schema validation keywords and validated
/// at startup by <see cref="Configuration.IServiceConfiguration.ValidateMultipleOf"/>.
/// </para>
/// <para>
/// Use this for ensuring configuration values follow required increments, such as:
/// - Timeouts in milliseconds (multiple of 1000 for whole seconds)
/// - Percentage steps (multiple of 5 for 0%, 5%, 10%, etc.)
/// - Currency precision (multiple of 0.01 for cents)
/// - Buffer sizes (multiple of 1024 for KB boundaries)
/// </para>
/// <para>
/// For floating point values, a small tolerance is used to handle precision issues.
/// </para>
/// </remarks>
/// <example>
/// Schema:
/// <code>
/// TimeoutMs:
///   type: integer
///   multipleOf: 1000
///   default: 5000
/// </code>
/// Generated:
/// <code>
/// [ConfigMultipleOf(1000)]
/// public int TimeoutMs { get; set; } = 5000;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class ConfigMultipleOfAttribute : Attribute, IServiceAttribute
{
    /// <summary>
    /// Default tolerance for floating point comparison.
    /// </summary>
    private const double DefaultTolerance = 1e-9;

    /// <summary>
    /// The value that the property must be a multiple of.
    /// </summary>
    public double Factor { get; }

    /// <summary>
    /// Tolerance for floating point comparison. Defaults to 1e-9.
    /// </summary>
    public double Tolerance { get; set; } = DefaultTolerance;

    /// <summary>
    /// Creates a multipleOf constraint with the specified factor.
    /// </summary>
    /// <param name="factor">The value that the property must be a multiple of. Must be positive and non-zero.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when factor is zero or negative.</exception>
    public ConfigMultipleOfAttribute(double factor)
    {
        if (factor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "Factor must be positive and non-zero.");
        }

        Factor = factor;
    }

    /// <summary>
    /// Validates whether the given value is a multiple of <see cref="Factor"/>.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <returns>True if the value is a multiple of the factor; otherwise, false.</returns>
    public bool IsValid(double value)
    {
        // Special case: zero is a multiple of any positive number
        if (value == 0)
        {
            return true;
        }

        // Calculate remainder
        var remainder = Math.Abs(value % Factor);

        // Check if remainder is close to zero or close to the factor
        // (handles floating point precision issues)
        return remainder < Tolerance || Math.Abs(remainder - Factor) < Tolerance;
    }

    /// <summary>
    /// Gets a human-readable description of this constraint.
    /// </summary>
    /// <returns>A string describing the required multiple.</returns>
    public string GetMultipleOfDescription()
    {
        // Format nicely for common cases
        if (Factor == Math.Floor(Factor))
        {
            return $"must be a multiple of {(long)Factor}";
        }
        else
        {
            return $"must be a multiple of {Factor}";
        }
    }
}
