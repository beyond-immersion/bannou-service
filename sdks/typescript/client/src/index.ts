/**
 * @beyondimmersion/bannou-client
 *
 * TypeScript client SDK for connecting to Bannou services via WebSocket.
 *
 * @example
 * ```typescript
 * import { BannouClient } from '@beyondimmersion/bannou-client';
 *
 * const client = new BannouClient();
 *
 * // Connect with credentials
 * await client.connectAsync('http://localhost:5012', 'user@example.com', 'password');
 *
 * // Make API calls
 * const response = await client.invokeAsync('POST', '/account/get', { accountId: '...' });
 * if (response.isSuccess) {
 *   console.log(response.result);
 * }
 *
 * // Subscribe to events
 * const subscription = client.onEvent('game_session.player_joined', (event) => {
 *   console.log('Player joined:', event);
 * });
 *
 * // Cleanup
 * subscription.dispose();
 * await client.disconnectAsync();
 * ```
 */

// Re-export core types for convenience
export {
  ApiResponse,
  BannouJson,
  type BaseClientEvent,
  type ErrorResponse,
  mapToHttpStatusCode,
  getErrorName,
  createErrorResponse,
  fromJson,
  toJson,
  isBaseClientEvent,
} from '@beyondimmersion/bannou-core';

// Client exports
export { BannouClient } from './BannouClient.js';
export type {
  IBannouClient,
  DisconnectInfo,
  MetaResponse,
  EndpointInfoData,
  JsonSchemaData,
  FullSchemaData,
  ClientCapabilityEntry,
} from './IBannouClient.js';
export { DisconnectReason, MetaType } from './IBannouClient.js';
export { ConnectionState, type PendingMessageInfo } from './ConnectionState.js';
export { EventSubscription, type IEventSubscription } from './EventSubscription.js';
export { generateMessageId, generateUuid, resetMessageIdCounter } from './GuidGenerator.js';

// Protocol re-exports (for advanced use cases)
export * from './protocol/index.js';
