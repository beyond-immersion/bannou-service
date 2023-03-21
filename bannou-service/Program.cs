using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Logging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Services.Messages;
using Dapr.Client;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

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

        private static async Task Main(string[] args)
        {
            Logger = ServiceLogging.CreateLogger();

            Logger.Log(LogLevel.Debug, null, "Service starting.");

            Configuration = ServiceConfiguration.BuildConfiguration(args, "BANNOU_");
            if (!ValidateConfiguration())
                return;

            ServiceGUID = Configuration.ForceServiceID ?? Guid.NewGuid().ToString().ToLower();

            WebApplication? webApp = WebApplication.CreateBuilder(args)?.Build();
            if (webApp == null)
            {
                Logger.Log(LogLevel.Error, null, "Building web application failed- exiting application.");
                return;
            }

            DaprClient = new DaprClientBuilder()
                .UseJsonSerializationOptions(ServiceConfiguration.DaprSerializerConfig)
                .Build();

            if (!await DaprClient.CheckHealthAsync(ShutdownCancellationTokenSource.Token))
            {
                Logger.Log(LogLevel.Error, null, "Dapr sidecar unhealthy/not found- exiting application.");
                return;
            }

            try
            {
                SetAdminEndpoints(webApp);
                var unused = AddDaprServiceEndpoints(webApp);

                Logger.Log(LogLevel.Debug, null, "Service startup complete- webhost starting.");
                {
                    // blocks until webhost dies / server shutdown command received
                    await Task.Run(async () => await webApp.RunAsync(ShutdownCancellationTokenSource.Token), ShutdownCancellationTokenSource.Token);
                }

                Logger.Log(LogLevel.Debug, null, "Webhost stopped- starting controlled service shutdown.");
            }
            catch (Exception e)
            {
                Logger.Log(LogLevel.Error, e, "A critical error has occurred- starting service shutdown.");
            }
            finally
            {
                await webApp.DisposeAsync();
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

            foreach ((Type, DaprServiceAttribute) serviceClassData in GetDaprServiceTypes(enabledOnly: true))
            {
                Type serviceType = serviceClassData.Item1;

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
        /// Gets the full list of all dapr service classes (with associated attribute) in loaded assemblies.
        /// </summary>
        private static (Type, DaprServiceAttribute)[] GetDaprServiceTypes(bool enabledOnly = false)
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

                if (enabledOnly && !Configuration.IsServiceEnabled(serviceType))
                    continue;

                string servicePrefix = ((IDaprService)serviceType).GetServiceName().ToLower();
                if (!serviceLookup.ContainsKey(servicePrefix) || serviceClass.GetType().Assembly != Assembly.GetExecutingAssembly())
                    serviceLookup[servicePrefix] = serviceClass;
            }

            return serviceLookup.Values.ToArray();
        }

        /// <summary>
        /// Binds HTTP endpoints for all registered dapr services.
        /// </summary>
        private static bool AddDaprServiceEndpoints(WebApplication webApp)
        {
            var routeAdded = false;
            foreach (var serviceClass in GetDaprServiceTypes(enabledOnly: true))
            {
                string controllerTemplate = serviceClass.Item2.Template ?? ((IDaprService)serviceClass.Item1).GetServiceName();
                if (Configuration.IsServiceEnabled(serviceClass.Item1))
                {
                    webApp.MapControllerRoute(controllerTemplate, controllerTemplate + "/{action=Index}/{id?}");
                    routeAdded = true;
                }
            }

            return routeAdded;
        }
    }
}
