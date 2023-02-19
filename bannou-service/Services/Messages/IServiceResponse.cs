using BeyondImmersion.BannouService.Application;
using BeyondImmersion.BannouService.Attributes;
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
        string Code { get; }
        string Message { get; }
    }
}
