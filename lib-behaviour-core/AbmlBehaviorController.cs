using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Behaviour;

/// <summary>
/// Concrete implementation of the generated behavior controller.
/// Delegates to the injected behavior service for business logic.
/// </summary>
public class AbmlBehaviorController : BehaviorControllerControllerBase
{
    private readonly IBehaviorService _behaviorService;

    public AbmlBehaviorController(IBehaviorService behaviorService)
    {
        _behaviorService = behaviorService;
    }

    public override async Task<ActionResult<CompileBehaviorResponse>> CompileAbmlBehavior(string body, CancellationToken cancellationToken = default)
    {
        return await _behaviorService.CompileAbmlBehavior(body, cancellationToken);
    }

    public override async Task<ActionResult<CompileBehaviorResponse>> CompileBehaviorStack(BehaviorStackRequest body, CancellationToken cancellationToken = default)
    {
        return await _behaviorService.CompileBehaviorStack(body, cancellationToken);
    }

    public override async Task<ActionResult<ValidateAbmlResponse>> ValidateAbml(string body, CancellationToken cancellationToken = default)
    {
        return await _behaviorService.ValidateAbml(body, cancellationToken);
    }

    public override async Task<ActionResult<CachedBehaviorResponse>> GetCachedBehavior(string behavior_id, CancellationToken cancellationToken = default)
    {
        return await _behaviorService.GetCachedBehavior(behavior_id, cancellationToken);
    }

    public override async Task<IActionResult> InvalidateCachedBehavior(string behavior_id, CancellationToken cancellationToken = default)
    {
        return await _behaviorService.InvalidateCachedBehavior(behavior_id, cancellationToken);
    }

    public override async Task<ActionResult<ResolveContextResponse>> ResolveContextVariables(ResolveContextRequest body, CancellationToken cancellationToken = default)
    {
        return await _behaviorService.ResolveContextVariables(body, cancellationToken);
    }
}
