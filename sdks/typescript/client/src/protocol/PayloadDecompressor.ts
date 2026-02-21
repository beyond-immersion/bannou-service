/**
 * Brotli decompression for inbound WebSocket payloads.
 * Used when the server sends messages with the Compressed flag (0x04) set,
 * indicating the payload has been Brotli-compressed above a size threshold.
 *
 * In Node.js environments, automatically uses the built-in zlib module.
 * In browser environments, call {@link setPayloadDecompressor} to provide
 * a Brotli decompressor (e.g. from a WASM-based library).
 */

import { MessageFlags, hasFlag } from './MessageFlags.js';
import type { BinaryMessage } from './BinaryMessage.js';

type DecompressFn = (data: Uint8Array) => Uint8Array;

let _nodeDecompressor: DecompressFn | null = null;
let _customDecompressor: DecompressFn | null = null;
let _initialized = false;

/**
 * Initializes the decompressor by attempting to load Node.js zlib.
 * Call this once during client setup (e.g. in connect()).
 * Safe to call multiple times; only initializes once.
 */
export async function initializeDecompressor(): Promise<void> {
  if (_initialized) return;
  _initialized = true;

  try {
    const zlib = await import('node:zlib');
    _nodeDecompressor = (data: Uint8Array): Uint8Array => {
      const result = zlib.brotliDecompressSync(data);
      return new Uint8Array(result.buffer, result.byteOffset, result.byteLength);
    };
  } catch {
    // Not in Node.js or zlib not available - browser environment
  }
}

/**
 * Sets a custom Brotli decompressor function.
 * Use this in browser environments where Node.js zlib is not available.
 * The function must accept compressed bytes and return decompressed bytes.
 */
export function setPayloadDecompressor(fn: DecompressFn): void {
  _customDecompressor = fn;
}

/**
 * Returns the active decompressor function, or null if none available.
 */
function getDecompressor(): DecompressFn | null {
  return _customDecompressor ?? _nodeDecompressor;
}

/**
 * Decompresses the payload of a message that has the Compressed flag set.
 * Returns the message unchanged if the Compressed flag is not set or payload is empty.
 * Returns a new BinaryMessage with the decompressed payload and the Compressed flag cleared.
 *
 * @throws Error if the message is compressed but no decompressor is available
 */
export function decompressPayload(message: BinaryMessage): BinaryMessage {
  if (!hasFlag(message.flags, MessageFlags.Compressed)) {
    return message;
  }

  if (message.payload.length === 0) {
    return message;
  }

  const decompress = getDecompressor();
  if (!decompress) {
    throw new Error(
      'Received compressed message but no Brotli decompressor is available. ' +
        'In browser environments, call setPayloadDecompressor() with a Brotli implementation.'
    );
  }

  const decompressed = decompress(message.payload);
  return {
    ...message,
    flags: message.flags & ~MessageFlags.Compressed,
    payload: decompressed,
  };
}

/**
 * Resets the decompressor state. Used for testing only.
 */
export function resetDecompressor(): void {
  _nodeDecompressor = null;
  _customDecompressor = null;
  _initialized = false;
}
