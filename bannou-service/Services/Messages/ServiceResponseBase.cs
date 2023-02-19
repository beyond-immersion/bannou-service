using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services.Messages
{
    /// <summary>
    /// The interface all response models from service endpoints should implement.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public abstract class ServiceResponseBase : IServiceResponse
    {
        /// <summary>
        /// Response status code.
        /// </summary>
        [JsonProperty("code", Required = Required.Always)]
        public string Code { get; } = "200";

        /// <summary>
        /// Response message.
        /// </summary>
        [JsonProperty("message", Required = Required.Always)]
        public string Message { get; } = "OK";
    }
}
