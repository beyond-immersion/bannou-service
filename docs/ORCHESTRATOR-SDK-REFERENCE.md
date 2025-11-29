# Orchestrator SDK Reference

**Purpose**: Local documentation of SDK APIs used by the orchestrator service.
**Last Updated**: 2025-11-29

## Docker.DotNet SDK (v3.125.15)

**Package**: `Docker.DotNet`
**GitHub**: https://github.com/dotnet/Docker.DotNet
**NuGet**: https://www.nuget.org/packages/Docker.DotNet

### Client Initialization

```csharp
// Default local socket
var client = new DockerClientConfiguration().CreateClient();

// Explicit socket (Linux)
var client = new DockerClientConfiguration(
    new Uri("unix:///var/run/docker.sock")).CreateClient();

// Named pipe (Windows)
var client = new DockerClientConfiguration(
    new Uri("npipe://./pipe/docker_engine")).CreateClient();

// Remote with TLS
var credentials = new CertificateCredentials(
    new X509Certificate2("cert.pfx", "password"));
var config = new DockerClientConfiguration(
    new Uri("https://docker-host:2376"), credentials);
var client = config.CreateClient();
```

### Container Operations (`client.Containers`)

| Method | Signature |
|--------|-----------|
| **ListContainersAsync** | `Task<IList<ContainerListResponse>> ListContainersAsync(ContainersListParameters, CancellationToken)` |
| **InspectContainerAsync** | `Task<ContainerInspectResponse> InspectContainerAsync(string id, CancellationToken)` |
| **CreateContainerAsync** | `Task<CreateContainerResponse> CreateContainerAsync(CreateContainerParameters, CancellationToken)` |
| **StartContainerAsync** | `Task<bool> StartContainerAsync(string id, ContainerStartParameters, CancellationToken)` |
| **StopContainerAsync** | `Task<bool> StopContainerAsync(string id, ContainerStopParameters, CancellationToken)` |
| **RestartContainerAsync** | `Task RestartContainerAsync(string id, ContainerRestartParameters, CancellationToken)` |
| **RemoveContainerAsync** | `Task RemoveContainerAsync(string id, ContainerRemoveParameters, CancellationToken)` |
| **KillContainerAsync** | `Task KillContainerAsync(string id, ContainerKillParameters, CancellationToken)` |
| **GetContainerLogsAsync** | `Task<Stream> GetContainerLogsAsync(string id, ContainerLogsParameters, CancellationToken)` |
| **GetContainerStatsAsync** | `Task<Stream> GetContainerStatsAsync(string id, ContainerStatsParameters, CancellationToken)` |

**Key Parameters**:
- `ContainersListParameters`: `All`, `Limit`, `Filters`
- `ContainerStopParameters`: `WaitBeforeKillSeconds`
- `ContainerRestartParameters`: `WaitBeforeKillSeconds`
- `ContainerRemoveParameters`: `Force`, `RemoveVolumes`

### Swarm Operations (`client.Swarm`)

| Method | Signature |
|--------|-----------|
| **CreateServiceAsync** | `Task<ServiceCreateResponse> CreateServiceAsync(ServiceCreateParameters, CancellationToken)` |
| **UpdateServiceAsync** | `Task<ServiceUpdateResponse> UpdateServiceAsync(string id, ServiceUpdateParameters, CancellationToken)` |
| **ListServicesAsync** | `Task<IEnumerable<SwarmService>> ListServicesAsync(ServicesListParameters, CancellationToken)` |
| **InspectServiceAsync** | `Task<SwarmService> InspectServiceAsync(string id, CancellationToken)` |
| **RemoveServiceAsync** | `Task RemoveServiceAsync(string id, CancellationToken)` |
| **GetServiceLogsAsync** | `Task<Stream> GetServiceLogsAsync(string id, ServiceLogsParameters, CancellationToken)` |
| **InspectSwarmAsync** | `Task<SwarmInspectResponse> InspectSwarmAsync(CancellationToken)` |
| **ListNodesAsync** | `Task<IEnumerable<NodeListResponse>> ListNodesAsync(CancellationToken)` |

**Service Update for Rolling Restart**:
```csharp
// Force update to trigger rolling restart (--force flag equivalent)
var service = await client.Swarm.InspectServiceAsync(serviceId);
var updateParams = new ServiceUpdateParameters
{
    Service = service.Spec,
    Version = service.Version.Index
};
updateParams.Service.TaskTemplate.ForceUpdate = (updateParams.Service.TaskTemplate.ForceUpdate ?? 0) + 1;
await client.Swarm.UpdateServiceAsync(serviceId, updateParams);
```

### System Operations (`client.System`)

| Method | Signature |
|--------|-----------|
| **GetSystemInfoAsync** | `Task<SystemInfoResponse> GetSystemInfoAsync(CancellationToken)` |
| **GetVersionAsync** | `Task<VersionResponse> GetVersionAsync(CancellationToken)` |
| **PingAsync** | `Task PingAsync(CancellationToken)` |
| **MonitorEventsAsync** | `Task<Stream> MonitorEventsAsync(ContainerEventsParameters, CancellationToken)` |

### Swarm Mode Detection

```csharp
var systemInfo = await client.System.GetSystemInfoAsync();

// Check if swarm mode is active
bool isSwarmActive = systemInfo.Swarm?.LocalNodeState == "active";

// Check if this node is a manager
bool isManager = systemInfo.Swarm?.ControlAvailable == true;

// LocalNodeState values: "inactive", "pending", "active", "error", "locked"
```

### Network Operations (`client.Networks`)

| Method | Signature |
|--------|-----------|
| **ListNetworksAsync** | `Task<IList<NetworkResponse>> ListNetworksAsync(NetworksListParameters, CancellationToken)` |
| **InspectNetworkAsync** | `Task<NetworkResponse> InspectNetworkAsync(string id, CancellationToken)` |
| **CreateNetworkAsync** | `Task<NetworksCreateResponse> CreateNetworkAsync(NetworksCreateParameters, CancellationToken)` |
| **DeleteNetworkAsync** | `Task DeleteNetworkAsync(string id, CancellationToken)` |

### Image Operations (`client.Images`)

| Method | Signature |
|--------|-----------|
| **ListImagesAsync** | `Task<IList<ImagesListResponse>> ListImagesAsync(ImagesListParameters, CancellationToken)` |
| **InspectImageAsync** | `Task<ImageInspectResponse> InspectImageAsync(string name, CancellationToken)` |
| **CreateImageAsync** | `Task CreateImageAsync(ImagesCreateParameters, AuthConfig, IProgress<JSONMessage>, CancellationToken)` |
| **DeleteImageAsync** | `Task<IList<IDictionary<string, string>>> DeleteImageAsync(string name, ImageDeleteParameters, CancellationToken)` |

### Volume Operations (`client.Volumes`)

| Method | Signature |
|--------|-----------|
| **ListAsync** | `Task<VolumesListResponse> ListAsync(CancellationToken)` |
| **CreateAsync** | `Task<VolumeResponse> CreateAsync(VolumesCreateParameters, CancellationToken)` |
| **InspectAsync** | `Task<VolumeResponse> InspectAsync(string name, CancellationToken)` |
| **RemoveAsync** | `Task RemoveAsync(string name, bool? force, CancellationToken)` |
| **PruneAsync** | `Task<VolumesPruneResponse> PruneAsync(VolumesPruneParameters, CancellationToken)` |

---

## KubernetesClient SDK

**Package**: `KubernetesClient`
**GitHub**: https://github.com/kubernetes-client/csharp
**NuGet**: https://www.nuget.org/packages/KubernetesClient

### Client Initialization

```csharp
// From default kubeconfig (~/.kube/config or KUBECONFIG env var)
var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
var client = new Kubernetes(config);

// From specific file
var config = KubernetesClientConfiguration.BuildConfigFromConfigFile("/path/to/kubeconfig");
var client = new Kubernetes(config);

// From in-cluster (service account)
var config = KubernetesClientConfiguration.InClusterConfig();
var client = new Kubernetes(config);
```

### Core API Operations (`client.CoreV1`)

| Method | Signature |
|--------|-----------|
| **ListNamespacedPodAsync** | `Task<V1PodList> ListNamespacedPodAsync(string namespaceParameter, ...)` |
| **ReadNamespacedPodAsync** | `Task<V1Pod> ReadNamespacedPodAsync(string name, string namespaceParameter)` |
| **CreateNamespacedPodAsync** | `Task<V1Pod> CreateNamespacedPodAsync(V1Pod body, string namespaceParameter)` |
| **DeleteNamespacedPodAsync** | `Task<V1Pod> DeleteNamespacedPodAsync(string name, string namespaceParameter, V1DeleteOptions body)` |
| **ListNamespaceAsync** | `Task<V1NamespaceList> ListNamespaceAsync()` |

### Apps API Operations (`client.AppsV1`)

| Method | Signature |
|--------|-----------|
| **ListNamespacedDeploymentAsync** | `Task<V1DeploymentList> ListNamespacedDeploymentAsync(string namespaceParameter)` |
| **ReadNamespacedDeploymentAsync** | `Task<V1Deployment> ReadNamespacedDeploymentAsync(string name, string namespaceParameter)` |
| **CreateNamespacedDeploymentAsync** | `Task<V1Deployment> CreateNamespacedDeploymentAsync(V1Deployment body, string namespaceParameter)` |
| **PatchNamespacedDeploymentAsync** | `Task<V1Deployment> PatchNamespacedDeploymentAsync(V1Patch body, string name, string namespaceParameter)` |
| **ReplaceNamespacedDeploymentAsync** | `Task<V1Deployment> ReplaceNamespacedDeploymentAsync(V1Deployment body, string name, string namespaceParameter)` |
| **DeleteNamespacedDeploymentAsync** | `Task<V1Status> DeleteNamespacedDeploymentAsync(string name, string namespaceParameter)` |

### Scaling Deployments

```csharp
// Scale by patching replicas
var patch = new V1Patch(
    new { spec = new { replicas = 3 } },
    V1Patch.PatchType.MergePatch);
await client.AppsV1.PatchNamespacedDeploymentScaleAsync(patch, deploymentName, namespaceName);
```

### Rolling Restart (Trigger Rollout)

```csharp
// Force rollout by patching annotations
var patch = new V1Patch(
    new {
        spec = new {
            template = new {
                metadata = new {
                    annotations = new Dictionary<string, string> {
                        { "kubectl.kubernetes.io/restartedAt", DateTime.UtcNow.ToString("o") }
                    }
                }
            }
        }
    },
    V1Patch.PatchType.StrategicMergePatch);
await client.AppsV1.PatchNamespacedDeploymentAsync(patch, deploymentName, namespaceName);
```

### Version Detection

```csharp
var version = await client.Version.GetCodeAsync();
// Returns VersionInfo with GitVersion, Major, Minor, etc.
```

---

## Portainer REST API

**Documentation**: https://docs.portainer.io/api/docs
**Examples**: https://docs.portainer.io/api/examples

### Authentication

```http
# Get JWT token (valid 8 hours)
POST /api/auth
Content-Type: application/json
{"Username": "admin", "Password": "password"}

# Response: {"jwt": "eyJhbG..."}

# Use API Key (preferred for automation)
X-API-Key: ptr_your_api_key_here
```

### Key Architectural Note

**Portainer does NOT expose specific container management endpoints.** Instead, it acts as a **reverse-proxy to the Docker API**:

```
Portainer API: /api/endpoints/{endpointId}/docker/{docker_api_path}
Maps to:       Docker API: /{docker_api_path}
```

### Container Operations (via Docker proxy)

| Portainer Endpoint | Docker Equivalent |
|-------------------|-------------------|
| `GET /api/endpoints/1/docker/containers/json?all=true` | `GET /containers/json?all=true` |
| `POST /api/endpoints/1/docker/containers/create?name=mycontainer` | `POST /containers/create` |
| `POST /api/endpoints/1/docker/containers/{id}/start` | `POST /containers/{id}/start` |
| `POST /api/endpoints/1/docker/containers/{id}/stop` | `POST /containers/{id}/stop` |
| `POST /api/endpoints/1/docker/containers/{id}/restart` | `POST /containers/{id}/restart` |
| `DELETE /api/endpoints/1/docker/containers/{id}?force=true` | `DELETE /containers/{id}` |

### Portainer-Specific Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/status` | GET | Portainer status and version |
| `/api/endpoints` | GET | List all endpoints (Docker hosts) |
| `/api/endpoints/{id}` | GET | Get endpoint details |
| `/api/stacks` | GET | List all stacks |
| `/api/stacks` | POST | Deploy a stack (Compose/Swarm) |
| `/api/stacks/{id}` | DELETE | Remove a stack |
| `/api/stacks/{id}/start` | POST | Start a stack |
| `/api/stacks/{id}/stop` | POST | Stop a stack |

### Stack Environment Updates

```http
PUT /api/stacks/{stackId}
{
  "env": [
    {"name": "AUTH_JWT_SECRET", "value": "new_secret"},
    {"name": "DATABASE_PASSWORD", "value": "new_password"}
  ],
  "pullImage": true,
  "prune": true
}
```

### C# HTTP Client Pattern

```csharp
public class PortainerClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly int _endpointId;

    public async Task<HttpResponseMessage> ListContainersAsync()
    {
        return await _httpClient.GetAsync(
            $"{_baseUrl}/api/endpoints/{_endpointId}/docker/containers/json?all=true");
    }

    public async Task<HttpResponseMessage> RestartContainerAsync(string containerId)
    {
        return await _httpClient.PostAsync(
            $"{_baseUrl}/api/endpoints/{_endpointId}/docker/containers/{containerId}/restart",
            null);
    }
}
```

---

## Implementation Notes

### Backend Detection Priority

1. **Kubernetes**: Try `KubernetesClientConfiguration.BuildConfigFromConfigFile()` or `InClusterConfig()`
2. **Portainer**: Check if `PORTAINER_URL` environment variable is set and API responds
3. **Docker Swarm**: Use `client.System.GetSystemInfoAsync()` and check `Swarm.LocalNodeState == "active"`
4. **Docker Compose**: Default fallback - Docker available but not in Swarm mode

### Error Handling

```csharp
// Docker.DotNet specific exceptions
catch (DockerApiException ex) { /* API error with StatusCode */ }
catch (DockerContainerNotFoundException ex) { /* Container not found */ }
catch (DockerImageNotFoundException ex) { /* Image not found */ }

// Kubernetes specific exceptions
catch (HttpOperationException ex) { /* API error with Response */ }
```

### Connection Resilience

**StackExchange.Redis** (already in project):
```csharp
var options = ConfigurationOptions.Parse(connectionString);
options.AbortOnConnectFail = false;  // Keep retrying
options.ConnectRetry = 10;
options.ReconnectRetryPolicy = new ExponentialRetry(1000, 30000);
```

**RabbitMQ.Client** (already in project):
```csharp
var factory = new ConnectionFactory
{
    AutomaticRecoveryEnabled = true,
    NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
    RequestedHeartbeat = TimeSpan.FromSeconds(30)
};
```

---

## References

- [Docker.DotNet GitHub](https://github.com/dotnet/Docker.DotNet)
- [Docker Engine API](https://docs.docker.com/engine/api/)
- [Kubernetes C# Client GitHub](https://github.com/kubernetes-client/csharp)
- [Kubernetes API Reference](https://kubernetes.io/docs/reference/kubernetes-api/)
- [Portainer API Documentation](https://docs.portainer.io/api/docs)
- [Docker Swarm Rolling Updates](https://docs.docker.com/engine/swarm/swarm-tutorial/rolling-update/)
