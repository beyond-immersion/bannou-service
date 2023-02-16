using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// 
    /// 
    /// Can have the login service make an additional endpoint for `/login_{service_guid}` which would be unique to this service instance.
    /// This would allow communication with a specific instance, in order to count down a queue position. This means the bulk of the work
    /// for the queue could be done internally, with minimal traffic to the datastore only to relay metrics used to bring instances up and
    /// down as demand dictates.
    /// 
    /// This would mean that a bad actor could spam a specific login server instance, but if that doesn't actually increase the internal
    /// network traffic on each request, I'm not sure it matters.
    /// </summary>
    public class LoginService : IDaprService
    {
        /// <summary>
        /// Unique service id for this instance.
        /// </summary>
        public string ServiceID { get; } = $"LOGIN_{Program.ServiceGUID}";

        /// <summary>
        /// Unique service ID for instance that's being forwarded to, if applicable.
        /// </summary>
        private string sForwardServiceID { get; }

        /// <summary>
        /// Number of login requests to handle (per second).
        /// -1 indicates to let everything through.
        /// </summary>
        private int sQueueProcessingRate { get; } = -1;

        /// <summary>
        /// List of clients in the queue to login.
        /// </summary>
        private ConcurrentQueue<string> sLoginQueue = new ConcurrentQueue<string>();

        public LoginService()
        {
            if (Program.DaprClient != null)
            {
#pragma warning disable CS0618 // alpha feature - not obsolete
                var configurationResponse = Program.DaprClient.GetConfiguration("service config", new[] { "login_queue_processing_rate" }).Result;
                if (configurationResponse != null)
                {
                    foreach (var configKvp in configurationResponse.Items)
                    {
                        if (configKvp.Key == "login_queue_processing_rate")
                        {
                            // use configured processing rate for login queue, if set (per second)
                            if (int.TryParse(configKvp.Value.Value, out var configuredProcessingRate))
                                sQueueProcessingRate = configuredProcessingRate;
                        }
                    }
                }
#pragma warning restore CS0618
            }
        }

        void IDaprService.AddEndpointsToWebApp(WebApplication? webApp)
        {
            if (webApp == null)
                return;

            webApp.MapGet("/login", Login);
            webApp.MapGet($"/login/{ServiceID}", LoginDirect);
            webApp.MapGet($"/login/service_id", GetServiceID);
        }

        /// <summary>
        /// Return ID of login service instance.
        /// </summary>
        private async Task GetServiceID(HttpContext requestContext)
        {
            requestContext.Response.StatusCode = 200;
            await requestContext.Response.WriteAsync(sForwardServiceID ?? ServiceID);
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// Shared login endpoint / first point of contact for clients.
        /// Generate the queue_id, and feed the queue_url back to the client
        /// for any follow-up requests (if there's a queue).
        /// </summary>
        private async Task Login(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;

            var refreshRate = 15;
            var nextTickTime = refreshRate + DateTimeOffset.Now.ToUnixTimeMilliseconds() / 1000d;
            string queueURL = $"{requestContext.Request.Path}/{sForwardServiceID ?? ServiceID}";
            var queuePosition = 0;

            // if the queue id is found in headers, use it
            string queueID = null;
            if (requestContext.Request.Headers.TryGetValue("queue_id", out var queueIDHeader))
                queueID = queueIDHeader.ToString();

            // - otherwise generate a new one
            if (string.IsNullOrWhiteSpace(queueID))
                queueID = Guid.NewGuid().ToString().ToLower();

            // add headers to response
            response.Headers.Add("queue_url", queueURL);
            response.Headers.Add("queue_id", queueID);
            response.Headers.Add("queue_position", queuePosition.ToString());
            response.Headers.Add("next_tick", nextTickTime.ToString());

            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// Unique login endpoint- track client's position in queue, ensuring that requests to the
        /// datastore happen at fixed intervals and no more frequently, even if a client is impatient.
        /// </summary>
        private async Task LoginDirect(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;

            if (!requestContext.Request.Headers.TryGetValue("queue_id", out var queueID))
            {
                response.StatusCode = 400;
                return;
            }
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }
    }
}
