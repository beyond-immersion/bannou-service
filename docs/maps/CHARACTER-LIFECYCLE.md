# Character Lifecycle Implementation Map

> **Plugin**: lib-character-lifecycle
> **Schema**: schemas/character-lifecycle-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/CHARACTER-LIFECYCLE.md](../plugins/CHARACTER-LIFECYCLE.md)
> **Status**: Aspirational -- pseudo-code represents intended behavior, not verified implementation

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-character-lifecycle |
| Layer | L4 GameFeatures |
| Endpoints | 29 |
| State Stores | character-lifecycle-profiles (MySQL), character-lifecycle-heritage (MySQL), character-lifecycle-bloodlines (MySQL), character-lifecycle-cache (Redis), character-lifecycle-lock (Redis) |
| Events Published | 21 (`character-lifecycle.birth`, `character-lifecycle.stage-changed`, `character-lifecycle.marriage`, `character-lifecycle.divorce`, `character-lifecycle.dying`, `character-lifecycle.death`, `character-lifecycle.trait.expressed`, `character-lifecycle.bloodline.formed`, `character-lifecycle.inheritance.processed`, + 12 x-lifecycle CRUD for templates and bloodlines) |
| Events Consumed | 5 |
| Client Events | 0 |
| Background Services | 3 |

---

## State

**Store**: `character-lifecycle-profiles` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `profile:{characterId}` | `LifecycleProfileModel` | Primary lifecycle state for a character |
| `profile:realm:{realmId}` | `LifecycleProfileModel` | Realm-scoped query for batch aging |
| `profile:realm:{realmId}:stage:{stageCode}` | `LifecycleProfileModel` | Stage-filtered query within realm |
| `profile:household:{orgId}` | `LifecycleProfileModel` | All characters in a household |
| `profile:parent:{characterId}` | `LifecycleProfileModel` | Children of a character (reverse lookup) |

**Store**: `character-lifecycle-heritage` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `genetic:{characterId}` | `GeneticProfileModel` | Full genetic profile (immutable after creation) |
| `genetic:parents:{parentAId}:{parentBId}` | `GeneticProfileModel` | Children by parent pair lookup |
| `trait-template:{speciesCode}:{gameServiceId}` | `HeritableTraitTemplateModel` | Species heritable trait definitions |
| `lifecycle-template:{speciesCode}:{gameServiceId}` | `LifecycleTemplateModel` | Species lifecycle stage definitions |
| `hybrid-template:{speciesA}:{speciesB}:{gameServiceId}` | `HybridTraitTemplateModel` | Cross-species hybridization rules |

**Store**: `character-lifecycle-bloodlines` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `bloodline:{bloodlineId}` | `BloodlineModel` | Bloodline definition |
| `bloodline:code:{gameServiceId}:{bloodlineCode}` | `BloodlineModel` | Code-based lookup |
| `bloodline:member:{characterId}` | `BloodlineMembershipModel` | All bloodlines a character belongs to |
| `bloodline:members:{bloodlineId}` | `BloodlineMemberListModel` | All living members of a bloodline |

**Store**: `character-lifecycle-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `manifest:{characterId}` | `LifecycleManifestModel` | Cached composite manifest (stage, age, phenotype, aptitudes, bloodlines). TTL-based with event-driven invalidation. |
| `realm-pop:{realmId}` | `RealmPopulationModel` | Cached population statistics per realm |

**Store**: `character-lifecycle-lock` (Backend: Redis)

| Key Pattern | Purpose |
|-------------|---------|
| `aging:{realmId}` | Aging batch lock (prevents concurrent aging across nodes) |
| `procreation:{parentAId}:{parentBId}` | Procreation lock |
| `death:{characterId}` | Death processing lock |
| `marriage:{characterAId}:{characterBId}` | Marriage lock |
| `pregnancy-worker` | Pregnancy worker lock |
| `bloodline-worker` | Bloodline formation worker lock |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | 5 stores: profiles, heritage, bloodlines, cache, lock |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Aging, procreation, death, marriage, worker locks |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing lifecycle events |
| lib-messaging (`IEventConsumer`) | L0 | Hard | Subscribing to worldstate, contract, seed events |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation |
| lib-contract (`IContractClient`) | L1 | Hard | Marriage contracts, testament contracts |
| lib-resource (`IResourceClient`) | L1 | Hard | Archive compression, reference tracking, cleanup callbacks |
| lib-character (`ICharacterClient`) | L2 | Hard | Creating child characters during procreation |
| lib-relationship (`IRelationshipClient`) | L2 | Hard | PARENT/CHILD/SIBLING/SPOUSE bond creation |
| lib-species (`ISpeciesClient`) | L2 | Hard | Species data for template resolution |
| lib-worldstate (`IWorldstateClient`) | L2 | Hard | Current game time queries |
| lib-seed (`ISeedClient`) | L2 | Hard | Guardian spirit growth on death |
| lib-game-service (`IGameServiceClient`) | L2 | Hard | Game service scope validation |
| lib-inventory (`IInventoryClient`) | L2 | Hard | Heirloom transfer during inheritance |
| lib-currency (`ICurrencyClient`) | L2 | Hard | Wallet transfer during death processing |
| lib-organization (`IOrganizationClient`) | L4 | Soft | Household management (marriage, birth, succession). Degrades: marriage without household integration. |
| lib-disposition (`IDispositionClient`) | L4 | Soft | Fulfillment calculation, drive seeding. Degrades: fulfillment defaults to 0.3. |
| lib-character-personality (`ICharacterPersonalityClient`) | L4 | Soft | Seeding personality from heritage. Degrades: species defaults. |
| lib-character-history (`ICharacterHistoryClient`) | L4 | Soft | Seeding backstory for newborns. Degrades: blank history. |
| lib-character-encounter (`ICharacterEncounterClient`) | L4 | Soft | Birth encounter records. Degrades: no birth encounters. |
| lib-hearsay (`IHearsayClient`) | L4 | Soft | Family belief seeding. Degrades: no inherited beliefs. |
| lib-analytics (`IAnalyticsClient`) | L4 | Soft | Population statistics. Degrades: no analytics. |

**T32 note**: Lifecycle never receives `accountId` directly. Guardian spirit seed resolution follows: characterId → household Org → account owner → guardian seed.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `character-lifecycle.birth` | `CharacterLifecycleBirthEvent` | LifecyclePregnancyWorkerService (birth flow) |
| `character-lifecycle.stage-changed` | `CharacterLifecycleStageChangedEvent` | LifecycleAgingWorkerService (stage transition) |
| `character-lifecycle.marriage` | `CharacterLifecycleMarriageEvent` | InitiateMarriage |
| `character-lifecycle.divorce` | `CharacterLifecycleDivorceEvent` | HandleContractTerminatedAsync |
| `character-lifecycle.dying` | `CharacterLifecycleDyingEvent` | LifecycleAgingWorkerService (natural death detection) |
| `character-lifecycle.death` | `CharacterLifecycleDeathEvent` | RecordDeath |
| `character-lifecycle.trait.expressed` | `CharacterLifecycleTraitExpressedEvent` | LifecycleAgingWorkerService (latent trait activation) |
| `character-lifecycle.bloodline.formed` | `CharacterLifecycleBloodlineFormedEvent` | LifecycleBloodlineWorkerService, EstablishBloodline |
| `character-lifecycle.inheritance.processed` | `CharacterLifecycleInheritanceProcessedEvent` | RecordDeath (step 5) |
| `character-lifecycle.lifecycle-template.created` | `LifecycleTemplateCreatedEvent` | SeedLifecycleTemplate |
| `character-lifecycle.lifecycle-template.updated` | `LifecycleTemplateUpdatedEvent` | (template update methods) |
| `character-lifecycle.lifecycle-template.deleted` | `LifecycleTemplateDeletedEvent` | (template deletion) |
| `character-lifecycle.heritable-trait-template.created` | `HeritableTraitTemplateCreatedEvent` | SeedHeritableTraitTemplate |
| `character-lifecycle.heritable-trait-template.updated` | `HeritableTraitTemplateUpdatedEvent` | (template update methods) |
| `character-lifecycle.heritable-trait-template.deleted` | `HeritableTraitTemplateDeletedEvent` | (template deletion) |
| `character-lifecycle.hybrid-trait-template.created` | `HybridTraitTemplateCreatedEvent` | SeedHybridTemplate |
| `character-lifecycle.hybrid-trait-template.updated` | `HybridTraitTemplateUpdatedEvent` | (template update methods) |
| `character-lifecycle.hybrid-trait-template.deleted` | `HybridTraitTemplateDeletedEvent` | (template deletion) |
| `character-lifecycle.bloodline.created` | `BloodlineCreatedEvent` | EstablishBloodline |
| `character-lifecycle.bloodline.updated` | `BloodlineUpdatedEvent` | (bloodline update methods) |
| `character-lifecycle.bloodline.deleted` | `BloodlineDeletedEvent` | DeleteBloodline |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `worldstate.year-changed` | `HandleYearChangedAsync` | Batch-advance character ages. Detect stage transitions and natural deaths. Lock `aging:{realmId}` prevents concurrent processing. |
| `worldstate.season-changed` | `HandleSeasonChangedAsync` | Update seasonal fertility modifiers. |
| `contract.terminated` | `HandleContractTerminatedAsync` | Process marriage contract termination as divorce. Update profiles, publish `character-lifecycle.divorce`. |
| `contract.breach.detected` | `HandleContractBreachedAsync` | Evaluate marriage contract breach severity. May trigger divorce or reconciliation. |
| `seed.phase.changed` | `HandleSeedPhaseChangedAsync` | If household seed gains `dynasty.establish` capability: evaluate bloodline formation eligibility. |

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<CharacterLifecycleService>` | Structured logging |
| `CharacterLifecycleServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 5 stores) |
| `IDistributedLockProvider` | Distributed lock acquisition |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event subscription registration |
| `ITelemetryProvider` | Telemetry span creation |
| `ICharacterClient` | Character creation and queries (L2) |
| `IRelationshipClient` | Family bond creation (L2) |
| `ISpeciesClient` | Species data for template resolution (L2) |
| `IWorldstateClient` | Game time queries (L2) |
| `IContractClient` | Marriage and testament contracts (L1) |
| `IResourceClient` | Archive compression and reference tracking (L1) |
| `ISeedClient` | Guardian spirit growth (L2) |
| `IGameServiceClient` | Game service validation (L2) |
| `IInventoryClient` | Heirloom transfer (L2) |
| `ICurrencyClient` | Wallet transfer (L2) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies |

#### DI Interfaces Implemented by This Plugin

| Interface | Registered As | Direction | Consumer |
|-----------|---------------|-----------|----------|
| `IVariableProviderFactory` (`HeritageProviderFactory`) | Singleton | L4→L2 pull | Actor (L2) discovers `${heritage.*}` variables |
| `IVariableProviderFactory` (`LifecycleProviderFactory`) | Singleton | L4→L2 pull | Actor (L2) discovers `${lifecycle.*}` variables |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| InitiateMarriage | POST /character-lifecycle/marriage/initiate | generated | [] | profile (x2), cache | character-lifecycle.marriage |
| InitiateProcreation | POST /character-lifecycle/procreation/initiate | generated | [] | pregnancy-pending | - |
| RecordDeath | POST /character-lifecycle/death/record | generated | [] | profile, cache | character-lifecycle.death, character-lifecycle.inheritance.processed |
| GetLifecycleProfile | POST /character-lifecycle/profile/get | generated | [] | - | - |
| QueryByStage | POST /character-lifecycle/profile/query-by-stage | generated | [] | - | - |
| QueryByBloodline | POST /character-lifecycle/profile/query-by-bloodline | generated | [] | - | - |
| SetNaturalDeathYear | POST /character-lifecycle/profile/set-death-year | generated | developer | profile, cache | - |
| SeedLifecycleProfile | POST /character-lifecycle/profile/seed | generated | developer | profile, genetic | - |
| GetGeneticProfile | POST /character-lifecycle/heritage/get-genetic-profile | generated | user | - | - |
| GetPhenotype | POST /character-lifecycle/heritage/get-phenotype | generated | user | - | - |
| QueryByAptitude | POST /character-lifecycle/heritage/query-by-aptitude | generated | [] | - | - |
| SeedGeneticProfile | POST /character-lifecycle/heritage/seed-genetic-profile | generated | developer | genetic, cache | - |
| SimulateOffspring | POST /character-lifecycle/heritage/simulate-offspring | generated | [] | - | - |
| GetFamilyTree | POST /character-lifecycle/heritage/get-family-tree | generated | user | - | - |
| SeedLifecycleTemplate | POST /character-lifecycle/template/seed-lifecycle | generated | developer | lifecycle-template | character-lifecycle.lifecycle-template.created |
| SeedHeritableTraitTemplate | POST /character-lifecycle/template/seed-heritable-traits | generated | developer | trait-template | character-lifecycle.heritable-trait-template.created |
| SeedHybridTemplate | POST /character-lifecycle/template/seed-hybrid | generated | developer | hybrid-template | character-lifecycle.hybrid-trait-template.created |
| GetLifecycleTemplate | POST /character-lifecycle/template/get-lifecycle | generated | [] | - | - |
| GetHeritableTraitTemplate | POST /character-lifecycle/template/get-heritable-traits | generated | [] | - | - |
| ListTemplates | POST /character-lifecycle/template/list | generated | developer | - | - |
| GetBloodline | POST /character-lifecycle/bloodline/get | generated | user | - | - |
| ListBloodlines | POST /character-lifecycle/bloodline/list | generated | user | - | - |
| EstablishBloodline | POST /character-lifecycle/bloodline/establish | generated | developer | bloodline, member, cache | character-lifecycle.bloodline.formed, character-lifecycle.bloodline.created |
| DeleteBloodline | POST /character-lifecycle/bloodline/delete | generated | developer | bloodline, member | character-lifecycle.bloodline.deleted |
| QueryBloodlineMembers | POST /character-lifecycle/bloodline/query-members | generated | user | - | - |
| CleanupByCharacter | POST /character-lifecycle/cleanup-by-character | generated | [] | profile, genetic, member, cache | - |
| CleanupByRealm | POST /character-lifecycle/cleanup-by-realm | generated | [] | profile, genetic, member, cache | - |
| GetCompressData | POST /character-lifecycle/get-compress-data | generated | [] | - | - |
| RestoreFromArchive | POST /character-lifecycle/restore-from-archive | generated | [] | profile, genetic, cache | - |

---

## Methods

### InitiateMarriage
POST /character-lifecycle/marriage/initiate | Roles: []

```
LOCK lock:marriage:{characterAId}:{characterBId}              -> 409 if fails
READ profileStore:profile:{characterAId}                      -> 404 if null
READ profileStore:profile:{characterBId}                      -> 404 if null
READ heritageStore:lifecycle-template:{speciesCode}:{gameServiceId}  // stage capability
IF NOT template.stages[profileA.currentStage].canMarry        -> 400
IF NOT template.stages[profileB.currentStage].canMarry        -> 400
// Validate: no prohibited relationship, species compatibility

CALL IContractClient.CreateAsync(template: config.MarriageContractTemplateCode, parties)
  // Self-healing: orphaned contract expires via ContractExpirationService
CALL IRelationshipClient.CreateAsync(SPOUSE: characterAId ↔ characterBId)
  // If fails after contract: contract self-heals via TTL expiration

// Soft: household resolution
IF IOrganizationClient available
  CALL IOrganizationClient.AddMemberAsync(...)                // degraded if unavailable

// Soft: Disposition feeling seeds
IF IDispositionClient available
  CALL IDispositionClient.SeedFeelingAsync(warmth, trust)     // degraded if unavailable

WRITE profileStore:profile:{characterAId} <- updated with marriageContractIds, spouseCharacterIds
WRITE profileStore:profile:{characterBId} <- updated with marriageContractIds, spouseCharacterIds
DELETE cacheStore:manifest:{characterAId}
DELETE cacheStore:manifest:{characterBId}
PUBLISH character-lifecycle.marriage { characterAId, characterBId, contractId, householdOrgId, realmId }
RETURN (200, InitiateMarriageResponse)
```

### InitiateProcreation
POST /character-lifecycle/procreation/initiate | Roles: []

```
LOCK lock:procreation:{parentAId}:{parentBId}                 -> 409 if fails
READ profileStore:profile:{parentAId}                         -> 404 if null
READ profileStore:profile:{parentBId}                         -> 404 if null
READ heritageStore:lifecycle-template:{speciesCode}:{gameServiceId}
IF NOT template.stages[profileA.currentStage].canProcreate    -> 400
IF NOT template.stages[profileB.currentStage].canProcreate    -> 400
// Fertility check: combined modifier produces probability
IF fertility roll fails                                       -> 400
IF childCount >= config.MaxChildrenPerPair                    -> 400
IF parentA.totalChildCount >= config.MaxChildrenPerCharacter  -> 400

CALL IWorldstateClient.GetCurrentTimeAsync(realmId)
// expectedBirthDate = currentGameDay + config.PregnancyDurationGameDays

WRITE profileStore:pregnancy-pending:{pregnancyId} <- pending pregnancy record
RETURN (200, InitiateProcreationResponse { pregnancyId, expectedBirthDate })
```

### RecordDeath
POST /character-lifecycle/death/record | Roles: []

```
LOCK lock:death:{characterId}                                 -> 409 if fails
READ profileStore:profile:{characterId} [with ETag]           -> 404 if null
IF profile.status == Dead                                     -> 200 (idempotent return)

// Step 1: Cause of death (from request)
// Step 2: Fulfillment calculation
IF IDispositionClient available
  CALL IDispositionClient.GetAllDrivesAsync(characterId)
  // fulfillment = avg(drive.satisfaction * drive.intensity - drive.frustration * config.FulfillmentFrustrationWeight)
  // normalized to 0.0-1.0
ELSE
  fulfillment = config.FulfillmentNeutralDefault              // 0.3

// Step 3: Guardian spirit contribution
IF IDispositionClient available
  CALL IDispositionClient.GetGuardianFeelingsAsync(characterId)
// logos = (fulfillment * speciesLogosMultiplier * config.GuardianSpiritBaseMultiplier)
//       + (guardianTrust * config.GuardianTrustBonusWeight)
// Resolve: characterId → household Org → account owner → guardian seed
CALL ISeedClient.RecordGrowthAsync(guardianSeedId, logos)

// Step 4: Archive compression
CALL IResourceClient.ExecuteCleanupAsync(characterId, "character")  // idempotent

// Step 5: Inheritance (each with idempotency guards)
IF IOrganizationClient available
  CALL IOrganizationClient.TriggerSuccessionAsync(...)        // skip if new head assigned
CALL IContractClient.GetAsync(testament) + ExecuteAsync       // skip if already executed
CALL IInventoryClient.TransferItemsAsync(heirlooms)           // skip if already transferred
CALL ICurrencyClient.TransferAsync(inheritance)

// Step 6: Afterlife pathway (pure computation)
// Based on fulfillment, cause, personality traits, divine favor

// Step 7: Update profile
WRITE profileStore:profile:{characterId} <- status=Dead, deathGameYear, deathCause, fulfillmentScore, afterlifePath
DELETE cacheStore:manifest:{characterId}

// Step 8: Publish (only if not already Dead on entry)
PUBLISH character-lifecycle.death { characterId, deathCause, fulfillmentScore, afterlifePath, realmId, guardianSpiritContribution, archiveId }
PUBLISH character-lifecycle.inheritance.processed { deceasedId, heirId, assetsTransferred }
RETURN (200, RecordDeathResponse { fulfillmentScore, afterlifePath })
```

### GetLifecycleProfile
POST /character-lifecycle/profile/get | Roles: []

```
READ profileStore:profile:{characterId}                       -> 404 if null
RETURN (200, GetLifecycleProfileResponse)
```

### QueryByStage
POST /character-lifecycle/profile/query-by-stage | Roles: []

```
QUERY profileStore WHERE $.realmId == realmId AND $.currentStage == stageCode
  AND (includeArchived OR $.status != Archived)
  PAGED(page, config.QueryPageSize)
RETURN (200, QueryByStageResponse)
```

### QueryByBloodline
POST /character-lifecycle/profile/query-by-bloodline | Roles: []

```
READ bloodlineStore:bloodline:members:{bloodlineId}           -> 404 if null
QUERY profileStore WHERE $.characterId IN members AND $.status == Alive
  PAGED(page, config.QueryPageSize)
RETURN (200, QueryByBloodlineResponse)
```

### SetNaturalDeathYear
POST /character-lifecycle/profile/set-death-year | Roles: [developer]

```
READ profileStore:profile:{characterId} [with ETag]           -> 404 if null
ETAG-WRITE profileStore:profile:{characterId} <- updated naturalDeathYear  -> 409 if conflict
DELETE cacheStore:manifest:{characterId}
// No event published (silent adjustment)
RETURN (200, SetNaturalDeathYearResponse)
```

### SeedLifecycleProfile
POST /character-lifecycle/profile/seed | Roles: [developer]

```
FOREACH character in request.characters                       // per-item error isolation
  READ profileStore:profile:{characterId}                     // skip if exists
  READ heritageStore:lifecycle-template:{speciesCode}:{gameServiceId}
  // Compute naturalDeathYear from template + random
  WRITE profileStore:profile:{characterId} <- new LifecycleProfileModel { causeOfCreation=Seeded }
  IF heritageData provided
    WRITE heritageStore:genetic:{characterId} <- minimal GeneticProfileModel
RETURN (200, SeedLifecycleProfileResponse { createdCount, errors })
```

### GetGeneticProfile
POST /character-lifecycle/heritage/get-genetic-profile | Roles: [user]

```
READ heritageStore:genetic:{characterId}                      -> 404 if null
RETURN (200, GetGeneticProfileResponse)
```

### GetPhenotype
POST /character-lifecycle/heritage/get-phenotype | Roles: [user]

```
READ heritageStore:genetic:{characterId}                      -> 404 if null
// Return phenotype + aptitudes subset only
RETURN (200, GetPhenotypeResponse)
```

### QueryByAptitude
POST /character-lifecycle/heritage/query-by-aptitude | Roles: []

```
QUERY heritageStore WHERE $.aptitudes[domain].value > threshold
  PAGED(page, config.QueryPageSize)
RETURN (200, QueryByAptitudeResponse)
```

### SeedGeneticProfile
POST /character-lifecycle/heritage/seed-genetic-profile | Roles: [developer]

```
READ heritageStore:genetic:{characterId}                      -> 409 if exists (immutable)
READ heritageStore:trait-template:{speciesCode}:{gameServiceId}  -> 404 if no template
// Generate phenotype from provided values or species-default + random variation
// First-gen: genotype = phenotype (both alleles equal expressed value)
WRITE heritageStore:genetic:{characterId} <- new GeneticProfileModel
DELETE cacheStore:manifest:{characterId}
RETURN (200, SeedGeneticProfileResponse)
```

### SimulateOffspring
POST /character-lifecycle/heritage/simulate-offspring | Roles: []

```
READ heritageStore:genetic:{parentAId}                        -> 404 if null
READ heritageStore:genetic:{parentBId}                        -> 404 if null
READ heritageStore:trait-template:{speciesCode}:{gameServiceId}
// Run recombination algorithm multiple iterations for probability ranges
// Pure computation, no writes
RETURN (200, SimulateOffspringResponse { traitRanges, aptitudeRanges, bloodlineComposition })
```

### GetFamilyTree
POST /character-lifecycle/heritage/get-family-tree | Roles: [user]

```
READ profileStore:profile:{characterId}                       -> 404 if null
// Traverse ancestors: depth default 3 generations up
FOREACH generation (up to ancestorDepth)
  READ heritageStore:genetic:{ancestorId}                     // parentAId, parentBId
// Traverse descendants: depth default 2 generations down
FOREACH generation (up to descendantDepth)
  QUERY profileStore WHERE $.parentAId == characterId OR $.parentBId == characterId
// Assemble tree with heritage trait summaries per node
RETURN (200, GetFamilyTreeResponse)
```

### SeedLifecycleTemplate
POST /character-lifecycle/template/seed-lifecycle | Roles: [developer]

```
CALL IGameServiceClient.ValidateAsync(gameServiceId)          -> 400 if invalid
READ heritageStore:lifecycle-template:{speciesCode}:{gameServiceId}  -> 409 if exists
// Validate stage boundaries are contiguous (no gaps or overlaps)
WRITE heritageStore:lifecycle-template:{speciesCode}:{gameServiceId} <- new LifecycleTemplateModel
PUBLISH character-lifecycle.lifecycle-template.created { ... }
RETURN (200, SeedLifecycleTemplateResponse)
```

### SeedHeritableTraitTemplate
POST /character-lifecycle/template/seed-heritable-traits | Roles: [developer]

```
CALL IGameServiceClient.ValidateAsync(gameServiceId)          -> 400 if invalid
READ heritageStore:trait-template:{speciesCode}:{gameServiceId}  -> 409 if exists
WRITE heritageStore:trait-template:{speciesCode}:{gameServiceId} <- new HeritableTraitTemplateModel
PUBLISH character-lifecycle.heritable-trait-template.created { ... }
RETURN (200, SeedHeritableTraitTemplateResponse)
```

### SeedHybridTemplate
POST /character-lifecycle/template/seed-hybrid | Roles: [developer]

```
CALL IGameServiceClient.ValidateAsync(gameServiceId)          -> 400 if invalid
READ heritageStore:hybrid-template:{speciesA}:{speciesB}:{gameServiceId}  -> 409 if exists
WRITE heritageStore:hybrid-template:{speciesA}:{speciesB}:{gameServiceId} <- new HybridTraitTemplateModel
PUBLISH character-lifecycle.hybrid-trait-template.created { ... }
RETURN (200, SeedHybridTemplateResponse)
```

### GetLifecycleTemplate
POST /character-lifecycle/template/get-lifecycle | Roles: []

```
READ heritageStore:lifecycle-template:{speciesCode}:{gameServiceId}  -> 404 if null
RETURN (200, GetLifecycleTemplateResponse)
```

### GetHeritableTraitTemplate
POST /character-lifecycle/template/get-heritable-traits | Roles: []

```
READ heritageStore:trait-template:{speciesCode}:{gameServiceId}  -> 404 if null
RETURN (200, GetHeritableTraitTemplateResponse)
```

### ListTemplates
POST /character-lifecycle/template/list | Roles: [developer]

```
QUERY heritageStore WHERE $.gameServiceId == gameServiceId
  AND (includeDeprecated OR $.isDeprecated == false)          // Category B deprecation
// Query lifecycle templates, heritable trait templates, and hybrid templates
RETURN (200, ListTemplatesResponse)
```

### GetBloodline
POST /character-lifecycle/bloodline/get | Roles: [user]

```
READ bloodlineStore:bloodline:{bloodlineId}                   -> 404 if null
RETURN (200, GetBloodlineResponse)
```

### ListBloodlines
POST /character-lifecycle/bloodline/list | Roles: [user]

```
QUERY bloodlineStore WHERE $.gameServiceId == gameServiceId
  AND (traitSignature filter if provided)
  AND (generationSpan >= minGenerationDepth if provided)
  AND (memberCount >= minMemberCount if provided)
  PAGED(page, config.QueryPageSize)
RETURN (200, ListBloodlinesResponse)
```

### EstablishBloodline
POST /character-lifecycle/bloodline/establish | Roles: [developer]

```
READ bloodlineStore:bloodline:code:{gameServiceId}:{bloodlineCode}  -> 409 if exists
WRITE bloodlineStore:bloodline:{bloodlineId} <- new BloodlineModel
WRITE bloodlineStore:bloodline:code:{gameServiceId}:{bloodlineCode} <- lookup
// Retroactive ancestor assignment
FOREACH characterId in [originCharacterId + specified ancestors]
  WRITE bloodlineStore:bloodline:member:{characterId} <- updated membership
WRITE bloodlineStore:bloodline:members:{bloodlineId} <- initial member list
DELETE cacheStore:manifest:{characterId} for each assigned character
PUBLISH character-lifecycle.bloodline.formed { bloodlineCode, originCharacterId, traitSignature }
PUBLISH character-lifecycle.bloodline.created { ... }
RETURN (200, EstablishBloodlineResponse { bloodlineId })
```

### DeleteBloodline
POST /character-lifecycle/bloodline/delete | Roles: [developer]

```
READ bloodlineStore:bloodline:{bloodlineId}                   -> 404 if null
DELETE bloodlineStore:bloodline:{bloodlineId}
DELETE bloodlineStore:bloodline:code:{gameServiceId}:{bloodlineCode}
CALL IResourceClient.ExecuteCleanupAsync(bloodlineId, "bloodline")
  // CASCADE-cleans bloodline:member and bloodline:members indexes
PUBLISH character-lifecycle.bloodline.deleted { ... }
RETURN (200, DeleteBloodlineResponse)
```

### QueryBloodlineMembers
POST /character-lifecycle/bloodline/query-members | Roles: [user]

```
READ bloodlineStore:bloodline:members:{bloodlineId}           -> 404 if null
QUERY profileStore WHERE $.characterId IN members AND $.status == Alive
  PAGED(page, config.QueryPageSize)
// Include generation depth and trait expression per member
RETURN (200, QueryBloodlineMembersResponse)
```

### CleanupByCharacter
POST /character-lifecycle/cleanup-by-character | Roles: []

```
READ profileStore:profile:{characterId}                       // for parentAId, parentBId
DELETE profileStore:profile:{characterId}
DELETE heritageStore:genetic:{characterId}
READ bloodlineStore:bloodline:member:{characterId}
DELETE bloodlineStore:bloodline:member:{characterId}
FOREACH bloodlineId in memberships
  READ bloodlineStore:bloodline:members:{bloodlineId}
  WRITE bloodlineStore:bloodline:members:{bloodlineId} <- remove characterId
// Decrement parent child counts
IF parentAId != null
  READ profileStore:profile:{parentAId} [with ETag]
  ETAG-WRITE profileStore:profile:{parentAId} <- childCount--
IF parentBId != null
  READ profileStore:profile:{parentBId} [with ETag]
  ETAG-WRITE profileStore:profile:{parentBId} <- childCount--
DELETE cacheStore:manifest:{characterId}
// Does NOT cascade-delete children (they exist independently)
RETURN (200, CleanupByCharacterResponse)
```

### CleanupByRealm
POST /character-lifecycle/cleanup-by-realm | Roles: []

```
QUERY profileStore WHERE $.realmId == realmId
FOREACH profile in results                                    // per-item error isolation
  DELETE profileStore:profile:{characterId}
  DELETE heritageStore:genetic:{characterId}
  DELETE bloodlineStore:bloodline:member:{characterId}
  DELETE cacheStore:manifest:{characterId}
RETURN (200, CleanupByRealmResponse { cleanedCount })
```

### GetCompressData
POST /character-lifecycle/get-compress-data | Roles: []

```
READ profileStore:profile:{characterId}                       -> 404 if null
READ heritageStore:genetic:{characterId}                      // may be null for first-gen
READ bloodlineStore:bloodline:member:{characterId}
// Assemble LifecycleArchive: lifecycle summary + heritage profile + generational context
RETURN (200, LifecycleArchive)
```

### RestoreFromArchive
POST /character-lifecycle/restore-from-archive | Roles: []

```
// Restore from provided archive data
WRITE profileStore:profile:{characterId} <- restored LifecycleProfileModel
WRITE heritageStore:genetic:{characterId} <- restored GeneticProfileModel (as-is, immutable)
DELETE cacheStore:manifest:{characterId}
RETURN (200, RestoreFromArchiveResponse)
```

---

## Background Services

### LifecycleAgingWorkerService
**Trigger**: `worldstate.year-changed` event
**Lock**: `character-lifecycle:lock:aging:{realmId}`

```
// Triggered by worldstate.year-changed (not interval-based)
LOCK lock:aging:{realmId}                                     // skip at Debug if fails
QUERY profileStore WHERE $.realmId == realmId AND $.status == Alive
  PAGED(page, config.AgingBatchSize)
FOREACH profile in results                                    // per-item error isolation
  profile.currentAge += yearsCrossed
  READ heritageStore:lifecycle-template:{speciesCode}:{gameServiceId}
  newStage = ComputeStage(profile.currentAge, template.stages)
  IF newStage != profile.currentStage
    profile.currentStage = newStage
    profile.healthModifier = template.stages[newStage].healthModifier
    profile.fertilityModifier = ComputeFertility(...)
    WRITE profileStore:profile:{characterId}
    DELETE cacheStore:manifest:{characterId}
    PUBLISH character-lifecycle.stage-changed { characterId, previousStage, newStage, age, realmId }
    // Stage-specific: check latent trait expression at adolescence
    IF newStage triggers trait expression
      PUBLISH character-lifecycle.trait.expressed { characterId, traitCode, phenotypeValue }
    IF newStage == "dying"
      profile.status = Dying
      PUBLISH character-lifecycle.dying { characterId, projectedDeathYear, cause: "natural" }
  ELSE
    WRITE profileStore:profile:{characterId}                  // update age + modifiers
    DELETE cacheStore:manifest:{characterId}
  // Natural death check
  IF profile.currentAge >= profile.naturalDeathYear
    // Transition to Dying; death processing via RecordDeath (not inline)
WRITE cacheStore:realm-pop:{realmId} <- updated RealmPopulationModel
```

### LifecyclePregnancyWorkerService
**Trigger**: `worldstate.day-changed` event
**Lock**: `character-lifecycle:lock:pregnancy-worker`

```
// Triggered by worldstate.day-changed
LOCK lock:pregnancy-worker                                    // skip at Debug if fails
QUERY profileStore WHERE pregnancyExpectedDate <= currentGameDay
FOREACH pregnancy in results                                  // per-item error isolation
  READ heritageStore:genetic:{parentAId}
  READ heritageStore:genetic:{parentBId}
  READ heritageStore:trait-template:{speciesCode}:{gameServiceId}
  // Heritage computation (pure, no side effects)
  childGenotype = ComputeRecombination(parentA, parentB, template, config)
  childPhenotype = ComputePhenotype(childGenotype, template)
  childAptitudes = ComputeAptitudes(childPhenotype, template)
  childBloodlines = UnionBloodlines(parentA.bloodlines, parentB.bloodlines)

  // Point of no return: create character
  CALL ICharacterClient.CreateAsync(species, realm, ...)
  // Compensation boundary: if relationship fails, delete child
  CALL IRelationshipClient.CreateAsync(PARENT/CHILD/SIBLING bonds)
    // If fails: CALL ICharacterClient.DeleteAsync(childId) — compensate

  // Self-healing steps (all independently optional)
  CALL IWorldstateClient.GetCurrentTimeAsync(realmId)
  WRITE profileStore:profile:{childId} <- new LifecycleProfileModel { causeOfCreation=Procreation }
  WRITE heritageStore:genetic:{childId} <- new GeneticProfileModel
  IF IOrganizationClient available: CALL AddMemberAsync(household, child, "dependent")
  IF ICharacterHistoryClient available: CALL SeedBackstoryAsync(childId, ...)
  IF ICharacterPersonalityClient available: CALL SeedFromHeritageAsync(childId, ...)
  IF ICharacterEncounterClient available: CALL RecordBirthEncounterAsync(childId, ...)
  IF IHearsayClient available: CALL SeedFamilyBeliefsAsync(childId, ...)
  CALL IResourceClient.RegisterReferenceAsync(childId, "character", "lifecycle")

  // Update parent profiles
  WRITE profileStore:profile:{parentAId} <- childCount++, totalChildCount++
  WRITE profileStore:profile:{parentBId} <- childCount++, totalChildCount++
  DELETE pregnancy record
  DELETE cacheStore:manifest:{parentAId}
  DELETE cacheStore:manifest:{parentBId}
  PUBLISH character-lifecycle.birth { childId, parentIds, speciesCode, realmId, bloodlineIds }
```

### LifecycleBloodlineWorkerService
**Trigger**: `character-lifecycle.birth` event (batched)
**Lock**: `character-lifecycle:lock:bloodline-worker`

```
// Triggered by character-lifecycle.birth events, batched
LOCK lock:bloodline-worker                                    // skip at Debug if fails
FOREACH birth in pendingBirths                                // per-item error isolation
  // Evaluate whether newborn's lineage qualifies for bloodline formation
  READ heritageStore:genetic:{childId}
  // Walk ancestors up config.BloodlineFormationGenerations generations
  FOREACH generation
    READ heritageStore:genetic:{ancestorId}
  // Check trait consistency above config.BloodlineFormationTraitThreshold
  IF qualifying trait pattern found AND no existing bloodline matches
    WRITE bloodlineStore:bloodline:{newBloodlineId} <- new BloodlineModel
    WRITE bloodlineStore:bloodline:code:{gameServiceId}:{code} <- lookup
    FOREACH qualifying ancestor
      WRITE bloodlineStore:bloodline:member:{ancestorId} <- add membership
    WRITE bloodlineStore:bloodline:members:{bloodlineId} <- member list
    PUBLISH character-lifecycle.bloodline.formed { bloodlineCode, originCharacterId, traitSignature }
```

---

## Non-Standard Implementation Patterns

No non-standard patterns. All 29 endpoints are generated standard endpoints. No manual controllers, no `x-controller-only`, no `x-manual-implementation`, no `MapPost`/`MapGet` registrations.
