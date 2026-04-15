# Generated Deprecation Entities Reference

> **Source**: `schemas/*-service-events.yaml` (x-lifecycle blocks with `deprecation: true`)
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists every entity with `deprecation: true` in its x-lifecycle definition.
Use this to identify which entities need `instanceEntity` declarations, which services
need deprecation marker interfaces, and the current state of Category B compliance.

## Summary

- **Total deprecatable entities**: 28
- **With `instanceEntity` declared**: 19
- **Missing `instanceEntity`**: 9

## All Deprecatable Entities

| Service | Entity | `instanceEntity` | Status |
|---------|--------|-------------------|--------|
| Achievement | `AchievementDefinition` | `AchievementProgressRecord` | OK |
| Affix | `AffixDefinition` | `AffixInstance` | OK |
| Character Encounter | `EncounterType` | `EncounterRecord` | OK |
| Character Lifecycle | `HeritableTraitTemplate` | `GeneticProfile` | OK |
| Character Lifecycle | `HybridTraitTemplate` | `GeneticProfile` | OK |
| Character Lifecycle | `LifecycleTemplate` | `LifecycleProfile` | OK |
| Chat | `ChatRoomType` | `ChatRoom` | OK |
| Collection | `CollectionEntryTemplate` | `Collection` | OK |
| Contract | `ContractTemplate` | `ContractInstance` | OK |
| Craft | `CraftRecipe` | `CraftSession` | OK |
| Currency | `CurrencyDefinition` | `CurrencyBalance` | OK |
| Divine | `Deity` | — | Missing |
| Environment | `ClimateTemplate` | — | Missing |
| Faction | `Faction` | — | Missing |
| Gardener | `ScenarioTemplate` | `ScenarioInstance` | OK |
| Genesis | `GenesisTemplate` | `GenesisEntity` | OK |
| Item | `ItemTemplate` | `ItemInstance` | OK |
| Leaderboard | `LeaderboardDefinition` | `LeaderboardEntry` | OK |
| License | `LicenseBoardTemplate` | `LicenseBoard` | OK |
| Location | `Location` | — | Missing |
| Quest | `QuestDefinition` | `QuestInstance` | OK |
| Realm | `Realm` | — | Missing |
| Relationship | `RelationshipType` | — | Missing |
| Seed | `SeedType` | `Seed` | OK |
| Species | `Species` | — | Missing |
| Status | `StatusTemplate` | — | Missing |
| Storyline | `ScenarioDefinition` | `ScenarioExecution` | OK |
| Transit | `TransitMode` | — | Missing |

## Entity Details

### Achievement: AchievementDefinition

- **Service**: `achievement`
- **Schema**: `schemas/achievement-service-events.yaml`
- **Topic prefix**: `achievement`
- **Instance entity**: `AchievementProgressRecord`
- **Model fields**: `gameServiceId`, `achievementId`, `displayName`, `description`, `hiddenDescription`, `category`, `achievementType`, `entityTypes`, `progressTarget`, `points`, `iconUrl`, `platforms`, `platformMappings`, `prerequisites`, `scoreType`, `milestoneType`, `milestoneValue`, `milestoneName`, `leaderboardId`, `rankThreshold`, `isActive`, `earnedCount`, `metadata`

### Affix: AffixDefinition

- **Service**: `affix`
- **Schema**: `schemas/affix-service-events.yaml`
- **Topic prefix**: `affix`
- **Instance entity**: `AffixInstance`
- **Model fields**: `definitionId`, `gameServiceId`, `code`, `slotType`, `modGroup`, `tier`, `category`, `tags`, `statGrants`, `spawnWeight`, `spawnTagModifiers`, `requiredItemLevel`, `requiredInfluences`, `validItemClasses`, `displayName`, `displayOrder`

### Character Encounter: EncounterType

- **Service**: `character-encounter`
- **Schema**: `schemas/character-encounter-service-events.yaml`
- **Topic prefix**: `character-encounter`
- **Instance entity**: `EncounterRecord`
- **Model fields**: `typeId`, `code`, `name`, `description`, `isBuiltIn`, `sortOrder`, `isActive`

### Character Lifecycle: HeritableTraitTemplate

- **Service**: `character-lifecycle`
- **Schema**: `schemas/character-lifecycle-service-events.yaml`
- **Topic prefix**: `character-lifecycle`
- **Instance entity**: `GeneticProfile`
- **Model fields**: `speciesCode`, `gameServiceId`, `traits`

### Character Lifecycle: HybridTraitTemplate

- **Service**: `character-lifecycle`
- **Schema**: `schemas/character-lifecycle-service-events.yaml`
- **Topic prefix**: `character-lifecycle`
- **Instance entity**: `GeneticProfile`
- **Model fields**: `speciesA`, `speciesB`, `gameServiceId`, `traitOverrides`, `hybridFertilityModifier`

### Character Lifecycle: LifecycleTemplate

- **Service**: `character-lifecycle`
- **Schema**: `schemas/character-lifecycle-service-events.yaml`
- **Topic prefix**: `character-lifecycle`
- **Instance entity**: `LifecycleProfile`
- **Model fields**: `speciesCode`, `gameServiceId`, `stages`, `naturalDeathRange`, `fertilityWindow`

### Chat: ChatRoomType

- **Service**: `chat`
- **Schema**: `schemas/chat-service-events.yaml`
- **Topic prefix**: `chat`
- **Instance entity**: `ChatRoom`
- **Model fields**: `code`, `displayName`, `gameServiceId`, `messageFormat`, `persistenceMode`, `allowAnonymousSenders`, `status`

### Collection: CollectionEntryTemplate

- **Service**: `collection`
- **Schema**: `schemas/collection-service-events.yaml`
- **Topic prefix**: `collection`
- **Instance entity**: `Collection`
- **Model fields**: `entryTemplateId`, `code`, `collectionType`, `gameServiceId`, `displayName`, `category`, `hideWhenLocked`, `itemTemplateId`

### Contract: ContractTemplate

- **Service**: `contract`
- **Schema**: `schemas/contract-service-events.yaml`
- **Topic prefix**: `contract`
- **Instance entity**: `ContractInstance`
- **Model fields**: `templateId`, `code`, `name`, `description`, `realmId`, `minParties`, `maxParties`, `defaultEnforcementMode`, `transferable`, `isActive`

### Craft: CraftRecipe

- **Service**: `craft`
- **Schema**: `schemas/craft-service-events.yaml`
- **Topic prefix**: `craft`
- **Instance entity**: `CraftSession`
- **Model fields**: `recipeId`, `gameServiceId`, `code`, `recipeType`, `domain`, `category`, `tags`, `isDiscoverable`

### Currency: CurrencyDefinition

- **Service**: `currency`
- **Schema**: `schemas/currency-service-events.yaml`
- **Topic prefix**: `currency`
- **Instance entity**: `CurrencyBalance`
- **Model fields**: `definitionId`, `code`, `name`, `scope`, `precision`, `isActive`, `modifiedAt`

### Divine: Deity

- **Service**: `divine`
- **Schema**: `schemas/divine-service-events.yaml`
- **Topic prefix**: `divine`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `deityId`, `gameServiceId`, `code`, `displayName`, `description`, `domains`, `status`, `followerCount`, `maxAttentionSlots`, `actorId`, `seedId`, `genesisEntityId`, `currencyWalletId`, `realmId`, `divineAffectations`

### Environment: ClimateTemplate

- **Service**: `environment`
- **Schema**: `schemas/environment-service-events.yaml`
- **Topic prefix**: `environment`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `templateId`, `gameServiceId`, `biomeCode`, `displayName`, `description`, `temperatureCurves`, `weatherDistributions`, `atmosphericBaselines`, `resourceAvailability`, `altitudeTemperatureRate`, `depthTemperatureRate`, `heatThreshold`

### Faction: Faction

- **Service**: `faction`
- **Schema**: `schemas/faction-service-events.yaml`
- **Topic prefix**: `*(none — Pattern A)*`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `factionId`, `gameServiceId`, `name`, `code`, `realmId`, `isRealmBaseline`, `parentFactionId`, `seedId`, `status`, `authorityLevel`, `currentPhase`, `memberCount`

### Gardener: ScenarioTemplate

- **Service**: `gardener`
- **Schema**: `schemas/gardener-service-events.yaml`
- **Topic prefix**: `*(none — Pattern A)*`
- **Instance entity**: `ScenarioInstance`
- **Model fields**: `scenarioTemplateId`, `code`, `displayName`, `description`, `category`, `connectivityMode`, `status`

### Genesis: GenesisTemplate

- **Service**: `genesis`
- **Schema**: `schemas/genesis-service-events.yaml`
- **Topic prefix**: `genesis`
- **Instance entity**: `GenesisEntity`
- **Model fields**: `templateCode`, `gameServiceId`, `displayName`, `description`, `physicalFormType`

### Item: ItemTemplate

- **Service**: `item`
- **Schema**: `schemas/item-service-events.yaml`
- **Topic prefix**: `item`
- **Instance entity**: `ItemInstance`
- **Model fields**: `templateId`, `code`, `gameId`, `name`, `description`, `category`, `rarity`, `quantityModel`, `maxStackSize`, `scope`, `soulboundType`, `tradeable`, `destroyable`, `hasDurability`, `maxDurability`, `isActive`, `migrationTargetId`

### Leaderboard: LeaderboardDefinition

- **Service**: `leaderboard`
- **Schema**: `schemas/leaderboard-service-events.yaml`
- **Topic prefix**: `leaderboard`
- **Instance entity**: `LeaderboardEntry`
- **Model fields**: `gameServiceId`, `leaderboardId`, `displayName`, `sortOrder`, `updateMode`, `isSeasonal`, `isPublic`

### License: LicenseBoardTemplate

- **Service**: `license`
- **Schema**: `schemas/license-service-events.yaml`
- **Topic prefix**: `license`
- **Instance entity**: `LicenseBoard`
- **Model fields**: `boardTemplateId`, `gameServiceId`, `name`, `gridWidth`, `gridHeight`, `boardContractTemplateId`, `adjacencyMode`, `isActive`

### Location: Location

- **Service**: `location`
- **Schema**: `schemas/location-service-events.yaml`
- **Topic prefix**: `*(none — Pattern A)*`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `locationId`, `realmId`, `code`, `name`, `description`, `locationType`, `parentLocationId`, `depth`, `bounds`, `boundsPrecision`, `coordinateMode`, `localOrigin`, `metadata`

### Quest: QuestDefinition

- **Service**: `quest`
- **Schema**: `schemas/quest-service-events.yaml`
- **Topic prefix**: `quest`
- **Instance entity**: `QuestInstance`
- **Model fields**: `definitionId`, `contractTemplateId`, `code`, `name`, `description`, `category`, `difficulty`, `levelRequirement`, `repeatable`, `cooldownSeconds`, `deadlineSeconds`, `maxQuestors`, `gameServiceId`

### Realm: Realm

- **Service**: `realm`
- **Schema**: `schemas/realm-service-events.yaml`
- **Topic prefix**: `*(none — Pattern A)*`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `realmId`, `code`, `name`, `gameServiceId`, `description`, `category`, `isActive`, `isSystemType`, `metadata`

### Relationship: RelationshipType

- **Service**: `relationship`
- **Schema**: `schemas/relationship-service-events.yaml`
- **Topic prefix**: `relationship`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `relationshipTypeId`, `code`, `name`, `description`, `category`, `parentTypeId`, `parentTypeCode`, `inverseTypeId`, `inverseTypeCode`, `isBidirectional`, `depth`, `metadata`

### Seed: SeedType

- **Service**: `seed`
- **Schema**: `schemas/seed-service-events.yaml`
- **Topic prefix**: `seed`
- **Instance entity**: `Seed`
- **Model fields**: `seedTypeCode`, `gameServiceId`, `displayName`, `description`, `maxPerOwner`, `bondCardinality`, `bondPermanent`, `sameOwnerGrowthMultiplier`

### Species: Species

- **Service**: `species`
- **Schema**: `schemas/species-service-events.yaml`
- **Topic prefix**: `*(none — Pattern A)*`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `speciesId`, `code`, `name`, `description`, `category`, `isPlayable`, `baseLifespan`, `maturityAge`, `traitModifiers`, `realmIds`, `metadata`

### Status: StatusTemplate

- **Service**: `status`
- **Schema**: `schemas/status-service-events.yaml`
- **Topic prefix**: `status`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `statusTemplateId`, `gameServiceId`, `code`, `displayName`, `category`, `stackable`, `maxStacks`, `stackBehavior`

### Storyline: ScenarioDefinition

- **Service**: `storyline`
- **Schema**: `schemas/storyline-service-events.yaml`
- **Topic prefix**: `storyline`
- **Instance entity**: `ScenarioExecution`
- **Model fields**: `scenarioId`, `code`, `name`, `description`, `priority`, `enabled`, `realmId`, `gameServiceId`

### Transit: TransitMode

- **Service**: `transit`
- **Schema**: `schemas/transit-service-events.yaml`
- **Topic prefix**: `transit`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `code`, `name`, `description`, `baseSpeedKmPerGameHour`, `terrainSpeedModifiers`, `passengerCapacity`, `cargoCapacityKg`, `cargoSpeedPenaltyRate`, `compatibleTerrainTypes`, `validEntityTypes`, `requirements`, `fatigueRatePerGameHour`, `noiseLevelNormalized`, `realmRestrictions`, `tags`, `modifiedAt`

---

*This file is auto-generated from x-lifecycle definitions. See [TENETS.md](../reference/TENETS.md) for deprecation lifecycle rules.*
