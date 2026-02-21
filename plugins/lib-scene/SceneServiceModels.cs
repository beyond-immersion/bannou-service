namespace BeyondImmersion.BannouService.Scene;

/// <summary>
/// Internal data models for SceneService.
/// </summary>
/// <remarks>
/// <para>
/// This file contains internal data models, DTOs, and helper structures used
/// exclusively by this service. These are NOT exposed via the API and are NOT
/// generated from schemas.
/// </para>
/// <para>
/// <b>When to add models here:</b>
/// <list type="bullet">
///   <item>Storage models for state stores (different from API request/response types)</item>
///   <item>Cache entry structures</item>
///   <item>Internal DTOs for service-to-service communication not exposed in API</item>
///   <item>Helper records for intermediate processing</item>
/// </list>
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class SceneService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Index entry for efficient scene queries.
/// </summary>
internal class SceneIndexEntry
{
    public Guid SceneId { get; set; }
    public Guid AssetId { get; set; }
    public string GameId { get; set; } = string.Empty;
    public SceneType SceneType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public int NodeCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsCheckedOut { get; set; }
    public string? CheckedOutBy { get; set; }
}

/// <summary>
/// Checkout state stored in lib-state.
/// </summary>
internal class CheckoutState
{
    public Guid SceneId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string EditorId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public int ExtensionCount { get; set; }
}

/// <summary>
/// Scene content entry stored in lib-state (YAML serialized scene).
/// </summary>
internal class SceneContentEntry
{
    public Guid SceneId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public long UpdatedAt { get; set; }
}

/// <summary>
/// Version history entry for tracking scene version changes.
/// </summary>
internal class VersionHistoryEntry
{
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
