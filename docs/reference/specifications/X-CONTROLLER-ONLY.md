# x-controller-only / x-manual-implementation

> **Version**: 1.0
> **Status**: Implemented
> **Last Updated**: 2026-03-16
> **Schema Scope**: `*-api.yaml`
> **Generated Output**: Modified controller class — abstract base class with abstract methods (x-controller-only) or partial class with comment placeholders (x-manual-implementation)

---

## Summary

Marks individual API endpoints as requiring manual controller implementation, excluding them from the generated service interface and implementation stub. Two variants exist: x-controller-only generates an abstract method signature for override in a concrete controller subclass, while x-manual-implementation generates nothing but a comment placeholder, allowing fully custom routes and parameters in a partial class. These flags are not interchangeable as they produce different class structures.

---

## Schema Syntax

### x-controller-only

Applied to individual operations in `*-api.yaml`. The endpoint is excluded from `I{Service}Service` and the service implementation stub. NSwag generates an abstract method with the route and parameter signature.

```yaml
paths:
  /connect/websocket:
    get:
      operationId: ConnectWebSocket
      summary: Establish WebSocket connection
      x-permissions: []
      x-controller-only: true
      parameters:
        - name: Connection
          in: header
          required: true
          schema:
            type: string
            enum: [Upgrade]
        - name: Upgrade
          in: header
          required: true
          schema:
            type: string
            enum: [websocket]
        - name: Authorization
          in: header
          required: true
          schema:
            type: string
      responses:
        '101':
          description: WebSocket connection established
```

### x-manual-implementation

Applied to individual operations in `*-api.yaml`. The endpoint is excluded from `I{Service}Service` and the service implementation stub. NSwag generates only a comment placeholder — no method signature at all.

```yaml
paths:
  /documentation/{slug}:
    get:
      operationId: documentBySlug
      summary: Get rendered documentation page
      x-manual-implementation: true
      parameters:
        - name: slug
          in: path
          required: true
          schema:
            type: string
      responses:
        '200':
          description: HTML documentation page (returns ContentResult)
        '404':
          description: Document not found
```

---

## Field Reference

### x-controller-only

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `x-controller-only` | boolean | Yes | Must be `true`. Marks the operation for abstract method generation in a base controller class. |

### x-manual-implementation

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `x-manual-implementation` | boolean | Yes | Must be `true`. Marks the operation for comment-only placeholder generation in a partial controller class. |

---

## Generated Output

### Behavioral Differences

| Aspect | x-controller-only | x-manual-implementation |
|--------|-------------------|------------------------|
| `I{Service}Service` interface | Excluded | Excluded |
| `{Service}Service.cs` stub | Excluded | Excluded |
| Controller class keyword | `abstract class {Service}ControllerBase` | `partial class {Service}Controller` |
| Controller method output | Abstract method with route, parameters, and XML docs | Comment placeholder only |
| Manual class pattern | Concrete class inheriting `{Service}ControllerBase` with `override` methods | Additional methods in manual partial class file |
| NSwag parameter types | Must match generated parameter types exactly | Fully custom — developer defines routes and parameters |

### x-controller-only: Generated Abstract Base Class

When any operation in a service has `x-controller-only: true`, the generated controller becomes an abstract base class. Non-flagged endpoints get full implementations delegating to the service interface. Flagged endpoints become abstract methods.

```csharp
// Generated: plugins/lib-connect/Generated/ConnectController.cs

[BannouController(typeof(IConnectService))]
public abstract class ConnectControllerBase : ControllerBase, IConnectController
{
    private IConnectService _implementation;
    private ITelemetryProvider _telemetryProvider;

    public ConnectControllerBase(IConnectService implementation, ITelemetryProvider telemetryProvider)
    {
        _implementation = implementation;
        _telemetryProvider = telemetryProvider;
    }

    // Normal endpoints get full generated implementations...
    [HttpPost, Route("internal/proxy")]
    public async Task<ActionResult<InternalProxyResponse>> ProxyInternalRequest(
        [FromBody] [BindRequired] InternalProxyRequest body,
        CancellationToken cancellationToken = default)
    {
        // Full generated implementation with telemetry, error handling, delegation
    }

    // x-controller-only endpoints become abstract methods:
    [HttpGet, Route("websocket")]
    public abstract Task<IActionResult> ConnectWebSocket(
        Connection connection, Upgrade upgrade, string authorization,
        CancellationToken cancellationToken = default);
}
```

The developer provides a concrete controller class:

```csharp
// Manual: plugins/lib-connect/ConnectController.cs

public class ConnectController : ConnectControllerBase
{
    public ConnectController(IConnectService implementation, ITelemetryProvider telemetryProvider)
        : base(implementation, telemetryProvider) { }

    public override async Task<IActionResult> ConnectWebSocket(
        Connection connection, Upgrade upgrade, string authorization,
        CancellationToken cancellationToken = default)
    {
        // Manual WebSocket upgrade implementation
    }
}
```

### x-manual-implementation: Generated Partial Class with Comment

When any operation has `x-manual-implementation: true`, the generated controller remains a partial class. Flagged endpoints produce only a comment indicating where the manual implementation belongs.

```csharp
// Generated: plugins/lib-documentation/Generated/DocumentationController.cs

[BannouController(typeof(IDocumentationService))]
public partial class DocumentationController : ControllerBase, IDocumentationController
{
    private IDocumentationService _implementation;
    private ITelemetryProvider _telemetryProvider;

    // Normal endpoints get full generated implementations...

    // x-manual-implementation endpoints:
    // See x-manual-implementation: true in the OpenAPI schema.
    // See x-manual-implementation: true in the OpenAPI schema.
}
```

The developer adds the manual methods in a separate partial class file:

```csharp
// Manual: plugins/lib-documentation/DocumentationController.cs

public partial class DocumentationController
{
    [HttpGet("/documentation/{slug}")]
    public async Task<IActionResult> DocumentBySlug(string slug)
    {
        // Fully custom — returns HTML ContentResult, not JSON
    }

    [HttpGet("/documentation/raw/{slug}")]
    public async Task<IActionResult> RawDocumentBySlug(string slug)
    {
        // Fully custom — returns raw markdown
    }
}
```

---

## Runtime Behavior

Both flags produce endpoints that function identically at runtime once the manual implementation is provided. The difference is purely in the code generation pattern:

- **x-controller-only** endpoints retain NSwag-generated route attributes and parameter binding on the abstract method. The override must match the generated signature exactly.
- **x-manual-implementation** endpoints have no generated method at all. The developer has full control over route patterns, HTTP methods, parameter binding, and return types.

Both flag types exclude the endpoint from the generated service interface (`I{Service}Service`), so the manual controller implementation calls service methods directly or performs custom logic without the standard delegation pattern.

---

## Structural Tests

No structural tests currently enforce these flags. The NSwag generation pipeline handles them during code generation.

---

## Examples

### Example 1: WebSocket Upgrade (x-controller-only)

The Connect service uses `x-controller-only` for WebSocket upgrade endpoints because they require HTTP protocol switching that cannot be expressed through the standard service interface pattern.

**Schema** (`connect-api.yaml`):
```yaml
/connect/websocket:
  get:
    operationId: ConnectWebSocket
    summary: Establish WebSocket connection
    x-permissions: []
    x-controller-only: true
    parameters:
      - name: Connection
        in: header
        required: true
        schema:
          type: string
          enum: [Upgrade]
      - name: Upgrade
        in: header
        required: true
        schema:
          type: string
          enum: [websocket]
      - name: Authorization
        in: header
        required: true
        schema:
          type: string
    responses:
      '101':
        description: WebSocket connection established
```

**Why x-controller-only**: The WebSocket upgrade requires direct access to `HttpContext` for protocol switching. The generated abstract method provides the correct route and parameter signature while allowing the override to perform the upgrade.

### Example 2: Browser-Facing HTML Endpoints (x-manual-implementation)

The Documentation service uses `x-manual-implementation` for browser-facing GET endpoints that return HTML instead of JSON.

**Schema** (`documentation-api.yaml`):
```yaml
/documentation/{slug}:
  get:
    operationId: documentBySlug
    summary: Get rendered documentation page
    x-manual-implementation: true
    parameters:
      - name: slug
        in: path
        required: true
        schema:
          type: string
    responses:
      '200':
        description: HTML documentation page (returns ContentResult)
      '404':
        description: Document not found
```

**Why x-manual-implementation**: These endpoints use GET with path parameters and return `ContentResult` (HTML), which is incompatible with the standard POST-only JSON pattern. The developer needs full control over the route template, HTTP method, and return type.

### Example 3: OAuth Redirect (x-controller-only)

The Auth service uses `x-controller-only` for the OAuth callback endpoint that returns a 302 redirect.

**Schema** (`auth-api.yaml`):
```yaml
/auth/oauth/callback:
  get:
    operationId: oauthCallback
    summary: OAuth provider callback
    x-controller-only: true
    parameters:
      - name: code
        in: query
        schema:
          type: string
      - name: state
        in: query
        schema:
          type: string
    responses:
      '302':
        description: Redirect to application
```

**Why x-controller-only**: OAuth callbacks must be GET endpoints returning redirects, which cannot be expressed through the standard service interface tuple return pattern.

---

## Edge Cases & Restrictions

### Forbidden Combinations

| Restriction | Reason |
|---|---|
| Both `x-controller-only` and `x-manual-implementation` on the same operation | They produce incompatible class structures (abstract base vs partial) |
| Both flags used on different operations within the same service | Mixing would require the controller to be both abstract and partial in conflicting ways. In practice, a service uses one pattern or the other. |
| `x-controller-only: false` or `x-manual-implementation: false` | These are not toggle flags. Omit the attribute entirely for standard generation. |

### Scoping Rules

- Both flags apply to individual operations, not to entire path items or services.
- When any operation in a service uses `x-controller-only`, the entire generated controller class becomes an abstract base class (`{Service}ControllerBase`), affecting the class name and inheritance pattern for all endpoints in that service.
- When `x-manual-implementation` is used, the controller remains a partial class (`{Service}Controller`) with comment placeholders for the flagged endpoints.

### Choosing Between the Two

| Scenario | Use |
|---|---|
| Endpoint needs non-standard behavior but standard route/parameter binding | `x-controller-only` |
| Endpoint needs fully custom routes, HTTP methods, or return types | `x-manual-implementation` |
| WebSocket upgrade, OAuth redirect, webhook receiver | `x-controller-only` |
| Browser-facing HTML pages, file downloads, custom content types | `x-manual-implementation` |

### Current Usage

| Service | Flag | Endpoints | Reason |
|---------|------|-----------|--------|
| Connect | `x-controller-only` | `ConnectWebSocket`, `ConnectWebSocketPost`, `BroadcastWebSocket` | WebSocket protocol upgrade |
| Auth | `x-controller-only` | `oauthCallback` | OAuth 302 redirect |
| Broadcast | `x-controller-only` | Webhook endpoints | External platform webhook receivers |
| Documentation | `x-manual-implementation` | `documentBySlug`, `rawDocumentBySlug` | Browser-facing GET endpoints returning HTML/markdown |
