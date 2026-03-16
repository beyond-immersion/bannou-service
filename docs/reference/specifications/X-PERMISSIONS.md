# x-permissions

> **Version**: 1.0
> **Status**: Implemented
> **Last Updated**: 2026-03-16
> **Schema Scope**: `*-api.yaml`
> **Generated Output**: `{Service}PermissionRegistration.cs` -- permission matrix registration for the Permission service
> **Related Specifications**: [x-lifecycle](X-LIFECYCLE.md)
> **Tenet References**: T13 (FOUNDATION)

---

## Summary

Declares role and state requirements for WebSocket client access on API endpoints. The Permission service compiles per-session capability manifests from registered permission matrices. Every API endpoint must declare x-permissions; the value controls whether the endpoint appears in WebSocket capability manifests and which roles can access it. Use on every operation in a service API schema to define its access level.

---

## Schema Syntax

### Basic Role Permission

```yaml
paths:
  /account/get:
    post:
      x-permissions:
        - role: user
      summary: Get account details
```

### Pre-Auth Public Access

```yaml
paths:
  /auth/login:
    post:
      x-permissions:
        - role: anonymous
      summary: Login (accessible before authentication)
```

### Service-to-Service Only

```yaml
paths:
  /achievement/progress/update:
    post:
      x-permissions: []
      summary: Internal progress update (not exposed via WebSocket)
```

### With State Requirements

```yaml
paths:
  /game-session/start:
    post:
      x-permissions:
        - role: user
          states:
            game-session: in_lobby
      summary: Start game (requires user role AND in_lobby state)
```

---

## Field Reference

### Permission Entry Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `role` | string (enum) | Yes | -- | Access role requirement. One of: `anonymous`, `user`, `developer`, `admin` |
| `states` | object | No | -- | Additional state requirements as key-value pairs (e.g., `game-session: in_lobby`) |

### Permission Values

| Value | Meaning | WebSocket Access | Use Case |
|-------|---------|------------------|----------|
| `[{role: admin}]` | Admin-only | Admin WebSocket sessions only | Destructive operations, system management |
| `[{role: developer}]` | Developer and above | Developer and admin sessions | Debug endpoints, developer tools |
| `[{role: user}]` | Authenticated users | Any authenticated session | Most gameplay and account endpoints |
| `[{role: anonymous}]` | Pre-auth public | All connected clients | Login, registration, public queries (rare) |
| `[]` (empty array) | Service-to-service only | **No WebSocket access** | Internal orchestration, cleanup callbacks |
| *(omitted entirely)* | Not in WebSocket API | **No WebSocket access** | Endpoints not yet integrated with permissions |

### Role Hierarchy

Roles form an inclusive hierarchy where higher roles inherit all lower permissions:

```
anonymous -> user -> developer -> admin
```

An endpoint with `role: user` is accessible to `user`, `developer`, and `admin` sessions. An endpoint with `role: admin` is accessible only to `admin` sessions.

---

## Generated Output

### File: `plugins/lib-{service}/Generated/{Service}PermissionRegistration.cs`

The permission generator reads all `x-permissions` entries from the API schema and produces a static registration class. Only endpoints with at least one role entry are included in the matrix.

```csharp
/// <summary>
/// Auto-generated permission registration for the Account service.
/// </summary>
public static class AccountPermissionRegistration
{
    /// <summary>
    /// Registers all Account service permissions with the permission matrix.
    /// </summary>
    public static void Register(IPermissionMatrix matrix)
    {
        matrix.RegisterEndpoint("account", "/account/get", new[] { "user" });
        matrix.RegisterEndpoint("account", "/account/update", new[] { "user" });
        matrix.RegisterEndpoint("account", "/account/delete", new[] { "admin" });
    }
}
```

Endpoints with `x-permissions: []` are excluded entirely -- they receive no session GUID, do not appear in the client's capability manifest, and are unreachable via WebSocket. They are accessible only via lib-mesh (service-to-service calls).

### Integration Flow

1. **Build Time**: `generate-permissions.sh` extracts `x-permissions` from API schema
2. **Generated Code**: Creates `{Service}PermissionRegistration.cs` in `Generated/` with permission matrix
3. **Service Startup**: PluginLoader calls `RegisterServicePermissionsAsync()` with the resolved `IPermissionRegistry`
4. **Permission Service**: Receives registration via DI, updates Redis permission matrices
5. **Session Recompilation**: All active sessions get updated capabilities
6. **Connect Service**: Receives capability updates, notifies WebSocket clients via capability manifest push

---

## Runtime Behavior

### Capability Manifest Compilation

1. Each plugin registers its permission matrix at startup via the generated `{Service}PermissionRegistration.Register()` method
2. When a client authenticates (or connects as anonymous), the Permission service evaluates the session's role against all registered matrices
3. Endpoints where the session's role meets or exceeds the required role are included in the capability manifest
4. State requirements (if any) are evaluated dynamically -- the manifest updates when session state changes

### Standard States

States are for contextual navigation, **not authentication status**. Authentication is handled by roles.

| State Service | State Value | Meaning |
|---------------|-------------|---------|
| `game-session` | `in_lobby` | User is in a game lobby |
| `game-session` | `in_game` | User is in an active game session |
| `game-session` | `spectating` | User is watching a game as spectator |

Games can define additional state services and values. The Permission service treats state keys as opaque strings.

### WebSocket Routing

- Only endpoints present in the session's capability manifest receive client-salted GUIDs
- The Connect service routes incoming messages by GUID -- if the GUID was never issued, the message is rejected
- Endpoints with `x-permissions: []` never receive GUIDs and are invisible to all WebSocket clients

### Edge Cases

- **Missing x-permissions**: An endpoint without x-permissions is treated the same as `[]` -- not exposed via WebSocket. However, structural tests flag this as an error since every endpoint should explicitly declare its access level.
- **Multiple roles**: Only one role entry per permission array is standard. The role hierarchy handles inheritance automatically.
- **State changes**: When a session's state changes (e.g., joining a lobby), the capability manifest is recompiled and updated GUIDs are pushed to the client.

---

## Structural Tests

| Test Name | Validates |
|-----------|-----------|
| `Endpoints_MustDeclarePermissions` | Every operation in every `*-api.yaml` has an `x-permissions` declaration |
| `PermissionRegistration_MatchesSchema` | Generated `{Service}PermissionRegistration.cs` entries match schema `x-permissions` declarations |

---

## Examples

### Example 1: Account Service (Mixed Access Levels)

**Schema** (`account-api.yaml`):
```yaml
paths:
  /account/get:
    post:
      x-permissions:
        - role: user
      summary: Get account details
      operationId: Account_Get
  /account/create:
    post:
      x-permissions:
        - role: anonymous
      summary: Create new account (pre-auth)
      operationId: Account_Create
  /account/admin/list:
    post:
      x-permissions:
        - role: admin
      summary: List all accounts (admin only)
      operationId: Account_AdminList
  /account/cleanup-by-resource:
    post:
      x-permissions: []
      summary: Internal cleanup callback
      operationId: Account_CleanupByResource
```

**Generated** (`AccountPermissionRegistration.cs`):
```csharp
public static class AccountPermissionRegistration
{
    public static void Register(IPermissionMatrix matrix)
    {
        matrix.RegisterEndpoint("account", "/account/get", new[] { "user" });
        matrix.RegisterEndpoint("account", "/account/create", new[] { "anonymous" });
        matrix.RegisterEndpoint("account", "/account/admin/list", new[] { "admin" });
        // /account/cleanup-by-resource excluded (x-permissions: [])
    }
}
```

### Example 2: Game Session with State Requirements

**Schema** (`game-session-api.yaml`):
```yaml
paths:
  /game-session/create:
    post:
      x-permissions:
        - role: user
      summary: Create a new game session
      operationId: GameSession_Create
  /game-session/start:
    post:
      x-permissions:
        - role: user
          states:
            game-session: in_lobby
      summary: Start the game (must be in lobby)
      operationId: GameSession_Start
```

The `/game-session/start` endpoint requires both `role: user` AND the session to be in `in_lobby` state. The capability manifest includes this endpoint only when both conditions are met.

---

## Edge Cases & Restrictions

### Forbidden Combinations

| Restriction | Reason |
|-------------|--------|
| Omitting `x-permissions` entirely | Every endpoint must explicitly declare access level; structural tests enforce this |
| Using `x-permissions` in non-API schemas | Only valid in `*-api.yaml` -- events, configuration, and client-events schemas do not have endpoints |
| Multiple role entries in the array for cumulative access | Use the role hierarchy instead; `role: user` already grants access to user, developer, and admin |

### Scoping Rules

- `x-permissions` is scoped to individual operations (POST/GET methods under a path), not to paths or schemas
- Each operation independently declares its own access level
- There is no inheritance between endpoints -- a service with mostly `user` endpoints still needs `x-permissions` on every single one

### Interaction with Other Extension Attributes

- **x-controller-only / x-manual-implementation**: These endpoints still require `x-permissions` declarations. The permission registration is independent of how the controller is implemented.
- **x-lifecycle**: Lifecycle events do not have `x-permissions` (events are not HTTP endpoints). The API endpoints that trigger lifecycle events (create, update, delete) do require `x-permissions`.

### Choosing the Right Level

For detailed guidance on which permission level to choose for each endpoint type, see [ENDPOINT-PERMISSION-GUIDELINES.md](../ENDPOINT-PERMISSION-GUIDELINES.md). This specification defines the syntax; the guidelines document provides the decision framework.
