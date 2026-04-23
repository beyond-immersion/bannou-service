/**
 * Tenet parsers and catalog helpers.
 *
 * Read-only surface for the Bannou tenet documentation system. Parses the
 * existing files in place — no content is rewritten.
 *
 * Parsed files:
 *   - docs/reference/TENETS.md               (index; contains T0/T1/T2 bodies,
 *                                              per-category summary tables,
 *                                              and the Quick Reference
 *                                              violations table)
 *   - docs/reference/tenets/FOUNDATION.md    (T4, T5, T6, T13, T15, T18,
 *                                              T27, T28, T29, T32)
 *   - docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md  (T3, T7, T8, T9,
 *                                                        T17, T30, T31)
 *   - docs/reference/tenets/IMPLEMENTATION-DATA.md      (T14, T20, T21, T23,
 *                                                        T24, T25, T26)
 *   - docs/reference/tenets/QUALITY.md       (T10, T11, T12, T16, T19, T22)
 *
 * All parsing is mechanical (regex over structural markers that already
 * exist in the files). No inference, no reformatting.
 *
 * DATA MODEL
 *
 *   Tenet {
 *     id:            "T4"                                       // canonical id
 *     number:        4                                          // numeric for sort
 *     name:          "Infrastructure Libs Pattern"
 *     severity:      "ABSOLUTE" | "MANDATORY" | ...
 *     category:      "foundation" | "quality" | ...             // see CATEGORIES
 *     rule:          "Services MUST use the three infrastructure libs..."
 *                                                               // from **Rule**: line
 *                                                               // (bold first line for T1/T2,
 *                                                               //  first paragraph for T0)
 *     summaryRule:   "MUST use lib-state, lib-messaging..."     // from TENETS.md
 *                                                               // per-category summary table
 *                                                               // (null if none recorded)
 *     body:          "<full markdown body>"                     // between heading and next boundary
 *     sourceFile:    "docs/reference/tenets/FOUNDATION.md"
 *     bodyStartLine: 17                                         // 1-based, first line of body
 *     bodyEndLine:   173                                        // 1-based, last line of body
 *     violations:    Violation[]                                // rows citing this tenet
 *   }
 *
 *   Violation {
 *     violation:     "Direct Redis/MySQL connection"
 *     tenets:        ["T4"]                                     // one or more tenet ids
 *     fix:           "Use IStateStoreFactory via lib-state"
 *     sourceLine:    251                                        // line in TENETS.md
 *   }
 *
 * CACHING
 *
 *   getTenetCatalog() parses all source files and returns the computed
 *   catalog. Results are cached per file-mtime tuple — if no source file
 *   has changed since the last call, the cached catalog is returned.
 *   This keeps list_tenets cheap for repeated invocations.
 */

import { readFile, stat, writeFile, access } from "node:fs/promises";
import { resolve } from "node:path";
import { constants } from "node:fs";

// ─── Project Directory ─────────────────────────────────────────────────────

function projectDir() {
  return process.env.CLAUDE_PROJECT_DIR || process.cwd();
}

function absolute(relPath) {
  return resolve(projectDir(), relPath);
}

// ─── Category Metadata ─────────────────────────────────────────────────────

/**
 * Single source of truth for where each category lives, what it covers, and
 * how it shows up in source-code comments (per T0). Keyed by category id
 * (kebab-case, matches the category-id tokens used in list_tenets filters).
 */
export const CATEGORIES = Object.freeze({
  meta: {
    label: "Meta",
    file: "docs/reference/TENETS.md",
    description: "Meta-rules about how tenets are referenced",
    whenToReference: "When writing comments about tenet compliance in any file",
    sourceCodeCategoryName: null,
    // Structural categories hosted in TENETS.md alongside T0/T1/T2 have no
    // auto-maintained derived sections. add_tenet rejects these categories.
    navLabel: null,
    summaryTableHeading: null,
    autoMaintained: false,
  },
  "schema-rules": {
    label: "Schema Rules",
    file: "docs/reference/TENETS.md",
    description: "Schema-first development (delegates to SCHEMA-RULES.md)",
    whenToReference: "Before creating or modifying any schema file",
    externalDoc: "docs/reference/SCHEMA-RULES.md",
    sourceCodeCategoryName: null,
    navLabel: null,
    summaryTableHeading: null,
    autoMaintained: false,
  },
  "service-hierarchy": {
    label: "Service Hierarchy",
    file: "docs/reference/TENETS.md",
    description: "Service layer dependencies (delegates to SERVICE-HIERARCHY.md)",
    whenToReference: "Before adding any service client dependency",
    externalDoc: "docs/reference/SERVICE-HIERARCHY.md",
    sourceCodeCategoryName: null,
    navLabel: null,
    summaryTableHeading: null,
    autoMaintained: false,
  },
  foundation: {
    label: "Foundation",
    file: "docs/reference/tenets/FOUNDATION.md",
    description: "Architecture & design",
    whenToReference: "Before starting any new service or feature",
    sourceCodeCategoryName: "FOUNDATION TENETS",
    // navLabel: used to find this category's row in the "Tenet Categories"
    // navigation table in TENETS.md. The row looks like:
    //   | [**{navLabel}**](tenets/{FILE}.md) | T4, T5, ... | {whenToReference} |
    navLabel: "Foundation",
    // summaryTableHeading: the H2 heading introducing this category's
    // per-category summary table (| **T4** | Name | Core Rule |).
    summaryTableHeading: "Foundation Tenets (Architecture & Design)",
    autoMaintained: true,
  },
  "implementation-behavior": {
    label: "Implementation: Behavior",
    file: "docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md",
    description: "Service behavior & contracts",
    whenToReference: "While designing service method behavior",
    sourceCodeCategoryName: "IMPLEMENTATION TENETS",
    navLabel: "Implementation: Behavior",
    summaryTableHeading: "Implementation Tenets: Service Behavior & Contracts",
    autoMaintained: true,
  },
  "implementation-data": {
    label: "Implementation: Data",
    file: "docs/reference/tenets/IMPLEMENTATION-DATA.md",
    description: "Data modeling & code discipline",
    whenToReference: "While writing code and modeling data",
    sourceCodeCategoryName: "IMPLEMENTATION TENETS",
    navLabel: "Implementation: Data",
    summaryTableHeading: "Implementation Tenets: Data Modeling & Code Discipline",
    autoMaintained: true,
  },
  quality: {
    label: "Quality",
    file: "docs/reference/tenets/QUALITY.md",
    description: "Standards & verification",
    whenToReference: "During code review or before PR submission",
    sourceCodeCategoryName: "QUALITY TENETS",
    navLabel: "Quality",
    summaryTableHeading: "Quality Tenets (Standards & Verification)",
    autoMaintained: true,
  },
});

/**
 * Category for each structurally-special tenet hosted in TENETS.md.
 *
 * Three categories (meta, schema-rules, service-hierarchy) share the index
 * file, so file-based derivation is ambiguous for their ids. This tiny
 * fallback map resolves the ambiguity for T0, T1, and T2 — the three
 * structural tenets that have lived in TENETS.md since the document was
 * authored.
 *
 * `add_tenet` rejects categories ∈ {meta, schema-rules, service-hierarchy}
 * because extending them requires hand-authoring new entries in both the
 * TENETS.md body AND this fallback map. If you add a new structural tenet
 * by hand, append it here in lockstep.
 */
const TENETS_MD_ID_CATEGORIES = Object.freeze({
  T0: "meta",
  T1: "schema-rules",
  T2: "service-hierarchy",
});

/**
 * Reverse lookup: source-file path → category id. Populated at module load
 * from the CATEGORIES table. Skips TENETS.md (ambiguous — see above).
 *
 * This replaces the previous hardcoded `TENET_CATEGORY_MAP`. Category
 * assignment is now derived from the file the tenet was parsed from,
 * eliminating the persistence problem that comes with a separate map.
 */
const FILE_TO_CATEGORY_ID = (() => {
  const map = {};
  for (const [id, meta] of Object.entries(CATEGORIES)) {
    if (meta.file === "docs/reference/TENETS.md") continue;
    map[meta.file] = id;
  }
  return Object.freeze(map);
})();

// ─── Regexes ───────────────────────────────────────────────────────────────

// `## Tenet 4: Infrastructure Libs Pattern (ABSOLUTE)` — captures number, name, severity.
const TENET_HEADING_RE = /^##\s+Tenet\s+(\d+)\s*:\s*(.+?)\s*(?:\(([^)]+)\))?\s*$/;

// `**Rule**: ...` (first occurrence inside a tenet body is the canonical rule).
const RULE_LINE_RE = /^\*\*Rule\*\*:\s*(.+)$/;

// `**... sentence ...**` — used by T1/T2 in the index to declare the rule as
// the first bolded sentence instead of a `**Rule**:` label.
const BOLD_SENTENCE_LINE_RE = /^\*\*([^*][^*]*?)\*\*(?:\s|$)/;

// Quick Reference row: `| violation | T5 | fix |` or `| violation | T5, T29 | fix |`.
// Tenet ids in the middle column are comma-separated; middle cells that are NOT
// T-numbers (e.g. column headers) are rejected by the tenets-column pattern.
const VIOLATION_ROW_RE =
  /^\|\s*(.+?)\s*\|\s*(T\d+(?:\s*,\s*T\d+)*)\s*\|\s*(.+?)\s*\|\s*$/;

// Index summary table row: `| **T4** | Name | Core Rule |`. Matches across the
// index's four category summary tables and also the per-category tables in
// IMPLEMENTATION.md's redirect stub. Captures: (number, name, core rule).
const SUMMARY_ROW_RE =
  /^\|\s*\*\*T(\d+)\*\*\s*\|\s*(.+?)\s*\|\s*(.+?)\s*\|\s*$/;

// Sentinel markers wrapping each tenet body. The id in the open sentinel must
// match the id in the close sentinel; mismatches surface in catalog.errors.
// See docs/reference/templates/TENET-TEMPLATE.md for the full structural spec.
const OPEN_SENTINEL_RE = /^\s*<!--\s*TENET:T(\d+)\s*-->\s*$/;
const CLOSE_SENTINEL_RE = /^\s*<!--\s*\/TENET:T(\d+)\s*-->\s*$/;

// ─── Cache ─────────────────────────────────────────────────────────────────

let cachedCatalog = null;
let cachedMtimes = null;

/**
 * Read and stat every tenet source file, returning { content, mtimeMs, exists }
 * per path. Missing files are surfaced as errors on the catalog — we never
 * silently omit a category.
 */
async function readAllSources() {
  const files = [
    absolute("docs/reference/TENETS.md"),
    absolute("docs/reference/tenets/FOUNDATION.md"),
    absolute("docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md"),
    absolute("docs/reference/tenets/IMPLEMENTATION-DATA.md"),
    absolute("docs/reference/tenets/QUALITY.md"),
  ];

  const sources = new Map();
  for (const path of files) {
    try {
      const [content, st] = await Promise.all([
        readFile(path, "utf-8"),
        stat(path),
      ]);
      sources.set(path, { content, mtimeMs: st.mtimeMs, exists: true });
    } catch (err) {
      sources.set(path, { content: null, mtimeMs: 0, exists: false, error: err.message });
    }
  }
  return sources;
}

function sameMtimes(a, b) {
  if (!a || !b || a.size !== b.size) return false;
  for (const [path, mt] of a) {
    if (b.get(path) !== mt) return false;
  }
  return true;
}

// ─── Parsers ───────────────────────────────────────────────────────────────

/**
 * Parse a category file into an array of tenet records.
 *
 * Tenet boundaries: every tenet body is wrapped in `<!-- TENET:T{N} -->` /
 * `<!-- /TENET:T{N} -->` HTML-comment sentinels. The body excludes the
 * sentinel lines themselves but includes everything between (heading,
 * blank lines, prose, tables, code blocks, HRs).
 *
 * The `## Tenet N: Name (SEVERITY)` heading must appear inside the pair;
 * its number must match the sentinel id, or a structural error is recorded
 * (and the tenet is skipped from the parse so downstream consumers get a
 * consistent map).
 *
 * Returns [{ id, number, name, severity, rule, body, bodyStartLine,
 *            bodyEndLine, sourceFile, errors }]. The errors array surfaces
 * structural problems specific to this file's parse — orphaned sentinels,
 * id mismatches, missing headings, duplicate ids in the same file.
 *
 * The category assignment and summaryRule come from index-parsing in
 * parseIndexSummary() and are attached at catalog-build time.
 */
export function parseCategoryFile(content, sourceFile) {
  const lines = content.split("\n");
  const tenets = [];
  const errors = [];

  // First pass: enumerate every open and close sentinel position.
  const opens = []; // { id, number, line: 0-based }
  const closes = []; // { id, number, line: 0-based }
  for (let i = 0; i < lines.length; i++) {
    const om = lines[i].match(OPEN_SENTINEL_RE);
    if (om) {
      opens.push({ id: `T${parseInt(om[1], 10)}`, number: parseInt(om[1], 10), line: i });
      continue;
    }
    const cm = lines[i].match(CLOSE_SENTINEL_RE);
    if (cm) {
      closes.push({ id: `T${parseInt(cm[1], 10)}`, number: parseInt(cm[1], 10), line: i });
    }
  }

  // Second pass: pair them. Sentinels are flat (no nesting), so we walk both
  // arrays in parallel. Any imbalance or id mismatch is a structural error.
  if (opens.length !== closes.length) {
    errors.push(
      `${sourceFile}: ${opens.length} open sentinels vs ${closes.length} close sentinels — ` +
      `the document is structurally broken (see TENET-TEMPLATE.md).`,
    );
  }

  const seenIds = new Set();
  const pairCount = Math.min(opens.length, closes.length);
  for (let k = 0; k < pairCount; k++) {
    const open = opens[k];
    const close = closes[k];

    if (close.line < open.line) {
      errors.push(
        `${sourceFile}: close sentinel ${close.id} at line ${close.line + 1} ` +
        `precedes open sentinel ${open.id} at line ${open.line + 1}.`,
      );
      continue;
    }

    if (open.id !== close.id) {
      errors.push(
        `${sourceFile}: sentinel pair mismatch — open ${open.id} at line ${open.line + 1} ` +
        `paired with close ${close.id} at line ${close.line + 1}.`,
      );
      continue;
    }

    // Detect nesting: the next open (if any) must be after this close.
    if (k + 1 < opens.length && opens[k + 1].line < close.line) {
      errors.push(
        `${sourceFile}: tenets do not nest — ${opens[k + 1].id} at line ${opens[k + 1].line + 1} ` +
        `opens before ${open.id} at line ${close.line + 1} closes.`,
      );
      // Don't skip — parse what we can; the user will see the error.
    }

    if (seenIds.has(open.id)) {
      errors.push(
        `${sourceFile}: ${open.id} appears in more than one sentinel pair.`,
      );
      continue;
    }
    seenIds.add(open.id);

    // Extract the body (lines strictly between the sentinels).
    const bodyStart = open.line + 1;
    const bodyEnd = close.line - 1;

    // Trim leading/trailing blanks for stable body content.
    let effectiveStart = bodyStart;
    while (effectiveStart <= bodyEnd && lines[effectiveStart].trim() === "") {
      effectiveStart++;
    }
    let effectiveEnd = bodyEnd;
    while (effectiveEnd >= effectiveStart && lines[effectiveEnd].trim() === "") {
      effectiveEnd--;
    }

    if (effectiveEnd < effectiveStart) {
      errors.push(
        `${sourceFile}: ${open.id} has an empty body between sentinels at lines ` +
        `${open.line + 1}-${close.line + 1}.`,
      );
      continue;
    }

    const bodyLines = lines.slice(effectiveStart, effectiveEnd + 1);
    const body = bodyLines.join("\n");

    // Locate the `## Tenet N:` heading inside the body and verify the number.
    let heading = null;
    for (const line of bodyLines) {
      const hm = line.match(TENET_HEADING_RE);
      if (hm) {
        heading = hm;
        break;
      }
    }

    if (!heading) {
      errors.push(
        `${sourceFile}: ${open.id} has no \`## Tenet N:\` heading inside its sentinel pair ` +
        `(lines ${open.line + 1}-${close.line + 1}).`,
      );
      continue;
    }

    const headingNumber = parseInt(heading[1], 10);
    if (headingNumber !== open.number) {
      errors.push(
        `${sourceFile}: ${open.id} sentinel wraps a heading for Tenet ${headingNumber} — ` +
        `the sentinel id and heading number must match.`,
      );
      continue;
    }

    const name = heading[2].trim();
    const severity = (heading[3] || "").trim() || null;

    // bodyAfterHeading: the post-heading content that editTenet expects.
    // Skip the heading line and any single blank line that follows it for
    // a clean round-trip (heading + optional blank are structural, not
    // body-authored content).
    let postHeadingLines = [];
    let headingFoundAt = -1;
    for (let k = 0; k < bodyLines.length; k++) {
      if (TENET_HEADING_RE.test(bodyLines[k])) {
        headingFoundAt = k;
        break;
      }
    }
    if (headingFoundAt >= 0) {
      let startPost = headingFoundAt + 1;
      // Drop ONE blank line if present directly after the heading (the structural
      // blank that editTenet re-inserts on write). Further blanks are preserved.
      if (startPost < bodyLines.length && bodyLines[startPost].trim() === "") {
        startPost++;
      }
      postHeadingLines = bodyLines.slice(startPost);
    }
    const bodyAfterHeading = postHeadingLines.join("\n");

    // Extract rule from POST-HEADING content so the tier-3 first-paragraph
    // fallback picks up the rule prose, not the `## Tenet N: ...` heading itself.
    // T0/T1/T2 rely on this path — they declare their rule in the first paragraph
    // (T0) or a leading bold sentence (T1/T2) rather than a `**Rule**:` label.
    const rule = extractRule(postHeadingLines);

    tenets.push({
      id: open.id,
      number: open.number,
      name,
      severity,
      rule,
      body,
      bodyAfterHeading,
      sourceFile,
      bodyStartLine: effectiveStart + 1, // 1-based
      bodyEndLine: effectiveEnd + 1,
      openSentinelLine: open.line + 1, // 1-based; used by write tools for splice positioning
      closeSentinelLine: close.line + 1, // 1-based
    });
  }

  return { tenets, errors };
}

/**
 * Extract the "Rule" line from a tenet body. Three-tier detection:
 *   1. `**Rule**: ...` — the canonical form used across all category files.
 *   2. First fully-bold line (`**...**`) — used by T1/T2 in the index where
 *      the rule is bolded as a declarative sentence.
 *   3. First non-empty prose line — fallback for T0 (meta tenet, no label).
 */
function extractRule(bodyLines) {
  // Tier 1: explicit **Rule**: label.
  for (const line of bodyLines) {
    const m = line.match(RULE_LINE_RE);
    if (m) return m[1].trim();
  }

  // Tier 2: leading fully-bold sentence.
  for (const line of bodyLines) {
    const trimmed = line.trim();
    if (trimmed.length === 0) continue;
    const m = trimmed.match(BOLD_SENTENCE_LINE_RE);
    if (m) return m[1].trim();
    break; // only check the first non-empty line
  }

  // Tier 3: first non-empty prose line that isn't a blockquote or HR.
  for (const line of bodyLines) {
    const trimmed = line.trim();
    if (trimmed.length === 0) continue;
    if (trimmed.startsWith(">") || trimmed.startsWith("---")) continue;
    return trimmed;
  }

  return null;
}

/**
 * Parse TENETS.md's per-category summary tables into a Map<id, summaryRule>.
 *
 * Summary rows look like `| **T4** | Infrastructure Libs Pattern | MUST
 * use lib-state, lib-messaging... |`. Capture 3 (the "Core Rule" cell) is
 * what we want as the summary.
 *
 * First-occurrence wins. TENETS.md has the canonical tables at the top; if
 * IMPLEMENTATION.md's redirect stub duplicates rows later, those are ignored.
 */
export function parseIndexSummary(content) {
  const lines = content.split("\n");
  const summary = new Map();

  for (const line of lines) {
    const m = line.match(SUMMARY_ROW_RE);
    if (!m) continue;
    const id = `T${parseInt(m[1], 10)}`;
    if (summary.has(id)) continue;
    const coreRule = m[3].trim();
    summary.set(id, coreRule);
  }

  return summary;
}

/**
 * Parse TENETS.md's Quick Reference table into Violation records.
 *
 * The table is introduced by a row whose body is exactly
 * `| Violation | Tenet | Fix |`. We skip the header and separator rows and
 * capture every data row whose second column matches the `T\d+(, T\d+)*`
 * shape.
 *
 * Non-table text (empty line or a line not starting with `|`) terminates
 * the table, so pipe-separated lines appearing later in the document don't
 * leak false positives.
 */
export function parseViolations(content) {
  const lines = content.split("\n");
  const violations = [];

  let inTable = false;
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (!inTable) {
      if (/^\|\s*Violation\s*\|\s*Tenet\s*\|\s*Fix\s*\|\s*$/.test(line)) {
        inTable = true;
        // Skip the separator row (`|---|---|---|`) if present on next line.
        if (i + 1 < lines.length && /^\|[\s\-:|]+\|$/.test(lines[i + 1])) {
          i++;
        }
        continue;
      }
      continue;
    }

    if (line.trim() === "" || !line.trim().startsWith("|")) {
      inTable = false;
      continue;
    }

    const m = line.match(VIOLATION_ROW_RE);
    if (!m) continue;

    const tenetIds = m[2]
      .split(",")
      .map((t) => t.trim())
      .filter(Boolean);

    violations.push({
      violation: m[1].trim(),
      tenets: tenetIds,
      fix: m[3].trim(),
      sourceLine: i + 1,
    });
  }

  return violations;
}

// ─── Catalog Assembly ──────────────────────────────────────────────────────

/**
 * Build the full tenet catalog. Parses every source file, cross-references
 * summary rules from the index, and attaches violation rows to the tenets
 * they cite. Returns a structured object; downstream formatters shape it
 * into tool responses.
 */
export async function getTenetCatalog({ force = false } = {}) {
  const sources = await readAllSources();

  const currentMtimes = new Map();
  for (const [path, entry] of sources) {
    currentMtimes.set(path, entry.mtimeMs);
  }
  if (!force && cachedCatalog && sameMtimes(cachedMtimes, currentMtimes)) {
    return cachedCatalog;
  }

  const indexPath = absolute("docs/reference/TENETS.md");
  const foundationPath = absolute("docs/reference/tenets/FOUNDATION.md");
  const implBehaviorPath = absolute("docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md");
  const implDataPath = absolute("docs/reference/tenets/IMPLEMENTATION-DATA.md");
  const qualityPath = absolute("docs/reference/tenets/QUALITY.md");

  const indexEntry = sources.get(indexPath);
  const foundationEntry = sources.get(foundationPath);
  const implBehaviorEntry = sources.get(implBehaviorPath);
  const implDataEntry = sources.get(implDataPath);
  const qualityEntry = sources.get(qualityPath);

  const errors = [];
  const tenets = new Map();

  const categorySources = [
    { entry: indexEntry, rel: "docs/reference/TENETS.md" },
    { entry: foundationEntry, rel: "docs/reference/tenets/FOUNDATION.md" },
    { entry: implBehaviorEntry, rel: "docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md" },
    { entry: implDataEntry, rel: "docs/reference/tenets/IMPLEMENTATION-DATA.md" },
    { entry: qualityEntry, rel: "docs/reference/tenets/QUALITY.md" },
  ];

  for (const { entry, rel } of categorySources) {
    if (!entry || !entry.exists) {
      errors.push(`Missing source file: ${rel}${entry?.error ? ` (${entry.error})` : ""}`);
      continue;
    }
    const { tenets: parsed, errors: parseErrors } = parseCategoryFile(entry.content, rel);

    // Surface every structural error from the parse (sentinel imbalance,
    // mismatched ids, missing headings, duplicate ids in the same file).
    for (const e of parseErrors) errors.push(e);

    for (const t of parsed) {
      // Derive category from the source file. TENETS.md is ambiguous (3
      // categories share it), so fall back to the TENETS_MD_ID_CATEGORIES
      // map for structural ids (T0/T1/T2).
      let category;
      if (rel === "docs/reference/TENETS.md") {
        category = TENETS_MD_ID_CATEGORIES[t.id];
        if (!category) {
          errors.push(
            `${rel}: ${t.id} is hosted in TENETS.md but not listed in ` +
            `TENETS_MD_ID_CATEGORIES. Only T0/T1/T2 are structurally supported ` +
            `in the index file; add_tenet does not create new tenets here. ` +
            `If you authored this tenet by hand, add its id to TENETS_MD_ID_CATEGORIES.`,
          );
          continue;
        }
      } else {
        category = FILE_TO_CATEGORY_ID[rel];
        if (!category) {
          errors.push(
            `${rel}: file is not registered in CATEGORIES — cannot assign a category.`,
          );
          continue;
        }
      }

      if (tenets.has(t.id)) {
        errors.push(
          `${t.id} declared in multiple files: ${tenets.get(t.id).sourceFile} and ${rel}`,
        );
        continue;
      }
      tenets.set(t.id, { ...t, category, summaryRule: null, violations: [] });
    }
  }

  // Attach summary rules from the index tables.
  if (indexEntry?.exists) {
    const summary = parseIndexSummary(indexEntry.content);
    for (const [id, summaryRule] of summary) {
      if (tenets.has(id)) {
        tenets.get(id).summaryRule = summaryRule;
      }
    }
  }

  // Parse the Quick Reference table and attach violations to their tenets.
  let violations = [];
  if (indexEntry?.exists) {
    violations = parseViolations(indexEntry.content);
    for (const v of violations) {
      for (const id of v.tenets) {
        if (tenets.has(id)) {
          tenets.get(id).violations.push(v);
        }
      }
    }
  }

  // Report structural TENETS.md tenets (T0/T1/T2) that are in the fallback
  // map but missing from the parsed index. (Previously this loop iterated
  // over TENET_CATEGORY_MAP; category is now derived from file structure,
  // so the only "should exist" invariant left is the structural T0/T1/T2
  // lookup.)
  for (const id of Object.keys(TENETS_MD_ID_CATEGORIES)) {
    if (!tenets.has(id)) {
      errors.push(
        `${id} is in TENETS_MD_ID_CATEGORIES but was not parsed from TENETS.md — ` +
        `structural tenet is missing from the index file.`,
      );
    }
  }

  // Report violations that cite non-existent tenets.
  for (const v of violations) {
    for (const id of v.tenets) {
      if (!tenets.has(id)) {
        errors.push(
          `Quick Reference row cites unknown tenet ${id}: "${v.violation}" (line ${v.sourceLine})`,
        );
      }
    }
  }

  const catalog = {
    tenets,
    violations,
    errors,
    sources: {
      "docs/reference/TENETS.md": indexEntry?.exists ?? false,
      "docs/reference/tenets/FOUNDATION.md": foundationEntry?.exists ?? false,
      "docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md": implBehaviorEntry?.exists ?? false,
      "docs/reference/tenets/IMPLEMENTATION-DATA.md": implDataEntry?.exists ?? false,
      "docs/reference/tenets/QUALITY.md": qualityEntry?.exists ?? false,
    },
  };

  cachedCatalog = catalog;
  cachedMtimes = currentMtimes;
  return catalog;
}

// ─── Public Read API ───────────────────────────────────────────────────────

/**
 * List tenets matching the filter. Returns the short-list view — id, number,
 * name, category, severity, summary rule (or the full rule if no summary is
 * recorded), violation count, source file. Sorted by number.
 *
 * Filters (all optional):
 *   - category: category id (e.g., "foundation")
 *   - severity: uppercase match against parsed severity token
 *   - keyword:  case-insensitive substring match against name + rule +
 *               summaryRule
 */
export async function listTenets(filters = {}) {
  const { category, severity, keyword } = filters;
  const catalog = await getTenetCatalog();

  // ── Filter validation ────────────────────────────────────────────────
  // Invalid category → explicit error listing valid options. Silent empty
  // results hide typos; compare with add_tenet / add_violation which also
  // reject unknown categories and tenet ids.
  if (category !== undefined && category !== null && category !== "") {
    if (!(category in CATEGORIES)) {
      return {
        tenets: [],
        total: 0,
        categories: Object.fromEntries(
          Object.entries(CATEGORIES).map(([id, meta]) => [id, { ...meta }]),
        ),
        errors: catalog.errors,
        filterError:
          `Unknown category "${category}". ` +
          `Valid categories: ${Object.keys(CATEGORIES).join(", ")}.`,
      };
    }
  }

  // Severity filter: not a closed set (any heading `(X)` suffix parses as
  // severity), so we cross-check against the severities actually present in
  // the catalog. Mismatches are flagged as warnings rather than hard errors
  // so the tool still returns an empty list with context.
  let severityWarning = null;
  if (severity !== undefined && severity !== null && severity !== "") {
    const knownSeverities = new Set();
    for (const t of catalog.tenets.values()) {
      if (t.severity) knownSeverities.add(t.severity.toUpperCase());
    }
    const sevUpper = severity.toUpperCase();
    if (!knownSeverities.has(sevUpper)) {
      severityWarning =
        `Severity "${severity}" does not match any tenet in the catalog. ` +
        `Known severities: ${[...knownSeverities].sort().join(", ")}.`;
    }
  }

  const kw = keyword ? keyword.toLowerCase() : null;
  const sev = severity ? severity.toUpperCase() : null;

  const rows = [];
  for (const t of catalog.tenets.values()) {
    if (category && t.category !== category) continue;
    if (sev && (t.severity || "").toUpperCase() !== sev) continue;
    if (kw) {
      const hay = [t.name, t.rule, t.summaryRule].filter(Boolean).join("\n").toLowerCase();
      if (!hay.includes(kw)) continue;
    }
    rows.push({
      id: t.id,
      number: t.number,
      name: t.name,
      category: t.category,
      categoryLabel: CATEGORIES[t.category].label,
      severity: t.severity,
      rule: t.summaryRule || t.rule,
      violationCount: t.violations.length,
      sourceFile: t.sourceFile,
    });
  }

  rows.sort((a, b) => a.number - b.number);

  return {
    tenets: rows,
    total: rows.length,
    categories: Object.fromEntries(
      Object.entries(CATEGORIES).map(([id, meta]) => [id, { ...meta }]),
    ),
    errors: catalog.errors,
    filterWarning: severityWarning,
  };
}

/**
 * Get a single tenet by id, including full body and all violation rows citing
 * it. Returns null if the id isn't known.
 */
export async function getTenet(id) {
  const catalog = await getTenetCatalog();
  const normalized = normalizeTenetId(id);
  if (!normalized) return null;
  const t = catalog.tenets.get(normalized);
  if (!t) return null;
  return {
    id: t.id,
    number: t.number,
    name: t.name,
    category: t.category,
    categoryMeta: CATEGORIES[t.category],
    severity: t.severity,
    rule: t.rule,
    summaryRule: t.summaryRule,
    body: t.body,
    bodyAfterHeading: t.bodyAfterHeading,
    sourceFile: t.sourceFile,
    bodyStartLine: t.bodyStartLine,
    bodyEndLine: t.bodyEndLine,
    openSentinelLine: t.openSentinelLine,
    closeSentinelLine: t.closeSentinelLine,
    violations: t.violations,
  };
}

/**
 * Batch get for a set of tenet ids. Unknown ids are returned in the
 * `notFound` array; found tenets are returned in `tenets` in input order.
 */
export async function getTenets(ids) {
  const catalog = await getTenetCatalog();
  const found = [];
  const notFound = [];
  for (const rawId of ids) {
    const normalized = normalizeTenetId(rawId);
    if (!normalized) {
      notFound.push(rawId);
      continue;
    }
    const t = catalog.tenets.get(normalized);
    if (!t) {
      notFound.push(normalized);
      continue;
    }
    found.push({
      id: t.id,
      number: t.number,
      name: t.name,
      category: t.category,
      categoryMeta: CATEGORIES[t.category],
      severity: t.severity,
      rule: t.rule,
      summaryRule: t.summaryRule,
      body: t.body,
      bodyAfterHeading: t.bodyAfterHeading,
      sourceFile: t.sourceFile,
      bodyStartLine: t.bodyStartLine,
      bodyEndLine: t.bodyEndLine,
      openSentinelLine: t.openSentinelLine,
      closeSentinelLine: t.closeSentinelLine,
      violations: t.violations,
    });
  }
  return { tenets: found, notFound };
}

/**
 * List violations from the Quick Reference table, optionally filtered by
 * citing tenet or keyword.
 *
 * Filters:
 *   - tenet:    one or more tenet ids (array OK); match if the violation's
 *               `tenets` intersects the filter
 *   - keyword:  case-insensitive substring over violation + fix text
 */
export async function listViolations(filters = {}) {
  const { tenet, keyword } = filters;
  const catalog = await getTenetCatalog();
  const kw = keyword ? keyword.toLowerCase() : null;

  // ── Filter validation ────────────────────────────────────────────────
  // Normalize case (F5) and validate each id against the catalog (F4). An
  // unknown id in the filter is always a user error — a typo shouldn't
  // silently return "no matches". Match add_violation's behaviour exactly:
  // list every known id in the error so the user can correct it.
  let wanted = null;
  if (tenet !== undefined && tenet !== null) {
    const rawIds = Array.isArray(tenet) ? tenet : [tenet];
    if (rawIds.length > 0) {
      const normalizedIds = [];
      const unknownIds = [];
      for (const raw of rawIds) {
        const norm = normalizeTenetId(raw);
        if (!norm || !catalog.tenets.has(norm)) {
          unknownIds.push(raw);
        } else {
          normalizedIds.push(norm);
        }
      }
      if (unknownIds.length > 0) {
        const known = [...catalog.tenets.keys()]
          .sort((a, b) => parseInt(a.slice(1), 10) - parseInt(b.slice(1), 10))
          .join(", ");
        return {
          violations: [],
          total: 0,
          filterError:
            `Unknown tenet id${unknownIds.length > 1 ? "s" : ""} in filter: ` +
            `${unknownIds.join(", ")}. Known ids: ${known}.`,
        };
      }
      wanted = new Set(normalizedIds);
    }
  }

  const rows = [];
  for (const v of catalog.violations) {
    if (wanted && !v.tenets.some((t) => wanted.has(t))) continue;
    if (kw) {
      const hay = `${v.violation}\n${v.fix}`.toLowerCase();
      if (!hay.includes(kw)) continue;
    }
    rows.push(v);
  }

  return { violations: rows, total: rows.length };
}

/**
 * Full-text keyword search across tenet bodies + violations. Returns a
 * ranked list of tenets sorted by match strength:
 *   - Name hit:         weight 10
 *   - SummaryRule hit:  weight 4
 *   - Rule hit:         weight 4
 *   - Body hit:         weight 1 per occurrence
 *   - Violation hit:    weight 2 per row (violation or fix column)
 *
 * Tokens are split on whitespace. A row matches if ALL tokens appear
 * anywhere in its combined searchable text (case-insensitive).
 */
export async function searchTenets(query, { maxResults = 20 } = {}) {
  const catalog = await getTenetCatalog();
  const tokens = query
    .toLowerCase()
    .split(/\s+/)
    .map((t) => t.trim())
    .filter(Boolean);

  if (tokens.length === 0) {
    return { query, tokens, results: [], totalMatches: 0 };
  }

  const results = [];
  for (const t of catalog.tenets.values()) {
    const nameLc = t.name.toLowerCase();
    const summaryLc = (t.summaryRule || "").toLowerCase();
    const ruleLc = (t.rule || "").toLowerCase();
    const bodyLc = t.body.toLowerCase();
    const violationLc = t.violations
      .map((v) => `${v.violation}\n${v.fix}`.toLowerCase())
      .join("\n");

    const combined = `${nameLc}\n${summaryLc}\n${ruleLc}\n${bodyLc}\n${violationLc}`;

    const allPresent = tokens.every((tok) => combined.includes(tok));
    if (!allPresent) continue;

    let score = 0;
    for (const tok of tokens) {
      if (nameLc.includes(tok)) score += 10;
      if (summaryLc.includes(tok)) score += 4;
      if (ruleLc.includes(tok)) score += 4;
      score += countOccurrences(bodyLc, tok);
      for (const v of t.violations) {
        const vtext = `${v.violation}\n${v.fix}`.toLowerCase();
        if (vtext.includes(tok)) score += 2;
      }
    }

    results.push({
      id: t.id,
      number: t.number,
      name: t.name,
      category: t.category,
      severity: t.severity,
      rule: t.summaryRule || t.rule,
      violationCount: t.violations.length,
      score,
    });
  }

  results.sort((a, b) => b.score - a.score || a.number - b.number);
  const limited = results.slice(0, maxResults);

  return {
    query,
    tokens,
    results: limited,
    totalMatches: results.length,
  };
}

// ─── Formatters ────────────────────────────────────────────────────────────

/**
 * Format the short list — the "always pulled" view. Produces a compact
 * markdown table grouped by category. Designed to fit comfortably under
 * the MCP response size limit even for all 32+ tenets.
 */
export function formatTenetList(result) {
  const { tenets, total, categories, errors, filterError, filterWarning } = result;

  // Surface invalid-filter errors loudly so typos don't hide behind empty
  // result lists. The rest of the formatting still runs so the caller sees
  // the header + zero matches as well.
  if (filterError) {
    return `⚠️  ${filterError}`;
  }

  const lines = [];
  lines.push(`# Tenet Catalog — ${total} tenets`);
  if (filterWarning) {
    lines.push("");
    lines.push(`⚠️  ${filterWarning}`);
  }
  lines.push("");

  const byCategory = new Map();
  for (const t of tenets) {
    if (!byCategory.has(t.category)) byCategory.set(t.category, []);
    byCategory.get(t.category).push(t);
  }

  // Stable display order (rough dependency: meta → schema/hierarchy →
  // foundation → impl → quality).
  const CATEGORY_ORDER = [
    "meta",
    "schema-rules",
    "service-hierarchy",
    "foundation",
    "implementation-behavior",
    "implementation-data",
    "quality",
  ];

  for (const catId of CATEGORY_ORDER) {
    const tenetsInCat = byCategory.get(catId);
    if (!tenetsInCat || tenetsInCat.length === 0) continue;
    const meta = categories[catId];
    lines.push(`## ${meta.label} — ${meta.description}`);
    lines.push(`*When to reference*: ${meta.whenToReference}`);
    if (meta.externalDoc) lines.push(`*See also*: \`${meta.externalDoc}\``);
    if (meta.sourceCodeCategoryName) {
      lines.push(`*Source-code tag*: \`${meta.sourceCodeCategoryName}\``);
    }
    lines.push("");
    lines.push("| ID | Name | Severity | Rule | Violations |");
    lines.push("|----|------|----------|------|------------|");
    for (const t of tenetsInCat) {
      const sev = t.severity || "-";
      const rule = (t.rule || "").replace(/\|/g, "\\|");
      lines.push(`| **${t.id}** | ${t.name} | ${sev} | ${rule} | ${t.violationCount} |`);
    }
    lines.push("");
  }

  if (errors && errors.length > 0) {
    lines.push("## Parse issues");
    lines.push("");
    for (const e of errors) lines.push(`- ⚠️  ${e}`);
    lines.push("");
  }

  lines.push("---");
  lines.push(
    "Use `get_tenet(id: \"T{N}\")` for full body, `list_violations` to query " +
    "the Quick Reference table, `search_tenets` for keyword search.",
  );

  return lines.join("\n");
}

/**
 * Format a single tenet for detail view — heading, metadata, full body, and
 * every violation row citing it.
 */
export function formatTenetDetail(tenet) {
  if (!tenet) return "Tenet not found.";

  const lines = [];
  lines.push(`# ${tenet.id}: ${tenet.name}`);
  lines.push("");
  const sev = tenet.severity ? ` (${tenet.severity})` : "";
  lines.push(`**Category**: ${tenet.categoryMeta.label}${sev}`);
  lines.push(`**Source**: \`${tenet.sourceFile}\` lines ${tenet.bodyStartLine}-${tenet.bodyEndLine}`);
  if (tenet.summaryRule) {
    lines.push(`**Summary**: ${tenet.summaryRule}`);
  }
  if (tenet.categoryMeta.externalDoc) {
    lines.push(`**See also**: \`${tenet.categoryMeta.externalDoc}\``);
  }
  if (tenet.categoryMeta.sourceCodeCategoryName) {
    lines.push(`**Source-code tag**: \`${tenet.categoryMeta.sourceCodeCategoryName}\` (per T0)`);
  }
  lines.push("");
  lines.push("## Rule");
  lines.push("");
  lines.push(tenet.rule || "(no rule extracted)");
  lines.push("");
  lines.push("## Body");
  lines.push("");
  lines.push(tenet.body);
  lines.push("");

  if (tenet.violations.length > 0) {
    lines.push(`## Quick Reference violations citing ${tenet.id} (${tenet.violations.length})`);
    lines.push("");
    lines.push("| Violation | Cited Tenets | Fix |");
    lines.push("|-----------|--------------|-----|");
    for (const v of tenet.violations) {
      const cited = v.tenets.join(", ");
      const violation = v.violation.replace(/\|/g, "\\|");
      const fix = v.fix.replace(/\|/g, "\\|");
      lines.push(`| ${violation} | ${cited} | ${fix} |`);
    }
  } else {
    lines.push(`No Quick Reference violations currently cite ${tenet.id}.`);
  }

  return lines.join("\n");
}

/**
 * Format a violation list result.
 */
export function formatViolationList(result) {
  const { violations, total, filterError } = result;
  if (filterError) return `⚠️  ${filterError}`;
  if (total === 0) return "No violations matched the filter.";

  const lines = [];
  lines.push(`# Quick Reference violations — ${total} match${total === 1 ? "" : "es"}`);
  lines.push("");
  lines.push("| Violation | Tenets | Fix |");
  lines.push("|-----------|--------|-----|");
  for (const v of violations) {
    const violation = v.violation.replace(/\|/g, "\\|");
    const fix = v.fix.replace(/\|/g, "\\|");
    lines.push(`| ${violation} | ${v.tenets.join(", ")} | ${fix} |`);
  }
  return lines.join("\n");
}

/**
 * Format a search result.
 */
export function formatSearchResults(result) {
  const { query, tokens, results, totalMatches } = result;
  const lines = [];
  lines.push(`# Search: "${query}"`);
  lines.push(`Tokens: ${tokens.join(", ")}`);
  const showing = results.length < totalMatches ? `, showing top ${results.length}` : "";
  lines.push(`Found ${totalMatches} tenet${totalMatches === 1 ? "" : "s"}${showing}.`);
  lines.push("");
  if (results.length === 0) {
    lines.push("No matches.");
    return lines.join("\n");
  }
  lines.push("| Score | ID | Name | Category | Rule |");
  lines.push("|-------|----|----|----------|------|");
  for (const r of results) {
    const rule = (r.rule || "").replace(/\|/g, "\\|");
    lines.push(`| ${r.score} | **${r.id}** | ${r.name} | ${r.category} | ${rule} |`);
  }
  return lines.join("\n");
}

// ─── Utilities ─────────────────────────────────────────────────────────────

/**
 * Count non-overlapping occurrences of a substring. Used by searchTenets to
 * score body hits.
 */
function countOccurrences(haystack, needle) {
  if (!needle) return 0;
  let count = 0;
  let pos = 0;
  while ((pos = haystack.indexOf(needle, pos)) !== -1) {
    count++;
    pos += needle.length;
  }
  return count;
}

/**
 * Reset the mtime cache. Primarily for tests — tools themselves rely on
 * mtime-based invalidation which is cheap and correct.
 */
export function resetCache() {
  cachedCatalog = null;
  cachedMtimes = null;
}

// ─── Write Surface: History Log ────────────────────────────────────────────

/**
 * Path to the append-only history log emitted by every write tool in this
 * module. Lazily created on first append; the header is frozen below to
 * keep all entries discoverable via a single regex.
 */
export const TENETS_HISTORY_REL_PATH = "docs/reference/TENETS-HISTORY.md";
export const TENETS_INDEX_REL_PATH = "docs/reference/TENETS.md";

const HISTORY_HEADER = [
  "# Tenet & Violation Change History",
  "",
  "> ⛔ **APPEND-ONLY CHANGELOG** — emitted by the tenet/violation write tools in",
  "> `.claude/mcp/helpers/tenets.mjs`. Do NOT edit existing rows by hand; new rows",
  "> are appended automatically whenever a write tool succeeds.",
  "",
  "Each row records one mutation to `TENETS.md` or a tenet category file. `Tool`",
  "is the helper / MCP tool that performed the write. `Target` is the affected",
  "tenet id (or comma-separated list of cited ids for violation rows). `Summary`",
  "is a one-line description of what changed.",
  "",
  "| Timestamp | Tool | Target | Summary |",
  "|-----------|------|--------|---------|",
  "",
].join("\n");

/**
 * Escape pipes and newlines for safe inclusion in a markdown table cell.
 * Called only inside history-row formatting; payload text in TENETS.md is
 * pipe-rejected upstream so it never reaches this path.
 */
function sanitizeCell(value) {
  return String(value).replace(/\r?\n/g, " ").replace(/\|/g, "\\|").trim();
}

/**
 * Append one structured entry to TENETS-HISTORY.md. Creates the file with the
 * frozen header on first call; subsequent calls append a single row.
 *
 * Does NOT check isPathProtected — that gate belongs to the MCP tool layer,
 * not the helper. Direct callers (smoke tests, future maintenance scripts)
 * are responsible for ensuring the target path is writable.
 *
 * Returns { path, entry, created } on success.
 */
export async function appendHistory({ tool, target, summary }) {
  if (!tool || typeof tool !== "string") {
    throw new Error("appendHistory: 'tool' is required");
  }
  if (typeof target !== "string") {
    throw new Error("appendHistory: 'target' must be a string (may be empty)");
  }
  if (!summary || typeof summary !== "string") {
    throw new Error("appendHistory: 'summary' is required");
  }

  const path = absolute(TENETS_HISTORY_REL_PATH);
  const timestamp = new Date().toISOString();
  const row = `| ${timestamp} | \`${sanitizeCell(tool)}\` | ${sanitizeCell(target)} | ${sanitizeCell(summary)} |\n`;

  let created = false;
  let existingContent = "";
  try {
    await access(path, constants.R_OK);
    existingContent = await readFile(path, "utf-8");
  } catch {
    // File does not exist yet — seed with the frozen header.
    existingContent = HISTORY_HEADER;
    created = true;
  }

  // Ensure the existing content ends with a newline so the new row lines up
  // in the table without dangling whitespace.
  if (existingContent.length > 0 && !existingContent.endsWith("\n")) {
    existingContent += "\n";
  }

  await writeFile(path, existingContent + row, "utf-8");

  return {
    path: TENETS_HISTORY_REL_PATH,
    entry: { timestamp, tool, target, summary },
    created,
  };
}

// ─── Write Surface: Validation & Row Placement ─────────────────────────────

const TENET_ID_RE = /^T\d+$/;

/**
 * Normalize a tenet id so `"t4"`, `"T4"`, and `" t4 "` all resolve to `"T4"`.
 * Returns the canonical `T{N}` form on success, or null if the input is not
 * a valid tenet id shape after normalization.
 *
 * Accepts case-insensitive `T` prefix + digits. Does NOT accept bare numbers
 * (`"4"` → null) because id-less inputs hide typos at too many call sites.
 */
function normalizeTenetId(id) {
  if (typeof id !== "string") return null;
  const trimmed = id.trim();
  if (trimmed.length === 0) return null;
  // Uppercase handles `t4` → `T4`; the regex validates shape.
  const upper = trimmed.toUpperCase();
  if (!TENET_ID_RE.test(upper)) return null;
  return upper;
}

/**
 * Reject input strings that would corrupt the Quick Reference table.
 *  - Empty / whitespace-only strings are meaningless rows.
 *  - Pipes collide with table column delimiters.
 *  - Newlines break row structure (markdown tables are single-line).
 */
function validateRowText(label, value) {
  if (typeof value !== "string" || value.trim().length === 0) {
    return `${label} is required and must be a non-empty string.`;
  }
  if (value.includes("|")) {
    return `${label} must not contain '|' characters (they collide with markdown table columns).`;
  }
  if (/\r|\n/.test(value)) {
    return `${label} must not contain newline characters (markdown table rows are single-line).`;
  }
  return null;
}

/**
 * Given the parsed violations array and a target tenet, return the 0-based
 * line index at which a new row should be spliced so that it follows the
 * preferred placement rule:
 *
 *   1. After the LAST row whose primary (first-listed) tenet == target
 *   2. Else after the LAST row whose tenets list contains target
 *   3. Else after the LAST row in the table (end-of-table fallback)
 *
 * Returns null when the violations array is empty — indicates the caller
 * could not locate the table at all.
 */
function findInsertionLineIdx(violations, targetTenet) {
  if (!violations || violations.length === 0) return null;

  // Priority 1: primary-citation match
  let last = null;
  for (const v of violations) {
    if (v.tenets[0] === targetTenet) last = v;
  }
  if (last) return last.sourceLine; // splice AT sourceLine (1-based) == after row in 0-based

  // Priority 2: any-citation match
  for (const v of violations) {
    if (v.tenets.includes(targetTenet)) last = v;
  }
  if (last) return last.sourceLine;

  // Priority 3: end of table
  return violations[violations.length - 1].sourceLine;
}

// ─── Write Surface: addViolation ───────────────────────────────────────────

/**
 * Append a new row to the Quick Reference violations table in TENETS.md.
 *
 * Placement: see findInsertionLineIdx above.
 *
 * Returns { tenet, violation, fix, insertedAtLine, path } on success, or
 * { error } on validation failure (unknown tenet, duplicate row, invalid
 * text). Emits a `TENETS-HISTORY.md` entry when the write succeeds.
 *
 * Does NOT check isPathProtected — that check is the MCP tool's responsibility.
 */
export async function addViolation({ tenet, violation, fix }) {
  const normalizedTenet = normalizeTenetId(tenet);
  if (!normalizedTenet) {
    return { error: `Invalid tenet id "${tenet}" — must match /^T\\d+$/ (e.g., "T4").` };
  }
  tenet = normalizedTenet; // canonicalize for the rest of the function
  const vError = validateRowText("violation", violation);
  if (vError) return { error: vError };
  const fError = validateRowText("fix", fix);
  if (fError) return { error: fError };

  // Force a fresh parse — mtime cache can hide between-call writes to TENETS.md.
  const catalog = await getTenetCatalog({ force: true });

  if (!catalog.tenets.has(tenet)) {
    const known = [...catalog.tenets.keys()]
      .sort((a, b) => parseInt(a.slice(1), 10) - parseInt(b.slice(1), 10))
      .join(", ");
    return { error: `Tenet ${tenet} is not registered. Known ids: ${known}.` };
  }

  const trimmedViolation = violation.trim();
  const duplicate = catalog.violations.find((v) => v.violation === trimmedViolation);
  if (duplicate) {
    return {
      error:
        `A violation row with text "${trimmedViolation}" already exists at line ` +
        `${duplicate.sourceLine} (cites ${duplicate.tenets.join(", ")}). ` +
        `Use edit_violation to change its fix.`,
    };
  }

  const insertIdx = findInsertionLineIdx(catalog.violations, tenet);
  if (insertIdx === null) {
    return {
      error:
        "Could not locate the Quick Reference violations table in TENETS.md — " +
        "the file appears to have zero existing violation rows. Manual intervention required.",
    };
  }

  const indexPath = absolute(TENETS_INDEX_REL_PATH);
  const content = await readFile(indexPath, "utf-8");
  const lines = content.split("\n");
  const newRow = `| ${trimmedViolation} | ${tenet} | ${fix.trim()} |`;
  lines.splice(insertIdx, 0, newRow);
  await writeFile(indexPath, lines.join("\n"), "utf-8");
  resetCache();

  await appendHistory({
    tool: "add_violation",
    target: tenet,
    summary: `Added row "${truncateForSummary(trimmedViolation, 80)}" citing ${tenet}`,
  });

  return {
    tenet,
    violation: trimmedViolation,
    fix: fix.trim(),
    insertedAtLine: insertIdx + 1, // 1-based line number of the new row
    path: TENETS_INDEX_REL_PATH,
  };
}

// ─── Write Surface: editViolation ──────────────────────────────────────────

/**
 * Replace the fix column of an existing violation row (matched by EXACT
 * violation text). Preserves the violation text and the cited-tenets column.
 *
 * Returns { violation, tenets, oldFix, newFix, lineEdited, path } on success,
 * or { error } if the row is not found or inputs are invalid. Emits a history
 * entry on success.
 */
export async function editViolation({ violation, newFix }) {
  const vError = validateRowText("violation", violation);
  if (vError) return { error: vError };
  const fError = validateRowText("newFix", newFix);
  if (fError) return { error: fError };

  const catalog = await getTenetCatalog({ force: true });
  const trimmedViolation = violation.trim();
  const existing = catalog.violations.find((v) => v.violation === trimmedViolation);
  if (!existing) {
    return {
      error:
        `No violation row found with text "${trimmedViolation}". ` +
        `Use list_violations to find the exact text.`,
    };
  }

  const trimmedFix = newFix.trim();
  if (trimmedFix === existing.fix) {
    return {
      error:
        `newFix is identical to the existing fix at line ${existing.sourceLine}. ` +
        `Nothing to change.`,
    };
  }

  const indexPath = absolute(TENETS_INDEX_REL_PATH);
  const content = await readFile(indexPath, "utf-8");
  const lines = content.split("\n");
  const lineIdx = existing.sourceLine - 1; // 1-based → 0-based
  const replacement = `| ${existing.violation} | ${existing.tenets.join(", ")} | ${trimmedFix} |`;
  lines[lineIdx] = replacement;
  await writeFile(indexPath, lines.join("\n"), "utf-8");
  resetCache();

  await appendHistory({
    tool: "edit_violation",
    target: existing.tenets.join(", "),
    summary:
      `Edited fix for "${truncateForSummary(trimmedViolation, 60)}": ` +
      `"${truncateForSummary(existing.fix, 40)}" → "${truncateForSummary(trimmedFix, 40)}"`,
  });

  return {
    violation: trimmedViolation,
    tenets: existing.tenets,
    oldFix: existing.fix,
    newFix: trimmedFix,
    lineEdited: existing.sourceLine,
    path: TENETS_INDEX_REL_PATH,
  };
}

// ─── Write Surface: removeViolation ────────────────────────────────────────

/**
 * Delete an existing violation row (matched by EXACT violation text).
 *
 * Returns { violation, tenets, removedFix, lineRemoved, path } on success,
 * or { error } if the row is not found. Emits a history entry on success.
 */
export async function removeViolation({ violation }) {
  const vError = validateRowText("violation", violation);
  if (vError) return { error: vError };

  const catalog = await getTenetCatalog({ force: true });
  const trimmedViolation = violation.trim();
  const existing = catalog.violations.find((v) => v.violation === trimmedViolation);
  if (!existing) {
    return {
      error:
        `No violation row found with text "${trimmedViolation}". ` +
        `Use list_violations to find the exact text.`,
    };
  }

  const indexPath = absolute(TENETS_INDEX_REL_PATH);
  const content = await readFile(indexPath, "utf-8");
  const lines = content.split("\n");
  const lineIdx = existing.sourceLine - 1;
  lines.splice(lineIdx, 1);
  await writeFile(indexPath, lines.join("\n"), "utf-8");
  resetCache();

  await appendHistory({
    tool: "remove_violation",
    target: existing.tenets.join(", "),
    summary:
      `Removed row "${truncateForSummary(trimmedViolation, 80)}" citing ${existing.tenets.join(", ")}`,
  });

  return {
    violation: trimmedViolation,
    tenets: existing.tenets,
    removedFix: existing.fix,
    lineRemoved: existing.sourceLine,
    path: TENETS_INDEX_REL_PATH,
  };
}

// ─── Write Surface: Internal Helpers ───────────────────────────────────────

function truncateForSummary(text, maxLen) {
  if (text.length <= maxLen) return text;
  return text.slice(0, maxLen - 1) + "…";
}

// ─── Write Surface: Tenet Body/Heading Mutations ───────────────────────────
//
// editTenet / addTenet / removeTenet / renumberTenet — mutate the sentinel
// blocks in TENETS.md and the category files. Category is derived from the
// source file (no separate category map to persist — see TENETS_MD_ID_CATEGORIES
// above). Each mutation that changes the set of tenets (add/remove/renumber)
// also updates three derived sections in TENETS.md:
//   1. T0 body prose category lists (the `- \`FOUNDATION TENETS\` - for T4, T5, ...` bullets)
//   2. The "Tenet Categories" navigation table (the rows under `## Tenet Categories`)
//   3. The per-category summary tables (one table per autoMaintained category)
//
// All write tools assume the caller has already gone through isPathProtected.
// That gate lives in the MCP tool layer (server.mjs), matching the pattern
// used by edit_file / write_file / move_lines and by Step 3's violation writes.

// ─── Derived-Content Maintenance ───────────────────────────────────────────

/**
 * Escape a string for use inside a regex. Handles category labels that may
 * contain special chars (colons, parens, brackets, etc.).
 */
function escapeRegex(str) {
  return str.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/**
 * Regenerate T0's prose category-to-ids lists in-place on `indexLines`.
 *
 * T0's body contains bullets like:
 *   - `FOUNDATION TENETS` - for T4, T5, T6, T13, T15, T18, T27, T28, T29, T32
 *   - `IMPLEMENTATION TENETS` - for T3, T7, T8, T9, T14, T17, ...
 *   - `QUALITY TENETS` - for T10, T11, T12, T16, T19, T22
 *
 * Tenets are grouped by `sourceCodeCategoryName` (which merges
 * implementation-behavior + implementation-data under "IMPLEMENTATION TENETS"),
 * sorted by number, and written back into the matching bullet line.
 *
 * Tenets whose category has no `sourceCodeCategoryName` (meta / schema-rules /
 * service-hierarchy) are NOT emitted to any list — those categories have
 * their own structural mentions elsewhere in T0's body.
 */
function regenerateT0ProseLists(indexLines, logicalTenets) {
  const groupsByLabel = new Map();
  for (const t of logicalTenets.values()) {
    const meta = CATEGORIES[t.category];
    if (!meta || !meta.sourceCodeCategoryName) continue;
    const label = meta.sourceCodeCategoryName;
    if (!groupsByLabel.has(label)) groupsByLabel.set(label, []);
    groupsByLabel.get(label).push(t);
  }
  for (const ts of groupsByLabel.values()) {
    ts.sort((a, b) => a.number - b.number);
  }

  let touchedLabels = 0;
  for (const [label, ts] of groupsByLabel) {
    const ids = ts.map((t) => t.id).join(", ");
    const escapedLabel = escapeRegex(label);
    // Match: `- `{LABEL}` - for T4, T5, ...`
    const re = new RegExp(
      `^(-\\s+\`${escapedLabel}\`\\s+-\\s+for\\s+)(T\\d+(?:\\s*,\\s*T\\d+)*)(.*)$`,
    );
    for (let i = 0; i < indexLines.length; i++) {
      const m = indexLines[i].match(re);
      if (m) {
        indexLines[i] = `${m[1]}${ids}${m[3]}`;
        touchedLabels++;
        break;
      }
    }
  }
  return touchedLabels;
}

/**
 * Regenerate "Tenet Categories" navigation-table rows in-place on `indexLines`.
 *
 * Each row for an auto-maintained category looks like:
 *   | [**{navLabel}**](tenets/{FILE}.md) | T4, T5, ... | {whenToReference} |
 *
 * Tenets are grouped by category, sorted by number, and their ids joined with
 * `, ` into the row's second column. First column (label link) and third
 * column (when-to-reference) are preserved byte-exact.
 *
 * Rows for non-auto-maintained categories (meta, schema-rules, service-hierarchy)
 * are untouched; their contents are hand-curated.
 */
function regenerateNavTableRows(indexLines, logicalTenets) {
  const groupsByCat = new Map();
  for (const t of logicalTenets.values()) {
    if (!groupsByCat.has(t.category)) groupsByCat.set(t.category, []);
    groupsByCat.get(t.category).push(t);
  }
  for (const ts of groupsByCat.values()) {
    ts.sort((a, b) => a.number - b.number);
  }

  let touchedRows = 0;
  for (const [catId, ts] of groupsByCat) {
    const meta = CATEGORIES[catId];
    if (!meta || !meta.navLabel) continue;
    const ids = ts.map((t) => t.id).join(", ");
    const escapedLabel = escapeRegex(meta.navLabel);
    // Match: `| [**{navLabel}**](path) | T4, T5, ... | ... |`
    const re = new RegExp(
      `^(\\|\\s*\\[\\*\\*${escapedLabel}\\*\\*\\]\\([^)]+\\)\\s*\\|\\s*)(T\\d+(?:\\s*,\\s*T\\d+)*)(\\s*\\|)`,
    );
    for (let i = 0; i < indexLines.length; i++) {
      const m = indexLines[i].match(re);
      if (m) {
        indexLines[i] = `${m[1]}${ids}${m[3]}${indexLines[i].slice(m[0].length)}`;
        touchedRows++;
        break;
      }
    }
  }
  return touchedRows;
}

/**
 * Insert a new row into the per-category summary table for `category`.
 *
 * Finds the section by `summaryTableHeading`, walks the table rows in
 * number order, and inserts the new row at the correct sorted position.
 * Returns { touched, insertedAtLine } or { touched: false, reason }.
 */
function insertSummaryTableRow(indexLines, category, id, number, name, summaryRule) {
  const meta = CATEGORIES[category];
  if (!meta || !meta.summaryTableHeading) {
    return { touched: false, reason: `category "${category}" has no summaryTableHeading` };
  }

  const headingMarker = `## ${meta.summaryTableHeading}`;
  let sectionStart = -1;
  for (let i = 0; i < indexLines.length; i++) {
    if (indexLines[i].trim() === headingMarker) {
      sectionStart = i;
      break;
    }
  }
  if (sectionStart === -1) {
    return { touched: false, reason: `section heading "${headingMarker}" not found` };
  }

  // Find the next H2 (or end of file) to bound the section.
  let sectionEnd = indexLines.length;
  for (let i = sectionStart + 1; i < indexLines.length; i++) {
    if (/^##\s+/.test(indexLines[i])) { sectionEnd = i; break; }
  }

  // Locate the table header `| # | Name | Core Rule |`, then skip header + separator.
  let tableRowStart = -1;
  for (let i = sectionStart + 1; i < sectionEnd; i++) {
    if (/^\|\s*#\s*\|\s*Name\s*\|\s*Core Rule\s*\|\s*$/.test(indexLines[i])) {
      tableRowStart = i + 2;
      break;
    }
  }
  if (tableRowStart === -1) {
    return { touched: false, reason: `table header not found in "${headingMarker}" section` };
  }

  // Walk rows, find sorted insertion point.
  let insertIdx = tableRowStart;
  for (let i = tableRowStart; i < sectionEnd; i++) {
    const m = indexLines[i].match(SUMMARY_ROW_RE);
    if (!m) break; // end of table
    const rowNumber = parseInt(m[1], 10);
    if (rowNumber < number) {
      insertIdx = i + 1;
    } else {
      break;
    }
  }

  const escapedName = name.replace(/\|/g, "\\|");
  const escapedRule = (summaryRule || "").replace(/\|/g, "\\|");
  const newRow = `| **${id}** | ${escapedName} | ${escapedRule} |`;
  indexLines.splice(insertIdx, 0, newRow);
  return { touched: true, insertedAtLine: insertIdx + 1 };
}

/**
 * Remove a row from any per-category summary table by tenet id. Captures the
 * row's name and summary rule so the caller can re-insert under a new id
 * (used by renumberTenet).
 *
 * Returns { touched, removedAtLine, capturedRow: { name, summaryRule } }
 * on success, or { touched: false } if no matching row was found.
 */
function removeSummaryTableRow(indexLines, id) {
  const idEsc = escapeRegex(id);
  // Match: `| **T{id}** | {name} | {rule} |` — capture name and rule.
  const re = new RegExp(`^\\|\\s*\\*\\*${idEsc}\\*\\*\\s*\\|\\s*(.+?)\\s*\\|\\s*(.+?)\\s*\\|\\s*$`);
  for (let i = 0; i < indexLines.length; i++) {
    const m = indexLines[i].match(re);
    if (m) {
      const capturedRow = { name: m[1], summaryRule: m[2] };
      indexLines.splice(i, 1);
      return { touched: true, removedAtLine: i + 1, capturedRow };
    }
  }
  return { touched: false };
}

/**
 * Trim leading and trailing blank lines from a body string so the surrounding
 * structural whitespace (blank line after heading, no blank before close
 * sentinel) is deterministic regardless of how carefully the caller formatted
 * their input.
 */
function trimBodyBlankLines(body) {
  return body.replace(/^(\s*\n)+/, "").replace(/(\n\s*)+$/, "");
}

// ─── Write Surface: editTenet ──────────────────────────────────────────────

/**
 * Replace a tenet's body content between the heading and the close sentinel.
 * The sentinels and the `## Tenet N: ...` heading are PRESERVED — this tool
 * edits only the prose, tables, and code blocks that live inside the block.
 *
 * Body contract:
 *   - Content that replaces everything from (heading + 1 blank line) to (line
 *     immediately before close sentinel).
 *   - A single blank line is always inserted between the heading and the new
 *     body for readability.
 *   - Surrounding blank lines in the input are trimmed — the caller does not
 *     need to hand-manage leading/trailing whitespace.
 *
 * Rejects the call when:
 *   - `id` is not a known tenet
 *   - `body` is empty / whitespace-only
 *   - the first non-blank line of `body` starts with `## Tenet ` (guard against
 *     the common mistake of including the heading in the body payload)
 *
 * Returns { id, path, linesReplaced, oldBodyLength, newBodyLength } on success.
 */
export async function editTenet({ id, body }) {
  const normalized = normalizeTenetId(id);
  if (!normalized) {
    return { error: `Invalid tenet id "${id}" — must match /^T\\d+$/ (e.g., "T4").` };
  }
  id = normalized;
  if (typeof body !== "string" || body.trim().length === 0) {
    return { error: `body is required and must be a non-empty string.` };
  }

  // Guard against the "caller included heading in body" mistake.
  const firstNonBlank = body.split("\n").find((l) => l.trim().length > 0);
  if (firstNonBlank && TENET_HEADING_RE.test(firstNonBlank)) {
    return {
      error:
        `body must NOT include the \`## Tenet N: ...\` heading line. ` +
        `Only the post-heading content is passed; the heading is preserved by edit_tenet. ` +
        `Use renumber_tenet to change the number; headings remain fixed otherwise.`,
    };
  }

  const catalog = await getTenetCatalog({ force: true });
  const t = catalog.tenets.get(id);
  if (!t) {
    return { error: `Tenet ${id} not found in any source file.` };
  }

  const filePath = absolute(t.sourceFile);
  const content = await readFile(filePath, "utf-8");
  const lines = content.split("\n");
  const openIdx = t.openSentinelLine - 1;
  const closeIdx = t.closeSentinelLine - 1;

  // Locate the heading line inside the block.
  let headingIdx = -1;
  for (let i = openIdx + 1; i < closeIdx; i++) {
    if (TENET_HEADING_RE.test(lines[i])) {
      headingIdx = i;
      break;
    }
  }
  if (headingIdx === -1) {
    return {
      error:
        `Could not locate \`## Tenet N: ...\` heading inside ${t.sourceFile} ` +
        `between lines ${t.openSentinelLine} and ${t.closeSentinelLine}. ` +
        `The file may be corrupted — re-run list_tenets to re-verify structure.`,
    };
  }

  const oldBody = lines.slice(headingIdx + 1, closeIdx).join("\n");
  const newBodyLines = trimBodyBlankLines(body).split("\n");

  // Rebuild: keep lines up-to-and-including heading, insert 1 blank line,
  // then body, then keep from close sentinel onward.
  const newLines = [
    ...lines.slice(0, headingIdx + 1),
    "",
    ...newBodyLines,
    ...lines.slice(closeIdx),
  ];

  await writeFile(filePath, newLines.join("\n"), "utf-8");
  resetCache();

  const result = {
    id,
    path: t.sourceFile,
    linesReplaced: { start: headingIdx + 2, end: t.closeSentinelLine - 1 }, // 1-based inclusive
    oldBodyLength: oldBody.length,
    newBodyLength: newBodyLines.join("\n").length,
  };

  await appendHistory({
    tool: "edit_tenet",
    target: id,
    summary:
      `Edited body of ${id} (${t.name}) in ${t.sourceFile}: ` +
      `${oldBody.length} → ${result.newBodyLength} chars`,
  });

  return result;
}

// ─── Write Surface: addTenet ───────────────────────────────────────────────

/**
 * Insert a new sentinel-wrapped tenet block into the target category's file.
 *
 * Insertion placement:
 *   1. PREFER_SUCCESSOR — if any existing tenet in the target file has a
 *      higher number, insert before its open sentinel. The new block carries
 *      its trailing `---` separator so the successor stays cleanly delimited.
 *   2. FALLBACK_PREDECESSOR — otherwise, insert AFTER the close sentinel of
 *      the highest-numbered existing tenet, with a LEADING `---` separator
 *      so the predecessor stays cleanly delimited.
 *   3. EMPTY_FILE — if the target file has zero existing tenets, reject;
 *      the file's structural skeleton must be authored by hand first.
 *
 * Derived sections in TENETS.md are maintained atomically: the T0 prose
 * category list, the "Tenet Categories" navigation row, and the per-category
 * summary table are all updated so the file stays internally consistent
 * after the call returns.
 *
 * Structural categories (meta, schema-rules, service-hierarchy) are rejected —
 * those tenets live in TENETS.md alongside T0/T1/T2 and require hand-authoring
 * plus a manual update to TENETS_MD_ID_CATEGORIES.
 *
 * Returns { id, category, path, insertedAtLines: {start, end}, derived } on success.
 */
export async function addTenet({ category, number, name, severity, body, summaryRule }) {
  if (typeof category !== "string" || !(category in CATEGORIES)) {
    return {
      error:
        `Unknown category "${category}". ` +
        `Valid: ${Object.keys(CATEGORIES).join(", ")}.`,
    };
  }
  const catMeta = CATEGORIES[category];
  if (!catMeta.autoMaintained) {
    return {
      error:
        `add_tenet does not support category "${category}" — T0/T1/T2 and any ` +
        `other tenets hosted in TENETS.md are structurally special and must be ` +
        `authored by hand (with a matching TENETS_MD_ID_CATEGORIES entry). ` +
        `Valid categories for add_tenet: ${Object.entries(CATEGORIES)
          .filter(([, m]) => m.autoMaintained)
          .map(([id]) => id)
          .join(", ")}.`,
    };
  }
  if (!Number.isInteger(number) || number < 0) {
    return { error: `number must be a non-negative integer, got ${number}.` };
  }
  if (typeof name !== "string" || name.trim().length === 0) {
    return { error: `name is required and must be a non-empty string.` };
  }
  if (severity !== null && severity !== undefined && typeof severity !== "string") {
    return { error: `severity must be a string, null, or omitted.` };
  }
  if (typeof body !== "string" || body.trim().length === 0) {
    return { error: `body is required and must be a non-empty string.` };
  }
  if (
    summaryRule !== null &&
    summaryRule !== undefined &&
    (typeof summaryRule !== "string" || summaryRule.includes("|") || /\r|\n/.test(summaryRule))
  ) {
    return {
      error:
        `summaryRule must be a single-line string without '|' characters ` +
        `(markdown table cell restriction), or omitted (in which case it is ` +
        `derived from body via extractRule).`,
    };
  }

  const id = `T${number}`;
  const catalog = await getTenetCatalog({ force: true });

  if (catalog.tenets.has(id)) {
    const existing = catalog.tenets.get(id);
    return {
      error:
        `Tenet ${id} already exists in ${existing.sourceFile} (line ${existing.openSentinelLine}). ` +
        `Use edit_tenet to change its body, renumber_tenet to give it a different id, ` +
        `or remove_tenet first if you want to replace it entirely.`,
    };
  }

  const targetFile = catMeta.file;
  const existingInFile = [...catalog.tenets.values()]
    .filter((t) => t.sourceFile === targetFile)
    .sort((a, b) => a.number - b.number);

  if (existingInFile.length === 0) {
    return {
      error:
        `Category file ${targetFile} has zero existing tenets. ` +
        `add_tenet cannot bootstrap an empty category — author at least ` +
        `one tenet by hand first so the structural skeleton is in place.`,
    };
  }

  // Pick insertion strategy.
  const successor = existingInFile.find((t) => t.number > number);
  const predecessor = [...existingInFile].reverse().find((t) => t.number < number);

  const filePath = absolute(targetFile);
  const content = await readFile(filePath, "utf-8");
  const lines = content.split("\n");

  const trimmedName = name.trim();
  const normalizedSeverity =
    severity && severity.trim().length > 0 ? severity.trim() : null;
  const heading = normalizedSeverity
    ? `## Tenet ${number}: ${trimmedName} (${normalizedSeverity})`
    : `## Tenet ${number}: ${trimmedName}`;
  const trimmedBody = trimBodyBlankLines(body);
  const bodyLines = trimmedBody.split("\n");

  // Derive summaryRule from body if the caller didn't provide one.
  const resolvedSummaryRule = (() => {
    if (summaryRule && summaryRule.trim().length > 0) return summaryRule.trim();
    // Use the body lines (without heading) via extractRule's heuristics.
    const derived = extractRule(bodyLines);
    return derived || "";
  })();

  let spliceIdx;
  let newBlock;
  let strategy;

  if (successor) {
    // PREFER_SUCCESSOR: splice before successor's open sentinel, carry the
    // trailing separator so the successor stays delimited.
    spliceIdx = successor.openSentinelLine - 1; // 0-based
    newBlock = [
      `<!-- TENET:${id} -->`,
      heading,
      "",
      ...bodyLines,
      `<!-- /TENET:${id} -->`,
      "",
      "---",
      "",
    ];
    strategy = `before-successor ${successor.id}`;
  } else {
    // FALLBACK_PREDECESSOR: splice after predecessor's close sentinel, carry
    // the leading separator so the predecessor stays delimited.
    spliceIdx = predecessor.closeSentinelLine; // 0-based position AFTER close sentinel
    newBlock = [
      "",
      "---",
      "",
      `<!-- TENET:${id} -->`,
      heading,
      "",
      ...bodyLines,
      `<!-- /TENET:${id} -->`,
    ];
    strategy = `after-predecessor ${predecessor.id}`;
  }

  lines.splice(spliceIdx, 0, ...newBlock);
  await writeFile(filePath, lines.join("\n"), "utf-8");

  // ── Maintain derived sections in TENETS.md ────────────────────────────
  // Build the logical post-mutation tenet set and feed it to the regenerators.
  const logicalTenets = new Map(catalog.tenets);
  logicalTenets.set(id, {
    id, number, name: trimmedName, severity: normalizedSeverity,
    category, sourceFile: targetFile,
  });

  const indexPath = absolute(TENETS_INDEX_REL_PATH);
  const indexContent = await readFile(indexPath, "utf-8");
  const indexLines = indexContent.split("\n");

  const t0Touched = regenerateT0ProseLists(indexLines, logicalTenets);
  const navTouched = regenerateNavTableRows(indexLines, logicalTenets);
  const summaryResult = insertSummaryTableRow(
    indexLines, category, id, number, trimmedName, resolvedSummaryRule,
  );

  await writeFile(indexPath, indexLines.join("\n"), "utf-8");
  resetCache();

  await appendHistory({
    tool: "add_tenet",
    target: id,
    summary:
      `Added ${id} "${trimmedName}"${normalizedSeverity ? ` (${normalizedSeverity})` : ""} ` +
      `to ${category} (${targetFile}), strategy=${strategy}; derived: ` +
      `t0=${t0Touched}, nav=${navTouched}, summary=${summaryResult.touched ? 1 : 0}`,
  });

  return {
    id,
    number,
    category,
    name: trimmedName,
    severity: normalizedSeverity,
    summaryRule: resolvedSummaryRule,
    path: targetFile,
    strategy,
    insertedAtLines: {
      start: spliceIdx + 1, // 1-based
      end: spliceIdx + newBlock.length,
    },
    derived: {
      t0ProseListsTouched: t0Touched,
      navTableRowsTouched: navTouched,
      summaryRowInserted: summaryResult.touched,
      summaryRowInsertedAt: summaryResult.insertedAtLine || null,
      summaryRowSkipReason: summaryResult.touched ? null : summaryResult.reason || null,
    },
  };
}

// ─── Write Surface: removeTenet ────────────────────────────────────────────

/**
 * Remove a tenet's sentinel block from its category file. Also consumes the
 * trailing `\n\n---\n\n` separator pattern if present, to avoid doubled
 * separators in the resulting file.
 *
 * Derived sections in TENETS.md are maintained atomically: the T0 prose
 * category list, the "Tenet Categories" navigation row, and the per-category
 * summary table row are all updated so the file stays internally consistent
 * after the call returns.
 *
 * Does NOT touch:
 *   - Quick Reference citations that name the removed id (Step 5's
 *     validate_tenets flags dangling refs)
 *   - prose cross-references ("See T4 for details") inside other tenet bodies
 *
 * Returns { id, name, path, removedLines: {start, end}, derived } on success.
 */
export async function removeTenet({ id, cleanupCitations, confirm }) {
  const normalized = normalizeTenetId(id);
  if (!normalized) {
    return { error: `Invalid tenet id "${id}" — must match /^T\\d+$/ (e.g., "T4").` };
  }
  id = normalized;

  const catalog = await getTenetCatalog({ force: true });
  const t = catalog.tenets.get(id);
  if (!t) {
    return { error: `Tenet ${id} not found in any source file.` };
  }

  // ── Citation cleanup — two-step dance ─────────────────────────────────
  // Collect the rows that would be affected upfront. The first invocation
  // with cleanupCitations:true surfaces the list as a forced dry-run; the
  // second invocation must explicitly confirm before any violation rows
  // are removed or rewritten. See server.mjs remove_tenet registration —
  // the `confirm` parameter is intentionally NOT documented at the tool
  // level so agents always encounter the dry-run first.
  const cleanup = cleanupCitations === true;
  const rowsCiting = cleanup
    ? catalog.violations.filter((v) => v.tenets.includes(id))
    : [];
  // Partition: rows that cite ONLY this tenet (would be deleted) vs. rows
  // that cite this tenet + others (would be rewritten with this id stripped).
  const soleCitations = rowsCiting.filter((v) => v.tenets.length === 1);
  const sharedCitations = rowsCiting.filter((v) => v.tenets.length > 1);

  if (cleanup && confirm !== true) {
    // Forced dry-run. Return the cleanup preview as an "error" so the MCP
    // tool layer surfaces it prominently (same shape as other rejections).
    if (rowsCiting.length === 0) {
      // Nothing to clean — proceed directly with the normal removal. The
      // caller will see the standard "no orphan citations" result without
      // needing a second invocation.
    } else {
      const preview = [];
      preview.push(
        `cleanupCitations:true requires explicit confirmation before ` +
        `Quick Reference rows are modified. ${rowsCiting.length} row(s) cite ${id}:`,
      );
      preview.push("");
      if (soleCitations.length > 0) {
        preview.push(`  Would DELETE ${soleCitations.length} row(s) citing ONLY ${id}:`);
        for (const v of soleCitations) {
          preview.push(`    - line ${v.sourceLine}: "${truncateForSummary(v.violation, 80)}"`);
        }
      }
      if (sharedCitations.length > 0) {
        if (soleCitations.length > 0) preview.push("");
        preview.push(`  Would REWRITE ${sharedCitations.length} row(s) (removing ${id} from citation list):`);
        for (const v of sharedCitations) {
          const others = v.tenets.filter((x) => x !== id).join(", ");
          preview.push(
            `    - line ${v.sourceLine}: "${truncateForSummary(v.violation, 60)}" ` +
            `(keeps: ${others})`,
          );
        }
      }
      preview.push("");
      preview.push(
        `Re-run remove_tenet with cleanupCitations:true AND confirm:true to proceed. ` +
        `The tenet itself has NOT been removed — this invocation only previews.`,
      );
      return {
        error: preview.join("\n"),
        preview: {
          soleCitations: soleCitations.map((v) => ({
            violation: v.violation,
            tenets: v.tenets,
            fix: v.fix,
            sourceLine: v.sourceLine,
          })),
          sharedCitations: sharedCitations.map((v) => ({
            violation: v.violation,
            tenets: v.tenets,
            fix: v.fix,
            sourceLine: v.sourceLine,
          })),
        },
      };
    }
  }

  const filePath = absolute(t.sourceFile);
  const content = await readFile(filePath, "utf-8");
  const lines = content.split("\n");

  const startIdx = t.openSentinelLine - 1; // 0-based
  const endIdx = t.closeSentinelLine - 1; // 0-based inclusive

  let removeCount = endIdx - startIdx + 1; // block lines
  // Consume trailing `\n\n---\n\n` pattern if present — avoids double separators.
  if (
    lines[endIdx + 1] === "" &&
    lines[endIdx + 2] === "---" &&
    lines[endIdx + 3] === ""
  ) {
    removeCount += 3; // include blank + `---` + blank lines after the block
  }

  lines.splice(startIdx, removeCount);
  await writeFile(filePath, lines.join("\n"), "utf-8");

  // ── Maintain derived sections in TENETS.md ────────────────────────────
  const logicalTenets = new Map(catalog.tenets);
  logicalTenets.delete(id);

  const isIndexSelf = t.sourceFile === TENETS_INDEX_REL_PATH;
  const indexPath = absolute(TENETS_INDEX_REL_PATH);
  let indexLines;
  if (isIndexSelf) {
    // Sentinel removal already happened in `lines`; reuse that buffer.
    indexLines = lines;
  } else {
    const indexContent = await readFile(indexPath, "utf-8");
    indexLines = indexContent.split("\n");
  }

  // ── Citation cleanup (confirmed) ──────────────────────────────────────
  // ORDER MATTERS: must run BEFORE removeSummaryTableRow. That function
  // splices a line out of the same indexLines buffer, which shifts every
  // QR table row down by one (summary tables always precede the QR table
  // in TENETS.md). Doing citations first keeps the captured sourceLines
  // stable. Sort by sourceLine descending so each mutation preserves line
  // numbers for rows processed later in the loop. Delete vs. rewrite
  // depends on whether the row cited only this tenet or this-plus-others.
  let citationsDeleted = 0;
  let citationsRewritten = 0;
  if (cleanup && confirm === true && rowsCiting.length > 0) {
    const sorted = [...rowsCiting].sort((a, b) => b.sourceLine - a.sourceLine);
    for (const v of sorted) {
      const lineIdx = v.sourceLine - 1;
      if (v.tenets.length === 1) {
        indexLines.splice(lineIdx, 1);
        citationsDeleted++;
      } else {
        const kept = v.tenets.filter((x) => x !== id);
        const replacement =
          `| ${v.violation} | ${kept.join(", ")} | ${v.fix} |`;
        indexLines[lineIdx] = replacement;
        citationsRewritten++;
      }
    }
  }

  const t0Touched = regenerateT0ProseLists(indexLines, logicalTenets);
  const navTouched = regenerateNavTableRows(indexLines, logicalTenets);
  const summaryResult = removeSummaryTableRow(indexLines, id);

  await writeFile(indexPath, indexLines.join("\n"), "utf-8");
  resetCache();

  const historySummary =
    `Removed ${id} "${t.name}" from ${t.sourceFile} (${removeCount} lines); derived: ` +
    `t0=${t0Touched}, nav=${navTouched}, summary=${summaryResult.touched ? 1 : 0}` +
    (cleanup && confirm === true
      ? `; citations: deleted=${citationsDeleted}, rewritten=${citationsRewritten}`
      : "");

  await appendHistory({
    tool: "remove_tenet",
    target: id,
    summary: historySummary,
  });

  return {
    id,
    name: t.name,
    path: t.sourceFile,
    removedLines: {
      start: startIdx + 1, // 1-based
      end: startIdx + removeCount,
    },
    derived: {
      t0ProseListsTouched: t0Touched,
      navTableRowsTouched: navTouched,
      summaryRowRemoved: summaryResult.touched,
      summaryRowRemovedAt: summaryResult.removedAtLine || null,
    },
    citationCleanup: cleanup
      ? {
          confirmed: confirm === true,
          rowsDeleted: citationsDeleted,
          rowsRewritten: citationsRewritten,
        }
      : null,
  };
}

// ─── Write Surface: renumberTenet ──────────────────────────────────────────

/**
 * Rename a tenet by id, preserving everything else.
 *
 * Updates atomically:
 *   1. Open sentinel: `<!-- TENET:T{old} -->` → `<!-- TENET:T{new} -->`
 *   2. Close sentinel: `<!-- /TENET:T{old} -->` → `<!-- /TENET:T{new} -->`
 *   3. Heading line: `## Tenet {oldN}: Name (SEV)` → `## Tenet {newN}: Name (SEV)`
 *   4. Summary-table row in TENETS.md: `| **T{old}** |` → `| **T{new}** |`
 *      (skipped if the tenet has no summary-table row)
 *   5. Quick Reference citation columns in TENETS.md (scoped to rows parsed
 *      into the `Quick Reference: Common Violations` table only — nav table
 *      rows are handled separately in step 6 to avoid conflicts).
 *   6. T0 prose category lists + "Tenet Categories" nav table row — both
 *      regenerated from the post-rename logical tenet set, so ordering stays
 *      sorted by number.
 *
 * Renumber does NOT touch prose cross-references inside other tenet bodies
 * (e.g., "See T4 for details"). Step 5's validate_tenets can flag those.
 *
 * No separate category map is mutated — category is derived from the source
 * file, which doesn't change on renumber.
 *
 * Returns a detailed report of which file sections were touched.
 */
export async function renumberTenet({ oldId, newId }) {
  const normalizedOld = normalizeTenetId(oldId);
  if (!normalizedOld) {
    return { error: `Invalid oldId "${oldId}" — must match /^T\\d+$/.` };
  }
  const normalizedNew = normalizeTenetId(newId);
  if (!normalizedNew) {
    return { error: `Invalid newId "${newId}" — must match /^T\\d+$/.` };
  }
  oldId = normalizedOld;
  newId = normalizedNew;
  if (oldId === newId) {
    return { error: `oldId and newId are identical. Nothing to rename.` };
  }

  const catalog = await getTenetCatalog({ force: true });
  const oldTenet = catalog.tenets.get(oldId);
  if (!oldTenet) {
    return { error: `Tenet ${oldId} not found in any source file.` };
  }
  if (catalog.tenets.has(newId)) {
    const clash = catalog.tenets.get(newId);
    return {
      error:
        `Target id ${newId} is already taken by "${clash.name}" (${clash.sourceFile}:${clash.openSentinelLine}). ` +
        `Pick a different newId or rename the existing ${newId} first.`,
    };
  }

  const oldNumber = parseInt(oldId.slice(1), 10);
  const newNumber = parseInt(newId.slice(1), 10);

  // ── Phase 1: update category-file sentinels + heading ────────────────
  const categoryFilePath = absolute(oldTenet.sourceFile);
  const categoryContent = await readFile(categoryFilePath, "utf-8");
  const catLines = categoryContent.split("\n");

  // Open sentinel
  const openIdx = oldTenet.openSentinelLine - 1;
  catLines[openIdx] = catLines[openIdx].replace(
    new RegExp(`<!--\\s*TENET:${oldId}\\s*-->`),
    `<!-- TENET:${newId} -->`,
  );

  // Close sentinel
  const closeIdx = oldTenet.closeSentinelLine - 1;
  catLines[closeIdx] = catLines[closeIdx].replace(
    new RegExp(`<!--\\s*/TENET:${oldId}\\s*-->`),
    `<!-- /TENET:${newId} -->`,
  );

  // Heading — rewrite only the `Tenet N:` prefix, preserving name+severity
  let headingTouched = false;
  for (let i = openIdx + 1; i < closeIdx; i++) {
    const m = catLines[i].match(TENET_HEADING_RE);
    if (m && parseInt(m[1], 10) === oldNumber) {
      catLines[i] = catLines[i].replace(
        new RegExp(`^(##\\s+Tenet\\s+)${oldNumber}(\\s*:)`),
        `$1${newNumber}$2`,
      );
      headingTouched = true;
      break;
    }
  }

  await writeFile(categoryFilePath, catLines.join("\n"), "utf-8");

  // ── Phase 2: update TENETS.md — summary row, QR citations, T0, nav ──
  let summaryTouched = false;
  let citationsTouched = 0;
  const indexPath = absolute(TENETS_INDEX_REL_PATH);
  const isIndexSelf = oldTenet.sourceFile === TENETS_INDEX_REL_PATH;
  let indexLines;

  if (isIndexSelf) {
    // The tenet lives in TENETS.md itself (e.g., T0/T1/T2). Re-use the lines
    // we already mutated to avoid losing the sentinel/heading edits.
    indexLines = catLines;
  } else {
    const indexContent = await readFile(indexPath, "utf-8");
    indexLines = indexContent.split("\n");
  }

  // Summary table row: move the row from its old sorted position to the new
  // one. We remove by oldId (capturing name + rule), then insert under newId
  // at the correct sorted position in the same category's summary section.
  // This keeps summary tables numerically ordered even when the id changes.
  const removedSummary = removeSummaryTableRow(indexLines, oldId);
  if (removedSummary.touched) {
    const reinsert = insertSummaryTableRow(
      indexLines,
      oldTenet.category,
      newId,
      newNumber,
      removedSummary.capturedRow.name,
      removedSummary.capturedRow.summaryRule,
    );
    if (reinsert.touched) summaryTouched = true;
  }

  // Quick Reference citations — SCOPED to rows inside the Quick Reference
  // table only. We re-parse to find the exact line numbers of rows that
  // currently cite oldId, then do a word-boundary replace within the tenets
  // column, preserving the rest of the row byte-exact.
  // (Nav table rows that match the same pattern are rebuilt in step 3 below,
  // so we intentionally do NOT touch them here.)
  const qrRowsCitingOld = catalog.violations.filter((v) => v.tenets.includes(oldId));
  const citationCellRe = /^(\|\s*.+?\s*\|\s*)(T\d+(?:\s*,\s*T\d+)*)(\s*\|.*?\|\s*)$/;
  for (const v of qrRowsCitingOld) {
    const lineIdx = v.sourceLine - 1;
    const line = indexLines[lineIdx];
    const m = line.match(citationCellRe);
    if (!m) continue;
    const newCitations = m[2].replace(new RegExp(`\\b${oldId}\\b`, "g"), newId);
    if (newCitations === m[2]) continue;
    indexLines[lineIdx] = `${m[1]}${newCitations}${m[3]}`;
    citationsTouched++;
  }

  // ── Phase 3: T0 prose + nav table (rebuilt from logical set) ─────────
  const logicalTenets = new Map();
  for (const [id, t] of catalog.tenets) {
    if (id === oldId) {
      logicalTenets.set(newId, { ...t, id: newId, number: newNumber });
    } else {
      logicalTenets.set(id, t);
    }
  }
  const t0Touched = regenerateT0ProseLists(indexLines, logicalTenets);
  const navTouched = regenerateNavTableRows(indexLines, logicalTenets);

  await writeFile(indexPath, indexLines.join("\n"), "utf-8");

  resetCache();

  const result = {
    oldId,
    newId,
    categoryFile: oldTenet.sourceFile,
    indexFile: TENETS_INDEX_REL_PATH,
    changes: {
      headingTouched,
      summaryTouched,
      citationsTouched,
      sentinelsTouched: 2,
      t0ProseListsTouched: t0Touched,
      navTableRowsTouched: navTouched,
    },
  };

  await appendHistory({
    tool: "renumber_tenet",
    target: `${oldId}→${newId}`,
    summary:
      `Renumbered ${oldId} → ${newId} "${oldTenet.name}" in ${oldTenet.sourceFile} ` +
      `(heading${headingTouched ? "✓" : "✗"}, summary${summaryTouched ? "✓" : "✗"}, ` +
      `${citationsTouched} QR citation(s), ${t0Touched} T0 list(s), ${navTouched} nav row(s))`,
  });

  return result;
}

// ═══════════════════════════════════════════════════════════════════════════
// VALIDATE — Full tenet-system consistency check
// ═══════════════════════════════════════════════════════════════════════════
//
// Runs every structural + derived-content check the write tools depend on.
// Exposed as an MCP tool (`validate_tenets`) and wired into the `tenets`
// scope's `stop_scope` exit validation — scope release is BLOCKED when
// `errors.length > 0`, forcing the maintainer to fix inconsistencies before
// committing.
//
// Error categories (blocking):
//   • Parse errors surfaced by getTenetCatalog (sentinel imbalance, orphaned
//     sentinels, id mismatches, duplicate ids, missing structural tenets,
//     Quick Reference citations of unknown ids)
//   • Derived-content drift:
//       – T0 prose bullet for a sourceCodeCategoryName missing, or listing a
//         different set of ids than the parsed tenets for that group
//       – "Tenet Categories" nav row missing for an autoMaintained category,
//         or listing the wrong ids
//       – Per-category summary table missing, or rows out of number-order,
//         or rows present without a matching parsed tenet, or tenets without
//         a matching summary row

/**
 * Compute, for each sourceCodeCategoryName present across parsed tenets, the
 * sorted list of ids that SHOULD appear in T0's prose bullet for that label.
 * Returns Map<label, sortedIds[]>.
 */
function expectedT0ProseGroups(tenets) {
  const groups = new Map();
  for (const t of tenets.values()) {
    const meta = CATEGORIES[t.category];
    if (!meta || !meta.sourceCodeCategoryName) continue;
    const label = meta.sourceCodeCategoryName;
    if (!groups.has(label)) groups.set(label, []);
    groups.get(label).push(t);
  }
  for (const ts of groups.values()) ts.sort((a, b) => a.number - b.number);
  return groups;
}

/**
 * Compute expected nav-table id lists per autoMaintained category.
 * Returns Map<categoryId, sortedTenets[]>.
 */
function expectedNavGroups(tenets) {
  const groups = new Map();
  for (const t of tenets.values()) {
    const meta = CATEGORIES[t.category];
    if (!meta || !meta.navLabel) continue;
    if (!groups.has(t.category)) groups.set(t.category, []);
    groups.get(t.category).push(t);
  }
  for (const ts of groups.values()) ts.sort((a, b) => a.number - b.number);
  return groups;
}

/**
 * Run the full tenet-system validation pass. Returns:
 *   {
 *     errors:   string[],  // blocking — stop_scope refuses to release
 *     warnings: string[],  // advisory — reported but not blocking
 *     stats: {
 *       tenetCount, violationCount, parseErrorCount,
 *       t0ProseLabels, navRows, summarySections,
 *     },
 *   }
 *
 * Does NOT write to disk. Does NOT modify the catalog. Safe to call from
 * read-only contexts.
 */
export async function validateTenets() {
  const catalog = await getTenetCatalog({ force: true });
  const errors = [...catalog.errors];
  const warnings = [];

  // Read TENETS.md for derived-content checks. If it's missing we've already
  // reported it via catalog.errors; return what we have.
  const indexPath = absolute(TENETS_INDEX_REL_PATH);
  let indexLines;
  try {
    const content = await readFile(indexPath, "utf-8");
    indexLines = content.split("\n");
  } catch (err) {
    errors.push(`Cannot read ${TENETS_INDEX_REL_PATH}: ${err.message}`);
    return {
      errors, warnings,
      stats: {
        tenetCount: catalog.tenets.size,
        violationCount: catalog.violations.length,
        parseErrorCount: catalog.errors.length,
        t0ProseLabels: 0, navRows: 0, summarySections: 0,
      },
    };
  }

  // ── Check A: T0 prose lists ─────────────────────────────────────────
  // For each sourceCodeCategoryName appearing among parsed tenets, the T0
  // prose must contain a bullet `- `{LABEL}` - for {sorted-ids}` whose id
  // list matches exactly.
  const t0Groups = expectedT0ProseGroups(catalog.tenets);
  let t0ProseLabels = 0;
  for (const [label, ts] of t0Groups) {
    const expectedIds = ts.map((t) => t.id).join(", ");
    const labelEsc = escapeRegex(label);
    const re = new RegExp(`^-\\s+\`${labelEsc}\`\\s+-\\s+for\\s+(.+?)\\s*$`);
    let foundLine = null;
    let foundIdx = -1;
    for (let i = 0; i < indexLines.length; i++) {
      const m = indexLines[i].match(re);
      if (m) { foundLine = m[1]; foundIdx = i; break; }
    }
    if (foundLine === null) {
      errors.push(
        `T0 prose: no bullet line found for "${label}". Expected: ` +
        `\`- \`${label}\` - for ${expectedIds}\``,
      );
      continue;
    }
    // Extract the comma-separated id sequence from the bullet (may be followed
    // by parenthetical annotations, e.g., "(service layer dependencies)").
    const idMatch = foundLine.match(/^T\d+(?:\s*,\s*T\d+)*/);
    const actualIds = idMatch ? idMatch[0].replace(/\s+/g, " ").trim() : "";
    const expectedIdsNorm = expectedIds.replace(/\s+/g, " ").trim();
    if (actualIds !== expectedIdsNorm) {
      errors.push(
        `T0 prose for "${label}" (line ${foundIdx + 1}): id list mismatch. ` +
        `Expected "${expectedIdsNorm}", got "${actualIds}".`,
      );
    }
    t0ProseLabels++;
  }

  // ── Check B: "Tenet Categories" nav table rows ──────────────────────
  const navGroups = expectedNavGroups(catalog.tenets);
  let navRows = 0;
  for (const [catId, ts] of navGroups) {
    const meta = CATEGORIES[catId];
    const expectedIds = ts.map((t) => t.id).join(", ");
    const labelEsc = escapeRegex(meta.navLabel);
    const re = new RegExp(
      `^\\|\\s*\\[\\*\\*${labelEsc}\\*\\*\\]\\([^)]+\\)\\s*\\|\\s*([^|]+?)\\s*\\|`,
    );
    let foundIds = null;
    let foundIdx = -1;
    for (let i = 0; i < indexLines.length; i++) {
      const m = indexLines[i].match(re);
      if (m) { foundIds = m[1].trim(); foundIdx = i; break; }
    }
    if (foundIds === null) {
      errors.push(
        `Nav table: no row found for category "${meta.navLabel}". Expected: ` +
        `\`| [**${meta.navLabel}**](...) | ${expectedIds} | ... |\``,
      );
      continue;
    }
    if (foundIds !== expectedIds) {
      errors.push(
        `Nav table for "${meta.navLabel}" (line ${foundIdx + 1}): id list mismatch. ` +
        `Expected "${expectedIds}", got "${foundIds}".`,
      );
    }
    navRows++;
  }

  // ── Check C: Per-category summary tables ────────────────────────────
  let summarySections = 0;
  for (const [catId, ts] of navGroups) {
    const meta = CATEGORIES[catId];
    if (!meta.summaryTableHeading) continue;
    const headingMarker = `## ${meta.summaryTableHeading}`;
    const sectionStart = indexLines.findIndex((l) => l.trim() === headingMarker);
    if (sectionStart === -1) {
      errors.push(
        `Summary table for "${meta.summaryTableHeading}": section heading not found in TENETS.md.`,
      );
      continue;
    }
    let sectionEnd = indexLines.length;
    for (let i = sectionStart + 1; i < indexLines.length; i++) {
      if (/^##\s+/.test(indexLines[i])) { sectionEnd = i; break; }
    }
    // Collect row ids in order.
    const rowIds = [];
    for (let i = sectionStart + 1; i < sectionEnd; i++) {
      const m = indexLines[i].match(/^\|\s*\*\*T(\d+)\*\*\s*\|/);
      if (m) rowIds.push(`T${parseInt(m[1], 10)}`);
    }
    const expectedIds = ts.map((t) => t.id);
    if (JSON.stringify(rowIds) !== JSON.stringify(expectedIds)) {
      errors.push(
        `Summary table for "${meta.summaryTableHeading}": row order/set mismatch. ` +
        `Expected [${expectedIds.join(", ")}], got [${rowIds.join(", ")}].`,
      );
    }
    summarySections++;
  }

  // ── Check D: Summary rows without matching tenet ────────────────────
  // If TENETS.md has a summary row for T{N} that's not a parsed tenet,
  // something is out of sync. Catalog already surfaces citations of
  // unknown ids; this check catches orphaned summary rows.
  const knownIds = new Set([...catalog.tenets.keys()]);
  for (let i = 0; i < indexLines.length; i++) {
    const m = indexLines[i].match(/^\|\s*\*\*T(\d+)\*\*\s*\|/);
    if (!m) continue;
    const id = `T${parseInt(m[1], 10)}`;
    if (!knownIds.has(id)) {
      errors.push(
        `Summary table row at line ${i + 1} references unknown tenet ${id}. ` +
        `Remove the row or restore the tenet.`,
      );
    }
  }

  // ── Check E: TENETS-HISTORY.md existence (warning only) ─────────────
  // Optional — file is auto-created by write tools on first use. Surface
  // a warning if absent so new checkouts see a clear signal.
  const historyPath = absolute(TENETS_HISTORY_REL_PATH);
  try {
    await access(historyPath, constants.R_OK);
  } catch {
    warnings.push(
      `${TENETS_HISTORY_REL_PATH} does not exist yet. Write tools create it ` +
      `lazily on first use; create it by hand with the frozen header if you ` +
      `want it tracked in version control ahead of the first write.`,
    );
  }

  return {
    errors,
    warnings,
    stats: {
      tenetCount: catalog.tenets.size,
      violationCount: catalog.violations.length,
      parseErrorCount: catalog.errors.length,
      t0ProseLabels,
      navRows,
      summarySections,
    },
  };
}

/**
 * Format the validate_tenets result as markdown for the MCP tool response.
 */
export function formatValidateTenetsResult(result) {
  const lines = [];
  const hasErrors = result.errors.length > 0;
  const icon = hasErrors ? "❌" : "✅";
  lines.push(`# ${icon} validate_tenets`);
  lines.push("");
  const s = result.stats;
  lines.push(`**Stats**: ${s.tenetCount} tenets, ${s.violationCount} violation rows, ` +
    `${s.t0ProseLabels} T0 prose bullet(s), ${s.navRows} nav row(s), ${s.summarySections} summary section(s)`);
  lines.push("");
  if (hasErrors) {
    lines.push(`## Errors (${result.errors.length}) — BLOCKING`);
    lines.push("");
    for (const e of result.errors) lines.push(`- ${e}`);
    lines.push("");
  }
  if (result.warnings.length > 0) {
    lines.push(`## Warnings (${result.warnings.length})`);
    lines.push("");
    for (const w of result.warnings) lines.push(`- ${w}`);
    lines.push("");
  }
  if (!hasErrors && result.warnings.length === 0) {
    lines.push("No issues. The tenet system is internally consistent.");
  }
  return lines.join("\n");
}
