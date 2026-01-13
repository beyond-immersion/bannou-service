# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

---

## [0.10.0] - 2026-01-13

### Added
- **Godot SceneComposer SDK** with unit tests for scene composition in Godot projects
- **OpenResty Configuration System**: Template-based nginx config generation
  - SSL termination templates for HTTPS endpoints
  - MinIO storage proxy templates (port 9000 and subdomain patterns)
  - Dynamic config generation via `generate-configs.sh`
  - Override directory for environment-specific configs
- **Production Deployment Tooling**:
  - ACME challenge configuration for Let's Encrypt certificate renewal
  - Certbot webroot integration with OpenResty
  - Production quickstart guide (`docs/guides/PRODUCTION-QUICKSTART.md`)
- **Release Management**: `scripts/prepare-release.sh` for semantic version releases
- **Releasing Guide** (`docs/guides/RELEASING.md`): Complete release workflow documentation
- **Permission Registration Tests**: Verify all plugins register permissions correctly
- **TestConfigurationHelper**: Shared test utilities for configuration testing

### Changed
- **Configuration Defaults**: All services now work out-of-box without secrets
  - JWT, salts, and credentials have development defaults
  - MinIO accepts host:port format (not full URLs)
  - RabbitMQ queue names have sensible defaults
- **JWT Centralized**: Token configuration moved to AppConfiguration (web host access)
- **Asset Service**: Pre-signed URLs now use `BANNOU_SERVICE_DOMAIN` when set
- **Permission System**: More secure defaults, reasonable role assignments
- **OpenResty Networking**: Only OpenResty exposes ports; services communicate via Docker network
- **Environment Variables**: Removed hardcoded overrides from Makefile and docker-compose files
- Save-load plugin uses Redis instead of SQL for pending upload state

### Fixed
- Asset upload tests failing due to incorrect service domain in presigned URLs
- Permission registration missing for several plugins
- References to planning docs cleaned up
- Nullable reference fixes throughout codebase

---

## [0.9.0] - 2026-01-10

### Added
- **Save-Load Plugin** (`lib-save-load`): Complete cloud saves system with 26 endpoints
  - Polymorphic ownership (Account, Character, Session, Realm)
  - Versioned saves with rolling cleanup and pinnable checkpoints
  - Delta saves using JSON Patch (RFC 6902) for incremental changes
  - Schema migration support
  - Export/import for backup and disaster recovery
  - Integrity verification via SHA-256 content hashing
  - Circuit breaker and async upload queue for storage protection
- Stride SceneComposer SDK for scene composition
- Save/load developer guide (`docs/guides/SAVING_AND_LOADING.md`)

### Changed
- Improved integration test infrastructure

---

## [0.8.0] - 2026-01-08

### Added
- **Matchmaking Service** (`lib-matchmaking`): Full-featured matchmaking system
  - Skill-based matching with configurable expansion curves
  - Party support with skill aggregation (highest, average, weighted)
  - Match accept/decline flow with timeout handling
  - Exclusive groups to prevent conflicting concurrent queues
  - Tournament support with registration requirements
  - Auto-requeue on match decline
  - Game-session reservation integration
- **Scene Service** (`lib-scene`): Hierarchical composition storage for game worlds
- **Realm History Service** (`lib-realm-history`): Historical event and lore management
- **Mapping Service** (`lib-mapping`): Spatial data management with affordance queries
- Matchmaking developer guide (`docs/guides/MATCHMAKING.md`)
- Behavior system improvements and additional ABML capabilities

---

## [0.7.0] - 2026-01-05

### Added
- **Actor Service** (`lib-actor`): Distributed actor management for NPC brains
  - Actor templates and spawning
  - Encounter system for Event Brain orchestration
  - Perception injection for testing
- **Game Transport Layer**: UDP game state protocol with LiteNetLib
- SceneComposer SDK for scene composition tooling
- Configurable service parameters via environment variables

### Changed
- NuGet publishing improvements and SDK stabilization

---

## [0.6.0] - 2025-12-28

### Added
- **Behavior Service** (`lib-behavior`): ABML YAML DSL and GOAP planner
  - Bytecode compiler for behavior definitions
  - 414+ behavior system tests
  - Behavior cache with invalidation
- **GOAP Planner**: Goal-Oriented Action Planning for autonomous NPCs
- Actor system developer guide (`docs/guides/ACTOR_SYSTEM.md`)
- ABML reference guide (`docs/guides/ABML.md`)
- GOAP developer guide (`docs/guides/GOAP.md`)
- Comprehensive schema and documentation overhaul

---

## [0.5.0] - 2025-12-20

### Added
- **Documentation Service** (`lib-documentation`): Knowledge base API for AI agents
  - Git repository binding for automatic documentation sync
  - Full-text search and natural language queries
  - Archive and restore functionality
- TENETS reorganization into Foundation, Implementation, and Quality categories
- Infrastructure plugins for lib-state, lib-messaging, lib-mesh

### Changed
- Removed Dapr dependency in favor of native infrastructure libs
- Asset management system improvements

---

## [0.4.0] - 2025-12-15

### Added
- **Voice Service** (`lib-voice`): P2P and room-based voice communication
  - WebRTC peer connections
  - SIP signaling integration
  - Scaled tier support for large rooms
- Meta endpoints for runtime schema introspection via WebSocket
- Session shortcuts for pre-bound API calls with secure routing

---

## [0.3.0] - 2025-12-10

### Added
- **Character Service** (`lib-character`): Character entities with stats and capabilities
- **Character History Service** (`lib-character-history`): Backstory and event participation
- **Character Personality Service** (`lib-character-personality`): Machine-readable personality traits
- **Relationship Service** (`lib-relationship`): Bidirectional entity relationships
- **Relationship Type Service** (`lib-relationship-type`): Relationship taxonomy
- **Species Service** (`lib-species`): Character species management
- **Realm Service** (`lib-realm`): World/realm instances
- **Location Service** (`lib-location`): Named locations and zones
- Client event delivery system with session-specific WebSocket push
- Compact response protocol for reduced bandwidth

---

## [0.2.0] - 2025-12-05

### Added
- **Game Session Service** (`lib-game-session`): Lobby system with chat and reservations
- **Game Service** (`lib-game-service`): Game configuration and registry
- **Subscription Service** (`lib-subscription`): Premium accounts and time-limited access
- **Orchestrator Service** (`lib-orchestrator`): Dynamic instance provisioning
- **Permission Service** (`lib-permission`): Redis-backed role-based access control
- Complete OAuth implementation (Discord, Twitch, Google, Steam)
- Service event system with error eventing
- Idempotent permission registration

---

## [0.1.0] - 2025-11-28

### Added
- Initial release of Bannou platform
- **Auth Service** (`lib-auth`): Authentication with Email, OAuth, Steam, JWT
- **Account Service** (`lib-account`): Account management, verification, profiles
- **Connect Service** (`lib-connect`): WebSocket edge gateway with binary protocol
- **State Service** (`lib-state`): State store abstraction (Redis, MySQL)
- **Messaging Service** (`lib-messaging`): Pub/Sub abstraction (RabbitMQ)
- **Mesh Service** (`lib-mesh`): Service-to-service invocation (YARP)
- **Analytics Service** (`lib-analytics`): Event ingestion and Glicko-2 skill ratings
- **Leaderboard Service** (`lib-leaderboard`): Seasonal leaderboards with Redis sorted sets
- **Achievement Service** (`lib-achievement`): Achievement tracking with platform sync
- **Asset Service** (`lib-asset`): Asset distribution via MinIO/S3
- **Website Service** (`lib-website`): Public-facing website with CMS
- Schema-first development with OpenAPI code generation
- Zero-copy WebSocket routing with client-salted GUIDs
- Monoservice architecture (deploy as monolith or microservices)
- Comprehensive test infrastructure (unit, HTTP, WebSocket edge tests)

---

[Unreleased]: https://github.com/beyond-immersion/bannou-service/compare/v0.10.0...HEAD
[0.10.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.9.0...v0.10.0
[0.9.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.8.0...v0.9.0
[0.8.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/beyond-immersion/bannou-service/releases/tag/v0.1.0
