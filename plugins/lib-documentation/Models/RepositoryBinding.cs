namespace BeyondImmersion.BannouService.Documentation.Models;

/// <summary>
/// Internal storage model for repository binding state.
/// Persisted in lib-state store with key pattern: repo-binding:{namespace}
/// </summary>
internal sealed class RepositoryBinding
{
    /// <summary>Gets or sets the unique binding identifier.</summary>
    public Guid BindingId { get; set; }

    /// <summary>Gets or sets the namespace this binding is for.</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>Gets or sets the git repository URL.</summary>
    public string RepositoryUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the branch to sync.</summary>
    public string Branch { get; set; } = "main";

    /// <summary>Gets or sets the binding status.</summary>
    public BindingStatusInternal Status { get; set; } = BindingStatusInternal.Pending;

    /// <summary>Gets or sets whether sync is enabled.</summary>
    public bool SyncEnabled { get; set; } = true;

    /// <summary>Gets or sets the sync interval in minutes.</summary>
    public int SyncIntervalMinutes { get; set; } = 60;

    /// <summary>Gets or sets file patterns to include.</summary>
    public List<string> FilePatterns { get; set; } = ["**/*.md"];

    /// <summary>Gets or sets file patterns to exclude.</summary>
    public List<string> ExcludePatterns { get; set; } = [".git/**", ".obsidian/**", "node_modules/**"];

    /// <summary>Gets or sets path-to-category mappings.</summary>
    public Dictionary<string, string> CategoryMapping { get; set; } = [];

    /// <summary>Gets or sets the default category.</summary>
    public DocumentCategory DefaultCategory { get; set; } = DocumentCategory.Other;

    /// <summary>Gets or sets whether archiving is enabled.</summary>
    public bool ArchiveEnabled { get; set; }

    /// <summary>Gets or sets whether to archive on each sync.</summary>
    public bool ArchiveOnSync { get; set; }

    /// <summary>Gets or sets the last successful sync time.</summary>
    public DateTimeOffset? LastSyncAt { get; set; }

    /// <summary>Gets or sets the last known commit hash.</summary>
    public string? LastCommitHash { get; set; }

    /// <summary>Gets or sets the current document count.</summary>
    public int DocumentCount { get; set; }

    /// <summary>Gets or sets when the binding was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the owner of this binding. NOT a session ID.
    /// Contains either an accountId (UUID format) for user-initiated bindings
    /// or a service name for service-initiated bindings.
    /// </summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>Gets or sets the last sync error message.</summary>
    public string? LastSyncError { get; set; }

    /// <summary>Gets or sets the next scheduled sync time.</summary>
    public DateTimeOffset? NextSyncAt { get; set; }
}

/// <summary>
/// Internal binding status enum to avoid dependency on generated types.
/// </summary>
internal enum BindingStatusInternal
{
    /// <summary>Binding created, awaiting first sync.</summary>
    Pending,

    /// <summary>Sync in progress.</summary>
    Syncing,

    /// <summary>Last sync completed successfully.</summary>
    Synced,

    /// <summary>Last sync failed with error.</summary>
    Error,

    /// <summary>Binding is disabled.</summary>
    Disabled
}

/// <summary>
/// Result of a sync operation.
/// </summary>
internal sealed class SyncResult
{
    /// <summary>Gets or sets the sync operation ID (null for conflict results).</summary>
    public Guid? SyncId { get; set; }

    /// <summary>Gets or sets the sync status.</summary>
    public SyncStatusInternal Status { get; set; }

    /// <summary>Gets or sets the commit hash after sync.</summary>
    public string? CommitHash { get; set; }

    /// <summary>Gets or sets the number of documents created.</summary>
    public int DocumentsCreated { get; set; }

    /// <summary>Gets or sets the number of documents updated.</summary>
    public int DocumentsUpdated { get; set; }

    /// <summary>Gets or sets the number of documents deleted.</summary>
    public int DocumentsDeleted { get; set; }

    /// <summary>Gets or sets the duration in milliseconds.</summary>
    public int DurationMs { get; set; }

    /// <summary>Gets or sets the error message if failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Gets or sets when the sync started.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Gets or sets when the sync completed.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Creates a success result.</summary>
    public static SyncResult Success(
        Guid syncId,
        string? commitHash,
        int created,
        int updated,
        int deleted,
        DateTimeOffset startedAt)
    {
        var now = DateTimeOffset.UtcNow;
        return new SyncResult
        {
            SyncId = syncId,
            Status = SyncStatusInternal.Success,
            CommitHash = commitHash,
            DocumentsCreated = created,
            DocumentsUpdated = updated,
            DocumentsDeleted = deleted,
            StartedAt = startedAt,
            CompletedAt = now,
            DurationMs = (int)(now - startedAt).TotalMilliseconds
        };
    }

    /// <summary>Creates a partial success result.</summary>
    public static SyncResult Partial(
        Guid syncId,
        string commitHash,
        int created,
        int updated,
        int deleted,
        string errorMessage,
        DateTimeOffset startedAt)
    {
        var now = DateTimeOffset.UtcNow;
        return new SyncResult
        {
            SyncId = syncId,
            Status = SyncStatusInternal.Partial,
            CommitHash = commitHash,
            DocumentsCreated = created,
            DocumentsUpdated = updated,
            DocumentsDeleted = deleted,
            ErrorMessage = errorMessage,
            StartedAt = startedAt,
            CompletedAt = now,
            DurationMs = (int)(now - startedAt).TotalMilliseconds
        };
    }

    /// <summary>Creates a failed result.</summary>
    public static SyncResult Failed(Guid syncId, string errorMessage, DateTimeOffset startedAt)
    {
        var now = DateTimeOffset.UtcNow;
        return new SyncResult
        {
            SyncId = syncId,
            Status = SyncStatusInternal.Failed,
            ErrorMessage = errorMessage,
            StartedAt = startedAt,
            CompletedAt = now,
            DurationMs = (int)(now - startedAt).TotalMilliseconds
        };
    }

    /// <summary>Creates a conflict result (sync already in progress).</summary>
    public static SyncResult Conflict(string message)
    {
        return new SyncResult
        {
            SyncId = null,
            Status = SyncStatusInternal.Failed,
            ErrorMessage = message,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            DurationMs = 0
        };
    }
}

/// <summary>
/// Internal sync status enum.
/// </summary>
internal enum SyncStatusInternal
{
    /// <summary>Sync completed successfully.</summary>
    Success,

    /// <summary>Sync completed with some failures.</summary>
    Partial,

    /// <summary>Sync failed completely.</summary>
    Failed
}

/// <summary>
/// Internal model for archive storage.
/// </summary>
internal sealed class DocumentationArchive
{
    /// <summary>Gets or sets the archive ID.</summary>
    public Guid ArchiveId { get; set; }

    /// <summary>Gets or sets the namespace this archive is for.</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>Gets or sets the Asset Service bundle ID.</summary>
    public Guid BundleAssetId { get; set; }

    /// <summary>Gets or sets the number of documents in the archive.</summary>
    public int DocumentCount { get; set; }

    /// <summary>Gets or sets the archive size in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Gets or sets when the archive was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the commit hash at archive time.</summary>
    public string? CommitHash { get; set; }

    /// <summary>Gets or sets the description of the archive.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the owner of this archive. NOT a session ID.
    /// Contains either an accountId (UUID format) for user-initiated archives
    /// or a service name for service-initiated archives.
    /// </summary>
    public string Owner { get; set; } = string.Empty;
}
