namespace BeyondImmersion.Bannou.SceneComposer.Abstractions;

/// <summary>
/// Client interface for communicating with the lib-scene service.
/// Implementations may use WebSocket (via BeyondImmersion.Bannou.Client) or HTTP.
/// </summary>
public interface ISceneServiceClient
{
    /// <summary>
    /// Create a new scene on the server.
    /// </summary>
    /// <param name="sceneType">Type of scene.</param>
    /// <param name="name">Scene name.</param>
    /// <param name="gameId">Game identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created scene data.</returns>
    Task<SceneServiceResponse> CreateSceneAsync(
        string sceneType,
        string name,
        string gameId,
        CancellationToken ct = default);

    /// <summary>
    /// Get a scene from the server.
    /// </summary>
    /// <param name="sceneId">Scene identifier.</param>
    /// <param name="resolveReferences">Whether to resolve reference nodes.</param>
    /// <param name="maxDepth">Maximum reference resolution depth.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Scene data.</returns>
    Task<SceneServiceResponse?> GetSceneAsync(
        string sceneId,
        bool resolveReferences = true,
        int maxDepth = 10,
        CancellationToken ct = default);

    /// <summary>
    /// Checkout a scene for exclusive editing.
    /// </summary>
    /// <param name="sceneId">Scene identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Checkout result with session info.</returns>
    Task<CheckoutResult> CheckoutAsync(string sceneId, CancellationToken ct = default);

    /// <summary>
    /// Extend the checkout lock.
    /// </summary>
    /// <param name="sceneId">Scene identifier.</param>
    /// <param name="sessionId">Checkout session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if extension succeeded.</returns>
    Task<bool> HeartbeatAsync(string sceneId, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Commit changes to the scene.
    /// </summary>
    /// <param name="sceneId">Scene identifier.</param>
    /// <param name="sessionId">Checkout session identifier.</param>
    /// <param name="sceneData">Updated scene data.</param>
    /// <param name="comment">Optional commit comment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Commit result with new version.</returns>
    Task<CommitResult> CommitAsync(
        string sceneId,
        string sessionId,
        SceneData sceneData,
        string? comment = null,
        CancellationToken ct = default);

    /// <summary>
    /// Discard checkout and release lock.
    /// </summary>
    /// <param name="sceneId">Scene identifier.</param>
    /// <param name="sessionId">Checkout session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DiscardAsync(string sceneId, string sessionId, CancellationToken ct = default);
}

/// <summary>
/// Response from scene service operations.
/// </summary>
public class SceneServiceResponse
{
    /// <summary>
    /// Scene identifier.
    /// </summary>
    public string SceneId { get; init; } = string.Empty;

    /// <summary>
    /// Scene name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Scene type.
    /// </summary>
    public string SceneType { get; init; } = string.Empty;

    /// <summary>
    /// Scene version.
    /// </summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>
    /// Scene data (nodes, metadata).
    /// </summary>
    public SceneData? Data { get; init; }
}

/// <summary>
/// Scene data for serialization.
/// </summary>
public class SceneData
{
    /// <summary>
    /// Root nodes of the scene.
    /// </summary>
    public List<SceneNodeData> RootNodes { get; init; } = new();

    /// <summary>
    /// Scene metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// Scene tags.
    /// </summary>
    public List<string> Tags { get; init; } = new();
}

/// <summary>
/// Node data for serialization.
/// </summary>
public class SceneNodeData
{
    /// <summary>
    /// Node identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Node name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Node type.
    /// </summary>
    public string NodeType { get; init; } = "group";

    /// <summary>
    /// Local transform.
    /// </summary>
    public TransformData Transform { get; init; } = new();

    /// <summary>
    /// Asset reference (if applicable).
    /// </summary>
    public AssetReferenceData? Asset { get; init; }

    /// <summary>
    /// Child nodes.
    /// </summary>
    public List<SceneNodeData> Children { get; init; } = new();

    /// <summary>
    /// Node annotations.
    /// </summary>
    public Dictionary<string, object> Annotations { get; init; } = new();

    /// <summary>
    /// Whether the node is visible.
    /// </summary>
    public bool IsVisible { get; init; } = true;

    /// <summary>
    /// Whether the node is locked from editing.
    /// </summary>
    public bool IsLocked { get; init; }
}

/// <summary>
/// Transform data for serialization.
/// </summary>
public class TransformData
{
    public double PositionX { get; init; }
    public double PositionY { get; init; }
    public double PositionZ { get; init; }
    public double RotationX { get; init; }
    public double RotationY { get; init; }
    public double RotationZ { get; init; }
    public double RotationW { get; init; } = 1.0;
    public double ScaleX { get; init; } = 1.0;
    public double ScaleY { get; init; } = 1.0;
    public double ScaleZ { get; init; } = 1.0;
}

/// <summary>
/// Asset reference data for serialization.
/// </summary>
public class AssetReferenceData
{
    public string BundleId { get; init; } = string.Empty;
    public string AssetId { get; init; } = string.Empty;
    public string? VariantId { get; init; }
}

/// <summary>
/// Result of a checkout operation.
/// </summary>
public class CheckoutResult
{
    /// <summary>
    /// Whether checkout succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Checkout session identifier.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// When the checkout expires.
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// Error message if checkout failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// User who has the scene locked (if locked by another).
    /// </summary>
    public string? LockedBy { get; init; }
}

/// <summary>
/// Result of a commit operation.
/// </summary>
public class CommitResult
{
    /// <summary>
    /// Whether commit succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// New version after commit.
    /// </summary>
    public string? NewVersion { get; init; }

    /// <summary>
    /// Error message if commit failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
