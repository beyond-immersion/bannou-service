using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services.Messages
{
    /// <summary>
    /// An empty service message payload model.
    /// Contains no required properties.
    /// 
    /// Use this type when specifying a <see cref="ServiceRequestContext{T, S}"/>
    /// to gain that additional functionality in API method
    /// handlers, but the "Request" property will be null/skipped.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class EmptyServiceRequest : ServiceRequestBase
    {
        public EmptyServiceRequest() { }
    }
}
