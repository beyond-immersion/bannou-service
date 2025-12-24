# Configuration Fuckups

## Part 1: Direct `Environment.GetEnvironmentVariable` Calls

Direct calls that bypass the configuration system.

## Manual Code (Needs Review)

### lib-asset/AssetServicePlugin.cs
- Line 50-51: `BANNOU_MINIO_ENDPOINT`, `MINIO_ENDPOINT`
- Line 55-57: `BANNOU_MINIO_ACCESS_KEY`, `MINIO_ACCESS_KEY`, `MINIO_ROOT_USER`
- Line 61-63: `BANNOU_MINIO_SECRET_KEY`, `MINIO_SECRET_KEY`, `MINIO_ROOT_PASSWORD`
- Line 67-68: `BANNOU_MINIO_USE_SSL`, `MINIO_USE_SSL`
- Line 72-73: `BANNOU_MINIO_DEFAULT_BUCKET`, `MINIO_DEFAULT_BUCKET`
- Line 77-78: `BANNOU_MINIO_REGION`, `MINIO_REGION`
- Line 134-135: `ASSET_PROCESSING_MODE`, `BANNOU_ASSET_PROCESSING_MODE`

### lib-voice/VoiceServicePlugin.cs
- Line 53-54: `BANNOU_KAMAILIOHOST`, `KAMAILIOHOST`
- Line 56-57: `BANNOU_KAMAILIORPCPORT`, `KAMAILIORPCPORT`
- Line 65-66: `BANNOU_RTPENGINEHOST`, `RTPENGINEHOST`
- Line 68-69: `BANNOU_RTPENGINEPORT`, `RTPENGINEPORT`

### lib-state/StateServicePlugin.cs
- Line 47: `STATE_REDIS_CONNECTION`
- Line 48: `STATE_MYSQL_CONNECTION`

### lib-connect/ClientEvents/ClientEventRabbitMQSubscriber.cs
- Line 66-67: `BANNOU_RabbitMqConnectionString`, `RabbitMqConnectionString`

### lib-connect/ConnectService.cs
- Line 974: `DAPR_HTTP_ENDPOINT`

### lib-connect/Protocol/GuidGenerator.cs
- Line 32: `BANNOU_SERVER_SALT`

### lib-orchestrator/OrchestratorServicePlugin.cs
- Line 32: `ORCHESTRATOR_SERVICE_ENABLED`
- Line 41: `DAPR_APP_ID`

### lib-orchestrator/OrchestratorRedisManager.cs
- Line 51-52: `BANNOU_RedisConnectionString`, `RedisConnectionString`

### lib-orchestrator/PresetLoader.cs
- Line 37: `BANNOU_PRESETS_DIR`

### lib-orchestrator/Backends/BackendDetector.cs
- Line 145: `KUBECONFIG`

### lib-orchestrator/Backends/KubernetesOrchestrator.cs
- Line 60: `KUBECONFIG`

### lib-orchestrator/Backends/DockerComposeOrchestrator.cs
- Line 95: `BANNOU_DockerNetwork`
- Line 97: `BANNOU_DaprComponentsHostPath`
- Line 100: `BANNOU_DaprComponentsContainerPath`
- Line 102: `BANNOU_DaprImage`
- Line 104: `BANNOU_PlacementHost`
- Line 106: `BANNOU_CertificatesHostPath`
- Line 108: `BANNOU_PresetsHostPath`
- Line 110: `BANNOU_LogsVolume`
- Line 882: `DAPR_APP_ID`

### lib-mesh/Services/MeshRedisManager.cs
- Line 40-42: `MESH_REDIS_CONNECTION_STRING`, `BANNOU_RedisConnectionString`, `RedisConnectionString`

### bannou-service/Program.cs
- Line 117: `DAPR_GRPC_ENDPOINT`
- Line 125: `DAPR_HTTP_ENDPOINT`
- Line 379: `HEARTBEAT_ENABLED`
- Line 650: `DAPR_HTTP_ENDPOINT`

### bannou-service/Plugins/PluginLoader.cs
- Line 145: `SERVICES_ENABLED`
- Line 156: `{SERVICE}_SERVICE_DISABLED`
- Line 167: `{SERVICE}_SERVICE_ENABLED`

### bannou-service/Protocol/GuidGenerator.cs
- Line 33: `BANNOU_SERVER_SALT`

### bannou-service/Services/ServiceHeartbeatManager.cs
- Line 92-93: `DAPR_APP_ID`, `APP_ID`
- Line 99: `DAPR_HTTP_ENDPOINT`
- Line 108: `DAPR_HTTP_PORT`
- Line 114: `HEARTBEAT_INTERVAL_SECONDS`
- Line 120: `PERMISSION_HEARTBEAT_ENABLED`

### bannou-service/Services/IDaprService.cs
- Line 397: `SERVICES_ENABLED`
- Line 407: `{SERVICE}_SERVICE_DISABLED`
- Line 414: `{SERVICE}_SERVICE_ENABLED`

### bannou-service/ServiceClients/DaprServiceClientBase.cs
- Line 95: `DAPR_HTTP_ENDPOINT`

### bannou-service/Events/ServiceErrorPublisher.cs
- Line 44: `DAPR_APP_ID`

## Test Code

### http-tester/Program.cs
- Line 72: `DAEMON_MODE`
- Line 141: `DAPR_HTTP_ENDPOINT`
- Line 174: `DAPR_GRPC_ENDPOINT`
- Line 638: `PLUGIN`

### http-tester/Tests/TestingTestHandler.cs
- Line 23: `DAPR_HTTP_ENDPOINT`
- Line 317: `DAPR_HTTP_PORT`

### edge-tester/Program.cs
- Line 97: `DAEMON_MODE`

### edge-tester/Tests/VoiceWebSocketTestHandler.cs
- Line 34: `VOICE_TESTS_ENABLED`
- Line 1160: `BANNOU_P2PMAXPARTICIPANTS`

### lib-mesh.tests/MeshServiceTests.cs
- Line 1012: `MESH_REDIS_CONNECTION_STRING`

### lib-testing/DaprServiceMappingTestHandler.cs
- Line 162: `DAPR_HTTP_ENDPOINT`

## Generated Code (Fix in generation templates)

All `*PermissionRegistration.cs` files use `DAPR_APP_ID`:
- lib-accounts/Generated/AccountsPermissionRegistration.cs:42
- lib-realm/Generated/RealmPermissionRegistration.cs:42
- lib-asset/Generated/AssetPermissionRegistration.cs:42
- lib-location/Generated/LocationPermissionRegistration.cs:42
- lib-relationship/Generated/RelationshipPermissionRegistration.cs:42
- lib-documentation/Generated/DocumentationPermissionRegistration.cs:42
- lib-subscriptions/Generated/SubscriptionsPermissionRegistration.cs:42
- lib-voice/Generated/VoicePermissionRegistration.cs:42
- lib-permissions/Generated/PermissionsPermissionRegistration.cs:42
- lib-behavior/Generated/BehaviorPermissionRegistration.cs:42
- lib-website/Generated/WebsitePermissionRegistration.cs:42
- lib-species/Generated/SpeciesPermissionRegistration.cs:42
- lib-character/Generated/CharacterPermissionRegistration.cs:42
- lib-relationship-type/Generated/RelationshipTypePermissionRegistration.cs:42
- lib-game-session/Generated/GameSessionPermissionRegistration.cs:42
- lib-servicedata/Generated/ServicedataPermissionRegistration.cs:42
- lib-state/Generated/StatePermissionRegistration.cs:42
- lib-mesh/Generated/MeshPermissionRegistration.cs:42
- lib-testing/TestingPermissionRegistration.cs:41
- lib-connect/Generated/ConnectPermissionRegistration.cs:42
- lib-auth/Generated/AuthPermissionRegistration.cs:42
- lib-orchestrator/Generated/OrchestratorPermissionRegistration.cs:42
- lib-messaging/Generated/MessagingPermissionRegistration.cs:42

**Fix required in**: `scripts/generate-all-services.sh` or NSwag templates in `templates/nswag/`

---

## Part 2: Hardcoded Configuration Defaults

Fallback defaults that are holding the system together with tape.

### CRITICAL: Hardcoded Credentials/Secrets

#### lib-connect/ClientEvents/ClientEventRabbitMQSubscriber.cs
- Line 68: `?? "amqp://guest:guest@rabbitmq:5672"` - **HARDCODED CREDENTIALS**

#### lib-messaging/MessagingServicePlugin.cs
- Line 40: `?? "rabbitmq"` - default host
- Line 42: `?? "guest"` - **HARDCODED USERNAME**
- Line 43: `?? "guest"` - **HARDCODED PASSWORD**

#### lib-orchestrator/Generated/OrchestratorServiceConfiguration.cs
- Line 76: `= "amqp://guest:guest@localhost:5672/"` - **HARDCODED CREDENTIALS IN GENERATED CODE**

#### bannou-service/Program.cs
- Line 166: `?? "default-fallback-secret-key-for-development"` - **DEFAULT JWT SECRET**

#### lib-voice/Services/ScaledTierCoordinator.cs
- Line 75: `?? "bannou-voice-default-salt"` - **DEFAULT SECURITY SALT**

### Redis Connection Strings

#### lib-mesh/Services/MeshRedisManager.cs
- Line 43: `?? "redis:6379"`

#### lib-orchestrator/OrchestratorRedisManager.cs
- Line 53: `?? "redis:6379"`

#### lib-state/StateServicePlugin.cs
- Line 47: `?? "bannou-redis:6379"`

#### lib-state/Services/StateStoreFactory.cs
- Line 45: `= "bannou-redis:6379"`

#### lib-mesh/Generated/MeshServiceConfiguration.cs
- Line 22: `= "localhost:6379"` - generated default

#### lib-orchestrator/Generated/OrchestratorServiceConfiguration.cs
- Line 70: `= "localhost:6379"` - generated default

### Dapr Endpoint Defaults

#### bannou-service/Program.cs
- Line 651: `?? "http://127.0.0.1:3500"`

#### bannou-service/ServiceClients/DaprServiceClientBase.cs
- Line 95: `?? "http://localhost:3500"`

#### http-tester/Program.cs
- Line 141: `?? "http://localhost:3500"`
- Line 174: `?? "http://localhost:50001"`

#### http-tester/Tests/TestingTestHandler.cs
- Line 23: `?? "http://localhost:5012"`

#### lib-testing/DaprServiceMappingTestHandler.cs
- Line 162: `?? "localhost:3500"`

### Voice Service Defaults

#### lib-voice/Generated/VoiceServiceConfiguration.cs
- Line 70: `= "localhost"` - KamailioHost default
- Line 82: `= "localhost"` - RtpEngineHost default

#### lib-voice/VoiceServicePlugin.cs
- Line 55: `?? "localhost"` - KamailioHost fallback
- Line 67: `?? "localhost"` - RtpEngineHost fallback

#### lib-voice/Services/ScaledTierCoordinator.cs
- Line 83: `?? "voice.bannou"` - SipDomain default
- Line 95: `?? "localhost"` - KamailioHost fallback
- Line 141: `?? "localhost"` - RtpEngineHost fallback

### Orchestrator/Docker Defaults

#### lib-orchestrator/Backends/DockerComposeOrchestrator.cs
- Line 96: `?? "bannou_default"` - Docker network
- Line 103: `?? "daprio/daprd:1.16.3"` - Dapr image version
- Line 105: `?? "placement:50006"` - Placement host
- Line 111: `?? "logs-data"` - Logs volume name

#### lib-orchestrator/SmartRestartManager.cs
- Line 42: `?? "unix:///var/run/docker.sock"` - Docker socket path

#### lib-orchestrator/Backends/KubernetesOrchestrator.cs
- Line 70: `?? "default"` - Kubernetes namespace

### State Store Defaults

#### lib-servicedata/ServicedataService.cs
- Line 48: `?? "servicedata-statestore"`

#### lib-subscriptions/SubscriptionsService.cs
- Line 57: `?? "subscriptions-statestore"`
- Line 58: `?? "authorized"`

### Messaging Service Defaults

#### lib-messaging/Generated/MessagingServiceConfiguration.cs
- Line 22: `= "rabbitmq"` - RabbitMQHost default

### App ID Defaults

Multiple files use `?? "bannou"` as default app ID - this is intentional for the omnipotent routing pattern but should still come from configuration.

---

## Notes

These should ideally use the `[ServiceConfiguration]` attribute pattern or be consolidated into proper configuration classes that bind from environment variables automatically via IOptions<T>.

**Priority fixes:**
1. Remove all hardcoded credentials (guest/guest, default secrets, default salts)
2. Ensure all connection strings come from proper configuration
3. Fix generated code templates to not include hardcoded defaults
