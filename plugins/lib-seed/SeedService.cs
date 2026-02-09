using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Seed;

/// <summary>
/// Implementation of the Seed service. Provides generic progressive growth primitives
/// for game entities. Seeds start empty and grow by accumulating metadata from external
/// events, progressively gaining capabilities.
/// </summary>
[BannouService("seed", typeof(ISeedService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class SeedService : ISeedService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<SeedService> _logger;
    private readonly SeedServiceConfiguration _configuration;
    private readonly IEventConsumer _eventConsumer;
    private readonly IGameServiceClient _gameServiceClient;

    /// <summary>
    /// Creates a new instance of the SeedService.
    /// </summary>
    public SeedService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        IDistributedLockProvider lockProvider,
        ILogger<SeedService> logger,
        SeedServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IGameServiceClient gameServiceClient)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _lockProvider = lockProvider;
        _logger = logger;
        _configuration = configuration;
        _eventConsumer = eventConsumer;
        _gameServiceClient = gameServiceClient;

        RegisterEventConsumers(_eventConsumer);
    }

    // ========================================================================
    // SEED CRUD
    // ========================================================================

    /// <summary>
    /// Creates a new seed entity for the specified owner.
    /// Validates seed type exists, owner type is allowed, and per-owner limit not exceeded.
    /// </summary>
    public async Task<(StatusCodes, SeedResponse?)> CreateSeedAsync(CreateSeedRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating seed of type {SeedTypeCode} for owner {OwnerId} ({OwnerType})",
            body.SeedTypeCode, body.OwnerId, body.OwnerType);

        try
        {
            var typeStore = _stateStoreFactory.GetStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions);
            var typeKey = $"type:{body.GameServiceId}:{body.SeedTypeCode}";
            var seedType = await typeStore.GetAsync(typeKey, cancellationToken);

            if (seedType == null)
            {
                _logger.LogWarning("Seed type {SeedTypeCode} not found for game service {GameServiceId}",
                    body.SeedTypeCode, body.GameServiceId);
                return (StatusCodes.NotFound, null);
            }

            if (!seedType.AllowedOwnerTypes.Contains(body.OwnerType))
            {
                _logger.LogWarning("Owner type {OwnerType} not allowed for seed type {SeedTypeCode}",
                    body.OwnerType, body.SeedTypeCode);
                return (StatusCodes.BadRequest, null);
            }

            // Check per-owner limit
            var maxPerOwner = seedType.MaxPerOwner > 0 ? seedType.MaxPerOwner : _configuration.DefaultMaxSeedsPerOwner;
            var seedStore = _stateStoreFactory.GetJsonQueryableStore<SeedModel>(StateStoreDefinitions.Seed);
            var ownerConditions = new List<QueryCondition>
            {
                new QueryCondition { Path = "$.OwnerId", Operator = QueryOperator.Equals, Value = body.OwnerId.ToString() },
                new QueryCondition { Path = "$.OwnerType", Operator = QueryOperator.Equals, Value = body.OwnerType },
                new QueryCondition { Path = "$.SeedTypeCode", Operator = QueryOperator.Equals, Value = body.SeedTypeCode },
                new QueryCondition { Path = "$.GameServiceId", Operator = QueryOperator.Equals, Value = body.GameServiceId.ToString() },
                new QueryCondition { Path = "$.SeedId", Operator = QueryOperator.Exists, Value = true }
            };
            var existing = await seedStore.JsonQueryPagedAsync(ownerConditions, 0, 1, null, cancellationToken);

            if (existing.TotalCount >= maxPerOwner)
            {
                _logger.LogWarning("Owner {OwnerId} already has {Count} seeds of type {SeedTypeCode} (max {Max})",
                    body.OwnerId, existing.TotalCount, body.SeedTypeCode, maxPerOwner);
                return (StatusCodes.Conflict, null);
            }

            // Determine initial phase (first phase in the type definition)
            var initialPhase = seedType.GrowthPhases.Count > 0
                ? seedType.GrowthPhases[0].PhaseCode
                : "initial";

            var seed = new SeedModel
            {
                SeedId = Guid.NewGuid(),
                OwnerId = body.OwnerId,
                OwnerType = body.OwnerType,
                SeedTypeCode = body.SeedTypeCode,
                GameServiceId = body.GameServiceId,
                CreatedAt = DateTimeOffset.UtcNow,
                GrowthPhase = initialPhase,
                TotalGrowth = 0f,
                BondId = null,
                DisplayName = body.DisplayName ?? $"{body.SeedTypeCode}-{Guid.NewGuid():N}"[..20],
                Status = SeedStatus.Active,
                Metadata = body.Metadata != null ? new Dictionary<string, object> { ["data"] = body.Metadata } : null
            };

            var rawStore = _stateStoreFactory.GetStore<SeedModel>(StateStoreDefinitions.Seed);
            await rawStore.SaveAsync($"seed:{seed.SeedId}", seed, cancellationToken: cancellationToken);

            // Initialize empty growth record
            var growthStore = _stateStoreFactory.GetStore<SeedGrowthModel>(StateStoreDefinitions.SeedGrowth);
            var growth = new SeedGrowthModel { SeedId = seed.SeedId, Domains = new() };
            await growthStore.SaveAsync($"growth:{seed.SeedId}", growth, cancellationToken: cancellationToken);

            // Publish lifecycle event
            await _messageBus.TryPublishAsync("seed.created", new SeedCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = seed.CreatedAt,
                SeedId = seed.SeedId,
                OwnerId = seed.OwnerId,
                OwnerType = seed.OwnerType,
                SeedTypeCode = seed.SeedTypeCode,
                GameServiceId = seed.GameServiceId,
                GrowthPhase = seed.GrowthPhase,
                TotalGrowth = seed.TotalGrowth,
                DisplayName = seed.DisplayName,
                Status = seed.Status,
                BondId = seed.BondId
            }, cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapToResponse(seed));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating seed of type {SeedTypeCode}", body.SeedTypeCode);
            await _messageBus.TryPublishErrorAsync("seed", "CreateSeed", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/create", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Retrieves a seed by its unique identifier.
    /// </summary>
    public async Task<(StatusCodes, SeedResponse?)> GetSeedAsync(GetSeedRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var store = _stateStoreFactory.GetStore<SeedModel>(StateStoreDefinitions.Seed);
            var seed = await store.GetAsync($"seed:{body.SeedId}", cancellationToken);

            if (seed == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapToResponse(seed));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting seed {SeedId}", body.SeedId);
            await _messageBus.TryPublishErrorAsync("seed", "GetSeed", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/get", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Retrieves all seeds for a given owner, with optional type and archive filtering.
    /// </summary>
    public async Task<(StatusCodes, ListSeedsResponse?)> GetSeedsByOwnerAsync(GetSeedsByOwnerRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var store = _stateStoreFactory.GetJsonQueryableStore<SeedModel>(StateStoreDefinitions.Seed);
            var conditions = new List<QueryCondition>
            {
                new QueryCondition { Path = "$.OwnerId", Operator = QueryOperator.Equals, Value = body.OwnerId.ToString() },
                new QueryCondition { Path = "$.OwnerType", Operator = QueryOperator.Equals, Value = body.OwnerType },
                new QueryCondition { Path = "$.SeedId", Operator = QueryOperator.Exists, Value = true }
            };

            if (!string.IsNullOrEmpty(body.SeedTypeCode))
            {
                conditions.Add(new QueryCondition { Path = "$.SeedTypeCode", Operator = QueryOperator.Equals, Value = body.SeedTypeCode });
            }

            if (!body.IncludeArchived)
            {
                conditions.Add(new QueryCondition { Path = "$.Status", Operator = QueryOperator.NotEquals, Value = SeedStatus.Archived.ToString() });
            }

            var result = await store.JsonQueryPagedAsync(conditions, 0, 100, null, cancellationToken);
            var seeds = result.Items.Select(r => MapToResponse(r.Value)).ToList();

            return (StatusCodes.OK, new ListSeedsResponse
            {
                Seeds = seeds,
                TotalCount = (int)result.TotalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting seeds for owner {OwnerId}", body.OwnerId);
            await _messageBus.TryPublishErrorAsync("seed", "GetSeedsByOwner", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/get-by-owner", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Lists seeds with server-side pagination and optional filters.
    /// </summary>
    public async Task<(StatusCodes, ListSeedsResponse?)> ListSeedsAsync(ListSeedsRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var store = _stateStoreFactory.GetJsonQueryableStore<SeedModel>(StateStoreDefinitions.Seed);
            var conditions = new List<QueryCondition>
            {
                new QueryCondition { Path = "$.SeedId", Operator = QueryOperator.Exists, Value = true }
            };

            if (!string.IsNullOrEmpty(body.SeedTypeCode))
                conditions.Add(new QueryCondition { Path = "$.SeedTypeCode", Operator = QueryOperator.Equals, Value = body.SeedTypeCode });
            if (!string.IsNullOrEmpty(body.OwnerType))
                conditions.Add(new QueryCondition { Path = "$.OwnerType", Operator = QueryOperator.Equals, Value = body.OwnerType });
            if (body.GameServiceId.HasValue)
                conditions.Add(new QueryCondition { Path = "$.GameServiceId", Operator = QueryOperator.Equals, Value = body.GameServiceId.Value.ToString() });
            if (!string.IsNullOrEmpty(body.GrowthPhase))
                conditions.Add(new QueryCondition { Path = "$.GrowthPhase", Operator = QueryOperator.Equals, Value = body.GrowthPhase });
            if (body.Status.HasValue)
                conditions.Add(new QueryCondition { Path = "$.Status", Operator = QueryOperator.Equals, Value = body.Status.Value.ToString() });

            var offset = (body.Page - 1) * body.PageSize;
            var result = await store.JsonQueryPagedAsync(conditions, offset, body.PageSize, null, cancellationToken);
            var seeds = result.Items.Select(r => MapToResponse(r.Value)).ToList();

            return (StatusCodes.OK, new ListSeedsResponse
            {
                Seeds = seeds,
                TotalCount = (int)result.TotalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing seeds");
            await _messageBus.TryPublishErrorAsync("seed", "ListSeeds", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/list", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Updates mutable fields on a seed (display name, metadata).
    /// </summary>
    public async Task<(StatusCodes, SeedResponse?)> UpdateSeedAsync(UpdateSeedRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var lockOwner = $"update-seed-{Guid.NewGuid():N}";
            await using var lockResponse = await _lockProvider.LockAsync(
                StateStoreDefinitions.SeedLock, body.SeedId.ToString(), lockOwner, 10, cancellationToken);

            if (!lockResponse.Success)
            {
                return (StatusCodes.Conflict, null);
            }

            var store = _stateStoreFactory.GetStore<SeedModel>(StateStoreDefinitions.Seed);
            var key = $"seed:{body.SeedId}";
            var seed = await store.GetAsync(key, cancellationToken);

            if (seed == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var changedFields = new List<string>();

            if (body.DisplayName != null)
            {
                seed.DisplayName = body.DisplayName;
                changedFields.Add("displayName");
            }

            if (body.Metadata != null)
            {
                seed.Metadata ??= new Dictionary<string, object>();
                seed.Metadata["data"] = body.Metadata;
                changedFields.Add("metadata");
            }

            await store.SaveAsync(key, seed, cancellationToken: cancellationToken);

            await _messageBus.TryPublishAsync("seed.updated", new SeedUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SeedId = seed.SeedId,
                OwnerId = seed.OwnerId,
                OwnerType = seed.OwnerType,
                SeedTypeCode = seed.SeedTypeCode,
                GameServiceId = seed.GameServiceId,
                GrowthPhase = seed.GrowthPhase,
                TotalGrowth = seed.TotalGrowth,
                DisplayName = seed.DisplayName,
                Status = seed.Status,
                BondId = seed.BondId,
                ChangedFields = changedFields
            }, cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapToResponse(seed));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating seed {SeedId}", body.SeedId);
            await _messageBus.TryPublishErrorAsync("seed", "UpdateSeed", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/update", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Activates a seed, deactivating any previously active seed of the same type for the same owner.
    /// </summary>
    public async Task<(StatusCodes, SeedResponse?)> ActivateSeedAsync(ActivateSeedRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var store = _stateStoreFactory.GetStore<SeedModel>(StateStoreDefinitions.Seed);
            var key = $"seed:{body.SeedId}";
            var seed = await store.GetAsync(key, cancellationToken);

            if (seed == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (seed.Status == SeedStatus.Active)
            {
                return (StatusCodes.OK, MapToResponse(seed));
            }

            if (seed.Status == SeedStatus.Archived)
            {
                return (StatusCodes.BadRequest, null);
            }

            var lockOwner = $"activate-seed-{Guid.NewGuid():N}";
            await using var lockResponse = await _lockProvider.LockAsync(
                StateStoreDefinitions.SeedLock, $"owner:{seed.OwnerId}:{seed.SeedTypeCode}",
                lockOwner, 10, cancellationToken);

            if (!lockResponse.Success)
            {
                return (StatusCodes.Conflict, null);
            }

            // Find and deactivate current active seed of same type for same owner
            Guid? previousActiveSeedId = null;
            var queryStore = _stateStoreFactory.GetJsonQueryableStore<SeedModel>(StateStoreDefinitions.Seed);
            var conditions = new List<QueryCondition>
            {
                new QueryCondition { Path = "$.OwnerId", Operator = QueryOperator.Equals, Value = seed.OwnerId.ToString() },
                new QueryCondition { Path = "$.OwnerType", Operator = QueryOperator.Equals, Value = seed.OwnerType },
                new QueryCondition { Path = "$.SeedTypeCode", Operator = QueryOperator.Equals, Value = seed.SeedTypeCode },
                new QueryCondition { Path = "$.GameServiceId", Operator = QueryOperator.Equals, Value = seed.GameServiceId.ToString() },
                new QueryCondition { Path = "$.Status", Operator = QueryOperator.Equals, Value = SeedStatus.Active.ToString() },
                new QueryCondition { Path = "$.SeedId", Operator = QueryOperator.Exists, Value = true }
            };
            var activeSeeds = await queryStore.JsonQueryPagedAsync(conditions, 0, 10, null, cancellationToken);

            foreach (var activeSeed in activeSeeds.Items)
            {
                if (activeSeed.Value.SeedId != seed.SeedId)
                {
                    previousActiveSeedId = activeSeed.Value.SeedId;
                    activeSeed.Value.Status = SeedStatus.Dormant;
                    await store.SaveAsync($"seed:{activeSeed.Value.SeedId}", activeSeed.Value,
                        cancellationToken: cancellationToken);
                }
            }

            seed.Status = SeedStatus.Active;
            await store.SaveAsync(key, seed, cancellationToken: cancellationToken);

            await _messageBus.TryPublishAsync("seed.activated", new SeedActivatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SeedId = seed.SeedId,
                OwnerId = seed.OwnerId,
                OwnerType = seed.OwnerType,
                SeedTypeCode = seed.SeedTypeCode,
                PreviousActiveSeedId = previousActiveSeedId
            }, cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapToResponse(seed));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating seed {SeedId}", body.SeedId);
            await _messageBus.TryPublishErrorAsync("seed", "ActivateSeed", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/activate", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Archives a seed. Active seeds cannot be archived (must be deactivated first).
    /// </summary>
    public async Task<(StatusCodes, SeedResponse?)> ArchiveSeedAsync(ArchiveSeedRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var store = _stateStoreFactory.GetStore<SeedModel>(StateStoreDefinitions.Seed);
            var key = $"seed:{body.SeedId}";
            var seed = await store.GetAsync(key, cancellationToken);

            if (seed == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (seed.Status == SeedStatus.Active)
            {
                _logger.LogWarning("Cannot archive active seed {SeedId}, deactivate first", body.SeedId);
                return (StatusCodes.BadRequest, null);
            }

            if (seed.Status == SeedStatus.Archived)
            {
                return (StatusCodes.OK, MapToResponse(seed));
            }

            seed.Status = SeedStatus.Archived;
            await store.SaveAsync(key, seed, cancellationToken: cancellationToken);

            await _messageBus.TryPublishAsync("seed.archived", new SeedArchivedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SeedId = seed.SeedId,
                OwnerId = seed.OwnerId,
                OwnerType = seed.OwnerType,
                SeedTypeCode = seed.SeedTypeCode
            }, cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapToResponse(seed));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving seed {SeedId}", body.SeedId);
            await _messageBus.TryPublishErrorAsync("seed", "ArchiveSeed", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/archive", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    // ========================================================================
    // GROWTH
    // ========================================================================

    /// <summary>
    /// Retrieves the growth domain data for a seed.
    /// </summary>
    public async Task<(StatusCodes, GrowthResponse?)> GetGrowthAsync(GetGrowthRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var growthStore = _stateStoreFactory.GetStore<SeedGrowthModel>(StateStoreDefinitions.SeedGrowth);
            var growth = await growthStore.GetAsync($"growth:{body.SeedId}", cancellationToken);

            if (growth == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var domains = new Dictionary<string, float>(growth.Domains);

            // Apply decay if enabled (deferred feature #352 - config wires up the knobs)
            if (_configuration.GrowthDecayEnabled)
            {
                var seedStore = _stateStoreFactory.GetStore<SeedModel>(StateStoreDefinitions.Seed);
                var seed = await seedStore.GetAsync($"seed:{body.SeedId}", cancellationToken);
                if (seed != null)
                {
                    var daysSinceCreation = (DateTimeOffset.UtcNow - seed.CreatedAt).TotalDays;
                    var decayFactor = Math.Max(0f, 1f - (float)(daysSinceCreation * _configuration.GrowthDecayRatePerDay));
                    foreach (var key in domains.Keys.ToList())
                    {
                        domains[key] *= decayFactor;
                    }
                }
            }

            return (StatusCodes.OK, new GrowthResponse
            {
                SeedId = growth.SeedId,
                TotalGrowth = domains.Values.Sum(),
                Domains = domains
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting growth for seed {SeedId}", body.SeedId);
            await _messageBus.TryPublishErrorAsync("seed", "GetGrowth", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/growth/get", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Records growth in a specific domain for a seed. Checks for phase transitions.
    /// </summary>
    public async Task<(StatusCodes, GrowthResponse?)> RecordGrowthAsync(RecordGrowthRequest body, CancellationToken cancellationToken)
    {
        try
        {
            return await RecordGrowthInternalAsync(
                body.SeedId, new[] { (body.Domain, body.Amount) }, body.Source, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording growth for seed {SeedId}", body.SeedId);
            await _messageBus.TryPublishErrorAsync("seed", "RecordGrowth", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/growth/record", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Records growth across multiple domains atomically for a seed.
    /// </summary>
    public async Task<(StatusCodes, GrowthResponse?)> RecordGrowthBatchAsync(RecordGrowthBatchRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var entries = body.Entries.Select(e => (e.Domain, e.Amount)).ToArray();
            return await RecordGrowthInternalAsync(body.SeedId, entries, body.Source, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording batch growth for seed {SeedId}", body.SeedId);
            await _messageBus.TryPublishErrorAsync("seed", "RecordGrowthBatch", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/growth/record-batch", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets the current growth phase for a seed, including progress toward the next phase.
    /// </summary>
    public async Task<(StatusCodes, GrowthPhaseResponse?)> GetGrowthPhaseAsync(GetGrowthPhaseRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var seedStore = _stateStoreFactory.GetStore<SeedModel>(StateStoreDefinitions.Seed);
            var seed = await seedStore.GetAsync($"seed:{body.SeedId}", cancellationToken);

            if (seed == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var typeStore = _stateStoreFactory.GetStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions);
            var seedType = await typeStore.GetAsync($"type:{seed.GameServiceId}:{seed.SeedTypeCode}", cancellationToken);

            if (seedType == null)
            {
                _logger.LogError("Seed type {SeedTypeCode} not found for seed {SeedId}", seed.SeedTypeCode, seed.SeedId);
                return (StatusCodes.InternalServerError, null);
            }

            var (currentPhase, nextPhase) = ComputePhaseInfo(seedType.GrowthPhases, seed.TotalGrowth);

            return (StatusCodes.OK, new GrowthPhaseResponse
            {
                SeedId = seed.SeedId,
                PhaseCode = currentPhase.PhaseCode,
                DisplayName = currentPhase.DisplayName,
                TotalGrowth = seed.TotalGrowth,
                NextPhaseCode = nextPhase?.PhaseCode,
                NextPhaseThreshold = nextPhase?.MinTotalGrowth
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting growth phase for seed {SeedId}", body.SeedId);
            await _messageBus.TryPublishErrorAsync("seed", "GetGrowthPhase", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/growth/get-phase", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    // ========================================================================
    // CAPABILITIES
    // ========================================================================

    /// <summary>
    /// Gets the computed capability manifest for a seed. Uses Redis cache with computation on miss.
    /// </summary>
    public async Task<(StatusCodes, CapabilityManifestResponse?)> GetCapabilityManifestAsync(GetCapabilityManifestRequest body, CancellationToken cancellationToken)
    {
        try
        {
            // Check cache first; honor debounce window from configuration
            var cacheStore = _stateStoreFactory.GetStore<CapabilityManifestModel>(StateStoreDefinitions.SeedCapabilitiesCache);
            var cached = await cacheStore.GetAsync($"cap:{body.SeedId}", cancellationToken);
            var debounceWindow = TimeSpan.FromMilliseconds(_configuration.CapabilityRecomputeDebounceMs);

            if (cached != null && (DateTimeOffset.UtcNow - cached.ComputedAt) < debounceWindow)
            {
                return (StatusCodes.OK, MapManifestToResponse(cached));
            }

            // Cache miss - compute
            var seedStore = _stateStoreFactory.GetStore<SeedModel>(StateStoreDefinitions.Seed);
            var seed = await seedStore.GetAsync($"seed:{body.SeedId}", cancellationToken);

            if (seed == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var manifest = await ComputeAndCacheManifestAsync(seed, cancellationToken);
            return (StatusCodes.OK, MapManifestToResponse(manifest));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting capability manifest for seed {SeedId}", body.SeedId);
            await _messageBus.TryPublishErrorAsync("seed", "GetCapabilityManifest", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/capability/get-manifest", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    // ========================================================================
    // SEED TYPE DEFINITIONS
    // ========================================================================

    /// <summary>
    /// Registers a new seed type definition for a game service.
    /// </summary>
    public async Task<(StatusCodes, SeedTypeResponse?)> RegisterSeedTypeAsync(RegisterSeedTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Registering seed type {SeedTypeCode} for game service {GameServiceId}",
            body.SeedTypeCode, body.GameServiceId);

        try
        {
            var store = _stateStoreFactory.GetStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions);
            var key = $"type:{body.GameServiceId}:{body.SeedTypeCode}";

            var existing = await store.GetAsync(key, cancellationToken);
            if (existing != null)
            {
                _logger.LogWarning("Seed type {SeedTypeCode} already exists for game service {GameServiceId}",
                    body.SeedTypeCode, body.GameServiceId);
                return (StatusCodes.Conflict, null);
            }

            // Check max types per game service
            var queryStore = _stateStoreFactory.GetJsonQueryableStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions);
            var conditions = new List<QueryCondition>
            {
                new QueryCondition { Path = "$.GameServiceId", Operator = QueryOperator.Equals, Value = body.GameServiceId.ToString() },
                new QueryCondition { Path = "$.SeedTypeCode", Operator = QueryOperator.Exists, Value = true }
            };
            var typeCount = await queryStore.JsonQueryPagedAsync(conditions, 0, 1, null, cancellationToken);

            if (typeCount.TotalCount >= _configuration.MaxSeedTypesPerGameService)
            {
                _logger.LogWarning("Game service {GameServiceId} has reached max seed types ({Max})",
                    body.GameServiceId, _configuration.MaxSeedTypesPerGameService);
                return (StatusCodes.Conflict, null);
            }

            var model = new SeedTypeDefinitionModel
            {
                SeedTypeCode = body.SeedTypeCode,
                GameServiceId = body.GameServiceId,
                DisplayName = body.DisplayName,
                Description = body.Description,
                MaxPerOwner = body.MaxPerOwner,
                AllowedOwnerTypes = body.AllowedOwnerTypes.ToList(),
                GrowthPhases = body.GrowthPhases.ToList(),
                BondCardinality = body.BondCardinality,
                BondPermanent = body.BondPermanent,
                CapabilityRules = body.CapabilityRules?.ToList()
            };

            await store.SaveAsync(key, model, cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapTypeToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering seed type {SeedTypeCode}", body.SeedTypeCode);
            await _messageBus.TryPublishErrorAsync("seed", "RegisterSeedType", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/type/register", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Retrieves a seed type definition by code and game service.
    /// </summary>
    public async Task<(StatusCodes, SeedTypeResponse?)> GetSeedTypeAsync(GetSeedTypeRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var store = _stateStoreFactory.GetStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions);
            var seedType = await store.GetAsync($"type:{body.GameServiceId}:{body.SeedTypeCode}", cancellationToken);

            if (seedType == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapTypeToResponse(seedType));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting seed type {SeedTypeCode}", body.SeedTypeCode);
            await _messageBus.TryPublishErrorAsync("seed", "GetSeedType", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/type/get", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Lists all seed types registered for a game service.
    /// </summary>
    public async Task<(StatusCodes, ListSeedTypesResponse?)> ListSeedTypesAsync(ListSeedTypesRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var store = _stateStoreFactory.GetJsonQueryableStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions);
            var conditions = new List<QueryCondition>
            {
                new QueryCondition { Path = "$.GameServiceId", Operator = QueryOperator.Equals, Value = body.GameServiceId.ToString() },
                new QueryCondition { Path = "$.SeedTypeCode", Operator = QueryOperator.Exists, Value = true }
            };

            var result = await store.JsonQueryPagedAsync(conditions, 0, 100, null, cancellationToken);
            var types = result.Items.Select(r => MapTypeToResponse(r.Value)).ToList();

            return (StatusCodes.OK, new ListSeedTypesResponse { SeedTypes = types });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing seed types for game service {GameServiceId}", body.GameServiceId);
            await _messageBus.TryPublishErrorAsync("seed", "ListSeedTypes", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/type/list", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Updates an existing seed type definition.
    /// </summary>
    public async Task<(StatusCodes, SeedTypeResponse?)> UpdateSeedTypeAsync(UpdateSeedTypeRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var store = _stateStoreFactory.GetStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions);
            var key = $"type:{body.GameServiceId}:{body.SeedTypeCode}";
            var seedType = await store.GetAsync(key, cancellationToken);

            if (seedType == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (body.DisplayName != null)
                seedType.DisplayName = body.DisplayName;
            if (body.Description != null)
                seedType.Description = body.Description;
            if (body.MaxPerOwner.HasValue)
                seedType.MaxPerOwner = body.MaxPerOwner.Value;
            if (body.GrowthPhases != null)
                seedType.GrowthPhases = body.GrowthPhases.ToList();
            if (body.CapabilityRules != null)
                seedType.CapabilityRules = body.CapabilityRules.ToList();

            await store.SaveAsync(key, seedType, cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapTypeToResponse(seedType));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating seed type {SeedTypeCode}", body.SeedTypeCode);
            await _messageBus.TryPublishErrorAsync("seed", "UpdateSeedType", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/type/update", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    // ========================================================================
    // BONDS
    // ========================================================================

    /// <summary>
    /// Initiates a bond between two seeds. Both must be the same type and support bonding.
    /// </summary>
    public async Task<(StatusCodes, BondResponse?)> InitiateBondAsync(InitiateBondRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initiating bond between seeds {InitiatorId} and {TargetId}",
            body.InitiatorSeedId, body.TargetSeedId);

        try
        {
            var seedStore = _stateStoreFactory.GetStore<SeedModel>(StateStoreDefinitions.Seed);
            var initiator = await seedStore.GetAsync($"seed:{body.InitiatorSeedId}", cancellationToken);
            var target = await seedStore.GetAsync($"seed:{body.TargetSeedId}", cancellationToken);

            if (initiator == null || target == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (initiator.SeedTypeCode != target.SeedTypeCode)
            {
                _logger.LogWarning("Cannot bond seeds of different types: {TypeA} vs {TypeB}",
                    initiator.SeedTypeCode, target.SeedTypeCode);
                return (StatusCodes.BadRequest, null);
            }

            // Check seed type allows bonding
            var typeStore = _stateStoreFactory.GetStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions);
            var seedType = await typeStore.GetAsync(
                $"type:{initiator.GameServiceId}:{initiator.SeedTypeCode}", cancellationToken);

            if (seedType == null || seedType.BondCardinality < 1)
            {
                _logger.LogWarning("Seed type {SeedTypeCode} does not support bonding", initiator.SeedTypeCode);
                return (StatusCodes.BadRequest, null);
            }

            // Check neither seed is already bonded
            if (initiator.BondId.HasValue || target.BondId.HasValue)
            {
                _logger.LogWarning("One or both seeds are already bonded");
                return (StatusCodes.Conflict, null);
            }

            var now = DateTimeOffset.UtcNow;
            var bond = new SeedBondModel
            {
                BondId = Guid.NewGuid(),
                SeedTypeCode = initiator.SeedTypeCode,
                Participants = new List<BondParticipantEntry>
                {
                    new()
                    {
                        SeedId = body.InitiatorSeedId,
                        JoinedAt = now,
                        Role = "initiator",
                        Confirmed = true
                    },
                    new()
                    {
                        SeedId = body.TargetSeedId,
                        JoinedAt = now,
                        Role = null,
                        Confirmed = false
                    }
                },
                CreatedAt = now,
                Status = BondStatus.PendingConfirmation,
                BondStrength = 0f,
                SharedGrowth = 0f,
                Permanent = seedType.BondPermanent
            };

            var bondStore = _stateStoreFactory.GetStore<SeedBondModel>(StateStoreDefinitions.SeedBonds);
            await bondStore.SaveAsync($"bond:{bond.BondId}", bond, cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapBondToResponse(bond));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating bond");
            await _messageBus.TryPublishErrorAsync("seed", "InitiateBond", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/bond/initiate", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Confirms a pending bond. When all participants confirm, the bond becomes active.
    /// </summary>
    public async Task<(StatusCodes, BondResponse?)> ConfirmBondAsync(ConfirmBondRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var bondStore = _stateStoreFactory.GetStore<SeedBondModel>(StateStoreDefinitions.SeedBonds);
            var bondKey = $"bond:{body.BondId}";
            var bond = await bondStore.GetAsync(bondKey, cancellationToken);

            if (bond == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (bond.Status != BondStatus.PendingConfirmation)
            {
                return (StatusCodes.BadRequest, null);
            }

            var participant = bond.Participants.FirstOrDefault(p => p.SeedId == body.ConfirmingSeedId);
            if (participant == null)
            {
                return (StatusCodes.BadRequest, null);
            }

            participant.Confirmed = true;

            // Check if all participants have confirmed
            var allConfirmed = bond.Participants.All(p => p.Confirmed);
            if (allConfirmed)
            {
                bond.Status = BondStatus.Active;

                // Update seeds with bond reference
                var seedStore = _stateStoreFactory.GetStore<SeedModel>(StateStoreDefinitions.Seed);
                foreach (var p in bond.Participants)
                {
                    var seed = await seedStore.GetAsync($"seed:{p.SeedId}", cancellationToken);
                    if (seed != null)
                    {
                        seed.BondId = bond.BondId;
                        await seedStore.SaveAsync($"seed:{seed.SeedId}", seed, cancellationToken: cancellationToken);
                    }
                }

                await _messageBus.TryPublishAsync("seed.bond.formed", new SeedBondFormedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    BondId = bond.BondId,
                    SeedTypeCode = bond.SeedTypeCode,
                    ParticipantSeedIds = bond.Participants.Select(p => p.SeedId).ToList()
                }, cancellationToken: cancellationToken);
            }

            await bondStore.SaveAsync(bondKey, bond, cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapBondToResponse(bond));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming bond {BondId}", body.BondId);
            await _messageBus.TryPublishErrorAsync("seed", "ConfirmBond", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/bond/confirm", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Retrieves a bond by its unique identifier.
    /// </summary>
    public async Task<(StatusCodes, BondResponse?)> GetBondAsync(GetBondRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var store = _stateStoreFactory.GetStore<SeedBondModel>(StateStoreDefinitions.SeedBonds);
            var bond = await store.GetAsync($"bond:{body.BondId}", cancellationToken);

            if (bond == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapBondToResponse(bond));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bond {BondId}", body.BondId);
            await _messageBus.TryPublishErrorAsync("seed", "GetBond", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/bond/get", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Retrieves the bond for a specific seed, if any.
    /// </summary>
    public async Task<(StatusCodes, BondResponse?)> GetBondForSeedAsync(GetBondForSeedRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var seedStore = _stateStoreFactory.GetStore<SeedModel>(StateStoreDefinitions.Seed);
            var seed = await seedStore.GetAsync($"seed:{body.SeedId}", cancellationToken);

            if (seed == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (!seed.BondId.HasValue)
            {
                return (StatusCodes.NotFound, null);
            }

            var bondStore = _stateStoreFactory.GetStore<SeedBondModel>(StateStoreDefinitions.SeedBonds);
            var bond = await bondStore.GetAsync($"bond:{seed.BondId.Value}", cancellationToken);

            if (bond == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapBondToResponse(bond));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bond for seed {SeedId}", body.SeedId);
            await _messageBus.TryPublishErrorAsync("seed", "GetBondForSeed", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/bond/get-for-seed", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets partner seed summaries for a bond associated with a seed.
    /// </summary>
    public async Task<(StatusCodes, BondPartnersResponse?)> GetBondPartnersAsync(GetBondPartnersRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var seedStore = _stateStoreFactory.GetStore<SeedModel>(StateStoreDefinitions.Seed);
            var seed = await seedStore.GetAsync($"seed:{body.SeedId}", cancellationToken);

            if (seed == null || !seed.BondId.HasValue)
            {
                return (StatusCodes.NotFound, null);
            }

            var bondStore = _stateStoreFactory.GetStore<SeedBondModel>(StateStoreDefinitions.SeedBonds);
            var bond = await bondStore.GetAsync($"bond:{seed.BondId.Value}", cancellationToken);

            if (bond == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var partners = new List<PartnerSummary>();
            foreach (var participant in bond.Participants.Where(p => p.SeedId != body.SeedId))
            {
                var partnerSeed = await seedStore.GetAsync($"seed:{participant.SeedId}", cancellationToken);
                if (partnerSeed != null)
                {
                    partners.Add(new PartnerSummary
                    {
                        SeedId = partnerSeed.SeedId,
                        OwnerId = partnerSeed.OwnerId,
                        OwnerType = partnerSeed.OwnerType,
                        GrowthPhase = partnerSeed.GrowthPhase,
                        Status = partnerSeed.Status
                    });
                }
            }

            return (StatusCodes.OK, new BondPartnersResponse
            {
                BondId = bond.BondId,
                Partners = partners
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bond partners for seed {SeedId}", body.SeedId);
            await _messageBus.TryPublishErrorAsync("seed", "GetBondPartners", "unexpected_exception", ex.Message,
                dependency: null, endpoint: "post:/seed/bond/get-partners", details: null, stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    // ========================================================================
    // PRIVATE HELPERS
    // ========================================================================

    /// <summary>
    /// Internal helper for recording growth across one or more domains atomically.
    /// Handles locking, phase transitions, bond shared growth, and event publishing.
    /// </summary>
    private async Task<(StatusCodes, GrowthResponse?)> RecordGrowthInternalAsync(
        Guid seedId, (string Domain, float Amount)[] entries, string source, CancellationToken cancellationToken)
    {
        var lockOwner = $"growth-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.SeedLock, seedId.ToString(), lockOwner, 10, cancellationToken);

        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var seedStore = _stateStoreFactory.GetStore<SeedModel>(StateStoreDefinitions.Seed);
        var seed = await seedStore.GetAsync($"seed:{seedId}", cancellationToken);

        if (seed == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (seed.Status != SeedStatus.Active)
        {
            _logger.LogWarning("Cannot record growth for non-active seed {SeedId} (status: {Status})",
                seedId, seed.Status);
            return (StatusCodes.BadRequest, null);
        }

        var growthStore = _stateStoreFactory.GetStore<SeedGrowthModel>(StateStoreDefinitions.SeedGrowth);
        var growth = await growthStore.GetAsync($"growth:{seedId}", cancellationToken);
        growth ??= new SeedGrowthModel { SeedId = seedId, Domains = new() };

        // Apply bond shared growth multiplier if bonded
        var multiplier = 1.0f;
        if (seed.BondId.HasValue)
        {
            multiplier = (float)_configuration.BondSharedGrowthMultiplier;

            // Update bond shared growth
            var bondStore = _stateStoreFactory.GetStore<SeedBondModel>(StateStoreDefinitions.SeedBonds);
            var bond = await bondStore.GetAsync($"bond:{seed.BondId.Value}", cancellationToken);
            if (bond is { Status: BondStatus.Active })
            {
                var totalAmount = entries.Sum(e => e.Amount);
                bond.SharedGrowth += totalAmount;
                bond.BondStrength += totalAmount * 0.1f;
                await bondStore.SaveAsync($"bond:{bond.BondId}", bond, cancellationToken: cancellationToken);
            }
        }

        // Record growth in each domain
        foreach (var (domain, amount) in entries)
        {
            var adjustedAmount = amount * multiplier;
            var previousDepth = growth.Domains.GetValueOrDefault(domain, 0f);
            growth.Domains[domain] = previousDepth + adjustedAmount;

            await _messageBus.TryPublishAsync("seed.growth.updated", new SeedGrowthUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SeedId = seedId,
                SeedTypeCode = seed.SeedTypeCode,
                Domain = domain,
                PreviousDepth = previousDepth,
                NewDepth = growth.Domains[domain],
                TotalGrowth = growth.Domains.Values.Sum()
            }, cancellationToken: cancellationToken);
        }

        await growthStore.SaveAsync($"growth:{seedId}", growth, cancellationToken: cancellationToken);

        // Update seed total growth and check phase transition
        var newTotalGrowth = growth.Domains.Values.Sum();
        var previousPhase = seed.GrowthPhase;
        seed.TotalGrowth = newTotalGrowth;

        // Load type definition for phase check
        var typeStore = _stateStoreFactory.GetStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions);
        var seedType = await typeStore.GetAsync($"type:{seed.GameServiceId}:{seed.SeedTypeCode}", cancellationToken);

        if (seedType != null)
        {
            var (currentPhase, _) = ComputePhaseInfo(seedType.GrowthPhases, newTotalGrowth);
            seed.GrowthPhase = currentPhase.PhaseCode;
        }

        await seedStore.SaveAsync($"seed:{seedId}", seed, cancellationToken: cancellationToken);

        // Publish phase change event if phase transitioned
        if (previousPhase != seed.GrowthPhase)
        {
            _logger.LogInformation("Seed {SeedId} transitioned from phase {OldPhase} to {NewPhase}",
                seedId, previousPhase, seed.GrowthPhase);

            await _messageBus.TryPublishAsync("seed.phase.changed", new SeedPhaseChangedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SeedId = seedId,
                SeedTypeCode = seed.SeedTypeCode,
                PreviousPhase = previousPhase,
                NewPhase = seed.GrowthPhase,
                TotalGrowth = newTotalGrowth
            }, cancellationToken: cancellationToken);
        }

        // Invalidate capability cache so next read recomputes
        var cacheStore = _stateStoreFactory.GetStore<CapabilityManifestModel>(StateStoreDefinitions.SeedCapabilitiesCache);
        await cacheStore.DeleteAsync($"cap:{seedId}", cancellationToken);

        return (StatusCodes.OK, new GrowthResponse
        {
            SeedId = seedId,
            TotalGrowth = newTotalGrowth,
            Domains = new Dictionary<string, float>(growth.Domains)
        });
    }

    /// <summary>
    /// Computes the current and next growth phase based on total growth and phase definitions.
    /// </summary>
    private static (GrowthPhaseDefinition Current, GrowthPhaseDefinition? Next) ComputePhaseInfo(
        List<GrowthPhaseDefinition> phases, float totalGrowth)
    {
        if (phases.Count == 0)
        {
            return (new GrowthPhaseDefinition { PhaseCode = "initial", DisplayName = "Initial", MinTotalGrowth = 0 }, null);
        }

        var sorted = phases.OrderBy(p => p.MinTotalGrowth).ToList();
        GrowthPhaseDefinition current = sorted[0];
        GrowthPhaseDefinition? next = sorted.Count > 1 ? sorted[1] : null;

        for (var i = 0; i < sorted.Count; i++)
        {
            if (totalGrowth >= sorted[i].MinTotalGrowth)
            {
                current = sorted[i];
                next = i + 1 < sorted.Count ? sorted[i + 1] : null;
            }
            else
            {
                break;
            }
        }

        return (current, next);
    }

    /// <summary>
    /// Computes a capability manifest from growth domains and seed type rules, then caches in Redis.
    /// </summary>
    private async Task<CapabilityManifestModel> ComputeAndCacheManifestAsync(SeedModel seed, CancellationToken cancellationToken)
    {
        var growthStore = _stateStoreFactory.GetStore<SeedGrowthModel>(StateStoreDefinitions.SeedGrowth);
        var growth = await growthStore.GetAsync($"growth:{seed.SeedId}", cancellationToken);
        var domains = growth?.Domains ?? new Dictionary<string, float>();

        var typeStore = _stateStoreFactory.GetStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions);
        var seedType = await typeStore.GetAsync($"type:{seed.GameServiceId}:{seed.SeedTypeCode}", cancellationToken);

        var capabilities = new List<CapabilityEntry>();
        if (seedType?.CapabilityRules != null)
        {
            foreach (var rule in seedType.CapabilityRules)
            {
                var domainDepth = domains.GetValueOrDefault(rule.Domain, 0f);
                var unlocked = domainDepth >= rule.UnlockThreshold;
                var fidelity = unlocked ? ComputeFidelity(domainDepth, rule.UnlockThreshold, rule.FidelityFormula) : 0f;

                capabilities.Add(new CapabilityEntry
                {
                    CapabilityCode = rule.CapabilityCode,
                    Domain = rule.Domain,
                    Fidelity = fidelity,
                    Unlocked = unlocked
                });
            }
        }

        // Load existing manifest to increment version
        var cacheStore = _stateStoreFactory.GetStore<CapabilityManifestModel>(StateStoreDefinitions.SeedCapabilitiesCache);
        var existing = await cacheStore.GetAsync($"cap:{seed.SeedId}", cancellationToken);

        var manifest = new CapabilityManifestModel
        {
            SeedId = seed.SeedId,
            SeedTypeCode = seed.SeedTypeCode,
            ComputedAt = DateTimeOffset.UtcNow,
            Version = (existing?.Version ?? 0) + 1,
            Capabilities = capabilities
        };

        await cacheStore.SaveAsync($"cap:{seed.SeedId}", manifest, cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync("seed.capability.updated", new SeedCapabilityUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = manifest.ComputedAt,
            SeedId = seed.SeedId,
            SeedTypeCode = seed.SeedTypeCode,
            Version = manifest.Version,
            CapabilityCount = capabilities.Count(c => c.Unlocked)
        }, cancellationToken: cancellationToken);

        return manifest;
    }

    /// <summary>
    /// Computes capability fidelity (0.0-1.0) based on domain depth, threshold, and formula.
    /// </summary>
    private static float ComputeFidelity(float domainDepth, float threshold, string formula)
    {
        if (threshold <= 0) return 1.0f;

        var normalized = domainDepth / threshold;
        return formula switch
        {
            "logarithmic" => Math.Min(1.0f, (float)(Math.Log(1 + normalized) / Math.Log(2))),
            "step" => normalized >= 2.0f ? 1.0f : normalized >= 1.0f ? 0.5f : 0f,
            _ => Math.Min(1.0f, (normalized - 1.0f) / 1.0f) // "linear" default: 0 at threshold, 1 at 2x threshold
        };
    }

    /// <summary>
    /// Maps an internal SeedModel to the API SeedResponse.
    /// </summary>
    private static SeedResponse MapToResponse(SeedModel seed) => new()
    {
        SeedId = seed.SeedId,
        OwnerId = seed.OwnerId,
        OwnerType = seed.OwnerType,
        SeedTypeCode = seed.SeedTypeCode,
        GameServiceId = seed.GameServiceId,
        CreatedAt = seed.CreatedAt,
        GrowthPhase = seed.GrowthPhase,
        TotalGrowth = seed.TotalGrowth,
        BondId = seed.BondId,
        DisplayName = seed.DisplayName,
        Status = seed.Status
    };

    /// <summary>
    /// Maps an internal SeedTypeDefinitionModel to the API SeedTypeResponse.
    /// </summary>
    private static SeedTypeResponse MapTypeToResponse(SeedTypeDefinitionModel model) => new()
    {
        SeedTypeCode = model.SeedTypeCode,
        GameServiceId = model.GameServiceId,
        DisplayName = model.DisplayName,
        Description = model.Description,
        MaxPerOwner = model.MaxPerOwner,
        AllowedOwnerTypes = model.AllowedOwnerTypes,
        GrowthPhases = model.GrowthPhases,
        BondCardinality = model.BondCardinality,
        BondPermanent = model.BondPermanent,
        CapabilityRules = model.CapabilityRules
    };

    /// <summary>
    /// Maps an internal SeedBondModel to the API BondResponse.
    /// </summary>
    private static BondResponse MapBondToResponse(SeedBondModel bond) => new()
    {
        BondId = bond.BondId,
        SeedTypeCode = bond.SeedTypeCode,
        Participants = bond.Participants.Select(p => new BondParticipant
        {
            SeedId = p.SeedId,
            JoinedAt = p.JoinedAt,
            Role = p.Role
        }).ToList(),
        CreatedAt = bond.CreatedAt,
        Status = bond.Status,
        BondStrength = bond.BondStrength,
        SharedGrowth = bond.SharedGrowth
    };

    /// <summary>
    /// Maps an internal CapabilityManifestModel to the API CapabilityManifestResponse.
    /// </summary>
    private static CapabilityManifestResponse MapManifestToResponse(CapabilityManifestModel manifest) => new()
    {
        SeedId = manifest.SeedId,
        SeedTypeCode = manifest.SeedTypeCode,
        ComputedAt = manifest.ComputedAt,
        Version = manifest.Version,
        Capabilities = manifest.Capabilities.Select(c => new Capability
        {
            CapabilityCode = c.CapabilityCode,
            Domain = c.Domain,
            Fidelity = c.Fidelity,
            Unlocked = c.Unlocked
        }).ToList()
    };
}
