using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;

namespace BeyondImmersion.BannouService.Services.Messages
{
    /// <summary>
    /// The base class for service responses.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn, ItemNullValueHandling = NullValueHandling.Ignore)]
    public class ServiceResponse : IServiceResponse
    {
        /// <summary>
        /// Response status code.
        /// </summary>
        [JsonProperty("code", Required = Required.Always)]
        public int Code { get; private set; } = 200;

        /// <summary>
        /// List of messages to return to the client.
        /// </summary>
        [JsonProperty("messages", Required = Required.Default)]
        public List<string> Messages { get; private set; } = new List<string>();

        public ServiceResponse() { }

        public ServiceResponse(int code, params string?[]? additionalMessages)
        {
            Code = code;

            if (additionalMessages != null && additionalMessages.Length > 0)
                foreach (var message in additionalMessages)
                    if (!string.IsNullOrWhiteSpace(message))
                        Messages.Add(message);
        }

        public ServiceResponse(ResponseCodes responseCode, params string?[]? additionalMessages)
            => SetResponse(responseCode, additionalMessages);

        /// <summary>
        /// Whether this response has data, or can be discarded.
        /// </summary>
        public virtual bool HasData()
        {
            if (Code != 200)
                return true;

            return JObject.FromObject(this).Count > 2;
        }

        /// <summary>
        /// Set fixed service response, based on a given response code.
        /// </summary>
        public IServiceResponse SetResponse(ResponseCodes responseCode, params string?[]? additionalMessages)
        {
            switch (responseCode)
            {
                case ResponseCodes.Ok:
                    Code = 200;
                    break;
                case ResponseCodes.Accepted:
                    Code = 202;
                    break;
                case ResponseCodes.BadRequest:
                    Code = 400;
                    Messages.Add("Bad request");
                    break;
                case ResponseCodes.Unauthorized:
                    Code = 403;
                    Messages.Add("Unauthorized request");
                    break;
                case ResponseCodes.NotFound:
                    Code = 403;
                    Messages.Add("Resource not found");
                    break;
                case ResponseCodes.ServerBusy:
                    Code = 503;
                    Messages.Add("Server busy");
                    break;
                case ResponseCodes.ServerError:
                default:
                    Code = 500;
                    Messages.Add("Server error");
                    break;
            }

            if (additionalMessages != null && additionalMessages.Length > 0)
                foreach (var message in additionalMessages)
                    if (!string.IsNullOrWhiteSpace(message))
                        Messages.Add(message);

            return this;
        }
    }
}
