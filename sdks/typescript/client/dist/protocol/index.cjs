'use strict';

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
function fromJson(channel, sequenceNumber, serviceGuid, messageId, jsonPayload, additionalFlags = MessageFlags.None) {
  const payloadBytes = new TextEncoder().encode(jsonPayload);
  return createRequest(
    additionalFlags,
    channel,
    sequenceNumber,
    serviceGuid,
    messageId,
    payloadBytes
  );
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

exports.EMPTY_GUID = EMPTY_GUID;
exports.HEADER_SIZE = HEADER_SIZE;
exports.MessageFlags = MessageFlags;
exports.RESPONSE_HEADER_SIZE = RESPONSE_HEADER_SIZE;
exports.ResponseCodes = ResponseCodes;
exports.createRequest = createRequest;
exports.createResponse = createResponse;
exports.expectsResponse = expectsResponse;
exports.fromJson = fromJson;
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
exports.testNetworkByteOrderCompatibility = testNetworkByteOrderCompatibility;
exports.toByteArray = toByteArray;
exports.writeGuid = writeGuid;
exports.writeUInt16 = writeUInt16;
exports.writeUInt32 = writeUInt32;
exports.writeUInt64 = writeUInt64;
//# sourceMappingURL=index.cjs.map
//# sourceMappingURL=index.cjs.map