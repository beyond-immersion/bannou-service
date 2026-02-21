# Meta Endpoints Guide

Meta endpoints are auto-generated companion endpoints that provide runtime schema introspection for every API operation in Bannou. They enable clients and tools to discover what an endpoint accepts, returns, and does -- without reading external documentation.

## What Meta Endpoints Are

Every service endpoint in Bannou has four companion meta endpoints generated alongside its controller:

| Suffix | Returns |
|--------|---------|
| `/meta/info` | Human-readable description: summary, tags, deprecated status, operationId |
| `/meta/request-schema` | JSON Schema for the request body |
| `/meta/response-schema` | JSON Schema for the response body |
| `/meta/schema` | All three combined (info + request + response) |

For example, the `POST /account/get` endpoint has:
- `GET /account/get/meta/info`
- `GET /account/get/meta/request-schema`
- `GET /account/get/meta/response-schema`
- `GET /account/get/meta/schema`

All meta endpoints are `GET` requests with no body. They return a `MetaResponse`:

```json
{
  "metaType": "endpoint-info",
  "serviceName": "Account",
  "method": "POST",
  "path": "/account/get",
  "data": {
    "summary": "Get account by ID",
    "description": "...",
    "tags": ["Account"],
    "deprecated": false,
    "operationId": "GetAccount"
  },
  "generatedAt": "2026-02-10T12:00:00Z",
  "schemaVersion": "1.0.0.0"
}
```

The `data` field structure varies by `metaType`:
- **endpoint-info**: `{ summary, description, tags, deprecated, operationId }`
- **request-schema**: A full JSON Schema object
- **response-schema**: A full JSON Schema object
- **full-schema**: `{ info, request, response }` combining all three

## How Meta Endpoints Are Generated

The generation pipeline (`scripts/generate-meta-controller.sh`) reads each service's OpenAPI YAML schema and produces `{Service}Controller.Meta.cs` in the plugin's `Generated/` directory. The generated controller contains embedded JSON schema literals -- the schema information is baked into the compiled assembly, not computed at runtime.

This means:
- Meta responses are fast (no reflection or runtime schema generation)
- Schema version matches the deployed assembly version
- `generatedAt` reflects when the code was compiled, not when the request was made

Meta controllers are regenerated whenever `generate-service.sh` or `generate-all-services.sh` runs. Never edit `Controller.Meta.cs` files manually.

## Accessing Meta via WebSocket

WebSocket clients access meta endpoints using the **Meta flag** (0x80) in the binary message header. When a message has this flag set:

1. The `Service GUID` field identifies the target endpoint (using the client's salted GUID from the capability manifest)
2. The `Channel` field encodes which meta type to return:
   - `0` = EndpointInfo
   - `1` = RequestSchema
   - `2` = ResponseSchema
   - `3` = FullSchema
3. The JSON payload is ignored (meta requests take no input)

Connect intercepts messages with the Meta flag in `HandleMetaRequestAsync`, transforms the salted GUID back to the canonical endpoint, constructs the companion meta path (e.g., `/account/get/meta/info`), and routes an internal `GET` request to the service.

**Permission check**: Connect verifies the client's session has the base endpoint in its capability mappings (`HasServiceMapping`) before routing the meta request. If the client cannot access `/account/get`, they cannot access its meta endpoints either.

## Accessing Meta via HTTP

Meta endpoints are also accessible over HTTP for tools, documentation generators, and development workflows. This path requires an active WebSocket connection -- Connect's in-memory session state is the permission source.

### Flow

1. Client sends `GET /account/get/meta/info` with `Authorization: Bearer <jwt>` header
2. OpenResty matches the `/meta/` path pattern and rewrites the request as `POST /connect/get-endpoint-meta` with body `{"path": "/account/get/meta/info"}`
3. Connect's `GetEndpointMetaAsync` handler:
   - Extracts and validates the JWT via the Auth service
   - Looks up the caller's active WebSocket session using the JWT's `sessionKey`
   - Checks the session's capability mappings for the base endpoint (`account:/account/get`)
   - If authorized, proxies an internal `GET` to the service's meta companion endpoint
   - Returns the `MetaResponse` as the HTTP response body
4. If the JWT is invalid or the session has no active WebSocket connection: **401 Unauthorized**
5. If the session lacks permission for the base endpoint: **403 Forbidden**
6. If the path format is invalid or the meta type is unrecognized: **404 Not Found**

### Why It Requires a WebSocket Session

The HTTP meta proxy is not an independent access path. It piggybacks on the same permission model as WebSocket message routing:

- When a WebSocket session connects, the Permission service compiles a capability manifest based on the user's roles, states, and the service permission matrix
- Connect stores this compiled manifest as in-memory `ServiceMappings` on the `ConnectionState`
- Both WebSocket meta requests and HTTP meta requests check `HasServiceMapping` against this same data
- Without an active WebSocket session, there are no compiled permissions to check against

This ensures a single permission source and prevents the HTTP path from becoming a bypass.

## Security Model

Meta endpoints themselves are bare internal infrastructure -- they are `GET` endpoints with no authentication. The security boundary is enforced entirely by the gateway:

- **WebSocket path**: Connect only routes meta requests for GUIDs that exist in the client's session-specific routing table. The salted GUID serves as both the route lookup key and an implicit access check. The explicit `HasServiceMapping` check adds defense in depth.
- **HTTP path**: OpenResty forwards to Connect, which validates the JWT, resolves the session, and checks `HasServiceMapping` before proxying.

In both cases, the permission check answers: "Does this session's compiled capability manifest include the base endpoint?" If not, the meta request is denied. The caller cannot discover endpoints they don't have permission to use.

## For Client SDK Developers

The capability manifest and meta endpoints together form a self-describing protocol:

1. **Connect** and receive your capability manifest (list of available endpoints with salted GUIDs)
2. **Query meta** for any endpoint to discover its request/response schemas
3. **Build dynamic UI** or validation from the schemas -- no hardcoded contracts needed

This enables fully dynamic client SDKs that adapt to server changes at connection time.

## For Service Developers

Meta endpoints are fully automatic. When you add a new endpoint to a service's OpenAPI schema and regenerate:

- The meta controller is generated with embedded schemas
- The endpoint appears in capability manifests for authorized sessions
- Meta requests work immediately via both WebSocket and HTTP

No manual work is needed. The only configuration is the service's `x-permissions` array in the schema, which controls which sessions can see the endpoint (and by extension, its meta endpoints).
