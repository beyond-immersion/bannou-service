/**
 * Sentinel file injection — external state manipulation.
 *
 * Enables humans (via Stream Deck, terminal, scripts) to inject commands
 * into the MCP server's state without agent involvement. The server checks
 * for a sentinel file at the start of every tool call. If found, it
 * processes the commands and deletes the file.
 *
 * SECURITY: The sentinel file and scripts/restricted/ are in NEVER_OVERRIDABLE —
 * agents cannot write to them via any MCP tool, even with grantPermissions.
 * Frozen directories (scripts/, docs/reference/, etc.) are blocked by default
 * but can be temporarily unlocked via grantPermissions sentinel injection.
 *
 * Sentinel file format (/tmp/bannou-mcp-inject-{bucket}.json):
 * {
 *   "markRead": ["/path/to/file1", "/path/to/file2"],
 *   "grantPermissions": ["scripts/", "docs/reference/"],
 *   "revokePermissions": ["scripts/"],
 *   "clearGate": true,
 *   "prepareContext": { "profile": "dev" },
 *   "prepareContext": { "profile": "plugin", "service": "account" },
 *   "prepareContext": { "profile": "custom", "files": ["path/to/file"] },
 *   "message": "Optional message displayed to the agent"
 * }
 */

import { readFile, unlink, access } from "node:fs/promises";
import { constants } from "node:fs";
import { resolve } from "node:path";

import {
  readFiles,
  requiredReading,
  SENTINEL_PATH,
  NEVER_OVERRIDABLE,
  FROZEN_PREFIXES,
  FROZEN_EXACT,
} from "../state.mjs";

import { prepareContext } from "./context.mjs";
import { emitSentinelEvent } from "./dashboard-emitter.mjs";
import {
  checkPathAgainstScope,
  isPathScopeControlled,
  listScopes,
  clearActiveScope as clearActiveScopeState,
} from "./scope.mjs";

// ─── Permission State ──────────────────────────────────────────────────────

/**
 * Tracks paths/prefixes that have been granted temporary write permission
 * via sentinel injection. Checked by isPathProtected() to allow writes
 * to normally-frozen locations.
 *
 * Format: Set of path prefixes (e.g., "scripts/", "docs/reference/")
 * A write to "/home/user/repo/scripts/foo.sh" is allowed if "scripts/"
 * is in grantedPermissions and the resolved path matches.
 */
export const grantedPermissions = new Set();

/**
 * Messages injected by sentinel for the agent to see on next tool call.
 * Cleared after being returned once.
 */
let pendingMessage = null;

// ─── Sentinel Processing ───────────────────────────────────────────────────

/**
 * Process the sentinel file if it exists. Called at the start of every
 * tool invocation via the registerTool wrapper in server.mjs.
 *
 * If the file exists, reads it, processes commands, and deletes it.
 * Returns a message string if one was injected, null otherwise.
 *
 * This function never throws — sentinel processing failures are logged
 * to stderr but do not block tool execution.
 */
export async function processSentinel() {
  try {
    // Check if sentinel file exists
    try {
      await access(SENTINEL_PATH, constants.R_OK);
    } catch {
      // No sentinel file — return any pending message from last processing
      const msg = pendingMessage;
      pendingMessage = null;
      return msg;
    }

    // Read and parse
    const raw = await readFile(SENTINEL_PATH, "utf-8");
    let commands;
    try {
      commands = JSON.parse(raw);
    } catch (parseErr) {
      console.error(`[sentinel] Failed to parse ${SENTINEL_PATH}: ${parseErr.message}`);
      await safeUnlink();
      return null;
    }

    // Delete sentinel file immediately (before processing, so it's not re-processed)
    await safeUnlink();

    // Broadcast sentinel event to dashboard (fire-and-forget)
    try { emitSentinelEvent(commands); } catch { /* never break sentinel processing */ }

    const results = [];

    // ── markRead: add paths to the readFiles set ──
    if (Array.isArray(commands.markRead)) {
      for (const p of commands.markRead) {
        readFiles.add(resolve(p));
      }
      results.push(`Marked ${commands.markRead.length} file(s) as read`);
    }

    // ── grantPermissions: temporarily allow writes to frozen paths ──
    if (Array.isArray(commands.grantPermissions)) {
      for (const prefix of commands.grantPermissions) {
        grantedPermissions.add(prefix);
      }
      results.push(`Granted write permission for: ${commands.grantPermissions.join(", ")}`);
    }

    // ── revokePermissions: remove previously granted permissions ──
    if (Array.isArray(commands.revokePermissions)) {
      for (const prefix of commands.revokePermissions) {
        grantedPermissions.delete(prefix);
      }
      results.push(`Revoked write permission for: ${commands.revokePermissions.join(", ")}`);
    }

    // ── clearGate: clear all required reading gate entries ──
    if (commands.clearGate === true) {
      const count = requiredReading.size;
      requiredReading.clear();
      results.push(`Cleared ${count} required reading gate entr${count === 1 ? "y" : "ies"}`);
    }

    // ── clearScope: force-clear the active scope without running exit validation ──
    if (commands.clearScope === true) {
      const cleared = await clearActiveScopeState();
      results.push(cleared ? "Cleared active scope (no exit validation)" : "No active scope to clear");
    }

    // ── prepareContext: pack documentation into composites and gate the agent ──
    if (commands.prepareContext && typeof commands.prepareContext === "object") {
      const { profile, service, files } = commands.prepareContext;
      if (profile) {
        try {
          const options = {};
          if (service) options.service = service;
          if (files) options.files = files;

          const ctxResult = await prepareContext(profile, options);

          if (ctxResult.error) {
            results.push(`prepareContext failed: ${ctxResult.error}`);
          } else if (ctxResult.alreadyLoaded) {
            results.push(`Context "${profile}" already loaded (${ctxResult.totalFiles} files in context)`);
          } else {
            const compositeCount = ctxResult.composites.length;
            const compositeList = ctxResult.composites
              .map((c) => c.path)
              .join(", ");
            results.push(
              `Context "${profile}" prepared: ${ctxResult.packed} files packed into ${compositeCount} composite(s). ` +
              `Read these to unlock all tools: ${compositeList}`
            );
          }
        } catch (err) {
          console.error(`[sentinel] prepareContext error: ${err.message}`);
          results.push(`prepareContext error: ${err.message}`);
        }
      }
    }

    // ── message: store for delivery to agent on next tool response ──
    if (commands.message) {
      pendingMessage = String(commands.message);
      results.push(`Message queued for agent`);
    }

    if (results.length > 0) {
      console.error(`[sentinel] Processed: ${results.join("; ")}`);
    }

    // Return message immediately if one was set
    if (pendingMessage) {
      const msg = pendingMessage;
      pendingMessage = null;
      return msg;
    }
    return null;
  } catch (err) {
    console.error(`[sentinel] Error processing sentinel file: ${err.message}`);
    return null;
  }
}

async function safeUnlink() {
  try {
    await unlink(SENTINEL_PATH);
  } catch {
    // File may have been deleted by another process
  }
}

// ─── Path Protection ───────────────────────────────────────────────────────

/**
 * Resolves a project-relative path to an absolute path using CLAUDE_PROJECT_DIR.
 */
function resolveProjectPath(relativePath) {
  const projectDir = process.env.CLAUDE_PROJECT_DIR || process.cwd();
  return resolve(projectDir, relativePath);
}

/**
 * Check if a resolved file path is protected from agent writes.
 * Returns { blocked: true, reason: string } if blocked, or { blocked: false } if allowed.
 *
 * Three-tier protection (checked in order):
 *   1. NEVER_OVERRIDABLE — sentinel file, scripts/restricted/. Always blocked.
 *   2. Scope gate — there must be an active scope, and the path must be in it.
 *      No active scope → gentle nudge with list_scopes output.
 *      Scope active but path outside → gentle nudge with scope metadata.
 *   3. FROZEN_PREFIXES + FROZEN_EXACT — frozen directories still require a
 *      sentinel grant even when the scope allows them (belt-and-suspenders).
 *
 * Async because the scope check reads persistent state from disk.
 */
export async function isPathProtected(resolvedPath) {
  // ── Tier 1: Never overridable (not even with grantPermissions) ──
  for (const entry of NEVER_OVERRIDABLE) {
    if (entry.type === "exact" && resolvedPath === entry.path) {
      return { blocked: true, reason: `${resolvedPath} is a permanently protected path and cannot be modified by agents.` };
    }
    if (entry.type === "prefix") {
      const absPrefix = resolveProjectPath(entry.path);
      if (resolvedPath.startsWith(absPrefix)) {
        return { blocked: true, reason: `${resolvedPath} is in a permanently protected directory (${entry.path}) and cannot be modified by agents.` };
      }
    }
  }

  // ── Tier 2: Scope gate ──
  // Scopes control writes to scope-managed paths (plugins/, sdks/, schemas/).
  // Files NOT in any scope-managed directory AND not frozen are freely editable
  // without requiring an active scope — repo root files (CLAUDE.md, etc.),
  // docs/plugins/, docs/maps/, docs/generated/, etc. are all open.
  const scopeCheck = await checkPathAgainstScope(resolvedPath);
  if (!scopeCheck.hasScope) {
    // No active scope. Check if this path is in a scope-controlled area.
    // If it's not scope-controlled AND not frozen → allow freely.
    const isFrozen = isFrozenPath(resolvedPath);
    const isScopeControlled = isPathScopeControlled(resolvedPath);
    if (!isFrozen && !isScopeControlled) {
      return { blocked: false };
    }
    const scopes = await listScopes();
    return { blocked: true, reason: buildNoScopeMessage(resolvedPath, scopes) };
  }
  if (!scopeCheck.inScope) {
    // Active scope exists but path is outside it. Same check: if the path
    // isn't scope-controlled or frozen, allow it even outside the active scope.
    const isFrozen = isFrozenPath(resolvedPath);
    const isScopeControlled = isPathScopeControlled(resolvedPath);
    if (!isFrozen && !isScopeControlled) {
      return { blocked: false };
    }
    return { blocked: true, reason: buildOutOfScopeMessage(resolvedPath, scopeCheck.scope) };
  }

  // ── Tier 3: Frozen directories (scope allows the path but frozen grant is still required) ──
  for (const prefix of FROZEN_PREFIXES) {
    const absPrefix = resolveProjectPath(prefix);
    if (resolvedPath.startsWith(absPrefix)) {
      if (grantedPermissions.has(prefix)) {
        return { blocked: false };
      }
      return {
        blocked: true,
        reason: `${resolvedPath} is in a frozen directory (${prefix}). The active scope permits this path, but a sentinel grant is also required. Ask the user to grant ${prefix}.`,
      };
    }
  }

  for (const exactPath of FROZEN_EXACT) {
    const absExact = resolveProjectPath(exactPath);
    if (resolvedPath === absExact) {
      if (grantedPermissions.has(exactPath)) {
        return { blocked: false };
      }
      return {
        blocked: true,
        reason: `${resolvedPath} is a frozen file (${exactPath}). The active scope permits this path, but a sentinel grant is also required. Ask the user to grant ${exactPath}.`,
      };
    }
  }

  return { blocked: false };
}

/**
 * Returns true if the resolved path is in a frozen directory (docs/reference/,
 * scripts/, structural-tests/, etc.) or is a frozen exact file.
 */
function isFrozenPath(resolvedPath) {
  for (const prefix of FROZEN_PREFIXES) {
    const absPrefix = resolveProjectPath(prefix);
    if (resolvedPath.startsWith(absPrefix)) return true;
  }
  for (const exactPath of FROZEN_EXACT) {
    const absExact = resolveProjectPath(exactPath);
    if (resolvedPath === absExact) return true;
  }
  return false;
}

/**
 * Builds the "no active scope" message — intentionally informational rather
 * than punitive. Includes list_scopes output so the agent has everything it
 * needs to start a scope in one response.
 */
function buildNoScopeMessage(resolvedPath, scopes) {
  const pluginList = scopes.plugins.map((p) => p.name).join(", ");
  const sdkList = scopes.sdks.map((s) => s.name).join(", ");
  const dirList = scopes.directories.map((d) => `    ${d.id.padEnd(18)} ${d.description}`).join("\n");
  const alsoList = scopes.alsoKeys.map((a) => `    ${a.id.padEnd(18)} ${a.description}`).join("\n");

  return [
    `No active scope — writes are locked until one is started.`,
    ``,
    `Scopes control which files are editable during a task. Each scope auto-loads`,
    `its own context (dev profile + deep dive + implementation map for plugins/SDKs)`,
    `and activates the required reading gate. stop_scope runs exit validation`,
    `(build, diff scan) before releasing.`,
    ``,
    `To begin, call start_scope with one of:`,
    ``,
    `  plugin:name   (${scopes.plugins.length} available)`,
    `    ${pluginList}`,
    ``,
    `  sdk:name      (${scopes.sdks.length} available)`,
    `    ${sdkList}`,
    ``,
    `  Directory scopes (sentinel grant required before starting):`,
    dirList,
    ``,
    `  Supplementary unlocks (pass via also: [...] on plugin/sdk scopes):`,
    alsoList,
    ``,
    `Attempted write: ${resolvedPath}`,
  ].join("\n");
}

/**
 * Builds the "path outside active scope" message — reminds the agent of
 * the active scope and the cost of switching (stop_scope validation).
 */
function buildOutOfScopeMessage(resolvedPath, scope) {
  const alsoSuffix = scope.also && scope.also.length > 0 ? ` (+also: ${scope.also.join(", ")})` : "";
  return [
    `Path is outside the active scope '${scope.scope}'${alsoSuffix}.`,
    ``,
    `Active scope:   ${scope.scope}${alsoSuffix}`,
    `Started:        ${scope.startedAt}`,
    `Attempted write: ${resolvedPath}`,
    ``,
    `If this work genuinely belongs to a different scope, call stop_scope`,
    `(which runs build + diff validations) and then start_scope with the new`,
    `identifier. If you need shared infrastructure access, stop_scope and`,
    `restart with also: ["infrastructure"] from the beginning — scope switches`,
    `are deliberately high-friction to prevent target fixation.`,
  ].join("\n");
}
