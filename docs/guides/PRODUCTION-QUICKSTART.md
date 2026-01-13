# Production Quickstart Guide

This guide walks you through deploying Bannou on a fresh server (e.g., DigitalOcean droplet) in monoservice mode with all services enabled.

## Prerequisites

- Fresh Ubuntu 22.04+ server
- SSH access with root privileges
- At least 4GB RAM, 2 vCPUs recommended
- Ports 80, 443 open to the internet

## Step 1: Install Dependencies

Clone the repository and run the install script:

```bash
git clone https://github.com/BeyondImmersion/bannou-service.git
cd bannou-service
chmod +x scripts/install-dev-tools.sh
sudo ./scripts/install-dev-tools.sh --docker-only
```

This installs Docker, Docker Compose, and basic utilities.

For local development with full .NET SDK:
```bash
sudo ./scripts/install-dev-tools.sh --with-dotnet
```

## Step 2: Get Your Server IP/Domain

```bash
curl -s ifconfig.me
```

If you have a domain pointing to this IP, use that instead.

## Step 3: Generate Secure Secrets

Generate random secrets for production:

```bash
# 64-character secrets (JWT, internal token)
openssl rand -hex 32

# 32-character salts
openssl rand -hex 16
```

## Step 4: Create Production .env

Copy the minimal example and configure:

```bash
cp .env.example.minimal .env
nano .env
```

### Required Configuration

| Variable | Description | How to Generate |
|----------|-------------|-----------------|
| `BANNOU_SERVICE_DOMAIN` | Your server domain or IP | Your domain/IP |
| `BANNOU_JWT_SECRET` | JWT signing key (64 chars) | `openssl rand -hex 32` |
| `CONNECT_SERVER_SALT` | GUID generation salt (32 chars) | `openssl rand -hex 16` |
| `CONNECT_INTERNAL_SERVICE_TOKEN` | Internal auth (64 chars) | `openssl rand -hex 32` |

### Infrastructure Defaults

The following infrastructure is auto-configured with standard Docker service names:

| Service | Default Connection |
|---------|-------------------|
| Redis | `bannou-redis:6379` |
| MySQL | `server=bannou-mysql;database=bannou;user=guest;password=guest` |
| RabbitMQ | `rabbitmq:5672` |

Override only if using non-standard hostnames or credentials.

### Automatic URL Defaults

When `BANNOU_SERVICE_DOMAIN` is set, the following URLs are automatically configured:

| URL | Default Value |
|-----|---------------|
| WebSocket (AUTH_CONNECT_URL, CONNECT_URL) | `wss://{domain}/connect` |
| Discord OAuth callback | `https://{domain}/auth/oauth/discord/callback` |
| Google OAuth callback | `https://{domain}/auth/oauth/google/callback` |
| Twitch OAuth callback | `https://{domain}/auth/oauth/twitch/callback` |

You only need to set explicit URLs if your setup differs from these defaults.

## Step 5: Build Docker Image

```bash
make build-compose
```

This builds the Bannou container with all service plugins.

## Step 6: Start Production Stack

Start with OpenResty edge proxy on ports 80/443:

```bash
make up-external
```

This runs the full production stack:
- Bannou monoservice (all services)
- bannou-redis (state/cache)
- bannou-mysql (state persistence)
- RabbitMQ (messaging)
- MinIO (asset storage)
- OpenResty (edge proxy on 80/443)

Wait ~45 seconds for all services to initialize.

## Step 7: Verify Deployment

Check container health:
```bash
docker ps --format "table {{.Names}}\t{{.Status}}"
```

All containers should show `(healthy)`.

Test health endpoints:
```bash
# Via OpenResty (port 80)
curl http://<your-domain>/health

# Direct to Bannou (port 8080)
curl http://localhost:8080/health
```

## Step 8: Register Admin Account

With `ACCOUNT_ADMIN_EMAILS` configured in .env, register to get automatic admin role:

```bash
curl -X POST http://<your-domain>/auth/register \
    -H "Content-Type: application/json" \
    -d '{
        "username": "admin",
        "email": "admin@example.com",
        "password": "YourSecurePassword123"
    }'
```

Response includes:
- `accountId` - Your account UUID
- `accessToken` - JWT for API calls
- `refreshToken` - For token refresh
- `connectUrl` - WebSocket endpoint

## Step 9: Test Login

```bash
curl -X POST http://<your-domain>/auth/login \
    -H "Content-Type: application/json" \
    -d '{
        "email": "admin@example.com",
        "password": "YourSecurePassword123"
    }'
```

## Useful Commands

```bash
# View logs
make logs-external

# Stop stack
make down-external

# View all container status
docker ps --format "table {{.Names}}\t{{.Status}}"

# Restart just bannou (keeps databases running)
docker compose --project-name bannou-external restart bannou

# Rebuild and restart after code changes
make build-compose && make up-external
```

## Service Endpoints

| Endpoint | Description |
|----------|-------------|
| `POST /auth/register` | Create new account |
| `POST /auth/login` | Login and get JWT |
| `POST /auth/logout` | Logout and invalidate tokens |
| `WS /connect` | WebSocket gateway |
| `GET /health` | Health check |

## OAuth Provider Setup

To enable OAuth login, set the provider credentials in .env:

### Discord
```bash
AUTH_DISCORD_CLIENT_ID=your-client-id
AUTH_DISCORD_CLIENT_SECRET=your-client-secret
# Redirect URI auto-configured from BANNOU_SERVICE_DOMAIN
```

### Google
```bash
AUTH_GOOGLE_CLIENT_ID=your-client-id
AUTH_GOOGLE_CLIENT_SECRET=your-client-secret
```

### Twitch
```bash
AUTH_TWITCH_CLIENT_ID=your-client-id
AUTH_TWITCH_CLIENT_SECRET=your-client-secret
```

### Steam
```bash
AUTH_STEAM_API_KEY=your-steam-api-key
AUTH_STEAM_APP_ID=your-app-id
```

## Step 10: Configure SSL/TLS (HTTPS)

Bannou uses OpenResty for SSL termination. The configuration uses templates with environment variable substitution.

### Generate or Obtain SSL Certificates

**Option A: Let's Encrypt (production)**
```bash
# Install certbot
apt install certbot

# Generate certificate
certbot certonly --standalone -d your-domain.com

# Certificates are saved to:
# /etc/letsencrypt/live/your-domain.com/fullchain.pem
# /etc/letsencrypt/live/your-domain.com/privkey.pem
```

**Option B: Self-signed (development/testing)**
```bash
mkdir -p provisioning/certificates
openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
    -keyout provisioning/certificates/key.pem \
    -out provisioning/certificates/cert.pem \
    -subj "/CN=your-domain.com"
```

### Configure SSL Environment Variables

Add to your `.env`:

```bash
# SSL Certificate Paths
SSL_CERT_PATH=/etc/nginx/ssl/cert.pem
SSL_KEY_PATH=/etc/nginx/ssl/key.pem

# For MinIO storage proxy (both patterns work)
ASSET_STORAGE_PUBLIC_ENDPOINT=your-domain.com:9000
# Or use subdomain: storage.your-domain.com
```

### Generate OpenResty Configs

```bash
cd provisioning/openresty
./generate-configs.sh
```

This creates SSL-enabled server blocks from templates using your environment variables.

### Mount Certificates in Docker

Update your Docker Compose to mount the certificates:

```yaml
services:
  openresty:
    volumes:
      - ./certificates:/etc/nginx/ssl:ro
      # Or for Let's Encrypt:
      - /etc/letsencrypt/live/your-domain.com:/etc/nginx/ssl:ro
```

### Verify SSL Configuration

```bash
# Test HTTPS endpoint
curl -k https://your-domain.com/health

# Test WebSocket (wss://)
# Your game client should now connect via wss://your-domain.com/connect

# Test MinIO storage proxy (port 9000)
curl -k https://your-domain.com:9000/health
```

## Security Checklist

- [ ] Generate unique secrets for each environment
- [ ] Configure firewall (only allow 22, 80, 443, 9000)
- [ ] Set up SSL certificates for HTTPS (Step 10)
- [ ] Configure backup for MySQL and Redis data volumes
- [ ] Review admin email configuration
- [ ] Regenerate OpenResty configs after domain changes

## Troubleshooting

### Bannou exits immediately
Check logs: `docker logs bannou-external-bannou-1`

Common issues:
- Missing `BANNOU_JWT_SECRET` - Add to .env
- Wrong `MESH_ENDPOINT_HOST` - Use `bannou` (the Docker service name)

### Health returns 500
Check JWT configuration in logs.

### Registration returns 500
Verify mesh routing - `MESH_ENDPOINT_HOST` should match the Docker service name.

### OpenResty keeps restarting
Check that `bannou` container is healthy first - OpenResty needs bannou as upstream.

### OAuth redirects fail
Verify your domain is correctly set in `BANNOU_SERVICE_DOMAIN` and matches what's registered with the OAuth provider.

## Next Steps

- Configure SSL certificates for HTTPS
- Set up OAuth providers (Discord, Google, Steam)
- Configure monitoring and alerting
- Review [DEPLOYMENT.md](DEPLOYMENT.md) for advanced deployment patterns
