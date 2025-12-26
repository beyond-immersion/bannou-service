# Generated State Store Reference

> **Auto-generated**: 2025-12-26 13:15:51
> **Source**: `provisioning/state-stores/*.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists all state store components used in Bannou.

## State Store Components

| Component Name | Backend | Service | Purpose |
|----------------|---------|---------|---------|

## Naming Conventions

| Pattern | Backend | Description |
|---------|---------|-------------|
| `{service}-statestore` | Redis | Service-specific ephemeral state |
| `mysql-{service}-statestore` | MySQL | Persistent queryable data |

## Deployment Flexibility

The state store abstraction means multiple logical state stores can share physical
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
