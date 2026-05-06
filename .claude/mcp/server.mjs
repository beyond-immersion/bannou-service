/**
 * Bannou MCP Server
 *
 * Development-time MCP server for Claude Code providing file operations,
 * command execution, C# structure validation, and (planned) documentation
 * tools, schema introspection, and seed data generation.
 *
 * ARCHITECTURE:
 *
 *   .claude/mcp/
 *   ├── server.mjs              ← YOU ARE HERE — entry point, tool registrations
 *   ├── state.mjs               ← Shared mutable state (ES module singleton)
 *   ├── profiles.mjs            ← Context profile definitions (dev, plugin, schema)
 *   ├── package.json
 *   ├── helpers/
 *   │   ├── file-ops.mjs        ← Read tracking, gate checks, chunking
 *   │   ├── tool-output.mjs     ← Gated tool response (shared oversized output handler)
 *   │   ├── structure.mjs       ← C# brace/region/preprocessor validation
 *   │   ├── commands.mjs        ← Command whitelist, blocked patterns, execution
 *   │   ├── sentinel.mjs        ← External state injection (Stream Deck, manual)
 *   │   ├── scripts.mjs         ← Sandboxed script writing with syntax validation
 *   │   ├── context.mjs         ← Context preparation, composite packing, profile resolution
 *   │   ├── plugins.mjs         ← Plugin catalog, documentation retrieval
 *   │   ├── docs.mjs            ← Documentation catalog, search, retrieval
 *   │   ├── schemas.mjs         ← Schema catalog, retrieval
 *   │   ├── infrastructure.mjs  ← Service details, events, state stores, model/interface shapes
 *   │   ├── generators.mjs     ← Code generation catalog and execution
 *   │   ├── dashboard-emitter.mjs ← WebSocket event emitter for real-time dashboard
 *   │   ├── worker.mjs          ← Worker agent spawning (dispatch_worker tool)
 *   │   └── seed.mjs            ← (planned) Seed bundle generation orchestration
 *   └── node_modules/
 *
 * DESIGN PRINCIPLES:
 *
 * 1. server.mjs is the "table of contents" — every tool is registered here
 *    as a thin wrapper calling a helper function. Scan this file to see all
 *    available tools at a glance.
 *
 * 2. helpers/ contains the logic — each file is a single concern with pure
 *    functions (except file-ops.mjs which manages read-tracking state).
 *    Helpers are independently callable from other helpers and from the
 *    seed generation tool.
 *
 * 3. state.mjs is the shared singleton — ES modules are evaluated once and
 *    cached. Every file importing state.mjs gets the same object references.
 *    This is how readFiles, pendingContinuations, etc. are shared across
 *    all helpers without parameter passing.
 *
 * 4. Shared helper pattern — validateStructure() is called by both move_lines
 *    (automatic post-move check) and validate_structure (standalone tool).
 *    All new helpers follow this pattern: callable from tools AND from the
 *    seed generation orchestrator.
 *
 * WHY THIS EXISTS:
 * - Built-in Read truncates files over ~2000 lines
 * - Built-in Edit requires built-in Read tracking (MCP tools can't satisfy)
 * - Built-in Write has no /tmp recovery — content is lost on gate failure
 * - By owning read, edit, and write, this server provides a self-contained
 *   file operation space with read-before-edit gates, large file splitting,
 *   and content-preserving failure recovery
 *
 * LARGE FILE HANDLING:
 * Claude Code caps MCP output at ~100KB serialized. read_file splits large
 * files into continuation parts written to /tmp with original line numbers.
 * The agent must read all parts before edit_file or run_command will proceed.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";

// ─── Shared State ──────────────────────────────────────────────────────────

import {
  readFiles,
  pendingContinuations,
  continuationToOriginal,
  requiredReading,
  BINARY_EXTENSIONS,
  CHUNK_BYTE_LIMIT,
  CONTINUATION_PREFIX,
  COMPOSITE_PREFIX,
} from "./state.mjs";

// ─── Helpers ───────────────────────────────────────────────────────────────

import {
  isContinuationFile, isCompositeFile, isPersistedOutputFile, extractPersistedContent,
  pathHash, splitIntoChunks, countOccurrences,
  wouldExceedResponseLimit,
  checkFileGate, checkRequiredReadingGate,
  mcpResponse,
  readFile, writeFile, stat, access, resolve, extname, basename, constants, tmpdir, join,
  createHash,
} from "./helpers/file-ops.mjs";

import { validateStructure, formatValidationResults } from "./helpers/structure.mjs";

import { execAsync, validateCommand, checkCommandGate, handleLargeOutput } from "./helpers/commands.mjs";

import { processSentinel, isPathProtected } from "./helpers/sentinel.mjs";

import { writeScript } from "./helpers/scripts.mjs";

import { prepareContext } from "./helpers/context.mjs";

import { getPluginCatalog, getPluginDocs } from "./helpers/plugins.mjs";

import { getDocumentCatalog, getDocument, searchDocs } from "./helpers/docs.mjs";

import { getSchemaCatalog, getSchema } from "./helpers/schemas.mjs";

import {
  listTenets, getTenet, getTenets, listViolations, searchTenets,
  formatTenetList, formatTenetDetail, formatViolationList, formatSearchResults,
  addViolation, editViolation, removeViolation,
  editTenet, addTenet, removeTenet, renumberTenet,
  validateTenets, formatValidateTenetsResult,
  TENETS_INDEX_REL_PATH, TENETS_HISTORY_REL_PATH,
  CATEGORIES,
} from "./helpers/tenets.mjs";

import {
  getServiceDetails, getEventCatalog, getStateStoreCatalog, getConfigurationCatalog,
  printModelShapes, printInterfaceShapes,
} from "./helpers/infrastructure.mjs";

import { gatedToolResponse } from "./helpers/tool-output.mjs";

import { getGeneratorCatalog, runGenerator } from "./helpers/generators.mjs";

import { startScope, addScope, stopScope, listScopes, getActiveScope, getActiveScopeStack } from "./helpers/scope.mjs";
import { grantedPermissions } from "./helpers/sentinel.mjs";

import { buildWorkerPrompt, spawnWorker, saveWorkerLog, MAX_WORKER_SCOPES } from "./helpers/worker.mjs";

import { checkCoverage, formatCoverageReport } from "./helpers/coverage.mjs";

import {
  auditGatePending, AUDIT_ATTESTATION,
  setAuditGate, clearAuditGateState,
} from "./state.mjs";

import { fetchPage, getAllowedDomains, researchDomain, getValidDomains, getImplementedDomains } from "./helpers/research.mjs";

import { initDashboardEmitter, emitToolEvent } from "./helpers/dashboard-emitter.mjs";

import { dirname } from "node:path";
import { mkdir } from "node:fs/promises";

// ─── Server ────────────────────────────────────────────────────────────────

const server = new McpServer({
  name: "bannou-read",
  version: "3.0.0",
});

// ─── Sentinel Auto-Processing ──────────────────────────────────────────────
// Wrap registerTool so every tool call processes the sentinel file first.
// If a human injected commands via /tmp/bannou-mcp-inject.json, they are
// applied before the tool handler runs. Injected messages are prepended
// to the tool response.

const _originalRegisterTool = server.registerTool.bind(server);
server.registerTool = (name, options, handler) => {
  _originalRegisterTool(name, options, async (args) => {
    const sentinelMsg = await processSentinel();
    const result = await handler(args);

    // If sentinel injected a message, prepend it to the response
    if (sentinelMsg && result.content && result.content.length > 0) {
      result.content[0].text = `\ud83d\udce8 [External injection]: ${sentinelMsg}\n\n${result.content[0].text}`;
    }

    // Broadcast tool event to dashboard (fire-and-forget, never throws)
    try { emitToolEvent(name, args, result); } catch { /* never break MCP */ }

    return result;
  });
};

// ═══════════════════════════════════════════════════════════════════════════
// FILE OPERATION TOOLS
// ═══════════════════════════════════════════════════════════════════════════

// ─── Tool: read_file ───────────────────────────────────────────────────────

server.registerTool(
  "read_file",
  {
    description: [
      "Read a file's COMPLETE contents with line numbers. No truncation. No pagination. No size limits.",
      "",
      "This tool reads the ENTIRE file regardless of size and returns ALL content.",
      "It never truncates, never suggests using limit/offset, and never breaks files into chunks.",
      "",
      "Use this instead of the built-in Read tool when you need the full file.",
      "Returns content in 'cat -n' format (line number + tab + content).",
    ].join("\n"),
    inputSchema: {
      file_path: z.string().describe("Absolute path to the file to read"),
    },
  },
  async ({ file_path }) => {
    try {
      const resolvedPath = resolve(file_path);

      try {
        await access(resolvedPath, constants.R_OK);
      } catch {
        return mcpResponse("read_file", "err", `Error: File not found or not readable: ${resolvedPath}`);
      }

      const fileStats = await stat(resolvedPath);

      if (fileStats.isDirectory()) {
        return mcpResponse("read_file", "err", `Error: ${resolvedPath} is a directory, not a file. Use ls or Glob to list directory contents.`);
      }

      const ext = extname(resolvedPath).toLowerCase();
      if (BINARY_EXTENSIONS.has(ext)) {
        return mcpResponse("read_file", "err", `Error: ${resolvedPath} is a binary file (${ext}). Binary files cannot be read as text.`);
      }

      // --- Persisted tool output: parse JSON and extract text content ---
      if (isPersistedOutputFile(resolvedPath)) {
        const rawJson = await readFile(resolvedPath, "utf-8");
        const extracted = extractPersistedContent(rawJson);

        if (extracted) {
          readFiles.add(resolvedPath);
          const sizeKB = (Buffer.byteLength(extracted, "utf-8") / 1024).toFixed(1);
          return mcpResponse("read_file", "ok", `${resolvedPath} (persisted output, ${sizeKB} KB extracted)\n${extracted}`);
        }
        // If extraction fails, fall through to normal read (returns raw JSON)
      }

      // --- Continuation file: return as-is without re-numbering ---
      if (isContinuationFile(resolvedPath)) {
        readFiles.add(resolvedPath);

        const originalPath = continuationToOriginal.get(resolvedPath);
        if (originalPath) {
          const pending = pendingContinuations.get(originalPath);
          if (pending) {
            pending.delete(resolvedPath);
            if (pending.size === 0) pendingContinuations.delete(originalPath);
          }
          continuationToOriginal.delete(resolvedPath);
        }

        const content = await readFile(resolvedPath, "utf-8");
        return mcpResponse("read_file", "ok", content);
      }

      // --- Composite context file: return as-is, clear from requiredReading ---
      if (isCompositeFile(resolvedPath)) {
        readFiles.add(resolvedPath);
        requiredReading.delete(resolvedPath);

        const content = await readFile(resolvedPath, "utf-8");
        const sizeKB = (fileStats.size / 1024).toFixed(1);
        return mcpResponse("read_file", "ok", `${resolvedPath} (${sizeKB} KB — context composite)\n${content}`);
      }

      // --- Normal file: read and number ---
      const content = await readFile(resolvedPath, "utf-8");
      const lines = content.split("\n");
      readFiles.add(resolvedPath);

      // Clear from requiredReading if present (handles research documents
      // and any other non-composite files added to the reading gate)
      if (requiredReading.has(resolvedPath)) {
        requiredReading.delete(resolvedPath);
      }

      const maxWidth = String(lines.length).length;
      const numberedLines = lines.map((line, i) => {
        const num = String(i + 1).padStart(Math.max(maxWidth, 6));
        return `${num}\t${line}`;
      });

      const fullText = numberedLines.join("\n");
      const sizeKB = (fileStats.size / 1024).toFixed(1);
      const sizeBytes = fileStats.size;
      const responseText = `${resolvedPath} (${lines.length} lines, ${sizeKB} KB, ${sizeBytes} bytes)\n${fullText}`;

      // Small file: return everything (check serialized size to avoid Claude Code persisting the response)
      if (!wouldExceedResponseLimit(responseText)) {
        return mcpResponse("read_file", "ok", responseText);
      }

      // Large file: split and write continuation files
      const chunks = splitIntoChunks(numberedLines);
      const hash = pathHash(resolvedPath);

      // Degenerate case: file is under CHUNK_BYTE_LIMIT raw but over
      // RESPONSE_SERIALIZED_LIMIT serialized (heavy escaping overhead).
      // Write entire content to a single continuation file to avoid persistence.
      if (chunks.length === 1) {
        const contPath = join(tmpdir(), `${CONTINUATION_PREFIX}${hash}-full.txt`);
        const contContent = `${resolvedPath} (${lines.length} lines, ${sizeKB} KB, ${sizeBytes} bytes)\n\n${chunks[0].text}`;
        await writeFile(contPath, contContent, "utf-8");
        pendingContinuations.set(resolvedPath, new Set([contPath]));
        continuationToOriginal.set(contPath, resolvedPath);

        return mcpResponse("read_file", "ok", [
          `${resolvedPath} (${lines.length} lines, ${sizeKB} KB, ${sizeBytes} bytes — continuation required)`,
          "",
          `File exceeds inline response limit after JSON serialization.`,
          `Read the continuation file to access the full content:`,
          `  ${contPath}`,
        ].join("\n"));
      }

      const continuationPaths = [];
      const pendingSet = new Set();

      for (let i = 1; i < chunks.length; i++) {
        const chunk = chunks[i];
        const contPath = join(tmpdir(), `${CONTINUATION_PREFIX}${hash}-part${i + 1}.txt`);
        const contHeader = `${resolvedPath} \u2014 part ${i + 1} of ${chunks.length} (lines ${chunk.startLine}-${chunk.endLine} of ${lines.length})\n\n`;
        await writeFile(contPath, contHeader + chunk.text, "utf-8");
        continuationPaths.push({ path: contPath, chunk });
        pendingSet.add(contPath);
        continuationToOriginal.set(contPath, resolvedPath);
      }

      if (pendingSet.size > 0) pendingContinuations.set(resolvedPath, pendingSet);

      const firstChunk = chunks[0];
      const header = [
        `${resolvedPath} (${lines.length} lines, ${sizeKB} KB, ${sizeBytes} bytes — split into ${chunks.length} parts)`,
        "",
        `\u26a0\ufe0f SPLIT FILE — read ALL continuation parts before editing or running commands:`,
        ...continuationPaths.map(({ path, chunk }, i) =>
          `  ${path}  (Part ${i + 2} of ${chunks.length}, lines ${chunk.startLine}-${chunk.endLine})`),
        "",
        `Part 1 of ${chunks.length}: lines ${firstChunk.startLine}-${firstChunk.endLine}`,
      ].join("\n") + "\n";

      return mcpResponse("read_file", "ok", header + firstChunk.text);
    } catch (err) {
      return mcpResponse("read_file", "err", `Error reading ${file_path}: ${err.message}`);
    }
  },
);

// ─── Tool: edit_file ───────────────────────────────────────────────────────

server.registerTool(
  "edit_file",
  {
    description: [
      "Performs exact string replacements in files.",
      "",
      "You must use read_file at least once on a file before editing it.",
      "The edit will FAIL if old_string is not found in the file.",
      "The edit will FAIL if old_string is not unique (appears more than once) unless replace_all is true.",
      "old_string and new_string must be different.",
      "",
      "When editing text from read_file output, preserve the exact indentation",
      "(tabs/spaces) as shown in the file content after the line number prefix.",
      "Never include line number prefixes in old_string or new_string.",
    ].join("\n"),
    inputSchema: {
      file_path: z.string().describe("Absolute path to the file to modify"),
      old_string: z.string().describe("The exact text to find and replace"),
      new_string: z.string().describe("The replacement text (must differ from old_string)"),
      replace_all: z.boolean().optional().default(false)
        .describe("Replace all occurrences (default: false, requires unique match)"),
    },
  },
  async ({ file_path, old_string, new_string, replace_all }) => {
    try {
      const resolvedPath = resolve(file_path);

      const protection = await isPathProtected(resolvedPath);
      if (protection.blocked) {
        return mcpResponse("edit_file", "err", `Error: ${protection.reason}`);
      }

      const readingGate = checkRequiredReadingGate("edit_file");
      if (readingGate) return readingGate;

      const gateResult = checkFileGate(resolvedPath, "edit_file");
      if (gateResult) return gateResult;

      if (old_string === new_string) return mcpResponse("edit_file", "err", "Error: old_string and new_string are identical. Nothing to change.");
      if (old_string.length === 0) return mcpResponse("edit_file", "err", "Error: old_string cannot be empty.");

      const content = await readFile(resolvedPath, "utf-8");
      const occurrences = countOccurrences(content, old_string);

      if (occurrences === 0) return mcpResponse("edit_file", "err", `Error: old_string not found in ${resolvedPath}.\n\nEnsure you are matching the exact text from the file, including\nindentation (tabs/spaces). Do not include line number prefixes.`);
      if (!replace_all && occurrences > 1) return mcpResponse("edit_file", "err", `Error: old_string appears ${occurrences} times in ${resolvedPath}.\nProvide more surrounding context to make it unique, or set replace_all to true.`);

      let newContent;
      if (replace_all) {
        newContent = content.split(old_string).join(new_string);
      } else {
        const idx = content.indexOf(old_string);
        newContent = content.slice(0, idx) + new_string + content.slice(idx + old_string.length);
      }

      await writeFile(resolvedPath, newContent, "utf-8");

      // ── Post-write verification: read back and confirm edit landed ──
      // Defends against silent failures (transient disk issues, client-side
      // tool-result caching glitches, race conditions). If on-disk content
      // doesn't match what we just wrote, return an error rather than a
      // deceptive "success" response. The agent then re-reads the file to
      // inspect actual state and retry.
      const verifyContent = await readFile(resolvedPath, "utf-8");
      if (verifyContent !== newContent) {
        return mcpResponse(
          "edit_file",
          "err",
          [
            `Edit applied to ${resolvedPath} but POST-WRITE VERIFICATION FAILED.`,
            `  Expected: ${Buffer.byteLength(newContent, "utf-8")} bytes`,
            `  On-disk:  ${Buffer.byteLength(verifyContent, "utf-8")} bytes`,
            ``,
            `Re-read the file with read_file to inspect actual state, then retry.`,
          ].join("\n"),
        );
      }

      const count = replace_all ? occurrences : 1;
      return mcpResponse(
        "edit_file",
        "ok",
        `Successfully edited ${resolvedPath}. ${count} replacement${count > 1 ? "s" : ""} made (verified).`,
      );
    } catch (err) {
      return mcpResponse("edit_file", "err", `Error editing ${file_path}: ${err.message}`);
    }
  },
);

// ─── Tool: write_file ──────────────────────────────────────────────────────

server.registerTool(
  "write_file",
  {
    description: [
      "Write complete file contents. Replaces the built-in Write tool.",
      "",
      "Uses this server's own read-tracking gate (read_file must have been called first",
      "for existing files). For NEW files that don't exist yet, no prior read is required.",
      "",
      "SAFETY: If the gate check fails (file not read yet), the content is NOT lost.",
      "It is saved to a /tmp/ recovery file and the path is returned in the error.",
      "After re-reading the target file, call write_file again with the same content,",
      "or use read_file on the recovery path to retrieve it.",
    ].join("\n"),
    inputSchema: {
      file_path: z.string().describe("Absolute path to the file to write"),
      content: z.string().describe("The complete file content to write"),
      expected_size_bytes: z.number().int().optional()
        .describe("Optional: expected current file size in bytes (race condition check). If provided and the file's actual size differs, the write is rejected. Use the size reported by read_file to ensure no changes occurred between read and write."),
      dry_run: z.boolean().optional().default(false)
        .describe("If true, validates all gates and checks without writing. Returns what would happen."),
    },
  },
  async ({ file_path, content, expected_size_bytes, dry_run }) => {
    try {
      const resolvedPath = resolve(file_path);

      const readingGate = checkRequiredReadingGate("write_file");
      if (readingGate) return readingGate;

      const protection = await isPathProtected(resolvedPath);
      if (protection.blocked) {
        // Still preserve content — never lose agent work, even on protection blocks
        const hash = createHash("md5").update(resolvedPath).digest("hex").slice(0, 12);
        const recoveryPath = join(tmpdir(), `bannou-write-recovery-${hash}.txt`);
        await writeFile(recoveryPath, content, "utf-8");
        return {
          content: [{ type: "text", text: [
            `Error: ${protection.reason}`,
            "",
            "\u2500\u2500\u2500 CONTENT PRESERVED \u2500\u2500\u2500",
            `Your file content has been saved to: ${recoveryPath}`,
            `The recovery file contains the exact content you provided (${content.length} chars).`,
          ].join("\n") }],
          isError: true,
        };
      }

      // For existing files: gate check (must have been read first)
      // For new files: skip gate (file doesn't exist yet)
      let fileExists = false;
      let currentSize = 0;
      try {
        const fileStats = await stat(resolvedPath);
        fileExists = true;
        currentSize = fileStats.size;
      } catch {
        // File doesn't exist — that's fine, we're creating it
      }

      if (fileExists) {
        // ── Gate: file must have been read ──
        const gateResult = checkFileGate(resolvedPath, "write_file");
        if (gateResult) {
          if (dry_run) {
            return {
              content: [{ type: "text", text: `[DRY RUN] Gate check FAILED for ${resolvedPath}:\n${gateResult.content[0].text}` }],
              isError: true,
            };
          }
          // RECOVERY: save content to /tmp so it's not lost
          const hash = createHash("md5").update(resolvedPath).digest("hex").slice(0, 12);
          const recoveryPath = join(tmpdir(), `bannou-write-recovery-${hash}.txt`);
          await writeFile(recoveryPath, content, "utf-8");

          const gateText = gateResult.content[0].text;
          return {
            content: [{
              type: "text",
              text: [
                gateText, "",
                "\u2500\u2500\u2500 CONTENT PRESERVED \u2500\u2500\u2500",
                `Your file content has been saved to: ${recoveryPath}`,
                `After reading the target file, retry the write.`,
                `The recovery file contains the exact content you provided (${content.length} chars).`,
              ].join("\n"),
            }],
            isError: true,
          };
        }

        // ── Race condition check: verify file hasn't changed since read ──
        if (expected_size_bytes !== undefined && expected_size_bytes !== null) {
          if (currentSize !== expected_size_bytes) {
            const msg = [
              `Error: File size mismatch — race condition detected.`,
              `  Expected: ${expected_size_bytes} bytes (from your read_file call)`,
              `  Actual:   ${currentSize} bytes (file on disk right now)`,
              ``,
              `The file was modified between your read and this write.`,
              `Re-read the file with read_file to get current content, then retry.`,
            ].join("\n");

            if (dry_run) {
              return { content: [{ type: "text", text: `[DRY RUN] Size check FAILED:\n${msg}` }], isError: true };
            }

            // Still preserve content on race condition failure
            const hash = createHash("md5").update(resolvedPath).digest("hex").slice(0, 12);
            const recoveryPath = join(tmpdir(), `bannou-write-recovery-${hash}.txt`);
            await writeFile(recoveryPath, content, "utf-8");

            return {
              content: [{ type: "text", text: `${msg}\n\nContent preserved at: ${recoveryPath}` }],
              isError: true,
            };
          }
        }
      }

      // ── Dry run: report what would happen without writing ──
      if (dry_run) {
        const newLines = content.split("\n").length;
        const newSizeKB = (Buffer.byteLength(content, "utf-8") / 1024).toFixed(1);
        const action = fileExists ? "overwrite" : "create";
        const sizeInfo = fileExists ? ` (current: ${currentSize} bytes)` : "";
        return {
          content: [{
            type: "text",
            text: [
              `[DRY RUN] Would ${action} ${resolvedPath}${sizeInfo}`,
              `  New content: ${newLines} lines, ${newSizeKB} KB`,
              `  Gate: PASSED`,
              fileExists && expected_size_bytes !== undefined ? `  Size check: PASSED (${currentSize} === ${expected_size_bytes})` : null,
            ].filter(Boolean).join("\n"),
          }],
        };
      }

      // ── Ensure parent directory exists ──
      await mkdir(dirname(resolvedPath), { recursive: true });

      // ── Write the file ──
      await writeFile(resolvedPath, content, "utf-8");

      // ── Mark as read (so subsequent edits work without re-reading) ──
      readFiles.add(resolvedPath);

      const lines = content.split("\n").length;
      const sizeKB = (Buffer.byteLength(content, "utf-8") / 1024).toFixed(1);

      // ── Post-write verification: read back, confirm content landed ──
      // Detects silent failures from disk issues, races, or upstream caching.
      const verifyContent = await readFile(resolvedPath, "utf-8");
      if (verifyContent !== content) {
        return mcpResponse(
          "write_file",
          "err",
          [
            `Write to ${resolvedPath} completed but POST-WRITE VERIFICATION FAILED.`,
            `  Expected: ${Buffer.byteLength(content, "utf-8")} bytes`,
            `  On-disk:  ${Buffer.byteLength(verifyContent, "utf-8")} bytes`,
            ``,
            `Re-read the file with read_file to inspect actual state, then retry.`,
          ].join("\n"),
        );
      }

      return mcpResponse(
        "write_file",
        "ok",
        `Successfully wrote ${resolvedPath} (${lines} lines, ${sizeKB} KB)${fileExists ? "" : " [new file created]"} (verified).`,
      );
    } catch (err) {
      // Last resort: try to save content to /tmp even on unexpected errors
      try {
        const hash = createHash("md5").update(file_path).digest("hex").slice(0, 12);
        const recoveryPath = join(tmpdir(), `bannou-write-recovery-${hash}.txt`);
        await writeFile(recoveryPath, content, "utf-8");
        return mcpResponse("write_file", "err", `Error writing ${file_path}: ${err.message}\n\nContent preserved at: ${recoveryPath}`);
      } catch {
        return mcpResponse("write_file", "err", `Error writing ${file_path}: ${err.message} (content could not be preserved)`);
      }
    }
  },
);

// ═══════════════════════════════════════════════════════════════════════════
// STRUCTURE VALIDATION & LINE MOVE TOOLS
// ═══════════════════════════════════════════════════════════════════════════

// ─── Tool: move_lines ──────────────────────────────────────────────────────

server.registerTool(
  "move_lines",
  {
    description: [
      "Move a range of lines from one file to another with mechanical precision.",
      "",
      "Extracts lines start_line through end_line (inclusive, 1-based) from source_file,",
      "inserts them into dest_file after dest_insert_after_line (0 = top of file),",
      "and removes them from source_file. Both operations happen atomically.",
      "",
      "Both files must have been read via read_file first (including all continuation parts).",
      "",
      "Optional safety anchors: if expect_first_line_contains or expect_last_line_contains",
      "is provided, the tool verifies that the actual line content contains the expected",
      "substring before proceeding. This catches off-by-one errors.",
    ].join("\n"),
    inputSchema: {
      source_file: z.string().describe("Absolute path to the file to move lines FROM"),
      start_line: z.number().int().min(1).describe("First line to move (1-based, inclusive)"),
      end_line: z.number().int().min(1).describe("Last line to move (1-based, inclusive)"),
      dest_file: z.string().describe("Absolute path to the file to move lines TO"),
      dest_insert_after_line: z.number().int().min(0).describe("Insert after this line number in dest (0 = insert at top of file)"),
      expect_first_line_contains: z.string().optional().describe("Optional: substring that start_line must contain (safety anchor)"),
      expect_last_line_contains: z.string().optional().describe("Optional: substring that end_line must contain (safety anchor)"),
    },
  },
  async ({ source_file, start_line, end_line, dest_file, dest_insert_after_line,
           expect_first_line_contains, expect_last_line_contains }) => {
    try {
      const readingGate = checkRequiredReadingGate("move_lines");
      if (readingGate) return readingGate;

      const resolvedSource = resolve(source_file);
      const resolvedDest = resolve(dest_file);

      // Scope/frozen protection — both files must be writable
      const sourceProtection = await isPathProtected(resolvedSource);
      if (sourceProtection.blocked) {
        return mcpResponse("move_lines", "err", `Error (source): ${sourceProtection.reason}`);
      }
      const destProtection = await isPathProtected(resolvedDest);
      if (destProtection.blocked) {
        return mcpResponse("move_lines", "err", `Error (dest): ${destProtection.reason}`);
      }

      const sourceGate = checkFileGate(resolvedSource, "move_lines");
      if (sourceGate) return sourceGate;
      const destGate = checkFileGate(resolvedDest, "move_lines");
      if (destGate) return destGate;

      if (end_line < start_line) return mcpResponse("move_lines", "err", `Error: end_line (${end_line}) must be >= start_line (${start_line}).`);

      const sourceContent = await readFile(resolvedSource, "utf-8");
      const destContent = await readFile(resolvedDest, "utf-8");
      const sourceLines = sourceContent.split("\n");
      const destLines = destContent.split("\n");

      if (start_line > sourceLines.length) return mcpResponse("move_lines", "err", `Error: start_line (${start_line}) exceeds source file length (${sourceLines.length} lines).`);
      if (end_line > sourceLines.length) return mcpResponse("move_lines", "err", `Error: end_line (${end_line}) exceeds source file length (${sourceLines.length} lines).`);
      if (dest_insert_after_line > destLines.length) return mcpResponse("move_lines", "err", `Error: dest_insert_after_line (${dest_insert_after_line}) exceeds dest file length (${destLines.length} lines).`);

      const firstLine = sourceLines[start_line - 1];
      const lastLine = sourceLines[end_line - 1];

      if (expect_first_line_contains && !firstLine.includes(expect_first_line_contains)) {
        return mcpResponse("move_lines", "err", `Error: Safety anchor mismatch on start_line ${start_line}.\nExpected line to contain: "${expect_first_line_contains}"\nActual line content: "${firstLine.slice(0, 200)}"\n\nThis usually means a line number is off by one or the file was modified.`);
      }
      if (expect_last_line_contains && !lastLine.includes(expect_last_line_contains)) {
        return mcpResponse("move_lines", "err", `Error: Safety anchor mismatch on end_line ${end_line}.\nExpected line to contain: "${expect_last_line_contains}"\nActual line content: "${lastLine.slice(0, 200)}"\n\nThis usually means a line number is off by one or the file was modified.`);
      }

      const movedLines = sourceLines.slice(start_line - 1, end_line);
      const newSourceLines = [...sourceLines.slice(0, start_line - 1), ...sourceLines.slice(end_line)];
      const newDestLines = [...destLines.slice(0, dest_insert_after_line), ...movedLines, ...destLines.slice(dest_insert_after_line)];

      const newSourceContent = newSourceLines.join("\n");
      const newDestContent = newDestLines.join("\n");
      await writeFile(resolvedSource, newSourceContent, "utf-8");
      await writeFile(resolvedDest, newDestContent, "utf-8");

      // ── Post-write verification: read back both files, confirm content landed ──
      // Defends against silent failures (transient disk issues, races, caching glitches).
      // If either file mismatches, both may now be in inconsistent state — surface loudly.
      const verifySource = await readFile(resolvedSource, "utf-8");
      const verifyDest = await readFile(resolvedDest, "utf-8");
      if (verifySource !== newSourceContent || verifyDest !== newDestContent) {
        const mismatches = [];
        if (verifySource !== newSourceContent) {
          mismatches.push(`  Source: expected ${Buffer.byteLength(newSourceContent, "utf-8")} bytes, on-disk ${Buffer.byteLength(verifySource, "utf-8")} bytes`);
        }
        if (verifyDest !== newDestContent) {
          mismatches.push(`  Dest:   expected ${Buffer.byteLength(newDestContent, "utf-8")} bytes, on-disk ${Buffer.byteLength(verifyDest, "utf-8")} bytes`);
        }
        return mcpResponse(
          "move_lines",
          "err",
          [
            `Move completed but POST-WRITE VERIFICATION FAILED.`,
            ...mismatches,
            ``,
            `Re-read both files to inspect actual state. Source/dest may now be in an inconsistent state.`,
          ].join("\n"),
        );
      }

      // Post-move structure validation (shared helper)
      const sourceValidation = validateStructure(newSourceContent, resolvedSource);
      const destValidation = validateStructure(newDestContent, resolvedDest);
      const validationReport = formatValidationResults(sourceValidation, resolvedSource, destValidation, resolvedDest);

      return mcpResponse("move_lines", "ok", [
        `Successfully moved ${movedLines.length} lines (${start_line}-${end_line}) from source to dest (verified).`,
        `  Source: ${resolvedSource} (${sourceLines.length} \u2192 ${newSourceLines.length} lines)`,
        `  Dest:   ${resolvedDest} (${destLines.length} \u2192 ${newDestLines.length} lines)`,
        `  Inserted after line ${dest_insert_after_line} in dest.`,
        `  First moved line: "${firstLine.slice(0, 120)}"`,
        `  Last moved line:  "${lastLine.slice(0, 120)}"`,
        validationReport,
      ].join("\n"));
    } catch (err) {
      return mcpResponse("move_lines", "err", `Error moving lines: ${err.message}`);
    }
  },
);

// ─── Tool: validate_structure ──────────────────────────────────────────────

server.registerTool(
  "validate_structure",
  {
    description: [
      "Validates structural integrity of a C# file.",
      "",
      "Checks for balanced braces, #region/#endregion pairs, and #if/#endif pairs.",
      "Returns OK if all balanced, or lists specific findings with line numbers.",
      "File must have been read via read_file first.",
      "",
      "Use after move_lines or manual edits to catch orphaned regions or missing braces.",
    ].join("\n"),
    inputSchema: {
      file_path: z.string().describe("Absolute path to the C# file to validate"),
    },
  },
  async ({ file_path }) => {
    try {
      const readingGate = checkRequiredReadingGate("validate_structure");
      if (readingGate) return readingGate;

      const resolvedPath = resolve(file_path);
      const gateResult = checkFileGate(resolvedPath, "validate_structure");
      if (gateResult) return gateResult;

      const content = await readFile(resolvedPath, "utf-8");
      const result = validateStructure(content, resolvedPath);

      if (result.ok) return { content: [{ type: "text", text: `\u2705 ${basename(resolvedPath)}: Structure OK (braces, regions, preprocessor directives all balanced)` }] };

      const lines = [`\u26a0\ufe0f ${basename(resolvedPath)}: ${result.findings.length} structural issue(s) found:`, ""];
      for (const f of result.findings) lines.push(`  [${f.type}] ${f.message}${f.line ? ` (line ${f.line})` : ""}`);
      return mcpResponse("validate_structure", "ok", lines.join("\n"));
    } catch (err) {
      return mcpResponse("validate_structure", "err", `Error validating ${file_path}: ${err.message}`);
    }
  },
);

// ═══════════════════════════════════════════════════════════════════════════
// COMMAND EXECUTION TOOLS
// ═══════════════════════════════════════════════════════════════════════════

// ─── Tool: run_command ─────────────────────────────────────────────────────

server.registerTool(
  "run_command",
  {
    description: [
      "Execute a whitelisted command and return its output.",
      "",
      "Only specific commands are allowed: gh (GitHub CLI), dotnet build/test,",
      "make targets, generation scripts, ls, find, wc, git status/diff/log.",
      "",
      "For file reading use read_file. For file editing use edit_file.",
      "This tool is for builds, tests, code generation, GitHub operations,",
      "and file discovery only.",
    ].join("\n"),
    inputSchema: {
      command: z.string().describe("The command to execute (must match an allowed prefix)"),
      timeout_ms: z.number().optional().default(120000)
        .describe("Timeout in milliseconds (default: 120000 = 2 minutes, max: 600000 = 10 minutes)"),
    },
  },
  async ({ command, timeout_ms }) => {
    try {
      const trimmed = command.trim();

      const readingGate = checkRequiredReadingGate();
      if (readingGate) return readingGate;

      const gateResult = checkCommandGate();
      if (gateResult) return gateResult;

      const validationResult = validateCommand(trimmed);
      if (validationResult) return validationResult;

      const timeout = Math.min(Math.max(timeout_ms || 120000, 5000), 600000);
      const cwd = process.env.CLAUDE_PROJECT_DIR || process.cwd();

      const { stdout, stderr } = await execAsync(trimmed, {
        cwd, timeout, maxBuffer: 10 * 1024 * 1024, shell: "/bin/bash",
      });

      const output = (stdout || "") + (stderr ? `\n--- stderr ---\n${stderr}` : "");
      if (output.length === 0) return { content: [{ type: "text", text: "(command completed with no output)" }] };

      return await handleLargeOutput(output, trimmed);
    } catch (err) {
      const message = err.stderr || err.message || String(err);
      const code = err.code === "ERR_CHILD_PROCESS_STDIO_MAXBUFFER"
        ? "Output exceeded 10MB buffer"
        : err.killed ? `Command timed out after ${(timeout_ms || 120000) / 1000}s`
        : `Exit code ${err.code || "unknown"}`;
      return { content: [{ type: "text", text: `Command failed (${code}):\n\n${message.slice(0, CHUNK_BYTE_LIMIT)}` }], isError: true };
    }
  },
);

// ═══════════════════════════════════════════════════════════════════════════
// SCRIPT TOOLS
// ═══════════════════════════════════════════════════════════════════════════

// ─── Tool: write_script ────────────────────────────────────────────────────

server.registerTool(
  "write_script",
  {
    description: [
      "Write a script to the sandboxed scripts directory with automatic chmod +x and syntax validation.",
      "",
      "Scripts are written to /tmp/bannou-scripts/ ONLY — agents cannot create scripts elsewhere.",
      "Allowed extensions: .sh, .py, .mjs",
      "Shebangs are auto-added if missing.",
      "Syntax is validated before returning (bash -n, python3 -m py_compile, node --check).",
      "",
      "Execute scripts via run_command: /tmp/bannou-scripts/my-script.sh",
    ].join("\n"),
    inputSchema: {
      filename: z.string().describe("Script filename with extension (e.g., 'process-data.sh', 'transform.py'). No path separators."),
      content: z.string().describe("The script content. Shebang is auto-added if missing."),
    },
  },
  async ({ filename, content }) => {
    try {
      const readingGate = checkRequiredReadingGate();
      if (readingGate) return readingGate;

      const result = await writeScript(filename, content);

      if (result.error) {
        return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
      }

      const lines = result.content.split("\n").length;
      const validStatus = result.validation.valid ? "\u2705 Syntax OK" : "\u274c Syntax errors found";

      const response = [
        `Successfully wrote ${result.path} (${lines} lines, chmod +x)`,
        "",
        `Validation: ${validStatus}`,
      ];

      if (result.validation.output && result.validation.output !== "Syntax OK") {
        response.push(result.validation.output);
      }

      if (result.validation.valid) {
        response.push("", `Execute with: run_command("${result.path}")`);
      }

      return { content: [{ type: "text", text: response.join("\n") }] };
    } catch (err) {
      return { content: [{ type: "text", text: `Error writing script: ${err.message}` }], isError: true };
    }
  },
);

// ═══════════════════════════════════════════════════════════════════════════
// DOCUMENTATION & INTROSPECTION TOOLS (Phase 1b of MCP-SERVER.md)
// ═══════════════════════════════════════════════════════════════════════════

// ─── Tool: list_plugins ───────────────────────────────────────────────────

server.registerTool(
  "list_plugins",
  {
    description: [
      "List all Bannou plugins with layer, endpoint count, and documentation availability.",
      "",
      "Returns a structured catalog of all 78 service plugins parsed from the",
      "composition reference, with flags indicating whether each plugin has a",
      "deep dive document and implementation map.",
    ].join("\n"),
    inputSchema: {},
  },
  async () => {
    const result = await getPluginCatalog();
    if (result.error) {
      return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
    }

    const lines = [`Bannou Plugin Catalog (${result.plugins.length} plugins, ${result.totalEndpoints} total endpoints)`, ""];

    // Group by layer
    const byLayer = {};
    for (const p of result.plugins) {
      if (!byLayer[p.layer]) byLayer[p.layer] = [];
      byLayer[p.layer].push(p);
    }

    for (const [layer, plugins] of Object.entries(byLayer)) {
      const layerEP = plugins.reduce((s, p) => s + p.endpoints, 0);
      lines.push(`[${layer}] (${plugins.length} plugins, ${layerEP} endpoints)`);
      for (const p of plugins) {
        const docs = [p.hasDeepDive ? "📄" : "  ", p.hasMap ? "🗺️" : "  "].join("");
        lines.push(`  ${docs} ${p.name.padEnd(24)} ${String(p.endpoints).padStart(3)} EP  ${p.role}`);
      }
      lines.push("");
    }

    lines.push("Legend: 📄 = deep dive, 🗺️ = implementation map");

    return gatedToolResponse({
      text: lines.join("\n"),
      sourceFiles: ["docs/generated/GENERATED-COMPOSITION-REFERENCE.md"],
      label: "Plugin Catalog",
    });
  },
);

// ─── Tool: get_plugin_docs ────────────────────────────────────────────────

server.registerTool(
  "get_plugin_docs",
  {
    description: [
      "Get deep dive documentation and implementation map for a specific plugin.",
      "",
      "Returns both the deep dive (high-level context, quirks, design rationale)",
      "and the implementation map (method-level detail, state keys, dependencies).",
      "Use list_plugins to see available plugins.",
    ].join("\n"),
    inputSchema: {
      name: z.string().describe("Plugin name (e.g., 'account', 'divine', 'character')"),
    },
  },
  async ({ name }) => {
    const result = await getPluginDocs(name);
    if (result.error) {
      return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
    }

    const sections = [];
    if (result.deepDive) {
      sections.push(`# Deep Dive: ${result.name}\n# Source: ${result.deepDivePath}\n\n${result.deepDive}`);
    } else {
      sections.push(`# Deep Dive: ${result.name}\n(not available)`);
    }

    sections.push("\n" + "═".repeat(76) + "\n");

    if (result.map) {
      sections.push(`# Implementation Map: ${result.name}\n# Source: ${result.mapPath}\n\n${result.map}`);
    } else {
      sections.push(`# Implementation Map: ${result.name}\n(not available)`);
    }

    return gatedToolResponse({
      text: sections.join("\n"),
      sourceFiles: [result.deepDivePath, result.mapPath].filter(Boolean),
      label: `Plugin Docs: ${result.name}`,
    });
  },
);

// ─── Tool: list_documents ─────────────────────────────────────────────────

server.registerTool(
  "list_documents",
  {
    description: [
      "List all documentation organized by category (guides, planning, FAQs, operations, specifications, SDKs).",
      "",
      "Returns the content of all 6 generated catalog files. Each catalog contains",
      "summaries, metadata, and direct links — read catalogs to find relevant docs",
      "before opening individual files.",
    ].join("\n"),
    inputSchema: {
      category: z.string().optional().describe("Filter to a specific category: guides, planning, faqs, operations, specifications, sdks"),
    },
  },
  async ({ category }) => {
    const result = await getDocumentCatalog();
    if (result.error) {
      return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
    }

    if (category) {
      const key = category.toLowerCase();
      const cat = result.catalogs[key];
      if (!cat) {
        const available = Object.keys(result.catalogs).join(", ");
        return { content: [{ type: "text", text: `Unknown category "${category}". Available: ${available}` }], isError: true };
      }
      return gatedToolResponse({
        text: `[${cat.label}]\n\n${cat.content}`,
        label: `Document Catalog: ${cat.label}`,
      });
    }

    // All catalogs
    const sections = Object.values(result.catalogs).map((cat) =>
      `${"═".repeat(76)}\n[${cat.label}]\n${"═".repeat(76)}\n\n${cat.content}`
    );

    return gatedToolResponse({
      text: sections.join("\n\n"),
      label: "Document Catalog (all categories)",
    });
  },
);

// ─── Tool: get_document ───────────────────────────────────────────────────

server.registerTool(
  "get_document",
  {
    description: [
      "Get a specific documentation file by path.",
      "",
      "Accepts paths relative to the project root (e.g., 'docs/guides/ECONOMY-SYSTEM.md')",
      "or relative to docs/ (e.g., 'guides/ECONOMY-SYSTEM.md').",
    ].join("\n"),
    inputSchema: {
      path: z.string().describe("Document path (relative to project root or docs/)"),
    },
  },
  async ({ path }) => {
    const result = await getDocument(path);
    if (result.error) {
      return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
    }

    return gatedToolResponse({
      text: `# ${result.path}\n\n${result.content}`,
      sourceFiles: [result.path],
      label: `Document: ${result.path}`,
    });
  },
);

// ─── Tool: search_docs ────────────────────────────────────────────────────

server.registerTool(
  "search_docs",
  {
    description: [
      "Full-text search across all documentation files.",
      "",
      "Searches all .md files in docs/ using keyword matching with frequency scoring.",
      "Results are ranked by relevance (title matches weighted higher, all-token matches boosted).",
      "Index is built lazily on first call and cached with mtime-based invalidation.",
    ].join("\n"),
    inputSchema: {
      query: z.string().describe("Search query (keywords)"),
      max_results: z.number().optional().default(20).describe("Maximum results to return (default: 20)"),
    },
  },
  async ({ query, max_results }) => {
    const result = await searchDocs(query, max_results);
    if (result.error) {
      return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
    }

    const lines = [
      `Search: "${result.query}" (tokens: ${result.tokens.join(", ")})`,
      `Found ${result.totalMatches} matching documents, showing top ${result.results.length}:`,
      "",
    ];

    for (let i = 0; i < result.results.length; i++) {
      const r = result.results[i];
      lines.push(`${String(i + 1).padStart(2)}. [score: ${r.score}] ${r.path}`);
      lines.push(`    ${r.title} (${r.lineCount} lines)`);
    }

    return { content: [{ type: "text", text: lines.join("\n") }] };
  },
);

// ═══════════════════════════════════════════════════════════════════════════
// TENET TOOLS
// ═══════════════════════════════════════════════════════════════════════════
//
// Read-only catalog over docs/reference/TENETS.md and docs/reference/tenets/*.
// Replaces the full-file reads that the `dev` profile used to include.
//
// The five tools cover: short-list browsing (list_tenets), full-body detail
// for one or more tenets (get_tenet, get_tenets), Quick Reference violation
// lookup (list_violations), and full-text search across tenet bodies and
// violations (search_tenets).
//
// All tools parse the source files on demand with an mtime-keyed cache, so
// changes to the reference docs are visible immediately.

// ─── Tool: list_tenets ────────────────────────────────────────────────────

server.registerTool(
  "list_tenets",
  {
    description: [
      "List all tenets with a compact one-line summary per tenet. This is the",
      "short-list view — use this to find the relevant tenet(s) for a task, then",
      "call get_tenet for full body.",
      "",
      "Output is grouped by category (meta, schema-rules, service-hierarchy,",
      "foundation, implementation-behavior, implementation-data, quality) and",
      "includes each tenet's severity, summary rule, and Quick Reference violation",
      "count.",
      "",
      "Filters (all optional):",
      "  category  — kebab-case category id (e.g., 'foundation', 'quality')",
      "  severity  — severity token (e.g., 'INVIOLABLE', 'ABSOLUTE', 'MANDATORY')",
      "  keyword   — case-insensitive substring match over name + rule",
    ].join("\n"),
    inputSchema: {
      category: z.string().optional()
        .describe("Category id: meta, schema-rules, service-hierarchy, foundation, implementation-behavior, implementation-data, quality"),
      severity: z.string().optional()
        .describe("Severity token (e.g., INVIOLABLE, ABSOLUTE, MANDATORY, REQUIRED)"),
      keyword: z.string().optional().describe("Substring match over name + rule"),
    },
  },
  async ({ category, severity, keyword }) => {
    try {
      const result = await listTenets({ category, severity, keyword });
      return { content: [{ type: "text", text: formatTenetList(result) }] };
    } catch (err) {
      return { content: [{ type: "text", text: `list_tenets error: ${err.message}` }], isError: true };
    }
  },
);

// ─── Tool: get_tenet ──────────────────────────────────────────────────────

server.registerTool(
  "get_tenet",
  {
    description: [
      "Get full detail for one tenet: heading, severity, category metadata, rule,",
      "full body, and every Quick Reference violation row citing this tenet.",
      "",
      "Use list_tenets first to browse — call get_tenet only for the specific",
      "tenet(s) relevant to your task.",
    ].join("\n"),
    inputSchema: {
      id: z.string().describe("Tenet id (e.g., 'T4', 'T27')"),
    },
  },
  async ({ id }) => {
    try {
      const tenet = await getTenet(id);
      if (!tenet) {
        return { content: [{ type: "text", text: `Tenet ${id} not found.` }], isError: true };
      }
      return { content: [{ type: "text", text: formatTenetDetail(tenet) }] };
    } catch (err) {
      return { content: [{ type: "text", text: `get_tenet error: ${err.message}` }], isError: true };
    }
  },
);

// ─── Tool: get_tenets ─────────────────────────────────────────────────────

server.registerTool(
  "get_tenets",
  {
    description: [
      "Batch-get multiple tenets by id. Returns the full detail view for each.",
      "Unknown ids are reported separately.",
      "",
      "Use this for focused audits — pull the 2-4 tenets relevant to a code",
      "review instead of loading the full tenet bundle.",
    ].join("\n"),
    inputSchema: {
      ids: z.array(z.string()).min(1)
        .describe("Array of tenet ids (e.g., ['T4', 'T5', 'T27'])"),
    },
  },
  async ({ ids }) => {
    try {
      const { tenets, notFound } = await getTenets(ids);
      const sections = [];
      if (notFound.length > 0) {
        sections.push(`⚠️  Not found: ${notFound.join(", ")}`);
        sections.push("");
      }
      for (const t of tenets) {
        sections.push(formatTenetDetail(t));
        sections.push("\n" + "═".repeat(76) + "\n");
      }
      return { content: [{ type: "text", text: sections.join("\n").trimEnd() }] };
    } catch (err) {
      return { content: [{ type: "text", text: `get_tenets error: ${err.message}` }], isError: true };
    }
  },
);

// ─── Tool: list_violations ────────────────────────────────────────────────

server.registerTool(
  "list_violations",
  {
    description: [
      "List rows from the Quick Reference violations table (the common-violations",
      "catalog at the bottom of TENETS.md). Each row has a violation description,",
      "the cited tenet(s), and the prescribed fix.",
      "",
      "Use this when searching for a specific anti-pattern or when auditing code",
      "against known violations. Filters:",
      "  tenet    — tenet id (string) or array of ids; match if any cited tenet",
      "             intersects the filter",
      "  keyword  — case-insensitive substring over violation + fix text",
    ].join("\n"),
    inputSchema: {
      tenet: z.union([z.string(), z.array(z.string())]).optional()
        .describe("Tenet id or array of ids to filter by"),
      keyword: z.string().optional().describe("Substring match over violation + fix"),
    },
  },
  async ({ tenet, keyword }) => {
    try {
      const result = await listViolations({ tenet, keyword });
      return { content: [{ type: "text", text: formatViolationList(result) }] };
    } catch (err) {
      return { content: [{ type: "text", text: `list_violations error: ${err.message}` }], isError: true };
    }
  },
);

// ─── Tool: search_tenets ──────────────────────────────────────────────────

server.registerTool(
  "search_tenets",
  {
    description: [
      "Full-text keyword search across tenet bodies + Quick Reference violations.",
      "Results are ranked by match strength:",
      "  Name hit:        weight 10",
      "  Summary hit:     weight 4",
      "  Rule hit:        weight 4",
      "  Body hit:        weight 1 per occurrence",
      "  Violation hit:   weight 2 per row",
      "",
      "Tokens are whitespace-split; a tenet matches if every token appears somewhere",
      "in its combined searchable text (case-insensitive).",
    ].join("\n"),
    inputSchema: {
      query: z.string().describe("Search query (one or more keywords)"),
      max_results: z.number().optional().default(20).describe("Maximum results (default: 20)"),
    },
  },
  async ({ query, max_results }) => {
    try {
      const result = await searchTenets(query, { maxResults: max_results });
      return { content: [{ type: "text", text: formatSearchResults(result) }] };
    } catch (err) {
      return { content: [{ type: "text", text: `search_tenets error: ${err.message}` }], isError: true };
    }
  },
);

// ═══════════════════════════════════════════════════════════════════════════
// TENET WRITE TOOLS — Quick Reference violation rows
// ═══════════════════════════════════════════════════════════════════════════
//
// Mutate the Quick Reference violations table in docs/reference/TENETS.md.
// Each successful write appends a timestamped entry to
// docs/reference/TENETS-HISTORY.md.
//
// Writes require an active scope that unlocks docs/reference/ (the `tenets`
// scope) — isPathProtected is consulted exactly like edit_file / write_file.
// Callers without the right scope receive the standard protection error
// pointing them at the sentinel grant procedure.

function tenetsIndexAbsPath() {
  const base = process.env.CLAUDE_PROJECT_DIR || process.cwd();
  return resolve(base, TENETS_INDEX_REL_PATH);
}

// ─── Tool: add_violation ──────────────────────────────────────────────────

server.registerTool(
  "add_violation",
  {
    description: [
      "Append a new row to the Quick Reference violations table in TENETS.md.",
      "",
      "Placement rule (tiered, mechanical):",
      "  1. After the LAST row whose primary (first-listed) tenet equals `tenet`",
      "  2. Else after the LAST row that cites `tenet` anywhere in its list",
      "  3. Else at the end of the table",
      "",
      "Rejects the call when:",
      "  - `tenet` is not a known tenet (parsed from any category file)",
      "  - `violation` or `fix` is empty, contains '|', or contains a newline",
      "  - A row with the identical `violation` text already exists",
      "",
      "On success, the caller's tenet-catalog cache is invalidated and a",
      "timestamped row is appended to docs/reference/TENETS-HISTORY.md.",
    ].join("\n"),
    inputSchema: {
      tenet: z.string().describe("Tenet id to cite (e.g., 'T4'). Must be registered."),
      violation: z.string().describe("Violation description (left column). No '|' or newlines."),
      fix: z.string().describe("Prescribed fix (right column). No '|' or newlines."),
    },
  },
  async ({ tenet, violation, fix }) => {
    try {
      const readingGate = checkRequiredReadingGate();
      if (readingGate) return readingGate;

      const indexPath = tenetsIndexAbsPath();
      const protection = await isPathProtected(indexPath);
      if (protection.blocked) {
        return { content: [{ type: "text", text: `Error: ${protection.reason}` }], isError: true };
      }

      const result = await addViolation({ tenet, violation, fix });
      if (result.error) {
        return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
      }

      const lines = [
        `✓ Added violation row`,
        `  Tenet:     ${result.tenet}`,
        `  Violation: ${result.violation}`,
        `  Fix:       ${result.fix}`,
        `  Inserted:  line ${result.insertedAtLine} of ${result.path}`,
        `  History:   ${TENETS_HISTORY_REL_PATH} appended`,
      ];
      return { content: [{ type: "text", text: lines.join("\n") }] };
    } catch (err) {
      return { content: [{ type: "text", text: `add_violation error: ${err.message}` }], isError: true };
    }
  },
);

// ─── Tool: edit_violation ─────────────────────────────────────────────────

server.registerTool(
  "edit_violation",
  {
    description: [
      "Replace the fix column of an existing Quick Reference violation row.",
      "The row is matched by EXACT `violation` text; the cited-tenets column",
      "and the violation text itself are preserved unchanged.",
      "",
      "Rejects the call when:",
      "  - No row matches the given `violation` text",
      "  - `newFix` is empty, contains '|', or contains a newline",
      "  - `newFix` is identical to the existing fix (no-op)",
      "",
      "On success, the caller's tenet-catalog cache is invalidated and a",
      "timestamped row is appended to docs/reference/TENETS-HISTORY.md.",
    ].join("\n"),
    inputSchema: {
      violation: z.string().describe("Exact violation text of the row to edit (use list_violations to find it)."),
      newFix: z.string().describe("Replacement fix text. No '|' or newlines. Must differ from the current fix."),
    },
  },
  async ({ violation, newFix }) => {
    try {
      const readingGate = checkRequiredReadingGate();
      if (readingGate) return readingGate;

      const indexPath = tenetsIndexAbsPath();
      const protection = await isPathProtected(indexPath);
      if (protection.blocked) {
        return { content: [{ type: "text", text: `Error: ${protection.reason}` }], isError: true };
      }

      const result = await editViolation({ violation, newFix });
      if (result.error) {
        return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
      }

      const lines = [
        `✓ Edited violation row`,
        `  Violation: ${result.violation}`,
        `  Cites:     ${result.tenets.join(", ")}`,
        `  Old fix:   ${result.oldFix}`,
        `  New fix:   ${result.newFix}`,
        `  Line:      ${result.lineEdited} of ${result.path}`,
        `  History:   ${TENETS_HISTORY_REL_PATH} appended`,
      ];
      return { content: [{ type: "text", text: lines.join("\n") }] };
    } catch (err) {
      return { content: [{ type: "text", text: `edit_violation error: ${err.message}` }], isError: true };
    }
  },
);

// ─── Tool: remove_violation ───────────────────────────────────────────────

server.registerTool(
  "remove_violation",
  {
    description: [
      "Delete an existing Quick Reference violation row, matched by EXACT",
      "violation text. No auto-discovery, no fuzzy matching — pass the full",
      "string as it appears in the table (use list_violations first).",
      "",
      "Rejects the call when no row matches the given `violation` text.",
      "",
      "On success, the caller's tenet-catalog cache is invalidated and a",
      "timestamped row is appended to docs/reference/TENETS-HISTORY.md.",
    ].join("\n"),
    inputSchema: {
      violation: z.string().describe("Exact violation text of the row to remove (use list_violations to find it)."),
    },
  },
  async ({ violation }) => {
    try {
      const readingGate = checkRequiredReadingGate();
      if (readingGate) return readingGate;

      const indexPath = tenetsIndexAbsPath();
      const protection = await isPathProtected(indexPath);
      if (protection.blocked) {
        return { content: [{ type: "text", text: `Error: ${protection.reason}` }], isError: true };
      }

      const result = await removeViolation({ violation });
      if (result.error) {
        return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
      }

      const lines = [
        `✓ Removed violation row`,
        `  Violation:   ${result.violation}`,
        `  Cited:       ${result.tenets.join(", ")}`,
        `  Removed fix: ${result.removedFix}`,
        `  Line:        ${result.lineRemoved} of ${result.path}`,
        `  History:     ${TENETS_HISTORY_REL_PATH} appended`,
      ];
      return { content: [{ type: "text", text: lines.join("\n") }] };
    } catch (err) {
      return { content: [{ type: "text", text: `remove_violation error: ${err.message}` }], isError: true };
    }
  },
);

// ═══════════════════════════════════════════════════════════════════════════
// TENET WRITE TOOLS — Sentinel-wrapped tenet blocks
// ═══════════════════════════════════════════════════════════════════════════
//
// Mutate the `## Tenet N: ...` blocks in TENETS.md and the category files.
// Each tool emits a timestamped row to docs/reference/TENETS-HISTORY.md on
// success. isPathProtected is consulted on every file the tool will touch —
// the `tenets` scope (which unlocks docs/reference/) is required.

function categoryFileAbsPath(categoryId) {
  const cat = CATEGORIES[categoryId];
  if (!cat) return null;
  const base = process.env.CLAUDE_PROJECT_DIR || process.cwd();
  return resolve(base, cat.file);
}

// ─── Tool: edit_tenet ─────────────────────────────────────────────────────

server.registerTool(
  "edit_tenet",
  {
    description: [
      "Replace a tenet's body content (post-heading prose, tables, code blocks)",
      "between the heading line and the close sentinel. The heading, both",
      "sentinels, and the tenet's id are ALL preserved.",
      "",
      "Body contract:",
      "  - Do NOT include the `## Tenet N: ...` heading line in `body` —",
      "    the tool rejects such calls (common mistake).",
      "  - Leading/trailing blank lines are trimmed automatically.",
      "  - A single blank line is always inserted between heading and body.",
      "",
      "To change the number, use `renumber_tenet`. Renaming (changing the name",
      "or severity suffix) is not supported by this tool — edit the heading",
      "manually within the tenets scope if needed.",
      "",
      "Emits a timestamped row to TENETS-HISTORY.md on success.",
    ].join("\n"),
    inputSchema: {
      id: z.string().describe("Tenet id to edit (e.g., 'T4'). Must exist."),
      body: z.string().describe("Replacement body content. Must NOT include the `## Tenet N: ...` heading."),
    },
  },
  async ({ id, body }) => {
    try {
      const readingGate = checkRequiredReadingGate();
      if (readingGate) return readingGate;

      // Resolve the target file via a catalog lookup — we don't know which
      // category file hosts the tenet until we've parsed. Resolve protection
      // by checking against TENETS.md first (covers the common case); the
      // helper's readFile will fail with a clear error if scope lacks the
      // category file too.
      const indexPath = resolve(process.env.CLAUDE_PROJECT_DIR || process.cwd(), TENETS_INDEX_REL_PATH);
      const protection = await isPathProtected(indexPath);
      if (protection.blocked) {
        return { content: [{ type: "text", text: `Error: ${protection.reason}` }], isError: true };
      }

      const result = await editTenet({ id, body });
      if (result.error) {
        return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
      }

      const lines = [
        `✓ Edited body of ${result.id}`,
        `  File:       ${result.path}`,
        `  Replaced:   lines ${result.linesReplaced.start}–${result.linesReplaced.end}`,
        `  Body size:  ${result.oldBodyLength} → ${result.newBodyLength} chars`,
        `  History:    ${TENETS_HISTORY_REL_PATH} appended`,
      ];
      return { content: [{ type: "text", text: lines.join("\n") }] };
    } catch (err) {
      return { content: [{ type: "text", text: `edit_tenet error: ${err.message}` }], isError: true };
    }
  },
);

// ─── Tool: add_tenet ──────────────────────────────────────────────────────

server.registerTool(
  "add_tenet",
  {
    description: [
      "Insert a new tenet sentinel block into the target category's file.",
      "",
      "Placement strategy (mechanical, tiered):",
      "  1. PREFER_SUCCESSOR — if any existing tenet in the target file has a",
      "     higher number, splice before its open sentinel with a trailing `---`.",
      "  2. FALLBACK_PREDECESSOR — otherwise splice after the highest-numbered",
      "     existing tenet's close sentinel with a leading `---`.",
      "  3. EMPTY_FILE — if the file has zero existing tenets, reject; the",
      "     category's structural skeleton must be authored by hand first.",
      "",
      "Rejects the call when:",
      "  - `category` is not an auto-maintained category (valid: foundation,",
      "    implementation-behavior, implementation-data, quality). The",
      "    structural categories meta/schema-rules/service-hierarchy host",
      "    T0/T1/T2 only; extending them requires hand-authoring in TENETS.md.",
      "  - `number` is not a non-negative integer",
      "  - A tenet with id T{number} already exists",
      "  - `name` or `body` is empty",
      "",
      "Derived sections in TENETS.md are maintained atomically:",
      "  - The T0 prose category list is regenerated with the new id sorted in",
      "  - The \"Tenet Categories\" navigation table row for this category is rebuilt",
      "  - A new summary-table row is inserted into the category's summary section",
      "    in sorted position (Core Rule = `summaryRule` if given, else extracted",
      "    from body via extractRule heuristics)",
      "",
      "Category is derived from the source file (no separate map to persist).",
      "Emits a timestamped row to TENETS-HISTORY.md on success.",
    ].join("\n"),
    inputSchema: {
      category: z.string().describe(
        "Category id. Valid for add_tenet: foundation, implementation-behavior, " +
        "implementation-data, quality. meta/schema-rules/service-hierarchy are " +
        "structural and must be hand-authored."
      ),
      number: z.number().int().nonnegative().describe("Tenet number (e.g., 33 → T33). Must not collide with existing id."),
      name: z.string().describe("Tenet name (goes after `## Tenet N: `). Non-empty."),
      severity: z.string().optional().describe("Severity suffix (e.g., 'ABSOLUTE'). Omit for no parenthetical."),
      body: z.string().describe("Body content (post-heading). Non-empty."),
      summaryRule: z.string().optional().describe(
        "Summary-table Core Rule text. If omitted, derived from body via extractRule. " +
        "Must be a single line without '|' characters (markdown table cell constraint)."
      ),
    },
  },
  async ({ category, number, name, severity, body, summaryRule }) => {
    try {
      const readingGate = checkRequiredReadingGate();
      if (readingGate) return readingGate;

      // Resolve the target category file so we can protection-check upfront.
      const categoryPath = categoryFileAbsPath(category);
      if (!categoryPath) {
        return {
          content: [{ type: "text", text: `Error: Unknown category "${category}". Valid: ${Object.keys(CATEGORIES).join(", ")}.` }],
          isError: true,
        };
      }

      const protection = await isPathProtected(categoryPath);
      if (protection.blocked) {
        return { content: [{ type: "text", text: `Error: ${protection.reason}` }], isError: true };
      }

      const result = await addTenet({ category, number, name, severity, body, summaryRule });
      if (result.error) {
        return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
      }

      const d = result.derived;
      const lines = [
        `✓ Added ${result.id}`,
        `  Name:        ${result.name}${result.severity ? ` (${result.severity})` : ""}`,
        `  Category:    ${result.category}`,
        `  File:        ${result.path}`,
        `  Strategy:    ${result.strategy}`,
        `  Inserted:    lines ${result.insertedAtLines.start}–${result.insertedAtLines.end}`,
        `  Summary:     "${result.summaryRule || "(empty)"}"`,
        `  Derived:     T0 lists=${d.t0ProseListsTouched}, nav rows=${d.navTableRowsTouched}, ` +
          `summary row=${d.summaryRowInserted ? `line ${d.summaryRowInsertedAt}` : "skipped"}`,
        `  History:     ${TENETS_HISTORY_REL_PATH} appended`,
      ];
      if (!d.summaryRowInserted && d.summaryRowSkipReason) {
        lines.push(`  ⚠ Summary skip reason: ${d.summaryRowSkipReason}`);
      }
      return { content: [{ type: "text", text: lines.join("\n") }] };
    } catch (err) {
      return { content: [{ type: "text", text: `add_tenet error: ${err.message}` }], isError: true };
    }
  },
);

// ─── Tool: remove_tenet ───────────────────────────────────────────────────

server.registerTool(
  "remove_tenet",
  {
    description: [
      "Remove a tenet's sentinel block from its category file. Consumes the",
      "trailing `\\n\\n---\\n\\n` separator when present so the resulting file",
      "does not end up with doubled separators.",
      "",
      "Derived sections in TENETS.md are maintained atomically:",
      "  - The T0 prose category list is regenerated without the removed id",
      "  - The \"Tenet Categories\" navigation table row for this category is rebuilt",
      "  - The summary-table row for this id is deleted",
      "",
      "Quick Reference citation handling:",
      "  - DEFAULT (cleanupCitations omitted or false): the tenet block is",
      "    removed but any Quick Reference rows citing the tenet are left",
      "    intact — validate_tenets will flag the dangling references so you",
      "    can clean them up with remove_violation or edit_violation.",
      "  - cleanupCitations:true: ALSO handles the cited violation rows in",
      "    one operation. The first invocation returns a forced-dry-run",
      "    listing of every row that would be deleted or rewritten so you",
      "    can verify the set matches intent before anything is modified.",
      "",
      "Does NOT touch prose cross-references (\"See T4 for details\") in",
      "other tenet bodies — those are validated separately.",
      "",
      "Category is derived from the source file (no separate map to persist).",
      "Emits a timestamped row to TENETS-HISTORY.md on success.",
    ].join("\n"),
    inputSchema: {
      id: z.string().describe("Tenet id to remove (e.g., 'T33'). Must exist."),
      cleanupCitations: z.boolean().optional().describe(
        "If true, ALSO remove Quick Reference violation rows that cite this tenet. " +
        "Rows citing only this tenet are deleted; rows citing this tenet alongside " +
        "others are rewritten with this tenet stripped from the citation list. First " +
        "call with this flag surfaces a preview of every affected row — review the " +
        "preview, then re-invoke to proceed."
      ),
      // `confirm` is intentionally undocumented at the tool-description level.
      // The first call with cleanupCitations:true returns a forced-dry-run
      // listing every row that would be touched; the response tells the agent
      // to re-invoke with confirm:true. This makes the preview unavoidable —
      // agents cannot blind-confirm without first seeing the citation list.
      confirm: z.boolean().optional(),
    },
  },
  async ({ id, cleanupCitations, confirm }) => {
    try {
      const readingGate = checkRequiredReadingGate();
      if (readingGate) return readingGate;

      // We don't know which file yet; check TENETS.md as the tenets-scope
      // proxy. If the tenet lives in a category file, the helper's readFile
      // will succeed under the same scope.
      const indexPath = resolve(process.env.CLAUDE_PROJECT_DIR || process.cwd(), TENETS_INDEX_REL_PATH);
      const protection = await isPathProtected(indexPath);
      if (protection.blocked) {
        return { content: [{ type: "text", text: `Error: ${protection.reason}` }], isError: true };
      }

      const result = await removeTenet({ id, cleanupCitations, confirm });
      if (result.error) {
        return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
      }

      const d = result.derived;
      const lines = [
        `✓ Removed ${result.id}`,
        `  Name:       ${result.name}`,
        `  File:       ${result.path}`,
        `  Removed:    lines ${result.removedLines.start}–${result.removedLines.end}`,
        `  Derived:    T0 lists=${d.t0ProseListsTouched}, nav rows=${d.navTableRowsTouched}, ` +
          `summary row=${d.summaryRowRemoved ? `line ${d.summaryRowRemovedAt} (removed)` : "not present"}`,
      ];
      const cc = result.citationCleanup;
      if (cc) {
        lines.push(
          `  Citations:  cleaned — deleted=${cc.rowsDeleted}, rewritten=${cc.rowsRewritten}`,
        );
      } else {
        lines.push(
          `  Reminder:   Quick Reference citations are NOT auto-removed (validate_tenets will flag them). ` +
          `Pass cleanupCitations:true to handle them in a single call.`,
        );
      }
      lines.push(`  History:    ${TENETS_HISTORY_REL_PATH} appended`);
      return { content: [{ type: "text", text: lines.join("\n") }] };
    } catch (err) {
      return { content: [{ type: "text", text: `remove_tenet error: ${err.message}` }], isError: true };
    }
  },
);

// ─── Tool: renumber_tenet ─────────────────────────────────────────────────

server.registerTool(
  "renumber_tenet",
  {
    description: [
      "Rename a tenet from oldId → newId. Updates all id-bearing lines atomically:",
      "  1. Open sentinel `<!-- TENET:T{old} -->` → `<!-- TENET:T{new} -->`",
      "  2. Close sentinel `<!-- /TENET:T{old} -->` → `<!-- /TENET:T{new} -->`",
      "  3. Heading `## Tenet {oldN}: Name (SEV)` → `## Tenet {newN}: Name (SEV)`",
      "  4. Summary-table row in TENETS.md (one `| **T{old}** |` → `| **T{new}** |`)",
      "  5. Quick Reference citation columns — scoped to the Quick Reference table",
      "     only. Word-boundary replace within the tenets column, byte-preserving",
      "     the rest of each row.",
      "  6. T0 prose category lists + \"Tenet Categories\" navigation row — rebuilt",
      "     from the post-rename logical tenet set, so ordering stays sorted.",
      "",
      "Rejects the call when:",
      "  - `oldId` is not a known tenet",
      "  - `newId` is already taken",
      "  - `oldId` and `newId` are identical",
      "",
      "Category is derived from the source file, which doesn't change on renumber.",
      "Does NOT touch prose cross-references in other tenet bodies (\"See T4 for",
      "details\") — Step 5's validate_tenets flags those.",
      "",
      "Emits a timestamped row to TENETS-HISTORY.md on success.",
    ].join("\n"),
    inputSchema: {
      oldId: z.string().describe("Current tenet id (e.g., 'T4'). Must exist."),
      newId: z.string().describe("Desired new id (e.g., 'T33'). Must not already exist."),
    },
  },
  async ({ oldId, newId }) => {
    try {
      const readingGate = checkRequiredReadingGate();
      if (readingGate) return readingGate;

      const indexPath = resolve(process.env.CLAUDE_PROJECT_DIR || process.cwd(), TENETS_INDEX_REL_PATH);
      const protection = await isPathProtected(indexPath);
      if (protection.blocked) {
        return { content: [{ type: "text", text: `Error: ${protection.reason}` }], isError: true };
      }

      const result = await renumberTenet({ oldId, newId });
      if (result.error) {
        return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
      }

      const c = result.changes;
      const lines = [
        `✓ Renumbered ${result.oldId} → ${result.newId}`,
        `  Category file:    ${result.categoryFile}`,
        `  Index file:       ${result.indexFile}`,
        `  Sentinels:        ${c.sentinelsTouched} updated`,
        `  Heading:          ${c.headingTouched ? "updated" : "NOT FOUND (check file)"}`,
        `  Summary row:      ${c.summaryTouched ? "updated" : "not present (no change)"}`,
        `  QR citations:     ${c.citationsTouched} row(s) updated`,
        `  T0 prose lists:   ${c.t0ProseListsTouched} list(s) regenerated`,
        `  Nav table rows:   ${c.navTableRowsTouched} row(s) regenerated`,
        `  History:          ${TENETS_HISTORY_REL_PATH} appended`,
      ];
      return { content: [{ type: "text", text: lines.join("\n") }] };
    } catch (err) {
      return { content: [{ type: "text", text: `renumber_tenet error: ${err.message}` }], isError: true };
    }
  },
);

// ─── Tool: validate_tenets ────────────────────────────────────────────────
//
// Full tenet-system consistency check. Runs every structural + derived-content
// check the write tools depend on. Also wired into the `tenets` scope's
// `stop_scope` exit validation — scope release is BLOCKED when errors > 0.

server.registerTool(
  "validate_tenets",
  {
    description: [
      "Validate the full tenet system for structural and derived-content consistency.",
      "",
      "Checks:",
      "  1. All parse errors from getTenetCatalog (sentinel imbalance, orphaned",
      "     sentinels, id mismatches, duplicate ids, missing T0/T1/T2, Quick",
      "     Reference citations of unknown tenets).",
      "  2. T0 body prose bullets list the correct sorted ids for each",
      "     sourceCodeCategoryName (FOUNDATION TENETS, IMPLEMENTATION TENETS, etc.).",
      "  3. \"Tenet Categories\" nav table has the correct sorted ids per",
      "     auto-maintained category (foundation, implementation-*, quality).",
      "  4. Per-category summary tables have the correct set of rows in",
      "     number order, matching the parsed tenets for that category.",
      "  5. No summary-table rows reference unknown tenet ids.",
      "",
      "Returns { errors, warnings, stats }. Any error blocks stop_scope release",
      "when the `tenets` scope is active — fix all reported errors, then retry",
      "stop_scope. Warnings are advisory and do not block.",
      "",
      "Also invoked automatically by stop_scope when releasing the tenets scope;",
      "call this tool directly to check consistency mid-session without ending",
      "the scope.",
    ].join("\n"),
    inputSchema: {},
  },
  async () => {
    try {
      const result = await validateTenets();
      const text = formatValidateTenetsResult(result);
      const isError = result.errors.length > 0;
      return { content: [{ type: "text", text }], isError };
    } catch (err) {
      return {
        content: [{ type: "text", text: `validate_tenets error: ${err.message}` }],
        isError: true,
      };
    }
  },
);

// ─── Tool: list_schemas ───────────────────────────────────────────────────

server.registerTool(
  "list_schemas",
  {
    description: [
      "List all OpenAPI schema files organized by service.",
      "",
      "Shows which schema types exist for each service (api, events, configuration,",
      "client-events, lifecycle-events) and lists shared/common schemas.",
    ].join("\n"),
    inputSchema: {},
  },
  async () => {
    const result = await getSchemaCatalog();
    if (result.error) {
      return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
    }

    const lines = [
      `Schema Catalog (${result.serviceCount} services, ${result.totalFiles} files)`,
      "",
    ];

    // Shared schemas first
    if (result.shared.length > 0) {
      lines.push("[Shared Schemas]");
      for (const s of result.shared) {
        lines.push(`  ${s.path}`);
      }
      lines.push("");
    }

    // Per-service
    lines.push("[Service Schemas]");
    const sortedServices = Object.entries(result.services).sort(([a], [b]) => a.localeCompare(b));
    for (const [service, schemas] of sortedServices) {
      const types = Object.keys(schemas).join(", ");
      lines.push(`  ${service.padEnd(24)} ${types}`);
    }

    return gatedToolResponse({
      text: lines.join("\n"),
      label: "Schema Catalog",
    });
  },
);

// ─── Tool: get_schema ─────────────────────────────────────────────────────

server.registerTool(
  "get_schema",
  {
    description: [
      "Get a specific schema file or all schemas for a service.",
      "",
      "Accepts a specific filename ('account-api.yaml'), a service name ('account'",
      "to get all schemas), or a relative path ('schemas/account-api.yaml').",
    ].join("\n"),
    inputSchema: {
      name: z.string().describe("Schema filename, service name, or path"),
    },
  },
  async ({ name }) => {
    const result = await getSchema(name);
    if (result.error) {
      return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
    }

    const sections = result.files.map((f) =>
      `${"━".repeat(76)}\n# ${f.path}\n${"━".repeat(76)}\n\n${f.content}`
    );

    return gatedToolResponse({
      text: sections.join("\n\n"),
      sourceFiles: result.files.map((f) => f.path),
      label: `Schema: ${name}`,
    });
  },
);

// ─── Tool: get_service_details ────────────────────────────────────────────

server.registerTool(
  "get_service_details",
  {
    description: [
      "Get generated service details, optionally filtered by layer.",
      "",
      "Without a layer filter, returns the combined service details.",
      "With a layer filter, returns details for that specific layer.",
      "Layers: infrastructure, app-foundation, game-foundation, app-features, game-features.",
    ].join("\n"),
    inputSchema: {
      layer: z.string().optional().describe("Filter by layer (e.g., 'game-features', 'app-foundation')"),
    },
  },
  async ({ layer }) => {
    const result = await getServiceDetails(layer);
    if (result.error) {
      return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
    }

    const header = result.layer ? `Service Details: ${result.layer}` : "Service Details (All Layers)";
    return gatedToolResponse({
      text: `# ${header}\n# Source: ${result.path}\n\n${result.content}`,
      sourceFiles: [result.path],
      label: header,
    });
  },
);

// ─── Tool: get_events ─────────────────────────────────────────────────────

server.registerTool(
  "get_events",
  {
    description: "Get the generated events reference — all event schemas and topics across all services.",
    inputSchema: {},
  },
  async () => {
    const result = await getEventCatalog();
    if (result.error) {
      return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
    }

    return gatedToolResponse({
      text: `# Source: ${result.path}\n\n${result.content}`,
      sourceFiles: [result.path],
      label: "Event Catalog",
    });
  },
);

// ─── Tool: get_state_stores ───────────────────────────────────────────────

server.registerTool(
  "get_state_stores",
  {
    description: "Get the generated state stores reference — all Redis/MySQL state store definitions across all services.",
    inputSchema: {},
  },
  async () => {
    const result = await getStateStoreCatalog();
    if (result.error) {
      return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
    }

    return gatedToolResponse({
      text: `# Source: ${result.path}\n\n${result.content}`,
      sourceFiles: [result.path],
      label: "State Store Catalog",
    });
  },
);

// ─── Tool: get_configuration ──────────────────────────────────────────────

server.registerTool(
  "get_configuration",
  {
    description: "Get the generated configuration reference — all environment variables and configuration properties across all services.",
    inputSchema: {},
  },
  async () => {
    const result = await getConfigurationCatalog();
    if (result.error) {
      return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
    }

    return gatedToolResponse({
      text: `# Source: ${result.path}\n\n${result.content}`,
      sourceFiles: [result.path],
      label: "Configuration Catalog",
    });
  },
);

// ─── Tool: print_models ───────────────────────────────────────────────────

server.registerTool(
  "print_models",
  {
    description: [
      "Print compact model shapes for a service plugin (~6x smaller than raw schemas).",
      "",
      "Equivalent to 'make print-models PLUGIN=name'. Shows all request/response/event",
      "models with types, nullability, defaults, and inheritance.",
      "Format: * = required, ? = nullable, = val = default.",
    ].join("\n"),
    inputSchema: {
      plugin: z.string().describe("Service plugin name (e.g., 'character', 'currency', 'divine')"),
    },
  },
  async ({ plugin }) => {
    const result = await printModelShapes(plugin);
    if (result.error) {
      return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
    }

    const header = `Model Shapes: ${result.plugin}${result.stats ? `\n${result.stats}` : ""}`;
    return gatedToolResponse({
      text: `${header}\n\n${result.output}`,
      label: `Model Shapes: ${result.plugin}`,
    });
  },
);

// ─── Tool: print_interfaces ───────────────────────────────────────────────

server.registerTool(
  "print_interfaces",
  {
    description: [
      "Print interface shapes from bannou-service shared infrastructure.",
      "",
      "Two modes:",
      "  - No name: catalog mode — all interfaces by category with one-line summaries",
      "  - With name: detail mode — full method signatures for a specific interface",
      "",
      "Partial name matching supported (e.g., 'Cacheable' matches ICacheableStateStore).",
      "Equivalent to 'make print-interfaces [INTERFACE=name]'.",
    ].join("\n"),
    inputSchema: {
      name: z.string().optional().describe("Interface name for detail mode (partial match supported). Omit for catalog."),
    },
  },
  async ({ name }) => {
    const result = await printInterfaceShapes(name);
    if (result.error) {
      return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
    }

    const modeLabel = result.mode === "catalog" ? "Interface Catalog" : `Interface Detail: ${result.name}`;
    const header = `${modeLabel}${result.stats ? `\n${result.stats}` : ""}`;
    return gatedToolResponse({
      text: `${header}\n\n${result.output}`,
      label: modeLabel,
    });
  },
);

// ─── Tool: generate ──────────────────────────────────────────────────────

server.registerTool(
  "generate",
  {
    description: [
      "Run Bannou code generation scripts.",
      "",
      "Two modes:",
      "  - No args: catalog mode — lists all generators with triggers, ordering rules, and timing",
      "  - With script: execute mode — runs a specific generator",
      "",
      "Per-service generators (models, config, events, client-events, service) require a service name.",
      "Global generators (state-stores, telemetry-metrics, published-topics, event-publishers, docs, all) do not.",
      "",
      "Always use the most granular generator possible. Use 'all' only when common-*.yaml changed.",
    ].join("\n"),
    inputSchema: {
      script: z.string().optional().describe("Generator to run (e.g., 'models', 'events', 'state-stores', 'all'). Omit for catalog."),
      service: z.string().optional().describe("Service name for per-service generators (e.g., 'analytics', 'character')"),
    },
  },
  async ({ script, service }) => {
    // Gate check — generation modifies files
    const readingGate = checkRequiredReadingGate();
    if (readingGate) return readingGate;

    // Catalog mode
    if (!script) {
      return { content: [{ type: "text", text: getGeneratorCatalog() }] };
    }

    // Execute mode
    const result = await runGenerator(script, service);

    if (result.error) {
      return { content: [{ type: "text", text: result.error }], isError: true };
    }

    return await handleLargeOutput(result.output, `generate: ${script}${service ? ` ${service}` : ""}`);
  },
);

// ═══════════════════════════════════════════════════════════════════════════
// RESEARCH TOOLS
// ═══════════════════════════════════════════════════════════════════════════

// ─── Tool: research ─────────────────────────────────────────────────────

server.registerTool(
  "research",
  {
    description: [
      "Research a topic using structured API integrations or headless browser.",
      "",
      "Two modes:",
      "  - URL mode: Fetch a web page via headless browser (bypasses bot detection)",
      "  - Domain mode: Query structured databases (AniList, VNDB, MusicBrainz, etc.)",
      "",
      "Domain mode performs all relevant API calls in parallel, compiles results",
      "into a research document in /tmp/, and gates reading (must read before continuing).",
      "",
      "Domains: anime, manga, character, visual-novel, light-novel,",
      "         music-artist, music-track, movie, tv, game",
      "",
      "Use this instead of WebFetch when a site returns 403 or incomplete content.",
      "For general web search (finding URLs), use WebSearch first, then research() for the content.",
    ].join("\n"),
    inputSchema: {
      url: z.string().optional()
        .describe("URL to fetch via headless browser (mutually exclusive with domain)"),
      domain: z.enum(["anime", "manga", "character", "visual-novel", "light-novel",
                       "music-artist", "music-track", "movie", "tv", "game"]).optional()
        .describe("Research domain for structured API queries (mutually exclusive with url)"),
      query: z.string().optional()
        .describe("What to research — title, character name, artist name (required with domain)"),
      timeout_ms: z.number().optional().default(15000)
        .describe("Navigation timeout for URL mode (default: 15000, max: 30000)"),
    },
  },
  async ({ url, domain, query, timeout_ms }) => {
    // ── Validate mutual exclusivity ──
    if (url && domain) {
      return { content: [{ type: "text", text: "Error: Provide url OR domain+query, not both. Use url for web page fetching, domain+query for structured API research." }], isError: true };
    }
    if (!url && !domain) {
      return { content: [{ type: "text", text: `Error: Provide either url (for web page fetching) or domain+query (for structured API research).\n\nAvailable domains: ${getValidDomains().join(", ")}\nImplemented: ${getImplementedDomains().join(", ")}` }], isError: true };
    }

    // ── URL mode (headless browser) ──
    if (url) {
      try {
        const timeout = Math.min(Math.max(timeout_ms || 15000, 5000), 30000);
        const result = await fetchPage(url, timeout);

        if (result.error) {
          return { content: [{ type: "text", text: `Research error: ${result.error}` }], isError: true };
        }

        const header = [
          `# ${result.title}`,
          `Source: ${result.url}`,
          `Content: ${result.charCount.toLocaleString()} characters${result.truncated ? " (truncated)" : ""}`,
          "",
        ].join("\n");

        const text = header + result.content;

        const CHUNK_LIMIT = 26000;
        if (text.length <= CHUNK_LIMIT) {
          return { content: [{ type: "text", text }] };
        }

        return { content: [{ type: "text", text: text.slice(0, CHUNK_LIMIT) + "\n\n[... response truncated to fit MCP limit]" }] };
      } catch (err) {
        return { content: [{ type: "text", text: `Research error: ${err.message}` }], isError: true };
      }
    }

    // ── Domain mode (structured API research) ──
    if (!query) {
      return { content: [{ type: "text", text: "Error: query is required when using domain mode. Provide the title, character name, or artist name to research." }], isError: true };
    }

    try {
      const result = await researchDomain(domain, query);

      if (result.error) {
        return { content: [{ type: "text", text: `Research error: ${result.error}` }], isError: true };
      }

      const response = [
        `📚 Research compiled: ${result.summary}`,
        "",
        `Document: ${result.path} (${(result.charCount / 1024).toFixed(1)} KB)`,
      ];

      if (result.alternatives?.length) {
        response.push("", "Other matches:");
        for (const alt of result.alternatives) {
          response.push(`  - ${alt.title} (${alt.format || "?"}, ${alt.year || "?"}, score: ${alt.score || "?"})`);
        }
      }

      response.push("", "⚠️ Read the research document before continuing — required reading gate is active.");

      return { content: [{ type: "text", text: response.join("\n") }] };
    } catch (err) {
      return { content: [{ type: "text", text: `Research error: ${err.message}` }], isError: true };
    }
  },
);

// ═══════════════════════════════════════════════════════════════════════════
// SEED GENERATION TOOLS (Phase 2 of MCP-SERVER.md — planned)
// ═══════════════════════════════════════════════════════════════════════════

// Tools to be added here:
//   generate_docs_seed

// ═══════════════════════════════════════════════════════════════════════════
// CONTEXT MANAGEMENT TOOLS
// ═══════════════════════════════════════════════════════════════════════════

// ─── Tool: prepare_context ────────────────────────────────────────────────

server.registerTool(
  "prepare_context",
  {
    description: [
      "Prepare agent context by packing documentation files into optimally-sized composites.",
      "",
      "Reads all files for a profile server-side, packs them into /tmp/ composites",
      "(each ≤64KB for single-response read_file), pre-registers originals as read",
      "(enabling immediate edit_file), and gates all other tools until composites are read.",
      "",
      "Profiles: 'dev' (project instructions + patterns), 'plugin' (dev + deep dive + map),",
      "          'schema' (dev + schema rules), 'tenets-full' (the 5 tenet category files —",
      "          stack on top of dev for deep audits), 'custom' (arbitrary file list).",
      "",
      "Tenet bodies are NOT in the default 'dev' profile. Use the list_tenets /",
      "get_tenet / list_violations / search_tenets MCP tools for targeted lookups,",
      "and reserve 'tenets-full' for tasks that need the entire bundle preloaded.",
      "",
      "Idempotent: skips files already read. Stackable: callable while gate is locked.",
      "Call once per profile, then read the returned composites to unlock all tools.",
    ].join("\n"),
    inputSchema: {
      profile: z.string().describe("Context profile: 'dev', 'plugin', 'schema', 'tenets-full', or 'custom'"),
      service: z.string().optional().describe("Service name for 'plugin' profile (e.g., 'account', 'auth')"),
      files: z.array(z.string()).optional().describe("File paths for 'custom' profile (relative to project root)"),
    },
  },
  async ({ profile, service, files }) => {
    try {
      const options = {};
      if (service) options.service = service;
      if (files) options.files = files;

      const result = await prepareContext(profile, options);

      if (result.error) {
        return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
      }

      if (result.alreadyLoaded) {
        return {
          content: [{
            type: "text",
            text: [
              `Context already loaded (profile: ${result.profile})`,
              "",
              `All ${result.totalFiles} files are already in context. No composites created.`,
              `${result.description}`,
            ].join("\n"),
          }],
        };
      }

      const compositeList = result.composites
        .map((c) => {
          const fileNames = c.files.map((f) => `    ${basename(f.path)} (${f.lines} lines, ${f.sizeBytes} bytes)`).join("\n");
          return `  ${c.path} (${c.sizeKB} KB)\n${fileNames}`;
        })
        .join("\n");

      const errorList = result.errors.length > 0
        ? `\nErrors (${result.errors.length}):\n${result.errors.map((e) => `  ⚠ ${e}`).join("\n")}`
        : "";

      const response = [
        `Context prepared (profile: ${result.profile})`,
        "",
        `Files packed: ${result.packed} of ${result.totalFiles}${result.skipped > 0 ? ` (${result.skipped} already in context)` : ""}`,
        `Composites created: ${result.composites.length}`,
        `${result.description}`,
        "",
        "Composites to read:",
        compositeList,
        errorList,
        "",
        `Pre-registered ${result.packed} original file(s) for editing.`,
        "",
        result.composites.length > 0
          ? `⚠️ Read all ${result.composites.length} composite(s) before using other tools — required reading gate is active.`
          : "No composites to read — all tools available.",
      ].join("\n");

      return { content: [{ type: "text", text: response }] };
    } catch (err) {
      return { content: [{ type: "text", text: `Error preparing context: ${err.message}` }], isError: true };
    }
  },
);

// Planned tools:
//   find_relevant_docs — weighted tokenized search, returns deterministic file set

// ═══════════════════════════════════════════════════════════════════════════
// SCOPE MANAGEMENT TOOLS
// ═══════════════════════════════════════════════════════════════════════════

// ─── Tool: list_scopes ────────────────────────────────────────────────────

server.registerTool(
  "list_scopes",
  {
    description: [
      "List all available scope identifiers for start_scope / add_scope.",
      "",
      "Scopes control which files are editable during a task. Everything is",
      "write-locked by default — a scope must be active before any edit_file,",
      "write_file, or move_lines call proceeds.",
      "",
      "Scope types:",
      "  plugin:name   — unlocks a specific plugins/lib-{name} + schemas + generated code",
      "  sdk:name      — unlocks a specific sdks/{name}",
      "  scripts, structural-tests, claude, test-utilities, tenets — directory scopes",
      "                  (each requires a sentinel grant before start_scope will succeed)",
      "",
      "Plugin and SDK scopes also accept an `also` parameter with supplementary",
      "unlocks (currently: 'infrastructure' for bannou-service/ access).",
      "",
      "Scopes can be stacked: after start_scope, call add_scope to layer a second",
      "scope on top (e.g., plugin:foo then scripts). Path checks pass if ANY scope",
      "in the stack permits the path. stop_scope pops the top scope only.",
    ].join("\n"),
    inputSchema: {},
  },
  async () => {
    const scopes = await listScopes();
    const stack = await getActiveScopeStack();

    const lines = ["Scope Catalog", ""];

    if (stack.length === 0) {
      lines.push("Active scope: (none) — writes are locked until a scope is started");
      lines.push("");
    } else if (stack.length === 1) {
      const active = stack[0];
      const alsoSuffix = active.also && active.also.length > 0
        ? ` (+also: ${active.also.join(", ")})`
        : "";
      lines.push(`Active scope: ${active.scope}${alsoSuffix}`);
      lines.push(`Started:      ${active.startedAt}`);
      lines.push("");
    } else {
      lines.push(`Active scope stack: ${stack.length} layer(s) — top of stack listed last`);
      for (let i = 0; i < stack.length; i++) {
        const s = stack[i];
        const alsoSuffix = s.also && s.also.length > 0 ? ` (+also: ${s.also.join(", ")})` : "";
        const marker = i === stack.length - 1 ? " ← top (popped by next stop_scope)" : "";
        lines.push(`  ${i + 1}. ${s.scope}${alsoSuffix}${marker}`);
        lines.push(`       started ${s.startedAt}`);
      }
      lines.push("");
    }

    lines.push(`Plugins (${scopes.plugins.length}):`);
    for (const p of scopes.plugins) lines.push(`  ${p.id}`);
    lines.push("");

    lines.push(`SDKs (${scopes.sdks.length}):`);
    for (const s of scopes.sdks) lines.push(`  ${s.id}`);
    lines.push("");

    lines.push("Directory scopes (sentinel grant required):");
    for (const d of scopes.directories) {
      lines.push(`  ${d.id.padEnd(18)} ${d.description}`);
      lines.push(`  ${"".padEnd(18)}   (requires grantPermissions: ["${d.frozenPrefix}"])`);
    }
    lines.push("");

    lines.push("Supplementary unlocks (via `also` parameter):");
    for (const a of scopes.alsoKeys) {
      lines.push(`  ${a.id.padEnd(18)} ${a.description}`);
    }

    return { content: [{ type: "text", text: lines.join("\n") }] };
  },
);

// ─── Tool: start_scope ────────────────────────────────────────────────────

server.registerTool(
  "start_scope",
  {
    description: [
      "Start a scope — unlocks writes to a specific plugin/SDK/directory and",
      "auto-loads its context via prepare_context (dev profile + deep dive +",
      "implementation map for plugins/SDKs).",
      "",
      "Fails if another scope is already active — call stop_scope first.",
      "Fails if the target doesn't exist or the scope identifier is malformed.",
      "Fails if the scope targets a frozen directory without a sentinel grant.",
      "",
      "After start_scope, all composites in the required reading gate must be",
      "read before any other tool call proceeds. The gate clears automatically",
      "as composites are read.",
    ].join("\n"),
    inputSchema: {
      scope: z.string().describe(
        "Scope identifier. Examples: 'plugin:genesis', 'sdk:music-theory', " +
        "'scripts', 'structural-tests', 'claude', 'test-utilities', 'tenets'."
      ),
      also: z.array(z.string()).optional().describe(
        "Optional supplementary unlocks for plugin/sdk scopes. Currently " +
        "supported: 'infrastructure' (unlocks bannou-service/ excluding Generated/). " +
        "Not allowed for directory scopes."
      ),
    },
  },
  async ({ scope, also }) => {
    const result = await startScope(scope, also, grantedPermissions);
    if (result.error) {
      return { content: [{ type: "text", text: result.error }], isError: true };
    }

    const { scope: scopeData, contextResult } = result;

    const lines = [
      `Scope '${scopeData.scope}' started.`,
      `Type:     ${scopeData.type}${scopeData.name ? ` (${scopeData.name})` : ""}`,
      `Also:     ${scopeData.also.length > 0 ? scopeData.also.join(", ") : "(none)"}`,
      `Started:  ${scopeData.startedAt}`,
      "",
    ];

    if (contextResult) {
      if (contextResult.alreadyLoaded) {
        lines.push(
          `Context: profile '${contextResult.profile}' already loaded (${contextResult.totalFiles} files in context).`,
          "",
          "All tools available — no required reading gate activated.",
        );
      } else if (contextResult.composites && contextResult.composites.length > 0) {
        lines.push(
          `Context: profile '${contextResult.profile}' loaded — ${contextResult.packed} file(s) packed into ${contextResult.composites.length} composite(s).`,
          "",
          "⚠️ Required reading gate active — read all composites before any other tool call:",
        );
        for (const c of contextResult.composites) {
          lines.push(`  ${c.path} (${c.sizeKB} KB)`);
        }
      } else if (contextResult.errors && contextResult.errors.length > 0) {
        lines.push(
          `Context: profile '${contextResult.profile}' — ${contextResult.errors.length} file(s) could not be loaded:`,
        );
        for (const e of contextResult.errors) lines.push(`  ⚠ ${e}`);
      } else {
        lines.push(`Context: profile '${contextResult.profile}' loaded.`);
      }
    }

    lines.push("");
    lines.push("Writes are now allowed within the scope's path set.");
    lines.push("Call stop_scope when done to trigger exit validation (build, diff scan) and release the scope.");

    return { content: [{ type: "text", text: lines.join("\n") }] };
  },
);

// ─── Tool: add_scope ──────────────────────────────────────────────────────

server.registerTool(
  "add_scope",
  {
    description: [
      "Layer a supplementary scope on top of the currently-active scope — without",
      "stopping it. Use this when a task needs access to BOTH a primary scope",
      "(e.g., plugin:foo) AND another area simultaneously (e.g., scripts to fix a",
      "generation bug while the plugin scope remains active).",
      "",
      "Fails if no scope is currently active — call start_scope first.",
      "Fails if the target doesn't exist or the scope identifier is malformed.",
      "Fails if the scope targets a frozen directory without a sentinel grant.",
      "Fails if the scope is already present in the stack (no duplicates).",
      "",
      "Path checks then pass when the path is permitted by ANY scope in the stack.",
      "The new scope's context is loaded via prepare_context (idempotent — already",
      "loaded files are skipped). The required-reading gate activates if new",
      "composites are produced.",
      "",
      "stop_scope pops ONLY the top scope — layers below it remain active. Call",
      "stop_scope repeatedly to unwind the stack from the top down. Each pop runs",
      "that layer's exit validation independently.",
    ].join("\n"),
    inputSchema: {
      scope: z.string().describe(
        "Scope identifier to layer on top. Examples: 'scripts', 'plugin:genesis', " +
        "'sdk:music-theory'. Cannot duplicate a scope already in the stack."
      ),
      also: z.array(z.string()).optional().describe(
        "Optional supplementary unlocks for plugin/sdk scopes. Currently " +
        "supported: 'infrastructure' (unlocks bannou-service/ excluding Generated/). " +
        "Not allowed for directory scopes."
      ),
    },
  },
  async ({ scope, also }) => {
    const result = await addScope(scope, also, grantedPermissions);
    if (result.error) {
      return { content: [{ type: "text", text: result.error }], isError: true };
    }

    const { scope: scopeData, contextResult, stackDepth, stack } = result;

    const lines = [
      `Scope '${scopeData.scope}' layered on top of existing stack.`,
      `Type:         ${scopeData.type}${scopeData.name ? ` (${scopeData.name})` : ""}`,
      `Also:         ${scopeData.also.length > 0 ? scopeData.also.join(", ") : "(none)"}`,
      `Started:      ${scopeData.startedAt}`,
      `Stack depth:  ${stackDepth}`,
      "",
      "Active stack (bottom → top):",
    ];
    for (let i = 0; i < stack.length; i++) {
      const s = stack[i];
      const alsoSuffix = s.also && s.also.length > 0 ? ` (+also: ${s.also.join(", ")})` : "";
      const marker = i === stack.length - 1 ? " ← top" : "";
      lines.push(`  ${i + 1}. ${s.scope}${alsoSuffix}${marker}`);
    }
    lines.push("");

    if (contextResult) {
      if (contextResult.alreadyLoaded) {
        lines.push(
          `Context: profile '${contextResult.profile}' already loaded (${contextResult.totalFiles} files in context).`,
          "",
          "All tools available — no required reading gate activated.",
        );
      } else if (contextResult.composites && contextResult.composites.length > 0) {
        lines.push(
          `Context: profile '${contextResult.profile}' loaded — ${contextResult.packed} file(s) packed into ${contextResult.composites.length} composite(s).`,
          "",
          "⚠️ Required reading gate active — read all composites before any other tool call:",
        );
        for (const c of contextResult.composites) {
          lines.push(`  ${c.path} (${c.sizeKB} KB)`);
        }
      } else if (contextResult.errors && contextResult.errors.length > 0) {
        lines.push(
          `Context: profile '${contextResult.profile}' — ${contextResult.errors.length} file(s) could not be loaded:`,
        );
        for (const e of contextResult.errors) lines.push(`  ⚠ ${e}`);
      } else {
        lines.push(`Context: profile '${contextResult.profile}' loaded.`);
      }
    }

    lines.push("");
    lines.push("Writes are now allowed within ANY layer's path set.");
    lines.push("Call stop_scope to pop this top layer (runs its exit validation); lower scopes remain active.");

    return { content: [{ type: "text", text: lines.join("\n") }] };
  },
);

// ─── Tool: stop_scope ─────────────────────────────────────────────────────

server.registerTool(
  "stop_scope",
  {
    description: [
      "Stop the top scope on the stack — runs exit validation and pops that layer.",
      "",
      "Exit validation runs per scope type:",
      "  plugin:X / sdk:X / structural-tests / test-utilities — dotnet build on the scope project",
      "  scripts — bash -n / python3 -m py_compile / node --check on modified files",
      "  claude  — bash/node syntax check on modified hooks/mjs, JSON check on settings.json",
      "  tenets  — diff summary only",
      "",
      "All scope types also run a null-forgiving-operator scan on added .cs lines",
      "(excluding Generated/).",
      "",
      "If any validation fails, the scope is NOT released — the agent must fix",
      "the issue and call stop_scope again. This prevents the 'release-then-defer'",
      "pattern where broken code leaves an orphaned scope.",
      "",
      "When the scope stack has multiple layers (from add_scope), only the top",
      "layer is popped. Call stop_scope repeatedly to unwind from the top down.",
    ].join("\n"),
    inputSchema: {
      force: z.boolean().optional(),
    },
  },
  async ({ force } = {}) => {
    const result = await stopScope({ force });
    if (result.error && !result.findings) {
      // No active scope — benign error
      return { content: [{ type: "text", text: result.error }], isError: true };
    }

    const { scope, findings, released, forced, remainingStackDepth, remainingTop } = result;
    const lines = [];

    if (released) {
      if (forced) {
        lines.push(`⚠️ Scope '${scope.scope}' forcibly released — exit validation failures were IGNORED.`);
      } else {
        lines.push(`Scope '${scope.scope}' released successfully.`);
      }
      if (typeof remainingStackDepth === "number" && remainingStackDepth > 0 && remainingTop) {
        const alsoSuffix = remainingTop.also && remainingTop.also.length > 0
          ? ` (+also: ${remainingTop.also.join(", ")})`
          : "";
        lines.push(`Stack now has ${remainingStackDepth} layer(s) remaining. Top: '${remainingTop.scope}'${alsoSuffix}.`);
      } else {
        lines.push("Scope stack is now empty.");
      }
    } else {
      lines.push(`⚠️ Scope '${scope.scope}' still active — exit validation failed.`);
      lines.push(`Fix the issues listed below and call stop_scope again.`);
    }
    lines.push(`Started: ${scope.startedAt}`);
    lines.push("");
    lines.push("Exit validation findings:");
    lines.push("");

    for (const f of findings) {
      const icon = f.kind === "pass" ? "✓" : f.kind === "fail" ? "✗" : "·";
      lines.push(`  ${icon} [${f.label}] ${f.message}`);
      if (f.files && f.files.length > 0 && f.files.length <= 20) {
        for (const file of f.files) lines.push(`      ${file}`);
      } else if (f.files && f.files.length > 20) {
        for (const file of f.files.slice(0, 20)) lines.push(`      ${file}`);
        lines.push(`      ... and ${f.files.length - 20} more`);
      }
      if (f.hits && f.hits.length > 0) {
        for (const hit of f.hits.slice(0, 10)) lines.push(`      → ${hit}`);
        if (f.hits.length > 10) lines.push(`      ... and ${f.hits.length - 10} more`);
      }
      if (f.output && f.kind === "fail") {
        const truncated = f.output.length > 800 ? f.output.slice(-800) : f.output;
        lines.push("      ---");
        for (const outLine of truncated.split("\n")) lines.push(`      ${outLine}`);
        lines.push("      ---");
      }
    }

    return {
      content: [{ type: "text", text: lines.join("\n") }],
      isError: !released,
    };
  },
);

// ═══════════════════════════════════════════════════════════════════════════
// WORKER DISPATCH TOOL
// ═══════════════════════════════════════════════════════════════════════════

// ─── Tool: dispatch_worker ───────────────────────────────────────────────

server.registerTool(
  "dispatch_worker",
  {
    description: [
      "Spawn a constrained worker agent to execute a focused implementation task.",
      "",
      "The worker runs as an independent Claude Code session with access to all",
      "MCP tools (read_file, edit_file, start_scope, etc.) on a separate state",
      "bucket. It loads project context via prepare_context, works within the",
      "declared scopes, and returns results including mandatory stop_scope output.",
      "",
      "Hard constraints enforced on the worker:",
      "  - Maximum 3 scopes per task (declared upfront)",
      "  - Cannot modify frozen files (scripts/, docs/reference/, structural-tests/)",
      "  - Cannot spawn sub-agents (no nesting)",
      "  - Cannot request user input — stops and returns the question",
      "  - Must call stop_scope as final action and include result in output",
      "",
      "Use this for mechanical implementation tasks that don't require user judgment.",
      "The parent agent reviews the worker's output (especially stop_scope results)",
      "and handles any failures or design questions the worker surfaces.",
    ].join("\n"),
    inputSchema: {
      task: z.string().describe(
        "Complete task description. Include: what to do, which files/plugins, " +
        "what patterns to follow, acceptance criteria. The worker has project " +
        "context but needs specific instructions."
      ),
      scopes: z.array(z.string()).min(1).max(MAX_WORKER_SCOPES).describe(
        `Scope identifiers the worker is authorized to use (1-${MAX_WORKER_SCOPES}). ` +
        "Examples: 'plugin:state', 'plugin:behavior', 'sdk:music-theory'. " +
        "The worker manages its own start_scope/stop_scope cycles."
      ),
      timeout_ms: z.number().optional().default(600000).describe(
        "Timeout in milliseconds (default: 600000 = 10 minutes, max: 1200000 = 20 minutes)"
      ),
      audit: z.boolean().optional().default(false).describe(
        "If true, activates an audit gate after the worker completes. " +
        "While the gate is active, dispatch_worker is blocked until " +
        "clear_audit_gate is called with the required attestation. " +
        "Use this to enforce sequential worker execution with mandatory " +
        "parent review between each worker."
      ),
    },
  },
  async ({ task, scopes, timeout_ms, audit }) => {
    try {
      // Gate: don't dispatch while required reading is pending
      const readingGate = checkRequiredReadingGate();
      if (readingGate) return readingGate;

      // Gate: don't dispatch while audit gate is pending
      if (auditGatePending) {
        return {
          content: [{
            type: "text",
            text: [
              "Error: Audit gate is active — a previous worker's output has not been audited yet.",
              "",
              `Previous worker scopes: ${auditGatePending.scopes.join(", ")}`,
              `Completed at: ${auditGatePending.completedAt}`,
              "",
              "You MUST audit the previous worker's output, make corrections, then call:",
              '  clear_audit_gate(attestation: "I have fully audited this agent\'s work and corrected it to bring in line with developer TENETS and schema-rules")',
              "",
              "Only after clearing the gate can you dispatch another worker.",
            ].join("\n"),
          }],
          isError: true,
        };
      }

      // Validate scope count
      if (scopes.length > MAX_WORKER_SCOPES) {
        return {
          content: [{
            type: "text",
            text: `Error: Maximum ${MAX_WORKER_SCOPES} scopes per worker. Got ${scopes.length}: ${scopes.join(", ")}`,
          }],
          isError: true,
        };
      }

      // Build the prompt
      const prompt = buildWorkerPrompt(task, scopes);

      // Resolve timeout (cap at 20 minutes)
      const timeout = Math.min(Math.max(timeout_ms || 600000, 60000), 1200000);

      // Resolve working directory
      const cwd = process.env.CLAUDE_PROJECT_DIR || process.cwd();

      // Spawn the worker
      const startTime = Date.now();
      const result = await spawnWorker(prompt, { cwd, timeoutMs: timeout });
      const elapsed = ((Date.now() - startTime) / 1000).toFixed(1);

      // Save log for review
      const taskId = scopes[0].replace(/[^a-z0-9-]/g, "-");
      const logPath = await saveWorkerLog(taskId, result.stdout, result.stderr);

      // Activate audit gate if requested
      if (audit) {
        setAuditGate({
          task: task.slice(0, 200),
          scopes,
          completedAt: new Date().toISOString(),
          logPath,
        });
      }

      // Format response
      const lines = [
        `## Worker Result`,
        "",
        `**Scopes**: ${scopes.join(", ")}`,
        `**Bucket**: ${result.bucket}`,
        `**Duration**: ${elapsed}s`,
        `**Exit code**: ${result.exitCode ?? "null (killed)"}`,
        `**Log**: ${logPath}`,
        audit ? `**Audit gate**: ACTIVATED — review output before dispatching next worker` : "",
        "",
      ].filter(Boolean);

      if (result.stderr && !result.stderr.includes("Worker timed out")) {
        lines.push(`### Stderr`, "```", result.stderr.slice(0, 500), "```", "");
      }

      if (result.stderr && result.stderr.includes("Worker timed out")) {
        lines.push(`⚠️ **Worker timed out after ${timeout / 1000}s.** Partial output below.`, "");
      }

      lines.push(`### Worker Output`, "", result.stdout || "(no output)");

      return { content: [{ type: "text", text: lines.join("\n") }] };
    } catch (err) {
      return {
        content: [{
          type: "text",
          text: [
            `Error dispatching worker: ${err.message}`,
            "",
            "Common causes:",
            "  - `claude` CLI not found in PATH",
            "  - MCP server startup failure in worker session",
            "  - Worker exceeded output buffer (10MB)",
          ].join("\n"),
        }],
        isError: true,
      };
    }
  },
);

// ─── Tool: clear_audit_gate ──────────────────────────────────────────────

server.registerTool(
  "clear_audit_gate",
  {
    description: [
      "Clear the audit gate activated by a previous dispatch_worker(audit: true) call.",
      "",
      "The audit gate blocks dispatch_worker from spawning new workers until",
      "the parent agent has reviewed the previous worker's output, made any",
      "necessary corrections, and attested to having done so.",
      "",
      "Requires an exact attestation string to clear. This makes it behaviorally",
      "impossible to skip auditing without explicitly misrepresenting the work.",
    ].join("\n"),
    inputSchema: {
      attestation: z.string().describe(
        'Exact attestation: "I have fully audited this agent\'s work and corrected it to bring in line with developer TENETS and schema-rules"'
      ),
    },
  },
  async ({ attestation }) => {
    if (!auditGatePending) {
      return { content: [{ type: "text", text: "No audit gate is currently active. Nothing to clear." }] };
    }

    if (attestation !== AUDIT_ATTESTATION) {
      return {
        content: [{
          type: "text",
          text: [
            "Error: Attestation does not match the required text.",
            "",
            "You must type EXACTLY:",
            `  "${AUDIT_ATTESTATION}"`,
            "",
            "This attestation confirms you have:",
            "  1. Read the worker's complete output",
            "  2. Verified the implementation against the specification",
            "  3. Made corrections to bring it in line with project tenets",
            "",
            "If you have not done these things, do them now before clearing the gate.",
          ].join("\n"),
        }],
        isError: true,
      };
    }

    const prev = clearAuditGateState();
    return {
      content: [{
        type: "text",
        text: [
          "✓ Audit gate cleared.",
          "",
          `Previous worker: ${prev.scopes.join(", ")} (completed ${prev.completedAt})`,
          "",
          "dispatch_worker is now available for the next task.",
        ].join("\n"),
      }],
    };
  },
);

// ═══════════════════════════════════════════════════════════════════════════
// COVERAGE CHECK TOOL
// ═══════════════════════════════════════════════════════════════════════════

// ─── Tool: coverage_check ───────────────────────────────────────────────

server.registerTool(
  "coverage_check",
  {
    description: [
      "Check implementation coverage of an implementation map against source code.",
      "",
      "Parses an implementation map (plugin or SDK) to extract expected types and",
      "methods, then scans the source directory for matches. Returns a structured",
      "report showing found vs. missing items with coverage percentage.",
      "",
      "Works for both:",
      "  - Plugins: docs/maps/{SERVICE}.md → plugins/lib-{service}/",
      "  - SDKs:    docs/sdks/maps/{SDK}.md → sdks/{sdk}/",
      "",
      "Auto-detects plugin vs SDK if kind is omitted.",
    ].join("\n"),
    inputSchema: {
      name: z.string().describe(
        "Component name (e.g., 'account', 'sprite-theory', 'music-theory')"
      ),
      kind: z.enum(["plugin", "sdk"]).optional().describe(
        "Component kind: 'plugin' or 'sdk'. Auto-detected if omitted."
      ),
    },
  },
  async ({ name, kind }) => {
    const result = await checkCoverage(name, kind);

    if (result.error) {
      return { content: [{ type: "text", text: `Error: ${result.error}` }], isError: true };
    }

    const text = formatCoverageReport(result.report);
    return { content: [{ type: "text", text }] };
  },
);

// ─── Dashboard Emitter & Connect ──────────────────────────────────────────

try { await initDashboardEmitter(); } catch { /* never block startup */ }

const transport = new StdioServerTransport();
await server.connect(transport);
