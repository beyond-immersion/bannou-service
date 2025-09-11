# WebSocket Protocol Architecture

## Overview

Bannou uses a WebSocket-first architecture where all client communication is routed through the Connect service, which acts as an intelligent edge gateway. This design enables zero-copy message routing, dynamic service discovery, and seamless integration with both Dapr microservices and peer-to-peer client communication.

## Core Architecture

### Connection Establishment

1. **Initial WebSocket Connection**
   ```
   Client --WebSocket--> Connect Service
   ```

2. **Service Discovery Response**
   ```json
   {
     "type": "service_discovery",
     "services": {
       "account.create": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
       "account.get": "b2c3d4e5-f6g7-8901-bcde-f23456789012", 
       "auth.login": "c3d4e5f6-g7h8-9012-cdef-345678901234",
       "game.connect": "d4e5f6g7-h8i9-0123-def0-456789012345"
     },
     "client_id": "client-uuid-for-rabbitmq"
   }
   ```

3. **Client Registration**
   - Connect service registers client UUID with RabbitMQ for bidirectional messaging
   - Client-specific GUIDs are salted/unique per connection for security

## Message Routing Protocol

### Request Flow
```
Client Request → WebSocket → Connect Service → Route Decision → Destination
```

### Binary Message Format
```
[Service GUID: 16 bytes][Message ID: 8 bytes][Payload: Variable]
```

### Routing Decision Matrix
```
Connect Service Routing Logic:
1. Check client connections hashset for Service GUID
   ├─ Found → Forward to connected client (P2P)
   └─ Not Found → Check Dapr service registry
       ├─ Has Permission → Forward to Dapr service
       └─ No Permission → Return 403 Forbidden
```

## Dual Routing Capability

### Client-to-Client Communication
- **Use Case**: P2P services, distributed game logic, shared resources
- **Flow**: `Client A --[Service GUID]--> Connect ---> Client B`
- **Benefits**: Same protocol for services and peers

### Client-to-Service Communication  
- **Use Case**: Authentication, account management, game server APIs
- **Flow**: `Client --[Service GUID]--> Connect ---> Dapr Service`
- **Benefits**: Zero-copy forwarding, service abstraction

## RabbitMQ Integration

### Server-to-Client Push
```
Service/Client --RabbitMQ--> Connect Service --WebSocket--> Target Client
```

### Bidirectional RPC
```
1. Service sends RPC request via RabbitMQ → Connect → Client
2. Client responds with same Message ID → Connect → RabbitMQ → Service
```

## Progressive Access Control

### Initial Connection
- Client receives basic service mappings based on initial JWT claims
- Limited to public APIs (registration, login, etc.)

### Post-Authentication 
- Successful login unlocks additional service mappings
- New services appear in client's available API list
- Game-specific services become accessible

### Additional Media Connections
- WebSocket used to negotiate separate TCP connections
- **Example Flow**:
  ```
  1. Client calls "game.connect" via WebSocket
  2. Game service returns: {"tcp_url": "game1.server:9001", "auth_token": "xyz"}
  3. Client establishes direct TCP connection for low-latency input/location data
  4. WebSocket remains for API calls, events, and general communication
  ```

## Security Features

### Client-Specific GUIDs
- Same service gets different GUID per client connection
- GUIDs are salted to prevent predictable patterns
- **Example**: 
  - Client A: `account.create` → `a1b2c3d4-e5f6-7890-abcd-ef1234567890`
  - Client B: `account.create` → `x9y8z7w6-v5u4-3210-zyxw-vu0987654321`

### Permission-Based Discovery
- Clients only receive mappings for services they can access
- Service list dynamically updates based on authentication state
- Unauthorized services never appear in client mapping

### Zero-Copy Routing
- Connect service never deserializes message payloads
- Only reads Service GUID header for routing decisions
- Reduces latency and prevents data inspection

## Implementation Benefits

### For Clients
- **Single Connection**: One WebSocket handles all API communication
- **Type Safety**: Service mappings enable compile-time API validation
- **Seamless P2P**: Same protocol for services and peer communication
- **Progressive Enhancement**: More services unlock as authentication progresses

### for Services
- **Service Abstraction**: Services don't know about client routing
- **Push Capability**: RabbitMQ enables server-initiated communication
- **Load Distribution**: Connect service handles connection management
- **Security Isolation**: Services never directly handle client connections

### For Infrastructure
- **Scalability**: Multiple Connect service instances can load balance
- **Fault Tolerance**: Client connections can failover between Connect instances
- **Resource Efficiency**: Zero-copy routing minimizes CPU overhead
- **Protocol Flexibility**: Additional media connections for specialized needs

## Protocol Extensions

### Future Capabilities
- **Connection Pooling**: Multiple WebSocket connections per client for throughput
- **Service Mesh Integration**: Automatic discovery of new Dapr services
- **Circuit Breakers**: Automatic failover for unhealthy services  
- **Rate Limiting**: Per-client, per-service request limiting
- **Analytics**: Request pattern analysis without payload inspection

This architecture provides a robust foundation for Arcadia's massive multiplayer simulation while maintaining security, performance, and developer experience.