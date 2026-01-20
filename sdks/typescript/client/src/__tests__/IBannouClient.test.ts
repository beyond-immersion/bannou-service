/**
 * Unit tests for IBannouClient types and enums.
 */

import { describe, it, expect } from 'vitest';
import { DisconnectReason, MetaType } from '../IBannouClient.js';

describe('IBannouClient Types', () => {
  describe('DisconnectReason', () => {
    it('should have all expected reason values', () => {
      expect(DisconnectReason.ClientDisconnect).toBe('client_disconnect');
      expect(DisconnectReason.ServerClose).toBe('server_close');
      expect(DisconnectReason.NetworkError).toBe('network_error');
      expect(DisconnectReason.SessionExpired).toBe('session_expired');
      expect(DisconnectReason.ServerShutdown).toBe('server_shutdown');
      expect(DisconnectReason.Kicked).toBe('kicked');
    });

    it('should have unique values', () => {
      const values = [
        DisconnectReason.ClientDisconnect,
        DisconnectReason.ServerClose,
        DisconnectReason.NetworkError,
        DisconnectReason.SessionExpired,
        DisconnectReason.ServerShutdown,
        DisconnectReason.Kicked,
      ];

      const unique = new Set(values);
      expect(unique.size).toBe(values.length);
    });
  });

  describe('MetaType', () => {
    it('should have correct numeric values matching protocol spec', () => {
      // These values are encoded in the Channel field of the binary message
      expect(MetaType.EndpointInfo).toBe(0);
      expect(MetaType.RequestSchema).toBe(1);
      expect(MetaType.ResponseSchema).toBe(2);
      expect(MetaType.FullSchema).toBe(3);
    });

    it('should be usable as numeric channel values', () => {
      // MetaType values are used directly as channel numbers
      const channel: number = MetaType.FullSchema;
      expect(channel).toBe(3);
    });
  });
});
