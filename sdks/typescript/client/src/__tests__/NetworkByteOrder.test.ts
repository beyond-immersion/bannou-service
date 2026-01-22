/**
 * Unit tests for NetworkByteOrder utilities.
 * Validates cross-platform binary protocol compatibility.
 */

import { describe, it, expect } from 'vitest';
import {
  writeUInt16,
  readUInt16,
  writeUInt32,
  readUInt32,
  writeUInt64,
  readUInt64,
  writeGuid,
  readGuid,
  EMPTY_GUID,
  testNetworkByteOrderCompatibility,
} from '../protocol/NetworkByteOrder.js';

describe('NetworkByteOrder', () => {
  describe('UInt16', () => {
    it('should write and read UInt16 in big-endian', () => {
      const buffer = new ArrayBuffer(2);
      const view = new DataView(buffer);

      writeUInt16(view, 0, 0x1234);

      // Verify big-endian byte order (high byte first)
      expect(new Uint8Array(buffer)[0]).toBe(0x12);
      expect(new Uint8Array(buffer)[1]).toBe(0x34);

      // Verify round-trip
      expect(readUInt16(view, 0)).toBe(0x1234);
    });

    it('should handle zero', () => {
      const buffer = new ArrayBuffer(2);
      const view = new DataView(buffer);

      writeUInt16(view, 0, 0);
      expect(readUInt16(view, 0)).toBe(0);
    });

    it('should handle max value', () => {
      const buffer = new ArrayBuffer(2);
      const view = new DataView(buffer);

      writeUInt16(view, 0, 0xffff);
      expect(readUInt16(view, 0)).toBe(0xffff);
    });
  });

  describe('UInt32', () => {
    it('should write and read UInt32 in big-endian', () => {
      const buffer = new ArrayBuffer(4);
      const view = new DataView(buffer);

      writeUInt32(view, 0, 0x12345678);

      // Verify big-endian byte order
      const bytes = new Uint8Array(buffer);
      expect(bytes[0]).toBe(0x12);
      expect(bytes[1]).toBe(0x34);
      expect(bytes[2]).toBe(0x56);
      expect(bytes[3]).toBe(0x78);

      // Verify round-trip
      expect(readUInt32(view, 0)).toBe(0x12345678);
    });

    it('should handle zero', () => {
      const buffer = new ArrayBuffer(4);
      const view = new DataView(buffer);

      writeUInt32(view, 0, 0);
      expect(readUInt32(view, 0)).toBe(0);
    });

    it('should handle max value', () => {
      const buffer = new ArrayBuffer(4);
      const view = new DataView(buffer);

      writeUInt32(view, 0, 0xffffffff);
      expect(readUInt32(view, 0)).toBe(0xffffffff);
    });
  });

  describe('UInt64', () => {
    it('should write and read UInt64 in big-endian', () => {
      const buffer = new ArrayBuffer(8);
      const view = new DataView(buffer);

      writeUInt64(view, 0, 0x123456789abcdef0n);

      // Verify big-endian byte order (high 32 bits, then low 32 bits)
      const bytes = new Uint8Array(buffer);
      expect(bytes[0]).toBe(0x12);
      expect(bytes[1]).toBe(0x34);
      expect(bytes[2]).toBe(0x56);
      expect(bytes[3]).toBe(0x78);
      expect(bytes[4]).toBe(0x9a);
      expect(bytes[5]).toBe(0xbc);
      expect(bytes[6]).toBe(0xde);
      expect(bytes[7]).toBe(0xf0);

      // Verify round-trip
      expect(readUInt64(view, 0)).toBe(0x123456789abcdef0n);
    });

    it('should handle zero', () => {
      const buffer = new ArrayBuffer(8);
      const view = new DataView(buffer);

      writeUInt64(view, 0, 0n);
      expect(readUInt64(view, 0)).toBe(0n);
    });

    it('should handle large values', () => {
      const buffer = new ArrayBuffer(8);
      const view = new DataView(buffer);

      // Test with a value that exercises both high and low 32-bit parts
      const testValue = 0xfedcba9876543210n;
      writeUInt64(view, 0, testValue);
      expect(readUInt64(view, 0)).toBe(testValue);
    });

    it('should handle max safe integer and beyond', () => {
      const buffer = new ArrayBuffer(8);
      const view = new DataView(buffer);

      // BigInt can handle values beyond Number.MAX_SAFE_INTEGER
      const testValue = 0xffffffffffffffffn;
      writeUInt64(view, 0, testValue);
      expect(readUInt64(view, 0)).toBe(testValue);
    });
  });

  describe('GUID', () => {
    it('should write and read GUID with RFC 4122 byte ordering', () => {
      const buffer = new ArrayBuffer(16);
      const view = new DataView(buffer);

      const testGuid = '12345678-1234-5678-9abc-def012345678';
      writeGuid(view, 0, testGuid);

      // Verify RFC 4122 byte transformation:
      // Time-low (bytes 0-3): reversed from 12345678 to 78563412
      const bytes = new Uint8Array(buffer);
      expect(bytes[0]).toBe(0x78);
      expect(bytes[1]).toBe(0x56);
      expect(bytes[2]).toBe(0x34);
      expect(bytes[3]).toBe(0x12);

      // Time-mid (bytes 4-5): reversed from 1234 to 3412
      expect(bytes[4]).toBe(0x34);
      expect(bytes[5]).toBe(0x12);

      // Time-high (bytes 6-7): reversed from 5678 to 7856
      expect(bytes[6]).toBe(0x78);
      expect(bytes[7]).toBe(0x56);

      // Clock-seq and node (bytes 8-15): unchanged
      expect(bytes[8]).toBe(0x9a);
      expect(bytes[9]).toBe(0xbc);
      expect(bytes[10]).toBe(0xde);
      expect(bytes[11]).toBe(0xf0);
      expect(bytes[12]).toBe(0x12);
      expect(bytes[13]).toBe(0x34);
      expect(bytes[14]).toBe(0x56);
      expect(bytes[15]).toBe(0x78);

      // Verify round-trip
      expect(readGuid(view, 0)).toBe(testGuid);
    });

    it('should handle empty GUID', () => {
      const buffer = new ArrayBuffer(16);
      const view = new DataView(buffer);

      writeGuid(view, 0, EMPTY_GUID);
      expect(readGuid(view, 0)).toBe(EMPTY_GUID);

      // Verify all zeros
      const bytes = new Uint8Array(buffer);
      for (let i = 0; i < 16; i++) {
        expect(bytes[i]).toBe(0);
      }
    });

    it('should handle uppercase input', () => {
      const buffer = new ArrayBuffer(16);
      const view = new DataView(buffer);

      const upperGuid = 'ABCDEF12-3456-789A-BCDE-F01234567890';
      writeGuid(view, 0, upperGuid);

      // readGuid returns lowercase
      expect(readGuid(view, 0)).toBe(upperGuid.toLowerCase());
    });

    it('should throw on invalid GUID format', () => {
      const buffer = new ArrayBuffer(16);
      const view = new DataView(buffer);

      expect(() => writeGuid(view, 0, 'invalid')).toThrow('Invalid GUID format');
      expect(() => writeGuid(view, 0, '12345678-1234')).toThrow('Invalid GUID format');
      expect(() => writeGuid(view, 0, '')).toThrow('Invalid GUID format');
    });

    it('should work with different GUIDs', () => {
      const buffer = new ArrayBuffer(16);
      const view = new DataView(buffer);

      const testGuids = [
        'ffffffff-ffff-ffff-ffff-ffffffffffff',
        'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
        '00000001-0002-0003-0004-000000000005',
      ];

      for (const guid of testGuids) {
        writeGuid(view, 0, guid);
        expect(readGuid(view, 0)).toBe(guid);
      }
    });
  });

  describe('testNetworkByteOrderCompatibility', () => {
    it('should pass the built-in compatibility test', () => {
      expect(testNetworkByteOrderCompatibility()).toBe(true);
    });
  });

  describe('offset handling', () => {
    it('should correctly handle non-zero offsets', () => {
      const buffer = new ArrayBuffer(64);
      const view = new DataView(buffer);

      // Write values at non-overlapping offsets
      writeUInt16(view, 0, 0xabcd);
      writeUInt32(view, 4, 0x12345678);
      writeUInt64(view, 10, 0xfedcba9876543210n);
      writeGuid(view, 20, 'abcdef12-3456-789a-bcde-f01234567890');

      // Verify each can be read back correctly from their offsets
      expect(readUInt16(view, 0)).toBe(0xabcd);
      expect(readUInt32(view, 4)).toBe(0x12345678);
      expect(readUInt64(view, 10)).toBe(0xfedcba9876543210n);
      expect(readGuid(view, 20)).toBe('abcdef12-3456-789a-bcde-f01234567890');
    });
  });
});
