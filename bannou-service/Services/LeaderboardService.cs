using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Application;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// Service component responsible for leaderboard handling.
    /// </summary>
    [DaprService("Leaderboard Service", "leaderboard")]
    public class LeaderboardService : IDaprService
    {
        /// <summary>
        /// 
        /// </summary>
        [ServiceRoute("/list")]
        public async Task ListLeaderboards(HttpContext requestContext)
        {
            requestContext.Response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            requestContext.Response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        [ServiceRoute("/create")]
        public async Task CreateLeaderboard(HttpContext requestContext)
        {
            requestContext.Response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            requestContext.Response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        [ServiceRoute("/update")]
        public async Task UpdateLeaderboard(HttpContext requestContext)
        {
            requestContext.Response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            requestContext.Response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        [ServiceRoute("/destroy")]
        public async Task DestroyLeaderboard(HttpContext requestContext)
        {
            requestContext.Response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            requestContext.Response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }
    }
}
