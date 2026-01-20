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
export function writeUInt16(view: DataView, offset: number, value: number): void {
  view.setUint16(offset, value, false); // false = big-endian
}

/**
 * Writes a 32-bit unsigned integer in network byte order (big-endian).
 */
export function writeUInt32(view: DataView, offset: number, value: number): void {
  view.setUint32(offset, value, false); // false = big-endian
}

/**
 * Writes a 64-bit unsigned integer in network byte order (big-endian).
 * JavaScript's DataView doesn't have native BigInt support in all versions,
 * so we write it as two 32-bit values.
 */
export function writeUInt64(view: DataView, offset: number, value: bigint): void {
  const high = Number(value >> 32n);
  const low = Number(value & 0xffffffffn);
  view.setUint32(offset, high, false); // High 32 bits first (big-endian)
  view.setUint32(offset + 4, low, false); // Low 32 bits second
}

/**
 * Reads a 16-bit unsigned integer from network byte order (big-endian).
 */
export function readUInt16(view: DataView, offset: number): number {
  return view.getUint16(offset, false); // false = big-endian
}

/**
 * Reads a 32-bit unsigned integer from network byte order (big-endian).
 */
export function readUInt32(view: DataView, offset: number): number {
  return view.getUint32(offset, false); // false = big-endian
}

/**
 * Reads a 64-bit unsigned integer from network byte order (big-endian).
 */
export function readUInt64(view: DataView, offset: number): bigint {
  const high = BigInt(view.getUint32(offset, false));
  const low = BigInt(view.getUint32(offset + 4, false));
  return (high << 32n) | low;
}

/**
 * Writes a GUID in consistent network byte order.
 * Uses RFC 4122 standard byte ordering for cross-platform compatibility.
 *
 * RFC 4122 specifies that the time fields (first 8 bytes) use network byte order.
 * .NET's Guid.ToByteArray() returns little-endian for time fields, so we need
 * to reverse bytes 0-3, 4-5, 6-7 when writing. Bytes 8-15 remain as-is.
 *
 * @param view - DataView to write to
 * @param offset - Byte offset to start writing
 * @param guid - GUID string in standard format (e.g., "12345678-1234-5678-9abc-def012345678")
 */
export function writeGuid(view: DataView, offset: number, guid: string): void {
  // Parse GUID string (format: "12345678-1234-5678-9abc-def012345678")
  const cleanGuid = guid.replace(/-/g, '');

  if (cleanGuid.length !== 32) {
    throw new Error(`Invalid GUID format: ${guid}`);
  }

  // Parse each hex pair as bytes
  const bytes = new Uint8Array(16);
  for (let i = 0; i < 16; i++) {
    bytes[i] = parseInt(cleanGuid.slice(i * 2, i * 2 + 2), 16);
  }

  // RFC 4122 network byte order transformation:
  // Time-low (4 bytes, indices 0-3) - reverse
  view.setUint8(offset + 0, bytes[3]);
  view.setUint8(offset + 1, bytes[2]);
  view.setUint8(offset + 2, bytes[1]);
  view.setUint8(offset + 3, bytes[0]);

  // Time-mid (2 bytes, indices 4-5) - reverse
  view.setUint8(offset + 4, bytes[5]);
  view.setUint8(offset + 5, bytes[4]);

  // Time-high-and-version (2 bytes, indices 6-7) - reverse
  view.setUint8(offset + 6, bytes[7]);
  view.setUint8(offset + 7, bytes[6]);

  // Clock-seq-and-reserved + clock-seq-low + node (8 bytes, indices 8-15) - keep as-is
  for (let i = 8; i < 16; i++) {
    view.setUint8(offset + i, bytes[i]);
  }
}

/**
 * Reads a GUID from consistent network byte order.
 * Uses RFC 4122 standard byte ordering for cross-platform compatibility.
 *
 * @param view - DataView to read from
 * @param offset - Byte offset to start reading
 * @returns GUID string in standard format (lowercase)
 */
export function readGuid(view: DataView, offset: number): string {
  const bytes = new Uint8Array(16);

  // Reverse the RFC 4122 transformation:
  // Time-low (4 bytes) - reverse back
  bytes[0] = view.getUint8(offset + 3);
  bytes[1] = view.getUint8(offset + 2);
  bytes[2] = view.getUint8(offset + 1);
  bytes[3] = view.getUint8(offset + 0);

  // Time-mid (2 bytes) - reverse back
  bytes[4] = view.getUint8(offset + 5);
  bytes[5] = view.getUint8(offset + 4);

  // Time-high-and-version (2 bytes) - reverse back
  bytes[6] = view.getUint8(offset + 7);
  bytes[7] = view.getUint8(offset + 6);

  // Clock-seq and node (8 bytes) - keep as-is
  for (let i = 8; i < 16; i++) {
    bytes[i] = view.getUint8(offset + i);
  }

  // Format as GUID string
  const hex = Array.from(bytes)
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('');

  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20, 32)}`;
}

/**
 * Generates an empty GUID (all zeros).
 */
export const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

/**
 * Tests cross-platform compatibility of network byte order operations.
 * Useful for unit testing and validation.
 */
export function testNetworkByteOrderCompatibility(): boolean {
  try {
    const buffer = new ArrayBuffer(32);
    const view = new DataView(buffer);

    // Test all data types
    writeUInt16(view, 0, 0x1234);
    writeUInt32(view, 2, 0x12345678);
    writeUInt64(view, 6, 0x123456789abcdef0n);

    const testGuid = '12345678-1234-5678-9abc-def012345678';
    writeGuid(view, 14, testGuid);

    // Read back and verify
    const readU16 = readUInt16(view, 0);
    const readU32 = readUInt32(view, 2);
    const readU64 = readUInt64(view, 6);
    const readG = readGuid(view, 14);

    return (
      readU16 === 0x1234 &&
      readU32 === 0x12345678 &&
      readU64 === 0x123456789abcdef0n &&
      readG === testGuid
    );
  } catch {
    return false;
  }
}
