# Why Does Bannou Have Its Own Documentation Service Instead of Using a Wiki or CMS?

> **Short Answer**: Because the primary consumer of Bannou's documentation is not a human with a browser -- it is an AI agent with an HTTP client. The Documentation service is a knowledge base API, not a wiki.

---

## The Obvious Alternatives

There is no shortage of documentation tools:
- **Wikis**: Confluence, MediaWiki, Notion, GitBook
- **Static site generators**: Docusaurus, MkDocs, Hugo
- **CMS platforms**: WordPress, Ghost, Strapi
- **Hosted docs**: ReadTheDocs, Mintlify

All of these are mature, well-maintained, and purpose-built for documentation. Building a custom documentation service with 27 endpoints, full-text search, git repository synchronization, archive management, and namespace-scoped CRUD seems like a textbook case of reinventing the wheel.

So why does Bannou have `lib-documentation`?

---

## The Primary Consumer Is Not a Human

The Documentation service was designed for a specific use case that no existing tool addresses well: **AI agents that need to query a knowledge base programmatically**.

The service's API is explicitly built for three AI integration patterns:

1. **SignalWire SWAIG** -- voice AI agents that need to look up answers during phone calls and read them aloud. This is why documents have a `voiceSummary` field with a configurable maximum length (default 200 characters). A wiki page with 3,000 words of markdown is useless to a voice agent that needs a one-sentence answer.

2. **OpenAI function calling** -- LLM agents that call `documentation/query` with a natural language question and receive structured, relevance-scored results. The response includes `relevanceScore`, `matchType` (title/content/tag), and optionally `relatedDocuments` at configurable depth. This is a purpose-built retrieval API, not a search box bolted onto a wiki.

3. **Claude tool use** -- AI coding assistants that need to look up Bannou's own architecture, tenets, and service details during development. The same API that serves voice agents serves development tools.

A Confluence wiki can serve human readers. It cannot serve a voice agent that needs a 200-character spoken summary of "what is the escrow service?" during a support call.

---

## Git-Bound Namespaces: The Authoring Model

The Documentation service does not replace wikis for authoring. It replaces them for **serving**.

The core authoring model is **git-bound namespaces**: you bind a namespace to a git repository, and the Documentation service automatically syncs markdown files from that repository on a configurable schedule. When a namespace is git-bound, manual mutations (create, update, delete) are rejected -- git is the single source of truth.

This means:
- **Authors write markdown in git** -- with pull requests, code review, version history, and all the tooling developers already use.
- **The Documentation service indexes and serves** -- full-text search, natural language query, voice summaries, relevance scoring.
- **No dual-source-of-truth problem** -- the content lives in git. The service is a read-optimized query layer over that content.

Manual namespaces also exist for content that does not belong in a git repository (user-submitted knowledge, runtime-generated documentation, admin-curated content). But the primary workflow is: write in git, serve through the API.

---

## Why Not Just Call the Git API Directly?

Because raw markdown files in a git repository do not have:
- Full-text search indexing (Redis Search with relevance scoring)
- Voice-friendly summaries
- Slug-based lookups
- Namespace isolation
- Related document discovery
- Structured query responses with match types and relevance scores
- Archive and restore capabilities
- Trashcan with TTL-based auto-cleanup

You could build all of this as a middleware layer in front of a git API. At that point, you have built the Documentation service -- just with worse integration into the rest of the Bannou platform.

---

## Why It Is L3 and Not L4

Documentation is an App Feature (L3), not a Game Feature (L4). This is deliberate:

- **L3 means no game dependencies**: The Documentation service does not import Character, Realm, Quest, or any other L2 service. It stores and serves text documents. That is all.
- **L3 means useful for non-game deployments**: A Bannou deployment running a real-time collaboration platform still benefits from a queryable knowledge base. The documentation service is useful wherever AI agents need structured access to text content.
- **L3 means optional**: If you do not need a documentation API, disable it. Nothing else breaks.

If the Documentation service needed to index game-specific content (character lore, realm histories, quest descriptions), it would need L2 dependencies and would be reclassified to L4. Instead, it is content-agnostic -- it stores and serves documents regardless of what those documents are about.

---

## The Browser Exception

The Documentation service is one of only two Bannou services (alongside Website) that exposes browser-facing GET endpoints. This is an explicit exception to Bannou's POST-only API pattern:

- `GET /documentation/render/{namespaceId}/{slug}` renders a markdown document as HTML
- `GET /documentation/render/{namespaceId}` renders a namespace index page

These exist because documentation is one of the few things where a bookmarkable, browser-accessible URL is genuinely useful. An engineer debugging a production issue should be able to paste a documentation URL into Slack and have it render in a browser without requiring a POST client.

This exception is documented, intentional, and narrow. The 25 other Documentation endpoints follow the standard POST-only pattern.

---

## The Real Question

The real question is not "why build a documentation service?" It is "who is reading your documentation?"

If the answer is "humans in browsers," use a wiki. Bannou does not compete with Confluence.

If the answer is "AI agents over HTTP that need structured, relevance-scored, voice-friendly responses from a knowledge base that syncs from git" -- that tool does not exist off the shelf. The Documentation service exists because its use case does not.
