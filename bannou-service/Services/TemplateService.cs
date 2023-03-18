using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services.Data;
using BeyondImmersion.BannouService.Services.Messages;
using Dapr.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// Service component responsible for template definition handling.
    /// </summary>
    [DaprService("Template Service", "template")]
    public class TemplateService : IDaprService
    {
        /// <summary>
        /// Dapr endpoint to get a specific template definition.
        /// </summary>
        [ServiceRoute(HttpMethodTypes.GET, "/{templateID}")]
        public async Task Get(HttpContext context)
        {
            var templateID = (string?)context.GetRouteValue("templateID");
            if (string.IsNullOrWhiteSpace(templateID))
            {
                await context.SendResponseAsync(ResponseCodes.BadRequest, $"{nameof(templateID)} cannot be null or empty");
                return;
            }

            await context.SendResponseAsync(ResponseCodes.NotFound);
        }

        /// <summary>
        /// Dapr endpoint to list template definitions.
        /// </summary>
        [ServiceRoute(HttpMethodTypes.GET, "/list")]
        public async Task List(ServiceMessageContext<TemplateListRequest, TemplateListResponse> context)
        {
            Dapr.StateEntry<string> stateEntry = await Program.DaprClient.GetStateEntryAsync<string>("template-datastore", "template-list");

            // 200/Ok/Empty results list, if no templates in datastore
            if (string.IsNullOrWhiteSpace(stateEntry.Value))
                return;

            try
            {
                foreach(JToken templateToken in JArray.Parse(stateEntry.Value))
                {
                    try
                    {
                        Template? retrievedTemplate = templateToken.ToObject<Template>();
                        if (retrievedTemplate == null)
                            throw new NullReferenceException("Template null");

                        context.Response.Templates.Add(retrievedTemplate);
                    }
                    catch (Exception e)
                    {
                        Program.Logger.Log(LogLevel.Error, e, "An error occurred parsing template to construct list.");
                    }
                }
            }
            catch(Exception e)
            {
                Program.Logger.Log(LogLevel.Error, e, "An error occurred parsing templates to construct list.");
                context.Response.SetResponse(ResponseCodes.ServerError);
                context.Response.Templates.Clear();
                return;
            }
        }

        /// <summary>
        /// Dapr endpoint to create new template definitions.
        /// </summary>
        [ServiceRoute("/create")]
        public async Task Create(ServiceMessageContext<TemplateCreateRequest, ServiceResponse> context)
        {
            if (context.Request.Templates.Length == 0)
            {
                await context.SetAndSendResponseAsync(ResponseCodes.BadRequest, "Templates cannot be empty.");
                return;
            }

            var templateKeys = context.Request.Templates.Select(t => "template_" + t.ID).ToList();
            var existingTemplates = await Program.DaprClient.GetBulkStateAsync(
                storeName: "template-store",
                keys: templateKeys,
                parallelism: -1,
                metadata: null,
                cancellationToken: Program.ShutdownCancellationTokenSource.Token);

            if (existingTemplates.Any())
            {
                context.Response.SetResponse(ResponseCodes.BadRequest, "Some of the given templates already exist.");
                return;
            }

            var createItems = new List<StateTransactionRequest>();
            foreach (var newTemplate in context.Request.Templates)
                createItems.Add(new StateTransactionRequest(
                    key: "template_" + newTemplate.ID,
                    value: Encoding.UTF8.GetBytes(JObject.FromObject(newTemplate).ToString(Formatting.None)),
                    operationType: StateOperationType.Upsert,
                    etag: null,
                    metadata: null,
                    options: new StateOptions()
                    {
                        Concurrency = ConcurrencyMode.FirstWrite,
                        Consistency = ConsistencyMode.Eventual
                    }));

            await Program.DaprClient.ExecuteStateTransactionAsync(
                storeName: "template-store",
                operations: createItems,
                cancellationToken: Program.ShutdownCancellationTokenSource.Token);
        }

        /// <summary>
        /// Dapr endpoint to update an existing template definition.
        /// </summary>
        [ServiceRoute("/update")]
        public async Task Update(ServiceMessageContext<TemplateUpdateRequest, ServiceResponse> context)
        {
            if (context.Request.Templates.Length == 0)
            {
                context.Response.SetResponse(ResponseCodes.BadRequest);
                return;
            }

            var templateKeys = context.Request.Templates.Select(t => "template_" + t.ID).ToList();
            var existingTemplates = await Program.DaprClient.GetBulkStateAsync(
                storeName: "template-store",
                keys: templateKeys,
                parallelism: -1,
                metadata: null,
                cancellationToken: Program.ShutdownCancellationTokenSource.Token);

            if (existingTemplates.Count != templateKeys.Count)
            {
                context.Response.SetResponse(ResponseCodes.NotFound);
                return;
            }

            var updateItems = new List<StateTransactionRequest>();
            foreach (var updatedTemplate in context.Request.Templates)
                updateItems.Add(new StateTransactionRequest(
                    key: "template_" + updatedTemplate.ID,
                    value: Encoding.UTF8.GetBytes(JObject.FromObject(updatedTemplate).ToString(Formatting.None)),
                    operationType: StateOperationType.Upsert,
                    etag: null,
                    metadata: null,
                    options: new StateOptions()
                    {
                        Concurrency = ConcurrencyMode.FirstWrite,
                        Consistency = ConsistencyMode.Eventual
                    }));

            await Program.DaprClient.ExecuteStateTransactionAsync(
                storeName: "template-store",
                operations: updateItems,
                cancellationToken: Program.ShutdownCancellationTokenSource.Token);
        }

        /// <summary>
        /// Dapr endpoint to destroy an existing template definition.
        /// </summary>
        [ServiceRoute("/destroy")]
        public async Task Destroy(ServiceMessageContext<TemplateDestroyRequest, ServiceResponse> context)
        {
            if (context.Request.Templates.Length == 0)
            {
                context.Response.SetResponse(ResponseCodes.BadRequest);
                return;
            }

            var templateKeys = context.Request.Templates.Select(t => "template_" + t.ID).ToList();
            var existingTemplates = await Program.DaprClient.GetBulkStateAsync(
                storeName: "template-store",
                keys: templateKeys,
                parallelism: -1,
                metadata: null,
                cancellationToken: Program.ShutdownCancellationTokenSource.Token);

            if (existingTemplates.Count != templateKeys.Count)
            {
                context.Response.SetResponse(ResponseCodes.NotFound);
                return;
            }

            var deleteItems = new List<StateTransactionRequest>();
            foreach (var deletedTemplate in context.Request.Templates)
                deleteItems.Add(new StateTransactionRequest(
                    key: "template_" + deletedTemplate.ID,
                    value: Encoding.UTF8.GetBytes(JObject.FromObject(deletedTemplate).ToString(Formatting.None)),
                    operationType: StateOperationType.Delete,
                    etag: null,
                    metadata: null,
                    options: new StateOptions()
                    {
                        Concurrency = ConcurrencyMode.FirstWrite,
                        Consistency = ConsistencyMode.Eventual
                    }));

            await Program.DaprClient.ExecuteStateTransactionAsync(
                storeName: "template-store",
                operations: deleteItems,
                cancellationToken: Program.ShutdownCancellationTokenSource.Token);
        }

        /// <summary>
        /// Dapr endpoint to destroy an existing template definition.
        /// </summary>
        [ServiceRoute("/destroy-by-key")]
        public async Task DestroyByKey(ServiceMessageContext<TemplateDestroyByKeyRequest, ServiceResponse> context)
        {
            if (context.Request.IDs.Length == 0)
            {
                context.Response.SetResponse(ResponseCodes.BadRequest);
                return;
            }

            var deleteItems = new List<BulkDeleteStateItem>();
            foreach (var newTemplate in context.Request.IDs)
                deleteItems.Add(new BulkDeleteStateItem(
                    key: "template_" + newTemplate,
                    etag: null,
                    metadata: null,
                    stateOptions: new StateOptions()
                    {
                        Concurrency = ConcurrencyMode.FirstWrite,
                        Consistency = ConsistencyMode.Eventual
                    }));

            if (deleteItems.Count == 0)
                return;

            await Program.DaprClient.DeleteBulkStateAsync(
                storeName: "template-store",
                items: deleteItems,
                cancellationToken: Program.ShutdownCancellationTokenSource.Token);
        }
    }
}
