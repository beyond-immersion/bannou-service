using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services.Messages
{
    /// <summary>
    /// The response model for service API calls to `/template/create`.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class TemplateGetResponse : ServiceResponse<TemplateGetRequest>
    {
        [JsonProperty("template")]
        public Template Template { get; set; }

        public TemplateGetResponse() { }
        public TemplateGetResponse(Template template)
            => Template = template;
    }
}
