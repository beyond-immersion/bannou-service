# Generated Service Details Reference

> **Source**: `schemas/*-api.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document provides a compact reference of all Bannou services and their API endpoints.

## Service Overview

| Service | Version | Endpoints | Description |
|---------|---------|-----------|-------------|
| [Account](#account) | 2.0.0 | 16 | Internal account management service (CRUD operations only, n... |
| [Achievement](#achievement) | 1.0.0 | 11 | Achievement and trophy system with progress tracking and pla... |
| [Actor](#actor) | 1.0.0 | 15 | Distributed actor management and execution for NPC brains, e... |
| [Analytics](#analytics) | 1.0.0 | 8 | Event ingestion, entity statistics, skill ratings (Glicko-2)... |
| [Asset](#asset) | 1.0.0 | 20 | Asset management service for storage, versioning, and distri... |
| [Auth](#auth) | 4.0.0 | 13 | Authentication and session management service (Internet-faci... |
| [Behavior](#behavior) | 3.0.0 | 6 | Arcadia Behavior Markup Language (ABML) API for character be... |
| [Character](#character) | 1.0.0 | 10 | Character management service for Arcadia game world. |
| [Character Encounter](#character-encounter) | 1.0.0 | 19 | Character encounter tracking service for memorable interacti... |
| [Character History](#character-history) | 1.0.0 | 10 | Historical event participation and backstory management for ... |
| [Character Personality](#character-personality) | 1.0.0 | 9 | Machine-readable personality traits for NPC behavior decisio... |
| [Connect](#connect) | 2.0.0 | 5 | Real-time communication and WebSocket connection management ... |
| [Contract](#contract) | 1.0.0 | 30 | Binding agreements between entities with milestone-based pro... |
| [Currency](#currency) | 1.0.0 | 32 | Multi-currency management service for game economies. |
| [Documentation](#documentation) | 1.0.0 | 27 | Knowledge base API for AI agents to query documentation.
Des... |
| [Escrow](#escrow) | 1.0.0 | 20 | Full-custody orchestration layer for multi-party asset excha... |
| [Game Service](#game-service) | 1.0.0 | 5 | Registry service for game services that users can subscribe ... |
| [Game Session](#game-session) | 2.0.0 | 11 | Minimal game session management for Arcadia and other games. |
| [Inventory](#inventory) | 1.0.0 | 16 | Container and inventory management service for games. |
| [Item](#item) | 1.0.0 | 13 | Item template and instance management service. |
| [Leaderboard](#leaderboard) | 1.0.0 | 12 | Real-time leaderboard management using Redis Sorted Sets for... |
| [Location](#location) | 1.0.0 | 17 | Location management service for Arcadia game world. |
| [Mapping](#mapping) | 1.0.0 | 18 | Spatial data management service for Arcadia game worlds. |
| [Matchmaking](#matchmaking) | 1.0.0 | 11 | Matchmaking service for competitive and casual game matching... |
| [Mesh](#mesh) | 1.0.0 | 8 | Native service mesh plugin providing direct service-to-servi... |
| [Messaging](#messaging) | 1.0.0 | 4 | Native RabbitMQ pub/sub messaging with native serialization. |
| [Music](#music) | 1.0.0 | 8 | Pure computation music generation using formal music theory ... |
| [Orchestrator](#orchestrator) | 3.0.0 | 22 | Central intelligence for Bannou environment management and s... |
| [Permission](#permission) | 3.0.0 | 8 | Redis-backed high-performance permission system for WebSocke... |
| [Realm](#realm) | 1.0.0 | 10 | Realm management service for Arcadia game world. |
| [Realm History](#realm-history) | 1.0.0 | 10 | Historical event participation and lore management for realm... |
| [Relationship](#relationship) | 1.0.0 | 7 | Generic relationship management service for entity-to-entity... |
| [Relationship Type](#relationship-type) | 2.0.0 | 13 | Relationship type management service for Arcadia game world. |
| [Save Load](#save-load) | 1.0.0 | 26 | Generic save/load system for game state persistence.
Support... |
| [Scene](#scene) | 1.0.0 | 19 | Hierarchical composition storage for game worlds. |
| [Species](#species) | 2.0.0 | 13 | Species management service for Arcadia game world. |
| [State](#state) | 1.0.0 | 6 | Repository pattern state management with Redis and MySQL bac... |
| [Subscription](#subscription) | 1.0.0 | 7 | Manages user subscriptions to game services.
Tracks which ac... |
| [Voice](#voice) | 1.1.0 | 7 | Voice communication coordination service for P2P and room-ba... |
| [Website](#website) | 1.0.0 | 17 | Public-facing website service for registration, information,... |

---

## Account {#account}

**Version**: 2.0.0 | **Schema**: `schemas/account-api.yaml` | **Deep Dive**: [docs/plugins/ACCOUNT.md](plugins/ACCOUNT.md)

Internal account management service (CRUD operations only, never exposed to internet).

### Account Lookup

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/account/by-email` | Get account by email | admin |
| `POST` | `/account/by-provider` | Get account by external provider ID | admin |

### Account Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/account/batch-get` | Get multiple accounts by ID | admin |
| `POST` | `/account/count` | Count accounts matching filters | admin |
| `POST` | `/account/create` | Create new account | admin |
| `POST` | `/account/delete` | Delete account | admin |
| `POST` | `/account/get` | Get account by ID | admin |
| `POST` | `/account/list` | List accounts with filtering | admin |
| `POST` | `/account/password/update` | Update account password hash | user |
| `POST` | `/account/roles/bulk-update` | Bulk update roles for multiple accounts | admin |
| `POST` | `/account/update` | Update account | admin |
| `POST` | `/account/verification/update` | Update email verification status | user |

### Authentication Methods

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/account/auth-methods/add` | Add authentication method to account | admin |
| `POST` | `/account/auth-methods/list` | Get authentication methods for account | admin |
| `POST` | `/account/auth-methods/remove` | Remove authentication method from account | admin |

### Profile Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/account/profile/update` | Update account profile | user |

---

## Achievement {#achievement}

**Version**: 1.0.0 | **Schema**: `schemas/achievement-api.yaml` | **Deep Dive**: [docs/plugins/ACHIEVEMENT.md](plugins/ACHIEVEMENT.md)

Achievement and trophy system with progress tracking and platform synchronization.

### Definitions

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/achievement/definition/create` | Create a new achievement definition | developer |
| `POST` | `/achievement/definition/delete` | Delete achievement definition | developer |
| `POST` | `/achievement/definition/get` | Get achievement definition | authenticated |
| `POST` | `/achievement/definition/list` | List achievement definitions | user |
| `POST` | `/achievement/definition/update` | Update achievement definition | developer |

### Platform Sync

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/achievement/platform/status` | Get platform sync status | authenticated |
| `POST` | `/achievement/platform/sync` | Manually trigger platform sync | admin |

### Progress

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/achievement/list-unlocked` | List unlocked achievements | user |
| `POST` | `/achievement/progress/get` | Get entity's achievement progress | user |
| `POST` | `/achievement/progress/update` | Update achievement progress | authenticated |
| `POST` | `/achievement/unlock` | Directly unlock an achievement | authenticated |

---

## Actor {#actor}

**Version**: 1.0.0 | **Schema**: `schemas/actor-api.yaml` | **Deep Dive**: [docs/plugins/ACTOR.md](plugins/ACTOR.md)

Distributed actor management and execution for NPC brains, event coordinators,
and other long-running behavior loops. Actors output behavioral state (feelings,
goals, memories) to characters - NOT ...

### Other

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/actor/encounter/end` | End an active encounter | developer |
| `POST` | `/actor/encounter/get` | Get the current encounter state for an actor | admin |
| `POST` | `/actor/encounter/start` | Start an encounter managed by an Event Brain actor | developer |
| `POST` | `/actor/encounter/update-phase` | Update the phase of an active encounter | developer |
| `POST` | `/actor/get` | Get actor instance (instantiate-on-access if template allows) | admin |
| `POST` | `/actor/inject-perception` | Inject a perception event into an actor's queue (testing) | developer |
| `POST` | `/actor/list` | List actors with optional filters | admin |
| `POST` | `/actor/query-options` | Query an actor for its available options | authenticated |
| `POST` | `/actor/spawn` | Spawn a new actor from a template | developer |
| `POST` | `/actor/stop` | Stop a running actor | developer |
| `POST` | `/actor/template/create` | Create an actor template (category definition) | developer |
| `POST` | `/actor/template/delete` | Delete an actor template | developer |
| `POST` | `/actor/template/get` | Get an actor template by ID or category | admin |
| `POST` | `/actor/template/list` | List all actor templates | admin |
| `POST` | `/actor/template/update` | Update an actor template | developer |

---

## Analytics {#analytics}

**Version**: 1.0.0 | **Schema**: `schemas/analytics-api.yaml` | **Deep Dive**: [docs/plugins/ANALYTICS.md](plugins/ANALYTICS.md)

Event ingestion, entity statistics, skill ratings (Glicko-2), and controller history tracking.

### Controller History

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/analytics/controller-history/query` | Query controller history | admin |
| `POST` | `/analytics/controller-history/record` | Record controller possession event | authenticated |

### Event Ingestion

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/analytics/event/ingest` | Ingest a single analytics event | authenticated |
| `POST` | `/analytics/event/ingest-batch` | Ingest multiple analytics events | authenticated |

### Skill Ratings

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/analytics/rating/get` | Get entity Glicko-2 skill rating | admin |
| `POST` | `/analytics/rating/update` | Update entity skill rating after match | authenticated |

### Statistics

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/analytics/summary/get` | Get entity statistics summary | admin |
| `POST` | `/analytics/summary/query` | Query entity summaries with filters | admin |

---

## Asset {#asset}

**Version**: 1.0.0 | **Schema**: `schemas/asset-api.yaml` | **Deep Dive**: [docs/plugins/ASSET.md](plugins/ASSET.md)

Asset management service for storage, versioning, and distribution of large binary assets.

### Assets

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/assets/bulk-get` | Batch asset metadata lookup | user |
| `POST` | `/assets/delete` | Delete an asset | admin |
| `POST` | `/assets/get` | Get asset metadata and download URL | user |
| `POST` | `/assets/list-versions` | List all versions of an asset | user |
| `POST` | `/assets/search` | Search assets by tags, type, or realm | user |
| `POST` | `/assets/upload/complete` | Mark upload as complete, trigger processing | user |
| `POST` | `/assets/upload/request` | Request upload URL for a new asset | user |

### Bundles

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/bundles/create` | Create asset bundle from multiple assets | user |
| `POST` | `/bundles/delete` | Soft-delete a bundle | user |
| `POST` | `/bundles/get` | Get bundle manifest and download URL | user |
| `POST` | `/bundles/job/cancel` | Cancel an async metabundle job | user |
| `POST` | `/bundles/job/status` | Get async metabundle job status | user |
| `POST` | `/bundles/list-versions` | List version history for a bundle | user |
| `POST` | `/bundles/metabundle/create` | Create metabundle from source bundles | user |
| `POST` | `/bundles/query` | Query bundles with advanced filters | user |
| `POST` | `/bundles/query/by-asset` | Find all bundles containing a specific asset | user |
| `POST` | `/bundles/resolve` | Compute optimal bundles for requested assets | user |
| `POST` | `/bundles/restore` | Restore a soft-deleted bundle | user |
| `POST` | `/bundles/update` | Update bundle metadata | user |
| `POST` | `/bundles/upload/request` | Request upload URL for a pre-made bundle | user |

---

## Auth {#auth}

**Version**: 4.0.0 | **Schema**: `schemas/auth-api.yaml` | **Deep Dive**: [docs/plugins/AUTH.md](plugins/AUTH.md)

Authentication and session management service (Internet-facing).

### Authentication

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/auth/login` | Login with email/password | anonymous |
| `POST` | `/auth/logout` | Logout and invalidate tokens | user |
| `POST` | `/auth/providers` | List available authentication providers | anonymous |
| `POST` | `/auth/register` | Register new user account | anonymous |

### OAuth

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/auth/oauth/{provider}/callback` | Complete OAuth2 flow (browser redirect callback) | anonymous |
| `GET` | `/auth/oauth/{provider}/init` | Initialize OAuth2 flow (browser redirect) | anonymous |

### Password

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/auth/password/confirm` | Confirm password reset with token | anonymous |
| `POST` | `/auth/password/reset` | Request password reset | anonymous |

### Sessions

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/auth/sessions/list` | Get active sessions for account | user |
| `POST` | `/auth/sessions/terminate` | Terminate specific session | user |

### Steam

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/auth/steam/verify` | Verify Steam Session Ticket | anonymous |

### Tokens

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/auth/refresh` | Refresh access token | user |
| `POST` | `/auth/validate` | Validate access token | user |

---

## Behavior {#behavior}

**Version**: 3.0.0 | **Schema**: `schemas/behavior-api.yaml` | **Deep Dive**: [docs/plugins/BEHAVIOR.md](plugins/BEHAVIOR.md)

Arcadia Behavior Markup Language (ABML) API for character behavior management.

### ABML

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/compile` | Compile ABML behavior definition | developer |

### Cache

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/cache/get` | Get cached compiled behavior | developer |
| `POST` | `/cache/invalidate` | Invalidate cached behavior | developer |

### GOAP

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/goap/plan` | Generate GOAP plan | developer |
| `POST` | `/goap/validate-plan` | Validate existing GOAP plan | developer |

### Validation

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/validate` | Validate ABML definition | developer |

---

## Character {#character}

**Version**: 1.0.0 | **Schema**: `schemas/character-api.yaml` | **Deep Dive**: [docs/plugins/CHARACTER.md](plugins/CHARACTER.md)

Character management service for Arcadia game world.

### Character Compression

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character/check-references` | Check reference count for cleanup eligibility | admin |
| `POST` | `/character/compress` | Compress a dead character to archive format | admin |
| `POST` | `/character/get-archive` | Get compressed archive data for a character | user |

### Character Lookup

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character/by-realm` | Get all characters in a realm (primary query pattern) | user |
| `POST` | `/character/get-enriched` | Get character with optional related data (personality, backstory, family) | user |

### Character Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character/create` | Create new character | admin |
| `POST` | `/character/delete` | Delete character (permanent removal) | admin |
| `POST` | `/character/get` | Get character by ID | user |
| `POST` | `/character/list` | List characters with filtering | user |
| `POST` | `/character/update` | Update character | admin |

---

## Character Encounter {#character-encounter}

**Version**: 1.0.0 | **Schema**: `schemas/character-encounter-api.yaml` | **Deep Dive**: [docs/plugins/CHARACTER-ENCOUNTER.md](plugins/CHARACTER-ENCOUNTER.md)

Character encounter tracking service for memorable interactions between characters.

### Admin

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character-encounter/decay-memories` | Trigger memory decay (maintenance) | admin |
| `POST` | `/character-encounter/delete` | Delete encounter and perspectives | admin |
| `POST` | `/character-encounter/delete-by-character` | Delete all encounters for a character | admin |

### Encounter Type Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character-encounter/type/create` | Create new encounter type | admin |
| `POST` | `/character-encounter/type/delete` | Delete encounter type | admin |
| `POST` | `/character-encounter/type/get` | Get encounter type by code | user |
| `POST` | `/character-encounter/type/list` | List all encounter types | user |
| `POST` | `/character-encounter/type/seed` | Seed default encounter types | admin |
| `POST` | `/character-encounter/type/update` | Update encounter type | admin |

### Perspectives

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character-encounter/get-perspective` | Get character's view of encounter | user |
| `POST` | `/character-encounter/refresh-memory` | Strengthen memory (referenced) | authenticated |
| `POST` | `/character-encounter/update-perspective` | Update perspective (reflection) | authenticated |

### Queries

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character-encounter/batch-get` | Bulk sentiment for multiple targets | authenticated |
| `POST` | `/character-encounter/get-sentiment` | Aggregate sentiment toward another character | user |
| `POST` | `/character-encounter/has-met` | Quick check if two characters have met | user |
| `POST` | `/character-encounter/query/between` | Get encounters between two characters | user |
| `POST` | `/character-encounter/query/by-character` | Get character's encounters (paginated) | user |
| `POST` | `/character-encounter/query/by-location` | Recent encounters at location | user |

### Recording

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character-encounter/record` | Record new encounter with perspectives | authenticated |

---

## Character History {#character-history}

**Version**: 1.0.0 | **Schema**: `schemas/character-history-api.yaml` | **Deep Dive**: [docs/plugins/CHARACTER-HISTORY.md](plugins/CHARACTER-HISTORY.md)

Historical event participation and backstory management for characters.

### Backstory

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character-history/add-backstory-element` | Add a single backstory element | admin |
| `POST` | `/character-history/delete-backstory` | Delete all backstory for a character | admin |
| `POST` | `/character-history/get-backstory` | Get machine-readable backstory elements for behavior system | user |
| `POST` | `/character-history/set-backstory` | Set backstory elements for a character | admin |

### Historical Events

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character-history/delete-participation` | Delete a participation record | admin |
| `POST` | `/character-history/get-event-participants` | Get all characters who participated in a historical event | user |
| `POST` | `/character-history/get-participation` | Get all historical events a character participated in | user |
| `POST` | `/character-history/record-participation` | Record character participation in a historical event | authenticated |

### History Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character-history/delete-all` | Delete all history data for a character | admin |
| `POST` | `/character-history/summarize` | Generate text summaries for character compression | authenticated |

---

## Character Personality {#character-personality}

**Version**: 1.0.0 | **Schema**: `schemas/character-personality-api.yaml` | **Deep Dive**: [docs/plugins/CHARACTER-PERSONALITY.md](plugins/CHARACTER-PERSONALITY.md)

Machine-readable personality traits for NPC behavior decisions.

### Combat Preferences

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character-personality/delete-combat` | Delete combat preferences for a character | admin |
| `POST` | `/character-personality/evolve-combat` | Record combat experience that may evolve preferences | authenticated |
| `POST` | `/character-personality/get-combat` | Get combat preferences for a character | user |
| `POST` | `/character-personality/set-combat` | Create or update combat preferences for a character | admin |

### Personality Evolution

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character-personality/evolve` | Record an experience that may evolve personality | authenticated |

### Personality Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character-personality/batch-get` | Get personalities for multiple characters | authenticated |
| `POST` | `/character-personality/delete` | Delete personality for a character | admin |
| `POST` | `/character-personality/get` | Get personality for a character | user |
| `POST` | `/character-personality/set` | Create or update personality for a character | admin |

---

## Connect {#connect}

**Version**: 2.0.0 | **Schema**: `schemas/connect-api.yaml` | **Deep Dive**: [docs/plugins/CONNECT.md](plugins/CONNECT.md)

Real-time communication and WebSocket connection management for Bannou services.

### Client Capabilities

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/client-capabilities` | Get client capability manifest (GUID → API mappings) | user |

### Internal Proxy

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/internal/proxy` | Internal API proxy for stateless requests | authenticated |

### Session Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/connect/get-account-sessions` | Get all active WebSocket sessions for an account | admin |

### WebSocket Connection

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `GET` | `/connect` | Establish WebSocket connection | authenticated |
| `POST` | `/connect` | Establish WebSocket connection (POST variant) | authenticated |

---

## Contract {#contract}

**Version**: 1.0.0 | **Schema**: `schemas/contract-api.yaml` | **Deep Dive**: [docs/plugins/CONTRACT.md](plugins/CONTRACT.md)

Binding agreements between entities with milestone-based progression.

### Breaches

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/contract/breach/cure` | Mark breach as cured (system/admin action) | developer |
| `POST` | `/contract/breach/get` | Get breach details | user |
| `POST` | `/contract/breach/report` | Report a contract breach | user |

### ClauseTypes

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/contract/clause-type/list` | List all registered clause types | developer |
| `POST` | `/contract/clause-type/register` | Register a new clause type | admin |

### Constraints

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/contract/check-constraint` | Check if entity can take action given contracts | user |
| `POST` | `/contract/query-active` | Query active contracts for entity | user |

### Execution

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/contract/instance/check-asset-requirements` | Check if asset requirement clauses are satisfied | developer |
| `POST` | `/contract/instance/execute` | Execute all contract clauses (idempotent) | developer |
| `POST` | `/contract/instance/set-template-values` | Set template values on contract instance | developer |

### Guardian

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/contract/lock` | Lock contract under guardian custody | developer |
| `POST` | `/contract/transfer-party` | Transfer party role to new entity | developer |
| `POST` | `/contract/unlock` | Unlock contract from guardian custody | developer |

### Instances

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/contract/instance/consent` | Party consents to contract | user |
| `POST` | `/contract/instance/create` | Create contract instance from template | user |
| `POST` | `/contract/instance/get` | Get instance by ID | user |
| `POST` | `/contract/instance/get-status` | Get current status and milestone progress | user |
| `POST` | `/contract/instance/propose` | Propose contract to parties (starts consent flow) | user |
| `POST` | `/contract/instance/query` | Query instances by party, template, status | user |
| `POST` | `/contract/instance/terminate` | Request early termination | user |

### Metadata

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/contract/metadata/get` | Get game metadata | user |
| `POST` | `/contract/metadata/update` | Update game metadata on instance | developer |

### Milestones

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/contract/milestone/complete` | External system reports milestone completed | developer |
| `POST` | `/contract/milestone/fail` | External system reports milestone failed | developer |
| `POST` | `/contract/milestone/get` | Get milestone details and status | user |

### Templates

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/contract/template/create` | Create a contract template | admin |
| `POST` | `/contract/template/delete` | Soft-delete template | admin |
| `POST` | `/contract/template/get` | Get template by ID or code | user |
| `POST` | `/contract/template/list` | List templates with filters | user |
| `POST` | `/contract/template/update` | Update template (not instances) | admin |

---

## Currency {#currency}

**Version**: 1.0.0 | **Schema**: `schemas/currency-api.yaml` | **Deep Dive**: [docs/plugins/CURRENCY.md](plugins/CURRENCY.md)

Multi-currency management service for game economies.

### Analytics

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/currency/stats/global-supply` | Get global supply statistics for a currency | user |
| `POST` | `/currency/stats/wallet-distribution` | Get wealth distribution statistics | admin |

### Authorization Hold

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/currency/hold/capture` | Capture held funds (debit final amount) | developer |
| `POST` | `/currency/hold/create` | Create an authorization hold (reserve funds) | developer |
| `POST` | `/currency/hold/get` | Get hold status and details | developer |
| `POST` | `/currency/hold/release` | Release held funds (make available again) | developer |

### Balance

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/currency/balance/batch-get` | Get multiple balances in one call | user |
| `POST` | `/currency/balance/get` | Get balance for a specific currency in a wallet | user |
| `POST` | `/currency/batch-credit` | Credit multiple wallets in one call | developer |
| `POST` | `/currency/credit` | Credit currency to a wallet (faucet operation) | developer |
| `POST` | `/currency/debit` | Debit currency from a wallet (sink operation) | developer |
| `POST` | `/currency/transfer` | Transfer currency between wallets | developer |

### Conversion

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/currency/convert/calculate` | Calculate conversion without executing | user |
| `POST` | `/currency/convert/execute` | Execute currency conversion in a wallet | developer |
| `POST` | `/currency/exchange-rate/get` | Get exchange rate between two currencies | user |
| `POST` | `/currency/exchange-rate/update` | Update a currency's exchange rate to base | admin |

### Currency Definition

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/currency/definition/create` | Create a new currency definition | admin |
| `POST` | `/currency/definition/get` | Get currency definition by ID or code | user |
| `POST` | `/currency/definition/list` | List currency definitions with filters | user |
| `POST` | `/currency/definition/update` | Update mutable fields of a currency definition | admin |

### Escrow Integration

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/currency/escrow/deposit` | Debit wallet for escrow deposit | developer |
| `POST` | `/currency/escrow/refund` | Credit depositor on escrow refund | developer |
| `POST` | `/currency/escrow/release` | Credit recipient on escrow completion | developer |

### Transaction History

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/currency/transaction/by-reference` | Get transactions by reference type and ID | developer |
| `POST` | `/currency/transaction/get` | Get a transaction by ID | developer |
| `POST` | `/currency/transaction/history` | Get paginated transaction history for a wallet | user |

### Wallet

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/currency/wallet/close` | Permanently close a wallet | admin |
| `POST` | `/currency/wallet/create` | Create a new wallet for an owner | developer |
| `POST` | `/currency/wallet/freeze` | Freeze a wallet to prevent transactions | admin |
| `POST` | `/currency/wallet/get` | Get wallet by ID or owner | user |
| `POST` | `/currency/wallet/get-or-create` | Get existing wallet or create if not exists | developer |
| `POST` | `/currency/wallet/unfreeze` | Unfreeze a frozen wallet | admin |

---

## Documentation {#documentation}

**Version**: 1.0.0 | **Schema**: `schemas/documentation-api.yaml` | **Deep Dive**: [docs/plugins/DOCUMENTATION.md](plugins/DOCUMENTATION.md)

Knowledge base API for AI agents to query documentation.
Designed for SignalWire SWAIG, OpenAI function calling, and Claude tool use.
All endpoints return voice-friendly summaries alongside detaile...

### Admin

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/documentation/bulk-delete` | Bulk soft-delete documents to trashcan | admin |
| `POST` | `/documentation/bulk-update` | Bulk update document metadata | admin |
| `POST` | `/documentation/create` | Create new documentation entry | admin |
| `POST` | `/documentation/delete` | Soft-delete documentation entry to trashcan | admin |
| `POST` | `/documentation/import` | Bulk import documentation from structured source | admin |
| `POST` | `/documentation/purge` | Permanently delete trashcan items | admin |
| `POST` | `/documentation/recover` | Recover document from trashcan | admin |
| `POST` | `/documentation/stats` | Get namespace documentation statistics | admin |
| `POST` | `/documentation/trashcan` | List documents in the trashcan | admin |
| `POST` | `/documentation/update` | Update existing documentation entry | admin |

### Archive

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/documentation/repo/archive/create` | Create documentation archive | developer |
| `POST` | `/documentation/repo/archive/delete` | Delete documentation archive | admin |
| `POST` | `/documentation/repo/archive/list` | List documentation archives | developer |
| `POST` | `/documentation/repo/archive/restore` | Restore documentation from archive | admin |

### Browser

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `GET` | `/documentation/raw/{slug}` | Get raw markdown content | authenticated |
| `GET` | `/documentation/view/{slug}` | View documentation page in browser | authenticated |

### Documents

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/documentation/get` | Get specific document by ID or slug | anonymous |
| `POST` | `/documentation/list` | List documents by category | anonymous |

### Repository

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/documentation/repo/bind` | Bind a git repository to a documentation namespace | developer |
| `POST` | `/documentation/repo/list` | List all repository bindings | developer |
| `POST` | `/documentation/repo/status` | Get repository binding status | developer |
| `POST` | `/documentation/repo/sync` | Manually trigger repository sync | developer |
| `POST` | `/documentation/repo/unbind` | Remove repository binding from namespace | admin |
| `POST` | `/documentation/repo/update` | Update repository binding configuration | developer |

### Search

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/documentation/query` | Natural language documentation search | anonymous |
| `POST` | `/documentation/search` | Full-text keyword search | anonymous |
| `POST` | `/documentation/suggest` | Get related topics and follow-up suggestions | anonymous |

---

## Escrow {#escrow}

**Version**: 1.0.0 | **Schema**: `schemas/escrow-api.yaml` | **Deep Dive**: [docs/plugins/ESCROW.md](plugins/ESCROW.md)

Full-custody orchestration layer for multi-party asset exchanges.

### Arbiter

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/escrow/resolve` | Arbiter resolves disputed escrow | developer |

### Completion

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/escrow/cancel` | Cancel escrow before fully funded | developer |
| `POST` | `/escrow/dispute` | Raise a dispute on funded escrow | user |
| `POST` | `/escrow/refund` | Trigger refund | developer |
| `POST` | `/escrow/release` | Trigger release | developer |

### Condition

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/escrow/verify-condition` | Verify condition for conditional escrow | developer |

### Consent

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/escrow/consent` | Record party consent | user |
| `POST` | `/escrow/consent/status` | Get consent status for escrow | user |

### Deposits

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/escrow/deposit` | Deposit assets into escrow | user |
| `POST` | `/escrow/deposit/status` | Get deposit status for a party | user |
| `POST` | `/escrow/deposit/validate` | Validate a deposit without executing | user |

### Handlers

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/escrow/handler/deregister` | Remove a custom asset handler registration | admin |
| `POST` | `/escrow/handler/list` | List registered asset handlers | admin |
| `POST` | `/escrow/handler/register` | Register a custom asset type handler | admin |

### Lifecycle

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/escrow/create` | Create a new escrow agreement | developer |
| `POST` | `/escrow/get` | Get escrow details | user |
| `POST` | `/escrow/get-my-token` | Get deposit or release token for a party | authenticated |
| `POST` | `/escrow/list` | List escrows for a party | user |

### Validation

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/escrow/reaffirm` | Re-affirm after validation failure | user |
| `POST` | `/escrow/validate` | Manually trigger validation | admin |

---

## Game Service {#game-service}

**Version**: 1.0.0 | **Schema**: `schemas/game-service-api.yaml` | **Deep Dive**: [docs/plugins/GAME-SERVICE.md](plugins/GAME-SERVICE.md)

Registry service for game services that users can subscribe to.
Provides a minimal registry of available services (games/applications) like Arcadia, Fantasia, etc.

### Game Service Registry

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/game-service/services/create` | Create a new game service entry | admin |
| `POST` | `/game-service/services/delete` | Delete a game service entry | admin |
| `POST` | `/game-service/services/get` | Get service by ID or stub name | user |
| `POST` | `/game-service/services/list` | List all registered game services | user |
| `POST` | `/game-service/services/update` | Update a game service entry | admin |

---

## Game Session {#game-session}

**Version**: 2.0.0 | **Schema**: `schemas/game-session-api.yaml` | **Deep Dive**: [docs/plugins/GAME-SESSION.md](plugins/GAME-SESSION.md)

Minimal game session management for Arcadia and other games.

### Game Actions

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/sessions/actions` | Perform game action (enhanced permissions after joining) | user |

### Game Chat

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/sessions/chat` | Send chat message to game session | user |

### Game Sessions

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/sessions/create` | Create new game session | authenticated |
| `POST` | `/sessions/get` | Get game session details | user |
| `POST` | `/sessions/join` | Join a game session | authenticated |
| `POST` | `/sessions/join-session` | Join a specific game session by ID | authenticated |
| `POST` | `/sessions/kick` | Kick player from game session (admin only) | admin |
| `POST` | `/sessions/leave` | Leave a game session | user |
| `POST` | `/sessions/leave-session` | Leave a specific game session by ID | user |
| `POST` | `/sessions/list` | List available game sessions | admin |

### Internal

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/sessions/publish-join-shortcut` | Publish join shortcut for matchmade session | authenticated |

---

## Inventory {#inventory}

**Version**: 1.0.0 | **Schema**: `schemas/inventory-api.yaml` | **Deep Dive**: [docs/plugins/INVENTORY.md](plugins/INVENTORY.md)

Container and inventory management service for games.

### Container

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/inventory/container/create` | Create a new container | developer |
| `POST` | `/inventory/container/delete` | Delete container | admin |
| `POST` | `/inventory/container/get` | Get container with contents | user |
| `POST` | `/inventory/container/get-or-create` | Get container or create if not exists | developer |
| `POST` | `/inventory/container/list` | List containers for owner | user |
| `POST` | `/inventory/container/update` | Update container properties | developer |

### Inventory Operations

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/inventory/add` | Add item to container | developer |
| `POST` | `/inventory/merge` | Merge two stacks | user |
| `POST` | `/inventory/move` | Move item to different slot or container | user |
| `POST` | `/inventory/remove` | Remove item from container | developer |
| `POST` | `/inventory/split` | Split stack into two | user |
| `POST` | `/inventory/transfer` | Transfer item to different owner | developer |

### Inventory Queries

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/inventory/count` | Count items of a template | user |
| `POST` | `/inventory/find-space` | Find where item would fit | user |
| `POST` | `/inventory/has` | Check if entity has required items | user |
| `POST` | `/inventory/query` | Find items across containers | user |

---

## Item {#item}

**Version**: 1.0.0 | **Schema**: `schemas/item-api.yaml` | **Deep Dive**: [docs/plugins/ITEM.md](plugins/ITEM.md)

Item template and instance management service.

### Item Instance

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/item/instance/bind` | Bind item to character | developer |
| `POST` | `/item/instance/create` | Create a new item instance | developer |
| `POST` | `/item/instance/destroy` | Destroy item instance | developer |
| `POST` | `/item/instance/get` | Get item instance by ID | user |
| `POST` | `/item/instance/modify` | Modify item instance state | developer |

### Item Query

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/item/instance/batch-get` | Get multiple item instances by ID | user |
| `POST` | `/item/instance/list-by-container` | List items in a container | user |
| `POST` | `/item/instance/list-by-template` | List instances of a template | admin |

### Item Template

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/item/template/create` | Create a new item template | developer |
| `POST` | `/item/template/deprecate` | Deprecate an item template | admin |
| `POST` | `/item/template/get` | Get item template by ID or code | user |
| `POST` | `/item/template/list` | List item templates with filters | user |
| `POST` | `/item/template/update` | Update mutable fields of an item template | developer |

---

## Leaderboard {#leaderboard}

**Version**: 1.0.0 | **Schema**: `schemas/leaderboard-api.yaml` | **Deep Dive**: [docs/plugins/LEADERBOARD.md](plugins/LEADERBOARD.md)

Real-time leaderboard management using Redis Sorted Sets for efficient ranking.

### Definitions

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/leaderboard/definition/create` | Create a new leaderboard definition | developer |
| `POST` | `/leaderboard/definition/delete` | Delete leaderboard definition | developer |
| `POST` | `/leaderboard/definition/get` | Get leaderboard definition | authenticated |
| `POST` | `/leaderboard/definition/list` | List leaderboard definitions | authenticated |
| `POST` | `/leaderboard/definition/update` | Update leaderboard definition | developer |

### Rankings

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/leaderboard/rank/around` | Get entries around entity | user |
| `POST` | `/leaderboard/rank/get` | Get entity's rank | user |
| `POST` | `/leaderboard/rank/top` | Get top entries | user |

### Scores

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/leaderboard/score/submit` | Submit or update a score | authenticated |
| `POST` | `/leaderboard/score/submit-batch` | Submit multiple scores | authenticated |

### Seasons

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/leaderboard/season/create` | Start a new season | admin |
| `POST` | `/leaderboard/season/get` | Get current season info | user |

---

## Location {#location}

**Version**: 1.0.0 | **Schema**: `schemas/location-api.yaml` | **Deep Dive**: [docs/plugins/LOCATION.md](plugins/LOCATION.md)

Location management service for Arcadia game world.

### Location

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/location/exists` | Check if location exists and is active | user |
| `POST` | `/location/get` | Get location by ID | user |
| `POST` | `/location/get-ancestors` | Get all ancestors of a location | user |
| `POST` | `/location/get-by-code` | Get location by code and realm | user |
| `POST` | `/location/get-descendants` | Get all descendants of a location | user |
| `POST` | `/location/list` | List locations with filtering | user |
| `POST` | `/location/list-by-parent` | Get child locations for a parent location | user |
| `POST` | `/location/list-by-realm` | List all locations in a realm (primary query pattern) | user |
| `POST` | `/location/list-root` | Get root locations in a realm | user |

### Location Admin

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/location/create` | Create new location | admin |
| `POST` | `/location/delete` | Delete location | admin |
| `POST` | `/location/deprecate` | Deprecate a location | admin |
| `POST` | `/location/remove-parent` | Remove parent from a location (make it a root location) | admin |
| `POST` | `/location/seed` | Seed locations from configuration | admin |
| `POST` | `/location/set-parent` | Set or change the parent of a location | admin |
| `POST` | `/location/undeprecate` | Restore a deprecated location | admin |
| `POST` | `/location/update` | Update location | admin |

---

## Mapping {#mapping}

**Version**: 1.0.0 | **Schema**: `schemas/mapping-api.yaml` | **Deep Dive**: [docs/plugins/MAPPING.md](plugins/MAPPING.md)

Spatial data management service for Arcadia game worlds.

### Authoring

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/mapping/authoring/checkout` | Acquire exclusive edit lock for design-time editing | developer |
| `POST` | `/mapping/authoring/commit` | Commit design-time changes | developer |
| `POST` | `/mapping/authoring/release` | Release authoring checkout without committing | developer |

### Authority

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/mapping/authority-heartbeat` | Maintain authority over channel | authenticated |
| `POST` | `/mapping/create-channel` | Create a new map channel and become its authority | authenticated |
| `POST` | `/mapping/release-authority` | Release authority over a channel | authenticated |

### Definition

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/mapping/definition/create` | Create a map definition template | developer |
| `POST` | `/mapping/definition/delete` | Delete a map definition | admin |
| `POST` | `/mapping/definition/get` | Get a map definition by ID | user |
| `POST` | `/mapping/definition/list` | List map definitions with optional filters | user |
| `POST` | `/mapping/definition/update` | Update a map definition | developer |

### Query

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/mapping/query/affordance` | Find locations that afford a specific action or scene type | user |
| `POST` | `/mapping/query/bounds` | Query map data within bounds | user |
| `POST` | `/mapping/query/objects-by-type` | Find all objects of a type in region | user |
| `POST` | `/mapping/query/point` | Query map data at a specific point | user |

### Runtime

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/mapping/publish` | Publish map data update (RPC path) | authenticated |
| `POST` | `/mapping/publish-objects` | Publish metadata object changes (batch) | authenticated |
| `POST` | `/mapping/request-snapshot` | Request full snapshot for cold start | user |

---

## Matchmaking {#matchmaking}

**Version**: 1.0.0 | **Schema**: `schemas/matchmaking-api.yaml` | **Deep Dive**: [docs/plugins/MATCHMAKING.md](plugins/MATCHMAKING.md)

Matchmaking service for competitive and casual game matching.

### Matchmaking

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/matchmaking/accept` | Accept a formed match | user |
| `POST` | `/matchmaking/decline` | Decline a formed match | user |
| `POST` | `/matchmaking/join` | Join matchmaking queue | user |
| `POST` | `/matchmaking/leave` | Leave matchmaking queue | user |
| `POST` | `/matchmaking/status` | Get matchmaking status | user |

### Queue Administration

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/matchmaking/queue/create` | Create a new matchmaking queue | admin |
| `POST` | `/matchmaking/queue/delete` | Delete a matchmaking queue | admin |
| `POST` | `/matchmaking/queue/update` | Update a matchmaking queue | admin |

### Queues

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/matchmaking/queue/get` | Get queue details | user |
| `POST` | `/matchmaking/queue/list` | List available matchmaking queues | user |

### Statistics

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/matchmaking/stats` | Get queue statistics | user |

---

## Mesh {#mesh}

**Version**: 1.0.0 | **Schema**: `schemas/mesh-api.yaml` | **Deep Dive**: [docs/plugins/MESH.md](plugins/MESH.md)

Native service mesh plugin providing direct service-to-service invocation
natively. Replaces mesh invocation with YARP-based
HTTP routing and Redis-backed service discovery.

### Diagnostics

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/mesh/health` | Get mesh health status | authenticated |

### Registration

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/mesh/deregister` | Deregister a service endpoint | authenticated |
| `POST` | `/mesh/heartbeat` | Update endpoint health and load | authenticated |
| `POST` | `/mesh/register` | Register a service endpoint | authenticated |

### Routing

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/mesh/mappings` | Get service-to-app-id mappings | authenticated |
| `POST` | `/mesh/route` | Get optimal endpoint for routing | authenticated |

### Service Discovery

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/mesh/endpoints/get` | Get endpoints for a service | authenticated |
| `POST` | `/mesh/endpoints/list` | List all registered endpoints | authenticated |

---

## Messaging {#messaging}

**Version**: 1.0.0 | **Schema**: `schemas/messaging-api.yaml` | **Deep Dive**: [docs/plugins/MESSAGING.md](plugins/MESSAGING.md)

Native RabbitMQ pub/sub messaging with native serialization.

### Messaging

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/messaging/list-topics` | List all known topics | authenticated |
| `POST` | `/messaging/publish` | Publish an event to a topic | authenticated |
| `POST` | `/messaging/subscribe` | Create a dynamic subscription to a topic | authenticated |
| `POST` | `/messaging/unsubscribe` | Remove a dynamic subscription | authenticated |

---

## Music {#music}

**Version**: 1.0.0 | **Schema**: `schemas/music-api.yaml` | **Deep Dive**: [docs/plugins/MUSIC.md](plugins/MUSIC.md)

Pure computation music generation using formal music theory rules.

### Generation

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/music/generate` | Generate composition from style and constraints | user |

### Styles

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/music/style/create` | Create new style definition | admin |
| `POST` | `/music/style/get` | Get style definition | user |
| `POST` | `/music/style/list` | List available styles | user |

### Theory

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/music/theory/melody` | Generate melody over harmony | user |
| `POST` | `/music/theory/progression` | Generate chord progression | user |
| `POST` | `/music/theory/voice-lead` | Apply voice leading to chords | user |

### Validation

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/music/validate` | Validate MIDI-JSON structure | user |

---

## Orchestrator {#orchestrator}

**Version**: 3.0.0 | **Schema**: `schemas/orchestrator-api.yaml` | **Deep Dive**: [docs/plugins/ORCHESTRATOR.md](plugins/ORCHESTRATOR.md)

Central intelligence for Bannou environment management and service orchestration.

### Other

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/orchestrator/backends/list` | Detect available container orchestration backends | admin |
| `POST` | `/orchestrator/clean` | Clean up unused resources | admin |
| `POST` | `/orchestrator/config/rollback` | Rollback to previous configuration | admin |
| `POST` | `/orchestrator/config/version` | Get current configuration version and metadata | admin |
| `POST` | `/orchestrator/containers/request-restart` | Request container restart (self-service pattern) | admin |
| `POST` | `/orchestrator/containers/status` | Get container health and restart history | admin |
| `POST` | `/orchestrator/deploy` | Deploy or update an environment | admin |
| `POST` | `/orchestrator/health/infrastructure` | Check infrastructure component health | admin |
| `POST` | `/orchestrator/health/services` | Get health status of all services | admin |
| `POST` | `/orchestrator/logs` | Get service/container logs | admin |
| `POST` | `/orchestrator/presets/list` | List available deployment presets | admin |
| `POST` | `/orchestrator/processing-pool/acquire` | Acquire a processor from a pool | admin |
| `POST` | `/orchestrator/processing-pool/cleanup` | Cleanup idle processing pool instances | admin |
| `POST` | `/orchestrator/processing-pool/release` | Release a processor back to the pool | admin |
| `POST` | `/orchestrator/processing-pool/scale` | Scale a processing pool | admin |
| `POST` | `/orchestrator/processing-pool/status` | Get processing pool status | admin |
| `POST` | `/orchestrator/service-routing` | Get current service-to-app-id routing mappings | admin |
| `POST` | `/orchestrator/services/restart` | Restart service with optional configuration | admin |
| `POST` | `/orchestrator/services/should-restart` | Check if service needs restart | admin |
| `POST` | `/orchestrator/status` | Get current environment status | admin |
| `POST` | `/orchestrator/teardown` | Tear down the current environment | admin |
| `POST` | `/orchestrator/topology` | Update service topology without full redeploy | admin |

---

## Permission {#permission}

**Version**: 3.0.0 | **Schema**: `schemas/permission-api.yaml` | **Deep Dive**: [docs/plugins/PERMISSION.md](plugins/PERMISSION.md)

Redis-backed high-performance permission system for WebSocket services.

### Permission Lookup

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/permission/capabilities` | Get available API methods for session | authenticated |

### Permission Validation

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/permission/validate` | Validate specific API access for session | authenticated |

### Service Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/permission/register-service` | Register or update service permission matrix | authenticated |
| `POST` | `/permission/services/list` | List all registered services | admin |

### Session Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/permission/clear-session-state` | Clear session state for specific service | authenticated |
| `POST` | `/permission/get-session-info` | Get complete session information | authenticated |
| `POST` | `/permission/update-session-role` | Update session role (affects all services) | authenticated |
| `POST` | `/permission/update-session-state` | Update session state for specific service | admin |

---

## Realm {#realm}

**Version**: 1.0.0 | **Schema**: `schemas/realm-api.yaml` | **Deep Dive**: [docs/plugins/REALM.md](plugins/REALM.md)

Realm management service for Arcadia game world.

### Realm

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/realm/exists` | Check if realm exists and is active | user |
| `POST` | `/realm/get` | Get realm by ID | user |
| `POST` | `/realm/get-by-code` | Get realm by code | user |
| `POST` | `/realm/list` | List all realms | user |

### Realm Admin

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/realm/create` | Create new realm | admin |
| `POST` | `/realm/delete` | Delete realm | admin |
| `POST` | `/realm/deprecate` | Deprecate a realm | admin |
| `POST` | `/realm/seed` | Seed realms from configuration | admin |
| `POST` | `/realm/undeprecate` | Restore a deprecated realm | admin |
| `POST` | `/realm/update` | Update realm | admin |

---

## Realm History {#realm-history}

**Version**: 1.0.0 | **Schema**: `schemas/realm-history-api.yaml` | **Deep Dive**: [docs/plugins/REALM-HISTORY.md](plugins/REALM-HISTORY.md)

Historical event participation and lore management for realms.

### Historical Events

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/realm-history/delete-participation` | Delete a participation record | admin |
| `POST` | `/realm-history/get-event-participants` | Get all realms that participated in a historical event | user |
| `POST` | `/realm-history/get-participation` | Get all historical events a realm participated in | user |
| `POST` | `/realm-history/record-participation` | Record realm participation in a historical event | authenticated |

### History Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/realm-history/delete-all` | Delete all history data for a realm | admin |
| `POST` | `/realm-history/summarize` | Generate text summaries for realm archival | authenticated |

### Lore

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/realm-history/add-lore-element` | Add a single lore element | admin |
| `POST` | `/realm-history/delete-lore` | Delete all lore for a realm | admin |
| `POST` | `/realm-history/get-lore` | Get machine-readable lore elements for behavior system | user |
| `POST` | `/realm-history/set-lore` | Set lore elements for a realm | admin |

---

## Relationship {#relationship}

**Version**: 1.0.0 | **Schema**: `schemas/relationship-api.yaml` | **Deep Dive**: [docs/plugins/RELATIONSHIP.md](plugins/RELATIONSHIP.md)

Generic relationship management service for entity-to-entity relationships.

### Relationship Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/relationship/create` | Create a new relationship between two entities | admin |
| `POST` | `/relationship/end` | End a relationship | admin |
| `POST` | `/relationship/get` | Get a relationship by ID | user |
| `POST` | `/relationship/get-between` | Get all relationships between two specific entities | user |
| `POST` | `/relationship/list-by-entity` | List all relationships for an entity | user |
| `POST` | `/relationship/list-by-type` | List all relationships of a specific type | user |
| `POST` | `/relationship/update` | Update relationship metadata | admin |

---

## Relationship Type {#relationship-type}

**Version**: 2.0.0 | **Schema**: `schemas/relationship-type-api.yaml` | **Deep Dive**: [docs/plugins/RELATIONSHIP-TYPE.md](plugins/RELATIONSHIP-TYPE.md)

Relationship type management service for Arcadia game world.

### RelationshipType

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/relationship-type/get` | Get relationship type by ID | user |
| `POST` | `/relationship-type/get-ancestors` | Get all ancestors of a relationship type | user |
| `POST` | `/relationship-type/get-by-code` | Get relationship type by code | user |
| `POST` | `/relationship-type/get-children` | Get child types for a parent type | user |
| `POST` | `/relationship-type/list` | List all relationship types | user |
| `POST` | `/relationship-type/matches-hierarchy` | Check if type matches ancestor in hierarchy | user |

### RelationshipType Admin

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/relationship-type/create` | Create new relationship type | admin |
| `POST` | `/relationship-type/delete` | Delete relationship type | admin |
| `POST` | `/relationship-type/deprecate` | Deprecate a relationship type | admin |
| `POST` | `/relationship-type/merge` | Merge a deprecated type into another type | admin |
| `POST` | `/relationship-type/seed` | Seed relationship types from configuration | admin |
| `POST` | `/relationship-type/undeprecate` | Restore a deprecated relationship type | admin |
| `POST` | `/relationship-type/update` | Update relationship type | admin |

---

## Save Load {#save-load}

**Version**: 1.0.0 | **Schema**: `schemas/save-load-api.yaml` | **Deep Dive**: [docs/plugins/SAVE-LOAD.md](plugins/SAVE-LOAD.md)

Generic save/load system for game state persistence.
Supports polymorphic ownership, versioned saves, and schema migration.

### Admin

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/save-load/admin/cleanup` | Run cleanup for expired/orphaned saves | admin |
| `POST` | `/save-load/admin/stats` | Get storage statistics | admin |

### Migration

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/save-load/migrate` | Migrate save to new schema version | developer |
| `POST` | `/save-load/schema/list` | List registered schemas | user |
| `POST` | `/save-load/schema/register` | Register a save data schema | developer |

### Query

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/save-load/query` | Query saves with filters | user |

### Saves

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/save-load/collapse-deltas` | Collapse delta chain into full snapshot | user |
| `POST` | `/save-load/load` | Load data from slot | user |
| `POST` | `/save-load/load-with-deltas` | Load save reconstructing from delta chain | user |
| `POST` | `/save-load/save` | Save data to slot | user |
| `POST` | `/save-load/save-delta` | Save incremental changes from base version | user |

### Slots

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/save-load/slot/bulk-delete` | Delete multiple slots at once | admin |
| `POST` | `/save-load/slot/create` | Create or configure a save slot | user |
| `POST` | `/save-load/slot/delete` | Delete slot and all versions | user |
| `POST` | `/save-load/slot/get` | Get slot metadata | user |
| `POST` | `/save-load/slot/list` | List slots for owner | user |
| `POST` | `/save-load/slot/rename` | Rename a save slot | user |

### Transfer

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/save-load/copy` | Copy save to different slot or owner | user |
| `POST` | `/save-load/export` | Export saves for backup/portability | user |
| `POST` | `/save-load/import` | Import saves from backup | admin |

### Validation

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/save-load/verify` | Verify save data integrity | user |

### Versions

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/save-load/version/delete` | Delete specific version | user |
| `POST` | `/save-load/version/list` | List versions in slot | user |
| `POST` | `/save-load/version/pin` | Pin a version as checkpoint | user |
| `POST` | `/save-load/version/promote` | Promote old version to latest | user |
| `POST` | `/save-load/version/unpin` | Unpin a version | user |

---

## Scene {#scene}

**Version**: 1.0.0 | **Schema**: `schemas/scene-api.yaml` | **Deep Dive**: [docs/plugins/SCENE.md](plugins/SCENE.md)

Hierarchical composition storage for game worlds.

### Instance

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/scene/destroy-instance` | Declare that a scene instance was removed | authenticated |
| `POST` | `/scene/instantiate` | Declare that a scene was instantiated in the game world | authenticated |

### Query

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/scene/find-asset-usage` | Find scenes using a specific asset | user |
| `POST` | `/scene/find-references` | Find scenes that reference a given scene | user |
| `POST` | `/scene/search` | Full-text search across scenes | user |

### Scene

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/scene/create` | Create a new scene document | developer |
| `POST` | `/scene/delete` | Delete a scene | developer |
| `POST` | `/scene/duplicate` | Duplicate a scene with a new ID | developer |
| `POST` | `/scene/get` | Retrieve a scene by ID | user |
| `POST` | `/scene/list` | List scenes with filtering | user |
| `POST` | `/scene/update` | Update a scene document | developer |
| `POST` | `/scene/validate` | Validate a scene structure | user |

### Validation

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/scene/get-validation-rules` | Get validation rules for a gameId+sceneType | user |
| `POST` | `/scene/register-validation-rules` | Register validation rules for a gameId+sceneType | admin |

### Versioning

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/scene/checkout` | Lock a scene for editing | developer |
| `POST` | `/scene/commit` | Save changes and release lock | developer |
| `POST` | `/scene/discard` | Release lock without saving changes | developer |
| `POST` | `/scene/heartbeat` | Extend checkout lock TTL | developer |
| `POST` | `/scene/history` | Get version history for a scene | user |

---

## Species {#species}

**Version**: 2.0.0 | **Schema**: `schemas/species-api.yaml` | **Deep Dive**: [docs/plugins/SPECIES.md](plugins/SPECIES.md)

Species management service for Arcadia game world.

### Species

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/species/get` | Get species by ID | user |
| `POST` | `/species/get-by-code` | Get species by code | user |
| `POST` | `/species/list` | List all species | user |
| `POST` | `/species/list-by-realm` | List species available in a realm | user |

### Species Admin

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/species/add-to-realm` | Add species to a realm | admin |
| `POST` | `/species/create` | Create new species | admin |
| `POST` | `/species/delete` | Delete species | admin |
| `POST` | `/species/deprecate` | Deprecate a species | admin |
| `POST` | `/species/merge` | Merge a deprecated species into another species | admin |
| `POST` | `/species/remove-from-realm` | Remove species from a realm | admin |
| `POST` | `/species/seed` | Seed species from configuration | admin |
| `POST` | `/species/undeprecate` | Restore a deprecated species | admin |
| `POST` | `/species/update` | Update species | admin |

---

## State {#state}

**Version**: 1.0.0 | **Schema**: `schemas/state-api.yaml` | **Deep Dive**: [docs/plugins/STATE.md](plugins/STATE.md)

Repository pattern state management with Redis and MySQL backends.

### State

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/state/bulk-get` | Bulk get multiple keys | authenticated |
| `POST` | `/state/delete` | Delete state value | authenticated |
| `POST` | `/state/get` | Get state value by key | authenticated |
| `POST` | `/state/list-stores` | List configured state stores | authenticated |
| `POST` | `/state/query` | Query state (MySQL JSON queries or Redis with search enabled) | authenticated |
| `POST` | `/state/save` | Save state value | authenticated |

---

## Subscription {#subscription}

**Version**: 1.0.0 | **Schema**: `schemas/subscription-api.yaml` | **Deep Dive**: [docs/plugins/SUBSCRIPTION.md](plugins/SUBSCRIPTION.md)

Manages user subscriptions to game services.
Tracks which accounts have access to which services (games/applications) with time-limited subscriptions.

### Subscription Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/subscription/account/list` | Get subscriptions for an account | user |
| `POST` | `/subscription/cancel` | Cancel a subscription | user |
| `POST` | `/subscription/create` | Create a new subscription | admin |
| `POST` | `/subscription/get` | Get a specific subscription by ID | user |
| `POST` | `/subscription/query` | Query current (active, non-expired) subscriptions | authenticated |
| `POST` | `/subscription/renew` | Renew or extend a subscription | admin |
| `POST` | `/subscription/update` | Update a subscription | admin |

---

## Voice {#voice}

**Version**: 1.1.0 | **Schema**: `schemas/voice-api.yaml` | **Deep Dive**: [docs/plugins/VOICE.md](plugins/VOICE.md)

Voice communication coordination service for P2P and room-based audio.

### Voice Peers

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/voice/peer/answer` | Send SDP answer to complete WebRTC handshake | user |
| `POST` | `/voice/peer/heartbeat` | Update peer endpoint TTL | admin |

### Voice Rooms

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/voice/room/create` | Create voice room for a game session | authenticated |
| `POST` | `/voice/room/delete` | Delete voice room | authenticated |
| `POST` | `/voice/room/get` | Get voice room details | admin |
| `POST` | `/voice/room/join` | Join voice room and register SIP endpoint | authenticated |
| `POST` | `/voice/room/leave` | Leave voice room | authenticated |

---

## Website {#website}

**Version**: 1.0.0 | **Schema**: `schemas/website-api.yaml` | **Deep Dive**: [docs/plugins/WEBSITE.md](plugins/WEBSITE.md)

Public-facing website service for registration, information, and account management.

### Account

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `GET` | `/website/account/characters` | Get character list for logged-in user | user |
| `GET` | `/website/account/profile` | Get account profile for logged-in user | user |
| `GET` | `/website/account/subscription` | Get subscription status | user |

### CMS

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `GET` | `/website/cms/pages` | List all CMS pages | developer |
| `POST` | `/website/cms/pages` | Create new CMS page | developer |
| `PUT` | `/website/cms/pages/{slug}` | Update CMS page | developer |
| `DELETE` | `/website/cms/pages/{slug}` | Delete CMS page | developer |
| `GET` | `/website/cms/site-settings` | Get site configuration | developer |
| `PUT` | `/website/cms/site-settings` | Update site configuration | developer |
| `GET` | `/website/cms/theme` | Get current theme configuration | developer |
| `PUT` | `/website/cms/theme` | Update theme configuration | developer |

### Contact

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/website/contact` | Submit contact form | anonymous |

### Content

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `GET` | `/website/content/{slug}` | Get dynamic page content from CMS | anonymous |
| `GET` | `/website/news` | Get latest news and announcements | anonymous |

### Downloads

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `GET` | `/website/downloads` | Get download links for game clients | anonymous |

### Status

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `GET` | `/website/server-status` | Get game server status for all realms | anonymous |
| `GET` | `/website/status` | Get website status and version | anonymous |

---

## Summary

- **Total services**: 40
- **Total endpoints**: 539

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
