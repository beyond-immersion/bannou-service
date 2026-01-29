namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Specifies string length constraints for a configuration property.
/// Matches OpenAPI 3.0 validation keywords: minLength, maxLength.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is generated from OpenAPI schema validation keywords and validated
/// at startup by <see cref="Configuration.IServiceConfiguration.ValidateStringLengths"/>.
/// </para>
/// <para>
/// Use this for ensuring configuration strings meet length requirements, such as:
/// - JWT secrets (minimum 32 characters for security)
/// - Salt values (minimum length for cryptographic strength)
/// - Names and codes (maximum length for database constraints)
/// </para>
/// <para>
/// Use <see cref="NoMinLength"/> and <see cref="NoMaxLength"/> sentinel values to indicate
/// unbounded lengths, as C# doesn't allow nullable types in attribute parameters.
/// </para>
/// </remarks>
/// <example>
/// Schema:
/// <code>
/// JwtSecret:
///   type: string
///   minLength: 32
///   maxLength: 512
/// </code>
/// Generated:
/// <code>
/// [ConfigStringLength(MinLength = 32, MaxLength = 512)]
/// public string JwtSecret { get; set; } = "default-dev-secret-key-32-chars!";
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class ConfigStringLengthAttribute : Attribute, IServiceAttribute
{
    /// <summary>
    /// Sentinel value indicating no minimum length. Use in attribute: <c>MinLength = ConfigStringLengthAttribute.NoMinLength</c>
    /// </summary>
    public const int NoMinLength = -1;

    /// <summary>
    /// Sentinel value indicating no maximum length. Use in attribute: <c>MaxLength = ConfigStringLengthAttribute.NoMaxLength</c>
    /// </summary>
    public const int NoMaxLength = -1;

    /// <summary>
    /// The minimum allowed string length (inclusive).
    /// Use <see cref="NoMinLength"/> (-1) for no lower bound.
    /// </summary>
    public int MinLength { get; set; } = NoMinLength;

    /// <summary>
    /// The maximum allowed string length (inclusive).
    /// Use <see cref="NoMaxLength"/> (-1) for no upper bound.
    /// </summary>
    public int MaxLength { get; set; } = NoMaxLength;

    /// <summary>
    /// Creates a string length constraint with no bounds (use named properties to set bounds).
    /// </summary>
    public ConfigStringLengthAttribute()
    {
    }

    /// <summary>
    /// Creates a string length constraint with minimum and maximum bounds.
    /// </summary>
    /// <param name="minLength">The minimum allowed string length (inclusive).</param>
    /// <param name="maxLength">The maximum allowed string length (inclusive).</param>
    public ConfigStringLengthAttribute(int minLength, int maxLength)
    {
        MinLength = minLength;
        MaxLength = maxLength;
    }

    /// <summary>
    /// Returns true if a minimum length is defined (not equal to <see cref="NoMinLength"/>).
    /// </summary>
    public bool HasMinLength => MinLength != NoMinLength;

    /// <summary>
    /// Returns true if a maximum length is defined (not equal to <see cref="NoMaxLength"/>).
    /// </summary>
    public bool HasMaxLength => MaxLength != NoMaxLength;

    /// <summary>
    /// Validates whether the given string satisfies this length constraint.
    /// </summary>
    /// <param name="value">The string value to validate. Null values are considered invalid if MinLength > 0.</param>
    /// <returns>True if the string length is within the allowed range; otherwise, false.</returns>
    public bool IsValid(string? value)
    {
        // Null handling: if MinLength is specified and > 0, null is invalid
        // If no MinLength or MinLength is 0, null is treated as empty string (length 0)
        if (value == null)
        {
            return !HasMinLength || MinLength == 0;
        }

        var length = value.Length;

        if (HasMinLength && length < MinLength)
        {
            return false;
        }

        if (HasMaxLength && length > MaxLength)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets a human-readable description of this length constraint.
    /// </summary>
    /// <returns>A string describing the allowed length range.</returns>
    public string GetLengthDescription()
    {
        if (HasMinLength && HasMaxLength)
        {
            return $"[{MinLength}, {MaxLength}] characters";
        }
        else if (HasMinLength)
        {
            return $">= {MinLength} characters";
        }
        else if (HasMaxLength)
        {
            return $"<= {MaxLength} characters";
        }
        else
        {
            return "any length";
        }
    }
}
