using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Scene.Helpers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BeyondImmersion.BannouService.Scene;

/// <summary>
/// Implementation of the Scene service.
/// Provides hierarchical composition storage for game worlds.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// </remarks>
[BannouService("scene", typeof(ISceneService), lifetime: ServiceLifetime.Scoped)]
public partial class SceneService : ISceneService
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<SceneService> _logger;
    private readonly SceneServiceConfiguration _configuration;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IEventConsumer _eventConsumer;
    private readonly ISceneValidationService _validationService;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IMeshInstanceIdentifier _meshInstanceIdentifier;

    /// <summary>State store for scene index entries (scene metadata and lookup).</summary>
    private readonly IStateStore<SceneIndexEntry> _indexStore;

    /// <summary>State store for GUID set indexes (game, type, reference, and asset tracking).</summary>
    private readonly IStateStore<HashSet<Guid>> _guidSetStore;

    /// <summary>State store for checkout state tracking.</summary>
    private readonly IStateStore<CheckoutState> _checkoutStore;

    /// <summary>State store for scene YAML content.</summary>
    private readonly IStateStore<SceneContentEntry> _contentStore;

    /// <summary>State store for game-specific validation rules.</summary>
    private readonly IStateStore<List<ValidationRule>> _rulesStore;

    /// <summary>State store for scene version history.</summary>
    private readonly IStateStore<List<VersionHistoryEntry>> _historyStore;

    // State store key prefixes
    private const string SCENE_INDEX_PREFIX = "scene:index:";
    private const string SCENE_CONTENT_PREFIX = "scene:content:";
    private const string SCENE_BY_GAME_PREFIX = "scene:by-game:";
    private const string SCENE_BY_TYPE_PREFIX = "scene:by-type:";
    private const string SCENE_REFERENCES_PREFIX = "scene:references:";
    private const string SCENE_ASSETS_PREFIX = "scene:assets:";
    private const string SCENE_CHECKOUT_PREFIX = "scene:checkout:";
    private const string VALIDATION_RULES_PREFIX = "scene:validation:";
    private const string SCENE_GLOBAL_INDEX_KEY = "scene:global-index";
    private const string SCENE_VERSION_HISTORY_PREFIX = "scene:version-history:";

    #region Key Building Helpers

    internal static string BuildSceneIndexKey(Guid sceneId)
        => $"{SCENE_INDEX_PREFIX}{sceneId}";

    internal static string BuildSceneContentKey(Guid sceneId)
        => $"{SCENE_CONTENT_PREFIX}{sceneId}";

    internal static string BuildSceneByGameKey(string gameId)
        => $"{SCENE_BY_GAME_PREFIX}{gameId}";

    internal static string BuildSceneByTypeKey(string gameId, string sceneType)
        => $"{SCENE_BY_TYPE_PREFIX}{gameId}:{sceneType}";

    internal static string BuildSceneReferencesKey(Guid sceneId)
        => $"{SCENE_REFERENCES_PREFIX}{sceneId}";

    internal static string BuildSceneAssetsKey(Guid assetId)
        => $"{SCENE_ASSETS_PREFIX}{assetId}";

    internal static string BuildSceneCheckoutKey(Guid sceneId)
        => $"{SCENE_CHECKOUT_PREFIX}{sceneId}";

    internal static string BuildValidationRulesKey(string gameId, string sceneType)
        => $"{VALIDATION_RULES_PREFIX}{gameId}:{sceneType}";

    internal static string BuildSceneVersionHistoryKey(Guid sceneId)
        => $"{SCENE_VERSION_HISTORY_PREFIX}{sceneId}";

    #endregion

    // YAML serializer/deserializer
    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Creates a new instance of the SceneService.
    /// </summary>
    public SceneService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<SceneService> logger,
        SceneServiceConfiguration configuration,
        IDistributedLockProvider lockProvider,
        IEventConsumer eventConsumer,
        ISceneValidationService validationService,
        ITelemetryProvider telemetryProvider,
        IMeshInstanceIdentifier meshInstanceIdentifier)
    {
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _lockProvider = lockProvider;
        _eventConsumer = eventConsumer;
        _validationService = validationService;
        _telemetryProvider = telemetryProvider;
        _meshInstanceIdentifier = meshInstanceIdentifier;

        // Constructor-cache all state store references per FOUNDATION TENETS
        _indexStore = stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);
        _guidSetStore = stateStoreFactory.GetStore<HashSet<Guid>>(StateStoreDefinitions.Scene);
        _checkoutStore = stateStoreFactory.GetStore<CheckoutState>(StateStoreDefinitions.Scene);
        _contentStore = stateStoreFactory.GetStore<SceneContentEntry>(StateStoreDefinitions.Scene);
        _rulesStore = stateStoreFactory.GetStore<List<ValidationRule>>(StateStoreDefinitions.Scene);
        _historyStore = stateStoreFactory.GetStore<List<VersionHistoryEntry>>(StateStoreDefinitions.Scene);

        // Register event consumers via partial class
        RegisterEventConsumers(_eventConsumer);
    }

    /// <summary>
    /// Registers event consumers. Extended in partial class.
    /// </summary>
    partial void RegisterEventConsumers(IEventConsumer eventConsumer);

    #region Scene CRUD Operations

    /// <inheritdoc />
    public async Task<(StatusCodes, SceneResponse?)> CreateSceneAsync(CreateSceneRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("CreateScene: sceneId={SceneId}, name={Name}", body.Scene.SceneId, body.Scene.Name);

        var scene = body.Scene;

        // Validate scene structure
        var validationResult = _validationService.ValidateStructure(scene, _configuration.MaxNodeCount);
        if (!validationResult.Valid)
        {
            _logger.LogDebug("CreateScene failed validation: {Errors}", string.Join(", ", validationResult.Errors?.Select(e => e.Message) ?? Array.Empty<string>()));
            return (StatusCodes.BadRequest, null);
        }

        // Validate tag counts per IMPLEMENTATION TENETS (configuration-first)
        var tagError = ValidateTagCounts(scene);
        if (tagError != null)
        {
            _logger.LogDebug("CreateScene failed tag validation: {Error}", tagError);
            return (StatusCodes.BadRequest, null);
        }

        // Check if scene already exists

        var existingIndex = await _indexStore.GetAsync(BuildSceneIndexKey(scene.SceneId), cancellationToken);
        if (existingIndex != null)
        {
            _logger.LogDebug("CreateScene failed: Scene {SceneId} already exists", scene.SceneId);
            return (StatusCodes.Conflict, null);
        }

        // Set timestamps
        var now = DateTimeOffset.UtcNow;
        scene.CreatedAt = now;
        scene.UpdatedAt = now;

        // Ensure version is set
        if (string.IsNullOrEmpty(scene.Version))
        {
            scene.Version = "1.0.0";
        }

        // Store scene as YAML in lib-asset
        var assetId = await StoreSceneAssetAsync(scene, cancellationToken);

        // Create index entry
        var nodeCount = CountNodes(scene.Root);
        var indexEntry = new SceneIndexEntry
        {
            SceneId = scene.SceneId,
            AssetId = assetId,
            GameId = scene.GameId,
            SceneType = scene.SceneType,
            Name = scene.Name,
            Description = scene.Description,
            Version = scene.Version,
            Tags = scene.Tags?.ToList() ?? new List<string>(),
            NodeCount = nodeCount,
            CreatedAt = scene.CreatedAt,
            UpdatedAt = scene.UpdatedAt,
            IsCheckedOut = false
        };

        await _indexStore.SaveAsync(BuildSceneIndexKey(scene.SceneId), indexEntry, cancellationToken: cancellationToken);

        // Add to global scene index
        await AddToGlobalSceneIndexAsync(scene.SceneId, cancellationToken);

        // Update indexes
        await UpdateSceneIndexesAsync(scene, null, cancellationToken);

        // Store initial version history entry
        await AddVersionHistoryEntryAsync(scene.SceneId, scene.Version, null, cancellationToken);

        // Publish created event
        await PublishSceneCreatedEventAsync(scene, nodeCount, cancellationToken);

        _logger.LogInformation("CreateScene succeeded: sceneId={SceneId}", scene.SceneId);
        return (StatusCodes.OK, new SceneResponse { Scene = scene });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, GetSceneResponse?)> GetSceneAsync(GetSceneRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("GetScene: sceneId={SceneId}, resolveReferences={ResolveReferences}", body.SceneId, body.ResolveReferences);

        // Get index entry to find asset

        var indexEntry = await _indexStore.GetAsync(BuildSceneIndexKey(body.SceneId), cancellationToken);
        if (indexEntry == null)
        {
            _logger.LogDebug("GetScene: Scene {SceneId} not found", body.SceneId);
            return (StatusCodes.NotFound, null);
        }

        // Load scene from lib-asset
        var scene = await LoadSceneAssetAsync(indexEntry.AssetId, body.Version, cancellationToken);
        if (scene == null)
        {
            _logger.LogError("GetScene: Scene {SceneId} index exists but asset {AssetId} not found", body.SceneId, indexEntry.AssetId);
            return (StatusCodes.NotFound, null);
        }

        var response = new GetSceneResponse { Scene = scene };

        // Resolve references if requested
        if (body.ResolveReferences)
        {
            var requestedDepth = body.MaxReferenceDepth > 0 ? body.MaxReferenceDepth : _configuration.DefaultMaxReferenceDepth;
            var maxDepth = Math.Min(requestedDepth, _configuration.MaxReferenceDepthLimit);

            var (resolved, unresolved, errors) = await ResolveReferencesAsync(scene, maxDepth, cancellationToken);
            response.ResolvedReferences = resolved.Count > 0 ? resolved : null;
            response.UnresolvedReferences = unresolved.Count > 0 ? unresolved : null;
            response.ResolutionErrors = errors.Count > 0 ? errors : null;
        }

        return (StatusCodes.OK, response);
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListScenesResponse?)> ListScenesAsync(ListScenesRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("ListScenes: gameId={GameId}, sceneType={SceneType}", body.GameId, body.SceneType);




        // Get candidate scene IDs based on filters
        HashSet<Guid>? candidateIds = null;

        if (!string.IsNullOrEmpty(body.GameId))
        {
            var gameIndex = await _guidSetStore.GetAsync(BuildSceneByGameKey(body.GameId), cancellationToken);
            candidateIds = gameIndex ?? new HashSet<Guid>();
        }

        if (body.SceneType != null)
        {
            var typeKey = BuildSceneByTypeKey(body.GameId ?? "all", body.SceneType);
            var typeIndex = await _guidSetStore.GetAsync(typeKey, cancellationToken);
            if (candidateIds == null)
            {
                candidateIds = typeIndex ?? new HashSet<Guid>();
            }
            else if (typeIndex != null)
            {
                candidateIds.IntersectWith(typeIndex);
            }
        }

        if (body.SceneTypes != null && body.SceneTypes.Count > 0)
        {
            var typeUnion = new HashSet<Guid>();
            foreach (var sceneType in body.SceneTypes)
            {
                var typeKey = BuildSceneByTypeKey(body.GameId ?? "all", sceneType);
                var typeIndex = await _guidSetStore.GetAsync(typeKey, cancellationToken);
                if (typeIndex != null)
                {
                    typeUnion.UnionWith(typeIndex);
                }
            }
            if (candidateIds == null)
            {
                candidateIds = typeUnion;
            }
            else
            {
                candidateIds.IntersectWith(typeUnion);
            }
        }

        // If no filter, get all scenes (expensive - scan all indexes)
        if (candidateIds == null)
        {
            candidateIds = await GetAllSceneIdsAsync(cancellationToken);
        }

        // Load all index entries in bulk (single database round-trip)
        var indexKeys = candidateIds.Select(id => BuildSceneIndexKey(id)).ToList();
        var indexEntries = await _indexStore.GetBulkAsync(indexKeys, cancellationToken);

        // Filter index entries
        var matchingScenes = new List<SceneSummary>();
        foreach (var indexEntry in indexEntries.Values)
        {
            // Apply additional filters
            if (!string.IsNullOrEmpty(body.NameContains) &&
                !indexEntry.Name.Contains(body.NameContains, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (body.Tags != null && body.Tags.Count > 0)
            {
                var sceneTags = indexEntry.Tags ?? new List<string>();
                if (!body.Tags.All(t => sceneTags.Contains(t)))
                {
                    continue;
                }
            }

            matchingScenes.Add(CreateSceneSummary(indexEntry));
        }

        // Sort by updatedAt descending
        matchingScenes = matchingScenes.OrderByDescending(s => s.UpdatedAt).ToList();

        // Apply pagination
        var offset = body.Offset;
        var limit = Math.Min(body.Limit, _configuration.MaxListResults);
        var total = matchingScenes.Count;
        var pagedResults = matchingScenes.Skip(offset).Take(limit).ToList();

        return (StatusCodes.OK, new ListScenesResponse
        {
            Scenes = pagedResults,
            Total = total,
            Offset = offset,
            Limit = limit
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, SceneResponse?)> UpdateSceneAsync(UpdateSceneRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("UpdateScene: sceneId={SceneId}", body.Scene.SceneId);

        var scene = body.Scene;

        // Get existing index entry
        var existingIndex = await _indexStore.GetAsync(BuildSceneIndexKey(scene.SceneId), cancellationToken);
        if (existingIndex == null)
        {
            _logger.LogDebug("UpdateScene: Scene {SceneId} not found", scene.SceneId);
            return (StatusCodes.NotFound, null);
        }

        // Check checkout status and get editor ID if available
        string? editorId = null;
        if (existingIndex.IsCheckedOut)
        {
            // If we have a checkout token, validate it
            if (string.IsNullOrEmpty(body.CheckoutToken))
            {
                _logger.LogDebug("UpdateScene: Scene {SceneId} is checked out", scene.SceneId);
                return (StatusCodes.Conflict, null);
            }


            var checkout = await _checkoutStore.GetAsync(BuildSceneCheckoutKey(scene.SceneId), cancellationToken);
            if (checkout == null || checkout.Token != body.CheckoutToken)
            {
                _logger.LogDebug("UpdateScene: Invalid checkout token for scene {SceneId}", scene.SceneId);
                return (StatusCodes.Forbidden, null);
            }
            // Get editor ID from checkout state for version history
            editorId = checkout.EditorId;
        }

        // Validate scene structure
        var validationResult = _validationService.ValidateStructure(scene, _configuration.MaxNodeCount);
        if (!validationResult.Valid)
        {
            _logger.LogDebug("UpdateScene failed validation: {Errors}", string.Join(", ", validationResult.Errors?.Select(e => e.Message) ?? Array.Empty<string>()));
            return (StatusCodes.BadRequest, null);
        }

        // Validate tag counts per IMPLEMENTATION TENETS (configuration-first)
        var tagError = ValidateTagCounts(scene);
        if (tagError != null)
        {
            _logger.LogDebug("UpdateScene failed tag validation: {Error}", tagError);
            return (StatusCodes.BadRequest, null);
        }

        // Load existing scene to get previous version and timestamps
        var existingScene = await LoadSceneAssetAsync(existingIndex.AssetId, null, cancellationToken);
        var previousVersion = existingScene?.Version ?? "1.0.0";

        // Update timestamps and increment version
        scene.CreatedAt = existingIndex.CreatedAt;
        scene.UpdatedAt = DateTimeOffset.UtcNow;
        scene.Version = IncrementPatchVersion(previousVersion);

        // Store updated scene
        var assetId = await StoreSceneAssetAsync(scene, cancellationToken, existingIndex.AssetId);

        // Update index entry
        var nodeCount = CountNodes(scene.Root);

        // Snapshot old values for changedFields computation (before overwrite)
        var oldName = existingIndex.Name;
        var oldDescription = existingIndex.Description;
        var oldVersion = existingIndex.Version;
        var oldTags = existingIndex.Tags?.ToList() ?? new List<string>();
        var oldNodeCount = existingIndex.NodeCount;

        existingIndex.AssetId = assetId;
        existingIndex.Name = scene.Name;
        existingIndex.Description = scene.Description;
        existingIndex.Version = scene.Version;
        existingIndex.Tags = scene.Tags?.ToList() ?? new List<string>();
        existingIndex.NodeCount = nodeCount;
        existingIndex.UpdatedAt = scene.UpdatedAt;

        // Build changedFields per FOUNDATION TENETS (camelCase property names)
        var changedFields = new List<string>();
        if (oldName != existingIndex.Name) changedFields.Add("name");
        if (oldDescription != existingIndex.Description) changedFields.Add("description");
        if (oldVersion != existingIndex.Version) changedFields.Add("version");
        if (!oldTags.SequenceEqual(existingIndex.Tags)) changedFields.Add("tags");
        if (oldNodeCount != existingIndex.NodeCount) changedFields.Add("nodeCount");
        changedFields.Add("updatedAt");

        await _indexStore.SaveAsync(BuildSceneIndexKey(scene.SceneId), existingIndex, cancellationToken: cancellationToken);

        // Update secondary indexes
        await UpdateSceneIndexesAsync(scene, existingScene, cancellationToken);

        // Record version history entry
        await AddVersionHistoryEntryAsync(scene.SceneId, scene.Version, editorId, cancellationToken);

        // Publish updated event
        await PublishSceneUpdatedEventAsync(scene, previousVersion, nodeCount, changedFields, cancellationToken);

        _logger.LogInformation("UpdateScene succeeded: sceneId={SceneId}, version={Version}", scene.SceneId, scene.Version);
        return (StatusCodes.OK, new SceneResponse { Scene = scene });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, DeleteSceneResponse?)> DeleteSceneAsync(DeleteSceneRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("DeleteScene: sceneId={SceneId}", body.SceneId);

        // Get existing index entry
        var existingIndex = await _indexStore.GetAsync(BuildSceneIndexKey(body.SceneId), cancellationToken);
        if (existingIndex == null)
        {
            _logger.LogDebug("DeleteScene: Scene {SceneId} not found", body.SceneId);
            return (StatusCodes.NotFound, null);
        }

        // Check if other scenes reference this one
        var referencingScenes = await _guidSetStore.GetAsync(BuildSceneReferencesKey(body.SceneId), cancellationToken);
        if (referencingScenes != null && referencingScenes.Count > 0)
        {
            _logger.LogDebug("DeleteScene: Scene {SceneId} is referenced by {Count} other scenes: {ReferencingScenes}",
                body.SceneId, referencingScenes.Count, string.Join(", ", referencingScenes));
            return (StatusCodes.Conflict, null);
        }

        // Load scene for event data
        var scene = await LoadSceneAssetAsync(existingIndex.AssetId, null, cancellationToken);

        // Remove from indexes (soft delete - asset remains until TTL)
        await _indexStore.DeleteAsync(BuildSceneIndexKey(body.SceneId), cancellationToken);
        await RemoveFromIndexesAsync(existingIndex, cancellationToken);

        // Remove from global scene index
        await RemoveFromGlobalSceneIndexAsync(body.SceneId, cancellationToken);

        // Delete scene content

        await _contentStore.DeleteAsync(BuildSceneContentKey(body.SceneId), cancellationToken);

        // Delete version history
        await DeleteVersionHistoryAsync(body.SceneId, cancellationToken);

        // Publish deleted event
        if (scene != null)
        {
            await PublishSceneDeletedEventAsync(scene, existingIndex.NodeCount, body.Reason, cancellationToken);
        }

        _logger.LogInformation("DeleteScene succeeded: sceneId={SceneId}", body.SceneId);
        return (StatusCodes.OK, new DeleteSceneResponse());
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ValidationResult?)> ValidateSceneAsync(ValidateSceneRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("ValidateScene: sceneId={SceneId}", body.Scene.SceneId);

        // Structural validation
        var result = _validationService.ValidateStructure(body.Scene, _configuration.MaxNodeCount);

        // Apply game-specific rules if requested
        if (body.ApplyGameRules)
        {
            // Fetch game validation rules from state store

            var key = BuildValidationRulesKey(body.Scene.GameId ?? "all", body.Scene.SceneType);
            var rules = await _rulesStore.GetAsync(key, cancellationToken);

            var gameRulesResult = _validationService.ApplyGameValidationRules(body.Scene, rules);
            _validationService.MergeResults(result, gameRulesResult);
        }

        return (StatusCodes.OK, result);
    }

    #endregion

    #region Instantiation Operations

    /// <inheritdoc />
    public async Task<(StatusCodes, InstantiateSceneResponse?)> InstantiateSceneAsync(InstantiateSceneRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("InstantiateScene: sceneAssetId={SceneAssetId}, instanceId={InstanceId}, regionId={RegionId}",
            body.SceneAssetId, body.InstanceId, body.RegionId);


        // Validate scene exists
        var indexEntry = await _indexStore.GetAsync(BuildSceneIndexKey(body.SceneAssetId), cancellationToken);
        if (indexEntry == null)
        {
            _logger.LogDebug("InstantiateScene: Scene {SceneAssetId} not found", body.SceneAssetId);
            return (StatusCodes.NotFound, null);
        }

        // Publish instantiated event
        var eventModel = new SceneInstantiatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            InstanceId = body.InstanceId,
            SceneAssetId = body.SceneAssetId,
            SceneVersion = body.Version ?? indexEntry.Version,
            SceneName = indexEntry.Name,
            GameId = indexEntry.GameId,
            SceneType = indexEntry.SceneType,
            RegionId = body.RegionId,
            WorldTransform = new Transform
            {
                Position = new Vector3
                {
                    X = body.WorldTransform.Position.X,
                    Y = body.WorldTransform.Position.Y,
                    Z = body.WorldTransform.Position.Z
                },
                Rotation = new Quaternion
                {
                    X = body.WorldTransform.Rotation.X,
                    Y = body.WorldTransform.Rotation.Y,
                    Z = body.WorldTransform.Rotation.Z,
                    W = body.WorldTransform.Rotation.W
                },
                Scale = new Vector3
                {
                    X = body.WorldTransform.Scale.X,
                    Y = body.WorldTransform.Scale.Y,
                    Z = body.WorldTransform.Scale.Z
                }
            },
            Metadata = body.Metadata
        };

        var published = await _messageBus.PublishSceneInstantiatedAsync(eventModel, cancellationToken);
        if (!published)
        {
            _logger.LogWarning("Failed to publish scene.instantiated event for instance {InstanceId}", body.InstanceId);
        }

        _logger.LogInformation("InstantiateScene succeeded: instanceId={InstanceId}", body.InstanceId);
        return (StatusCodes.OK, new InstantiateSceneResponse
        {
            SceneVersion = indexEntry.Version
        });
    }

    /// <inheritdoc />
    public async Task<StatusCodes> DestroyInstanceAsync(DestroyInstanceRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("DestroyInstance: instanceId={InstanceId}", body.InstanceId);

        // Publish destroyed event
        var eventModel = new SceneDestroyedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            InstanceId = body.InstanceId,
            SceneAssetId = body.SceneAssetId,
            RegionId = body.RegionId,
            Metadata = body.Metadata
        };

        var published = await _messageBus.PublishSceneDestroyedAsync(eventModel, cancellationToken);
        if (!published)
        {
            _logger.LogWarning("Failed to publish scene.destroyed event for instance {InstanceId}", body.InstanceId);
        }

        _logger.LogInformation("DestroyInstance succeeded: instanceId={InstanceId}", body.InstanceId);
        return StatusCodes.OK;
    }

    #endregion

    #region Checkout/Versioning Operations

    /// <inheritdoc />
    public async Task<(StatusCodes, CheckoutResponse?)> CheckoutSceneAsync(CheckoutRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("CheckoutScene: sceneId={SceneId}", body.SceneId);

        var indexKey = BuildSceneIndexKey(body.SceneId);

        // Get scene index with ETag for optimistic concurrency
        var (indexEntry, indexEtag) = await _indexStore.GetWithETagAsync(indexKey, cancellationToken);
        if (indexEntry == null)
        {
            _logger.LogDebug("CheckoutScene: Scene {SceneId} not found", body.SceneId);
            return (StatusCodes.NotFound, null);
        }

        // Check if already checked out
        if (indexEntry.IsCheckedOut)
        {
            var existingCheckout = await _checkoutStore.GetAsync(BuildSceneCheckoutKey(body.SceneId), cancellationToken);
            if (existingCheckout != null && existingCheckout.ExpiresAt > DateTimeOffset.UtcNow)
            {
                _logger.LogDebug("CheckoutScene: Scene {SceneId} already checked out by {Editor}", body.SceneId, existingCheckout.EditorId);
                return (StatusCodes.Conflict, null);
            }
            // Expired checkout - can take over
        }

        // Load the scene
        var scene = await LoadSceneAssetAsync(indexEntry.AssetId, null, cancellationToken);
        if (scene == null)
        {
            _logger.LogError("CheckoutScene: Scene asset not found for {SceneId}", body.SceneId);
            return (StatusCodes.NotFound, null);
        }

        // Create checkout state
        var ttlMinutes = body.TtlMinutes ?? _configuration.DefaultCheckoutTtlMinutes;
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(ttlMinutes);
        var checkoutToken = Guid.NewGuid().ToString("N");

        var checkoutState = new CheckoutState
        {
            SceneId = body.SceneId,
            Token = checkoutToken,
            EditorType = body.EditorType ?? SceneEditorType.Session,
            EditorId = body.EditorId ?? _meshInstanceIdentifier.InstanceId.ToString(),
            ExpiresAt = expiresAt,
            ExtensionCount = 0
        };

        var ttlSeconds = (int)TimeSpan.FromMinutes(ttlMinutes + _configuration.CheckoutTtlBufferMinutes).TotalSeconds;
        await _checkoutStore.SaveAsync(
            BuildSceneCheckoutKey(body.SceneId),
            checkoutState,
            new StateOptions { Ttl = ttlSeconds },
            cancellationToken);

        // Update index with optimistic concurrency
        indexEntry.IsCheckedOut = true;
        indexEntry.CheckedOutByType = checkoutState.EditorType;
        indexEntry.CheckedOutById = checkoutState.EditorId;
        var newIndexEtag = await _indexStore.TrySaveAsync(indexKey, indexEntry, indexEtag ?? string.Empty, cancellationToken: cancellationToken);
        if (newIndexEtag == null)
        {
            _logger.LogDebug("CheckoutScene: Concurrent modification on scene index {SceneId}", body.SceneId);
            await _checkoutStore.DeleteAsync(BuildSceneCheckoutKey(body.SceneId), cancellationToken);
            return (StatusCodes.Conflict, null);
        }

        // Publish event
        var eventModel = new SceneCheckedOutEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = body.SceneId,
            SceneName = indexEntry.Name,
            GameId = indexEntry.GameId,
            CheckedOutByType = checkoutState.EditorType,
            CheckedOutById = checkoutState.EditorId,
            ExpiresAt = expiresAt
        };
        await _messageBus.PublishSceneCheckedOutAsync(eventModel, cancellationToken);

        _logger.LogInformation("CheckoutScene succeeded: sceneId={SceneId}, expiresAt={ExpiresAt}", body.SceneId, expiresAt);
        return (StatusCodes.OK, new CheckoutResponse
        {
            CheckoutToken = checkoutToken,
            Scene = scene,
            ExpiresAt = expiresAt
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, CommitResponse?)> CommitSceneAsync(CommitRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("CommitScene: sceneId={SceneId}", body.SceneId);

        // Validate checkout token
        var checkout = await _checkoutStore.GetAsync(BuildSceneCheckoutKey(body.SceneId), cancellationToken);
        if (checkout == null || checkout.Token != body.CheckoutToken)
        {
            _logger.LogDebug("CommitScene: Invalid checkout token for scene {SceneId}", body.SceneId);
            return (StatusCodes.Forbidden, null);
        }

        if (checkout.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _logger.LogDebug("CommitScene: Checkout expired for scene {SceneId}", body.SceneId);
            return (StatusCodes.Conflict, null);
        }

        // Get index entry with ETag for optimistic concurrency
        var indexKey = BuildSceneIndexKey(body.SceneId);
        var (indexEntry, indexEtag) = await _indexStore.GetWithETagAsync(indexKey, cancellationToken);
        if (indexEntry == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // STEP 1: Clear index checkout flag FIRST (can fail safely — nothing mutated yet)
        // Per IMPLEMENTATION TENETS multi-service call compensation: ETag-guarded step before irreversible mutations
        indexEntry.IsCheckedOut = false;
        indexEntry.CheckedOutByType = null;
        indexEntry.CheckedOutById = null;
        var newIndexEtag = await _indexStore.TrySaveAsync(indexKey, indexEntry, indexEtag ?? string.Empty, cancellationToken: cancellationToken);
        if (newIndexEtag == null)
        {
            _logger.LogDebug("CommitScene: Concurrent modification on scene index {SceneId}", body.SceneId);
            return (StatusCodes.Conflict, null);
        }

        // STEP 2: Update scene content (index already updated — compensate on failure)
        var updateRequest = new UpdateSceneRequest
        {
            Scene = body.Scene,
            CheckoutToken = body.CheckoutToken
        };
        var (status, updateResponse) = await UpdateSceneAsync(updateRequest, cancellationToken);
        if (status != StatusCodes.OK || updateResponse == null)
        {
            // Compensation: re-mark index as checked out since content update failed
            indexEntry.IsCheckedOut = true;
            indexEntry.CheckedOutByType = checkout.EditorType;
            indexEntry.CheckedOutById = checkout.EditorId;
            await _indexStore.TrySaveAsync(indexKey, indexEntry, newIndexEtag ?? string.Empty, cancellationToken: cancellationToken);
            return (status, null);
        }

        // STEP 3: Delete checkout state (content updated, checkout no longer needed)
        await _checkoutStore.DeleteAsync(BuildSceneCheckoutKey(body.SceneId), cancellationToken);

        // Publish committed event
        var eventModel = new SceneCommittedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = body.SceneId,
            SceneName = indexEntry.Name,
            GameId = indexEntry.GameId,
            Version = updateResponse.Scene.Version,
            PreviousVersion = body.Scene.Version,
            CommittedByType = checkout.EditorType,
            CommittedById = checkout.EditorId,
            ChangesSummary = body.ChangesSummary,
            NodeCount = CountNodes(body.Scene.Root)
        };
        await _messageBus.PublishSceneCommittedAsync(eventModel, cancellationToken);

        _logger.LogInformation("CommitScene succeeded: sceneId={SceneId}, newVersion={Version}", body.SceneId, updateResponse.Scene.Version);
        return (StatusCodes.OK, new CommitResponse
        {
            NewVersion = updateResponse.Scene.Version,
            Scene = updateResponse.Scene
        });
    }

    /// <inheritdoc />
    public async Task<StatusCodes> DiscardCheckoutAsync(DiscardRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("DiscardCheckout: sceneId={SceneId}", body.SceneId);

        // Validate checkout token
        var checkout = await _checkoutStore.GetAsync(BuildSceneCheckoutKey(body.SceneId), cancellationToken);
        if (checkout == null || checkout.Token != body.CheckoutToken)
        {
            _logger.LogDebug("DiscardCheckout: Invalid checkout token for scene {SceneId}", body.SceneId);
            return StatusCodes.Forbidden;
        }

        // Get index entry with ETag for optimistic concurrency
        var indexKey = BuildSceneIndexKey(body.SceneId);
        var (indexEntry, indexEtag) = await _indexStore.GetWithETagAsync(indexKey, cancellationToken);

        // Release checkout
        await _checkoutStore.DeleteAsync(BuildSceneCheckoutKey(body.SceneId), cancellationToken);
        if (indexEntry != null)
        {
            indexEntry.IsCheckedOut = false;
            indexEntry.CheckedOutByType = null;
            indexEntry.CheckedOutById = null;
            var newIndexEtag = await _indexStore.TrySaveAsync(indexKey, indexEntry, indexEtag ?? string.Empty, cancellationToken: cancellationToken);
            if (newIndexEtag == null)
            {
                _logger.LogDebug("DiscardCheckout: Concurrent modification on scene index {SceneId}", body.SceneId);
                return StatusCodes.Conflict;
            }
        }

        // Publish event
        var eventModel = new SceneCheckoutDiscardedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = body.SceneId,
            SceneName = indexEntry?.Name,
            GameId = indexEntry?.GameId,
            DiscardedByType = checkout.EditorType,
            DiscardedById = checkout.EditorId
        };
        await _messageBus.PublishSceneCheckoutDiscardedAsync(eventModel, cancellationToken);

        _logger.LogInformation("DiscardCheckout succeeded: sceneId={SceneId}", body.SceneId);
        return StatusCodes.OK;
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, HeartbeatResponse?)> HeartbeatCheckoutAsync(HeartbeatRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("HeartbeatCheckout: sceneId={SceneId}", body.SceneId);

        // Validate checkout token
        var checkout = await _checkoutStore.GetAsync(BuildSceneCheckoutKey(body.SceneId), cancellationToken);
        if (checkout == null || checkout.Token != body.CheckoutToken)
        {
            _logger.LogDebug("HeartbeatCheckout: Invalid checkout token for scene {SceneId}", body.SceneId);
            return (StatusCodes.Forbidden, null);
        }

        if (checkout.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _logger.LogDebug("HeartbeatCheckout: Checkout expired for scene {SceneId}", body.SceneId);
            return (StatusCodes.Conflict, null);
        }

        // Check extension limit — return 409 when limit reached per QUALITY TENETS (no filler booleans)
        if (checkout.ExtensionCount >= _configuration.MaxCheckoutExtensions)
        {
            _logger.LogDebug("HeartbeatCheckout: Extension limit reached for scene {SceneId}", body.SceneId);
            return (StatusCodes.Conflict, null);
        }

        // Extend checkout
        var newExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_configuration.DefaultCheckoutTtlMinutes);
        checkout.ExpiresAt = newExpiresAt;
        checkout.ExtensionCount++;

        var ttlSeconds = (int)TimeSpan.FromMinutes(_configuration.DefaultCheckoutTtlMinutes + _configuration.CheckoutTtlBufferMinutes).TotalSeconds;
        await _checkoutStore.SaveAsync(
            BuildSceneCheckoutKey(body.SceneId),
            checkout,
            new StateOptions { Ttl = ttlSeconds },
            cancellationToken);

        return (StatusCodes.OK, new HeartbeatResponse
        {
            NewExpiresAt = newExpiresAt,
            ExtensionsRemaining = _configuration.MaxCheckoutExtensions - checkout.ExtensionCount
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, HistoryResponse?)> GetSceneHistoryAsync(HistoryRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("GetSceneHistory: sceneId={SceneId}", body.SceneId);

        // Get index entry
        var indexEntry = await _indexStore.GetAsync(BuildSceneIndexKey(body.SceneId), cancellationToken);
        if (indexEntry == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Get version history from lib-asset
        var versions = await GetAssetVersionHistoryAsync(indexEntry.AssetId, body.Limit, cancellationToken);

        return (StatusCodes.OK, new HistoryResponse
        {
            SceneId = body.SceneId,
            CurrentVersion = indexEntry.Version,
            Versions = versions
        });
    }

    #endregion

    #region Validation Rules Operations

    /// <inheritdoc />
    public async Task<StatusCodes> RegisterValidationRulesAsync(RegisterValidationRulesRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("RegisterValidationRules: gameId={GameId}, sceneType={SceneType}, ruleCount={RuleCount}",
            body.GameId, body.SceneType, body.Rules.Count);

        var key = BuildValidationRulesKey(body.GameId, body.SceneType);

        await _rulesStore.SaveAsync(key, body.Rules.ToList(), cancellationToken: cancellationToken);

        // Publish event
        var eventModel = new SceneValidationRulesUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            GameId = body.GameId,
            SceneType = body.SceneType,
            RuleCount = body.Rules.Count
        };
        await _messageBus.PublishSceneValidationRulesUpdatedAsync(eventModel, cancellationToken);

        _logger.LogInformation("RegisterValidationRules succeeded: gameId={GameId}, sceneType={SceneType}", body.GameId, body.SceneType);
        return StatusCodes.OK;
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, GetValidationRulesResponse?)> GetValidationRulesAsync(GetValidationRulesRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("GetValidationRules: gameId={GameId}, sceneType={SceneType}", body.GameId, body.SceneType);


        var key = BuildValidationRulesKey(body.GameId, body.SceneType);

        var rules = await _rulesStore.GetAsync(key, cancellationToken);

        return (StatusCodes.OK, new GetValidationRulesResponse
        {
            Rules = rules ?? new List<ValidationRule>()
        });
    }

    #endregion

    #region Query Operations

    /// <inheritdoc />
    public async Task<(StatusCodes, SearchScenesResponse?)> SearchScenesAsync(SearchScenesRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SearchScenes: query={Query}", body.Query);



        // Get all scene IDs (with optional game/type filter)
        var candidateIds = await GetAllSceneIdsAsync(cancellationToken);

        // Load all index entries in bulk (single database round-trip)
        var indexKeys = candidateIds.Select(id => BuildSceneIndexKey(id)).ToList();
        var indexEntries = await _indexStore.GetBulkAsync(indexKeys, cancellationToken);

        var results = new List<SearchResult>();
        foreach (var indexEntry in indexEntries.Values)
        {
            // Apply filters
            if (!string.IsNullOrEmpty(body.GameId) && indexEntry.GameId != body.GameId) continue;
            if (body.SceneTypes != null && body.SceneTypes.Count > 0)
            {
                if (!body.SceneTypes.Contains(indexEntry.SceneType))
                {
                    continue;
                }
            }

            // Search in name, description, tags
            SearchMatchType? matchType = null;
            string? matchContext = null;

            if (indexEntry.Name.Contains(body.Query, StringComparison.OrdinalIgnoreCase))
            {
                matchType = SearchMatchType.Name;
                matchContext = indexEntry.Name;
            }
            else if (!string.IsNullOrEmpty(indexEntry.Description) &&
                    indexEntry.Description.Contains(body.Query, StringComparison.OrdinalIgnoreCase))
            {
                matchType = SearchMatchType.Description;
                matchContext = indexEntry.Description;
            }
            else if (indexEntry.Tags != null && indexEntry.Tags.Any(t => t.Contains(body.Query, StringComparison.OrdinalIgnoreCase)))
            {
                matchType = SearchMatchType.Tag;
                matchContext = string.Join(", ", indexEntry.Tags.Where(t => t.Contains(body.Query, StringComparison.OrdinalIgnoreCase)));
            }

            if (matchType != null)
            {
                results.Add(new SearchResult
                {
                    Scene = CreateSceneSummary(indexEntry),
                    MatchType = matchType.Value,
                    MatchContext = matchContext
                });
            }
        }

        // Apply pagination
        var offset = body.Offset;
        var limit = Math.Min(body.Limit, _configuration.MaxSearchResults);
        var pagedResults = results.Skip(offset).Take(limit).ToList();

        return (StatusCodes.OK, new SearchScenesResponse
        {
            Results = pagedResults,
            Total = results.Count
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, FindReferencesResponse?)> FindReferencesAsync(FindReferencesRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("FindReferences: sceneId={SceneId}", body.SceneId);




        var referencingSceneIds = await _guidSetStore.GetAsync(BuildSceneReferencesKey(body.SceneId), cancellationToken);
        var references = new List<ReferenceInfo>();

        if (referencingSceneIds != null && referencingSceneIds.Count > 0)
        {
            // Load all index entries in bulk (single database round-trip)
            var indexKeys = referencingSceneIds.Select(id => BuildSceneIndexKey(id)).ToList();
            var indexEntries = await _indexStore.GetBulkAsync(indexKeys, cancellationToken);

            foreach (var (key, indexEntry) in indexEntries)
            {
                // Extract scene ID from key (format: "scene:index:{sceneId}")
                var refSceneIdStr = key.Substring(SCENE_INDEX_PREFIX.Length);
                if (!Guid.TryParse(refSceneIdStr, out var refSceneId)) continue;

                // Load scene to find the referencing node
                var scene = await LoadSceneAssetAsync(indexEntry.AssetId, null, cancellationToken);
                if (scene == null) continue;

                var refNodes = FindReferenceNodes(scene.Root, body.SceneId);
                foreach (var node in refNodes)
                {
                    references.Add(new ReferenceInfo
                    {
                        SceneId = refSceneId,
                        SceneName = indexEntry.Name,
                        NodeId = node.NodeId,
                        NodeRefId = node.RefId,
                        NodeName = node.Name
                    });
                }
            }
        }

        return (StatusCodes.OK, new FindReferencesResponse
        {
            ReferencingScenes = references
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, FindAssetUsageResponse?)> FindAssetUsageAsync(FindAssetUsageRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("FindAssetUsage: assetId={AssetId}", body.AssetId);

        var usingSceneIds = await _guidSetStore.GetAsync(BuildSceneAssetsKey(body.AssetId), cancellationToken);
        var usages = new List<AssetUsageInfo>();

        if (usingSceneIds != null && usingSceneIds.Count > 0)
        {
            // Load all index entries in bulk (single database round-trip)
            var indexKeys = usingSceneIds.Select(id => BuildSceneIndexKey(id)).ToList();
            var indexEntries = await _indexStore.GetBulkAsync(indexKeys, cancellationToken);

            foreach (var (key, indexEntry) in indexEntries)
            {
                // Extract scene ID from key (format: "scene:index:{sceneId}")
                var sceneIdStr = key.Substring(SCENE_INDEX_PREFIX.Length);
                if (!Guid.TryParse(sceneIdStr, out var sceneId)) continue;

                // Apply game filter
                if (!string.IsNullOrEmpty(body.GameId) && indexEntry.GameId != body.GameId) continue;

                // Load scene to find the nodes using this asset
                var scene = await LoadSceneAssetAsync(indexEntry.AssetId, null, cancellationToken);
                if (scene == null) continue;

                var assetNodes = FindAssetNodes(scene.Root, body.AssetId);
                foreach (var node in assetNodes)
                {
                    usages.Add(new AssetUsageInfo
                    {
                        SceneId = sceneId,
                        SceneName = indexEntry.Name,
                        NodeId = node.NodeId,
                        NodeRefId = node.RefId,
                        NodeName = node.Name,
                        NodeType = node.NodeType
                    });
                }
            }
        }

        return (StatusCodes.OK, new FindAssetUsageResponse
        {
            Usages = usages
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, SceneResponse?)> DuplicateSceneAsync(DuplicateSceneRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("DuplicateScene: sourceSceneId={SourceSceneId}, newName={NewName}", body.SourceSceneId, body.NewName);

        // Get source scene
        var sourceIndex = await _indexStore.GetAsync(BuildSceneIndexKey(body.SourceSceneId), cancellationToken);
        if (sourceIndex == null)
        {
            _logger.LogDebug("DuplicateScene: Source scene {SourceSceneId} not found", body.SourceSceneId);
            return (StatusCodes.NotFound, null);
        }

        // Load source scene
        var sourceScene = await LoadSceneAssetAsync(sourceIndex.AssetId, null, cancellationToken);
        if (sourceScene == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Create duplicate with new IDs
        var newScene = DuplicateSceneWithNewIds(sourceScene, body.NewName, body.NewGameId, body.NewSceneType);

        // Create via CreateSceneAsync
        var createRequest = new CreateSceneRequest { Scene = newScene };
        return await CreateSceneAsync(createRequest, cancellationToken);
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Stores scene content as YAML in the state store.
    /// </summary>
    /// <param name="scene">The scene to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="existingAssetId">Existing asset ID for updates (not used in state store approach).</param>
    /// <returns>The asset ID (scene ID) for the stored content.</returns>
    private async Task<Guid> StoreSceneAssetAsync(Scene scene, CancellationToken cancellationToken, Guid? existingAssetId = null)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.StoreSceneAssetAsync");
        var yaml = YamlSerializer.Serialize(scene);
        var sceneId = scene.SceneId;
        var contentKey = BuildSceneContentKey(sceneId);

        // Store the YAML content directly in state store

        var contentEntry = new SceneContentEntry
        {
            SceneId = sceneId,
            Version = scene.Version,
            Content = yaml,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _contentStore.SaveAsync(contentKey, contentEntry, null, cancellationToken);

        return sceneId;
    }

    /// <summary>
    /// Loads scene content from the state store.
    /// </summary>
    /// <param name="assetId">The scene/asset ID to load.</param>
    /// <param name="version">Optional version (not currently supported for state store).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized scene, or null if not found.</returns>
    private async Task<Scene?> LoadSceneAssetAsync(Guid assetId, string? version, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.LoadSceneAssetAsync");
        var contentKey = BuildSceneContentKey(assetId);

        var contentEntry = await _contentStore.GetAsync(contentKey, cancellationToken);
        if (contentEntry == null || string.IsNullOrEmpty(contentEntry.Content))
        {
            return null;
        }

        // Deserialize YAML to Scene
        return YamlDeserializer.Deserialize<Scene>(contentEntry.Content);
    }

    /// <summary>
    /// Gets version history for a scene.
    /// </summary>
    /// <param name="sceneId">The scene ID to get history for.</param>
    /// <param name="limit">Maximum number of versions to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of version info entries, newest first.</returns>
    private async Task<List<VersionInfo>> GetAssetVersionHistoryAsync(Guid sceneId, int limit, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.GetAssetVersionHistoryAsync");

        var historyKey = BuildSceneVersionHistoryKey(sceneId);

        var historyEntries = await _historyStore.GetAsync(historyKey, cancellationToken);
        if (historyEntries == null || historyEntries.Count == 0)
        {
            return new List<VersionInfo>();
        }

        // Return newest first, limited to requested count
        return historyEntries
            .OrderByDescending(h => h.CreatedAt)
            .Take(limit)
            .Select(h => new VersionInfo
            {
                Version = h.Version,
                CreatedAt = h.CreatedAt,
                CreatedBy = h.CreatedBy
            })
            .ToList();
    }

    /// <summary>
    /// Adds a version history entry for a scene.
    /// </summary>
    private async Task AddVersionHistoryEntryAsync(Guid sceneId, string version, string? editorId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.AddVersionHistoryEntryAsync");

        var historyKey = BuildSceneVersionHistoryKey(sceneId);

        var historyEntries = await _historyStore.GetAsync(historyKey, cancellationToken) ?? new List<VersionHistoryEntry>();

        historyEntries.Add(new VersionHistoryEntry
        {
            Version = version,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = editorId
        });

        // Enforce retention limit
        var maxVersions = _configuration.MaxVersionRetentionCount;
        if (historyEntries.Count > maxVersions)
        {
            historyEntries = historyEntries
                .OrderByDescending(h => h.CreatedAt)
                .Take(maxVersions)
                .ToList();
        }

        await _historyStore.SaveAsync(historyKey, historyEntries, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Deletes all version history for a scene.
    /// </summary>
    private async Task DeleteVersionHistoryAsync(Guid sceneId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.DeleteVersionHistoryAsync");

        var historyKey = BuildSceneVersionHistoryKey(sceneId);
        await _historyStore.DeleteAsync(historyKey, cancellationToken);
    }

    /// <summary>
    /// Atomically modifies a HashSet index with optimistic concurrency retry.
    /// Per IMPLEMENTATION TENETS multi-instance safety.
    /// </summary>
    private async Task ModifyGuidSetIndexAsync(
        string key,
        Action<HashSet<Guid>> modify,
        CancellationToken ct,
        int maxRetries = 3)
    {
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            var (existing, etag) = await _guidSetStore.GetWithETagAsync(key, ct);
            var set = existing ?? new HashSet<Guid>();
            modify(set);
            var result = await _guidSetStore.TrySaveAsync(key, set, etag ?? string.Empty, cancellationToken: ct);
            if (result != null) return;
            _logger.LogDebug("Concurrent modification on index {Key}, retry {Attempt}", key, attempt + 1);
        }
        _logger.LogWarning("Failed to update index {Key} after {MaxRetries} retries", key, maxRetries);
    }

    private async Task UpdateSceneIndexesAsync(Scene newScene, Scene? oldScene, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.UpdateSceneIndexesAsync");

        // Remove from old game/type indexes if changed
        if (oldScene != null && oldScene.GameId != newScene.GameId)
        {
            var oldGameKey = BuildSceneByGameKey(oldScene.GameId ?? "all");
            await ModifyGuidSetIndexAsync(oldGameKey, set => set.Remove(newScene.SceneId), cancellationToken);
        }

        if (oldScene != null && (oldScene.GameId != newScene.GameId || oldScene.SceneType != newScene.SceneType))
        {
            var oldTypeKey = BuildSceneByTypeKey(oldScene.GameId ?? "all", oldScene.SceneType);
            await ModifyGuidSetIndexAsync(oldTypeKey, set => set.Remove(newScene.SceneId), cancellationToken);
        }

        // Update game index
        var gameKey = BuildSceneByGameKey(newScene.GameId ?? "all");
        await ModifyGuidSetIndexAsync(gameKey, set => set.Add(newScene.SceneId), cancellationToken);

        // Update type index
        var typeKey = BuildSceneByTypeKey(newScene.GameId ?? "all", newScene.SceneType);
        await ModifyGuidSetIndexAsync(typeKey, set => set.Add(newScene.SceneId), cancellationToken);

        // Update reference tracking
        var newReferences = ExtractSceneReferences(newScene.Root);
        var oldReferences = oldScene != null ? ExtractSceneReferences(oldScene.Root) : new HashSet<Guid>();

        // Add new references
        foreach (var refSceneId in newReferences.Except(oldReferences))
        {
            var refKey = BuildSceneReferencesKey(refSceneId);
            await ModifyGuidSetIndexAsync(refKey, set => set.Add(newScene.SceneId), cancellationToken);
        }

        // Remove old references
        foreach (var refSceneId in oldReferences.Except(newReferences))
        {
            var refKey = BuildSceneReferencesKey(refSceneId);
            await ModifyGuidSetIndexAsync(refKey, set => set.Remove(newScene.SceneId), cancellationToken);
        }

        // Update asset usage tracking
        var newAssets = ExtractAssetReferences(newScene.Root);
        var oldAssets = oldScene != null ? ExtractAssetReferences(oldScene.Root) : new HashSet<Guid>();

        foreach (var assetId in newAssets.Except(oldAssets))
        {
            var assetKey = BuildSceneAssetsKey(assetId);
            await ModifyGuidSetIndexAsync(assetKey, set => set.Add(newScene.SceneId), cancellationToken);
        }

        foreach (var assetId in oldAssets.Except(newAssets))
        {
            var assetKey = BuildSceneAssetsKey(assetId);
            await ModifyGuidSetIndexAsync(assetKey, set => set.Remove(newScene.SceneId), cancellationToken);
        }
    }

    private async Task RemoveFromIndexesAsync(SceneIndexEntry indexEntry, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.RemoveFromIndexesAsync");

        // Remove from game index
        var gameKey = BuildSceneByGameKey(indexEntry.GameId ?? "all");
        await ModifyGuidSetIndexAsync(gameKey, set => set.Remove(indexEntry.SceneId), cancellationToken);

        // Remove from type index
        var typeKey = BuildSceneByTypeKey(indexEntry.GameId ?? "all", indexEntry.SceneType);
        await ModifyGuidSetIndexAsync(typeKey, set => set.Remove(indexEntry.SceneId), cancellationToken);
    }

    /// <summary>
    /// Gets all scene IDs in the system from the global index.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Set of all scene IDs.</returns>
    private async Task<HashSet<Guid>> GetAllSceneIdsAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.GetAllSceneIdsAsync");

        var globalIndex = await _guidSetStore.GetAsync(SCENE_GLOBAL_INDEX_KEY, cancellationToken);
        return globalIndex ?? new HashSet<Guid>();
    }

    /// <summary>
    /// Adds a scene ID to the global index.
    /// </summary>
    private async Task AddToGlobalSceneIndexAsync(Guid sceneId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.AddToGlobalSceneIndexAsync");

        await ModifyGuidSetIndexAsync(SCENE_GLOBAL_INDEX_KEY, set => set.Add(sceneId), cancellationToken);
    }

    /// <summary>
    /// Removes a scene ID from the global index.
    /// </summary>
    private async Task RemoveFromGlobalSceneIndexAsync(Guid sceneId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.RemoveFromGlobalSceneIndexAsync");

        await ModifyGuidSetIndexAsync(SCENE_GLOBAL_INDEX_KEY, set => set.Remove(sceneId), cancellationToken);
    }

    private async Task<(List<ResolvedReference>, List<UnresolvedReference>, List<string>)> ResolveReferencesAsync(
        Scene scene, int maxDepth, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.ResolveReferencesAsync");
        var resolved = new List<ResolvedReference>();
        var unresolved = new List<UnresolvedReference>();
        var errors = new List<string>();
        var visited = new HashSet<Guid> { scene.SceneId };

        await ResolveReferencesRecursiveAsync(scene.Root, 1, maxDepth, visited, resolved, unresolved, errors, cancellationToken);

        return (resolved, unresolved, errors);
    }

    private async Task ResolveReferencesRecursiveAsync(
        SceneNode node, int depth, int maxDepth, HashSet<Guid> visited,
        List<ResolvedReference> resolved, List<UnresolvedReference> unresolved, List<string> errors,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.ResolveReferencesRecursiveAsync");
        if (node.NodeType == NodeType.Reference)
        {
            var referencedSceneId = GetReferenceSceneId(node);
            if (referencedSceneId.HasValue)
            {
                if (depth > maxDepth)
                {
                    unresolved.Add(new UnresolvedReference
                    {
                        NodeId = node.NodeId,
                        RefId = node.RefId,
                        ReferencedSceneId = referencedSceneId.Value,
                        Reason = UnresolvedReferenceReason.DepthExceeded
                    });
                    errors.Add($"Reference at node '{node.RefId}' exceeds max depth {maxDepth}");
                }
                else if (visited.Contains(referencedSceneId.Value))
                {
                    unresolved.Add(new UnresolvedReference
                    {
                        NodeId = node.NodeId,
                        RefId = node.RefId,
                        ReferencedSceneId = referencedSceneId.Value,
                        Reason = UnresolvedReferenceReason.CircularReference,
                        CyclePath = visited.ToList()
                    });
                    errors.Add($"Circular reference detected at node '{node.RefId}'");
                }
                else
                {

                    var indexEntry = await _indexStore.GetAsync(BuildSceneIndexKey(referencedSceneId.Value), cancellationToken);

                    if (indexEntry == null)
                    {
                        unresolved.Add(new UnresolvedReference
                        {
                            NodeId = node.NodeId,
                            RefId = node.RefId,
                            ReferencedSceneId = referencedSceneId.Value,
                            Reason = UnresolvedReferenceReason.NotFound
                        });
                        errors.Add($"Referenced scene '{referencedSceneId.Value}' not found");
                    }
                    else
                    {
                        var referencedScene = await LoadSceneAssetAsync(indexEntry.AssetId, null, cancellationToken);
                        if (referencedScene != null)
                        {
                            resolved.Add(new ResolvedReference
                            {
                                NodeId = node.NodeId,
                                RefId = node.RefId,
                                ReferencedSceneId = referencedSceneId.Value,
                                ReferencedVersion = referencedScene.Version,
                                Scene = referencedScene,
                                Depth = depth
                            });

                            // Recursively resolve references in the referenced scene
                            visited.Add(referencedSceneId.Value);
                            await ResolveReferencesRecursiveAsync(referencedScene.Root, depth + 1, maxDepth, visited, resolved, unresolved, errors, cancellationToken);
                        }
                    }
                }
            }
        }

        // Process children
        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                await ResolveReferencesRecursiveAsync(child, depth, maxDepth, visited, resolved, unresolved, errors, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Extracts the referenced scene ID from a reference node using the typed ReferenceSceneId field.
    /// </summary>
    private static Guid? GetReferenceSceneId(SceneNode node)
    {
        return node.ReferenceSceneId;
    }

    private HashSet<Guid> ExtractSceneReferences(SceneNode root)
    {
        var references = new HashSet<Guid>();
        ExtractSceneReferencesRecursive(root, references);
        return references;
    }

    private void ExtractSceneReferencesRecursive(SceneNode node, HashSet<Guid> references)
    {
        if (node.NodeType == NodeType.Reference)
        {
            var sceneId = GetReferenceSceneId(node);
            if (sceneId.HasValue)
            {
                references.Add(sceneId.Value);
            }
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                ExtractSceneReferencesRecursive(child, references);
            }
        }
    }

    private HashSet<Guid> ExtractAssetReferences(SceneNode root)
    {
        var assets = new HashSet<Guid>();
        ExtractAssetReferencesRecursive(root, assets);
        return assets;
    }

    private void ExtractAssetReferencesRecursive(SceneNode node, HashSet<Guid> assets)
    {
        if (node.Asset != null)
        {
            assets.Add(node.Asset.AssetId);
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                ExtractAssetReferencesRecursive(child, assets);
            }
        }
    }

    private List<SceneNode> FindReferenceNodes(SceneNode root, Guid targetSceneId)
    {
        var nodes = new List<SceneNode>();
        FindReferenceNodesRecursive(root, targetSceneId, nodes);
        return nodes;
    }

    private void FindReferenceNodesRecursive(SceneNode node, Guid targetSceneId, List<SceneNode> nodes)
    {
        if (node.NodeType == NodeType.Reference)
        {
            var sceneId = GetReferenceSceneId(node);
            if (sceneId == targetSceneId)
            {
                nodes.Add(node);
            }
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                FindReferenceNodesRecursive(child, targetSceneId, nodes);
            }
        }
    }

    private List<SceneNode> FindAssetNodes(SceneNode root, Guid targetAssetId)
    {
        var nodes = new List<SceneNode>();
        FindAssetNodesRecursive(root, targetAssetId, nodes);
        return nodes;
    }

    private void FindAssetNodesRecursive(SceneNode node, Guid targetAssetId, List<SceneNode> nodes)
    {
        if (node.Asset != null && node.Asset.AssetId == targetAssetId)
        {
            nodes.Add(node);
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                FindAssetNodesRecursive(child, targetAssetId, nodes);
            }
        }
    }

    private int CountNodes(SceneNode root)
    {
        var count = 1;
        if (root.Children != null)
        {
            foreach (var child in root.Children)
            {
                count += CountNodes(child);
            }
        }
        return count;
    }

    /// <summary>
    /// Validates scene-level and node-level tag counts against configuration limits.
    /// </summary>
    /// <param name="scene">The scene to validate.</param>
    /// <returns>Error message if validation fails, null if valid.</returns>
    private string? ValidateTagCounts(Scene scene)
    {
        // Validate scene-level tags
        if (scene.Tags != null && scene.Tags.Count > _configuration.MaxTagsPerScene)
        {
            return $"Scene has {scene.Tags.Count} tags, exceeds maximum of {_configuration.MaxTagsPerScene}";
        }

        // Validate per-node tags
        if (scene.Root != null)
        {
            var nodeError = ValidateNodeTagsRecursive(scene.Root);
            if (nodeError != null)
            {
                return nodeError;
            }
        }

        return null;
    }

    private string? ValidateNodeTagsRecursive(SceneNode node)
    {
        if (node.Tags != null && node.Tags.Count > _configuration.MaxTagsPerNode)
        {
            return $"Node '{node.RefId}' has {node.Tags.Count} tags, exceeds maximum of {_configuration.MaxTagsPerNode}";
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                var error = ValidateNodeTagsRecursive(child);
                if (error != null)
                {
                    return error;
                }
            }
        }

        return null;
    }

    private string IncrementPatchVersion(string version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return "1.0.1";
        }

        var parts = version.Split('.');
        if (parts.Length == 3 && int.TryParse(parts[2], out var patch))
        {
            return $"{parts[0]}.{parts[1]}.{patch + 1}";
        }

        return "1.0.1";
    }

    private SceneSummary CreateSceneSummary(SceneIndexEntry indexEntry)
    {
        return new SceneSummary
        {
            SceneId = indexEntry.SceneId,
            GameId = indexEntry.GameId,
            SceneType = indexEntry.SceneType,
            Name = indexEntry.Name,
            Description = indexEntry.Description,
            Version = indexEntry.Version,
            Tags = indexEntry.Tags,
            NodeCount = indexEntry.NodeCount,
            CreatedAt = indexEntry.CreatedAt,
            UpdatedAt = indexEntry.UpdatedAt,
            IsCheckedOut = indexEntry.IsCheckedOut
        };
    }

    private Scene DuplicateSceneWithNewIds(Scene source, string newName, string? newGameId, string? newSceneType)
    {
        var newSceneId = Guid.NewGuid();
        var idMapping = new Dictionary<Guid, Guid>();

        return new Scene
        {
            Schema = source.Schema,
            SceneId = newSceneId,
            GameId = newGameId ?? source.GameId,
            SceneType = newSceneType ?? source.SceneType,
            Name = newName,
            Description = source.Description,
            Version = "1.0.0",
            Root = DuplicateNodeWithNewIds(source.Root, idMapping),
            Tags = source.Tags?.ToList() ?? new List<string>(),
            Metadata = source.Metadata
        };
    }

    private SceneNode DuplicateNodeWithNewIds(SceneNode source, Dictionary<Guid, Guid> idMapping)
    {
        var newNodeId = Guid.NewGuid();
        idMapping[source.NodeId] = newNodeId;

        Guid? newParentId = null;
        if (source.ParentNodeId != null && idMapping.TryGetValue(source.ParentNodeId.Value, out var mappedParentId))
        {
            newParentId = mappedParentId;
        }

        return new SceneNode
        {
            NodeId = newNodeId,
            RefId = source.RefId,
            ParentNodeId = newParentId,
            Name = source.Name,
            NodeType = source.NodeType,
            LocalTransform = source.LocalTransform,
            Asset = source.Asset,
            Children = source.Children?.Select(c => DuplicateNodeWithNewIds(c, idMapping)).ToList() ?? new List<SceneNode>(),
            Enabled = source.Enabled,
            SortOrder = source.SortOrder,
            Tags = source.Tags?.ToList() ?? new List<string>(),
            Annotations = source.Annotations
        };
    }

    private async Task PublishSceneCreatedEventAsync(Scene scene, int nodeCount, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.PublishSceneCreatedEventAsync");
        var eventModel = new SceneCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = scene.SceneId,
            GameId = scene.GameId,
            SceneType = scene.SceneType,
            Name = scene.Name,
            Description = scene.Description,
            Version = scene.Version,
            Tags = scene.Tags?.ToList() ?? new List<string>(),
            NodeCount = nodeCount,
            CreatedAt = scene.CreatedAt,
            UpdatedAt = scene.UpdatedAt
        };

        await _messageBus.PublishSceneCreatedAsync(eventModel, cancellationToken);
    }

    private async Task PublishSceneUpdatedEventAsync(Scene scene, string previousVersion, int nodeCount, ICollection<string> changedFields, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.PublishSceneUpdatedEventAsync");
        var eventModel = new SceneUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = scene.SceneId,
            GameId = scene.GameId,
            SceneType = scene.SceneType,
            Name = scene.Name,
            Description = scene.Description,
            Version = scene.Version,
            Tags = scene.Tags?.ToList() ?? new List<string>(),
            NodeCount = nodeCount,
            CreatedAt = scene.CreatedAt,
            UpdatedAt = scene.UpdatedAt,
            ChangedFields = changedFields
        };

        await _messageBus.PublishSceneUpdatedAsync(eventModel, cancellationToken);
    }

    private async Task PublishSceneDeletedEventAsync(Scene scene, int nodeCount, string? reason, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.PublishSceneDeletedEventAsync");
        var eventModel = new SceneDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = scene.SceneId,
            GameId = scene.GameId,
            SceneType = scene.SceneType,
            Name = scene.Name,
            Description = scene.Description,
            Version = scene.Version,
            Tags = scene.Tags?.ToList() ?? new List<string>(),
            NodeCount = nodeCount,
            CreatedAt = scene.CreatedAt,
            UpdatedAt = scene.UpdatedAt
        };

        await _messageBus.PublishSceneDeletedAsync(eventModel, cancellationToken);
    }

    #endregion
}
