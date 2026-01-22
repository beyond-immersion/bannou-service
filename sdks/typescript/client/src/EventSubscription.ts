/**
 * Represents a subscription to client events.
 * Call dispose() to unsubscribe.
 */
export interface IEventSubscription {
  /**
   * The event name this subscription is for.
   */
  readonly eventName: string;

  /**
   * Unique identifier for this subscription.
   */
  readonly subscriptionId: string;

  /**
   * Unsubscribe from the event.
   */
  dispose(): void;
}

/**
 * Implementation of IEventSubscription.
 */
export class EventSubscription implements IEventSubscription {
  readonly eventName: string;
  readonly subscriptionId: string;
  private readonly disposeCallback: (subscriptionId: string) => void;
  private disposed = false;

  constructor(
    eventName: string,
    subscriptionId: string,
    disposeCallback: (subscriptionId: string) => void
  ) {
    this.eventName = eventName;
    this.subscriptionId = subscriptionId;
    this.disposeCallback = disposeCallback;
  }

  dispose(): void {
    if (!this.disposed) {
      this.disposed = true;
      this.disposeCallback(this.subscriptionId);
    }
  }
}
