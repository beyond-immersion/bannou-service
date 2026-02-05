# Generated Client API Reference

> **Source**: `schemas/*-api.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists all typed proxy methods available in the Bannou Client SDK.

## Quick Reference

| Service | Proxy Property | Methods | Description |
|---------|---------------|---------|-------------|
| [Bannou Account Service API](#account) | `client.Account` | 16 | Internal account management service (CRUD operations only, n... |
| [Bannou Achievement Service API](#achievement) | `client.Achievement` | 11 | Achievement and trophy system with progress tracking and pla... |
| [Actor Service API](#actor) | `client.Actor` | 16 | Distributed actor management and execution for NPC brains, e... |
| [Bannou Analytics Service API](#analytics) | `client.Analytics` | 9 | Event ingestion, entity statistics, skill ratings (Glicko-2)... |
| [Asset Service API](#asset) | `client.Asset` | 20 | Asset management service for storage, versioning, and distri... |
| [Bannou Auth Service API](#auth) | `client.Auth` | 13 | Authentication and session management service (Internet-faci... |
| [ABML Behavior Management API](#behavior) | `client.Behavior` | 6 | Arcadia Behavior Markup Language (ABML) API for character be... |
| [Bannou Character Service API](#character) | `client.Character` | 12 | Character management service for game worlds. |
| [Bannou Character Encounter Service API](#character-encounter) | `client.CharacterEncounter` | 21 | Character encounter tracking service for memorable interacti... |
| [Bannou Character History Service API](#character-history) | `client.CharacterHistory` | 12 | Historical event participation and backstory management for ... |
| [Bannou Character Personality Service API](#character-personality) | `client.CharacterPersonality` | 12 | Machine-readable personality traits for NPC behavior decisio... |
| [Bannou Connect API](#connect) | `client.Connect` | 4 | Real-time communication and WebSocket connection management ... |
| [Contract Service API](#contract) | `client.Contract` | 30 | Binding agreements between entities with milestone-based pro... |
| [Currency Service API](#currency) | `client.Currency` | 32 | Multi-currency management service for game economies. |
| [Bannou Documentation API](#documentation) | `client.Documentation` | 25 | Knowledge base API for AI agents to query documentation. Des... |
| [Escrow Service API](#escrow) | `client.Escrow` | 22 | Full-custody orchestration layer for multi-party asset excha... |
| [Bannou Game Service API](#game-service) | `client.GameService` | 5 | Registry service for game services that users can subscribe ... |
| [Bannou Game Session Service API](#game-session) | `client.GameSession` | 11 | Minimal game session management for games. |
| [Inventory Service API](#inventory) | `client.Inventory` | 16 | Container and inventory management service for games. |
| [Item Service API](#item) | `client.Item` | 14 | Item template and instance management service. |
| [Bannou Leaderboard Service API](#leaderboard) | `client.Leaderboard` | 12 | Real-time leaderboard management using Redis Sorted Sets for... |
| [Bannou Location Service API](#location) | `client.Location` | 18 | Location management service for game worlds. |
| [Bannou Mapping Service API](#mapping) | `client.Mapping` | 18 | Spatial data management service for game worlds. |
| [Bannou Matchmaking Service API](#matchmaking) | `client.Matchmaking` | 11 | Matchmaking service for competitive and casual game matching... |
| [Bannou Mesh Service API](#mesh) | `client.Mesh` | 8 | Native service mesh plugin providing direct service-to-servi... |
| [Bannou Messaging Service API](#messaging) | `client.Messaging` | 4 | Native RabbitMQ pub/sub messaging with native serialization. |
| [Music Theory Engine API](#music) | `client.Music` | 8 | Pure computation music generation using formal music theory ... |
| [Orchestrator API](#orchestrator) | `client.Orchestrator` | 22 | Central intelligence for Bannou environment management and s... |
| [Bannou Permission System API](#permission) | `client.Permission` | 8 | Redis-backed high-performance permission system for WebSocke... |
| [Bannou Realm Service API](#realm) | `client.Realm` | 11 | Realm management service for game worlds. |
| [Bannou Realm History Service API](#realm-history) | `client.RealmHistory` | 12 | Historical event participation and lore management for realm... |
| [Relationship Service API](#relationship) | `client.Relationship` | 7 | Generic relationship management service for entity-to-entity... |
| [Bannou RelationshipType Service API](#relationship-type) | `client.RelationshipType` | 13 | Relationship type management service for game worlds. |
| [Resource Lifecycle API](#resource) | `client.Resource` | 15 | Resource reference tracking and lifecycle management. |
| [Save-Load Service API](#save-load) | `client.SaveLoad` | 26 | Generic save/load system for game state persistence. Support... |
| [Bannou Scene Service API](#scene) | `client.Scene` | 19 | Hierarchical composition storage for game worlds. |
| [Bannou Species Service API](#species) | `client.Species` | 13 | Species management service for game worlds. |
| [Bannou State Service API](#state) | `client.State` | 9 | Repository pattern state management with Redis and MySQL bac... |
| [Storyline Composer API](#storyline) | `client.Storyline` | 3 | Seeded narrative generation from compressed archives using t... |
| [Bannou Subscription Service API](#subscription) | `client.Subscription` | 7 | Manages user subscriptions to game services. Tracks which ac... |
| [Bannou Telemetry Service API](#telemetry) | `client.Telemetry` | 2 | Unified observability plugin providing distributed tracing, ... |
| [Bannou Voice Service API](#voice) | `client.Voice` | 7 | Voice communication coordination service for P2P and room-ba... |
| [Bannou Website Service API](#website) | `client.Website` | 12 | Public-facing website service for registration, information,... |

---

## Usage Pattern

```csharp
using BeyondImmersion.Bannou.Client;

var client = new BannouClient();
await client.ConnectWithTokenAsync(url, token);

// Use typed proxy methods
var response = await client.Auth.LoginAsync(new LoginRequest
{
    Email = "user@example.com",
    Password = "password"
});

if (response.IsSuccess)
{
    var token = response.Result.Token;
}
```

---

## Bannou Account Service API {#account}

**Proxy**: `client.Account` | **Version**: 2.0.0

Internal account management service (CRUD operations only, never exposed to internet).

### Account Lookup

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetAccountbyemailAsync` | `GetAccountByEmailRequest` | `AccountResponse` | Get account by email |
| `GetAccountbyproviderAsync` | `GetAccountByProviderRequest` | `AccountResponse` | Get account by external provider ID |

### Account Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ListAccountsAsync` | `ListAccountsRequest` | `AccountListResponse` | List accounts with filtering |
| `CreateAccountAsync` | `CreateAccountRequest` | `AccountResponse` | Create new account |
| `GetAccountAsync` | `GetAccountRequest` | `AccountResponse` | Get account by ID |
| `UpdateAccountAsync` | `UpdateAccountRequest` | `AccountResponse` | Update account |
| `DeleteAccountEventAsync` | `DeleteAccountRequest` | *(fire-and-forget)* | Delete account |
| `UpdatePasswordhashEventAsync` | `UpdatePasswordRequest` | *(fire-and-forget)* | Update account password hash |
| `BatchgetaccountsAsync` | `BatchGetAccountsRequest` | `BatchGetAccountsResponse` | Get multiple accounts by ID |
| `CountaccountsAsync` | `CountAccountsRequest` | `CountAccountsResponse` | Count accounts matching filters |
| `BulkupdaterolesAsync` | `BulkUpdateRolesRequest` | `BulkUpdateRolesResponse` | Bulk update roles for multiple accounts |
| `UpdateVerificationstatusEventAsync` | `UpdateVerificationRequest` | *(fire-and-forget)* | Update email verification status |

### Authentication Methods

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetAuthmethodsAsync` | `GetAuthMethodsRequest` | `AuthMethodsResponse` | Get authentication methods for account |
| `AddauthmethodAsync` | `AddAuthMethodRequest` | `AuthMethodResponse` | Add authentication method to account |
| `RemoveauthmethodEventAsync` | `RemoveAuthMethodRequest` | *(fire-and-forget)* | Remove authentication method from account |

### Profile Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `UpdateProfileAsync` | `UpdateProfileRequest` | `AccountResponse` | Update account profile |

---

## Bannou Achievement Service API {#achievement}

**Proxy**: `client.Achievement` | **Version**: 1.0.0

Achievement and trophy system with progress tracking and platform synchronization.

### Definitions

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateAchievementdefinitionAsync` | `CreateAchievementDefinitionRequest` | `AchievementDefinitionResponse` | Create a new achievement definition |
| `GetAchievementdefinitionAsync` | `GetAchievementDefinitionRequest` | `AchievementDefinitionResponse` | Get achievement definition |
| `ListAchievementdefinitionsAsync` | `ListAchievementDefinitionsRequest` | `ListAchievementDefinitionsResponse` | List achievement definitions |
| `UpdateAchievementdefinitionAsync` | `UpdateAchievementDefinitionRequest` | `AchievementDefinitionResponse` | Update achievement definition |
| `DeleteAchievementdefinitionEventAsync` | `DeleteAchievementDefinitionRequest` | *(fire-and-forget)* | Delete achievement definition |

### Platform Sync

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `SyncplatformachievementsAsync` | `SyncPlatformAchievementsRequest` | `SyncPlatformAchievementsResponse` | Manually trigger platform sync |
| `GetPlatformsyncstatusAsync` | `GetPlatformSyncStatusRequest` | `PlatformSyncStatusResponse` | Get platform sync status |

### Progress

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetAchievementprogressAsync` | `GetAchievementProgressRequest` | `AchievementProgressResponse` | Get entity's achievement progress |
| `UpdateAchievementprogressAsync` | `UpdateAchievementProgressRequest` | `UpdateAchievementProgressResponse` | Update achievement progress |
| `UnlockachievementAsync` | `UnlockAchievementRequest` | `UnlockAchievementResponse` | Directly unlock an achievement |
| `ListUnlockedachievementsAsync` | `ListUnlockedAchievementsRequest` | `ListUnlockedAchievementsResponse` | List unlocked achievements |

---

## Actor Service API {#actor}

**Proxy**: `client.Actor` | **Version**: 1.0.0

Distributed actor management and execution for NPC brains, event coordinators, and other long-running behavior loops. Actors output behavioral stat...

### Other

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateActortemplateAsync` | `CreateActorTemplateRequest` | `ActorTemplateResponse` | Create an actor template (category definition) |
| `GetActortemplateAsync` | `GetActorTemplateRequest` | `ActorTemplateResponse` | Get an actor template by ID or category |
| `ListActortemplatesAsync` | `ListActorTemplatesRequest` | `ListActorTemplatesResponse` | List all actor templates |
| `UpdateActortemplateAsync` | `UpdateActorTemplateRequest` | `ActorTemplateResponse` | Update an actor template |
| `DeleteActortemplateAsync` | `DeleteActorTemplateRequest` | `DeleteActorTemplateResponse` | Delete an actor template |
| `SpawnactorAsync` | `SpawnActorRequest` | `ActorInstanceResponse` | Spawn a new actor from a template |
| `GetActorAsync` | `GetActorRequest` | `ActorInstanceResponse` | Get actor instance (instantiate-on-access if template allows) |
| `StopactorAsync` | `StopActorRequest` | `StopActorResponse` | Stop a running actor |
| `CleanupbycharacterAsync` | `CleanupByCharacterRequest` | `CleanupByCharacterResponse` | Cleanup actors referencing a deleted character |
| `ListActorsAsync` | `ListActorsRequest` | `ListActorsResponse` | List actors with optional filters |
| `InjectperceptionAsync` | `InjectPerceptionRequest` | `InjectPerceptionResponse` | Inject a perception event into an actor's queue (testing) |
| `QueryoptionsAsync` | `QueryOptionsRequest` | `QueryOptionsResponse` | Query an actor for its available options |
| `StartencounterEventAsync` | `StartEncounterRequest` | *(fire-and-forget)* | Start an encounter managed by an Event Brain actor |
| `UpdateEncounterphaseAsync` | `UpdateEncounterPhaseRequest` | `UpdateEncounterPhaseResponse` | Update the phase of an active encounter |
| `EndencounterAsync` | `EndEncounterRequest` | `EndEncounterResponse` | End an active encounter |
| `GetEncounterAsync` | `GetEncounterRequest` | `GetEncounterResponse` | Get the current encounter state for an actor |

---

## Bannou Analytics Service API {#analytics}

**Proxy**: `client.Analytics` | **Version**: 1.0.0

Event ingestion, entity statistics, skill ratings (Glicko-2), and controller history tracking.

### Controller History

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `RecordcontrollereventEventAsync` | `RecordControllerEventRequest` | *(fire-and-forget)* | Record controller possession event |
| `QuerycontrollerhistoryAsync` | `QueryControllerHistoryRequest` | `QueryControllerHistoryResponse` | Query controller history |
| `CleanupcontrollerhistoryAsync` | `CleanupControllerHistoryRequest` | `CleanupControllerHistoryResponse` | Cleanup expired controller history |

### Event Ingestion

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `IngesteventAsync` | `IngestEventRequest` | `IngestEventResponse` | Ingest a single analytics event |
| `IngesteventbatchAsync` | `IngestEventBatchRequest` | `IngestEventBatchResponse` | Ingest multiple analytics events |

### Skill Ratings

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetSkillratingAsync` | `GetSkillRatingRequest` | `SkillRatingResponse` | Get entity Glicko-2 skill rating |
| `UpdateSkillratingAsync` | `UpdateSkillRatingRequest` | `UpdateSkillRatingResponse` | Update entity skill rating after match |

### Statistics

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetEntitysummaryAsync` | `GetEntitySummaryRequest` | `EntitySummaryResponse` | Get entity statistics summary |
| `QueryentitysummariesAsync` | `QueryEntitySummariesRequest` | `QueryEntitySummariesResponse` | Query entity summaries with filters |

---

## Asset Service API {#asset}

**Proxy**: `client.Asset` | **Version**: 1.0.0

Asset management service for storage, versioning, and distribution of large binary assets.

### Assets

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `RequestuploadAsync` | `UploadRequest` | `UploadResponse` | Request upload URL for a new asset |
| `CompleteuploadAsync` | `CompleteUploadRequest` | `AssetMetadata` | Mark upload as complete, trigger processing |
| `GetAssetAsync` | `GetAssetRequest` | `AssetWithDownloadUrl` | Get asset metadata and download URL |
| `DeleteAssetAsync` | `DeleteAssetRequest` | `DeleteAssetResponse` | Delete an asset |
| `ListAssetversionsAsync` | `ListVersionsRequest` | `AssetVersionList` | List all versions of an asset |
| `SearchassetsAsync` | `AssetSearchRequest` | `AssetSearchResult` | Search assets by tags, type, or realm |
| `BulkgetassetsAsync` | `BulkGetAssetsRequest` | `BulkGetAssetsResponse` | Batch asset metadata lookup |

### Bundles

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateBundleAsync` | `CreateBundleRequest` | `CreateBundleResponse` | Create asset bundle from multiple assets |
| `GetBundleAsync` | `GetBundleRequest` | `BundleWithDownloadUrl` | Get bundle manifest and download URL |
| `RequestbundleuploadAsync` | `BundleUploadRequest` | `UploadResponse` | Request upload URL for a pre-made bundle |
| `CreateMetabundleAsync` | `CreateMetabundleRequest` | `CreateMetabundleResponse` | Create metabundle from source bundles |
| `GetJobstatusAsync` | `GetJobStatusRequest` | `GetJobStatusResponse` | Get async metabundle job status |
| `CanceljobAsync` | `CancelJobRequest` | `CancelJobResponse` | Cancel an async metabundle job |
| `ResolvebundlesAsync` | `ResolveBundlesRequest` | `ResolveBundlesResponse` | Compute optimal bundles for requested assets |
| `QuerybundlesbyassetAsync` | `QueryBundlesByAssetRequest` | `QueryBundlesByAssetResponse` | Find all bundles containing a specific asset |
| `UpdateBundleAsync` | `UpdateBundleRequest` | `UpdateBundleResponse` | Update bundle metadata |
| `DeleteBundleAsync` | `DeleteBundleRequest` | `DeleteBundleResponse` | Soft-delete a bundle |
| `RestorebundleAsync` | `RestoreBundleRequest` | `RestoreBundleResponse` | Restore a soft-deleted bundle |
| `QuerybundlesAsync` | `QueryBundlesRequest` | `QueryBundlesResponse` | Query bundles with advanced filters |
| `ListBundleversionsAsync` | `ListBundleVersionsRequest` | `ListBundleVersionsResponse` | List version history for a bundle |

---

## Bannou Auth Service API {#auth}

**Proxy**: `client.Auth` | **Version**: 4.0.0

Authentication and session management service (Internet-facing).

### Authentication

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `LoginAsync` | `LoginRequest` | `AuthResponse` | Login with email/password |
| `RegisterAsync` | `RegisterRequest` | `RegisterResponse` | Register new user account |
| `LogoutEventAsync` | `LogoutRequest` | *(fire-and-forget)* | Logout and invalidate tokens |
| `ListProvidersAsync` | - | `ProvidersResponse` | List available authentication providers |

### OAuth

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CompleteoauthAsync` | `OAuthCallbackRequest` | `AuthResponse` | Complete OAuth2 flow (browser redirect callback) |

### Password

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `RequestpasswordresetEventAsync` | `PasswordResetRequest` | *(fire-and-forget)* | Request password reset |
| `ConfirmpasswordresetEventAsync` | `PasswordResetConfirmRequest` | *(fire-and-forget)* | Confirm password reset with token |

### Sessions

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetSessionsAsync` | - | `SessionsResponse` | Get active sessions for account |
| `TerminatesessionEventAsync` | `TerminateSessionRequest` | *(fire-and-forget)* | Terminate specific session |

### Steam

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `VerifysteamauthAsync` | `SteamVerifyRequest` | `AuthResponse` | Verify Steam Session Ticket |

### Token Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetRevocationlistAsync` | `GetRevocationListRequest` | `RevocationListResponse` | Get current token revocation list |

### Tokens

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `RefreshtokenAsync` | `RefreshRequest` | `AuthResponse` | Refresh access token |
| `ValidateTokenAsync` | - | `ValidateTokenResponse` | Validate access token |

---

## ABML Behavior Management API {#behavior}

**Proxy**: `client.Behavior` | **Version**: 3.0.0

Arcadia Behavior Markup Language (ABML) API for character behavior management.

### ABML

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CompileabmlbehaviorAsync` | `CompileBehaviorRequest` | `CompileBehaviorResponse` | Compile ABML behavior definition |

### Cache

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetCachedbehaviorAsync` | `GetCachedBehaviorRequest` | `CachedBehaviorResponse` | Get cached compiled behavior |
| `InvalidatecachedbehaviorEventAsync` | `InvalidateCacheRequest` | *(fire-and-forget)* | Invalidate cached behavior |

### GOAP

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GenerategoapplanAsync` | `GoapPlanRequest` | `GoapPlanResponse` | Generate GOAP plan |
| `ValidateGoapplanAsync` | `ValidateGoapPlanRequest` | `ValidateGoapPlanResponse` | Validate existing GOAP plan |

### Validation

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ValidateAbmlAsync` | `ValidateAbmlRequest` | `ValidateAbmlResponse` | Validate ABML definition |

---

## Bannou Character Service API {#character}

**Proxy**: `client.Character` | **Version**: 1.0.0

Character management service for game worlds.

### Character Compression

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CompresscharacterAsync` | `CompressCharacterRequest` | `CharacterArchive` | Compress a dead character to archive format |
| `GetCharacterarchiveAsync` | `GetCharacterArchiveRequest` | `CharacterArchive` | Get compressed archive data for a character |
| `CheckcharacterreferencesAsync` | `CheckReferencesRequest` | `CharacterRefCount` | Check reference count for cleanup eligibility |
| `GetCompressdataAsync` | `GetCompressDataRequest` | `CharacterBaseArchive` | Get character base data for compression |

### Character Lookup

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetEnrichedcharacterAsync` | `GetEnrichedCharacterRequest` | `EnrichedCharacterResponse` | Get character with optional related data (personality, backstory, family) |
| `GetCharactersbyrealmAsync` | `GetCharactersByRealmRequest` | `CharacterListResponse` | Get all characters in a realm (primary query pattern) |

### Character Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateCharacterAsync` | `CreateCharacterRequest` | `CharacterResponse` | Create new character |
| `GetCharacterAsync` | `GetCharacterRequest` | `CharacterResponse` | Get character by ID |
| `UpdateCharacterAsync` | `UpdateCharacterRequest` | `CharacterResponse` | Update character |
| `DeleteCharacterEventAsync` | `DeleteCharacterRequest` | *(fire-and-forget)* | Delete character (permanent removal) |
| `ListCharactersAsync` | `ListCharactersRequest` | `CharacterListResponse` | List characters with filtering |
| `TransfercharactertorealmAsync` | `TransferCharacterToRealmRequest` | `CharacterResponse` | Transfer character to a different realm |

---

## Bannou Character Encounter Service API {#character-encounter}

**Proxy**: `client.CharacterEncounter` | **Version**: 1.0.0

Character encounter tracking service for memorable interactions between characters.

### Admin

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `DeleteEncounterAsync` | `DeleteEncounterRequest` | `DeleteEncounterResponse` | Delete encounter and perspectives |
| `DeleteBycharacterAsync` | `DeleteByCharacterRequest` | `DeleteByCharacterResponse` | Delete all encounters for a character |
| `DecaymemoriesAsync` | `DecayMemoriesRequest` | `DecayMemoriesResponse` | Trigger memory decay (maintenance) |

### Compression

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetCompressdataAsync` | `GetCompressDataRequest` | `CharacterEncounterArchive` | Get encounter data for compression |
| `RestorefromarchiveAsync` | `RestoreFromArchiveRequest` | `RestoreFromArchiveResponse` | Restore encounter data from archive |

### Encounter Type Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateEncountertypeAsync` | `CreateEncounterTypeRequest` | `EncounterTypeResponse` | Create new encounter type |
| `GetEncountertypeAsync` | `GetEncounterTypeRequest` | `EncounterTypeResponse` | Get encounter type by code |
| `ListEncountertypesAsync` | `ListEncounterTypesRequest` | `EncounterTypeListResponse` | List all encounter types |
| `UpdateEncountertypeAsync` | `UpdateEncounterTypeRequest` | `EncounterTypeResponse` | Update encounter type |
| `DeleteEncountertypeEventAsync` | `DeleteEncounterTypeRequest` | *(fire-and-forget)* | Delete encounter type |
| `SeedencountertypesAsync` | `SeedEncounterTypesRequest` | `SeedEncounterTypesResponse` | Seed default encounter types |

### Perspectives

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetPerspectiveAsync` | `GetPerspectiveRequest` | `PerspectiveResponse` | Get character's view of encounter |
| `UpdatePerspectiveAsync` | `UpdatePerspectiveRequest` | `PerspectiveResponse` | Update perspective (reflection) |
| `RefreshmemoryAsync` | `RefreshMemoryRequest` | `PerspectiveResponse` | Strengthen memory (referenced) |

### Queries

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `QuerybycharacterAsync` | `QueryByCharacterRequest` | `EncounterListResponse` | Get character's encounters (paginated) |
| `QuerybetweenAsync` | `QueryBetweenRequest` | `EncounterListResponse` | Get encounters between two characters |
| `QuerybylocationAsync` | `QueryByLocationRequest` | `EncounterListResponse` | Recent encounters at location |
| `HasmetAsync` | `HasMetRequest` | `HasMetResponse` | Quick check if two characters have met |
| `GetSentimentAsync` | `GetSentimentRequest` | `SentimentResponse` | Aggregate sentiment toward another character |
| `BatchgetsentimentAsync` | `BatchGetSentimentRequest` | `BatchSentimentResponse` | Bulk sentiment for multiple targets |

### Recording

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `RecordencounterAsync` | `RecordEncounterRequest` | `EncounterResponse` | Record new encounter with perspectives |

---

## Bannou Character History Service API {#character-history}

**Proxy**: `client.CharacterHistory` | **Version**: 1.0.0

Historical event participation and backstory management for characters.

### Backstory

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetBackstoryAsync` | `GetBackstoryRequest` | `BackstoryResponse` | Get machine-readable backstory elements for behavior system |
| `SetbackstoryAsync` | `SetBackstoryRequest` | `BackstoryResponse` | Set backstory elements for a character |
| `AddbackstoryelementAsync` | `AddBackstoryElementRequest` | `BackstoryResponse` | Add a single backstory element |
| `DeleteBackstoryEventAsync` | `DeleteBackstoryRequest` | *(fire-and-forget)* | Delete all backstory for a character |

### Compression

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetCompressdataAsync` | `GetCompressDataRequest` | `CharacterHistoryArchive` | Get history data for compression |
| `RestorefromarchiveAsync` | `RestoreFromArchiveRequest` | `RestoreFromArchiveResponse` | Restore history data from archive |

### Historical Events

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `RecordparticipationAsync` | `RecordParticipationRequest` | `HistoricalParticipation` | Record character participation in a historical event |
| `GetParticipationAsync` | `GetParticipationRequest` | `ParticipationListResponse` | Get all historical events a character participated in |
| `GetEventparticipantsAsync` | `GetEventParticipantsRequest` | `ParticipationListResponse` | Get all characters who participated in a historical event |
| `DeleteParticipationEventAsync` | `DeleteParticipationRequest` | *(fire-and-forget)* | Delete a participation record |

### History Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `DeleteAllhistoryAsync` | `DeleteAllHistoryRequest` | `DeleteAllHistoryResponse` | Delete all history data for a character |
| `SummarizehistoryAsync` | `SummarizeHistoryRequest` | `HistorySummaryResponse` | Generate text summaries for character compression |

---

## Bannou Character Personality Service API {#character-personality}

**Proxy**: `client.CharacterPersonality` | **Version**: 1.0.0

Machine-readable personality traits for NPC behavior decisions.

### Combat Preferences

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetCombatpreferencesAsync` | `GetCombatPreferencesRequest` | `CombatPreferencesResponse` | Get combat preferences for a character |
| `SetcombatpreferencesAsync` | `SetCombatPreferencesRequest` | `CombatPreferencesResponse` | Create or update combat preferences for a character |
| `EvolvecombatpreferencesAsync` | `EvolveCombatRequest` | `CombatEvolutionResult` | Record combat experience that may evolve preferences |
| `DeleteCombatpreferencesEventAsync` | `DeleteCombatPreferencesRequest` | *(fire-and-forget)* | Delete combat preferences for a character |

### Compression

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetCompressdataAsync` | `GetCompressDataRequest` | `CharacterPersonalityArchive` | Get personality data for compression |
| `RestorefromarchiveAsync` | `RestoreFromArchiveRequest` | `RestoreFromArchiveResponse` | Restore personality data from archive |

### Personality Evolution

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `RecordexperienceAsync` | `RecordExperienceRequest` | `ExperienceResult` | Record an experience that may evolve personality |

### Personality Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetPersonalityAsync` | `GetPersonalityRequest` | `PersonalityResponse` | Get personality for a character |
| `SetpersonalityAsync` | `SetPersonalityRequest` | `PersonalityResponse` | Create or update personality for a character |
| `BatchgetpersonalitiesAsync` | `BatchGetPersonalitiesRequest` | `BatchPersonalityResponse` | Get personalities for multiple characters |
| `DeletePersonalityEventAsync` | `DeletePersonalityRequest` | *(fire-and-forget)* | Delete personality for a character |

### Resource Cleanup

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CleanupbycharacterAsync` | `CleanupByCharacterRequest` | `CleanupByCharacterResponse` | Cleanup all personality data for a deleted character |

---

## Bannou Connect API {#connect}

**Proxy**: `client.Connect` | **Version**: 2.0.0

Real-time communication and WebSocket connection management for Bannou services.

### Client Capabilities

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetClientcapabilitiesAsync` | `GetClientCapabilitiesRequest` | `ClientCapabilitiesResponse` | Get client capability manifest (GUID → API mappings) |

### Internal Proxy

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ProxyinternalrequestAsync` | `InternalProxyRequest` | `InternalProxyResponse` | Internal API proxy for stateless requests |

### Session Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetAccountsessionsAsync` | `GetAccountSessionsRequest` | `GetAccountSessionsResponse` | Get all active WebSocket sessions for an account |

### WebSocket Connection

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ConnectwebsocketpostEventAsync` | `ConnectRequest` | *(fire-and-forget)* | Establish WebSocket connection (POST variant) |

---

## Contract Service API {#contract}

**Proxy**: `client.Contract` | **Version**: 1.0.0

Binding agreements between entities with milestone-based progression.

### Breaches

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ReportbreachAsync` | `ReportBreachRequest` | `BreachResponse` | Report a contract breach |
| `CurebreachAsync` | `CureBreachRequest` | `BreachResponse` | Mark breach as cured (system/admin action) |
| `GetBreachAsync` | `GetBreachRequest` | `BreachResponse` | Get breach details |

### ClauseTypes

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `RegisterClausetypeAsync` | `RegisterClauseTypeRequest` | `RegisterClauseTypeResponse` | Register a new clause type |
| `ListClausetypesAsync` | `ListClauseTypesRequest` | `ListClauseTypesResponse` | List all registered clause types |

### Constraints

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CheckcontractconstraintAsync` | `CheckConstraintRequest` | `CheckConstraintResponse` | Check if entity can take action given contracts |
| `QueryactivecontractsAsync` | `QueryActiveContractsRequest` | `QueryActiveContractsResponse` | Query active contracts for entity |

### Execution

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `SetcontracttemplatevaluesAsync` | `SetTemplateValuesRequest` | `SetTemplateValuesResponse` | Set template values on contract instance |
| `CheckassetrequirementsAsync` | `CheckAssetRequirementsRequest` | `CheckAssetRequirementsResponse` | Check if asset requirement clauses are satisfied |
| `ExecutecontractAsync` | `ExecuteContractRequest` | `ExecuteContractResponse` | Execute all contract clauses (idempotent) |

### Guardian

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `LockcontractAsync` | `LockContractRequest` | `LockContractResponse` | Lock contract under guardian custody |
| `UnlockcontractAsync` | `UnlockContractRequest` | `UnlockContractResponse` | Unlock contract from guardian custody |
| `TransfercontractpartyAsync` | `TransferContractPartyRequest` | `TransferContractPartyResponse` | Transfer party role to new entity |

### Instances

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateContractinstanceAsync` | `CreateContractInstanceRequest` | `ContractInstanceResponse` | Create contract instance from template |
| `ProposecontractinstanceAsync` | `ProposeContractInstanceRequest` | `ContractInstanceResponse` | Propose contract to parties (starts consent flow) |
| `ConsenttocontractAsync` | `ConsentToContractRequest` | `ContractInstanceResponse` | Party consents to contract |
| `GetContractinstanceAsync` | `GetContractInstanceRequest` | `ContractInstanceResponse` | Get instance by ID |
| `QuerycontractinstancesAsync` | `QueryContractInstancesRequest` | `QueryContractInstancesResponse` | Query instances by party, template, status |
| `TerminatecontractinstanceAsync` | `TerminateContractInstanceRequest` | `ContractInstanceResponse` | Request early termination |
| `GetContractinstancestatusAsync` | `GetContractInstanceStatusRequest` | `ContractInstanceStatusResponse` | Get current status and milestone progress |

### Metadata

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `UpdateContractmetadataAsync` | `UpdateContractMetadataRequest` | `ContractMetadataResponse` | Update game metadata on instance |
| `GetContractmetadataAsync` | `GetContractMetadataRequest` | `ContractMetadataResponse` | Get game metadata |

### Milestones

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CompletemilestoneAsync` | `CompleteMilestoneRequest` | `MilestoneResponse` | External system reports milestone completed |
| `FailmilestoneAsync` | `FailMilestoneRequest` | `MilestoneResponse` | External system reports milestone failed |
| `GetMilestoneAsync` | `GetMilestoneRequest` | `MilestoneResponse` | Get milestone details and status |

### Templates

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateContracttemplateAsync` | `CreateContractTemplateRequest` | `ContractTemplateResponse` | Create a contract template |
| `GetContracttemplateAsync` | `GetContractTemplateRequest` | `ContractTemplateResponse` | Get template by ID or code |
| `ListContracttemplatesAsync` | `ListContractTemplatesRequest` | `ListContractTemplatesResponse` | List templates with filters |
| `UpdateContracttemplateAsync` | `UpdateContractTemplateRequest` | `ContractTemplateResponse` | Update template (not instances) |
| `DeleteContracttemplateEventAsync` | `DeleteContractTemplateRequest` | *(fire-and-forget)* | Soft-delete template |

---

## Currency Service API {#currency}

**Proxy**: `client.Currency` | **Version**: 1.0.0

Multi-currency management service for game economies.

### Analytics

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetGlobalsupplyAsync` | `GetGlobalSupplyRequest` | `GetGlobalSupplyResponse` | Get global supply statistics for a currency |
| `GetWalletdistributionAsync` | `GetWalletDistributionRequest` | `GetWalletDistributionResponse` | Get wealth distribution statistics |

### Authorization Hold

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateHoldAsync` | `CreateHoldRequest` | `HoldResponse` | Create an authorization hold (reserve funds) |
| `CaptureholdAsync` | `CaptureHoldRequest` | `CaptureHoldResponse` | Capture held funds (debit final amount) |
| `ReleaseholdAsync` | `ReleaseHoldRequest` | `HoldResponse` | Release held funds (make available again) |
| `GetHoldAsync` | `GetHoldRequest` | `HoldResponse` | Get hold status and details |

### Balance

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetBalanceAsync` | `GetBalanceRequest` | `GetBalanceResponse` | Get balance for a specific currency in a wallet |
| `BatchgetbalancesAsync` | `BatchGetBalancesRequest` | `BatchGetBalancesResponse` | Get multiple balances in one call |
| `CreditcurrencyAsync` | `CreditCurrencyRequest` | `CreditCurrencyResponse` | Credit currency to a wallet (faucet operation) |
| `DebitcurrencyAsync` | `DebitCurrencyRequest` | `DebitCurrencyResponse` | Debit currency from a wallet (sink operation) |
| `TransfercurrencyAsync` | `TransferCurrencyRequest` | `TransferCurrencyResponse` | Transfer currency between wallets |
| `BatchcreditcurrencyAsync` | `BatchCreditRequest` | `BatchCreditResponse` | Credit multiple wallets in one call |

### Conversion

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CalculateconversionAsync` | `CalculateConversionRequest` | `CalculateConversionResponse` | Calculate conversion without executing |
| `ExecuteconversionAsync` | `ExecuteConversionRequest` | `ExecuteConversionResponse` | Execute currency conversion in a wallet |
| `GetExchangerateAsync` | `GetExchangeRateRequest` | `GetExchangeRateResponse` | Get exchange rate between two currencies |
| `UpdateExchangerateAsync` | `UpdateExchangeRateRequest` | `UpdateExchangeRateResponse` | Update a currency's exchange rate to base |

### Currency Definition

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateCurrencydefinitionAsync` | `CreateCurrencyDefinitionRequest` | `CurrencyDefinitionResponse` | Create a new currency definition |
| `GetCurrencydefinitionAsync` | `GetCurrencyDefinitionRequest` | `CurrencyDefinitionResponse` | Get currency definition by ID or code |
| `ListCurrencydefinitionsAsync` | `ListCurrencyDefinitionsRequest` | `ListCurrencyDefinitionsResponse` | List currency definitions with filters |
| `UpdateCurrencydefinitionAsync` | `UpdateCurrencyDefinitionRequest` | `CurrencyDefinitionResponse` | Update mutable fields of a currency definition |

### Escrow Integration

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `EscrowdepositAsync` | `EscrowDepositRequest` | `EscrowDepositResponse` | Debit wallet for escrow deposit |
| `EscrowreleaseAsync` | `EscrowReleaseRequest` | `EscrowReleaseResponse` | Credit recipient on escrow completion |
| `EscrowrefundAsync` | `EscrowRefundRequest` | `EscrowRefundResponse` | Credit depositor on escrow refund |

### Transaction History

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetTransactionAsync` | `GetTransactionRequest` | `TransactionResponse` | Get a transaction by ID |
| `GetTransactionhistoryAsync` | `GetTransactionHistoryRequest` | `GetTransactionHistoryResponse` | Get paginated transaction history for a wallet |
| `GetTransactionsbyreferenceAsync` | `GetTransactionsByReferenceRequest` | `GetTransactionsByReferenceResponse` | Get transactions by reference type and ID |

### Wallet

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateWalletAsync` | `CreateWalletRequest` | `WalletResponse` | Create a new wallet for an owner |
| `GetWalletAsync` | `GetWalletRequest` | `WalletWithBalancesResponse` | Get wallet by ID or owner |
| `GetOrcreatewalletAsync` | `GetOrCreateWalletRequest` | `GetOrCreateWalletResponse` | Get existing wallet or create if not exists |
| `FreezewalletAsync` | `FreezeWalletRequest` | `WalletResponse` | Freeze a wallet to prevent transactions |
| `UnfreezewalletAsync` | `UnfreezeWalletRequest` | `WalletResponse` | Unfreeze a frozen wallet |
| `ClosewalletAsync` | `CloseWalletRequest` | `CloseWalletResponse` | Permanently close a wallet |

---

## Bannou Documentation API {#documentation}

**Proxy**: `client.Documentation` | **Version**: 1.0.0

Knowledge base API for AI agents to query documentation. Designed for SignalWire SWAIG, OpenAI function calling, and Claude tool use. All endpoints...

### Admin

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateDocumentAsync` | `CreateDocumentRequest` | `CreateDocumentResponse` | Create new documentation entry |
| `UpdateDocumentAsync` | `UpdateDocumentRequest` | `UpdateDocumentResponse` | Update existing documentation entry |
| `DeleteDocumentAsync` | `DeleteDocumentRequest` | `DeleteDocumentResponse` | Soft-delete documentation entry to trashcan |
| `RecoverdocumentAsync` | `RecoverDocumentRequest` | `RecoverDocumentResponse` | Recover document from trashcan |
| `BulkupdatedocumentsAsync` | `BulkUpdateRequest` | `BulkUpdateResponse` | Bulk update document metadata |
| `BulkdeletedocumentsAsync` | `BulkDeleteRequest` | `BulkDeleteResponse` | Bulk soft-delete documents to trashcan |
| `ImportdocumentationAsync` | `ImportDocumentationRequest` | `ImportDocumentationResponse` | Bulk import documentation from structured source |
| `ListTrashcanAsync` | `ListTrashcanRequest` | `ListTrashcanResponse` | List documents in the trashcan |
| `PurgetrashcanAsync` | `PurgeTrashcanRequest` | `PurgeTrashcanResponse` | Permanently delete trashcan items |
| `GetNamespacestatsAsync` | `GetNamespaceStatsRequest` | `NamespaceStatsResponse` | Get namespace documentation statistics |

### Archive

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateDocumentationarchiveAsync` | `CreateArchiveRequest` | `CreateArchiveResponse` | Create documentation archive |
| `ListDocumentationarchivesAsync` | `ListArchivesRequest` | `ListArchivesResponse` | List documentation archives |
| `RestoredocumentationarchiveAsync` | `RestoreArchiveRequest` | `RestoreArchiveResponse` | Restore documentation from archive |
| `DeleteDocumentationarchiveAsync` | `DeleteArchiveRequest` | `DeleteArchiveResponse` | Delete documentation archive |

### Documents

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetDocumentAsync` | `GetDocumentRequest` | `GetDocumentResponse` | Get specific document by ID or slug |
| `ListDocumentsAsync` | `ListDocumentsRequest` | `ListDocumentsResponse` | List documents by category |

### Repository

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `BindrepositoryAsync` | `BindRepositoryRequest` | `BindRepositoryResponse` | Bind a git repository to a documentation namespace |
| `UnbindrepositoryAsync` | `UnbindRepositoryRequest` | `UnbindRepositoryResponse` | Remove repository binding from namespace |
| `SyncrepositoryAsync` | `SyncRepositoryRequest` | `SyncRepositoryResponse` | Manually trigger repository sync |
| `GetRepositorystatusAsync` | `RepositoryStatusRequest` | `RepositoryStatusResponse` | Get repository binding status |
| `ListRepositorybindingsAsync` | `ListRepositoryBindingsRequest` | `ListRepositoryBindingsResponse` | List all repository bindings |
| `UpdateRepositorybindingAsync` | `UpdateRepositoryBindingRequest` | `UpdateRepositoryBindingResponse` | Update repository binding configuration |

### Search

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `QuerydocumentationAsync` | `QueryDocumentationRequest` | `QueryDocumentationResponse` | Natural language documentation search |
| `SearchdocumentationAsync` | `SearchDocumentationRequest` | `SearchDocumentationResponse` | Full-text keyword search |
| `SuggestrelatedtopicsAsync` | `SuggestRelatedRequest` | `SuggestRelatedResponse` | Get related topics and follow-up suggestions |

---

## Escrow Service API {#escrow}

**Proxy**: `client.Escrow` | **Version**: 1.0.0

Full-custody orchestration layer for multi-party asset exchanges.

### Arbiter

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ResolveAsync` | `ResolveRequest` | `ResolveResponse` | Arbiter resolves disputed escrow |

### Completion

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ReleaseAsync` | `ReleaseRequest` | `ReleaseResponse` | Trigger release |
| `RefundAsync` | `RefundRequest` | `RefundResponse` | Trigger refund |
| `CancelAsync` | `CancelRequest` | `CancelResponse` | Cancel escrow before fully funded |
| `DisputeAsync` | `DisputeRequest` | `DisputeResponse` | Raise a dispute on funded escrow |
| `ConfirmreleaseAsync` | `ConfirmReleaseRequest` | `ConfirmReleaseResponse` | Confirm receipt of released assets |
| `ConfirmrefundAsync` | `ConfirmRefundRequest` | `ConfirmRefundResponse` | Confirm receipt of refunded assets |

### Condition

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `VerifyconditionAsync` | `VerifyConditionRequest` | `VerifyConditionResponse` | Verify condition for conditional escrow |

### Consent

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `RecordconsentAsync` | `ConsentRequest` | `ConsentResponse` | Record party consent |
| `GetConsentstatusAsync` | `GetConsentStatusRequest` | `GetConsentStatusResponse` | Get consent status for escrow |

### Deposits

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `DepositAsync` | `DepositRequest` | `DepositResponse` | Deposit assets into escrow |
| `ValidateDepositAsync` | `ValidateDepositRequest` | `ValidateDepositResponse` | Validate a deposit without executing |
| `GetDepositstatusAsync` | `GetDepositStatusRequest` | `GetDepositStatusResponse` | Get deposit status for a party |

### Handlers

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `RegisterHandlerAsync` | `RegisterHandlerRequest` | `RegisterHandlerResponse` | Register a custom asset type handler |
| `ListHandlersAsync` | `ListHandlersRequest` | `ListHandlersResponse` | List registered asset handlers |
| `DeregisterhandlerAsync` | `DeregisterHandlerRequest` | `DeregisterHandlerResponse` | Remove a custom asset handler registration |

### Lifecycle

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateEscrowAsync` | `CreateEscrowRequest` | `CreateEscrowResponse` | Create a new escrow agreement |
| `GetEscrowAsync` | `GetEscrowRequest` | `GetEscrowResponse` | Get escrow details |
| `ListEscrowsAsync` | `ListEscrowsRequest` | `ListEscrowsResponse` | List escrows for a party |
| `GetMytokenAsync` | `GetMyTokenRequest` | `GetMyTokenResponse` | Get deposit or release token for a party |

### Validation

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ValidateEscrowAsync` | `ValidateEscrowRequest` | `ValidateEscrowResponse` | Manually trigger validation |
| `ReaffirmAsync` | `ReaffirmRequest` | `ReaffirmResponse` | Re-affirm after validation failure |

---

## Bannou Game Service API {#game-service}

**Proxy**: `client.GameService` | **Version**: 1.0.0

Registry service for game services that users can subscribe to. Provides a minimal registry of available services (games/applications).

### Game Service Registry

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ListServicesAsync` | `ListServicesRequest` | `ListServicesResponse` | List all registered game services |
| `GetServiceAsync` | `GetServiceRequest` | `ServiceInfo` | Get service by ID or stub name |
| `CreateServiceAsync` | `CreateServiceRequest` | `ServiceInfo` | Create a new game service entry |
| `UpdateServiceAsync` | `UpdateServiceRequest` | `ServiceInfo` | Update a game service entry |
| `DeleteServiceEventAsync` | `DeleteServiceRequest` | *(fire-and-forget)* | Delete a game service entry |

---

## Bannou Game Session Service API {#game-session}

**Proxy**: `client.GameSession` | **Version**: 2.0.0

Minimal game session management for games.

### Game Actions

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `PerformgameactionAsync` | `GameActionRequest` | `GameActionResponse` | Perform game action (enhanced permissions after joining) |

### Game Chat

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `SendchatmessageEventAsync` | `ChatMessageRequest` | *(fire-and-forget)* | Send chat message to game session |

### Game Sessions

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ListGamesessionsAsync` | `ListGameSessionsRequest` | `GameSessionListResponse` | List available game sessions |
| `CreateGamesessionAsync` | `CreateGameSessionRequest` | `GameSessionResponse` | Create new game session |
| `GetGamesessionAsync` | `GetGameSessionRequest` | `GameSessionResponse` | Get game session details |
| `JoingamesessionAsync` | `JoinGameSessionRequest` | `JoinGameSessionResponse` | Join a game session |
| `LeavegamesessionEventAsync` | `LeaveGameSessionRequest` | *(fire-and-forget)* | Leave a game session |
| `KickplayerEventAsync` | `KickPlayerRequest` | *(fire-and-forget)* | Kick player from game session (admin only) |
| `JoingamesessionbyidAsync` | `JoinGameSessionByIdRequest` | `JoinGameSessionResponse` | Join a specific game session by ID |
| `LeavegamesessionbyidEventAsync` | `LeaveGameSessionByIdRequest` | *(fire-and-forget)* | Leave a specific game session by ID |

### Internal

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `PublishjoinshortcutAsync` | `PublishJoinShortcutRequest` | `PublishJoinShortcutResponse` | Publish join shortcut for matchmade session |

---

## Inventory Service API {#inventory}

**Proxy**: `client.Inventory` | **Version**: 1.0.0

Container and inventory management service for games.

### Container

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateContainerAsync` | `CreateContainerRequest` | `ContainerResponse` | Create a new container |
| `GetContainerAsync` | `GetContainerRequest` | `ContainerWithContentsResponse` | Get container with contents |
| `GetOrcreatecontainerAsync` | `GetOrCreateContainerRequest` | `ContainerResponse` | Get container or create if not exists |
| `ListContainersAsync` | `ListContainersRequest` | `ListContainersResponse` | List containers for owner |
| `UpdateContainerAsync` | `UpdateContainerRequest` | `ContainerResponse` | Update container properties |
| `DeleteContainerAsync` | `DeleteContainerRequest` | `DeleteContainerResponse` | Delete container |

### Inventory Operations

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `AdditemtocontainerAsync` | `AddItemRequest` | `AddItemResponse` | Add item to container |
| `RemoveitemfromcontainerAsync` | `RemoveItemRequest` | `RemoveItemResponse` | Remove item from container |
| `MoveitemAsync` | `MoveItemRequest` | `MoveItemResponse` | Move item to different slot or container |
| `TransferitemAsync` | `TransferItemRequest` | `TransferItemResponse` | Transfer item to different owner |
| `SplitstackAsync` | `SplitStackRequest` | `SplitStackResponse` | Split stack into two |
| `MergestacksAsync` | `MergeStacksRequest` | `MergeStacksResponse` | Merge two stacks |

### Inventory Queries

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `QueryitemsAsync` | `QueryItemsRequest` | `QueryItemsResponse` | Find items across containers |
| `CountitemsAsync` | `CountItemsRequest` | `CountItemsResponse` | Count items of a template |
| `HasitemsAsync` | `HasItemsRequest` | `HasItemsResponse` | Check if entity has required items |
| `FindspaceAsync` | `FindSpaceRequest` | `FindSpaceResponse` | Find where item would fit |

---

## Item Service API {#item}

**Proxy**: `client.Item` | **Version**: 1.0.0

Item template and instance management service.

### Item Instance

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateIteminstanceAsync` | `CreateItemInstanceRequest` | `ItemInstanceResponse` | Create a new item instance |
| `GetIteminstanceAsync` | `GetItemInstanceRequest` | `ItemInstanceResponse` | Get item instance by ID |
| `ModifyiteminstanceAsync` | `ModifyItemInstanceRequest` | `ItemInstanceResponse` | Modify item instance state |
| `BinditeminstanceAsync` | `BindItemInstanceRequest` | `ItemInstanceResponse` | Bind item to character |
| `UnbinditeminstanceAsync` | `UnbindItemInstanceRequest` | `ItemInstanceResponse` | Unbind item from character |
| `DestroyiteminstanceAsync` | `DestroyItemInstanceRequest` | `DestroyItemInstanceResponse` | Destroy item instance |

### Item Query

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ListItemsbycontainerAsync` | `ListItemsByContainerRequest` | `ListItemsResponse` | List items in a container |
| `ListItemsbytemplateAsync` | `ListItemsByTemplateRequest` | `ListItemsResponse` | List instances of a template |
| `BatchgetiteminstancesAsync` | `BatchGetItemInstancesRequest` | `BatchGetItemInstancesResponse` | Get multiple item instances by ID |

### Item Template

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateItemtemplateAsync` | `CreateItemTemplateRequest` | `ItemTemplateResponse` | Create a new item template |
| `GetItemtemplateAsync` | `GetItemTemplateRequest` | `ItemTemplateResponse` | Get item template by ID or code |
| `ListItemtemplatesAsync` | `ListItemTemplatesRequest` | `ListItemTemplatesResponse` | List item templates with filters |
| `UpdateItemtemplateAsync` | `UpdateItemTemplateRequest` | `ItemTemplateResponse` | Update mutable fields of an item template |
| `DeprecateitemtemplateAsync` | `DeprecateItemTemplateRequest` | `ItemTemplateResponse` | Deprecate an item template |

---

## Bannou Leaderboard Service API {#leaderboard}

**Proxy**: `client.Leaderboard` | **Version**: 1.0.0

Real-time leaderboard management using Redis Sorted Sets for efficient ranking.

### Definitions

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateLeaderboarddefinitionAsync` | `CreateLeaderboardDefinitionRequest` | `LeaderboardDefinitionResponse` | Create a new leaderboard definition |
| `GetLeaderboarddefinitionAsync` | `GetLeaderboardDefinitionRequest` | `LeaderboardDefinitionResponse` | Get leaderboard definition |
| `ListLeaderboarddefinitionsAsync` | `ListLeaderboardDefinitionsRequest` | `ListLeaderboardDefinitionsResponse` | List leaderboard definitions |
| `UpdateLeaderboarddefinitionAsync` | `UpdateLeaderboardDefinitionRequest` | `LeaderboardDefinitionResponse` | Update leaderboard definition |
| `DeleteLeaderboarddefinitionEventAsync` | `DeleteLeaderboardDefinitionRequest` | *(fire-and-forget)* | Delete leaderboard definition |

### Rankings

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetEntityrankAsync` | `GetEntityRankRequest` | `EntityRankResponse` | Get entity's rank |
| `GetTopranksAsync` | `GetTopRanksRequest` | `LeaderboardEntriesResponse` | Get top entries |
| `GetRanksaroundAsync` | `GetRanksAroundRequest` | `LeaderboardEntriesResponse` | Get entries around entity |

### Scores

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `SubmitscoreAsync` | `SubmitScoreRequest` | `SubmitScoreResponse` | Submit or update a score |
| `SubmitscorebatchAsync` | `SubmitScoreBatchRequest` | `SubmitScoreBatchResponse` | Submit multiple scores |

### Seasons

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateSeasonAsync` | `CreateSeasonRequest` | `SeasonResponse` | Start a new season |
| `GetSeasonAsync` | `GetSeasonRequest` | `SeasonResponse` | Get current season info |

---

## Bannou Location Service API {#location}

**Proxy**: `client.Location` | **Version**: 1.0.0

Location management service for game worlds.

### Location

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetLocationAsync` | `GetLocationRequest` | `LocationResponse` | Get location by ID |
| `GetLocationbycodeAsync` | `GetLocationByCodeRequest` | `LocationResponse` | Get location by code and realm |
| `ListLocationsAsync` | `ListLocationsRequest` | `LocationListResponse` | List locations with filtering |
| `ListLocationsbyrealmAsync` | `ListLocationsByRealmRequest` | `LocationListResponse` | List all locations in a realm (primary query pattern) |
| `ListLocationsbyparentAsync` | `ListLocationsByParentRequest` | `LocationListResponse` | Get child locations for a parent location |
| `ListRootlocationsAsync` | `ListRootLocationsRequest` | `LocationListResponse` | Get root locations in a realm |
| `GetLocationancestorsAsync` | `GetLocationAncestorsRequest` | `LocationListResponse` | Get all ancestors of a location |
| `ValidateTerritoryAsync` | `ValidateTerritoryRequest` | `ValidateTerritoryResponse` | Validate location against territory boundaries |
| `GetLocationdescendantsAsync` | `GetLocationDescendantsRequest` | `LocationListResponse` | Get all descendants of a location |
| `LocationexistsAsync` | `LocationExistsRequest` | `LocationExistsResponse` | Check if location exists and is active |

### Location Admin

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateLocationAsync` | `CreateLocationRequest` | `LocationResponse` | Create new location |
| `UpdateLocationAsync` | `UpdateLocationRequest` | `LocationResponse` | Update location |
| `SetlocationparentAsync` | `SetLocationParentRequest` | `LocationResponse` | Set or change the parent of a location |
| `RemovelocationparentAsync` | `RemoveLocationParentRequest` | `LocationResponse` | Remove parent from a location (make it a root location) |
| `DeleteLocationEventAsync` | `DeleteLocationRequest` | *(fire-and-forget)* | Delete location |
| `DeprecatelocationAsync` | `DeprecateLocationRequest` | `LocationResponse` | Deprecate a location |
| `UndeprecatelocationAsync` | `UndeprecateLocationRequest` | `LocationResponse` | Restore a deprecated location |
| `SeedlocationsAsync` | `SeedLocationsRequest` | `SeedLocationsResponse` | Seed locations from configuration |

---

## Bannou Mapping Service API {#mapping}

**Proxy**: `client.Mapping` | **Version**: 1.0.0

Spatial data management service for game worlds.

### Authoring

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CheckoutforauthoringAsync` | `AuthoringCheckoutRequest` | `AuthoringCheckoutResponse` | Acquire exclusive edit lock for design-time editing |
| `CommitauthoringAsync` | `AuthoringCommitRequest` | `AuthoringCommitResponse` | Commit design-time changes |
| `ReleaseauthoringAsync` | `AuthoringReleaseRequest` | `AuthoringReleaseResponse` | Release authoring checkout without committing |

### Authority

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateChannelAsync` | `CreateChannelRequest` | `AuthorityGrant` | Create a new map channel and become its authority |
| `ReleaseauthorityAsync` | `ReleaseAuthorityRequest` | `ReleaseAuthorityResponse` | Release authority over a channel |
| `AuthorityheartbeatAsync` | `AuthorityHeartbeatRequest` | `AuthorityHeartbeatResponse` | Maintain authority over channel |

### Definition

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateDefinitionAsync` | `CreateDefinitionRequest` | `MapDefinition` | Create a map definition template |
| `GetDefinitionAsync` | `GetDefinitionRequest` | `MapDefinition` | Get a map definition by ID |
| `ListDefinitionsAsync` | `ListDefinitionsRequest` | `ListDefinitionsResponse` | List map definitions with optional filters |
| `UpdateDefinitionAsync` | `UpdateDefinitionRequest` | `MapDefinition` | Update a map definition |
| `DeleteDefinitionAsync` | `DeleteDefinitionRequest` | `DeleteDefinitionResponse` | Delete a map definition |

### Query

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `QuerypointAsync` | `QueryPointRequest` | `QueryPointResponse` | Query map data at a specific point |
| `QueryboundsAsync` | `QueryBoundsRequest` | `QueryBoundsResponse` | Query map data within bounds |
| `QueryobjectsbytypeAsync` | `QueryObjectsByTypeRequest` | `QueryObjectsByTypeResponse` | Find all objects of a type in region |
| `QueryaffordanceAsync` | `AffordanceQueryRequest` | `AffordanceQueryResponse` | Find locations that afford a specific action or scene type |

### Runtime

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `PublishmapupdateAsync` | `PublishMapUpdateRequest` | `PublishMapUpdateResponse` | Publish map data update (RPC path) |
| `PublishobjectchangesAsync` | `PublishObjectChangesRequest` | `PublishObjectChangesResponse` | Publish metadata object changes (batch) |
| `RequestsnapshotAsync` | `RequestSnapshotRequest` | `RequestSnapshotResponse` | Request full snapshot for cold start |

---

## Bannou Matchmaking Service API {#matchmaking}

**Proxy**: `client.Matchmaking` | **Version**: 1.0.0

Matchmaking service for competitive and casual game matching.

### Matchmaking

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `JoinmatchmakingAsync` | `JoinMatchmakingRequest` | `JoinMatchmakingResponse` | Join matchmaking queue |
| `LeavematchmakingEventAsync` | `LeaveMatchmakingRequest` | *(fire-and-forget)* | Leave matchmaking queue |
| `GetMatchmakingstatusAsync` | `GetMatchmakingStatusRequest` | `MatchmakingStatusResponse` | Get matchmaking status |
| `AcceptmatchAsync` | `AcceptMatchRequest` | `AcceptMatchResponse` | Accept a formed match |
| `DeclinematchEventAsync` | `DeclineMatchRequest` | *(fire-and-forget)* | Decline a formed match |

### Queue Administration

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateQueueAsync` | `CreateQueueRequest` | `QueueResponse` | Create a new matchmaking queue |
| `UpdateQueueAsync` | `UpdateQueueRequest` | `QueueResponse` | Update a matchmaking queue |
| `DeleteQueueEventAsync` | `DeleteQueueRequest` | *(fire-and-forget)* | Delete a matchmaking queue |

### Queues

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ListQueuesAsync` | `ListQueuesRequest` | `ListQueuesResponse` | List available matchmaking queues |
| `GetQueueAsync` | `GetQueueRequest` | `QueueResponse` | Get queue details |

### Statistics

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetMatchmakingstatsAsync` | `GetMatchmakingStatsRequest` | `MatchmakingStatsResponse` | Get queue statistics |

---

## Bannou Mesh Service API {#mesh}

**Proxy**: `client.Mesh` | **Version**: 1.0.0

Native service mesh plugin providing direct service-to-service invocation natively. Replaces mesh invocation with YARP-based HTTP routing and Redis...

### Diagnostics

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetHealthAsync` | `GetHealthRequest` | `MeshHealthResponse` | Get mesh health status |

### Registration

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `RegisterEndpointAsync` | `RegisterEndpointRequest` | `RegisterEndpointResponse` | Register a service endpoint |
| `DeregisterendpointEventAsync` | `DeregisterEndpointRequest` | *(fire-and-forget)* | Deregister a service endpoint |
| `HeartbeatAsync` | `HeartbeatRequest` | `HeartbeatResponse` | Update endpoint health and load |

### Routing

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetRouteAsync` | `GetRouteRequest` | `GetRouteResponse` | Get optimal endpoint for routing |
| `GetMappingsAsync` | `GetMappingsRequest` | `GetMappingsResponse` | Get service-to-app-id mappings |

### Service Discovery

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetEndpointsAsync` | `GetEndpointsRequest` | `GetEndpointsResponse` | Get endpoints for a service |
| `ListEndpointsAsync` | `ListEndpointsRequest` | `ListEndpointsResponse` | List all registered endpoints |

---

## Bannou Messaging Service API {#messaging}

**Proxy**: `client.Messaging` | **Version**: 1.0.0

Native RabbitMQ pub/sub messaging with native serialization.

### Messaging

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `PublisheventAsync` | `PublishEventRequest` | `PublishEventResponse` | Publish an event to a topic |
| `CreateSubscriptionAsync` | `CreateSubscriptionRequest` | `CreateSubscriptionResponse` | Create a dynamic subscription to a topic |
| `RemovesubscriptionEventAsync` | `RemoveSubscriptionRequest` | *(fire-and-forget)* | Remove a dynamic subscription |
| `ListTopicsAsync` | `ListTopicsRequest` | `ListTopicsResponse` | List all known topics |

---

## Music Theory Engine API {#music}

**Proxy**: `client.Music` | **Version**: 1.0.0

Pure computation music generation using formal music theory rules.

### Generation

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GeneratecompositionAsync` | `GenerateCompositionRequest` | `GenerateCompositionResponse` | Generate composition from style and constraints |

### Styles

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetStyleAsync` | `GetStyleRequest` | `StyleDefinitionResponse` | Get style definition |
| `ListStylesAsync` | `ListStylesRequest` | `ListStylesResponse` | List available styles |
| `CreateStyleAsync` | `CreateStyleRequest` | `StyleDefinitionResponse` | Create new style definition |

### Theory

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GenerateprogressionAsync` | `GenerateProgressionRequest` | `GenerateProgressionResponse` | Generate chord progression |
| `GeneratemelodyAsync` | `GenerateMelodyRequest` | `GenerateMelodyResponse` | Generate melody over harmony |
| `ApplyvoiceleadingAsync` | `VoiceLeadRequest` | `VoiceLeadResponse` | Apply voice leading to chords |

### Validation

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ValidateMidijsonAsync` | `ValidateMidiJsonRequest` | `ValidateMidiJsonResponse` | Validate MIDI-JSON structure |

---

## Orchestrator API {#orchestrator}

**Proxy**: `client.Orchestrator` | **Version**: 3.0.0

Central intelligence for Bannou environment management and service orchestration.

### Other

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetInfrastructurehealthAsync` | `InfrastructureHealthRequest` | `InfrastructureHealthResponse` | Check infrastructure component health |
| `GetServiceshealthAsync` | `ServiceHealthRequest` | `ServiceHealthReport` | Get health status of all services |
| `RestartserviceAsync` | `ServiceRestartRequest` | `ServiceRestartResult` | Restart service with optional configuration |
| `ShouldrestartserviceAsync` | `ShouldRestartServiceRequest` | `RestartRecommendation` | Check if service needs restart |
| `GetBackendsAsync` | `ListBackendsRequest` | `BackendsResponse` | Detect available container orchestration backends |
| `GetPresetsAsync` | `ListPresetsRequest` | `PresetsResponse` | List available deployment presets |
| `DeployAsync` | `DeployRequest` | `DeployResponse` | Deploy or update an environment |
| `GetServiceroutingAsync` | `GetServiceRoutingRequest` | `ServiceRoutingResponse` | Get current service-to-app-id routing mappings |
| `GetStatusAsync` | `GetStatusRequest` | `EnvironmentStatus` | Get current environment status |
| `TeardownAsync` | `TeardownRequest` | `TeardownResponse` | Tear down the current environment |
| `CleanAsync` | `CleanRequest` | `CleanResponse` | Clean up unused resources |
| `GetLogsAsync` | `GetLogsRequest` | `LogsResponse` | Get service/container logs |
| `UpdateTopologyAsync` | `TopologyUpdateRequest` | `TopologyUpdateResponse` | Update service topology without full redeploy |
| `RequestcontainerrestartAsync` | `ContainerRestartRequestBody` | `ContainerRestartResponse` | Request container restart (self-service pattern) |
| `GetContainerstatusAsync` | `GetContainerStatusRequest` | `ContainerStatus` | Get container health and restart history |
| `RollbackconfigurationAsync` | `ConfigRollbackRequest` | `ConfigRollbackResponse` | Rollback to previous configuration |
| `GetConfigversionAsync` | `GetConfigVersionRequest` | `ConfigVersionResponse` | Get current configuration version and metadata |
| `AcquireprocessorAsync` | `AcquireProcessorRequest` | `AcquireProcessorResponse` | Acquire a processor from a pool |
| `ReleaseprocessorAsync` | `ReleaseProcessorRequest` | `ReleaseProcessorResponse` | Release a processor back to the pool |
| `GetPoolstatusAsync` | `GetPoolStatusRequest` | `PoolStatusResponse` | Get processing pool status |
| `ScalepoolAsync` | `ScalePoolRequest` | `ScalePoolResponse` | Scale a processing pool |
| `CleanuppoolAsync` | `CleanupPoolRequest` | `CleanupPoolResponse` | Cleanup idle processing pool instances |

---

## Bannou Permission System API {#permission}

**Proxy**: `client.Permission` | **Version**: 3.0.0

Redis-backed high-performance permission system for WebSocket services.

### Permission Lookup

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetCapabilitiesAsync` | `CapabilityRequest` | `CapabilityResponse` | Get available API methods for session |

### Permission Validation

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ValidateApiaccessAsync` | `ValidationRequest` | `ValidationResponse` | Validate specific API access for session |

### Service Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `RegisterServicepermissionsAsync` | `ServicePermissionMatrix` | `RegistrationResponse` | Register or update service permission matrix |
| `GetRegisteredservicesAsync` | `ListServicesRequest` | `RegisteredServicesResponse` | List all registered services |

### Session Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `UpdateSessionstateAsync` | `SessionStateUpdate` | `SessionUpdateResponse` | Update session state for specific service |
| `UpdateSessionroleAsync` | `SessionRoleUpdate` | `SessionUpdateResponse` | Update session role (affects all services) |
| `ClearsessionstateAsync` | `ClearSessionStateRequest` | `SessionUpdateResponse` | Clear session state for specific service |
| `GetSessioninfoAsync` | `SessionInfoRequest` | `SessionInfo` | Get complete session information |

---

## Bannou Realm Service API {#realm}

**Proxy**: `client.Realm` | **Version**: 1.0.0

Realm management service for game worlds.

### Realm

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetRealmAsync` | `GetRealmRequest` | `RealmResponse` | Get realm by ID |
| `GetRealmbycodeAsync` | `GetRealmByCodeRequest` | `RealmResponse` | Get realm by code |
| `ListRealmsAsync` | `ListRealmsRequest` | `RealmListResponse` | List all realms |
| `RealmexistsAsync` | `RealmExistsRequest` | `RealmExistsResponse` | Check if realm exists and is active |
| `RealmsexistbatchAsync` | `RealmsExistBatchRequest` | `RealmsExistBatchResponse` | Check if multiple realms exist and are active |

### Realm Admin

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateRealmAsync` | `CreateRealmRequest` | `RealmResponse` | Create new realm |
| `UpdateRealmAsync` | `UpdateRealmRequest` | `RealmResponse` | Update realm |
| `DeleteRealmEventAsync` | `DeleteRealmRequest` | *(fire-and-forget)* | Delete realm |
| `DeprecaterealmAsync` | `DeprecateRealmRequest` | `RealmResponse` | Deprecate a realm |
| `UndeprecaterealmAsync` | `UndeprecateRealmRequest` | `RealmResponse` | Restore a deprecated realm |
| `SeedrealmsAsync` | `SeedRealmsRequest` | `SeedRealmsResponse` | Seed realms from configuration |

---

## Bannou Realm History Service API {#realm-history}

**Proxy**: `client.RealmHistory` | **Version**: 1.0.0

Historical event participation and lore management for realms.

### Compression

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetCompressdataAsync` | `GetCompressDataRequest` | `RealmHistoryArchive` | Get realm history data for compression |
| `RestorefromarchiveAsync` | `RestoreFromArchiveRequest` | `RestoreFromArchiveResponse` | Restore realm history from archive |

### Historical Events

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `RecordrealmparticipationAsync` | `RecordRealmParticipationRequest` | `RealmHistoricalParticipation` | Record realm participation in a historical event |
| `GetRealmparticipationAsync` | `GetRealmParticipationRequest` | `RealmParticipationListResponse` | Get all historical events a realm participated in |
| `GetRealmeventparticipantsAsync` | `GetRealmEventParticipantsRequest` | `RealmParticipationListResponse` | Get all realms that participated in a historical event |
| `DeleteRealmparticipationEventAsync` | `DeleteRealmParticipationRequest` | *(fire-and-forget)* | Delete a participation record |

### History Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `DeleteAllrealmhistoryAsync` | `DeleteAllRealmHistoryRequest` | `DeleteAllRealmHistoryResponse` | Delete all history data for a realm |
| `SummarizerealmhistoryAsync` | `SummarizeRealmHistoryRequest` | `RealmHistorySummaryResponse` | Generate text summaries for realm archival |

### Lore

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetRealmloreAsync` | `GetRealmLoreRequest` | `RealmLoreResponse` | Get machine-readable lore elements for behavior system |
| `SetrealmloreAsync` | `SetRealmLoreRequest` | `RealmLoreResponse` | Set lore elements for a realm |
| `AddrealmloreelementAsync` | `AddRealmLoreElementRequest` | `RealmLoreResponse` | Add a single lore element |
| `DeleteRealmloreEventAsync` | `DeleteRealmLoreRequest` | *(fire-and-forget)* | Delete all lore for a realm |

---

## Relationship Service API {#relationship}

**Proxy**: `client.Relationship` | **Version**: 1.0.0

Generic relationship management service for entity-to-entity relationships.

### Relationship Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateRelationshipAsync` | `CreateRelationshipRequest` | `RelationshipResponse` | Create a new relationship between two entities |
| `GetRelationshipAsync` | `GetRelationshipRequest` | `RelationshipResponse` | Get a relationship by ID |
| `ListRelationshipsbyentityAsync` | `ListRelationshipsByEntityRequest` | `RelationshipListResponse` | List all relationships for an entity |
| `GetRelationshipsbetweenAsync` | `GetRelationshipsBetweenRequest` | `RelationshipListResponse` | Get all relationships between two specific entities |
| `ListRelationshipsbytypeAsync` | `ListRelationshipsByTypeRequest` | `RelationshipListResponse` | List all relationships of a specific type |
| `UpdateRelationshipAsync` | `UpdateRelationshipRequest` | `RelationshipResponse` | Update relationship metadata |
| `EndrelationshipEventAsync` | `EndRelationshipRequest` | *(fire-and-forget)* | End a relationship |

---

## Bannou RelationshipType Service API {#relationship-type}

**Proxy**: `client.RelationshipType` | **Version**: 2.0.0

Relationship type management service for game worlds.

### RelationshipType

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetRelationshiptypeAsync` | `GetRelationshipTypeRequest` | `RelationshipTypeResponse` | Get relationship type by ID |
| `GetRelationshiptypebycodeAsync` | `GetRelationshipTypeByCodeRequest` | `RelationshipTypeResponse` | Get relationship type by code |
| `ListRelationshiptypesAsync` | `ListRelationshipTypesRequest` | `RelationshipTypeListResponse` | List all relationship types |
| `GetChildrelationshiptypesAsync` | `GetChildRelationshipTypesRequest` | `RelationshipTypeListResponse` | Get child types for a parent type |
| `MatcheshierarchyAsync` | `MatchesHierarchyRequest` | `MatchesHierarchyResponse` | Check if type matches ancestor in hierarchy |
| `GetAncestorsAsync` | `GetAncestorsRequest` | `RelationshipTypeListResponse` | Get all ancestors of a relationship type |

### RelationshipType Admin

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateRelationshiptypeAsync` | `CreateRelationshipTypeRequest` | `RelationshipTypeResponse` | Create new relationship type |
| `UpdateRelationshiptypeAsync` | `UpdateRelationshipTypeRequest` | `RelationshipTypeResponse` | Update relationship type |
| `DeleteRelationshiptypeEventAsync` | `DeleteRelationshipTypeRequest` | *(fire-and-forget)* | Delete relationship type |
| `DeprecaterelationshiptypeAsync` | `DeprecateRelationshipTypeRequest` | `RelationshipTypeResponse` | Deprecate a relationship type |
| `UndeprecaterelationshiptypeAsync` | `UndeprecateRelationshipTypeRequest` | `RelationshipTypeResponse` | Restore a deprecated relationship type |
| `MergerelationshiptypeAsync` | `MergeRelationshipTypeRequest` | `MergeRelationshipTypeResponse` | Merge a deprecated type into another type |
| `SeedrelationshiptypesAsync` | `SeedRelationshipTypesRequest` | `SeedRelationshipTypesResponse` | Seed relationship types from configuration |

---

## Resource Lifecycle API {#resource}

**Proxy**: `client.Resource` | **Version**: 1.0.0

Resource reference tracking and lifecycle management.

### Cleanup Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `DefinecleanupcallbackAsync` | `DefineCleanupRequest` | `DefineCleanupResponse` | Define cleanup callbacks for a resource type |
| `ExecutecleanupAsync` | `ExecuteCleanupRequest` | `ExecuteCleanupResponse` | Execute cleanup for a resource |
| `ListCleanupcallbacksAsync` | `ListCleanupCallbacksRequest` | `ListCleanupCallbacksResponse` | List registered cleanup callbacks |
| `RemovecleanupcallbackAsync` | `RemoveCleanupCallbackRequest` | `RemoveCleanupCallbackResponse` | Remove a cleanup callback registration |

### Compression Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `DefinecompresscallbackAsync` | `DefineCompressCallbackRequest` | `DefineCompressCallbackResponse` | Register compression callback for a resource type |
| `ExecutecompressAsync` | `ExecuteCompressRequest` | `ExecuteCompressResponse` | Compress a resource and all dependents |
| `ExecutedecompressAsync` | `ExecuteDecompressRequest` | `ExecuteDecompressResponse` | Restore data from archive |
| `ListCompresscallbacksAsync` | `ListCompressCallbacksRequest` | `ListCompressCallbacksResponse` | List registered compression callbacks |
| `GetArchiveAsync` | `GetArchiveRequest` | `GetArchiveResponse` | Retrieve compressed archive |

### Reference Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `RegisterReferenceAsync` | `RegisterReferenceRequest` | `RegisterReferenceResponse` | Register a reference to a resource |
| `UnregisterreferenceAsync` | `UnregisterReferenceRequest` | `UnregisterReferenceResponse` | Remove a reference to a resource |
| `CheckreferencesAsync` | `CheckReferencesRequest` | `CheckReferencesResponse` | Check reference count and cleanup eligibility |
| `ListReferencesAsync` | `ListReferencesRequest` | `ListReferencesResponse` | List all references to a resource |

### Snapshot Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ExecutesnapshotAsync` | `ExecuteSnapshotRequest` | `ExecuteSnapshotResponse` | Create ephemeral snapshot of a living resource |
| `GetSnapshotAsync` | `GetSnapshotRequest` | `GetSnapshotResponse` | Retrieve an ephemeral snapshot |

---

## Save-Load Service API {#save-load}

**Proxy**: `client.SaveLoad` | **Version**: 1.0.0

Generic save/load system for game state persistence. Supports polymorphic ownership, versioned saves, and schema migration.

### Admin

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `AdmincleanupAsync` | `AdminCleanupRequest` | `AdminCleanupResponse` | Run cleanup for expired/orphaned saves |
| `AdminstatsAsync` | `AdminStatsRequest` | `AdminStatsResponse` | Get storage statistics |

### Migration

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `MigratesaveAsync` | `MigrateSaveRequest` | `MigrateSaveResponse` | Migrate save to new schema version |
| `RegisterSchemaAsync` | `RegisterSchemaRequest` | `SchemaResponse` | Register a save data schema |
| `ListSchemasAsync` | `ListSchemasRequest` | `ListSchemasResponse` | List registered schemas |

### Query

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `QuerysavesAsync` | `QuerySavesRequest` | `QuerySavesResponse` | Query saves with filters |

### Saves

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `SaveAsync` | `SaveRequest` | `SaveResponse` | Save data to slot |
| `LoadAsync` | `LoadRequest` | `LoadResponse` | Load data from slot |
| `SavedeltaAsync` | `SaveDeltaRequest` | `SaveDeltaResponse` | Save incremental changes from base version |
| `LoadwithdeltasAsync` | `LoadRequest` | `LoadResponse` | Load save reconstructing from delta chain |
| `CollapsedeltasAsync` | `CollapseDeltasRequest` | `SaveResponse` | Collapse delta chain into full snapshot |

### Slots

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateSlotAsync` | `CreateSlotRequest` | `SlotResponse` | Create or configure a save slot |
| `GetSlotAsync` | `GetSlotRequest` | `SlotResponse` | Get slot metadata |
| `ListSlotsAsync` | `ListSlotsRequest` | `ListSlotsResponse` | List slots for owner |
| `DeleteSlotAsync` | `DeleteSlotRequest` | `DeleteSlotResponse` | Delete slot and all versions |
| `RenameslotAsync` | `RenameSlotRequest` | `SlotResponse` | Rename a save slot |
| `BulkdeleteslotsAsync` | `BulkDeleteSlotsRequest` | `BulkDeleteSlotsResponse` | Delete multiple slots at once |

### Transfer

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CopysaveAsync` | `CopySaveRequest` | `SaveResponse` | Copy save to different slot or owner |
| `ExportsavesAsync` | `ExportSavesRequest` | `ExportSavesResponse` | Export saves for backup/portability |
| `ImportsavesAsync` | `ImportSavesRequest` | `ImportSavesResponse` | Import saves from backup |

### Validation

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `VerifyintegrityAsync` | `VerifyIntegrityRequest` | `VerifyIntegrityResponse` | Verify save data integrity |

### Versions

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ListVersionsAsync` | `ListVersionsRequest` | `ListVersionsResponse` | List versions in slot |
| `PinversionAsync` | `PinVersionRequest` | `VersionResponse` | Pin a version as checkpoint |
| `UnpinversionAsync` | `UnpinVersionRequest` | `VersionResponse` | Unpin a version |
| `DeleteVersionAsync` | `DeleteVersionRequest` | `DeleteVersionResponse` | Delete specific version |
| `PromoteversionAsync` | `PromoteVersionRequest` | `SaveResponse` | Promote old version to latest |

---

## Bannou Scene Service API {#scene}

**Proxy**: `client.Scene` | **Version**: 1.0.0

Hierarchical composition storage for game worlds.

### Instance

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `InstantiatesceneAsync` | `InstantiateSceneRequest` | `InstantiateSceneResponse` | Declare that a scene was instantiated in the game world |
| `DestroyinstanceAsync` | `DestroyInstanceRequest` | `DestroyInstanceResponse` | Declare that a scene instance was removed |

### Query

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `SearchscenesAsync` | `SearchScenesRequest` | `SearchScenesResponse` | Full-text search across scenes |
| `FindreferencesAsync` | `FindReferencesRequest` | `FindReferencesResponse` | Find scenes that reference a given scene |
| `FindassetusageAsync` | `FindAssetUsageRequest` | `FindAssetUsageResponse` | Find scenes using a specific asset |

### Scene

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateSceneAsync` | `CreateSceneRequest` | `SceneResponse` | Create a new scene document |
| `GetSceneAsync` | `GetSceneRequest` | `GetSceneResponse` | Retrieve a scene by ID |
| `ListScenesAsync` | `ListScenesRequest` | `ListScenesResponse` | List scenes with filtering |
| `UpdateSceneAsync` | `UpdateSceneRequest` | `SceneResponse` | Update a scene document |
| `DeleteSceneAsync` | `DeleteSceneRequest` | `DeleteSceneResponse` | Delete a scene |
| `ValidateSceneAsync` | `ValidateSceneRequest` | `ValidationResult` | Validate a scene structure |
| `DuplicatesceneAsync` | `DuplicateSceneRequest` | `SceneResponse` | Duplicate a scene with a new ID |

### Validation

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `RegisterValidationrulesAsync` | `RegisterValidationRulesRequest` | `RegisterValidationRulesResponse` | Register validation rules for a gameId+sceneType |
| `GetValidationrulesAsync` | `GetValidationRulesRequest` | `GetValidationRulesResponse` | Get validation rules for a gameId+sceneType |

### Versioning

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CheckoutsceneAsync` | `CheckoutRequest` | `CheckoutResponse` | Lock a scene for editing |
| `CommitsceneAsync` | `CommitRequest` | `CommitResponse` | Save changes and release lock |
| `DiscardcheckoutAsync` | `DiscardRequest` | `DiscardResponse` | Release lock without saving changes |
| `HeartbeatcheckoutAsync` | `HeartbeatRequest` | `HeartbeatResponse` | Extend checkout lock TTL |
| `GetScenehistoryAsync` | `HistoryRequest` | `HistoryResponse` | Get version history for a scene |

---

## Bannou Species Service API {#species}

**Proxy**: `client.Species` | **Version**: 2.0.0

Species management service for game worlds.

### Species

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetSpeciesAsync` | `GetSpeciesRequest` | `SpeciesResponse` | Get species by ID |
| `GetSpeciesbycodeAsync` | `GetSpeciesByCodeRequest` | `SpeciesResponse` | Get species by code |
| `ListSpeciesAsync` | `ListSpeciesRequest` | `SpeciesListResponse` | List all species |
| `ListSpeciesbyrealmAsync` | `ListSpeciesByRealmRequest` | `SpeciesListResponse` | List species available in a realm |

### Species Admin

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateSpeciesAsync` | `CreateSpeciesRequest` | `SpeciesResponse` | Create new species |
| `UpdateSpeciesAsync` | `UpdateSpeciesRequest` | `SpeciesResponse` | Update species |
| `DeleteSpeciesEventAsync` | `DeleteSpeciesRequest` | *(fire-and-forget)* | Delete species |
| `DeprecatespeciesAsync` | `DeprecateSpeciesRequest` | `SpeciesResponse` | Deprecate a species |
| `UndeprecatespeciesAsync` | `UndeprecateSpeciesRequest` | `SpeciesResponse` | Restore a deprecated species |
| `MergespeciesAsync` | `MergeSpeciesRequest` | `MergeSpeciesResponse` | Merge a deprecated species into another species |
| `AddspeciestorealmAsync` | `AddSpeciesToRealmRequest` | `SpeciesResponse` | Add species to a realm |
| `RemovespeciesfromrealmAsync` | `RemoveSpeciesFromRealmRequest` | `SpeciesResponse` | Remove species from a realm |
| `SeedspeciesAsync` | `SeedSpeciesRequest` | `SeedSpeciesResponse` | Seed species from configuration |

---

## Bannou State Service API {#state}

**Proxy**: `client.State` | **Version**: 1.0.0

Repository pattern state management with Redis and MySQL backends.

### State

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetStateAsync` | `GetStateRequest` | `GetStateResponse` | Get state value by key |
| `SavestateAsync` | `SaveStateRequest` | `SaveStateResponse` | Save state value |
| `DeleteStateAsync` | `DeleteStateRequest` | `DeleteStateResponse` | Delete state value |
| `QuerystateAsync` | `QueryStateRequest` | `QueryStateResponse` | Query state (MySQL JSON queries or Redis with search enabled) |
| `BulkgetstateAsync` | `BulkGetStateRequest` | `BulkGetStateResponse` | Bulk get multiple keys |
| `BulksavestateAsync` | `BulkSaveStateRequest` | `BulkSaveStateResponse` | Bulk save multiple key-value pairs |
| `BulkexistsstateAsync` | `BulkExistsStateRequest` | `BulkExistsStateResponse` | Check existence of multiple keys |
| `BulkdeletestateAsync` | `BulkDeleteStateRequest` | `BulkDeleteStateResponse` | Delete multiple keys |
| `ListStoresAsync` | `ListStoresRequest` | `ListStoresResponse` | List configured state stores |

---

## Storyline Composer API {#storyline}

**Proxy**: `client.Storyline` | **Version**: 1.0.0

Seeded narrative generation from compressed archives using the storyline SDKs.

### Composition

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `ComposeAsync` | `ComposeRequest` | `ComposeResponse` | Compose a storyline plan from archive seeds |

### Plans

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetPlanAsync` | `GetPlanRequest` | `GetPlanResponse` | Retrieve a cached storyline plan |
| `ListPlansAsync` | `ListPlansRequest` | `ListPlansResponse` | List cached storyline plans |

---

## Bannou Subscription Service API {#subscription}

**Proxy**: `client.Subscription` | **Version**: 1.0.0

Manages user subscriptions to game services. Tracks which accounts have access to which services (games/applications) with time-limited subscriptions.

### Subscription Management

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetAccountsubscriptionsAsync` | `GetAccountSubscriptionsRequest` | `SubscriptionListResponse` | Get subscriptions for an account |
| `QuerycurrentsubscriptionsAsync` | `QueryCurrentSubscriptionsRequest` | `QuerySubscriptionsResponse` | Query current (active, non-expired) subscriptions |
| `GetSubscriptionAsync` | `GetSubscriptionRequest` | `SubscriptionInfo` | Get a specific subscription by ID |
| `CreateSubscriptionAsync` | `CreateSubscriptionRequest` | `SubscriptionInfo` | Create a new subscription |
| `UpdateSubscriptionAsync` | `UpdateSubscriptionRequest` | `SubscriptionInfo` | Update a subscription |
| `CancelsubscriptionAsync` | `CancelSubscriptionRequest` | `SubscriptionInfo` | Cancel a subscription |
| `RenewsubscriptionAsync` | `RenewSubscriptionRequest` | `SubscriptionInfo` | Renew or extend a subscription |

---

## Bannou Telemetry Service API {#telemetry}

**Proxy**: `client.Telemetry` | **Version**: 1.0.0

Unified observability plugin providing distributed tracing, metrics, and log correlation for Bannou services using OpenTelemetry standards.

### Telemetry

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `HealthAsync` | `TelemetryHealthRequest` | `TelemetryHealthResponse` | Check telemetry exporter health |
| `StatusAsync` | `TelemetryStatusRequest` | `TelemetryStatusResponse` | Get telemetry status and configuration |

---

## Bannou Voice Service API {#voice}

**Proxy**: `client.Voice` | **Version**: 1.1.0

Voice communication coordination service for P2P and room-based audio.

### Voice Peers

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `PeerheartbeatEventAsync` | `PeerHeartbeatRequest` | *(fire-and-forget)* | Update peer endpoint TTL |
| `AnswerpeerEventAsync` | `AnswerPeerRequest` | *(fire-and-forget)* | Send SDP answer to complete WebRTC handshake |

### Voice Rooms

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreateVoiceroomAsync` | `CreateVoiceRoomRequest` | `VoiceRoomResponse` | Create voice room for a game session |
| `GetVoiceroomAsync` | `GetVoiceRoomRequest` | `VoiceRoomResponse` | Get voice room details |
| `JoinvoiceroomAsync` | `JoinVoiceRoomRequest` | `JoinVoiceRoomResponse` | Join voice room and register SIP endpoint |
| `LeavevoiceroomEventAsync` | `LeaveVoiceRoomRequest` | *(fire-and-forget)* | Leave voice room |
| `DeleteVoiceroomEventAsync` | `DeleteVoiceRoomRequest` | *(fire-and-forget)* | Delete voice room |

---

## Bannou Website Service API {#website}

**Proxy**: `client.Website` | **Version**: 1.0.0

Public-facing website service for registration, information, and account management.

### Account

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetAccountprofileAsync` | - | `AccountProfile` | Get account profile for logged-in user |

### CMS

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CreatePageAsync` | `PageContent` | `PageContent` | Create new CMS page |
| `UpdatePageAsync` | `PageContent` | `PageContent` | Update CMS page |
| `GetSitesettingsAsync` | - | `SiteSettings` | Get site configuration |
| `UpdateSitesettingsAsync` | `SiteSettings` | `SiteSettings` | Update site configuration |
| `GetThemeAsync` | - | `ThemeConfig` | Get current theme configuration |
| `UpdateThemeEventAsync` | `ThemeConfig` | *(fire-and-forget)* | Update theme configuration |

### Contact

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `SubmitcontactAsync` | `ContactRequest` | `ContactResponse` | Submit contact form |

### Content

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetPagecontentAsync` | - | `PageContent` | Get dynamic page content from CMS |
| `GetNewsAsync` | - | `NewsResponse` | Get latest news and announcements |

### Downloads

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetDownloadsAsync` | - | `DownloadsResponse` | Get download links for game clients |

### Status

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetStatusAsync` | - | `StatusResponse` | Get website status and version |

---

## Summary

- **Total services**: 43
- **Total methods**: 572

---

*This file is auto-generated from OpenAPI schemas. See [TENETS.md](reference/TENETS.md) for architectural context.*
