using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

namespace BeyondImmersion.BannouService.Scene;

// =============================================================================
// SceneService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by SceneService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (SceneService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in ISceneService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (SceneService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for SceneService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class SceneService
{
    #region Private Helper Methods

    /// <summary>
    /// Stores scene content as YAML in the state store.
    /// </summary>
    /// <param name="scene">The scene to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="existingAssetId">Existing asset ID for updates (not used in state store approach).</param>
    /// <returns>The asset ID (scene ID) for the stored content.</returns>
    private async Task<Guid> StoreSceneAssetAsync(Scene scene, CancellationToken cancellationToken, Guid? existingAssetId = null)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.StoreSceneAssetAsync");
        var yaml = YamlSerializer.Serialize(scene);
        var sceneId = scene.SceneId;
        var contentKey = BuildSceneContentKey(sceneId);

        // Store the YAML content directly in state store

        var contentEntry = new SceneContentEntry
        {
            SceneId = sceneId,
            Version = scene.Version,
            Content = yaml,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _contentStore.SaveAsync(contentKey, contentEntry, null, cancellationToken);

        return sceneId;
    }

    /// <summary>
    /// Loads scene content from the state store.
    /// </summary>
    /// <param name="assetId">The scene/asset ID to load.</param>
    /// <param name="version">Optional version (not currently supported for state store).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized scene, or null if not found.</returns>
    private async Task<Scene?> LoadSceneAssetAsync(Guid assetId, string? version, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.LoadSceneAssetAsync");
        var contentKey = BuildSceneContentKey(assetId);

        var contentEntry = await _contentStore.GetAsync(contentKey, cancellationToken);
        if (contentEntry == null || string.IsNullOrEmpty(contentEntry.Content))
        {
            return null;
        }

        // Deserialize YAML to Scene
        return YamlDeserializer.Deserialize<Scene>(contentEntry.Content);
    }

    /// <summary>
    /// Gets version history for a scene.
    /// </summary>
    /// <param name="sceneId">The scene ID to get history for.</param>
    /// <param name="limit">Maximum number of versions to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of version info entries, newest first.</returns>
    private async Task<List<VersionInfo>> GetAssetVersionHistoryAsync(Guid sceneId, int limit, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.GetAssetVersionHistoryAsync");

        var historyKey = BuildSceneVersionHistoryKey(sceneId);

        var historyEntries = await _historyStore.GetAsync(historyKey, cancellationToken);
        if (historyEntries == null || historyEntries.Count == 0)
        {
            return new List<VersionInfo>();
        }

        // Return newest first, limited to requested count
        return historyEntries
            .OrderByDescending(h => h.CreatedAt)
            .Take(limit)
            .Select(h => new VersionInfo
            {
                Version = h.Version,
                CreatedAt = h.CreatedAt,
                CreatedBy = h.CreatedBy
            })
            .ToList();
    }

    /// <summary>
    /// Adds a version history entry for a scene.
    /// </summary>
    private async Task AddVersionHistoryEntryAsync(Guid sceneId, string version, string? editorId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.AddVersionHistoryEntryAsync");

        var historyKey = BuildSceneVersionHistoryKey(sceneId);

        var historyEntries = await _historyStore.GetAsync(historyKey, cancellationToken) ?? new List<VersionHistoryEntry>();

        historyEntries.Add(new VersionHistoryEntry
        {
            Version = version,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = editorId
        });

        // Enforce retention limit
        var maxVersions = _configuration.MaxVersionRetentionCount;
        if (historyEntries.Count > maxVersions)
        {
            historyEntries = historyEntries
                .OrderByDescending(h => h.CreatedAt)
                .Take(maxVersions)
                .ToList();
        }

        await _historyStore.SaveAsync(historyKey, historyEntries, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Deletes all version history for a scene.
    /// </summary>
    private async Task DeleteVersionHistoryAsync(Guid sceneId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.DeleteVersionHistoryAsync");

        var historyKey = BuildSceneVersionHistoryKey(sceneId);
        await _historyStore.DeleteAsync(historyKey, cancellationToken);
    }

    /// <summary>
    /// Atomically modifies a HashSet index with optimistic concurrency retry.
    /// Per IMPLEMENTATION TENETS multi-instance safety.
    /// </summary>
    private async Task ModifyGuidSetIndexAsync(
        string key,
        Action<HashSet<Guid>> modify,
        CancellationToken ct,
        int maxRetries = 3)
    {
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            var (existing, etag) = await _guidSetStore.GetWithETagAsync(key, ct);
            var set = existing ?? new HashSet<Guid>();
            modify(set);
            var result = await _guidSetStore.TrySaveAsync(key, set, etag ?? string.Empty, cancellationToken: ct);
            if (result != null) return;
            _logger.LogDebug("Concurrent modification on index {Key}, retry {Attempt}", key, attempt + 1);
        }
        _logger.LogWarning("Failed to update index {Key} after {MaxRetries} retries", key, maxRetries);
    }

    private async Task UpdateSceneIndexesAsync(Scene newScene, Scene? oldScene, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.UpdateSceneIndexesAsync");

        // Remove from old game/type indexes if changed
        if (oldScene != null && oldScene.GameId != newScene.GameId)
        {
            var oldGameKey = BuildSceneByGameKey(oldScene.GameId ?? "all");
            await ModifyGuidSetIndexAsync(oldGameKey, set => set.Remove(newScene.SceneId), cancellationToken);
        }

        if (oldScene != null && (oldScene.GameId != newScene.GameId || oldScene.SceneType != newScene.SceneType))
        {
            var oldTypeKey = BuildSceneByTypeKey(oldScene.GameId ?? "all", oldScene.SceneType);
            await ModifyGuidSetIndexAsync(oldTypeKey, set => set.Remove(newScene.SceneId), cancellationToken);
        }

        // Update game index
        var gameKey = BuildSceneByGameKey(newScene.GameId ?? "all");
        await ModifyGuidSetIndexAsync(gameKey, set => set.Add(newScene.SceneId), cancellationToken);

        // Update type index
        var typeKey = BuildSceneByTypeKey(newScene.GameId ?? "all", newScene.SceneType);
        await ModifyGuidSetIndexAsync(typeKey, set => set.Add(newScene.SceneId), cancellationToken);

        // Update reference tracking
        var newReferences = ExtractSceneReferences(newScene.Root);
        var oldReferences = oldScene != null ? ExtractSceneReferences(oldScene.Root) : new HashSet<Guid>();

        // Add new references
        foreach (var refSceneId in newReferences.Except(oldReferences))
        {
            var refKey = BuildSceneReferencesKey(refSceneId);
            await ModifyGuidSetIndexAsync(refKey, set => set.Add(newScene.SceneId), cancellationToken);
        }

        // Remove old references
        foreach (var refSceneId in oldReferences.Except(newReferences))
        {
            var refKey = BuildSceneReferencesKey(refSceneId);
            await ModifyGuidSetIndexAsync(refKey, set => set.Remove(newScene.SceneId), cancellationToken);
        }

        // Update asset usage tracking
        var newAssets = ExtractAssetReferences(newScene.Root);
        var oldAssets = oldScene != null ? ExtractAssetReferences(oldScene.Root) : new HashSet<Guid>();

        foreach (var assetId in newAssets.Except(oldAssets))
        {
            var assetKey = BuildSceneAssetsKey(assetId);
            await ModifyGuidSetIndexAsync(assetKey, set => set.Add(newScene.SceneId), cancellationToken);
        }

        foreach (var assetId in oldAssets.Except(newAssets))
        {
            var assetKey = BuildSceneAssetsKey(assetId);
            await ModifyGuidSetIndexAsync(assetKey, set => set.Remove(newScene.SceneId), cancellationToken);
        }
    }

    private async Task RemoveFromIndexesAsync(SceneIndexEntry indexEntry, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.RemoveFromIndexesAsync");

        // Remove from game index
        var gameKey = BuildSceneByGameKey(indexEntry.GameId ?? "all");
        await ModifyGuidSetIndexAsync(gameKey, set => set.Remove(indexEntry.SceneId), cancellationToken);

        // Remove from type index
        var typeKey = BuildSceneByTypeKey(indexEntry.GameId ?? "all", indexEntry.SceneType);
        await ModifyGuidSetIndexAsync(typeKey, set => set.Remove(indexEntry.SceneId), cancellationToken);
    }

    /// <summary>
    /// Gets all scene IDs in the system from the global index.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Set of all scene IDs.</returns>
    private async Task<HashSet<Guid>> GetAllSceneIdsAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.GetAllSceneIdsAsync");

        var globalIndex = await _guidSetStore.GetAsync(SCENE_GLOBAL_INDEX_KEY, cancellationToken);
        return globalIndex ?? new HashSet<Guid>();
    }

    /// <summary>
    /// Adds a scene ID to the global index.
    /// </summary>
    private async Task AddToGlobalSceneIndexAsync(Guid sceneId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.AddToGlobalSceneIndexAsync");

        await ModifyGuidSetIndexAsync(SCENE_GLOBAL_INDEX_KEY, set => set.Add(sceneId), cancellationToken);
    }

    /// <summary>
    /// Removes a scene ID from the global index.
    /// </summary>
    private async Task RemoveFromGlobalSceneIndexAsync(Guid sceneId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.RemoveFromGlobalSceneIndexAsync");

        await ModifyGuidSetIndexAsync(SCENE_GLOBAL_INDEX_KEY, set => set.Remove(sceneId), cancellationToken);
    }

    private async Task<(List<ResolvedReference>, List<UnresolvedReference>, List<string>)> ResolveReferencesAsync(
        Scene scene, int maxDepth, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.ResolveReferencesAsync");
        var resolved = new List<ResolvedReference>();
        var unresolved = new List<UnresolvedReference>();
        var errors = new List<string>();
        var visited = new HashSet<Guid> { scene.SceneId };

        await ResolveReferencesRecursiveAsync(scene.Root, 1, maxDepth, visited, resolved, unresolved, errors, cancellationToken);

        return (resolved, unresolved, errors);
    }

    private async Task ResolveReferencesRecursiveAsync(
        SceneNode node, int depth, int maxDepth, HashSet<Guid> visited,
        List<ResolvedReference> resolved, List<UnresolvedReference> unresolved, List<string> errors,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.ResolveReferencesRecursiveAsync");
        if (node.NodeType == NodeType.Reference)
        {
            var referencedSceneId = GetReferenceSceneId(node);
            if (referencedSceneId.HasValue)
            {
                if (depth > maxDepth)
                {
                    unresolved.Add(new UnresolvedReference
                    {
                        NodeId = node.NodeId,
                        RefId = node.RefId,
                        ReferencedSceneId = referencedSceneId.Value,
                        Reason = UnresolvedReferenceReason.DepthExceeded
                    });
                    errors.Add($"Reference at node '{node.RefId}' exceeds max depth {maxDepth}");
                }
                else if (visited.Contains(referencedSceneId.Value))
                {
                    unresolved.Add(new UnresolvedReference
                    {
                        NodeId = node.NodeId,
                        RefId = node.RefId,
                        ReferencedSceneId = referencedSceneId.Value,
                        Reason = UnresolvedReferenceReason.CircularReference,
                        CyclePath = visited.ToList()
                    });
                    errors.Add($"Circular reference detected at node '{node.RefId}'");
                }
                else
                {

                    var indexEntry = await _indexStore.GetAsync(BuildSceneIndexKey(referencedSceneId.Value), cancellationToken);

                    if (indexEntry == null)
                    {
                        unresolved.Add(new UnresolvedReference
                        {
                            NodeId = node.NodeId,
                            RefId = node.RefId,
                            ReferencedSceneId = referencedSceneId.Value,
                            Reason = UnresolvedReferenceReason.NotFound
                        });
                        errors.Add($"Referenced scene '{referencedSceneId.Value}' not found");
                    }
                    else
                    {
                        var referencedScene = await LoadSceneAssetAsync(indexEntry.AssetId, null, cancellationToken);
                        if (referencedScene != null)
                        {
                            resolved.Add(new ResolvedReference
                            {
                                NodeId = node.NodeId,
                                RefId = node.RefId,
                                ReferencedSceneId = referencedSceneId.Value,
                                ReferencedVersion = referencedScene.Version,
                                Scene = referencedScene,
                                Depth = depth
                            });

                            // Recursively resolve references in the referenced scene
                            visited.Add(referencedSceneId.Value);
                            await ResolveReferencesRecursiveAsync(referencedScene.Root, depth + 1, maxDepth, visited, resolved, unresolved, errors, cancellationToken);
                        }
                    }
                }
            }
        }

        // Process children
        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                await ResolveReferencesRecursiveAsync(child, depth, maxDepth, visited, resolved, unresolved, errors, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Extracts the referenced scene ID from a reference node using the typed ReferenceSceneId field.
    /// </summary>
    private static Guid? GetReferenceSceneId(SceneNode node)
    {
        return node.ReferenceSceneId;
    }

    private HashSet<Guid> ExtractSceneReferences(SceneNode root)
    {
        var references = new HashSet<Guid>();
        ExtractSceneReferencesRecursive(root, references);
        return references;
    }

    private void ExtractSceneReferencesRecursive(SceneNode node, HashSet<Guid> references)
    {
        if (node.NodeType == NodeType.Reference)
        {
            var sceneId = GetReferenceSceneId(node);
            if (sceneId.HasValue)
            {
                references.Add(sceneId.Value);
            }
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                ExtractSceneReferencesRecursive(child, references);
            }
        }
    }

    private HashSet<Guid> ExtractAssetReferences(SceneNode root)
    {
        var assets = new HashSet<Guid>();
        ExtractAssetReferencesRecursive(root, assets);
        return assets;
    }

    private void ExtractAssetReferencesRecursive(SceneNode node, HashSet<Guid> assets)
    {
        if (node.Asset != null)
        {
            assets.Add(node.Asset.AssetId);
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                ExtractAssetReferencesRecursive(child, assets);
            }
        }
    }

    private List<SceneNode> FindReferenceNodes(SceneNode root, Guid targetSceneId)
    {
        var nodes = new List<SceneNode>();
        FindReferenceNodesRecursive(root, targetSceneId, nodes);
        return nodes;
    }

    private void FindReferenceNodesRecursive(SceneNode node, Guid targetSceneId, List<SceneNode> nodes)
    {
        if (node.NodeType == NodeType.Reference)
        {
            var sceneId = GetReferenceSceneId(node);
            if (sceneId == targetSceneId)
            {
                nodes.Add(node);
            }
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                FindReferenceNodesRecursive(child, targetSceneId, nodes);
            }
        }
    }

    private List<SceneNode> FindAssetNodes(SceneNode root, Guid targetAssetId)
    {
        var nodes = new List<SceneNode>();
        FindAssetNodesRecursive(root, targetAssetId, nodes);
        return nodes;
    }

    private void FindAssetNodesRecursive(SceneNode node, Guid targetAssetId, List<SceneNode> nodes)
    {
        if (node.Asset != null && node.Asset.AssetId == targetAssetId)
        {
            nodes.Add(node);
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                FindAssetNodesRecursive(child, targetAssetId, nodes);
            }
        }
    }

    private int CountNodes(SceneNode root)
    {
        var count = 1;
        if (root.Children != null)
        {
            foreach (var child in root.Children)
            {
                count += CountNodes(child);
            }
        }
        return count;
    }

    /// <summary>
    /// Validates scene-level and node-level tag counts against configuration limits.
    /// </summary>
    /// <param name="scene">The scene to validate.</param>
    /// <returns>Error message if validation fails, null if valid.</returns>
    private string? ValidateTagCounts(Scene scene)
    {
        // Validate scene-level tags
        if (scene.Tags != null && scene.Tags.Count > _configuration.MaxTagsPerScene)
        {
            return $"Scene has {scene.Tags.Count} tags, exceeds maximum of {_configuration.MaxTagsPerScene}";
        }

        // Validate per-node tags
        if (scene.Root != null)
        {
            var nodeError = ValidateNodeTagsRecursive(scene.Root);
            if (nodeError != null)
            {
                return nodeError;
            }
        }

        return null;
    }

    private string? ValidateNodeTagsRecursive(SceneNode node)
    {
        if (node.Tags != null && node.Tags.Count > _configuration.MaxTagsPerNode)
        {
            return $"Node '{node.RefId}' has {node.Tags.Count} tags, exceeds maximum of {_configuration.MaxTagsPerNode}";
        }

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                var error = ValidateNodeTagsRecursive(child);
                if (error != null)
                {
                    return error;
                }
            }
        }

        return null;
    }

    private string IncrementPatchVersion(string version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return "1.0.1";
        }

        var parts = version.Split('.');
        if (parts.Length == 3 && int.TryParse(parts[2], out var patch))
        {
            return $"{parts[0]}.{parts[1]}.{patch + 1}";
        }

        return "1.0.1";
    }

    private SceneSummary CreateSceneSummary(SceneIndexEntry indexEntry)
    {
        return new SceneSummary
        {
            SceneId = indexEntry.SceneId,
            GameId = indexEntry.GameId,
            SceneType = indexEntry.SceneType,
            Name = indexEntry.Name,
            Description = indexEntry.Description,
            Version = indexEntry.Version,
            Tags = indexEntry.Tags,
            NodeCount = indexEntry.NodeCount,
            CreatedAt = indexEntry.CreatedAt,
            UpdatedAt = indexEntry.UpdatedAt,
            IsCheckedOut = indexEntry.IsCheckedOut
        };
    }

    private Scene DuplicateSceneWithNewIds(Scene source, string newName, string? newGameId, string? newSceneType)
    {
        var newSceneId = Guid.NewGuid();
        var idMapping = new Dictionary<Guid, Guid>();

        return new Scene
        {
            Schema = source.Schema,
            SceneId = newSceneId,
            GameId = newGameId ?? source.GameId,
            SceneType = newSceneType ?? source.SceneType,
            Name = newName,
            Description = source.Description,
            Version = "1.0.0",
            Root = DuplicateNodeWithNewIds(source.Root, idMapping),
            Tags = source.Tags?.ToList() ?? new List<string>(),
            Metadata = source.Metadata
        };
    }

    private SceneNode DuplicateNodeWithNewIds(SceneNode source, Dictionary<Guid, Guid> idMapping)
    {
        var newNodeId = Guid.NewGuid();
        idMapping[source.NodeId] = newNodeId;

        Guid? newParentId = null;
        if (source.ParentNodeId != null && idMapping.TryGetValue(source.ParentNodeId.Value, out var mappedParentId))
        {
            newParentId = mappedParentId;
        }

        return new SceneNode
        {
            NodeId = newNodeId,
            RefId = source.RefId,
            ParentNodeId = newParentId,
            Name = source.Name,
            NodeType = source.NodeType,
            LocalTransform = source.LocalTransform,
            Asset = source.Asset,
            Children = source.Children?.Select(c => DuplicateNodeWithNewIds(c, idMapping)).ToList() ?? new List<SceneNode>(),
            Enabled = source.Enabled,
            SortOrder = source.SortOrder,
            Tags = source.Tags?.ToList() ?? new List<string>(),
            Annotations = source.Annotations
        };
    }

    private async Task PublishSceneCreatedEventAsync(Scene scene, int nodeCount, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.PublishSceneCreatedEventAsync");
        var eventModel = new SceneCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = scene.SceneId,
            GameId = scene.GameId,
            SceneType = scene.SceneType,
            Name = scene.Name,
            Description = scene.Description,
            Version = scene.Version,
            Tags = scene.Tags?.ToList() ?? new List<string>(),
            NodeCount = nodeCount,
            CreatedAt = scene.CreatedAt,
            UpdatedAt = scene.UpdatedAt
        };

        await _messageBus.PublishSceneCreatedAsync(eventModel, cancellationToken);
    }

    private async Task PublishSceneUpdatedEventAsync(Scene scene, string previousVersion, int nodeCount, ICollection<string> changedFields, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.PublishSceneUpdatedEventAsync");
        var eventModel = new SceneUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = scene.SceneId,
            GameId = scene.GameId,
            SceneType = scene.SceneType,
            Name = scene.Name,
            Description = scene.Description,
            Version = scene.Version,
            Tags = scene.Tags?.ToList() ?? new List<string>(),
            NodeCount = nodeCount,
            CreatedAt = scene.CreatedAt,
            UpdatedAt = scene.UpdatedAt,
            ChangedFields = changedFields
        };

        await _messageBus.PublishSceneUpdatedAsync(eventModel, cancellationToken);
    }

    private async Task PublishSceneDeletedEventAsync(Scene scene, int nodeCount, string? reason, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.scene", "SceneService.PublishSceneDeletedEventAsync");
        var eventModel = new SceneDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SceneId = scene.SceneId,
            GameId = scene.GameId,
            SceneType = scene.SceneType,
            Name = scene.Name,
            Description = scene.Description,
            Version = scene.Version,
            Tags = scene.Tags?.ToList() ?? new List<string>(),
            NodeCount = nodeCount,
            CreatedAt = scene.CreatedAt,
            UpdatedAt = scene.UpdatedAt
        };

        await _messageBus.PublishSceneDeletedAsync(eventModel, cancellationToken);
    }

    #endregion
}
