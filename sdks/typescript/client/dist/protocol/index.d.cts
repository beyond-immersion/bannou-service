/**
 * Bit flags controlling message behavior in the enhanced 31-byte binary protocol.
 * These flags provide zero-copy routing and performance optimizations.
 *
 * Flags can be combined using bitwise OR operations.
 *
 * @example
 * ```typescript
 * const flags = MessageFlags.Binary | MessageFlags.Event;
 * ```
 */
declare const MessageFlags: {
    /**
     * Default - JSON text, service request, unencrypted, standard priority, expects response
     */
    readonly None: 0;
    /**
     * Message payload is binary data (not JSON)
     */
    readonly Binary: 1;
    /** Reserved for future use. Do not assign. */
    readonly Reserved0x02: 2;
    /**
     * Payload is Brotli-compressed. Client must decompress before parsing.
     * Only set on server-to-client messages when compression is enabled and
     * payload exceeds the configured size threshold.
     */
    readonly Compressed: 4;
    /** Reserved for future use. Do not assign. */
    readonly Reserved0x08: 8;
    /**
     * Fire-and-forget message, no response expected
     */
    readonly Event: 16;
    /**
     * Route to another WebSocket client (not a Bannou service)
     */
    readonly Client: 32;
    /**
     * Message is a response to an RPC (not a new request)
     */
    readonly Response: 64;
    /**
     * Request metadata about endpoint instead of executing it.
     * When set, the Channel field specifies the meta type:
     * 0 = endpoint-info, 1 = request-schema, 2 = response-schema, 3 = full-schema.
     * Connect service transforms the path and routes to companion endpoints.
     */
    readonly Meta: 128;
};
type MessageFlagsType = (typeof MessageFlags)[keyof typeof MessageFlags];
/**
 * Check if a flags byte has a specific flag set.
 */
declare function hasFlag(flags: number, flag: number): boolean;
/**
 * Check if this is a response message.
 */
declare function isResponse$1(flags: number): boolean;
/**
 * Check if this is an event message (fire-and-forget).
 */
declare function isEvent(flags: number): boolean;
/**
 * Check if this is a meta request.
 */
declare function isMeta$1(flags: number): boolean;
/**
 * Check if payload is binary (not JSON).
 */
declare function isBinary(flags: number): boolean;
/**
 * Check if payload is Brotli-compressed.
 */
declare function isCompressed(flags: number): boolean;

/**
 * Response codes used in the binary WebSocket protocol for success/error indication.
 * These codes are returned in the response header's ResponseCode field.
 *
 * Code ranges:
 * - 0-49: Protocol-level errors (Connect service)
 * - 50-69: Service-level errors (downstream service responses)
 * - 70+: Shortcut-specific errors
 */
declare const ResponseCodes: {
    /** Request completed successfully. */
    readonly OK: 0;
    /** Generic request error (malformed message, invalid format). */
    readonly RequestError: 10;
    /** Request payload exceeds maximum allowed size. */
    readonly RequestTooLarge: 11;
    /** Rate limit exceeded for this client/session. */
    readonly TooManyRequests: 12;
    /** Invalid channel number in request header. */
    readonly InvalidRequestChannel: 13;
    /** Authentication required but not provided or invalid. */
    readonly Unauthorized: 20;
    /** Target service GUID not found in capability manifest. */
    readonly ServiceNotFound: 30;
    /** Target client GUID not found (for client-to-client messages). */
    readonly ClientNotFound: 31;
    /** Referenced message ID not found. */
    readonly MessageNotFound: 32;
    /** Broadcast not allowed in this connection mode (External mode blocks broadcast). */
    readonly BroadcastNotAllowed: 40;
    /** Service returned 400 Bad Request. */
    readonly Service_BadRequest: 50;
    /** Service returned 404 Not Found. */
    readonly Service_NotFound: 51;
    /** Service returned 401/403 Unauthorized/Forbidden. */
    readonly Service_Unauthorized: 52;
    /** Service returned 409 Conflict. */
    readonly Service_Conflict: 53;
    /** Service returned 500 Internal Server Error. */
    readonly Service_InternalServerError: 60;
    /** Shortcut has expired and is no longer valid. */
    readonly ShortcutExpired: 70;
    /** Shortcut target endpoint no longer exists. */
    readonly ShortcutTargetNotFound: 71;
    /** Shortcut was explicitly revoked. */
    readonly ShortcutRevoked: 72;
};
type ResponseCodesType = (typeof ResponseCodes)[keyof typeof ResponseCodes];
/**
 * Maps protocol response codes to HTTP status codes.
 * This hides the binary protocol implementation details from client code.
 */
declare function mapToHttpStatus(code: number): number;
/**
 * Gets a human-readable error name for a response code.
 */
declare function getResponseCodeName(code: number): string;
/**
 * Check if a response code indicates success.
 */
declare function isSuccess$1(code: number): boolean;
/**
 * Check if a response code indicates an error.
 */
declare function isError$1(code: number): boolean;

/**
 * Network byte order utilities for cross-platform binary protocol compatibility.
 * Ensures consistent endianness across all client and server implementations.
 *
 * All multi-byte integers in the Bannou protocol use BIG-ENDIAN (network) byte order.
 * GUIDs follow RFC 4122 network byte ordering.
 */
/**
 * Writes a 16-bit unsigned integer in network byte order (big-endian).
 */
declare function writeUInt16(view: DataView, offset: number, value: number): void;
/**
 * Writes a 32-bit unsigned integer in network byte order (big-endian).
 */
declare function writeUInt32(view: DataView, offset: number, value: number): void;
/**
 * Writes a 64-bit unsigned integer in network byte order (big-endian).
 * JavaScript's DataView doesn't have native BigInt support in all versions,
 * so we write it as two 32-bit values.
 */
declare function writeUInt64(view: DataView, offset: number, value: bigint): void;
/**
 * Reads a 16-bit unsigned integer from network byte order (big-endian).
 */
declare function readUInt16(view: DataView, offset: number): number;
/**
 * Reads a 32-bit unsigned integer from network byte order (big-endian).
 */
declare function readUInt32(view: DataView, offset: number): number;
/**
 * Reads a 64-bit unsigned integer from network byte order (big-endian).
 */
declare function readUInt64(view: DataView, offset: number): bigint;
/**
 * Writes a GUID in consistent network byte order.
 * Uses RFC 4122 standard byte ordering for cross-platform compatibility.
 *
 * CRITICAL: The GUID string is already in RFC 4122 display format (big-endian),
 * so we write the parsed bytes directly WITHOUT reversal.
 *
 * This differs from C# which uses Guid.ToByteArray() that returns little-endian
 * for time fields, requiring reversal. TypeScript parses the string directly
 * which is already big-endian.
 *
 * @param view - DataView to write to
 * @param offset - Byte offset to start writing
 * @param guid - GUID string in standard format (e.g., "12345678-1234-5678-9abc-def012345678")
 */
declare function writeGuid(view: DataView, offset: number, guid: string): void;
/**
 * Reads a GUID from consistent network byte order.
 * Uses RFC 4122 standard byte ordering for cross-platform compatibility.
 *
 * CRITICAL: Bytes are read directly without reversal because they're stored
 * in RFC 4122 big-endian format, which matches the GUID string display format.
 *
 * @param view - DataView to read from
 * @param offset - Byte offset to start reading
 * @returns GUID string in standard format (lowercase)
 */
declare function readGuid(view: DataView, offset: number): string;
/**
 * Generates an empty GUID (all zeros).
 */
declare const EMPTY_GUID = "00000000-0000-0000-0000-000000000000";
/**
 * Tests cross-platform compatibility of network byte order operations.
 * Useful for unit testing and validation.
 */
declare function testNetworkByteOrderCompatibility(): boolean;

/**
 * Represents a complete binary message in the Bannou WebSocket protocol.
 * Request messages use a 31-byte header, response messages use a 16-byte header.
 * Provides zero-copy parsing and serialization for optimal performance.
 */
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
declare const HEADER_SIZE = 31;
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
declare const RESPONSE_HEADER_SIZE = 16;
/**
 * Represents a parsed or constructed binary message.
 */
interface BinaryMessage {
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
declare function createRequest(flags: number, channel: number, sequenceNumber: number, serviceGuid: string, messageId: bigint, payload: Uint8Array): BinaryMessage;
/**
 * Creates a response message.
 */
declare function createResponse(flags: number, channel: number, sequenceNumber: number, messageId: bigint, responseCode: number, payload: Uint8Array): BinaryMessage;
/**
 * Creates a binary message from a JSON string payload.
 */
declare function fromJson(channel: number, sequenceNumber: number, serviceGuid: string, messageId: bigint, jsonPayload: string, additionalFlags?: number): BinaryMessage;
/**
 * Gets the JSON payload as a string (if the message is JSON).
 * @throws Error if the message has the Binary flag set
 */
declare function getJsonPayload(message: BinaryMessage): string;
/**
 * Parses a binary message from a byte array.
 * Automatically detects whether this is a request (31-byte header) or response (16-byte header).
 */
declare function parse(buffer: ArrayBuffer | Uint8Array): BinaryMessage;
/**
 * Serializes the binary message to a byte array.
 * Uses 31-byte header for requests, 16-byte header for responses.
 */
declare function toByteArray(message: BinaryMessage): Uint8Array;
/**
 * Returns true if this message expects a response.
 */
declare function expectsResponse(message: BinaryMessage): boolean;
/**
 * Returns true if this message is a response to another message.
 */
declare function isResponse(message: BinaryMessage): boolean;
/**
 * Returns true if this message should be routed to a client (not a service).
 */
declare function isClientRouted(message: BinaryMessage): boolean;
/**
 * Returns true if this is a successful response (ResponseCode is 0).
 */
declare function isSuccess(message: BinaryMessage): boolean;
/**
 * Returns true if this is an error response (ResponseCode is non-zero).
 */
declare function isError(message: BinaryMessage): boolean;
/**
 * Returns true if this message requests endpoint metadata.
 */
declare function isMeta(message: BinaryMessage): boolean;

/**
 * Brotli decompression for inbound WebSocket payloads.
 * Used when the server sends messages with the Compressed flag (0x04) set,
 * indicating the payload has been Brotli-compressed above a size threshold.
 *
 * In Node.js environments, automatically uses the built-in zlib module.
 * In browser environments, call {@link setPayloadDecompressor} to provide
 * a Brotli decompressor (e.g. from a WASM-based library).
 */

type DecompressFn = (data: Uint8Array) => Uint8Array;
/**
 * Initializes the decompressor by attempting to load Node.js zlib.
 * Call this once during client setup (e.g. in connect()).
 * Safe to call multiple times; only initializes once.
 */
declare function initializeDecompressor(): Promise<void>;
/**
 * Sets a custom Brotli decompressor function.
 * Use this in browser environments where Node.js zlib is not available.
 * The function must accept compressed bytes and return decompressed bytes.
 */
declare function setPayloadDecompressor(fn: DecompressFn): void;
/**
 * Decompresses the payload of a message that has the Compressed flag set.
 * Returns the message unchanged if the Compressed flag is not set or payload is empty.
 * Returns a new BinaryMessage with the decompressed payload and the Compressed flag cleared.
 *
 * @throws Error if the message is compressed but no decompressor is available
 */
declare function decompressPayload(message: BinaryMessage): BinaryMessage;
/**
 * Resets the decompressor state. Used for testing only.
 */
declare function resetDecompressor(): void;

export { type BinaryMessage, EMPTY_GUID, HEADER_SIZE, MessageFlags, type MessageFlagsType, RESPONSE_HEADER_SIZE, ResponseCodes, type ResponseCodesType, createRequest, createResponse, decompressPayload, expectsResponse, fromJson, getJsonPayload, getResponseCodeName, hasFlag, initializeDecompressor, isBinary as isBinaryFlag, isClientRouted, isCompressed as isCompressedFlag, isError, isError$1 as isErrorCode, isEvent as isEventFlag, isMeta, isMeta$1 as isMetaFlag, isResponse, isResponse$1 as isResponseFlag, isSuccess, isSuccess$1 as isSuccessCode, mapToHttpStatus, parse, readGuid, readUInt16, readUInt32, readUInt64, resetDecompressor, setPayloadDecompressor, testNetworkByteOrderCompatibility, toByteArray, writeGuid, writeUInt16, writeUInt32, writeUInt64 };
