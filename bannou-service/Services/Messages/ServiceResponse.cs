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
        public int Code { get; set; } = 200;

        /// <summary>
        /// List of messages to return to the client.
        /// </summary>
        [JsonProperty("messages", Required = Required.Default)]
        public List<string>? Messages { get; set; }

        public ServiceResponse() { }
        public ServiceResponse(int code)
            => Code = code;
        public ServiceResponse(int code, string? message = null)
            : this(code)
            => AddMessage(message);

        public void AddMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            if (Messages == null)
                Messages = new List<string>() { message };
            else
                Messages.Add(message);
        }
    }
}
