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
   - Containers use service names: `bannou`, `rabbitmq`, `account-db`
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
- `bannou-infra-tester` (curl container)

**Network:**
- All on default bridge (implicit)
- Tester can reach bannou by service name

**Communication:**
- Tester → Bannou: `curl http://bannou:80/health` (service name)

### Layer 2: HTTP Integration Tests (Service-to-Service)
**Purpose:** Test service-to-service communication within datacenter

**Services:**
- `bannou` (all services enabled)
- `rabbitmq`
- `account-db`, `bannou-redis`, `auth-redis` (from services.yml)
- `bannou-http-tester` (http-tester service)

**Network:**
- All on default bridge (implicit)
- Each service reachable by name

**Communication:**
- HTTP Tester → Bannou: `http://bannou:80/account/...` (via service name)
- Bannou → RabbitMQ: `rabbitmq:5672` (service name)
- Bannou → MySQL: `account-db:3306` (service name)
- Bannou → Redis: `bannou-redis:6379` (service name)

### Layer 3: Edge/WebSocket Tests (External Client)
**Purpose:** Simulate real client connecting from outside datacenter

**Services:**
- `bannou` (all services enabled)
- `rabbitmq`
- `account-db`, `bannou-redis`, `auth-redis`
- `routing-redis` (for NGINX routing state)
- `openresty` (NGINX edge proxy - TWO NETWORKS)
- `bannou-edge-tester` (external network ONLY)

**Networks:**
- **internal** (default): bannou, rabbitmq, databases, routing-redis, openresty
- **external**: openresty, bannou-edge-tester

**Communication:**
- Edge Tester → OpenResty: `http://openresty:80/...` (external network)
- OpenResty → Bannou: `http://bannou:80/...` (internal network, proxy_pass)
- OpenResty → Redis: `routing-redis:6379` (internal network, routing state)

## Service Configuration Requirements

### Bannou Service
```yaml
healthcheck:
  # Use service name for internal health check
  test: ["CMD", "curl", "--fail", "http://bannou:80/health"]
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
until curl -f http://bannou:80/health; do
  echo "Bannou not ready, retrying in 2s..."
  sleep 2
done
echo "Bannou is healthy!"
```

### HTTP Test Wait Script
```bash
#!/bin/sh
# wait-for-services.sh - used by http tester

# Wait for bannou service
until curl -f http://bannou:80/health; do
  echo "Bannou not ready, retrying..."
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

5. **Configure dual-network for OpenResty**
   - Internal (default) network for backend services
   - External network for edge testers

6. **Add wait scripts to testers**
   - Infrastructure: wait for bannou health
   - HTTP: wait for bannou
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

## Migration History

This networking strategy replaced a complex multi-file setup that failed on WSL2/Windows.

### Deleted Files (Old Complex Stack)
```
provisioning/docker-compose.ci.yml                   → Replaced by docker-compose.test.yml
provisioning/docker-compose.ci.infrastructure.yml   → Replaced by docker-compose.test.infrastructure.yml
provisioning/docker-compose.ci.http.yml             → Replaced by docker-compose.test.http.yml
provisioning/docker-compose.ci.edge.yml             → Replaced by docker-compose.test.edge.yml
provisioning/docker-compose.local.yml               → Removed (no longer needed)
provisioning/docker-compose.ingress.local.yml       → Removed (no longer needed)
provisioning/docker-compose.host.yml                → Removed (old WSL2 workaround)
provisioning/docker-compose.host.http.yml           → Removed (old WSL2 workaround)
provisioning/docker-compose.ek.yml                  → Removed (unused)
provisioning/docker-compose.elk.yml                 → Removed (unused)
docs/HOST-NETWORKING.md                              → Removed (obsolete workaround documentation)
```

### Current File Structure
```
provisioning/docker-compose.yml                     → Base services (bannou, rabbitmq)
provisioning/docker-compose.services.yml            → Service dependencies (MySQL, Redis)
provisioning/docker-compose.ingress.yml             → OpenResty edge proxy (dual network)
provisioning/docker-compose.test.yml                → Shared test configuration
provisioning/docker-compose.test.infrastructure.yml → Infrastructure test overrides
provisioning/docker-compose.test.http.yml           → HTTP integration test overrides
provisioning/docker-compose.test.edge.yml           → Edge/WebSocket test overrides
```

### Key Technical Changes
- **Before**: Custom bridge networks with explicit `driver: bridge`, mDNS resolution failures on WSL2
- **After**: Docker's default bridge network with service name resolution working reliably
- **Before**: `depends_on` blocks causing circular dependency restart loops
- **After**: Wait scripts handle service readiness independently
