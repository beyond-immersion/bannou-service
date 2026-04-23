/**
 * Infrastructure introspection helpers.
 *
 * - getServiceDetails(layer?): Read generated service details by layer.
 * - getEventCatalog(): Read generated events reference.
 * - getStateStoreCatalog(): Read generated state stores reference.
 * - getConfigurationCatalog(): Read generated configuration reference.
 * - printModelShapes(plugin): Execute print-model-shapes.py internally.
 * - printInterfaceShapes(name?): Execute print-interface-shapes.py internally.
 *
 * print_models and print_interfaces delegate to existing Python scripts via
 * child_process.exec. This keeps the scripts as the single source of truth
 * (human operators use them via `make`; MCP tools use them internally).
 */

import { readFile, readdir } from "node:fs/promises";
import { resolve, join, relative } from "node:path";
import { exec } from "node:child_process";
import { promisify } from "node:util";

const execAsync = promisify(exec);

// ─── Helpers ──────────────────────────────────────────────────────────────

function getProjectDir() {
  return process.env.CLAUDE_PROJECT_DIR || process.cwd();
}

/**
 * Execute a command internally (not through the agent-facing whitelist).
 * Used for server-internal script execution only.
 */
async function execInternal(command, options = {}) {
  const cwd = options.cwd || getProjectDir();
  const timeout = options.timeout || 30000;
  return execAsync(command, { cwd, timeout, maxBuffer: 5 * 1024 * 1024 });
}

// ─── Generated Reference File Reader ──────────────────────────────────────

/**
 * Read a generated reference file and return its content.
 */
async function readGeneratedFile(filename) {
  const projectDir = getProjectDir();
  const filePath = resolve(projectDir, "docs/generated", filename);
  try {
    const content = await readFile(filePath, "utf-8");
    return { content, path: `docs/generated/${filename}` };
  } catch (err) {
    if (err.code === "ENOENT") {
      return { error: `File not found: docs/generated/${filename}. Run 'make generate-docs' to create it.` };
    }
    return { error: `Failed to read ${filename}: ${err.message}` };
  }
}

// ─── Service Details ──────────────────────────────────────────────────────

/** Maps layer names to their generated detail files. */
const LAYER_FILES = {
  infrastructure: "GENERATED-INFRASTRUCTURE-SERVICE-DETAILS.md",
  "app-foundation": "GENERATED-APP-FOUNDATION-SERVICE-DETAILS.md",
  "game-foundation": "GENERATED-GAME-FOUNDATION-SERVICE-DETAILS.md",
  "app-features": "GENERATED-APP-FEATURES-SERVICE-DETAILS.md",
  "game-features": "GENERATED-GAME-FEATURES-SERVICE-DETAILS.md",
};

/**
 * Get service details, optionally filtered by layer.
 *
 * If layer is provided, returns details for that layer only.
 * If omitted, returns the combined service details file.
 *
 * Returns { content, path, layer? } or { error: string }.
 */
export async function getServiceDetails(layer) {
  if (layer) {
    const normalized = layer.toLowerCase().replace(/\s+/g, "-").replace(/^l\d-?/, "");
    const file = LAYER_FILES[normalized];
    if (!file) {
      const available = Object.keys(LAYER_FILES).join(", ");
      return { error: `Unknown layer "${layer}". Available: ${available}` };
    }
    const result = await readGeneratedFile(file);
    if (result.content) result.layer = normalized;
    return result;
  }

  // Return combined details
  return readGeneratedFile("GENERATED-SERVICE-DETAILS.md");
}

// ─── Event Catalog ────────────────────────────────────────────────────────

/**
 * Get the generated events reference.
 *
 * Returns { content, path } or { error: string }.
 */
export async function getEventCatalog() {
  return readGeneratedFile("GENERATED-EVENTS.md");
}

// ─── State Store Catalog ──────────────────────────────────────────────────

/**
 * Get the generated state stores reference.
 *
 * Returns { content, path } or { error: string }.
 */
export async function getStateStoreCatalog() {
  return readGeneratedFile("GENERATED-STATE-STORES.md");
}

// ─── Configuration Catalog ────────────────────────────────────────────────

/**
 * Get the generated configuration reference.
 *
 * Returns { content, path } or { error: string }.
 */
export async function getConfigurationCatalog() {
  return readGeneratedFile("GENERATED-CONFIGURATION.md");
}

// ─── Model Shape Printing ─────────────────────────────────────────────────

/**
 * Print compact model shapes for a service plugin.
 *
 * Delegates to scripts/print-model-shapes.py internally.
 *
 * Returns { plugin, output } or { error: string }.
 */
export async function printModelShapes(plugin) {
  if (!plugin || typeof plugin !== "string") {
    return { error: "Plugin name is required." };
  }

  const projectDir = getProjectDir();
  const sanitized = plugin.toLowerCase().replace(/[^a-z0-9-]/g, "");

  try {
    const { stdout, stderr } = await execInternal(
      `python3 scripts/print-model-shapes.py "${sanitized}"`,
      { cwd: projectDir },
    );

    // The script writes summary stats to stderr
    const stats = stderr ? stderr.trim() : "";
    const output = stdout || "";

    if (!output && stats.startsWith("ERROR")) {
      return { error: stats };
    }

    return {
      plugin: sanitized,
      output: output.trimEnd(),
      stats: stats || undefined,
    };
  } catch (err) {
    const stderr = err.stderr || "";
    if (stderr.includes("No schemas found")) {
      return { error: stderr.trim() };
    }
    return { error: `Failed to print model shapes: ${err.message}\n${stderr}` };
  }
}

// ─── Interface Shape Printing ─────────────────────────────────────────────

/**
 * Print interface shapes from bannou-service.
 *
 * Delegates to scripts/print-interface-shapes.py internally.
 *
 * - No argument: catalog mode (all interfaces by category)
 * - With name: detail mode (full signatures for matching interface)
 *
 * Returns { mode, output } or { error: string }.
 */
export async function printInterfaceShapes(name) {
  const projectDir = getProjectDir();

  try {
    let command = "python3 scripts/print-interface-shapes.py";
    let mode = "catalog";

    if (name && typeof name === "string" && name.trim()) {
      const sanitized = name.trim().replace(/[^a-zA-Z0-9<>_]/g, "");
      command += ` "${sanitized}"`;
      mode = "detail";
    }

    const { stdout, stderr } = await execInternal(command, { cwd: projectDir });

    const stats = stderr ? stderr.trim() : "";
    const output = stdout || "";

    if (!output && stats.startsWith("ERROR")) {
      return { error: stats };
    }

    return {
      mode,
      name: mode === "detail" ? name : undefined,
      output: output.trimEnd(),
      stats: stats || undefined,
    };
  } catch (err) {
    const stderr = err.stderr || "";
    if (stderr.includes("No interface matching")) {
      return { error: stderr.trim() };
    }
    return { error: `Failed to print interface shapes: ${err.message}\n${stderr}` };
  }
}
