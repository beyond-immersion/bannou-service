using BeyondImmersion.Bannou.Asset.ClientEvents;
using BeyondImmersion.Bannou.Bundle.Format;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Asset.Bundles;
using BeyondImmersion.BannouService.Asset.Events;
using BeyondImmersion.BannouService.Asset.Models;
using BeyondImmersion.BannouService.Asset.Pool;
using BeyondImmersion.BannouService.Asset.Storage;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
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
    private readonly IAssetProcessorPoolManager _processorPoolManager;
    private readonly IBundleConverter _bundleConverter;

    // State store name and key prefixes now come from configuration
    // See AssetServiceConfiguration for defaults: StateStoreName, UploadSessionKeyPrefix, etc.

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
    /// <param name="processorPoolManager">Pool manager for tracking processor node state.</param>
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
        IAssetProcessorPoolManager processorPoolManager,
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
        _processorPoolManager = processorPoolManager;
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
            var storageKey = $"{_configuration.TempUploadPathPrefix}/{uploadId:N}/{SanitizeFilename(body.Filename)}";

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
                    Owner = body.Owner,
                    StorageKey = storageKey,
                    IsMultipart = true,
                    PartCount = partCount,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = multipartResult.ExpiresAt
                };

                var sessionStore = _stateStoreFactory.GetStore<UploadSession>(_configuration.StatestoreName);
                await sessionStore.SaveAsync($"{_configuration.UploadSessionKeyPrefix}{uploadId}", session, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Generate single upload URL
                // Note: Metadata is NOT signed in the presigned URL - it's stored in the upload session
                // and applied during CompleteUploadAsync. This simplifies client implementation
                // (they only need to send Content-Type) and avoids signature mismatches.
                var uploadResult = await _storageProvider.GenerateUploadUrlAsync(
                    _configuration.StorageBucket,
                    storageKey,
                    body.ContentType,
                    body.Size,
                    expiration,
                    metadata: null)  // Metadata applied server-side during CompleteUploadAsync
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
                    Owner = body.Owner,
                    StorageKey = storageKey,
                    IsMultipart = false,
                    PartCount = 0,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = uploadResult.ExpiresAt
                };

                var sessionStore = _stateStoreFactory.GetStore<UploadSession>(_configuration.StatestoreName);
                await sessionStore.SaveAsync($"{_configuration.UploadSessionKeyPrefix}{uploadId}", session, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("RequestUpload: Created upload session {UploadId}, multipart={IsMultipart}",
                uploadId, isMultipart);

            // Publish AssetUploadRequestedEvent for audit trail
            await _messageBus.TryPublishAsync(
                "asset.upload.requested",
                new BeyondImmersion.BannouService.Events.AssetUploadRequestedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    UploadId = uploadId,
                    Owner = body.Owner,
                    Filename = body.Filename,
                    Size = body.Size,
                    ContentType = body.ContentType,
                    IsMultipart = isMultipart
                });

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
            // Retrieve upload session - check both regular and bundle upload prefixes
            var sessionStore = _stateStoreFactory.GetStore<UploadSession>(_configuration.StatestoreName);
            var session = await sessionStore.GetAsync($"{_configuration.UploadSessionKeyPrefix}{body.UploadId}", cancellationToken).ConfigureAwait(false);

            // If not found, check for bundle upload session and convert
            if (session == null)
            {
                var bundleStore = _stateStoreFactory.GetStore<BundleUploadSession>(_configuration.StatestoreName);
                var bundleSession = await bundleStore.GetAsync($"bundle-upload:{body.UploadId}", cancellationToken).ConfigureAwait(false);

                if (bundleSession != null)
                {
                    // Convert BundleUploadSession to UploadSession for processing
                    session = new UploadSession
                    {
                        UploadId = body.UploadId,
                        Filename = bundleSession.Filename,
                        Size = bundleSession.SizeBytes,
                        ContentType = bundleSession.ContentType,
                        Owner = bundleSession.Owner,
                        StorageKey = bundleSession.StorageKey,
                        IsMultipart = false,
                        PartCount = 0,
                        CreatedAt = bundleSession.CreatedAt,
                        ExpiresAt = bundleSession.ExpiresAt,
                        IsComplete = false
                    };
                    _logger.LogDebug("CompleteUpload: Found bundle upload session {UploadId}", body.UploadId);
                }
            }

            if (session == null)
            {
                _logger.LogWarning("CompleteUpload: Upload session not found {UploadId}", body.UploadId);
                return (StatusCodes.NotFound, null);
            }

            // Check expiration
            if (DateTimeOffset.UtcNow > session.ExpiresAt)
            {
                _logger.LogWarning("CompleteUpload: Upload session expired {UploadId}", body.UploadId);
                // Delete both possible session keys (only one will exist)
                await sessionStore.DeleteAsync($"{_configuration.UploadSessionKeyPrefix}{body.UploadId}", cancellationToken).ConfigureAwait(false);
                var expiredBundleStore = _stateStoreFactory.GetStore<BundleUploadSession>(_configuration.StatestoreName);
                await expiredBundleStore.DeleteAsync($"bundle-upload:{body.UploadId}", cancellationToken).ConfigureAwait(false);
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
                    body.UploadId.ToString(),
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

            // Compute SHA256 content hash by downloading the file
            string contentHash;
            using (var objectStream = await _storageProvider.GetObjectAsync(
                _configuration.StorageBucket,
                session.StorageKey).ConfigureAwait(false))
            {
                var hashBytes = await SHA256.HashDataAsync(objectStream).ConfigureAwait(false);
                contentHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            var assetId = GenerateAssetId(session.ContentType, contentHash);

            // Determine final storage key
            var assetType = session.Metadata?.AssetType ?? AssetType.Other;
            var extension = Path.GetExtension(session.Filename);
            var finalKey = $"{_configuration.FinalAssetPathPrefix}/{assetType.ToString().ToLowerInvariant()}/{assetId}{extension}";

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
            var assetStore = _stateStoreFactory.GetStore<InternalAssetRecord>(_configuration.StatestoreName);
            await assetStore.SaveAsync($"{_configuration.AssetKeyPrefix}{assetId}", internalRecord, cancellationToken: cancellationToken).ConfigureAwait(false);

            // Convert to public metadata for return value and events
            var assetMetadata = internalRecord.ToPublicMetadata();
            assetMetadata.IsArchived = false; // New assets are always in standard storage

            // Store index entries for search
            await IndexAssetAsync(assetMetadata, cancellationToken).ConfigureAwait(false);

            // Delete upload session (both regular and bundle prefixes - only one will exist)
            await sessionStore.DeleteAsync($"{_configuration.UploadSessionKeyPrefix}{body.UploadId}", cancellationToken).ConfigureAwait(false);
            var bundleSessionStore = _stateStoreFactory.GetStore<BundleUploadSession>(_configuration.StatestoreName);
            await bundleSessionStore.DeleteAsync($"bundle-upload:{body.UploadId}", cancellationToken).ConfigureAwait(false);

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
                    Owner = session.Owner,
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

            return (StatusCodes.OK, assetMetadata);
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
            var assetStore = _stateStoreFactory.GetStore<InternalAssetRecord>(_configuration.StatestoreName);
            var internalRecord = await assetStore.GetAsync($"{_configuration.AssetKeyPrefix}{body.AssetId}", cancellationToken).ConfigureAwait(false);

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
    /// Implementation of DeleteAsset operation.
    /// Deletes an asset from storage. If versionId is specified, only that version is deleted.
    /// </summary>
    public async Task<(StatusCodes, DeleteAssetResponse?)> DeleteAssetAsync(DeleteAssetRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteAsset: assetId={AssetId}, versionId={VersionId}", body.AssetId, body.VersionId);

        try
        {
            if (string.IsNullOrWhiteSpace(body.AssetId))
            {
                _logger.LogWarning("DeleteAsset: Missing AssetId");
                return (StatusCodes.BadRequest, null);
            }

            // Retrieve internal asset record to get storage details
            var assetStore = _stateStoreFactory.GetStore<InternalAssetRecord>(_configuration.StatestoreName);
            var assetKey = $"{_configuration.AssetKeyPrefix}{body.AssetId}";
            var internalRecord = await assetStore.GetAsync(assetKey, cancellationToken).ConfigureAwait(false);

            if (internalRecord == null)
            {
                _logger.LogWarning("DeleteAsset: Asset not found {AssetId}", body.AssetId);
                return (StatusCodes.NotFound, null);
            }

            int versionsDeleted;

            if (!string.IsNullOrEmpty(body.VersionId))
            {
                // Delete specific version
                await _storageProvider.DeleteObjectAsync(
                    internalRecord.Bucket,
                    internalRecord.StorageKey,
                    body.VersionId).ConfigureAwait(false);
                versionsDeleted = 1;

                _logger.LogInformation(
                    "DeleteAsset: Deleted version {VersionId} for asset {AssetId}",
                    body.VersionId,
                    body.AssetId);
            }
            else
            {
                // Delete all versions - list them first
                var versions = await _storageProvider.ListVersionsAsync(
                    internalRecord.Bucket,
                    internalRecord.StorageKey).ConfigureAwait(false);

                foreach (var version in versions)
                {
                    await _storageProvider.DeleteObjectAsync(
                        internalRecord.Bucket,
                        internalRecord.StorageKey,
                        version.VersionId).ConfigureAwait(false);
                }

                versionsDeleted = versions.Count;

                // Also delete the asset metadata from state store
                await assetStore.DeleteAsync(assetKey, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "DeleteAsset: Deleted all {Count} versions for asset {AssetId}",
                    versionsDeleted,
                    body.AssetId);
            }

            var response = new DeleteAssetResponse
            {
                AssetId = body.AssetId,
                VersionsDeleted = versionsDeleted
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing DeleteAsset operation");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "DeleteAsset",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/assets/delete",
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
            var assetStore = _stateStoreFactory.GetStore<InternalAssetRecord>(_configuration.StatestoreName);
            var internalRecord = await assetStore.GetAsync($"{_configuration.AssetKeyPrefix}{body.AssetId}", cancellationToken).ConfigureAwait(false);

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
            if (!_stateStoreFactory.SupportsSearch(_configuration.StatestoreName))
            {
                _logger.LogDebug("Search not supported for store {Store}, using index fallback", _configuration.StatestoreName);
                return await SearchAssetsIndexFallbackAsync(body, cancellationToken).ConfigureAwait(false);
            }

            var searchStore = _stateStoreFactory.GetSearchableStore<AssetMetadata>(_configuration.StatestoreName);

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
                Assets = paginatedAssets,
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
        var indexStore = _stateStoreFactory.GetStore<List<string>>(_configuration.StatestoreName);
        var assetStore = _stateStoreFactory.GetStore<InternalAssetRecord>(_configuration.StatestoreName);

        var indexKey = $"{_configuration.AssetIndexKeyPrefix}type:{body.AssetType.ToString().ToLowerInvariant()}";
        var assetIds = await indexStore.GetAsync(indexKey, cancellationToken).ConfigureAwait(false);

        if (assetIds != null)
        {
            foreach (var assetId in assetIds)
            {
                var internalRecord = await assetStore.GetAsync($"{_configuration.AssetKeyPrefix}{assetId}", cancellationToken).ConfigureAwait(false);

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
            var bundleStore = _stateStoreFactory.GetStore<BundleMetadata>(_configuration.StatestoreName);
            var assetStore = _stateStoreFactory.GetStore<InternalAssetRecord>(_configuration.StatestoreName);

            var bundleKey = $"{_configuration.BundleKeyPrefix}{body.BundleId}";
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
                var assetKey = $"{_configuration.AssetKeyPrefix}{assetId}";
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
                    Metadata = MetadataHelper.ConvertToDictionary(body.Metadata),
                    Status = BundleCreationStatus.Queued,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                // Store job state
                var jobStore = _stateStoreFactory.GetStore<BundleCreationJob>(_configuration.StatestoreName);
                var jobKey = $"bundle-job:{jobId}";
                await jobStore.SaveAsync(jobKey, job, cancellationToken: cancellationToken);

                // Publish job event for processing pool
                await _messageBus.TryPublishAsync("asset.bundle.create", job);

                _logger.LogInformation(
                    "CreateBundle: Queued bundle creation job {JobId} for bundle {BundleId}",
                    jobId,
                    body.BundleId);

                // Return OK - the Status field in the response tells client it's queued
                return (StatusCodes.OK, new CreateBundleResponse
                {
                    BundleId = body.BundleId,
                    Status = CreateBundleResponseStatus.Queued,
                    EstimatedSize = totalSize
                });
            }

            // For smaller bundles, create inline
            var bundlePath = $"{_configuration.BundleCurrentPathPrefix}/{body.BundleId}.bundle";
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
                MetadataHelper.ConvertToStringDictionary(body.Metadata));

            // Upload bundle
            bundleStream.Position = 0;
            await _storageProvider.PutObjectAsync(
                bucket,
                bundlePath,
                bundleStream,
                bundleStream.Length,
                "application/x-bannou-bundle");

            // Build asset entries for the bundle
            var bundleAssetEntries = assetRecords.Select(r => new StoredBundleAssetEntry
            {
                AssetId = r.AssetId,
                ContentHash = r.ContentHash,
                Filename = r.Filename,
                ContentType = r.ContentType,
                Size = r.Size
            }).ToList();

            // Store bundle metadata
            var bundleMetadata = new BundleMetadata
            {
                BundleId = body.BundleId,
                Version = body.Version ?? "1.0.0",
                BundleType = BundleType.Source,
                Realm = body.Realm ?? Asset.Realm.Shared,
                AssetIds = body.AssetIds.ToList(),
                Assets = bundleAssetEntries,
                StorageKey = bundlePath,
                Bucket = bucket,
                SizeBytes = bundleStream.Length,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = Models.BundleStatus.Ready,
                Owner = body.Owner
            };

            await bundleStore.SaveAsync(bundleKey, bundleMetadata, cancellationToken: cancellationToken);

            // Populate reverse indexes for asset → bundle lookups
            await IndexBundleAssetsAsync(bundleMetadata, cancellationToken).ConfigureAwait(false);

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
                    Compression = BeyondImmersion.BannouService.Events.CompressionTypeEnum.Lz4,
                    Owner = body.Owner
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
    /// Returns bundle metadata and a pre-signed download URL.
    /// Supports both native .bannou format and ZIP conversion (cached).
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
            var bundleStore = _stateStoreFactory.GetStore<BundleMetadata>(_configuration.StatestoreName);
            var bundleKey = $"{_configuration.BundleKeyPrefix}{body.BundleId}";
            var bundleMetadata = await bundleStore.GetAsync(bundleKey, cancellationToken);

            if (bundleMetadata == null)
            {
                _logger.LogWarning("GetBundle: Bundle not found: {BundleId}", body.BundleId);
                return (StatusCodes.NotFound, null);
            }

            // Check if bundle is ready
            if (bundleMetadata.Status != Models.BundleStatus.Ready)
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
                var zipCachePath = $"{_configuration.BundleZipCachePathPrefix}/{body.BundleId}.zip";
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
            var tokenStore = _stateStoreFactory.GetStore<BundleDownloadToken>(_configuration.StatestoreName);
            await tokenStore.SaveAsync(
                $"bundle-download:{downloadToken}",
                new BundleDownloadToken
                {
                    BundleId = Guid.Parse(body.BundleId),
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
            var bundleStore = _stateStoreFactory.GetStore<BundleMetadata>(_configuration.StatestoreName);
            var existingBundle = await bundleStore.GetAsync($"{_configuration.BundleKeyPrefix}{body.ManifestPreview.BundleId}", cancellationToken);

            if (existingBundle != null)
            {
                _logger.LogWarning("RequestBundleUpload: Bundle already exists: {BundleId}",
                    body.ManifestPreview.BundleId);
                return (StatusCodes.Conflict, null);
            }

            // Generate upload ID and storage key
            var uploadIdGuid = Guid.NewGuid();
            var uploadId = uploadIdGuid.ToString(); // Standard format with dashes for Redis key consistency
            var uploadIdForPath = uploadIdGuid.ToString("N"); // No dashes for S3 paths (cleaner)
            var sanitizedFilename = SanitizeFilename(body.Filename);
            var storageKey = $"{_configuration.BundleUploadPathPrefix}/{uploadIdForPath}/{sanitizedFilename}";
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
                    { "upload-id", uploadIdForPath }, // Use path format for S3 metadata
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
                Owner = body.Owner,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.Add(tokenTtl)
            };

            var bundleUploadStore = _stateStoreFactory.GetStore<BundleUploadSession>(_configuration.StatestoreName);
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

    /// <summary>
    /// Implementation of CreateMetabundle operation.
    /// Composes a metabundle from multiple source bundles by extracting and repackaging assets.
    /// </summary>
    public async Task<(StatusCodes, CreateMetabundleResponse?)> CreateMetabundleAsync(CreateMetabundleRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("CreateMetabundle: metabundleId={MetabundleId}, sourceBundles={SourceBundleCount}, standaloneAssets={StandaloneCount}",
            body.MetabundleId, body.SourceBundleIds?.Count ?? 0, body.StandaloneAssetIds?.Count ?? 0);

        try
        {
            // Validate request
            if (string.IsNullOrWhiteSpace(body.MetabundleId))
            {
                _logger.LogWarning("CreateMetabundle: Empty metabundle_id");
                return (StatusCodes.BadRequest, null);
            }

            var hasSourceBundles = body.SourceBundleIds != null && body.SourceBundleIds.Count > 0;
            var hasStandaloneAssets = body.StandaloneAssetIds != null && body.StandaloneAssetIds.Count > 0;

            if (!hasSourceBundles && !hasStandaloneAssets)
            {
                _logger.LogWarning("CreateMetabundle: No source_bundle_ids or standalone_asset_ids provided");
                return (StatusCodes.BadRequest, null);
            }

            var bundleStore = _stateStoreFactory.GetStore<BundleMetadata>(_configuration.StatestoreName);
            var bucket = _configuration.StorageBucket;

            // Check if metabundle already exists
            var metabundleKey = $"{_configuration.BundleKeyPrefix}{body.MetabundleId}";
            var existing = await bundleStore.GetAsync(metabundleKey, cancellationToken).ConfigureAwait(false);
            if (existing != null)
            {
                _logger.LogWarning("CreateMetabundle: Metabundle {MetabundleId} already exists", body.MetabundleId);
                return (StatusCodes.Conflict, null);
            }

            // Collect all source bundles and validate
            var sourceBundles = new List<BundleMetadata>();
            if (hasSourceBundles)
            {
                foreach (var sourceBundleId in body.SourceBundleIds!)
                {
                    var sourceKey = $"{_configuration.BundleKeyPrefix}{sourceBundleId}";
                    var sourceBundle = await bundleStore.GetAsync(sourceKey, cancellationToken).ConfigureAwait(false);

                    if (sourceBundle == null)
                    {
                        _logger.LogWarning("CreateMetabundle: Source bundle {BundleId} not found", sourceBundleId);
                        return (StatusCodes.NotFound, null);
                    }

                    if (sourceBundle.Status != Models.BundleStatus.Ready)
                    {
                        _logger.LogWarning("CreateMetabundle: Source bundle {BundleId} not ready (status={Status})",
                            sourceBundleId, sourceBundle.Status);
                        return (StatusCodes.BadRequest, null);
                    }

                    // Validate realm consistency (all must be same realm or 'shared')
                    if (sourceBundle.Realm != body.Realm && sourceBundle.Realm != Asset.Realm.Shared)
                    {
                        _logger.LogWarning("CreateMetabundle: Realm mismatch - bundle {BundleId} is {BundleRealm}, expected {ExpectedRealm}",
                            sourceBundleId, sourceBundle.Realm, body.Realm);
                        return (StatusCodes.BadRequest, null);
                    }

                    sourceBundles.Add(sourceBundle);
                }
            }

            // Collect and validate standalone assets
            var assetStore = _stateStoreFactory.GetStore<InternalAssetRecord>(_configuration.StatestoreName);
            var standaloneAssets = new List<InternalAssetRecord>();
            if (hasStandaloneAssets)
            {
                foreach (var assetId in body.StandaloneAssetIds!)
                {
                    var assetKey = $"{_configuration.AssetKeyPrefix}{assetId}";
                    var asset = await assetStore.GetAsync(assetKey, cancellationToken).ConfigureAwait(false);

                    if (asset == null)
                    {
                        _logger.LogWarning("CreateMetabundle: Standalone asset {AssetId} not found", assetId);
                        return (StatusCodes.NotFound, null);
                    }

                    if (asset.ProcessingStatus != ProcessingStatus.Complete)
                    {
                        _logger.LogWarning("CreateMetabundle: Standalone asset {AssetId} not ready (status={Status})",
                            assetId, asset.ProcessingStatus);
                        return (StatusCodes.BadRequest, null);
                    }

                    // Validate realm consistency
                    var assetRealm = asset.Realm ?? Asset.Realm.Omega;
                    if (assetRealm != body.Realm && assetRealm != Asset.Realm.Shared)
                    {
                        _logger.LogWarning("CreateMetabundle: Realm mismatch - asset {AssetId} is {AssetRealm}, expected {ExpectedRealm}",
                            assetId, assetRealm, body.Realm);
                        return (StatusCodes.BadRequest, null);
                    }

                    standaloneAssets.Add(asset);
                }
            }

            // Collect all assets from source bundles and standalone assets, checking for conflicts
            var assetsByHash = new Dictionary<string, (StoredBundleAssetEntry Entry, string SourceBundleId)>();
            var assetsByPlatformId = new Dictionary<string, List<(string BundleId, string ContentHash)>>();
            var standaloneByHash = new Dictionary<string, InternalAssetRecord>(); // Standalone assets tracked separately
            var conflicts = new List<AssetConflict>();

            // Process source bundle assets
            foreach (var sourceBundle in sourceBundles)
            {
                if (sourceBundle.Assets == null)
                {
                    continue;
                }

                foreach (var asset in sourceBundle.Assets)
                {
                    // Track by platform ID to detect conflicts
                    if (!assetsByPlatformId.TryGetValue(asset.AssetId, out var versions))
                    {
                        versions = new List<(string BundleId, string ContentHash)>();
                        assetsByPlatformId[asset.AssetId] = versions;
                    }
                    versions.Add((sourceBundle.BundleId, asset.ContentHash));

                    // Deduplicate by content hash
                    if (!assetsByHash.ContainsKey(asset.ContentHash))
                    {
                        assetsByHash[asset.ContentHash] = (asset, sourceBundle.BundleId);
                    }
                }
            }

            // Process standalone assets (track conflicts with bundle assets)
            const string standaloneMarker = "__standalone__";
            foreach (var standalone in standaloneAssets)
            {
                var contentHash = standalone.ContentHash ?? standalone.AssetId; // Use asset ID if no hash

                // Track by platform ID to detect conflicts
                if (!assetsByPlatformId.TryGetValue(standalone.AssetId, out var versions))
                {
                    versions = new List<(string BundleId, string ContentHash)>();
                    assetsByPlatformId[standalone.AssetId] = versions;
                }
                versions.Add((standaloneMarker, contentHash));

                // Track standalone assets by hash for deduplication
                if (!standaloneByHash.ContainsKey(contentHash) && !assetsByHash.ContainsKey(contentHash))
                {
                    standaloneByHash[contentHash] = standalone;
                }
            }

            // Check for platform ID conflicts (same ID, different hash)
            foreach (var (platformId, versions) in assetsByPlatformId)
            {
                var uniqueHashes = versions.Select(v => v.ContentHash).Distinct().ToList();
                if (uniqueHashes.Count > 1)
                {
                    conflicts.Add(new AssetConflict
                    {
                        AssetId = platformId,
                        ConflictingBundles = versions.Select(v => new ConflictingBundleEntry
                        {
                            BundleId = v.BundleId,
                            ContentHash = v.ContentHash
                        }).ToList()
                    });
                }
            }

            if (conflicts.Count > 0)
            {
                // Log conflict details server-side for debugging
                foreach (var conflict in conflicts)
                {
                    var bundleHashes = conflict.ConflictingBundles?
                        .Select(b => $"{b.BundleId}={b.ContentHash}")
                        .ToList() ?? new List<string>();
                    _logger.LogWarning(
                        "CreateMetabundle: Asset {AssetId} has conflicting hashes across bundles: {BundleHashes}",
                        conflict.AssetId, string.Join(", ", bundleHashes));
                }
                return (StatusCodes.Conflict, null);
            }

            // Apply asset filter if provided (to both bundle assets and standalone assets)
            var assetsToInclude = assetsByHash.Values.ToList();
            var standalonesToInclude = standaloneByHash.Values.ToList();
            if (body.AssetFilter != null && body.AssetFilter.Count > 0)
            {
                var filterSet = new HashSet<string>(body.AssetFilter);
                assetsToInclude = assetsToInclude
                    .Where(a => filterSet.Contains(a.Entry.AssetId))
                    .ToList();
                standalonesToInclude = standalonesToInclude
                    .Where(a => filterSet.Contains(a.AssetId))
                    .ToList();
            }

            // Calculate total size (bundle assets + standalone assets)
            var bundleAssetSize = assetsToInclude.Sum(a => a.Entry.Size);
            var standaloneAssetSize = standalonesToInclude.Sum(a => a.Size);
            var totalSize = bundleAssetSize + standaloneAssetSize;
            var totalAssetCount = assetsToInclude.Count + standalonesToInclude.Count;

            _logger.LogInformation(
                "CreateMetabundle: Creating metabundle {MetabundleId} with {AssetCount} assets ({Size} bytes)",
                body.MetabundleId, totalAssetCount, totalSize);

            // Check if job should be processed asynchronously
            var shouldProcessAsync = ShouldProcessMetabundleAsync(
                sourceBundles.Count,
                totalAssetCount,
                totalSize);

            if (shouldProcessAsync)
            {
                // Create async job and return queued response
                return await CreateMetabundleJobAsync(
                    body,
                    sourceBundles,
                    standaloneAssets,
                    assetsToInclude,
                    standalonesToInclude,
                    totalAssetCount,
                    totalSize,
                    cancellationToken).ConfigureAwait(false);
            }

            // Synchronous processing for small jobs
            // Create metabundle
            var metabundlePath = $"{_configuration.BundleCurrentPathPrefix}/{body.MetabundleId}.bundle";

            using var bundleStream = new MemoryStream();
            using var writer = new BannouBundleWriter(bundleStream);

            // Track which assets came from which source bundle for provenance
            var provenanceByBundle = new Dictionary<string, List<string>>();
            var standaloneAssetIds = new List<string>();

            // Process assets from source bundles
            foreach (var (entry, sourceBundleId) in assetsToInclude)
            {
                // Get source bundle info
                var sourceBundle = sourceBundles.First(b => b.BundleId == sourceBundleId);

                // Download source bundle and extract asset data
                using var sourceBundleStream = await _storageProvider.GetObjectAsync(
                    sourceBundle.Bucket ?? bucket,
                    sourceBundle.StorageKey).ConfigureAwait(false);

                // Read the bundle to extract the specific asset
                using var reader = new BannouBundleReader(sourceBundleStream);
                var assetData = reader.ReadAsset(entry.AssetId);

                if (assetData == null)
                {
                    _logger.LogWarning("CreateMetabundle: Asset {AssetId} not found in source bundle {BundleId}",
                        entry.AssetId, sourceBundleId);
                    continue;
                }

                writer.AddAsset(
                    entry.AssetId,
                    entry.Filename ?? entry.AssetId,
                    entry.ContentType ?? "application/octet-stream",
                    assetData);

                // Track provenance
                if (!provenanceByBundle.TryGetValue(sourceBundleId, out var assetList))
                {
                    assetList = new List<string>();
                    provenanceByBundle[sourceBundleId] = assetList;
                }
                assetList.Add(entry.AssetId);
            }

            // Process standalone assets (download directly from storage)
            foreach (var standalone in standalonesToInclude)
            {
                using var assetStream = await _storageProvider.GetObjectAsync(
                    standalone.Bucket ?? bucket,
                    standalone.StorageKey).ConfigureAwait(false);

                using var memoryStream = new MemoryStream();
                await assetStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
                var assetData = memoryStream.ToArray();

                writer.AddAsset(
                    standalone.AssetId,
                    standalone.Filename ?? standalone.AssetId,
                    standalone.ContentType ?? "application/octet-stream",
                    assetData);

                standaloneAssetIds.Add(standalone.AssetId);
            }

            // Finalize the bundle
            writer.Finalize(
                body.MetabundleId,
                body.MetabundleId,
                body.Version ?? "1.0.0",
                body.Owner,
                body.Description,
                body.Metadata != null ? MetadataHelper.ConvertToStringDictionary(body.Metadata) : null);

            // Upload metabundle
            bundleStream.Position = 0;
            await _storageProvider.PutObjectAsync(
                bucket,
                metabundlePath,
                bundleStream,
                bundleStream.Length,
                "application/x-bannou-bundle").ConfigureAwait(false);

            // Build provenance references
            var sourceBundleRefs = sourceBundles
                .Where(sb => provenanceByBundle.ContainsKey(sb.BundleId))
                .Select(sb => new StoredSourceBundleReference
                {
                    BundleId = sb.BundleId,
                    Version = sb.Version,
                    AssetIds = provenanceByBundle[sb.BundleId],
                    ContentHash = sb.StorageKey // Use storage key as proxy for content hash
                })
                .ToList();

            // Build asset entries for storage (bundle assets + standalone assets)
            var metabundleAssets = assetsToInclude.Select(a => a.Entry).ToList();

            // Convert standalone assets to StoredBundleAssetEntry format
            var standaloneEntries = standalonesToInclude.Select(s => new StoredBundleAssetEntry
            {
                AssetId = s.AssetId,
                Filename = s.Filename,
                ContentType = s.ContentType,
                Size = s.Size,
                ContentHash = s.ContentHash
            }).ToList();
            metabundleAssets.AddRange(standaloneEntries);

            // All asset IDs (bundle + standalone)
            var allAssetIds = metabundleAssets.Select(a => a.AssetId).ToList();

            // Store metabundle metadata
            var metabundleMetadata = new BundleMetadata
            {
                BundleId = body.MetabundleId,
                Version = body.Version ?? "1.0.0",
                BundleType = BundleType.Metabundle,
                Realm = body.Realm,
                AssetIds = allAssetIds,
                Assets = metabundleAssets,
                StorageKey = metabundlePath,
                Bucket = bucket,
                SizeBytes = bundleStream.Length,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = Models.BundleStatus.Ready,
                Owner = body.Owner,
                SourceBundles = sourceBundleRefs,
                StandaloneAssetIds = standaloneAssetIds.Count > 0 ? standaloneAssetIds : null,
                Metadata = body.Metadata != null ? MetadataHelper.ConvertToDictionary(body.Metadata) : null
            };

            await bundleStore.SaveAsync(metabundleKey, metabundleMetadata, cancellationToken: cancellationToken).ConfigureAwait(false);

            // Populate reverse indexes
            await IndexBundleAssetsAsync(metabundleMetadata, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "CreateMetabundle: Created metabundle {MetabundleId} with {AssetCount} assets ({BundleAssets} from bundles, {StandaloneAssets} standalone) from {SourceCount} sources ({Size} bytes)",
                body.MetabundleId, metabundleAssets.Count, assetsToInclude.Count, standalonesToInclude.Count, sourceBundles.Count, bundleStream.Length);

            // Publish metabundle.created event
            await _messageBus.TryPublishAsync(
                "asset.metabundle.created",
                new BeyondImmersion.BannouService.Events.MetabundleCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    MetabundleId = body.MetabundleId,
                    Version = body.Version ?? "1.0.0",
                    Realm = (BeyondImmersion.BannouService.Events.RealmEnum)body.Realm,
                    SourceBundleCount = sourceBundles.Count,
                    SourceBundleIds = sourceBundles.Select(sb => sb.BundleId).ToList(),
                    AssetCount = metabundleAssets.Count,
                    StandaloneAssetCount = standalonesToInclude.Count,
                    Bucket = bucket,
                    Key = metabundlePath,
                    SizeBytes = bundleStream.Length,
                    Owner = body.Owner
                }).ConfigureAwait(false);

            return (StatusCodes.OK, new CreateMetabundleResponse
            {
                MetabundleId = body.MetabundleId,
                Status = CreateMetabundleResponseStatus.Ready,
                AssetCount = metabundleAssets.Count,
                SizeBytes = bundleStream.Length,
                SourceBundles = sourceBundleRefs.Select(r => r.ToApiModel()).ToList(),
                StandaloneAssetCount = standalonesToInclude.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing CreateMetabundle operation");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "CreateMetabundle",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/bundles/metabundle/create",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of ResolveBundles operation.
    /// Computes optimal bundle downloads for requested assets using greedy set-cover.
    /// </summary>
    public async Task<(StatusCodes, ResolveBundlesResponse?)> ResolveBundlesAsync(ResolveBundlesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("ResolveBundles: assetCount={AssetCount}, realm={Realm}, preferMetabundles={PreferMetabundles}",
            body.AssetIds?.Count ?? 0, body.Realm, body.PreferMetabundles);

        try
        {
            if (body.AssetIds == null || body.AssetIds.Count == 0)
            {
                return (StatusCodes.BadRequest, null);
            }

            var maxAssets = _configuration.MaxResolutionAssets;
            if (body.AssetIds.Count > maxAssets)
            {
                _logger.LogWarning("ResolveBundles: Too many assets requested ({Count} > {Max})",
                    body.AssetIds.Count, maxAssets);
                return (StatusCodes.BadRequest, null);
            }

            var bundleStore = _stateStoreFactory.GetStore<BundleMetadata>(_configuration.StatestoreName);
            var indexStore = _stateStoreFactory.GetStore<AssetBundleIndex>(_configuration.StatestoreName);
            var assetStore = _stateStoreFactory.GetStore<InternalAssetRecord>(_configuration.StatestoreName);

            // Build coverage matrix: bundleId → set of requested assets it contains
            var bundleCoverage = new Dictionary<Guid, HashSet<string>>();
            var bundleMetadataCache = new Dictionary<Guid, BundleMetadata>();
            var unresolved = new List<string>();

            foreach (var assetId in body.AssetIds)
            {
                // Look up reverse index for this asset in the specified realm
                var indexKey = $"{body.Realm.ToString().ToLowerInvariant()}:asset-bundles:{assetId}";
                var index = await indexStore.GetAsync(indexKey, cancellationToken).ConfigureAwait(false);

                if (index == null || index.BundleIds.Count == 0)
                {
                    // No bundle contains this asset - might be standalone
                    continue;
                }

                foreach (var bundleId in index.BundleIds)
                {
                    // Get bundle metadata if not cached
                    if (!bundleMetadataCache.TryGetValue(bundleId, out var bundleMeta))
                    {
                        var bundleKey = $"{_configuration.BundleKeyPrefix}{bundleId}";
                        bundleMeta = await bundleStore.GetAsync(bundleKey, cancellationToken).ConfigureAwait(false);
                        if (bundleMeta != null)
                        {
                            bundleMetadataCache[bundleId] = bundleMeta;
                        }
                    }

                    if (bundleMeta == null || bundleMeta.Status != Models.BundleStatus.Ready)
                    {
                        continue;
                    }

                    // Add to coverage
                    if (!bundleCoverage.TryGetValue(bundleId, out var covered))
                    {
                        covered = new HashSet<string>();
                        bundleCoverage[bundleId] = covered;
                    }
                    covered.Add(assetId);
                }
            }

            // Greedy set-cover algorithm
            var selectedBundles = new List<ResolvedBundle>();
            var remainingAssets = new HashSet<string>(body.AssetIds);
            var maxBundles = body.MaxBundles ?? int.MaxValue;
            var bucket = _configuration.StorageBucket;
            var tokenTtl = TimeSpan.FromSeconds(_configuration.TokenTtlSeconds);

            while (remainingAssets.Count > 0 && selectedBundles.Count < maxBundles)
            {
                // Find bundle with best coverage
                string? bestBundleId = null;
                int bestCoverage = 0;
                bool bestIsMetabundle = false;

                foreach (var (bundleId, covered) in bundleCoverage)
                {
                    var coverage = covered.Count(a => remainingAssets.Contains(a));
                    if (coverage == 0)
                    {
                        continue;
                    }

                    var isMetabundle = bundleMetadataCache.TryGetValue(bundleId, out var meta) &&
                                        meta.BundleType == BundleType.Metabundle;

                    // Select if: better coverage, or equal coverage but prefer metabundle
                    var isBetter = coverage > bestCoverage ||
                                    (coverage == bestCoverage && body.PreferMetabundles == true && isMetabundle && !bestIsMetabundle);

                    if (isBetter)
                    {
                        bestBundleId = bundleId;
                        bestCoverage = coverage;
                        bestIsMetabundle = isMetabundle;
                    }
                }

                if (bestBundleId == null)
                {
                    break; // No more bundles can cover remaining assets
                }

                // Add selected bundle
                var selectedMeta = bundleMetadataCache[bestBundleId];
                var downloadUrl = await _storageProvider.GenerateDownloadUrlAsync(
                    selectedMeta.Bucket ?? bucket,
                    selectedMeta.StorageKey,
                    expiration: tokenTtl).ConfigureAwait(false);

                var providedAssets = bundleCoverage[bestBundleId]
                    .Where(a => remainingAssets.Contains(a))
                    .ToList();

                selectedBundles.Add(new ResolvedBundle
                {
                    BundleId = bestBundleId,
                    BundleType = selectedMeta.BundleType,
                    Version = selectedMeta.Version,
                    DownloadUrl = new Uri(downloadUrl.DownloadUrl),
                    ExpiresAt = downloadUrl.ExpiresAt,
                    Size = selectedMeta.SizeBytes,
                    AssetsProvided = providedAssets
                });

                // Remove covered assets from remaining
                foreach (var assetId in providedAssets)
                {
                    remainingAssets.Remove(assetId);
                }
            }

            // Check for standalone assets
            var standaloneAssets = new List<ResolvedAsset>();
            if (body.IncludeStandalone == true && remainingAssets.Count > 0)
            {
                foreach (var assetId in remainingAssets.ToList())
                {
                    var assetKey = $"{_configuration.AssetKeyPrefix}{assetId}";
                    var assetRecord = await assetStore.GetAsync(assetKey, cancellationToken).ConfigureAwait(false);

                    if (assetRecord != null)
                    {
                        var downloadUrl = await _storageProvider.GenerateDownloadUrlAsync(
                            assetRecord.Bucket,
                            assetRecord.StorageKey,
                            expiration: tokenTtl).ConfigureAwait(false);

                        standaloneAssets.Add(new ResolvedAsset
                        {
                            AssetId = assetId,
                            DownloadUrl = new Uri(downloadUrl.DownloadUrl),
                            ExpiresAt = downloadUrl.ExpiresAt,
                            Size = assetRecord.Size,
                            ContentHash = assetRecord.ContentHash
                        });

                        remainingAssets.Remove(assetId);
                    }
                }
            }

            // Any still remaining are unresolved
            unresolved.AddRange(remainingAssets);

            // Calculate efficiency
            float? efficiency = null;
            if (selectedBundles.Count > 0)
            {
                var assetsFromBundles = selectedBundles.Sum(b => b.AssetsProvided.Count);
                efficiency = (float)assetsFromBundles / selectedBundles.Count;
            }

            var response = new ResolveBundlesResponse
            {
                Bundles = selectedBundles,
                StandaloneAssets = standaloneAssets,
                Coverage = new CoverageAnalysis
                {
                    TotalRequested = body.AssetIds.Count,
                    ResolvedViaBundles = selectedBundles.Sum(b => b.AssetsProvided.Count),
                    ResolvedStandalone = standaloneAssets.Count,
                    UnresolvedCount = unresolved.Count,
                    BundleEfficiency = efficiency
                },
                Unresolved = unresolved.Count > 0 ? unresolved : null
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ResolveBundles operation");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "ResolveBundles",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/bundles/resolve",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetJobStatus operation.
    /// Returns the status of an async metabundle creation job.
    /// </summary>
    public async Task<(StatusCodes, GetJobStatusResponse?)> GetJobStatusAsync(GetJobStatusRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetJobStatus: jobId={JobId}", body.JobId);

        try
        {
            if (body.JobId == default)
            {
                _logger.LogWarning("GetJobStatus: Invalid job ID");
                return (StatusCodes.BadRequest, null);
            }

            var jobStore = _stateStoreFactory.GetStore<MetabundleJob>(_configuration.StatestoreName);
            var jobKey = $"{_configuration.MetabundleJobKeyPrefix}{body.JobId}";
            var job = await jobStore.GetAsync(jobKey, cancellationToken).ConfigureAwait(false);

            if (job == null)
            {
                _logger.LogWarning("GetJobStatus: Job {JobId} not found", body.JobId);
                return (StatusCodes.NotFound, null);
            }

            // Convert internal status to API status
            var apiStatus = job.Status switch
            {
                InternalJobStatus.Queued => GetJobStatusResponseStatus.Queued,
                InternalJobStatus.Processing => GetJobStatusResponseStatus.Processing,
                InternalJobStatus.Ready => GetJobStatusResponseStatus.Ready,
                InternalJobStatus.Failed => GetJobStatusResponseStatus.Failed,
                InternalJobStatus.Cancelled => GetJobStatusResponseStatus.Cancelled,
                _ => GetJobStatusResponseStatus.Failed
            };

            var response = new GetJobStatusResponse
            {
                JobId = job.JobId,
                MetabundleId = job.MetabundleId.ToString(),
                Status = apiStatus,
                Progress = job.Progress,
                CreatedAt = job.CreatedAt,
                UpdatedAt = job.UpdatedAt,
                ProcessingTimeMs = job.ProcessingTimeMs,
                ErrorCode = job.ErrorCode,
                ErrorMessage = job.ErrorMessage
            };

            // If job is complete, include result data and generate download URL
            if (job.Status == InternalJobStatus.Ready && job.Result != null)
            {
                response.AssetCount = job.Result.AssetCount;
                response.StandaloneAssetCount = job.Result.StandaloneAssetCount;
                response.SizeBytes = job.Result.SizeBytes;

                // Generate download URL if we have the storage key
                if (!string.IsNullOrEmpty(job.Result.StorageKey))
                {
                    var tokenTtl = TimeSpan.FromSeconds(_configuration.DownloadTokenTtlSeconds);
                    var downloadResult = await _storageProvider.GenerateDownloadUrlAsync(
                        _configuration.StorageBucket,
                        job.Result.StorageKey,
                        expiration: tokenTtl).ConfigureAwait(false);
                    response.DownloadUrl = new Uri(downloadResult.DownloadUrl);
                }

                // Convert source bundles
                if (job.Result.SourceBundles != null)
                {
                    response.SourceBundles = job.Result.SourceBundles
                        .Select(sb => new SourceBundleReference
                        {
                            BundleId = sb.BundleId.ToString(),
                            Version = sb.Version,
                            AssetIds = sb.AssetIds.Select(a => a.ToString()).ToList(),
                            ContentHash = sb.ContentHash
                        })
                        .ToList();
                }
            }

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetJobStatus operation");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "GetJobStatus",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/bundles/job/status",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of CancelJob operation.
    /// Cancels a pending or processing async metabundle job.
    /// </summary>
    public async Task<(StatusCodes, CancelJobResponse?)> CancelJobAsync(CancelJobRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("CancelJob: jobId={JobId}", body.JobId);

        try
        {
            if (body.JobId == default)
            {
                _logger.LogWarning("CancelJob: Invalid job ID");
                return (StatusCodes.BadRequest, null);
            }

            var jobStore = _stateStoreFactory.GetStore<MetabundleJob>(_configuration.StatestoreName);
            var jobKey = $"{_configuration.MetabundleJobKeyPrefix}{body.JobId}";
            var job = await jobStore.GetAsync(jobKey, cancellationToken).ConfigureAwait(false);

            if (job == null)
            {
                _logger.LogWarning("CancelJob: Job {JobId} not found", body.JobId);
                return (StatusCodes.NotFound, null);
            }

            // Check if job can be cancelled
            if (job.Status == InternalJobStatus.Ready || job.Status == InternalJobStatus.Failed)
            {
                // Job already completed - cannot cancel
                var completedStatus = job.Status == InternalJobStatus.Ready
                    ? CancelJobResponseStatus.Ready
                    : CancelJobResponseStatus.Failed;

                return (StatusCodes.Conflict, new CancelJobResponse
                {
                    JobId = body.JobId,
                    Cancelled = false,
                    Status = completedStatus,
                    Message = $"Job already completed with status '{job.Status}'"
                });
            }

            if (job.Status == InternalJobStatus.Cancelled)
            {
                // Already cancelled
                return (StatusCodes.OK, new CancelJobResponse
                {
                    JobId = body.JobId,
                    Cancelled = true,
                    Status = CancelJobResponseStatus.Cancelled,
                    Message = "Job was already cancelled"
                });
            }

            // Cancel the job
            var previousStatus = job.Status;
            job.Status = InternalJobStatus.Cancelled;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ErrorCode = MetabundleErrorCode.CANCELLED.ToString();
            job.ErrorMessage = "Job cancelled by user request";
            await jobStore.SaveAsync(jobKey, job, cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("CancelJob: Job {JobId} cancelled (was {PreviousStatus})", body.JobId, previousStatus);

            // Emit completion event if there's a session to notify
            if (job.RequesterSessionId.HasValue)
            {
                await _eventEmitter.EmitMetabundleCreationCompleteAsync(
                    job.RequesterSessionId.Value.ToString(),
                    job.JobId,
                    job.MetabundleId.ToString(),
                    success: false,
                    MetabundleJobStatus.Cancelled,
                    downloadUrl: null,
                    sizeBytes: null,
                    assetCount: null,
                    standaloneAssetCount: null,
                    processingTimeMs: null,
                    MetabundleErrorCode.CANCELLED,
                    "Job cancelled by user request",
                    cancellationToken).ConfigureAwait(false);
            }

            return (StatusCodes.OK, new CancelJobResponse
            {
                JobId = body.JobId,
                Cancelled = true,
                Status = CancelJobResponseStatus.Cancelled,
                Message = $"Job cancelled successfully (was {previousStatus})"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing CancelJob operation");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "CancelJob",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/bundles/job/cancel",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of QueryBundlesByAsset operation.
    /// Finds all bundles containing a specific asset.
    /// </summary>
    public async Task<(StatusCodes, QueryBundlesByAssetResponse?)> QueryBundlesByAssetAsync(QueryBundlesByAssetRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("QueryBundlesByAsset: assetId={AssetId}, realm={Realm}", body.AssetId, body.Realm);

        try
        {
            if (string.IsNullOrWhiteSpace(body.AssetId))
            {
                return (StatusCodes.BadRequest, null);
            }

            var indexStore = _stateStoreFactory.GetStore<AssetBundleIndex>(_configuration.StatestoreName);
            var bundleStore = _stateStoreFactory.GetStore<BundleMetadata>(_configuration.StatestoreName);

            // Look up reverse index
            var indexKey = $"{body.Realm.ToString().ToLowerInvariant()}:asset-bundles:{body.AssetId}";
            var index = await indexStore.GetAsync(indexKey, cancellationToken).ConfigureAwait(false);

            if (index == null || index.BundleIds.Count == 0)
            {
                return (StatusCodes.OK, new QueryBundlesByAssetResponse
                {
                    AssetId = body.AssetId,
                    Bundles = new List<BundleSummary>(),
                    Total = 0,
                    Limit = body.Limit,
                    Offset = body.Offset
                });
            }

            // Filter and fetch bundle metadata
            var bundles = new List<BundleSummary>();
            foreach (var bundleId in index.BundleIds)
            {
                var bundleKey = $"{_configuration.BundleKeyPrefix}{bundleId}";
                var bundleMeta = await bundleStore.GetAsync(bundleKey, cancellationToken).ConfigureAwait(false);

                if (bundleMeta == null)
                {
                    continue;
                }

                // Apply bundle type filter if specified
                if (body.BundleType.HasValue && bundleMeta.BundleType != body.BundleType.Value)
                {
                    continue;
                }

                bundles.Add(bundleMeta.ToBundleSummary());
            }

            // Apply pagination
            var total = bundles.Count;
            var paginatedBundles = bundles
                .Skip(body.Offset)
                .Take(body.Limit)
                .ToList();

            return (StatusCodes.OK, new QueryBundlesByAssetResponse
            {
                AssetId = body.AssetId,
                Bundles = paginatedBundles,
                Total = total,
                Limit = body.Limit,
                Offset = body.Offset
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing QueryBundlesByAsset operation");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "QueryBundlesByAsset",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/bundles/query/by-asset",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of BulkGetAssets operation.
    /// Retrieves metadata for multiple assets in a single request.
    /// </summary>
    public async Task<(StatusCodes, BulkGetAssetsResponse?)> BulkGetAssetsAsync(BulkGetAssetsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("BulkGetAssets: assetCount={AssetCount}, includeDownloadUrls={IncludeUrls}",
            body.AssetIds?.Count ?? 0, body.IncludeDownloadUrls);

        try
        {
            if (body.AssetIds == null || body.AssetIds.Count == 0)
            {
                return (StatusCodes.BadRequest, null);
            }

            var maxAssets = _configuration.MaxBulkGetAssets;
            if (body.AssetIds.Count > maxAssets)
            {
                _logger.LogWarning("BulkGetAssets: Too many assets requested ({Count} > {Max})",
                    body.AssetIds.Count, maxAssets);
                return (StatusCodes.BadRequest, null);
            }

            var assetStore = _stateStoreFactory.GetStore<InternalAssetRecord>(_configuration.StatestoreName);
            var assets = new List<AssetWithDownloadUrl>();
            var notFound = new List<string>();
            var tokenTtl = TimeSpan.FromSeconds(_configuration.DownloadTokenTtlSeconds);

            foreach (var assetId in body.AssetIds)
            {
                var assetKey = $"{_configuration.AssetKeyPrefix}{assetId}";
                var record = await assetStore.GetAsync(assetKey, cancellationToken).ConfigureAwait(false);

                if (record == null)
                {
                    notFound.Add(assetId);
                    continue;
                }

                var metadata = record.ToPublicMetadata();

                Uri? downloadUrl = null;
                DateTimeOffset? expiresAt = null;

                if (body.IncludeDownloadUrls == true)
                {
                    var downloadResult = await _storageProvider.GenerateDownloadUrlAsync(
                        record.Bucket,
                        record.StorageKey,
                        expiration: tokenTtl).ConfigureAwait(false);
                    downloadUrl = new Uri(downloadResult.DownloadUrl);
                    expiresAt = downloadResult.ExpiresAt;
                }

                assets.Add(new AssetWithDownloadUrl
                {
                    AssetId = record.AssetId,
                    VersionId = "latest",
                    DownloadUrl = downloadUrl,
                    ExpiresAt = expiresAt,
                    Size = record.Size,
                    ContentHash = record.ContentHash,
                    ContentType = record.ContentType,
                    Metadata = metadata
                });
            }

            return (StatusCodes.OK, new BulkGetAssetsResponse
            {
                Assets = assets,
                NotFound = notFound
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing BulkGetAssets operation");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "BulkGetAssets",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/assets/bulk-get",
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
        var typeIndexKey = $"{_configuration.AssetIndexKeyPrefix}type:{asset.AssetType.ToString().ToLowerInvariant()}";
        await AddToIndexWithOptimisticConcurrencyAsync(typeIndexKey, asset.AssetId, cancellationToken).ConfigureAwait(false);

        // Index by realm
        var realmIndexKey = $"{_configuration.AssetIndexKeyPrefix}realm:{asset.Realm.ToString().ToLowerInvariant()}";
        await AddToIndexWithOptimisticConcurrencyAsync(realmIndexKey, asset.AssetId, cancellationToken).ConfigureAwait(false);

        // Index by tags
        if (asset.Tags != null)
        {
            foreach (var tag in asset.Tags)
            {
                var tagIndexKey = $"{_configuration.AssetIndexKeyPrefix}tag:{tag.ToLowerInvariant()}";
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
        var indexStore = _stateStoreFactory.GetStore<List<string>>(_configuration.StatestoreName);

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
            if (etag == null)
            {
                // First time creating this index
                await indexStore.SaveAsync(indexKey, index, cancellationToken: cancellationToken).ConfigureAwait(false);
                return; // Success
            }

            var savedEtag = await indexStore.TrySaveAsync(indexKey, index, etag, cancellationToken).ConfigureAwait(false);
            if (savedEtag != null)
            {
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

    /// <summary>
    /// Indexes all assets in a bundle for reverse lookup (asset → bundles).
    /// </summary>
    private async Task IndexBundleAssetsAsync(BundleMetadata bundle, CancellationToken cancellationToken)
    {
        var indexStore = _stateStoreFactory.GetStore<AssetBundleIndex>(_configuration.StatestoreName);
        var realmPrefix = bundle.Realm.ToString().ToLowerInvariant();

        foreach (var assetId in bundle.AssetIds)
        {
            var indexKey = $"{realmPrefix}:asset-bundles:{assetId}";
            await AddBundleToAssetIndexAsync(indexStore, indexKey, bundle.BundleId, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug("IndexBundleAssets: Indexed {AssetCount} assets for bundle {BundleId}",
            bundle.AssetIds.Count, bundle.BundleId);
    }

    /// <summary>
    /// Adds a bundle ID to an asset's reverse index using optimistic concurrency.
    /// </summary>
    private async Task AddBundleToAssetIndexAsync(
        IStateStore<AssetBundleIndex> indexStore,
        string indexKey,
        string bundleId,
        CancellationToken cancellationToken,
        int maxRetries = 5)
    {
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(indexKey, cancellationToken).ConfigureAwait(false);

            index ??= new AssetBundleIndex();

            // Already indexed
            var bundleGuid = Guid.Parse(bundleId);
            if (index.BundleIds.Contains(bundleGuid))
            {
                return;
            }

            index.BundleIds.Add(bundleGuid);

            if (etag == null)
            {
                await indexStore.SaveAsync(indexKey, index, cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }

            var savedEtag = await indexStore.TrySaveAsync(indexKey, index, etag, cancellationToken).ConfigureAwait(false);
            if (savedEtag != null)
            {
                return;
            }

            // ETag mismatch - retry
            await Task.Delay(TimeSpan.FromMilliseconds(10 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
        }

        _logger.LogWarning(
            "Failed to update asset-bundle index {IndexKey} after {MaxRetries} attempts",
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
    /// Pool type names come from configuration for operational consistency.
    /// </summary>
    private string GetProcessorPoolType(string contentType)
    {
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return _configuration.TextureProcessorPoolType;
        }
        if (contentType.StartsWith("model/", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("fbx", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("blender", StringComparison.OrdinalIgnoreCase))
        {
            return _configuration.ModelProcessorPoolType;
        }
        if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return _configuration.AudioProcessorPoolType;
        }

        return _configuration.DefaultProcessorPoolType;
    }

    /// <summary>
    /// Ensures at least one processor is available for the given pool type.
    /// If no processors are available, spawns a new one via the orchestrator.
    /// </summary>
    /// <param name="poolType">The pool type to check/spawn.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a processor is available (or was spawned), false if spawning failed.</returns>
    private async Task<bool> EnsureProcessorAvailableAsync(
        string poolType,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if any processors are available in the pool
            var availableCount = await _processorPoolManager.GetAvailableCountAsync(poolType, cancellationToken);

            if (availableCount > 0)
            {
                _logger.LogDebug(
                    "EnsureProcessorAvailable: {Count} processors available in pool {PoolType}",
                    availableCount, poolType);
                return true;
            }

            // No processors available - spawn one via orchestrator
            _logger.LogInformation(
                "EnsureProcessorAvailable: No processors available in pool {PoolType}, spawning new instance",
                poolType);

            // Get current total count to calculate target
            var totalCount = await _processorPoolManager.GetTotalNodeCountAsync(poolType, cancellationToken);

            // Request orchestrator to scale up by 1
            var scaleResponse = await _orchestratorClient.ScalePoolAsync(
                new ScalePoolRequest
                {
                    PoolType = poolType,
                    TargetInstances = totalCount + 1
                },
                cancellationToken);

            _logger.LogInformation(
                "EnsureProcessorAvailable: Orchestrator scaled pool {PoolType} from {Previous} to {Current} instances",
                poolType, scaleResponse.PreviousInstances, scaleResponse.CurrentInstances);

            // Wait for the new processor to register (poll state)
            var maxWait = TimeSpan.FromSeconds(60);
            var pollInterval = TimeSpan.FromSeconds(2);
            var elapsed = TimeSpan.Zero;

            while (elapsed < maxWait)
            {
                await Task.Delay(pollInterval, cancellationToken);
                elapsed += pollInterval;

                availableCount = await _processorPoolManager.GetAvailableCountAsync(poolType, cancellationToken);
                if (availableCount > 0)
                {
                    _logger.LogInformation(
                        "EnsureProcessorAvailable: Processor registered in pool {PoolType} after {Elapsed:F1}s",
                        poolType, elapsed.TotalSeconds);
                    return true;
                }
            }

            _logger.LogWarning(
                "EnsureProcessorAvailable: Spawned processor did not register within {Timeout}s timeout for pool {PoolType}",
                maxWait.TotalSeconds, poolType);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EnsureProcessorAvailable: Failed to ensure processor availability for pool {PoolType}",
                poolType);
            return false;
        }
    }

    /// <summary>
    /// Delegates processing to the processing pool.
    /// Spawns a processor if none available, then acquires and publishes job.
    /// </summary>
    private async Task DelegateToProcessingPoolAsync(
        string assetId,
        AssetMetadata metadata,
        string storageKey,
        CancellationToken cancellationToken)
    {
        var poolType = GetProcessorPoolType(metadata.ContentType);
        var assetStore = _stateStoreFactory.GetStore<AssetMetadata>(_configuration.StatestoreName);

        try
        {
            // Ensure at least one processor is available, spawning if needed
            var ensured = await EnsureProcessorAvailableAsync(poolType, cancellationToken);
            if (!ensured)
            {
                _logger.LogWarning(
                    "DelegateToProcessingPool: Could not ensure processor availability for pool {PoolType}, queueing asset {AssetId} for retry",
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
                return;
            }

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
            await assetStore.SaveAsync($"{_configuration.AssetKeyPrefix}{assetId}", metadata, cancellationToken: cancellationToken).ConfigureAwait(false);

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
            await assetStore.SaveAsync($"{_configuration.AssetKeyPrefix}{assetId}", metadata, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    #endregion

    #region Bundle Management

    /// <summary>
    /// Update bundle metadata (name, description, tags).
    /// </summary>
    public async Task<(StatusCodes, UpdateBundleResponse?)> UpdateBundleAsync(UpdateBundleRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("UpdateBundle: bundleId={BundleId}", body.BundleId);

        try
        {
            if (string.IsNullOrWhiteSpace(body.BundleId))
            {
                _logger.LogWarning("UpdateBundle: Empty bundleId");
                return (StatusCodes.BadRequest, null);
            }

            var bundleStore = _stateStoreFactory.GetStore<Models.BundleMetadata>(_configuration.StatestoreName);
            var versionStore = _stateStoreFactory.GetStore<Models.StoredBundleVersionRecord>(_configuration.StatestoreName);
            var bundleKey = $"{_configuration.BundleKeyPrefix}{body.BundleId}";

            var bundle = await bundleStore.GetAsync(bundleKey, cancellationToken);
            if (bundle == null)
            {
                _logger.LogWarning("UpdateBundle: Bundle {BundleId} not found", body.BundleId);
                return (StatusCodes.NotFound, null);
            }

            if (bundle.LifecycleStatus == Models.BundleLifecycleStatus.Deleted)
            {
                _logger.LogWarning("UpdateBundle: Bundle {BundleId} is deleted", body.BundleId);
                return (StatusCodes.NotFound, null);
            }

            // Track changes
            var changes = new List<string>();
            var previousVersion = bundle.MetadataVersion;

            // Apply updates
            if (body.Name != null && body.Name != bundle.Name)
            {
                bundle.Name = body.Name;
                changes.Add("name changed");
            }

            if (body.Description != null && body.Description != bundle.Description)
            {
                bundle.Description = body.Description;
                changes.Add("description changed");
            }

            // Handle tag operations
            bundle.Tags ??= new Dictionary<string, string>();

            if (body.Tags != null)
            {
                // Replace all tags
                bundle.Tags = new Dictionary<string, string>(body.Tags);
                changes.Add("tags replaced");
            }
            else
            {
                // Add tags
                if (body.AddTags != null)
                {
                    foreach (var (key, value) in body.AddTags)
                    {
                        bundle.Tags[key] = value;
                        changes.Add($"tag '{key}' added");
                    }
                }

                // Remove tags
                if (body.RemoveTags != null)
                {
                    foreach (var key in body.RemoveTags)
                    {
                        if (bundle.Tags.Remove(key))
                        {
                            changes.Add($"tag '{key}' removed");
                        }
                    }
                }
            }

            if (changes.Count == 0)
            {
                // No changes made
                return (StatusCodes.OK, new UpdateBundleResponse
                {
                    BundleId = body.BundleId,
                    Version = bundle.MetadataVersion,
                    PreviousVersion = bundle.MetadataVersion,
                    Changes = new List<string> { "no changes" },
                    UpdatedAt = bundle.UpdatedAt ?? bundle.CreatedAt
                });
            }

            // Increment version and set updated timestamp
            bundle.MetadataVersion++;
            bundle.UpdatedAt = DateTimeOffset.UtcNow;

            // Save version history record
            var versionRecord = new Models.StoredBundleVersionRecord
            {
                BundleId = body.BundleId,
                Version = bundle.MetadataVersion,
                CreatedAt = bundle.UpdatedAt.Value,
                CreatedBy = bundle.Owner ?? "system",
                Changes = changes,
                Reason = body.Reason
            };

            var versionKey = $"bundle-version:{body.BundleId}:{bundle.MetadataVersion}";
            await versionStore.SaveAsync(versionKey, versionRecord, cancellationToken: cancellationToken);

            // Add to version index for efficient listing
            var versionIndexKey = $"bundle-version-index:{body.BundleId}";
            await versionStore.AddToSetAsync(versionIndexKey, bundle.MetadataVersion, cancellationToken: cancellationToken);

            // Save updated bundle
            await bundleStore.SaveAsync(bundleKey, bundle, cancellationToken: cancellationToken);

            // Publish event
            await _messageBus.TryPublishAsync("asset.bundle.updated", new BeyondImmersion.BannouService.Events.BundleUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = bundle.UpdatedAt.Value,
                BundleId = body.BundleId,
                Version = bundle.MetadataVersion,
                PreviousVersion = previousVersion,
                Changes = changes,
                Reason = body.Reason,
                UpdatedBy = bundle.Owner ?? "system",
                Realm = MapRealmToEventEnum(bundle.Realm)
            });

            _logger.LogInformation("UpdateBundle: Updated bundle {BundleId} from version {PreviousVersion} to {NewVersion}",
                body.BundleId, previousVersion, bundle.MetadataVersion);

            return (StatusCodes.OK, new UpdateBundleResponse
            {
                BundleId = body.BundleId,
                Version = bundle.MetadataVersion,
                PreviousVersion = previousVersion,
                Changes = changes,
                UpdatedAt = bundle.UpdatedAt.Value
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateBundle: Unexpected error for bundle {BundleId}", body.BundleId);
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "UpdateBundle",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/bundles/update",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Soft-delete or permanently delete a bundle.
    /// </summary>
    public async Task<(StatusCodes, DeleteBundleResponse?)> DeleteBundleAsync(DeleteBundleRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("DeleteBundle: bundleId={BundleId}, permanent={Permanent}", body.BundleId, body.Permanent);

        try
        {
            if (string.IsNullOrWhiteSpace(body.BundleId))
            {
                _logger.LogWarning("DeleteBundle: Empty bundleId");
                return (StatusCodes.BadRequest, null);
            }

            var bundleStore = _stateStoreFactory.GetStore<Models.BundleMetadata>(_configuration.StatestoreName);
            var versionStore = _stateStoreFactory.GetStore<Models.StoredBundleVersionRecord>(_configuration.StatestoreName);
            var bundleKey = $"{_configuration.BundleKeyPrefix}{body.BundleId}";

            var bundle = await bundleStore.GetAsync(bundleKey, cancellationToken);
            if (bundle == null)
            {
                _logger.LogWarning("DeleteBundle: Bundle {BundleId} not found", body.BundleId);
                return (StatusCodes.NotFound, null);
            }

            var deletedAt = DateTimeOffset.UtcNow;
            DateTimeOffset? retentionUntil = null;

            if (body.Permanent == true)
            {
                // Permanent delete - remove from storage and state
                // Delete the actual bundle file
                if (!string.IsNullOrEmpty(bundle.StorageKey))
                {
                    try
                    {
                        await _storageProvider.DeleteObjectAsync(
                            bundle.Bucket ?? _configuration.StorageBucket,
                            bundle.StorageKey).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "DeleteBundle: Failed to delete storage object for bundle {BundleId}", body.BundleId);
                        // Continue with metadata deletion even if storage deletion fails
                    }
                }

                // Delete metadata
                await bundleStore.DeleteAsync(bundleKey, cancellationToken);

                // Remove from asset-bundle index
                await RemoveFromBundleIndexAsync(bundle.BundleId, bundle.AssetIds, cancellationToken);

                _logger.LogInformation("DeleteBundle: Permanently deleted bundle {BundleId}", body.BundleId);
            }
            else
            {
                // Soft delete - mark as deleted with retention period
                var retentionDays = _configuration.DeletedBundleRetentionDays > 0 ? _configuration.DeletedBundleRetentionDays : 30;
                retentionUntil = deletedAt.AddDays(retentionDays);

                bundle.LifecycleStatus = Models.BundleLifecycleStatus.Deleted;
                bundle.DeletedAt = deletedAt;
                bundle.MetadataVersion++;
                bundle.UpdatedAt = deletedAt;

                // Save version history
                var versionRecord = new Models.StoredBundleVersionRecord
                {
                    BundleId = body.BundleId,
                    Version = bundle.MetadataVersion,
                    CreatedAt = deletedAt,
                    CreatedBy = bundle.Owner ?? "system",
                    Changes = new List<string> { "bundle deleted" },
                    Reason = body.Reason,
                    Snapshot = bundle // Save snapshot for potential restore
                };

                var versionKey = $"bundle-version:{body.BundleId}:{bundle.MetadataVersion}";
                await versionStore.SaveAsync(versionKey, versionRecord, cancellationToken: cancellationToken);

                // Add to version index for efficient listing
                var versionIndexKey = $"bundle-version-index:{body.BundleId}";
                await versionStore.AddToSetAsync(versionIndexKey, bundle.MetadataVersion, cancellationToken: cancellationToken);

                await bundleStore.SaveAsync(bundleKey, bundle, cancellationToken: cancellationToken);

                _logger.LogInformation("DeleteBundle: Soft-deleted bundle {BundleId}, retention until {RetentionUntil}",
                    body.BundleId, retentionUntil);
            }

            // Publish event
            await _messageBus.TryPublishAsync("asset.bundle.deleted", new BeyondImmersion.BannouService.Events.BundleDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = deletedAt,
                BundleId = body.BundleId,
                Permanent = body.Permanent == true,
                RetentionUntil = retentionUntil,
                Reason = body.Reason,
                DeletedBy = bundle.Owner ?? "system",
                Realm = MapRealmToEventEnum(bundle.Realm)
            });

            return (StatusCodes.OK, new DeleteBundleResponse
            {
                BundleId = body.BundleId,
                Status = body.Permanent == true
                    ? DeleteBundleResponseStatus.Permanently_deleted
                    : DeleteBundleResponseStatus.Deleted,
                DeletedAt = deletedAt,
                RetentionUntil = retentionUntil
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteBundle: Unexpected error for bundle {BundleId}", body.BundleId);
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "DeleteBundle",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/bundles/delete",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Restore a soft-deleted bundle.
    /// </summary>
    public async Task<(StatusCodes, RestoreBundleResponse?)> RestoreBundleAsync(RestoreBundleRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RestoreBundle: bundleId={BundleId}", body.BundleId);

        try
        {
            if (string.IsNullOrWhiteSpace(body.BundleId))
            {
                _logger.LogWarning("RestoreBundle: Empty bundleId");
                return (StatusCodes.BadRequest, null);
            }

            var bundleStore = _stateStoreFactory.GetStore<Models.BundleMetadata>(_configuration.StatestoreName);
            var versionStore = _stateStoreFactory.GetStore<Models.StoredBundleVersionRecord>(_configuration.StatestoreName);
            var bundleKey = $"{_configuration.BundleKeyPrefix}{body.BundleId}";

            var bundle = await bundleStore.GetAsync(bundleKey, cancellationToken);
            if (bundle == null)
            {
                _logger.LogWarning("RestoreBundle: Bundle {BundleId} not found", body.BundleId);
                return (StatusCodes.NotFound, null);
            }

            if (bundle.LifecycleStatus != Models.BundleLifecycleStatus.Deleted)
            {
                _logger.LogWarning("RestoreBundle: Bundle {BundleId} is not deleted (status={Status})",
                    body.BundleId, bundle.LifecycleStatus);
                return (StatusCodes.BadRequest, null);
            }

            var restoredAt = DateTimeOffset.UtcNow;
            var restoredFromVersion = bundle.MetadataVersion;

            // Restore the bundle
            bundle.LifecycleStatus = Models.BundleLifecycleStatus.Active;
            bundle.DeletedAt = null;
            bundle.MetadataVersion++;
            bundle.UpdatedAt = restoredAt;

            // Save version history
            var versionRecord = new Models.StoredBundleVersionRecord
            {
                BundleId = body.BundleId,
                Version = bundle.MetadataVersion,
                CreatedAt = restoredAt,
                CreatedBy = bundle.Owner ?? "system",
                Changes = new List<string> { "bundle restored" },
                Reason = body.Reason
            };

            var versionKey = $"bundle-version:{body.BundleId}:{bundle.MetadataVersion}";
            await versionStore.SaveAsync(versionKey, versionRecord, cancellationToken: cancellationToken);

            // Add to version index for efficient listing
            var versionIndexKey = $"bundle-version-index:{body.BundleId}";
            await versionStore.AddToSetAsync(versionIndexKey, bundle.MetadataVersion, cancellationToken: cancellationToken);

            await bundleStore.SaveAsync(bundleKey, bundle, cancellationToken: cancellationToken);

            // Publish event
            await _messageBus.TryPublishAsync("asset.bundle.restored", new BeyondImmersion.BannouService.Events.BundleRestoredEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = restoredAt,
                BundleId = body.BundleId,
                RestoredFromVersion = restoredFromVersion,
                Reason = body.Reason,
                RestoredBy = bundle.Owner ?? "system",
                Realm = MapRealmToEventEnum(bundle.Realm)
            });

            _logger.LogInformation("RestoreBundle: Restored bundle {BundleId} from version {RestoredFromVersion}",
                body.BundleId, restoredFromVersion);

            return (StatusCodes.OK, new RestoreBundleResponse
            {
                BundleId = body.BundleId,
                Status = "active",
                RestoredAt = restoredAt,
                RestoredFromVersion = restoredFromVersion
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RestoreBundle: Unexpected error for bundle {BundleId}", body.BundleId);
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "RestoreBundle",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/bundles/restore",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Query bundles with advanced filters.
    /// Note: This implementation requires the owner filter to be provided for efficient querying.
    /// Without owner filter, returns an empty result (full scan not supported).
    /// </summary>
    public async Task<(StatusCodes, QueryBundlesResponse?)> QueryBundlesAsync(QueryBundlesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("QueryBundles: owner={Owner}, tags={TagCount}, nameContains={NameContains}",
            body.Owner, body.Tags?.Count ?? 0, body.NameContains);

        try
        {
            var bundleStore = _stateStoreFactory.GetStore<Models.BundleMetadata>(_configuration.StatestoreName);

            // For now, require owner filter for efficient querying
            // A full bundle registry would be needed for arbitrary queries
            if (string.IsNullOrWhiteSpace(body.Owner))
            {
                _logger.LogWarning("QueryBundles: Owner filter required for bundle queries");
                return (StatusCodes.OK, new QueryBundlesResponse
                {
                    Bundles = new List<BundleInfo>(),
                    TotalCount = 0,
                    Limit = body.Limit,
                    Offset = body.Offset
                });
            }

            // Get bundle IDs from owner index
            var ownerIndexKey = $"bundle-owner-index:{body.Owner}";
            var bundleIds = await bundleStore.GetSetAsync<string>(ownerIndexKey, cancellationToken);

            if (bundleIds.Count == 0)
            {
                return (StatusCodes.OK, new QueryBundlesResponse
                {
                    Bundles = new List<BundleInfo>(),
                    TotalCount = 0,
                    Limit = body.Limit,
                    Offset = body.Offset
                });
            }

            // Bulk load bundles
            var bundleKeys = bundleIds.Select(id => $"{_configuration.BundleKeyPrefix}{id}");
            var bundleDict = await bundleStore.GetBulkAsync(bundleKeys, cancellationToken);
            var bundles = bundleDict.Values.AsEnumerable();

            // Apply filters
            if (body.IncludeDeleted != true)
            {
                bundles = bundles.Where(b => b.LifecycleStatus != Models.BundleLifecycleStatus.Deleted);
            }

            if (body.Status.HasValue)
            {
                var targetStatus = body.Status.Value switch
                {
                    BundleLifecycle.Active => Models.BundleLifecycleStatus.Active,
                    BundleLifecycle.Deleted => Models.BundleLifecycleStatus.Deleted,
                    BundleLifecycle.Processing => Models.BundleLifecycleStatus.Processing,
                    _ => Models.BundleLifecycleStatus.Active
                };
                bundles = bundles.Where(b => b.LifecycleStatus == targetStatus);
            }

            if (body.Realm.HasValue)
            {
                bundles = bundles.Where(b => b.Realm == body.Realm.Value);
            }

            if (body.BundleType.HasValue)
            {
                bundles = bundles.Where(b => b.BundleType == body.BundleType.Value);
            }

            if (!string.IsNullOrWhiteSpace(body.NameContains))
            {
                bundles = bundles.Where(b =>
                    b.Name != null && b.Name.Contains(body.NameContains, StringComparison.OrdinalIgnoreCase));
            }

            if (body.CreatedAfter.HasValue)
            {
                bundles = bundles.Where(b => b.CreatedAt >= body.CreatedAfter.Value);
            }

            if (body.CreatedBefore.HasValue)
            {
                bundles = bundles.Where(b => b.CreatedAt <= body.CreatedBefore.Value);
            }

            if (body.Tags != null && body.Tags.Count > 0)
            {
                bundles = bundles.Where(b =>
                    b.Tags != null && body.Tags.All(t =>
                        b.Tags.TryGetValue(t.Key, out var value) && value == t.Value));
            }

            if (body.TagExists != null && body.TagExists.Count > 0)
            {
                bundles = bundles.Where(b =>
                    b.Tags != null && body.TagExists.All(key => b.Tags.ContainsKey(key)));
            }

            if (body.TagNotExists != null && body.TagNotExists.Count > 0)
            {
                bundles = bundles.Where(b =>
                    b.Tags == null || !body.TagNotExists.Any(key => b.Tags.ContainsKey(key)));
            }

            var bundleList = bundles.ToList();
            var totalCount = bundleList.Count;

            // Apply sorting
            var sortField = body.SortField ?? QueryBundlesRequestSortField.Created_at;
            var sortDesc = body.SortOrder != QueryBundlesRequestSortOrder.Asc;

            bundleList = sortField switch
            {
                QueryBundlesRequestSortField.Name => sortDesc
                    ? bundleList.OrderByDescending(b => b.Name).ToList()
                    : bundleList.OrderBy(b => b.Name).ToList(),
                QueryBundlesRequestSortField.Updated_at => sortDesc
                    ? bundleList.OrderByDescending(b => b.UpdatedAt ?? b.CreatedAt).ToList()
                    : bundleList.OrderBy(b => b.UpdatedAt ?? b.CreatedAt).ToList(),
                QueryBundlesRequestSortField.Size => sortDesc
                    ? bundleList.OrderByDescending(b => b.SizeBytes).ToList()
                    : bundleList.OrderBy(b => b.SizeBytes).ToList(),
                _ => sortDesc
                    ? bundleList.OrderByDescending(b => b.CreatedAt).ToList()
                    : bundleList.OrderBy(b => b.CreatedAt).ToList()
            };

            // Apply pagination
            var limit = Math.Min(body.Limit, 1000);
            var offset = body.Offset;

            var pagedBundles = bundleList
                .Skip(offset)
                .Take(limit)
                .Select(b => b.ToApiMetadata())
                .ToList();

            _logger.LogInformation("QueryBundles: Found {TotalCount} bundles for owner {Owner}, returning {Count}",
                totalCount, body.Owner, pagedBundles.Count);

            return (StatusCodes.OK, new QueryBundlesResponse
            {
                Bundles = pagedBundles,
                TotalCount = totalCount,
                Limit = limit,
                Offset = offset
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QueryBundles: Unexpected error");
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "QueryBundles",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/bundles/query",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// List version history for a bundle.
    /// </summary>
    public async Task<(StatusCodes, ListBundleVersionsResponse?)> ListBundleVersionsAsync(ListBundleVersionsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("ListBundleVersions: bundleId={BundleId}", body.BundleId);

        try
        {
            if (string.IsNullOrWhiteSpace(body.BundleId))
            {
                _logger.LogWarning("ListBundleVersions: Empty bundleId");
                return (StatusCodes.BadRequest, null);
            }

            var bundleStore = _stateStoreFactory.GetStore<Models.BundleMetadata>(_configuration.StatestoreName);
            var versionStore = _stateStoreFactory.GetStore<Models.StoredBundleVersionRecord>(_configuration.StatestoreName);
            var bundleKey = $"{_configuration.BundleKeyPrefix}{body.BundleId}";

            var bundle = await bundleStore.GetAsync(bundleKey, cancellationToken);
            if (bundle == null)
            {
                _logger.LogWarning("ListBundleVersions: Bundle {BundleId} not found", body.BundleId);
                return (StatusCodes.NotFound, null);
            }

            // Get version numbers from index
            var versionIndexKey = $"bundle-version-index:{body.BundleId}";
            var versionNumbers = await versionStore.GetSetAsync<int>(versionIndexKey, cancellationToken);

            // Sort by version number descending (newest first)
            var sortedVersionNumbers = versionNumbers
                .OrderByDescending(v => v)
                .ToList();

            var totalCount = sortedVersionNumbers.Count;
            var limit = body.Limit > 0 ? body.Limit : 50;
            var offset = body.Offset;

            // Apply pagination to version numbers first
            var pagedVersionNumbers = sortedVersionNumbers
                .Skip(offset)
                .Take(limit)
                .ToList();

            // Bulk load version records for paginated set
            var versionKeys = pagedVersionNumbers.Select(v => $"bundle-version:{body.BundleId}:{v}");
            var versionDict = await versionStore.GetBulkAsync(versionKeys, cancellationToken);

            // Build result list, preserving order
            var pagedVersions = new List<BundleVersionRecord>();
            for (var i = 0; i < pagedVersionNumbers.Count; i++)
            {
                var versionNum = pagedVersionNumbers[i];
                var versionKey = $"bundle-version:{body.BundleId}:{versionNum}";
                if (versionDict.TryGetValue(versionKey, out var versionRecord) && versionRecord != null)
                {
                    // Include snapshot only for first item when viewing from start
                    pagedVersions.Add(versionRecord.ToApiModel(includeSnapshot: i == 0 && offset == 0));
                }
            }

            _logger.LogInformation("ListBundleVersions: Found {TotalCount} versions for bundle {BundleId}",
                totalCount, body.BundleId);

            return (StatusCodes.OK, new ListBundleVersionsResponse
            {
                BundleId = body.BundleId,
                CurrentVersion = bundle.MetadataVersion,
                Versions = pagedVersions,
                TotalCount = totalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListBundleVersions: Unexpected error for bundle {BundleId}", body.BundleId);
            await _messageBus.TryPublishErrorAsync(
                "asset",
                "ListBundleVersions",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/bundles/list-versions",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Helper to remove bundle from asset-bundle reverse index.
    /// </summary>
    private async Task RemoveFromBundleIndexAsync(string bundleId, List<string> assetIds, CancellationToken cancellationToken)
    {
        var indexStore = _stateStoreFactory.GetStore<AssetBundleIndex>(_configuration.StatestoreName);

        foreach (var assetId in assetIds)
        {
            var indexKey = $"asset-bundle:{assetId}";
            var index = await indexStore.GetAsync(indexKey, cancellationToken);

            var bundleGuid = Guid.Parse(bundleId);
            if (index != null && index.BundleIds.Contains(bundleGuid))
            {
                index.BundleIds.Remove(bundleGuid);

                if (index.BundleIds.Count == 0)
                {
                    await indexStore.DeleteAsync(indexKey, cancellationToken);
                }
                else
                {
                    await indexStore.SaveAsync(indexKey, index, cancellationToken: cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// Helper to map Realm enum to event RealmEnum.
    /// </summary>
    private static BeyondImmersion.BannouService.Events.RealmEnum? MapRealmToEventEnum(Realm realm)
    {
        return realm switch
        {
            Realm.Omega => BeyondImmersion.BannouService.Events.RealmEnum.Omega,
            Realm.Arcadia => BeyondImmersion.BannouService.Events.RealmEnum.Arcadia,
            Realm.Fantasia => BeyondImmersion.BannouService.Events.RealmEnum.Fantasia,
            Realm.Shared => BeyondImmersion.BannouService.Events.RealmEnum.Shared,
            _ => null
        };
    }

    #endregion

    #region Async Metabundle Helpers

    /// <summary>
    /// Determines if a metabundle creation job should be processed asynchronously
    /// based on configured thresholds.
    /// </summary>
    private bool ShouldProcessMetabundleAsync(int sourceBundleCount, int totalAssetCount, long totalSizeBytes)
    {
        // Check source bundle threshold
        if (sourceBundleCount >= _configuration.MetabundleAsyncSourceBundleThreshold)
        {
            _logger.LogInformation(
                "CreateMetabundle: Job exceeds source bundle threshold ({Count} >= {Threshold}), using async processing",
                sourceBundleCount, _configuration.MetabundleAsyncSourceBundleThreshold);
            return true;
        }

        // Check asset count threshold
        if (totalAssetCount >= _configuration.MetabundleAsyncAssetCountThreshold)
        {
            _logger.LogInformation(
                "CreateMetabundle: Job exceeds asset count threshold ({Count} >= {Threshold}), using async processing",
                totalAssetCount, _configuration.MetabundleAsyncAssetCountThreshold);
            return true;
        }

        // Check size threshold
        if (totalSizeBytes >= _configuration.MetabundleAsyncSizeBytesThreshold)
        {
            _logger.LogInformation(
                "CreateMetabundle: Job exceeds size threshold ({Size} >= {Threshold}), using async processing",
                totalSizeBytes, _configuration.MetabundleAsyncSizeBytesThreshold);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Creates an async metabundle job and returns a queued response.
    /// </summary>
    private async Task<(StatusCodes, CreateMetabundleResponse?)> CreateMetabundleJobAsync(
        CreateMetabundleRequest request,
        List<BundleMetadata> sourceBundles,
        List<InternalAssetRecord> standaloneAssets,
        List<(StoredBundleAssetEntry Entry, string SourceBundleId)> assetsToInclude,
        List<InternalAssetRecord> standalonesToInclude,
        int totalAssetCount,
        long totalSizeBytes,
        CancellationToken cancellationToken)
    {
        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Capture requester session ID for completion notification
        var requesterSessionId = ServiceRequestContext.SessionId;

        // Create job record
        var job = new MetabundleJob
        {
            JobId = jobId,
            MetabundleId = request.MetabundleId,
            Status = InternalJobStatus.Queued,
            Request = request,
            RequesterSessionId = requesterSessionId,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Save job to state store
        var jobStore = _stateStoreFactory.GetStore<MetabundleJob>(_configuration.StatestoreName);
        var jobKey = $"{_configuration.MetabundleJobKeyPrefix}{jobId}";
        await jobStore.SaveAsync(jobKey, job, cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "CreateMetabundle: Created async job {JobId} for metabundle {MetabundleId} with {AssetCount} assets",
            jobId, request.MetabundleId, totalAssetCount);

        // Publish job to processing queue
        await _messageBus.TryPublishAsync(
            "asset.metabundle.job.queued",
            new MetabundleJobQueuedEvent
            {
                JobId = jobId,
                MetabundleId = request.MetabundleId,
                SourceBundleCount = sourceBundles.Count,
                AssetCount = totalAssetCount,
                EstimatedSizeBytes = totalSizeBytes,
                RequesterSessionId = requesterSessionId
            }).ConfigureAwait(false);

        // Build provenance data for response
        var sourceBundleRefs = sourceBundles.Select(sb => new SourceBundleReference
        {
            BundleId = sb.BundleId,
            Version = sb.Version,
            AssetIds = assetsToInclude
                .Where(a => a.SourceBundleId == sb.BundleId)
                .Select(a => a.Entry.AssetId)
                .ToList(),
            ContentHash = sb.StorageKey // Use storage key as proxy for content hash
        }).ToList();

        return (StatusCodes.OK, new CreateMetabundleResponse
        {
            MetabundleId = request.MetabundleId,
            JobId = jobId,
            Status = CreateMetabundleResponseStatus.Queued,
            AssetCount = totalAssetCount,
            StandaloneAssetCount = standalonesToInclude.Count,
            SizeBytes = totalSizeBytes,
            SourceBundles = sourceBundleRefs
        });
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Overrides the default IBannouService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Asset service permissions...");
        await AssetPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
    }

    #endregion

}

/// <summary>
/// Event published when an asset processing job is assigned to a processor.
/// </summary>
public sealed class AssetProcessingJobEvent
{
    public Guid JobId { get; set; } = Guid.NewGuid();
    public Guid AssetId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Filename { get; set; } = string.Empty;
    /// <summary>
    /// Owner of this processing job. NOT a session ID.
    /// Contains either an accountId or service name.
    /// </summary>
    public string Owner { get; set; } = string.Empty;
    public Guid? RealmId { get; set; }
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
    public Guid AssetId { get; set; }
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
    public Guid BundleId { get; set; }
    public BundleFormat Format { get; set; }
    public string Path { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Index entry for asset-to-bundle reverse lookup.
/// </summary>
internal sealed class AssetBundleIndex
{
    /// <summary>
    /// List of bundle IDs containing this asset.
    /// </summary>
    public List<Guid> BundleIds { get; set; } = new();
}

/// <summary>
/// Internal model for tracking async metabundle creation jobs.
/// Stored in state store for status polling and completion handling.
/// </summary>
internal sealed class MetabundleJob
{
    /// <summary>
    /// Unique job identifier.
    /// </summary>
    public Guid JobId { get; set; }

    /// <summary>
    /// Target metabundle identifier.
    /// </summary>
    public Guid MetabundleId { get; set; }

    /// <summary>
    /// Current job status.
    /// </summary>
    public InternalJobStatus Status { get; set; } = InternalJobStatus.Queued;

    /// <summary>
    /// Progress percentage (0-100) when processing.
    /// </summary>
    public int? Progress { get; set; }

    /// <summary>
    /// Session ID of the requester for completion notification.
    /// </summary>
    public Guid? RequesterSessionId { get; set; }

    /// <summary>
    /// Serialized request for background processing.
    /// Always set when job is created; null indicates data corruption.
    /// </summary>
    public CreateMetabundleRequest? Request { get; set; }

    /// <summary>
    /// When the job was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the job was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When the job completed (success or failure).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Processing time in milliseconds after completion.
    /// </summary>
    public long? ProcessingTimeMs { get; set; }

    /// <summary>
    /// Error code if job failed.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Error message if job failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Result data when job completes successfully.
    /// </summary>
    public MetabundleJobResult? Result { get; set; }
}

/// <summary>
/// Job status enum for internal tracking (distinct from client event enum).
/// </summary>
internal enum InternalJobStatus
{
    Queued,
    Processing,
    Ready,
    Failed,
    Cancelled
}

/// <summary>
/// Result data stored when metabundle job completes successfully.
/// </summary>
internal sealed class MetabundleJobResult
{
    public int AssetCount { get; set; }
    public int? StandaloneAssetCount { get; set; }
    public long SizeBytes { get; set; }
    public string? StorageKey { get; set; }
    public List<SourceBundleReferenceInternal>? SourceBundles { get; set; }
}

/// <summary>
/// Internal model for source bundle reference in job results.
/// </summary>
internal sealed class SourceBundleReferenceInternal
{
    public Guid BundleId { get; set; }
    public string Version { get; set; } = string.Empty;
    public List<Guid> AssetIds { get; set; } = new();
    public string ContentHash { get; set; } = string.Empty;
}

/// <summary>
/// Event published when a metabundle job is queued for async processing.
/// Consumed by background workers to process the metabundle creation.
/// </summary>
internal sealed class MetabundleJobQueuedEvent
{
    /// <summary>
    /// Unique identifier for this job.
    /// </summary>
    public Guid JobId { get; set; }

    /// <summary>
    /// The metabundle ID being created.
    /// </summary>
    public Guid MetabundleId { get; set; }

    /// <summary>
    /// Number of source bundles to merge.
    /// </summary>
    public int SourceBundleCount { get; set; }

    /// <summary>
    /// Total number of assets in the metabundle.
    /// </summary>
    public int AssetCount { get; set; }

    /// <summary>
    /// Estimated total size in bytes.
    /// </summary>
    public long EstimatedSizeBytes { get; set; }

    /// <summary>
    /// Session ID of the requester for completion notification.
    /// Null if request did not originate from a WebSocket session.
    /// </summary>
    public string? RequesterSessionId { get; set; }
}
