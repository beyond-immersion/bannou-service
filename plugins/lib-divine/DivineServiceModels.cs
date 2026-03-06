namespace BeyondImmersion.BannouService.Divine;

/// <summary>
/// Internal data models for DivineService.
/// </summary>
/// <remarks>
/// <para>
/// This file contains internal data models, DTOs, and helper structures used
/// exclusively by this service. These are NOT exposed via the API and are NOT
/// generated from schemas.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class DivineService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Internal storage model for a deity entity.
/// Stored in divine-deities (MySQL).
/// </summary>
internal class DeityModel
{
    public Guid DeityId { get; set; }
    public Guid GameServiceId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<DomainInfluenceData> Domains { get; set; } = new();
    public DeityStatus Status { get; set; }
    public DeityPersonalityTraits? PersonalityTraits { get; set; }
    public int FollowerCount { get; set; }
    public int MaxAttentionSlots { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? SeedId { get; set; }
    public Guid? CurrencyWalletId { get; set; }
    public Guid? CharacterId { get; set; }
    public Guid? RealmId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Internal storage model for a domain influence entry.
/// </summary>
internal class DomainInfluenceData
{
    public string Domain { get; set; } = string.Empty;
    public double Influence { get; set; }
}

/// <summary>
/// Internal storage model for a blessing record.
/// Stored in divine-blessings (MySQL).
/// </summary>
internal class BlessingModel
{
    public Guid BlessingId { get; set; }
    public Guid DeityId { get; set; }
    public Guid EntityId { get; set; }
    public EntityType EntityType { get; set; }
    public BlessingTier Tier { get; set; }
    public BlessingStatus Status { get; set; }
    public Guid ItemInstanceId { get; set; }
    public string? ItemTemplateCode { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// Internal storage model for an attention slot tracking a deity's focus on a character.
/// Stored in divine-attention (Redis).
/// </summary>
internal class AttentionSlotModel
{
    public Guid DeityId { get; set; }
    public Guid CharacterId { get; set; }
    public double Impression { get; set; }
    public DateTimeOffset LastInteractionAt { get; set; }
}

/// <summary>
/// Internal storage model for a pending divinity generation event awaiting batch processing.
/// Stored in divine-divinity-events (Redis).
/// </summary>
internal class DivinityEventModel
{
    public Guid EventId { get; set; }
    public Guid DeityId { get; set; }
    public double Amount { get; set; }
    public string Source { get; set; } = string.Empty;
    public Guid? SourceEventId { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
}
