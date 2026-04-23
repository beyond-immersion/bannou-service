/**
 * Shared HTTP client for research API integrations.
 *
 * Provides rate-limited fetch with per-domain throttling, automatic JSON parsing,
 * User-Agent identification, and error normalization.
 *
 * All API clients import fetchJson/fetchGraphQL from here instead of using raw fetch.
 */

// ─── Rate Limiting ──────────────────────────────────────────────────────
// Per-domain request timestamps for sliding-window rate limiting.
// Each domain has a minimum interval between requests.

const DOMAIN_INTERVALS = {
  "graphql.anilist.co": 700,       // 90/min → ~667ms, add margin
  "api.jikan.moe": 350,            // 3/sec → 333ms, add margin
  "api.vndb.org": 1500,            // 200/5min → 1500ms
  "ranobedb.org": 1000,            // undocumented, be conservative
  "musicbrainz.org": 1100,         // 1/sec strict — add margin
  "ws.audioscrobbler.com": 200,    // undocumented, generous
  "api.themoviedb.org": 260,       // ~40/10sec → 250ms
  "api.igdb.com": 260,             // 4/sec → 250ms
};

const lastRequestTime = new Map();

function getDomainKey(url) {
  try {
    return new URL(url).hostname;
  } catch {
    return "unknown";
  }
}

async function waitForRateLimit(url) {
  const domain = getDomainKey(url);
  const interval = DOMAIN_INTERVALS[domain] || 500;
  const last = lastRequestTime.get(domain) || 0;
  const elapsed = Date.now() - last;

  if (elapsed < interval) {
    await new Promise((resolve) => setTimeout(resolve, interval - elapsed));
  }

  lastRequestTime.set(domain, Date.now());
}

// ─── User-Agent ─────────────────────────────────────────────────────────

const USER_AGENT = "BannouResearch/1.0 (https://github.com/beyondimmersion/bannou; dev-tooling)";

// ─── Core Fetch Functions ───────────────────────────────────────────────

/**
 * Rate-limited JSON fetch with error normalization.
 *
 * @param {string} url - Full URL to fetch
 * @param {object} [options] - Fetch options (method, headers, body, etc.)
 * @returns {Promise<{data: any, status: number, ok: boolean, error?: string}>}
 */
export async function fetchJson(url, options = {}) {
  await waitForRateLimit(url);

  const headers = {
    "User-Agent": USER_AGENT,
    "Accept": "application/json",
    ...options.headers,
  };

  try {
    const response = await fetch(url, { ...options, headers });

    if (response.status === 429) {
      // Rate limited — wait and retry once
      const retryAfter = parseInt(response.headers.get("Retry-After") || "5", 10);
      await new Promise((resolve) => setTimeout(resolve, retryAfter * 1000));
      lastRequestTime.set(getDomainKey(url), Date.now());

      const retry = await fetch(url, { ...options, headers });
      if (!retry.ok) {
        return { data: null, status: retry.status, ok: false, error: `HTTP ${retry.status} after retry` };
      }
      const data = await retry.json();
      return { data, status: retry.status, ok: true };
    }

    if (!response.ok) {
      const text = await response.text().catch(() => "");
      return { data: null, status: response.status, ok: false, error: `HTTP ${response.status}: ${text.slice(0, 200)}` };
    }

    const data = await response.json();
    return { data, status: response.status, ok: true };
  } catch (err) {
    return { data: null, status: 0, ok: false, error: `Fetch error: ${err.message}` };
  }
}

/**
 * Rate-limited GraphQL query (POST with JSON body).
 *
 * @param {string} endpoint - GraphQL endpoint URL
 * @param {string} query - GraphQL query string
 * @param {object} [variables] - GraphQL variables
 * @returns {Promise<{data: any, errors?: any[], ok: boolean, error?: string}>}
 */
export async function fetchGraphQL(endpoint, query, variables = {}) {
  const result = await fetchJson(endpoint, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ query, variables }),
  });

  if (!result.ok) return { data: null, ok: false, error: result.error };

  // GraphQL can return 200 with errors
  if (result.data?.errors?.length > 0) {
    const msg = result.data.errors.map((e) => e.message).join("; ");
    // If there's also data, it's a partial success — return both
    if (result.data.data) {
      return { data: result.data.data, errors: result.data.errors, ok: true, error: `Partial: ${msg}` };
    }
    return { data: null, errors: result.data.errors, ok: false, error: msg };
  }

  return { data: result.data?.data || result.data, ok: true };
}

/**
 * Rate-limited POST with JSON body (for VNDB-style APIs).
 *
 * @param {string} url - API endpoint
 * @param {object} body - JSON body
 * @param {object} [extraHeaders] - Additional headers
 * @returns {Promise<{data: any, status: number, ok: boolean, error?: string}>}
 */
export async function postJson(url, body, extraHeaders = {}) {
  return fetchJson(url, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...extraHeaders },
    body: JSON.stringify(body),
  });
}
