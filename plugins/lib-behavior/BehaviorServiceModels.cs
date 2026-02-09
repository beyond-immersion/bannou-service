using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Internal data models for BehaviorService.
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
public partial class BehaviorService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as public classes.
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================

/// <summary>
/// Metadata for a compiled behavior stored in the system.
/// </summary>
public class BehaviorMetadata
{
    /// <summary>
    /// Unique identifier for the behavior (content-addressable hash).
    /// </summary>
    [JsonPropertyName("behaviorId")]
    public string BehaviorId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name of the behavior.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Category of the behavior (e.g., professional, cultural).
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>
    /// Asset service ID where bytecode is stored.
    /// </summary>
    [JsonPropertyName("assetId")]
    public string AssetId { get; set; } = string.Empty;

    /// <summary>
    /// Bundle ID this behavior belongs to, if any.
    /// </summary>
    [JsonPropertyName("bundleId")]
    public string? BundleId { get; set; }

    /// <summary>
    /// Size of the compiled bytecode in bytes.
    /// </summary>
    [JsonPropertyName("bytecodeSize")]
    public int BytecodeSize { get; set; }

    /// <summary>
    /// ABML schema version used for compilation.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    /// <summary>
    /// When the behavior was first compiled.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the behavior was last updated.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Tracks which behaviors belong to a bundle.
/// </summary>
public class BundleMembership
{
    /// <summary>
    /// The bundle identifier.
    /// </summary>
    [JsonPropertyName("bundleId")]
    public string BundleId { get; set; } = string.Empty;

    /// <summary>
    /// Map of behavior ID to asset ID for all behaviors in this bundle.
    /// </summary>
    [JsonPropertyName("behaviorAssetIds")]
    public Dictionary<string, string> BehaviorAssetIds { get; set; } = new();

    /// <summary>
    /// The asset bundle ID if one has been created.
    /// </summary>
    [JsonPropertyName("assetBundleId")]
    public string? AssetBundleId { get; set; }

    /// <summary>
    /// When the bundle was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the bundle was last updated.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Cached GOAP metadata for a compiled behavior.
/// Stores goals and actions as serializable string dictionaries.
/// </summary>
public class CachedGoapMetadata
{
    /// <summary>
    /// The behavior ID this metadata belongs to.
    /// </summary>
    [JsonPropertyName("behaviorId")]
    public string BehaviorId { get; set; } = string.Empty;

    /// <summary>
    /// GOAP goals defined in the behavior's goals: section.
    /// </summary>
    [JsonPropertyName("goals")]
    public List<CachedGoapGoal> Goals { get; set; } = new();

    /// <summary>
    /// GOAP actions extracted from flows with goap: blocks.
    /// </summary>
    [JsonPropertyName("actions")]
    public List<CachedGoapAction> Actions { get; set; } = new();

    /// <summary>
    /// When this metadata was cached.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Cached GOAP goal from a behavior's goals: section.
/// </summary>
public class CachedGoapGoal
{
    /// <summary>
    /// Goal name (key in the goals: section).
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Goal priority (higher = more important).
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    /// <summary>
    /// Goal conditions as string key-value pairs (e.g., "hunger" -> "<= 0.3").
    /// </summary>
    [JsonPropertyName("conditions")]
    public Dictionary<string, string> Conditions { get; set; } = new();
}

/// <summary>
/// Cached GOAP action from a flow's goap: block.
/// </summary>
public class CachedGoapAction
{
    /// <summary>
    /// Flow name (becomes the action ID).
    /// </summary>
    [JsonPropertyName("flowName")]
    public string FlowName { get; set; } = string.Empty;

    /// <summary>
    /// Action preconditions as string key-value pairs (e.g., "gold" -> ">= 5").
    /// </summary>
    [JsonPropertyName("preconditions")]
    public Dictionary<string, string> Preconditions { get; set; } = new();

    /// <summary>
    /// Action effects as string key-value pairs (e.g., "hunger" -> "-0.8").
    /// </summary>
    [JsonPropertyName("effects")]
    public Dictionary<string, string> Effects { get; set; } = new();

    /// <summary>
    /// Cost of performing this action.
    /// </summary>
    [JsonPropertyName("cost")]
    public float Cost { get; set; } = 1.0f;
}
