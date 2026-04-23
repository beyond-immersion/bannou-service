/**
 * Command execution helpers — whitelist, blocked patterns, execution.
 *
 * Defines the allowed command prefixes and blocked patterns for run_command.
 * The actual tool registration is in server.mjs; this file provides the
 * validation and execution logic.
 */

import { exec } from "node:child_process";
import { promisify } from "node:util";
import { createHash } from "node:crypto";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { writeFile } from "node:fs/promises";

import { pendingContinuations, CHUNK_BYTE_LIMIT, RESPONSE_SERIALIZED_LIMIT } from "../state.mjs";

export const execAsync = promisify(exec);

/**
 * Whitelist of allowed command prefixes. Each entry defines what commands
 * agents can execute. Everything else is rejected.
 *
 * This replaces direct Bash access — agents get controlled execution of
 * specific tools without the ability to improvise shell scripts, cat files
 * (bypassing read_file), sed/awk edit files (bypassing edit_file), or run
 * arbitrary code.
 */
export const ALLOWED_COMMANDS = [
  // GitHub CLI — issue management, PR operations, API queries
  { prefix: "gh ", description: "GitHub CLI" },

  // .NET build and test
  { prefix: "dotnet build", description: ".NET build" },
  { prefix: "dotnet test", description: ".NET test" },

  // Makefile targets (print-models, build, test-structural, etc.)
  { prefix: "make ", description: "Makefile targets" },

  // Code generation scripts
  { prefix: "scripts/", description: "Generation scripts (from repo root)" },
  { prefix: "cd scripts && ./generate-", description: "Generation scripts (from scripts dir)" },
  { prefix: "python3 scripts/", description: "Python generation scripts" },

  // File discovery (read-only, safe)
  { prefix: "ls ", description: "List directory contents" },
  { prefix: "ls docs/", description: "List docs" },
  { prefix: "ls plugins/", description: "List plugins" },
  { prefix: "ls schemas/", description: "List schemas" },
  { prefix: "ls bannou-service/", description: "List bannou-service" },
  { prefix: "find ", description: "Find files" },
  { prefix: "wc ", description: "Count lines/words" },
  { prefix: "comm ", description: "Set difference operations" },

  // Random selection (used by skills for target picking)
  { prefix: "shuf ", description: "Random selection/shuffle" },

  // Read gate management (restricted to /tmp/)
  { prefix: "echo ", description: "Echo (for read gate activation)" },
  { prefix: "rm -f /tmp/", description: "Clean temp files" },
  { prefix: "cat /tmp/", description: "Read temp files" },

  // Git (read-only queries — destructive ops blocked by pattern check below)
  { prefix: "git status", description: "Git status" },
  { prefix: "git diff", description: "Git diff" },
  { prefix: "git log", description: "Git log" },

  // Agent-created scripts (sandboxed to /tmp/bannou-scripts/)
  { prefix: "/tmp/bannou-scripts/", description: "Execute agent-created scripts" },

  // Node.js syntax checking and version (read-only, no execution)
  { prefix: "node --check ", description: "Node.js syntax check" },
  { prefix: "node --version", description: "Node.js version" },
  { prefix: "node -v", description: "Node.js version (short)" },
];

/**
 * Commands that are always blocked regardless of prefix match.
 * These patterns catch dangerous operations that might slip through
 * the prefix whitelist (e.g., "echo malicious > /etc/important").
 */
export const BLOCKED_PATTERNS = [
  // File write operations outside /tmp/ via shell redirection
  /(?<!\d)>\s*(?!\/tmp\/|&)[^\s]/,  // redirect to non-/tmp path (excludes >& stderr merge)
  />>(?!\/tmp\/)/,                   // append to non-/tmp path

  // MCP-internal state file protection — prevents shell-redirect bypass of
  // NEVER_OVERRIDABLE and the scope system. The general /tmp/ allowlist above
  // lets agents write recovery files, scripts, and continuation data, but the
  // sentinel file, active-scope state, and parallel-read gate files are
  // server-owned state that agents must NEVER modify directly. Bypassing them
  // via `echo '{...}' > /tmp/bannou-mcp-inject-1.json` would grant self-permissions
  // without user approval — this pattern was exploited once in session, caught here.
  />\s*['"]?\/tmp\/bannou-mcp-inject-/,     // sentinel injection file (permission grants)
  />\s*['"]?\/tmp\/bannou-active-scope-/,   // active scope state (scope bypass)
  />\s*['"]?\/tmp\/\.parallel-read-/,       // parallel read gate files

  // Dangerous git operations
  /git\s+(reset|restore|checkout\s+--|checkout\s+\.|clean|stash|push\s+--force|push\s+-f)/,

  // Process execution / code injection
  /\$\(/,                        // command substitution (except in gh --body which uses heredoc)
  /`[^`]+`/,                    // backtick execution

  // Dangerous file operations
  /\brm\s+(?!-f\s+\/tmp\/)/,   // rm anything outside /tmp/
  /\bmv\s/,                     // move files
  /\bcp\s/,                     // copy files (could overwrite)
  /\bchmod\s/,                  // change permissions
  /\bchown\s/,                  // change ownership

  // Ad-hoc scripting
  /\bpython3?\s+-c\b/,          // inline python
  /\bnode\s+-e\b/,              // inline node
  /\beval\b/,                   // eval
  /\bsource\b/,                 // source
  /\bcurl\b/,                   // network requests
  /\bwget\b/,                   // network requests
];

/**
 * gh commands use heredocs with $() for --body, so we exempt gh issue/pr commands
 * from the command substitution check.
 */
export function isGhHeredocCommand(command) {
  return /^gh\s+(issue|pr)\s+(create|comment|close)/.test(command);
}

/**
 * Validates a command against the whitelist and blocked patterns.
 * Returns null if valid, or an error response object if blocked.
 */
export function validateCommand(trimmed) {
  // Strip leading environment variable assignments for prefix matching.
  // Allows patterns like: BANNOU_RUN_INFORMATIONAL_TESTS=true dotnet test ...
  // The full command (with env vars) is still passed to exec — the shell handles them.
  const commandForPrefixCheck = trimmed.replace(/^(?:[A-Z_][A-Z0-9_]*=\S+\s+)+/, "");

  // Check against whitelist
  const allowed = ALLOWED_COMMANDS.find((entry) =>
    commandForPrefixCheck.startsWith(entry.prefix),
  );

  if (!allowed) {
    const prefixes = ALLOWED_COMMANDS.map((e) => `  ${e.prefix}* — ${e.description}`).join("\n");
    return {
      content: [
        {
          type: "text",
          text: [
            `Error: Command not allowed: ${trimmed.slice(0, 80)}${trimmed.length > 80 ? "..." : ""}`,
            "",
            "Allowed command prefixes:",
            prefixes,
            "",
            "For file reading use read_file. For file editing use edit_file.",
          ].join("\n"),
        },
      ],
      isError: true,
    };
  }

  // Check against blocked patterns
  for (const pattern of BLOCKED_PATTERNS) {
    if (pattern.source === "\\$\\(" && isGhHeredocCommand(trimmed)) {
      continue;
    }
    if (pattern.test(trimmed)) {
      return {
        content: [
          {
            type: "text",
            text: `Error: Command contains a blocked pattern. ${trimmed.slice(0, 80)}`,
          },
        ],
        isError: true,
      };
    }
  }

  return null; // Valid
}

/**
 * Checks the global pending-continuations gate for run_command.
 * Returns null if clear, or an error response if blocked.
 */
export function checkCommandGate() {
  if (pendingContinuations.size > 0) {
    const details = [];
    for (const [original, pending] of pendingContinuations) {
      details.push(`  ${original} — ${pending.size} unread part(s):`);
      for (const p of pending) {
        details.push(`    ${p}`);
      }
    }
    return {
      content: [
        {
          type: "text",
          text: [
            "Error: Cannot run commands while split-file reads are incomplete.",
            "The following files have unread continuation parts:",
            "",
            ...details,
            "",
            "Read ALL continuation files with read_file first, then retry.",
          ].join("\n"),
        },
      ],
      isError: true,
    };
  }
  return null;
}

/**
 * Handles large command output — writes to temp file if over chunk limit.
 * Returns the formatted response content.
 */
export async function handleLargeOutput(output, commandStr) {
  // Check serialized size to avoid Claude Code persisting the response as escaped JSON
  const serializedSize = Buffer.byteLength(JSON.stringify(output), "utf-8") + 40;

  if (serializedSize <= RESPONSE_SERIALIZED_LIMIT) {
    return { content: [{ type: "text", text: output }] };
  }

  const hash = createHash("md5").update(commandStr).digest("hex").slice(0, 12);
  const outputPath = join(tmpdir(), `bannou-cmd-${hash}.txt`);
  await writeFile(outputPath, output, "utf-8");

  const preview = output.slice(0, CHUNK_BYTE_LIMIT);
  const totalKB = (outputSize / 1024).toFixed(1);

  return {
    content: [
      {
        type: "text",
        text: [
          `Command output (${totalKB} KB — showing first ${(CHUNK_BYTE_LIMIT / 1024).toFixed(0)} KB):`,
          "",
          preview,
          "",
          `--- OUTPUT TRUNCATED ---`,
          `Full output saved to: ${outputPath}`,
          `Use read_file to read the full output.`,
        ].join("\n"),
      },
    ],
  };
}
