using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.CodeAnalysis;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute to specify endpoints used for dapr service.
/// </summary>
[AttributeUsage(validOn: AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class DaprRouteAttribute : RouteAttribute, IServiceAttribute
{
    /// <summary>
    /// Initializes a new instance of the DaprRouteAttribute with the specified route template.
    /// </summary>
    /// <param name="template">The route template for the Dapr service endpoint.</param>
    public DaprRouteAttribute([StringSyntax("Route")] string template)
        : base(template) { }
}
