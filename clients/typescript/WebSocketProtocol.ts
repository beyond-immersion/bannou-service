/**
 * WebSocket Protocol Definitions for Bannou Edge Communication
 * 
 * This module defines the binary protocol used for client-server communication
 * through the Connect service WebSocket gateway.
 */

// ============================================================================
// Core Protocol Types
// ============================================================================

/**
 * Service mapping received during initial connection
 * Maps human-readable service names to client-specific GUIDs
 */
export interface ServiceDiscoveryResponse {
    type: 'service_discovery';
    services: Record<string, string>; // "account.create" -> "a1b2c3d4-..."
    client_id: string; // UUID for RabbitMQ registration
    session_id?: string; // Optional session identifier
}

/**
 * Binary message header structure
 * Total: 24 bytes (16 + 8)
 */
export interface MessageHeader {
    service_guid: Uint8Array; // 16 bytes - Service/client identifier
    message_id: Uint8Array;   // 8 bytes - Request/response correlation
}

/**
 * Complete WebSocket message structure
 */
export interface WebSocketMessage {
    header: MessageHeader;
    payload: Uint8Array; // Serialized request/response data
}

// ============================================================================
// Service Registry Types
// ============================================================================

/**
 * Service capability information
 */
export interface ServiceInfo {
    name: string;           // "account", "auth", "game"
    methods: string[];      // ["create", "get", "update", "delete"]
    version: string;        // "1.0.0"
    description?: string;   // Human-readable description
    requires_auth: boolean; // Whether service requires authentication
}

/**
 * Available services with their mappings
 */
export interface ServiceRegistry {
    services: Record<string, ServiceInfo>; // Service metadata
    mappings: Record<string, string>;      // Method -> GUID mappings
    permissions: string[];                 // Client's current permissions/scopes
    timestamp: number;                     // When registry was generated
}

// ============================================================================
// Message Types
// ============================================================================

/**
 * Request message types
 */
export type WebSocketMessageType = 
    | 'service_discovery'
    | 'service_request' 
    | 'service_response'
    | 'service_event'
    | 'connection_request'
    | 'error'
    | 'ping'
    | 'pong';

/**
 * Base message envelope
 */
export interface BaseMessage {
    type: WebSocketMessageType;
    timestamp: number;
    correlation_id?: string;
}

/**
 * Service request through WebSocket
 */
export interface ServiceRequestMessage extends BaseMessage {
    type: 'service_request';
    service_method: string;  // "account.create"
    payload: any;           // Request data (will be serialized)
    expect_response: boolean; // Whether client expects a response
}

/**
 * Service response from server
 */
export interface ServiceResponseMessage extends BaseMessage {
    type: 'service_response';
    success: boolean;
    payload?: any;          // Response data
    error?: ErrorDetails;   // Error information if success = false
}

/**
 * Server-initiated event (via RabbitMQ)
 */
export interface ServiceEventMessage extends BaseMessage {
    type: 'service_event';
    event_name: string;     // "user.login", "game.state_changed"
    payload: any;           // Event data
    source_service?: string; // Which service generated the event
}

/**
 * Additional connection request (TCP, UDP, etc.)
 */
export interface ConnectionRequestMessage extends BaseMessage {
    type: 'connection_request';
    connection_type: 'tcp' | 'udp' | 'websocket';
    purpose: 'game_input' | 'audio' | 'video' | 'file_transfer';
    service_method: string; // Which service to request connection from
}

/**
 * Connection response with credentials
 */
export interface ConnectionResponseMessage extends BaseMessage {
    type: 'service_response';
    connection_info?: {
        url: string;        // "game1.server:9001"
        protocol: string;   // "tcp", "udp", "ws"
        auth_token?: string; // Temporary credentials
        expires_at?: number; // Token expiration timestamp
    };
    error?: ErrorDetails;
}

// ============================================================================
// Error Handling
// ============================================================================

/**
 * Error details structure
 */
export interface ErrorDetails {
    code: string;           // "UNAUTHORIZED", "SERVICE_UNAVAILABLE"
    message: string;        // Human-readable error message
    details?: any;          // Additional error context
    retry_after?: number;   // Milliseconds to wait before retry
}

/**
 * Common error codes
 */
export enum ErrorCode {
    // Client errors
    UNAUTHORIZED = 'UNAUTHORIZED',
    FORBIDDEN = 'FORBIDDEN', 
    BAD_REQUEST = 'BAD_REQUEST',
    SERVICE_NOT_FOUND = 'SERVICE_NOT_FOUND',
    
    // Server errors
    SERVICE_UNAVAILABLE = 'SERVICE_UNAVAILABLE',
    INTERNAL_ERROR = 'INTERNAL_ERROR',
    TIMEOUT = 'TIMEOUT',
    
    // Protocol errors  
    INVALID_MESSAGE_FORMAT = 'INVALID_MESSAGE_FORMAT',
    UNSUPPORTED_MESSAGE_TYPE = 'UNSUPPORTED_MESSAGE_TYPE',
    CORRELATION_ID_MISMATCH = 'CORRELATION_ID_MISMATCH'
}

// ============================================================================
// Client SDK Interface
// ============================================================================

/**
 * WebSocket client configuration
 */
export interface WebSocketClientConfig {
    url: string;                    // WebSocket server URL
    auth_token?: string;           // Initial JWT token
    reconnect_attempts?: number;    // Auto-reconnection attempts
    heartbeat_interval?: number;    // Ping interval in milliseconds
    request_timeout?: number;       // Default request timeout
}

/**
 * Service call options
 */
export interface ServiceCallOptions {
    timeout?: number;       // Override default timeout
    expect_response?: boolean; // Whether to wait for response
    retry_attempts?: number;   // Number of retry attempts
    correlation_id?: string;   // Custom correlation ID
}

/**
 * WebSocket client interface
 */
export interface IWebSocketClient {
    // Connection management
    connect(): Promise<void>;
    disconnect(): Promise<void>;
    isConnected(): boolean;
    
    // Service discovery
    getAvailableServices(): ServiceRegistry;
    refreshServices(): Promise<ServiceRegistry>;
    
    // Service calls
    call<T = any>(method: string, payload?: any, options?: ServiceCallOptions): Promise<T>;
    send(method: string, payload?: any, options?: ServiceCallOptions): void;
    
    // Event handling
    on(event: 'connected' | 'disconnected' | 'error' | 'service_event', handler: (data?: any) => void): void;
    off(event: string, handler?: (data?: any) => void): void;
    
    // Additional connections
    requestConnection(type: 'tcp' | 'udp', purpose: string, service: string): Promise<ConnectionResponseMessage>;
}

// ============================================================================
// Binary Protocol Utilities
// ============================================================================

/**
 * Binary protocol utilities
 */
export class ProtocolUtils {
    /**
     * Generate message ID (8 bytes)
     */
    static generateMessageId(): Uint8Array {
        const id = new Uint8Array(8);
        crypto.getRandomValues(id);
        return id;
    }
    
    /**
     * Convert GUID string to bytes (16 bytes)
     */
    static guidToBytes(guid: string): Uint8Array {
        const hex = guid.replace(/-/g, '');
        const bytes = new Uint8Array(16);
        for (let i = 0; i < 16; i++) {
            bytes[i] = parseInt(hex.substr(i * 2, 2), 16);
        }
        return bytes;
    }
    
    /**
     * Convert bytes to GUID string
     */
    static bytesToGuid(bytes: Uint8Array): string {
        const hex = Array.from(bytes)
            .map(b => b.toString(16).padStart(2, '0'))
            .join('');
        return [
            hex.substr(0, 8),
            hex.substr(8, 4),
            hex.substr(12, 4),
            hex.substr(16, 4),
            hex.substr(20, 12)
        ].join('-');
    }
    
    /**
     * Pack message header
     */
    static packHeader(serviceGuid: string, messageId?: Uint8Array): Uint8Array {
        const header = new Uint8Array(24);
        const guidBytes = this.guidToBytes(serviceGuid);
        const msgIdBytes = messageId || this.generateMessageId();
        
        header.set(guidBytes, 0);      // Service GUID at offset 0
        header.set(msgIdBytes, 16);    // Message ID at offset 16
        
        return header;
    }
    
    /**
     * Unpack message header
     */
    static unpackHeader(data: Uint8Array): MessageHeader {
        return {
            service_guid: data.slice(0, 16),
            message_id: data.slice(16, 24)
        };
    }
    
    /**
     * Pack complete WebSocket message
     */
    static packMessage(serviceGuid: string, payload: Uint8Array, messageId?: Uint8Array): Uint8Array {
        const header = this.packHeader(serviceGuid, messageId);
        const message = new Uint8Array(header.length + payload.length);
        
        message.set(header, 0);
        message.set(payload, header.length);
        
        return message;
    }
    
    /**
     * Unpack WebSocket message
     */
    static unpackMessage(data: Uint8Array): WebSocketMessage {
        return {
            header: this.unpackHeader(data),
            payload: data.slice(24)
        };
    }
}

// ============================================================================
// Type Guards
// ============================================================================

export function isServiceDiscoveryResponse(msg: any): msg is ServiceDiscoveryResponse {
    return msg && msg.type === 'service_discovery' && msg.services && msg.client_id;
}

export function isServiceRequestMessage(msg: any): msg is ServiceRequestMessage {
    return msg && msg.type === 'service_request' && msg.service_method;
}

export function isServiceResponseMessage(msg: any): msg is ServiceResponseMessage {
    return msg && msg.type === 'service_response' && typeof msg.success === 'boolean';
}

export function isServiceEventMessage(msg: any): msg is ServiceEventMessage {
    return msg && msg.type === 'service_event' && msg.event_name;
}

export function isErrorMessage(msg: any): msg is ServiceResponseMessage {
    return msg && msg.type === 'service_response' && msg.success === false && msg.error;
}