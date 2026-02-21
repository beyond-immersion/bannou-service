/**
 * Unit tests for MessageFlags utilities.
 */

import { describe, it, expect } from 'vitest';
import {
  MessageFlags,
  hasFlag,
  isResponse,
  isEvent,
  isMeta,
  isBinary,
  isCompressed,
} from '../protocol/MessageFlags.js';

describe('MessageFlags', () => {
  describe('flag values', () => {
    it('should have correct bit values', () => {
      expect(MessageFlags.None).toBe(0x00);
      expect(MessageFlags.Binary).toBe(0x01);
      expect(MessageFlags.Reserved0x02).toBe(0x02);
      expect(MessageFlags.Compressed).toBe(0x04);
      expect(MessageFlags.Reserved0x08).toBe(0x08);
      expect(MessageFlags.Event).toBe(0x10);
      expect(MessageFlags.Client).toBe(0x20);
      expect(MessageFlags.Response).toBe(0x40);
      expect(MessageFlags.Meta).toBe(0x80);
    });

    it('should be distinct powers of 2', () => {
      const flags = [
        MessageFlags.Binary,
        MessageFlags.Reserved0x02,
        MessageFlags.Compressed,
        MessageFlags.Reserved0x08,
        MessageFlags.Event,
        MessageFlags.Client,
        MessageFlags.Response,
        MessageFlags.Meta,
      ];

      // Each flag should be a power of 2
      for (const flag of flags) {
        expect(flag > 0).toBe(true);
        expect((flag & (flag - 1)) === 0).toBe(true); // Power of 2 check
      }

      // No two flags should be equal
      const unique = new Set(flags);
      expect(unique.size).toBe(flags.length);
    });
  });

  describe('hasFlag', () => {
    it('should detect single flags', () => {
      expect(hasFlag(MessageFlags.Binary, MessageFlags.Binary)).toBe(true);
      expect(hasFlag(MessageFlags.Response, MessageFlags.Response)).toBe(true);
      expect(hasFlag(MessageFlags.None, MessageFlags.Binary)).toBe(false);
    });

    it('should detect flags in combined values', () => {
      const combined = MessageFlags.Binary | MessageFlags.Reserved0x02 | MessageFlags.Reserved0x08;

      expect(hasFlag(combined, MessageFlags.Binary)).toBe(true);
      expect(hasFlag(combined, MessageFlags.Reserved0x02)).toBe(true);
      expect(hasFlag(combined, MessageFlags.Reserved0x08)).toBe(true);
      expect(hasFlag(combined, MessageFlags.Response)).toBe(false);
      expect(hasFlag(combined, MessageFlags.Event)).toBe(false);
    });

    it('should handle None flag', () => {
      expect(hasFlag(MessageFlags.None, MessageFlags.None)).toBe(true);
      expect(hasFlag(MessageFlags.Binary, MessageFlags.None)).toBe(true);
    });
  });

  describe('isResponse', () => {
    it('should return true when Response flag is set', () => {
      expect(isResponse(MessageFlags.Response)).toBe(true);
      expect(isResponse(MessageFlags.Response | MessageFlags.Reserved0x08)).toBe(true);
    });

    it('should return false when Response flag is not set', () => {
      expect(isResponse(MessageFlags.None)).toBe(false);
      expect(isResponse(MessageFlags.Binary | MessageFlags.Event)).toBe(false);
    });
  });

  describe('isEvent', () => {
    it('should return true when Event flag is set', () => {
      expect(isEvent(MessageFlags.Event)).toBe(true);
      expect(isEvent(MessageFlags.Event | MessageFlags.Binary)).toBe(true);
    });

    it('should return false when Event flag is not set', () => {
      expect(isEvent(MessageFlags.None)).toBe(false);
      expect(isEvent(MessageFlags.Response)).toBe(false);
    });
  });

  describe('isMeta', () => {
    it('should return true when Meta flag is set', () => {
      expect(isMeta(MessageFlags.Meta)).toBe(true);
      expect(isMeta(MessageFlags.Meta | MessageFlags.Reserved0x08)).toBe(true);
    });

    it('should return false when Meta flag is not set', () => {
      expect(isMeta(MessageFlags.None)).toBe(false);
      expect(isMeta(MessageFlags.Response)).toBe(false);
    });
  });

  describe('isBinary', () => {
    it('should return true when Binary flag is set', () => {
      expect(isBinary(MessageFlags.Binary)).toBe(true);
      expect(isBinary(MessageFlags.Binary | MessageFlags.Compressed)).toBe(true);
    });

    it('should return false when Binary flag is not set', () => {
      expect(isBinary(MessageFlags.None)).toBe(false);
      expect(isBinary(MessageFlags.Response)).toBe(false);
    });
  });

  describe('isCompressed', () => {
    it('should return true when Compressed flag is set', () => {
      expect(isCompressed(MessageFlags.Compressed)).toBe(true);
      expect(isCompressed(MessageFlags.Compressed | MessageFlags.Response)).toBe(true);
    });

    it('should return false when Compressed flag is not set', () => {
      expect(isCompressed(MessageFlags.None)).toBe(false);
      expect(isCompressed(MessageFlags.Binary)).toBe(false);
    });
  });

  describe('flag combinations', () => {
    it('should allow combining multiple flags with bitwise OR', () => {
      const combined = MessageFlags.Binary | MessageFlags.Reserved0x02 | MessageFlags.Reserved0x08;

      expect(combined).toBe(0x01 | 0x02 | 0x08);
      expect(combined).toBe(0x0b);
    });

    it('should allow combining all flags', () => {
      const all =
        MessageFlags.Binary |
        MessageFlags.Reserved0x02 |
        MessageFlags.Compressed |
        MessageFlags.Reserved0x08 |
        MessageFlags.Event |
        MessageFlags.Client |
        MessageFlags.Response |
        MessageFlags.Meta;
      expect(all).toBe(0xff);
    });
  });
});
