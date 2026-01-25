namespace BeyondImmersion.BannouService.Asset.Models;

/// <summary>
/// Internal representation of an asset record including storage details.
/// Used for internal service operations; the storage_key is not exposed to clients.
/// </summary>
public sealed class InternalAssetRecord
{
    /// <summary>
    /// Unique asset identifier.
    /// </summary>
    public required string AssetId { get; init; }

    /// <summary>
    /// SHA256 hash of file contents.
    /// </summary>
    public required string ContentHash { get; init; }

    /// <summary>
    /// Original filename.
    /// </summary>
    public required string Filename { get; init; }

    /// <summary>
    /// MIME content type.
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public required long Size { get; init; }

    /// <summary>
    /// Asset type classification.
    /// </summary>
    public required AssetType AssetType { get; init; }

    /// <summary>
    /// Realm stub name the asset belongs to.
    /// </summary>
    public string? Realm { get; init; }

    /// <summary>
    /// Storage key (path) in the object store.
    /// </summary>
    public required string StorageKey { get; init; }

    /// <summary>
    /// Storage bucket name.
    /// </summary>
    public required string Bucket { get; init; }

    /// <summary>
    /// Optional version ID in storage.
    /// </summary>
    public string? VersionId { get; init; }

    /// <summary>
    /// Tags associated with the asset.
    /// </summary>
    public ICollection<string>? Tags { get; init; }

    /// <summary>
    /// Processing status of the asset.
    /// </summary>
    public required ProcessingStatus ProcessingStatus { get; init; }

    /// <summary>
    /// When the asset was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the asset was last updated.
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Convert to public AssetMetadata (without storage_key).
    /// </summary>
    public AssetMetadata ToPublicMetadata()
    {
        return new AssetMetadata
        {
            AssetId = AssetId,
            ContentHash = ContentHash,
            Filename = Filename,
            ContentType = ContentType,
            Size = Size,
            AssetType = AssetType,
            Realm = Realm ?? "shared",
            Tags = Tags ?? new List<string>(),
            ProcessingStatus = ProcessingStatus,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }
}
