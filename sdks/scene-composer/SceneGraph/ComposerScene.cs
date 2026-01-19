using BeyondImmersion.Bannou.SceneComposer.Abstractions;

namespace BeyondImmersion.Bannou.SceneComposer.SceneGraph;

/// <summary>
/// Represents a scene document being edited in the Scene Composer.
/// Contains the hierarchical structure of nodes and scene metadata.
/// </summary>
public class ComposerScene
{
    private readonly List<ComposerSceneNode> _rootNodes = new();
    private readonly Dictionary<Guid, ComposerSceneNode> _nodeIndex = new();

    /// <summary>
    /// Unique identifier for this scene.
    /// </summary>
    public string SceneId { get; }

    /// <summary>
    /// Display name of the scene.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Type of scene (building, room, prefab, etc.).
    /// </summary>
    public SceneType SceneType { get; }

    /// <summary>
    /// Game ID this scene belongs to.
    /// </summary>
    public string? GameId { get; set; }

    /// <summary>
    /// Tags for categorization and search.
    /// </summary>
    public List<string> Tags { get; } = new();

    /// <summary>
    /// Scene version (SchemaVer format: MODEL-REVISION-ADDITION).
    /// </summary>
    public string Version { get; internal set; } = "1-0-0";

    /// <summary>
    /// Schema version for the scene format.
    /// </summary>
    public int SchemaVersion { get; internal set; } = 1;

    /// <summary>
    /// Root-level nodes in the scene.
    /// </summary>
    public IReadOnlyList<ComposerSceneNode> RootNodes => _rootNodes;

    /// <summary>
    /// Total number of nodes in the scene.
    /// </summary>
    public int NodeCount => _nodeIndex.Count;

    /// <summary>
    /// Whether this is a new scene that hasn't been saved yet.
    /// </summary>
    public bool IsNew { get; internal set; } = true;

    /// <summary>
    /// Create a new scene.
    /// </summary>
    /// <param name="sceneId">Unique scene identifier.</param>
    /// <param name="name">Display name.</param>
    /// <param name="sceneType">Type of scene.</param>
    public ComposerScene(string sceneId, string name, SceneType sceneType)
    {
        SceneId = sceneId ?? throw new ArgumentNullException(nameof(sceneId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        SceneType = sceneType;
    }

    /// <summary>
    /// Get a node by its ID.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <returns>The node, or null if not found.</returns>
    public ComposerSceneNode? GetNode(Guid nodeId)
    {
        _nodeIndex.TryGetValue(nodeId, out var node);
        return node;
    }

    /// <summary>
    /// Check if a node exists in this scene.
    /// </summary>
    public bool ContainsNode(Guid nodeId) => _nodeIndex.ContainsKey(nodeId);

    /// <summary>
    /// Get all nodes in the scene (depth-first order).
    /// </summary>
    public IEnumerable<ComposerSceneNode> GetAllNodes()
    {
        foreach (var root in _rootNodes)
        {
            yield return root;
            foreach (var descendant in GetDescendants(root))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Get all descendants of a node (depth-first).
    /// </summary>
    public IEnumerable<ComposerSceneNode> GetDescendants(ComposerSceneNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var descendant in GetDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Get all ancestors of a node (parent, grandparent, etc.).
    /// </summary>
    public IEnumerable<ComposerSceneNode> GetAncestors(ComposerSceneNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            yield return current;
            current = current.Parent;
        }
    }

    /// <summary>
    /// Find nodes matching a predicate.
    /// </summary>
    public IEnumerable<ComposerSceneNode> FindNodes(Func<ComposerSceneNode, bool> predicate)
    {
        return GetAllNodes().Where(predicate);
    }

    /// <summary>
    /// Find nodes by name.
    /// </summary>
    public IEnumerable<ComposerSceneNode> FindNodesByName(string name, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        return GetAllNodes().Where(n => n.Name.Equals(name, comparison));
    }

    /// <summary>
    /// Find nodes by type.
    /// </summary>
    public IEnumerable<ComposerSceneNode> FindNodesByType(NodeType type)
    {
        return GetAllNodes().Where(n => n.NodeType == type);
    }

    /// <summary>
    /// Register a node in the scene index.
    /// Called by SceneComposer when nodes are created.
    /// </summary>
    internal void RegisterNode(ComposerSceneNode node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        if (_nodeIndex.ContainsKey(node.Id))
            throw new InvalidOperationException($"Node with ID {node.Id} already exists in this scene.");

        _nodeIndex[node.Id] = node;
    }

    /// <summary>
    /// Unregister a node from the scene index.
    /// Called by SceneComposer when nodes are deleted.
    /// </summary>
    internal void UnregisterNode(ComposerSceneNode node)
    {
        _nodeIndex.Remove(node.Id);
    }

    /// <summary>
    /// Add a node as a root-level node.
    /// </summary>
    internal void AddRootNode(ComposerSceneNode node, int? insertIndex = null)
    {
        if (insertIndex.HasValue && insertIndex.Value >= 0 && insertIndex.Value < _rootNodes.Count)
            _rootNodes.Insert(insertIndex.Value, node);
        else
            _rootNodes.Add(node);
    }

    /// <summary>
    /// Remove a node from root-level nodes.
    /// </summary>
    internal void RemoveRootNode(ComposerSceneNode node)
    {
        _rootNodes.Remove(node);
    }

    /// <summary>
    /// Get the index of a root node.
    /// </summary>
    internal int GetRootNodeIndex(ComposerSceneNode node)
    {
        return _rootNodes.IndexOf(node);
    }

    /// <summary>
    /// Clear all nodes from the scene.
    /// </summary>
    internal void Clear()
    {
        _rootNodes.Clear();
        _nodeIndex.Clear();
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"Scene({SceneId}, {Name}, {SceneType}, {NodeCount} nodes)";
}
