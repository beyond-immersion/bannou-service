# Generated Deprecation Entities Reference

> **Source**: `schemas/*-events.yaml` (x-lifecycle blocks with `deprecation: true`)
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists every entity with `deprecation: true` in its x-lifecycle definition.
Use this to identify which entities need `instanceEntity` declarations, which services
need deprecation marker interfaces, and the current state of Category B compliance.

## Summary

- **Total deprecatable entities**: 20
- **With `instanceEntity` declared**: 8
- **Missing `instanceEntity`**: 12

## All Deprecatable Entities

| Service | Entity | `instanceEntity` | Status |
|---------|--------|-------------------|--------|
| Achievement | `AchievementDefinition` | — | Missing |
| Character Encounter | `EncounterType` | — | Missing |
| Chat | `ChatRoomType` | `ChatRoom` | OK |
| Collection | `CollectionEntryTemplate` | `Collection` | OK |
| Contract | `ContractTemplate` | `ContractInstance` | OK |
| Currency | `CurrencyDefinition` | `CurrencyWallet` | OK |
| Faction | `Faction` | — | Missing |
| Gardener | `ScenarioTemplate` | — | Missing |
| Item | `ItemTemplate` | `ItemInstance` | OK |
| Leaderboard | `LeaderboardDefinition` | — | Missing |
| License | `LicenseBoardTemplate` | `LicenseBoard` | OK |
| Location | `Location` | — | Missing |
| Quest | `QuestDefinition` | `QuestInstance` | OK |
| Realm | `Realm` | — | Missing |
| Relationship | `RelationshipType` | — | Missing |
| Seed | `SeedType` | — | Missing |
| Species | `Species` | — | Missing |
| Status | `StatusTemplate` | — | Missing |
| Storyline | `ScenarioDefinition` | `ScenarioExecution` | OK |
| Transit | `TransitMode` | — | Missing |

## Entity Details

### Achievement: AchievementDefinition

- **Service**: `achievement`
- **Schema**: `schemas/achievement-events.yaml`
- **Topic prefix**: `achievement`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `gameServiceId`, `achievementId`, `displayName`, `description`, `hiddenDescription`, `category`, `achievementType`, `entityTypes`, `progressTarget`, `points`, `iconUrl`, `platforms`, `platformMappings`, `prerequisites`, `scoreType`, `milestoneType`, `milestoneValue`, `milestoneName`, `leaderboardId`, `rankThreshold`, `isActive`, `earnedCount`, `metadata`

### Character Encounter: EncounterType

- **Service**: `character-encounter`
- **Schema**: `schemas/character-encounter-events.yaml`
- **Topic prefix**: `character-encounter`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `typeId`, `code`, `name`, `description`, `isBuiltIn`, `sortOrder`, `isActive`

### Chat: ChatRoomType

- **Service**: `chat`
- **Schema**: `schemas/chat-events.yaml`
- **Topic prefix**: `chat`
- **Instance entity**: `ChatRoom`
- **Model fields**: `code`, `displayName`, `gameServiceId`, `messageFormat`, `persistenceMode`, `allowAnonymousSenders`, `status`

### Collection: CollectionEntryTemplate

- **Service**: `collection`
- **Schema**: `schemas/collection-events.yaml`
- **Topic prefix**: `collection`
- **Instance entity**: `Collection`
- **Model fields**: `entryTemplateId`, `code`, `collectionType`, `gameServiceId`, `displayName`, `category`, `hideWhenLocked`, `itemTemplateId`

### Contract: ContractTemplate

- **Service**: `contract`
- **Schema**: `schemas/contract-events.yaml`
- **Topic prefix**: `contract`
- **Instance entity**: `ContractInstance`
- **Model fields**: `templateId`, `code`, `name`, `description`, `realmId`, `minParties`, `maxParties`, `defaultEnforcementMode`, `transferable`, `isActive`

### Currency: CurrencyDefinition

- **Service**: `currency`
- **Schema**: `schemas/currency-events.yaml`
- **Topic prefix**: `currency`
- **Instance entity**: `CurrencyWallet`
- **Model fields**: `definitionId`, `code`, `name`, `scope`, `precision`, `isActive`, `modifiedAt`

### Faction: Faction

- **Service**: `faction`
- **Schema**: `schemas/faction-events.yaml`
- **Topic prefix**: `*(none — Pattern A)*`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `factionId`, `gameServiceId`, `name`, `code`, `realmId`, `isRealmBaseline`, `parentFactionId`, `seedId`, `status`, `currentPhase`, `memberCount`

### Gardener: ScenarioTemplate

- **Service**: `gardener`
- **Schema**: `schemas/gardener-events.yaml`
- **Topic prefix**: `*(none — Pattern A)*`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `scenarioTemplateId`, `code`, `displayName`, `description`, `category`, `connectivityMode`, `status`

### Item: ItemTemplate

- **Service**: `item`
- **Schema**: `schemas/item-events.yaml`
- **Topic prefix**: `item`
- **Instance entity**: `ItemInstance`
- **Model fields**: `templateId`, `code`, `gameId`, `name`, `description`, `category`, `rarity`, `quantityModel`, `maxStackSize`, `scope`, `soulboundType`, `tradeable`, `destroyable`, `hasDurability`, `maxDurability`, `isActive`, `migrationTargetId`

### Leaderboard: LeaderboardDefinition

- **Service**: `leaderboard`
- **Schema**: `schemas/leaderboard-events.yaml`
- **Topic prefix**: `leaderboard`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `gameServiceId`, `leaderboardId`, `displayName`, `sortOrder`, `updateMode`, `isSeasonal`, `isPublic`

### License: LicenseBoardTemplate

- **Service**: `license`
- **Schema**: `schemas/license-events.yaml`
- **Topic prefix**: `license`
- **Instance entity**: `LicenseBoard`
- **Model fields**: `boardTemplateId`, `gameServiceId`, `name`, `gridWidth`, `gridHeight`, `boardContractTemplateId`, `adjacencyMode`, `isActive`

### Location: Location

- **Service**: `location`
- **Schema**: `schemas/location-events.yaml`
- **Topic prefix**: `*(none — Pattern A)*`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `locationId`, `realmId`, `code`, `name`, `description`, `locationType`, `parentLocationId`, `depth`, `bounds`, `boundsPrecision`, `coordinateMode`, `localOrigin`, `metadata`

### Quest: QuestDefinition

- **Service**: `quest`
- **Schema**: `schemas/quest-events.yaml`
- **Topic prefix**: `quest`
- **Instance entity**: `QuestInstance`
- **Model fields**: `definitionId`, `contractTemplateId`, `code`, `name`, `description`, `category`, `difficulty`, `levelRequirement`, `repeatable`, `cooldownSeconds`, `deadlineSeconds`, `maxQuestors`, `gameServiceId`

### Realm: Realm

- **Service**: `realm`
- **Schema**: `schemas/realm-events.yaml`
- **Topic prefix**: `*(none — Pattern A)*`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `realmId`, `code`, `name`, `gameServiceId`, `description`, `category`, `isActive`, `isSystemType`, `metadata`

### Relationship: RelationshipType

- **Service**: `relationship`
- **Schema**: `schemas/relationship-events.yaml`
- **Topic prefix**: `relationship`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `relationshipTypeId`, `code`, `name`, `description`, `category`, `parentTypeId`, `parentTypeCode`, `inverseTypeId`, `inverseTypeCode`, `isBidirectional`, `depth`, `metadata`

### Seed: SeedType

- **Service**: `seed`
- **Schema**: `schemas/seed-events.yaml`
- **Topic prefix**: `seed`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `seedTypeCode`, `gameServiceId`, `displayName`, `description`, `maxPerOwner`, `bondCardinality`, `bondPermanent`, `sameOwnerGrowthMultiplier`

### Species: Species

- **Service**: `species`
- **Schema**: `schemas/species-events.yaml`
- **Topic prefix**: `*(none — Pattern A)*`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `speciesId`, `code`, `name`, `description`, `category`, `isPlayable`, `baseLifespan`, `maturityAge`, `traitModifiers`, `realmIds`, `metadata`

### Status: StatusTemplate

- **Service**: `status`
- **Schema**: `schemas/status-events.yaml`
- **Topic prefix**: `status`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `statusTemplateId`, `gameServiceId`, `code`, `displayName`, `category`, `stackable`, `maxStacks`, `stackBehavior`

### Storyline: ScenarioDefinition

- **Service**: `storyline`
- **Schema**: `schemas/storyline-events.yaml`
- **Topic prefix**: `storyline`
- **Instance entity**: `ScenarioExecution`
- **Model fields**: `scenarioId`, `code`, `name`, `description`, `priority`, `enabled`, `realmId`, `gameServiceId`

### Transit: TransitMode

- **Service**: `transit`
- **Schema**: `schemas/transit-events.yaml`
- **Topic prefix**: `transit`
- **Instance entity**: `*(not declared)*`
- **Model fields**: `code`, `name`, `description`, `baseSpeedKmPerGameHour`, `terrainSpeedModifiers`, `passengerCapacity`, `cargoCapacityKg`, `cargoSpeedPenaltyRate`, `compatibleTerrainTypes`, `validEntityTypes`, `requirements`, `fatigueRatePerGameHour`, `noiseLevelNormalized`, `realmRestrictions`, `tags`, `modifiedAt`

---

*This file is auto-generated from x-lifecycle definitions. See [TENETS.md](reference/TENETS.md) for deprecation lifecycle rules.*
