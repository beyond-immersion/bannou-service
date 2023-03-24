using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services.Template.Data;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services.Template.Messages
{
    /// <summary>
    /// The request model for service API calls to `/template/create`.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class TemplateCreateRequest : ServiceRequest
    {
        /// <summary>
        /// New templates to create.
        /// </summary>
        [JsonProperty("templates", Required = Required.Always)]
        public TemplateModel[] Templates { get; }

        private TemplateCreateRequest() { }
        public TemplateCreateRequest(params TemplateModel[] templates)
            => Templates = templates ?? throw new ArgumentNullException(nameof(templates));
    }
}
