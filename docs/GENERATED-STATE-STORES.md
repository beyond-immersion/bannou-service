# Generated State Store Reference

> **Source**: `schemas/state-stores.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists all state store components used in Bannou.

## State Store Components

| Component Name | Backend | Service | Purpose |
|----------------|---------|---------|---------|
| `account-lock` | Redis | Account | Distributed locks for account email uniqueness during creation |
| `account-statestore` | MySQL | Account | Persistent account data |
| `achievement-definition` | Redis | Achievement | Achievement definitions |
| `achievement-progress` | Redis | Achievement | Player achievement progress |
| `actor-assignments` | Redis | Actor | Actor-to-node assignments |
| `actor-instances` | Redis | Actor | Active actor instance registry |
| `actor-pool-nodes` | Redis | Actor | Actor pool node assignments |
| `actor-state` | Redis | Actor | Runtime actor state |
| `actor-templates` | Redis | Actor | Actor template definitions |
| `agent-memories` | Redis | Behavior | Cognition pipeline memory storage (used by lib-behavior's ActorLocalMemoryStore) |
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
| `character-lock` | Redis | Character | Distributed locks for character update and compression operations |
| `character-personality-statestore` | MySQL | CharacterPersonality | Character personality traits and combat preferences |
| `character-statestore` | MySQL | Character | Persistent character data |
| `collection-area-music-configs` | MySQL | Collection | Area-to-theme mappings for dynamic music track selection |
| `collection-cache` | Redis | Collection | Collection state cache (unlocked entries per collection) |
| `collection-entry-templates` | MySQL | Collection | Entry template definitions per collection type and game service |
| `collection-instances` | MySQL | Collection | Per-owner collection containers linking entities to collection types |
| `collection-lock` | Redis | Collection | Distributed locks for collection mutations and grant operations |
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
| `edge-revocation-statestore` | Redis | Auth | Edge revocation tracking for CDN/firewall layer blocking |
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
| `license-board-cache` | Redis | License | Board state cache (unlocked license positions per board) |
| `license-board-templates` | MySQL | License | Board template definitions with grid layout and contract references |
| `license-boards` | MySQL | License | Character board instances linking characters to board templates |
| `license-definitions` | MySQL | License | License definitions (nodes) per board template with grid positions |
| `license-lock` | Redis | License | Distributed locks for board mutations and unlock operations |
| `location-cache` | Redis | Location | Location lookup cache for frequently-accessed locations |
| `location-lock` | Redis | Location | Distributed locks for concurrent index modifications |
| `location-statestore` | MySQL | Location | Location hierarchy and metadata |
| `mapping-statestore` | Redis | Mapping | Spatial map data and channels |
| `matchmaking-statestore` | Redis | Matchmaking | Matchmaking queue and ticket state |
| `mesh-appid-index` | Redis | Mesh | App-ID to instance-ID mapping index |
| `mesh-circuit-breaker` | Redis | Mesh | Distributed circuit breaker state for cross-instance failure tracking |
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
| `quest-character-index` | Redis | Quest | Character to active quest instance mapping |
| `quest-cooldown` | Redis | Quest | Per-character quest cooldown tracking |
| `quest-definition-cache` | Redis | Quest | Quest definition read-through cache |
| `quest-definition-statestore` | MySQL | Quest | Quest definitions with contract template IDs and metadata |
| `quest-idempotency` | Redis | Quest | Idempotency keys for accept/complete operations |
| `quest-instance-statestore` | MySQL | Quest | Quest instances with status and party information |
| `quest-objective-progress` | Redis | Quest | Real-time objective progress tracking |
| `realm-history-statestore` | MySQL | RealmHistory | Realm historical events and lore |
| `realm-statestore` | MySQL | Realm | Realm definitions and configuration |
| `relationship-lock` | Redis | Relationship | Distributed locks for composite uniqueness and index modifications |
| `relationship-statestore` | MySQL | Relationship | Entity relationships |
| `relationship-type-statestore` | MySQL | Relationship | Relationship type definitions |
| `resource-archives` | MySQL | Resource | Compressed archive bundles (durable storage for long-term archival) |
| `resource-cleanup` | Redis | Resource | Cleanup callback definitions per resource type |
| `resource-compress` | Redis | Resource | Compression callback definitions and callback index sets |
| `resource-grace` | Redis | Resource | Grace period timestamps for resources with zero references |
| `resource-refcounts` | Redis | Resource | Reference counts and source tracking per resource |
| `resource-snapshots` | Redis | Resource | Ephemeral snapshots of living resources (TTL-based auto-expiry for storyline/actor consumption) |
| `save-load-cache` | Redis | SaveLoad | Recently accessed save data cache |
| `save-load-pending` | Redis | SaveLoad | Pending save operations |
| `save-load-schemas` | MySQL | SaveLoad | Registered save data schemas |
| `save-load-slots` | MySQL | SaveLoad | Save slot metadata and ownership |
| `save-load-versions` | MySQL | SaveLoad | Save version history |
| `scene-statestore` | MySQL | Scene | Hierarchical scene composition storage |
| `seed-bonds-statestore` | MySQL | Seed | Bond records between seeds (durable) |
| `seed-capabilities-cache` | Redis | Seed | Computed capability manifests (cached, frequently read) |
| `seed-growth-statestore` | MySQL | Seed | Growth domain records per seed (durable, queryable) |
| `seed-lock` | Redis | Seed | Distributed locks for seed modifications |
| `seed-statestore` | MySQL | Seed | Seed entity records (durable, queryable by owner/type) |
| `seed-type-definitions-statestore` | MySQL | Seed | Registered seed type definitions (durable, admin-managed) |
| `species-statestore` | MySQL | Species | Species definitions |
| `storyline-plan-index` | Redis | Storyline | Plan index by realm for list queries |
| `storyline-plans` | Redis | Storyline | Cached composed storyline plans (ephemeral, TTL from config) |
| `storyline-scenario-active` | Redis | Storyline | Active scenario tracking per character (set membership) |
| `storyline-scenario-cache` | Redis | Storyline | Scenario definition read-through cache (TTL from config) |
| `storyline-scenario-cooldown` | Redis | Storyline | Per-character scenario cooldowns with TTL-based auto-expiry |
| `storyline-scenario-definitions` | MySQL | Storyline | Durable scenario template definitions with conditions and mutations |
| `storyline-scenario-executions` | MySQL | Storyline | Scenario execution history with outcome tracking |
| `storyline-scenario-idempotency` | Redis | Storyline | Scenario trigger idempotency keys for deduplication |
| `subscription-statestore` | MySQL | Subscription | User subscriptions to game services |
| `test-search-statestore` | Redis | State | Test store with RedisSearch enabled |
| `voice-statestore` | Redis | Voice | Voice room and peer state |

**Total**: 124 stores (77 Redis, 47 MySQL)

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
