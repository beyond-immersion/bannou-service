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
        int Code { get; }

        /// <summary>
        /// List of messages to return to the client.
        /// </summary>
        List<string>? Messages { get; }
    }
}
