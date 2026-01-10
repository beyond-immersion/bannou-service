namespace BeyondImmersion.BannouService.Documentation.Services;

/// <summary>
/// Interface for git repository synchronization operations.
/// Provides clone, pull, and file discovery capabilities for repository bindings.
/// </summary>
public interface IGitSyncService
{
    /// <summary>
    /// Clones or pulls a repository to local storage.
    /// If the repository already exists locally, performs a pull.
    /// </summary>
    /// <param name="repositoryUrl">The git clone URL (HTTPS for public repos).</param>
    /// <param name="branch">The branch to checkout.</param>
    /// <param name="localPath">The local filesystem path for the repository.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the current commit hash and any errors.</returns>
    Task<GitSyncResult> SyncRepositoryAsync(
        string repositoryUrl,
        string branch,
        string localPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of files changed between two commits.
    /// Used for incremental sync operations.
    /// </summary>
    /// <param name="localPath">The local repository path.</param>
    /// <param name="fromCommit">The starting commit hash (null for full comparison).</param>
    /// <param name="toCommit">The ending commit hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of changed files with their change type.</returns>
    Task<IReadOnlyList<GitFileChange>> GetChangedFilesAsync(
        string localPath,
        string? fromCommit,
        string toCommit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all files in the repository matching the specified patterns.
    /// Used for full sync operations.
    /// </summary>
    /// <param name="localPath">The local repository path.</param>
    /// <param name="includePatterns">Glob patterns for files to include (e.g., "**/*.md").</param>
    /// <param name="excludePatterns">Glob patterns for files to exclude (e.g., ".git/**").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching file paths relative to repository root.</returns>
    Task<IReadOnlyList<string>> GetMatchingFilesAsync(
        string localPath,
        IEnumerable<string> includePatterns,
        IEnumerable<string> excludePatterns,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current HEAD commit hash for a local repository.
    /// </summary>
    /// <param name="localPath">The local repository path.</param>
    /// <returns>The commit hash, or null if the repository doesn't exist.</returns>
    string? GetHeadCommit(string localPath);

    /// <summary>
    /// Reads the content of a file from the repository.
    /// </summary>
    /// <param name="localPath">The local repository path.</param>
    /// <param name="filePath">The file path relative to repository root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file content as a string.</returns>
    Task<string> ReadFileContentAsync(
        string localPath,
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up a local repository directory.
    /// </summary>
    /// <param name="localPath">The local repository path to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CleanupRepositoryAsync(string localPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a local repository exists and is valid.
    /// </summary>
    /// <param name="localPath">The local repository path.</param>
    /// <returns>True if the repository exists and is valid.</returns>
    bool RepositoryExists(string localPath);
}

/// <summary>
/// Result of a git sync operation.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="CommitHash">The current HEAD commit hash after sync.</param>
/// <param name="ErrorMessage">Error message if the operation failed.</param>
/// <param name="IsClone">True if this was a new clone, false if it was a pull.</param>
public record GitSyncResult(
    bool Success,
    string? CommitHash,
    string? ErrorMessage = null,
    bool IsClone = false)
{
    /// <summary>
    /// Creates a successful sync result.
    /// </summary>
    public static GitSyncResult Succeeded(string? commitHash, bool isClone = false)
        => new(true, commitHash, null, isClone);

    /// <summary>
    /// Creates a failed sync result.
    /// </summary>
    public static GitSyncResult Failed(string errorMessage)
        => new(false, null, errorMessage, false);
}

/// <summary>
/// Represents a file change in a git repository.
/// </summary>
/// <param name="FilePath">The file path relative to repository root.</param>
/// <param name="ChangeType">The type of change.</param>
public record GitFileChange(string FilePath, GitChangeType ChangeType);

/// <summary>
/// Type of change for a git file.
/// </summary>
public enum GitChangeType
{
    /// <summary>File was added.</summary>
    Added,

    /// <summary>File was modified.</summary>
    Modified,

    /// <summary>File was deleted.</summary>
    Deleted,

    /// <summary>File was renamed.</summary>
    Renamed
}
