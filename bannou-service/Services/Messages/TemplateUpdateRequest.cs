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
    /// The request model for service API calls to `/template/update`.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class TemplateUpdateRequest : ServiceRequestBase
    {
        /// <summary>
        /// Template data to be updated.
        /// </summary>
        [JsonProperty("template", Required = Required.Always)]
        public Template Template { get; }

        private TemplateUpdateRequest() { }
        public TemplateUpdateRequest(Template template)
            => Template = template ?? throw new ArgumentNullException(nameof(template));
    }
}
