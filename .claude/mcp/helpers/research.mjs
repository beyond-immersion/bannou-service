/**
 * Research Helper — Headless Browser + Structured API Research
 *
 * Two modes:
 *   1. URL mode: Headless Chromium fetching for bot-blocked sites (Fandom, TV Tropes)
 *   2. Domain mode: Structured API queries (AniList, Jikan, VNDB, MusicBrainz, etc.)
 *
 * Domain mode queries multiple APIs in parallel, compiles results into a markdown
 * document in /tmp/, and adds it to the required reading gate so the agent must
 * full-read the research before continuing.
 *
 * ARCHITECTURE:
 *   fetchPage(url) → headless browser, returns text content
 *   researchDomain(domain, query) → structured API calls, returns gated file path
 *
 * API clients live in ./research/*.mjs — each exports stateless async functions.
 * Domain handlers here orchestrate multi-API calls and delegate to the compiler.
 */

import { chromium } from "playwright";
import * as anilist from "./research/anilist.mjs";
import * as jikan from "./research/jikan.mjs";
import * as vndb from "./research/vndb.mjs";
import * as narou from "./research/narou.mjs";
import { compile } from "./research/compiler.mjs";

// ─── Browser Singleton ──────────────────────────────────────────────────
// Reuse a single browser instance across calls to avoid 500ms+ startup per request.
// Launched lazily on first call, cleaned up on process exit.

let _browser = null;
let _browserLaunchPromise = null;

async function getBrowser() {
  if (_browser && _browser.isConnected()) return _browser;

  // Prevent concurrent launches
  if (_browserLaunchPromise) return _browserLaunchPromise;

  _browserLaunchPromise = (async () => {
    try {
      _browser = await chromium.launch({
        headless: true,
        args: [
          "--disable-blink-features=AutomationControlled",
          "--no-sandbox",
          "--disable-setuid-sandbox",
          "--disable-dev-shm-usage",
        ],
      });
      return _browser;
    } catch (err) {
      // Clear the cached promise so the next call retries instead of
      // returning the same stale rejection forever (e.g., after browser install)
      _browserLaunchPromise = null;
      throw err;
    }
  })();

  const browser = await _browserLaunchPromise;
  _browserLaunchPromise = null;
  return browser;
}

// Cleanup on process exit
const cleanup = async () => {
  if (_browser) {
    try { await _browser.close(); } catch { /* ignore */ }
    _browser = null;
  }
};
process.on("exit", () => { cleanup(); });
process.on("SIGINT", () => { cleanup(); process.exit(0); });
process.on("SIGTERM", () => { cleanup(); process.exit(0); });

// ─── URL Whitelist ──────────────────────────────────────────────────────
// Only allow fetching from domains we've identified as useful research sources.
// This prevents the tool from becoming a general-purpose web browser.

const ALLOWED_DOMAINS = [
  // Wikis — English (the original pain point — these block bot user agents)
  "fandom.com",
  "tvtropes.org",
  "wikipedia.org",
  "wikia.com",

  // Wikis — Japanese fan encyclopedias (powerhouse deep lore for popular series)
  "dic.pixiv.net",         // ピクシブ百科事典 — extensive character/series/term entries
  "dic.nicovideo.jp",      // ニコニコ大百科 — similar depth to Pixiv encyclopedia

  // Wikis — Korean (legendary depth for anime/LN/VN)
  "namu.wiki",             // 나무위키 — often more detailed than EN or JP wikis

  // Anime/Manga databases
  "anilist.co",
  "myanimelist.net",
  "anime-planet.com",

  // Japanese analysis & review sites (deep lore, 考察, ネタバレ)
  "note.com",              // Blog-style deep analysis pieces, often spoiler-rich
  "bookmeter.com",         // 読書メーター — per-volume reader reviews (Kadokawa)
  "booklive.jp",           // Ebook store with detailed user reviews per volume
  "sakuhindb.com",         // 作品データベース — comprehensive anime/manga database + reviews
  "anikore.jp",            // あにこれ — anime reviews and analysis

  // Music databases
  "musicbrainz.org",
  "last.fm",
  "genius.com",
  "songmeaningsandfacts.com",
  "songtell.com",

  // Light novel / visual novel databases
  "ranobedb.org",
  "vndb.org",
  "novelupdates.com",
  "j-novel.club",
  "ncode.syosetu.com",     // 小説家になろう — web novel text/info pages
  "syosetu.com",           // Syosetu main site

  // Game databases
  "igdb.com",
  "rawg.io",

  // Movie / TV databases
  "themoviedb.org",
  "imdb.com",

  // Music analysis / theory
  "hooktheory.com",
  "musicstax.com",
  "chosic.com",

  // General reference (already accessible but sometimes flaky)
  "goodreads.com",
  "rateyourmusic.com",

  // API documentation
  "docs.api.jikan.moe",
  "developer.spotify.com",
  "api-docs.igdb.com",
  "developer.themoviedb.org",
];

function isDomainAllowed(url) {
  try {
    const hostname = new URL(url).hostname.toLowerCase();
    return ALLOWED_DOMAINS.some(
      (domain) => hostname === domain || hostname.endsWith(`.${domain}`)
    );
  } catch {
    return false;
  }
}

// ─── Content Extraction ─────────────────────────────────────────────────
// Strip navigation, ads, sidebars, and scripts to return clean article text.

async function extractPageContent(page) {
  // Wait for main content to load
  await page.waitForLoadState("domcontentloaded");

  // Give JS-rendered content a moment (Fandom is heavy on client-side rendering)
  await page.waitForTimeout(1500);

  // Extract text content, stripping nav/ads/sidebars
  const content = await page.evaluate(() => {
    // Remove noise elements
    const removeSelectors = [
      "nav", "header", "footer", ".ad", ".ads", ".advertisement",
      ".sidebar", "#sidebar", ".nav", "#nav", ".navigation",
      ".cookie-banner", ".cookie-notice", ".popup", ".modal",
      ".social-share", ".share-buttons", "script", "style",
      ".notifications-placeholder", ".page-header__languages",
      ".fandom-sticky-header", ".global-navigation",
      ".wiki-side-bar", ".WikiaRail", ".wikia-rail",
      "[data-tracking-label='advertisement']",
      ".mw-editsection",
    ];

    for (const sel of removeSelectors) {
      for (const el of document.querySelectorAll(sel)) {
        el.remove();
      }
    }

    // Try to find the main content area
    const contentSelectors = [
      // Fandom / Wikia
      ".mw-parser-output",
      ".page-content",
      "#content",
      ".WikiaArticle",

      // TV Tropes
      "#main-article",
      ".article-content",

      // Wikipedia
      "#bodyContent",
      "#mw-content-text",

      // Japanese fan encyclopedias
      "#article-body",         // Pixiv Encyclopedia (dic.pixiv.net)
      ".a-body",               // Nico Nico Encyclopedia (dic.nicovideo.jp)

      // Korean wiki
      ".wiki-content",         // Namu Wiki (namu.wiki)
      ".wiki-paragraph",       // Namu Wiki alternate

      // Japanese review/analysis sites
      ".note-common-styles__textnote-body", // note.com article body
      "article.note-body",     // note.com alternate
      ".review-body",          // booklive.jp reviews
      ".review__text",         // bookmeter.com reviews
      ".p-review",             // sakuhindb.com reviews

      // Novel sites
      "#novel_honbun",         // Syosetu (ncode.syosetu.com) novel text
      "#novel_ex",             // Syosetu novel synopsis
      ".novel_view",           // Syosetu novel page

      // Generic
      "main",
      "article",
      "[role='main']",
      ".content",
      "#main-content",
    ];

    for (const sel of contentSelectors) {
      const el = document.querySelector(sel);
      if (el && el.textContent.trim().length > 200) {
        return el.textContent.trim();
      }
    }

    // Fallback: body text
    return document.body.textContent.trim();
  });

  // Clean up whitespace
  return content
    .replace(/\n{3,}/g, "\n\n")
    .replace(/[ \t]+/g, " ")
    .replace(/^ +/gm, "")
    .trim();
}

// ─── Main Research Function ─────────────────────────────────────────────

/**
 * Fetch a URL using headless Chromium and extract text content.
 *
 * @param {string} url - The URL to fetch (must be on an allowed domain)
 * @param {number} [timeoutMs=15000] - Navigation timeout in milliseconds
 * @returns {{ content: string, title: string, url: string, charCount: number, truncated: boolean, error?: string }}
 */
export async function fetchPage(url, timeoutMs = 15000) {
  // Validate domain
  if (!isDomainAllowed(url)) {
    let hostname;
    try { hostname = new URL(url).hostname; } catch { hostname = url; }
    return {
      error: `Domain not in whitelist: ${hostname}\n\nAllowed domains:\n${ALLOWED_DOMAINS.map(d => `  - ${d}`).join("\n")}`,
      content: "",
      title: "",
      url,
      charCount: 0,
      truncated: false,
    };
  }

  let context = null;
  try {
    const browser = await getBrowser();

    // Each fetch gets its own context (isolated cookies/state)
    context = await browser.newContext({
      userAgent: "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
      viewport: { width: 1920, height: 1080 },
      locale: "en-US",
      bypassCSP: true,
    });

    // Block images, fonts, media to speed up loading
    await context.route("**/*", (route) => {
      const type = route.request().resourceType();
      if (["image", "media", "font", "stylesheet"].includes(type)) {
        return route.abort();
      }
      return route.continue();
    });

    const page = await context.newPage();

    // Navigate
    const response = await page.goto(url, {
      waitUntil: "domcontentloaded",
      timeout: timeoutMs,
    });

    if (!response) {
      return { error: `No response from ${url}`, content: "", title: "", url, charCount: 0, truncated: false };
    }

    const status = response.status();
    if (status >= 400) {
      return { error: `HTTP ${status} from ${url}`, content: "", title: "", url, charCount: 0, truncated: false };
    }

    // Check for redirects to a different domain
    const finalUrl = page.url();
    const finalHostname = new URL(finalUrl).hostname;
    const originalHostname = new URL(url).hostname;
    if (finalHostname !== originalHostname && !isDomainAllowed(finalUrl)) {
      return {
        error: `Redirected to non-whitelisted domain: ${finalHostname}`,
        content: "",
        title: "",
        url: finalUrl,
        charCount: 0,
        truncated: false,
      };
    }

    const title = await page.title();
    const content = await extractPageContent(page);

    // Truncate very long pages to avoid overwhelming context
    const MAX_CHARS = 50000;
    const truncated = content.length > MAX_CHARS;
    const finalContent = truncated
      ? content.slice(0, MAX_CHARS) + `\n\n[... truncated at ${MAX_CHARS} characters — ${content.length} total]`
      : content;

    return {
      content: finalContent,
      title,
      url: finalUrl,
      truncated,
      charCount: content.length,
    };
  } catch (err) {
    return {
      error: `Fetch failed: ${err.message}`,
      content: "",
      title: "",
      url,
      charCount: 0,
      truncated: false,
    };
  } finally {
    if (context) {
      try { await context.close(); } catch { /* ignore */ }
    }
  }
}

/**
 * Get the list of allowed domains (for tool description / help).
 */
export function getAllowedDomains() {
  return [...ALLOWED_DOMAINS];
}

// ═══════════════════════════════════════════════════════════════════════════
// DOMAIN MODE — Structured API Research
// ═══════════════════════════════════════════════════════════════════════════

const VALID_DOMAINS = [
  "anime", "manga", "character", "visual-novel", "light-novel",
  "music-artist", "music-track", "movie", "tv", "game",
];

// ─── Domain Handlers ──────────────────────────────────────────────────
// Each handler: search → resolve IDs → parallel detail fetches → compile

async function handleAnimeDomain(query) {
  // 1. Search both APIs in parallel for ID resolution
  const [anilistSearch, jikanSearch] = await Promise.allSettled([
    anilist.searchMedia(query, "ANIME"),
    jikan.searchAnime(query),
  ]);

  const anilistResults = anilistSearch.status === "fulfilled" ? anilistSearch.value : { results: [], error: anilistSearch.reason?.message };
  const jikanResults = jikanSearch.status === "fulfilled" ? jikanSearch.value : { results: [], error: jikanSearch.reason?.message };

  // 2. Pick best match from AniList (primary)
  const media = anilistResults.results?.[0];
  if (!media) {
    // Fallback: try Jikan results
    const jikanMedia = jikanResults.results?.[0];
    if (!jikanMedia) {
      return { error: `No anime found for "${query}"` };
    }
    // Jikan-only path: get what we can
    const jikanFull = await jikan.getAnimeFull(jikanMedia.mal_id);
    const jikanThemesResult = await jikan.getAnimeThemes(jikanMedia.mal_id);
    const result = await compile("anime", query, {
      anilist: null,
      jikan: jikanFull.anime,
      jikanThemes: jikanThemesResult.themes,
    });
    return result;
  }

  // 3. Fire ALL detail queries in parallel
  const detailPromises = [
    anilist.getMediaFull(media.id),
  ];

  // Add Jikan queries if we have a MAL ID
  const malId = media.idMal || jikanResults.results?.[0]?.mal_id;
  if (malId) {
    detailPromises.push(jikan.getAnimeFull(malId));
    detailPromises.push(jikan.getAnimeThemes(malId));
  } else {
    detailPromises.push(Promise.resolve({ anime: null }));
    detailPromises.push(Promise.resolve({ themes: null }));
  }

  const [anilistFull, jikanFull, jikanThemes] = await Promise.allSettled(detailPromises);

  // 4. Compile into research document
  const result = await compile("anime", query, {
    anilist: anilistFull.status === "fulfilled" ? anilistFull.value?.media : null,
    jikan: jikanFull.status === "fulfilled" ? jikanFull.value?.anime : null,
    jikanThemes: jikanThemes.status === "fulfilled" ? jikanThemes.value?.themes : null,
  });

  // 5. Add alternative results for disambiguation
  if (anilistResults.results?.length > 1) {
    result.alternatives = anilistResults.results.slice(1).map((m) => ({
      title: m.title?.english || m.title?.romaji,
      format: m.format,
      year: m.seasonYear,
      score: m.averageScore,
    }));
  }

  return result;
}

async function handleMangaDomain(query) {
  // Same pattern as anime but with MANGA type
  const [anilistSearch] = await Promise.allSettled([
    anilist.searchMedia(query, "MANGA"),
  ]);

  const results = anilistSearch.status === "fulfilled" ? anilistSearch.value : { results: [] };
  const media = results.results?.[0];
  if (!media) return { error: `No manga found for "${query}"` };

  const anilistFull = await anilist.getMediaFull(media.id);

  // Reuse anime compiler for now — manga fields are a superset
  return compile("anime", query, {
    anilist: anilistFull.media,
    jikan: null,
    jikanThemes: null,
  });
}

async function handleVisualNovelDomain(query) {
  // 1. Search VNDB
  const searchResult = await vndb.searchVN(query);
  const vn = searchResult.results?.[0];
  if (!vn) return { error: `No visual novel found for "${query}"` };

  // 2. Fire ALL detail queries in parallel
  const [fullVN, characters, releases] = await Promise.allSettled([
    vndb.getVNFull(vn.id),
    vndb.getVNCharacters(vn.id),
    vndb.getVNReleases(vn.id),
  ]);

  // 3. Compile
  const result = await compile("visual-novel", query, {
    vndb: fullVN.status === "fulfilled" ? fullVN.value?.vn : null,
    characters: characters.status === "fulfilled" ? characters.value?.characters : [],
    releases: releases.status === "fulfilled" ? releases.value?.releases : [],
  });

  // 4. Alternatives
  if (searchResult.results?.length > 1) {
    result.alternatives = searchResult.results.slice(1).map((v) => ({
      title: v.title,
      year: v.released,
      score: v.rating ? (v.rating / 10).toFixed(1) : null,
    }));
  }

  return result;
}

// ─── Deep Search Helper ─────────────────────────────────────────────
// Searches Japanese review/analysis sites for deep lore content.
// Uses the headless browser to fetch pages that block bot user agents.
// Returns extracted text snippets keyed by source.

async function deepSearchJP(titleJP, titleEN) {
  const results = { reviews: [], analyses: [], errors: [] };

  // Build search queries — try Japanese title first, fall back to English
  const searchTitle = titleJP || titleEN;
  if (!searchTitle) return results;

  // Search bookmeter for reader reviews (last volume = most spoilers)
  // Search note.com for analysis pieces
  // These are fetched via headless browser since they may block bots
  const targets = [
    {
      url: `https://bookmeter.com/search?keyword=${encodeURIComponent(searchTitle)}`,
      source: "bookmeter",
      type: "reviews",
    },
    {
      url: `https://note.com/search?q=${encodeURIComponent(searchTitle)}&context=note&mode=search`,
      source: "note.com",
      type: "analyses",
    },
  ];

  // Fetch in parallel with short timeouts — these are supplementary, not critical
  const fetches = targets.map(async (target) => {
    try {
      const page = await fetchPage(target.url, 12000);
      if (page.error || !page.content || page.content.length < 100) {
        results.errors.push(`${target.source}: ${page.error || "no content"}`);
        return;
      }
      // Truncate to avoid overwhelming the research doc
      const content = page.content.length > 3000
        ? page.content.slice(0, 3000) + "\n[... truncated]"
        : page.content;
      results[target.type].push({ source: target.source, content, url: page.url });
    } catch (err) {
      results.errors.push(`${target.source}: ${err.message}`);
    }
  });

  await Promise.allSettled(fetches);
  return results;
}

async function handleLightNovelDomain(query) {
  // 1. Search AniList (NOVEL format), Narou, and deep JP sites in parallel
  const [anilistSearch, narouSearch] = await Promise.allSettled([
    anilist.searchMedia(query, "MANGA"),
    narou.searchNovel(query),
  ]);

  const anilistResults = anilistSearch.status === "fulfilled" ? anilistSearch.value : { results: [] };
  const narouResults = narouSearch.status === "fulfilled" ? narouSearch.value : { results: [], total: 0 };

  // 2. Pick AniList match — prefer NOVEL format, fall back to any MANGA match
  let media = anilistResults.results?.find((m) => m.format === "NOVEL") || anilistResults.results?.[0];

  // Best Narou match
  const narouNovel = narouResults.results?.[0] || null;

  if (!media && !narouNovel) {
    return { error: `No light novel found for "${query}"` };
  }

  // 3. Get full details + deep search in parallel
  const detailPromises = [];

  if (media) {
    detailPromises.push(anilist.getMediaFull(media.id));
  } else {
    detailPromises.push(Promise.resolve({ media: null }));
  }

  detailPromises.push(Promise.resolve(narouNovel));

  // Deep search using Japanese title (from AniList native) or the query itself
  const jpTitle = media?.title?.native || (narouNovel?.title) || null;
  detailPromises.push(deepSearchJP(jpTitle, media?.title?.romaji || query));

  const [anilistFull, narouFull, deepSearch] = await Promise.allSettled(detailPromises);

  // 4. Compile
  const result = await compile("light-novel", query, {
    anilist: anilistFull.status === "fulfilled" ? (anilistFull.value?.media || null) : null,
    narou: narouFull.status === "fulfilled" ? narouFull.value : null,
    deepSearch: deepSearch.status === "fulfilled" ? deepSearch.value : null,
  });

  // 5. Alternatives
  const alts = [];
  if (anilistResults.results?.length > 1) {
    for (const m of anilistResults.results.slice(1, 4)) {
      alts.push({ title: m.title?.english || m.title?.romaji, format: m.format, year: m.seasonYear, score: m.averageScore });
    }
  }
  if (narouResults.results?.length > 1) {
    for (const n of narouResults.results.slice(1, 3)) {
      alts.push({ title: n.title, format: "WEB NOVEL", year: n.firstPosted?.slice(0, 4), score: null });
    }
  }
  if (alts.length) result.alternatives = alts;

  return result;
}

// ─── Domain Router ────────────────────────────────────────────────────

const DOMAIN_HANDLERS = {
  anime: handleAnimeDomain,
  manga: handleMangaDomain,
  "visual-novel": handleVisualNovelDomain,
  "light-novel": handleLightNovelDomain,
  // character: handleCharacterDomain,       // future
  // "music-artist": handleMusicArtistDomain, // future
  // "music-track": handleMusicTrackDomain,   // future
  // movie: handleMovieDomain,                // future
  // tv: handleTvDomain,                      // future
  // game: handleGameDomain,                  // future
};

/**
 * Research a topic via structured API queries.
 *
 * Queries multiple APIs for the given domain, compiles results into a
 * markdown document in /tmp/, and adds it to the required reading gate.
 *
 * @param {string} domain - Research domain (anime, manga, character, etc.)
 * @param {string} query - What to research (title, character name, artist, etc.)
 * @returns {Promise<{path?: string, charCount?: number, summary?: string, alternatives?: object[], error?: string}>}
 */
export async function researchDomain(domain, query) {
  if (!VALID_DOMAINS.includes(domain)) {
    return { error: `Unknown domain: "${domain}". Valid domains: ${VALID_DOMAINS.join(", ")}` };
  }

  const handler = DOMAIN_HANDLERS[domain];
  if (!handler) {
    return { error: `Domain "${domain}" is not yet implemented. Implemented: ${Object.keys(DOMAIN_HANDLERS).join(", ")}` };
  }

  try {
    return await handler(query);
  } catch (err) {
    return { error: `Research failed for ${domain}:"${query}": ${err.message}` };
  }
}

/**
 * Get the list of valid research domains.
 */
export function getValidDomains() {
  return [...VALID_DOMAINS];
}

/**
 * Get the list of currently implemented domains.
 */
export function getImplementedDomains() {
  return Object.keys(DOMAIN_HANDLERS);
}
