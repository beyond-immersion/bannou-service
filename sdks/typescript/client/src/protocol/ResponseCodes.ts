/**
 * Response codes used in the binary WebSocket protocol for success/error indication.
 * These codes are returned in the response header's ResponseCode field.
 *
 * Code ranges:
 * - 0-49: Protocol-level errors (Connect service)
 * - 50-69: Service-level errors (downstream service responses)
 * - 70+: Shortcut-specific errors
 */
export const ResponseCodes = {
  /** Request completed successfully. */
  OK: 0,

  /** Generic request error (malformed message, invalid format). */
  RequestError: 10,

  /** Request payload exceeds maximum allowed size. */
  RequestTooLarge: 11,

  /** Rate limit exceeded for this client/session. */
  TooManyRequests: 12,

  /** Invalid channel number in request header. */
  InvalidRequestChannel: 13,

  /** Authentication required but not provided or invalid. */
  Unauthorized: 20,

  /** Target service GUID not found in capability manifest. */
  ServiceNotFound: 30,

  /** Target client GUID not found (for client-to-client messages). */
  ClientNotFound: 31,

  /** Referenced message ID not found. */
  MessageNotFound: 32,

  /** Broadcast not allowed in this connection mode (External mode blocks broadcast). */
  BroadcastNotAllowed: 40,

  /** Service returned 400 Bad Request. */
  Service_BadRequest: 50,

  /** Service returned 404 Not Found. */
  Service_NotFound: 51,

  /** Service returned 401/403 Unauthorized/Forbidden. */
  Service_Unauthorized: 52,

  /** Service returned 409 Conflict. */
  Service_Conflict: 53,

  /** Service returned 500 Internal Server Error. */
  Service_InternalServerError: 60,

  /** Shortcut has expired and is no longer valid. */
  ShortcutExpired: 70,

  /** Shortcut target endpoint no longer exists. */
  ShortcutTargetNotFound: 71,

  /** Shortcut was explicitly revoked. */
  ShortcutRevoked: 72,
} as const;

export type ResponseCodesType = (typeof ResponseCodes)[keyof typeof ResponseCodes];

/**
 * Maps protocol response codes to HTTP status codes.
 * This hides the binary protocol implementation details from client code.
 */
export function mapToHttpStatus(code: number): number {
  switch (code) {
    case ResponseCodes.OK:
      return 200;
    case ResponseCodes.RequestError:
    case ResponseCodes.RequestTooLarge:
    case ResponseCodes.InvalidRequestChannel:
    case ResponseCodes.Service_BadRequest:
      return 400;
    case ResponseCodes.Unauthorized:
    case ResponseCodes.Service_Unauthorized:
      return 401;
    case ResponseCodes.TooManyRequests:
      return 429;
    case ResponseCodes.ServiceNotFound:
    case ResponseCodes.ClientNotFound:
    case ResponseCodes.MessageNotFound:
    case ResponseCodes.Service_NotFound:
    case ResponseCodes.ShortcutTargetNotFound:
      return 404;
    case ResponseCodes.Service_Conflict:
      return 409;
    case ResponseCodes.BroadcastNotAllowed:
      return 403;
    case ResponseCodes.ShortcutExpired:
    case ResponseCodes.ShortcutRevoked:
      return 410; // Gone
    case ResponseCodes.Service_InternalServerError:
    default:
      return 500;
  }
}

/**
 * Gets a human-readable error name for a response code.
 */
export function getResponseCodeName(code: number): string {
  const entries = Object.entries(ResponseCodes);
  for (const [name, value] of entries) {
    if (value === code) {
      return name;
    }
  }
  return 'UnknownError';
}

/**
 * Check if a response code indicates success.
 */
export function isSuccess(code: number): boolean {
  return code === ResponseCodes.OK;
}

/**
 * Check if a response code indicates an error.
 */
export function isError(code: number): boolean {
  return code !== ResponseCodes.OK;
}
