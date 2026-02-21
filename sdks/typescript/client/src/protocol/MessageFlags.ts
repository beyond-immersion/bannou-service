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
export const MessageFlags = {
  /**
   * Default - JSON text, service request, unencrypted, standard priority, expects response
   */
  None: 0x00,

  /**
   * Message payload is binary data (not JSON)
   */
  Binary: 0x01,

  // Bit 0x02 is reserved (originally Encrypted, removed).
  // Bit 0x08 is reserved (originally HighPriority, removed).
  // Values are preserved to avoid shifting existing flag assignments.

  /** Reserved for future use. Do not assign. */
  Reserved0x02: 0x02,

  /**
   * Payload is Brotli-compressed. Client must decompress before parsing.
   * Only set on server-to-client messages when compression is enabled and
   * payload exceeds the configured size threshold.
   */
  Compressed: 0x04,

  /** Reserved for future use. Do not assign. */
  Reserved0x08: 0x08,

  /**
   * Fire-and-forget message, no response expected
   */
  Event: 0x10,

  /**
   * Route to another WebSocket client (not a Bannou service)
   */
  Client: 0x20,

  /**
   * Message is a response to an RPC (not a new request)
   */
  Response: 0x40,

  /**
   * Request metadata about endpoint instead of executing it.
   * When set, the Channel field specifies the meta type:
   * 0 = endpoint-info, 1 = request-schema, 2 = response-schema, 3 = full-schema.
   * Connect service transforms the path and routes to companion endpoints.
   */
  Meta: 0x80,
} as const;

export type MessageFlagsType = (typeof MessageFlags)[keyof typeof MessageFlags];

/**
 * Check if a flags byte has a specific flag set.
 */
export function hasFlag(flags: number, flag: number): boolean {
  return (flags & flag) === flag;
}

/**
 * Check if this is a response message.
 */
export function isResponse(flags: number): boolean {
  return hasFlag(flags, MessageFlags.Response);
}

/**
 * Check if this is an event message (fire-and-forget).
 */
export function isEvent(flags: number): boolean {
  return hasFlag(flags, MessageFlags.Event);
}

/**
 * Check if this is a meta request.
 */
export function isMeta(flags: number): boolean {
  return hasFlag(flags, MessageFlags.Meta);
}

/**
 * Check if payload is binary (not JSON).
 */
export function isBinary(flags: number): boolean {
  return hasFlag(flags, MessageFlags.Binary);
}

/**
 * Check if payload is Brotli-compressed.
 */
export function isCompressed(flags: number): boolean {
  return hasFlag(flags, MessageFlags.Compressed);
}
