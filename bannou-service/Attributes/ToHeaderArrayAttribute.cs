namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute to propagate common headers through dapr requests.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ToHeaderArrayAttribute : Attribute
{
    public string? Name { get; set; }
    public string? Delineator { get; set; }
}
