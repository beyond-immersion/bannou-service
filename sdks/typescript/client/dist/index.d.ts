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
 * Disconnect reason codes.
 */
declare enum DisconnectReason {
    /** Normal client-initiated disconnect */
    ClientDisconnect = "client_disconnect",
    /** Server closed the connection */
    ServerClose = "server_close",
    /** Network error or connection lost */
    NetworkError = "network_error",
    /** Session expired (token invalid) */
    SessionExpired = "session_expired",
    /** Server is shutting down */
    ServerShutdown = "server_shutdown",
    /** Kicked by admin or anti-cheat */
    Kicked = "kicked"
}
/**
 * Information about a disconnect event.
 */
interface DisconnectInfo {
    /** Reason for the disconnect */
    reason: DisconnectReason;
    /** Human-readable message */
    message?: string;
    /** Reconnection token (if server provided one) */
    reconnectionToken?: string;
    /** Whether reconnection is possible */
    canReconnect: boolean;
    /** WebSocket close code (if available) */
    closeCode?: number;
}
/**
 * Meta type for endpoint metadata requests.
 */
declare enum MetaType {
    /** Basic endpoint info (summary, description, tags) */
    EndpointInfo = 0,
    /** JSON Schema for request body */
    RequestSchema = 1,
    /** JSON Schema for response body */
    ResponseSchema = 2,
    /** Full schema (info + request + response) */
    FullSchema = 3
}
/**
 * Endpoint information from meta request.
 */
interface EndpointInfoData {
    summary?: string;
    description?: string;
    tags?: string[];
    deprecated?: boolean;
}
/**
 * JSON Schema data from meta request.
 */
interface JsonSchemaData {
    schema: Record<string, unknown>;
}
/**
 * Full schema data from meta request.
 */
interface FullSchemaData {
    info: EndpointInfoData;
    requestSchema?: Record<string, unknown>;
    responseSchema?: Record<string, unknown>;
}
/**
 * Meta response wrapper.
 */
interface MetaResponse<T> {
    data: T;
    endpointKey: string;
    metaType: MetaType;
}
/**
 * Capability entry from the capability manifest.
 */
interface ClientCapabilityEntry {
    /** Client-salted GUID for binary header routing */
    serviceId: string;
    /** API path (e.g., "/account/get") - used as lookup key */
    endpoint: string;
    /** Service name (e.g., "account") */
    service: string;
    /** Human-readable description */
    description?: string;
}
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
     * Key format: "/path" (e.g., "/species/get")
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
     * Connects in internal mode using a service token (or network-trust if token
     * is null) without JWT login.
     * @param connectUrl - Full WebSocket URL to the Connect service (internal)
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
     * @param endpoint - API path (e.g., "/account/get")
     * @returns The client-salted GUID, or undefined if not found
     */
    getServiceGuid(endpoint: string): string | undefined;
    /**
     * Invokes a service method by specifying the API endpoint path.
     * @param endpoint - API path (e.g., "/account/get")
     * @param request - Request payload
     * @param channel - Message channel for ordering (default 0)
     * @param timeout - Request timeout in milliseconds
     * @returns ApiResponse containing either the success result or error details
     */
    invokeAsync<TRequest, TResponse>(endpoint: string, request: TRequest, channel?: number, timeout?: number): Promise<ApiResponse<TResponse>>;
    /**
     * Sends a fire-and-forget event (no response expected).
     * @param endpoint - API path (e.g., "/events/publish")
     * @param request - Request payload
     * @param channel - Message channel for ordering
     */
    sendEventAsync<TRequest>(endpoint: string, request: TRequest, channel?: number): Promise<void>;
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
    /**
     * Set callback for when the connection is closed.
     */
    set onDisconnect(callback: ((info: DisconnectInfo) => void) | null);
    /**
     * Set callback for when a connection error occurs.
     */
    set onError(callback: ((error: Error) => void) | null);
    /**
     * Set callback for when event handlers throw exceptions.
     */
    set onEventHandlerFailed(callback: ((error: Error) => void) | null);
    /**
     * Set callback for when new capabilities are added to the session.
     * Fires once per capability manifest update with all newly added capabilities.
     */
    set onCapabilitiesAdded(callback: ((capabilities: ClientCapabilityEntry[]) => void) | null);
    /**
     * Set callback for when capabilities are removed from the session.
     * Fires once per capability manifest update with all removed capabilities.
     */
    set onCapabilitiesRemoved(callback: ((capabilities: ClientCapabilityEntry[]) => void) | null);
    /**
     * Requests metadata about an endpoint instead of executing it.
     * @param endpoint - API path (e.g., "/account/get")
     * @param metaType - Type of metadata to request
     * @param timeout - Request timeout in milliseconds
     */
    getEndpointMetaAsync<T>(endpoint: string, metaType?: MetaType, timeout?: number): Promise<MetaResponse<T>>;
    /**
     * Gets human-readable endpoint information (summary, description, tags).
     * @param endpoint - API path (e.g., "/account/get")
     */
    getEndpointInfoAsync(endpoint: string): Promise<MetaResponse<EndpointInfoData>>;
    /**
     * Gets JSON Schema for the request body of an endpoint.
     * @param endpoint - API path (e.g., "/account/get")
     */
    getRequestSchemaAsync(endpoint: string): Promise<MetaResponse<JsonSchemaData>>;
    /**
     * Gets JSON Schema for the response body of an endpoint.
     * @param endpoint - API path (e.g., "/account/get")
     */
    getResponseSchemaAsync(endpoint: string): Promise<MetaResponse<JsonSchemaData>>;
    /**
     * Gets full schema including info, request schema, and response schema.
     * @param endpoint - API path (e.g., "/account/get")
     */
    getFullSchemaAsync(endpoint: string): Promise<MetaResponse<FullSchemaData>>;
    /**
     * Reconnects using a reconnection token from a DisconnectNotificationEvent.
     * @param reconnectionToken - Token provided by the server
     */
    reconnectWithTokenAsync(reconnectionToken: string): Promise<boolean>;
    /**
     * Refreshes the access token using the stored refresh token.
     * Requires a valid refresh token from previous login/registration.
     * @returns True if refresh successful, false otherwise
     */
    refreshAccessTokenAsync(): Promise<boolean>;
    /**
     * Set callback for when tokens are refreshed.
     * Useful for persisting updated tokens.
     */
    set onTokenRefreshed(callback: ((accessToken: string, refreshToken: string | undefined) => void) | null);
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
    private readonly previousCapabilities;
    private _accessToken;
    private _refreshToken;
    private serverBaseUrl;
    private connectUrl;
    private serviceToken;
    private _sessionId;
    private _lastError;
    private capabilityManifestResolver;
    private eventHandlerFailedCallback;
    private disconnectCallback;
    private errorCallback;
    private tokenRefreshedCallback;
    private capabilitiesAddedCallback;
    private capabilitiesRemovedCallback;
    private pendingReconnectionToken;
    private lastDisconnectInfo;
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
     * Set a callback for when the connection is closed.
     */
    set onDisconnect(callback: ((info: DisconnectInfo) => void) | null);
    /**
     * Set a callback for when a connection error occurs.
     */
    set onError(callback: ((error: Error) => void) | null);
    /**
     * Set a callback for when tokens are refreshed.
     */
    set onTokenRefreshed(callback: ((accessToken: string, refreshToken: string | undefined) => void) | null);
    /**
     * Set a callback for when new capabilities are added to the session.
     * Fires once per capability manifest update with all newly added capabilities.
     */
    set onCapabilitiesAdded(callback: ((capabilities: ClientCapabilityEntry[]) => void) | null);
    /**
     * Set a callback for when capabilities are removed from the session.
     * Fires once per capability manifest update with all removed capabilities.
     */
    set onCapabilitiesRemoved(callback: ((capabilities: ClientCapabilityEntry[]) => void) | null);
    /**
     * Get the last disconnect information (if any).
     */
    get lastDisconnect(): DisconnectInfo | undefined;
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
     * @param endpoint API path (e.g., "/account/get")
     */
    getServiceGuid(endpoint: string): string | undefined;
    /**
     * Invokes a service method by specifying the API endpoint path.
     * @param endpoint API path (e.g., "/account/get")
     */
    invokeAsync<TRequest, TResponse>(endpoint: string, request: TRequest, channel?: number, timeout?: number): Promise<ApiResponse<TResponse>>;
    /**
     * Sends a fire-and-forget event (no response expected).
     * @param endpoint API path (e.g., "/events/publish")
     */
    sendEventAsync<TRequest>(endpoint: string, request: TRequest, channel?: number): Promise<void>;
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
     * Requests metadata about an endpoint instead of executing it.
     * Uses the Meta flag (0x80) which triggers route transformation at Connect service.
     * @param endpoint API path (e.g., "/account/get")
     */
    getEndpointMetaAsync<T>(endpoint: string, metaType?: MetaType, timeout?: number): Promise<MetaResponse<T>>;
    /**
     * Gets human-readable endpoint information (summary, description, tags).
     * @param endpoint API path (e.g., "/account/get")
     */
    getEndpointInfoAsync(endpoint: string): Promise<MetaResponse<EndpointInfoData>>;
    /**
     * Gets JSON Schema for the request body of an endpoint.
     * @param endpoint API path (e.g., "/account/get")
     */
    getRequestSchemaAsync(endpoint: string): Promise<MetaResponse<JsonSchemaData>>;
    /**
     * Gets JSON Schema for the response body of an endpoint.
     * @param endpoint API path (e.g., "/account/get")
     */
    getResponseSchemaAsync(endpoint: string): Promise<MetaResponse<JsonSchemaData>>;
    /**
     * Gets full schema including info, request schema, and response schema.
     * @param endpoint API path (e.g., "/account/get")
     */
    getFullSchemaAsync(endpoint: string): Promise<MetaResponse<FullSchemaData>>;
    /**
     * Reconnects using a reconnection token from a DisconnectNotificationEvent.
     */
    reconnectWithTokenAsync(reconnectionToken: string): Promise<boolean>;
    /**
     * Refreshes the access token using the stored refresh token.
     */
    refreshAccessTokenAsync(): Promise<boolean>;
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
    private handleDisconnectNotification;
    private waitForCapabilityManifest;
    private createResponsePromise;
    private sendBinaryMessage;
}

interface components {
    schemas: {
        /** @description Request to accept a formed match */
        AcceptMatchRequest: {
            /**
             * Format: uuid
             * @description WebSocket session ID
             */
            webSocketSessionId: string;
            /**
             * Format: uuid
             * @description Account ID of the player
             */
            accountId: string;
            /**
             * Format: uuid
             * @description ID of the match to accept
             */
            matchId: string;
        };
        /** @description Response after accepting a match */
        AcceptMatchResponse: {
            /**
             * Format: uuid
             * @description Match identifier
             */
            matchId: string;
            /** @description Whether all players have accepted */
            allAccepted: boolean;
            /** @description Number of players who have accepted */
            acceptedCount?: number | null;
            /** @description Total players who need to accept */
            totalCount?: number | null;
            /**
             * Format: uuid
             * @description Game session ID (set when all players accept)
             */
            gameSessionId?: string | null;
        };
        /** @description User account information displayed on the website profile page */
        AccountProfile: {
            /**
             * Format: uuid
             * @description Unique identifier for the account
             */
            accountId: string;
            /**
             * Format: email
             * @description Email address associated with the account
             */
            email: string;
            /** @description User-chosen display name */
            displayName?: string | null;
            /**
             * Format: date-time
             * @description Date and time when the account was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Date and time of the last successful login
             */
            lastLogin?: string | null;
            /** @description Total number of character slots available */
            characterSlots?: number;
            /** @description Number of character slots currently in use */
            usedSlots?: number;
        };
        /** @description Account information response */
        AccountResponse: {
            /**
             * Format: uuid
             * @description Unique identifier for the account
             */
            accountId: string;
            /**
             * Format: email
             * @description Email address associated with the account
             */
            email: string;
            /** @description Display name for the account */
            displayName?: string | null;
            /** @description BCrypt hashed password for authentication */
            passwordHash?: string | null;
            /**
             * Format: date-time
             * @description Timestamp when the account was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Timestamp when the account was last updated
             */
            updatedAt?: string | null;
            /** @description Whether the email address has been verified */
            emailVerified: boolean;
            /** @description List of roles assigned to the account */
            roles: string[];
            /** @description List of authentication methods linked to the account */
            authMethods?: components["schemas"]["AuthMethodInfo"][];
            /** @description Custom metadata associated with the account */
            metadata?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Achievement definition details */
        AchievementDefinitionResponse: {
            /**
             * Format: uuid
             * @description ID of the owning game service
             */
            gameServiceId: string;
            /** @description Unique identifier */
            achievementId: string;
            /** @description Human-readable name */
            displayName: string;
            /** @description How to earn this achievement */
            description: string;
            /** @description Description for hidden achievements */
            hiddenDescription?: string | null;
            /** @description Classification of the achievement */
            achievementType: components["schemas"]["AchievementType"];
            /** @description Allowed entity types */
            entityTypes?: components["schemas"]["EntityType"][];
            /** @description Target for progressive achievements */
            progressTarget?: number | null;
            /** @description Point value */
            points: number;
            /** @description Achievement icon URL */
            iconUrl?: string | null;
            /** @description Available platforms */
            platforms?: components["schemas"]["Platform"][];
            /** @description Platform-specific IDs */
            platformIds?: {
                [key: string]: string;
            } | null;
            /** @description Required achievements */
            prerequisites?: string[] | null;
            /** @description Whether achievement is earnable */
            isActive: boolean;
            /**
             * Format: int64
             * @description How many entities have earned this
             */
            earnedCount?: number;
            /**
             * Format: date-time
             * @description When the achievement was created
             */
            createdAt: string;
            /** @description Additional metadata */
            metadata?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Progress toward a single achievement */
        AchievementProgress: {
            /** @description Achievement identifier */
            achievementId: string;
            /** @description Achievement display name */
            displayName?: string;
            /** @description Current progress (for progressive) */
            currentProgress?: number | null;
            /** @description Target progress (for progressive) */
            targetProgress?: number | null;
            /**
             * Format: double
             * @description Completion percentage (0-100)
             */
            percentComplete?: number | null;
            /** @description Whether achievement is unlocked */
            isUnlocked: boolean;
            /**
             * Format: date-time
             * @description When achievement was unlocked
             */
            unlockedAt?: string | null;
        };
        /** @description Achievement progress for an entity */
        AchievementProgressResponse: {
            /**
             * Format: uuid
             * @description ID of the entity
             */
            entityId: string;
            /** @description Entity type that owns this progress summary */
            entityType: components["schemas"]["EntityType"];
            /** @description Progress for each achievement */
            progress: components["schemas"]["AchievementProgress"][];
            /** @description Total points from unlocked achievements */
            totalPoints?: number;
            /** @description Number of unlocked achievements */
            unlockedCount?: number;
        };
        /**
         * @description Type of achievement
         * @enum {string}
         */
        AchievementType: "standard" | "progressive" | "hidden" | "secret";
        /**
         * @description Actor-specific capabilities that affect affordance evaluation.
         *     Same location may afford different actions to different actor types.
         */
        ActorCapabilities: {
            /** @description Affects cover requirements and passage width */
            size?: components["schemas"]["ActorSize"];
            /** @description Actor height in meters (affects cover, sightlines) */
            height?: number | null;
            /**
             * @description Can reach elevated positions
             * @default false
             */
            canClimb: boolean;
            /**
             * @description Includes water-based positions
             * @default false
             */
            canSwim: boolean;
            /**
             * @description Includes aerial positions
             * @default false
             */
            canFly: boolean;
            /** @description Affects sightline distance requirements */
            perceptionRange?: number | null;
            /** @description Affects escape route viability calculations */
            movementSpeed?: number | null;
            /** @description Affects ambush/hidden_path affordance scoring */
            stealthRating?: number | null;
        };
        /** @description Response containing actor instance details */
        ActorInstanceResponse: {
            /** @description Unique actor identifier */
            actorId: string;
            /**
             * Format: uuid
             * @description Template this actor was instantiated from
             */
            templateId: string;
            /** @description Actor category from template */
            category: string;
            /** @description Pool node running this actor (null in bannou mode) */
            nodeId?: string | null;
            /** @description Pool node's app-id for direct messaging */
            nodeAppId?: string | null;
            /** @description Current actor lifecycle state */
            status: components["schemas"]["ActorStatus"];
            /**
             * Format: uuid
             * @description Associated character ID (for NPC brains)
             */
            characterId?: string | null;
            /**
             * Format: date-time
             * @description When the actor started running
             */
            startedAt: string;
            /**
             * Format: date-time
             * @description Last heartbeat timestamp from the actor
             */
            lastHeartbeat?: string | null;
            /**
             * Format: int64
             * @description Number of behavior loop iterations executed
             */
            loopIterations: number;
        };
        /**
         * @description Size classification affecting cover requirements and passage width
         * @default medium
         * @enum {string}
         */
        ActorSize: "tiny" | "small" | "medium" | "large" | "huge";
        /**
         * @description Current actor lifecycle state
         * @enum {string}
         */
        ActorStatus: "pending" | "starting" | "running" | "paused" | "stopping" | "stopped" | "error";
        /** @description Response containing actor template details */
        ActorTemplateResponse: {
            /**
             * Format: uuid
             * @description Unique template identifier
             */
            templateId: string;
            /** @description Category identifier */
            category: string;
            /** @description Reference to behavior in lib-assets */
            behaviorRef: string;
            /** @description Default configuration passed to behavior execution */
            configuration?: {
                [key: string]: unknown;
            } | null;
            /** @description Auto-spawn configuration for instantiate-on-access */
            autoSpawn?: components["schemas"]["AutoSpawnConfig"];
            /** @description Milliseconds between behavior loop iterations */
            tickIntervalMs: number;
            /** @description Seconds between automatic state saves */
            autoSaveIntervalSeconds: number;
            /** @description Maximum actors of this category per pool node */
            maxInstancesPerNode: number;
            /**
             * Format: date-time
             * @description When the template was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description When the template was last updated
             */
            updatedAt: string;
        };
        /** @description Request to add item to container */
        AddItemRequest: {
            /**
             * Format: uuid
             * @description Item instance ID to add
             */
            instanceId: string;
            /**
             * Format: uuid
             * @description Target container ID
             */
            containerId: string;
            /** @description Specific slot (auto-assign if null) */
            slotIndex?: number | null;
            /** @description Grid X position */
            slotX?: number | null;
            /** @description Grid Y position */
            slotY?: number | null;
            /** @description Rotate in grid */
            rotated?: boolean | null;
            /**
             * @description Auto-merge with existing stacks
             * @default true
             */
            autoStack: boolean;
        };
        /** @description Response after adding item */
        AddItemResponse: {
            /** @description Whether add succeeded */
            success: boolean;
            /**
             * Format: uuid
             * @description Added item ID
             */
            instanceId: string;
            /**
             * Format: uuid
             * @description Container ID
             */
            containerId: string;
            /** @description Assigned slot */
            slotIndex?: number | null;
            /** @description Assigned X position */
            slotX?: number | null;
            /** @description Assigned Y position */
            slotY?: number | null;
            /**
             * Format: uuid
             * @description Instance merged into if stacked
             */
            mergedWithInstanceId?: string | null;
        };
        /**
         * @description Describes a capability or interaction mode for a node.
         *     Used by AI systems to understand what actions are possible and by
         *     character controllers for contextual animations.
         */
        Affordance: {
            /** @description The type of affordance */
            type: components["schemas"]["AffordanceType"];
            /**
             * @description Type-specific parameters. Examples:
             *     - sittable: { height: 0.5, facing: [0,0,1] }
             *     - door: { openAngle: 90, locked: false }
             *     - container: { capacity: 10, itemTypes: ["weapon", "consumable"] }
             */
            parameters?: {
                [key: string]: unknown;
            } | null;
        };
        /**
         * @description Controls caching behavior for affordance queries
         * @default cached
         * @enum {string}
         */
        AffordanceFreshness: "fresh" | "cached" | "aggressive_cache";
        /** @description A location that affords the requested action */
        AffordanceLocation: {
            /** @description Location position */
            position?: components["schemas"]["Position3D"];
            /** @description Area bounds if affordance spans an area */
            bounds?: components["schemas"]["Bounds"];
            /** @description How well this location affords the action (0-1) */
            score?: number;
            /**
             * @description What makes this location suitable.
             *     Example: { "cover_rating": 0.8, "sightlines": ["north"], "terrain": "rocky" }
             */
            features?: {
                [key: string]: unknown;
            } | null;
            /** @description Map objects contributing to this affordance */
            objectIds?: string[] | null;
        };
        /** @description Metadata about the affordance query execution */
        AffordanceQueryMetadata: {
            /** @description Map kinds that were queried */
            kindsSearched?: string[] | null;
            /** @description Number of candidate objects evaluated */
            objectsEvaluated?: number;
            /** @description Number of candidate positions generated */
            candidatesGenerated?: number;
            /** @description Query execution time in milliseconds */
            searchDurationMs?: number;
            /** @description Whether results came from cache */
            cacheHit?: boolean;
        };
        /** @description Query for locations that afford a specific action */
        AffordanceQueryRequest: {
            /**
             * Format: uuid
             * @description Region to search
             */
            regionId: string;
            /** @description Type of affordance to search for */
            affordanceType: components["schemas"]["AffordanceType"];
            /** @description Custom affordance definition (when affordanceType=custom) */
            customAffordance?: components["schemas"]["CustomAffordance"];
            /** @description Optional bounds to search within */
            bounds?: components["schemas"]["Bounds"];
            /**
             * @description Maximum locations to return
             * @default 10
             */
            maxResults: number;
            /**
             * @description Minimum affordance score to include
             * @default 0.5
             */
            minScore: number;
            /** @description Expected participants (affects space requirements) */
            participantCount?: number | null;
            /** @description Positions to exclude (e.g., player's current location) */
            excludePositions?: components["schemas"]["Position3D"][] | null;
            /** @description Actor capabilities affecting evaluation */
            actorCapabilities?: components["schemas"]["ActorCapabilities"];
            /** @description Cache freshness level */
            freshness?: components["schemas"]["AffordanceFreshness"];
            /** @description Max age of cached results (for cached/aggressive_cache) */
            maxAgeSeconds?: number | null;
        };
        /** @description Affordance query results */
        AffordanceQueryResponse: {
            /** @description Scored locations (highest score first) */
            locations?: components["schemas"]["AffordanceLocation"][];
            /** @description Metadata about query execution (optional) */
            queryMetadata?: components["schemas"]["AffordanceQueryMetadata"];
        };
        /**
         * @description Well-known affordance types with predefined scoring logic.
         *     Use 'custom' for novel affordance definitions.
         * @enum {string}
         */
        AffordanceType: "ambush" | "shelter" | "vista" | "choke_point" | "gathering_spot" | "dramatic_reveal" | "hidden_path" | "defensible_position" | "custom";
        /** @description Analytics and tracking configuration for website visitor metrics */
        Analytics: {
            /** @description Google Analytics tracking ID */
            googleAnalyticsId?: string | null;
            /** @description Configuration for other analytics trackers */
            otherTrackers?: {
                [key: string]: unknown;
            };
        };
        /**
         * @description Request to send an SDP answer to complete a WebRTC handshake.
         *     Sent by clients after receiving a VoicePeerJoinedEvent with an SDP offer.
         */
        AnswerPeerRequest: {
            /**
             * Format: uuid
             * @description Voice room ID
             */
            roomId: string;
            /**
             * Format: uuid
             * @description Session ID of the answering peer (caller of this endpoint)
             */
            senderSessionId: string;
            /**
             * Format: uuid
             * @description Session ID of the peer whose offer we're answering
             */
            targetSessionId: string;
            /** @description SDP answer generated by this client's WebRTC stack */
            sdpAnswer: string;
            /** @description ICE candidates for NAT traversal (can be trickled later) */
            iceCandidates?: string[];
        };
        /** @description Archive metadata including size and document count */
        ArchiveInfo: {
            /**
             * Format: uuid
             * @description Unique identifier of the archive
             */
            archiveId: string;
            /** @description Namespace the archive belongs to */
            namespace: string;
            /**
             * Format: uuid
             * @description Asset ID in Asset Service
             */
            bundleAssetId?: string;
            /** @description Description of the archive */
            description?: string | null;
            /** @description Number of documents in the archive */
            documentCount?: number;
            /** @description Total size of the archive in bytes */
            sizeBytes?: number;
            /** @description Git commit hash if namespace was bound at archive time */
            commitHash?: string | null;
            /**
             * Format: date-time
             * @description Timestamp when the archive was created
             */
            createdAt: string;
            /**
             * @description Owner of this archive. NOT a session ID.
             *     Contains either an accountId (UUID format) for user-initiated archives
             *     or a service name for service-initiated archives.
             */
            owner?: string;
        };
        /** @description Describes a conflict when the same asset ID has different content hashes */
        AssetConflict: {
            /** @description The conflicting asset identifier */
            assetId: string;
            /** @description Bundles with conflicting versions of this asset */
            conflictingBundles: components["schemas"]["ConflictingBundleEntry"][];
        };
        /** @description Complete asset metadata including system-generated fields */
        AssetMetadata: {
            /** @description Unique asset identifier */
            assetId: string;
            /** @description SHA256 hash of file contents */
            contentHash: string;
            /** @description Original filename */
            filename: string;
            /** @description MIME content type */
            contentType: string;
            /**
             * Format: int64
             * @description File size in bytes
             */
            size: number;
            /** @description Type classification for the asset */
            assetType: components["schemas"]["AssetType"];
            /** @description Game realm the asset belongs to */
            realm: components["schemas"]["GameRealm"];
            /** @description Searchable tags for the asset */
            tags: string[];
            /** @description Current status of asset processing pipeline */
            processingStatus: components["schemas"]["ProcessingStatus"];
            /**
             * @description Whether the asset is in cold/archival storage
             * @default false
             */
            isArchived: boolean;
            /**
             * Format: date-time
             * @description Timestamp when the asset was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Timestamp when the asset was last updated
             */
            updatedAt: string;
        };
        /** @description User-provided metadata for asset categorization */
        AssetMetadataInput: {
            /** @description Type classification for the asset */
            assetType?: components["schemas"]["AssetType"];
            /** @description Game realm the asset belongs to */
            realm?: components["schemas"]["GameRealm"];
            /** @description Searchable tags for the asset */
            tags?: string[];
        };
        /** @description Reference to an asset in lib-asset */
        AssetReference: {
            /**
             * Format: uuid
             * @description Optional bundle containing the asset
             */
            bundleId?: string | null;
            /**
             * Format: uuid
             * @description Asset identifier in lib-asset
             */
            assetId: string;
            /** @description Variant identifier (consumer interprets meaning) */
            variantId?: string | null;
        };
        /** @description Information about a required asset */
        AssetRequirementInfo: {
            /** @description Asset type (currency, item, item_stack) */
            type: string;
            /** @description Currency code or item template code */
            code: string;
            /** @description Amount or quantity required */
            amount: number;
        };
        /** @description Search criteria for filtering assets with pagination */
        AssetSearchRequest: {
            /** @description Filter by tags (assets must have all specified tags) (null to skip tag filtering) */
            tags?: string[] | null;
            /** @description Filter by asset type */
            assetType: components["schemas"]["AssetType"];
            /** @description Filter by game realm */
            realm: components["schemas"]["GameRealm"];
            /** @description MIME content type filter (null to skip content type filtering) */
            contentType?: string | null;
            /**
             * @description Maximum number of results to return
             * @default 50
             */
            limit: number;
            /**
             * @description Number of results to skip for pagination
             * @default 0
             */
            offset: number;
        };
        /** @description Paginated results from an asset search query */
        AssetSearchResult: {
            /** @description List of matching assets */
            assets: components["schemas"]["AssetMetadata"][];
            /** @description Total number of matching assets */
            total: number;
            /** @description Maximum number of results returned per page */
            limit: number;
            /** @description Number of results skipped */
            offset: number;
        };
        /**
         * @description Defines acceptable asset types for procedural swapping at this node.
         *     Used by procedural generation systems to substitute assets while
         *     maintaining scene coherence.
         */
        AssetSlot: {
            /**
             * @description Category of acceptable assets.
             *     Examples: chair, table, wall_art, floor_lamp
             */
            slotType: string;
            /**
             * @description Tags that acceptable assets must have.
             *     Used for filtering when selecting random variations.
             */
            acceptsTags?: string[];
            /** @description Default asset if no specific asset is bound */
            defaultAsset?: components["schemas"]["AssetReference"];
            /**
             * @description Pre-approved asset variations for random selection.
             *     Procedural systems pick from this list rather than searching all assets.
             */
            variations?: components["schemas"]["AssetReference"][];
        };
        /**
         * @description Type classification for assets
         * @enum {string}
         */
        AssetType: "texture" | "model" | "audio" | "behavior" | "bundle" | "prefab" | "other";
        /** @description Information about asset usage */
        AssetUsageInfo: {
            /**
             * Format: uuid
             * @description Scene using the asset
             */
            sceneId: string;
            /** @description Scene name */
            sceneName: string;
            /**
             * Format: uuid
             * @description Node using the asset
             */
            nodeId: string;
            /** @description refId of the node */
            nodeRefId: string;
            /** @description Node name */
            nodeName?: string;
            /** @description Type of the node */
            nodeType?: components["schemas"]["NodeType"];
        };
        /** @description Metadata for a specific version of an asset */
        AssetVersion: {
            /** @description Unique version identifier */
            versionId: string;
            /**
             * Format: date-time
             * @description Timestamp when this version was created
             */
            createdAt: string;
            /**
             * Format: int64
             * @description File size in bytes for this version
             */
            size: number;
            /** @description Whether this version is in cold storage */
            isArchived: boolean;
        };
        /** @description Paginated list of asset versions */
        AssetVersionList: {
            /** @description Asset identifier */
            assetId: string;
            /** @description List of asset versions */
            versions: components["schemas"]["AssetVersion"][];
            /** @description Total number of versions available */
            total: number;
            /** @description Maximum number of versions returned per page */
            limit: number;
            /** @description Number of versions skipped */
            offset: number;
        };
        /** @description Asset metadata with optional pre-signed download URL */
        AssetWithDownloadUrl: {
            /** @description Unique asset identifier */
            assetId: string;
            /** @description Version identifier for this specific asset version */
            versionId: string;
            /**
             * Format: uri
             * @description Pre-signed download URL (only populated when requested)
             */
            downloadUrl?: string | null;
            /**
             * Format: date-time
             * @description When the download URL expires (only populated when requested)
             */
            expiresAt?: string | null;
            /**
             * Format: int64
             * @description File size in bytes
             */
            size: number;
            /** @description SHA256 hash of file contents */
            contentHash: string;
            /** @description MIME content type */
            contentType: string;
            /** @description Complete asset metadata */
            metadata: components["schemas"]["AssetMetadata"];
        };
        /**
         * @description A predefined location where child objects can be attached.
         *     Used for decorating furniture, walls, and other objects with accessories.
         *     Example: A wall may have attachment points for paintings, shelves, or light fixtures.
         */
        AttachmentPoint: {
            /**
             * @description Unique name for this attachment point within the node.
             *     Examples: wall_hook_left, shelf_1, lamp_socket
             */
            name: string;
            /** @description Position and orientation relative to the owning node */
            localTransform: components["schemas"]["Transform"];
            /**
             * @description Tags of assets that can attach here.
             *     Examples: wall_decoration, picture_frame, plant
             */
            acceptsTags?: string[];
            /** @description Default asset to display if no specific attachment is specified */
            defaultAsset?: components["schemas"]["AssetReference"];
            /**
             * Format: uuid
             * @description ID of the node currently attached at this point (runtime state)
             */
            attachedNodeId?: string | null;
        };
        /** @description Information about a linked authentication method */
        AuthMethodInfo: {
            /**
             * Format: uuid
             * @description Unique identifier for the authentication method
             */
            methodId?: string | null;
            /** @description Authentication provider type */
            provider: components["schemas"]["AuthProvider"];
            /** @description External user ID from the authentication provider */
            externalId?: string | null;
            /** @description Display name from the authentication provider */
            displayName?: string | null;
            /**
             * Format: date-time
             * @description Timestamp when the authentication method was linked
             */
            linkedAt: string;
        };
        /**
         * @description All authentication provider types including email
         * @enum {string}
         */
        AuthProvider: "email" | "google" | "discord" | "twitch" | "steam";
        /** @description Successful authentication response containing tokens and session information */
        AuthResponse: {
            /**
             * Format: uuid
             * @description Unique identifier for the authenticated account
             */
            accountId: string;
            /** @description JWT access token for API authentication */
            accessToken: string;
            /** @description Token used to obtain new access tokens when the current one expires */
            refreshToken: string;
            /** @description Seconds until access token expires */
            expiresIn: number;
            /**
             * Format: uri
             * @description WebSocket endpoint for Connect service
             */
            connectUrl: string;
            /** @description List of roles assigned to the authenticated user */
            roles?: string[] | null;
            /**
             * @description Whether the user needs to complete two-factor authentication
             * @default false
             */
            requiresTwoFactor: boolean;
        };
        /** @description Request to checkout for authoring */
        AuthoringCheckoutRequest: {
            /**
             * Format: uuid
             * @description Region to checkout
             */
            regionId: string;
            /** @description Map kind to checkout */
            kind: components["schemas"]["MapKind"];
            /** @description Identifier for the editor/user */
            editorId: string;
        };
        /** @description Checkout response */
        AuthoringCheckoutResponse: {
            /** @description Token for publishing changes (if successful) */
            authorityToken?: string | null;
            /**
             * Format: date-time
             * @description When the checkout expires
             */
            expiresAt?: string | null;
            /** @description Who has the lock (if checkout failed) */
            lockedBy?: string | null;
            /**
             * Format: date-time
             * @description When the lock was acquired (if checkout failed)
             */
            lockedAt?: string | null;
        };
        /** @description Request to commit authoring changes */
        AuthoringCommitRequest: {
            /**
             * Format: uuid
             * @description Region being edited
             */
            regionId: string;
            /** @description Map kind being edited */
            kind: components["schemas"]["MapKind"];
            /** @description Checkout authority token */
            authorityToken: string;
            /** @description Optional commit message for history */
            commitMessage?: string | null;
        };
        /** @description Commit response */
        AuthoringCommitResponse: {
            /**
             * Format: int64
             * @description Committed version number
             */
            version?: number | null;
        };
        /** @description Request to release authoring checkout */
        AuthoringReleaseRequest: {
            /**
             * Format: uuid
             * @description Region being edited
             */
            regionId: string;
            /** @description Map kind being edited */
            kind: components["schemas"]["MapKind"];
            /** @description Checkout authority token */
            authorityToken: string;
        };
        /** @description Release response */
        AuthoringReleaseResponse: {
            /** @description Whether checkout was released */
            released?: boolean;
        };
        /** @description Configuration for instantiate-on-access behavior */
        AutoSpawnConfig: {
            /**
             * @description If true, accessing a non-existent actor creates it
             * @default false
             */
            enabled: boolean;
            /**
             * @description Regex pattern for actor IDs that trigger auto-spawn.
             *     Examples: "npc-.*" matches "npc-grok", "npc-merchant-123"
             */
            idPattern?: string | null;
            /** @description Maximum auto-spawned instances (0 = unlimited) */
            maxInstances?: number | null;
            /**
             * @description 1-based regex capture group index for extracting CharacterId from actor ID.
             *     Example: With idPattern "npc-brain-([a-f0-9-]+)" and characterIdCaptureGroup: 1,
             *     actor ID "npc-brain-abc-123-def" extracts "abc-123-def" as CharacterId (parsed as GUID).
             */
            characterIdCaptureGroup?: number | null;
        };
        /** @description Autogain status for a balance */
        AutogainInfo: {
            /**
             * Format: date-time
             * @description When autogain was last calculated
             */
            lastCalculatedAt: string;
            /**
             * Format: date-time
             * @description When the next autogain will apply
             */
            nextGainAt: string;
            /**
             * Format: double
             * @description Estimated next gain amount
             */
            nextGainAmount: number;
            /** @description Current autogain mode */
            mode: components["schemas"]["AutogainMode"];
        };
        /**
         * @description How autogain (energy/interest) is calculated
         * @enum {string}
         */
        AutogainMode: "simple" | "compound";
        /** @description A machine-readable backstory element for behavior system consumption */
        BackstoryElement: {
            /** @description Category of this backstory element */
            elementType: components["schemas"]["BackstoryElementType"];
            /**
             * @description Machine-readable key (e.g., "homeland", "trained_by", "past_job").
             *     Used by behavior system to query specific aspects.
             */
            key: string;
            /**
             * @description Machine-readable value (e.g., "northlands", "knights_guild", "blacksmith").
             *     Referenced in behavior rules.
             */
            value: string;
            /**
             * Format: float
             * @description How strongly this element affects behavior (0.0 to 1.0).
             *     Higher strength = greater influence on decisions.
             * @default 0.5
             */
            strength: number;
            /**
             * Format: uuid
             * @description Optional related entity (location, organization, character)
             */
            relatedEntityId?: string | null;
            /** @description Type of the related entity (if any) */
            relatedEntityType?: string | null;
        };
        /** @description Single backstory element */
        BackstoryElementSnapshot: {
            /** @description Type of backstory element (ORIGIN, TRAUMA, GOAL, etc.) */
            elementType: string;
            /** @description Machine-readable key (homeland, past_job, etc.) */
            key: string;
            /** @description Machine-readable value (northlands, blacksmith, etc.) */
            value: string;
            /**
             * Format: float
             * @description How strongly this affects behavior (0.0 to 1.0)
             */
            strength: number;
        };
        /**
         * @description Types of backstory elements. Each type represents a different aspect
         *     of the character's background that influences behavior.
         * @enum {string}
         */
        BackstoryElementType: "ORIGIN" | "OCCUPATION" | "TRAINING" | "TRAUMA" | "ACHIEVEMENT" | "SECRET" | "GOAL" | "FEAR" | "BELIEF";
        /** @description Complete backstory data for a character */
        BackstoryResponse: {
            /**
             * Format: uuid
             * @description ID of the character this backstory belongs to
             */
            characterId: string;
            /** @description All backstory elements for this character */
            elements: components["schemas"]["BackstoryElement"][];
            /**
             * Format: date-time
             * @description When this backstory was first created
             */
            createdAt?: string | null;
            /**
             * Format: date-time
             * @description When this backstory was last modified
             */
            updatedAt?: string | null;
        };
        /** @description Snapshot of backstory for enriched response */
        BackstorySnapshot: {
            /** @description List of backstory elements */
            elements: components["schemas"]["BackstoryElementSnapshot"][];
        };
        /** @description A single balance query */
        BalanceQuery: {
            /**
             * Format: uuid
             * @description Wallet ID
             */
            walletId: string;
            /**
             * Format: uuid
             * @description Currency definition ID
             */
            currencyDefinitionId: string;
        };
        /** @description Summary of a balance in a wallet */
        BalanceSummary: {
            /**
             * Format: uuid
             * @description Currency definition ID
             */
            currencyDefinitionId: string;
            /** @description Currency code for convenience */
            currencyCode: string;
            /**
             * Format: double
             * @description Total balance amount
             */
            amount: number;
            /**
             * Format: double
             * @description Amount reserved by authorization holds
             */
            lockedAmount: number;
            /**
             * Format: double
             * @description Available balance (amount - lockedAmount)
             */
            effectiveAmount: number;
        };
        /** @description Result of a single balance query in a batch */
        BatchBalanceResult: {
            /**
             * Format: uuid
             * @description Wallet ID
             */
            walletId: string;
            /**
             * Format: uuid
             * @description Currency definition ID
             */
            currencyDefinitionId: string;
            /**
             * Format: double
             * @description Total balance
             */
            amount: number;
            /**
             * Format: double
             * @description Amount in holds
             */
            lockedAmount: number;
            /**
             * Format: double
             * @description Available balance
             */
            effectiveAmount: number;
        };
        /** @description A single credit operation in a batch */
        BatchCreditOperation: {
            /**
             * Format: uuid
             * @description Target wallet ID
             */
            walletId: string;
            /**
             * Format: uuid
             * @description Currency to credit
             */
            currencyDefinitionId: string;
            /**
             * Format: double
             * @description Amount to credit
             */
            amount: number;
            /** @description Faucet transaction type */
            transactionType: components["schemas"]["TransactionType"];
            /** @description Reference type */
            referenceType?: string | null;
            /**
             * Format: uuid
             * @description Reference ID
             */
            referenceId?: string | null;
        };
        /** @description Request to credit multiple wallets */
        BatchCreditRequest: {
            /** @description Credit operations to execute */
            operations: components["schemas"]["BatchCreditOperation"][];
            /** @description Unique key covering the entire batch */
            idempotencyKey: string;
        };
        /** @description Results of batch credit operations */
        BatchCreditResponse: {
            /** @description Results for each operation */
            results: components["schemas"]["BatchCreditResult"][];
        };
        /** @description Result of a single credit in a batch */
        BatchCreditResult: {
            /** @description Index in the operations array */
            index: number;
            /** @description Whether the operation succeeded */
            success: boolean;
            /** @description Transaction record if successful */
            transaction?: components["schemas"]["CurrencyTransactionRecord"];
            /** @description Error code if failed */
            error?: string | null;
        };
        /** @description Request to get multiple balances */
        BatchGetBalancesRequest: {
            /** @description Balance queries to execute */
            queries: components["schemas"]["BalanceQuery"][];
        };
        /** @description Results of batch balance queries */
        BatchGetBalancesResponse: {
            /** @description Balance results (same order as queries) */
            balances: components["schemas"]["BatchBalanceResult"][];
        };
        /** @description Request to get multiple item instances */
        BatchGetItemInstancesRequest: {
            /** @description Instance IDs to retrieve */
            instanceIds: string[];
        };
        /** @description Multiple item instances */
        BatchGetItemInstancesResponse: {
            /** @description Found items */
            items: components["schemas"]["ItemInstanceResponse"][];
            /** @description Instance IDs that were not found */
            notFound: string[];
        };
        /** @description Compiled behavior tree data with bytecode or download reference */
        BehaviorTreeData: {
            /** @description Base64-encoded compiled bytecode for the behavior tree */
            bytecode?: string | null;
            /** @description Size of the bytecode in bytes */
            bytecodeSize?: number;
            /** @description URL to download the compiled behavior asset */
            downloadUrl?: string | null;
        };
        /** @description Request to bind an item to a character */
        BindItemInstanceRequest: {
            /**
             * Format: uuid
             * @description Instance ID to bind
             */
            instanceId: string;
            /**
             * Format: uuid
             * @description Character to bind the item to
             */
            characterId: string;
            /** @description Type of binding to apply */
            bindType?: components["schemas"]["SoulboundType"];
        };
        /** @description Request to bind a Git repository for automatic documentation sync */
        BindRepositoryRequest: {
            /**
             * @description Owner of this binding. NOT a session ID.
             *     For user-initiated bindings: the accountId (UUID format).
             *     For service-initiated bindings: the service name (e.g., "orchestrator").
             */
            owner: string;
            /** @description Documentation namespace to bind */
            namespace: string;
            /** @description Git clone URL (HTTPS for public repos) */
            repositoryUrl: string;
            /**
             * @description Branch to sync from
             * @default main
             */
            branch: string;
            /**
             * @description How often to sync (5 min to 24 hours)
             * @default 60
             */
            syncIntervalMinutes: number;
            /**
             * @description Glob patterns for files to include (defaults to all markdown files if not provided)
             * @default [
             *       "**\/*.md"
             *     ]
             */
            filePatterns: string[] | null;
            /**
             * @description Glob patterns for files to exclude (defaults to common non-content directories if not provided)
             * @default [
             *       ".git/**",
             *       ".obsidian/**",
             *       "node_modules/**"
             *     ]
             */
            excludePatterns: string[] | null;
            /** @description Map directory prefixes to categories (empty mapping if not provided) */
            categoryMapping?: {
                [key: string]: string;
            } | null;
            /** @description Default category for documents without mapping */
            defaultCategory?: components["schemas"]["DocumentCategory"];
            /**
             * @description Enable archive functionality
             * @default false
             */
            archiveEnabled: boolean;
            /**
             * @description Create archive after each sync
             * @default false
             */
            archiveOnSync: boolean;
        };
        /** @description Response confirming repository binding creation */
        BindRepositoryResponse: {
            /**
             * Format: uuid
             * @description Unique identifier of the repository binding
             */
            bindingId: string;
            /** @description Namespace the repository is bound to */
            namespace: string;
            /** @description URL of the bound repository */
            repositoryUrl?: string;
            /** @description Branch being synced */
            branch?: string;
            /** @description Current status of the binding */
            status: components["schemas"]["BindingStatus"];
            /**
             * Format: date-time
             * @description Timestamp when the binding was created
             */
            createdAt?: string;
        };
        /**
         * @description Status of a repository binding
         * @enum {string}
         */
        BindingStatus: "pending" | "syncing" | "synced" | "error" | "disabled";
        /** @description An axis-aligned bounding box in 3D space */
        Bounds: {
            /** @description Minimum corner (lowest x, y, z values) */
            min: components["schemas"]["Position3D"];
            /** @description Maximum corner (highest x, y, z values) */
            max: components["schemas"]["Position3D"];
        };
        /** @description Breach record details */
        BreachResponse: {
            /**
             * Format: uuid
             * @description Unique breach identifier
             */
            breachId: string;
            /**
             * Format: uuid
             * @description Contract that was breached
             */
            contractId: string;
            /**
             * Format: uuid
             * @description Entity that breached
             */
            breachingEntityId: string;
            /** @description Type of breaching entity */
            breachingEntityType: components["schemas"]["EntityType"];
            /** @description Type of breach */
            breachType: components["schemas"]["BreachType"];
            /** @description What was breached */
            breachedTermOrMilestone?: string | null;
            /** @description Breach description */
            description?: string | null;
            /** @description Current status */
            status: components["schemas"]["BreachStatus"];
            /**
             * Format: date-time
             * @description When breach was detected
             */
            detectedAt: string;
            /**
             * Format: date-time
             * @description Deadline to cure breach
             */
            cureDeadline?: string | null;
            /**
             * Format: date-time
             * @description When breach was cured
             */
            curedAt?: string | null;
            /**
             * Format: date-time
             * @description When consequences were applied
             */
            consequencesAppliedAt?: string | null;
        };
        /**
         * @description Current status of a breach record
         * @enum {string}
         */
        BreachStatus: "detected" | "cure_period" | "cured" | "consequences_applied" | "disputed" | "forgiven";
        /** @description Brief breach information */
        BreachSummary: {
            /**
             * Format: uuid
             * @description Breach ID
             */
            breachId: string;
            /** @description Type of breach */
            breachType: components["schemas"]["BreachType"];
            /** @description Current status */
            status: components["schemas"]["BreachStatus"];
        };
        /**
         * @description Type of contract breach
         * @enum {string}
         */
        BreachType: "term_violation" | "milestone_missed" | "unauthorized_action" | "non_payment";
        /** @description Request to retrieve metadata for multiple assets */
        BulkGetAssetsRequest: {
            /** @description Asset IDs to retrieve (max 100) */
            assetIds: string[];
            /**
             * @description Whether to generate pre-signed download URLs
             * @default false
             */
            includeDownloadUrls: boolean;
        };
        /** @description Batch asset metadata response */
        BulkGetAssetsResponse: {
            /** @description Found assets with metadata */
            assets: components["schemas"]["AssetWithDownloadUrl"][];
            /** @description Asset IDs that weren't found */
            notFound: string[];
        };
        /**
         * @description Bundle file format
         * @enum {string}
         */
        BundleFormat: "bannou" | "zip";
        /** @description Complete metadata for an asset bundle (API response model) */
        BundleInfo: {
            /** @description Human-readable bundle identifier (e.g., "synty/polygon-adventure", "my-bundle-v1") */
            bundleId: string;
            /** @description Whether source or metabundle */
            bundleType: components["schemas"]["BundleType"];
            /** @description Bundle content version string */
            version: string;
            /** @description Metadata version number (increments on metadata changes) */
            metadataVersion: number;
            /** @description Human-readable bundle name */
            name?: string | null;
            /** @description Bundle description */
            description?: string | null;
            /** @description Owner account ID or service name (null for system-owned bundles) */
            owner?: string | null;
            /** @description Game realm this bundle belongs to */
            realm: components["schemas"]["GameRealm"];
            /** @description Key-value tags for categorization and filtering */
            tags?: {
                [key: string]: string;
            } | null;
            /** @description Bundle lifecycle status */
            status: components["schemas"]["BundleLifecycle"];
            /** @description Number of assets in the bundle */
            assetCount: number;
            /**
             * Format: int64
             * @description Bundle file size in bytes (null if not yet calculated)
             */
            sizeBytes?: number | null;
            /**
             * Format: date-time
             * @description When the bundle was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description When the bundle metadata was last updated
             */
            updatedAt?: string | null;
            /**
             * Format: date-time
             * @description When the bundle was soft-deleted (null if active)
             */
            deletedAt?: string | null;
        };
        /**
         * @description Bundle lifecycle status:
         *     - active: Bundle is available for use
         *     - deleted: Bundle has been soft-deleted (within retention period)
         *     - processing: Bundle is being processed (metabundle creation)
         * @enum {string}
         */
        BundleLifecycle: "active" | "deleted" | "processing";
        /** @description Preview of bundle manifest for validation */
        BundleManifestPreview: {
            /** @description Human-readable bundle identifier from the manifest */
            bundleId: string;
            /** @description Bundle version from the manifest */
            version: string;
            /** @description Number of assets declared in the manifest */
            assetCount: number;
        };
        /** @description Summary information about a bundle */
        BundleSummary: {
            /** @description Human-readable bundle identifier */
            bundleId: string;
            /** @description Source or metabundle */
            bundleType: components["schemas"]["BundleType"];
            /** @description Bundle version */
            version: string;
            /** @description Number of assets in bundle */
            assetCount: number;
            /**
             * Format: int64
             * @description Bundle file size
             */
            sizeBytes?: number | null;
            /** @description Game realm */
            realm: components["schemas"]["GameRealm"];
            /**
             * Format: date-time
             * @description When the bundle was created
             */
            createdAt?: string | null;
        };
        /**
         * @description Bundle category:
         *     - source: Original bundle (uploaded or server-created from assets)
         *     - metabundle: Composed from other bundles server-side
         * @enum {string}
         */
        BundleType: "source" | "metabundle";
        /** @description Request to upload a pre-built asset bundle file */
        BundleUploadRequest: {
            /**
             * @description Owner of this bundle upload. NOT a session ID.
             *     For user-initiated uploads: the accountId (UUID format).
             *     For service-initiated uploads: the service name (e.g., "orchestrator").
             */
            owner: string;
            /** @description Must end with .bannou or .zip */
            filename: string;
            /**
             * Format: int64
             * @description Bundle file size in bytes
             */
            size: number;
            /** @description Optional preview of bundle manifest for validation */
            manifestPreview?: components["schemas"]["BundleManifestPreview"] | null;
        };
        /** @description A single version record in bundle history */
        BundleVersionRecord: {
            /** @description Version number */
            version: number;
            /**
             * Format: date-time
             * @description When this version was created
             */
            createdAt: string;
            /** @description Account ID that made the change */
            createdBy: string;
            /** @description List of changes in this version */
            changes: string[];
            /** @description Reason provided for the change */
            reason?: string | null;
            /** @description Full metadata snapshot at this version (only for current version) */
            snapshot?: components["schemas"]["BundleInfo"] | null;
        };
        /** @description Bundle metadata combined with a pre-signed download URL */
        BundleWithDownloadUrl: {
            /** @description Human-readable bundle identifier (e.g., "synty/polygon-adventure", "my-bundle-v1") */
            bundleId: string;
            /** @description Bundle version string */
            version: string;
            /**
             * Format: uri
             * @description Pre-signed URL for downloading the bundle
             */
            downloadUrl: string;
            /** @description Format of the downloadable bundle */
            format: components["schemas"]["BundleFormat"];
            /**
             * Format: date-time
             * @description When the download URL expires
             */
            expiresAt: string;
            /**
             * Format: int64
             * @description Bundle file size in bytes
             */
            size: number;
            /** @description Number of assets contained in the bundle */
            assetCount: number;
            /** @description True if ZIP format was served from conversion cache */
            fromCache: boolean;
        };
        /** @description Response containing a previously compiled behavior retrieved from cache */
        CachedBehaviorResponse: {
            /** @description Unique identifier for the cached behavior */
            behaviorId: string;
            /** @description The compiled behavior data retrieved from cache */
            compiledBehavior: components["schemas"]["CompiledBehavior"];
            /**
             * Format: date-time
             * @description When the behavior was cached
             */
            cacheTimestamp?: string | null;
            /** @description Whether this was a cache hit or miss */
            cacheHit?: boolean;
        };
        /** @description Information about a cadence */
        CadenceInfo: {
            /**
             * @description Cadence type
             * @enum {string}
             */
            type: "authentic" | "half" | "plagal" | "deceptive";
            /** @description Chord index where cadence ends */
            position: number;
            /**
             * @description Cadence strength
             * @enum {string|null}
             */
            strength?: "perfect" | "imperfect" | null;
        };
        /** @description Request to preview a currency conversion */
        CalculateConversionRequest: {
            /**
             * Format: uuid
             * @description Source currency definition ID
             */
            fromCurrencyId: string;
            /**
             * Format: uuid
             * @description Target currency definition ID
             */
            toCurrencyId: string;
            /**
             * Format: double
             * @description Amount to convert
             */
            fromAmount: number;
        };
        /** @description Conversion preview result */
        CalculateConversionResponse: {
            /**
             * Format: double
             * @description Amount that would be received
             */
            toAmount: number;
            /**
             * Format: double
             * @description Effective conversion rate applied
             */
            effectiveRate: number;
            /** @description Steps in the conversion */
            conversionPath?: components["schemas"]["ConversionStep"][];
            /** @description Base currency used for conversion */
            baseCurrency: string;
        };
        /** @description Request to cancel an async metabundle creation job */
        CancelJobRequest: {
            /**
             * Format: uuid
             * @description Job ID from the createMetabundle response
             */
            jobId: string;
        };
        /** @description Result of job cancellation attempt */
        CancelJobResponse: {
            /**
             * Format: uuid
             * @description Job identifier
             */
            jobId: string;
            /** @description Whether the job was successfully cancelled */
            cancelled: boolean;
            /**
             * @description Current job status after cancellation attempt
             * @enum {string}
             */
            status: "queued" | "processing" | "ready" | "failed" | "cancelled";
            /** @description Additional context about the cancellation result */
            message?: string | null;
        };
        /** @description Request to cancel escrow before fully funded */
        CancelRequest: {
            /**
             * Format: uuid
             * @description Escrow ID
             */
            escrowId: string;
            /** @description Reason for cancellation */
            reason?: string | null;
            /** @description Idempotency key */
            idempotencyKey: string;
        };
        /** @description Response from cancelling an escrow */
        CancelResponse: {
            /** @description Cancelled escrow agreement */
            escrow: components["schemas"]["EscrowAgreement"];
            /** @description Refund results for any deposits */
            refunds: components["schemas"]["RefundResult"][];
        };
        /** @description Request to cancel a subscription */
        CancelSubscriptionRequest: {
            /**
             * Format: uuid
             * @description ID of the subscription to cancel
             */
            subscriptionId: string;
            /** @description Optional reason for cancellation */
            reason?: string | null;
        };
        /**
         * @description What happens when a credit would exceed the wallet cap
         * @enum {string}
         */
        CapOverflowBehavior: "reject" | "cap_and_lose" | "cap_and_return";
        /** @description Request to capture (finalize) a hold */
        CaptureHoldRequest: {
            /**
             * Format: uuid
             * @description Hold ID to capture
             */
            holdId: string;
            /**
             * Format: double
             * @description Final amount to debit (may be less than hold amount)
             */
            captureAmount: number;
            /** @description Idempotency key */
            idempotencyKey: string;
        };
        /** @description Result of hold capture */
        CaptureHoldResponse: {
            /** @description Updated hold record */
            hold: components["schemas"]["HoldRecord"];
            /** @description Debit transaction */
            transaction: components["schemas"]["CurrencyTransactionRecord"];
            /**
             * Format: double
             * @description Balance after capture
             */
            newBalance: number;
            /**
             * Format: double
             * @description Difference between hold and capture (released back)
             */
            amountReleased: number;
        };
        /**
         * @description Compressed archive of a dead character.
         *     Contains text summaries instead of structured data for long-term storage.
         *     Self-contained with no external references (suitable for cleanup).
         */
        CharacterArchive: {
            /**
             * Format: uuid
             * @description Original character ID
             */
            characterId: string;
            /** @description Character display name */
            name: string;
            /**
             * Format: uuid
             * @description Realm the character belonged to
             */
            realmId: string;
            /**
             * Format: uuid
             * @description Character's species
             */
            speciesId: string;
            /**
             * Format: date-time
             * @description In-game birth date
             */
            birthDate: string;
            /**
             * Format: date-time
             * @description In-game death date
             */
            deathDate: string;
            /**
             * Format: date-time
             * @description When this archive was created
             */
            compressedAt: string;
            /**
             * @description Text summary of personality traits.
             *     Example: "Brave and loyal, somewhat hot-tempered with a strong sense of justice"
             */
            personalitySummary?: string | null;
            /**
             * @description Key backstory elements as text.
             *     Example: ["Trained by the Knights Guild", "Born in the Northlands"]
             */
            keyBackstoryPoints?: string[];
            /**
             * @description Significant life events as text.
             *     Example: ["Fought in the Battle of Stormgate (Hero)", "Survived the Great Flood"]
             */
            majorLifeEvents?: string[];
            /**
             * @description Text summary of family relationships.
             *     Example: "Father of 3, married to Elena, orphaned at young age"
             */
            familySummary?: string | null;
        };
        /** @description Context information about a character for behavior resolution */
        CharacterContext: {
            /**
             * @description Unique identifier for the NPC
             * @example npc_12345
             */
            npcId?: string | null;
            /**
             * @description Cultural background identifier
             * @example european_medieval
             */
            culture?: string | null;
            /**
             * @description Character profession identifier
             * @example blacksmith
             */
            profession?: string | null;
            /**
             * @description Character statistics and attributes
             * @example {
             *       "energy": 0.8,
             *       "health": 1,
             *       "hunger": 0.3
             *     }
             */
            stats?: {
                [key: string]: number;
            } | null;
            /**
             * @description Character skill levels
             * @example {
             *       "blacksmithing": 85,
             *       "trading": 42
             *     }
             */
            skills?: {
                [key: string]: number;
            } | null;
            /** @description Current location information for the character */
            location?: components["schemas"]["Location"];
            /** @description Relationship values with other characters */
            relationships?: {
                [key: string]: number;
            } | null;
            /** @description Relevant world state information */
            worldState?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Paginated list of characters with metadata for navigation */
        CharacterListResponse: {
            /** @description List of characters matching the query */
            characters: components["schemas"]["CharacterResponse"][];
            /** @description Total number of characters matching the filter criteria */
            totalCount: number;
            /** @description Current page number (1-based) */
            page: number;
            /** @description Number of results per page */
            pageSize: number;
            /** @description Whether there are more results after this page */
            hasNextPage?: boolean;
            /** @description Whether there are results before this page */
            hasPreviousPage?: boolean;
        };
        /** @description Complete character data returned from character operations */
        CharacterResponse: {
            /**
             * Format: uuid
             * @description Unique identifier for the character
             */
            characterId: string;
            /** @description Display name of the character */
            name: string;
            /**
             * Format: uuid
             * @description Realm ID (partition key)
             */
            realmId: string;
            /**
             * Format: uuid
             * @description Species ID (foreign key to Species service)
             */
            speciesId: string;
            /**
             * Format: date-time
             * @description In-game birth timestamp
             */
            birthDate: string;
            /**
             * Format: date-time
             * @description In-game death timestamp
             */
            deathDate?: string | null;
            /** @description Current lifecycle status of the character */
            status: components["schemas"]["CharacterStatus"];
            /**
             * Format: date-time
             * @description Real-world creation timestamp
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Real-world last update timestamp
             */
            updatedAt?: string | null;
        };
        /**
         * @description Character lifecycle status
         * @enum {string}
         */
        CharacterStatus: "alive" | "dead" | "dormant";
        /** @description Request to send a chat message to players in a game session */
        ChatMessageRequest: {
            /**
             * Format: uuid
             * @description WebSocket session ID of the sender. Provided by shortcut system.
             */
            sessionId: string;
            /**
             * Format: uuid
             * @description Account ID of the sender. Provided by shortcut system.
             */
            accountId: string;
            /** @description Game type for the chat. Determines which lobby's players receive the message. Provided by shortcut system. */
            gameType: string;
            /** @description Content of the chat message */
            message: string;
            /**
             * @description Type of message (public to all, whisper to one player, or system announcement)
             * @default public
             */
            messageType: components["schemas"]["ChatMessageType"];
            /**
             * Format: uuid
             * @description For whisper messages
             */
            targetPlayerId?: string | null;
        };
        /**
         * @description Type of chat message
         * @enum {string}
         */
        ChatMessageType: "public" | "whisper" | "system";
        /** @description Request to check asset requirement clauses */
        CheckAssetRequirementsRequest: {
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractInstanceId: string;
        };
        /** @description Response from checking asset requirements */
        CheckAssetRequirementsResponse: {
            /** @description Whether all requirements across all parties are satisfied */
            allSatisfied: boolean;
            /** @description Status broken down by party */
            byParty: components["schemas"]["PartyAssetRequirementStatus"][];
        };
        /** @description Request to check constraint */
        CheckConstraintRequest: {
            /**
             * Format: uuid
             * @description Entity to check
             */
            entityId: string;
            /** @description Entity type */
            entityType: components["schemas"]["EntityType"];
            /** @description Type of constraint to check */
            constraintType: components["schemas"]["ConstraintType"];
            /** @description What the entity wants to do */
            proposedAction?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Constraint check result */
        CheckConstraintResponse: {
            /** @description Whether action is allowed */
            allowed: boolean;
            /** @description Contracts that would be violated */
            conflictingContracts?: components["schemas"]["ContractSummary"][] | null;
            /** @description Explanation if not allowed */
            reason?: string | null;
        };
        /** @description Request to checkout a scene for editing */
        CheckoutRequest: {
            /**
             * Format: uuid
             * @description Scene to checkout
             */
            sceneId: string;
            /** @description Optional editor identifier (defaults to caller identity) */
            editorId?: string | null;
            /** @description Custom lock TTL (uses default if not specified) */
            ttlMinutes?: number | null;
        };
        /** @description Response containing checkout token and scene */
        CheckoutResponse: {
            /** @description Token required for commit/discard/heartbeat */
            checkoutToken: string;
            /** @description Current scene document */
            scene: components["schemas"]["Scene"];
            /**
             * Format: date-time
             * @description When the checkout lock expires
             */
            expiresAt: string;
        };
        /** @description A chord with timing information */
        ChordEvent: {
            /** @description Chord symbol */
            chord: components["schemas"]["ChordSymbol"];
            /** @description Start position in ticks */
            startTick: number;
            /** @description Duration in ticks */
            durationTicks: number;
            /** @description Roman numeral analysis (e.g., "IV", "V7") */
            romanNumeral?: string | null;
        };
        /** @description A chord symbol with root and quality */
        ChordSymbol: {
            /** @description Chord root */
            root: components["schemas"]["PitchClass"];
            /**
             * @description Chord quality
             * @enum {string}
             */
            quality: "major" | "minor" | "diminished" | "augmented" | "dominant7" | "major7" | "minor7" | "diminished7" | "halfDiminished7" | "augmented7" | "sus2" | "sus4";
            /** @description Bass note (for inversions/slash chords) */
            bass?: components["schemas"]["PitchClass"];
            /** @description Chord extensions (e.g., "9", "11", "13") */
            extensions?: string[] | null;
        };
        /** @description Status of a single clause's asset requirements */
        ClauseAssetStatus: {
            /** @description Clause identifier */
            clauseId: string;
            /** @description Whether requirement is satisfied */
            satisfied: boolean;
            /** @description What the clause requires */
            required: components["schemas"]["AssetRequirementInfo"];
            /** @description Current amount present */
            current: number;
            /** @description Amount still needed (0 if satisfied) */
            missing: number;
        };
        /**
         * @description Category of clause type
         * @enum {string}
         */
        ClauseCategory: "validation" | "execution" | "both";
        /** @description Summary of a clause type */
        ClauseTypeSummary: {
            /** @description Unique identifier */
            typeCode: string;
            /** @description Human-readable description */
            description: string;
            /** @description Clause category */
            category: components["schemas"]["ClauseCategory"];
            /** @description Whether a validation handler is registered */
            hasValidationHandler: boolean;
            /** @description Whether an execution handler is registered */
            hasExecutionHandler: boolean;
            /** @description Whether this is a built-in type */
            isBuiltIn: boolean;
        };
        /** @description Response containing the client's capability manifest with available API endpoints and shortcuts */
        ClientCapabilitiesResponse: {
            /**
             * Format: uuid
             * @description Session ID this capability manifest belongs to
             */
            sessionId: string;
            /** @description Available API capabilities for this client */
            capabilities: components["schemas"]["ClientCapability"][];
            /**
             * @description Pre-bound API calls available for this session.
             *     Shortcuts are invoked like normal capabilities but Connect injects
             *     a pre-bound payload instead of using the client's payload.
             */
            shortcuts?: components["schemas"]["ClientShortcut"][] | null;
            /** @description Capability manifest version (increments on changes) */
            version: number;
            /**
             * Format: date-time
             * @description When this capability manifest was generated
             */
            generatedAt: string;
            /**
             * Format: date-time
             * @description When these capabilities expire and need refresh
             */
            expiresAt?: string | null;
        };
        /** @description A single API capability available to the client, mapping a client-salted GUID to a service endpoint */
        ClientCapability: {
            /**
             * Format: uuid
             * @description Client-salted GUID for this API endpoint (unique per session)
             */
            guid: string;
            /** @description Service name (e.g., "account", "auth") */
            service: string;
            /** @description API endpoint path (e.g., "/account/create") */
            endpoint: string;
            /**
             * @description HTTP method for this endpoint
             * @enum {string}
             */
            method: "GET" | "POST" | "PUT" | "DELETE" | "PATCH";
            /** @description Human-readable description of this capability */
            description?: string | null;
            /**
             * Format: uint16
             * @description Preferred WebSocket channel for this capability
             * @default 0
             */
            channel: number;
        };
        /**
         * @description Session shortcut information sent to clients in the capability manifest.
         *     Shortcuts appear as invocable capabilities but Connect injects a pre-bound
         *     payload when the shortcut GUID is used, replacing any client-provided payload.
         */
        ClientShortcut: {
            /**
             * Format: uuid
             * @description GUID to use in WebSocket message header when invoking this shortcut.
             *     Uses UUID version 7 bits to distinguish from regular service GUIDs (version 5).
             */
            guid: string;
            /** @description The service this shortcut invokes (for client display purposes). */
            targetService: string;
            /** @description The endpoint this shortcut invokes (for client display purposes). */
            targetEndpoint: string;
            /** @description Machine-readable shortcut identifier (e.g., "get_my_stats", "join_game"). */
            name: string;
            /** @description Human-readable description of what this shortcut does. */
            description?: string | null;
            /** @description User-friendly name for display in client UIs. */
            displayName?: string | null;
            /** @description The service that created this shortcut. */
            sourceService?: string | null;
            /** @description Categorization tags for client-side organization. */
            tags?: string[] | null;
            /**
             * Format: date-time
             * @description When this shortcut expires (if time-limited).
             */
            expiresAt?: string | null;
        };
        /** @description Request to collapse a delta chain into a full snapshot */
        CollapseDeltasRequest: {
            /** @description Game identifier for namespace isolation */
            gameId: string;
            /**
             * Format: uuid
             * @description Entity ID that owns the save slot
             */
            ownerId: string;
            /** @description Type of entity that owns this save slot */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Name of the slot containing deltas to collapse */
            slotName: string;
            /** @description Version to collapse to (latest if null) */
            versionNumber?: number | null;
            /**
             * @description Delete intermediate delta versions after collapse
             * @default true
             */
            deleteIntermediates: boolean;
        };
        /**
         * @description Combat behavior preferences that influence tactical decisions.
         *     These values affect GOAP action selection, retreat conditions,
         *     and group coordination behavior.
         */
        CombatPreferences: {
            /** @description Overall combat approach */
            style: components["schemas"]["CombatStyle"];
            /** @description Preferred engagement distance */
            preferredRange: components["schemas"]["PreferredRange"];
            /** @description Role when fighting in groups */
            groupRole: components["schemas"]["GroupRole"];
            /**
             * Format: float
             * @description Willingness to take dangerous actions (0.0 = very cautious, 1.0 = reckless).
             *     Affects ability selection and target prioritization.
             */
            riskTolerance: number;
            /**
             * Format: float
             * @description Health percentage at which retreat is considered (0.0 = fight to death,
             *     0.5 = retreat at half health, 1.0 = retreat at any damage).
             */
            retreatThreshold: number;
            /**
             * @description Whether to prioritize ally protection over self-preservation.
             *     Affects target selection and positioning decisions.
             */
            protectAllies: boolean;
        };
        /** @description Combat preferences profile for behavior system consumption */
        CombatPreferencesResponse: {
            /**
             * Format: uuid
             * @description Character these preferences belong to
             */
            characterId: string;
            /** @description The combat preferences values */
            preferences: components["schemas"]["CombatPreferences"];
            /** @description Preferences version number (increments on each evolution) */
            version: number;
            /**
             * Format: date-time
             * @description When these preferences were first created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description When these preferences were last modified
             */
            updatedAt?: string | null;
        };
        /** @description Snapshot of combat preferences for enriched response */
        CombatPreferencesSnapshot: {
            /** @description Combat style (DEFENSIVE, BALANCED, AGGRESSIVE, BERSERKER, TACTICAL) */
            style: string;
            /** @description Preferred engagement distance (MELEE, CLOSE, MEDIUM, RANGED) */
            preferredRange: string;
            /** @description Role in group combat (FRONTLINE, SUPPORT, FLANKER, LEADER, SOLO) */
            groupRole: string;
            /**
             * Format: float
             * @description Willingness to take risky actions (0.0 to 1.0)
             */
            riskTolerance: number;
            /**
             * Format: float
             * @description Health percentage at which retreat is considered (0.0 to 1.0)
             */
            retreatThreshold: number;
            /** @description Whether to prioritize ally protection */
            protectAllies: boolean;
        };
        /**
         * @description Overall approach to combat situations. Affects target selection,
         *     ability usage, and engagement decisions.
         * @enum {string}
         */
        CombatStyle: "DEFENSIVE" | "BALANCED" | "AGGRESSIVE" | "BERSERKER" | "TACTICAL";
        /** @description Request to commit checkout changes */
        CommitRequest: {
            /**
             * Format: uuid
             * @description Scene being committed
             */
            sceneId: string;
            /** @description Checkout token from checkout response */
            checkoutToken: string;
            /** @description Updated scene document */
            scene: components["schemas"]["Scene"];
            /** @description Optional summary of changes for audit */
            changesSummary?: string | null;
        };
        /** @description Response confirming commit */
        CommitResponse: {
            /** @description Whether commit was successful */
            committed: boolean;
            /** @description New version after commit */
            newVersion: string;
            /** @description Committed scene with updated metadata */
            scene?: components["schemas"]["Scene"];
        };
        /**
         * @description Comparison operators for numeric conditions
         * @enum {string}
         */
        ComparisonOperator: "eq" | "ne" | "gt" | "gte" | "lt" | "lte";
        /** @description Options controlling the ABML compilation process including optimizations and caching */
        CompilationOptions: {
            /**
             * @description Enable behavior tree optimizations
             * @default true
             */
            enableOptimizations: boolean;
            /**
             * @description Cache the compiled behavior for reuse
             * @default true
             */
            cacheCompiledResult: boolean;
            /**
             * @description Enable strict validation mode
             * @default false
             */
            strictValidation: boolean;
            /**
             * @description Apply cultural adaptations during compilation
             * @default true
             */
            culturalAdaptations: boolean;
            /**
             * @description Generate GOAP goals from behaviors
             * @default true
             */
            goapIntegration: boolean;
        };
        /** @description Request to compile an ABML behavior definition into executable behavior trees */
        CompileBehaviorRequest: {
            /**
             * @description Raw ABML YAML content to compile
             * @example version: "1.0.0"
             *     metadata:
             *       id: "example_behavior"
             *       category: "basic"
             *     behaviors:
             *       example:
             *         triggers:
             *           - condition: "true"
             *         actions:
             *           - log:
             *               message: "Hello World"
             */
            abmlContent: string;
            /**
             * @description Optional human-readable name for the behavior.
             *     If not provided, extracted from ABML metadata.id or generated from content hash.
             * @example blacksmith_daily_routine
             */
            behaviorName?: string | null;
            /**
             * @description Category for organizing behaviors (e.g., profession, cultural, situational).
             *     Used for filtering and grouping in bundles.
             * @example professional
             * @enum {string|null}
             */
            behaviorCategory?: "base" | "cultural" | "professional" | "personal" | "situational" | "ambient" | null;
            /**
             * @description Optional bundle identifier for grouping related behaviors.
             *     When specified, the compiled behavior will be added to a bundle with this ID.
             *     Clients can then download entire bundles for efficient bulk loading.
             *     If the bundle doesn't exist, it will be created.
             * @example blacksmith-behaviors-v1
             */
            bundleId?: string | null;
            /** @description Character context for context variable resolution during compilation */
            characterContext?: components["schemas"]["CharacterContext"];
            /** @description Options controlling the compilation process */
            compilationOptions?: components["schemas"]["CompilationOptions"];
        };
        /** @description Response containing the results of an ABML behavior compilation */
        CompileBehaviorResponse: {
            /**
             * @description Unique identifier for the compiled behavior (content-addressable hash)
             * @example behavior-a1b2c3d4e5f6g7h8
             */
            behaviorId: string;
            /**
             * @description Human-readable name of the behavior
             * @example blacksmith_daily_routine
             */
            behaviorName?: string | null;
            /** @description The compiled behavior data including behavior tree and metadata */
            compiledBehavior?: components["schemas"]["CompiledBehavior"];
            /** @description Time taken to compile the behavior in milliseconds */
            compilationTimeMs?: number;
            /** @description Asset service ID where the compiled bytecode is stored. Null only when caching is explicitly disabled. */
            assetId?: string | null;
            /** @description Bundle ID if the behavior was added to a bundle. Null if not bundled. */
            bundleId?: string | null;
            /** @description True if this replaced an existing behavior with the same content hash */
            isUpdate?: boolean;
            /** @description Non-fatal warnings during compilation */
            warnings?: string[] | null;
        };
        /** @description Compiled behavior containing behavior tree, context schema, and GOAP integration data */
        CompiledBehavior: {
            /** @description Compiled behavior tree data with bytecode or download reference */
            behaviorTree: components["schemas"]["BehaviorTreeData"];
            /** @description Schema defining required context variables for execution */
            contextSchema: components["schemas"]["ContextSchemaData"];
            /** @description List of required services for this behavior */
            serviceDependencies?: string[] | null;
            /** @description GOAP goals extracted from the behavior */
            goapGoals?: components["schemas"]["GoapGoal"][] | null;
            /** @description Metadata for behavior execution including performance hints and resource requirements */
            executionMetadata?: components["schemas"]["ExecutionMetadata"];
        };
        /** @description Request to complete a milestone */
        CompleteMilestoneRequest: {
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractId: string;
            /** @description Milestone to complete */
            milestoneCode: string;
            /** @description Evidence of completion */
            evidence?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Request to finalize an upload and trigger asset processing */
        CompleteUploadRequest: {
            /**
             * Format: uuid
             * @description Upload session ID from requestUpload
             */
            uploadId: string;
            /** @description For multipart uploads - ETags of completed parts (null for single-file uploads) */
            parts?: components["schemas"]["CompletedPart"][] | null;
        };
        /** @description Information about a completed part in a multipart upload */
        CompletedPart: {
            /** @description Part number (1-based) */
            partNumber: number;
            /** @description ETag returned from part upload */
            etag: string;
        };
        /** @description Metadata about a generated composition */
        CompositionMetadata: {
            /** @description Style used */
            styleId?: string | null;
            /** @description Key signature */
            key?: components["schemas"]["KeySignature"];
            /** @description Tempo in BPM */
            tempo?: number | null;
            /** @description Number of bars */
            bars?: number | null;
            /** @description Tune type if applicable */
            tuneType?: string | null;
            /** @description Random seed used */
            seed?: number | null;
        };
        /**
         * @description Compression algorithm for bundles
         * @enum {string}
         */
        CompressionType: "lz4" | "lzma" | "none";
        /** @description A bundle entry in an asset conflict */
        ConflictingBundleEntry: {
            /** @description Bundle containing this version */
            bundleId: string;
            /** @description Content hash of asset in this bundle */
            contentHash: string;
        };
        /** @description Request to record party consent for release or refund */
        ConsentRequest: {
            /**
             * Format: uuid
             * @description Escrow ID
             */
            escrowId: string;
            /**
             * Format: uuid
             * @description Party giving consent
             */
            partyId: string;
            /** @description Type of entity (Account, Character, etc.) */
            partyType: components["schemas"]["EntityType"];
            /** @description Type of consent being given */
            consentType: components["schemas"]["EscrowConsentType"];
            /** @description Release token (required for full_consent) */
            releaseToken?: string | null;
            /** @description Optional notes */
            notes?: string | null;
            /** @description Idempotency key */
            idempotencyKey: string;
        };
        /** @description Response from recording party consent */
        ConsentResponse: {
            /** @description Updated escrow agreement */
            escrow: components["schemas"]["EscrowAgreement"];
            /** @description Whether consent was recorded */
            consentRecorded: boolean;
            /** @description Whether this consent triggered completion */
            triggered: boolean;
            /** @description New escrow status after consent */
            newStatus: components["schemas"]["EscrowStatus"];
        };
        /**
         * @description Party's consent status
         * @enum {string}
         */
        ConsentStatus: "pending" | "consented" | "declined" | "implicit";
        /** @description Request to consent to a contract */
        ConsentToContractRequest: {
            /**
             * Format: uuid
             * @description Contract to consent to
             */
            contractId: string;
            /**
             * Format: uuid
             * @description Entity ID of consenting party
             */
            partyEntityId: string;
            /** @description Entity type of consenting party */
            partyEntityType: components["schemas"]["EntityType"];
        };
        /**
         * @description Type of constraint to check
         * @enum {string}
         */
        ConstraintType: "exclusivity" | "non_compete" | "territory" | "time_commitment";
        /** @description User-submitted contact form data */
        ContactRequest: {
            /**
             * Format: email
             * @description Sender email address for replies
             */
            email: string;
            /** @description Name of the person submitting the form */
            name?: string | null;
            /** @description Subject line of the contact message */
            subject: string;
            /** @description Body content of the contact message */
            message: string;
            /**
             * @description Category to route the contact request
             * @default general
             * @enum {string}
             */
            category: "general" | "support" | "bug" | "feedback" | "business";
        };
        /** @description Confirmation response after submitting a contact form */
        ContactResponse: {
            /**
             * Format: uuid
             * @description Unique identifier for the created support ticket
             */
            ticketId: string;
            /**
             * @description Confirmation message displayed to the user
             * @default Thank you for contacting us. We will respond within 24-48 hours.
             */
            message: string;
        };
        /**
         * @description Container capacity constraint type
         * @enum {string}
         */
        ContainerConstraintModel: "slot_only" | "weight_only" | "slot_and_weight" | "grid" | "volumetric" | "unlimited";
        /** @description Item in a container */
        ContainerItem: {
            /**
             * Format: uuid
             * @description Item instance ID
             */
            instanceId: string;
            /**
             * Format: uuid
             * @description Item template ID
             */
            templateId: string;
            /**
             * Format: double
             * @description Item quantity
             */
            quantity: number;
            /** @description Slot position */
            slotIndex?: number | null;
            /** @description Grid X position */
            slotX?: number | null;
            /** @description Grid Y position */
            slotY?: number | null;
            /** @description Rotated in grid */
            rotated?: boolean | null;
        };
        /**
         * @description Type of entity that owns this container
         * @enum {string}
         */
        ContainerOwnerType: "character" | "account" | "location" | "vehicle" | "guild" | "escrow" | "mail" | "other";
        /** @description Container details */
        ContainerResponse: {
            /**
             * Format: uuid
             * @description Container unique identifier
             */
            containerId: string;
            /**
             * Format: uuid
             * @description Owner entity ID
             */
            ownerId: string;
            /** @description Owner type */
            ownerType: components["schemas"]["ContainerOwnerType"];
            /** @description Container type */
            containerType: string;
            /** @description Constraint model */
            constraintModel: components["schemas"]["ContainerConstraintModel"];
            /** @description Whether this is an equipment slot */
            isEquipmentSlot: boolean;
            /** @description Equipment slot name */
            equipmentSlotName?: string | null;
            /** @description Maximum slots */
            maxSlots?: number | null;
            /** @description Current used slots */
            usedSlots?: number | null;
            /**
             * Format: double
             * @description Maximum weight
             */
            maxWeight?: number | null;
            /** @description Internal grid width */
            gridWidth?: number | null;
            /** @description Internal grid height */
            gridHeight?: number | null;
            /**
             * Format: double
             * @description Maximum volume
             */
            maxVolume?: number | null;
            /**
             * Format: double
             * @description Current volume used
             */
            currentVolume?: number | null;
            /**
             * Format: uuid
             * @description Parent container ID
             */
            parentContainerId?: string | null;
            /** @description Depth in container hierarchy */
            nestingDepth: number;
            /** @description Whether can hold containers */
            canContainContainers: boolean;
            /** @description Max nesting depth */
            maxNestingDepth?: number | null;
            /**
             * Format: double
             * @description Empty container weight
             */
            selfWeight: number;
            /** @description Weight propagation mode */
            weightContribution: components["schemas"]["WeightContribution"];
            /** @description Slots used in parent */
            slotCost: number;
            /** @description Width in parent grid */
            parentGridWidth?: number | null;
            /** @description Height in parent grid */
            parentGridHeight?: number | null;
            /**
             * Format: double
             * @description Volume in parent
             */
            parentVolume?: number | null;
            /**
             * Format: double
             * @description Weight of direct contents
             */
            contentsWeight: number;
            /**
             * Format: double
             * @description Total weight including self
             */
            totalWeight: number;
            /** @description Allowed categories */
            allowedCategories?: string[] | null;
            /** @description Forbidden categories */
            forbiddenCategories?: string[] | null;
            /** @description Required tags */
            allowedTags?: string[] | null;
            /**
             * Format: uuid
             * @description Realm ID
             */
            realmId?: string | null;
            /** @description Container tags */
            tags?: string[];
            /** @description Game-specific data */
            metadata?: Record<string, never> | null;
            /**
             * Format: date-time
             * @description Creation timestamp
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Last modification
             */
            modifiedAt?: string | null;
        };
        /** @description Container with item contents */
        ContainerWithContentsResponse: {
            /** @description Container details */
            container: components["schemas"]["ContainerResponse"];
            /** @description Items in container */
            items: components["schemas"]["ContainerItem"][];
        };
        /** @description Schema defining required context variables for behavior execution */
        ContextSchemaData: {
            [key: string]: unknown;
        };
        /** @description Contract instance details */
        ContractInstanceResponse: {
            /**
             * Format: uuid
             * @description Unique contract identifier
             */
            contractId: string;
            /**
             * Format: uuid
             * @description Source template ID
             */
            templateId: string;
            /** @description Source template code */
            templateCode?: string;
            /** @description Current contract status */
            status: components["schemas"]["ContractStatus"];
            /** @description Contract parties */
            parties: components["schemas"]["ContractPartyResponse"][];
            /** @description Contract terms */
            terms?: components["schemas"]["ContractTerms"];
            /** @description Milestone progress */
            milestones?: components["schemas"]["MilestoneInstanceResponse"][] | null;
            /** @description Index of current milestone */
            currentMilestoneIndex?: number | null;
            /** @description Related escrow IDs */
            escrowIds?: string[] | null;
            /**
             * Format: date-time
             * @description When contract was proposed
             */
            proposedAt?: string | null;
            /**
             * Format: date-time
             * @description When all parties consented
             */
            acceptedAt?: string | null;
            /**
             * Format: date-time
             * @description When contract became active
             */
            effectiveFrom?: string | null;
            /**
             * Format: date-time
             * @description When contract expires
             */
            effectiveUntil?: string | null;
            /**
             * Format: date-time
             * @description When contract was terminated
             */
            terminatedAt?: string | null;
            /** @description Game-specific metadata */
            gameMetadata?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: date-time
             * @description Creation timestamp
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Last update timestamp
             */
            updatedAt?: string | null;
        };
        /** @description Contract status summary */
        ContractInstanceStatusResponse: {
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractId: string;
            /** @description Current status */
            status: components["schemas"]["ContractStatus"];
            /** @description Milestone progress summary */
            milestoneProgress: components["schemas"]["MilestoneProgressSummary"][];
            /** @description Parties who haven't consented */
            pendingConsents?: components["schemas"]["PendingConsentSummary"][] | null;
            /** @description Active breach records */
            activeBreaches?: components["schemas"]["BreachSummary"][] | null;
            /** @description Days until natural expiration */
            daysUntilExpiration?: number | null;
        };
        /** @description Contract metadata */
        ContractMetadataResponse: {
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractId: string;
            /** @description Instance-level metadata */
            instanceData?: {
                [key: string]: unknown;
            } | null;
            /** @description Runtime state metadata */
            runtimeState?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Party input for contract creation */
        ContractPartyInput: {
            /**
             * Format: uuid
             * @description Entity ID
             */
            entityId: string;
            /** @description Entity type */
            entityType: components["schemas"]["EntityType"];
            /** @description Role from template */
            role: string;
        };
        /** @description Contract party details */
        ContractPartyResponse: {
            /**
             * Format: uuid
             * @description Entity ID
             */
            entityId: string;
            /** @description Entity type */
            entityType: components["schemas"]["EntityType"];
            /** @description Role in contract */
            role: string;
            /** @description Consent status */
            consentStatus: components["schemas"]["ConsentStatus"];
            /**
             * Format: date-time
             * @description When consent was given
             */
            consentedAt?: string | null;
        };
        /**
         * @description Current status of a contract instance
         * @enum {string}
         */
        ContractStatus: "draft" | "proposed" | "pending" | "active" | "fulfilled" | "expired" | "terminated" | "breached" | "suspended" | "disputed" | "declined";
        /** @description Brief contract information */
        ContractSummary: {
            /**
             * Format: uuid
             * @description Contract ID
             */
            contractId: string;
            /** @description Template code */
            templateCode: string;
            /** @description Template name */
            templateName?: string | null;
            /** @description Current status */
            status: components["schemas"]["ContractStatus"];
            /** @description Entity's role in contract */
            role: string;
            /**
             * Format: date-time
             * @description When contract expires
             */
            effectiveUntil?: string | null;
        };
        /** @description Contract template details */
        ContractTemplateResponse: {
            /**
             * Format: uuid
             * @description Unique template identifier
             */
            templateId: string;
            /** @description Unique template code */
            code: string;
            /** @description Human-readable name */
            name: string;
            /** @description Detailed description */
            description?: string | null;
            /**
             * Format: uuid
             * @description Realm ID if realm-specific
             */
            realmId?: string | null;
            /** @description Minimum parties required */
            minParties: number;
            /** @description Maximum parties allowed */
            maxParties: number;
            /** @description Party role definitions */
            partyRoles: components["schemas"]["PartyRoleDefinition"][];
            /** @description Default contract terms */
            defaultTerms?: components["schemas"]["ContractTerms"];
            /** @description Milestone definitions */
            milestones?: components["schemas"]["MilestoneDefinition"][] | null;
            /** @description Default enforcement mode */
            defaultEnforcementMode: components["schemas"]["EnforcementMode"];
            /** @description Whether contracts can be transferred */
            transferable?: boolean;
            /** @description Game-specific metadata */
            gameMetadata?: {
                [key: string]: unknown;
            } | null;
            /** @description Whether template is active */
            isActive: boolean;
            /**
             * Format: date-time
             * @description Creation timestamp
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Last update timestamp
             */
            updatedAt?: string | null;
        };
        /** @description Configurable contract terms */
        ContractTerms: {
            /** @description Contract duration (ISO 8601 duration, null for perpetual) */
            duration?: string | null;
            /** @description When payments occur */
            paymentSchedule?: components["schemas"]["PaymentSchedule"];
            /** @description Recurring payment frequency (ISO 8601 duration) */
            paymentFrequency?: string | null;
            /** @description How contract can be terminated */
            terminationPolicy?: components["schemas"]["TerminationPolicy"];
            /** @description Required notice for termination (ISO 8601 duration) */
            terminationNoticePeriod?: string | null;
            /** @description Breaches before auto-termination (0 for no auto) */
            breachThreshold?: number | null;
            /** @description Time to cure breach (ISO 8601 duration) */
            gracePeriodForCure?: string | null;
            /** @description Game-specific custom terms */
            customTerms?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description A step in the conversion path */
        ConversionStep: {
            /** @description Source currency code */
            from: string;
            /** @description Target currency code */
            to: string;
            /**
             * Format: double
             * @description Rate applied in this step
             */
            rate: number;
        };
        /** @description 3D spatial coordinates representing a position in the game world */
        Coordinates: {
            /** @description X coordinate position */
            x?: number;
            /** @description Y coordinate position */
            y?: number;
            /** @description Z coordinate position */
            z?: number;
        };
        /** @description Request to copy save data from one slot to another, optionally across different entities or games. */
        CopySaveRequest: {
            /** @description Game identifier of the source save */
            sourceGameId: string;
            /**
             * Format: uuid
             * @description Entity ID that owns the source save
             */
            sourceOwnerId: string;
            /** @description Type of entity that owns the source save */
            sourceOwnerType: components["schemas"]["OwnerType"];
            /** @description Name of the source slot to copy from */
            sourceSlotName: string;
            /** @description Version to copy (latest if null) */
            sourceVersion?: number | null;
            /** @description Game identifier for the target save */
            targetGameId: string;
            /**
             * Format: uuid
             * @description Entity ID that will own the copied save
             */
            targetOwnerId: string;
            /** @description Type of entity that will own the copied save */
            targetOwnerType: components["schemas"]["OwnerType"];
            /** @description Name of the target slot to copy to */
            targetSlotName: string;
            /** @description Category for new slot if auto-created */
            targetCategory?: components["schemas"]["SaveCategory"];
        };
        /** @description Request to count items */
        CountItemsRequest: {
            /**
             * Format: uuid
             * @description Owner to count for
             */
            ownerId: string;
            /** @description Owner type */
            ownerType: components["schemas"]["ContainerOwnerType"];
            /**
             * Format: uuid
             * @description Template to count
             */
            templateId: string;
        };
        /** @description Count result */
        CountItemsResponse: {
            /**
             * Format: uuid
             * @description Counted template
             */
            templateId: string;
            /**
             * Format: double
             * @description Total quantity
             */
            totalQuantity: number;
            /** @description Number of stacks */
            stackCount: number;
        };
        /** @description Statistics about asset resolution coverage */
        CoverageAnalysis: {
            /** @description Total number of assets requested */
            totalRequested: number;
            /** @description Assets resolved through bundle downloads */
            resolvedViaBundles: number;
            /** @description Assets resolved as standalone downloads */
            resolvedStandalone: number;
            /** @description Assets that could not be found */
            unresolvedCount: number;
            /**
             * Format: float
             * @description Ratio of assets provided to bundle downloads (higher is better)
             */
            bundleEfficiency?: number | null;
        };
        /** @description Request to create a new achievement */
        CreateAchievementDefinitionRequest: {
            /**
             * Format: uuid
             * @description ID of the game service owning this achievement
             */
            gameServiceId: string;
            /** @description Unique identifier for this achievement (lowercase, no spaces) */
            achievementId: string;
            /** @description Human-readable name */
            displayName: string;
            /** @description Description of how to earn this achievement */
            description: string;
            /** @description Description shown before achievement is earned (for hidden types) */
            hiddenDescription?: string | null;
            /**
             * @description Classification of the achievement (affects visibility and progress behavior)
             * @default standard
             */
            achievementType: components["schemas"]["AchievementType"];
            /** @description Which entity types can earn this achievement */
            entityTypes?: components["schemas"]["EntityType"][];
            /** @description Target value for progressive achievements */
            progressTarget?: number | null;
            /**
             * @description Point value of this achievement
             * @default 10
             */
            points: number;
            /** @description URL to achievement icon */
            iconUrl?: string | null;
            /** @description Platforms where this achievement exists */
            platforms?: components["schemas"]["Platform"][];
            /** @description Platform-specific achievement IDs (e.g., {"steam": "ACH_001"}) */
            platformIds?: {
                [key: string]: string;
            } | null;
            /** @description Achievement IDs that must be unlocked first */
            prerequisites?: string[] | null;
            /**
             * @description Whether this achievement can be earned
             * @default true
             */
            isActive: boolean;
            /** @description Additional achievement-specific metadata */
            metadata?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Request to create a new actor template definition */
        CreateActorTemplateRequest: {
            /** @description Category identifier (e.g., "npc-brain", "world-admin", "cron-cleanup") */
            category: string;
            /** @description Reference to behavior in lib-assets (e.g., "asset://behaviors/npc-brain-v1") */
            behaviorRef: string;
            /** @description Default configuration passed to behavior execution */
            configuration?: {
                [key: string]: unknown;
            } | null;
            /** @description Auto-spawn configuration for instantiate-on-access */
            autoSpawn?: components["schemas"]["AutoSpawnConfig"];
            /**
             * @description Milliseconds between behavior loop iterations
             * @default 1000
             */
            tickIntervalMs: number;
            /**
             * @description Seconds between automatic state saves (0 to disable)
             * @default 60
             */
            autoSaveIntervalSeconds: number;
            /**
             * @description Maximum actors of this category per pool node
             * @default 100
             */
            maxInstancesPerNode: number;
        };
        /** @description Request to create a point-in-time snapshot of namespace documentation */
        CreateArchiveRequest: {
            /**
             * @description Owner of this archive. NOT a session ID.
             *     For user-initiated archives: the accountId (UUID format).
             *     For service-initiated archives: the service name (e.g., "orchestrator").
             */
            owner: string;
            /** @description Documentation namespace to archive */
            namespace: string;
            /** @description Optional description for the archive */
            description?: string;
        };
        /** @description Response containing the created archive details */
        CreateArchiveResponse: {
            /**
             * Format: uuid
             * @description Unique identifier of the created archive
             */
            archiveId: string;
            /** @description Namespace that was archived */
            namespace: string;
            /**
             * Format: uuid
             * @description Asset ID in Asset Service
             */
            bundleAssetId?: string;
            /** @description Number of documents in the archive */
            documentCount?: number;
            /** @description Total size of the archive in bytes */
            sizeBytes?: number;
            /**
             * Format: date-time
             * @description Timestamp when the archive was created
             */
            createdAt?: string;
            /** @description Git commit hash if namespace is bound */
            commitHash?: string | null;
        };
        /** @description Request to create a new asset bundle from multiple assets */
        CreateBundleRequest: {
            /**
             * @description Owner of this bundle. NOT a session ID.
             *     For user-initiated bundles: the accountId (UUID format).
             *     For service-initiated bundles: the service name (e.g., "orchestrator").
             */
            owner: string;
            /** @description Human-readable bundle identifier (e.g., "synty/polygon-adventure", "my-bundle-v1") */
            bundleId: string;
            /**
             * @description Bundle version string
             * @default 1.0.0
             */
            version: string;
            /**
             * @description Game realm this bundle belongs to.
             *     Defaults to 'shared' if not specified.
             */
            realm?: components["schemas"]["GameRealm"] | null;
            /** @description List of asset IDs to include in the bundle */
            assetIds: string[];
            /** @description Compression algorithm to use for the bundle */
            compression?: components["schemas"]["CompressionType"];
            /** @description Custom metadata for the bundle (null if none) */
            metadata?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Response with bundle creation status and estimated size */
        CreateBundleResponse: {
            /** @description Human-readable bundle identifier (e.g., "synty/polygon-adventure", "my-bundle-v1") */
            bundleId: string;
            /**
             * @description Bundle creation status
             * @enum {string}
             */
            status: "queued" | "processing" | "ready" | "failed";
            /**
             * Format: int64
             * @description Estimated bundle size in bytes
             */
            estimatedSize: number;
        };
        /** @description Request to create a new container */
        CreateContainerRequest: {
            /**
             * Format: uuid
             * @description ID of the entity that owns this container
             */
            ownerId: string;
            /** @description Type of the owning entity */
            ownerType: components["schemas"]["ContainerOwnerType"];
            /** @description Game-defined container type (e.g., inventory, bank, equipment_slot) */
            containerType: string;
            /** @description Capacity constraint model */
            constraintModel: components["schemas"]["ContainerConstraintModel"];
            /**
             * @description Whether this container is an equipment slot
             * @default false
             */
            isEquipmentSlot: boolean;
            /** @description Equipment slot name if isEquipmentSlot is true */
            equipmentSlotName?: string | null;
            /** @description Maximum slots for slot-based containers */
            maxSlots?: number | null;
            /**
             * Format: double
             * @description Maximum weight capacity
             */
            maxWeight?: number | null;
            /** @description Internal grid width for grid containers */
            gridWidth?: number | null;
            /** @description Internal grid height for grid containers */
            gridHeight?: number | null;
            /**
             * Format: double
             * @description Maximum volume for volumetric containers
             */
            maxVolume?: number | null;
            /**
             * Format: uuid
             * @description Parent container ID for nested containers
             */
            parentContainerId?: string | null;
            /**
             * @description Whether this container can hold other containers
             * @default false
             */
            canContainContainers: boolean;
            /** @description Maximum nesting depth (null uses global default) */
            maxNestingDepth?: number | null;
            /**
             * Format: double
             * @description Empty container weight
             * @default 0
             */
            selfWeight: number;
            /** @description How weight propagates to parent */
            weightContribution?: components["schemas"]["WeightContribution"];
            /**
             * @description Slots used in slot-based parent
             * @default 1
             */
            slotCost: number;
            /** @description Width footprint in grid-based parent */
            parentGridWidth?: number | null;
            /** @description Height footprint in grid-based parent */
            parentGridHeight?: number | null;
            /**
             * Format: double
             * @description Volume footprint in volumetric parent
             */
            parentVolume?: number | null;
            /** @description Allowed item categories (null allows all) */
            allowedCategories?: string[] | null;
            /** @description Forbidden item categories */
            forbiddenCategories?: string[] | null;
            /** @description Required item tags for placement */
            allowedTags?: string[] | null;
            /**
             * Format: uuid
             * @description Realm this container belongs to (null for account-level)
             */
            realmId?: string | null;
            /** @description Container tags for filtering */
            tags?: string[] | null;
            /** @description Game-specific container data */
            metadata?: Record<string, never> | null;
        };
        /** @description Request to create a contract instance */
        CreateContractInstanceRequest: {
            /**
             * Format: uuid
             * @description Template to create instance from
             */
            templateId: string;
            /** @description Parties to this contract */
            parties: components["schemas"]["ContractPartyInput"][];
            /** @description Terms overriding template defaults */
            terms?: components["schemas"]["ContractTerms"];
            /**
             * Format: date-time
             * @description When contract becomes active (null for immediate)
             */
            effectiveFrom?: string | null;
            /**
             * Format: date-time
             * @description When contract expires (null for perpetual)
             */
            effectiveUntil?: string | null;
            /** @description Related escrow IDs */
            escrowIds?: string[] | null;
            /** @description Instance-level game metadata */
            gameMetadata?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Request to create a map definition */
        CreateDefinitionRequest: {
            /** @description Human-readable name */
            name: string;
            /** @description Description of the map template */
            description?: string | null;
            /** @description Layer configurations */
            layers?: components["schemas"]["LayerDefinition"][] | null;
            /** @description Default bounds for regions using this definition */
            defaultBounds?: components["schemas"]["Bounds"];
            /** @description Additional metadata */
            metadata?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Input for defining a party in escrow creation */
        CreateEscrowPartyInput: {
            /**
             * Format: uuid
             * @description Party entity ID
             */
            partyId: string;
            /** @description Type of entity (Account, Character, etc.) */
            partyType: components["schemas"]["EntityType"];
            /** @description Display name */
            displayName?: string | null;
            /** @description Role of this party in the escrow */
            role: components["schemas"]["EscrowPartyRole"];
            /** @description Whether consent is required (defaults based on role) */
            consentRequired?: boolean | null;
            /**
             * Format: uuid
             * @description Party wallet for currency operations
             */
            walletId?: string | null;
            /**
             * Format: uuid
             * @description Party container for item operations
             */
            containerId?: string | null;
        };
        /** @description Request to create a new escrow agreement */
        CreateEscrowRequest: {
            /** @description Type of escrow agreement */
            escrowType: components["schemas"]["EscrowType"];
            /** @description Trust mode for the escrow */
            trustMode: components["schemas"]["EscrowTrustMode"];
            /**
             * Format: uuid
             * @description For single_party_trusted mode
             */
            trustedPartyId?: string | null;
            /** @description Type of the trusted party */
            trustedPartyType?: components["schemas"]["EntityType"] | null;
            /** @description Parties in the escrow */
            parties: components["schemas"]["CreateEscrowPartyInput"][];
            /** @description Expected deposits from parties */
            expectedDeposits: components["schemas"]["ExpectedDepositInput"][];
            /** @description Optional explicit release allocations */
            releaseAllocations?: components["schemas"]["ReleaseAllocationInput"][] | null;
            /**
             * Format: uuid
             * @description Contract governing this escrow
             */
            boundContractId?: string | null;
            /** @description Number of consents required (-1 for all) */
            requiredConsentsForRelease?: number | null;
            /**
             * Format: date-time
             * @description Optional expiration time
             */
            expiresAt?: string | null;
            /** @description Reference type (trade, auction, etc.) */
            referenceType?: string | null;
            /**
             * Format: uuid
             * @description Reference entity ID
             */
            referenceId?: string | null;
            /** @description Human-readable description */
            description?: string | null;
            /** @description Application metadata */
            metadata?: {
                [key: string]: unknown;
            } | null;
            /** @description Idempotency key for this operation */
            idempotencyKey: string;
        };
        /** @description Response from creating an escrow agreement */
        CreateEscrowResponse: {
            /** @description Created escrow agreement */
            escrow: components["schemas"]["EscrowAgreement"];
            /** @description Deposit tokens for each party (full_consent mode) */
            depositTokens: components["schemas"]["PartyToken"][];
        };
        /** @description Request to create an authorization hold */
        CreateHoldRequest: {
            /**
             * Format: uuid
             * @description Wallet to hold funds in
             */
            walletId: string;
            /**
             * Format: uuid
             * @description Currency to hold
             */
            currencyDefinitionId: string;
            /**
             * Format: double
             * @description Amount to reserve
             */
            amount: number;
            /**
             * Format: date-time
             * @description When the hold auto-releases
             */
            expiresAt: string;
            /** @description Reference type (e.g. dining, hotel, gas) */
            referenceType?: string | null;
            /**
             * Format: uuid
             * @description Reference entity ID
             */
            referenceId?: string | null;
            /** @description Idempotency key */
            idempotencyKey: string;
        };
        /** @description Request to create a new item instance */
        CreateItemInstanceRequest: {
            /**
             * Format: uuid
             * @description Template to instantiate
             */
            templateId: string;
            /**
             * Format: uuid
             * @description Container to place the item in
             */
            containerId: string;
            /**
             * Format: uuid
             * @description Realm this instance exists in
             */
            realmId: string;
            /**
             * Format: double
             * @description Item quantity (respects template's quantityModel)
             */
            quantity: number;
            /** @description Slot position in slot-based containers */
            slotIndex?: number | null;
            /** @description X position in grid-based containers */
            slotX?: number | null;
            /** @description Y position in grid-based containers */
            slotY?: number | null;
            /** @description Whether item is rotated in grid */
            rotated?: boolean | null;
            /** @description Initial durability (defaults to template's maxDurability) */
            currentDurability?: number | null;
            /** @description Instance-specific stat modifications */
            customStats?: Record<string, never> | null;
            /** @description Player-assigned custom name */
            customName?: string | null;
            /** @description Any other instance-specific data */
            instanceMetadata?: Record<string, never> | null;
            /** @description How this item instance was created */
            originType: components["schemas"]["ItemOriginType"];
            /**
             * Format: uuid
             * @description Source entity ID (quest ID, creature ID, etc.)
             */
            originId?: string | null;
        };
        /** @description Request to create a new item template */
        CreateItemTemplateRequest: {
            /** @description Unique code within the game (immutable after creation) */
            code: string;
            /** @description Game service this template belongs to (immutable after creation) */
            gameId: string;
            /** @description Human-readable display name */
            name: string;
            /** @description Detailed description of this item */
            description?: string | null;
            /** @description Item classification category */
            category: components["schemas"]["ItemCategory"];
            /** @description Game-defined subcategory (e.g., sword, helmet) */
            subcategory?: string | null;
            /** @description Flexible filtering tags */
            tags?: string[] | null;
            /** @description Item rarity tier (defaults to config when not specified) */
            rarity?: components["schemas"]["ItemRarity"];
            /** @description How quantities are tracked for this item */
            quantityModel: components["schemas"]["QuantityModel"];
            /** @description Maximum stack size (1 for unique items) */
            maxStackSize: number;
            /** @description Unit for continuous quantities (e.g., liters, kg) */
            unitOfMeasure?: string | null;
            /** @description Precision for weight values (defaults to config when not specified) */
            weightPrecision?: components["schemas"]["WeightPrecision"];
            /**
             * Format: double
             * @description Weight value (interpreted per weightPrecision)
             */
            weight?: number | null;
            /**
             * Format: double
             * @description Volume for volumetric inventories
             */
            volume?: number | null;
            /** @description Width in grid-based inventories */
            gridWidth?: number | null;
            /** @description Height in grid-based inventories */
            gridHeight?: number | null;
            /** @description Whether item can be rotated in grid */
            canRotate?: boolean | null;
            /**
             * Format: double
             * @description Reference price for vendors/markets
             */
            baseValue?: number | null;
            /**
             * @description Whether item can be traded/auctioned
             * @default true
             */
            tradeable: boolean;
            /**
             * @description Whether item can be destroyed/discarded
             * @default true
             */
            destroyable: boolean;
            /** @description Binding behavior when item is acquired (defaults to config when not specified) */
            soulboundType?: components["schemas"]["SoulboundType"];
            /**
             * @description Whether item has durability tracking
             * @default false
             */
            hasDurability: boolean;
            /** @description Maximum durability value */
            maxDurability?: number | null;
            /** @description Realm availability scope */
            scope: components["schemas"]["ItemScope"];
            /** @description Realm IDs where this template is available (for realm_specific or multi_realm) */
            availableRealms?: string[] | null;
            /** @description Game-defined stats (e.g., attack, defense) */
            stats?: Record<string, never> | null;
            /** @description Game-defined effects (e.g., on_use, on_equip) */
            effects?: Record<string, never> | null;
            /** @description Game-defined requirements (e.g., level, strength) */
            requirements?: Record<string, never> | null;
            /** @description Display properties (e.g., iconId, modelId) */
            display?: Record<string, never> | null;
            /** @description Any other game-specific data */
            metadata?: Record<string, never> | null;
        };
        /** @description Request to create a new leaderboard */
        CreateLeaderboardDefinitionRequest: {
            /**
             * Format: uuid
             * @description ID of the game service owning this leaderboard
             */
            gameServiceId: string;
            /** @description Unique identifier for this leaderboard (lowercase, no spaces) */
            leaderboardId: string;
            /** @description Human-readable name for the leaderboard */
            displayName: string;
            /** @description Description of what this leaderboard tracks */
            description?: string | null;
            /** @description Which entity types can appear on this leaderboard */
            entityTypes?: components["schemas"]["EntityType"][];
            /**
             * @description Sort order (descending for high scores, ascending for times)
             * @default descending
             */
            sortOrder: components["schemas"]["SortOrder"];
            /**
             * @description How to handle score updates
             * @default replace
             */
            updateMode: components["schemas"]["UpdateMode"];
            /**
             * @description Whether this leaderboard resets each season
             * @default false
             */
            isSeasonal: boolean;
            /**
             * @description Whether the leaderboard is publicly visible
             * @default true
             */
            isPublic: boolean;
            /** @description Additional leaderboard-specific metadata */
            metadata?: {
                [key: string]: unknown;
            } | null;
        };
        /**
         * @description Request to create a metabundle from source bundles and/or standalone assets.
         *     At least one of sourceBundleIds or standaloneAssetIds must be provided.
         *     This enables packaging behaviors/scripts with 3D assets as a complete unit.
         */
        CreateMetabundleRequest: {
            /** @description Human-readable identifier for the new metabundle (e.g., "game-assets-v1") */
            metabundleId: string;
            /**
             * @description Human-readable source bundle IDs (e.g., "synty/polygon-adventure") to pull assets from.
             *     Can cherry-pick specific assets using assetFilter, or include all if assetFilter is null.
             */
            sourceBundleIds?: string[] | null;
            /**
             * @description Individual asset IDs (not in bundles) to include directly.
             *     Useful for including behaviors, scripts, or metadata files
             *     alongside bundled 3D assets.
             */
            standaloneAssetIds?: string[] | null;
            /**
             * @description Metabundle version string
             * @default 1.0.0
             */
            version: string;
            /**
             * @description Owner of this metabundle. NOT a session ID.
             *     For user-initiated: the accountId (UUID format).
             *     For service-initiated: the service name.
             */
            owner: string;
            /** @description Game realm for this metabundle */
            realm: components["schemas"]["GameRealm"];
            /** @description Human-readable description */
            description?: string | null;
            /**
             * @description Optional subset of asset IDs to include FROM SOURCE BUNDLES.
             *     If null, all assets from source bundles are included.
             *     Standalone assets are always included regardless of this filter.
             */
            assetFilter?: string[] | null;
            /** @description Custom metadata for the metabundle */
            metadata?: {
                [key: string]: unknown;
            } | null;
        };
        /**
         * @description Response from metabundle creation.
         *     For synchronous creation (small jobs): status=ready with downloadUrl.
         *     For async creation (large jobs): status=queued with jobId for polling.
         */
        CreateMetabundleResponse: {
            /** @description Human-readable metabundle identifier */
            metabundleId: string;
            /**
             * Format: uuid
             * @description Job ID for async processing. Only present when status is 'queued' or 'processing'.
             *     Use /bundles/job/status to poll for completion, or wait for
             *     MetabundleCreationCompleteEvent via WebSocket.
             */
            jobId?: string | null;
            /**
             * @description Creation status.
             *     - queued: Job accepted for async processing (poll with jobId)
             *     - processing: Job is actively running
             *     - ready: Metabundle created and available for download
             *     - failed: Creation failed (see conflicts for details)
             * @enum {string}
             */
            status: "queued" | "processing" | "ready" | "failed";
            /**
             * Format: uri
             * @description Pre-signed download URL (only present when status is 'ready')
             */
            downloadUrl?: string | null;
            /** @description Number of assets in the metabundle */
            assetCount: number;
            /** @description Number of standalone assets included directly (not from bundles) */
            standaloneAssetCount?: number | null;
            /**
             * Format: int64
             * @description Total size in bytes
             */
            sizeBytes: number;
            /** @description Provenance data for the metabundle */
            sourceBundles?: components["schemas"]["SourceBundleReference"][];
            /** @description Present if creation failed due to asset conflicts */
            conflicts?: components["schemas"]["AssetConflict"][] | null;
        };
        /** @description Request to create a new scene */
        CreateSceneRequest: {
            /** @description The scene document to create */
            scene: components["schemas"]["Scene"];
        };
        /** @description Request to create a new save slot for an entity. */
        CreateSlotRequest: {
            /** @description Game identifier for namespace isolation (e.g., "game-1", "game-2") */
            gameId: string;
            /**
             * Format: uuid
             * @description ID of the owning entity
             */
            ownerId: string;
            /** @description Type of entity that owns this save slot */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Slot name (lowercase alphanumeric with hyphens, single char like "q" allowed) */
            slotName: string;
            /** @description Save category determining retention and cleanup behavior */
            category: components["schemas"]["SaveCategory"];
            /** @description Override default max versions for this category (null = use category default) */
            maxVersions?: number | null;
            /** @description Days to retain versions (null = indefinite) */
            retentionDays?: number | null;
            /** @description Compression algorithm to use for save data (null = use category default) */
            compressionType?: components["schemas"]["CompressionType"] | null;
            /** @description Searchable tags for slot categorization (e.g., "boss-fight", "chapter-3") */
            tags?: string[] | null;
            /** @description Custom key-value metadata for the slot */
            metadata?: {
                [key: string]: string;
            } | null;
        };
        /** @description Request to create a new wallet */
        CreateWalletRequest: {
            /**
             * Format: uuid
             * @description Owner entity ID
             */
            ownerId: string;
            /** @description Type of owner entity */
            ownerType: components["schemas"]["WalletOwnerType"];
            /**
             * Format: uuid
             * @description Realm ID for realm-scoped wallets (null for global)
             */
            realmId?: string | null;
        };
        /** @description Request to credit currency to a wallet */
        CreditCurrencyRequest: {
            /**
             * Format: uuid
             * @description Target wallet ID
             */
            walletId: string;
            /**
             * Format: uuid
             * @description Currency to credit
             */
            currencyDefinitionId: string;
            /**
             * Format: double
             * @description Amount to credit (must be positive)
             */
            amount: number;
            /** @description Must be a faucet type (mint, quest_reward, loot_drop, etc.) */
            transactionType: components["schemas"]["TransactionType"];
            /** @description What triggered this transaction (quest, admin, etc.) */
            referenceType?: string | null;
            /**
             * Format: uuid
             * @description Reference entity ID
             */
            referenceId?: string | null;
            /** @description Unique key to prevent duplicate processing */
            idempotencyKey: string;
            /**
             * @description Skip earn cap enforcement (admin use)
             * @default false
             */
            bypassEarnCap: boolean;
            /** @description Free-form transaction metadata */
            metadata?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Result of credit operation */
        CreditCurrencyResponse: {
            /** @description Created transaction record */
            transaction: components["schemas"]["CurrencyTransactionRecord"];
            /**
             * Format: double
             * @description Balance after credit
             */
            newBalance: number;
            /** @description Whether earn cap limited the credit */
            earnCapApplied: boolean;
            /**
             * Format: double
             * @description Amount reduced by earn cap
             */
            earnCapAmountLimited?: number | null;
            /** @description Whether wallet cap limited the credit */
            walletCapApplied: boolean;
            /**
             * Format: double
             * @description Amount lost due to wallet cap (cap_and_lose behavior)
             */
            walletCapAmountLost?: number | null;
        };
        /** @description Request to cure a breach */
        CureBreachRequest: {
            /**
             * Format: uuid
             * @description Breach to cure
             */
            breachId: string;
            /** @description Evidence of cure */
            cureEvidence?: string | null;
        };
        /** @description Currency definition details */
        CurrencyDefinitionResponse: {
            /**
             * Format: uuid
             * @description Unique definition identifier
             */
            definitionId: string;
            /** @description Unique currency code */
            code: string;
            /** @description Human-readable name */
            name: string;
            /** @description Detailed description */
            description?: string | null;
            /** @description Realm availability scope */
            scope: components["schemas"]["CurrencyScope"];
            /** @description Available realm IDs */
            realmsAvailable?: string[] | null;
            /** @description Decimal precision */
            precision: components["schemas"]["CurrencyPrecision"];
            /** @description Whether transferable between wallets */
            transferable: boolean;
            /** @description Whether usable in trades */
            tradeable: boolean;
            /** @description Whether negative balance allowed (null uses plugin default) */
            allowNegative?: boolean | null;
            /**
             * Format: double
             * @description Maximum per-wallet balance
             */
            perWalletCap?: number | null;
            /** @description Overflow behavior when cap exceeded */
            capOverflowBehavior?: components["schemas"]["CapOverflowBehavior"];
            /**
             * Format: double
             * @description Global supply cap
             */
            globalSupplyCap?: number | null;
            /**
             * Format: double
             * @description Daily earn cap
             */
            dailyEarnCap?: number | null;
            /**
             * Format: double
             * @description Weekly earn cap
             */
            weeklyEarnCap?: number | null;
            /** @description Earn cap reset time */
            earnCapResetTime?: string | null;
            /** @description Whether autogain is enabled */
            autogainEnabled: boolean;
            /** @description Autogain calculation mode */
            autogainMode?: components["schemas"]["AutogainMode"];
            /**
             * Format: double
             * @description Autogain amount per interval
             */
            autogainAmount?: number | null;
            /** @description Autogain interval duration */
            autogainInterval?: string | null;
            /**
             * Format: double
             * @description Autogain balance cap
             */
            autogainCap?: number | null;
            /** @description Whether currency can expire */
            expires: boolean;
            /** @description Expiration policy */
            expirationPolicy?: components["schemas"]["ExpirationPolicy"];
            /**
             * Format: date-time
             * @description Fixed expiration date
             */
            expirationDate?: string | null;
            /** @description Expiration duration */
            expirationDuration?: string | null;
            /**
             * Format: uuid
             * @description Season ID for expiration
             */
            seasonId?: string | null;
            /** @description Whether linked to inventory item */
            linkedToItem: boolean;
            /**
             * Format: uuid
             * @description Linked item template ID
             */
            linkedItemTemplateId?: string | null;
            /** @description Item linkage mode */
            linkageMode?: components["schemas"]["ItemLinkageMode"];
            /** @description Whether this is the base currency */
            isBaseCurrency: boolean;
            /**
             * Format: double
             * @description Exchange rate to base currency
             */
            exchangeRateToBase?: number | null;
            /**
             * Format: date-time
             * @description When exchange rate was last updated
             */
            exchangeRateUpdatedAt?: string | null;
            /**
             * Format: uuid
             * @description Icon asset ID
             */
            iconAssetId?: string | null;
            /** @description Display format string */
            displayFormat?: string | null;
            /** @description Whether definition is active */
            isActive: boolean;
            /**
             * Format: date-time
             * @description Creation timestamp
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Last modification timestamp
             */
            modifiedAt?: string | null;
        };
        /**
         * @description How the currency handles decimal values (immutable after creation)
         * @enum {string}
         */
        CurrencyPrecision: "integer" | "decimal_2" | "decimal_4" | "decimal_8" | "decimal_full" | "big_integer";
        /**
         * @description Scope of currency availability across realms
         * @enum {string}
         */
        CurrencyScope: "global" | "realm_specific" | "multi_realm";
        /** @description Immutable record of a currency transaction */
        CurrencyTransactionRecord: {
            /**
             * Format: uuid
             * @description Unique transaction identifier
             */
            transactionId: string;
            /**
             * Format: uuid
             * @description Source wallet (null for faucets)
             */
            sourceWalletId?: string | null;
            /**
             * Format: uuid
             * @description Target wallet (null for sinks)
             */
            targetWalletId?: string | null;
            /**
             * Format: uuid
             * @description Currency definition ID
             */
            currencyDefinitionId: string;
            /**
             * Format: double
             * @description Transaction amount (always positive)
             */
            amount: number;
            /** @description Transaction classification */
            transactionType: components["schemas"]["TransactionType"];
            /** @description What triggered this transaction */
            referenceType?: string | null;
            /**
             * Format: uuid
             * @description Reference entity ID
             */
            referenceId?: string | null;
            /**
             * Format: uuid
             * @description Associated escrow ID
             */
            escrowId?: string | null;
            /** @description Idempotency key */
            idempotencyKey: string;
            /**
             * Format: date-time
             * @description When transaction occurred
             */
            timestamp: string;
            /**
             * Format: double
             * @description Source balance before transaction
             */
            sourceBalanceBefore?: number | null;
            /**
             * Format: double
             * @description Source balance after transaction
             */
            sourceBalanceAfter?: number | null;
            /**
             * Format: double
             * @description Target balance before transaction
             */
            targetBalanceBefore?: number | null;
            /**
             * Format: double
             * @description Target balance after transaction
             */
            targetBalanceAfter?: number | null;
            /** @description Number of autogain periods applied */
            autogainPeriodsApplied?: number | null;
            /**
             * Format: double
             * @description Amount lost to cap overflow
             */
            overflowAmountLost?: number | null;
            /**
             * Format: double
             * @description Amount limited by earn cap
             */
            earnCapAmountLimited?: number | null;
            /** @description Free-form metadata */
            metadata?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Custom affordance definition for novel scenarios */
        CustomAffordance: {
            /** @description Human-readable description of this affordance */
            description?: string | null;
            /**
             * @description Required criteria. Object types, property constraints.
             *     Example: { "objectTypes": ["boulder"], "cover_rating": { "min": 0.5 } }
             */
            requires?: {
                [key: string]: unknown;
            } | null;
            /**
             * @description Preferred criteria (boost score but not required).
             *     Example: { "elevation": { "prefer_higher": true } }
             */
            prefers?: {
                [key: string]: unknown;
            } | null;
            /**
             * @description Exclusion criteria. Reject candidates matching these.
             *     Example: { "hazards": true, "contested": true }
             */
            excludes?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Custom script injection configuration for adding JavaScript to pages */
        CustomScripts: {
            /** @description Custom scripts to inject in the HTML head */
            head?: string | null;
            /** @description Custom scripts to inject at the start of the body */
            bodyStart?: string | null;
            /** @description Custom scripts to inject at the end of the body */
            bodyEnd?: string | null;
        };
        /** @description Request to debit currency from a wallet */
        DebitCurrencyRequest: {
            /**
             * Format: uuid
             * @description Source wallet ID
             */
            walletId: string;
            /**
             * Format: uuid
             * @description Currency to debit
             */
            currencyDefinitionId: string;
            /**
             * Format: double
             * @description Amount to debit (must be positive)
             */
            amount: number;
            /** @description Must be a sink type (burn, vendor_purchase, fee, etc.) */
            transactionType: components["schemas"]["TransactionType"];
            /** @description What triggered this transaction */
            referenceType?: string | null;
            /**
             * Format: uuid
             * @description Reference entity ID
             */
            referenceId?: string | null;
            /** @description Unique key to prevent duplicate processing */
            idempotencyKey: string;
            /** @description Override negative balance allowance for this transaction */
            allowNegative?: boolean | null;
            /** @description Free-form transaction metadata */
            metadata?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Result of debit operation */
        DebitCurrencyResponse: {
            /** @description Created transaction record */
            transaction: components["schemas"]["CurrencyTransactionRecord"];
            /**
             * Format: double
             * @description Balance after debit
             */
            newBalance: number;
        };
        /** @description Request to decline a formed match */
        DeclineMatchRequest: {
            /**
             * Format: uuid
             * @description WebSocket session ID
             */
            webSocketSessionId: string;
            /**
             * Format: uuid
             * @description Account ID of the player
             */
            accountId: string;
            /**
             * Format: uuid
             * @description ID of the match to decline
             */
            matchId: string;
        };
        /** @description Request to delete an achievement */
        DeleteAchievementDefinitionRequest: {
            /**
             * Format: uuid
             * @description ID of the game service
             */
            gameServiceId: string;
            /** @description ID of the achievement to delete */
            achievementId: string;
        };
        /** @description Request to delete an actor template */
        DeleteActorTemplateRequest: {
            /**
             * Format: uuid
             * @description ID of the template to delete
             */
            templateId: string;
            /**
             * @description If true, stops all running actors using this template
             * @default false
             */
            forceStopActors: boolean;
        };
        /** @description Response confirming template deletion */
        DeleteActorTemplateResponse: {
            /** @description Whether the template was successfully deleted */
            deleted: boolean;
            /** @description Number of running actors that were stopped */
            stoppedActorCount: number;
        };
        /** @description Request to delete a bundle */
        DeleteBundleRequest: {
            /** @description Human-readable bundle identifier to delete */
            bundleId: string;
            /**
             * @description If true, permanently delete (admin only). If false, soft-delete.
             * @default false
             */
            permanent: boolean;
            /** @description Optional reason for deletion (recorded in version history) */
            reason?: string | null;
        };
        /** @description Result of bundle deletion */
        DeleteBundleResponse: {
            /** @description Human-readable bundle identifier that was deleted */
            bundleId: string;
            /**
             * @description Deletion status
             * @enum {string}
             */
            status: "deleted" | "permanently_deleted";
            /**
             * Format: date-time
             * @description When the bundle was deleted
             */
            deletedAt: string;
            /**
             * Format: date-time
             * @description When soft-deleted bundle will be permanently removed (null for permanent deletes)
             */
            retentionUntil?: string | null;
        };
        /** @description Request to delete a leaderboard */
        DeleteLeaderboardDefinitionRequest: {
            /**
             * Format: uuid
             * @description ID of the game service
             */
            gameServiceId: string;
            /** @description ID of the leaderboard to delete */
            leaderboardId: string;
        };
        /** @description Request to delete a scene */
        DeleteSceneRequest: {
            /**
             * Format: uuid
             * @description ID of the scene to delete
             */
            sceneId: string;
            /** @description Optional reason for deletion (included in event) */
            reason?: string | null;
        };
        /** @description Response confirming scene deletion */
        DeleteSceneResponse: {
            /** @description Whether the scene was successfully deleted */
            deleted: boolean;
            /**
             * Format: uuid
             * @description ID of the deleted scene
             */
            sceneId?: string;
            /** @description If deletion failed, IDs of scenes that reference this one */
            referencingScenes?: string[] | null;
        };
        /** @description Request to permanently delete a save slot and all its versions */
        DeleteSlotRequest: {
            /** @description Game identifier for namespace isolation */
            gameId: string;
            /**
             * Format: uuid
             * @description ID of the owning entity
             */
            ownerId: string;
            /** @description Type of entity that owns this save slot */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Slot name */
            slotName: string;
        };
        /** @description Result of a slot deletion operation with cleanup statistics */
        DeleteSlotResponse: {
            /** @description Whether slot was deleted */
            deleted: boolean;
            /** @description Number of versions deleted */
            versionsDeleted: number;
            /**
             * Format: int64
             * @description Storage freed in bytes
             */
            bytesFreed: number;
        };
        /** @description Request to permanently delete a specific save version */
        DeleteVersionRequest: {
            /**
             * Format: uuid
             * @description ID of the owning entity
             */
            ownerId: string;
            /** @description Type of entity that owns this save slot */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Slot name */
            slotName: string;
            /** @description Version to delete */
            versionNumber: number;
        };
        /** @description Result of a version deletion operation with storage freed */
        DeleteVersionResponse: {
            /** @description Whether version was deleted */
            deleted: boolean;
            /**
             * Format: int64
             * @description Storage freed in bytes
             */
            bytesFreed: number;
        };
        /**
         * @description Algorithm used for delta computation.
         *     JSON_PATCH: RFC 6902, best for structured JSON data
         *     BSDIFF: Binary diff, good for general binary data
         *     XDELTA: RFC 3284 VCDIFF, efficient for large binary files
         * @default JSON_PATCH
         * @enum {string}
         */
        DeltaAlgorithm: "JSON_PATCH" | "BSDIFF" | "XDELTA";
        /** @description Request to deposit assets into an escrow */
        DepositRequest: {
            /**
             * Format: uuid
             * @description Escrow ID
             */
            escrowId: string;
            /**
             * Format: uuid
             * @description Party depositing
             */
            partyId: string;
            /** @description Type of entity (Account, Character, etc.) */
            partyType: components["schemas"]["EntityType"];
            /** @description Assets to deposit */
            assets: components["schemas"]["EscrowAssetBundleInput"];
            /** @description Deposit token (required for full_consent) */
            depositToken?: string | null;
            /** @description Idempotency key */
            idempotencyKey: string;
        };
        /** @description Response from depositing assets into an escrow */
        DepositResponse: {
            /** @description Updated escrow agreement */
            escrow: components["schemas"]["EscrowAgreement"];
            /** @description Deposit record */
            deposit: components["schemas"]["EscrowDeposit"];
            /** @description Whether escrow is now fully funded */
            fullyFunded: boolean;
            /** @description Release tokens (issued when fully funded) */
            releaseTokens: components["schemas"]["PartyToken"][];
        };
        /** @description Request to destroy an item instance */
        DestroyItemInstanceRequest: {
            /**
             * Format: uuid
             * @description Instance ID to destroy
             */
            instanceId: string;
            /** @description Reason for destruction (consumed, destroyed, expired, admin) */
            reason: string;
        };
        /** @description Response after destroying an item instance */
        DestroyItemInstanceResponse: {
            /** @description Whether destruction was successful */
            destroyed: boolean;
            /**
             * Format: uuid
             * @description Destroyed instance ID
             */
            instanceId: string;
            /**
             * Format: uuid
             * @description Template of destroyed instance
             */
            templateId: string;
        };
        /** @description Information about the client device used for authentication or session tracking */
        DeviceInfo: {
            /**
             * @description Category of the device
             * @enum {string|null}
             */
            deviceType?: "desktop" | "mobile" | "tablet" | "console" | null;
            /** @description Operating system or platform name */
            platform?: string | null;
            /** @description Browser name and version if applicable */
            browser?: string | null;
            /** @description Version of the client application */
            appVersion?: string | null;
        };
        /** @description Request to discard checkout */
        DiscardRequest: {
            /**
             * Format: uuid
             * @description Scene to discard changes for
             */
            sceneId: string;
            /** @description Checkout token */
            checkoutToken: string;
        };
        /** @description Response confirming discard */
        DiscardResponse: {
            /** @description Whether discard was successful */
            discarded: boolean;
        };
        /** @description Request to raise a dispute on a funded escrow */
        DisputeRequest: {
            /**
             * Format: uuid
             * @description Escrow ID
             */
            escrowId: string;
            /**
             * Format: uuid
             * @description Party raising dispute
             */
            partyId: string;
            /** @description Type of entity (Account, Character, etc.) */
            partyType: components["schemas"]["EntityType"];
            /** @description Reason for dispute */
            reason: string;
            /** @description Release token (proves party identity) */
            releaseToken?: string | null;
            /** @description Idempotency key */
            idempotencyKey: string;
        };
        /** @description Response from raising a dispute on an escrow */
        DisputeResponse: {
            /** @description Disputed escrow agreement */
            escrow: components["schemas"]["EscrowAgreement"];
        };
        /** @description Record of an asset distribution */
        DistributionRecord: {
            /** @description Clause that was executed */
            clauseId: string;
            /** @description Type of clause (fee, distribution) */
            clauseType: string;
            /** @description Type of asset (currency, item) */
            assetType: string;
            /** @description Amount transferred */
            amount: number;
            /**
             * Format: uuid
             * @description Source wallet ID (for currency)
             */
            sourceWalletId?: string | null;
            /**
             * Format: uuid
             * @description Destination wallet ID (for currency)
             */
            destinationWalletId?: string | null;
            /**
             * Format: uuid
             * @description Source container ID (for items)
             */
            sourceContainerId?: string | null;
            /**
             * Format: uuid
             * @description Destination container ID (for items)
             */
            destinationContainerId?: string | null;
        };
        /** @description Complete document with all metadata and content */
        Document: {
            /**
             * Format: uuid
             * @description Unique identifier of the document
             */
            documentId: string;
            /** @description Namespace the document belongs to */
            namespace: string;
            /** @description URL-friendly unique identifier */
            slug: string;
            /** @description Display title of the document */
            title: string;
            /** @description Category for organizing the document */
            category: components["schemas"]["DocumentCategory"];
            /** @description Full markdown content of the document */
            content?: string;
            /** @description Brief text summary of the document */
            summary?: string | null;
            /** @description Concise summary optimized for voice AI */
            voiceSummary?: string | null;
            /** @description Tags for filtering and search */
            tags?: string[];
            /** @description IDs of related documents */
            relatedDocuments?: string[];
            /** @description Custom metadata key-value pairs */
            metadata?: {
                [key: string]: unknown;
            };
            /**
             * Format: date-time
             * @description Timestamp when the document was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Timestamp when the document was last updated
             */
            updatedAt: string;
        };
        /**
         * @description Fixed categories for type-safe filtering
         * @enum {string}
         */
        DocumentCategory: "getting-started" | "api-reference" | "architecture" | "deployment" | "troubleshooting" | "tutorials" | "game-systems" | "world-lore" | "npc-ai" | "other";
        /** @description Search result with relevance scoring and match highlights */
        DocumentResult: {
            /**
             * Format: uuid
             * @description Unique identifier of the document
             */
            documentId: string;
            /** @description URL-friendly unique identifier */
            slug: string;
            /** @description Display title of the document */
            title: string;
            /** @description Category of the document */
            category?: components["schemas"]["DocumentCategory"];
            /** @description Brief text summary of the document */
            summary?: string | null;
            /** @description Concise summary optimized for voice AI */
            voiceSummary?: string | null;
            /** @description Full document content if requested */
            content?: string | null;
            /**
             * Format: float
             * @description Relevance score from 0.0 to 1.0
             */
            relevanceScore: number;
            /** @description Text snippets showing where matches occurred */
            matchHighlights?: string[];
        };
        /** @description Lightweight document representation for listings and references */
        DocumentSummary: {
            /**
             * Format: uuid
             * @description Unique identifier of the document
             */
            documentId: string;
            /** @description URL-friendly unique identifier */
            slug: string;
            /** @description Display title of the document */
            title: string;
            /** @description Category of the document */
            category: components["schemas"]["DocumentCategory"];
            /** @description Brief text summary of the document */
            summary?: string | null;
            /** @description Concise summary optimized for voice AI */
            voiceSummary?: string | null;
            /** @description Tags associated with the document */
            tags?: string[];
        };
        /** @description Download details for a specific game client version and platform */
        DownloadInfo: {
            /** @description Target platform for this download */
            platform: components["schemas"]["Platform"];
            /** @description Version number of the game client */
            version: string;
            /**
             * Format: uri
             * @description Download URL for the game client
             */
            url: string;
            /** @description File size in bytes */
            size: number;
            /** @description SHA256 checksum */
            checksum: string;
            /** @description Release notes or changelog for this version */
            releaseNotes?: string | null;
            /** @description Minimum system requirements for the client */
            minimumRequirements?: {
                [key: string]: unknown;
            };
        };
        /** @description Collection of available game client downloads by platform */
        DownloadsResponse: {
            /** @description Available game client downloads */
            clients: components["schemas"]["DownloadInfo"][];
        };
        /** @description Request to duplicate a scene */
        DuplicateSceneRequest: {
            /**
             * Format: uuid
             * @description Scene to duplicate
             */
            sourceSceneId: string;
            /** @description Name for the duplicate */
            newName: string;
            /** @description Optional different game ID */
            newGameId?: string | null;
            /** @description Optional different scene type */
            newSceneType?: components["schemas"]["SceneType"];
        };
        /** @description Current earn cap status for a balance */
        EarnCapInfo: {
            /**
             * Format: double
             * @description Amount earned today
             */
            dailyEarned: number;
            /**
             * Format: double
             * @description Remaining daily earn allowance
             */
            dailyRemaining: number;
            /**
             * Format: date-time
             * @description When daily counter resets
             */
            dailyResetsAt: string;
            /**
             * Format: double
             * @description Amount earned this week
             */
            weeklyEarned: number;
            /**
             * Format: double
             * @description Remaining weekly earn allowance
             */
            weeklyRemaining: number;
            /**
             * Format: date-time
             * @description When weekly counter resets
             */
            weeklyResetsAt: string;
        };
        /**
         * @description How the encounter emotionally affected the character
         * @enum {string}
         */
        EmotionalImpact: "GRATITUDE" | "ANGER" | "FEAR" | "RESPECT" | "CONTEMPT" | "AFFECTION" | "RIVALRY" | "INDIFFERENCE" | "GUILT" | "PRIDE";
        /** @description 6-dimensional emotional state input (all values 0-1) */
        EmotionalStateInput: {
            /** @description Tension level (0=resolved, 1=maximum tension) */
            tension?: number | null;
            /** @description Brightness level (0=dark, 1=bright) */
            brightness?: number | null;
            /** @description Energy level (0=calm, 1=energetic) */
            energy?: number | null;
            /** @description Warmth level (0=distant, 1=intimate) */
            warmth?: number | null;
            /** @description Stability level (0=unstable, 1=grounded) */
            stability?: number | null;
            /** @description Valence level (0=negative, 1=positive) */
            valence?: number | null;
        };
        /** @description Snapshot of emotional state at a specific point in the composition */
        EmotionalStateSnapshot: {
            /** @description Bar number where this snapshot was taken */
            bar: number;
            /** @description Name of the narrative phase at this point */
            phaseName?: string | null;
            /** @description Tension level (0-1) */
            tension: number;
            /** @description Brightness level (0-1) */
            brightness: number;
            /** @description Energy level (0-1) */
            energy: number;
            /** @description Warmth level (0-1) */
            warmth?: number;
            /** @description Stability level (0-1) */
            stability?: number;
            /** @description Valence level (0-1) */
            valence?: number;
        };
        /** @description Paginated list of encounters */
        EncounterListResponse: {
            /** @description List of encounters with perspectives */
            encounters: components["schemas"]["EncounterResponse"][];
            /** @description Total matching encounters */
            totalCount: number;
            /** @description Current page (1-based) */
            page: number;
            /** @description Results per page */
            pageSize: number;
            /** @description Whether more results exist */
            hasNextPage?: boolean;
            /** @description Whether previous results exist */
            hasPreviousPage?: boolean;
        };
        /** @description Core encounter record representing a memorable interaction */
        EncounterModel: {
            /**
             * Format: uuid
             * @description Unique identifier for this encounter
             */
            encounterId: string;
            /**
             * Format: date-time
             * @description In-game time when the encounter occurred
             */
            timestamp: string;
            /**
             * Format: uuid
             * @description Realm where the encounter took place
             */
            realmId: string;
            /**
             * Format: uuid
             * @description Specific location within the realm (optional)
             */
            locationId?: string | null;
            /** @description Type code for this encounter (e.g., COMBAT, TRADE) */
            encounterTypeCode: string;
            /** @description What triggered or contextualized the encounter */
            context?: string | null;
            /** @description Outcome of the encounter (POSITIVE, NEGATIVE, NEUTRAL, MEMORABLE, TRANSFORMATIVE) */
            outcome: components["schemas"]["EncounterOutcome"];
            /** @description All character IDs involved in the encounter */
            participantIds: string[];
            /** @description Additional encounter-specific data */
            metadata?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: date-time
             * @description System timestamp when record was created
             */
            createdAt: string;
        };
        /**
         * @description Overall outcome of an encounter
         * @enum {string}
         */
        EncounterOutcome: "POSITIVE" | "NEGATIVE" | "NEUTRAL" | "MEMORABLE" | "TRANSFORMATIVE";
        /** @description A character's individual perspective on an encounter */
        EncounterPerspectiveModel: {
            /**
             * Format: uuid
             * @description Unique identifier for this perspective
             */
            perspectiveId: string;
            /**
             * Format: uuid
             * @description Reference to the shared encounter record
             */
            encounterId: string;
            /**
             * Format: uuid
             * @description Character holding this perspective
             */
            characterId: string;
            /** @description Primary emotional response to the encounter */
            emotionalImpact: components["schemas"]["EmotionalImpact"];
            /**
             * Format: float
             * @description Opinion change toward other participants (-1.0 to +1.0)
             */
            sentimentShift?: number | null;
            /**
             * Format: float
             * @description How strongly remembered (0.0-1.0, decays over time)
             */
            memoryStrength: number;
            /** @description Short description from this character's POV */
            rememberedAs?: string | null;
            /**
             * Format: date-time
             * @description When memory decay was last applied
             */
            lastDecayedAt?: string | null;
            /**
             * Format: date-time
             * @description System timestamp when record was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Last modification timestamp
             */
            updatedAt?: string | null;
        };
        /** @description Response containing an encounter with perspectives */
        EncounterResponse: {
            /** @description The shared encounter record */
            encounter: components["schemas"]["EncounterModel"];
            /** @description All perspectives on this encounter */
            perspectives: components["schemas"]["EncounterPerspectiveModel"][];
        };
        /** @description Response containing a list of encounter types */
        EncounterTypeListResponse: {
            /** @description List of encounter types */
            types: components["schemas"]["EncounterTypeResponse"][];
            /** @description Total number of types */
            totalCount: number;
        };
        /** @description Response containing an encounter type */
        EncounterTypeResponse: {
            /**
             * Format: uuid
             * @description Unique identifier
             */
            typeId: string;
            /** @description Unique code */
            code: string;
            /** @description Display name */
            name: string;
            /** @description Description */
            description?: string | null;
            /** @description Whether this is a built-in type */
            isBuiltIn: boolean;
            /** @description Suggested emotional response */
            defaultEmotionalImpact?: components["schemas"]["EmotionalImpact"];
            /** @description Display ordering */
            sortOrder: number;
            /** @description Whether the type is active */
            isActive: boolean;
            /**
             * Format: date-time
             * @description When the type was created
             */
            createdAt: string;
        };
        /** @description Request to end an active encounter */
        EndEncounterRequest: {
            /** @description ID of the Event Brain actor managing the encounter */
            actorId: string;
        };
        /** @description Response after ending an encounter */
        EndEncounterResponse: {
            /** @description ID of the actor */
            actorId: string;
            /**
             * Format: uuid
             * @description ID of the ended encounter
             */
            encounterId: string;
            /** @description Duration of the encounter in milliseconds */
            durationMs?: number | null;
        };
        /**
         * @description How contract breaches are handled
         * @enum {string}
         */
        EnforcementMode: "advisory" | "event_only" | "consequence_based" | "community";
        /**
         * @description Character data with optional enriched fields.
         *     Fields are only populated if the corresponding include flag was set in the request.
         */
        EnrichedCharacterResponse: {
            /**
             * Format: uuid
             * @description Unique identifier for the character
             */
            characterId: string;
            /** @description Display name of the character */
            name: string;
            /**
             * Format: uuid
             * @description Realm ID (partition key)
             */
            realmId: string;
            /**
             * Format: uuid
             * @description Species ID (foreign key to Species service)
             */
            speciesId: string;
            /**
             * Format: date-time
             * @description In-game birth timestamp
             */
            birthDate: string;
            /**
             * Format: date-time
             * @description In-game death timestamp
             */
            deathDate?: string | null;
            /** @description Current lifecycle status of the character */
            status: components["schemas"]["CharacterStatus"];
            /**
             * Format: date-time
             * @description Real-world creation timestamp
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Real-world last update timestamp
             */
            updatedAt?: string | null;
            /** @description Personality traits (included if includePersonality=true) */
            personality?: components["schemas"]["PersonalitySnapshot"];
            /** @description Backstory elements (included if includeBackstory=true) */
            backstory?: components["schemas"]["BackstorySnapshot"];
            /** @description Family relationships (included if includeFamilyTree=true) */
            familyTree?: components["schemas"]["FamilyTreeResponse"];
            /** @description Combat preferences (included if includeCombatPreferences=true) */
            combatPreferences?: components["schemas"]["CombatPreferencesSnapshot"];
        };
        /** @description Entity's rank on a leaderboard */
        EntityRankResponse: {
            /**
             * Format: uuid
             * @description ID of the entity
             */
            entityId: string;
            /** @description Entity type for the ranked entity */
            entityType: components["schemas"]["EntityType"];
            /**
             * Format: double
             * @description Entity's current score
             */
            score: number;
            /**
             * Format: int64
             * @description Entity's current rank (1-based)
             */
            rank: number;
            /**
             * Format: int64
             * @description Total entries on the leaderboard
             */
            totalEntries: number;
            /**
             * Format: double
             * @description Percentile ranking (0-100)
             */
            percentile?: number;
        };
        /**
         * @description Universal entity type identifier used across Bannou services.
         *     Provides first-class support for various kinds of entities in analytics,
         *     achievements, leaderboards, contracts, relationships, and other systems.
         * @enum {string}
         */
        EntityType: "system" | "account" | "character" | "actor" | "guild" | "organization" | "government" | "faction" | "location" | "realm" | "item" | "monster" | "custom" | "other";
        /** @description Standard error response format */
        ErrorResponse: {
            /** @description Error code */
            error: string;
            /** @description Human-readable error message */
            message: string;
            /** @description Additional error details */
            details?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Main escrow agreement record */
        EscrowAgreement: {
            /**
             * Format: uuid
             * @description Unique escrow agreement identifier
             */
            id: string;
            /** @description Type of escrow agreement */
            escrowType: components["schemas"]["EscrowType"];
            /** @description Trust mode for the escrow */
            trustMode: components["schemas"]["EscrowTrustMode"];
            /**
             * Format: uuid
             * @description For single_party_trusted - which party has authority
             */
            trustedPartyId?: string | null;
            /** @description Type of the trusted party */
            trustedPartyType?: components["schemas"]["EntityType"] | null;
            /** @description For initiator_trusted - which service created this */
            initiatorServiceId?: string | null;
            /** @description All parties involved in the escrow */
            parties: components["schemas"]["EscrowParty"][];
            /** @description What deposits are expected from each party */
            expectedDeposits: components["schemas"]["ExpectedDeposit"][];
            /** @description Actual deposits received */
            deposits: components["schemas"]["EscrowDeposit"][];
            /** @description How assets should be distributed on release */
            releaseAllocations?: components["schemas"]["ReleaseAllocation"][] | null;
            /**
             * Format: uuid
             * @description Contract governing conditions for this escrow
             */
            boundContractId?: string | null;
            /** @description Consent decisions from parties */
            consents: components["schemas"]["EscrowConsent"][];
            /** @description Current escrow status */
            status: components["schemas"]["EscrowStatus"];
            /** @description How many parties must consent for release (-1 = all required) */
            requiredConsentsForRelease: number;
            /**
             * Format: date-time
             * @description When the escrow was last validated
             */
            lastValidatedAt?: string | null;
            /** @description Any validation failures detected */
            validationFailures?: components["schemas"]["ValidationFailure"][] | null;
            /**
             * Format: date-time
             * @description When the escrow was created
             */
            createdAt: string;
            /**
             * Format: uuid
             * @description Who created the escrow
             */
            createdBy: string;
            /** @description Type of the creator entity */
            createdByType: components["schemas"]["EntityType"];
            /**
             * Format: date-time
             * @description When all expected deposits were received
             */
            fundedAt?: string | null;
            /**
             * Format: date-time
             * @description Auto-refund if not completed by this time
             */
            expiresAt: string;
            /**
             * Format: date-time
             * @description When the escrow reached terminal state
             */
            completedAt?: string | null;
            /** @description What this escrow is for (e.g., trade, auction, contract) */
            referenceType?: string | null;
            /**
             * Format: uuid
             * @description ID of the referenced entity
             */
            referenceId?: string | null;
            /** @description Human-readable description */
            description?: string | null;
            /** @description Game/application specific metadata */
            metadata?: {
                [key: string]: unknown;
            } | null;
            /** @description How the escrow was resolved */
            resolution?: components["schemas"]["EscrowResolution"];
            /** @description Notes about the resolution */
            resolutionNotes?: string | null;
        };
        /** @description An asset held in escrow */
        EscrowAsset: {
            /** @description Type of asset held in escrow */
            assetType: components["schemas"]["AssetType"];
            /**
             * Format: uuid
             * @description For assetType=currency - currency definition ID
             */
            currencyDefinitionId?: string | null;
            /** @description Denormalized currency code for display */
            currencyCode?: string | null;
            /** @description Amount of currency */
            currencyAmount?: number | null;
            /**
             * Format: uuid
             * @description For assetType=item - unique item instance ID
             */
            itemInstanceId?: string | null;
            /** @description Denormalized item name for display */
            itemName?: string | null;
            /**
             * Format: uuid
             * @description For assetType=item_stack - stackable item template
             */
            itemTemplateId?: string | null;
            /** @description Denormalized template name for display */
            itemTemplateName?: string | null;
            /** @description For assetType=item_stack - quantity */
            itemQuantity?: number | null;
            /**
             * Format: uuid
             * @description For assetType=contract - contract instance ID
             */
            contractInstanceId?: string | null;
            /** @description Denormalized contract template code */
            contractTemplateCode?: string | null;
            /** @description Description of the contract */
            contractDescription?: string | null;
            /** @description Which party role in the contract is being escrowed */
            contractPartyRole?: string | null;
            /** @description For assetType=custom - registered handler type */
            customAssetType?: string | null;
            /** @description Custom asset identifier */
            customAssetId?: string | null;
            /** @description Handler-specific data */
            customAssetData?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: uuid
             * @description Where this asset came from (for refunds)
             */
            sourceOwnerId: string;
            /** @description Type of the source owner */
            sourceOwnerType: components["schemas"]["EntityType"];
            /**
             * Format: uuid
             * @description Source wallet/container ID
             */
            sourceContainerId?: string | null;
        };
        /** @description Groups multiple assets for a single deposit or release */
        EscrowAssetBundle: {
            /**
             * Format: uuid
             * @description Bundle identifier
             */
            bundleId: string;
            /** @description Assets in this bundle */
            assets: components["schemas"]["EscrowAsset"][];
            /** @description Summary for display */
            description?: string | null;
            /** @description Optional valuation for UI display */
            estimatedValue?: number | null;
        };
        /** @description Input for specifying a bundle of assets */
        EscrowAssetBundleInput: {
            /** @description Assets to deposit */
            assets: components["schemas"]["EscrowAssetInput"][];
            /** @description Bundle description */
            description?: string | null;
            /** @description Estimated value */
            estimatedValue?: number | null;
        };
        /** @description Input for specifying an asset in escrow operations */
        EscrowAssetInput: {
            /** @description Type of asset to deposit */
            assetType: components["schemas"]["AssetType"];
            /**
             * Format: uuid
             * @description Currency definition ID
             */
            currencyDefinitionId?: string | null;
            /** @description Currency code */
            currencyCode?: string | null;
            /** @description Currency amount */
            currencyAmount?: number | null;
            /**
             * Format: uuid
             * @description Item instance ID
             */
            itemInstanceId?: string | null;
            /** @description Item name */
            itemName?: string | null;
            /**
             * Format: uuid
             * @description Item template ID (for stacks)
             */
            itemTemplateId?: string | null;
            /** @description Item template name */
            itemTemplateName?: string | null;
            /** @description Item quantity (for stacks) */
            itemQuantity?: number | null;
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractInstanceId?: string | null;
            /** @description Contract template code */
            contractTemplateCode?: string | null;
            /** @description Contract description */
            contractDescription?: string | null;
            /** @description Contract party role being escrowed */
            contractPartyRole?: string | null;
            /** @description Custom asset type */
            customAssetType?: string | null;
            /** @description Custom asset ID */
            customAssetId?: string | null;
            /** @description Custom asset data */
            customAssetData?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Records a party consent decision */
        EscrowConsent: {
            /**
             * Format: uuid
             * @description Party giving consent
             */
            partyId: string;
            /** @description Type of entity (Account, Character, etc.) */
            partyType: components["schemas"]["EntityType"];
            /** @description Type of consent given */
            consentType: components["schemas"]["EscrowConsentType"];
            /**
             * Format: date-time
             * @description When consent was given
             */
            consentedAt: string;
            /** @description Token used (for audit) */
            releaseTokenUsed?: string | null;
            /** @description Optional notes */
            notes?: string | null;
        };
        /**
         * @description Type of consent being given.
         *     - release: Agrees to release assets to recipients
         *     - refund: Agrees to refund assets to depositors
         *     - dispute: Raises a dispute
         *     - reaffirm: Re-affirms after validation failure
         * @enum {string}
         */
        EscrowConsentType: "release" | "refund" | "dispute" | "reaffirm";
        /** @description Records an actual deposit */
        EscrowDeposit: {
            /**
             * Format: uuid
             * @description Deposit record identifier
             */
            id: string;
            /**
             * Format: uuid
             * @description Escrow this deposit belongs to
             */
            escrowId: string;
            /**
             * Format: uuid
             * @description Party who deposited
             */
            partyId: string;
            /** @description Type of entity (Account, Character, etc.) */
            partyType: components["schemas"]["EntityType"];
            /** @description Assets deposited */
            assets: components["schemas"]["EscrowAssetBundle"];
            /**
             * Format: date-time
             * @description When the deposit was made
             */
            depositedAt: string;
            /** @description Token used (for audit) */
            depositTokenUsed?: string | null;
            /** @description Idempotency key for this deposit */
            idempotencyKey: string;
        };
        /** @description Request from lib-escrow to debit wallet for deposit */
        EscrowDepositRequest: {
            /**
             * Format: uuid
             * @description Wallet to debit
             */
            walletId: string;
            /**
             * Format: uuid
             * @description Currency to debit
             */
            currencyDefinitionId: string;
            /**
             * Format: double
             * @description Amount to debit for escrow
             */
            amount: number;
            /**
             * Format: uuid
             * @description Associated escrow agreement ID
             */
            escrowId: string;
            /** @description Idempotency key */
            idempotencyKey: string;
        };
        /** @description Result of escrow deposit (wallet debit) */
        EscrowDepositResponse: {
            /** @description Debit transaction record */
            transaction: components["schemas"]["CurrencyTransactionRecord"];
            /**
             * Format: double
             * @description Wallet balance after debit
             */
            newBalance: number;
        };
        /** @description A party in the escrow agreement */
        EscrowParty: {
            /**
             * Format: uuid
             * @description Party entity identifier
             */
            partyId: string;
            /** @description Type of entity (Account, Character, etc.) */
            partyType: components["schemas"]["EntityType"];
            /** @description Display name for UI/logging */
            displayName?: string | null;
            /** @description Role of this party in the escrow */
            role: components["schemas"]["EscrowPartyRole"];
            /** @description Whether this party consent is required for release */
            consentRequired: boolean;
            /**
             * Format: uuid
             * @description Party own wallet (where currency comes from/returns to)
             */
            walletId?: string | null;
            /**
             * Format: uuid
             * @description Party own container (where items come from/return to)
             */
            containerId?: string | null;
            /**
             * Format: uuid
             * @description Escrow wallet for THIS party deposits (owned by escrow)
             */
            escrowWalletId?: string | null;
            /**
             * Format: uuid
             * @description Escrow container for THIS party deposits (owned by escrow)
             */
            escrowContainerId?: string | null;
            /** @description Token for depositing (full_consent mode) */
            depositToken?: string | null;
            /** @description Whether the deposit token has been used */
            depositTokenUsed: boolean;
            /**
             * Format: date-time
             * @description When the deposit token was used
             */
            depositTokenUsedAt?: string | null;
            /** @description Token for consenting to release */
            releaseToken?: string | null;
            /** @description Whether the release token has been used */
            releaseTokenUsed: boolean;
            /**
             * Format: date-time
             * @description When the release token was used
             */
            releaseTokenUsedAt?: string | null;
        };
        /**
         * @description Role of a party in the escrow.
         *     - depositor: Deposits assets into escrow
         *     - recipient: Receives assets when released
         *     - depositor_recipient: Both deposits and can receive (typical for trades)
         *     - arbiter: Can resolve disputes, does not deposit or receive
         *     - observer: Can view status but cannot act
         * @enum {string}
         */
        EscrowPartyRole: "depositor" | "recipient" | "depositor_recipient" | "arbiter" | "observer";
        /** @description Request from lib-escrow to credit depositor on refund */
        EscrowRefundRequest: {
            /**
             * Format: uuid
             * @description Depositor wallet to credit
             */
            walletId: string;
            /**
             * Format: uuid
             * @description Currency to credit
             */
            currencyDefinitionId: string;
            /**
             * Format: double
             * @description Amount to refund
             */
            amount: number;
            /**
             * Format: uuid
             * @description Associated escrow agreement ID
             */
            escrowId: string;
            /** @description Idempotency key */
            idempotencyKey: string;
        };
        /** @description Result of escrow refund (depositor credit) */
        EscrowRefundResponse: {
            /** @description Credit transaction record */
            transaction: components["schemas"]["CurrencyTransactionRecord"];
            /**
             * Format: double
             * @description Depositor balance after credit
             */
            newBalance: number;
        };
        /** @description Request from lib-escrow to credit recipient on completion */
        EscrowReleaseRequest: {
            /**
             * Format: uuid
             * @description Recipient wallet to credit
             */
            walletId: string;
            /**
             * Format: uuid
             * @description Currency to credit
             */
            currencyDefinitionId: string;
            /**
             * Format: double
             * @description Amount to credit
             */
            amount: number;
            /**
             * Format: uuid
             * @description Associated escrow agreement ID
             */
            escrowId: string;
            /** @description Idempotency key */
            idempotencyKey: string;
        };
        /** @description Result of escrow release (recipient credit) */
        EscrowReleaseResponse: {
            /** @description Credit transaction record */
            transaction: components["schemas"]["CurrencyTransactionRecord"];
            /**
             * Format: double
             * @description Recipient balance after credit
             */
            newBalance: number;
        };
        /**
         * @description How the escrow was resolved.
         *     - released: Assets went to designated recipients
         *     - refunded: Assets returned to depositors
         *     - split: Arbiter split assets between parties
         *     - expired_refunded: Timed out, auto-refunded
         *     - cancelled_refunded: Cancelled, deposits refunded
         *     - violation_refunded: Validation failure caused refund
         * @enum {string}
         */
        EscrowResolution: "released" | "refunded" | "split" | "expired_refunded" | "cancelled_refunded" | "violation_refunded";
        /**
         * @description Current status of the escrow agreement.
         *     - pending_deposits: Waiting for parties to deposit
         *     - partially_funded: Some but not all deposits received
         *     - funded: All deposits received, awaiting consent/condition
         *     - pending_consent: Some consents received, waiting for more
         *     - pending_condition: Waiting for contract fulfillment or external verification
         *     - finalizing: Running contract finalizer prebound APIs (transient)
         *     - releasing: Release in progress (transient)
         *     - released: Assets transferred to recipients
         *     - refunding: Refund in progress (transient)
         *     - refunded: Assets returned to depositors
         *     - disputed: In dispute, arbiter must resolve
         *     - expired: Timed out without completion
         *     - cancelled: Cancelled before funding complete
         *     - validation_failed: Held assets changed, awaiting re-affirmation
         * @enum {string}
         */
        EscrowStatus: "pending_deposits" | "partially_funded" | "funded" | "pending_consent" | "pending_condition" | "finalizing" | "releasing" | "released" | "refunding" | "refunded" | "disputed" | "expired" | "cancelled" | "validation_failed";
        /**
         * @description Trust model for the escrow agreement.
         *     - full_consent: All parties must explicitly consent using tokens
         *     - initiator_trusted: The service that created the escrow can complete unilaterally
         *     - single_party_trusted: A designated party can complete unilaterally
         * @enum {string}
         */
        EscrowTrustMode: "full_consent" | "initiator_trusted" | "single_party_trusted";
        /**
         * @description Type of escrow agreement.
         *     - two_party: Simple trade escrow between Party A and Party B
         *     - multi_party: N parties with complex deposit/receive rules
         *     - conditional: Release based on external condition or contract fulfillment
         *     - auction: Winner-takes-all with refunds to losers
         * @enum {string}
         */
        EscrowType: "two_party" | "multi_party" | "conditional" | "auction";
        /**
         * @description Categories of historical events that characters can participate in
         * @enum {string}
         */
        EventCategory: "WAR" | "NATURAL_DISASTER" | "POLITICAL" | "ECONOMIC" | "RELIGIOUS" | "CULTURAL" | "PERSONAL";
        /** @description Request to execute contract clauses */
        ExecuteContractRequest: {
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractInstanceId: string;
            /** @description Idempotency key for the execution */
            idempotencyKey?: string | null;
        };
        /** @description Response from executing a contract */
        ExecuteContractResponse: {
            /** @description Whether execution was successful */
            executed: boolean;
            /** @description True if this was a repeat call (idempotency) */
            alreadyExecuted: boolean;
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractId?: string;
            /** @description Records of what was moved where */
            distributions?: components["schemas"]["DistributionRecord"][] | null;
            /**
             * Format: date-time
             * @description When execution occurred
             */
            executedAt?: string | null;
        };
        /** @description Request to execute a currency conversion */
        ExecuteConversionRequest: {
            /**
             * Format: uuid
             * @description Wallet to perform conversion in
             */
            walletId: string;
            /**
             * Format: uuid
             * @description Currency to debit
             */
            fromCurrencyId: string;
            /**
             * Format: uuid
             * @description Currency to credit
             */
            toCurrencyId: string;
            /**
             * Format: double
             * @description Amount to convert
             */
            fromAmount: number;
            /** @description Unique key for idempotency */
            idempotencyKey: string;
        };
        /** @description Result of conversion execution */
        ExecuteConversionResponse: {
            /** @description Debit transaction (conversion_debit) */
            debitTransaction: components["schemas"]["CurrencyTransactionRecord"];
            /** @description Credit transaction (conversion_credit) */
            creditTransaction: components["schemas"]["CurrencyTransactionRecord"];
            /**
             * Format: double
             * @description Amount debited
             */
            fromDebited: number;
            /**
             * Format: double
             * @description Amount credited
             */
            toCredited: number;
            /**
             * Format: double
             * @description Rate applied
             */
            effectiveRate: number;
        };
        /** @description Metadata about behavior execution requirements including timing, resources, and interrupt conditions */
        ExecutionMetadata: {
            /** @description Estimated execution time in seconds */
            estimatedDuration?: number;
            /** @description Resource requirements for behavior execution */
            resourceRequirements?: {
                [key: string]: number;
            } | null;
            /** @description Conditions that can interrupt behavior execution */
            interruptConditions?: string[] | null;
        };
        /** @description Defines what a party should deposit */
        ExpectedDeposit: {
            /**
             * Format: uuid
             * @description Party who should deposit
             */
            partyId: string;
            /** @description Type of depositing party */
            partyType: components["schemas"]["EntityType"];
            /** @description Expected assets from this party */
            expectedAssets: components["schemas"]["EscrowAsset"][];
            /** @description Is this deposit optional */
            optional: boolean;
            /**
             * Format: date-time
             * @description Deadline for this specific deposit
             */
            depositDeadline?: string | null;
            /** @description Has this party fulfilled their deposit requirement */
            fulfilled: boolean;
        };
        /** @description Input for defining expected deposits from a party */
        ExpectedDepositInput: {
            /**
             * Format: uuid
             * @description Party who should deposit
             */
            partyId: string;
            /** @description Type of depositing party */
            partyType: components["schemas"]["EntityType"];
            /** @description Expected assets */
            expectedAssets: components["schemas"]["EscrowAssetInput"][];
            /** @description Is this deposit optional */
            optional?: boolean | null;
            /**
             * Format: date-time
             * @description Specific deadline for this deposit
             */
            depositDeadline?: string | null;
        };
        /**
         * @description How currency expiration is determined
         * @enum {string}
         */
        ExpirationPolicy: "fixed_date" | "duration_from_earn" | "end_of_season";
        /** @description Request to export all saves for an owner to a downloadable archive */
        ExportSavesRequest: {
            /** @description Game identifier for namespace isolation */
            gameId: string;
            /**
             * Format: uuid
             * @description Entity ID that owns the saves to export
             */
            ownerId: string;
            /** @description Type of entity that owns the saves to export */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Specific slots to export (all if null) */
            slotNames?: string[];
        };
        /** @description Response with pre-signed URL for downloading exported save archive */
        ExportSavesResponse: {
            /**
             * Format: uri
             * @description Pre-signed URL to download export archive
             */
            downloadUrl: string;
            /**
             * Format: date-time
             * @description When the download URL expires
             */
            expiresAt: string;
            /**
             * Format: int64
             * @description Archive size
             */
            sizeBytes: number;
        };
        /** @description Request to fail a milestone */
        FailMilestoneRequest: {
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractId: string;
            /** @description Milestone that failed */
            milestoneCode: string;
            /** @description Reason for failure */
            reason?: string | null;
        };
        /** @description Reference to a family member */
        FamilyMember: {
            /**
             * Format: uuid
             * @description ID of the related character
             */
            characterId: string;
            /** @description Display name (if available) */
            name?: string | null;
            /** @description Specific relationship type (MOTHER, FATHER, SON, DAUGHTER, etc.) */
            relationshipType: string;
            /** @description Whether the related character is alive */
            isAlive?: boolean;
        };
        /** @description Family relationships for a character */
        FamilyTreeResponse: {
            /** @description Parent relationships (biological and adoptive) */
            parents?: components["schemas"]["FamilyMember"][];
            /** @description Child relationships */
            children?: components["schemas"]["FamilyMember"][];
            /** @description Sibling relationships (including half-siblings) */
            siblings?: components["schemas"]["FamilyMember"][];
            /** @description Current spouse (if any) */
            spouse?: components["schemas"]["FamilyMember"];
            /** @description Previous incarnations (if reincarnation tracked) */
            pastLives?: components["schemas"]["PastLifeReference"][];
        };
        /** @description Result from a contract finalizer API call */
        FinalizerResult: {
            /** @description Finalizer endpoint */
            endpoint: string;
            /** @description Whether it succeeded */
            success: boolean;
            /** @description Error message if failed */
            error?: string | null;
        };
        /** @description Request to find asset usage */
        FindAssetUsageRequest: {
            /**
             * Format: uuid
             * @description Asset ID to find usage of
             */
            assetId: string;
            /** @description Optional game filter */
            gameId?: string | null;
        };
        /** @description Scenes using the asset */
        FindAssetUsageResponse: {
            /** @description Asset usage instances */
            usages: components["schemas"]["AssetUsageInfo"][];
        };
        /** @description Request to find referencing scenes */
        FindReferencesRequest: {
            /**
             * Format: uuid
             * @description Scene ID to find references to
             */
            sceneId: string;
        };
        /** @description Scenes that reference the target */
        FindReferencesResponse: {
            /** @description Scenes containing references */
            referencingScenes: components["schemas"]["ReferenceInfo"][];
        };
        /** @description Request to find space for item */
        FindSpaceRequest: {
            /**
             * Format: uuid
             * @description Owner to search
             */
            ownerId: string;
            /** @description Owner type */
            ownerType: components["schemas"]["ContainerOwnerType"];
            /**
             * Format: uuid
             * @description Item template
             */
            templateId: string;
            /**
             * Format: double
             * @description Quantity to place
             * @default 1
             */
            quantity: number;
            /**
             * @description Prefer existing stacks
             * @default true
             */
            preferStackable: boolean;
        };
        /** @description Find space result */
        FindSpaceResponse: {
            /** @description Whether space found */
            hasSpace: boolean;
            /** @description Potential placements */
            candidates: components["schemas"]["SpaceCandidate"][];
        };
        /** @description A musical form structure template */
        FormTemplate: {
            /** @description Form name (e.g., "AABB", "verse-chorus") */
            name: string;
            /** @description Section sequence (e.g., ["A", "A", "B", "B"]) */
            sections: string[];
            /**
             * @description Default bars per section
             * @default 8
             */
            barsPerSection: number;
        };
        /** @description Request to perform a game action such as movement or combat */
        GameActionRequest: {
            /**
             * Format: uuid
             * @description WebSocket session ID of the client. Provided by shortcut system.
             */
            sessionId: string;
            /**
             * Format: uuid
             * @description Account ID of the player. Provided by shortcut system.
             */
            accountId: string;
            /** @description Game type for the action. Determines which lobby to apply the action. Provided by shortcut system. */
            gameType: string;
            /** @description Type of game action to perform */
            actionType: components["schemas"]["GameActionType"];
            /** @description Action-specific data */
            actionData?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: uuid
             * @description Target of the action (if applicable)
             */
            targetId?: string | null;
        };
        /** @description Response indicating the result of a game action with any state changes */
        GameActionResponse: {
            /**
             * Format: uuid
             * @description Unique identifier for this action instance
             */
            actionId: string;
            /** @description Action result data */
            result?: {
                [key: string]: unknown;
            } | null;
            /** @description Updated game state (if applicable) */
            newGameState?: {
                [key: string]: unknown;
            } | null;
        };
        /**
         * @description Type of game action
         * @enum {string}
         */
        GameActionType: "move" | "interact" | "attack" | "cast_spell" | "use_item";
        /** @description Information about a player currently participating in a game session */
        GamePlayer: {
            /**
             * Format: uuid
             * @description Unique identifier for the player's account
             */
            accountId: string;
            /**
             * Format: uuid
             * @description WebSocket session ID that joined the game. Chat and events are delivered to this specific session only.
             */
            sessionId: string;
            /** @description Display name shown to other players */
            displayName?: string | null;
            /** @description Role of the player in the game session */
            role: components["schemas"]["PlayerRole"];
            /**
             * Format: date-time
             * @description Timestamp when the player joined the session
             */
            joinedAt: string;
            /** @description Game-specific character data for this player (null if none provided) */
            characterData?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: uuid
             * @description Voice participant session ID (if player has joined voice)
             */
            voiceSessionId?: string | null;
        };
        /**
         * @description Realm stub name (lowercase string identifier) that this asset belongs to.
         *     Use the realm's stub_name property (e.g., "realm-1", "realm-2") from the Realm service.
         *     Use "shared" for assets that are available across all realms.
         */
        GameRealm: string;
        /** @description Complete details of a game session including players and settings */
        GameSessionResponse: {
            /**
             * Format: uuid
             * @description Unique identifier for the game session
             */
            sessionId: string;
            /** @description Type of game for this session */
            gameType: components["schemas"]["GameType"];
            /** @description Type of session - lobby or matchmade */
            sessionType: components["schemas"]["SessionType"];
            /** @description Display name for the session */
            sessionName?: string | null;
            /** @description Current status of the game session */
            status: components["schemas"]["SessionStatus"];
            /** @description Maximum number of players allowed in the session */
            maxPlayers?: number;
            /** @description Current number of players in the session */
            currentPlayers?: number;
            /** @description Whether the session requires a password to join */
            isPrivate?: boolean;
            /**
             * Format: uuid
             * @description Account ID of the session owner
             */
            owner?: string;
            /** @description List of players currently in the session */
            players?: components["schemas"]["GamePlayer"][];
            /**
             * Format: date-time
             * @description Timestamp when the session was created
             */
            createdAt: string;
            /** @description Game-specific configuration settings */
            gameSettings?: {
                [key: string]: unknown;
            } | null;
            /** @description For matchmade sessions - reservation tokens for expected players */
            reservations?: components["schemas"]["ReservationInfo"][] | null;
            /**
             * Format: date-time
             * @description For matchmade sessions - when reservations expire
             */
            reservationExpiresAt?: string | null;
        };
        /** @description Game service stub name for this session. Use the game service's stubName property (e.g., "my-game"). Use "generic" for non-game-specific sessions. */
        GameType: string;
        /** @description Request to generate a complete musical composition */
        GenerateCompositionRequest: {
            /**
             * @description ID of the style to use for generation
             * @example celtic
             */
            styleId: string;
            /**
             * @description Target duration in bars
             * @default 32
             */
            durationBars: number;
            /** @description Key signature (random if not specified) */
            key?: components["schemas"]["KeySignature"];
            /** @description Tempo in BPM (uses style default if not specified) */
            tempo?: number;
            /**
             * @description Mood constraint for generation
             * @enum {string|null}
             */
            mood?: "bright" | "dark" | "neutral" | "melancholic" | "triumphant" | null;
            /**
             * @description Style-specific tune type (e.g., "reel", "jig" for Celtic)
             * @example reel
             */
            tuneType?: string | null;
            /** @description Random seed for reproducible generation */
            seed?: number | null;
            /**
             * @description Narrative/emotional arc options for storyteller-driven composition.
             *     If omitted, narrative is inferred from mood. When provided, enables
             *     fine-grained control over emotional journey and tension curves.
             */
            narrative?: components["schemas"]["NarrativeOptions"];
        };
        /** @description Response containing the generated composition */
        GenerateCompositionResponse: {
            /** @description Unique identifier for the composition */
            compositionId: string;
            /** @description Generated MIDI-JSON output */
            midiJson: components["schemas"]["MidiJson"];
            /** @description Metadata about the generation */
            metadata?: components["schemas"]["CompositionMetadata"];
            /** @description Time taken to generate in milliseconds */
            generationTimeMs?: number;
            /** @description ID of the narrative template used for composition */
            narrativeUsed?: string | null;
            /** @description Emotional state at each section boundary */
            emotionalJourney?: components["schemas"]["EmotionalStateSnapshot"][] | null;
            /** @description Tension values at bar boundaries (0-1 scale) */
            tensionCurve?: number[] | null;
        };
        /** @description Request to generate a melody over harmony */
        GenerateMelodyRequest: {
            /** @description Chord progression to generate melody over */
            harmony: components["schemas"]["ChordEvent"][];
            /** @description Style for melodic preferences */
            styleId?: string | null;
            /** @description Pitch range for the melody */
            range?: components["schemas"]["PitchRange"];
            /**
             * @description Overall melodic contour
             * @enum {string|null}
             */
            contour?: "arch" | "wave" | "ascending" | "descending" | "static" | null;
            /**
             * Format: float
             * @description Note density (0=sparse, 1=dense)
             */
            rhythmDensity?: number | null;
            /**
             * Format: float
             * @description Amount of syncopation
             */
            syncopation?: number | null;
            /** @description Random seed for reproducibility */
            seed?: number | null;
        };
        /** @description Response containing a generated melody */
        GenerateMelodyResponse: {
            /** @description Generated note events */
            notes: components["schemas"]["NoteEvent"][];
            /** @description Analysis of the melody */
            analysis?: components["schemas"]["MelodyAnalysis"];
        };
        /** @description Request to generate a chord progression */
        GenerateProgressionRequest: {
            /** @description Key for the progression */
            key: components["schemas"]["KeySignature"];
            /** @description Number of chords in the progression */
            length: number;
            /** @description Style to use for harmonic preferences */
            styleId?: string | null;
            /** @description Starting chord (roman numeral, e.g., "I") */
            startChord?: string | null;
            /** @description Ending chord (roman numeral, e.g., "I") */
            endChord?: string | null;
            /**
             * @description Cadence type for ending
             * @enum {string|null}
             */
            cadenceType?: "authentic" | "half" | "plagal" | "deceptive" | null;
            /**
             * @description Allow secondary dominant chords
             * @default true
             */
            allowSecondaryDominants: boolean;
            /**
             * @description Allow borrowed chords from parallel modes
             * @default false
             */
            allowModalInterchange: boolean;
            /** @description Random seed for reproducibility */
            seed?: number | null;
        };
        /** @description Response containing a generated chord progression */
        GenerateProgressionResponse: {
            /** @description Generated chord events */
            chords: components["schemas"]["ChordEvent"][];
            /** @description Analysis of the progression */
            analysis?: components["schemas"]["ProgressionAnalysis"];
        };
        /** @description Request to get subscriptions for an account */
        GetAccountSubscriptionsRequest: {
            /**
             * Format: uuid
             * @description ID of the account to get subscriptions for
             */
            accountId: string;
            /**
             * @description If true, include cancelled subscriptions
             * @default false
             */
            includeInactive: boolean;
            /**
             * @description If true, include expired subscriptions
             * @default false
             */
            includeExpired: boolean;
        };
        /** @description Request to get achievement progress */
        GetAchievementProgressRequest: {
            /**
             * Format: uuid
             * @description ID of the game service
             */
            gameServiceId: string;
            /**
             * Format: uuid
             * @description ID of the entity
             */
            entityId: string;
            /** @description Entity type whose progress is requested */
            entityType: components["schemas"]["EntityType"];
            /** @description Specific achievement ID (null for all) */
            achievementId?: string | null;
        };
        /** @description Request to retrieve all ancestor types in the hierarchy chain from a relationship type up to the root */
        GetAncestorsRequest: {
            /**
             * Format: uuid
             * @description The relationship type to get ancestors for
             */
            typeId: string;
        };
        /** @description Request to retrieve asset metadata and download URL */
        GetAssetRequest: {
            /** @description Asset identifier */
            assetId: string;
            /**
             * @description Version ID or 'latest'
             * @default latest
             */
            version: string;
        };
        /** @description Request payload for getting a character's backstory */
        GetBackstoryRequest: {
            /**
             * Format: uuid
             * @description ID of the character to get backstory for
             */
            characterId: string;
            /** @description Filter by element types (null for all) */
            elementTypes?: components["schemas"]["BackstoryElementType"][] | null;
            /**
             * Format: float
             * @description Filter by minimum strength
             */
            minimumStrength?: number | null;
        };
        /** @description Request to get a specific currency balance */
        GetBalanceRequest: {
            /**
             * Format: uuid
             * @description Wallet ID
             */
            walletId: string;
            /**
             * Format: uuid
             * @description Currency definition ID
             */
            currencyDefinitionId: string;
        };
        /** @description Detailed balance information */
        GetBalanceResponse: {
            /**
             * Format: uuid
             * @description Wallet ID
             */
            walletId: string;
            /**
             * Format: uuid
             * @description Currency definition ID
             */
            currencyDefinitionId: string;
            /** @description Currency code */
            currencyCode?: string;
            /**
             * Format: double
             * @description Total balance
             */
            amount: number;
            /**
             * Format: double
             * @description Amount in authorization holds
             */
            lockedAmount: number;
            /**
             * Format: double
             * @description Available balance (amount - lockedAmount)
             */
            effectiveAmount: number;
            /** @description Earn cap status (null if no caps configured) */
            earnCapInfo?: components["schemas"]["EarnCapInfo"];
            /** @description Autogain status (null if autogain not enabled) */
            autogainInfo?: components["schemas"]["AutogainInfo"];
        };
        /** @description Request to get breach details */
        GetBreachRequest: {
            /**
             * Format: uuid
             * @description Breach ID
             */
            breachId: string;
        };
        /** @description Request to retrieve bundle metadata and download URL */
        GetBundleRequest: {
            /** @description Human-readable bundle identifier to retrieve */
            bundleId: string;
            /** @description Desired download format (bannou or zip) */
            format?: components["schemas"]["BundleFormat"];
        };
        /** @description Request to get a cached compiled behavior */
        GetCachedBehaviorRequest: {
            /** @description Unique identifier for the cached behavior */
            behaviorId: string;
        };
        /** @description Request to retrieve a character's compressed archive */
        GetCharacterArchiveRequest: {
            /**
             * Format: uuid
             * @description ID of the character to get archive for
             */
            characterId: string;
        };
        /** @description Request payload for retrieving a single character by ID */
        GetCharacterRequest: {
            /**
             * Format: uuid
             * @description ID of the character to retrieve
             */
            characterId: string;
        };
        /** @description Request payload for retrieving all characters within a specific realm */
        GetCharactersByRealmRequest: {
            /**
             * Format: uuid
             * @description Realm ID to query (uses partition key for efficiency)
             */
            realmId: string;
            /**
             * Format: uuid
             * @description Filter by species
             */
            speciesId?: string | null;
            /** @description Optional status filter */
            status?: components["schemas"]["CharacterStatus"] | null;
            /**
             * @description Page number for pagination (1-based)
             * @default 1
             */
            page: number;
            /**
             * @description Number of results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to retrieve all child relationship types for a given parent type */
        GetChildRelationshipTypesRequest: {
            /**
             * Format: uuid
             * @description ID of the parent relationship type
             */
            parentTypeId: string;
            /**
             * @description Include all descendants, not just direct children
             * @default false
             */
            recursive: boolean;
        };
        /** @description Request to get capability manifest for a connected session (debugging endpoint) */
        GetClientCapabilitiesRequest: {
            /**
             * Format: uuid
             * @description Session ID to retrieve capabilities for (must have active WebSocket connection)
             */
            sessionId: string;
            /** @description Optional filter by service name prefix */
            serviceFilter?: string | null;
            /**
             * @description Include additional metadata about each capability
             * @default false
             */
            includeMetadata: boolean;
        };
        /** @description Request payload for retrieving combat preferences */
        GetCombatPreferencesRequest: {
            /**
             * Format: uuid
             * @description ID of the character to get combat preferences for
             */
            characterId: string;
        };
        /** @description Request to get consent status for all parties */
        GetConsentStatusRequest: {
            /**
             * Format: uuid
             * @description Escrow ID
             */
            escrowId: string;
        };
        /** @description Response containing consent status for all parties */
        GetConsentStatusResponse: {
            /** @description Consent status per party */
            partiesRequiringConsent: components["schemas"]["PartyConsentStatus"][];
            /** @description Number of consents received */
            consentsReceived: number;
            /** @description Number of consents required */
            consentsRequired: number;
            /** @description Whether release can proceed */
            canRelease: boolean;
            /** @description Whether refund can proceed */
            canRefund: boolean;
        };
        /** @description Request to get a container */
        GetContainerRequest: {
            /**
             * Format: uuid
             * @description Container ID to retrieve
             */
            containerId: string;
            /**
             * @description Whether to include item contents
             * @default true
             */
            includeContents: boolean;
        };
        /** @description Request to get a contract instance */
        GetContractInstanceRequest: {
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractId: string;
        };
        /** @description Request to get contract status */
        GetContractInstanceStatusRequest: {
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractId: string;
        };
        /** @description Request to get contract metadata */
        GetContractMetadataRequest: {
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractId: string;
        };
        /** @description Request to get a contract template */
        GetContractTemplateRequest: {
            /**
             * Format: uuid
             * @description Template ID (provide this or code)
             */
            templateId?: string | null;
            /** @description Template code (provide this or templateId) */
            code?: string | null;
        };
        /** @description Request to get a currency definition */
        GetCurrencyDefinitionRequest: {
            /**
             * Format: uuid
             * @description Definition ID (provide this or code)
             */
            definitionId?: string | null;
            /** @description Currency code (provide this or definitionId) */
            code?: string | null;
        };
        /** @description Request to get a map definition */
        GetDefinitionRequest: {
            /**
             * Format: uuid
             * @description Definition ID to retrieve
             */
            definitionId: string;
        };
        /** @description Request to get deposit status for a party */
        GetDepositStatusRequest: {
            /**
             * Format: uuid
             * @description Escrow ID
             */
            escrowId: string;
            /**
             * Format: uuid
             * @description Party ID
             */
            partyId: string;
            /** @description Type of entity (Account, Character, etc.) */
            partyType: components["schemas"]["EntityType"];
        };
        /** @description Response containing party deposit status */
        GetDepositStatusResponse: {
            /** @description Expected assets from this party */
            expectedAssets: components["schemas"]["EscrowAsset"][];
            /** @description Actually deposited assets */
            depositedAssets: components["schemas"]["EscrowAsset"][];
            /** @description Whether deposit requirement is fulfilled */
            fulfilled: boolean;
            /** @description Deposit token (if not yet used) */
            depositToken?: string | null;
            /**
             * Format: date-time
             * @description Deposit deadline
             */
            depositDeadline?: string | null;
        };
        /** @description Request to retrieve a specific document by ID or slug */
        GetDocumentRequest: {
            /** @description Documentation namespace containing the document */
            namespace: string;
            /**
             * Format: uuid
             * @description Unique identifier of the document to retrieve (null if using slug)
             */
            documentId?: string | null;
            /** @description URL-friendly slug of the document to retrieve (null if using documentId) */
            slug?: string | null;
            /**
             * Format: uuid
             * @description Optional session ID for tracking document views (null if not tracking)
             */
            sessionId?: string | null;
            /** @description How deep to fetch related documents (null for no related documents) */
            includeRelated?: components["schemas"]["RelatedDepth"];
            /**
             * @description Whether to include full document content
             * @default false
             */
            includeContent: boolean;
            /**
             * @description Whether to render markdown content as HTML
             * @default false
             */
            renderHtml: boolean;
        };
        /** @description Response containing the requested document and optional related documents */
        GetDocumentResponse: {
            /** @description The requested document */
            document: components["schemas"]["Document"];
            /** @description List of related documents based on includeRelated depth */
            relatedDocuments?: components["schemas"]["DocumentSummary"][];
            /**
             * @description Format of the content field in the response
             * @enum {string}
             */
            contentFormat?: "markdown" | "html" | "none";
        };
        /** @description Request to retrieve an encounter type by code */
        GetEncounterTypeRequest: {
            /** @description Unique code of the encounter type */
            code: string;
        };
        /**
         * @description Request payload for retrieving a character with optional related data.
         *     Each include flag fetches data from its respective service (zero overhead if not requested).
         */
        GetEnrichedCharacterRequest: {
            /**
             * Format: uuid
             * @description ID of the character to retrieve
             */
            characterId: string;
            /**
             * @description Include personality traits from character-personality service
             * @default false
             */
            includePersonality: boolean;
            /**
             * @description Include backstory elements from character-history service
             * @default false
             */
            includeBackstory: boolean;
            /**
             * @description Include family relationships from relationship service
             * @default false
             */
            includeFamilyTree: boolean;
            /**
             * @description Include combat preferences from character-personality service
             * @default false
             */
            includeCombatPreferences: boolean;
        };
        /** @description Request to get an entity's rank */
        GetEntityRankRequest: {
            /**
             * Format: uuid
             * @description ID of the game service
             */
            gameServiceId: string;
            /** @description ID of the leaderboard */
            leaderboardId: string;
            /**
             * Format: uuid
             * @description ID of the entity
             */
            entityId: string;
            /** @description Entity type whose rank is requested */
            entityType: components["schemas"]["EntityType"];
        };
        /** @description Request to retrieve an escrow agreement by ID */
        GetEscrowRequest: {
            /**
             * Format: uuid
             * @description Escrow ID to retrieve
             */
            escrowId: string;
        };
        /** @description Response containing escrow agreement details */
        GetEscrowResponse: {
            /** @description Escrow agreement details */
            escrow: components["schemas"]["EscrowAgreement"];
        };
        /** @description Request payload for getting participants of an event */
        GetEventParticipantsRequest: {
            /**
             * Format: uuid
             * @description ID of the historical event
             */
            eventId: string;
            /** @description Filter by participation role */
            role?: components["schemas"]["ParticipationRole"];
            /**
             * @description Page number for pagination (1-based)
             * @default 1
             */
            page: number;
            /**
             * @description Number of results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to get exchange rate between currencies */
        GetExchangeRateRequest: {
            /**
             * Format: uuid
             * @description Source currency ID
             */
            fromCurrencyId: string;
            /**
             * Format: uuid
             * @description Target currency ID
             */
            toCurrencyId: string;
        };
        /** @description Exchange rate information */
        GetExchangeRateResponse: {
            /**
             * Format: double
             * @description Conversion rate (from -> to)
             */
            rate: number;
            /**
             * Format: double
             * @description Inverse rate (to -> from)
             */
            inverseRate: number;
            /** @description Base currency code */
            baseCurrency: string;
            /**
             * Format: double
             * @description Source currency rate to base
             */
            fromCurrencyRateToBase: number;
            /**
             * Format: double
             * @description Target currency rate to base
             */
            toCurrencyRateToBase: number;
        };
        /** @description Request to get a specific game session */
        GetGameSessionRequest: {
            /**
             * Format: uuid
             * @description ID of the game session to retrieve
             */
            sessionId: string;
        };
        /** @description Request to get global supply stats */
        GetGlobalSupplyRequest: {
            /**
             * Format: uuid
             * @description Currency to query
             */
            currencyDefinitionId: string;
        };
        /** @description Global supply statistics */
        GetGlobalSupplyResponse: {
            /**
             * Format: double
             * @description Sum of all positive balances
             */
            totalSupply: number;
            /**
             * Format: double
             * @description Total in wallets
             */
            inCirculation: number;
            /**
             * Format: double
             * @description Locked in escrow
             */
            inEscrow: number;
            /**
             * Format: double
             * @description All-time faucet total
             */
            totalMinted: number;
            /**
             * Format: double
             * @description All-time sink total
             */
            totalBurned: number;
            /**
             * Format: double
             * @description Global supply cap (null if none)
             */
            supplyCap?: number | null;
            /**
             * Format: double
             * @description Remaining supply before cap
             */
            supplyCapRemaining?: number | null;
        };
        /** @description Request to get hold details */
        GetHoldRequest: {
            /**
             * Format: uuid
             * @description Hold ID
             */
            holdId: string;
        };
        /** @description Request to get an item instance */
        GetItemInstanceRequest: {
            /**
             * Format: uuid
             * @description Instance ID to retrieve
             */
            instanceId: string;
        };
        /** @description Request to get an item template */
        GetItemTemplateRequest: {
            /**
             * Format: uuid
             * @description Template ID (provide this or code+gameId)
             */
            templateId?: string | null;
            /** @description Item code (provide with gameId) */
            code?: string | null;
            /** @description Game service ID (provide with code) */
            gameId?: string | null;
        };
        /** @description Request to get the status of an async metabundle creation job */
        GetJobStatusRequest: {
            /**
             * Format: uuid
             * @description Job ID from the createMetabundle response
             */
            jobId: string;
        };
        /**
         * @description Status of an async metabundle creation job.
         *     When status is 'ready', the response includes the full metabundle details.
         */
        GetJobStatusResponse: {
            /**
             * Format: uuid
             * @description Job identifier
             */
            jobId: string;
            /** @description Human-readable metabundle identifier being created */
            metabundleId: string;
            /**
             * @description Current job status.
             *     - queued: Waiting for processing resources
             *     - processing: Actively being processed
             *     - ready: Completed successfully
             *     - failed: Creation failed
             *     - cancelled: Job was cancelled
             * @enum {string}
             */
            status: "queued" | "processing" | "ready" | "failed" | "cancelled";
            /** @description Progress percentage (0-100) when status is 'processing' */
            progress?: number | null;
            /**
             * Format: uri
             * @description Pre-signed download URL (only when status is 'ready')
             */
            downloadUrl?: string | null;
            /** @description Number of assets in metabundle (when ready) */
            assetCount?: number | null;
            /** @description Number of standalone assets included (when ready) */
            standaloneAssetCount?: number | null;
            /**
             * Format: int64
             * @description Total size in bytes (when ready)
             */
            sizeBytes?: number | null;
            /** @description Provenance data (when ready) */
            sourceBundles?: components["schemas"]["SourceBundleReference"][] | null;
            /** @description Error code (when status is 'failed') */
            errorCode?: string | null;
            /** @description Human-readable error description (when status is 'failed') */
            errorMessage?: string | null;
            /**
             * Format: date-time
             * @description When the job was created
             */
            createdAt?: string | null;
            /**
             * Format: date-time
             * @description When the job was last updated
             */
            updatedAt?: string | null;
            /**
             * Format: int64
             * @description Total processing time in milliseconds (when complete)
             */
            processingTimeMs?: number | null;
        };
        /** @description Request to retrieve the full ancestry chain of a location up to the root */
        GetLocationAncestorsRequest: {
            /**
             * Format: uuid
             * @description The location to get ancestors for
             */
            locationId: string;
        };
        /** @description Request to retrieve a location by its code within a specific realm */
        GetLocationByCodeRequest: {
            /** @description Unique code for the location within the realm */
            code: string;
            /**
             * Format: uuid
             * @description Realm ID to scope the code lookup
             */
            realmId: string;
        };
        /** @description Request to retrieve all descendants of a location (children, grandchildren, etc.) */
        GetLocationDescendantsRequest: {
            /**
             * Format: uuid
             * @description The location to get descendants for
             */
            locationId: string;
            /** @description Optional filter by location type */
            locationType?: components["schemas"]["LocationType"] | null;
            /** @description Maximum depth of descendants to return (null = all) */
            maxDepth?: number | null;
            /**
             * @description Whether to include deprecated locations in the response
             * @default false
             */
            includeDeprecated: boolean;
            /**
             * @description Page number for pagination (1-indexed)
             * @default 1
             */
            page: number;
            /**
             * @description Number of results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to retrieve a location by its unique identifier */
        GetLocationRequest: {
            /**
             * Format: uuid
             * @description Unique identifier of the location
             */
            locationId: string;
        };
        /** @description Request to get matchmaking statistics */
        GetMatchmakingStatsRequest: {
            /** @description Filter by specific queue (null for all queues) */
            queueId?: string | null;
            /** @description Filter by game ID */
            gameId?: string | null;
        };
        /** @description Request to get matchmaking status */
        GetMatchmakingStatusRequest: {
            /**
             * Format: uuid
             * @description WebSocket session ID
             */
            webSocketSessionId: string;
            /**
             * Format: uuid
             * @description Account ID of the player
             */
            accountId: string;
            /**
             * Format: uuid
             * @description ID of the ticket to query
             */
            ticketId: string;
        };
        /** @description Request to get milestone details */
        GetMilestoneRequest: {
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractId: string;
            /** @description Milestone code */
            milestoneCode: string;
        };
        /** @description Request to get or create a container */
        GetOrCreateContainerRequest: {
            /**
             * Format: uuid
             * @description Owner entity ID
             */
            ownerId: string;
            /** @description Owner type */
            ownerType: components["schemas"]["ContainerOwnerType"];
            /** @description Container type to find or create */
            containerType: string;
            /** @description Constraint model for new container */
            constraintModel: components["schemas"]["ContainerConstraintModel"];
            /** @description Default max slots if creating */
            maxSlots?: number | null;
            /**
             * Format: double
             * @description Default max weight if creating
             */
            maxWeight?: number | null;
            /** @description Default grid width if creating */
            gridWidth?: number | null;
            /** @description Default grid height if creating */
            gridHeight?: number | null;
            /**
             * Format: uuid
             * @description Realm for new container
             */
            realmId?: string | null;
        };
        /** @description Request to get or create a wallet */
        GetOrCreateWalletRequest: {
            /**
             * Format: uuid
             * @description Owner entity ID
             */
            ownerId: string;
            /** @description Type of owner entity */
            ownerType: components["schemas"]["WalletOwnerType"];
            /**
             * Format: uuid
             * @description Realm ID for realm-scoped wallets
             */
            realmId?: string | null;
        };
        /** @description Result of get-or-create operation */
        GetOrCreateWalletResponse: {
            /** @description Wallet details */
            wallet: components["schemas"]["WalletResponse"];
            /** @description All non-zero balances */
            balances: components["schemas"]["BalanceSummary"][];
            /** @description Whether a new wallet was created */
            created: boolean;
        };
        /** @description Request payload for getting a character's event participation */
        GetParticipationRequest: {
            /**
             * Format: uuid
             * @description ID of the character to get participation for
             */
            characterId: string;
            /** @description Filter by event category */
            eventCategory?: components["schemas"]["EventCategory"];
            /**
             * Format: float
             * @description Filter by minimum significance
             */
            minimumSignificance?: number | null;
            /**
             * @description Page number for pagination (1-based)
             * @default 1
             */
            page: number;
            /**
             * @description Number of results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request payload for retrieving a character's personality */
        GetPersonalityRequest: {
            /**
             * Format: uuid
             * @description ID of the character to get personality for
             */
            characterId: string;
        };
        /** @description Request to get a character's perspective on an encounter */
        GetPerspectiveRequest: {
            /**
             * Format: uuid
             * @description Encounter to get perspective for
             */
            encounterId: string;
            /**
             * Format: uuid
             * @description Character whose perspective to retrieve
             */
            characterId: string;
        };
        /** @description Request to get details of a specific queue */
        GetQueueRequest: {
            /** @description ID of the queue to retrieve */
            queueId: string;
        };
        /** @description Request to get entries around an entity */
        GetRanksAroundRequest: {
            /**
             * Format: uuid
             * @description ID of the game service
             */
            gameServiceId: string;
            /** @description ID of the leaderboard */
            leaderboardId: string;
            /**
             * Format: uuid
             * @description ID of the entity to center on
             */
            entityId: string;
            /** @description Entity type of the anchor entry */
            entityType: components["schemas"]["EntityType"];
            /**
             * @description Entries to show before the entity
             * @default 5
             */
            countBefore: number;
            /**
             * @description Entries to show after the entity
             * @default 5
             */
            countAfter: number;
        };
        /** @description Request to retrieve a realm by its unique code identifier */
        GetRealmByCodeRequest: {
            /** @description Unique code for the realm (e.g., "REALM_1", "REALM_2") */
            code: string;
        };
        /** @description Request payload for getting participants of an event */
        GetRealmEventParticipantsRequest: {
            /**
             * Format: uuid
             * @description ID of the historical event
             */
            eventId: string;
            /** @description Filter by participation role */
            role?: components["schemas"]["RealmEventRole"];
            /**
             * @description Page number for pagination (1-based)
             * @default 1
             */
            page: number;
            /**
             * @description Number of results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request payload for getting a realm's lore */
        GetRealmLoreRequest: {
            /**
             * Format: uuid
             * @description ID of the realm to get lore for
             */
            realmId: string;
            /** @description Filter by element types (null for all) */
            elementTypes?: components["schemas"]["RealmLoreElementType"][] | null;
            /**
             * Format: float
             * @description Filter by minimum strength
             */
            minimumStrength?: number | null;
        };
        /** @description Request payload for getting a realm's event participation */
        GetRealmParticipationRequest: {
            /**
             * Format: uuid
             * @description ID of the realm to get participation for
             */
            realmId: string;
            /** @description Filter by event category */
            eventCategory?: components["schemas"]["RealmEventCategory"];
            /**
             * Format: float
             * @description Filter by minimum impact
             */
            minimumImpact?: number | null;
            /**
             * @description Page number for pagination (1-based)
             * @default 1
             */
            page: number;
            /**
             * @description Number of results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to retrieve a specific realm by its unique identifier */
        GetRealmRequest: {
            /**
             * Format: uuid
             * @description Unique identifier of the realm
             */
            realmId: string;
        };
        /** @description Request to retrieve a specific relationship by its ID */
        GetRelationshipRequest: {
            /**
             * Format: uuid
             * @description ID of the relationship to retrieve
             */
            relationshipId: string;
        };
        /** @description Request to retrieve a relationship type by its unique code string */
        GetRelationshipTypeByCodeRequest: {
            /** @description Unique code for the relationship type (e.g., "SON", "MOTHER", "FRIEND") */
            code: string;
        };
        /** @description Request to retrieve a relationship type by its unique identifier */
        GetRelationshipTypeRequest: {
            /**
             * Format: uuid
             * @description Unique identifier of the relationship type
             */
            relationshipTypeId: string;
        };
        /** @description Request to get all relationships between two specific entities */
        GetRelationshipsBetweenRequest: {
            /**
             * Format: uuid
             * @description ID of the first entity to check relationships for
             */
            entity1Id: string;
            /** @description Type of the first entity */
            entity1Type: components["schemas"]["EntityType"];
            /**
             * Format: uuid
             * @description ID of the second entity to check relationships for
             */
            entity2Id: string;
            /** @description Type of the second entity */
            entity2Type: components["schemas"]["EntityType"];
            /**
             * Format: uuid
             * @description Optional filter by relationship type
             */
            relationshipTypeId?: string | null;
            /**
             * @description Include relationships that have ended
             * @default false
             */
            includeEnded: boolean;
        };
        /** @description Request to retrieve a scene */
        GetSceneRequest: {
            /**
             * Format: uuid
             * @description ID of the scene to retrieve
             */
            sceneId: string;
            /** @description Specific version to retrieve (null = latest) */
            version?: string | null;
            /**
             * @description Whether to resolve and embed referenced scenes
             * @default false
             */
            resolveReferences: boolean;
            /**
             * @description Maximum depth for reference resolution (prevents infinite recursion)
             * @default 3
             */
            maxReferenceDepth: number;
        };
        /** @description Response containing a scene and resolution metadata */
        GetSceneResponse: {
            /** @description The retrieved scene */
            scene: components["schemas"]["Scene"];
            /** @description List of resolved references (if resolveReferences was true) */
            resolvedReferences?: components["schemas"]["ResolvedReference"][] | null;
            /** @description References that could not be resolved (circular, missing, depth exceeded) */
            unresolvedReferences?: components["schemas"]["UnresolvedReference"][] | null;
            /** @description Error messages for reference resolution issues */
            resolutionErrors?: string[] | null;
        };
        /** @description Request to get season information */
        GetSeasonRequest: {
            /**
             * Format: uuid
             * @description ID of the game service
             */
            gameServiceId: string;
            /** @description ID of the leaderboard */
            leaderboardId: string;
            /** @description Specific season number (null for current) */
            seasonNumber?: number | null;
        };
        /** @description Request to get aggregate sentiment toward another character */
        GetSentimentRequest: {
            /**
             * Format: uuid
             * @description Character whose sentiment to query
             */
            characterId: string;
            /**
             * Format: uuid
             * @description Target character to measure sentiment toward
             */
            targetCharacterId: string;
        };
        /** @description Request to get a service by ID or stub name (provide either one) */
        GetServiceRequest: {
            /**
             * Format: uuid
             * @description ID of the service to retrieve (null if using stubName)
             */
            serviceId?: string | null;
            /** @description Stub name of the service to retrieve (null if using serviceId) */
            stubName?: string | null;
        };
        /** @description Request to retrieve metadata for a specific save slot */
        GetSlotRequest: {
            /** @description Game identifier for namespace isolation */
            gameId: string;
            /**
             * Format: uuid
             * @description ID of the owning entity
             */
            ownerId: string;
            /** @description Type of entity that owns this save slot */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Slot name */
            slotName: string;
        };
        /** @description Request to retrieve a species by its unique code identifier */
        GetSpeciesByCodeRequest: {
            /** @description Unique code for the species (e.g., "HUMAN", "ELF", "DWARF") */
            code: string;
        };
        /** @description Request to retrieve a single species by its unique identifier */
        GetSpeciesRequest: {
            /**
             * Format: uuid
             * @description Unique identifier of the species
             */
            speciesId: string;
        };
        /** @description Request to get a style definition */
        GetStyleRequest: {
            /** @description Style ID to retrieve */
            styleId?: string | null;
            /** @description Style name to retrieve (alternative to ID) */
            styleName?: string | null;
        };
        /** @description Request to get a specific subscription */
        GetSubscriptionRequest: {
            /**
             * Format: uuid
             * @description ID of the subscription to retrieve
             */
            subscriptionId: string;
        };
        /** @description Request to get top leaderboard entries */
        GetTopRanksRequest: {
            /**
             * Format: uuid
             * @description ID of the game service
             */
            gameServiceId: string;
            /** @description ID of the leaderboard */
            leaderboardId: string;
            /**
             * @description Number of entries to return
             * @default 100
             */
            count: number;
            /**
             * @description Number of entries to skip
             * @default 0
             */
            offset: number;
        };
        /** @description Request to get transaction history */
        GetTransactionHistoryRequest: {
            /**
             * Format: uuid
             * @description Wallet to query
             */
            walletId: string;
            /**
             * Format: uuid
             * @description Filter by currency
             */
            currencyDefinitionId?: string | null;
            /** @description Filter by transaction types */
            transactionTypes?: components["schemas"]["TransactionType"][] | null;
            /**
             * Format: date-time
             * @description Start of date range
             */
            fromDate?: string | null;
            /**
             * Format: date-time
             * @description End of date range
             */
            toDate?: string | null;
            /**
             * @description Results per page
             * @default 50
             */
            limit: number;
            /**
             * @description Result offset
             * @default 0
             */
            offset: number;
        };
        /** @description Paginated transaction history */
        GetTransactionHistoryResponse: {
            /** @description Transaction records */
            transactions: components["schemas"]["CurrencyTransactionRecord"][];
            /** @description Total matching transactions */
            totalCount: number;
        };
        /** @description Request to get a transaction by ID */
        GetTransactionRequest: {
            /**
             * Format: uuid
             * @description Transaction ID
             */
            transactionId: string;
        };
        /** @description Request to get transactions by reference */
        GetTransactionsByReferenceRequest: {
            /** @description Reference type */
            referenceType: string;
            /**
             * Format: uuid
             * @description Reference ID
             */
            referenceId: string;
        };
        /** @description Transactions for a reference */
        GetTransactionsByReferenceResponse: {
            /** @description Matching transactions */
            transactions: components["schemas"]["CurrencyTransactionRecord"][];
        };
        /** @description Request to get validation rules */
        GetValidationRulesRequest: {
            /** @description Game ID */
            gameId: string;
            /** @description Scene type */
            sceneType: components["schemas"]["SceneType"];
        };
        /** @description Response containing validation rules */
        GetValidationRulesResponse: {
            /** @description Game ID */
            gameId: string;
            /** @description Scene type */
            sceneType: components["schemas"]["SceneType"];
            /** @description Registered rules (empty if none) */
            rules?: components["schemas"]["ValidationRule"][];
        };
        /** @description Request to get a wallet */
        GetWalletRequest: {
            /**
             * Format: uuid
             * @description Wallet ID (provide this or ownerId+ownerType)
             */
            walletId?: string | null;
            /**
             * Format: uuid
             * @description Owner ID (requires ownerType)
             */
            ownerId?: string | null;
            /** @description Owner type (requires ownerId) */
            ownerType?: components["schemas"]["WalletOwnerType"];
            /**
             * Format: uuid
             * @description Realm ID (required if using ownerId lookup)
             */
            realmId?: string | null;
        };
        /** @description Goal definition for GOAP planning with conditions and priority */
        GoapGoal: {
            /**
             * @description Name of the goal
             * @example satisfy_hunger
             */
            name: string;
            /** @description Human-readable description of the goal */
            description?: string | null;
            /**
             * @description World state conditions that satisfy this goal (literal conditions)
             * @example {
             *       "hunger": "<= 0.3",
             *       "gold": ">= 50"
             *     }
             */
            conditions: {
                [key: string]: string;
            };
            /** @description Priority of this goal relative to others */
            priority: number;
            /** @description World state conditions required to pursue this goal */
            preconditions?: {
                [key: string]: string;
            } | null;
        };
        /** @description Request to generate a GOAP plan to achieve a goal from current world state */
        GoapPlanRequest: {
            /** @description Unique identifier for the agent requesting the plan */
            agentId?: string | null;
            /** @description The goal to achieve through planning */
            goal: components["schemas"]["GoapGoal"];
            /**
             * @description Current world state as key-value pairs
             * @example {
             *       "hunger": 0.8,
             *       "gold": 50,
             *       "location": "home"
             *     }
             */
            worldState: {
                [key: string]: unknown;
            };
            /** @description ID of compiled behavior containing GOAP actions */
            behaviorId: string;
            /** @description Options controlling the planning process */
            options?: components["schemas"]["GoapPlanningOptions"];
        };
        /** @description Response containing the generated GOAP plan. If no plan could be found, plan is null and failureReason explains why. */
        GoapPlanResponse: {
            /** @description The generated plan if successful */
            plan?: components["schemas"]["GoapPlanResult"];
            /** @description Time spent planning in milliseconds */
            planningTimeMs?: number;
            /** @description Number of nodes expanded during A* search */
            nodesExpanded?: number;
            /**
             * @description Reason for planning failure if unsuccessful
             * @example No plan found - goal unreachable
             */
            failureReason?: string | null;
        };
        /** @description Result of GOAP planning containing the ordered sequence of actions to achieve a goal */
        GoapPlanResult: {
            /** @description ID of the goal this plan achieves */
            goalId: string;
            /** @description Ordered sequence of actions to execute */
            actions: components["schemas"]["PlannedActionResponse"][];
            /**
             * Format: float
             * @description Total cost of all actions in the plan
             */
            totalCost: number;
        };
        /** @description Options controlling the GOAP planning process including depth and timeout limits */
        GoapPlanningOptions: {
            /**
             * @description Maximum plan depth (number of actions)
             * @default 10
             */
            maxDepth: number;
            /**
             * @description Maximum nodes to expand during search
             * @default 1000
             */
            maxNodes: number;
            /**
             * @description Planning timeout in milliseconds
             * @default 100
             */
            timeoutMs: number;
        };
        /**
         * @description Preferred role when fighting in groups. Affects positioning,
         *     target priority, and coordination behavior.
         * @enum {string}
         */
        GroupRole: "FRONTLINE" | "SUPPORT" | "FLANKER" | "LEADER" | "SOLO";
        /** @description Harmonic progression style preferences */
        HarmonyStyle: {
            /**
             * @description Most common cadence type
             * @default authentic
             * @enum {string}
             */
            primaryCadence: "authentic" | "plagal" | "half" | "deceptive";
            /**
             * Format: float
             * @description Probability of pre-dominant before dominant
             * @default 0.6
             */
            dominantPrepProbability: number;
            /**
             * Format: float
             * @description Probability of secondary dominants
             * @default 0.3
             */
            secondaryDominantProbability: number;
            /**
             * Format: float
             * @description Probability of borrowed chords
             * @default 0.1
             */
            modalInterchangeProbability: number;
            /** @description Common chord progressions as roman numeral strings */
            commonProgressions?: string[] | null;
        };
        /** @description Individual item check result */
        HasItemResult: {
            /**
             * Format: uuid
             * @description Template checked
             */
            templateId: string;
            /**
             * Format: double
             * @description Required quantity
             */
            required: number;
            /**
             * Format: double
             * @description Available quantity
             */
            available: number;
            /** @description Whether requirement met */
            satisfied: boolean;
        };
        /** @description Request to check for items */
        HasItemsRequest: {
            /**
             * Format: uuid
             * @description Owner to check
             */
            ownerId: string;
            /** @description Owner type */
            ownerType: components["schemas"]["ContainerOwnerType"];
            /** @description Required items */
            requirements: components["schemas"]["ItemRequirement"][];
        };
        /** @description Has items result */
        HasItemsResponse: {
            /** @description Whether all requirements met */
            hasAll: boolean;
            /** @description Per-item results */
            results: components["schemas"]["HasItemResult"][];
        };
        /** @description Request to check if two characters have met */
        HasMetRequest: {
            /**
             * Format: uuid
             * @description First character
             */
            characterIdA: string;
            /**
             * Format: uuid
             * @description Second character
             */
            characterIdB: string;
        };
        /** @description Response for has-met check */
        HasMetResponse: {
            /** @description Whether the characters have any recorded encounters */
            hasMet: boolean;
            /** @description Total number of encounters between them */
            encounterCount: number;
        };
        /** @description Information about a hazard in range */
        HazardInfo: {
            /** @description Type of hazard (fire, poison, radiation, deep_water, etc.) */
            hazardType?: string;
            /**
             * Format: float
             * @description Distance to hazard edge
             */
            distance?: number;
            /**
             * Format: float
             * @description Hazard severity (0-1)
             */
            severity?: number;
            /** @description Direction to hazard center */
            direction?: string | null;
        } & {
            [key: string]: unknown;
        };
        /** @description Request to extend checkout lock */
        HeartbeatRequest: {
            /**
             * Format: uuid
             * @description Scene being edited
             */
            sceneId: string;
            /** @description Checkout token */
            checkoutToken: string;
        };
        /** @description Response confirming lock extension */
        HeartbeatResponse: {
            /** @description Whether extension was successful */
            extended: boolean;
            /**
             * Format: date-time
             * @description New expiration time
             */
            newExpiresAt: string;
            /** @description Number of extensions remaining */
            extensionsRemaining?: number;
        };
        /** @description Record of a character's participation in a historical event */
        HistoricalParticipation: {
            /**
             * Format: uuid
             * @description Unique ID for this participation record
             */
            participationId: string;
            /**
             * Format: uuid
             * @description ID of the character who participated
             */
            characterId: string;
            /**
             * Format: uuid
             * @description ID of the historical event
             */
            eventId: string;
            /** @description Name of the event (for display and summarization) */
            eventName: string;
            /** @description Category of the historical event */
            eventCategory: components["schemas"]["EventCategory"];
            /** @description How the character participated */
            role: components["schemas"]["ParticipationRole"];
            /**
             * Format: date-time
             * @description In-game date when the event occurred
             */
            eventDate: string;
            /**
             * Format: float
             * @description How significant this event was for the character (0.0 to 1.0).
             *     Affects behavior system weighting of this memory.
             * @default 0.5
             */
            significance: number;
            /** @description Event-specific details for behavior decisions */
            metadata?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: date-time
             * @description When this record was created
             */
            createdAt: string;
        };
        /** @description Request for scene version history */
        HistoryRequest: {
            /**
             * Format: uuid
             * @description Scene to get history for
             */
            sceneId: string;
            /**
             * @description Maximum versions to return
             * @default 10
             */
            limit: number;
        };
        /** @description Scene version history */
        HistoryResponse: {
            /**
             * Format: uuid
             * @description Scene ID
             */
            sceneId: string;
            /** @description Current active version */
            currentVersion?: string;
            /** @description Version history entries */
            versions: components["schemas"]["VersionInfo"][];
        };
        /** @description Authorization hold record */
        HoldRecord: {
            /**
             * Format: uuid
             * @description Unique hold identifier
             */
            holdId: string;
            /**
             * Format: uuid
             * @description Wallet with held funds
             */
            walletId: string;
            /**
             * Format: uuid
             * @description Currency held
             */
            currencyDefinitionId: string;
            /**
             * Format: double
             * @description Amount reserved
             */
            amount: number;
            /** @description Current hold status */
            status: components["schemas"]["HoldStatus"];
            /**
             * Format: date-time
             * @description When hold was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description When hold auto-releases
             */
            expiresAt: string;
            /** @description Reference type */
            referenceType?: string | null;
            /**
             * Format: uuid
             * @description Reference ID
             */
            referenceId?: string | null;
            /**
             * Format: double
             * @description Amount actually captured (may differ from held amount)
             */
            capturedAmount?: number | null;
            /**
             * Format: date-time
             * @description When hold was captured/released/expired
             */
            completedAt?: string | null;
        };
        /** @description Hold details */
        HoldResponse: {
            /** @description Hold record */
            hold: components["schemas"]["HoldRecord"];
        };
        /**
         * @description Current status of an authorization hold
         * @enum {string}
         */
        HoldStatus: "active" | "captured" | "released" | "expired";
        /** @description Request to inject a perception event into an actor's queue */
        InjectPerceptionRequest: {
            /** @description Target actor to inject perception into */
            actorId: string;
            /** @description Perception data to inject */
            perception: components["schemas"]["PerceptionData"];
        };
        /** @description Response confirming perception injection */
        InjectPerceptionResponse: {
            /** @description Whether the perception was successfully queued */
            queued: boolean;
            /** @description Current depth of the perception queue */
            queueDepth: number;
        };
        /** @description Melodic interval preference weights */
        IntervalPreferences: {
            /**
             * Format: float
             * @description Weight for stepwise motion (M2, m2)
             * @default 0.5
             */
            stepWeight: number;
            /**
             * Format: float
             * @description Weight for thirds (M3, m3)
             * @default 0.25
             */
            thirdWeight: number;
            /**
             * Format: float
             * @description Weight for larger leaps (P4, P5)
             * @default 0.15
             */
            leapWeight: number;
            /**
             * Format: float
             * @description Weight for leaps larger than P5
             * @default 0.1
             */
            largeLeapWeight: number;
        };
        /** @description Request to invalidate a cached behavior */
        InvalidateCacheRequest: {
            /** @description Unique identifier for the cached behavior to invalidate */
            behaviorId: string;
        };
        /**
         * @description Item classification category
         * @enum {string}
         */
        ItemCategory: "weapon" | "armor" | "accessory" | "consumable" | "material" | "container" | "quest" | "currency_like" | "misc" | "custom";
        /** @description Item instance details */
        ItemInstanceResponse: {
            /**
             * Format: uuid
             * @description Unique instance identifier
             */
            instanceId: string;
            /**
             * Format: uuid
             * @description Reference to the item template
             */
            templateId: string;
            /**
             * Format: uuid
             * @description Container holding this item
             */
            containerId: string;
            /**
             * Format: uuid
             * @description Realm this instance exists in
             */
            realmId: string;
            /**
             * Format: double
             * @description Item quantity
             */
            quantity: number;
            /** @description Slot position in slot-based containers */
            slotIndex?: number | null;
            /** @description X position in grid-based containers */
            slotX?: number | null;
            /** @description Y position in grid-based containers */
            slotY?: number | null;
            /** @description Whether item is rotated in grid */
            rotated?: boolean | null;
            /** @description Current durability */
            currentDurability?: number | null;
            /**
             * Format: uuid
             * @description Character ID this item is bound to
             */
            boundToId?: string | null;
            /**
             * Format: date-time
             * @description When item was bound
             */
            boundAt?: string | null;
            /** @description Instance-specific stat modifications */
            customStats?: Record<string, never> | null;
            /** @description Player-assigned custom name */
            customName?: string | null;
            /** @description Other instance-specific data */
            instanceMetadata?: Record<string, never> | null;
            /** @description How this item instance was created */
            originType: components["schemas"]["ItemOriginType"];
            /**
             * Format: uuid
             * @description Source entity ID
             */
            originId?: string | null;
            /**
             * Format: date-time
             * @description Instance creation timestamp
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Last modification timestamp
             */
            modifiedAt?: string | null;
        };
        /**
         * @description How currency is linked to inventory items
         * @enum {string}
         */
        ItemLinkageMode: "none" | "visual_only" | "reference_only";
        /**
         * @description How an item instance was created
         * @enum {string}
         */
        ItemOriginType: "loot" | "quest" | "craft" | "trade" | "purchase" | "spawn" | "other";
        /**
         * @description Item rarity tier
         * @enum {string}
         */
        ItemRarity: "common" | "uncommon" | "rare" | "epic" | "legendary" | "custom";
        /** @description Required item and quantity */
        ItemRequirement: {
            /**
             * Format: uuid
             * @description Required template
             */
            templateId: string;
            /**
             * Format: double
             * @description Required quantity
             */
            quantity: number;
        };
        /**
         * @description Realm availability scope (consistent with CurrencyScope)
         * @enum {string}
         */
        ItemScope: "global" | "realm_specific" | "multi_realm";
        /** @description Item template details */
        ItemTemplateResponse: {
            /**
             * Format: uuid
             * @description Unique template identifier
             */
            templateId: string;
            /** @description Unique code within the game */
            code: string;
            /** @description Game service this template belongs to */
            gameId: string;
            /** @description Human-readable display name */
            name: string;
            /** @description Detailed description */
            description?: string | null;
            /** @description Item classification category */
            category: components["schemas"]["ItemCategory"];
            /** @description Game-defined subcategory */
            subcategory?: string | null;
            /** @description Filtering tags */
            tags?: string[];
            /** @description Item rarity tier */
            rarity?: components["schemas"]["ItemRarity"];
            /** @description How quantities are tracked */
            quantityModel: components["schemas"]["QuantityModel"];
            /** @description Maximum stack size */
            maxStackSize: number;
            /** @description Unit for continuous quantities */
            unitOfMeasure?: string | null;
            /** @description Precision for weight values */
            weightPrecision?: components["schemas"]["WeightPrecision"];
            /**
             * Format: double
             * @description Weight value
             */
            weight?: number | null;
            /**
             * Format: double
             * @description Volume for volumetric inventories
             */
            volume?: number | null;
            /** @description Width in grid-based inventories */
            gridWidth?: number | null;
            /** @description Height in grid-based inventories */
            gridHeight?: number | null;
            /** @description Whether item can be rotated in grid */
            canRotate?: boolean | null;
            /**
             * Format: double
             * @description Reference price
             */
            baseValue?: number | null;
            /** @description Whether item can be traded */
            tradeable: boolean;
            /** @description Whether item can be destroyed */
            destroyable: boolean;
            /** @description Binding behavior type */
            soulboundType: components["schemas"]["SoulboundType"];
            /** @description Whether item has durability */
            hasDurability: boolean;
            /** @description Maximum durability value */
            maxDurability?: number | null;
            /** @description Realm availability scope */
            scope: components["schemas"]["ItemScope"];
            /** @description Available realms */
            availableRealms?: string[] | null;
            /** @description Game-defined stats */
            stats?: Record<string, never> | null;
            /** @description Game-defined effects */
            effects?: Record<string, never> | null;
            /** @description Game-defined requirements */
            requirements?: Record<string, never> | null;
            /** @description Display properties */
            display?: Record<string, never> | null;
            /** @description Other game-specific data */
            metadata?: Record<string, never> | null;
            /** @description Whether template is active */
            isActive: boolean;
            /** @description Whether template is deprecated */
            isDeprecated: boolean;
            /**
             * Format: date-time
             * @description When template was deprecated
             */
            deprecatedAt?: string | null;
            /**
             * Format: uuid
             * @description Migration target template
             */
            migrationTargetId?: string | null;
            /**
             * Format: date-time
             * @description Creation timestamp
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Last update timestamp
             */
            updatedAt: string;
        };
        /** @description Request to join a matchmaking queue */
        JoinMatchmakingRequest: {
            /**
             * Format: uuid
             * @description WebSocket session ID for event delivery
             */
            webSocketSessionId: string;
            /**
             * Format: uuid
             * @description Account ID of the player joining
             */
            accountId: string;
            /** @description ID of the queue to join */
            queueId: string;
            /**
             * Format: uuid
             * @description Party ID if joining as part of a party
             */
            partyId?: string | null;
            /** @description Party member information (required if partyId provided) */
            partyMembers?: components["schemas"]["PartyMemberInfo"][] | null;
            /** @description String properties for query matching */
            stringProperties?: {
                [key: string]: string;
            } | null;
            /** @description Numeric properties for query matching */
            numericProperties?: {
                [key: string]: number;
            } | null;
            /** @description Lucene-like query for opponent matching */
            query?: string | null;
            /**
             * Format: uuid
             * @description Tournament ID if joining tournament queue
             */
            tournamentId?: string | null;
        };
        /** @description Response after joining a matchmaking queue */
        JoinMatchmakingResponse: {
            /**
             * Format: uuid
             * @description Unique identifier for this matchmaking ticket
             */
            ticketId: string;
            /** @description Queue that was joined */
            queueId: string;
            /** @description Estimated wait time based on current queue (null if unknown) */
            estimatedWaitSeconds: number | null;
            /** @description Approximate position in queue (null if not tracked) */
            position?: number | null;
        };
        /**
         * @description JSON Patch operation per RFC 6902.
         *     Uses JsonPatch.Net library (MIT licensed).
         */
        JsonPatchOperation: {
            /**
             * @description Operation type
             * @enum {string}
             */
            op: "add" | "remove" | "replace" | "move" | "copy" | "test";
            /** @description JSON Pointer to target location */
            path: string;
            /** @description Source path (for move/copy operations) */
            from?: string | null;
            /** @description Value to use (for add/replace/test operations) */
            value?: unknown;
        };
        /** @description A key signature with tonic and mode */
        KeySignature: {
            /** @description Tonic pitch class */
            tonic: components["schemas"]["PitchClass"];
            /**
             * @description Mode/scale type
             * @enum {string}
             */
            mode: "major" | "minor" | "dorian" | "phrygian" | "lydian" | "mixolydian" | "aeolian" | "locrian";
        };
        /** @description A key signature change event */
        KeySignatureEvent: {
            /** @description Tick position */
            tick: number;
            /** @description Tonic pitch class */
            tonic: components["schemas"]["PitchClass"];
            /** @description Mode/scale type */
            mode: components["schemas"]["ModeType"];
        };
        /** @description Configuration for a specific layer within a map definition */
        LayerDefinition: {
            /** @description The layer kind */
            kind: components["schemas"]["MapKind"];
            /**
             * @description How this layer's data should be stored
             * @default cached
             * @enum {string}
             */
            storageMode: "durable" | "cached" | "ephemeral";
            /** @description TTL for cached/ephemeral data (0 = no TTL) */
            ttlSeconds?: number | null;
            /** @description Default non-authority handling for channels using this layer */
            defaultNonAuthorityHandling?: components["schemas"]["NonAuthorityHandlingMode"];
            /**
             * Format: double
             * @description Spatial cell size for indexing (default from config if not set)
             */
            cellSize?: number | null;
        };
        /** @description Leaderboard definition details */
        LeaderboardDefinitionResponse: {
            /**
             * Format: uuid
             * @description ID of the owning game service
             */
            gameServiceId: string;
            /** @description Unique identifier for this leaderboard */
            leaderboardId: string;
            /** @description Human-readable name */
            displayName: string;
            /** @description Description of the leaderboard */
            description?: string | null;
            /** @description Allowed entity types */
            entityTypes?: components["schemas"]["EntityType"][];
            /** @description Ordering used when ranking scores (descending for high scores, ascending for low) */
            sortOrder: components["schemas"]["SortOrder"];
            /** @description Rule applied when new scores are submitted (replace/increment/max/min) */
            updateMode: components["schemas"]["UpdateMode"];
            /** @description Whether the leaderboard is seasonal */
            isSeasonal: boolean;
            /** @description Whether the leaderboard is publicly visible */
            isPublic: boolean;
            /** @description Current season number (if seasonal) */
            currentSeason?: number | null;
            /**
             * Format: int64
             * @description Number of entries on the leaderboard
             */
            entryCount?: number;
            /**
             * Format: date-time
             * @description When the leaderboard was created
             */
            createdAt: string;
            /** @description Additional metadata */
            metadata?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Response containing leaderboard entries */
        LeaderboardEntriesResponse: {
            /** @description ID of the leaderboard */
            leaderboardId: string;
            /** @description List of leaderboard entries */
            entries: components["schemas"]["LeaderboardEntry"][];
            /**
             * Format: int64
             * @description Total entries on the leaderboard
             */
            totalEntries: number;
        };
        /** @description A single entry on a leaderboard */
        LeaderboardEntry: {
            /**
             * Format: uuid
             * @description ID of the entity
             */
            entityId: string;
            /** @description Entity type for this leaderboard entry */
            entityType: components["schemas"]["EntityType"];
            /**
             * Format: double
             * @description Entity's score
             */
            score: number;
            /**
             * Format: int64
             * @description Entity's rank (1-based)
             */
            rank: number;
            /** @description Cached display name for the entity */
            displayName?: string | null;
            /** @description Entry metadata */
            metadata?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Request to leave a specific game session by ID */
        LeaveGameSessionByIdRequest: {
            /**
             * Format: uuid
             * @description WebSocket session ID of the client leaving.
             */
            webSocketSessionId: string;
            /**
             * Format: uuid
             * @description Account ID of the player leaving.
             */
            accountId: string;
            /**
             * Format: uuid
             * @description ID of the game session to leave.
             */
            gameSessionId: string;
        };
        /** @description Request to leave a game session */
        LeaveGameSessionRequest: {
            /**
             * Format: uuid
             * @description WebSocket session ID of the client leaving. Provided by shortcut system.
             */
            sessionId: string;
            /**
             * Format: uuid
             * @description Account ID of the player leaving. Provided by shortcut system.
             */
            accountId: string;
            /** @description Game type being left. Determines which lobby to leave. Provided by shortcut system. */
            gameType: string;
        };
        /** @description Request to leave a matchmaking queue */
        LeaveMatchmakingRequest: {
            /**
             * Format: uuid
             * @description WebSocket session ID
             */
            webSocketSessionId: string;
            /**
             * Format: uuid
             * @description Account ID of the player
             */
            accountId: string;
            /**
             * Format: uuid
             * @description ID of the ticket to cancel
             */
            ticketId: string;
        };
        /** @description Request to list achievement definitions */
        ListAchievementDefinitionsRequest: {
            /**
             * Format: uuid
             * @description ID of the game service
             */
            gameServiceId: string;
            /** @description Filter by platform */
            platform?: components["schemas"]["Platform"];
            /** @description Filter by achievement classification */
            achievementType?: components["schemas"]["AchievementType"];
            /** @description Filter by active status */
            isActive?: boolean | null;
            /**
             * @description Include hidden achievements in response
             * @default false
             */
            includeHidden: boolean;
        };
        /** @description Response containing achievement definitions */
        ListAchievementDefinitionsResponse: {
            /** @description List of achievement definitions */
            achievements: components["schemas"]["AchievementDefinitionResponse"][];
        };
        /** @description Request to list available archives for a namespace */
        ListArchivesRequest: {
            /** @description Documentation namespace to list archives for */
            namespace: string;
            /**
             * @description Maximum number of archives to return
             * @default 20
             */
            limit: number;
            /**
             * @description Number of archives to skip
             * @default 0
             */
            offset: number;
        };
        /** @description Response containing a paginated list of archives */
        ListArchivesResponse: {
            /** @description List of archives for the namespace */
            archives: components["schemas"]["ArchiveInfo"][];
            /** @description Total number of archives */
            total: number;
        };
        /** @description Request to list bundle version history */
        ListBundleVersionsRequest: {
            /** @description Human-readable bundle identifier to get history for */
            bundleId: string;
            /**
             * @description Maximum versions to return
             * @default 50
             */
            limit: number;
            /**
             * @description Pagination offset
             * @default 0
             */
            offset: number;
        };
        /** @description Bundle version history */
        ListBundleVersionsResponse: {
            /** @description Human-readable bundle identifier */
            bundleId: string;
            /** @description Current version number */
            currentVersion: number;
            /** @description Version history records (newest first) */
            versions: components["schemas"]["BundleVersionRecord"][];
            /** @description Total number of versions */
            totalCount: number;
        };
        /** @description Request payload for listing characters with filtering and pagination */
        ListCharactersRequest: {
            /**
             * Format: uuid
             * @description Realm to list characters from (required for efficiency)
             */
            realmId: string;
            /**
             * Format: uuid
             * @description Filter by species
             */
            speciesId?: string | null;
            /** @description Filter by status */
            status?: components["schemas"]["CharacterStatus"] | null;
            /**
             * @description Page number for pagination (1-based)
             * @default 1
             */
            page: number;
            /**
             * @description Number of results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to list clause types */
        ListClauseTypesRequest: {
            /** @description Filter by category */
            category?: components["schemas"]["ClauseCategory"];
            /**
             * @description Include built-in types in response
             * @default true
             */
            includeBuiltIn: boolean;
        };
        /** @description Response containing list of clause types */
        ListClauseTypesResponse: {
            /** @description List of registered clause types */
            clauseTypes: components["schemas"]["ClauseTypeSummary"][];
        };
        /** @description Request to list containers for an owner */
        ListContainersRequest: {
            /**
             * Format: uuid
             * @description Owner entity ID
             */
            ownerId: string;
            /** @description Owner type */
            ownerType: components["schemas"]["ContainerOwnerType"];
            /** @description Filter by container type */
            containerType?: string | null;
            /**
             * @description Include equipment slot containers
             * @default true
             */
            includeEquipmentSlots: boolean;
            /**
             * Format: uuid
             * @description Filter by realm
             */
            realmId?: string | null;
        };
        /** @description List of containers */
        ListContainersResponse: {
            /** @description List of containers */
            containers: components["schemas"]["ContainerResponse"][];
            /** @description Total count */
            totalCount: number;
        };
        /** @description Request to list contract templates */
        ListContractTemplatesRequest: {
            /**
             * Format: uuid
             * @description Filter by realm (null includes cross-realm templates)
             */
            realmId?: string | null;
            /** @description Filter by active status */
            isActive?: boolean | null;
            /** @description Search in name and description */
            searchTerm?: string | null;
            /**
             * @description Page number (1-based)
             * @default 1
             */
            page: number;
            /**
             * @description Results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Paginated list of contract templates */
        ListContractTemplatesResponse: {
            /** @description List of templates */
            templates: components["schemas"]["ContractTemplateResponse"][];
            /** @description Total matching templates */
            totalCount: number;
            /** @description Current page number */
            page: number;
            /** @description Results per page */
            pageSize: number;
            /** @description Whether more results exist */
            hasNextPage?: boolean;
        };
        /** @description Request to list currency definitions */
        ListCurrencyDefinitionsRequest: {
            /**
             * Format: uuid
             * @description Filter by realm availability
             */
            realmId?: string | null;
            /** @description Filter by scope */
            scope?: components["schemas"]["CurrencyScope"];
            /**
             * @description Include inactive definitions
             * @default false
             */
            includeInactive: boolean;
            /** @description Filter by base currency flag */
            isBaseCurrency?: boolean | null;
        };
        /** @description List of currency definitions */
        ListCurrencyDefinitionsResponse: {
            /** @description Currency definitions matching filter */
            definitions: components["schemas"]["CurrencyDefinitionResponse"][];
        };
        /** @description Request to list map definitions */
        ListDefinitionsRequest: {
            /** @description Filter by name (partial match) */
            nameFilter?: string | null;
            /**
             * @description Pagination offset
             * @default 0
             */
            offset: number;
            /**
             * @description Max results to return
             * @default 50
             */
            limit: number;
        };
        /** @description Response containing list of map definitions */
        ListDefinitionsResponse: {
            /** @description List of definitions */
            definitions?: components["schemas"]["MapDefinition"][];
            /** @description Total count matching filter */
            total?: number;
            /** @description Current offset */
            offset?: number;
            /** @description Results limit used */
            limit?: number;
        };
        /** @description Request to list documents with optional filtering and pagination */
        ListDocumentsRequest: {
            /** @description Documentation namespace to list documents from */
            namespace: string;
            /** @description Filter to a specific category */
            category?: components["schemas"]["DocumentCategory"];
            /** @description Filter by tags (null to skip tag filtering) */
            tags?: string[] | null;
            /**
             * @description Whether documents must match all tags or any tag
             * @default all
             * @enum {string}
             */
            tagsMatch: "all" | "any";
            /**
             * Format: date-time
             * @description Filter to documents created after this timestamp
             */
            createdAfter?: string;
            /**
             * Format: date-time
             * @description Filter to documents created before this timestamp
             */
            createdBefore?: string;
            /**
             * Format: date-time
             * @description Filter to documents updated after this timestamp
             */
            updatedAfter?: string;
            /**
             * Format: date-time
             * @description Filter to documents updated before this timestamp
             */
            updatedBefore?: string;
            /**
             * @description Return only document titles without summaries
             * @default false
             */
            titlesOnly: boolean;
            /**
             * @description Page number for pagination
             * @default 1
             */
            page: number;
            /**
             * @description Number of documents per page
             * @default 20
             */
            pageSize: number;
            /** @description Field to sort results by */
            sortBy?: components["schemas"]["ListSortField"];
            /**
             * @description Sort order direction
             * @default desc
             * @enum {string}
             */
            sortOrder: "asc" | "desc";
        };
        /** @description Response containing a paginated list of documents */
        ListDocumentsResponse: {
            /** @description The namespace that was listed */
            namespace: string;
            /** @description List of documents in the namespace */
            documents: components["schemas"]["DocumentSummary"][];
            /** @description Total number of documents matching filters */
            totalCount?: number;
            /** @description Current page number */
            page?: number;
            /** @description Number of documents per page */
            pageSize?: number;
            /** @description Total number of pages available */
            totalPages?: number;
        };
        /** @description Request to list encounter types with optional filtering */
        ListEncounterTypesRequest: {
            /**
             * @description Include soft-deleted types
             * @default false
             */
            includeInactive: boolean;
            /**
             * @description Only return built-in types
             * @default false
             */
            builtInOnly: boolean;
            /**
             * @description Only return custom types
             * @default false
             */
            customOnly: boolean;
        };
        /** @description Request to list escrow agreements with optional filters */
        ListEscrowsRequest: {
            /**
             * Format: uuid
             * @description Filter by party
             */
            partyId?: string | null;
            /** @description Party type filter */
            partyType?: components["schemas"]["EntityType"] | null;
            /** @description Filter by status */
            status?: components["schemas"]["EscrowStatus"][] | null;
            /** @description Filter by reference type */
            referenceType?: string | null;
            /**
             * Format: uuid
             * @description Filter by reference ID
             */
            referenceId?: string | null;
            /**
             * Format: date-time
             * @description Filter from date
             */
            fromDate?: string | null;
            /**
             * Format: date-time
             * @description Filter to date
             */
            toDate?: string | null;
            /** @description Limit results */
            limit?: number | null;
            /** @description Offset for pagination */
            offset?: number | null;
        };
        /** @description Response containing list of escrow agreements */
        ListEscrowsResponse: {
            /** @description List of escrow agreements matching the query */
            escrows: components["schemas"]["EscrowAgreement"][];
            /** @description Total count for pagination */
            totalCount: number;
        };
        /** @description Request to list item templates */
        ListItemTemplatesRequest: {
            /** @description Filter by game service */
            gameId?: string;
            /** @description Filter by item category */
            category?: components["schemas"]["ItemCategory"];
            /** @description Filter by subcategory */
            subcategory?: string | null;
            /** @description Filter by tags (items must have all specified tags) */
            tags?: string[] | null;
            /** @description Filter by rarity tier */
            rarity?: components["schemas"]["ItemRarity"];
            /** @description Filter by realm scope */
            scope?: components["schemas"]["ItemScope"];
            /**
             * Format: uuid
             * @description Filter by realm availability
             */
            realmId?: string | null;
            /**
             * @description Include inactive templates
             * @default false
             */
            includeInactive: boolean;
            /**
             * @description Include deprecated templates
             * @default false
             */
            includeDeprecated: boolean;
            /** @description Search in name and description */
            search?: string | null;
            /**
             * @description Pagination offset
             * @default 0
             */
            offset: number;
            /**
             * @description Maximum results to return
             * @default 50
             */
            limit: number;
        };
        /** @description Paginated list of item templates */
        ListItemTemplatesResponse: {
            /** @description List of templates */
            templates: components["schemas"]["ItemTemplateResponse"][];
            /** @description Total number of matching templates */
            totalCount: number;
        };
        /** @description Request to list items in a container */
        ListItemsByContainerRequest: {
            /**
             * Format: uuid
             * @description Container to list items from
             */
            containerId: string;
        };
        /** @description List of item instances */
        ListItemsResponse: {
            /** @description List of items */
            items: components["schemas"]["ItemInstanceResponse"][];
            /** @description Total number of matching items */
            totalCount: number;
        };
        /** @description Request to list all child locations of a specified parent location */
        ListLocationsByParentRequest: {
            /**
             * Format: uuid
             * @description ID of the parent location
             */
            parentLocationId: string;
            /** @description Optional filter by location type */
            locationType?: components["schemas"]["LocationType"] | null;
            /**
             * @description Whether to include deprecated locations in the response
             * @default false
             */
            includeDeprecated: boolean;
            /**
             * @description Page number for pagination (1-indexed)
             * @default 1
             */
            page: number;
            /**
             * @description Number of results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to list all locations within a specific realm with optional filtering */
        ListLocationsByRealmRequest: {
            /**
             * Format: uuid
             * @description Realm ID to query
             */
            realmId: string;
            /** @description Optional type filter */
            locationType?: components["schemas"]["LocationType"] | null;
            /**
             * @description Whether to include deprecated locations in the response
             * @default false
             */
            includeDeprecated: boolean;
            /**
             * @description Page number for pagination (1-indexed)
             * @default 1
             */
            page: number;
            /**
             * @description Number of results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to list locations within a realm with optional type and deprecation filtering */
        ListLocationsRequest: {
            /**
             * Format: uuid
             * @description Realm ID to query (required - locations are partitioned by realm)
             */
            realmId: string;
            /** @description Filter by location type */
            locationType?: components["schemas"]["LocationType"] | null;
            /**
             * @description Whether to include deprecated locations in the response
             * @default false
             */
            includeDeprecated: boolean;
            /**
             * @description Page number for pagination (1-indexed)
             * @default 1
             */
            page: number;
            /**
             * @description Number of results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to list available matchmaking queues */
        ListQueuesRequest: {
            /** @description Filter by game ID (null for all games) */
            gameId?: string | null;
            /**
             * @description Include disabled queues in the list (admin only)
             * @default false
             */
            includeDisabled: boolean;
        };
        /** @description Response containing available matchmaking queues */
        ListQueuesResponse: {
            /** @description List of available queues */
            queues: components["schemas"]["QueueSummary"][];
        };
        /** @description Request to list realms with optional filtering and pagination */
        ListRealmsRequest: {
            /** @description Filter by category (e.g., "MAIN", "SPECIAL", "TEST") */
            category?: string | null;
            /** @description Filter by active status */
            isActive?: boolean | null;
            /**
             * @description Whether to include deprecated realms in the response
             * @default false
             */
            includeDeprecated: boolean;
            /**
             * @description Page number for pagination (1-indexed)
             * @default 1
             */
            page: number;
            /**
             * @description Number of realms to return per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to list relationship types with optional filtering by category, hierarchy, and deprecation status */
        ListRelationshipTypesRequest: {
            /** @description Filter by category (e.g., "FAMILY", "SOCIAL", "ECONOMIC") (null to include all) */
            category?: string | null;
            /**
             * @description Whether to include child types in the response
             * @default true
             */
            includeChildren: boolean;
            /**
             * @description Only return types with no parent (root types)
             * @default false
             */
            rootsOnly: boolean;
            /**
             * @description Whether to include deprecated types in the response
             * @default false
             */
            includeDeprecated: boolean;
        };
        /** @description Request to list all relationships for a specific entity with optional filters */
        ListRelationshipsByEntityRequest: {
            /**
             * Format: uuid
             * @description ID of the entity to get relationships for
             */
            entityId: string;
            /** @description Type of the entity to get relationships for */
            entityType: components["schemas"]["EntityType"];
            /**
             * Format: uuid
             * @description Optional filter by relationship type
             */
            relationshipTypeId?: string | null;
            /** @description Optional filter by the other entity's type */
            otherEntityType?: components["schemas"]["EntityType"];
            /**
             * @description Include relationships that have ended
             * @default false
             */
            includeEnded: boolean;
            /**
             * @description Page number for paginated results (1-based)
             * @default 1
             */
            page: number;
            /**
             * @description Number of results per page (max 100)
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to list all relationships of a specific relationship type */
        ListRelationshipsByTypeRequest: {
            /**
             * Format: uuid
             * @description Relationship type to filter by
             */
            relationshipTypeId: string;
            /** @description Optional filter by entity1 type */
            entity1Type?: components["schemas"]["EntityType"];
            /** @description Optional filter by entity2 type */
            entity2Type?: components["schemas"]["EntityType"];
            /**
             * @description Include relationships that have ended
             * @default false
             */
            includeEnded: boolean;
            /**
             * @description Page number for paginated results (1-based)
             * @default 1
             */
            page: number;
            /**
             * @description Number of results per page (max 100)
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to list all repository bindings with optional filtering */
        ListRepositoryBindingsRequest: {
            /** @description Filter by binding status */
            status?: components["schemas"]["BindingStatus"];
            /**
             * @description Maximum number of bindings to return
             * @default 50
             */
            limit: number;
            /**
             * @description Number of bindings to skip
             * @default 0
             */
            offset: number;
        };
        /** @description Response containing a list of repository bindings */
        ListRepositoryBindingsResponse: {
            /** @description List of repository bindings */
            bindings: components["schemas"]["RepositoryBindingInfo"][];
            /** @description Total number of bindings matching filter */
            total: number;
        };
        /** @description Request to list all top-level locations (without parents) in a realm */
        ListRootLocationsRequest: {
            /**
             * Format: uuid
             * @description Realm ID to get root locations for
             */
            realmId: string;
            /** @description Optional filter by location type */
            locationType?: components["schemas"]["LocationType"] | null;
            /**
             * @description Whether to include deprecated locations in the response
             * @default false
             */
            includeDeprecated: boolean;
            /**
             * @description Page number for pagination (1-indexed)
             * @default 1
             */
            page: number;
            /**
             * @description Number of results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to list scenes with optional filters */
        ListScenesRequest: {
            /** @description Filter by game ID */
            gameId?: string | null;
            /** @description Filter by single scene type */
            sceneType?: components["schemas"]["SceneType"];
            /** @description Filter by multiple scene types (OR) */
            sceneTypes?: components["schemas"]["SceneType"][] | null;
            /** @description Filter by tags (scenes must have ALL specified tags) */
            tags?: string[] | null;
            /** @description Filter by name containing this substring (case-insensitive) */
            nameContains?: string | null;
            /**
             * @description Pagination offset
             * @default 0
             */
            offset: number;
            /**
             * @description Maximum results to return
             * @default 50
             */
            limit: number;
        };
        /** @description Response containing scene list and pagination info */
        ListScenesResponse: {
            /** @description List of scene summaries (not full documents) */
            scenes: components["schemas"]["SceneSummary"][];
            /** @description Total number of matching scenes */
            total: number;
            /** @description Current offset */
            offset?: number;
            /** @description Applied limit */
            limit?: number;
        };
        /** @description Request to list all registered schemas for a namespace */
        ListSchemasRequest: {
            /** @description Schema namespace to list */
            namespace: string;
        };
        /** @description List of registered schemas with latest version indicator */
        ListSchemasResponse: {
            /** @description Registered schemas */
            schemas: components["schemas"]["SchemaResponse"][];
            /** @description Latest schema version */
            latestVersion?: string | null;
        };
        /** @description Request to list all game services */
        ListServicesRequest: {
            /**
             * @description If true, only return active services
             * @default false
             */
            activeOnly: boolean;
        };
        /** @description Response containing list of game services */
        ListServicesResponse: {
            /** @description List of game services matching the request criteria */
            services: components["schemas"]["ServiceInfo"][];
            /** @description Total number of services matching the filter */
            totalCount: number;
        };
        /** @description Request to list all save slots belonging to a specific owner */
        ListSlotsRequest: {
            /** @description Game identifier for namespace isolation */
            gameId: string;
            /**
             * Format: uuid
             * @description ID of the owning entity
             */
            ownerId: string;
            /** @description Type of entity that owns the save slots to list */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Optional filter by save category */
            category?: components["schemas"]["SaveCategory"] | null;
            /**
             * @description Include version count in response
             * @default true
             */
            includeVersionCount: boolean;
        };
        /** @description Response containing a list of save slots for an owner */
        ListSlotsResponse: {
            /** @description List of slots */
            slots: components["schemas"]["SlotResponse"][];
            /** @description Total number of slots for owner */
            totalCount?: number;
        };
        /**
         * @description Fields available for sorting document lists
         * @default updated_at
         * @enum {string}
         */
        ListSortField: "created_at" | "updated_at" | "title";
        /** @description Request to list species available within a specific realm */
        ListSpeciesByRealmRequest: {
            /**
             * Format: uuid
             * @description ID of the realm to filter by
             */
            realmId: string;
            /** @description Filter by playable status */
            isPlayable?: boolean | null;
            /**
             * @description Page number for pagination (1-based)
             * @default 1
             */
            page: number;
            /**
             * @description Number of items per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to list species with optional filtering and pagination */
        ListSpeciesRequest: {
            /** @description Filter by category (e.g., "HUMANOID", "BEAST", "MAGICAL") */
            category?: string | null;
            /** @description Filter by playable status */
            isPlayable?: boolean | null;
            /**
             * @description Whether to include deprecated species in the response
             * @default false
             */
            includeDeprecated: boolean;
            /**
             * @description Page number for pagination (1-based)
             * @default 1
             */
            page: number;
            /**
             * @description Number of items per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to list available styles */
        ListStylesRequest: {
            /** @description Filter by category (e.g., "folk", "classical", "jazz") */
            category?: string | null;
            /**
             * @description Maximum number of styles to return
             * @default 50
             */
            limit: number;
            /**
             * @description Pagination offset
             * @default 0
             */
            offset: number;
        };
        /** @description Response containing a list of styles */
        ListStylesResponse: {
            /** @description Style summaries */
            styles: components["schemas"]["StyleSummary"][];
            /** @description Total number of styles matching filter */
            total: number;
        };
        /** @description Request to list unlocked achievements */
        ListUnlockedAchievementsRequest: {
            /**
             * Format: uuid
             * @description ID of the game service
             */
            gameServiceId: string;
            /**
             * Format: uuid
             * @description ID of the entity
             */
            entityId: string;
            /** @description Entity type whose unlocked achievements are listed */
            entityType: components["schemas"]["EntityType"];
            /** @description Filter by platform */
            platform?: components["schemas"]["Platform"];
        };
        /** @description Response containing unlocked achievements */
        ListUnlockedAchievementsResponse: {
            /**
             * Format: uuid
             * @description ID of the entity
             */
            entityId: string;
            /** @description Entity type for the returned unlocked achievements */
            entityType: components["schemas"]["EntityType"];
            /** @description List of unlocked achievements */
            achievements: components["schemas"]["UnlockedAchievement"][];
            /** @description Total points earned */
            totalPoints: number;
        };
        /** @description Request to list all versions of an asset with pagination */
        ListVersionsRequest: {
            /** @description Asset identifier to list versions for */
            assetId: string;
            /**
             * @description Maximum number of versions to return
             * @default 50
             */
            limit: number;
            /**
             * @description Number of versions to skip for pagination
             * @default 0
             */
            offset: number;
        };
        /** @description Paginated list of save versions within a slot */
        ListVersionsResponse: {
            /** @description List of versions */
            versions: components["schemas"]["VersionResponse"][];
            /** @description Total version count in slot */
            totalCount: number;
        };
        /** @description Request to load save data from a specific slot and version */
        LoadRequest: {
            /** @description Game identifier for namespace isolation */
            gameId: string;
            /**
             * Format: uuid
             * @description ID of the owning entity
             */
            ownerId: string;
            /** @description Type of entity that owns this save slot */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Slot name */
            slotName: string;
            /** @description Specific version to load (defaults to latest) */
            versionNumber?: number | null;
            /** @description Load by checkpoint name instead of version number */
            checkpointName?: string | null;
            /**
             * @description Include version metadata in response
             * @default true
             */
            includeMetadata: boolean;
        };
        /** @description Response containing loaded save data with integrity verification */
        LoadResponse: {
            /**
             * Format: uuid
             * @description Slot identifier
             */
            slotId: string;
            /** @description Version number loaded */
            versionNumber: number;
            /**
             * Format: byte
             * @description Base64-encoded save data (decompressed)
             */
            data: string;
            /** @description SHA-256 hash for integrity verification */
            contentHash: string;
            /** @description Schema version of this save */
            schemaVersion?: string | null;
            /** @description Human-readable name */
            displayName?: string | null;
            /** @description Whether this version is pinned */
            pinned?: boolean;
            /** @description Checkpoint name if pinned */
            checkpointName?: string | null;
            /**
             * Format: date-time
             * @description Save timestamp
             */
            createdAt?: string;
            /** @description Custom metadata */
            metadata?: {
                [key: string]: string;
            };
        };
        /** @description Character location information including current position, region, and 3D coordinates */
        Location: {
            /** @description Current location name or identifier */
            current?: string | null;
            /** @description Region or zone the character is in */
            region?: string | null;
            /** @description 3D spatial coordinates of the character's position in the game world */
            coordinates?: components["schemas"]["Coordinates"];
        };
        /** @description Request to check if a location exists and is active */
        LocationExistsRequest: {
            /**
             * Format: uuid
             * @description ID of the location to validate
             */
            locationId: string;
        };
        /** @description Response indicating whether a location exists and its active status */
        LocationExistsResponse: {
            /** @description Whether the location exists */
            exists: boolean;
            /** @description Whether the location is active (false if deprecated or not found) */
            isActive: boolean;
            /**
             * Format: uuid
             * @description The location ID if found
             */
            locationId?: string | null;
            /**
             * Format: uuid
             * @description The realm ID if location found
             */
            realmId?: string | null;
        };
        /** @description Paginated list of locations with metadata for navigation */
        LocationListResponse: {
            /** @description List of locations matching the query */
            locations: components["schemas"]["LocationResponse"][];
            /** @description Total number of locations matching the query (across all pages) */
            totalCount: number;
            /** @description Current page number (1-indexed) */
            page: number;
            /** @description Number of results per page */
            pageSize: number;
            /** @description Whether there are more pages after the current page */
            hasNextPage?: boolean;
            /** @description Whether there are pages before the current page */
            hasPreviousPage?: boolean;
        };
        /** @description Complete location data returned from API operations */
        LocationResponse: {
            /**
             * Format: uuid
             * @description Unique identifier for the location
             */
            locationId: string;
            /**
             * Format: uuid
             * @description Realm this location belongs to
             */
            realmId: string;
            /** @description Unique code for the location within its realm */
            code: string;
            /** @description Display name of the location */
            name: string;
            /** @description Optional description of the location */
            description?: string | null;
            /** @description Type classification of the location */
            locationType: components["schemas"]["LocationType"];
            /**
             * Format: uuid
             * @description Parent location ID (null for root locations)
             */
            parentLocationId?: string | null;
            /** @description Depth in hierarchy (0 for root locations) */
            depth: number;
            /** @description Whether this location is deprecated and cannot be used */
            isDeprecated: boolean;
            /**
             * Format: date-time
             * @description Timestamp when this location was deprecated
             */
            deprecatedAt?: string | null;
            /** @description Optional reason for deprecation */
            deprecationReason?: string | null;
            /** @description Additional metadata for the location (JSON) */
            metadata?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: date-time
             * @description Timestamp when the location was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Timestamp when the location was last updated
             */
            updatedAt: string;
        };
        /**
         * @description Type classification for locations
         * @enum {string}
         */
        LocationType: "CONTINENT" | "REGION" | "CITY" | "DISTRICT" | "BUILDING" | "ROOM" | "LANDMARK" | "WILDERNESS" | "DUNGEON" | "OTHER";
        /** @description Request to lock a contract under guardian custody */
        LockContractRequest: {
            /**
             * Format: uuid
             * @description Contract instance ID to lock
             */
            contractInstanceId: string;
            /**
             * Format: uuid
             * @description Guardian entity ID (e.g., escrow agreement ID)
             */
            guardianId: string;
            /** @description Guardian entity type (e.g., "escrow") */
            guardianType: string;
            /** @description Optional idempotency key for the operation */
            idempotencyKey?: string | null;
        };
        /** @description Response from locking a contract */
        LockContractResponse: {
            /** @description Whether the contract was locked */
            locked: boolean;
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractId: string;
            /**
             * Format: uuid
             * @description Guardian entity ID
             */
            guardianId?: string;
            /**
             * Format: date-time
             * @description When the contract was locked
             */
            lockedAt?: string;
        };
        /** @description Request to authenticate a user with email and password credentials */
        LoginRequest: {
            /**
             * Format: email
             * @description Email address for authentication
             */
            email: string;
            /**
             * Format: password
             * @description User password for authentication
             */
            password: string;
            /**
             * @description Whether to extend the session duration for persistent login
             * @default false
             */
            rememberMe: boolean;
            /** @description Information about the client device (optional) */
            deviceInfo?: components["schemas"]["DeviceInfo"];
        };
        /** @description Site logo configuration including image URL and accessibility text */
        Logo: {
            /**
             * Format: uri
             * @description URL of the site logo image
             */
            url?: string;
            /** @description Alt text for the logo image */
            alt?: string;
        };
        /** @description Request to logout and invalidate authentication tokens */
        LogoutRequest: {
            /**
             * @description Logout from all sessions/devices
             * @default false
             */
            allSessions: boolean;
        };
        /** @description A map definition template that describes the structure of a region */
        MapDefinition: {
            /**
             * Format: uuid
             * @description Unique identifier for this definition
             */
            definitionId: string;
            /** @description Human-readable name */
            name: string;
            /** @description Description of the map template */
            description?: string | null;
            /** @description Layer configurations for this map */
            layers?: components["schemas"]["LayerDefinition"][] | null;
            /** @description Default bounds for regions using this definition */
            defaultBounds?: components["schemas"]["Bounds"];
            /** @description Additional metadata (schema-less) */
            metadata?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: date-time
             * @description When the definition was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description When the definition was last updated
             */
            updatedAt?: string | null;
        };
        /**
         * @description The category of spatial data this map contains.
         *     Different kinds have different update frequencies, storage models, and TTLs.
         * @enum {string}
         */
        MapKind: "terrain" | "static_geometry" | "navigation" | "resources" | "spawn_points" | "points_of_interest" | "dynamic_objects" | "hazards" | "weather_effects" | "ownership" | "combat_effects" | "visual_effects";
        /** @description A stored map object with full metadata */
        MapObject: {
            /**
             * Format: uuid
             * @description Unique identifier for this object
             */
            objectId: string;
            /**
             * Format: uuid
             * @description Region this object belongs to
             */
            regionId: string;
            /** @description Map kind this object is stored under */
            kind: components["schemas"]["MapKind"];
            /** @description Publisher-defined type */
            objectType: string;
            /** @description Position for point objects */
            position?: components["schemas"]["Position3D"];
            /** @description Bounding box for area objects */
            bounds?: components["schemas"]["Bounds"];
            /** @description Schema-less object data (publisher-defined) */
            data?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: int64
             * @description Monotonic version for ordering
             */
            version?: number;
            /**
             * Format: date-time
             * @description When the object was first created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description When the object was last updated
             */
            updatedAt: string;
        };
        /**
         * @description Types of marker nodes for spawn points, waypoints, and other positional markers.
         * @enum {string}
         */
        MarkerType: "generic" | "spawn_point" | "npc_spawn" | "waypoint" | "camera_point" | "light_point" | "audio_point" | "trigger_point";
        /** @description Request to check if a relationship type matches or descends from an ancestor type in the hierarchy */
        MatchesHierarchyRequest: {
            /**
             * Format: uuid
             * @description The relationship type to check
             */
            typeId: string;
            /**
             * Format: uuid
             * @description The potential ancestor type
             */
            ancestorTypeId: string;
        };
        /** @description Response indicating whether a type matches an ancestor in the hierarchy and the depth between them */
        MatchesHierarchyResponse: {
            /** @description True if typeId equals or descends from ancestorTypeId */
            matches: boolean;
            /** @description Number of levels between the types (0 if same, -1 if no match) */
            depth?: number;
        };
        /** @description Matchmaking operational statistics */
        MatchmakingStatsResponse: {
            /**
             * Format: date-time
             * @description When these stats were collected
             */
            timestamp: string;
            /** @description Statistics per queue */
            queueStats: components["schemas"]["QueueStats"][];
        };
        /** @description Current matchmaking status for a ticket */
        MatchmakingStatusResponse: {
            /**
             * Format: uuid
             * @description Ticket identifier
             */
            ticketId: string;
            /** @description Queue the ticket is in */
            queueId: string;
            /** @description Current ticket status */
            status: components["schemas"]["TicketStatus"];
            /** @description Number of processing intervals elapsed */
            intervalsElapsed: number;
            /** @description Current skill matching range (null if skill not used) */
            currentSkillRange?: number | null;
            /** @description Updated estimated wait time */
            estimatedWaitSeconds?: number | null;
            /**
             * Format: date-time
             * @description When the ticket was created
             */
            createdAt: string;
            /**
             * Format: uuid
             * @description Match ID if a match has been found
             */
            matchId?: string | null;
        };
        /** @description Analysis of a melody */
        MelodyAnalysis: {
            /** @description Pitch range used */
            range?: components["schemas"]["PitchRange"];
            /** @description Distribution of interval sizes */
            intervalDistribution?: {
                [key: string]: number;
            } | null;
            /** @description Detected contour shape */
            contour?: string | null;
            /** @description Total number of notes */
            noteCount?: number;
            /**
             * Format: float
             * @description Average note duration in ticks
             */
            averageNoteDuration?: number | null;
        };
        /** @description Request to merge stacks */
        MergeStacksRequest: {
            /**
             * Format: uuid
             * @description Stack to merge from (destroyed)
             */
            sourceInstanceId: string;
            /**
             * Format: uuid
             * @description Stack to merge into
             */
            targetInstanceId: string;
        };
        /** @description Response after merging */
        MergeStacksResponse: {
            /** @description Whether merge succeeded */
            success: boolean;
            /**
             * Format: uuid
             * @description Merged stack ID
             */
            targetInstanceId: string;
            /**
             * Format: double
             * @description New quantity
             */
            newQuantity: number;
            /** @description Whether source was destroyed */
            sourceDestroyed: boolean;
            /**
             * Format: double
             * @description Quantity that didn't fit
             */
            overflowQuantity?: number | null;
        };
        /**
         * @description Type of metadata to update
         * @enum {string}
         */
        MetadataType: "instance_data" | "runtime_state";
        /** @description A single MIDI event */
        MidiEvent: {
            /** @description Absolute tick position */
            tick: number;
            /** @description Event type */
            type: components["schemas"]["MidiEventType"];
            /** @description MIDI note number (for note events) */
            note?: number | null;
            /** @description Note velocity (for note events) */
            velocity?: number | null;
            /** @description Note duration in ticks (for noteOn with implicit noteOff) */
            duration?: number | null;
            /** @description Program number (for programChange) */
            program?: number | null;
            /** @description Controller number (for controlChange) */
            controller?: number | null;
            /** @description Controller value (for controlChange) */
            value?: number | null;
        };
        /**
         * @description MIDI event type
         * @enum {string}
         */
        MidiEventType: "NoteOn" | "NoteOff" | "ProgramChange" | "ControlChange";
        /** @description MIDI file header information */
        MidiHeader: {
            /**
             * @description MIDI format type (0, 1, or 2)
             * @default 1
             */
            format: number;
            /** @description Composition name */
            name?: string | null;
            /** @description Tempo changes */
            tempos?: components["schemas"]["TempoEvent"][] | null;
            /** @description Time signature changes */
            timeSignatures?: components["schemas"]["TimeSignatureEvent"][] | null;
            /** @description Key signature changes */
            keySignatures?: components["schemas"]["KeySignatureEvent"][] | null;
        };
        /** @description MIDI-JSON format representation of a musical piece */
        MidiJson: {
            /** @description MIDI header information */
            header?: components["schemas"]["MidiHeader"];
            /**
             * @description Ticks per beat (PPQN)
             * @default 480
             */
            ticksPerBeat: number;
            /** @description MIDI tracks */
            tracks: components["schemas"]["MidiTrack"][];
        };
        /** @description A single MIDI track containing events */
        MidiTrack: {
            /** @description Track name */
            name?: string | null;
            /**
             * @description MIDI channel
             * @default 0
             */
            channel: number;
            /** @description GM instrument number */
            instrument?: number | null;
            /** @description Track events */
            events: components["schemas"]["MidiEvent"][];
        };
        /** @description Request to migrate a save to a newer schema version */
        MigrateSaveRequest: {
            /**
             * Format: uuid
             * @description ID of the owning entity
             */
            ownerId: string;
            /** @description Type of entity that owns this save slot */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Slot name */
            slotName: string;
            /** @description Specific version to migrate (defaults to latest) */
            versionNumber?: number;
            /** @description Target schema version to migrate to */
            targetSchemaVersion: string;
            /**
             * @description Validate migration without saving
             * @default false
             */
            dryRun: boolean;
        };
        /** @description Result of a schema migration operation with version path details */
        MigrateSaveResponse: {
            /** @description Whether migration succeeded */
            success: boolean;
            /** @description Original schema version */
            fromSchemaVersion: string;
            /** @description Target schema version */
            toSchemaVersion: string;
            /** @description New version number (null if dry run) */
            newVersionNumber?: number | null;
            /** @description Migration path applied (list of versions) */
            migrationPath?: string[];
            /** @description Non-fatal migration warnings */
            warnings?: string[];
        };
        /** @description Milestone definition in a template */
        MilestoneDefinition: {
            /** @description Unique milestone code within template */
            code: string;
            /** @description Human-readable name */
            name: string;
            /** @description What this milestone represents */
            description?: string | null;
            /** @description Order in the contract flow */
            sequence: number;
            /** @description Whether milestone must be completed */
            required: boolean;
            /** @description Relative deadline (ISO 8601 duration) */
            deadline?: string | null;
            /** @description APIs to call on completion */
            onComplete?: components["schemas"]["PreboundApi"][] | null;
            /** @description APIs to call if deadline passes */
            onExpire?: components["schemas"]["PreboundApi"][] | null;
        };
        /** @description Milestone instance status */
        MilestoneInstanceResponse: {
            /** @description Milestone code */
            code: string;
            /** @description Milestone name */
            name: string;
            /** @description Order in flow */
            sequence: number;
            /** @description Whether required */
            required: boolean;
            /** @description Current status */
            status: components["schemas"]["MilestoneStatus"];
            /**
             * Format: date-time
             * @description When completed
             */
            completedAt?: string | null;
            /**
             * Format: date-time
             * @description When failed
             */
            failedAt?: string | null;
            /**
             * Format: date-time
             * @description Absolute deadline
             */
            deadline?: string | null;
        };
        /** @description Brief milestone progress */
        MilestoneProgressSummary: {
            /** @description Milestone code */
            code: string;
            /** @description Current status */
            status: components["schemas"]["MilestoneStatus"];
        };
        /** @description Milestone details */
        MilestoneResponse: {
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractId: string;
            /** @description Milestone details */
            milestone: components["schemas"]["MilestoneInstanceResponse"];
        };
        /**
         * @description Current status of a milestone
         * @enum {string}
         */
        MilestoneStatus: "pending" | "active" | "completed" | "failed" | "skipped";
        /** @description Probability distribution over musical modes */
        ModeDistribution: {
            /**
             * Format: float
             * @description Probability of major mode
             * @default 0
             */
            major: number;
            /**
             * Format: float
             * @description Probability of natural minor
             * @default 0
             */
            minor: number;
            /**
             * Format: float
             * @description Probability of dorian mode
             * @default 0
             */
            dorian: number;
            /**
             * Format: float
             * @description Probability of phrygian mode
             * @default 0
             */
            phrygian: number;
            /**
             * Format: float
             * @description Probability of lydian mode
             * @default 0
             */
            lydian: number;
            /**
             * Format: float
             * @description Probability of mixolydian mode
             * @default 0
             */
            mixolydian: number;
            /**
             * Format: float
             * @description Probability of aeolian mode
             * @default 0
             */
            aeolian: number;
            /**
             * Format: float
             * @description Probability of locrian mode
             * @default 0
             */
            locrian: number;
        };
        /**
         * @description Musical mode/scale type
         * @enum {string}
         */
        ModeType: "Major" | "Minor" | "Dorian" | "Phrygian" | "Lydian" | "Mixolydian" | "Aeolian" | "Locrian" | "HarmonicMinor" | "MelodicMinor" | "MajorPentatonic" | "MinorPentatonic" | "Blues" | "WholeTone" | "Chromatic";
        /** @description Request to modify item instance state */
        ModifyItemInstanceRequest: {
            /**
             * Format: uuid
             * @description Instance ID to modify
             */
            instanceId: string;
            /** @description Change to durability (positive to repair, negative for damage) */
            durabilityDelta?: number | null;
            /** @description New custom stats (merges with existing) */
            customStats?: Record<string, never> | null;
            /** @description New custom name */
            customName?: string | null;
            /**
             * Format: double
             * @description Change to quantity (positive to add, negative to subtract). Only valid for stackable items.
             */
            quantityDelta?: number | null;
            /** @description New instance metadata (merges with existing) */
            instanceMetadata?: Record<string, never> | null;
            /**
             * Format: uuid
             * @description Move item to a different container. Used by inventory service for item movement.
             */
            newContainerId?: string | null;
            /** @description New slot index within the container */
            newSlotIndex?: number | null;
            /** @description New X position for grid-based containers */
            newSlotX?: number | null;
            /** @description New Y position for grid-based containers */
            newSlotY?: number | null;
        };
        /** @description Request to move item */
        MoveItemRequest: {
            /**
             * Format: uuid
             * @description Item instance ID to move
             */
            instanceId: string;
            /**
             * Format: uuid
             * @description Target container ID
             */
            targetContainerId: string;
            /** @description Target slot */
            targetSlotIndex?: number | null;
            /** @description Target grid X */
            targetSlotX?: number | null;
            /** @description Target grid Y */
            targetSlotY?: number | null;
            /** @description Rotate in target */
            rotated?: boolean | null;
        };
        /** @description Response after moving item */
        MoveItemResponse: {
            /** @description Whether move succeeded */
            success: boolean;
            /**
             * Format: uuid
             * @description Moved item ID
             */
            instanceId: string;
            /**
             * Format: uuid
             * @description Previous container
             */
            sourceContainerId: string;
            /**
             * Format: uuid
             * @description New container
             */
            targetContainerId: string;
            /** @description New slot */
            slotIndex?: number | null;
            /** @description New grid X */
            slotX?: number | null;
            /** @description New grid Y */
            slotY?: number | null;
        };
        /** @description Configuration for multipart uploads of large files */
        MultipartConfig: {
            /** @description Whether multipart upload is required for this file size */
            required?: boolean;
            /** @description Size of each part in bytes */
            partSize?: number;
            /** @description Maximum number of parts */
            maxParts?: number;
            /** @description Pre-signed URLs for each part of the multipart upload */
            uploadUrls?: components["schemas"]["PartUploadInfo"][] | null;
        };
        /** @description Options for narrative-driven composition using the Storyteller engine */
        NarrativeOptions: {
            /**
             * @description Specific narrative template ID (e.g., 'journey_and_return', 'tension_and_release', 'simple_arc').
             *     If not specified, template is inferred from mood or defaults to 'simple_arc'.
             */
            templateId?: string | null;
            /** @description Starting emotional state for the composition */
            initialEmotion?: components["schemas"]["EmotionalStateInput"];
            /** @description Target emotional state for the ending */
            targetEmotion?: components["schemas"]["EmotionalStateInput"];
            /**
             * @description Preferred tension curve shape throughout the composition
             * @enum {string|null}
             */
            tensionProfile?: "gradual_build" | "early_climax" | "late_climax" | "sustained" | "wave" | null;
        };
        /** @description A navigation menu entry with optional nested children for dropdowns */
        NavigationItem: {
            /** @description Display text for the navigation link */
            label: string;
            /** @description Target URL or path for the navigation link */
            url: string;
            /** @description Sort order for the navigation item */
            order: number;
            /**
             * @description Link target attribute for opening behavior
             * @default _self
             * @enum {string}
             */
            target: "_self" | "_blank";
            /** @description Nested child navigation items for dropdowns */
            children?: components["schemas"]["NavigationItem"][];
        };
        /** @description Information about a nearby object perceived by the character */
        NearbyObject: {
            /**
             * Format: uuid
             * @description Unique identifier of the object
             */
            objectId?: string;
            /** @description Type of object (boulder_cluster, tree, building, etc.) */
            objectType?: string;
            /**
             * Format: float
             * @description Distance from character in game units
             */
            distance?: number;
            /** @description Relative direction (north, south, east, west, above, below, etc.) */
            direction?: string;
            /** @description Optional absolute position */
            position?: components["schemas"]["Position3D"] | null;
        } & {
            [key: string]: unknown;
        };
        /** @description A single news article or announcement entry */
        NewsItem: {
            /**
             * Format: uuid
             * @description Unique identifier for the news item
             */
            id: string;
            /** @description Headline of the news item */
            title: string;
            /** @description Brief summary or excerpt of the news content */
            summary: string;
            /** @description Full content body of the news item */
            content?: string | null;
            /** @description Name of the news item author */
            author?: string;
            /**
             * Format: date-time
             * @description Date and time when the news was published
             */
            publishedAt: string;
            /** @description Category tags associated with the news item */
            tags?: string[];
            /**
             * Format: uri
             * @description URL of the featured image for the news item
             */
            imageUrl?: string | null;
        };
        /** @description Paginated list of news items with total count */
        NewsResponse: {
            /** @description List of news items for the current page */
            items: components["schemas"]["NewsItem"][];
            /** @description Total number of news items available */
            total: number;
            /** @description Whether more news items are available beyond this page */
            hasMore?: boolean;
        };
        /**
         * @description Structural node type. Indicates what kind of data the node contains,
         *     not how it will be used at runtime. Consumers interpret nodes according
         *     to their own needs via tags and annotations.
         * @enum {string}
         */
        NodeType: "group" | "mesh" | "marker" | "volume" | "emitter" | "reference" | "custom";
        /**
         * @description How to handle publish attempts from non-authority sources
         * @default reject_and_alert
         * @enum {string}
         */
        NonAuthorityHandlingMode: "reject_and_alert" | "accept_and_alert" | "reject_silent";
        /** @description A musical note event with timing and pitch */
        NoteEvent: {
            /** @description Note pitch */
            pitch: components["schemas"]["Pitch"];
            /** @description Start position in ticks */
            startTick: number;
            /** @description Duration in ticks */
            durationTicks: number;
            /**
             * @description Note velocity
             * @default 80
             */
            velocity: number;
        };
        /** @description Request containing OAuth provider callback data to complete authentication */
        OAuthCallbackRequest: {
            /** @description Authorization code returned by the OAuth provider */
            code: string;
            /** @description State parameter for CSRF protection, must match the value sent in the init request */
            state?: string | null;
            /** @description Information about the client device (optional) */
            deviceInfo?: components["schemas"]["DeviceInfo"];
        };
        /**
         * @description Type of entity that owns this save slot
         * @enum {string}
         */
        OwnerType: "ACCOUNT" | "CHARACTER" | "SESSION" | "REALM";
        /** @description Full content and metadata for a CMS-managed page */
        PageContent: {
            /** @description URL-friendly identifier for the page */
            slug: string;
            /** @description Display title of the page */
            title: string;
            /** @description HTML, Markdown, or custom template content */
            content: string;
            /**
             * @description Format of the page content
             * @enum {string}
             */
            contentType: "html" | "markdown" | "blazor";
            /** @description Template name for custom layouts */
            template?: string | null;
            /** @description Whether the page is publicly visible */
            published: boolean;
            /**
             * Format: date-time
             * @description Date and time when the page was published
             */
            publishedAt?: string | null;
            /**
             * Format: date-time
             * @description Date and time of the last modification
             */
            lastModified: string;
            /** @description Name or identifier of the page author */
            author?: string | null;
            /** @description Custom metadata for the page */
            metadata?: {
                [key: string]: unknown;
            };
            /** @description Search engine optimization settings for the page */
            seo?: components["schemas"]["SEOMetadata"];
        };
        /** @description Summary metadata for a CMS page without full content */
        PageMetadata: {
            /** @description URL-friendly identifier for the page */
            slug: string;
            /** @description Display title of the page */
            title: string;
            /** @description Whether the page is publicly visible */
            published: boolean;
            /**
             * Format: date-time
             * @description Date and time when the page was published
             */
            publishedAt?: string | null;
            /**
             * Format: date-time
             * @description Date and time of the last modification
             */
            lastModified: string;
            /** @description Name or identifier of the page author */
            author?: string | null;
        };
        /** @description Upload information for a single part in a multipart upload */
        PartUploadInfo: {
            /** @description Part number (1-based) */
            partNumber: number;
            /**
             * Format: uri
             * @description Pre-signed URL for uploading this part
             */
            uploadUrl: string;
            /**
             * Format: int64
             * @description Minimum size for this part
             */
            minSize?: number;
            /**
             * Format: int64
             * @description Maximum size for this part
             */
            maxSize?: number;
        };
        /** @description Paginated list of participation records */
        ParticipationListResponse: {
            /** @description List of participation records */
            participations: components["schemas"]["HistoricalParticipation"][];
            /** @description Total number of matching records */
            totalCount: number;
            /** @description Current page number (1-based) */
            page: number;
            /** @description Number of results per page */
            pageSize: number;
            /** @description Whether there are more results after this page */
            hasNextPage?: boolean;
            /** @description Whether there are results before this page */
            hasPreviousPage?: boolean;
        };
        /**
         * @description How the character participated in the historical event
         * @enum {string}
         */
        ParticipationRole: "LEADER" | "COMBATANT" | "VICTIM" | "WITNESS" | "BENEFICIARY" | "CONSPIRATOR" | "HERO" | "SURVIVOR";
        /** @description Asset requirement status for a single party */
        PartyAssetRequirementStatus: {
            /** @description Party role (e.g., party_a, party_b) */
            partyRole: string;
            /** @description Whether all party's requirements are satisfied */
            satisfied: boolean;
            /** @description Status of each clause for this party */
            clauses: components["schemas"]["ClauseAssetStatus"][];
        };
        /** @description Consent status for a single party */
        PartyConsentStatus: {
            /**
             * Format: uuid
             * @description Party ID
             */
            partyId: string;
            /** @description Type of entity (Account, Character, etc.) */
            partyType: components["schemas"]["EntityType"];
            /** @description Party display name */
            displayName?: string | null;
            /** @description Whether consent was given */
            consentGiven: boolean;
            /** @description Type of consent given (if any) */
            consentType?: components["schemas"]["EscrowConsentType"];
            /**
             * Format: date-time
             * @description When consent was given
             */
            consentedAt?: string | null;
        };
        /** @description Information about a party member for matchmaking */
        PartyMemberInfo: {
            /**
             * Format: uuid
             * @description Account ID of the party member
             */
            accountId: string;
            /**
             * Format: uuid
             * @description WebSocket session ID for event delivery
             */
            webSocketSessionId: string;
            /** @description Pre-fetched skill rating (optional, will be looked up if not provided) */
            skillRating?: number | null;
        };
        /** @description Definition of a party role in a contract template */
        PartyRoleDefinition: {
            /** @description Role identifier (employer, employee, buyer, seller, etc.) */
            role: string;
            /** @description Minimum entities required in this role */
            minCount: number;
            /** @description Maximum entities allowed in this role */
            maxCount: number;
            /** @description Which entity types can fill this role (null for any) */
            allowedEntityTypes?: components["schemas"]["EntityType"][] | null;
        };
        /**
         * @description Method for aggregating party member skills
         * @enum {string}
         */
        PartySkillAggregation: "highest" | "average" | "weighted";
        /** @description Token issued to a party for deposit or release operations */
        PartyToken: {
            /**
             * Format: uuid
             * @description Party ID
             */
            partyId: string;
            /** @description Type of entity (Account, Character, etc.) */
            partyType: components["schemas"]["EntityType"];
            /** @description The token */
            token: string;
        };
        /** @description Request to confirm a password reset using the emailed token and new password */
        PasswordResetConfirmRequest: {
            /** @description Password reset token received via email */
            token: string;
            /**
             * Format: password
             * @description New password to set for the account
             */
            newPassword: string;
        };
        /** @description Request to initiate a password reset by sending a reset link to the email */
        PasswordResetRequest: {
            /**
             * Format: email
             * @description Email address associated with the account to reset
             */
            email: string;
        };
        /** @description Reference to a past incarnation */
        PastLifeReference: {
            /**
             * Format: uuid
             * @description ID of the previous incarnation
             */
            characterId: string;
            /** @description Display name of the past life */
            name?: string | null;
            /**
             * Format: date-time
             * @description When the past life ended
             */
            deathDate?: string | null;
        };
        /**
         * @description When payments occur
         * @enum {string}
         */
        PaymentSchedule: "one_time" | "recurring" | "milestone_based";
        /** @description Summary of pending consent */
        PendingConsentSummary: {
            /**
             * Format: uuid
             * @description Entity ID
             */
            entityId: string;
            /** @description Entity type */
            entityType: components["schemas"]["EntityType"];
            /** @description Role in contract */
            role: string;
        };
        /**
         * @description Data representing a perception event for an actor.
         *
         *     Spatial context can be provided in two ways (hybrid approach):
         *     1. Typed: Use the optional spatialContext field for structured spatial data
         *     2. Schema-less: Use perceptionType="spatial" with data containing spatial info
         *
         *     The typed approach is recommended when game server has structured spatial data.
         *     The schema-less approach allows flexibility for game-specific spatial formats.
         */
        PerceptionData: {
            /**
             * @description Perception type. Common values: visual, auditory, tactile, olfactory,
             *     proprioceptive, spatial. Use "spatial" for schema-less spatial data in 'data' field.
             */
            perceptionType: string;
            /** @description ID of the entity causing this perception */
            sourceId: string;
            /** @description Type of source (character, npc, object, environment, coordinator, scheduled, message) */
            sourceType?: components["schemas"]["PerceptionSourceType"] | null;
            /**
             * @description Perception-specific data. For perceptionType="spatial", this can contain
             *     game-specific spatial context in any format the game server defines.
             */
            data?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: float
             * @description How urgent this perception is (0-1)
             * @default 0.5
             */
            urgency: number;
            /**
             * @description Optional typed spatial context from game server's local spatial state.
             *     Provides structured information about terrain, nearby objects, hazards, etc.
             *     Alternative to using perceptionType="spatial" with schema-less data.
             */
            spatialContext?: components["schemas"]["SpatialContext"] | null;
        };
        /**
         * @description Type of source generating a perception event
         * @enum {string}
         */
        PerceptionSourceType: "character" | "npc" | "object" | "environment" | "coordinator" | "scheduled" | "message" | "service" | "system";
        /** @description Complete personality profile for behavior system consumption */
        PersonalityResponse: {
            /**
             * Format: uuid
             * @description Character this personality belongs to
             */
            characterId: string;
            /** @description All trait axis values for this character */
            traits: components["schemas"]["TraitValue"][];
            /** @description Personality version number (increments on each evolution) */
            version: number;
            /**
             * @description Optional archetype code for behavior optimization (e.g., "guardian",
             *     "merchant", "scholar", "trickster"). Allows behavior system to use
             *     pre-compiled behavior variants for common personality patterns.
             */
            archetypeHint?: string | null;
            /**
             * Format: date-time
             * @description When this personality was first created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description When this personality was last modified
             */
            updatedAt?: string | null;
        };
        /** @description Snapshot of personality traits for enriched response */
        PersonalitySnapshot: {
            /** @description Trait values keyed by trait name (OPENNESS, AGREEABLENESS, etc.) */
            traits: {
                [key: string]: number;
            };
            /** @description Personality version number (increments on evolution) */
            version: number;
        };
        /** @description Response containing a perspective */
        PerspectiveResponse: {
            /** @description The character's perspective on the encounter */
            perspective: components["schemas"]["EncounterPerspectiveModel"];
        };
        /** @description Request to pin a save version as a checkpoint to prevent cleanup */
        PinVersionRequest: {
            /**
             * Format: uuid
             * @description ID of the owning entity
             */
            ownerId: string;
            /** @description Type of entity that owns this save slot */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Slot name */
            slotName: string;
            /** @description Version to pin */
            versionNumber: number;
            /** @description Optional checkpoint name for easy retrieval */
            checkpointName?: string;
        };
        /** @description A specific pitch with pitch class and octave */
        Pitch: {
            /** @description Pitch class (note name) */
            pitchClass: components["schemas"]["PitchClass"];
            /** @description Octave number (middle C = C4) */
            octave: number;
            /** @description MIDI note number (computed if not provided) */
            midiNumber?: number;
        };
        /**
         * @description A pitch class (note name without octave)
         * @enum {string}
         */
        PitchClass: "C" | "Cs" | "D" | "Ds" | "E" | "F" | "Fs" | "G" | "Gs" | "A" | "As" | "B";
        /** @description A pitch range from low to high */
        PitchRange: {
            /** @description Lowest pitch (inclusive) */
            low: components["schemas"]["Pitch"];
            /** @description Highest pitch (inclusive) */
            high: components["schemas"]["Pitch"];
        };
        /** @description Single action within a GOAP plan with position and cost information */
        PlannedActionResponse: {
            /** @description ID of the action (flow name) */
            actionId: string;
            /** @description Position in the plan sequence */
            index: number;
            /**
             * Format: float
             * @description Cost of this action
             */
            cost: number;
        };
        /**
         * @description External platform for achievement sync
         * @enum {string}
         */
        Platform: "steam" | "xbox" | "playstation" | "internal";
        /**
         * @description Role of the player in the game session
         * @enum {string}
         */
        PlayerRole: "player" | "spectator" | "moderator";
        /** @description 3D position in world coordinates */
        Position3D: {
            /**
             * Format: float
             * @description X coordinate
             */
            x: number;
            /**
             * Format: float
             * @description Y coordinate (typically vertical)
             */
            y: number;
            /**
             * Format: float
             * @description Z coordinate
             */
            z: number;
        };
        /** @description Pre-configured API call to execute on contract events */
        PreboundApi: {
            /** @description Target service name */
            serviceName: string;
            /** @description Target endpoint path */
            endpoint: string;
            /** @description JSON payload with variable placeholders */
            payloadTemplate: string;
            /** @description Human-readable description */
            description?: string | null;
            /**
             * @description How to execute the API call
             * @default sync
             * @enum {string}
             */
            executionMode: "sync" | "async" | "fire_and_forget";
            /** @description Optional validation rules for the response */
            responseValidation?: components["schemas"]["ResponseValidation"];
        };
        /**
         * @description Preferred engagement distance. Influences positioning and
         *     ability selection in combat.
         * @enum {string}
         */
        PreferredRange: "MELEE" | "CLOSE" | "MEDIUM" | "RANGED";
        /**
         * @description Asset processing pipeline status
         * @enum {string}
         */
        ProcessingStatus: "pending" | "processing" | "complete" | "failed";
        /** @description Analysis of a chord progression */
        ProgressionAnalysis: {
            /** @description Roman numeral analysis */
            romanNumerals?: string[] | null;
            /** @description Detected cadences */
            cadences?: components["schemas"]["CadenceInfo"][] | null;
            /** @description Functional analysis per chord */
            functionalAnalysis?: ("tonic" | "subdominant" | "dominant" | "predominant")[] | null;
        };
        /** @description Request to promote an older save version to be the latest */
        PromoteVersionRequest: {
            /** @description Game identifier for namespace isolation */
            gameId: string;
            /**
             * Format: uuid
             * @description ID of the owning entity
             */
            ownerId: string;
            /** @description Type of entity that owns this save slot */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Name of the slot containing the version to promote */
            slotName: string;
            /** @description Old version to promote to latest */
            versionNumber: number;
            /** @description Display name for promoted version */
            displayName?: string | null;
        };
        /** @description Request to propose a contract to parties */
        ProposeContractInstanceRequest: {
            /**
             * Format: uuid
             * @description Contract instance to propose
             */
            contractId: string;
        };
        /**
         * @description Authentication provider type
         * @enum {string}
         */
        Provider: "google" | "discord" | "twitch" | "steam";
        /** @description Information about an available authentication provider */
        ProviderInfo: {
            /**
             * @description Internal identifier for the provider (matches Provider enum for OAuth)
             * @example discord
             */
            name: string;
            /**
             * @description Human-readable name for the provider
             * @example Discord
             */
            displayName: string;
            /**
             * @description Authentication mechanism (oauth = browser redirect, ticket = game client token)
             * @enum {string}
             */
            authType: "oauth" | "ticket";
            /**
             * Format: uri
             * @description URL to initiate OAuth authentication (null for ticket-based auth like Steam)
             * @example https://discord.com/oauth2/authorize
             */
            authUrl?: string | null;
        };
        /** @description List of available authentication providers */
        ProvidersResponse: {
            /** @description Available authentication providers */
            providers: components["schemas"]["ProviderInfo"][];
        };
        /**
         * @description How quantities are tracked for this item type
         * @enum {string}
         */
        QuantityModel: "discrete" | "continuous" | "unique";
        /** @description Rotation represented as a quaternion */
        Quaternion: {
            /**
             * Format: double
             * @description X component
             */
            x: number;
            /**
             * Format: double
             * @description Y component
             */
            y: number;
            /**
             * Format: double
             * @description Z component
             */
            z: number;
            /**
             * Format: double
             * @description W component (scalar)
             */
            w: number;
        };
        /** @description Request to query active contracts */
        QueryActiveContractsRequest: {
            /**
             * Format: uuid
             * @description Entity to query
             */
            entityId: string;
            /** @description Entity type */
            entityType: components["schemas"]["EntityType"];
            /** @description Filter by template codes */
            templateCodes?: string[] | null;
        };
        /** @description Active contracts for entity */
        QueryActiveContractsResponse: {
            /** @description Active contracts */
            contracts: components["schemas"]["ContractSummary"][];
        };
        /** @description Request to query encounters between two characters */
        QueryBetweenRequest: {
            /**
             * Format: uuid
             * @description First character
             */
            characterIdA: string;
            /**
             * Format: uuid
             * @description Second character
             */
            characterIdB: string;
            /** @description Filter by encounter type */
            encounterTypeCode?: string | null;
            /**
             * Format: float
             * @description Filter by minimum memory strength (for either character)
             */
            minimumMemoryStrength?: number | null;
            /**
             * @description Page number (1-based)
             * @default 1
             */
            page: number;
            /**
             * @description Results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Query map data within bounds */
        QueryBoundsRequest: {
            /**
             * Format: uuid
             * @description Region to query
             */
            regionId: string;
            /** @description Bounding box to query */
            bounds: components["schemas"]["Bounds"];
            /** @description Kinds to query (default all) */
            kinds?: components["schemas"]["MapKind"][] | null;
            /**
             * @description Maximum objects to return
             * @default 500
             */
            maxObjects: number;
        };
        /** @description Bounds query results */
        QueryBoundsResponse: {
            /** @description Objects within bounds */
            objects?: components["schemas"]["MapObject"][];
            /** @description Queried bounds */
            bounds?: components["schemas"]["Bounds"];
            /** @description Whether results were truncated */
            truncated?: boolean;
        };
        /** @description Request to find bundles containing a specific asset */
        QueryBundlesByAssetRequest: {
            /** @description Platform asset ID to search for */
            assetId: string;
            /** @description Game realm to search within */
            realm: components["schemas"]["GameRealm"];
            /** @description Filter by bundle type (optional, null for all types) */
            bundleType?: components["schemas"]["BundleType"] | null;
            /**
             * @description Maximum results to return
             * @default 50
             */
            limit: number;
            /**
             * @description Pagination offset
             * @default 0
             */
            offset: number;
        };
        /** @description Bundles containing the requested asset */
        QueryBundlesByAssetResponse: {
            /** @description The queried asset ID */
            assetId: string;
            /** @description Bundles containing this asset */
            bundles: components["schemas"]["BundleSummary"][];
            /** @description Total matching bundles */
            total: number;
            /** @description Page size */
            limit: number;
            /** @description Page offset */
            offset: number;
        };
        /** @description Advanced bundle query with filters */
        QueryBundlesRequest: {
            /** @description Filter by exact tag key-value matches */
            tags?: {
                [key: string]: string;
            } | null;
            /** @description Filter bundles that have these tag keys (any value) */
            tagExists?: string[] | null;
            /** @description Filter bundles that do NOT have these tag keys */
            tagNotExists?: string[] | null;
            /** @description Filter by lifecycle status (null for active only by default) */
            status?: components["schemas"]["BundleLifecycle"] | null;
            /**
             * Format: date-time
             * @description Filter bundles created after this time
             */
            createdAfter?: string | null;
            /**
             * Format: date-time
             * @description Filter bundles created before this time
             */
            createdBefore?: string | null;
            /** @description Filter bundles with name containing this string (case-insensitive) */
            nameContains?: string | null;
            /** @description Filter by bundle owner account ID */
            owner?: string | null;
            /** @description Filter by realm */
            realm?: components["schemas"]["GameRealm"] | null;
            /** @description Filter by bundle type (source or metabundle) */
            bundleType?: components["schemas"]["BundleType"] | null;
            /**
             * @description Field to sort by (default created_at)
             * @enum {string|null}
             */
            sortField?: "created_at" | "updated_at" | "name" | "size" | null;
            /**
             * @description Sort order (default desc)
             * @enum {string|null}
             */
            sortOrder?: "asc" | "desc" | null;
            /**
             * @description Maximum results to return (max 1000)
             * @default 100
             */
            limit: number;
            /**
             * @description Pagination offset
             * @default 0
             */
            offset: number;
            /**
             * @description Include soft-deleted bundles in results
             * @default false
             */
            includeDeleted: boolean;
        };
        /** @description Bundle query results */
        QueryBundlesResponse: {
            /** @description Matching bundles */
            bundles: components["schemas"]["BundleInfo"][];
            /** @description Total number of matching bundles (for pagination) */
            totalCount: number;
            /** @description Page size used */
            limit: number;
            /** @description Page offset used */
            offset: number;
        };
        /** @description Request to query encounters by character */
        QueryByCharacterRequest: {
            /**
             * Format: uuid
             * @description Character to query encounters for
             */
            characterId: string;
            /** @description Filter by encounter type */
            encounterTypeCode?: string | null;
            /** @description Filter by outcome */
            outcome?: components["schemas"]["EncounterOutcome"];
            /**
             * Format: float
             * @description Filter by minimum memory strength
             */
            minimumMemoryStrength?: number | null;
            /**
             * Format: date-time
             * @description Filter encounters after this time
             */
            fromTimestamp?: string | null;
            /**
             * Format: date-time
             * @description Filter encounters before this time
             */
            toTimestamp?: string | null;
            /**
             * @description Page number (1-based)
             * @default 1
             */
            page: number;
            /**
             * @description Results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to query recent encounters at a location */
        QueryByLocationRequest: {
            /**
             * Format: uuid
             * @description Location to query
             */
            locationId: string;
            /** @description Filter by encounter type */
            encounterTypeCode?: string | null;
            /**
             * Format: date-time
             * @description Filter encounters after this time
             */
            fromTimestamp?: string | null;
            /**
             * @description Page number (1-based)
             * @default 1
             */
            page: number;
            /**
             * @description Results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Request to query contract instances */
        QueryContractInstancesRequest: {
            /**
             * Format: uuid
             * @description Filter by party entity ID
             */
            partyEntityId?: string | null;
            /** @description Filter by party entity type */
            partyEntityType?: components["schemas"]["EntityType"];
            /**
             * Format: uuid
             * @description Filter by template
             */
            templateId?: string | null;
            /** @description Filter by statuses */
            statuses?: components["schemas"]["ContractStatus"][] | null;
            /**
             * @description Page number (1-based)
             * @default 1
             */
            page: number;
            /**
             * @description Results per page
             * @default 20
             */
            pageSize: number;
        };
        /** @description Paginated list of contract instances */
        QueryContractInstancesResponse: {
            /** @description List of contracts */
            contracts: components["schemas"]["ContractInstanceResponse"][];
            /** @description Total matching contracts */
            totalCount: number;
            /** @description Current page number */
            page: number;
            /** @description Results per page */
            pageSize: number;
            /** @description Whether more results exist */
            hasNextPage?: boolean;
        };
        /** @description Request to search documentation using natural language queries */
        QueryDocumentationRequest: {
            /** @description Documentation namespace to search within */
            namespace: string;
            /** @description Natural language query to search for */
            query: string;
            /**
             * Format: uuid
             * @description Optional session ID for conversational context
             */
            sessionId?: string;
            /** @description Filter results to a specific category */
            category?: components["schemas"]["DocumentCategory"];
            /**
             * @description Maximum number of results to return
             * @default 5
             */
            maxResults: number;
            /**
             * @description Whether to include full document content in results
             * @default false
             */
            includeContent: boolean;
            /**
             * @description Maximum length of summaries in characters
             * @default 300
             */
            maxSummaryLength: number;
            /**
             * Format: float
             * @description Minimum relevance score threshold for results
             * @default 0.3
             */
            minRelevanceScore: number;
        };
        /** @description Response containing search results and voice-friendly summaries */
        QueryDocumentationResponse: {
            /** @description The namespace that was searched */
            namespace: string;
            /** @description The original query string */
            query: string;
            /** @description List of matching documents */
            results: components["schemas"]["DocumentResult"][];
            /** @description Total number of matching documents */
            totalResults?: number;
            /** @description Concise spoken summary for voice AI */
            voiceSummary?: string;
            /** @description Suggested follow-up queries */
            suggestedFollowups?: string[];
            /** @description User-friendly message when no results found */
            noResultsMessage?: string;
        };
        /** @description Request to query items */
        QueryItemsRequest: {
            /**
             * Format: uuid
             * @description Owner to search
             */
            ownerId: string;
            /** @description Owner type */
            ownerType: components["schemas"]["ContainerOwnerType"];
            /**
             * Format: uuid
             * @description Filter by template
             */
            templateId?: string | null;
            /** @description Filter by category */
            category?: string | null;
            /** @description Filter by tags */
            tags?: string[] | null;
            /** @description Filter by container type */
            containerType?: string | null;
            /**
             * @description Exclude equipment slots
             * @default false
             */
            excludeEquipmentSlots: boolean;
            /**
             * @description Pagination offset
             * @default 0
             */
            offset: number;
            /**
             * @description Max results
             * @default 50
             */
            limit: number;
        };
        /** @description Query results */
        QueryItemsResponse: {
            /** @description Found items */
            items: components["schemas"]["QueryResultItem"][];
            /** @description Total matching */
            totalCount: number;
        };
        /** @description Query objects by type */
        QueryObjectsByTypeRequest: {
            /**
             * Format: uuid
             * @description Region to query
             */
            regionId: string;
            /** @description Object type to filter by */
            objectType: string;
            /** @description Optional bounds filter */
            bounds?: components["schemas"]["Bounds"];
            /**
             * @description Maximum objects to return
             * @default 500
             */
            maxObjects: number;
        };
        /** @description Object type query results */
        QueryObjectsByTypeResponse: {
            /** @description Matching objects */
            objects?: components["schemas"]["MapObject"][];
            /** @description Queried object type */
            objectType?: string;
            /** @description Whether results were truncated */
            truncated?: boolean;
        };
        /** @description Query map data at a point */
        QueryPointRequest: {
            /**
             * Format: uuid
             * @description Region to query
             */
            regionId: string;
            /** @description Point to query at */
            position: components["schemas"]["Position3D"];
            /** @description Kinds to query (default all) */
            kinds?: components["schemas"]["MapKind"][] | null;
            /** @description Include objects within this radius */
            radius?: number | null;
        };
        /** @description Point query results */
        QueryPointResponse: {
            /** @description Objects at/near the point */
            objects?: components["schemas"]["MapObject"][];
            /** @description Queried position */
            position?: components["schemas"]["Position3D"];
            /** @description Applied radius filter */
            radius?: number | null;
        };
        /** @description Item in query results */
        QueryResultItem: {
            /**
             * Format: uuid
             * @description Item instance ID
             */
            instanceId: string;
            /**
             * Format: uuid
             * @description Template ID
             */
            templateId: string;
            /**
             * Format: uuid
             * @description Container ID
             */
            containerId: string;
            /** @description Container type */
            containerType?: string;
            /**
             * Format: double
             * @description Quantity
             */
            quantity: number;
            /** @description Slot position */
            slotIndex?: number | null;
        };
        /** @description Advanced query for saves across multiple owners with filtering and sorting */
        QuerySavesRequest: {
            /**
             * Format: uuid
             * @description Filter by owner ID
             */
            ownerId?: string | null;
            /** @description Filter by owner type */
            ownerType?: components["schemas"]["OwnerType"] | null;
            /** @description Filter by save category */
            category?: components["schemas"]["SaveCategory"] | null;
            /**
             * Format: date-time
             * @description Filter by creation date
             */
            createdAfter?: string | null;
            /**
             * Format: date-time
             * @description Filter by creation date
             */
            createdBefore?: string | null;
            /** @description Only return pinned versions */
            pinnedOnly?: boolean | null;
            /** @description Filter by schema version */
            schemaVersion?: string | null;
            /** @description Filter by metadata key-value pairs */
            metadataFilter?: {
                [key: string]: string;
            } | null;
            /**
             * @description Pagination offset
             * @default 0
             */
            offset: number;
            /**
             * @description Maximum results
             * @default 20
             */
            limit: number;
            /**
             * @description Sort field
             * @default created_at
             * @enum {string}
             */
            sortBy: "created_at" | "size" | "version_number";
            /**
             * @description Sort order
             * @default desc
             * @enum {string}
             */
            sortOrder: "asc" | "desc";
        };
        /** @description Paginated results from a save query operation */
        QuerySavesResponse: {
            /** @description Query results */
            results: components["schemas"]["QueryResultItem"][];
            /** @description Total matching results */
            totalCount: number;
        };
        /** @description Full configuration details of a matchmaking queue */
        QueueResponse: {
            /** @description Unique identifier for the queue */
            queueId: string;
            /** @description Game this queue is for */
            gameId: string;
            /** @description Game type for created sessions (maps to game-session service) */
            sessionGameType?: components["schemas"]["SessionGameType"];
            /** @description Human-readable queue name */
            displayName: string;
            /** @description Detailed description of the queue */
            description?: string | null;
            /** @description Whether the queue is currently accepting tickets */
            enabled: boolean;
            /** @description Minimum players required for a match */
            minCount: number;
            /** @description Maximum players in a match */
            maxCount: number;
            /** @description Player count must be divisible by this (e.g., 2 for pairs) */
            countMultiple: number;
            /** @description Seconds between match processing intervals */
            intervalSeconds: number;
            /** @description Maximum intervals before relaxing to minCount */
            maxIntervals: number;
            /** @description Skill window expansion steps */
            skillExpansion?: components["schemas"]["SkillExpansionStep"][] | null;
            /** @description How to calculate party skill rating */
            partySkillAggregation?: components["schemas"]["PartySkillAggregation"];
            /** @description Weights for weighted party skill aggregation */
            partySkillWeights?: number[] | null;
            /** @description Maximum party size for this queue */
            partyMaxSize?: number | null;
            /**
             * @description Whether players can be in multiple queues
             * @default true
             */
            allowConcurrent: boolean;
            /** @description Exclusive group name (player can only be in one queue of the group) */
            exclusiveGroup?: string | null;
            /**
             * @description Whether to use lib-analytics skill rating for matching
             * @default true
             */
            useSkillRating: boolean;
            /** @description lib-analytics rating category to use */
            ratingCategory?: string | null;
            /**
             * @description Start match with minCount after maxIntervals (for large lobbies)
             * @default false
             */
            startWhenMinimumReached: boolean;
            /**
             * @description Whether players must be registered for a tournament
             * @default false
             */
            requiresRegistration: boolean;
            /**
             * @description Whether a tournament ID is required to join
             * @default false
             */
            tournamentIdRequired: boolean;
            /**
             * @description Seconds players have to accept/decline a formed match
             * @default 30
             */
            matchAcceptTimeoutSeconds: number;
            /**
             * Format: date-time
             * @description When the queue was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description When the queue was last updated
             */
            updatedAt?: string | null;
        };
        /** @description Statistics for a single matchmaking queue */
        QueueStats: {
            /** @description Queue identifier */
            queueId: string;
            /** @description Number of active tickets */
            currentTickets: number;
            /** @description Matches formed in the last hour */
            matchesFormedLastHour: number;
            /** @description Average wait time in seconds */
            averageWaitSeconds: number;
            /** @description Median wait time in seconds */
            medianWaitSeconds?: number | null;
            /** @description Percentage of tickets that timed out */
            timeoutRatePercent?: number | null;
            /** @description Percentage of tickets cancelled by user */
            cancelRatePercent?: number | null;
        };
        /** @description Summary information about a matchmaking queue */
        QueueSummary: {
            /** @description Unique identifier for the queue */
            queueId: string;
            /** @description Game this queue is for */
            gameId: string;
            /** @description Human-readable queue name */
            displayName: string;
            /** @description Whether the queue is currently accepting tickets */
            enabled: boolean;
            /** @description Minimum players required for a match */
            minCount: number;
            /** @description Maximum players in a match */
            maxCount: number;
            /** @description Current number of tickets in queue (if available) */
            currentTickets?: number | null;
            /** @description Average wait time in seconds (if available) */
            averageWaitSeconds?: number | null;
        };
        /** @description Request to re-affirm after validation failure */
        ReaffirmRequest: {
            /**
             * Format: uuid
             * @description Escrow ID
             */
            escrowId: string;
            /**
             * Format: uuid
             * @description Party reaffirming
             */
            partyId: string;
            /** @description Type of entity (Account, Character, etc.) */
            partyType: components["schemas"]["EntityType"];
            /** @description Release token */
            releaseToken?: string | null;
            /** @description Idempotency key */
            idempotencyKey: string;
        };
        /** @description Response from party re-affirming after validation failure */
        ReaffirmResponse: {
            /** @description Reaffirmed escrow agreement */
            escrow: components["schemas"]["EscrowAgreement"];
            /** @description Whether all parties have reaffirmed */
            allReaffirmed: boolean;
        };
        /**
         * @description Categories of historical events that realms can participate in
         * @enum {string}
         */
        RealmEventCategory: "FOUNDING" | "WAR" | "TREATY" | "CATACLYSM" | "DISCOVERY" | "MIGRATION" | "CULTURAL_SHIFT" | "ECONOMIC_CHANGE" | "POLITICAL_UPHEAVAL";
        /**
         * @description How the realm participated in the historical event
         * @enum {string}
         */
        RealmEventRole: "ORIGIN" | "AGGRESSOR" | "DEFENDER" | "MEDIATOR" | "AFFECTED" | "BENEFICIARY" | "INSTIGATOR" | "NEUTRAL_PARTY";
        /** @description Request to check if a realm exists and is available for use */
        RealmExistsRequest: {
            /**
             * Format: uuid
             * @description ID of the realm to validate
             */
            realmId: string;
        };
        /** @description Response indicating whether a realm exists and its active status */
        RealmExistsResponse: {
            /** @description Whether the realm exists */
            exists: boolean;
            /** @description Whether the realm is active (false if deprecated or not found) */
            isActive: boolean;
            /**
             * Format: uuid
             * @description The realm ID if found
             */
            realmId?: string | null;
        };
        /** @description Record of a realm's participation in a historical event */
        RealmHistoricalParticipation: {
            /**
             * Format: uuid
             * @description Unique ID for this participation record
             */
            participationId: string;
            /**
             * Format: uuid
             * @description ID of the realm that participated
             */
            realmId: string;
            /**
             * Format: uuid
             * @description ID of the historical event
             */
            eventId: string;
            /** @description Name of the event (for display and summarization) */
            eventName: string;
            /** @description Category of the historical event */
            eventCategory: components["schemas"]["RealmEventCategory"];
            /** @description How the realm participated */
            role: components["schemas"]["RealmEventRole"];
            /**
             * Format: date-time
             * @description In-game date when the event occurred
             */
            eventDate: string;
            /**
             * Format: float
             * @description How significant this event was for the realm (0.0 to 1.0).
             *     Affects behavior system weighting.
             * @default 0.5
             */
            impact: number;
            /** @description Event-specific details for behavior decisions */
            metadata?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: date-time
             * @description When this record was created
             */
            createdAt: string;
        };
        /** @description Paginated list of realms with metadata for navigation */
        RealmListResponse: {
            /** @description List of realms matching the query criteria */
            realms: components["schemas"]["RealmResponse"][];
            /** @description Total number of realms matching the query (before pagination) */
            totalCount: number;
            /** @description Current page number (1-indexed) */
            page: number;
            /** @description Number of realms per page */
            pageSize: number;
            /** @description Whether there are more realms available on the next page */
            hasNextPage?: boolean;
            /** @description Whether there are realms available on the previous page */
            hasPreviousPage?: boolean;
        };
        /** @description A machine-readable lore element for behavior system consumption */
        RealmLoreElement: {
            /** @description Category of this lore element */
            elementType: components["schemas"]["RealmLoreElementType"];
            /**
             * @description Machine-readable key (e.g., "founding_year", "primary_export", "capital_city").
             *     Used by behavior system to query specific aspects.
             */
            key: string;
            /**
             * @description Machine-readable value (e.g., "year_of_the_dragon", "iron_ore", "stormgate").
             *     Referenced in behavior rules.
             */
            value: string;
            /**
             * Format: float
             * @description How strongly this element affects behavior (0.0 to 1.0).
             *     Higher strength = greater influence on decisions.
             * @default 0.5
             */
            strength: number;
            /**
             * Format: uuid
             * @description Optional related entity (location, organization, character)
             */
            relatedEntityId?: string | null;
            /** @description Type of the related entity (if any) */
            relatedEntityType?: string | null;
        };
        /**
         * @description Types of lore elements. Each type represents a different aspect
         *     of the realm's background that influences behavior.
         * @enum {string}
         */
        RealmLoreElementType: "ORIGIN_MYTH" | "CULTURAL_PRACTICE" | "POLITICAL_SYSTEM" | "ECONOMIC_BASE" | "RELIGIOUS_TRADITION" | "GEOGRAPHIC_FEATURE" | "FAMOUS_FIGURE" | "TECHNOLOGICAL_LEVEL";
        /** @description Complete lore data for a realm */
        RealmLoreResponse: {
            /**
             * Format: uuid
             * @description ID of the realm this lore belongs to
             */
            realmId: string;
            /** @description All lore elements for this realm */
            elements: components["schemas"]["RealmLoreElement"][];
            /**
             * Format: date-time
             * @description When this lore was first created
             */
            createdAt?: string | null;
            /**
             * Format: date-time
             * @description When this lore was last modified
             */
            updatedAt?: string | null;
        };
        /** @description Paginated list of participation records */
        RealmParticipationListResponse: {
            /** @description List of participation records */
            participations: components["schemas"]["RealmHistoricalParticipation"][];
            /** @description Total number of matching records */
            totalCount: number;
            /** @description Current page number (1-based) */
            page: number;
            /** @description Number of results per page */
            pageSize: number;
            /** @description Whether there are more results after this page */
            hasNextPage?: boolean;
            /** @description Whether there are results before this page */
            hasPreviousPage?: boolean;
        };
        /** @description Complete realm information returned from API operations */
        RealmResponse: {
            /**
             * Format: uuid
             * @description Unique identifier of the realm
             */
            realmId: string;
            /** @description Unique code for the realm (e.g., "REALM_1", "REALM_2") */
            code: string;
            /** @description Display name for the realm */
            name: string;
            /**
             * Format: uuid
             * @description ID of the game service this realm belongs to
             */
            gameServiceId: string;
            /** @description Detailed description of the realm */
            description?: string | null;
            /** @description Category for grouping realms */
            category?: string | null;
            /** @description Whether the realm is currently active for gameplay */
            isActive: boolean;
            /** @description Whether this realm is deprecated and cannot be used for new entities */
            isDeprecated: boolean;
            /**
             * Format: date-time
             * @description Timestamp when this realm was deprecated
             */
            deprecatedAt?: string | null;
            /** @description Optional reason for deprecation */
            deprecationReason?: string | null;
            /** @description Additional custom metadata for the realm */
            metadata?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: date-time
             * @description Timestamp when the realm was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Timestamp when the realm was last updated
             */
            updatedAt: string;
        };
        /** @description Status and population information for a single game realm */
        RealmStatus: {
            /**
             * Format: uuid
             * @description Unique identifier for the game realm
             */
            realmId: string;
            /** @description Display name of the game realm */
            name: string;
            /**
             * @description Current operational status of the realm
             * @enum {string}
             */
            status: "online" | "offline" | "maintenance" | "full";
            /**
             * @description Current player population level
             * @enum {string}
             */
            population: "low" | "medium" | "high" | "full";
            /** @description Current number of players online */
            playerCount?: number | null;
            /** @description Latency in milliseconds */
            ping?: number | null;
        };
        /** @description Information about a reference */
        ReferenceInfo: {
            /**
             * Format: uuid
             * @description Scene containing the reference
             */
            sceneId: string;
            /** @description Name of the referencing scene */
            sceneName: string;
            /**
             * Format: uuid
             * @description Node containing the reference
             */
            nodeId: string;
            /** @description refId of the referencing node */
            nodeRefId: string;
            /** @description Name of the referencing node */
            nodeName?: string;
        };
        /** @description Request to obtain a new access token using a valid refresh token */
        RefreshRequest: {
            /** @description Refresh token issued during authentication to obtain a new access token */
            refreshToken: string;
        };
        /** @description Request to trigger escrow refund to depositors */
        RefundRequest: {
            /**
             * Format: uuid
             * @description Escrow ID
             */
            escrowId: string;
            /** @description For initiator_trusted mode */
            initiatorServiceId?: string | null;
            /** @description Reason for refund */
            reason?: string | null;
            /** @description Idempotency key */
            idempotencyKey: string;
        };
        /** @description Response from refunding escrow assets to depositors */
        RefundResponse: {
            /** @description Refunded escrow agreement */
            escrow: components["schemas"]["EscrowAgreement"];
            /** @description Refund results per depositor */
            refunds: components["schemas"]["RefundResult"][];
        };
        /** @description Result of refunding assets to a single depositor */
        RefundResult: {
            /**
             * Format: uuid
             * @description Depositor party ID
             */
            depositorPartyId: string;
            /** @description Assets refunded (null if failed) */
            assets?: components["schemas"]["EscrowAssetBundle"];
            /** @description Whether refund succeeded */
            success: boolean;
            /** @description Error message if failed */
            error?: string | null;
        };
        /** @description Request to register a new user account */
        RegisterRequest: {
            /**
             * @description Unique username for the account
             * @example gameuser123
             */
            username: string;
            /**
             * Format: password
             * @description Password for the account (will be hashed)
             * @example SecurePassword123!
             */
            password: string;
            /**
             * Format: email
             * @description Email address for account recovery and notifications
             * @example user@example.com
             */
            email: string;
        };
        /** @description Response from successful user registration */
        RegisterResponse: {
            /**
             * Format: uuid
             * @description Unique identifier for the newly created account
             */
            accountId: string;
            /**
             * @description JWT access token for immediate authentication
             * @example eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJnYW1ldXNlcjEyMyIsImlhdCI6MTY0MDk5NTIwMCwiZXhwIjoxNjQwOTk4ODAwfQ.signature
             */
            accessToken: string;
            /**
             * @description Refresh token for obtaining new access tokens
             * @example refresh_token_abc123xyz789
             */
            refreshToken?: string | null;
            /**
             * Format: uri
             * @description WebSocket endpoint for Connect service
             */
            connectUrl: string;
        };
        /** @description Request to register a new save data schema with optional migration rules */
        RegisterSchemaRequest: {
            /** @description Schema namespace (e.g., game identifier) */
            namespace: string;
            /** @description Schema version identifier */
            schemaVersion: string;
            /** @description JSON Schema definition for validation */
            schema: Record<string, never>;
            /** @description Previous version this migrates from */
            previousVersion?: string | null;
            /**
             * @description JSON Patch (RFC 6902) operations to migrate from previousVersion.
             *     Uses JsonPatch.Net library (MIT licensed).
             */
            migrationPatch?: components["schemas"]["JsonPatchOperation"][] | null;
        };
        /**
         * @description How deep to traverse related document links:
         *     - none: No related documents included
         *     - direct: Only directly linked documents (depth 1)
         *     - extended: Related documents + their related documents (depth 2)
         * @default direct
         * @enum {string}
         */
        RelatedDepth: "none" | "direct" | "extended";
        /** @description Paginated list of relationships with metadata for navigation */
        RelationshipListResponse: {
            /** @description List of relationships matching the query */
            relationships: components["schemas"]["RelationshipResponse"][];
            /** @description Total number of relationships matching the query */
            totalCount: number;
            /** @description Current page number (1-based) */
            page: number;
            /** @description Number of results per page */
            pageSize: number;
            /** @description Whether there are more results on the next page */
            hasNextPage?: boolean;
            /** @description Whether there are results on the previous page */
            hasPreviousPage?: boolean;
        };
        /** @description Complete details of a relationship between two entities */
        RelationshipResponse: {
            /**
             * Format: uuid
             * @description Unique identifier of the relationship
             */
            relationshipId: string;
            /**
             * Format: uuid
             * @description ID of the first entity in the relationship
             */
            entity1Id: string;
            /** @description Type of the first entity in the relationship */
            entity1Type: components["schemas"]["EntityType"];
            /**
             * Format: uuid
             * @description ID of the second entity in the relationship
             */
            entity2Id: string;
            /** @description Type of the second entity in the relationship */
            entity2Type: components["schemas"]["EntityType"];
            /**
             * Format: uuid
             * @description Relationship type ID (from RelationshipType service)
             */
            relationshipTypeId: string;
            /**
             * Format: date-time
             * @description In-game timestamp when relationship started
             */
            startedAt: string;
            /**
             * Format: date-time
             * @description In-game timestamp when relationship ended, null if still active
             */
            endedAt?: string | null;
            /** @description Type-specific relationship data */
            metadata?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: date-time
             * @description System timestamp when the relationship record was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description System timestamp when the relationship record was last updated
             */
            updatedAt?: string | null;
        };
        /** @description Response containing a list of relationship types with total count */
        RelationshipTypeListResponse: {
            /** @description List of relationship types matching the query */
            types: components["schemas"]["RelationshipTypeResponse"][];
            /** @description Total number of relationship types returned */
            totalCount: number;
        };
        /** @description Complete representation of a relationship type including hierarchy, inverse, and deprecation information */
        RelationshipTypeResponse: {
            /**
             * Format: uuid
             * @description Unique identifier of the relationship type
             */
            relationshipTypeId: string;
            /** @description Unique code for the relationship type (e.g., "SON", "MOTHER") */
            code: string;
            /** @description Human-readable display name for the relationship type */
            name: string;
            /** @description Detailed description of the relationship type */
            description?: string | null;
            /** @description Category for grouping relationship types (e.g., "FAMILY", "SOCIAL") */
            category?: string | null;
            /**
             * Format: uuid
             * @description ID of the parent type in the hierarchy (null for root types)
             */
            parentTypeId?: string | null;
            /** @description Code of the parent type (for convenience) */
            parentTypeCode?: string | null;
            /**
             * Format: uuid
             * @description ID of the inverse relationship type (e.g., PARENT is inverse of CHILD)
             */
            inverseTypeId?: string | null;
            /** @description Code of the inverse relationship type (for convenience) */
            inverseTypeCode?: string | null;
            /** @description Whether the relationship is the same in both directions (e.g., SIBLING) */
            isBidirectional: boolean;
            /** @description Whether this type is deprecated and cannot be used for new relationships */
            isDeprecated: boolean;
            /**
             * Format: date-time
             * @description Timestamp when this type was deprecated
             */
            deprecatedAt?: string | null;
            /** @description Optional reason for deprecation */
            deprecationReason?: string | null;
            /** @description Depth in the hierarchy (0 for root types) */
            depth: number;
            /** @description Additional custom metadata for the relationship type */
            metadata?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: date-time
             * @description Timestamp when the relationship type was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Timestamp when the relationship type was last updated
             */
            updatedAt: string;
        };
        /** @description Defines who gets what on release */
        ReleaseAllocation: {
            /**
             * Format: uuid
             * @description Party receiving assets
             */
            recipientPartyId: string;
            /** @description Type of the recipient party */
            recipientPartyType: components["schemas"]["EntityType"];
            /** @description Assets this recipient should receive */
            assets: components["schemas"]["EscrowAsset"][];
            /**
             * Format: uuid
             * @description Where to deliver currency
             */
            destinationWalletId?: string | null;
            /**
             * Format: uuid
             * @description Where to deliver items
             */
            destinationContainerId?: string | null;
        };
        /** @description Input for specifying how assets should be allocated on release */
        ReleaseAllocationInput: {
            /**
             * Format: uuid
             * @description Recipient party ID
             */
            recipientPartyId: string;
            /** @description Recipient party type */
            recipientPartyType: components["schemas"]["EntityType"];
            /** @description Assets to allocate */
            assets: components["schemas"]["EscrowAssetInput"][];
            /**
             * Format: uuid
             * @description Destination wallet
             */
            destinationWalletId?: string | null;
            /**
             * Format: uuid
             * @description Destination container
             */
            destinationContainerId?: string | null;
        };
        /** @description Request to release a hold */
        ReleaseHoldRequest: {
            /**
             * Format: uuid
             * @description Hold ID to release
             */
            holdId: string;
        };
        /** @description Request to trigger escrow release to recipients */
        ReleaseRequest: {
            /**
             * Format: uuid
             * @description Escrow ID
             */
            escrowId: string;
            /** @description For initiator_trusted mode */
            initiatorServiceId?: string | null;
            /** @description Optional notes */
            notes?: string | null;
            /** @description Idempotency key */
            idempotencyKey: string;
        };
        /** @description Response from releasing escrow assets to recipients */
        ReleaseResponse: {
            /** @description Released escrow agreement */
            escrow: components["schemas"]["EscrowAgreement"];
            /** @description Results of contract finalizer APIs */
            finalizerResults: components["schemas"]["FinalizerResult"][];
            /** @description Release results per recipient */
            releases: components["schemas"]["ReleaseResult"][];
        };
        /** @description Result of releasing assets to a single recipient */
        ReleaseResult: {
            /**
             * Format: uuid
             * @description Recipient party ID
             */
            recipientPartyId: string;
            /** @description Assets released (null if failed) */
            assets?: components["schemas"]["EscrowAssetBundle"];
            /** @description Whether release succeeded */
            success: boolean;
            /** @description Error message if failed */
            error?: string | null;
        };
        /** @description Request to remove item from container */
        RemoveItemRequest: {
            /**
             * Format: uuid
             * @description Item instance ID to remove
             */
            instanceId: string;
        };
        /** @description Response after removing item */
        RemoveItemResponse: {
            /** @description Whether remove succeeded */
            success: boolean;
            /**
             * Format: uuid
             * @description Removed item ID
             */
            instanceId: string;
            /**
             * Format: uuid
             * @description Container removed from
             */
            previousContainerId: string;
        };
        /** @description Request to rename an existing save slot */
        RenameSlotRequest: {
            /** @description Game identifier */
            gameId: string;
            /**
             * Format: uuid
             * @description Entity ID that owns the slot
             */
            ownerId: string;
            /** @description Type of entity that owns this save slot */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Current slot name */
            slotName: string;
            /** @description New slot name */
            newSlotName: string;
        };
        /** @description Request to report a breach */
        ReportBreachRequest: {
            /**
             * Format: uuid
             * @description Contract that was breached
             */
            contractId: string;
            /**
             * Format: uuid
             * @description Entity that breached
             */
            breachingEntityId: string;
            /** @description Type of breaching entity */
            breachingEntityType: components["schemas"]["EntityType"];
            /** @description Type of breach */
            breachType: components["schemas"]["BreachType"];
            /** @description Code of breached term or milestone */
            breachedTermOrMilestone?: string | null;
            /** @description Breach description */
            description?: string | null;
        };
        /** @description Detailed repository binding configuration and status */
        RepositoryBindingInfo: {
            /**
             * Format: uuid
             * @description Unique identifier of the repository binding
             */
            bindingId: string;
            /** @description Namespace the repository is bound to */
            namespace: string;
            /** @description URL of the bound repository */
            repositoryUrl: string;
            /** @description Branch being synced */
            branch?: string;
            /** @description Current status of the binding */
            status: components["schemas"]["BindingStatus"];
            /** @description Whether automatic sync is enabled */
            syncEnabled?: boolean;
            /** @description Sync interval in minutes */
            syncIntervalMinutes?: number;
            /** @description Number of documents from this repository */
            documentCount?: number;
            /**
             * Format: date-time
             * @description Timestamp when the binding was created
             */
            createdAt?: string;
            /**
             * @description Owner of this binding. NOT a session ID.
             *     Contains either an accountId (UUID format) for user-initiated bindings
             *     or a service name for service-initiated bindings.
             */
            owner?: string;
        };
        /** @description Request to get current repository binding and sync status */
        RepositoryStatusRequest: {
            /** @description Documentation namespace to get status for */
            namespace: string;
        };
        /** @description Response containing binding configuration and recent sync information */
        RepositoryStatusResponse: {
            /** @description Current binding configuration and status */
            binding?: components["schemas"]["RepositoryBindingInfo"];
            /** @description Information about the most recent sync */
            lastSync?: components["schemas"]["SyncInfo"];
        };
        /** @description Request for full snapshot */
        RequestSnapshotRequest: {
            /**
             * Format: uuid
             * @description Region to snapshot
             */
            regionId: string;
            /** @description Which kinds to include (default all) */
            kinds?: components["schemas"]["MapKind"][] | null;
            /** @description Optional bounds filter */
            bounds?: components["schemas"]["Bounds"];
            /**
             * @description Optional authority token. If provided and valid, clears the
             *     RequiresConsumeBeforePublish flag for require_consume takeover mode.
             */
            authorityToken?: string | null;
        };
        /** @description Snapshot response */
        RequestSnapshotResponse: {
            /**
             * Format: uuid
             * @description Region ID
             */
            regionId?: string;
            /** @description All objects in snapshot */
            objects?: components["schemas"]["MapObject"][];
            /** @description For large snapshots, lib-asset reference */
            payloadRef?: string | null;
            /**
             * Format: int64
             * @description Snapshot version
             */
            version?: number;
        };
        /** @description Reservation token returned when creating a matchmade session */
        ReservationInfo: {
            /**
             * Format: uuid
             * @description Account ID this reservation is for
             */
            accountId: string;
            /** @description Token to claim this reservation */
            token: string;
            /**
             * Format: date-time
             * @description When this reservation expires
             */
            expiresAt: string;
        };
        /** @description Request to resolve optimal bundle downloads for requested assets */
        ResolveBundlesRequest: {
            /** @description Platform asset IDs to resolve */
            assetIds: string[];
            /** @description Game realm to search within */
            realm: components["schemas"]["GameRealm"];
            /**
             * @description Prefer metabundles when coverage is equal
             * @default true
             */
            preferMetabundles: boolean;
            /**
             * @description Include standalone assets not in any bundle
             * @default true
             */
            includeStandalone: boolean;
            /** @description Maximum number of bundles to return (optimization limit) */
            maxBundles?: number | null;
        };
        /** @description Optimal bundle set for requested assets */
        ResolveBundlesResponse: {
            /** @description Bundles to download */
            bundles: components["schemas"]["ResolvedBundle"][];
            /** @description Individual assets to download */
            standaloneAssets: components["schemas"]["ResolvedAsset"][];
            /** @description Coverage statistics */
            coverage: components["schemas"]["CoverageAnalysis"];
            /** @description Asset IDs that couldn't be found (null if all resolved) */
            unresolved?: string[] | null;
        };
        /** @description Request for arbiter to resolve a disputed escrow */
        ResolveRequest: {
            /**
             * Format: uuid
             * @description Escrow ID
             */
            escrowId: string;
            /**
             * Format: uuid
             * @description Arbiter ID
             */
            arbiterId: string;
            /** @description Arbiter type */
            arbiterType: components["schemas"]["EntityType"];
            /** @description Resolution decision for the dispute */
            resolution: components["schemas"]["EscrowResolution"];
            /** @description For split resolution */
            splitAllocations?: components["schemas"]["SplitAllocation"][] | null;
            /** @description Resolution notes */
            notes?: string | null;
            /** @description Idempotency key */
            idempotencyKey: string;
        };
        /** @description Response from arbiter resolving a disputed escrow */
        ResolveResponse: {
            /** @description Resolved escrow agreement */
            escrow: components["schemas"]["EscrowAgreement"];
            /** @description Transfer results */
            transfers: components["schemas"]["TransferResult"][];
        };
        /** @description A standalone asset selected for download */
        ResolvedAsset: {
            /** @description Asset identifier */
            assetId: string;
            /**
             * Format: uri
             * @description Pre-signed download URL
             */
            downloadUrl: string;
            /**
             * Format: date-time
             * @description When the download URL expires
             */
            expiresAt: string;
            /**
             * Format: int64
             * @description Asset file size in bytes
             */
            size: number;
            /** @description SHA256 hash of asset content */
            contentHash?: string | null;
        };
        /** @description A bundle selected for download in resolution */
        ResolvedBundle: {
            /** @description Human-readable bundle identifier */
            bundleId: string;
            /** @description Whether source or metabundle */
            bundleType: components["schemas"]["BundleType"];
            /** @description Bundle version */
            version?: string | null;
            /**
             * Format: uri
             * @description Pre-signed download URL
             */
            downloadUrl: string;
            /**
             * Format: date-time
             * @description When the download URL expires
             */
            expiresAt: string;
            /**
             * Format: int64
             * @description Bundle file size in bytes
             */
            size: number;
            /** @description Which of the requested assets this bundle provides */
            assetsProvided: string[];
        };
        /** @description A successfully resolved scene reference */
        ResolvedReference: {
            /**
             * Format: uuid
             * @description Node ID containing the reference
             */
            nodeId: string;
            /** @description refId of the referencing node */
            refId: string;
            /**
             * Format: uuid
             * @description ID of the referenced scene
             */
            referencedSceneId: string;
            /** @description Version that was resolved */
            referencedVersion?: string | null;
            /** @description The resolved scene content */
            scene: components["schemas"]["Scene"];
            /** @description Depth level of this reference */
            depth?: number;
        };
        /**
         * @description Validation rules for API responses with three-outcome model.
         *     Used by lib-contract to validate clause conditions without
         *     understanding the specific API semantics.
         */
        ResponseValidation: {
            /**
             * @description Conditions that must ALL pass for success.
             *     If any fail, checks permanent failure conditions.
             */
            successConditions?: components["schemas"]["ValidationCondition"][];
            /**
             * @description Conditions that indicate permanent failure (clause violated).
             *     Checked when success conditions fail.
             */
            permanentFailureConditions?: components["schemas"]["ValidationCondition"][];
            /**
             * @description HTTP status codes that indicate transient failure (retry later).
             *     Default: [408, 429, 502, 503, 504]
             */
            transientFailureStatusCodes?: number[];
        };
        /** @description Request to restore a soft-deleted bundle */
        RestoreBundleRequest: {
            /** @description Human-readable bundle identifier to restore */
            bundleId: string;
            /** @description Optional reason for restoration (recorded in version history) */
            reason?: string | null;
        };
        /** @description Result of bundle restoration */
        RestoreBundleResponse: {
            /** @description Human-readable bundle identifier that was restored */
            bundleId: string;
            /** @description Current bundle status (should be "active") */
            status: string;
            /**
             * Format: date-time
             * @description When the bundle was restored
             */
            restoredAt: string;
            /** @description Version number the bundle was restored from */
            restoredFromVersion: number;
        };
        /** @description Search engine optimization and social media sharing metadata */
        SEOMetadata: {
            /** @description Meta description for search engines */
            description?: string | null;
            /** @description Keywords for search engine indexing */
            keywords?: string[];
            /** @description Open Graph title for social media sharing */
            ogTitle?: string | null;
            /** @description Open Graph description for social media sharing */
            ogDescription?: string | null;
            /**
             * Format: uri
             * @description Open Graph image URL for social media sharing
             */
            ogImage?: string | null;
        };
        /**
         * @description Category of save with predefined behaviors.
         *     QUICK_SAVE: Single-slot fast save, overwritten frequently (max 1 version).
         *     AUTO_SAVE: System-triggered periodic saves (max 5 versions, rolling).
         *     MANUAL_SAVE: User-initiated named saves (max 10 versions, no auto-cleanup).
         *     CHECKPOINT: Progress markers (max 20 versions, rolling).
         *     STATE_SNAPSHOT: Full state captures for debugging (max 3 versions, rolling).
         * @enum {string}
         */
        SaveCategory: "QUICK_SAVE" | "AUTO_SAVE" | "MANUAL_SAVE" | "CHECKPOINT" | "STATE_SNAPSHOT";
        /** @description Request to save incremental changes as a delta from a base version */
        SaveDeltaRequest: {
            /** @description Game identifier for namespace isolation */
            gameId: string;
            /**
             * Format: uuid
             * @description ID of the owning entity
             */
            ownerId: string;
            /** @description Type of entity that owns this save slot */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Name of the slot to save the delta to */
            slotName: string;
            /** @description Version number this delta is based on */
            baseVersion: number;
            /**
             * Format: byte
             * @description Base64-encoded delta/patch data.
             *     For JSON_PATCH: Array of RFC 6902 operations
             *     For BSDIFF/XDELTA: Binary patch data
             */
            delta: string;
            /** @description Delta computation algorithm to use (defaults to JSON_PATCH) */
            algorithm?: components["schemas"]["DeltaAlgorithm"] | null;
            /** @description Schema version of this save for migration tracking */
            schemaVersion?: string | null;
            /** @description Human-readable name for this delta save */
            displayName?: string | null;
            /** @description Device identifier for cross-device sync conflict detection */
            deviceId?: string | null;
            /** @description Custom key-value metadata for this delta version */
            metadata?: {
                [key: string]: string;
            } | null;
        };
        /** @description Result of delta save operation with size and chain information */
        SaveDeltaResponse: {
            /**
             * Format: uuid
             * @description Unique identifier of the save slot
             */
            slotId: string;
            /** @description New version number */
            versionNumber: number;
            /** @description Base version this delta is relative to */
            baseVersion: number;
            /**
             * Format: int64
             * @description Size of stored delta
             */
            deltaSizeBytes: number;
            /**
             * Format: int64
             * @description Estimated size when reconstructed
             */
            estimatedFullSizeBytes: number;
            /** @description Number of deltas in chain to base snapshot */
            chainLength?: number;
            /**
             * Format: double
             * @description Storage savings vs full snapshot (0-1)
             */
            compressionSavings?: number;
            /**
             * Format: date-time
             * @description When the delta version was created
             */
            createdAt: string;
        };
        /** @description Request to save game state data to a slot with optional compression and metadata */
        SaveRequest: {
            /** @description Game identifier for namespace isolation */
            gameId: string;
            /**
             * Format: uuid
             * @description ID of the owning entity
             */
            ownerId: string;
            /** @description Type of entity that owns this save slot */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Slot name (auto-created if doesn't exist) */
            slotName: string;
            /** @description Category for auto-created slots (defaults to MANUAL_SAVE) */
            category?: components["schemas"]["SaveCategory"] | null;
            /**
             * Format: byte
             * @description Base64-encoded save data
             */
            data: string;
            /** @description Schema version identifier for migration tracking */
            schemaVersion?: string | null;
            /** @description Human-readable name for this save */
            displayName?: string | null;
            /**
             * Format: byte
             * @description Optional preview image (JPEG/WebP). Max size configurable
             *     (default 256KB). Used for save slot previews in game UI.
             */
            thumbnail?: string | null;
            /**
             * @description Optional device identifier for cloud save conflict detection.
             *     When provided, saves are prefixed/tagged with device info,
             *     enabling opt-in cross-device sync with collision awareness.
             */
            deviceId?: string | null;
            /** @description Custom metadata (e.g., level, playtime, location) */
            metadata?: {
                [key: string]: string;
            } | null;
            /** @description If provided, pin this version with checkpoint name */
            pinAsCheckpoint?: string | null;
        };
        /** @description Result of a save operation including version info and conflict detection */
        SaveResponse: {
            /**
             * Format: uuid
             * @description Slot identifier
             */
            slotId: string;
            /** @description Assigned version number */
            versionNumber: number;
            /** @description SHA-256 hash of save data */
            contentHash: string;
            /**
             * Format: int64
             * @description Size of save data in bytes
             */
            sizeBytes: number;
            /**
             * Format: int64
             * @description Compressed size (if compression applied)
             */
            compressedSizeBytes?: number;
            /**
             * Format: double
             * @description Compression ratio (0-1)
             */
            compressionRatio?: number;
            /** @description Whether version was pinned */
            pinned?: boolean;
            /** @description Checkpoint name if pinned */
            checkpointName?: string | null;
            /**
             * Format: uri
             * @description Pre-signed URL to retrieve thumbnail (if provided)
             */
            thumbnailUrl?: string | null;
            /**
             * @description True if this save overwrote a version from a different device.
             *     Only relevant when deviceId is used for cloud sync.
             */
            conflictDetected?: boolean;
            /** @description Device ID of the overwritten version (if conflict) */
            conflictingDeviceId?: string | null;
            /** @description Version number that was overwritten (if conflict) */
            conflictingVersion?: number | null;
            /**
             * Format: date-time
             * @description Save timestamp
             */
            createdAt: string;
            /** @description Number of old versions cleaned up by rolling policy */
            versionsCleanedUp?: number;
            /**
             * @description True if async upload is enabled and data is queued for MinIO upload.
             *     Save is immediately loadable from Redis cache, but not yet durable.
             */
            uploadPending?: boolean;
        };
        /** @description A complete scene document with hierarchical node structure */
        Scene: {
            /**
             * @description Schema identifier for validation
             * @default bannou://schemas/scene/v1
             */
            schema: string;
            /**
             * Format: uuid
             * @description Unique scene identifier
             */
            sceneId: string;
            /**
             * @description Game service identifier for partitioning. Treated as opaque string.
             *     Default is the nil UUID for unpartitioned scenes.
             * @default 00000000-0000-0000-0000-000000000000
             */
            gameId: string;
            /** @description Scene classification for querying and validation */
            sceneType: components["schemas"]["SceneType"];
            /** @description Human-readable scene name */
            name: string;
            /** @description Optional scene description */
            description?: string | null;
            /** @description Semantic version (MAJOR.MINOR.PATCH) */
            version: string;
            /** @description Root node of the scene hierarchy */
            root: components["schemas"]["SceneNode"];
            /** @description Searchable tags for filtering scenes */
            tags?: string[];
            /**
             * @description Scene-level metadata. Not interpreted by Scene service.
             *     Examples: author, thumbnail, editor preferences, generator config.
             */
            metadata?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: date-time
             * @description When the scene was first created
             */
            createdAt?: string;
            /**
             * Format: date-time
             * @description When the scene was last modified
             */
            updatedAt?: string;
        };
        /**
         * @description A node in the scene hierarchy. Nodes can contain children to form
         *     a tree structure. Each node has a local transform relative to its parent.
         */
        SceneNode: {
            /**
             * Format: uuid
             * @description Globally unique node identifier
             */
            nodeId: string;
            /**
             * @description Scene-local reference identifier. Must be unique within the scene.
             *     Used for scripting and cross-referencing. Examples: main_door, npc_spawn_1
             */
            refId: string;
            /**
             * Format: uuid
             * @description Parent node ID. Null for the root node only.
             */
            parentNodeId?: string | null;
            /** @description Human-readable display name for the node */
            name: string;
            /** @description The structural type of this node */
            nodeType: components["schemas"]["NodeType"];
            /** @description Transform relative to parent node */
            localTransform: components["schemas"]["Transform"];
            /** @description Optional asset binding (mesh, sound, particle effect) */
            asset?: components["schemas"]["AssetReference"];
            /** @description Child nodes in the hierarchy */
            children?: components["schemas"]["SceneNode"][];
            /**
             * @description Whether this node is active in the scene definition
             * @default true
             */
            enabled: boolean;
            /**
             * @description Ordering among siblings for deterministic iteration
             * @default 0
             */
            sortOrder: number;
            /** @description Arbitrary tags for consumer filtering (e.g., entrance, spawn, interactive) */
            tags?: string[];
            /**
             * @description Consumer-specific data stored without interpretation.
             *     Use namespaced keys (e.g., render.castShadows, game.interactionType).
             */
            annotations?: {
                [key: string]: unknown;
            } | null;
            /**
             * @description Predefined locations for attaching child objects.
             *     Used by Scene Composer for furniture decoration, wall accessories, etc.
             */
            attachmentPoints?: components["schemas"]["AttachmentPoint"][];
            /**
             * @description Interaction capabilities of this node.
             *     Used by AI navigation and character controllers.
             */
            affordances?: components["schemas"]["Affordance"][];
            /**
             * @description Procedural asset swapping configuration.
             *     Defines which assets can substitute for this node's asset.
             */
            assetSlot?: components["schemas"]["AssetSlot"];
            /**
             * @description Type of marker for marker nodes.
             *     Only relevant when nodeType is 'marker'.
             */
            markerType?: components["schemas"]["MarkerType"];
            /**
             * @description Shape of volume for volume nodes.
             *     Only relevant when nodeType is 'volume'.
             */
            volumeShape?: components["schemas"]["VolumeShape"];
            /**
             * @description Size/extents of the volume (interpretation depends on volumeShape).
             *     For box: full dimensions. For sphere: x=radius. For capsule: x=radius, y=height.
             */
            volumeSize?: components["schemas"]["Vector3"];
            /**
             * Format: uuid
             * @description Scene ID to embed for reference nodes.
             *     Only relevant when nodeType is 'reference'.
             */
            referenceSceneId?: string | null;
        };
        /** @description Standard response containing a scene */
        SceneResponse: {
            /** @description The scene document */
            scene: components["schemas"]["Scene"];
        };
        /** @description Summary of a scene for list results (excludes full node tree) */
        SceneSummary: {
            /**
             * Format: uuid
             * @description Unique scene identifier
             */
            sceneId: string;
            /** @description Game service identifier */
            gameId: string;
            /** @description Scene classification */
            sceneType: components["schemas"]["SceneType"];
            /** @description Scene name */
            name: string;
            /** @description Scene description */
            description?: string | null;
            /** @description Current version */
            version: string;
            /** @description Scene tags */
            tags?: string[];
            /** @description Total number of nodes in scene */
            nodeCount?: number;
            /**
             * Format: date-time
             * @description Creation timestamp
             */
            createdAt?: string;
            /**
             * Format: date-time
             * @description Last update timestamp
             */
            updatedAt?: string;
            /** @description Whether scene is currently checked out */
            isCheckedOut?: boolean;
        };
        /**
         * @description Scene classification for querying and validation rule lookup.
         *     Different types may have different validation requirements per game.
         * @enum {string}
         */
        SceneType: "unknown" | "region" | "city" | "district" | "lot" | "building" | "room" | "dungeon" | "arena" | "vehicle" | "prefab" | "cutscene" | "other";
        /** @description Registered schema definition with version lineage information */
        SchemaResponse: {
            /** @description Schema namespace */
            namespace: string;
            /** @description Schema version */
            schemaVersion: string;
            /** @description JSON Schema definition */
            schema?: Record<string, never>;
            /** @description Previous version */
            previousVersion?: string | null;
            /** @description Whether migration script is registered */
            hasMigration?: boolean;
            /**
             * Format: date-time
             * @description Registration timestamp
             */
            createdAt: string;
        };
        /** @description Request to search documentation using keyword matching */
        SearchDocumentationRequest: {
            /** @description Documentation namespace to search within */
            namespace: string;
            /** @description Keyword or phrase to search for */
            searchTerm: string;
            /**
             * Format: uuid
             * @description Optional session ID for tracking searches (null if not tracking)
             */
            sessionId?: string | null;
            /** @description Filter results to a specific category (null for all categories) */
            category?: components["schemas"]["DocumentCategory"];
            /**
             * @description Maximum number of results to return
             * @default 10
             */
            maxResults: number;
            /** @description Fields to search within (null for default fields) */
            searchIn?: components["schemas"]["SearchField"][] | null;
            /**
             * @description How to sort the search results
             * @default relevance
             * @enum {string}
             */
            sortBy: "relevance" | "recency" | "alphabetical";
            /**
             * @description Whether to include full document content in results
             * @default false
             */
            includeContent: boolean;
        };
        /** @description Response containing keyword search results */
        SearchDocumentationResponse: {
            /** @description The namespace that was searched */
            namespace: string;
            /** @description List of matching documents */
            results: components["schemas"]["DocumentResult"][];
            /** @description Total number of matching documents */
            totalResults?: number;
            /** @description The original search term */
            searchTerm?: string;
        };
        /**
         * @description Fields that can be searched within documents
         * @enum {string}
         */
        SearchField: "title" | "content" | "tags" | "summary";
        /**
         * @description Where the search match was found
         * @enum {string}
         */
        SearchMatchType: "name" | "description" | "tag" | "node_name";
        /** @description A single search result */
        SearchResult: {
            /** @description Matching scene summary */
            scene: components["schemas"]["SceneSummary"];
            /** @description Where the match was found */
            matchType: components["schemas"]["SearchMatchType"];
            /** @description Context around the match */
            matchContext?: string | null;
        };
        /** @description Request for full-text search */
        SearchScenesRequest: {
            /** @description Search query text */
            query: string;
            /** @description Filter by game ID */
            gameId?: string | null;
            /** @description Filter by scene types */
            sceneTypes?: components["schemas"]["SceneType"][] | null;
            /**
             * @description Pagination offset
             * @default 0
             */
            offset: number;
            /**
             * @description Maximum results
             * @default 50
             */
            limit: number;
        };
        /** @description Search results */
        SearchScenesResponse: {
            /** @description Matching scenes */
            results: components["schemas"]["SearchResult"][];
            /** @description Total matches */
            total: number;
        };
        /** @description Season information */
        SeasonResponse: {
            /** @description ID of the leaderboard */
            leaderboardId: string;
            /** @description Season number */
            seasonNumber: number;
            /** @description Name of the season */
            seasonName?: string | null;
            /**
             * Format: date-time
             * @description When this season started
             */
            startedAt: string;
            /**
             * Format: date-time
             * @description When this season ended (null if active)
             */
            endedAt?: string | null;
            /** @description Whether this is the current season */
            isActive: boolean;
            /**
             * Format: int64
             * @description Number of entries in this season
             */
            entryCount?: number;
        };
        /** @description Response containing aggregate sentiment */
        SentimentResponse: {
            /**
             * Format: uuid
             * @description Character whose sentiment was queried
             */
            characterId: string;
            /**
             * Format: uuid
             * @description Target of the sentiment
             */
            targetCharacterId: string;
            /**
             * Format: float
             * @description Aggregate sentiment (-1.0 = hostile, +1.0 = friendly)
             */
            sentiment: number;
            /** @description Number of encounters factored in */
            encounterCount: number;
            /** @description Most common emotional impact across encounters */
            dominantEmotion?: components["schemas"]["EmotionalImpact"];
        };
        /** @description Aggregated status of all game server realms */
        ServerStatusResponse: {
            /**
             * @description Overall status across all game realms
             * @enum {string}
             */
            globalStatus: "online" | "partial" | "offline" | "maintenance";
            /** @description Status information for each game realm */
            realms: components["schemas"]["RealmStatus"][];
        };
        /** @description Information about a game service */
        ServiceInfo: {
            /**
             * Format: uuid
             * @description Unique identifier for the service
             */
            serviceId: string;
            /** @description URL-safe identifier (e.g., "my-game") */
            stubName: string;
            /** @description Human-readable name (e.g., "My Game Online") */
            displayName: string;
            /** @description Optional description */
            description?: string | null;
            /** @description Whether the service is currently active */
            isActive: boolean;
            /**
             * Format: date-time
             * @description When the service was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description When the service was last updated
             */
            updatedAt?: string | null;
        };
        /**
         * @description Game service stub name for created sessions. Use the game service's stubName property (e.g., "my-game"). Use "generic" for non-game-specific sessions.
         * @default generic
         */
        SessionGameType: string;
        /** @description Information about an active user session including device and activity details */
        SessionInfo: {
            /**
             * Format: uuid
             * @description Unique identifier for the session
             */
            sessionId: string;
            /** @description Information about the device used for this session */
            deviceInfo?: components["schemas"]["DeviceInfo"];
            /**
             * Format: date-time
             * @description Timestamp when the session was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Timestamp of the last activity in this session
             */
            lastActive: string;
            /** @description IP address from which the session was initiated */
            ipAddress?: string | null;
            /** @description Geographic location derived from the IP address */
            location?: string | null;
        };
        /**
         * @description Current status of the game session
         * @enum {string}
         */
        SessionStatus: "waiting" | "active" | "full" | "finished";
        /**
         * @description Type of game session - determines join behavior
         * @enum {string}
         */
        SessionType: "lobby" | "matchmade";
        /** @description Response containing a list of all active sessions for an account */
        SessionsResponse: {
            /** @description List of active sessions for the account */
            sessions: components["schemas"]["SessionInfo"][];
        };
        /** @description Request to set template values on a contract */
        SetTemplateValuesRequest: {
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractInstanceId: string;
            /**
             * @description Key-value pairs for template substitution.
             *     Keys should follow pattern: EscrowId, PartyA_EscrowWalletId, etc.
             */
            templateValues: {
                [key: string]: string;
            };
        };
        /** @description Response from setting template values */
        SetTemplateValuesResponse: {
            /** @description Whether values were updated */
            updated: boolean;
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractId: string;
            /** @description Number of template values set */
            valueCount?: number;
        };
        /** @description Global website configuration including branding, languages, and integrations */
        SiteSettings: {
            /** @description Display name of the website */
            siteName: string;
            /**
             * Format: uri
             * @description Base URL of the website
             */
            siteUrl: string;
            /** @description Short slogan or description of the site */
            tagline?: string | null;
            /**
             * @description Default language code for the website
             * @default en
             */
            defaultLanguage: string;
            /** @description List of supported language codes */
            supportedLanguages?: string[];
            /**
             * @description Whether the site is in maintenance mode
             * @default false
             */
            maintenanceMode: boolean;
            /** @description Message displayed during maintenance mode */
            maintenanceMessage?: string | null;
            /**
             * Format: email
             * @description Primary contact email address for the site
             */
            contactEmail?: string;
            /** @description Map of social media platform names to profile URLs */
            socialLinks?: {
                [key: string]: string;
            };
            /** @description Analytics and tracking service configuration */
            analytics?: components["schemas"]["Analytics"];
            /** @description Custom JavaScript scripts to inject into pages */
            customScripts?: components["schemas"]["CustomScripts"];
        };
        /** @description A step in the skill window expansion curve */
        SkillExpansionStep: {
            /** @description Number of intervals after which this step applies */
            intervals: number;
            /** @description Skill range (null means any skill level) */
            range?: number | null;
        };
        /** @description Complete metadata for a save slot including version statistics */
        SlotResponse: {
            /**
             * Format: uuid
             * @description Unique slot identifier
             */
            slotId: string;
            /**
             * Format: uuid
             * @description ID of the owning entity
             */
            ownerId: string;
            /** @description Type of entity that owns this save slot */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Slot name */
            slotName: string;
            /** @description Save category determining retention and cleanup behavior */
            category: components["schemas"]["SaveCategory"];
            /** @description Maximum versions to retain */
            maxVersions?: number;
            /** @description Days to retain versions (null = indefinite) */
            retentionDays?: number | null;
            /** @description Compression algorithm used for save data */
            compressionType?: components["schemas"]["CompressionType"];
            /** @description Current number of versions in slot */
            versionCount?: number;
            /** @description Latest version number (null if empty) */
            latestVersion?: number | null;
            /**
             * Format: int64
             * @description Total storage used by all versions
             */
            totalSizeBytes?: number;
            /**
             * Format: date-time
             * @description Slot creation timestamp
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Last modification timestamp
             */
            updatedAt?: string;
            /** @description Custom key-value metadata */
            metadata?: {
                [key: string]: string;
            };
        };
        /**
         * @description How scores are sorted
         * @enum {string}
         */
        SortOrder: "descending" | "ascending";
        /**
         * @description When item becomes bound to a character
         * @enum {string}
         */
        SoulboundType: "none" | "on_pickup" | "on_equip" | "on_use";
        /** @description Provenance reference to a source bundle used in metabundle creation */
        SourceBundleReference: {
            /** @description Source bundle identifier */
            bundleId: string;
            /** @description Version of source bundle at composition time */
            version: string;
            /** @description Asset IDs contributed from this source bundle */
            assetIds: string[];
            /** @description Hash of source bundle at composition time (for integrity verification) */
            contentHash: string;
        };
        /** @description Potential placement location */
        SpaceCandidate: {
            /**
             * Format: uuid
             * @description Container ID
             */
            containerId: string;
            /** @description Container type */
            containerType: string;
            /**
             * Format: double
             * @description How much can fit
             */
            canFitQuantity: number;
            /** @description Available slot */
            slotIndex?: number | null;
            /** @description Available grid X */
            slotX?: number | null;
            /** @description Available grid Y */
            slotY?: number | null;
            /**
             * Format: uuid
             * @description Stack to merge with
             */
            existingStackInstanceId?: string | null;
        };
        /**
         * @description Spatial context derived from game server's authoritative spatial state.
         *     Included in perception events to give NPC actors awareness of their environment
         *     without requiring direct map subscriptions.
         *
         *     Note: additionalProperties=true allows game-specific extensions.
         */
        SpatialContext: {
            /** @description Terrain type at character position (grass, stone, water, etc.) */
            terrainType?: string | null;
            /**
             * Format: float
             * @description Elevation at character position
             */
            elevation?: number | null;
            /** @description Objects within perception radius */
            nearbyObjects?: components["schemas"]["NearbyObject"][] | null;
            /** @description Active hazards within detection range */
            hazardsInRange?: components["schemas"]["HazardInfo"][] | null;
            /** @description Directions the character can move (for navigation awareness) */
            pathableDirections?: string[] | null;
            /** @description Whether cover is available within close range */
            coverNearby?: boolean | null;
            /** @description Whether character is currently indoors/under roof */
            indoors?: boolean | null;
        } & {
            [key: string]: unknown;
        };
        /** @description Request to spawn a new actor from a template */
        SpawnActorRequest: {
            /**
             * Format: uuid
             * @description Template to instantiate from
             */
            templateId: string;
            /** @description Optional custom actor ID (auto-generated if not provided) */
            actorId?: string | null;
            /** @description Override template defaults */
            configurationOverrides?: {
                [key: string]: unknown;
            } | null;
            /** @description Initial state passed to behavior */
            initialState?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: uuid
             * @description Optional character ID for NPC brain actors
             */
            characterId?: string | null;
        };
        /** @description Paginated list of species with total count for pagination */
        SpeciesListResponse: {
            /** @description List of species matching the query */
            species: components["schemas"]["SpeciesResponse"][];
            /** @description Total number of species matching the query (for pagination) */
            totalCount: number;
            /** @description Current page number */
            page?: number;
            /** @description Number of items per page */
            pageSize?: number;
        };
        /** @description Complete species data including all attributes and realm associations */
        SpeciesResponse: {
            /**
             * Format: uuid
             * @description Unique identifier of the species
             */
            speciesId: string;
            /** @description Unique code for the species (e.g., "HUMAN", "ELF") */
            code: string;
            /** @description Display name for the species */
            name: string;
            /** @description Description of the species */
            description?: string | null;
            /** @description Category for grouping (e.g., "HUMANOID", "BEAST", "MAGICAL") */
            category?: string | null;
            /** @description Whether players can create characters of this species */
            isPlayable: boolean;
            /** @description Whether this species is deprecated and cannot be used for new characters */
            isDeprecated: boolean;
            /**
             * Format: date-time
             * @description Timestamp when this species was deprecated
             */
            deprecatedAt?: string | null;
            /** @description Optional reason for deprecation */
            deprecationReason?: string | null;
            /** @description Base lifespan in game years */
            baseLifespan?: number | null;
            /** @description Age at which the species reaches maturity */
            maturityAge?: number | null;
            /** @description Base trait modifiers for this species */
            traitModifiers?: {
                [key: string]: unknown;
            } | null;
            /** @description Realms where this species is available */
            realmIds?: string[];
            /** @description Additional metadata for the species */
            metadata?: {
                [key: string]: unknown;
            } | null;
            /**
             * Format: date-time
             * @description Timestamp when the species was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Timestamp when the species was last updated
             */
            updatedAt: string;
        };
        /** @description Allocation of assets to a party in a split resolution */
        SplitAllocation: {
            /**
             * Format: uuid
             * @description Party ID
             */
            partyId: string;
            /** @description Type of entity (Account, Character, etc.) */
            partyType: components["schemas"]["EntityType"];
            /** @description Assets allocated to this party */
            assets: components["schemas"]["EscrowAssetInput"][];
        };
        /** @description Request to split a stack */
        SplitStackRequest: {
            /**
             * Format: uuid
             * @description Stack to split
             */
            instanceId: string;
            /**
             * Format: double
             * @description Quantity to split off
             */
            quantity: number;
            /** @description Slot for new stack */
            targetSlotIndex?: number | null;
            /** @description Grid X for new stack */
            targetSlotX?: number | null;
            /** @description Grid Y for new stack */
            targetSlotY?: number | null;
        };
        /** @description Response after splitting */
        SplitStackResponse: {
            /** @description Whether split succeeded */
            success: boolean;
            /**
             * Format: uuid
             * @description Original stack ID
             */
            originalInstanceId: string;
            /**
             * Format: uuid
             * @description New stack ID
             */
            newInstanceId: string;
            /**
             * Format: double
             * @description Remaining quantity
             */
            originalQuantity: number;
            /**
             * Format: double
             * @description Split quantity
             */
            newQuantity: number;
        };
        /** @description Request to start an encounter managed by an Event Brain actor */
        StartEncounterRequest: {
            /** @description ID of the Event Brain actor that will manage this encounter */
            actorId: string;
            /**
             * Format: uuid
             * @description Unique identifier for this encounter
             */
            encounterId: string;
            /** @description Type of encounter (e.g., "combat", "conversation", "choreography") */
            encounterType: string;
            /** @description Character IDs of participants in the encounter */
            participants: string[];
            /** @description Optional initial data for the encounter */
            initialData?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Health and version status information for the website service */
        StatusResponse: {
            /**
             * @description Current health status of the website service
             * @enum {string}
             */
            status: "healthy" | "degraded" | "maintenance";
            /**
             * @description Current version of the website service
             * @example 1.0.0
             */
            version: string;
            /** @description Uptime in seconds */
            uptime: number;
            /** @description Message displayed during maintenance mode */
            maintenanceMessage?: string | null;
        };
        /**
         * @description Request to verify a Steam Session Ticket. The ticket is obtained client-side via
         *     ISteamUser::GetAuthTicketForWebApi("bannou"). SteamID is NOT included because
         *     it must be obtained from Steam's Web API response (never trust client-provided SteamID).
         */
        SteamVerifyRequest: {
            /**
             * @description Hex-encoded Steam Session Ticket from ISteamUser::GetAuthTicketForWebApi().
             *     Client converts ticket bytes to hex string: BitConverter.ToString(ticketData).Replace("-", "")
             * @example 140000006A7B3C8E...
             */
            ticket: string;
            /** @description Information about the client device (optional) */
            deviceInfo?: components["schemas"]["DeviceInfo"];
        };
        /** @description Request to stop a running actor */
        StopActorRequest: {
            /** @description ID of the actor to stop */
            actorId: string;
            /**
             * @description If true, allows behavior to complete current iteration
             * @default true
             */
            graceful: boolean;
        };
        /** @description Response confirming actor stop operation */
        StopActorResponse: {
            /** @description Whether the actor was successfully stopped */
            stopped: boolean;
            /** @description Final status of the actor after stopping */
            finalStatus: components["schemas"]["ActorStatus"];
        };
        /** @description Response containing a style definition */
        StyleDefinitionResponse: {
            /** @description Unique style identifier */
            styleId: string;
            /** @description Style name */
            name: string;
            /** @description Style category */
            category: string;
            /** @description Human-readable description */
            description?: string | null;
            /** @description Mode probability distribution */
            modeDistribution?: components["schemas"]["ModeDistribution"];
            /** @description Interval preferences */
            intervalPreferences?: components["schemas"]["IntervalPreferences"];
            /** @description Available forms */
            formTemplates?: components["schemas"]["FormTemplate"][] | null;
            /** @description Style-specific tune types */
            tuneTypes?: components["schemas"]["TuneType"][] | null;
            /** @description Default tempo */
            defaultTempo?: number;
            /** @description Harmony preferences */
            harmonyStyle?: components["schemas"]["HarmonyStyle"];
        };
        /** @description Brief style summary for listing */
        StyleSummary: {
            /** @description Style identifier */
            styleId: string;
            /** @description Style name */
            name: string;
            /** @description Style category */
            category: string;
            /** @description Brief description */
            description?: string | null;
        };
        /** @description Information about a subscription */
        SubscriptionInfo: {
            /**
             * Format: uuid
             * @description Unique identifier for the subscription
             */
            subscriptionId: string;
            /**
             * Format: uuid
             * @description ID of the account this subscription belongs to
             */
            accountId: string;
            /**
             * Format: uuid
             * @description ID of the subscribed service (game)
             */
            serviceId: string;
            /** @description Stub name of the service (denormalized for efficiency) */
            stubName: string;
            /** @description Display name of the service (denormalized for efficiency) */
            displayName?: string;
            /**
             * Format: date-time
             * @description When the subscription started
             */
            startDate: string;
            /**
             * Format: date-time
             * @description When the subscription expires (null for unlimited)
             */
            expirationDate?: string | null;
            /** @description Whether the subscription is currently active */
            isActive: boolean;
            /**
             * Format: date-time
             * @description When the subscription was cancelled (if applicable)
             */
            cancelledAt?: string | null;
            /** @description Reason for cancellation (if applicable) */
            cancellationReason?: string | null;
            /**
             * Format: date-time
             * @description When the subscription was created
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description When the subscription was last updated
             */
            updatedAt?: string | null;
        };
        /** @description Response containing list of subscriptions */
        SubscriptionListResponse: {
            /** @description List of subscriptions matching the filter criteria */
            subscriptions: components["schemas"]["SubscriptionInfo"][];
            /** @description Total number of subscriptions matching the filter */
            totalCount: number;
        };
        /** @description Current subscription status and plan details for an account */
        SubscriptionResponse: {
            /**
             * @description Current state of the subscription
             * @enum {string}
             */
            status: "active" | "inactive" | "trial" | "expired";
            /**
             * @description Subscription tier or plan type
             * @enum {string}
             */
            type: "free" | "basic" | "premium" | "lifetime";
            /**
             * Format: date-time
             * @description Date and time when the subscription expires
             */
            expiresAt?: string | null;
            /** @description Whether automatic renewal is enabled */
            autoRenew?: boolean;
            /** @description List of benefits included in the subscription */
            benefits?: string[];
        };
        /** @description Request to get related topic suggestions based on a source */
        SuggestRelatedRequest: {
            /** @description Documentation namespace for suggestions */
            namespace: string;
            /** @description Type of source to base suggestions on */
            suggestionSource: components["schemas"]["SuggestionSource"];
            /** @description The value for the suggestion source (document ID, slug, topic, or category) */
            sourceValue?: string;
            /**
             * Format: uuid
             * @description Optional session ID for personalized suggestions
             */
            sessionId?: string;
            /**
             * @description Maximum number of suggestions to return
             * @default 5
             */
            maxSuggestions: number;
            /**
             * @description Exclude documents viewed in current session
             * @default true
             */
            excludeRecentlyViewed: boolean;
        };
        /** @description Response containing suggested related topics for conversational flow */
        SuggestRelatedResponse: {
            /** @description The namespace suggestions are from */
            namespace: string;
            /** @description List of suggested related topics */
            suggestions: components["schemas"]["TopicSuggestion"][];
            /** @description Voice-friendly prompt for presenting suggestions */
            voicePrompt?: string;
            /** @description Whether suggestions were influenced by session history */
            sessionInfluenced?: boolean;
        };
        /**
         * @description Source type for generating related topic suggestions
         * @enum {string}
         */
        SuggestionSource: "document_id" | "slug" | "topic" | "category";
        /** @description Information about a repository sync operation */
        SyncInfo: {
            /**
             * Format: uuid
             * @description Unique identifier of the sync operation
             */
            syncId?: string;
            /** @description Result status of the sync */
            status?: components["schemas"]["SyncStatus"];
            /** @description What triggered the sync */
            triggeredBy?: components["schemas"]["SyncTrigger"];
            /**
             * Format: date-time
             * @description Timestamp when sync started
             */
            startedAt?: string;
            /**
             * Format: date-time
             * @description Timestamp when sync completed
             */
            completedAt?: string;
            /** @description Git commit hash that was synced (null if sync failed or repo is empty) */
            commitHash?: string | null;
            /** @description Total documents processed in sync */
            documentsProcessed?: number;
        };
        /** @description Request to trigger a manual repository sync */
        SyncRepositoryRequest: {
            /** @description Documentation namespace to sync */
            namespace: string;
            /**
             * @description Force full re-sync even if commit hash unchanged
             * @default false
             */
            force: boolean;
        };
        /** @description Response containing sync operation results and statistics */
        SyncRepositoryResponse: {
            /**
             * Format: uuid
             * @description Unique identifier of this sync operation
             */
            syncId: string;
            /** @description Result status of the sync */
            status: components["schemas"]["SyncStatus"];
            /** @description Git commit hash that was synced (null if sync failed or repo is empty) */
            commitHash?: string | null;
            /** @description Number of new documents created */
            documentsCreated?: number;
            /** @description Number of existing documents updated */
            documentsUpdated?: number;
            /** @description Number of documents deleted */
            documentsDeleted?: number;
            /** @description Number of documents that failed to process */
            documentsFailed?: number;
            /** @description Time taken for sync in milliseconds */
            durationMs?: number;
            /** @description Error message if sync failed */
            errorMessage?: string | null;
        };
        /**
         * @description Status of platform synchronization
         * @enum {string}
         */
        SyncStatus: "pending" | "synced" | "failed" | "not_linked";
        /**
         * @description What triggered the sync operation
         * @enum {string}
         */
        SyncTrigger: "manual" | "scheduled";
        /** @description A tempo change event */
        TempoEvent: {
            /** @description Tick position */
            tick: number;
            /**
             * Format: float
             * @description Tempo in BPM
             */
            bpm: number;
        };
        /** @description A tempo range with min and max BPM */
        TempoRange: {
            /** @description Minimum tempo */
            min: number;
            /** @description Maximum tempo */
            max: number;
        };
        /** @description Request to terminate a contract */
        TerminateContractInstanceRequest: {
            /**
             * Format: uuid
             * @description Contract to terminate
             */
            contractId: string;
            /**
             * Format: uuid
             * @description Entity requesting termination
             */
            requestingEntityId: string;
            /** @description Type of requesting entity */
            requestingEntityType: components["schemas"]["EntityType"];
            /** @description Reason for termination */
            reason?: string | null;
        };
        /** @description Request to terminate a specific session */
        TerminateSessionRequest: {
            /**
             * Format: uuid
             * @description ID of the session to terminate
             */
            sessionId: string;
        };
        /**
         * @description How the contract can be terminated
         * @enum {string}
         */
        TerminationPolicy: "mutual_consent" | "unilateral_with_notice" | "unilateral_immediate" | "non_terminable";
        /** @description Visual theme configuration including colors, fonts, and navigation */
        ThemeConfig: {
            /** @description Name of the active theme */
            themeName: string;
            /** @description Primary brand color in hex format */
            primaryColor: string;
            /** @description Secondary brand color in hex format */
            secondaryColor?: string;
            /** @description Default background color in hex format */
            backgroundColor?: string;
            /** @description Default text color in hex format */
            textColor?: string;
            /** @description Primary font family for the site */
            fontFamily?: string;
            /** @description Additional custom CSS styles */
            customCSS?: string | null;
            /** @description Site logo configuration for branding */
            logo?: components["schemas"]["Logo"];
            /**
             * Format: uri
             * @description URL of the site favicon
             */
            favicon?: string | null;
            /** @description Main navigation menu items */
            navigation?: components["schemas"]["NavigationItem"][];
        };
        /**
         * @description Current status of a matchmaking ticket
         * @enum {string}
         */
        TicketStatus: "searching" | "match_found" | "match_accepted" | "cancelled" | "expired";
        /** @description A time signature change event */
        TimeSignatureEvent: {
            /** @description Tick position */
            tick: number;
            /** @description Beats per measure */
            numerator: number;
            /** @description Beat unit (4 = quarter, 8 = eighth) */
            denominator: number;
        };
        /** @description A suggested related topic with relevance context */
        TopicSuggestion: {
            /**
             * Format: uuid
             * @description Unique identifier of the suggested document
             */
            documentId: string;
            /** @description URL-friendly slug of the suggested document */
            slug?: string;
            /** @description Title of the suggested document */
            title: string;
            /** @description Category of the suggested document */
            category?: components["schemas"]["DocumentCategory"];
            /** @description Explanation of why this document is relevant */
            relevanceReason?: string;
        };
        /**
         * @description Core personality trait axes. Each represents a spectrum from -1.0 to +1.0.
         *     Based on psychological research (Big Five + game-relevant extensions).
         * @enum {string}
         */
        TraitAxis: "OPENNESS" | "CONSCIENTIOUSNESS" | "EXTRAVERSION" | "AGREEABLENESS" | "NEUROTICISM" | "HONESTY" | "AGGRESSION" | "LOYALTY";
        /** @description A single personality trait with its current value and evolution history */
        TraitValue: {
            /** @description The personality axis this value represents */
            axis: components["schemas"]["TraitAxis"];
            /**
             * Format: float
             * @description Current trait value on the spectrum (-1.0 to +1.0)
             */
            value: number;
            /**
             * Format: date-time
             * @description When this trait last evolved (null if never changed since creation)
             */
            lastChangedAt?: string | null;
            /**
             * @description Number of times this trait has evolved
             * @default 0
             */
            changeCount: number;
        };
        /** @description Transaction details */
        TransactionResponse: {
            /** @description Transaction record */
            transaction: components["schemas"]["CurrencyTransactionRecord"];
        };
        /**
         * @description Classification of the currency transaction
         * @enum {string}
         */
        TransactionType: "mint" | "quest_reward" | "loot_drop" | "vendor_sale" | "autogain" | "refund" | "conversion_credit" | "burn" | "vendor_purchase" | "fee" | "expiration" | "cap_overflow" | "conversion_debit" | "transfer" | "trade" | "gift" | "escrow_deposit" | "escrow_release" | "escrow_refund";
        /** @description Request to transfer a party role to a new entity */
        TransferContractPartyRequest: {
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractInstanceId: string;
            /**
             * Format: uuid
             * @description Current party entity ID
             */
            fromEntityId: string;
            /** @description Current party entity type */
            fromEntityType: components["schemas"]["EntityType"];
            /**
             * Format: uuid
             * @description New entity ID to receive the role
             */
            toEntityId: string;
            /** @description New entity type */
            toEntityType: components["schemas"]["EntityType"];
            /**
             * Format: uuid
             * @description Guardian entity ID (must be current guardian)
             */
            guardianId: string;
            /** @description Guardian entity type */
            guardianType: string;
            /** @description Optional idempotency key for the operation */
            idempotencyKey?: string | null;
        };
        /** @description Response from transferring a party role */
        TransferContractPartyResponse: {
            /** @description Whether the transfer was successful */
            transferred: boolean;
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractId: string;
            /** @description Role that was transferred */
            role?: string;
            /**
             * Format: uuid
             * @description Previous party entity ID
             */
            fromEntityId?: string;
            /**
             * Format: uuid
             * @description New party entity ID
             */
            toEntityId?: string;
        };
        /** @description Request to transfer currency between wallets */
        TransferCurrencyRequest: {
            /**
             * Format: uuid
             * @description Source wallet ID
             */
            sourceWalletId: string;
            /**
             * Format: uuid
             * @description Target wallet ID
             */
            targetWalletId: string;
            /**
             * Format: uuid
             * @description Currency to transfer
             */
            currencyDefinitionId: string;
            /**
             * Format: double
             * @description Amount to transfer (must be positive)
             */
            amount: number;
            /** @description Must be a transfer type (transfer, trade, gift) */
            transactionType: components["schemas"]["TransactionType"];
            /** @description What triggered this transfer */
            referenceType?: string | null;
            /**
             * Format: uuid
             * @description Reference entity ID
             */
            referenceId?: string | null;
            /** @description Unique key to prevent duplicate processing */
            idempotencyKey: string;
            /** @description Free-form transaction metadata */
            metadata?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Result of transfer operation */
        TransferCurrencyResponse: {
            /** @description Created transaction record */
            transaction: components["schemas"]["CurrencyTransactionRecord"];
            /**
             * Format: double
             * @description Source wallet balance after transfer
             */
            sourceNewBalance: number;
            /**
             * Format: double
             * @description Target wallet balance after transfer
             */
            targetNewBalance: number;
            /** @description Whether target wallet cap was applied */
            targetCapApplied: boolean;
            /**
             * Format: double
             * @description Amount lost due to target wallet cap
             */
            targetCapAmountLost?: number | null;
        };
        /** @description Request to transfer item ownership */
        TransferItemRequest: {
            /**
             * Format: uuid
             * @description Item instance ID
             */
            instanceId: string;
            /**
             * Format: uuid
             * @description Target container ID
             */
            targetContainerId: string;
            /**
             * Format: double
             * @description Quantity to transfer (all if null)
             */
            quantity?: number | null;
        };
        /** @description Response after transfer */
        TransferItemResponse: {
            /** @description Whether transfer succeeded */
            success: boolean;
            /**
             * Format: uuid
             * @description Transferred item ID
             */
            instanceId: string;
            /**
             * Format: uuid
             * @description Previous container
             */
            sourceContainerId: string;
            /**
             * Format: uuid
             * @description New container
             */
            targetContainerId: string;
            /**
             * Format: double
             * @description Amount transferred
             */
            quantityTransferred: number;
        };
        /** @description Result of transferring assets to a party during resolution */
        TransferResult: {
            /**
             * Format: uuid
             * @description Party ID
             */
            partyId: string;
            /** @description Assets transferred (null if failed) */
            assets?: components["schemas"]["EscrowAssetBundle"];
            /** @description Whether transfer succeeded */
            success: boolean;
            /** @description Error message if failed */
            error?: string | null;
        };
        /** @description Position, rotation, and scale in 3D space */
        Transform: {
            /** @description Position relative to parent */
            position: components["schemas"]["Vector3"];
            /** @description Rotation relative to parent */
            rotation: components["schemas"]["Quaternion"];
            /** @description Scale relative to parent */
            scale: components["schemas"]["Vector3"];
        };
        /** @description A style-specific tune type definition */
        TuneType: {
            /** @description Tune type name (e.g., "reel", "jig") */
            name: string;
            /** @description Time signature for this tune type */
            meter: components["schemas"]["TimeSignatureEvent"];
            /** @description Typical tempo range */
            tempoRange?: components["schemas"]["TempoRange"];
            /** @description Default form for this tune type */
            defaultForm?: string | null;
            /** @description Rhythm pattern names to use */
            rhythmPatterns?: string[] | null;
        };
        /** @description Request to unlock a contract from guardian custody */
        UnlockContractRequest: {
            /**
             * Format: uuid
             * @description Contract instance ID to unlock
             */
            contractInstanceId: string;
            /**
             * Format: uuid
             * @description Guardian entity ID (must match current guardian)
             */
            guardianId: string;
            /** @description Guardian entity type */
            guardianType: string;
            /** @description Optional idempotency key for the operation */
            idempotencyKey?: string | null;
        };
        /** @description Response from unlocking a contract */
        UnlockContractResponse: {
            /** @description Whether the contract was unlocked */
            unlocked: boolean;
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractId: string;
        };
        /** @description An unlocked achievement instance */
        UnlockedAchievement: {
            /** @description Achievement identifier */
            achievementId: string;
            /** @description Achievement name */
            displayName: string;
            /** @description Achievement description */
            description?: string;
            /** @description Point value */
            points: number;
            /** @description Achievement icon */
            iconUrl?: string | null;
            /**
             * Format: date-time
             * @description When it was unlocked
             */
            unlockedAt: string;
        };
        /** @description Request to unpin a previously pinned save version */
        UnpinVersionRequest: {
            /**
             * Format: uuid
             * @description ID of the owning entity
             */
            ownerId: string;
            /** @description Type of entity that owns this save slot */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Slot name */
            slotName: string;
            /** @description Version to unpin */
            versionNumber: number;
        };
        /** @description A scene reference that could not be resolved */
        UnresolvedReference: {
            /**
             * Format: uuid
             * @description Node ID containing the reference
             */
            nodeId: string;
            /** @description refId of the referencing node */
            refId: string;
            /**
             * Format: uuid
             * @description ID of the scene that could not be resolved
             */
            referencedSceneId: string;
            /** @description Why the reference could not be resolved */
            reason: components["schemas"]["UnresolvedReferenceReason"];
            /** @description For circular references, the cycle path (sceneId chain) */
            cyclePath?: string[] | null;
        };
        /**
         * @description Reason why a scene reference could not be resolved
         * @enum {string}
         */
        UnresolvedReferenceReason: "not_found" | "circular_reference" | "depth_exceeded" | "access_denied";
        /** @description Request to update an achievement definition */
        UpdateAchievementDefinitionRequest: {
            /**
             * Format: uuid
             * @description ID of the game service
             */
            gameServiceId: string;
            /** @description ID of the achievement to update */
            achievementId: string;
            /** @description New display name */
            displayName?: string | null;
            /** @description New description */
            description?: string | null;
            /** @description New active status */
            isActive?: boolean | null;
            /** @description Updated platform ID mappings */
            platformIds?: {
                [key: string]: string;
            } | null;
        };
        /** @description Request to update an existing actor template */
        UpdateActorTemplateRequest: {
            /**
             * Format: uuid
             * @description ID of the template to update
             */
            templateId: string;
            /** @description New behavior reference (triggers behavior.updated subscription) */
            behaviorRef?: string | null;
            /** @description Updated configuration settings */
            configuration?: {
                [key: string]: unknown;
            } | null;
            /** @description Updated auto-spawn configuration */
            autoSpawn?: components["schemas"]["AutoSpawnConfig"];
            /** @description Updated tick interval in milliseconds */
            tickIntervalMs?: number | null;
            /** @description Updated auto-save interval in seconds */
            autoSaveIntervalSeconds?: number | null;
        };
        /** @description Request to update bundle metadata */
        UpdateBundleRequest: {
            /** @description Human-readable bundle identifier to update */
            bundleId: string;
            /** @description New bundle name (null to leave unchanged) */
            name?: string | null;
            /** @description New bundle description (null to leave unchanged) */
            description?: string | null;
            /** @description Replace all tags with these (null to leave unchanged) */
            tags?: {
                [key: string]: string;
            } | null;
            /** @description Tags to add (merged with existing) */
            addTags?: {
                [key: string]: string;
            } | null;
            /** @description Tag keys to remove */
            removeTags?: string[] | null;
            /** @description Optional reason for the update (recorded in version history) */
            reason?: string | null;
        };
        /** @description Result of bundle update operation */
        UpdateBundleResponse: {
            /** @description Human-readable bundle identifier that was updated */
            bundleId: string;
            /** @description New version number after update */
            version: number;
            /** @description Version number before update */
            previousVersion: number;
            /** @description List of changes made (e.g., "name changed", "tag 'env' added") */
            changes: string[];
            /**
             * Format: date-time
             * @description When the update occurred
             */
            updatedAt?: string;
        };
        /** @description Request to update container properties */
        UpdateContainerRequest: {
            /**
             * Format: uuid
             * @description Container ID to update
             */
            containerId: string;
            /** @description New max slots */
            maxSlots?: number | null;
            /**
             * Format: double
             * @description New max weight
             */
            maxWeight?: number | null;
            /** @description New grid width */
            gridWidth?: number | null;
            /** @description New grid height */
            gridHeight?: number | null;
            /**
             * Format: double
             * @description New max volume
             */
            maxVolume?: number | null;
            /** @description New allowed categories */
            allowedCategories?: string[] | null;
            /** @description New forbidden categories */
            forbiddenCategories?: string[] | null;
            /** @description New allowed tags */
            allowedTags?: string[] | null;
            /** @description New container tags */
            tags?: string[] | null;
            /** @description New metadata */
            metadata?: Record<string, never> | null;
        };
        /** @description Request to update contract metadata */
        UpdateContractMetadataRequest: {
            /**
             * Format: uuid
             * @description Contract instance ID
             */
            contractId: string;
            /** @description Which metadata to update */
            metadataType: components["schemas"]["MetadataType"];
            /** @description Metadata to set or merge */
            data: {
                [key: string]: unknown;
            };
        };
        /** @description Request to update a map definition */
        UpdateDefinitionRequest: {
            /**
             * Format: uuid
             * @description Definition ID to update
             */
            definitionId: string;
            /** @description New name (optional) */
            name?: string | null;
            /** @description New description (optional) */
            description?: string | null;
            /** @description New layer configurations (replaces existing) */
            layers?: components["schemas"]["LayerDefinition"][] | null;
            /** @description New default bounds */
            defaultBounds?: components["schemas"]["Bounds"];
            /** @description New metadata (replaces existing) */
            metadata?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Request to update the phase of an active encounter */
        UpdateEncounterPhaseRequest: {
            /** @description ID of the Event Brain actor managing the encounter */
            actorId: string;
            /** @description New phase name for the encounter */
            phase: string;
        };
        /** @description Response after updating encounter phase */
        UpdateEncounterPhaseResponse: {
            /** @description ID of the actor managing the encounter */
            actorId: string;
            /** @description Previous phase name */
            previousPhase?: string | null;
            /** @description Current phase name after update */
            currentPhase: string;
        };
        /** @description Request to update mutable fields of an item template */
        UpdateItemTemplateRequest: {
            /**
             * Format: uuid
             * @description Template ID to update
             */
            templateId: string;
            /** @description New display name */
            name?: string | null;
            /** @description New description */
            description?: string | null;
            /** @description New subcategory */
            subcategory?: string | null;
            /** @description New tags (replaces existing) */
            tags?: string[] | null;
            /** @description New rarity tier */
            rarity?: components["schemas"]["ItemRarity"];
            /**
             * Format: double
             * @description New weight value
             */
            weight?: number | null;
            /**
             * Format: double
             * @description New volume value
             */
            volume?: number | null;
            /** @description New grid width */
            gridWidth?: number | null;
            /** @description New grid height */
            gridHeight?: number | null;
            /** @description New rotation setting */
            canRotate?: boolean | null;
            /**
             * Format: double
             * @description New base value
             */
            baseValue?: number | null;
            /** @description New tradeable setting */
            tradeable?: boolean | null;
            /** @description New destroyable setting */
            destroyable?: boolean | null;
            /** @description New max durability */
            maxDurability?: number | null;
            /** @description New available realms */
            availableRealms?: string[] | null;
            /** @description New stats */
            stats?: Record<string, never> | null;
            /** @description New effects */
            effects?: Record<string, never> | null;
            /** @description New requirements */
            requirements?: Record<string, never> | null;
            /** @description New display properties */
            display?: Record<string, never> | null;
            /** @description New metadata */
            metadata?: Record<string, never> | null;
            /** @description Active status */
            isActive?: boolean | null;
        };
        /** @description Request to update a leaderboard definition */
        UpdateLeaderboardDefinitionRequest: {
            /**
             * Format: uuid
             * @description ID of the game service
             */
            gameServiceId: string;
            /** @description ID of the leaderboard to update */
            leaderboardId: string;
            /** @description New display name */
            displayName?: string | null;
            /** @description New description */
            description?: string | null;
            /** @description New visibility setting */
            isPublic?: boolean | null;
        };
        /**
         * @description How to handle score updates
         * @enum {string}
         */
        UpdateMode: "replace" | "increment" | "max" | "min";
        /** @description Request to update an account password */
        UpdatePasswordRequest: {
            /**
             * Format: uuid
             * @description ID of the account to update
             */
            accountId: string;
            /** @description New pre-hashed password from Auth service */
            passwordHash: string;
        };
        /** @description Request to update an account profile */
        UpdateProfileRequest: {
            /**
             * Format: uuid
             * @description ID of the account to update
             */
            accountId: string;
            /** @description New display name for the account */
            displayName?: string | null;
            /** @description Updated custom metadata for the account */
            metadata?: {
                [key: string]: unknown;
            } | null;
        };
        /** @description Request to update repository binding configuration */
        UpdateRepositoryBindingRequest: {
            /** @description Documentation namespace of the binding to update */
            namespace: string;
            /** @description Enable or disable automatic syncing */
            syncEnabled?: boolean;
            /** @description New sync interval in minutes */
            syncIntervalMinutes?: number;
            /** @description New glob patterns for files to include (null to keep unchanged) */
            filePatterns?: string[] | null;
            /** @description New glob patterns for files to exclude (null to keep unchanged) */
            excludePatterns?: string[] | null;
            /** @description New directory-to-category mapping (null to keep unchanged) */
            categoryMapping?: {
                [key: string]: string;
            } | null;
            /** @description New default category for unmapped documents */
            defaultCategory?: components["schemas"]["DocumentCategory"];
            /** @description Enable or disable archive functionality */
            archiveEnabled?: boolean;
            /** @description Enable or disable archiving after each sync */
            archiveOnSync?: boolean;
        };
        /** @description Response containing the updated binding configuration */
        UpdateRepositoryBindingResponse: {
            /** @description Updated binding configuration */
            binding: components["schemas"]["RepositoryBindingInfo"];
        };
        /** @description Request to update an existing scene */
        UpdateSceneRequest: {
            /** @description The updated scene document (sceneId must match existing) */
            scene: components["schemas"]["Scene"];
            /** @description Checkout token if updating via checkout workflow */
            checkoutToken?: string | null;
        };
        /** @description Request to update email verification status */
        UpdateVerificationRequest: {
            /**
             * Format: uuid
             * @description ID of the account to update
             */
            accountId: string;
            /** @description New email verification status */
            emailVerified: boolean;
        };
        /** @description Request to initiate an asset upload and receive a pre-signed URL */
        UploadRequest: {
            /**
             * @description Owner of this asset operation. NOT a session ID.
             *     For user-initiated uploads: the accountId (UUID format).
             *     For service-initiated uploads: the service name (e.g., "behavior", "orchestrator").
             */
            owner: string;
            /** @description Original filename with extension */
            filename: string;
            /**
             * Format: int64
             * @description File size in bytes
             */
            size: number;
            /** @description MIME content type (e.g., image/png, model/gltf-binary) */
            contentType: string;
            /** @description Optional metadata for asset categorization */
            metadata?: components["schemas"]["AssetMetadataInput"];
        };
        /** @description Response containing pre-signed URL and configuration for uploading an asset */
        UploadResponse: {
            /**
             * Format: uuid
             * @description Unique upload session identifier
             */
            uploadId: string;
            /**
             * Format: uri
             * @description Pre-signed URL for uploading the file
             */
            uploadUrl: string;
            /**
             * Format: date-time
             * @description When the upload URL expires
             */
            expiresAt: string;
            /** @description Configuration for multipart uploads if file size requires it */
            multipart?: components["schemas"]["MultipartConfig"];
            /** @description Headers the client must include when uploading to the pre-signed URL */
            requiredHeaders?: {
                [key: string]: string;
            };
        };
        /** @description Request to validate ABML YAML content against schema and semantic rules */
        ValidateAbmlRequest: {
            /** @description Raw ABML YAML content to validate */
            abmlContent: string;
            /**
             * @description Enable strict validation mode with enhanced checking
             * @default false
             */
            strictMode: boolean;
        };
        /** @description Response containing the results of ABML validation including errors and warnings */
        ValidateAbmlResponse: {
            /** @description Whether the ABML definition is valid */
            isValid: boolean;
            /** @description List of validation errors if invalid */
            validationErrors?: components["schemas"]["ValidationError"][] | null;
            /** @description Semantic warnings that don't prevent compilation */
            semanticWarnings?: string[] | null;
            /** @description ABML schema version used for validation */
            schemaVersion?: string | null;
        };
        /** @description Request to validate a deposit without executing */
        ValidateDepositRequest: {
            /**
             * Format: uuid
             * @description Escrow ID
             */
            escrowId: string;
            /**
             * Format: uuid
             * @description Party to validate
             */
            partyId: string;
            /** @description Type of entity (Account, Character, etc.) */
            partyType: components["schemas"]["EntityType"];
            /** @description Assets to validate */
            assets: components["schemas"]["EscrowAssetBundleInput"];
        };
        /** @description Response from deposit validation */
        ValidateDepositResponse: {
            /** @description Whether the deposit would be valid */
            valid: boolean;
            /** @description Validation errors */
            errors: string[];
            /** @description Validation warnings */
            warnings: string[];
        };
        /** @description Request to validate an existing GOAP plan against current world state */
        ValidateGoapPlanRequest: {
            /** @description The plan to validate */
            plan: components["schemas"]["GoapPlanResult"];
            /** @description Index of the action currently being executed */
            currentActionIndex: number;
            /** @description Current world state */
            worldState: {
                [key: string]: unknown;
            };
            /** @description All active goals for priority checking */
            activeGoals?: components["schemas"]["GoapGoal"][] | null;
        };
        /** @description Response indicating whether a GOAP plan is still valid and suggested next action */
        ValidateGoapPlanResponse: {
            /** @description Whether the plan is still valid */
            isValid: boolean;
            /**
             * @description Reason for the validation result
             * @enum {string}
             */
            reason: "none" | "preconditionInvalidated" | "actionFailed" | "betterGoalAvailable" | "planCompleted" | "goalAlreadySatisfied" | "suboptimalPlan";
            /**
             * @description Suggested action based on validation
             * @enum {string}
             */
            suggestedAction: "continue" | "replan" | "abort";
            /** @description Index where plan became invalid (if applicable) */
            invalidatedAtIndex?: number;
            /** @description Additional details about the validation result. Null when no additional context is needed. */
            message?: string | null;
        };
        /** @description Request to validate MIDI-JSON structure */
        ValidateMidiJsonRequest: {
            /** @description MIDI-JSON structure to validate */
            midiJson: components["schemas"]["MidiJson"];
            /**
             * @description Enable strict validation with additional checks
             * @default false
             */
            strictMode: boolean;
        };
        /** @description Response containing validation results */
        ValidateMidiJsonResponse: {
            /** @description Whether the MIDI-JSON is valid */
            isValid: boolean;
            /** @description Validation errors if invalid */
            errors?: components["schemas"]["ValidationError"][] | null;
            /** @description Non-fatal warnings */
            warnings?: string[] | null;
        };
        /** @description Request to validate a scene structure */
        ValidateSceneRequest: {
            /** @description The scene to validate */
            scene: components["schemas"]["Scene"];
            /**
             * @description Whether to apply registered game-specific validation rules
             * @default true
             */
            applyGameRules: boolean;
        };
        /** @description Response from token validation containing validity status and associated account details */
        ValidateTokenResponse: {
            /** @description Whether the token is valid and not expired */
            valid: boolean;
            /**
             * Format: uuid
             * @description Unique identifier for the account associated with the token
             */
            accountId: string;
            /**
             * Format: uuid
             * @description Session identifier for WebSocket connections and service routing
             */
            sessionId: string;
            /** @description List of roles assigned to the authenticated user */
            roles?: string[] | null;
            /**
             * @description Authorization strings from active subscriptions.
             *     Format: "{stubName}:{state}" (e.g., "my-game:authorized")
             */
            authorizations?: string[] | null;
            /** @description Seconds until expiration */
            remainingTime?: number;
        };
        /** @description A single condition to check against an API response */
        ValidationCondition: {
            /** @description The type of validation condition to check */
            type: components["schemas"]["ValidationConditionType"];
            /**
             * @description JsonPath expression to extract value from response.
             *     Required for jsonPathEquals, jsonPathExists, jsonPathNotExists.
             *     Example: "$.balance", "$.items[0].status"
             */
            jsonPath?: string | null;
            /**
             * @description Expected value for comparison conditions.
             *     Type coercion applied: "true"/"false" for booleans, numeric strings for numbers.
             */
            expectedValue?: string | null;
            /** @description Comparison operator for numeric comparisons */
            operator?: components["schemas"]["ComparisonOperator"];
            /** @description HTTP status codes for statusCodeIn condition */
            statusCodes?: number[];
        };
        /**
         * @description Type of validation condition
         * @enum {string}
         */
        ValidationConditionType: "statusCodeIn" | "jsonPathEquals" | "jsonPathNotEquals" | "jsonPathExists" | "jsonPathNotExists" | "jsonPathGreaterThan" | "jsonPathLessThan" | "jsonPathContains";
        /** @description Detailed validation error with type, location, and message information */
        ValidationError: {
            /**
             * @description Type of validation error
             * @enum {string}
             */
            type: "syntax" | "semantic" | "schema" | "context" | "service_dependency";
            /** @description Human-readable error message */
            message: string;
            /** @description Line number where the error occurred (if applicable) */
            lineNumber?: number;
            /** @description Column number where the error occurred (if applicable) */
            columnNumber?: number;
            /**
             * @description YAML path to the problematic element
             * @example behaviors.morning_startup.actions[0]
             */
            yamlPath?: string | null;
        };
        /** @description Records a validation check failure */
        ValidationFailure: {
            /**
             * Format: date-time
             * @description When the failure was detected
             */
            detectedAt: string;
            /** @description Type of asset affected */
            assetType: components["schemas"]["AssetType"];
            /** @description Description of the affected asset */
            assetDescription: string;
            /** @description Type of validation failure */
            failureType: components["schemas"]["ValidationFailureType"];
            /**
             * Format: uuid
             * @description Which party deposit is affected
             */
            affectedPartyId: string;
            /** @description Type of the affected party */
            affectedPartyType: components["schemas"]["EntityType"];
            /** @description Additional failure details */
            details?: {
                [key: string]: unknown;
            } | null;
        };
        /**
         * @description Type of validation failure detected.
         *     - asset_missing: Asset no longer exists in escrow custody
         *     - asset_mutated: Asset properties changed (e.g., item durability)
         *     - asset_expired: Asset has a time-based expiration that triggered
         *     - balance_mismatch: Wallet balance does not match expected held amount
         * @enum {string}
         */
        ValidationFailureType: "asset_missing" | "asset_mutated" | "asset_expired" | "balance_mismatch";
        /** @description Result of scene validation */
        ValidationResult: {
            /** @description Whether the scene passed all validation checks */
            valid: boolean;
            /** @description Validation errors (severity = error) */
            errors?: components["schemas"]["ValidationError"][] | null;
            /** @description Validation warnings (severity = warning) */
            warnings?: components["schemas"]["ValidationError"][] | null;
        };
        /** @description A validation rule definition */
        ValidationRule: {
            /** @description Unique rule identifier within the gameId+sceneType */
            ruleId: string;
            /** @description Human-readable description of the rule */
            description: string;
            /** @description Whether violation is an error or warning */
            severity: components["schemas"]["ValidationSeverity"];
            /** @description Type of validation check */
            ruleType: components["schemas"]["ValidationRuleType"];
            /** @description Rule-specific configuration */
            config?: components["schemas"]["ValidationRuleConfig"];
        };
        /** @description Configuration for a validation rule */
        ValidationRuleConfig: {
            /** @description Filter to nodes of this type (for require_tag) */
            nodeType?: string | null;
            /** @description Tag to check for */
            tag?: string | null;
            /** @description Minimum occurrences required */
            minCount?: number | null;
            /** @description Maximum occurrences allowed */
            maxCount?: number | null;
            /** @description JSONPath to required annotation field (for require_annotation) */
            annotationPath?: string | null;
            /** @description Custom validation expression (for custom_expression) */
            expression?: string | null;
        };
        /**
         * @description Type of validation check to perform
         * @enum {string}
         */
        ValidationRuleType: "require_tag" | "require_node_type" | "forbid_tag" | "require_annotation" | "custom_expression";
        /**
         * @description Severity level of a validation issue
         * @enum {string}
         */
        ValidationSeverity: "error" | "warning";
        /** @description A point or direction in 3D space */
        Vector3: {
            /**
             * Format: double
             * @description X coordinate
             */
            x: number;
            /**
             * Format: double
             * @description Y coordinate
             */
            y: number;
            /**
             * Format: double
             * @description Z coordinate
             */
            z: number;
        };
        /** @description Request to verify a condition for conditional escrow */
        VerifyConditionRequest: {
            /**
             * Format: uuid
             * @description Escrow ID
             */
            escrowId: string;
            /** @description Whether the condition was met */
            conditionMet: boolean;
            /**
             * Format: uuid
             * @description Verifier entity ID
             */
            verifierId: string;
            /** @description Verifier entity type */
            verifierType: components["schemas"]["EntityType"];
            /** @description Proof/evidence data */
            verificationData?: {
                [key: string]: unknown;
            } | null;
            /** @description Idempotency key */
            idempotencyKey: string;
        };
        /** @description Response from verifying a condition on an escrow */
        VerifyConditionResponse: {
            /** @description Updated escrow agreement */
            escrow: components["schemas"]["EscrowAgreement"];
            /** @description Whether this triggered release/refund */
            triggered: boolean;
        };
        /** @description Request to verify data integrity of a save version via hash comparison */
        VerifyIntegrityRequest: {
            /** @description Game identifier for namespace isolation */
            gameId: string;
            /**
             * Format: uuid
             * @description ID of the owning entity
             */
            ownerId: string;
            /** @description Type of entity that owns this save slot */
            ownerType: components["schemas"]["OwnerType"];
            /** @description Name of the slot to verify */
            slotName: string;
            /** @description Version to verify (latest if null) */
            versionNumber?: number | null;
        };
        /** @description Result of integrity verification with hash comparison details */
        VerifyIntegrityResponse: {
            /** @description Whether integrity check passed */
            valid: boolean;
            /** @description Version that was verified */
            versionNumber: number;
            /** @description Expected SHA-256 hash */
            expectedHash?: string;
            /** @description Actual hash (null if data unavailable) */
            actualHash?: string | null;
            /** @description Error details if verification failed */
            errorMessage?: string | null;
        };
        /** @description Information about a specific version */
        VersionInfo: {
            /** @description Version string */
            version: string;
            /**
             * Format: date-time
             * @description When this version was created
             */
            createdAt: string;
            /** @description Who created this version */
            createdBy?: string | null;
            /** @description Summary of changes */
            changesSummary?: string | null;
            /** @description Node count at this version */
            nodeCount?: number;
        };
        /** @description Metadata for a single save version including size and checkpoint info */
        VersionResponse: {
            /** @description Version number */
            versionNumber: number;
            /**
             * Format: uuid
             * @description Reference to asset in lib-asset
             */
            assetId?: string;
            /** @description SHA-256 hash */
            contentHash: string;
            /**
             * Format: int64
             * @description Size in bytes
             */
            sizeBytes: number;
            /**
             * Format: int64
             * @description Compressed size if applicable
             */
            compressedSizeBytes?: number;
            /** @description Schema version */
            schemaVersion?: string | null;
            /** @description Human-readable name */
            displayName?: string | null;
            /** @description Whether version is pinned */
            pinned?: boolean;
            /** @description Checkpoint name if pinned */
            checkpointName?: string | null;
            /**
             * Format: date-time
             * @description Creation timestamp
             */
            createdAt: string;
            /** @description Custom metadata */
            metadata?: {
                [key: string]: string;
            };
        };
        /** @description Request to apply voice leading to a chord sequence */
        VoiceLeadRequest: {
            /** @description Chord symbols to voice */
            chords: components["schemas"]["ChordSymbol"][];
            /** @description Number of voices */
            voiceCount: number;
            /** @description Pitch range per voice (defaults based on voice count) */
            ranges?: components["schemas"]["PitchRange"][] | null;
            /** @description Voice leading rules to apply */
            rules?: components["schemas"]["VoiceLeadingRules"];
        };
        /** @description Response containing voiced chords */
        VoiceLeadResponse: {
            /** @description Voiced chord realizations */
            voicings: components["schemas"]["VoicedChord"][];
            /** @description Voice leading rule violations (warnings) */
            violations?: components["schemas"]["VoiceLeadingViolation"][] | null;
        };
        /** @description Rules for voice leading */
        VoiceLeadingRules: {
            /**
             * @description Avoid parallel perfect fifths
             * @default true
             */
            avoidParallelFifths: boolean;
            /**
             * @description Avoid parallel octaves
             * @default true
             */
            avoidParallelOctaves: boolean;
            /**
             * @description Prefer stepwise voice motion
             * @default true
             */
            preferStepwiseMotion: boolean;
            /**
             * @description Avoid voice crossing
             * @default true
             */
            avoidVoiceCrossing: boolean;
            /**
             * @description Maximum leap in semitones
             * @default 7
             */
            maxLeap: number;
        };
        /** @description A voice leading rule violation */
        VoiceLeadingViolation: {
            /** @description Type of violation */
            type: components["schemas"]["VoiceLeadingViolationType"];
            /** @description Position in the progression (0-based) */
            position: number;
            /** @description Voice indices involved (0 = bass) */
            voices: number[];
            /** @description Severity (true = error, false = warning) */
            isError: boolean;
            /** @description Human-readable description */
            message: string;
        };
        /**
         * @description Type of voice leading rule violation
         * @enum {string}
         */
        VoiceLeadingViolationType: "ParallelFifths" | "ParallelOctaves" | "VoiceCrossing" | "VoiceOverlap" | "LargeLeap" | "UnresolvedLeap" | "DoubledLeadingTone";
        /** @description A chord with specific voice pitches */
        VoicedChord: {
            /** @description Original chord symbol */
            symbol: components["schemas"]["ChordSymbol"];
            /** @description Pitches from lowest to highest voice */
            pitches: components["schemas"]["Pitch"][];
        };
        /**
         * @description Shape of a volume node for spatial bounds
         * @enum {string}
         */
        VolumeShape: "box" | "sphere" | "capsule" | "cylinder";
        /**
         * @description Type of entity that owns a wallet
         * @enum {string}
         */
        WalletOwnerType: "account" | "character" | "npc" | "guild" | "faction" | "location" | "system";
        /** @description Wallet details */
        WalletResponse: {
            /**
             * Format: uuid
             * @description Unique wallet identifier
             */
            walletId: string;
            /**
             * Format: uuid
             * @description Owner entity ID
             */
            ownerId: string;
            /** @description Owner type */
            ownerType: components["schemas"]["WalletOwnerType"];
            /**
             * Format: uuid
             * @description Realm ID
             */
            realmId?: string | null;
            /** @description Current wallet status */
            status: components["schemas"]["WalletStatus"];
            /** @description Reason wallet was frozen */
            frozenReason?: string | null;
            /**
             * Format: date-time
             * @description When wallet was frozen
             */
            frozenAt?: string | null;
            /** @description Who froze the wallet */
            frozenBy?: string | null;
            /**
             * Format: date-time
             * @description Creation timestamp
             */
            createdAt: string;
            /**
             * Format: date-time
             * @description Last transaction timestamp
             */
            lastActivityAt?: string | null;
        };
        /**
         * @description Current status of a wallet
         * @enum {string}
         */
        WalletStatus: "active" | "frozen" | "closed";
        /** @description Wallet with all non-zero balances */
        WalletWithBalancesResponse: {
            /** @description Wallet details */
            wallet: components["schemas"]["WalletResponse"];
            /** @description All non-zero balances in this wallet */
            balances: components["schemas"]["BalanceSummary"][];
        };
        /**
         * @description How container weight propagates to parent
         * @enum {string}
         */
        WeightContribution: "none" | "self_only" | "self_plus_contents";
        /**
         * @description Precision for weight values (consistent with CurrencyPrecision)
         * @enum {string}
         */
        WeightPrecision: "integer" | "decimal_1" | "decimal_2" | "decimal_3";
    };
    responses: never;
    parameters: never;
    requestBodies: never;
    headers: never;
    pathItems: never;
}

type Schemas$B = components['schemas'];
/**
 * Typed proxy for Account API endpoints.
 */
declare class AccountProxy {
    private readonly client;
    /**
     * Creates a new AccountProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Update account profile
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    updateProfileAsync(request: Schemas$B['UpdateProfileRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$B['AccountResponse']>>;
    /**
     * Update account password hash
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    updatePasswordHashEventAsync(request: Schemas$B['UpdatePasswordRequest'], channel?: number): Promise<void>;
    /**
     * Update email verification status
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    updateVerificationStatusEventAsync(request: Schemas$B['UpdateVerificationRequest'], channel?: number): Promise<void>;
}

type Schemas$A = components['schemas'];
/**
 * Typed proxy for Achievement API endpoints.
 */
declare class AchievementProxy {
    private readonly client;
    /**
     * Creates a new AchievementProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Create a new achievement definition
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createAchievementDefinitionAsync(request: Schemas$A['CreateAchievementDefinitionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$A['AchievementDefinitionResponse']>>;
    /**
     * List achievement definitions
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listAchievementDefinitionsAsync(request: Schemas$A['ListAchievementDefinitionsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$A['ListAchievementDefinitionsResponse']>>;
    /**
     * Update achievement definition
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    updateAchievementDefinitionAsync(request: Schemas$A['UpdateAchievementDefinitionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$A['AchievementDefinitionResponse']>>;
    /**
     * Delete achievement definition
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    deleteAchievementDefinitionEventAsync(request: Schemas$A['DeleteAchievementDefinitionRequest'], channel?: number): Promise<void>;
    /**
     * Get entity's achievement progress
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getAchievementProgressAsync(request: Schemas$A['GetAchievementProgressRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$A['AchievementProgressResponse']>>;
    /**
     * List unlocked achievements
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listUnlockedAchievementsAsync(request: Schemas$A['ListUnlockedAchievementsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$A['ListUnlockedAchievementsResponse']>>;
}

type Schemas$z = components['schemas'];
/**
 * Typed proxy for Actor API endpoints.
 */
declare class ActorProxy {
    private readonly client;
    /**
     * Creates a new ActorProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Create an actor template (category definition)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createActorTemplateAsync(request: Schemas$z['CreateActorTemplateRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$z['ActorTemplateResponse']>>;
    /**
     * Update an actor template
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    updateActorTemplateAsync(request: Schemas$z['UpdateActorTemplateRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$z['ActorTemplateResponse']>>;
    /**
     * Delete an actor template
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    deleteActorTemplateAsync(request: Schemas$z['DeleteActorTemplateRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$z['DeleteActorTemplateResponse']>>;
    /**
     * Spawn a new actor from a template
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    spawnActorAsync(request: Schemas$z['SpawnActorRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$z['ActorInstanceResponse']>>;
    /**
     * Stop a running actor
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    stopActorAsync(request: Schemas$z['StopActorRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$z['StopActorResponse']>>;
    /**
     * Inject a perception event into an actor's queue (testing)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    injectPerceptionAsync(request: Schemas$z['InjectPerceptionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$z['InjectPerceptionResponse']>>;
    /**
     * Start an encounter managed by an Event Brain actor
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    startEncounterEventAsync(request: Schemas$z['StartEncounterRequest'], channel?: number): Promise<void>;
    /**
     * Update the phase of an active encounter
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    updateEncounterPhaseAsync(request: Schemas$z['UpdateEncounterPhaseRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$z['UpdateEncounterPhaseResponse']>>;
    /**
     * End an active encounter
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    endEncounterAsync(request: Schemas$z['EndEncounterRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$z['EndEncounterResponse']>>;
}

type Schemas$y = components['schemas'];
/**
 * Typed proxy for Assets API endpoints.
 */
declare class AssetsProxy {
    private readonly client;
    /**
     * Creates a new AssetsProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Request upload URL for a new asset
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    requestUploadAsync(request: Schemas$y['UploadRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$y['UploadResponse']>>;
    /**
     * Mark upload as complete, trigger processing
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    completeUploadAsync(request: Schemas$y['CompleteUploadRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$y['AssetMetadata']>>;
    /**
     * Get asset metadata and download URL
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getAssetAsync(request: Schemas$y['GetAssetRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$y['AssetWithDownloadUrl']>>;
    /**
     * List all versions of an asset
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listAssetVersionsAsync(request: Schemas$y['ListVersionsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$y['AssetVersionList']>>;
    /**
     * Search assets by tags, type, or realm
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    searchAssetsAsync(request: Schemas$y['AssetSearchRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$y['AssetSearchResult']>>;
    /**
     * Batch asset metadata lookup
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    bulkGetAssetsAsync(request: Schemas$y['BulkGetAssetsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$y['BulkGetAssetsResponse']>>;
}

type Schemas$x = components['schemas'];
/**
 * Typed proxy for Auth API endpoints.
 */
declare class AuthProxy {
    private readonly client;
    /**
     * Creates a new AuthProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Login with email/password
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    loginAsync(request: Schemas$x['LoginRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$x['AuthResponse']>>;
    /**
     * Register new user account
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    registerAsync(request: Schemas$x['RegisterRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$x['RegisterResponse']>>;
    /**
     * Complete OAuth2 flow (browser redirect callback)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    completeOAuthAsync(request: Schemas$x['OAuthCallbackRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$x['AuthResponse']>>;
    /**
     * Verify Steam Session Ticket
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    verifySteamAuthAsync(request: Schemas$x['SteamVerifyRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$x['AuthResponse']>>;
    /**
     * Refresh access token
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    refreshTokenAsync(request: Schemas$x['RefreshRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$x['AuthResponse']>>;
    /**
     * Validate access token
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    validateTokenAsync(channel?: number, timeout?: number): Promise<ApiResponse<Schemas$x['ValidateTokenResponse']>>;
    /**
     * Logout and invalidate tokens
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    logoutEventAsync(request: Schemas$x['LogoutRequest'], channel?: number): Promise<void>;
    /**
     * Get active sessions for account
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getSessionsAsync(channel?: number, timeout?: number): Promise<ApiResponse<Schemas$x['SessionsResponse']>>;
    /**
     * Terminate specific session
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    terminateSessionEventAsync(request: Schemas$x['TerminateSessionRequest'], channel?: number): Promise<void>;
    /**
     * Request password reset
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    requestPasswordResetEventAsync(request: Schemas$x['PasswordResetRequest'], channel?: number): Promise<void>;
    /**
     * Confirm password reset with token
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    confirmPasswordResetEventAsync(request: Schemas$x['PasswordResetConfirmRequest'], channel?: number): Promise<void>;
    /**
     * List available authentication providers
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listProvidersAsync(channel?: number, timeout?: number): Promise<ApiResponse<Schemas$x['ProvidersResponse']>>;
}

type Schemas$w = components['schemas'];
/**
 * Typed proxy for Bundles API endpoints.
 */
declare class BundlesProxy {
    private readonly client;
    /**
     * Creates a new BundlesProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Create asset bundle from multiple assets
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createBundleAsync(request: Schemas$w['CreateBundleRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$w['CreateBundleResponse']>>;
    /**
     * Get bundle manifest and download URL
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getBundleAsync(request: Schemas$w['GetBundleRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$w['BundleWithDownloadUrl']>>;
    /**
     * Request upload URL for a pre-made bundle
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    requestBundleUploadAsync(request: Schemas$w['BundleUploadRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$w['UploadResponse']>>;
    /**
     * Create metabundle from source bundles
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createMetabundleAsync(request: Schemas$w['CreateMetabundleRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$w['CreateMetabundleResponse']>>;
    /**
     * Get async metabundle job status
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getJobStatusAsync(request: Schemas$w['GetJobStatusRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$w['GetJobStatusResponse']>>;
    /**
     * Cancel an async metabundle job
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    cancelJobAsync(request: Schemas$w['CancelJobRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$w['CancelJobResponse']>>;
    /**
     * Compute optimal bundles for requested assets
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    resolveBundlesAsync(request: Schemas$w['ResolveBundlesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$w['ResolveBundlesResponse']>>;
    /**
     * Find all bundles containing a specific asset
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    queryBundlesByAssetAsync(request: Schemas$w['QueryBundlesByAssetRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$w['QueryBundlesByAssetResponse']>>;
    /**
     * Update bundle metadata
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    updateBundleAsync(request: Schemas$w['UpdateBundleRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$w['UpdateBundleResponse']>>;
    /**
     * Soft-delete a bundle
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    deleteBundleAsync(request: Schemas$w['DeleteBundleRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$w['DeleteBundleResponse']>>;
    /**
     * Restore a soft-deleted bundle
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    restoreBundleAsync(request: Schemas$w['RestoreBundleRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$w['RestoreBundleResponse']>>;
    /**
     * Query bundles with advanced filters
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    queryBundlesAsync(request: Schemas$w['QueryBundlesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$w['QueryBundlesResponse']>>;
    /**
     * List version history for a bundle
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listBundleVersionsAsync(request: Schemas$w['ListBundleVersionsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$w['ListBundleVersionsResponse']>>;
}

type Schemas$v = components['schemas'];
/**
 * Typed proxy for Cache API endpoints.
 */
declare class CacheProxy {
    private readonly client;
    /**
     * Creates a new CacheProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get cached compiled behavior
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getCachedBehaviorAsync(request: Schemas$v['GetCachedBehaviorRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$v['CachedBehaviorResponse']>>;
    /**
     * Invalidate cached behavior
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    invalidateCachedBehaviorEventAsync(request: Schemas$v['InvalidateCacheRequest'], channel?: number): Promise<void>;
}

type Schemas$u = components['schemas'];
/**
 * Typed proxy for Character API endpoints.
 */
declare class CharacterProxy {
    private readonly client;
    /**
     * Creates a new CharacterProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get character by ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getCharacterAsync(request: Schemas$u['GetCharacterRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$u['CharacterResponse']>>;
    /**
     * List characters with filtering
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listCharactersAsync(request: Schemas$u['ListCharactersRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$u['CharacterListResponse']>>;
    /**
     * Get character with optional related data (personality, backstory, family)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getEnrichedCharacterAsync(request: Schemas$u['GetEnrichedCharacterRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$u['EnrichedCharacterResponse']>>;
    /**
     * Get compressed archive data for a character
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getCharacterArchiveAsync(request: Schemas$u['GetCharacterArchiveRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$u['CharacterArchive']>>;
    /**
     * Get all characters in a realm (primary query pattern)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getCharactersByRealmAsync(request: Schemas$u['GetCharactersByRealmRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$u['CharacterListResponse']>>;
}

type Schemas$t = components['schemas'];
/**
 * Typed proxy for CharacterEncounter API endpoints.
 */
declare class CharacterEncounterProxy {
    private readonly client;
    /**
     * Creates a new CharacterEncounterProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get encounter type by code
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getEncounterTypeAsync(request: Schemas$t['GetEncounterTypeRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$t['EncounterTypeResponse']>>;
    /**
     * List all encounter types
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listEncounterTypesAsync(request: Schemas$t['ListEncounterTypesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$t['EncounterTypeListResponse']>>;
    /**
     * Get character's encounters (paginated)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    queryByCharacterAsync(request: Schemas$t['QueryByCharacterRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$t['EncounterListResponse']>>;
    /**
     * Get encounters between two characters
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    queryBetweenAsync(request: Schemas$t['QueryBetweenRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$t['EncounterListResponse']>>;
    /**
     * Recent encounters at location
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    queryByLocationAsync(request: Schemas$t['QueryByLocationRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$t['EncounterListResponse']>>;
    /**
     * Quick check if two characters have met
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    hasMetAsync(request: Schemas$t['HasMetRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$t['HasMetResponse']>>;
    /**
     * Aggregate sentiment toward another character
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getSentimentAsync(request: Schemas$t['GetSentimentRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$t['SentimentResponse']>>;
    /**
     * Get character's view of encounter
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getPerspectiveAsync(request: Schemas$t['GetPerspectiveRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$t['PerspectiveResponse']>>;
}

type Schemas$s = components['schemas'];
/**
 * Typed proxy for CharacterHistory API endpoints.
 */
declare class CharacterHistoryProxy {
    private readonly client;
    /**
     * Creates a new CharacterHistoryProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get all historical events a character participated in
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getParticipationAsync(request: Schemas$s['GetParticipationRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$s['ParticipationListResponse']>>;
    /**
     * Get all characters who participated in a historical event
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getEventParticipantsAsync(request: Schemas$s['GetEventParticipantsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$s['ParticipationListResponse']>>;
    /**
     * Get machine-readable backstory elements for behavior system
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getBackstoryAsync(request: Schemas$s['GetBackstoryRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$s['BackstoryResponse']>>;
}

type Schemas$r = components['schemas'];
/**
 * Typed proxy for CharacterPersonality API endpoints.
 */
declare class CharacterPersonalityProxy {
    private readonly client;
    /**
     * Creates a new CharacterPersonalityProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get personality for a character
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getPersonalityAsync(request: Schemas$r['GetPersonalityRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$r['PersonalityResponse']>>;
    /**
     * Get combat preferences for a character
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getCombatPreferencesAsync(request: Schemas$r['GetCombatPreferencesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$r['CombatPreferencesResponse']>>;
}

type Schemas$q = components['schemas'];
/**
 * Typed proxy for ClientCapabilities API endpoints.
 */
declare class ClientCapabilitiesProxy {
    private readonly client;
    /**
     * Creates a new ClientCapabilitiesProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get client capability manifest (GUID → API mappings)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getClientCapabilitiesAsync(request: Schemas$q['GetClientCapabilitiesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$q['ClientCapabilitiesResponse']>>;
}

type Schemas$p = components['schemas'];
/**
 * Typed proxy for Compile API endpoints.
 */
declare class CompileProxy {
    private readonly client;
    /**
     * Creates a new CompileProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Compile ABML behavior definition
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    compileAbmlBehaviorAsync(request: Schemas$p['CompileBehaviorRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$p['CompileBehaviorResponse']>>;
}

type Schemas$o = components['schemas'];
/**
 * Typed proxy for Contract API endpoints.
 */
declare class ContractProxy {
    private readonly client;
    /**
     * Creates a new ContractProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get template by ID or code
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getContractTemplateAsync(request: Schemas$o['GetContractTemplateRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['ContractTemplateResponse']>>;
    /**
     * List templates with filters
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listContractTemplatesAsync(request: Schemas$o['ListContractTemplatesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['ListContractTemplatesResponse']>>;
    /**
     * Create contract instance from template
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createContractInstanceAsync(request: Schemas$o['CreateContractInstanceRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['ContractInstanceResponse']>>;
    /**
     * Propose contract to parties (starts consent flow)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    proposeContractInstanceAsync(request: Schemas$o['ProposeContractInstanceRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['ContractInstanceResponse']>>;
    /**
     * Party consents to contract
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    consentToContractAsync(request: Schemas$o['ConsentToContractRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['ContractInstanceResponse']>>;
    /**
     * Get instance by ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getContractInstanceAsync(request: Schemas$o['GetContractInstanceRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['ContractInstanceResponse']>>;
    /**
     * Query instances by party, template, status
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    queryContractInstancesAsync(request: Schemas$o['QueryContractInstancesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['QueryContractInstancesResponse']>>;
    /**
     * Request early termination
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    terminateContractInstanceAsync(request: Schemas$o['TerminateContractInstanceRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['ContractInstanceResponse']>>;
    /**
     * Get current status and milestone progress
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getContractInstanceStatusAsync(request: Schemas$o['GetContractInstanceStatusRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['ContractInstanceStatusResponse']>>;
    /**
     * External system reports milestone completed
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    completeMilestoneAsync(request: Schemas$o['CompleteMilestoneRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['MilestoneResponse']>>;
    /**
     * External system reports milestone failed
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    failMilestoneAsync(request: Schemas$o['FailMilestoneRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['MilestoneResponse']>>;
    /**
     * Get milestone details and status
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getMilestoneAsync(request: Schemas$o['GetMilestoneRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['MilestoneResponse']>>;
    /**
     * Report a contract breach
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    reportBreachAsync(request: Schemas$o['ReportBreachRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['BreachResponse']>>;
    /**
     * Mark breach as cured (system/admin action)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    cureBreachAsync(request: Schemas$o['CureBreachRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['BreachResponse']>>;
    /**
     * Get breach details
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getBreachAsync(request: Schemas$o['GetBreachRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['BreachResponse']>>;
    /**
     * Update game metadata on instance
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    updateContractMetadataAsync(request: Schemas$o['UpdateContractMetadataRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['ContractMetadataResponse']>>;
    /**
     * Get game metadata
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getContractMetadataAsync(request: Schemas$o['GetContractMetadataRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['ContractMetadataResponse']>>;
    /**
     * Check if entity can take action given contracts
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    checkContractConstraintAsync(request: Schemas$o['CheckConstraintRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['CheckConstraintResponse']>>;
    /**
     * Query active contracts for entity
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    queryActiveContractsAsync(request: Schemas$o['QueryActiveContractsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['QueryActiveContractsResponse']>>;
    /**
     * Lock contract under guardian custody
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    lockContractAsync(request: Schemas$o['LockContractRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['LockContractResponse']>>;
    /**
     * Unlock contract from guardian custody
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    unlockContractAsync(request: Schemas$o['UnlockContractRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['UnlockContractResponse']>>;
    /**
     * Transfer party role to new entity
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    transferContractPartyAsync(request: Schemas$o['TransferContractPartyRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['TransferContractPartyResponse']>>;
    /**
     * List all registered clause types
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listClauseTypesAsync(request: Schemas$o['ListClauseTypesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['ListClauseTypesResponse']>>;
    /**
     * Set template values on contract instance
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    setContractTemplateValuesAsync(request: Schemas$o['SetTemplateValuesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['SetTemplateValuesResponse']>>;
    /**
     * Check if asset requirement clauses are satisfied
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    checkAssetRequirementsAsync(request: Schemas$o['CheckAssetRequirementsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['CheckAssetRequirementsResponse']>>;
    /**
     * Execute all contract clauses (idempotent)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    executeContractAsync(request: Schemas$o['ExecuteContractRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$o['ExecuteContractResponse']>>;
}

type Schemas$n = components['schemas'];
/**
 * Typed proxy for Currency API endpoints.
 */
declare class CurrencyProxy {
    private readonly client;
    /**
     * Creates a new CurrencyProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get currency definition by ID or code
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getCurrencyDefinitionAsync(request: Schemas$n['GetCurrencyDefinitionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['CurrencyDefinitionResponse']>>;
    /**
     * List currency definitions with filters
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listCurrencyDefinitionsAsync(request: Schemas$n['ListCurrencyDefinitionsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['ListCurrencyDefinitionsResponse']>>;
    /**
     * Create a new wallet for an owner
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createWalletAsync(request: Schemas$n['CreateWalletRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['WalletResponse']>>;
    /**
     * Get wallet by ID or owner
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getWalletAsync(request: Schemas$n['GetWalletRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['WalletWithBalancesResponse']>>;
    /**
     * Get existing wallet or create if not exists
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getOrCreateWalletAsync(request: Schemas$n['GetOrCreateWalletRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['GetOrCreateWalletResponse']>>;
    /**
     * Get balance for a specific currency in a wallet
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getBalanceAsync(request: Schemas$n['GetBalanceRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['GetBalanceResponse']>>;
    /**
     * Get multiple balances in one call
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    batchGetBalancesAsync(request: Schemas$n['BatchGetBalancesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['BatchGetBalancesResponse']>>;
    /**
     * Credit currency to a wallet (faucet operation)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    creditCurrencyAsync(request: Schemas$n['CreditCurrencyRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['CreditCurrencyResponse']>>;
    /**
     * Debit currency from a wallet (sink operation)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    debitCurrencyAsync(request: Schemas$n['DebitCurrencyRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['DebitCurrencyResponse']>>;
    /**
     * Transfer currency between wallets
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    transferCurrencyAsync(request: Schemas$n['TransferCurrencyRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['TransferCurrencyResponse']>>;
    /**
     * Credit multiple wallets in one call
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    batchCreditCurrencyAsync(request: Schemas$n['BatchCreditRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['BatchCreditResponse']>>;
    /**
     * Calculate conversion without executing
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    calculateConversionAsync(request: Schemas$n['CalculateConversionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['CalculateConversionResponse']>>;
    /**
     * Execute currency conversion in a wallet
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    executeConversionAsync(request: Schemas$n['ExecuteConversionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['ExecuteConversionResponse']>>;
    /**
     * Get exchange rate between two currencies
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getExchangeRateAsync(request: Schemas$n['GetExchangeRateRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['GetExchangeRateResponse']>>;
    /**
     * Get a transaction by ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getTransactionAsync(request: Schemas$n['GetTransactionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['TransactionResponse']>>;
    /**
     * Get paginated transaction history for a wallet
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getTransactionHistoryAsync(request: Schemas$n['GetTransactionHistoryRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['GetTransactionHistoryResponse']>>;
    /**
     * Get transactions by reference type and ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getTransactionsByReferenceAsync(request: Schemas$n['GetTransactionsByReferenceRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['GetTransactionsByReferenceResponse']>>;
    /**
     * Get global supply statistics for a currency
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getGlobalSupplyAsync(request: Schemas$n['GetGlobalSupplyRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['GetGlobalSupplyResponse']>>;
    /**
     * Debit wallet for escrow deposit
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    escrowDepositAsync(request: Schemas$n['EscrowDepositRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['EscrowDepositResponse']>>;
    /**
     * Credit recipient on escrow completion
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    escrowReleaseAsync(request: Schemas$n['EscrowReleaseRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['EscrowReleaseResponse']>>;
    /**
     * Credit depositor on escrow refund
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    escrowRefundAsync(request: Schemas$n['EscrowRefundRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['EscrowRefundResponse']>>;
    /**
     * Create an authorization hold (reserve funds)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createHoldAsync(request: Schemas$n['CreateHoldRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['HoldResponse']>>;
    /**
     * Capture held funds (debit final amount)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    captureHoldAsync(request: Schemas$n['CaptureHoldRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['CaptureHoldResponse']>>;
    /**
     * Release held funds (make available again)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    releaseHoldAsync(request: Schemas$n['ReleaseHoldRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['HoldResponse']>>;
    /**
     * Get hold status and details
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getHoldAsync(request: Schemas$n['GetHoldRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$n['HoldResponse']>>;
}

type Schemas$m = components['schemas'];
/**
 * Typed proxy for Documentation API endpoints.
 */
declare class DocumentationProxy {
    private readonly client;
    /**
     * Creates a new DocumentationProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Natural language documentation search
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    queryDocumentationAsync(request: Schemas$m['QueryDocumentationRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$m['QueryDocumentationResponse']>>;
    /**
     * Get specific document by ID or slug
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getDocumentAsync(request: Schemas$m['GetDocumentRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$m['GetDocumentResponse']>>;
    /**
     * Full-text keyword search
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    searchDocumentationAsync(request: Schemas$m['SearchDocumentationRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$m['SearchDocumentationResponse']>>;
    /**
     * List documents by category
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listDocumentsAsync(request: Schemas$m['ListDocumentsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$m['ListDocumentsResponse']>>;
    /**
     * Get related topics and follow-up suggestions
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    suggestRelatedTopicsAsync(request: Schemas$m['SuggestRelatedRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$m['SuggestRelatedResponse']>>;
    /**
     * Bind a git repository to a documentation namespace
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    bindRepositoryAsync(request: Schemas$m['BindRepositoryRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$m['BindRepositoryResponse']>>;
    /**
     * Manually trigger repository sync
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    syncRepositoryAsync(request: Schemas$m['SyncRepositoryRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$m['SyncRepositoryResponse']>>;
    /**
     * Get repository binding status
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getRepositoryStatusAsync(request: Schemas$m['RepositoryStatusRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$m['RepositoryStatusResponse']>>;
    /**
     * List all repository bindings
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listRepositoryBindingsAsync(request: Schemas$m['ListRepositoryBindingsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$m['ListRepositoryBindingsResponse']>>;
    /**
     * Update repository binding configuration
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    updateRepositoryBindingAsync(request: Schemas$m['UpdateRepositoryBindingRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$m['UpdateRepositoryBindingResponse']>>;
    /**
     * Create documentation archive
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createDocumentationArchiveAsync(request: Schemas$m['CreateArchiveRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$m['CreateArchiveResponse']>>;
    /**
     * List documentation archives
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listDocumentationArchivesAsync(request: Schemas$m['ListArchivesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$m['ListArchivesResponse']>>;
}

type Schemas$l = components['schemas'];
/**
 * Typed proxy for Escrow API endpoints.
 */
declare class EscrowProxy {
    private readonly client;
    /**
     * Creates a new EscrowProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Create a new escrow agreement
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createEscrowAsync(request: Schemas$l['CreateEscrowRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$l['CreateEscrowResponse']>>;
    /**
     * Get escrow details
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getEscrowAsync(request: Schemas$l['GetEscrowRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$l['GetEscrowResponse']>>;
    /**
     * List escrows for a party
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listEscrowsAsync(request: Schemas$l['ListEscrowsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$l['ListEscrowsResponse']>>;
    /**
     * Deposit assets into escrow
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    depositAsync(request: Schemas$l['DepositRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$l['DepositResponse']>>;
    /**
     * Validate a deposit without executing
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    validateDepositAsync(request: Schemas$l['ValidateDepositRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$l['ValidateDepositResponse']>>;
    /**
     * Get deposit status for a party
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getDepositStatusAsync(request: Schemas$l['GetDepositStatusRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$l['GetDepositStatusResponse']>>;
    /**
     * Record party consent
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    recordConsentAsync(request: Schemas$l['ConsentRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$l['ConsentResponse']>>;
    /**
     * Get consent status for escrow
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getConsentStatusAsync(request: Schemas$l['GetConsentStatusRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$l['GetConsentStatusResponse']>>;
    /**
     * Trigger release
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    releaseAsync(request: Schemas$l['ReleaseRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$l['ReleaseResponse']>>;
    /**
     * Trigger refund
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    refundAsync(request: Schemas$l['RefundRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$l['RefundResponse']>>;
    /**
     * Cancel escrow before fully funded
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    cancelAsync(request: Schemas$l['CancelRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$l['CancelResponse']>>;
    /**
     * Raise a dispute on funded escrow
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    disputeAsync(request: Schemas$l['DisputeRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$l['DisputeResponse']>>;
    /**
     * Arbiter resolves disputed escrow
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    resolveAsync(request: Schemas$l['ResolveRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$l['ResolveResponse']>>;
    /**
     * Verify condition for conditional escrow
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    verifyConditionAsync(request: Schemas$l['VerifyConditionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$l['VerifyConditionResponse']>>;
    /**
     * Re-affirm after validation failure
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    reaffirmAsync(request: Schemas$l['ReaffirmRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$l['ReaffirmResponse']>>;
}

type Schemas$k = components['schemas'];
/**
 * Typed proxy for GameService API endpoints.
 */
declare class GameServiceProxy {
    private readonly client;
    /**
     * Creates a new GameServiceProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * List all registered game services
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listServicesAsync(request: Schemas$k['ListServicesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$k['ListServicesResponse']>>;
    /**
     * Get service by ID or stub name
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getServiceAsync(request: Schemas$k['GetServiceRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$k['ServiceInfo']>>;
}

type Schemas$j = components['schemas'];
/**
 * Typed proxy for Goap API endpoints.
 */
declare class GoapProxy {
    private readonly client;
    /**
     * Creates a new GoapProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Generate GOAP plan
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    generateGoapPlanAsync(request: Schemas$j['GoapPlanRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$j['GoapPlanResponse']>>;
    /**
     * Validate existing GOAP plan
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    validateGoapPlanAsync(request: Schemas$j['ValidateGoapPlanRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$j['ValidateGoapPlanResponse']>>;
}

type Schemas$i = components['schemas'];
/**
 * Typed proxy for Inventory API endpoints.
 */
declare class InventoryProxy {
    private readonly client;
    /**
     * Creates a new InventoryProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Create a new container
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createContainerAsync(request: Schemas$i['CreateContainerRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$i['ContainerResponse']>>;
    /**
     * Get container with contents
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getContainerAsync(request: Schemas$i['GetContainerRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$i['ContainerWithContentsResponse']>>;
    /**
     * Get container or create if not exists
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getOrCreateContainerAsync(request: Schemas$i['GetOrCreateContainerRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$i['ContainerResponse']>>;
    /**
     * List containers for owner
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listContainersAsync(request: Schemas$i['ListContainersRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$i['ListContainersResponse']>>;
    /**
     * Update container properties
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    updateContainerAsync(request: Schemas$i['UpdateContainerRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$i['ContainerResponse']>>;
    /**
     * Add item to container
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    addItemToContainerAsync(request: Schemas$i['AddItemRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$i['AddItemResponse']>>;
    /**
     * Remove item from container
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    removeItemFromContainerAsync(request: Schemas$i['RemoveItemRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$i['RemoveItemResponse']>>;
    /**
     * Move item to different slot or container
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    moveItemAsync(request: Schemas$i['MoveItemRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$i['MoveItemResponse']>>;
    /**
     * Transfer item to different owner
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    transferItemAsync(request: Schemas$i['TransferItemRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$i['TransferItemResponse']>>;
    /**
     * Split stack into two
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    splitStackAsync(request: Schemas$i['SplitStackRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$i['SplitStackResponse']>>;
    /**
     * Merge two stacks
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    mergeStacksAsync(request: Schemas$i['MergeStacksRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$i['MergeStacksResponse']>>;
    /**
     * Find items across containers
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    queryItemsAsync(request: Schemas$i['QueryItemsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$i['QueryItemsResponse']>>;
    /**
     * Count items of a template
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    countItemsAsync(request: Schemas$i['CountItemsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$i['CountItemsResponse']>>;
    /**
     * Check if entity has required items
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    hasItemsAsync(request: Schemas$i['HasItemsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$i['HasItemsResponse']>>;
    /**
     * Find where item would fit
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    findSpaceAsync(request: Schemas$i['FindSpaceRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$i['FindSpaceResponse']>>;
}

type Schemas$h = components['schemas'];
/**
 * Typed proxy for Item API endpoints.
 */
declare class ItemProxy {
    private readonly client;
    /**
     * Creates a new ItemProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Create a new item template
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createItemTemplateAsync(request: Schemas$h['CreateItemTemplateRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$h['ItemTemplateResponse']>>;
    /**
     * Get item template by ID or code
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getItemTemplateAsync(request: Schemas$h['GetItemTemplateRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$h['ItemTemplateResponse']>>;
    /**
     * List item templates with filters
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listItemTemplatesAsync(request: Schemas$h['ListItemTemplatesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$h['ListItemTemplatesResponse']>>;
    /**
     * Update mutable fields of an item template
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    updateItemTemplateAsync(request: Schemas$h['UpdateItemTemplateRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$h['ItemTemplateResponse']>>;
    /**
     * Create a new item instance
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createItemInstanceAsync(request: Schemas$h['CreateItemInstanceRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$h['ItemInstanceResponse']>>;
    /**
     * Get item instance by ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getItemInstanceAsync(request: Schemas$h['GetItemInstanceRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$h['ItemInstanceResponse']>>;
    /**
     * Modify item instance state
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    modifyItemInstanceAsync(request: Schemas$h['ModifyItemInstanceRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$h['ItemInstanceResponse']>>;
    /**
     * Bind item to character
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    bindItemInstanceAsync(request: Schemas$h['BindItemInstanceRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$h['ItemInstanceResponse']>>;
    /**
     * Destroy item instance
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    destroyItemInstanceAsync(request: Schemas$h['DestroyItemInstanceRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$h['DestroyItemInstanceResponse']>>;
    /**
     * List items in a container
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listItemsByContainerAsync(request: Schemas$h['ListItemsByContainerRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$h['ListItemsResponse']>>;
    /**
     * Get multiple item instances by ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    batchGetItemInstancesAsync(request: Schemas$h['BatchGetItemInstancesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$h['BatchGetItemInstancesResponse']>>;
}

type Schemas$g = components['schemas'];
/**
 * Typed proxy for Leaderboard API endpoints.
 */
declare class LeaderboardProxy {
    private readonly client;
    /**
     * Creates a new LeaderboardProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Create a new leaderboard definition
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createLeaderboardDefinitionAsync(request: Schemas$g['CreateLeaderboardDefinitionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$g['LeaderboardDefinitionResponse']>>;
    /**
     * Update leaderboard definition
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    updateLeaderboardDefinitionAsync(request: Schemas$g['UpdateLeaderboardDefinitionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$g['LeaderboardDefinitionResponse']>>;
    /**
     * Delete leaderboard definition
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    deleteLeaderboardDefinitionEventAsync(request: Schemas$g['DeleteLeaderboardDefinitionRequest'], channel?: number): Promise<void>;
    /**
     * Get entity's rank
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getEntityRankAsync(request: Schemas$g['GetEntityRankRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$g['EntityRankResponse']>>;
    /**
     * Get top entries
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getTopRanksAsync(request: Schemas$g['GetTopRanksRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$g['LeaderboardEntriesResponse']>>;
    /**
     * Get entries around entity
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getRanksAroundAsync(request: Schemas$g['GetRanksAroundRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$g['LeaderboardEntriesResponse']>>;
    /**
     * Get current season info
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getSeasonAsync(request: Schemas$g['GetSeasonRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$g['SeasonResponse']>>;
}

type Schemas$f = components['schemas'];
/**
 * Typed proxy for Location API endpoints.
 */
declare class LocationProxy {
    private readonly client;
    /**
     * Creates a new LocationProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get location by ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getLocationAsync(request: Schemas$f['GetLocationRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$f['LocationResponse']>>;
    /**
     * Get location by code and realm
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getLocationByCodeAsync(request: Schemas$f['GetLocationByCodeRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$f['LocationResponse']>>;
    /**
     * List locations with filtering
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listLocationsAsync(request: Schemas$f['ListLocationsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$f['LocationListResponse']>>;
    /**
     * List all locations in a realm (primary query pattern)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listLocationsByRealmAsync(request: Schemas$f['ListLocationsByRealmRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$f['LocationListResponse']>>;
    /**
     * Get child locations for a parent location
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listLocationsByParentAsync(request: Schemas$f['ListLocationsByParentRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$f['LocationListResponse']>>;
    /**
     * Get root locations in a realm
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listRootLocationsAsync(request: Schemas$f['ListRootLocationsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$f['LocationListResponse']>>;
    /**
     * Get all ancestors of a location
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getLocationAncestorsAsync(request: Schemas$f['GetLocationAncestorsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$f['LocationListResponse']>>;
    /**
     * Get all descendants of a location
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getLocationDescendantsAsync(request: Schemas$f['GetLocationDescendantsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$f['LocationListResponse']>>;
    /**
     * Check if location exists and is active
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    locationExistsAsync(request: Schemas$f['LocationExistsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$f['LocationExistsResponse']>>;
}

type Schemas$e = components['schemas'];
/**
 * Typed proxy for Mapping API endpoints.
 */
declare class MappingProxy {
    private readonly client;
    /**
     * Creates a new MappingProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Request full snapshot for cold start
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    requestSnapshotAsync(request: Schemas$e['RequestSnapshotRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$e['RequestSnapshotResponse']>>;
    /**
     * Query map data at a specific point
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    queryPointAsync(request: Schemas$e['QueryPointRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$e['QueryPointResponse']>>;
    /**
     * Query map data within bounds
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    queryBoundsAsync(request: Schemas$e['QueryBoundsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$e['QueryBoundsResponse']>>;
    /**
     * Find all objects of a type in region
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    queryObjectsByTypeAsync(request: Schemas$e['QueryObjectsByTypeRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$e['QueryObjectsByTypeResponse']>>;
    /**
     * Find locations that afford a specific action or scene type
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    queryAffordanceAsync(request: Schemas$e['AffordanceQueryRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$e['AffordanceQueryResponse']>>;
    /**
     * Acquire exclusive edit lock for design-time editing
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    checkoutForAuthoringAsync(request: Schemas$e['AuthoringCheckoutRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$e['AuthoringCheckoutResponse']>>;
    /**
     * Commit design-time changes
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    commitAuthoringAsync(request: Schemas$e['AuthoringCommitRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$e['AuthoringCommitResponse']>>;
    /**
     * Release authoring checkout without committing
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    releaseAuthoringAsync(request: Schemas$e['AuthoringReleaseRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$e['AuthoringReleaseResponse']>>;
    /**
     * Create a map definition template
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createDefinitionAsync(request: Schemas$e['CreateDefinitionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$e['MapDefinition']>>;
    /**
     * Get a map definition by ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getDefinitionAsync(request: Schemas$e['GetDefinitionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$e['MapDefinition']>>;
    /**
     * List map definitions with optional filters
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listDefinitionsAsync(request: Schemas$e['ListDefinitionsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$e['ListDefinitionsResponse']>>;
    /**
     * Update a map definition
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    updateDefinitionAsync(request: Schemas$e['UpdateDefinitionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$e['MapDefinition']>>;
}

type Schemas$d = components['schemas'];
/**
 * Typed proxy for Matchmaking API endpoints.
 */
declare class MatchmakingProxy {
    private readonly client;
    /**
     * Creates a new MatchmakingProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * List available matchmaking queues
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listQueuesAsync(request: Schemas$d['ListQueuesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$d['ListQueuesResponse']>>;
    /**
     * Get queue details
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getQueueAsync(request: Schemas$d['GetQueueRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$d['QueueResponse']>>;
    /**
     * Join matchmaking queue
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    joinMatchmakingAsync(request: Schemas$d['JoinMatchmakingRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$d['JoinMatchmakingResponse']>>;
    /**
     * Leave matchmaking queue
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    leaveMatchmakingEventAsync(request: Schemas$d['LeaveMatchmakingRequest'], channel?: number): Promise<void>;
    /**
     * Get matchmaking status
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getMatchmakingStatusAsync(request: Schemas$d['GetMatchmakingStatusRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$d['MatchmakingStatusResponse']>>;
    /**
     * Accept a formed match
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    acceptMatchAsync(request: Schemas$d['AcceptMatchRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$d['AcceptMatchResponse']>>;
    /**
     * Decline a formed match
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    declineMatchEventAsync(request: Schemas$d['DeclineMatchRequest'], channel?: number): Promise<void>;
    /**
     * Get queue statistics
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getMatchmakingStatsAsync(request: Schemas$d['GetMatchmakingStatsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$d['MatchmakingStatsResponse']>>;
}

type Schemas$c = components['schemas'];
/**
 * Typed proxy for Music API endpoints.
 */
declare class MusicProxy {
    private readonly client;
    /**
     * Creates a new MusicProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Generate composition from style and constraints
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    generateCompositionAsync(request: Schemas$c['GenerateCompositionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$c['GenerateCompositionResponse']>>;
    /**
     * Validate MIDI-JSON structure
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    validateMidiJsonAsync(request: Schemas$c['ValidateMidiJsonRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$c['ValidateMidiJsonResponse']>>;
    /**
     * Get style definition
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getStyleAsync(request: Schemas$c['GetStyleRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$c['StyleDefinitionResponse']>>;
    /**
     * List available styles
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listStylesAsync(request: Schemas$c['ListStylesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$c['ListStylesResponse']>>;
    /**
     * Generate chord progression
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    generateProgressionAsync(request: Schemas$c['GenerateProgressionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$c['GenerateProgressionResponse']>>;
    /**
     * Generate melody over harmony
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    generateMelodyAsync(request: Schemas$c['GenerateMelodyRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$c['GenerateMelodyResponse']>>;
    /**
     * Apply voice leading to chords
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    applyVoiceLeadingAsync(request: Schemas$c['VoiceLeadRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$c['VoiceLeadResponse']>>;
}

type Schemas$b = components['schemas'];
/**
 * Typed proxy for Realm API endpoints.
 */
declare class RealmProxy {
    private readonly client;
    /**
     * Creates a new RealmProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get realm by ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getRealmAsync(request: Schemas$b['GetRealmRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$b['RealmResponse']>>;
    /**
     * Get realm by code
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getRealmByCodeAsync(request: Schemas$b['GetRealmByCodeRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$b['RealmResponse']>>;
    /**
     * List all realms
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listRealmsAsync(request: Schemas$b['ListRealmsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$b['RealmListResponse']>>;
    /**
     * Check if realm exists and is active
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    realmExistsAsync(request: Schemas$b['RealmExistsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$b['RealmExistsResponse']>>;
}

type Schemas$a = components['schemas'];
/**
 * Typed proxy for RealmHistory API endpoints.
 */
declare class RealmHistoryProxy {
    private readonly client;
    /**
     * Creates a new RealmHistoryProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get all historical events a realm participated in
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getRealmParticipationAsync(request: Schemas$a['GetRealmParticipationRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$a['RealmParticipationListResponse']>>;
    /**
     * Get all realms that participated in a historical event
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getRealmEventParticipantsAsync(request: Schemas$a['GetRealmEventParticipantsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$a['RealmParticipationListResponse']>>;
    /**
     * Get machine-readable lore elements for behavior system
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getRealmLoreAsync(request: Schemas$a['GetRealmLoreRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$a['RealmLoreResponse']>>;
}

type Schemas$9 = components['schemas'];
/**
 * Typed proxy for Relationship API endpoints.
 */
declare class RelationshipProxy {
    private readonly client;
    /**
     * Creates a new RelationshipProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get a relationship by ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getRelationshipAsync(request: Schemas$9['GetRelationshipRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$9['RelationshipResponse']>>;
    /**
     * List all relationships for an entity
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listRelationshipsByEntityAsync(request: Schemas$9['ListRelationshipsByEntityRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$9['RelationshipListResponse']>>;
    /**
     * Get all relationships between two specific entities
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getRelationshipsBetweenAsync(request: Schemas$9['GetRelationshipsBetweenRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$9['RelationshipListResponse']>>;
    /**
     * List all relationships of a specific type
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listRelationshipsByTypeAsync(request: Schemas$9['ListRelationshipsByTypeRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$9['RelationshipListResponse']>>;
}

type Schemas$8 = components['schemas'];
/**
 * Typed proxy for RelationshipType API endpoints.
 */
declare class RelationshipTypeProxy {
    private readonly client;
    /**
     * Creates a new RelationshipTypeProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get relationship type by ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getRelationshipTypeAsync(request: Schemas$8['GetRelationshipTypeRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$8['RelationshipTypeResponse']>>;
    /**
     * Get relationship type by code
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getRelationshipTypeByCodeAsync(request: Schemas$8['GetRelationshipTypeByCodeRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$8['RelationshipTypeResponse']>>;
    /**
     * List all relationship types
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listRelationshipTypesAsync(request: Schemas$8['ListRelationshipTypesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$8['RelationshipTypeListResponse']>>;
    /**
     * Get child types for a parent type
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getChildRelationshipTypesAsync(request: Schemas$8['GetChildRelationshipTypesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$8['RelationshipTypeListResponse']>>;
    /**
     * Check if type matches ancestor in hierarchy
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    matchesHierarchyAsync(request: Schemas$8['MatchesHierarchyRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$8['MatchesHierarchyResponse']>>;
    /**
     * Get all ancestors of a relationship type
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getAncestorsAsync(request: Schemas$8['GetAncestorsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$8['RelationshipTypeListResponse']>>;
}

type Schemas$7 = components['schemas'];
/**
 * Typed proxy for SaveLoad API endpoints.
 */
declare class SaveLoadProxy {
    private readonly client;
    /**
     * Creates a new SaveLoadProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Create or configure a save slot
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createSlotAsync(request: Schemas$7['CreateSlotRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['SlotResponse']>>;
    /**
     * Get slot metadata
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getSlotAsync(request: Schemas$7['GetSlotRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['SlotResponse']>>;
    /**
     * List slots for owner
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listSlotsAsync(request: Schemas$7['ListSlotsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['ListSlotsResponse']>>;
    /**
     * Delete slot and all versions
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    deleteSlotAsync(request: Schemas$7['DeleteSlotRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['DeleteSlotResponse']>>;
    /**
     * Rename a save slot
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    renameSlotAsync(request: Schemas$7['RenameSlotRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['SlotResponse']>>;
    /**
     * Save data to slot
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    saveAsync(request: Schemas$7['SaveRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['SaveResponse']>>;
    /**
     * Load data from slot
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    loadAsync(request: Schemas$7['LoadRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['LoadResponse']>>;
    /**
     * Save incremental changes from base version
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    saveDeltaAsync(request: Schemas$7['SaveDeltaRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['SaveDeltaResponse']>>;
    /**
     * Load save reconstructing from delta chain
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    loadWithDeltasAsync(request: Schemas$7['LoadRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['LoadResponse']>>;
    /**
     * Collapse delta chain into full snapshot
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    collapseDeltasAsync(request: Schemas$7['CollapseDeltasRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['SaveResponse']>>;
    /**
     * List versions in slot
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listVersionsAsync(request: Schemas$7['ListVersionsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['ListVersionsResponse']>>;
    /**
     * Pin a version as checkpoint
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    pinVersionAsync(request: Schemas$7['PinVersionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['VersionResponse']>>;
    /**
     * Unpin a version
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    unpinVersionAsync(request: Schemas$7['UnpinVersionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['VersionResponse']>>;
    /**
     * Delete specific version
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    deleteVersionAsync(request: Schemas$7['DeleteVersionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['DeleteVersionResponse']>>;
    /**
     * Query saves with filters
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    querySavesAsync(request: Schemas$7['QuerySavesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['QuerySavesResponse']>>;
    /**
     * Copy save to different slot or owner
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    copySaveAsync(request: Schemas$7['CopySaveRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['SaveResponse']>>;
    /**
     * Export saves for backup/portability
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    exportSavesAsync(request: Schemas$7['ExportSavesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['ExportSavesResponse']>>;
    /**
     * Verify save data integrity
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    verifyIntegrityAsync(request: Schemas$7['VerifyIntegrityRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['VerifyIntegrityResponse']>>;
    /**
     * Promote old version to latest
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    promoteVersionAsync(request: Schemas$7['PromoteVersionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['SaveResponse']>>;
    /**
     * Migrate save to new schema version
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    migrateSaveAsync(request: Schemas$7['MigrateSaveRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['MigrateSaveResponse']>>;
    /**
     * Register a save data schema
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    registerSchemaAsync(request: Schemas$7['RegisterSchemaRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['SchemaResponse']>>;
    /**
     * List registered schemas
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listSchemasAsync(request: Schemas$7['ListSchemasRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$7['ListSchemasResponse']>>;
}

type Schemas$6 = components['schemas'];
/**
 * Typed proxy for Scene API endpoints.
 */
declare class SceneProxy {
    private readonly client;
    /**
     * Creates a new SceneProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Create a new scene document
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createSceneAsync(request: Schemas$6['CreateSceneRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['SceneResponse']>>;
    /**
     * Retrieve a scene by ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getSceneAsync(request: Schemas$6['GetSceneRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['GetSceneResponse']>>;
    /**
     * List scenes with filtering
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listScenesAsync(request: Schemas$6['ListScenesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['ListScenesResponse']>>;
    /**
     * Update a scene document
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    updateSceneAsync(request: Schemas$6['UpdateSceneRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['SceneResponse']>>;
    /**
     * Delete a scene
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    deleteSceneAsync(request: Schemas$6['DeleteSceneRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['DeleteSceneResponse']>>;
    /**
     * Validate a scene structure
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    validateSceneAsync(request: Schemas$6['ValidateSceneRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['ValidationResult']>>;
    /**
     * Lock a scene for editing
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    checkoutSceneAsync(request: Schemas$6['CheckoutRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['CheckoutResponse']>>;
    /**
     * Save changes and release lock
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    commitSceneAsync(request: Schemas$6['CommitRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['CommitResponse']>>;
    /**
     * Release lock without saving changes
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    discardCheckoutAsync(request: Schemas$6['DiscardRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['DiscardResponse']>>;
    /**
     * Extend checkout lock TTL
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    heartbeatCheckoutAsync(request: Schemas$6['HeartbeatRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['HeartbeatResponse']>>;
    /**
     * Get version history for a scene
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getSceneHistoryAsync(request: Schemas$6['HistoryRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['HistoryResponse']>>;
    /**
     * Get validation rules for a gameId+sceneType
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getValidationRulesAsync(request: Schemas$6['GetValidationRulesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['GetValidationRulesResponse']>>;
    /**
     * Full-text search across scenes
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    searchScenesAsync(request: Schemas$6['SearchScenesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['SearchScenesResponse']>>;
    /**
     * Find scenes that reference a given scene
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    findReferencesAsync(request: Schemas$6['FindReferencesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['FindReferencesResponse']>>;
    /**
     * Find scenes using a specific asset
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    findAssetUsageAsync(request: Schemas$6['FindAssetUsageRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['FindAssetUsageResponse']>>;
    /**
     * Duplicate a scene with a new ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    duplicateSceneAsync(request: Schemas$6['DuplicateSceneRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$6['SceneResponse']>>;
}

type Schemas$5 = components['schemas'];
/**
 * Typed proxy for Sessions API endpoints.
 */
declare class SessionsProxy {
    private readonly client;
    /**
     * Creates a new SessionsProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get game session details
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getGameSessionAsync(request: Schemas$5['GetGameSessionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$5['GameSessionResponse']>>;
    /**
     * Leave a game session
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    leaveGameSessionEventAsync(request: Schemas$5['LeaveGameSessionRequest'], channel?: number): Promise<void>;
    /**
     * Send chat message to game session
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    sendChatMessageEventAsync(request: Schemas$5['ChatMessageRequest'], channel?: number): Promise<void>;
    /**
     * Perform game action (enhanced permissions after joining)
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    performGameActionAsync(request: Schemas$5['GameActionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$5['GameActionResponse']>>;
    /**
     * Leave a specific game session by ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    leaveGameSessionByIdEventAsync(request: Schemas$5['LeaveGameSessionByIdRequest'], channel?: number): Promise<void>;
}

type Schemas$4 = components['schemas'];
/**
 * Typed proxy for Species API endpoints.
 */
declare class SpeciesProxy {
    private readonly client;
    /**
     * Creates a new SpeciesProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get species by ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getSpeciesAsync(request: Schemas$4['GetSpeciesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$4['SpeciesResponse']>>;
    /**
     * Get species by code
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getSpeciesByCodeAsync(request: Schemas$4['GetSpeciesByCodeRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$4['SpeciesResponse']>>;
    /**
     * List all species
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listSpeciesAsync(request: Schemas$4['ListSpeciesRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$4['SpeciesListResponse']>>;
    /**
     * List species available in a realm
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    listSpeciesByRealmAsync(request: Schemas$4['ListSpeciesByRealmRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$4['SpeciesListResponse']>>;
}

type Schemas$3 = components['schemas'];
/**
 * Typed proxy for Subscription API endpoints.
 */
declare class SubscriptionProxy {
    private readonly client;
    /**
     * Creates a new SubscriptionProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get subscriptions for an account
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getAccountSubscriptionsAsync(request: Schemas$3['GetAccountSubscriptionsRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$3['SubscriptionListResponse']>>;
    /**
     * Get a specific subscription by ID
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getSubscriptionAsync(request: Schemas$3['GetSubscriptionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$3['SubscriptionInfo']>>;
    /**
     * Cancel a subscription
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    cancelSubscriptionAsync(request: Schemas$3['CancelSubscriptionRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$3['SubscriptionInfo']>>;
}

type Schemas$2 = components['schemas'];
/**
 * Typed proxy for Validate API endpoints.
 */
declare class ValidateProxy {
    private readonly client;
    /**
     * Creates a new ValidateProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Validate ABML definition
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    validateAbmlAsync(request: Schemas$2['ValidateAbmlRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas$2['ValidateAbmlResponse']>>;
}

type Schemas$1 = components['schemas'];
/**
 * Typed proxy for Voice API endpoints.
 */
declare class VoiceProxy {
    private readonly client;
    /**
     * Creates a new VoiceProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Send SDP answer to complete WebRTC handshake
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    answerPeerEventAsync(request: Schemas$1['AnswerPeerRequest'], channel?: number): Promise<void>;
}

type Schemas = components['schemas'];
/**
 * Typed proxy for Website API endpoints.
 */
declare class WebsiteProxy {
    private readonly client;
    /**
     * Creates a new WebsiteProxy instance.
     * @param client - The underlying Bannou client.
     */
    constructor(client: IBannouClient);
    /**
     * Get website status and version
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getStatusAsync(channel?: number, timeout?: number): Promise<ApiResponse<Schemas['StatusResponse']>>;
    /**
     * Get dynamic page content from CMS
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getPageContentAsync(channel?: number, timeout?: number): Promise<ApiResponse<Schemas['PageContent']>>;
    /**
     * Get latest news and announcements
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getNewsAsync(channel?: number, timeout?: number): Promise<ApiResponse<Schemas['NewsResponse']>>;
    /**
     * Get game server status for all realms
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getServerStatusAsync(channel?: number, timeout?: number): Promise<ApiResponse<Schemas['ServerStatusResponse']>>;
    /**
     * Get download links for game clients
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getDownloadsAsync(channel?: number, timeout?: number): Promise<ApiResponse<Schemas['DownloadsResponse']>>;
    /**
     * Submit contact form
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    submitContactAsync(request: Schemas['ContactRequest'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas['ContactResponse']>>;
    /**
     * Get account profile for logged-in user
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getAccountProfileAsync(channel?: number, timeout?: number): Promise<ApiResponse<Schemas['AccountProfile']>>;
    /**
     * Get character list for logged-in user
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getAccountCharactersAsync(channel?: number, timeout?: number): Promise<ApiResponse<Schemas['CharacterListResponse']>>;
    /**
     * Create new CMS page
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    createPageAsync(request: Schemas['PageContent'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas['PageContent']>>;
    /**
     * Update CMS page
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    updatePageAsync(request: Schemas['PageContent'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas['PageContent']>>;
    /**
     * Get site configuration
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getSiteSettingsAsync(channel?: number, timeout?: number): Promise<ApiResponse<Schemas['SiteSettings']>>;
    /**
     * Update site configuration
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    updateSiteSettingsAsync(request: Schemas['SiteSettings'], channel?: number, timeout?: number): Promise<ApiResponse<Schemas['SiteSettings']>>;
    /**
     * Get current theme configuration
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getThemeAsync(channel?: number, timeout?: number): Promise<ApiResponse<Schemas['ThemeConfig']>>;
    /**
     * Update theme configuration
     * @param request - The request payload.
     * @param channel - Message channel for ordering (default 0).
     * @returns Promise that completes when the event is sent.
     */
    updateThemeEventAsync(request: Schemas['ThemeConfig'], channel?: number): Promise<void>;
    /**
     * Get subscription status
     * @param channel - Message channel for ordering (default 0).
     * @param timeout - Request timeout in milliseconds.
     * @returns ApiResponse containing the response on success.
     */
    getAccountSubscriptionAsync(channel?: number, timeout?: number): Promise<ApiResponse<Schemas['SubscriptionResponse']>>;
}

/**
 * BannouClient Extensions
 *
 * This module augments BannouClient with lazy-initialized proxy properties
 * for type-safe API access. Import this module (or import from the main entry
 * point) to enable proxy access like `client.account.getAccountAsync(...)`.
 */

declare module '../BannouClient.js' {
    interface BannouClient {
        /**
         * Typed proxy for Account API endpoints.
         */
        readonly account: AccountProxy;
        /**
         * Typed proxy for Achievement API endpoints.
         */
        readonly achievement: AchievementProxy;
        /**
         * Typed proxy for Actor API endpoints.
         */
        readonly actor: ActorProxy;
        /**
         * Typed proxy for Assets API endpoints.
         */
        readonly assets: AssetsProxy;
        /**
         * Typed proxy for Auth API endpoints.
         */
        readonly auth: AuthProxy;
        /**
         * Typed proxy for Bundles API endpoints.
         */
        readonly bundles: BundlesProxy;
        /**
         * Typed proxy for Cache API endpoints.
         */
        readonly cache: CacheProxy;
        /**
         * Typed proxy for Character API endpoints.
         */
        readonly character: CharacterProxy;
        /**
         * Typed proxy for CharacterEncounter API endpoints.
         */
        readonly characterEncounter: CharacterEncounterProxy;
        /**
         * Typed proxy for CharacterHistory API endpoints.
         */
        readonly characterHistory: CharacterHistoryProxy;
        /**
         * Typed proxy for CharacterPersonality API endpoints.
         */
        readonly characterPersonality: CharacterPersonalityProxy;
        /**
         * Typed proxy for ClientCapabilities API endpoints.
         */
        readonly clientCapabilities: ClientCapabilitiesProxy;
        /**
         * Typed proxy for Compile API endpoints.
         */
        readonly compile: CompileProxy;
        /**
         * Typed proxy for Contract API endpoints.
         */
        readonly contract: ContractProxy;
        /**
         * Typed proxy for Currency API endpoints.
         */
        readonly currency: CurrencyProxy;
        /**
         * Typed proxy for Documentation API endpoints.
         */
        readonly documentation: DocumentationProxy;
        /**
         * Typed proxy for Escrow API endpoints.
         */
        readonly escrow: EscrowProxy;
        /**
         * Typed proxy for GameService API endpoints.
         */
        readonly gameService: GameServiceProxy;
        /**
         * Typed proxy for Goap API endpoints.
         */
        readonly goap: GoapProxy;
        /**
         * Typed proxy for Inventory API endpoints.
         */
        readonly inventory: InventoryProxy;
        /**
         * Typed proxy for Item API endpoints.
         */
        readonly item: ItemProxy;
        /**
         * Typed proxy for Leaderboard API endpoints.
         */
        readonly leaderboard: LeaderboardProxy;
        /**
         * Typed proxy for Location API endpoints.
         */
        readonly location: LocationProxy;
        /**
         * Typed proxy for Mapping API endpoints.
         */
        readonly mapping: MappingProxy;
        /**
         * Typed proxy for Matchmaking API endpoints.
         */
        readonly matchmaking: MatchmakingProxy;
        /**
         * Typed proxy for Music API endpoints.
         */
        readonly music: MusicProxy;
        /**
         * Typed proxy for Realm API endpoints.
         */
        readonly realm: RealmProxy;
        /**
         * Typed proxy for RealmHistory API endpoints.
         */
        readonly realmHistory: RealmHistoryProxy;
        /**
         * Typed proxy for Relationship API endpoints.
         */
        readonly relationship: RelationshipProxy;
        /**
         * Typed proxy for RelationshipType API endpoints.
         */
        readonly relationshipType: RelationshipTypeProxy;
        /**
         * Typed proxy for SaveLoad API endpoints.
         */
        readonly saveLoad: SaveLoadProxy;
        /**
         * Typed proxy for Scene API endpoints.
         */
        readonly scene: SceneProxy;
        /**
         * Typed proxy for Sessions API endpoints.
         */
        readonly sessions: SessionsProxy;
        /**
         * Typed proxy for Species API endpoints.
         */
        readonly species: SpeciesProxy;
        /**
         * Typed proxy for Subscription API endpoints.
         */
        readonly subscription: SubscriptionProxy;
        /**
         * Typed proxy for Validate API endpoints.
         */
        readonly validate: ValidateProxy;
        /**
         * Typed proxy for Voice API endpoints.
         */
        readonly voice: VoiceProxy;
        /**
         * Typed proxy for Website API endpoints.
         */
        readonly website: WebsiteProxy;
    }
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

export { AccountProxy, AchievementProxy, ActorProxy, AssetsProxy, AuthProxy, BannouClient, BundlesProxy, CacheProxy, CharacterEncounterProxy, CharacterHistoryProxy, CharacterPersonalityProxy, CharacterProxy, ClientCapabilitiesProxy, type ClientCapabilityEntry, CompileProxy, ConnectionState, ContractProxy, CurrencyProxy, type DisconnectInfo, DisconnectReason, DocumentationProxy, type EndpointInfoData, EscrowProxy, EventSubscription, type FullSchemaData, GameServiceProxy, GoapProxy, type IBannouClient, type IEventSubscription, InventoryProxy, ItemProxy, type JsonSchemaData, LeaderboardProxy, LocationProxy, MappingProxy, MatchmakingProxy, type MetaResponse, MetaType, MusicProxy, type PendingMessageInfo, RealmHistoryProxy, RealmProxy, RelationshipProxy, RelationshipTypeProxy, SaveLoadProxy, SceneProxy, SessionsProxy, SpeciesProxy, SubscriptionProxy, ValidateProxy, VoiceProxy, WebsiteProxy, generateMessageId, generateUuid, resetMessageIdCounter };
