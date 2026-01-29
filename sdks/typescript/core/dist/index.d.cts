/**
 * Static helper for JSON serialization/deserialization that uses Bannou's
 * standard configuration. This is the SINGLE SOURCE OF TRUTH for JSON
 * serialization settings across all Bannou TypeScript SDKs.
 *
 * USE THIS INSTEAD OF JSON.parse/stringify DIRECTLY.
 *
 * This ensures consistent behavior across the codebase:
 * - Case-insensitive property matching during deserialization (via normalization)
 * - Enums serialize as strings matching C# enum names
 * - Null/undefined values are omitted when serializing
 * - Strict number handling (no string-to-number coercion in results)
 *
 * @example
 * ```typescript
 * const model = BannouJson.deserialize<MyModel>(jsonString);
 * const json = BannouJson.serialize(model);
 * ```
 */
declare class BannouJson {
    /**
     * Deserialize JSON string to object using Bannou's standard configuration.
     * @param json - The JSON string to parse
     * @returns The deserialized object, or null if parsing fails
     */
    static deserialize<T>(json: string): T | null;
    /**
     * Deserialize JSON string to object using Bannou's standard configuration.
     * Throws if result is null or parsing fails.
     * @param json - The JSON string to parse
     * @throws Error if deserialization fails
     */
    static deserializeRequired<T>(json: string): T;
    /**
     * Serialize object to JSON string using Bannou's standard configuration.
     * Omits null and undefined values from the output.
     * @param value - The value to serialize
     * @returns JSON string
     */
    static serialize<T>(value: T): string;
    /**
     * Serialize object to UTF-8 bytes using Bannou's standard configuration.
     * @param value - The value to serialize
     * @returns UTF-8 encoded bytes
     */
    static serializeToUtf8Bytes<T>(value: T): Uint8Array;
    /**
     * Deserialize from UTF-8 bytes using Bannou's standard configuration.
     * @param utf8Json - UTF-8 encoded JSON bytes
     * @returns The deserialized object, or null if parsing fails
     */
    static deserializeFromUtf8Bytes<T>(utf8Json: Uint8Array): T | null;
}
/**
 * Extension function to deserialize a JSON string.
 * @param json - The JSON string to parse
 * @returns The deserialized object, or null if parsing fails
 */
declare function fromJson<T>(json: string): T | null;
/**
 * Extension function to serialize an object to JSON.
 * @param value - The value to serialize
 * @returns JSON string
 */
declare function toJson<T>(value: T): string;

/**
 * Represents an error response from a Bannou API call.
 * Contains all available information about the failed request.
 */
interface ErrorResponse {
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
declare function mapToHttpStatusCode(wsResponseCode: number): number;
/**
 * Maps internal WebSocket protocol response codes to error names.
 */
declare function getErrorName(wsResponseCode: number): string;
/**
 * Creates an ErrorResponse from protocol response code.
 */
declare function createErrorResponse(wsResponseCode: number, messageId: bigint, endpoint: string, message?: string | null): ErrorResponse;
/**
 * Represents the result of an API call, containing either a success response or error details.
 * @typeParam T - The expected response type on success.
 */
declare class ApiResponse<T> {
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
    private constructor();
    /**
     * Creates a successful response.
     */
    static success<T>(result: T): ApiResponse<T>;
    /**
     * Creates a successful response with no content (empty body).
     * Used for endpoints that return 200 OK without a response body.
     */
    static successEmpty<T>(): ApiResponse<T>;
    /**
     * Creates an error response.
     */
    static failure<T>(error: ErrorResponse): ApiResponse<T>;
    /**
     * Gets the result if successful, or throws an Error with error details.
     * For empty success responses (200 with no body), returns undefined.
     * Useful for test code or scenarios where exceptions are preferred over explicit error handling.
     * @throws Error when the response is an error.
     */
    getResultOrThrow(): T | undefined;
}

/**
 * Base interface for all client events pushed from server to client.
 * All event types inherit from this interface and include:
 * - eventName: The event type identifier
 * - eventId: Unique ID for this event instance
 * - timestamp: ISO 8601 timestamp when event was created
 */
interface BaseClientEvent {
    /**
     * The event type name (e.g., "connect.capability_manifest", "game_session.player_joined").
     * This is used for routing events to the appropriate handlers.
     */
    eventName: string;
    /**
     * Unique identifier for this specific event instance.
     * Can be used for deduplication or tracking.
     */
    eventId: string;
    /**
     * ISO 8601 timestamp when this event was created on the server.
     */
    timestamp: string;
}
/**
 * Type guard to check if an object is a BaseClientEvent.
 */
declare function isBaseClientEvent(obj: unknown): obj is BaseClientEvent;

export { ApiResponse, BannouJson, type BaseClientEvent, type ErrorResponse, createErrorResponse, fromJson, getErrorName, isBaseClientEvent, mapToHttpStatusCode, toJson };
