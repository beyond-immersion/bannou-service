/**
 * Interface for the Bannou WebSocket client.
 * Provides a mockable interface for testing.
 */

import type { ApiResponse, BaseClientEvent } from '@beyondimmersion/bannou-core';
import type { IEventSubscription } from './EventSubscription.js';

/**
 * Disconnect reason codes.
 */
export enum DisconnectReason {
  /** Normal client-initiated disconnect */
  ClientDisconnect = 'client_disconnect',
  /** Server closed the connection */
  ServerClose = 'server_close',
  /** Network error or connection lost */
  NetworkError = 'network_error',
  /** Session expired (token invalid) */
  SessionExpired = 'session_expired',
  /** Server is shutting down */
  ServerShutdown = 'server_shutdown',
  /** Kicked by admin or anti-cheat */
  Kicked = 'kicked',
}

/**
 * Information about a disconnect event.
 */
export interface DisconnectInfo {
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
export enum MetaType {
  /** Basic endpoint info (summary, description, tags) */
  EndpointInfo = 0,
  /** JSON Schema for request body */
  RequestSchema = 1,
  /** JSON Schema for response body */
  ResponseSchema = 2,
  /** Full schema (info + request + response) */
  FullSchema = 3,
}

/**
 * Endpoint information from meta request.
 */
export interface EndpointInfoData {
  summary?: string;
  description?: string;
  tags?: string[];
  deprecated?: boolean;
}

/**
 * JSON Schema data from meta request.
 */
export interface JsonSchemaData {
  schema: Record<string, unknown>;
}

/**
 * Full schema data from meta request.
 */
export interface FullSchemaData {
  info: EndpointInfoData;
  requestSchema?: Record<string, unknown>;
  responseSchema?: Record<string, unknown>;
}

/**
 * Meta response wrapper.
 */
export interface MetaResponse<T> {
  data: T;
  endpointKey: string;
  metaType: MetaType;
}

/**
 * Interface for the Bannou WebSocket client.
 */
export interface IBannouClient {
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
  connectWithTokenAsync(
    serverUrl: string,
    accessToken: string,
    refreshToken?: string
  ): Promise<boolean>;

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
  registerAndConnectAsync(
    serverUrl: string,
    username: string,
    email: string,
    password: string
  ): Promise<boolean>;

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
  invokeAsync<TRequest, TResponse>(
    method: string,
    path: string,
    request: TRequest,
    channel?: number,
    timeout?: number
  ): Promise<ApiResponse<TResponse>>;

  /**
   * Sends a fire-and-forget event (no response expected).
   * @param method - HTTP method
   * @param path - API path
   * @param request - Request payload
   * @param channel - Message channel for ordering
   */
  sendEventAsync<TRequest>(
    method: string,
    path: string,
    request: TRequest,
    channel?: number
  ): Promise<void>;

  /**
   * Subscribe to a typed event with automatic deserialization.
   * @param eventName - Event name to subscribe to
   * @param handler - Handler to invoke when event is received
   * @returns Subscription handle - call dispose() to unsubscribe
   */
  onEvent<TEvent extends BaseClientEvent>(
    eventName: string,
    handler: (event: TEvent) => void
  ): IEventSubscription;

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

  // Lifecycle callbacks

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

  // Meta requests

  /**
   * Requests metadata about an endpoint instead of executing it.
   * @param method - HTTP method
   * @param path - API path
   * @param metaType - Type of metadata to request
   * @param timeout - Request timeout in milliseconds
   */
  getEndpointMetaAsync<T>(
    method: string,
    path: string,
    metaType?: MetaType,
    timeout?: number
  ): Promise<MetaResponse<T>>;

  /**
   * Gets human-readable endpoint information (summary, description, tags).
   */
  getEndpointInfoAsync(method: string, path: string): Promise<MetaResponse<EndpointInfoData>>;

  /**
   * Gets JSON Schema for the request body of an endpoint.
   */
  getRequestSchemaAsync(method: string, path: string): Promise<MetaResponse<JsonSchemaData>>;

  /**
   * Gets JSON Schema for the response body of an endpoint.
   */
  getResponseSchemaAsync(method: string, path: string): Promise<MetaResponse<JsonSchemaData>>;

  /**
   * Gets full schema including info, request schema, and response schema.
   */
  getFullSchemaAsync(method: string, path: string): Promise<MetaResponse<FullSchemaData>>;

  // Reconnection

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
  set onTokenRefreshed(
    callback: ((accessToken: string, refreshToken: string | undefined) => void) | null
  );
}
