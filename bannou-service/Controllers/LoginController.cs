using BeyondImmersion.BannouService.Controllers.Messages;
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
    internal int QueueProcessingRate { get; } = -1;

    /// <summary>
    /// List of clients in the queue to login.
    /// </summary>
    internal ConcurrentQueue<string> LoginQueue = new();

    public LoginController()
    {
        if (Program.DaprClient != null)
        {
            Dapr.Client.GetConfigurationResponse configurationResponse = Program.DaprClient.GetConfiguration("service config", new[] { "login_queue_processing_rate" }).Result;
            if (configurationResponse != null)
            {
                foreach (KeyValuePair<string, Dapr.Client.ConfigurationItem> configKvp in configurationResponse.Items)
                {
                    if (configKvp.Key == "login_queue_processing_rate")
                    {
                        // use configured processing rate for login queue, if set (per second)
                        if (int.TryParse(configKvp.Value.Value, out var configuredProcessingRate))
                            QueueProcessingRate = configuredProcessingRate;
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
    [DaprRoute("")]
    public async Task Login(HttpContext context)
    {
        HttpResponse response = context.Response;
        response.ContentType = MediaTypeNames.Text.Plain;
        response.StatusCode = 200;

        var refreshRate = 15;
        var nextTickTime = refreshRate + DateTimeOffset.Now.ToUnixTimeMilliseconds() / 1000d;
        var queueURL = $"{context.Request.Path}/{ForwardServiceID ?? Program.ServiceGUID}";
        var queuePosition = 0;

        // if the queue id is found in headers, use it
        string? queueID = null;
        if (context.Request.Headers.TryGetValue("queue_id", out Microsoft.Extensions.Primitives.StringValues queueIDHeader))
            queueID = queueIDHeader.ToString();

        // - otherwise generate a new one
        if (string.IsNullOrWhiteSpace(queueID))
            queueID = Guid.NewGuid().ToString().ToLower();

        // add headers to response
        response.Headers.Add("queue_url", queueURL);
        response.Headers.Add("queue_id", queueID);
        response.Headers.Add("queue_position", queuePosition.ToString());
        response.Headers.Add("next_tick", nextTickTime.ToString());

        await context.Response.StartAsync();
    }

    /// <summary>
    /// Unique login endpoint- track client's position in queue, ensuring that requests to the
    /// datastore happen at fixed intervals and no more frequently, even if a client is impatient.
    /// </summary>
    [DaprRoute($"{ServiceConstants.SERVICE_UUID_PLACEHOLDER}")]
    public async Task LoginDirect(HttpContext context)
    {
        await Task.CompletedTask;
    }
}
