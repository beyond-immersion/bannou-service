using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Logging;
using BeyondImmersion.BannouService.Services;
using Dapr;
using Dapr.Client;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;

namespace BeyondImmersion.BannouService
{
    public static class Program
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
            Logger = ServiceLogging.CreateLogger();

            Logger.Log(LogLevel.Debug, null, "Service starting.");

            Configuration = ServiceConfiguration.BuildConfiguration(args, "BANNOU_");
            if (!ValidateConfiguration())
                return;

            ServiceGUID = Configuration.Force_Service_ID ?? Guid.NewGuid().ToString().ToLower();

            using WebApplication? webApp = WebApplication.CreateBuilder(args)?.Build();
            if (webApp == null)
            {
                Logger.Log(LogLevel.Error, null, "Building web application failed- exiting application.");
                return;
            }

            DaprClient = new DaprClientBuilder()
                .UseJsonSerializationOptions(ServiceConfiguration.DaprSerializerConfig)
                .Build();

            if (!Configuration.Skip_Dapr_Healthcheck && !await DaprClient.CheckHealthAsync(ShutdownCancellationTokenSource.Token))
            {
                Logger.Log(LogLevel.Error, null, "Dapr sidecar unhealthy/not found- exiting application.");
                return;
            }

            try
            {
                BuildDaprServiceRegistry();

                SetAdminEndpoints(webApp);
                var unused = AddDaprServiceEndpoints(webApp);

                Logger.Log(LogLevel.Debug, null, "Service startup complete- webhost starting.");
                {
                    // blocks until webhost dies / server shutdown command received
                    await Task.Run(async () => await webApp.RunAsync(ShutdownCancellationTokenSource.Token), ShutdownCancellationTokenSource.Token);
                }

                Logger.Log(LogLevel.Debug, null, "Webhost stopped- starting controlled service shutdown.");

                StopRegisteredDaprServices();
            }
            catch (Exception e)
            {
                Logger.Log(LogLevel.Error, e, "Starting emergency service shutdown.");
            }
            finally
            {
                DaprClient.Dispose();
            }

            Logger.Log(LogLevel.Debug, null, "Service shutdown complete.");
        }

        /// <summary>
        /// Will stop the webhost and initiate a service shutdown.
        /// </summary>
        public static void InitiateShutdown() => ShutdownCancellationTokenSource.Cancel();

        /// <summary>
        /// Verifies that the service configuration contains required values (from ENVs/switches/etc).
        /// </summary>
        private static bool ValidateConfiguration()
        {
            if (Configuration == null)
            {
                Logger.Log(LogLevel.Error, null, "Service configuration required, even if only with default values.");
                return false;
            }

            if (!Configuration.IsAnyServiceEnabled())
            {
                Logger.Log(LogLevel.Error, null, "Dapr services not configured to handle any roles / APIs.");
                return false;
            }

            foreach ((Type, DaprServiceAttribute) serviceClassData in GetDaprServiceTypes())
            {
                Type serviceType = serviceClassData.Item1;

                if (!Configuration.IsServiceEnabled(serviceType))
                    continue;

                if (!Configuration.HasRequiredConfiguration(serviceType))
                {
                    Logger.Log(LogLevel.Debug, null, $"Required configuration is missing to start an enabled dapr service.",
                        logParams: new JObject() { ["service_type"] = serviceType.Name });
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Binds the HTTP endpoints for root administrative commands against this service against.
        /// </summary>
        private static void SetAdminEndpoints(WebApplication webApp) => webApp.MapGet($"/admin_{ServiceGUID}/shutdown", InitiateShutdown);

        /// <summary>
        /// Finds all dapr service classes in loaded assemblies, determines which need to be enabled
        /// based on the current configuration, and instantiates new instances of the services.
        /// 
        /// Places service instances (along with their service attribute) in the local registry.
        /// </summary>
        private static void BuildDaprServiceRegistry()
        {
            foreach ((Type, DaprServiceAttribute) serviceClassData in GetDaprServiceTypes())
            {
                Type serviceType = serviceClassData.Item1;
                DaprServiceAttribute serviceAttr = serviceClassData.Item2;

                if (!Configuration.IsServiceEnabled(serviceType))
                    continue;

                try
                {
                    var serviceInstance = (IDaprService?)Activator.CreateInstance(type: serviceType, nonPublic: true);
                    if (serviceInstance == null)
                        throw new NullReferenceException();

                    ServiceRegistry[serviceAttr.ServicePrefix] = (serviceAttr, serviceInstance);

                    Logger.Log(LogLevel.Debug, null, $"Instantiation of dapr service successful.",
                        logParams: new JObject() { ["service_type"] = serviceType.Name });
                }
                catch (Exception e)
                {
                    Logger.Log(LogLevel.Error, e, $"Instantiation of dapr service failed.",
                        logParams: new JObject() { ["service_type"] = serviceType.Name });
                }
            }
        }

        /// <summary>
        /// Gets the full list of all dapr service classes (with associated attribute) in loaded assemblies.
        /// </summary>
        private static (Type, DaprServiceAttribute)[] GetDaprServiceTypes()
        {
            List<(Type, DaprServiceAttribute)> serviceClasses = BaseServiceAttribute.GetClassesWithAttribute<DaprServiceAttribute>();
            if (!serviceClasses.Any())
            {
                Logger.Log(LogLevel.Error, null, $"No dapr services found to instantiate.");
                return Array.Empty<(Type, DaprServiceAttribute)>();
            }

            // prefixes need to be unique, so assign to a tmp hash/dictionary lookup
            var serviceLookup = new Dictionary<string, (Type, DaprServiceAttribute)>();
            foreach ((Type, DaprServiceAttribute) serviceClass in serviceClasses)
            {
                Type serviceType = serviceClass.Item1;
                DaprServiceAttribute serviceAttr = serviceClass.Item2;

                if (!typeof(IDaprService).IsAssignableFrom(serviceType))
                {
                    Logger.Log(LogLevel.Error, null, $"Dapr service attribute attached to a non-service class.",
                        logParams: new JObject() { ["service_type"] = serviceType.Name });
                    continue;
                }

                var servicePrefix = serviceAttr.ServicePrefix.ToUpper();
                if (!serviceLookup.ContainsKey(servicePrefix) || serviceClass.GetType().Assembly != Assembly.GetExecutingAssembly())
                    serviceLookup[servicePrefix] = serviceClass;
            }

            return serviceLookup.Values.ToArray();
        }

        /// <summary>
        /// Stops all dapr services instances in the registry, before clearing out the registry itself.
        /// </summary>
        private static void StopRegisteredDaprServices()
        {
            foreach ((DaprServiceAttribute, IDaprService) serviceData in ServiceRegistry.Values)
                serviceData.Item2.Shutdown();

            ServiceRegistry.Clear();
        }

        /// <summary>
        /// Binds HTTP endpoints for all registered dapr services.
        /// </summary>
        private static bool AddDaprServiceEndpoints(WebApplication webApp)
        {
            var endpointAdded = false;
            foreach ((DaprServiceAttribute, IDaprService) serviceClass in ServiceRegistry.Values)
            {
                DaprServiceAttribute serviceAttr = serviceClass.Item1;
                IDaprService serviceInstance = serviceClass.Item2;

                // iterate route methods for service, add to webapp
                foreach ((MethodInfo, ServiceRoute) routeMethod in BaseServiceAttribute.GetMethodsWithAttribute<ServiceRoute>(serviceClass.Item2.GetType()))
                {
                    MethodInfo routeMethodInfo = routeMethod.Item1;
                    ServiceRoute routeAttr = routeMethod.Item2;

                    try
                    {
                        if (routeMethodInfo.GetParameters().Length > 1 || !new Type?[] { null, typeof(HttpContext) }.Contains(routeMethodInfo.GetParameters()?.FirstOrDefault()?.ParameterType))
                        {
                            Logger.Log(LogLevel.Error, null, $"Dapr route methods can only contain one parameter, of type HttpContext.",
                                logParams: new JObject()
                                {
                                    ["service_type"] = serviceInstance.GetType().Name,
                                    ["endpoint_type"] = routeAttr.HttpMethod.ToString()
                                });
                            continue;
                        }

                        if (!new[] { typeof(void), typeof(Task) }.Contains(routeMethodInfo.ReturnType))
                        {
                            Logger.Log(LogLevel.Error, null, $"Dapr route methods must have void or task return type.",
                                logParams: new JObject()
                                {
                                    ["service_type"] = serviceInstance.GetType().Name,
                                    ["endpoint_type"] = routeAttr.HttpMethod.ToString()
                                });
                            continue;
                        }

                        var routePrefix = serviceAttr.ServicePrefix ?? "";
                        if (string.IsNullOrWhiteSpace(routePrefix))
                        {
                            if (!routePrefix.StartsWith('/'))
                                routePrefix = $"/{routePrefix}";

                            if (routePrefix.EndsWith('/'))
                                routePrefix = routePrefix.Remove(routePrefix.Length - 1, 1);
                        }

                        var routeSuffix = routeAttr.RouteUrl ?? "";
                        if (!string.IsNullOrWhiteSpace(routeSuffix))
                        {
                            routeSuffix = routeSuffix.Replace(ServiceConstants.SERVICE_UUID_PLACEHOLDER, ServiceGUID);

                            if (routeSuffix.StartsWith('/'))
                                routeSuffix = routeSuffix.Remove(0, 1);

                            if (routeSuffix.EndsWith('/'))
                                routeSuffix = routeSuffix.Remove(routeSuffix.Length - 1, 1);
                        }

                        Delegate methodDelegate = routeMethodInfo.IsStatic
                            ? routeMethodInfo.ReturnType == typeof(Task)
                                ? Delegate.CreateDelegate(typeof(Func<HttpContext, Task>), target: serviceInstance.GetType(), method: routeMethodInfo.Name)
                                : Delegate.CreateDelegate(typeof(Action<HttpContext>), target: serviceInstance.GetType(), method: routeMethodInfo.Name)
                            : routeMethodInfo.ReturnType == typeof(Task)
                                ? Delegate.CreateDelegate(typeof(Func<HttpContext, Task>), target: serviceInstance, method: routeMethodInfo.Name, ignoreCase: true)
                                : Delegate.CreateDelegate(typeof(Action<HttpContext>), target: serviceInstance, method: routeMethodInfo.Name, ignoreCase: true);

                        switch (routeAttr.HttpMethod)
                        {
                            case HttpMethodTypes.GET:
                                _ = webApp.MapGet($"{routePrefix}/{routeSuffix}", methodDelegate);
                                endpointAdded = true;
                                break;
                            case HttpMethodTypes.POST:
                                _ = webApp.MapPost($"{routePrefix}/{routeSuffix}", methodDelegate);
                                endpointAdded = true;
                                break;
                            case HttpMethodTypes.PUT:
                                _ = webApp.MapPut($"{routePrefix}/{routeSuffix}", methodDelegate);
                                endpointAdded = true;
                                break;
                            case HttpMethodTypes.DELETE:
                                _ = webApp.MapDelete($"{routePrefix}/{routeSuffix}", methodDelegate);
                                endpointAdded = true;
                                break;
                            default:
                                Logger.Log(LogLevel.Error, null, $"Unsupported dapr service endpoint type.",
                                    logParams: new JObject()
                                    {
                                        ["service_type"] = serviceInstance.GetType().Name,
                                        ["endpoint_type"] = routeAttr.HttpMethod.ToString()
                                    });
                                continue;
                        }

                        Logger.Log(LogLevel.Debug, null, $"Dapr service endpoint added successfully.",
                            logParams: new JObject()
                            {
                                ["service_type"] = serviceInstance.GetType().Name,
                                ["endpoint_type"] = routeAttr.HttpMethod.ToString()
                            });
                    }
                    catch (Exception e)
                    {
                        Logger.Log(LogLevel.Error, e, $"Failed to add dapr service endpoint.",
                            logParams: new JObject()
                            {
                                ["service_type"] = serviceInstance.GetType().Name,
                                ["endpoint_type"] = routeAttr.HttpMethod.ToString()
                            });
                    }
                }
            }

            return endpointAdded;
        }
    }
}
