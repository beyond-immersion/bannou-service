using BeyondImmersion.BannouService.Services;
using LibGit2Sharp;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Documentation.Services;

/// <summary>
/// Git repository synchronization service using LibGit2Sharp.
/// Provides clone, pull, and file discovery operations for documentation bindings.
/// </summary>
public class GitSyncService : IGitSyncService
{
    private readonly ILogger<GitSyncService> _logger;
    private readonly DocumentationServiceConfiguration _configuration;
    private readonly IMessageBus _messageBus;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new instance of the GitSyncService.
    /// </summary>
    public GitSyncService(
        ILogger<GitSyncService> logger,
        DocumentationServiceConfiguration configuration,
        IMessageBus messageBus,
        ITelemetryProvider telemetryProvider)
    {
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration, nameof(configuration));
        ArgumentNullException.ThrowIfNull(messageBus, nameof(messageBus));
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));

        _logger = logger;
        _configuration = configuration;
        _messageBus = messageBus;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc />
    public async Task<GitSyncResult> SyncRepositoryAsync(
        string repositoryUrl,
        string branch,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "GitSyncService.SyncRepositoryAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);

        // Apply configured timeout to prevent indefinitely hanging git operations
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_configuration.GitCloneTimeoutSeconds));
        var effectiveToken = timeoutCts.Token;

        return await Task.Run(async () =>
        {
            try
            {
                effectiveToken.ThrowIfCancellationRequested();

                // Check if repository already exists
                if (Repository.IsValid(localPath))
                {
                    return PullRepository(localPath, branch, effectiveToken);
                }
                else
                {
                    return CloneRepository(repositoryUrl, branch, localPath, effectiveToken);
                }
            }
            catch (OperationCanceledException)
            {
                var timedOut = !cancellationToken.IsCancellationRequested;
                var message = timedOut
                    ? $"Git operation timed out after {_configuration.GitCloneTimeoutSeconds}s"
                    : "Operation cancelled";
                _logger.LogInformation("Git sync operation {Reason} for {Repository}",
                    timedOut ? "timed out" : "cancelled", repositoryUrl);
                return GitSyncResult.Failed(message);
            }
            catch (LibGit2SharpException ex)
            {
                _logger.LogError(ex, "Git operation failed for {Repository}", repositoryUrl);
                await _messageBus.TryPublishErrorAsync(
                    "documentation",
                    "SyncRepository",
                    ex.GetType().Name,
                    ex.Message,
                    dependency: "git",
                    details: new { repositoryUrl, branch },
                    stack: ex.StackTrace);
                return GitSyncResult.Failed(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during git sync for {Repository}", repositoryUrl);
                await _messageBus.TryPublishErrorAsync(
                    "documentation",
                    "SyncRepository",
                    ex.GetType().Name,
                    ex.Message,
                    dependency: "git",
                    details: new { repositoryUrl, branch },
                    stack: ex.StackTrace);
                return GitSyncResult.Failed($"Unexpected error: {ex.Message}");
            }
        }, effectiveToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitFileChange>> GetChangedFilesAsync(
        string localPath,
        string? fromCommit,
        string toCommit,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "GitSyncService.GetChangedFilesAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(toCommit);

        return await Task.Run(async () =>
        {
            var changes = new List<GitFileChange>();

            try
            {
                using var repo = new Repository(localPath);

                var toCommitObj = repo.Lookup<Commit>(toCommit);
                if (toCommitObj == null)
                {
                    _logger.LogWarning("Commit {Commit} not found in repository", toCommit);
                    return changes;
                }

                TreeChanges? treeChanges;

                if (string.IsNullOrEmpty(fromCommit))
                {
                    // Compare against empty tree (all files are "added")
                    treeChanges = repo.Diff.Compare<TreeChanges>(null, toCommitObj.Tree);
                }
                else
                {
                    var fromCommitObj = repo.Lookup<Commit>(fromCommit);
                    if (fromCommitObj == null)
                    {
                        _logger.LogWarning("Commit {Commit} not found, treating as full sync", fromCommit);
                        treeChanges = repo.Diff.Compare<TreeChanges>(null, toCommitObj.Tree);
                    }
                    else
                    {
                        treeChanges = repo.Diff.Compare<TreeChanges>(fromCommitObj.Tree, toCommitObj.Tree);
                    }
                }

                foreach (var change in treeChanges)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var changeType = change.Status switch
                    {
                        ChangeKind.Added => GitChangeType.Added,
                        ChangeKind.Deleted => GitChangeType.Deleted,
                        ChangeKind.Renamed => GitChangeType.Renamed,
                        ChangeKind.Modified => GitChangeType.Modified,
                        ChangeKind.Copied => GitChangeType.Added,
                        _ => GitChangeType.Modified
                    };

                    // For renames and copies, use the new path
                    var filePath = change.Status == ChangeKind.Deleted ? change.OldPath : change.Path;
                    changes.Add(new GitFileChange(filePath, changeType));
                }

                _logger.LogDebug("Found {Count} changed files between {From} and {To}",
                    changes.Count, fromCommit ?? "(empty)", toCommit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting changed files in repository at {Path}", localPath);
                await _messageBus.TryPublishErrorAsync(
                    "documentation",
                    "GetChangedFiles",
                    ex.GetType().Name,
                    ex.Message,
                    dependency: "git",
                    details: new { localPath, fromCommit, toCommit },
                    stack: ex.StackTrace);
            }

            return changes;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetMatchingFilesAsync(
        string localPath,
        IEnumerable<string> includePatterns,
        IEnumerable<string> excludePatterns,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "GitSyncService.GetMatchingFilesAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);

        return await Task.Run(async () =>
        {
            var matcher = new Matcher();

            // Add include patterns
            foreach (var pattern in includePatterns)
            {
                matcher.AddInclude(pattern);
            }

            // Add exclude patterns
            foreach (var pattern in excludePatterns)
            {
                matcher.AddExclude(pattern);
            }

            try
            {
                var directoryInfo = new DirectoryInfo(localPath);
                var result = matcher.Execute(new DirectoryInfoWrapper(directoryInfo));

                var files = result.Files
                    .Select(f => f.Path.Replace('\\', '/'))
                    .ToList();

                _logger.LogDebug("Found {Count} matching files in repository at {Path}", files.Count, localPath);
                return files;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting matching files in repository at {Path}", localPath);
                await _messageBus.TryPublishErrorAsync(
                    "documentation",
                    "GetMatchingFiles",
                    ex.GetType().Name,
                    ex.Message,
                    dependency: "filesystem",
                    details: new { localPath },
                    stack: ex.StackTrace);
                return new List<string>();
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public string? GetHeadCommit(string localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return null;
        }

        try
        {
            if (!Repository.IsValid(localPath))
            {
                return null;
            }

            using var repo = new Repository(localPath);
            return repo.Head.Tip?.Sha;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting HEAD commit for repository at {Path}", localPath);
            // Fire-and-forget error publishing: this is a synchronous method and error is already logged;
            // per IMPLEMENTATION TENETS, use discard to avoid blocking on async call
            _ = _messageBus.TryPublishErrorAsync(
                "documentation",
                "GetHeadCommit",
                ex.GetType().Name,
                ex.Message,
                dependency: "git",
                details: new { localPath },
                stack: ex.StackTrace);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string> ReadFileContentAsync(
        string localPath,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "GitSyncService.ReadFileContentAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.Combine(localPath, filePath);

        try
        {
            return await File.ReadAllTextAsync(fullPath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            _logger.LogWarning("File not found: {FilePath}", fullPath);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file: {FilePath}", fullPath);
            await _messageBus.TryPublishErrorAsync(
                "documentation",
                "ReadFileContent",
                ex.GetType().Name,
                ex.Message,
                dependency: "filesystem",
                details: new { localPath, filePath, fullPath },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task CleanupRepositoryAsync(string localPath, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "GitSyncService.CleanupRepositoryAsync");
        if (string.IsNullOrWhiteSpace(localPath) || !Directory.Exists(localPath))
        {
            return;
        }

        await Task.Run(async () =>
        {
            try
            {
                // Make all files writable (git marks some as read-only)
                foreach (var file in Directory.EnumerateFiles(localPath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(localPath, recursive: true);
                _logger.LogInformation("Cleaned up repository at {Path}", localPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up repository at {Path}", localPath);
                await _messageBus.TryPublishErrorAsync(
                    "documentation",
                    "CleanupRepository",
                    ex.GetType().Name,
                    ex.Message,
                    dependency: "filesystem",
                    details: new { localPath },
                    stack: ex.StackTrace);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public bool RepositoryExists(string localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return false;
        }

        return Repository.IsValid(localPath);
    }

    #region Private Methods

    private GitSyncResult CloneRepository(
        string repositoryUrl,
        string branch,
        string localPath,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cloning repository {Url} to {Path}", repositoryUrl, localPath);

        // Ensure parent directory exists
        var parentDir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        var cloneOptions = new CloneOptions
        {
            BranchName = branch,
            // Note: LibGit2Sharp doesn't support --depth directly
            // For large repos, consider using ProcessStartInfo to shell out to git CLI
            // Cancellation is checked via the OnCheckoutProgress callback
            OnCheckoutProgress = (path, completedSteps, totalSteps) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        };

        try
        {
            Repository.Clone(repositoryUrl, localPath, cloneOptions);

            using var repo = new Repository(localPath);
            // Head.Tip can be null for an empty repository with no commits
            var commitHash = repo.Head.Tip?.Sha;

            _logger.LogInformation("Successfully cloned repository to {Path}, HEAD: {Commit}",
                localPath, commitHash?[..Math.Min(8, commitHash.Length)] ?? "(empty)");

            return GitSyncResult.Succeeded(commitHash, isClone: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clone repository {Url}", repositoryUrl);
            // Fire-and-forget error publishing: this is a synchronous method and error is already logged;
            // per IMPLEMENTATION TENETS, use discard to avoid blocking on async call
            _ = _messageBus.TryPublishErrorAsync(
                "documentation",
                "CloneRepository",
                ex.GetType().Name,
                ex.Message,
                dependency: "git",
                details: new { repositoryUrl, branch, localPath },
                stack: ex.StackTrace);
            return GitSyncResult.Failed(ex.Message);
        }
    }

    private GitSyncResult PullRepository(
        string localPath,
        string branch,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Pulling updates for repository at {Path}", localPath);

        try
        {
            using var repo = new Repository(localPath);

            // Checkout the correct branch if needed
            var targetBranch = repo.Branches[branch] ?? repo.Branches[$"origin/{branch}"];
            if (targetBranch == null)
            {
                return GitSyncResult.Failed($"Branch '{branch}' not found");
            }

            if (repo.Head.FriendlyName != branch)
            {
                Commands.Checkout(repo, targetBranch);
            }

            // Fetch from origin
            var remote = repo.Network.Remotes["origin"];
            if (remote == null)
            {
                return GitSyncResult.Failed("Remote 'origin' not found");
            }

            var fetchOptions = new FetchOptions();

            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
            Commands.Fetch(repo, remote.Name, refSpecs, fetchOptions, string.Empty);

            // Merge or fast-forward
            var trackingBranch = repo.Head.TrackedBranch;
            if (trackingBranch != null)
            {
                var signature = new Signature("Bannou", "bannou@system.local", DateTimeOffset.UtcNow);
                var mergeResult = repo.Merge(trackingBranch, signature, new MergeOptions());

                if (mergeResult.Status == MergeStatus.Conflicts)
                {
                    _logger.LogWarning("Merge conflicts detected, resetting to remote");
                    repo.Reset(ResetMode.Hard, trackingBranch.Tip);
                }
            }

            // Head.Tip can be null for an empty repository with no commits
            var commitHash = repo.Head.Tip?.Sha;

            _logger.LogInformation("Successfully pulled repository at {Path}, HEAD: {Commit}",
                localPath, commitHash?[..Math.Min(8, commitHash.Length)] ?? "(empty)");

            return GitSyncResult.Succeeded(commitHash, isClone: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pull repository at {Path}", localPath);
            // Fire-and-forget error publishing: this is a synchronous method and error is already logged;
            // per IMPLEMENTATION TENETS, use discard to avoid blocking on async call
            _ = _messageBus.TryPublishErrorAsync(
                "documentation",
                "PullRepository",
                ex.GetType().Name,
                ex.Message,
                dependency: "git",
                details: new { localPath, branch },
                stack: ex.StackTrace);
            return GitSyncResult.Failed(ex.Message);
        }
    }

    #endregion
}
