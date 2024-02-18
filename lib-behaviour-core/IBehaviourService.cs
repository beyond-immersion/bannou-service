using BeyondImmersion.BannouService.Services;
using System.Net;

namespace BeyondImmersion.BannouService.Behaviour;

/// <summary>
/// Service component responsible for AI behaviour handling.
/// </summary>
public interface IBehaviourService : IDaprService
{
    /// <summary>
    /// Adds a new behaviour tree to the system.
    /// </summary>
    Task<ServiceResponse> AddBehaviourTree();
}
