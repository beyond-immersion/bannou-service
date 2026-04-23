/**
 * C# structure validation helpers.
 *
 * Validates balanced braces, #region/#endregion pairs, and #if/#endif pairs.
 * Used by move_lines (automatic post-move check) and validate_structure (standalone tool).
 */

import { extname, basename } from "node:path";

/**
 * Validates structural integrity of a C# file: balanced braces, regions, and
 * preprocessor directives. Returns an object with { ok, findings[] }.
 * Each finding is { type, message, line? }.
 */
export function validateStructure(content, filePath) {
  // Only validate C# files — braces/regions/preprocessor checks are meaningless for .md, .yaml, etc.
  if (extname(filePath).toLowerCase() !== ".cs") {
    return { ok: true, findings: [] };
  }

  const lines = content.split("\n");
  const findings = [];

  // ── Brace balance ──
  let braceDepth = 0;
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    let stripped = line.replace(/\/\/.*$/, ""); // remove // comments
    stripped = stripped.replace(/"(?:[^"\\]|\\.)*"/g, '""'); // collapse string literals
    stripped = stripped.replace(/'(?:[^'\\]|\\.)*'/g, "''"); // collapse char literals

    for (const ch of stripped) {
      if (ch === "{") braceDepth++;
      if (ch === "}") braceDepth--;
    }

    if (braceDepth < 0) {
      findings.push({
        type: "brace",
        message: `Brace depth went negative (extra closing brace)`,
        line: i + 1,
      });
    }
  }

  if (braceDepth !== 0) {
    findings.push({
      type: "brace",
      message: `Brace imbalance: ${braceDepth > 0 ? `${braceDepth} unclosed opening brace(s)` : `${-braceDepth} extra closing brace(s)`}`,
    });
  }

  // ── Region balance ──
  const regionStack = [];
  for (let i = 0; i < lines.length; i++) {
    const trimmed = lines[i].trim();
    if (trimmed.startsWith("#region")) {
      regionStack.push({ name: trimmed, line: i + 1 });
    } else if (trimmed === "#endregion") {
      if (regionStack.length === 0) {
        findings.push({
          type: "region",
          message: `#endregion without matching #region`,
          line: i + 1,
        });
      } else {
        regionStack.pop();
      }
    }
  }

  for (const unclosed of regionStack) {
    findings.push({
      type: "region",
      message: `Unclosed ${unclosed.name}`,
      line: unclosed.line,
    });
  }

  // ── Preprocessor directive balance (#if / #endif) ──
  const ppStack = [];
  for (let i = 0; i < lines.length; i++) {
    const trimmed = lines[i].trim();
    if (trimmed.startsWith("#if")) {
      ppStack.push({ directive: trimmed, line: i + 1 });
    } else if (trimmed === "#endif") {
      if (ppStack.length === 0) {
        findings.push({
          type: "preprocessor",
          message: `#endif without matching #if`,
          line: i + 1,
        });
      } else {
        ppStack.pop();
      }
    }
  }

  for (const unclosed of ppStack) {
    findings.push({
      type: "preprocessor",
      message: `Unclosed ${unclosed.directive}`,
      line: unclosed.line,
    });
  }

  return {
    ok: findings.length === 0,
    findings,
  };
}

/**
 * Formats validation results for inclusion in tool responses.
 * Returns empty string if no issues, or a formatted warning block.
 */
export function formatValidationResults(sourceResult, sourcePath, destResult, destPath) {
  const parts = [];

  if (!sourceResult.ok) {
    parts.push(`\u26a0\ufe0f Structure issues in source (${basename(sourcePath)}):`);
    for (const f of sourceResult.findings) {
      parts.push(`  [${f.type}] ${f.message}${f.line ? ` (line ${f.line})` : ""}`);
    }
  }

  if (!destResult.ok) {
    parts.push(`\u26a0\ufe0f Structure issues in dest (${basename(destPath)}):`);
    for (const f of destResult.findings) {
      parts.push(`  [${f.type}] ${f.message}${f.line ? ` (line ${f.line})` : ""}`);
    }
  }

  if (parts.length === 0) {
    return "\n  Structure validation: \u2705 both files OK";
  }

  return "\n" + parts.join("\n");
}
