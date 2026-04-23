/**
 * Plugin catalog and documentation helpers.
 *
 * Provides plugin listing (from composition reference + directory scan)
 * and plugin doc retrieval (deep dive + implementation map).
 */

import { readFile, readdir, stat } from "node:fs/promises";
import { resolve, join } from "node:path";

// ─── Helpers ──────────────────────────────────────────────────────────────

function getProjectDir() {
  return process.env.CLAUDE_PROJECT_DIR || process.cwd();
}

// ─── Plugin Catalog ───────────────────────────────────────────────────────

/**
 * List all plugins with layer, endpoint count, and documentation availability.
 *
 * Parses the Service Registry table from GENERATED-COMPOSITION-REFERENCE.md
 * and cross-references with docs/plugins/ and docs/maps/ directories.
 *
 * Returns { plugins: [...], totalEndpoints: N } or { error: string }.
 */
export async function getPluginCatalog() {
  const projectDir = getProjectDir();

  try {
    // Parse composition reference for the service registry table
    const compRefPath = resolve(projectDir, "docs/generated/GENERATED-COMPOSITION-REFERENCE.md");
    const compRef = await readFile(compRefPath, "utf-8");

    // Find the Service Registry table — starts with "| Service | Layer |"
    const lines = compRef.split("\n");
    const tableStart = lines.findIndex((l) => l.startsWith("| Service | Layer |"));
    if (tableStart < 0) {
      return { error: "Could not find Service Registry table in composition reference." };
    }

    const plugins = [];
    // Skip header and separator rows
    for (let i = tableStart + 2; i < lines.length; i++) {
      const line = lines[i].trim();
      if (!line.startsWith("|")) break;

      const cells = line.split("|").map((c) => c.trim()).filter(Boolean);
      if (cells.length < 4) continue;

      const [service, layer, role, epStr] = cells;
      const endpoints = parseInt(epStr, 10) || 0;
      const serviceLower = service.toLowerCase().replace(/\s+/g, "-");

      plugins.push({
        name: service,
        slug: serviceLower,
        layer,
        role: role.substring(0, 120), // Truncate long descriptions
        endpoints,
        hasDeepDive: false,
        hasMap: false,
      });
    }

    // Check doc availability
    const pluginDocsDir = resolve(projectDir, "docs/plugins");
    const mapsDir = resolve(projectDir, "docs/maps");

    let deepDiveFiles = new Set();
    let mapFiles = new Set();

    try {
      const entries = await readdir(pluginDocsDir);
      deepDiveFiles = new Set(entries.map((e) => e.toUpperCase().replace(/\.MD$/, "")));
    } catch { /* directory may not exist */ }

    try {
      const entries = await readdir(mapsDir);
      mapFiles = new Set(entries.map((e) => e.toUpperCase().replace(/\.MD$/, "")));
    } catch { /* directory may not exist */ }

    for (const p of plugins) {
      const upper = p.slug.toUpperCase();
      p.hasDeepDive = deepDiveFiles.has(upper);
      p.hasMap = mapFiles.has(upper);
    }

    const totalEndpoints = plugins.reduce((sum, p) => sum + p.endpoints, 0);

    return { plugins, totalEndpoints };
  } catch (err) {
    return { error: `Failed to load plugin catalog: ${err.message}` };
  }
}

// ─── Plugin Docs ──────────────────────────────────────────────────────────

/**
 * Get deep dive and implementation map for a specific plugin.
 *
 * Returns { deepDive: string|null, map: string|null, name: string }
 * or { error: string }.
 */
export async function getPluginDocs(name) {
  if (!name || typeof name !== "string") {
    return { error: "Plugin name is required." };
  }

  const projectDir = getProjectDir();
  const upper = name.toUpperCase();

  const deepDivePath = resolve(projectDir, `docs/plugins/${upper}.md`);
  const mapPath = resolve(projectDir, `docs/maps/${upper}.md`);

  let deepDive = null;
  let map = null;

  try {
    deepDive = await readFile(deepDivePath, "utf-8");
  } catch { /* file may not exist */ }

  try {
    map = await readFile(mapPath, "utf-8");
  } catch { /* file may not exist */ }

  if (!deepDive && !map) {
    // Try to suggest correct name
    try {
      const pluginDocsDir = resolve(projectDir, "docs/plugins");
      const entries = await readdir(pluginDocsDir);
      const available = entries
        .filter((e) => e.endsWith(".md"))
        .map((e) => e.replace(/\.md$/i, "").toLowerCase())
        .sort();
      return {
        error: `No documentation found for plugin "${name}". Available: ${available.join(", ")}`,
      };
    } catch {
      return { error: `No documentation found for plugin "${name}".` };
    }
  }

  return {
    name: upper,
    deepDive,
    deepDivePath: deepDive ? deepDivePath : null,
    map,
    mapPath: map ? mapPath : null,
  };
}
