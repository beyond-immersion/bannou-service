# x-permissions OpenAPI Extension Specification

## Overview

The `x-permissions` extension defines role-based access control for API endpoints. When services start up, they use this information to register their API permissions with the Permissions service, enabling dynamic capability discovery for WebSocket clients.

## Schema Format

### Endpoint-Level Permission Declaration

```yaml
paths:
  /example/endpoint:
    post:
      summary: Example endpoint
      operationId: exampleOperation
      x-permissions:
        - role: user
          states:
            auth: authenticated  # Requires auth state = authenticated
        - role: admin
          states: {}  # Admin can access regardless of state
```

### Permission Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `role` | string | Yes | Role required for access (e.g., `user`, `admin`, `npc`, `anonymous`) |
| `states` | object | No | Map of service states required. Empty object means no state requirements. |

### State Requirements

The `states` property is a map where:
- **Key**: Service ID (e.g., `auth`, `game-session`)
- **Value**: Required state for that service (e.g., `authenticated`, `in_game`)

```yaml
x-permissions:
  - role: user
    states:
      auth: authenticated        # Must be authenticated
      game-session: in_game      # Must be in an active game
```

## Standard Roles

| Role | Description |
|------|-------------|
| `anonymous` | Unauthenticated users (pre-login) |
| `user` | Standard authenticated users |
| `admin` | Administrative users with elevated privileges |
| `npc` | AI agent NPCs (server-side entities) |
| `service` | Service-to-service calls (internal APIs) |

## Standard States

### Auth Service States
- `anonymous`: Not yet authenticated
- `authenticated`: Successfully logged in

### Game Session States
- `none`: Not in any game session
- `in_lobby`: In game lobby
- `in_game`: In active game session
- `spectating`: Watching game as spectator

## Examples

### Public Endpoint (No Auth Required)

```yaml
/auth/login:
  post:
    x-permissions:
      - role: anonymous
        states: {}
      - role: user
        states: {}  # Already logged in users can also call login
```

### Authenticated User Endpoint

```yaml
/accounts/{id}:
  get:
    x-permissions:
      - role: user
        states:
          auth: authenticated
      - role: admin
        states:
          auth: authenticated
```

### Admin-Only Endpoint

```yaml
/orchestrator/deploy:
  post:
    x-permissions:
      - role: admin
        states:
          auth: authenticated
```

### Game Session Endpoint

```yaml
/game-session/action:
  post:
    x-permissions:
      - role: user
        states:
          auth: authenticated
          game-session: in_game
```

### NPC Endpoint (Server-Side Only)

```yaml
/npc/behavior/update:
  post:
    x-permissions:
      - role: npc
        states: {}
      - role: service
        states: {}
```

## Generation Output

When services start, they call `RegisterServicePermissionsAsync()` which publishes a `ServiceRegistrationEvent` containing:

```json
{
  "eventId": "uuid",
  "timestamp": "2025-01-19T12:00:00Z",
  "serviceId": "auth",
  "version": "3.0.0",
  "appId": "bannou",
  "endpoints": [
    {
      "path": "/auth/login",
      "method": "POST",
      "permissions": [
        { "role": "anonymous", "requiredStates": {} },
        { "role": "user", "requiredStates": {} }
      ]
    }
  ]
}
```

## Integration Flow

1. **Build Time**: `generate-permissions.sh` extracts x-permissions from schema
2. **Generated Code**: Creates `{Service}PermissionRegistration.Generated.cs` with permission matrix
3. **Service Startup**: `RegisterServicePermissionsAsync()` publishes `ServiceRegistrationEvent`
4. **Permissions Service**: Receives event, updates Redis permission matrices
5. **Session Recompilation**: All active sessions get updated capabilities
6. **Connect Service**: Receives capability updates, notifies WebSocket clients
