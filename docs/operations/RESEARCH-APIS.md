# Research API Reference

> **Purpose**: Documents all external APIs available to the MCP research tool, their capabilities, authentication requirements, rate limits, and the research domains they serve. This is the design input for the structured API integration in `.claude/mcp/helpers/research.mjs`.

---

## Architecture Overview

The research tool operates at the **intent level**, not the API level. The agent says "tell me about this anime" and the MCP server:

1. Determines which APIs serve the `anime` domain (AniList + Jikan)
2. Searches for the subject across both APIs in parallel
3. Fires all detail queries simultaneously once IDs are resolved
4. Compiles results into a single structured markdown document
5. Writes to `/tmp/bannou-research-{hash}.md`
6. Adds to the required reading gate (agent must full-read before continuing)

The agent never constructs queries, handles pagination, or parses API responses.

### Research Domains

| Domain | Primary API | Secondary API(s) | Auth Required | Description |
|--------|-------------|-------------------|---------------|-------------|
| `anime` | AniList | Jikan (MAL) | None | Anime series/movies — metadata, characters, staff, studios, genres, tags, relations, recommendations, reviews, stats |
| `manga` | AniList | Jikan (MAL) | None | Manga — same breadth as anime, plus volumes/chapters |
| `character` | AniList | Jikan, VNDB | None | Characters across anime/manga/VN — appearances, voice actors, descriptions, traits |
| `visual-novel` | VNDB | — | None | Visual novels — details, characters with traits, staff, releases, tags, relations |
| `light-novel` | RanobeDB | AniList (type: MANGA, format: NOVEL) | None | Light novels — books, series, publishers, staff, translations, volumes |
| `music-artist` | MusicBrainz | Last.fm | Last.fm: API key | Artists — discography, tags/genres, similar artists, relationships, top tracks |
| `music-track` | MusicBrainz | Last.fm | Last.fm: API key | Tracks — recording info, releases, tags, similar tracks, artist connections |
| `movie` | TMDB | — | TMDB: API key | Movies — details, cast, crew, genres, keywords, similar titles |
| `tv` | TMDB | — | TMDB: API key | TV shows — details, seasons, episodes, cast, crew, genres, similar |
| `game` | IGDB | — | Twitch: OAuth | Games — details, characters, themes, genres, platforms, similar games |

### Authentication Summary

| API | Auth Type | How to Obtain | Stored As |
|-----|-----------|---------------|-----------|
| AniList | None (public reads) | — | — |
| Jikan | None | — | — |
| VNDB | None (public reads) | — | — |
| RanobeDB | None | — | — |
| MusicBrainz | None (User-Agent required) | Set custom UA string | Hardcoded |
| Last.fm | API Key | https://www.last.fm/api/account/create | `LASTFM_API_KEY` env var |
| TMDB | API Key (Bearer token) | https://www.themoviedb.org/settings/api | `TMDB_API_KEY` env var |
| IGDB | Twitch OAuth (client credentials) | https://dev.twitch.tv/console/apps | `TWITCH_CLIENT_ID` + `TWITCH_CLIENT_SECRET` env vars |

**Tier 1 (no auth, implement first)**: AniList, Jikan, VNDB, RanobeDB, MusicBrainz
**Tier 2 (API key needed)**: Last.fm, TMDB
**Tier 3 (OAuth needed)**: IGDB

---

## API Details

### 1. AniList

**Endpoint**: `POST https://graphql.anilist.co`
**Protocol**: GraphQL
**Auth**: None for public reads
**Rate Limit**: 90 requests/minute, burst limiter
**Response Format**: JSON

#### Capabilities

**Root Queries** (single item by ID or search):
- `Media` — anime or manga entry
- `Character` — character across all media
- `Staff` — production staff / voice actors
- `Studio` — animation studios
- `MediaTrend` — popularity/score trends over time
- `AiringSchedule` — upcoming episode schedule
- `Page` — paginated collection of any of the above

**Media Fields** (the core entity — covers both anime and manga):
- **Identity**: id, idMal, title (romaji, english, native, userPreferred), type (ANIME/MANGA), format (TV, MOVIE, OVA, ONA, SPECIAL, TV_SHORT, MUSIC, MANGA, NOVEL, ONE_SHOT)
- **Status**: status (FINISHED, RELEASING, NOT_YET_RELEASED, CANCELLED, HIATUS), season, seasonYear, startDate, endDate
- **Content**: description (supports HTML and markdown), episodes, chapters, volumes, duration, countryOfOrigin, isLicensed, source (ORIGINAL, MANGA, LIGHT_NOVEL, VISUAL_NOVEL, VIDEO_GAME, OTHER, NOVEL, DOUJINSHI, ANIME, WEB_NOVEL, LIVE_ACTION, GAME, COMIC, MULTIMEDIA_PROJECT, PICTURE_BOOK)
- **Classification**: genres (string array), tags (array with name, description, category, rank, isMediaSpoiler, isGeneralSpoiler), isAdult
- **Scores**: averageScore (0-100), meanScore, popularity, favourites, trending
- **Rankings**: rankings (array with rank, type, format, year, season, allTime, context)
- **Relations**: relations (array of Media with relationType: ADAPTATION, PREQUEL, SEQUEL, PARENT, SIDE_STORY, CHARACTER, SUMMARY, ALTERNATIVE, SPIN_OFF, OTHER, SOURCE, COMPILATION, CONTAINS)
- **Characters**: characters (array with role [MAIN, SUPPORTING, BACKGROUND], name, image, description, voiceActors with languageV2)
- **Staff**: staff (array with role description, name, image, primaryOccupations, languageV2)
- **Studios**: studios (array with name, isAnimationStudio, siteUrl)
- **Recommendations**: recommendations (array with rating, mediaRecommendation)
- **External Links**: externalLinks (array with url, site, type, language, color, icon)
- **Streaming**: streamingEpisodes (array with title, thumbnail, url, site)
- **Images**: coverImage (extraLarge, large, medium, color), bannerImage
- **Stats**: stats.scoreDistribution, stats.statusDistribution, trends

**Character Fields**:
- id, name (first, middle, last, full, native, alternative, alternativeSpoiler), image (large, medium), description, gender, dateOfBirth, age, bloodType, favourites
- media (array of Media with characterRole)

**Staff Fields**:
- id, name (first, middle, last, full, native), image, description, primaryOccupations, gender, dateOfBirth, dateOfDeath, age, yearsActive, homeTown, bloodType, languageV2
- staffMedia (array of Media with staffRole)
- characters (array of Character with characterRole)

**Filtering & Sorting**:
- Media search by: id, idMal, startDate, endDate, season, seasonYear, type, format, status, episodes, duration, chapters, volumes, isAdult, genre, tag, minimumTagRank, tagCategory, onList, licensedBy, licensedById, averageScore, popularity, source, countryOfOrigin, isLicensed, search (text), and `_not`, `_in`, `_not_in`, `_greater`, `_lesser`, `_like` variants on most fields
- MediaSort options: ID, TITLE_ROMAJI, TITLE_ENGLISH, TITLE_NATIVE, TYPE, FORMAT, START_DATE, END_DATE, SCORE, POPULARITY, TRENDING, EPISODES, DURATION, STATUS, CHAPTERS, VOLUMES, UPDATED_AT, SEARCH_MATCH, FAVOURITES
- Character/Staff search by: id, isBirthday, search, id_not, id_in, id_not_in, sort

#### What AniList Is Best For
- **Comprehensive metadata**: Tags with granular categories and spoiler levels, detailed genre classification
- **Relation graphs**: Full adaptation/sequel/prequel/spin-off chains
- **Character-voice actor mapping**: Which VA voiced which character in which language
- **Scoring and ranking**: Granular score distributions, format-specific rankings
- **Single query depth**: GraphQL allows fetching media + characters + staff + studios + relations in one request

---

### 2. Jikan (Unofficial MAL API)

**Base URL**: `https://api.jikan.moe/v4`
**Protocol**: REST (GET only)
**Auth**: None
**Rate Limit**: 60 requests/minute, 3 requests/second
**Response Format**: JSON
**Caching**: All responses cached 24 hours

#### Endpoints

**Anime** (`/anime`):
- `GET /anime/{id}/full` — Complete anime details (all sub-resources in one call)
- `GET /anime/{id}` — Basic anime details
- `GET /anime/{id}/characters` — Character list with roles and voice actors
- `GET /anime/{id}/staff` — Staff list with positions
- `GET /anime/{id}/episodes` — Episode list (paginated)
- `GET /anime/{id}/news` — Related news articles
- `GET /anime/{id}/forum` — Forum discussions
- `GET /anime/{id}/videos` — Promotional videos
- `GET /anime/{id}/pictures` — Pictures/images
- `GET /anime/{id}/statistics` — User stats (watching, completed, dropped, etc.)
- `GET /anime/{id}/recommendations` — User recommendations
- `GET /anime/{id}/reviews` — User reviews
- `GET /anime/{id}/relations` — Related anime/manga
- `GET /anime/{id}/themes` — Opening/ending theme songs
- `GET /anime/{id}/external` — External links
- `GET /anime/{id}/streaming` — Streaming links
- `GET /anime` — Search/filter anime

**Manga** (`/manga`): Same pattern with chapters, volumes, serialization fields.

**Characters** (`/characters`):
- `GET /characters/{id}/full` — Full character details
- `GET /characters/{id}/anime` — Anime appearances
- `GET /characters/{id}/manga` — Manga appearances
- `GET /characters/{id}/voices` — Voice actors across languages
- `GET /characters/{id}/pictures` — Character images
- `GET /characters` — Search characters

**People** (`/people`):
- `GET /people/{id}/full` — Full person details
- `GET /people/{id}/anime` — Anime roles
- `GET /people/{id}/voices` — Voice acting roles
- `GET /people/{id}/manga` — Manga roles
- `GET /people` — Search people

**Additional Resources**:
- `GET /genres/anime`, `GET /genres/manga` — Genre listings
- `GET /producers` — Producer/studio listings
- `GET /schedules` — Weekly airing schedule
- `GET /seasons/now`, `GET /seasons/{year}/{season}`, `GET /seasons/upcoming` — Seasonal anime
- `GET /top/anime`, `GET /top/manga`, `GET /top/characters`, `GET /top/people` — Top lists
- `GET /recommendations/anime`, `GET /recommendations/manga` — Recent recommendations
- `GET /reviews/anime`, `GET /reviews/manga` — Recent reviews
- `GET /random/anime`, `GET /random/manga`, `GET /random/characters` — Random entries

**Search/Filter Parameters** (for `/anime`, `/manga`, etc.):
- `q` (search string), `page`, `limit` (max 25), `order_by`, `sort` (asc/desc)
- `type`, `score`, `min_score`, `max_score`, `status`, `rating`, `sfw`
- `genres`, `genres_exclude` (comma-separated IDs)
- `start_date`, `end_date` (YYYY-MM-DD)

#### What Jikan Is Best For
- **MAL scores and statistics**: The de facto community scoring source
- **Theme songs**: Opening/ending song lists (not available from AniList)
- **User reviews**: Full review text with scores
- **Episode details**: Per-episode metadata and titles
- **Community statistics**: Watching/completed/dropped/plan-to-watch breakdowns

---

### 3. VNDB

**Endpoint**: `POST https://api.vndb.org/kana`
**Protocol**: REST with POST-body JSON queries
**Auth**: None for public reads; token for user list operations
**Rate Limit**: 200 requests / 5 minutes; 1 second execution time per minute
**Response Format**: JSON

#### Query Structure

All queries use POST with JSON body:
```json
{
  "filters": ["and", ["search", "=", "query"], ["lang", "=", "en"]],
  "fields": "id, title, description, rating, tags{name, rating, spoiler}",
  "sort": "rating",
  "reverse": true,
  "results": 10,
  "page": 1
}
```

Filters: 3-element arrays `["field", "operator", "value"]`
Operators: `=`, `!=`, `>=`, `>`, `<=`, `<`
Logical: `["and", ...]`, `["or", ...]`
Nested: `["character", "=", ["search", "=", "Saber"]]`

#### Queryable Entities

**POST /vn** — Visual novels (id, title, alttitle, titles, aliases, olang, devstatus, released, description, languages, platforms, length, length_minutes, average, rating, votecount; nested: image, relations, tags, developers, editions, staff, va, extlinks)

**POST /character** — Characters (id, name, original, aliases, description, blood_type, height, weight, bust/waist/hips, cup, age, birthday, sex, gender; nested: image, vns with role/spoiler, traits with spoiler)

**POST /staff** — Staff/VAs (id, aid, ismain, name, original, lang, gender, description, aliases, extlinks)

**POST /release** — Releases (id, title, released, minage, patch, freeware, uncensored, official, has_ero, languages, platforms, media, vns, producers, images, resolution, engine, voiced, extlinks)

**POST /producer** — Developers/publishers (id, name, original, lang, type, description, aliases, extlinks)

**POST /tag** — Content tags (id, name, aliases, description, category, vn_count)

**POST /trait** — Character traits (id, name, aliases, description, sexual, group_id, group_name, char_count)

**POST /quote** — Character quotes (id, quote, score; nested vn/character fields)

**Utility**: `GET /schema` (enums, fields), `GET /stats` (database counts)

#### What VNDB Is Best For
- **Visual novel metadata**: The definitive VN database (50,000+ entries)
- **Character detail depth**: Physical attributes, personality traits with spoiler levels
- **Tag system**: Hierarchical tags with spoiler levels and user ratings
- **Nested queries**: Filter VNs by character traits, or characters by VN tags
- **Release tracking**: Multi-language, multi-platform with censorship/voice status

---

### 4. RanobeDB

**Base URL**: `https://ranobedb.org/api`
**Protocol**: REST (GET with URL params)
**Auth**: None
**Rate Limit**: Not documented (v0 API)
**Response Format**: JSON
**Status**: v0 — in development

#### Endpoints

- `GET /books` — Search books
- `GET /series` — Search series
- `GET /staff` — Search staff
- `GET /publishers` — Search publishers

**Parameters**: `q` (query), `rl` (release language), `rf` (release format), `sort`, `staff`, `p` (page)

**Data**: Open Database License. Non-commercial use only.

#### What RanobeDB Is Best For
- **Light novel metadata**: Series info, individual volumes, publication details
- **Translation tracking**: Which volumes translated, in which formats
- **Publisher/staff data**: Authors, illustrators, translators, Japanese and English publishers

---

### 5. MusicBrainz

**Base URL**: `https://musicbrainz.org/ws/2/`
**Protocol**: REST
**Auth**: None (User-Agent header REQUIRED)
**Rate Limit**: 1 request/second (IP blocks if exceeded)
**Response Format**: XML default, JSON via `fmt=json` or Accept header

#### Entity Types (13 core)
area, artist, event, genre, instrument, label, place, recording, release, release-group, series, work, url

#### Request Types

**Lookup**: `GET /<entity>/<MBID>?inc=<INC>` — by MusicBrainz ID
**Browse**: `GET /<entity>?<linked_entity>=<MBID>&limit=<N>&offset=<N>&inc=<INC>` — linked entities
**Search**: `GET /<entity>?query=<QUERY>&limit=<N>&offset=<N>` — Lucene full-text

#### Include Parameters
- **Artist**: recordings, releases, release-groups, works
- **Recording**: releases, release-groups
- **Release**: collections, labels, recordings, release-groups
- **Universal**: aliases, annotation, tags, ratings, genres
- **Relationships**: area-rels, artist-rels, recording-rels, release-rels, url-rels, work-rels (+ recording-level-rels, work-level-rels)

#### What MusicBrainz Is Best For
- **Definitive music metadata**: ~30M recordings, 2M artists
- **Relationship graphs**: Collaborations, cover versions, samples, remixes, credits
- **Cross-reference IDs**: Links to Discogs, Spotify, Apple Music, YouTube, Wikidata
- **Genre/tag data**: Community-driven classifications

---

### 6. Last.fm

**Base URL**: `https://ws.audioscrobbler.com/2.0/`
**Protocol**: REST (GET with `method` parameter)
**Auth**: API Key required (`api_key` parameter)
**Rate Limit**: Not formally documented
**Response Format**: JSON via `format=json`

#### Key Read Methods
- `artist.getInfo`, `artist.getSimilar`, `artist.getTopTags`, `artist.getTopAlbums`, `artist.getTopTracks`, `artist.search`
- `track.getInfo`, `track.getSimilar`, `track.getTopTags`, `track.search`
- `album.getInfo`, `album.getTopTags`, `album.search`
- `tag.getInfo`, `tag.getSimilar`, `tag.getTopArtists`, `tag.getTopTracks`, `tag.getTopTags`
- `chart.getTopArtists`, `chart.getTopTags`, `chart.getTopTracks`

#### What Last.fm Is Best For
- **Social tagging**: Community-driven genres, moods, themes, eras
- **Similar artist/track discovery**: Match scores
- **Play count data**: Popularity from actual listening behavior

---

### 7. TMDB

**Base URL**: `https://api.themoviedb.org/3`
**Protocol**: REST
**Auth**: Bearer token in Authorization header
**Rate Limit**: ~40 requests / 10 seconds
**Response Format**: JSON

#### Key Endpoints
- **Search**: `/search/movie`, `/search/tv`, `/search/person`, `/search/multi`
- **Discover**: `/discover/movie`, `/discover/tv` (filter by genres, dates, scores, etc.)
- **Find**: `/find/{external_id}` (lookup by IMDB, TVDB, Wikidata)
- **Movie**: `/movie/{id}` + `/credits`, `/keywords`, `/similar`, `/recommendations`, `/reviews`, `/external_ids`, `/watch/providers`
- **TV**: `/tv/{id}` + same sub-resources + `/season/{n}`, `/season/{n}/episode/{n}`, `/aggregate_credits`
- **Person**: `/person/{id}` + `/combined_credits`, `/external_ids`
- **append_to_response**: Combine multiple sub-resources in ONE request

#### What TMDB Is Best For
- **Movie/TV metadata**: Most comprehensive open movie/TV database
- **Cast/crew data**: Full credits with character names
- **Streaming availability**: By region
- **append_to_response**: Massive efficiency — one request for everything

---

### 8. IGDB

**Base URL**: `https://api.igdb.com/v4`
**Protocol**: REST (POST with Apicalypse body queries)
**Auth**: Twitch OAuth (Client Credentials)
**Rate Limit**: 4 requests/second

#### Auth Flow
1. Register at https://dev.twitch.tv/console/apps
2. `POST https://id.twitch.tv/oauth2/token?client_id={id}&client_secret={secret}&grant_type=client_credentials`
3. Headers: `Client-ID: {id}`, `Authorization: Bearer {token}`

#### Key Endpoints
- `POST /games`, `/characters`, `/companies`, `/franchises`, `/genres`, `/themes`, `/platforms`, `/game_modes`, `/player_perspectives`, `/keywords`, `/involved_companies`, `/release_dates`, `/similar_games`, `/search`

#### What IGDB Is Best For
- **Game metadata**: 300,000+ entries
- **Thematic classification**: Themes + genres + keywords
- **Franchise tracking**: Series and collections

---

### 9. Spotify Web API ⚠️

**Status**: Audio Features and Audio Analysis endpoints **deprecated for new applications** as of November 27, 2024.

**What was available**: Danceability, Energy, Valence, Instrumentalness, Acousticness, Speechiness, Liveness, Tempo, Key, Mode, Time Signature, Loudness; plus detailed beat/section/segment analysis.

**Implication**: Cannot use for new integrations. Use Last.fm tags + MusicBrainz genres as alternatives.

---

## Research Domain → API Call Mapping

### `anime` Domain
1. **AniList**: Search Media (type: ANIME) → full details with characters, staff, studios, relations, recommendations, tags, stats, streaming, externalLinks
2. **Jikan**: Search → `/anime/{id}/full` + `/anime/{id}/themes` + `/anime/{id}/reviews`

**Output Sections**: Basic Info, Synopsis, Genres & Tags, Characters & VAs, Staff, Relations, Recommendations, Scores & Rankings (AniList + MAL), Community Stats, Theme Songs, Streaming, External Links

### `manga` Domain
Same as anime with manga-specific fields (chapters, volumes, serialization).

### `character` Domain
1. **AniList**: Search Character → appearances, VAs, description
2. **Jikan**: Search → appearances, VAs, pictures
3. **VNDB** (if VN character): Search → traits, physical attributes, VN appearances

**Output Sections**: Character Info, Description, Media Appearances, Voice Actors, Physical Attributes, Personality Traits, Images

### `visual-novel` Domain
1. **VNDB**: Search VN → full details with characters, staff, tags, relations, releases, extlinks

**Output Sections**: Basic Info, Description, Tags, Characters, Staff, Relations, Releases, Developer, External Links, Rating

### `light-novel` Domain
1. **RanobeDB**: Search → volumes, publishers, staff, translations
2. **AniList**: Search Media (type: MANGA, format: NOVEL) → genres, tags, scores, relations

**Output Sections**: Series Info, Synopsis, Publication Details, Volume List, Translation Status, Staff, Genres & Tags, Scores, Adaptations, External Links

### `music-artist` Domain
1. **MusicBrainz**: Search artist → lookup with releases, release-groups, genres, url-rels, artist-rels
2. **Last.fm**: artist.getInfo + getSimilar + getTopTags + getTopTracks + getTopAlbums

**Output Sections**: Artist Info, Biography, Genres & Tags, Discography, Top Tracks, Similar Artists, Relationships, External Links, Listening Stats

### `music-track` Domain
1. **MusicBrainz**: Search recording → lookup with releases, artist-credits, work-rels, url-rels
2. **Last.fm**: track.getInfo + getSimilar + getTopTags

**Output Sections**: Track Info, Appears On, Tags, Similar Tracks, Credits, Wiki/Description, External Links

### `movie` Domain
1. **TMDB**: Search → fetch with `append_to_response=credits,keywords,similar,recommendations,external_ids,watch/providers,reviews`

**Output Sections**: Basic Info, Cast, Crew, Keywords, Similar & Recommendations, Scores, Streaming, External IDs, Budget & Revenue

### `tv` Domain
Same as movie with seasons, episodes, networks, episode runtime.

### `game` Domain
1. **IGDB**: Search → fetch with genres, themes, platforms, companies, similar_games, keywords, modes, perspectives, release_dates, storyline, summary, ratings

**Output Sections**: Basic Info, Release Dates, Companies, Modes & Perspectives, Keywords & Themes, Similar Games, Ratings, Franchise, Media, External Links

---

## Implementation Notes

### Parallel Execution Pattern
1. Fire search queries to resolve entity IDs (1-2 API calls)
2. Once IDs known, fire ALL detail queries in parallel via `Promise.all()`
3. Compile results as each resolves
4. Write final markdown to `/tmp/bannou-research-{hash}.md`
5. Add to required reading gate

### Error Handling
- Secondary API failure: include primary results + failure note
- Primary API failure: return error
- Rate limits: respect `Retry-After`; enforce 1 req/sec for MusicBrainz

### Document Template
```markdown
# Research: {subject}
> Domain: {domain} | Sources: {api1}, {api2} | Generated: {timestamp}

## Summary
{One-paragraph overview}

## {Section 1}
...

## Sources
- AniList: {url}
- MAL: {url}
```

### Environment Variables
```bash
# Tier 1 — no config needed (AniList, Jikan, VNDB, RanobeDB, MusicBrainz)

# Tier 2 — API keys
LASTFM_API_KEY=...
TMDB_API_KEY=...

# Tier 3 — OAuth
TWITCH_CLIENT_ID=...
TWITCH_CLIENT_SECRET=...
```

Missing API keys → corresponding API skipped with note in output. Tier 1 always works.
