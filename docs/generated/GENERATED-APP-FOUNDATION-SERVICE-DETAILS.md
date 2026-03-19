# Generated App Foundation (L1) Service Details

> **Source**: `docs/plugins/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

Services in the **App Foundation (L1)** layer.

## Account {#account}

**Version**: 2.0.0 | **Schema**: `schemas/account-api.yaml` | **Endpoints**: 18 | **Deep Dive**: [docs/plugins/ACCOUNT.md](../plugins/ACCOUNT.md) | **Map**: [docs/maps/ACCOUNT.md](../maps/ACCOUNT.md)

The Account plugin is an internal-only CRUD service (L1 AppFoundation) for managing user accounts. It is never exposed directly to the internet -- all external account operations go through the Auth service, which calls Account via lib-mesh. Handles account creation, lookup (by ID, email, or OAuth provider), updates, soft-deletion, and authentication method management (linking/unlinking OAuth providers). Email is optional -- accounts created via OAuth or Steam may have no email address, identified solely by their linked authentication methods.

## Auth {#auth}

**Version**: 4.0.0 | **Schema**: `schemas/auth-api.yaml` | **Endpoints**: 19 | **Deep Dive**: [docs/plugins/AUTH.md](../plugins/AUTH.md) | **Map**: [docs/maps/AUTH.md](../maps/AUTH.md)

The Auth plugin is the internet-facing authentication and session management service (L1 AppFoundation). Handles email/password login, OAuth provider integration (Discord, Google, Twitch), Steam session ticket verification, JWT token generation/validation, password reset flows, TOTP-based MFA, and session lifecycle management. It is the primary gateway between external users and the internal service mesh -- after authenticating, clients receive a JWT and a WebSocket connect URL to establish persistent connections via lib-connect.

## Chat {#chat}

**Version**: 1.0.0 | **Schema**: `schemas/chat-api.yaml` | **Endpoints**: 33 | **Deep Dive**: [docs/plugins/CHAT.md](../plugins/CHAT.md) | **Map**: [docs/maps/CHAT.md](../maps/CHAT.md)

The Chat service (L1 AppFoundation) provides universal typed message channel primitives for real-time communication. Room types determine valid message formats (text, sentiment, emoji, custom-validated payloads), with rooms optionally governed by Contract instances for lifecycle management. Supports ephemeral (Redis TTL) and persistent (MySQL) message storage, participant moderation (kick/ban/mute), rate limiting via atomic Redis counters, typing indicators via Redis sorted set with server-side expiry, and automatic idle room cleanup. Three built-in room types (text, sentiment, emoji) are registered on startup. Internal-only, never internet-facing.

## Connect {#connect}

**Version**: 2.0.0 | **Schema**: `schemas/connect-api.yaml` | **Endpoints**: 7 | **Deep Dive**: [docs/plugins/CONNECT.md](../plugins/CONNECT.md) | **Map**: [docs/maps/CONNECT.md](../maps/CONNECT.md)

WebSocket-first edge gateway (L1 AppFoundation) providing zero-copy binary message routing between game clients and backend services. Manages persistent connections with client-salted GUID generation for cross-session security, three connection modes (external, relayed, internal), session shortcuts for game-specific flows, reconnection windows, per-session RabbitMQ subscriptions for server-to-client event delivery, and multi-node broadcast relay via a WebSocket mesh between Connect instances. Internet-facing (the primary client entry point alongside Auth). Registered as Singleton (unusual for Bannou) because it maintains in-memory connection state.

## Contract {#contract}

**Version**: 1.0.0 | **Schema**: `schemas/contract-api.yaml` | **Endpoints**: 32 | **Deep Dive**: [docs/plugins/CONTRACT.md](../plugins/CONTRACT.md) | **Map**: [docs/maps/CONTRACT.md](../maps/CONTRACT.md)

Binding agreement management (L1 AppFoundation) between entities with milestone-based progression, consent flows, and prebound API execution on state transitions. Contracts are reactive: external systems report condition fulfillment via API calls; contracts store state, emit events, and execute callbacks. Templates define structure (party roles, milestones, terms, enforcement mode); instances track consent, sequential progression, and breach handling. Used as infrastructure by lib-quest (quest objectives map to contract milestones) and lib-escrow (asset-backed contracts via guardian locking).

## Localization {#localization}

**Version**: 1.0.0 | **Schema**: `schemas/localization-api.yaml` | **Endpoints**: 12 | **Deep Dive**: [docs/plugins/LOCALIZATION.md](../plugins/LOCALIZATION.md) | **Map**: [docs/maps/LOCALIZATION.md](../maps/LOCALIZATION.md)

The Localization service (L1 AppFoundation) manages structured translation tables that map language × category × key to translated text with optional pronunciation annotations (IPA phonemes for TTS consumption). Categories organize translation entries by domain (items, quests, locations, UI, lexicon codes). The service provides bulk export for client-side caching (Pattern C distribution via Asset bundles), W3C PLS pronunciation lexicon export for TTS engines, and a DI-based key validation interface (`ILocalizationKeyValidator`) that L2+ services optionally use to verify localization keys exist at entity creation time. When the localization plugin is not loaded, validation is silently skipped — higher-layer services are unaware of whether validation is active.

L1 placement is deliberate: localization is infrastructure that any layer needs (L2 game entities have display names, L3 documentation/website need translations, L4 features reference localized text). Like Chat and Permission, it is a thin data service that everything above can reference without hierarchy concerns.

TTS rendering is explicitly **not** a Localization service concern — it is a client-side operation. The service stores pronunciation data; clients consume it via Kokoro (Apache 2.0), Azure Cognitive Services, or any SSML-consuming TTS engine. This follows the established principle that all AI/neural inference stays client-side (per FAQ: WHY-DOESNT-BANNOU-USE-AI-FOR-CONTENT-GENERATION).

## Permission {#permission}

**Version**: 3.0.0 | **Schema**: `schemas/permission-api.yaml` | **Endpoints**: 8 | **Deep Dive**: [docs/plugins/PERMISSION.md](../plugins/PERMISSION.md) | **Map**: [docs/maps/PERMISSION.md](../maps/PERMISSION.md)

Redis-backed RBAC permission system (L1 AppFoundation) for WebSocket services. Manages per-session capability manifests compiled from a multi-dimensional permission matrix (service x state x role -> allowed endpoints). Services register their permission matrices on startup; the Permission service recompiles affected session capabilities whenever roles, states, or registrations change and pushes updates to connected clients via the Connect service's per-session RabbitMQ queues.

## Resource {#resource}

**Version**: 1.0.0 | **Schema**: `schemas/resource-api.yaml` | **Endpoints**: 26 | **Deep Dive**: [docs/plugins/RESOURCE.md](../plugins/RESOURCE.md) | **Map**: [docs/maps/RESOURCE.md](../maps/RESOURCE.md)

Resource reference tracking, lifecycle management, and hierarchical compression service (L1 AppFoundation) for foundational resources. Enables safe deletion of L2 resources by tracking references from higher-layer consumers (L2/L3/L4) without hierarchy violations, coordinates cleanup callbacks with CASCADE/RESTRICT/DETACH policies, and centralizes compression of resources and their dependents into unified MySQL-backed archives. Placed at L1 so all layers can use it; uses opaque string identifiers for resource/source types to avoid coupling to higher layers. Widely integrated: 13 services use generated reference tracking, 11 services register compression callbacks, and 20 services total inject `IResourceClient`.

## Summary

- **Services in layer**: 8
- **Endpoints in layer**: 155

---

*This file is auto-generated. See [TENETS.md](../reference/TENETS.md) for architectural context.*
