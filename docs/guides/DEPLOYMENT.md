# Deployment Guide

This guide covers deploying Bannou from local development to production environments.

## Local Development

### Prerequisites

- Docker and Docker Compose
- Make (optional but recommended)
- .NET 9 SDK (for building)

### Quick Start

```bash
# Start the full stack
make up-compose

# Verify services are running
docker compose -f provisioning/docker-compose.yml ps

# View logs
docker compose -f provisioning/docker-compose.yml logs -f bannou
```

### Manual Docker Compose

```bash
# Build
docker compose -f provisioning/docker-compose.yml build

# Start
docker compose -f provisioning/docker-compose.yml up -d

# Stop
docker compose -f provisioning/docker-compose.yml down
```

## Testing Environments

Bannou provides specific configurations for testing:

### Infrastructure Tests

Minimal setup for basic service validation:

```bash
make test-infrastructure
```

This verifies Docker health and basic connectivity.

### HTTP Integration Tests

Service-to-service HTTP communication:

```bash
make test-http
```

Starts a focused set of services and runs HTTP endpoint tests.

### WebSocket Edge Tests

Full protocol validation through the Connect service:

```bash
make test-edge
```

Starts the complete stack and runs WebSocket binary protocol tests.

## External Client Testing

For testing with real game clients (Unity, Unreal) from external networks:

### Prerequisites

1. **Domain**: DNS pointing to your development machine
2. **Port Forwarding**: Forward ports 80 and 443 to your machine
3. **Docker & Docker Compose**

### Configuration

Set environment variables in `.env`:

```bash
# JWT Configuration
BANNOU_JWTSECRET=your-secure-jwt-secret-key
BANNOU_JWTISSUER=bannou-auth-external
BANNOU_JWTAUDIENCE=bannou-api-external

# WebSocket URL (returned to clients after login)
BANNOU_CONNECTURL=ws://your-domain.com/connect

# OAuth Mock Providers (for testing without real OAuth)
BANNOU_MOCKPROVIDERS=true

# Admin auto-assignment (emails matching this domain get admin role)
BANNOU_ADMINEMAILDOMAIN=@admin.test.local
```

### Running External Stack

```bash
# Start with OpenResty on ports 80/443
make up-external

# Register test admin account
make external-register

# Login and get JWT
make external-login

# View logs
make logs-external

# Stop
make down-external
```

### Connecting Game Clients

1. Use the `connectUrl` from login response
2. Include JWT in the Authorization header
3. Follow the [WebSocket Protocol](../WEBSOCKET-PROTOCOL.md)

## Orchestrator Service

The Orchestrator manages deployment topology programmatically:

### Starting Orchestrator

```bash
make up-orchestrator
```

### Deploying with Presets

```bash
# Deploy using a preset
curl -X POST http://localhost:5012/orchestrator/deploy \
  -H "Content-Type: application/json" \
  -d '{"preset": "http-tests"}'

# Check status
curl http://localhost:5012/orchestrator/status
```

### Available Presets

Located in `provisioning/orchestrator/presets/`:

| Preset | Description |
|--------|-------------|
| `bannou.yaml` | Default monolith - all services |
| `http-tests.yaml` | HTTP integration testing |
| `edge-tests.yaml` | WebSocket protocol testing |
| `auth-only.yaml` | Minimal auth service |
| `minimal-services.yaml` | Core services only |
| `local-development.yaml` | Full development stack |

## Production Deployment Patterns

### Single Node (Monolith)

All services on one machine:

```bash
SERVICES_ENABLED=true
```

Simplest deployment, suitable for small scale or development.

### Service Groups (Applications)

Group related services on dedicated nodes:

```bash
# Auth Node
SERVICES_ENABLED=false
AUTH_SERVICE_ENABLED=true
ACCOUNTS_SERVICE_ENABLED=true

# Game Session Node
SERVICES_ENABLED=false
GAME_SESSION_SERVICE_ENABLED=true
CONNECT_SERVICE_ENABLED=true

# NPC Processing Node
SERVICES_ENABLED=false
BEHAVIOR_SERVICE_ENABLED=true
CHARACTER_SERVICE_ENABLED=true
```

### Dynamic Service Mapping

In distributed deployments, the Orchestrator publishes service routing information:

1. Each node sends heartbeats to the Orchestrator
2. Orchestrator publishes `FullServiceMappingsEvent` to RabbitMQ
3. `ServiceAppMappingResolver` updates routing tables atomically
4. Requests route to correct nodes based on current topology

## Networking Considerations

### WSL2 Docker Networking

On Windows with WSL2, use the default bridge network (not host networking):

```yaml
networks:
  default:
    driver: bridge
```

See [Networking Strategy](../operations/NETWORKING_STRATEGY.md) for details.

### Service Discovery

- **Development**: mDNS on Docker bridge networks
- **Production**: DNS or Kubernetes service mesh via lib-mesh

### Port Allocation

| Service | Default Port |
|---------|-------------|
| Bannou HTTP | 5012 |
| Bannou HTTPS | 5013 |
| Redis | 6379 |
| RabbitMQ | 5672 |
| MySQL | 3306 |

## Environment Variables

Key environment variables for deployment:

```bash
# Service selection
SERVICES_ENABLED=true|false
{SERVICE}_SERVICE_ENABLED=true|false

# JWT settings
BANNOU_JWTSECRET=your-secret-key
BANNOU_JWTISSUER=bannou-auth
BANNOU_JWTAUDIENCE=bannou-api
BANNOU_JWTEXPIRATION_MINUTES=60

# WebSocket
BANNOU_CONNECTURL=ws://your-domain/connect
```

See [Configuration Reference](../reference/CONFIGURATION.md) for the complete list.

## Health Monitoring

### Health Endpoints

```bash
# App health
curl http://localhost:5012/health
```

### Redis Heartbeats

The Orchestrator tracks service health via Redis:

- `service:heartbeat:{appId}` - TTL 90s, health status
- `service:routing:{serviceName}` - TTL 5min, current routing

### Log Aggregation

All services log to stdout for container log collection:

```bash
# Docker logs
docker logs -f bannou-container

# Compose logs
docker compose logs -f
```

## Troubleshooting

### Services Not Starting

1. Check environment variables are set correctly
2. Check logs for startup errors

```bash
docker compose logs bannou-service
```

### Service Communication Failures

1. Verify Redis and RabbitMQ are healthy
2. Check service discovery is working
3. Verify network connectivity between containers

```bash
# Test service invocation
curl http://localhost:5012/health
```

### WebSocket Connection Issues

1. Verify Connect service is running
2. Check JWT is valid
3. Verify correct WebSocket URL

```bash
# Test WebSocket health
curl http://localhost:5012/connect/health
```

## Next Steps

- [Testing Guide](TESTING.md) - Test your deployment
- [GitHub Actions](../operations/GITHUB_ACTIONS.md) - CI/CD pipeline
- [Configuration Reference](../reference/CONFIGURATION.md) - All environment variables
