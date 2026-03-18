# Generated Events Reference

> **Source**: `schemas/*-service-events.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists all events defined in Bannou's event schemas.

## Events by Service

### Achievement

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `AchievementPlatformSyncedEvent` | Custom | `achievement-platform-synced` | Published when an achievement is synced to an exte... |
| `AchievementProgressUpdatedEvent` | Lifecycle (Updated) | `achievement-progress.updated` | Published when progress is made on a progressive a... |
| `AchievementUnlockedEvent` | Custom | `achievement-unlocked` | Published when an entity unlocks an achievement |

### Actor

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `ActorCharacterBoundEvent` | Custom | `actor-character-bound` | Published when an actor binds to a character, eith... |
| `ActorCompletedEvent` | Custom | `actor-completed` | Published when an actor completes execution (self-... |
| `ActorEncounterEndedEvent` | Custom | `actor-encounter-ended` | Published when an Event Brain actor ends an encoun... |
| `ActorEncounterPhaseChangedEvent` | Custom | `actor-encounter-phase-changed` | Published when an encounter transitions to a new p... |
| `ActorEncounterStartedEvent` | Custom | `actor-encounter-started` | Published when an Event Brain actor starts managin... |
| `ActorInstanceStartedEvent` | Custom | `actor-instance-started` | Published when an actor instance successfully star... |
| `ActorStatePersistedEvent` | Custom | `actor-state-persisted` | Published when actor state is auto-saved to persis... |
| `ActorStatusChangedEvent` | Custom | `actor-status-changed` | Published when an actor's status changes. |
| `CharacterPerceptionEvent` | Custom | `character-perception` | Perception event published by game servers for cha... |
| `PoolNodeDrainingEvent` | Custom | `pool-node-draining` | Published when a pool node begins graceful shutdow... |
| `PoolNodeHeartbeatEvent` | Health | `pool-node.heartbeat` | Periodic heartbeat from pool nodes to control plan... |
| `PoolNodeRegisteredEvent` | Registration | `pool-node.registered` | Published when a pool node starts and registers wi... |
| `PoolNodeUnhealthyEvent` | Custom | `pool-node-unhealthy` | Published by control plane when a pool node is det... |

### Affix

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `AffixBatchGeneratedEvent` | Custom | `affix-batch-generated` | Published when a batch generation completes (dedup... |
| `AffixInfluenceChangedEvent` | Custom | `affix-influence-changed` | Published when influence types change on an item |
| `AffixInstanceStateChangedEvent` | Custom | `affix-instance-state-changed` | Published when item state flags change |
| `AffixModifierAppliedEvent` | Custom | `affix-modifier-applied` | Published when an affix is applied to an item |
| `AffixModifierRemovedEvent` | Custom | `affix-modifier-removed` | Published when an affix is removed from an item |
| `AffixModifierRerolledEvent` | Custom | `affix-modifier-rerolled` | Published when affix values are rerolled on an ite... |
| `AffixRarityChangedEvent` | Custom | `affix-rarity-changed` | Published when an item's effective rarity transiti... |

### Analytics

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `AnalyticsControllerRecordedEvent` | Custom | `analytics-controller-recorded` | Published when a controller possession or release ... |
| `AnalyticsMilestoneReachedEvent` | Custom | `analytics-milestone-reached` | Published when an entity reaches a statistical mil... |
| `AnalyticsRatingUpdatedEvent` | Lifecycle (Updated) | `analytics-rating.updated` | Published when an entity's Glicko-2 skill rating c... |
| `AnalyticsScoreUpdatedEvent` | Lifecycle (Updated) | `analytics-score.updated` | Published when an entity's score or statistic chan... |

### Arbitration

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `ArbitrationArbiterAssignedEvent` | Custom | `arbitration-arbiter-assigned` | Published when an arbiter is assigned to a case |
| `ArbitrationArbiterRecusedEvent` | Custom | `arbitration-arbiter-recused` | Published when an arbiter recuses from a case |
| `ArbitrationArbiterRequestedEvent` | Custom | `arbitration-arbiter-requested` | Published when an external arbiter is requested. C... |
| `ArbitrationCaseClosedEvent` | Custom | `arbitration-case-closed` | Published when a case reaches a terminal closed st... |
| `ArbitrationCaseDefaultedEvent` | Custom | `arbitration-case-defaulted` | Published when a respondent fails to respond withi... |
| `ArbitrationCaseFiledEvent` | Custom | `arbitration-case-filed` | Published when a case is filed with accepted juris... |
| `ArbitrationEvidenceSubmittedEvent` | Custom | `arbitration-evidence-submitted` | Published when a party submits evidence for a case |
| `ArbitrationHearingCompletedEvent` | Custom | `arbitration-hearing-completed` | Published when a hearing milestone is completed |
| `ArbitrationJurisdictionChallengedEvent` | Custom | `arbitration-jurisdiction-challenged` | Published when a party challenges jurisdictional a... |
| `ArbitrationNoticeConfirmedEvent` | Custom | `arbitration-notice-confirmed` | Published when service of process is confirmed |
| `ArbitrationRulingAppealedEvent` | Custom | `arbitration-ruling-appealed` | Published when a ruling is appealed |
| `ArbitrationRulingEnforcedEvent` | Custom | `arbitration-ruling-enforced` | Published when all ruling consequences have been s... |
| `ArbitrationRulingIssuedEvent` | Custom | `arbitration-ruling-issued` | Published when an arbiter issues a ruling |

### Asset

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `AssetProcessingCompletedEvent` | Custom | `asset-processing-completed` | Event published when asset processing completes |
| `AssetProcessingJobDispatchedEvent` | Custom | `asset-processing-job-dispatched` | Event published when an asset processing job is di... |
| `AssetProcessingQueuedEvent` | Custom | `asset-processing-queued` | Event published when an asset is queued for proces... |
| `AssetProcessingRetryEvent` | Custom | `asset-processing-retry` | Event published when asset processing needs to be ... |
| `AssetReadyEvent` | Custom | `asset-ready` | Event published when an asset is fully processed a... |
| `AssetUploadCompletedEvent` | Custom | `asset-upload-completed` | Event published when an upload is completed and fi... |
| `AssetUploadRequestedEvent` | Custom | `asset-upload-requested` | Event published when a new upload is initiated via... |
| `BundleCreatedEvent` | Lifecycle (Created) | `bundle.created` | Event published when a bundle is successfully crea... |
| `BundleCreationJobQueuedEvent` | Custom | `bundle-creation-job-queued` | Event published when a bundle creation job is queu... |
| `BundleDeletedEvent` | Lifecycle (Deleted) | `bundle.deleted` | Event published when a bundle is permanently delet... |
| `BundleUpdatedEvent` | Lifecycle (Updated) | `bundle.updated` | Event published when bundle metadata is updated |
| `MetabundleCreatedEvent` | Lifecycle (Created) | `metabundle.created` | Event published when a metabundle is successfully ... |
| `MetabundleJobQueuedEvent` | Custom | `metabundle-job-queued` | Event published when a metabundle creation job is ... |

### Auth

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `AuthLoginFailedEvent` | Custom | `auth-login-failed` | Published when a login attempt fails (for brute fo... |
| `AuthLoginSuccessfulEvent` | Custom | `auth-login-successful` | Published when a user successfully authenticates w... |
| `AuthMfaDisabledEvent` | Custom | `auth-mfa-disabled` | Published when MFA is disabled for an account |
| `AuthMfaEnabledEvent` | Custom | `auth-mfa-enabled` | Published when MFA is successfully enabled for an ... |
| `AuthMfaFailedEvent` | Custom | `auth-mfa-failed` | Published when MFA verification fails during login |
| `AuthMfaVerifiedEvent` | Custom | `auth-mfa-verified` | Published when MFA verification succeeds during lo... |
| `AuthOAuthLoginSuccessfulEvent` | Custom | `auth-o-auth-login-successful` | Published when a user authenticates via OAuth prov... |
| `AuthPasswordResetSuccessfulEvent` | Custom | `auth-password-reset-successful` | Published when a password reset is successfully co... |
| `AuthRegistrationSuccessfulEvent` | Custom | `auth-registration-successful` | Published when a new user successfully registers |
| `AuthSteamLoginSuccessfulEvent` | Custom | `auth-steam-login-successful` | Published when a user authenticates via Steam |
| `SessionInvalidatedEvent` | Custom | `session.invalidated` | Event published when sessions are invalidated (log... |
| `SessionUpdatedEvent` | Lifecycle (Updated) | `session.updated` | Published when a session's roles or authorizations... |

### Behavior

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `BehaviorCompilationFailedEvent` | Custom | `behavior-compilation-failed` | Event published when ABML compilation fails. Used ... |
| `CinematicExtensionAvailableEvent` | Custom | `cinematic-extension-available` | Event published when a cinematic extension is avai... |
| `GoapPlanGeneratedEvent` | Custom | `goap-plan-generated` | Event published when the GOAP planner generates a ... |

### Broadcast

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `BroadcastAudiencePulseEvent` | Custom | `broadcast-audience-pulse` | Periodic batched anonymous sentiment data from a p... |

### Character

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `CharacterCompressedEvent` | Custom | `character-compressed` | Published when a character is compressed to archiv... |
| `CharacterRealmJoinedEvent` | Custom | `character-realm-joined` | Event published when a character joins a realm (cr... |
| `CharacterRealmLeftEvent` | Custom | `character-realm-left` | Event published when a character leaves a realm (d... |

### Character Encounter

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `EncounterDeletedEvent` | Lifecycle (Deleted) | `encounter.deleted` | Published when an encounter is deleted |
| `EncounterMemoryFadedEvent` | Custom | `encounter-memory-faded` | Published when a character's memory of an encounte... |
| `EncounterMemoryRefreshedEvent` | Custom | `encounter-memory-refreshed` | Published when a character's memory of an encounte... |
| `EncounterPerspectiveUpdatedEvent` | Lifecycle (Updated) | `encounter-perspective.updated` | Published when a character's perspective on an enc... |
| `EncounterRecordedEvent` | Custom | `encounter-recorded` | Published when a new encounter is recorded between... |

### Character History

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `CharacterBackstoryCreatedEvent` | Lifecycle (Created) | `character-backstory.created` | Published when a character's backstory is first cr... |
| `CharacterBackstoryDeletedEvent` | Lifecycle (Deleted) | `character-backstory.deleted` | Published when a character's backstory is deleted |
| `CharacterBackstoryUpdatedEvent` | Lifecycle (Updated) | `character-backstory.updated` | Published when a character's backstory is updated |
| `CharacterHistoryDeletedEvent` | Lifecycle (Deleted) | `character-history.deleted` | Published when all history data for a character is... |

### Character Lifecycle

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `CharacterLifecycleBirthEvent` | Custom | `character-lifecycle-birth` | Published when a character is born through procrea... |
| `CharacterLifecycleBloodlineFormedEvent` | Custom | `character-lifecycle-bloodline-formed` | Published when a new bloodline is identified from ... |
| `CharacterLifecycleDeathEvent` | Custom | `character-lifecycle-death` | Published when a character dies and death processi... |
| `CharacterLifecycleDivorceEvent` | Custom | `character-lifecycle-divorce` | Published when a marriage ends |
| `CharacterLifecycleDyingEvent` | Custom | `character-lifecycle-dying` | Published when a character enters the dying state |
| `CharacterLifecycleInheritanceProcessedEvent` | Custom | `character-lifecycle-inheritance-processed` | Published when inheritance is distributed after de... |
| `CharacterLifecycleMarriageEvent` | Custom | `character-lifecycle-marriage` | Published when two characters marry |
| `CharacterLifecycleStageChangedEvent` | Custom | `character-lifecycle-stage-changed` | Published when a character transitions lifecycle s... |
| `CharacterLifecycleTraitExpressedEvent` | Custom | `character-lifecycle-trait-expressed` | Published when a latent heritage trait activates (... |

### Character Personality

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `CombatPreferencesEvolvedEvent` | Custom | `combat-preferences-evolved` | Published when a character's combat preferences ev... |
| `PersonalityEvolvedEvent` | Custom | `personality-evolved` | Published when a character's personality evolves d... |

### Chat

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `ChatMessageDeletedEvent` | Lifecycle (Deleted) | `chat-message.deleted` | Published when a message is deleted from a chat ro... |
| `ChatMessageSentEvent` | Custom | `chat-message-sent` | Published when a message is sent. Contains metadat... |
| `ChatParticipantBannedEvent` | Custom | `chat-participant-banned` | Published when a participant is banned from a chat... |
| `ChatParticipantJoinedEvent` | Custom | `chat-participant-joined` | Published when a participant joins a chat room |
| `ChatParticipantKickedEvent` | Custom | `chat-participant-kicked` | Published when a participant is kicked from a chat... |
| `ChatParticipantLeftEvent` | Custom | `chat-participant-left` | Published when a participant leaves a chat room |
| `ChatParticipantMutedEvent` | Custom | `chat-participant-muted` | Published when a participant is muted in a chat ro... |
| `ChatParticipantRoleChangedEvent` | Custom | `chat-participant-role-changed` | Published when a participant's role changes (manua... |
| `ChatParticipantUnbannedEvent` | Custom | `chat-participant-unbanned` | Published when a participant is unbanned from a ch... |
| `ChatParticipantUnmutedEvent` | Custom | `chat-participant-unmuted` | Published when a participant is unmuted in a chat ... |
| `ChatRoomArchivedEvent` | Custom | `chat-room-archived` | Published when a chat room is archived via Resourc... |
| `ChatRoomLockedEvent` | Custom | `chat-room-locked` | Published when a chat room is locked (contract-tri... |

### Collection

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `CollectionDiscoveryAdvancedEvent` | Custom | `collection-discovery-advanced` | Published when progressive discovery level is adva... |
| `CollectionEntryGrantFailedEvent` | Custom | `collection-entry-grant-failed` | Published when a grant attempt fails |
| `CollectionEntryUnlockedEvent` | Custom | `collection-entry-unlocked` | Published when an entry is successfully unlocked i... |
| `CollectionMilestoneReachedEvent` | Custom | `collection-milestone-reached` | Published when a completion milestone is reached (... |

### Contract

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `ClauseTypeRegisteredEvent` | Registration | `clause-type.registered` | Event published when a new clause type is register... |
| `ContractAcceptedEvent` | Custom | `contract-accepted` | Event published when all required parties consent |
| `ContractActivatedEvent` | Custom | `contract-activated` | Event published when contract becomes active |
| `ContractBreachCuredEvent` | Custom | `contract-breach-cured` | Event published when a breach is cured |
| `ContractBreachDetectedEvent` | Custom | `contract-breach-detected` | Event published when a breach is recorded |
| `ContractConsentReceivedEvent` | Custom | `contract-consent-received` | Event published when one party consents |
| `ContractExecutedEvent` | Custom | `contract-executed` | Event published when contract clauses are executed... |
| `ContractExpiredEvent` | Expiration | `contract.expired` | Event published when contract reaches natural expi... |
| `ContractFulfilledEvent` | Custom | `contract-fulfilled` | Event published when all required milestones compl... |
| `ContractLockedEvent` | Custom | `contract-locked` | Event published when a contract is locked under gu... |
| `ContractMilestoneCompletedEvent` | Custom | `contract-milestone-completed` | Event published when a milestone is completed |
| `ContractMilestoneFailedEvent` | Custom | `contract-milestone-failed` | Event published when a milestone fails |
| `ContractPartyTransferredEvent` | Custom | `contract-party-transferred` | Event published when a party role is transferred t... |
| `ContractPaymentDueEvent` | Custom | `contract-payment-due` | Event published when a contract payment is due bas... |
| `ContractPreboundApiExecutedEvent` | Custom | `contract-prebound-api-executed` | Event published when a prebound API is executed |
| `ContractPreboundApiFailedEvent` | Custom | `contract-prebound-api-failed` | Event published when a prebound API call fails |
| `ContractPreboundApiValidationFailedEvent` | Custom | `contract-prebound-api-validation-failed` | Event published when a prebound API response fails... |
| `ContractProposedEvent` | Custom | `contract-proposed` | Event published when a contract is proposed to par... |
| `ContractTemplateValuesSetEvent` | Custom | `contract-template-values-set` | Event published when template values are set on a ... |
| `ContractTerminatedEvent` | Custom | `contract-terminated` | Event published when contract is terminated early |
| `ContractUnlockedEvent` | Custom | `contract-unlocked` | Event published when a contract is released from g... |

### Craft

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `CraftProficiencyGainedEvent` | Custom | `craft-proficiency-gained` | Published when an entity gains crafting experience... |
| `CraftProficiencyLeveledEvent` | Custom | `craft-proficiency-leveled` | Published when an entity's proficiency level incre... |
| `CraftRecipeDiscoveredEvent` | Custom | `craft-recipe-discovered` | Published when an entity discovers or is taught a ... |
| `CraftSessionCancelledEvent` | Custom | `craft-session-cancelled` | Published when a session is cancelled and material... |
| `CraftSessionCompletedEvent` | Custom | `craft-session-completed` | Published when a crafting session finishes success... |
| `CraftSessionFailedEvent` | Custom | `craft-session-failed` | Published when a session fails due to contract ter... |
| `CraftSessionStartedEvent` | Custom | `craft-session-started` | Published when a crafting session begins with vali... |
| `CraftSessionStepCompletedEvent` | Custom | `craft-session-step-completed` | Published when a session step is completed |

### Currency

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `CurrencyAutogainCalculatedEvent` | Custom | `currency-autogain-calculated` | Event published when autogain is calculated for a ... |
| `CurrencyCreditedEvent` | Custom | `currency-credited` | Event published when currency is credited to a wal... |
| `CurrencyDebitedEvent` | Custom | `currency-debited` | Event published when currency is debited from a wa... |
| `CurrencyEarnCapReachedEvent` | Custom | `currency-earn-cap-reached` | Event published when a credit is limited by earn c... |
| `CurrencyExchangeRateUpdatedEvent` | Lifecycle (Updated) | `currency-exchange-rate.updated` | Event published when a currency exchange rate is u... |
| `CurrencyExpiredEvent` | Expiration | `currency.expired` | Event published when currency expires |
| `CurrencyHoldCapturedEvent` | Custom | `currency-hold-captured` | Event published when a hold is captured |
| `CurrencyHoldCreatedEvent` | Lifecycle (Created) | `currency-hold.created` | Event published when an authorization hold is crea... |
| `CurrencyHoldExpiredEvent` | Expiration | `currency-hold.expired` | Event published when a hold expires (auto-release) |
| `CurrencyHoldReleasedEvent` | Custom | `currency-hold-released` | Event published when a hold is released |
| `CurrencyTransferredEvent` | Custom | `currency-transferred` | Event published when currency is transferred betwe... |
| `CurrencyWalletCapReachedEvent` | Custom | `currency-wallet-cap-reached` | Event published when a credit hits the wallet cap |
| `CurrencyWalletClosedEvent` | Custom | `currency-wallet-closed` | Event published when a wallet is permanently close... |
| `CurrencyWalletFrozenEvent` | Custom | `currency-wallet-frozen` | Event published when a wallet is frozen |
| `CurrencyWalletUnfrozenEvent` | Custom | `currency-wallet-unfrozen` | Event published when a wallet is unfrozen |

### Divine

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `DivineBlessingGrantedEvent` | Custom | `divine-blessing-granted` | Published when a god grants a blessing to an entit... |
| `DivineBlessingRevokedEvent` | Expiration | `divine-blessing.revoked` | Published when a blessing is revoked |
| `DivineDeityActivatedEvent` | Custom | `divine-deity-activated` | Published when a deity becomes active in the world |
| `DivineDeityDormantEvent` | Custom | `divine-deity-dormant` | Published when a deity goes dormant |
| `DivineDivinityCreditedEvent` | Custom | `divine-divinity-credited` | Published when divinity is earned by a deity from ... |
| `DivineDivinityDebitedEvent` | Custom | `divine-divinity-debited` | Published when divinity is spent by a deity for bl... |
| `DivineFollowerRegisteredEvent` | Registration | `divine-follower.registered` | Published when a character becomes a follower of a... |
| `DivineFollowerRemovedEvent` | Custom | `divine-follower-removed` | Published when a character is removed as a followe... |

### Documentation

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `DocumentationArchiveCreatedEvent` | Lifecycle (Created) | `documentation-archive.created` | Published when a documentation archive is created |
| `DocumentationArchiveDeletedEvent` | Lifecycle (Deleted) | `documentation-archive.deleted` | Published when a documentation archive is deleted |
| `DocumentationBindingCreatedEvent` | Lifecycle (Created) | `documentation-binding.created` | Published when a repository binding is created |
| `DocumentationBindingRemovedEvent` | Custom | `documentation-binding-removed` | Published when a repository binding is removed |
| `DocumentationQueriedEvent` | Custom | `documentation-queried` | Published when documentation is queried with natur... |
| `DocumentationSearchedEvent` | Custom | `documentation-searched` | Published when documentation is searched with keyw... |
| `DocumentationSyncCompletedEvent` | Custom | `documentation-sync-completed` | Published when a repository sync completes |
| `DocumentationSyncStartedEvent` | Custom | `documentation-sync-started` | Published when a repository sync starts |

### Environment

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `EnvironmentConditionsChangedEvent` | Custom | `environment-conditions-changed` | Published when environmental conditions change at ... |
| `EnvironmentResourceAvailabilityChangedEvent` | Custom | `environment-resource-availability-changed` | Published when resource availability crosses the s... |
| `EnvironmentWeatherEventEndedEvent` | Custom | `environment-weather-event-ended` | Published when a weather event is cancelled, sourc... |
| `EnvironmentWeatherEventStartedEvent` | Custom | `environment-weather-event-started` | Published when a weather event override is created |

### Escrow

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `EscrowCancelledEvent` | Custom | `escrow-cancelled` | Event published when escrow is cancelled |
| `EscrowConsentReceivedEvent` | Custom | `escrow-consent-received` | Event published when a party consents |
| `EscrowCreatedEvent` | Lifecycle (Created) | `escrow.created` | Event published when a new escrow agreement is cre... |
| `EscrowDepositReceivedEvent` | Custom | `escrow-deposit-received` | Event published when a deposit is received |
| `EscrowDisputedEvent` | Custom | `escrow-disputed` | Event published when a party raises a dispute |
| `EscrowExpiredEvent` | Expiration | `escrow.expired` | Event published when escrow times out |
| `EscrowFinalizingEvent` | Custom | `escrow-finalizing` | Event published when finalization begins |
| `EscrowFundedEvent` | Custom | `escrow-funded` | Event published when all expected deposits are rec... |
| `EscrowRefundedEvent` | Custom | `escrow-refunded` | Event published when assets are refunded |
| `EscrowRefundingEvent` | Custom | `escrow-refunding` | Event published when escrow transitions to Refundi... |
| `EscrowReleasedEvent` | Custom | `escrow-released` | Event published when assets are released |
| `EscrowReleasingEvent` | Custom | `escrow-releasing` | Event published when escrow transitions to Releasi... |
| `EscrowResolvedEvent` | Custom | `escrow-resolved` | Event published when an arbiter resolves a dispute |
| `EscrowValidationFailedEvent` | Custom | `escrow-validation-failed` | Event published when validation detects asset chan... |
| `EscrowValidationReaffirmedEvent` | Custom | `escrow-validation-reaffirmed` | Event published when a party reaffirms after valid... |

### Faction

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `FactionAuthorityDelegatedEvent` | Custom | `faction-authority-delegated` | Published when a sovereign faction delegates autho... |
| `FactionAuthorityRevokedEvent` | Expiration | `faction-authority.revoked` | Published when delegated authority is revoked from... |
| `FactionGovernanceDefinedEvent` | Custom | `faction-governance-defined` | Published when a governance entry is created or up... |
| `FactionGovernanceDeletedEvent` | Lifecycle (Deleted) | `faction-governance.deleted` | Published when a governance entry is removed from ... |
| `FactionMemberAddedEvent` | Custom | `faction-member-added` | Published when a character joins a faction |
| `FactionMemberRemovedEvent` | Custom | `faction-member-removed` | Published when a character leaves or is removed fr... |
| `FactionMemberRoleChangedEvent` | Custom | `faction-member-role-changed` | Published when a member's role is updated |
| `FactionNormDefinedEvent` | Custom | `faction-norm-defined` | Published when a new behavioral norm is defined fo... |
| `FactionNormDeletedEvent` | Lifecycle (Deleted) | `faction-norm.deleted` | Published when a norm definition is removed from a... |
| `FactionNormUpdatedEvent` | Lifecycle (Updated) | `faction-norm.updated` | Published when a norm definition is modified |
| `FactionRealmBaselineDesignatedEvent` | Custom | `faction-realm-baseline-designated` | Published when a faction is designated as the real... |
| `FactionTerritoryClaimedEvent` | Custom | `faction-territory-claimed` | Published when a faction claims a location as terr... |
| `FactionTerritoryReleasedEvent` | Custom | `faction-territory-released` | Published when a faction releases a territory clai... |

### Game Session

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `GameSessionActionPerformedEvent` | Custom | `game-session-action-performed` | Published when a game action is performed in a ses... |
| `GameSessionCancelledEvent` | Custom | `game-session-cancelled` | Published when a matchmade session is cancelled du... |
| `GameSessionPlayerJoinedEvent` | Custom | `game-session-player-joined` | Published when a player joins a game session |
| `GameSessionPlayerLeftEvent` | Custom | `game-session-player-left` | Published when a player leaves a game session |

### Gardener

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `GardenerBondEnteredTogetherEvent` | Custom | `gardener-bond-entered-together` | Published when bonded players enter a scenario tog... |
| `GardenerGardenEnteredEvent` | Custom | `gardener-garden-entered` | Published when a player enters the garden |
| `GardenerGardenLeftEvent` | Custom | `gardener-garden-left` | Published when a player leaves the garden |
| `GardenerPhaseChangedEvent` | Custom | `gardener-phase-changed` | Published when the deployment phase changes |
| `GardenerPoiDeclinedEvent` | Custom | `gardener-poi-declined` | Published when a player declines a POI |
| `GardenerPoiEnteredEvent` | Custom | `gardener-poi-entered` | Published when a player enters a POI or triggers a... |
| `GardenerPoiExpiredEvent` | Expiration | `gardener-poi.expired` | Published when a POI expires without player intera... |
| `GardenerPoiSpawnedEvent` | Custom | `gardener-poi-spawned` | Published when a POI spawns in a garden instance |
| `GardenerScenarioAbandonedEvent` | Custom | `gardener-scenario-abandoned` | Published when a scenario is abandoned |
| `GardenerScenarioChainedEvent` | Custom | `gardener-scenario-chained` | Published when a player chains from one scenario t... |
| `GardenerScenarioCompletedEvent` | Custom | `gardener-scenario-completed` | Published when a scenario is completed with growth... |
| `GardenerScenarioStartedEvent` | Custom | `gardener-scenario-started` | Published when a scenario instance is created |

### Inventory

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `InventoryContainerFullEvent` | Custom | `inventory-container-full` | Event when container reaches capacity |
| `InventoryItemMovedEvent` | Custom | `inventory-item-moved` | Event when an item moves between slots or containe... |
| `InventoryItemPlacedEvent` | Custom | `inventory-item-placed` | Event when an item is placed in a container |
| `InventoryItemRemovedEvent` | Custom | `inventory-item-removed` | Event when an item is removed from a container |
| `InventoryItemSplitEvent` | Custom | `inventory-item-split` | Event when a stack is split |
| `InventoryItemStackedEvent` | Custom | `inventory-item-stacked` | Event when items are stacked together |
| `InventoryItemTransferredEvent` | Custom | `inventory-item-transferred` | Event when item ownership transfers |

### Item

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `ItemInstanceBoundEvent` | Custom | `item-instance-bound` | Event published when an item is bound to a charact... |
| `ItemInstanceUnboundEvent` | Custom | `item-instance-unbound` | Event published when an item binding is removed (a... |
| `ItemUseFailedEvent` | Custom | `item-use-failed` | Published when item use attempts fail, batched wit... |
| `ItemUseStepCompletedEvent` | Custom | `item-use-step-completed` | Published when a multi-step item use milestone is ... |
| `ItemUseStepFailedEvent` | Custom | `item-use-step-failed` | Published when a multi-step item use milestone fai... |
| `ItemUsedEvent` | Custom | `item-used` | Published when items are successfully used, batche... |

### Leaderboard

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `LeaderboardEntryAddedEvent` | Custom | `leaderboard-entry-added` | Published when a new entity joins a leaderboard |
| `LeaderboardRankChangedEvent` | Custom | `leaderboard-rank-changed` | Published when an entity's rank changes on a leade... |
| `LeaderboardSeasonStartedEvent` | Custom | `leaderboard-season-started` | Published when a new leaderboard season begins |

### License

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `LicenseBoardClonedEvent` | Custom | `license-board-cloned` | Published when a board's unlock state is cloned to... |
| `LicenseUnlockFailedEvent` | Custom | `license-unlock-failed` | Published when a license unlock attempt fails |
| `LicenseUnlockedEvent` | Custom | `license-unlocked` | Published when a license is successfully unlocked ... |

### Location

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `LocationEntityArrivedEvent` | Custom | `location-entity-arrived` | Published when an entity arrives at a location |
| `LocationEntityDepartedEvent` | Custom | `location-entity-departed` | Published when an entity departs from a location |

### Mapping

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `MapIngestEvent` | Custom | `map-ingest` | Event published by authority to ingest topic for h... |
| `MapObjectsChangedEvent` | Custom | `map-objects-changed` | Published when metadata objects change in a map. |
| `MapSnapshotEvent` | Custom | `map-snapshot` | Published when a full snapshot is available. |
| `MapUpdatedEvent` | Lifecycle (Updated) | `map.updated` | Published when map layer data changes. |
| `MappingAuthorityExpiredEvent` | Expiration | `mapping-authority.expired` | Published when authority expires (detected during ... |
| `MappingAuthorityGrantedEvent` | Custom | `mapping-authority-granted` | Published when authority is granted over a mapping... |
| `MappingAuthorityReleasedEvent` | Custom | `mapping-authority-released` | Published when authority is explicitly released ov... |

### Matchmaking

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `MatchmakingMatchAcceptedEvent` | Custom | `matchmaking-match-accepted` | Published when all players accept a match |
| `MatchmakingMatchDeclinedEvent` | Custom | `matchmaking-match-declined` | Published when a player declines a match or match ... |
| `MatchmakingMatchFormedEvent` | Custom | `matchmaking-match-formed` | Published when a match is successfully formed from... |
| `MatchmakingStatsEvent` | Custom | `matchmaking-stats` | Published periodically with queue statistics for m... |
| `MatchmakingTicketCancelledEvent` | Custom | `matchmaking-ticket-cancelled` | Published when a matchmaking ticket is cancelled |
| `MatchmakingTicketCreatedEvent` | Lifecycle (Created) | `matchmaking-ticket.created` | Published when a new matchmaking ticket is created |
| `MatchmakingTicketUpdatedEvent` | Lifecycle (Updated) | `matchmaking-ticket.updated` | Published when a matchmaking ticket status changes |

### Mesh

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `MeshCircuitStateChangedEvent` | Custom | `mesh-circuit-state-changed` | Published when circuit breaker state changes for a... |
| `MeshEndpointDegradedEvent` | Custom | `mesh-endpoint-degraded` | Published when an endpoint transitions to Degraded... |
| `MeshEndpointDeregisteredEvent` | Registration | `mesh-endpoint-deregistered` | Published when an endpoint is removed from the ser... |
| `MeshEndpointHealthCheckFailedEvent` | Custom | `mesh-endpoint-health-check-failed` | Published when a health check probe fails (before ... |
| `MeshEndpointRegisteredEvent` | Registration | `mesh-endpoint.registered` | Published when a new endpoint is registered in the... |
| `MeshMappingsUpdatedEvent` | Lifecycle (Updated) | `mesh-mappings.updated` | Published by Mesh when service-to-appId mappings a... |

### Obligation

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `ObligationCacheRebuiltEvent` | Custom | `obligation-cache-rebuilt` | Published when a character's obligation cache is r... |
| `ObligationViolationReportedEvent` | Custom | `obligation-violation-reported` | Published when a character knowingly violates an o... |

### Orchestrator

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `ConfigurationChangedEvent` | Custom | `configuration-changed` | Published when configuration or secrets change. Al... |
| `DeploymentEvent` | Custom | `deployment` | Published when deployment state changes. Topic ban... |
| `OrchestratorHealthPingEvent` | Custom | `orchestrator-health-ping` | Simple health ping event published to verify pub/s... |
| `ProcessorReleasedEvent` | Custom | `processor-released` | Published when a processor is released back to the... |
| `ServiceRestartEvent` | Custom | `service-restart` | Published when a service is restarted. Topic banno... |

### Puppetmaster

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `BehaviorInvalidatedEvent` | Custom | `behavior.invalidated` | Event published when behavior documents are invali... |
| `WatcherStartedEvent` | Custom | `watcher-started` | Event published when a regional watcher is started |
| `WatcherStoppedEvent` | Custom | `watcher-stopped` | Event published when a regional watcher is stopped |

### Quest

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `QuestAbandonedEvent` | Custom | `quest-abandoned` | Published when quest is voluntarily abandoned |
| `QuestAcceptedEvent` | Custom | `quest-accepted` | Published when a character accepts a quest |
| `QuestCompletedEvent` | Custom | `quest-completed` | Published when quest is completed successfully |
| `QuestFailedEvent` | Custom | `quest-failed` | Published when quest fails |
| `QuestObjectiveProgressedEvent` | Custom | `quest-objective-progressed` | Published when objective progress changes |

### Realm

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `RealmMergedEvent` | Custom | `realm-merged` | Published when a deprecated realm is merged into a... |

### Realm History

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `RealmHistoryDeletedEvent` | Lifecycle (Deleted) | `realm-history.deleted` | Published when all history data for a realm is del... |
| `RealmLoreCreatedEvent` | Lifecycle (Created) | `realm-lore.created` | Published when a realm's lore is first created |
| `RealmLoreDeletedEvent` | Lifecycle (Deleted) | `realm-lore.deleted` | Published when a realm's lore is deleted |
| `RealmLoreUpdatedEvent` | Lifecycle (Updated) | `realm-lore.updated` | Published when a realm's lore is updated |
| `RealmParticipationDeletedEvent` | Lifecycle (Deleted) | `realm-participation.deleted` | Published when a realm's participation record is d... |
| `RealmParticipationRecordedEvent` | Custom | `realm-participation-recorded` | Published when a realm's participation in a histor... |

### Relationship

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `RelationshipTypeMergedEvent` | Custom | `relationship-type-merged` | Published when a relationship type merge operation... |

### Resource

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `ResourceCleanupCallbackFailedEvent` | Custom | `resource-cleanup-callback-failed` | Published when a cleanup callback fails during res... |
| `ResourceCompressCallbackFailedEvent` | Custom | `resource-compress-callback-failed` | Published when a compression callback fails. |
| `ResourceCompressedEvent` | Custom | `resource-compressed` | Published when compression completes successfully. |
| `ResourceDecompressedEvent` | Custom | `resource-decompressed` | Published when decompression completes with at lea... |
| `ResourceGracePeriodStartedEvent` | Custom | `resource-grace-period-started` | Published when a resource's reference count reache... |
| `ResourceMigrateCallbackFailedEvent` | Custom | `resource-migrate-callback-failed` | Published when a migration callback fails during r... |
| `ResourceMigratedEvent` | Custom | `resource-migrated` | Published when a resource migration completes succ... |
| `ResourceSnapshotCreatedEvent` | Lifecycle (Created) | `resource-snapshot.created` | Published when an ephemeral snapshot of a living r... |

### Save Load

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `CircuitBreakerStateChangedEvent` | Custom | `circuit-breaker-state-changed` | Published when storage circuit breaker changes sta... |
| `CleanupCompletedEvent` | Custom | `cleanup-completed` | Published when automatic cleanup completes |
| `SaveCreatedEvent` | Lifecycle (Created) | `save.created` | Published when a new save version is created |
| `SaveLoadedEvent` | Custom | `save-loaded` | Published when a save is loaded |
| `SaveMigratedEvent` | Custom | `save-migrated` | Published when a save is migrated to a new schema ... |
| `SaveQueuedEvent` | Custom | `save-queued` | Published when a save is queued for async upload. ... |
| `SaveUploadCompletedEvent` | Custom | `save-upload-completed` | Published when async upload to MinIO completes suc... |
| `SaveUploadFailedEvent` | Custom | `save-upload-failed` | Published when async upload fails after all retry ... |
| `VersionDeletedEvent` | Lifecycle (Deleted) | `version.deleted` | Published when a version is deleted |
| `VersionPinnedEvent` | Custom | `version-pinned` | Published when a version is pinned as checkpoint |
| `VersionUnpinnedEvent` | Custom | `version-unpinned` | Published when a version is unpinned |

### Scene

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `SceneCheckedOutEvent` | Custom | `scene-checked-out` | Published when a scene is locked for editing |
| `SceneCheckoutDiscardedEvent` | Custom | `scene-checkout-discarded` | Published when a checkout is discarded without sav... |
| `SceneCommittedEvent` | Custom | `scene-committed` | Published when checkout changes are committed |
| `SceneDestroyedEvent` | Custom | `scene-destroyed` | Published when a scene instance is removed from th... |
| `SceneInstantiatedEvent` | Custom | `scene-instantiated` | Published when a scene is instantiated in the game... |
| `SceneValidationRulesUpdatedEvent` | Lifecycle (Updated) | `scene-validation-rules.updated` | Published when validation rules are registered or ... |

### Seed

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `SeedActivatedEvent` | Custom | `seed-activated` | Published when a seed is set as active for its own... |
| `SeedArchivedEvent` | Custom | `seed-archived` | Published when a seed is archived |
| `SeedBondFormedEvent` | Custom | `seed-bond-formed` | Published when a bond between seeds becomes active... |
| `SeedCapabilityUpdatedEvent` | Lifecycle (Updated) | `seed-capability.updated` | Published when a seed's capability manifest is rec... |
| `SeedGrowthTransferredEvent` | Custom | `seed-growth-transferred` | Published when growth is transferred between seeds... |
| `SeedGrowthUpdatedEvent` | Lifecycle (Updated) | `seed-growth.updated` | Published when growth domain values change for a s... |
| `SeedPhaseChangedEvent` | Custom | `seed-phase-changed` | Published when a seed transitions to a new growth ... |

### Species

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `SpeciesMergedEvent` | Custom | `species-merged` | Published when two species are merged, with the so... |

### State

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `StateMigrationCompletedEvent` | Custom | `state-migration-completed` | Published when a state store migration finishes su... |
| `StateMigrationFailedEvent` | Custom | `state-migration-failed` | Published when a state store migration encounters ... |
| `StateMigrationStartedEvent` | Custom | `state-migration-started` | Published when a state store migration begins exec... |

### Status

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `StatusCleansedEvent` | Custom | `status-cleansed` | Published when statuses are removed by category cl... |
| `StatusExpiredEvent` | Expiration | `status.expired` | Published when a status effect expires via TTL or ... |
| `StatusGrantFailedEvent` | Custom | `status-grant-failed` | Published when a grant attempt is rejected |
| `StatusGrantedEvent` | Custom | `status-granted` | Published when a status effect is successfully app... |
| `StatusRemovedEvent` | Custom | `status-removed` | Published when a status effect is removed from an ... |
| `StatusStackedEvent` | Custom | `status-stacked` | Published when a status effect stack count changes |

### Storyline

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `ScenarioAvailableEvent` | Custom | `scenario-available` | Event published when new scenarios become availabl... |
| `ScenarioCompletedEvent` | Custom | `scenario-completed` | Event published when a scenario finishes successfu... |
| `ScenarioFailedEvent` | Custom | `scenario-failed` | Event published when a scenario fails during execu... |
| `ScenarioPhaseCompletedEvent` | Custom | `scenario-phase-completed` | Event published when a phase within a multi-phase ... |
| `ScenarioTriggeredEvent` | Custom | `scenario-triggered` | Event published when a scenario starts executing f... |
| `StorylinePlanComposedEvent` | Custom | `storyline-plan-composed` | Event published when a storyline plan is generated |

### Subscription

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `SubscriptionUpdatedEvent` | Lifecycle (Updated) | `subscription.updated` | Published when a subscription changes state (creat... |

### Transit

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `TransitConnectionStatusChangedEvent` | Custom | `transit-connection-status-changed` | Published when a connection's operational status c... |
| `TransitDiscoveryRevealedEvent` | Custom | `transit-discovery-revealed` | Published when a discoverable connection is reveal... |
| `TransitJourneyAbandonedEvent` | Custom | `transit-journey-abandoned` | Published when a journey is abandoned before reach... |
| `TransitJourneyArrivedEvent` | Custom | `transit-journey-arrived` | Published when a traveling entity reaches its dest... |
| `TransitJourneyDepartedEvent` | Custom | `transit-journey-departed` | Published when an entity begins traveling |
| `TransitJourneyInterruptedEvent` | Custom | `transit-journey-interrupted` | Published when a journey is interrupted (combat, e... |
| `TransitJourneyResumedEvent` | Custom | `transit-journey-resumed` | Published when an interrupted journey is resumed |
| `TransitJourneyWaypointReachedEvent` | Custom | `transit-journey-waypoint-reached` | Published when a traveling entity reaches an inter... |

### Voice

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `VoiceBroadcastApprovedEvent` | Custom | `voice-broadcast-approved` | All participants consented to broadcasting. lib-st... |
| `VoiceBroadcastDeclinedEvent` | Custom | `voice-broadcast-declined` | A participant declined broadcast consent |
| `VoiceBroadcastStoppedEvent` | Custom | `voice-broadcast-stopped` | Broadcasting stopped |
| `VoicePeerJoinedEvent` | Custom | `voice-peer-joined` | Published when a peer joins a room |
| `VoicePeerLeftEvent` | Custom | `voice-peer-left` | Published when a peer leaves a room |
| `VoiceRoomCreatedEvent` | Lifecycle (Created) | `voice-room.created` | Published when a voice room is created |
| `VoiceRoomDeletedEvent` | Lifecycle (Deleted) | `voice-room.deleted` | Published when a voice room is deleted |
| `VoiceRoomTierUpgradedEvent` | Custom | `voice-room-tier-upgraded` | Published when a room upgrades from P2P to scaled ... |

### Worldstate

| Event | Type | Likely Topic | Description |
|-------|------|--------------|-------------|
| `WorldstateClockAdvancedEvent` | Custom | `worldstate-clock-advanced` | Published when a realm clock is advanced via Advan... |
| `WorldstateDayChangedEvent` | Custom | `worldstate-day-changed` | Published when a game-day boundary is crossed. Dur... |
| `WorldstateHourChangedEvent` | Custom | `worldstate-hour-changed` | Published when a game-hour boundary is crossed dur... |
| `WorldstateMonthChangedEvent` | Custom | `worldstate-month-changed` | Published when a month boundary is crossed |
| `WorldstatePeriodChangedEvent` | Custom | `worldstate-period-changed` | Published when a day-period boundary is crossed (e... |
| `WorldstateRatioChangedEvent` | Custom | `worldstate-ratio-changed` | Published when a realm's time ratio is changed via... |
| `WorldstateRealmClockInitializedEvent` | Custom | `worldstate-realm-clock-initialized` | Published when a realm clock is initialized for th... |
| `WorldstateSeasonChangedEvent` | Custom | `worldstate-season-changed` | Published when a season boundary is crossed |
| `WorldstateYearChangedEvent` | Custom | `worldstate-year-changed` | Published when a year boundary is crossed |

## Event Types

| Type | Description | Example |
|------|-------------|---------|
| Lifecycle (Created) | Entity creation events from `x-lifecycle` | `AccountCreatedEvent` |
| Lifecycle (Updated) | Entity update events from `x-lifecycle` | `CharacterUpdatedEvent` |
| Lifecycle (Deleted) | Entity deletion events from `x-lifecycle` | `RelationshipDeletedEvent` |
| Session | WebSocket connection events | `SessionConnectedEvent` |
| Registration | Service/capability registration | `ServiceRegistrationEvent` |
| Health | Heartbeat and health status | `ServiceHeartbeatEvent` |
| Error | Error reporting events | `ServiceErrorEvent` |
| Expiration | Subscription/token expiration | `SubscriptionExpiredEvent` |
| Custom | Service-specific events | Varies by service |

## Topic Naming Convention

Events are published to topics following the pattern: `{entity}.{action}`

Examples:
- `account.created` - Account was created
- `session.invalidated` - Session was invalidated
- `character.updated` - Character was updated

All events use the `bannou-pubsub` pub/sub component.

## Lifecycle Events (x-lifecycle)

Lifecycle events are auto-generated from `x-lifecycle` definitions in API schemas.
They follow a consistent pattern:

- **Created**: Full entity data on creation
- **Updated**: Full entity data + `changedFields` array
- **Deleted**: Entity ID + `deletedReason`

See [TENETS.md](../TENETS.md#lifecycle-events-x-lifecycle) for usage details.

---

*This file is auto-generated. See [TENETS.md](../TENETS.md) for architectural context.*
