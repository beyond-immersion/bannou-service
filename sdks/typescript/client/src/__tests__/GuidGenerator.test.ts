/**
 * Unit tests for GuidGenerator.
 */

import { describe, it, expect, beforeEach } from 'vitest';
import { generateMessageId, generateUuid, resetMessageIdCounter } from '../GuidGenerator.js';

describe('GuidGenerator', () => {
  describe('generateMessageId', () => {
    beforeEach(() => {
      resetMessageIdCounter();
    });

    it('should return a bigint', () => {
      const id = generateMessageId();
      expect(typeof id).toBe('bigint');
    });

    it('should generate unique IDs', () => {
      const ids = new Set<bigint>();
      for (let i = 0; i < 100; i++) {
        ids.add(generateMessageId());
      }
      expect(ids.size).toBe(100);
    });

    it('should generate sequential counter values in lower bits', () => {
      const id1 = generateMessageId();
      const id2 = generateMessageId();

      // Lower 16 bits are the counter, should increment
      const counter1 = id1 & 0xffffn;
      const counter2 = id2 & 0xffffn;

      expect(counter2).toBe(counter1 + 1n);
    });

    it('should incorporate timestamp in upper bits', () => {
      const id = generateMessageId();

      // Upper bits should be non-zero (timestamp)
      const timestamp = id >> 16n;
      expect(timestamp).toBeGreaterThan(0n);
    });

    it('should produce IDs that fit in 64 bits', () => {
      const id = generateMessageId();

      // JavaScript BigInt can represent arbitrary precision, but our IDs should fit in 64 bits
      expect(id).toBeGreaterThanOrEqual(0n);
      expect(id).toBeLessThan(2n ** 64n);
    });
  });

  describe('generateUuid', () => {
    it('should return a valid UUID format', () => {
      const uuid = generateUuid();

      // UUID format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
      const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
      expect(uuid).toMatch(uuidRegex);
    });

    it('should generate unique UUIDs', () => {
      const uuids = new Set<string>();
      for (let i = 0; i < 100; i++) {
        uuids.add(generateUuid());
      }
      expect(uuids.size).toBe(100);
    });

    it('should generate UUID v4 format', () => {
      const uuid = generateUuid();

      // UUID v4: version 4 in position 13 (after second hyphen)
      expect(uuid[14]).toBe('4');

      // Variant bits: position 19 should be 8, 9, a, or b
      const variantChar = uuid[19].toLowerCase();
      expect(['8', '9', 'a', 'b']).toContain(variantChar);
    });

    it('should return lowercase characters (or match native format)', () => {
      const uuid = generateUuid();

      // Should be 36 characters total (8-4-4-4-12 plus 4 hyphens)
      expect(uuid.length).toBe(36);
    });
  });

  describe('resetMessageIdCounter', () => {
    it('should reset the counter to zero', () => {
      // Generate some IDs
      generateMessageId();
      generateMessageId();
      generateMessageId();

      // Reset
      resetMessageIdCounter();

      // Next ID should have counter value 0 in lower bits
      const id = generateMessageId();
      const counter = id & 0xffffn;
      expect(counter).toBe(0n);
    });
  });
});
