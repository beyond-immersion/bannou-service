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
    /// The request model for service API calls to `/template/destroy`.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class TemplateDestroyRequest : ServiceRequest
    {
        /// <summary>
        /// Templates to destroy.
        /// </summary>
        [JsonProperty("templates", Required = Required.Always)]
        public TemplateModel[] Templates { get; }

        private TemplateDestroyRequest() { }
        public TemplateDestroyRequest(TemplateModel[] templates)
            => templates = templates ?? throw new ArgumentNullException(nameof(templates));
    }
}
