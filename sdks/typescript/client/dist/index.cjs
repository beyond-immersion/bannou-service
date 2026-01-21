'use strict';

var bannouCore = require('@beyondimmersion/bannou-core');

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
   */
  getServiceGuid(method, path) {
    const key = `${method}:${path}`;
    return this.apiMappings.get(key);
  }
  /**
   * Invokes a service method by specifying the HTTP method and path.
   */
  async invokeAsync(method, path, request, channel = 0, timeout = DEFAULT_TIMEOUT_MS) {
    if (!this.isConnected || !this.webSocket) {
      throw new Error("WebSocket is not connected. Call connectAsync first.");
    }
    if (!this.connectionState) {
      throw new Error("Connection state not initialized.");
    }
    const serviceGuid = this.getServiceGuid(method, path);
    if (!serviceGuid) {
      const available = Array.from(this.apiMappings.keys()).slice(0, 10).join(", ");
      throw new Error(`Unknown endpoint: ${method} ${path}. Available: ${available}...`);
    }
    const jsonPayload = bannouCore.BannouJson.serialize(request);
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
    const responsePromise = this.createResponsePromise(messageId, timeout, method, path);
    this.connectionState.addPendingMessage(messageId, `${method}:${path}`, /* @__PURE__ */ new Date());
    try {
      const messageBytes = toByteArray(message);
      this.sendBinaryMessage(messageBytes);
      const response = await responsePromise;
      if (response.responseCode !== 0) {
        return bannouCore.ApiResponse.failure(
          bannouCore.createErrorResponse(response.responseCode, messageId, method, path)
        );
      }
      if (response.payload.length === 0) {
        return bannouCore.ApiResponse.successEmpty();
      }
      const responseJson = getJsonPayload(response);
      const result = bannouCore.BannouJson.deserialize(responseJson);
      if (result === null) {
        return bannouCore.ApiResponse.failure(
          bannouCore.createErrorResponse(60, messageId, method, path, "Failed to deserialize response")
        );
      }
      return bannouCore.ApiResponse.success(result);
    } finally {
      this.pendingRequests.delete(messageId.toString());
      this.connectionState?.removePendingMessage(messageId);
    }
  }
  /**
   * Sends a fire-and-forget event (no response expected).
   */
  async sendEventAsync(method, path, request, channel = 0) {
    if (!this.isConnected || !this.webSocket) {
      throw new Error("WebSocket is not connected. Call connectAsync first.");
    }
    if (!this.connectionState) {
      throw new Error("Connection state not initialized.");
    }
    const serviceGuid = this.getServiceGuid(method, path);
    if (!serviceGuid) {
      throw new Error(`Unknown endpoint: ${method} ${path}`);
    }
    const jsonPayload = bannouCore.BannouJson.serialize(request);
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
        const event = bannouCore.BannouJson.deserialize(json);
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
   */
  async getEndpointMetaAsync(method, path, metaType = 3 /* FullSchema */, timeout = 1e4) {
    if (!this.isConnected || !this.webSocket) {
      throw new Error("WebSocket is not connected. Call connectAsync first.");
    }
    if (!this.connectionState) {
      throw new Error("Connection state not initialized.");
    }
    const serviceGuid = this.getServiceGuid(method, path);
    if (!serviceGuid) {
      const available = Array.from(this.apiMappings.keys()).slice(0, 10).join(", ");
      throw new Error(`Unknown endpoint: ${method} ${path}. Available: ${available}...`);
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
    const responsePromise = this.createResponsePromise(messageId, timeout, `META:${method}`, path);
    this.connectionState.addPendingMessage(messageId, `META:${method}:${path}`, /* @__PURE__ */ new Date());
    try {
      const messageBytes = toByteArray(message);
      this.sendBinaryMessage(messageBytes);
      const response = await responsePromise;
      if (response.responseCode !== 0) {
        throw new Error(`Meta request failed with response code ${response.responseCode}`);
      }
      const responseJson = getJsonPayload(response);
      const result = bannouCore.BannouJson.deserialize(responseJson);
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
   */
  async getEndpointInfoAsync(method, path) {
    return this.getEndpointMetaAsync(method, path, 0 /* EndpointInfo */);
  }
  /**
   * Gets JSON Schema for the request body of an endpoint.
   */
  async getRequestSchemaAsync(method, path) {
    return this.getEndpointMetaAsync(method, path, 1 /* RequestSchema */);
  }
  /**
   * Gets JSON Schema for the response body of an endpoint.
   */
  async getResponseSchemaAsync(method, path) {
    return this.getEndpointMetaAsync(method, path, 2 /* ResponseSchema */);
  }
  /**
   * Gets full schema including info, request schema, and response schema.
   */
  async getFullSchemaAsync(method, path) {
    return this.getEndpointMetaAsync(method, path, 3 /* FullSchema */);
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
      if (Array.isArray(manifest.availableAPIs)) {
        for (const api of manifest.availableAPIs) {
          if (api.endpointKey && api.serviceGuid) {
            this.apiMappings.set(api.endpointKey, api.serviceGuid);
            this.connectionState?.addServiceMapping(api.endpointKey, api.serviceGuid);
          }
        }
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
  createResponsePromise(messageId, timeout, method, path) {
    return new Promise((resolve, reject) => {
      const timeoutId = setTimeout(() => {
        this.pendingRequests.delete(messageId.toString());
        reject(new Error(`Request to ${method} ${path} timed out after ${timeout}ms`));
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

Object.defineProperty(exports, "ApiResponse", {
  enumerable: true,
  get: function () { return bannouCore.ApiResponse; }
});
Object.defineProperty(exports, "BannouJson", {
  enumerable: true,
  get: function () { return bannouCore.BannouJson; }
});
Object.defineProperty(exports, "createErrorResponse", {
  enumerable: true,
  get: function () { return bannouCore.createErrorResponse; }
});
Object.defineProperty(exports, "fromJson", {
  enumerable: true,
  get: function () { return bannouCore.fromJson; }
});
Object.defineProperty(exports, "getErrorName", {
  enumerable: true,
  get: function () { return bannouCore.getErrorName; }
});
Object.defineProperty(exports, "isBaseClientEvent", {
  enumerable: true,
  get: function () { return bannouCore.isBaseClientEvent; }
});
Object.defineProperty(exports, "mapToHttpStatusCode", {
  enumerable: true,
  get: function () { return bannouCore.mapToHttpStatusCode; }
});
Object.defineProperty(exports, "toJson", {
  enumerable: true,
  get: function () { return bannouCore.toJson; }
});
exports.BannouClient = BannouClient;
exports.ConnectionState = ConnectionState;
exports.DisconnectReason = DisconnectReason;
exports.EMPTY_GUID = EMPTY_GUID;
exports.EventSubscription = EventSubscription;
exports.HEADER_SIZE = HEADER_SIZE;
exports.MessageFlags = MessageFlags;
exports.MetaType = MetaType;
exports.RESPONSE_HEADER_SIZE = RESPONSE_HEADER_SIZE;
exports.ResponseCodes = ResponseCodes;
exports.createRequest = createRequest;
exports.createResponse = createResponse;
exports.expectsResponse = expectsResponse;
exports.generateMessageId = generateMessageId;
exports.generateUuid = generateUuid;
exports.getJsonPayload = getJsonPayload;
exports.getResponseCodeName = getResponseCodeName;
exports.hasFlag = hasFlag;
exports.isBinaryFlag = isBinary;
exports.isClientRouted = isClientRouted;
exports.isError = isError2;
exports.isErrorCode = isError;
exports.isEventFlag = isEvent;
exports.isHighPriority = isHighPriority;
exports.isMeta = isMeta2;
exports.isMetaFlag = isMeta;
exports.isResponse = isResponse2;
exports.isResponseFlag = isResponse;
exports.isSuccess = isSuccess2;
exports.isSuccessCode = isSuccess;
exports.mapToHttpStatus = mapToHttpStatus;
exports.parse = parse;
exports.readGuid = readGuid;
exports.readUInt16 = readUInt16;
exports.readUInt32 = readUInt32;
exports.readUInt64 = readUInt64;
exports.resetMessageIdCounter = resetMessageIdCounter;
exports.testNetworkByteOrderCompatibility = testNetworkByteOrderCompatibility;
exports.toByteArray = toByteArray;
exports.writeGuid = writeGuid;
exports.writeUInt16 = writeUInt16;
exports.writeUInt32 = writeUInt32;
exports.writeUInt64 = writeUInt64;
//# sourceMappingURL=index.cjs.map
//# sourceMappingURL=index.cjs.map