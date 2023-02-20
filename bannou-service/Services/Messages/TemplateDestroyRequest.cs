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
        /// ID of template to destroy.
        /// </summary>
        [JsonProperty("id", Required = Required.Always)]
        public string ID { get; }

        private TemplateDestroyRequest() { }
        public TemplateDestroyRequest(string templateID)
            => ID = templateID ?? throw new ArgumentNullException(nameof(templateID));
    }
}
