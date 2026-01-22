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
// Client instance
let client = null;
/**
 * Write a JSON response to stdout.
 */
function respond(response) {
    console.log(JSON.stringify(response));
}
/**
 * Write an error response.
 */
function respondError(message, code, errorName) {
    respond({ ok: false, error: { message, code, errorName } });
}
/**
 * Write a success response.
 */
function respondSuccess(result) {
    respond({ ok: true, result });
}
/**
 * Process a command from stdin.
 */
async function processCommand(line) {
    let command;
    try {
        command = JSON.parse(line);
    }
    catch {
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
                respondError(`Unknown command: ${command.cmd}`);
        }
    }
    catch (err) {
        const error = err instanceof Error ? err : new Error(String(err));
        respondError(error.message);
    }
}
/**
 * Handle connect command.
 */
async function handleConnect(command) {
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
    }
    else {
        respondError(client.lastError ?? 'Connection failed');
        client = null;
    }
}
/**
 * Handle connect with token command.
 */
async function handleConnectWithToken(command) {
    if (client && client.isConnected) {
        await client.disconnectAsync();
    }
    client = new BannouClient();
    const success = await client.connectWithTokenAsync(
        command.url, command.accessToken, command.refreshToken);
    if (success) {
        respondSuccess({
            sessionId: client.sessionId,
        });
    }
    else {
        respondError(client.lastError ?? 'Connection failed');
        client = null;
    }
}
/**
 * Handle register and connect command.
 */
async function handleRegisterAndConnect(command) {
    if (client && client.isConnected) {
        await client.disconnectAsync();
    }
    client = new BannouClient();
    const success = await client.registerAndConnectAsync(
        command.url, command.username, command.email, command.password);
    if (success) {
        respondSuccess({
            sessionId: client.sessionId,
            accessToken: client.accessToken,
            refreshToken: client.refreshToken,
        });
    }
    else {
        respondError(client.lastError ?? 'Registration/connection failed');
        client = null;
    }
}
/**
 * Handle invoke command.
 */
async function handleInvoke(command) {
    if (!client || !client.isConnected) {
        respondError('Not connected');
        return;
    }
    const response = await client.invokeAsync(
        command.method, command.path, command.request, command.channel, command.timeout);
    if (response.isSuccess) {
        respondSuccess(response.result);
    }
    else {
        const err = response.error;
        respondError(err?.message ?? 'Unknown error', err?.responseCode, err?.errorName);
    }
}
/**
 * Handle send event command.
 */
async function handleSendEvent(command) {
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
async function handleDisconnect() {
    if (client) {
        await client.disconnectAsync();
        client = null;
    }
    respondSuccess();
}
/**
 * Handle get capabilities command.
 */
function handleGetCapabilities() {
    if (!client || !client.isConnected) {
        respondError('Not connected');
        return;
    }
    const capabilities = [];
    for (const [key, guid] of client.availableApis) {
        const [method, path] = key.split(':');
        capabilities.push({ method, path, guid });
    }
    respondSuccess({ capabilities });
}
/**
 * Handle get status command.
 */
function handleGetStatus() {
    respondSuccess({
        connected: client?.isConnected ?? false,
        sessionId: client?.sessionId,
        apiCount: client?.availableApis.size ?? 0,
    });
}
/**
 * Main entry point.
 */
async function main() {
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
