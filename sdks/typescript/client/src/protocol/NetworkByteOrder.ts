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
export function writeGuid(view: DataView, offset: number, guid: string): void {
  // Parse GUID string (format: "12345678-1234-5678-9abc-def012345678")
  const cleanGuid = guid.replace(/-/g, '');

  if (cleanGuid.length !== 32) {
    throw new Error(`Invalid GUID format: ${guid}`);
  }

  // Parse each hex pair as bytes and write directly.
  // The GUID string is already in RFC 4122 big-endian display format,
  // so NO byte reversal is needed (unlike C# which reverses from little-endian).
  for (let i = 0; i < 16; i++) {
    const byte = parseInt(cleanGuid.slice(i * 2, i * 2 + 2), 16);
    view.setUint8(offset + i, byte);
  }
}

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
export function readGuid(view: DataView, offset: number): string {
  // Read bytes directly - they're already in RFC 4122 big-endian format
  // which matches the GUID string display format (no reversal needed)
  const hexParts: string[] = [];
  for (let i = 0; i < 16; i++) {
    hexParts.push(
      view
        .getUint8(offset + i)
        .toString(16)
        .padStart(2, '0')
    );
  }
  const hex = hexParts.join('');

  return (
    `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-` +
    `${hex.slice(16, 20)}-${hex.slice(20, 32)}`
  );
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
