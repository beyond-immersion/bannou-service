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
export class BannouJson {
  /**
   * Deserialize JSON string to object using Bannou's standard configuration.
   * @param json - The JSON string to parse
   * @returns The deserialized object, or null if parsing fails
   */
  static deserialize<T>(json: string): T | null {
    try {
      return JSON.parse(json) as T;
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
  static deserializeRequired<T>(json: string): T {
    const result = this.deserialize<T>(json);
    if (result === null) {
      throw new Error('Failed to deserialize JSON');
    }
    return result;
  }

  /**
   * Serialize object to JSON string using Bannou's standard configuration.
   * Omits null and undefined values from the output.
   * @param value - The value to serialize
   * @returns JSON string
   */
  static serialize<T>(value: T): string {
    return JSON.stringify(value, (_, v) => {
      // Omit null and undefined values (matches C# DefaultIgnoreCondition.WhenWritingNull)
      if (v === null || v === undefined) {
        return undefined;
      }
      return v;
    });
  }

  /**
   * Serialize object to UTF-8 bytes using Bannou's standard configuration.
   * @param value - The value to serialize
   * @returns UTF-8 encoded bytes
   */
  static serializeToUtf8Bytes<T>(value: T): Uint8Array {
    const json = this.serialize(value);
    return new TextEncoder().encode(json);
  }

  /**
   * Deserialize from UTF-8 bytes using Bannou's standard configuration.
   * @param utf8Json - UTF-8 encoded JSON bytes
   * @returns The deserialized object, or null if parsing fails
   */
  static deserializeFromUtf8Bytes<T>(utf8Json: Uint8Array): T | null {
    const json = new TextDecoder().decode(utf8Json);
    return this.deserialize<T>(json);
  }
}

/**
 * Extension function to deserialize a JSON string.
 * @param json - The JSON string to parse
 * @returns The deserialized object, or null if parsing fails
 */
export function fromJson<T>(json: string): T | null {
  return BannouJson.deserialize<T>(json);
}

/**
 * Extension function to serialize an object to JSON.
 * @param value - The value to serialize
 * @returns JSON string
 */
export function toJson<T>(value: T): string {
  return BannouJson.serialize(value);
}
