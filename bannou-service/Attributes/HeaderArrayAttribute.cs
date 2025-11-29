namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute to propagate common headers through dapr requests.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class HeaderArrayAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the header name to map to this property.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the delimiter used to split array values in the header.
    /// </summary>
    public string? Delimeter { get; set; }
}
