# Generated Guides Catalog

> **Source**: `docs/guides/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

Developer guides covering cross-cutting Bannou systems and workflows.

## ABML - Arcadia Behavior Markup Language {#abml}

**Version**: 3.0 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-behavior (L4), lib-actor (L2) | [Full Guide](guides/ABML.md)

Comprehensive reference for the Arcadia Behavior Markup Language (ABML), a YAML-based DSL for authoring event-driven, stateful action sequences that power NPC behaviors, dialogue systems, cutscenes, and agent cognition. Covers document structure, the expression language with variable providers, control flow, GOAP integration, channels and parallelism, and the compilation pipeline from YAML to portable stack-based bytecode. Intended for developers writing or modifying ABML behavior documents.

## Bannou Asset SDK {#asset-sdk}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-asset (L3) | [Full Guide](guides/ASSET-SDK.md)

Explains how to use the Bannou Asset SDKs for consuming assets in game clients via the asset-loader-client and producing bundles with content tools via the asset-bundler. Covers the consumer-side AssetManager for downloading and loading bundles with LZ4 compression, and the producer-side BundleBuilder for creating and uploading asset bundles to the MinIO/S3-backed Asset service.

## Asset Service Developer Guide {#asset-service}

**Version**: 1.1 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-asset (L3) | [Full Guide](guides/ASSET-SERVICE.md)

Developer guide for the Asset service covering binary asset management including storage, versioning, processing, and bundling of textures, 3D models, and audio files via MinIO/S3-compatible object storage. Covers the pre-signed URL architecture for direct client uploads and downloads, the custom .bannou bundle format with LZ4 compression, the processing pool pipeline, and metabundle merging. Intended for developers integrating asset workflows into game clients or content tools.

## Behavior System - Bringing Worlds to Life {#behavior-system}

**Version**: 3.1 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-actor (L2), lib-behavior (L4), lib-puppetmaster (L4), lib-character-personality (L4), lib-character-encounter (L4), lib-character-history (L4), lib-quest (L2) | [Full Guide](guides/BEHAVIOR-SYSTEM.md)

Comprehensive guide to the NPC intelligence stack covering the Actor runtime, ABML compilation, GOAP planning, the 5-stage cognition pipeline, variable providers, behavior document authoring, cutscene orchestration, and scaling architecture. Intended for developers building or modifying NPC behaviors, integrating character data providers, or understanding how autonomous agents operate within the service hierarchy. After reading, developers will understand the full path from ABML YAML to compiled bytecode executing in the Actor runtime, the Variable Provider Factory pattern enabling L4 data flow into L2, and the Event Brain coordination model.

## Behavioral Bootstrap Pattern {#behavioral-bootstrap}

**Version**: 1.1 | **Status**: Aspirational | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-actor (L2), lib-puppetmaster (L4), lib-gardener (L4), lib-divine (L4) | [Full Guide](guides/BEHAVIORAL-BOOTSTRAP.md)

Cross-cutting architectural pattern describing how Puppetmaster and Gardener bootstrap autonomous god-actors as the connective tissue between Bannou's decomposed services, driving the content flywheel and player experience orchestration. God-actors are long-running ABML behavior documents executed by the Actor runtime, not a plugin. Intended for developers working on divine actors, regional watchers, gardener behaviors, or the content flywheel pipeline. After reading, developers will understand the full bootstrap sequence, god-actor lifecycle, and how orchestration emerges from authored behavior content rather than compiled service code.

## Character Communication - Lexicon-Shaped Social Interaction {#character-communication}

**Version**: 1.0 | **Status**: Aspirational | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-chat (L1), lib-collection (L2), lib-actor (L2), lib-worldstate (L2), lib-lexicon (L4), lib-hearsay (L4), lib-disposition (L4), lib-ethology (L4) | [Full Guide](guides/CHARACTER-COMMUNICATION.md)

Aspirational design for a Lexicon-shaped social interaction layer where NPCs communicate using structured concept combinations rather than free text, enabling machine-parseable social behavior without NLP. Covers the lexicon room type for Chat, the social variable provider for Actor cognition, Hearsay-based belief propagation with concept-level distortion, and Disposition-driven communication motivations. Intended for developers planning NPC social behavior systems or integrating with the Lexicon, Hearsay, or Disposition services.

## Client Integration Guide {#client-integration}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-connect (L1), lib-auth (L1), lib-permission (L1) | [Full Guide](guides/CLIENT-INTEGRATION.md)

Covers integrating game clients with Bannou services via the WebSocket binary protocol, including authentication flows (email/password, OAuth, Steam), WebSocket connection establishment, the 31-byte binary message format, capability manifest management, session shortcuts, server-pushed events, reconnection, and both the .NET and TypeScript client SDKs. Intended for game engine developers building client-side networking against Bannou backends.

## Bannou Development Quickstart {#development-quickstart}

**Version**: 1.1 | **Status**: Production | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-mesh (L0), lib-auth (L1), lib-connect (L1), lib-character (L2) | [Full Guide](guides/DEVELOPMENT-QUICKSTART.md)

Quickstart guide for getting Bannou running locally in five minutes, connecting a game client via WebSocket, and making service-to-service calls using generated clients. Intended for experienced developers joining the project who need to build, run, and interact with the platform immediately. After reading, developers will have a working local environment and understand the three primary integration surfaces: Docker Compose stack, WebSocket client SDK, and generated mesh clients.

## Economy System - Architecture & Design {#economy-system}

**Version**: 1.2 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-currency (L2), lib-item (L2), lib-inventory (L2), lib-contract (L1), lib-escrow (L4), lib-affix (L4), lib-craft (L4), lib-loot (L4), lib-market (L4), lib-trade (L4), lib-workshop (L4) | [Full Guide](guides/ECONOMY-SYSTEM.md)

Cross-cutting guide to the Bannou economy architecture spanning foundation services (Currency, Item, Inventory, Contract, Escrow) and feature services (Affix, Craft, Loot, Market, Trade, Workshop). Covers faucet/sink discipline, NPC economic participation via GOAP, divine economic intervention, quest-economy integration, exchange rate extensions, and scale strategies for 100K+ NPC economies. Intended for developers building or extending economic game mechanics across the service mesh.

## Extending Bannou - Building L5 Extension Plugins {#extending-bannou}

**Version**: 1.0 | **Status**: Aspirational | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-seed (L2), lib-license (L4), lib-collection (L2), lib-status (L4), lib-actor (L2), lib-faction (L4), lib-organization (L4) | [Full Guide](guides/EXTENDING-BANNOU.md)

Guide to building L5 Extension plugins that provide game-specific vocabulary, simplified APIs, and opinionated defaults on top of Bannou's generic L0-L4 primitives. Covers the six extension patterns (Semantic Facade, Composition Orchestrator, Configuration Hardener, View Aggregator, Event Translator, Variable Provider), an extension catalog organized by gameplay domain, genre kit examples, DI integration points, anti-patterns, and a complete step-by-step tutorial building a Reputation extension. Intended for developers building game-specific plugins that compose existing Bannou services into domain-specific APIs.

Bannou provides 70+ services with 900+ endpoints covering everything a multiplayer game backend needs -- authentication, economies, inventories, quests, matchmaking, voice, spatial data, NPC intelligence, and more. These services are deliberately **generic**: there is no "skills plugin," no "magic plugin," no "combat plugin," no "guild plugin." Game concepts like skills, spells, and guilds emerge from the **composition** of lower-level primitives (Seed, License, Status, Collection, Actor, Organization, Faction, Contract, and others).

This design gives Bannou maximum flexibility. But flexibility has a cost: a developer building a fantasy RPG doesn't want to think about "seed domain depths" and "license board adjacency" -- they want to call `SkillsClient.GetLevel("swordsmanship", characterId)` and get an integer back.

**That's what extensions are for.** L5 Extension plugins provide game-specific vocabulary, simplified APIs, and opinionated defaults on top of Bannou's generic primitives. This guide explains how to build them.

## Getting Started with Bannou {#getting-started}

**Version**: 1.1 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-auth (L1), lib-connect (L1), lib-account (L1), lib-state (L0), lib-messaging (L0), lib-mesh (L0) | [Full Guide](guides/GETTING-STARTED.md)

Step-by-step onboarding guide covering development environment setup, architecture orientation, configuration, Docker Compose local development, client SDK integration via WebSocket, and service SDK integration via generated mesh clients. Intended for developers new to Bannou who need to go from zero to a running local stack with working client and service communication. After reading, developers will be able to build, run, and extend Bannou services using the schema-first workflow.

## Mapping System - Spatial Intelligence for Game Worlds {#mapping-system}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-mapping (L4) | [Full Guide](guides/MAPPING-SYSTEM.md)

Comprehensive guide to the Mapping service's spatial data management, authority-based channel ownership, affordance query system, and actor integration patterns. Intended for developers building game servers that publish spatial data, writing NPC behaviors that consume spatial context, or creating event actors that orchestrate region-wide encounters. After reading, developers will understand the authority lifecycle, event architecture, affordance scoring pipeline, and how to integrate spatial intelligence into ABML behaviors.

## Matchmaking and Game Sessions Developer Guide {#matchmaking}

**Version**: 1.1 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-matchmaking (L4), lib-game-session (L2) | [Full Guide](guides/MATCHMAKING.md)

Developer guide covering ticket-based matchmaking and game session integration for competitive and casual multiplayer matching. Covers queue configuration, skill expansion curves, match accept/decline flows, reservation systems, reconnection handling, and queue configuration presets for common game modes. Intended for developers integrating matchmaking into game clients or configuring queue parameters for new game modes.

## Meta Endpoints Guide {#meta-endpoints}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-connect (L1) | [Full Guide](guides/META-ENDPOINTS.md)

Covers Bannou's auto-generated meta endpoints, which provide runtime JSON Schema introspection for every API operation. Intended for client SDK developers needing self-describing protocols and service developers wanting to understand how meta endpoints are generated, routed via WebSocket and HTTP, and secured through the capability manifest permission model.

## Morality System - Conscience, Norms, and Second Thoughts {#morality-system}

**Version**: 1.1 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-faction (L4), lib-obligation (L4) | [Full Guide](guides/MORALITY-SYSTEM.md)

Cross-service guide to the morality pipeline that gives NPCs emergent moral reasoning through GOAP action cost modifications. Covers how lib-faction provides the social norm landscape (guild codes, territorial laws, realm-wide cultural norms) and how lib-obligation transforms those norms plus contractual obligations into personality-weighted costs that change NPC behavior. Intended for developers authoring ABML behaviors, designing faction norm structures, or integrating contract behavioral clauses with the obligation system.

The Morality System gives NPCs a conscience. An honest merchant hesitates before swindling a customer. A loyal knight resists betraying their lord even when tactically optimal. A character in lawless territory acts with less restraint than one standing in a temple district. This is not scripted morality -- it is emergent moral reasoning arising from the intersection of social context, personal character, and contractual commitments, expressed as GOAP action cost modifications that change what NPCs choose to do.

## Bannou Music System {#music-system}

**Version**: 1.0 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-music (L4) | [Full Guide](guides/MUSIC-SYSTEM.md)

Comprehensive guide to Bannou's procedural music generation system covering the MusicTheory SDK (pitch, harmony, scales, voice leading, MIDI-JSON output) and the MusicStoryteller SDK (narrative-driven composition via GOAP planning and emotional state tracking). Intended for developers integrating music generation into game features or authoring custom styles. After reading, developers will understand the two-layer SDK architecture, the theoretical foundations (Lerdahl, Huron, Juslin, Meyer), and how to use the Music service API for composition generation.

## Plugin Development Guide {#plugin-development}

**Version**: 2.0 | **Status**: Production | **Last Updated**: 2026-03-08 | **Key Plugins**: All lib-* plugins (L0-L4) | [Full Guide](guides/PLUGIN-DEVELOPMENT.md)

Comprehensive guide to creating and extending Bannou service plugins using schema-first development. Covers the full plugin lifecycle from OpenAPI schema definition through code generation, business logic implementation, state management, event publishing, service-to-service calls, permissions, configuration, and testing. Intended for developers building new services or extending existing ones. After reading, developers will understand the plugin structure, generation pipeline, and implementation patterns required for any Bannou service.

## Production Quickstart Guide {#production-quickstart}

**Version**: 1.0 | **Status**: Production | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-auth (L1), lib-connect (L1), lib-account (L1), lib-mesh (L0), lib-state (L0), lib-messaging (L0) | [Full Guide](guides/PRODUCTION-QUICKSTART.md)

Step-by-step guide for deploying Bannou on a fresh server in monoservice mode with all services enabled, covering dependency installation, secret generation, environment configuration, Docker image building, SSL/TLS setup, and deployment verification. Intended for operators deploying Bannou to production for the first time. After following this guide, operators will have a running Bannou instance with OpenResty edge proxy, health checks passing, and an admin account registered.

## Save System Guide {#save-system}

**Version**: 1.1 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-save-load (L4) | [Full Guide](guides/SAVE-SYSTEM.md)

Guide to Bannou's Save-Load service for game state persistence, covering slots, versioned saves, delta/incremental saves, schema migration, export/import, and the two-tier storage architecture (Redis hot cache with async MinIO upload). Intended for developers integrating save/load functionality into game services. After reading, developers will understand how to create save slots, manage versions, use delta saves for efficiency, and handle schema migrations when game data formats change.

## Scene System Guide {#scene-system}

**Version**: 2.0 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-scene (L4), lib-asset (L3) | [Full Guide](guides/SCENE-SYSTEM.md)

Comprehensive guide to the scene composition pipeline covering hierarchical scene document storage in lib-scene, binary asset distribution via lib-asset, and the SceneComposer SDK for engine-agnostic authoring. Intended for developers building scene editing tools, integrating game engines, or working with scene data in higher-layer services. After reading, developers will understand the full content authoring pipeline from scene document creation through checkout/commit workflows to runtime instantiation and consumer event handling.

## Bannou SDKs Overview {#sdk-overview}

**Version**: 1.1 | **Status**: Production | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-music (L4), lib-storyline (L4), lib-behavior (L4), lib-scene (L4), lib-actor (L2) | [Full Guide](guides/SDK-OVERVIEW.md)

Comprehensive catalog of Bannou's SDK ecosystem organized by the three-layer creative domain pattern (theory, storyteller, composer) and infrastructure categories (connectivity, asset pipeline, TypeScript, Unreal). Intended for developers selecting which SDKs to integrate into game clients, editor tools, or server-side services. After reading, developers will understand how SDKs relate to plugins, which packages to use for their platform, and the unified scenario pattern that enables mixing hand-authored and procedurally generated content.

## Seed System - Progressive Growth for Living Worlds {#seed-system}

**Version**: 1.3 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-seed (L2), lib-gardener (L4) | [Full Guide](guides/SEED-SYSTEM.md)

Explains the Seed progressive growth primitive and its consumer pattern, covering seed types, growth domains, capability manifests, bonds, and the Collection-to-Seed pipeline. Intended for developers building systems that need progressive mastery tracking. After reading, developers will understand how to register seed types, contribute growth, query capabilities, and implement new consumers like Gardener, Faction, and Dungeon.

## Bannou Story System {#story-system}

**Version**: 1.1 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-storyline (L4), lib-quest (L2), lib-contract (L1) | [Full Guide](guides/STORY-SYSTEM.md)

Comprehensive guide to Bannou's narrative generation system covering the three-layer narrative stack (storyline-theory SDK, storyline-storyteller SDK, and the Storyline plugin), two execution modes (simple direct trigger and emergent regional watcher discovery), lazy phase evaluation, and compression-driven content recycling. Intended for developers building narrative features or authoring ABML behavior documents for regional watchers. After reading, developers will understand how scenarios compose into storyline arcs, how quests wrap contracts, and how the content flywheel turns character deaths into new narratives.

> **Implementation Status**: The storyline SDKs are **complete** with 22 passing tests. The `lib-storyline` plugin provides `/compose`, `/plan/get`, and `/plan/list` endpoints. lib-quest is implemented. Scenario capabilities are planned extensions. See [Implementation Status](#implementation-status) for details.

## TypeScript SDK Integration {#typescript-sdk}

**Version**: 1.1 | **Status**: Aspirational | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-connect (L1), lib-auth (L1) | [Full Guide](guides/TYPESCRIPT-SDK.md)

Covers integrating the Bannou TypeScript SDK into browser and Node.js applications via the bannou-core and bannou-client packages. Intended for frontend and game client developers building real-time applications on the Bannou WebSocket gateway. After reading, developers will understand authentication flows, typed service proxy usage, server-push event subscriptions, and framework integration patterns for React and Vue.

## Unreal Engine Integration Guide {#unreal-integration}

**Version**: 1.1 | **Status**: Implemented | **Last Updated**: 2026-03-08 | **Key Plugins**: lib-connect (L1) | [Full Guide](guides/UNREAL-INTEGRATION.md)

Covers integrating Bannou services into Unreal Engine 4 and 5 projects using generated C++ helper artifacts including type definitions, protocol constants, endpoint registries, and event definitions. Intended for Unreal Engine developers connecting their game to Bannou backend services. After reading, developers will understand how to use the generated headers, implement the binary WebSocket protocol, and handle request/response correlation.

## Summary

- **Documents in catalog**: 25

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
