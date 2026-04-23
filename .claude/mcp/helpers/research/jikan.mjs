/**
 * Jikan (Unofficial MAL) REST API Client
 *
 * GET-only REST proxy for MyAnimeList data. No authentication required.
 * Rate limit: 60 requests/minute, 3 requests/second.
 * All responses cached 24 hours server-side.
 *
 * Base URL: https://api.jikan.moe/v4
 */

import { fetchJson } from "./http.mjs";

const BASE = "https://api.jikan.moe/v4";

// ═══════════════════════════════════════════════════════════════════════════
// ANIME
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Search anime by title.
 * @param {string} query - Search text
 * @returns {Promise<{results: object[], error?: string}>}
 */
export async function searchAnime(query) {
  const url = `${BASE}/anime?q=${encodeURIComponent(query)}&limit=5&order_by=relevance&sfw=true`;
  const result = await fetchJson(url);

  if (!result.ok) return { results: [], error: result.error };

  return { results: result.data?.data || [] };
}

/**
 * Get full anime details (all sub-resources) by MAL ID.
 * @param {number} malId - MyAnimeList anime ID
 * @returns {Promise<{anime: object|null, error?: string}>}
 */
export async function getAnimeFull(malId) {
  const url = `${BASE}/anime/${malId}/full`;
  const result = await fetchJson(url);

  if (!result.ok) return { anime: null, error: result.error };

  return { anime: result.data?.data || null };
}

/**
 * Get anime theme songs (opening/ending) by MAL ID.
 * @param {number} malId - MyAnimeList anime ID
 * @returns {Promise<{themes: {openings: string[], endings: string[]}|null, error?: string}>}
 */
export async function getAnimeThemes(malId) {
  const url = `${BASE}/anime/${malId}/themes`;
  const result = await fetchJson(url);

  if (!result.ok) return { themes: null, error: result.error };

  const data = result.data?.data;
  return {
    themes: data ? {
      openings: data.openings || [],
      endings: data.endings || [],
    } : null,
  };
}

/**
 * Get anime reviews by MAL ID.
 * @param {number} malId - MyAnimeList anime ID
 * @param {number} [limit=3] - Number of reviews to fetch
 * @returns {Promise<{reviews: object[], error?: string}>}
 */
export async function getAnimeReviews(malId, limit = 3) {
  const url = `${BASE}/anime/${malId}/reviews`;
  const result = await fetchJson(url);

  if (!result.ok) return { reviews: [], error: result.error };

  const reviews = (result.data?.data || []).slice(0, limit);
  return { reviews };
}

// ═══════════════════════════════════════════════════════════════════════════
// MANGA
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Search manga by title.
 * @param {string} query - Search text
 * @returns {Promise<{results: object[], error?: string}>}
 */
export async function searchManga(query) {
  const url = `${BASE}/manga?q=${encodeURIComponent(query)}&limit=5&order_by=relevance`;
  const result = await fetchJson(url);

  if (!result.ok) return { results: [], error: result.error };

  return { results: result.data?.data || [] };
}

/**
 * Get full manga details by MAL ID.
 * @param {number} malId - MyAnimeList manga ID
 * @returns {Promise<{manga: object|null, error?: string}>}
 */
export async function getMangaFull(malId) {
  const url = `${BASE}/manga/${malId}/full`;
  const result = await fetchJson(url);

  if (!result.ok) return { manga: null, error: result.error };

  return { manga: result.data?.data || null };
}

// ═══════════════════════════════════════════════════════════════════════════
// CHARACTERS
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Search characters by name.
 * @param {string} query - Search text
 * @returns {Promise<{results: object[], error?: string}>}
 */
export async function searchCharacter(query) {
  const url = `${BASE}/characters?q=${encodeURIComponent(query)}&limit=5&order_by=favorites&sort=desc`;
  const result = await fetchJson(url);

  if (!result.ok) return { results: [], error: result.error };

  return { results: result.data?.data || [] };
}

/**
 * Get full character details by MAL ID.
 * @param {number} malId - MyAnimeList character ID
 * @returns {Promise<{character: object|null, error?: string}>}
 */
export async function getCharacterFull(malId) {
  const url = `${BASE}/characters/${malId}/full`;
  const result = await fetchJson(url);

  if (!result.ok) return { character: null, error: result.error };

  return { character: result.data?.data || null };
}
