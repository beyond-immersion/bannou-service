using System;
using System.Collections.Generic;
using System.Text.Json;
using Dapr;
using Dapr.Client;

namespace BeyondImmersion.BannouService
{
    using Services;

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
        public static string ServiceGUID { get; } = Guid.NewGuid().ToString().ToLower();


        /// <summary>
        /// Shared dapr client interface, used by all enabled internal services.
        /// </summary>
        internal static DaprClient DaprClient { get; private set; }

        /// <summary>
        /// Service component responsible for asset handling.
        /// </summary>
        internal static AssetService? AssetService { get; private set; }

        /// <summary>
        /// Service component responsible for login queue handling.
        /// </summary>
        internal static LoginService? LoginService { get; private set; }

        /// <summary>
        /// Service component responsible for login authorization handling.
        /// </summary>
        internal static AuthorizationService? AuthorizationService { get; private set; }

        /// <summary>
        /// Service component responsible for player profile handling.
        /// </summary>
        internal static ProfileService? ProfileService { get; private set; }

        /// <summary>
        /// Service component responsible for inventory handling.
        /// </summary>
        internal static InventoryService? InventoryService { get; private set; }

        /// <summary>
        /// Service component responsible for leaderboard handling.
        /// </summary>
        internal static LeaderboardService? LeaderboardService { get; private set; }

        /// <summary>
        /// Token source for initiating a clean shutdown.
        /// </summary>
        internal static CancellationTokenSource ShutdownCancellationTokenSource { get; } = new CancellationTokenSource();


        private static async Task Main(string[] args)
        {
            Configuration = ServiceConfiguration.BuildConfiguration<ServiceConfiguration>(args, "BANNOUSERVICE_") ?? new ServiceConfiguration();
            if (!ValidateConfiguration())
                return;

            Logger = LoggerFactory.Create((options) =>
                {
                    options.AddJsonConsole();
                    options.SetMinimumLevel(LogLevel.Trace);
                })
                .CreateLogger<Program>();

            var builder = WebApplication.CreateBuilder(args);

            // all clients should share the same dapr configuration settings
            DaprClient = new DaprClientBuilder()
                .UseJsonSerializationOptions(new JsonSerializerOptions {
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

            var app = builder.Build();

            // add administrative HTTP endpoints
            SetAdminEndpoints(app);

            // initialize and add HTTP endpoints for enabled services
            if (Configuration.Asset_Endpoints_Enabled) AssetService = app.AddDaprService<AssetService>();
            if (Configuration.Login_Endpoints_Enabled) LoginService = app.AddDaprService<LoginService>();
            if (Configuration.Authorization_Endpoints_Enabled) AuthorizationService = app.AddDaprService<AuthorizationService>();
            if (Configuration.Profile_Endpoints_Enabled) ProfileService = app.AddDaprService<ProfileService>();
            if (Configuration.Inventory_Endpoints_Enabled) InventoryService = app.AddDaprService<InventoryService>();
            if (Configuration.Leaderboard_Endpoints_Enabled) LeaderboardService = app.AddDaprService<LeaderboardService>();

            // run webhost and accept requests, until shut down
            await Task.Run(async () => await app.RunAsync(ShutdownCancellationTokenSource.Token));

            // shut-down process
            (AssetService as IDaprService)?.Shutdown();
            (LoginService as IDaprService)?.Shutdown();
            (AuthorizationService as IDaprService)?.Shutdown();
            (ProfileService as IDaprService)?.Shutdown();
            (InventoryService as IDaprService)?.Shutdown();
            (LeaderboardService as IDaprService)?.Shutdown();

            DaprClient.Dispose();
        }

        private static void SetAdminEndpoints(WebApplication? webApp)
        {
            if (webApp == null)
                return;

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
                Logger.Log(LogLevel.Error, "Service configuration required, even if only with default values.");
                return false;
            }

            if (!Configuration.Login_Endpoints_Enabled && !Configuration.Authorization_Endpoints_Enabled &&
                !Configuration.Inventory_Endpoints_Enabled && !Configuration.Profile_Endpoints_Enabled)
            {
                Logger.Log(LogLevel.Error, "Service not configured to handle any roles / APIs.");
                return false;
            }

            return true;
        }
    }
}
