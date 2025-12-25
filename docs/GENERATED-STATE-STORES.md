# Generated State Store Reference

> **Auto-generated**: 2025-12-25 00:17:55
> **Source**: `provisioning/dapr/components/*.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists all Dapr state store components used in Bannou.

## State Store Components

| Component Name | Backend | Service | Purpose |
|----------------|---------|---------|---------|
| `accounts-statestore` | MySQL | Accounts | Persistent queryable data |
| `asset-statestore` | Redis | Asset | Service-specific state |
| `auth-statestore` | Redis | Auth | Session/token state (ephemeral) |
| `character-statestore` | MySQL | Character | Persistent queryable data |
| `connect-statestore` | Redis | Connect | WebSocket session state |
| `documentation-statestore` | Redis | Documentation | Service-specific state |
| `game-session-statestore` | Redis | Game-session | Active game session state |
| `location-statestore` | MySQL | Location | Persistent queryable data |
| `permissions-store` | Redis | Permissions | Permission cache and session capabilities |
| `realm-statestore` | MySQL | Realm | Persistent queryable data |
| `relationship-statestore` | MySQL | Relationship | Persistent queryable data |
| `relationship-type-statestore` | MySQL | Relationship-type | Persistent queryable data |
| `servicedata-statestore` | MySQL | Servicedata | Persistent queryable data |
| `species-statestore` | MySQL | Species | Persistent queryable data |
| `statestore` | Redis | Statestore | Service-specific state |
| `subscriptions-statestore` | MySQL | Subscriptions | Persistent queryable data |
| `voice-statestore` | Redis | Voice | Service-specific state |

## Naming Conventions

| Pattern | Backend | Description |
|---------|---------|-------------|
| `{service}-statestore` | Redis | Service-specific ephemeral state |
| `mysql-{service}-statestore` | MySQL | Persistent queryable data |

## Deployment Flexibility

Dapr component abstraction means multiple logical state stores can share physical
Redis/MySQL instances in simple deployments, while production deployments can map
to dedicated infrastructure without code changes.

### Example: Development (Shared Infrastructure)

```yaml
# All Redis state stores point to same instance
auth-statestore:     bannou-redis:6379
connect-statestore:  bannou-redis:6379
permissions-statestore: bannou-redis:6379
```

### Example: Production (Dedicated Infrastructure)

```yaml
# Each service can have its own infrastructure
auth-statestore:     auth-redis-cluster.prod:6379
connect-statestore:  connect-redis-cluster.prod:6379
permissions-statestore: permissions-redis.prod:6379
```

---

*This file is auto-generated. See [TENETS.md](../TENETS.md) for architectural context.*
