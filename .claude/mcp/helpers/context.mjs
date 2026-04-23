/**
 * Context preparation helpers — profile resolution, file packing, composite creation.
 *
 * Replaces behavioral "read these N files" instructions with a mechanical tool.
 * prepare_context reads files server-side, packs them into optimally-sized
 * composites in /tmp/, pre-registers originals in readFiles (enabling immediate
 * edit_file), and adds composites to requiredReading (gating all other tools
 * until the agent reads them).
 *
 * Key behaviors:
 *   - Skips files already in readFiles (idempotent — second call is a no-op)
 *   - Callable while requiredReading gate is locked (stacks composites)
 *   - Composites contain line-numbered content from originals (same format as read_file)
 *   - File sizes (bytes) included in headers for write_file expected_size_bytes
 *   - Each composite stays under COMPOSITE_BYTE_LIMIT for single-response read_file
 */

import { readFile, writeFile, stat, readdir, unlink } from "node:fs/promises";
import { resolve, basename } from "node:path";
import { tmpdir } from "node:os";
import { join } from "node:path";

import {
  readFiles,
  requiredReading,
  COMPOSITE_PREFIX,
  COMPOSITE_BYTE_LIMIT,
} from "../state.mjs";

import { CONTEXT_PROFILES } from "../profiles.mjs";

// ─── Profile Resolution ─────────────────────────────────────────────────

/**
 * Resolves a profile name + options into a list of absolute file paths.
 * Handles inheritance (extends) and dynamic files (from options).
 * Returns { files: string[] } or { error: string }.
 */
export function resolveProfile(profile, options = {}) {
  if (profile === "custom") {
    if (!options.files || !Array.isArray(options.files) || options.files.length === 0) {
      return { error: `Custom profile requires options.files (non-empty array of paths).` };
    }
    const projectDir = process.env.CLAUDE_PROJECT_DIR || process.cwd();
    return {
      files: options.files.map((f) => resolve(projectDir, f)),
      description: `Custom context: ${options.files.length} files`,
    };
  }

  const profileDef = CONTEXT_PROFILES[profile];
  if (!profileDef) {
    const available = Object.keys(CONTEXT_PROFILES).join(", ");
    return { error: `Unknown profile: "${profile}". Available: ${available}, custom` };
  }

  // Check required options
  if (profileDef.requiresOption && !options[profileDef.requiresOption]) {
    return { error: `Profile "${profile}" requires option "${profileDef.requiresOption}".` };
  }

  const projectDir = process.env.CLAUDE_PROJECT_DIR || process.cwd();
  const files = [];

  // Resolve base profile (extends)
  if (profileDef.extends) {
    const baseResult = resolveProfile(profileDef.extends, options);
    if (baseResult.error) return baseResult;
    files.push(...baseResult.files);
  }

  // Static files
  if (profileDef.files) {
    for (const f of profileDef.files) {
      files.push(resolve(projectDir, f));
    }
  }

  // Dynamic files
  if (profileDef.dynamicFiles) {
    const dynamicPaths = profileDef.dynamicFiles(options);
    for (const f of dynamicPaths) {
      files.push(resolve(projectDir, f));
    }
  }

  return { files, description: profileDef.description };
}

// ─── Composite Cleanup ──────────────────────────────────────────────────

/**
 * Remove previous composites for a profile. Cleans both disk files and
 * requiredReading entries for stale composites.
 */
async function cleanPreviousComposites(profile) {
  const dir = tmpdir();
  const prefix = `${COMPOSITE_PREFIX}${profile}-`;
  try {
    const entries = await readdir(dir);
    for (const entry of entries) {
      if (entry.startsWith(prefix) && entry.endsWith(".txt")) {
        const fullPath = join(dir, entry);
        requiredReading.delete(fullPath);
        try {
          await unlink(fullPath);
        } catch { /* file may already be gone */ }
      }
    }
  } catch { /* tmpdir listing failed — not fatal */ }
}

// ─── File Reading & Numbering ──────────────────────────────────────────

/**
 * Read a file and return its content with line numbers (same format as read_file).
 * Returns null if the file doesn't exist or can't be read.
 */
async function readAndNumber(filePath) {
  try {
    const content = await readFile(filePath, "utf-8");
    const fileStats = await stat(filePath);
    const lines = content.split("\n");
    const maxWidth = String(lines.length).length;
    const numbered = lines
      .map((line, i) => {
        const num = String(i + 1).padStart(Math.max(maxWidth, 6));
        return `${num}\t${line}`;
      })
      .join("\n");

    return {
      path: filePath,
      numbered,
      sizeBytes: fileStats.size,
      lineCount: lines.length,
    };
  } catch (err) {
    return null;
  }
}

// ─── Packing ────────────────────────────────────────────────────────────

/**
 * Build the delimiter that precedes each file's content in a composite.
 */
function buildFileDelimiter(fileInfo) {
  return [
    "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━",
    `FILE: ${fileInfo.path}  (${fileInfo.lineCount} lines, ${fileInfo.sizeBytes} bytes)`,
    "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━",
  ].join("\n");
}

/**
 * Build the header block for a composite file.
 */
function buildCompositeHeader(compositeIndex, totalComposites, profile, entries) {
  const fileList = entries
    .map((e, i) => `║   ${i + 1}. ${e.path} (${e.lineCount} lines, ${e.sizeBytes} bytes)`)
    .join("\n");

  return [
    "╔══════════════════════════════════════════════════════════════════════════════",
    `║ CONTEXT COMPOSITE ${compositeIndex} of ${totalComposites}  —  Profile: ${profile}`,
    `║ Generated: ${new Date().toISOString()}`,
    "║",
    `║ Contains ${entries.length} file(s):`,
    fileList,
    "║",
    "║ Original files pre-registered as read — edit_file works immediately.",
    "║ File sizes (bytes) shown above for write_file expected_size_bytes.",
    "╚══════════════════════════════════════════════════════════════════════════════",
  ].join("\n");
}

/**
 * Pack file data into composites, each under COMPOSITE_BYTE_LIMIT.
 *
 * Strategy:
 *   - Files are kept in profile order (not sorted by size) to preserve
 *     logical grouping when the agent reads them.
 *   - If a single file's content (with delimiter) exceeds the limit,
 *     it is split across multiple composites at line boundaries.
 *   - Small files are greedily packed into the current composite.
 */
function packFiles(fileDataArray) {
  const composites = []; // Array of { entries: [{path, sizeBytes, lineCount, content}] }
  let current = { entries: [], size: 0 };

  for (const file of fileDataArray) {
    const delimiter = buildFileDelimiter(file);
    const delimiterSize = Buffer.byteLength(delimiter, "utf-8");
    const contentSize = Buffer.byteLength(file.numbered, "utf-8");
    const entrySize = delimiterSize + 1 + contentSize + 2; // delimiter + \n + content + \n\n

    // Estimate header size (~600 bytes, grows with file count)
    const headerEstimate = 700;

    if (entrySize + headerEstimate <= COMPOSITE_BYTE_LIMIT) {
      // File fits in a single entry — check if it fits in the current composite
      if (current.size + entrySize > COMPOSITE_BYTE_LIMIT - headerEstimate && current.entries.length > 0) {
        // Close current composite and start a new one
        composites.push(current);
        current = { entries: [], size: 0 };
      }

      current.entries.push({
        path: file.path,
        sizeBytes: file.sizeBytes,
        lineCount: file.lineCount,
        content: delimiter + "\n" + file.numbered,
      });
      current.size += entrySize;
    } else {
      // File is too large for a single composite — split at line boundaries
      if (current.entries.length > 0) {
        composites.push(current);
        current = { entries: [], size: 0 };
      }

      const numberedLines = file.numbered.split("\n");
      const contentLimit = COMPOSITE_BYTE_LIMIT - headerEstimate - delimiterSize - 100;
      let lineStart = 0;
      let partIndex = 1;
      const totalParts = Math.ceil(contentSize / contentLimit) || 1;

      while (lineStart < numberedLines.length) {
        let chunkSize = 0;
        let lineEnd = lineStart;

        while (lineEnd < numberedLines.length) {
          const lineSize = Buffer.byteLength(numberedLines[lineEnd], "utf-8") + 1;
          if (chunkSize + lineSize > contentLimit && lineEnd > lineStart) break;
          chunkSize += lineSize;
          lineEnd++;
        }

        const chunkContent = numberedLines.slice(lineStart, lineEnd).join("\n");
        const partLabel = totalParts > 1 ? ` — PART ${partIndex}` : "";
        const partDelimiter = [
          "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━",
          `FILE: ${file.path}  (${file.lineCount} lines, ${file.sizeBytes} bytes)${partLabel}`,
          "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━",
        ].join("\n");

        composites.push({
          entries: [
            {
              path: file.path,
              sizeBytes: file.sizeBytes,
              lineCount: file.lineCount,
              content: partDelimiter + "\n" + chunkContent,
            },
          ],
          size: Buffer.byteLength(chunkContent, "utf-8"),
        });

        lineStart = lineEnd;
        partIndex++;
      }
    }
  }

  // Close the final composite if it has content
  if (current.entries.length > 0) {
    composites.push(current);
  }

  return composites;
}

// ─── Main Entry Point ───────────────────────────────────────────────────

/**
 * Prepare context for an agent session.
 *
 * 1. Resolves profile to file list
 * 2. Skips files already in readFiles
 * 3. Reads and numbers remaining files
 * 4. Packs into composites under COMPOSITE_BYTE_LIMIT
 * 5. Pre-registers originals in readFiles
 * 6. Adds composites to requiredReading
 * 7. Returns manifest for the agent
 */
export async function prepareContext(profile, options = {}) {
  // Resolve profile to file list
  const resolved = resolveProfile(profile, options);
  if (resolved.error) return { error: resolved.error };

  const { files, description } = resolved;

  // Clean up previous composites for this profile
  await cleanPreviousComposites(profile);

  // Partition: skip already-read, read the rest
  const skipped = [];
  const toRead = [];

  for (const filePath of files) {
    if (readFiles.has(filePath)) {
      skipped.push(filePath);
    } else {
      toRead.push(filePath);
    }
  }

  // If everything is already read, no composites needed
  if (toRead.length === 0) {
    return {
      profile,
      description,
      alreadyLoaded: true,
      totalFiles: files.length,
      skipped: skipped.length,
      composites: [],
      errors: [],
    };
  }

  // Read and number all files
  const fileData = [];
  const errors = [];

  for (const filePath of toRead) {
    const result = await readAndNumber(filePath);
    if (result) {
      fileData.push(result);
    } else {
      errors.push(filePath);
    }
  }

  if (fileData.length === 0) {
    return {
      profile,
      description,
      alreadyLoaded: false,
      totalFiles: files.length,
      skipped: skipped.length,
      composites: [],
      errors,
    };
  }

  // Pack into composites
  const packedComposites = packFiles(fileData);
  const totalComposites = packedComposites.length;

  // Write composites to disk and register in requiredReading
  const compositeManifest = [];

  for (let i = 0; i < packedComposites.length; i++) {
    const composite = packedComposites[i];
    const compositePath = join(
      tmpdir(),
      `${COMPOSITE_PREFIX}${profile}-${i + 1}.txt`,
    );

    const header = buildCompositeHeader(
      i + 1,
      totalComposites,
      profile,
      composite.entries,
    );
    const body = composite.entries.map((e) => e.content).join("\n\n");
    const fullContent = header + "\n\n" + body + "\n";

    await writeFile(compositePath, fullContent, "utf-8");

    const compositeSize = Buffer.byteLength(fullContent, "utf-8");
    compositeManifest.push({
      path: compositePath,
      sizeKB: (compositeSize / 1024).toFixed(1),
      files: composite.entries.map((e) => ({
        path: e.path,
        sizeBytes: e.sizeBytes,
        lines: e.lineCount,
      })),
    });

    // Add to required reading gate
    requiredReading.add(compositePath);
  }

  // Pre-register originals as read (enables immediate edit_file)
  for (const file of fileData) {
    readFiles.add(file.path);
  }

  return {
    profile,
    description,
    alreadyLoaded: false,
    totalFiles: files.length,
    skipped: skipped.length,
    packed: fileData.length,
    composites: compositeManifest,
    errors,
  };
}
