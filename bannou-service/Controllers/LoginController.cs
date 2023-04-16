using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Service component responsible for login queue handling.
/// 
/// Can have the login service make an additional endpoint for `/login_{service_guid}` which would be unique to this service instance.
/// This would allow communication with a specific instance, in order to count down a queue position. This means the bulk of the work
/// for the queue could be done internally, with minimal traffic to the datastore only to relay metrics used to bring instances up and
/// down as demand dictates.
/// 
/// This would mean that a bad actor could spam a specific login server instance, but if that doesn't actually increase the internal
/// network traffic on each request, I'm not sure it matters.
/// </summary>
[DaprController(template: "login", serviceType: typeof(LoginService), Name = "login")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public sealed class LoginController : BaseDaprController
{
    /// <summary>
    /// Unique service ID for instance that's being forwarded to, if applicable.
    /// </summary>
    internal string ForwardServiceID { get; }

    /// <summary>
    /// Number of login requests to handle (per second).
    /// -1 indicates to let everything through.
    /// </summary>
    internal int QueueTime { get; } = -1;

    /// <summary>
    /// List of clients in the queue to login.
    /// </summary>
    internal ConcurrentQueue<string> LoginQueue = new();

    [Obsolete]
    public LoginController()
    {
        if (Program.DaprClient != null)
        {
            Dapr.Client.GetConfigurationResponse configurationResponse = Program.DaprClient.GetConfiguration("service config", new[] { "login_queue_time" }).Result;
            if (configurationResponse != null)
            {
                foreach (KeyValuePair<string, Dapr.Client.ConfigurationItem> configKvp in configurationResponse.Items)
                {
                    if (configKvp.Key == "login_queue_time")
                    {
                        // use configured processing rate for login queue, if set (per second)
                        if (int.TryParse(configKvp.Value.Value, out var configuredProcessingRate))
                            QueueTime = configuredProcessingRate;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Shared login endpoint / first point of contact for clients.
    /// Generate the queue_id, and feed the queue_url back to the client
    /// for any follow-up requests (if there's a queue).
    /// </summary>
    [DaprRoute("login")]
    public async Task Login(HttpContext context)
    {
        string? queueID = null;
        if (context.Request.Headers.TryGetValue("queue_id", out Microsoft.Extensions.Primitives.StringValues queueIDHeader))
            queueID = queueIDHeader.ToString();

        await context.Response.StartAsync();
    }
}
