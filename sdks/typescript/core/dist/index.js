// src/BannouJson.ts
var BannouJson = class {
  /**
   * Deserialize JSON string to object using Bannou's standard configuration.
   * @param json - The JSON string to parse
   * @returns The deserialized object, or null if parsing fails
   */
  static deserialize(json) {
    try {
      return JSON.parse(json);
    } catch {
      return null;
    }
  }
  /**
   * Deserialize JSON string to object using Bannou's standard configuration.
   * Throws if result is null or parsing fails.
   * @param json - The JSON string to parse
   * @throws Error if deserialization fails
   */
  static deserializeRequired(json) {
    const result = this.deserialize(json);
    if (result === null) {
      throw new Error("Failed to deserialize JSON");
    }
    return result;
  }
  /**
   * Serialize object to JSON string using Bannou's standard configuration.
   * Omits null and undefined values from the output.
   * @param value - The value to serialize
   * @returns JSON string
   */
  static serialize(value) {
    return JSON.stringify(value, (_, v) => {
      if (v === null || v === void 0) {
        return void 0;
      }
      return v;
    });
  }
  /**
   * Serialize object to UTF-8 bytes using Bannou's standard configuration.
   * @param value - The value to serialize
   * @returns UTF-8 encoded bytes
   */
  static serializeToUtf8Bytes(value) {
    const json = this.serialize(value);
    return new TextEncoder().encode(json);
  }
  /**
   * Deserialize from UTF-8 bytes using Bannou's standard configuration.
   * @param utf8Json - UTF-8 encoded JSON bytes
   * @returns The deserialized object, or null if parsing fails
   */
  static deserializeFromUtf8Bytes(utf8Json) {
    const json = new TextDecoder().decode(utf8Json);
    return this.deserialize(json);
  }
};
function fromJson(json) {
  return BannouJson.deserialize(json);
}
function toJson(value) {
  return BannouJson.serialize(value);
}

// src/ApiResponse.ts
function mapToHttpStatusCode(wsResponseCode) {
  switch (wsResponseCode) {
    case 0:
      return 200;
    // OK
    case 50:
      return 400;
    // Service_BadRequest
    case 51:
      return 404;
    // Service_NotFound
    case 52:
      return 401;
    // Service_Unauthorized (covers both 401/403)
    case 53:
      return 409;
    // Service_Conflict
    case 60:
      return 500;
    // Service_InternalServerError
    default:
      return 500;
  }
}
function getErrorName(wsResponseCode) {
  switch (wsResponseCode) {
    case 0:
      return "OK";
    case 50:
      return "BadRequest";
    case 51:
      return "NotFound";
    case 52:
      return "Unauthorized";
    case 53:
      return "Conflict";
    case 60:
      return "InternalServerError";
    default:
      return "UnknownError";
  }
}
function createErrorResponse(wsResponseCode, messageId, method, path, message = null) {
  return {
    responseCode: mapToHttpStatusCode(wsResponseCode),
    errorName: getErrorName(wsResponseCode),
    message,
    messageId,
    method,
    path
  };
}
var ApiResponse = class _ApiResponse {
  /**
   * Whether the API call was successful.
   */
  isSuccess;
  /**
   * The successful response data. Only valid when isSuccess is true.
   */
  result;
  /**
   * Error details when the request failed. Only valid when isSuccess is false.
   */
  error;
  constructor(isSuccess, result, error) {
    this.isSuccess = isSuccess;
    this.result = result;
    this.error = error;
  }
  /**
   * Creates a successful response.
   */
  static success(result) {
    return new _ApiResponse(true, result, void 0);
  }
  /**
   * Creates a successful response with no content (empty body).
   * Used for endpoints that return 200 OK without a response body.
   */
  static successEmpty() {
    return new _ApiResponse(true, void 0, void 0);
  }
  /**
   * Creates an error response.
   */
  static failure(error) {
    return new _ApiResponse(false, void 0, error);
  }
  /**
   * Gets the result if successful, or throws an Error with error details.
   * For empty success responses (200 with no body), returns undefined.
   * Useful for test code or scenarios where exceptions are preferred over explicit error handling.
   * @throws Error when the response is an error.
   */
  getResultOrThrow() {
    if (this.isSuccess) {
      return this.result;
    }
    const error = this.error;
    throw new Error(
      `${error?.errorName ?? "Error"}: ${error?.message ?? "Request failed"} (code: ${error?.responseCode ?? -1})`
    );
  }
};

// src/BaseClientEvent.ts
function isBaseClientEvent(obj) {
  if (typeof obj !== "object" || obj === null) {
    return false;
  }
  const event = obj;
  return typeof event.eventName === "string" && typeof event.eventId === "string" && typeof event.timestamp === "string";
}

export { ApiResponse, BannouJson, createErrorResponse, fromJson, getErrorName, isBaseClientEvent, mapToHttpStatusCode, toJson };
//# sourceMappingURL=index.js.map
//# sourceMappingURL=index.js.map
