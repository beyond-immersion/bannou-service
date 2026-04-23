/**
 * Implementation map → source code coverage checking.
 *
 * Parses an implementation map markdown file (plugin or SDK) to extract
 * expected types and methods, then scans the source directory for matches.
 * Returns a structured coverage report.
 *
 * Works for both:
 *   - Plugin maps (docs/maps/{SERVICE}.md → plugins/lib-{service}/)
 *   - SDK maps (docs/sdks/maps/{SDK}.md → sdks/{sdk}/)
 */

import { readFile, readdir, stat } from "node:fs/promises";
import { join, resolve, extname } from "node:path";

const PROJECT_DIR = process.env.CLAUDE_PROJECT_DIR || process.cwd();

/**
 * Main entry point: check coverage of an implementation map against source code.
 *
 * @param {string} name - Component name (e.g., "account", "sprite-theory")
 * @param {"plugin"|"sdk"|undefined} kind - Component kind (auto-detected if omitted)
 * @returns {Promise<{error?: string, report?: object}>}
 */
export async function checkCoverage(name, kind) {
  // ── Auto-detect kind ──
  if (!kind) {
    const pluginMapPath = join(PROJECT_DIR, "docs/maps", `${name.toUpperCase()}.md`);
    const sdkMapPath = join(PROJECT_DIR, "docs/sdks/maps", `${name.toUpperCase()}.md`);
    try {
      await stat(pluginMapPath);
      kind = "plugin";
    } catch {
      try {
        await stat(sdkMapPath);
        kind = "sdk";
      } catch {
        return { error: `No implementation map found for "${name}". Checked:\n  ${pluginMapPath}\n  ${sdkMapPath}` };
      }
    }
  }

  // ── Resolve paths ──
  const mapPath = kind === "plugin"
    ? join(PROJECT_DIR, "docs/maps", `${name.toUpperCase()}.md`)
    : join(PROJECT_DIR, "docs/sdks/maps", `${name.toUpperCase()}.md`);

  const sourceDir = kind === "plugin"
    ? join(PROJECT_DIR, "plugins", `lib-${name}`)
    : join(PROJECT_DIR, "sdks", name);

  // ── Read the map ──
  let mapContent;
  try {
    mapContent = await readFile(mapPath, "utf-8");
  } catch {
    return { error: `Implementation map not found: ${mapPath}` };
  }

  // ── Check source directory exists ──
  try {
    const s = await stat(sourceDir);
    if (!s.isDirectory()) {
      return { error: `Source path is not a directory: ${sourceDir}` };
    }
  } catch {
    return {
      report: {
        name,
        kind,
        mapPath: mapPath.replace(PROJECT_DIR + "/", ""),
        sourceDir: sourceDir.replace(PROJECT_DIR + "/", ""),
        sourceExists: false,
        expectedTypes: parseTypes(mapContent),
        expectedMethods: parseMethods(mapContent, kind),
        foundTypes: [],
        foundMethods: [],
        missingTypes: [],
        missingMethods: [],
        coveragePct: 0,
        summary: "Source directory does not exist — 0% coverage",
      },
    };
  }

  // ── Parse expected types and methods from the map ──
  const expectedTypes = parseTypes(mapContent);
  const expectedMethods = parseMethods(mapContent, kind);

  // ── Scan source files ──
  const csFiles = await findCsFiles(sourceDir);
  const allSource = await readAllFiles(csFiles);

  // ── Match types ──
  const foundTypes = [];
  const missingTypes = [];
  for (const t of expectedTypes) {
    if (findTypeInSource(t.name, allSource)) {
      foundTypes.push(t);
    } else {
      missingTypes.push(t);
    }
  }

  // ── Match methods ──
  const foundMethods = [];
  const missingMethods = [];
  for (const m of expectedMethods) {
    if (findMethodInSource(m.name, m.className, allSource)) {
      foundMethods.push(m);
    } else {
      missingMethods.push(m);
    }
  }

  // ── Compute coverage ──
  const totalExpected = expectedTypes.length + expectedMethods.length;
  const totalFound = foundTypes.length + foundMethods.length;
  const coveragePct = totalExpected > 0 ? Math.round((totalFound / totalExpected) * 100) : 100;

  return {
    report: {
      name,
      kind,
      mapPath: mapPath.replace(PROJECT_DIR + "/", ""),
      sourceDir: sourceDir.replace(PROJECT_DIR + "/", ""),
      sourceExists: true,
      sourceFileCount: csFiles.length,
      expectedTypes,
      expectedMethods,
      foundTypes,
      foundMethods,
      missingTypes,
      missingMethods,
      coveragePct,
      summary: `${totalFound}/${totalExpected} items found (${coveragePct}% coverage)`,
    },
  };
}

// ═══════════════════════════════════════════════════════════════════════════
// PARSING — Extract expected types and methods from implementation maps
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Parse type definitions from the implementation map.
 * Looks for ### headings followed by **Kind**: Record/Class/Interface/Enum/Struct
 * under "Data Structures" or "Enums" sections.
 */
function parseTypes(mapContent) {
  const types = [];
  const lines = mapContent.split("\n");

  // Track which section we're in
  let inDataSection = false;

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();

    // Detect data structure sections
    if (/^##\s+(Data Structures|Enums|Shared Value Types|Shared Types|Interfaces)$/i.test(line)) {
      inDataSection = true;
      continue;
    }

    // Exit section on next ## heading
    if (/^##\s+/.test(line) && inDataSection && !/^###/.test(line)) {
      inDataSection = false;
      continue;
    }

    // Detect type heading: ### TypeName
    if (inDataSection && /^###\s+(\w+)$/.test(line)) {
      const name = line.match(/^###\s+(\w+)$/)[1];
      // Look ahead for **Kind**: ... to determine type kind
      let typeKind = "unknown";
      for (let j = i + 1; j < Math.min(i + 5, lines.length); j++) {
        const kindMatch = lines[j].match(/\*\*Kind\*\*:\s*(\w+)/i);
        if (kindMatch) {
          typeKind = kindMatch[1].toLowerCase();
          break;
        }
      }
      types.push({ name, kind: typeKind });
    }
  }

  // Also parse enum tables: | Enum | Values | Purpose |
  const enumTableRegex = /\|\s*`?(\w+)`?\s*\|\s*`?([^|]+)`?\s*\|\s*([^|]*)\|/g;
  let inEnumTable = false;
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();
    if (/\|\s*Enum\s*\|\s*Values\s*\|/i.test(line)) {
      inEnumTable = true;
      continue;
    }
    if (inEnumTable && /^\|[-\s|]+\|$/.test(line)) continue; // separator
    if (inEnumTable && /^\|/.test(line)) {
      const match = line.match(/\|\s*`?(\w+)`?\s*\|/);
      if (match && match[1] !== "Enum" && match[1] !== "---") {
        // Check not already added
        if (!types.find((t) => t.name === match[1])) {
          types.push({ name: match[1], kind: "enum" });
        }
      }
    } else if (inEnumTable) {
      inEnumTable = false;
    }
  }

  // Also parse from "Public API Surface" tables if they exist
  const apiSurfaceRegex = /\|\s*`?(\w+)`?\s*\|\s*(Record|Class|Static class|Interface|Enum|Struct)\s*\|/gi;
  let match;
  while ((match = apiSurfaceRegex.exec(mapContent)) !== null) {
    const typeName = match[1];
    const typeKind = match[2].toLowerCase().replace("static ", "");
    if (!types.find((t) => t.name === typeName)) {
      types.push({ name: typeName, kind: typeKind });
    }
  }

  return types;
}

/**
 * Parse method definitions from the implementation map.
 * Handles both SDK API Index tables and plugin Method Index tables.
 */
function parseMethods(mapContent, kind) {
  const methods = [];
  const lines = mapContent.split("\n");

  // ── SDK: Parse "## API Index" section with ### ClassName subsections ──
  let inApiIndex = false;
  let currentClass = null;

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();

    if (/^##\s+API Index$/i.test(line)) {
      inApiIndex = true;
      continue;
    }
    if (inApiIndex && /^##\s+/.test(line) && !/^###/.test(line)) {
      inApiIndex = false;
      continue;
    }
    if (inApiIndex && /^###\s+(\w+)/.test(line)) {
      currentClass = line.match(/^###\s+(\w+)/)[1];
      continue;
    }

    // Parse method table rows: | `MethodName` | signature | ... |
    if (inApiIndex && currentClass && /^\|/.test(line)) {
      const methodMatch = line.match(/\|\s*`(\w+)`\s*\|/);
      if (methodMatch && methodMatch[1] !== "Method") {
        methods.push({ name: methodMatch[1], className: currentClass });
      }
    }
  }

  // ── Plugin: Parse "## Method Index" table ──
  let inMethodIndex = false;
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();
    if (/^##\s+Method Index$/i.test(line)) {
      inMethodIndex = true;
      continue;
    }
    if (inMethodIndex && /^##\s+/.test(line)) {
      inMethodIndex = false;
      continue;
    }
    if (inMethodIndex && /^\|/.test(line)) {
      // | MethodName | Route | ... |
      const match = line.match(/\|\s*(\w+(?:Async)?)\s*\|/);
      if (match && match[1] !== "Method" && match[1] !== "---") {
        methods.push({ name: match[1], className: null });
      }
    }
  }

  // ── Both: Parse ### MethodName headers in "## Methods" section ──
  let inMethodsSection = false;
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();
    if (/^##\s+Methods$/i.test(line)) {
      inMethodsSection = true;
      continue;
    }
    if (inMethodsSection && /^##\s+[^#]/.test(line)) {
      inMethodsSection = false;
      continue;
    }
    if (inMethodsSection && /^###\s+/.test(line)) {
      // ### ClassName.MethodName or ### MethodNameAsync
      const heading = line.replace(/^###\s+/, "");
      const dotMatch = heading.match(/^(\w+)\.(\w+)$/);
      if (dotMatch) {
        const className = dotMatch[1];
        const methodName = dotMatch[2];
        if (!methods.find((m) => m.name === methodName && m.className === className)) {
          methods.push({ name: methodName, className });
        }
      } else {
        const simpleMatch = heading.match(/^(\w+)$/);
        if (simpleMatch) {
          if (!methods.find((m) => m.name === simpleMatch[1])) {
            methods.push({ name: simpleMatch[1], className: null });
          }
        }
      }
    }
  }

  return methods;
}

// ═══════════════════════════════════════════════════════════════════════════
// SOURCE SCANNING — Find types and methods in C# files
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Recursively find all .cs files in a directory, excluding obj/, bin/, Generated/.
 */
async function findCsFiles(dir) {
  const results = [];
  try {
    const entries = await readdir(dir, { withFileTypes: true });
    for (const entry of entries) {
      const fullPath = join(dir, entry.name);
      if (entry.isDirectory()) {
        if (["obj", "bin", "Generated", "node_modules", ".vs"].includes(entry.name)) continue;
        results.push(...await findCsFiles(fullPath));
      } else if (entry.isFile() && extname(entry.name) === ".cs") {
        results.push(fullPath);
      }
    }
  } catch {
    // Directory might not exist
  }
  return results;
}

/**
 * Read all C# files and concatenate for searching.
 */
async function readAllFiles(paths) {
  const contents = [];
  for (const p of paths) {
    try {
      contents.push({ path: p, content: await readFile(p, "utf-8") });
    } catch {
      // Skip unreadable files
    }
  }
  return contents;
}

/**
 * Check if a type (class, record, struct, interface, enum) exists in source.
 */
function findTypeInSource(typeName, sources) {
  // Match: (public|internal|private)? (sealed|abstract|partial|static|readonly)* (class|record|struct|interface|enum) TypeName
  const pattern = new RegExp(
    `\\b(?:class|record|struct|interface|enum)\\s+${escapeRegex(typeName)}\\b`
  );
  return sources.some((s) => pattern.test(s.content));
}

/**
 * Check if a method exists in source, optionally within a specific class.
 */
function findMethodInSource(methodName, className, sources) {
  // For static classes, methods might be: public static ReturnType MethodName(
  // For instance methods: public ReturnType MethodName(
  // Also match async variants
  const methodPattern = new RegExp(
    `\\b${escapeRegex(methodName)}\\s*[(<]`
  );

  if (className) {
    // Check within a class context — find the class first, then the method
    for (const source of sources) {
      if (methodPattern.test(source.content)) {
        // Verify the class also exists in the same file or nearby
        const classPattern = new RegExp(
          `\\b(?:class|record|struct|interface)\\s+${escapeRegex(className)}\\b`
        );
        if (classPattern.test(source.content)) {
          return true;
        }
      }
    }
    // Also accept the method without the class context (may be in a different file)
    return sources.some((s) => methodPattern.test(s.content));
  }

  return sources.some((s) => methodPattern.test(s.content));
}

function escapeRegex(str) {
  return str.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/**
 * Format the coverage report as human-readable text.
 */
export function formatCoverageReport(report) {
  const lines = [
    `Coverage Report: ${report.name} (${report.kind})`,
    `Map:    ${report.mapPath}`,
    `Source: ${report.sourceDir}${report.sourceExists ? ` (${report.sourceFileCount} .cs files)` : " (NOT FOUND)"}`,
    "",
    `Summary: ${report.summary}`,
    "",
  ];

  // Types
  const typesTotal = report.expectedTypes.length;
  const typesFound = report.foundTypes.length;
  lines.push(`Types: ${typesFound}/${typesTotal}`);
  if (report.missingTypes.length > 0) {
    lines.push("  Missing:");
    for (const t of report.missingTypes) {
      lines.push(`    ✗ ${t.name} (${t.kind})`);
    }
  }
  if (report.foundTypes.length > 0) {
    lines.push("  Found:");
    for (const t of report.foundTypes) {
      lines.push(`    ✓ ${t.name} (${t.kind})`);
    }
  }
  lines.push("");

  // Methods
  const methodsTotal = report.expectedMethods.length;
  const methodsFound = report.foundMethods.length;
  lines.push(`Methods: ${methodsFound}/${methodsTotal}`);
  if (report.missingMethods.length > 0) {
    lines.push("  Missing:");
    for (const m of report.missingMethods) {
      const ctx = m.className ? `${m.className}.` : "";
      lines.push(`    ✗ ${ctx}${m.name}`);
    }
  }
  if (report.foundMethods.length > 0) {
    lines.push("  Found:");
    for (const m of report.foundMethods) {
      const ctx = m.className ? `${m.className}.` : "";
      lines.push(`    ✓ ${ctx}${m.name}`);
    }
  }

  return lines.join("\n");
}
