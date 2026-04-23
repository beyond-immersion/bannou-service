/**
 * Scope system — mechanical gate controlling which files agents may edit
 * during a task. Everything is locked by default; start_scope unlocks a
 * specific plugin/SDK/directory, stop_scope runs exit validation and
 * re-locks. Composes with the existing frozen-directory + sentinel-grant
 * gates in sentinel.mjs.
 *
 * State is persisted per-bucket to /tmp/bannou-active-scope-{bucket}.json
 * so scope survives across sessions (forgotten scopes are detected on the
 * next tool call). The sentinel clearScope command force-clears it.
 *
 * PATH MATCHING:
 *   isPathInActiveScope checks a resolved absolute path against the active
 *   scope's pre-computed pathSet. Three forms:
 *     1. Exact file match (Set of absolute paths)
 *     2. Directory prefix match (array of absolute directory prefixes, with
 *        optional exclusions for the `also: infrastructure` case)
 *     3. Schema prefix match (dynamic — always allows schemas/common-*.yaml
 *        when any scope is active, plus schemas/{name}-*.yaml for plugin
 *        scopes)
 */

import { readFile, writeFile, unlink, access, readdir } from "node:fs/promises";
import { resolve, join } from "node:path";
import { constants } from "node:fs";
import { tmpdir } from "node:os";

import { MCP_BUCKET } from "../state.mjs";
import { prepareContext } from "./context.mjs";
import { execAsync } from "./commands.mjs";
import { validateTenets } from "./tenets.mjs";

// ─── Paths ────────────────────────────────────────────────────────────────

function projectDir() {
  return process.env.CLAUDE_PROJECT_DIR || process.cwd();
}

function resolveProjectPath(rel) {
  return resolve(projectDir(), rel);
}

const SCOPE_STATE_PATH = join(tmpdir(), `bannou-active-scope-${MCP_BUCKET}.json`);

// ─── Naming helpers ───────────────────────────────────────────────────────

// genesis → Genesis, character-encounter → CharacterEncounter
function toPascalCase(name) {
  return name
    .split("-")
    .map((s) => s.charAt(0).toUpperCase() + s.slice(1))
    .join("");
}

// ─── Scope manifest ───────────────────────────────────────────────────────

/**
 * Directory-style scopes that are fixed (not parameterized). Each maps to
 * a specific frozen directory and requires a sentinel grant before the
 * scope can be started.
 */
const DIRECTORY_SCOPES = {
  "scripts": {
    description: "Code generation scripts (bash, python)",
    directory: "scripts/",
    frozenPrefix: "scripts/",
    contextProfile: { profile: "dev" },
  },
  "structural-tests": {
    description: "Structural tests project — cross-cutting conventions validation",
    directory: "structural-tests/",
    frozenPrefix: "structural-tests/",
    contextProfile: { profile: "dev" },
  },
  "claude": {
    description: "Claude Code configuration — hooks, skills, MCP server, agents, rules",
    directory: ".claude/",
    frozenPrefix: ".claude/",
    contextProfile: { profile: "claude" },
  },
  "test-utilities": {
    description: "Test utilities project — shared validators and test infrastructure",
    directory: "test-utilities/",
    frozenPrefix: "test-utilities/",
    contextProfile: { profile: "dev" },
  },
  "tenets": {
    description: "Tenet reference documentation — docs/reference/",
    directory: "docs/reference/",
    frozenPrefix: "docs/reference/",
    contextProfile: { profile: "dev" },
  },
};

/**
 * Supplementary unlock keys — additive to a primary plugin/sdk scope via
 * the `also` parameter. Not allowed for directory scopes.
 */
const ALSO_MANIFEST = {
  "infrastructure": {
    description: "Shared infrastructure in bannou-service/ (excluding Generated/)",
    dirPrefixes: ["bannou-service/"],
    dirExclusions: ["bannou-service/Generated/"],
  },
};

// ─── Scope path set computation ───────────────────────────────────────────

/**
 * Known schema file suffixes for a single service. Used to compute exact
 * plugin-owned schema file paths at scope-start time, avoiding the
 * prefix-collision problem where `plugin:character` would otherwise match
 * `schemas/character-encounter-*.yaml` (which belongs to a different plugin).
 */
const SCHEMA_SUFFIXES = [
  "-api.yaml",
  "-service-events.yaml",
  "-client-events.yaml",
  "-configuration.yaml",
];

/**
 * Computes the absolute allowed path set for a given scope.
 * Returns { exactFiles, dirPrefixes, dirExclusions, schemaPrefixes, schemasDir }.
 *
 * schemaPrefixes contains ONLY collision-safe prefixes (currently just
 * "common-"). Plugin-owned schemas are added to exactFiles via the known
 * SCHEMA_SUFFIXES list, not via prefix matching, so that `plugin:character`
 * does not accidentally unlock `character-encounter-*.yaml`.
 */
function computeScopePathSet(type, name, alsoList) {
  const exactFiles = new Set();
  const dirPrefixes = [];
  const dirExclusions = [];
  // common-*.yaml is always editable when any scope is active.
  // "common-" is reserved — no plugin/sdk is named with this prefix, so there's
  // no collision risk from using dynamic prefix matching here.
  const schemaPrefixes = ["common-"];

  if (type === "plugin") {
    dirPrefixes.push(resolveProjectPath(`plugins/lib-${name}/`));
    dirPrefixes.push(resolveProjectPath(`plugins/lib-${name}.tests/`));

    // Plugin-owned schemas — enumerate exact paths from known suffix list
    for (const suffix of SCHEMA_SUFFIXES) {
      exactFiles.add(resolveProjectPath(`schemas/${name}${suffix}`));
    }

    // Generated files in bannou-service/Generated/ that belong to this plugin
    const pascal = toPascalCase(name);
    exactFiles.add(resolveProjectPath(`bannou-service/Generated/Models/${pascal}Models.cs`));
    exactFiles.add(resolveProjectPath(`bannou-service/Generated/Clients/${pascal}Client.cs`));
    exactFiles.add(resolveProjectPath(`bannou-service/Generated/Events/${pascal}EventsModels.cs`));

    // Documentation
    const upper = name.toUpperCase();
    exactFiles.add(resolveProjectPath(`docs/plugins/${upper}.md`));
    exactFiles.add(resolveProjectPath(`docs/maps/${upper}.md`));

    // Planning documents — plugin development often involves planning docs
    dirPrefixes.push(resolveProjectPath("docs/planning/"));
  } else if (type === "sdk") {
    dirPrefixes.push(resolveProjectPath(`sdks/${name}/`));
    dirPrefixes.push(resolveProjectPath(`sdks/${name}.tests/`));

    const upper = name.toUpperCase();
    exactFiles.add(resolveProjectPath(`docs/sdks/${upper}.md`));
    exactFiles.add(resolveProjectPath(`docs/sdks/maps/${upper}.md`));

    // Planning documents — SDK development often involves planning docs
    dirPrefixes.push(resolveProjectPath("docs/planning/"));
  } else if (DIRECTORY_SCOPES[type]) {
    dirPrefixes.push(resolveProjectPath(DIRECTORY_SCOPES[type].directory));
  }

  // Apply `also` additions (not allowed for directory scopes — validated upstream)
  for (const alsoKey of alsoList || []) {
    const also = ALSO_MANIFEST[alsoKey];
    if (!also) continue;
    for (const dir of also.dirPrefixes) dirPrefixes.push(resolveProjectPath(dir));
    for (const excl of also.dirExclusions) dirExclusions.push(resolveProjectPath(excl));
  }

  return {
    exactFiles: Array.from(exactFiles),
    dirPrefixes,
    dirExclusions,
    schemaPrefixes,
    schemasDir: resolveProjectPath("schemas") + "/",
  };
}

// ─── Scope state persistence ──────────────────────────────────────────────
//
// State is persisted as a stack: `{ scopes: [scope1, scope2, ...] }` where
// scopes[0] is the base (first-pushed) and the last element is the top
// (most-recently-pushed). A single-scope session is a one-element stack.
//
// Backward-compat read: older state files stored a single scope object at
// the top level with a `scope` field. getActiveScopeStack() normalizes that
// shape to `[oldObject]` transparently; writers always emit the new shape.

/**
 * Reads the active scope stack from disk. Returns [] if none.
 * Normalizes legacy single-scope state files into a one-element array.
 */
export async function getActiveScopeStack() {
  try {
    await access(SCOPE_STATE_PATH, constants.R_OK);
    const raw = await readFile(SCOPE_STATE_PATH, "utf-8");
    const data = JSON.parse(raw);
    // New format: { scopes: [...] }
    if (data && Array.isArray(data.scopes)) return data.scopes;
    // Legacy format: a single scope object with a `scope` string at top level
    if (data && typeof data.scope === "string") return [data];
    return [];
  } catch {
    return [];
  }
}

/**
 * Returns the top-of-stack scope (most recently pushed) or null if no scope
 * is active. Preserves the pre-stack call-site API — callers that only need
 * "the active scope" (e.g., display, error messaging) get the most-relevant
 * entry without having to reason about stacking.
 */
export async function getActiveScope() {
  const stack = await getActiveScopeStack();
  return stack.length > 0 ? stack[stack.length - 1] : null;
}

async function saveScopeStack(scopes) {
  await writeFile(
    SCOPE_STATE_PATH,
    JSON.stringify({ scopes }, null, 2),
    "utf-8",
  );
}

/**
 * Clears the persistent scope state entirely (removes ALL stacked scopes).
 * Used by stop_scope when the last scope is released and by the sentinel
 * clearScope command. Returns true if state was cleared.
 */
export async function clearActiveScope() {
  try {
    await unlink(SCOPE_STATE_PATH);
    return true;
  } catch {
    return false;
  }
}

// ─── Scope ID parsing ─────────────────────────────────────────────────────

/**
 * Parses a scope identifier. Returns { type, name, valid, error }.
 *
 * Formats:
 *   - "plugin:name" / "sdk:name"
 *   - "scripts" / "structural-tests" / "claude" / "test-utilities" / "tenets"
 */
export function parseScopeId(scopeId) {
  if (typeof scopeId !== "string" || scopeId.length === 0) {
    return { valid: false, error: "Scope identifier must be a non-empty string" };
  }

  const colonIdx = scopeId.indexOf(":");
  if (colonIdx === -1) {
    if (DIRECTORY_SCOPES[scopeId]) {
      return { type: scopeId, name: null, valid: true };
    }
    return {
      valid: false,
      error: `Unknown scope '${scopeId}'. Use a directory scope (${Object.keys(DIRECTORY_SCOPES).join(", ")}) or a parameterized scope (plugin:name, sdk:name).`,
    };
  }

  const type = scopeId.slice(0, colonIdx);
  const name = scopeId.slice(colonIdx + 1);

  if (type !== "plugin" && type !== "sdk") {
    return {
      valid: false,
      error: `Unknown parameterized scope type '${type}'. Supported: plugin, sdk.`,
    };
  }

  if (name.length === 0) {
    return { valid: false, error: `Scope type '${type}' requires a name (e.g., '${type}:genesis').` };
  }

  if (!/^[a-z][a-z0-9-]*$/.test(name)) {
    return {
      valid: false,
      error: `Scope name '${name}' must be kebab-case alphanumeric (starts with lowercase letter, only a-z 0-9 and hyphens).`,
    };
  }

  return { type, name, valid: true };
}

// ─── Plugin & SDK discovery ───────────────────────────────────────────────

async function listPluginsFromDisk() {
  const pluginsDir = resolveProjectPath("plugins");
  try {
    const entries = await readdir(pluginsDir, { withFileTypes: true });
    const names = [];
    for (const entry of entries) {
      if (!entry.isDirectory()) continue;
      if (!entry.name.startsWith("lib-")) continue;
      if (entry.name.endsWith(".tests")) continue;
      names.push(entry.name.slice(4)); // strip "lib-"
    }
    return names.sort();
  } catch {
    return [];
  }
}

async function listSdksFromDisk() {
  const sdksDir = resolveProjectPath("sdks");
  try {
    const entries = await readdir(sdksDir, { withFileTypes: true });
    const names = [];
    for (const entry of entries) {
      if (!entry.isDirectory()) continue;
      // Skip hidden directories (.vs, .git, .idea, etc.) — they're IDE/VCS metadata, not SDKs
      if (entry.name.startsWith(".")) continue;
      if (entry.name.endsWith(".tests")) continue;
      names.push(entry.name);
    }
    return names.sort();
  } catch {
    return [];
  }
}

// ─── Target validation ───────────────────────────────────────────────────

async function validateScopeTarget(type, name) {
  if (DIRECTORY_SCOPES[type]) return { valid: true };

  if (type === "plugin") {
    const plugins = await listPluginsFromDisk();
    if (!plugins.includes(name)) {
      return { valid: false, error: `Plugin '${name}' not found. Use list_scopes to see available plugins.` };
    }
    return { valid: true };
  }

  if (type === "sdk") {
    const sdks = await listSdksFromDisk();
    if (!sdks.includes(name)) {
      // SDK directory doesn't exist yet — allow it for new SDK creation.
      // The scope will unlock sdks/{name}/ for writing, enabling mkdir + file creation.
      return { valid: true, isNew: true };
    }
    return { valid: true };
  }

  return { valid: false, error: `Unknown scope type '${type}'.` };
}

// ─── Main API: start_scope ────────────────────────────────────────────────

/**
 * Start a new scope. Fails if another scope is already active, if the
 * identifier is malformed, if the target doesn't exist, or if the scope
 * targets a frozen directory without a sentinel grant.
 *
 * grantedPermissions is the Set<string> from sentinel.mjs — passed in to
 * avoid a circular import.
 */
export async function startScope(scopeId, alsoList, grantedPermissions) {
  const existingStack = await getActiveScopeStack();
  if (existingStack.length > 0) {
    const top = existingStack[existingStack.length - 1];
    return {
      error: `Scope '${top.scope}' is already active (started ${top.startedAt}). Call stop_scope to release it before starting a new one, or use add_scope to layer a supplementary scope on top.`,
    };
  }

  const parsed = parseScopeId(scopeId);
  if (!parsed.valid) return { error: parsed.error };

  const { type, name } = parsed;

  const targetCheck = await validateScopeTarget(type, name);
  if (!targetCheck.valid) return { error: targetCheck.error };

  // Frozen directory scopes require a sentinel grant before they can start
  if (DIRECTORY_SCOPES[type]) {
    const frozenPrefix = DIRECTORY_SCOPES[type].frozenPrefix;
    if (!grantedPermissions || !grantedPermissions.has(frozenPrefix)) {
      return {
        error: [
          `Scope '${type}' targets a frozen directory (${frozenPrefix}) but no sentinel grant is present.`,
          ``,
          `Ask the user to grant ${frozenPrefix} via sentinel injection:`,
          `  { "grantPermissions": ["${frozenPrefix}"] }`,
          ``,
          `Written to ${SCOPE_STATE_PATH.replace(/active-scope-\d+\.json$/, "inject-{bucket}.json")}. After the grant is in place, retry start_scope.`,
        ].join("\n"),
      };
    }
  }

  const normalizedAlso = Array.isArray(alsoList) ? alsoList : [];
  for (const alsoKey of normalizedAlso) {
    if (!ALSO_MANIFEST[alsoKey]) {
      return {
        error: `Unknown 'also' key '${alsoKey}'. Supported: ${Object.keys(ALSO_MANIFEST).join(", ")}.`,
      };
    }
    if (DIRECTORY_SCOPES[type]) {
      return {
        error: `The 'also' parameter is not supported for directory scopes (${type}). Only plugin/sdk scopes can use 'also'.`,
      };
    }
  }

  const pathSet = computeScopePathSet(type, name, normalizedAlso);

  let contextProfile;
  if (type === "plugin") {
    contextProfile = { profile: "plugin", service: name };
  } else if (type === "sdk") {
    contextProfile = { profile: "sdk", sdk: name };
  } else {
    contextProfile = DIRECTORY_SCOPES[type].contextProfile;
  }

  let contextResult = null;
  try {
    const ctxOptions = {};
    if (contextProfile.service) ctxOptions.service = contextProfile.service;
    if (contextProfile.sdk) ctxOptions.sdk = contextProfile.sdk;
    contextResult = await prepareContext(contextProfile.profile, ctxOptions);
  } catch (err) {
    return { error: `Failed to load context for scope: ${err.message}` };
  }

  const scopeData = {
    scope: scopeId,
    type,
    name,
    also: normalizedAlso,
    startedAt: new Date().toISOString(),
    bucket: MCP_BUCKET,
    pathSet,
    contextProfile,
  };
  await saveScopeStack([scopeData]);

  return { scope: scopeData, contextResult };
}

// ─── Main API: add_scope ──────────────────────────────────────────────────

/**
 * Layer a supplementary scope on top of the existing scope stack. Fails if
 * no scope is currently active, if the new scope is already present in the
 * stack, or if the new scope targets a frozen directory without a sentinel
 * grant (same grant rules as start_scope).
 *
 * Path checks pass when the path is in ANY scope in the stack — the new
 * scope's path set is additive. stop_scope pops only the top scope, so a
 * stack with plugin:foo + scripts pops the scripts layer first (runs
 * scripts validations, leaves plugin:foo active).
 *
 * grantedPermissions is the Set<string> from sentinel.mjs — passed in to
 * avoid a circular import.
 */
export async function addScope(scopeId, alsoList, grantedPermissions) {
  const stack = await getActiveScopeStack();
  if (stack.length === 0) {
    return {
      error: "No active scope to stack onto. Call start_scope first.",
    };
  }

  const parsed = parseScopeId(scopeId);
  if (!parsed.valid) return { error: parsed.error };

  const { type, name } = parsed;

  // Reject duplicate — stacking the same scope twice has no semantic value
  // and makes stop_scope ambiguous (which layer does it pop?).
  for (const existing of stack) {
    if (existing.scope === scopeId) {
      return {
        error: `Scope '${scopeId}' is already in the active stack (started ${existing.startedAt}).`,
      };
    }
  }

  const targetCheck = await validateScopeTarget(type, name);
  if (!targetCheck.valid) return { error: targetCheck.error };

  // Same frozen-directory sentinel-grant rule as startScope
  if (DIRECTORY_SCOPES[type]) {
    const frozenPrefix = DIRECTORY_SCOPES[type].frozenPrefix;
    if (!grantedPermissions || !grantedPermissions.has(frozenPrefix)) {
      return {
        error: [
          `Scope '${type}' targets a frozen directory (${frozenPrefix}) but no sentinel grant is present.`,
          ``,
          `Ask the user to grant ${frozenPrefix} via sentinel injection:`,
          `  { "grantPermissions": ["${frozenPrefix}"] }`,
          ``,
          `After the grant is in place, retry add_scope.`,
        ].join("\n"),
      };
    }
  }

  const normalizedAlso = Array.isArray(alsoList) ? alsoList : [];
  for (const alsoKey of normalizedAlso) {
    if (!ALSO_MANIFEST[alsoKey]) {
      return {
        error: `Unknown 'also' key '${alsoKey}'. Supported: ${Object.keys(ALSO_MANIFEST).join(", ")}.`,
      };
    }
    if (DIRECTORY_SCOPES[type]) {
      return {
        error: `The 'also' parameter is not supported for directory scopes (${type}). Only plugin/sdk scopes can use 'also'.`,
      };
    }
  }

  const pathSet = computeScopePathSet(type, name, normalizedAlso);

  let contextProfile;
  if (type === "plugin") {
    contextProfile = { profile: "plugin", service: name };
  } else if (type === "sdk") {
    contextProfile = { profile: "sdk", sdk: name };
  } else {
    contextProfile = DIRECTORY_SCOPES[type].contextProfile;
  }

  let contextResult = null;
  try {
    const ctxOptions = {};
    if (contextProfile.service) ctxOptions.service = contextProfile.service;
    if (contextProfile.sdk) ctxOptions.sdk = contextProfile.sdk;
    contextResult = await prepareContext(contextProfile.profile, ctxOptions);
  } catch (err) {
    return { error: `Failed to load context for scope: ${err.message}` };
  }

  const scopeData = {
    scope: scopeId,
    type,
    name,
    also: normalizedAlso,
    startedAt: new Date().toISOString(),
    bucket: MCP_BUCKET,
    pathSet,
    contextProfile,
  };

  const newStack = [...stack, scopeData];
  await saveScopeStack(newStack);

  return {
    scope: scopeData,
    contextResult,
    stackDepth: newStack.length,
    stack: newStack,
  };
}

// ─── Main API: stop_scope ─────────────────────────────────────────────────

/**
 * Stop the top scope on the stack. Runs exit validations for that scope
 * before popping it. Hard-fails (does NOT release) if any validation with
 * kind "fail" occurs, so the agent is forced to fix the issue before
 * proceeding. When the stack is multiple deep, only the top layer is
 * popped — lower scopes remain active.
 *
 * When `options.force === true`, the top scope is popped regardless of
 * validation results. Findings are still returned so the caller can see
 * what was ignored. Intended for rare recovery cases only.
 */
export async function stopScope(options = {}) {
  const force = options && options.force === true;
  const stack = await getActiveScopeStack();
  if (stack.length === 0) {
    return { error: "No active scope to stop." };
  }

  const active = stack[stack.length - 1];
  const findings = await runStopScopeValidations(active);
  const hasFailures = findings.some((f) => f.kind === "fail");

  if (hasFailures && !force) {
    return {
      scope: active,
      findings,
      released: false,
      stackDepth: stack.length,
      error: "Stop-scope validation failed. Fix the issues listed below, then call stop_scope again. Scope remains active.",
    };
  }

  const remaining = stack.slice(0, -1);
  if (remaining.length === 0) {
    await clearActiveScope();
  } else {
    await saveScopeStack(remaining);
  }

  return {
    scope: active,
    findings,
    released: true,
    forced: force && hasFailures,
    remainingStackDepth: remaining.length,
    remainingTop: remaining.length > 0 ? remaining[remaining.length - 1] : null,
  };
}

// ─── Main API: list_scopes ────────────────────────────────────────────────

export async function listScopes() {
  const plugins = await listPluginsFromDisk();
  const sdks = await listSdksFromDisk();
  const directories = Object.entries(DIRECTORY_SCOPES).map(([key, def]) => ({
    id: key,
    description: def.description,
    frozenPrefix: def.frozenPrefix,
  }));
  const alsoKeys = Object.entries(ALSO_MANIFEST).map(([key, def]) => ({
    id: key,
    description: def.description,
  }));

  return {
    plugins: plugins.map((name) => ({ id: `plugin:${name}`, name })),
    sdks: sdks.map((name) => ({ id: `sdk:${name}`, name })),
    directories,
    alsoKeys,
  };
}

// ─── Scope-controlled path detection ──────────────────────────────────────

/**
 * Returns true if the resolved path is in a directory that ANY scope definition
 * could manage. Files outside all scope-managed directories are freely editable
 * without requiring an active scope (e.g., CLAUDE.md at repo root, docs/plugins/,
 * docs/maps/, docs/generated/).
 *
 * This is derived from the scope definitions themselves — not a hardcoded list.
 * If a new scope type or `also` key is added, this function automatically covers it.
 *
 * The parent directories are the common prefixes that all scopes of each type share:
 *   - plugin scopes → plugins/, schemas/, bannou-service/Generated/
 *   - sdk scopes → sdks/
 *   - directory scopes → their explicit directory field
 *   - also keys → their explicit dirPrefixes
 *
 * docs/planning/ is included in plugin/SDK path sets but is intentionally
 * excluded here — planning docs should be freely editable without a scope.
 */
export function isPathScopeControlled(resolvedPath) {
  const pd = projectDir();

  // Plugin scopes manage paths under these parent directories
  const pluginParents = ["plugins/", "schemas/", "bannou-service/Generated/"];
  for (const rel of pluginParents) {
    if (resolvedPath.startsWith(resolve(pd, rel))) return true;
  }

  // SDK scopes manage paths under sdks/
  if (resolvedPath.startsWith(resolve(pd, "sdks/"))) return true;

  // Directory scopes each manage their explicit directory
  for (const def of Object.values(DIRECTORY_SCOPES)) {
    if (resolvedPath.startsWith(resolve(pd, def.directory))) return true;
  }

  // Also keys manage their explicit dirPrefixes (excluding their exclusions,
  // but for "is this controlled at all?" the prefix is sufficient)
  for (const def of Object.values(ALSO_MANIFEST)) {
    for (const dir of def.dirPrefixes) {
      if (resolvedPath.startsWith(resolve(pd, dir))) return true;
    }
  }

  return false;
}

// ─── Main API: path checks ────────────────────────────────────────────────

/**
 * Returns true if the given resolved absolute path is within ANY active
 * scope's allowed path set. Returns false if no scope is active. With a
 * stacked scope, the path is allowed if at least one layer permits it.
 */
export async function isPathInActiveScope(resolvedPath) {
  const stack = await getActiveScopeStack();
  if (stack.length === 0) return false;
  for (const scope of stack) {
    if (matchesPathSet(resolvedPath, scope.pathSet)) return true;
  }
  return false;
}

/**
 * Combined check used by sentinel.mjs isPathProtected — returns everything
 * needed to build an error message in a single read of the scope state.
 *
 *   { hasScope: false }                                      → no active scope
 *   { hasScope: true, inScope: true,  scope, stack }         → path is allowed by some scope in the stack (`scope` is the matching layer)
 *   { hasScope: true, inScope: false, scope, stack }         → path is outside every active scope (`scope` is the top layer, for error messaging)
 */
export async function checkPathAgainstScope(resolvedPath) {
  const stack = await getActiveScopeStack();
  if (stack.length === 0) return { hasScope: false };

  for (const scope of stack) {
    if (matchesPathSet(resolvedPath, scope.pathSet)) {
      return { hasScope: true, inScope: true, scope, stack };
    }
  }

  return {
    hasScope: true,
    inScope: false,
    scope: stack[stack.length - 1],
    stack,
  };
}

function matchesPathSet(resolvedPath, pathSet) {
  // 1. Exact file match
  if (pathSet.exactFiles.includes(resolvedPath)) return true;

  // 2. Directory prefix match (with exclusions)
  for (const prefix of pathSet.dirPrefixes) {
    if (resolvedPath.startsWith(prefix)) {
      for (const excl of pathSet.dirExclusions) {
        if (resolvedPath.startsWith(excl)) return false;
      }
      return true;
    }
  }

  // 3. Schema prefix match (dynamic)
  if (pathSet.schemasDir && resolvedPath.startsWith(pathSet.schemasDir)) {
    const filename = resolvedPath.slice(pathSet.schemasDir.length);
    if (!filename.endsWith(".yaml")) return false;
    for (const schemaPrefix of pathSet.schemaPrefixes) {
      if (filename.startsWith(schemaPrefix)) return true;
    }
  }

  return false;
}

// ─── Stop-scope validators ────────────────────────────────────────────────

async function runStopScopeValidations(scopeData) {
  const findings = [];
  const { type, name } = scopeData;

  // 1. Collect modified files in scope
  const modifiedFiles = await getModifiedFilesInScope(scopeData);
  findings.push({
    kind: "info",
    label: "modified-files",
    message: `${modifiedFiles.length} file(s) modified in this scope`,
    files: modifiedFiles,
  });

  // 2. Diff scan for forbidden patterns (null-forgiving operator)
  const diffFindings = await scanDiffForForbiddenPatterns(modifiedFiles);
  for (const f of diffFindings) findings.push(f);

  // 3. Type-specific build/test validation
  if (type === "plugin") {
    const csproj = `plugins/lib-${name}/lib-${name}.csproj`;
    const result = await runBuild(csproj);
    findings.push({
      kind: result.success ? "pass" : "fail",
      label: "build",
      message: result.success ? `${csproj}: build succeeded` : `${csproj}: build failed`,
      output: result.output,
    });
  } else if (type === "sdk") {
    const csproj = await findCsprojFor(`sdks/${name}`);
    if (csproj) {
      const result = await runBuild(csproj);
      findings.push({
        kind: result.success ? "pass" : "fail",
        label: "build",
        message: result.success ? `${csproj}: build succeeded` : `${csproj}: build failed`,
        output: result.output,
      });
    } else {
      findings.push({
        kind: "info",
        label: "build",
        message: `sdks/${name}: no .csproj found — build validation skipped`,
      });
    }
  } else if (type === "structural-tests") {
    const result = await runBuild("structural-tests/structural-tests.csproj");
    findings.push({
      kind: result.success ? "pass" : "fail",
      label: "build",
      message: result.success ? "structural-tests: build succeeded" : "structural-tests: build failed",
      output: result.output,
    });
  } else if (type === "test-utilities") {
    const result = await runBuild("test-utilities/test-utilities.csproj");
    findings.push({
      kind: result.success ? "pass" : "fail",
      label: "build",
      message: result.success ? "test-utilities: build succeeded" : "test-utilities: build failed",
      output: result.output,
    });
  } else if (type === "scripts") {
    const scriptResults = await validateScriptSyntax(modifiedFiles);
    for (const f of scriptResults) findings.push(f);
  } else if (type === "claude") {
    const claudeResults = await validateClaudeFiles(modifiedFiles);
    for (const f of claudeResults) findings.push(f);
  } else if (type === "tenets") {
    // `tenets` scope: run the full tenet-system validation. Release is
    // BLOCKED when errors > 0 — the maintainer must fix inconsistencies
    // (dangling citations, T0/nav/summary drift, sentinel imbalances)
    // before the scope can be released.
    try {
      const result = await validateTenets();
      const errorCount = result.errors.length;
      const warningCount = result.warnings.length;
      const s = result.stats;

      if (errorCount > 0) {
        findings.push({
          kind: "fail",
          label: "validate-tenets",
          message:
            `${errorCount} tenet validation error(s) — fix before releasing scope ` +
            `(${s.tenetCount} tenets, ${s.violationCount} violations, ${warningCount} warning(s))`,
          hits: [
            ...result.errors.map((e) => `✗ ${e}`),
            ...result.warnings.map((w) => `⚠ ${w}`),
          ],
        });
      } else {
        findings.push({
          kind: "pass",
          label: "validate-tenets",
          message:
            `tenets consistent: ${s.tenetCount} tenets, ${s.violationCount} violation rows, ` +
            `${s.t0ProseLabels} T0 prose bullet(s), ${s.navRows} nav row(s), ` +
            `${s.summarySections} summary section(s)` +
            (warningCount > 0 ? `, ${warningCount} warning(s)` : ""),
          hits: result.warnings.map((w) => `⚠ ${w}`),
        });
      }
    } catch (err) {
      findings.push({
        kind: "fail",
        label: "validate-tenets",
        message: `validate_tenets threw: ${err.message}`,
      });
    }
  }

  return findings;
}

async function getModifiedFilesInScope(scopeData) {
  try {
    const { stdout } = await execAsync("git diff --name-only HEAD", {
      cwd: projectDir(),
    });
    const modified = stdout.split("\n").filter(Boolean);
    const inScope = [];
    for (const relFile of modified) {
      const absFile = resolveProjectPath(relFile);
      if (matchesPathSet(absFile, scopeData.pathSet)) {
        inScope.push(relFile);
      }
    }
    return inScope;
  } catch {
    return [];
  }
}

// Matches "null!", "default!", "identifier!.", or "somecall()!" / "...!;"
const NULL_FORGIVING_REGEX = /(?:\bnull!|\bdefault!|\w+!\.|\)\s*!(?!=))/;

async function scanDiffForForbiddenPatterns(modifiedFiles) {
  const findings = [];
  for (const relFile of modifiedFiles) {
    if (!relFile.endsWith(".cs")) continue;
    if (relFile.includes("/Generated/")) continue;

    try {
      const { stdout: diffOutput } = await execAsync(
        `git diff HEAD -- ${JSON.stringify(relFile)}`,
        { cwd: projectDir(), maxBuffer: 5 * 1024 * 1024 },
      );

      const lines = diffOutput.split("\n");
      const hits = [];
      for (const line of lines) {
        if (!line.startsWith("+") || line.startsWith("+++")) continue;
        const content = line.slice(1);
        if (NULL_FORGIVING_REGEX.test(content)) {
          hits.push(content.trim().slice(0, 200));
        }
      }
      if (hits.length > 0) {
        findings.push({
          kind: "fail",
          label: "null-forgiving-operator",
          message: `${relFile}: ${hits.length} null-forgiving operator(s) added`,
          hits,
        });
      }
    } catch {
      // per-file diff failure — ignore
    }
  }
  return findings;
}

async function runBuild(csproj) {
  try {
    const { stdout, stderr } = await execAsync(
      `dotnet build ${JSON.stringify(csproj)} --no-restore`,
      { cwd: projectDir(), maxBuffer: 10 * 1024 * 1024, timeout: 240000 },
    );
    const output = (stdout || "") + (stderr ? `\n${stderr}` : "");
    const success = !output.includes("Build FAILED");
    return { success, output: output.slice(-2000) };
  } catch (err) {
    const output = (err.stdout || "") + (err.stderr || "") + (err.message || "");
    return { success: false, output: output.slice(-2000) };
  }
}

async function findCsprojFor(relDir) {
  try {
    const absDir = resolveProjectPath(relDir);
    const entries = await readdir(absDir, { withFileTypes: true });
    for (const entry of entries) {
      if (entry.isFile() && entry.name.endsWith(".csproj")) {
        return join(relDir, entry.name);
      }
    }
    return null;
  } catch {
    return null;
  }
}

async function validateScriptSyntax(modifiedFiles) {
  const findings = [];
  for (const relFile of modifiedFiles) {
    const absFile = resolveProjectPath(relFile);
    let cmd = null;
    if (relFile.endsWith(".sh")) cmd = `bash -n ${JSON.stringify(absFile)}`;
    else if (relFile.endsWith(".py")) cmd = `python3 -m py_compile ${JSON.stringify(absFile)}`;
    else if (relFile.endsWith(".mjs") || relFile.endsWith(".js")) {
      cmd = `node --check ${JSON.stringify(absFile)}`;
    }
    if (!cmd) continue;

    try {
      await execAsync(cmd, { cwd: projectDir(), timeout: 10000 });
      findings.push({ kind: "pass", label: "syntax", message: `${relFile}: syntax OK` });
    } catch (err) {
      findings.push({
        kind: "fail",
        label: "syntax",
        message: `${relFile}: syntax error`,
        output: ((err.stderr || "") + (err.stdout || "") + (err.message || "")).slice(0, 500),
      });
    }
  }
  return findings;
}

async function validateClaudeFiles(modifiedFiles) {
  const findings = [];
  for (const relFile of modifiedFiles) {
    const absFile = resolveProjectPath(relFile);
    if (relFile.endsWith(".sh")) {
      try {
        await execAsync(`bash -n ${JSON.stringify(absFile)}`, {
          cwd: projectDir(),
          timeout: 10000,
        });
        findings.push({ kind: "pass", label: "bash-syntax", message: `${relFile}: syntax OK` });
      } catch (err) {
        findings.push({
          kind: "fail",
          label: "bash-syntax",
          message: `${relFile}: syntax error`,
          output: ((err.stderr || "") + (err.message || "")).slice(0, 500),
        });
      }
    } else if (relFile.endsWith(".mjs") || relFile.endsWith(".js")) {
      try {
        await execAsync(`node --check ${JSON.stringify(absFile)}`, {
          cwd: projectDir(),
          timeout: 10000,
        });
        findings.push({ kind: "pass", label: "node-syntax", message: `${relFile}: syntax OK` });
      } catch (err) {
        findings.push({
          kind: "fail",
          label: "node-syntax",
          message: `${relFile}: syntax error`,
          output: ((err.stderr || "") + (err.message || "")).slice(0, 500),
        });
      }
    } else if (relFile === ".claude/settings.json") {
      try {
        const content = await readFile(absFile, "utf-8");
        JSON.parse(content);
        findings.push({ kind: "pass", label: "json-syntax", message: `${relFile}: JSON valid` });
      } catch (err) {
        findings.push({
          kind: "fail",
          label: "json-syntax",
          message: `${relFile}: JSON error`,
          output: err.message,
        });
      }
    }
  }
  return findings;
}
