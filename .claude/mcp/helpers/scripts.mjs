/**
 * Script writing and validation helpers.
 *
 * Provides write_script functionality: writes scripts to a sandboxed
 * directory (/tmp/bannou-scripts/), auto-chmod +x, validates syntax
 * before returning. Agents cannot chmod or create scripts elsewhere.
 */

import { writeFile, mkdir, chmod } from "node:fs/promises";
import { join, extname } from "node:path";
import { exec } from "node:child_process";
import { promisify } from "node:util";

import { SCRIPTS_DIR, SCRIPT_EXTENSIONS } from "../state.mjs";

const execAsync = promisify(exec);

/**
 * Validates a script extension is allowed.
 * Returns null if valid, or an error message if not.
 */
export function validateScriptExtension(filename) {
  const ext = extname(filename).toLowerCase();
  if (!SCRIPT_EXTENSIONS.has(ext)) {
    return `Extension "${ext}" is not allowed. Allowed: ${[...SCRIPT_EXTENSIONS].join(", ")}`;
  }
  return null;
}

/**
 * Validates script filename — no path traversal, no hidden files.
 */
export function validateScriptFilename(filename) {
  if (filename.includes("/") || filename.includes("\\")) {
    return "Filename must not contain path separators. Scripts are written to a flat directory.";
  }
  if (filename.startsWith(".")) {
    return "Hidden files (starting with .) are not allowed.";
  }
  if (filename.includes("..")) {
    return "Path traversal (..) is not allowed.";
  }
  if (!/^[a-zA-Z0-9_.-]+$/.test(filename)) {
    return "Filename may only contain alphanumeric characters, underscores, hyphens, and dots.";
  }
  return null;
}

/**
 * Validates script syntax without executing it.
 * Returns { valid: boolean, output: string }.
 */
export async function validateScriptSyntax(filePath, ext) {
  let cmd;
  switch (ext) {
    case ".sh":
      cmd = `bash -n "${filePath}"`;
      break;
    case ".py":
      cmd = `python3 -m py_compile "${filePath}"`;
      break;
    case ".mjs":
      cmd = `node --check "${filePath}"`;
      break;
    default:
      return { valid: true, output: "No syntax validator available for this extension." };
  }

  try {
    const { stdout, stderr } = await execAsync(cmd, { timeout: 10000 });
    const output = (stdout || "") + (stderr || "");
    return { valid: true, output: output.trim() || "Syntax OK" };
  } catch (err) {
    const output = (err.stderr || err.stdout || err.message || "").trim();
    return { valid: false, output: output || "Syntax validation failed" };
  }
}

/**
 * Writes a script to the sandboxed scripts directory.
 * Returns { path, validation } on success, or throws on filesystem errors.
 *
 * Steps:
 * 1. Validate filename and extension
 * 2. Ensure scripts directory exists
 * 3. Write the file
 * 4. chmod +x
 * 5. Validate syntax
 * 6. Return path + validation results
 */
export async function writeScript(filename, content) {
  // Validate filename
  const filenameError = validateScriptFilename(filename);
  if (filenameError) {
    return { error: filenameError };
  }

  // Validate extension
  const extError = validateScriptExtension(filename);
  if (extError) {
    return { error: extError };
  }

  const ext = extname(filename).toLowerCase();
  const filePath = join(SCRIPTS_DIR, filename);

  // Ensure directory exists
  await mkdir(SCRIPTS_DIR, { recursive: true });

  // Auto-add shebang if missing for shell scripts
  let finalContent = content;
  if (ext === ".sh" && !content.startsWith("#!")) {
    finalContent = "#!/usr/bin/env bash\n" + content;
  } else if (ext === ".py" && !content.startsWith("#!")) {
    finalContent = "#!/usr/bin/env python3\n" + content;
  } else if (ext === ".mjs" && !content.startsWith("#!")) {
    finalContent = "#!/usr/bin/env node\n" + content;
  }

  // Write
  await writeFile(filePath, finalContent, "utf-8");

  // Make executable
  await chmod(filePath, 0o755);

  // Validate syntax
  const validation = await validateScriptSyntax(filePath, ext);

  return {
    path: filePath,
    content: finalContent,
    validation,
  };
}
