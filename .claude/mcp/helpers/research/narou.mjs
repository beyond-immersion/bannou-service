/**
 * Narou (Syosetu) Novel API Client
 *
 * Official API for 小説家になろう (Shousetsuka ni Narou), Japan's largest
 * web novel platform. Many light novels originate as web novels here.
 *
 * Base URL: https://api.syosetu.com/novelapi/api/
 * No authentication required. Rate limit: 80,000 requests/day per IP.
 * GET only, JSON output via out=json parameter.
 */

import { fetchJson } from "./http.mjs";

const BASE = "https://api.syosetu.com/novelapi/api/";

// ─── Genre Mappings ─────────────────────────────────────────────────────

const BIG_GENRES = {
  1: "Romance", 2: "Fantasy", 3: "Literature", 4: "Sci-Fi", 99: "Other", 98: "Non-Genre",
};

const GENRES = {
  101: "Romance (Other)", 102: "Romance (Fantasy)", 201: "High Fantasy",
  202: "Low Fantasy", 301: "Pure Literature", 302: "Drama/Literature",
  303: "Historical", 304: "Mystery", 305: "Horror", 306: "Action",
  307: "Comedy", 401: "VR Game", 402: "Space", 403: "Science Fantasy",
  404: "Panic", 9901: "Fairy Tale", 9902: "Poetry", 9903: "Essay",
  9904: "Replay", 9999: "Other", 9801: "Nonfiction",
};

// ─── Helpers ────────────────────────────────────────────────────────────

function buildUrl(params) {
  const url = new URL(BASE);
  url.searchParams.set("out", "json");
  url.searchParams.set("lim", "5");
  for (const [key, val] of Object.entries(params)) {
    if (val !== undefined && val !== null) url.searchParams.set(key, String(val));
  }
  return url.toString();
}

function parseNovel(raw) {
  if (!raw || typeof raw !== "object" || raw.allcount !== undefined) return null;
  return {
    title: raw.title,
    ncode: raw.ncode,
    author: raw.writer,
    authorId: raw.userid,
    synopsis: raw.story,
    bigGenre: BIG_GENRES[raw.biggenre] || `Unknown (${raw.biggenre})`,
    genre: GENRES[raw.genre] || `Unknown (${raw.genre})`,
    keywords: raw.keyword ? raw.keyword.split(" ").filter(Boolean) : [],
    firstPosted: raw.general_firstup,
    lastPosted: raw.general_lastup,
    isSerial: raw.novel_type === 1,
    isComplete: raw.end === 0,
    episodes: raw.general_all_no,
    charCount: raw.length,
    readingTimeMinutes: raw.time,
    isOnHiatus: raw.isstop === 1,
    isR15: raw.isr15 === 1,
    isBL: raw.isbl === 1,
    isGL: raw.isgl === 1,
    isBrutal: raw.iszankoku === 1,
    isIsekai: raw.istensei === 1 || raw.istenni === 1,
    isekaiType: raw.istensei === 1 ? "reincarnation" : raw.istenni === 1 ? "transfer" : null,
    globalPoints: raw.global_point,
    bookmarks: raw.fav_novel_cnt,
    reviews: raw.review_cnt,
    ratingPoints: raw.all_point,
    ratingCount: raw.all_hyoka_cnt,
    illustrations: raw.sasie_cnt,
    dialogueRate: raw.kaiwaritu,
    lastContentUpdate: raw.novelupdated_at,
    url: raw.ncode ? `https://ncode.syosetu.com/${raw.ncode.toLowerCase()}/` : null,
  };
}

// ═══════════════════════════════════════════════════════════════════════════
// EXPORTED FUNCTIONS
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Search for novels by title/keyword.
 * @param {string} query - Search text (AND search, space-separated)
 * @returns {Promise<{results: object[], total: number, error?: string}>}
 */
export async function searchNovel(query) {
  const url = buildUrl({ word: query, order: "hyoka", lim: 5 });
  const result = await fetchJson(url);

  if (!result.ok) return { results: [], total: 0, error: result.error };

  const data = result.data;
  if (!Array.isArray(data)) return { results: [], total: 0, error: "Unexpected response format" };

  // First element is the count object: { allcount: N }
  const total = data[0]?.allcount || 0;
  const novels = data.slice(1).map(parseNovel).filter(Boolean);

  return { results: novels, total };
}

/**
 * Get full novel details by N-code.
 * @param {string} ncode - Novel N-code (e.g., "n9669bk")
 * @returns {Promise<{novel: object|null, error?: string}>}
 */
export async function getNovelByNcode(ncode) {
  const url = buildUrl({ ncode, lim: 1 });
  const result = await fetchJson(url);

  if (!result.ok) return { novel: null, error: result.error };

  const data = result.data;
  if (!Array.isArray(data) || data.length < 2) return { novel: null, error: "Novel not found" };

  return { novel: parseNovel(data[1]) };
}

/**
 * Search specifically for isekai novels.
 * @param {string} query - Search text
 * @param {"reincarnation"|"transfer"|"both"} [type="both"] - Isekai type
 * @returns {Promise<{results: object[], total: number, error?: string}>}
 */
export async function searchIsekai(query, type = "both") {
  const params = { word: query, order: "hyoka", lim: 5 };
  if (type === "reincarnation") params.istensei = 1;
  else if (type === "transfer") params.istenni = 1;
  else params.istt = 1;

  const url = buildUrl(params);
  const result = await fetchJson(url);

  if (!result.ok) return { results: [], total: 0, error: result.error };

  const data = result.data;
  if (!Array.isArray(data)) return { results: [], total: 0, error: "Unexpected response format" };

  const total = data[0]?.allcount || 0;
  const novels = data.slice(1).map(parseNovel).filter(Boolean);

  return { results: novels, total };
}
