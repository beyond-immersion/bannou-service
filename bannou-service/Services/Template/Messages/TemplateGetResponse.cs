using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services.Template.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services.Template.Messages
{
    /// <summary>
    /// The response model for service API calls to `/template/create`.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class TemplateGetResponse : ServiceResponse<TemplateGetRequest>
    {
        [JsonProperty("template")]
        public TemplateModel Template { get; set; }

        public TemplateGetResponse() { }
        public TemplateGetResponse(TemplateModel template)
            => Template = template;
    }
}
