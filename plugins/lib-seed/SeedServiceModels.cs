namespace BeyondImmersion.BannouService.Seed;

/// <summary>
/// Internal data models for SeedService.
/// </summary>
/// <remarks>
/// <para>
/// Storage models for MySQL and Redis state stores. These map to the state store
/// schema defined in schemas/state-stores.yaml. Not exposed via the API.
/// </para>
/// </remarks>
public partial class SeedService
{
    // Partial class declaration signals model ownership.
}

/// <summary>
/// Internal storage model for seed entities. Stored in seed-statestore (MySQL).
/// Key pattern: seed:{seedId}
/// </summary>
internal class SeedModel
{
    /// <summary>Unique seed identifier.</summary>
    public Guid SeedId { get; set; }

    /// <summary>The entity that owns this seed.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Owner entity type discriminator (e.g., "account", "actor", "realm").</summary>
    public string OwnerType { get; set; } = string.Empty;

    /// <summary>Registered seed type code.</summary>
    public string SeedTypeCode { get; set; } = string.Empty;

    /// <summary>Game service this seed is scoped to.</summary>
    public Guid GameServiceId { get; set; }

    /// <summary>When the seed was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Current computed growth phase code.</summary>
    public string GrowthPhase { get; set; } = string.Empty;

    /// <summary>Aggregate growth across all domains.</summary>
    public float TotalGrowth { get; set; }

    /// <summary>Bond ID if this seed is bonded, null otherwise.</summary>
    public Guid? BondId { get; set; }

    /// <summary>Human-readable name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Current lifecycle status.</summary>
    public SeedStatus Status { get; set; }

    /// <summary>Opaque seed-type-specific metadata.</summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Internal storage model for growth domain data. Stored in seed-growth-statestore (MySQL).
/// Key pattern: growth:{seedId}
/// </summary>
internal class SeedGrowthModel
{
    /// <summary>The seed this growth data belongs to.</summary>
    public Guid SeedId { get; set; }

    /// <summary>Map of domain path to depth value (e.g., "combat.melee.sword" -> 3.5).</summary>
    public Dictionary<string, float> Domains { get; set; } = new();
}

/// <summary>
/// Internal storage model for seed type definitions. Stored in seed-type-definitions-statestore (MySQL).
/// Key pattern: type:{gameServiceId}:{seedTypeCode}
/// </summary>
internal class SeedTypeDefinitionModel
{
    /// <summary>Unique type code (e.g., "guardian", "dungeon_core").</summary>
    public string SeedTypeCode { get; set; } = string.Empty;

    /// <summary>Game service this type is scoped to.</summary>
    public Guid GameServiceId { get; set; }

    /// <summary>Human-readable name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Description of what this seed type represents.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Maximum seeds of this type per owner entity.</summary>
    public int MaxPerOwner { get; set; }

    /// <summary>Entity types that can own seeds of this type.</summary>
    public List<string> AllowedOwnerTypes { get; set; } = new();

    /// <summary>Ordered growth phase definitions with thresholds.</summary>
    public List<GrowthPhaseDefinition> GrowthPhases { get; set; } = new();

    /// <summary>Max bond participants. 0 = no bonding.</summary>
    public int BondCardinality { get; set; }

    /// <summary>Whether bonds of this type are permanent.</summary>
    public bool BondPermanent { get; set; }

    /// <summary>Rules for computing capabilities from growth domains.</summary>
    public List<CapabilityRule>? CapabilityRules { get; set; }
}

/// <summary>
/// Internal storage model for seed bonds. Stored in seed-bonds-statestore (MySQL).
/// Key pattern: bond:{bondId}
/// </summary>
internal class SeedBondModel
{
    /// <summary>Unique bond identifier.</summary>
    public Guid BondId { get; set; }

    /// <summary>Seed type this bond connects.</summary>
    public string SeedTypeCode { get; set; } = string.Empty;

    /// <summary>Seeds participating in this bond.</summary>
    public List<BondParticipantEntry> Participants { get; set; } = new();

    /// <summary>When the bond was initiated.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Current bond lifecycle status.</summary>
    public BondStatus Status { get; set; }

    /// <summary>Bond strength (grows with shared growth).</summary>
    public float BondStrength { get; set; }

    /// <summary>Total accumulated shared growth.</summary>
    public float SharedGrowth { get; set; }

    /// <summary>Whether this bond is permanent (cannot be dissolved).</summary>
    public bool Permanent { get; set; }
}

/// <summary>
/// A participant in a seed bond with confirmation tracking.
/// </summary>
internal class BondParticipantEntry
{
    /// <summary>Participant seed ID.</summary>
    public Guid SeedId { get; set; }

    /// <summary>When this seed joined the bond.</summary>
    public DateTimeOffset JoinedAt { get; set; }

    /// <summary>Optional role within the bond (e.g., "initiator").</summary>
    public string? Role { get; set; }

    /// <summary>Whether this participant has confirmed the bond.</summary>
    public bool Confirmed { get; set; }
}

/// <summary>
/// Cached capability manifest. Stored in seed-capabilities-cache (Redis).
/// Key pattern: cap:{seedId}
/// </summary>
internal class CapabilityManifestModel
{
    /// <summary>The seed this manifest belongs to.</summary>
    public Guid SeedId { get; set; }

    /// <summary>Seed type code for consumer interpretation.</summary>
    public string SeedTypeCode { get; set; } = string.Empty;

    /// <summary>When this manifest was last computed.</summary>
    public DateTimeOffset ComputedAt { get; set; }

    /// <summary>Monotonically increasing version number.</summary>
    public int Version { get; set; }

    /// <summary>Computed capabilities with fidelity scores.</summary>
    public List<CapabilityEntry> Capabilities { get; set; } = new();
}

/// <summary>
/// A single computed capability entry in a manifest.
/// </summary>
internal class CapabilityEntry
{
    /// <summary>Unique capability identifier.</summary>
    public string CapabilityCode { get; set; } = string.Empty;

    /// <summary>Growth domain this capability maps to.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Capability fidelity from 0.0 to 1.0.</summary>
    public float Fidelity { get; set; }

    /// <summary>Whether this capability is available.</summary>
    public bool Unlocked { get; set; }
}
