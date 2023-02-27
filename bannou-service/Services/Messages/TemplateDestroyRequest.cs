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
    /// The request model for service API calls to `/template/destroy`.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class TemplateDestroyRequest : ServiceRequest
    {
        /// <summary>
        /// Templates to destroy.
        /// </summary>
        [JsonProperty("templates", Required = Required.Always)]
        public Template[] Templates { get; }

        private TemplateDestroyRequest() { }
        public TemplateDestroyRequest(Template[] templates)
            => templates = templates ?? throw new ArgumentNullException(nameof(templates));
    }
}
