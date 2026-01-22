/**
 * Unit tests for BinaryMessage serialization/deserialization.
 * Validates the binary WebSocket protocol format.
 */

import { describe, it, expect } from 'vitest';
import {
  HEADER_SIZE,
  RESPONSE_HEADER_SIZE,
  createRequest,
  createResponse,
  fromJson,
  getJsonPayload,
  parse,
  toByteArray,
  expectsResponse,
  isResponse,
  isClientRouted,
  isHighPriority,
  isSuccess,
  isError,
  isMeta,
  type BinaryMessage,
} from '../protocol/BinaryMessage.js';
import { MessageFlags } from '../protocol/MessageFlags.js';
import { EMPTY_GUID } from '../protocol/NetworkByteOrder.js';

describe('BinaryMessage', () => {
  describe('constants', () => {
    it('should have correct header sizes', () => {
      expect(HEADER_SIZE).toBe(31);
      expect(RESPONSE_HEADER_SIZE).toBe(16);
    });
  });

  describe('createRequest', () => {
    it('should create a request message with correct fields', () => {
      const msg = createRequest(
        MessageFlags.None,
        1,
        42,
        '12345678-1234-5678-9abc-def012345678',
        123456789n,
        new Uint8Array([1, 2, 3])
      );

      expect(msg.flags).toBe(MessageFlags.None);
      expect(msg.channel).toBe(1);
      expect(msg.sequenceNumber).toBe(42);
      expect(msg.serviceGuid).toBe('12345678-1234-5678-9abc-def012345678');
      expect(msg.messageId).toBe(123456789n);
      expect(msg.responseCode).toBe(0);
      expect(msg.payload).toEqual(new Uint8Array([1, 2, 3]));
    });
  });

  describe('createResponse', () => {
    it('should create a response message with Response flag set', () => {
      const msg = createResponse(
        MessageFlags.None,
        1,
        42,
        123456789n,
        0,
        new Uint8Array([1, 2, 3])
      );

      expect(msg.flags & MessageFlags.Response).toBe(MessageFlags.Response);
      expect(msg.channel).toBe(1);
      expect(msg.sequenceNumber).toBe(42);
      expect(msg.serviceGuid).toBe(EMPTY_GUID);
      expect(msg.messageId).toBe(123456789n);
      expect(msg.responseCode).toBe(0);
      expect(msg.payload).toEqual(new Uint8Array([1, 2, 3]));
    });

    it('should preserve additional flags', () => {
      const msg = createResponse(
        MessageFlags.HighPriority,
        1,
        42,
        123456789n,
        0,
        new Uint8Array([])
      );

      expect(msg.flags & MessageFlags.Response).toBe(MessageFlags.Response);
      expect(msg.flags & MessageFlags.HighPriority).toBe(MessageFlags.HighPriority);
    });
  });

  describe('fromJson', () => {
    it('should create a message from JSON string', () => {
      const json = '{"test": "value"}';
      const msg = fromJson(1, 42, '12345678-1234-5678-9abc-def012345678', 123456789n, json);

      expect(msg.flags).toBe(MessageFlags.None);
      expect(msg.channel).toBe(1);
      expect(getJsonPayload(msg)).toBe(json);
    });

    it('should support additional flags', () => {
      const msg = fromJson(
        0,
        1,
        '12345678-1234-5678-9abc-def012345678',
        1n,
        '{}',
        MessageFlags.HighPriority
      );

      expect(msg.flags).toBe(MessageFlags.HighPriority);
    });
  });

  describe('getJsonPayload', () => {
    it('should decode JSON payload', () => {
      const json = '{"key": "value", "number": 42}';
      const msg = fromJson(0, 1, '12345678-1234-5678-9abc-def012345678', 1n, json);

      expect(getJsonPayload(msg)).toBe(json);
    });

    it('should throw for binary messages', () => {
      const msg = createRequest(
        MessageFlags.Binary,
        0,
        1,
        '12345678-1234-5678-9abc-def012345678',
        1n,
        new Uint8Array([1, 2, 3])
      );

      expect(() => getJsonPayload(msg)).toThrow('Cannot get JSON payload from binary message');
    });
  });

  describe('toByteArray and parse', () => {
    describe('request messages', () => {
      it('should serialize to 31-byte header + payload', () => {
        const msg = fromJson(2, 100, 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee', 999n, '{"test":true}');

        const bytes = toByteArray(msg);

        // Header (31 bytes) + payload
        expect(bytes.length).toBe(31 + msg.payload.length);
      });

      it('should round-trip request messages', () => {
        const original = createRequest(
          MessageFlags.HighPriority,
          1000,
          50000,
          'abcdef12-3456-789a-bcde-f01234567890',
          0xfedcba9876543210n,
          new Uint8Array([0x48, 0x65, 0x6c, 0x6c, 0x6f]) // "Hello"
        );

        const bytes = toByteArray(original);
        const parsed = parse(bytes);

        expect(parsed.flags).toBe(original.flags);
        expect(parsed.channel).toBe(original.channel);
        expect(parsed.sequenceNumber).toBe(original.sequenceNumber);
        expect(parsed.serviceGuid).toBe(original.serviceGuid);
        expect(parsed.messageId).toBe(original.messageId);
        expect(parsed.responseCode).toBe(0);
        expect(parsed.payload).toEqual(original.payload);
      });

      it('should handle empty payload', () => {
        const msg = createRequest(
          MessageFlags.None,
          0,
          1,
          '00000000-0000-0000-0000-000000000000',
          1n,
          new Uint8Array(0)
        );

        const bytes = toByteArray(msg);
        expect(bytes.length).toBe(HEADER_SIZE);

        const parsed = parse(bytes);
        expect(parsed.payload.length).toBe(0);
      });
    });

    describe('response messages', () => {
      it('should serialize to 16-byte header + payload', () => {
        const msg = createResponse(
          MessageFlags.None,
          1,
          42,
          123456789n,
          0,
          new Uint8Array([1, 2, 3])
        );

        const bytes = toByteArray(msg);

        // Response header (16 bytes) + payload
        expect(bytes.length).toBe(16 + 3);
      });

      it('should round-trip response messages', () => {
        const original = createResponse(
          MessageFlags.HighPriority,
          500,
          12345,
          0x123456789n,
          0, // Success
          new Uint8Array([0x7b, 0x7d]) // "{}"
        );

        const bytes = toByteArray(original);
        const parsed = parse(bytes);

        expect(parsed.flags & MessageFlags.Response).toBe(MessageFlags.Response);
        expect(parsed.channel).toBe(original.channel);
        expect(parsed.sequenceNumber).toBe(original.sequenceNumber);
        expect(parsed.serviceGuid).toBe(EMPTY_GUID);
        expect(parsed.messageId).toBe(original.messageId);
        expect(parsed.responseCode).toBe(0);
        expect(parsed.payload).toEqual(original.payload);
      });

      it('should handle error response codes', () => {
        const errorCodes = [50, 51, 52, 53, 60]; // Common response codes

        for (const code of errorCodes) {
          const msg = createResponse(
            MessageFlags.None,
            0,
            1,
            1n,
            code,
            new Uint8Array(0) // Error responses typically have empty payload
          );

          const bytes = toByteArray(msg);
          const parsed = parse(bytes);

          expect(parsed.responseCode).toBe(code);
        }
      });
    });
  });

  describe('parse error handling', () => {
    it('should throw on empty buffer', () => {
      expect(() => parse(new Uint8Array(0))).toThrow('Expected at least 1 byte');
    });

    it('should throw on truncated request header', () => {
      // Request with no Response flag needs 31 bytes minimum
      const truncated = new Uint8Array(20);
      truncated[0] = MessageFlags.None; // Not a response

      expect(() => parse(truncated)).toThrow('Expected at least 31 bytes');
    });

    it('should throw on truncated response header', () => {
      // Response needs 16 bytes minimum
      const truncated = new Uint8Array(10);
      truncated[0] = MessageFlags.Response;

      expect(() => parse(truncated)).toThrow('Expected at least 16 bytes');
    });
  });

  describe('flag helpers', () => {
    describe('expectsResponse', () => {
      it('should return true for normal requests', () => {
        const msg = createRequest(
          MessageFlags.None,
          0,
          1,
          '12345678-1234-5678-9abc-def012345678',
          1n,
          new Uint8Array(0)
        );
        expect(expectsResponse(msg)).toBe(true);
      });

      it('should return false for events', () => {
        const msg = createRequest(
          MessageFlags.Event,
          0,
          1,
          '12345678-1234-5678-9abc-def012345678',
          1n,
          new Uint8Array(0)
        );
        expect(expectsResponse(msg)).toBe(false);
      });

      it('should return false for responses', () => {
        const msg = createResponse(MessageFlags.None, 0, 1, 1n, 0, new Uint8Array(0));
        expect(expectsResponse(msg)).toBe(false);
      });
    });

    describe('isResponse', () => {
      it('should return true for response messages', () => {
        const msg = createResponse(MessageFlags.None, 0, 1, 1n, 0, new Uint8Array(0));
        expect(isResponse(msg)).toBe(true);
      });

      it('should return false for request messages', () => {
        const msg = createRequest(
          MessageFlags.None,
          0,
          1,
          '12345678-1234-5678-9abc-def012345678',
          1n,
          new Uint8Array(0)
        );
        expect(isResponse(msg)).toBe(false);
      });
    });

    describe('isClientRouted', () => {
      it('should detect Client flag', () => {
        const msg = createRequest(
          MessageFlags.Client,
          0,
          1,
          '12345678-1234-5678-9abc-def012345678',
          1n,
          new Uint8Array(0)
        );
        expect(isClientRouted(msg)).toBe(true);
      });

      it('should return false without Client flag', () => {
        const msg = createRequest(
          MessageFlags.None,
          0,
          1,
          '12345678-1234-5678-9abc-def012345678',
          1n,
          new Uint8Array(0)
        );
        expect(isClientRouted(msg)).toBe(false);
      });
    });

    describe('isHighPriority', () => {
      it('should detect HighPriority flag', () => {
        const msg = createRequest(
          MessageFlags.HighPriority,
          0,
          1,
          '12345678-1234-5678-9abc-def012345678',
          1n,
          new Uint8Array(0)
        );
        expect(isHighPriority(msg)).toBe(true);
      });
    });

    describe('isSuccess', () => {
      it('should return true for success response', () => {
        const msg = createResponse(MessageFlags.None, 0, 1, 1n, 0, new Uint8Array(0));
        expect(isSuccess(msg)).toBe(true);
      });

      it('should return false for error response', () => {
        const msg = createResponse(MessageFlags.None, 0, 1, 1n, 50, new Uint8Array(0));
        expect(isSuccess(msg)).toBe(false);
      });

      it('should return false for non-response', () => {
        const msg = createRequest(
          MessageFlags.None,
          0,
          1,
          '12345678-1234-5678-9abc-def012345678',
          1n,
          new Uint8Array(0)
        );
        expect(isSuccess(msg)).toBe(false);
      });
    });

    describe('isError', () => {
      it('should return true for error response', () => {
        const msg = createResponse(MessageFlags.None, 0, 1, 1n, 50, new Uint8Array(0));
        expect(isError(msg)).toBe(true);
      });

      it('should return false for success response', () => {
        const msg = createResponse(MessageFlags.None, 0, 1, 1n, 0, new Uint8Array(0));
        expect(isError(msg)).toBe(false);
      });
    });

    describe('isMeta', () => {
      it('should detect Meta flag', () => {
        const msg = createRequest(
          MessageFlags.Meta,
          0,
          1,
          '12345678-1234-5678-9abc-def012345678',
          1n,
          new Uint8Array(0)
        );
        expect(isMeta(msg)).toBe(true);
      });
    });
  });

  describe('combined flags', () => {
    it('should handle multiple flags', () => {
      const flags = MessageFlags.HighPriority | MessageFlags.Binary | MessageFlags.Encrypted;
      const msg = createRequest(
        flags,
        0,
        1,
        '12345678-1234-5678-9abc-def012345678',
        1n,
        new Uint8Array([1, 2, 3])
      );

      const bytes = toByteArray(msg);
      const parsed = parse(bytes);

      expect(parsed.flags).toBe(flags);
      expect(isHighPriority(parsed)).toBe(true);
    });
  });
});
