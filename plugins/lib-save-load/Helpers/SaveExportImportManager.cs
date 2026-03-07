using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.SaveLoad.Compression;
using BeyondImmersion.BannouService.SaveLoad.Models;
using BeyondImmersion.BannouService.SaveLoad.Processing;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using System.IO.Compression;

namespace BeyondImmersion.BannouService.SaveLoad.Helpers;

/// <summary>
/// Implementation of save data export and import operations.
/// Handles archive creation, download, and conflict resolution.
/// </summary>
public sealed class SaveExportImportManager : ISaveExportImportManager
{
    /// <summary>Queryable store for save slot metadata (MySQL-backed for LINQ queries).</summary>
    private readonly IQueryableStateStore<SaveSlotMetadata> _slotQueryStore;
    /// <summary>Store for save slot metadata (basic CRUD operations).</summary>
    private readonly IStateStore<SaveSlotMetadata> _slotStore;
    /// <summary>Store for version manifests (basic CRUD operations).</summary>
    private readonly IStateStore<SaveVersionManifest> _versionStore;
    /// <summary>Queryable store for version manifests (MySQL-backed for LINQ queries).</summary>
    private readonly IQueryableStateStore<SaveVersionManifest> _versionQueryStore;
    /// <summary>Hot cache store for fast save data retrieval (Redis-backed with TTL).</summary>
    private readonly IStateStore<HotSaveEntry> _hotCacheStore;
    /// <summary>Cacheable store for pending upload entries (Redis-backed with set operations).</summary>
    private readonly ICacheableStateStore<PendingUploadEntry> _pendingStore;
    private readonly SaveLoadServiceConfiguration _configuration;
    private readonly IAssetClient _assetClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IVersionDataLoader _versionDataLoader;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<SaveExportImportManager> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SaveExportImportManager"/> class.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for save data access.</param>
    /// <param name="configuration">Save-load service configuration.</param>
    /// <param name="assetClient">Asset client for archive storage.</param>
    /// <param name="httpClientFactory">HTTP client factory for presigned URL operations.</param>
    /// <param name="versionDataLoader">Version data loader for save data retrieval.</param>
    /// <param name="messageBus">Message bus for error event publishing.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public SaveExportImportManager(
        IStateStoreFactory stateStoreFactory,
        SaveLoadServiceConfiguration configuration,
        IAssetClient assetClient,
        IHttpClientFactory httpClientFactory,
        IVersionDataLoader versionDataLoader,
        IMessageBus messageBus,
        ILogger<SaveExportImportManager> logger,
        ITelemetryProvider telemetryProvider)
    {
        _slotQueryStore = stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        _slotStore = stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        _versionStore = stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
        _versionQueryStore = stateStoreFactory.GetQueryableStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
        _hotCacheStore = stateStoreFactory.GetStore<HotSaveEntry>(StateStoreDefinitions.SaveLoadCache);
        _pendingStore = stateStoreFactory.GetCacheableStore<PendingUploadEntry>(StateStoreDefinitions.SaveLoadPending);
        _configuration = configuration;
        _assetClient = assetClient;
        _httpClientFactory = httpClientFactory;
        _versionDataLoader = versionDataLoader;
        _messageBus = messageBus;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ExportSavesResponse?)> ExportSavesAsync(
        ExportSavesRequest body,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "SaveExportImportManager.ExportSavesAsync");
        _logger.LogDebug(
            "Exporting saves for owner {OwnerId} ({OwnerType}) game {GameId}",
            body.OwnerId, body.OwnerType, body.GameId);

        var ownerIdStr = body.OwnerId.ToString();
        var ownerTypeStr = body.OwnerType.ToString().ToLowerInvariant();

        // Query slots for this owner
        var slots = await _slotQueryStore.QueryAsync(
            s => s.GameId == body.GameId && s.OwnerId == body.OwnerId && s.OwnerType == body.OwnerType,
            cancellationToken);

        // Filter by specific slot names if provided
        if (body.SlotNames != null && body.SlotNames.Count > 0)
        {
            var slotNameSet = new HashSet<string>(body.SlotNames);
            slots = slots.Where(s => slotNameSet.Contains(s.SlotName)).ToList();
        }

        if (!slots.Any())
        {
            _logger.LogWarning("No slots found to export");
            return (StatusCodes.NotFound, null);
        }

        // Create ZIP archive in memory
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            var manifest = new ExportManifest
            {
                GameId = body.GameId,
                OwnerId = body.OwnerId,
                OwnerType = body.OwnerType,
                ExportedAt = DateTimeOffset.UtcNow,
                Slots = new List<ExportSlotEntry>()
            };

            foreach (var slot in slots)
            {
                // Get the latest version
                if (!slot.LatestVersion.HasValue)
                {
                    continue;
                }

                var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), slot.LatestVersion.Value);
                var version = await _versionStore.GetAsync(versionKey, cancellationToken);
                if (version == null)
                {
                    continue;
                }

                var data = await _versionDataLoader.LoadVersionDataAsync(slot.SlotId.ToString(), version, cancellationToken);
                if (data == null)
                {
                    _logger.LogWarning("Failed to load data for slot {SlotName}", slot.SlotName);
                    continue;
                }

                // Add data file to archive
                var dataEntry = archive.CreateEntry($"slots/{slot.SlotName}/data.bin");
                using (var entryStream = dataEntry.Open())
                {
                    await entryStream.WriteAsync(data, cancellationToken);
                }

                manifest.Slots.Add(new ExportSlotEntry
                {
                    SlotId = slot.SlotId,
                    SlotName = slot.SlotName,
                    Category = slot.Category,
                    DisplayName = slot.SlotName,
                    VersionNumber = version.VersionNumber,
                    SchemaVersion = version.SchemaVersion,
                    ContentHash = version.ContentHash,
                    SizeBytes = data.Length,
                    CreatedAt = version.CreatedAt
                });
            }

            // Add manifest to archive
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var entryStream = manifestEntry.Open())
            {
                var manifestJson = BannouJson.Serialize(manifest);
                var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson);
                await entryStream.WriteAsync(manifestBytes, cancellationToken);
            }
        }

        memoryStream.Position = 0;
        var archiveData = memoryStream.ToArray();

        // Upload archive to asset service
        var uploadRequest = new UploadRequest
        {
            OwnerType = AssetOwnerType.Service,
            OwnerId = "save-load",
            Filename = $"export_{body.GameId}_{body.OwnerId}_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.zip",
            ContentType = "application/zip",
            Size = archiveData.Length,
            Metadata = new AssetMetadataInput { AssetType = AssetType.Other }
        };

        var uploadResponse = await _assetClient.RequestUploadAsync(uploadRequest, cancellationToken);
        if (uploadResponse?.UploadUrl == null)
        {
            _logger.LogError("Failed to request upload URL for export archive");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "ExportSaves",
                "AssetServiceFailure",
                "Failed to request upload URL for export archive");
            return (StatusCodes.InternalServerError, null);
        }

        // Upload to presigned URL
        using var httpClient = _httpClientFactory.CreateClient();
        using var uploadContent = new ByteArrayContent(archiveData);
        uploadContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        var uploadResult = await httpClient.PutAsync(uploadResponse.UploadUrl, uploadContent, cancellationToken);

        if (!uploadResult.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to upload export archive: {Status}", uploadResult.StatusCode);
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "ExportSaves",
                "UploadFailure",
                $"Failed to upload export archive: {uploadResult.StatusCode}");
            return (StatusCodes.InternalServerError, null);
        }

        // Complete upload
        var completeRequest = new CompleteUploadRequest
        {
            UploadId = uploadResponse.UploadId
        };
        var assetMetadata = await _assetClient.CompleteUploadAsync(completeRequest, cancellationToken);

        if (assetMetadata == null)
        {
            _logger.LogError("Failed to complete export archive upload");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "ExportSaves",
                "AssetServiceFailure",
                "Failed to complete export archive upload");
            return (StatusCodes.InternalServerError, null);
        }

        // Get download URL
        var getAssetResponse = await _assetClient.GetAssetAsync(
            new GetAssetRequest { AssetId = assetMetadata.AssetId },
            cancellationToken);

        if (getAssetResponse?.DownloadUrl == null)
        {
            _logger.LogError("Failed to get download URL for export");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "ExportSaves",
                "AssetServiceFailure",
                "Failed to get download URL for export");
            return (StatusCodes.InternalServerError, null);
        }

        _logger.LogInformation(
            "Exported {SlotCount} slots for owner {OwnerId}, archive size {Size} bytes",
            slots.Count(), body.OwnerId, archiveData.Length);

        return (StatusCodes.OK, new ExportSavesResponse
        {
            DownloadUrl = getAssetResponse.DownloadUrl.ToString(),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            SizeBytes = archiveData.Length
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ImportSavesResponse?)> ImportSavesAsync(
        ImportSavesRequest body,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "SaveExportImportManager.ImportSavesAsync");
        _logger.LogDebug(
            "Importing saves from archive {AssetId} for owner {OwnerId} ({OwnerType})",
            body.ArchiveAssetId, body.TargetOwnerId, body.TargetOwnerType);

        // Download the archive
        var assetResponse = await _assetClient.GetAssetAsync(
            new GetAssetRequest { AssetId = body.ArchiveAssetId.ToString() },
            cancellationToken);

        if (assetResponse?.DownloadUrl == null)
        {
            _logger.LogError("Failed to get download URL for archive asset");
            return (StatusCodes.NotFound, null);
        }

        using var httpClient = _httpClientFactory.CreateClient();
        var archiveData = await httpClient.GetByteArrayAsync(assetResponse.DownloadUrl, cancellationToken);

        // Parse the archive
        ExportManifest? manifest = null;
        var slotDataMap = new Dictionary<string, byte[]>();

        using (var memoryStream = new MemoryStream(archiveData))
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read))
        {
            // Read manifest
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry == null)
            {
                _logger.LogError("Archive missing manifest.json");
                return (StatusCodes.BadRequest, null);
            }

            using (var manifestStream = manifestEntry.Open())
            using (var reader = new StreamReader(manifestStream))
            {
                var manifestJson = await reader.ReadToEndAsync(cancellationToken);
                manifest = BannouJson.Deserialize<ExportManifest>(manifestJson);
            }

            if (manifest == null || manifest.Slots == null)
            {
                _logger.LogError("Invalid manifest in archive");
                return (StatusCodes.BadRequest, null);
            }

            // Read slot data
            foreach (var slotEntry in manifest.Slots)
            {
                var dataEntryPath = $"slots/{slotEntry.SlotName}/data.bin";
                var dataEntry = archive.GetEntry(dataEntryPath);
                if (dataEntry == null)
                {
                    _logger.LogWarning("Missing data file for slot {SlotName}", slotEntry.SlotName);
                    continue;
                }

                using var dataStream = dataEntry.Open();
                using var dataMemory = new MemoryStream();
                await dataStream.CopyToAsync(dataMemory, cancellationToken);
                slotDataMap[slotEntry.SlotName] = dataMemory.ToArray();
            }
        }

        var targetOwnerIdStr = body.TargetOwnerId.ToString();
        var targetOwnerTypeStr = body.TargetOwnerType.ToString().ToLowerInvariant();
        var importedSlots = 0;
        var importedVersions = 0;
        var skippedSlots = 0;
        var conflicts = new List<string>();

        foreach (var slotEntry in manifest.Slots)
        {
            if (!slotDataMap.TryGetValue(slotEntry.SlotName, out var data))
            {
                continue;
            }

            // Check for existing slot
            // SaveSlotMetadata.GetStateKey expects strings for ownerType and ownerId
            var slotKey = SaveSlotMetadata.GetStateKey(body.TargetGameId, targetOwnerTypeStr, targetOwnerIdStr, slotEntry.SlotName);
            var existingSlot = await _slotStore.GetAsync(slotKey, cancellationToken);

            if (existingSlot != null)
            {
                switch (body.ConflictResolution)
                {
                    case ConflictResolution.Skip:
                        conflicts.Add(slotEntry.SlotName);
                        skippedSlots++;
                        continue;

                    case ConflictResolution.Overwrite:
                        // Delete existing slot and versions
                        var existingVersions = await _versionQueryStore
                            .QueryAsync(v => v.SlotId == existingSlot.SlotId, cancellationToken);
                        foreach (var existingVersion in existingVersions)
                        {
                            await _versionStore.DeleteAsync(existingVersion.GetStateKey(), cancellationToken);
                        }
                        await _slotStore.DeleteAsync(existingSlot.GetStateKey(), cancellationToken);
                        break;

                    case ConflictResolution.Rename:
                        var counter = 1;
                        var baseName = slotEntry.SlotName;
                        while (existingSlot != null)
                        {
                            slotEntry.SlotName = $"{baseName}_{counter}";
                            slotKey = SaveSlotMetadata.GetStateKey(body.TargetGameId, targetOwnerTypeStr, targetOwnerIdStr, slotEntry.SlotName);
                            existingSlot = await _slotStore.GetAsync(slotKey, cancellationToken);
                            counter++;
                        }
                        break;
                }
            }

            // Create slot - SaveSlotMetadata fields are now proper types
            var newSlot = new SaveSlotMetadata
            {
                SlotId = Guid.NewGuid(),
                GameId = body.TargetGameId,
                OwnerId = body.TargetOwnerId,
                OwnerType = body.TargetOwnerType,
                SlotName = slotEntry.SlotName,
                Category = slotEntry.Category ?? SaveCategory.ManualSave,
                MaxVersions = _configuration.DefaultMaxVersionsManualSave,
                LatestVersion = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _slotStore.SaveAsync(newSlot.GetStateKey(), newSlot, cancellationToken: cancellationToken);
            importedSlots++;

            // Create version
            var contentHash = Hashing.ContentHasher.ComputeHash(data);
            // Configuration already provides typed enum (T25 compliant)
            var compressionTypeEnum = _configuration.DefaultCompressionType;
            var importCompressionLevel = compressionTypeEnum == CompressionType.Brotli
                ? _configuration.BrotliCompressionLevel
                : compressionTypeEnum == CompressionType.Gzip
                    ? _configuration.GzipCompressionLevel
                    : (int?)null;
            var compressedData = CompressionHelper.Compress(data, compressionTypeEnum, importCompressionLevel);

            // SaveVersionManifest fields are now proper types
            var newVersion = new SaveVersionManifest
            {
                SlotId = newSlot.SlotId,
                VersionNumber = 1,
                ContentHash = contentHash,
                SizeBytes = data.Length,
                CompressedSizeBytes = compressedData.Length,
                CompressionType = compressionTypeEnum,
                SchemaVersion = slotEntry.SchemaVersion,
                CheckpointName = slotEntry.DisplayName,
                IsPinned = false,
                IsDelta = false,
                UploadStatus = _configuration.AsyncUploadEnabled ? UploadStatus.Pending : UploadStatus.Complete,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _versionStore.SaveAsync(newVersion.GetStateKey(), newVersion, cancellationToken: cancellationToken);
            importedVersions++;

            // Store in hot cache - HotSaveEntry fields are now proper types
            // Use constructor-cached hot cache store per FOUNDATION TENETS
            var hotEntry = new HotSaveEntry
            {
                SlotId = newSlot.SlotId,
                VersionNumber = 1,
                Data = Convert.ToBase64String(compressedData),
                ContentHash = contentHash,
                IsCompressed = compressionTypeEnum != CompressionType.None,
                CompressionType = compressionTypeEnum,
                SizeBytes = data.Length,
                CachedAt = DateTimeOffset.UtcNow
            };
            var hotCacheTtlSeconds = (int)TimeSpan.FromMinutes(_configuration.HotCacheTtlMinutes).TotalSeconds;
            await _hotCacheStore.SaveAsync(
                hotEntry.GetStateKey(),
                hotEntry,
                new StateOptions { Ttl = hotCacheTtlSeconds },
                cancellationToken);

            // Queue for upload if enabled - PendingUploadEntry fields are now proper types
            if (_configuration.AsyncUploadEnabled)
            {
                // Use constructor-cached pending store per FOUNDATION TENETS
                var uploadId = Guid.NewGuid();
                var pendingEntry = new PendingUploadEntry
                {
                    UploadId = uploadId,
                    SlotId = newSlot.SlotId,
                    VersionNumber = 1,
                    GameId = body.TargetGameId,
                    OwnerId = body.TargetOwnerId,
                    OwnerType = body.TargetOwnerType,
                    Data = Convert.ToBase64String(compressedData),
                    ContentHash = contentHash,
                    CompressionType = compressionTypeEnum,
                    CompressedSizeBytes = compressedData.Length,
                    Priority = 1,
                    QueuedAt = DateTimeOffset.UtcNow
                };
                var pendingTtlSeconds = (int)TimeSpan.FromMinutes(_configuration.PendingUploadTtlMinutes).TotalSeconds;
                await _pendingStore.SaveAsync(
                    pendingEntry.GetStateKey(),
                    pendingEntry,
                    new StateOptions { Ttl = pendingTtlSeconds },
                    cancellationToken);
                // Add to tracking set for Redis-based queue processing
                await _pendingStore.AddToSetAsync(SaveUploadWorker.PendingUploadIdsSetKey, uploadId.ToString(), cancellationToken: cancellationToken);
            }
        }

        _logger.LogInformation(
            "Imported {Slots} slots, {Versions} versions, skipped {Skipped} due to conflicts",
            importedSlots, importedVersions, skippedSlots);

        return (StatusCodes.OK, new ImportSavesResponse
        {
            ImportedSlots = importedSlots,
            ImportedVersions = importedVersions,
            SkippedSlots = skippedSlots,
            Conflicts = conflicts
        });
    }
}
