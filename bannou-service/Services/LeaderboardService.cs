using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// 
    /// </summary>
    public class LeaderboardService : IDaprService
    {
        /// <summary>
        /// Unique service id for this instance.
        /// </summary>
        public string ServiceID { get; } = $"LEADERBOARD_{Program.ServiceGUID}";

        void IDaprService.AddEndpointsToWebApp(WebApplication? webApp)
        {
            if (webApp == null)
                return;

            webApp.MapGet("/leaderboard/list", ListLeaderboards);
            webApp.MapGet("/leaderboard/create", CreateLeaderboard);
            webApp.MapGet("/leaderboard/update", UpdateLeaderboard);
            webApp.MapGet("/leaderboard/destroy", DestroyLeaderboard);
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task ListLeaderboards(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task CreateLeaderboard(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task UpdateLeaderboard(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task DestroyLeaderboard(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }
    }
}
