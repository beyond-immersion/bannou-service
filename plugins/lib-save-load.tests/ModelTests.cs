using BeyondImmersion.BannouService.SaveLoad.Models;

namespace BeyondImmersion.BannouService.SaveLoad.Tests;

/// <summary>
/// Tests for save-load internal models and their state key generation.
/// </summary>
public class ModelTests
{
    #region SaveSlotMetadata Tests

    [Fact]
    public void SaveSlotMetadata_GetStateKey_ReturnsCorrectFormat()
    {
        // Arrange
        var ownerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var slot = new SaveSlotMetadata
        {
            SlotId = Guid.NewGuid(),
            GameId = "arcadia",
            OwnerId = ownerId,
            OwnerType = OwnerType.ACCOUNT,
            SlotName = "main-save",
            Category = SaveCategory.MANUAL_SAVE
        };

        // Act
        var key = slot.GetStateKey();

        // Assert
        Assert.Equal($"slot:arcadia:ACCOUNT:{ownerId}:main-save", key);
    }

    [Fact]
    public void SaveSlotMetadata_GetStateKeyStatic_ReturnsCorrectFormat()
    {
        // Arrange & Act
        var key = SaveSlotMetadata.GetStateKey("game1", "CHARACTER", "char-789", "quick-save");

        // Assert
        Assert.Equal("slot:game1:CHARACTER:char-789:quick-save", key);
    }

    [Fact]
    public void SaveSlotMetadata_InstanceAndStaticKeys_Match()
    {
        // Arrange
        var ownerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var slot = new SaveSlotMetadata
        {
            SlotId = Guid.NewGuid(),
            GameId = "testgame",
            OwnerId = ownerId,
            OwnerType = OwnerType.SESSION,
            SlotName = "slot1",
            Category = SaveCategory.AUTO_SAVE
        };

        // Act
        var instanceKey = slot.GetStateKey();
        var staticKey = SaveSlotMetadata.GetStateKey(
            slot.GameId, slot.OwnerType.ToString(), slot.OwnerId.ToString(), slot.SlotName);

        // Assert
        Assert.Equal(instanceKey, staticKey);
    }

    [Fact]
    public void SaveSlotMetadata_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var slot = new SaveSlotMetadata
        {
            SlotId = Guid.NewGuid(),
            GameId = "game",
            OwnerId = Guid.NewGuid(),
            OwnerType = OwnerType.ACCOUNT,
            SlotName = "slot",
            Category = SaveCategory.MANUAL_SAVE
        };

        // Assert defaults
        Assert.Equal(CompressionType.GZIP, slot.CompressionType);
        Assert.Equal(0, slot.VersionCount);
        Assert.Null(slot.LatestVersion);
        Assert.Equal(0, slot.TotalSizeBytes);
        Assert.NotNull(slot.Tags);
        Assert.Empty(slot.Tags);
        Assert.NotNull(slot.Metadata);
        Assert.Empty(slot.Metadata);
        Assert.Null(slot.ETag);
        Assert.Null(slot.RetentionDays);
    }

    #endregion

    #region SaveVersionManifest Tests

    [Fact]
    public void SaveVersionManifest_GetStateKey_ReturnsCorrectFormat()
    {
        // Arrange
        var slotId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var manifest = new SaveVersionManifest
        {
            SlotId = slotId,
            VersionNumber = 42,
            ContentHash = "hash123"
        };

        // Act
        var key = manifest.GetStateKey();

        // Assert
        Assert.Equal($"version:{slotId}:42", key);
    }

    [Fact]
    public void SaveVersionManifest_GetStateKeyStatic_ReturnsCorrectFormat()
    {
        // Arrange & Act
        var key = SaveVersionManifest.GetStateKey("my-slot", 7);

        // Assert
        Assert.Equal("version:my-slot:7", key);
    }

    [Fact]
    public void SaveVersionManifest_InstanceAndStaticKeys_Match()
    {
        // Arrange
        var slotId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var manifest = new SaveVersionManifest
        {
            SlotId = slotId,
            VersionNumber = 100,
            ContentHash = "abc123"
        };

        // Act
        var instanceKey = manifest.GetStateKey();
        var staticKey = SaveVersionManifest.GetStateKey(manifest.SlotId.ToString(), manifest.VersionNumber);

        // Assert
        Assert.Equal(instanceKey, staticKey);
    }

    [Fact]
    public void SaveVersionManifest_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var manifest = new SaveVersionManifest
        {
            SlotId = Guid.NewGuid(),
            VersionNumber = 1,
            ContentHash = "hash"
        };

        // Assert defaults
        Assert.Null(manifest.AssetId);
        Assert.Equal(CompressionType.NONE, manifest.CompressionType);
        Assert.False(manifest.IsPinned);
        Assert.Null(manifest.CheckpointName);
        Assert.False(manifest.IsDelta);
        Assert.Null(manifest.BaseVersionNumber);
        Assert.Null(manifest.DeltaAlgorithm);
        Assert.Null(manifest.ThumbnailAssetId);
        Assert.Null(manifest.DeviceId);
        Assert.Null(manifest.SchemaVersion);
        Assert.NotNull(manifest.Metadata);
        Assert.Empty(manifest.Metadata);
        Assert.Equal(UploadStatus.PENDING, manifest.UploadStatus);
        Assert.Null(manifest.ETag);
    }

    [Fact]
    public void SaveVersionManifest_KeyWithVersionZero_Works()
    {
        // Arrange & Act
        var key = SaveVersionManifest.GetStateKey("slot", 0);

        // Assert
        Assert.Equal("version:slot:0", key);
    }

    #endregion

    #region HotSaveEntry Tests

    [Fact]
    public void HotSaveEntry_GetStateKey_ReturnsCorrectFormat()
    {
        // Arrange
        var slotId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var entry = new HotSaveEntry
        {
            SlotId = slotId,
            VersionNumber = 5,
            Data = "base64data",
            ContentHash = "sha256hash"
        };

        // Act
        var key = entry.GetStateKey();

        // Assert
        Assert.Equal($"hot:{slotId}:5", key);
    }

    [Fact]
    public void HotSaveEntry_GetStateKeyStatic_ReturnsCorrectFormat()
    {
        // Arrange & Act
        var key = HotSaveEntry.GetStateKey("cache-slot", 12);

        // Assert
        Assert.Equal("hot:cache-slot:12", key);
    }

    [Fact]
    public void HotSaveEntry_GetLatestKey_ReturnsCorrectFormat()
    {
        // Arrange & Act
        var key = HotSaveEntry.GetLatestKey("my-slot");

        // Assert
        Assert.Equal("hot:my-slot:latest", key);
    }

    [Fact]
    public void HotSaveEntry_InstanceAndStaticKeys_Match()
    {
        // Arrange
        var slotId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var entry = new HotSaveEntry
        {
            SlotId = slotId,
            VersionNumber = 99,
            Data = "data",
            ContentHash = "hash"
        };

        // Act
        var instanceKey = entry.GetStateKey();
        var staticKey = HotSaveEntry.GetStateKey(entry.SlotId.ToString(), entry.VersionNumber);

        // Assert
        Assert.Equal(instanceKey, staticKey);
    }

    [Fact]
    public void HotSaveEntry_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var entry = new HotSaveEntry
        {
            SlotId = Guid.NewGuid(),
            VersionNumber = 1,
            Data = "data",
            ContentHash = "hash"
        };

        // Assert defaults
        Assert.False(entry.IsCompressed);
        Assert.Null(entry.CompressionType);
        Assert.Equal(0, entry.SizeBytes);
        Assert.False(entry.IsDelta);
    }

    #endregion

    #region PendingUploadEntry Tests

    [Fact]
    public void PendingUploadEntry_GetStateKey_ReturnsCorrectFormat()
    {
        // Arrange
        var uploadId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var entry = new PendingUploadEntry
        {
            UploadId = uploadId,
            SlotId = Guid.NewGuid(),
            VersionNumber = 3,
            GameId = "game",
            OwnerId = Guid.NewGuid(),
            OwnerType = OwnerType.ACCOUNT,
            Data = "data",
            ContentHash = "hash"
        };

        // Act
        var key = entry.GetStateKey();

        // Assert
        Assert.Equal($"pending:{uploadId}", key);
    }

    [Fact]
    public void PendingUploadEntry_GetStateKeyStatic_ReturnsCorrectFormat()
    {
        // Arrange & Act
        var key = PendingUploadEntry.GetStateKey("some-upload-id");

        // Assert
        Assert.Equal("pending:some-upload-id", key);
    }

    [Fact]
    public void PendingUploadEntry_InstanceAndStaticKeys_Match()
    {
        // Arrange
        var uploadId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var entry = new PendingUploadEntry
        {
            UploadId = uploadId,
            SlotId = Guid.NewGuid(),
            VersionNumber = 1,
            GameId = "game",
            OwnerId = Guid.NewGuid(),
            OwnerType = OwnerType.CHARACTER,
            Data = "data",
            ContentHash = "hash"
        };

        // Act
        var instanceKey = entry.GetStateKey();
        var staticKey = PendingUploadEntry.GetStateKey(entry.UploadId.ToString());

        // Assert
        Assert.Equal(instanceKey, staticKey);
    }

    [Fact]
    public void PendingUploadEntry_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var entry = new PendingUploadEntry
        {
            UploadId = Guid.NewGuid(),
            SlotId = Guid.NewGuid(),
            VersionNumber = 1,
            GameId = "game",
            OwnerId = Guid.NewGuid(),
            OwnerType = OwnerType.ACCOUNT,
            Data = "data",
            ContentHash = "hash"
        };

        // Assert defaults
        Assert.Equal(CompressionType.GZIP, entry.CompressionType);
        Assert.Equal(0, entry.SizeBytes);
        Assert.Equal(0, entry.CompressedSizeBytes);
        Assert.False(entry.IsDelta);
        Assert.Null(entry.BaseVersionNumber);
        Assert.Null(entry.DeltaAlgorithm);
        Assert.Null(entry.ThumbnailData);
        Assert.Equal(0, entry.AttemptCount);
        Assert.Null(entry.LastError);
        Assert.Null(entry.LastAttemptAt);
        Assert.Equal(0, entry.Priority);
    }

    #endregion

    #region SaveSchemaDefinition Tests

    [Fact]
    public void SaveSchemaDefinition_GetStateKey_ReturnsCorrectFormat()
    {
        // Arrange & Act
        var key = SaveSchemaDefinition.GetStateKey("arcadia", "v1.0.0");

        // Assert
        Assert.Equal("arcadia:v1.0.0", key);
    }

    [Fact]
    public void SaveSchemaDefinition_GetStateKey_WithSpecialCharacters()
    {
        // Arrange & Act
        var key = SaveSchemaDefinition.GetStateKey("my-game", "2025.01.01");

        // Assert
        Assert.Equal("my-game:2025.01.01", key);
    }

    [Fact]
    public void SaveSchemaDefinition_HasMigration_TrueWhenPatchExists()
    {
        // Arrange
        var schema = new SaveSchemaDefinition
        {
            MigrationPatchJson = """[{"op":"add","path":"/field","value":1}]"""
        };

        // Act & Assert
        Assert.True(schema.HasMigration);
    }

    [Fact]
    public void SaveSchemaDefinition_HasMigration_FalseWhenPatchNull()
    {
        // Arrange
        var schema = new SaveSchemaDefinition
        {
            MigrationPatchJson = null
        };

        // Act & Assert
        Assert.False(schema.HasMigration);
    }

    [Fact]
    public void SaveSchemaDefinition_HasMigration_FalseWhenPatchEmpty()
    {
        // Arrange
        var schema = new SaveSchemaDefinition
        {
            MigrationPatchJson = ""
        };

        // Act & Assert
        Assert.False(schema.HasMigration);
    }

    [Fact]
    public void SaveSchemaDefinition_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var schema = new SaveSchemaDefinition();

        // Assert defaults
        Assert.Equal(string.Empty, schema.Namespace);
        Assert.Equal(string.Empty, schema.SchemaVersion);
        Assert.Equal(string.Empty, schema.SchemaJson);
        Assert.Null(schema.PreviousVersion);
        Assert.Null(schema.MigrationPatchJson);
        Assert.False(schema.HasMigration);
    }

    #endregion

    #region Key Uniqueness Tests

    [Fact]
    public void StateKeys_AreUniqueAcrossModels()
    {
        // Arrange - create keys with same base values
        var slotKey = SaveSlotMetadata.GetStateKey("game", "ACCOUNT", "owner", "slot");
        var versionKey = SaveVersionManifest.GetStateKey("slot", 1);
        var hotKey = HotSaveEntry.GetStateKey("slot", 1);
        var pendingKey = PendingUploadEntry.GetStateKey("upload-id");
        var schemaKey = SaveSchemaDefinition.GetStateKey("namespace", "version");

        // Act & Assert - all keys should be different due to prefixes
        var allKeys = new[] { slotKey, versionKey, hotKey, pendingKey, schemaKey };
        Assert.Equal(allKeys.Length, allKeys.Distinct().Count());
    }

    [Fact]
    public void StateKeys_HaveConsistentPrefixes()
    {
        // Assert prefixes match expected pattern
        Assert.StartsWith("slot:", SaveSlotMetadata.GetStateKey("g", "t", "o", "n"));
        Assert.StartsWith("version:", SaveVersionManifest.GetStateKey("s", 1));
        Assert.StartsWith("hot:", HotSaveEntry.GetStateKey("s", 1));
        Assert.StartsWith("pending:", PendingUploadEntry.GetStateKey("id"));
        // SaveSchemaDefinition uses namespace:version without prefix (by design)
    }

    #endregion
}
