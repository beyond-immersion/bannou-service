/**
 * AniList GraphQL API Client
 *
 * Public read-only queries against https://graphql.anilist.co.
 * No authentication required. Rate limit: 90 requests/minute.
 *
 * Exports search and full-detail functions for Media and Character entities.
 * All functions return normalized objects — callers never see raw GraphQL responses.
 */

import { fetchGraphQL } from "./http.mjs";

const ENDPOINT = "https://graphql.anilist.co";

// ═══════════════════════════════════════════════════════════════════════════
// GRAPHQL QUERIES — Pre-built query strings requesting exact fields needed
// ═══════════════════════════════════════════════════════════════════════════

const SEARCH_MEDIA_QUERY = `
query ($search: String, $type: MediaType) {
  Page(perPage: 5) {
    media(search: $search, type: $type, sort: SEARCH_MATCH) {
      id
      idMal
      title { romaji english native }
      format
      status
      season
      seasonYear
      episodes
      chapters
      volumes
      averageScore
      popularity
      isAdult
    }
  }
}`;

const FULL_MEDIA_QUERY = `
query ($id: Int) {
  Media(id: $id) {
    id
    idMal
    title { romaji english native userPreferred }
    type
    format
    status
    description(asHtml: false)
    season
    seasonYear
    startDate { year month day }
    endDate { year month day }
    episodes
    chapters
    volumes
    duration
    countryOfOrigin
    isLicensed
    source
    isAdult
    averageScore
    meanScore
    popularity
    favourites
    trending
    genres
    tags {
      name
      description
      category
      rank
      isMediaSpoiler
      isGeneralSpoiler
    }
    characters(sort: [ROLE, RELEVANCE], perPage: 25) {
      edges {
        role
        voiceActors(language: JAPANESE) {
          name { full native }
          languageV2
        }
        voiceActorRoles {
          voiceActor {
            name { full native }
            languageV2
          }
          roleNotes
        }
        node {
          name { full native alternative }
          image { medium }
          description(asHtml: false)
          gender
          age
        }
      }
    }
    staff(sort: RELEVANCE, perPage: 25) {
      edges {
        role
        node {
          name { full native }
          primaryOccupations
          image { medium }
        }
      }
    }
    studios {
      edges {
        isMain
        node {
          name
          isAnimationStudio
          siteUrl
        }
      }
    }
    relations {
      edges {
        relationType(version: 2)
        node {
          id
          title { romaji english }
          format
          type
          status
        }
      }
    }
    recommendations(sort: RATING_DESC, perPage: 10) {
      edges {
        node {
          rating
          mediaRecommendation {
            id
            title { romaji english }
            format
            averageScore
          }
        }
      }
    }
    externalLinks {
      url
      site
      type
      language
    }
    streamingEpisodes {
      title
      url
      site
    }
    rankings {
      rank
      type
      format
      year
      season
      allTime
      context
    }
    stats {
      scoreDistribution { score amount }
      statusDistribution { status amount }
    }
    coverImage { extraLarge large medium color }
    bannerImage
    siteUrl
  }
}`;

const SEARCH_CHARACTER_QUERY = `
query ($search: String) {
  Page(perPage: 5) {
    characters(search: $search, sort: SEARCH_MATCH) {
      id
      name { full native alternative }
      image { medium }
      favourites
      media(perPage: 3) {
        edges {
          characterRole
          node {
            id
            title { romaji english }
            type
            format
          }
        }
      }
    }
  }
}`;

const FULL_CHARACTER_QUERY = `
query ($id: Int) {
  Character(id: $id) {
    id
    name { first middle last full native alternative alternativeSpoiler }
    image { large medium }
    description(asHtml: false)
    gender
    dateOfBirth { year month day }
    age
    bloodType
    favourites
    media(sort: POPULARITY_DESC, perPage: 25) {
      edges {
        characterRole
        voiceActors {
          name { full native }
          languageV2
          image { medium }
        }
        node {
          id
          title { romaji english native }
          type
          format
          status
          seasonYear
          coverImage { medium }
          siteUrl
        }
      }
    }
    siteUrl
  }
}`;

// ═══════════════════════════════════════════════════════════════════════════
// EXPORTED FUNCTIONS
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Search for anime or manga by title.
 * @param {string} query - Search text
 * @param {"ANIME"|"MANGA"} type - Media type
 * @returns {Promise<{results: object[], error?: string}>}
 */
export async function searchMedia(query, type) {
  const result = await fetchGraphQL(ENDPOINT, SEARCH_MEDIA_QUERY, { search: query, type });

  if (!result.ok) return { results: [], error: result.error };

  const media = result.data?.Page?.media || [];
  return { results: media };
}

/**
 * Get complete media details by AniList ID.
 * @param {number} id - AniList media ID
 * @returns {Promise<{media: object|null, error?: string}>}
 */
export async function getMediaFull(id) {
  const result = await fetchGraphQL(ENDPOINT, FULL_MEDIA_QUERY, { id });

  if (!result.ok) return { media: null, error: result.error };

  return { media: result.data?.Media || null };
}

/**
 * Search for characters by name.
 * @param {string} query - Search text
 * @returns {Promise<{results: object[], error?: string}>}
 */
export async function searchCharacter(query) {
  const result = await fetchGraphQL(ENDPOINT, SEARCH_CHARACTER_QUERY, { search: query });

  if (!result.ok) return { results: [], error: result.error };

  const characters = result.data?.Page?.characters || [];
  return { results: characters };
}

/**
 * Get complete character details by AniList ID.
 * @param {number} id - AniList character ID
 * @returns {Promise<{character: object|null, error?: string}>}
 */
export async function getCharacterFull(id) {
  const result = await fetchGraphQL(ENDPOINT, FULL_CHARACTER_QUERY, { id });

  if (!result.ok) return { character: null, error: result.error };

  return { character: result.data?.Character || null };
}
