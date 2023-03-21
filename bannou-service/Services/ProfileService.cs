using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// Service component responsible for player profile handling.
    /// </summary>
    [DaprService("profile")]
    public class ProfileService : Controller, IDaprService
    {
        /// <summary>
        /// Create new player profile.
        /// </summary>
        [ServiceRoute("/create")]
        public async Task CreateProfile(HttpContext context)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// Update existing player profile.
        /// </summary>
        [ServiceRoute("/update")]
        public async Task UpdateProfile(HttpContext context)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// Destroy existing player profile.
        /// </summary>
        [ServiceRoute("/destroy")]
        public async Task DestroyProfile(HttpContext context)
        {
            await Task.CompletedTask;
        }
    }
}
