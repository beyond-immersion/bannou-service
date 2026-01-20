/**
 * Interface for the Bannou WebSocket client.
 * Provides a mockable interface for testing.
 */

import type { ApiResponse, BaseClientEvent } from '@beyondimmersion/bannou-core';
import type { IEventSubscription } from './EventSubscription.js';

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
}
