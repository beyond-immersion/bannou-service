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
    public interface IServiceResponse
    {
        /// <summary>
        /// Response code (200|400|403|500).
        /// </summary>
        string Code { get; }

        /// <summary>
        /// Response message.
        /// </summary>
        string Message { get; }

        /// <summary>
        /// Whether the response is valid.
        /// By default, only checks if the code and message
        /// are not null/empty, but this can be overridden.
        /// </summary>
        bool HasRequiredProperties()
        {
            return !string.IsNullOrWhiteSpace(Code) && !string.IsNullOrWhiteSpace(Message);
        }
    }
}
