using BeyondImmersion.BannouService.Actor.Caching;
using BeyondImmersion.BannouService.Actor.Pool;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("lib-actor.tests")]

namespace BeyondImmersion.BannouService.Actor;

/// <summary>
/// Implementation of the Actor service.
/// This class contains the business logic for all Actor operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// Standard structure:
/// <list type="bullet">
///   <item>ActorService.cs (this file) - Business logic</item>
///   <item>ActorServiceEvents.cs - Event consumer handlers (generated)</item>
///   <item>Generated/ActorPermissionRegistration.cs - Permission registration (generated)</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("actor", typeof(IActorService), lifetime: ServiceLifetime.Scoped)]
public partial class ActorService : IActorService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<ActorService> _logger;
    private readonly ActorServiceConfiguration _configuration;
    private readonly IActorRegistry _actorRegistry;
    private readonly IActorRunnerFactory _actorRunnerFactory;
    private readonly IEventConsumer _eventConsumer;
    private readonly IBehaviorDocumentCache _behaviorCache;
    private readonly IActorPoolManager _poolManager;
    private readonly IPersonalityCache _personalityCache;
    private readonly IMeshInvocationClient _meshClient;

    // State store names now come from configuration (TemplateStatestoreName, InstanceStatestoreName)
    private const string ALL_TEMPLATES_KEY = "_all_template_ids";

    /// <summary>
    /// Creates a new instance of the ActorService.
    /// </summary>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="stateStoreFactory">Factory for creating state stores.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="actorRegistry">Registry for tracking active actors.</param>
    /// <param name="actorRunnerFactory">Factory for creating actor runners.</param>
    /// <param name="eventConsumer">Event consumer for registering handlers.</param>
    /// <param name="behaviorCache">Behavior document cache for hot-reload invalidation.</param>
    /// <param name="poolManager">Pool manager for distributed actor routing.</param>
    /// <param name="personalityCache">Cache for character personality data.</param>
    /// <param name="meshClient">Mesh client for invoking methods on remote nodes.</param>
    public ActorService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<ActorService> logger,
        ActorServiceConfiguration configuration,
        IActorRegistry actorRegistry,
        IActorRunnerFactory actorRunnerFactory,
        IEventConsumer eventConsumer,
        IBehaviorDocumentCache behaviorCache,
        IActorPoolManager poolManager,
        IPersonalityCache personalityCache,
        IMeshInvocationClient meshClient)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _actorRegistry = actorRegistry;
        _actorRunnerFactory = actorRunnerFactory;
        _eventConsumer = eventConsumer;
        _behaviorCache = behaviorCache;
        _poolManager = poolManager;
        _personalityCache = personalityCache;
        _meshClient = meshClient;

        // Register event handlers via partial class (ActorServiceEvents.cs)
        RegisterEventConsumers(_eventConsumer);
    }

    #region Template CRUD

    /// <summary>
    /// Creates a new actor template.
    /// </summary>
    public async Task<(StatusCodes, ActorTemplateResponse?)> CreateActorTemplateAsync(
        CreateActorTemplateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating actor template for category {Category}", body.Category);

        try
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(body.Category))
            {
                return (StatusCodes.BadRequest, null);
            }

            if (string.IsNullOrWhiteSpace(body.BehaviorRef))
            {
                return (StatusCodes.BadRequest, null);
            }

            var templateStore = _stateStoreFactory.GetStore<ActorTemplateData>(_configuration.TemplateStatestoreName);
            var templateId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            // Check if category already exists
            var existing = await templateStore.GetAsync($"category:{body.Category}", cancellationToken);
            if (existing != null)
            {
                _logger.LogWarning("Template for category {Category} already exists", body.Category);
                return (StatusCodes.Conflict, null);
            }

            var template = new ActorTemplateData
            {
                TemplateId = templateId,
                Category = body.Category,
                BehaviorRef = body.BehaviorRef,
                Configuration = body.Configuration,
                AutoSpawn = AutoSpawnConfigData.FromConfig(body.AutoSpawn),
                TickIntervalMs = body.TickIntervalMs > 0 ? body.TickIntervalMs : _configuration.DefaultTickIntervalMs,
                AutoSaveIntervalSeconds = body.AutoSaveIntervalSeconds >= 0
                    ? body.AutoSaveIntervalSeconds
                    : _configuration.DefaultAutoSaveIntervalSeconds,
                MaxInstancesPerNode = body.MaxInstancesPerNode > 0
                    ? body.MaxInstancesPerNode
                    : _configuration.DefaultActorsPerNode,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Save to state store (by ID and by category for lookup)
            await templateStore.SaveAsync(templateId.ToString(), template, cancellationToken: cancellationToken);
            await templateStore.SaveAsync($"category:{body.Category}", template, cancellationToken: cancellationToken);

            // Add to template index
            var indexStore = _stateStoreFactory.GetStore<List<string>>(_configuration.TemplateStatestoreName);
            var allIds = await indexStore.GetAsync(ALL_TEMPLATES_KEY, cancellationToken) ?? new List<string>();
            if (!allIds.Contains(templateId.ToString()))
            {
                allIds.Add(templateId.ToString());
                await indexStore.SaveAsync(ALL_TEMPLATES_KEY, allIds, cancellationToken: cancellationToken);
            }

            // Publish created event
            var evt = new ActorTemplateCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                TemplateId = templateId,
                Category = body.Category,
                BehaviorRef = body.BehaviorRef,
                CreatedAt = now
            };
            await _messageBus.TryPublishAsync("actor-template.created", evt, cancellationToken: cancellationToken);

            _logger.LogInformation("Created actor template {TemplateId} for category {Category}",
                templateId, body.Category);

            return (StatusCodes.OK, template.ToResponse());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating actor template");
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "CreateActorTemplate",
                "unexpected_exception",
                ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets an actor template by ID or category.
    /// </summary>
    public async Task<(StatusCodes, ActorTemplateResponse?)> GetActorTemplateAsync(
        GetActorTemplateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting actor template (templateId: {TemplateId}, category: {Category})",
            body.TemplateId, body.Category);

        try
        {
            var templateStore = _stateStoreFactory.GetStore<ActorTemplateData>(_configuration.TemplateStatestoreName);
            ActorTemplateData? template = null;

            if (body.TemplateId.HasValue)
            {
                template = await templateStore.GetAsync(body.TemplateId.Value.ToString(), cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(body.Category))
            {
                template = await templateStore.GetAsync($"category:{body.Category}", cancellationToken);
            }
            else
            {
                return (StatusCodes.BadRequest, null);
            }

            if (template == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, template.ToResponse());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting actor template");
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "GetActorTemplate",
                "unexpected_exception",
                ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Lists actor templates with pagination.
    /// </summary>
    public async Task<(StatusCodes, ListActorTemplatesResponse?)> ListActorTemplatesAsync(
        ListActorTemplatesRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing actor templates (limit: {Limit}, offset: {Offset})", body.Limit, body.Offset);

        try
        {
            var templateStore = _stateStoreFactory.GetStore<ActorTemplateData>(_configuration.TemplateStatestoreName);
            var indexStore = _stateStoreFactory.GetStore<List<string>>(_configuration.TemplateStatestoreName);

            // Get all template IDs from index
            var allIds = await indexStore.GetAsync(ALL_TEMPLATES_KEY, cancellationToken) ?? new List<string>();

            if (allIds.Count == 0)
            {
                return (StatusCodes.OK, new ListActorTemplatesResponse
                {
                    Templates = new List<ActorTemplateResponse>(),
                    Total = 0
                });
            }

            // Load templates by IDs
            var templatesDict = await templateStore.GetBulkAsync(allIds, cancellationToken);
            var templates = templatesDict.Values
                .OrderBy(t => t.CreatedAt)
                .Skip(body.Offset)
                .Take(body.Limit)
                .Select(t => t.ToResponse())
                .ToList();

            return (StatusCodes.OK, new ListActorTemplatesResponse
            {
                Templates = templates,
                Total = templatesDict.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing actor templates");
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "ListActorTemplates",
                "unexpected_exception",
                ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Updates an actor template.
    /// </summary>
    public async Task<(StatusCodes, ActorTemplateResponse?)> UpdateActorTemplateAsync(
        UpdateActorTemplateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating actor template {TemplateId}", body.TemplateId);

        try
        {
            var templateStore = _stateStoreFactory.GetStore<ActorTemplateData>(_configuration.TemplateStatestoreName);
            var existing = await templateStore.GetAsync(body.TemplateId.ToString(), cancellationToken);

            if (existing == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var changedFields = new List<string>();
            var now = DateTimeOffset.UtcNow;

            // Apply updates
            if (!string.IsNullOrWhiteSpace(body.BehaviorRef) && body.BehaviorRef != existing.BehaviorRef)
            {
                existing.BehaviorRef = body.BehaviorRef;
                changedFields.Add("behaviorRef");
            }

            if (body.Configuration != null)
            {
                existing.Configuration = body.Configuration;
                changedFields.Add("configuration");
            }

            if (body.AutoSpawn != null)
            {
                existing.AutoSpawn = AutoSpawnConfigData.FromConfig(body.AutoSpawn);
                changedFields.Add("autoSpawn");
            }

            if (body.TickIntervalMs.HasValue && body.TickIntervalMs.Value != existing.TickIntervalMs)
            {
                existing.TickIntervalMs = body.TickIntervalMs.Value;
                changedFields.Add("tickIntervalMs");
            }

            if (body.AutoSaveIntervalSeconds.HasValue && body.AutoSaveIntervalSeconds.Value != existing.AutoSaveIntervalSeconds)
            {
                existing.AutoSaveIntervalSeconds = body.AutoSaveIntervalSeconds.Value;
                changedFields.Add("autoSaveIntervalSeconds");
            }

            existing.UpdatedAt = now;

            // Save updates
            await templateStore.SaveAsync(body.TemplateId.ToString(), existing, cancellationToken: cancellationToken);
            await templateStore.SaveAsync($"category:{existing.Category}", existing, cancellationToken: cancellationToken);

            // Publish updated event
            var evt = new ActorTemplateUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                TemplateId = body.TemplateId,
                Category = existing.Category,
                BehaviorRef = existing.BehaviorRef,
                CreatedAt = existing.CreatedAt,
                ChangedFields = changedFields
            };
            await _messageBus.TryPublishAsync("actor-template.updated", evt, cancellationToken: cancellationToken);

            _logger.LogInformation("Updated actor template {TemplateId} (changed: {Fields})",
                body.TemplateId, string.Join(", ", changedFields));

            return (StatusCodes.OK, existing.ToResponse());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating actor template");
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "UpdateActorTemplate",
                "unexpected_exception",
                ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Deletes an actor template.
    /// </summary>
    public async Task<(StatusCodes, DeleteActorTemplateResponse?)> DeleteActorTemplateAsync(
        DeleteActorTemplateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting actor template {TemplateId} (forceStop: {ForceStop})",
            body.TemplateId, body.ForceStopActors);

        try
        {
            var templateStore = _stateStoreFactory.GetStore<ActorTemplateData>(_configuration.TemplateStatestoreName);
            var existing = await templateStore.GetAsync(body.TemplateId.ToString(), cancellationToken);

            if (existing == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var stoppedCount = 0;

            // Stop running actors if requested
            if (body.ForceStopActors)
            {
                var actorsToStop = _actorRegistry.GetByTemplateId(body.TemplateId).ToList();
                foreach (var actor in actorsToStop)
                {
                    try
                    {
                        await actor.StopAsync(graceful: true, cancellationToken);
                        _actorRegistry.TryRemove(actor.ActorId, out _);
                        stoppedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error stopping actor {ActorId} during template deletion",
                            actor.ActorId);
                    }
                }
            }

            // Delete from state store
            await templateStore.DeleteAsync(body.TemplateId.ToString(), cancellationToken);
            await templateStore.DeleteAsync($"category:{existing.Category}", cancellationToken);

            // Remove from template index
            var indexStore = _stateStoreFactory.GetStore<List<string>>(_configuration.TemplateStatestoreName);
            var allIds = await indexStore.GetAsync(ALL_TEMPLATES_KEY, cancellationToken) ?? new List<string>();
            if (allIds.Remove(body.TemplateId.ToString()))
            {
                await indexStore.SaveAsync(ALL_TEMPLATES_KEY, allIds, cancellationToken: cancellationToken);
            }

            // Publish deleted event
            var evt = new ActorTemplateDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                TemplateId = body.TemplateId,
                Category = existing.Category,
                BehaviorRef = existing.BehaviorRef,
                CreatedAt = existing.CreatedAt,
                DeletedReason = body.ForceStopActors ? $"Deleted with {stoppedCount} actors stopped" : null
            };
            await _messageBus.TryPublishAsync("actor-template.deleted", evt, cancellationToken: cancellationToken);

            _logger.LogInformation("Deleted actor template {TemplateId} (stopped {StoppedCount} actors)",
                body.TemplateId, stoppedCount);

            return (StatusCodes.OK, new DeleteActorTemplateResponse
            {
                Deleted = true,
                StoppedActorCount = stoppedCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting actor template");
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "DeleteActorTemplate",
                "unexpected_exception",
                ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Actor Lifecycle

    /// <summary>
    /// Spawns a new actor instance from a template.
    /// </summary>
    public async Task<(StatusCodes, ActorInstanceResponse?)> SpawnActorAsync(
        SpawnActorRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Spawning actor from template {TemplateId}", body.TemplateId);

        try
        {
            // Get template
            var templateStore = _stateStoreFactory.GetStore<ActorTemplateData>(_configuration.TemplateStatestoreName);
            var template = await templateStore.GetAsync(body.TemplateId.ToString(), cancellationToken);

            if (template == null)
            {
                _logger.LogWarning("Template {TemplateId} not found", body.TemplateId);
                return (StatusCodes.NotFound, null);
            }

            // Generate or use provided actor ID
            var actorId = !string.IsNullOrWhiteSpace(body.ActorId)
                ? body.ActorId
                : $"{template.Category}-{Guid.NewGuid():N}";

            // Check for duplicate (local registry - only in bannou mode)
            if (_configuration.DeploymentMode == "bannou" && _actorRegistry.TryGet(actorId, out _))
            {
                _logger.LogWarning("Actor {ActorId} already exists", actorId);
                return (StatusCodes.Conflict, null);
            }

            // Check pool assignment for non-bannou modes
            if (_configuration.DeploymentMode != "bannou")
            {
                var existingAssignment = await _poolManager.GetActorAssignmentAsync(actorId, cancellationToken);
                if (existingAssignment != null)
                {
                    _logger.LogWarning("Actor {ActorId} already assigned to node {NodeId}", actorId, existingAssignment.NodeId);
                    return (StatusCodes.Conflict, null);
                }
            }

            string nodeId;
            string nodeAppId;
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;

            if (_configuration.DeploymentMode == "bannou")
            {
                // Bannou mode: run locally
                var runner = _actorRunnerFactory.Create(
                    actorId,
                    template,
                    body.CharacterId,
                    body.ConfigurationOverrides,
                    body.InitialState);

                if (!_actorRegistry.TryRegister(actorId, runner))
                {
                    _logger.LogWarning("Failed to register actor {ActorId}", actorId);
                    await runner.DisposeAsync();
                    return (StatusCodes.Conflict, null);
                }

                await runner.StartAsync(cancellationToken);
                nodeId = _configuration.LocalModeNodeId;
                nodeAppId = _configuration.LocalModeAppId;
                startedAt = runner.StartedAt;
            }
            else
            {
                // Pool mode: route to pool node
                // Acquire a pool node with capacity
                var poolNode = await _poolManager.AcquireNodeForActorAsync(template.Category, 1, cancellationToken);
                if (poolNode == null)
                {
                    _logger.LogWarning("No pool nodes with capacity available for category {Category}", template.Category);
                    return (StatusCodes.ServiceUnavailable, null);
                }

                // Record the assignment
                var assignment = new ActorAssignment
                {
                    ActorId = actorId,
                    NodeId = poolNode.NodeId,
                    NodeAppId = poolNode.AppId,
                    TemplateId = body.TemplateId.ToString(),
                    Category = template.Category,
                    Status = "pending",
                    CharacterId = body.CharacterId
                };
                await _poolManager.RecordActorAssignmentAsync(assignment, cancellationToken);

                // Send spawn command to pool node
                var spawnCommand = new SpawnActorCommand
                {
                    ActorId = actorId,
                    TemplateId = body.TemplateId,
                    BehaviorRef = template.BehaviorRef,
                    Configuration = template.Configuration,
                    InitialState = body.InitialState,
                    TickIntervalMs = template.TickIntervalMs > 0 ? template.TickIntervalMs : _configuration.DefaultTickIntervalMs,
                    AutoSaveIntervalSeconds = template.AutoSaveIntervalSeconds > 0 ? template.AutoSaveIntervalSeconds : _configuration.DefaultAutoSaveIntervalSeconds,
                    CharacterId = body.CharacterId
                };
                await _messageBus.TryPublishAsync($"actor.node.{poolNode.AppId}.spawn", spawnCommand, cancellationToken: cancellationToken);

                nodeId = poolNode.NodeId;
                nodeAppId = poolNode.AppId;

                _logger.LogInformation("Routed actor {ActorId} to pool node {NodeId} (appId: {AppId})",
                    actorId, poolNode.NodeId, poolNode.AppId);
            }

            // Publish spawned event
            var evt = new ActorInstanceCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ActorId = actorId,
                TemplateId = body.TemplateId,
                CharacterId = body.CharacterId ?? Guid.Empty,
                NodeId = nodeId,
                Status = _configuration.DeploymentMode == "bannou" ? "running" : "pending",
                StartedAt = startedAt
            };
            await _messageBus.TryPublishAsync("actor-instance.created", evt, cancellationToken: cancellationToken);

            _logger.LogInformation("Spawned actor {ActorId} from template {TemplateId}",
                actorId, body.TemplateId);

            return (StatusCodes.OK, new ActorInstanceResponse
            {
                ActorId = actorId,
                TemplateId = body.TemplateId,
                Category = template.Category,
                CharacterId = body.CharacterId,
                NodeId = nodeId,
                NodeAppId = nodeAppId,
                Status = _configuration.DeploymentMode == "bannou" ? ActorStatus.Running : ActorStatus.Pending,
                StartedAt = startedAt,
                LoopIterations = 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error spawning actor");
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "SpawnActor",
                "unexpected_exception",
                ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets an actor instance by ID, optionally auto-spawning if a matching template allows it.
    /// </summary>
    /// <remarks>
    /// Auto-spawn behavior: If the actor doesn't exist but a template has AutoSpawn.Enabled=true
    /// and the actorId matches the template's IdPattern regex, the actor will be automatically
    /// spawned on first access. This enables "instantiate-on-access" patterns for NPC brains.
    /// </remarks>
    public async Task<(StatusCodes, ActorInstanceResponse?)> GetActorAsync(
        GetActorRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting actor {ActorId}", body.ActorId);

        try
        {
            // First check local registry (bannou mode) or pool assignments (pool mode)
            if (_actorRegistry.TryGet(body.ActorId, out var runner) && runner != null)
            {
                return (StatusCodes.OK, runner.GetStateSnapshot().ToResponse(
                    nodeId: _configuration.DeploymentMode == "bannou" ? _configuration.LocalModeNodeId : null,
                    nodeAppId: _configuration.DeploymentMode == "bannou" ? _configuration.LocalModeAppId : null));
            }

            // In pool mode, check if actor is assigned to a pool node
            if (_configuration.DeploymentMode != "bannou")
            {
                var assignment = await _poolManager.GetActorAssignmentAsync(body.ActorId, cancellationToken);
                if (assignment != null)
                {
                    // Actor exists on a pool node - return its status
                    return (StatusCodes.OK, new ActorInstanceResponse
                    {
                        ActorId = assignment.ActorId,
                        TemplateId = Guid.TryParse(assignment.TemplateId, out var tid) ? tid : Guid.Empty,
                        Category = assignment.Category ?? "unknown",
                        CharacterId = assignment.CharacterId,
                        NodeId = assignment.NodeId,
                        NodeAppId = assignment.NodeAppId,
                        Status = assignment.Status == "running" ? ActorStatus.Running :
                                assignment.Status == "stopping" ? ActorStatus.Stopping :
                                ActorStatus.Pending,
                        StartedAt = assignment.StartedAt ?? assignment.AssignedAt,
                        LoopIterations = 0
                    });
                }
            }

            // Actor not found - check for auto-spawn templates
            var (matchingTemplate, extractedCharacterId) = await FindAutoSpawnTemplateAsync(body.ActorId, cancellationToken);
            if (matchingTemplate != null)
            {
                _logger.LogInformation(
                    "Auto-spawning actor {ActorId} from template {TemplateId} (category: {Category}, characterId: {CharacterId})",
                    body.ActorId, matchingTemplate.TemplateId, matchingTemplate.Category, extractedCharacterId);

                // Spawn the actor using the matched template
                // CharacterId is extracted from actor ID pattern via characterIdCaptureGroup if configured
                var spawnRequest = new SpawnActorRequest
                {
                    TemplateId = matchingTemplate.TemplateId,
                    ActorId = body.ActorId,
                    CharacterId = extractedCharacterId
                };

                var (spawnStatus, spawnResponse) = await SpawnActorAsync(spawnRequest, cancellationToken);
                if (spawnStatus == StatusCodes.OK && spawnResponse != null)
                {
                    return (StatusCodes.OK, spawnResponse);
                }

                // If spawn failed (e.g., max instances exceeded, conflict), return not found
                _logger.LogWarning(
                    "Auto-spawn of actor {ActorId} failed with status {Status}",
                    body.ActorId, spawnStatus);
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.NotFound, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting actor {ActorId}", body.ActorId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "GetActor",
                "unexpected_exception",
                ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
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
        var templateStore = _stateStoreFactory.GetStore<ActorTemplateData>(_configuration.TemplateStatestoreName);
        var indexStore = _stateStoreFactory.GetStore<List<string>>(_configuration.TemplateStatestoreName);

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

            // Check if actorId matches the pattern
            Match match;
            try
            {
                match = Regex.Match(actorId, template.AutoSpawn.IdPattern);
                if (!match.Success)
                {
                    continue;
                }
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
                if (_configuration.DeploymentMode != "bannou")
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
    /// Stops a running actor.
    /// </summary>
    public async Task<(StatusCodes, StopActorResponse?)> StopActorAsync(
        StopActorRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping actor {ActorId} (graceful: {Graceful})", body.ActorId, body.Graceful);

        try
        {
            if (_configuration.DeploymentMode == "bannou")
            {
                // Bannou mode: stop locally
                if (!_actorRegistry.TryGet(body.ActorId, out var runner) || runner == null)
                {
                    return (StatusCodes.NotFound, null);
                }

                await runner.StopAsync(body.Graceful, cancellationToken);
                _actorRegistry.TryRemove(body.ActorId, out _);

                // Publish stopped event
                var evt = new ActorInstanceDeletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    ActorId = body.ActorId,
                    TemplateId = runner.TemplateId,
                    CharacterId = runner.CharacterId ?? Guid.Empty,
                    NodeId = _configuration.LocalModeNodeId,
                    Status = runner.Status.ToString().ToLowerInvariant(),
                    StartedAt = runner.StartedAt,
                    DeletedReason = body.Graceful ? "graceful_stop" : "forced_stop"
                };
                await _messageBus.TryPublishAsync("actor-instance.deleted", evt, cancellationToken: cancellationToken);

                await runner.DisposeAsync();

                _logger.LogInformation("Stopped actor {ActorId}", body.ActorId);

                return (StatusCodes.OK, new StopActorResponse
                {
                    Stopped = true,
                    FinalStatus = runner.Status
                });
            }
            else
            {
                // Pool mode: send stop command to pool node
                // Get assignment to find the node
                var assignment = await _poolManager.GetActorAssignmentAsync(body.ActorId, cancellationToken);
                if (assignment == null)
                {
                    _logger.LogWarning("Actor {ActorId} not found in pool assignments", body.ActorId);
                    return (StatusCodes.NotFound, null);
                }

                // Send stop command to pool node
                var stopCommand = new StopActorCommand
                {
                    ActorId = body.ActorId,
                    Graceful = body.Graceful
                };
                await _messageBus.TryPublishAsync($"actor.node.{assignment.NodeAppId}.stop", stopCommand, cancellationToken: cancellationToken);

                // Remove assignment (the pool node will publish ActorCompletedEvent)
                await _poolManager.RemoveActorAssignmentAsync(body.ActorId, cancellationToken);

                _logger.LogInformation("Sent stop command for actor {ActorId} to node {NodeId}",
                    body.ActorId, assignment.NodeId);

                return (StatusCodes.OK, new StopActorResponse
                {
                    Stopped = true,
                    FinalStatus = ActorStatus.Stopping
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping actor {ActorId}", body.ActorId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "StopActor",
                "unexpected_exception",
                ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Lists active actors with filtering.
    /// </summary>
    public async Task<(StatusCodes, ListActorsResponse?)> ListActorsAsync(
        ListActorsRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing actors (category: {Category}, nodeId: {NodeId}, status: {Status})",
            body.Category, body.NodeId, body.Status);

        try
        {
            IEnumerable<IActorRunner> runners = _actorRegistry.GetAllRunners();

            // Apply filters
            if (!string.IsNullOrWhiteSpace(body.Category))
            {
                runners = runners.Where(r => string.Equals(r.Category, body.Category, StringComparison.OrdinalIgnoreCase));
            }

            if (body.Status != default)
            {
                runners = runners.Where(r => r.Status == body.Status);
            }

            if (body.CharacterId.HasValue)
            {
                runners = runners.Where(r => r.CharacterId == body.CharacterId);
            }

            // Note: nodeId filtering not applicable in bannou mode

            var total = runners.Count();
            var actors = runners
                .Skip(body.Offset)
                .Take(body.Limit)
                .Select(r => r.GetStateSnapshot().ToResponse(
                    nodeId: _configuration.DeploymentMode == "bannou" ? _configuration.LocalModeNodeId : null,
                    nodeAppId: _configuration.DeploymentMode == "bannou" ? _configuration.LocalModeAppId : null))
                .ToList();

            return (StatusCodes.OK, new ListActorsResponse
            {
                Actors = actors,
                Total = total
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing actors");
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "ListActors",
                "unexpected_exception",
                ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Testing

    /// <summary>
    /// Injects a perception into an actor's perception queue for testing.
    /// </summary>
    public async Task<(StatusCodes, InjectPerceptionResponse?)> InjectPerceptionAsync(
        InjectPerceptionRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Injecting perception into actor {ActorId}", body.ActorId);

        try
        {
            if (!_actorRegistry.TryGet(body.ActorId, out var runner) || runner == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var queued = runner.InjectPerception(body.Perception);

            _logger.LogDebug("Perception injected into actor {ActorId} (queued: {Queued})", body.ActorId, queued);

            return (StatusCodes.OK, new InjectPerceptionResponse
            {
                Queued = queued,
                QueueDepth = runner.PerceptionQueueDepth
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error injecting perception into actor {ActorId}", body.ActorId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "InjectPerception",
                "unexpected_exception",
                ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Query Options

    // Default tick interval for waiting on fresh queries (matches ActorRunner default)
    private const int DefaultTickIntervalMs = 100;

    /// <summary>
    /// Queries an actor for its available options.
    /// </summary>
    /// <remarks>
    /// Options are maintained by the actor in its state.memories.{queryType}_options.
    /// This endpoint reads from the actor's state based on requested freshness level.
    /// </remarks>
    public async Task<(StatusCodes, QueryOptionsResponse?)> QueryOptionsAsync(
        QueryOptionsRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying options from actor {ActorId} (type: {QueryType}, freshness: {Freshness})",
            body.ActorId, body.QueryType, body.Freshness);

        try
        {
            // Find the actor
            if (!_actorRegistry.TryGet(body.ActorId, out var runner) || runner == null)
            {
                // In pool mode, actor might be on another node
                if (_configuration.DeploymentMode != "bannou")
                {
                    var assignment = await _poolManager.GetActorAssignmentAsync(body.ActorId, cancellationToken);
                    if (assignment != null)
                    {
                        _logger.LogWarning(
                            "Actor {ActorId} is on pool node {NodeId} - query-options requires local actor",
                            body.ActorId, assignment.NodeId);
                        return (StatusCodes.BadRequest, null);
                    }
                }
                return (StatusCodes.NotFound, null);
            }

            var freshness = body.Freshness;
            var maxAgeMs = body.MaxAgeMs ?? 5000;
            var optionsKey = $"{body.QueryType.ToString().ToLowerInvariant()}_options";

            // Get actor state snapshot
            var stateSnapshot = runner.GetStateSnapshot();

            // Handle fresh queries by injecting context as perception
            if (freshness == OptionsFreshness.Fresh && body.Context != null)
            {
                // Inject query context as a perception to trigger recomputation
                var queryPerception = new PerceptionData
                {
                    PerceptionType = "options_query",
                    SourceId = "query-options-endpoint",
                    SourceType = "system",
                    Data = new Dictionary<string, object?>
                    {
                        ["queryType"] = body.QueryType.ToString(),
                        ["context"] = body.Context
                    },
                    Urgency = body.Context.Urgency ?? 0.5f
                };
                runner.InjectPerception(queryPerception);

                // Wait briefly for actor to process (one tick)
                await Task.Delay(DefaultTickIntervalMs, cancellationToken);

                // Re-fetch state after processing
                stateSnapshot = runner.GetStateSnapshot();
            }

            // Read options from actor's memories (list of MemoryEntry)
            var options = new List<ActorOption>();
            DateTimeOffset computedAt = DateTimeOffset.UtcNow;

            // Find the options memory entry by key
            var optionsEntry = stateSnapshot.Memories.FirstOrDefault(m => m.MemoryKey == optionsKey);
            if (optionsEntry?.MemoryValue != null)
            {
                // Options are stored as a memory with value being the list
                if (optionsEntry.MemoryValue is IEnumerable<ActorOption> optionsList)
                {
                    options = optionsList.ToList();
                }
                else if (optionsEntry.MemoryValue is IEnumerable<object> objectList)
                {
                    // Try to convert from generic objects
                    foreach (var obj in objectList)
                    {
                        if (obj is ActorOption option)
                        {
                            options.Add(option);
                        }
                        else if (obj is IDictionary<string, object?> dict)
                        {
                            options.Add(ConvertDictToOption(dict));
                        }
                    }
                }

                computedAt = optionsEntry.CreatedAt;
            }

            // Try to get computed timestamp from separate memory entry
            var timestampKey = $"{optionsKey}_timestamp";
            var timestampEntry = stateSnapshot.Memories.FirstOrDefault(m => m.MemoryKey == timestampKey);
            if (timestampEntry?.MemoryValue is DateTimeOffset storedTimestamp)
            {
                computedAt = storedTimestamp;
            }

            // Check freshness requirements
            var ageMs = (int)(DateTimeOffset.UtcNow - computedAt).TotalMilliseconds;
            if (freshness == OptionsFreshness.Cached && ageMs > maxAgeMs && options.Count == 0)
            {
                // Options too old and empty - actor may not support this query type
                _logger.LogDebug("Actor {ActorId} has no {QueryType} options (or they're stale)",
                    body.ActorId, body.QueryType);
            }

            // Build character context if this is a character-based actor
            CharacterOptionContext? characterContext = null;
            if (runner.CharacterId.HasValue)
            {
                characterContext = new CharacterOptionContext();

                // Extract combat preferences from memories if available
                var combatStyleEntry = stateSnapshot.Memories.FirstOrDefault(m => m.MemoryKey == "combat_style");
                if (combatStyleEntry?.MemoryValue != null)
                {
                    characterContext.CombatStyle = combatStyleEntry.MemoryValue.ToString();
                }

                var riskEntry = stateSnapshot.Memories.FirstOrDefault(m => m.MemoryKey == "risk_tolerance");
                if (riskEntry?.MemoryValue is float riskValue)
                {
                    characterContext.RiskTolerance = riskValue;
                }
                else if (riskEntry?.MemoryValue is double riskDouble)
                {
                    characterContext.RiskTolerance = (float)riskDouble;
                }

                var protectEntry = stateSnapshot.Memories.FirstOrDefault(m => m.MemoryKey == "protect_allies");
                if (protectEntry?.MemoryValue is bool protectValue)
                {
                    characterContext.ProtectAllies = protectValue;
                }

                if (stateSnapshot.Goals?.PrimaryGoal != null)
                {
                    characterContext.CurrentGoal = stateSnapshot.Goals.PrimaryGoal;
                }

                // Get dominant emotion
                var dominantEmotion = stateSnapshot.Feelings
                    .OrderByDescending(f => f.Value)
                    .FirstOrDefault();
                if (!string.IsNullOrEmpty(dominantEmotion.Key))
                {
                    characterContext.EmotionalState = dominantEmotion.Key;
                }
            }

            _logger.LogDebug("Returning {Count} options for actor {ActorId} (type: {QueryType}, age: {AgeMs}ms)",
                options.Count, body.ActorId, body.QueryType, ageMs);

            return (StatusCodes.OK, new QueryOptionsResponse
            {
                ActorId = body.ActorId,
                QueryType = body.QueryType,
                Options = options,
                ComputedAt = computedAt,
                AgeMs = ageMs,
                CharacterContext = characterContext
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying options from actor {ActorId}", body.ActorId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "QueryOptions",
                "unexpected_exception",
                ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Converts a dictionary to an ActorOption.
    /// </summary>
    private static ActorOption ConvertDictToOption(IDictionary<string, object?> dict)
    {
        var option = new ActorOption
        {
            ActionId = dict.TryGetValue("actionId", out var actionId) ? actionId?.ToString() ?? "" : "",
            Preference = dict.TryGetValue("preference", out var pref) && pref is float prefFloat ? prefFloat : 0.5f,
            Available = dict.TryGetValue("available", out var avail) && avail is bool availBool && availBool
        };

        if (dict.TryGetValue("risk", out var risk) && risk is float riskFloat)
        {
            option.Risk = riskFloat;
        }

        if (dict.TryGetValue("cooldownMs", out var cooldown) && cooldown is int cooldownInt)
        {
            option.CooldownMs = cooldownInt;
        }

        if (dict.TryGetValue("requirements", out var reqs) && reqs is IEnumerable<string> reqsList)
        {
            option.Requirements = reqsList.ToList();
        }

        if (dict.TryGetValue("tags", out var tags) && tags is IEnumerable<string> tagsList)
        {
            option.Tags = tagsList.ToList();
        }

        return option;
    }

    #endregion

    #region Encounter Management

    /// <summary>
    /// Starts an encounter managed by an Event Brain actor.
    /// </summary>
    public async Task<(StatusCodes, StartEncounterResponse?)> StartEncounterAsync(
        StartEncounterRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting encounter {EncounterId} on actor {ActorId}", body.EncounterId, body.ActorId);

        try
        {
            // Find the actor (may be local or remote)
            var (localRunner, remoteNodeId) = await FindActorAsync(body.ActorId, cancellationToken);

            // If actor is on a remote node, forward the request
            if (remoteNodeId != null)
            {
                var remoteResponse = await InvokeRemoteAsync<StartEncounterRequest, StartEncounterResponse>(
                    remoteNodeId,
                    "actor/encounter/start",
                    body,
                    cancellationToken);
                return (StatusCodes.OK, remoteResponse);
            }

            // Actor not found anywhere
            if (localRunner == null)
            {
                _logger.LogDebug("Actor {ActorId} not found for encounter start", body.ActorId);
                return (StatusCodes.NotFound, null);
            }

            var runner = localRunner;

            // Convert initialData to Dictionary<string, object?> if it's a dictionary
            Dictionary<string, object?>? initialData = null;
            if (body.InitialData is IDictionary<string, object?> dict)
            {
                initialData = new Dictionary<string, object?>(dict);
            }
            else if (body.InitialData is System.Text.Json.JsonElement jsonElement &&
                    jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                initialData = new Dictionary<string, object?>();
                foreach (var prop in jsonElement.EnumerateObject())
                {
                    initialData[prop.Name] = prop.Value.Clone();
                }
            }

            // Start the encounter (Participants is already ICollection<Guid>)
            var success = runner.StartEncounter(
                body.EncounterId,
                body.EncounterType,
                body.Participants.ToList(),
                initialData);

            if (!success)
            {
                _logger.LogDebug("Actor {ActorId} already has an active encounter", body.ActorId);
                return (StatusCodes.Conflict, null);
            }

            _logger.LogInformation("Started encounter {EncounterId} on actor {ActorId} with {Count} participants",
                body.EncounterId, body.ActorId, body.Participants.Count);

            return (StatusCodes.OK, new StartEncounterResponse
            {
                ActorId = body.ActorId,
                EncounterId = body.EncounterId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting encounter {EncounterId} on actor {ActorId}",
                body.EncounterId, body.ActorId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "StartEncounter",
                "unexpected_exception",
                ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Updates the phase of an active encounter.
    /// </summary>
    public async Task<(StatusCodes, UpdateEncounterPhaseResponse?)> UpdateEncounterPhaseAsync(
        UpdateEncounterPhaseRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating encounter phase on actor {ActorId} to {Phase}", body.ActorId, body.Phase);

        try
        {
            // Find the actor (may be local or remote)
            var (localRunner, remoteNodeId) = await FindActorAsync(body.ActorId, cancellationToken);

            // If actor is on a remote node, forward the request
            if (remoteNodeId != null)
            {
                var remoteResponse = await InvokeRemoteAsync<UpdateEncounterPhaseRequest, UpdateEncounterPhaseResponse>(
                    remoteNodeId,
                    "actor/encounter/phase/update",
                    body,
                    cancellationToken);
                return (StatusCodes.OK, remoteResponse);
            }

            // Actor not found anywhere
            if (localRunner == null)
            {
                _logger.LogDebug("Actor {ActorId} not found for encounter phase update", body.ActorId);
                return (StatusCodes.NotFound, null);
            }

            var runner = localRunner;

            // Get previous phase for response
            var snapshot = runner.GetStateSnapshot();
            var encounter = snapshot.Encounter;

            // No active encounter to update
            if (encounter == null)
            {
                _logger.LogDebug("Actor {ActorId} has no active encounter to update phase", body.ActorId);
                return (StatusCodes.NotFound, null);
            }

            var previousPhase = encounter.Phase;

            // Update the phase
            var success = runner.SetEncounterPhase(body.Phase);

            return (StatusCodes.OK, new UpdateEncounterPhaseResponse
            {
                ActorId = body.ActorId,
                PreviousPhase = previousPhase,
                CurrentPhase = success ? body.Phase : previousPhase
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating encounter phase on actor {ActorId}", body.ActorId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "UpdateEncounterPhase",
                "unexpected_exception",
                ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Ends an active encounter.
    /// </summary>
    public async Task<(StatusCodes, EndEncounterResponse?)> EndEncounterAsync(
        EndEncounterRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Ending encounter on actor {ActorId}", body.ActorId);

        try
        {
            // Find the actor (may be local or remote)
            var (localRunner, remoteNodeId) = await FindActorAsync(body.ActorId, cancellationToken);

            // If actor is on a remote node, forward the request
            if (remoteNodeId != null)
            {
                var remoteResponse = await InvokeRemoteAsync<EndEncounterRequest, EndEncounterResponse>(
                    remoteNodeId,
                    "actor/encounter/end",
                    body,
                    cancellationToken);
                return (StatusCodes.OK, remoteResponse);
            }

            // Actor not found anywhere
            if (localRunner == null)
            {
                _logger.LogDebug("Actor {ActorId} not found for encounter end", body.ActorId);
                return (StatusCodes.NotFound, null);
            }

            var runner = localRunner;

            // Get encounter info before ending
            var snapshot = runner.GetStateSnapshot();
            var encounter = snapshot.Encounter;

            // No active encounter to end
            if (encounter == null)
            {
                _logger.LogDebug("Actor {ActorId} has no active encounter to end", body.ActorId);
                return (StatusCodes.NotFound, null);
            }

            var encounterId = encounter.EncounterId;
            var startedAt = encounter.StartedAt;

            // End the encounter
            var success = runner.EndEncounter();

            var durationMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

            if (success)
            {
                _logger.LogInformation("Ended encounter {EncounterId} on actor {ActorId} (duration: {Duration}ms)",
                    encounterId, body.ActorId, durationMs);
            }

            return (StatusCodes.OK, new EndEncounterResponse
            {
                ActorId = body.ActorId,
                EncounterId = encounterId,
                DurationMs = durationMs
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending encounter on actor {ActorId}", body.ActorId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "EndEncounter",
                "unexpected_exception",
                ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets the current encounter state for an actor.
    /// </summary>
    public async Task<(StatusCodes, GetEncounterResponse?)> GetEncounterAsync(
        GetEncounterRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            // Find the actor (may be local or remote)
            var (localRunner, remoteNodeId) = await FindActorAsync(body.ActorId, cancellationToken);

            // If actor is on a remote node, forward the request
            if (remoteNodeId != null)
            {
                var remoteResponse = await InvokeRemoteAsync<GetEncounterRequest, GetEncounterResponse>(
                    remoteNodeId,
                    "actor/encounter/get",
                    body,
                    cancellationToken);
                return (StatusCodes.OK, remoteResponse);
            }

            // Actor not found anywhere
            if (localRunner == null)
            {
                _logger.LogDebug("Actor {ActorId} not found for encounter get", body.ActorId);
                return (StatusCodes.NotFound, null);
            }

            var runner = localRunner;
            var snapshot = runner.GetStateSnapshot();
            var encounterData = snapshot.Encounter;

            if (encounterData == null)
            {
                return (StatusCodes.OK, new GetEncounterResponse
                {
                    ActorId = body.ActorId,
                    HasActiveEncounter = false,
                    Encounter = null
                });
            }

            return (StatusCodes.OK, new GetEncounterResponse
            {
                ActorId = body.ActorId,
                HasActiveEncounter = true,
                Encounter = new EncounterState
                {
                    EncounterId = encounterData.EncounterId,
                    EncounterType = encounterData.EncounterType,
                    Participants = encounterData.Participants,
                    Phase = encounterData.Phase,
                    StartedAt = encounterData.StartedAt,
                    Data = encounterData.Data
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting encounter for actor {ActorId}", body.ActorId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "GetEncounter",
                "unexpected_exception",
                ex.Message,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
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
        _logger.LogDebug("Forwarding {Endpoint} to remote node {NodeId}", endpoint, nodeId);
        return await _meshClient.InvokeMethodAsync<TRequest, TResponse>(
            nodeId,
            endpoint,
            request,
            cancellationToken);
    }

    #endregion
}
