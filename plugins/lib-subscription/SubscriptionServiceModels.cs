namespace BeyondImmersion.BannouService.Subscription;

/// <summary>
/// Internal data models for SubscriptionService.
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
public partial class SubscriptionService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Internal storage model using Unix timestamps to avoid serialization issues.
/// Accessible to test project via InternalsVisibleTo attribute.
/// </summary>
internal class SubscriptionDataModel
{
    public Guid SubscriptionId { get; set; }
    public Guid AccountId { get; set; }
    public Guid ServiceId { get; set; }
    public string StubName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long StartDateUnix { get; set; }
    public long? ExpirationDateUnix { get; set; }
    public bool IsActive { get; set; }
    public long? CancelledAtUnix { get; set; }
    public string? CancellationReason { get; set; }
    public long CreatedAtUnix { get; set; }
    public long? UpdatedAtUnix { get; set; }
}
