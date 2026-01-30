# Generated State Store Reference

> **Source**: `schemas/state-stores.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists all state store components used in Bannou.

## State Store Components

| Component Name | Backend | Service | Purpose |
|----------------|---------|---------|---------|
| `account-statestore` | MySQL | Account | Persistent account data |
| `achievement-definition` | Redis | Achievement | Achievement definitions |
| `achievement-progress` | Redis | Achievement | Player achievement progress |
| `actor-assignments` | Redis | Actor | Actor-to-node assignments |
| `actor-instances` | Redis | Actor | Active actor instance registry |
| `actor-pool-nodes` | Redis | Actor | Actor pool node assignments |
| `actor-state` | Redis | Actor | Runtime actor state |
| `actor-templates` | Redis | Actor | Actor template definitions |
| `agent-memories` | Redis | Behavior | Cognition pipeline memory storage (used by lib-behavior's ActorLocalMemoryStore) |
| `analytics-history` | Redis | Analytics | Event buffer and legacy controller history (Redis for backward compatibility) |
| `analytics-history-data` | MySQL | Analytics | Controller possession history for queryable audit trails (MySQL for server-side filtering) |
| `analytics-rating` | Redis | Analytics | Glicko-2 skill ratings |
| `analytics-summary` | Redis | Analytics | Event buffer, session mappings, and resolution caches for analytics ingestion |
| `analytics-summary-data` | MySQL | Analytics | Entity summary data for queryable analytics (MySQL for server-side filtering) |
| `asset-processor-pool` | Redis | Asset | Processor pool node state and indexing |
| `asset-statestore` | Redis | Asset | Asset upload tracking and bundle state |
| `auth-statestore` | Redis | Auth | Session and token state (ephemeral) |
| `behavior-statestore` | Redis | Behavior | Behavior metadata and compiled definitions |
| `character-encounter-statestore` | MySQL | CharacterEncounter | Encounter records and participant perspectives |
| `character-history-statestore` | MySQL | CharacterHistory | Character historical events and backstory |
| `character-personality-statestore` | MySQL | CharacterPersonality | Character personality traits and combat preferences |
| `character-statestore` | MySQL | Character | Persistent character data |
| `connect-statestore` | Redis | Connect | WebSocket session state |
| `contract-statestore` | Redis | Contract | Contract templates, instances, breaches, and indexes |
| `currency-balance-cache` | Redis | Currency | Real-time balance lookups (cached, refreshed on access) |
| `currency-balances` | MySQL | Currency | Currency balance records per wallet |
| `currency-definitions` | MySQL | Currency | Currency type definitions and behavior rules |
| `currency-holds` | MySQL | Currency | Authorization hold records |
| `currency-holds-cache` | Redis | Currency | Authorization hold state for pre-auth scenarios |
| `currency-idempotency` | Redis | Currency | Idempotency key deduplication |
| `currency-transactions` | MySQL | Currency | Immutable transaction history |
| `currency-wallets` | MySQL | Currency | Wallet ownership and status |
| `documentation-statestore` | Redis | Documentation | Documentation content and metadata |
| `escrow-active-validation` | Redis | Escrow | Track active escrows requiring periodic validation |
| `escrow-agreements` | MySQL | Escrow | Main escrow agreement records |
| `escrow-handler-registry` | MySQL | Escrow | Custom asset type handler registrations |
| `escrow-idempotency` | Redis | Escrow | Idempotency key deduplication cache |
| `escrow-party-pending` | Redis | Escrow | Count pending escrows per party for limits |
| `escrow-status-index` | Redis | Escrow | Escrow IDs by status (sorted set for expiration/validation) |
| `escrow-tokens` | Redis | Escrow | Token hash validation (hashed tokens to escrow/party info) |
| `game-service-statestore` | MySQL | GameService | Game service registry |
| `game-session-statestore` | MySQL | GameSession | Game session state and history |
| `inventory-container-cache` | Redis | Inventory | Container state and item list cache |
| `inventory-container-store` | MySQL | Inventory | Container definitions (persistent) |
| `inventory-lock` | Redis | Inventory | Distributed locks for concurrent modifications |
| `item-instance-cache` | Redis | Item | Hot item instance data for active gameplay |
| `item-instance-store` | MySQL | Item | Item instances (persistent, realm-partitioned) |
| `item-lock` | Redis | Item | Distributed locks for item instance modifications |
| `item-template-cache` | Redis | Item | Template lookup cache (global, aggressive caching) |
| `item-template-store` | MySQL | Item | Item template definitions (persistent, queryable) |
| `leaderboard-definition` | Redis | Leaderboard | Leaderboard definitions and metadata |
| `leaderboard-ranking` | Redis | Leaderboard | Real-time ranking data (sorted sets) |
| `leaderboard-season` | MySQL | Leaderboard | Season history and archives |
| `location-statestore` | MySQL | Location | Location hierarchy and metadata |
| `mapping-statestore` | Redis | Mapping | Spatial map data and channels |
| `matchmaking-statestore` | Redis | Matchmaking | Matchmaking queue and ticket state |
| `mesh-appid-index` | Redis | Mesh | App-ID to instance-ID mapping index |
| `mesh-endpoints` | Redis | Mesh | Service endpoint registration and health status |
| `mesh-global-index` | Redis | Mesh | Global endpoint index for discovery |
| `messaging-external-subs` | Redis | Messaging | External subscription recovery data |
| `music-compositions` | Redis | Music | Cached generated compositions |
| `music-styles` | MySQL | Music | Style definitions (celtic, jazz, baroque, etc.) |
| `orchestrator-config` | Redis | Orchestrator | Configuration version and metadata |
| `orchestrator-heartbeats` | Redis | Orchestrator | Service heartbeat tracking |
| `orchestrator-routings` | Redis | Orchestrator | Service-to-app-id routing tables |
| `orchestrator-statestore` | Redis | Orchestrator | Primary orchestrator state |
| `permission-statestore` | Redis | Permission | Permission cache and session capabilities |
| `realm-history-statestore` | MySQL | RealmHistory | Realm historical events and lore |
| `realm-statestore` | MySQL | Realm | Realm definitions and configuration |
| `relationship-statestore` | MySQL | Relationship | Entity relationships |
| `relationship-type-statestore` | MySQL | RelationshipType | Relationship type definitions |
| `save-load-cache` | Redis | SaveLoad | Recently accessed save data cache |
| `save-load-pending` | Redis | SaveLoad | Pending save operations |
| `save-load-schemas` | MySQL | SaveLoad | Registered save data schemas |
| `save-load-slots` | MySQL | SaveLoad | Save slot metadata and ownership |
| `save-load-versions` | MySQL | SaveLoad | Save version history |
| `scene-statestore` | MySQL | Scene | Hierarchical scene composition storage |
| `species-statestore` | MySQL | Species | Species definitions |
| `subscription-statestore` | MySQL | Subscription | User subscriptions to game services |
| `test-search-statestore` | Redis | State | Test store with RedisSearch enabled |
| `voice-statestore` | Redis | Voice | Voice room and peer state |

**Total**: 81 stores (49 Redis, 32 MySQL)

## Naming Conventions

| Pattern | Backend | Description |
|---------|---------|-------------|
| `{service}-statestore` | Redis/MySQL | Service-specific state storage |
| `{service}-{feature}` | Redis | Feature-specific ephemeral state |
| `mysql-{service}-statestore` | MySQL | Legacy pattern (prefer `{service}-statestore`) |

## Backend Selection Guide

| Use Case | Backend | Rationale |
|----------|---------|-----------|
| Session data, tokens | Redis | Fast, ephemeral, supports TTL |
| Caches, rankings | Redis | In-memory performance, sorted sets |
| Persistent entities | MySQL | Durable, queryable, relational |
| Full-text search | Redis + Search | RedisSearch module for indexing |

## Deployment Flexibility

The state store abstraction means multiple logical state stores can share physical
Redis/MySQL instances in simple deployments, while production deployments can map
to dedicated infrastructure without code changes.

### Example: Development (Shared Infrastructure)

```yaml
# All Redis state stores point to same instance
auth-statestore:     bannou-redis:6379
connect-statestore:  bannou-redis:6379
permission-statestore: bannou-redis:6379
```

### Example: Production (Dedicated Infrastructure)

```yaml
# Each service can have its own infrastructure
auth-statestore:     auth-redis-cluster.prod:6379
connect-statestore:  connect-redis-cluster.prod:6379
permission-statestore: permission-redis.prod:6379
```

## Generated Code

State store definitions are generated to `bannou-service/Generated/StateStoreDefinitions.cs`,
providing:

- **Name constants**: `StateStoreDefinitions.Auth`, `StateStoreDefinitions.Account`, etc.
- **Configurations**: `StateStoreDefinitions.Configurations` dictionary
- **Metadata**: `StateStoreDefinitions.Metadata` for tooling

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
