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
import type { IBannouClient } from './IBannouClient.js';
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
   */
  getServiceGuid(method: string, path: string): string | undefined {
    const key = `${method}:${path}`;
    return this.apiMappings.get(key);
  }

  /**
   * Invokes a service method by specifying the HTTP method and path.
   */
  async invokeAsync<TRequest, TResponse>(
    method: string,
    path: string,
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
    const serviceGuid = this.getServiceGuid(method, path);
    if (!serviceGuid) {
      const available = Array.from(this.apiMappings.keys()).slice(0, 10).join(', ');
      throw new Error(`Unknown endpoint: ${method} ${path}. Available: ${available}...`);
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
    const responsePromise = this.createResponsePromise(messageId, timeout, method, path);
    this.connectionState.addPendingMessage(messageId, `${method}:${path}`, new Date());

    try {
      // Send message
      const messageBytes = toByteArray(message);
      this.sendBinaryMessage(messageBytes);

      // Wait for response
      const response = await responsePromise;

      // Check response code
      if (response.responseCode !== 0) {
        return ApiResponse.failure<TResponse>(
          createErrorResponse(response.responseCode, messageId, method, path)
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
          createErrorResponse(60, messageId, method, path, 'Failed to deserialize response')
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
   */
  async sendEventAsync<TRequest>(
    method: string,
    path: string,
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
    const serviceGuid = this.getServiceGuid(method, path);
    if (!serviceGuid) {
      throw new Error(`Unknown endpoint: ${method} ${path}`);
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

        ws.on('open', () => resolve(ws));
        ws.on('error', (err: Error) => reject(err));
      });
    }
  }

  private setupMessageHandler(): void {
    if (!this.webSocket) return;

    const handleMessage = (data: ArrayBuffer | Buffer): void => {
      try {
        const bytes = new Uint8Array(data instanceof ArrayBuffer ? data : data.buffer);

        // Check minimum size
        if (bytes.length < RESPONSE_HEADER_SIZE) {
          return;
        }

        const message = parse(bytes);
        this.handleReceivedMessage(message);
      } catch {
        // Ignore malformed messages
      }
    };

    if ('addEventListener' in this.webSocket) {
      // Browser WebSocket
      (this.webSocket as WebSocket).addEventListener('message', (event: MessageEvent) => {
        if (event.data instanceof ArrayBuffer) {
          handleMessage(event.data);
        }
      });
    } else {
      // Node.js 'ws' WebSocket
      (this.webSocket as import('ws').WebSocket).on('message', (data: Buffer) => {
        handleMessage(data);
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
      // Ignore parse errors
    }
  }

  private handleCapabilityManifest(json: string): void {
    try {
      const manifest = JSON.parse(json) as {
        sessionId?: string;
        availableAPIs?: Array<{ endpointKey?: string; serviceGuid?: string }>;
      };

      // Extract session ID
      if (manifest.sessionId) {
        this._sessionId = manifest.sessionId;
      }

      // Extract available APIs
      if (Array.isArray(manifest.availableAPIs)) {
        for (const api of manifest.availableAPIs) {
          if (api.endpointKey && api.serviceGuid) {
            this.apiMappings.set(api.endpointKey, api.serviceGuid);
            this.connectionState?.addServiceMapping(api.endpointKey, api.serviceGuid);
          }
        }
      }

      // Signal that capabilities are received
      if (this.capabilityManifestResolver) {
        this.capabilityManifestResolver(true);
        this.capabilityManifestResolver = null;
      }
    } catch {
      // Ignore manifest parse errors
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
    method: string,
    path: string
  ): Promise<BinaryMessage> {
    return new Promise<BinaryMessage>((resolve, reject) => {
      const timeoutId = setTimeout(() => {
        this.pendingRequests.delete(messageId.toString());
        reject(new Error(`Request to ${method} ${path} timed out after ${timeout}ms`));
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
