using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.SaveLoad.Delta;
using BeyondImmersion.BannouService.SaveLoad.Helpers;
using BeyondImmersion.BannouService.SaveLoad.Migration;
using BeyondImmersion.BannouService.SaveLoad.Models;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-save-load.tests")]

namespace BeyondImmersion.BannouService.SaveLoad;

/// <summary>
/// Implementation of the SaveLoad service.
/// This class contains the business logic for all SaveLoad operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// Standard structure:
/// <list type="bullet">
///   <item>SaveLoadService.cs (this file) - Business logic</item>
///   <item>SaveLoadServiceEvents.cs - Event consumer handlers (generated)</item>
///   <item>Generated/SaveLoadPermissionRegistration.cs - Permission registration (generated)</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("save-load", typeof(ISaveLoadService), lifetime: ServiceLifetime.Scoped)]
public partial class SaveLoadService : ISaveLoadService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<SaveLoadService> _logger;
    private readonly SaveLoadServiceConfiguration _configuration;
    private readonly IAssetClient _assetClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IVersionDataLoader _versionDataLoader;
    private readonly IVersionCleanupManager _versionCleanupManager;
    private readonly ISaveExportImportManager _saveExportImportManager;
    private readonly ISaveMigrationHandler _saveMigrationHandler;

    public SaveLoadService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<SaveLoadService> logger,
        SaveLoadServiceConfiguration configuration,
        IAssetClient assetClient,
        IHttpClientFactory httpClientFactory,
        IVersionDataLoader versionDataLoader,
        IVersionCleanupManager versionCleanupManager,
        ISaveExportImportManager saveExportImportManager,
        ISaveMigrationHandler saveMigrationHandler)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _assetClient = assetClient;
        _httpClientFactory = httpClientFactory;
        _versionDataLoader = versionDataLoader;
        _versionCleanupManager = versionCleanupManager;
        _saveExportImportManager = saveExportImportManager;
        _saveMigrationHandler = saveMigrationHandler;
    }

    /// <summary>
    /// Creates a new save slot for the specified owner.
    /// </summary>
    public async Task<(StatusCodes, SlotResponse?)> CreateSlotAsync(CreateSlotRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Creating save slot for game {GameId}, owner {OwnerType}:{OwnerId}, slot {SlotName}",
            body.GameId, body.OwnerType, body.OwnerId, body.SlotName);

        try
        {
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var ownerType = body.OwnerType.ToString();
            var ownerId = body.OwnerId.ToString();

            // Check if slot already exists
            var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, ownerType, ownerId, body.SlotName);
            var existingSlot = await slotStore.GetAsync(slotKey, cancellationToken);
            if (existingSlot != null)
            {
                _logger.LogWarning("Slot already exists: {SlotKey}", slotKey);
                return (StatusCodes.Conflict, null);
            }

            // Get category defaults
            var category = body.Category;
            var maxVersions = body.MaxVersions > 0 ? body.MaxVersions : GetDefaultMaxVersions(category);
            var compressionType = GetDefaultCompressionType(category);

            // Create the slot
            var now = DateTimeOffset.UtcNow;
            var slotId = Guid.NewGuid();
            var slot = new SaveSlotMetadata
            {
                SlotId = slotId.ToString(),
                GameId = body.GameId,
                OwnerId = ownerId,
                OwnerType = ownerType,
                SlotName = body.SlotName,
                Category = category.ToString(),
                MaxVersions = (int)maxVersions,
                RetentionDays = body.RetentionDays > 0 ? body.RetentionDays : null,
                CompressionType = compressionType.ToString(),
                VersionCount = 0,
                LatestVersion = null,
                TotalSizeBytes = 0,
                Tags = body.Tags?.ToList() ?? new List<string>(),
                Metadata = body.Metadata?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, string>(),
                CreatedAt = now,
                UpdatedAt = now,
                ETag = Guid.NewGuid().ToString()
            };

            await slotStore.SaveAsync(slotKey, slot, cancellationToken: cancellationToken);

            _logger.LogInformation("Created slot {SlotId} for {OwnerType}:{OwnerId}", slotId, ownerType, ownerId);

            return (StatusCodes.OK, ToSlotResponse(slot));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing CreateSlot operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "CreateSlot",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/slot/create",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets a save slot by its identifiers.
    /// </summary>
    public async Task<(StatusCodes, SlotResponse?)> GetSlotAsync(GetSlotRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);

            var slotKey = SaveSlotMetadata.GetStateKey(
                body.GameId, body.OwnerType.ToString(),
                body.OwnerId.ToString(), body.SlotName);
            var slot = await slotStore.GetAsync(slotKey, cancellationToken);

            if (slot == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, ToSlotResponse(slot));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetSlot operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "GetSlot",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/slot/get",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Lists save slots for an owner with optional filtering.
    /// </summary>
    public async Task<(StatusCodes, ListSlotsResponse?)> ListSlotsAsync(ListSlotsRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var queryableStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var gameId = body.GameId;
            var ownerId = body.OwnerId.ToString();
            var ownerType = body.OwnerType.ToString();
            var category = body.Category.ToString();

            // Query with LINQ expression
            var slots = await queryableStore.QueryAsync(
                s => s.GameId == gameId &&
                    s.OwnerId == ownerId &&
                    s.OwnerType == ownerType &&
                    (string.IsNullOrEmpty(category) || s.Category == category),
                cancellationToken);

            // Sort by UpdatedAt descending (most recent first)
            var sortedSlots = slots.OrderByDescending(s => s.UpdatedAt).ToList();

            var response = new ListSlotsResponse
            {
                Slots = sortedSlots.Select(ToSlotResponse).ToList(),
                TotalCount = sortedSlots.Count
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ListSlots operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "ListSlots",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/slot/list",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Deletes a save slot and all its versions.
    /// </summary>
    public async Task<(StatusCodes, DeleteSlotResponse?)> DeleteSlotAsync(DeleteSlotRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);

            var slotKey = SaveSlotMetadata.GetStateKey(
                body.GameId, body.OwnerType.ToString(),
                body.OwnerId.ToString(), body.SlotName);
            var slot = await slotStore.GetAsync(slotKey, cancellationToken);

            if (slot == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Delete all versions for this slot
            var deletedVersions = 0;
            for (var v = 1; v <= (slot.LatestVersion ?? 0); v++)
            {
                var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, v);
                await versionStore.DeleteAsync(versionKey, cancellationToken);
                deletedVersions++;
            }

            // Delete the slot
            await slotStore.DeleteAsync(slotKey, cancellationToken);

            _logger.LogInformation("Deleted slot {SlotId} with {VersionCount} versions", slot.SlotId, deletedVersions);

            var response = new DeleteSlotResponse
            {
                Deleted = true,
                VersionsDeleted = deletedVersions,
                BytesFreed = slot.TotalSizeBytes
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing DeleteSlot operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "DeleteSlot",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/slot/delete",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Renames a save slot (creates new key, migrates data, deletes old).
    /// </summary>
    public async Task<(StatusCodes, SlotResponse?)> RenameSlotAsync(RenameSlotRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);

            // Get old slot key
            var oldSlotKey = SaveSlotMetadata.GetStateKey(
                body.GameId, body.OwnerType.ToString(),
                body.OwnerId.ToString(), body.SlotName);
            var slot = await slotStore.GetAsync(oldSlotKey, cancellationToken);

            if (slot == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Check if new name already exists
            var newSlotKey = SaveSlotMetadata.GetStateKey(
                body.GameId, body.OwnerType.ToString(),
                body.OwnerId.ToString(), body.NewSlotName);
            var existingNewSlot = await slotStore.GetAsync(newSlotKey, cancellationToken);

            if (existingNewSlot != null)
            {
                _logger.LogWarning("Cannot rename - target slot already exists: {NewSlotName}", body.NewSlotName);
                return (StatusCodes.Conflict, null);
            }

            // Update slot name and save to new key
            slot.SlotName = body.NewSlotName;
            slot.UpdatedAt = DateTimeOffset.UtcNow;
            slot.ETag = Guid.NewGuid().ToString();

            await slotStore.SaveAsync(newSlotKey, slot, cancellationToken: cancellationToken);
            await slotStore.DeleteAsync(oldSlotKey, cancellationToken);

            _logger.LogInformation("Renamed slot from {OldName} to {NewName}", body.SlotName, body.NewSlotName);

            return (StatusCodes.OK, ToSlotResponse(slot));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing RenameSlot operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "RenameSlot",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/slot/rename",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Bulk deletes multiple save slots.
    /// </summary>
    public async Task<(StatusCodes, BulkDeleteSlotsResponse?)> BulkDeleteSlotsAsync(BulkDeleteSlotsRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
            var queryableStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);

            var deletedCount = 0;
            long totalBytesFreed = 0;

            foreach (var slotId in body.SlotIds)
            {
                try
                {
                    // Find slot by ID using queryable store
                    var slotIdStr = slotId.ToString();
                    var slots = await queryableStore.QueryAsync(
                        s => s.SlotId == slotIdStr && s.GameId == body.GameId,
                        cancellationToken);
                    var slot = slots.FirstOrDefault();

                    if (slot == null)
                    {
                        _logger.LogWarning("Slot {SlotId} not found for bulk delete", slotId);
                        continue;
                    }

                    // Delete all versions for this slot
                    for (var v = 1; v <= (slot.LatestVersion ?? 0); v++)
                    {
                        var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, v);
                        await versionStore.DeleteAsync(versionKey, cancellationToken);
                    }

                    // Delete the slot
                    var slotKey = slot.GetStateKey();
                    await slotStore.DeleteAsync(slotKey, cancellationToken);

                    deletedCount++;
                    totalBytesFreed += slot.TotalSizeBytes;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete slot {SlotId}", slotId);
                }
            }

            var response = new BulkDeleteSlotsResponse
            {
                DeletedCount = deletedCount,
                BytesFreed = totalBytesFreed
            };

            _logger.LogInformation(
                "Bulk delete completed: {DeletedCount} slots deleted, {BytesFreed} bytes freed",
                deletedCount, totalBytesFreed);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing BulkDeleteSlots operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "BulkDeleteSlots",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/slot/bulk-delete",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Saves game data to a slot, creating the slot if it doesn't exist.
    /// Uses async upload queue by default for consistent response times.
    /// </summary>
    public async Task<(StatusCodes, SaveResponse?)> SaveAsync(SaveRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Saving to slot {SlotName} for {OwnerType}:{OwnerId} in game {GameId}",
            body.SlotName, body.OwnerType, body.OwnerId, body.GameId);

        try
        {
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
            var hotCacheStore = _stateStoreFactory.GetStore<HotSaveEntry>(StateStoreDefinitions.SaveLoadCache);
            var pendingStore = _stateStoreFactory.GetStore<PendingUploadEntry>(StateStoreDefinitions.SaveLoadPending);

            var ownerType = body.OwnerType.ToString();
            var ownerId = body.OwnerId.ToString();
            var now = DateTimeOffset.UtcNow;

            // Get or create slot
            var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, ownerType, ownerId, body.SlotName);
            var slot = await slotStore.GetAsync(slotKey, cancellationToken);

            if (slot == null)
            {
                // Auto-create slot with defaults
                var category = body.Category ?? SaveCategory.MANUAL_SAVE;
                slot = new SaveSlotMetadata
                {
                    SlotId = Guid.NewGuid().ToString(),
                    GameId = body.GameId,
                    OwnerId = ownerId,
                    OwnerType = ownerType,
                    SlotName = body.SlotName,
                    Category = category.ToString(),
                    MaxVersions = GetDefaultMaxVersions(category),
                    CompressionType = GetDefaultCompressionType(category).ToString(),
                    VersionCount = 0,
                    LatestVersion = null,
                    TotalSizeBytes = 0,
                    Tags = new List<string>(),
                    Metadata = body.Metadata?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, string>(),
                    CreatedAt = now,
                    UpdatedAt = now,
                    ETag = Guid.NewGuid().ToString()
                };
                await slotStore.SaveAsync(slotKey, slot, cancellationToken: cancellationToken);
                _logger.LogInformation("Auto-created slot {SlotId} for {SlotName}", slot.SlotId, body.SlotName);
            }

            // Get raw data
            var rawData = body.Data;
            var originalSize = rawData.Length;

            // Compute hash of original data
            var contentHash = Hashing.ContentHasher.ComputeHash(rawData);

            // Determine compression
            var compressionType = Enum.TryParse<CompressionType>(slot.CompressionType, out var ct) ? ct : CompressionType.GZIP;
            var compressedData = rawData;
            var compressedSize = originalSize;

            if (Compression.CompressionHelper.ShouldCompress(originalSize, _configuration.AutoCompressThresholdBytes))
            {
                compressedData = Compression.CompressionHelper.Compress(rawData, compressionType);
                compressedSize = compressedData.Length;
            }
            else
            {
                compressionType = CompressionType.NONE;
            }

            var compressionRatio = Compression.CompressionHelper.CalculateCompressionRatio(originalSize, compressedSize);

            // Determine next version number
            var nextVersion = (slot.LatestVersion ?? 0) + 1;

            // Create version manifest
            var shouldPin = !string.IsNullOrEmpty(body.PinAsCheckpoint);
            var manifest = new SaveVersionManifest
            {
                SlotId = slot.SlotId,
                VersionNumber = nextVersion,
                ContentHash = contentHash,
                SizeBytes = originalSize,
                CompressedSizeBytes = compressedSize,
                CompressionType = compressionType.ToString(),
                IsPinned = shouldPin,
                CheckpointName = shouldPin ? body.PinAsCheckpoint : null,
                IsDelta = false,
                DeviceId = body.DeviceId,
                SchemaVersion = body.SchemaVersion,
                Metadata = new Dictionary<string, object>(),
                UploadStatus = _configuration.AsyncUploadEnabled ? "PENDING" : "COMPLETE",
                CreatedAt = now,
                ETag = Guid.NewGuid().ToString()
            };

            // Store version manifest
            var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, nextVersion);
            await versionStore.SaveAsync(versionKey, manifest, cancellationToken: cancellationToken);

            // Store in hot cache for immediate load availability
            var hotEntry = new HotSaveEntry
            {
                SlotId = slot.SlotId,
                VersionNumber = nextVersion,
                Data = Convert.ToBase64String(compressedData),
                ContentHash = contentHash,
                IsCompressed = compressionType != CompressionType.NONE,
                CompressionType = compressionType.ToString(),
                SizeBytes = compressedSize,
                CachedAt = now,
                IsDelta = false
            };
            var hotKey = HotSaveEntry.GetStateKey(slot.SlotId, nextVersion);
            await hotCacheStore.SaveAsync(hotKey, hotEntry, cancellationToken: cancellationToken);

            // Also update the "latest" hot cache pointer
            var latestHotKey = HotSaveEntry.GetLatestKey(slot.SlotId);
            await hotCacheStore.SaveAsync(latestHotKey, hotEntry, cancellationToken: cancellationToken);

            var uploadPending = false;

            if (_configuration.AsyncUploadEnabled)
            {
                // Queue for async upload
                var uploadId = Guid.NewGuid().ToString();
                var pendingEntry = new PendingUploadEntry
                {
                    UploadId = uploadId,
                    SlotId = slot.SlotId,
                    VersionNumber = nextVersion,
                    GameId = body.GameId,
                    OwnerId = ownerId,
                    OwnerType = ownerType,
                    Data = Convert.ToBase64String(compressedData),
                    ContentHash = contentHash,
                    CompressionType = compressionType.ToString(),
                    SizeBytes = originalSize,
                    CompressedSizeBytes = compressedSize,
                    IsDelta = false,
                    ThumbnailData = body.Thumbnail != null ? Convert.ToBase64String(body.Thumbnail) : null,
                    AttemptCount = 0,
                    QueuedAt = now,
                    Priority = GetUploadPriority(Enum.TryParse<SaveCategory>(slot.Category, out var cat) ? cat : SaveCategory.MANUAL_SAVE)
                };
                var pendingKey = PendingUploadEntry.GetStateKey(uploadId);
                await pendingStore.SaveAsync(pendingKey, pendingEntry, cancellationToken: cancellationToken);
                // Add to tracking set for Redis-based queue processing
                await pendingStore.AddToSetAsync(Processing.SaveUploadWorker.PendingUploadIdsSetKey, uploadId, cancellationToken: cancellationToken);
                uploadPending = true;

                _logger.LogDebug("Queued async upload {UploadId} for slot {SlotId} version {Version}",
                    uploadId, slot.SlotId, nextVersion);
            }

            // Update slot metadata
            slot.LatestVersion = nextVersion;
            slot.VersionCount++;
            slot.TotalSizeBytes += compressedSize;
            slot.UpdatedAt = now;
            slot.ETag = Guid.NewGuid().ToString();
            await slotStore.SaveAsync(slotKey, slot, cancellationToken: cancellationToken);

            // Rolling cleanup if needed
            var versionsCleanedUp = await _versionCleanupManager.PerformRollingCleanupAsync(slot, versionStore, hotCacheStore, cancellationToken);

            _logger.LogInformation(
                "Saved version {Version} to slot {SlotId}, size {Size} bytes (compressed {CompressedSize}), upload pending: {Pending}",
                nextVersion, slot.SlotId, originalSize, compressedSize, uploadPending);

            var response = new SaveResponse
            {
                SlotId = Guid.Parse(slot.SlotId),
                VersionNumber = nextVersion,
                ContentHash = contentHash,
                SizeBytes = originalSize,
                CompressedSizeBytes = compressedSize,
                CompressionRatio = compressionRatio,
                Pinned = shouldPin,
                CheckpointName = shouldPin ? body.PinAsCheckpoint : null,
                CreatedAt = now,
                VersionsCleanedUp = versionsCleanedUp,
                UploadPending = uploadPending
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Save operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "Save",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/save",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets the upload priority based on save category (lower = higher priority).
    /// </summary>
    private static int GetUploadPriority(SaveCategory category)
    {
        return category switch
        {
            SaveCategory.CHECKPOINT => 0,      // Highest priority
            SaveCategory.MANUAL_SAVE => 1,
            SaveCategory.AUTO_SAVE => 2,
            SaveCategory.QUICK_SAVE => 3,
            SaveCategory.STATE_SNAPSHOT => 4,  // Lowest priority
            _ => 2
        };
    }

    /// <summary>
    /// Loads save data from a slot, checking hot cache first for performance.
    /// </summary>
    public async Task<(StatusCodes, LoadResponse?)> LoadAsync(LoadRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Loading from slot {SlotName} for {OwnerType}:{OwnerId} in game {GameId}",
            body.SlotName, body.OwnerType, body.OwnerId, body.GameId);

        try
        {
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
            var hotCacheStore = _stateStoreFactory.GetStore<HotSaveEntry>(StateStoreDefinitions.SaveLoadCache);

            var ownerType = body.OwnerType.ToString();
            var ownerId = body.OwnerId.ToString();

            // Get the slot
            var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, ownerType, ownerId, body.SlotName);
            var slot = await slotStore.GetAsync(slotKey, cancellationToken);

            if (slot == null)
            {
                _logger.LogWarning("Slot not found: {SlotKey}", slotKey);
                return (StatusCodes.NotFound, null);
            }

            // Determine which version to load
            int targetVersion;

            if (!string.IsNullOrEmpty(body.CheckpointName))
            {
                // Find version by checkpoint name
                targetVersion = await _versionDataLoader.FindVersionByCheckpointAsync(slot, body.CheckpointName, versionStore, cancellationToken);
                if (targetVersion == 0)
                {
                    _logger.LogWarning("Checkpoint {CheckpointName} not found in slot {SlotId}", body.CheckpointName, slot.SlotId);
                    return (StatusCodes.NotFound, null);
                }
            }
            else if (body.VersionNumber > 0)
            {
                targetVersion = (int)body.VersionNumber;
            }
            else
            {
                // Default to latest
                targetVersion = slot.LatestVersion ?? 0;
                if (targetVersion == 0)
                {
                    _logger.LogWarning("No versions exist in slot {SlotId}", slot.SlotId);
                    return (StatusCodes.NotFound, null);
                }
            }

            // Try hot cache first
            var hotKey = HotSaveEntry.GetStateKey(slot.SlotId, targetVersion);
            var hotEntry = await hotCacheStore.GetAsync(hotKey, cancellationToken);

            byte[] decompressedData;
            string contentHash;
            SaveVersionManifest? manifest = null;

            if (hotEntry != null)
            {
                _logger.LogDebug("Hot cache hit for slot {SlotId} version {Version}", slot.SlotId, targetVersion);

                // Decompress if needed
                var compressedData = Convert.FromBase64String(hotEntry.Data);
                var compressionType = Enum.TryParse<CompressionType>(hotEntry.CompressionType, out var ct) ? ct : CompressionType.NONE;

                decompressedData = hotEntry.IsCompressed
                    ? Compression.CompressionHelper.Decompress(compressedData, compressionType)
                    : compressedData;

                contentHash = hotEntry.ContentHash;

                // Get manifest for additional metadata if requested
                if (body.IncludeMetadata)
                {
                    var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, targetVersion);
                    manifest = await versionStore.GetAsync(versionKey, cancellationToken);
                }
            }
            else
            {
                _logger.LogDebug("Hot cache miss for slot {SlotId} version {Version}, fetching from storage", slot.SlotId, targetVersion);

                // Get version manifest
                var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, targetVersion);
                manifest = await versionStore.GetAsync(versionKey, cancellationToken);

                if (manifest == null)
                {
                    _logger.LogWarning("Version {Version} not found in slot {SlotId}", targetVersion, slot.SlotId);
                    return (StatusCodes.NotFound, null);
                }

                // Load from Asset service if available
                if (!string.IsNullOrEmpty(manifest.AssetId))
                {
                    var assetResponse = await _versionDataLoader.LoadFromAssetServiceAsync(manifest.AssetId, cancellationToken);
                    if (assetResponse == null)
                    {
                        _logger.LogError("Failed to load asset {AssetId} for slot {SlotId} version {Version}",
                            manifest.AssetId, slot.SlotId, targetVersion);
                        await _messageBus.TryPublishErrorAsync(
                            "save-load",
                            "Load",
                            "AssetLoadFailure",
                            $"Failed to load asset {manifest.AssetId} for slot {slot.SlotId} version {targetVersion}");
                        return (StatusCodes.InternalServerError, null);
                    }

                    var compressionType = Enum.TryParse<CompressionType>(manifest.CompressionType, out var ct) ? ct : CompressionType.NONE;
                    decompressedData = compressionType != CompressionType.NONE
                        ? Compression.CompressionHelper.Decompress(assetResponse, compressionType)
                        : assetResponse;
                }
                else
                {
                    // Asset not yet uploaded - version should be in pending upload queue
                    // or the upload failed. Return not found.
                    _logger.LogWarning("Version {Version} has no asset ID and is not in hot cache", targetVersion);
                    return (StatusCodes.NotFound, null);
                }

                contentHash = manifest.ContentHash;

                // Re-cache in hot store
                await _versionDataLoader.CacheInHotStoreAsync(slot.SlotId, targetVersion, decompressedData, contentHash, manifest, hotCacheStore, cancellationToken);
            }

            // Verify integrity
            if (!Hashing.ContentHasher.VerifyHash(decompressedData, contentHash))
            {
                _logger.LogError("Hash mismatch for slot {SlotId} version {Version}", slot.SlotId, targetVersion);
                await _messageBus.TryPublishErrorAsync(
                    "save-load",
                    "Load",
                    "DataCorruption",
                    $"Hash mismatch for slot {slot.SlotId} version {targetVersion} - data may be corrupted");
                return (StatusCodes.InternalServerError, null);
            }

            var response = new LoadResponse
            {
                SlotId = Guid.Parse(slot.SlotId),
                VersionNumber = targetVersion,
                Data = decompressedData,
                ContentHash = contentHash,
                SchemaVersion = manifest?.SchemaVersion,
                DisplayName = manifest?.CheckpointName,
                Pinned = manifest?.IsPinned ?? false,
                CheckpointName = manifest?.CheckpointName,
                CreatedAt = manifest?.CreatedAt ?? DateTimeOffset.MinValue
            };

            _logger.LogInformation("Loaded version {Version} from slot {SlotId}, {Size} bytes",
                targetVersion, slot.SlotId, decompressedData.Length);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Load operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "Load",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/load",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Creates a new version by applying a delta (patch) to a base version.
    /// Delta versions store only the patch; full data is reconstructed on load.
    /// </summary>
    public async Task<(StatusCodes, SaveDeltaResponse?)> SaveDeltaAsync(SaveDeltaRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Saving delta for slot {SlotName} based on version {BaseVersion}",
            body.SlotName, body.BaseVersion);

        try
        {
            // Check if delta saves are enabled
            if (!_configuration.DeltaSavesEnabled)
            {
                _logger.LogWarning("Delta saves are disabled");
                return (StatusCodes.BadRequest, null);
            }

            // Find the slot
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var ownerType = body.OwnerType.ToString();
            var ownerId = body.OwnerId.ToString();
            var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, ownerType, ownerId, body.SlotName);
            var slot = await slotStore.GetAsync(slotKey, cancellationToken);

            if (slot == null)
            {
                _logger.LogWarning("Slot not found: {SlotKey}", slotKey);
                return (StatusCodes.NotFound, null);
            }

            // Get the base version
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
            var baseVersionKey = SaveVersionManifest.GetStateKey(slot.SlotId, body.BaseVersion);
            var baseVersion = await versionStore.GetAsync(baseVersionKey, cancellationToken);

            if (baseVersion == null)
            {
                _logger.LogWarning("Base version {Version} not found", body.BaseVersion);
                return (StatusCodes.NotFound, null);
            }

            // Validate the delta
            var deltaProcessor = new DeltaProcessor(
                _logger as ILogger<DeltaProcessor> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DeltaProcessor>.Instance,
                _configuration.MigrationMaxPatchOperations);
            var algorithm = (body.Algorithm ?? DeltaAlgorithm.JSON_PATCH).ToString();
            if (!deltaProcessor.ValidateDelta(body.Delta, algorithm))
            {
                _logger.LogWarning("Invalid delta provided");
                return (StatusCodes.BadRequest, null);
            }

            // Calculate delta chain length
            var chainLength = 1;
            var currentVersion = baseVersion;
            while (currentVersion.IsDelta && currentVersion.BaseVersionNumber.HasValue)
            {
                chainLength++;
                var prevKey = SaveVersionManifest.GetStateKey(slot.SlotId, currentVersion.BaseVersionNumber.Value);
                currentVersion = await versionStore.GetAsync(prevKey, cancellationToken);
                if (currentVersion == null)
                {
                    _logger.LogError("Delta chain broken at version {Version}", currentVersion?.BaseVersionNumber);
                    await _messageBus.TryPublishErrorAsync(
                        "save-load",
                        "SaveDelta",
                        "DeltaChainBroken",
                        "Delta chain broken - base version not found");
                    return (StatusCodes.InternalServerError, null);
                }
            }

            // Check if chain is too long
            if (chainLength >= _configuration.MaxDeltaChainLength)
            {
                _logger.LogWarning(
                    "Delta chain too long ({Length} >= {Max}), consider collapsing",
                    chainLength, _configuration.MaxDeltaChainLength);
            }

            // Check delta size threshold (only for base saves large enough for threshold to be meaningful)
            var deltaSize = body.Delta.Length;
            var estimatedFullSize = baseVersion.SizeBytes;
            if (estimatedFullSize >= _configuration.MinBaseSizeForDeltaThresholdBytes)
            {
                var deltaPercent = (double)deltaSize / estimatedFullSize * 100;
                if (deltaPercent > _configuration.DeltaSizeThresholdPercent)
                {
                    _logger.LogWarning(
                        "Delta size ({DeltaSize}) is {Percent:F1}% of full save, exceeds threshold of {Threshold}%",
                        deltaSize, deltaPercent, _configuration.DeltaSizeThresholdPercent);
                    return (StatusCodes.BadRequest, null);
                }
            }

            // Create new version number
            var newVersionNumber = (slot.LatestVersion ?? 0) + 1;
            slot.LatestVersion = newVersionNumber;

            // Compute content hash
            var contentHash = Hashing.ContentHasher.ComputeHash(body.Delta);

            // Compress the delta if needed
            var compressedDelta = body.Delta;
            var compressionTypeEnum = CompressionType.NONE;
            if (deltaSize > _configuration.AutoCompressThresholdBytes)
            {
                compressionTypeEnum = Enum.TryParse<CompressionType>(_configuration.DefaultCompressionType, out var ct)
                    ? ct : CompressionType.GZIP;
                compressedDelta = Compression.CompressionHelper.Compress(body.Delta, compressionTypeEnum);
            }

            // Store in hot cache
            var hotCacheStore = _stateStoreFactory.GetStore<HotSaveEntry>(StateStoreDefinitions.SaveLoadCache);
            var hotEntry = new HotSaveEntry
            {
                SlotId = slot.SlotId,
                VersionNumber = newVersionNumber,
                Data = Convert.ToBase64String(compressedDelta),
                ContentHash = contentHash,
                IsCompressed = compressionTypeEnum != CompressionType.NONE,
                CompressionType = compressionTypeEnum.ToString(),
                SizeBytes = deltaSize,
                CachedAt = DateTimeOffset.UtcNow,
                IsDelta = true
            };
            var hotCacheTtlSeconds = (int)TimeSpan.FromMinutes(_configuration.HotCacheTtlMinutes).TotalSeconds;
            await hotCacheStore.SaveAsync(
                hotEntry.GetStateKey(),
                hotEntry,
                new StateOptions { Ttl = hotCacheTtlSeconds },
                cancellationToken);

            // Create version manifest
            var manifest = new SaveVersionManifest
            {
                SlotId = slot.SlotId,
                VersionNumber = newVersionNumber,
                ContentHash = contentHash,
                SizeBytes = deltaSize,
                CompressedSizeBytes = compressedDelta.Length,
                CompressionType = compressionTypeEnum.ToString(),
                IsDelta = true,
                BaseVersionNumber = body.BaseVersion,
                DeltaAlgorithm = algorithm,
                DeviceId = body.DeviceId,
                SchemaVersion = body.SchemaVersion,
                Metadata = body.Metadata?.ToDictionary(kv => kv.Key, kv => (object)kv.Value) ?? new Dictionary<string, object>(),
                CreatedAt = DateTimeOffset.UtcNow,
                UploadStatus = _configuration.AsyncUploadEnabled ? "PENDING" : "COMPLETE"
            };

            // Save manifest and update slot
            await versionStore.SaveAsync(manifest.GetStateKey(), manifest, cancellationToken: cancellationToken);
            await slotStore.SaveAsync(slotKey, slot, cancellationToken: cancellationToken);

            // Queue for async upload if enabled
            if (_configuration.AsyncUploadEnabled)
            {
                var pendingStore = _stateStoreFactory.GetStore<PendingUploadEntry>(StateStoreDefinitions.SaveLoadPending);
                var uploadId = Guid.NewGuid().ToString();
                var pendingEntry = new PendingUploadEntry
                {
                    UploadId = uploadId,
                    SlotId = slot.SlotId,
                    VersionNumber = newVersionNumber,
                    GameId = slot.GameId,
                    OwnerId = ownerId,
                    OwnerType = ownerType,
                    Data = Convert.ToBase64String(compressedDelta),
                    ContentHash = contentHash,
                    CompressionType = compressionTypeEnum.ToString(),
                    SizeBytes = deltaSize,
                    CompressedSizeBytes = compressedDelta.Length,
                    IsDelta = true,
                    BaseVersionNumber = body.BaseVersion,
                    DeltaAlgorithm = algorithm,
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
                await pendingStore.AddToSetAsync(Processing.SaveUploadWorker.PendingUploadIdsSetKey, uploadId, cancellationToken: cancellationToken);
            }

            // Publish event
            var createdEvent = new SaveCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SlotId = Guid.Parse(slot.SlotId),
                SlotName = slot.SlotName,
                VersionNumber = newVersionNumber,
                OwnerId = Guid.Parse(ownerId),
                OwnerType = ownerType,
                Category = slot.Category,
                SizeBytes = deltaSize,
                SchemaVersion = body.SchemaVersion,
                Pinned = false
            };
            await _messageBus.TryPublishAsync("save-load.save.created", createdEvent, cancellationToken: cancellationToken);

            // Calculate compression savings
            var compressionSavings = estimatedFullSize > 0 ? 1.0 - ((double)deltaSize / estimatedFullSize) : 0;

            return (StatusCodes.OK, new SaveDeltaResponse
            {
                SlotId = Guid.Parse(slot.SlotId),
                VersionNumber = newVersionNumber,
                BaseVersion = body.BaseVersion,
                DeltaSizeBytes = deltaSize,
                EstimatedFullSizeBytes = estimatedFullSize,
                ChainLength = chainLength + 1,
                CompressionSavings = compressionSavings,
                CreatedAt = manifest.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing SaveDelta operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "SaveDelta",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/save-delta",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Loads save data, automatically reconstructing from delta chain if needed.
    /// Returns the full reconstructed data, not the raw delta.
    /// </summary>
    public async Task<(StatusCodes, LoadResponse?)> LoadWithDeltasAsync(LoadRequest body, CancellationToken cancellationToken)
    {
        // VersionNumber 0 means "load latest"
        var requestedVersion = body.VersionNumber == 0 ? -1 : body.VersionNumber;
        _logger.LogDebug(
            "Loading with delta reconstruction for slot {SlotName} version {Version}",
            body.SlotName, requestedVersion);

        try
        {
            // Find the slot
            var slot = await FindSlotByOwnerAndNameAsync(
                body.OwnerId.ToString(),
                body.OwnerType.ToString(),
                body.SlotName,
                cancellationToken);

            if (slot == null)
            {
                _logger.LogWarning("Slot not found: {SlotName}", body.SlotName);
                return (StatusCodes.NotFound, null);
            }

            // Get the target version (0 or null means latest)
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
            var versionNumber = (body.VersionNumber == null || body.VersionNumber == 0)
                ? (slot.LatestVersion ?? 0)
                : body.VersionNumber.Value;
            if (versionNumber == 0)
            {
                _logger.LogWarning("Slot {SlotName} has no versions", body.SlotName);
                return (StatusCodes.NotFound, null);
            }

            var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, versionNumber);
            var version = await versionStore.GetAsync(versionKey, cancellationToken);

            if (version == null)
            {
                _logger.LogWarning("Version {Version} not found", versionNumber);
                return (StatusCodes.NotFound, null);
            }

            byte[]? data;

            if (!version.IsDelta)
            {
                // Not a delta, load directly
                data = await _versionDataLoader.LoadVersionDataAsync(slot.SlotId, version, cancellationToken);
            }
            else
            {
                // Delta version - need to reconstruct
                data = await _versionDataLoader.ReconstructFromDeltaChainAsync(slot.SlotId, version, versionStore, cancellationToken);
            }

            if (data == null)
            {
                _logger.LogError("Failed to load data for version {Version}", versionNumber);
                await _messageBus.TryPublishErrorAsync(
                    "save-load",
                    "LoadWithDeltas",
                    "DataLoadFailure",
                    $"Failed to load data for version {versionNumber}");
                return (StatusCodes.InternalServerError, null);
            }

            // Publish load event
            var loadEvent = new SaveLoadedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SlotId = Guid.Parse(slot.SlotId),
                SlotName = slot.SlotName,
                VersionNumber = (int)versionNumber,
                OwnerId = body.OwnerId,
                OwnerType = body.OwnerType.ToString()
            };
            await _messageBus.TryPublishAsync("save-load.save.loaded", loadEvent, cancellationToken: cancellationToken);

            return (StatusCodes.OK, new LoadResponse
            {
                SlotId = Guid.Parse(slot.SlotId),
                VersionNumber = (int)versionNumber,
                Data = data,
                ContentHash = version.ContentHash,
                SchemaVersion = version.SchemaVersion,
                CreatedAt = version.CreatedAt,
                // Filter out null metadata values rather than coercing to empty string
                Metadata = version.Metadata?
                    .Where(kv => kv.Value != null)
                    .ToDictionary(
                        kv => kv.Key,
                        kv => kv.Value?.ToString() ?? throw new InvalidOperationException(
                            $"Metadata value became null after filter for key '{kv.Key}'"))
                    ?? new Dictionary<string, string>()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing LoadWithDeltas operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "LoadWithDeltas",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/load-with-deltas",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Collapses a chain of delta versions into a single full snapshot.
    /// Useful for reducing load latency or before deleting base versions.
    /// </summary>
    public async Task<(StatusCodes, SaveResponse?)> CollapseDeltasAsync(CollapseDeltasRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Collapsing deltas for slot {SlotName} to version {Version}",
            body.SlotName, body.VersionNumber ?? -1);

        try
        {
            // Find the slot
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var ownerType = body.OwnerType.ToString();
            var ownerId = body.OwnerId.ToString();
            var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, ownerType, ownerId, body.SlotName);
            var slot = await slotStore.GetAsync(slotKey, cancellationToken);

            if (slot == null)
            {
                _logger.LogWarning("Slot not found: {SlotKey}", slotKey);
                return (StatusCodes.NotFound, null);
            }

            // Get the target version
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
            var versionToFetch = body.VersionNumber ?? slot.LatestVersion;
            if (!versionToFetch.HasValue)
            {
                _logger.LogWarning("No version specified and slot has no latest version");
                return (StatusCodes.NotFound, null);
            }
            var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, versionToFetch.Value);
            var targetVersion = await versionStore.GetAsync(versionKey, cancellationToken);

            if (targetVersion == null)
            {
                _logger.LogWarning("Target version {Version} not found", versionToFetch.Value);
                return (StatusCodes.NotFound, null);
            }

            // If it's not a delta, nothing to collapse
            if (!targetVersion.IsDelta)
            {
                _logger.LogInformation("Version {Version} is already a full snapshot", targetVersion.VersionNumber);
                return (StatusCodes.OK, new SaveResponse
                {
                    SlotId = Guid.Parse(slot.SlotId),
                    VersionNumber = targetVersion.VersionNumber,
                    SizeBytes = targetVersion.SizeBytes,
                    CreatedAt = targetVersion.CreatedAt,
                    UploadPending = targetVersion.UploadStatus == "PENDING"
                });
            }

            // Reconstruct full data from delta chain
            var reconstructedData = await _versionDataLoader.ReconstructFromDeltaChainAsync(
                slot.SlotId, targetVersion, versionStore, cancellationToken);

            if (reconstructedData == null)
            {
                _logger.LogError("Failed to reconstruct data from delta chain");
                await _messageBus.TryPublishErrorAsync(
                    "save-load",
                    "CollapseDeltas",
                    "DeltaReconstructionFailure",
                    "Failed to reconstruct data from delta chain");
                return (StatusCodes.InternalServerError, null);
            }

            // Collect intermediate delta versions for potential deletion
            var intermediateVersions = new List<SaveVersionManifest>();
            var currentVersion = targetVersion;
            while (currentVersion.IsDelta && currentVersion.BaseVersionNumber.HasValue)
            {
                intermediateVersions.Add(currentVersion);
                var prevKey = SaveVersionManifest.GetStateKey(slot.SlotId, currentVersion.BaseVersionNumber.Value);
                currentVersion = await versionStore.GetAsync(prevKey, cancellationToken);
                if (currentVersion == null)
                {
                    break;
                }
            }

            // Compress the reconstructed data
            var compressionTypeEnum = Enum.TryParse<CompressionType>(_configuration.DefaultCompressionType, out var ct) ? ct : CompressionType.GZIP;
            var compressedData = Compression.CompressionHelper.Compress(reconstructedData, compressionTypeEnum);

            // Update the target version to be a full snapshot
            var contentHash = Hashing.ContentHasher.ComputeHash(reconstructedData);
            targetVersion.IsDelta = false;
            targetVersion.BaseVersionNumber = null;
            targetVersion.DeltaAlgorithm = null;
            targetVersion.SizeBytes = reconstructedData.Length;
            targetVersion.CompressedSizeBytes = compressedData.Length;
            targetVersion.CompressionType = compressionTypeEnum.ToString();
            targetVersion.ContentHash = contentHash;
            targetVersion.UploadStatus = _configuration.AsyncUploadEnabled ? "PENDING" : "COMPLETE";

            // Save updated manifest
            await versionStore.SaveAsync(targetVersion.GetStateKey(), targetVersion, cancellationToken: cancellationToken);

            // Store in hot cache
            var hotCacheStore = _stateStoreFactory.GetStore<HotSaveEntry>(StateStoreDefinitions.SaveLoadCache);
            var resolvedVersionNumber = targetVersion.VersionNumber;
            var hotEntry = new HotSaveEntry
            {
                SlotId = slot.SlotId,
                VersionNumber = resolvedVersionNumber,
                Data = Convert.ToBase64String(compressedData),
                ContentHash = contentHash,
                IsCompressed = compressionTypeEnum != CompressionType.NONE,
                CompressionType = compressionTypeEnum.ToString(),
                SizeBytes = reconstructedData.Length,
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
                var pendingStore = _stateStoreFactory.GetStore<PendingUploadEntry>(StateStoreDefinitions.SaveLoadPending);
                var uploadId = Guid.NewGuid().ToString();
                var pendingEntry = new PendingUploadEntry
                {
                    UploadId = uploadId,
                    SlotId = slot.SlotId,
                    VersionNumber = resolvedVersionNumber,
                    GameId = slot.GameId,
                    OwnerId = ownerId,
                    OwnerType = ownerType,
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
                await pendingStore.AddToSetAsync(Processing.SaveUploadWorker.PendingUploadIdsSetKey, uploadId, cancellationToken: cancellationToken);
            }

            // Delete intermediate versions if requested
            if (body.DeleteIntermediates)
            {
                foreach (var intermediate in intermediateVersions)
                {
                    // Don't delete pinned versions
                    if (intermediate.IsPinned)
                    {
                        continue;
                    }

                    // Don't delete the target version itself
                    if (intermediate.VersionNumber == resolvedVersionNumber)
                    {
                        continue;
                    }

                    await versionStore.DeleteAsync(intermediate.GetStateKey(), cancellationToken);
                    _logger.LogDebug("Deleted intermediate delta version {Version}", intermediate.VersionNumber);
                }
            }

            _logger.LogInformation(
                "Collapsed delta chain for version {Version}, new size {Size} bytes",
                resolvedVersionNumber, reconstructedData.Length);

            return (StatusCodes.OK, new SaveResponse
            {
                SlotId = Guid.Parse(slot.SlotId),
                VersionNumber = resolvedVersionNumber,
                SizeBytes = reconstructedData.Length,
                CreatedAt = targetVersion.CreatedAt,
                UploadPending = targetVersion.UploadStatus == "PENDING"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing CollapseDeltas operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "CollapseDeltas",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/collapse-deltas",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of ListVersions operation.
    /// Lists all versions in a slot with optional filtering.
    /// </summary>
    public async Task<(StatusCodes, ListVersionsResponse?)> ListVersionsAsync(ListVersionsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Listing versions for slot {SlotName} owner {OwnerId} ({OwnerType})",
            body.SlotName, body.OwnerId, body.OwnerType);

        try
        {
            // Find the slot by querying slots for this owner
            var slotQueryStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var ownerIdStr = body.OwnerId.ToString();
            var ownerTypeStr = body.OwnerType.ToString();
            var matchingSlots = await slotQueryStore.QueryAsync(
                s => s.OwnerId == ownerIdStr &&
                    s.OwnerType == ownerTypeStr &&
                    s.SlotName == body.SlotName,
                cancellationToken);

            var slot = matchingSlots.FirstOrDefault();
            if (slot == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Query versions for this slot
            var versionQueryStore = _stateStoreFactory.GetQueryableStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
            var versions = await versionQueryStore.QueryAsync(
                v => v.SlotId == slot.SlotId,
                cancellationToken);

            // Apply filters
            if (body.PinnedOnly)
            {
                versions = versions.Where(v => v.IsPinned).ToList();
            }

            var totalCount = versions.Count();

            // Apply pagination
            var pagedVersions = versions
                .OrderByDescending(v => v.VersionNumber)
                .Skip(body.Offset)
                .Take(body.Limit)
                .Select(v => MapToVersionResponse(v))
                .ToList();

            return (StatusCodes.OK, new ListVersionsResponse
            {
                Versions = pagedVersions,
                TotalCount = totalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing versions");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "ListVersions",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/version/list",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of PinVersion operation.
    /// Pins a version to prevent rolling cleanup deletion.
    /// </summary>
    public async Task<(StatusCodes, VersionResponse?)> PinVersionAsync(PinVersionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Pinning version {Version} in slot {SlotName} for owner {OwnerId}",
            body.VersionNumber, body.SlotName, body.OwnerId);

        try
        {
            // Find the slot
            var slot = await FindSlotByOwnerAndNameAsync(body.OwnerId.ToString(), body.OwnerType.ToString(), body.SlotName, cancellationToken);
            if (slot == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get the version
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
            var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, body.VersionNumber);
            var version = await versionStore.GetAsync(versionKey, cancellationToken);

            if (version == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Pin the version
            version.IsPinned = true;
            version.CheckpointName = body.CheckpointName;
            await versionStore.SaveAsync(versionKey, version, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Pinned version {Version} in slot {SlotId} with checkpoint name {CheckpointName}",
                body.VersionNumber, slot.SlotId, body.CheckpointName);

            // Publish event
            var pinnedEvent = new VersionPinnedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SlotId = Guid.Parse(slot.SlotId),
                SlotName = slot.SlotName,
                VersionNumber = version.VersionNumber,
                CheckpointName = body.CheckpointName
            };
            await _messageBus.TryPublishAsync(
                "save-load.version.pinned",
                pinnedEvent,
                cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapToVersionResponse(version));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pinning version");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "PinVersion",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/version/pin",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of UnpinVersion operation.
    /// Unpins a version allowing it to be cleaned up by rolling retention.
    /// </summary>
    public async Task<(StatusCodes, VersionResponse?)> UnpinVersionAsync(UnpinVersionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Unpinning version {Version} in slot {SlotName} for owner {OwnerId}",
            body.VersionNumber, body.SlotName, body.OwnerId);

        try
        {
            // Find the slot
            var slot = await FindSlotByOwnerAndNameAsync(body.OwnerId.ToString(), body.OwnerType.ToString(), body.SlotName, cancellationToken);
            if (slot == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get the version
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
            var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, body.VersionNumber);
            var version = await versionStore.GetAsync(versionKey, cancellationToken);

            if (version == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Unpin the version
            var previousCheckpointName = version.CheckpointName;
            version.IsPinned = false;
            version.CheckpointName = null;
            await versionStore.SaveAsync(versionKey, version, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Unpinned version {Version} in slot {SlotId}",
                body.VersionNumber, slot.SlotId);

            // Publish event
            var unpinnedEvent = new VersionUnpinnedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SlotId = Guid.Parse(slot.SlotId),
                SlotName = slot.SlotName,
                VersionNumber = version.VersionNumber,
                PreviousCheckpointName = previousCheckpointName
            };
            await _messageBus.TryPublishAsync(
                "save-load.version.unpinned",
                unpinnedEvent,
                cancellationToken: cancellationToken);

            return (StatusCodes.OK, MapToVersionResponse(version));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unpinning version");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "UnpinVersion",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/version/unpin",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of DeleteVersion operation.
    /// Deletes a specific version (cannot delete pinned versions).
    /// </summary>
    public async Task<(StatusCodes, DeleteVersionResponse?)> DeleteVersionAsync(DeleteVersionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Deleting version {Version} in slot {SlotName} for owner {OwnerId}",
            body.VersionNumber, body.SlotName, body.OwnerId);

        try
        {
            // Find the slot
            var slot = await FindSlotByOwnerAndNameAsync(body.OwnerId.ToString(), body.OwnerType.ToString(), body.SlotName, cancellationToken);
            if (slot == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get the version
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
            var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, body.VersionNumber);
            var version = await versionStore.GetAsync(versionKey, cancellationToken);

            if (version == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Check if version is pinned
            if (version.IsPinned)
            {
                _logger.LogWarning(
                    "Cannot delete pinned version {Version} in slot {SlotId}",
                    body.VersionNumber, slot.SlotId);
                return (StatusCodes.Conflict, null);
            }

            var bytesFreed = version.CompressedSizeBytes ?? version.SizeBytes;

            // Delete the asset if it exists
            if (!string.IsNullOrEmpty(version.AssetId) && Guid.TryParse(version.AssetId, out var assetGuid))
            {
                try
                {
                    await _assetClient.DeleteAssetAsync(new DeleteAssetRequest { AssetId = assetGuid.ToString() }, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete asset {AssetId} for version {Version}", version.AssetId, body.VersionNumber);
                    // Continue with version deletion even if asset deletion fails
                }
            }

            // Delete the version manifest
            await versionStore.DeleteAsync(versionKey, cancellationToken);

            // Delete from hot cache if present
            var hotStore = _stateStoreFactory.GetStore<HotSaveEntry>(StateStoreDefinitions.SaveLoadCache);
            var hotCacheKey = HotSaveEntry.GetStateKey(slot.SlotId, body.VersionNumber);
            await hotStore.DeleteAsync(hotCacheKey, cancellationToken);

            // Update slot metadata
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            slot.VersionCount = Math.Max(0, slot.VersionCount - 1);
            slot.TotalSizeBytes = Math.Max(0, slot.TotalSizeBytes - bytesFreed);
            slot.UpdatedAt = DateTimeOffset.UtcNow;

            // If we deleted the latest version, find the new latest
            if (slot.LatestVersion == body.VersionNumber)
            {
                var versionQueryStore = _stateStoreFactory.GetQueryableStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
                var remainingVersions = await versionQueryStore.QueryAsync(
                    v => v.SlotId == slot.SlotId,
                    cancellationToken);
                slot.LatestVersion = remainingVersions.OrderByDescending(v => v.VersionNumber).FirstOrDefault()?.VersionNumber;
            }

            await slotStore.SaveAsync(slot.GetStateKey(), slot, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Deleted version {Version} in slot {SlotId}, freed {BytesFreed} bytes",
                body.VersionNumber, slot.SlotId, bytesFreed);

            // Publish event
            var deletedEvent = new VersionDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SlotId = Guid.Parse(slot.SlotId),
                SlotName = slot.SlotName,
                VersionNumber = body.VersionNumber,
                BytesFreed = bytesFreed
            };
            await _messageBus.TryPublishAsync(
                "save-load.version.deleted",
                deletedEvent,
                cancellationToken: cancellationToken);

            return (StatusCodes.OK, new DeleteVersionResponse
            {
                Deleted = true,
                BytesFreed = bytesFreed
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting version");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "DeleteVersion",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/version/delete",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Queries saves based on filter criteria including owner, category, tags, and date ranges.
    /// </summary>
    public async Task<(StatusCodes, QuerySavesResponse?)> QuerySavesAsync(QuerySavesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Querying saves for owner {OwnerId} ({OwnerType})",
            body.OwnerId, body.OwnerType);

        try
        {
            var ownerIdStr = body.OwnerId.ToString();
            var ownerTypeStr = body.OwnerType.ToString();
            var categoryStr = body.Category.ToString();

            // Query slots for this owner
            var slotQueryStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var slots = await slotQueryStore.QueryAsync(
                s => s.OwnerId == ownerIdStr && s.OwnerType == ownerTypeStr,
                cancellationToken);

            // Filter by category if specified
            if (body.Category != default)
            {
                slots = slots.Where(s => s.Category == categoryStr).ToList();
            }

            // Query versions for matching slots
            var versionQueryStore = _stateStoreFactory.GetQueryableStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
            var results = new List<QueryResultItem>();

            foreach (var slot in slots)
            {
                var versions = await versionQueryStore.QueryAsync(
                    v => v.SlotId == slot.SlotId,
                    cancellationToken);

                // Apply version filters
                foreach (var version in versions)
                {
                    // Filter by date
                    if (body.CreatedAfter != default && version.CreatedAt < body.CreatedAfter)
                    {
                        continue;
                    }
                    if (body.CreatedBefore != default && version.CreatedAt > body.CreatedBefore)
                    {
                        continue;
                    }

                    // Filter by pinned status
                    if (body.PinnedOnly == true && !version.IsPinned)
                    {
                        continue;
                    }

                    // Filter by schema version
                    if (!string.IsNullOrEmpty(body.SchemaVersion) && version.SchemaVersion != body.SchemaVersion)
                    {
                        continue;
                    }

                    // Filter by metadata
                    if (body.MetadataFilter != null && body.MetadataFilter.Count > 0)
                    {
                        if (version.Metadata == null)
                        {
                            continue;
                        }

                        bool metadataMatch = true;
                        foreach (var kvp in body.MetadataFilter)
                        {
                            if (!version.Metadata.TryGetValue(kvp.Key, out var value) || !string.Equals(value?.ToString(), kvp.Value, StringComparison.Ordinal))
                            {
                                metadataMatch = false;
                                break;
                            }
                        }
                        if (!metadataMatch)
                        {
                            continue;
                        }
                    }

                    results.Add(new QueryResultItem
                    {
                        SlotId = Guid.Parse(slot.SlotId),
                        SlotName = slot.SlotName,
                        OwnerId = Guid.Parse(slot.OwnerId),
                        OwnerType = Enum.TryParse<OwnerType>(slot.OwnerType, out var ot) ? ot : OwnerType.ACCOUNT,
                        Category = Enum.TryParse<SaveCategory>(slot.Category, out var cat) ? cat : SaveCategory.MANUAL_SAVE,
                        VersionNumber = version.VersionNumber,
                        SizeBytes = version.SizeBytes,
                        SchemaVersion = version.SchemaVersion,
                        DisplayName = version.CheckpointName,
                        Pinned = version.IsPinned,
                        CheckpointName = version.CheckpointName,
                        CreatedAt = version.CreatedAt,
                        // Filter out null metadata values rather than coercing to empty string
                        Metadata = version.Metadata?
                            .Where(kvp => kvp.Value != null)
                            .ToDictionary(
                                kvp => kvp.Key,
                                kvp => kvp.Value?.ToString() ?? throw new InvalidOperationException(
                                    $"Metadata value became null after filter for key '{kvp.Key}'"))
                            ?? new Dictionary<string, string>()
                    });
                }
            }

            // Sort results
            results = body.SortBy switch
            {
                QuerySavesRequestSortBy.Created_at => body.SortOrder == QuerySavesRequestSortOrder.Asc
                    ? results.OrderBy(r => r.CreatedAt).ToList()
                    : results.OrderByDescending(r => r.CreatedAt).ToList(),
                QuerySavesRequestSortBy.Size => body.SortOrder == QuerySavesRequestSortOrder.Asc
                    ? results.OrderBy(r => r.SizeBytes).ToList()
                    : results.OrderByDescending(r => r.SizeBytes).ToList(),
                QuerySavesRequestSortBy.Version_number => body.SortOrder == QuerySavesRequestSortOrder.Asc
                    ? results.OrderBy(r => r.VersionNumber).ToList()
                    : results.OrderByDescending(r => r.VersionNumber).ToList(),
                _ => results.OrderByDescending(r => r.CreatedAt).ToList()
            };

            // Apply pagination
            var totalCount = results.Count;
            results = results.Skip(body.Offset).Take(body.Limit).ToList();

            _logger.LogInformation(
                "Query returned {Count} results out of {Total} total",
                results.Count, totalCount);

            return (StatusCodes.OK, new QuerySavesResponse
            {
                Results = results,
                TotalCount = totalCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing QuerySaves operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "QuerySaves",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/query",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of CopySave operation.
    /// Copies a save from one slot to another (optionally cross-owner or cross-game).
    /// </summary>
    public async Task<(StatusCodes, SaveResponse?)> CopySaveAsync(CopySaveRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Copying save from {SourceSlot} to {TargetSlot}",
            body.SourceSlotName, body.TargetSlotName);

        try
        {
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);

            // Find source slot
            var sourceOwnerType = body.SourceOwnerType.ToString();
            var sourceOwnerId = body.SourceOwnerId.ToString();
            var sourceSlotKey = SaveSlotMetadata.GetStateKey(body.SourceGameId, sourceOwnerType, sourceOwnerId, body.SourceSlotName);
            var sourceSlot = await slotStore.GetAsync(sourceSlotKey, cancellationToken);

            if (sourceSlot == null)
            {
                _logger.LogWarning("Source slot not found: {SlotKey}", sourceSlotKey);
                return (StatusCodes.NotFound, null);
            }

            // Get source version
            var sourceVersionNumber = body.SourceVersion ?? sourceSlot.LatestVersion;
            if (!sourceVersionNumber.HasValue)
            {
                _logger.LogWarning("Source slot has no versions");
                return (StatusCodes.NotFound, null);
            }
            var sourceVersionKey = SaveVersionManifest.GetStateKey(sourceSlot.SlotId, sourceVersionNumber.Value);
            var sourceVersion = await versionStore.GetAsync(sourceVersionKey, cancellationToken);

            if (sourceVersion == null)
            {
                _logger.LogWarning("Source version {Version} not found", sourceVersionNumber);
                return (StatusCodes.NotFound, null);
            }

            // Load source data
            var sourceData = await _versionDataLoader.LoadVersionDataAsync(sourceSlot.SlotId, sourceVersion, cancellationToken);
            if (sourceData == null)
            {
                _logger.LogError("Failed to load source version data for slot {SlotId} version {Version}",
                    sourceSlot.SlotId, sourceVersionNumber);
                await _messageBus.TryPublishErrorAsync(
                    "save-load",
                    "Copy",
                    "DataLoadFailure",
                    $"Failed to load source version data for slot {sourceSlot.SlotId} version {sourceVersionNumber}");
                return (StatusCodes.InternalServerError, null);
            }

            // Find or create target slot
            var targetOwnerType = body.TargetOwnerType.ToString();
            var targetOwnerId = body.TargetOwnerId.ToString();
            var targetSlotKey = SaveSlotMetadata.GetStateKey(body.TargetGameId, targetOwnerType, targetOwnerId, body.TargetSlotName);
            var targetSlot = await slotStore.GetAsync(targetSlotKey, cancellationToken);

            if (targetSlot == null)
            {
                // Create target slot
                var category = body.TargetCategory != default ? body.TargetCategory.ToString() : sourceSlot.Category;
                targetSlot = new SaveSlotMetadata
                {
                    SlotId = Guid.NewGuid().ToString(),
                    GameId = body.TargetGameId,
                    OwnerId = targetOwnerId,
                    OwnerType = targetOwnerType,
                    SlotName = body.TargetSlotName,
                    Category = category,
                    MaxVersions = sourceSlot.MaxVersions,
                    LatestVersion = 0,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await slotStore.SaveAsync(targetSlot.GetStateKey(), targetSlot, cancellationToken: cancellationToken);
            }

            // Create new version in target slot
            var newVersionNumber = (targetSlot.LatestVersion ?? 0) + 1;
            var contentHash = Hashing.ContentHasher.ComputeHash(sourceData);
            var compressionTypeEnum = Enum.TryParse<CompressionType>(_configuration.DefaultCompressionType, out var ct) ? ct : CompressionType.GZIP;
            var compressedData = Compression.CompressionHelper.Compress(sourceData, compressionTypeEnum);

            var newVersion = new SaveVersionManifest
            {
                SlotId = targetSlot.SlotId,
                VersionNumber = newVersionNumber,
                ContentHash = contentHash,
                SizeBytes = sourceData.Length,
                CompressedSizeBytes = compressedData.Length,
                CompressionType = compressionTypeEnum.ToString(),
                SchemaVersion = sourceVersion.SchemaVersion,
                CheckpointName = sourceVersion.CheckpointName,
                Metadata = sourceVersion.Metadata != null ? new Dictionary<string, object>(sourceVersion.Metadata) : new Dictionary<string, object>(),
                IsPinned = false,
                IsDelta = false,
                UploadStatus = _configuration.AsyncUploadEnabled ? "PENDING" : "COMPLETE",
                CreatedAt = DateTimeOffset.UtcNow
            };
            await versionStore.SaveAsync(newVersion.GetStateKey(), newVersion, cancellationToken: cancellationToken);

            // Update target slot
            targetSlot.LatestVersion = newVersionNumber;
            targetSlot.UpdatedAt = DateTimeOffset.UtcNow;
            await slotStore.SaveAsync(targetSlot.GetStateKey(), targetSlot, cancellationToken: cancellationToken);

            // Store in hot cache
            var hotCacheStore = _stateStoreFactory.GetStore<HotSaveEntry>(StateStoreDefinitions.SaveLoadCache);
            var hotEntry = new HotSaveEntry
            {
                SlotId = targetSlot.SlotId,
                VersionNumber = newVersionNumber,
                Data = Convert.ToBase64String(compressedData),
                ContentHash = contentHash,
                IsCompressed = compressionTypeEnum != CompressionType.NONE,
                CompressionType = compressionTypeEnum.ToString(),
                SizeBytes = sourceData.Length,
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
                var pendingStore = _stateStoreFactory.GetStore<PendingUploadEntry>(StateStoreDefinitions.SaveLoadPending);
                var uploadId = Guid.NewGuid().ToString();
                var pendingEntry = new PendingUploadEntry
                {
                    UploadId = uploadId,
                    SlotId = targetSlot.SlotId,
                    VersionNumber = newVersionNumber,
                    GameId = body.TargetGameId,
                    OwnerId = targetOwnerId,
                    OwnerType = targetOwnerType,
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
                await pendingStore.AddToSetAsync(Processing.SaveUploadWorker.PendingUploadIdsSetKey, uploadId, cancellationToken: cancellationToken);
            }

            _logger.LogInformation(
                "Copied save from {SourceSlot} version {SourceVersion} to {TargetSlot} version {TargetVersion}",
                body.SourceSlotName, sourceVersionNumber, body.TargetSlotName, newVersionNumber);

            return (StatusCodes.OK, new SaveResponse
            {
                SlotId = Guid.Parse(targetSlot.SlotId),
                VersionNumber = newVersionNumber,
                SizeBytes = sourceData.Length,
                CreatedAt = newVersion.CreatedAt,
                UploadPending = _configuration.AsyncUploadEnabled
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing CopySave operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "CopySave",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/copy",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of ExportSaves operation.
    /// Creates a ZIP archive of saves for the owner and uploads it to asset storage.
    /// </summary>
    public async Task<(StatusCodes, ExportSavesResponse?)> ExportSavesAsync(ExportSavesRequest body, CancellationToken cancellationToken)
    {
        return await _saveExportImportManager.ExportSavesAsync(body, cancellationToken);
    }

    /// <summary>
    /// Implementation of ImportSaves operation.
    /// Imports saves from an uploaded export archive.
    /// </summary>
    public async Task<(StatusCodes, ImportSavesResponse?)> ImportSavesAsync(ImportSavesRequest body, CancellationToken cancellationToken)
    {
        return await _saveExportImportManager.ImportSavesAsync(body, cancellationToken);
    }

    /// <summary>
    /// Implementation of VerifyIntegrity operation.
    /// Verifies the integrity of save data by comparing hash values.
    /// </summary>
    public async Task<(StatusCodes, VerifyIntegrityResponse?)> VerifyIntegrityAsync(VerifyIntegrityRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Verifying integrity for slot {SlotName} owner {OwnerId}",
            body.SlotName, body.OwnerId);

        try
        {
            var ownerIdStr = body.OwnerId.ToString();
            var ownerTypeStr = body.OwnerType.ToString();

            // Find the slot
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, ownerTypeStr, ownerIdStr, body.SlotName);
            var slot = await slotStore.GetAsync(slotKey, cancellationToken);

            if (slot == null)
            {
                _logger.LogWarning("Slot not found: {SlotKey}", slotKey);
                return (StatusCodes.NotFound, null);
            }

            // Get the version to verify
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
            var versionNumber = body.VersionNumber ?? slot.LatestVersion;
            if (!versionNumber.HasValue)
            {
                _logger.LogWarning("No version to verify");
                return (StatusCodes.NotFound, null);
            }

            var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, versionNumber.Value);
            var version = await versionStore.GetAsync(versionKey, cancellationToken);

            if (version == null)
            {
                _logger.LogWarning("Version {Version} not found", versionNumber);
                return (StatusCodes.NotFound, null);
            }

            var expectedHash = version.ContentHash;

            // Try to load the data
            var data = await _versionDataLoader.LoadVersionDataAsync(slot.SlotId, version, cancellationToken);

            if (data == null)
            {
                return (StatusCodes.OK, new VerifyIntegrityResponse
                {
                    Valid = false,
                    VersionNumber = versionNumber.Value,
                    ExpectedHash = expectedHash,
                    ActualHash = null,
                    ErrorMessage = "Unable to load save data for verification"
                });
            }

            // Compute actual hash
            var actualHash = Hashing.ContentHasher.ComputeHash(data);
            var hashesMatch = string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase);

            _logger.LogInformation(
                "Integrity verification for version {Version}: {Result} (expected {Expected}, actual {Actual})",
                versionNumber, hashesMatch ? "VALID" : "INVALID", expectedHash, actualHash);

            return (StatusCodes.OK, new VerifyIntegrityResponse
            {
                Valid = hashesMatch,
                VersionNumber = versionNumber.Value,
                ExpectedHash = expectedHash,
                ActualHash = actualHash,
                ErrorMessage = hashesMatch ? null : "Hash mismatch - data may be corrupted"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing VerifyIntegrity operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "VerifyIntegrity",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/verify",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of PromoteVersion operation.
    /// Promotes an old version to become the new latest version by copying it.
    /// </summary>
    public async Task<(StatusCodes, SaveResponse?)> PromoteVersionAsync(PromoteVersionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Promoting version {Version} to latest in slot {SlotName} for owner {OwnerId}",
            body.VersionNumber, body.SlotName, body.OwnerId);

        try
        {
            // Get the slot
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, body.OwnerType.ToString(), body.OwnerId.ToString(), body.SlotName);
            var slot = await slotStore.GetAsync(slotKey, cancellationToken);

            if (slot == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get the source version
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
            var sourceVersionKey = SaveVersionManifest.GetStateKey(slot.SlotId, body.VersionNumber);
            var sourceVersion = await versionStore.GetAsync(sourceVersionKey, cancellationToken);

            if (sourceVersion == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get the data from hot cache or asset service
            byte[] data;
            var hotStore = _stateStoreFactory.GetStore<HotSaveEntry>(StateStoreDefinitions.SaveLoadCache);
            var hotCacheKey = HotSaveEntry.GetStateKey(slot.SlotId, body.VersionNumber);
            var hotEntry = await hotStore.GetAsync(hotCacheKey, cancellationToken);

            if (hotEntry != null)
            {
                data = Convert.FromBase64String(hotEntry.Data);
            }
            else if (!string.IsNullOrEmpty(sourceVersion.AssetId) && Guid.TryParse(sourceVersion.AssetId, out var assetGuid))
            {
                var assetResponse = await _assetClient.GetAssetAsync(new GetAssetRequest { AssetId = assetGuid.ToString() }, cancellationToken);
                if (assetResponse?.DownloadUrl == null)
                {
                    _logger.LogError("Failed to get download URL for asset {AssetId}", sourceVersion.AssetId);
                    await _messageBus.TryPublishErrorAsync(
                        "save-load",
                        "PromoteVersion",
                        "AssetServiceFailure",
                        $"Failed to get download URL for asset {sourceVersion.AssetId}");
                    return (StatusCodes.InternalServerError, null);
                }

                using var httpClient = _httpClientFactory.CreateClient();
                data = await httpClient.GetByteArrayAsync(assetResponse.DownloadUrl, cancellationToken);
            }
            else
            {
                _logger.LogError("No data available for version {Version} in slot {SlotId} - neither hot cache nor asset storage",
                    body.VersionNumber, slot.SlotId);
                await _messageBus.TryPublishErrorAsync(
                    "save-load",
                    "PromoteVersion",
                    "DataUnavailable",
                    $"No data available for version {body.VersionNumber} in slot {slot.SlotId} - neither hot cache nor asset storage");
                return (StatusCodes.InternalServerError, null);
            }

            // Create new version number
            var newVersionNumber = (slot.LatestVersion ?? 0) + 1;

            // Create new version manifest (copy metadata from source)
            var newVersion = new SaveVersionManifest
            {
                SlotId = slot.SlotId,
                VersionNumber = newVersionNumber,
                ContentHash = sourceVersion.ContentHash,
                SizeBytes = sourceVersion.SizeBytes,
                CompressedSizeBytes = sourceVersion.CompressedSizeBytes,
                CompressionType = sourceVersion.CompressionType,
                SchemaVersion = sourceVersion.SchemaVersion,
                Metadata = new Dictionary<string, object>(sourceVersion.Metadata),
                UploadStatus = "PENDING",
                CreatedAt = DateTimeOffset.UtcNow
            };

            // Add promoted metadata
            newVersion.Metadata["promotedFrom"] = body.VersionNumber;

            // Store in hot cache
            var newHotEntry = new HotSaveEntry
            {
                SlotId = slot.SlotId,
                VersionNumber = newVersionNumber,
                Data = Convert.ToBase64String(data),
                ContentHash = sourceVersion.ContentHash,
                CachedAt = DateTimeOffset.UtcNow
            };
            var newHotCacheKey = HotSaveEntry.GetStateKey(slot.SlotId, newVersionNumber);
            await hotStore.SaveAsync(newHotCacheKey, newHotEntry, cancellationToken: cancellationToken);

            // Queue for async upload
            if (_configuration.AsyncUploadEnabled)
            {
                var pendingStore = _stateStoreFactory.GetStore<PendingUploadEntry>(StateStoreDefinitions.SaveLoadPending);
                var uploadId = Guid.NewGuid().ToString();
                var pendingEntry = new PendingUploadEntry
                {
                    UploadId = uploadId,
                    SlotId = slot.SlotId,
                    VersionNumber = newVersionNumber,
                    GameId = slot.GameId,
                    OwnerId = slot.OwnerId,
                    OwnerType = slot.OwnerType,
                    Data = Convert.ToBase64String(data),
                    ContentHash = sourceVersion.ContentHash,
                    SizeBytes = sourceVersion.SizeBytes,
                    CompressedSizeBytes = sourceVersion.CompressedSizeBytes ?? sourceVersion.SizeBytes,
                    Priority = 0,
                    AttemptCount = 0,
                    QueuedAt = DateTimeOffset.UtcNow
                };
                await pendingStore.SaveAsync(pendingEntry.GetStateKey(), pendingEntry, cancellationToken: cancellationToken);
                // Add to tracking set for Redis-based queue processing
                await pendingStore.AddToSetAsync(Processing.SaveUploadWorker.PendingUploadIdsSetKey, uploadId, cancellationToken: cancellationToken);
            }

            // Save version manifest
            var newVersionKey = SaveVersionManifest.GetStateKey(slot.SlotId, newVersionNumber);
            await versionStore.SaveAsync(newVersionKey, newVersion, cancellationToken: cancellationToken);

            // Update slot
            slot.LatestVersion = newVersionNumber;
            slot.VersionCount++;
            slot.TotalSizeBytes += sourceVersion.CompressedSizeBytes ?? sourceVersion.SizeBytes;
            slot.UpdatedAt = DateTimeOffset.UtcNow;
            await slotStore.SaveAsync(slotKey, slot, cancellationToken: cancellationToken);

            // Run rolling cleanup
            await _versionCleanupManager.CleanupOldVersionsAsync(slot, cancellationToken);

            _logger.LogInformation(
                "Promoted version {SourceVersion} to version {NewVersion} in slot {SlotId}",
                body.VersionNumber, newVersionNumber, slot.SlotId);

            // Publish event
            var createdEvent = new SaveCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SlotId = Guid.Parse(slot.SlotId),
                SlotName = slot.SlotName,
                VersionNumber = newVersionNumber,
                OwnerId = Guid.Parse(slot.OwnerId),
                OwnerType = slot.OwnerType,
                SizeBytes = sourceVersion.SizeBytes
            };
            await _messageBus.TryPublishAsync(
                "save-load.save.created",
                createdEvent,
                cancellationToken: cancellationToken);

            return (StatusCodes.OK, new SaveResponse
            {
                SlotId = Guid.Parse(slot.SlotId),
                VersionNumber = newVersionNumber,
                ContentHash = sourceVersion.ContentHash,
                SizeBytes = sourceVersion.SizeBytes,
                CompressedSizeBytes = sourceVersion.CompressedSizeBytes ?? sourceVersion.SizeBytes,
                UploadPending = _configuration.AsyncUploadEnabled
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error promoting version");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "PromoteVersion",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/version/promote",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of AdminCleanup operation.
    /// Cleans up old save versions based on age and category filters.
    /// </summary>
    public async Task<(StatusCodes, AdminCleanupResponse?)> AdminCleanupAsync(AdminCleanupRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing AdminCleanup: dryRun={DryRun}, olderThanDays={Days}, category={Category}",
            body.DryRun, body.OlderThanDays, body.Category);

        try
        {
            var slotStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var versionStore = _stateStoreFactory.GetQueryableStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);

            var ownerType = body.OwnerType.ToString();
            var category = body.Category.ToString();
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-body.OlderThanDays);

            // Query slots matching filters
            var slots = await slotStore.QueryAsync(
                s => s.OwnerType == ownerType && s.Category == category,
                cancellationToken);

            var versionsDeleted = 0;
            var slotsDeleted = 0;
            long bytesFreed = 0;

            foreach (var slot in slots)
            {
                // Query versions for this slot
                var versions = await versionStore.QueryAsync(
                    v => v.SlotId == slot.SlotId,
                    cancellationToken);

                var versionsToDelete = versions
                    .Where(v => !v.IsPinned && v.CreatedAt < cutoffDate)
                    .ToList();

                foreach (var version in versionsToDelete)
                {
                    bytesFreed += version.SizeBytes;
                    versionsDeleted++;

                    if (!body.DryRun)
                    {
                        var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, version.VersionNumber);
                        await versionStore.DeleteAsync(versionKey, cancellationToken);

                        // Delete asset if exists
                        if (!string.IsNullOrEmpty(version.AssetId))
                        {
                            try
                            {
                                await _assetClient.DeleteAssetAsync(
                                    new DeleteAssetRequest { AssetId = version.AssetId },
                                    cancellationToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to delete asset {AssetId}", version.AssetId);
                            }
                        }
                    }
                }

                // Check if slot is now empty
                var remainingVersions = versions.Count - versionsToDelete.Count;
                if (remainingVersions == 0)
                {
                    slotsDeleted++;
                    if (!body.DryRun)
                    {
                        await slotStore.DeleteAsync(slot.GetStateKey(), cancellationToken);
                    }
                }
                else if (!body.DryRun && versionsToDelete.Count > 0)
                {
                    // Update slot metadata
                    slot.VersionCount = remainingVersions;
                    slot.TotalSizeBytes -= bytesFreed;
                    slot.UpdatedAt = DateTimeOffset.UtcNow;
                    await slotStore.SaveAsync(slot.GetStateKey(), slot, cancellationToken: cancellationToken);
                }
            }

            _logger.LogInformation(
                "AdminCleanup {Mode}: deleted {Versions} versions, {Slots} slots, freed {Bytes} bytes",
                body.DryRun ? "preview" : "executed",
                versionsDeleted, slotsDeleted, bytesFreed);

            return (StatusCodes.OK, new AdminCleanupResponse
            {
                VersionsDeleted = versionsDeleted,
                SlotsDeleted = slotsDeleted,
                BytesFreed = bytesFreed,
                DryRun = body.DryRun
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing AdminCleanup operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "AdminCleanup",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/admin/cleanup",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of AdminStats operation.
    /// Returns aggregated statistics about save data storage.
    /// </summary>
    public async Task<(StatusCodes, AdminStatsResponse?)> AdminStatsAsync(AdminStatsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing AdminStats with groupBy={GroupBy}", body.GroupBy);

        try
        {
            var slotStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var versionStore = _stateStoreFactory.GetQueryableStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);

            // Get all slots
            var slots = await slotStore.QueryAsync(_ => true, cancellationToken);
            var slotList = slots.ToList();

            // Get all versions
            var versions = await versionStore.QueryAsync(_ => true, cancellationToken);
            var versionList = versions.ToList();

            // Calculate totals
            var totalSlots = slotList.Count;
            var totalVersions = versionList.Count;
            var totalSizeBytes = versionList.Sum(v => v.SizeBytes);
            var pinnedVersions = versionList.Count(v => v.IsPinned);

            // Create breakdown based on groupBy
            var breakdown = new List<StatsBreakdown>();

            switch (body.GroupBy)
            {
                case AdminStatsRequestGroupBy.Owner_type:
                    var ownerTypeGroups = slotList.GroupBy(s => s.OwnerType);
                    foreach (var group in ownerTypeGroups)
                    {
                        var slotIds = group.Select(s => s.SlotId).ToHashSet();
                        var groupVersions = versionList.Where(v => slotIds.Contains(v.SlotId)).ToList();
                        breakdown.Add(new StatsBreakdown
                        {
                            Key = group.Key,
                            Slots = group.Count(),
                            Versions = groupVersions.Count,
                            SizeBytes = groupVersions.Sum(v => v.SizeBytes)
                        });
                    }
                    break;

                case AdminStatsRequestGroupBy.Category:
                    var categoryGroups = slotList.GroupBy(s => s.Category);
                    foreach (var group in categoryGroups)
                    {
                        var slotIds = group.Select(s => s.SlotId).ToHashSet();
                        var groupVersions = versionList.Where(v => slotIds.Contains(v.SlotId)).ToList();
                        breakdown.Add(new StatsBreakdown
                        {
                            Key = group.Key,
                            Slots = group.Count(),
                            Versions = groupVersions.Count,
                            SizeBytes = groupVersions.Sum(v => v.SizeBytes)
                        });
                    }
                    break;

                case AdminStatsRequestGroupBy.Schema_version:
                    var schemaGroups = versionList.GroupBy(v => v.SchemaVersion ?? "unversioned");
                    foreach (var group in schemaGroups)
                    {
                        var slotIds = group.Select(v => v.SlotId).Distinct().ToHashSet();
                        breakdown.Add(new StatsBreakdown
                        {
                            Key = group.Key,
                            Slots = slotIds.Count,
                            Versions = group.Count(),
                            SizeBytes = group.Sum(v => v.SizeBytes)
                        });
                    }
                    break;
            }

            return (StatusCodes.OK, new AdminStatsResponse
            {
                TotalSlots = totalSlots,
                TotalVersions = totalVersions,
                TotalSizeBytes = totalSizeBytes,
                PinnedVersions = pinnedVersions,
                Breakdown = breakdown
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing AdminStats operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "AdminStats",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/admin/stats",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    #region Helper Methods

    /// <summary>
    /// Gets the default maximum versions for a save category.
    /// </summary>
    private int GetDefaultMaxVersions(SaveCategory category)
    {
        return category switch
        {
            SaveCategory.QUICK_SAVE => _configuration.DefaultMaxVersionsQuickSave,
            SaveCategory.AUTO_SAVE => _configuration.DefaultMaxVersionsAutoSave,
            SaveCategory.MANUAL_SAVE => _configuration.DefaultMaxVersionsManualSave,
            SaveCategory.CHECKPOINT => _configuration.DefaultMaxVersionsCheckpoint,
            SaveCategory.STATE_SNAPSHOT => _configuration.DefaultMaxVersionsStateSnapshot,
            _ => _configuration.DefaultMaxVersionsManualSave
        };
    }

    /// <summary>
    /// Gets the default compression type for a save category.
    /// </summary>
    private CompressionType GetDefaultCompressionType(SaveCategory category)
    {
        // Parse default compression by category if configured
        // Format: "QUICK_SAVE:NONE,AUTO_SAVE:GZIP,..."
        if (!string.IsNullOrEmpty(_configuration.DefaultCompressionByCategory))
        {
            var parts = _configuration.DefaultCompressionByCategory.Split(',');
            foreach (var part in parts)
            {
                var kv = part.Trim().Split(':');
                if (kv.Length == 2 &&
                    Enum.TryParse<SaveCategory>(kv[0].Trim(), out var cat) &&
                    cat == category &&
                    Enum.TryParse<CompressionType>(kv[1].Trim(), out var comp))
                {
                    return comp;
                }
            }
        }

        // Default based on category characteristics
        return category switch
        {
            SaveCategory.QUICK_SAVE => CompressionType.NONE,
            SaveCategory.AUTO_SAVE => CompressionType.GZIP,
            SaveCategory.MANUAL_SAVE => CompressionType.GZIP,
            SaveCategory.CHECKPOINT => CompressionType.GZIP,
            SaveCategory.STATE_SNAPSHOT => CompressionType.BROTLI,
            _ => Enum.TryParse<CompressionType>(_configuration.DefaultCompressionType, out var comp)
                ? comp
                : CompressionType.GZIP
        };
    }

    /// <summary>
    /// Converts internal SaveSlotMetadata to API SlotResponse.
    /// </summary>
    private static SlotResponse ToSlotResponse(SaveSlotMetadata slot)
    {
        return new SlotResponse
        {
            SlotId = Guid.Parse(slot.SlotId),
            OwnerId = Guid.Parse(slot.OwnerId),
            OwnerType = Enum.Parse<OwnerType>(slot.OwnerType),
            SlotName = slot.SlotName,
            Category = Enum.Parse<SaveCategory>(slot.Category),
            MaxVersions = slot.MaxVersions,
            RetentionDays = slot.RetentionDays,
            CompressionType = Enum.Parse<CompressionType>(slot.CompressionType),
            VersionCount = slot.VersionCount,
            LatestVersion = slot.LatestVersion,
            TotalSizeBytes = slot.TotalSizeBytes,
            CreatedAt = slot.CreatedAt,
            UpdatedAt = slot.UpdatedAt,
            Metadata = slot.Metadata
        };
    }

    /// <summary>
    /// Converts internal SaveVersionManifest to API VersionResponse.
    /// </summary>
    private static VersionResponse MapToVersionResponse(SaveVersionManifest version)
    {
        return new VersionResponse
        {
            VersionNumber = version.VersionNumber,
            AssetId = !string.IsNullOrEmpty(version.AssetId) && Guid.TryParse(version.AssetId, out var assetGuid)
                ? assetGuid
                : Guid.Empty,
            ContentHash = version.ContentHash,
            SizeBytes = version.SizeBytes,
            CompressedSizeBytes = version.CompressedSizeBytes ?? version.SizeBytes,
            SchemaVersion = version.SchemaVersion,
            Pinned = version.IsPinned,
            CheckpointName = version.CheckpointName,
            CreatedAt = version.CreatedAt,
            // Filter out null metadata values rather than coercing to empty string
            Metadata = version.Metadata
                .Where(kv => kv.Value != null)
                .ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value?.ToString() ?? throw new InvalidOperationException(
                        $"Metadata value became null after filter for key '{kv.Key}'"))
        };
    }

    /// <summary>
    /// Finds a slot by owner and name (without gameId).
    /// Used by version operations that don't include gameId in request.
    /// </summary>
    private async Task<SaveSlotMetadata?> FindSlotByOwnerAndNameAsync(
        string ownerId,
        string ownerType,
        string slotName,
        CancellationToken cancellationToken)
    {
        var slotQueryStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        var matchingSlots = await slotQueryStore.QueryAsync(
            s => s.OwnerId == ownerId &&
                s.OwnerType == ownerType &&
                s.SlotName == slotName,
            cancellationToken);

        return matchingSlots.FirstOrDefault();
    }

    #endregion

    #region Schema Migration Operations

    /// <summary>
    /// Implementation of RegisterSchema operation.
    /// Registers a schema version with optional migration patch.
    /// </summary>
    public async Task<(StatusCodes, SchemaResponse?)> RegisterSchemaAsync(RegisterSchemaRequest body, CancellationToken cancellationToken)
    {
        return await _saveMigrationHandler.RegisterSchemaAsync(body, cancellationToken);
    }

    /// <summary>
    /// Implementation of ListSchemas operation.
    /// Lists all schemas registered for a namespace.
    /// </summary>
    public async Task<(StatusCodes, ListSchemasResponse?)> ListSchemasAsync(ListSchemasRequest body, CancellationToken cancellationToken)
    {
        return await _saveMigrationHandler.ListSchemasAsync(body, cancellationToken);
    }

    /// <summary>
    /// Implementation of MigrateSave operation.
    /// Migrates a save from its current schema version to a target version.
    /// Delegates to ISaveMigrationHandler for improved testability.
    /// </summary>
    public async Task<(StatusCodes, MigrateSaveResponse?)> MigrateSaveAsync(MigrateSaveRequest body, CancellationToken cancellationToken)
    {
        return await _saveMigrationHandler.MigrateSaveAsync(body, cancellationToken);
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Overrides the default IBannouService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering SaveLoad service permissions...");
        await SaveLoadPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
    }

    #endregion
}
