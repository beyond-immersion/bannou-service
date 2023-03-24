using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// Service component responsible for login authorization handling.
    /// </summary>
    [DaprService("authorization")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    public class AuthorizationService : IDaprService
    {
        /// <summary>
        /// Shared endpoint to try authorizing a client connection.
        /// Will hand back a specific instance endpoint to use, for
        /// follow-up requests / exchanges.
        /// </summary>
        [ServiceRoute("/")]
        public async Task Authorize(HttpContext context)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// Instance endpoint, for any follow-up exchanges beyond the
        /// initial handshake, for authorizing a client connection.
        /// </summary>
        [ServiceRoute($"/{ServiceConstants.SERVICE_UUID_PLACEHOLDER}")]
        public async Task AuthorizeDirect(HttpContext context)
        {
            await Task.CompletedTask;
        }
    }
}
