/**
 * VNDB API Client (v2 / Kana)
 *
 * POST-body JSON query API against https://api.vndb.org/kana.
 * No authentication required for public reads.
 * Rate limit: 200 requests / 5 minutes.
 *
 * Exports search and full-detail functions for VN and Character entities.
 */

import { postJson } from "./http.mjs";

const ENDPOINT = "https://api.vndb.org/kana";

// ═══════════════════════════════════════════════════════════════════════════
// VISUAL NOVELS
// ═══════════════════════════════════════════════════════════════════════════

const VN_SEARCH_FIELDS = "id, title, alttitle, olang, released, rating, votecount, length, platforms, languages";

const VN_FULL_FIELDS = [
  "id", "title", "alttitle", "olang", "devstatus", "released", "description",
  "aliases", "languages", "platforms",
  "length", "length_minutes", "length_votes",
  "average", "rating", "votecount",
  "image.url", "image.sexual", "image.violence",
  "titles{lang, title, latin, official, main}",
  "tags{name, category, rating, spoiler}",
  "developers{id, name, original}",
  "staff{name, original, role, note}",
  "va{character{id, name, original}, staff{id, name, original}}",
  "relations{relation, relation_official, id, title}",
  "extlinks{url, label, name}",
].join(", ");

/**
 * Search for visual novels by title.
 * @param {string} query - Search text
 * @returns {Promise<{results: object[], error?: string}>}
 */
export async function searchVN(query) {
  const result = await postJson(`${ENDPOINT}/vn`, {
    filters: ["search", "=", query],
    fields: VN_SEARCH_FIELDS,
    sort: "searchrank",
    results: 5,
  });

  if (!result.ok) return { results: [], error: result.error };
  return { results: result.data?.results || [] };
}

/**
 * Get full VN details by VNDB ID.
 * @param {string} id - VNDB VN ID (e.g., "v17")
 * @returns {Promise<{vn: object|null, error?: string}>}
 */
export async function getVNFull(id) {
  const result = await postJson(`${ENDPOINT}/vn`, {
    filters: ["id", "=", id],
    fields: VN_FULL_FIELDS,
    results: 1,
  });

  if (!result.ok) return { vn: null, error: result.error };
  return { vn: result.data?.results?.[0] || null };
}

// ═══════════════════════════════════════════════════════════════════════════
// CHARACTERS
// ═══════════════════════════════════════════════════════════════════════════

const CHAR_FULL_FIELDS = [
  "id", "name", "original", "aliases", "description",
  "blood_type", "height", "weight", "bust", "waist", "hips", "cup", "age",
  "birthday", "sex", "gender",
  "image.url", "image.sexual", "image.violence",
  "vns{spoiler, role, id, title}",
  "traits{name, group_name, spoiler}",
].join(", ");

/**
 * Get characters for a specific VN.
 * @param {string} vnId - VNDB VN ID (e.g., "v17")
 * @param {number} [limit=25] - Max characters to return
 * @returns {Promise<{characters: object[], error?: string}>}
 */
export async function getVNCharacters(vnId, limit = 25) {
  const result = await postJson(`${ENDPOINT}/character`, {
    filters: ["vn", "=", ["id", "=", vnId]],
    fields: CHAR_FULL_FIELDS,
    sort: "name",
    results: limit,
  });

  if (!result.ok) return { characters: [], error: result.error };
  return { characters: result.data?.results || [] };
}

/**
 * Get releases for a specific VN.
 * @param {string} vnId - VNDB VN ID
 * @param {number} [limit=20] - Max releases to return
 * @returns {Promise<{releases: object[], error?: string}>}
 */
export async function getVNReleases(vnId, limit = 20) {
  const RELEASE_FIELDS = [
    "id", "title", "released", "minage", "freeware", "uncensored", "official",
    "has_ero", "voiced",
    "languages{lang, title, mtl}",
    "platforms",
    "producers{name, developer, publisher}",
    "extlinks{url, label, name}",
  ].join(", ");

  const result = await postJson(`${ENDPOINT}/release`, {
    filters: ["vn", "=", ["id", "=", vnId]],
    fields: RELEASE_FIELDS,
    sort: "released",
    results: limit,
  });

  if (!result.ok) return { releases: [], error: result.error };
  return { releases: result.data?.results || [] };
}
