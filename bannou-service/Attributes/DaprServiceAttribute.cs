using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute for auto-loading dapr services.
/// Use {Name}_SERVICE_ENABLE as ENV or switch to enable/disable.
/// </summary>
[AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DaprServiceAttribute : BaseServiceAttribute
{
    public string? Name { get; }
    public DaprServiceAttribute(string? name = null)
        => Name = name;
}
