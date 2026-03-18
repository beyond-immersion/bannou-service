namespace BeyondImmersion.BannouService.State;

/// <summary>
/// Internal data models for StateService.
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
/// <b>Note:</b> The State service is infrastructure - most of its internal types
/// are in the Services/ folder (state store implementations, lock providers) and
/// Data/ folder (EF Core entities). This file is reserved for any StateService-specific
/// internal models that don't fit those categories.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class StateService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================
// The State service's internal types are primarily in:
// - Services/ folder: State store implementations (Redis, MySQL, InMemory)
// - Data/ folder: EF Core entities (StateEntry, StateDbContext)
//
// Add any StateService-specific internal models below if needed.
// ============================================================================
