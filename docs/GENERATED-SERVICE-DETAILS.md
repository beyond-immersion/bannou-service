# Generated Service Details Reference

> **Auto-generated**: 2026-01-01 09:17:20
> **Source**: `schemas/*-api.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document provides a compact reference of all Bannou services and their API endpoints.

## Service Overview

| Service | Version | Endpoints | Description |
|---------|---------|-----------|-------------|
| [Accounts](#accounts) | 2.0.0 | 13 | Internal account management service (CRUD operations only, n... |
| [Asset](#asset) | 1.0.0 | 8 | Asset management service for storage, versioning, and distri... |
| [Auth](#auth) | 4.0.0 | 12 | Authentication and session management service (Internet-faci... |
| [Behavior](#behavior) | 3.0.0 | 8 | Arcadia Behavior Markup Language (ABML) API for character be... |
| [Character](#character) | 1.0.0 | 6 | Character management service for Arcadia game world. |
| [Connect](#connect) | 2.0.0 | 4 | Real-time communication and WebSocket connection management ... |
| [Documentation](#documentation) | 1.0.0 | 26 | Knowledge base API for AI agents to query documentation.
Des... |
| [Game Session](#game-session) | 2.0.0 | 8 | Minimal game session management for Arcadia and other games. |
| [Location](#location) | 1.0.0 | 17 | Location management service for Arcadia game world. |
| [Mesh](#mesh) | 1.0.0 | 8 | Native service mesh plugin providing direct service-to-servi... |
| [Messaging](#messaging) | 1.0.0 | 4 | Native RabbitMQ pub/sub messaging with native serialization. |
| [Orchestrator](#orchestrator) | 3.0.0 | 22 | Central intelligence for Bannou environment management and s... |
| [Permissions](#permissions) | 3.0.0 | 8 | Redis-backed high-performance permission system for WebSocke... |
| [Realm](#realm) | 1.0.0 | 10 | Realm management service for Arcadia game world. |
| [Relationship](#relationship) | 1.0.0 | 7 | Generic relationship management service for entity-to-entity... |
| [Relationship Type](#relationship-type) | 2.0.0 | 13 | Relationship type management service for Arcadia game world. |
| [Servicedata](#servicedata) | 1.0.0 | 5 | Registry service for game services that users can subscribe ... |
| [Species](#species) | 2.0.0 | 13 | Species management service for Arcadia game world. |
| [State](#state) | 1.0.0 | 6 | Repository pattern state management with Redis and MySQL bac... |
| [Subscriptions](#subscriptions) | 1.0.0 | 7 | Manages user subscriptions to game services.
Tracks which ac... |
| [Voice](#voice) | 1.1.0 | 7 | Voice communication coordination service for P2P and room-ba... |
| [Website](#website) | 1.0.0 | 17 | Public-facing website service for registration, information,... |

---

## Accounts {#accounts}

**Version**: 2.0.0 | **Schema**: `schemas/accounts-api.yaml`

Internal account management service (CRUD operations only, never exposed to internet).

### Account Lookup

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/accounts/by-email` | Get account by email | admin |
| `POST` | `/accounts/by-provider` | Get account by external provider ID | admin |

### Account Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/accounts/create` | Create new account | admin |
| `POST` | `/accounts/delete` | Delete account | admin |
| `POST` | `/accounts/get` | Get account by ID | admin |
| `POST` | `/accounts/list` | List accounts with filtering | admin |
| `POST` | `/accounts/password/update` | Update account password hash | user |
| `POST` | `/accounts/update` | Update account | admin |
| `POST` | `/accounts/verification/update` | Update email verification status | user |

### Authentication Methods

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/accounts/auth-methods/add` | Add authentication method to account | admin |
| `POST` | `/accounts/auth-methods/list` | Get authentication methods for account | admin |
| `POST` | `/accounts/auth-methods/remove` | Remove authentication method from account | admin |

### Profile Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/accounts/profile/update` | Update account profile | user |

---

## Asset {#asset}

**Version**: 1.0.0 | **Schema**: `schemas/asset-api.yaml`

Asset management service for storage, versioning, and distribution of large binary assets.

### Assets

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/assets/get` | Get asset metadata and download URL | user |
| `POST` | `/assets/list-versions` | List all versions of an asset | user |
| `POST` | `/assets/search` | Search assets by tags, type, or realm | user |
| `POST` | `/assets/upload/complete` | Mark upload as complete, trigger processing | user |
| `POST` | `/assets/upload/request` | Request upload URL for a new asset | user |

### Bundles

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/bundles/create` | Create asset bundle from multiple assets | user |
| `POST` | `/bundles/get` | Get bundle manifest and download URL | user |
| `POST` | `/bundles/upload/request` | Request upload URL for a pre-made bundle | user |

---

## Auth {#auth}

**Version**: 4.0.0 | **Schema**: `schemas/auth-api.yaml`

Authentication and session management service (Internet-facing).

### Authentication

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/auth/login` | Login with email/password | anonymous |
| `POST` | `/auth/logout` | Logout and invalidate tokens | user |
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

**Version**: 3.0.0 | **Schema**: `schemas/behavior-api.yaml`

Arcadia Behavior Markup Language (ABML) API for character behavior management.

### ABML

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/compile` | Compile ABML behavior definition | developer |

### BehaviorStacks

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/stack/compile` | Compile stackable behavior sets | developer |

### Cache

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/cache/get` | Get cached compiled behavior | developer |
| `POST` | `/cache/invalidate` | Invalidate cached behavior | developer |

### Context

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/context/resolve` | Resolve context variables | developer |

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

**Version**: 1.0.0 | **Schema**: `schemas/character-api.yaml`

Character management service for Arcadia game world.

### Character Lookup

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character/by-realm` | Get all characters in a realm (primary query pattern) | user |

### Character Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/character/create` | Create new character | admin |
| `POST` | `/character/delete` | Delete character (permanent removal) | admin |
| `POST` | `/character/get` | Get character by ID | user |
| `POST` | `/character/list` | List characters with filtering | user |
| `POST` | `/character/update` | Update character | admin |

---

## Connect {#connect}

**Version**: 2.0.0 | **Schema**: `schemas/connect-api.yaml`

Real-time communication and WebSocket connection management for Bannou services.

### Client Capabilities

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/client-capabilities` | Get client capability manifest (GUID → API mappings) | user |

### Internal Proxy

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/internal/proxy` | Internal API proxy for stateless requests | authenticated |

### WebSocket Connection

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `GET` | `/connect` | Establish WebSocket connection | authenticated |
| `POST` | `/connect` | Establish WebSocket connection (POST variant) | authenticated |

---

## Documentation {#documentation}

**Version**: 1.0.0 | **Schema**: `schemas/documentation-api.yaml`

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

## Game Session {#game-session}

**Version**: 2.0.0 | **Schema**: `schemas/game-session-api.yaml`

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
| `POST` | `/sessions/kick` | Kick player from game session (admin only) | admin |
| `POST` | `/sessions/leave` | Leave a game session | user |
| `POST` | `/sessions/list` | List available game sessions | authenticated |

---

## Location {#location}

**Version**: 1.0.0 | **Schema**: `schemas/location-api.yaml`

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

## Mesh {#mesh}

**Version**: 1.0.0 | **Schema**: `schemas/mesh-api.yaml`

Native service mesh plugin providing direct service-to-service invocation
natively. Replaces mesh invocation with YARP-based
HTTP routing and Redis-backed service discovery.

### Diagnostics

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/mesh/health` | Get mesh health status | user |

### Registration

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/mesh/deregister` | Deregister a service endpoint | user |
| `POST` | `/mesh/heartbeat` | Update endpoint health and load | user |
| `POST` | `/mesh/register` | Register a service endpoint | user |

### Routing

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/mesh/mappings` | Get service-to-app-id mappings | user |
| `POST` | `/mesh/route` | Get optimal endpoint for routing | user |

### Service Discovery

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/mesh/endpoints/get` | Get endpoints for a service | user |
| `POST` | `/mesh/endpoints/list` | List all registered endpoints | admin |

---

## Messaging {#messaging}

**Version**: 1.0.0 | **Schema**: `schemas/messaging-api.yaml`

Native RabbitMQ pub/sub messaging with native serialization.

### Messaging

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/messaging/list-topics` | List all known topics | authenticated |
| `POST` | `/messaging/publish` | Publish an event to a topic | authenticated |
| `POST` | `/messaging/subscribe` | Create a dynamic subscription to a topic | authenticated |
| `POST` | `/messaging/unsubscribe` | Remove a dynamic subscription | authenticated |

---

## Orchestrator {#orchestrator}

**Version**: 3.0.0 | **Schema**: `schemas/orchestrator-api.yaml`

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
| `POST` | `/orchestrator/processing-pool/acquire` | Acquire a processor from a pool | service |
| `POST` | `/orchestrator/processing-pool/cleanup` | Cleanup idle processing pool instances | admin |
| `POST` | `/orchestrator/processing-pool/release` | Release a processor back to the pool | service |
| `POST` | `/orchestrator/processing-pool/scale` | Scale a processing pool | admin |
| `POST` | `/orchestrator/processing-pool/status` | Get processing pool status | admin |
| `POST` | `/orchestrator/service-routing` | Get current service-to-app-id routing mappings | admin |
| `POST` | `/orchestrator/services/restart` | Restart service with optional configuration | admin |
| `POST` | `/orchestrator/services/should-restart` | Check if service needs restart | admin |
| `POST` | `/orchestrator/status` | Get current environment status | admin |
| `POST` | `/orchestrator/teardown` | Tear down the current environment | admin |
| `POST` | `/orchestrator/topology` | Update service topology without full redeploy | admin |

---

## Permissions {#permissions}

**Version**: 3.0.0 | **Schema**: `schemas/permissions-api.yaml`

Redis-backed high-performance permission system for WebSocket services.

### Permission Lookup

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/permissions/capabilities` | Get available API methods for session | authenticated |

### Permission Validation

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/permissions/validate` | Validate specific API access for session | authenticated |

### Service Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/permissions/register-service` | Register or update service permission matrix | authenticated |
| `POST` | `/permissions/services/list` | List all registered services | admin |

### Session Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/permissions/clear-session-state` | Clear session state for specific service | authenticated |
| `POST` | `/permissions/get-session-info` | Get complete session information | authenticated |
| `POST` | `/permissions/update-session-role` | Update session role (affects all services) | authenticated |
| `POST` | `/permissions/update-session-state` | Update session state for specific service | admin |

---

## Realm {#realm}

**Version**: 1.0.0 | **Schema**: `schemas/realm-api.yaml`

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

## Relationship {#relationship}

**Version**: 1.0.0 | **Schema**: `schemas/relationship-api.yaml`

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

**Version**: 2.0.0 | **Schema**: `schemas/relationship-type-api.yaml`

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

## Servicedata {#servicedata}

**Version**: 1.0.0 | **Schema**: `schemas/servicedata-api.yaml`

Registry service for game services that users can subscribe to.
Provides a minimal registry of available services (games/applications) like Arcadia, Fantasia, etc.

### Service Registry

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/servicedata/services/create` | Create a new game service entry | admin |
| `POST` | `/servicedata/services/delete` | Delete a game service entry | admin |
| `POST` | `/servicedata/services/get` | Get service by ID or stub name | user |
| `POST` | `/servicedata/services/list` | List all registered game services | user |
| `POST` | `/servicedata/services/update` | Update a game service entry | admin |

---

## Species {#species}

**Version**: 2.0.0 | **Schema**: `schemas/species-api.yaml`

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

**Version**: 1.0.0 | **Schema**: `schemas/state-api.yaml`

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

## Subscriptions {#subscriptions}

**Version**: 1.0.0 | **Schema**: `schemas/subscriptions-api.yaml`

Manages user subscriptions to game services.
Tracks which accounts have access to which services (games/applications) with time-limited subscriptions.

### Subscription Management

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/subscriptions/account/current` | Get current (active, non-expired) subscriptions | user |
| `POST` | `/subscriptions/account/list` | Get subscriptions for an account | user |
| `POST` | `/subscriptions/cancel` | Cancel a subscription | user |
| `POST` | `/subscriptions/create` | Create a new subscription | admin |
| `POST` | `/subscriptions/get` | Get a specific subscription by ID | user |
| `POST` | `/subscriptions/renew` | Renew or extend a subscription | admin |
| `POST` | `/subscriptions/update` | Update a subscription | admin |

---

## Voice {#voice}

**Version**: 1.1.0 | **Schema**: `schemas/voice-api.yaml`

Voice communication coordination service for P2P and room-based audio.

### Voice Peers

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/voice/peer/answer` | Send SDP answer to complete WebRTC handshake | user |
| `POST` | `/voice/peer/heartbeat` | Update peer endpoint TTL | authenticated |

### Voice Rooms

| Method | Path | Summary | Access |
|--------|------|---------|--------|
| `POST` | `/voice/room/create` | Create voice room for a game session | authenticated |
| `POST` | `/voice/room/delete` | Delete voice room | authenticated |
| `POST` | `/voice/room/get` | Get voice room details | authenticated |
| `POST` | `/voice/room/join` | Join voice room and register SIP endpoint | authenticated |
| `POST` | `/voice/room/leave` | Leave voice room | authenticated |

---

## Website {#website}

**Version**: 1.0.0 | **Schema**: `schemas/website-api.yaml`

Public-facing website service for registration, information, and account management

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

- **Total services**: 22
- **Total endpoints**: 229

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
