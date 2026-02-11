# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

---

## [0.13.0] - 2026-01-29

### Added
- **Currency Service** (`lib-currency`): Multi-currency management for game economies (32 endpoints)
  - Currency definitions with precision, scope, caps, autogain, expiration
  - Wallet management (create, freeze, close with balance transfer)
  - Balance operations (credit, debit, transfer with deadlock-free locking)
  - Authorization holds (reserve/capture/release pattern)
  - Currency conversion via exchange rates
  - Escrow integration wrappers
  - Background autogain task service
  - Idempotency-key deduplication throughout
- **Contract Service** (`lib-contract`): Binding agreements with milestone-based progression (30 endpoints)
  - Reactive design: external systems report conditions; contracts store state and execute prebound APIs
  - Template-based structure (party roles, milestones, terms)
  - Four enforcement modes (advisory, event_only, consequence_based, community)
  - Breach handling with ISO 8601 cure periods
  - Guardian custody for asset-backed contracts (escrow integration)
  - Prebound API execution with template substitution
- **Escrow Service** (`lib-escrow`): Multi-party asset exchange orchestration (20 endpoints)
  - 13-state finite state machine
  - Four escrow types (two_party, multi_party, conditional, auction)
  - Three trust modes (full_consent with tokens, initiator_trusted, single_party_trusted)
  - SHA-256 token authorization
  - Arbiter-mediated dispute resolution
  - Custom asset type handler registration
- **Inventory Service** (`lib-inventory`): Container and item placement management (16 endpoints)
  - Multiple constraint models (slot, weight, grid, volumetric, unlimited)
  - Item placement, movement, stacking operations
  - Equipment slots as specialized containers
  - Nested containers with weight propagation
  - Graceful degradation when item service unavailable
- **Item Service** (`lib-item`): Dual-model item management (13 endpoints)
  - Templates (immutable definitions) and Instances (individual occurrences)
  - Multiple quantity models (discrete, continuous, unique)
  - Soulbound/binding system (none, on_pickup, on_equip, on_use)
  - Redis read-through caching
- **Character Encounter Service** (`lib-character-encounter`): Memorable interaction tracking (19 endpoints)
  - Multi-participant encounter/perspective design
  - Time-based memory decay applied lazily
  - Weighted sentiment aggregation
- **TENETS v6.0**: Major documentation restructuring
  - New T25 (Type Safety): ALL models must use proper types (enums, GUIDs) - no string representations
  - T21 expanded: No dead configuration, no hardcoded tunables
  - T0 added: Never reference tenet numbers in source code
- **SCHEMA-RULES.md**: Comprehensive schema authoring reference (899 lines)
  - 10-step generation pipeline with dependencies
  - Extension attributes documentation
  - 30+ anti-patterns with explanations
- **Plugin Deep-Dives**: 41 comprehensive service documentation files at `docs/plugins/{SERVICE}.md`
- **Configuration Validation Framework**: New attributes translating OpenAPI keywords
  - `ConfigRangeAttribute`, `ConfigPatternAttribute`, `ConfigStringLengthAttribute`, `ConfigMultipleOfAttribute`
  - Startup validation via `IServiceConfiguration.Validate()`
- **Prebound API Infrastructure**: Template substitution and response validation for contract execution
- **Claude Safety Hooks**: 4 PreToolUse hooks enforcing development standards
- **Assembly Inspector** (`tools/bannou-inspect`): CLI for external API research
- **Code Generation Improvements**: `scripts/common.sh` (420 lines), `scripts/resolve-event-refs.py` (383 lines)

### Changed
- **Testing Infrastructure Reorganization**: HTTP and Edge testers moved to `tools/` directory
  - Edge tester updated to use strongly typed models (critical fix for type safety)
  - Typed proxy requirement enforces compile-time validation
  - TypeScript SDK parity validation via embedded harness
- **Client SDK Enhancements**: New helpers for detecting capability additions/removals on reconnection
- **SDK Updates**: 5 new service proxies across C#, TypeScript, and Unreal platforms
  - C#: Lazy-loaded proxy properties, ClientEndpointMetadata registry
  - TypeScript: 29,000+ line type definitions, discriminated union support
  - Unreal: 1,019+ line endpoint registry, 13,221+ line type definitions
- **State Store Expansion**: 23 new state stores across the 5 new services

### Fixed
- Type safety enforcement across all models (no more string-typed enums or GUIDs)

---

## [0.12.0] - 2026-01-19

### Added
- **Music Theory SDK** (`sdks/music-theory`): Pure computation music generation using formal music theory rules
  - Chord progression generation with voice leading
  - Melody generation over harmony
  - Scale and mode support
- **Music Storyteller SDK** (`sdks/music-storyteller`): Narrative-driven music composition
  - Story arc to musical structure mapping
  - Emotional trajectory support
- **global.json**: Pin .NET SDK version to 9.0.x for consistent builds across local and CI environments

### Changed
- **lib-state Infrastructure**: Consolidated state management with improved async metabundle processing
- **Meta Endpoint Schema Separation**: Cleaner API organization

### Fixed
- **CA2000 Warnings**: Fixed disposable object handling across edge-tester and SDK projects
- **CS8618 Warnings**: Fixed non-nullable field initialization warnings
- **IDE0031 Suppression**: Restored pragma for null-check patterns where null propagation isn't valid C#
- **SDK Release Process**: Fixed version baseline drift issues
- **CI Build Issues**: Resolved missing files and preview feature alignment

---

## [0.11.0] - 2026-01-16

### Added
- **SDK Architecture Overhaul**: Complete reorganization under `sdks/` directory
  - SDK Conventions document (`sdks/CONVENTIONS.md`) following Azure SDK naming guidelines
  - Separate `bannou-sdks.sln` solution for SDK development
  - Shared `SDK_VERSION` file for synchronized package versions
- **Client SDK Typed Proxies**: Generated proxy classes for all 33 services
  - Type-safe API calls with `client.Account.GetAsync()` pattern
  - Full IntelliSense support with XML documentation
  - Automatic request/response serialization
- **Client Event Registry**: Centralized event type registration and dispatch
  - Generated `ClientEventRegistry` for all service events
  - Type-safe event subscription via `client.Events.Subscribe<T>()`
- **Asset Metabundles**: Compose optimized bundles from multiple source bundles
  - Provenance metadata tracking source bundle origins
  - Asset deduplication across source bundles
- **Asset Bundler SDKs**: Engine-specific asset processing pipelines
  - `asset-bundler-godot`: Godot 4.x resource processing
  - `asset-bundler-stride`: Stride engine asset compilation
  - `asset-loader-godot`: Godot runtime asset loading
  - `asset-loader-stride`: Stride runtime asset loading
- **Client Integration Tests**: New `client.integration.tests` project for tests requiring both compiler and client runtime

### Changed
- **ServiceLib.targets Refactor**: Improved build infrastructure for service plugins
- **SDK Directory Structure**: All SDKs moved from `Bannou.SDK/` to `sdks/` with consistent naming
- **Transport Layer**: Extracted to `sdks/transport/` as shared internal library
- **Bundle Format**: Extracted to `sdks/bundle-format/` as shared internal library

### Fixed
- MinIO SDK signed header issues resolved by switching to Amazon S3 SDK
- GUID normalization allowing dashes in Redis keys
- Asset upload presigned URL generation with correct service domain
- Namespace collision in client SDK tests (round-trip tests moved to integration project)

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
- **Releasing Guide** (`docs/operations/RELEASING.md`): Complete release workflow documentation
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

[Unreleased]: https://github.com/beyond-immersion/bannou-service/compare/v0.13.0...HEAD
[0.13.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.12.0...v0.13.0
[0.12.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.11.0...v0.12.0
[0.11.0]: https://github.com/beyond-immersion/bannou-service/compare/v0.10.0...v0.11.0
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
