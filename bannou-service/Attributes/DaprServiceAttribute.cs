using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute for auto-loading dapr services.
/// Use {Name}_SERVICE_ENABLE as ENV or switch to enable/disable.
/// </summary>
[AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DaprServiceAttribute : BaseServiceAttribute
{
    public bool Priority { get; }
    public string Name { get; }

    private DaprServiceAttribute() { }
    public DaprServiceAttribute(string name, bool priority = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        Name = name;
        Priority = priority;
    }
}
