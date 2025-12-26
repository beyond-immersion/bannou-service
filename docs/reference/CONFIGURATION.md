# Configuration Reference

This document lists all environment variables and configuration options for Bannou services.

## Service Selection

### Global Service Control

| Variable | Default | Description |
|----------|---------|-------------|
| `SERVICES_ENABLED` | `true` | Enable all services by default |

### Individual Services

| Variable | Default | Description |
|----------|---------|-------------|
| `ACCOUNTS_SERVICE_ENABLED` | from global | Accounts service |
| `AUTH_SERVICE_ENABLED` | from global | Authentication service |
| `BEHAVIOR_SERVICE_ENABLED` | from global | NPC behavior service |
| `CHARACTER_SERVICE_ENABLED` | from global | Character management |
| `CONNECT_SERVICE_ENABLED` | from global | WebSocket gateway |
| `GAME_SESSION_SERVICE_ENABLED` | from global | Game session management |
| `ORCHESTRATOR_SERVICE_ENABLED` | from global | Deployment orchestrator |
| `PERMISSIONS_SERVICE_ENABLED` | from global | Permission management |
| `SERVICEDATA_SERVICE_ENABLED` | from global | Service registry |
| `SUBSCRIPTIONS_SERVICE_ENABLED` | from global | Subscription management |
| `TESTING_SERVICE_ENABLED` | from global | Testing service |
| `WEBSITE_SERVICE_ENABLED` | from global | Web frontend |

**Example**:
```bash
# Disable all, enable specific services
SERVICES_ENABLED=false
AUTH_SERVICE_ENABLED=true
ACCOUNTS_SERVICE_ENABLED=true
CONNECT_SERVICE_ENABLED=true
```

## JWT Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `BANNOU_JWTSECRET` | required | Secret key for JWT signing |
| `BANNOU_JWTISSUER` | `bannou-auth` | JWT issuer claim |
| `BANNOU_JWTAUDIENCE` | `bannou-api` | JWT audience claim |
| `BANNOU_JWTEXPIRATION_MINUTES` | `60` | Token expiration time |

**Example**:
```bash
BANNOU_JWTSECRET=your-very-secure-secret-key-here
BANNOU_JWTISSUER=bannou-auth-production
BANNOU_JWTAUDIENCE=bannou-api-production
BANNOU_JWTEXPIRATION_MINUTES=120
```

## WebSocket Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `BANNOU_CONNECTURL` | | WebSocket URL returned to clients |

**Example**:
```bash
BANNOU_CONNECTURL=wss://api.example.com/connect
```

## OAuth Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `BANNOU_MOCKPROVIDERS` | `false` | Use mock OAuth for testing |
| `BANNOU_ADMINEMAILDOMAIN` | | Email domain for auto-admin |

### Provider-Specific

**Discord**:
| Variable | Description |
|----------|-------------|
| `BANNOU_DISCORD_CLIENT_ID` | Discord app client ID |
| `BANNOU_DISCORD_CLIENT_SECRET` | Discord app client secret |
| `BANNOU_DISCORD_REDIRECT_URI` | OAuth redirect URI |

**Google**:
| Variable | Description |
|----------|-------------|
| `BANNOU_GOOGLE_CLIENT_ID` | Google OAuth client ID |
| `BANNOU_GOOGLE_CLIENT_SECRET` | Google OAuth client secret |
| `BANNOU_GOOGLE_REDIRECT_URI` | OAuth redirect URI |

**Twitch**:
| Variable | Description |
|----------|-------------|
| `BANNOU_TWITCH_CLIENT_ID` | Twitch app client ID |
| `BANNOU_TWITCH_CLIENT_SECRET` | Twitch app client secret |
| `BANNOU_TWITCH_REDIRECT_URI` | OAuth redirect URI |

**Steam**:
| Variable | Description |
|----------|-------------|
| `BANNOU_STEAM_WEB_API_KEY` | Steam Web API key |

## HTTP Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `HTTP_Web_Host_Port` | `5012` | HTTP listen port |
| `HTTPS_Web_Host_Port` | `5013` | HTTPS listen port |
| `BANNOU_HTTP_Web_Host_Port` | | Prefixed port override |
| `BANNOU_HTTPS_Web_Host_Port` | | Prefixed port override |

## Database Configuration

| Variable | Description |
|----------|-------------|
| `ACCOUNT_DB_HOST` | MySQL host for accounts |
| `ACCOUNT_DB_PORT` | MySQL port |
| `ACCOUNT_DB_USER` | MySQL username |
| `ACCOUNT_DB_PASSWORD` | MySQL password |
| `ACCOUNT_DB_NAME` | Database name |

## Redis Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `REDIS_HOST` | `redis` | Redis hostname |
| `REDIS_PORT` | `6379` | Redis port |
| `REDIS_PASSWORD` | | Redis password (optional) |

## RabbitMQ Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `RABBITMQ_HOST` | `rabbitmq` | RabbitMQ hostname |
| `RABBITMQ_PORT` | `5672` | RabbitMQ AMQP port |
| `RABBITMQ_USER` | `guest` | RabbitMQ username |
| `RABBITMQ_PASSWORD` | `guest` | RabbitMQ password |

## Docker Compose Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `SERVICE_DOMAIN` | | Service domain for compose naming |
| `COMPOSE_PROJECT_NAME` | | Docker compose project name |

## OpenResty/Ingress Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `OPENRESTY_HTTP_PORT` | `80` | HTTP ingress port |
| `OPENRESTY_HTTPS_PORT` | `443` | HTTPS ingress port |

## State Stores

State stores are configured via lib-state:

| Store Name | Type | Purpose |
|------------|------|---------|
| `accounts-statestore` | MySQL | Account persistence |
| `auth-statestore` | Redis | Session management |
| `connect-statestore` | Redis | WebSocket session state |
| `behavior-statestore` | Redis | Behavior cache |
| `game-session-statestore` | Redis | Game session state |

## Pub/Sub (RabbitMQ via MassTransit)

| Component | Broker | Topics |
|-----------|--------|--------|
| `bannou-pubsub` | RabbitMQ | All service events |

Common topics:
- `account-created`, `account-updated`, `account-deleted`
- `session-invalidated`
- `capabilities-updated`
- `full-service-mappings`

## Service-Specific Configuration

Each service can have custom configuration defined in its schema:

```yaml
# In schema x-service-configuration section
x-service-configuration:
  properties:
    MaxRetries:
      type: integer
      default: 3
```

Environment variable pattern:
```bash
BANNOU_{SERVICE}_{PropertyName}=value
```

Example:
```bash
BANNOU_BEHAVIOR_MaxConcurrentRequests=100
BANNOU_BEHAVIOR_EnableCaching=true
```

## Loading Priority

Configuration values are loaded in order (later overrides earlier):

1. Default values in code
2. `appsettings.json` (if present)
3. `.env` file in repository root
4. `.env` file in parent directory
5. Environment variables

## .env File Format

```bash
# Comments start with #
VARIABLE_NAME=value

# Multi-word values don't need quotes
BANNOU_JWTSECRET=this is a long secret key

# Empty values are valid
OPTIONAL_VAR=
```

## Common Configuration Patterns

### Development

```bash
# .env for local development
SERVICES_ENABLED=true
BANNOU_JWTSECRET=dev-secret-key
BANNOU_MOCKPROVIDERS=true
```

### CI Testing

```bash
# .env.ci.http for HTTP tests
SERVICES_ENABLED=false
AUTH_SERVICE_ENABLED=true
ACCOUNTS_SERVICE_ENABLED=true
TESTING_SERVICE_ENABLED=true
BANNOU_MOCKPROVIDERS=true
BANNOU_ADMINEMAILDOMAIN=@admin.test.local
```

### Production

```bash
# Production configuration
SERVICES_ENABLED=false
AUTH_SERVICE_ENABLED=true
ACCOUNTS_SERVICE_ENABLED=true
CONNECT_SERVICE_ENABLED=true

BANNOU_JWTSECRET=production-secret-from-secrets-manager
BANNOU_JWTISSUER=production-bannou
BANNOU_CONNECTURL=wss://api.production.com/connect

BANNOU_DISCORD_CLIENT_ID=real-discord-id
BANNOU_DISCORD_CLIENT_SECRET=real-discord-secret
# ... other OAuth providers
```

## Next Steps

- [Deployment Guide](../guides/DEPLOYMENT.md) - Using configuration in deployments
- [Plugin Development](../guides/PLUGIN_DEVELOPMENT.md) - Service-specific configuration
- [Development Rules](TENETS.md) - Configuration best practices
