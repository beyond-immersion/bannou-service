namespace BeyondImmersion.BannouService.CharacterLifecycle;

/// <summary>
/// Internal storage model for lifecycle profiles.
/// Stored in character-lifecycle-profiles (MySQL).
/// Key pattern: profile:{characterId}
/// </summary>
internal class LifecycleProfileModel
{
    public Guid CharacterId { get; set; }
    public Guid GameServiceId { get; set; }
    public Guid RealmId { get; set; }
    public string SpeciesCode { get; set; } = string.Empty;
    public int BirthGameYear { get; set; }
    public string? BirthSeason { get; set; }
    public int CurrentAge { get; set; }
    public string CurrentStage { get; set; } = string.Empty;
    public CreationCause CauseOfCreation { get; set; }
    public Guid? ParentAId { get; set; }
    public Guid? ParentBId { get; set; }
    public Guid? HouseholdOrgId { get; set; }
    public List<Guid> MarriageContractIds { get; set; } = new();
    public List<Guid> SpouseCharacterIds { get; set; } = new();
    public int ChildCount { get; set; }
    public int TotalChildCount { get; set; }
    public float FertilityModifier { get; set; }
    public float HealthModifier { get; set; }
    public int? NaturalDeathYear { get; set; }
    public float? FulfillmentScore { get; set; }
    public int? DeathGameYear { get; set; }
    public string? DeathCause { get; set; }
    public string? AfterlifePath { get; set; }
    public LifecycleStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal storage model for genetic profiles.
/// Stored in character-lifecycle-heritage (MySQL).
/// Key pattern: genetic:{characterId}
/// </summary>
internal class GeneticProfileModel
{
    public Guid CharacterId { get; set; }
    public string SpeciesCode { get; set; } = string.Empty;
    public string? SecondarySpecies { get; set; }
    public Guid? ParentAId { get; set; }
    public Guid? ParentBId { get; set; }
    public int GenerationDepth { get; set; }
    public List<GenotypeEntry> Genotype { get; set; } = new();
    public List<PhenotypeEntry> Phenotype { get; set; } = new();
    public List<AptitudeEntry> Aptitudes { get; set; } = new();
    public List<BloodlineEntry> Bloodlines { get; set; } = new();
    public List<MutationEntry> Mutations { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Internal storage model for lifecycle templates.
/// Stored in character-lifecycle-heritage (MySQL).
/// Key pattern: lifecycle-template:{speciesCode}:{gameServiceId}
/// </summary>
internal class LifecycleTemplateModel
{
    public string SpeciesCode { get; set; } = string.Empty;
    public Guid GameServiceId { get; set; }
    public List<LifecycleStageDefinition> Stages { get; set; } = new();
    public NaturalDeathRange NaturalDeathRange { get; set; } = new();
    public FertilityWindow FertilityWindow { get; set; } = new();
    public bool IsDeprecated { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }
    public string? DeprecationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal storage model for heritable trait templates.
/// Stored in character-lifecycle-heritage (MySQL).
/// Key pattern: trait-template:{speciesCode}:{gameServiceId}
/// </summary>
internal class HeritableTraitTemplateModel
{
    public string SpeciesCode { get; set; } = string.Empty;
    public Guid GameServiceId { get; set; }
    public List<HeritableTraitDefinition> Traits { get; set; } = new();
    public bool IsDeprecated { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }
    public string? DeprecationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal storage model for hybrid trait templates.
/// Stored in character-lifecycle-heritage (MySQL).
/// Key pattern: hybrid-template:{speciesA}:{speciesB}:{gameServiceId}
/// </summary>
internal class HybridTraitTemplateModel
{
    public string SpeciesA { get; set; } = string.Empty;
    public string SpeciesB { get; set; } = string.Empty;
    public Guid GameServiceId { get; set; }
    public List<HybridTraitOverride> TraitOverrides { get; set; } = new();
    public float HybridFertilityModifier { get; set; }
    public bool IsDeprecated { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }
    public string? DeprecationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal storage model for bloodline definitions.
/// Stored in character-lifecycle-bloodlines (MySQL).
/// Key pattern: bloodline:{bloodlineId}
/// </summary>
internal class BloodlineModel
{
    public Guid BloodlineId { get; set; }
    public string BloodlineCode { get; set; } = string.Empty;
    public Guid GameServiceId { get; set; }
    public Guid OriginCharacterId { get; set; }
    public int OriginGameYear { get; set; }
    public List<string> TraitSignature { get; set; } = new();
    public int MemberCount { get; set; }
    public int GenerationSpan { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal storage model for bloodline membership.
/// Stored in character-lifecycle-bloodlines (MySQL).
/// Key pattern: bloodline:member:{characterId}
/// </summary>
internal class BloodlineMembershipModel
{
    public Guid CharacterId { get; set; }
    public List<BloodlineEntry> Bloodlines { get; set; } = new();
}

/// <summary>
/// Internal storage model for bloodline member lists.
/// Stored in character-lifecycle-bloodlines (MySQL).
/// Key pattern: bloodline:members:{bloodlineId}
/// </summary>
internal class BloodlineMemberListModel
{
    public Guid BloodlineId { get; set; }
    public List<Guid> MemberIds { get; set; } = new();
}

/// <summary>
/// Cached lifecycle manifest combining profile, phenotype, aptitudes, and bloodlines.
/// Stored in character-lifecycle-cache (Redis) with TTL.
/// Key pattern: manifest:{characterId}
/// </summary>
internal class LifecycleManifestModel
{
    public Guid CharacterId { get; set; }
    public int CurrentAge { get; set; }
    public string CurrentStage { get; set; } = string.Empty;
    public float HealthModifier { get; set; }
    public float FertilityModifier { get; set; }
    public List<PhenotypeEntry> Phenotype { get; set; } = new();
    public List<AptitudeEntry> Aptitudes { get; set; } = new();
    public List<BloodlineEntry> Bloodlines { get; set; } = new();
}

/// <summary>
/// Pending pregnancy record.
/// Stored in character-lifecycle-profiles (MySQL).
/// Key pattern: pregnancy-pending:{pregnancyId}
/// </summary>
internal class PendingPregnancyModel
{
    public Guid PregnancyId { get; set; }
    public Guid ParentAId { get; set; }
    public Guid ParentBId { get; set; }
    public Guid GameServiceId { get; set; }
    public Guid RealmId { get; set; }
    public string SpeciesCode { get; set; } = string.Empty;
    public int ExpectedBirthGameDay { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
