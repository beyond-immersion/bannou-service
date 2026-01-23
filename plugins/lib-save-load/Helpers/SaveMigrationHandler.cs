using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.SaveLoad.Compression;
using BeyondImmersion.BannouService.SaveLoad.Migration;
using BeyondImmersion.BannouService.SaveLoad.Models;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.SaveLoad.Helpers;

/// <summary>
/// Implementation of schema registration and save data migration operations.
/// </summary>
public sealed class SaveMigrationHandler : ISaveMigrationHandler
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly SaveLoadServiceConfiguration _configuration;
    private readonly IAssetClient _assetClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IVersionDataLoader _versionDataLoader;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<SaveMigrationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SaveMigrationHandler"/> class.
    /// </summary>
    public SaveMigrationHandler(
        IStateStoreFactory stateStoreFactory,
        SaveLoadServiceConfiguration configuration,
        IAssetClient assetClient,
        IHttpClientFactory httpClientFactory,
        IVersionDataLoader versionDataLoader,
        IMessageBus messageBus,
        ILogger<SaveMigrationHandler> logger)
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
    public async Task<(StatusCodes, SchemaResponse?)> RegisterSchemaAsync(
        RegisterSchemaRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Registering schema {Namespace}:{Version}",
            body.Namespace, body.SchemaVersion);

        try
        {
            var schemaStore = _stateStoreFactory.GetStore<SaveSchemaDefinition>(StateStoreDefinitions.SaveLoadSchemas);

            // Check if schema already exists
            var schemaKey = SaveSchemaDefinition.GetStateKey(body.Namespace, body.SchemaVersion);
            var existingSchema = await schemaStore.GetAsync(schemaKey, cancellationToken);
            if (existingSchema != null)
            {
                _logger.LogWarning(
                    "Schema {Namespace}:{Version} already exists",
                    body.Namespace, body.SchemaVersion);
                return (StatusCodes.Conflict, null);
            }

            // Validate previous version exists if specified
            if (!string.IsNullOrEmpty(body.PreviousVersion))
            {
                var previousKey = SaveSchemaDefinition.GetStateKey(body.Namespace, body.PreviousVersion);
                var previousSchema = await schemaStore.GetAsync(previousKey, cancellationToken);
                if (previousSchema == null)
                {
                    _logger.LogWarning(
                        "Previous schema version {Version} not found",
                        body.PreviousVersion);
                    return (StatusCodes.BadRequest, null);
                }
            }

            // Serialize migration patch if provided
            string? migrationPatchJson = null;
            if (body.MigrationPatch != null && body.MigrationPatch.Count > 0)
            {
                migrationPatchJson = BannouJson.Serialize(body.MigrationPatch);
            }

            // Create schema definition
            var schema = new SaveSchemaDefinition
            {
                Namespace = body.Namespace,
                SchemaVersion = body.SchemaVersion,
                SchemaJson = BannouJson.Serialize(body.Schema),
                PreviousVersion = body.PreviousVersion,
                MigrationPatchJson = migrationPatchJson,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await schemaStore.SaveAsync(schemaKey, schema, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Registered schema {Namespace}:{Version}",
                body.Namespace, body.SchemaVersion);

            return (StatusCodes.OK, new SchemaResponse
            {
                Namespace = schema.Namespace,
                SchemaVersion = schema.SchemaVersion,
                Schema = body.Schema,
                PreviousVersion = schema.PreviousVersion,
                HasMigration = schema.HasMigration,
                CreatedAt = schema.CreatedAt
            });
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
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListSchemasResponse?)> ListSchemasAsync(
        ListSchemasRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing schemas for namespace {Namespace}", body.Namespace);

        try
        {
            var schemaStore = _stateStoreFactory.GetQueryableStore<SaveSchemaDefinition>(StateStoreDefinitions.SaveLoadSchemas);

            // Query all schemas in the namespace
            var schemas = await schemaStore.QueryAsync(
                s => s.Namespace == body.Namespace,
                cancellationToken);

            var schemaList = schemas.ToList();

            // Find latest version (the one with no successor)
            string? latestVersion = null;
            var versionSet = new HashSet<string>(schemaList.Select(s => s.SchemaVersion));
            // Where clause ensures PreviousVersion is not null/empty; coalesce satisfies compiler's nullable analysis
            var predecessorSet = new HashSet<string>(schemaList.Where(s => !string.IsNullOrEmpty(s.PreviousVersion)).Select(s => s.PreviousVersion ?? string.Empty));

            foreach (var version in versionSet)
            {
                if (!predecessorSet.Contains(version))
                {
                    latestVersion = version;
                    break;
                }
            }

            // If no clear successor chain, fall back to most recently created
            if (latestVersion == null && schemaList.Count > 0)
            {
                latestVersion = schemaList.OrderByDescending(s => s.CreatedAt).First().SchemaVersion;
            }

            var response = new ListSchemasResponse
            {
                Schemas = schemaList.Select(s => new SchemaResponse
                {
                    Namespace = s.Namespace,
                    SchemaVersion = s.SchemaVersion,
                    Schema = !string.IsNullOrEmpty(s.SchemaJson)
                        ? BannouJson.Deserialize<object>(s.SchemaJson) ?? new object()
                        : new object(),
                    PreviousVersion = s.PreviousVersion,
                    HasMigration = s.HasMigration,
                    CreatedAt = s.CreatedAt
                }).ToList(),
                LatestVersion = latestVersion
            };

            _logger.LogInformation(
                "Listed {Count} schemas for namespace {Namespace}",
                schemaList.Count, body.Namespace);

            return (StatusCodes.OK, response);
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
                endpoint: "post:/save-load/schemas",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, MigrateSaveResponse?)> MigrateSaveAsync(
        MigrateSaveRequest body,
        CancellationToken cancellationToken)
    {
        if (!_configuration.MigrationsEnabled)
        {
            _logger.LogWarning("Schema migrations are disabled by configuration");
            return (StatusCodes.BadRequest, null);
        }

        _logger.LogDebug(
            "Migrating save {SlotName} to schema version {TargetVersion}",
            body.SlotName, body.TargetSchemaVersion);

        try
        {
            var slotStore = _stateStoreFactory.GetQueryableStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
            var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
            var schemaStore = _stateStoreFactory.GetQueryableStore<SaveSchemaDefinition>(StateStoreDefinitions.SaveLoadSchemas);

            // Find source slot by querying by owner and slot name
            var ownerType = body.OwnerType.ToString();
            var ownerId = body.OwnerId.ToString();

            // Query slots for this owner and slot name
            var slots = await slotStore.QueryAsync(
                s => s.OwnerId == ownerId && s.OwnerType == ownerType && s.SlotName == body.SlotName,
                cancellationToken);
            var slot = slots.FirstOrDefault();

            if (slot == null)
            {
                _logger.LogWarning("Slot not found for owner {OwnerId}, slot {SlotName}", ownerId, body.SlotName);
                return (StatusCodes.NotFound, null);
            }

            var slotKey = SaveSlotMetadata.GetStateKey(slot.GameId, ownerType, ownerId, body.SlotName);

            // Get source version
            var versionNumber = body.VersionNumber > 0 ? body.VersionNumber : (slot.LatestVersion ?? 0);
            if (versionNumber == 0)
            {
                _logger.LogWarning("No versions found for slot {SlotId}", slot.SlotId);
                return (StatusCodes.NotFound, null);
            }

            var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, versionNumber);
            var version = await versionStore.GetAsync(versionKey, cancellationToken);

            if (version == null)
            {
                _logger.LogWarning("Version {Version} not found for slot {SlotId}", versionNumber, slot.SlotId);
                return (StatusCodes.NotFound, null);
            }

            // Check if migration is needed
            var currentSchemaVersion = version.SchemaVersion ?? "1.0.0";
            if (currentSchemaVersion == body.TargetSchemaVersion)
            {
                _logger.LogInformation(
                    "Save is already at target schema version {Version}",
                    body.TargetSchemaVersion);

                return (StatusCodes.OK, new MigrateSaveResponse
                {
                    Success = true,
                    FromSchemaVersion = currentSchemaVersion,
                    ToSchemaVersion = body.TargetSchemaVersion,
                    NewVersionNumber = null,
                    MigrationPath = new List<string> { currentSchemaVersion },
                    Warnings = new List<string>()
                });
            }

            // Create migrator and find migration path (default max 10 steps)
            var migrator = new SchemaMigrator(_logger, schemaStore, maxMigrationSteps: 10);
            var migrationPath = await migrator.FindMigrationPathAsync(
                slot.GameId,
                currentSchemaVersion,
                body.TargetSchemaVersion,
                cancellationToken);

            if (migrationPath == null)
            {
                _logger.LogWarning(
                    "No migration path found from {From} to {To}",
                    currentSchemaVersion, body.TargetSchemaVersion);
                return (StatusCodes.BadRequest, null);
            }

            // Load the save data
            var saveData = await _versionDataLoader.LoadVersionDataAsync(slot.SlotId, version, cancellationToken);
            if (saveData == null)
            {
                _logger.LogError("Failed to load save data for migration");
                return (StatusCodes.InternalServerError, null);
            }

            // Apply migration
            var migrationResult = await migrator.ApplyMigrationPathAsync(
                slot.GameId,
                saveData,
                migrationPath,
                cancellationToken);

            if (migrationResult == null)
            {
                _logger.LogError("Migration failed");
                return (StatusCodes.InternalServerError, null);
            }

            // If dry run, return without saving
            if (body.DryRun)
            {
                return (StatusCodes.OK, new MigrateSaveResponse
                {
                    Success = true,
                    FromSchemaVersion = currentSchemaVersion,
                    ToSchemaVersion = body.TargetSchemaVersion,
                    NewVersionNumber = null,
                    MigrationPath = migrationPath,
                    Warnings = migrationResult.Warnings
                });
            }

            // Save the migrated data as a new version
            var newVersionNumber = (slot.LatestVersion ?? 0) + 1;

            // Compress data
            var compressionType = Enum.TryParse<CompressionType>(slot.CompressionType, out var ct) ? ct : CompressionType.GZIP;
            var migrationCompressionLevel = compressionType == CompressionType.BROTLI
                ? _configuration.BrotliCompressionLevel
                : compressionType == CompressionType.GZIP
                    ? _configuration.GzipCompressionLevel
                    : (int?)null;
            var compressedData = CompressionHelper.Compress(migrationResult.Data, compressionType, migrationCompressionLevel);

            // Upload to storage
            var uploadRequest = new UploadRequest
            {
                Owner = "save-load",
                Filename = $"{slot.SlotId}_{newVersionNumber}.save",
                ContentType = "application/octet-stream",
                Size = compressedData.Length,
                Metadata = new AssetMetadataInput { AssetType = AssetType.Other }
            };

            var uploadResponse = await _assetClient.RequestUploadAsync(uploadRequest, cancellationToken);
            if (uploadResponse?.UploadUrl == null)
            {
                _logger.LogError("Failed to request upload URL for migrated save");
                return (StatusCodes.InternalServerError, null);
            }

            using var httpClient = _httpClientFactory.CreateClient();
            using var uploadContent = new ByteArrayContent(compressedData);
            uploadContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            var uploadResult = await httpClient.PutAsync(uploadResponse.UploadUrl, uploadContent, cancellationToken);

            if (!uploadResult.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to upload migrated save: {Status}", uploadResult.StatusCode);
                return (StatusCodes.InternalServerError, null);
            }

            var completeRequest = new CompleteUploadRequest { UploadId = uploadResponse.UploadId };
            var assetMetadata = await _assetClient.CompleteUploadAsync(completeRequest, cancellationToken);

            if (assetMetadata == null)
            {
                _logger.LogError("Failed to complete upload for migrated save");
                return (StatusCodes.InternalServerError, null);
            }

            // Create new version manifest
            var contentHash = Hashing.ContentHasher.ComputeHash(migrationResult.Data);
            var newVersion = new SaveVersionManifest
            {
                SlotId = slot.SlotId,
                VersionNumber = newVersionNumber,
                ContentHash = contentHash,
                SizeBytes = migrationResult.Data.Length,
                CompressedSizeBytes = compressedData.Length,
                CompressionType = compressionType.ToString(),
                SchemaVersion = body.TargetSchemaVersion,
                CheckpointName = $"Migrated from {currentSchemaVersion}",
                AssetId = assetMetadata.AssetId,
                CreatedAt = DateTimeOffset.UtcNow,
                Metadata = version.Metadata != null ? new Dictionary<string, object>(version.Metadata) : new Dictionary<string, object>()
            };

            var newVersionKey = SaveVersionManifest.GetStateKey(slot.SlotId, newVersionNumber);
            await versionStore.SaveAsync(newVersionKey, newVersion, cancellationToken: cancellationToken);

            // Update slot's latest version
            slot.LatestVersion = newVersionNumber;
            slot.UpdatedAt = DateTimeOffset.UtcNow;
            await slotStore.SaveAsync(slotKey, slot, cancellationToken: cancellationToken);

            // Publish event
            await _messageBus.TryPublishAsync("save.migrated", new SaveMigratedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SlotId = Guid.Parse(slot.SlotId),
                SlotName = slot.SlotName,
                OriginalVersionNumber = versionNumber,
                NewVersionNumber = newVersionNumber,
                OwnerId = Guid.Parse(slot.OwnerId),
                OwnerType = slot.OwnerType,
                FromSchemaVersion = currentSchemaVersion,
                ToSchemaVersion = body.TargetSchemaVersion
            }, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Migrated save {SlotName} from {From} to {To}, new version {Version}",
                body.SlotName, currentSchemaVersion, body.TargetSchemaVersion, newVersionNumber);

            return (StatusCodes.OK, new MigrateSaveResponse
            {
                Success = true,
                FromSchemaVersion = currentSchemaVersion,
                ToSchemaVersion = body.TargetSchemaVersion,
                NewVersionNumber = newVersionNumber,
                MigrationPath = migrationPath,
                Warnings = migrationResult.Warnings
            });
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
            return (StatusCodes.InternalServerError, null);
        }
    }
}
