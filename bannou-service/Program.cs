using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using System.Runtime.CompilerServices;

[assembly: ApiController]
[assembly: InternalsVisibleTo("unit-tests")]
namespace BeyondImmersion.BannouService;

public static class Program
{
    /// <summary>
    /// Service configuration- pulled from Config.json, ENVs, and command switches.
    /// </summary>
    internal static ServiceConfiguration Configuration { get; set; }

    /// <summary>
    /// Service logger.
    /// </summary>
    internal static ILogger Logger { get; set; }

    /// <summary>
    /// Internal service GUID- largely used for administrative network commands.
    /// Randomly generated on service startup.
    /// </summary>
    internal static string ServiceGUID { get; private set; }

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

        Configuration = IServiceConfiguration.BuildConfiguration<ServiceConfiguration>(args);

        if (!Validators.RunAll())
        {
            Logger.Log(LogLevel.Error, null, "Validation error- service start aborted.");
            return;
        }

        ServiceGUID = Configuration.ForceServiceID ?? Guid.NewGuid().ToString().ToLower();
        WebApplicationBuilder? webAppBuilder = WebApplication.CreateBuilder(args);
        if (webAppBuilder == null)
        {
            Logger.Log(LogLevel.Error, null, "Failed to create web application builder- exiting application.");
            return;
        }

        webAppBuilder.Services.AddAuthentication();
        webAppBuilder.Services.AddControllersWithViews();
        webAppBuilder.Services.AddDaprClient();

        WebApplication webApp = webAppBuilder.Build();

        DaprClient = new DaprClientBuilder()
            .UseJsonSerializationOptions(IServiceConfiguration.DaprSerializerConfig)
            .Build();

        if (!await DaprClient.CheckHealthAsync(ShutdownCancellationTokenSource.Token))
        {
            Logger.Log(LogLevel.Error, null, "Dapr sidecar unhealthy/not found- exiting application.");
            return;
        }

        try
        {
            webApp
                .UseRouting()
                .UseAuthorization()
                .UseEndpoints((b) =>
                {
                    b.MapNonServiceControllers();
                    b.MapDaprServiceControllers();
                });

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

    public static void ConfigurationServices(IServiceCollection services)
    {
        services.AddControllersWithViews();
    }

    /// <summary>
    /// Will stop the webhost and initiate a service shutdown.
    /// </summary>
    public static void InitiateShutdown() => ShutdownCancellationTokenSource.Cancel();
}
