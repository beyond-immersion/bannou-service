/**
 * Schema catalog and retrieval helpers.
 *
 * - getSchemaCatalog(): Scan schemas/ directory, return indexed list by service.
 * - getSchema(name): Read a specific schema file.
 */

import { readFile, readdir } from "node:fs/promises";
import { resolve, join, relative } from "node:path";

// ─── Helpers ──────────────────────────────────────────────────────────────

function getProjectDir() {
  return process.env.CLAUDE_PROJECT_DIR || process.cwd();
}

// ─── Schema Categories ────────────────────────────────────────────────────

/**
 * Classify a schema filename into its category and extract the service name.
 */
function classifySchema(filename) {
  // Generated lifecycle events
  if (filename.endsWith("-service-lifecycle-events.yaml")) {
    const service = filename.replace("-service-lifecycle-events.yaml", "");
    return { service, category: "lifecycle-events", generated: true };
  }
  // Service events
  if (filename.endsWith("-service-events.yaml")) {
    const service = filename.replace("-service-events.yaml", "");
    return { service, category: "service-events", generated: false };
  }
  // Client events
  if (filename.endsWith("-client-events.yaml")) {
    const service = filename.replace("-client-events.yaml", "");
    return { service, category: "client-events", generated: false };
  }
  // Configuration
  if (filename.endsWith("-configuration.yaml")) {
    const service = filename.replace("-configuration.yaml", "");
    return { service, category: "configuration", generated: false };
  }
  // API
  if (filename.endsWith("-api.yaml")) {
    const service = filename.replace("-api.yaml", "");
    return { service, category: "api", generated: false };
  }
  // Common/shared schemas
  if (filename.startsWith("common-")) {
    return { service: "common", category: filename.replace(".yaml", ""), generated: false };
  }
  // Shared infrastructure schemas
  if (filename === "state-stores.yaml") {
    return { service: "shared", category: "state-stores", generated: false };
  }
  if (filename === "telemetry-metrics.yaml") {
    return { service: "shared", category: "telemetry-metrics", generated: false };
  }
  if (filename === "variable-providers.yaml") {
    return { service: "shared", category: "variable-providers", generated: false };
  }
  if (filename === "localization-categories.yaml") {
    return { service: "shared", category: "localization-categories", generated: false };
  }
  if (filename === "archetype-definitions.yaml") {
    return { service: "shared", category: "archetype-definitions", generated: false };
  }

  return { service: "unknown", category: "other", generated: false };
}

// ─── Schema Catalog ───────────────────────────────────────────────────────

/**
 * List all schema files organized by service.
 *
 * Returns {
 *   services: { [service]: { api, events, config, clientEvents, lifecycleEvents } },
 *   shared: [...],
 *   totalFiles: N
 * } or { error: string }.
 */
export async function getSchemaCatalog() {
  const projectDir = getProjectDir();
  const schemasDir = resolve(projectDir, "schemas");
  const generatedDir = resolve(projectDir, "schemas/Generated");

  try {
    const services = {};
    const shared = [];
    let totalFiles = 0;

    // Scan main schemas directory
    const mainFiles = await readdir(schemasDir);
    for (const file of mainFiles.filter((f) => f.endsWith(".yaml"))) {
      totalFiles++;
      const { service, category } = classifySchema(file);

      if (service === "common" || service === "shared") {
        shared.push({ file, category, path: `schemas/${file}` });
        continue;
      }

      if (!services[service]) {
        services[service] = {};
      }
      services[service][category] = `schemas/${file}`;
    }

    // Scan Generated subdirectory
    try {
      const genFiles = await readdir(generatedDir);
      for (const file of genFiles.filter((f) => f.endsWith(".yaml"))) {
        totalFiles++;
        const { service, category } = classifySchema(file);

        if (!services[service]) {
          services[service] = {};
        }
        services[service][category] = `schemas/Generated/${file}`;
      }
    } catch { /* Generated dir may not exist */ }

    return {
      services,
      shared,
      totalFiles,
      serviceCount: Object.keys(services).length,
    };
  } catch (err) {
    return { error: `Failed to scan schemas directory: ${err.message}` };
  }
}

// ─── Schema Retrieval ─────────────────────────────────────────────────────

/**
 * Get a specific schema file by name.
 *
 * Accepts:
 *   - Full filename: "account-api.yaml"
 *   - Service name (returns all schemas): "account"
 *   - Relative path: "schemas/account-api.yaml"
 *
 * Returns { files: [{ path, content }] } or { error: string }.
 */
export async function getSchema(name) {
  if (!name || typeof name !== "string") {
    return { error: "Schema name is required." };
  }

  const projectDir = getProjectDir();
  const schemasDir = resolve(projectDir, "schemas");
  const cleanName = name.replace(/^schemas\//, "").toLowerCase();

  const results = [];

  // If it looks like a specific file
  if (cleanName.endsWith(".yaml")) {
    // Try main dir first, then Generated
    for (const dir of [schemasDir, resolve(schemasDir, "Generated")]) {
      const filePath = resolve(dir, cleanName);
      try {
        const content = await readFile(filePath, "utf-8");
        const relPath = relative(projectDir, filePath);
        results.push({ path: relPath, content });
      } catch { /* not found in this dir */ }
    }
  } else {
    // Treat as service name — find all schemas for this service
    const patterns = [
      `${cleanName}-api.yaml`,
      `${cleanName}-service-events.yaml`,
      `${cleanName}-configuration.yaml`,
      `${cleanName}-client-events.yaml`,
    ];
    const generatedPatterns = [
      `${cleanName}-service-lifecycle-events.yaml`,
    ];

    for (const pattern of patterns) {
      const filePath = resolve(schemasDir, pattern);
      try {
        const content = await readFile(filePath, "utf-8");
        results.push({ path: `schemas/${pattern}`, content });
      } catch { /* file doesn't exist for this service */ }
    }

    for (const pattern of generatedPatterns) {
      const filePath = resolve(schemasDir, "Generated", pattern);
      try {
        const content = await readFile(filePath, "utf-8");
        results.push({ path: `schemas/Generated/${pattern}`, content });
      } catch { /* file doesn't exist */ }
    }
  }

  if (results.length === 0) {
    // List available services
    try {
      const files = await readdir(schemasDir);
      const services = new Set();
      for (const f of files) {
        if (f.endsWith("-api.yaml")) {
          services.add(f.replace("-api.yaml", ""));
        }
      }
      return {
        error: `No schemas found for "${name}". Available services: ${[...services].sort().join(", ")}`,
      };
    } catch {
      return { error: `No schemas found for "${name}".` };
    }
  }

  return { files: results };
}
