namespace BeyondImmersion.Bannou.AssetBundler.Upload;

/// <summary>
/// Configuration options for the Bannou uploader.
/// </summary>
public sealed class UploaderOptions
{
    /// <summary>
    /// Bannou service URL (e.g., "wss://bannou.example.com").
    /// </summary>
    public required string ServerUrl { get; init; }

    /// <summary>
    /// Service token for authentication (preferred for automation).
    /// </summary>
    public string? ServiceToken { get; init; }

    /// <summary>
    /// Email for password-based authentication.
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// Password for password-based authentication.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Owner identifier for uploaded assets.
    /// </summary>
    public required string Owner { get; init; }
}
