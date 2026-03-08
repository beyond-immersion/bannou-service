# Agency Implementation Map

> **Plugin**: lib-agency
> **Schema**: schemas/agency-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/AGENCY.md](../plugins/AGENCY.md)
> **Status**: Aspirational -- pseudo-code represents intended behavior, not verified implementation

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-agency |
| Layer | L4 GameFeatures |
| Endpoints | 22 |
| State Stores | agency-domains (MySQL), agency-modules (MySQL), agency-influences (MySQL), agency-manifest-cache (Redis), agency-manifest-history (MySQL), agency-seed-config (Redis), agency-lock (Redis) |
| Events Published | 13 (9 lifecycle + 4 custom: manifest.updated, influence.executed, influence.resisted, influence.rejected) |
| Events Consumed | 4 (seed.capability.updated, seed.growth.recorded, actor.spirit-nudge.resisted, connect.session.disconnected) |
| Client Events | 0 (manifest updates routed through Gardener) |
| Background Services | 2 (ManifestRecomputeWorker, ManifestHistoryRetentionWorker) |

---

## State

**Store**: `agency-domains` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `domain:{domainCode}` | `AgencyDomainModel` | UX domain definitions with seed domain path mappings |

**Store**: `agency-modules` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `module:{moduleCode}` | `AgencyModuleModel` | UX module definitions with fidelity curves |

**Store**: `agency-influences` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `influence:{influenceCode}` | `AgencyInfluenceModel` | Influence type definitions with compliance factors |

**Store**: `agency-manifest-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `manifest:{seedId}` | `AgencyManifestModel` | Cached computed manifests per seed (TTL-based) |
| `debounce:{seedId}` | raw TTL key | Recompute debounce timer (Redis key expiry) |
| `rate:{seedId}` | atomic counter | Influence rate limit per seed (1-second TTL window) |
| `influence-last:{seedId}` | `AgencyInfluenceLastModel` | Last influence attempt (code, timestamp, accepted) |
| `influence-freq:{seedId}` | sorted set | Rolling window of influence timestamps for frequency calculation |
| `resistance:{seedId}` | float | Accumulated resistance buildup from frequent overrides |

**Store**: `agency-manifest-history` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `history:{seedId}:{timestamp}` | `AgencyManifestHistoryModel` | Manifest change log with previous/new states and delta |

**Store**: `agency-seed-config` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `seed-config:{gameServiceId}` | `AgencySeedConfigModel` | Per-game guardian seed type configuration |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | 6 typed stores + lock store |
| lib-state (IDistributedLockProvider) | L0 | Hard | Uniqueness locks on create/delete operations |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing lifecycle + custom events; consuming seed/actor/connect events |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation on async helpers |
| lib-seed (ISeedClient) | L2 | Hard | Read capability manifests for manifest computation |
| lib-disposition (IDispositionClient) | L4 | Soft | Guardian feelings for compliance computation; defaults to `DefaultComplianceBase` if absent |
| lib-gardener (IGardenerClient) | L4 | Soft | Garden context for manifest routing; manifests computed but not routed if absent |

**DI Provider Registration**: Agency implements `IVariableProviderFactory` as `SpiritProviderFactory`, providing the `${spirit.*}` namespace to Actor (L2) via the Variable Provider Factory pattern.

**T28 Note**: `agency-manifest-history` (MySQL) is persistent data keyed by seedId. Must implement `ISeededResourceProvider` and declare `x-references` with `target: seed, onDelete: cascade`.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `agency.domain.created` | lifecycle | CreateDomain |
| `agency.domain.updated` | lifecycle | (future: DeprecateDomain / UndeprecateDomain) |
| `agency.domain.deleted` | lifecycle | DeleteDomain |
| `agency.module.created` | lifecycle | CreateModule |
| `agency.module.updated` | lifecycle | UpdateModule |
| `agency.module.deleted` | lifecycle | DeleteModule, DeleteDomain (cascade) |
| `agency.influence.created` | lifecycle | CreateInfluence |
| `agency.influence.updated` | lifecycle | UpdateInfluence |
| `agency.influence.deleted` | lifecycle | DeleteInfluence, DeleteDomain (cascade) |
| `agency.manifest.updated` | `AgencyManifestUpdatedEvent` | RecomputeManifest, ManifestRecomputeWorker (on seed capability change) |
| `agency.influence.executed` | `AgencyInfluenceExecutedEvent` | ExecuteInfluence |
| `agency.influence.resisted` | `AgencyInfluenceResistedEvent` | HandleActorSpiritNudgeResistedAsync (relay from Actor L2 event) |
| `agency.influence.rejected` | `AgencyInfluenceRejectedEvent` | ExecuteInfluence (rate-limited or not in manifest) |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `seed.capability.updated` | `HandleSeedCapabilityUpdatedAsync` | Set/reset Redis debounce key for seedId; ManifestRecomputeWorker processes after debounce |
| `seed.growth.recorded` | `HandleSeedGrowthRecordedAsync` | Check threshold crossings; if crossed, set debounce key for manifest recomputation |
| `actor.spirit-nudge.resisted` | `HandleActorSpiritNudgeResistedAsync` | Enrich with Agency context; relay as `agency.influence.resisted` |
| `connect.session.disconnected` | `HandleSessionDisconnectedAsync` | Clear cached manifest for disconnected seed (stale cache prevention) |

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<AgencyService>` | Structured logging |
| `AgencyServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (used in constructor, not stored as field) |
| `IDistributedLockProvider` | Distributed locking for create/delete operations |
| `IMessageBus` | Event publishing and subscription |
| `ISeedClient` | Read seed capability manifests (L2 hard dependency) |
| `ITelemetryProvider` | Span creation for async helpers |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies (Disposition, Gardener) |
| `IEventConsumer` | Register event handler subscriptions (used in constructor only) |
| `SpiritProviderFactory` | `IVariableProviderFactory` implementation providing `${spirit.*}` namespace |
| `ManifestComputeHelper` | Encapsulates manifest computation logic (threshold evaluation, fidelity mapping, cross-seed pollination) |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| CreateDomain | POST /agency/domain/create | developer | domains | agency.domain.created |
| GetDomain | POST /agency/domain/get | developer | - | - |
| ListDomains | POST /agency/domain/list | developer | - | - |
| DeleteDomain | POST /agency/domain/delete | developer | domains, modules, influences | agency.domain.deleted, agency.module.deleted, agency.influence.deleted |
| CreateModule | POST /agency/module/create | developer | modules | agency.module.created |
| UpdateModule | POST /agency/module/update | developer | modules | agency.module.updated |
| GetModule | POST /agency/module/get | developer | - | - |
| ListModules | POST /agency/module/list | developer | - | - |
| DeleteModule | POST /agency/module/delete | developer | modules | agency.module.deleted |
| CreateInfluence | POST /agency/influence/create | developer | influences | agency.influence.created |
| UpdateInfluence | POST /agency/influence/update | developer | influences | agency.influence.updated |
| GetInfluence | POST /agency/influence/get | developer | - | - |
| ListInfluences | POST /agency/influence/list | developer | - | - |
| DeleteInfluence | POST /agency/influence/delete | developer | influences | agency.influence.deleted |
| GetManifest | POST /agency/manifest/get | user | manifest-cache | - |
| RecomputeManifest | POST /agency/manifest/recompute | developer | manifest-cache, manifest-history | agency.manifest.updated |
| DiffManifests | POST /agency/manifest/diff | user | - | - |
| GetManifestHistory | POST /agency/manifest/history | user | - | - |
| EvaluateInfluence | POST /agency/influence/evaluate | user | - | - |
| ExecuteInfluence | POST /agency/influence/execute | user | rate, influence-last, influence-freq, resistance | agency.influence.executed, agency.influence.rejected |
| SetSeedConfig | POST /agency/seed-config/set | developer | seed-config | - |
| GetSeedConfig | POST /agency/seed-config/get | developer | - | - |

---

## Methods

### CreateDomain
POST /agency/domain/create | Roles: [developer]

```
LOCK agency-lock:domain:{request.DomainCode}                 -> 409 if lock fails
  READ _domainsStore:domain:{request.DomainCode}              -> 409 if exists
  WRITE _domainsStore:domain:{request.DomainCode} <- AgencyDomainModel from request
  PUBLISH agency.domain.created { domainCode, displayName, seedDomainPaths }
RETURN (200, DomainResponse)
```

---

### GetDomain
POST /agency/domain/get | Roles: [developer]

```
READ _domainsStore:domain:{request.DomainCode}                -> 404 if null
RETURN (200, DomainResponse)
```

---

### ListDomains
POST /agency/domain/list | Roles: [developer]

```
QUERY _domainsStore WHERE $.GameServiceId == request.GameServiceId (if provided)
  AND $.IsDeprecated == false (unless request.IncludeDeprecated)
  [ORDER BY $.DomainCode ASC]
  [PAGED(request.Page, request.PageSize)]
// Module/influence counts per domain require additional COUNT queries
FOREACH domain in results
  COUNT _modulesStore WHERE $.DomainCode == domain.DomainCode AND $.IsDeprecated == false
  COUNT _influencesStore WHERE $.DomainCode == domain.DomainCode AND $.IsDeprecated == false
RETURN (200, ListDomainsResponse { domains, totalCount })
```

---

### DeleteDomain
POST /agency/domain/delete | Roles: [developer]

```
READ _domainsStore:domain:{request.DomainCode}                -> 404 if null
IF existing.IsDeprecated == false
  -> 400 // Category A: must deprecate before delete (T31)
LOCK agency-lock:domain:{request.DomainCode}                  -> 409 if lock fails
  // Cascade: delete child modules
  QUERY _modulesStore WHERE $.DomainCode == request.DomainCode
  FOREACH module in modules
    DELETE _modulesStore:module:{module.ModuleCode}
    PUBLISH agency.module.deleted { moduleCode }
  // Cascade: delete child influences
  QUERY _influencesStore WHERE $.DomainCode == request.DomainCode
  FOREACH influence in influences
    DELETE _influencesStore:influence:{influence.InfluenceCode}
    PUBLISH agency.influence.deleted { influenceCode }
  DELETE _domainsStore:domain:{request.DomainCode}
  PUBLISH agency.domain.deleted { domainCode }
RETURN (200, null)
```

---

### CreateModule
POST /agency/module/create | Roles: [developer]

```
READ _domainsStore:domain:{request.DomainCode}                -> 404 if null
IF domain.IsDeprecated == true
  -> 400 // Cannot add modules to deprecated domain
COUNT _modulesStore WHERE $.DomainCode == request.DomainCode AND $.IsDeprecated == false
IF count >= config.MaxModulesPerDomain
  -> 400 // Module limit reached
LOCK agency-lock:module:{request.ModuleCode}                  -> 409 if lock fails
  READ _modulesStore:module:{request.ModuleCode}              -> 409 if exists
  WRITE _modulesStore:module:{request.ModuleCode} <- AgencyModuleModel from request
  PUBLISH agency.module.created { moduleCode, domainCode, depthThreshold, fidelityCurve, sortOrder }
RETURN (200, ModuleResponse)
```

---

### UpdateModule
POST /agency/module/update | Roles: [developer]

```
READ _modulesStore:module:{request.ModuleCode} [with ETag]    -> 404 if null
// Updatable: DepthThreshold, FidelityCurve, SortOrder, DisplayName, Description
// Immutable: ModuleCode, DomainCode
ETAG-WRITE _modulesStore:module:{request.ModuleCode} <- merged model -> 409 on ETag mismatch
PUBLISH agency.module.updated { moduleCode, changedFields }
RETURN (200, ModuleResponse)
```

---

### GetModule
POST /agency/module/get | Roles: [developer]

```
READ _modulesStore:module:{request.ModuleCode}                -> 404 if null
RETURN (200, ModuleResponse)
```

---

### ListModules
POST /agency/module/list | Roles: [developer]

```
QUERY _modulesStore WHERE $.DomainCode == request.DomainCode (if provided)
  AND $.IsDeprecated == false (unless request.IncludeDeprecated)
  AND $.GameServiceId == request.GameServiceId (if provided)
  [ORDER BY $.SortOrder ASC]
  [PAGED(request.Page, request.PageSize)]
RETURN (200, ListModulesResponse { modules, totalCount })
```

---

### DeleteModule
POST /agency/module/delete | Roles: [developer]

```
READ _modulesStore:module:{request.ModuleCode}                -> 404 if null
IF existing.IsDeprecated == false
  -> 400 // Category A: must deprecate before delete (T31)
DELETE _modulesStore:module:{request.ModuleCode}
PUBLISH agency.module.deleted { moduleCode }
RETURN (200, null)
```

---

### CreateInfluence
POST /agency/influence/create | Roles: [developer]

```
READ _domainsStore:domain:{request.DomainCode}                -> 404 if null
IF domain.IsDeprecated == true
  -> 400 // Cannot add influences to deprecated domain
COUNT _influencesStore WHERE $.DomainCode == request.DomainCode AND $.IsDeprecated == false
IF count >= config.MaxInfluencesPerDomain
  -> 400 // Influence limit reached
LOCK agency-lock:influence:{request.InfluenceCode}            -> 409 if lock fails
  READ _influencesStore:influence:{request.InfluenceCode}     -> 409 if exists
  // ComplianceFactors is typed array of {AxisCode, Weight} objects (not additionalProperties)
  WRITE _influencesStore:influence:{request.InfluenceCode} <- AgencyInfluenceModel from request
  PUBLISH agency.influence.created { influenceCode, domainCode, perceptionType, depthThreshold }
RETURN (200, InfluenceResponse)
```

---

### UpdateInfluence
POST /agency/influence/update | Roles: [developer]

```
READ _influencesStore:influence:{request.InfluenceCode} [with ETag]  -> 404 if null
// Updatable: DepthThreshold, PerceptionType, ComplianceFactors, Intensity
// Immutable: InfluenceCode, DomainCode
ETAG-WRITE _influencesStore:influence:{request.InfluenceCode} <- merged model -> 409 on ETag mismatch
PUBLISH agency.influence.updated { influenceCode, changedFields }
RETURN (200, InfluenceResponse)
```

---

### GetInfluence
POST /agency/influence/get | Roles: [developer]

```
READ _influencesStore:influence:{request.InfluenceCode}       -> 404 if null
RETURN (200, InfluenceResponse)
```

---

### ListInfluences
POST /agency/influence/list | Roles: [developer]

```
QUERY _influencesStore WHERE $.DomainCode == request.DomainCode (if provided)
  AND $.IsDeprecated == false (unless request.IncludeDeprecated)
  AND $.GameServiceId == request.GameServiceId (if provided)
  [PAGED(request.Page, request.PageSize)]
RETURN (200, ListInfluencesResponse { influences, totalCount })
```

---

### DeleteInfluence
POST /agency/influence/delete | Roles: [developer]

```
READ _influencesStore:influence:{request.InfluenceCode}       -> 404 if null
IF existing.IsDeprecated == false
  -> 400 // Category A: must deprecate before delete (T31)
DELETE _influencesStore:influence:{request.InfluenceCode}
PUBLISH agency.influence.deleted { influenceCode }
RETURN (200, null)
```

---

### GetManifest
POST /agency/manifest/get | Roles: [user]

```
READ _manifestCacheStore:manifest:{request.SeedId}
IF cached AND not expired
  RETURN (200, ManifestResponse)
// Cache miss: compute on demand
CALL ISeedClient.GetCapabilityManifestAsync(request.SeedId)   -> 404 if seed not found
IF config.CrossSeedPollinationEnabled
  CALL ISeedClient.GetAccountSeedsAsync(seed.AccountId)
  // Apply CrossSeedPollinationFactor to cross-seed depths
  // Merge: max(primary, crossSeed * factor) per capability path
// see helper: ManifestComputeHelper
QUERY _modulesStore WHERE $.IsDeprecated == false [ORDER BY $.SortOrder ASC]
QUERY _influencesStore WHERE $.IsDeprecated == false
// For each module: if seedDepth >= depthThreshold, enabled; compute fidelity from curve
// For each influence: if seedDepth >= depthThreshold, available
WRITE _manifestCacheStore:manifest:{request.SeedId} <- manifest with TTL(config.ManifestCacheTtlMinutes)
RETURN (200, ManifestResponse)
```

---

### RecomputeManifest
POST /agency/manifest/recompute | Roles: [developer]

```
CALL ISeedClient.GetCapabilityManifestAsync(request.SeedId)   -> 404 if seed not found
IF config.CrossSeedPollinationEnabled
  CALL ISeedClient.GetAccountSeedsAsync(seed.AccountId)
  // Apply CrossSeedPollinationFactor; merge capabilities
// see helper: ManifestComputeHelper
QUERY _modulesStore WHERE $.IsDeprecated == false [ORDER BY $.SortOrder ASC]
QUERY _influencesStore WHERE $.IsDeprecated == false
READ _manifestCacheStore:manifest:{request.SeedId}            // Previous manifest for diff
WRITE _manifestCacheStore:manifest:{request.SeedId} <- newManifest with TTL
WRITE _manifestHistoryStore:history:{request.SeedId}:{now} <- AgencyManifestHistoryModel { previous, new, delta }
IF newManifest differs from previous
  PUBLISH agency.manifest.updated { seedId, changedModules, changedInfluences }
RETURN (200, ManifestResponse)
```

---

### DiffManifests
POST /agency/manifest/diff | Roles: [user]

```
READ _manifestCacheStore:manifest:{request.SeedId}            -> 404 if null
QUERY _manifestHistoryStore WHERE $.SeedId == request.SeedId
  AND $.ComputedAt <= request.FromTimestamp
  [ORDER BY $.ComputedAt DESC] LIMIT 1
// Compute delta between historical snapshot and current manifest
RETURN (200, ManifestDiffResponse { changedModules, changedInfluences })
```

---

### GetManifestHistory
POST /agency/manifest/history | Roles: [user]

```
QUERY _manifestHistoryStore WHERE $.SeedId == request.SeedId
  [ORDER BY $.ComputedAt DESC]
  [PAGED(request.Page, request.PageSize)]
RETURN (200, ManifestHistoryResponse { entries, totalCount })
```

---

### EvaluateInfluence
POST /agency/influence/evaluate | Roles: [user]

```
READ _influencesStore:influence:{request.InfluenceCode}       -> 404 if null
IF influence.IsDeprecated == true
  -> 400
READ _manifestCacheStore:manifest:{request.SeedId}            -> 404 if null
// Check influence availability in manifest
IF influence not available in manifest
  RETURN (200, InfluenceEvaluationResponse { available: false, reason: "BelowThreshold" })
// Compute compliance estimate
IF IDispositionClient available (soft resolve)
  CALL IDispositionClient.GetGuardianFeelingsAsync(request.CharacterId, request.SeedId)
  // compliance = trust * (1 - resentment * ComplianceResentmentWeight) * familiarityModifier
  // familiarityModifier = min(1.0, familiarity * ComplianceFamiliarityScale + ComplianceFamiliarityFloor)
ELSE
  complianceBase <- config.DefaultComplianceBase
READ _manifestCacheStore:rate:{request.SeedId}                // Rate limit check
RETURN (200, InfluenceEvaluationResponse { available: true, complianceBase, rateLimitRemaining })
```

---

### ExecuteInfluence
POST /agency/influence/execute | Roles: [user]

```
READ _influencesStore:influence:{request.InfluenceCode}       -> 404 if null
IF influence.IsDeprecated == true
  -> 400
// Rate limit check (Redis atomic INCR with 1-second TTL)
READ _manifestCacheStore:rate:{request.SeedId}
IF rate >= config.InfluenceRateLimitPerSecond
  PUBLISH agency.influence.rejected { seedId, influenceCode, rejectionReason: "RateLimited" }
  -> 409
INCREMENT _manifestCacheStore:rate:{request.SeedId}
// Manifest availability check
READ _manifestCacheStore:manifest:{request.SeedId}
IF manifest null OR influence not available
  PUBLISH agency.influence.rejected { seedId, influenceCode, rejectionReason: "NotInManifest" }
  -> 409
// Compute compliance
IF IDispositionClient available (soft resolve)
  CALL IDispositionClient.GetGuardianFeelingsAsync(request.CharacterId, request.SeedId)
  // compliance formula (same as EvaluateInfluence)
ELSE
  complianceBase <- config.DefaultComplianceBase
// Read resistance buildup
READ _manifestCacheStore:resistance:{request.SeedId}
// Update influence tracking in Redis
WRITE _manifestCacheStore:influence-last:{request.SeedId} <- { influenceCode, timestamp }
// Add to frequency sorted set; prune entries older than InfluenceFrequencyWindowMinutes
// Does NOT call Actor directly (Intentional Quirk #3 -- returns payload for Gardener)
PUBLISH agency.influence.executed { seedId, influenceCode, perceptionType, complianceFactors, complianceBase, intensity }
RETURN (200, InfluenceExecutionResponse { perceptionType, complianceFactors, complianceBase, intensity })
```

---

### SetSeedConfig
POST /agency/seed-config/set | Roles: [developer]

```
WRITE _seedConfigStore:seed-config:{request.GameServiceId} <- AgencySeedConfigModel from request
RETURN (200, SeedConfigResponse)
```

---

### GetSeedConfig
POST /agency/seed-config/get | Roles: [developer]

```
READ _seedConfigStore:seed-config:{request.GameServiceId}
IF null
  // Fall back to service-wide default
  RETURN (200, SeedConfigResponse { seedTypeCode: config.SeedTypeCode, isDefault: true })
RETURN (200, SeedConfigResponse { seedTypeCode, isDefault: false })
```

---

## Background Services

### ManifestRecomputeWorker
**Trigger**: Event-driven via `seed.capability.updated` and `seed.growth.recorded`; debounced per seed via Redis TTL key
**Purpose**: Recompute manifests when seed capabilities change

```
// Event handlers set Redis debounce key:
WRITE _manifestCacheStore:debounce:{seedId} <- "1" with TTL(config.ManifestRecomputeDebounceMs)
// After debounce settles (key expires):
CALL ISeedClient.GetCapabilityManifestAsync(seedId)
IF config.CrossSeedPollinationEnabled
  CALL ISeedClient.GetAccountSeedsAsync(accountId)
  // Apply CrossSeedPollinationFactor; merge capabilities
// see helper: ManifestComputeHelper
QUERY _modulesStore WHERE $.IsDeprecated == false [ORDER BY $.SortOrder ASC]
QUERY _influencesStore WHERE $.IsDeprecated == false
READ _manifestCacheStore:manifest:{seedId}                    // Previous for diff
WRITE _manifestCacheStore:manifest:{seedId} <- newManifest with TTL
WRITE _manifestHistoryStore:history:{seedId}:{now} <- history entry
IF newManifest != previous
  PUBLISH agency.manifest.updated { seedId, changedModules, changedInfluences }
```

### ManifestHistoryRetentionWorker
**Interval**: Periodic (daily); retention from `config.ManifestHistoryRetentionDays`
**Purpose**: Purge stale manifest history records

```
QUERY _manifestHistoryStore WHERE $.ComputedAt < (now - config.ManifestHistoryRetentionDays)
FOREACH entry in results
  DELETE _manifestHistoryStore:history:{entry.SeedId}:{entry.ComputedAt}
```
