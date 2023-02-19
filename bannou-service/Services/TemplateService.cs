using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services.Messages;
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
        [ServiceRoute("/create")]
        public async Task Create(ServiceRequestContext<TemplateCreateRequest, TemplateCreateResponse> contextData)
        {
            contextData.HttpContext.Response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            contextData.HttpContext.Response.StatusCode = 200;
            await contextData.HttpContext.Response.StartAsync();
        }

        /// <summary>
        /// Dapr endpoint to update an existing template definition.
        /// </summary>
        [ServiceRoute("/update")]
        public async Task Update(HttpContext requestContext)
        {
            requestContext.Response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            requestContext.Response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// Dapr endpoint to destroy an existing template definition.
        /// </summary>
        [ServiceRoute("/destroy")]
        public async Task Destroy(HttpContext requestContext)
        {
            requestContext.Response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            requestContext.Response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }
    }
}
