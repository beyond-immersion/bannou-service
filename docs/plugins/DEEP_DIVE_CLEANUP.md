# Deep Dive Cleanup Checklist

## ⛔ WHAT NOT TO DO - FAILED EXAMPLE

When fixing T25 type safety issues, **DO NOT** make a partial fix that only changes the POCO class fields while leaving internal data structures (dictionaries, tuples, local variables) as strings. This creates unnecessary conversions at every usage site.

**Bad approach (what was done wrong in lib-asset):**
1. Changed `BundleMetadata.BundleId` from `string` to `Guid` ✓
2. Left `Dictionary<string, List<string>> provenanceByBundle` with string keys ✗
3. Left tuples like `(StoredBundleAssetEntry, string SourceBundleId)` with string ✗
4. Added `.ToString()` everywhere to convert back to string for dictionary keys ✗
5. Result: 25+ unnecessary conversions, code is harder to read, defeats the purpose

**Correct approach - FIX EVERYTHING:**
1. Change the POCO class fields to proper types (Guid, enum) ✓
2. Change ALL internal data structures (dictionaries, tuples, local vars) to use proper types ✓
3. Change API request/response schemas to use proper types ✓
4. If the schema uses `type: string` for a GUID or enum, **fix the schema** ✓
5. Result: Zero conversions anywhere. Type safety end-to-end.

**The goal is ZERO `.ToString()` and ZERO `Guid.Parse()`/`Enum.Parse()` calls in service code.**

If you find yourself adding conversions, you haven't fixed everything - go back and fix the schema or the model you missed.

**Signs you're doing it wrong:**
- Adding `.ToString()` anywhere (fix the destination type instead)
- Adding `Guid.Parse()` anywhere (fix the source type instead)
- Adding `Enum.Parse()` anywhere (fix the source type instead)
- Any conversion at all means something is still the wrong type

---

## Cleanup Items

### lib-asset

- [ ] **T25**: `BundleMetadata.BundleId`, `SourceBundleReferenceInternal.BundleId`, and related bundle model ID fields store GUIDs as strings requiring `Guid.Parse()` at usage sites. **Decision**: Change internal POCOs to use `Guid` type. **NEEDS REDO** - partial fix applied incorrectly, left internal dictionaries/tuples as strings. Must also fix: `provenanceByBundle`, `assetsByPlatformId`, `assetsByHash`, `assetsBySourceBundle`, `assetsToInclude` tuple types.

- [ ] **T25**: `AssetProcessingResult.ErrorCode` and `AssetValidationResult.ErrorCode` use string constants (`"UNSUPPORTED_CONTENT_TYPE"`, `"FILE_TOO_LARGE"`, etc.). **Decision**: Define an `AssetProcessingErrorCode` enum in schema.

### lib-auth

- [x] **T25**: `SessionDataModel.SessionId` stores a GUID as string with `= string.Empty` default. Multiple sites use `Guid.Parse(sessionId)`. **Decision**: Change to `Guid` type. **FIXED** - `SessionDataModel.SessionId` was already `Guid`. Changed `ISessionService` and `ITokenService` method signatures from `string sessionId/accountId` to `Guid`. Conversions occur only at storage boundaries (state store requires reference types). Updated tests.

### lib-character-encounter

- [ ] **T25**: Multiple internal models store enums as strings requiring `Enum.Parse`: `EncounterData.Outcome`, `PerspectiveData.EmotionalImpact`, `EncounterTypeData.DefaultEmotionalImpact`. **Decision**: Change POCOs to use enum types directly.

- [ ] **T21/T25**: `MemoryDecayMode` configuration property is string representing discrete values ("lazy", "scheduled"). **Decision**: Define `MemoryDecayMode` enum in schema.

### lib-character-history

- [ ] **T25**: Multiple internal POCOs store enums as strings requiring `Enum.Parse`: `EventParticipationData.EventCategory`, `EventParticipationData.Role`, `BackstoryElementData.ElementType`. **Decision**: Change POCOs to use enum types directly.

- [ ] **T25**: Multiple internal POCOs store GUIDs as strings: `ParticipationData.ParticipationId`, `ParticipationData.CharacterId`, `ParticipationData.EventId`, `BackstoryData.CharacterId`. **Decision**: Change to `Guid` type.

### lib-character-personality

- [ ] **T25**: `CombatPreferencesModel` stores enums as strings: `Style`, `PreferredRange`, `GroupRole`. **Decision**: Change POCOs to use enum types directly.

- [ ] **T25**: `PersonalityData.Traits` uses `Dictionary<string, float>` with string keys for trait axes. **Decision**: Change to `Dictionary<TraitAxis, float>`.

- [ ] **T25**: Both `CombatPreferencesData.CharacterId` and `PersonalityData.CharacterId` use `string` instead of `Guid`. **Decision**: Change to `Guid` type.

### lib-contract

- [ ] **T21/T25**: `DefaultEnforcementMode` config uses string requiring runtime parsing. **Decision**: Define `EnforcementMode` enum in schema.

- [ ] **T25**: `PartyModel.Role`, `MilestoneModel.Role`, `AssetReferenceModel.AssetType` store enums as strings. **Decision**: Change POCOs to use enum types directly.

### lib-currency

- [ ] **T25**: Multiple internal models store enums as strings requiring `Enum.TryParse`: `CurrencyDefinitionModel.Scope`, `CurrencyDefinitionModel.Precision`, `CurrencyDefinitionModel.CapOverflowBehavior`, `CurrencyDefinitionModel.AutogainMode`, `CurrencyDefinitionModel.ExpirationPolicy`, `CurrencyDefinitionModel.LinkageMode`, `WalletModel.OwnerType`, `TransactionRecordModel.TransactionType`. **Decision**: Change POCOs to use enum types directly.

- [ ] **T25**: Internal models use `string` for GUID fields: `CurrencyDefinitionModel.DefinitionId`, `WalletModel.WalletId`, `BalanceModel` ID fields, `TransactionRecordModel.TransactionId`, `HoldModel.HoldId`, etc. **Decision**: Change to `Guid` type.

### lib-escrow

- [ ] **T25**: `AssetFailureData.AssetType` is stored as string requiring `Enum.TryParse<AssetType>()`. **Decision**: Change POCO to use `AssetType` enum directly.

### lib-game-session

- [ ] **T25**: `GameSessionModel.SessionId` and `CleanupSessionModel.SessionId` are `string` instead of `Guid`. **Decision**: Change to `Guid` type.

- [ ] **T25**: `HandleSubscriptionUpdatedInternalAsync` compares string literals for subscription actions instead of using typed enum. **Decision**: Accept typed enum parameter and use enum equality.

### lib-inventory

- [ ] **T21/T25**: `DefaultWeightContribution` configuration property is `string` type but represents `WeightContribution` enum. Service parses with `Enum.TryParse`. **Decision**: Define as enum in configuration schema.

### lib-item

- [ ] **T21/T25**: Three configuration properties are `string` type but represent enums: `DefaultWeightPrecision` → `WeightPrecision`, `DefaultRarity` → `ItemRarity`, `DefaultSoulboundType` → `SoulboundType`. **Decision**: Define as enums in configuration schema.

- [ ] **T25**: Both `ItemTemplateModel` and `ItemInstanceModel` store GUID fields as `string` types, requiring `Guid.Parse()` in mappings. **Decision**: Change to `Guid` type.

### lib-location

- [x] **T25**: `LocationModel.LocationType` is stored as string requiring `Enum.Parse<LocationType>()`. **Decision**: Change POCO to use `LocationType` enum directly. **FIXED** - LocationModel already used enum type; fixed helper methods to use `Guid` instead of strings throughout, changed `List<string>` index stores to `List<Guid>`.

### lib-permission

- [ ] **T25**: `registrationInfo` uses anonymous type which cannot be reliably deserialized. **Decision**: Define a typed `ServiceRegistrationInfo` POCO class.

### lib-realm

- [x] **T25**: `RealmModel.RealmId` stored as string instead of Guid. **Decision**: Change POCO to use `Guid` type. **FIXED** - Changed `RealmModel.RealmId` to `Guid`, changed all helper method signatures, changed `List<string>` index to `List<Guid>`.

### lib-realm-history

- [ ] **T25**: Multiple internal models store enums as strings requiring `Enum.TryParse`: `RealmEventDataModel.EventCategory`, `RealmEventDataModel.Role`, `RealmLoreDataModel.ElementType`. **Decision**: Change POCOs to use enum types directly.

- [ ] **T25**: Multiple internal models store GUIDs as strings: `ParticipationId`, `RealmId`, `EventId`. **Decision**: Change to `Guid` type.

### lib-relationship

- [ ] **T25**: `RelationshipModel` stores entity types as strings requiring `Enum.Parse`: `Entity1Type`, `Entity2Type`. **Decision**: Change POCOs to use `EntityType` enum directly.

- [ ] **T25**: `RelationshipModel` stores GUIDs as strings: `RelationshipId`, `Entity1Id`, `Entity2Id`, `RelationshipTypeId`. **Decision**: Change to `Guid` type.

### lib-relationship-type

- [ ] **T25**: `RelationshipTypeModel` stores GUIDs as strings: `RelationshipTypeId`, `ParentTypeId`, `InverseTypeId`. **Decision**: Change to `Guid` type.

### lib-save-load

- [ ] **T21/T25**: `DefaultCompressionType` and `DefaultDeltaAlgorithm` configuration properties are `string` but represent enums. **Decision**: Define as enums in configuration schema.

- [ ] **T25**: Multiple internal models store enums as strings: `PendingUploadEntry.CompressionType`, `SaveVersionManifest.CompressionType`, `SaveSlotMetadata.CompressionType/OwnerType/Category`, `ExportManifest.OwnerType`. **Decision**: Change POCOs to use enum types directly.

### lib-scene

- [ ] **T25**: `SceneIndexEntry.SceneType` is stored as string requiring `Enum.TryParse<SceneType>()`. **Decision**: Change POCO to use `SceneType` enum directly.

- [ ] **T25**: Multiple internal models store GUIDs as strings: `SceneIndexEntry.SceneId`, `SceneIndexEntry.AssetId`, `CheckoutState.SceneId`, `CheckoutState.EditorId`, `SceneContentEntry.SceneId`. **Decision**: Change to `Guid` type.

### lib-species

- [ ] **T25**: `SpeciesModel` uses `string` for `SpeciesId` and `List<string>` for `RealmIds` instead of proper GUID types. **Decision**: Change to `Guid` and `List<Guid>`.

### lib-subscription

- [ ] **T25**: `SubscriptionDataModel` stores `SubscriptionId`, `AccountId`, and `ServiceId` as `string` rather than `Guid`. **Decision**: Change to `Guid` type.

### lib-voice

- [ ] **T25**: `VoiceRoomData` stores tier and codec as strings, requiring parsing in service methods. **Decision**: Change to enum types (`VoiceTier`, `VoiceCodec`).

---

## Code Generation Issues

### bannou-service (Program.cs)

- [ ] **T25**: `Program.ServiceGUID` is declared as `string` constant, causing all generated `*PermissionRegistration.cs` files to use `Guid.Parse(Program.ServiceGUID)`. This affects ALL services. **Decision**: Change `Program.ServiceGUID` to `Guid` type and update code generator.

