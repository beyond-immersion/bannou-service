using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute for auto-loading dapr services.
/// Use [RunServiceIfEnabled] on configuration to optionally/automatically enable services.
/// </summary>
[AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class DaprControllerAttribute : RouteAttribute, IServiceAttribute
{
    public DaprControllerAttribute(string template)
        : base(template) {}
}
