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
        [ServiceRoute("/create")]
        public async Task Create(ServiceRequestContext<TemplateCreateRequest, TemplateCreateResponse> context)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// Dapr endpoint to update an existing template definition.
        /// </summary>
        [ServiceRoute("/update")]
        public async Task Update(ServiceRequestContext<TemplateUpdateRequest, TemplateUpdateResponse> context)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// Dapr endpoint to destroy an existing template definition.
        /// </summary>
        [ServiceRoute("/destroy")]
        public async Task Destroy(ServiceRequestContext<TemplateDestroyRequest, TemplateDestroyResponse> context)
        {
            await Task.CompletedTask;
        }
    }
}
