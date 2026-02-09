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

  // Bits 0x02, 0x04, 0x08 are reserved. These were originally defined as Encrypted,
  // Compressed, and HighPriority respectively, but all three were removed because they
  // conflict with Connect's zero-copy routing architecture. Connect forwards payloads
  // unchanged to backend services; gateway-level encryption, compression, or priority
  // queuing would require payload transformation, destroying that property.
  // Transport-level concerns (TLS, permessage-deflate) handle encryption and compression.
  // Endpoint-to-endpoint concerns are signaled by clients and handled by target services.
  // Values are preserved to avoid shifting existing flag assignments.

  /** Reserved for future use. Do not assign. */
  Reserved0x02: 0x02,

  /** Reserved for future use. Do not assign. */
  Reserved0x04: 0x04,

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
