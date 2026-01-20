import { ApiResponse, BaseClientEvent } from '@beyondimmersion/bannou-core';
export { ApiResponse, BannouJson, BaseClientEvent, ErrorResponse, createErrorResponse, fromJson, getErrorName, isBaseClientEvent, mapToHttpStatusCode, toJson } from '@beyondimmersion/bannou-core';
export { BinaryMessage, EMPTY_GUID, HEADER_SIZE, MessageFlags, MessageFlagsType, RESPONSE_HEADER_SIZE, ResponseCodes, ResponseCodesType, createRequest, createResponse, expectsResponse, getJsonPayload, getResponseCodeName, hasFlag, isBinaryFlag, isClientRouted, isError, isErrorCode, isEventFlag, isHighPriority, isMeta, isMetaFlag, isResponse, isResponseFlag, isSuccess, isSuccessCode, mapToHttpStatus, parse, readGuid, readUInt16, readUInt32, readUInt64, testNetworkByteOrderCompatibility, toByteArray, writeGuid, writeUInt16, writeUInt32, writeUInt64 } from './protocol/index.js';

/**
 * Represents a subscription to client events.
 * Call dispose() to unsubscribe.
 */
interface IEventSubscription {
    /**
     * The event name this subscription is for.
     */
    readonly eventName: string;
    /**
     * Unique identifier for this subscription.
     */
    readonly subscriptionId: string;
    /**
     * Unsubscribe from the event.
     */
    dispose(): void;
}
/**
 * Implementation of IEventSubscription.
 */
declare class EventSubscription implements IEventSubscription {
    readonly eventName: string;
    readonly subscriptionId: string;
    private readonly disposeCallback;
    private disposed;
    constructor(eventName: string, subscriptionId: string, disposeCallback: (subscriptionId: string) => void);
    dispose(): void;
}

/**
 * Interface for the Bannou WebSocket client.
 * Provides a mockable interface for testing.
 */

/**
 * Interface for the Bannou WebSocket client.
 */
interface IBannouClient {
    /**
     * Whether the client is currently connected.
     */
    readonly isConnected: boolean;
    /**
     * Session ID assigned by the server after connection.
     */
    readonly sessionId: string | undefined;
    /**
     * All available API endpoints with their client-salted GUIDs.
     * Key format: "METHOD:/path" (e.g., "POST:/species/get")
     */
    readonly availableApis: ReadonlyMap<string, string>;
    /**
     * Current access token (JWT).
     */
    readonly accessToken: string | undefined;
    /**
     * Current refresh token for re-authentication.
     */
    readonly refreshToken: string | undefined;
    /**
     * Last error message from a failed operation.
     */
    readonly lastError: string | undefined;
    /**
     * Connects to a Bannou server using username/password authentication.
     * @param serverUrl - Base URL (e.g., "http://localhost:8080" or "https://game.example.com")
     * @param email - Account email
     * @param password - Account password
     * @returns True if connection successful
     */
    connectAsync(serverUrl: string, email: string, password: string): Promise<boolean>;
    /**
     * Connects using an existing JWT token.
     * @param serverUrl - Base URL (e.g., "http://localhost:8080")
     * @param accessToken - Valid JWT access token
     * @param refreshToken - Optional refresh token for re-authentication
     * @returns True if connection successful
     */
    connectWithTokenAsync(serverUrl: string, accessToken: string, refreshToken?: string): Promise<boolean>;
    /**
     * Connects in internal mode using a service token (or network-trust if token is null) without JWT login.
     * @param connectUrl - Full WebSocket URL to the Connect service (internal node)
     * @param serviceToken - Optional X-Service-Token for internal auth mode
     * @returns True if connection successful
     */
    connectInternalAsync(connectUrl: string, serviceToken?: string): Promise<boolean>;
    /**
     * Registers a new account and connects.
     * @param serverUrl - Base URL
     * @param username - Desired username
     * @param email - Email address
     * @param password - Password
     * @returns True if registration and connection successful
     */
    registerAndConnectAsync(serverUrl: string, username: string, email: string, password: string): Promise<boolean>;
    /**
     * Gets the service GUID for a specific API endpoint.
     * @param method - HTTP method (GET, POST, etc.)
     * @param path - API path (e.g., "/account/profile")
     * @returns The client-salted GUID, or undefined if not found
     */
    getServiceGuid(method: string, path: string): string | undefined;
    /**
     * Invokes a service method by specifying the HTTP method and path.
     * @param method - HTTP method (GET, POST, PUT, DELETE)
     * @param path - API path (e.g., "/account/profile")
     * @param request - Request payload
     * @param channel - Message channel for ordering (default 0)
     * @param timeout - Request timeout in milliseconds
     * @returns ApiResponse containing either the success result or error details
     */
    invokeAsync<TRequest, TResponse>(method: string, path: string, request: TRequest, channel?: number, timeout?: number): Promise<ApiResponse<TResponse>>;
    /**
     * Sends a fire-and-forget event (no response expected).
     * @param method - HTTP method
     * @param path - API path
     * @param request - Request payload
     * @param channel - Message channel for ordering
     */
    sendEventAsync<TRequest>(method: string, path: string, request: TRequest, channel?: number): Promise<void>;
    /**
     * Subscribe to a typed event with automatic deserialization.
     * @param eventName - Event name to subscribe to
     * @param handler - Handler to invoke when event is received
     * @returns Subscription handle - call dispose() to unsubscribe
     */
    onEvent<TEvent extends BaseClientEvent>(eventName: string, handler: (event: TEvent) => void): IEventSubscription;
    /**
     * Subscribe to raw event JSON.
     * @param eventName - Event name to subscribe to
     * @param handler - Handler to invoke with raw JSON payload
     * @returns Subscription handle - call dispose() to unsubscribe
     */
    onRawEvent(eventName: string, handler: (json: string) => void): IEventSubscription;
    /**
     * Removes all event handlers for a specific event.
     */
    removeEventHandlers(eventName: string): void;
    /**
     * Disconnects from the server.
     */
    disconnectAsync(): Promise<void>;
}

/**
 * High-level client for connecting to Bannou services via WebSocket.
 * Handles authentication, connection lifecycle, capability discovery, and message correlation.
 */

/**
 * High-level client for connecting to Bannou services via WebSocket.
 */
declare class BannouClient implements IBannouClient {
    private webSocket;
    private connectionState;
    private readonly pendingRequests;
    private readonly apiMappings;
    private readonly eventHandlers;
    private _accessToken;
    private _refreshToken;
    private serverBaseUrl;
    private connectUrl;
    private serviceToken;
    private _sessionId;
    private _lastError;
    private capabilityManifestResolver;
    private eventHandlerFailedCallback;
    /**
     * Current connection state.
     */
    get isConnected(): boolean;
    /**
     * Session ID assigned by the server after connection.
     */
    get sessionId(): string | undefined;
    /**
     * All available API endpoints with their client-salted GUIDs.
     */
    get availableApis(): ReadonlyMap<string, string>;
    /**
     * Current access token (JWT).
     */
    get accessToken(): string | undefined;
    /**
     * Current refresh token for re-authentication.
     */
    get refreshToken(): string | undefined;
    /**
     * Last error message from a failed operation.
     */
    get lastError(): string | undefined;
    /**
     * Set a callback for when event handlers throw exceptions.
     */
    set onEventHandlerFailed(callback: ((error: Error) => void) | null);
    /**
     * Connects to a Bannou server using username/password authentication.
     */
    connectAsync(serverUrl: string, email: string, password: string): Promise<boolean>;
    /**
     * Connects using an existing JWT token.
     */
    connectWithTokenAsync(serverUrl: string, accessToken: string, refreshToken?: string): Promise<boolean>;
    /**
     * Connects in internal mode using a service token.
     */
    connectInternalAsync(connectUrl: string, serviceToken?: string): Promise<boolean>;
    /**
     * Registers a new account and connects.
     */
    registerAndConnectAsync(serverUrl: string, username: string, email: string, password: string): Promise<boolean>;
    /**
     * Gets the service GUID for a specific API endpoint.
     */
    getServiceGuid(method: string, path: string): string | undefined;
    /**
     * Invokes a service method by specifying the HTTP method and path.
     */
    invokeAsync<TRequest, TResponse>(method: string, path: string, request: TRequest, channel?: number, timeout?: number): Promise<ApiResponse<TResponse>>;
    /**
     * Sends a fire-and-forget event (no response expected).
     */
    sendEventAsync<TRequest>(method: string, path: string, request: TRequest, channel?: number): Promise<void>;
    /**
     * Subscribe to a typed event with automatic deserialization.
     */
    onEvent<TEvent extends BaseClientEvent>(eventName: string, handler: (event: TEvent) => void): IEventSubscription;
    /**
     * Subscribe to raw event JSON.
     */
    onRawEvent(eventName: string, handler: (json: string) => void): IEventSubscription;
    /**
     * Removes all event handlers for a specific event.
     */
    removeEventHandlers(eventName: string): void;
    /**
     * Disconnects from the server.
     */
    disconnectAsync(): Promise<void>;
    private buildAuthorizationHeader;
    private loginAsync;
    private registerAsync;
    private establishWebSocketAsync;
    private createWebSocket;
    private setupMessageHandler;
    private handleReceivedMessage;
    private handleEventMessage;
    private handleCapabilityManifest;
    private waitForCapabilityManifest;
    private createResponsePromise;
    private sendBinaryMessage;
}

/**
 * Manages connection state for a BannouClient session.
 * Tracks sequence numbers, pending requests, and service mappings.
 */
declare class ConnectionState {
    /** Session ID assigned by the server */
    readonly sessionId: string;
    /** Per-channel sequence numbers for message ordering */
    private readonly sequenceNumbers;
    /** Service GUID mappings from capability manifest */
    private readonly serviceMappings;
    /** Pending message tracking for debugging */
    private readonly pendingMessages;
    constructor(sessionId: string);
    /**
     * Gets the next sequence number for a channel and increments it.
     */
    getNextSequenceNumber(channel: number): number;
    /**
     * Gets the current sequence number for a channel without incrementing.
     */
    getCurrentSequenceNumber(channel: number): number;
    /**
     * Adds a service mapping from endpoint key to GUID.
     */
    addServiceMapping(endpointKey: string, guid: string): void;
    /**
     * Gets the service GUID for an endpoint key.
     */
    getServiceGuid(endpointKey: string): string | undefined;
    /**
     * Gets all service mappings.
     */
    getServiceMappings(): ReadonlyMap<string, string>;
    /**
     * Clears all service mappings (for capability manifest updates).
     */
    clearServiceMappings(): void;
    /**
     * Tracks a pending message for debugging.
     */
    addPendingMessage(messageId: bigint, endpointKey: string, sentAt: Date): void;
    /**
     * Removes a pending message after response or timeout.
     */
    removePendingMessage(messageId: bigint): void;
    /**
     * Gets pending message info for debugging.
     */
    getPendingMessage(messageId: bigint): PendingMessageInfo | undefined;
    /**
     * Gets count of pending messages.
     */
    getPendingMessageCount(): number;
}
/**
 * Information about a pending message.
 */
interface PendingMessageInfo {
    endpointKey: string;
    sentAt: Date;
}

/**
 * Generates unique message IDs and GUIDs for the Bannou protocol.
 */
/**
 * Generates a unique message ID for request/response correlation.
 * Message IDs are 64-bit unsigned integers.
 */
declare function generateMessageId(): bigint;
/**
 * Generates a random UUID v4.
 * Uses crypto.randomUUID() if available, otherwise falls back to manual generation.
 */
declare function generateUuid(): string;
/**
 * Resets the message ID counter (for testing).
 */
declare function resetMessageIdCounter(): void;

export { BannouClient, ConnectionState, EventSubscription, type IBannouClient, type IEventSubscription, type PendingMessageInfo, generateMessageId, generateUuid, resetMessageIdCounter };
