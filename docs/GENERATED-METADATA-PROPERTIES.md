# Generated Metadata Properties Reference

> **Source**: `schemas/*.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document tracks all `additionalProperties: true` properties across Bannou schemas
for FOUNDATION TENETS (T29: No Metadata Bag Contracts) compliance.

## What Metadata Bags Are For

Per T29, `additionalProperties: true` serves exactly **two** legitimate purposes:

1. **Client-side display data** - Game clients store rendering hints, UI preferences,
   or display-only information that no Bannou service consumes.
2. **Game-specific implementation data** - Data the game engine (not Bannou services)
   interprets at runtime. Opaque to all Bannou plugins.

In both cases, the metadata is **opaque to all Bannou plugins**. No plugin reads
specific keys by convention. No plugin's correctness depends on its structure.

### Compliance Marker

Compliant properties include one of these phrases in their description:
- "No Bannou plugin reads specific keys"
- "Client-only"
- "Opaque to all Bannou plugins"

## Compliance Summary

| Metric | Count |
|--------|-------|
| Total metadata bag properties | 165 |
| Compliant (has marker) | 141 |
| Non-compliant (missing marker) | 24 |
| Compliance rate | 85% |

## Properties by Service

### Infrastructure (L0)

#### Messaging

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `PublishEventRequest` | `payload` | `messaging-api.yaml` | Y | Arbitrary event payload (any valid JSON object). Wrapped opaquely in GenericM... |
| `PublishOptions` | `headers` | `messaging-api.yaml` | Y | Caller-provided custom headers included with the message. No Bannou plugin re... |

#### State

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `BulkSaveItem` | `value` | `state-api.yaml` | **N** | The value to store |
| `BulkStateItem` | `value` | `state-api.yaml` | **N** | The value (null if not found) |
| `GetStateResponse` | `value` | `state-api.yaml` | **N** | The stored value (null if not found) |
| `SaveStateRequest` | `value` | `state-api.yaml` | **N** | Value to store |

### App Foundation (L1)

#### Account

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `AccountResponse` | `metadata` | `account-api.yaml` | Y | Client-only metadata. No Bannou plugin reads specific keys from this field by... |
| `CreateAccountRequest` | `metadata` | `account-api.yaml` | Y | Client-only metadata. No Bannou plugin reads specific keys from this field by... |
| `UpdateAccountRequest` | `metadata` | `account-api.yaml` | Y | Client-only metadata. No Bannou plugin reads specific keys from this field by... |
| `UpdateProfileRequest` | `metadata` | `account-api.yaml` | Y | Client-only metadata. No Bannou plugin reads specific keys from this field by... |

#### Connect

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `AuthEvent` | `metadata` | `connect-api.yaml` | Y | Additional auth event metadata (device info, location, etc.). Uses additional... |
| `GetEndpointMetaResponse` | `data` | `connect-api.yaml` | **N** | Metadata payload whose structure varies by metaType (endpoint-info returns su... |
| `InternalProxyRequest` | `body` | `connect-api.yaml` | **N** | Request body to forward to target service (null for no body). Uses additional... |

#### Contract

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `CheckConstraintRequest` | `proposedAction` | `contract-api.yaml` | Y | Client-only proposed action data. No Bannou plugin reads specific keys from t... |
| `ClauseHandlerDefinition` | `requestMapping` | `contract-api.yaml` | Y | Client-only request field mapping. No Bannou plugin reads specific keys from ... |
| `ClauseHandlerDefinition` | `responseMapping` | `contract-api.yaml` | Y | Client-only response field mapping. No Bannou plugin reads specific keys from... |
| `CompleteMilestoneRequest` | `evidence` | `contract-api.yaml` | Y | Client-only evidence data. No Bannou plugin reads specific keys from this fie... |
| `ContractInstanceResponse` | `gameMetadata` | `contract-api.yaml` | Y | Client-only game metadata. No Bannou plugin reads specific keys from this fie... |
| `ContractMetadataResponse` | `instanceData` | `contract-api.yaml` | Y | Client-only instance metadata. No Bannou plugin reads specific keys from this... |
| `ContractMetadataResponse` | `runtimeState` | `contract-api.yaml` | Y | Client-only runtime state. No Bannou plugin reads specific keys from this fie... |
| `ContractTemplateResponse` | `gameMetadata` | `contract-api.yaml` | Y | Client-only game metadata. No Bannou plugin reads specific keys from this fie... |
| `ContractTerms` | `customTerms` | `contract-api.yaml` | Y | Client-only custom terms. No Bannou plugin reads specific keys from this fiel... |
| `CreateContractInstanceRequest` | `gameMetadata` | `contract-api.yaml` | Y | Client-only game metadata. No Bannou plugin reads specific keys from this fie... |
| `CreateContractTemplateRequest` | `gameMetadata` | `contract-api.yaml` | Y | Client-only game metadata. No Bannou plugin reads specific keys from this fie... |
| `UpdateContractMetadataRequest` | `data` | `contract-api.yaml` | Y | Client-only metadata payload. No Bannou plugin reads specific keys from this ... |
| `UpdateContractTemplateRequest` | `gameMetadata` | `contract-api.yaml` | Y | Client-only game metadata. No Bannou plugin reads specific keys from this fie... |
| `ContractMilestoneCompletedEvent` | `evidence` | `contract-events.yaml` | Y | Client-only completion evidence. No Bannou plugin reads specific keys from th... |

### Game Foundation (L2)

#### Actor

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `ActorTemplateResponse` | `cognitionOverrides` | `actor-api.yaml` | **N** | Static template-level cognition overrides (polymorphic JSON). Deserialized in... |
| `ActorTemplateResponse` | `configuration` | `actor-api.yaml` | Y | Game-specific configuration passed to ABML behavior execution scope. No Banno... |
| `CreateActorTemplateRequest` | `cognitionOverrides` | `actor-api.yaml` | **N** | Static template-level cognition overrides (polymorphic JSON). Deserialized in... |
| `CreateActorTemplateRequest` | `configuration` | `actor-api.yaml` | Y | Game-specific configuration passed to ABML behavior execution scope. No Banno... |
| `EncounterState` | `data` | `actor-api.yaml` | Y | Game-specific encounter state data passed to ABML behavior scope. No Bannou p... |
| `OptionsQueryContext` | `customContext` | `actor-api.yaml` | Y | Game-specific context data passed through to ABML behavior scope. No Bannou p... |
| `PerceptionData` | `data` | `actor-api.yaml` | Y | Game-specific perception payload passed to ABML behavior execution scope. No ... |
| `SpawnActorRequest` | `configurationOverrides` | `actor-api.yaml` | Y | Game-specific configuration overrides merged with template defaults. No Banno... |
| `SpawnActorRequest` | `initialState` | `actor-api.yaml` | Y | Initial actor state snapshot. Deserialized internally to ActorStateSnapshot. ... |
| `StartEncounterRequest` | `initialData` | `actor-api.yaml` | Y | Game-specific encounter initialization data passed to ABML behavior scope. No... |
| `UpdateActorTemplateRequest` | `cognitionOverrides` | `actor-api.yaml` | **N** | Updated cognition overrides (polymorphic JSON). Deserialized internally to Co... |
| `UpdateActorTemplateRequest` | `configuration` | `actor-api.yaml` | Y | Updated game-specific configuration for ABML behavior execution scope. No Ban... |
| `SendMessageCommand` | `payload` | `actor-events.yaml` | Y | Game-specific message payload passed to ABML behavior scope. No Bannou plugin... |
| `SpawnActorCommand` | `configuration` | `actor-events.yaml` | Y | Game-specific configuration for ABML behavior execution scope. No Bannou plug... |
| `SpawnActorCommand` | `initialState` | `actor-events.yaml` | Y | Initial actor state snapshot. No Bannou plugin reads specific keys from this ... |

#### Collection

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `EntryMetadata` | `customData` | `collection-api.yaml` | Y | Arbitrary custom data for game-specific metadata. No Bannou plugin reads spec... |
| `UpdateEntryMetadataRequest` | `customData` | `collection-api.yaml` | Y | Updated custom data (merged with existing). No Bannou plugin reads specific k... |

#### Currency

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `CreditCurrencyRequest` | `metadata` | `currency-api.yaml` | Y | Free-form transaction metadata. No Bannou plugin reads specific keys from thi... |
| `CurrencyTransactionRecord` | `metadata` | `currency-api.yaml` | Y | Free-form metadata. No Bannou plugin reads specific keys from this field by c... |
| `DebitCurrencyRequest` | `metadata` | `currency-api.yaml` | Y | Free-form transaction metadata. No Bannou plugin reads specific keys from thi... |
| `TransferCurrencyRequest` | `metadata` | `currency-api.yaml` | Y | Free-form transaction metadata. No Bannou plugin reads specific keys from thi... |

#### Game Session

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `CreateGameSessionRequest` | `gameSettings` | `game-session-api.yaml` | Y | Game-specific configuration settings. No Bannou plugin reads specific keys fr... |
| `GameActionRequest` | `actionData` | `game-session-api.yaml` | Y | Game-specific action data. No Bannou plugin reads specific keys from this fie... |
| `GameActionResponse` | `newGameState` | `game-session-api.yaml` | Y | Updated game state (if applicable). No Bannou plugin reads specific keys from... |
| `GameActionResponse` | `result` | `game-session-api.yaml` | Y | Game-specific action result data. No Bannou plugin reads specific keys from t... |
| `GamePlayer` | `characterData` | `game-session-api.yaml` | Y | Game-specific character data for this player. No Bannou plugin reads specific... |
| `GameSessionResponse` | `gameSettings` | `game-session-api.yaml` | Y | Game-specific configuration settings. No Bannou plugin reads specific keys fr... |
| `JoinGameSessionByIdRequest` | `characterData` | `game-session-api.yaml` | Y | Game-specific character data. No Bannou plugin reads specific keys from this ... |
| `JoinGameSessionRequest` | `characterData` | `game-session-api.yaml` | Y | Game-specific character data. No Bannou plugin reads specific keys from this ... |
| `JoinGameSessionResponse` | `gameData` | `game-session-api.yaml` | Y | Game-specific initial state data. No Bannou plugin reads specific keys from t... |

#### Item

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `UseItemRequest` | `context` | `item-api.yaml` | Y | Caller-provided context merged into contract gameMetadata for template value ... |
| `UseItemStepRequest` | `context` | `item-api.yaml` | Y | Caller-provided context merged into contract gameMetadata for template value ... |
| `UseItemStepRequest` | `evidence` | `item-api.yaml` | Y | Evidence data passed through to Contract milestone completion. No Bannou plug... |

#### Location

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `CreateLocationRequest` | `metadata` | `location-api.yaml` | Y | Client-provided location metadata. No Bannou plugin reads specific keys from ... |
| `LocationResponse` | `metadata` | `location-api.yaml` | Y | Client-provided location metadata. No Bannou plugin reads specific keys from ... |
| `SeedLocation` | `metadata` | `location-api.yaml` | Y | Client-provided location metadata. No Bannou plugin reads specific keys from ... |
| `UpdateLocationRequest` | `metadata` | `location-api.yaml` | Y | Updated client-provided location metadata. No Bannou plugin reads specific ke... |

#### Realm

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `CreateRealmRequest` | `metadata` | `realm-api.yaml` | Y | Client-only metadata. No Bannou plugin reads specific keys from this field by... |
| `RealmResponse` | `metadata` | `realm-api.yaml` | Y | Client-only metadata. No Bannou plugin reads specific keys from this field by... |
| `SeedRealm` | `metadata` | `realm-api.yaml` | Y | Client-only metadata. No Bannou plugin reads specific keys from this field by... |
| `UpdateRealmRequest` | `metadata` | `realm-api.yaml` | Y | Client-only metadata. No Bannou plugin reads specific keys from this field by... |

#### Relationship

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `CreateRelationshipRequest` | `metadata` | `relationship-api.yaml` | Y | Client-provided relationship data. No Bannou plugin reads specific keys from ... |
| `CreateRelationshipTypeRequest` | `metadata` | `relationship-api.yaml` | Y | Client-provided relationship type metadata. No Bannou plugin reads specific k... |
| `RelationshipResponse` | `metadata` | `relationship-api.yaml` | Y | Client-provided relationship data. No Bannou plugin reads specific keys from ... |
| `RelationshipTypeResponse` | `metadata` | `relationship-api.yaml` | Y | Client-provided relationship type metadata. No Bannou plugin reads specific k... |
| `SeedRelationshipType` | `metadata` | `relationship-api.yaml` | Y | Client-provided relationship type metadata. No Bannou plugin reads specific k... |
| `UpdateRelationshipRequest` | `metadata` | `relationship-api.yaml` | Y | Updated client-provided relationship data. No Bannou plugin reads specific ke... |
| `UpdateRelationshipTypeRequest` | `metadata` | `relationship-api.yaml` | Y | Updated client-provided relationship type metadata. No Bannou plugin reads sp... |

#### Seed

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `CreateSeedRequest` | `metadata` | `seed-api.yaml` | Y | Client-provided seed-type-specific initial data. No Bannou plugin reads speci... |
| `UpdateSeedRequest` | `metadata` | `seed-api.yaml` | Y | Client-provided metadata fields to merge (set key to null to delete). No Bann... |

#### Species

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `CreateSpeciesRequest` | `metadata` | `species-api.yaml` | Y | Client-only metadata. No Bannou plugin reads specific keys from this field by... |
| `CreateSpeciesRequest` | `traitModifiers` | `species-api.yaml` | Y | Client-only trait modifiers. No Bannou plugin reads specific keys from this f... |
| `SeedSpecies` | `metadata` | `species-api.yaml` | Y | Client-only metadata. No Bannou plugin reads specific keys from this field by... |
| `SeedSpecies` | `traitModifiers` | `species-api.yaml` | Y | Client-only trait modifiers. No Bannou plugin reads specific keys from this f... |
| `SpeciesResponse` | `metadata` | `species-api.yaml` | Y | Client-only metadata. No Bannou plugin reads specific keys from this field by... |
| `SpeciesResponse` | `traitModifiers` | `species-api.yaml` | Y | Client-only trait modifiers. No Bannou plugin reads specific keys from this f... |
| `UpdateSpeciesRequest` | `metadata` | `species-api.yaml` | Y | Client-only metadata. No Bannou plugin reads specific keys from this field by... |
| `UpdateSpeciesRequest` | `traitModifiers` | `species-api.yaml` | Y | Client-only trait modifiers. No Bannou plugin reads specific keys from this f... |

### App Features (L3)

#### Asset

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `CreateBundleRequest` | `metadata` | `asset-api.yaml` | Y | Custom metadata for the bundle (null if none). No Bannou plugin reads specifi... |
| `CreateMetabundleRequest` | `metadata` | `asset-api.yaml` | Y | Custom metadata for the metabundle. No Bannou plugin reads specific keys from... |

#### Documentation

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `CreateDocumentRequest` | `metadata` | `documentation-api.yaml` | Y | Client-provided custom metadata. No Bannou plugin reads specific keys from th... |
| `Document` | `metadata` | `documentation-api.yaml` | Y | Client-provided custom metadata. No Bannou plugin reads specific keys from th... |
| `ImportDocument` | `metadata` | `documentation-api.yaml` | Y | Client-provided custom metadata. No Bannou plugin reads specific keys from th... |
| `UpdateDocumentRequest` | `metadata` | `documentation-api.yaml` | Y | Updated client-provided custom metadata (null to keep unchanged). No Bannou p... |

#### Orchestrator

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `AcquireProcessorRequest` | `metadata` | `orchestrator-api.yaml` | Y | Client-provided processing job metadata. No Bannou plugin reads specific keys... |
| `ComponentHealth` | `metrics` | `orchestrator-api.yaml` | Y | Component-specific metrics reported by the service. No Bannou plugin reads sp... |
| `ReleaseProcessorRequest` | `metrics` | `orchestrator-api.yaml` | Y | Client-provided processing metrics (duration, items processed, etc.). No Bann... |
| `ServiceHealthStatus` | `metadata` | `orchestrator-api.yaml` | Y | Client-provided service metadata. No Bannou plugin reads specific keys from t... |

#### Website

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `Analytics` | `otherTrackers` | `website-api.yaml` | Y | Configuration for other analytics trackers. No Bannou plugin reads specific k... |
| `DownloadInfo` | `minimumRequirements` | `website-api.yaml` | Y | Minimum system requirements for the client. No Bannou plugin reads specific k... |
| `PageContent` | `metadata` | `website-api.yaml` | Y | Custom metadata for the page. No Bannou plugin reads specific keys from this ... |

### Game Features (L4)

#### Achievement

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `AchievementDefinitionResponse` | `metadata` | `achievement-api.yaml` | **N** | Additional metadata |
| `CreateAchievementDefinitionRequest` | `metadata` | `achievement-api.yaml` | **N** | Additional achievement-specific metadata |

#### Analytics

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `IngestEventRequest` | `metadata` | `analytics-api.yaml` | **N** | Additional event-specific data |

#### Behavior

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `CharacterContext` | `worldState` | `behavior-api.yaml` | **N** | Relevant world state information |
| `GoapPlanRequest` | `worldState` | `behavior-api.yaml` | **N** | Current world state as key-value pairs |
| `ValidateGoapPlanRequest` | `worldState` | `behavior-api.yaml` | **N** | Current world state |

#### Character Encounter

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `EncounterModel` | `metadata` | `character-encounter-api.yaml` | Y | Client-provided encounter-specific data. No Bannou plugin reads specific keys... |
| `RecordEncounterRequest` | `metadata` | `character-encounter-api.yaml` | Y | Client-provided encounter-specific data. No Bannou plugin reads specific keys... |

#### Character History

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `HistoricalParticipation` | `metadata` | `character-history-api.yaml` | Y | Client-provided event-specific details. No Bannou plugin reads specific keys ... |
| `RecordParticipationRequest` | `metadata` | `character-history-api.yaml` | Y | Client-provided event-specific details. No Bannou plugin reads specific keys ... |

#### Character Personality

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `EvolveCombatRequest` | `contextData` | `character-personality-api.yaml` | Y | Optional context for logging and debugging (e.g., enemy type, ally count, loc... |
| `RecordExperienceRequest` | `contextData` | `character-personality-api.yaml` | Y | Optional context for logging and debugging. Not used in evolution calculation... |

#### Escrow

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `CreateEscrowRequest` | `metadata` | `escrow-api.yaml` | Y | Client-provided application-specific metadata. No Bannou plugin reads specifi... |
| `EscrowAgreement` | `metadata` | `escrow-api.yaml` | Y | Client-provided application-specific metadata. No Bannou plugin reads specifi... |
| `EscrowAsset` | `customAssetData` | `escrow-api.yaml` | Y | Custom asset handler-specific data. No Bannou plugin reads specific keys from... |
| `EscrowAssetInput` | `customAssetData` | `escrow-api.yaml` | Y | Custom asset handler-specific data. No Bannou plugin reads specific keys from... |
| `ValidationFailure` | `details` | `escrow-api.yaml` | Y | Validation failure diagnostic details. No Bannou plugin reads specific keys f... |
| `VerifyConditionRequest` | `verificationData` | `escrow-api.yaml` | Y | Caller-provided proof/evidence data for condition verification. No Bannou plu... |
| `ReleaseAllocationWithConfirmation` | `confirmationShortcut` | `escrow-events.yaml` | Y | Prebound API shortcut for client confirmation (pushed via WebSocket). No Bann... |
| `ValidationFailureInfo` | `details` | `escrow-events.yaml` | Y | Additional failure details. No Bannou plugin reads specific keys from this fi... |

#### Leaderboard

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `CreateLeaderboardDefinitionRequest` | `metadata` | `leaderboard-api.yaml` | Y | Client-provided leaderboard-specific metadata. No Bannou plugin reads specifi... |
| `LeaderboardDefinitionResponse` | `metadata` | `leaderboard-api.yaml` | Y | Client-provided leaderboard-specific metadata. No Bannou plugin reads specifi... |
| `LeaderboardEntry` | `metadata` | `leaderboard-api.yaml` | Y | Client-provided entry metadata. No Bannou plugin reads specific keys from thi... |
| `SubmitScoreRequest` | `metadata` | `leaderboard-api.yaml` | Y | Client-provided score metadata (e.g., how the score was achieved). No Bannou ... |

#### License

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `AddLicenseDefinitionRequest` | `metadata` | `license-api.yaml` | Y | Game-specific metadata for this license node. No Bannou plugin reads specific... |
| `BoardNodeState` | `metadata` | `license-api.yaml` | Y | Game-specific metadata for this license node. No Bannou plugin reads specific... |
| `LicenseDefinitionResponse` | `metadata` | `license-api.yaml` | Y | Game-specific metadata for this license node. No Bannou plugin reads specific... |
| `UpdateLicenseDefinitionRequest` | `metadata` | `license-api.yaml` | Y | Updated game-specific metadata. No Bannou plugin reads specific keys from thi... |

#### Mapping

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `AffordanceLocation` | `features` | `mapping-api.yaml` | Y | Game-specific features of this affordance result. No Bannou plugin reads spec... |
| `CreateDefinitionRequest` | `metadata` | `mapping-api.yaml` | Y | Client-provided definition metadata. No Bannou plugin reads specific keys fro... |
| `CustomAffordance` | `excludes` | `mapping-api.yaml` | Y | Game-specific exclusion criteria for affordance matching. No Bannou plugin re... |
| `CustomAffordance` | `prefers` | `mapping-api.yaml` | Y | Game-specific preferred criteria (boost score but not required). No Bannou pl... |
| `CustomAffordance` | `requires` | `mapping-api.yaml` | Y | Game-specific required criteria for affordance matching. No Bannou plugin rea... |
| `MapDefinition` | `metadata` | `mapping-api.yaml` | Y | Client-provided definition metadata. No Bannou plugin reads specific keys fro... |
| `MapObject` | `data` | `mapping-api.yaml` | Y | Game-specific spatial object data. No Bannou plugin reads specific keys from ... |
| `MapPayload` | `data` | `mapping-api.yaml` | Y | Game-specific spatial object data. lib-mapping stores and returns this as-is.... |
| `ObjectChange` | `data` | `mapping-api.yaml` | Y | Game-specific object state data. No Bannou plugin reads specific keys from th... |
| `UpdateDefinitionRequest` | `metadata` | `mapping-api.yaml` | Y | Updated client-provided definition metadata (replaces existing). No Bannou pl... |
| `EventMapObject` | `data` | `mapping-events.yaml` | **N** | Schema-less object data (publisher-defined) |
| `IngestPayload` | `data` | `mapping-events.yaml` | **N** | Schema-less object data (publisher-defined) |
| `MapUpdatedEvent` | `payload` | `mapping-events.yaml` | **N** | Schema-less payload data |
| `ObjectChangeEvent` | `data` | `mapping-events.yaml` | **N** | Object data (for created/updated) |

#### Realm History

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `RealmHistoricalParticipation` | `metadata` | `realm-history-api.yaml` | Y | Client-provided event-specific details. No Bannou plugin reads specific keys ... |
| `RecordRealmParticipationRequest` | `metadata` | `realm-history-api.yaml` | Y | Client-provided event-specific details. No Bannou plugin reads specific keys ... |

#### Scene

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `Affordance` | `parameters` | `scene-api.yaml` | Y | Game-specific affordance parameters interpreted by game engines and AI system... |
| `DestroyInstanceRequest` | `metadata` | `scene-api.yaml` | Y | Caller-provided metadata passed through to destruction events. No Bannou plug... |
| `InstantiateSceneRequest` | `metadata` | `scene-api.yaml` | Y | Caller-provided metadata passed through to instantiation events. No Bannou pl... |
| `Scene` | `metadata` | `scene-api.yaml` | Y | Client-only scene metadata (author, thumbnail, editor preferences). No Bannou... |
| `SceneNode` | `annotations` | `scene-api.yaml` | Y | Client-only node annotations for game engines and editors. No Bannou plugin r... |
| `ValidationError` | `context` | `scene-api.yaml` | Y | Client-only validation error context. No Bannou plugin reads specific keys fr... |
| `SceneDestroyedEvent` | `metadata` | `scene-events.yaml` | Y | Caller-provided metadata passed through from destruction request. No Bannou p... |
| `SceneInstantiatedEvent` | `metadata` | `scene-events.yaml` | Y | Caller-provided metadata passed through from instantiation request. No Bannou... |

#### Status

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `GrantStatusRequest` | `metadata` | `status-api.yaml` | **N** | Arbitrary key-value data passed to contract template values and stored on the... |
| `StatusInstanceResponse` | `metadata` | `status-api.yaml` | **N** | Arbitrary metadata associated with this status instance |

### Common / Shared Schemas

#### Asset Client

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `AssetReadyEvent` | `metadata` | `asset-client-events.yaml` | Y | Asset metadata. No Bannou plugin reads specific keys from this field by conve... |

#### Common

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `GoalState` | `goalParameters` | `common-events.yaml` | **N** | Parameters for the primary goal (e.g., target entity, location) |
| `MemoryUpdate` | `memoryValue` | `common-events.yaml` | **N** | Memory value (entity ID, context, intensity, etc.) |
| `ServiceErrorEvent` | `details` | `common-events.yaml` | **N** | Redacted structured context (exclude PII/secrets) |
| `ServiceHeartbeatEvent` | `metadata` | `common-events.yaml` | Y | Additional instance-level metadata. No Bannou plugin reads specific keys from... |
| `ServiceStatus` | `metadata` | `common-events.yaml` | Y | Service-specific metadata from OnHeartbeat callback. No Bannou plugin reads s... |
| `SessionConnectedEvent` | `clientInfo` | `common-events.yaml` | Y | Optional client metadata (version, platform, etc.). No Bannou plugin reads sp... |
| `SessionReconnectedEvent` | `reconnectionContext` | `common-events.yaml` | Y | Optional context from the reconnection (client info, etc.). No Bannou plugin ... |

#### Common Client

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `SystemErrorEvent` | `details` | `common-client-events.yaml` | Y | Additional error details (service-specific). No Bannou plugin reads specific ... |

#### Game Session Client

| Schema Type | Property | Schema File | Compliant | Description |
|-------------|----------|-------------|-----------|-------------|
| `GameActionResultEvent` | `resultData` | `game-session-client-events.yaml` | Y | Action-specific result data. No Bannou plugin reads specific keys from this f... |
| `GameStateUpdatedEvent` | `stateDelta` | `game-session-client-events.yaml` | Y | Partial game state changes. No Bannou plugin reads specific keys from this fi... |
| `PlayerInfo` | `characterData` | `game-session-client-events.yaml` | Y | Game-specific character data. No Bannou plugin reads specific keys from this ... |
| `VisibleEffect` | `effectData` | `game-session-client-events.yaml` | Y | Effect-specific parameters. No Bannou plugin reads specific keys from this fi... |

## Structural Exceptions

These properties use `additionalProperties: true` for structural reasons that are
fundamentally different from metadata bags. They are **not subject to T29** because
they are not data contracts between services -- they are the infrastructure primitives
that other services build on, or they represent genuinely polymorphic payloads whose
shape is defined by authored content rather than service convention.

### Infrastructure Primitives

These exist because the service's entire purpose is storing/forwarding arbitrary data:

| Service | Property | Why Exempt |
|---------|----------|-----------|
| **State** (L0) | `SaveStateRequest.value`, `GetStateResponse.value`, `BulkSaveItem.value`, `BulkStateItem.value` | Key-value store. The entire purpose is to persist any JSON value. Not a metadata bag -- it IS the storage primitive. |
| **Messaging** (L0) | `PublishEventRequest.payload` | Generic pub/sub bus. Must accept any event shape for forwarding. Wrapped opaquely in `GenericMessageEnvelope`. |
| **Connect** (L1) | `InternalProxyRequest.body` | HTTP proxy forwarding body. Must accept any shape because it forwards arbitrary service request payloads. |

### Polymorphic Response Data

These vary by a typed discriminator field, not by cross-service convention:

| Service | Property | Why Exempt |
|---------|----------|-----------|
| **Connect** (L1) | `GetEndpointMetaResponse.data` | Structure varies by `metaType` discriminator. Each meta type has a known shape. |
| **Common** | `ServiceErrorEvent.details` | Diagnostic context that varies per error type. No service reads other services' error details by key. |

### Behavior-Authored Content

These are free-form because the keys are defined by ABML behavior documents (authored
YAML content), not by any service schema. Different NPCs have different keys:

| Service | Property | Why Exempt |
|---------|----------|-----------|
| **Behavior** (L4) | `GoapPlanRequest.worldState`, `ValidateGoapPlanRequest.worldState`, `CharacterContext.worldState` | GOAP world state keys (e.g., `hunger`, `gold`, `in_combat`) are defined per-NPC by behavior documents. The planner iterates them generically. |
| **Common** | `GoalState.goalParameters`, `MemoryUpdate.memoryValue` | Actor internal state populated by behavior execution. Keys are behavior-authored. |

## Non-Compliant Properties

These properties are missing the compliance marker text in their description.
Each needs investigation: is it a legitimate metadata bag missing the marker,
or is it being misused as a cross-service data contract?

| Service | Schema Type | Property | Schema File | Description |
|---------|-------------|----------|-------------|-------------|
| Achievement | `CreateAchievementDefinitionRequest` | `metadata` | `achievement-api.yaml` | Additional achievement-specific metadata |
| Achievement | `AchievementDefinitionResponse` | `metadata` | `achievement-api.yaml` | Additional metadata |
| Actor | `CreateActorTemplateRequest` | `cognitionOverrides` | `actor-api.yaml` | Static template-level cognition overrides (polymorphic JSON). Deserialized in... |
| Actor | `ActorTemplateResponse` | `cognitionOverrides` | `actor-api.yaml` | Static template-level cognition overrides (polymorphic JSON). Deserialized in... |
| Actor | `UpdateActorTemplateRequest` | `cognitionOverrides` | `actor-api.yaml` | Updated cognition overrides (polymorphic JSON). Deserialized internally to Co... |
| Analytics | `IngestEventRequest` | `metadata` | `analytics-api.yaml` | Additional event-specific data |
| Behavior | `CharacterContext` | `worldState` | `behavior-api.yaml` | Relevant world state information |
| Behavior | `GoapPlanRequest` | `worldState` | `behavior-api.yaml` | Current world state as key-value pairs |
| Behavior | `ValidateGoapPlanRequest` | `worldState` | `behavior-api.yaml` | Current world state |
| Common | `ServiceErrorEvent` | `details` | `common-events.yaml` | Redacted structured context (exclude PII/secrets) |
| Common | `GoalState` | `goalParameters` | `common-events.yaml` | Parameters for the primary goal (e.g., target entity, location) |
| Common | `MemoryUpdate` | `memoryValue` | `common-events.yaml` | Memory value (entity ID, context, intensity, etc.) |
| Connect | `InternalProxyRequest` | `body` | `connect-api.yaml` | Request body to forward to target service (null for no body). Uses additional... |
| Connect | `GetEndpointMetaResponse` | `data` | `connect-api.yaml` | Metadata payload whose structure varies by metaType (endpoint-info returns su... |
| Mapping | `EventMapObject` | `data` | `mapping-events.yaml` | Schema-less object data (publisher-defined) |
| Mapping | `IngestPayload` | `data` | `mapping-events.yaml` | Schema-less object data (publisher-defined) |
| Mapping | `ObjectChangeEvent` | `data` | `mapping-events.yaml` | Object data (for created/updated) |
| Mapping | `MapUpdatedEvent` | `payload` | `mapping-events.yaml` | Schema-less payload data |
| State | `GetStateResponse` | `value` | `state-api.yaml` | The stored value (null if not found) |
| State | `SaveStateRequest` | `value` | `state-api.yaml` | Value to store |
| State | `BulkStateItem` | `value` | `state-api.yaml` | The value (null if not found) |
| State | `BulkSaveItem` | `value` | `state-api.yaml` | The value to store |
| Status | `GrantStatusRequest` | `metadata` | `status-api.yaml` | Arbitrary key-value data passed to contract template values and stored on the... |
| Status | `StatusInstanceResponse` | `metadata` | `status-api.yaml` | Arbitrary metadata associated with this status instance |

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for T29 (No Metadata Bag Contracts).*
