# Generated App Foundation (L1) Service Details

> **Source**: `docs/plugins/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

Services in the **App Foundation (L1)** layer.

## Account {#account}

**Version**: 2.0.0 | **Schema**: `schemas/account-api.yaml` | **Endpoints**: 18 | **Deep Dive**: [docs/plugins/ACCOUNT.md](plugins/ACCOUNT.md)

The Account plugin is an internal-only CRUD service (L1 AppFoundation) for managing user accounts. It is never exposed directly to the internet -- all external account operations go through the Auth service, which calls Account via lib-mesh. Handles account creation, lookup (by ID, email, or OAuth provider), updates, soft-deletion, and authentication method management (linking/unlinking OAuth providers). Email is optional -- accounts created via OAuth or Steam may have no email address, identified solely by their linked authentication methods.

## Auth {#auth}

**Version**: 4.0.0 | **Schema**: `schemas/auth-api.yaml` | **Endpoints**: 19 | **Deep Dive**: [docs/plugins/AUTH.md](plugins/AUTH.md)

The Auth plugin is the internet-facing authentication and session management service (L1 AppFoundation). Handles email/password login, OAuth provider integration (Discord, Google, Twitch), Steam session ticket verification, JWT token generation/validation, password reset flows, TOTP-based MFA, and session lifecycle management. It is the primary gateway between external users and the internal service mesh -- after authenticating, clients receive a JWT and a WebSocket connect URL to establish persistent connections via lib-connect.

## Chat {#chat}

**Version**: 1.0.0 | **Schema**: `schemas/chat-api.yaml` | **Endpoints**: 32 | **Deep Dive**: [docs/plugins/CHAT.md](plugins/CHAT.md)

The Chat service (L1 AppFoundation) provides universal typed message channel primitives for real-time communication. Room types determine valid message formats (text, sentiment, emoji, custom-validated payloads), with rooms optionally governed by Contract instances for lifecycle management. Supports ephemeral (Redis TTL) and persistent (MySQL) message storage, participant moderation (kick/ban/mute), rate limiting via atomic Redis counters, typing indicators via Redis sorted set with server-side expiry, and automatic idle room cleanup. Three built-in room types (text, sentiment, emoji) are registered on startup. Internal-only, never internet-facing.

## Connect {#connect}

**Version**: 2.0.0 | **Schema**: `schemas/connect-api.yaml` | **Endpoints**: 7 | **Deep Dive**: [docs/plugins/CONNECT.md](plugins/CONNECT.md)

WebSocket-first edge gateway (L1 AppFoundation) providing zero-copy binary message routing between game clients and backend services. Manages persistent connections with client-salted GUID generation for cross-session security, three connection modes (external, relayed, internal), session shortcuts for game-specific flows, reconnection windows, per-session RabbitMQ subscriptions for server-to-client event delivery, and multi-node broadcast relay via a WebSocket mesh between Connect instances. Internet-facing (the primary client entry point alongside Auth). Registered as Singleton (unusual for Bannou) because it maintains in-memory connection state.

## Contract {#contract}

**Version**: 1.0.0 | **Schema**: `schemas/contract-api.yaml` | **Endpoints**: 30 | **Deep Dive**: [docs/plugins/CONTRACT.md](plugins/CONTRACT.md)

Binding agreement management (L1 AppFoundation) between entities with milestone-based progression, consent flows, and prebound API execution on state transitions. Contracts are reactive: external systems report condition fulfillment via API calls; contracts store state, emit events, and execute callbacks. Templates define structure (party roles, milestones, terms, enforcement mode); instances track consent, sequential progression, and breach handling. Used as infrastructure by lib-quest (quest objectives map to contract milestones) and lib-escrow (asset-backed contracts via guardian locking).

## Permission {#permission}

**Version**: 3.0.0 | **Schema**: `schemas/permission-api.yaml` | **Endpoints**: 8 | **Deep Dive**: [docs/plugins/PERMISSION.md](plugins/PERMISSION.md)

Redis-backed RBAC permission system (L1 AppFoundation) for WebSocket services. Manages per-session capability manifests compiled from a multi-dimensional permission matrix (service x state x role -> allowed endpoints). Services register their permission matrices on startup; the Permission service recompiles affected session capabilities whenever roles, states, or registrations change and pushes updates to connected clients via the Connect service's per-session RabbitMQ queues.

## Resource {#resource}

**Version**: 1.0.0 | **Schema**: `schemas/resource-api.yaml` | **Endpoints**: 17 | **Deep Dive**: [docs/plugins/RESOURCE.md](plugins/RESOURCE.md)

Resource reference tracking, lifecycle management, and hierarchical compression service (L1 AppFoundation) for foundational resources. Enables safe deletion of L2 resources by tracking references from higher-layer consumers (L3/L4) without hierarchy violations, coordinates cleanup callbacks with CASCADE/RESTRICT/DETACH policies, and centralizes compression of resources and their dependents into unified MySQL-backed archives. Placed at L1 so all layers can use it; uses opaque string identifiers for resource/source types to avoid coupling to higher layers. Currently integrated by lib-character (L2) for deletion checks, and by lib-actor, lib-character-encounter, lib-character-history, and lib-character-personality (L4) as reference publishers.

## Summary

- **Services in layer**: 7
- **Endpoints in layer**: 131

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
