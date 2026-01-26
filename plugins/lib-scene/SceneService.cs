using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Scene.Helpers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

[assembly: InternalsVisibleTo("lib-scene.tests")]

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
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<SceneService> _logger;
    private readonly SceneServiceConfiguration _configuration;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IEventConsumer _eventConsumer;
    private readonly ISceneValidationService _validationService;

    // State store key prefixes
    private const string SCENE_INDEX_PREFIX = "scene:index:";
    private const string SCENE_CONTENT_PREFIX = "scene:content:";
    private const string SCENE_BY_GAME_PREFIX = "scene:by-game:";
    private const string SCENE_BY_TYPE_PREFIX = "scene:by-type:";
    private const string SCENE_REFERENCES_PREFIX = "scene:references:";
    private const string SCENE_ASSETS_PREFIX = "scene:assets:";
    private const string SCENE_CHECKOUT_PREFIX = "scene:checkout:";
    private const string SCENE_CHECKOUT_EXT_PREFIX = "scene:checkout-ext:";
    private const string VALIDATION_RULES_PREFIX = "scene:validation:";
    private const string VERSION_RETENTION_PREFIX = "scene:version-retention:";
    private const string SCENE_GLOBAL_INDEX_KEY = "scene:global-index";
    private const string SCENE_VERSION_HISTORY_PREFIX = "scene:version-history:";

    // Event topics following QUALITY TENETS: {entity}.{action} pattern
    private const string SCENE_CREATED_TOPIC = "scene.created";
    private const string SCENE_UPDATED_TOPIC = "scene.updated";
    private const string SCENE_DELETED_TOPIC = "scene.deleted";
    private const string SCENE_INSTANTIATED_TOPIC = "scene.instantiated";
    private const string SCENE_DESTROYED_TOPIC = "scene.destroyed";
    private const string SCENE_CHECKED_OUT_TOPIC = "scene.checked_out";
    private const string SCENE_COMMITTED_TOPIC = "scene.committed";
    private const string SCENE_CHECKOUT_DISCARDED_TOPIC = "scene.checkout.discarded";
    private const string SCENE_CHECKOUT_EXPIRED_TOPIC = "scene.checkout.expired";
    private const string VALIDATION_RULES_UPDATED_TOPIC = "scene.validation_rules.updated";
    private const string SCENE_REFERENCE_BROKEN_TOPIC = "scene.reference.broken";

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
        ISceneValidationService validationService)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _lockProvider = lockProvider;
        _eventConsumer = eventConsumer;
        _validationService = validationService;

        // Register event consumers via partial class
        RegisterEventConsumers(_eventConsumer);
    }

    /// <summary>
    /// Registers event consumers. Extended in partial class.
    /// </summary>
    partial void RegisterEventConsumers(IEventConsumer eventConsumer);

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogDebug("Registering Scene service permissions");
        await ScenePermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
    }

    #region Scene CRUD Operations

    /// <inheritdoc />
    public async Task<(StatusCodes, SceneResponse?)> CreateSceneAsync(CreateSceneRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("CreateScene: sceneId={SceneId}, name={Name}", body.Scene.SceneId, body.Scene.Name);

        try
        {
            var scene = body.Scene;
            var sceneIdStr = scene.SceneId.ToString();

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
            var indexStore = _stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);
            var existingIndex = await indexStore.GetAsync($"{SCENE_INDEX_PREFIX}{sceneIdStr}", cancellationToken);
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

            await indexStore.SaveAsync($"{SCENE_INDEX_PREFIX}{sceneIdStr}", indexEntry, cancellationToken: cancellationToken);

            // Add to global scene index
            await AddToGlobalSceneIndexAsync(sceneIdStr, cancellationToken);

            // Update indexes
            await UpdateSceneIndexesAsync(scene, null, cancellationToken);

            // Store initial version history entry
            await AddVersionHistoryEntryAsync(sceneIdStr, scene.Version, null, cancellationToken);

            // Publish created event
            await PublishSceneCreatedEventAsync(scene, nodeCount, cancellationToken);

            _logger.LogInformation("CreateScene succeeded: sceneId={SceneId}", scene.SceneId);
            return (StatusCodes.OK, new SceneResponse { Scene = scene });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateScene");
            await _messageBus.TryPublishErrorAsync(
                "scene", "CreateScene", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/create", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, GetSceneResponse?)> GetSceneAsync(GetSceneRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("GetScene: sceneId={SceneId}, resolveReferences={ResolveReferences}", body.SceneId, body.ResolveReferences);

        try
        {
            var sceneIdStr = body.SceneId.ToString();

            // Get index entry to find asset
            var indexStore = _stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);
            var indexEntry = await indexStore.GetAsync($"{SCENE_INDEX_PREFIX}{sceneIdStr}", cancellationToken);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetScene");
            await _messageBus.TryPublishErrorAsync(
                "scene", "GetScene", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/get", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListScenesResponse?)> ListScenesAsync(ListScenesRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("ListScenes: gameId={GameId}, sceneType={SceneType}", body.GameId, body.SceneType);

        try
        {
            var indexStore = _stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);
            var stringSetStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Scene);

            // Get candidate scene IDs based on filters
            HashSet<string>? candidateIds = null;

            if (!string.IsNullOrEmpty(body.GameId))
            {
                var gameIndex = await stringSetStore.GetAsync($"{SCENE_BY_GAME_PREFIX}{body.GameId}", cancellationToken);
                candidateIds = gameIndex ?? new HashSet<string>();
            }

            if (body.SceneType != null)
            {
                var typeKey = $"{SCENE_BY_TYPE_PREFIX}{body.GameId ?? "all"}:{body.SceneType}";
                var typeIndex = await stringSetStore.GetAsync(typeKey, cancellationToken);
                if (candidateIds == null)
                {
                    candidateIds = typeIndex ?? new HashSet<string>();
                }
                else if (typeIndex != null)
                {
                    candidateIds.IntersectWith(typeIndex);
                }
            }

            if (body.SceneTypes != null && body.SceneTypes.Count > 0)
            {
                var typeUnion = new HashSet<string>();
                foreach (var sceneType in body.SceneTypes)
                {
                    var typeKey = $"{SCENE_BY_TYPE_PREFIX}{body.GameId ?? "all"}:{sceneType}";
                    var typeIndex = await stringSetStore.GetAsync(typeKey, cancellationToken);
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

            // Load and filter index entries
            var matchingScenes = new List<SceneSummary>();
            foreach (var sceneId in candidateIds)
            {
                var indexEntry = await indexStore.GetAsync($"{SCENE_INDEX_PREFIX}{sceneId}", cancellationToken);
                if (indexEntry == null) continue;

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ListScenes");
            await _messageBus.TryPublishErrorAsync(
                "scene", "ListScenes", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/list", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, SceneResponse?)> UpdateSceneAsync(UpdateSceneRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("UpdateScene: sceneId={SceneId}", body.Scene.SceneId);

        try
        {
            var scene = body.Scene;
            var sceneIdStr = scene.SceneId.ToString();
            var indexStore = _stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);

            // Get existing index entry
            var existingIndex = await indexStore.GetAsync($"{SCENE_INDEX_PREFIX}{sceneIdStr}", cancellationToken);
            if (existingIndex == null)
            {
                _logger.LogDebug("UpdateScene: Scene {SceneId} not found", scene.SceneId);
                return (StatusCodes.NotFound, null);
            }

            // Check checkout status and get editor ID if available
            Guid? editorId = null;
            if (existingIndex.IsCheckedOut)
            {
                // If we have a checkout token, validate it
                if (string.IsNullOrEmpty(body.CheckoutToken))
                {
                    _logger.LogDebug("UpdateScene: Scene {SceneId} is checked out", scene.SceneId);
                    return (StatusCodes.Conflict, null);
                }

                var checkoutStore = _stateStoreFactory.GetStore<CheckoutState>(StateStoreDefinitions.Scene);
                var checkout = await checkoutStore.GetAsync($"{SCENE_CHECKOUT_PREFIX}{sceneIdStr}", cancellationToken);
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
            existingIndex.AssetId = assetId;
            existingIndex.Name = scene.Name;
            existingIndex.Description = scene.Description;
            existingIndex.Version = scene.Version;
            existingIndex.Tags = scene.Tags?.ToList() ?? new List<string>();
            existingIndex.NodeCount = nodeCount;
            existingIndex.UpdatedAt = scene.UpdatedAt;

            await indexStore.SaveAsync($"{SCENE_INDEX_PREFIX}{sceneIdStr}", existingIndex, cancellationToken: cancellationToken);

            // Update secondary indexes
            await UpdateSceneIndexesAsync(scene, existingScene, cancellationToken);

            // Record version history entry
            await AddVersionHistoryEntryAsync(sceneIdStr, scene.Version, editorId, cancellationToken);

            // Publish updated event
            await PublishSceneUpdatedEventAsync(scene, previousVersion, nodeCount, cancellationToken);

            _logger.LogInformation("UpdateScene succeeded: sceneId={SceneId}, version={Version}", scene.SceneId, scene.Version);
            return (StatusCodes.OK, new SceneResponse { Scene = scene });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UpdateScene");
            await _messageBus.TryPublishErrorAsync(
                "scene", "UpdateScene", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/update", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, DeleteSceneResponse?)> DeleteSceneAsync(DeleteSceneRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("DeleteScene: sceneId={SceneId}", body.SceneId);

        try
        {
            var sceneIdStr = body.SceneId.ToString();
            var indexStore = _stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);
            var stringSetStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Scene);

            // Get existing index entry
            var existingIndex = await indexStore.GetAsync($"{SCENE_INDEX_PREFIX}{sceneIdStr}", cancellationToken);
            if (existingIndex == null)
            {
                _logger.LogDebug("DeleteScene: Scene {SceneId} not found", body.SceneId);
                return (StatusCodes.NotFound, null);
            }

            // Check if other scenes reference this one
            var referencingScenes = await stringSetStore.GetAsync($"{SCENE_REFERENCES_PREFIX}{sceneIdStr}", cancellationToken);
            if (referencingScenes != null && referencingScenes.Count > 0)
            {
                _logger.LogDebug("DeleteScene: Scene {SceneId} is referenced by {Count} other scenes: {ReferencingScenes}",
                    body.SceneId, referencingScenes.Count, string.Join(", ", referencingScenes));
                return (StatusCodes.Conflict, null);
            }

            // Load scene for event data
            var scene = await LoadSceneAssetAsync(existingIndex.AssetId, null, cancellationToken);

            // Remove from indexes (soft delete - asset remains until TTL)
            await indexStore.DeleteAsync($"{SCENE_INDEX_PREFIX}{sceneIdStr}", cancellationToken);
            await RemoveFromIndexesAsync(existingIndex, cancellationToken);

            // Remove from global scene index
            await RemoveFromGlobalSceneIndexAsync(sceneIdStr, cancellationToken);

            // Delete scene content
            var contentStore = _stateStoreFactory.GetStore<SceneContentEntry>(StateStoreDefinitions.Scene);
            await contentStore.DeleteAsync($"{SCENE_CONTENT_PREFIX}{sceneIdStr}", cancellationToken);

            // Delete version history
            await DeleteVersionHistoryAsync(sceneIdStr, cancellationToken);

            // Publish deleted event
            if (scene != null)
            {
                await PublishSceneDeletedEventAsync(scene, existingIndex.NodeCount, body.Reason, cancellationToken);
            }

            _logger.LogInformation("DeleteScene succeeded: sceneId={SceneId}", body.SceneId);
            return (StatusCodes.OK, new DeleteSceneResponse
            {
                Deleted = true,
                SceneId = body.SceneId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DeleteScene");
            await _messageBus.TryPublishErrorAsync(
                "scene", "DeleteScene", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/delete", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ValidationResult?)> ValidateSceneAsync(ValidateSceneRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("ValidateScene: sceneId={SceneId}", body.Scene.SceneId);

        try
        {
            // Structural validation
            var result = _validationService.ValidateStructure(body.Scene, _configuration.MaxNodeCount);

            // Apply game-specific rules if requested
            if (body.ApplyGameRules)
            {
                // Fetch game validation rules from state store
                var rulesStore = _stateStoreFactory.GetStore<List<ValidationRule>>(StateStoreDefinitions.Scene);
                var key = $"{VALIDATION_RULES_PREFIX}{body.Scene.GameId}:{body.Scene.SceneType}";
                var rules = await rulesStore.GetAsync(key, cancellationToken);

                var gameRulesResult = _validationService.ApplyGameValidationRules(body.Scene, rules);
                _validationService.MergeResults(result, gameRulesResult);
            }

            return (StatusCodes.OK, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ValidateScene");
            await _messageBus.TryPublishErrorAsync(
                "scene", "ValidateScene", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/validate", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Instantiation Operations

    /// <inheritdoc />
    public async Task<(StatusCodes, InstantiateSceneResponse?)> InstantiateSceneAsync(InstantiateSceneRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("InstantiateScene: sceneAssetId={SceneAssetId}, instanceId={InstanceId}, regionId={RegionId}",
            body.SceneAssetId, body.InstanceId, body.RegionId);

        try
        {
            var indexStore = _stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);
            var sceneAssetIdStr = body.SceneAssetId.ToString();

            // Validate scene exists
            var indexEntry = await indexStore.GetAsync($"{SCENE_INDEX_PREFIX}{sceneAssetIdStr}", cancellationToken);
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
                WorldTransform = new EventTransform
                {
                    Position = new EventVector3
                    {
                        X = body.WorldTransform.Position.X,
                        Y = body.WorldTransform.Position.Y,
                        Z = body.WorldTransform.Position.Z
                    },
                    Rotation = new EventQuaternion
                    {
                        X = body.WorldTransform.Rotation.X,
                        Y = body.WorldTransform.Rotation.Y,
                        Z = body.WorldTransform.Rotation.Z,
                        W = body.WorldTransform.Rotation.W
                    },
                    Scale = new EventVector3
                    {
                        X = body.WorldTransform.Scale.X,
                        Y = body.WorldTransform.Scale.Y,
                        Z = body.WorldTransform.Scale.Z
                    }
                },
                Metadata = body.Metadata
            };

            var published = await _messageBus.TryPublishAsync(SCENE_INSTANTIATED_TOPIC, eventModel, cancellationToken: cancellationToken);

            _logger.LogInformation("InstantiateScene succeeded: instanceId={InstanceId}", body.InstanceId);
            return (StatusCodes.OK, new InstantiateSceneResponse
            {
                InstanceId = body.InstanceId,
                SceneVersion = indexEntry.Version,
                EventPublished = published
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in InstantiateScene");
            await _messageBus.TryPublishErrorAsync(
                "scene", "InstantiateScene", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/instantiate", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, DestroyInstanceResponse?)> DestroyInstanceAsync(DestroyInstanceRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("DestroyInstance: instanceId={InstanceId}", body.InstanceId);

        try
        {
            // Publish destroyed event
            // After regeneration from updated schema, SceneAssetId/RegionId will be nullable Guid?
            // and this ?? Guid.Empty workaround can be removed (assign directly from body)
            var eventModel = new SceneDestroyedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                InstanceId = body.InstanceId,
                SceneAssetId = body.SceneAssetId ?? Guid.Empty,
                RegionId = body.RegionId ?? Guid.Empty,
                Metadata = body.Metadata
            };

            var published = await _messageBus.TryPublishAsync(SCENE_DESTROYED_TOPIC, eventModel, cancellationToken: cancellationToken);

            _logger.LogInformation("DestroyInstance succeeded: instanceId={InstanceId}", body.InstanceId);
            return (StatusCodes.OK, new DestroyInstanceResponse
            {
                Destroyed = true,
                EventPublished = published
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DestroyInstance");
            await _messageBus.TryPublishErrorAsync(
                "scene", "DestroyInstance", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/destroy-instance", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Checkout/Versioning Operations

    /// <inheritdoc />
    public async Task<(StatusCodes, CheckoutResponse?)> CheckoutSceneAsync(CheckoutRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("CheckoutScene: sceneId={SceneId}", body.SceneId);

        try
        {
            var sceneIdStr = body.SceneId.ToString();
            var indexStore = _stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);
            var checkoutStore = _stateStoreFactory.GetStore<CheckoutState>(StateStoreDefinitions.Scene);
            var indexKey = $"{SCENE_INDEX_PREFIX}{sceneIdStr}";

            // Get scene index with ETag for optimistic concurrency
            var (indexEntry, indexEtag) = await indexStore.GetWithETagAsync(indexKey, cancellationToken);
            if (indexEntry == null)
            {
                _logger.LogDebug("CheckoutScene: Scene {SceneId} not found", body.SceneId);
                return (StatusCodes.NotFound, null);
            }

            // Check if already checked out
            if (indexEntry.IsCheckedOut)
            {
                var existingCheckout = await checkoutStore.GetAsync($"{SCENE_CHECKOUT_PREFIX}{sceneIdStr}", cancellationToken);
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
                EditorId = body.EditorId ?? Guid.Empty,
                ExpiresAt = expiresAt,
                ExtensionCount = 0
            };

            var ttlSeconds = (int)TimeSpan.FromMinutes(ttlMinutes + _configuration.CheckoutTtlBufferMinutes).TotalSeconds;
            await checkoutStore.SaveAsync(
                $"{SCENE_CHECKOUT_PREFIX}{sceneIdStr}",
                checkoutState,
                new StateOptions { Ttl = ttlSeconds },
                cancellationToken);

            // Update index with optimistic concurrency
            indexEntry.IsCheckedOut = true;
            indexEntry.CheckedOutBy = checkoutState.EditorId;
            var newIndexEtag = await indexStore.TrySaveAsync(indexKey, indexEntry, indexEtag ?? string.Empty, cancellationToken);
            if (newIndexEtag == null)
            {
                _logger.LogDebug("CheckoutScene: Concurrent modification on scene index {SceneId}", body.SceneId);
                await checkoutStore.DeleteAsync($"{SCENE_CHECKOUT_PREFIX}{sceneIdStr}", cancellationToken);
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
                CheckedOutBy = checkoutState.EditorId.ToString(),
                ExpiresAt = expiresAt
            };
            await _messageBus.TryPublishAsync(SCENE_CHECKED_OUT_TOPIC, eventModel, cancellationToken: cancellationToken);

            _logger.LogInformation("CheckoutScene succeeded: sceneId={SceneId}, expiresAt={ExpiresAt}", body.SceneId, expiresAt);
            return (StatusCodes.OK, new CheckoutResponse
            {
                CheckoutToken = checkoutToken,
                Scene = scene,
                ExpiresAt = expiresAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CheckoutScene");
            await _messageBus.TryPublishErrorAsync(
                "scene", "CheckoutScene", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/checkout", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, CommitResponse?)> CommitSceneAsync(CommitRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("CommitScene: sceneId={SceneId}", body.SceneId);

        try
        {
            var sceneIdStr = body.SceneId.ToString();
            var checkoutStore = _stateStoreFactory.GetStore<CheckoutState>(StateStoreDefinitions.Scene);
            var indexStore = _stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);

            // Validate checkout token
            var checkout = await checkoutStore.GetAsync($"{SCENE_CHECKOUT_PREFIX}{sceneIdStr}", cancellationToken);
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
            var indexKey = $"{SCENE_INDEX_PREFIX}{sceneIdStr}";
            var (indexEntry, indexEtag) = await indexStore.GetWithETagAsync(indexKey, cancellationToken);
            if (indexEntry == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Update scene with UpdateSceneAsync
            var updateRequest = new UpdateSceneRequest
            {
                Scene = body.Scene,
                CheckoutToken = body.CheckoutToken
            };
            var (status, updateResponse) = await UpdateSceneAsync(updateRequest, cancellationToken);
            if (status != StatusCodes.OK || updateResponse == null)
            {
                return (status, null);
            }

            // Release checkout
            await checkoutStore.DeleteAsync($"{SCENE_CHECKOUT_PREFIX}{sceneIdStr}", cancellationToken);
            indexEntry.IsCheckedOut = false;
            indexEntry.CheckedOutBy = null;
            var newIndexEtag = await indexStore.TrySaveAsync(indexKey, indexEntry, indexEtag ?? string.Empty, cancellationToken);
            if (newIndexEtag == null)
            {
                _logger.LogDebug("CommitScene: Concurrent modification on scene index {SceneId}", body.SceneId);
                return (StatusCodes.Conflict, null);
            }

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
                CommittedBy = checkout.EditorId.ToString(),
                ChangesSummary = body.ChangesSummary,
                NodeCount = CountNodes(body.Scene.Root)
            };
            await _messageBus.TryPublishAsync(SCENE_COMMITTED_TOPIC, eventModel, cancellationToken: cancellationToken);

            _logger.LogInformation("CommitScene succeeded: sceneId={SceneId}, newVersion={Version}", body.SceneId, updateResponse.Scene.Version);
            return (StatusCodes.OK, new CommitResponse
            {
                Committed = true,
                NewVersion = updateResponse.Scene.Version,
                Scene = updateResponse.Scene
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CommitScene");
            await _messageBus.TryPublishErrorAsync(
                "scene", "CommitScene", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/commit", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, DiscardResponse?)> DiscardCheckoutAsync(DiscardRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("DiscardCheckout: sceneId={SceneId}", body.SceneId);

        try
        {
            var sceneIdStr = body.SceneId.ToString();
            var checkoutStore = _stateStoreFactory.GetStore<CheckoutState>(StateStoreDefinitions.Scene);
            var indexStore = _stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);

            // Validate checkout token
            var checkout = await checkoutStore.GetAsync($"{SCENE_CHECKOUT_PREFIX}{sceneIdStr}", cancellationToken);
            if (checkout == null || checkout.Token != body.CheckoutToken)
            {
                _logger.LogDebug("DiscardCheckout: Invalid checkout token for scene {SceneId}", body.SceneId);
                return (StatusCodes.Forbidden, null);
            }

            // Get index entry with ETag for optimistic concurrency
            var indexKey = $"{SCENE_INDEX_PREFIX}{sceneIdStr}";
            var (indexEntry, indexEtag) = await indexStore.GetWithETagAsync(indexKey, cancellationToken);

            // Release checkout
            await checkoutStore.DeleteAsync($"{SCENE_CHECKOUT_PREFIX}{sceneIdStr}", cancellationToken);
            if (indexEntry != null)
            {
                indexEntry.IsCheckedOut = false;
                indexEntry.CheckedOutBy = null;
                var newIndexEtag = await indexStore.TrySaveAsync(indexKey, indexEntry, indexEtag ?? string.Empty, cancellationToken);
                if (newIndexEtag == null)
                {
                    _logger.LogDebug("DiscardCheckout: Concurrent modification on scene index {SceneId}", body.SceneId);
                    return (StatusCodes.Conflict, null);
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
                DiscardedBy = checkout.EditorId.ToString()
            };
            await _messageBus.TryPublishAsync(SCENE_CHECKOUT_DISCARDED_TOPIC, eventModel, cancellationToken: cancellationToken);

            _logger.LogInformation("DiscardCheckout succeeded: sceneId={SceneId}", body.SceneId);
            return (StatusCodes.OK, new DiscardResponse { Discarded = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DiscardCheckout");
            await _messageBus.TryPublishErrorAsync(
                "scene", "DiscardCheckout", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/discard", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, HeartbeatResponse?)> HeartbeatCheckoutAsync(HeartbeatRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("HeartbeatCheckout: sceneId={SceneId}", body.SceneId);

        try
        {
            var sceneIdStr = body.SceneId.ToString();
            var checkoutStore = _stateStoreFactory.GetStore<CheckoutState>(StateStoreDefinitions.Scene);

            // Validate checkout token
            var checkout = await checkoutStore.GetAsync($"{SCENE_CHECKOUT_PREFIX}{sceneIdStr}", cancellationToken);
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

            // Check extension limit
            if (checkout.ExtensionCount >= _configuration.MaxCheckoutExtensions)
            {
                _logger.LogDebug("HeartbeatCheckout: Extension limit reached for scene {SceneId}", body.SceneId);
                return (StatusCodes.OK, new HeartbeatResponse
                {
                    Extended = false,
                    NewExpiresAt = checkout.ExpiresAt,
                    ExtensionsRemaining = 0
                });
            }

            // Extend checkout
            var newExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_configuration.DefaultCheckoutTtlMinutes);
            checkout.ExpiresAt = newExpiresAt;
            checkout.ExtensionCount++;

            var ttlSeconds = (int)TimeSpan.FromMinutes(_configuration.DefaultCheckoutTtlMinutes + _configuration.CheckoutTtlBufferMinutes).TotalSeconds;
            await checkoutStore.SaveAsync(
                $"{SCENE_CHECKOUT_PREFIX}{sceneIdStr}",
                checkout,
                new StateOptions { Ttl = ttlSeconds },
                cancellationToken);

            return (StatusCodes.OK, new HeartbeatResponse
            {
                Extended = true,
                NewExpiresAt = newExpiresAt,
                ExtensionsRemaining = _configuration.MaxCheckoutExtensions - checkout.ExtensionCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in HeartbeatCheckout");
            await _messageBus.TryPublishErrorAsync(
                "scene", "HeartbeatCheckout", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/heartbeat", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, HistoryResponse?)> GetSceneHistoryAsync(HistoryRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("GetSceneHistory: sceneId={SceneId}", body.SceneId);

        try
        {
            var sceneIdStr = body.SceneId.ToString();
            var indexStore = _stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);

            // Get index entry
            var indexEntry = await indexStore.GetAsync($"{SCENE_INDEX_PREFIX}{sceneIdStr}", cancellationToken);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetSceneHistory");
            await _messageBus.TryPublishErrorAsync(
                "scene", "GetSceneHistory", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/history", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Validation Rules Operations

    /// <inheritdoc />
    public async Task<(StatusCodes, RegisterValidationRulesResponse?)> RegisterValidationRulesAsync(RegisterValidationRulesRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("RegisterValidationRules: gameId={GameId}, sceneType={SceneType}, ruleCount={RuleCount}",
            body.GameId, body.SceneType, body.Rules.Count);

        try
        {
            var rulesStore = _stateStoreFactory.GetStore<List<ValidationRule>>(StateStoreDefinitions.Scene);
            var key = $"{VALIDATION_RULES_PREFIX}{body.GameId}:{body.SceneType}";

            await rulesStore.SaveAsync(key, body.Rules.ToList(), cancellationToken: cancellationToken);

            // Publish event
            var eventModel = new SceneValidationRulesUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                GameId = body.GameId,
                SceneType = body.SceneType,
                RuleCount = body.Rules.Count
            };
            await _messageBus.TryPublishAsync(VALIDATION_RULES_UPDATED_TOPIC, eventModel, cancellationToken: cancellationToken);

            _logger.LogInformation("RegisterValidationRules succeeded: gameId={GameId}, sceneType={SceneType}", body.GameId, body.SceneType);
            return (StatusCodes.OK, new RegisterValidationRulesResponse
            {
                Registered = true,
                RuleCount = body.Rules.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RegisterValidationRules");
            await _messageBus.TryPublishErrorAsync(
                "scene", "RegisterValidationRules", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/register-validation-rules", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, GetValidationRulesResponse?)> GetValidationRulesAsync(GetValidationRulesRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("GetValidationRules: gameId={GameId}, sceneType={SceneType}", body.GameId, body.SceneType);

        try
        {
            var rulesStore = _stateStoreFactory.GetStore<List<ValidationRule>>(StateStoreDefinitions.Scene);
            var key = $"{VALIDATION_RULES_PREFIX}{body.GameId}:{body.SceneType}";

            var rules = await rulesStore.GetAsync(key, cancellationToken);

            return (StatusCodes.OK, new GetValidationRulesResponse
            {
                GameId = body.GameId,
                SceneType = body.SceneType,
                Rules = rules ?? new List<ValidationRule>()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetValidationRules");
            await _messageBus.TryPublishErrorAsync(
                "scene", "GetValidationRules", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/get-validation-rules", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Query Operations

    /// <inheritdoc />
    public async Task<(StatusCodes, SearchScenesResponse?)> SearchScenesAsync(SearchScenesRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SearchScenes: query={Query}", body.Query);

        try
        {
            var indexStore = _stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);

            // Get all scene IDs (with optional game/type filter)
            var candidateIds = await GetAllSceneIdsAsync(cancellationToken);
            var results = new List<SearchResult>();

            foreach (var sceneId in candidateIds)
            {
                var indexEntry = await indexStore.GetAsync($"{SCENE_INDEX_PREFIX}{sceneId}", cancellationToken);
                if (indexEntry == null) continue;

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SearchScenes");
            await _messageBus.TryPublishErrorAsync(
                "scene", "SearchScenes", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/search", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, FindReferencesResponse?)> FindReferencesAsync(FindReferencesRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("FindReferences: sceneId={SceneId}", body.SceneId);

        try
        {
            var sceneIdStr = body.SceneId.ToString();
            var stringSetStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Scene);
            var indexStore = _stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);

            var referencingSceneIds = await stringSetStore.GetAsync($"{SCENE_REFERENCES_PREFIX}{sceneIdStr}", cancellationToken);
            var references = new List<ReferenceInfo>();

            if (referencingSceneIds != null)
            {
                foreach (var refSceneId in referencingSceneIds)
                {
                    var indexEntry = await indexStore.GetAsync($"{SCENE_INDEX_PREFIX}{refSceneId}", cancellationToken);
                    if (indexEntry == null) continue;

                    // Load scene to find the referencing node
                    var scene = await LoadSceneAssetAsync(indexEntry.AssetId, null, cancellationToken);
                    if (scene == null) continue;

                    var refNodes = FindReferenceNodes(scene.Root, sceneIdStr);
                    foreach (var node in refNodes)
                    {
                        references.Add(new ReferenceInfo
                        {
                            SceneId = Guid.Parse(refSceneId),
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in FindReferences");
            await _messageBus.TryPublishErrorAsync(
                "scene", "FindReferences", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/find-references", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, FindAssetUsageResponse?)> FindAssetUsageAsync(FindAssetUsageRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("FindAssetUsage: assetId={AssetId}", body.AssetId);

        try
        {
            var assetIdStr = body.AssetId.ToString();
            var stringSetStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Scene);
            var indexStore = _stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);

            var usingSceneIds = await stringSetStore.GetAsync($"{SCENE_ASSETS_PREFIX}{assetIdStr}", cancellationToken);
            var usages = new List<AssetUsageInfo>();

            if (usingSceneIds != null)
            {
                foreach (var sceneId in usingSceneIds)
                {
                    var indexEntry = await indexStore.GetAsync($"{SCENE_INDEX_PREFIX}{sceneId}", cancellationToken);
                    if (indexEntry == null) continue;

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
                            SceneId = Guid.Parse(sceneId),
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in FindAssetUsage");
            await _messageBus.TryPublishErrorAsync(
                "scene", "FindAssetUsage", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/find-asset-usage", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, SceneResponse?)> DuplicateSceneAsync(DuplicateSceneRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("DuplicateScene: sourceSceneId={SourceSceneId}, newName={NewName}", body.SourceSceneId, body.NewName);

        try
        {
            var sourceSceneIdStr = body.SourceSceneId.ToString();
            var indexStore = _stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);

            // Get source scene
            var sourceIndex = await indexStore.GetAsync($"{SCENE_INDEX_PREFIX}{sourceSceneIdStr}", cancellationToken);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DuplicateScene");
            await _messageBus.TryPublishErrorAsync(
                "scene", "DuplicateScene", "unexpected_exception", ex.Message,
                endpoint: "post:/scene/duplicate", stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
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
        var yaml = YamlSerializer.Serialize(scene);
        var sceneId = scene.SceneId;
        var contentKey = $"{SCENE_CONTENT_PREFIX}{sceneId}";

        // Store the YAML content directly in state store
        var contentStore = _stateStoreFactory.GetStore<SceneContentEntry>(StateStoreDefinitions.Scene);
        var contentEntry = new SceneContentEntry
        {
            SceneId = sceneId,
            Version = scene.Version,
            Content = yaml,
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await contentStore.SaveAsync(contentKey, contentEntry, null, cancellationToken);

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
        var contentKey = $"{SCENE_CONTENT_PREFIX}{assetId}";
        var contentStore = _stateStoreFactory.GetStore<SceneContentEntry>(StateStoreDefinitions.Scene);

        var contentEntry = await contentStore.GetAsync(contentKey, cancellationToken);
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
        var historyStore = _stateStoreFactory.GetStore<List<VersionHistoryEntry>>(StateStoreDefinitions.Scene);
        var historyKey = $"{SCENE_VERSION_HISTORY_PREFIX}{sceneId}";

        var historyEntries = await historyStore.GetAsync(historyKey, cancellationToken);
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
    private async Task AddVersionHistoryEntryAsync(string sceneId, string version, string? editorId, CancellationToken cancellationToken)
    {
        var historyStore = _stateStoreFactory.GetStore<List<VersionHistoryEntry>>(StateStoreDefinitions.Scene);
        var historyKey = $"{SCENE_VERSION_HISTORY_PREFIX}{sceneId}";

        var historyEntries = await historyStore.GetAsync(historyKey, cancellationToken) ?? new List<VersionHistoryEntry>();

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

        await historyStore.SaveAsync(historyKey, historyEntries, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Deletes all version history for a scene.
    /// </summary>
    private async Task DeleteVersionHistoryAsync(string sceneId, CancellationToken cancellationToken)
    {
        var historyStore = _stateStoreFactory.GetStore<List<VersionHistoryEntry>>(StateStoreDefinitions.Scene);
        var historyKey = $"{SCENE_VERSION_HISTORY_PREFIX}{sceneId}";
        await historyStore.DeleteAsync(historyKey, cancellationToken);
    }

    private async Task UpdateSceneIndexesAsync(Scene newScene, Scene? oldScene, CancellationToken cancellationToken)
    {
        var sceneIdStr = newScene.SceneId.ToString();
        var stringSetStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Scene);

        // Remove from old game/type indexes if changed
        if (oldScene != null && oldScene.GameId != newScene.GameId)
        {
            var oldGameKey = $"{SCENE_BY_GAME_PREFIX}{oldScene.GameId}";
            var oldGameIndex = await stringSetStore.GetAsync(oldGameKey, cancellationToken);
            if (oldGameIndex != null)
            {
                oldGameIndex.Remove(sceneIdStr);
                await stringSetStore.SaveAsync(oldGameKey, oldGameIndex, cancellationToken: cancellationToken);
            }
        }

        if (oldScene != null && (oldScene.GameId != newScene.GameId || oldScene.SceneType != newScene.SceneType))
        {
            var oldTypeKey = $"{SCENE_BY_TYPE_PREFIX}{oldScene.GameId}:{oldScene.SceneType}";
            var oldTypeIndex = await stringSetStore.GetAsync(oldTypeKey, cancellationToken);
            if (oldTypeIndex != null)
            {
                oldTypeIndex.Remove(sceneIdStr);
                await stringSetStore.SaveAsync(oldTypeKey, oldTypeIndex, cancellationToken: cancellationToken);
            }
        }

        // Update game index
        var gameKey = $"{SCENE_BY_GAME_PREFIX}{newScene.GameId}";
        var gameIndex = await stringSetStore.GetAsync(gameKey, cancellationToken) ?? new HashSet<string>();
        gameIndex.Add(sceneIdStr);
        await stringSetStore.SaveAsync(gameKey, gameIndex, cancellationToken: cancellationToken);

        // Update type index
        var typeKey = $"{SCENE_BY_TYPE_PREFIX}{newScene.GameId}:{newScene.SceneType}";
        var typeIndex = await stringSetStore.GetAsync(typeKey, cancellationToken) ?? new HashSet<string>();
        typeIndex.Add(sceneIdStr);
        await stringSetStore.SaveAsync(typeKey, typeIndex, cancellationToken: cancellationToken);

        // Update reference tracking
        var newReferences = ExtractSceneReferences(newScene.Root);
        var oldReferences = oldScene != null ? ExtractSceneReferences(oldScene.Root) : new HashSet<string>();

        // Add new references
        foreach (var refSceneId in newReferences.Except(oldReferences))
        {
            var refKey = $"{SCENE_REFERENCES_PREFIX}{refSceneId}";
            var refSet = await stringSetStore.GetAsync(refKey, cancellationToken) ?? new HashSet<string>();
            refSet.Add(sceneIdStr);
            await stringSetStore.SaveAsync(refKey, refSet, cancellationToken: cancellationToken);
        }

        // Remove old references
        foreach (var refSceneId in oldReferences.Except(newReferences))
        {
            var refKey = $"{SCENE_REFERENCES_PREFIX}{refSceneId}";
            var refSet = await stringSetStore.GetAsync(refKey, cancellationToken);
            if (refSet != null)
            {
                refSet.Remove(sceneIdStr);
                await stringSetStore.SaveAsync(refKey, refSet, cancellationToken: cancellationToken);
            }
        }

        // Update asset usage tracking
        var newAssets = ExtractAssetReferences(newScene.Root);
        var oldAssets = oldScene != null ? ExtractAssetReferences(oldScene.Root) : new HashSet<string>();

        foreach (var assetId in newAssets.Except(oldAssets))
        {
            var assetKey = $"{SCENE_ASSETS_PREFIX}{assetId}";
            var assetSet = await stringSetStore.GetAsync(assetKey, cancellationToken) ?? new HashSet<string>();
            assetSet.Add(sceneIdStr);
            await stringSetStore.SaveAsync(assetKey, assetSet, cancellationToken: cancellationToken);
        }

        foreach (var assetId in oldAssets.Except(newAssets))
        {
            var assetKey = $"{SCENE_ASSETS_PREFIX}{assetId}";
            var assetSet = await stringSetStore.GetAsync(assetKey, cancellationToken);
            if (assetSet != null)
            {
                assetSet.Remove(sceneIdStr);
                await stringSetStore.SaveAsync(assetKey, assetSet, cancellationToken: cancellationToken);
            }
        }
    }

    private async Task RemoveFromIndexesAsync(SceneIndexEntry indexEntry, CancellationToken cancellationToken)
    {
        var stringSetStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Scene);

        // Remove from game index
        var gameKey = $"{SCENE_BY_GAME_PREFIX}{indexEntry.GameId}";
        var gameIndex = await stringSetStore.GetAsync(gameKey, cancellationToken);
        if (gameIndex != null)
        {
            gameIndex.Remove(indexEntry.SceneId);
            await stringSetStore.SaveAsync(gameKey, gameIndex, cancellationToken: cancellationToken);
        }

        // Remove from type index
        var typeKey = $"{SCENE_BY_TYPE_PREFIX}{indexEntry.GameId}:{indexEntry.SceneType}";
        var typeIndex = await stringSetStore.GetAsync(typeKey, cancellationToken);
        if (typeIndex != null)
        {
            typeIndex.Remove(indexEntry.SceneId);
            await stringSetStore.SaveAsync(typeKey, typeIndex, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Gets all scene IDs in the system from the global index.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Set of all scene IDs.</returns>
    private async Task<HashSet<string>> GetAllSceneIdsAsync(CancellationToken cancellationToken)
    {
        var globalIndexStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Scene);
        var globalIndex = await globalIndexStore.GetAsync(SCENE_GLOBAL_INDEX_KEY, cancellationToken);
        return globalIndex ?? new HashSet<string>();
    }

    /// <summary>
    /// Adds a scene ID to the global index.
    /// </summary>
    private async Task AddToGlobalSceneIndexAsync(string sceneId, CancellationToken cancellationToken)
    {
        var globalIndexStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Scene);
        var globalIndex = await globalIndexStore.GetAsync(SCENE_GLOBAL_INDEX_KEY, cancellationToken) ?? new HashSet<string>();
        globalIndex.Add(sceneId);
        await globalIndexStore.SaveAsync(SCENE_GLOBAL_INDEX_KEY, globalIndex, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Removes a scene ID from the global index.
    /// </summary>
    private async Task RemoveFromGlobalSceneIndexAsync(string sceneId, CancellationToken cancellationToken)
    {
        var globalIndexStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Scene);
        var globalIndex = await globalIndexStore.GetAsync(SCENE_GLOBAL_INDEX_KEY, cancellationToken);
        if (globalIndex != null)
        {
            globalIndex.Remove(sceneId);
            await globalIndexStore.SaveAsync(SCENE_GLOBAL_INDEX_KEY, globalIndex, cancellationToken: cancellationToken);
        }
    }

    private async Task<(List<ResolvedReference>, List<UnresolvedReference>, List<string>)> ResolveReferencesAsync(
        Scene scene, int maxDepth, CancellationToken cancellationToken)
    {
        var resolved = new List<ResolvedReference>();
        var unresolved = new List<UnresolvedReference>();
        var errors = new List<string>();
        var visited = new HashSet<string> { scene.SceneId.ToString() };

        await ResolveReferencesRecursiveAsync(scene.Root, 1, maxDepth, visited, resolved, unresolved, errors, cancellationToken);

        return (resolved, unresolved, errors);
    }

    private async Task ResolveReferencesRecursiveAsync(
        SceneNode node, int depth, int maxDepth, HashSet<string> visited,
        List<ResolvedReference> resolved, List<UnresolvedReference> unresolved, List<string> errors,
        CancellationToken cancellationToken)
    {
        if (node.NodeType == NodeType.Reference)
        {
            var referencedSceneId = GetReferenceSceneId(node);
            if (!string.IsNullOrEmpty(referencedSceneId))
            {
                if (depth > maxDepth)
                {
                    unresolved.Add(new UnresolvedReference
                    {
                        NodeId = node.NodeId,
                        RefId = node.RefId,
                        ReferencedSceneId = Guid.Parse(referencedSceneId),
                        Reason = UnresolvedReferenceReason.Depth_exceeded
                    });
                    errors.Add($"Reference at node '{node.RefId}' exceeds max depth {maxDepth}");
                }
                else if (visited.Contains(referencedSceneId))
                {
                    unresolved.Add(new UnresolvedReference
                    {
                        NodeId = node.NodeId,
                        RefId = node.RefId,
                        ReferencedSceneId = Guid.Parse(referencedSceneId),
                        Reason = UnresolvedReferenceReason.Circular_reference,
                        CyclePath = visited.Select(Guid.Parse).ToList()
                    });
                    errors.Add($"Circular reference detected at node '{node.RefId}'");
                }
                else
                {
                    var indexStore = _stateStoreFactory.GetStore<SceneIndexEntry>(StateStoreDefinitions.Scene);
                    var indexEntry = await indexStore.GetAsync($"{SCENE_INDEX_PREFIX}{referencedSceneId}", cancellationToken);

                    if (indexEntry == null)
                    {
                        unresolved.Add(new UnresolvedReference
                        {
                            NodeId = node.NodeId,
                            RefId = node.RefId,
                            ReferencedSceneId = Guid.Parse(referencedSceneId),
                            Reason = UnresolvedReferenceReason.Not_found
                        });
                        errors.Add($"Referenced scene '{referencedSceneId}' not found");
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
                                ReferencedSceneId = Guid.Parse(referencedSceneId),
                                ReferencedVersion = referencedScene.Version,
                                Scene = referencedScene,
                                Depth = depth
                            });

                            // Recursively resolve references in the referenced scene
                            visited.Add(referencedSceneId);
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
    /// Extracts the referenced scene ID from a reference node.
    /// Checks the typed ReferenceSceneId field first, falls back to annotations for backward compatibility.
    /// </summary>
    private static string? GetReferenceSceneId(SceneNode node)
    {
        // Primary: use the typed schema field
        if (node.ReferenceSceneId.HasValue)
        {
            return node.ReferenceSceneId.Value.ToString();
        }

        // Fallback: annotations-based lookup for backward compatibility with legacy scene data
        if (node.Annotations is IDictionary<string, object> annotationsDict &&
            annotationsDict.TryGetValue("reference", out var refObj) &&
            refObj is IDictionary<string, object> refDict &&
            refDict.TryGetValue("sceneAssetId", out var sceneIdObj))
        {
            return sceneIdObj?.ToString();
        }

        return null;
    }

    private HashSet<string> ExtractSceneReferences(SceneNode root)
    {
        var references = new HashSet<string>();
        ExtractSceneReferencesRecursive(root, references);
        return references;
    }

    private void ExtractSceneReferencesRecursive(SceneNode node, HashSet<string> references)
    {
        if (node.NodeType == NodeType.Reference)
        {
            var sceneId = GetReferenceSceneId(node);
            if (!string.IsNullOrEmpty(sceneId))
            {
                references.Add(sceneId);
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

    private HashSet<string> ExtractAssetReferences(SceneNode root)
    {
        var assets = new HashSet<string>();
        ExtractAssetReferencesRecursive(root, assets);
        return assets;
    }

    private void ExtractAssetReferencesRecursive(SceneNode node, HashSet<string> assets)
    {
        if (node.Asset != null && node.Asset.AssetId != Guid.Empty)
        {
            assets.Add(node.Asset.AssetId.ToString());
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                ExtractAssetReferencesRecursive(child, assets);
            }
        }
    }

    private List<SceneNode> FindReferenceNodes(SceneNode root, string targetSceneId)
    {
        var nodes = new List<SceneNode>();
        FindReferenceNodesRecursive(root, targetSceneId, nodes);
        return nodes;
    }

    private void FindReferenceNodesRecursive(SceneNode node, string targetSceneId, List<SceneNode> nodes)
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
            SceneId = Guid.Parse(indexEntry.SceneId),
            GameId = indexEntry.GameId,
            SceneType = Enum.TryParse<SceneType>(indexEntry.SceneType, out var type) ? type : SceneType.Unknown,
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

    private Scene DuplicateSceneWithNewIds(Scene source, string newName, string? newGameId, SceneType? newSceneType)
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
        var eventModel = new SceneCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = scene.SceneId,
            GameId = scene.GameId,
            SceneType = scene.SceneType.ToString(),
            Name = scene.Name,
            Description = scene.Description,
            Version = scene.Version,
            Tags = scene.Tags?.ToList() ?? new List<string>(),
            NodeCount = nodeCount,
            CreatedAt = scene.CreatedAt,
            UpdatedAt = scene.UpdatedAt
        };

        await _messageBus.TryPublishAsync(SCENE_CREATED_TOPIC, eventModel, cancellationToken: cancellationToken);
    }

    private async Task PublishSceneUpdatedEventAsync(Scene scene, string previousVersion, int nodeCount, CancellationToken cancellationToken)
    {
        var eventModel = new SceneUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = scene.SceneId,
            GameId = scene.GameId,
            SceneType = scene.SceneType.ToString(),
            Name = scene.Name,
            Description = scene.Description,
            Version = scene.Version,
            Tags = scene.Tags?.ToList() ?? new List<string>(),
            NodeCount = nodeCount,
            CreatedAt = scene.CreatedAt,
            UpdatedAt = scene.UpdatedAt
        };

        await _messageBus.TryPublishAsync(SCENE_UPDATED_TOPIC, eventModel, cancellationToken: cancellationToken);
    }

    private async Task PublishSceneDeletedEventAsync(Scene scene, int nodeCount, string? reason, CancellationToken cancellationToken)
    {
        var eventModel = new SceneDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = scene.SceneId,
            GameId = scene.GameId,
            SceneType = scene.SceneType.ToString(),
            Name = scene.Name,
            Description = scene.Description,
            Version = scene.Version,
            Tags = scene.Tags?.ToList() ?? new List<string>(),
            NodeCount = nodeCount,
            CreatedAt = scene.CreatedAt,
            UpdatedAt = scene.UpdatedAt
        };

        await _messageBus.TryPublishAsync(SCENE_DELETED_TOPIC, eventModel, cancellationToken: cancellationToken);
    }

    #endregion
}

#region Internal Models

/// <summary>
/// Index entry for efficient scene queries.
/// </summary>
internal class SceneIndexEntry
{
    public Guid SceneId { get; set; }
    public Guid AssetId { get; set; }
    public string GameId { get; set; } = string.Empty;
    public SceneType SceneType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public int NodeCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsCheckedOut { get; set; }
    public Guid? CheckedOutBy { get; set; }
}

/// <summary>
/// Checkout state stored in lib-state.
/// </summary>
internal class CheckoutState
{
    public Guid SceneId { get; set; }
    public string Token { get; set; } = string.Empty;
    public Guid EditorId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int ExtensionCount { get; set; }
}

/// <summary>
/// Scene content entry stored in lib-state (YAML serialized scene).
/// </summary>
internal class SceneContentEntry
{
    public Guid SceneId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public long UpdatedAt { get; set; }
}

/// <summary>
/// Version history entry for tracking scene version changes.
/// </summary>
internal class VersionHistoryEntry
{
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

#endregion
