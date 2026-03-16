# Craft Implementation Map

> **Plugin**: lib-craft
> **Schema**: schemas/craft-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/CRAFT.md](../plugins/CRAFT.md)
> **Status**: Aspirational -- pseudo-code represents intended behavior, not verified implementation

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-craft |
| Layer | L4 GameFeatures |
| Endpoints | 31 (30 generated + 1 non-standard) |
| State Stores | craft-recipe-store (MySQL), craft-recipe-cache (Redis), craft-session-store (MySQL), craft-station-registry (MySQL), craft-discovery-store (MySQL), craft-lock (Redis) |
| Events Published | 11 (craft.recipe.created, craft.recipe.updated, craft.recipe.deleted, craft.session.started, craft.session.step-completed, craft.session.completed, craft.session.failed, craft.session.cancelled, craft.proficiency.gained, craft.proficiency.leveled, craft.recipe.discovered) |
| Events Consumed | 1 (contract.terminated) |
| Client Events | 0 |
| Background Services | 0 |

---

## State

**Store**: `craft-recipe-store` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `recipe:{recipeId}` | `RecipeDefinitionModel` | Primary recipe lookup by ID |
| `recipe-code:{gameServiceId}:{code}` | `RecipeDefinitionModel` | Code-uniqueness lookup within game service |

Paged queries via `IJsonQueryableStateStore.JsonQueryPagedAsync()` with filters for gameServiceId, recipeType, domain, category, tags, proficiency level range, and isDeprecated.

**Store**: `craft-recipe-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `recipe:{recipeId}` | `RecipeDefinitionModel` | Recipe hot cache (read-through from MySQL, TTL: `RecipeCacheTtlSeconds`) |
| `recipe-domain:{gameServiceId}:{domain}` | `List<RecipeDefinitionModel>` | All recipes in a domain for proficiency-based filtering |

**Store**: `craft-session-store` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `session:{sessionId}` | `CraftingSessionModel` | Active session state (sessionId = contractInstanceId) |
| `session-entity:{entityId}:{entityType}` | `List<string>` | Active session IDs per entity |

**Store**: `craft-station-registry` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `station:{stationId}` | `StationDefinitionModel` | Station definition and location |
| `station-loc:{locationId}` | `List<string>` | Station IDs at a location |
| `station-type:{gameServiceId}:{stationType}` | `List<string>` | Station IDs by type within game service |

**Store**: `craft-discovery-store` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `known:{entityId}:{entityType}:{gameServiceId}` | `KnownRecipesModel` | Set of recipe codes known by entity |
| `discovery-attempt:{entityId}:{hash}` | `DiscoveryAttemptModel` | Recent discovery attempts for hint progression |

**Store**: `craft-lock` (Backend: Redis) — accessed via `IDistributedLockProvider`

| Key Pattern | Purpose |
|-------------|---------|
| `session:{sessionId}` | Session step advancement lock |
| `recipe:{recipeId}` | Recipe definition mutation lock |
| `station:{stationId}` | Station occupancy lock (when `StationExclusivity` enabled) |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | Persistence for recipes (MySQL), sessions (MySQL), stations (MySQL), discovery (MySQL), recipe cache (Redis) |
| lib-state (IDistributedLockProvider) | L0 | Hard | Distributed locks for session advancement, recipe mutation, station occupancy |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing all crafting lifecycle events |
| lib-messaging (IEventConsumer) | L0 | Hard | Fan-out registration for `contract.terminated` subscription |
| lib-contract (IContractClient) | L1 | Hard | Creating session contracts from recipe step structures, milestone progression |
| lib-item (IItemClient) | L2 | Hard | Creating output items, destroying items for extraction, reading item metadata for quality |
| lib-inventory (IInventoryClient) | L2 | Hard | Consuming materials, placing outputs, checking tool availability, advisory material reservation |
| lib-currency (ICurrencyClient) | L2 | Hard | Deducting currency costs, authorization holds |
| lib-game-service (IGameServiceClient) | L2 | Hard | Validating game service existence for recipe scoping |
| lib-seed (ISeedClient) | L2 | Hard | Reading/updating proficiency seeds for crafting skill tracking |
| lib-location (ILocationClient) | L2 | Hard | Resolving station locations for proximity checks |
| lib-character (ICharacterClient) | L2 | Hard | Resolving character's current location for variable provider station proximity |
| lib-affix (IAffixClient) | L4 | Soft | Executing modification recipe affix operations (graceful degradation: modification recipes return error when absent) |
| lib-analytics (IAnalyticsClient) | L4 | Soft | Publishing crafting statistics for economy monitoring (graceful degradation: statistics not collected) |

**Note**: `IServiceProvider` held as a field for runtime resolution of soft L4 dependencies.

**Resource cleanup**: Three CASCADE callbacks registered via `RegisterResourceCleanupCallbacksAsync()`:
- game-service → `/craft/cleanup-by-game-service`
- character → `/craft/cleanup-by-entity`
- location → `/craft/cleanup-by-location`

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `craft.recipe.created` | `CraftRecipeCreatedEvent` | CreateRecipe — full recipe definition (x-lifecycle) |
| `craft.recipe.updated` | `CraftRecipeUpdatedEvent` | UpdateRecipe, DeprecateRecipe — full model + changedFields (x-lifecycle) |
| `craft.recipe.deleted` | `CraftRecipeDeletedEvent` | CleanDeprecatedRecipes — permanently removed recipe (x-lifecycle, Category B infrastructure) |
| `craft.session.started` | `CraftSessionStartedEvent` | StartSession — sessionId, recipeId, entityId, steps |
| `craft.session.step-completed` | `CraftSessionStepCompletedEvent` | AdvanceSession — stepCode, qualityContribution |
| `craft.session.completed` | `CraftSessionCompletedEvent` | CompleteSession (prebound) — outputItems, quality score |
| `craft.session.failed` | `CraftSessionFailedEvent` | CompleteSession on failure, HandleContractTerminated |
| `craft.session.cancelled` | `CraftSessionCancelledEvent` | CancelSession, CleanupByEntity — materialsReturned |
| `craft.proficiency.gained` | `CraftProficiencyGainedEvent` | CompleteSession, GrantExperience — domain, experienceAmount |
| `craft.proficiency.leveled` | `CraftProficiencyLeveledEvent` | CompleteSession, GrantExperience — domain, newLevel (only if seed phase changed) |
| `craft.recipe.discovered` | `CraftRecipeDiscoveredEvent` | AttemptDiscovery, TeachRecipe — recipeCode, entityId |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `contract.terminated` | `HandleContractTerminatedAsync` | Look up session by contractInstanceId; if found: return materials, release currency holds, delete session, publish `craft.session.failed` |

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `ILogger<CraftService>` | Structured logging |
| `CraftServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 5 stores + lock store) |
| `IDistributedLockProvider` | Distributed lock acquisition |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event subscription registration |
| `IContractClient` | Session contract creation and milestone progression (L1 hard) |
| `IItemClient` | Item creation/destruction, metadata reads (L2 hard) |
| `IInventoryClient` | Material consumption, output placement, tool checks (L2 hard) |
| `ICurrencyClient` | Currency cost deduction and holds (L2 hard) |
| `IGameServiceClient` | Game service existence validation (L2 hard) |
| `ISeedClient` | Proficiency seed reading/updating (L2 hard) |
| `ILocationClient` | Station location resolution (L2 hard) |
| `ICharacterClient` | Character location resolution for variable provider (L2 hard) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies |

#### DI Interfaces Implemented by This Plugin

| Interface | Registered As | Direction | Consumer |
|-----------|---------------|-----------|----------|
| `IVariableProviderFactory` | `Singleton` (via `CraftDecisionProviderFactory`) | L4→L2 pull | Actor (L2) discovers `${craft.*}` variables for GOAP decisions |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| CreateRecipe | POST /craft/recipe/create | generated | developer | recipe, recipe-code, cache | craft.recipe.created |
| GetRecipe | POST /craft/recipe/get | generated | [] | - | - |
| ListRecipes | POST /craft/recipe/list | generated | [] | - | - |
| UpdateRecipe | POST /craft/recipe/update | generated | developer | recipe, cache | craft.recipe.updated |
| DeprecateRecipe | POST /craft/recipe/deprecate | generated | developer | recipe, cache | craft.recipe.updated |
| SeedRecipes | POST /craft/recipe/seed | generated | developer | recipe, recipe-code, cache | craft.recipe.created |
| ListDomains | POST /craft/recipe/list-domains | generated | [] | - | - |
| CleanDeprecatedRecipes | POST /craft/recipe/clean-deprecated | generated | admin | recipe, recipe-code, cache | craft.recipe.deleted |
| StartSession | POST /craft/session/start | generated | [] | session, session-entity | craft.session.started |
| AdvanceSession | POST /craft/session/advance | generated | [] | session | craft.session.step-completed |
| CancelSession | POST /craft/session/cancel | generated | [] | session, session-entity | craft.session.cancelled |
| GetSession | POST /craft/session/get | generated | [] | - | - |
| ListSessions | POST /craft/session/list | generated | [] | - | - |
| GetProficiency | POST /craft/proficiency/get | generated | [] | - | - |
| ListProficiencies | POST /craft/proficiency/list | generated | [] | - | - |
| GrantExperience | POST /craft/proficiency/grant | generated | developer | - | craft.proficiency.gained, craft.proficiency.leveled |
| SetProficiency | POST /craft/proficiency/set | generated | developer | - | craft.proficiency.gained, craft.proficiency.leveled |
| RegisterStation | POST /craft/station/register | generated | developer | station, station-loc, station-type | - |
| GetStation | POST /craft/station/get | generated | [] | - | - |
| ListStations | POST /craft/station/list | generated | [] | - | - |
| DeregisterStation | POST /craft/station/deregister | generated | developer | station, station-type | - |
| AttemptDiscovery | POST /craft/discovery/attempt | generated | [] | known, discovery-attempt | craft.recipe.discovered |
| ListKnownRecipes | POST /craft/discovery/list-known | generated | [] | - | - |
| TeachRecipe | POST /craft/discovery/teach | generated | [] | known | craft.recipe.discovered |
| CanCraft | POST /craft/query/can-craft | generated | [] | - | - |
| EstimateCraftQuality | POST /craft/query/estimate-quality | generated | [] | - | - |
| GetRecipeOutputPreview | POST /craft/query/output-preview | generated | [] | - | - |
| CleanupByGameService | POST /craft/cleanup-by-game-service | generated | [] | recipe, session, station, discovery | craft.session.cancelled |
| CleanupByEntity | POST /craft/cleanup-by-entity | generated | [] | session, discovery | craft.session.cancelled |
| CleanupByLocation | POST /craft/cleanup-by-location | generated | [] | station, station-loc | - |
| CompleteSession | POST /craft/internal/complete-session | manual | internal | session, session-entity | craft.session.completed, craft.session.failed, craft.proficiency.gained, craft.proficiency.leveled |

---

## Methods

### CreateRecipe
POST /craft/recipe/create | Roles: [developer]

```
CALL _gameServiceClient.GetGameServiceAsync(gameServiceId)       -> 400 if not found
READ recipe-store:recipe-code:{gameServiceId}:{code}             -> 409 if exists
COUNT recipe-store WHERE $.gameServiceId == gameServiceId
  -> 400 if count >= config.MaxRecipesPerGameService
// Validate step structure, input/output references, quality weights sum to 1.0
IF recipe.qualityWeights AND sum != 1.0                          -> 400
WRITE recipe-store:recipe:{recipeId} <- RecipeDefinitionModel from request
WRITE recipe-store:recipe-code:{gameServiceId}:{code} <- RecipeDefinitionModel
WRITE recipe-cache:recipe:{recipeId} <- RecipeDefinitionModel (TTL: config.RecipeCacheTtlSeconds)
PUBLISH craft.recipe.created { recipeId, gameServiceId, code, recipeType, domain }
RETURN (200, CreateRecipeResponse { recipeId })
```

### GetRecipe
POST /craft/recipe/get | Roles: []

```
IF request has recipeId
  READ recipe-cache:recipe:{recipeId}
  IF cache miss
    READ recipe-store:recipe:{recipeId}                          -> 404 if null
    WRITE recipe-cache:recipe:{recipeId} <- model (TTL: config.RecipeCacheTtlSeconds)
ELSE // lookup by gameServiceId + code
  READ recipe-store:recipe-code:{gameServiceId}:{code}           -> 404 if null
RETURN (200, GetRecipeResponse { full RecipeDefinitionModel })
```

### ListRecipes
POST /craft/recipe/list | Roles: []

```
QUERY recipe-store WHERE $.gameServiceId == gameServiceId
  [AND $.recipeType == filter] [AND $.domain == filter]
  [AND $.category == filter] [AND $.tags CONTAINS ANY filter]
  [AND $.isDeprecated == false (default)]
  ORDER BY $.domain, $.category PAGED(page, pageSize)
RETURN (200, ListRecipesResponse { items, pagination })
```

### UpdateRecipe
POST /craft/recipe/update | Roles: [developer]

```
LOCK craft-lock:recipe:{recipeId}                                -> 409 if lock fails
  READ recipe-store:recipe:{recipeId}                            -> 404 if null
  IF request changes code, gameServiceId, or recipeType          -> 400 (identity-level)
  // Apply partial update to model
  WRITE recipe-store:recipe:{recipeId} <- updated model
  DELETE recipe-cache:recipe:{recipeId}
  DELETE recipe-cache:recipe-domain:{gameServiceId}:{domain}
  PUBLISH craft.recipe.updated { recipeId, changedFields }
RETURN (200, UpdateRecipeResponse { updated recipe })
```

### DeprecateRecipe
POST /craft/recipe/deprecate | Roles: [developer]

```
LOCK craft-lock:recipe:{recipeId}                                -> 409 if lock fails
  READ recipe-store:recipe:{recipeId}                            -> 404 if null
  IF recipe.isDeprecated                                         -> 200 (idempotent, no event)
  recipe.isDeprecated = true
  recipe.deprecatedAt = DateTimeOffset.UtcNow
  recipe.deprecationReason = request.reason
  WRITE recipe-store:recipe:{recipeId} <- updated model
  DELETE recipe-cache:recipe:{recipeId}
  DELETE recipe-cache:recipe-domain:{gameServiceId}:{domain}
  PUBLISH craft.recipe.updated { changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
RETURN (200, DeprecateRecipeResponse { recipe })
```

### SeedRecipes
POST /craft/recipe/seed | Roles: [developer]

```
CALL _gameServiceClient.GetGameServiceAsync(gameServiceId)       -> 400 if not found
FOREACH recipe in request.recipes
  READ recipe-store:recipe-code:{gameServiceId}:{recipe.code}
  IF exists: skip (increment skippedCount)
  ELSE
    WRITE recipe-store:recipe:{recipeId} <- RecipeDefinitionModel
    WRITE recipe-store:recipe-code:{gameServiceId}:{code} <- model
    WRITE recipe-cache:recipe:{recipeId} <- model (TTL)
    PUBLISH craft.recipe.created { recipeId, code }
    // increment createdCount
RETURN (200, SeedRecipesResponse { createdCount, skippedCount })
```

### ListDomains
POST /craft/recipe/list-domains | Roles: []

```
QUERY recipe-store for distinct domain values
  WHERE $.gameServiceId == gameServiceId
  [AND $.isDeprecated == false (default)]
  GROUP BY $.domain with COUNT
RETURN (200, ListDomainsResponse { domains: [{ domain, recipeCount }] })
```

### CleanDeprecatedRecipes
POST /craft/recipe/clean-deprecated | Roles: [admin]

```
// Uses DeprecationCleanupHelper.ExecuteCleanupSweepAsync
QUERY recipe-store WHERE $.isDeprecated == true
FOREACH recipe in deprecated
  // Check grace period
  IF recipe.deprecatedAt + request.gracePeriodSeconds > now: skip
  // Check active sessions (ephemeral instance check — direct store query)
  QUERY session-store WHERE $.recipeId == recipe.recipeId
  IF active sessions > 0: skip
  IF request.dryRun: count only
  ELSE
    DELETE recipe-store:recipe:{recipeId}
    DELETE recipe-store:recipe-code:{gameServiceId}:{code}
    DELETE recipe-cache:recipe:{recipeId}
    PUBLISH craft.recipe.deleted { recipeId, deletedReason }
RETURN (200, CleanDeprecatedResponse { deletedCount, skippedCount })
```

### StartSession
POST /craft/session/start | Roles: []

```
READ recipe-store:recipe:{recipeId} (via cache)                  -> 404 if not found
IF recipe.isDeprecated                                           -> 400 (Category B guard)
READ discovery-store:known:{entityId}:{entityType}:{gameServiceId}
IF recipe.code NOT in known set                                  -> 400 (recipe not known)
READ session-store:session-entity:{entityId}:{entityType}
IF count >= config.MaxActiveSessionsPerEntity                    -> 400
CALL _seedClient.GetSeedAsync(entityId, "proficiency:{domain}")  -> 400 if below minimum
CALL _inventoryClient.CheckItemAvailabilityAsync(materials)      -> 400 if insufficient
CALL _currencyClient.GetWalletAsync(entityId)                    -> 400 if insufficient
IF recipe requires station
  CALL _locationClient.GetStationsAtLocationAsync(entityLocation) -> 400 if no matching station
IF recipe requires tool
  CALL _inventoryClient.CheckToolAvailabilityAsync(toolCategory) -> 400 if no tool
IF recipe.recipeType == "modification"
  IF _serviceProvider.GetService<IAffixClient>() == null         -> 400 (affix unavailable)
  CALL _itemClient.GetItemInstanceAsync(targetItemId)            -> 400 if target requirements not met
IF config.StationExclusivity
  LOCK craft-lock:station:{stationId}
// Multi-entity sessions: Contract multi-party model
IF request.participants is not empty
  CALL _contractClient.CreateContractAsync(milestones from steps,
    parties: [primary + participants], prebound: /craft/internal/complete-session)
  // Contract handles multi-party consent flow
ELSE
  CALL _contractClient.CreateContractAsync(milestones from steps, prebound: /craft/internal/complete-session)
CALL _inventoryClient.ReserveItemsAsync(materials)               // advisory reservation
CALL _currencyClient.CreateAuthorizationHoldAsync(costs)
WRITE session-store:session:{contractInstanceId} <- CraftingSessionModel { participants }
WRITE session-store:session-entity:{entityId}:{entityType} <- append sessionId
PUBLISH craft.session.started { sessionId, recipeId, entityId, entityType, participants }
RETURN (200, StartSessionResponse { sessionId, firstStep, participants })
```

### AdvanceSession
POST /craft/session/advance | Roles: []

```
LOCK craft-lock:session:{sessionId}                              -> 409 if lock fails
  READ session-store:session:{sessionId}                         -> 404 if not found
  IF request.stepCode != session.nextExpectedStep                -> 400 (wrong sequence)
  // Multi-entity: check role eligibility for this step
  IF step.eligibleRoles is not null
    IF request.entityId not in session.participants              -> 400 (not a participant)
    IF participant.role NOT in step.eligibleRoles                -> 400 (role not eligible for step)
  ELSE
    IF request.entityId != session.primaryEntityId               -> 400 (primary only)
  IF step requires station
    CALL _locationClient.GetStationsAtLocationAsync(location)    -> 400 if station gone
  IF step requires tool
    CALL _inventoryClient.CheckToolAvailabilityAsync(tool)       -> 400 if tool lost
  IF step.skillCheck
    // Compute quality contribution (seeded PRNG, uses advancing entity's proficiency)
    qualityContribution = computeStepQuality(advancingEntity, recipe, step)
  IF step.consumeOnStep
    CALL _inventoryClient.ConsumeItemsAsync(consumeOnStep materials)
  CALL _contractClient.CompleteMilestoneAsync(sessionId, stepCode)
  session.accumulatedQuality += qualityContribution
  session.currentStep = nextStep
  WRITE session-store:session:{sessionId} <- updated session
  PUBLISH craft.session.step-completed { sessionId, stepCode, qualityContribution, advancedBy }
  // If final step: Contract executes prebound API → CompleteSession
RETURN (200, AdvanceSessionResponse { qualityContribution, nextStepCode })
```

### CancelSession
POST /craft/session/cancel | Roles: []

```
READ session-store:session:{sessionId}                           -> 404 if not found
CALL _inventoryClient.ReturnReservedItemsAsync(session.reservedMaterials)
CALL _currencyClient.ReleaseAuthorizationHoldAsync(session.holdId)
CALL _contractClient.TerminateContractAsync(sessionId)
DELETE session-store:session:{sessionId}
// Remove from entity index
READ session-store:session-entity:{entityId}:{entityType}
WRITE session-store:session-entity:{entityId}:{entityType} <- remove sessionId
IF config.StationExclusivity
  // Release station lock
PUBLISH craft.session.cancelled { sessionId, recipeId, entityId, materialsReturned }
RETURN (200, CancelSessionResponse)
```

### GetSession
POST /craft/session/get | Roles: []

```
READ session-store:session:{sessionId}                           -> 404 if null
RETURN (200, GetSessionResponse { recipeId, currentStep, accumulatedQuality, elapsed })
```

### ListSessions
POST /craft/session/list | Roles: []

```
READ session-store:session-entity:{entityId}:{entityType}
FOREACH sessionId in list
  READ session-store:session:{sessionId}
RETURN (200, ListSessionsResponse { sessions })
```

### GetProficiency
POST /craft/proficiency/get | Roles: []

```
CALL _seedClient.GetSeedAsync(entityId, "proficiency:{domain}")  -> 404 if not found
RETURN (200, GetProficiencyResponse { domain, level, experience, phase })
```

### ListProficiencies
POST /craft/proficiency/list | Roles: []

```
CALL _seedClient.ListSeedsAsync(entityId, prefix: "proficiency:")
RETURN (200, ListProficienciesResponse { proficiencies })
```

### GrantExperience
POST /craft/proficiency/grant | Roles: [developer]

```
CALL _seedClient.AddGrowthAsync(entityId, "proficiency:{domain}", amount) -> 400 if invalid domain
CALL _seedClient.GetSeedAsync(entityId, "proficiency:{domain}")  // read new state
PUBLISH craft.proficiency.gained { entityId, domain, experienceAmount }
IF seed phase changed
  PUBLISH craft.proficiency.leveled { entityId, domain, newLevel }
RETURN (200, GrantExperienceResponse { newExperience, levelChanged, newLevel })
```

### SetProficiency
POST /craft/proficiency/set | Roles: [developer]

```
CALL _seedClient.SetGrowthAsync(entityId, "proficiency:{domain}", targetValue) -> 400 if invalid
PUBLISH craft.proficiency.gained { entityId, domain }
IF seed phase changed
  PUBLISH craft.proficiency.leveled { entityId, domain, newLevel }
RETURN (200, SetProficiencyResponse)
```

### RegisterStation
POST /craft/station/register | Roles: [developer]

```
CALL _locationClient.GetLocationAsync(locationId)                -> 400 if not found
WRITE station-registry:station:{stationId} <- StationDefinitionModel from request
WRITE station-registry:station-loc:{locationId} <- append stationId
WRITE station-registry:station-type:{gameServiceId}:{stationType} <- append stationId
RETURN (200, RegisterStationResponse { stationId })
```

### GetStation
POST /craft/station/get | Roles: []

```
READ station-registry:station:{stationId}                        -> 404 if null
RETURN (200, GetStationResponse { station })
```

### ListStations
POST /craft/station/list | Roles: []

```
QUERY station-registry with filters: stationType, locationId, gameServiceId, isActive
RETURN (200, ListStationsResponse { stations })
```

### DeregisterStation
POST /craft/station/deregister | Roles: [developer]

```
READ station-registry:station:{stationId}                        -> 404 if null
station.isActive = false
WRITE station-registry:station:{stationId} <- updated model
// Remove from type index
READ station-registry:station-type:{gameServiceId}:{stationType}
WRITE station-registry:station-type:{gameServiceId}:{stationType} <- remove stationId
RETURN (200, DeregisterStationResponse)
```

### AttemptDiscovery
POST /craft/discovery/attempt | Roles: []

```
CALL _inventoryClient.CheckItemAvailabilityAsync(materials)      -> 400 if unavailable
// Hash material codes + station type (deterministic)
READ discovery-store:discovery-attempt:{entityId}:{hash}         // hint progression
QUERY recipe-store WHERE $.isDiscoverable == true
  AND prerequisiteRecipeCodes all in entity's known set
IF hash matches a recipe's discoveryHints
  READ discovery-store:known:{entityId}:{entityType}:{gameServiceId}
  WRITE discovery-store:known:{entityId}:{entityType}:{gameServiceId} <- append recipeCode
  CALL _inventoryClient.ConsumeItemsAsync(materials * config.DiscoveryMaterialConsumptionRate)
  PUBLISH craft.recipe.discovered { entityId, recipeCode }
  RETURN (200, AttemptDiscoveryResponse { discovered: true, recipeCode })
ELSE
  CALL _inventoryClient.ConsumeItemsAsync(materials * config.DiscoveryMaterialConsumptionRate)
  WRITE discovery-store:discovery-attempt:{entityId}:{hash} <- attempt record
  RETURN (200, AttemptDiscoveryResponse { discovered: false, hintProgression })
```

### ListKnownRecipes
POST /craft/discovery/list-known | Roles: []

```
READ discovery-store:known:{entityId}:{entityType}:{gameServiceId}
FOREACH code in knownRecipeCodes
  READ recipe-store (via cache) for recipe details
  CALL _seedClient.GetSeedAsync(entityId, "proficiency:{domain}")
  // Determine proficiency eligibility
RETURN (200, ListKnownRecipesResponse { recipes with canCraftNow })
```

### TeachRecipe
POST /craft/discovery/teach | Roles: []

```
READ recipe-store:recipe:{recipeId} (via cache)                  -> 404 if not found
IF recipe.isDeprecated                                           -> 400
READ discovery-store:known:{entityId}:{entityType}:{gameServiceId}
IF recipeCode already in known set                               -> 200 (idempotent)
WRITE discovery-store:known:{entityId}:{entityType}:{gameServiceId} <- append recipeCode
PUBLISH craft.recipe.discovered { entityId, recipeCode }
RETURN (200, TeachRecipeResponse)
```

### CanCraft
POST /craft/query/can-craft | Roles: []

```
READ recipe-store:recipe:{recipeId} (via cache)                  -> 404 if not found
READ discovery-store:known:{entityId}:{entityType}:{gameServiceId}
CALL _seedClient.GetSeedAsync(entityId, "proficiency:{domain}")
CALL _inventoryClient.CheckItemAvailabilityAsync(recipe.inputs)
CALL _locationClient.GetStationsAtLocationAsync(entityLocation)
CALL _inventoryClient.CheckToolAvailabilityAsync(recipe.steps.toolCategory)
CALL _currencyClient.GetWalletAsync(entityId)
RETURN (200, CanCraftResponse { canCraft, breakdown })
```

### EstimateCraftQuality
POST /craft/query/estimate-quality | Roles: []

```
READ recipe-store:recipe:{recipeId} (via cache)                  -> 404 if not found
FOREACH materialId in request.materials
  CALL _itemClient.GetItemInstanceAsync(materialId)              // read quality metadata
CALL _seedClient.GetSeedAsync(entityId, "proficiency:{domain}")
CALL _itemClient.GetItemInstanceAsync(request.toolId)            // read tool quality
// Compute: materialQuality * weights + proficiency * weights + toolQuality * weights
RETURN (200, EstimateCraftQualityResponse { estimatedQuality, breakdown })
```

### GetRecipeOutputPreview
POST /craft/query/output-preview | Roles: []

```
READ recipe-store:recipe:{recipeId} (via cache)                  -> 404 if not found
// Compute output preview at various quality levels
RETURN (200, GetRecipeOutputPreviewResponse { outputs with qualityBands })
```

### CleanupByGameService
POST /craft/cleanup-by-game-service | Roles: []

```
// Cancel all active sessions for this game service
QUERY session-store for sessions with recipes matching gameServiceId
FOREACH session (per-item error isolation)
  CALL _contractClient.TerminateContractAsync(session.contractInstanceId)
  CALL _inventoryClient.ReturnReservedItemsAsync(session.reservedMaterials)
  CALL _currencyClient.ReleaseAuthorizationHoldAsync(session.holdId)
  DELETE session-store:session:{sessionId}
  PUBLISH craft.session.cancelled { sessionId }
// Delete all recipes
QUERY recipe-store WHERE $.gameServiceId == gameServiceId
FOREACH recipe: DELETE recipe-store:recipe:{recipeId}; DELETE recipe-code index; DELETE cache
// Delete all stations
QUERY station-registry WHERE gameServiceId matches
FOREACH station: DELETE station-registry entries and indexes
// Delete all discovery data
QUERY discovery-store for gameServiceId
FOREACH: DELETE discovery entries
RETURN (200, CleanupResponse)
```

### CleanupByEntity
POST /craft/cleanup-by-entity | Roles: []

```
READ session-store:session-entity:{entityId}:{entityType}
FOREACH sessionId (per-item error isolation)
  READ session-store:session:{sessionId}
  CALL _contractClient.TerminateContractAsync(session.contractInstanceId)
  CALL _inventoryClient.ReturnReservedItemsAsync(session.reservedMaterials)
  CALL _currencyClient.ReleaseAuthorizationHoldAsync(session.holdId)
  DELETE session-store:session:{sessionId}
  PUBLISH craft.session.cancelled { sessionId, entityId }
DELETE session-store:session-entity:{entityId}:{entityType}
// Delete all discovery data for this entity across all game services
DELETE discovery-store:known:{entityId}:{entityType}:*
DELETE discovery-store:discovery-attempt:{entityId}:*
RETURN (200, CleanupResponse)
```

### CleanupByLocation
POST /craft/cleanup-by-location | Roles: []

```
READ station-registry:station-loc:{locationId}
FOREACH stationId (per-item error isolation)
  READ station-registry:station:{stationId}
  station.isActive = false
  WRITE station-registry:station:{stationId} <- updated model
DELETE station-registry:station-loc:{locationId}
RETURN (200, CleanupResponse)
```

### CompleteSession
POST /craft/internal/complete-session | Roles: [internal]

```
// Prebound API called by Contract on final milestone completion
READ session-store:session:{sessionId}
// Compute final quality
materialQuality = average quality of reserved materials
proficiencyFactor = normalized proficiency level
toolQuality = tool's quality value (default 0.5)
stepBonuses = accumulated quality from skill-check steps
finalQuality = (materialQuality * weights.materialQuality)
             + (proficiencyFactor * weights.proficiencyLevel)
             + (toolQuality * weights.toolQuality)
             + stepBonuses
// Multi-entity collaboration bonus
IF session.participants.count > 1
  finalQuality += (session.participants.count - 1) * config.CollaborationBonusMultiplier

CALL _inventoryClient.ConsumeItemsAsync(remaining materials)
CALL _currencyClient.ExecuteAuthorizationHoldAsync(session.holdId)

IF recipe.recipeType == "production"
  FOREACH output in recipe.outputs
    CALL _itemClient.CreateItemInstanceAsync(output.templateCode, quality metadata)
    CALL _inventoryClient.PlaceItemAsync(entity inventory, itemInstanceId)
ELSE IF recipe.recipeType == "modification"
  // Translate quality to affix parameters (lib-craft configuration)
  CALL _affixClient.{affixOperation}Async(targetItemId, affixConfig, qualityParams)
ELSE IF recipe.recipeType == "extraction"
  CALL _itemClient.DestroyItemInstanceAsync(targetItemId)
  FOREACH output in recipe.extractionOutputs
    IF random(0,1) <= output.probability
      quantity = output.baseQuantity ± output.quantityVariance (quality-influenced)
      CALL _itemClient.CreateItemInstanceAsync(output.templateCode, quantity)
      CALL _inventoryClient.PlaceItemAsync(entity inventory, itemInstanceId)

// Grant XP to all participants (primary gets full, others get apprentice multiplier)
CALL _seedClient.AddGrowthAsync(primaryEntityId, "proficiency:{domain}", experience * config.ExperienceScalingFactor)
IF session.participants.count > 1
  FOREACH participant in session.participants (non-primary)
    apprenticeXP = experience * config.ExperienceScalingFactor * config.ApprenticeBonusMultiplier
    CALL _seedClient.AddGrowthAsync(participant.entityId, "proficiency:{domain}", apprenticeXP)
    PUBLISH craft.proficiency.gained { participant.entityId, domain, apprenticeXP }
    IF seed phase changed
      PUBLISH craft.proficiency.leveled { participant.entityId, domain, newLevel }
DELETE session-store:session:{sessionId}
// Remove from entity index
WRITE session-store:session-entity:{entityId}:{entityType} <- remove sessionId
IF config.StationExclusivity
  // Release station lock

PUBLISH craft.session.completed { sessionId, outputItems, finalQuality, participants }
PUBLISH craft.proficiency.gained { primaryEntityId, domain, experience }
IF seed phase changed
  PUBLISH craft.proficiency.leveled { primaryEntityId, domain, newLevel }
RETURN (200, CompleteSessionResponse)
```

---

## Background Services

No background services. Session expiration is handled by Contract's own expiration mechanism (sessions backed by Contract instances with `SessionTimeoutSeconds` TTL). The `contract.terminated` event triggers cleanup via the event handler.

---

## Non-Standard Implementation Patterns

#### Plugin Lifecycle (OnRunningAsync)

```
// Seed type registration — derived from existing recipe data
QUERY recipe-store for distinct proficiency domains across all recipes
FOREACH domain in discovered domains
  CALL _seedClient.RegisterSeedTypeAsync("proficiency:{domain}", phases: [novice, apprentice, journeyman, expert, master])

// Resource cleanup callback registration
CALL RegisterResourceCleanupCallbacksAsync()
  // Registers: game-service → /craft/cleanup-by-game-service (CASCADE)
  //            character → /craft/cleanup-by-entity (CASCADE)
  //            location → /craft/cleanup-by-location (CASCADE)
```

**Note**: Seed type registration happens after recipe seeding — seed types are derived from recipe data, not predefined. A fresh deployment with no recipes has no proficiency seed types registered (Intentional Quirk #9 in deep dive).

#### Non-Schema HTTP Endpoint

The `CompleteSession` endpoint (`POST /craft/internal/complete-session`) is a prebound API callback registered with Contract at session creation time. It is called by Contract via lib-mesh when the final milestone completes. It is not on the generated `ICraftService` interface and must be registered via `x-permissions: []` in the schema or manually via `MapPost` in plugin startup.
