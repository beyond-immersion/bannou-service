/**
 * Base interface for all client events pushed from server to client.
 * All event types inherit from this interface and include:
 * - eventName: The event type identifier
 * - eventId: Unique ID for this event instance
 * - timestamp: ISO 8601 timestamp when event was created
 */
export interface BaseClientEvent {
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
export function isBaseClientEvent(obj: unknown): obj is BaseClientEvent {
  if (typeof obj !== 'object' || obj === null) {
    return false;
  }

  const event = obj as Record<string, unknown>;
  return (
    typeof event.eventName === 'string' &&
    typeof event.eventId === 'string' &&
    typeof event.timestamp === 'string'
  );
}
