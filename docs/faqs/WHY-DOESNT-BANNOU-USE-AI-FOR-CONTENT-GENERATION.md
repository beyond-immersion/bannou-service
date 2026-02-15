# Why Doesn't Bannou Use AI/LLM for Content Generation?

> **Short Answer**: Because Bannou's computational systems follow a deliberate "formal theory over AI" design pattern. Music uses formal music theory and GOAP planning. Behavior uses ABML (a formal DSL) and GOAP planning. Storyline uses formal narrative theory and GOAP planning. Compression uses deterministic template-based archival. Every system that produces content does so through auditable, deterministic, reproducible formal rules -- not through non-deterministic neural inference. This is not a temporary limitation; it is a core architectural decision.

---

## The Pattern: Formal Theory Over AI

Bannou has a consistent design pattern across all its computational content systems:

| System | Formal Approach | NOT This |
|--------|----------------|----------|
| **Music** | Lerdahl tonal pitch space, ITPRA model, BRECVEMA mechanisms, GOAP planning | LLM/neural music generation |
| **Behavior** | ABML (formal DSL), GOAP planning, Variable Provider Factory | Neural behavior generation |
| **Storyline** | Propp functions, Reagan arcs, GOAP planning | AI prose generation |
| **Compression** | Deterministic template-based archival | AI-summarized archives |
| **Summarization** | Switch-expression templates with formatted values | LLM narrative prose |

The design justification, stated in the music system's architecture: *"automated systems should be trustworthy, auditable, and deterministic."*

---

## Why This Matters at Scale

Bannou targets 100,000+ concurrent AI-driven NPCs. At that scale, the properties of formal systems become critical advantages:

### 1. Determinism

Same input always produces same output. This enables:
- **Redis caching**: Music compositions are cached by seed. Template summaries are cacheable by input hash. LLM outputs cannot be cached because the same prompt produces different text each call.
- **Test reproducibility**: Unit tests can assert exact outputs. Non-deterministic systems require fuzzy matching or snapshot testing.
- **Archive consistency**: Character compression produces archives consumed by the Storyline SDK. If the archive text changes between calls, the narrative extraction produces different content from the same history.

### 2. Zero External Dependencies

Bannou's computational systems have no external service dependencies:
- Music generation is pure computation (MusicTheory + MusicStoryteller SDKs).
- ABML compilation and GOAP planning run entirely in-process.
- Storyline generation uses in-process SDKs (storyline-theory + storyline-storyteller).
- Template summarization is a switch expression.

LLM integration would require API keys, external service availability, network latency, and a new infrastructure dependency. This violates the self-contained deployment model where Bannou runs with only Redis, MySQL, and RabbitMQ.

### 3. Cost at Scale

Template summarization: <1ms, zero marginal cost.
LLM summarization: 1-5+ seconds, per-call API cost.

With 100K+ characters needing periodic summarization during compression workflows, LLM costs scale linearly with population size. Template costs are constant.

### 4. Latency

Summarization is called during character compression workflows (lib-resource), which are time-sensitive operations triggered by character death. Template generation completes in microseconds. LLM calls introduce seconds of latency per character, creating backpressure in the compression pipeline.

---

## The Common Feature Request

The most frequent form of this request (see [#230](https://github.com/beyond-immersion/bannou-service/issues/230), [#269](https://github.com/beyond-immersion/bannou-service/issues/269)):

> "Replace template-based summarization with LLM-generated narrative prose for richer, more natural-sounding text."

The template output is intentionally formulaic:
- `"From the northlands"`
- `"Worked as a blacksmith"`
- `"Led the Battle of Stormgate"`

This text is consumed by:
- The **compression system** (deterministic archival)
- The **Storyline SDK** (archive extraction for narrative generation)
- **ABML behavior expressions** (NPC decision-making from history)

All three consumers require predictable, parseable, reproducible text. "Richer prose" would break the consumers that depend on structural consistency.

---

## If Richer Output Is Needed

Improve the templates. The switch-expression approach can be extended with:

- **More template variations per element type**: Randomized selection (seeded for determinism) provides variety without non-determinism.
- **Combination templates**: Weave multiple backstory elements into compound sentences.
- **Configurable prose style**: Configuration schema controls formality, verbosity, and cultural flavor.
- **Narrative arc awareness**: Templates that reference the character's overall trajectory (rise, fall, redemption) rather than treating each element independently.

This follows the music system's proven approach: rich, emotionally resonant output from formal rules, not AI inference. The MusicStoryteller SDK produces compositions with narrative arcs, emotional dynamics, and stylistic variety -- all from Lerdahl tonal pitch space and GOAP planning.

---

## What About AI for Non-Generation Use Cases?

This FAQ addresses **content generation** -- producing text, music, behavior, or narrative. AI/ML may be appropriate for fundamentally different use cases:

| Use Case | Category | AI Appropriate? |
|----------|----------|-----------------|
| Character summaries | Generation | No -- formal templates |
| Music composition | Generation | No -- formal music theory |
| NPC behavior | Generation | No -- ABML + GOAP |
| Narrative planning | Generation | No -- formal narrative theory |
| Document search | Retrieval | Potentially -- vector embeddings for semantic similarity are a retrieval optimization, not content generation |
| Anomaly detection | Classification | Potentially -- statistical analysis via Analytics already exists; ML could enhance pattern detection |
| Content moderation | Classification | Delegate to specialists -- see [WHY-DOESNT-BANNOU-BUILD-ANTI-CHEAT-REPLAYS-OR-GAME-HOSTING.md](WHY-DOESNT-BANNOU-BUILD-ANTI-CHEAT-REPLAYS-OR-GAME-HOSTING.md) |

The distinction: generation systems must be deterministic and self-contained. Retrieval and classification systems have different requirements and may benefit from ML techniques without violating the formal-theory principle.

---

## The Litmus Test

Before proposing AI/LLM integration for a Bannou system:

1. **Is the output consumed by other systems?** If yes, those consumers likely depend on structural consistency that LLM output cannot guarantee.
2. **Does the system need to scale to 100K+ entities?** If yes, per-call API costs and latency make LLM integration impractical.
3. **Is determinism required?** If the same input must produce the same output (for caching, testing, archival), LLM is the wrong tool.
4. **Can formal rules achieve the goal?** Music theory produces emotionally rich compositions. Narrative theory produces compelling story arcs. Template systems can be extended with variation and combination. Exhaust formal approaches before considering AI.
5. **Is this generation or retrieval/classification?** Only the latter categories may warrant AI consideration, and even then, prefer specialist third-party services over building in-house.
