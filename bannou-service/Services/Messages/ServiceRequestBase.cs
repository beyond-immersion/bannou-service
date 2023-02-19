using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services.Messages
{
    /// <summary>
    /// The basic service message payload model.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public abstract class ServiceRequestBase : IServiceRequest
    {
        /// <summary>
        /// Message ID, for logging/tracing through the system.
        /// </summary>
        [JsonProperty("request_id", Required = Required.Default)]
        public virtual string RequestID { get; } = Guid.NewGuid().ToString().ToLower();
    }
}
