/**
 * Generates unique message IDs and GUIDs for the Bannou protocol.
 */

/**
 * Counter for generating unique message IDs within a session.
 * Uses BigInt to support the 64-bit message ID space.
 */
let messageIdCounter = 0n;

/**
 * Generates a unique message ID for request/response correlation.
 * Message IDs are 64-bit unsigned integers.
 */
export function generateMessageId(): bigint {
  // Combine timestamp with counter for uniqueness across sessions
  const timestamp = BigInt(Date.now()) << 16n;
  const counter = messageIdCounter++ & 0xffffn;
  return timestamp | counter;
}

/**
 * Generates a random UUID v4.
 * Uses crypto.randomUUID() if available, otherwise falls back to manual generation.
 */
export function generateUuid(): string {
  // Use native crypto.randomUUID if available (modern browsers and Node.js 19+)
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  // Fallback: manual UUID v4 generation
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

/**
 * Resets the message ID counter (for testing).
 */
export function resetMessageIdCounter(): void {
  messageIdCounter = 0n;
}
