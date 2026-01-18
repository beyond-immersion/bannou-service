# Generated Client API Reference

> **Source**: `schemas/*-api.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists all typed proxy methods available in the Bannou Client SDK.

## Quick Reference

| Service | Proxy Property | Methods | Description |
|---------|---------------|---------|-------------|
| [Bannou Account Service API](#account) | `client.Account` | 13 | Internal account management service (CRUD operations only, n... |
| [Bannou Achievement Service API](#achievement) | `client.Achievement` | 11 | Achievement and trophy system with progress tracking and pla... |
| [Actor Service API](#actor) | `client.Actor` | 15 | Distributed actor management and execution for NPC brains, e... |
| [Bannou Analytics Service API](#analytics) | `client.Analytics` | 8 | Event ingestion, entity statistics, skill ratings (Glicko-2)... |
| [Asset Service API](#asset) | `client.Asset` | 20 | Asset management service for storage, versioning, and distri... |
| [Bannou Auth Service API](#auth) | `client.Auth` | 12 | Authentication and session management service (Internet-faci... |
| [ABML Behavior Management API](#behavior) | `client.Behavior` | 6 | Arcadia Behavior Markup Language (ABML) API for character be... |
| [Bannou Character Service API](#character) | `client.Character` | 10 | Character management service for Arcadia game world. |
| [Bannou Character History Service API](#character-history) | `client.CharacterHistory` | 10 | Historical event participation and backstory management for ... |
| [Bannou Character Personality Service API](#character-personality) | `client.CharacterPersonality` | 9 | Machine-readable personality traits for NPC behavior decisio... |
| [Bannou Connect API](#connect) | `client.Connect` | 4 | Real-time communication and WebSocket connection management ... |
| [Bannou Documentation API](#documentation) | `client.Documentation` | 25 | Knowledge base API for AI agents to query documentation. Des... |
| [Bannou Game Service API](#game-service) | `client.GameService` | 5 | Registry service for game services that users can subscribe ... |
| [Bannou Game Session Service API](#game-session) | `client.GameSession` | 11 | Minimal game session management for Arcadia and other games. |
| [Bannou Leaderboard Service API](#leaderboard) | `client.Leaderboard` | 12 | Real-time leaderboard management using Redis Sorted Sets for... |
| [Bannou Location Service API](#location) | `client.Location` | 17 | Location management service for Arcadia game world. |
| [Bannou Mapping Service API](#mapping) | `client.Mapping` | 18 | Spatial data management service for Arcadia game worlds. |
| [Bannou Matchmaking Service API](#matchmaking) | `client.Matchmaking` | 11 | Matchmaking service for competitive and casual game matching... |
| [Bannou Mesh Service API](#mesh) | `client.Mesh` | 8 | Native service mesh plugin providing direct service-to-servi... |
| [Bannou Messaging Service API](#messaging) | `client.Messaging` | 4 | Native RabbitMQ pub/sub messaging with native serialization. |
| [Music Theory Engine API](#music) | `client.Music` | 8 | Pure computation music generation using formal music theory ... |
| [Orchestrator API](#orchestrator) | `client.Orchestrator` | 22 | Central intelligence for Bannou environment management and s... |
| [Bannou Permission System API](#permission) | `client.Permission` | 8 | Redis-backed high-performance permission system for WebSocke... |
| [Bannou Realm Service API](#realm) | `client.Realm` | 10 | Realm management service for Arcadia game world. |
| [Bannou Realm History Service API](#realm-history) | `client.RealmHistory` | 10 | Historical event participation and lore management for realm... |
| [Relationship Service API](#relationship) | `client.Relationship` | 7 | Generic relationship management service for entity-to-entity... |
| [Bannou RelationshipType Service API](#relationship-type) | `client.RelationshipType` | 13 | Relationship type management service for Arcadia game world. |
| [Save-Load Service API](#save-load) | `client.SaveLoad` | 26 | Generic save/load system for game state persistence. Support... |
| [Bannou Scene Service API](#scene) | `client.Scene` | 19 | Hierarchical composition storage for game worlds. |
| [Bannou Species Service API](#species) | `client.Species` | 13 | Species management service for Arcadia game world. |
| [Bannou State Service API](#state) | `client.State` | 6 | Repository pattern state management with Redis and MySQL bac... |
| [Bannou Subscription Service API](#subscription) | `client.Subscription` | 7 | Manages user subscriptions to game services. Tracks which ac... |
| [Bannou Voice Service API](#voice) | `client.Voice` | 7 | Voice communication coordination service for P2P and room-ba... |
| [Bannou Website Service API](#website) | `client.Website` | 15 | Public-facing website service for registration, information,... |

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

Character management service for Arcadia game world.

### Character Compression

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `CompresscharacterAsync` | `CompressCharacterRequest` | `CharacterArchive` | Compress a dead character to archive format |
| `GetCharacterarchiveAsync` | `GetCharacterArchiveRequest` | `CharacterArchive` | Get compressed archive data for a character |
| `CheckcharacterreferencesAsync` | `CheckReferencesRequest` | `CharacterRefCount` | Check reference count for cleanup eligibility |

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

## Bannou Game Service API {#game-service}

**Proxy**: `client.GameService` | **Version**: 1.0.0

Registry service for game services that users can subscribe to. Provides a minimal registry of available services (games/applications) like Arcadia...

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

Minimal game session management for Arcadia and other games.

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

Location management service for Arcadia game world.

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

Spatial data management service for Arcadia game worlds.

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

Realm management service for Arcadia game world.

### Realm

| Method | Request | Response | Summary |
|--------|---------|----------|---------|
| `GetRealmAsync` | `GetRealmRequest` | `RealmResponse` | Get realm by ID |
| `GetRealmbycodeAsync` | `GetRealmByCodeRequest` | `RealmResponse` | Get realm by code |
| `ListRealmsAsync` | `ListRealmsRequest` | `RealmListResponse` | List all realms |
| `RealmexistsAsync` | `RealmExistsRequest` | `RealmExistsResponse` | Check if realm exists and is active |

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

Relationship type management service for Arcadia game world.

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

Species management service for Arcadia game world.

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
| `ListStoresAsync` | `ListStoresRequest` | `ListStoresResponse` | List configured state stores |

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
| `GetAccountcharactersAsync` | - | `CharacterListResponse` | Get character list for logged-in user |
| `GetSubscriptionAsync` | - | `SubscriptionResponse` | Get subscription status |

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
| `GetServerstatusAsync` | - | `ServerStatusResponse` | Get game server status for all realms |

---

## Summary

- **Total services**: 34
- **Total methods**: 400

---

*This file is auto-generated from OpenAPI schemas. See [TENETS.md](reference/TENETS.md) for architectural context.*
