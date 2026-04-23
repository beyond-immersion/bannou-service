/**
 * Worker agent spawning logic for dispatch_worker MCP tool.
 *
 * Spawns a constrained sub-agent via `claude -p` with:
 *   - Separate MCP bucket (BANNOU_MCP_BUCKET=2–5, rotary) to avoid scope conflicts
 *   - Full prompt containing worker rules + task
 *   - Timeout enforcement
 *   - Output capture
 *
 * The worker runs as an independent Claude Code session with its own MCP server
 * instance. It has access to the same tools (read_file, edit_file, start_scope, etc.)
 * but operates on a separate state bucket so scope tracking doesn't conflict with
 * the parent session.
 */

import { spawn } from "node:child_process";
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

/** Maximum scopes a worker can use per task. */
export const MAX_WORKER_SCOPES = 3;

/** Default timeout for worker execution (10 minutes). */
const DEFAULT_TIMEOUT_MS = 600_000;

/** Maximum output buffer (10 MB). */
const MAX_BUFFER = 10 * 1024 * 1024;

/** Worker buckets rotate 2→3→4→5→2… to avoid scope/state collisions between concurrent workers. */
const WORKER_BUCKET_MIN = 2;
const WORKER_BUCKET_MAX = 5;
let _nextWorkerBucket = WORKER_BUCKET_MIN;

/**
 * Returns the next MCP bucket ID (2–5) in a rotary cycle.
 * Each call advances the counter so consecutive workers get different buckets.
 */
export function getNextBucket() {
  const bucket = _nextWorkerBucket;
  _nextWorkerBucket = _nextWorkerBucket >= WORKER_BUCKET_MAX
    ? WORKER_BUCKET_MIN
    : _nextWorkerBucket + 1;
  return String(bucket);
}

/**
 * Constructs the full worker prompt from the task description and scope list.
 */
export function buildWorkerPrompt(task, scopes) {
  const scopeList = scopes.map((s, i) => `  ${i + 1}. ${s}`).join("\n");

  return [
    "# Worker Task Assignment",
    "",
    "You are a bannou-worker executing a focused implementation task.",
    "Read and follow the bannou-worker rules loaded via your agent definition.",
    "",
    "## Your Declared Scopes (max 3)",
    "",
    scopeList,
    "",
    "You are authorized to start_scope for ONLY the scopes listed above.",
    "Manage them yourself — start when you need to write, stop when done.",
    "",
    "## Task",
    "",
    task,
    "",
    "## Reminders",
    "",
    "- Call `prepare_context(profile: \"dev\")` FIRST and read all composites",
    "- For plugin work, also call `prepare_context(profile: \"plugin\", service: \"{name}\")`",
    "- Your FINAL action MUST be `stop_scope` — include its full output in your response",
    "- If you encounter a problem requiring user judgment, STOP and describe it",
    "- Do NOT attempt to modify frozen files (scripts/, docs/reference/, structural-tests/, etc.)",
  ].join("\n");
}

/**
 * Spawns a worker agent via `claude -p` and captures output.
 *
 * @param {string} prompt - Full worker prompt
 * @param {object} options - Spawn options
 * @param {string} options.cwd - Working directory (project root)
 * @param {number} [options.timeoutMs] - Timeout in milliseconds
 * @param {number} [options.maxTurns] - Maximum conversation turns
 * @returns {Promise<{stdout: string, stderr: string, exitCode: number|null, bucket: string}>}
 */
export function spawnWorker(prompt, options = {}) {
  const timeoutMs = options.timeoutMs || DEFAULT_TIMEOUT_MS;
  const maxTurns = options.maxTurns || 100;
  const bucket = getNextBucket();

  return new Promise((resolve, reject) => {
    const args = [
      "-p",                          // Non-interactive print mode
      "--max-turns", String(maxTurns),
    ];

    const child = spawn("claude", args, {
      cwd: options.cwd,
      env: {
        ...process.env,
        BANNOU_MCP_BUCKET: bucket,   // Rotary bucket (2–5) — avoids collisions between concurrent workers
      },
      stdio: ["pipe", "pipe", "pipe"],
    });

    let stdout = "";
    let stderr = "";
    let killed = false;

    // Enforce timeout
    const timer = setTimeout(() => {
      killed = true;
      child.kill("SIGTERM");
    }, timeoutMs);

    child.stdout.on("data", (chunk) => {
      stdout += chunk.toString();
      if (stdout.length > MAX_BUFFER) {
        killed = true;
        child.kill("SIGTERM");
      }
    });

    child.stderr.on("data", (chunk) => {
      stderr += chunk.toString();
    });

    child.on("close", (code) => {
      clearTimeout(timer);
      if (killed && stdout.length > MAX_BUFFER) {
        resolve({
          stdout: stdout.slice(0, MAX_BUFFER),
          stderr: "Output exceeded buffer limit and was truncated.",
          exitCode: code,
          bucket,
        });
      } else if (killed) {
        resolve({
          stdout: stdout.trim(),
          stderr: `Worker timed out after ${timeoutMs / 1000}s. Partial output returned.`,
          exitCode: code,
          bucket,
        });
      } else {
        resolve({
          stdout: stdout.trim(),
          stderr: stderr.trim(),
          exitCode: code,
          bucket,
        });
      }
    });

    child.on("error", (err) => {
      clearTimeout(timer);
      reject(err);
    });

    // Send prompt via stdin
    child.stdin.write(prompt);
    child.stdin.end();
  });
}

/**
 * Saves worker output to a temp file for later review.
 */
export async function saveWorkerLog(taskId, stdout, stderr) {
  const timestamp = new Date().toISOString().replace(/[:.]/g, "-");
  const logPath = `/tmp/bannou-worker-${taskId}-${timestamp}.txt`;
  const content = [
    `=== Worker Output (${timestamp}) ===`,
    "",
    stdout,
    "",
    stderr ? `=== Stderr ===\n${stderr}` : "",
  ].join("\n");

  await writeFile(logPath, content, "utf-8");
  return logPath;
}
