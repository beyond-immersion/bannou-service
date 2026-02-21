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
    // Topic constants for event publishing (per FOUNDATION TENETS - Event-Driven Architecture)
    private const string SAVE_SLOT_CREATED_TOPIC = "save-slot.created";
    private const string SAVE_SLOT_UPDATED_TOPIC = "save-slot.updated";
    private const string SAVE_SLOT_DELETED_TOPIC = "save-slot.deleted";
    private const string SAVE_CREATED_TOPIC = "save.created";
    private const string SAVE_LOADED_TOPIC = "save.loaded";
    private const string SAVE_VERSION_PINNED_TOPIC = "save.version-pinned";
    private const string SAVE_VERSION_UNPINNED_TOPIC = "save.version-unpinned";
    private const string SAVE_VERSION_DELETED_TOPIC = "save.version-deleted";
    private const string SAVE_QUEUED_TOPIC = "save.queued";

    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IDistributedLockProvider _lockProvider;
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
        IDistributedLockProvider lockProvider,
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
        _lockProvider = lockProvider;
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
        _logger.LogDebug(
            "Creating save slot for game {GameId}, owner {OwnerType}:{OwnerId}, slot {SlotName}",
            body.GameId, body.OwnerType, body.OwnerId, body.SlotName);

        var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        var ownerType = body.OwnerType.ToString();
        var ownerId = body.OwnerId.ToString();

        // Check if slot already exists
        var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, ownerType, ownerId, body.SlotName);
        var existingSlot = await slotStore.GetAsync(slotKey, cancellationToken);
        if (existingSlot != null)
        {
            _logger.LogDebug("Slot already exists: {SlotKey}", slotKey);
            return (StatusCodes.Conflict, null);
        }

        // Check max slots per owner limit
        var queryableSlotStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        var ownerSlots = await queryableSlotStore.QueryAsync(
            s => s.OwnerId == body.OwnerId && s.OwnerType == body.OwnerType,
            cancellationToken);
        if (ownerSlots.Count() >= _configuration.MaxSlotsPerOwner)
        {
            _logger.LogDebug(
                "Owner {OwnerId} has reached maximum slot limit of {MaxSlots}",
                body.OwnerId, _configuration.MaxSlotsPerOwner);
            return (StatusCodes.BadRequest, null);
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
            SlotId = slotId,
            GameId = body.GameId,
            OwnerId = body.OwnerId,
            OwnerType = body.OwnerType,
            SlotName = body.SlotName,
            Category = category,
            MaxVersions = (int)maxVersions,
            RetentionDays = body.RetentionDays > 0 ? body.RetentionDays : null,
            CompressionType = compressionType,
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
        await PublishSaveSlotCreatedEventAsync(slot, cancellationToken);

        _logger.LogDebug("Created slot {SlotId} for {OwnerType}:{OwnerId}", slotId, ownerType, ownerId);

        return (StatusCodes.OK, ToSlotResponse(slot));
    }

    /// <summary>
    /// Gets a save slot by its identifiers.
    /// </summary>
    public async Task<(StatusCodes, SlotResponse?)> GetSlotAsync(GetSlotRequest body, CancellationToken cancellationToken)
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

    /// <summary>
    /// Lists save slots for an owner with optional filtering.
    /// </summary>
    public async Task<(StatusCodes, ListSlotsResponse?)> ListSlotsAsync(ListSlotsRequest body, CancellationToken cancellationToken)
    {
        var queryableStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        var gameId = body.GameId;
        // SaveSlotMetadata.OwnerId/OwnerType/Category are now Guid/enum types - compare properly
        var bodyCategory = body.Category;

        // Query with LINQ expression - use proper type comparisons
        var slots = await queryableStore.QueryAsync(
            s => s.GameId == gameId &&
                s.OwnerId == body.OwnerId &&
                s.OwnerType == body.OwnerType &&
                (bodyCategory == default || s.Category == bodyCategory),
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

    /// <summary>
    /// Deletes a save slot and all its versions.
    /// </summary>
    public async Task<(StatusCodes, DeleteSlotResponse?)> DeleteSlotAsync(DeleteSlotRequest body, CancellationToken cancellationToken)
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

        // Delete all versions for this slot - SlotId is Guid, convert to string for state key
        var deletedVersions = 0;
        for (var v = 1; v <= (slot.LatestVersion ?? 0); v++)
        {
            var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), v);
            await versionStore.DeleteAsync(versionKey, cancellationToken);
            deletedVersions++;
        }

        await slotStore.DeleteAsync(slotKey, cancellationToken);
        await PublishSaveSlotDeletedEventAsync(slot, "User requested deletion", cancellationToken);

        _logger.LogDebug("Deleted slot {SlotId} with {VersionCount} versions", slot.SlotId, deletedVersions);

        var response = new DeleteSlotResponse
        {
            Deleted = true,
            VersionsDeleted = deletedVersions,
            BytesFreed = slot.TotalSizeBytes
        };

        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Renames a save slot (creates new key, migrates data, deletes old).
    /// </summary>
    public async Task<(StatusCodes, SlotResponse?)> RenameSlotAsync(RenameSlotRequest body, CancellationToken cancellationToken)
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

        // Acquire distributed lock to prevent concurrent rename/modification
        // SlotId is Guid - convert to string for lock
        await using var slotLock = await _lockProvider.LockAsync(
            "save-load-slot", slot.SlotId.ToString(), Guid.NewGuid().ToString(), 30, cancellationToken);
        if (!slotLock.Success)
        {
            _logger.LogDebug("Could not acquire slot lock for {SlotId} during rename", slot.SlotId);
            return (StatusCodes.Conflict, null);
        }

        // Re-read under lock to ensure consistency
        slot = await slotStore.GetAsync(oldSlotKey, cancellationToken);
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
            _logger.LogDebug("Cannot rename - target slot already exists: {NewSlotName}", body.NewSlotName);
            return (StatusCodes.Conflict, null);
        }

        // Update slot name and save to new key
        slot.SlotName = body.NewSlotName;
        slot.UpdatedAt = DateTimeOffset.UtcNow;
        slot.ETag = Guid.NewGuid().ToString();

        await slotStore.SaveAsync(newSlotKey, slot, cancellationToken: cancellationToken);
        await slotStore.DeleteAsync(oldSlotKey, cancellationToken);
        await PublishSaveSlotUpdatedEventAsync(slot, ["slotName"], cancellationToken);

        _logger.LogDebug("Renamed slot from {OldName} to {NewName}", body.SlotName, body.NewSlotName);

        return (StatusCodes.OK, ToSlotResponse(slot));
    }

    /// <summary>
    /// Bulk deletes multiple save slots.
    /// </summary>
    public async Task<(StatusCodes, BulkDeleteSlotsResponse?)> BulkDeleteSlotsAsync(BulkDeleteSlotsRequest body, CancellationToken cancellationToken)
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
                // SaveSlotMetadata.SlotId is now Guid - compare properly
                var slots = await queryableStore.QueryAsync(
                    s => s.SlotId == slotId && s.GameId == body.GameId,
                    cancellationToken);
                var slot = slots.FirstOrDefault();

                if (slot == null)
                {
                    _logger.LogDebug("Slot {SlotId} not found for bulk delete", slotId);
                    continue;
                }

                // Delete all versions for this slot - SlotId is Guid, convert to string for state key
                for (var v = 1; v <= (slot.LatestVersion ?? 0); v++)
                {
                    var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), v);
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
                _logger.LogDebug(ex, "Failed to delete slot {SlotId}", slotId);
            }
        }

        var response = new BulkDeleteSlotsResponse
        {
            DeletedCount = deletedCount,
            BytesFreed = totalBytesFreed
        };

        _logger.LogDebug(
            "Bulk delete completed: {DeletedCount} slots deleted, {BytesFreed} bytes freed",
            deletedCount, totalBytesFreed);

        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Saves game data to a slot, creating the slot if it doesn't exist.
    /// Uses async upload queue by default for consistent response times.
    /// </summary>
    public async Task<(StatusCodes, SaveResponse?)> SaveAsync(SaveRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Saving to slot {SlotName} for {OwnerType}:{OwnerId} in game {GameId}",
            body.SlotName, body.OwnerType, body.OwnerId, body.GameId);

        // Rate limiting: check saves per minute per owner
        if (_configuration.MaxSavesPerMinute > 0)
        {
            var rateStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.SaveLoadCache);
            var minuteBucket = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmm");
            var rateKey = $"save-rate:{body.GameId}:{body.OwnerType}:{body.OwnerId}:{minuteBucket}";
            var rateStr = await rateStore.GetAsync(rateKey, cancellationToken);
            var saveCount = int.TryParse(rateStr, out var parsedCount) ? parsedCount : 0;

            if (saveCount >= _configuration.MaxSavesPerMinute)
            {
                _logger.LogDebug(
                    "Rate limit exceeded for {OwnerType}:{OwnerId} in game {GameId}: {Count} saves in current minute (max {Max})",
                    body.OwnerType, body.OwnerId, body.GameId, saveCount, _configuration.MaxSavesPerMinute);
                return (StatusCodes.BadRequest, null);
            }

            await rateStore.SaveAsync(rateKey, (saveCount + 1).ToString(),
                options: new StateOptions { Ttl = 120 }, cancellationToken: cancellationToken);
        }

        // Validate save data size against configured maximum
        if (body.Data.Length > _configuration.MaxSaveSizeBytes)
        {
            _logger.LogDebug(
                "Save data size {Size} bytes exceeds maximum {MaxSize} bytes",
                body.Data.Length, _configuration.MaxSaveSizeBytes);
            return (StatusCodes.BadRequest, null);
        }

        // Validate thumbnail if provided
        if (body.Thumbnail != null && body.Thumbnail.Length > 0)
        {
            if (body.Thumbnail.Length > _configuration.ThumbnailMaxSizeBytes)
            {
                _logger.LogDebug(
                    "Thumbnail size {Size} bytes exceeds maximum {MaxSize} bytes",
                    body.Thumbnail.Length, _configuration.ThumbnailMaxSizeBytes);
                return (StatusCodes.BadRequest, null);
            }
        }

        var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
        var hotCacheStore = _stateStoreFactory.GetStore<HotSaveEntry>(StateStoreDefinitions.SaveLoadCache);
        var pendingStore = _stateStoreFactory.GetCacheableStore<PendingUploadEntry>(StateStoreDefinitions.SaveLoadPending);

        // Convert request types to strings for state key construction
        var ownerTypeStr = body.OwnerType.ToString();
        var ownerIdStr = body.OwnerId.ToString();
        var now = DateTimeOffset.UtcNow;

        // Lock the slot to prevent concurrent version number allocation
        var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, ownerTypeStr, ownerIdStr, body.SlotName);
        await using var slotLock = await _lockProvider.LockAsync(
            "save-load-slot", slotKey, Guid.NewGuid().ToString(), 60, cancellationToken);
        if (!slotLock.Success)
        {
            _logger.LogDebug("Could not acquire slot lock for {SlotKey}", slotKey);
            return (StatusCodes.Conflict, null);
        }

        // Get or create slot
        var slot = await slotStore.GetAsync(slotKey, cancellationToken);

        if (slot == null)
        {
            // Auto-create slot with defaults
            // SaveSlotMetadata fields are now proper types (Guid, enums)
            var category = body.Category ?? SaveCategory.MANUAL_SAVE;
            slot = new SaveSlotMetadata
            {
                SlotId = Guid.NewGuid(),
                GameId = body.GameId,
                OwnerId = body.OwnerId,
                OwnerType = body.OwnerType,
                SlotName = body.SlotName,
                Category = category,
                MaxVersions = GetDefaultMaxVersions(category),
                CompressionType = GetDefaultCompressionType(category),
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
            await PublishSaveSlotCreatedEventAsync(slot, cancellationToken);
            _logger.LogDebug("Auto-created slot {SlotId} for {SlotName}", slot.SlotId, body.SlotName);
        }

        // Check total storage quota per owner
        if (slot.TotalSizeBytes + body.Data.Length > _configuration.MaxTotalSizeBytesPerOwner)
        {
            _logger.LogDebug(
                "Owner {OwnerId} would exceed storage quota: current {CurrentBytes} + new {NewBytes} > max {MaxBytes}",
                ownerIdStr, slot.TotalSizeBytes, body.Data.Length, _configuration.MaxTotalSizeBytesPerOwner);
            return (StatusCodes.BadRequest, null);
        }

        // Conflict detection: check if another device saved within the detection window
        // SaveSlotMetadata.SlotId is now Guid - convert to string for state key
        if (_configuration.ConflictDetectionEnabled && !string.IsNullOrEmpty(body.DeviceId) && slot.LatestVersion.HasValue)
        {
            var latestVersionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), slot.LatestVersion.Value);
            var latestVersion = await versionStore.GetAsync(latestVersionKey, cancellationToken);
            if (latestVersion != null &&
                !string.IsNullOrEmpty(latestVersion.DeviceId) &&
                latestVersion.DeviceId != body.DeviceId)
            {
                var timeSinceLastSave = now - latestVersion.CreatedAt;
                if (timeSinceLastSave < TimeSpan.FromMinutes(_configuration.ConflictDetectionWindowMinutes))
                {
                    _logger.LogDebug(
                        "Potential save conflict detected: device {NewDevice} saving within {Minutes} minutes of device {OldDevice}",
                        body.DeviceId, _configuration.ConflictDetectionWindowMinutes, latestVersion.DeviceId);
                }
            }
        }

        // Get raw data
        var rawData = body.Data;
        var originalSize = rawData.Length;

        // Compute hash of original data
        var contentHash = Hashing.ContentHasher.ComputeHash(rawData);

        // Determine compression - SaveSlotMetadata.CompressionType is now an enum
        var compressionType = slot.CompressionType;
        var compressedData = rawData;
        var compressedSize = originalSize;

        if (Compression.CompressionHelper.ShouldCompress(originalSize, _configuration.AutoCompressThresholdBytes))
        {
            var compressionLevel = compressionType == CompressionType.BROTLI
                ? _configuration.BrotliCompressionLevel
                : compressionType == CompressionType.GZIP
                    ? _configuration.GzipCompressionLevel
                    : (int?)null;
            compressedData = Compression.CompressionHelper.Compress(rawData, compressionType, compressionLevel);
            compressedSize = compressedData.Length;
        }
        else
        {
            compressionType = CompressionType.NONE;
        }

        var compressionRatio = Compression.CompressionHelper.CalculateCompressionRatio(originalSize, compressedSize);

        // Determine next version number
        var nextVersion = (slot.LatestVersion ?? 0) + 1;

        // Create version manifest - SaveVersionManifest fields are now proper types
        var shouldPin = !string.IsNullOrEmpty(body.PinAsCheckpoint);
        var manifest = new SaveVersionManifest
        {
            SlotId = slot.SlotId,
            VersionNumber = nextVersion,
            ContentHash = contentHash,
            SizeBytes = originalSize,
            CompressedSizeBytes = compressedSize,
            CompressionType = compressionType,
            IsPinned = shouldPin,
            CheckpointName = shouldPin ? body.PinAsCheckpoint : null,
            IsDelta = false,
            DeviceId = body.DeviceId,
            SchemaVersion = body.SchemaVersion,
            Metadata = new Dictionary<string, object>(),
            UploadStatus = _configuration.AsyncUploadEnabled ? UploadStatus.PENDING : UploadStatus.COMPLETE,
            CreatedAt = now,
            ETag = Guid.NewGuid().ToString()
        };

        // Store version manifest - SlotId is Guid, convert to string for state key
        var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), nextVersion);
        await versionStore.SaveAsync(versionKey, manifest, cancellationToken: cancellationToken);

        // Store in hot cache for immediate load availability - HotSaveEntry fields are now proper types
        var hotEntry = new HotSaveEntry
        {
            SlotId = slot.SlotId,
            VersionNumber = nextVersion,
            Data = Convert.ToBase64String(compressedData),
            ContentHash = contentHash,
            IsCompressed = compressionType != CompressionType.NONE,
            CompressionType = compressionType,
            SizeBytes = compressedSize,
            CachedAt = now,
            IsDelta = false
        };
        var hotKey = HotSaveEntry.GetStateKey(slot.SlotId.ToString(), nextVersion);
        await hotCacheStore.SaveAsync(hotKey, hotEntry, cancellationToken: cancellationToken);

        // Also update the "latest" hot cache pointer - SlotId is Guid, convert to string
        var latestHotKey = HotSaveEntry.GetLatestKey(slot.SlotId.ToString());
        await hotCacheStore.SaveAsync(latestHotKey, hotEntry, cancellationToken: cancellationToken);

        var uploadPending = false;

        if (_configuration.AsyncUploadEnabled)
        {
            // Queue for async upload - PendingUploadEntry fields are now proper types
            var uploadId = Guid.NewGuid();
            var pendingEntry = new PendingUploadEntry
            {
                UploadId = uploadId,
                SlotId = slot.SlotId,
                VersionNumber = nextVersion,
                GameId = body.GameId,
                OwnerId = body.OwnerId,
                OwnerType = body.OwnerType,
                Data = Convert.ToBase64String(compressedData),
                ContentHash = contentHash,
                CompressionType = compressionType,
                SizeBytes = originalSize,
                CompressedSizeBytes = compressedSize,
                IsDelta = false,
                ThumbnailData = body.Thumbnail != null ? Convert.ToBase64String(body.Thumbnail) : null,
                AttemptCount = 0,
                QueuedAt = now,
                Priority = GetUploadPriority(slot.Category)
            };
            var pendingKey = PendingUploadEntry.GetStateKey(uploadId.ToString());
            await pendingStore.SaveAsync(pendingKey, pendingEntry, cancellationToken: cancellationToken);
            // Add to tracking set for Redis-based queue processing
            await pendingStore.AddToSetAsync(Processing.SaveUploadWorker.PendingUploadIdsSetKey, uploadId.ToString(), cancellationToken: cancellationToken);
            uploadPending = true;

            await _messageBus.TryPublishAsync(SAVE_QUEUED_TOPIC, new SaveQueuedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                SlotId = slot.SlotId,
                SlotName = slot.SlotName,
                VersionNumber = nextVersion,
                OwnerId = body.OwnerId,
                OwnerType = body.OwnerType,
                SizeBytes = compressedSize
            }, cancellationToken: cancellationToken);

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
        await PublishSaveSlotUpdatedEventAsync(slot, ["latestVersion", "versionCount", "totalSizeBytes"], cancellationToken);

        // Rolling cleanup if needed
        var versionsCleanedUp = await _versionCleanupManager.PerformRollingCleanupAsync(slot, versionStore, hotCacheStore, cancellationToken);

        _logger.LogDebug(
            "Saved version {Version} to slot {SlotId}, size {Size} bytes (compressed {CompressedSize}), upload pending: {Pending}",
            nextVersion, slot.SlotId, originalSize, compressedSize, uploadPending);

        // SaveSlotMetadata.SlotId is now Guid - use directly
        var response = new SaveResponse
        {
            SlotId = slot.SlotId,
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
        _logger.LogDebug(
            "Loading from slot {SlotName} for {OwnerType}:{OwnerId} in game {GameId}",
            body.SlotName, body.OwnerType, body.OwnerId, body.GameId);

        var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
        var hotCacheStore = _stateStoreFactory.GetStore<HotSaveEntry>(StateStoreDefinitions.SaveLoadCache);

        // Convert request types to strings for state key construction
        var ownerTypeStr = body.OwnerType.ToString();
        var ownerIdStr = body.OwnerId.ToString();

        // Get the slot
        var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, ownerTypeStr, ownerIdStr, body.SlotName);
        var slot = await slotStore.GetAsync(slotKey, cancellationToken);

        if (slot == null)
        {
            _logger.LogDebug("Slot not found: {SlotKey}", slotKey);
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
                _logger.LogDebug("Checkpoint {CheckpointName} not found in slot {SlotId}", body.CheckpointName, slot.SlotId);
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
                _logger.LogDebug("No versions exist in slot {SlotId}", slot.SlotId);
                return (StatusCodes.NotFound, null);
            }
        }

        // Try hot cache first - SlotId is Guid, convert to string for state key
        var hotKey = HotSaveEntry.GetStateKey(slot.SlotId.ToString(), targetVersion);
        var hotEntry = await hotCacheStore.GetAsync(hotKey, cancellationToken);

        byte[] decompressedData;
        string contentHash;
        SaveVersionManifest? manifest = null;

        if (hotEntry != null)
        {
            _logger.LogDebug("Hot cache hit for slot {SlotId} version {Version}", slot.SlotId, targetVersion);

            // Decompress if needed - HotSaveEntry.CompressionType is now a nullable enum
            var compressedData = Convert.FromBase64String(hotEntry.Data);
            var compressionType = hotEntry.CompressionType ?? CompressionType.NONE;

            decompressedData = hotEntry.IsCompressed
                ? Compression.CompressionHelper.Decompress(compressedData, compressionType)
                : compressedData;

            contentHash = hotEntry.ContentHash;

            // Get manifest for additional metadata if requested
            if (body.IncludeMetadata)
            {
                var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), targetVersion);
                manifest = await versionStore.GetAsync(versionKey, cancellationToken);
            }
        }
        else
        {
            _logger.LogDebug("Hot cache miss for slot {SlotId} version {Version}, fetching from storage", slot.SlotId, targetVersion);

            // Get version manifest - SlotId is Guid, convert to string for state key
            var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), targetVersion);
            manifest = await versionStore.GetAsync(versionKey, cancellationToken);

            if (manifest == null)
            {
                _logger.LogDebug("Version {Version} not found in slot {SlotId}", targetVersion, slot.SlotId);
                return (StatusCodes.NotFound, null);
            }

            // Load from Asset service if available - AssetId is now Guid?
            if (manifest.AssetId.HasValue && manifest.AssetId.Value != Guid.Empty)
            {
                var assetResponse = await _versionDataLoader.LoadFromAssetServiceAsync(manifest.AssetId.Value.ToString(), cancellationToken);
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

                // SaveVersionManifest.CompressionType is now an enum - use directly
                decompressedData = manifest.CompressionType != CompressionType.NONE
                    ? Compression.CompressionHelper.Decompress(assetResponse, manifest.CompressionType)
                    : assetResponse;
            }
            else
            {
                // Asset not yet uploaded - version should be in pending upload queue
                // or the upload failed. Return not found.
                _logger.LogDebug("Version {Version} has no asset ID and is not in hot cache", targetVersion);
                return (StatusCodes.NotFound, null);
            }

            contentHash = manifest.ContentHash;

            // Re-cache in hot store - SlotId is Guid, convert to string
            await _versionDataLoader.CacheInHotStoreAsync(slot.SlotId.ToString(), targetVersion, decompressedData, contentHash, manifest, hotCacheStore, cancellationToken);
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

        // SaveSlotMetadata.SlotId is now Guid - use directly
        var response = new LoadResponse
        {
            SlotId = slot.SlotId,
            VersionNumber = targetVersion,
            Data = decompressedData,
            ContentHash = contentHash,
            SchemaVersion = manifest?.SchemaVersion,
            DisplayName = manifest?.CheckpointName,
            Pinned = manifest?.IsPinned ?? false,
            CheckpointName = manifest?.CheckpointName,
            CreatedAt = manifest?.CreatedAt ?? DateTimeOffset.MinValue
        };

        _logger.LogDebug("Loaded version {Version} from slot {SlotId}, {Size} bytes",
            targetVersion, slot.SlotId, decompressedData.Length);

        return (StatusCodes.OK, response);
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

        // Check if delta saves are enabled
        if (!_configuration.DeltaSavesEnabled)
        {
            _logger.LogDebug("Delta saves are disabled");
            return (StatusCodes.BadRequest, null);
        }

        // Find the slot - convert types to strings for state key
        var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        var ownerTypeStr = body.OwnerType.ToString();
        var ownerIdStr = body.OwnerId.ToString();
        var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, ownerTypeStr, ownerIdStr, body.SlotName);
        var slot = await slotStore.GetAsync(slotKey, cancellationToken);

        if (slot == null)
        {
            _logger.LogDebug("Slot not found: {SlotKey}", slotKey);
            return (StatusCodes.NotFound, null);
        }

        // Lock the slot to prevent concurrent version number allocation
        // SlotId is Guid, convert to string for lock
        await using var slotLock = await _lockProvider.LockAsync(
            "save-load-slot", slot.SlotId.ToString(), Guid.NewGuid().ToString(), 60, cancellationToken);
        if (!slotLock.Success)
        {
            _logger.LogDebug("Could not acquire slot lock for {SlotId}", slot.SlotId);
            return (StatusCodes.Conflict, null);
        }

        // Re-read slot under lock for accurate version number
        slot = await slotStore.GetAsync(slotKey, cancellationToken) ?? slot;

        // Get the base version - SlotId is Guid, convert to string for state key
        var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
        var baseVersionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), body.BaseVersion);
        var baseVersion = await versionStore.GetAsync(baseVersionKey, cancellationToken);

        if (baseVersion == null)
        {
            _logger.LogDebug("Base version {Version} not found", body.BaseVersion);
            return (StatusCodes.NotFound, null);
        }

        // Validate the delta
        var deltaProcessor = new DeltaProcessor(
            _logger as ILogger<DeltaProcessor> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DeltaProcessor>.Instance,
            _configuration.MigrationMaxPatchOperations);
        var algorithmEnum = body.Algorithm ?? DeltaAlgorithm.JSON_PATCH;
        var algorithm = algorithmEnum.ToString();
        if (!deltaProcessor.ValidateDelta(body.Delta, algorithm))
        {
            _logger.LogDebug("Invalid delta provided");
            return (StatusCodes.BadRequest, null);
        }

        // Calculate delta chain length
        var chainLength = 1;
        var currentVersion = baseVersion;
        while (currentVersion.IsDelta && currentVersion.BaseVersionNumber.HasValue)
        {
            chainLength++;
            var prevKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), currentVersion.BaseVersionNumber.Value);
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
            _logger.LogDebug(
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
                _logger.LogDebug(
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
            // Configuration already provides typed enum (T25 compliant)
            compressionTypeEnum = _configuration.DefaultCompressionType;
            var deltaCompressionLevel = compressionTypeEnum == CompressionType.BROTLI
                ? _configuration.BrotliCompressionLevel
                : compressionTypeEnum == CompressionType.GZIP
                    ? _configuration.GzipCompressionLevel
                    : (int?)null;
            compressedDelta = Compression.CompressionHelper.Compress(body.Delta, compressionTypeEnum, deltaCompressionLevel);
        }

        // Store in hot cache - HotSaveEntry fields are now proper types
        var hotCacheStore = _stateStoreFactory.GetStore<HotSaveEntry>(StateStoreDefinitions.SaveLoadCache);
        var hotEntry = new HotSaveEntry
        {
            SlotId = slot.SlotId,
            VersionNumber = newVersionNumber,
            Data = Convert.ToBase64String(compressedDelta),
            ContentHash = contentHash,
            IsCompressed = compressionTypeEnum != CompressionType.NONE,
            CompressionType = compressionTypeEnum,
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

        // Create version manifest - SaveVersionManifest fields are now proper types
        var manifest = new SaveVersionManifest
        {
            SlotId = slot.SlotId,
            VersionNumber = newVersionNumber,
            ContentHash = contentHash,
            SizeBytes = deltaSize,
            CompressedSizeBytes = compressedDelta.Length,
            CompressionType = compressionTypeEnum,
            IsDelta = true,
            BaseVersionNumber = body.BaseVersion,
            DeltaAlgorithm = algorithmEnum,
            DeviceId = body.DeviceId,
            SchemaVersion = body.SchemaVersion,
            Metadata = body.Metadata?.ToDictionary(kv => kv.Key, kv => (object)kv.Value) ?? new Dictionary<string, object>(),
            CreatedAt = DateTimeOffset.UtcNow,
            UploadStatus = _configuration.AsyncUploadEnabled ? UploadStatus.PENDING : UploadStatus.COMPLETE
        };

        // Save manifest and update slot
        await versionStore.SaveAsync(manifest.GetStateKey(), manifest, cancellationToken: cancellationToken);
        await slotStore.SaveAsync(slotKey, slot, cancellationToken: cancellationToken);

        // Queue for async upload if enabled - PendingUploadEntry fields are now proper types
        if (_configuration.AsyncUploadEnabled)
        {
            var pendingStore = _stateStoreFactory.GetCacheableStore<PendingUploadEntry>(StateStoreDefinitions.SaveLoadPending);
            var uploadId = Guid.NewGuid();
            var pendingEntry = new PendingUploadEntry
            {
                UploadId = uploadId,
                SlotId = slot.SlotId,
                VersionNumber = newVersionNumber,
                GameId = slot.GameId,
                OwnerId = slot.OwnerId,
                OwnerType = slot.OwnerType,
                Data = Convert.ToBase64String(compressedDelta),
                ContentHash = contentHash,
                CompressionType = compressionTypeEnum,
                SizeBytes = deltaSize,
                CompressedSizeBytes = compressedDelta.Length,
                IsDelta = true,
                BaseVersionNumber = body.BaseVersion,
                DeltaAlgorithm = algorithmEnum,
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
            await pendingStore.AddToSetAsync(Processing.SaveUploadWorker.PendingUploadIdsSetKey, uploadId.ToString(), cancellationToken: cancellationToken);
        }

        // Publish event - SaveSlotMetadata fields are now proper types
        var createdEvent = new SaveCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SlotId = slot.SlotId,
            SlotName = slot.SlotName,
            VersionNumber = newVersionNumber,
            OwnerId = slot.OwnerId,
            OwnerType = slot.OwnerType,
            Category = slot.Category,
            SizeBytes = deltaSize,
            SchemaVersion = body.SchemaVersion,
            Pinned = false
        };
        await _messageBus.TryPublishAsync(SAVE_CREATED_TOPIC, createdEvent, cancellationToken: cancellationToken);

        // Calculate compression savings
        var compressionSavings = estimatedFullSize > 0 ? 1.0 - ((double)deltaSize / estimatedFullSize) : 0;

        // SaveSlotMetadata.SlotId is now Guid - use directly
        return (StatusCodes.OK, new SaveDeltaResponse
        {
            SlotId = slot.SlotId,
            VersionNumber = newVersionNumber,
            BaseVersion = body.BaseVersion,
            DeltaSizeBytes = deltaSize,
            EstimatedFullSizeBytes = estimatedFullSize,
            ChainLength = chainLength + 1,
            CompressionSavings = compressionSavings,
            CreatedAt = manifest.CreatedAt
        });
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

        // Find the slot
        var slot = await FindSlotByOwnerAndNameAsync(
            body.OwnerId,
            body.OwnerType,
            body.SlotName,
            cancellationToken);

        if (slot == null)
        {
            _logger.LogDebug("Slot not found: {SlotName}", body.SlotName);
            return (StatusCodes.NotFound, null);
        }

        // Get the target version (0 or null means latest)
        var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
        var versionNumber = (body.VersionNumber == null || body.VersionNumber == 0)
            ? (slot.LatestVersion ?? 0)
            : body.VersionNumber.Value;
        if (versionNumber == 0)
        {
            _logger.LogDebug("Slot {SlotName} has no versions", body.SlotName);
            return (StatusCodes.NotFound, null);
        }

        var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), versionNumber);
        var version = await versionStore.GetAsync(versionKey, cancellationToken);

        if (version == null)
        {
            _logger.LogDebug("Version {Version} not found", versionNumber);
            return (StatusCodes.NotFound, null);
        }

        byte[]? data;

        if (!version.IsDelta)
        {
            // Not a delta, load directly - SlotId is Guid, convert to string
            data = await _versionDataLoader.LoadVersionDataAsync(slot.SlotId.ToString(), version, cancellationToken);
        }
        else
        {
            // Delta version - need to reconstruct - SlotId is Guid, convert to string
            data = await _versionDataLoader.ReconstructFromDeltaChainAsync(slot.SlotId.ToString(), version, versionStore, cancellationToken);
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

        // Publish load event - SaveSlotMetadata.SlotId is now Guid
        var loadEvent = new SaveLoadedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SlotId = slot.SlotId,
            SlotName = slot.SlotName,
            VersionNumber = (int)versionNumber,
            OwnerId = body.OwnerId,
            OwnerType = body.OwnerType
        };
        await _messageBus.TryPublishAsync(SAVE_LOADED_TOPIC, loadEvent, cancellationToken: cancellationToken);

        return (StatusCodes.OK, new LoadResponse
        {
            SlotId = slot.SlotId,
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

    /// <summary>
    /// Collapses a chain of delta versions into a single full snapshot.
    /// Useful for reducing load latency or before deleting base versions.
    /// </summary>
    public async Task<(StatusCodes, SaveResponse?)> CollapseDeltasAsync(CollapseDeltasRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Collapsing deltas for slot {SlotName} to version {Version}",
            body.SlotName, body.VersionNumber ?? -1);

        // Find the slot
        var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        var ownerType = body.OwnerType.ToString();
        var ownerId = body.OwnerId.ToString();
        var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, ownerType, ownerId, body.SlotName);
        var slot = await slotStore.GetAsync(slotKey, cancellationToken);

        if (slot == null)
        {
            _logger.LogDebug("Slot not found: {SlotKey}", slotKey);
            return (StatusCodes.NotFound, null);
        }

        // Get the target version with ETag for optimistic concurrency
        var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
        var versionToFetch = body.VersionNumber ?? slot.LatestVersion;
        if (!versionToFetch.HasValue)
        {
            _logger.LogDebug("No version specified and slot has no latest version");
            return (StatusCodes.NotFound, null);
        }
        // SlotId is Guid - convert to string for state key
        var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), versionToFetch.Value);
        var (targetVersion, versionEtag) = await versionStore.GetWithETagAsync(versionKey, cancellationToken);

        if (targetVersion == null)
        {
            _logger.LogDebug("Target version {Version} not found", versionToFetch.Value);
            return (StatusCodes.NotFound, null);
        }

        // If it's not a delta, nothing to collapse
        if (!targetVersion.IsDelta)
        {
            _logger.LogDebug("Version {Version} is already a full snapshot", targetVersion.VersionNumber);
            return (StatusCodes.OK, new SaveResponse
            {
                SlotId = slot.SlotId,
                VersionNumber = targetVersion.VersionNumber,
                SizeBytes = targetVersion.SizeBytes,
                CreatedAt = targetVersion.CreatedAt,
                UploadPending = targetVersion.UploadStatus == UploadStatus.PENDING
            });
        }

        // Reconstruct full data from delta chain - SlotId is Guid, convert to string
        var reconstructedData = await _versionDataLoader.ReconstructFromDeltaChainAsync(
            slot.SlotId.ToString(), targetVersion, versionStore, cancellationToken);

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
            var prevKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), currentVersion.BaseVersionNumber.Value);
            currentVersion = await versionStore.GetAsync(prevKey, cancellationToken);
            if (currentVersion == null)
            {
                break;
            }
        }

        // Compress the reconstructed data - config already provides typed enum (T25 compliant)
        var compressionTypeEnum = _configuration.DefaultCompressionType;
        var collapseCompressionLevel = compressionTypeEnum == CompressionType.BROTLI
            ? _configuration.BrotliCompressionLevel
            : compressionTypeEnum == CompressionType.GZIP
                ? _configuration.GzipCompressionLevel
                : (int?)null;
        var compressedData = Compression.CompressionHelper.Compress(reconstructedData, compressionTypeEnum, collapseCompressionLevel);

        // Update the target version to be a full snapshot
        var contentHash = Hashing.ContentHasher.ComputeHash(reconstructedData);
        targetVersion.IsDelta = false;
        targetVersion.BaseVersionNumber = null;
        targetVersion.DeltaAlgorithm = null;
        targetVersion.SizeBytes = reconstructedData.Length;
        targetVersion.CompressedSizeBytes = compressedData.Length;
        targetVersion.CompressionType = compressionTypeEnum;
        targetVersion.ContentHash = contentHash;
        targetVersion.UploadStatus = _configuration.AsyncUploadEnabled ? UploadStatus.PENDING : UploadStatus.COMPLETE;

        // Save updated manifest with optimistic concurrency
        var newEtag = await versionStore.TrySaveAsync(targetVersion.GetStateKey(), targetVersion, versionEtag ?? string.Empty, cancellationToken);
        if (newEtag == null)
        {
            _logger.LogDebug("Concurrent modification detected for version {Version} in slot {SlotId}",
                targetVersion.VersionNumber, slot.SlotId);
            return (StatusCodes.Conflict, null);
        }

        // Store in hot cache - HotSaveEntry fields are now proper types
        var hotCacheStore = _stateStoreFactory.GetStore<HotSaveEntry>(StateStoreDefinitions.SaveLoadCache);
        var resolvedVersionNumber = targetVersion.VersionNumber;
        var hotEntry = new HotSaveEntry
        {
            SlotId = slot.SlotId,
            VersionNumber = resolvedVersionNumber,
            Data = Convert.ToBase64String(compressedData),
            ContentHash = contentHash,
            IsCompressed = compressionTypeEnum != CompressionType.NONE,
            CompressionType = compressionTypeEnum,
            SizeBytes = reconstructedData.Length,
            CachedAt = DateTimeOffset.UtcNow
        };
        var hotCacheTtlSeconds = (int)TimeSpan.FromMinutes(_configuration.HotCacheTtlMinutes).TotalSeconds;
        await hotCacheStore.SaveAsync(
            hotEntry.GetStateKey(),
            hotEntry,
            new StateOptions { Ttl = hotCacheTtlSeconds },
            cancellationToken);

        // Queue for upload if enabled - PendingUploadEntry fields are now proper types
        if (_configuration.AsyncUploadEnabled)
        {
            var pendingStore = _stateStoreFactory.GetCacheableStore<PendingUploadEntry>(StateStoreDefinitions.SaveLoadPending);
            var uploadId = Guid.NewGuid();
            var pendingEntry = new PendingUploadEntry
            {
                UploadId = uploadId,
                SlotId = slot.SlotId,
                VersionNumber = resolvedVersionNumber,
                GameId = slot.GameId,
                OwnerId = slot.OwnerId,
                OwnerType = slot.OwnerType,
                Data = Convert.ToBase64String(compressedData),
                ContentHash = contentHash,
                CompressionType = compressionTypeEnum,
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
            await pendingStore.AddToSetAsync(Processing.SaveUploadWorker.PendingUploadIdsSetKey, uploadId.ToString(), cancellationToken: cancellationToken);
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

        _logger.LogDebug(
            "Collapsed delta chain for version {Version}, new size {Size} bytes",
            resolvedVersionNumber, reconstructedData.Length);

        return (StatusCodes.OK, new SaveResponse
        {
            SlotId = slot.SlotId,
            VersionNumber = resolvedVersionNumber,
            SizeBytes = reconstructedData.Length,
            CreatedAt = targetVersion.CreatedAt,
            UploadPending = targetVersion.UploadStatus == UploadStatus.PENDING
        });
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

        // Find the slot by querying slots for this owner
        var slotQueryStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        var matchingSlots = await slotQueryStore.QueryAsync(
            s => s.OwnerId == body.OwnerId &&
                s.OwnerType == body.OwnerType &&
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

    /// <summary>
    /// Implementation of PinVersion operation.
    /// Pins a version to prevent rolling cleanup deletion.
    /// </summary>
    public async Task<(StatusCodes, VersionResponse?)> PinVersionAsync(PinVersionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Pinning version {Version} in slot {SlotName} for owner {OwnerId}",
            body.VersionNumber, body.SlotName, body.OwnerId);

        // Find the slot
        var slot = await FindSlotByOwnerAndNameAsync(body.OwnerId, body.OwnerType, body.SlotName, cancellationToken);
        if (slot == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Get the version with ETag for optimistic concurrency
        var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
        var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), body.VersionNumber);
        var (version, versionEtag) = await versionStore.GetWithETagAsync(versionKey, cancellationToken);

        if (version == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Pin the version
        version.IsPinned = true;
        version.CheckpointName = body.CheckpointName;
        var newEtag = await versionStore.TrySaveAsync(versionKey, version, versionEtag ?? string.Empty, cancellationToken);
        if (newEtag == null)
        {
            _logger.LogDebug("Concurrent modification detected for version {Version} in slot {SlotId}",
                body.VersionNumber, slot.SlotId);
            return (StatusCodes.Conflict, null);
        }

        _logger.LogDebug(
            "Pinned version {Version} in slot {SlotId} with checkpoint name {CheckpointName}",
            body.VersionNumber, slot.SlotId, body.CheckpointName);

        // Publish event
        var pinnedEvent = new VersionPinnedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SlotId = slot.SlotId,
            SlotName = slot.SlotName,
            VersionNumber = version.VersionNumber,
            CheckpointName = body.CheckpointName
        };
        await _messageBus.TryPublishAsync(
            SAVE_VERSION_PINNED_TOPIC,
            pinnedEvent,
            cancellationToken: cancellationToken);

        return (StatusCodes.OK, MapToVersionResponse(version));
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

        // Find the slot
        var slot = await FindSlotByOwnerAndNameAsync(body.OwnerId, body.OwnerType, body.SlotName, cancellationToken);
        if (slot == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Get the version with ETag for optimistic concurrency
        var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
        var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), body.VersionNumber);
        var (version, versionEtag) = await versionStore.GetWithETagAsync(versionKey, cancellationToken);

        if (version == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Unpin the version
        var previousCheckpointName = version.CheckpointName;
        version.IsPinned = false;
        version.CheckpointName = null;
        var newEtag = await versionStore.TrySaveAsync(versionKey, version, versionEtag ?? string.Empty, cancellationToken);
        if (newEtag == null)
        {
            _logger.LogDebug("Concurrent modification detected for version {Version} in slot {SlotId}",
                body.VersionNumber, slot.SlotId);
            return (StatusCodes.Conflict, null);
        }

        _logger.LogDebug(
            "Unpinned version {Version} in slot {SlotId}",
            body.VersionNumber, slot.SlotId);

        // Publish event
        var unpinnedEvent = new VersionUnpinnedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SlotId = slot.SlotId,
            SlotName = slot.SlotName,
            VersionNumber = version.VersionNumber,
            PreviousCheckpointName = previousCheckpointName
        };
        await _messageBus.TryPublishAsync(
            SAVE_VERSION_UNPINNED_TOPIC,
            unpinnedEvent,
            cancellationToken: cancellationToken);

        return (StatusCodes.OK, MapToVersionResponse(version));
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

        // Find the slot
        var slot = await FindSlotByOwnerAndNameAsync(body.OwnerId, body.OwnerType, body.SlotName, cancellationToken);
        if (slot == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Lock the slot to prevent concurrent metadata modifications
        await using var slotLock = await _lockProvider.LockAsync(
            "save-load-slot", slot.SlotId.ToString(), Guid.NewGuid().ToString(), 30, cancellationToken);
        if (!slotLock.Success)
        {
            _logger.LogDebug("Could not acquire slot lock for {SlotId}", slot.SlotId);
            return (StatusCodes.Conflict, null);
        }

        // Get the version
        var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
        var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), body.VersionNumber);
        var version = await versionStore.GetAsync(versionKey, cancellationToken);

        if (version == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Check if version is pinned
        if (version.IsPinned)
        {
            _logger.LogDebug(
                "Cannot delete pinned version {Version} in slot {SlotId}",
                body.VersionNumber, slot.SlotId);
            return (StatusCodes.Conflict, null);
        }

        var bytesFreed = version.CompressedSizeBytes ?? version.SizeBytes;

        // Delete the asset if it exists
        if (version.AssetId.HasValue && version.AssetId.Value != Guid.Empty)
        {
            try
            {
                await _assetClient.DeleteAssetAsync(new DeleteAssetRequest { AssetId = version.AssetId.Value.ToString() }, cancellationToken);
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
        var hotCacheKey = HotSaveEntry.GetStateKey(slot.SlotId.ToString(), body.VersionNumber);
        await hotStore.DeleteAsync(hotCacheKey, cancellationToken);

        // Re-read slot metadata under lock for accurate update
        var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        slot = await slotStore.GetAsync(slot.GetStateKey(), cancellationToken) ?? slot;
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
        await PublishSaveSlotUpdatedEventAsync(slot, ["versionCount", "totalSizeBytes", "latestVersion"], cancellationToken);

        _logger.LogDebug(
            "Deleted version {Version} in slot {SlotId}, freed {BytesFreed} bytes",
            body.VersionNumber, slot.SlotId, bytesFreed);

        var deletedEvent = new VersionDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SlotId = slot.SlotId,
            SlotName = slot.SlotName,
            VersionNumber = body.VersionNumber,
            BytesFreed = bytesFreed
        };
        await _messageBus.TryPublishAsync(
            SAVE_VERSION_DELETED_TOPIC,
            deletedEvent,
            cancellationToken: cancellationToken);

        return (StatusCodes.OK, new DeleteVersionResponse
        {
            Deleted = true,
            BytesFreed = bytesFreed
        });
    }

    /// <summary>
    /// Queries saves based on filter criteria including owner, category, tags, and date ranges.
    /// </summary>
    public async Task<(StatusCodes, QuerySavesResponse?)> QuerySavesAsync(QuerySavesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Querying saves for owner {OwnerId} ({OwnerType})",
            body.OwnerId, body.OwnerType);

        // Query slots for this owner
        var slotQueryStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        var slots = await slotQueryStore.QueryAsync(
            s => s.OwnerId == body.OwnerId && s.OwnerType == body.OwnerType,
            cancellationToken);

        // Filter by category if specified
        if (body.Category != default)
        {
            slots = slots.Where(s => s.Category == body.Category).ToList();
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
                    SlotId = slot.SlotId,
                    SlotName = slot.SlotName,
                    OwnerId = slot.OwnerId,
                    OwnerType = slot.OwnerType,
                    Category = slot.Category,
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
            QuerySavesRequestSortBy.CreatedAt => body.SortOrder == QuerySavesRequestSortOrder.Asc
                ? results.OrderBy(r => r.CreatedAt).ToList()
                : results.OrderByDescending(r => r.CreatedAt).ToList(),
            QuerySavesRequestSortBy.Size => body.SortOrder == QuerySavesRequestSortOrder.Asc
                ? results.OrderBy(r => r.SizeBytes).ToList()
                : results.OrderByDescending(r => r.SizeBytes).ToList(),
            QuerySavesRequestSortBy.VersionNumber => body.SortOrder == QuerySavesRequestSortOrder.Asc
                ? results.OrderBy(r => r.VersionNumber).ToList()
                : results.OrderByDescending(r => r.VersionNumber).ToList(),
            _ => results.OrderByDescending(r => r.CreatedAt).ToList()
        };

        // Apply pagination
        var totalCount = results.Count;
        results = results.Skip(body.Offset).Take(body.Limit).ToList();

        _logger.LogDebug(
            "Query returned {Count} results out of {Total} total",
            results.Count, totalCount);

        return (StatusCodes.OK, new QuerySavesResponse
        {
            Results = results,
            TotalCount = totalCount
        });
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

        var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);

        // Find source slot
        var sourceOwnerType = body.SourceOwnerType.ToString();
        var sourceOwnerId = body.SourceOwnerId.ToString();
        var sourceSlotKey = SaveSlotMetadata.GetStateKey(body.SourceGameId, sourceOwnerType, sourceOwnerId, body.SourceSlotName);
        var sourceSlot = await slotStore.GetAsync(sourceSlotKey, cancellationToken);

        if (sourceSlot == null)
        {
            _logger.LogDebug("Source slot not found: {SlotKey}", sourceSlotKey);
            return (StatusCodes.NotFound, null);
        }

        // Get source version
        var sourceVersionNumber = body.SourceVersion ?? sourceSlot.LatestVersion;
        if (!sourceVersionNumber.HasValue)
        {
            _logger.LogDebug("Source slot has no versions");
            return (StatusCodes.NotFound, null);
        }
        var sourceVersionKey = SaveVersionManifest.GetStateKey(sourceSlot.SlotId.ToString(), sourceVersionNumber.Value);
        var sourceVersion = await versionStore.GetAsync(sourceVersionKey, cancellationToken);

        if (sourceVersion == null)
        {
            _logger.LogDebug("Source version {Version} not found", sourceVersionNumber);
            return (StatusCodes.NotFound, null);
        }

        // Load source data
        var sourceData = await _versionDataLoader.LoadVersionDataAsync(sourceSlot.SlotId.ToString(), sourceVersion, cancellationToken);
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

        // Find or create target slot (lock to prevent concurrent version number allocation)
        var targetOwnerType = body.TargetOwnerType.ToString();
        var targetOwnerId = body.TargetOwnerId.ToString();
        var targetSlotKey = SaveSlotMetadata.GetStateKey(body.TargetGameId, targetOwnerType, targetOwnerId, body.TargetSlotName);

        await using var targetSlotLock = await _lockProvider.LockAsync(
            "save-load-slot", targetSlotKey, Guid.NewGuid().ToString(), 60, cancellationToken);
        if (!targetSlotLock.Success)
        {
            _logger.LogDebug("Could not acquire target slot lock for {SlotKey}", targetSlotKey);
            return (StatusCodes.Conflict, null);
        }

        var targetSlot = await slotStore.GetAsync(targetSlotKey, cancellationToken);

        if (targetSlot == null)
        {
            // Create target slot
            var category = body.TargetCategory != default ? body.TargetCategory : sourceSlot.Category;
            targetSlot = new SaveSlotMetadata
            {
                SlotId = Guid.NewGuid(),
                GameId = body.TargetGameId,
                OwnerId = body.TargetOwnerId,
                OwnerType = body.TargetOwnerType,
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
        // Configuration already provides typed enum (T25 compliant)
        var compressionTypeEnum = _configuration.DefaultCompressionType;
        var copyCompressionLevel = compressionTypeEnum == CompressionType.BROTLI
            ? _configuration.BrotliCompressionLevel
            : compressionTypeEnum == CompressionType.GZIP
                ? _configuration.GzipCompressionLevel
                : (int?)null;
        var compressedData = Compression.CompressionHelper.Compress(sourceData, compressionTypeEnum, copyCompressionLevel);

        var newVersion = new SaveVersionManifest
        {
            SlotId = targetSlot.SlotId,
            VersionNumber = newVersionNumber,
            ContentHash = contentHash,
            SizeBytes = sourceData.Length,
            CompressedSizeBytes = compressedData.Length,
            CompressionType = compressionTypeEnum,
            SchemaVersion = sourceVersion.SchemaVersion,
            CheckpointName = sourceVersion.CheckpointName,
            Metadata = sourceVersion.Metadata != null ? new Dictionary<string, object>(sourceVersion.Metadata) : new Dictionary<string, object>(),
            IsPinned = false,
            IsDelta = false,
            UploadStatus = _configuration.AsyncUploadEnabled ? UploadStatus.PENDING : UploadStatus.COMPLETE,
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
            CompressionType = compressionTypeEnum,
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
            var pendingStore = _stateStoreFactory.GetCacheableStore<PendingUploadEntry>(StateStoreDefinitions.SaveLoadPending);
            var uploadId = Guid.NewGuid();
            var pendingEntry = new PendingUploadEntry
            {
                UploadId = uploadId,
                SlotId = targetSlot.SlotId,
                VersionNumber = newVersionNumber,
                GameId = body.TargetGameId,
                OwnerId = body.TargetOwnerId,
                OwnerType = body.TargetOwnerType,
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

        _logger.LogDebug(
            "Copied save from {SourceSlot} version {SourceVersion} to {TargetSlot} version {TargetVersion}",
            body.SourceSlotName, sourceVersionNumber, body.TargetSlotName, newVersionNumber);

        return (StatusCodes.OK, new SaveResponse
        {
            SlotId = targetSlot.SlotId,
            VersionNumber = newVersionNumber,
            SizeBytes = sourceData.Length,
            CreatedAt = newVersion.CreatedAt,
            UploadPending = _configuration.AsyncUploadEnabled
        });
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

        var ownerIdStr = body.OwnerId.ToString();
        var ownerTypeStr = body.OwnerType.ToString();

        // Find the slot
        var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, ownerTypeStr, ownerIdStr, body.SlotName);
        var slot = await slotStore.GetAsync(slotKey, cancellationToken);

        if (slot == null)
        {
            _logger.LogDebug("Slot not found: {SlotKey}", slotKey);
            return (StatusCodes.NotFound, null);
        }

        // Get the version to verify
        var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
        var versionNumber = body.VersionNumber ?? slot.LatestVersion;
        if (!versionNumber.HasValue)
        {
            _logger.LogDebug("No version to verify");
            return (StatusCodes.NotFound, null);
        }

        var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), versionNumber.Value);
        var version = await versionStore.GetAsync(versionKey, cancellationToken);

        if (version == null)
        {
            _logger.LogDebug("Version {Version} not found", versionNumber);
            return (StatusCodes.NotFound, null);
        }

        var expectedHash = version.ContentHash;

        // Try to load the data
        var data = await _versionDataLoader.LoadVersionDataAsync(slot.SlotId.ToString(), version, cancellationToken);

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

        _logger.LogDebug(
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

    /// <summary>
    /// Implementation of PromoteVersion operation.
    /// Promotes an old version to become the new latest version by copying it.
    /// </summary>
    public async Task<(StatusCodes, SaveResponse?)> PromoteVersionAsync(PromoteVersionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Promoting version {Version} to latest in slot {SlotName} for owner {OwnerId}",
            body.VersionNumber, body.SlotName, body.OwnerId);

        // Get the slot
        var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, body.OwnerType.ToString(), body.OwnerId.ToString(), body.SlotName);
        var slot = await slotStore.GetAsync(slotKey, cancellationToken);

        if (slot == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Lock the slot to prevent concurrent version number allocation
        await using var slotLock = await _lockProvider.LockAsync(
            "save-load-slot", slot.SlotId.ToString(), Guid.NewGuid().ToString(), 60, cancellationToken);
        if (!slotLock.Success)
        {
            _logger.LogDebug("Could not acquire slot lock for {SlotId}", slot.SlotId);
            return (StatusCodes.Conflict, null);
        }

        // Re-read slot under lock for accurate version number
        slot = await slotStore.GetAsync(slotKey, cancellationToken) ?? slot;

        // Get the source version
        var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
        var sourceVersionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), body.VersionNumber);
        var sourceVersion = await versionStore.GetAsync(sourceVersionKey, cancellationToken);

        if (sourceVersion == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Get the data from hot cache or asset service
        byte[] data;
        var hotStore = _stateStoreFactory.GetStore<HotSaveEntry>(StateStoreDefinitions.SaveLoadCache);
        var hotCacheKey = HotSaveEntry.GetStateKey(slot.SlotId.ToString(), body.VersionNumber);
        var hotEntry = await hotStore.GetAsync(hotCacheKey, cancellationToken);

        if (hotEntry != null)
        {
            data = Convert.FromBase64String(hotEntry.Data);
        }
        else if (sourceVersion.AssetId.HasValue)
        {
            var assetResponse = await _assetClient.GetAssetAsync(new GetAssetRequest { AssetId = sourceVersion.AssetId.Value.ToString() }, cancellationToken);
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
            UploadStatus = UploadStatus.PENDING,
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
        var newHotCacheKey = HotSaveEntry.GetStateKey(slot.SlotId.ToString(), newVersionNumber);
        await hotStore.SaveAsync(newHotCacheKey, newHotEntry, cancellationToken: cancellationToken);

        // Queue for async upload
        if (_configuration.AsyncUploadEnabled)
        {
            var pendingStore = _stateStoreFactory.GetCacheableStore<PendingUploadEntry>(StateStoreDefinitions.SaveLoadPending);
            var uploadId = Guid.NewGuid();
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
            await pendingStore.AddToSetAsync(Processing.SaveUploadWorker.PendingUploadIdsSetKey, uploadId.ToString(), cancellationToken: cancellationToken);
        }

        // Save version manifest
        var newVersionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), newVersionNumber);
        await versionStore.SaveAsync(newVersionKey, newVersion, cancellationToken: cancellationToken);

        slot.LatestVersion = newVersionNumber;
        slot.VersionCount++;
        slot.TotalSizeBytes += sourceVersion.CompressedSizeBytes ?? sourceVersion.SizeBytes;
        slot.UpdatedAt = DateTimeOffset.UtcNow;
        await slotStore.SaveAsync(slotKey, slot, cancellationToken: cancellationToken);
        await PublishSaveSlotUpdatedEventAsync(slot, ["latestVersion", "versionCount", "totalSizeBytes"], cancellationToken);

        // Run rolling cleanup
        await _versionCleanupManager.CleanupOldVersionsAsync(slot, cancellationToken);

        _logger.LogDebug(
            "Promoted version {SourceVersion} to version {NewVersion} in slot {SlotId}",
            body.VersionNumber, newVersionNumber, slot.SlotId);

        var createdEvent = new SaveCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SlotId = slot.SlotId,
            SlotName = slot.SlotName,
            VersionNumber = newVersionNumber,
            OwnerId = slot.OwnerId,
            OwnerType = slot.OwnerType,
            SizeBytes = sourceVersion.SizeBytes
        };
        await _messageBus.TryPublishAsync(SAVE_CREATED_TOPIC, createdEvent, cancellationToken: cancellationToken);

        return (StatusCodes.OK, new SaveResponse
        {
            SlotId = slot.SlotId,
            VersionNumber = newVersionNumber,
            ContentHash = sourceVersion.ContentHash,
            SizeBytes = sourceVersion.SizeBytes,
            CompressedSizeBytes = sourceVersion.CompressedSizeBytes ?? sourceVersion.SizeBytes,
            UploadPending = _configuration.AsyncUploadEnabled
        });
    }

    /// <summary>
    /// Implementation of AdminCleanup operation.
    /// Cleans up old save versions based on age and category filters.
    /// </summary>
    public async Task<(StatusCodes, AdminCleanupResponse?)> AdminCleanupAsync(AdminCleanupRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Executing AdminCleanup: dryRun={DryRun}, olderThanDays={Days}, category={Category}",
            body.DryRun, body.OlderThanDays, body.Category);

        var slotStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        var versionStore = _stateStoreFactory.GetQueryableStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);

        var ownerType = body.OwnerType;
        var category = body.Category;
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

            long slotBytesFreed = 0;

            foreach (var version in versionsToDelete)
            {
                // Use CompressedSizeBytes when available, consistent with how TotalSizeBytes is accumulated
                var versionSize = version.CompressedSizeBytes ?? version.SizeBytes;
                slotBytesFreed += versionSize;
                bytesFreed += versionSize;
                versionsDeleted++;

                if (!body.DryRun)
                {
                    var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId.ToString(), version.VersionNumber);
                    await versionStore.DeleteAsync(versionKey, cancellationToken);

                    // Delete asset if exists
                    if (version.AssetId.HasValue)
                    {
                        try
                        {
                            await _assetClient.DeleteAssetAsync(
                                new DeleteAssetRequest { AssetId = version.AssetId.Value.ToString() },
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
                    await PublishSaveSlotDeletedEventAsync(slot, "Admin cleanup - empty slot", cancellationToken);
                }
            }
            else if (!body.DryRun && versionsToDelete.Count > 0)
            {
                slot.VersionCount = remainingVersions;
                slot.TotalSizeBytes -= slotBytesFreed;
                slot.UpdatedAt = DateTimeOffset.UtcNow;
                await slotStore.SaveAsync(slot.GetStateKey(), slot, cancellationToken: cancellationToken);
                await PublishSaveSlotUpdatedEventAsync(slot, ["versionCount", "totalSizeBytes"], cancellationToken);
            }
        }

        _logger.LogDebug(
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

    /// <summary>
    /// Implementation of AdminStats operation.
    /// Returns aggregated statistics about save data storage.
    /// </summary>
    public async Task<(StatusCodes, AdminStatsResponse?)> AdminStatsAsync(AdminStatsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing AdminStats with groupBy={GroupBy}", body.GroupBy);

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
            case AdminStatsRequestGroupBy.OwnerType:
                var ownerTypeGroups = slotList.GroupBy(s => s.OwnerType);
                foreach (var group in ownerTypeGroups)
                {
                    var slotIds = group.Select(s => s.SlotId).ToHashSet();
                    var groupVersions = versionList.Where(v => slotIds.Contains(v.SlotId)).ToList();
                    breakdown.Add(new StatsBreakdown
                    {
                        Key = group.Key.ToString(),
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
                        Key = group.Key.ToString(),
                        Slots = group.Count(),
                        Versions = groupVersions.Count,
                        SizeBytes = groupVersions.Sum(v => v.SizeBytes)
                    });
                }
                break;

            case AdminStatsRequestGroupBy.SchemaVersion:
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
            // Configuration already provides typed enum (T25 compliant)
            _ => _configuration.DefaultCompressionType
        };
    }

    /// <summary>
    /// Converts internal SaveSlotMetadata to API SlotResponse.
    /// </summary>
    private static SlotResponse ToSlotResponse(SaveSlotMetadata slot)
    {
        return new SlotResponse
        {
            SlotId = slot.SlotId,
            OwnerId = slot.OwnerId,
            OwnerType = slot.OwnerType,
            SlotName = slot.SlotName,
            Category = slot.Category,
            MaxVersions = slot.MaxVersions,
            RetentionDays = slot.RetentionDays,
            CompressionType = slot.CompressionType,
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
            AssetId = version.AssetId ?? Guid.Empty,
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
        Guid ownerId,
        OwnerType ownerType,
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

    #endregion

    #region Lifecycle Event Publishing

    private async Task PublishSaveSlotCreatedEventAsync(SaveSlotMetadata slot, CancellationToken cancellationToken)
    {
        var eventModel = new SaveSlotCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SlotId = slot.SlotId,
            GameId = slot.GameId,
            OwnerId = slot.OwnerId,
            OwnerType = slot.OwnerType,
            SlotName = slot.SlotName,
            Category = slot.Category,
            MaxVersions = slot.MaxVersions,
            RetentionDays = slot.RetentionDays,
            CompressionType = slot.CompressionType,
            VersionCount = slot.VersionCount,
            LatestVersion = slot.LatestVersion,
            TotalSizeBytes = slot.TotalSizeBytes,
            CreatedAt = slot.CreatedAt,
            UpdatedAt = slot.UpdatedAt
        };

        await _messageBus.TryPublishAsync(SAVE_SLOT_CREATED_TOPIC, eventModel, cancellationToken: cancellationToken);
        _logger.LogDebug("Published SaveSlotCreatedEvent for slot: {SlotId}", slot.SlotId);
    }

    private async Task PublishSaveSlotUpdatedEventAsync(SaveSlotMetadata slot, IEnumerable<string> changedFields, CancellationToken cancellationToken)
    {
        var eventModel = new SaveSlotUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SlotId = slot.SlotId,
            GameId = slot.GameId,
            OwnerId = slot.OwnerId,
            OwnerType = slot.OwnerType,
            SlotName = slot.SlotName,
            Category = slot.Category,
            MaxVersions = slot.MaxVersions,
            RetentionDays = slot.RetentionDays,
            CompressionType = slot.CompressionType,
            VersionCount = slot.VersionCount,
            LatestVersion = slot.LatestVersion,
            TotalSizeBytes = slot.TotalSizeBytes,
            CreatedAt = slot.CreatedAt,
            UpdatedAt = slot.UpdatedAt,
            ChangedFields = changedFields.ToList()
        };

        await _messageBus.TryPublishAsync(SAVE_SLOT_UPDATED_TOPIC, eventModel, cancellationToken: cancellationToken);
        _logger.LogDebug("Published SaveSlotUpdatedEvent for slot: {SlotId}, changed: {ChangedFields}", slot.SlotId, string.Join(", ", changedFields));
    }

    private async Task PublishSaveSlotDeletedEventAsync(SaveSlotMetadata slot, string? deletedReason, CancellationToken cancellationToken)
    {
        var eventModel = new SaveSlotDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SlotId = slot.SlotId,
            GameId = slot.GameId,
            OwnerId = slot.OwnerId,
            OwnerType = slot.OwnerType,
            SlotName = slot.SlotName,
            Category = slot.Category,
            MaxVersions = slot.MaxVersions,
            RetentionDays = slot.RetentionDays,
            CompressionType = slot.CompressionType,
            VersionCount = slot.VersionCount,
            LatestVersion = slot.LatestVersion,
            TotalSizeBytes = slot.TotalSizeBytes,
            CreatedAt = slot.CreatedAt,
            UpdatedAt = slot.UpdatedAt,
            DeletedReason = deletedReason
        };

        await _messageBus.TryPublishAsync(SAVE_SLOT_DELETED_TOPIC, eventModel, cancellationToken: cancellationToken);
        _logger.LogDebug("Published SaveSlotDeletedEvent for slot: {SlotId}", slot.SlotId);
    }

    #endregion
}
