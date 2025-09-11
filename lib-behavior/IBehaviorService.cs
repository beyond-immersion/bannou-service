using Microsoft.AspNetCore.Mvc;
using BeyondImmersion.BannouService.Controllers.Generated;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Interface for behavior service operations.
/// Implements business logic for the generated BehaviourController methods.
/// </summary>
public interface IBehaviorService
{
    /// <summary>
    /// Add new behavior tree
    /// </summary>
    Task<ActionResult<AddBehaviourTreeResponse>> AddBehaviourTreeAsync(
        AddBehaviourTreeRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate behavior definition
    /// </summary>
    Task<ActionResult<ValidateBehaviourResponse>> ValidateBehaviourAsync(
        ValidateBehaviourRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate prerequisite condition
    /// </summary>
    Task<ActionResult<ValidatePrerequisiteResponse>> ValidatePrerequisiteAsync(
        ValidatePrerequisiteRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve behavior references
    /// </summary>
    Task<ActionResult<ResolveReferencesResponse>> ResolveBehaviourReferencesAsync(
        ResolveReferencesRequest body,
        CancellationToken cancellationToken = default);
}
