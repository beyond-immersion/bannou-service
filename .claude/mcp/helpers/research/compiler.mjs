/**
 * Research Document Compiler
 *
 * Transforms raw API responses into structured markdown research documents.
 * Writes to /tmp/ and integrates with the MCP required-reading gate so the
 * agent must full-read the document before continuing.
 *
 * Each domain has a compile function that receives normalized API data and
 * produces markdown sections. The shared compile() function handles the
 * document skeleton, file writing, and gate registration.
 */

import { writeFile } from "node:fs/promises";
import { join } from "node:path";
import { tmpdir } from "node:os";
import { createHash } from "node:crypto";
import { requiredReading } from "../../state.mjs";

// ─── File Writing & Gate ────────────────────────────────────────────────

function makeResearchPath(domain, query) {
  const hash = createHash("md5").update(`${domain}:${query}`).digest("hex").slice(0, 10);
  return join(tmpdir(), `bannou-research-${hash}.md`);
}

/**
 * Write a research document to /tmp and add to required reading gate.
 *
 * @param {string} domain - Research domain (anime, manga, etc.)
 * @param {string} query - Original search query
 * @param {string} markdown - Compiled markdown content
 * @returns {{ path: string, charCount: number }}
 */
async function writeResearchDocument(domain, query, markdown) {
  const path = makeResearchPath(domain, query);
  await writeFile(path, markdown, "utf-8");
  requiredReading.add(path);
  return { path, charCount: markdown.length };
}

// ─── Helpers ────────────────────────────────────────────────────────────

function formatDate(dateObj) {
  if (!dateObj) return "—";
  const { year, month, day } = dateObj;
  if (!year) return "—";
  if (!month) return `${year}`;
  if (!day) return `${year}-${String(month).padStart(2, "0")}`;
  return `${year}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
}

function formatSeason(season, year) {
  if (!season || !year) return "—";
  return `${season.charAt(0)}${season.slice(1).toLowerCase()} ${year}`;
}

function escapeMarkdown(text) {
  if (!text) return "";
  return text.replace(/\|/g, "\\|").replace(/\n/g, " ");
}

function truncate(text, maxLen = 300) {
  if (!text) return "—";
  const cleaned = text.replace(/<br\s*\/?>/gi, " ").replace(/<[^>]+>/g, "").replace(/\s+/g, " ").trim();
  if (cleaned.length <= maxLen) return cleaned;
  return cleaned.slice(0, maxLen) + "…";
}

function stripHtml(text) {
  if (!text) return "";
  return text.replace(/<br\s*\/?>/gi, "\n").replace(/<[^>]+>/g, "").trim();
}

// ─── Anime Compiler ─────────────────────────────────────────────────────

function compileAnime(query, data) {
  const { anilist, jikan, jikanThemes } = data;
  const m = anilist; // main media object
  if (!m) return "No data available from AniList.";

  const sections = [];

  // ── Header ──
  const title = m.title?.english || m.title?.romaji || query;
  const sources = ["AniList", jikan ? "MyAnimeList" : null].filter(Boolean).join(", ");
  sections.push(`# Research: ${title}`);
  sections.push(`> Domain: anime | Sources: ${sources} | Generated: ${new Date().toISOString()}`);
  sections.push("");

  // ── Summary ──
  const studioList = (m.studios?.edges || [])
    .filter((e) => e.isMain || e.node?.isAnimationStudio)
    .map((e) => e.node?.name)
    .filter(Boolean);
  const malScore = jikan?.score ? `${jikan.score}/10 on MAL` : null;
  const alScore = m.averageScore ? `${m.averageScore}/100 on AniList` : null;
  const scores = [alScore, malScore].filter(Boolean).join(", ");
  const memberCount = jikan?.members ? ` with ${jikan.members.toLocaleString()} MAL members` : "";

  sections.push("## Summary");
  sections.push(
    `${title} is a${m.isAdult ? "n adult" : ""} ${(m.format || "").replace(/_/g, " ")} anime` +
    (m.seasonYear ? ` (${formatSeason(m.season, m.seasonYear)}` +
      (m.episodes ? `, ${m.episodes} episode${m.episodes > 1 ? "s" : ""}` : "") + ")" : "") +
    (studioList.length ? ` produced by ${studioList.join(", ")}` : "") +
    `. ${m.source ? `Adapted from ${m.source.replace(/_/g, " ").toLowerCase()}.` : ""}` +
    (scores ? ` Scored ${scores}${memberCount}.` : "")
  );
  sections.push("");

  // ── Basic Info ──
  sections.push("## Basic Info");
  sections.push("| Field | Value |");
  sections.push("|-------|-------|");
  if (m.title?.romaji) sections.push(`| Title (Romaji) | ${m.title.romaji} |`);
  if (m.title?.english) sections.push(`| Title (English) | ${m.title.english} |`);
  if (m.title?.native) sections.push(`| Title (Native) | ${m.title.native} |`);
  sections.push(`| Format | ${(m.format || "—").replace(/_/g, " ")} |`);
  if (m.episodes) sections.push(`| Episodes | ${m.episodes} |`);
  if (m.duration) sections.push(`| Duration | ${m.duration} min/ep |`);
  sections.push(`| Status | ${(m.status || "—").replace(/_/g, " ")} |`);
  if (m.season) sections.push(`| Season | ${formatSeason(m.season, m.seasonYear)} |`);
  sections.push(`| Aired | ${formatDate(m.startDate)} → ${formatDate(m.endDate)} |`);
  if (m.source) sections.push(`| Source | ${m.source.replace(/_/g, " ")} |`);
  if (studioList.length) sections.push(`| Studio(s) | ${studioList.join(", ")} |`);
  sections.push(`| Country | ${m.countryOfOrigin || "—"} |`);
  if (m.isAdult) sections.push("| Adult | Yes |");
  sections.push("");

  // ── Synopsis ──
  if (m.description) {
    sections.push("## Synopsis");
    sections.push(stripHtml(m.description));
    sections.push("");
  }

  // ── Genres & Tags ──
  sections.push("## Genres & Tags");
  if (m.genres?.length) sections.push(`**Genres**: ${m.genres.join(", ")}`);
  const tags = (m.tags || [])
    .filter((t) => !t.isGeneralSpoiler && !t.isMediaSpoiler)
    .sort((a, b) => (b.rank || 0) - (a.rank || 0))
    .slice(0, 15);
  if (tags.length) {
    sections.push("**Tags**:");
    for (const t of tags) {
      sections.push(`- ${t.name} (${t.rank}% relevance)${t.description ? ` — ${truncate(t.description, 80)}` : ""}`);
    }
  }
  const spoilerTags = (m.tags || []).filter((t) => t.isMediaSpoiler);
  if (spoilerTags.length) {
    sections.push(`\n*${spoilerTags.length} spoiler tag(s) hidden.*`);
  }
  sections.push("");

  // ── Characters & Voice Actors ──
  const chars = m.characters?.edges || [];
  if (chars.length) {
    sections.push("## Characters & Voice Actors");
    sections.push("| Character | Role | VA (Japanese) |");
    sections.push("|-----------|------|---------------|");
    for (const edge of chars) {
      const name = edge.node?.name?.full || "—";
      const role = (edge.role || "—").charAt(0) + (edge.role || "").slice(1).toLowerCase();
      // Get Japanese VA from voiceActors array or voiceActorRoles
      let va = "—";
      if (edge.voiceActors?.length) {
        va = edge.voiceActors[0]?.name?.full || "—";
      } else if (edge.voiceActorRoles?.length) {
        const jpRole = edge.voiceActorRoles.find((r) => r.voiceActor?.languageV2 === "Japanese");
        if (jpRole) va = jpRole.voiceActor?.name?.full || "—";
      }
      sections.push(`| ${escapeMarkdown(name)} | ${role} | ${escapeMarkdown(va)} |`);
    }
    sections.push("");
  }

  // ── Staff ──
  const staffEdges = m.staff?.edges || [];
  if (staffEdges.length) {
    sections.push("## Staff");
    sections.push("| Role | Name |");
    sections.push("|------|------|");
    for (const edge of staffEdges) {
      sections.push(`| ${escapeMarkdown(edge.role || "—")} | ${escapeMarkdown(edge.node?.name?.full || "—")} |`);
    }
    sections.push("");
  }

  // ── Relations ──
  const rels = m.relations?.edges || [];
  if (rels.length) {
    sections.push("## Relations");
    sections.push("| Type | Title | Format |");
    sections.push("|------|-------|--------|");
    for (const edge of rels) {
      const relType = (edge.relationType || "—").replace(/_/g, " ");
      const relTitle = edge.node?.title?.english || edge.node?.title?.romaji || "—";
      const relFormat = (edge.node?.format || "—").replace(/_/g, " ");
      sections.push(`| ${relType} | ${escapeMarkdown(relTitle)} | ${relFormat} |`);
    }
    sections.push("");
  }

  // ── Recommendations ──
  const recs = (m.recommendations?.edges || []).filter((e) => e.node?.mediaRecommendation);
  if (recs.length) {
    sections.push("## Recommendations");
    for (const edge of recs) {
      const rec = edge.node.mediaRecommendation;
      const recTitle = rec.title?.english || rec.title?.romaji || "—";
      const score = rec.averageScore ? ` (${rec.averageScore}/100)` : "";
      sections.push(`- ${recTitle}${score}`);
    }
    sections.push("");
  }

  // ── Scores & Rankings ──
  sections.push("## Scores & Rankings");
  sections.push("| Source | Score | Popularity |");
  sections.push("|--------|-------|------------|");
  if (m.averageScore) sections.push(`| AniList | ${m.averageScore}/100 | #${m.popularity || "—"} (${(m.favourites || 0).toLocaleString()} favs) |`);
  if (jikan?.score) sections.push(`| MAL | ${jikan.score}/10 | #${jikan.popularity || "—"} (${(jikan.members || 0).toLocaleString()} members) |`);
  sections.push("");

  // Rankings detail
  const rankings = (m.rankings || []).slice(0, 5);
  if (rankings.length) {
    for (const r of rankings) {
      const ctx = r.context || "";
      const scope = r.allTime ? "all time" : r.year ? `${r.year}` : r.season ? `${r.season} ${r.year}` : "";
      sections.push(`- **#${r.rank}** ${ctx}${scope ? ` (${scope})` : ""}`);
    }
    sections.push("");
  }

  // ── Community Stats (MAL) ──
  if (jikan?.statistics) {
    const stats = jikan.statistics;
    sections.push("## Community Stats (MAL)");
    const statLine = [
      stats.watching ? `Watching: ${stats.watching.toLocaleString()}` : null,
      stats.completed ? `Completed: ${stats.completed.toLocaleString()}` : null,
      stats.on_hold ? `On Hold: ${stats.on_hold.toLocaleString()}` : null,
      stats.dropped ? `Dropped: ${stats.dropped.toLocaleString()}` : null,
      stats.plan_to_watch ? `Plan to Watch: ${stats.plan_to_watch.toLocaleString()}` : null,
    ].filter(Boolean).join(" | ");
    if (statLine) sections.push(statLine);
    sections.push("");
  }

  // ── Theme Songs ──
  const themes = jikanThemes;
  if (themes && (themes.openings?.length || themes.endings?.length)) {
    sections.push("## Theme Songs");
    if (themes.openings?.length) {
      sections.push("**Opening**:");
      for (const op of themes.openings) sections.push(`- ${op}`);
    }
    if (themes.endings?.length) {
      sections.push("**Ending**:");
      for (const ed of themes.endings) sections.push(`- ${ed}`);
    }
    sections.push("");
  }

  // ── Streaming ──
  const streams = m.streamingEpisodes || [];
  const streamSites = [...new Set(streams.map((s) => s.site).filter(Boolean))];
  if (streamSites.length) {
    sections.push("## Streaming");
    for (const site of streamSites) sections.push(`- ${site}`);
    sections.push("");
  }

  // ── External Links ──
  const links = m.externalLinks || [];
  if (links.length || m.siteUrl) {
    sections.push("## External Links");
    if (m.siteUrl) sections.push(`- AniList: ${m.siteUrl}`);
    if (jikan?.url) sections.push(`- MAL: ${jikan.url}`);
    for (const link of links) {
      if (link.url && link.site) sections.push(`- ${link.site}: ${link.url}`);
    }
    sections.push("");
  }

  // ── Score Distribution ──
  const scoreDist = m.stats?.scoreDistribution;
  if (scoreDist?.length) {
    sections.push("## Score Distribution (AniList)");
    for (const s of scoreDist) {
      const bar = "█".repeat(Math.round((s.amount / Math.max(...scoreDist.map((x) => x.amount))) * 20));
      sections.push(`${String(s.score).padStart(3)}: ${bar} ${s.amount.toLocaleString()}`);
    }
    sections.push("");
  }

  // ── Sources footer ──
  sections.push("---");
  sections.push("*Data compiled from AniList API" + (jikan ? " and Jikan (MyAnimeList) API" : "") + ".*");

  return sections.join("\n");
}

// ─── Visual Novel Compiler ──────────────────────────────────────────────

function stripVndbFormatting(text) {
  if (!text) return "";
  // VNDB uses [url=...]text[/url], [spoiler]...[/spoiler], etc.
  return text
    .replace(/\[url=[^\]]*\](.*?)\[\/url\]/g, "$1")
    .replace(/\[spoiler\].*?\[\/spoiler\]/gs, "[spoiler hidden]")
    .replace(/\[b\](.*?)\[\/b\]/g, "**$1**")
    .replace(/\[i\](.*?)\[\/i\]/g, "*$1*")
    .replace(/\[raw\](.*?)\[\/raw\]/gs, "$1")
    .trim();
}

const VNDB_LENGTH_LABELS = {
  1: "Very Short (< 2h)", 2: "Short (2-10h)", 3: "Medium (10-30h)",
  4: "Long (30-50h)", 5: "Very Long (50h+)",
};

const VNDB_VOICED = { 1: "Not voiced", 2: "Ero only", 3: "Partially voiced", 4: "Fully voiced" };

function compileVisualNovel(query, data) {
  const { vndb, characters, releases } = data;
  const vn = vndb;
  if (!vn) return "No data available from VNDB.";

  const sections = [];

  // Header
  const title = vn.title || query;
  sections.push(`# Research: ${title}`);
  sections.push(`> Domain: visual-novel | Sources: VNDB | Generated: ${new Date().toISOString()}`);
  sections.push("");

  // Summary
  const devs = (vn.developers || []).map((d) => d.name).filter(Boolean);
  const ratingStr = vn.rating ? `Rated ${(vn.rating / 10).toFixed(1)}/10` : "";
  const voteStr = vn.votecount ? `(${vn.votecount.toLocaleString()} votes)` : "";
  sections.push("## Summary");
  sections.push(
    `${title} is a visual novel` +
    (devs.length ? ` by ${devs.join(", ")}` : "") +
    (vn.released ? ` (released ${vn.released})` : "") +
    `. ${VNDB_LENGTH_LABELS[vn.length] || ""}` +
    (ratingStr ? `. ${ratingStr} ${voteStr}` : "") +
    "."
  );
  sections.push("");

  // Basic Info
  sections.push("## Basic Info");
  sections.push("| Field | Value |");
  sections.push("|-------|-------|");
  sections.push(`| Title | ${vn.title} |`);
  if (vn.alttitle) sections.push(`| Alt Title | ${vn.alttitle} |`);
  // Title variants
  const titles = vn.titles || [];
  for (const t of titles.filter((t) => t.official && !t.main)) {
    sections.push(`| Title (${t.lang}) | ${t.title}${t.latin ? ` (${t.latin})` : ""} |`);
  }
  if (vn.aliases?.length) sections.push(`| Aliases | ${vn.aliases.join(", ")} |`);
  if (vn.released) sections.push(`| Released | ${vn.released} |`);
  if (vn.olang) sections.push(`| Original Language | ${vn.olang} |`);
  if (vn.languages?.length) sections.push(`| Languages | ${vn.languages.join(", ")} |`);
  if (vn.platforms?.length) sections.push(`| Platforms | ${vn.platforms.join(", ")} |`);
  if (vn.length) sections.push(`| Length | ${VNDB_LENGTH_LABELS[vn.length] || vn.length} |`);
  if (vn.length_minutes) sections.push(`| Avg Playtime | ${vn.length_minutes} min (${vn.length_votes || 0} reports) |`);
  if (devs.length) sections.push(`| Developer | ${devs.join(", ")} |`);
  const devStatus = { 0: "Finished", 1: "In Development", 2: "Cancelled" };
  if (vn.devstatus !== undefined) sections.push(`| Dev Status | ${devStatus[vn.devstatus] || vn.devstatus} |`);
  sections.push("");

  // Rating
  if (vn.rating || vn.average) {
    sections.push("## Rating");
    if (vn.rating) sections.push(`- **Bayesian**: ${(vn.rating / 10).toFixed(2)}/10`);
    if (vn.average) sections.push(`- **Raw Average**: ${(vn.average / 10).toFixed(2)}/10`);
    if (vn.votecount) sections.push(`- **Votes**: ${vn.votecount.toLocaleString()}`);
    sections.push("");
  }

  // Description
  if (vn.description) {
    sections.push("## Description");
    sections.push(stripVndbFormatting(vn.description));
    sections.push("");
  }

  // Tags
  const vnTags = (vn.tags || []).filter((t) => t.spoiler === 0 || t.spoiler === undefined).sort((a, b) => (b.rating || 0) - (a.rating || 0));
  if (vnTags.length) {
    sections.push("## Tags");
    const byCat = {};
    for (const t of vnTags) {
      const cat = t.category === "cont" ? "Content" : t.category === "tech" ? "Technical" : t.category === "ero" ? "Sexual" : "Other";
      if (!byCat[cat]) byCat[cat] = [];
      byCat[cat].push(t);
    }
    for (const [cat, catTags] of Object.entries(byCat)) {
      if (cat === "Sexual") continue; // skip ero tags in research output
      sections.push(`**${cat}**: ${catTags.slice(0, 12).map((t) => `${t.name} (${t.rating?.toFixed(1) || "?"})`).join(", ")}`);
    }
    const spoilerTags = (vn.tags || []).filter((t) => t.spoiler > 0);
    if (spoilerTags.length) sections.push(`\n*${spoilerTags.length} spoiler tag(s) hidden.*`);
    sections.push("");
  }

  // Characters
  const chars = characters || [];
  if (chars.length) {
    sections.push("## Characters");
    for (const c of chars) {
      const role = (c.vns || []).find((v) => v.id === vn.id)?.role || "?";
      const spoiler = (c.vns || []).find((v) => v.id === vn.id)?.spoiler || 0;
      if (spoiler > 0) continue; // skip spoiler characters

      const physicals = [
        c.age ? `Age: ${c.age}` : null,
        c.height ? `${c.height}cm` : null,
        c.blood_type ? `Blood: ${c.blood_type}` : null,
      ].filter(Boolean).join(", ");

      sections.push(`### ${c.name}${c.original ? ` (${c.original})` : ""} — ${role}`);
      if (physicals) sections.push(`*${physicals}*`);
      if (c.description) sections.push(truncate(stripVndbFormatting(c.description), 200));

      // Traits (non-spoiler)
      const traits = (c.traits || []).filter((t) => t.spoiler === 0 || t.spoiler === undefined);
      if (traits.length) {
        const grouped = {};
        for (const t of traits) {
          const grp = t.group_name || "Other";
          if (!grouped[grp]) grouped[grp] = [];
          grouped[grp].push(t.name);
        }
        for (const [grp, names] of Object.entries(grouped)) {
          sections.push(`- **${grp}**: ${names.join(", ")}`);
        }
      }
      sections.push("");
    }
  }

  // Staff
  const staffList = vn.staff || [];
  if (staffList.length) {
    sections.push("## Staff");
    sections.push("| Role | Name |");
    sections.push("|------|------|");
    for (const s of staffList) {
      sections.push(`| ${escapeMarkdown(s.role || "—")}${s.note ? ` (${s.note})` : ""} | ${escapeMarkdown(s.name || "—")}${s.original ? ` (${s.original})` : ""} |`);
    }
    sections.push("");
  }

  // Voice Actors
  const vaList = vn.va || [];
  if (vaList.length) {
    sections.push("## Voice Actors");
    sections.push("| Character | Voice Actor |");
    sections.push("|-----------|-------------|");
    for (const va of vaList) {
      const charName = va.character?.name || "—";
      const staffName = va.staff?.name || "—";
      sections.push(`| ${escapeMarkdown(charName)} | ${escapeMarkdown(staffName)} |`);
    }
    sections.push("");
  }

  // Relations
  const rels = vn.relations || [];
  if (rels.length) {
    sections.push("## Relations");
    sections.push("| Type | Title |");
    sections.push("|------|-------|");
    for (const r of rels) {
      sections.push(`| ${r.relation_official || r.relation || "—"} | ${escapeMarkdown(r.title || "—")} |`);
    }
    sections.push("");
  }

  // Releases (summarized)
  const relsList = releases || [];
  if (relsList.length) {
    sections.push("## Releases");
    sections.push("| Date | Title | Platforms | Languages | Voiced |");
    sections.push("|------|-------|-----------|-----------|--------|");
    for (const r of relsList.slice(0, 15)) {
      const langs = (r.languages || []).map((l) => l.lang + (l.mtl ? " (MTL)" : "")).join(", ");
      const plats = (r.platforms || []).join(", ");
      sections.push(`| ${r.released || "—"} | ${escapeMarkdown(r.title || "—")} | ${plats || "—"} | ${langs || "—"} | ${VNDB_VOICED[r.voiced] || "—"} |`);
    }
    if (relsList.length > 15) sections.push(`\n*...and ${relsList.length - 15} more release(s).*`);
    sections.push("");
  }

  // External Links
  const extlinks = vn.extlinks || [];
  if (extlinks.length) {
    sections.push("## External Links");
    sections.push(`- VNDB: https://vndb.org/${vn.id}`);
    for (const link of extlinks) {
      if (link.url) sections.push(`- ${link.label || link.name || "Link"}: ${link.url}`);
    }
    sections.push("");
  }

  sections.push("---");
  sections.push("*Data compiled from VNDB API (v2/Kana).*");

  return sections.join("\n");
}

// ─── Light Novel Compiler ───────────────────────────────────────────────

function compileLightNovel(query, data) {
  const { anilist, narou, deepSearch } = data;
  const sections = [];

  const m = anilist;
  const wn = narou;
  const ds = deepSearch;
  const title = m?.title?.english || m?.title?.romaji || wn?.title || query;
  const deepSources = [];
  if (ds?.reviews?.length) deepSources.push("Bookmeter");
  if (ds?.analyses?.length) deepSources.push("note.com");
  const sources = [m ? "AniList" : null, wn ? "Narou (小説家になろう)" : null, ...deepSources].filter(Boolean).join(", ");

  sections.push(`# Research: ${title}`);
  sections.push(`> Domain: light-novel | Sources: ${sources} | Generated: ${new Date().toISOString()}`);
  sections.push("");

  // Summary
  sections.push("## Summary");
  if (m) {
    const volInfo = m.volumes ? `${m.volumes} volume${m.volumes > 1 ? "s" : ""}` : "";
    const chapInfo = m.chapters ? `${m.chapters} chapter${m.chapters > 1 ? "s" : ""}` : "";
    const countInfo = [volInfo, chapInfo].filter(Boolean).join(", ");
    sections.push(
      `${title} is a ${(m.format || "NOVEL").replace(/_/g, " ").toLowerCase()}` +
      (countInfo ? ` (${countInfo})` : "") +
      `. Status: ${(m.status || "Unknown").replace(/_/g, " ")}.` +
      (m.averageScore ? ` Scored ${m.averageScore}/100 on AniList.` : "") +
      (wn ? ` Originally serialized on Syosetu with ${(wn.bookmarks || 0).toLocaleString()} bookmarks.` : "")
    );
  } else if (wn) {
    sections.push(
      `${wn.title} is a web novel on Syosetu.` +
      ` ${wn.episodes} episode${wn.episodes !== 1 ? "s" : ""}, ${(wn.charCount || 0).toLocaleString()} characters.` +
      ` ${wn.isComplete ? "Completed" : wn.isOnHiatus ? "On hiatus" : "Ongoing"}.` +
      ` ${(wn.bookmarks || 0).toLocaleString()} bookmarks.`
    );
  }
  sections.push("");

  // Basic Info (AniList)
  if (m) {
    sections.push("## Basic Info (Published)");
    sections.push("| Field | Value |");
    sections.push("|-------|-------|");
    if (m.title?.romaji) sections.push(`| Title (Romaji) | ${m.title.romaji} |`);
    if (m.title?.english) sections.push(`| Title (English) | ${m.title.english} |`);
    if (m.title?.native) sections.push(`| Title (Native) | ${m.title.native} |`);
    sections.push(`| Format | ${(m.format || "—").replace(/_/g, " ")} |`);
    if (m.volumes) sections.push(`| Volumes | ${m.volumes} |`);
    if (m.chapters) sections.push(`| Chapters | ${m.chapters} |`);
    sections.push(`| Status | ${(m.status || "—").replace(/_/g, " ")} |`);
    sections.push(`| Published | ${formatDate(m.startDate)} → ${formatDate(m.endDate)} |`);
    if (m.source) sections.push(`| Source | ${m.source.replace(/_/g, " ")} |`);
    sections.push("");
  }

  // Web Novel Info (Narou)
  if (wn) {
    sections.push("## Web Novel Origin (Syosetu)");
    sections.push("| Field | Value |");
    sections.push("|-------|-------|");
    sections.push(`| Title | ${wn.title} |`);
    sections.push(`| Author | ${wn.author || "—"} |`);
    sections.push(`| Genre | ${wn.bigGenre} > ${wn.genre} |`);
    sections.push(`| Type | ${wn.isSerial ? "Serial" : "Short Story"} |`);
    sections.push(`| Status | ${wn.isComplete ? "Complete" : wn.isOnHiatus ? "On Hiatus" : "Ongoing"} |`);
    sections.push(`| Episodes | ${wn.episodes || "—"} |`);
    sections.push(`| Length | ${(wn.charCount || 0).toLocaleString()} characters (~${wn.readingTimeMinutes || "?"} min) |`);
    sections.push(`| First Posted | ${wn.firstPosted || "—"} |`);
    sections.push(`| Last Updated | ${wn.lastPosted || "—"} |`);
    if (wn.isIsekai) sections.push(`| Isekai | Yes (${wn.isekaiType || "unspecified"}) |`);
    if (wn.isR15) sections.push(`| R15 | Yes |`);
    sections.push("");

    // Popularity
    sections.push("## Web Novel Popularity");
    sections.push("| Metric | Value |");
    sections.push("|--------|-------|");
    sections.push(`| Global Points | ${(wn.globalPoints || 0).toLocaleString()} |`);
    sections.push(`| Bookmarks | ${(wn.bookmarks || 0).toLocaleString()} |`);
    sections.push(`| Reviews | ${(wn.reviews || 0).toLocaleString()} |`);
    sections.push(`| Rating | ${(wn.ratingPoints || 0).toLocaleString()} pts (${(wn.ratingCount || 0).toLocaleString()} raters) |`);
    if (wn.dialogueRate) sections.push(`| Dialogue Rate | ${wn.dialogueRate}% |`);
    sections.push("");

    // Keywords
    if (wn.keywords?.length) {
      sections.push("## Web Novel Keywords");
      sections.push(wn.keywords.join(", "));
      sections.push("");
    }
  }

  // Synopsis
  const synopsis = m?.description || wn?.synopsis;
  if (synopsis) {
    sections.push("## Synopsis");
    sections.push(stripHtml(synopsis));
    sections.push("");
  }

  // Genres & Tags (AniList)
  if (m?.genres?.length || m?.tags?.length) {
    sections.push("## Genres & Tags");
    if (m.genres?.length) sections.push(`**Genres**: ${m.genres.join(", ")}`);
    const tags = (m.tags || []).filter((t) => !t.isGeneralSpoiler && !t.isMediaSpoiler).sort((a, b) => (b.rank || 0) - (a.rank || 0)).slice(0, 12);
    if (tags.length) {
      sections.push("**Tags**:");
      for (const t of tags) {
        sections.push(`- ${t.name} (${t.rank}%)`);
      }
    }
    sections.push("");
  }

  // Characters (AniList)
  const chars = m?.characters?.edges || [];
  if (chars.length) {
    sections.push("## Characters");
    sections.push("| Character | Role |");
    sections.push("|-----------|------|");
    for (const edge of chars.slice(0, 15)) {
      const name = edge.node?.name?.full || "—";
      const role = (edge.role || "—").charAt(0) + (edge.role || "").slice(1).toLowerCase();
      sections.push(`| ${escapeMarkdown(name)} | ${role} |`);
    }
    sections.push("");
  }

  // Staff (AniList)
  const staffEdges = m?.staff?.edges || [];
  if (staffEdges.length) {
    sections.push("## Staff");
    sections.push("| Role | Name |");
    sections.push("|------|------|");
    for (const edge of staffEdges) {
      sections.push(`| ${escapeMarkdown(edge.role || "—")} | ${escapeMarkdown(edge.node?.name?.full || "—")} |`);
    }
    sections.push("");
  }

  // Relations (AniList) — especially anime adaptations
  const rels = m?.relations?.edges || [];
  if (rels.length) {
    sections.push("## Relations & Adaptations");
    sections.push("| Type | Title | Format |");
    sections.push("|------|-------|--------|");
    for (const edge of rels) {
      sections.push(`| ${(edge.relationType || "—").replace(/_/g, " ")} | ${escapeMarkdown(edge.node?.title?.english || edge.node?.title?.romaji || "—")} | ${(edge.node?.format || "—").replace(/_/g, " ")} |`);
    }
    sections.push("");
  }

  // Recommendations
  const recs = (m?.recommendations?.edges || []).filter((e) => e.node?.mediaRecommendation);
  if (recs.length) {
    sections.push("## Recommendations");
    for (const edge of recs.slice(0, 8)) {
      const rec = edge.node.mediaRecommendation;
      sections.push(`- ${rec.title?.english || rec.title?.romaji || "—"}${rec.averageScore ? ` (${rec.averageScore}/100)` : ""}`);
    }
    sections.push("");
  }

  // Scores
  if (m?.averageScore) {
    sections.push("## Scores");
    sections.push(`- AniList: ${m.averageScore}/100 (#${m.popularity || "?"} popularity, ${(m.favourites || 0).toLocaleString()} favs)`);
    sections.push("");
  }

  // Deep Search — Japanese community analysis and reviews
  if (ds) {
    const hasContent = (ds.analyses?.length > 0) || (ds.reviews?.length > 0);
    if (hasContent) {
      sections.push("## Japanese Community Insights (Deep Search)");
      sections.push("*Content extracted from Japanese review and analysis sites. May contain spoilers.*");
      sections.push("");

      for (const analysis of (ds.analyses || [])) {
        sections.push(`### Analysis (${analysis.source})`);
        sections.push(`*Source: ${analysis.url}*`);
        sections.push(analysis.content);
        sections.push("");
      }

      for (const review of (ds.reviews || [])) {
        sections.push(`### Reviews (${review.source})`);
        sections.push(`*Source: ${review.url}*`);
        sections.push(review.content);
        sections.push("");
      }
    }

    if (ds.errors?.length) {
      sections.push(`*Deep search notes: ${ds.errors.join("; ")}*`);
      sections.push("");
    }
  }

  // External Links
  sections.push("## External Links");
  if (m?.siteUrl) sections.push(`- AniList: ${m.siteUrl}`);
  if (wn?.url) sections.push(`- Syosetu: ${wn.url}`);
  const links = m?.externalLinks || [];
  for (const link of links) {
    if (link.url && link.site) sections.push(`- ${link.site}: ${link.url}`);
  }
  sections.push("");

  sections.push("---");
  sections.push(`*Data compiled from ${sources}.*`);

  return sections.join("\n");
}

// ═══════════════════════════════════════════════════════════════════════════
// MAIN COMPILE FUNCTION
// ═══════════════════════════════════════════════════════════════════════════

const COMPILERS = {
  anime: compileAnime,
  "visual-novel": compileVisualNovel,
  "light-novel": compileLightNovel,
};

/**
 * Compile API data into a research document and write to /tmp with reading gate.
 *
 * @param {string} domain - Research domain
 * @param {string} query - Original search query
 * @param {object} data - Raw API response data (keyed by source name)
 * @returns {Promise<{path: string, charCount: number, summary: string, error?: string}>}
 */
export async function compile(domain, query, data) {
  const compiler = COMPILERS[domain];
  if (!compiler) {
    return { path: "", charCount: 0, summary: "", error: `No compiler for domain: ${domain}` };
  }

  try {
    const markdown = compiler(query, data);
    const { path, charCount } = await writeResearchDocument(domain, query, markdown);

    // Extract first line of summary section for the tool response
    const summaryMatch = markdown.match(/## Summary\n(.+)/);
    const summary = summaryMatch ? truncate(summaryMatch[1], 200) : `Research compiled for "${query}"`;

    return { path, charCount, summary };
  } catch (err) {
    return { path: "", charCount: 0, summary: "", error: `Compilation error: ${err.message}` };
  }
}
