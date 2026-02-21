/**
 * Protocol layer for Bannou WebSocket binary messaging.
 *
 * This module provides low-level utilities for working with the Bannou binary protocol:
 * - MessageFlags: Bit flags controlling message behavior
 * - ResponseCodes: Protocol response codes for success/error indication
 * - NetworkByteOrder: Cross-platform byte order utilities (big-endian + RFC 4122 GUIDs)
 * - BinaryMessage: Message serialization and parsing
 */

export {
  MessageFlags,
  type MessageFlagsType,
  hasFlag,
  isResponse as isResponseFlag,
  isEvent as isEventFlag,
  isMeta as isMetaFlag,
  isBinary as isBinaryFlag,
  isCompressed as isCompressedFlag,
} from './MessageFlags.js';

export {
  ResponseCodes,
  type ResponseCodesType,
  mapToHttpStatus,
  getResponseCodeName,
  isSuccess as isSuccessCode,
  isError as isErrorCode,
} from './ResponseCodes.js';

export {
  writeUInt16,
  writeUInt32,
  writeUInt64,
  readUInt16,
  readUInt32,
  readUInt64,
  writeGuid,
  readGuid,
  EMPTY_GUID,
  testNetworkByteOrderCompatibility,
} from './NetworkByteOrder.js';

export {
  HEADER_SIZE,
  RESPONSE_HEADER_SIZE,
  type BinaryMessage,
  createRequest,
  createResponse,
  fromJson,
  getJsonPayload,
  parse,
  toByteArray,
  expectsResponse,
  isResponse,
  isClientRouted,
  isSuccess,
  isError,
  isMeta,
} from './BinaryMessage.js';

export {
  initializeDecompressor,
  setPayloadDecompressor,
  decompressPayload,
  resetDecompressor,
} from './PayloadDecompressor.js';
