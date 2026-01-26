# Deep Dive Cleanup Checklist

## ⛔ CRITICAL: UNDERSTAND SEMANTIC INTENT BEFORE CHANGING TYPES

**Before changing ANY field from `string` to `Guid` or enum, you MUST determine the semantic intent:**

1. **Who provides this value?**
   - If a HUMAN provides it → should be human-readable `string`
   - If a SYSTEM generates it → could be `Guid` or opaque identifier

2. **How is it used?**
   - If users query/filter/categorize by it → should be human-readable `string`
   - If it's just a unique key with no semantic meaning → `Guid` is fine

3. **Check the SDKs and documentation FIRST**
   - The SDK examples show how developers are EXPECTED to use the API
   - If SDKs show `"my-bundle-v1"` or `"synty/polygon-adventure"`, it's a human-readable string
   - Don't assume something is a Guid just because it's an "ID" field

**lib-asset was an example of getting this WRONG:**
- BundleId was changed from `string` to `Guid` without understanding the design
- The SDK documentation clearly shows BundleId should be human-readable: `"synty/polygon-adventure"`, `"game-assets-v1"`
- This change now needs to be REVERTED

---

## T25 Fix Guidelines (Once Semantic Intent Is Confirmed)

When fixing T25 type safety issues where a field IS legitimately a Guid or enum:

**DO NOT** make partial fixes - change everything end-to-end:
1. Change the POCO class fields to proper types (Guid, enum)
2. Change ALL internal data structures (dictionaries, tuples, local vars)
3. Change API request/response schemas
4. Result: Zero `.ToString()` or `Parse()` conversions anywhere

**Signs something is wrong:**
- Adding `.ToString()` anywhere → fix the destination type instead
- Adding `Guid.Parse()` anywhere → fix the source type instead
- Adding `Enum.Parse()` anywhere → fix the source type instead
- OR you misidentified the semantic intent and it should stay a string

---

## Cleanup Items

### lib-asset

#### ⚠️ CRITICAL SEMANTIC CLARIFICATION: AssetId and BundleId are BOTH strings

**The rule**: If a human provides it, it should be human-readable. If the system generates it, it's opaque.

**AssetId - STRING (server-generated content hash)**:
- **Semantic origin**: SERVER generates from uploaded file content
- **Format**: `{type-prefix}-{hash-prefix}` (e.g., `image-a1b2c3d4e5f6`)
- **Generation**: `AssetService.GenerateAssetId()` computes from SHA256 content hash
- **Why string**: Content-addressable identifier - same bytes = same ID
- **Type**: `string` is CORRECT

**BundleId - STRING (human-provided identifier)**:
- **Semantic origin**: HUMAN/DEVELOPER provides when creating bundles
- **Format**: Human-readable names like `"synty/polygon-adventure"`, `"my-bundle-v1"`, `"game-assets-v1"`
- **Evidence from SDKs**:
  - `asset-bundler/README.md`: `sourceId: "synty/polygon-adventure"`, `.WithId("game-assets-v1")`
  - `bundle-format/README.md`: `bundleId: "my-bundle-v1"`
  - `asset-loader/README.md`: `source.RegisterBundle("my-bundle", ...)`
- **Why string**: Developers need to categorize, query, and retrieve bundles by meaningful names
- **Type**: `string` is CORRECT

**Summary**:
- AssetId = content hash → `string` ✓
- BundleId = human-readable identifier → `string` ✓
- **NEITHER should be Guid**

#### Status: ✅ COMPLETE

- [x] **REVERTED BundleId to string**: Fixed. BundleId/MetabundleId are human-readable identifiers (`"synty/polygon-adventure"`, `"my-bundle-v1"`), not UUIDs.

  **Changes made**:
  - Schemas: Removed `format: uuid` from bundleId/metabundleId in `asset-api.yaml`, `asset-events.yaml`, `asset-client-events.yaml`
  - SDK bundle-format: `BundleManifest.BundleId`, `BannouBundleWriter.Finalize()`, `BundleValidator.ValidateAsync()` all use `string`
  - lib-asset models: `BundleMetadata.BundleId`, `StoredSourceBundleReference.BundleId`, all internal dictionaries/tuples use `string`
  - lib-asset service: Fixed all `== Guid.Empty` checks to `string.IsNullOrWhiteSpace()`
  - lib-asset event emitters: All bundleId/metabundleId parameters use `string`
  - lib-asset.tests: Updated all test fixtures to use string bundle IDs
  - **Note**: `lib-escrow/EscrowAssetBundleModel.BundleId` remains `Guid` - this is a DIFFERENT concept (internal escrow grouping, system-generated)

- [x] **T25**: `AssetProcessingResult.ErrorCode` and `AssetValidationResult.ErrorCode` use string constants. **CLARIFIED - NOT A T25 ISSUE**: These are internal processor implementation classes, not API types. API-facing types already use proper enums: `UploadErrorCode`, `ProcessingErrorCode`, `MetabundleErrorCode` (all defined in `asset-client-events.yaml` schema). The internal processing error codes are deliberately freeform strings because processors can return arbitrary implementation-specific error messages.

### lib-auth

- [x] **T25**: `SessionDataModel.SessionId` stores a GUID as string with `= string.Empty` default. **ALREADY FIXED** - `SessionDataModel.SessionId` is now `Guid SessionId { get; set; }`. The remaining `Guid.Parse(sessionKey)` at storage boundaries (e.g., TokenService.cs:300) is correct - Redis keys must be strings, and the parse converts back to Guid for API responses.

### lib-character-encounter

- [x] **T25**: Multiple internal models store enums as strings requiring `Enum.Parse`: `EncounterData.Outcome`, `PerspectiveData.EmotionalImpact`, `EncounterTypeData.DefaultEmotionalImpact`. **ALREADY FIXED** - All POCOs now use proper enum types: `EncounterOutcome`, `EmotionalImpact`, etc. No `Enum.Parse` calls remain.

- [ ] **T21/T25**: `MemoryDecayMode` configuration property is string representing discrete values ("lazy", "scheduled"). **Decision**: Define `MemoryDecayMode` enum in schema.

### lib-character-history

- [x] **T25**: Multiple internal POCOs store enums as strings requiring `Enum.Parse`: `EventParticipationData.EventCategory`, `EventParticipationData.Role`, `BackstoryElementData.ElementType`. **ALREADY FIXED** - All POCOs now use proper enum types: `EventCategory`, `ParticipationRole`, `BackstoryElementType`. No `Enum.Parse` calls remain in business logic.

- [x] **T25**: Multiple internal POCOs store GUIDs as strings: `ParticipationData.ParticipationId`, `ParticipationData.CharacterId`, `ParticipationData.EventId`, `BackstoryData.CharacterId`. **ALREADY FIXED** - All fields now use `Guid` type. Storage boundary conversions (`Guid.Parse`/`.ToString()`) for state store key access are acceptable per IMPLEMENTATION TENETS.

### lib-character-personality

- [x] **T25**: `CombatPreferencesModel` stores enums as strings: `Style`, `PreferredRange`, `GroupRole`. **ALREADY FIXED** - `CombatPreferencesData` uses enum types directly: `CombatStyle`, `PreferredRange`, `GroupRole`.

- [x] **T25**: `PersonalityData.Traits` uses `Dictionary<string, float>` with string keys for trait axes. **ALREADY FIXED** - Now uses `Dictionary<TraitAxis, float>` with enum keys.

- [x] **T25**: Both `CombatPreferencesData.CharacterId` and `PersonalityData.CharacterId` use `string` instead of `Guid`. **ALREADY FIXED** - Both use `Guid` type.

### lib-contract

- [x] **T21/T25**: `DefaultEnforcementMode` config uses string requiring runtime parsing. **FIXED** - Changed `schemas/contract-configuration.yaml` to use `$ref: 'contract-api.yaml#/components/schemas/EnforcementMode'`. Updated `generate-config.sh` to support `$ref` for enum types. Added `postprocess_enum_pascalcase()` to `common.sh` to convert NSwag's underscore-style enum names (`Event_only`) to proper PascalCase (`EventOnly`). Fixed all service code to use PascalCase enum values. Removed dead `ParseEnforcementMode` method.

- [x] **T25**: `PartyModel.Role`, `MilestoneModel.Role`, `AssetReferenceModel.AssetType` store enums as strings. **CLARIFIED - NOT A T25 ISSUE**:
  - `ContractPartyModel.Role` is intentionally `string` - roles are human-defined per contract template (e.g., "employer", "employee", "buyer", "seller"). Schema explicitly defines `role: type: string`. This is correct semantic intent.
  - `MilestoneInstanceModel` has no `Role` field - it has `Code`, `Name`, `Status` (which IS an enum: `MilestoneStatus`)
  - `AssetReferenceModel` does not exist in the codebase - likely was removed or never implemented

### lib-currency

- [x] **T25**: Multiple internal models store enums as strings requiring `Enum.TryParse`: `CurrencyDefinitionModel.Scope`, `CurrencyDefinitionModel.Precision`, `CurrencyDefinitionModel.CapOverflowBehavior`, `CurrencyDefinitionModel.AutogainMode`, `CurrencyDefinitionModel.ExpirationPolicy`, `CurrencyDefinitionModel.LinkageMode`, `WalletModel.OwnerType`, `TransactionRecordModel.TransactionType`. **ALREADY FIXED** - All POCOs use proper enum types: `CurrencyScope`, `CurrencyPrecision`, `CapOverflowBehavior?`, `AutogainMode?`, `ExpirationPolicy?`, `WalletOwnerType`, `TransactionType`. Note: `LinkageMode` is not used in the current POCOs.

- [x] **T25**: Internal models use `string` for GUID fields: `CurrencyDefinitionModel.DefinitionId`, `WalletModel.WalletId`, `BalanceModel` ID fields, `TransactionRecordModel.TransactionId`, `HoldModel.HoldId`, etc. **ALREADY FIXED** - All POCOs use `Guid` type for ID fields. The `Guid.Parse()` calls at internal helper method boundaries (e.g., `GetOrCreateBalanceAsync`) are for key construction, which is an acceptable storage boundary pattern.

### lib-escrow

- [x] **T25**: `ValidationFailure.assetType` (NOT `AssetFailureData`) uses `type: string` in schema instead of `$ref: '#/components/schemas/AssetType'`. **FIXED** - Changed `schemas/escrow-api.yaml` to use `$ref: '#/components/schemas/AssetType'`. Service code updates pending (will be exposed by build errors when working on this plugin).

### lib-game-session

- [ ] **T25**: `GameSessionModel.SessionId` and `CleanupSessionModel.SessionId` are `string` instead of `Guid`. **Decision**: Change to `Guid` type.

- [ ] **T25**: `HandleSubscriptionUpdatedInternalAsync` compares string literals for subscription actions instead of using typed enum. **Decision**: Accept typed enum parameter and use enum equality.

### lib-inventory

- [ ] **T21/T25**: `DefaultWeightContribution` configuration property is `string` type but represents `WeightContribution` enum. Service parses with `Enum.TryParse`. **Decision**: Define as enum in configuration schema.

### lib-item

- [ ] **T21/T25**: Three configuration properties are `string` type but represent enums: `DefaultWeightPrecision` → `WeightPrecision`, `DefaultRarity` → `ItemRarity`, `DefaultSoulboundType` → `SoulboundType`. **Decision**: Define as enums in configuration schema.

- [x] **T25**: Both `ItemTemplateModel` and `ItemInstanceModel` store GUID fields as `string` types, requiring `Guid.Parse()` in mappings. **FIXED**: Changed `ItemTemplateModel` fields (`TemplateId`, `MigrationTargetId`, `AvailableRealms`) and `ItemInstanceModel` fields (`InstanceId`, `TemplateId`, `ContainerId`, `RealmId`, `BoundToId`, `OriginId`) to `Guid`/`Guid?`/`List<Guid>?` types. Updated all mapping methods, event publishing, and tests to use direct Guid assignments.

### lib-location

- [x] **T25**: `LocationModel.LocationType` is stored as string requiring `Enum.Parse<LocationType>()`. **ALREADY FIXED** - `LocationModel.LocationType` uses `LocationType` enum directly. All ID fields (`LocationId`, `RealmId`, `ParentLocationId`) are `Guid`.

### lib-permission

- [x] **T25**: `registrationInfo` uses anonymous type which cannot be reliably deserialized. **Decision**: Define a typed `ServiceRegistrationInfo` POCO class. **FIXED** - Created `ServiceRegistrationInfo` internal class with `ServiceId`, `Version`, and `RegisteredAtUnix` properties. Updated write path to use typed store `GetStore<ServiceRegistrationInfo>()`. Simplified read path in `ListRegisteredServicesAsync` to directly access typed properties instead of manual dictionary parsing with JsonElement handling.

### lib-realm

- [x] **T25**: `RealmModel.RealmId` stored as string instead of Guid. **ALREADY FIXED** - `RealmModel.RealmId` is `Guid`. All ID fields are `Guid` type.

### lib-realm-history

- [x] **T25**: Multiple internal models store enums as strings requiring `Enum.TryParse`: `RealmEventDataModel.EventCategory`, `RealmEventDataModel.Role`, `RealmLoreDataModel.ElementType`. **ALREADY FIXED** - All POCOs use proper enum types: `RealmParticipationData.EventCategory` is `RealmEventCategory`, `Role` is `RealmEventRole`, `RealmLoreElementData.ElementType` is `RealmLoreElementType`.

- [x] **T25**: Multiple internal models store GUIDs as strings: `ParticipationId`, `RealmId`, `EventId`. **ALREADY FIXED** - All POCOs use `Guid` type: `RealmParticipationData.ParticipationId`, `RealmId`, `EventId`, `RealmLoreData.RealmId`, `RealmLoreElementData.RelatedEntityId`.

### lib-relationship

- [x] **T25**: `RelationshipModel` stores entity types as strings requiring `Enum.Parse`: `Entity1Type`, `Entity2Type`. **Decision**: Change POCOs to use `EntityType` enum directly. **ALREADY FIXED** - Internal `RelationshipModel` already uses `EntityType` enum directly for `Entity1Type` and `Entity2Type`. No `Enum.Parse` calls exist in the service.

- [x] **T25**: `RelationshipModel` stores GUIDs as strings: `RelationshipId`, `Entity1Id`, `Entity2Id`, `RelationshipTypeId`. **Decision**: Change to `Guid` type. **ALREADY FIXED** - Internal `RelationshipModel` already uses `Guid` type for all ID fields. Index stores use `List<Guid>`. Only boundary storage uses `.ToString()` for composite uniqueness key (same pattern as other services where state store constraint applies).

### lib-relationship-type

- [x] **T25**: `RelationshipTypeModel` stores GUIDs as strings: `RelationshipTypeId`, `ParentTypeId`, `InverseTypeId`. **Decision**: Change to `Guid` type. **FIXED** - Changed `RelationshipTypeModel.RelationshipTypeId` to `Guid`, `ParentTypeId` to `Guid?`, `InverseTypeId` to `Guid?`. Updated all helper methods (`BuildTypeKey`, `BuildParentIndexKey`, `AddToParentIndexAsync`, `RemoveFromParentIndexAsync`, `GetChildTypeIdsAsync`, `AddToAllTypesListAsync`, `RemoveFromAllTypesListAsync`) to use `Guid` parameters. Changed index stores from `List<string>` to `List<Guid>`. Removed all `Guid.Parse()` and `.ToString()` conversions throughout. Note: Code index (code→ID reverse lookup) uses string storage due to state store reference type constraint (`GetStore<TValue>` requires `TValue : class`), with `Guid.TryParse` at the boundary.

### lib-save-load

- [ ] **T21/T25**: `DefaultCompressionType` and `DefaultDeltaAlgorithm` configuration properties are `string` but represent enums. **Decision**: Define as enums in configuration schema. **NOTE**: Requires schema change in `schemas/save-load-configuration.yaml`, not service implementation change.

- [x] **T25**: Internal models now use proper enum types. **ALREADY FIXED** - `PendingUploadEntry.CompressionType`, `SaveVersionManifest.CompressionType`, `SaveSlotMetadata.CompressionType/OwnerType/Category` all use proper enum types. Note: `ExportManifest.OwnerType` intentionally uses `string` as this is a portable export format for ZIP archives - strings provide better cross-version compatibility and human readability.

### lib-scene

- [x] **T25**: `SceneIndexEntry.SceneType` is stored as string requiring `Enum.TryParse<SceneType>()`. **Decision**: Change POCO to use `SceneType` enum directly. **FIXED** - Changed POCO and all related code to use enum directly, including event publishing and CreateSceneSummary.

- [x] **T25**: Multiple internal models store GUIDs as strings: `SceneIndexEntry.SceneId`, `SceneIndexEntry.AssetId`, `CheckoutState.SceneId`, `SceneContentEntry.SceneId`. **Decision**: Change to `Guid` type. **FIXED** - Changed all to Guid, including HashSet index stores, extraction functions (ExtractSceneReferences, ExtractAssetReferences), GetReferenceSceneId, and reference resolution (ResolveReferencesAsync). Note: `CheckoutState.EditorId` and `SceneIndexEntry.CheckedOutBy` intentionally remain `string` because editorId can hold either accountId (Guid) OR app-id (string like "bannou") - this is correct semantic intent.

### lib-species

- [x] **T25**: `SpeciesModel` uses `string` for `SpeciesId` and `List<string>` for `RealmIds` instead of proper GUID types. **Decision**: Change to `Guid` and `List<Guid>`. **FIXED** - Changed `SpeciesModel.SpeciesId` to `Guid`, `SpeciesModel.RealmIds` to `List<Guid>`. Updated all helper methods (`AddToRealmIndexAsync`, `RemoveFromRealmIndexAsync`, `LoadSpeciesByIdsAsync`, `BuildSpeciesKey`, `BuildRealmIndexKey`) to use `Guid` parameters. Removed `Guid.Parse()` from all event publishing and mapping methods. Note: Code index (code→ID reverse lookup) uses string storage due to state store reference type constraint, with `Guid.TryParse` at the boundary.

### lib-subscription

- [x] **T25**: `SubscriptionDataModel` stores `SubscriptionId`, `AccountId`, and `ServiceId` as `string` rather than `Guid`. **Decision**: Change to `Guid` type. **FIXED** - Changed internal `SubscriptionDataModel` fields to `Guid`. Updated helper methods, index stores to use `List<Guid>`, removed all `Guid.Parse()` and `.ToString()` conversions. Fixed `SubscriptionExpirationService.cs` to use `Guid` types throughout.

### lib-voice

- [x] **T25**: `VoiceRoomData` stores tier and codec as strings, requiring parsing in service methods. **Decision**: Change to enum types (`VoiceTier`, `VoiceCodec`). **FIXED** - Changed `VoiceRoomData.Tier` to `VoiceTier`, `VoiceRoomData.Codec` to `VoiceCodec`. Updated interface signatures `IP2PCoordinator.BuildP2PConnectionInfoAsync` and `IScaledTierCoordinator.BuildScaledConnectionInfoAsync` to use enum types. Removed all `ParseVoiceTier` and `ParseVoiceCodec` helper methods.

---

## Code Generation Issues

### bannou-service (Program.cs)

- [x] **T25**: `Program.ServiceGUID` is now declared as `Guid` type. All generated `*PermissionRegistration.cs` files and service implementations now use `Program.Service-GUID` directly without `Guid.Parse()`. Generator scripts updated.
