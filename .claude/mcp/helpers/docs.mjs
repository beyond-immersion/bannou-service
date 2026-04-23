/**
 * Documentation catalog, retrieval, and search helpers.
 *
 * - getDocumentCatalog(): Reads all 6 GENERATED-*-CATALOG.md files, returns structured index.
 * - getDocument(path): Reads a specific document from docs/.
 * - searchDocs(query): Full-text keyword search with lazy-loaded inverted index.
 *
 * Search index is built lazily on first call and cached. Cache is invalidated
 * when the docs/ directory mtime changes (detects file additions/modifications).
 */

import { readFile, readdir, stat } from "node:fs/promises";
import { resolve, join, relative, extname } from "node:path";

// ─── Helpers ──────────────────────────────────────────────────────────────

function getProjectDir() {
  return process.env.CLAUDE_PROJECT_DIR || process.cwd();
}

// ─── Document Catalog ─────────────────────────────────────────────────────

const CATALOG_FILES = [
  { key: "guides", file: "GENERATED-GUIDES-CATALOG.md", label: "Guides" },
  { key: "planning", file: "GENERATED-PLANNING-CATALOG.md", label: "Planning" },
  { key: "faqs", file: "GENERATED-FAQ-CATALOG.md", label: "FAQs" },
  { key: "operations", file: "GENERATED-OPERATIONS-CATALOG.md", label: "Operations" },
  { key: "specifications", file: "GENERATED-SPECIFICATIONS-CATALOG.md", label: "Specifications" },
  { key: "sdks", file: "GENERATED-SDKS-CATALOG.md", label: "SDKs" },
];

/**
 * Read all 6 generated catalog files and return their content indexed by category.
 *
 * Returns { catalogs: { guides: string, planning: string, ... } }
 * or { error: string }.
 */
export async function getDocumentCatalog() {
  const projectDir = getProjectDir();
  const catalogDir = resolve(projectDir, "docs/generated");

  const catalogs = {};
  const errors = [];

  for (const { key, file, label } of CATALOG_FILES) {
    try {
      const content = await readFile(join(catalogDir, file), "utf-8");
      catalogs[key] = { label, content };
    } catch (err) {
      errors.push(`${file}: ${err.message}`);
    }
  }

  if (Object.keys(catalogs).length === 0) {
    return { error: `No catalogs found. Errors: ${errors.join("; ")}` };
  }

  return { catalogs, errors: errors.length > 0 ? errors : undefined };
}

// ─── Document Retrieval ───────────────────────────────────────────────────

/**
 * Read a specific document by path (relative to project root).
 *
 * Returns { path, content } or { error: string }.
 */
export async function getDocument(docPath) {
  if (!docPath || typeof docPath !== "string") {
    return { error: "Document path is required." };
  }

  const projectDir = getProjectDir();

  // Normalize: allow paths with or without docs/ prefix
  let resolved;
  if (docPath.startsWith("docs/") || docPath.startsWith("/")) {
    resolved = resolve(projectDir, docPath);
  } else {
    resolved = resolve(projectDir, "docs", docPath);
  }

  // Security: ensure we're within the project
  if (!resolved.startsWith(projectDir)) {
    return { error: "Path escapes project directory." };
  }

  try {
    const content = await readFile(resolved, "utf-8");
    const relPath = relative(projectDir, resolved);
    return { path: relPath, content };
  } catch (err) {
    if (err.code === "ENOENT") {
      return { error: `Document not found: ${docPath}` };
    }
    return { error: `Failed to read ${docPath}: ${err.message}` };
  }
}

// ─── Full-Text Search ─────────────────────────────────────────────────────

/** Cached search index state. */
let searchIndex = null;
let searchIndexMtime = null;

/**
 * Tokenize text into lowercase words for indexing/querying.
 * Strips markdown formatting, punctuation, and short tokens.
 */
function tokenize(text) {
  return text
    .toLowerCase()
    .replace(/[#*_`\[\](){}|>~=\-]/g, " ")
    .replace(/[^\w\s]/g, " ")
    .split(/\s+/)
    .filter((t) => t.length > 2);
}

/**
 * Recursively collect all .md files under a directory.
 */
async function collectMarkdownFiles(dir, files = []) {
  try {
    const entries = await readdir(dir, { withFileTypes: true });
    for (const entry of entries) {
      const fullPath = join(dir, entry.name);
      if (entry.isDirectory()) {
        // Skip node_modules, .git, bin, obj
        if (["node_modules", ".git", "bin", "obj", "Generated"].includes(entry.name)) continue;
        await collectMarkdownFiles(fullPath, files);
      } else if (entry.isFile() && extname(entry.name).toLowerCase() === ".md") {
        files.push(fullPath);
      }
    }
  } catch { /* directory not readable */ }
  return files;
}

/**
 * Build the inverted index from all markdown files in docs/.
 * Each entry maps a token to a list of { path, count } objects.
 */
async function buildIndex() {
  const projectDir = getProjectDir();
  const docsDir = resolve(projectDir, "docs");

  const files = await collectMarkdownFiles(docsDir);
  const index = new Map(); // token → [{ path, count, titleMatch }]
  const docMeta = new Map(); // path → { title, lineCount }

  for (const filePath of files) {
    try {
      const content = await readFile(filePath, "utf-8");
      const relPath = relative(projectDir, filePath);
      const lines = content.split("\n");

      // Extract title (first # heading)
      const titleLine = lines.find((l) => l.startsWith("# "));
      const title = titleLine ? titleLine.replace(/^#+\s*/, "").trim() : relPath;

      docMeta.set(relPath, { title, lineCount: lines.length });

      // Tokenize and count
      const tokens = tokenize(content);
      const titleTokens = new Set(tokenize(title));
      const freq = new Map();
      for (const token of tokens) {
        freq.set(token, (freq.get(token) || 0) + 1);
      }

      for (const [token, count] of freq) {
        if (!index.has(token)) index.set(token, []);
        index.get(token).push({
          path: relPath,
          count,
          titleMatch: titleTokens.has(token),
        });
      }
    } catch { /* skip unreadable files */ }
  }

  return { index, docMeta };
}

/**
 * Check if the search index needs rebuilding by comparing docs/ directory mtime.
 */
async function needsRebuild() {
  if (!searchIndex) return true;

  const projectDir = getProjectDir();
  const docsDir = resolve(projectDir, "docs");

  try {
    const dirStat = await stat(docsDir);
    if (!searchIndexMtime || dirStat.mtimeMs > searchIndexMtime) {
      return true;
    }
  } catch {
    return true;
  }

  return false;
}

/**
 * Search documentation for a query string.
 *
 * Tokenizes the query, looks up each token in the inverted index,
 * scores documents by frequency (with title match boost), and returns
 * the top results.
 *
 * Returns { query, results: [{ path, title, score, lineCount }], totalMatches }
 * or { error: string }.
 */
export async function searchDocs(query, maxResults = 20) {
  if (!query || typeof query !== "string" || query.trim().length === 0) {
    return { error: "Search query is required." };
  }

  // Lazy-build or rebuild index
  if (await needsRebuild()) {
    const projectDir = getProjectDir();
    const docsDir = resolve(projectDir, "docs");

    const { index, docMeta } = await buildIndex();
    searchIndex = { index, docMeta };

    try {
      const dirStat = await stat(docsDir);
      searchIndexMtime = dirStat.mtimeMs;
    } catch { /* mtime tracking failed — index will rebuild next time */ }
  }

  const { index, docMeta } = searchIndex;

  // Tokenize query
  const queryTokens = tokenize(query);
  if (queryTokens.length === 0) {
    return { error: "Query contains no searchable terms (tokens must be 3+ characters)." };
  }

  // Score documents
  const scores = new Map(); // path → score

  for (const token of queryTokens) {
    const entries = index.get(token);
    if (!entries) continue;

    for (const { path, count, titleMatch } of entries) {
      const score = count + (titleMatch ? 10 : 0);
      scores.set(path, (scores.get(path) || 0) + score);
    }
  }

  // Boost documents matching ALL query tokens
  for (const [path, score] of scores) {
    let matchedTokens = 0;
    for (const token of queryTokens) {
      const entries = index.get(token);
      if (entries && entries.some((e) => e.path === path)) {
        matchedTokens++;
      }
    }
    if (matchedTokens === queryTokens.length) {
      scores.set(path, score * 2); // Double score for full-match documents
    }
  }

  // Sort by score descending
  const sorted = [...scores.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, maxResults);

  const results = sorted.map(([path, score]) => {
    const meta = docMeta.get(path) || { title: path, lineCount: 0 };
    return {
      path,
      title: meta.title,
      score,
      lineCount: meta.lineCount,
    };
  });

  return {
    query,
    tokens: queryTokens,
    results,
    totalMatches: scores.size,
  };
}
