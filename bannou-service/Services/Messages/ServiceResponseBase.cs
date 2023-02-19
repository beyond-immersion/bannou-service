using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services.Messages
{
    /// <summary>
    /// The base class for service responses.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class ServiceResponse : IServiceResponse
    {
        /// <summary>
        /// Response status code.
        /// </summary>
        [JsonProperty("code", Required = Required.Always)]
        public string Code { get; set; } = "200";

        /// <summary>
        /// Response message.
        /// </summary>
        [JsonProperty("message", Required = Required.Always)]
        public string Message { get; set; } = "OK";

        public virtual bool HasRequiredProperties()
            => (this as IServiceResponse).HasRequiredProperties();
    }
}
