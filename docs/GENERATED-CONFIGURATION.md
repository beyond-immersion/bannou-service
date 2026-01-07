# Generated Configuration Reference

> **Source**: `schemas/*-configuration.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists all configuration options defined in Bannou's configuration schemas.

## Configuration by Service

### Account

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `ACCOUNT_ADMIN_EMAILS` | string | **REQUIRED** | Comma-separated list of admin email addresses |
| `ACCOUNT_ADMIN_EMAIL_DOMAIN` | string | **REQUIRED** | Email domain that grants admin access (e.g., "@company.com") |

### Achievement

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `ACHIEVEMENT_AUTO_SYNC_ON_UNLOCK` | bool | `true` | Automatically sync achievements to platforms when unlocked |
| `ACHIEVEMENT_DEFINITION_STORE_NAME` | string | `achievement-definition` | Name of the state store for achievement definitions (MySQL r... |
| `ACHIEVEMENT_ENABLED` | bool | `true` | Enable/disable Achievement service |
| `ACHIEVEMENT_PLAYSTATION_CLIENT_ID` | string | **REQUIRED** | PlayStation Network client ID (stub - not implemented) |
| `ACHIEVEMENT_PLAYSTATION_CLIENT_SECRET` | string | **REQUIRED** | PlayStation Network client secret (stub - not implemented) |
| `ACHIEVEMENT_PROGRESS_CACHE_TTL_SECONDS` | int | `300` | TTL in seconds for cached progress data |
| `ACHIEVEMENT_PROGRESS_STORE_NAME` | string | `achievement-progress` | Name of the state store for progress tracking (Redis for hot... |
| `ACHIEVEMENT_RARE_THRESHOLD_PERCENT` | double | `5.0` | Threshold percentage below which an achievement is considere... |
| `ACHIEVEMENT_RARITY_CALCULATION_INTERVAL_MINUTES` | int | `60` | How often to recalculate achievement rarity percentages |
| `ACHIEVEMENT_STEAM_API_KEY` | string | **REQUIRED** | Steam Web API key for achievement sync |
| `ACHIEVEMENT_STEAM_APP_ID` | string | **REQUIRED** | Steam App ID for achievement mapping |
| `ACHIEVEMENT_SYNC_RETRY_ATTEMPTS` | int | `3` | Number of retry attempts for failed platform syncs |
| `ACHIEVEMENT_SYNC_RETRY_DELAY_SECONDS` | int | `60` | Delay between sync retry attempts in seconds |
| `ACHIEVEMENT_UNLOCK_STORE_NAME` | string | `achievement-unlock` | Name of the state store for unlock records (MySQL for persis... |
| `ACHIEVEMENT_XBOX_CLIENT_ID` | string | **REQUIRED** | Xbox Live client ID (stub - not implemented) |
| `ACHIEVEMENT_XBOX_CLIENT_SECRET` | string | **REQUIRED** | Xbox Live client secret (stub - not implemented) |

### Actor

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `ACTOR_CONTROL_PLANE_APP_ID` | string | `bannou` | App-id of control plane for pool node registration. Pool nod... |
| `ACTOR_DEFAULT_ACTORS_PER_NODE` | int | `100` | Default capacity per pool node |
| `ACTOR_DEFAULT_AUTOSAVE_INTERVAL_SECONDS` | int | `60` | Default interval for periodic state saves (0 to disable) |
| `ACTOR_DEFAULT_MEMORY_EXPIRATION_MINUTES` | int | `60` | Default expiration time in minutes for actor memories |
| `ACTOR_DEFAULT_TICK_INTERVAL_MS` | int | `100` | Default behavior loop interval in milliseconds |
| `ACTOR_DEPLOYMENT_MODE` | string | `bannou` | Actor deployment mode: bannou (local dev), pool-per-type, sh... |
| `ACTOR_GOAP_MAX_PLAN_DEPTH` | int | `10` | Maximum depth for GOAP planning search |
| `ACTOR_GOAP_PLAN_TIMEOUT_MS` | int | `50` | Maximum time allowed for GOAP planning in milliseconds |
| `ACTOR_GOAP_REPLAN_THRESHOLD` | double | `0.3` | Threshold for triggering GOAP replanning when goal relevance... |
| `ACTOR_HEARTBEAT_INTERVAL_SECONDS` | int | `10` | Pool node heartbeat frequency |
| `ACTOR_HEARTBEAT_TIMEOUT_SECONDS` | int | `30` | Mark node unhealthy after this many seconds without heartbea... |
| `ACTOR_INSTANCE_STATESTORE_NAME` | string | `actor-instances` | Name of the state store for actor instance tracking |
| `ACTOR_LOCAL_MODE_APP_ID` | string | `bannou` | App ID used when running in local/bannou deployment mode |
| `ACTOR_LOCAL_MODE_NODE_ID` | string | `bannou-local` | Node ID used when running in local/bannou deployment mode |
| `ACTOR_MAX_POOL_NODES` | int | `10` | Maximum pool nodes allowed (auto-scale mode) |
| `ACTOR_MESSAGE_QUEUE_SIZE` | int | `50` | Max messages queued per actor before dropping oldest |
| `ACTOR_MIN_POOL_NODES` | int | `1` | Minimum pool nodes to maintain (auto-scale mode) |
| `ACTOR_PERCEPTION_FILTER_THRESHOLD` | double | `0.1` | Minimum urgency for perception to be processed (0.0-1.0) |
| `ACTOR_PERCEPTION_MEMORY_THRESHOLD` | double | `0.7` | Minimum urgency for perception to become a memory (0.0-1.0) |
| `ACTOR_PERCEPTION_QUEUE_SIZE` | int | `100` | Max perceptions queued per actor before dropping oldest |
| `ACTOR_POOL_NODE_APP_ID` | string | **REQUIRED** | Mesh app-id for routing commands to this pool node. Required... |
| `ACTOR_POOL_NODE_CAPACITY` | int | `100` | Maximum actors this pool node can run. Overrides DefaultActo... |
| `ACTOR_POOL_NODE_ID` | string | **REQUIRED** | If set, this instance runs as a pool node (not control plane... |
| `ACTOR_POOL_NODE_IMAGE` | string | `bannou-actor-pool:latest` | Docker image for pool nodes (pool-per-type, shared-pool, aut... |
| `ACTOR_POOL_NODE_TYPE` | string | `shared` | Pool type this node belongs to: shared, npc-brain, event-coo... |
| `ACTOR_SHORT_TERM_MEMORY_MINUTES` | int | `5` | Expiration time in minutes for short-term memories from high... |
| `ACTOR_STATE_STORE_NAME` | string | `actor-state` | Name of the state store for actor state persistence |
| `ACTOR_STATE_UPDATE_TRANSPORT` | string | `messaging` | State update transport: messaging (default, works in bannou ... |
| `ACTOR_TEMPLATE_STATESTORE_NAME` | string | `actor-templates` | Name of the state store for actor templates |

### Analytics

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `ANALYTICS_ENABLED` | bool | `true` | Enable/disable Analytics service |
| `ANALYTICS_EVENT_BUFFER_FLUSH_INTERVAL_SECONDS` | int | `5` | Interval in seconds to flush event buffer |
| `ANALYTICS_EVENT_BUFFER_SIZE` | int | `1000` | Maximum events to buffer before flushing to storage |
| `ANALYTICS_GLICKO2_DEFAULT_DEVIATION` | double | `350.0` | Default rating deviation for new entities (higher = less cer... |
| `ANALYTICS_GLICKO2_DEFAULT_RATING` | double | `1500.0` | Default Glicko-2 rating for new entities |
| `ANALYTICS_GLICKO2_DEFAULT_VOLATILITY` | double | `0.06` | Default volatility for new entities (0.06 is standard) |
| `ANALYTICS_GLICKO2_SYSTEM_CONSTANT` | double | `0.5` | Glicko-2 system constant (tau) - controls volatility change ... |
| `ANALYTICS_HISTORY_STORE_NAME` | string | `analytics-history` | Name of the state store for event history (MySQL recommended... |
| `ANALYTICS_RATING_STORE_NAME` | string | `analytics-rating` | Name of the state store for skill ratings (Redis recommended... |
| `ANALYTICS_SUMMARY_CACHE_TTL_SECONDS` | int | `300` | TTL in seconds for cached entity summaries |
| `ANALYTICS_SUMMARY_STORE_NAME` | string | `analytics-summary` | Name of the state store for entity summaries (Redis recommen... |

### Asset

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `ASSET_ADDITIONAL_EXTENSION_MAPPINGS` | string | **REQUIRED** | Comma-separated ext=type pairs for additional extension mapp... |
| `ASSET_ADDITIONAL_FORBIDDEN_CONTENT_TYPES` | string | **REQUIRED** | Comma-separated list of additional forbidden content types |
| `ASSET_ADDITIONAL_PROCESSABLE_CONTENT_TYPES` | string | **REQUIRED** | Comma-separated list of additional processable content types... |
| `ASSET_AUDIO_BITRATE_KBPS` | int | `192` | Default audio bitrate in kbps |
| `ASSET_AUDIO_OUTPUT_FORMAT` | string | `mp3` | Default audio output format (mp3, opus, aac) |
| `ASSET_AUDIO_PRESERVE_LOSSLESS` | bool | `true` | Keep original lossless file alongside transcoded version |
| `ASSET_AUDIO_PROCESSOR_POOL_TYPE` | string | `audio-processor` | Pool type name for audio processing |
| `ASSET_BUNDLE_COMPRESSION_DEFAULT` | string | `lz4` | Default compression for bundles (lz4, lzma, none) |
| `ASSET_BUNDLE_CURRENT_PATH_PREFIX` | string | `bundles/current` | Path prefix for finalized bundles in storage bucket |
| `ASSET_BUNDLE_KEY_PREFIX` | string | `bundle:` | Key prefix for bundle entries in state store |
| `ASSET_BUNDLE_UPLOAD_PATH_PREFIX` | string | `bundles/uploads` | Path prefix for bundle upload staging in storage bucket |
| `ASSET_BUNDLE_ZIP_CACHE_PATH_PREFIX` | string | `bundles/zip-cache` | Path prefix for ZIP conversion cache in storage bucket |
| `ASSET_DEFAULT_PROCESSOR_POOL_TYPE` | string | `asset-processor` | Default pool type name for general asset processing |
| `ASSET_DOWNLOAD_TOKEN_TTL_SECONDS` | int | `900` | TTL for download URLs (can be shorter than upload) |
| `ASSET_FFMPEG_PATH` | string | **REQUIRED** | Path to FFmpeg binary (empty = use system PATH) |
| `ASSET_FFMPEG_WORKING_DIR` | string | `/tmp/bannou-ffmpeg` | Working directory for FFmpeg temporary files |
| `ASSET_FINAL_ASSET_PATH_PREFIX` | string | `assets` | Path prefix for final asset storage in bucket |
| `ASSET_INDEX_KEY_PREFIX` | string | `asset-index:` | Key prefix for asset index entries in state store |
| `ASSET_KEY_PREFIX` | string | `asset:` | Key prefix for asset entries in state store |
| `ASSET_LARGE_FILE_THRESHOLD_MB` | int | `50` | File size threshold for delegating to processing pool |
| `ASSET_MAX_UPLOAD_SIZE_MB` | int | `500` | Maximum upload size in megabytes |
| `ASSET_MINIO_WEBHOOK_SECRET` | string | **REQUIRED** | Secret for validating MinIO webhook requests |
| `ASSET_MODEL_PROCESSOR_POOL_TYPE` | string | `model-processor` | Pool type name for 3D model processing |
| `ASSET_MULTIPART_PART_SIZE_MB` | int | `16` | Size of each part in multipart uploads in megabytes |
| `ASSET_MULTIPART_THRESHOLD_MB` | int | `50` | File size threshold for multipart uploads in megabytes |
| `ASSET_PROCESSING_MAX_RETRIES` | int | `5` | Maximum retry attempts for asset processing |
| `ASSET_PROCESSING_MODE` | string | `both` | Service mode (api, worker, both) |
| `ASSET_PROCESSING_POOL_TYPE` | string | `asset-processor` | Processing pool identifier for orchestrator |
| `ASSET_PROCESSING_RETRY_DELAY_SECONDS` | int | `30` | Delay in seconds between processing retries |
| `ASSET_PROCESSOR_AVAILABILITY_MAX_WAIT_SECONDS` | int | `60` | Maximum seconds to wait for processor availability |
| `ASSET_PROCESSOR_AVAILABILITY_POLL_INTERVAL_SECONDS` | int | `2` | Polling interval in seconds when waiting for processor |
| `ASSET_PROCESSOR_HEARTBEAT_INTERVAL_SECONDS` | int | `30` | Heartbeat emission interval in seconds |
| `ASSET_PROCESSOR_HEARTBEAT_TIMEOUT_SECONDS` | int | `90` | Mark node unhealthy after this many seconds without heartbea... |
| `ASSET_PROCESSOR_IDLE_TIMEOUT_SECONDS` | int | `300` | Seconds of zero-load before auto-termination (0 to disable) |
| `ASSET_PROCESSOR_MAX_CONCURRENT_JOBS` | int | `10` | Maximum concurrent jobs per processor node |
| `ASSET_PROCESSOR_NODE_ID` | string | **REQUIRED** | Unique processor node ID (set by orchestrator when spawning ... |
| `ASSET_PROCESSOR_POOL_STORE_NAME` | string | `asset-processor-pool` | Name of the state store for processor pool management |
| `ASSET_STATESTORE_NAME` | string | `asset-statestore` | Name of the state store for asset metadata |
| `ASSET_STORAGE_ACCESS_KEY` | string | **REQUIRED** | Storage access key/username |
| `ASSET_STORAGE_BUCKET` | string | `bannou-assets` | Primary bucket/container name for assets |
| `ASSET_STORAGE_ENDPOINT` | string | `http://minio:9000` | Storage endpoint URL (MinIO/S3 compatible) |
| `ASSET_STORAGE_FORCE_PATH_STYLE` | bool | `true` | Force path-style URLs (required for MinIO) |
| `ASSET_STORAGE_PROVIDER` | string | `minio` | Storage backend type (minio, s3, r2, azure, filesystem) |
| `ASSET_STORAGE_REGION` | string | `us-east-1` | Storage region (for S3/R2) |
| `ASSET_STORAGE_SECRET_KEY` | string | **REQUIRED** | Storage secret key/password |
| `ASSET_STORAGE_USE_SSL` | bool | `false` | Use SSL/TLS for storage connections |
| `ASSET_TEMP_UPLOAD_PATH_PREFIX` | string | `temp` | Path prefix for temporary upload staging in storage bucket |
| `ASSET_TEXTURE_PROCESSOR_POOL_TYPE` | string | `texture-processor` | Pool type name for texture processing |
| `ASSET_TOKEN_TTL_SECONDS` | int | `3600` | TTL for pre-signed upload/download URLs in seconds |
| `ASSET_UPLOAD_SESSION_KEY_PREFIX` | string | `upload:` | Key prefix for upload session entries in state store |
| `ASSET_WORKER_POOL` | string | **REQUIRED** | Worker pool identifier when running in worker mode |
| `ASSET_ZIP_CACHE_TTL_HOURS` | int | `24` | TTL for cached ZIP conversions in hours |

### Auth

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `AUTH_CONNECT_URL` | string | `ws://localhost:5014/connect` | URL to the Connect service for WebSocket connections returne... |
| `AUTH_DISCORD_CLIENT_ID` | string | **REQUIRED** | Discord OAuth client ID |
| `AUTH_DISCORD_CLIENT_SECRET` | string | **REQUIRED** | Discord OAuth client secret |
| `AUTH_DISCORD_REDIRECT_URI` | string | **REQUIRED** | Discord OAuth redirect URI |
| `AUTH_GOOGLE_CLIENT_ID` | string | **REQUIRED** | Google OAuth client ID |
| `AUTH_GOOGLE_CLIENT_SECRET` | string | **REQUIRED** | Google OAuth client secret |
| `AUTH_GOOGLE_REDIRECT_URI` | string | **REQUIRED** | Google OAuth redirect URI |
| `AUTH_JWT_AUDIENCE` | string | `bannou-api` | JWT token audience |
| `AUTH_JWT_EXPIRATION_MINUTES` | int | `60` | JWT token expiration time in minutes |
| `AUTH_JWT_ISSUER` | string | `bannou-auth` | JWT token issuer |
| `AUTH_JWT_SECRET` | string | **REQUIRED** | Secret key for JWT token signing (REQUIRED - service fails f... |
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
| `BEHAVIOR_BUNDLE_MEMBERSHIP_KEY_PREFIX` | string | `bundle-membership:` | Key prefix for bundle membership entries |
| `BEHAVIOR_COMPILER_MAX_CONSTANTS` | int | `256` | Maximum constants in behavior constant pool |
| `BEHAVIOR_COMPILER_MAX_STRINGS` | int | `65536` | Maximum strings in behavior string table |
| `BEHAVIOR_DEFAULT_EMOTIONAL_WEIGHT` | double | `0.4` | Weight for emotional significance in perception scoring |
| `BEHAVIOR_DEFAULT_GOAL_RELEVANCE_WEIGHT` | double | `0.4` | Weight for goal relevance in perception scoring |
| `BEHAVIOR_DEFAULT_MEMORY_LIMIT` | int | `100` | Maximum memory entries per actor |
| `BEHAVIOR_DEFAULT_NOVELTY_WEIGHT` | double | `5.0` | Attention priority weight for novel perceptions |
| `BEHAVIOR_DEFAULT_RELATIONSHIP_WEIGHT` | double | `0.2` | Weight for relationship significance in perception scoring |
| `BEHAVIOR_DEFAULT_ROUTINE_WEIGHT` | double | `1.0` | Attention priority weight for routine perceptions |
| `BEHAVIOR_DEFAULT_SOCIAL_WEIGHT` | double | `3.0` | Attention priority weight for social perceptions |
| `BEHAVIOR_DEFAULT_STORAGE_THRESHOLD` | double | `0.7` | Significance score threshold for storing memories (0.0-1.0) |
| `BEHAVIOR_DEFAULT_THREAT_FAST_TRACK_THRESHOLD` | double | `0.8` | Urgency threshold for fast-tracking threat perceptions (0.0-... |
| `BEHAVIOR_DEFAULT_THREAT_WEIGHT` | double | `10.0` | Attention priority weight for threat perceptions |
| `BEHAVIOR_ENABLED` | bool | `true` | Enable/disable Behavior service |
| `BEHAVIOR_GOAP_METADATA_KEY_PREFIX` | string | `goap-metadata:` | Key prefix for GOAP metadata entries |
| `BEHAVIOR_HIGH_URGENCY_MAX_PLAN_DEPTH` | int | `3` | Maximum depth for GOAP planning search at high urgency |
| `BEHAVIOR_HIGH_URGENCY_MAX_PLAN_NODES` | int | `200` | Maximum nodes to explore during GOAP planning at high urgenc... |
| `BEHAVIOR_HIGH_URGENCY_PLAN_TIMEOUT_MS` | int | `20` | Maximum time in ms for GOAP planning at high urgency |
| `BEHAVIOR_HIGH_URGENCY_THRESHOLD` | double | `0.7` | Threshold above which urgency is considered high (0.0-1.0) |
| `BEHAVIOR_LOW_URGENCY_MAX_PLAN_DEPTH` | int | `10` | Maximum depth for GOAP planning search at low urgency |
| `BEHAVIOR_LOW_URGENCY_MAX_PLAN_NODES` | int | `1000` | Maximum nodes to explore during GOAP planning at low urgency |
| `BEHAVIOR_LOW_URGENCY_PLAN_TIMEOUT_MS` | int | `100` | Maximum time in ms for GOAP planning at low urgency |
| `BEHAVIOR_LOW_URGENCY_THRESHOLD` | double | `0.3` | Threshold below which urgency is considered low (0.0-1.0) |
| `BEHAVIOR_MEDIUM_URGENCY_MAX_PLAN_DEPTH` | int | `6` | Maximum depth for GOAP planning search at medium urgency |
| `BEHAVIOR_MEDIUM_URGENCY_MAX_PLAN_NODES` | int | `500` | Maximum nodes to explore during GOAP planning at medium urge... |
| `BEHAVIOR_MEDIUM_URGENCY_PLAN_TIMEOUT_MS` | int | `50` | Maximum time in ms for GOAP planning at medium urgency |
| `BEHAVIOR_MEMORY_CATEGORY_MATCH_WEIGHT` | double | `0.3` | Weight for category match in memory relevance scoring |
| `BEHAVIOR_MEMORY_CONTENT_OVERLAP_WEIGHT` | double | `0.4` | Weight for content keyword overlap in memory relevance scori... |
| `BEHAVIOR_MEMORY_INDEX_KEY_PREFIX` | string | `memory-index:` | Key prefix for memory index entries |
| `BEHAVIOR_MEMORY_KEY_PREFIX` | string | `memory:` | Key prefix for memory entries |
| `BEHAVIOR_MEMORY_METADATA_OVERLAP_WEIGHT` | double | `0.2` | Weight for metadata key overlap in memory relevance scoring |
| `BEHAVIOR_MEMORY_MINIMUM_RELEVANCE_THRESHOLD` | double | `0.1` | Minimum relevance score for memory retrieval (0.0-1.0) |
| `BEHAVIOR_MEMORY_RECENCY_BONUS_WEIGHT` | double | `0.1` | Maximum recency bonus for memories less than 1 hour old |
| `BEHAVIOR_MEMORY_SIGNIFICANCE_BONUS_WEIGHT` | double | `0.1` | Weight for memory significance in relevance scoring |
| `BEHAVIOR_MEMORY_STATESTORE_NAME` | string | `agent-memories` | Name of the state store for actor memories |
| `BEHAVIOR_MEMORY_STORE_MAX_RETRIES` | int | `3` | Max retries for memory store operations |
| `BEHAVIOR_METADATA_KEY_PREFIX` | string | `behavior-metadata:` | Key prefix for behavior metadata entries |
| `BEHAVIOR_STATESTORE_NAME` | string | `behavior-statestore` | Name of the state store for behavior metadata |

### Character

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `CHARACTER_DEFAULT_PAGE_SIZE` | int | `20` | Default page size when not specified |
| `CHARACTER_MAX_PAGE_SIZE` | int | `100` | Maximum page size for list queries |
| `CHARACTER_RETENTION_DAYS` | int | `90` | Number of days to retain deleted characters before permanent... |

### Connect

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `CONNECT_AUTHENTICATED_SERVICES` | string[] | `['account', 'behavior', 'permission', 'gamesession']` | Additional services available to authenticated connections |
| `CONNECT_BINARY_PROTOCOL_VERSION` | string | `2.0` | Binary protocol version identifier |
| `CONNECT_BUFFER_SIZE` | int | `65536` | Size of message buffers in bytes |
| `CONNECT_CONNECTION_MODE` | string | `external` | Connection mode: external (default, no broadcast), relayed (... |
| `CONNECT_CONNECTION_TIMEOUT_SECONDS` | int | `300` | WebSocket connection timeout in seconds |
| `CONNECT_DEFAULT_SERVICES` | string[] | `['auth', 'website']` | Services available to unauthenticated connections |
| `CONNECT_ENABLE_CLIENT_TO_CLIENT_ROUTING` | bool | `true` | Enable routing messages between WebSocket clients |
| `CONNECT_HEARTBEAT_INTERVAL_SECONDS` | int | `30` | Interval between heartbeat messages |
| `CONNECT_HEARTBEAT_TTL_SECONDS` | int | `300` | Heartbeat data TTL in Redis in seconds (default 5 minutes) |
| `CONNECT_INTERNAL_AUTH_MODE` | string | `service-token` | Auth mode for internal connections: service-token (validate ... |
| `CONNECT_INTERNAL_SERVICE_TOKEN` | string | **REQUIRED** | Secret for X-Service-Token validation when InternalAuthMode ... |
| `CONNECT_JWT_PUBLIC_KEY` | string | **REQUIRED** | RSA public key for JWT validation (PEM format) |
| `CONNECT_MAX_CONCURRENT_CONNECTIONS` | int | `10000` | Maximum number of concurrent WebSocket connections |
| `CONNECT_MAX_MESSAGES_PER_MINUTE` | int | `1000` | Rate limit for messages per minute per client |
| `CONNECT_MESSAGE_QUEUE_SIZE` | int | `1000` | Maximum number of queued messages per connection |
| `CONNECT_RABBITMQ_CONNECTION_STRING` | string | **REQUIRED** | RabbitMQ connection string for client event subscriptions. N... |
| `CONNECT_RATE_LIMIT_WINDOW_MINUTES` | int | `1` | Rate limit window in minutes |
| `CONNECT_RECONNECTION_WINDOW_SECONDS` | int | `300` | Window for client reconnection after disconnect in seconds (... |
| `CONNECT_SERVER_SALT` | string | **REQUIRED** | Server salt for client GUID generation. REQUIRED - must be s... |
| `CONNECT_SESSION_TTL_SECONDS` | int | `86400` | Session time-to-live in seconds (default 24 hours) |
| `CONNECT_URL` | string | **REQUIRED** | WebSocket URL returned to clients for reconnection |

### Documentation

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `DOCUMENTATION_AI_EMBEDDINGS_MODEL` | string | **REQUIRED** | Model for generating embeddings (when AI enabled) |
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

### Game Service

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `GAME_SERVICE_STATE_STORE_NAME` | string | `game-service-statestore` | State store name for game service data |

### Game Session

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `GAME_SESSION_DEFAULT_SESSION_TIMEOUT_SECONDS` | int | `7200` | Default session timeout in seconds |
| `GAME_SESSION_ENABLED` | bool | `true` | Enable/disable Game Session service |
| `GAME_SESSION_MAX_PLAYERS_PER_SESSION` | int | `16` | Maximum players allowed per session |
| `GAME_SESSION_SERVER_SALT` | string | **REQUIRED** | Server salt for GUID generation. REQUIRED - must be shared a... |

### Leaderboard

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `LEADERBOARD_AUTO_ARCHIVE_ON_SEASON_END` | bool | `true` | Automatically archive leaderboard data when season ends |
| `LEADERBOARD_DEFINITION_STORE_NAME` | string | `leaderboard-definition` | Name of the state store for leaderboard definitions (MySQL r... |
| `LEADERBOARD_ENABLED` | bool | `true` | Enable/disable Leaderboard service |
| `LEADERBOARD_MAX_ENTRIES_PER_QUERY` | int | `1000` | Maximum entries returned per rank query |
| `LEADERBOARD_RANKING_STORE_NAME` | string | `leaderboard-ranking` | Name of the state store for rankings (Redis required for sor... |
| `LEADERBOARD_RANK_CACHE_TTL_SECONDS` | int | `60` | TTL in seconds for cached rank queries |
| `LEADERBOARD_SCORE_UPDATE_BATCH_SIZE` | int | `1000` | Maximum scores to process in a single batch |
| `LEADERBOARD_SEASON_STORE_NAME` | string | `leaderboard-season` | Name of the state store for season data (MySQL recommended) |

### Location

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `LOCATION_ENABLED` | bool | `true` | Enable/disable Location service |

### Mapping

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `MAPPING_AFFORDANCE_CACHE_TIMEOUT_SECONDS` | int | `60` | Default TTL for cached affordance query results |
| `MAPPING_AUTHORITY_GRACE_PERIOD_SECONDS` | int | `30` | Grace period in seconds after missed heartbeat before author... |
| `MAPPING_AUTHORITY_HEARTBEAT_INTERVAL_SECONDS` | int | `30` | Recommended heartbeat interval for authorities (for client g... |
| `MAPPING_AUTHORITY_TIMEOUT_SECONDS` | int | `60` | Time in seconds before authority expires without heartbeat |
| `MAPPING_DEFAULT_LAYER_CACHE_TTL_SECONDS` | int | `3600` | Default TTL for cached layer data (ephemeral kinds) |
| `MAPPING_ENABLED` | bool | `true` | Enable/disable Mapping service |
| `MAPPING_INLINE_PAYLOAD_MAX_BYTES` | int | `65536` | Payloads larger than this are stored via lib-asset reference |
| `MAPPING_MAX_AFFORDANCE_CANDIDATES` | int | `1000` | Maximum candidate points to evaluate in affordance queries |
| `MAPPING_MAX_CHECKOUT_DURATION_SECONDS` | int | `1800` | Maximum duration for authoring checkout locks |
| `MAPPING_MAX_OBJECTS_PER_QUERY` | int | `5000` | Maximum objects returned in a single query |
| `MAPPING_MAX_PAYLOADS_PER_PUBLISH` | int | `100` | Maximum payloads in single publish or ingest event |
| `MAPPING_SPATIAL_CELL_SIZE` | double | `64.0` | Size of spatial index cells in world units (default 64) |

### Mesh

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `MESH_CIRCUIT_BREAKER_ENABLED` | bool | `true` | Whether to enable circuit breaker for failed endpoints |
| `MESH_CIRCUIT_BREAKER_RESET_SECONDS` | int | `30` | Seconds before attempting to close circuit |
| `MESH_CIRCUIT_BREAKER_THRESHOLD` | int | `5` | Number of consecutive failures before opening circuit |
| `MESH_DEFAULT_LOAD_BALANCER` | string | `RoundRobin` | Default load balancing algorithm (RoundRobin, LeastConnectio... |
| `MESH_DEGRADATION_THRESHOLD_SECONDS` | int | `60` | Time without heartbeat before marking endpoint as degraded |
| `MESH_ENABLE_DETAILED_LOGGING` | bool | `false` | Whether to log detailed routing decisions |
| `MESH_ENABLE_SERVICE_MAPPING_SYNC` | bool | `true` | Whether to subscribe to FullServiceMappingsEvent for routing... |
| `MESH_ENDPOINT_HOST` | string | **REQUIRED** | Hostname/IP for mesh endpoint registration. Defaults to app-... |
| `MESH_ENDPOINT_PORT` | int | `80` | Port for mesh endpoint registration. |
| `MESH_ENDPOINT_TTL_SECONDS` | int | `90` | TTL for endpoint registration (should be > 2x heartbeat inte... |
| `MESH_HEALTH_CHECK_ENABLED` | bool | `false` | Whether to perform active health checks on endpoints |
| `MESH_HEALTH_CHECK_INTERVAL_SECONDS` | int | `60` | Interval between active health checks |
| `MESH_HEALTH_CHECK_TIMEOUT_SECONDS` | int | `5` | Timeout for health check requests |
| `MESH_HEARTBEAT_INTERVAL_SECONDS` | int | `30` | Recommended interval between heartbeats |
| `MESH_LOAD_THRESHOLD_PERCENT` | int | `80` | Load percentage above which an endpoint is considered high-l... |
| `MESH_MAX_RETRIES` | int | `3` | Maximum retry attempts for failed service calls |
| `MESH_METRICS_ENABLED` | bool | `true` | Whether to collect routing metrics |
| `MESH_REDIS_CONNECTION_STRING` | string | `redis:6379` | Redis connection string for service registry storage. |
| `MESH_REDIS_CONNECTION_TIMEOUT_SECONDS` | int | `60` | Total timeout in seconds for Redis connection establishment ... |
| `MESH_REDIS_CONNECT_RETRY_COUNT` | int | `5` | Maximum number of Redis connection retry attempts |
| `MESH_REDIS_KEY_PREFIX` | string | `mesh:` | Prefix for all mesh-related Redis keys |
| `MESH_REDIS_SYNC_TIMEOUT_MS` | int | `5000` | Timeout in milliseconds for synchronous Redis operations |
| `MESH_RETRY_DELAY_MILLISECONDS` | int | `100` | Initial delay between retries (doubles on each retry) |
| `MESH_USE_LOCAL_ROUTING` | bool | `false` | Use local-only routing instead of Redis. All calls route to ... |

### Messaging

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `MESSAGING_CALLBACK_RETRY_DELAY_MS` | int | `1000` | Delay between HTTP callback retry attempts in milliseconds |
| `MESSAGING_CALLBACK_RETRY_MAX_ATTEMPTS` | int | `3` | Maximum retry attempts for HTTP callback delivery (network f... |
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
| `ORCHESTRATOR_DEFAULT_BACKEND` | string | `compose` | Default container orchestration backend when not specified i... |
| `ORCHESTRATOR_DEGRADATION_THRESHOLD_MINUTES` | int | `5` | Time in minutes before a service is marked as degraded |
| `ORCHESTRATOR_DOCKER_HOST` | string | `unix:///var/run/docker.sock` | Docker host for direct Docker API access |
| `ORCHESTRATOR_DOCKER_NETWORK` | string | `bannou_default` | Docker network name for deployed containers |
| `ORCHESTRATOR_HEARTBEAT_TIMEOUT_SECONDS` | int | `90` | Service heartbeat timeout in seconds |
| `ORCHESTRATOR_KUBECONFIG_PATH` | string | **REQUIRED** | Path to kubeconfig file (null uses default ~/.kube/config) |
| `ORCHESTRATOR_KUBERNETES_NAMESPACE` | string | `default` | Kubernetes namespace for deployments |
| `ORCHESTRATOR_LOGS_VOLUME` | string | `logs-data` | Docker volume name for logs |
| `ORCHESTRATOR_OPENRESTY_HOST` | string | `openresty` | OpenResty hostname for cache invalidation calls |
| `ORCHESTRATOR_OPENRESTY_PORT` | int | `80` | OpenResty port for cache invalidation calls |
| `ORCHESTRATOR_PORTAINER_API_KEY` | string | **REQUIRED** | Portainer API key |
| `ORCHESTRATOR_PORTAINER_ENDPOINT_ID` | int | `1` | Portainer endpoint ID |
| `ORCHESTRATOR_PORTAINER_URL` | string | **REQUIRED** | Portainer API URL |
| `ORCHESTRATOR_PRESETS_HOST_PATH` | string | `/app/provisioning/orchestrator/presets` | Host path for orchestrator deployment presets |
| `ORCHESTRATOR_RABBITMQ_CONNECTION_STRING` | string | **REQUIRED** | RabbitMQ connection string for orchestrator messaging. No de... |
| `ORCHESTRATOR_REDIS_CONNECTION_STRING` | string | `redis:6379` | Redis connection string for orchestrator state. |

### Permission

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `PERMISSION_ENABLED` | bool | `true` | Enable/disable Permission service |
| `PERMISSION_LOCK_BASE_DELAY_MS` | int | `100` | Base delay in ms between lock retry attempts (exponential ba... |
| `PERMISSION_LOCK_EXPIRY_SECONDS` | int | `30` | Distributed lock expiration time in seconds |
| `PERMISSION_LOCK_MAX_RETRIES` | int | `10` | Maximum retries for acquiring distributed lock |

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
| `RELATIONSHIP_TYPE_ENABLED` | bool | `true` | Enable/disable Relationship Type service |

### Species

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `SPECIES_ENABLED` | bool | `true` | Enable/disable Species service |

### State

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `STATE_CONNECTION_TIMEOUT_SECONDS` | int | `60` | Total timeout in seconds for establishing Redis/MySQL connec... |
| `STATE_CONNECT_RETRY_COUNT` | int | `5` | Maximum number of connection retry attempts |
| `STATE_DEFAULT_CONSISTENCY` | string | `strong` | Default consistency level for state operations (strong or ev... |
| `STATE_ENABLE_METRICS` | bool | `true` | Enable metrics collection for state operations |
| `STATE_ENABLE_TRACING` | bool | `true` | Enable distributed tracing for state operations |
| `STATE_MYSQL_CONNECTION_STRING` | string | **REQUIRED** | MySQL connection string for MySQL-backed state stores |
| `STATE_REDIS_CONNECTION_STRING` | string | `redis:6379` | Redis connection string (host:port format) for Redis-backed ... |
| `STATE_USE_INMEMORY` | bool | `false` | Use in-memory storage instead of Redis/MySQL. Data is NOT pe... |

### Subscription

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `SUBSCRIPTION_AUTHORIZATION_SUFFIX` | string | `authorized` | Suffix for authorization keys in state store |
| `SUBSCRIPTION_STATE_STORE_NAME` | string | `subscription-statestore` | State store name for subscription |

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
| `VOICE_SIP_PASSWORD_SALT` | string | **REQUIRED** | Salt for SIP password generation. Required only when ScaledT... |
| `VOICE_STUN_SERVERS` | string | `stun:stun.l.google.com:19302` | Comma-separated list of STUN server URLs for WebRTC |
| `VOICE_TIER_UPGRADE_ENABLED` | bool | `false` | Enable automatic tier upgrade from P2P to scaled |
| `VOICE_TIER_UPGRADE_MIGRATION_DEADLINE_MS` | int | `30000` | Migration deadline in milliseconds when upgrading tiers |

### Website

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `WEBSITE_ENABLED` | bool | `true` | Enable/disable Website service |

## Configuration Summary

- **Total properties**: 337
- **Required (no default)**: 46
- **Optional (has default)**: 291

## Environment Variable Naming Convention

Per FOUNDATION TENETS, all configuration environment variables follow `{SERVICE}_{PROPERTY}` pattern:

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

Per IMPLEMENTATION TENETS, configuration marked as **REQUIRED** will cause the service to
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
