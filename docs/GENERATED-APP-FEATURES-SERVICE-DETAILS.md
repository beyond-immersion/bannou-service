# Generated App Features (L3) Service Details

> **Source**: `docs/plugins/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

Services in the **App Features (L3)** layer.

## Asset {#asset}

**Version**: 1.0.0 | **Schema**: `schemas/asset-api.yaml` | **Endpoints**: 20 | **Deep Dive**: [docs/plugins/ASSET.md](plugins/ASSET.md)

The Asset service (L3 AppFeatures) provides storage, versioning, and distribution of large binary assets (textures, audio, 3D models) using MinIO/S3-compatible object storage. Issues pre-signed URLs so clients upload/download directly to the storage backend, never routing raw asset data through the WebSocket gateway. Also manages bundles (grouped assets in a custom `.bannou` format with LZ4 compression), metabundles (merged super-bundles), and a distributed processor pool for content-type-specific transcoding. Used by lib-behavior, lib-save-load, lib-mapping, and lib-documentation for binary storage needs.

## Broadcast {#broadcast}

**Deep Dive**: [docs/plugins/BROADCAST.md](plugins/BROADCAST.md)

Platform streaming integration and RTMP output management service (L3 AppFeatures) for linking external streaming platforms (Twitch, YouTube, custom RTMP), ingesting real audience data, and broadcasting server-side content. The bridge between Bannou's internal world and external streaming platforms -- everything that touches a third-party streaming service goes through lib-broadcast. Game-agnostic: which platforms are enabled and how sentiment categories map to game emotions are configured via environment variables and API calls. Internal-only for sentiment/broadcast management; webhook endpoints are internet-facing for platform callbacks.

## Documentation {#documentation}

**Version**: 1.0.0 | **Schema**: `schemas/documentation-api.yaml` | **Endpoints**: 27 | **Deep Dive**: [docs/plugins/DOCUMENTATION.md](plugins/DOCUMENTATION.md)

Knowledge base API (L3 AppFeatures) designed for AI agents (SignalWire SWAIG, OpenAI function calling, Claude tool use) with full-text search, natural language query, and voice-friendly summaries. Manages documentation within namespaces, supporting manual CRUD and automated git repository synchronization (git-bound namespaces reject mutations, enforcing git as single source of truth). Features browser-facing GET endpoints that render markdown to HTML (unusual exception to Bannou's POST-only pattern). Three background services handle index rebuilding, periodic repository sync, and trashcan purge.

## Orchestrator {#orchestrator}

**Version**: 3.0.0 | **Schema**: `schemas/orchestrator-api.yaml` | **Endpoints**: 22 | **Deep Dive**: [docs/plugins/ORCHESTRATOR.md](plugins/ORCHESTRATOR.md)

Central intelligence (L3 AppFeatures) for Bannou environment management and service orchestration. Manages distributed service deployments including preset-based topologies, live topology updates, processing pools for on-demand worker containers (used by lib-actor for NPC brains), service health monitoring via heartbeats, versioned deployment configurations with rollback, and service-to-app-id routing broadcasts consumed by lib-mesh. Features a pluggable backend architecture supporting Docker Compose, Docker Swarm, Portainer, and Kubernetes. Operates in a secure mode making it inaccessible via WebSocket (admin-only service-to-service calls).

## Voice {#voice}

**Version**: 2.0.0 | **Schema**: `schemas/voice-api.yaml` | **Endpoints**: 11 | **Deep Dive**: [docs/plugins/VOICE.md](plugins/VOICE.md)

Voice room coordination service (L3 AppFeatures) providing pure voice rooms as a platform primitive: P2P mesh topology for small groups, Kamailio/RTPEngine-based SFU for larger rooms, automatic tier upgrade, WebRTC SDP signaling, broadcast consent flows for streaming integration, and participant TTL enforcement via background worker. Agnostic to games, sessions, and subscriptions -- voice rooms are generic containers identified by Connect/Auth session IDs. Part of a planned three-service stack (voice, broadcast, showtime) where each delivers value independently; voice provides audio infrastructure while higher layers decide when and why to use it. Moved from L4 to L3 to eliminate a hierarchy violation where GameSession (L2) previously depended on Voice (L4) for room lifecycle.

## Website {#website}

**Version**: 1.0.0 | **Schema**: `schemas/website-api.yaml` | **Endpoints**: 14 | **Deep Dive**: [docs/plugins/WEBSITE.md](plugins/WEBSITE.md)

Public-facing website service (L3 AppFeatures) for browser-based access to news, account profiles, game downloads, CMS pages, and contact forms. Intentionally does NOT access game data (characters, subscriptions, realms) to respect the service hierarchy. Uses traditional REST HTTP methods (GET, PUT, DELETE) with path parameters for browser compatibility, which is an explicit exception to Bannou's POST-only pattern. **Currently a complete stub** -- every endpoint returns `NotImplemented`. When implemented, will require lib-account integration and state stores for CMS data.

## Summary

- **Services in layer**: 6
- **Endpoints in layer**: 94

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
