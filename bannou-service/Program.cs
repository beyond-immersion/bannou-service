using System;
using System.Collections.Generic;
using System.Text.Json;
using Dapr;
using Dapr.Client;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Logging;
using BeyondImmersion.BannouService.Services;

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

            var builder = WebApplication.CreateBuilder(args);
            var webApp = builder.Build();
            if (webApp == null)
            {
                Logger.Log(LogLevel.Error, "Building web application failed.");
                return;
            }

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

            // add administrative HTTP endpoints
            SetAdminEndpoints(webApp);

            // initialize and add HTTP endpoints for enabled services


            // run webhost and accept requests, until shut down
            await Task.Run(async () => await webApp.RunAsync(ShutdownCancellationTokenSource.Token));

            // shut-down process


            DaprClient.Dispose();
        }

        private static void SetAdminEndpoints(WebApplication webApp)
        {
            webApp.MapGet($"/admin_{ServiceGUID}/shutdown", InitiateShutdown);
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

            if (!ServiceConfiguration.IsAnyServiceEnabled(Configuration))
            {
                Logger.Log(LogLevel.Error, null, "Service not configured to handle any roles / APIs.");
                return false;
            }

            return true;
        }
    }
}
