# Saving and Loading Guide

This guide explains how to use Bannou's Save-Load service for game state persistence.

## Overview

The Save-Load service (`lib-save-load`) provides a generic, game-engine-agnostic system for persisting game state. It supports:

- **Polymorphic Ownership**: Saves can be owned by accounts, characters, sessions, or realms
- **Versioned Saves**: Automatic version history with rolling cleanup
- **Delta/Incremental Saves**: Store only changes to reduce storage costs
- **Schema Migration**: Upgrade old saves when game data formats change
- **Hybrid Storage**: Fast Redis cache + durable Asset service storage

## Core Concepts

### Save Slots

A **slot** is a named container for save versions. Each slot has:

| Property | Description |
|----------|-------------|
| `gameId` | Namespace isolation (e.g., "arcadia", "fantasia") |
| `ownerId` | UUID of the owning entity |
| `ownerType` | ACCOUNT, CHARACTER, SESSION, or REALM |
| `slotName` | Unique name per owner (e.g., "autosave", "manual-1") |
| `category` | Determines behavior (see Save Categories) |

### Save Categories

| Category | Max Versions | Auto-Cleanup | Use Case |
|----------|-------------|--------------|----------|
| `QUICK_SAVE` | 1 | Yes | Fast single-slot saves |
| `AUTO_SAVE` | 5 | Rolling | System-triggered periodic saves |
| `MANUAL_SAVE` | 10 | No | User-initiated named saves |
| `CHECKPOINT` | 20 | Rolling | Progress markers (level complete) |
| `STATE_SNAPSHOT` | 3 | Rolling | Debug/backup full state captures |

### Ownership Types

| Type | Lifetime | Use Case |
|------|----------|----------|
| `ACCOUNT` | Permanent | Cross-character progress, settings |
| `CHARACTER` | Character lifetime | Character-specific saves |
| `SESSION` | Session + grace period | Temporary/draft states, undo buffers |
| `REALM` | Realm lifetime | World state, shared progress |

**Note**: SESSION-owned saves are cleaned up after the session ends (default: 5 minute grace period). Other services can copy/promote saves to longer-term storage during this window.

## Basic Operations

### Creating a Slot

Slots are auto-created on first save, but you can pre-configure them:

```csharp
var request = new CreateSlotRequest
{
    GameId = "arcadia",
    OwnerId = accountId,
    OwnerType = OwnerType.ACCOUNT,
    SlotName = "manual-save-1",
    Category = SaveCategory.MANUAL_SAVE,
    MaxVersions = 15,  // Override default
    Tags = new List<string> { "chapter-3", "boss-fight" }
};

var (status, response) = await _saveLoadClient.CreateSlotAsync(request);
```

### Saving Data

```csharp
var saveData = BannouJson.SerializeToUtf8Bytes(gameState);

var request = new SaveRequest
{
    GameId = "arcadia",
    OwnerId = characterId,
    OwnerType = OwnerType.CHARACTER,
    SlotName = "autosave",
    Category = SaveCategory.AUTO_SAVE,
    Data = saveData,
    SchemaVersion = "1.2.0",
    DisplayName = "Chapter 3 - Before Boss",
    Metadata = new Dictionary<string, string>
    {
        ["level"] = "dungeon-depths",
        ["playtime"] = "12:34:56"
    }
};

var (status, response) = await _saveLoadClient.SaveAsync(request);
// response.VersionNumber contains the new version
```

### Loading Data

```csharp
// Load latest version
var request = new LoadRequest
{
    GameId = "arcadia",
    OwnerId = characterId,
    OwnerType = OwnerType.CHARACTER,
    SlotName = "autosave"
};

var (status, response) = await _saveLoadClient.LoadAsync(request);
var gameState = BannouJson.Deserialize<GameState>(response.Data);

// Load specific version
request.VersionNumber = 3;

// Load by checkpoint name
request.CheckpointName = "before-boss";
```

### Listing Slots

```csharp
var request = new ListSlotsRequest
{
    OwnerId = accountId,
    OwnerType = OwnerType.ACCOUNT,
    Category = SaveCategory.MANUAL_SAVE  // Optional filter
};

var (status, response) = await _saveLoadClient.ListSlotsAsync(request);
foreach (var slot in response.Slots)
{
    Console.WriteLine($"{slot.SlotName}: {slot.VersionCount} versions, {slot.TotalSizeBytes} bytes");
}
```

## Version Management

### Pinning Versions

Pin important versions to protect them from rolling cleanup:

```csharp
var request = new PinVersionRequest
{
    GameId = "arcadia",
    OwnerId = characterId,
    OwnerType = OwnerType.CHARACTER,
    SlotName = "autosave",
    VersionNumber = 5,
    CheckpointName = "before-final-boss"  // Optional name for easy retrieval
};

var (status, response) = await _saveLoadClient.PinVersionAsync(request);
```

### Promoting Old Versions

Restore from an old version by promoting it to latest:

```csharp
var request = new PromoteVersionRequest
{
    GameId = "arcadia",
    OwnerId = characterId,
    OwnerType = OwnerType.CHARACTER,
    SlotName = "autosave",
    VersionNumber = 3,  // Old version to promote
    DisplayName = "Restored from checkpoint"
};

var (status, response) = await _saveLoadClient.PromoteVersionAsync(request);
// Creates a NEW version with the old version's data
```

## Delta Saves

For large saves with small incremental changes, use delta saves to reduce storage:

```csharp
// Compute delta from base version
var delta = ComputeJsonPatch(oldState, newState);

var request = new SaveDeltaRequest
{
    GameId = "arcadia",
    OwnerId = characterId,
    OwnerType = OwnerType.CHARACTER,
    SlotName = "world-state",
    BaseVersion = 5,  // Version this delta is relative to
    Delta = delta,
    Algorithm = DeltaAlgorithm.JSON_PATCH
};

var (status, response) = await _saveLoadClient.SaveDeltaAsync(request);
// response.ChainLength shows depth of delta chain
```

### Loading Delta Saves

Delta saves are automatically reconstructed:

```csharp
// LoadWithDeltas handles reconstruction transparently
var request = new LoadRequest
{
    GameId = "arcadia",
    OwnerId = characterId,
    OwnerType = OwnerType.CHARACTER,
    SlotName = "world-state"
};

var (status, response) = await _saveLoadClient.LoadWithDeltasAsync(request);
// Returns fully reconstructed data, not raw delta
```

### Collapsing Delta Chains

Long delta chains increase load latency. Collapse them periodically:

```csharp
var request = new CollapseDeltasRequest
{
    GameId = "arcadia",
    OwnerId = characterId,
    OwnerType = OwnerType.CHARACTER,
    SlotName = "world-state",
    DeleteIntermediates = true  // Remove intermediate delta versions
};

var (status, response) = await _saveLoadClient.CollapseDeltasAsync(request);
```

## Schema Migration

When game data formats change, register schemas with migration patches:

### Registering a Schema

```csharp
var request = new RegisterSchemaRequest
{
    GameId = "arcadia",
    SchemaName = "character-save",
    Version = "2.0.0",
    Schema = characterSaveJsonSchema,
    MigrationPatch = new List<JsonPatchOperation>
    {
        new() { Op = "add", Path = "/inventory/capacity", Value = 100 },
        new() { Op = "move", From = "/gold", Path = "/currency/gold" },
        new() { Op = "remove", Path = "/deprecated_field" }
    },
    FromVersion = "1.0.0"
};

var (status, response) = await _saveLoadClient.RegisterSchemaAsync(request);
```

### Migrating a Save

```csharp
var request = new MigrateSaveRequest
{
    GameId = "arcadia",
    OwnerId = characterId,
    OwnerType = OwnerType.CHARACTER,
    SlotName = "main-save",
    TargetSchemaVersion = "2.0.0"
};

var (status, response) = await _saveLoadClient.MigrateSaveAsync(request);
// Creates new version with migrated data
```

## Export/Import

### Exporting Saves

```csharp
var request = new ExportSavesRequest
{
    GameId = "arcadia",
    OwnerId = accountId,
    OwnerType = OwnerType.ACCOUNT,
    SlotNames = new List<string> { "settings", "achievements" }  // Or null for all
};

var (status, response) = await _saveLoadClient.ExportSavesAsync(request);
// response.DownloadUrl is a pre-signed URL to download the archive
```

### Importing Saves

```csharp
var request = new ImportSavesRequest
{
    ArchiveAssetId = uploadedArchiveId,
    TargetGameId = "arcadia",
    TargetOwnerId = newAccountId,
    TargetOwnerType = OwnerType.ACCOUNT,
    ConflictResolution = ConflictResolution.RENAME  // SKIP, OVERWRITE, RENAME, or FAIL
};

var (status, response) = await _saveLoadClient.ImportSavesAsync(request);
```

## Querying Saves

Search across slots with filters:

```csharp
var request = new QuerySavesRequest
{
    GameId = "arcadia",
    OwnerId = accountId,
    OwnerType = OwnerType.ACCOUNT,
    Categories = new List<SaveCategory> { SaveCategory.MANUAL_SAVE, SaveCategory.CHECKPOINT },
    Tags = new List<string> { "boss-fight" },
    FromDate = DateTimeOffset.UtcNow.AddDays(-30),
    Limit = 20
};

var (status, response) = await _saveLoadClient.QuerySavesAsync(request);
```

## Integrity Verification

Verify save data hasn't been corrupted:

```csharp
var request = new VerifyIntegrityRequest
{
    GameId = "arcadia",
    OwnerId = characterId,
    OwnerType = OwnerType.CHARACTER,
    SlotName = "main-save",
    VersionNumber = 5  // Or null for latest
};

var (status, response) = await _saveLoadClient.VerifyIntegrityAsync(request);
if (!response.Valid)
{
    _logger.LogError("Save corruption detected: {Error}", response.ErrorMessage);
}
```

## Configuration

Key configuration options (environment variables):

| Variable | Default | Description |
|----------|---------|-------------|
| `SAVE_LOAD_MAX_SAVE_SIZE_BYTES` | 104857600 | Max save size (100MB) |
| `SAVE_LOAD_AUTO_COMPRESS_THRESHOLD_BYTES` | 1048576 | Auto-compress above 1MB |
| `SAVE_LOAD_DEFAULT_COMPRESSION_TYPE` | GZIP | NONE, GZIP, or BROTLI |
| `SAVE_LOAD_HOT_CACHE_TTL_MINUTES` | 60 | Redis cache TTL |
| `SAVE_LOAD_MAX_DELTA_CHAIN_LENGTH` | 10 | Max deltas before auto-collapse |
| `SAVE_LOAD_DELTA_SIZE_THRESHOLD_PERCENT` | 50 | Store full if delta > 50% of full |
| `SAVE_LOAD_MAX_SLOTS_PER_OWNER` | 100 | Per-owner slot limit |
| `SAVE_LOAD_MAX_SAVES_PER_MINUTE` | 10 | Rate limit |

## Events

The service publishes events for integration:

| Event | When |
|-------|------|
| `SaveSlotCreatedEvent` | Slot created |
| `SaveSlotDeletedEvent` | Slot deleted |
| `SaveCreatedEvent` | New version saved |
| `SaveLoadedEvent` | Version loaded |
| `SaveMigratedEvent` | Schema migration completed |
| `VersionPinnedEvent` | Version pinned |
| `VersionUnpinnedEvent` | Version unpinned |
| `VersionDeletedEvent` | Version deleted |
| `CleanupCompletedEvent` | Automatic cleanup finished |
| `SaveQueuedEvent` | Save queued for async upload |
| `SaveUploadCompletedEvent` | Async upload completed |
| `SaveUploadFailedEvent` | Async upload failed |
| `CircuitBreakerStateChangedEvent` | Storage circuit breaker state change |

## Best Practices

1. **Use appropriate categories**: Don't use MANUAL_SAVE for autosaves - the cleanup behavior differs

2. **Pin important versions**: Before major game events, pin a checkpoint so players can restore

3. **Use delta saves for large states**: World state that changes incrementally benefits from delta saves

4. **Set reasonable max versions**: Balance between history depth and storage costs

5. **Include schema versions**: Always set `schemaVersion` to enable future migrations

6. **Use tags for organization**: Tags enable efficient querying across many slots

7. **Handle SESSION cleanup**: If using SESSION ownership for draft states, subscribe to `session.ended` events to promote important saves before the grace period expires

8. **Verify integrity periodically**: For critical saves, verify integrity before and after major operations

## Troubleshooting

**Save returns 413 (Payload Too Large)**
- Check `SAVE_LOAD_MAX_SAVE_SIZE_BYTES` configuration
- Consider using compression or delta saves

**Load returns 404 but slot exists**
- Check if the specific version was cleaned up (rolling cleanup)
- Verify the version number or checkpoint name is correct

**Delta chain reconstruction fails (422)**
- A base version in the chain was deleted
- Use `CollapseDeltas` to consolidate remaining versions

**Export/Import fails**
- Check Asset service availability
- Verify the archive format is valid (ZIP with manifest.json)

---

*See [SAVE-LOAD-PLUGIN.md](../planning/SAVE-LOAD-PLUGIN.md) for full design documentation.*
