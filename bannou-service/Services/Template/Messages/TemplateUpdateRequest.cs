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
    /// The request model for service API calls to `/template/update`.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class TemplateUpdateRequest : ServiceRequest
    {
        /// <summary>
        /// Template data to be updated.
        /// </summary>
        [JsonProperty("templates", Required = Required.Always)]
        public TemplateModel[] Templates { get; }

        private TemplateUpdateRequest() { }
        public TemplateUpdateRequest(TemplateModel[] templates)
            => Templates = templates ?? throw new ArgumentNullException(nameof(templates));
    }
}
