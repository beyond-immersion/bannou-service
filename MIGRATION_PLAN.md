# Bannou Service Migration Plan

## Overview
This document outlines the comprehensive migration of all Bannou services from manual implementation to schema-driven architecture using NSwag code generation.

## Current State Assessment

### Working Components
- ✅ **Behaviour Service**: Fully schema-driven with passing tests
- ✅ **Main Application**: Builds successfully with assembly loading
- ✅ **Testing Infrastructure**: Dual-transport validation framework

### Broken Components
- ❌ **Accounts Service**: 238 compilation errors (schema mismatch)
- ❌ **Auth Service**: Broken dependencies
- ❌ **Connect Service**: Not schema-driven
- ❌ **Integration Tests**: Failing due to service issues

### Critical Elements to Preserve
1. **Connect Service Binary Protocol**
   - MessageFlags enum (Binary, Encrypted, Compressed, etc.)
   - ResponseCodes enum (OK, Unauthorized, ServiceNotFound, etc.)
   - 24-byte header structure (Flags + ServiceID + MessageID)
   - Zero-copy routing implementation

2. **Service Discovery Pattern**
   - DaprService attribute system
   - Assembly loading configuration
   - ServiceAppMappingResolver

## Migration Strategy

### Phase 1: Core Infrastructure (Week 1)

#### 1.1 Fix Accounts Service
**Priority**: CRITICAL - Blocking all other services

**Tasks**:
1. Backup current implementation
2. Review and correct schemas/accounts-api.yaml
3. Regenerate controllers with NSwag
4. Migrate business logic to new structure
5. Fix all compilation errors
6. Update tests to match new API

**Schema Enhancements**:
```yaml
/accounts:
  post:
    summary: Create account
    requestBody:
      required: true
      content:
        application/json:
          schema:
            type: object
            required: [username, password, email]
            properties:
              username:
                type: string
                minLength: 3
                maxLength: 32
                pattern: "^[a-zA-Z0-9_-]+$"
              password:
                type: string
                minLength: 8
                format: password
              email:
                type: string
                format: email
              realm:
                type: string
                enum: [omega, arcadia, fantasia]
                default: omega
    responses:
      201:
        description: Account created
        content:
          application/json:
            schema:
              type: object
              properties:
                accountId:
                  type: string
                  format: uuid
                characterSlots:
                  type: integer
                  default: 3
                premiumStatus:
                  type: boolean
                  default: false

/accounts/{accountId}:
  get:
    summary: Get account details
    parameters:
      - name: accountId
        in: path
        required: true
        schema:
          type: string
          format: uuid
      - name: includeCharacters
        in: query
        schema:
          type: boolean
          default: false
    responses:
      200:
        description: Account details
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/Account'

/accounts/{accountId}/link:
  post:
    summary: Link external account (Steam, Google, etc.)
    parameters:
      - name: accountId
        in: path
        required: true
        schema:
          type: string
          format: uuid
    requestBody:
      required: true
      content:
        application/json:
          schema:
            type: object
            required: [provider, externalId]
            properties:
              provider:
                type: string
                enum: [steam, google, discord]
              externalId:
                type: string
              verificationToken:
                type: string
```

#### 1.2 Rebuild Auth Service
**Priority**: CRITICAL - Required for all authenticated operations

**Tasks**:
1. Design comprehensive auth schema
2. Implement JWT token generation
3. Add OAuth2 flows
4. Implement refresh tokens
5. Add RBAC support
6. Create auth event schemas

**Schema Structure**:
```yaml
/auth/login:
  post:
    summary: Authenticate user
    requestBody:
      required: true
      content:
        application/json:
          schema:
            type: object
            required: [username, password]
            properties:
              username:
                type: string
              password:
                type: string
                format: password
              realm:
                type: string
                enum: [omega, arcadia, fantasia]
    responses:
      200:
        description: Authentication successful
        content:
          application/json:
            schema:
              type: object
              properties:
                accessToken:
                  type: string
                refreshToken:
                  type: string
                expiresIn:
                  type: integer
                  description: Seconds until token expires
                accountId:
                  type: string
                  format: uuid
                roles:
                  type: array
                  items:
                    type: string

/auth/refresh:
  post:
    summary: Refresh access token
    requestBody:
      required: true
      content:
        application/json:
          schema:
            type: object
            required: [refreshToken]
            properties:
              refreshToken:
                type: string

/auth/validate:
  post:
    summary: Validate access token
    security:
      - BearerAuth: []
    responses:
      200:
        description: Token is valid
        content:
          application/json:
            schema:
              type: object
              properties:
                accountId:
                  type: string
                  format: uuid
                roles:
                  type: array
                  items:
                    type: string
                remainingTime:
                  type: integer
                  description: Seconds until expiration

/auth/oauth/{provider}:
  get:
    summary: Initiate OAuth2 flow
    parameters:
      - name: provider
        in: path
        required: true
        schema:
          type: string
          enum: [steam, google, discord]
      - name: redirectUri
        in: query
        required: true
        schema:
          type: string
          format: uri
    responses:
      302:
        description: Redirect to OAuth provider
        headers:
          Location:
            schema:
              type: string
              format: uri

/auth/oauth/{provider}/callback:
  post:
    summary: OAuth2 callback
    parameters:
      - name: provider
        in: path
        required: true
        schema:
          type: string
          enum: [steam, google, discord]
    requestBody:
      required: true
      content:
        application/json:
          schema:
            type: object
            required: [code, state]
            properties:
              code:
                type: string
              state:
                type: string
```

#### 1.3 Schema-Drive Connect Service
**Priority**: HIGH - Core infrastructure component

**Tasks**:
1. Extract binary protocol to lib-connect-core
2. Create management API schema
3. Preserve WebSocket upgrade logic
4. Implement service discovery endpoints
5. Add connection metrics APIs

**Schema Focus**:
```yaml
/connect/services:
  get:
    summary: Get available services for client
    security:
      - BearerAuth: []
    responses:
      200:
        description: Service mappings
        content:
          application/json:
            schema:
              type: object
              additionalProperties:
                type: string
                format: uuid
                description: Service GUID for routing

/connect/status:
  get:
    summary: Connection status and metrics
    responses:
      200:
        description: Connection statistics
        content:
          application/json:
            schema:
              type: object
              properties:
                activeConnections:
                  type: integer
                messagesPerSecond:
                  type: number
                averageLatency:
                  type: number
                uptime:
                  type: integer

/connect/websocket:
  get:
    summary: WebSocket upgrade endpoint
    responses:
      101:
        description: Switching Protocols
        headers:
          Upgrade:
            schema:
              type: string
              enum: [websocket]
          Connection:
            schema:
              type: string
              enum: [Upgrade]
```

### Phase 2: Game Services (Week 2)

#### 2.1 Character Service
**New Service** - Core Arcadia functionality

**Schema Requirements**:
```yaml
/characters:
  post:
    summary: Create character
    requestBody:
      required: true
      content:
        application/json:
          schema:
            type: object
            required: [accountId, name, realm]
            properties:
              accountId:
                type: string
                format: uuid
              name:
                type: string
                minLength: 2
                maxLength: 32
              realm:
                type: string
                enum: [omega, arcadia, fantasia]
              traits:
                $ref: '#/components/schemas/CharacterTraits'
              guardianSpiritId:
                type: string
                format: uuid
                nullable: true

/characters/{characterId}/possess:
  post:
    summary: Guardian spirit possession
    parameters:
      - name: characterId
        in: path
        required: true
        schema:
          type: string
          format: uuid
    requestBody:
      required: true
      content:
        application/json:
          schema:
            type: object
            required: [spiritId]
            properties:
              spiritId:
                type: string
                format: uuid
              syncLevel:
                type: number
                minimum: 0
                maximum: 100

components:
  schemas:
    CharacterTraits:
      type: object
      properties:
        physical:
          type: object
          properties:
            strength:
              type: integer
              minimum: 1
              maximum: 100
            agility:
              type: integer
              minimum: 1
              maximum: 100
            constitution:
              type: integer
              minimum: 1
              maximum: 100
        mental:
          type: object
          properties:
            intelligence:
              type: integer
              minimum: 1
              maximum: 100
            wisdom:
              type: integer
              minimum: 1
              maximum: 100
            charisma:
              type: integer
              minimum: 1
              maximum: 100
        magical:
          type: object
          properties:
            fire:
              type: integer
              minimum: 0
              maximum: 100
            water:
              type: integer
              minimum: 0
              maximum: 100
            earth:
              type: integer
              minimum: 0
              maximum: 100
            air:
              type: integer
              minimum: 0
              maximum: 100
        behavioral:
          type: array
          items:
            type: string
            description: ABML behavior tags
        divine:
          type: array
          items:
            type: string
            description: Divine blessings or curses
```

#### 2.2 World State Service
**New Service** - Realm coordination

**Schema Requirements**:
```yaml
/world/time:
  get:
    summary: Get current world time
    parameters:
      - name: realm
        in: query
        required: true
        schema:
          type: string
          enum: [omega, arcadia, fantasia]
    responses:
      200:
        description: World time information
        content:
          application/json:
            schema:
              type: object
              properties:
                currentTime:
                  type: string
                  format: date-time
                dayProgress:
                  type: number
                  minimum: 0
                  maximum: 1
                  description: Progress through 4-hour day (0=dawn, 0.5=noon, 1=next dawn)
                season:
                  type: string
                  enum: [spring, summer, autumn, winter]
                weather:
                  type: string
                  enum: [clear, cloudy, rain, storm, snow]

/world/events:
  post:
    summary: Broadcast world event
    requestBody:
      required: true
      content:
        application/json:
          schema:
            type: object
            required: [realm, eventType, data]
            properties:
              realm:
                type: string
                enum: [omega, arcadia, fantasia]
              eventType:
                type: string
              data:
                type: object
                additionalProperties: true
              priority:
                type: string
                enum: [low, normal, high, critical]
                default: normal
```

### Phase 3: Event System (Week 3)

#### 3.1 Event Schema Pattern
Each service gets `schemas/{service}-events.yaml`:

```yaml
# schemas/auth-events.yaml
components:
  schemas:
    LoginEvent:
      type: object
      required: [accountId, timestamp, realm]
      properties:
        accountId:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        realm:
          type: string
          enum: [omega, arcadia, fantasia]
        ipAddress:
          type: string
        
    LogoutEvent:
      type: object
      required: [accountId, timestamp]
      properties:
        accountId:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        reason:
          type: string
          enum: [user_initiated, token_expired, forced]
    
    TokenRefreshedEvent:
      type: object
      required: [accountId, timestamp]
      properties:
        accountId:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        newExpiration:
          type: string
          format: date-time
```

#### 3.2 Dapr Pub/Sub Configuration
```yaml
# dapr/pubsub.yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: bannou-pubsub
spec:
  type: pubsub.rabbitmq
  version: v1
  metadata:
  - name: host
    value: "amqp://localhost:5672"
  - name: consumerID
    value: "bannou-service"
  - name: durable
    value: "true"
  - name: deletedWhenUnused
    value: "false"
  - name: autoAck
    value: "false"
```

### Phase 4: External Integration (Week 4)

#### 4.1 External Service Contracts
For services not part of Bannou:

```yaml
# schemas/external/freeswitch-api.yaml
# Documentation-only schema for FreeSWITCH integration
/calls/originate:
  post:
    summary: Originate call via FreeSWITCH
    x-external-service: true
    x-dapr-app-id: freeswitch-service
    requestBody:
      required: true
      content:
        application/json:
          schema:
            type: object
            required: [from, to, context]
            properties:
              from:
                type: string
              to:
                type: string
              context:
                type: string
              variables:
                type: object
                additionalProperties:
                  type: string
```

#### 4.2 Client Generation Strategy
```json
// nswag-external.json
{
  "runtime": "Net90",
  "defaultVariables": null,
  "documentGenerator": {
    "fromDocument": {
      "url": "schemas/external/freeswitch-api.yaml",
      "output": null
    }
  },
  "codeGenerators": {
    "openApiToCSharpClient": {
      "generateControllerInterfaces": false,
      "generateDtoTypes": true,
      "namespace": "BeyondImmersion.BannouService.External.FreeSWITCH",
      "output": "lib-external-clients/FreeSWITCH/FreeSWITCHClient.cs"
    }
  }
}
```

## Testing Strategy

### Unit Testing
- Each service must have 90%+ code coverage
- Schema compliance tests auto-generated
- Business logic tests in separate files

### Integration Testing
```bash
# Test execution order
1. dotnet test lib-accounts-core
2. dotnet test lib-auth-core  
3. dotnet test lib-connect-core
4. dotnet run --project http-tester
5. dotnet run --project edge-tester
6. docker compose -f provisioning/docker-compose.yml up --exit-code-from=bannou-tester
```

### Performance Testing
- Target: 10,000 concurrent WebSocket connections
- Message throughput: 100,000 msgs/sec
- Latency: P99 < 10ms for routing

## Migration Timeline

### Week 1: Core Infrastructure
- Day 1-2: Fix Accounts Service
- Day 3-4: Rebuild Auth Service  
- Day 5: Schema-drive Connect Service

### Week 2: Game Services
- Day 1-2: Character Service
- Day 3-4: World State Service
- Day 5: Integration testing

### Week 3: Event System
- Day 1-2: Event schemas
- Day 3-4: Dapr pub/sub setup
- Day 5: Event integration testing

### Week 4: External Integration & Polish
- Day 1-2: External service contracts
- Day 3-4: Performance testing
- Day 5: Documentation update

## Success Criteria

1. ✅ All services build without errors
2. ✅ Unit test coverage > 90%
3. ✅ Integration tests passing
4. ✅ Dual-transport consistency
5. ✅ Performance targets met
6. ✅ Documentation complete

## Rollback Plan

If migration fails:
1. Branch preservation: Keep `client-application-initial` branch
2. Incremental migration: Service-by-service cutover
3. Feature flags: Toggle between old/new implementations
4. Database migrations: Reversible with down scripts

## Next Steps

1. Review and approve this migration plan
2. Create feature branch: `schema-driven-migration`
3. Begin with Accounts Service fix
4. Daily progress updates via GitHub issues
5. Weekly demos of completed services