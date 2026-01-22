/**
 * TypeScript SDK Test Harness for edge-tester parity testing.
 *
 * This harness reads JSON commands from stdin, executes them using
 * the BannouClient, and writes JSON responses to stdout.
 *
 * Protocol:
 * → {"cmd": "connect", "url": "http://...", "email": "...", "password": "..."}
 * ← {"ok": true, "sessionId": "..."}
 *
 * → {"cmd": "invoke", "method": "POST", "path": "/account/get", "request": {...}}
 * ← {"ok": true, "result": {...}} or {"ok": false, "error": {...}}
 *
 * → {"cmd": "disconnect"}
 * ← {"ok": true}
 */

import * as readline from 'readline';
import { BannouClient } from '@beyondimmersion/bannou-client';

// Command types
interface ConnectCommand {
  cmd: 'connect';
  url: string;
  email: string;
  password: string;
}

interface ConnectWithTokenCommand {
  cmd: 'connectWithToken';
  url: string;
  accessToken: string;
  refreshToken?: string;
}

interface RegisterAndConnectCommand {
  cmd: 'registerAndConnect';
  url: string;
  username: string;
  email: string;
  password: string;
}

interface InvokeCommand {
  cmd: 'invoke';
  method: string;
  path: string;
  request: unknown;
  channel?: number;
  timeout?: number;
}

interface SendEventCommand {
  cmd: 'sendEvent';
  method: string;
  path: string;
  request: unknown;
  channel?: number;
}

interface DisconnectCommand {
  cmd: 'disconnect';
}

interface GetCapabilitiesCommand {
  cmd: 'getCapabilities';
}

interface GetStatusCommand {
  cmd: 'getStatus';
}

interface PingCommand {
  cmd: 'ping';
}

type Command =
  | ConnectCommand
  | ConnectWithTokenCommand
  | RegisterAndConnectCommand
  | InvokeCommand
  | SendEventCommand
  | DisconnectCommand
  | GetCapabilitiesCommand
  | GetStatusCommand
  | PingCommand;

// Response types
interface SuccessResponse<T = unknown> {
  ok: true;
  result?: T;
}

interface ErrorResponse {
  ok: false;
  error: {
    message: string;
    code?: number;
    errorName?: string;
  };
}

type Response<T = unknown> = SuccessResponse<T> | ErrorResponse;

// Client instance
let client: BannouClient | null = null;

/**
 * Write a JSON response to stdout.
 */
function respond<T>(response: Response<T>): void {
  console.log(JSON.stringify(response));
}

/**
 * Write an error response.
 */
function respondError(message: string, code?: number, errorName?: string): void {
  respond({ ok: false, error: { message, code, errorName } });
}

/**
 * Write a success response.
 */
function respondSuccess<T>(result?: T): void {
  respond({ ok: true, result });
}

/**
 * Process a command from stdin.
 */
async function processCommand(line: string): Promise<void> {
  let command: Command;

  try {
    command = JSON.parse(line) as Command;
  } catch {
    respondError('Invalid JSON input');
    return;
  }

  try {
    switch (command.cmd) {
      case 'ping':
        respondSuccess({ pong: true });
        break;

      case 'connect':
        await handleConnect(command);
        break;

      case 'connectWithToken':
        await handleConnectWithToken(command);
        break;

      case 'registerAndConnect':
        await handleRegisterAndConnect(command);
        break;

      case 'invoke':
        await handleInvoke(command);
        break;

      case 'sendEvent':
        await handleSendEvent(command);
        break;

      case 'disconnect':
        await handleDisconnect();
        break;

      case 'getCapabilities':
        handleGetCapabilities();
        break;

      case 'getStatus':
        handleGetStatus();
        break;

      default:
        respondError(`Unknown command: ${(command as { cmd: string }).cmd}`);
    }
  } catch (err) {
    const error = err instanceof Error ? err : new Error(String(err));
    respondError(error.message);
  }
}

/**
 * Handle connect command.
 */
async function handleConnect(command: ConnectCommand): Promise<void> {
  if (client && client.isConnected) {
    await client.disconnectAsync();
  }

  client = new BannouClient();

  const success = await client.connectAsync(command.url, command.email, command.password);

  if (success) {
    respondSuccess({
      sessionId: client.sessionId,
      accessToken: client.accessToken,
      refreshToken: client.refreshToken,
    });
  } else {
    respondError(client.lastError ?? 'Connection failed');
    client = null;
  }
}

/**
 * Handle connect with token command.
 */
async function handleConnectWithToken(command: ConnectWithTokenCommand): Promise<void> {
  if (client && client.isConnected) {
    await client.disconnectAsync();
  }

  client = new BannouClient();

  const success = await client.connectWithTokenAsync(
    command.url,
    command.accessToken,
    command.refreshToken
  );

  if (success) {
    respondSuccess({
      sessionId: client.sessionId,
    });
  } else {
    respondError(client.lastError ?? 'Connection failed');
    client = null;
  }
}

/**
 * Handle register and connect command.
 */
async function handleRegisterAndConnect(command: RegisterAndConnectCommand): Promise<void> {
  if (client && client.isConnected) {
    await client.disconnectAsync();
  }

  client = new BannouClient();

  const success = await client.registerAndConnectAsync(
    command.url,
    command.username,
    command.email,
    command.password
  );

  if (success) {
    respondSuccess({
      sessionId: client.sessionId,
      accessToken: client.accessToken,
      refreshToken: client.refreshToken,
    });
  } else {
    respondError(client.lastError ?? 'Registration/connection failed');
    client = null;
  }
}

/**
 * Handle invoke command.
 */
async function handleInvoke(command: InvokeCommand): Promise<void> {
  if (!client || !client.isConnected) {
    respondError('Not connected');
    return;
  }

  const response = await client.invokeAsync(
    command.method,
    command.path,
    command.request,
    command.channel,
    command.timeout
  );

  if (response.isSuccess) {
    respondSuccess(response.result);
  } else {
    respondError(
      response.error?.message ?? 'Unknown error',
      response.error?.responseCode,
      response.error?.errorName ?? undefined
    );
  }
}

/**
 * Handle send event command.
 */
async function handleSendEvent(command: SendEventCommand): Promise<void> {
  if (!client || !client.isConnected) {
    respondError('Not connected');
    return;
  }

  await client.sendEventAsync(command.method, command.path, command.request, command.channel);
  respondSuccess();
}

/**
 * Handle disconnect command.
 */
async function handleDisconnect(): Promise<void> {
  if (client) {
    await client.disconnectAsync();
    client = null;
  }
  respondSuccess();
}

/**
 * Handle get capabilities command.
 */
function handleGetCapabilities(): void {
  if (!client || !client.isConnected) {
    respondError('Not connected');
    return;
  }

  const capabilities: { method: string; path: string; guid: string }[] = [];
  for (const [key, guid] of client.availableApis) {
    const [method, path] = key.split(':');
    capabilities.push({ method, path, guid });
  }

  respondSuccess({ capabilities });
}

/**
 * Handle get status command.
 */
function handleGetStatus(): void {
  respondSuccess({
    connected: client?.isConnected ?? false,
    sessionId: client?.sessionId,
    apiCount: client?.availableApis.size ?? 0,
  });
}

/**
 * Main entry point.
 */
async function main(): Promise<void> {
  const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout,
    terminal: false,
  });

  // Signal ready
  console.log(JSON.stringify({ ready: true, version: '0.1.0' }));

  for await (const line of rl) {
    if (line.trim()) {
      await processCommand(line.trim());
    }
  }

  // Clean up on exit
  if (client) {
    await client.disconnectAsync();
  }
}

main().catch((err) => {
  console.error('Fatal error:', err);
  process.exit(1);
});
