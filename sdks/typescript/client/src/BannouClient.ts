/**
 * High-level client for connecting to Bannou services via WebSocket.
 * Handles authentication, connection lifecycle, capability discovery, and message correlation.
 */

import {
  ApiResponse,
  BannouJson,
  type BaseClientEvent,
  createErrorResponse,
} from '@beyondimmersion/bannou-core';
import type {
  IBannouClient,
  DisconnectInfo,
  MetaResponse,
  EndpointInfoData,
  JsonSchemaData,
  FullSchemaData,
} from './IBannouClient.js';
import { DisconnectReason, MetaType } from './IBannouClient.js';
import { ConnectionState } from './ConnectionState.js';
import { EventSubscription, type IEventSubscription } from './EventSubscription.js';
import { generateMessageId, generateUuid } from './GuidGenerator.js';
import {
  MessageFlags,
  RESPONSE_HEADER_SIZE,
  type BinaryMessage,
  parse,
  toByteArray,
  isResponse,
  getJsonPayload,
} from './protocol/index.js';

// Type definition for WebSocket (browser or Node.js)
type WebSocketType = WebSocket | import('ws').WebSocket;

/** Default request timeout in milliseconds */
const DEFAULT_TIMEOUT_MS = 30_000;

/** Capability manifest wait timeout in milliseconds */
const CAPABILITY_MANIFEST_TIMEOUT_MS = 30_000;

/** Internal authorization header value */
const INTERNAL_AUTHORIZATION_HEADER = 'Internal';

/**
 * Login response from auth service.
 */
interface LoginResponse {
  accessToken?: string;
  refreshToken?: string;
  connectUrl?: string;
}

/**
 * Pending request tracking.
 */
interface PendingRequest {
  resolve: (message: BinaryMessage) => void;
  reject: (error: Error) => void;
  timeoutId: ReturnType<typeof setTimeout>;
}

/**
 * Event handler entry.
 */
interface EventHandler {
  subscriptionId: string;
  handler: (json: string) => void;
}

/**
 * High-level client for connecting to Bannou services via WebSocket.
 */
export class BannouClient implements IBannouClient {
  private webSocket: WebSocketType | null = null;
  private connectionState: ConnectionState | null = null;
  private readonly pendingRequests = new Map<string, PendingRequest>();
  private readonly apiMappings = new Map<string, string>();
  private readonly eventHandlers = new Map<string, EventHandler[]>();

  private _accessToken: string | undefined;
  private _refreshToken: string | undefined;
  private serverBaseUrl: string | undefined;
  private connectUrl: string | undefined;
  private serviceToken: string | undefined;
  private _sessionId: string | undefined;
  private _lastError: string | undefined;

  private capabilityManifestResolver: ((value: boolean) => void) | null = null;
  private eventHandlerFailedCallback: ((error: Error) => void) | null = null;
  private disconnectCallback: ((info: DisconnectInfo) => void) | null = null;
  private errorCallback: ((error: Error) => void) | null = null;
  private tokenRefreshedCallback:
    | ((accessToken: string, refreshToken: string | undefined) => void)
    | null = null;
  private pendingReconnectionToken: string | undefined;
  private lastDisconnectInfo: DisconnectInfo | undefined;

  /**
   * Current connection state.
   */
  get isConnected(): boolean {
    return this.webSocket?.readyState === 1; // WebSocket.OPEN = 1
  }

  /**
   * Session ID assigned by the server after connection.
   */
  get sessionId(): string | undefined {
    return this._sessionId;
  }

  /**
   * All available API endpoints with their client-salted GUIDs.
   */
  get availableApis(): ReadonlyMap<string, string> {
    return this.apiMappings;
  }

  /**
   * Current access token (JWT).
   */
  get accessToken(): string | undefined {
    return this._accessToken;
  }

  /**
   * Current refresh token for re-authentication.
   */
  get refreshToken(): string | undefined {
    return this._refreshToken;
  }

  /**
   * Last error message from a failed operation.
   */
  get lastError(): string | undefined {
    return this._lastError;
  }

  /**
   * Set a callback for when event handlers throw exceptions.
   */
  set onEventHandlerFailed(callback: ((error: Error) => void) | null) {
    this.eventHandlerFailedCallback = callback;
  }

  /**
   * Set a callback for when the connection is closed.
   */
  set onDisconnect(callback: ((info: DisconnectInfo) => void) | null) {
    this.disconnectCallback = callback;
  }

  /**
   * Set a callback for when a connection error occurs.
   */
  set onError(callback: ((error: Error) => void) | null) {
    this.errorCallback = callback;
  }

  /**
   * Set a callback for when tokens are refreshed.
   */
  set onTokenRefreshed(
    callback: ((accessToken: string, refreshToken: string | undefined) => void) | null
  ) {
    this.tokenRefreshedCallback = callback;
  }

  /**
   * Get the last disconnect information (if any).
   */
  get lastDisconnect(): DisconnectInfo | undefined {
    return this.lastDisconnectInfo;
  }

  /**
   * Connects to a Bannou server using username/password authentication.
   */
  async connectAsync(serverUrl: string, email: string, password: string): Promise<boolean> {
    this.serverBaseUrl = serverUrl.replace(/\/$/, '');
    this.serviceToken = undefined;

    // Step 1: Authenticate via HTTP to get JWT
    const loginResult = await this.loginAsync(email, password);
    if (!loginResult) {
      return false;
    }

    // Step 2: Establish WebSocket connection with JWT
    return this.establishWebSocketAsync();
  }

  /**
   * Connects using an existing JWT token.
   */
  async connectWithTokenAsync(
    serverUrl: string,
    accessToken: string,
    refreshToken?: string
  ): Promise<boolean> {
    this.serverBaseUrl = serverUrl.replace(/\/$/, '');
    this._accessToken = accessToken;
    this._refreshToken = refreshToken;
    this.serviceToken = undefined;

    return this.establishWebSocketAsync();
  }

  /**
   * Connects in internal mode using a service token.
   */
  async connectInternalAsync(connectUrl: string, serviceToken?: string): Promise<boolean> {
    this.serverBaseUrl = undefined;
    this.connectUrl = connectUrl.replace(/\/$/, '');
    this._accessToken = undefined;
    this._refreshToken = undefined;
    this.serviceToken = serviceToken;

    return this.establishWebSocketAsync(true);
  }

  /**
   * Registers a new account and connects.
   */
  async registerAndConnectAsync(
    serverUrl: string,
    username: string,
    email: string,
    password: string
  ): Promise<boolean> {
    this.serverBaseUrl = serverUrl.replace(/\/$/, '');
    this.serviceToken = undefined;

    // Step 1: Register account
    const registerResult = await this.registerAsync(username, email, password);
    if (!registerResult) {
      return false;
    }

    // Step 2: Establish WebSocket connection with JWT
    return this.establishWebSocketAsync();
  }

  /**
   * Gets the service GUID for a specific API endpoint.
   * @param endpoint API path (e.g., "/account/get")
   */
  getServiceGuid(endpoint: string): string | undefined {
    return this.apiMappings.get(endpoint);
  }

  /**
   * Invokes a service method by specifying the API endpoint path.
   * @param endpoint API path (e.g., "/account/get")
   */
  async invokeAsync<TRequest, TResponse>(
    endpoint: string,
    request: TRequest,
    channel: number = 0,
    timeout: number = DEFAULT_TIMEOUT_MS
  ): Promise<ApiResponse<TResponse>> {
    if (!this.isConnected || !this.webSocket) {
      throw new Error('WebSocket is not connected. Call connectAsync first.');
    }

    if (!this.connectionState) {
      throw new Error('Connection state not initialized.');
    }

    // Get service GUID
    const serviceGuid = this.getServiceGuid(endpoint);
    if (!serviceGuid) {
      const available = Array.from(this.apiMappings.keys()).slice(0, 10).join(', ');
      throw new Error(`Unknown endpoint: ${endpoint}. Available: ${available}...`);
    }

    // Serialize request to JSON
    const jsonPayload = BannouJson.serialize(request);
    const payloadBytes = new TextEncoder().encode(jsonPayload);

    // Create binary message
    const messageId = generateMessageId();
    const sequenceNumber = this.connectionState.getNextSequenceNumber(channel);

    const message: BinaryMessage = {
      flags: MessageFlags.None,
      channel,
      sequenceNumber,
      serviceGuid,
      messageId,
      responseCode: 0,
      payload: payloadBytes,
    };

    // Set up response awaiter
    const responsePromise = this.createResponsePromise(messageId, timeout, endpoint);
    this.connectionState.addPendingMessage(messageId, endpoint, new Date());

    try {
      // Send message
      const messageBytes = toByteArray(message);
      this.sendBinaryMessage(messageBytes);

      // Wait for response
      const response = await responsePromise;

      // Check response code
      if (response.responseCode !== 0) {
        return ApiResponse.failure<TResponse>(
          createErrorResponse(response.responseCode, messageId, endpoint)
        );
      }

      // Success response - parse JSON payload
      if (response.payload.length === 0) {
        return ApiResponse.successEmpty<TResponse>();
      }

      const responseJson = getJsonPayload(response);
      const result = BannouJson.deserialize<TResponse>(responseJson);
      if (result === null) {
        return ApiResponse.failure<TResponse>(
          createErrorResponse(60, messageId, endpoint, 'Failed to deserialize response')
        );
      }

      return ApiResponse.success(result);
    } finally {
      this.pendingRequests.delete(messageId.toString());
      this.connectionState?.removePendingMessage(messageId);
    }
  }

  /**
   * Sends a fire-and-forget event (no response expected).
   * @param endpoint API path (e.g., "/events/publish")
   */
  async sendEventAsync<TRequest>(
    endpoint: string,
    request: TRequest,
    channel: number = 0
  ): Promise<void> {
    if (!this.isConnected || !this.webSocket) {
      throw new Error('WebSocket is not connected. Call connectAsync first.');
    }

    if (!this.connectionState) {
      throw new Error('Connection state not initialized.');
    }

    // Get service GUID
    const serviceGuid = this.getServiceGuid(endpoint);
    if (!serviceGuid) {
      throw new Error(`Unknown endpoint: ${endpoint}`);
    }

    // Serialize request to JSON
    const jsonPayload = BannouJson.serialize(request);
    const payloadBytes = new TextEncoder().encode(jsonPayload);

    // Create binary message with Event flag
    const messageId = generateMessageId();
    const sequenceNumber = this.connectionState.getNextSequenceNumber(channel);

    const message: BinaryMessage = {
      flags: MessageFlags.Event,
      channel,
      sequenceNumber,
      serviceGuid,
      messageId,
      responseCode: 0,
      payload: payloadBytes,
    };

    // Send message
    const messageBytes = toByteArray(message);
    this.sendBinaryMessage(messageBytes);
  }

  /**
   * Subscribe to a typed event with automatic deserialization.
   */
  onEvent<TEvent extends BaseClientEvent>(
    eventName: string,
    handler: (event: TEvent) => void
  ): IEventSubscription {
    const subscriptionId = generateUuid();

    const wrappedHandler = (json: string): void => {
      try {
        const event = BannouJson.deserialize<TEvent>(json);
        if (event) {
          handler(event);
        }
      } catch (error) {
        this.eventHandlerFailedCallback?.(error as Error);
      }
    };

    const handlers = this.eventHandlers.get(eventName) ?? [];
    handlers.push({ subscriptionId, handler: wrappedHandler });
    this.eventHandlers.set(eventName, handlers);

    return new EventSubscription(eventName, subscriptionId, (id) => {
      const existing = this.eventHandlers.get(eventName);
      if (existing) {
        const filtered = existing.filter((h) => h.subscriptionId !== id);
        if (filtered.length > 0) {
          this.eventHandlers.set(eventName, filtered);
        } else {
          this.eventHandlers.delete(eventName);
        }
      }
    });
  }

  /**
   * Subscribe to raw event JSON.
   */
  onRawEvent(eventName: string, handler: (json: string) => void): IEventSubscription {
    const subscriptionId = generateUuid();

    const handlers = this.eventHandlers.get(eventName) ?? [];
    handlers.push({ subscriptionId, handler });
    this.eventHandlers.set(eventName, handlers);

    return new EventSubscription(eventName, subscriptionId, (id) => {
      const existing = this.eventHandlers.get(eventName);
      if (existing) {
        const filtered = existing.filter((h) => h.subscriptionId !== id);
        if (filtered.length > 0) {
          this.eventHandlers.set(eventName, filtered);
        } else {
          this.eventHandlers.delete(eventName);
        }
      }
    });
  }

  /**
   * Removes all event handlers for a specific event.
   */
  removeEventHandlers(eventName: string): void {
    this.eventHandlers.delete(eventName);
  }

  /**
   * Requests metadata about an endpoint instead of executing it.
   * Uses the Meta flag (0x80) which triggers route transformation at Connect service.
   * @param endpoint API path (e.g., "/account/get")
   */
  async getEndpointMetaAsync<T>(
    endpoint: string,
    metaType: MetaType = MetaType.FullSchema,
    timeout: number = 10_000
  ): Promise<MetaResponse<T>> {
    if (!this.isConnected || !this.webSocket) {
      throw new Error('WebSocket is not connected. Call connectAsync first.');
    }

    if (!this.connectionState) {
      throw new Error('Connection state not initialized.');
    }

    // Get service GUID (same as regular requests)
    const serviceGuid = this.getServiceGuid(endpoint);
    if (!serviceGuid) {
      const available = Array.from(this.apiMappings.keys()).slice(0, 10).join(', ');
      throw new Error(`Unknown endpoint: ${endpoint}. Available: ${available}...`);
    }

    // Create meta message - meta type encoded in Channel field, payload is EMPTY
    const messageId = generateMessageId();
    const sequenceNumber = this.connectionState.getNextSequenceNumber(metaType);

    const message: BinaryMessage = {
      flags: MessageFlags.Meta, // Meta flag triggers route transformation
      channel: metaType, // Meta type in Channel (0=info, 1=req, 2=resp, 3=full)
      sequenceNumber,
      serviceGuid,
      messageId,
      responseCode: 0,
      payload: new Uint8Array(0), // EMPTY - Connect never reads payloads
    };

    // Set up response awaiter
    const responsePromise = this.createResponsePromise(messageId, timeout, `META:${endpoint}`);
    this.connectionState.addPendingMessage(messageId, `META:${endpoint}`, new Date());

    try {
      // Send message
      const messageBytes = toByteArray(message);
      this.sendBinaryMessage(messageBytes);

      // Wait for response
      const response = await responsePromise;

      // Check response code
      if (response.responseCode !== 0) {
        throw new Error(`Meta request failed with response code ${response.responseCode}`);
      }

      // Parse meta response
      const responseJson = getJsonPayload(response);
      const result = BannouJson.deserialize<MetaResponse<T>>(responseJson);
      if (result === null) {
        throw new Error('Failed to deserialize meta response');
      }

      return result;
    } finally {
      this.pendingRequests.delete(messageId.toString());
      this.connectionState?.removePendingMessage(messageId);
    }
  }

  /**
   * Gets human-readable endpoint information (summary, description, tags).
   * @param endpoint API path (e.g., "/account/get")
   */
  async getEndpointInfoAsync(endpoint: string): Promise<MetaResponse<EndpointInfoData>> {
    return this.getEndpointMetaAsync<EndpointInfoData>(endpoint, MetaType.EndpointInfo);
  }

  /**
   * Gets JSON Schema for the request body of an endpoint.
   * @param endpoint API path (e.g., "/account/get")
   */
  async getRequestSchemaAsync(endpoint: string): Promise<MetaResponse<JsonSchemaData>> {
    return this.getEndpointMetaAsync<JsonSchemaData>(endpoint, MetaType.RequestSchema);
  }

  /**
   * Gets JSON Schema for the response body of an endpoint.
   * @param endpoint API path (e.g., "/account/get")
   */
  async getResponseSchemaAsync(endpoint: string): Promise<MetaResponse<JsonSchemaData>> {
    return this.getEndpointMetaAsync<JsonSchemaData>(endpoint, MetaType.ResponseSchema);
  }

  /**
   * Gets full schema including info, request schema, and response schema.
   * @param endpoint API path (e.g., "/account/get")
   */
  async getFullSchemaAsync(endpoint: string): Promise<MetaResponse<FullSchemaData>> {
    return this.getEndpointMetaAsync<FullSchemaData>(endpoint, MetaType.FullSchema);
  }

  /**
   * Reconnects using a reconnection token from a DisconnectNotificationEvent.
   */
  async reconnectWithTokenAsync(reconnectionToken: string): Promise<boolean> {
    if (!this.serverBaseUrl && !this.connectUrl) {
      this._lastError = 'No server URL available for reconnection';
      return false;
    }

    // Store the reconnection token for the WebSocket connection
    this.pendingReconnectionToken = reconnectionToken;

    try {
      // Re-establish WebSocket connection with the reconnection token
      return await this.establishWebSocketAsync();
    } finally {
      this.pendingReconnectionToken = undefined;
    }
  }

  /**
   * Refreshes the access token using the stored refresh token.
   */
  async refreshAccessTokenAsync(): Promise<boolean> {
    if (!this._refreshToken) {
      this._lastError = 'No refresh token available';
      return false;
    }

    if (!this.serverBaseUrl) {
      this._lastError = 'No server URL available for token refresh';
      return false;
    }

    const refreshUrl = `${this.serverBaseUrl}/auth/refresh`;

    try {
      const response = await fetch(refreshUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: this._refreshToken }),
      });

      if (!response.ok) {
        const errorContent = await response.text();
        this._lastError = `Token refresh failed (${response.status}): ${errorContent}`;
        return false;
      }

      const result: LoginResponse = await response.json();
      if (!result.accessToken) {
        this._lastError = 'Token refresh response missing access token';
        return false;
      }

      this._accessToken = result.accessToken;
      if (result.refreshToken) {
        this._refreshToken = result.refreshToken;
      }

      // Notify callback if set
      this.tokenRefreshedCallback?.(this._accessToken, this._refreshToken);

      return true;
    } catch (error) {
      this._lastError = `Token refresh exception: ${(error as Error).message}`;
      return false;
    }
  }

  /**
   * Disconnects from the server.
   */
  async disconnectAsync(): Promise<void> {
    // Cancel all pending requests
    for (const [, pending] of this.pendingRequests) {
      clearTimeout(pending.timeoutId);
      pending.reject(new Error('Disconnecting'));
    }
    this.pendingRequests.clear();

    if (this.webSocket) {
      try {
        this.webSocket.close(1000, 'Client disconnecting');
      } catch {
        // Ignore close errors
      }
    }

    this.webSocket = null;
    this.connectionState = null;
    this._accessToken = undefined;
    this._refreshToken = undefined;
    this.serviceToken = undefined;
    this.apiMappings.clear();
  }

  // Private methods

  private buildAuthorizationHeader(allowAnonymousInternal: boolean): string {
    if (this._accessToken) {
      return `Bearer ${this._accessToken}`;
    }

    if (this.serviceToken || allowAnonymousInternal) {
      return INTERNAL_AUTHORIZATION_HEADER;
    }

    return '';
  }

  private async loginAsync(email: string, password: string): Promise<boolean> {
    const loginUrl = `${this.serverBaseUrl}/auth/login`;

    try {
      const response = await fetch(loginUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password }),
      });

      if (!response.ok) {
        const errorContent = await response.text();
        this._lastError = `Login failed (${response.status}): ${errorContent}`;
        return false;
      }

      const result: LoginResponse = await response.json();
      if (!result.accessToken) {
        this._lastError = 'Login response missing access token';
        return false;
      }

      this._accessToken = result.accessToken;
      this._refreshToken = result.refreshToken;
      this.connectUrl = result.connectUrl;
      return true;
    } catch (error) {
      this._lastError = `Login exception: ${(error as Error).message}`;
      return false;
    }
  }

  private async registerAsync(username: string, email: string, password: string): Promise<boolean> {
    const registerUrl = `${this.serverBaseUrl}/auth/register`;

    try {
      const response = await fetch(registerUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, email, password }),
      });

      if (!response.ok) {
        const errorContent = await response.text();
        this._lastError = `Registration failed (${response.status}): ${errorContent}`;
        return false;
      }

      const result: LoginResponse = await response.json();
      if (!result.accessToken) {
        this._lastError = 'Registration response missing access token';
        return false;
      }

      this._accessToken = result.accessToken;
      this._refreshToken = result.refreshToken;
      this.connectUrl = result.connectUrl;
      return true;
    } catch (error) {
      this._lastError = `Registration exception: ${(error as Error).message}`;
      return false;
    }
  }

  private async establishWebSocketAsync(allowAnonymousInternal: boolean = false): Promise<boolean> {
    if (!this._accessToken && !this.serviceToken && !allowAnonymousInternal) {
      this._lastError = 'No access token available for WebSocket connection';
      return false;
    }

    // Determine WebSocket URL
    let wsUrl: string;
    if (this.connectUrl) {
      wsUrl = this.connectUrl.replace('https://', 'wss://').replace('http://', 'ws://');
    } else if (this.serverBaseUrl) {
      wsUrl = this.serverBaseUrl.replace('https://', 'wss://').replace('http://', 'ws://');
      // Add /connect path if not already present
      if (!wsUrl.endsWith('/connect')) {
        wsUrl = `${wsUrl}/connect`;
      }
    } else {
      this._lastError = 'No server URL available';
      return false;
    }

    try {
      this.webSocket = await this.createWebSocket(wsUrl, allowAnonymousInternal);
    } catch (error) {
      this._lastError = `WebSocket connection failed to ${wsUrl}: ${(error as Error).message}`;
      return false;
    }

    // Initialize connection state
    const sessionId = generateUuid();
    this.connectionState = new ConnectionState(sessionId);

    // Set up message handler
    this.setupMessageHandler();

    // Wait for capability manifest
    const manifestReceived = await this.waitForCapabilityManifest();
    if (!manifestReceived) {
      // Connection still works, but warn
      console.warn('Capability manifest not received within timeout');
    }

    return true;
  }

  private async createWebSocket(
    url: string,
    allowAnonymousInternal: boolean
  ): Promise<WebSocketType> {
    const authHeader = this.buildAuthorizationHeader(allowAnonymousInternal);

    // Check if we're in a browser environment
    if (typeof window !== 'undefined' && typeof window.WebSocket !== 'undefined') {
      // Browser environment - WebSocket constructor doesn't support custom headers
      // The server should accept auth via query param or cookie for browser clients
      return new Promise<WebSocket>((resolve, reject) => {
        // Add auth to URL for browser (if server supports it)
        let authUrl = url;
        if (authHeader && authHeader.startsWith('Bearer ')) {
          const token = authHeader.replace('Bearer ', '');
          const separator = url.includes('?') ? '&' : '?';
          authUrl = `${url}${separator}token=${encodeURIComponent(token)}`;
        }

        const ws = new WebSocket(authUrl);
        ws.binaryType = 'arraybuffer';

        ws.onopen = () => resolve(ws);
        ws.onerror = () => reject(new Error('WebSocket connection failed'));
      });
    } else {
      // Node.js environment - use 'ws' package with headers
      const WebSocketModule = await import('ws');
      const WS = WebSocketModule.default || WebSocketModule.WebSocket || WebSocketModule;

      return new Promise<import('ws').WebSocket>((resolve, reject) => {
        const headers: Record<string, string> = {};
        if (authHeader) {
          headers['Authorization'] = authHeader;
        }
        if (this.serviceToken) {
          headers['X-Service-Token'] = this.serviceToken;
        }

        const ws = new WS(url, { headers }) as import('ws').WebSocket;
        console.error(`[DEBUG] createWebSocket: created WebSocket to ${url}`);

        // CRITICAL: Set up message buffering BEFORE 'open' fires to avoid race condition.
        // The server may send capability manifest immediately after accepting connection,
        // so we must buffer messages until setupMessageHandler() takes over.
        // The early handler stays active until setupMessageHandler removes it.
        const earlyMessages: Buffer[] = [];
        const earlyMessageHandler = (data: Buffer): void => {
          earlyMessages.push(data);
        };
        ws.on('message', earlyMessageHandler);

        // Store references so setupMessageHandler can access them
        const wsWithEarly = ws as import('ws').WebSocket & {
          _earlyMessages?: Buffer[];
          _earlyMessageHandler?: (data: Buffer) => void;
        };
        wsWithEarly._earlyMessages = earlyMessages;
        wsWithEarly._earlyMessageHandler = earlyMessageHandler;

        ws.on('open', () => {
          // Don't remove early handler here - setupMessageHandler will handle the transition
          // This prevents a gap where messages could be lost
          resolve(ws);
        });
        ws.on('error', (err: Error) => {
          reject(err);
        });
      });
    }
  }

  private setupMessageHandler(): void {
    if (!this.webSocket) return;

    const handleMessage = (data: ArrayBuffer | Buffer): void => {
      try {
        // Handle both ArrayBuffer and Node.js Buffer correctly
        // Node.js Buffers can be views into larger pooled ArrayBuffers,
        // so we must account for byteOffset when converting
        let bytes: Uint8Array;
        if (data instanceof ArrayBuffer) {
          bytes = new Uint8Array(data);
        } else {
          // Node.js Buffer: create Uint8Array with correct offset and length
          bytes = new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
        }

        // Check minimum size
        if (bytes.length < RESPONSE_HEADER_SIZE) {
          return;
        }

        const message = parse(bytes);
        this.handleReceivedMessage(message);
      } catch {
        // Silently ignore malformed messages
      }
    };

    const handleClose = (code: number, reason?: string): void => {
      // Map close code to disconnect reason
      let disconnectReason = DisconnectReason.ServerClose;
      let canReconnect = false;

      // WebSocket close codes: https://developer.mozilla.org/en-US/docs/Web/API/CloseEvent/code
      if (code === 1000) {
        disconnectReason = DisconnectReason.ClientDisconnect;
      } else if (code === 1001) {
        disconnectReason = DisconnectReason.ServerShutdown;
        canReconnect = true;
      } else if (code >= 4000 && code < 5000) {
        // Application-specific codes
        if (code === 4001) {
          disconnectReason = DisconnectReason.SessionExpired;
        } else if (code === 4002) {
          disconnectReason = DisconnectReason.Kicked;
        } else {
          disconnectReason = DisconnectReason.ServerClose;
          canReconnect = true;
        }
      } else if (code >= 1002 && code <= 1015) {
        disconnectReason = DisconnectReason.NetworkError;
        canReconnect = true;
      }

      // Use pending reconnection token if available (from DisconnectNotificationEvent)
      const reconnectionToken = this.pendingReconnectionToken;
      if (reconnectionToken) {
        canReconnect = true;
      }

      const info: DisconnectInfo = {
        reason: disconnectReason,
        message: reason || undefined,
        reconnectionToken,
        canReconnect,
        closeCode: code,
      };

      this.lastDisconnectInfo = info;
      this.disconnectCallback?.(info);
    };

    const handleError = (error: Error | Event): void => {
      const err = error instanceof Error ? error : new Error('WebSocket error');
      this.errorCallback?.(err);
    };

    // Check for browser environment the same way we do in createWebSocket
    const isBrowser = typeof window !== 'undefined' && typeof window.WebSocket !== 'undefined';

    if (isBrowser) {
      // Browser WebSocket
      const browserWs = this.webSocket as WebSocket;
      browserWs.addEventListener('message', (event: MessageEvent) => {
        if (event.data instanceof ArrayBuffer) {
          handleMessage(event.data);
        }
      });
      browserWs.addEventListener('close', (event: CloseEvent) => {
        handleClose(event.code, event.reason);
      });
      browserWs.addEventListener('error', (event: Event) => {
        handleError(event);
      });
    } else {
      // Node.js 'ws' WebSocket
      const nodeWs = this.webSocket as import('ws').WebSocket;
      console.error(`[DEBUG] setupMessageHandler: Node.js WebSocket detected`);

      // Remove the early message handler now that we're setting up the real handler.
      // The early handler was set up in createWebSocket to buffer messages that arrive
      // between 'open' firing and setupMessageHandler being called.
      const wsWithEarly = nodeWs as import('ws').WebSocket & {
        _earlyMessages?: Buffer[];
        _earlyMessageHandler?: (data: Buffer) => void;
      };
      const earlyHandler = wsWithEarly._earlyMessageHandler;
      console.error(`[DEBUG] setupMessageHandler: earlyHandler exists=${!!earlyHandler}`);
      if (earlyHandler) {
        nodeWs.off('message', earlyHandler);
        wsWithEarly._earlyMessageHandler = undefined;
      }

      // Process any messages that arrived before setupMessageHandler was called
      // (fixes race condition where server sends capability manifest immediately)
      const earlyMessages = wsWithEarly._earlyMessages;
      console.error(
        `[DEBUG] setupMessageHandler: earlyMessages count=${earlyMessages?.length ?? 0}`
      );
      if (earlyMessages && earlyMessages.length > 0) {
        console.error(
          `[DEBUG] setupMessageHandler: processing ${earlyMessages.length} early messages`
        );
        for (const data of earlyMessages) {
          console.error(
            `[DEBUG] setupMessageHandler: processing early message, length=${data.length}`
          );
          handleMessage(data);
        }
      }
      // Clear the early messages reference
      wsWithEarly._earlyMessages = undefined;

      nodeWs.on('message', (data: Buffer) => {
        console.error(`[DEBUG] setupMessageHandler: received live message, length=${data.length}`);
        handleMessage(data);
      });
      nodeWs.on('close', (code: number, reason: Buffer) => {
        handleClose(code, reason.toString());
      });
      nodeWs.on('error', (error: Error) => {
        handleError(error);
      });
    }
  }

  private handleReceivedMessage(message: BinaryMessage): void {
    if (isResponse(message)) {
      // Complete pending request
      const pending = this.pendingRequests.get(message.messageId.toString());
      if (pending) {
        clearTimeout(pending.timeoutId);
        pending.resolve(message);
      }
    } else if ((message.flags & MessageFlags.Event) !== 0) {
      // Server-pushed event
      this.handleEventMessage(message);
    }
  }

  private handleEventMessage(message: BinaryMessage): void {
    if (message.payload.length === 0) {
      return;
    }

    try {
      const payloadJson = new TextDecoder().decode(message.payload);
      const parsed = JSON.parse(payloadJson) as { eventName?: string };

      const eventName = parsed.eventName;
      if (!eventName) {
        return;
      }

      // Handle capability manifest specially
      if (eventName === 'connect.capability_manifest') {
        this.handleCapabilityManifest(payloadJson);
      }

      // Handle disconnect notification specially
      if (eventName === 'connect.disconnect_notification') {
        this.handleDisconnectNotification(payloadJson);
      }

      // Dispatch to registered handlers
      const handlers = this.eventHandlers.get(eventName);
      if (handlers) {
        for (const { handler } of handlers) {
          try {
            handler(payloadJson);
          } catch (error) {
            this.eventHandlerFailedCallback?.(error as Error);
          }
        }
      }
    } catch {
      // Silently ignore parse errors
    }
  }

  private handleCapabilityManifest(json: string): void {
    try {
      const manifest = JSON.parse(json) as {
        sessionId?: string;
        availableApis?: Array<{
          serviceId?: string;
          endpoint?: string;
          service?: string;
          description?: string;
        }>;
      };

      // Extract session ID
      if (manifest.sessionId) {
        this._sessionId = manifest.sessionId;
      }

      // Extract available APIs - new schema format: { serviceId, endpoint, service, description }
      if (Array.isArray(manifest.availableApis)) {
        for (const api of manifest.availableApis) {
          // endpoint is now the lookup key (e.g., "/account/get")
          if (api.endpoint && api.serviceId) {
            this.apiMappings.set(api.endpoint, api.serviceId);
            this.connectionState?.addServiceMapping(api.endpoint, api.serviceId);
          }
        }
      }

      // Signal that capabilities are received
      if (this.capabilityManifestResolver) {
        this.capabilityManifestResolver(true);
        this.capabilityManifestResolver = null;
      }
    } catch {
      // Silently ignore manifest parse errors
    }
  }

  private handleDisconnectNotification(json: string): void {
    try {
      const notification = JSON.parse(json) as {
        reason?: string;
        message?: string;
        reconnectionToken?: string;
        shutdownTime?: string;
      };

      // Store reconnection token for when WebSocket actually closes
      if (notification.reconnectionToken) {
        this.pendingReconnectionToken = notification.reconnectionToken;
      }

      // Pre-populate disconnect info with data from notification
      // This will be merged with close event data when connection actually closes
      let reason = DisconnectReason.ServerClose;
      if (notification.reason === 'session_expired') {
        reason = DisconnectReason.SessionExpired;
      } else if (notification.reason === 'kicked') {
        reason = DisconnectReason.Kicked;
      } else if (notification.reason === 'server_shutdown') {
        reason = DisconnectReason.ServerShutdown;
      }

      this.lastDisconnectInfo = {
        reason,
        message: notification.message,
        reconnectionToken: notification.reconnectionToken,
        canReconnect: !!notification.reconnectionToken,
      };
    } catch {
      // Ignore parse errors
    }
  }

  private waitForCapabilityManifest(): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      this.capabilityManifestResolver = resolve;

      // Set up timeout
      setTimeout(() => {
        if (this.capabilityManifestResolver) {
          this.capabilityManifestResolver(false);
          this.capabilityManifestResolver = null;
        }
      }, CAPABILITY_MANIFEST_TIMEOUT_MS);
    });
  }

  private createResponsePromise(
    messageId: bigint,
    timeout: number,
    endpoint: string
  ): Promise<BinaryMessage> {
    return new Promise<BinaryMessage>((resolve, reject) => {
      const timeoutId = setTimeout(() => {
        this.pendingRequests.delete(messageId.toString());
        reject(new Error(`Request to ${endpoint} timed out after ${timeout}ms`));
      }, timeout);

      this.pendingRequests.set(messageId.toString(), {
        resolve,
        reject,
        timeoutId,
      });
    });
  }

  private sendBinaryMessage(data: Uint8Array): void {
    if (!this.webSocket) {
      throw new Error('WebSocket not connected');
    }

    if ('send' in this.webSocket) {
      if (typeof window !== 'undefined') {
        // Browser
        (this.webSocket as WebSocket).send(data);
      } else {
        // Node.js
        (this.webSocket as import('ws').WebSocket).send(data);
      }
    }
  }
}
