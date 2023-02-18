using BeyondImmersion.BannouService.Application;
using System.Reflection;

namespace BeyondImmersion.BannouService.Attributes
{
    /// <summary>
    /// Attribute for auto-loading dapr services.
    /// Use [RunServiceIfEnabled] on configuration to optionally/automatically enable services.
    /// </summary>
    [AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class DaprServiceAttribute : BaseServiceAttribute
    {
        /// <summary>
        /// The readable name of this dapr service, for logging.
        /// </summary>
        public string ServiceName { get; }

        /// <summary>
        /// Prefix to use for generating service UUID -
        /// for receiving administrative commands to a distinct instance.
        /// </summary>
        public string ServicePrefix { get; }

        private DaprServiceAttribute() { }
        public DaprServiceAttribute(string serviceName, string servicePrefix)
        {
            ServiceName = serviceName;
            ServicePrefix = servicePrefix;
        }
    }

    [AttributeUsage(validOn: AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class ServiceRoute : BaseServiceAttribute
    {
        /// <summary>
        /// The route url string, ie: `{language}/{controller}/{action}/{id}`
        /// </summary>
        public string RouteUrl { get; }

        /// <summary>
        /// The HTTP Method to use for endpoint- default POST.
        /// </summary>
        public HttpMethodTypes HttpMethod { get; } = HttpMethodTypes.POST;

        private ServiceRoute() { }
        public ServiceRoute(string routeUrl)
            => RouteUrl = routeUrl;
        public ServiceRoute(HttpMethodTypes httpMethod, string routeUrl)
            : this(routeUrl)
            => HttpMethod = httpMethod;
    }
}
