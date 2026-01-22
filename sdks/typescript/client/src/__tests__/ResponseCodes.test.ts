/**
 * Unit tests for ResponseCodes and HTTP status mapping.
 */

import { describe, it, expect } from 'vitest';
import {
  ResponseCodes,
  mapToHttpStatus,
  getResponseCodeName,
  isSuccess,
  isError,
} from '../protocol/ResponseCodes.js';

describe('ResponseCodes', () => {
  describe('code values', () => {
    it('should have correct values matching protocol spec', () => {
      // Success
      expect(ResponseCodes.OK).toBe(0);

      // Protocol errors (10-19)
      expect(ResponseCodes.RequestError).toBe(10);
      expect(ResponseCodes.RequestTooLarge).toBe(11);
      expect(ResponseCodes.TooManyRequests).toBe(12);
      expect(ResponseCodes.InvalidRequestChannel).toBe(13);

      // Auth errors (20-29)
      expect(ResponseCodes.Unauthorized).toBe(20);

      // Routing errors (30-39)
      expect(ResponseCodes.ServiceNotFound).toBe(30);
      expect(ResponseCodes.ClientNotFound).toBe(31);
      expect(ResponseCodes.MessageNotFound).toBe(32);

      // Permission errors (40-49)
      expect(ResponseCodes.BroadcastNotAllowed).toBe(40);

      // Service errors (50-59)
      expect(ResponseCodes.Service_BadRequest).toBe(50);
      expect(ResponseCodes.Service_NotFound).toBe(51);
      expect(ResponseCodes.Service_Unauthorized).toBe(52);
      expect(ResponseCodes.Service_Conflict).toBe(53);
      expect(ResponseCodes.Service_InternalServerError).toBe(60);

      // Shortcut errors (70+)
      expect(ResponseCodes.ShortcutExpired).toBe(70);
      expect(ResponseCodes.ShortcutTargetNotFound).toBe(71);
      expect(ResponseCodes.ShortcutRevoked).toBe(72);
    });
  });

  describe('mapToHttpStatus', () => {
    it('should map OK to 200', () => {
      expect(mapToHttpStatus(ResponseCodes.OK)).toBe(200);
    });

    it('should map service errors to correct HTTP codes', () => {
      expect(mapToHttpStatus(ResponseCodes.Service_BadRequest)).toBe(400);
      expect(mapToHttpStatus(ResponseCodes.Service_Unauthorized)).toBe(401);
      expect(mapToHttpStatus(ResponseCodes.Service_NotFound)).toBe(404);
      expect(mapToHttpStatus(ResponseCodes.Service_Conflict)).toBe(409);
      expect(mapToHttpStatus(ResponseCodes.Service_InternalServerError)).toBe(500);
    });

    it('should map auth errors to 401', () => {
      expect(mapToHttpStatus(ResponseCodes.Unauthorized)).toBe(401);
    });

    it('should map rate limit errors to 429', () => {
      expect(mapToHttpStatus(ResponseCodes.TooManyRequests)).toBe(429);
    });

    it('should map routing errors to 404', () => {
      expect(mapToHttpStatus(ResponseCodes.ServiceNotFound)).toBe(404);
      expect(mapToHttpStatus(ResponseCodes.ClientNotFound)).toBe(404);
      expect(mapToHttpStatus(ResponseCodes.MessageNotFound)).toBe(404);
      expect(mapToHttpStatus(ResponseCodes.ShortcutTargetNotFound)).toBe(404);
    });

    it('should map permission errors to 403', () => {
      expect(mapToHttpStatus(ResponseCodes.BroadcastNotAllowed)).toBe(403);
    });

    it('should map protocol errors to 400', () => {
      expect(mapToHttpStatus(ResponseCodes.RequestError)).toBe(400);
      expect(mapToHttpStatus(ResponseCodes.RequestTooLarge)).toBe(400);
      expect(mapToHttpStatus(ResponseCodes.InvalidRequestChannel)).toBe(400);
    });

    it('should map shortcut expiry errors to 410 Gone', () => {
      expect(mapToHttpStatus(ResponseCodes.ShortcutExpired)).toBe(410);
      expect(mapToHttpStatus(ResponseCodes.ShortcutRevoked)).toBe(410);
    });

    it('should default unknown codes to 500', () => {
      expect(mapToHttpStatus(255)).toBe(500);
      expect(mapToHttpStatus(100)).toBe(500);
    });
  });

  describe('getResponseCodeName', () => {
    it('should return names for known codes', () => {
      expect(getResponseCodeName(ResponseCodes.OK)).toBe('OK');
      expect(getResponseCodeName(ResponseCodes.Service_BadRequest)).toBe('Service_BadRequest');
      expect(getResponseCodeName(ResponseCodes.Service_NotFound)).toBe('Service_NotFound');
      expect(getResponseCodeName(ResponseCodes.Unauthorized)).toBe('Unauthorized');
      expect(getResponseCodeName(ResponseCodes.Service_InternalServerError)).toBe(
        'Service_InternalServerError'
      );
    });

    it('should return "UnknownError" for unknown codes', () => {
      expect(getResponseCodeName(255)).toBe('UnknownError');
      expect(getResponseCodeName(100)).toBe('UnknownError');
    });
  });

  describe('isSuccess', () => {
    it('should return true only for OK', () => {
      expect(isSuccess(ResponseCodes.OK)).toBe(true);
    });

    it('should return false for error codes', () => {
      expect(isSuccess(ResponseCodes.Service_BadRequest)).toBe(false);
      expect(isSuccess(ResponseCodes.Unauthorized)).toBe(false);
      expect(isSuccess(ResponseCodes.Service_InternalServerError)).toBe(false);
    });
  });

  describe('isError', () => {
    it('should return false for OK', () => {
      expect(isError(ResponseCodes.OK)).toBe(false);
    });

    it('should return true for error codes', () => {
      expect(isError(ResponseCodes.Service_BadRequest)).toBe(true);
      expect(isError(ResponseCodes.Unauthorized)).toBe(true);
      expect(isError(ResponseCodes.Service_InternalServerError)).toBe(true);
    });
  });
});
