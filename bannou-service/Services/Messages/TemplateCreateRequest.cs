using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services.Data;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services.Messages
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
        public Template[] Templates { get; }

        private TemplateCreateRequest() { }
        public TemplateCreateRequest(params Template[] templates)
            => Templates = templates ?? throw new ArgumentNullException(nameof(templates));
    }
}
