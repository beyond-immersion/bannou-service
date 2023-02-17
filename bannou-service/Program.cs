using System;
using System.Collections.Generic;
using System.Text.Json;
using Dapr;
using Dapr.Client;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Logging;
using BeyondImmersion.BannouService.Services;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace BeyondImmersion.BannouService
{
    public class Program
    {
        /// <summary>
        /// Service configuration- pulled from Config.json, ENVs, and command switches.
        /// </summary>
        public static ServiceConfiguration Configuration { get; private set; }

        /// <summary>
        /// Service logger.
        /// </summary>
        public static ILogger Logger { get; private set; }

        /// <summary>
        /// Internal service GUID- largely used for administrative network commands.
        /// Randomly generated on service startup.
        /// </summary>
        public static string ServiceGUID { get; private set; }


        /// <summary>
        /// Shared dapr client interface, used by all enabled internal services.
        /// </summary>
        internal static DaprClient DaprClient { get; private set; }

        /// <summary>
        /// Token source for initiating a clean shutdown.
        /// </summary>
        internal static CancellationTokenSource ShutdownCancellationTokenSource { get; } = new CancellationTokenSource();

        /// <summary>
        /// Internal registry of all loaded dapr services.
        /// </summary>
        private static Dictionary<string, (DaprServiceAttribute, IDaprService)> ServiceRegistry { get; }
            = new Dictionary<string, (DaprServiceAttribute, IDaprService)>();


        private static async Task Main(string[] args)
        {
            Configuration = ServiceConfiguration.BuildConfiguration(args, "BANNOU_");
            if (!ValidateConfiguration())
                return;

            if (Configuration.Force_Service_ID != null)
                ServiceGUID = Configuration.Force_Service_ID;
            else
                ServiceGUID = Guid.NewGuid().ToString().ToLower();

            Logger = LoggerFactory.Create((options) =>
                {
                    options.AddJsonConsole();
                    options.SetMinimumLevel(LogLevel.Trace);
                })
                .CreateLogger<Program>();

            Logger.Log(LogLevel.Debug, null, "Service startup began.");

            // all clients should share the same dapr configuration settings
            DaprClient = new DaprClientBuilder()
                .UseJsonSerializationOptions(new JsonSerializerOptions
                {
                    AllowTrailingCommas = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
                    IgnoreReadOnlyFields = false,
                    IgnoreReadOnlyProperties = false,
                    IncludeFields = false,
                    MaxDepth = 32,
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
                    PropertyNameCaseInsensitive = false,
                    ReadCommentHandling = JsonCommentHandling.Allow,
                    UnknownTypeHandling = System.Text.Json.Serialization.JsonUnknownTypeHandling.JsonElement,
                    WriteIndented = false
                })
                .Build();

            if (!BuildDaprServiceRegistry())
            {
                Logger.Log(LogLevel.Error, null, "Building dapr service registry failed- exiting application.");
                return;
            }

            var builder = WebApplication.CreateBuilder(args);
            var webApp = builder.Build();
            if (webApp == null)
            {
                Logger.Log(LogLevel.Error, null, "Building web application failed- exiting application.");
                return;
            }

            SetAdminEndpoints(webApp);
            AddDaprServices(webApp);

            Logger.Log(LogLevel.Debug, null, "Service startup complete- webhost starting.");

            await Task.Run(async () => await webApp.RunAsync(ShutdownCancellationTokenSource.Token));

            Logger.Log(LogLevel.Debug, null, "Service shutdown began.");

            StopRegisteredDaprServices();
            DaprClient.Dispose();

            Logger.Log(LogLevel.Debug, null, "Service shutdown completed- exiting application.");
        }

        private static void InitiateShutdown()
        {
            ShutdownCancellationTokenSource.Cancel();
        }

        private static bool ValidateConfiguration()
        {
            if (Configuration == null)
            {
                Logger.Log(LogLevel.Error, null, "Service configuration required, even if only with default values.");
                return false;
            }

            if (!ServiceConfiguration.IsAnyServiceEnabled())
            {
                Logger.Log(LogLevel.Error, null, "Dapr services not configured to handle any roles / APIs.");
                return false;
            }

            return true;
        }


        private static void SetAdminEndpoints(WebApplication webApp)
        {
            webApp.MapGet($"/admin_{ServiceGUID}/shutdown", InitiateShutdown);
        }

        private static bool BuildDaprServiceRegistry()
        {
            // get all dapr services in all loaded assemblies
            var serviceClasses = BaseServiceAttribute.GetClassesWithAttribute<DaprServiceAttribute>();
            if (!serviceClasses.Any())
            {
                Logger.Log(LogLevel.Error, null, $"No dapr services found to instantiate.");
                return false;
            }

            // prefixes need to be unique, so build a registry
            var serviceLookup = new Dictionary<string, (Type, DaprServiceAttribute)>();
            foreach (var serviceClass in serviceClasses)
            {
                if (!typeof(IDaprService).IsAssignableFrom(serviceClass.Item1))
                {
                    Logger.Log(LogLevel.Error, null, $"Dapr service attribute attached to a non-service class.",
                        logParams: new JObject() { ["service_type"] = serviceClass.Item1.Name });
                    continue;
                }

                var servicePrefix = serviceClass.Item2.ServicePrefix.ToUpper();
                if (serviceLookup.ContainsKey(servicePrefix) && serviceClass.GetType().Assembly != Assembly.GetExecutingAssembly())
                    serviceLookup[servicePrefix] = serviceClass;
                else
                    serviceLookup[servicePrefix] = serviceClass;
            }

            // now we only have 1 per prefix, & with highest priority
            // instantiate them
            foreach (var serviceClass in serviceLookup.Values)
            {
                var serviceType = serviceClass.Item1;
                var serviceAttr = serviceClass.Item2;

                if (!ServiceConfiguration.IsServiceEnabled(serviceType))
                {
                    Logger.Log(LogLevel.Debug, null, $"Dapr service is disabled.",
                        logParams: new JObject() { ["service_type"] = serviceType.Name });
                    continue;
                }

                try
                {
                    var serviceInstance = (IDaprService)Activator.CreateInstance(serviceType, true, null);
                    if (serviceInstance == null)
                        throw new NullReferenceException();

                    ServiceRegistry[serviceClass.Item2.ServicePrefix] = (serviceAttr, serviceInstance);

                    Logger.Log(LogLevel.Debug, null, $"Instantiation of dapr service successful.",
                        logParams: new JObject() { ["service_type"] = serviceClass.Item1.Name });
                }
                catch (Exception e)
                {
                    Logger.Log(LogLevel.Error, e, $"Instantiation of dapr service failed.",
                        logParams: new JObject() { ["service_type"] = serviceClass.Item1.Name });
                }
            }
            return ServiceRegistry.Any();
        }

        private static void StopRegisteredDaprServices()
        {
            foreach (var serviceData in ServiceRegistry.Values)
                serviceData.Item2.Shutdown();
        }

        private static WebApplication AddDaprServices(WebApplication webApp)
        {
            // now we only have 1 per prefix, & with highest priority
            // instantiate them
            foreach (var serviceClass in ServiceRegistry.Values)
            {
                // iterate route methods for service, add to webapp
                foreach (var routeMethod in BaseServiceAttribute.GetMethodsWithAttribute<ServiceRoute>(serviceClass.Item2.GetType()))
                {
                    if (!routeMethod.Item1.GetParameters().Any() || routeMethod.Item1.GetParameters().Length > 1)
                        continue;

                    if (routeMethod.Item1.GetParameters()[0].ParameterType != typeof(HttpContext))
                        continue;

                    string routeSuffix = routeMethod.Item2.RouteUrl ?? "";
                    if (!routeSuffix.StartsWith('/'))
                        routeSuffix = $"/{routeSuffix}";

                    string routePrefix = serviceClass.Item1.ServicePrefix;
                    if (!routePrefix.StartsWith('/'))
                        routePrefix = $"/{routePrefix}";

                    if (routePrefix.EndsWith('/'))
                        routePrefix = routePrefix.Remove(routePrefix.Length - 1, 1);

                    Delegate methodDelegate;
                    if (routeMethod.Item1.IsStatic)
                        methodDelegate = Delegate.CreateDelegate(typeof(Action<HttpContext>), target: serviceClass.GetType(), method: routeMethod.Item1.Name);
                    else
                        methodDelegate = Delegate.CreateDelegate(typeof(Action<HttpContext>), target: serviceClass, method: routeMethod.Item1.Name, ignoreCase: true);

                    switch (routeMethod.Item2.HttpMethod)
                    {
                        case HttpMethodTypes.GET:
                            webApp.MapGet(routePrefix + routeSuffix, methodDelegate);
                            break;
                        case HttpMethodTypes.POST:
                            webApp.MapPost(routePrefix + routeSuffix, methodDelegate);
                            break;
                        case HttpMethodTypes.PUT:
                            webApp.MapPut(routePrefix + routeSuffix, methodDelegate);
                            break;
                        case HttpMethodTypes.DELETE:
                            webApp.MapDelete(routePrefix + routeSuffix, methodDelegate);
                            break;
                        default:
                            Logger.Log(LogLevel.Error, null, $"Could not add unknown dapr service route type.",
                                logParams: new JObject()
                                {
                                    ["service_type"] = serviceClass.Item2.GetType().Name,
                                    ["route_type"] = routeMethod.Item2.HttpMethod.ToString()
                                });
                            continue;
                    }

                    Logger.Log(LogLevel.Debug, null, $"Dapr service route added successfully.",
                        logParams: new JObject()
                        {
                            ["service_type"] = serviceClass.Item2.GetType().Name,
                            ["route_type"] = routeMethod.Item2.HttpMethod.ToString()
                        });
                }
            }
            return webApp;
        }
    }
}
