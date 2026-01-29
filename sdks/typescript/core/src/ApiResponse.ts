/**
 * Represents an error response from a Bannou API call.
 * Contains all available information about the failed request.
 */
export interface ErrorResponse {
  /**
   * The HTTP status code equivalent for this error.
   * Standard HTTP codes: 400 = Bad Request, 401 = Unauthorized, 404 = Not Found,
   * 409 = Conflict, 500 = Internal Server Error.
   */
  responseCode: number;

  /**
   * Human-readable error name (e.g., "InternalServerError", "Unauthorized").
   */
  errorName: string | null;

  /**
   * Detailed error message from the server, if provided.
   */
  message: string | null;

  /**
   * The unique message ID for request/response correlation.
   */
  messageId: bigint;

  /**
   * The endpoint path of the original request (e.g., "/subscription/create").
   */
  endpoint: string;
}

/**
 * Maps internal WebSocket protocol response codes to standard HTTP status codes.
 * This hides the binary protocol implementation details from client code.
 */
export function mapToHttpStatusCode(wsResponseCode: number): number {
  switch (wsResponseCode) {
    case 0:
      return 200; // OK
    case 50:
      return 400; // Service_BadRequest
    case 51:
      return 404; // Service_NotFound
    case 52:
      return 401; // Service_Unauthorized (covers both 401/403)
    case 53:
      return 409; // Service_Conflict
    case 60:
      return 500; // Service_InternalServerError
    default:
      return 500; // Pass through other codes as 500 for safety
  }
}

/**
 * Maps internal WebSocket protocol response codes to error names.
 */
export function getErrorName(wsResponseCode: number): string {
  switch (wsResponseCode) {
    case 0:
      return 'OK';
    case 50:
      return 'BadRequest';
    case 51:
      return 'NotFound';
    case 52:
      return 'Unauthorized';
    case 53:
      return 'Conflict';
    case 60:
      return 'InternalServerError';
    default:
      return 'UnknownError';
  }
}

/**
 * Creates an ErrorResponse from protocol response code.
 */
export function createErrorResponse(
  wsResponseCode: number,
  messageId: bigint,
  endpoint: string,
  message: string | null = null
): ErrorResponse {
  return {
    responseCode: mapToHttpStatusCode(wsResponseCode),
    errorName: getErrorName(wsResponseCode),
    message,
    messageId,
    endpoint,
  };
}

/**
 * Represents the result of an API call, containing either a success response or error details.
 * @typeParam T - The expected response type on success.
 */
export class ApiResponse<T> {
  /**
   * Whether the API call was successful.
   */
  readonly isSuccess: boolean;

  /**
   * The successful response data. Only valid when isSuccess is true.
   */
  readonly result?: T;

  /**
   * Error details when the request failed. Only valid when isSuccess is false.
   */
  readonly error?: ErrorResponse;

  private constructor(isSuccess: boolean, result?: T, error?: ErrorResponse) {
    this.isSuccess = isSuccess;
    this.result = result;
    this.error = error;
  }

  /**
   * Creates a successful response.
   */
  static success<T>(result: T): ApiResponse<T> {
    return new ApiResponse<T>(true, result, undefined);
  }

  /**
   * Creates a successful response with no content (empty body).
   * Used for endpoints that return 200 OK without a response body.
   */
  static successEmpty<T>(): ApiResponse<T> {
    return new ApiResponse<T>(true, undefined, undefined);
  }

  /**
   * Creates an error response.
   */
  static failure<T>(error: ErrorResponse): ApiResponse<T> {
    return new ApiResponse<T>(false, undefined, error);
  }

  /**
   * Gets the result if successful, or throws an Error with error details.
   * For empty success responses (200 with no body), returns undefined.
   * Useful for test code or scenarios where exceptions are preferred over explicit error handling.
   * @throws Error when the response is an error.
   */
  getResultOrThrow(): T | undefined {
    if (this.isSuccess) {
      return this.result;
    }

    const error = this.error;
    throw new Error(
      `${error?.errorName ?? 'Error'}: ${error?.message ?? 'Request failed'} ` +
        `(code: ${error?.responseCode ?? -1})`
    );
  }
}
