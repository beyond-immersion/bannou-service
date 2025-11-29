# Localhost Port Binding for WSL2 + Docker + Dapr Issues

This document explains how to use localhost port binding to work around known issues with WSL2, Docker, and Dapr placement service communication.

## Problem Background

When running Bannou locally on WSL2 with Docker, the combination of:
- WSL2 networking layers
- Docker private bridge networks (celestial_network)
- Dapr placement service mDNS communication

Can result in service-to-service communication failures, particularly with Dapr placement service discovery and inter-service calls. This manifests as timeouts, connection failures, and inability for services to discover each other.

## Solution: Localhost Port Binding

The localhost port binding approach uses explicit `127.0.0.1:host_port:container_port` mappings while maintaining Docker's standard bridge networking. This provides localhost accessibility for debugging and development while avoiding the complexity and WSL2 limitations of `network_mode: host`.

## Usage

### Local Development

```bash
# Start services with host networking
make up-host

# Stop host networking services
make down-host
```

### Testing with Host Networking

```bash
# Run HTTP integration tests with host networking
make test-http-host

# Run HTTP tests for specific plugin with host networking
make test-http-host PLUGIN=accounts
```

### Traditional Bridge Networking (for comparison)

```bash
# Start services with bridge networking (original approach)
make up-compose

# Run HTTP tests with bridge networking
make test-http
```

## Key Differences

### Bridge Networking (Original)
- **Network**: Custom bridge network `celestial_network` with internal isolation
- **Service Discovery**: Relies on Docker DNS and Dapr placement service
- **Port Access**: Services exposed via port mappings (e.g., 8080:80)
- **Security**: Container isolation via network segmentation
- **WSL2 Compatibility**: Known issues with mDNS and placement service

### Localhost Port Binding (Workaround)
- **Network**: Standard Docker bridge networking with explicit localhost port bindings
- **Service Discovery**: Container-to-container communication via Docker DNS
- **Port Access**: Services accessible on localhost via `127.0.0.1:port` mappings
- **Dapr Components**: Uses standard local components with container hostnames
- **Console App Support**: HTTP tester works as console app with separate Dapr sidecar
- **Security**: Localhost-only access (127.0.0.1), more secure than 0.0.0.0 binding
- **WSL2 Compatibility**: Excellent, avoids network_mode: host limitations

## Port Allocation

When using localhost port binding, services are accessible on these ports:

| Service | Port | Description |
|---------|------|-------------|
| bannou | 8080, 8443 | Main application HTTP/HTTPS |
| bannou-dapr | 3500, 50001 | Dapr HTTP/gRPC sidecar |
| http-tester-dapr | 3501, 50002 | HTTP tester Dapr HTTP/gRPC sidecar |
| placement | 50006 | Dapr placement service |
| bannou-redis | 6379 | Primary Redis (accounts, general state) |
| auth-redis | 6380 | Auth sessions + permissions Redis |
| routing-redis | 6381 | OpenResty routing cache Redis |
| account-db | 3306 | MySQL database |
| rabbitmq | 5672, 15672 | RabbitMQ + management interface |
| openresty | 80, 443 | **Ingress proxy (actual web ports)** |

**Note**: The http-tester is a console application with no HTTP server, so it doesn't bind to web ports directly.

## CI/CD Compatibility

The host networking configuration is **local development only**. CI/CD pipelines continue to use bridge networking for:

- **Security**: Container isolation in shared runner environments
- **Compatibility**: GitHub Actions doesn't have WSL2 + Docker issues
- **Consistency**: Production deployments use bridge/overlay networks

GitHub Actions workflows automatically use the correct networking configuration:
- **CI**: Uses bridge networking (`celestial_network`)
- **Local**: Can optionally use host networking

## Dapr Component Architecture

Host networking uses dedicated Dapr components with localhost hostnames:

```
provisioning/dapr/components/
├── local/                            # Bridge networking components
│   ├── pubsub-rabbitmq.yaml         # Uses rabbitmq:5672
│   ├── redis-statestore.yaml        # Uses bannou-redis:6379
│   └── permissions-statestore.yaml   # Uses auth-redis:6379
├── host/                             # Host networking components
│   ├── pubsub-rabbitmq.yaml         # Uses localhost:5672
│   ├── redis-statestore.yaml        # Uses localhost:6379
│   └── permissions-statestore.yaml   # Uses localhost:6380
└── ci/                               # CI components (bridge networking)
```

**Key Component Differences:**
- **pubsub-rabbitmq**: `amqp://localhost:5672` vs `amqp://rabbitmq:5672`
- **redis-statestore**: `localhost:6379` vs `bannou-redis:6379`
- **permissions-statestore**: `localhost:6380` vs `auth-redis:6379`

## File Structure

```
provisioning/
├── docker-compose.yml                 # Base services (bridge network)
├── docker-compose.local.yml           # Local development overrides
├── docker-compose.host.yml            # Host networking overrides
├── docker-compose.host.http.yml       # Host networking for HTTP testing
├── docker-compose.ci.http.yml         # CI HTTP testing configuration
├── openresty/
│   ├── nginx.conf                     # Bridge networking config
│   └── nginx.host.conf                # Host networking config (localhost:8080)
└── dapr/components/host/              # Host-specific Dapr components
```

## Troubleshooting

### Host Networking Issues

1. **Port Conflicts**: If you get "port already in use" errors, check for other services using standard ports:
   ```bash
   netstat -tulpn | grep :8080    # Check for port conflicts
   docker ps                      # Check for running containers
   ```

2. **Permission Issues**: Host networking may require elevated privileges on some systems:
   ```bash
   sudo make up-host              # If permission denied
   ```

3. **Service Discovery**: If services can't find each other, verify they're using localhost endpoints:
   - Check DAPR_HTTP_ENDPOINT=http://localhost:3500
   - Verify placement service uses localhost:50006

### Bridge Networking Issues (Original Problem)

If you want to troubleshoot the original bridge networking issues:

1. **Dapr Logs**: Check placement service logs for mDNS errors
2. **Container Communication**: Test inter-container connectivity
3. **DNS Resolution**: Verify Docker DNS resolution works

## When to Use Each Approach

### Use Host Networking When:
- Running on WSL2 with Docker Desktop
- Experiencing Dapr placement service issues
- Service-to-service communication failures
- Local development and testing

### Use Bridge Networking When:
- Production deployments
- CI/CD pipelines
- Multiple Bannou instances on same host
- Security isolation requirements

## Future Improvements

The host networking approach is a **workaround** for WSL2 + Docker + Dapr issues. Future improvements include:

1. **Staging Environment**: Dedicated staging environment for comprehensive testing
2. **Alternative Container Runtime**: Testing with different container runtimes
3. **Dapr Configuration**: Exploring Dapr configuration options for WSL2
4. **Kubernetes**: Moving to Kubernetes for service orchestration

This should provide a robust local development experience while maintaining full CI/CD compatibility.
