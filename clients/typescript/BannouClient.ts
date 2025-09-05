/**
 * Bannou WebSocket Client SDK
 * 
 * Production-ready client for connecting to Bannou services through
 * the Connect service WebSocket gateway.
 */

import {
    IWebSocketClient,
    WebSocketClientConfig,
    ServiceCallOptions,
    ServiceRegistry,
    ServiceRequestMessage,
    ServiceResponseMessage,
    ServiceEventMessage,
    ConnectionResponseMessage,
    ErrorDetails,
    ErrorCode,
    ProtocolUtils,
    isServiceDiscoveryResponse,
    isServiceResponseMessage,
    isServiceEventMessage
} from './WebSocketProtocol';

export class BannouClient implements IWebSocketClient {
    private ws: WebSocket | null = null;
    private config: Required<WebSocketClientConfig>;
    private serviceRegistry: ServiceRegistry = {
        services: {},
        mappings: {},
        permissions: [],
        timestamp: 0
    };
    
    private pendingRequests = new Map<string, {
        resolve: (value: any) => void;
        reject: (error: any) => void;
        timeout: NodeJS.Timeout;
    }>();
    
    private eventHandlers = new Map<string, Set<(data?: any) => void>>();
    private reconnectAttempts = 0;
    private heartbeatTimer: NodeJS.Timeout | null = null;

    constructor(config: WebSocketClientConfig) {
        this.config = {
            reconnect_attempts: 3,
            heartbeat_interval: 30000, // 30 seconds
            request_timeout: 10000,    // 10 seconds
            ...config
        };
    }

    // ========================================================================
    // Connection Management
    // ========================================================================

    async connect(): Promise<void> {
        if (this.ws?.readyState === WebSocket.OPEN) {
            return; // Already connected
        }

        return new Promise((resolve, reject) => {
            try {
                this.ws = new WebSocket(this.config.url);
                
                this.ws.onopen = () => {
                    console.log('🔌 Connected to Bannou Connect service');
                    this.reconnectAttempts = 0;
                    this.startHeartbeat();
                    this.emit('connected');
                    resolve();
                };
                
                this.ws.onclose = (event) => {
                    console.log('🔌 Disconnected from Connect service:', event.code, event.reason);
                    this.stopHeartbeat();
                    this.handleDisconnection();
                };
                
                this.ws.onerror = (error) => {
                    console.error('🚨 WebSocket error:', error);
                    this.emit('error', error);
                    reject(error);
                };
                
                this.ws.onmessage = (event) => {
                    this.handleMessage(event.data);
                };
                
                // Connection timeout
                setTimeout(() => {
                    if (this.ws?.readyState !== WebSocket.OPEN) {
                        reject(new Error('Connection timeout'));
                    }
                }, 5000);
                
            } catch (error) {
                reject(error);
            }
        });
    }

    async disconnect(): Promise<void> {
        this.stopHeartbeat();
        
        if (this.ws) {
            this.ws.close(1000, 'Client disconnect');
            this.ws = null;
        }
        
        // Reject all pending requests
        for (const [id, request] of this.pendingRequests) {
            clearTimeout(request.timeout);
            request.reject(new Error('Client disconnected'));
        }
        this.pendingRequests.clear();
    }

    isConnected(): boolean {
        return this.ws?.readyState === WebSocket.OPEN;
    }

    // ========================================================================
    // Service Discovery
    // ========================================================================

    getAvailableServices(): ServiceRegistry {
        return { ...this.serviceRegistry };
    }

    async refreshServices(): Promise<ServiceRegistry> {
        // Send service discovery request
        const response = await this.call('system.discover_services');
        
        if (isServiceDiscoveryResponse(response)) {
            this.serviceRegistry = {
                services: {},
                mappings: response.services,
                permissions: [],
                timestamp: Date.now()
            };
        }
        
        return this.getAvailableServices();
    }

    // ========================================================================
    // Service Communication
    // ========================================================================

    async call<T = any>(method: string, payload?: any, options: ServiceCallOptions = {}): Promise<T> {
        if (!this.isConnected()) {
            throw new Error('Not connected to Bannou service');
        }

        const serviceGuid = this.serviceRegistry.mappings[method];
        if (!serviceGuid) {
            throw new Error(`Unknown service method: ${method}`);
        }

        const correlationId = options.correlation_id || this.generateCorrelationId();
        const timeout = options.timeout || this.config.request_timeout;

        const request: ServiceRequestMessage = {
            type: 'service_request',
            timestamp: Date.now(),
            correlation_id: correlationId,
            service_method: method,
            payload: payload,
            expect_response: options.expect_response !== false
        };

        return new Promise((resolve, reject) => {
            // Set up timeout
            const timeoutHandle = setTimeout(() => {
                this.pendingRequests.delete(correlationId);
                reject(new Error(`Request timeout for ${method}`));
            }, timeout);

            // Store pending request
            this.pendingRequests.set(correlationId, {
                resolve,
                reject,
                timeout: timeoutHandle
            });

            // Send binary message
            try {
                const messageData = this.serializeMessage(request);
                const binaryMessage = ProtocolUtils.packMessage(serviceGuid, messageData);
                this.ws!.send(binaryMessage);
            } catch (error) {
                this.pendingRequests.delete(correlationId);
                clearTimeout(timeoutHandle);
                reject(error);
            }
        });
    }

    send(method: string, payload?: any, options: ServiceCallOptions = {}): void {
        options.expect_response = false;
        this.call(method, payload, options).catch(error => {
            console.warn('🚨 Fire-and-forget message failed:', error);
        });
    }

    // ========================================================================
    // Additional Connections
    // ========================================================================

    async requestConnection(
        type: 'tcp' | 'udp', 
        purpose: string, 
        service: string
    ): Promise<ConnectionResponseMessage> {
        const response = await this.call(`${service}.request_connection`, {
            connection_type: type,
            purpose: purpose
        });

        if (response.connection_info) {
            console.log(`🔗 Additional ${type.toUpperCase()} connection available:`, response.connection_info.url);
        }

        return response;
    }

    // ========================================================================
    // Event Handling
    // ========================================================================

    on(event: 'connected' | 'disconnected' | 'error' | 'service_event', handler: (data?: any) => void): void {
        if (!this.eventHandlers.has(event)) {
            this.eventHandlers.set(event, new Set());
        }
        this.eventHandlers.get(event)!.add(handler);
    }

    off(event: string, handler?: (data?: any) => void): void {
        const handlers = this.eventHandlers.get(event);
        if (!handlers) return;
        
        if (handler) {
            handlers.delete(handler);
        } else {
            handlers.clear();
        }
    }

    private emit(event: string, data?: any): void {
        const handlers = this.eventHandlers.get(event);
        if (handlers) {
            handlers.forEach(handler => {
                try {
                    handler(data);
                } catch (error) {
                    console.error(`🚨 Event handler error for ${event}:`, error);
                }
            });
        }
    }

    // ========================================================================
    // Message Handling
    // ========================================================================

    private handleMessage(data: ArrayBuffer | string): void {
        try {
            if (data instanceof ArrayBuffer) {
                this.handleBinaryMessage(new Uint8Array(data));
            } else {
                this.handleTextMessage(data);
            }
        } catch (error) {
            console.error('🚨 Message handling error:', error);
        }
    }

    private handleBinaryMessage(data: Uint8Array): void {
        const message = ProtocolUtils.unpackMessage(data);
        const payload = this.deserializeMessage(message.payload);
        
        if (isServiceResponseMessage(payload)) {
            this.handleServiceResponse(payload);
        } else if (isServiceEventMessage(payload)) {
            this.handleServiceEvent(payload);
        }
    }

    private handleTextMessage(data: string): void {
        const message = JSON.parse(data);
        
        if (isServiceDiscoveryResponse(message)) {
            this.handleServiceDiscovery(message);
        }
    }

    private handleServiceResponse(response: ServiceResponseMessage): void {
        const correlationId = response.correlation_id;
        if (!correlationId) return;
        
        const pending = this.pendingRequests.get(correlationId);
        if (!pending) return;
        
        this.pendingRequests.delete(correlationId);
        clearTimeout(pending.timeout);
        
        if (response.success) {
            pending.resolve(response.payload);
        } else {
            const error = new Error(response.error?.message || 'Service request failed');
            (error as any).code = response.error?.code;
            (error as any).details = response.error?.details;
            pending.reject(error);
        }
    }

    private handleServiceEvent(event: ServiceEventMessage): void {
        console.log('📡 Service event received:', event.event_name);
        this.emit('service_event', event);
    }

    private handleServiceDiscovery(discovery: any): void {
        console.log('🗺️  Service discovery received:', Object.keys(discovery.services).length, 'services');
        
        this.serviceRegistry = {
            services: {},
            mappings: discovery.services,
            permissions: [],
            timestamp: Date.now()
        };
    }

    // ========================================================================
    // Connection Recovery
    // ========================================================================

    private handleDisconnection(): void {
        this.emit('disconnected');
        
        if (this.reconnectAttempts < this.config.reconnect_attempts) {
            this.reconnectAttempts++;
            console.log(`🔄 Attempting reconnection (${this.reconnectAttempts}/${this.config.reconnect_attempts})`);
            
            setTimeout(() => {
                this.connect().catch(error => {
                    console.error('🚨 Reconnection failed:', error);
                });
            }, Math.pow(2, this.reconnectAttempts) * 1000); // Exponential backoff
        }
    }

    private startHeartbeat(): void {
        this.heartbeatTimer = setInterval(() => {
            if (this.isConnected()) {
                this.send('system.ping');
            }
        }, this.config.heartbeat_interval);
    }

    private stopHeartbeat(): void {
        if (this.heartbeatTimer) {
            clearInterval(this.heartbeatTimer);
            this.heartbeatTimer = null;
        }
    }

    // ========================================================================
    // Utilities
    // ========================================================================

    private generateCorrelationId(): string {
        return Date.now().toString(36) + Math.random().toString(36).substr(2);
    }

    private serializeMessage(message: any): Uint8Array {
        const json = JSON.stringify(message);
        return new TextEncoder().encode(json);
    }

    private deserializeMessage(data: Uint8Array): any {
        const json = new TextDecoder().decode(data);
        return JSON.parse(json);
    }
}

// ============================================================================
// Typed Service Clients
// ============================================================================

/**
 * Type-safe wrapper for account service calls
 */
export class AccountService {
    constructor(private client: BannouClient) {}

    async create(request: {username: string, email?: string, password: string}) {
        return this.client.call('account.create', request);
    }

    async get(accountId: string) {
        return this.client.call('account.get', {account_id: accountId});
    }

    async update(accountId: string, updates: Partial<{username: string, email: string}>) {
        return this.client.call('account.update', {account_id: accountId, ...updates});
    }

    async delete(accountId: string) {
        return this.client.call('account.delete', {account_id: accountId});
    }
}

/**
 * Type-safe wrapper for auth service calls  
 */
export class AuthService {
    constructor(private client: BannouClient) {}

    async register(username: string, password: string, email?: string) {
        return this.client.call('auth.register', {username, password, email});
    }

    async login(username: string, password: string) {
        return this.client.call('auth.login', {username, password});
    }

    async loginWithToken(token: string) {
        return this.client.call('auth.login_token', {token});
    }

    async validateToken(token: string) {
        return this.client.call('auth.validate', {token});
    }
}

/**
 * Main Bannou SDK with typed service clients
 */
export class BannouSDK {
    public readonly client: BannouClient;
    public readonly accounts: AccountService;
    public readonly auth: AuthService;

    constructor(config: WebSocketClientConfig) {
        this.client = new BannouClient(config);
        this.accounts = new AccountService(this.client);
        this.auth = new AuthService(this.client);
    }

    async connect(): Promise<void> {
        return this.client.connect();
    }

    async disconnect(): Promise<void> {
        return this.client.disconnect();
    }
}

// ============================================================================
// Usage Example
// ============================================================================

/*
// Initialize SDK
const bannou = new BannouSDK({
    url: 'wss://connect.bannou.game',
    auth_token: 'jwt-token-here'
});

// Connect and use services
await bannou.connect();

// Type-safe service calls
const loginResponse = await bannou.auth.login('username', 'password');
const account = await bannou.accounts.get('account-id');

// Direct client access for custom calls
await bannou.client.call('game.join_server', {server_id: 'main'});

// Request additional TCP connection for low-latency input
const tcpConnection = await bannou.client.requestConnection('tcp', 'game_input', 'game');
console.log('Game input TCP:', tcpConnection.connection_info?.url);

// Event handling
bannou.client.on('service_event', (event) => {
    console.log('Event received:', event.event_name, event.payload);
});
*/