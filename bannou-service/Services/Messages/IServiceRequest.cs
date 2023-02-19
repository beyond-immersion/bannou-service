using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services.Messages
{
    /// <summary>
    /// The interface all message payload models to service endpoints should implement.
    /// </summary>
    public interface IServiceRequest
    {
        string RequestID { get; }
    }
}
