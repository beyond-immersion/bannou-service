using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute to specify endpoints used for dapr service.
/// </summary>
[AttributeUsage(validOn: AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class DaprRouteAttribute : RouteAttribute, IServiceAttribute
{
    public DaprRouteAttribute(string template)
        : base(template) {}
}
