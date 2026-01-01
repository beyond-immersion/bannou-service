# Generated Configuration Reference

> **Auto-generated**: 2026-01-01 06:26:06
> **Source**: `schemas/*-configuration.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists all configuration options defined in Bannou's configuration schemas.

## Configuration by Service

### Accounts

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `ACCOUNTS_ADMIN_EMAILS` | string | **REQUIRED** | Comma-separated list of admin email addresses |
| `ACCOUNTS_ADMIN_EMAIL_DOMAIN` | string | **REQUIRED** | Email domain that grants admin access (e.g., "@company.com") |

### Asset

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `ASSET_BUNDLE_COMPRESSION_DEFAULT` | string | `lz4` | Default compression for bundles (lz4, lzma, none) |
| `ASSET_DOWNLOAD_TOKEN_TTL_SECONDS` | int | `900` | TTL for download URLs (can be shorter than upload) |
| `ASSET_LARGE_FILE_THRESHOLD_MB` | int | `50` | File size threshold for delegating to processing pool |
| `ASSET_MAX_UPLOAD_SIZE_MB` | int | `500` | Maximum upload size in megabytes |
| `ASSET_MINIO_WEBHOOK_SECRET` | string | **REQUIRED** | Secret for validating MinIO webhook requests |
| `ASSET_MULTIPART_PART_SIZE_MB` | int | `16` | Size of each part in multipart uploads in megabytes |
| `ASSET_MULTIPART_THRESHOLD_MB` | int | `50` | File size threshold for multipart uploads in megabytes |
| `ASSET_PROCESSING_MODE` | string | `both` | Service mode (api, worker, both) |
| `ASSET_PROCESSING_POOL_TYPE` | string | `asset-processor` | Processing pool identifier for orchestrator |
| `ASSET_STORAGE_ACCESS_KEY` | string | **REQUIRED** | Storage access key/username |
| `ASSET_STORAGE_BUCKET` | string | `bannou-assets` | Primary bucket/container name for assets |
| `ASSET_STORAGE_ENDPOINT` | string | `http://minio:9000` | Storage endpoint URL (MinIO/S3 compatible) |
| `ASSET_STORAGE_FORCE_PATH_STYLE` | bool | `true` | Force path-style URLs (required for MinIO) |
| `ASSET_STORAGE_PROVIDER` | string | `minio` | Storage backend type (minio, s3, r2, azure, filesystem) |
| `ASSET_STORAGE_REGION` | string | `us-east-1` | Storage region (for S3/R2) |
| `ASSET_STORAGE_SECRET_KEY` | string | **REQUIRED** | Storage secret key/password |
| `ASSET_STORAGE_USE_SSL` | bool | `false` | Use SSL/TLS for storage connections |
| `ASSET_TOKEN_TTL_SECONDS` | int | `3600` | TTL for pre-signed upload/download URLs in seconds |
| `ASSET_WORKER_POOL` | string | **REQUIRED** | Worker pool identifier when running in worker mode |
| `ASSET_ZIP_CACHE_TTL_HOURS` | int | `24` | TTL for cached ZIP conversions in hours |

### Auth

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `AUTH_CONNECT_URL` | string | **REQUIRED** | URL to the Connect service for WebSocket connections. REQUIR... |
| `AUTH_DISCORD_CLIENT_ID` | string | **REQUIRED** | Discord OAuth client ID |
| `AUTH_DISCORD_CLIENT_SECRET` | string | **REQUIRED** | Discord OAuth client secret |
| `AUTH_DISCORD_REDIRECT_URI` | string | **REQUIRED** | Discord OAuth redirect URI |
| `AUTH_GOOGLE_CLIENT_ID` | string | **REQUIRED** | Google OAuth client ID |
| `AUTH_GOOGLE_CLIENT_SECRET` | string | **REQUIRED** | Google OAuth client secret |
| `AUTH_GOOGLE_REDIRECT_URI` | string | **REQUIRED** | Google OAuth redirect URI |
| `AUTH_JWT_AUDIENCE` | string | `bannou-api` | JWT token audience |
| `AUTH_JWT_EXPIRATION_MINUTES` | int | `60` | JWT token expiration time in minutes |
| `AUTH_JWT_ISSUER` | string | `bannou-auth` | JWT token issuer |
| `AUTH_JWT_SECRET` | string | **REQUIRED** | Secret key for JWT token signing |
| `AUTH_MOCK_DISCORD_ID` | string | `mock-discord-123456` | Mock Discord user ID for testing |
| `AUTH_MOCK_GOOGLE_ID` | string | `mock-google-123456` | Mock Google user ID for testing |
| `AUTH_MOCK_PROVIDERS` | bool | `false` | Enable mock OAuth providers for testing |
| `AUTH_MOCK_STEAM_ID` | string | `76561198000000000` | Mock Steam user ID for testing |
| `AUTH_PASSWORD_RESET_BASE_URL` | string | **REQUIRED** | Base URL for password reset page |
| `AUTH_PASSWORD_RESET_TOKEN_TTL_MINUTES` | int | `30` | Password reset token expiration time in minutes |
| `AUTH_STEAM_API_KEY` | string | **REQUIRED** | Steam Web API key for session ticket validation |
| `AUTH_STEAM_APP_ID` | string | **REQUIRED** | Steam application ID |
| `AUTH_TWITCH_CLIENT_ID` | string | **REQUIRED** | Twitch OAuth client ID |
| `AUTH_TWITCH_CLIENT_SECRET` | string | **REQUIRED** | Twitch OAuth client secret |
| `AUTH_TWITCH_REDIRECT_URI` | string | **REQUIRED** | Twitch OAuth redirect URI |

### Behavior

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `BEHAVIOR_ENABLED` | bool | `true` | Enable/disable Behavior service |

### Character

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `CHARACTER_DEFAULT_PAGE_SIZE` | int | `20` | Default page size when not specified |
| `CHARACTER_MAX_PAGE_SIZE` | int | `100` | Maximum page size for list queries |
| `CHARACTER_RETENTION_DAYS` | int | `90` | Number of days to retain deleted characters before permanent... |

### Connect

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `CONNECT_AUTHENTICATEDSERVICES` | string[] | `['accounts', 'behavior', 'permissions', 'gamesession']` | Additional services available to authenticated connections |
| `CONNECT_BINARYPROTOCOLVERSION` | string | `2.0` | Binary protocol version identifier |
| `CONNECT_BUFFERSIZE` | int | `65536` | Size of message buffers in bytes |
| `CONNECT_CONNECTIONTIMEOUTSECONDS` | int | `300` | WebSocket connection timeout in seconds |
| `CONNECT_DEFAULTSERVICES` | string[] | `['auth', 'website']` | Services available to unauthenticated connections |
| `CONNECT_ENABLECLIENTTOCLIENTROUTING` | bool | `true` | Enable routing messages between WebSocket clients |
| `CONNECT_HEARTBEATINTERVALSECONDS` | int | `30` | Interval between heartbeat messages |
| `CONNECT_JWTPUBLICKEY` | string | **REQUIRED** | RSA public key for JWT validation (PEM format) |
| `CONNECT_MAXCONCURRENTCONNECTIONS` | int | `10000` | Maximum number of concurrent WebSocket connections |
| `CONNECT_MAXMESSAGESPERMINUTE` | int | `1000` | Rate limit for messages per minute per client |
| `CONNECT_MESSAGEQUEUESIZE` | int | `1000` | Maximum number of queued messages per connection |
| `CONNECT_RABBITMQ_CONNECTION_STRING` | string | **REQUIRED** | RabbitMQ connection string for client event subscriptions (T... |
| `CONNECT_RATELIMITWINDOWMINUTES` | int | `1` | Rate limit window in minutes |
| `CONNECT_SERVER_SALT` | string | **REQUIRED** | Server salt for client GUID generation. Must be shared acros... |
| `CONNECT_URL` | string | **REQUIRED** | WebSocket URL returned to clients for reconnection |

### Documentation

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `DOCUMENTATION_AI_EMBEDDINGS_MODEL` | string | `` | Model for generating embeddings (when AI enabled) |
| `DOCUMENTATION_AI_ENHANCEMENTS_ENABLED` | bool | `false` | Enable AI-powered semantic search (future feature) |
| `DOCUMENTATION_GIT_CLONE_TIMEOUT_SECONDS` | int | `300` | Clone/pull operation timeout in seconds |
| `DOCUMENTATION_GIT_STORAGE_CLEANUP_HOURS` | int | `24` | Hours before inactive repos are cleaned up |
| `DOCUMENTATION_GIT_STORAGE_PATH` | string | `/tmp/bannou-git-repos` | Local path for cloned git repositories |
| `DOCUMENTATION_MAX_CONCURRENT_SYNCS` | int | `3` | Maximum concurrent sync operations |
| `DOCUMENTATION_MAX_CONTENT_SIZE_BYTES` | int | `524288` | Maximum document content size in bytes (500KB default) |
| `DOCUMENTATION_MAX_DOCUMENTS_PER_SYNC` | int | `1000` | Maximum documents per sync operation |
| `DOCUMENTATION_MAX_IMPORT_DOCUMENTS` | int | `0` | Maximum documents per import (0 = unlimited) |
| `DOCUMENTATION_MAX_SEARCH_RESULTS` | int | `20` | Maximum search results to return |
| `DOCUMENTATION_MIN_RELEVANCE_SCORE` | double | `0.3` | Default minimum relevance score for search results |
| `DOCUMENTATION_SEARCH_CACHE_TTL_SECONDS` | int | `300` | TTL for search result caching |
| `DOCUMENTATION_SEARCH_INDEX_REBUILD_ON_STARTUP` | bool | `true` | Whether to rebuild search index on service startup |
| `DOCUMENTATION_SESSION_TTL_SECONDS` | int | `86400` | TTL for informal session tracking (24 hours default) |
| `DOCUMENTATION_SYNC_SCHEDULER_CHECK_INTERVAL_MINUTES` | int | `5` | How often to check for repos needing sync |
| `DOCUMENTATION_SYNC_SCHEDULER_ENABLED` | bool | `true` | Enable background sync scheduler |
| `DOCUMENTATION_TRASHCAN_TTL_DAYS` | int | `7` | Days before trashcan items are auto-purged |
| `DOCUMENTATION_VOICE_SUMMARY_MAX_LENGTH` | int | `200` | Maximum characters for voice summaries |

### Game Session

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `GAME-SESSION_DEFAULTSESSIONTIMEOUTSECONDS` | int | `7200` | Default session timeout in seconds |
| `GAME-SESSION_ENABLED` | bool | `true` | Enable/disable Game Session service |
| `GAME-SESSION_MAXPLAYERSPERSESSION` | int | `16` | Maximum players allowed per session |
| `GAME-SESSION_SERVERSALT` | string | **REQUIRED** | Server salt for GUID generation. If not set, generates rando... |

### Location

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `LOCATION_ENABLED` | bool | `true` | Enable/disable Location service |

### Mesh

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `MESH_CIRCUIT_BREAKER_ENABLED` | bool | `true` | Whether to enable circuit breaker for failed endpoints |
| `MESH_CIRCUIT_BREAKER_RESET_SECONDS` | int | `30` | Seconds before attempting to close circuit |
| `MESH_CIRCUIT_BREAKER_THRESHOLD` | int | `5` | Number of consecutive failures before opening circuit |
| `MESH_DEFAULT_APP_ID` | string | `bannou` | Default app-id when no service mapping exists (omnipotent ro... |
| `MESH_DEFAULT_LOAD_BALANCER` | string | `RoundRobin` | Default load balancing algorithm (RoundRobin, LeastConnectio... |
| `MESH_DEGRADATION_THRESHOLD_SECONDS` | int | `60` | Time without heartbeat before marking endpoint as degraded |
| `MESH_ENABLE_DETAILED_LOGGING` | bool | `false` | Whether to log detailed routing decisions |
| `MESH_ENABLE_SERVICE_MAPPING_SYNC` | bool | `true` | Whether to subscribe to FullServiceMappingsEvent for routing... |
| `MESH_ENDPOINT_TTL_SECONDS` | int | `90` | TTL for endpoint registration (should be > 2x heartbeat inte... |
| `MESH_HEALTH_CHECK_ENABLED` | bool | `false` | Whether to perform active health checks on endpoints |
| `MESH_HEALTH_CHECK_INTERVAL_SECONDS` | int | `60` | Interval between active health checks |
| `MESH_HEALTH_CHECK_TIMEOUT_SECONDS` | int | `5` | Timeout for health check requests |
| `MESH_HEARTBEAT_INTERVAL_SECONDS` | int | `30` | Recommended interval between heartbeats |
| `MESH_LOAD_THRESHOLD_PERCENT` | int | `80` | Load percentage above which an endpoint is considered high-l... |
| `MESH_MAX_RETRIES` | int | `3` | Maximum retry attempts for failed service calls |
| `MESH_METRICS_ENABLED` | bool | `true` | Whether to collect routing metrics |
| `MESH_REDIS_CONNECTION_STRING` | string | **REQUIRED** | Redis connection string for service registry storage. REQUIR... |
| `MESH_REDIS_CONNECTION_TIMEOUT_SECONDS` | int | `60` | Total timeout in seconds for Redis connection establishment ... |
| `MESH_REDIS_CONNECT_RETRY_COUNT` | int | `5` | Maximum number of Redis connection retry attempts |
| `MESH_REDIS_KEY_PREFIX` | string | `mesh:` | Prefix for all mesh-related Redis keys |
| `MESH_REDIS_SYNC_TIMEOUT_MS` | int | `5000` | Timeout in milliseconds for synchronous Redis operations |
| `MESH_RETRY_DELAY_MILLISECONDS` | int | `100` | Initial delay between retries (doubles on each retry) |
| `MESH_USE_LOCAL_ROUTING` | bool | `false` | Use local-only routing instead of Redis. All calls route to ... |

### Messaging

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `MESSAGING_CONNECTION_RETRY_COUNT` | int | `5` | Number of connection retry attempts |
| `MESSAGING_CONNECTION_RETRY_DELAY_MS` | int | `1000` | Delay between connection retry attempts in milliseconds |
| `MESSAGING_CONNECTION_TIMEOUT_SECONDS` | int | `60` | Timeout in seconds for establishing RabbitMQ connection |
| `MESSAGING_DEAD_LETTER_EXCHANGE` | string | `bannou-dlx` | Dead letter exchange name for failed messages |
| `MESSAGING_DEFAULT_AUTO_ACK` | bool | `false` | Default auto-acknowledge setting for subscriptions |
| `MESSAGING_DEFAULT_EXCHANGE` | string | `bannou` | Default exchange name for publishing |
| `MESSAGING_DEFAULT_PREFETCH_COUNT` | int | `10` | Default prefetch count for subscriptions |
| `MESSAGING_ENABLE_CONFIRMS` | bool | `true` | Enable RabbitMQ publisher confirms for reliability |
| `MESSAGING_ENABLE_METRICS` | bool | `true` | Enable message bus metrics collection |
| `MESSAGING_ENABLE_TRACING` | bool | `true` | Enable distributed tracing for messages |
| `MESSAGING_RABBITMQ_HOST` | string | `rabbitmq` | RabbitMQ server hostname |
| `MESSAGING_RABBITMQ_PASSWORD` | string | `guest` (insecure) | RabbitMQ password |
| `MESSAGING_RABBITMQ_PORT` | int | `5672` | RabbitMQ server port |
| `MESSAGING_RABBITMQ_USERNAME` | string | `guest` (insecure) | RabbitMQ username |
| `MESSAGING_RABBITMQ_VHOST` | string | `/` | RabbitMQ virtual host |
| `MESSAGING_REQUEST_TIMEOUT_SECONDS` | int | `30` | Timeout in seconds for individual message operations |
| `MESSAGING_RETRY_BUFFER_ENABLED` | bool | `true` | Enable retry buffer for failed event publishes |
| `MESSAGING_RETRY_BUFFER_INTERVAL_SECONDS` | int | `5` | Interval between retry attempts for buffered messages |
| `MESSAGING_RETRY_BUFFER_MAX_AGE_SECONDS` | int | `300` | Maximum age of buffered messages before node crash (prevents... |
| `MESSAGING_RETRY_BUFFER_MAX_SIZE` | int | `10000` | Maximum number of messages in retry buffer before node crash |
| `MESSAGING_RETRY_DELAY_MS` | int | `5000` | Delay between retry attempts in milliseconds |
| `MESSAGING_RETRY_MAX_ATTEMPTS` | int | `3` | Maximum retry attempts before dead-lettering |
| `MESSAGING_USE_INMEMORY` | bool | `false` | Use in-memory messaging instead of RabbitMQ. Messages are NO... |
| `MESSAGING_USE_MASSTRANSIT` | bool | `true` | Use MassTransit wrapper (true) or direct RabbitMQ.Client (fa... |

### Orchestrator

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `ORCHESTRATOR_CACHE_TTL_MINUTES` | int | `5` | Cache TTL in minutes for orchestrator data |
| `ORCHESTRATOR_CERTIFICATES_HOST_PATH` | string | `/app/provisioning/certificates` | Host path for TLS certificates |
| `ORCHESTRATOR_DEGRADATION_THRESHOLD_MINUTES` | int | `5` | Time in minutes before a service is marked as degraded |
| `ORCHESTRATOR_DOCKER_HOST` | string | `unix:///var/run/docker.sock` | Docker host for direct Docker API access |
| `ORCHESTRATOR_DOCKER_NETWORK` | string | `bannou_default` | Docker network name for deployed containers |
| `ORCHESTRATOR_HEARTBEAT_TIMEOUT_SECONDS` | int | `90` | Service heartbeat timeout in seconds |
| `ORCHESTRATOR_KUBERNETES_NAMESPACE` | string | `default` | Kubernetes namespace for deployments |
| `ORCHESTRATOR_LOGS_VOLUME` | string | `logs-data` | Docker volume name for logs |
| `ORCHESTRATOR_PORTAINER_API_KEY` | string | **REQUIRED** | Portainer API key |
| `ORCHESTRATOR_PORTAINER_ENDPOINT_ID` | int | `1` | Portainer endpoint ID |
| `ORCHESTRATOR_PORTAINER_URL` | string | **REQUIRED** | Portainer API URL |
| `ORCHESTRATOR_PRESETS_HOST_PATH` | string | `/app/provisioning/orchestrator/presets` | Host path for orchestrator deployment presets |
| `ORCHESTRATOR_RABBITMQ_CONNECTION_STRING` | string | **REQUIRED** | RabbitMQ connection string for orchestrator messaging (requi... |
| `ORCHESTRATOR_REDIS_CONNECTION_STRING` | string | **REQUIRED** | Redis connection string for orchestrator state (required, no... |

### Permissions

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `PERMISSIONS_ENABLED` | bool | `true` | Enable/disable Permissions service |

### Realm

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `REALM_ENABLED` | bool | `true` | Enable/disable Realm service |

### Relationship

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `RELATIONSHIP_ENABLED` | bool | `true` | Enable/disable Relationship service |

### Relationship Type

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `RELATIONSHIP-TYPE_ENABLED` | bool | `true` | Enable/disable Relationship Type service |

### Servicedata

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `SERVICEDATA_STATE_STORE_NAME` | string | `servicedata-statestore` | State store name for service data |

### Species

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `SPECIES_ENABLED` | bool | `true` | Enable/disable Species service |

### State

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `DEFAULT_CONSISTENCY` | string | `strong` | Default consistency level for state operations (strong or ev... |
| `ENABLE_METRICS` | bool | `true` | Enable metrics collection for state operations |
| `ENABLE_TRACING` | bool | `true` | Enable distributed tracing for state operations |
| `MYSQL_CONNECTION_STRING` | string | **REQUIRED** | MySQL connection string for MySQL-backed state stores |
| `REDIS_CONNECTION_STRING` | string | **REQUIRED** | Redis connection string (host:port format) for Redis-backed ... |
| `STATE_CONNECTION_TIMEOUT_SECONDS` | int | `60` | Total timeout in seconds for establishing Redis/MySQL connec... |
| `STATE_CONNECT_RETRY_COUNT` | int | `5` | Maximum number of connection retry attempts |
| `STATE_USE_INMEMORY` | bool | `false` | Use in-memory storage instead of Redis/MySQL. Data is NOT pe... |

### Subscriptions

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `SUBSCRIPTIONS_AUTHORIZATION_SUFFIX` | string | `authorized` | Suffix for authorization keys in state store |
| `SUBSCRIPTIONS_STATE_STORE_NAME` | string | `subscriptions-statestore` | State store name for subscriptions |

### Voice

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `VOICE_KAMAILIO_HOST` | string | `localhost` | Kamailio SIP server host |
| `VOICE_KAMAILIO_RPC_PORT` | int | `5080` | Kamailio JSON-RPC port (typically 5080, not SIP port 5060) |
| `VOICE_P2P_MAX_PARTICIPANTS` | int | `8` | Maximum participants in P2P voice sessions |
| `VOICE_RTPENGINE_HOST` | string | `localhost` | RTPEngine media relay host |
| `VOICE_RTPENGINE_PORT` | int | `22222` | RTPEngine control port |
| `VOICE_SCALED_MAX_PARTICIPANTS` | int | `100` | Maximum participants in scaled tier voice sessions |
| `VOICE_SCALED_TIER_ENABLED` | bool | `false` | Enable scaled tier voice communication (SIP-based) |
| `VOICE_SIP_DOMAIN` | string | `voice.bannou.local` | SIP domain for voice communication |
| `VOICE_SIP_PASSWORD_SALT` | string | **REQUIRED** | Salt for SIP password generation |
| `VOICE_STUN_SERVERS` | string | `stun:stun.l.google.com:19302` | Comma-separated list of STUN server URLs for WebRTC |
| `VOICE_TIER_UPGRADE_ENABLED` | bool | `false` | Enable automatic tier upgrade from P2P to scaled |
| `VOICE_TIER_UPGRADE_MIGRATION_DEADLINE_MS` | int | `30000` | Migration deadline in milliseconds when upgrading tiers |

### Website

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `WEBSITE_ENABLED` | bool | `true` | Enable/disable Website service |

## Configuration Summary

- **Total properties**: 176
- **Required (no default)**: 33
- **Optional (has default)**: 143

## Environment Variable Naming Convention

Per Tenet 2, all configuration environment variables follow `{SERVICE}_{PROPERTY}` pattern:

```bash
# Service prefix in UPPER_CASE
AUTH_JWT_SECRET=your-secret
CONNECT_MAX_CONNECTIONS=1000
BEHAVIOR_CACHE_TTL_SECONDS=300
```

The BANNOU_ prefix is also supported for backwards compatibility:

```bash
# Also valid (BANNOU_ prefix)
BANNOU_AUTH_JWT_SECRET=your-secret
```

## Required Configuration (Fail-Fast)

Per Tenet 21, configuration marked as **REQUIRED** will cause the service to
throw an exception at startup if not configured. This prevents running with
insecure defaults or missing critical configuration.

```csharp
// Example of fail-fast pattern
var secret = config.JwtSecret
    ?? throw new InvalidOperationException("AUTH_JWT_SECRET required");
```

## .env File Configuration

Create a `.env` file in the repository root with your configuration:

```bash
# Required configuration (no defaults)
AUTH_JWT_SECRET=your-production-secret
CONNECT_RABBITMQ_CONNECTION_STRING=amqp://user:pass@host:5672

# Optional configuration (has defaults)
AUTH_JWT_EXPIRATION_MINUTES=60
CONNECT_MAX_CONNECTIONS=1000
```

See `.env.example` for a complete template.

---

*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*
