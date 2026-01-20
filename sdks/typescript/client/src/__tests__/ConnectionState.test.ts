/**
 * Unit tests for ConnectionState.
 */

import { describe, it, expect, beforeEach } from 'vitest';
import { ConnectionState } from '../ConnectionState.js';

describe('ConnectionState', () => {
  let state: ConnectionState;

  beforeEach(() => {
    state = new ConnectionState('test-session-id');
  });

  describe('constructor', () => {
    it('should store the session ID', () => {
      expect(state.sessionId).toBe('test-session-id');
    });

    it('should initialize with empty state', () => {
      expect(state.getServiceMappings().size).toBe(0);
      expect(state.getPendingMessageCount()).toBe(0);
      expect(state.getCurrentSequenceNumber(0)).toBe(0);
    });
  });

  describe('sequence numbers', () => {
    it('should return 0 for first sequence number on a channel', () => {
      const seq = state.getNextSequenceNumber(0);
      expect(seq).toBe(0);
    });

    it('should increment sequence numbers', () => {
      const seq1 = state.getNextSequenceNumber(0);
      const seq2 = state.getNextSequenceNumber(0);
      const seq3 = state.getNextSequenceNumber(0);

      expect(seq1).toBe(0);
      expect(seq2).toBe(1);
      expect(seq3).toBe(2);
    });

    it('should maintain separate sequence numbers per channel', () => {
      const seq0_1 = state.getNextSequenceNumber(0);
      const seq1_1 = state.getNextSequenceNumber(1);
      const seq0_2 = state.getNextSequenceNumber(0);
      const seq1_2 = state.getNextSequenceNumber(1);

      expect(seq0_1).toBe(0);
      expect(seq1_1).toBe(0);
      expect(seq0_2).toBe(1);
      expect(seq1_2).toBe(1);
    });

    it('should get current sequence number without incrementing', () => {
      state.getNextSequenceNumber(5); // 0
      state.getNextSequenceNumber(5); // 1

      const current = state.getCurrentSequenceNumber(5);
      expect(current).toBe(2); // After two increments

      // Should not have changed
      expect(state.getCurrentSequenceNumber(5)).toBe(2);
    });

    it('should return 0 for uninitialized channel in getCurrentSequenceNumber', () => {
      expect(state.getCurrentSequenceNumber(99)).toBe(0);
    });
  });

  describe('service mappings', () => {
    it('should add and retrieve service mappings', () => {
      state.addServiceMapping('POST:/account/get', 'guid-123');

      expect(state.getServiceGuid('POST:/account/get')).toBe('guid-123');
    });

    it('should return undefined for unknown endpoints', () => {
      expect(state.getServiceGuid('POST:/unknown')).toBeUndefined();
    });

    it('should overwrite existing mappings', () => {
      state.addServiceMapping('POST:/test', 'guid-1');
      state.addServiceMapping('POST:/test', 'guid-2');

      expect(state.getServiceGuid('POST:/test')).toBe('guid-2');
    });

    it('should clear all mappings', () => {
      state.addServiceMapping('POST:/a', 'guid-a');
      state.addServiceMapping('POST:/b', 'guid-b');

      state.clearServiceMappings();

      expect(state.getServiceMappings().size).toBe(0);
      expect(state.getServiceGuid('POST:/a')).toBeUndefined();
    });

    it('should return read-only mappings', () => {
      state.addServiceMapping('POST:/test', 'guid-123');

      const mappings = state.getServiceMappings();
      expect(mappings.get('POST:/test')).toBe('guid-123');
    });
  });

  describe('pending messages', () => {
    it('should track pending messages', () => {
      const messageId = 12345n;
      const sentAt = new Date();

      state.addPendingMessage(messageId, 'POST:/test', sentAt);

      const info = state.getPendingMessage(messageId);
      expect(info).toBeDefined();
      expect(info?.endpointKey).toBe('POST:/test');
      expect(info?.sentAt).toBe(sentAt);
    });

    it('should return undefined for unknown message IDs', () => {
      expect(state.getPendingMessage(99999n)).toBeUndefined();
    });

    it('should remove pending messages', () => {
      const messageId = 12345n;
      state.addPendingMessage(messageId, 'POST:/test', new Date());

      state.removePendingMessage(messageId);

      expect(state.getPendingMessage(messageId)).toBeUndefined();
    });

    it('should count pending messages correctly', () => {
      expect(state.getPendingMessageCount()).toBe(0);

      state.addPendingMessage(1n, 'POST:/a', new Date());
      expect(state.getPendingMessageCount()).toBe(1);

      state.addPendingMessage(2n, 'POST:/b', new Date());
      expect(state.getPendingMessageCount()).toBe(2);

      state.removePendingMessage(1n);
      expect(state.getPendingMessageCount()).toBe(1);
    });

    it('should handle removing non-existent messages gracefully', () => {
      // Should not throw
      state.removePendingMessage(99999n);
      expect(state.getPendingMessageCount()).toBe(0);
    });
  });
});
