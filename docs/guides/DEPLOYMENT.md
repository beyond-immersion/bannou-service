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
BANNOU_JWT_SECRET=your-secure-jwt-secret-key
BANNOU_JWT_ISSUER=bannou-auth-external
BANNOU_JWT_AUDIENCE=bannou-api-external

# WebSocket URL (returned to clients after login)
AUTH_CONNECT_URL=ws://your-domain.com/connect

# OAuth Mock Providers (for testing without real OAuth)
AUTH_MOCK_PROVIDERS=true

# Admin auto-assignment (emails matching this domain get admin role)
ACCOUNT_ADMIN_EMAIL_DOMAIN=@admin.test.local
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
ACCOUNT_SERVICE_ENABLED=true

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

## OpenResty Edge Proxy Configuration

OpenResty serves as the edge proxy for all external traffic, providing:
- SSL/TLS termination (HTTPS, WSS)
- Dynamic service routing via Lua + Redis
- Rate limiting for asset operations
- MinIO storage proxy for pre-signed URLs

### Configuration Architecture

OpenResty uses a hybrid configuration approach:
- **Lua `os.getenv()`** for runtime configuration (Redis hosts, etc.)
- **envsubst templates** for compile-time directives (`server_name`)

```
provisioning/openresty/
├── templates/                    # Checked into git
│   ├── ssl-termination.conf.template
│   └── storage-proxy.conf.template
├── generated/                    # Gitignored - created by generate-configs.sh
├── overrides/                    # Gitignored - production customizations
├── lua/                          # Lua scripts for dynamic routing
├── nginx.conf                    # Main config (includes generated/*.conf)
├── generate-configs.sh           # Template processor
└── docker-entrypoint.sh          # Generates configs then starts nginx
```

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `BANNOU_SERVICE_DOMAIN` | `localhost` | Domain for SSL server blocks |
| `ASSET_STORAGE_ENDPOINT` | `minio:9000` | Internal MinIO endpoint |
| `ASSET_STORAGE_PUBLIC_ENDPOINT` | `{domain}:9000` | Public MinIO endpoint for pre-signed URLs |
| `SSL_CERT_PATH` | `/etc/nginx/ssl/cert.pem` | SSL certificate path |
| `SSL_KEY_PATH` | `/etc/nginx/ssl/key.pem` | SSL private key path |

### Generating Configs

```bash
# Set environment variables
export BANNOU_SERVICE_DOMAIN=demo.example.com
export ASSET_STORAGE_ENDPOINT=minio:9000

# Generate configs from templates
cd provisioning/openresty
./generate-configs.sh
```

Output shows substituted values and generated files:
```
=== OpenResty Config Generation ===
BANNOU_SERVICE_DOMAIN: demo.example.com
ASSET_STORAGE_ENDPOINT: minio:9000
...
Generating: ssl-termination.conf
Generating: storage-proxy.conf
```

### MinIO Storage Proxy

Pre-signed URLs for asset uploads/downloads are rewritten to use public endpoints. The storage proxy supports two access patterns:

**Port-based** (default):
```
https://demo.example.com:9000/bannou-assets/key?signature
```

**Subdomain-based** (optional):
```
https://storage.demo.example.com/bannou-assets/key?signature
```

Configure which pattern to use:
```bash
# Port-based (default when BANNOU_SERVICE_DOMAIN is set)
ASSET_STORAGE_PUBLIC_ENDPOINT=demo.example.com:9000

# Subdomain-based
ASSET_STORAGE_PUBLIC_ENDPOINT=storage.demo.example.com
```

### Production Overrides

For environment-specific configurations that shouldn't be templated, create files in `overrides/`:

```nginx
# overrides/rate-limiting.conf
limit_req_zone $binary_remote_addr zone=api:10m rate=100r/s;

# overrides/custom-headers.conf
add_header X-Frame-Options "SAMEORIGIN" always;
```

These files are gitignored and included after generated configs.

### Docker Integration

The `docker-entrypoint.sh` script handles config generation at container startup:

```yaml
# docker-compose.yml
services:
  openresty:
    image: openresty/openresty:alpine
    entrypoint: ["/etc/openresty/docker-entrypoint.sh"]
    environment:
      - BANNOU_SERVICE_DOMAIN=${BANNOU_SERVICE_DOMAIN}
      - ASSET_STORAGE_ENDPOINT=${ASSET_STORAGE_ENDPOINT:-minio:9000}
      - SSL_CERT_PATH=/etc/nginx/ssl/cert.pem
      - SSL_KEY_PATH=/etc/nginx/ssl/key.pem
    volumes:
      - ./openresty:/etc/openresty:ro
      - ./certificates:/etc/nginx/ssl:ro
```

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

# JWT settings (BANNOU_ for main app validation, AUTH_ for token generation)
BANNOU_JWT_SECRET=your-secret-key
BANNOU_JWT_ISSUER=bannou-auth
BANNOU_JWT_AUDIENCE=bannou-api
AUTH_JWT_EXPIRATION_MINUTES=60

# WebSocket
AUTH_CONNECT_URL=ws://your-domain/connect
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
