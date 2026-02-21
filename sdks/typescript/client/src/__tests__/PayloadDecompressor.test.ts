/**
 * Unit tests for PayloadDecompressor.
 * Validates Brotli decompression of server-compressed WebSocket payloads.
 */

import { describe, it, expect, beforeEach } from 'vitest';
import { brotliCompressSync } from 'node:zlib';
import { MessageFlags } from '../protocol/MessageFlags.js';
import { createRequest, createResponse } from '../protocol/BinaryMessage.js';
import { EMPTY_GUID } from '../protocol/NetworkByteOrder.js';
import {
  initializeDecompressor,
  decompressPayload,
  setPayloadDecompressor,
  resetDecompressor,
} from '../protocol/PayloadDecompressor.js';

describe('PayloadDecompressor', () => {
  beforeEach(async () => {
    resetDecompressor();
    await initializeDecompressor();
  });

  describe('passthrough', () => {
    it('should return uncompressed request messages unchanged', () => {
      const payload = new TextEncoder().encode('{"test":"value"}');
      const msg = createRequest(
        MessageFlags.None,
        0,
        1,
        '12345678-1234-5678-9abc-def012345678',
        1n,
        payload
      );

      const result = decompressPayload(msg);

      expect(result.flags).toBe(msg.flags);
      expect(result.payload).toEqual(msg.payload);
    });

    it('should return uncompressed response messages unchanged', () => {
      const payload = new TextEncoder().encode('{"ok":true}');
      const msg = createResponse(MessageFlags.None, 0, 1, 1n, 0, payload);

      const result = decompressPayload(msg);

      expect(result.flags).toBe(msg.flags);
      expect(result.payload).toEqual(msg.payload);
    });

    it('should return compressed messages with empty payload unchanged', () => {
      const msg = createRequest(
        MessageFlags.Compressed,
        0,
        1,
        '12345678-1234-5678-9abc-def012345678',
        1n,
        new Uint8Array(0)
      );

      const result = decompressPayload(msg);

      expect(result.payload.length).toBe(0);
    });
  });

  describe('decompression', () => {
    it('should decompress a Brotli-compressed request payload', () => {
      const originalJson = '{"character":"Moira","realm":"Arcadia","level":42}';
      const originalBytes = new TextEncoder().encode(originalJson);
      const compressed = new Uint8Array(brotliCompressSync(Buffer.from(originalBytes)));

      const msg = createRequest(
        MessageFlags.Compressed,
        0,
        1,
        '12345678-1234-5678-9abc-def012345678',
        1n,
        compressed
      );

      const result = decompressPayload(msg);

      const resultJson = new TextDecoder().decode(result.payload);
      expect(resultJson).toBe(originalJson);
    });

    it('should decompress a Brotli-compressed response payload', () => {
      const originalJson = '{"status":"ok","data":{"items":[1,2,3]}}';
      const originalBytes = new TextEncoder().encode(originalJson);
      const compressed = new Uint8Array(brotliCompressSync(Buffer.from(originalBytes)));

      const msg = createResponse(MessageFlags.Compressed, 0, 1, 1n, 0, compressed);

      const result = decompressPayload(msg);

      const resultJson = new TextDecoder().decode(result.payload);
      expect(resultJson).toBe(originalJson);
    });

    it('should clear the Compressed flag after decompression', () => {
      const compressed = new Uint8Array(brotliCompressSync(Buffer.from('{"test":true}')));

      const msg = createRequest(
        MessageFlags.Compressed,
        0,
        1,
        '12345678-1234-5678-9abc-def012345678',
        1n,
        compressed
      );

      const result = decompressPayload(msg);

      expect(result.flags & MessageFlags.Compressed).toBe(0);
    });

    it('should preserve other flags when clearing Compressed', () => {
      const compressed = new Uint8Array(brotliCompressSync(Buffer.from('{"test":true}')));
      const flags = MessageFlags.Compressed | MessageFlags.Event;

      const msg = createRequest(
        flags,
        0,
        1,
        '12345678-1234-5678-9abc-def012345678',
        1n,
        compressed
      );

      const result = decompressPayload(msg);

      expect(result.flags & MessageFlags.Event).toBe(MessageFlags.Event);
      expect(result.flags & MessageFlags.Compressed).toBe(0);
    });

    it('should preserve all message fields except flags and payload', () => {
      const compressed = new Uint8Array(brotliCompressSync(Buffer.from('{"test":true}')));

      const msg = createRequest(
        MessageFlags.Compressed,
        42,
        999,
        'abcdef12-3456-789a-bcde-f01234567890',
        12345n,
        compressed
      );

      const result = decompressPayload(msg);

      expect(result.channel).toBe(42);
      expect(result.sequenceNumber).toBe(999);
      expect(result.serviceGuid).toBe('abcdef12-3456-789a-bcde-f01234567890');
      expect(result.messageId).toBe(12345n);
      expect(result.responseCode).toBe(0);
    });

    it('should handle large payloads', () => {
      // Create a large JSON payload that compresses well
      const largeObject = {
        items: Array.from({ length: 500 }, (_, i) => ({
          id: i,
          name: `item_${i}`,
          description: 'This is a repeated description that compresses well',
        })),
      };
      const originalJson = JSON.stringify(largeObject);
      const originalBytes = new TextEncoder().encode(originalJson);
      const compressed = new Uint8Array(brotliCompressSync(Buffer.from(originalBytes)));

      // Verify compression actually reduced size
      expect(compressed.length).toBeLessThan(originalBytes.length);

      const msg = createResponse(MessageFlags.Compressed, 0, 1, 1n, 0, compressed);
      const result = decompressPayload(msg);
      const resultJson = new TextDecoder().decode(result.payload);

      expect(resultJson).toBe(originalJson);
    });
  });

  describe('custom decompressor', () => {
    it('should use custom decompressor when set', () => {
      resetDecompressor();

      const mockPayload = new Uint8Array([1, 2, 3]);
      const expectedOutput = new Uint8Array([4, 5, 6]);

      setPayloadDecompressor(() => expectedOutput);

      const msg = createRequest(
        MessageFlags.Compressed,
        0,
        1,
        '12345678-1234-5678-9abc-def012345678',
        1n,
        mockPayload
      );

      const result = decompressPayload(msg);

      expect(result.payload).toEqual(expectedOutput);
    });

    it('should prefer custom decompressor over Node.js zlib', async () => {
      resetDecompressor();
      await initializeDecompressor();

      const customOutput = new Uint8Array([99, 99, 99]);
      setPayloadDecompressor(() => customOutput);

      const compressed = new Uint8Array(brotliCompressSync(Buffer.from('{"test":true}')));
      const msg = createRequest(
        MessageFlags.Compressed,
        0,
        1,
        '12345678-1234-5678-9abc-def012345678',
        1n,
        compressed
      );

      const result = decompressPayload(msg);

      // Should use custom, not Node.js zlib
      expect(result.payload).toEqual(customOutput);
    });
  });

  describe('error handling', () => {
    it('should throw when no decompressor is available', () => {
      resetDecompressor();
      // Do NOT call initializeDecompressor - simulates browser without custom decompressor

      const msg = createRequest(
        MessageFlags.Compressed,
        0,
        1,
        '12345678-1234-5678-9abc-def012345678',
        1n,
        new Uint8Array([1, 2, 3])
      );

      expect(() => decompressPayload(msg)).toThrow('no Brotli decompressor');
    });
  });
});
