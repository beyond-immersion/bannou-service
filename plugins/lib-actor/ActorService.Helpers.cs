using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Actor.Pool;
using BeyondImmersion.BannouService.Actor.Providers;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService.Actor;

// =============================================================================
// ActorService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by ActorService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (ActorService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IActorService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (ActorService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for ActorService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class ActorService
{
    // Move private/internal helper methods here from ActorService.cs
    /// <summary>
    /// Updates the template index with retry on optimistic concurrency conflict.
    /// Re-reads the index on each attempt to resolve conflicts from concurrent mutations.
    /// </summary>
    /// <param name="mutate">
    /// Function that mutates the index list. Returns true if a change was made (needs save),
    /// false if no change needed (already present for add, already absent for remove).
    /// </param>
    /// <param name="operation">Operation name for logging (e.g., "create", "delete").</param>
    /// <param name="templateId">Template ID for logging context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task UpdateTemplateIndexAsync(
        Func<List<string>, bool> mutate,
        string operation,
        Guid templateId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorService.UpdateTemplateIndex");
        var indexStore = _templateIndexStore;

        for (int attempt = 1; attempt <= TemplateIndexMaxRetries; attempt++)
        {
            var (allIds, etag) = await indexStore.GetWithETagAsync(ALL_TEMPLATES_KEY, cancellationToken);
            allIds ??= new List<string>();

            if (!mutate(allIds))
            {
                return; // No mutation needed (idempotent - already in desired state)
            }

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var result = await indexStore.TrySaveAsync(ALL_TEMPLATES_KEY, allIds, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (result != null)
            {
                return; // Successfully saved
            }

            if (attempt < TemplateIndexMaxRetries)
            {
                _logger.LogDebug(
                    "Template index conflict during {Operation} of {TemplateId}, retrying (attempt {Attempt}/{Max})",
                    operation, templateId, attempt, TemplateIndexMaxRetries);
            }
            else
            {
                _logger.LogWarning(
                    "Template index update failed after {MaxRetries} attempts during {Operation} of {TemplateId}",
                    TemplateIndexMaxRetries, operation, templateId);
            }
        }
    }
    /// <summary>
    /// Resolves the realm ID for an actor spawn request.
    /// Uses the provided realmId if available, otherwise looks up from character.
    /// </summary>
    private async Task<Guid?> ResolveRealmIdAsync(Guid? providedRealmId, Guid? characterId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorService.ResolveRealmId");
        if (providedRealmId.HasValue)
        {
            return providedRealmId.Value;
        }

        if (!characterId.HasValue)
        {
            _logger.LogWarning("No realmId provided and no characterId to look up realm from");
            return null;
        }

        try
        {
            var response = await _characterClient.GetCharacterAsync(
                new Character.GetCharacterRequest { CharacterId = characterId.Value }, ct);

            _logger.LogDebug("Resolved realmId {RealmId} from character {CharacterId}",
                response.RealmId, characterId.Value);
            return response.RealmId;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Character service call failed for realm resolution of {CharacterId} with status {Status}",
                characterId.Value, ex.StatusCode);
            return null;
        }
    }
    /// <summary>
    /// Finds a template with auto-spawn enabled that matches the given actor ID.
    /// </summary>
    /// <param name="actorId">The actor ID to match against template patterns.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing the matching template (or null) and extracted CharacterId (or null).</returns>
    private async Task<(ActorTemplateData? Template, Guid? CharacterId)> FindAutoSpawnTemplateAsync(
        string actorId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorService.FindAutoSpawnTemplate");
        var templateStore = _templateStore;
        var indexStore = _templateIndexStore;

        // Get all template IDs
        var allIds = await indexStore.GetAsync(ALL_TEMPLATES_KEY, cancellationToken);
        if (allIds == null || allIds.Count == 0)
        {
            return (null, null);
        }

        // Load all templates
        var templates = await templateStore.GetBulkAsync(allIds, cancellationToken);

        foreach (var template in templates.Values)
        {
            // Skip templates without auto-spawn enabled
            if (template.AutoSpawn?.Enabled != true)
            {
                continue;
            }

            // Skip templates without a pattern
            if (string.IsNullOrEmpty(template.AutoSpawn.IdPattern))
            {
                continue;
            }

            // Check if actorId matches the pattern using cached regex with timeout
            Match match;
            try
            {
                var pattern = template.AutoSpawn.IdPattern;
                var regex = _regexCache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.Compiled, RegexTimeout));
                match = regex.Match(actorId);
                if (!match.Success)
                {
                    continue;
                }
            }
            catch (RegexMatchTimeoutException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Regex pattern timed out in template {TemplateId}: {Pattern}",
                    template.TemplateId, template.AutoSpawn.IdPattern);
                continue;
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Invalid regex pattern in template {TemplateId}: {Pattern}",
                    template.TemplateId, template.AutoSpawn.IdPattern);
                continue;
            }

            // Extract CharacterId from capture group if configured
            Guid? extractedCharacterId = null;
            if (template.AutoSpawn.CharacterIdCaptureGroup is int groupIndex && groupIndex > 0)
            {
                if (match.Groups.Count > groupIndex)
                {
                    var groupValue = match.Groups[groupIndex].Value;
                    if (Guid.TryParse(groupValue, out var parsedId))
                    {
                        extractedCharacterId = parsedId;
                        _logger.LogDebug(
                            "Extracted CharacterId {CharacterId} from actor ID {ActorId} using capture group {Group}",
                            extractedCharacterId, actorId, groupIndex);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Capture group {Group} value '{Value}' is not a valid GUID for actor ID {ActorId}",
                            groupIndex, groupValue, actorId);
                    }
                }
            }

            // Check max instances limit if configured
            if (template.AutoSpawn.MaxInstances.HasValue && template.AutoSpawn.MaxInstances.Value > 0)
            {
                var currentCount = _actorRegistry.GetByTemplateId(template.TemplateId).Count();

                // In pool mode, also count assigned actors
                if (_configuration.DeploymentMode != ActorDeploymentMode.Bannou)
                {
                    var assignments = await _poolManager.GetAssignmentsByTemplateAsync(
                        template.TemplateId.ToString(), cancellationToken);
                    currentCount += assignments.Count();
                }

                if (currentCount >= template.AutoSpawn.MaxInstances.Value)
                {
                    _logger.LogDebug(
                        "Template {TemplateId} has reached max instances ({Current}/{Max})",
                        template.TemplateId, currentCount, template.AutoSpawn.MaxInstances.Value);
                    continue;
                }
            }

            // Found a matching template
            return (template, extractedCharacterId);
        }

        return (null, null);
    }
    /// <summary>
    /// Lists actors from pool assignments for a specific node.
    /// Used in pool deployment modes when nodeId filter is specified.
    /// </summary>
    private async Task<(StatusCodes, ListActorsResponse?)> ListActorsFromPoolAsync(
        ListActorsRequest body,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorService.ListActorsFromPool");
        // NodeId is verified non-null by caller; extract to satisfy NRT analysis
        var nodeId = body.NodeId ?? throw new InvalidOperationException("NodeId must be set before calling ListActorsFromPoolAsync");
        var assignments = await _poolManager.ListActorsByNodeAsync(nodeId, cancellationToken);

        IEnumerable<ActorAssignment> filtered = assignments;

        if (!string.IsNullOrWhiteSpace(body.Category))
        {
            filtered = filtered.Where(a => string.Equals(a.Category, body.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (body.Status != default)
        {
            filtered = filtered.Where(a => a.Status == body.Status);
        }

        if (body.CharacterId.HasValue)
        {
            filtered = filtered.Where(a => a.CharacterId == body.CharacterId);
        }

        var filteredList = filtered.ToList();
        var total = filteredList.Count;
        var actors = filteredList
            .Skip(body.Offset)
            .Take(body.Limit)
            .Select(a => new ActorInstanceResponse
            {
                ActorId = a.ActorId,
                TemplateId = a.TemplateId,
                Category = a.Category ?? "unknown",
                CharacterId = a.CharacterId,
                RealmId = a.RealmId,
                NodeId = a.NodeId,
                NodeAppId = a.NodeAppId,
                Status = a.Status,
                StartedAt = a.StartedAt ?? a.AssignedAt,
                LoopIterations = null
            })
            .ToList();

        return (StatusCodes.OK, new ListActorsResponse
        {
            Actors = actors,
            Total = total
        });
    }
    /// <summary>
    /// Finds an actor by ID, returning either a local runner or the remote node ID.
    /// </summary>
    /// <returns>
    /// A tuple containing:
    /// - LocalRunner: The local actor runner if the actor is running locally, null otherwise
    /// - RemoteNodeId: The node ID where the actor is running if remote, null if local or not found
    /// </returns>
    private async Task<(IActorRunner? LocalRunner, string? RemoteNodeId)> FindActorAsync(
        string actorId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorService.FindActor");
        // First check local registry
        if (_actorRegistry.TryGet(actorId, out var localRunner))
        {
            return (localRunner, null);
        }

        // For distributed deployments, check pool manager for actor assignment
        var assignment = await _poolManager.GetActorAssignmentAsync(actorId, cancellationToken);
        if (assignment != null)
        {
            _logger.LogDebug(
                "Actor {ActorId} found on remote node {NodeId}, forwarding request",
                actorId,
                assignment.NodeId);
            return (null, assignment.NodeId);
        }

        return (null, null);
    }

    /// <summary>
    /// Invokes a method on a remote actor service node.
    /// </summary>
    private async Task<TResponse> InvokeRemoteAsync<TRequest, TResponse>(
        string nodeId,
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorService.InvokeRemote");
        _logger.LogDebug("Forwarding {Endpoint} to remote node {NodeId}", endpoint, nodeId);
        try
        {
            return await _meshClient.InvokeMethodAsync<TRequest, TResponse>(
                nodeId,
                endpoint,
                request,
                cancellationToken);
        }
        catch (ApiException ex)
        {
            // Remote service intentionally returned an error - propagate it
            _logger.LogDebug("Remote call to {NodeId}/{Endpoint} returned status {Status}: {Message}",
                nodeId, endpoint, ex.StatusCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            // Unexpected failure (network, timeout, etc.) - log and publish error event
            _logger.LogError(ex, "Unexpected error invoking remote {Endpoint} on node {NodeId}",
                endpoint, nodeId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "InvokeRemote",
                ex.GetType().Name,
                ex.Message,
                details: new { nodeId, endpoint });
            throw;
        }
    }

    /// <summary>
    /// Invokes a method on a remote actor service node without expecting a response body.
    /// </summary>
    private async Task InvokeRemoteNoResponseAsync<TRequest>(
        string nodeId,
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "ActorService.InvokeRemoteNoResponse");
        _logger.LogDebug("Forwarding {Endpoint} to remote node {NodeId} (no response)", endpoint, nodeId);
        try
        {
            await _meshClient.InvokeMethodAsync(
                nodeId,
                endpoint,
                request,
                cancellationToken);
        }
        catch (ApiException ex)
        {
            // Remote service intentionally returned an error - propagate it
            _logger.LogDebug("Remote call to {NodeId}/{Endpoint} returned status {Status}: {Message}",
                nodeId, endpoint, ex.StatusCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            // Unexpected failure (network, timeout, etc.) - log and publish error event
            _logger.LogError(ex, "Unexpected error invoking remote {Endpoint} on node {NodeId}",
                endpoint, nodeId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "InvokeRemote",
                ex.GetType().Name,
                ex.Message,
                details: new { nodeId, endpoint });
            throw;
        }
    }
}
