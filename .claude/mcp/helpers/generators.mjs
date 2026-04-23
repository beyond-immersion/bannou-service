/**
 * Code generation catalog and execution.
 *
 * Replaces manual `run_command` invocations for code generation scripts.
 * Catalog mode lists all generators with docs and ordering rules.
 * Execute mode runs a specific generator with proper cwd handling.
 */

import { exec } from "node:child_process";
import { promisify } from "node:util";
import { resolve } from "node:path";

const execAsync = promisify(exec);

// ─── Generator Catalog ──────────────────────────────────────────────────

/**
 * Each generator entry defines:
 *   command:       Shell command to execute (relative to repo root)
 *   requiresService: Whether the generator needs a service name
 *   description:   What this generator does
 *   trigger:       What schema change triggers the need for this generator
 *   cwd:           "scripts" if command must run from scripts/, "root" otherwise
 */
const GENERATORS = {
  // ── Per-Service Generators ──────────────────────────────────────────

  models: {
    command: (svc) => `./generate-models.sh ${svc}`,
    requiresService: true,
    description: "Request/response/event models from API schema",
    trigger: "Changed {service}-api.yaml (model definitions)",
    cwd: "scripts",
  },

  config: {
    command: (svc) => `./generate-config.sh ${svc}`,
    requiresService: true,
    description: "Configuration class from configuration schema",
    trigger: "Changed {service}-configuration.yaml",
    cwd: "scripts",
  },

  events: {
    command: (svc) => `scripts/generate-service-events.sh ${svc}`,
    requiresService: true,
    description: "Events + lifecycle events from events schema",
    trigger: "Changed {service}-service-events.yaml (events, x-lifecycle, or both)",
    cwd: "root",
  },

  "client-events": {
    command: (svc) => `scripts/generate-client-events.sh ${svc}`,
    requiresService: true,
    description: "Client events (server→client WebSocket push)",
    trigger: "Changed {service}-client-events.yaml",
    cwd: "root",
  },

  service: {
    command: (svc) => `./generate-service.sh ${svc}`,
    requiresService: true,
    description: "All generated code for one service (models + config + events + controller + client)",
    trigger: "Changed multiple schema types for one service",
    cwd: "scripts",
  },

  // ── Global Generators ───────────────────────────────────────────────

  "state-stores": {
    command: () => `python3 scripts/generate-state-stores.py`,
    requiresService: false,
    description: "State store constants and documentation",
    trigger: "Changed schemas/state-stores.yaml",
    cwd: "root",
  },

  "telemetry-metrics": {
    command: () => `python3 scripts/generate-telemetry-metrics.py`,
    requiresService: false,
    description: "Telemetry metric constants and documentation",
    trigger: "Changed schemas/telemetry-metrics.yaml",
    cwd: "root",
  },

  "published-topics": {
    command: () => `python3 scripts/generate-published-topics.py`,
    requiresService: false,
    description: "Published event topic string constants from x-event-publications",
    trigger: "Changed x-event-publications in any events schema",
    cwd: "root",
  },

  "event-publishers": {
    command: () => `python3 scripts/generate-event-publishers.py`,
    requiresService: false,
    description: "Typed Publish*Async extension methods from x-event-publications",
    trigger: "Changed x-event-publications in any events schema",
    cwd: "root",
  },

  docs: {
    command: () => `scripts/generate-docs.sh`,
    requiresService: false,
    description: "All generated documentation (state stores, events, config, service details, catalogs)",
    trigger: "After any schema changes, or to refresh generated docs",
    cwd: "root",
  },

  all: {
    command: () => `scripts/generate-all-services.sh`,
    requiresService: false,
    description: "FULL regeneration of ALL services and artifacts",
    trigger: "Changed common-*.yaml, or broad changes affecting multiple services",
    cwd: "root",
  },
};

// ─── Catalog Formatting ─────────────────────────────────────────────────

export function getGeneratorCatalog() {
  const lines = [
    "══════════════════════════════════════════════════════════════════════════",
    " Bannou Code Generation Commands",
    "══════════════════════════════════════════════════════════════════════════",
    "",
    "⚠️  ALWAYS use the MOST GRANULAR command possible.",
    "   Each per-service generator runs in ~30 seconds.",
    '   "all" processes 60+ services and takes ~10 MINUTES — use it ONLY',
    "   when common-*.yaml changed or changes span multiple services.",
    "",
    "══════════════════════════════════════════════════════════════════════════",
    "",
    "PER-SERVICE GENERATORS  (require service name)",
    "──────────────────────────────────────────────────────────────────────────",
    "",
  ];

  // Per-service generators
  for (const name of ["models", "config", "events", "client-events", "service"]) {
    const gen = GENERATORS[name];
    lines.push(`  ${name.padEnd(20)} ${gen.description}`);
    lines.push(`  ${"".padEnd(20)} Trigger: ${gen.trigger}`);
    lines.push(`  ${"".padEnd(20)} Usage: generate(script: "${name}", service: "{name}")`);
    lines.push("");
  }

  lines.push(
    "GLOBAL GENERATORS  (no service name needed)",
    "──────────────────────────────────────────────────────────────────────────",
    "",
  );

  // Global generators (excluding "all")
  for (const name of [
    "state-stores", "telemetry-metrics", "published-topics", "event-publishers", "docs",
  ]) {
    const gen = GENERATORS[name];
    lines.push(`  ${name.padEnd(20)} ${gen.description}`);
    lines.push(`  ${"".padEnd(20)} Trigger: ${gen.trigger}`);
    lines.push(`  ${"".padEnd(20)} Usage: generate(script: "${name}")`);
    lines.push("");
  }

  lines.push(
    "FULL REGENERATION  ⚠️  ~10 MINUTES — use only when necessary",
    "──────────────────────────────────────────────────────────────────────────",
    "",
    `  ${"all".padEnd(20)} ${GENERATORS.all.description}`,
    `  ${"".padEnd(20)} Trigger: ${GENERATORS.all.trigger}`,
    `  ${"".padEnd(20)} Usage: generate(script: "all")`,
    "",
    "ORDERING RULES",
    "──────────────────────────────────────────────────────────────────────────",
    "",
    "  • Changed api.yaml + events.yaml for same service:",
    '    Run models FIRST, then events. Events $ref types from api.yaml.',
    "",
    "  • Changed x-event-publications:",
    "    Run BOTH published-topics AND event-publishers (they read the same",
    "    schema declarations but generate different output files).",
    "",
    "  • Changed common-*.yaml:",
    '    Use "all" — it handles ordering automatically.',
    "",
  );

  return lines.join("\n");
}

// ─── Generator Execution ────────────────────────────────────────────────

/**
 * Run a specific generator.
 *
 * @param {string} script - Generator name from the catalog.
 * @param {string} [service] - Service name (required for per-service generators).
 * @param {number} [timeoutMs] - Timeout in ms (default: 120s, "all" gets 720s).
 * @returns {{ output: string } | { error: string }}
 */
export async function runGenerator(script, service, timeoutMs) {
  const gen = GENERATORS[script];
  if (!gen) {
    const available = Object.keys(GENERATORS).join(", ");
    return { error: `Unknown generator "${script}". Available: ${available}` };
  }

  if (gen.requiresService && !service) {
    return { error: `Generator "${script}" requires a service name. Usage: generate(script: "${script}", service: "{name}")` };
  }

  if (!gen.requiresService && service) {
    return { error: `Generator "${script}" is a global generator and does not accept a service name.` };
  }

  const projectDir = process.env.CLAUDE_PROJECT_DIR || process.cwd();
  const cwd = gen.cwd === "scripts" ? resolve(projectDir, "scripts") : projectDir;
  const command = gen.command(service);

  // "all" gets 12 minutes, everything else gets 2 minutes (or caller override)
  const timeout = timeoutMs || (script === "all" ? 720000 : 120000);

  try {
    const { stdout, stderr } = await execAsync(command, {
      cwd,
      timeout,
      maxBuffer: 10 * 1024 * 1024,
      shell: "/bin/bash",
    });

    const output = (stdout || "") + (stderr ? `\n--- stderr ---\n${stderr}` : "");
    return { output: output || "(completed with no output)" };
  } catch (err) {
    const message = err.stderr || err.stdout || err.message || "Unknown error";
    return { error: `Generator "${script}" failed:\n\n${message}` };
  }
}
