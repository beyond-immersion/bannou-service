/**
 * Unit tests for EventSubscription.
 */

import { describe, it, expect, vi } from 'vitest';
import { EventSubscription } from '../EventSubscription.js';

describe('EventSubscription', () => {
  describe('constructor', () => {
    it('should store event name and subscription ID', () => {
      const callback = vi.fn();
      const subscription = new EventSubscription('test.event', 'sub-123', callback);

      expect(subscription.eventName).toBe('test.event');
      expect(subscription.subscriptionId).toBe('sub-123');
    });
  });

  describe('dispose', () => {
    it('should call the dispose callback with subscription ID', () => {
      const callback = vi.fn();
      const subscription = new EventSubscription('test.event', 'sub-123', callback);

      subscription.dispose();

      expect(callback).toHaveBeenCalledTimes(1);
      expect(callback).toHaveBeenCalledWith('sub-123');
    });

    it('should only call dispose callback once', () => {
      const callback = vi.fn();
      const subscription = new EventSubscription('test.event', 'sub-123', callback);

      subscription.dispose();
      subscription.dispose();
      subscription.dispose();

      expect(callback).toHaveBeenCalledTimes(1);
    });

    it('should be idempotent', () => {
      let disposeCount = 0;
      const callback = () => {
        disposeCount++;
      };
      const subscription = new EventSubscription('test.event', 'sub-123', callback);

      // Multiple dispose calls should be safe
      subscription.dispose();
      subscription.dispose();

      expect(disposeCount).toBe(1);
    });
  });

  describe('IEventSubscription interface', () => {
    it('should implement readonly properties', () => {
      const callback = vi.fn();
      const subscription = new EventSubscription('game.started', 'sub-456', callback);

      // These should be readonly at compile time (TypeScript check)
      // Runtime check that values exist
      expect(subscription.eventName).toBeDefined();
      expect(subscription.subscriptionId).toBeDefined();
    });

    it('should work with different event names', () => {
      const callback = vi.fn();

      const events = [
        'connect.capability_manifest',
        'game_session.player_joined',
        'account.updated',
        'some.deeply.nested.event.name',
      ];

      for (const eventName of events) {
        const subscription = new EventSubscription(eventName, `sub-${eventName}`, callback);
        expect(subscription.eventName).toBe(eventName);
      }
    });

    it('should support UUID-style subscription IDs', () => {
      const callback = vi.fn();
      const uuid = '550e8400-e29b-41d4-a716-446655440000';
      const subscription = new EventSubscription('test.event', uuid, callback);

      expect(subscription.subscriptionId).toBe(uuid);
      subscription.dispose();
      expect(callback).toHaveBeenCalledWith(uuid);
    });
  });

  describe('edge cases', () => {
    it('should handle empty event name', () => {
      const callback = vi.fn();
      const subscription = new EventSubscription('', 'sub-123', callback);

      expect(subscription.eventName).toBe('');
    });

    it('should handle special characters in event name', () => {
      const callback = vi.fn();
      const subscription = new EventSubscription('event:with/special.chars', 'sub-123', callback);

      expect(subscription.eventName).toBe('event:with/special.chars');
    });
  });
});
