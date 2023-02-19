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
    /// The response model for service API calls to `/template/destroy`.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class TemplateDestroyResponse : ServiceResponse
    {
    }
}
