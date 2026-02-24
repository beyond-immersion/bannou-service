namespace BeyondImmersion.BannouService.Permission;

/// <summary>
/// Internal data models for PermissionService.
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
public partial class PermissionService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================

/// <summary>
/// Internal storage model for service registration information.
/// Stored per-service in Redis for tracking registration metadata.
/// </summary>
internal class ServiceRegistrationInfo
{
    /// <summary>
    /// Unique service identifier.
    /// </summary>
    public string ServiceId { get; set; } = string.Empty;

    /// <summary>
    /// Service API version at registration time.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Unix timestamp (seconds) of when the service registered its permissions.
    /// </summary>
    public long RegisteredAtUnix { get; set; }
}
