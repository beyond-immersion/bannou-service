using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// The interface all message payload models to service endpoints should implement.
    /// </summary>
    public interface IServiceRequest
    {
        /// <summary>
        /// Optional ID for any requests through the system.
        /// 
        /// Never required, and only used for logging / debugging.
        /// </summary>
        string RequestID { get; }
    }
}
