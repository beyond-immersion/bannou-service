/**
 * Represents a complete binary message in the Bannou WebSocket protocol.
 * Request messages use a 31-byte header, response messages use a 16-byte header.
 * Provides zero-copy parsing and serialization for optimal performance.
 */

import { MessageFlags, hasFlag, isResponse as checkIsResponse } from './MessageFlags.js';
import {
  readUInt16,
  readUInt32,
  readUInt64,
  readGuid,
  writeUInt16,
  writeUInt32,
  writeUInt64,
  writeGuid,
  EMPTY_GUID,
} from './NetworkByteOrder.js';

/**
 * Size of the binary header in bytes for request messages.
 *
 * Request header layout (31 bytes):
 * - [0]     Flags (1 byte)
 * - [1-2]   Channel (uint16 BE)
 * - [3-6]   Sequence (uint32 BE)
 * - [7-22]  ServiceGUID (16 bytes, RFC 4122)
 * - [23-30] MessageID (uint64 BE)
 * - [31+]   Payload (JSON UTF-8)
 */
export const HEADER_SIZE = 31;

/**
 * Size of the binary header in bytes for response messages.
 * Response headers are smaller because ServiceGuid is not needed (client correlates via MessageId).
 *
 * Response header layout (16 bytes):
 * - [0]     Flags (1 byte, Response flag set)
 * - [1-2]   Channel (uint16 BE)
 * - [3-6]   Sequence (uint32 BE)
 * - [7-14]  MessageID (uint64 BE)
 * - [15]    ResponseCode (1 byte)
 * - [16+]   Payload (empty for errors)
 */
export const RESPONSE_HEADER_SIZE = 16;

/**
 * Represents a parsed or constructed binary message.
 */
export interface BinaryMessage {
  /** Message behavior flags */
  flags: number;

  /** Channel for sequential message processing (0-65535, 0 = default) */
  channel: number;

  /** Per-channel sequence number for message ordering */
  sequenceNumber: number;

  /** Client-salted service GUID for routing (only used in request messages) */
  serviceGuid: string;

  /** Unique message ID for request/response correlation */
  messageId: bigint;

  /** Response code for response messages (0 = OK, non-zero = error) */
  responseCode: number;

  /** Message payload (JSON or binary data based on flags) */
  payload: Uint8Array;
}

/**
 * Creates a request message.
 */
export function createRequest(
  flags: number,
  channel: number,
  sequenceNumber: number,
  serviceGuid: string,
  messageId: bigint,
  payload: Uint8Array
): BinaryMessage {
  return {
    flags,
    channel,
    sequenceNumber,
    serviceGuid,
    messageId,
    responseCode: 0,
    payload,
  };
}

/**
 * Creates a response message.
 */
export function createResponse(
  flags: number,
  channel: number,
  sequenceNumber: number,
  messageId: bigint,
  responseCode: number,
  payload: Uint8Array
): BinaryMessage {
  return {
    flags: flags | MessageFlags.Response,
    channel,
    sequenceNumber,
    serviceGuid: EMPTY_GUID,
    messageId,
    responseCode,
    payload,
  };
}

/**
 * Creates a binary message from a JSON string payload.
 */
export function fromJson(
  channel: number,
  sequenceNumber: number,
  serviceGuid: string,
  messageId: bigint,
  jsonPayload: string,
  additionalFlags: number = MessageFlags.None
): BinaryMessage {
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

/**
 * Gets the JSON payload as a string (if the message is JSON).
 * @throws Error if the message has the Binary flag set
 */
export function getJsonPayload(message: BinaryMessage): string {
  if (hasFlag(message.flags, MessageFlags.Binary)) {
    throw new Error('Cannot get JSON payload from binary message');
  }
  return new TextDecoder().decode(message.payload);
}

/**
 * Parses a binary message from a byte array.
 * Automatically detects whether this is a request (31-byte header) or response (16-byte header).
 */
export function parse(buffer: ArrayBuffer | Uint8Array): BinaryMessage {
  const bytes = buffer instanceof Uint8Array ? buffer : new Uint8Array(buffer);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);

  if (bytes.length < 1) {
    throw new Error('Message too short. Expected at least 1 byte for flags.');
  }

  const flags = view.getUint8(0);

  // Check if this is a response message (16-byte header) or request message (31-byte header)
  if (checkIsResponse(flags)) {
    return parseResponse(view, bytes);
  } else {
    return parseRequest(view, bytes);
  }
}

function parseRequest(view: DataView, bytes: Uint8Array): BinaryMessage {
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

  // Extract payload (remaining bytes after 31-byte header)
  const payload = bytes.slice(HEADER_SIZE);

  return {
    flags,
    channel,
    sequenceNumber,
    serviceGuid,
    messageId,
    responseCode: 0,
    payload,
  };
}

function parseResponse(view: DataView, bytes: Uint8Array): BinaryMessage {
  if (bytes.length < RESPONSE_HEADER_SIZE) {
    throw new Error(
      `Response message too short. Expected at least ` +
        `${RESPONSE_HEADER_SIZE} bytes, got ${bytes.length}`
    );
  }

  const flags = view.getUint8(0);
  const channel = readUInt16(view, 1);
  const sequenceNumber = readUInt32(view, 3);
  const messageId = readUInt64(view, 7);
  const responseCode = view.getUint8(15);

  // Extract payload (remaining bytes after 16-byte header)
  const payload = bytes.slice(RESPONSE_HEADER_SIZE);

  return {
    flags,
    channel,
    sequenceNumber,
    serviceGuid: EMPTY_GUID,
    messageId,
    responseCode,
    payload,
  };
}

/**
 * Serializes the binary message to a byte array.
 * Uses 31-byte header for requests, 16-byte header for responses.
 */
export function toByteArray(message: BinaryMessage): Uint8Array {
  if (checkIsResponse(message.flags)) {
    return toResponseByteArray(message);
  } else {
    return toRequestByteArray(message);
  }
}

function toRequestByteArray(message: BinaryMessage): Uint8Array {
  const totalLength = HEADER_SIZE + message.payload.length;
  const result = new Uint8Array(totalLength);
  const view = new DataView(result.buffer);

  // Build 31-byte request header
  view.setUint8(0, message.flags);
  writeUInt16(view, 1, message.channel);
  writeUInt32(view, 3, message.sequenceNumber);
  writeGuid(view, 7, message.serviceGuid);
  writeUInt64(view, 23, message.messageId);

  // Append payload
  if (message.payload.length > 0) {
    result.set(message.payload, HEADER_SIZE);
  }

  return result;
}

function toResponseByteArray(message: BinaryMessage): Uint8Array {
  const totalLength = RESPONSE_HEADER_SIZE + message.payload.length;
  const result = new Uint8Array(totalLength);
  const view = new DataView(result.buffer);

  // Build 16-byte response header
  view.setUint8(0, message.flags);
  writeUInt16(view, 1, message.channel);
  writeUInt32(view, 3, message.sequenceNumber);
  writeUInt64(view, 7, message.messageId);
  view.setUint8(15, message.responseCode);

  // Append payload (empty for error responses)
  if (message.payload.length > 0) {
    result.set(message.payload, RESPONSE_HEADER_SIZE);
  }

  return result;
}

/**
 * Returns true if this message expects a response.
 */
export function expectsResponse(message: BinaryMessage): boolean {
  return !hasFlag(message.flags, MessageFlags.Event) && !checkIsResponse(message.flags);
}

/**
 * Returns true if this message is a response to another message.
 */
export function isResponse(message: BinaryMessage): boolean {
  return checkIsResponse(message.flags);
}

/**
 * Returns true if this message should be routed to a client (not a service).
 */
export function isClientRouted(message: BinaryMessage): boolean {
  return hasFlag(message.flags, MessageFlags.Client);
}

/**
 * Returns true if this is a successful response (ResponseCode is 0).
 */
export function isSuccess(message: BinaryMessage): boolean {
  return checkIsResponse(message.flags) && message.responseCode === 0;
}

/**
 * Returns true if this is an error response (ResponseCode is non-zero).
 */
export function isError(message: BinaryMessage): boolean {
  return checkIsResponse(message.flags) && message.responseCode !== 0;
}

/**
 * Returns true if this message requests endpoint metadata.
 */
export function isMeta(message: BinaryMessage): boolean {
  return hasFlag(message.flags, MessageFlags.Meta);
}
