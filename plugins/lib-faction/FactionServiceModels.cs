namespace BeyondImmersion.BannouService.Faction;

/// <summary>
/// Internal data models for FactionService.
/// </summary>
/// <remarks>
/// Contains storage models for MySQL state stores, Redis cache entries,
/// and internal DTOs. All models use proper C# types per IMPLEMENTATION TENETS.
/// </remarks>
public partial class FactionService
{
    // Partial class anchor for models below.
}

// ============================================================================
// FACTION STORAGE MODELS
// ============================================================================

/// <summary>
/// Primary faction storage model persisted in faction-statestore (MySQL).
/// </summary>
internal class FactionModel
{
    /// <summary>Unique faction identifier.</summary>
    public Guid FactionId { get; set; }

    /// <summary>Game service this faction belongs to.</summary>
    public Guid GameServiceId { get; set; }

    /// <summary>Display name of the faction.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Unique code within game service scope.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Optional description of the faction.</summary>
    public string? Description { get; set; }

    /// <summary>Realm this faction belongs to.</summary>
    public Guid RealmId { get; set; }

    /// <summary>Whether this faction is the realm baseline cultural faction.</summary>
    public bool IsRealmBaseline { get; set; }

    /// <summary>Parent faction in hierarchy (null if top-level).</summary>
    public Guid? ParentFactionId { get; set; }

    /// <summary>Associated seed for growth tracking (null before seed creation).</summary>
    public Guid? SeedId { get; set; }

    /// <summary>Current lifecycle status.</summary>
    public FactionStatus Status { get; set; }

    /// <summary>Current seed growth phase (null if no seed).</summary>
    public string? CurrentPhase { get; set; }

    /// <summary>Current member count (denormalized).</summary>
    public int MemberCount { get; set; }

    /// <summary>When the faction was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the faction was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

// ============================================================================
// MEMBERSHIP STORAGE MODELS
// ============================================================================

/// <summary>
/// Individual membership record persisted in faction-membership-statestore (MySQL).
/// Key: mem:{factionId}:{characterId}
/// </summary>
internal class FactionMemberModel
{
    /// <summary>Faction the character belongs to.</summary>
    public Guid FactionId { get; set; }

    /// <summary>Character that is a member.</summary>
    public Guid CharacterId { get; set; }

    /// <summary>Member's role in the faction.</summary>
    public FactionMemberRole Role { get; set; }

    /// <summary>When the character joined.</summary>
    public DateTimeOffset JoinedAt { get; set; }

    /// <summary>When the membership was last updated (role changes).</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Faction name cached for character membership lookups.</summary>
    public string FactionName { get; set; } = string.Empty;

    /// <summary>Faction code cached for character membership lookups.</summary>
    public string FactionCode { get; set; } = string.Empty;
}

/// <summary>
/// Aggregated membership list for a character, persisted in faction-membership-statestore (MySQL).
/// Key: mem:char:{characterId}
/// </summary>
internal class MembershipListModel
{
    /// <summary>Character these memberships belong to.</summary>
    public Guid CharacterId { get; set; }

    /// <summary>All faction memberships for this character.</summary>
    public List<MembershipEntry> Memberships { get; set; } = new();
}

/// <summary>
/// Compact membership entry within a character's membership list.
/// </summary>
internal class MembershipEntry
{
    /// <summary>Faction ID.</summary>
    public Guid FactionId { get; set; }

    /// <summary>Member's role.</summary>
    public FactionMemberRole Role { get; set; }

    /// <summary>When the character joined.</summary>
    public DateTimeOffset JoinedAt { get; set; }
}

// ============================================================================
// TERRITORY STORAGE MODELS
// ============================================================================

/// <summary>
/// Territory claim record persisted in faction-territory-statestore (MySQL).
/// Key: tcl:{claimId}
/// </summary>
internal class TerritoryClaimModel
{
    /// <summary>Unique claim identifier.</summary>
    public Guid ClaimId { get; set; }

    /// <summary>Faction that holds this claim.</summary>
    public Guid FactionId { get; set; }

    /// <summary>Location that is claimed.</summary>
    public Guid LocationId { get; set; }

    /// <summary>Current claim status.</summary>
    public TerritoryClaimStatus Status { get; set; }

    /// <summary>When the territory was claimed.</summary>
    public DateTimeOffset ClaimedAt { get; set; }

    /// <summary>When the territory was released (null if still active).</summary>
    public DateTimeOffset? ReleasedAt { get; set; }
}

/// <summary>
/// Aggregated territory claim list for a faction, persisted in faction-territory-statestore (MySQL).
/// Key: tcl:fac:{factionId}
/// </summary>
internal class TerritoryClaimListModel
{
    /// <summary>Faction these claims belong to.</summary>
    public Guid FactionId { get; set; }

    /// <summary>All territory claims for this faction.</summary>
    public List<Guid> ClaimIds { get; set; } = new();
}

// ============================================================================
// NORM STORAGE MODELS
// ============================================================================

/// <summary>
/// Norm definition record persisted in faction-norm-statestore (MySQL).
/// Key: nrm:{normId}
/// </summary>
internal class NormDefinitionModel
{
    /// <summary>Unique norm identifier.</summary>
    public Guid NormId { get; set; }

    /// <summary>Faction that defined this norm.</summary>
    public Guid FactionId { get; set; }

    /// <summary>Opaque violation type code (e.g., "theft", "deception").</summary>
    public string ViolationType { get; set; } = string.Empty;

    /// <summary>Base GOAP cost penalty for this violation.</summary>
    public float BasePenalty { get; set; }

    /// <summary>Enforcement intensity level.</summary>
    public NormSeverity Severity { get; set; }

    /// <summary>Applicability scope (internal vs external).</summary>
    public NormScope Scope { get; set; }

    /// <summary>Optional human-readable description.</summary>
    public string? Description { get; set; }

    /// <summary>When the norm was defined.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the norm was last updated (null if never).</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Aggregated norm list for a faction, persisted in faction-norm-statestore (MySQL).
/// Key: nrm:fac:{factionId}
/// </summary>
internal class NormListModel
{
    /// <summary>Faction these norms belong to.</summary>
    public Guid FactionId { get; set; }

    /// <summary>All norm IDs for this faction.</summary>
    public List<Guid> NormIds { get; set; } = new();
}

// ============================================================================
// CACHE MODELS (Redis)
// ============================================================================

/// <summary>
/// Cached resolved norm set per character+location in faction-cache (Redis).
/// Key: ncache:{characterId}:{locationId}
/// </summary>
internal class ResolvedNormCacheModel
{
    /// <summary>Character these norms were resolved for.</summary>
    public Guid CharacterId { get; set; }

    /// <summary>Location context (null if no location).</summary>
    public Guid? LocationId { get; set; }

    /// <summary>All applicable norms from the hierarchy.</summary>
    public List<CachedApplicableNorm> ApplicableNorms { get; set; } = new();

    /// <summary>Merged norm map (most specific wins per violation type).</summary>
    public Dictionary<string, CachedMergedNorm> MergedNormMap { get; set; } = new();

    /// <summary>Number of faction memberships that contributed norms.</summary>
    public int MembershipFactionCount { get; set; }

    /// <summary>Whether a territory controlling faction was resolved.</summary>
    public bool TerritoryFactionResolved { get; set; }

    /// <summary>Whether a realm baseline faction was resolved.</summary>
    public bool RealmBaselineResolved { get; set; }

    /// <summary>When this cache entry was created.</summary>
    public DateTimeOffset CachedAt { get; set; }
}

/// <summary>
/// Cached applicable norm entry within a resolved norm set.
/// </summary>
internal class CachedApplicableNorm
{
    /// <summary>Norm definition ID.</summary>
    public Guid NormId { get; set; }

    /// <summary>Owning faction ID.</summary>
    public Guid FactionId { get; set; }

    /// <summary>Owning faction name.</summary>
    public string FactionName { get; set; } = string.Empty;

    /// <summary>Violation type code.</summary>
    public string ViolationType { get; set; } = string.Empty;

    /// <summary>Base GOAP cost penalty.</summary>
    public float BasePenalty { get; set; }

    /// <summary>Enforcement severity.</summary>
    public NormSeverity Severity { get; set; }

    /// <summary>Applicability scope.</summary>
    public NormScope Scope { get; set; }

    /// <summary>Source of this norm in the resolution hierarchy.</summary>
    public NormSource Source { get; set; }

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Cached merged norm entry (winner per violation type).
/// </summary>
internal class CachedMergedNorm
{
    /// <summary>Violation type code.</summary>
    public string ViolationType { get; set; } = string.Empty;

    /// <summary>Winning base penalty.</summary>
    public float BasePenalty { get; set; }

    /// <summary>Source that won (most specific).</summary>
    public NormSource Source { get; set; }

    /// <summary>Faction that defined the winning norm.</summary>
    public Guid FactionId { get; set; }

    /// <summary>Severity of the winning norm.</summary>
    public NormSeverity Severity { get; set; }
}
