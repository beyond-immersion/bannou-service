using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.SaveLoad.Delta;
using BeyondImmersion.BannouService.SaveLoad.Models;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    public SaveLoadService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<SaveLoadService> logger,
        SaveLoadServiceConfiguration configuration,
        IAssetClient assetClient)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _assetClient = assetClient ?? throw new ArgumentNullException(nameof(assetClient));
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
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);
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
                MaxVersions = maxVersions,
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
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);

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
            var queryableStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);
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
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);

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
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);

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
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);
            var queryableStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);

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
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);
            var hotCacheStore = _stateStoreFactory.GetStore<HotSaveEntry>(_configuration.HotCacheStoreName);
            var pendingStore = _stateStoreFactory.GetStore<PendingUploadEntry>(_configuration.PendingUploadStoreName);

            var ownerType = body.OwnerType.ToString();
            var ownerId = body.OwnerId.ToString();
            var now = DateTimeOffset.UtcNow;

            // Get or create slot
            var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, ownerType, ownerId, body.SlotName);
            var slot = await slotStore.GetAsync(slotKey, cancellationToken);

            if (slot == null)
            {
                // Auto-create slot with defaults
                var category = body.Category;
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
            var versionsCleanedUp = await PerformRollingCleanupAsync(slot, versionStore, hotCacheStore, cancellationToken);

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
    /// Performs rolling cleanup of old versions based on slot's max version count.
    /// </summary>
    private async Task<int> PerformRollingCleanupAsync(
        SaveSlotMetadata slot,
        IStateStore<SaveVersionManifest> versionStore,
        IStateStore<HotSaveEntry> hotCacheStore,
        CancellationToken cancellationToken)
    {
        if (slot.VersionCount <= slot.MaxVersions)
        {
            return 0;
        }

        var cleanedUp = 0;
        var targetCleanup = slot.VersionCount - slot.MaxVersions;

        // Start from oldest version (1) and clean up non-pinned versions
        for (var v = 1; v <= (slot.LatestVersion ?? 0) && cleanedUp < targetCleanup; v++)
        {
            var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, v);
            var manifest = await versionStore.GetAsync(versionKey, cancellationToken);

            if (manifest == null)
            {
                continue;
            }

            if (manifest.IsPinned)
            {
                // Skip pinned versions
                continue;
            }

            // Delete version and hot cache entry
            await versionStore.DeleteAsync(versionKey, cancellationToken);
            var hotKey = HotSaveEntry.GetStateKey(slot.SlotId, v);
            await hotCacheStore.DeleteAsync(hotKey, cancellationToken);

            cleanedUp++;
            slot.VersionCount--;
            slot.TotalSizeBytes -= manifest.CompressedSizeBytes ?? manifest.SizeBytes;

            _logger.LogDebug("Cleaned up version {Version} from slot {SlotId}", v, slot.SlotId);
        }

        return cleanedUp;
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
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);
            var hotCacheStore = _stateStoreFactory.GetStore<HotSaveEntry>(_configuration.HotCacheStoreName);

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
                targetVersion = await FindVersionByCheckpointAsync(slot, body.CheckpointName, versionStore, cancellationToken);
                if (targetVersion == 0)
                {
                    _logger.LogWarning("Checkpoint {CheckpointName} not found in slot {SlotId}", body.CheckpointName, slot.SlotId);
                    return (StatusCodes.NotFound, null);
                }
            }
            else if (body.VersionNumber > 0)
            {
                targetVersion = body.VersionNumber;
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
                    var assetResponse = await LoadFromAssetServiceAsync(manifest.AssetId, cancellationToken);
                    if (assetResponse == null)
                    {
                        _logger.LogError("Failed to load asset {AssetId} for slot {SlotId} version {Version}",
                            manifest.AssetId, slot.SlotId, targetVersion);
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
                await CacheInHotStoreAsync(slot.SlotId, targetVersion, decompressedData, contentHash, manifest, hotCacheStore, cancellationToken);
            }

            // Verify integrity
            if (!Hashing.ContentHasher.VerifyHash(decompressedData, contentHash))
            {
                _logger.LogError("Hash mismatch for slot {SlotId} version {Version}", slot.SlotId, targetVersion);
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
    /// Finds a version by checkpoint name.
    /// </summary>
    private async Task<int> FindVersionByCheckpointAsync(
        SaveSlotMetadata slot,
        string checkpointName,
        IStateStore<SaveVersionManifest> versionStore,
        CancellationToken cancellationToken)
    {
        // Search through versions from newest to oldest
        for (var v = slot.LatestVersion ?? 0; v >= 1; v--)
        {
            var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, v);
            var manifest = await versionStore.GetAsync(versionKey, cancellationToken);

            if (manifest?.CheckpointName == checkpointName)
            {
                return v;
            }
        }

        return 0;
    }

    /// <summary>
    /// Loads save data from the Asset service.
    /// </summary>
    private async Task<byte[]?> LoadFromAssetServiceAsync(string assetId, CancellationToken cancellationToken)
    {
        try
        {
            var getRequest = new BeyondImmersion.BannouService.Asset.GetAssetRequest
            {
                AssetId = assetId
            };

            var response = await _assetClient.GetAssetAsync(getRequest, cancellationToken);

            if (response?.DownloadUrl == null)
            {
                _logger.LogWarning("Asset service returned no download URL for asset {AssetId}", assetId);
                return null;
            }

            // Download from presigned URL
            using var httpClient = new HttpClient();
            var data = await httpClient.GetByteArrayAsync(response.DownloadUrl, cancellationToken);
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading from Asset service for asset {AssetId}", assetId);
            return null;
        }
    }

    /// <summary>
    /// Caches loaded data in hot store for future fast access.
    /// </summary>
    private async Task CacheInHotStoreAsync(
        string slotId,
        int versionNumber,
        byte[] decompressedData,
        string contentHash,
        SaveVersionManifest manifest,
        IStateStore<HotSaveEntry> hotCacheStore,
        CancellationToken cancellationToken)
    {
        try
        {
            // Re-compress for storage efficiency
            var compressionType = Enum.TryParse<CompressionType>(manifest.CompressionType, out var ct) ? ct : CompressionType.NONE;
            var dataToStore = compressionType != CompressionType.NONE
                ? Compression.CompressionHelper.Compress(decompressedData, compressionType)
                : decompressedData;

            var hotEntry = new HotSaveEntry
            {
                SlotId = slotId,
                VersionNumber = versionNumber,
                Data = Convert.ToBase64String(dataToStore),
                ContentHash = contentHash,
                IsCompressed = compressionType != CompressionType.NONE,
                CompressionType = compressionType.ToString(),
                SizeBytes = dataToStore.Length,
                CachedAt = DateTimeOffset.UtcNow,
                IsDelta = manifest.IsDelta
            };

            var hotKey = HotSaveEntry.GetStateKey(slotId, versionNumber);
            await hotCacheStore.SaveAsync(hotKey, hotEntry, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Log but don't fail - caching is best-effort
            _logger.LogWarning(ex, "Failed to cache version {Version} in hot store", versionNumber);
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
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);
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
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);
            var baseVersionKey = SaveVersionManifest.GetStateKey(slot.SlotId, body.BaseVersion);
            var baseVersion = await versionStore.GetAsync(baseVersionKey, cancellationToken);

            if (baseVersion == null)
            {
                _logger.LogWarning("Base version {Version} not found", body.BaseVersion);
                return (StatusCodes.Gone, null);
            }

            // Validate the delta
            var deltaProcessor = new DeltaProcessor(
                _logger as ILogger<DeltaProcessor> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DeltaProcessor>.Instance,
                _configuration.MigrationMaxPatchOperations);
            var algorithm = body.Algorithm.ToString();
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

            // Check delta size threshold
            var deltaSize = body.Delta.Length;
            var estimatedFullSize = baseVersion.SizeBytes;
            var deltaPercent = (double)deltaSize / estimatedFullSize * 100;
            if (deltaPercent > _configuration.DeltaSizeThresholdPercent)
            {
                _logger.LogWarning(
                    "Delta size ({DeltaSize}) is {Percent:F1}% of full save, exceeds threshold of {Threshold}%",
                    deltaSize, deltaPercent, _configuration.DeltaSizeThresholdPercent);
                return (StatusCodes.RequestEntityTooLarge, null);
            }

            // Create new version number
            var newVersionNumber = slot.LatestVersion + 1;
            slot.LatestVersion = newVersionNumber;

            // Compress the delta if needed
            var compressedDelta = body.Delta;
            var compressionType = "NONE";
            if (deltaSize > _configuration.AutoCompressThresholdBytes)
            {
                compressedDelta = Compression.CompressionHelper.Compress(
                    body.Delta,
                    _configuration.DefaultCompressionType,
                    _configuration.GzipCompressionLevel,
                    _configuration.BrotliCompressionLevel);
                compressionType = _configuration.DefaultCompressionType;
            }

            // Store in hot cache
            var hotCacheStore = _stateStoreFactory.GetStore<HotSaveEntry>(_configuration.HotCacheStoreName);
            var hotEntry = new HotSaveEntry
            {
                SlotId = slot.SlotId,
                VersionNumber = newVersionNumber,
                Data = Convert.ToBase64String(compressedDelta),
                CompressionType = compressionType,
                CachedAt = DateTimeOffset.UtcNow
            };
            await hotCacheStore.SaveAsync(
                hotEntry.GetStateKey(),
                hotEntry,
                ttl: TimeSpan.FromMinutes(_configuration.HotCacheTtlMinutes),
                cancellationToken: cancellationToken);

            // Create version manifest
            var contentHash = Hashing.ContentHasher.ComputeHash(body.Delta);
            var manifest = new SaveVersionManifest
            {
                SlotId = slot.SlotId,
                VersionNumber = newVersionNumber,
                ContentHash = contentHash,
                SizeBytes = deltaSize,
                CompressedSizeBytes = compressedDelta.Length,
                CompressionType = compressionType,
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
                var pendingStore = _stateStoreFactory.GetStore<PendingUploadEntry>(_configuration.PendingUploadStoreName);
                var pendingEntry = new PendingUploadEntry
                {
                    UploadId = Guid.NewGuid().ToString(),
                    SlotId = slot.SlotId,
                    VersionNumber = newVersionNumber,
                    GameId = slot.GameId,
                    OwnerId = ownerId,
                    OwnerType = ownerType,
                    Data = Convert.ToBase64String(compressedDelta),
                    CompressedSizeBytes = compressedDelta.Length,
                    Priority = 1,
                    QueuedAt = DateTimeOffset.UtcNow
                };
                await pendingStore.SaveAsync(
                    pendingEntry.GetStateKey(),
                    pendingEntry,
                    ttl: TimeSpan.FromMinutes(_configuration.PendingUploadTtlMinutes),
                    cancellationToken: cancellationToken);
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

            return (StatusCodes.Created, new SaveDeltaResponse
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
    /// Implementation of LoadWithDeltas operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, LoadResponse?)> LoadWithDeltasAsync(LoadRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing LoadWithDeltas operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method LoadWithDeltas not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
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
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of CollapseDeltas operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, SaveResponse?)> CollapseDeltasAsync(CollapseDeltasRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing CollapseDeltas operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method CollapseDeltas not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
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
            return (StatusCodes.InternalServerError, default);
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
            var slotQueryStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);
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
            var versionQueryStore = _stateStoreFactory.GetQueryableStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);
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
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);
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
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);
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
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);
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
            var hotStore = _stateStoreFactory.GetStore<HotSaveEntry>(_configuration.HotCacheStoreName);
            var hotCacheKey = HotSaveEntry.GetStateKey(slot.SlotId, body.VersionNumber);
            await hotStore.DeleteAsync(hotCacheKey, cancellationToken);

            // Update slot metadata
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);
            slot.VersionCount = Math.Max(0, slot.VersionCount - 1);
            slot.TotalSizeBytes = Math.Max(0, slot.TotalSizeBytes - bytesFreed);
            slot.UpdatedAt = DateTimeOffset.UtcNow;

            // If we deleted the latest version, find the new latest
            if (slot.LatestVersion == body.VersionNumber)
            {
                var versionQueryStore = _stateStoreFactory.GetQueryableStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);
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
    /// Implementation of QuerySaves operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, QuerySavesResponse?)> QuerySavesAsync(QuerySavesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing QuerySaves operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method QuerySaves not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
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
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of CopySave operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, SaveResponse?)> CopySaveAsync(CopySaveRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing CopySave operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method CopySave not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
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
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of ExportSaves operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ExportSavesResponse?)> ExportSavesAsync(ExportSavesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ExportSaves operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method ExportSaves not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
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
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of ImportSaves operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ImportSavesResponse?)> ImportSavesAsync(ImportSavesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ImportSaves operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method ImportSaves not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
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
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of VerifyIntegrity operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, VerifyIntegrityResponse?)> VerifyIntegrityAsync(VerifyIntegrityRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing VerifyIntegrity operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method VerifyIntegrity not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
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
            return (StatusCodes.InternalServerError, default);
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
            var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);
            var slotKey = SaveSlotMetadata.GetStateKey(body.GameId, body.OwnerType.ToString(), body.OwnerId.ToString(), body.SlotName);
            var slot = await slotStore.GetAsync(slotKey, cancellationToken);

            if (slot == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get the source version
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);
            var sourceVersionKey = SaveVersionManifest.GetStateKey(slot.SlotId, body.VersionNumber);
            var sourceVersion = await versionStore.GetAsync(sourceVersionKey, cancellationToken);

            if (sourceVersion == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get the data from hot cache or asset service
            byte[] data;
            var hotStore = _stateStoreFactory.GetStore<HotSaveEntry>(_configuration.HotCacheStoreName);
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
                    return (StatusCodes.InternalServerError, null);
                }

                using var httpClient = new HttpClient();
                data = await httpClient.GetByteArrayAsync(assetResponse.DownloadUrl, cancellationToken);
            }
            else
            {
                _logger.LogError("No data available for version {Version}", body.VersionNumber);
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
                var pendingStore = _stateStoreFactory.GetStore<PendingUploadEntry>(_configuration.PendingUploadStoreName);
                var pendingEntry = new PendingUploadEntry
                {
                    UploadId = Guid.NewGuid().ToString(),
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
            await CleanupOldVersionsAsync(slot, cancellationToken);

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
    /// Implementation of MigrateSave operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, MigrateSaveResponse?)> MigrateSaveAsync(MigrateSaveRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing MigrateSave operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method MigrateSave not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing MigrateSave operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "MigrateSave",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/migrate",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of RegisterSchema operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, SchemaResponse?)> RegisterSchemaAsync(RegisterSchemaRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing RegisterSchema operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method RegisterSchema not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing RegisterSchema operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "RegisterSchema",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/schema/register",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of ListSchemas operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ListSchemasResponse?)> ListSchemasAsync(ListSchemasRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ListSchemas operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method ListSchemas not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ListSchemas operation");
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "ListSchemas",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/save-load/schema/list",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of AdminCleanup operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, AdminCleanupResponse?)> AdminCleanupAsync(AdminCleanupRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing AdminCleanup operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method AdminCleanup not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
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
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, AdminStatsResponse?)> AdminStatsAsync(AdminStatsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing AdminStats operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method AdminStats not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
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
            Metadata = version.Metadata.ToDictionary(
                kv => kv.Key,
                kv => kv.Value?.ToString() ?? string.Empty)
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
        var slotQueryStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);
        var matchingSlots = await slotQueryStore.QueryAsync(
            s => s.OwnerId == ownerId &&
                 s.OwnerType == ownerType &&
                 s.SlotName == slotName,
            cancellationToken);

        return matchingSlots.FirstOrDefault();
    }

    /// <summary>
    /// Performs rolling cleanup of old versions based on slot configuration.
    /// Pinned versions are excluded from cleanup.
    /// </summary>
    private async Task CleanupOldVersionsAsync(SaveSlotMetadata slot, CancellationToken cancellationToken)
    {
        if (slot.VersionCount <= slot.MaxVersions)
        {
            return;
        }

        var versionQueryStore = _stateStoreFactory.GetQueryableStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);
        var versions = await versionQueryStore.QueryAsync(
            v => v.SlotId == slot.SlotId,
            cancellationToken);

        // Get unpinned versions sorted by version number (oldest first)
        var unpinnedVersions = versions
            .Where(v => !v.IsPinned)
            .OrderBy(v => v.VersionNumber)
            .ToList();

        var pinnedCount = versions.Count(v => v.IsPinned);
        var targetUnpinnedCount = Math.Max(0, slot.MaxVersions - pinnedCount);
        var versionsToDelete = unpinnedVersions.Take(unpinnedVersions.Count - targetUnpinnedCount).ToList();

        if (versionsToDelete.Count == 0)
        {
            return;
        }

        var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);
        var hotStore = _stateStoreFactory.GetStore<HotSaveEntry>(_configuration.HotCacheStoreName);
        long bytesFreed = 0;

        foreach (var version in versionsToDelete)
        {
            try
            {
                // Delete asset if exists
                if (!string.IsNullOrEmpty(version.AssetId) && Guid.TryParse(version.AssetId, out var assetGuid))
                {
                    try
                    {
                        await _assetClient.DeleteAssetAsync(new DeleteAssetRequest { AssetId = assetGuid.ToString() }, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete asset {AssetId} during cleanup", version.AssetId);
                    }
                }

                // Delete version manifest
                var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, version.VersionNumber);
                await versionStore.DeleteAsync(versionKey, cancellationToken);

                // Delete from hot cache
                var hotCacheKey = HotSaveEntry.GetStateKey(slot.SlotId, version.VersionNumber);
                await hotStore.DeleteAsync(hotCacheKey, cancellationToken);

                bytesFreed += version.CompressedSizeBytes ?? version.SizeBytes;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup version {Version} in slot {SlotId}",
                    version.VersionNumber, slot.SlotId);
            }
        }

        // Update slot metadata
        var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);
        slot.VersionCount -= versionsToDelete.Count;
        slot.TotalSizeBytes -= bytesFreed;
        slot.UpdatedAt = DateTimeOffset.UtcNow;
        await slotStore.SaveAsync(slot.GetStateKey(), slot, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Rolling cleanup deleted {Count} versions from slot {SlotId}, freed {BytesFreed} bytes",
            versionsToDelete.Count, slot.SlotId, bytesFreed);

        // Publish cleanup event
        var cleanupEvent = new CleanupCompletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            VersionsDeleted = versionsToDelete.Count,
            SlotsDeleted = 0,
            BytesFreed = bytesFreed
        };
        await _messageBus.TryPublishAsync(
            "save-load.cleanup.completed",
            cleanupEvent,
            cancellationToken: cancellationToken);
    }

    #endregion
}
