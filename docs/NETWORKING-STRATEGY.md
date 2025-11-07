# WSL2-Compatible Docker Networking Strategy

## Problem Statement

Previous docker-compose networking setup failed on WSL2/Windows due to:
- Bridge network mDNS resolution issues (container names not resolving)
- Consul hostname resolution attempts also failed
- Circular dependencies with `depends_on` causing restart loops
- Complexity from multiple stacked compose files

## Solution: Simplified Default Bridge Network

### Core Principles

1. **Use Docker's Default Bridge Network**
   - No custom bridge networks (no `driver: bridge` declarations)
   - WSL2 handles default bridge perfectly
   - Service name resolution works out of the box

2. **Service-to-Service Communication**
   - Containers use service names: `bannou`, `rabbitmq`, `placement`, `account-db`
   - Dapr sidecars share network stack: `network_mode: service:bannou`
   - All bindings to `0.0.0.0` (not `127.0.0.1`)

3. **No depends_on Blocks**
   - Wait scripts handle service readiness
   - No circular dependency issues
   - Services start independently and wait for dependencies

## Network Architecture by Testing Layer

### Layer 1: Infrastructure Tests (Minimal)
**Purpose:** Validate core infrastructure without complex dependencies

**Services:**
- `bannou` (TESTING service only)
- `bannou-dapr` (sidecar, network_mode: service:bannou)
- `placement` (Dapr coordination)
- `bannou-infra-tester` (curl container, network_mode: service:bannou)

**Network:**
- All on default bridge (implicit)
- Tester shares network with bannou (can curl localhost:80)

**Communication:**
- Tester → Bannou: `curl http://127.0.0.1:80/health` (shared network stack)
- Dapr → Placement: `placement:50006` (service name)

### Layer 2: HTTP Integration Tests (Service-to-Service)
**Purpose:** Test service-to-service communication within datacenter

**Services:**
- `bannou` (all services enabled)
- `bannou-dapr` (sidecar, network_mode: service:bannou)
- `placement`
- `rabbitmq`
- `account-db`, `bannou-redis`, `auth-redis` (from services.yml)
- `bannou-http-tester` (http-tester service)
- `bannou-http-tester-dapr` (sidecar, network_mode: service:bannou-http-tester)

**Network:**
- All on default bridge (implicit)
- Each service reachable by name

**Communication:**
- HTTP Tester Dapr → Bannou Dapr: `http://bannou:80/accounts/...` (via service name)
- Bannou → RabbitMQ: `rabbitmq:5672` (service name)
- Bannou → MySQL: `account-db:3306` (service name)
- Bannou → Redis: `bannou-redis:6379` (service name)

### Layer 3: Edge/WebSocket Tests (External Client)
**Purpose:** Simulate real client connecting from outside datacenter

**Services:**
- `bannou` (all services enabled)
- `bannou-dapr` (sidecar, network_mode: service:bannou)
- `placement`
- `rabbitmq`
- `account-db`, `bannou-redis`, `auth-redis`
- `routing-redis` (for NGINX routing state)
- `openresty` (NGINX edge proxy - TWO NETWORKS)
- `bannou-edge-tester` (external network ONLY)

**Networks:**
- **internal** (default): bannou, placement, rabbitmq, databases, routing-redis, openresty
- **external**: openresty, bannou-edge-tester

**Communication:**
- Edge Tester → OpenResty: `http://openresty:80/...` (external network)
- OpenResty → Bannou: `http://bannou:80/...` (internal network, proxy_pass)
- OpenResty → Redis: `routing-redis:6379` (internal network, routing state)

## Service Configuration Requirements

### Bannou Service
```yaml
environment:
  # Dapr endpoints - use 127.0.0.1 since dapr sidecar shares network stack
  - DAPR_HTTP_ENDPOINT=http://127.0.0.1:3500
  - DAPR_GRPC_ENDPOINT=http://127.0.0.1:50001

healthcheck:
  # Use 127.0.0.1 for internal health check
  test: ["CMD", "curl", "--fail", "http://127.0.0.1:80/health"]
```

### Dapr Sidecar
```yaml
bannou-dapr:
  network_mode: service:bannou  # Share network stack with bannou
  command:
    - --app-id=bannou
    - --app-port=80              # Bannou listens on 0.0.0.0:80
    - --dapr-http-port=3500      # Dapr listens on 0.0.0.0:3500
    - --placement-host-address=placement:50006  # Use service name
```

### Database/Infrastructure Services
```yaml
account-db:
  # No network config needed - uses default bridge
  # MySQL binds to 0.0.0.0:3306 by default

rabbitmq:
  # No network config needed - uses default bridge
  # RabbitMQ binds to 0.0.0.0:5672 by default

bannou-redis:
  # No network config needed - uses default bridge
  # Redis binds to 0.0.0.0:6379 by default
```

### OpenResty (Dual Network)
```yaml
openresty:
  networks:
    - default      # Internal - reach bannou, routing-redis
    - external     # External - accept edge-tester connections
  environment:
    - REDIS_HOST=routing-redis  # Use service name on internal network
```

### Edge Tester (External Only)
```yaml
bannou-edge-tester:
  networks:
    - external     # Only on external network
  environment:
    # Connect to OpenResty on external network
    - WEBSOCKET_URL=ws://openresty:80/ws
```

## Wait Scripts (Replacing depends_on)

### Infrastructure Wait Script
```bash
#!/bin/sh
# wait-for-health.sh - used by infra tester

echo "Waiting for bannou health endpoint..."
until curl -f http://127.0.0.1:80/health; do
  echo "Bannou not ready, retrying in 2s..."
  sleep 2
done
echo "Bannou is healthy!"
```

### HTTP Test Wait Script
```bash
#!/bin/sh
# wait-for-services.sh - used by http tester

# Wait for Dapr sidecar
until curl -f http://127.0.0.1:3500/v1.0/healthz; do
  echo "Dapr not ready, retrying..."
  sleep 2
done

# Verify can reach bannou via Dapr service invocation
until curl -f http://127.0.0.1:3500/v1.0/invoke/bannou/method/health; do
  echo "Cannot reach bannou via Dapr, retrying..."
  sleep 2
done

echo "All services ready!"
```

### Edge Test Wait Script
```bash
#!/bin/sh
# wait-for-openresty.sh - used by edge tester

echo "Waiting for OpenResty edge proxy..."
until curl -f http://openresty:80/health; do
  echo "OpenResty not ready, retrying in 2s..."
  sleep 2
done
echo "OpenResty is ready!"
```

## Migration Steps

1. **Remove all custom network declarations**
   - Delete `networks:` sections with `driver: bridge`
   - Keep only named networks for dual-network services (openresty)

2. **Remove all depends_on blocks**
   - Services start independently
   - Wait scripts handle readiness

3. **Update service references**
   - Change `127.0.0.1` → service names for cross-container communication
   - Keep `127.0.0.1` for same-container communication (sidecar pattern)

4. **Update environment variables**
   - Database hosts: `account-db`, `bannou-redis`, `auth-redis`
   - RabbitMQ host: `rabbitmq`
   - Placement: `placement:50006`

5. **Configure dual-network for OpenResty**
   - Internal (default) network for backend services
   - External network for edge testers

6. **Add wait scripts to testers**
   - Infrastructure: wait for bannou health
   - HTTP: wait for Dapr + bannou
   - Edge: wait for OpenResty

## Testing Validation

### Local Testing Commands
```bash
# Infrastructure tests
make test-infrastructure

# HTTP integration tests
make test-http

# Edge/WebSocket tests
make test-edge

# Full test suite
make test-all
```

### Expected Behavior
✅ Infrastructure tests pass with minimal services
✅ HTTP tests validate service-to-service communication
✅ Edge tests simulate real client through NGINX
✅ No mDNS resolution failures on WSL2
✅ No circular dependency restart loops
✅ All tests work identically in CI/CD (GitHub Actions)

## Troubleshooting

**Issue:** Container can't resolve service name
- **Solution:** Ensure both containers on same network (or no network specified = default)

**Issue:** Dapr can't reach bannou
- **Solution:** Verify `network_mode: service:bannou` and `--app-port=80` match bannou's listen port

**Issue:** Edge tester can't reach OpenResty
- **Solution:** Verify OpenResty on `external` network and edge tester ONLY on `external` network

**Issue:** Services fail to start
- **Solution:** Check wait scripts are executable and have proper health check logic

## Benefits of This Approach

✅ **WSL2 Compatible** - No mDNS issues, no bridge network failures
✅ **Simple** - Uses Docker defaults, minimal configuration
✅ **Clear Separation** - Internal vs external networks explicit
✅ **No Circular Dependencies** - Wait scripts handle startup order
✅ **CI/CD Compatible** - Works identically in GitHub Actions
✅ **Maintainable** - Easy to understand and debug
