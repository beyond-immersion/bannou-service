/**
 * File operation helpers — read tracking, gate checks, chunking.
 *
 * These helpers manage the read-before-edit gate and large file splitting.
 * State is imported from ../state.mjs (ES module singleton).
 */

import { readFile, writeFile, stat, access } from "node:fs/promises";
import { resolve, extname, basename } from "node:path";
import { constants } from "node:fs";
import { createHash } from "node:crypto";
import { tmpdir } from "node:os";
import { join } from "node:path";

import {
  readFiles,
  pendingContinuations,
  continuationToOriginal,
  requiredReading,
  BINARY_EXTENSIONS,
  CHUNK_BYTE_LIMIT,
  RESPONSE_SERIALIZED_LIMIT,
  CONTINUATION_PREFIX,
  COMPOSITE_PREFIX,
} from "../state.mjs";

// ─── File Type Detection ────────────────────────────────────────────────

export function isContinuationFile(filePath) {
  const name = basename(filePath);
  return name.startsWith(CONTINUATION_PREFIX) && name.includes("-part");
}

export function isCompositeFile(filePath) {
  const name = basename(filePath);
  return name.startsWith(COMPOSITE_PREFIX) && name.endsWith(".txt");
}

// ─── Hashing ───────────────────────────────────────────────────────────────

export function pathHash(filePath) {
  return createHash("md5").update(filePath).digest("hex").slice(0, 12);
}

// ─── Chunking ──────────────────────────────────────────────────────────────

export function splitIntoChunks(numberedLines) {
  const chunks = [];
  let chunkStart = 0;
  let currentSize = 0;

  for (let i = 0; i < numberedLines.length; i++) {
    const lineSize = Buffer.byteLength(numberedLines[i], "utf-8") + 1;

    if (currentSize + lineSize > CHUNK_BYTE_LIMIT && i > chunkStart) {
      chunks.push({
        startLine: chunkStart + 1,
        endLine: i,
        text: numberedLines.slice(chunkStart, i).join("\n"),
      });
      chunkStart = i;
      currentSize = 0;
    }

    currentSize += lineSize;
  }

  if (chunkStart < numberedLines.length) {
    chunks.push({
      startLine: chunkStart + 1,
      endLine: numberedLines.length,
      text: numberedLines.slice(chunkStart).join("\n"),
    });
  }

  return chunks;
}

// ─── String Counting ───────────────────────────────────────────────────────

/**
 * Count non-overlapping occurrences of a substring in a string.
 */
export function countOccurrences(haystack, needle) {
  let count = 0;
  let pos = 0;
  while ((pos = haystack.indexOf(needle, pos)) !== -1) {
    count++;
    pos += needle.length;
  }
  return count;
}

// ─── Response Size Estimation ──────────────────────────────────────────────

/**
 * Compute the approximate serialized size of an MCP text response.
 * Accounts for JSON string escaping (\n → \\n, \t → \\t, " → \\", etc.)
 * and the MCP response JSON wrapper overhead.
 *
 * Use this instead of raw Buffer.byteLength when deciding whether to return
 * content inline or split into continuations/composites. Raw byte size
 * underestimates the serialized cost — code with many newlines, tabs, and
 * quotes can inflate 10-15% after JSON escaping.
 */
export function computeResponseSize(text) {
  // JSON.stringify wraps in quotes and escapes special characters —
  // this is the exact size the text will occupy in the serialized response
  const jsonStringSize = Buffer.byteLength(JSON.stringify(text), "utf-8");
  // MCP response wrapper: [{"type":"text","text":...}] ≈ 40 bytes
  return jsonStringSize + 40;
}

/**
 * Returns true if the response text would exceed the serialized size limit
 * and get persisted to disk by Claude Code (causing JSON escaping issues).
 */
export function wouldExceedResponseLimit(text) {
  return computeResponseSize(text) > RESPONSE_SERIALIZED_LIMIT;
}

// ─── Persisted Output Detection ───────────────────────────────────────────

/**
 * Detect if a file path is a Claude Code persisted tool output file.
 * These are JSON files at .claude/projects/.../tool-results/toolu_*.json
 * containing escaped MCP response content.
 */
export function isPersistedOutputFile(filePath) {
  return filePath.includes(".claude/") &&
    filePath.includes("/tool-results/") &&
    basename(filePath).startsWith("toolu_") &&
    filePath.endsWith(".json");
}

/**
 * Parse a persisted tool output file and extract the text content.
 * Returns the extracted text, or null if parsing fails.
 */
export function extractPersistedContent(jsonContent) {
  try {
    const parsed = JSON.parse(jsonContent);
    if (!Array.isArray(parsed) || parsed.length === 0) return null;

    // Concatenate all text entries (tool results are arrays of content blocks)
    const texts = [];
    for (const block of parsed) {
      if (block && block.type === "text" && typeof block.text === "string") {
        texts.push(block.text);
      }
    }

    return texts.length > 0 ? texts.join("\n") : null;
  } catch {
    return null;
  }
}

// ─── Gate Check ────────────────────────────────────────────────────────────

/**
 * Shared gate check for file modification tools (edit_file, move_lines, validate_structure).
 * Returns an error response if the file hasn't been read or has unread continuations.
 * Returns null if the file is clear for modification.
 *
 * Note: run_command uses a separate global gate (any pending continuations block all commands).
 */
export function checkFileGate(resolvedPath) {
  if (!readFiles.has(resolvedPath)) {
    return {
      content: [
        {
          type: "text",
          text: `Error: ${resolvedPath} has not been read yet. Use read_file first.`,
        },
      ],
      isError: true,
    };
  }

  const pending = pendingContinuations.get(resolvedPath);
  if (pending && pending.size > 0) {
    const unreadPaths = [...pending].map((p) => `  ${p}`).join("\n");
    return {
      content: [
        {
          type: "text",
          text: [
            `Error: ${resolvedPath} was split into multiple parts during reading.`,
            `You have ${pending.size} unread continuation file(s). You MUST read ALL parts before editing.`,
            "",
            "Unread continuation files:",
            unreadPaths,
            "",
            "Read each file above with read_file, then retry.",
          ].join("\n"),
        },
      ],
      isError: true,
    };
  }

  return null; // Clear
}

// ─── Required Reading Gate ──────────────────────────────────────────────

/**
 * Global gate: blocks all mutation tools while requiredReading is non-empty.
 * Returns null if clear, or an error response if blocked.
 *
 * NOT checked by: read_file, prepare_context (always allowed).
 * Checked by: edit_file, write_file, move_lines, validate_structure,
 *             run_command, write_script.
 */
export function checkRequiredReadingGate() {
  if (requiredReading.size === 0) return null;

  const paths = [...requiredReading].map((p) => `  ${p}`).join("\n");
  return {
    content: [
      {
        type: "text",
        text: [
          `Error: Required context files have not been read yet.`,
          `${requiredReading.size} composite(s) remaining:`,
          "",
          paths,
          "",
          "Read each file with read_file to load context, then retry.",
        ].join("\n"),
      },
    ],
    isError: true,
  };
}

// Re-export fs functions and constants used by tool handlers
export { readFile, writeFile, stat, access, resolve, extname, basename, constants, tmpdir, join, createHash };
// Re-export state for direct manipulation by read_file handler
export { readFiles, pendingContinuations, continuationToOriginal, requiredReading, BINARY_EXTENSIONS, CHUNK_BYTE_LIMIT, RESPONSE_SERIALIZED_LIMIT, CONTINUATION_PREFIX, COMPOSITE_PREFIX };
