/**
 * Manages connection state for a BannouClient session.
 * Tracks sequence numbers, pending requests, and service mappings.
 */
export class ConnectionState {
  /** Session ID assigned by the server */
  readonly sessionId: string;

  /** Per-channel sequence numbers for message ordering */
  private readonly sequenceNumbers: Map<number, number> = new Map();

  /** Service GUID mappings from capability manifest */
  private readonly serviceMappings: Map<string, string> = new Map();

  /** Pending message tracking for debugging */
  private readonly pendingMessages: Map<bigint, PendingMessageInfo> = new Map();

  constructor(sessionId: string) {
    this.sessionId = sessionId;
  }

  /**
   * Gets the next sequence number for a channel and increments it.
   */
  getNextSequenceNumber(channel: number): number {
    const current = this.sequenceNumbers.get(channel) ?? 0;
    this.sequenceNumbers.set(channel, current + 1);
    return current;
  }

  /**
   * Gets the current sequence number for a channel without incrementing.
   */
  getCurrentSequenceNumber(channel: number): number {
    return this.sequenceNumbers.get(channel) ?? 0;
  }

  /**
   * Adds a service mapping from endpoint key to GUID.
   */
  addServiceMapping(endpointKey: string, guid: string): void {
    this.serviceMappings.set(endpointKey, guid);
  }

  /**
   * Gets the service GUID for an endpoint key.
   */
  getServiceGuid(endpointKey: string): string | undefined {
    return this.serviceMappings.get(endpointKey);
  }

  /**
   * Gets all service mappings.
   */
  getServiceMappings(): ReadonlyMap<string, string> {
    return this.serviceMappings;
  }

  /**
   * Clears all service mappings (for capability manifest updates).
   */
  clearServiceMappings(): void {
    this.serviceMappings.clear();
  }

  /**
   * Tracks a pending message for debugging.
   */
  addPendingMessage(messageId: bigint, endpointKey: string, sentAt: Date): void {
    this.pendingMessages.set(messageId, {
      endpointKey,
      sentAt,
    });
  }

  /**
   * Removes a pending message after response or timeout.
   */
  removePendingMessage(messageId: bigint): void {
    this.pendingMessages.delete(messageId);
  }

  /**
   * Gets pending message info for debugging.
   */
  getPendingMessage(messageId: bigint): PendingMessageInfo | undefined {
    return this.pendingMessages.get(messageId);
  }

  /**
   * Gets count of pending messages.
   */
  getPendingMessageCount(): number {
    return this.pendingMessages.size;
  }
}

/**
 * Information about a pending message.
 */
export interface PendingMessageInfo {
  endpointKey: string;
  sentAt: Date;
}
