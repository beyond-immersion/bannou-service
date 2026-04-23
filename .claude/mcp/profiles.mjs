/**
 * Context profile definitions for prepare_context.
 *
 * Isolated in its own file for easy updates. Each profile defines a set
 * of files to load into agent context. Profiles can extend other profiles
 * and include dynamic files resolved from options.
 *
 * ADDING A NEW PROFILE:
 *   1. Add an entry to CONTEXT_PROFILES below
 *   2. Static files: list paths relative to project root
 *   3. Dynamic files: provide a function that takes options and returns paths
 *   4. Inheritance: set `extends` to include another profile's files
 */

import { readdirSync } from "node:fs";
import { join, resolve, extname, relative } from "node:path";

// ─── Directory Scanning Helper ──────────────────────────────────────────

function getProjectDir() {
  return process.env.CLAUDE_PROJECT_DIR || process.cwd();
}

/**
 * Recursively collect files from a directory with optional extension filter.
 * Returns paths relative to project root. Sync I/O is acceptable here —
 * this runs once per prepare_context call, scanning small config directories.
 *
 * @param {string} relDir - Directory relative to project root
 * @param {Object} [opts]
 * @param {string[]} [opts.extensions] - Include only these extensions (e.g., [".mjs"])
 * @param {string[]} [opts.excludeDirs=["node_modules"]] - Directory names to skip
 */
function collectFiles(relDir, { extensions, excludeDirs = ["node_modules"] } = {}) {
  const projectDir = getProjectDir();
  const absDir = resolve(projectDir, relDir);
  const results = [];

  function scan(dir) {
    try {
      const entries = readdirSync(dir, { withFileTypes: true });
      for (const entry of entries) {
        if (excludeDirs.includes(entry.name)) continue;
        const fullPath = join(dir, entry.name);
        if (entry.isDirectory()) {
          scan(fullPath);
        } else if (entry.isFile()) {
          if (!extensions || extensions.includes(extname(entry.name).toLowerCase())) {
            results.push(relative(projectDir, fullPath));
          }
        }
      }
    } catch { /* directory may not exist */ }
  }

  scan(absDir);
  return results.sort();
}

// ─── Profile Definitions ────────────────────────────────────────────────

export const CONTEXT_PROFILES = {
  dev: {
    description: "Core development context — project instructions + canonical patterns (tenet bodies are queried on demand via list_tenets / get_tenet / list_violations / search_tenets)",
    files: [
      "CLAUDE.md",
      "CLAUDE-PRACTICES.md",
      "docs/reference/HELPERS-AND-COMMON-PATTERNS.md",
    ],
  },

  "tenets-full": {
    description: "Full tenet bundle — all five category files at once. Stack on top of `dev` (or any other profile) when a task requires reading entire tenet bodies rather than querying them via MCP tools. Typical use: deep tenet audits, policy drafting, cross-tenet consistency reviews.",
    files: [
      "docs/reference/tenets/FOUNDATION.md",
      "docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md",
      "docs/reference/tenets/IMPLEMENTATION-DATA.md",
      "docs/reference/tenets/QUALITY.md",
      "docs/reference/tenets/TESTING-PATTERNS.md",
    ],
  },

  plugin: {
    description: "Plugin-specific context — dev profile + deep dive + implementation map",
    extends: "dev",
    requiresOption: "service",
    dynamicFiles: (options) => [
      `docs/plugins/${options.service.toUpperCase()}.md`,
      `docs/maps/${options.service.toUpperCase()}.md`,
    ],
  },

  sdk: {
    description: "SDK-specific context — dev profile + SDK deep dive + SDK implementation map",
    extends: "dev",
    requiresOption: "sdk",
    dynamicFiles: (options) => [
      `docs/sdks/${options.sdk.toUpperCase()}.md`,
      `docs/sdks/maps/${options.sdk.toUpperCase()}.md`,
    ],
  },

  schema: {
    description: "Schema work context — dev profile + schema rules + specifications catalog",
    extends: "dev",
    files: [
      "docs/reference/SCHEMA-RULES.md",
      "docs/generated/GENERATED-SPECIFICATIONS-CATALOG.md",
    ],
  },

  mcp: {
    description: "MCP server context — all .mjs source files in .claude/mcp/ (excludes node_modules)",
    dynamicFiles: () => collectFiles(".claude/mcp", { extensions: [".mjs"] }),
  },

  claude: {
    description: "Claude Code configuration — agents, skills, hooks, personalities, and rules",
    dynamicFiles: () => [
      ...collectFiles(".claude/agents"),
      ...collectFiles(".claude/skills"),
      ...collectFiles(".claude/hooks"),
      ...collectFiles(".claude/personalities"),
      ...collectFiles(".claude/rules"),
    ],
  },
};
