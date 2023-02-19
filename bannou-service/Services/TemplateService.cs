using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services.Messages;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// Service component responsible for template definition handling.
    /// </summary>
    [DaprService("Template Service", "template")]
    public class TemplateService : IDaprService
    {
        /// <summary>
        /// Dapr endpoint to create a new template definition.
        /// </summary>
        [ServiceRoute(HttpMethodTypes.GET, "/create")]
        public async Task Create_GET(ServiceRequestContext<TemplateCreateRequest, TemplateCreateResponse> context)
        {
            await Task.CompletedTask;

            // using the "ServiceRequestContext" parameter-
            // just setting the data will have it included in JSON response

            context.Response.Data = new JObject() { ["content"] = "something" };
        }

        /// <summary>
        /// Dapr endpoint to update an existing template definition.
        /// </summary>
        [ServiceRoute(HttpMethodTypes.POST, "/create")]
        public async Task Create_POST(HttpContext context)
        {
            // if using an "HttpContext" instead,
            // you must write and send the response yourself
            // if wanting to include any data

            var msgResponse = new TemplateCreateResponse() { Data = new JObject() { ["content"] = "something" } };
            await context.SendResponseAsync(msgResponse);
        }

        /// <summary>
        /// Dapr endpoint to update an existing template definition.
        /// </summary>
        [ServiceRoute(HttpMethodTypes.GET, "/update")]
        public async Task Update_GET(ServiceRequestContext<TemplateUpdateRequest, TemplateUpdateResponse> context)
        {
            // using the "ServiceRequestContext" parameter,
            // you could also write and send the response yourself (if it's needed)

            var msgResponse = new TemplateUpdateResponse() { };
            await context.SendResponseAsync(msgResponse);
        }

        /// <summary>
        /// Dapr endpoint to update an existing template definition.
        /// </summary>
        [ServiceRoute(HttpMethodTypes.POST, "/update")]
        public async Task Update_POST(HttpContext context)
        {
            await Task.CompletedTask;

            // if not wanting to include any data
            // you can just let these go to return a 200/OK
            // or throw an exception to return 500/error message
        }

        /// <summary>
        /// Dapr endpoint to destroy an existing template definition.
        /// </summary>
        [ServiceRoute("/destroy")]
        public async Task Destroy()
        {
            await Task.CompletedTask;

            // when parameterless, will just return 200/OK, as a generic JSON response-
            // unless an exception is thrown, in which case it'll be 500 + error message
        }
    }
}
