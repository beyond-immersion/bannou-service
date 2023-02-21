using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services.Data;
using BeyondImmersion.BannouService.Services.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        /// Used when emulating the dapr datastores.
        /// </summary>
        private Dictionary<string, string> EmulatedTemplateDatastore { get; }
            = new Dictionary<string, string>() { ["test"] = JObject.FromObject(new Template("something", "test template")).ToString(Formatting.None) };

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

            if (Program.Configuration.EmulateDapr && EmulatedTemplateDatastore.TryGetValue(templateID, out var template))
            {
                Program.Logger.Log(LogLevel.Debug, null, "Emulating dapr- template found.");

                Template? retrievedTemplate = null;
                try
                {
                    retrievedTemplate = JObject.Parse(template).ToObject<Template>();
                    if (retrievedTemplate == null)
                        throw new NullReferenceException("Parsed template is null.");
                }
                catch (Exception e)
                {
                    Program.Logger.Log(LogLevel.Error, e, "Could not parse template to object model.");
                    await context.SendResponseAsync(ResponseCodes.ServerError, $"Could not parse stored template data to object model.");
                    return;
                }
                var msgResponse = new TemplateGetResponse() { Template = retrievedTemplate };
                await context.SendResponseAsync(msgResponse);
                return;
            }

            await context.SendResponseAsync(ResponseCodes.NotFound);
        }

        /// <summary>
        /// Dapr endpoint to list template definitions.
        /// </summary>
        [ServiceRoute(HttpMethodTypes.GET, "/list")]
        public async Task List(ServiceRequestContext<TemplateListRequest, TemplateListResponse> context)
        {
            await Task.CompletedTask;

            if (Program.Configuration.EmulateDapr)
            {

            }

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
