/**
 * Gated tool response — shared output handler for read-like tools.
 *
 * Handles the same oversized-output problem as read_file, using the same
 * requiredReading gate mechanism as prepare_context. Any tool that returns
 * file-sourced content calls gatedToolResponse() instead of building raw
 * response objects.
 *
 * Behavior:
 *   1. sourceFiles → readFiles (already read, can be edit_file'd immediately)
 *   2. If serialized output fits under RESPONSE_SERIALIZED_LIMIT → returns directly (zero overhead)
 *   3. If output exceeds limit → splits into composite parts in /tmp/,
 *      adds overflow parts to requiredReading (gating ALL other tools),
 *      returns part 1 with links to remaining parts
 *
 * The overflow composites use COMPOSITE_PREFIX so read_file detects them
 * via isCompositeFile() and clears them from requiredReading when read.
 * No changes to read_file, checkRequiredReadingGate, or any existing
 * mechanism are needed.
 *
 * Usage in tool registrations:
 *
 *   const result = await getPluginDocs(name);
 *   return gatedToolResponse({
 *     text: formatOutput(result),
 *     sourceFiles: [result.deepDivePath, result.mapPath],
 *     label: `Plugin Docs: ${name}`,
 *   });
 */

import {
  readFiles,
  requiredReading,
  COMPOSITE_PREFIX,
} from "../state.mjs";

import { splitIntoChunks, pathHash, wouldExceedResponseLimit } from "./file-ops.mjs";

import { writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { tmpdir } from "node:os";
import { join } from "node:path";

/**
 * Format and return tool output with automatic read-gating for large results.
 *
 * @param {Object} options
 * @param {string} options.text — Full formatted text to return to the agent.
 * @param {string[]} [options.sourceFiles=[]] — Original file paths whose FULL content
 *   is represented in `text`. These are added to readFiles, enabling immediate edit_file.
 *   Null/undefined entries are silently skipped.
 * @param {string} options.label — Human-readable label for the output (used in
 *   composite headers and continuation links).
 * @returns {Object} MCP tool response ({ content: [{ type: "text", text }] }).
 */
export async function gatedToolResponse({ text, sourceFiles = [], label }) {
  // 1. Mark source files as read (enables edit_file without separate read_file)
  const projectDir = process.env.CLAUDE_PROJECT_DIR || process.cwd();
  for (const f of sourceFiles) {
    if (f) {
      // Resolve relative paths against project root
      const abs = f.startsWith("/") ? f : resolve(projectDir, f);
      readFiles.add(abs);
    }
  }

  // 2. Check if output fits in a single response (using serialized size to avoid persistence)
  if (!wouldExceedResponseLimit(text)) {
    return { content: [{ type: "text", text }] };
  }

  // 3. Split into chunks using the shared line-based splitter
  const lines = text.split("\n");
  const chunks = splitIntoChunks(lines);
  const sizeKB = (Buffer.byteLength(text, "utf-8") / 1024).toFixed(1);

  // 4. Write parts as composites to /tmp/
  //    Uses COMPOSITE_PREFIX so read_file's isCompositeFile() detects them
  //    and clears them from requiredReading when read.
  const hash = pathHash(label);
  const compositePaths = [];

  // Degenerate case: content under CHUNK_BYTE_LIMIT raw but over RESPONSE_SERIALIZED_LIMIT
  // serialized. Write the single chunk as a composite so the response stays small.
  const startIndex = chunks.length === 1 ? 0 : 1;

  for (let i = startIndex; i < chunks.length; i++) {
    const chunk = chunks[i];
    const partPath = join(tmpdir(), `${COMPOSITE_PREFIX}tool-${hash}-part${i + 1}.txt`);
    const header = `${label} — part ${i + 1} of ${chunks.length}\n\n`;
    await writeFile(partPath, header + chunk.text, "utf-8");
    compositePaths.push(partPath);
    requiredReading.add(partPath);
  }

  // If all content went to composites (degenerate case), return just the manifest
  if (startIndex === 0) {
    return { content: [{ type: "text", text: [
      `${label} (${sizeKB} KB — written to ${compositePaths.length} composite(s))`,
      "",
      `⚠️ Read all composites before using other tools:`,
      ...compositePaths.map((p, i) => `  ${p}  (Part ${i + 1})`),
    ].join("\n") }] };
  }

  // 5. Return first chunk with continuation links
  const responseHeader = [
    `${label} (${sizeKB} KB total — split into ${chunks.length} parts)`,
    "",
    `⚠️ Read ALL continuation parts before using other tools:`,
    ...compositePaths.map((p, i) =>
      `  ${p}  (Part ${i + 2} of ${chunks.length})`),
    "",
    `Part 1 of ${chunks.length}:`,
  ].join("\n");

  return { content: [{ type: "text", text: responseHeader + "\n" + chunks[0].text }] };
}
