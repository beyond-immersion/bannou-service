/**
 * Shared mutable state for the Bannou MCP server.
 *
 * ES modules are singletons — every file that imports this module gets the
 * same object references. This is how read-tracking state is shared between
 * the tool registrations in server.mjs and the helper functions in helpers/.
 */

// ─── Read Tracking ─────────────────────────────────────────────────────────

/**
 * Tracks which file paths have been read via read_file in this session.
 * edit_file checks this set before allowing modifications — mirroring
 * the built-in Edit tool's requirement that Read be called first, but
 * in our own independent permission space.
 */
export const readFiles = new Set();

/**
 * Tracks files that were split during reading and still have unread
 * continuation parts. edit_file blocks modifications to a file until
 * ALL continuation parts have been read.
 *
 * Key: original file path → Value: Set of continuation paths not yet read
 */
export const pendingContinuations = new Map();

/**
 * Reverse mapping: continuation file path → original file path.
 * Used to clear pending state when a continuation is read.
 */
export const continuationToOriginal = new Map();

// ─── Configuration Constants ───────────────────────────────────────────────

/** File extensions that should not be read as text. */
export const BINARY_EXTENSIONS = new Set([
  ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tiff",
  ".pdf",
  ".zip", ".gz", ".tar", ".rar", ".7z", ".bz2", ".xz", ".zst",
  ".exe", ".dll", ".so", ".dylib", ".o", ".a",
  ".woff", ".woff2", ".ttf", ".eot", ".otf",
  ".mp3", ".mp4", ".avi", ".mov", ".wav", ".flac", ".ogg", ".mkv",
  ".bin", ".dat", ".db", ".sqlite", ".sqlite3",
  ".nupkg", ".snupkg",
  ".class", ".pyc", ".pyo",
  ".wasm",
]);

/**
 * Maximum bytes of raw text per continuation/composite chunk.
 * Chunks end up in JSON "text" fields where special characters are escaped
 * (\n → \\n, \t → \\t, " → \\"). Must stay small enough that the resulting
 * serialized response avoids Claude Code's "Large MCP response" warning,
 * which fires at ~10k tokens (~39KB raw for code/schema content at ~3.9
 * chars/token). 28KB raw with 10-15% JSON escaping → ~32KB serialized →
 * ~8k tokens, safely under the warning threshold.
 *
 * Trade-off: more continuation parts per large file. A 140KB file produces
 * 5 parts instead of 3. This is acceptable — agents read all parts
 * automatically, and eliminating the warning prevents counter-productive
 * "efficiency" pressure on read-heavy tasks.
 *
 * History: 70KB (original) → 50KB (64KB persistence cap) → 28KB (warning suppression).
 */
export const CHUNK_BYTE_LIMIT = 28 * 1024;

/**
 * Maximum serialized JSON size for an inline MCP tool response.
 * Controls when gatedToolResponse() and handleLargeOutput() proactively
 * split responses into composites/temp files.
 *
 * Two thresholds matter:
 *   1. ~35KB serialized: Claude Code shows "Large MCP response" warning (bad UX)
 *   2. ~64KB serialized: Claude Code persists to disk with JSON double-escaping (broken)
 *
 * Set to 32KB to stay under #1 with margin. 28KB raw (CHUNK_BYTE_LIMIT)
 * with JSON escaping produces ~32KB serialized.
 *
 * History: 60KB (persistence guard only) → 32KB (warning avoidance).
 */
export const RESPONSE_SERIALIZED_LIMIT = 32 * 1024;

/** Prefix for continuation temp files — used to detect them on read. */
export const CONTINUATION_PREFIX = "bannou-read-";

// ─── Script & Sentinel Constants ───────────────────────────────────────────

/** Directory for agent-created scripts. Only location write_script can write to. */
export const SCRIPTS_DIR = "/tmp/bannou-scripts";

/**
 * Permission bucket — isolates sentinel files per terminal.
 * Set BANNOU_MCP_BUCKET=1..5 in each terminal's environment.
 * Default: 1. Each bucket has its own sentinel file and permission state.
 */
export const MCP_BUCKET = parseInt(process.env.BANNOU_MCP_BUCKET || "1", 10);

/** Sentinel file path for this bucket (Stream Deck, manual commands). */
export const SENTINEL_PATH = `/tmp/bannou-mcp-inject-${MCP_BUCKET}.json`;

/**
 * Paths/prefixes that agents can NEVER write/edit — not even with grantPermissions.
 * All bucket sentinel files and the restricted scripts directory are unconditionally blocked.
 */
export const NEVER_OVERRIDABLE = [
  { type: "prefix", path: "/tmp/bannou-mcp-inject-" },
  { type: "prefix", path: "scripts/restricted/" },
];

/**
 * Directory prefixes that are frozen by default — agents cannot write/edit files
 * in these directories UNLESS a matching grantPermissions entry exists (injected
 * via sentinel by the human). Mirrors the frozen-files list in CLAUDE-PRACTICES.md.
 *
 * These are relative to the project root. isPathProtected() resolves them against
 * CLAUDE_PROJECT_DIR before matching.
 */
export const FROZEN_PREFIXES = [
  "scripts/",
  "docs/reference/",
  "structural-tests/",
  "test-utilities/",
  ".claude/"
];

/**
 * Exact frozen paths (not directories). Same behavior as FROZEN_PREFIXES but
 * matched exactly rather than as prefixes.
 */
export const FROZEN_EXACT = [
  ".claude/settings.json",
];

// ─── Context Composite Constants ────────────────────────────────────────────

/**
 * Tracks composite files that the agent MUST read before any other tool call
 * (except read_file and prepare_context, which are always allowed).
 *
 * Set by prepare_context, cleared as composites are read via read_file.
 * When non-empty, all mutation tools are blocked.
 */
export const requiredReading = new Set();

/** Prefix for composite context files — used to detect them on read. */
export const COMPOSITE_PREFIX = "bannou-context-";

/**
 * Maximum bytes per composite file. Must stay under CHUNK_BYTE_LIMIT so
 * composites are returned in a single read_file response with no splitting.
 * At 26KB raw → ~30KB serialized → ~7.7k tokens, safely under warning threshold.
 *
 * History: 48KB (persistence guard) → 26KB (warning avoidance, must be < CHUNK_BYTE_LIMIT).
 */
export const COMPOSITE_BYTE_LIMIT = 26 * 1024;

// ─── Audit Gate ─────────────────────────────────────────────────────────────

/**
 * Audit gate — blocks dispatch_worker until the parent agent attests
 * to having audited the previous worker's output.
 *
 * When dispatch_worker is called with audit: true and the worker completes,
 * this is set to an object { task, scopes, completedAt }. While set,
 * dispatch_worker refuses to spawn new workers.
 *
 * Cleared ONLY by the clear_audit_gate tool, which requires the parent
 * agent to type an exact attestation string. This makes it behaviorally
 * impossible to skip auditing without explicitly lying in the attestation.
 */
export let auditGatePending = null;

/** Required attestation string — must be typed exactly to clear the gate. */
export const AUDIT_ATTESTATION = "I have fully audited this agent's work and corrected it to bring in line with developer TENETS and schema-rules";

export function setAuditGate(workerInfo) {
  auditGatePending = workerInfo;
}

export function clearAuditGateState() {
  const prev = auditGatePending;
  auditGatePending = null;
  return prev;
}

// ─── Script Constants ───────────────────────────────────────────────────────

/** Allowed script extensions for write_script. */
export const SCRIPT_EXTENSIONS = new Set([".sh", ".py", ".mjs"]);
