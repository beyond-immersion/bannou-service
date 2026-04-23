/**
 * Dashboard event emitter — WebSocket + HTTP ingest + JSONL persistent log.
 *
 * Broadcasts structured JSON events for every tool call, sentinel injection,
 * user prompt, and assistant response. Purely observational — never affects
 * tool execution. Gracefully degrades if `ws` package is not installed.
 *
 * Architecture:
 *   emitEvent(event) ← single core function
 *     ├─ append to JSONL file (always, even if no WS clients)
 *     └─ broadcast to WebSocket clients (if connected)
 *
 *   Entry points:
 *     1. emitToolEvent(tool, input, result)  — internal, from server.mjs registerTool wrapper
 *     2. emitSentinelEvent(sentinel)         — internal, from sentinel.mjs
 *     3. HTTP POST /event                    — external, from Claude Code hooks (curl)
 *
 *   Servers:
 *     WebSocket: port 9500 + BUCKET (live stream)
 *     HTTP:      port 9600 + BUCKET (ingest + history)
 *
 * SAFETY: Every public function is wrapped in try/catch. A WebSocket, HTTP,
 * or file write failure MUST NEVER break the MCP server.
 */

import { createServer } from "node:http";
import { appendFileSync, writeFileSync, readFileSync, existsSync } from "node:fs";

import { MCP_BUCKET } from "../state.mjs";
import { processSentinel } from "./sentinel.mjs";

const WS_PORT = 9500 + MCP_BUCKET;
const HTTP_PORT = 9600 + MCP_BUCKET;
const LOG_PATH = `/tmp/bannou-dashboard-bucket-${MCP_BUCKET}.jsonl`;
const TRUNCATE_LIMIT = 5000;

let wss = null;
let httpServer = null;
let initialized = false;

// ─── Initialization ──────────────────────────────────────────────────────

/**
 * Start the WebSocket server, HTTP ingest server, and truncate the JSONL log.
 * Idempotent — safe to call multiple times.
 * Uses dynamic import for `ws` so the server starts normally if not installed.
 */
export async function initDashboardEmitter() {
  if (initialized) return;
  initialized = true;

  // Truncate log file (new session = fresh log)
  try {
    writeFileSync(LOG_PATH, "", "utf-8");
  } catch (err) {
    console.error(`[dashboard] Failed to initialize log file: ${err.message}`);
  }

  // ── WebSocket server ──
  try {
    const { WebSocketServer } = await import("ws");
    wss = new WebSocketServer({ host: "0.0.0.0", port: WS_PORT });

    wss.on("error", (err) => {
      console.error(`[dashboard] WebSocket server error: ${err.message}`);
    });
  } catch (err) {
    console.error(`[dashboard] WebSocket disabled: ${err.message}`);
    wss = null;
  }

  // ── HTTP ingest server ──
  try {
    httpServer = createServer((req, res) => {
      try {
        handleHttpRequest(req, res);
      } catch (err) {
        console.error(`[dashboard] HTTP handler error: ${err.message}`);
        res.writeHead(500).end("Internal Server Error");
      }
    });

    httpServer.on("error", (err) => {
      console.error(`[dashboard] HTTP server error: ${err.message}`);
    });

    httpServer.listen(HTTP_PORT, "0.0.0.0");
  } catch (err) {
    console.error(`[dashboard] HTTP server disabled: ${err.message}`);
    httpServer = null;
  }

  // ── Startup log ──
  const wsPart = wss ? `WS ws://0.0.0.0:${WS_PORT}` : "WS disabled";
  const httpPart = httpServer ? `HTTP http://0.0.0.0:${HTTP_PORT}` : "HTTP disabled";
  console.error(`[dashboard] Emitter listening — ${wsPart}, ${httpPart}`);
}

// ─── HTTP Request Handling ───────────────────────────────────────────────

async function handleHttpRequest(req, res) {
  // CORS headers for dashboard app
  res.setHeader("Access-Control-Allow-Origin", "*");
  res.setHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
  res.setHeader("Access-Control-Allow-Headers", "Content-Type");

  if (req.method === "OPTIONS") {
    res.writeHead(204).end();
    return;
  }

  // GET /history — return JSONL as JSON array for dashboard catch-up
  if (req.method === "GET" && req.url === "/history") {
    try {
      if (!existsSync(LOG_PATH)) {
        res.writeHead(200, { "Content-Type": "application/json" }).end("[]");
        return;
      }

      const raw = readFileSync(LOG_PATH, "utf-8").trim();
      if (!raw) {
        res.writeHead(200, { "Content-Type": "application/json" }).end("[]");
        return;
      }

      const events = raw.split("\n").map((line) => {
        try { return JSON.parse(line); } catch { return null; }
      }).filter(Boolean);

      res.writeHead(200, { "Content-Type": "application/json" }).end(JSON.stringify(events));
    } catch (err) {
      console.error(`[dashboard] History read error: ${err.message}`);
      res.writeHead(500).end("Error reading history");
    }
    return;
  }

  // POST /event — ingest external event (from hooks)
  if (req.method === "POST" && req.url === "/event") {
    let body = "";
    req.on("data", (chunk) => { body += chunk; });
    req.on("end", () => {
      try {
        const parsed = JSON.parse(body);
        // Server always sets bucket and timestamp
        parsed.bucket = MCP_BUCKET;
        if (!parsed.timestamp) parsed.timestamp = new Date().toISOString();
        emitEvent(parsed);
        res.writeHead(200).end("OK");
      } catch {
        res.writeHead(400).end("Bad Request: invalid JSON");
      }
    });
    return;
  }

  // POST /poke — trigger immediate sentinel processing
  if (req.method === "POST" && req.url === "/poke") {
    try {
      const message = await processSentinel();
      const result = { processed: true, message: message ?? null };
      res.writeHead(200, { "Content-Type": "application/json" }).end(JSON.stringify(result));
    } catch (err) {
      console.error(`[dashboard] Poke error: ${err.message}`);
      res.writeHead(500).end(JSON.stringify({ processed: false, error: err.message }));
    }
    return;
  }

  res.writeHead(404).end("Not Found");
}

// ─── Core Event Pipeline ─────────────────────────────────────────────────

/**
 * Core event function — all events flow through here.
 * 1. Append to JSONL file (persistent log)
 * 2. Broadcast to WebSocket clients (live stream)
 * Both steps are independent — one failing doesn't prevent the other.
 */
function emitEvent(event) {
  // Step 1: Persistent log
  try {
    appendFileSync(LOG_PATH, JSON.stringify(event) + "\n", "utf-8");
  } catch {
    // File write failure must not prevent WS broadcast
  }

  // Step 2: WebSocket broadcast
  try {
    broadcast(event);
  } catch {
    // WS failure must not prevent anything
  }
}

// ─── WebSocket Broadcasting ──────────────────────────────────────────────

/**
 * Broadcast a JSON message to all connected WebSocket clients.
 * Fire-and-forget — silently drops if no clients connected.
 */
function broadcast(data) {
  if (!wss || wss.clients.size === 0) return;

  const json = JSON.stringify(data);
  for (const client of wss.clients) {
    // readyState 1 = OPEN
    if (client.readyState === 1) {
      try {
        client.send(json);
      } catch {
        // Individual client send failure — ignore
      }
    }
  }
}

// ─── Sanitization ────────────────────────────────────────────────────────

function truncate(value, limit = TRUNCATE_LIMIT) {
  if (typeof value !== "string") return value;
  if (value.length <= limit) return value;
  return value.slice(0, limit) + `... [truncated, ${value.length} total chars]`;
}

/**
 * Sanitize tool input for dashboard display.
 * Truncates large content fields to prevent overwhelming the log and WebSocket.
 */
function sanitizeInput(tool, input) {
  if (!input || typeof input !== "object") return input;

  const clean = { ...input };

  // write_file: truncate content
  if (tool === "write_file" && clean.content) {
    clean.content = truncate(clean.content);
  }

  // edit_file: truncate old_string and new_string
  if (tool === "edit_file") {
    if (clean.old_string) clean.old_string = truncate(clean.old_string);
    if (clean.new_string) clean.new_string = truncate(clean.new_string);
  }

  return clean;
}

/**
 * Extract a clean result summary from the MCP result format.
 * MCP results are { content: [{ type: "text", text: "..." }], isError?: boolean }
 */
function sanitizeResult(result) {
  if (!result) return { success: true, message: null, content: null };

  const success = result.isError !== true;
  let content = null;

  if (result.content && Array.isArray(result.content)) {
    const texts = result.content
      .filter((c) => c && c.type === "text" && typeof c.text === "string")
      .map((c) => c.text);
    content = truncate(texts.join("\n"));
  }

  return { success, message: null, content };
}

// ─── Public Emitters (Internal MCP Call Sites) ───────────────────────────

/**
 * Emit a tool call event. Called from the registerTool wrapper in server.mjs.
 * Fire-and-forget — never throws, never awaited.
 */
export function emitToolEvent(tool, input, result) {
  try {
    emitEvent({
      bucket: MCP_BUCKET,
      tool,
      input: sanitizeInput(tool, input),
      result: sanitizeResult(result),
      timestamp: new Date().toISOString(),
    });
  } catch {
    // Never break the MCP server
  }
}

/**
 * Emit a sentinel injection event. Called from sentinel.mjs.
 * Fire-and-forget — never throws, never awaited.
 */
export function emitSentinelEvent(sentinel) {
  try {
    emitEvent({
      bucket: MCP_BUCKET,
      type: "sentinel",
      sentinel,
      timestamp: new Date().toISOString(),
    });
  } catch {
    // Never break the MCP server
  }
}
