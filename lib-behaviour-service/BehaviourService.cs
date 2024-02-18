using BeyondImmersion.BannouService.Behaviour.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using JWT;
using JWT.Algorithms;
using JWT.Builder;
using JWT.Serializers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;

namespace BeyondImmersion.BannouService.Behaviour;

/// <summary>
/// Service component responsible for AI behaviour handling.
/// </summary>
[DaprService("behaviour", typeof(IBehaviourService))]
public sealed class BehaviourService : DaprService<BehaviourServiceConfiguration>, IBehaviourService
{
    async Task IDaprService.OnStart(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Adds a new behaviour tree to the AI behaviour system.
    /// </summary>
    public async Task<(HttpStatusCode, object?)> AddBehaviourTree()
    {
        await Task.CompletedTask;

        return (HttpStatusCode.OK, null);
    }
}
