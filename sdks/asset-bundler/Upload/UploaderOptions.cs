namespace BeyondImmersion.Bannou.AssetBundler.Upload;

/// <summary>
/// Configuration options for the Bannou uploader.
/// </summary>
public sealed class UploaderOptions
{
    /// <summary>
    /// Bannou service URL (e.g., "wss://bannou.example.com" or "https://bannou.example.com").
    /// For email/password auth, HTTP(S) URLs are converted to WS(S) for WebSocket connections.
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

    /// <summary>
    /// Whether to use manual HTTP login (POST /auth/login) before WebSocket connection.
    /// Useful when the server doesn't support auto-redirect or for debugging.
    /// Default is false (use BannouClient's built-in auth).
    /// </summary>
    public bool UseManualLogin { get; init; } = false;

    /// <summary>
    /// HTTP timeout for upload operations in milliseconds.
    /// Default is 5 minutes.
    /// </summary>
    public int UploadTimeoutMs { get; init; } = 300_000;

    /// <summary>
    /// Whether to enable verbose logging for uploads.
    /// </summary>
    public bool VerboseLogging { get; init; } = false;
}
