# x-from-authorization

> **Version**: 1.0
> **Status**: Implemented
> **Last Updated**: 2026-03-16
> **Schema Scope**: `*-api.yaml`
> **Generated Output**: Parameter stripped from generated service client (`{Service}Client.cs`)

---

## Summary

Marks a parameter as extracted from the HTTP Authorization header rather than the request body, used exclusively by auth-related endpoints where the JWT token serves as both credential and request parameter. The code generator strips these parameters from service-to-service clients since inter-service calls use a different authentication mechanism. Use when an endpoint needs to consume the bearer token as input data, not just as an authentication credential.

---

## Schema Syntax

The `x-from-authorization` attribute is defined on individual parameters within operation definitions in `*-api.yaml`:

```yaml
paths:
  /auth/refresh-token:
    post:
      operationId: refreshToken
      summary: Refresh an expired access token
      parameters:
        - name: Authorization
          in: header
          required: true
          schema:
            type: string
          description: Current JWT access token for refresh
          x-from-authorization: bearer
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RefreshTokenRequest'
      responses:
        '200':
          description: New token pair issued
```

---

## Field Reference

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `x-from-authorization` | string | Yes | The authorization scheme. Currently only `bearer` is supported, indicating the parameter is extracted from the `Authorization: Bearer <token>` header. |

---

## Generated Output

### Client Generation

The `generate-client.sh` script detects `x-from-authorization` on parameters and strips them from the generated service client in `bannou-service/Generated/Clients/{Service}Client.cs`. This means the parameter does not appear in the client method signature.

**Why**: Service-to-service calls via lib-mesh do not pass through the HTTP Authorization header the same way browser or WebSocket clients do. Inter-service authentication uses a different mechanism (service tokens, trusted internal network). The bearer token parameter is meaningful only for direct client-to-server calls where the JWT is the request input itself (e.g., "refresh this token", "validate this token").

### Controller Generation

The NSwag controller generator treats the parameter normally — it appears in the generated controller method signature as a header parameter. The controller extracts the value from the HTTP `Authorization` header at request time.

```csharp
// Generated controller method signature (parameter IS present):
public async Task<ActionResult<RefreshTokenResponse>> RefreshToken(
    [FromHeader] string authorization,
    [FromBody] [BindRequired] RefreshTokenRequest body,
    CancellationToken cancellationToken = default)
{
    // ...
}
```

```csharp
// Generated client method signature (parameter is STRIPPED):
public async Task<(StatusCodes, RefreshTokenResponse?)> RefreshTokenAsync(
    RefreshTokenRequest body,
    CancellationToken cancellationToken = default)
{
    // ...
}
```

---

## Runtime Behavior

At runtime, the ASP.NET model binder extracts the `Authorization` header value and passes it to the controller method. The service implementation receives the full header value (e.g., `"Bearer eyJ..."`) and parses the token as needed.

For service-to-service calls via the generated client, the parameter is absent. The lib-mesh infrastructure handles authentication transparently through its own mechanism, so the calling service does not need to provide a bearer token.

---

## Structural Tests

No structural tests currently enforce this attribute. The client generator script handles stripping during code generation.

---

## Examples

### Example 1: Token Refresh

The Auth service's refresh-token endpoint needs the current (potentially expired) access token to issue a new token pair.

**Schema** (`auth-api.yaml`):
```yaml
/auth/refresh-token:
  post:
    operationId: refreshToken
    summary: Refresh an expired access token
    parameters:
      - name: Authorization
        in: header
        required: true
        schema:
          type: string
        description: Current JWT access token for refresh
        x-from-authorization: bearer
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/RefreshTokenRequest'
```

**Client behavior**: Other services calling `_authClient.RefreshTokenAsync(request)` do not pass a token — the parameter is stripped. This endpoint is primarily used by direct clients (browsers, game clients) via HTTP or WebSocket.

### Example 2: Token Validation

The Auth service's validate-token endpoint takes the JWT token itself as the parameter to validate.

**Schema** (`auth-api.yaml`):
```yaml
/auth/validate-token:
  post:
    operationId: validateToken
    summary: Validate a JWT access token
    parameters:
      - name: Authorization
        in: header
        required: true
        schema:
          type: string
        description: JWT access token for validation
        x-from-authorization: bearer
    responses:
      '200':
        description: Token is valid
```

**Why x-from-authorization**: The token is not just a credential — it is the data being operated on. The endpoint's purpose is to inspect and validate the token itself.

---

## Edge Cases & Restrictions

### Forbidden Combinations

| Restriction | Reason |
|---|---|
| `x-from-authorization` on a request body property | The attribute applies only to `in: header` parameters, not to body fields |
| `x-from-authorization` with a value other than `bearer` | Only bearer token extraction is currently supported |
| `x-from-authorization` on non-auth service endpoints | The attribute exists for the specific case where the JWT token is both credential and input data, which is exclusive to auth-related flows |

### Scoping Rules

- The attribute applies to individual parameters on individual operations. It does not propagate to other operations or parameters.
- The stripping effect applies only to the generated service client. The generated controller, interface, and models are unaffected (except the client).
- The parameter must be `in: header` with `name: Authorization` to be meaningful. Applying the attribute to query or path parameters has no defined behavior.

### Current Usage

All current uses are in `auth-api.yaml` on endpoints where the JWT bearer token is the subject of the operation:

- `refreshToken` — token being refreshed
- `validateToken` — token being validated
- `logout` — token identifying the session to terminate
- `changePassword` — token identifying the authenticated user
- `revokeSession` — token identifying the session to revoke
- `getSessions` — token identifying the account whose sessions to list
- `enableMfa` / `disableMfa` — token identifying the account for MFA changes

### Interaction with Other Extension Attributes

- **x-permissions**: Endpoints with `x-from-authorization` typically have `x-permissions` specifying `role: user` or `role: authenticated`, since the bearer token implies an authenticated caller.
- **x-controller-only**: The OAuth callback endpoint (`oauthCallback`) uses `x-controller-only` but does not use `x-from-authorization` because it receives an authorization code, not a bearer token.
