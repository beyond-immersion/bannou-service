using BeyondImmersion.BannouService.Application;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace BeyondImmersion.BannouService.Attributes
{
    /// <summary>
    /// Attribute for auto-loading dapr services.
    /// Use [RunServiceIfEnabled] on configuration to optionally/automatically enable services.
    /// </summary>
    [AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class DaprServiceAttribute : RouteAttribute, IServiceAttribute
    {
        public DaprServiceAttribute(string template)
            : base(template) {}
    }

    [AttributeUsage(validOn: AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class ServiceRoute : RouteAttribute, IServiceAttribute
    {
        public ServiceRoute(string template)
            : base(template) {}
    }
}
