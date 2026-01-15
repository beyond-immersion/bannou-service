using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Messaging.Services;
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
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly SaveLoadServiceConfiguration _configuration;
    private readonly IAssetClient _assetClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IVersionDataLoader _versionDataLoader;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<SaveExportImportManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SaveExportImportManager"/> class.
    /// </summary>
    public SaveExportImportManager(
        IStateStoreFactory stateStoreFactory,
        SaveLoadServiceConfiguration configuration,
        IAssetClient assetClient,
        IHttpClientFactory httpClientFactory,
        IVersionDataLoader versionDataLoader,
        IMessageBus messageBus,
        ILogger<SaveExportImportManager> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _configuration = configuration;
        _assetClient = assetClient;
        _httpClientFactory = httpClientFactory;
        _versionDataLoader = versionDataLoader;
        _messageBus = messageBus;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ExportSavesResponse?)> ExportSavesAsync(
        ExportSavesRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Exporting saves for owner {OwnerId} ({OwnerType}) game {GameId}",
            body.OwnerId, body.OwnerType, body.GameId);

        try
        {
            var ownerIdStr = body.OwnerId.ToString();
            var ownerTypeStr = body.OwnerType.ToString();

            // Query slots for this owner
            var slotQueryStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);
            var slots = await slotQueryStore.QueryAsync(
                s => s.GameId == body.GameId && s.OwnerId == ownerIdStr && s.OwnerType == ownerTypeStr,
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
                var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);
                var manifest = new ExportManifest
                {
                    GameId = body.GameId,
                    OwnerId = ownerIdStr,
                    OwnerType = ownerTypeStr,
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

                    var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, slot.LatestVersion.Value);
                    var version = await versionStore.GetAsync(versionKey, cancellationToken);
                    if (version == null)
                    {
                        continue;
                    }

                    // Load the data
                    var data = await _versionDataLoader.LoadVersionDataAsync(slot.SlotId, version, cancellationToken);
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
                Owner = "save-load",
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
            var uploadContent = new ByteArrayContent(archiveData);
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
                DownloadUrl = getAssetResponse.DownloadUrl,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                SizeBytes = archiveData.Length
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ExportSaves operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "ExportSaves",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/export",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ImportSavesResponse?)> ImportSavesAsync(
        ImportSavesRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Importing saves from archive {AssetId} for owner {OwnerId} ({OwnerType})",
            body.ArchiveAssetId, body.TargetOwnerId, body.TargetOwnerType);

        try
        {
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
            var targetOwnerTypeStr = body.TargetOwnerType.ToString();
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);

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
                var slotKey = SaveSlotMetadata.GetStateKey(body.TargetGameId, targetOwnerTypeStr, targetOwnerIdStr, slotEntry.SlotName);
                var existingSlot = await slotStore.GetAsync(slotKey, cancellationToken);

                if (existingSlot != null)
                {
                    switch (body.ConflictResolution)
                    {
                        case ConflictResolution.SKIP:
                            conflicts.Add(slotEntry.SlotName);
                            skippedSlots++;
                            continue;

                        case ConflictResolution.OVERWRITE:
                            // Delete existing slot and versions
                            var existingVersions = await _stateStoreFactory.GetQueryableStore<SaveVersionManifest>(_configuration.VersionManifestStoreName)
                                .QueryAsync(v => v.SlotId == existingSlot.SlotId, cancellationToken);
                            foreach (var existingVersion in existingVersions)
                            {
                                await versionStore.DeleteAsync(existingVersion.GetStateKey(), cancellationToken);
                            }
                            await slotStore.DeleteAsync(existingSlot.GetStateKey(), cancellationToken);
                            break;

                        case ConflictResolution.RENAME:
                            var counter = 1;
                            var baseName = slotEntry.SlotName;
                            while (existingSlot != null)
                            {
                                slotEntry.SlotName = $"{baseName}_{counter}";
                                slotKey = SaveSlotMetadata.GetStateKey(body.TargetGameId, targetOwnerTypeStr, targetOwnerIdStr, slotEntry.SlotName);
                                existingSlot = await slotStore.GetAsync(slotKey, cancellationToken);
                                counter++;
                            }
                            break;
                    }
                }

                // Create slot
                var newSlot = new SaveSlotMetadata
                {
                    SlotId = Guid.NewGuid().ToString(),
                    GameId = body.TargetGameId,
                    OwnerId = targetOwnerIdStr,
                    OwnerType = targetOwnerTypeStr,
                    SlotName = slotEntry.SlotName,
                    Category = slotEntry.Category ?? "manual",
                    MaxVersions = _configuration.DefaultMaxVersionsManualSave,
                    LatestVersion = 1,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await slotStore.SaveAsync(newSlot.GetStateKey(), newSlot, cancellationToken: cancellationToken);
                importedSlots++;

                // Create version
                var contentHash = Hashing.ContentHasher.ComputeHash(data);
                var compressionTypeEnum = Enum.TryParse<CompressionType>(_configuration.DefaultCompressionType, out var ct) ? ct : CompressionType.GZIP;
                var compressedData = CompressionHelper.Compress(data, compressionTypeEnum);

                var newVersion = new SaveVersionManifest
                {
                    SlotId = newSlot.SlotId,
                    VersionNumber = 1,
                    ContentHash = contentHash,
                    SizeBytes = data.Length,
                    CompressedSizeBytes = compressedData.Length,
                    CompressionType = compressionTypeEnum.ToString(),
                    SchemaVersion = slotEntry.SchemaVersion,
                    CheckpointName = slotEntry.DisplayName,
                    IsPinned = false,
                    IsDelta = false,
                    UploadStatus = _configuration.AsyncUploadEnabled ? "PENDING" : "COMPLETE",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await versionStore.SaveAsync(newVersion.GetStateKey(), newVersion, cancellationToken: cancellationToken);
                importedVersions++;

                // Store in hot cache
                var hotCacheStore = _stateStoreFactory.GetStore<HotSaveEntry>(_configuration.HotCacheStoreName);
                var hotEntry = new HotSaveEntry
                {
                    SlotId = newSlot.SlotId,
                    VersionNumber = 1,
                    Data = Convert.ToBase64String(compressedData),
                    ContentHash = contentHash,
                    IsCompressed = compressionTypeEnum != CompressionType.NONE,
                    CompressionType = compressionTypeEnum.ToString(),
                    SizeBytes = data.Length,
                    CachedAt = DateTimeOffset.UtcNow
                };
                var hotCacheTtlSeconds = (int)TimeSpan.FromMinutes(_configuration.HotCacheTtlMinutes).TotalSeconds;
                await hotCacheStore.SaveAsync(
                    hotEntry.GetStateKey(),
                    hotEntry,
                    new StateOptions { Ttl = hotCacheTtlSeconds },
                    cancellationToken);

                // Queue for upload if enabled
                if (_configuration.AsyncUploadEnabled)
                {
                    var pendingStore = _stateStoreFactory.GetStore<PendingUploadEntry>(_configuration.PendingUploadStoreName);
                    var uploadId = Guid.NewGuid().ToString();
                    var pendingEntry = new PendingUploadEntry
                    {
                        UploadId = uploadId,
                        SlotId = newSlot.SlotId,
                        VersionNumber = 1,
                        GameId = body.TargetGameId,
                        OwnerId = targetOwnerIdStr,
                        OwnerType = targetOwnerTypeStr,
                        Data = Convert.ToBase64String(compressedData),
                        ContentHash = contentHash,
                        CompressedSizeBytes = compressedData.Length,
                        Priority = 1,
                        QueuedAt = DateTimeOffset.UtcNow
                    };
                    var pendingTtlSeconds = (int)TimeSpan.FromMinutes(_configuration.PendingUploadTtlMinutes).TotalSeconds;
                    await pendingStore.SaveAsync(
                        pendingEntry.GetStateKey(),
                        pendingEntry,
                        new StateOptions { Ttl = pendingTtlSeconds },
                        cancellationToken);
                    // Add to tracking set for Redis-based queue processing
                    await pendingStore.AddToSetAsync(SaveUploadWorker.PendingUploadIdsSetKey, uploadId, cancellationToken: cancellationToken);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ImportSaves operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "ImportSaves",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/import",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }
}
