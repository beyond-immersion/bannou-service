import { BannouJson, ApiResponse, createErrorResponse } from '@beyondimmersion/bannou-core';
export { ApiResponse, BannouJson, createErrorResponse, fromJson, getErrorName, isBaseClientEvent, mapToHttpStatusCode, toJson } from '@beyondimmersion/bannou-core';

// src/index.ts

// src/IBannouClient.ts
var DisconnectReason = /* @__PURE__ */ ((DisconnectReason2) => {
  DisconnectReason2["ClientDisconnect"] = "client_disconnect";
  DisconnectReason2["ServerClose"] = "server_close";
  DisconnectReason2["NetworkError"] = "network_error";
  DisconnectReason2["SessionExpired"] = "session_expired";
  DisconnectReason2["ServerShutdown"] = "server_shutdown";
  DisconnectReason2["Kicked"] = "kicked";
  return DisconnectReason2;
})(DisconnectReason || {});
var MetaType = /* @__PURE__ */ ((MetaType2) => {
  MetaType2[MetaType2["EndpointInfo"] = 0] = "EndpointInfo";
  MetaType2[MetaType2["RequestSchema"] = 1] = "RequestSchema";
  MetaType2[MetaType2["ResponseSchema"] = 2] = "ResponseSchema";
  MetaType2[MetaType2["FullSchema"] = 3] = "FullSchema";
  return MetaType2;
})(MetaType || {});

// src/ConnectionState.ts
var ConnectionState = class {
  /** Session ID assigned by the server */
  sessionId;
  /** Per-channel sequence numbers for message ordering */
  sequenceNumbers = /* @__PURE__ */ new Map();
  /** Service GUID mappings from capability manifest */
  serviceMappings = /* @__PURE__ */ new Map();
  /** Pending message tracking for debugging */
  pendingMessages = /* @__PURE__ */ new Map();
  constructor(sessionId) {
    this.sessionId = sessionId;
  }
  /**
   * Gets the next sequence number for a channel and increments it.
   */
  getNextSequenceNumber(channel) {
    const current = this.sequenceNumbers.get(channel) ?? 0;
    this.sequenceNumbers.set(channel, current + 1);
    return current;
  }
  /**
   * Gets the current sequence number for a channel without incrementing.
   */
  getCurrentSequenceNumber(channel) {
    return this.sequenceNumbers.get(channel) ?? 0;
  }
  /**
   * Adds a service mapping from endpoint key to GUID.
   */
  addServiceMapping(endpointKey, guid) {
    this.serviceMappings.set(endpointKey, guid);
  }
  /**
   * Gets the service GUID for an endpoint key.
   */
  getServiceGuid(endpointKey) {
    return this.serviceMappings.get(endpointKey);
  }
  /**
   * Gets all service mappings.
   */
  getServiceMappings() {
    return this.serviceMappings;
  }
  /**
   * Clears all service mappings (for capability manifest updates).
   */
  clearServiceMappings() {
    this.serviceMappings.clear();
  }
  /**
   * Tracks a pending message for debugging.
   */
  addPendingMessage(messageId, endpointKey, sentAt) {
    this.pendingMessages.set(messageId, {
      endpointKey,
      sentAt
    });
  }
  /**
   * Removes a pending message after response or timeout.
   */
  removePendingMessage(messageId) {
    this.pendingMessages.delete(messageId);
  }
  /**
   * Gets pending message info for debugging.
   */
  getPendingMessage(messageId) {
    return this.pendingMessages.get(messageId);
  }
  /**
   * Gets count of pending messages.
   */
  getPendingMessageCount() {
    return this.pendingMessages.size;
  }
};

// src/EventSubscription.ts
var EventSubscription = class {
  eventName;
  subscriptionId;
  disposeCallback;
  disposed = false;
  constructor(eventName, subscriptionId, disposeCallback) {
    this.eventName = eventName;
    this.subscriptionId = subscriptionId;
    this.disposeCallback = disposeCallback;
  }
  dispose() {
    if (!this.disposed) {
      this.disposed = true;
      this.disposeCallback(this.subscriptionId);
    }
  }
};

// src/GuidGenerator.ts
var messageIdCounter = 0n;
function generateMessageId() {
  const timestamp = BigInt(Date.now()) << 16n;
  const counter = messageIdCounter++ & 0xffffn;
  return timestamp | counter;
}
function generateUuid() {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = Math.random() * 16 | 0;
    const v = c === "x" ? r : r & 3 | 8;
    return v.toString(16);
  });
}
function resetMessageIdCounter() {
  messageIdCounter = 0n;
}

// src/protocol/MessageFlags.ts
var MessageFlags = {
  /**
   * Default - JSON text, service request, unencrypted, standard priority, expects response
   */
  None: 0,
  /**
   * Message payload is binary data (not JSON)
   */
  Binary: 1,
  /**
   * Message payload is encrypted
   */
  Encrypted: 2,
  /**
   * Message payload is compressed (gzip)
   */
  Compressed: 4,
  /**
   * Deliver at high priority, skip to front of queues
   */
  HighPriority: 8,
  /**
   * Fire-and-forget message, no response expected
   */
  Event: 16,
  /**
   * Route to another WebSocket client (not a Bannou service)
   */
  Client: 32,
  /**
   * Message is a response to an RPC (not a new request)
   */
  Response: 64,
  /**
   * Request metadata about endpoint instead of executing it.
   * When set, the Channel field specifies the meta type:
   * 0 = endpoint-info, 1 = request-schema, 2 = response-schema, 3 = full-schema.
   * Connect service transforms the path and routes to companion endpoints.
   */
  Meta: 128
};
function hasFlag(flags, flag) {
  return (flags & flag) === flag;
}
function isResponse(flags) {
  return hasFlag(flags, MessageFlags.Response);
}
function isEvent(flags) {
  return hasFlag(flags, MessageFlags.Event);
}
function isMeta(flags) {
  return hasFlag(flags, MessageFlags.Meta);
}
function isBinary(flags) {
  return hasFlag(flags, MessageFlags.Binary);
}

// src/protocol/ResponseCodes.ts
var ResponseCodes = {
  /** Request completed successfully. */
  OK: 0,
  /** Generic request error (malformed message, invalid format). */
  RequestError: 10,
  /** Request payload exceeds maximum allowed size. */
  RequestTooLarge: 11,
  /** Rate limit exceeded for this client/session. */
  TooManyRequests: 12,
  /** Invalid channel number in request header. */
  InvalidRequestChannel: 13,
  /** Authentication required but not provided or invalid. */
  Unauthorized: 20,
  /** Target service GUID not found in capability manifest. */
  ServiceNotFound: 30,
  /** Target client GUID not found (for client-to-client messages). */
  ClientNotFound: 31,
  /** Referenced message ID not found. */
  MessageNotFound: 32,
  /** Broadcast not allowed in this connection mode (External mode blocks broadcast). */
  BroadcastNotAllowed: 40,
  /** Service returned 400 Bad Request. */
  Service_BadRequest: 50,
  /** Service returned 404 Not Found. */
  Service_NotFound: 51,
  /** Service returned 401/403 Unauthorized/Forbidden. */
  Service_Unauthorized: 52,
  /** Service returned 409 Conflict. */
  Service_Conflict: 53,
  /** Service returned 500 Internal Server Error. */
  Service_InternalServerError: 60,
  /** Shortcut has expired and is no longer valid. */
  ShortcutExpired: 70,
  /** Shortcut target endpoint no longer exists. */
  ShortcutTargetNotFound: 71,
  /** Shortcut was explicitly revoked. */
  ShortcutRevoked: 72
};
function mapToHttpStatus(code) {
  switch (code) {
    case ResponseCodes.OK:
      return 200;
    case ResponseCodes.RequestError:
    case ResponseCodes.RequestTooLarge:
    case ResponseCodes.InvalidRequestChannel:
    case ResponseCodes.Service_BadRequest:
      return 400;
    case ResponseCodes.Unauthorized:
    case ResponseCodes.Service_Unauthorized:
      return 401;
    case ResponseCodes.TooManyRequests:
      return 429;
    case ResponseCodes.ServiceNotFound:
    case ResponseCodes.ClientNotFound:
    case ResponseCodes.MessageNotFound:
    case ResponseCodes.Service_NotFound:
    case ResponseCodes.ShortcutTargetNotFound:
      return 404;
    case ResponseCodes.Service_Conflict:
      return 409;
    case ResponseCodes.BroadcastNotAllowed:
      return 403;
    case ResponseCodes.ShortcutExpired:
    case ResponseCodes.ShortcutRevoked:
      return 410;
    // Gone
    case ResponseCodes.Service_InternalServerError:
    default:
      return 500;
  }
}
function getResponseCodeName(code) {
  const entries = Object.entries(ResponseCodes);
  for (const [name, value] of entries) {
    if (value === code) {
      return name;
    }
  }
  return "UnknownError";
}
function isSuccess(code) {
  return code === ResponseCodes.OK;
}
function isError(code) {
  return code !== ResponseCodes.OK;
}

// src/protocol/NetworkByteOrder.ts
function writeUInt16(view, offset, value) {
  view.setUint16(offset, value, false);
}
function writeUInt32(view, offset, value) {
  view.setUint32(offset, value, false);
}
function writeUInt64(view, offset, value) {
  const high = Number(value >> 32n);
  const low = Number(value & 0xffffffffn);
  view.setUint32(offset, high, false);
  view.setUint32(offset + 4, low, false);
}
function readUInt16(view, offset) {
  return view.getUint16(offset, false);
}
function readUInt32(view, offset) {
  return view.getUint32(offset, false);
}
function readUInt64(view, offset) {
  const high = BigInt(view.getUint32(offset, false));
  const low = BigInt(view.getUint32(offset + 4, false));
  return high << 32n | low;
}
function writeGuid(view, offset, guid) {
  const cleanGuid = guid.replace(/-/g, "");
  if (cleanGuid.length !== 32) {
    throw new Error(`Invalid GUID format: ${guid}`);
  }
  for (let i = 0; i < 16; i++) {
    const byte = parseInt(cleanGuid.slice(i * 2, i * 2 + 2), 16);
    view.setUint8(offset + i, byte);
  }
}
function readGuid(view, offset) {
  const hexParts = [];
  for (let i = 0; i < 16; i++) {
    hexParts.push(
      view.getUint8(offset + i).toString(16).padStart(2, "0")
    );
  }
  const hex = hexParts.join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20, 32)}`;
}
var EMPTY_GUID = "00000000-0000-0000-0000-000000000000";
function testNetworkByteOrderCompatibility() {
  try {
    const buffer = new ArrayBuffer(32);
    const view = new DataView(buffer);
    writeUInt16(view, 0, 4660);
    writeUInt32(view, 2, 305419896);
    writeUInt64(view, 6, 0x123456789abcdef0n);
    const testGuid = "12345678-1234-5678-9abc-def012345678";
    writeGuid(view, 14, testGuid);
    const readU16 = readUInt16(view, 0);
    const readU32 = readUInt32(view, 2);
    const readU64 = readUInt64(view, 6);
    const readG = readGuid(view, 14);
    return readU16 === 4660 && readU32 === 305419896 && readU64 === 0x123456789abcdef0n && readG === testGuid;
  } catch {
    return false;
  }
}

// src/protocol/BinaryMessage.ts
var HEADER_SIZE = 31;
var RESPONSE_HEADER_SIZE = 16;
function createRequest(flags, channel, sequenceNumber, serviceGuid, messageId, payload) {
  return {
    flags,
    channel,
    sequenceNumber,
    serviceGuid,
    messageId,
    responseCode: 0,
    payload
  };
}
function createResponse(flags, channel, sequenceNumber, messageId, responseCode, payload) {
  return {
    flags: flags | MessageFlags.Response,
    channel,
    sequenceNumber,
    serviceGuid: EMPTY_GUID,
    messageId,
    responseCode,
    payload
  };
}
function getJsonPayload(message) {
  if (hasFlag(message.flags, MessageFlags.Binary)) {
    throw new Error("Cannot get JSON payload from binary message");
  }
  return new TextDecoder().decode(message.payload);
}
function parse(buffer) {
  const bytes = buffer instanceof Uint8Array ? buffer : new Uint8Array(buffer);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (bytes.length < 1) {
    throw new Error("Message too short. Expected at least 1 byte for flags.");
  }
  const flags = view.getUint8(0);
  if (isResponse(flags)) {
    return parseResponse(view, bytes);
  } else {
    return parseRequest(view, bytes);
  }
}
function parseRequest(view, bytes) {
  if (bytes.length < HEADER_SIZE) {
    throw new Error(
      `Request message too short. Expected at least ${HEADER_SIZE} bytes, got ${bytes.length}`
    );
  }
  const flags = view.getUint8(0);
  const channel = readUInt16(view, 1);
  const sequenceNumber = readUInt32(view, 3);
  const serviceGuid = readGuid(view, 7);
  const messageId = readUInt64(view, 23);
  const payload = bytes.slice(HEADER_SIZE);
  return {
    flags,
    channel,
    sequenceNumber,
    serviceGuid,
    messageId,
    responseCode: 0,
    payload
  };
}
function parseResponse(view, bytes) {
  if (bytes.length < RESPONSE_HEADER_SIZE) {
    throw new Error(
      `Response message too short. Expected at least ${RESPONSE_HEADER_SIZE} bytes, got ${bytes.length}`
    );
  }
  const flags = view.getUint8(0);
  const channel = readUInt16(view, 1);
  const sequenceNumber = readUInt32(view, 3);
  const messageId = readUInt64(view, 7);
  const responseCode = view.getUint8(15);
  const payload = bytes.slice(RESPONSE_HEADER_SIZE);
  return {
    flags,
    channel,
    sequenceNumber,
    serviceGuid: EMPTY_GUID,
    messageId,
    responseCode,
    payload
  };
}
function toByteArray(message) {
  if (isResponse(message.flags)) {
    return toResponseByteArray(message);
  } else {
    return toRequestByteArray(message);
  }
}
function toRequestByteArray(message) {
  const totalLength = HEADER_SIZE + message.payload.length;
  const result = new Uint8Array(totalLength);
  const view = new DataView(result.buffer);
  view.setUint8(0, message.flags);
  writeUInt16(view, 1, message.channel);
  writeUInt32(view, 3, message.sequenceNumber);
  writeGuid(view, 7, message.serviceGuid);
  writeUInt64(view, 23, message.messageId);
  if (message.payload.length > 0) {
    result.set(message.payload, HEADER_SIZE);
  }
  return result;
}
function toResponseByteArray(message) {
  const totalLength = RESPONSE_HEADER_SIZE + message.payload.length;
  const result = new Uint8Array(totalLength);
  const view = new DataView(result.buffer);
  view.setUint8(0, message.flags);
  writeUInt16(view, 1, message.channel);
  writeUInt32(view, 3, message.sequenceNumber);
  writeUInt64(view, 7, message.messageId);
  view.setUint8(15, message.responseCode);
  if (message.payload.length > 0) {
    result.set(message.payload, RESPONSE_HEADER_SIZE);
  }
  return result;
}
function expectsResponse(message) {
  return !hasFlag(message.flags, MessageFlags.Event) && !isResponse(message.flags);
}
function isResponse2(message) {
  return isResponse(message.flags);
}
function isClientRouted(message) {
  return hasFlag(message.flags, MessageFlags.Client);
}
function isHighPriority(message) {
  return hasFlag(message.flags, MessageFlags.HighPriority);
}
function isSuccess2(message) {
  return isResponse(message.flags) && message.responseCode === 0;
}
function isError2(message) {
  return isResponse(message.flags) && message.responseCode !== 0;
}
function isMeta2(message) {
  return hasFlag(message.flags, MessageFlags.Meta);
}

// src/BannouClient.ts
var DEFAULT_TIMEOUT_MS = 3e4;
var CAPABILITY_MANIFEST_TIMEOUT_MS = 3e4;
var INTERNAL_AUTHORIZATION_HEADER = "Internal";
var BannouClient = class {
  webSocket = null;
  connectionState = null;
  pendingRequests = /* @__PURE__ */ new Map();
  apiMappings = /* @__PURE__ */ new Map();
  eventHandlers = /* @__PURE__ */ new Map();
  previousCapabilities = /* @__PURE__ */ new Map();
  _accessToken;
  _refreshToken;
  serverBaseUrl;
  connectUrl;
  serviceToken;
  _sessionId;
  _lastError;
  capabilityManifestResolver = null;
  eventHandlerFailedCallback = null;
  disconnectCallback = null;
  errorCallback = null;
  tokenRefreshedCallback = null;
  capabilitiesAddedCallback = null;
  capabilitiesRemovedCallback = null;
  pendingReconnectionToken;
  lastDisconnectInfo;
  /**
   * Current connection state.
   */
  get isConnected() {
    return this.webSocket?.readyState === 1;
  }
  /**
   * Session ID assigned by the server after connection.
   */
  get sessionId() {
    return this._sessionId;
  }
  /**
   * All available API endpoints with their client-salted GUIDs.
   */
  get availableApis() {
    return this.apiMappings;
  }
  /**
   * Current access token (JWT).
   */
  get accessToken() {
    return this._accessToken;
  }
  /**
   * Current refresh token for re-authentication.
   */
  get refreshToken() {
    return this._refreshToken;
  }
  /**
   * Last error message from a failed operation.
   */
  get lastError() {
    return this._lastError;
  }
  /**
   * Set a callback for when event handlers throw exceptions.
   */
  set onEventHandlerFailed(callback) {
    this.eventHandlerFailedCallback = callback;
  }
  /**
   * Set a callback for when the connection is closed.
   */
  set onDisconnect(callback) {
    this.disconnectCallback = callback;
  }
  /**
   * Set a callback for when a connection error occurs.
   */
  set onError(callback) {
    this.errorCallback = callback;
  }
  /**
   * Set a callback for when tokens are refreshed.
   */
  set onTokenRefreshed(callback) {
    this.tokenRefreshedCallback = callback;
  }
  /**
   * Set a callback for when new capabilities are added to the session.
   * Fires once per capability manifest update with all newly added capabilities.
   */
  set onCapabilitiesAdded(callback) {
    this.capabilitiesAddedCallback = callback;
  }
  /**
   * Set a callback for when capabilities are removed from the session.
   * Fires once per capability manifest update with all removed capabilities.
   */
  set onCapabilitiesRemoved(callback) {
    this.capabilitiesRemovedCallback = callback;
  }
  /**
   * Get the last disconnect information (if any).
   */
  get lastDisconnect() {
    return this.lastDisconnectInfo;
  }
  /**
   * Connects to a Bannou server using username/password authentication.
   */
  async connectAsync(serverUrl, email, password) {
    this.serverBaseUrl = serverUrl.replace(/\/$/, "");
    this.serviceToken = void 0;
    const loginResult = await this.loginAsync(email, password);
    if (!loginResult) {
      return false;
    }
    return this.establishWebSocketAsync();
  }
  /**
   * Connects using an existing JWT token.
   */
  async connectWithTokenAsync(serverUrl, accessToken, refreshToken) {
    this.serverBaseUrl = serverUrl.replace(/\/$/, "");
    this._accessToken = accessToken;
    this._refreshToken = refreshToken;
    this.serviceToken = void 0;
    return this.establishWebSocketAsync();
  }
  /**
   * Connects in internal mode using a service token.
   */
  async connectInternalAsync(connectUrl, serviceToken) {
    this.serverBaseUrl = void 0;
    this.connectUrl = connectUrl.replace(/\/$/, "");
    this._accessToken = void 0;
    this._refreshToken = void 0;
    this.serviceToken = serviceToken;
    return this.establishWebSocketAsync(true);
  }
  /**
   * Registers a new account and connects.
   */
  async registerAndConnectAsync(serverUrl, username, email, password) {
    this.serverBaseUrl = serverUrl.replace(/\/$/, "");
    this.serviceToken = void 0;
    const registerResult = await this.registerAsync(username, email, password);
    if (!registerResult) {
      return false;
    }
    return this.establishWebSocketAsync();
  }
  /**
   * Gets the service GUID for a specific API endpoint.
   * @param endpoint API path (e.g., "/account/get")
   */
  getServiceGuid(endpoint) {
    return this.apiMappings.get(endpoint);
  }
  /**
   * Invokes a service method by specifying the API endpoint path.
   * @param endpoint API path (e.g., "/account/get")
   */
  async invokeAsync(endpoint, request, channel = 0, timeout = DEFAULT_TIMEOUT_MS) {
    if (!this.isConnected || !this.webSocket) {
      throw new Error("WebSocket is not connected. Call connectAsync first.");
    }
    if (!this.connectionState) {
      throw new Error("Connection state not initialized.");
    }
    const serviceGuid = this.getServiceGuid(endpoint);
    if (!serviceGuid) {
      const available = Array.from(this.apiMappings.keys()).slice(0, 10).join(", ");
      throw new Error(`Unknown endpoint: ${endpoint}. Available: ${available}...`);
    }
    const jsonPayload = BannouJson.serialize(request);
    const payloadBytes = new TextEncoder().encode(jsonPayload);
    const messageId = generateMessageId();
    const sequenceNumber = this.connectionState.getNextSequenceNumber(channel);
    const message = {
      flags: MessageFlags.None,
      channel,
      sequenceNumber,
      serviceGuid,
      messageId,
      responseCode: 0,
      payload: payloadBytes
    };
    const responsePromise = this.createResponsePromise(messageId, timeout, endpoint);
    this.connectionState.addPendingMessage(messageId, endpoint, /* @__PURE__ */ new Date());
    try {
      const messageBytes = toByteArray(message);
      this.sendBinaryMessage(messageBytes);
      const response = await responsePromise;
      if (response.responseCode !== 0) {
        return ApiResponse.failure(
          createErrorResponse(response.responseCode, messageId, endpoint)
        );
      }
      if (response.payload.length === 0) {
        return ApiResponse.successEmpty();
      }
      const responseJson = getJsonPayload(response);
      const result = BannouJson.deserialize(responseJson);
      if (result === null) {
        return ApiResponse.failure(
          createErrorResponse(60, messageId, endpoint, "Failed to deserialize response")
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
  async sendEventAsync(endpoint, request, channel = 0) {
    if (!this.isConnected || !this.webSocket) {
      throw new Error("WebSocket is not connected. Call connectAsync first.");
    }
    if (!this.connectionState) {
      throw new Error("Connection state not initialized.");
    }
    const serviceGuid = this.getServiceGuid(endpoint);
    if (!serviceGuid) {
      throw new Error(`Unknown endpoint: ${endpoint}`);
    }
    const jsonPayload = BannouJson.serialize(request);
    const payloadBytes = new TextEncoder().encode(jsonPayload);
    const messageId = generateMessageId();
    const sequenceNumber = this.connectionState.getNextSequenceNumber(channel);
    const message = {
      flags: MessageFlags.Event,
      channel,
      sequenceNumber,
      serviceGuid,
      messageId,
      responseCode: 0,
      payload: payloadBytes
    };
    const messageBytes = toByteArray(message);
    this.sendBinaryMessage(messageBytes);
  }
  /**
   * Subscribe to a typed event with automatic deserialization.
   */
  onEvent(eventName, handler) {
    const subscriptionId = generateUuid();
    const wrappedHandler = (json) => {
      try {
        const event = BannouJson.deserialize(json);
        if (event) {
          handler(event);
        }
      } catch (error) {
        this.eventHandlerFailedCallback?.(error);
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
  onRawEvent(eventName, handler) {
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
  removeEventHandlers(eventName) {
    this.eventHandlers.delete(eventName);
  }
  /**
   * Requests metadata about an endpoint instead of executing it.
   * Uses the Meta flag (0x80) which triggers route transformation at Connect service.
   * @param endpoint API path (e.g., "/account/get")
   */
  async getEndpointMetaAsync(endpoint, metaType = 3 /* FullSchema */, timeout = 1e4) {
    if (!this.isConnected || !this.webSocket) {
      throw new Error("WebSocket is not connected. Call connectAsync first.");
    }
    if (!this.connectionState) {
      throw new Error("Connection state not initialized.");
    }
    const serviceGuid = this.getServiceGuid(endpoint);
    if (!serviceGuid) {
      const available = Array.from(this.apiMappings.keys()).slice(0, 10).join(", ");
      throw new Error(`Unknown endpoint: ${endpoint}. Available: ${available}...`);
    }
    const messageId = generateMessageId();
    const sequenceNumber = this.connectionState.getNextSequenceNumber(metaType);
    const message = {
      flags: MessageFlags.Meta,
      // Meta flag triggers route transformation
      channel: metaType,
      // Meta type in Channel (0=info, 1=req, 2=resp, 3=full)
      sequenceNumber,
      serviceGuid,
      messageId,
      responseCode: 0,
      payload: new Uint8Array(0)
      // EMPTY - Connect never reads payloads
    };
    const responsePromise = this.createResponsePromise(messageId, timeout, `META:${endpoint}`);
    this.connectionState.addPendingMessage(messageId, `META:${endpoint}`, /* @__PURE__ */ new Date());
    try {
      const messageBytes = toByteArray(message);
      this.sendBinaryMessage(messageBytes);
      const response = await responsePromise;
      if (response.responseCode !== 0) {
        throw new Error(`Meta request failed with response code ${response.responseCode}`);
      }
      const responseJson = getJsonPayload(response);
      const result = BannouJson.deserialize(responseJson);
      if (result === null) {
        throw new Error("Failed to deserialize meta response");
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
  async getEndpointInfoAsync(endpoint) {
    return this.getEndpointMetaAsync(endpoint, 0 /* EndpointInfo */);
  }
  /**
   * Gets JSON Schema for the request body of an endpoint.
   * @param endpoint API path (e.g., "/account/get")
   */
  async getRequestSchemaAsync(endpoint) {
    return this.getEndpointMetaAsync(endpoint, 1 /* RequestSchema */);
  }
  /**
   * Gets JSON Schema for the response body of an endpoint.
   * @param endpoint API path (e.g., "/account/get")
   */
  async getResponseSchemaAsync(endpoint) {
    return this.getEndpointMetaAsync(endpoint, 2 /* ResponseSchema */);
  }
  /**
   * Gets full schema including info, request schema, and response schema.
   * @param endpoint API path (e.g., "/account/get")
   */
  async getFullSchemaAsync(endpoint) {
    return this.getEndpointMetaAsync(endpoint, 3 /* FullSchema */);
  }
  /**
   * Reconnects using a reconnection token from a DisconnectNotificationEvent.
   */
  async reconnectWithTokenAsync(reconnectionToken) {
    if (!this.serverBaseUrl && !this.connectUrl) {
      this._lastError = "No server URL available for reconnection";
      return false;
    }
    this.pendingReconnectionToken = reconnectionToken;
    try {
      return await this.establishWebSocketAsync();
    } finally {
      this.pendingReconnectionToken = void 0;
    }
  }
  /**
   * Refreshes the access token using the stored refresh token.
   */
  async refreshAccessTokenAsync() {
    if (!this._refreshToken) {
      this._lastError = "No refresh token available";
      return false;
    }
    if (!this.serverBaseUrl) {
      this._lastError = "No server URL available for token refresh";
      return false;
    }
    const refreshUrl = `${this.serverBaseUrl}/auth/refresh`;
    try {
      const response = await fetch(refreshUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ refreshToken: this._refreshToken })
      });
      if (!response.ok) {
        const errorContent = await response.text();
        this._lastError = `Token refresh failed (${response.status}): ${errorContent}`;
        return false;
      }
      const result = await response.json();
      if (!result.accessToken) {
        this._lastError = "Token refresh response missing access token";
        return false;
      }
      this._accessToken = result.accessToken;
      if (result.refreshToken) {
        this._refreshToken = result.refreshToken;
      }
      this.tokenRefreshedCallback?.(this._accessToken, this._refreshToken);
      return true;
    } catch (error) {
      this._lastError = `Token refresh exception: ${error.message}`;
      return false;
    }
  }
  /**
   * Disconnects from the server.
   */
  async disconnectAsync() {
    for (const [, pending] of this.pendingRequests) {
      clearTimeout(pending.timeoutId);
      pending.reject(new Error("Disconnecting"));
    }
    this.pendingRequests.clear();
    if (this.webSocket) {
      try {
        this.webSocket.close(1e3, "Client disconnecting");
      } catch {
      }
    }
    this.webSocket = null;
    this.connectionState = null;
    this._accessToken = void 0;
    this._refreshToken = void 0;
    this.serviceToken = void 0;
    this.apiMappings.clear();
    this.previousCapabilities.clear();
  }
  // Private methods
  buildAuthorizationHeader(allowAnonymousInternal) {
    if (this._accessToken) {
      return `Bearer ${this._accessToken}`;
    }
    if (this.serviceToken || allowAnonymousInternal) {
      return INTERNAL_AUTHORIZATION_HEADER;
    }
    return "";
  }
  async loginAsync(email, password) {
    const loginUrl = `${this.serverBaseUrl}/auth/login`;
    try {
      const response = await fetch(loginUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password })
      });
      if (!response.ok) {
        const errorContent = await response.text();
        this._lastError = `Login failed (${response.status}): ${errorContent}`;
        return false;
      }
      const result = await response.json();
      if (!result.accessToken) {
        this._lastError = "Login response missing access token";
        return false;
      }
      this._accessToken = result.accessToken;
      this._refreshToken = result.refreshToken;
      this.connectUrl = result.connectUrl;
      return true;
    } catch (error) {
      this._lastError = `Login exception: ${error.message}`;
      return false;
    }
  }
  async registerAsync(username, email, password) {
    const registerUrl = `${this.serverBaseUrl}/auth/register`;
    try {
      const response = await fetch(registerUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ username, email, password })
      });
      if (!response.ok) {
        const errorContent = await response.text();
        this._lastError = `Registration failed (${response.status}): ${errorContent}`;
        return false;
      }
      const result = await response.json();
      if (!result.accessToken) {
        this._lastError = "Registration response missing access token";
        return false;
      }
      this._accessToken = result.accessToken;
      this._refreshToken = result.refreshToken;
      this.connectUrl = result.connectUrl;
      return true;
    } catch (error) {
      this._lastError = `Registration exception: ${error.message}`;
      return false;
    }
  }
  async establishWebSocketAsync(allowAnonymousInternal = false) {
    if (!this._accessToken && !this.serviceToken && !allowAnonymousInternal) {
      this._lastError = "No access token available for WebSocket connection";
      return false;
    }
    let wsUrl;
    if (this.connectUrl) {
      wsUrl = this.connectUrl.replace("https://", "wss://").replace("http://", "ws://");
    } else if (this.serverBaseUrl) {
      wsUrl = this.serverBaseUrl.replace("https://", "wss://").replace("http://", "ws://");
      if (!wsUrl.endsWith("/connect")) {
        wsUrl = `${wsUrl}/connect`;
      }
    } else {
      this._lastError = "No server URL available";
      return false;
    }
    try {
      this.webSocket = await this.createWebSocket(wsUrl, allowAnonymousInternal);
    } catch (error) {
      this._lastError = `WebSocket connection failed to ${wsUrl}: ${error.message}`;
      return false;
    }
    const sessionId = generateUuid();
    this.connectionState = new ConnectionState(sessionId);
    this.setupMessageHandler();
    const manifestReceived = await this.waitForCapabilityManifest();
    if (!manifestReceived) {
      console.warn("Capability manifest not received within timeout");
    }
    return true;
  }
  async createWebSocket(url, allowAnonymousInternal) {
    const authHeader = this.buildAuthorizationHeader(allowAnonymousInternal);
    if (typeof window !== "undefined" && typeof window.WebSocket !== "undefined") {
      return new Promise((resolve, reject) => {
        let authUrl = url;
        if (authHeader && authHeader.startsWith("Bearer ")) {
          const token = authHeader.replace("Bearer ", "");
          const separator = url.includes("?") ? "&" : "?";
          authUrl = `${url}${separator}token=${encodeURIComponent(token)}`;
        }
        const ws = new WebSocket(authUrl);
        ws.binaryType = "arraybuffer";
        ws.onopen = () => resolve(ws);
        ws.onerror = () => reject(new Error("WebSocket connection failed"));
      });
    } else {
      const WebSocketModule = await import('ws');
      const WS = WebSocketModule.default || WebSocketModule.WebSocket || WebSocketModule;
      return new Promise((resolve, reject) => {
        const headers = {};
        if (authHeader) {
          headers["Authorization"] = authHeader;
        }
        if (this.serviceToken) {
          headers["X-Service-Token"] = this.serviceToken;
        }
        const ws = new WS(url, { headers });
        console.error(`[DEBUG] createWebSocket: created WebSocket to ${url}`);
        const earlyMessages = [];
        const earlyMessageHandler = (data) => {
          earlyMessages.push(data);
        };
        ws.on("message", earlyMessageHandler);
        const wsWithEarly = ws;
        wsWithEarly._earlyMessages = earlyMessages;
        wsWithEarly._earlyMessageHandler = earlyMessageHandler;
        ws.on("open", () => {
          resolve(ws);
        });
        ws.on("error", (err) => {
          reject(err);
        });
      });
    }
  }
  setupMessageHandler() {
    if (!this.webSocket) return;
    const handleMessage = (data) => {
      try {
        let bytes;
        if (data instanceof ArrayBuffer) {
          bytes = new Uint8Array(data);
        } else {
          bytes = new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
        }
        if (bytes.length < RESPONSE_HEADER_SIZE) {
          return;
        }
        const message = parse(bytes);
        this.handleReceivedMessage(message);
      } catch {
      }
    };
    const handleClose = (code, reason) => {
      let disconnectReason = "server_close" /* ServerClose */;
      let canReconnect = false;
      if (code === 1e3) {
        disconnectReason = "client_disconnect" /* ClientDisconnect */;
      } else if (code === 1001) {
        disconnectReason = "server_shutdown" /* ServerShutdown */;
        canReconnect = true;
      } else if (code >= 4e3 && code < 5e3) {
        if (code === 4001) {
          disconnectReason = "session_expired" /* SessionExpired */;
        } else if (code === 4002) {
          disconnectReason = "kicked" /* Kicked */;
        } else {
          disconnectReason = "server_close" /* ServerClose */;
          canReconnect = true;
        }
      } else if (code >= 1002 && code <= 1015) {
        disconnectReason = "network_error" /* NetworkError */;
        canReconnect = true;
      }
      const reconnectionToken = this.pendingReconnectionToken;
      if (reconnectionToken) {
        canReconnect = true;
      }
      const info = {
        reason: disconnectReason,
        message: reason || void 0,
        reconnectionToken,
        canReconnect,
        closeCode: code
      };
      this.lastDisconnectInfo = info;
      this.disconnectCallback?.(info);
    };
    const handleError = (error) => {
      const err = error instanceof Error ? error : new Error("WebSocket error");
      this.errorCallback?.(err);
    };
    const isBrowser = typeof window !== "undefined" && typeof window.WebSocket !== "undefined";
    if (isBrowser) {
      const browserWs = this.webSocket;
      browserWs.addEventListener("message", (event) => {
        if (event.data instanceof ArrayBuffer) {
          handleMessage(event.data);
        }
      });
      browserWs.addEventListener("close", (event) => {
        handleClose(event.code, event.reason);
      });
      browserWs.addEventListener("error", (event) => {
        handleError(event);
      });
    } else {
      const nodeWs = this.webSocket;
      console.error(`[DEBUG] setupMessageHandler: Node.js WebSocket detected`);
      const wsWithEarly = nodeWs;
      const earlyHandler = wsWithEarly._earlyMessageHandler;
      console.error(`[DEBUG] setupMessageHandler: earlyHandler exists=${!!earlyHandler}`);
      if (earlyHandler) {
        nodeWs.off("message", earlyHandler);
        wsWithEarly._earlyMessageHandler = void 0;
      }
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
      wsWithEarly._earlyMessages = void 0;
      nodeWs.on("message", (data) => {
        console.error(`[DEBUG] setupMessageHandler: received live message, length=${data.length}`);
        handleMessage(data);
      });
      nodeWs.on("close", (code, reason) => {
        handleClose(code, reason.toString());
      });
      nodeWs.on("error", (error) => {
        handleError(error);
      });
    }
  }
  handleReceivedMessage(message) {
    if (isResponse2(message)) {
      const pending = this.pendingRequests.get(message.messageId.toString());
      if (pending) {
        clearTimeout(pending.timeoutId);
        pending.resolve(message);
      }
    } else if ((message.flags & MessageFlags.Event) !== 0) {
      this.handleEventMessage(message);
    }
  }
  handleEventMessage(message) {
    if (message.payload.length === 0) {
      return;
    }
    try {
      const payloadJson = new TextDecoder().decode(message.payload);
      const parsed = JSON.parse(payloadJson);
      const eventName = parsed.eventName;
      if (!eventName) {
        return;
      }
      if (eventName === "connect.capability_manifest") {
        this.handleCapabilityManifest(payloadJson);
      }
      if (eventName === "connect.disconnect_notification") {
        this.handleDisconnectNotification(payloadJson);
      }
      const handlers = this.eventHandlers.get(eventName);
      if (handlers) {
        for (const { handler } of handlers) {
          try {
            handler(payloadJson);
          } catch (error) {
            this.eventHandlerFailedCallback?.(error);
          }
        }
      }
    } catch {
    }
  }
  handleCapabilityManifest(json) {
    try {
      const manifest = JSON.parse(json);
      if (manifest.sessionId) {
        this._sessionId = manifest.sessionId;
      }
      const currentCapabilities = /* @__PURE__ */ new Map();
      if (Array.isArray(manifest.availableApis)) {
        for (const api of manifest.availableApis) {
          if (api.endpoint && api.serviceId && api.service) {
            const entry = {
              serviceId: api.serviceId,
              endpoint: api.endpoint,
              service: api.service,
              description: api.description
            };
            currentCapabilities.set(api.endpoint, entry);
            this.apiMappings.set(api.endpoint, api.serviceId);
            this.connectionState?.addServiceMapping(api.endpoint, api.serviceId);
          }
        }
      }
      const added = [];
      for (const [key, value] of currentCapabilities) {
        if (!this.previousCapabilities.has(key)) {
          added.push(value);
        }
      }
      const removed = [];
      for (const [key, value] of this.previousCapabilities) {
        if (!currentCapabilities.has(key)) {
          removed.push(value);
          this.apiMappings.delete(key);
        }
      }
      this.previousCapabilities.clear();
      for (const [key, value] of currentCapabilities) {
        this.previousCapabilities.set(key, value);
      }
      if (added.length > 0 && this.capabilitiesAddedCallback) {
        this.capabilitiesAddedCallback(added);
      }
      if (removed.length > 0 && this.capabilitiesRemovedCallback) {
        this.capabilitiesRemovedCallback(removed);
      }
      if (this.capabilityManifestResolver) {
        this.capabilityManifestResolver(true);
        this.capabilityManifestResolver = null;
      }
    } catch {
    }
  }
  handleDisconnectNotification(json) {
    try {
      const notification = JSON.parse(json);
      if (notification.reconnectionToken) {
        this.pendingReconnectionToken = notification.reconnectionToken;
      }
      let reason = "server_close" /* ServerClose */;
      if (notification.reason === "session_expired") {
        reason = "session_expired" /* SessionExpired */;
      } else if (notification.reason === "kicked") {
        reason = "kicked" /* Kicked */;
      } else if (notification.reason === "server_shutdown") {
        reason = "server_shutdown" /* ServerShutdown */;
      }
      this.lastDisconnectInfo = {
        reason,
        message: notification.message,
        reconnectionToken: notification.reconnectionToken,
        canReconnect: !!notification.reconnectionToken
      };
    } catch {
    }
  }
  waitForCapabilityManifest() {
    return new Promise((resolve) => {
      this.capabilityManifestResolver = resolve;
      setTimeout(() => {
        if (this.capabilityManifestResolver) {
          this.capabilityManifestResolver(false);
          this.capabilityManifestResolver = null;
        }
      }, CAPABILITY_MANIFEST_TIMEOUT_MS);
    });
  }
  createResponsePromise(messageId, timeout, endpoint) {
    return new Promise((resolve, reject) => {
      const timeoutId = setTimeout(() => {
        this.pendingRequests.delete(messageId.toString());
        reject(new Error(`Request to ${endpoint} timed out after ${timeout}ms`));
      }, timeout);
      this.pendingRequests.set(messageId.toString(), {
        resolve,
        reject,
        timeoutId
      });
    });
  }
  sendBinaryMessage(data) {
    if (!this.webSocket) {
      throw new Error("WebSocket not connected");
    }
    if ("send" in this.webSocket) {
      if (typeof window !== "undefined") {
        this.webSocket.send(data);
      } else {
        this.webSocket.send(data);
      }
    }
  }
};

// src/Generated/proxies/AccountProxy.ts
var AccountProxy = class {
  client;
  /**
   * Creates a new AccountProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Update account profile
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async updateProfileAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/account/profile/update",
      request,
      channel,
      timeout
    );
  }
  /**
   * Update account password hash
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async updatePasswordHashEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/account/password/update",
      request,
      channel
    );
  }
  /**
   * Update email verification status
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async updateVerificationStatusEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/account/verification/update",
      request,
      channel
    );
  }
};

// src/Generated/proxies/AchievementProxy.ts
var AchievementProxy = class {
  client;
  /**
   * Creates a new AchievementProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Create a new achievement definition
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createAchievementDefinitionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/achievement/definition/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * List achievement definitions
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listAchievementDefinitionsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/achievement/definition/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Update achievement definition
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async updateAchievementDefinitionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/achievement/definition/update",
      request,
      channel,
      timeout
    );
  }
  /**
   * Delete achievement definition
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async deleteAchievementDefinitionEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/achievement/definition/delete",
      request,
      channel
    );
  }
  /**
   * Get entity's achievement progress
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getAchievementProgressAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/achievement/progress/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * List unlocked achievements
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listUnlockedAchievementsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/achievement/list-unlocked",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/ActorProxy.ts
var ActorProxy = class {
  client;
  /**
   * Creates a new ActorProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Create an actor template (category definition)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createActorTemplateAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/actor/template/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * Update an actor template
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async updateActorTemplateAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/actor/template/update",
      request,
      channel,
      timeout
    );
  }
  /**
   * Delete an actor template
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async deleteActorTemplateAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/actor/template/delete",
      request,
      channel,
      timeout
    );
  }
  /**
   * Spawn a new actor from a template
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async spawnActorAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/actor/spawn",
      request,
      channel,
      timeout
    );
  }
  /**
   * Stop a running actor
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async stopActorAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/actor/stop",
      request,
      channel,
      timeout
    );
  }
  /**
   * Inject a perception event into an actor's queue (testing)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async injectPerceptionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/actor/inject-perception",
      request,
      channel,
      timeout
    );
  }
  /**
   * Start an encounter managed by an Event Brain actor
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async startEncounterEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/actor/encounter/start",
      request,
      channel
    );
  }
  /**
   * Update the phase of an active encounter
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async updateEncounterPhaseAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/actor/encounter/update-phase",
      request,
      channel,
      timeout
    );
  }
  /**
   * End an active encounter
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async endEncounterAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/actor/encounter/end",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/AssetsProxy.ts
var AssetsProxy = class {
  client;
  /**
   * Creates a new AssetsProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Request upload URL for a new asset
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async requestUploadAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/assets/upload/request",
      request,
      channel,
      timeout
    );
  }
  /**
   * Mark upload as complete, trigger processing
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async completeUploadAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/assets/upload/complete",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get asset metadata and download URL
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getAssetAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/assets/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * List all versions of an asset
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listAssetVersionsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/assets/list-versions",
      request,
      channel,
      timeout
    );
  }
  /**
   * Search assets by tags, type, or realm
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async searchAssetsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/assets/search",
      request,
      channel,
      timeout
    );
  }
  /**
   * Batch asset metadata lookup
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async bulkGetAssetsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/assets/bulk-get",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/AuthProxy.ts
var AuthProxy = class {
  client;
  /**
   * Creates a new AuthProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Login with email/password
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async loginAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/auth/login",
      request,
      channel,
      timeout
    );
  }
  /**
   * Register new user account
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async registerAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/auth/register",
      request,
      channel,
      timeout
    );
  }
  /**
   * Complete OAuth2 flow (browser redirect callback)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async completeOAuthAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/auth/oauth/{provider}/callback",
      request,
      channel,
      timeout
    );
  }
  /**
   * Verify Steam Session Ticket
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async verifySteamAuthAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/auth/steam/verify",
      request,
      channel,
      timeout
    );
  }
  /**
   * Refresh access token
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async refreshTokenAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/auth/refresh",
      request,
      channel,
      timeout
    );
  }
  /**
   * Validate access token
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async validateTokenAsync(channel = 0, timeout) {
    return this.client.invokeAsync(
      "/auth/validate",
      {},
      channel,
      timeout
    );
  }
  /**
   * Logout and invalidate tokens
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async logoutEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/auth/logout",
      request,
      channel
    );
  }
  /**
   * Get active sessions for account
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getSessionsAsync(channel = 0, timeout) {
    return this.client.invokeAsync(
      "/auth/sessions/list",
      {},
      channel,
      timeout
    );
  }
  /**
   * Terminate specific session
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async terminateSessionEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/auth/sessions/terminate",
      request,
      channel
    );
  }
  /**
   * Request password reset
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async requestPasswordResetEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/auth/password/reset",
      request,
      channel
    );
  }
  /**
   * Confirm password reset with token
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async confirmPasswordResetEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/auth/password/confirm",
      request,
      channel
    );
  }
  /**
   * List available authentication providers
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listProvidersAsync(channel = 0, timeout) {
    return this.client.invokeAsync(
      "/auth/providers",
      {},
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/BundlesProxy.ts
var BundlesProxy = class {
  client;
  /**
   * Creates a new BundlesProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Create asset bundle from multiple assets
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createBundleAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/bundles/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get bundle manifest and download URL
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getBundleAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/bundles/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Request upload URL for a pre-made bundle
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async requestBundleUploadAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/bundles/upload/request",
      request,
      channel,
      timeout
    );
  }
  /**
   * Create metabundle from source bundles
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createMetabundleAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/bundles/metabundle/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get async metabundle job status
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getJobStatusAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/bundles/job/status",
      request,
      channel,
      timeout
    );
  }
  /**
   * Cancel an async metabundle job
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async cancelJobAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/bundles/job/cancel",
      request,
      channel,
      timeout
    );
  }
  /**
   * Compute optimal bundles for requested assets
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async resolveBundlesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/bundles/resolve",
      request,
      channel,
      timeout
    );
  }
  /**
   * Find all bundles containing a specific asset
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async queryBundlesByAssetAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/bundles/query/by-asset",
      request,
      channel,
      timeout
    );
  }
  /**
   * Update bundle metadata
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async updateBundleAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/bundles/update",
      request,
      channel,
      timeout
    );
  }
  /**
   * Soft-delete a bundle
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async deleteBundleAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/bundles/delete",
      request,
      channel,
      timeout
    );
  }
  /**
   * Restore a soft-deleted bundle
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async restoreBundleAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/bundles/restore",
      request,
      channel,
      timeout
    );
  }
  /**
   * Query bundles with advanced filters
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async queryBundlesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/bundles/query",
      request,
      channel,
      timeout
    );
  }
  /**
   * List version history for a bundle
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listBundleVersionsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/bundles/list-versions",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/CacheProxy.ts
var CacheProxy = class {
  client;
  /**
   * Creates a new CacheProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get cached compiled behavior
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getCachedBehaviorAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/cache/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Invalidate cached behavior
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async invalidateCachedBehaviorEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/cache/invalidate",
      request,
      channel
    );
  }
};

// src/Generated/proxies/CharacterProxy.ts
var CharacterProxy = class {
  client;
  /**
   * Creates a new CharacterProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get character by ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getCharacterAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * List characters with filtering
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listCharactersAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get character with optional related data (personality, backstory, family)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getEnrichedCharacterAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character/get-enriched",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get compressed archive data for a character
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getCharacterArchiveAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character/get-archive",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get all characters in a realm (primary query pattern)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getCharactersByRealmAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character/by-realm",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/CharacterEncounterProxy.ts
var CharacterEncounterProxy = class {
  client;
  /**
   * Creates a new CharacterEncounterProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get encounter type by code
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getEncounterTypeAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character-encounter/type/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * List all encounter types
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listEncounterTypesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character-encounter/type/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get character's encounters (paginated)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async queryByCharacterAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character-encounter/query/by-character",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get encounters between two characters
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async queryBetweenAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character-encounter/query/between",
      request,
      channel,
      timeout
    );
  }
  /**
   * Recent encounters at location
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async queryByLocationAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character-encounter/query/by-location",
      request,
      channel,
      timeout
    );
  }
  /**
   * Quick check if two characters have met
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async hasMetAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character-encounter/has-met",
      request,
      channel,
      timeout
    );
  }
  /**
   * Aggregate sentiment toward another character
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getSentimentAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character-encounter/get-sentiment",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get character's view of encounter
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getPerspectiveAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character-encounter/get-perspective",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/CharacterHistoryProxy.ts
var CharacterHistoryProxy = class {
  client;
  /**
   * Creates a new CharacterHistoryProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get all historical events a character participated in
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getParticipationAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character-history/get-participation",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get all characters who participated in a historical event
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getEventParticipantsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character-history/get-event-participants",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get machine-readable backstory elements for behavior system
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getBackstoryAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character-history/get-backstory",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/CharacterPersonalityProxy.ts
var CharacterPersonalityProxy = class {
  client;
  /**
   * Creates a new CharacterPersonalityProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get personality for a character
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getPersonalityAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character-personality/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get combat preferences for a character
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getCombatPreferencesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/character-personality/get-combat",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/ClientCapabilitiesProxy.ts
var ClientCapabilitiesProxy = class {
  client;
  /**
   * Creates a new ClientCapabilitiesProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get client capability manifest (GUID → API mappings)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getClientCapabilitiesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/client-capabilities",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/CompileProxy.ts
var CompileProxy = class {
  client;
  /**
   * Creates a new CompileProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Compile ABML behavior definition
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async compileAbmlBehaviorAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/compile",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/ContractProxy.ts
var ContractProxy = class {
  client;
  /**
   * Creates a new ContractProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get template by ID or code
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getContractTemplateAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/template/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * List templates with filters
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listContractTemplatesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/template/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Create contract instance from template
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createContractInstanceAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/instance/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * Propose contract to parties (starts consent flow)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async proposeContractInstanceAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/instance/propose",
      request,
      channel,
      timeout
    );
  }
  /**
   * Party consents to contract
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async consentToContractAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/instance/consent",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get instance by ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getContractInstanceAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/instance/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Query instances by party, template, status
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async queryContractInstancesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/instance/query",
      request,
      channel,
      timeout
    );
  }
  /**
   * Request early termination
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async terminateContractInstanceAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/instance/terminate",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get current status and milestone progress
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getContractInstanceStatusAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/instance/get-status",
      request,
      channel,
      timeout
    );
  }
  /**
   * External system reports milestone completed
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async completeMilestoneAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/milestone/complete",
      request,
      channel,
      timeout
    );
  }
  /**
   * External system reports milestone failed
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async failMilestoneAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/milestone/fail",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get milestone details and status
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getMilestoneAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/milestone/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Report a contract breach
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async reportBreachAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/breach/report",
      request,
      channel,
      timeout
    );
  }
  /**
   * Mark breach as cured (system/admin action)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async cureBreachAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/breach/cure",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get breach details
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getBreachAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/breach/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Update game metadata on instance
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async updateContractMetadataAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/metadata/update",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get game metadata
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getContractMetadataAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/metadata/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Check if entity can take action given contracts
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async checkContractConstraintAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/check-constraint",
      request,
      channel,
      timeout
    );
  }
  /**
   * Query active contracts for entity
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async queryActiveContractsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/query-active",
      request,
      channel,
      timeout
    );
  }
  /**
   * Lock contract under guardian custody
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async lockContractAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/lock",
      request,
      channel,
      timeout
    );
  }
  /**
   * Unlock contract from guardian custody
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async unlockContractAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/unlock",
      request,
      channel,
      timeout
    );
  }
  /**
   * Transfer party role to new entity
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async transferContractPartyAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/transfer-party",
      request,
      channel,
      timeout
    );
  }
  /**
   * List all registered clause types
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listClauseTypesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/clause-type/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Set template values on contract instance
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async setContractTemplateValuesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/instance/set-template-values",
      request,
      channel,
      timeout
    );
  }
  /**
   * Check if asset requirement clauses are satisfied
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async checkAssetRequirementsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/instance/check-asset-requirements",
      request,
      channel,
      timeout
    );
  }
  /**
   * Execute all contract clauses (idempotent)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async executeContractAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/contract/instance/execute",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/CurrencyProxy.ts
var CurrencyProxy = class {
  client;
  /**
   * Creates a new CurrencyProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get currency definition by ID or code
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getCurrencyDefinitionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/definition/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * List currency definitions with filters
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listCurrencyDefinitionsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/definition/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Create a new wallet for an owner
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createWalletAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/wallet/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get wallet by ID or owner
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getWalletAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/wallet/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get existing wallet or create if not exists
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getOrCreateWalletAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/wallet/get-or-create",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get balance for a specific currency in a wallet
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getBalanceAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/balance/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get multiple balances in one call
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async batchGetBalancesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/balance/batch-get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Credit currency to a wallet (faucet operation)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async creditCurrencyAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/credit",
      request,
      channel,
      timeout
    );
  }
  /**
   * Debit currency from a wallet (sink operation)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async debitCurrencyAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/debit",
      request,
      channel,
      timeout
    );
  }
  /**
   * Transfer currency between wallets
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async transferCurrencyAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/transfer",
      request,
      channel,
      timeout
    );
  }
  /**
   * Credit multiple wallets in one call
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async batchCreditCurrencyAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/batch-credit",
      request,
      channel,
      timeout
    );
  }
  /**
   * Calculate conversion without executing
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async calculateConversionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/convert/calculate",
      request,
      channel,
      timeout
    );
  }
  /**
   * Execute currency conversion in a wallet
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async executeConversionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/convert/execute",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get exchange rate between two currencies
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getExchangeRateAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/exchange-rate/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get a transaction by ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getTransactionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/transaction/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get paginated transaction history for a wallet
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getTransactionHistoryAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/transaction/history",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get transactions by reference type and ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getTransactionsByReferenceAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/transaction/by-reference",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get global supply statistics for a currency
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getGlobalSupplyAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/stats/global-supply",
      request,
      channel,
      timeout
    );
  }
  /**
   * Debit wallet for escrow deposit
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async escrowDepositAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/escrow/deposit",
      request,
      channel,
      timeout
    );
  }
  /**
   * Credit recipient on escrow completion
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async escrowReleaseAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/escrow/release",
      request,
      channel,
      timeout
    );
  }
  /**
   * Credit depositor on escrow refund
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async escrowRefundAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/escrow/refund",
      request,
      channel,
      timeout
    );
  }
  /**
   * Create an authorization hold (reserve funds)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createHoldAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/hold/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * Capture held funds (debit final amount)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async captureHoldAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/hold/capture",
      request,
      channel,
      timeout
    );
  }
  /**
   * Release held funds (make available again)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async releaseHoldAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/hold/release",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get hold status and details
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getHoldAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/currency/hold/get",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/DocumentationProxy.ts
var DocumentationProxy = class {
  client;
  /**
   * Creates a new DocumentationProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Natural language documentation search
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async queryDocumentationAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/documentation/query",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get specific document by ID or slug
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getDocumentAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/documentation/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Full-text keyword search
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async searchDocumentationAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/documentation/search",
      request,
      channel,
      timeout
    );
  }
  /**
   * List documents by category
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listDocumentsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/documentation/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get related topics and follow-up suggestions
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async suggestRelatedTopicsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/documentation/suggest",
      request,
      channel,
      timeout
    );
  }
  /**
   * Bind a git repository to a documentation namespace
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async bindRepositoryAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/documentation/repo/bind",
      request,
      channel,
      timeout
    );
  }
  /**
   * Manually trigger repository sync
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async syncRepositoryAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/documentation/repo/sync",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get repository binding status
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getRepositoryStatusAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/documentation/repo/status",
      request,
      channel,
      timeout
    );
  }
  /**
   * List all repository bindings
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listRepositoryBindingsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/documentation/repo/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Update repository binding configuration
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async updateRepositoryBindingAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/documentation/repo/update",
      request,
      channel,
      timeout
    );
  }
  /**
   * Create documentation archive
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createDocumentationArchiveAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/documentation/repo/archive/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * List documentation archives
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listDocumentationArchivesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/documentation/repo/archive/list",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/EscrowProxy.ts
var EscrowProxy = class {
  client;
  /**
   * Creates a new EscrowProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Create a new escrow agreement
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createEscrowAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/escrow/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get escrow details
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getEscrowAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/escrow/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * List escrows for a party
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listEscrowsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/escrow/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Deposit assets into escrow
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async depositAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/escrow/deposit",
      request,
      channel,
      timeout
    );
  }
  /**
   * Validate a deposit without executing
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async validateDepositAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/escrow/deposit/validate",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get deposit status for a party
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getDepositStatusAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/escrow/deposit/status",
      request,
      channel,
      timeout
    );
  }
  /**
   * Record party consent
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async recordConsentAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/escrow/consent",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get consent status for escrow
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getConsentStatusAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/escrow/consent/status",
      request,
      channel,
      timeout
    );
  }
  /**
   * Trigger release
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async releaseAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/escrow/release",
      request,
      channel,
      timeout
    );
  }
  /**
   * Trigger refund
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async refundAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/escrow/refund",
      request,
      channel,
      timeout
    );
  }
  /**
   * Cancel escrow before fully funded
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async cancelAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/escrow/cancel",
      request,
      channel,
      timeout
    );
  }
  /**
   * Raise a dispute on funded escrow
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async disputeAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/escrow/dispute",
      request,
      channel,
      timeout
    );
  }
  /**
   * Arbiter resolves disputed escrow
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async resolveAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/escrow/resolve",
      request,
      channel,
      timeout
    );
  }
  /**
   * Verify condition for conditional escrow
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async verifyConditionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/escrow/verify-condition",
      request,
      channel,
      timeout
    );
  }
  /**
   * Re-affirm after validation failure
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async reaffirmAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/escrow/reaffirm",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/GameServiceProxy.ts
var GameServiceProxy = class {
  client;
  /**
   * Creates a new GameServiceProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * List all registered game services
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listServicesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/game-service/services/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get service by ID or stub name
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getServiceAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/game-service/services/get",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/GoapProxy.ts
var GoapProxy = class {
  client;
  /**
   * Creates a new GoapProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Generate GOAP plan
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async generateGoapPlanAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/goap/plan",
      request,
      channel,
      timeout
    );
  }
  /**
   * Validate existing GOAP plan
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async validateGoapPlanAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/goap/validate-plan",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/InventoryProxy.ts
var InventoryProxy = class {
  client;
  /**
   * Creates a new InventoryProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Create a new container
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createContainerAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/inventory/container/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get container with contents
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getContainerAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/inventory/container/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get container or create if not exists
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getOrCreateContainerAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/inventory/container/get-or-create",
      request,
      channel,
      timeout
    );
  }
  /**
   * List containers for owner
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listContainersAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/inventory/container/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Update container properties
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async updateContainerAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/inventory/container/update",
      request,
      channel,
      timeout
    );
  }
  /**
   * Add item to container
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async addItemToContainerAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/inventory/add",
      request,
      channel,
      timeout
    );
  }
  /**
   * Remove item from container
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async removeItemFromContainerAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/inventory/remove",
      request,
      channel,
      timeout
    );
  }
  /**
   * Move item to different slot or container
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async moveItemAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/inventory/move",
      request,
      channel,
      timeout
    );
  }
  /**
   * Transfer item to different owner
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async transferItemAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/inventory/transfer",
      request,
      channel,
      timeout
    );
  }
  /**
   * Split stack into two
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async splitStackAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/inventory/split",
      request,
      channel,
      timeout
    );
  }
  /**
   * Merge two stacks
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async mergeStacksAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/inventory/merge",
      request,
      channel,
      timeout
    );
  }
  /**
   * Find items across containers
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async queryItemsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/inventory/query",
      request,
      channel,
      timeout
    );
  }
  /**
   * Count items of a template
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async countItemsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/inventory/count",
      request,
      channel,
      timeout
    );
  }
  /**
   * Check if entity has required items
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async hasItemsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/inventory/has",
      request,
      channel,
      timeout
    );
  }
  /**
   * Find where item would fit
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async findSpaceAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/inventory/find-space",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/ItemProxy.ts
var ItemProxy = class {
  client;
  /**
   * Creates a new ItemProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Create a new item template
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createItemTemplateAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/item/template/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get item template by ID or code
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getItemTemplateAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/item/template/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * List item templates with filters
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listItemTemplatesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/item/template/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Update mutable fields of an item template
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async updateItemTemplateAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/item/template/update",
      request,
      channel,
      timeout
    );
  }
  /**
   * Create a new item instance
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createItemInstanceAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/item/instance/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get item instance by ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getItemInstanceAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/item/instance/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Modify item instance state
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async modifyItemInstanceAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/item/instance/modify",
      request,
      channel,
      timeout
    );
  }
  /**
   * Bind item to character
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async bindItemInstanceAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/item/instance/bind",
      request,
      channel,
      timeout
    );
  }
  /**
   * Destroy item instance
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async destroyItemInstanceAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/item/instance/destroy",
      request,
      channel,
      timeout
    );
  }
  /**
   * List items in a container
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listItemsByContainerAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/item/instance/list-by-container",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get multiple item instances by ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async batchGetItemInstancesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/item/instance/batch-get",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/LeaderboardProxy.ts
var LeaderboardProxy = class {
  client;
  /**
   * Creates a new LeaderboardProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Create a new leaderboard definition
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createLeaderboardDefinitionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/leaderboard/definition/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * Update leaderboard definition
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async updateLeaderboardDefinitionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/leaderboard/definition/update",
      request,
      channel,
      timeout
    );
  }
  /**
   * Delete leaderboard definition
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async deleteLeaderboardDefinitionEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/leaderboard/definition/delete",
      request,
      channel
    );
  }
  /**
   * Get entity's rank
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getEntityRankAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/leaderboard/rank/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get top entries
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getTopRanksAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/leaderboard/rank/top",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get entries around entity
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getRanksAroundAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/leaderboard/rank/around",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get current season info
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getSeasonAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/leaderboard/season/get",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/LocationProxy.ts
var LocationProxy = class {
  client;
  /**
   * Creates a new LocationProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get location by ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getLocationAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/location/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get location by code and realm
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getLocationByCodeAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/location/get-by-code",
      request,
      channel,
      timeout
    );
  }
  /**
   * List locations with filtering
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listLocationsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/location/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * List all locations in a realm (primary query pattern)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listLocationsByRealmAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/location/list-by-realm",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get child locations for a parent location
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listLocationsByParentAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/location/list-by-parent",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get root locations in a realm
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listRootLocationsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/location/list-root",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get all ancestors of a location
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getLocationAncestorsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/location/get-ancestors",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get all descendants of a location
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getLocationDescendantsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/location/get-descendants",
      request,
      channel,
      timeout
    );
  }
  /**
   * Check if location exists and is active
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async locationExistsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/location/exists",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/MappingProxy.ts
var MappingProxy = class {
  client;
  /**
   * Creates a new MappingProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Request full snapshot for cold start
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async requestSnapshotAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/mapping/request-snapshot",
      request,
      channel,
      timeout
    );
  }
  /**
   * Query map data at a specific point
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async queryPointAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/mapping/query/point",
      request,
      channel,
      timeout
    );
  }
  /**
   * Query map data within bounds
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async queryBoundsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/mapping/query/bounds",
      request,
      channel,
      timeout
    );
  }
  /**
   * Find all objects of a type in region
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async queryObjectsByTypeAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/mapping/query/objects-by-type",
      request,
      channel,
      timeout
    );
  }
  /**
   * Find locations that afford a specific action or scene type
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async queryAffordanceAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/mapping/query/affordance",
      request,
      channel,
      timeout
    );
  }
  /**
   * Acquire exclusive edit lock for design-time editing
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async checkoutForAuthoringAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/mapping/authoring/checkout",
      request,
      channel,
      timeout
    );
  }
  /**
   * Commit design-time changes
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async commitAuthoringAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/mapping/authoring/commit",
      request,
      channel,
      timeout
    );
  }
  /**
   * Release authoring checkout without committing
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async releaseAuthoringAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/mapping/authoring/release",
      request,
      channel,
      timeout
    );
  }
  /**
   * Create a map definition template
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createDefinitionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/mapping/definition/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get a map definition by ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getDefinitionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/mapping/definition/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * List map definitions with optional filters
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listDefinitionsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/mapping/definition/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Update a map definition
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async updateDefinitionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/mapping/definition/update",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/MatchmakingProxy.ts
var MatchmakingProxy = class {
  client;
  /**
   * Creates a new MatchmakingProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * List available matchmaking queues
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listQueuesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/matchmaking/queue/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get queue details
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getQueueAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/matchmaking/queue/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Join matchmaking queue
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async joinMatchmakingAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/matchmaking/join",
      request,
      channel,
      timeout
    );
  }
  /**
   * Leave matchmaking queue
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async leaveMatchmakingEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/matchmaking/leave",
      request,
      channel
    );
  }
  /**
   * Get matchmaking status
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getMatchmakingStatusAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/matchmaking/status",
      request,
      channel,
      timeout
    );
  }
  /**
   * Accept a formed match
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async acceptMatchAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/matchmaking/accept",
      request,
      channel,
      timeout
    );
  }
  /**
   * Decline a formed match
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async declineMatchEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/matchmaking/decline",
      request,
      channel
    );
  }
  /**
   * Get queue statistics
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getMatchmakingStatsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/matchmaking/stats",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/MusicProxy.ts
var MusicProxy = class {
  client;
  /**
   * Creates a new MusicProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Generate composition from style and constraints
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async generateCompositionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/music/generate",
      request,
      channel,
      timeout
    );
  }
  /**
   * Validate MIDI-JSON structure
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async validateMidiJsonAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/music/validate",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get style definition
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getStyleAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/music/style/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * List available styles
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listStylesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/music/style/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Generate chord progression
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async generateProgressionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/music/theory/progression",
      request,
      channel,
      timeout
    );
  }
  /**
   * Generate melody over harmony
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async generateMelodyAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/music/theory/melody",
      request,
      channel,
      timeout
    );
  }
  /**
   * Apply voice leading to chords
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async applyVoiceLeadingAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/music/theory/voice-lead",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/RealmProxy.ts
var RealmProxy = class {
  client;
  /**
   * Creates a new RealmProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get realm by ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getRealmAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/realm/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get realm by code
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getRealmByCodeAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/realm/get-by-code",
      request,
      channel,
      timeout
    );
  }
  /**
   * List all realms
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listRealmsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/realm/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Check if realm exists and is active
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async realmExistsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/realm/exists",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/RealmHistoryProxy.ts
var RealmHistoryProxy = class {
  client;
  /**
   * Creates a new RealmHistoryProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get all historical events a realm participated in
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getRealmParticipationAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/realm-history/get-participation",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get all realms that participated in a historical event
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getRealmEventParticipantsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/realm-history/get-event-participants",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get machine-readable lore elements for behavior system
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getRealmLoreAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/realm-history/get-lore",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/RelationshipProxy.ts
var RelationshipProxy = class {
  client;
  /**
   * Creates a new RelationshipProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get a relationship by ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getRelationshipAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/relationship/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * List all relationships for an entity
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listRelationshipsByEntityAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/relationship/list-by-entity",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get all relationships between two specific entities
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getRelationshipsBetweenAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/relationship/get-between",
      request,
      channel,
      timeout
    );
  }
  /**
   * List all relationships of a specific type
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listRelationshipsByTypeAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/relationship/list-by-type",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/RelationshipTypeProxy.ts
var RelationshipTypeProxy = class {
  client;
  /**
   * Creates a new RelationshipTypeProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get relationship type by ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getRelationshipTypeAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/relationship-type/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get relationship type by code
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getRelationshipTypeByCodeAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/relationship-type/get-by-code",
      request,
      channel,
      timeout
    );
  }
  /**
   * List all relationship types
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listRelationshipTypesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/relationship-type/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get child types for a parent type
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getChildRelationshipTypesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/relationship-type/get-children",
      request,
      channel,
      timeout
    );
  }
  /**
   * Check if type matches ancestor in hierarchy
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async matchesHierarchyAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/relationship-type/matches-hierarchy",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get all ancestors of a relationship type
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getAncestorsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/relationship-type/get-ancestors",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/SaveLoadProxy.ts
var SaveLoadProxy = class {
  client;
  /**
   * Creates a new SaveLoadProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Create or configure a save slot
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createSlotAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/slot/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get slot metadata
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getSlotAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/slot/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * List slots for owner
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listSlotsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/slot/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Delete slot and all versions
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async deleteSlotAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/slot/delete",
      request,
      channel,
      timeout
    );
  }
  /**
   * Rename a save slot
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async renameSlotAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/slot/rename",
      request,
      channel,
      timeout
    );
  }
  /**
   * Save data to slot
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async saveAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/save",
      request,
      channel,
      timeout
    );
  }
  /**
   * Load data from slot
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async loadAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/load",
      request,
      channel,
      timeout
    );
  }
  /**
   * Save incremental changes from base version
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async saveDeltaAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/save-delta",
      request,
      channel,
      timeout
    );
  }
  /**
   * Load save reconstructing from delta chain
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async loadWithDeltasAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/load-with-deltas",
      request,
      channel,
      timeout
    );
  }
  /**
   * Collapse delta chain into full snapshot
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async collapseDeltasAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/collapse-deltas",
      request,
      channel,
      timeout
    );
  }
  /**
   * List versions in slot
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listVersionsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/version/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Pin a version as checkpoint
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async pinVersionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/version/pin",
      request,
      channel,
      timeout
    );
  }
  /**
   * Unpin a version
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async unpinVersionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/version/unpin",
      request,
      channel,
      timeout
    );
  }
  /**
   * Delete specific version
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async deleteVersionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/version/delete",
      request,
      channel,
      timeout
    );
  }
  /**
   * Query saves with filters
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async querySavesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/query",
      request,
      channel,
      timeout
    );
  }
  /**
   * Copy save to different slot or owner
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async copySaveAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/copy",
      request,
      channel,
      timeout
    );
  }
  /**
   * Export saves for backup/portability
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async exportSavesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/export",
      request,
      channel,
      timeout
    );
  }
  /**
   * Verify save data integrity
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async verifyIntegrityAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/verify",
      request,
      channel,
      timeout
    );
  }
  /**
   * Promote old version to latest
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async promoteVersionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/version/promote",
      request,
      channel,
      timeout
    );
  }
  /**
   * Migrate save to new schema version
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async migrateSaveAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/migrate",
      request,
      channel,
      timeout
    );
  }
  /**
   * Register a save data schema
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async registerSchemaAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/schema/register",
      request,
      channel,
      timeout
    );
  }
  /**
   * List registered schemas
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listSchemasAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/save-load/schema/list",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/SceneProxy.ts
var SceneProxy = class {
  client;
  /**
   * Creates a new SceneProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Create a new scene document
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createSceneAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/create",
      request,
      channel,
      timeout
    );
  }
  /**
   * Retrieve a scene by ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getSceneAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * List scenes with filtering
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listScenesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Update a scene document
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async updateSceneAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/update",
      request,
      channel,
      timeout
    );
  }
  /**
   * Delete a scene
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async deleteSceneAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/delete",
      request,
      channel,
      timeout
    );
  }
  /**
   * Validate a scene structure
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async validateSceneAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/validate",
      request,
      channel,
      timeout
    );
  }
  /**
   * Lock a scene for editing
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async checkoutSceneAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/checkout",
      request,
      channel,
      timeout
    );
  }
  /**
   * Save changes and release lock
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async commitSceneAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/commit",
      request,
      channel,
      timeout
    );
  }
  /**
   * Release lock without saving changes
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async discardCheckoutAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/discard",
      request,
      channel,
      timeout
    );
  }
  /**
   * Extend checkout lock TTL
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async heartbeatCheckoutAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/heartbeat",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get version history for a scene
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getSceneHistoryAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/history",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get validation rules for a gameId+sceneType
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getValidationRulesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/get-validation-rules",
      request,
      channel,
      timeout
    );
  }
  /**
   * Full-text search across scenes
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async searchScenesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/search",
      request,
      channel,
      timeout
    );
  }
  /**
   * Find scenes that reference a given scene
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async findReferencesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/find-references",
      request,
      channel,
      timeout
    );
  }
  /**
   * Find scenes using a specific asset
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async findAssetUsageAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/find-asset-usage",
      request,
      channel,
      timeout
    );
  }
  /**
   * Duplicate a scene with a new ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async duplicateSceneAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/scene/duplicate",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/SessionsProxy.ts
var SessionsProxy = class {
  client;
  /**
   * Creates a new SessionsProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get game session details
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getGameSessionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/sessions/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Leave a game session
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async leaveGameSessionEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/sessions/leave",
      request,
      channel
    );
  }
  /**
   * Send chat message to game session
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async sendChatMessageEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/sessions/chat",
      request,
      channel
    );
  }
  /**
   * Perform game action (enhanced permissions after joining)
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async performGameActionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/sessions/actions",
      request,
      channel,
      timeout
    );
  }
  /**
   * Leave a specific game session by ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async leaveGameSessionByIdEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/sessions/leave-session",
      request,
      channel
    );
  }
};

// src/Generated/proxies/SpeciesProxy.ts
var SpeciesProxy = class {
  client;
  /**
   * Creates a new SpeciesProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get species by ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getSpeciesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/species/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get species by code
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getSpeciesByCodeAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/species/get-by-code",
      request,
      channel,
      timeout
    );
  }
  /**
   * List all species
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listSpeciesAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/species/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * List species available in a realm
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async listSpeciesByRealmAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/species/list-by-realm",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/SubscriptionProxy.ts
var SubscriptionProxy = class {
  client;
  /**
   * Creates a new SubscriptionProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get subscriptions for an account
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getAccountSubscriptionsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/subscription/account/list",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get a specific subscription by ID
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getSubscriptionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/subscription/get",
      request,
      channel,
      timeout
    );
  }
  /**
   * Cancel a subscription
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async cancelSubscriptionAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/subscription/cancel",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/ValidateProxy.ts
var ValidateProxy = class {
  client;
  /**
   * Creates a new ValidateProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Validate ABML definition
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async validateAbmlAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/validate",
      request,
      channel,
      timeout
    );
  }
};

// src/Generated/proxies/VoiceProxy.ts
var VoiceProxy = class {
  client;
  /**
   * Creates a new VoiceProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Send SDP answer to complete WebRTC handshake
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async answerPeerEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/voice/peer/answer",
      request,
      channel
    );
  }
};

// src/Generated/proxies/WebsiteProxy.ts
var WebsiteProxy = class {
  client;
  /**
   * Creates a new WebsiteProxy instance.
   * @param client - The underlying Bannou client.
   */
  constructor(client) {
    this.client = client;
  }
  /**
   * Get website status and version
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getStatusAsync(channel = 0, timeout) {
    return this.client.invokeAsync(
      "/website/status",
      {},
      channel,
      timeout
    );
  }
  /**
   * Get dynamic page content from CMS
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getPageContentAsync(channel = 0, timeout) {
    return this.client.invokeAsync(
      "/website/content/{slug}",
      {},
      channel,
      timeout
    );
  }
  /**
   * Get latest news and announcements
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getNewsAsync(channel = 0, timeout) {
    return this.client.invokeAsync(
      "/website/news",
      {},
      channel,
      timeout
    );
  }
  /**
   * Get game server status for all realms
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getServerStatusAsync(channel = 0, timeout) {
    return this.client.invokeAsync(
      "/website/server-status",
      {},
      channel,
      timeout
    );
  }
  /**
   * Get download links for game clients
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getDownloadsAsync(channel = 0, timeout) {
    return this.client.invokeAsync(
      "/website/downloads",
      {},
      channel,
      timeout
    );
  }
  /**
   * Submit contact form
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async submitContactAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/website/contact",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get account profile for logged-in user
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getAccountProfileAsync(channel = 0, timeout) {
    return this.client.invokeAsync(
      "/website/account/profile",
      {},
      channel,
      timeout
    );
  }
  /**
   * Get character list for logged-in user
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getAccountCharactersAsync(channel = 0, timeout) {
    return this.client.invokeAsync(
      "/website/account/characters",
      {},
      channel,
      timeout
    );
  }
  /**
   * Create new CMS page
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async createPageAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/website/cms/pages",
      request,
      channel,
      timeout
    );
  }
  /**
   * Update CMS page
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async updatePageAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/website/cms/pages/{slug}",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get site configuration
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getSiteSettingsAsync(channel = 0, timeout) {
    return this.client.invokeAsync(
      "/website/cms/site-settings",
      {},
      channel,
      timeout
    );
  }
  /**
   * Update site configuration
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async updateSiteSettingsAsync(request, channel = 0, timeout) {
    return this.client.invokeAsync(
      "/website/cms/site-settings",
      request,
      channel,
      timeout
    );
  }
  /**
   * Get current theme configuration
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getThemeAsync(channel = 0, timeout) {
    return this.client.invokeAsync(
      "/website/cms/theme",
      {},
      channel,
      timeout
    );
  }
  /**
   * Update theme configuration
   * @param request - The request payload.
   * @param channel - Message channel for ordering (default 0).
   * @returns Promise that completes when the event is sent.
   */
  async updateThemeEventAsync(request, channel = 0) {
    return this.client.sendEventAsync(
      "/website/cms/theme",
      request,
      channel
    );
  }
  /**
   * Get subscription status
   * @param channel - Message channel for ordering (default 0).
   * @param timeout - Request timeout in milliseconds.
   * @returns ApiResponse containing the response on success.
   */
  async getAccountSubscriptionAsync(channel = 0, timeout) {
    return this.client.invokeAsync(
      "/website/account/subscription",
      {},
      channel,
      timeout
    );
  }
};

// src/Generated/BannouClientExtensions.ts
var PROXY_CACHE = /* @__PURE__ */ Symbol("proxyCache");
Object.defineProperty(BannouClient.prototype, "account", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.account ??= new AccountProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "achievement", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.achievement ??= new AchievementProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "actor", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.actor ??= new ActorProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "assets", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.assets ??= new AssetsProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "auth", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.auth ??= new AuthProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "bundles", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.bundles ??= new BundlesProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "cache", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.cache ??= new CacheProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "character", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.character ??= new CharacterProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "characterEncounter", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.characterEncounter ??= new CharacterEncounterProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "characterHistory", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.characterHistory ??= new CharacterHistoryProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "characterPersonality", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.characterPersonality ??= new CharacterPersonalityProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "clientCapabilities", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.clientCapabilities ??= new ClientCapabilitiesProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "compile", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.compile ??= new CompileProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "contract", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.contract ??= new ContractProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "currency", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.currency ??= new CurrencyProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "documentation", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.documentation ??= new DocumentationProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "escrow", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.escrow ??= new EscrowProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "gameService", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.gameService ??= new GameServiceProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "goap", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.goap ??= new GoapProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "inventory", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.inventory ??= new InventoryProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "item", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.item ??= new ItemProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "leaderboard", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.leaderboard ??= new LeaderboardProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "location", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.location ??= new LocationProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "mapping", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.mapping ??= new MappingProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "matchmaking", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.matchmaking ??= new MatchmakingProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "music", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.music ??= new MusicProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "realm", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.realm ??= new RealmProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "realmHistory", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.realmHistory ??= new RealmHistoryProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "relationship", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.relationship ??= new RelationshipProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "relationshipType", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.relationshipType ??= new RelationshipTypeProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "saveLoad", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.saveLoad ??= new SaveLoadProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "scene", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.scene ??= new SceneProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "sessions", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.sessions ??= new SessionsProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "species", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.species ??= new SpeciesProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "subscription", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.subscription ??= new SubscriptionProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "validate", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.validate ??= new ValidateProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "voice", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.voice ??= new VoiceProxy(this);
  },
  configurable: true,
  enumerable: true
});
Object.defineProperty(BannouClient.prototype, "website", {
  get() {
    const cache = this[PROXY_CACHE] ??= {};
    return cache.website ??= new WebsiteProxy(this);
  },
  configurable: true,
  enumerable: true
});

export { AccountProxy, AchievementProxy, ActorProxy, AssetsProxy, AuthProxy, BannouClient, BundlesProxy, CacheProxy, CharacterEncounterProxy, CharacterHistoryProxy, CharacterPersonalityProxy, CharacterProxy, ClientCapabilitiesProxy, CompileProxy, ConnectionState, ContractProxy, CurrencyProxy, DisconnectReason, DocumentationProxy, EMPTY_GUID, EscrowProxy, EventSubscription, GameServiceProxy, GoapProxy, HEADER_SIZE, InventoryProxy, ItemProxy, LeaderboardProxy, LocationProxy, MappingProxy, MatchmakingProxy, MessageFlags, MetaType, MusicProxy, RESPONSE_HEADER_SIZE, RealmHistoryProxy, RealmProxy, RelationshipProxy, RelationshipTypeProxy, ResponseCodes, SaveLoadProxy, SceneProxy, SessionsProxy, SpeciesProxy, SubscriptionProxy, ValidateProxy, VoiceProxy, WebsiteProxy, createRequest, createResponse, expectsResponse, generateMessageId, generateUuid, getJsonPayload, getResponseCodeName, hasFlag, isBinary as isBinaryFlag, isClientRouted, isError2 as isError, isError as isErrorCode, isEvent as isEventFlag, isHighPriority, isMeta2 as isMeta, isMeta as isMetaFlag, isResponse2 as isResponse, isResponse as isResponseFlag, isSuccess2 as isSuccess, isSuccess as isSuccessCode, mapToHttpStatus, parse, readGuid, readUInt16, readUInt32, readUInt64, resetMessageIdCounter, testNetworkByteOrderCompatibility, toByteArray, writeGuid, writeUInt16, writeUInt32, writeUInt64 };
//# sourceMappingURL=index.js.map
//# sourceMappingURL=index.js.map
