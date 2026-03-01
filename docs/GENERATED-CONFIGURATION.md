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
| `ACCOUNT_AUTO_MANAGE_ANONYMOUS_ROLE` | bool | `true` | When true, automatically manages the anonymous role. If remo... |
| `ACCOUNT_CREATE_LOCK_EXPIRY_SECONDS` | int | `10` | Lock expiry in seconds for account creation email uniqueness... |
| `ACCOUNT_DEFAULT_PAGE_SIZE` | int | `20` | Default page size for list operations when not specified |
| `ACCOUNT_EMAIL_CHANGE_LOCK_EXPIRY_SECONDS` | int | `10` | Lock expiry in seconds for email uniqueness check during ema... |
| `ACCOUNT_LIST_BATCH_SIZE` | int | `100` | Number of accounts to process per batch in list operations |
| `ACCOUNT_MAX_PAGE_SIZE` | int | `100` | Maximum allowed page size for list operations |
| `ACCOUNT_PROVIDER_FILTER_MAX_SCAN_SIZE` | int | `10000` | Maximum number of accounts to scan when filtering by provide... |

### Achievement

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `ACHIEVEMENT_AUTO_SYNC_ON_UNLOCK` | bool | `true` | Automatically sync achievements to platforms when unlocked |
| `ACHIEVEMENT_EARNED_COUNT_RETRY_ATTEMPTS` | int | `3` | Maximum retry attempts for ETag conflicts when incrementing ... |
| `ACHIEVEMENT_LOCK_EXPIRY_SECONDS` | int | `30` | Expiry time in seconds for distributed locks on achievement ... |
| `ACHIEVEMENT_MOCK_PLATFORM_SYNC` | bool | `false` | Enable mock mode for platform sync (returns success without ... |
| `ACHIEVEMENT_PLAYSTATION_CLIENT_ID` | string | **REQUIRED** | PlayStation Network client ID (optional - not implemented) |
| `ACHIEVEMENT_PLAYSTATION_CLIENT_SECRET` | string | **REQUIRED** | PlayStation Network client secret (optional - not implemente... |
| `ACHIEVEMENT_PROGRESS_TTL_SECONDS` | int | `0` | TTL in seconds for progress data in Redis (0 = no expiry, pr... |
| `ACHIEVEMENT_RARE_THRESHOLD_PERCENT` | double | `5.0` | Threshold percentage below which an achievement is considere... |
| `ACHIEVEMENT_RARITY_CALCULATION_INTERVAL_MINUTES` | int | `60` | How often to recalculate achievement rarity percentages |
| `ACHIEVEMENT_RARITY_CALCULATION_STARTUP_DELAY_SECONDS` | int | `30` | Delay in seconds before first rarity calculation (allows ser... |
| `ACHIEVEMENT_RARITY_THRESHOLD_EARNED_COUNT` | int | `100` | Minimum earned count for an achievement to be considered com... |
| `ACHIEVEMENT_STEAM_API_KEY` | string | **REQUIRED** | Steam Web API key for achievement sync (optional - Steam syn... |
| `ACHIEVEMENT_STEAM_APP_ID` | string | **REQUIRED** | Steam App ID for achievement mapping (optional - Steam sync ... |
| `ACHIEVEMENT_SYNC_RETRY_ATTEMPTS` | int | `3` | Number of retry attempts for failed platform syncs |
| `ACHIEVEMENT_SYNC_RETRY_DELAY_SECONDS` | int | `60` | Delay between sync retry attempts in seconds |
| `ACHIEVEMENT_XBOX_CLIENT_ID` | string | **REQUIRED** | Xbox Live client ID (optional - not implemented) |
| `ACHIEVEMENT_XBOX_CLIENT_SECRET` | string | **REQUIRED** | Xbox Live client secret (optional - not implemented) |

### Actor

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `ACTOR_DEFAULT_ACTORS_PER_NODE` | int | `100` | Default capacity per pool node |
| `ACTOR_DEFAULT_AUTOSAVE_INTERVAL_SECONDS` | int | `60` | Default interval in seconds for periodic state saves |
| `ACTOR_DEFAULT_MEMORY_EXPIRATION_MINUTES` | int | `60` | Default expiration time in minutes for actor memories |
| `ACTOR_DEFAULT_TICK_INTERVAL_MS` | int | `100` | Default behavior loop interval in milliseconds |
| `ACTOR_DEPLOYMENT_MODE` | string | `bannou` | Actor deployment mode: bannou (local dev), pool-per-type, sh... |
| `ACTOR_ERROR_RETRY_DELAY_MS` | int | `1000` | Delay in milliseconds before retrying after behavior loop er... |
| `ACTOR_EVENT_BRAIN_DEFAULT_URGENCY` | double | `0.8` | Default urgency for Event Brain instruction perceptions (0.0... |
| `ACTOR_GOAP_MAX_PLAN_DEPTH` | int | `10` | Maximum depth for GOAP planning search |
| `ACTOR_GOAP_PLAN_TIMEOUT_MS` | int | `50` | Maximum time allowed for GOAP planning in milliseconds |
| `ACTOR_GOAP_REPLAN_THRESHOLD` | double | `0.3` | Threshold for triggering GOAP replanning when goal relevance... |
| `ACTOR_HEARTBEAT_INTERVAL_SECONDS` | int | `10` | Pool node heartbeat frequency in seconds |
| `ACTOR_HEARTBEAT_TIMEOUT_SECONDS` | int | `30` | Mark node unhealthy after this many seconds without heartbea... |
| `ACTOR_LOCAL_MODE_APP_ID` | string | `bannou` | App ID used when running in local/bannou deployment mode |
| `ACTOR_LOCAL_MODE_NODE_ID` | string | `bannou-local` | Node ID used when running in local/bannou deployment mode |
| `ACTOR_MAX_POOL_NODES` | int | `10` | Maximum pool nodes allowed (auto-scale mode) |
| `ACTOR_MEMORY_STORE_MAX_RETRIES` | int | `3` | Maximum retry attempts for memory store operations |
| `ACTOR_MIN_POOL_NODES` | int | `1` | Minimum pool nodes to maintain (auto-scale mode) |
| `ACTOR_OPERATION_TIMEOUT_SECONDS` | int | `5` | Timeout in seconds for individual actor operations |
| `ACTOR_PERCEPTION_FILTER_THRESHOLD` | double | `0.1` | Minimum urgency for perception to be processed (0.0-1.0) |
| `ACTOR_PERCEPTION_MEMORY_THRESHOLD` | double | `0.7` | Minimum urgency for perception to become a memory (0.0-1.0) |
| `ACTOR_PERCEPTION_QUEUE_SIZE` | int | `100` | Max perceptions queued per actor before dropping oldest |
| `ACTOR_POOL_CONCURRENCY_MAX_RETRIES` | int | `3` | Maximum retry attempts for optimistic concurrency operations... |
| `ACTOR_POOL_HEALTH_CHECK_INTERVAL_SECONDS` | int | `15` | Interval in seconds between pool health check operations |
| `ACTOR_POOL_HEALTH_MONITOR_STARTUP_DELAY_SECONDS` | int | `5` | Delay in seconds before pool health monitor starts checking ... |
| `ACTOR_POOL_NODE_APP_ID` | string | **REQUIRED** | Mesh app-id for routing commands to this pool node. Required... |
| `ACTOR_POOL_NODE_CAPACITY` | int | `100` | Maximum actors this pool node can run. Overrides DefaultActo... |
| `ACTOR_POOL_NODE_ID` | string | **REQUIRED** | If set, this instance runs as a pool node (not control plane... |
| `ACTOR_POOL_NODE_IMAGE` | string | `bannou-actor-pool:latest` | Docker image for pool nodes (pool-per-type, shared-pool, aut... |
| `ACTOR_POOL_NODE_TYPE` | string | `shared` | Pool type this node belongs to: shared, npc-brain, event-coo... |
| `ACTOR_QUERY_OPTIONS_DEFAULT_MAX_AGE_MS` | int | `5000` | Default max age in milliseconds for cached query options |
| `ACTOR_SCHEDULED_EVENT_CHECK_INTERVAL_MS` | int | `100` | Interval in milliseconds for checking scheduled events |
| `ACTOR_SCHEDULED_EVENT_DEFAULT_URGENCY` | double | `0.7` | Default urgency value for scheduled event perceptions (0.0-1... |
| `ACTOR_SHORT_TERM_MEMORY_MINUTES` | int | `5` | Expiration time in minutes for short-term memories from high... |
| `ACTOR_STATE_PERSISTENCE_RETRY_DELAY_MS` | int | `50` | Base delay in milliseconds between state persistence retry a... |
| `ACTOR_STOP_TIMEOUT_SECONDS` | int | `5` | Timeout in seconds for graceful actor stop operations |

### Analytics

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `ANALYTICS_CONTROLLER_HISTORY_CLEANUP_BATCH_SIZE` | int | `5000` | Maximum records to delete per cleanup invocation |
| `ANALYTICS_CONTROLLER_HISTORY_CLEANUP_SUB_BATCH_SIZE` | int | `100` | Number of records to delete per iteration within a cleanup b... |
| `ANALYTICS_CONTROLLER_HISTORY_RETENTION_DAYS` | int | `90` | Days to retain controller history records (0 = indefinite re... |
| `ANALYTICS_EVENT_BUFFER_FLUSH_INTERVAL_SECONDS` | int | `5` | Interval in seconds to flush event buffer |
| `ANALYTICS_EVENT_BUFFER_LOCK_EXPIRY_BASE_SECONDS` | int | `10` | Base lock expiry time in seconds for event buffer flush oper... |
| `ANALYTICS_EVENT_BUFFER_SIZE` | int | `1000` | Maximum events to buffer before flushing to storage |
| `ANALYTICS_GLICKO2_DEFAULT_DEVIATION` | double | `350.0` | Default rating deviation for new entities (higher = less cer... |
| `ANALYTICS_GLICKO2_DEFAULT_RATING` | double | `1500.0` | Default Glicko-2 rating for new entities |
| `ANALYTICS_GLICKO2_DEFAULT_VOLATILITY` | double | `0.06` | Default volatility for new entities (0.06 is standard) |
| `ANALYTICS_GLICKO2_MAX_RATING` | double | `4000.0` | Maximum allowed Glicko-2 rating (ceiling for clamping) |
| `ANALYTICS_GLICKO2_MAX_VOLATILITY_ITERATIONS` | int | `100` | Maximum iterations for Glicko-2 volatility convergence algor... |
| `ANALYTICS_GLICKO2_MIN_DEVIATION` | double | `30.0` | Minimum rating deviation (prevents overconfidence) |
| `ANALYTICS_GLICKO2_MIN_RATING` | double | `100.0` | Minimum allowed Glicko-2 rating (floor for clamping) |
| `ANALYTICS_GLICKO2_SYSTEM_CONSTANT` | double | `0.5` | Glicko-2 system constant (tau) - controls volatility change ... |
| `ANALYTICS_GLICKO2_VOLATILITY_CONVERGENCE_TOLERANCE` | double | `1e-06` | Convergence tolerance for Glicko-2 volatility iteration (sma... |
| `ANALYTICS_MILESTONE_THRESHOLDS` | string | `10,25,50,100,250,500,1000,2500,5000,10000,25000,50000,100000` | Comma-separated list of score thresholds that trigger milest... |
| `ANALYTICS_RATING_UPDATE_LOCK_EXPIRY_SECONDS` | int | `30` | Lock expiry time in seconds for skill rating update operatio... |
| `ANALYTICS_RESOLUTION_CACHE_TTL_SECONDS` | int | `300` | TTL in seconds for resolution caches (game service, realm, c... |
| `ANALYTICS_SESSION_MAPPING_TTL_SECONDS` | int | `3600` | TTL in seconds for game session mappings (should exceed typi... |

### Asset

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `ASSET_ADDITIONAL_EXTENSION_MAPPINGS` | string | **REQUIRED** | Comma-separated ext=type pairs for additional extension mapp... |
| `ASSET_ADDITIONAL_FORBIDDEN_CONTENT_TYPES` | string | **REQUIRED** | Comma-separated list of additional forbidden content types |
| `ASSET_ADDITIONAL_PROCESSABLE_CONTENT_TYPES` | string | **REQUIRED** | Comma-separated list of additional processable content types... |
| `ASSET_AUDIO_BITRATE_KBPS` | int | `192` | Default audio bitrate in kbps |
| `ASSET_AUDIO_LARGE_FILE_WARNING_THRESHOLD_MB` | int | `100` | File size in MB above which audio processing logs a warning |
| `ASSET_AUDIO_OUTPUT_FORMAT` | string | `mp3` | Default audio output format |
| `ASSET_AUDIO_PRESERVE_LOSSLESS` | bool | `true` | Keep original lossless file alongside transcoded version |
| `ASSET_AUDIO_PROCESSOR_POOL_TYPE` | string | `audio-processor` | Pool type name for audio processing |
| `ASSET_BUNDLE_CLEANUP_INTERVAL_MINUTES` | int | `60` | Interval in minutes between bundle cleanup scans for permane... |
| `ASSET_BUNDLE_CLEANUP_STARTUP_DELAY_SECONDS` | int | `30` | Delay in seconds before the bundle cleanup worker begins its... |
| `ASSET_BUNDLE_COMPRESSION_DEFAULT` | string | `lz4` | Default compression for bundles |
| `ASSET_BUNDLE_CURRENT_PATH_PREFIX` | string | `bundles/current` | Path prefix for finalized bundles in storage bucket |
| `ASSET_BUNDLE_KEY_PREFIX` | string | `bundle:` | Key prefix for bundle entries in state store |
| `ASSET_BUNDLE_UPLOAD_PATH_PREFIX` | string | `bundles/uploads` | Path prefix for bundle upload staging in storage bucket |
| `ASSET_BUNDLE_ZIP_CACHE_PATH_PREFIX` | string | `bundles/zip-cache` | Path prefix for ZIP conversion cache in storage bucket |
| `ASSET_DEFAULT_BUNDLE_CACHE_TTL_HOURS` | int | `24` | Default TTL in hours for bundle cache entries |
| `ASSET_DEFAULT_LIST_LIMIT` | int | `50` | Default number of results per page when client does not spec... |
| `ASSET_DEFAULT_PROCESSOR_POOL_TYPE` | string | `asset-processor` | Default pool type name for general asset processing |
| `ASSET_DELETED_BUNDLE_RETENTION_DAYS` | int | `30` | Number of days to retain soft-deleted bundles before permane... |
| `ASSET_DOWNLOAD_TOKEN_TTL_SECONDS` | int | `900` | TTL for download URLs (can be shorter than upload) |
| `ASSET_FFMPEG_PATH` | string | **REQUIRED** | Path to FFmpeg binary (empty = use system PATH) |
| `ASSET_FFMPEG_WORKING_DIR` | string | `/tmp/bannou-ffmpeg` | Working directory for FFmpeg temporary files |
| `ASSET_FINAL_ASSET_PATH_PREFIX` | string | `assets` | Path prefix for final asset storage in bucket |
| `ASSET_INDEX_KEY_PREFIX` | string | `asset-index:` | Key prefix for asset index entries in state store |
| `ASSET_INDEX_OPTIMISTIC_RETRY_BASE_DELAY_MS` | int | `10` | Base delay in milliseconds between optimistic retry attempts... |
| `ASSET_INDEX_OPTIMISTIC_RETRY_MAX_ATTEMPTS` | int | `5` | Maximum retry attempts for optimistic concurrency index upda... |
| `ASSET_KEY_PREFIX` | string | `asset:` | Key prefix for asset entries in state store |
| `ASSET_LARGE_FILE_THRESHOLD_MB` | int | `50` | File size threshold for delegating to processing pool |
| `ASSET_MAX_BULK_GET_ASSETS` | int | `100` | Maximum number of asset IDs allowed in a single bulk get req... |
| `ASSET_MAX_QUERY_LIMIT` | int | `1000` | Maximum number of results per query/list request |
| `ASSET_MAX_RESOLUTION_ASSETS` | int | `500` | Maximum number of asset IDs allowed in a single bundle resol... |
| `ASSET_MAX_UPLOAD_SIZE_MB` | int | `500` | Maximum upload size in megabytes |
| `ASSET_METABUNDLE_ASYNC_ASSET_COUNT_THRESHOLD` | int | `50` | Total asset count that triggers async processing. Jobs with ... |
| `ASSET_METABUNDLE_ASYNC_SIZE_BYTES_THRESHOLD` | int | `104857600` | Estimated total size in bytes that triggers async processing... |
| `ASSET_METABUNDLE_ASYNC_SOURCE_BUNDLE_THRESHOLD` | int | `3` | Number of source bundles that triggers async processing. Job... |
| `ASSET_METABUNDLE_JOB_KEY_PREFIX` | string | `metabundle-job:` | Key prefix for metabundle job entries in state store |
| `ASSET_METABUNDLE_JOB_TIMEOUT_SECONDS` | int | `3600` | Maximum time for a metabundle job before marking as failed |
| `ASSET_METABUNDLE_JOB_TTL_SECONDS` | int | `86400` | How long job status records are retained after completion (f... |
| `ASSET_MINIO_STARTUP_MAX_RETRIES` | int | `30` | Maximum number of retries when waiting for MinIO connectivit... |
| `ASSET_MINIO_STARTUP_RETRY_DELAY_MS` | int | `2000` | Delay in milliseconds between MinIO startup connectivity ret... |
| `ASSET_MINIO_WEBHOOK_SECRET` | string | **REQUIRED** | Secret for validating MinIO webhook requests |
| `ASSET_MODEL_GENERATE_LODS_DEFAULT` | bool | `true` | Default for whether to generate LOD levels during 3D model p... |
| `ASSET_MODEL_LARGE_FILE_WARNING_THRESHOLD_MB` | int | `50` | File size in MB above which model processing logs a warning |
| `ASSET_MODEL_LOD_LEVELS` | int | `3` | Default number of LOD levels to generate during 3D model pro... |
| `ASSET_MODEL_OPTIMIZE_MESHES_DEFAULT` | bool | `true` | Default for whether to optimize meshes during 3D model proce... |
| `ASSET_MODEL_PROCESSOR_POOL_TYPE` | string | `model-processor` | Pool type name for 3D model processing |
| `ASSET_MULTIPART_PART_SIZE_MB` | int | `16` | Size of each part in multipart uploads in megabytes |
| `ASSET_MULTIPART_THRESHOLD_MB` | int | `50` | File size threshold for multipart uploads in megabytes |
| `ASSET_PROCESSING_BATCH_INTERVAL_SECONDS` | int | `5` | Delay in seconds between batch processing attempts |
| `ASSET_PROCESSING_JOB_MAX_WAIT_SECONDS` | int | `60` | Maximum seconds to wait for a synchronous processing job to ... |
| `ASSET_PROCESSING_MAX_RETRIES` | int | `5` | Maximum retry attempts for asset processing |
| `ASSET_PROCESSING_MODE` | string | `both` | Service mode |
| `ASSET_PROCESSING_POOL_TYPE` | string | `asset-processor` | Processing pool identifier for orchestrator |
| `ASSET_PROCESSING_QUEUE_CHECK_INTERVAL_SECONDS` | int | `30` | Interval in seconds to check processing queue when no jobs a... |
| `ASSET_PROCESSING_RETRY_DELAY_SECONDS` | int | `30` | Delay in seconds between processing retries |
| `ASSET_PROCESSOR_ACQUISITION_TIMEOUT_SECONDS` | int | `600` | Maximum seconds to wait when acquiring a processor from the ... |
| `ASSET_PROCESSOR_AVAILABILITY_MAX_WAIT_SECONDS` | int | `60` | Maximum seconds to wait for processor availability |
| `ASSET_PROCESSOR_AVAILABILITY_POLL_INTERVAL_SECONDS` | int | `2` | Polling interval in seconds when waiting for processor |
| `ASSET_PROCESSOR_HEARTBEAT_INTERVAL_SECONDS` | int | `30` | Heartbeat emission interval in seconds |
| `ASSET_PROCESSOR_HEARTBEAT_TIMEOUT_SECONDS` | int | `90` | Mark node unhealthy after this many seconds without heartbea... |
| `ASSET_PROCESSOR_IDLE_TIMEOUT_SECONDS` | int | `300` | Seconds of zero-load before auto-termination (0 to disable) |
| `ASSET_PROCESSOR_MAX_CONCURRENT_JOBS` | int | `10` | Maximum concurrent jobs per processor node |
| `ASSET_PROCESSOR_NODE_ID` | string | **REQUIRED** | Unique processor node ID (set by orchestrator when spawning ... |
| `ASSET_PROCESSOR_NODE_TTL_SECONDS` | int | `180` | TTL in seconds for processor node state entries in the state... |
| `ASSET_SHUTDOWN_DRAIN_INTERVAL_SECONDS` | int | `2` | Interval in seconds between shutdown drain checks |
| `ASSET_SHUTDOWN_DRAIN_TIMEOUT_MINUTES` | int | `2` | Maximum minutes to allow queue draining during graceful shut... |
| `ASSET_STORAGE_ACCESS_KEY` | string | **REQUIRED** | Storage access key/username. Required for production deploym... |
| `ASSET_STORAGE_BUCKET` | string | `bannou-assets` | Primary bucket/container name for assets |
| `ASSET_STORAGE_ENDPOINT` | string | `minio:9000` | Storage endpoint host:port for internal service connections ... |
| `ASSET_STORAGE_FORCE_PATH_STYLE` | bool | `true` | Force path-style URLs (required for MinIO) |
| `ASSET_STORAGE_PROVIDER` | string | `minio` | Storage backend type |
| `ASSET_STORAGE_PUBLIC_ENDPOINT` | string | **REQUIRED** | Public endpoint for pre-signed URLs accessible by clients. I... |
| `ASSET_STORAGE_REGION` | string | `us-east-1` | Storage region (for S3/R2) |
| `ASSET_STORAGE_SECRET_KEY` | string | **REQUIRED** | Storage secret key/password. Required for production deploym... |
| `ASSET_STORAGE_USE_SSL` | bool | `false` | Use SSL/TLS for storage connections |
| `ASSET_STREAMING_COMPRESSION_BUFFER_KB` | int | `16384` | Size of compression buffer in KB for LZ4 streaming compressi... |
| `ASSET_STREAMING_MAX_CONCURRENT_SOURCE_STREAMS` | int | `2` | Maximum number of source bundles to stream concurrently duri... |
| `ASSET_STREAMING_MAX_MEMORY_MB` | int | `100` | Maximum memory in MB for streaming operations. Limits total ... |
| `ASSET_STREAMING_PART_SIZE_MB` | int | `50` | Size of each part in MB for streaming multipart uploads. S3/... |
| `ASSET_STREAMING_PROGRESS_UPDATE_INTERVAL_ASSETS` | int | `10` | Number of assets to process before updating job progress. Lo... |
| `ASSET_TEMP_UPLOAD_PATH_PREFIX` | string | `temp` | Path prefix for temporary upload staging in storage bucket |
| `ASSET_TEXTURE_DEFAULT_OUTPUT_FORMAT` | string | `webp` | Default output format for texture processing when not specif... |
| `ASSET_TEXTURE_LARGE_FILE_WARNING_THRESHOLD_MB` | int | `100` | File size in MB above which texture processing logs a warnin... |
| `ASSET_TEXTURE_MAX_DIMENSION` | int | `4096` | Default maximum texture dimension in pixels when not specifi... |
| `ASSET_TEXTURE_PROCESSOR_POOL_TYPE` | string | `texture-processor` | Pool type name for texture processing |
| `ASSET_TOKEN_TTL_SECONDS` | int | `3600` | TTL for pre-signed upload/download URLs in seconds |
| `ASSET_UPLOAD_SESSION_KEY_PREFIX` | string | `upload:` | Key prefix for upload session entries in state store |
| `ASSET_WORKER_POOL` | string | **REQUIRED** | Worker pool identifier when running in worker mode |
| `ASSET_ZIP_CACHE_CLEANUP_INTERVAL_MINUTES` | int | `120` | Interval in minutes between ZIP cache cleanup scans for remo... |
| `ASSET_ZIP_CACHE_CLEANUP_STARTUP_DELAY_SECONDS` | int | `60` | Delay in seconds before the ZIP cache cleanup worker begins ... |
| `ASSET_ZIP_CACHE_DIRECTORY` | string | **REQUIRED** | Directory for ZIP cache files. When null, uses the system te... |
| `ASSET_ZIP_CACHE_TTL_HOURS` | int | `24` | TTL for cached ZIP conversions in hours |

### Auth

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `AUTH_BCRYPT_WORK_FACTOR` | int | `12` | BCrypt work factor for password hashing. Higher values are m... |
| `AUTH_CLOUDFLARE_ACCOUNT_ID` | string | **REQUIRED** | CloudFlare account ID for KV API access. Required when Cloud... |
| `AUTH_CLOUDFLARE_API_TOKEN` | string | **REQUIRED** | CloudFlare API token with Workers KV write permissions. Use ... |
| `AUTH_CLOUDFLARE_EDGE_ENABLED` | bool | `false` | Enable CloudFlare Workers KV edge revocation. Requires Cloud... |
| `AUTH_CLOUDFLARE_KV_NAMESPACE_ID` | string | **REQUIRED** | CloudFlare KV namespace ID where revoked tokens are stored. ... |
| `AUTH_CONNECT_URL` | string | `ws://localhost:5014/connect` | URL to Connect service for WebSocket connections. Defaults t... |
| `AUTH_DISCORD_CLIENT_ID` | string | **REQUIRED** | Discord OAuth client ID |
| `AUTH_DISCORD_CLIENT_SECRET` | string | **REQUIRED** | Discord OAuth client secret |
| `AUTH_DISCORD_REDIRECT_URI` | string | **REQUIRED** | Discord OAuth redirect URI. Optional if BANNOU_SERVICE_DOMAI... |
| `AUTH_EDGE_REVOCATION_ENABLED` | bool | `false` | Master switch for edge-layer token revocation. When enabled,... |
| `AUTH_EDGE_REVOCATION_MAX_RETRY_ATTEMPTS` | int | `3` | Maximum retry attempts for failed edge pushes before giving ... |
| `AUTH_EDGE_REVOCATION_TIMEOUT_SECONDS` | int | `5` | Timeout in seconds for edge provider push operations. Operat... |
| `AUTH_EMAIL_FROM_ADDRESS` | string | **REQUIRED** | Sender email address for outgoing emails. Required when Emai... |
| `AUTH_EMAIL_FROM_NAME` | string | **REQUIRED** | Display name for the sender in outgoing emails (e.g., 'Banno... |
| `AUTH_EMAIL_PROVIDER` | string | `none` | Email delivery provider (none=console logging, sendgrid=Send... |
| `AUTH_GOOGLE_CLIENT_ID` | string | **REQUIRED** | Google OAuth client ID |
| `AUTH_GOOGLE_CLIENT_SECRET` | string | **REQUIRED** | Google OAuth client secret |
| `AUTH_GOOGLE_REDIRECT_URI` | string | **REQUIRED** | Google OAuth redirect URI. Optional if BANNOU_SERVICE_DOMAIN... |
| `AUTH_JWT_EXPIRATION_MINUTES` | int | `60` | JWT token expiration time in minutes |
| `AUTH_LOGIN_LOCKOUT_MINUTES` | int | `15` | Duration in minutes to lock out an email after exceeding Max... |
| `AUTH_MAX_LOGIN_ATTEMPTS` | int | `5` | Maximum failed login attempts before lockout. After this man... |
| `AUTH_MFA_CHALLENGE_TTL_MINUTES` | int | `5` | TTL in minutes for MFA challenge tokens issued during login ... |
| `AUTH_MFA_ENCRYPTION_KEY` | string | **REQUIRED** | AES-256-GCM encryption key for TOTP secrets at rest. Require... |
| `AUTH_MFA_ISSUER_NAME` | string | `Bannou` | Issuer name displayed in authenticator apps (appears as serv... |
| `AUTH_MOCK_DISCORD_ID` | string | `mock-discord-123456` | Mock Discord user ID for testing |
| `AUTH_MOCK_GOOGLE_ID` | string | `mock-google-123456` | Mock Google user ID for testing |
| `AUTH_MOCK_PROVIDERS` | bool | `false` | Enable mock OAuth providers for testing |
| `AUTH_MOCK_STEAM_ID` | string | `76561198000000000` | Mock Steam user ID for testing |
| `AUTH_MOCK_TWITCH_ID` | string | `mock-twitch-123456` | Mock Twitch user ID for testing |
| `AUTH_OPENRESTY_EDGE_ENABLED` | bool | `false` | Enable OpenResty/NGINX edge revocation verification. When en... |
| `AUTH_PASSWORD_RESET_BASE_URL` | string | **REQUIRED** | Base URL for password reset page |
| `AUTH_PASSWORD_RESET_TOKEN_TTL_MINUTES` | int | `30` | Password reset token expiration time in minutes |
| `AUTH_SENDGRID_API_KEY` | string | **REQUIRED** | SendGrid API key. Required when EmailProvider is 'sendgrid'. |
| `AUTH_SESSION_TOKEN_TTL_DAYS` | int | `7` | Session token TTL in days for persistent sessions |
| `AUTH_SES_ACCESS_KEY_ID` | string | **REQUIRED** | AWS access key ID for SES API authentication. Required when ... |
| `AUTH_SES_REGION` | string | `us-east-1` | AWS region for SES API (e.g., us-east-1, eu-west-1). SES mus... |
| `AUTH_SES_SECRET_ACCESS_KEY` | string | **REQUIRED** | AWS secret access key for SES API authentication. Required w... |
| `AUTH_SMTP_HOST` | string | **REQUIRED** | SMTP server hostname. Required when EmailProvider is 'smtp'. |
| `AUTH_SMTP_PASSWORD` | string | **REQUIRED** | SMTP authentication password. Optional if server allows anon... |
| `AUTH_SMTP_PORT` | int | `587` | SMTP server port (587 for STARTTLS, 465 for implicit SSL, 25... |
| `AUTH_SMTP_USERNAME` | string | **REQUIRED** | SMTP authentication username. Optional if server allows anon... |
| `AUTH_SMTP_USE_SSL` | bool | `true` | Use SSL/TLS when connecting to SMTP server. When true with p... |
| `AUTH_STEAM_API_KEY` | string | **REQUIRED** | Steam Web API key for session ticket validation |
| `AUTH_STEAM_APP_ID` | string | **REQUIRED** | Steam application ID |
| `AUTH_TWITCH_CLIENT_ID` | string | **REQUIRED** | Twitch OAuth client ID |
| `AUTH_TWITCH_CLIENT_SECRET` | string | **REQUIRED** | Twitch OAuth client secret |
| `AUTH_TWITCH_REDIRECT_URI` | string | **REQUIRED** | Twitch OAuth redirect URI. Optional if BANNOU_SERVICE_DOMAIN... |

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
| `BEHAVIOR_MEMORY_STORE_MAX_RETRIES` | int | `3` | Max retries for memory store operations |
| `BEHAVIOR_METADATA_KEY_PREFIX` | string | `behavior-metadata:` | Key prefix for behavior metadata entries |

### Character

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `CHARACTER_CLEANUP_GRACE_PERIOD_DAYS` | int | `30` | Grace period in days before cleanup of dead character refere... |
| `CHARACTER_DEFAULT_PAGE_SIZE` | int | `20` | Default page size when not specified |
| `CHARACTER_LOCK_TIMEOUT_SECONDS` | int | `30` | Timeout in seconds for acquiring distributed locks during ch... |
| `CHARACTER_MAX_PAGE_SIZE` | int | `100` | Maximum page size for list queries |
| `CHARACTER_REALM_INDEX_UPDATE_MAX_RETRIES` | int | `3` | Maximum retry attempts when updating realm character index (... |
| `CHARACTER_REF_COUNT_UPDATE_MAX_RETRIES` | int | `3` | Maximum retry attempts when updating reference count data (o... |

### Character Encounter

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `CHARACTER_ENCOUNTER_CACHE_MAX_RESULTS_PER_QUERY` | int | `50` | Maximum encounter results loaded per cache query for Actor b... |
| `CHARACTER_ENCOUNTER_CACHE_TTL_MINUTES` | int | `5` | TTL in minutes for the in-memory encounter data cache used b... |
| `CHARACTER_ENCOUNTER_DEFAULT_MEMORY_STRENGTH` | double | `1.0` | Default initial memory strength for new perspectives (0.0-1.... |
| `CHARACTER_ENCOUNTER_DEFAULT_PAGE_SIZE` | int | `20` | Default page size for query results |
| `CHARACTER_ENCOUNTER_DUPLICATE_TIMESTAMP_TOLERANCE_MINUTES` | int | `5` | Time window in minutes for duplicate encounter detection. En... |
| `CHARACTER_ENCOUNTER_MAX_BATCH_SIZE` | int | `100` | Maximum items in bulk operations (batch-get, etc.) |
| `CHARACTER_ENCOUNTER_MAX_PAGE_SIZE` | int | `100` | Maximum allowed page size for query results |
| `CHARACTER_ENCOUNTER_MAX_PARTICIPANTS_PER_ENCOUNTER` | int | `20` | Maximum participants allowed per encounter (caps O(N^2) pair... |
| `CHARACTER_ENCOUNTER_MAX_PER_CHARACTER` | int | `1000` | Maximum encounters stored per character before oldest are pr... |
| `CHARACTER_ENCOUNTER_MAX_PER_PAIR` | int | `100` | Maximum encounters stored per character pair before oldest a... |
| `CHARACTER_ENCOUNTER_MEMORY_DECAY_ENABLED` | bool | `true` | Enable time-based memory decay for encounter perspectives |
| `CHARACTER_ENCOUNTER_MEMORY_DECAY_INTERVAL_HOURS` | int | `24` | Hours between decay checks (used for calculating decay amoun... |
| `CHARACTER_ENCOUNTER_MEMORY_DECAY_MODE` | string | `lazy` | Memory decay mode - 'lazy' applies decay on access, 'schedul... |
| `CHARACTER_ENCOUNTER_MEMORY_DECAY_RATE` | double | `0.05` | Memory strength reduction per decay interval (0.0-1.0) |
| `CHARACTER_ENCOUNTER_MEMORY_FADE_THRESHOLD` | double | `0.1` | Memory strength below which encounters are considered forgot... |
| `CHARACTER_ENCOUNTER_MEMORY_REFRESH_BOOST` | double | `0.2` | Default memory strength boost when refreshing (0.0-1.0) |
| `CHARACTER_ENCOUNTER_SCHEDULED_DECAY_CHECK_INTERVAL_MINUTES` | int | `60` | Interval between scheduled decay checks in minutes (only use... |
| `CHARACTER_ENCOUNTER_SCHEDULED_DECAY_STARTUP_DELAY_SECONDS` | int | `30` | Startup delay before first scheduled decay check in seconds ... |
| `CHARACTER_ENCOUNTER_SEED_BUILTIN_TYPES_ON_STARTUP` | bool | `true` | Automatically seed built-in encounter types on service start... |
| `CHARACTER_ENCOUNTER_SENTIMENT_SHIFT_MEMORABLE` | double | `0.1` | Default sentiment shift for memorable encounter outcomes |
| `CHARACTER_ENCOUNTER_SENTIMENT_SHIFT_NEGATIVE` | double | `-0.2` | Default sentiment shift for negative encounter outcomes (sho... |
| `CHARACTER_ENCOUNTER_SENTIMENT_SHIFT_POSITIVE` | double | `0.2` | Default sentiment shift for positive encounter outcomes |
| `CHARACTER_ENCOUNTER_SENTIMENT_SHIFT_TRANSFORMATIVE` | double | `0.3` | Default sentiment shift for transformative encounter outcome... |

### Character History

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `CHARACTER_HISTORY_BACKSTORY_CACHE_TTL_SECONDS` | int | `600` | TTL in seconds for backstory cache entries. Backstory data i... |
| `CHARACTER_HISTORY_INDEX_LOCK_TIMEOUT_SECONDS` | int | `15` | Timeout in seconds for distributed locks during index and ba... |
| `CHARACTER_HISTORY_MAX_BACKSTORY_ELEMENTS` | int | `100` | Maximum number of backstory elements allowed per character. ... |

### Character Personality

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `CHARACTER_PERSONALITY_BASE_EVOLUTION_PROBABILITY` | double | `0.15` | Base chance for trait shift per evolution event (0.0-1.0) |
| `CHARACTER_PERSONALITY_CACHE_TTL_MINUTES` | int | `5` | TTL in minutes for personality and combat preferences cache ... |
| `CHARACTER_PERSONALITY_COMBAT_DEFEAT_STYLE_TRANSITION_PROBABILITY` | double | `0.4` | Probability for combat style transitions after defeat (0.0-1... |
| `CHARACTER_PERSONALITY_COMBAT_DEFENSIVE_SHIFT_PROBABILITY` | double | `0.5` | Probability for defensive shift after injury (0.0-1.0) |
| `CHARACTER_PERSONALITY_COMBAT_INTENSE_SHIFT_MULTIPLIER` | double | `1.5` | Multiplier for intense stat shifts (near-death, heavy defeat... |
| `CHARACTER_PERSONALITY_COMBAT_MILDEST_SHIFT_MULTIPLIER` | double | `0.3` | Multiplier for mildest stat shifts (minor injuries) |
| `CHARACTER_PERSONALITY_COMBAT_MILD_SHIFT_MULTIPLIER` | double | `0.5` | Multiplier for mild stat shifts (standard victories/defeats) |
| `CHARACTER_PERSONALITY_COMBAT_ROLE_TRANSITION_PROBABILITY` | double | `0.4` | Base probability for combat role transitions (0.0-1.0) |
| `CHARACTER_PERSONALITY_COMBAT_STYLE_TRANSITION_PROBABILITY` | double | `0.3` | Base probability for combat style transitions (0.0-1.0) |
| `CHARACTER_PERSONALITY_COMBAT_VICTORY_BALANCED_TRANSITION_PROBABILITY` | double | `0.2` | Probability for balanced style to become aggressive after vi... |
| `CHARACTER_PERSONALITY_MAX_BATCH_SIZE` | int | `100` | Maximum number of characters allowed in batch operations |
| `CHARACTER_PERSONALITY_MAX_CONCURRENCY_RETRIES` | int | `3` | Maximum retry attempts for optimistic concurrency conflicts |
| `CHARACTER_PERSONALITY_MAX_TRAIT_SHIFT` | double | `0.1` | Maximum magnitude of trait change per evolution event |
| `CHARACTER_PERSONALITY_MIN_TRAIT_SHIFT` | double | `0.02` | Minimum magnitude of trait change per evolution event |

### Chat

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `CHAT_BAN_EXPIRY_BATCH_SIZE` | int | `1000` | Maximum expired ban records processed per worker cycle |
| `CHAT_BAN_EXPIRY_INTERVAL_MINUTES` | int | `60` | How often the background worker checks for expired bans |
| `CHAT_BAN_EXPIRY_LOCK_EXPIRY_SECONDS` | int | `120` | Distributed lock expiry for ban expiry batch cycle |
| `CHAT_BAN_EXPIRY_STARTUP_DELAY_SECONDS` | int | `30` | Initial delay before the ban expiry worker begins its first ... |
| `CHAT_CONTRACT_ROOM_QUERY_BATCH_SIZE` | int | `100` | Page size for paginated contract-room queries during contrac... |
| `CHAT_DEFAULT_CONTRACT_BREACH_ACTION` | string | `Lock` | Default action when a breach is detected on governing contra... |
| `CHAT_DEFAULT_CONTRACT_EXPIRED_ACTION` | string | `Archive` | Default action when governing contract expires naturally |
| `CHAT_DEFAULT_CONTRACT_FULFILLED_ACTION` | string | `Archive` | Default action when governing contract is fulfilled |
| `CHAT_DEFAULT_CONTRACT_TERMINATED_ACTION` | string | `Delete` | Default action when governing contract is terminated early |
| `CHAT_DEFAULT_MAX_PARTICIPANTS_PER_ROOM` | int | `100` | Default participant limit when room type does not specify |
| `CHAT_DEFAULT_RATE_LIMIT_PER_MINUTE` | int | `60` | Default messages per minute per participant when room type d... |
| `CHAT_EPHEMERAL_MESSAGE_TTL_MINUTES` | int | `60` | TTL for messages in ephemeral (Redis) rooms |
| `CHAT_IDLE_ROOM_CLEANUP_INTERVAL_MINUTES` | int | `60` | How often the background worker checks for idle rooms |
| `CHAT_IDLE_ROOM_CLEANUP_LOCK_EXPIRY_SECONDS` | int | `120` | Distributed lock expiry for idle room cleanup batch cycle |
| `CHAT_IDLE_ROOM_CLEANUP_STARTUP_DELAY_SECONDS` | int | `30` | Initial delay before the idle room cleanup worker begins its... |
| `CHAT_IDLE_ROOM_TIMEOUT_MINUTES` | int | `1440` | Minutes of inactivity before a room is eligible for auto-cle... |
| `CHAT_LOCK_EXPIRY_SECONDS` | int | `15` | Distributed lock expiry for chat room and participant mutati... |
| `CHAT_MAX_CONTRACT_ROOM_QUERY_RESULTS` | int | `1000` | Safety cap on total rooms processed per contract lifecycle e... |
| `CHAT_MAX_PINNED_MESSAGES_PER_ROOM` | int | `10` | Maximum pinned messages per room |
| `CHAT_MAX_ROOM_TYPES_PER_GAME_SERVICE` | int | `50` | Maximum custom room types per game service |
| `CHAT_MESSAGE_HISTORY_PAGE_SIZE` | int | `50` | Default page size for message history queries |
| `CHAT_MESSAGE_RETENTION_BATCH_SIZE` | int | `500` | Maximum expired messages to delete per room per cleanup cycl... |
| `CHAT_MESSAGE_RETENTION_CLEANUP_INTERVAL_MINUTES` | int | `360` | How often the background worker checks for expired persisten... |
| `CHAT_MESSAGE_RETENTION_LOCK_EXPIRY_SECONDS` | int | `300` | Distributed lock expiry for message retention cleanup cycle |
| `CHAT_MESSAGE_RETENTION_MAX_ROOMS_PER_TYPE` | int | `1000` | Maximum rooms per type to process per retention cleanup cycl... |
| `CHAT_MESSAGE_RETENTION_MAX_ROOM_TYPE_RESULTS` | int | `1000` | Maximum room types with retention configuration to process p... |
| `CHAT_MESSAGE_RETENTION_STARTUP_DELAY_SECONDS` | int | `60` | Initial delay before the message retention cleanup worker be... |
| `CHAT_SERVER_SALT` | string | `bannou-dev-chat-salt-change-in-production` | Server salt for session shortcut GUID generation per IMPLEME... |
| `CHAT_TYPING_TIMEOUT_SECONDS` | int | `5` | Seconds of inactivity before typing indicator auto-expires |
| `CHAT_TYPING_WORKER_BATCH_SIZE` | int | `100` | Maximum expired entries processed per worker cycle |
| `CHAT_TYPING_WORKER_INTERVAL_MILLISECONDS` | int | `1000` | How often the typing expiry worker checks for stale typing e... |

### Collection

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `COLLECTION_CACHE_TTL_SECONDS` | int | `300` | Redis TTL for collection state cache in seconds |
| `COLLECTION_DEFAULT_PAGE_SIZE` | int | `20` | Default page size for paginated queries |
| `COLLECTION_LOCK_TIMEOUT_SECONDS` | int | `30` | Distributed lock TTL for collection mutations in seconds |
| `COLLECTION_MAX_COLLECTIONS_PER_OWNER` | int | `20` | Maximum number of collections a single owner entity can have |
| `COLLECTION_MAX_CONCURRENCY_RETRIES` | int | `3` | Maximum retry attempts for optimistic concurrency conflicts |
| `COLLECTION_MAX_ENTRIES_PER_COLLECTION` | int | `500` | Maximum number of unlocked entries per collection instance |

### Connect

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `CONNECT_BROADCAST_HEARTBEAT_INTERVAL_SECONDS` | int | `30` | Interval in seconds between broadcast registry heartbeat ref... |
| `CONNECT_BROADCAST_INTERNAL_URL` | string | **REQUIRED** | WebSocket URL where this instance accepts inter-node broadca... |
| `CONNECT_BROADCAST_STALE_THRESHOLD_SECONDS` | int | `90` | Threshold in seconds after which a broadcast registry entry ... |
| `CONNECT_BUFFER_SIZE` | int | `65536` | Size of message buffers in bytes |
| `CONNECT_COMPANION_ROOM_MODE` | string | `Disabled` | How Connect manages companion chat rooms (Disabled, AutoJoin... |
| `CONNECT_COMPRESSION_ENABLED` | bool | `false` | Enable Brotli compression for outbound WebSocket payloads ab... |
| `CONNECT_COMPRESSION_QUALITY` | int | `1` | Brotli compression quality level (0=no compression, 1=fastes... |
| `CONNECT_COMPRESSION_THRESHOLD_BYTES` | int | `1024` | Minimum payload size in bytes before compression is applied ... |
| `CONNECT_CONNECTION_CLEANUP_INTERVAL_SECONDS` | int | `30` | Interval in seconds between connection cleanup runs |
| `CONNECT_CONNECTION_MODE` | string | `External` | Connection mode: External (default, no broadcast), Relayed (... |
| `CONNECT_CONNECTION_SHUTDOWN_TIMEOUT_SECONDS` | int | `5` | Timeout in seconds when waiting for connection closure durin... |
| `CONNECT_DEFAULT_RPC_TIMEOUT_SECONDS` | int | `30` | Default timeout in seconds for RPC calls when not specified |
| `CONNECT_ENABLE_CLIENT_TO_CLIENT_ROUTING` | bool | `true` | Enable routing messages between WebSocket clients |
| `CONNECT_HEARTBEAT_INTERVAL_SECONDS` | int | `30` | Interval between heartbeat messages in seconds |
| `CONNECT_HEARTBEAT_TTL_SECONDS` | int | `300` | Heartbeat data TTL in Redis in seconds (default 5 minutes) |
| `CONNECT_INACTIVE_CONNECTION_TIMEOUT_MINUTES` | int | `30` | Timeout in minutes after which inactive connections are clea... |
| `CONNECT_INTERNAL_AUTH_MODE` | string | `ServiceToken` | Auth mode for internal connections: ServiceToken (validate X... |
| `CONNECT_INTERNAL_SERVICE_TOKEN` | string | **REQUIRED** | Secret for X-Service-Token validation when InternalAuthMode ... |
| `CONNECT_MAX_CHANNEL_NUMBER` | int | `1000` | Maximum allowed channel number in WebSocket binary messages.... |
| `CONNECT_MAX_CONCURRENT_CONNECTIONS` | int | `10000` | Maximum number of concurrent WebSocket connections |
| `CONNECT_MAX_MESSAGES_PER_MINUTE` | int | `1000` | Rate limit for messages per minute per client |
| `CONNECT_MULTINODE_BROADCAST_MODE` | string | `None` | Multi-node broadcast directionality (None disables broadcast... |
| `CONNECT_PENDING_MESSAGE_TIMEOUT_SECONDS` | int | `30` | Timeout in seconds for pending messages awaiting acknowledgm... |
| `CONNECT_RATE_LIMIT_WINDOW_MINUTES` | int | `1` | Rate limit window in minutes |
| `CONNECT_RECONNECTION_WINDOW_EXTENSION_MINUTES` | int | `1` | Additional minutes added to reconnection window on each exte... |
| `CONNECT_RECONNECTION_WINDOW_SECONDS` | int | `300` | Window for client reconnection after disconnect in seconds (... |
| `CONNECT_RPC_CLEANUP_INTERVAL_SECONDS` | int | `30` | Interval in seconds between pending RPC cleanup runs |
| `CONNECT_SERVER_SALT` | string | `bannou-dev-connect-salt-change-in-production` | Server salt for client GUID generation. Must be shared acros... |
| `CONNECT_SESSION_TTL_SECONDS` | int | `86400` | Session time-to-live in seconds (default 24 hours) |

### Contract

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `CONTRACT_DEFAULT_CONSENT_TIMEOUT_DAYS` | int | `7` | Default number of days for parties to consent before proposa... |
| `CONTRACT_DEFAULT_ENFORCEMENT_MODE` | string | `event_only` | Default enforcement mode for contracts |
| `CONTRACT_DEFAULT_PAGE_SIZE` | int | `20` | Default page size for paginated endpoints when not specified... |
| `CONTRACT_IDEMPOTENCY_TTL_SECONDS` | int | `86400` | TTL in seconds for idempotency key storage (default 24 hours... |
| `CONTRACT_INDEX_LOCK_FAILURE_MODE` | string | `warn` | Behavior when index lock acquisition fails (warn=continue, f... |
| `CONTRACT_INDEX_LOCK_TIMEOUT_SECONDS` | int | `15` | Lock timeout in seconds for index update distributed locks |
| `CONTRACT_LOCK_TIMEOUT_SECONDS` | int | `60` | Lock timeout in seconds for contract-level distributed locks |
| `CONTRACT_MAX_ACTIVE_CONTRACTS_PER_ENTITY` | int | `100` | Maximum active contracts per entity (0 for unlimited) |
| `CONTRACT_MAX_MILESTONES_PER_TEMPLATE` | int | `50` | Maximum number of milestones allowed in a template |
| `CONTRACT_MAX_PARTIES_PER_CONTRACT` | int | `20` | Maximum number of parties allowed in a single contract |
| `CONTRACT_MAX_PREBOUND_APIS_PER_MILESTONE` | int | `10` | Maximum number of prebound APIs per milestone |
| `CONTRACT_MILESTONE_DEADLINE_CHECK_INTERVAL_SECONDS` | int | `300` | Interval between contract expiration checks in seconds, cove... |
| `CONTRACT_MILESTONE_DEADLINE_STARTUP_DELAY_SECONDS` | int | `30` | Startup delay before first contract expiration check in seco... |
| `CONTRACT_PREBOUND_API_BATCH_SIZE` | int | `10` | Number of prebound APIs to execute in parallel |
| `CONTRACT_PREBOUND_API_TIMEOUT_MS` | int | `30000` | Timeout for individual prebound API calls in milliseconds |
| `CONTRACT_TERMS_MERGE_MODE` | string | `shallow` | How instance terms merge with template terms (shallow=replac... |

### Currency

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `CURRENCY_AUTOGAIN_BATCH_SIZE` | int | `1000` | For task mode - batch size per processing cycle |
| `CURRENCY_AUTOGAIN_PROCESSING_MODE` | string | `lazy` | How autogain is calculated (lazy = on-demand at query time, ... |
| `CURRENCY_AUTOGAIN_TASK_INTERVAL_MS` | int | `60000` | For task mode - how often to process autogain in millisecond... |
| `CURRENCY_AUTOGAIN_TASK_STARTUP_DELAY_SECONDS` | int | `15` | Delay in seconds before first autogain task cycle (allows se... |
| `CURRENCY_BALANCE_CACHE_TTL_SECONDS` | int | `60` | TTL in seconds for balance cache entries |
| `CURRENCY_BALANCE_LOCK_TIMEOUT_SECONDS` | int | `30` | Timeout in seconds for balance-level distributed locks |
| `CURRENCY_CONVERSION_ROUNDING_PRECISION` | int | `8` | Number of decimal places for currency conversion rounding |
| `CURRENCY_DEFAULT_ALLOW_NEGATIVE` | bool | `false` | Default for currencies that do not specify allowNegative |
| `CURRENCY_EXCHANGE_RATE_UPDATE_MAX_RETRIES` | int | `3` | Maximum retry attempts for exchange rate update with optimis... |
| `CURRENCY_HOLD_CACHE_TTL_SECONDS` | int | `120` | TTL in seconds for hold cache entries |
| `CURRENCY_HOLD_LOCK_TIMEOUT_SECONDS` | int | `30` | Timeout in seconds for hold-level distributed locks |
| `CURRENCY_HOLD_MAX_DURATION_DAYS` | int | `7` | Maximum duration for authorization holds in days |
| `CURRENCY_IDEMPOTENCY_TTL_SECONDS` | int | `3600` | How long to cache idempotency keys in seconds |
| `CURRENCY_INDEX_LOCK_MAX_RETRIES` | int | `3` | Maximum retry attempts for acquiring index update locks befo... |
| `CURRENCY_INDEX_LOCK_TIMEOUT_SECONDS` | int | `15` | Timeout in seconds for index update distributed locks |
| `CURRENCY_TRANSACTION_RETENTION_DAYS` | int | `365` | How many days to retain detailed transaction history |
| `CURRENCY_WALLET_LOCK_TIMEOUT_SECONDS` | int | `30` | Timeout in seconds for wallet-level distributed locks |

### Divine

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `DIVINE_ATTENTION_DECAY_INTERVAL_MINUTES` | int | `60` | Minutes between attention slot decay evaluations for inactiv... |
| `DIVINE_ATTENTION_IMPRESSION_THRESHOLD` | double | `0.1` | Minimum cumulative impression below which an attention slot ... |
| `DIVINE_ATTENTION_WORKER_INTERVAL_SECONDS` | int | `60` | Seconds between attention decay worker evaluation cycles |
| `DIVINE_BLESSING_COLLECTION_TYPE` | string | `divine_blessings` | Collection type code for permanent blessings via lib-collect... |
| `DIVINE_BLESSING_STATUS_CATEGORY` | string | `divine_blessing` | Status category code for temporary blessings via Status Inve... |
| `DIVINE_DEFAULT_MAX_ATTENTION_SLOTS` | int | `10` | Default maximum characters a deity can actively monitor simu... |
| `DIVINE_DEITY_ACTOR_TYPE_CODE` | string | `deity_watcher` | Actor type code used when starting deity watcher actors via ... |
| `DIVINE_DEITY_SEED_TYPE_CODE` | string | `deity_domain` | Seed type code for deity domain power growth |
| `DIVINE_DIVINITY_COST_GREATER` | double | `200.0` | Divinity cost for granting a Greater tier blessing |
| `DIVINE_DIVINITY_COST_MINOR` | double | `10.0` | Divinity cost for granting a Minor tier blessing |
| `DIVINE_DIVINITY_COST_STANDARD` | double | `50.0` | Divinity cost for granting a Standard tier blessing |
| `DIVINE_DIVINITY_COST_SUPREME` | double | `1000.0` | Divinity cost for granting a Supreme tier blessing |
| `DIVINE_DIVINITY_CURRENCY_CODE` | string | `divinity` | Currency code used for divinity economy within each game ser... |
| `DIVINE_DIVINITY_GENERATION_MULTIPLIER` | double | `1.0` | Global multiplier applied to all divinity generation from mo... |
| `DIVINE_DIVINITY_GENERATION_WORKER_INTERVAL_SECONDS` | int | `30` | Seconds between divinity generation worker processing cycles |
| `DIVINE_FOLLOWER_RELATIONSHIP_TYPE_CODE` | string | `deity_follower` | Relationship type code for deity-to-character follower bonds |
| `DIVINE_MAX_BLESSINGS_PER_ENTITY` | int | `10` | Maximum active blessings an entity can hold simultaneously |
| `DIVINE_RIVALRY_RELATIONSHIP_TYPE_CODE` | string | `deity_rivalry` | Relationship type code for deity-to-deity rivalry bonds |

### Documentation

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `DOCUMENTATION_BULK_OPERATION_BATCH_SIZE` | int | `10` | Maximum documents processed per bulk operation |
| `DOCUMENTATION_ESTIMATED_BYTES_PER_DOCUMENT` | int | `10000` | Estimated average document content size in bytes for stats c... |
| `DOCUMENTATION_GIT_CLONE_TIMEOUT_SECONDS` | int | `300` | Clone/pull operation timeout in seconds |
| `DOCUMENTATION_GIT_STORAGE_CLEANUP_HOURS` | int | `24` | Hours before inactive repos are cleaned up |
| `DOCUMENTATION_GIT_STORAGE_PATH` | string | `/tmp/bannou-git-repos` | Local path for cloned git repositories |
| `DOCUMENTATION_MAX_CONCURRENT_SYNCS` | int | `3` | Maximum concurrent sync operations |
| `DOCUMENTATION_MAX_CONTENT_SIZE_BYTES` | int | `524288` | Maximum document content size in bytes (500KB default) |
| `DOCUMENTATION_MAX_DOCUMENTS_PER_SYNC` | int | `1000` | Maximum documents per sync operation |
| `DOCUMENTATION_MAX_FETCH_LIMIT` | int | `1000` | Maximum documents to fetch when filtering/sorting in memory |
| `DOCUMENTATION_MAX_IMPORT_DOCUMENTS` | int | `0` | Maximum documents per import (0 = unlimited) |
| `DOCUMENTATION_MAX_RELATED_DOCUMENTS` | int | `5` | Maximum related documents to return for standard depth |
| `DOCUMENTATION_MAX_RELATED_DOCUMENTS_EXTENDED` | int | `10` | Maximum related documents to return for extended depth |
| `DOCUMENTATION_MAX_SEARCH_RESULTS` | int | `20` | Maximum search results to return |
| `DOCUMENTATION_MIN_RELEVANCE_SCORE` | double | `0.3` | Default minimum relevance score for search results |
| `DOCUMENTATION_REPOSITORY_SYNC_CHECK_INTERVAL_SECONDS` | int | `30` | Interval in seconds between repository sync opportunity chec... |
| `DOCUMENTATION_SEARCH_CACHE_TTL_SECONDS` | int | `300` | TTL for search result caching |
| `DOCUMENTATION_SEARCH_INDEX_REBUILD_ON_STARTUP` | bool | `true` | Whether to rebuild search index on service startup |
| `DOCUMENTATION_SEARCH_INDEX_REBUILD_STARTUP_DELAY_SECONDS` | int | `5` | Delay in seconds before search index rebuild starts (allows ... |
| `DOCUMENTATION_SEARCH_SNIPPET_LENGTH` | int | `200` | Length in characters for search result snippets |
| `DOCUMENTATION_STATS_SAMPLE_SIZE` | int | `10` | Number of documents to sample for namespace statistics |
| `DOCUMENTATION_SYNC_LOCK_TTL_SECONDS` | int | `1800` | TTL in seconds for repository sync distributed lock |
| `DOCUMENTATION_SYNC_SCHEDULER_CHECK_INTERVAL_MINUTES` | int | `5` | How often to check for repos needing sync |
| `DOCUMENTATION_SYNC_SCHEDULER_ENABLED` | bool | `true` | Enable background sync scheduler |
| `DOCUMENTATION_TRASHCAN_TTL_DAYS` | int | `7` | Days before trashcan items are auto-purged |
| `DOCUMENTATION_VOICE_SUMMARY_MAX_LENGTH` | int | `200` | Maximum characters for voice summaries |

### Escrow

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `ESCROW_CONFIRMATION_TIMEOUT_BATCH_SIZE` | int | `100` | Maximum escrows to process per timeout check cycle |
| `ESCROW_CONFIRMATION_TIMEOUT_BEHAVIOR` | string | `auto_confirm` | What happens on confirmation timeout (auto_confirm/dispute/r... |
| `ESCROW_CONFIRMATION_TIMEOUT_CHECK_INTERVAL_SECONDS` | int | `30` | How often the background service checks for expired confirma... |
| `ESCROW_CONFIRMATION_TIMEOUT_SECONDS` | int | `300` | Timeout for party confirmations in seconds (default 5 minute... |
| `ESCROW_DEFAULT_LIST_LIMIT` | int | `50` | Default limit for listing escrows when not specified |
| `ESCROW_DEFAULT_REFUND_MODE` | string | `immediate` | Default refund confirmation mode when not specified in reque... |
| `ESCROW_DEFAULT_RELEASE_MODE` | string | `service_only` | Default release confirmation mode when not specified in requ... |
| `ESCROW_DEFAULT_TIMEOUT` | string | `P7D` | Default escrow expiration if not specified (ISO 8601 duratio... |
| `ESCROW_EXPIRATION_BATCH_SIZE` | int | `100` | Batch size for expiration processing |
| `ESCROW_EXPIRATION_CHECK_INTERVAL` | string | `PT1M` | How often to check for expired escrows (ISO 8601 duration) |
| `ESCROW_EXPIRATION_GRACE_PERIOD` | string | `PT1H` | Grace period after expiration before auto-refund (ISO 8601 d... |
| `ESCROW_IDEMPOTENCY_TTL_HOURS` | int | `24` | TTL in hours for idempotency key storage |
| `ESCROW_MAX_ASSETS_PER_DEPOSIT` | int | `50` | Maximum asset lines per deposit |
| `ESCROW_MAX_CONCURRENCY_RETRIES` | int | `3` | Maximum retry attempts for optimistic concurrency operations |
| `ESCROW_MAX_PARTIES` | int | `10` | Maximum parties per escrow |
| `ESCROW_MAX_PENDING_PER_PARTY` | int | `100` | Maximum concurrent pending escrows per party |
| `ESCROW_MAX_TIMEOUT` | string | `P30D` | Maximum allowed escrow duration (ISO 8601 duration) |
| `ESCROW_TOKEN_LENGTH` | int | `32` | Token length in bytes (before encoding) |
| `ESCROW_VALIDATION_CHECK_INTERVAL` | string | `PT5M` | How often to validate held assets (ISO 8601 duration) |

### Faction

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `FACTION_DEFAULT_MEMBER_ROLE` | string | `Member` | Default role assigned to new members when no role is specifi... |
| `FACTION_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | int | `30` | Timeout in seconds for distributed lock acquisition on facti... |
| `FACTION_MAX_HIERARCHY_DEPTH` | int | `5` | Maximum parent/child nesting depth for faction hierarchy |
| `FACTION_MAX_NORMS_PER_FACTION` | int | `50` | Maximum number of norm definitions per faction |
| `FACTION_MAX_TERRITORIES_PER_FACTION` | int | `20` | Maximum number of territory claims per faction |
| `FACTION_NORM_QUERY_CACHE_TTL_SECONDS` | int | `300` | TTL in seconds for cached norm resolution results per charac... |
| `FACTION_SEED_BULK_PAGE_SIZE` | int | `100` | Page size for bulk seed operations during faction seeding |
| `FACTION_SEED_TYPE_CODE` | string | `faction` | Which seed type code this service manages for faction growth |

### Game Service

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `GAME_SERVICE_SERVICE_LIST_RETRY_ATTEMPTS` | int | `3` | Maximum number of optimistic concurrency retry attempts for ... |

### Game Session

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `GAME_SESSION_CLEANUP_INTERVAL_SECONDS` | int | `30` | Interval between reservation cleanup cycles |
| `GAME_SESSION_CLEANUP_SERVICE_STARTUP_DELAY_SECONDS` | int | `10` | Delay before cleanup service starts (allows other services t... |
| `GAME_SESSION_DEFAULT_LOBBY_MAX_PLAYERS` | int | `100` | Default maximum players for game lobbies |
| `GAME_SESSION_DEFAULT_RESERVATION_TTL_SECONDS` | int | `60` | Default TTL for player reservations when not specified in re... |
| `GAME_SESSION_DEFAULT_SESSION_TIMEOUT_SECONDS` | int | **REQUIRED** | Default session timeout in seconds. Null means sessions have... |
| `GAME_SESSION_GENERIC_LOBBIES_ENABLED` | bool | `false` | When true AND "generic" is in SupportedGameServices, auto-pu... |
| `GAME_SESSION_LOCK_TIMEOUT_SECONDS` | int | `60` | Timeout in seconds for distributed session locks |
| `GAME_SESSION_MAX_PLAYERS_PER_SESSION` | int | `16` | Maximum players allowed per session |
| `GAME_SESSION_SERVER_SALT` | string | `bannou-dev-game-session-salt-change-in-production` | Server salt for GUID generation. Must be shared across all i... |
| `GAME_SESSION_STARTUP_SERVICE_DELAY_SECONDS` | int | `2` | Delay before startup service initializes subscription caches |
| `GAME_SESSION_SUBSCRIBER_SESSION_RETRY_MAX_ATTEMPTS` | int | `3` | Maximum retry attempts for optimistic concurrency on subscri... |
| `GAME_SESSION_SUPPORTED_GAME_SERVICES` | string | `generic` | Comma-separated list of supported game service stub names (e... |

### Gardener

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `GARDENER_ABANDON_DETECTION_MINUTES` | int | `5` | Minutes without player input before scenario is marked aband... |
| `GARDENER_AFFINITY_WEIGHT` | double | `0.4` | Weight for domain affinity in scenario scoring algorithm |
| `GARDENER_BACKGROUND_SERVICE_STARTUP_DELAY_SECONDS` | int | `5` | Seconds to wait before starting background workers after ser... |
| `GARDENER_BOND_SCENARIO_PRIORITY` | double | `1.5` | Scoring boost multiplier for bond-friendly scenarios when pl... |
| `GARDENER_BOND_SHARED_GARDEN_ENABLED` | bool | `true` | Whether bonded players share a garden instance with merged P... |
| `GARDENER_DEFAULT_ESTIMATED_DURATION_MINUTES` | int | `30` | Fallback estimated duration for templates without an explici... |
| `GARDENER_DEFAULT_PHASE` | string | `Alpha` | Starting deployment phase for new installations |
| `GARDENER_DIRECTED_DISTANCE_THRESHOLD` | double | `200.0` | Minimum total drift distance to classify a player as directe... |
| `GARDENER_DIRECTED_MAX_HESITATION_RATIO` | double | `0.15` | Maximum hesitation ratio to classify a player as directed |
| `GARDENER_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | int | `30` | Timeout in seconds for distributed lock acquisition on garde... |
| `GARDENER_DIVERSITY_SEEN_PENALTY` | double | `0.2` | Diversity score multiplier for recently completed scenarios ... |
| `GARDENER_DIVERSITY_WEIGHT` | double | `0.3` | Weight for category diversity in scenario scoring algorithm |
| `GARDENER_EXPLORATION_DISTANCE_THRESHOLD` | double | `500.0` | Minimum total drift distance to classify a player as explori... |
| `GARDENER_EXPLORATION_MAX_HESITATION_RATIO` | double | `0.3` | Maximum hesitation ratio to still classify a player as explo... |
| `GARDENER_GARDEN_TICK_INTERVAL_MS` | int | `5000` | Milliseconds between garden orchestrator evaluation cycles |
| `GARDENER_GROWTH_AWARD_MULTIPLIER` | double | `1.0` | Global multiplier applied to all growth awards from scenario... |
| `GARDENER_GROWTH_FULL_COMPLETION_MAX_RATIO` | double | `1.5` | Maximum time ratio cap for full completion growth (overtime ... |
| `GARDENER_GROWTH_FULL_COMPLETION_MIN_RATIO` | double | `0.5` | Minimum time ratio floor for full completion growth (speed-r... |
| `GARDENER_GROWTH_PARTIAL_MAX_RATIO` | double | `0.5` | Maximum time ratio cap for abandoned/timed-out scenario grow... |
| `GARDENER_HESITANT_HESITATION_THRESHOLD` | double | `0.6` | Minimum hesitation ratio to classify a player as hesitant |
| `GARDENER_HESITATION_DETECTION_THRESHOLD` | double | `0.1` | Minimum distance movement for hesitation detection in garden... |
| `GARDENER_HESITATION_RATIO_NORMALIZATION_FACTOR` | double | `10.0` | Divisor for normalizing hesitation count to a ratio against ... |
| `GARDENER_MAX_ACTIVE_POIS_PER_GARDEN` | int | `8` | Maximum concurrent POIs per player garden instance |
| `GARDENER_MAX_CONCURRENT_SCENARIOS_GLOBAL` | int | `1000` | Maximum total active scenario instances across all players |
| `GARDENER_MIN_POI_SPACING` | double | `30.0` | Minimum distance between any two POIs in garden space units |
| `GARDENER_NARRATIVE_SCORE_HIGH` | double | `0.9` | Narrative score for strongest category-pattern match (e.g. e... |
| `GARDENER_NARRATIVE_SCORE_LOW` | double | `0.4` | Narrative score for weak category-pattern match (e.g. explor... |
| `GARDENER_NARRATIVE_SCORE_MEDIUM` | double | `0.6` | Narrative score for moderate category-pattern match (e.g. ex... |
| `GARDENER_NARRATIVE_SCORE_MEDIUM_HIGH` | double | `0.7` | Narrative score for good category-pattern match (e.g. explor... |
| `GARDENER_NARRATIVE_SCORE_NEUTRAL` | double | `0.5` | Baseline narrative score when no player drift pattern is det... |
| `GARDENER_NARRATIVE_WEIGHT` | double | `0.2` | Weight for drift-pattern narrative response in scenario scor... |
| `GARDENER_POI_DEFAULT_INTENSITY_RAMP` | double | `0.5` | Default initial intensity ramp value for spawned POIs (0.0-1... |
| `GARDENER_POI_DEFAULT_TRIGGER_RADIUS` | double | `15.0` | Default trigger radius for POI proximity detection in garden... |
| `GARDENER_POI_DEFAULT_TTL_MINUTES` | int | `10` | Default time-to-live in minutes for spawned POIs before expi... |
| `GARDENER_POI_POSITION_MAX_RETRIES` | int | `10` | Maximum rejection sampling attempts when generating a valid ... |
| `GARDENER_POI_SPAWN_RADIUS_MAX` | double | `200.0` | Maximum distance from player to spawn a POI in garden space ... |
| `GARDENER_POI_SPAWN_RADIUS_MIN` | double | `50.0` | Minimum distance from player to spawn a POI in garden space ... |
| `GARDENER_POI_VERTICAL_DAMPENING_FACTOR` | double | `0.3` | Y-axis scale factor for POI vertical distribution relative t... |
| `GARDENER_PROXIMITY_TRIGGER_MAX_HESITATION_RATIO` | double | `0.2` | Maximum hesitation ratio for proximity-based POI trigger mod... |
| `GARDENER_RANDOM_WEIGHT` | double | `0.1` | Weight for randomness and discovery in scenario scoring |
| `GARDENER_RECENT_SCENARIO_COOLDOWN_MINUTES` | int | `30` | Minutes before a completed scenario can be re-offered to the... |
| `GARDENER_SCENARIO_HISTORY_QUERY_PAGE_SIZE` | int | `200` | Maximum number of scenario history records to load per orche... |
| `GARDENER_SCENARIO_LIFECYCLE_WORKER_INTERVAL_SECONDS` | int | `30` | Seconds between scenario lifecycle worker evaluation cycles |
| `GARDENER_SCENARIO_TEMPLATE_QUERY_PAGE_SIZE` | int | `500` | Maximum number of scenario templates to load per orchestrato... |
| `GARDENER_SCENARIO_TIMEOUT_MINUTES` | int | `60` | Maximum scenario duration before forced completion in minute... |
| `GARDENER_SEED_TYPE_CODE` | string | `guardian` | Which seed type code this gardener manages for player spirit... |

### Inventory

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `INVENTORY_CONTAINER_CACHE_TTL_SECONDS` | int | `300` | TTL for container cache entries |
| `INVENTORY_DEFAULT_MAX_NESTING_DEPTH` | int | `3` | Default maximum nesting depth for containers |
| `INVENTORY_DEFAULT_MAX_SLOTS` | int | `20` | Default max slots for new slot-based containers |
| `INVENTORY_DEFAULT_MAX_WEIGHT` | double | `100.0` | Default max weight for new weight-based containers |
| `INVENTORY_DEFAULT_WEIGHT_CONTRIBUTION` | string | `self_plus_contents` | Default weight contribution mode for containers |
| `INVENTORY_DELETE_LOCK_TIMEOUT_SECONDS` | int | `120` | Timeout for container deletion locks (longer than standard l... |
| `INVENTORY_ENABLE_LAZY_CONTAINER_CREATION` | bool | `true` | Whether to enable lazy container creation for characters |
| `INVENTORY_LIST_LOCK_TIMEOUT_SECONDS` | int | `15` | Timeout for owner/type index list modification locks (shorte... |
| `INVENTORY_LOCK_TIMEOUT_SECONDS` | int | `30` | Timeout for container modification locks |
| `INVENTORY_MAX_COUNT_QUERY_LIMIT` | int | `10000` | Maximum items to scan when counting items across containers |
| `INVENTORY_QUERY_PAGE_SIZE` | int | `200` | Number of items to fetch per query page for inventory operat... |

### Item

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `ITEM_BINDING_ALLOW_ADMIN_OVERRIDE` | bool | `true` | Whether admins can unbind soulbound items |
| `ITEM_CAN_USE_MILESTONE_CODE` | string | `validate` | Milestone code to complete for CanUse validation contracts |
| `ITEM_DEFAULT_RARITY` | string | `common` | Default rarity for new templates when not specified |
| `ITEM_DEFAULT_SOULBOUND_TYPE` | string | `none` | Default soulbound type for new templates |
| `ITEM_DEFAULT_WEIGHT_PRECISION` | string | `decimal_2` | Default weight precision for new templates |
| `ITEM_INSTANCE_CACHE_TTL_SECONDS` | int | `900` | TTL for instance cache entries in seconds (15 minutes for ac... |
| `ITEM_LIST_OPERATION_MAX_RETRIES` | int | `3` | Maximum retry attempts for optimistic concurrency on list op... |
| `ITEM_LOCK_TIMEOUT_SECONDS` | int | `30` | Timeout in seconds for distributed locks on item instance mo... |
| `ITEM_MAX_INSTANCES_PER_QUERY` | int | `1000` | Maximum item instances returned in a single query |
| `ITEM_ON_USE_FAILED_MILESTONE_CODE` | string | `handle_failure` | Milestone code to complete for OnUseFailed handler contracts |
| `ITEM_SYSTEM_PARTY_ID` | string | **REQUIRED** | System party ID for item use contracts (null uses determinis... |
| `ITEM_SYSTEM_PARTY_TYPE` | string | `system` | Entity type for the system party in item use contracts |
| `ITEM_TEMPLATE_CACHE_TTL_SECONDS` | int | `3600` | TTL for template cache entries in seconds (templates change ... |
| `ITEM_USE_EVENT_BATCH_MAX_SIZE` | int | `100` | Maximum number of use records per batched event before force... |
| `ITEM_USE_EVENT_DEDUPLICATION_WINDOW_SECONDS` | int | `60` | Time window in seconds for deduplicating item use events by ... |
| `ITEM_USE_MILESTONE_CODE` | string | `use` | Milestone code to complete when using items via contract beh... |
| `ITEM_USE_STEP_LOCK_TIMEOUT_SECONDS` | int | `30` | Distributed lock timeout in seconds for UseItemStep operatio... |

### Leaderboard

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `LEADERBOARD_MAX_ENTRIES_PER_QUERY` | int | `1000` | Maximum entries returned per rank query |
| `LEADERBOARD_SCORE_UPDATE_BATCH_SIZE` | int | `1000` | Maximum scores to process in a single batch |

### License

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `LICENSE_BOARD_CACHE_TTL_SECONDS` | int | `300` | Redis TTL for board state cache in seconds |
| `LICENSE_DEFAULT_ADJACENCY_MODE` | string | `eight_way` | Default adjacency mode for new board templates |
| `LICENSE_DEFAULT_PAGE_SIZE` | int | `20` | Default page size for paginated queries (min 1, max 100) |
| `LICENSE_LOCK_TIMEOUT_SECONDS` | int | `30` | Distributed lock TTL for board mutations in seconds |
| `LICENSE_MAX_BOARDS_PER_OWNER` | int | `10` | Maximum number of active boards a single owner entity can ha... |
| `LICENSE_MAX_CONCURRENCY_RETRIES` | int | `3` | Maximum retry attempts for optimistic concurrency conflicts |
| `LICENSE_MAX_DEFINITIONS_PER_BOARD` | int | `200` | Maximum license definitions per board template |

### Location

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `LOCATION_CACHE_TTL_SECONDS` | int | `3600` | TTL for location cache entries in seconds (locations change ... |
| `LOCATION_CONTEXT_CACHE_TTL_SECONDS` | int | `10` | TTL for location context provider cache entries in seconds, ... |
| `LOCATION_CONTEXT_NEARBY_POIS_LIMIT` | int | `50` | Maximum number of sibling locations to include in the nearby... |
| `LOCATION_DEFAULT_DESCENDANT_MAX_DEPTH` | int | `10` | Default max depth when listing descendants if not specified ... |
| `LOCATION_ENTITY_PRESENCE_CLEANUP_INTERVAL_SECONDS` | int | `60` | Interval in seconds between background cleanup cycles for ex... |
| `LOCATION_ENTITY_PRESENCE_CLEANUP_STARTUP_DELAY_SECONDS` | int | `15` | Delay in seconds before entity presence cleanup worker start... |
| `LOCATION_ENTITY_PRESENCE_TTL_SECONDS` | int | `30` | Default TTL for entity presence entries in seconds (reporter... |
| `LOCATION_INDEX_LOCK_TIMEOUT_SECONDS` | int | `5` | Timeout for acquiring distributed locks on index operations ... |
| `LOCATION_MAX_ANCESTOR_DEPTH` | int | `20` | Maximum depth to traverse when walking ancestor chain (preve... |
| `LOCATION_MAX_DESCENDANT_DEPTH` | int | `20` | Safety limit for descendant traversal and circular reference... |
| `LOCATION_MAX_ENTITIES_PER_LOCATION_QUERY` | int | `100` | Maximum entities returned by list-entities-at-location (pagi... |

### Mapping

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `MAPPING_AFFORDANCE_CACHE_TIMEOUT_SECONDS` | int | `60` | Default TTL for cached affordance query results |
| `MAPPING_AFFORDANCE_EXCLUSION_TOLERANCE_UNITS` | double | `1.0` | Distance tolerance in world units for position exclusion mat... |
| `MAPPING_AUTHORITY_GRACE_PERIOD_SECONDS` | int | `30` | Grace period in seconds after missed heartbeat before author... |
| `MAPPING_AUTHORITY_TIMEOUT_SECONDS` | int | `60` | Time in seconds before authority expires without heartbeat |
| `MAPPING_DEFAULT_LAYER_CACHE_TTL_SECONDS` | int | `3600` | Default TTL for cached layer data (ephemeral kinds) |
| `MAPPING_EVENT_AGGREGATION_WINDOW_MS` | int | `100` | Window in milliseconds for batching rapid updates into singl... |
| `MAPPING_INLINE_PAYLOAD_MAX_BYTES` | int | `65536` | Payloads larger than this are stored via lib-asset reference |
| `MAPPING_MAX_AFFORDANCE_CANDIDATES` | int | `1000` | Maximum candidate points to evaluate in affordance queries |
| `MAPPING_MAX_BUFFER_FLUSH_RETRIES` | int | `3` | Maximum retry attempts for flushing spatial change buffers b... |
| `MAPPING_MAX_CHECKOUT_DURATION_SECONDS` | int | `1800` | Maximum duration for authoring checkout locks |
| `MAPPING_MAX_OBJECTS_PER_QUERY` | int | `5000` | Maximum objects returned in a single query |
| `MAPPING_MAX_PAYLOADS_PER_PUBLISH` | int | `100` | Maximum payloads in single publish or ingest event |
| `MAPPING_MAX_SPATIAL_QUERY_RESULTS` | int | `5` | Maximum results returned for spatial queries (bounding box, ... |
| `MAPPING_SPATIAL_CELL_SIZE` | double | `64.0` | Size of spatial index cells in world units (default 64) |
| `MAPPING_TTL_COMBAT_EFFECTS` | int | `30` | TTL for combat effects layer data (very short-lived, ephemer... |
| `MAPPING_TTL_DYNAMIC_OBJECTS` | int | `3600` | TTL for dynamic objects layer data |
| `MAPPING_TTL_HAZARDS` | int | `300` | TTL for hazards layer data (short-lived) |
| `MAPPING_TTL_NAVIGATION` | int | `-1` | TTL for navigation layer data (-1 = no TTL, durable) |
| `MAPPING_TTL_OWNERSHIP` | int | `-1` | TTL for ownership layer data (-1 = no TTL, durable) |
| `MAPPING_TTL_POINTS_OF_INTEREST` | int | `3600` | TTL for points of interest layer data |
| `MAPPING_TTL_RESOURCES` | int | `3600` | TTL for resources layer data |
| `MAPPING_TTL_SPAWN_POINTS` | int | `3600` | TTL for spawn points layer data |
| `MAPPING_TTL_STATIC_GEOMETRY` | int | `-1` | TTL for static geometry layer data (-1 = no TTL, durable) |
| `MAPPING_TTL_TERRAIN` | int | `-1` | TTL for terrain layer data (-1 = no TTL, durable) |
| `MAPPING_TTL_VISUAL_EFFECTS` | int | `60` | TTL for visual effects layer data (short-lived, ephemeral) |
| `MAPPING_TTL_WEATHER_EFFECTS` | int | `600` | TTL for weather effects layer data |

### Matchmaking

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `MATCHMAKING_AUTO_REQUEUE_ON_DECLINE` | bool | `true` | Automatically requeue non-declining players when a match is ... |
| `MATCHMAKING_BACKGROUND_SERVICE_STARTUP_DELAY_SECONDS` | int | `5` | Delay before background service starts processing (allows ot... |
| `MATCHMAKING_DEFAULT_JOIN_DEADLINE_SECONDS` | int | `120` | Default deadline for players to join the game session after ... |
| `MATCHMAKING_DEFAULT_MATCH_ACCEPT_TIMEOUT_SECONDS` | int | `30` | Default time for players to accept a formed match |
| `MATCHMAKING_DEFAULT_MAX_INTERVALS` | int | `6` | Default maximum intervals before timeout/relaxation |
| `MATCHMAKING_DEFAULT_RESERVATION_TTL_SECONDS` | int | `120` | Default TTL for player reservations in game sessions created... |
| `MATCHMAKING_IMMEDIATE_MATCH_CHECK_ENABLED` | bool | `true` | Enable immediate match check on ticket creation (quick match... |
| `MATCHMAKING_MAX_CONCURRENT_TICKETS_PER_PLAYER` | int | `3` | Maximum number of concurrent tickets a player can have |
| `MATCHMAKING_PENDING_MATCH_REDIS_KEY_TTL_SECONDS` | int | `300` | TTL for pending match data in Redis (for reconnection handli... |
| `MATCHMAKING_PROCESSING_INTERVAL_SECONDS` | int | `15` | Default interval between match processing cycles |
| `MATCHMAKING_SERVER_SALT` | string | `bannou-dev-matchmaking-salt-change-in-production` | Server salt for GUID generation. Must be shared across all i... |
| `MATCHMAKING_STATS_PUBLISH_INTERVAL_SECONDS` | int | `60` | Interval between stats event publications |

### Mesh

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `MESH_CIRCUIT_BREAKER_ENABLED` | bool | `true` | Whether to enable circuit breaker for failed endpoints |
| `MESH_CIRCUIT_BREAKER_RESET_SECONDS` | int | `30` | Seconds before attempting to close circuit |
| `MESH_CIRCUIT_BREAKER_THRESHOLD` | int | `5` | Number of consecutive failures before opening circuit |
| `MESH_CONNECT_TIMEOUT_SECONDS` | int | `10` | TCP connection timeout in seconds |
| `MESH_DEFAULT_LOAD_BALANCER` | string | `RoundRobin` | Default load balancing algorithm |
| `MESH_DEFAULT_MAX_CONNECTIONS` | int | `1000` | Default max connections for auto-registered endpoints when h... |
| `MESH_DEGRADATION_EVENT_DEDUPLICATION_WINDOW_SECONDS` | int | `60` | Time window in seconds for deduplicating degradation events ... |
| `MESH_DEGRADATION_THRESHOLD_SECONDS` | int | `60` | Time without heartbeat before marking endpoint as degraded |
| `MESH_ENABLE_SERVICE_MAPPING_SYNC` | bool | `true` | Whether to subscribe to FullServiceMappingsEvent for routing... |
| `MESH_ENDPOINT_CACHE_MAX_SIZE` | int | `0` | Maximum number of app-ids to cache endpoint resolutions for.... |
| `MESH_ENDPOINT_CACHE_TTL_SECONDS` | int | `5` | TTL in seconds for cached service endpoints |
| `MESH_ENDPOINT_HOST` | string | **REQUIRED** | Hostname/IP for mesh endpoint registration. Defaults to app-... |
| `MESH_ENDPOINT_PORT` | int | `80` | Port for mesh endpoint registration. |
| `MESH_ENDPOINT_TTL_SECONDS` | int | `90` | TTL for endpoint registration (should be > 2x heartbeat inte... |
| `MESH_HEALTH_CHECK_ENABLED` | bool | `false` | Whether to perform active health checks on endpoints |
| `MESH_HEALTH_CHECK_EVENT_DEDUPLICATION_WINDOW_SECONDS` | int | `60` | Time window in seconds for deduplicating health check failur... |
| `MESH_HEALTH_CHECK_FAILURE_THRESHOLD` | int | `3` | Consecutive health check failures before deregistering endpo... |
| `MESH_HEALTH_CHECK_INTERVAL_SECONDS` | int | `60` | Interval between active health checks |
| `MESH_HEALTH_CHECK_STARTUP_DELAY_SECONDS` | int | `10` | Delay in seconds before health check service starts probing ... |
| `MESH_HEALTH_CHECK_TIMEOUT_SECONDS` | int | `5` | Timeout for health check requests |
| `MESH_HEARTBEAT_INTERVAL_SECONDS` | int | `30` | Recommended interval between heartbeats |
| `MESH_INSTANCE_ID` | string | **REQUIRED** | Explicit mesh node identity override. When set, this value i... |
| `MESH_LOAD_BALANCING_STATE_MAX_APP_IDS` | int | `0` | Maximum number of app-ids to track in load balancing state (... |
| `MESH_LOAD_THRESHOLD_PERCENT` | int | `80` | Load percentage above which an endpoint is considered high-l... |
| `MESH_MAX_RETRIES` | int | `3` | Maximum retry attempts for failed service calls |
| `MESH_MAX_SERVICE_MAPPINGS_DISPLAYED` | int | `10` | Maximum service mappings shown in diagnostic logs |
| `MESH_MAX_TOP_ENDPOINTS_RETURNED` | int | `2` | Maximum top endpoints returned in health status queries |
| `MESH_POOLED_CONNECTION_LIFETIME_MINUTES` | int | `2` | How long to keep pooled HTTP connections alive in minutes |
| `MESH_REQUEST_TIMEOUT_SECONDS` | int | `30` | Maximum time in seconds for a complete request/response cycl... |
| `MESH_RETRY_DELAY_MILLISECONDS` | int | `100` | Initial delay between retries (doubles on each retry) |
| `MESH_USE_LOCAL_ROUTING` | bool | `false` | Use local-only routing instead of lib-state. All calls route... |

### Messaging

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `MESSAGING_CALLBACK_RETRY_DELAY_MS` | int | `1000` | Delay between HTTP callback retry attempts in milliseconds |
| `MESSAGING_CALLBACK_RETRY_MAX_ATTEMPTS` | int | `3` | Maximum retry attempts for HTTP callback delivery (network f... |
| `MESSAGING_CHANNEL_POOL_SIZE` | int | `100` | Maximum number of channels in the publisher channel pool |
| `MESSAGING_CONNECTION_MAX_BACKOFF_MS` | int | `60000` | Maximum backoff delay for connection retries in milliseconds |
| `MESSAGING_CONNECTION_RETRY_COUNT` | int | `5` | Number of connection retry attempts |
| `MESSAGING_CONNECTION_RETRY_DELAY_MS` | int | `1000` | Delay between connection retry attempts in milliseconds |
| `MESSAGING_DEAD_LETTER_CONSUMER_ENABLED` | bool | `true` | Enable dead letter consumer background service for logging a... |
| `MESSAGING_DEAD_LETTER_CONSUMER_STARTUP_DELAY_SECONDS` | int | `5` | Delay in seconds before dead letter consumer starts subscrib... |
| `MESSAGING_DEAD_LETTER_EXCHANGE` | string | `bannou-dlx` | Dead letter exchange name for failed messages |
| `MESSAGING_DEAD_LETTER_MAX_LENGTH` | int | `100000` | Maximum messages in dead letter queue before oldest dropped |
| `MESSAGING_DEAD_LETTER_OVERFLOW_BEHAVIOR` | string | `drop-head` | Behavior when DLX queue exceeds max length (drop-head drops ... |
| `MESSAGING_DEAD_LETTER_TTL_MS` | int | `604800000` | Time-to-live for dead letter messages in milliseconds (defau... |
| `MESSAGING_DEFAULT_AUTO_ACK` | bool | `false` | Default auto-acknowledge setting for subscriptions |
| `MESSAGING_DEFAULT_EXCHANGE` | string | `bannou` | Default exchange name for publishing |
| `MESSAGING_DEFAULT_PREFETCH_COUNT` | int | `10` | Default prefetch count for subscriptions |
| `MESSAGING_ENABLE_PUBLISHER_CONFIRMS` | bool | `true` | Enable RabbitMQ publisher confirms for reliability. When ena... |
| `MESSAGING_ENABLE_PUBLISH_BATCHING` | bool | `false` | Enable batched publishing for high-throughput scenarios. Whe... |
| `MESSAGING_EXTERNAL_SUBSCRIPTION_TTL_SECONDS` | int | `86400` | TTL in seconds for external HTTP callback subscriptions (def... |
| `MESSAGING_MAX_CONCURRENT_CHANNEL_CREATION` | int | `50` | Maximum concurrent channel creation requests (backpressure s... |
| `MESSAGING_MAX_TOTAL_CHANNELS` | int | `1000` | Hard limit on total active channels per connection (RabbitMQ... |
| `MESSAGING_PUBLISH_BATCH_SIZE` | int | `100` | Maximum number of messages to batch before sending. Batch is... |
| `MESSAGING_PUBLISH_BATCH_TIMEOUT_MS` | int | `10` | Maximum delay in milliseconds before flushing a partial batc... |
| `MESSAGING_RABBITMQ_HOST` | string | `rabbitmq` | RabbitMQ server hostname |
| `MESSAGING_RABBITMQ_NETWORK_RECOVERY_INTERVAL_SECONDS` | int | `10` | Interval in seconds between RabbitMQ connection recovery att... |
| `MESSAGING_RABBITMQ_PASSWORD` | string | `guest` (insecure) | RabbitMQ password |
| `MESSAGING_RABBITMQ_PORT` | int | `5672` | RabbitMQ server port |
| `MESSAGING_RABBITMQ_USERNAME` | string | `guest` (insecure) | RabbitMQ username |
| `MESSAGING_RABBITMQ_VHOST` | string | `/` | RabbitMQ virtual host |
| `MESSAGING_RETRY_BUFFER_BACKPRESSURE_THRESHOLD` | double | `0.8` | When buffer reaches this percentage full, start rejecting ne... |
| `MESSAGING_RETRY_BUFFER_ENABLED` | bool | `true` | Enable retry buffer for failed event publishes |
| `MESSAGING_RETRY_BUFFER_INTERVAL_SECONDS` | int | `5` | Interval between retry attempts for buffered messages |
| `MESSAGING_RETRY_BUFFER_MAX_AGE_SECONDS` | int | `600` | Maximum age of buffered messages before node crash (gives op... |
| `MESSAGING_RETRY_BUFFER_MAX_SIZE` | int | `500000` | Maximum number of messages in retry buffer before node crash... |
| `MESSAGING_RETRY_DELAY_MS` | int | `5000` | Base delay between retry attempts in milliseconds (doubles w... |
| `MESSAGING_RETRY_MAX_ATTEMPTS` | int | `5` | Maximum retry attempts before discarding message to dead-let... |
| `MESSAGING_RETRY_MAX_BACKOFF_MS` | int | `60000` | Maximum backoff delay between retries in milliseconds (caps ... |
| `MESSAGING_SHUTDOWN_TIMEOUT_SECONDS` | int | `10` | Maximum seconds to wait for graceful subscription cleanup du... |
| `MESSAGING_SUBSCRIPTION_RECOVERY_STARTUP_DELAY_SECONDS` | int | `2` | Delay in seconds before starting subscription recovery servi... |
| `MESSAGING_SUBSCRIPTION_TTL_REFRESH_INTERVAL_HOURS` | int | `6` | Interval in hours between subscription TTL refresh operation... |
| `MESSAGING_USE_INMEMORY` | bool | `false` | Use in-memory messaging instead of RabbitMQ. Messages are NO... |

### Music

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `MUSIC_COMPOSITION_CACHE_TTL_SECONDS` | int | `86400` | TTL in seconds for cached deterministic compositions |
| `MUSIC_CONTOUR_DEFAULT_TENSION` | double | `0.5` | Default initial tension used when no section provides a valu... |
| `MUSIC_CONTOUR_TENSION_THRESHOLD` | double | `0.2` | Tension delta threshold for determining ascending/descending... |
| `MUSIC_DEFAULT_BEATS_PER_CHORD` | double | `4.0` | Default beats per chord in progression generation |
| `MUSIC_DEFAULT_CHORDS_PER_BAR` | int | `1` | Default number of chords per bar in generated progressions |
| `MUSIC_DEFAULT_EMOTIONAL_BRIGHTNESS` | double | `0.5` | Default brightness value for emotional state (0.0-1.0) |
| `MUSIC_DEFAULT_EMOTIONAL_ENERGY` | double | `0.5` | Default energy value for emotional state (0.0-1.0) |
| `MUSIC_DEFAULT_EMOTIONAL_STABILITY` | double | `0.8` | Default stability value for emotional state (0.0-1.0) |
| `MUSIC_DEFAULT_EMOTIONAL_TENSION` | double | `0.2` | Default tension value for emotional state (0.0-1.0) |
| `MUSIC_DEFAULT_EMOTIONAL_VALENCE` | double | `0.5` | Default valence value for emotional state (0.0-1.0) |
| `MUSIC_DEFAULT_EMOTIONAL_WARMTH` | double | `0.5` | Default warmth value for emotional state (0.0-1.0) |
| `MUSIC_DEFAULT_MELODY_DENSITY` | double | `0.7` | Default note density for melody generation (0.0-1.0) |
| `MUSIC_DEFAULT_MELODY_SYNCOPATION` | double | `0.2` | Default syncopation amount for melody generation (0.0-1.0) |
| `MUSIC_DEFAULT_TICKS_PER_BEAT` | int | `480` | Default MIDI ticks per beat (PPQN) for composition rendering |
| `MUSIC_DEFAULT_VOICE_COUNT` | int | `4` | Default number of voices for chord voicing |
| `MUSIC_DENSITY_ENERGY_MULTIPLIER` | double | `0.5` | Multiplier applied to energy for density calculation.
Final ... |
| `MUSIC_DENSITY_MINIMUM` | double | `0.4` | Minimum melody density (floor value before energy scaling).
 |

### Obligation

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `OBLIGATION_BREACH_REPORT_ENABLED` | bool | `true` | Whether to auto-report violations as breaches to the contrac... |
| `OBLIGATION_CACHE_TTL_MINUTES` | int | `10` | TTL in minutes for obligation manifest cache entries per cha... |
| `OBLIGATION_DEFAULT_PAGE_SIZE` | int | `20` | Default page size for paginated queries |
| `OBLIGATION_IDEMPOTENCY_TTL_SECONDS` | int | `86400` | TTL in seconds for violation report idempotency keys |
| `OBLIGATION_LOCK_TIMEOUT_SECONDS` | int | `30` | Timeout in seconds for distributed locks on obligation cache... |
| `OBLIGATION_MAX_ACTIVE_CONTRACTS_QUERY` | int | `100` | Maximum number of active contracts to query per character du... |
| `OBLIGATION_MAX_OBLIGATIONS_PER_CHARACTER` | int | `200` | Safety limit on cached obligations per character (prevents r... |
| `OBLIGATION_NORM_RESOLUTION_MODE` | string | `PerfectKnowledge` | How norm-based obligation costs are resolved when the Hearsa... |
| `OBLIGATION_NORM_UNCERTAINTY_VARIANCE` | double | `0.2` | Maximum variance applied to norm penalties when NormResoluti... |

### Orchestrator

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `ORCHESTRATOR_CACHE_TTL_MINUTES` | int | `5` | Cache TTL in minutes for orchestrator data |
| `ORCHESTRATOR_CERTIFICATES_HOST_PATH` | string | `/app/provisioning/certificates` | Host path for TLS certificates |
| `ORCHESTRATOR_CONFIG_HISTORY_TTL_DAYS` | int | `30` | TTL in days for configuration history entries in state store |
| `ORCHESTRATOR_CONTAINER_STATUS_POLL_INTERVAL_SECONDS` | int | `2` | Interval in seconds for polling container status during depl... |
| `ORCHESTRATOR_DEFAULT_BACKEND` | string | `compose` | Default container orchestration backend when not specified i... |
| `ORCHESTRATOR_DEFAULT_WAIT_BEFORE_KILL_SECONDS` | int | `30` | Default seconds to wait before killing a container during st... |
| `ORCHESTRATOR_DEGRADATION_THRESHOLD_MINUTES` | int | `5` | Time in minutes before a service is marked as degraded |
| `ORCHESTRATOR_DOCKER_HOST` | string | `unix:///var/run/docker.sock` | Docker host for direct Docker API access |
| `ORCHESTRATOR_DOCKER_IMAGE_NAME` | string | `bannou:latest` | Docker image name for deployed Bannou containers |
| `ORCHESTRATOR_DOCKER_NETWORK` | string | `bannou_default` | Docker network name for deployed containers |
| `ORCHESTRATOR_HEALTH_CHECK_INTERVAL_MS` | int | `2000` | Interval in milliseconds between health checks during restar... |
| `ORCHESTRATOR_HEARTBEAT_TIMEOUT_SECONDS` | int | `90` | Service heartbeat timeout in seconds |
| `ORCHESTRATOR_HEARTBEAT_TTL_SECONDS` | int | `90` | TTL in seconds for service heartbeat entries in state store |
| `ORCHESTRATOR_KUBECONFIG_PATH` | string | **REQUIRED** | Path to kubeconfig file (null uses default ~/.kube/config) |
| `ORCHESTRATOR_KUBERNETES_NAMESPACE` | string | `default` | Kubernetes namespace for deployments |
| `ORCHESTRATOR_LOGS_VOLUME` | string | `logs-data` | Docker volume name for logs |
| `ORCHESTRATOR_OPENRESTY_HOST` | string | `openresty` | OpenResty hostname for cache invalidation calls |
| `ORCHESTRATOR_OPENRESTY_PORT` | int | `80` | OpenResty port for cache invalidation calls |
| `ORCHESTRATOR_OPENRESTY_REQUEST_TIMEOUT_SECONDS` | int | `5` | Timeout in seconds for OpenResty HTTP requests |
| `ORCHESTRATOR_PORTAINER_API_KEY` | string | **REQUIRED** | Portainer API key |
| `ORCHESTRATOR_PORTAINER_ENDPOINT_ID` | int | `1` | Portainer endpoint ID |
| `ORCHESTRATOR_PORTAINER_URL` | string | **REQUIRED** | Portainer API URL |
| `ORCHESTRATOR_PRESETS_HOST_PATH` | string | `/app/provisioning/orchestrator/presets` | Host path for orchestrator deployment presets |
| `ORCHESTRATOR_REDIS_CONNECTION_STRING` | string | `bannou-redis:6379` | Redis connection string for orchestrator state. |
| `ORCHESTRATOR_RESTART_TIMEOUT_SECONDS` | int | `120` | Default timeout in seconds for container restart operations |
| `ORCHESTRATOR_ROUTING_TTL_SECONDS` | int | `300` | TTL in seconds for service routing entries in state store |
| `ORCHESTRATOR_SECURE_WEBSOCKET` | bool | `true` | When true, publishes blank permission registration making or... |

### Permission

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `PERMISSION_MAX_CONCURRENT_RECOMPILATIONS` | int | `50` | Maximum number of concurrent session recompilations during s... |
| `PERMISSION_ROLE_HIERARCHY` | string[] | `['anonymous', 'user', 'developer', 'admin']` | Ordered role hierarchy from lowest to highest privilege. Ind... |
| `PERMISSION_SESSION_DATA_TTL_SECONDS` | int | `600` | Redis TTL in seconds for session permission data keys (state... |
| `PERMISSION_SESSION_LOCK_TIMEOUT_SECONDS` | int | `10` | Timeout in seconds for distributed locks during session stat... |

### Puppetmaster

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `PUPPETMASTER_ASSET_DOWNLOAD_TIMEOUT_SECONDS` | int | `30` | Timeout for downloading behavior YAML from asset service |
| `PUPPETMASTER_BEHAVIOR_CACHE_MAX_SIZE` | int | `1000` | Maximum number of behavior documents to cache in memory |
| `PUPPETMASTER_BEHAVIOR_CACHE_TTL_SECONDS` | int | `3600` | Time-to-live for cached behavior documents in seconds |
| `PUPPETMASTER_DEFAULT_WATCHER_TYPES` | string[] | `['regional']` | Default watcher types to start for each realm (comma-separat... |
| `PUPPETMASTER_SNAPSHOT_CACHE_TTL_SECONDS` | int | `300` | Time-to-live for cached resource snapshots in seconds (used ... |

### Quest

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `QUEST_DATA_CACHE_TTL_SECONDS` | int | `120` | TTL for in-memory quest data cache used by actor behavior ex... |
| `QUEST_DEFAULT_REWARD_CONTAINER_MAX_SLOTS` | int | `100` | Maximum slots in quest reward inventory containers |
| `QUEST_DEFINITION_CACHE_TTL_SECONDS` | int | `3600` | TTL for quest definition cache in Redis |
| `QUEST_LOCK_EXPIRY_SECONDS` | int | `30` | Distributed lock expiry for quest mutations |
| `QUEST_MAX_ACTIVE_QUESTS_PER_CHARACTER` | int | `25` | Maximum concurrent active quests per character |
| `QUEST_MAX_CONCURRENCY_RETRIES` | int | `5` | ETag concurrency retry attempts |
| `QUEST_PREREQUISITE_VALIDATION_MODE` | string | `CHECK_ALL` | Controls prerequisite validation behavior (FAIL_FAST stops o... |
| `QUEST_PROGRESS_CACHE_TTL_SECONDS` | int | `300` | TTL for objective progress cache |

### Realm

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `REALM_MERGE_LOCK_TIMEOUT_SECONDS` | int | `120` | Timeout in seconds for distributed lock during realm merge o... |
| `REALM_MERGE_PAGE_SIZE` | int | `50` | Page size for paginated entity migration during realm merge ... |
| `REALM_OPTIMISTIC_RETRY_ATTEMPTS` | int | `3` | Number of retry attempts for ETag-based optimistic concurren... |

### Realm History

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `REALM_HISTORY_ARCHIVE_SUMMARY_MAX_HISTORICAL_EVENTS` | int | `10` | Maximum number of major historical events to include in arch... |
| `REALM_HISTORY_ARCHIVE_SUMMARY_MAX_LORE_POINTS` | int | `10` | Maximum number of key lore points to include in archive text... |
| `REALM_HISTORY_INDEX_LOCK_TIMEOUT_SECONDS` | int | `15` | Timeout in seconds for distributed locks during index and lo... |
| `REALM_HISTORY_MAX_LORE_ELEMENTS` | int | `100` | Maximum number of lore elements allowed per realm. Prevents ... |

### Relationship

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `RELATIONSHIP_LOCK_TIMEOUT_SECONDS` | int | `30` | Timeout in seconds for distributed lock acquisition on index... |
| `RELATIONSHIP_TYPE_MAX_HIERARCHY_DEPTH` | int | `20` | Maximum depth for hierarchy traversal to prevent infinite lo... |
| `RELATIONSHIP_TYPE_MAX_MIGRATION_ERRORS_TO_TRACK` | int | `100` | Maximum number of individual migration error details to trac... |

### Resource

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `RESOURCE_CLEANUP_CALLBACK_TIMEOUT_SECONDS` | int | `30` | Timeout for each cleanup callback execution |
| `RESOURCE_CLEANUP_LOCK_EXPIRY_SECONDS` | int | `300` | Distributed lock timeout during cleanup execution |
| `RESOURCE_COMPRESSION_CALLBACK_TIMEOUT_SECONDS` | int | `60` | Timeout for each compression callback execution |
| `RESOURCE_COMPRESSION_LOCK_EXPIRY_SECONDS` | int | `600` | Distributed lock timeout during compression execution |
| `RESOURCE_DEFAULT_CLEANUP_POLICY` | string | `BEST_EFFORT` | Default cleanup policy when not specified per-resource-type |
| `RESOURCE_DEFAULT_COMPRESSION_POLICY` | string | `ALL_REQUIRED` | Default compression policy when not specified per-request |
| `RESOURCE_DEFAULT_GRACE_PERIOD_SECONDS` | int | `604800` | Default grace period in seconds before cleanup eligible (7 d... |
| `RESOURCE_SNAPSHOT_DEFAULT_TTL_SECONDS` | int | `3600` | Default TTL for snapshots when not specified in request (1 h... |
| `RESOURCE_SNAPSHOT_MAX_TTL_SECONDS` | int | `86400` | Maximum allowed TTL for snapshots (24 hours default, max 7 d... |
| `RESOURCE_SNAPSHOT_MIN_TTL_SECONDS` | int | `60` | Minimum allowed TTL for snapshots (1 minute default) |

### Save Load

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `SAVE_LOAD_ASSET_BUCKET` | string | `game-saves` | MinIO bucket for save assets |
| `SAVE_LOAD_ASYNC_UPLOAD_ENABLED` | bool | `true` | Queue uploads to MinIO/S3 instead of synchronous write. Save... |
| `SAVE_LOAD_AUTO_COLLAPSE_ENABLED` | bool | `true` | Automatically collapse delta chains during cleanup |
| `SAVE_LOAD_AUTO_COMPRESS_THRESHOLD_BYTES` | int | `1048576` | Auto-compress saves larger than this (default 1MB) |
| `SAVE_LOAD_BROTLI_COMPRESSION_LEVEL` | int | `6` | Brotli compression level (0-11, higher = better compression,... |
| `SAVE_LOAD_CLEANUP_CONTROL_PLANE_ONLY` | bool | `true` | Run scheduled cleanup only on control plane instance (Effect... |
| `SAVE_LOAD_CLEANUP_INTERVAL_MINUTES` | int | `60` | Interval for automatic cleanup task |
| `SAVE_LOAD_CLEANUP_STARTUP_DELAY_SECONDS` | int | `30` | Delay in seconds before cleanup service starts processing |
| `SAVE_LOAD_CONFLICT_DETECTION_ENABLED` | bool | `true` | Enable device-based conflict detection for cloud saves. Requ... |
| `SAVE_LOAD_CONFLICT_DETECTION_WINDOW_MINUTES` | int | `5` | Time window for considering saves as potentially conflicting... |
| `SAVE_LOAD_DEFAULT_COMPRESSION_BY_CATEGORY` | string | **REQUIRED** | Default compression per category as comma-separated KEY=VALU... |
| `SAVE_LOAD_DEFAULT_COMPRESSION_TYPE` | string | `GZIP` | Default compression algorithm |
| `SAVE_LOAD_DEFAULT_DELTA_ALGORITHM` | string | `JSON_PATCH` | Default algorithm for delta computation |
| `SAVE_LOAD_DEFAULT_MAX_VERSIONS_AUTO_SAVE` | int | `5` | Default max versions for AUTO_SAVE category |
| `SAVE_LOAD_DEFAULT_MAX_VERSIONS_CHECKPOINT` | int | `20` | Default max versions for CHECKPOINT category |
| `SAVE_LOAD_DEFAULT_MAX_VERSIONS_MANUAL_SAVE` | int | `10` | Default max versions for MANUAL_SAVE category |
| `SAVE_LOAD_DEFAULT_MAX_VERSIONS_QUICK_SAVE` | int | `1` | Default max versions for QUICK_SAVE category |
| `SAVE_LOAD_DEFAULT_MAX_VERSIONS_STATE_SNAPSHOT` | int | `3` | Default max versions for STATE_SNAPSHOT category |
| `SAVE_LOAD_DELTA_SAVES_ENABLED` | bool | `true` | Enable delta/incremental save support |
| `SAVE_LOAD_DELTA_SIZE_THRESHOLD_PERCENT` | int | `50` | If delta is larger than this percent of full save, store as ... |
| `SAVE_LOAD_GZIP_COMPRESSION_LEVEL` | int | `6` | GZIP compression level (1-9, higher = better compression, sl... |
| `SAVE_LOAD_HOT_CACHE_TTL_MINUTES` | int | `60` | TTL for hot cache entries in minutes |
| `SAVE_LOAD_MAX_CONCURRENT_UPLOADS` | int | `10` | Maximum concurrent uploads to storage backend (semaphore). P... |
| `SAVE_LOAD_MAX_DELTA_CHAIN_LENGTH` | int | `10` | Maximum number of deltas before forcing collapse. Longer cha... |
| `SAVE_LOAD_MAX_SAVES_PER_MINUTE` | int | `10` | Rate limit - maximum saves per owner per minute |
| `SAVE_LOAD_MAX_SAVE_SIZE_BYTES` | int | `104857600` | Maximum size for a single save in bytes (default 100MB) |
| `SAVE_LOAD_MAX_SLOTS_PER_OWNER` | int | `100` | Maximum save slots per owner entity |
| `SAVE_LOAD_MAX_TOTAL_SIZE_BYTES_PER_OWNER` | int | `1073741824` | Maximum total storage per owner (default 1GB) |
| `SAVE_LOAD_MIGRATIONS_ENABLED` | bool | `true` | Enable/disable schema migrations entirely |
| `SAVE_LOAD_MIGRATION_MAX_PATCH_OPERATIONS` | int | `1000` | Maximum JSON Patch operations per migration (safety limit) |
| `SAVE_LOAD_MIN_BASE_SIZE_FOR_DELTA_THRESHOLD_BYTES` | int | `1024` | Minimum base save size in bytes before applying delta thresh... |
| `SAVE_LOAD_PENDING_UPLOAD_TTL_MINUTES` | int | `60` | TTL for pending uploads in Redis. If upload fails repeatedly... |
| `SAVE_LOAD_SESSION_CLEANUP_GRACE_PERIOD_MINUTES` | int | `5` | Grace period before cleaning up SESSION-owned saves after se... |
| `SAVE_LOAD_STORAGE_CIRCUIT_BREAKER_ENABLED` | bool | `true` | Enable circuit breaker for storage backend (MinIO/S3). When ... |
| `SAVE_LOAD_STORAGE_CIRCUIT_BREAKER_HALF_OPEN_ATTEMPTS` | int | `2` | Successful uploads needed in half-open state to close circui... |
| `SAVE_LOAD_STORAGE_CIRCUIT_BREAKER_RESET_SECONDS` | int | `30` | Seconds before attempting to close circuit (half-open state) |
| `SAVE_LOAD_STORAGE_CIRCUIT_BREAKER_THRESHOLD` | int | `5` | Number of consecutive failures before circuit opens |
| `SAVE_LOAD_THUMBNAIL_ALLOWED_FORMATS` | string | `image/jpeg,image/webp,image/png` | Comma-separated list of allowed thumbnail MIME types |
| `SAVE_LOAD_THUMBNAIL_MAX_SIZE_BYTES` | int | `262144` | Maximum thumbnail size in bytes (default 256KB) |
| `SAVE_LOAD_UPLOAD_BATCH_INTERVAL_MS` | int | `100` | Interval between upload batch processing cycles |
| `SAVE_LOAD_UPLOAD_BATCH_SIZE` | int | `5` | Number of pending uploads to process per batch cycle |
| `SAVE_LOAD_UPLOAD_RETRY_ATTEMPTS` | int | `3` | Number of retry attempts for failed uploads before giving up |
| `SAVE_LOAD_UPLOAD_RETRY_DELAY_MS` | int | `1000` | Base delay between retry attempts (exponential backoff appli... |

### Scene

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `SCENE_CHECKOUT_TTL_BUFFER_MINUTES` | int | `5` | Buffer time added to checkout TTL for state store expiry (gr... |
| `SCENE_DEFAULT_CHECKOUT_TTL_MINUTES` | int | `60` | Default lock TTL for checkout operations in minutes |
| `SCENE_DEFAULT_MAX_REFERENCE_DEPTH` | int | `3` | Default maximum depth for reference resolution (prevents inf... |
| `SCENE_MAX_CHECKOUT_EXTENSIONS` | int | `10` | Maximum number of times a checkout can be extended |
| `SCENE_MAX_LIST_RESULTS` | int | `200` | Maximum results returned in a single list query |
| `SCENE_MAX_NODE_COUNT` | int | `10000` | Maximum nodes allowed in a single scene |
| `SCENE_MAX_REFERENCE_DEPTH_LIMIT` | int | `10` | Hard limit on reference depth that cannot be exceeded by req... |
| `SCENE_MAX_SEARCH_RESULTS` | int | `100` | Maximum results returned in a single search query |
| `SCENE_MAX_TAGS_PER_NODE` | int | `20` | Maximum tags allowed per node |
| `SCENE_MAX_TAGS_PER_SCENE` | int | `50` | Maximum tags allowed per scene |
| `SCENE_MAX_VERSION_RETENTION_COUNT` | int | `100` | Maximum versions that can be retained (configurable per game... |

### Seed

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `SEED_BOND_SHARED_GROWTH_MULTIPLIER` | double | `1.5` | Growth multiplier applied when bonded seeds grow together in... |
| `SEED_BOND_STRENGTH_GROWTH_RATE` | double | `0.1` | Rate at which bond strength increases per unit of shared gro... |
| `SEED_CAPABILITY_RECOMPUTE_DEBOUNCE_MS` | int | `5000` | Debounce interval in milliseconds before recomputing capabil... |
| `SEED_DECAY_WORKER_INTERVAL_SECONDS` | int | `900` | Interval in seconds between growth decay worker cycles (defa... |
| `SEED_DECAY_WORKER_STARTUP_DELAY_SECONDS` | int | `30` | Delay in seconds before the decay worker starts its first cy... |
| `SEED_DEFAULT_MAX_SEEDS_PER_OWNER` | int | `3` | Default maximum seeds of any single type per owner when not ... |
| `SEED_DEFAULT_QUERY_PAGE_SIZE` | int | `100` | Default page size for queries that do not expose pagination ... |
| `SEED_GROWTH_DECAY_ENABLED` | bool | `false` | Global toggle for growth domain decay. Per-type overrides ca... |
| `SEED_GROWTH_DECAY_RATE_PER_DAY` | double | `0.01` | Global daily decay rate applied to unused growth domains. Pe... |
| `SEED_MAX_SEED_TYPES_PER_GAME_SERVICE` | int | `50` | Maximum number of seed types that can be registered per game... |
| `SEED_SEED_DATA_CACHE_TTL_SECONDS` | int | `60` | TTL in seconds for the seed data cache used by the variable ... |

### Species

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `SPECIES_LOCK_TIMEOUT_SECONDS` | int | `30` | Timeout in seconds for distributed lock acquisition on speci... |
| `SPECIES_MERGE_PAGE_SIZE` | int | `100` | Number of characters to process per page during species merg... |

### State

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `STATE_CONNECTION_RETRY_COUNT` | int | `10` | Maximum number of connection retry attempts for MySQL initia... |
| `STATE_CONNECTION_TIMEOUT_SECONDS` | int | `60` | Total timeout in seconds for establishing Redis/MySQL connec... |
| `STATE_ENABLE_ERROR_EVENT_PUBLISHING` | bool | `true` | Enable publishing error events when state store operations f... |
| `STATE_ERROR_EVENT_DEDUPLICATION_WINDOW_SECONDS` | int | `60` | Time window in seconds for deduplicating identical error eve... |
| `STATE_INMEMORY_FALLBACK_LIMIT` | int | `10000` | Maximum entries for in-memory LINQ fallback before throwing ... |
| `STATE_MIN_RETRY_DELAY_MS` | int | `1000` | Minimum delay in milliseconds between MySQL connection retry... |
| `STATE_MYSQL_CONNECTION_STRING` | string | `server=bannou-mysql;database=bannou;user=guest;password=guest` (insecure) | MySQL connection string for MySQL-backed state stores |
| `STATE_REDIS_CONNECTION_STRING` | string | `bannou-redis:6379` | Redis connection string (host:port format) for Redis-backed ... |
| `STATE_SQLITE_DATA_PATH` | string | `./data/state` | Directory path for SQLite database files. Each MySQL-configu... |
| `STATE_USE_INMEMORY` | bool | `false` | Use in-memory storage instead of Redis/MySQL. Data is NOT pe... |
| `STATE_USE_SQLITE` | bool | `false` | Use SQLite file storage instead of MySQL for SQL-backed stor... |

### Status

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `STATUS_CACHE_WARMING_ENABLED` | bool | `false` | Enable proactive cache warming on startup by pre-loading act... |
| `STATUS_DEFAULT_PAGE_SIZE` | int | `50` | Default page size for paginated queries |
| `STATUS_DEFAULT_STATUS_DURATION_SECONDS` | int | `60` | Default duration when template has no defaultDurationSeconds... |
| `STATUS_LOCK_ACQUISITION_TIMEOUT_SECONDS` | int | `5` | Maximum seconds to wait when acquiring a distributed lock be... |
| `STATUS_LOCK_TIMEOUT_SECONDS` | int | `30` | TTL for distributed locks on status mutations |
| `STATUS_MAX_CACHED_ENTITIES` | int | `10000` | Maximum number of entity active-status cache entries retaine... |
| `STATUS_MAX_STACKS_PER_STATUS` | int | `10` | Global maximum stack count per status (template maxStacks ta... |
| `STATUS_MAX_STATUSES_PER_ENTITY` | int | `50` | Maximum concurrent active statuses per entity |
| `STATUS_MAX_STATUS_TEMPLATES_PER_GAME_SERVICE` | int | `200` | Maximum status template definitions per game service |
| `STATUS_SEED_EFFECTS_CACHE_TTL_SECONDS` | int | `300` | TTL in seconds for seed-derived effects cache (longer becaus... |
| `STATUS_SEED_EFFECTS_ENABLED` | bool | `true` | Enable seed-derived passive effects in unified queries (disa... |
| `STATUS_STATUS_CACHE_TTL_SECONDS` | int | `60` | TTL in seconds for active status cache per entity (short bec... |

### Storyline

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `STORYLINE_CONFIDENCE_ACTION_COUNT_BONUS` | double | `0.15` | Confidence bonus when action count is within acceptable rang... |
| `STORYLINE_CONFIDENCE_BASE_SCORE` | double | `0.5` | Base confidence score before any bonuses are applied.
 |
| `STORYLINE_CONFIDENCE_CORE_EVENT_BONUS` | double | `0.15` | Confidence bonus when plan contains core events.
 |
| `STORYLINE_CONFIDENCE_MAX_ACTION_COUNT` | int | `20` | Maximum action count for action count bonus.
 |
| `STORYLINE_CONFIDENCE_MIN_ACTION_COUNT` | int | `5` | Minimum action count for action count bonus.
 |
| `STORYLINE_CONFIDENCE_PHASE_BONUS` | double | `0.2` | Confidence bonus when phase threshold is met.
 |
| `STORYLINE_CONFIDENCE_PHASE_THRESHOLD` | int | `3` | Minimum number of phases to receive a phase count bonus.
 |
| `STORYLINE_DEFAULT_GENRE` | string | `drama` | Default genre when not specified and cannot be inferred.
 |
| `STORYLINE_DEFAULT_PLANNING_URGENCY` | string | `medium` | Default urgency tier for GOAP planning.
low = more iteration... |
| `STORYLINE_MAX_SEED_SOURCES` | int | `10` | Maximum number of seed sources per compose request.
 |
| `STORYLINE_PLAN_CACHE_ENABLED` | bool | `true` | Whether to cache deterministic plans (those with explicit se... |
| `STORYLINE_PLAN_CACHE_TTL_SECONDS` | int | `3600` | TTL in seconds for cached composed plans.
Default: 3600 (1 h... |
| `STORYLINE_RISK_MIN_ACTION_THRESHOLD` | int | `3` | Minimum action count before "thin_content" risk is flagged.
 |
| `STORYLINE_RISK_MIN_PHASE_THRESHOLD` | int | `2` | Minimum phase count before "flat_arc" risk is flagged.
 |
| `STORYLINE_SCENARIO_BACKSTORY_MATCH_BONUS` | double | `0.1` | Bonus added to fit score for each matching backstory conditi... |
| `STORYLINE_SCENARIO_COOLDOWN_DEFAULT_SECONDS` | int | `86400` | Default cooldown in seconds before a scenario can trigger ag... |
| `STORYLINE_SCENARIO_DEFINITION_CACHE_TTL_SECONDS` | int | `300` | TTL in seconds for cached scenario definitions (Redis read-t... |
| `STORYLINE_SCENARIO_FIT_SCORE_BASE_WEIGHT` | double | `0.5` | Base weight for scenario fit score calculation.
Applied when... |
| `STORYLINE_SCENARIO_FIT_SCORE_MINIMUM_THRESHOLD` | double | `0.3` | Minimum fit score required for a scenario to be considered a... |
| `STORYLINE_SCENARIO_FIT_SCORE_RECOMMEND_THRESHOLD` | double | `0.7` | Fit score threshold above which immediate trigger is recomme... |
| `STORYLINE_SCENARIO_IDEMPOTENCY_TTL_SECONDS` | int | `3600` | TTL in seconds for idempotency keys to prevent duplicate tri... |
| `STORYLINE_SCENARIO_LOCATION_MATCH_BONUS` | double | `0.08` | Bonus added to fit score for matching location condition.
 |
| `STORYLINE_SCENARIO_MAX_ACTIVE_PER_CHARACTER` | int | `3` | Maximum number of active (in-progress) scenarios per charact... |
| `STORYLINE_SCENARIO_RELATIONSHIP_MATCH_BONUS` | double | `0.12` | Bonus added to fit score for each matching relationship cond... |
| `STORYLINE_SCENARIO_TRAIT_MATCH_BONUS` | double | `0.15` | Bonus added to fit score for each matching trait condition.
 |
| `STORYLINE_SCENARIO_TRIGGER_LOCK_TIMEOUT_SECONDS` | int | `30` | Timeout in seconds for the distributed lock during scenario ... |
| `STORYLINE_SCENARIO_WORLD_STATE_MATCH_BONUS` | double | `0.05` | Bonus added to fit score for matching world state conditions... |

### Subscription

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `SUBSCRIPTION_EXPIRATION_CHECK_INTERVAL_MINUTES` | int | `5` | Interval in minutes between subscription expiration checks |
| `SUBSCRIPTION_EXPIRATION_GRACE_PERIOD_SECONDS` | int | `30` | Grace period in seconds before expired subscriptions are mar... |
| `SUBSCRIPTION_LOCK_TIMEOUT_SECONDS` | int | `10` | Maximum time in seconds to wait for a distributed lock acqui... |
| `SUBSCRIPTION_STARTUP_DELAY_SECONDS` | int | `30` | Delay in seconds before background service starts processing |

### Telemetry

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `TELEMETRY_DEPLOYMENT_ENVIRONMENT` | string | `development` | Deployment environment (development, staging, production) |
| `TELEMETRY_METRICS_ENABLED` | bool | `true` | Enable metrics export via Prometheus scraping endpoint (/met... |
| `TELEMETRY_OTLP_ENDPOINT` | string | `http://localhost:4317` | OTLP exporter endpoint (gRPC or HTTP) |
| `TELEMETRY_OTLP_PROTOCOL` | string | `grpc` | OTLP transport protocol |
| `TELEMETRY_SERVICE_NAME` | string | **REQUIRED** | Service name for telemetry (defaults to effective app-id if ... |
| `TELEMETRY_SERVICE_NAMESPACE` | string | `bannou` | Service namespace for telemetry grouping |
| `TELEMETRY_TRACING_ENABLED` | bool | `true` | Enable distributed tracing export |
| `TELEMETRY_TRACING_SAMPLING_RATIO` | double | `1.0` | Trace sampling ratio (0.0-1.0). Use 1.0 for full sampling in... |

### Transit

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `TRANSIT_AUTO_UPDATE_LOCATION_ON_TRANSITION` | bool | `true` | When true, Transit automatically calls Location's /location/... |
| `TRANSIT_CARGO_SPEED_PENALTY_THRESHOLD_KG` | double | `100.0` | Cargo weight above this threshold reduces speed. Speed reduc... |
| `TRANSIT_CONNECTION_GRAPH_CACHE_SECONDS` | int | `300` | TTL for cached connection graph in Redis (seconds). Graph is... |
| `TRANSIT_DEFAULT_CARGO_SPEED_PENALTY_RATE` | double | `0.3` | Default cargo speed penalty rate applied when a mode's cargo... |
| `TRANSIT_DEFAULT_FALLBACK_MODE_CODE` | string | `walking` | Default fallback mode code used when no compatible transit m... |
| `TRANSIT_DEFAULT_WALKING_SPEED_KM_PER_GAME_HOUR` | double | `5.0` | Default walking speed used when no mode is specified |
| `TRANSIT_DISCOVERY_CACHE_TTL_SECONDS` | int | `600` | TTL for per-entity discovery cache in Redis (seconds). Route... |
| `TRANSIT_JOURNEY_ARCHIVAL_WORKER_INTERVAL_SECONDS` | int | `300` | Real-time interval in seconds between journey archival worke... |
| `TRANSIT_JOURNEY_ARCHIVAL_WORKER_STARTUP_DELAY_SECONDS` | int | `10` | Startup delay in seconds before the journey archival worker ... |
| `TRANSIT_JOURNEY_ARCHIVE_AFTER_GAME_HOURS` | double | `168.0` | Game-hours after arrival/abandonment before journey is archi... |
| `TRANSIT_JOURNEY_ARCHIVE_RETENTION_DAYS` | int | `365` | Number of real-time days to retain archived journeys in MySQ... |
| `TRANSIT_LOCK_TIMEOUT_SECONDS` | int | `10` | Timeout in seconds for distributed lock acquisition on mode ... |
| `TRANSIT_MAX_PREFERENCE_COST` | double | `2.0` | Maximum allowed preference cost after aggregating all DI cos... |
| `TRANSIT_MAX_ROUTE_CALCULATION_LEGS` | int | `8` | Maximum number of legs to consider in route calculation (pre... |
| `TRANSIT_MAX_ROUTE_OPTIONS` | int | `5` | Maximum number of route options to return from calculate end... |
| `TRANSIT_MAX_SPEED_MULTIPLIER` | double | `3.0` | Maximum allowed speed multiplier after aggregating all DI co... |
| `TRANSIT_MINIMUM_EFFECTIVE_SPEED_KM_PER_GAME_HOUR` | double | `0.01` | Minimum effective speed floor in km/game-hour applied after ... |
| `TRANSIT_MIN_PREFERENCE_COST` | double | `0.0` | Minimum allowed preference cost after aggregating all DI cos... |
| `TRANSIT_MIN_SPEED_MULTIPLIER` | double | `0.1` | Minimum allowed speed multiplier after aggregating all DI co... |
| `TRANSIT_SEASONAL_CONNECTION_CHECK_INTERVAL_SECONDS` | int | `60` | How often the background worker checks Worldstate for season... |
| `TRANSIT_SEASONAL_WORKER_STARTUP_DELAY_SECONDS` | int | `5` | Startup delay in seconds before the seasonal connection work... |

### Voice

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `VOICE_AD_HOC_ROOMS_ENABLED` | bool | `false` | If true, joining a non-existent room auto-creates it with au... |
| `VOICE_BROADCAST_CONSENT_TIMEOUT_SECONDS` | int | `30` | Seconds to wait for all participants to respond before auto-... |
| `VOICE_EMPTY_ROOM_GRACE_PERIOD_SECONDS` | int | `300` | Seconds an empty autoCleanup room persists before auto-delet... |
| `VOICE_EVICTION_WORKER_INITIAL_DELAY_SECONDS` | int | `10` | Seconds to wait after startup before the first eviction cycl... |
| `VOICE_KAMAILIO_HOST` | string | `localhost` | Kamailio SIP server host |
| `VOICE_KAMAILIO_REQUEST_TIMEOUT_SECONDS` | int | `5` | Timeout in seconds for Kamailio service requests |
| `VOICE_KAMAILIO_RPC_PORT` | int | `5080` | Kamailio JSON-RPC port (typically 5080, not SIP port 5060) |
| `VOICE_KAMAILIO_SIP_PORT` | int | `5060` | Kamailio SIP signaling port for client registration |
| `VOICE_P2P_MAX_PARTICIPANTS` | int | `8` | Maximum participants in P2P voice sessions |
| `VOICE_PARTICIPANT_EVICTION_CHECK_INTERVAL_SECONDS` | int | `15` | How often the background worker checks for stale participant... |
| `VOICE_PARTICIPANT_HEARTBEAT_TIMEOUT_SECONDS` | int | `60` | Seconds of missed heartbeats before participant is evicted |
| `VOICE_RTPENGINE_HOST` | string | `localhost` | RTPEngine media relay host |
| `VOICE_RTPENGINE_PORT` | int | `22222` | RTPEngine control port |
| `VOICE_RTPENGINE_TIMEOUT_SECONDS` | int | `5` | Timeout in seconds for RTPEngine UDP requests |
| `VOICE_SCALED_MAX_PARTICIPANTS` | int | `100` | Maximum participants in scaled tier voice sessions |
| `VOICE_SCALED_TIER_ENABLED` | bool | `false` | Enable scaled tier voice communication (SIP-based) |
| `VOICE_SIP_CREDENTIAL_EXPIRATION_HOURS` | int | `24` | Hours until SIP credentials expire (clients should re-authen... |
| `VOICE_SIP_DOMAIN` | string | `voice.bannou.local` | SIP domain for voice communication |
| `VOICE_SIP_PASSWORD_SALT` | string | **REQUIRED** | Salt for SIP password generation. Required only when ScaledT... |
| `VOICE_STUN_SERVERS` | string | `stun:stun.l.google.com:19302` | Comma-separated list of STUN server URLs for WebRTC |
| `VOICE_TIER_UPGRADE_ENABLED` | bool | `false` | Enable automatic tier upgrade from P2P to scaled |
| `VOICE_TIER_UPGRADE_MIGRATION_DEADLINE_MS` | int | `30000` | Migration deadline in milliseconds when upgrading tiers |

### Worldstate

| Environment Variable | Type | Default | Description |
|---------------------|------|---------|-------------|
| `WORLDSTATE_BOUNDARY_EVENT_BATCH_SIZE` | int | `50` | Maximum boundary events published per tick. Prevents event f... |
| `WORLDSTATE_CALENDAR_CACHE_TTL_MINUTES` | int | `60` | TTL for in-memory calendar template cache. Calendar structur... |
| `WORLDSTATE_CLOCK_CACHE_TTL_SECONDS` | int | `10` | TTL for in-memory clock cache used by the variable provider.... |
| `WORLDSTATE_CLOCK_TICK_INTERVAL_SECONDS` | int | `5` | Real-time seconds between clock advancement ticks. At 24:1 r... |
| `WORLDSTATE_CLOCK_TICK_STARTUP_DELAY_SECONDS` | int | `10` | Seconds to wait after startup before first clock tick, allow... |
| `WORLDSTATE_DEFAULT_CALENDAR_TEMPLATE_CODE` | string | **REQUIRED** | Default calendar template code used as fallback when Initial... |
| `WORLDSTATE_DEFAULT_DOWNTIME_POLICY` | string | `Advance` | Default downtime handling policy for new realm clocks. Advan... |
| `WORLDSTATE_DEFAULT_TIME_RATIO` | double | `24.0` | Default game-seconds per real-second for new realm clocks. 2... |
| `WORLDSTATE_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | int | `10` | Timeout in seconds for distributed lock acquisition during c... |
| `WORLDSTATE_MAX_BATCH_REALM_TIME_QUERIES` | int | `100` | Maximum number of realm IDs accepted in a single BatchGetRea... |
| `WORLDSTATE_MAX_CALENDARS_PER_GAME_SERVICE` | int | `10` | Safety limit on calendar templates per game service. |
| `WORLDSTATE_MAX_CATCH_UP_GAME_DAYS` | int | `365` | Maximum game-days to advance during catch-up on startup. Pre... |

## Configuration Summary

- **Total properties**: 959
- **Required (no default)**: 58
- **Optional (has default)**: 901

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
