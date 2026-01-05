# x-permissions OpenAPI Extension Specification

## Overview

The `x-permissions` extension defines role-based access control for API endpoints. When services start up, they use this information to register their API permissions with the Permission service, enabling dynamic capability discovery for WebSocket clients.

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
          states: {}  # Any authenticated user
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
- **Key**: Service ID that manages the state (e.g., `game-session`, `character`)
- **Value**: Required state value (e.g., `in_game`, `selected`)

**Important**: Authentication status is NOT a state - it's determined by the role.
`role: user` = authenticated, `role: anonymous` = not authenticated.

States are for **contextual access control** based on user's current activity:

```yaml
x-permissions:
  - role: user
    states:
      game-session: in_game      # Must be in an active game
      character: selected        # Must have selected a character
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

States are for contextual navigation, **not authentication status**. Authentication is handled by roles.

### Game Session States (set by game-session service)
- `in_lobby`: User is in a game lobby
- `in_game`: User is in an active game session
- `spectating`: User is watching a game as spectator

### Character States (set by character service)
- `selected`: User has selected a character
- `in_creation`: User is creating a character

### Realm States (set by realm service)
- `in_realm`: User is in a specific realm instance

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
/account/{id}:
  get:
    x-permissions:
      - role: user
        states: {}
      - role: admin
        states: {}
```

### Admin-Only Endpoint

```yaml
/orchestrator/deploy:
  post:
    x-permissions:
      - role: admin
        states: {}
```

### Game Session Endpoint (Requires Contextual State)

```yaml
/game-session/action:
  post:
    x-permissions:
      - role: user
        states:
          game-session: in_game  # Must be in active game
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
2. **Generated Code**: Creates `{Service}PermissionRegistration.cs` in `Generated/` with permission matrix
3. **Service Startup**: `RegisterServicePermissionsAsync()` publishes `ServiceRegistrationEvent`
4. **Permission Service**: Receives event, updates Redis permission matrices
5. **Session Recompilation**: All active sessions get updated capabilities
6. **Connect Service**: Receives capability updates, notifies WebSocket clients

## State Key Storage Format

The Permission service stores permission matrices in Redis using the following key format:

```
permissions:{serviceId}:{stateKey}:{role}
```

### State Key Construction

The `stateKey` is constructed differently depending on the permission configuration:

| x-permissions `states` value | State Key | Example Redis Key |
|------------------------------|-----------|-------------------|
| `{}` (empty object) | `"default"` | `permissions:account:default:user` |
| `{game-session: in_game}` (same service) | `"in_game"` | `permissions:game-session:in_game:user` |
| `{game-session: in_game}` (different service) | `"game-session:in_game"` | `permissions:character:game-session:in_game:user` |

### Key Matching Rules

When a session's state changes (e.g., user joins a game), the Permission service recompiles capabilities by:

1. **Default permissions**: Always include endpoints stored at `permissions:{serviceId}:default:{role}`
2. **State-based permissions**: For each session state `{stateServiceId: stateValue}`:
   - If `stateServiceId == serviceId`: look up `permissions:{serviceId}:{stateValue}:{role}`
   - If `stateServiceId != serviceId`: look up `permissions:{serviceId}:{stateServiceId}:{stateValue}:{role}`

### Example Flow

1. User joins a game session, setting `game-session=in_game` state
2. Permission service checks all registered services:
   - For `game-session` service: looks up `permissions:game-session:in_game:user`
   - For `character` service: looks up `permissions:character:game-session:in_game:user`
   - For `chat` service: looks up `permissions:chat:game-session:in_game:user`
3. All matching endpoints are added to the session's capability manifest
