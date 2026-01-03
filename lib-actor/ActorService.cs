using BeyondImmersion.BannouService.Actor.Caching;
using BeyondImmersion.BannouService.Actor.Pool;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-actor.tests")]

namespace BeyondImmersion.BannouService.Actor;

/// <summary>
/// Implementation of the Actor service.
/// This class contains the business logic for all Actor operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>TENET T6 - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
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
    private readonly IActorPoolManager? _poolManager;

    private const string TEMPLATE_STORE = "actor-templates";
    private const string INSTANCE_STORE = "actor-instances";
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
    /// <param name="poolManager">Pool manager for distributed actor routing (null in bannou mode).</param>
    public ActorService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<ActorService> logger,
        ActorServiceConfiguration configuration,
        IActorRegistry actorRegistry,
        IActorRunnerFactory actorRunnerFactory,
        IEventConsumer eventConsumer,
        IBehaviorDocumentCache behaviorCache,
        IActorPoolManager? poolManager = null)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _actorRegistry = actorRegistry ?? throw new ArgumentNullException(nameof(actorRegistry));
        _actorRunnerFactory = actorRunnerFactory ?? throw new ArgumentNullException(nameof(actorRunnerFactory));
        _eventConsumer = eventConsumer ?? throw new ArgumentNullException(nameof(eventConsumer));
        _behaviorCache = behaviorCache ?? throw new ArgumentNullException(nameof(behaviorCache));
        _poolManager = poolManager; // Optional - only used in pool modes

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

            var templateStore = _stateStoreFactory.GetStore<ActorTemplateData>(TEMPLATE_STORE);
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
            var indexStore = _stateStoreFactory.GetStore<List<string>>(TEMPLATE_STORE);
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

            return (StatusCodes.Created, template.ToResponse());
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
            var templateStore = _stateStoreFactory.GetStore<ActorTemplateData>(TEMPLATE_STORE);
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
            var templateStore = _stateStoreFactory.GetStore<ActorTemplateData>(TEMPLATE_STORE);
            var indexStore = _stateStoreFactory.GetStore<List<string>>(TEMPLATE_STORE);

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
            var templateStore = _stateStoreFactory.GetStore<ActorTemplateData>(TEMPLATE_STORE);
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
            var templateStore = _stateStoreFactory.GetStore<ActorTemplateData>(TEMPLATE_STORE);
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
            var indexStore = _stateStoreFactory.GetStore<List<string>>(TEMPLATE_STORE);
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
            var templateStore = _stateStoreFactory.GetStore<ActorTemplateData>(TEMPLATE_STORE);
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
            if (_configuration.DeploymentMode != "bannou" && _poolManager != null)
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
                nodeId = "bannou-local";
                nodeAppId = "bannou";
                startedAt = runner.StartedAt;
            }
            else
            {
                // Pool mode: route to pool node
                if (_poolManager == null)
                {
                    _logger.LogError("Pool manager not available for deployment mode {Mode}", _configuration.DeploymentMode);
                    return (StatusCodes.InternalServerError, null);
                }

                // Acquire a pool node with capacity
                var poolNode = await _poolManager.AcquireNodeForActorAsync(template.Category, 1, cancellationToken);
                if (poolNode == null)
                {
                    _logger.LogWarning("No pool nodes with capacity available for category {Category}", template.Category);
                    return (StatusCodes.TooManyRequests, null);
                }

                // Record the assignment
                var assignment = new ActorAssignment
                {
                    ActorId = actorId,
                    NodeId = poolNode.NodeId,
                    NodeAppId = poolNode.AppId,
                    TemplateId = body.TemplateId.ToString(),
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

            return (StatusCodes.Created, new ActorInstanceResponse
            {
                ActorId = actorId,
                TemplateId = body.TemplateId,
                CharacterId = body.CharacterId,
                NodeId = nodeId,
                NodeAppId = nodeAppId,
                Status = _configuration.DeploymentMode == "bannou" ? ActorStatus.Running : ActorStatus.Pending,
                StartedAt = startedAt
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
    /// Gets an actor instance by ID.
    /// </summary>
    public async Task<(StatusCodes, ActorInstanceResponse?)> GetActorAsync(
        GetActorRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting actor {ActorId}", body.ActorId);

        try
        {
            if (!_actorRegistry.TryGet(body.ActorId, out var runner) || runner == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, runner.GetStateSnapshot().ToResponse(
                nodeId: _configuration.DeploymentMode == "bannou" ? "bannou-local" : null,
                nodeAppId: _configuration.DeploymentMode == "bannou" ? "bannou" : null));
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
                    NodeId = "bannou-local",
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
                if (_poolManager == null)
                {
                    _logger.LogError("Pool manager not available for deployment mode {Mode}", _configuration.DeploymentMode);
                    return (StatusCodes.InternalServerError, null);
                }

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
                    nodeId: _configuration.DeploymentMode == "bannou" ? "bannou-local" : null,
                    nodeAppId: _configuration.DeploymentMode == "bannou" ? "bannou" : null))
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
}
