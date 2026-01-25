# Deep Dive Cleanup Checklist

> **INSTRUCTIONS (DO NOT MODIFY)**:
>
> Let's go through the deep dive documents in docs/plugins, in alphabetical order, and make comprehensive updates to each, and ensure each document is up to the original standards set in the DEEP_DIVE_TEMPLATE. Before you start though, I want you to clear the "_CLEANUP" file, empty it out so it's just an empty checklist again, and as you're going, anything in "bugs" sections that hasn't been handled, add to the list (keep em stacking up), any T21/T25/T26 related stuff, those are bugs, add them to the list, and any simple shit that has no reason to still be left, add those to the list. I want you to maintain that pattern of A) read the deep-dive for a plugin, B) investigate the plugin, C) update the deep-dive with new information as you investigate the plugin, especially new configuration, changes around quirks it mentions, new quirks it didn't mention, etc, D) clear out entries in the deep dive that say "FIXED" and such, update the relevant sections of the deep dive with updated information from our work in this branch, E) ensure each "this only requires one simple decision, no more" type bug or issue or tenet violation ends up in the "_CLEANUP" file, to be tracked more easily- be critical, and the criteria is "this is simple because it requires only a simple decision"- it is NOT determined by anything else (whether or not we need to edit schema files, etc), F) ensure the final deep dive document when you're done is pristine, exactly according to the "_TEMPLATE" document all deep dives are meant to follow, and finally G) move on to the next deep dive/plugin and repeat. Once you're done with ALL of the deep dives, we'll then go back and look at the cleanup file. I want you to copy all of these instructions VERBATIM into the top of the "_CLEANUP" file as instructions that must be followed for this task, so that every time you go to update it with an entry, you'll be reminded of what exactly you need to be doing, and you won't get off-track after compacts. Your compact message should always include precisely "re-read the docs/plugins/DEEP_DIVE_CLEANUP.md file again after compacting and then continue".
>
> **Criteria for adding items here**: "This is simple because it requires only a simple decision" - NOT determined by implementation complexity, schema changes needed, etc.

---

## Cleanup Items

### lib-asset

- [ ] **T25**: `BundleMetadata.BundleId`, `SourceBundleReferenceInternal.BundleId`, and related bundle model ID fields store GUIDs as strings requiring `Guid.Parse()` at usage sites. **Decision**: Change internal POCOs to use `Guid` type.

- [ ] **T25**: `AssetProcessingResult.ErrorCode` and `AssetValidationResult.ErrorCode` use string constants (`"UNSUPPORTED_CONTENT_TYPE"`, `"FILE_TOO_LARGE"`, etc.). **Decision**: Define an `AssetProcessingErrorCode` enum in schema.

### lib-auth

- [ ] **T25**: `SessionDataModel.SessionId` stores a GUID as string with `= string.Empty` default. Multiple sites use `Guid.Parse(sessionId)`. **Decision**: Change to `Guid` type.

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

