using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Asset.Bundles;
using BeyondImmersion.BannouService.Asset.Events;
using BeyondImmersion.BannouService.Asset.Models;
using BeyondImmersion.BannouService.Asset.Storage;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StorageModels = BeyondImmersion.BannouService.Storage;

namespace BeyondImmersion.BannouService.Asset;

/// <summary>
/// Implementation of the Asset service.
/// This class contains the business logic for all Asset operations.
/// </summary>
[BannouServiceAttribute("asset", typeof(IAssetService), lifetime: ServiceLifetime.Scoped)]
public partial class AssetService : IAssetService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<AssetService> _logger;
    private readonly AssetServiceConfiguration _configuration;
    private readonly IAssetEventEmitter _eventEmitter;
    private readonly StorageModels.IAssetStorageProvider _storageProvider;
    private readonly IOrchestratorClient _orchestratorClient;
    private readonly IBundleConverter _bundleConverter;

    private const string STATE_STORE = "asset-statestore";
    private const string UPLOAD_SESSION_PREFIX = "upload:";
    private const string ASSET_PREFIX = "asset:";
    private const string ASSET_INDEX_PREFIX = "asset-index:";
    private const string BUNDLE_PREFIX = "bundle:";

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetService"/> class.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for state operations.</param>
    /// <param name="messageBus">Message bus for pub/sub operations.</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="errorEventEmitter">Error event emitter for unexpected failures.</param>
    /// <param name="eventEmitter">Client event emitter for WebSocket notifications.</param>
    /// <param name="storageProvider">Storage provider abstraction for S3/MinIO.</param>
    /// <param name="orchestratorClient">Orchestrator client for processor pool management.</param>
    /// <param name="bundleConverter">Bundle format converter (.bannou ↔ .zip).</param>
    /// <param name="eventConsumer">Event consumer for pub/sub event handling.</param>
    public AssetService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<AssetService> logger,
        AssetServiceConfiguration configuration,
        IAssetEventEmitter eventEmitter,
        StorageModels.IAssetStorageProvider storageProvider,
        IOrchestratorClient orchestratorClient,
        IBundleConverter bundleConverter,
        IEventConsumer eventConsumer)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _eventEmitter = eventEmitter;
        _storageProvider = storageProvider;
        _orchestratorClient = orchestratorClient;
        _bundleConverter = bundleConverter;

        // Register event handlers via partial class
        RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Implementation of RequestUpload operation.
    /// Validates request, generates upload token, and returns pre-signed URL(s).
    /// </summary>
    public async Task<(StatusCodes, UploadResponse?)> RequestUploadAsync(UploadRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RequestUpload: filename={Filename}, size={Size}, contentType={ContentType}",
            body.Filename, body.Size, body.ContentType);

        try
        {
            // Validate request
            if (string.IsNullOrWhiteSpace(body.Filename))
            {
                _logger.LogWarning("RequestUpload: Empty filename");
                return (StatusCodes.BadRequest, null);
            }

            var maxSizeBytes = (long)_configuration.MaxUploadSizeMb * 1024 * 1024;
            if (body.Size <= 0 || body.Size > maxSizeBytes)
            {
                _logger.LogWarning("RequestUpload: Invalid size {Size}, max={MaxSize}", body.Size, maxSizeBytes);
                return (StatusCodes.BadRequest, null);
            }

            if (string.IsNullOrWhiteSpace(body.ContentType))
            {
                _logger.LogWarning("RequestUpload: Empty content type");
                return (StatusCodes.BadRequest, null);
            }

            // Generate upload session
            var uploadId = Guid.NewGuid();
            var multipartThreshold = (long)_configuration.MultipartThresholdMb * 1024 * 1024;
            var isMultipart = body.Size > multipartThreshold;
            var expiration = TimeSpan.FromSeconds(_configuration.TokenTtlSeconds);

            // Generate storage key: temp/{uploadId}/{filename}
            var storageKey = $"temp/{uploadId:N}/{SanitizeFilename(body.Filename)}";

            UploadResponse response;

            if (isMultipart)
            {
                // Calculate part count
                var partSizeBytes = (long)_configuration.MultipartPartSizeMb * 1024 * 1024;
                var partCount = (int)Math.Ceiling((double)body.Size / partSizeBytes);

                // Generate multipart upload URLs
                var multipartResult = await _storageProvider.InitiateMultipartUploadAsync(
                    _configuration.StorageBucket,
                    storageKey,
                    body.ContentType,
                    partCount,
                    expiration).ConfigureAwait(false);

                // Build response with multipart config
                var uploadUrls = multipartResult.Parts.Select(p => new PartUploadInfo
                {
                    PartNumber = p.PartNumber,
                    UploadUrl = new Uri(p.UploadUrl),
                    MinSize = p.MinSize,
                    MaxSize = p.MaxSize
                }).ToList();

                response = new UploadResponse
                {
                    UploadId = uploadId,
                    UploadUrl = new Uri(multipartResult.Parts.First().UploadUrl), // First part URL as primary
                    ExpiresAt = multipartResult.ExpiresAt,
                    Multipart = new MultipartConfig
                    {
                        Required = true,
                        PartSize = (int)partSizeBytes,
                        MaxParts = partCount,
                        UploadUrls = uploadUrls
                    }
                };

                // Store upload session
                var session = new UploadSession
                {
                    UploadId = uploadId,
                    Filename = body.Filename,
                    Size = body.Size,
                    ContentType = body.ContentType,
                    Metadata = body.Metadata,
                    StorageKey = storageKey,
                    IsMultipart = true,
                    PartCount = partCount,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = multipartResult.ExpiresAt
                };

                var sessionStore = _stateStoreFactory.GetStore<UploadSession>(STATE_STORE);
                await sessionStore.SaveAsync($"{UPLOAD_SESSION_PREFIX}{uploadId:N}", session, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Generate single upload URL
                var uploadResult = await _storageProvider.GenerateUploadUrlAsync(
                    _configuration.StorageBucket,
                    storageKey,
                    body.ContentType,
                    body.Size,
                    expiration,
                    body.Metadata?.Tags != null ? new Dictionary<string, string> { { "tags", string.Join(",", body.Metadata.Tags) } } : null)
                    .ConfigureAwait(false);

                response = new UploadResponse
                {
                    UploadId = uploadId,
                    UploadUrl = new Uri(uploadResult.UploadUrl),
                    ExpiresAt = uploadResult.ExpiresAt,
                    Multipart = new MultipartConfig
                    {
                        Required = false,
                        PartSize = 0,
                        MaxParts = 0,
                        UploadUrls = Array.Empty<PartUploadInfo>()
                    }
                };

                // Store upload session
                var session = new UploadSession
                {
                    UploadId = uploadId,
                    Filename = body.Filename,
                    Size = body.Size,
                    ContentType = body.ContentType,
                    Metadata = body.Metadata,
                    StorageKey = storageKey,
                    IsMultipart = false,
                    PartCount = 0,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = uploadResult.ExpiresAt
                };

                var sessionStore = _stateStoreFactory.GetStore<UploadSession>(STATE_STORE);
                await sessionStore.SaveAsync($"{UPLOAD_SESSION_PREFIX}{uploadId:N}", session, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("RequestUpload: Created upload session {UploadId}, multipart={IsMultipart}",
                uploadId, isMultipart);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing RequestUpload operation");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "RequestUpload",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/assets/upload/request",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of CompleteUpload operation.
    /// Validates upload, moves file to final location, and creates asset metadata.
    /// </summary>
    public async Task<(StatusCodes, AssetMetadata?)> CompleteUploadAsync(CompleteUploadRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("CompleteUpload: uploadId={UploadId}", body.UploadId);

        try
        {
            // Retrieve upload session
            var sessionStore = _stateStoreFactory.GetStore<UploadSession>(STATE_STORE);
            var session = await sessionStore.GetAsync($"{UPLOAD_SESSION_PREFIX}{body.UploadId:N}", cancellationToken).ConfigureAwait(false);

            if (session == null)
            {
                _logger.LogWarning("CompleteUpload: Upload session not found {UploadId}", body.UploadId);
                return (StatusCodes.NotFound, null);
            }

            // Check expiration
            if (DateTimeOffset.UtcNow > session.ExpiresAt)
            {
                _logger.LogWarning("CompleteUpload: Upload session expired {UploadId}", body.UploadId);
                await sessionStore.DeleteAsync($"{UPLOAD_SESSION_PREFIX}{body.UploadId:N}", cancellationToken).ConfigureAwait(false);
                return (StatusCodes.BadRequest, null); // Session expired
            }

            // For multipart uploads, complete the upload
            if (session.IsMultipart)
            {
                if (body.Parts == null || body.Parts.Count != session.PartCount)
                {
                    _logger.LogWarning("CompleteUpload: Invalid part count. Expected={Expected}, Got={Got}",
                        session.PartCount, body.Parts?.Count ?? 0);
                    return (StatusCodes.BadRequest, null);
                }

                var storageParts = body.Parts.Select(p => new StorageModels.StorageCompletedPart(p.PartNumber, p.Etag)).ToList();
                await _storageProvider.CompleteMultipartUploadAsync(
                    _configuration.StorageBucket,
                    session.StorageKey,
                    body.UploadId.ToString("N"),
                    storageParts).ConfigureAwait(false);
            }

            // Verify file exists in temp location
            var exists = await _storageProvider.ObjectExistsAsync(
                _configuration.StorageBucket,
                session.StorageKey).ConfigureAwait(false);

            if (!exists)
            {
                _logger.LogWarning("CompleteUpload: File not found in temp location {Key}", session.StorageKey);
                return (StatusCodes.NotFound, null);
            }

            // Get file metadata for hash calculation
            var objectMeta = await _storageProvider.GetObjectMetadataAsync(
                _configuration.StorageBucket,
                session.StorageKey).ConfigureAwait(false);

            // Generate asset ID from content hash (ETag is typically MD5, we'll use it as-is for now)
            var contentHash = objectMeta.ETag.Replace("\"", ""); // Remove quotes from ETag
            var assetId = GenerateAssetId(session.ContentType, contentHash);

            // Determine final storage key
            var assetType = session.Metadata?.AssetType ?? AssetType.Other;
            var extension = Path.GetExtension(session.Filename);
            var finalKey = $"assets/{assetType.ToString().ToLowerInvariant()}/{assetId}{extension}";

            // Copy to final location
            var assetRef = await _storageProvider.CopyObjectAsync(
                _configuration.StorageBucket,
                session.StorageKey,
                _configuration.StorageBucket,
                finalKey).ConfigureAwait(false);

            // Delete temp file
            await _storageProvider.DeleteObjectAsync(
                _configuration.StorageBucket,
                session.StorageKey).ConfigureAwait(false);

            // Create asset metadata
            var now = DateTimeOffset.UtcNow;
            var isLargeFile = assetRef.Size > (_configuration.LargeFileThresholdMb * 1024L * 1024L);
            var requiresProcessing = isLargeFile && RequiresProcessing(session.ContentType);

            // Create internal record with storage details (for bundle creation and internal ops)
            var internalRecord = new InternalAssetRecord
            {
                AssetId = assetId,
                ContentHash = contentHash,
                Filename = session.Filename,
                ContentType = session.ContentType,
                Size = assetRef.Size,
                AssetType = session.Metadata?.AssetType ?? AssetType.Other,
                Realm = session.Metadata?.Realm ?? Asset.Realm.Shared,
                Tags = session.Metadata?.Tags ?? new List<string>(),
                ProcessingStatus = requiresProcessing ? ProcessingStatus.Pending : ProcessingStatus.Complete,
                StorageKey = finalKey,
                Bucket = _configuration.StorageBucket,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Store internal asset record (includes storage details for bundle creation)
            var assetStore = _stateStoreFactory.GetStore<InternalAssetRecord>(STATE_STORE);
            await assetStore.SaveAsync($"{ASSET_PREFIX}{assetId}", internalRecord, cancellationToken: cancellationToken).ConfigureAwait(false);

            // Convert to public metadata for return value and events
            var assetMetadata = internalRecord.ToPublicMetadata();
            assetMetadata.IsArchived = false; // New assets are always in standard storage

            // Store index entries for search
            await IndexAssetAsync(assetMetadata, cancellationToken).ConfigureAwait(false);

            // Delete upload session
            await sessionStore.DeleteAsync($"{UPLOAD_SESSION_PREFIX}{body.UploadId:N}", cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("CompleteUpload: Asset created {AssetId}, finalKey={FinalKey}, requiresProcessing={RequiresProcessing}",
                assetId, finalKey, requiresProcessing);

            // Publish asset.upload.completed event
            await _messageBus.TryPublishAsync(
                "asset.upload.completed",
                new BeyondImmersion.BannouService.Events.AssetUploadCompletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    AssetId = assetId,
                    UploadId = body.UploadId,
                    SessionId = "system", // TODO: Get from context when auth is integrated
                    AccountId = null,
                    Bucket = _configuration.StorageBucket,
                    Key = finalKey,
                    Size = assetRef.Size,
                    ContentHash = contentHash,
                    ContentType = session.ContentType
                }).ConfigureAwait(false);

            // If the file requires processing, delegate to the processing pool
            if (requiresProcessing)
            {
                await DelegateToProcessingPoolAsync(assetId, assetMetadata, finalKey, cancellationToken).ConfigureAwait(false);
            }

            return (StatusCodes.Created, assetMetadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing CompleteUpload operation");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "CompleteUpload",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/assets/upload/complete",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetAsset operation.
    /// Retrieves asset metadata and generates download URL.
    /// </summary>
    public async Task<(StatusCodes, AssetWithDownloadUrl?)> GetAssetAsync(GetAssetRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetAsset: assetId={AssetId}, version={Version}", body.AssetId, body.Version);

        try
        {
            // Retrieve internal asset record (includes storage details)
            var assetStore = _stateStoreFactory.GetStore<InternalAssetRecord>(STATE_STORE);
            var internalRecord = await assetStore.GetAsync($"{ASSET_PREFIX}{body.AssetId}", cancellationToken).ConfigureAwait(false);

            if (internalRecord == null)
            {
                _logger.LogWarning("GetAsset: Asset not found {AssetId}", body.AssetId);
                return (StatusCodes.NotFound, null);
            }

            // Resolve version
            string? versionId = null;
            if (body.Version != "latest" && !string.IsNullOrEmpty(body.Version))
            {
                versionId = body.Version;
            }

            // Generate download URL using stored storage key
            var expiration = TimeSpan.FromSeconds(_configuration.DownloadTokenTtlSeconds);
            var downloadResult = await _storageProvider.GenerateDownloadUrlAsync(
                internalRecord.Bucket,
                internalRecord.StorageKey,
                versionId,
                expiration).ConfigureAwait(false);

            // Convert to public metadata
            var metadata = internalRecord.ToPublicMetadata();

            var response = new AssetWithDownloadUrl
            {
                AssetId = internalRecord.AssetId,
                VersionId = versionId ?? "latest",
                DownloadUrl = new Uri(downloadResult.DownloadUrl),
                ExpiresAt = downloadResult.ExpiresAt,
                Size = internalRecord.Size,
                ContentHash = internalRecord.ContentHash,
                ContentType = internalRecord.ContentType,
                Metadata = metadata
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetAsset operation");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "GetAsset",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/assets/get",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of ListAssetVersions operation.
    /// Returns list of all versions for an asset.
    /// </summary>
    public async Task<(StatusCodes, AssetVersionList?)> ListAssetVersionsAsync(ListVersionsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("ListAssetVersions: assetId={AssetId}", body.AssetId);

        try
        {
            // Retrieve internal asset record to verify it exists and get storage details
            var assetStore = _stateStoreFactory.GetStore<InternalAssetRecord>(STATE_STORE);
            var internalRecord = await assetStore.GetAsync($"{ASSET_PREFIX}{body.AssetId}", cancellationToken).ConfigureAwait(false);

            if (internalRecord == null)
            {
                _logger.LogWarning("ListAssetVersions: Asset not found {AssetId}", body.AssetId);
                return (StatusCodes.NotFound, null);
            }

            // List versions from storage using stored key
            var storageVersions = await _storageProvider.ListVersionsAsync(
                internalRecord.Bucket,
                internalRecord.StorageKey).ConfigureAwait(false);

            // Apply pagination
            var total = storageVersions.Count;
            var paginatedVersions = storageVersions
                .Skip(body.Offset)
                .Take(body.Limit)
                .Select(v => new AssetVersion
                {
                    VersionId = v.VersionId,
                    CreatedAt = v.LastModified,
                    Size = v.Size,
                    IsArchived = v.IsArchived // Uses StorageClass to detect archival tier
                })
                .ToList();

            var response = new AssetVersionList
            {
                AssetId = body.AssetId,
                Versions = paginatedVersions,
                Total = total,
                Limit = body.Limit,
                Offset = body.Offset
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ListAssetVersions operation");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "ListAssetVersions",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/assets/list-versions",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of SearchAssets operation.
    /// Searches assets by tags, type, realm, and content type using RedisSearch.
    /// Requires Redis Stack with RediSearch module for query support.
    /// </summary>
    public async Task<(StatusCodes, AssetSearchResult?)> SearchAssetsAsync(AssetSearchRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SearchAssets: tags={Tags}, assetType={AssetType}, realm={Realm}",
            body.Tags != null ? string.Join(",", body.Tags) : "none",
            body.AssetType,
            body.Realm);

        try
        {
            // Check if search is supported for this store
            if (!_stateStoreFactory.SupportsSearch(STATE_STORE))
            {
                _logger.LogDebug("Search not supported for store {Store}, using index fallback", STATE_STORE);
                return await SearchAssetsIndexFallbackAsync(body, cancellationToken).ConfigureAwait(false);
            }

            var searchStore = _stateStoreFactory.GetSearchableStore<AssetMetadata>(STATE_STORE);

            // Build RedisSearch query
            // Format: @asset_type:{type} @realm:{realm} [@content_type:{content_type}]
            var queryParts = new List<string>
            {
                $"@asset_type:{{{body.AssetType}}}",
                $"@realm:{{{body.Realm}}}"
            };

            if (!string.IsNullOrEmpty(body.ContentType))
            {
                // Escape special characters in content type
                var escapedContentType = body.ContentType.Replace("/", "\\/");
                queryParts.Add($"@content_type:{{{escapedContentType}}}");
            }

            var query = string.Join(" ", queryParts);

            _logger.LogDebug("Executing asset search query: {Query}", query);

            // Execute the search
            var searchResult = await searchStore.SearchAsync(
                "assetMetadataIndex",
                query,
                new SearchQueryOptions
                {
                    Offset = 0, // Get all matching to filter tags in-memory
                    Limit = body.Limit + body.Offset + 1000, // Get enough to paginate after tag filter
                    SortBy = "created_at",
                    SortDescending = true
                },
                cancellationToken).ConfigureAwait(false);

            // Filter by tags in-memory (tags are arrays, complex for Redis query)
            var matchingAssets = searchResult.Items
                .Select(r => r.Value)
                .Where(m => m != null)
                .Where(m => body.Tags == null || body.Tags.Count == 0 ||
                    (m!.Tags != null && body.Tags.All(t => m.Tags.Contains(t))))
                .ToList();

            // Apply pagination
            var total = matchingAssets.Count;
            var paginatedAssets = matchingAssets
                .Skip(body.Offset)
                .Take(body.Limit)
                .ToList();

            var response = new AssetSearchResult
            {
                Assets = paginatedAssets!,
                Total = total,
                Limit = body.Limit,
                Offset = body.Offset
            };

            return (StatusCodes.OK, response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("search") || ex.Message.Contains("Search"))
        {
            // Search not available, fall back to index-based search
            _logger.LogError(ex, "Search store not available, falling back to index-based search - infrastructure degraded");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "SearchAssets",
                "search_infrastructure_degraded",
                "RedisSearch not available, using fallback search",
                dependency: "redis_search",
                endpoint: "post:/assets/search",
                details: ex.Message,
                stack: ex.StackTrace);
            return await SearchAssetsIndexFallbackAsync(body, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing SearchAssets operation");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "SearchAssets",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/assets/search",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Fallback search using index keys when RedisSearch is not available.
    /// Used when Redis Stack is not configured or search module is unavailable.
    /// </summary>
    private async Task<(StatusCodes, AssetSearchResult?)> SearchAssetsIndexFallbackAsync(
        AssetSearchRequest body,
        CancellationToken cancellationToken)
    {
        var matchingAssets = new List<AssetMetadata>();

        // Search by asset type index
        var indexStore = _stateStoreFactory.GetStore<List<string>>(STATE_STORE);
        var assetStore = _stateStoreFactory.GetStore<InternalAssetRecord>(STATE_STORE);

        var indexKey = $"{ASSET_INDEX_PREFIX}type:{body.AssetType.ToString().ToLowerInvariant()}";
        var assetIds = await indexStore.GetAsync(indexKey, cancellationToken).ConfigureAwait(false);

        if (assetIds != null)
        {
            foreach (var assetId in assetIds)
            {
                var internalRecord = await assetStore.GetAsync($"{ASSET_PREFIX}{assetId}", cancellationToken).ConfigureAwait(false);

                if (internalRecord != null)
                {
                    // Apply filters
                    var matchesRealm = internalRecord.Realm == body.Realm;
                    var matchesTags = body.Tags == null || body.Tags.Count == 0 ||
                        (internalRecord.Tags != null && body.Tags.All(t => internalRecord.Tags.Contains(t)));
                    var matchesContentType = string.IsNullOrEmpty(body.ContentType) ||
                        internalRecord.ContentType == body.ContentType;

                    if (matchesRealm && matchesTags && matchesContentType)
                    {
                        matchingAssets.Add(internalRecord.ToPublicMetadata());
                    }
                }
            }
        }

        // Apply pagination
        var total = matchingAssets.Count;
        var paginatedAssets = matchingAssets
            .OrderByDescending(a => a.CreatedAt)
            .Skip(body.Offset)
            .Take(body.Limit)
            .ToList();

        var response = new AssetSearchResult
        {
            Assets = paginatedAssets,
            Total = total,
            Limit = body.Limit,
            Offset = body.Offset
        };

        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Implementation of CreateBundle operation.
    /// Creates a .bannou bundle from multiple assets.
    /// For large bundles, processing is delegated to the processing pool.
    /// </summary>
    public async Task<(StatusCodes, CreateBundleResponse?)> CreateBundleAsync(CreateBundleRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("CreateBundle: bundleId={BundleId}, assetCount={AssetCount}",
            body.BundleId, body.AssetIds?.Count ?? 0);

        try
        {
            // Validate request
            if (string.IsNullOrWhiteSpace(body.BundleId))
            {
                _logger.LogWarning("CreateBundle: Empty bundle_id");
                return (StatusCodes.BadRequest, null);
            }

            if (body.AssetIds == null || body.AssetIds.Count == 0)
            {
                _logger.LogWarning("CreateBundle: No asset_ids provided");
                return (StatusCodes.BadRequest, null);
            }

            // Check if bundle already exists
            var bundleStore = _stateStoreFactory.GetStore<BundleMetadata>(STATE_STORE);
            var assetStore = _stateStoreFactory.GetStore<InternalAssetRecord>(STATE_STORE);

            var bundleKey = $"{BUNDLE_PREFIX}{body.BundleId}";
            var existingBundle = await bundleStore.GetAsync(bundleKey, cancellationToken);

            if (existingBundle != null)
            {
                _logger.LogWarning("CreateBundle: Bundle {BundleId} already exists", body.BundleId);
                return (StatusCodes.Conflict, null);
            }

            // Validate all asset IDs exist and collect metadata
            var assetRecords = new List<InternalAssetRecord>();
            long totalSize = 0;

            foreach (var assetId in body.AssetIds)
            {
                var assetKey = $"{ASSET_PREFIX}{assetId}";
                var assetRecord = await assetStore.GetAsync(assetKey, cancellationToken);

                if (assetRecord == null)
                {
                    _logger.LogWarning("CreateBundle: Asset {AssetId} not found", assetId);
                    return (StatusCodes.BadRequest, null);
                }

                assetRecords.Add(assetRecord);
                totalSize += assetRecord.Size;
            }

            // Determine if we should delegate to processing pool (for large bundles)
            var largeFileThreshold = (long)_configuration.LargeFileThresholdMb * 1024 * 1024;
            var delegateToPool = totalSize > largeFileThreshold;

            if (delegateToPool)
            {
                // Queue bundle creation job to processing pool
                var jobId = Guid.NewGuid().ToString();
                var job = new BundleCreationJob
                {
                    JobId = jobId,
                    BundleId = body.BundleId,
                    Version = body.Version ?? "1.0.0",
                    AssetIds = body.AssetIds.ToList(),
                    Compression = body.Compression,
                    Metadata = ConvertMetadataToDictionary(body.Metadata),
                    Status = BundleCreationStatus.Queued,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                // Store job state
                var jobStore = _stateStoreFactory.GetStore<BundleCreationJob>(STATE_STORE);
                var jobKey = $"bundle-job:{jobId}";
                await jobStore.SaveAsync(jobKey, job, cancellationToken: cancellationToken);

                // Publish job event for processing pool
                await _messageBus.TryPublishAsync("asset.bundle.create", job);

                _logger.LogInformation(
                    "CreateBundle: Queued bundle creation job {JobId} for bundle {BundleId}",
                    jobId,
                    body.BundleId);

                return (StatusCodes.Accepted, new CreateBundleResponse
                {
                    BundleId = body.BundleId,
                    Status = CreateBundleResponseStatus.Queued,
                    EstimatedSize = totalSize
                });
            }

            // For smaller bundles, create inline
            var bundlePath = $"bundles/current/{body.BundleId}.bundle";
            var bucket = _configuration.StorageBucket;

            // Create bundle in memory and upload
            using var bundleStream = new MemoryStream();
            using var writer = new BannouBundleWriter(bundleStream);

            foreach (var assetRecord in assetRecords)
            {
                // Download asset data
                using var assetData = await _storageProvider.GetObjectAsync(bucket, assetRecord.StorageKey);

                using var assetStream = new MemoryStream();
                await assetData.CopyToAsync(assetStream, cancellationToken);
                var data = assetStream.ToArray();

                writer.AddAsset(
                    assetRecord.AssetId,
                    assetRecord.Filename,
                    assetRecord.ContentType,
                    data);
            }

            writer.Finalize(
                body.BundleId,
                body.BundleId,
                body.Version ?? "1.0.0",
                "system",
                null,
                ConvertMetadataToStringDictionary(body.Metadata));

            // Upload bundle
            bundleStream.Position = 0;
            await _storageProvider.PutObjectAsync(
                bucket,
                bundlePath,
                bundleStream,
                bundleStream.Length,
                "application/x-bannou-bundle");

            // Store bundle metadata
            var bundleMetadata = new BundleMetadata
            {
                BundleId = body.BundleId,
                Version = body.Version ?? "1.0.0",
                AssetIds = body.AssetIds.ToList(),
                StorageKey = bundlePath,
                SizeBytes = bundleStream.Length,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = BundleStatus.Ready
            };

            await bundleStore.SaveAsync(bundleKey, bundleMetadata, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "CreateBundle: Created bundle {BundleId} with {AssetCount} assets ({Size} bytes)",
                body.BundleId,
                body.AssetIds.Count,
                bundleStream.Length);

            // Publish asset.bundle.created event
            await _messageBus.TryPublishAsync(
                "asset.bundle.created",
                new BeyondImmersion.BannouService.Events.BundleCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    BundleId = body.BundleId,
                    Version = body.Version ?? "1.0.0",
                    Bucket = _configuration.StorageBucket,
                    Key = bundlePath,
                    Size = bundleStream.Length,
                    AssetCount = body.AssetIds.Count,
                    Compression = null, // TODO: Map compression type when implemented
                    CreatedBy = null // TODO: Get from context when auth is integrated
                }).ConfigureAwait(false);

            return (StatusCodes.OK, new CreateBundleResponse
            {
                BundleId = body.BundleId,
                Status = CreateBundleResponseStatus.Ready,
                EstimatedSize = bundleStream.Length
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing CreateBundle operation");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "CreateBundle",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/bundles/create",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetBundle operation.
    /// TODO: Implement in Phase 5 (Bundle System).
    /// </summary>
    public async Task<(StatusCodes, BundleWithDownloadUrl?)> GetBundleAsync(GetBundleRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetBundle: bundleId={BundleId}, format={Format}", body.BundleId, body.Format);

        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(body.BundleId))
            {
                return (StatusCodes.BadRequest, null);
            }

            // Look up bundle metadata
            var bundleStore = _stateStoreFactory.GetStore<BundleMetadata>(STATE_STORE);
            var bundleKey = $"{BUNDLE_PREFIX}{body.BundleId}";
            var bundleMetadata = await bundleStore.GetAsync(bundleKey, cancellationToken);

            if (bundleMetadata == null)
            {
                _logger.LogWarning("GetBundle: Bundle not found: {BundleId}", body.BundleId);
                return (StatusCodes.NotFound, null);
            }

            // Check if bundle is ready
            if (bundleMetadata.Status != BundleStatus.Ready)
            {
                _logger.LogWarning("GetBundle: Bundle not ready: {BundleId}, status={Status}",
                    body.BundleId, bundleMetadata.Status);
                return (StatusCodes.Conflict, null);
            }

            var bucket = _configuration.StorageBucket;
            var fromCache = false;
            string downloadPath;
            long downloadSize;

            if (body.Format == BundleFormat.Zip)
            {
                // Check ZIP cache
                var zipCachePath = $"bundles/zip-cache/{body.BundleId}.zip";
                var cacheExists = await _storageProvider.ObjectExistsAsync(bucket, zipCachePath);

                if (cacheExists)
                {
                    // Use cached ZIP
                    downloadPath = zipCachePath;
                    fromCache = true;
                    var cachedMeta = await _storageProvider.GetObjectMetadataAsync(bucket, zipCachePath);
                    downloadSize = cachedMeta.ContentLength;
                    _logger.LogDebug("GetBundle: Using cached ZIP for {BundleId}", body.BundleId);
                }
                else
                {
                    // Download bundle, convert to ZIP, cache, and return
                    _logger.LogDebug("GetBundle: Converting bundle to ZIP for {BundleId}", body.BundleId);

                    using var bundleStream = await _storageProvider.GetObjectAsync(
                        bucket, bundleMetadata.StorageKey);

                    using var zipStream = new MemoryStream();
                    var converted = await _bundleConverter.ConvertBundleToZipAsync(
                        bundleStream,
                        zipStream,
                        body.BundleId,
                        cancellationToken);

                    if (!converted)
                    {
                        _logger.LogError("GetBundle: Failed to convert bundle to ZIP: {BundleId}", body.BundleId);
                        return (StatusCodes.InternalServerError, null);
                    }

                    // Upload ZIP to cache
                    zipStream.Position = 0;
                    await _storageProvider.PutObjectAsync(
                        bucket,
                        zipCachePath,
                        zipStream,
                        zipStream.Length,
                        "application/zip");

                    downloadPath = zipCachePath;
                    downloadSize = zipStream.Length;
                    fromCache = false;
                }
            }
            else
            {
                // Return native .bannou format
                downloadPath = bundleMetadata.StorageKey;
                downloadSize = bundleMetadata.SizeBytes;
            }

            // Generate download token
            var downloadToken = Guid.NewGuid().ToString("N");
            var tokenTtl = TimeSpan.FromSeconds(_configuration.TokenTtlSeconds);

            // Store download token
            var tokenStore = _stateStoreFactory.GetStore<BundleDownloadToken>(STATE_STORE);
            await tokenStore.SaveAsync(
                $"bundle-download:{downloadToken}",
                new BundleDownloadToken
                {
                    BundleId = body.BundleId,
                    Format = body.Format,
                    Path = downloadPath,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.Add(tokenTtl)
                },
                new StateOptions { Ttl = (int)tokenTtl.TotalSeconds },
                cancellationToken);

            // Generate pre-signed download URL
            var downloadResult = await _storageProvider.GenerateDownloadUrlAsync(
                bucket,
                downloadPath,
                expiration: tokenTtl);

            _logger.LogInformation(
                "GetBundle: Generated download URL for bundle {BundleId}, format={Format}, fromCache={FromCache}",
                body.BundleId, body.Format, fromCache);

            return (StatusCodes.OK, new BundleWithDownloadUrl
            {
                BundleId = body.BundleId,
                Version = bundleMetadata.Version,
                DownloadUrl = new Uri(downloadResult.DownloadUrl),
                Format = body.Format,
                ExpiresAt = downloadResult.ExpiresAt,
                Size = downloadSize,
                AssetCount = bundleMetadata.AssetIds.Count,
                FromCache = fromCache
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetBundle operation");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "GetBundle",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/bundles/get",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of RequestBundleUpload operation.
    /// Generates pre-signed URL for uploading a pre-made bundle.
    /// </summary>
    public async Task<(StatusCodes, UploadResponse?)> RequestBundleUploadAsync(BundleUploadRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RequestBundleUpload: filename={Filename}, size={Size}", body.Filename, body.Size);

        try
        {
            // Validate filename
            if (string.IsNullOrWhiteSpace(body.Filename))
            {
                return (StatusCodes.BadRequest, null);
            }

            var extension = Path.GetExtension(body.Filename).ToLowerInvariant();
            if (extension != ".bannou" && extension != ".zip")
            {
                _logger.LogWarning("RequestBundleUpload: Invalid extension: {Extension}", extension);
                return (StatusCodes.BadRequest, null);
            }

            // Validate size
            var maxSizeBytes = (long)_configuration.MaxUploadSizeMb * 1024 * 1024;
            if (body.Size <= 0 || body.Size > maxSizeBytes)
            {
                _logger.LogWarning("RequestBundleUpload: Invalid size: {Size}", body.Size);
                return (StatusCodes.BadRequest, null);
            }

            // Validate manifest preview
            if (body.ManifestPreview == null ||
                string.IsNullOrWhiteSpace(body.ManifestPreview.BundleId))
            {
                _logger.LogWarning("RequestBundleUpload: Missing manifest preview");
                return (StatusCodes.BadRequest, null);
            }

            // Check if bundle already exists
            var bundleStore = _stateStoreFactory.GetStore<BundleMetadata>(STATE_STORE);
            var existingBundle = await bundleStore.GetAsync($"{BUNDLE_PREFIX}{body.ManifestPreview.BundleId}", cancellationToken);

            if (existingBundle != null)
            {
                _logger.LogWarning("RequestBundleUpload: Bundle already exists: {BundleId}",
                    body.ManifestPreview.BundleId);
                return (StatusCodes.Conflict, null);
            }

            // Generate upload ID and storage key
            var uploadIdGuid = Guid.NewGuid();
            var uploadId = uploadIdGuid.ToString("N");
            var sanitizedFilename = SanitizeFilename(body.Filename);
            var storageKey = $"bundles/uploads/{uploadId}/{sanitizedFilename}";
            var bucket = _configuration.StorageBucket;

            // Determine content type
            var contentType = extension == ".zip"
                ? "application/zip"
                : "application/x-bannou-bundle";

            // Generate pre-signed upload URL
            var tokenTtl = TimeSpan.FromSeconds(_configuration.TokenTtlSeconds);
            var uploadResult = await _storageProvider.GenerateUploadUrlAsync(
                bucket,
                storageKey,
                contentType,
                body.Size,
                tokenTtl,
                new Dictionary<string, string>
                {
                    { "bundle-id", body.ManifestPreview.BundleId },
                    { "upload-id", uploadId },
                    { "validation-required", "true" }
                });

            // Store upload session for validation on completion
            var uploadSession = new BundleUploadSession
            {
                UploadId = uploadId,
                BundleId = body.ManifestPreview.BundleId,
                Filename = sanitizedFilename,
                ContentType = contentType,
                SizeBytes = body.Size,
                StorageKey = storageKey,
                ManifestPreview = body.ManifestPreview,
                SessionId = "system", // TODO: Get from context when auth is integrated
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.Add(tokenTtl)
            };

            var bundleUploadStore = _stateStoreFactory.GetStore<BundleUploadSession>(STATE_STORE);
            await bundleUploadStore.SaveAsync(
                $"bundle-upload:{uploadId}",
                uploadSession,
                new StateOptions { Ttl = (int)tokenTtl.TotalSeconds },
                cancellationToken);

            _logger.LogInformation(
                "RequestBundleUpload: Generated upload URL for bundle {BundleId}, uploadId={UploadId}",
                body.ManifestPreview.BundleId, uploadId);

            var response = new UploadResponse
            {
                UploadId = uploadIdGuid,
                UploadUrl = new Uri(uploadResult.UploadUrl),
                ExpiresAt = uploadResult.ExpiresAt
            };

            // Add required headers if any
            if (uploadResult.RequiredHeaders != null)
            {
                response.RequiredHeaders = new Dictionary<string, string>(uploadResult.RequiredHeaders);
            }

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing RequestBundleUpload operation");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "RequestBundleUpload",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/bundles/upload/request",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Private Helper Methods

    private static string SanitizeFilename(string filename)
    {
        // Remove path separators and invalid characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder();
        foreach (var c in filename)
        {
            if (!invalidChars.Contains(c) && c != '/' && c != '\\')
            {
                sanitized.Append(c);
            }
        }
        return sanitized.ToString();
    }

    private static string GenerateAssetId(string contentType, string contentHash)
    {
        // Generate a deterministic asset ID from content type and hash
        // Format: {type-prefix}-{hash-prefix}
        var typePrefix = contentType.Split('/').FirstOrDefault()?.ToLowerInvariant() ?? "other";
        var hashPrefix = contentHash.Length >= 12 ? contentHash[..12] : contentHash;
        return $"{typePrefix}-{hashPrefix}";
    }

    private async Task IndexAssetAsync(AssetMetadata asset, CancellationToken cancellationToken)
    {
        // Index by asset type
        var typeIndexKey = $"{ASSET_INDEX_PREFIX}type:{asset.AssetType.ToString().ToLowerInvariant()}";
        await AddToIndexWithOptimisticConcurrencyAsync(typeIndexKey, asset.AssetId, cancellationToken).ConfigureAwait(false);

        // Index by realm
        var realmIndexKey = $"{ASSET_INDEX_PREFIX}realm:{asset.Realm.ToString().ToLowerInvariant()}";
        await AddToIndexWithOptimisticConcurrencyAsync(realmIndexKey, asset.AssetId, cancellationToken).ConfigureAwait(false);

        // Index by tags
        if (asset.Tags != null)
        {
            foreach (var tag in asset.Tags)
            {
                var tagIndexKey = $"{ASSET_INDEX_PREFIX}tag:{tag.ToLowerInvariant()}";
                await AddToIndexWithOptimisticConcurrencyAsync(tagIndexKey, asset.AssetId, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Adds an asset ID to an index using ETag-based optimistic concurrency.
    /// Retries on concurrent modification conflicts.
    /// </summary>
    private async Task AddToIndexWithOptimisticConcurrencyAsync(
        string indexKey,
        string assetId,
        CancellationToken cancellationToken,
        int maxRetries = 5)
    {
        var indexStore = _stateStoreFactory.GetStore<List<string>>(STATE_STORE);

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            // Get current state with ETag for optimistic concurrency
            var (index, etag) = await indexStore.GetWithETagAsync(indexKey, cancellationToken).ConfigureAwait(false);

            index ??= new List<string>();

            // Already indexed, no update needed
            if (index.Contains(assetId))
            {
                return;
            }

            // Add asset ID
            index.Add(assetId);

            // Try to save with ETag (fails if state changed since read)
            var saved = etag == null || await indexStore.TrySaveAsync(indexKey, index, etag, cancellationToken).ConfigureAwait(false); // No ETag means new entry, just save it

            if (saved || etag == null)
            {
                if (etag == null)
                {
                    // First time creating this index
                    await indexStore.SaveAsync(indexKey, index, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                return; // Success
            }

            // ETag mismatch - retry after brief delay
            _logger.LogDebug(
                "Index update conflict for {IndexKey}, retrying (attempt {Attempt}/{MaxRetries})",
                indexKey, attempt + 1, maxRetries);

            await Task.Delay(TimeSpan.FromMilliseconds(10 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
        }

        _logger.LogWarning(
            "Failed to update index {IndexKey} after {MaxRetries} attempts due to concurrent modifications",
            indexKey, maxRetries);
    }

    #endregion

    #region Processing Pool Delegation

    /// <summary>
    /// Content types that require processing before they're ready for use.
    /// </summary>
    private static readonly HashSet<string> ProcessableContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Textures
        "image/png",
        "image/jpeg",
        "image/tga",
        "image/x-tga",
        "image/tiff",
        "image/bmp",
        "image/x-dds",
        // 3D Models
        "model/gltf+json",
        "model/gltf-binary",
        "application/x-fbx",
        "model/obj",
        "model/x-blender",
        // Audio
        "audio/wav",
        "audio/x-wav",
        "audio/flac",
        "audio/ogg",
        "audio/mpeg"
    };

    /// <summary>
    /// Determines if a content type requires processing.
    /// </summary>
    private static bool RequiresProcessing(string contentType)
    {
        return ProcessableContentTypes.Contains(contentType);
    }

    /// <summary>
    /// Determines the processor pool type for a given content type.
    /// </summary>
    private static string GetProcessorPoolType(string contentType)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return "texture-processor";
        }
        if (contentType.StartsWith("model/", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("fbx", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("blender", StringComparison.OrdinalIgnoreCase))
        {
            return "model-processor";
        }
        if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return "audio-processor";
        }

        return "asset-processor";
    }

    /// <summary>
    /// Delegates processing to the processing pool.
    /// If no processor is available (429), queues for retry via pub/sub.
    /// </summary>
    private async Task DelegateToProcessingPoolAsync(
        string assetId,
        AssetMetadata metadata,
        string storageKey,
        CancellationToken cancellationToken)
    {
        var poolType = GetProcessorPoolType(metadata.ContentType);
        var assetStore = _stateStoreFactory.GetStore<AssetMetadata>(STATE_STORE);

        try
        {
            _logger.LogInformation(
                "DelegateToProcessingPool: Acquiring processor from pool {PoolType} for asset {AssetId}",
                poolType, assetId);

            // Try to acquire a processor from the pool
            var processorResponse = await _orchestratorClient.AcquireProcessorAsync(
                new AcquireProcessorRequest
                {
                    PoolType = poolType,
                    Priority = 0,
                    TimeoutSeconds = 600, // 10 minutes for processing
                    Metadata = new { AssetId = assetId, StorageKey = storageKey }
                },
                cancellationToken).ConfigureAwait(false);

            // Update asset metadata to Processing status
            metadata.ProcessingStatus = ProcessingStatus.Processing;
            await assetStore.SaveAsync($"{ASSET_PREFIX}{assetId}", metadata, cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "DelegateToProcessingPool: Acquired processor {ProcessorId} from pool {PoolType} for asset {AssetId}, lease expires at {ExpiresAt}",
                processorResponse.ProcessorId, poolType, assetId, processorResponse.ExpiresAt);

            // Publish processing job event for the processor to pick up
            var processingJob = new AssetProcessingJobEvent
            {
                AssetId = assetId,
                StorageKey = storageKey,
                ContentType = metadata.ContentType,
                ProcessorId = processorResponse.ProcessorId,
                AppId = processorResponse.AppId,
                LeaseId = processorResponse.LeaseId,
                ExpiresAt = processorResponse.ExpiresAt
            };

            await _messageBus.TryPublishAsync($"asset.processing.job.{poolType}", processingJob).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.StatusCode == 429)
        {
            // Pool is busy - queue for retry
            _logger.LogWarning(
                "DelegateToProcessingPool: No processors available in pool {PoolType}, queueing asset {AssetId} for retry",
                poolType, assetId);

            // Publish delayed retry event
            var retryEvent = new AssetProcessingRetryEvent
            {
                AssetId = assetId,
                StorageKey = storageKey,
                ContentType = metadata.ContentType,
                PoolType = poolType,
                RetryCount = 0,
                MaxRetries = 5,
                RetryDelaySeconds = 30
            };

            await _messageBus.TryPublishAsync("asset.processing.retry", retryEvent).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DelegateToProcessingPool: Failed to delegate asset {AssetId} to pool {PoolType}",
                assetId, poolType);

            // Mark as failed
            metadata.ProcessingStatus = ProcessingStatus.Failed;
            await assetStore.SaveAsync($"{ASSET_PREFIX}{assetId}", metadata, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    #endregion

    #region Metadata Conversion Helpers

    /// <summary>
    /// Converts metadata object (typically JsonElement) to Dictionary&lt;string, object&gt;.
    /// </summary>
    private static Dictionary<string, object>? ConvertMetadataToDictionary(object? metadata)
    {
        if (metadata == null)
            return null;

        if (metadata is Dictionary<string, object> dict)
            return dict;

        if (metadata is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            var result = new Dictionary<string, object>();
            foreach (var property in jsonElement.EnumerateObject())
            {
                result[property.Name] = GetJsonValue(property.Value);
            }
            return result;
        }

        return null;
    }

    /// <summary>
    /// Converts metadata object (typically JsonElement) to IReadOnlyDictionary&lt;string, string&gt;.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? ConvertMetadataToStringDictionary(object? metadata)
    {
        if (metadata == null)
            return null;

        if (metadata is Dictionary<string, string> strDict)
            return strDict;

        if (metadata is Dictionary<string, object> objDict)
        {
            var result = new Dictionary<string, string>();
            foreach (var kvp in objDict)
            {
                result[kvp.Key] = kvp.Value?.ToString() ?? "";
            }
            return result;
        }

        if (metadata is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            var result = new Dictionary<string, string>();
            foreach (var property in jsonElement.EnumerateObject())
            {
                result[property.Name] = property.Value.ToString();
            }
            return result;
        }

        return null;
    }

    /// <summary>
    /// Extracts a .NET value from a JsonElement.
    /// </summary>
    private static object GetJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Array => element.EnumerateArray().Select(GetJsonValue).ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => GetJsonValue(p.Value)),
            _ => element.ToString()
        };
    }

    #endregion
}

/// <summary>
/// Event published when an asset processing job is assigned to a processor.
/// </summary>
public sealed class AssetProcessingJobEvent
{
    public string JobId { get; set; } = Guid.NewGuid().ToString();
    public string AssetId { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? RealmId { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
    public Dictionary<string, object>? ProcessingOptions { get; set; }
    public string PoolType { get; set; } = string.Empty;
    public string ProcessorId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public Guid LeaseId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Event published when processing needs to be retried later.
/// </summary>
public sealed class AssetProcessingRetryEvent
{
    public string AssetId { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string PoolType { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 5;
    public int RetryDelaySeconds { get; set; } = 30;
}

/// <summary>
/// Internal model for bundle download tokens stored in state.
/// </summary>
internal sealed class BundleDownloadToken
{
    public string BundleId { get; set; } = string.Empty;
    public BundleFormat Format { get; set; }
    public string Path { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
