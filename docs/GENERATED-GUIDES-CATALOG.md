# Generated Guides Catalog

> **Source**: `docs/guides/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

Developer guides covering cross-cutting Bannou systems and workflows.

## ABML - Arcadia Behavior Markup Language {#abml}

**Version**: 3.0 | **Status**: Implemented | [Full Guide](guides/ABML.md)

ABML is a YAML-based domain-specific language for authoring event-driven, stateful sequences of actions. It powers NPC behaviors, dialogue systems, cutscenes, and agent cognition in Bannou-powered games.

## Bannou Asset SDK Guide {#asset-sdk}

[Full Guide](guides/ASSET-SDK.md)

This guide explains how to use the Bannou Asset SDKs for both **consuming** assets in game clients and **producing** bundles with content tools.

## Asset Service Developer Guide {#asset-service}

[Full Guide](guides/ASSET-SERVICE.md)

The Asset Service provides binary asset management for Bannou including storage, versioning, processing, and bundling of textures, 3D models, audio files, and asset bundles.

## Behavior System - Bringing Worlds to Life {#behavior-system}

**Version**: 3.0 | **Status**: Production-ready (all core systems implemented) | **Key Plugins**: `lib-actor` (L2), `lib-behavior` (L4), `lib-puppetmaster` (L4), `lib-character-personality` (L4), `lib-character-encounter` (L4), `lib-character-history` (L4), `lib-quest` (L2) | [Full Guide](guides/BEHAVIOR-SYSTEM.md)

The Behavior System is the cognitive layer that makes Arcadia's worlds alive. It gives NPCs personality, memory, and growth. It orchestrates dramatic encounters. It implements the gods who curate regional flavor. It is the engine behind the vision's promise: **living worlds where content emerges from accumulated play history, not hand-authored content**.

## Behavioral Bootstrap Pattern {#behavioral-bootstrap}

**Version**: 1.0 | **Last Updated**: 2026-02-14 | [Full Guide](guides/BEHAVIORAL-BOOTSTRAP.md)

This document describes how Puppetmaster and Gardener bootstrap autonomous god-actors that serve as the connective tissue between Bannou's disparate services. These god-actors are the "drive belt" of the content flywheel and the player experience orchestration system. They are **not a plugin** -- they are authored ABML behavior documents executed by the Actor runtime.

## Character Communication - Lexicon-Shaped Social Interaction {#character-communication}

**Version**: 1.0 | **Status**: Aspirational (no implementation yet) | [Full Guide](guides/CHARACTER-COMMUNICATION.md)

Character Communication is the social interaction layer for Arcadia's living world. NPCs communicate using structured Lexicon entry combinations rather than free text, creating a universal protocol that both NPCs and players can understand, that discovery levels gate progressively, and that Hearsay distorts as it propagates through social networks.

## Client Integration Guide {#client-integration}

[Full Guide](guides/CLIENT-INTEGRATION.md)

This guide covers integrating game clients with Bannou services via the WebSocket protocol.

## Bannou Development Quickstart {#development-quickstart}

[Full Guide](guides/DEVELOPMENT-QUICKSTART.md)

| I want to... | Time | Guide |
|--------------|------|-------|
| **Run Bannou locally** | 5 min | [Local Development](#local-development) (this page) |
| **Connect a game client** | 15 min | [Client Integration](#client-integration) |
| **Make service-to-service calls** | 10 min | [Service SDK](#service-sdk) |
| **Understand the architecture** | 30 min | [Getting Started](GETTING_STARTED.md) |
| **Add a new service** | 1 hr | [Plugin Development](PLUGIN_DEVELOPMENT.md) |

## Economy System - Architecture & Design {#economy-system}

**Version**: 1.1 | **Status**: Foundation services production-ready; economy feature services have architectural specifications (deep dives) | [Full Guide](guides/ECONOMY-SYSTEM.md)

Economy is not a single plugin but an **architectural layer** spanning multiple services. This guide documents the cross-cutting design: how foundation services compose into a living economy, how NPCs participate as economic actors, how divine entities maintain economic health through narrative intervention, and what planned services will complete the picture.

## Extending Bannou - Building L5 Extension Plugins {#extending-bannou}

**Version**: 1.0 | **Status**: Reference guide | [Full Guide](guides/EXTENDING-BANNOU.md)

Bannou provides 48+ services with 690+ endpoints covering everything a multiplayer game backend needs -- authentication, economies, inventories, quests, matchmaking, voice, spatial data, NPC intelligence, and more. These services are deliberately **generic**: there is no "skills plugin," no "magic plugin," no "combat plugin," no "guild plugin." Game concepts like skills, spells, and guilds emerge from the **composition** of lower-level primitives (Seed, License, Status, Collection, Actor, Organization, Faction, Contract, and others).

## Getting Started with Bannou {#getting-started}

[Full Guide](guides/GETTING-STARTED.md)

This guide walks you through setting up Bannou from scratch, understanding its architecture, and integrating with game clients and backend services.

## Mapping System - Spatial Intelligence for Game Worlds {#mapping-system}

**Version**: 1.0 | **Status**: Implemented | [Full Guide](guides/MAPPING-SYSTEM.md)

The Mapping System (`lib-mapping`) manages spatial data for game worlds. It enables game servers to publish live spatial data, event actors to orchestrate region-wide encounters, NPC actors to receive perception-filtered context, and behaviors to make spatially-aware decisions through affordance queries.

## Matchmaking and Game Sessions Developer Guide {#matchmaking}

[Full Guide](guides/MATCHMAKING.md)

This guide covers the Matchmaking Service (`lib-matchmaking`) and its integration with Game Sessions (`lib-game-session`) for competitive and casual multiplayer game matching.

## Meta Endpoints Guide {#meta-endpoints}

[Full Guide](guides/META-ENDPOINTS.md)

Meta endpoints are auto-generated companion endpoints that provide runtime schema introspection for every API operation in Bannou. They enable clients and tools to discover what an endpoint accepts, returns, and does -- without reading external documentation.

## Morality System - Conscience, Norms, and Second Thoughts {#morality-system}

**Version**: 1.0 | **Key Plugins**: `lib-faction` (L4), `lib-obligation` (L4) | [Full Guide](guides/MORALITY-SYSTEM.md)

The Morality System gives NPCs a conscience. An honest merchant hesitates before swindling a customer. A loyal knight resists betraying their lord even when tactically optimal. A character in lawless territory acts with less restraint than one standing in a temple district. This is not scripted morality -- it is emergent moral reasoning arising from the intersection of social context, personal character, and contractual commitments, expressed as GOAP action cost modifications that change what NPCs choose to do.

## Bannou Music System {#music-system}

[Full Guide](guides/MUSIC-SYSTEM.md)

This document provides a comprehensive overview of Bannou's procedural music generation system, including the theoretical foundations, SDK architecture, and integration patterns.

## Plugin Development Guide {#plugin-development}

[Full Guide](guides/PLUGIN-DEVELOPMENT.md)

This guide walks through creating and extending Bannou service plugins using schema-first development.

## Production Quickstart Guide {#production-quickstart}

[Full Guide](guides/PRODUCTION-QUICKSTART.md)

This guide walks you through deploying Bannou on a fresh server (e.g., DigitalOcean droplet) in monoservice mode with all services enabled.

## Save System Guide {#save-system}

[Full Guide](guides/SAVE-SYSTEM.md)

This guide explains how to use Bannou's Save-Load service for game state persistence.

## Scene System Guide {#scene-system}

**Version**: 2.0.0 | **Status**: PRODUCTION | [Full Guide](guides/SCENE-SYSTEM.md)

The Scene System is the content authoring pipeline for Bannou game worlds. It spans three layers:

## Bannou SDKs Overview {#sdk-overview}

[Full Guide](guides/SDK-OVERVIEW.md)

Bannou's SDK ecosystem follows a consistent three-layer pattern across creative domains: **theory** (formal primitives), **storyteller** (procedural generation), and **composer** (handcrafted authoring). Each layer is independently useful, and each domain doesn't necessarily need all three -- the pattern emerges where the domain's complexity warrants it.

## Seed System - Progressive Growth for Living Worlds {#seed-system}

**Version**: 1.1 | **Status**: Seed service and Gardener (first consumer) implemented | [Full Guide](guides/SEED-SYSTEM.md)

The Seed System provides the foundational growth primitive for Arcadia's progressive mastery model. Seeds are entities that start empty and grow by accumulating metadata across named domains, progressively gaining capabilities at configurable thresholds. They power guardian spirits, dungeon cores, combat archetypes, crafting specializations, and any future system that needs "progressive growth in a role."

## Bannou Story System {#story-system}

[Full Guide](guides/STORY-SYSTEM.md)

This document provides a comprehensive overview of Bannou's narrative generation system, including the theoretical foundations, plugin architecture, and integration patterns for both traditional quest-hub gameplay and emergent AI-driven storytelling.

## TypeScript SDK Integration Guide {#typescript-sdk}

[Full Guide](guides/TYPESCRIPT-SDK.md)

This guide covers integrating the Bannou TypeScript SDK into browser and Node.js applications.

## Unreal Engine Integration Guide {#unreal-integration}

[Full Guide](guides/UNREAL-INTEGRATION.md)

This guide covers integrating Bannou services into Unreal Engine 4 and 5 projects using the generated helper artifacts.

## Summary

- **Documents in catalog**: 25

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
