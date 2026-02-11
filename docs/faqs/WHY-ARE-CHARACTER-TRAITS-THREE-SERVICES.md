# Why Are Character Personality, History, and Encounters THREE Separate Services?

> **Short Answer**: Because they have different data lifecycles, different scaling profiles, different consumers, and different eviction strategies. Merging them would either couple the Actor runtime to a monolithic dependency or force a single service to juggle three incompatible state management patterns.

---

## The Obvious Objection

"They're all about the same character. Just put them in one `character-details` service."

This is the most common reaction. It sounds reasonable. A character has a personality, a history, and memories of encounters. These feel like properties of a single entity. Why split them across three services with three schemas, three state stores, and three event streams?

The answer has nothing to do with organizational preference and everything to do with how the data actually flows through the system at runtime.

---

## Three Different Data Lifecycles

### Character Personality

Personality traits are **continuously mutating floating-point values on bipolar axes**. Aggression, curiosity, loyalty, discipline -- each is a number between -1.0 and 1.0 that shifts probabilistically based on character experiences. A character who witnesses repeated betrayals drifts toward suspicion. One who succeeds at diplomacy drifts toward sociability.

These values change **frequently** (potentially every behavior tick), change **incrementally** (small floating-point adjustments), and are **read on every NPC decision cycle** (every 100-500ms for active actors). The storage pattern is a hot Redis cache with periodic MySQL persistence. The eviction strategy is "never evict active characters" because the Actor runtime reads personality values on every tick.

### Character History

History entries are **append-only records of participation in world events**. A character fought in the Battle of Ironvale. A character witnessed the coronation of Queen Sera. A character survived the Red Plague. These are written once, never modified, and queried infrequently -- primarily during character compression (when the character dies and their life archive is created for the content flywheel) or when the Storyline service needs backstory data for narrative generation.

The storage pattern is MySQL-primary with minimal Redis caching. Records accumulate over a character's lifetime and can number in the hundreds. They are rarely read during active gameplay -- the behavior system only accesses a summarized backstory, not individual historical events.

### Character Encounters

Encounters are **decaying interaction records between character pairs**. When two characters interact memorably (trade, fight, have a conversation, betray each other), the encounter is recorded with per-participant sentiment perspectives. These records decay over time -- a grudge from five years ago weighs less than one from yesterday. The service maintains per-character and per-pair limits, automatically pruning the least significant encounters when limits are exceeded.

The storage pattern is Redis-primary with time-weighted scoring. Encounters are read frequently during NPC decisions ("have I met this person before? do I like them?") but are also actively pruned and re-scored. The eviction strategy involves both TTL-based decay and active limit enforcement -- fundamentally different from both personality's "never evict" and history's "never delete."

---

## Three Different Scaling Profiles

At the target scale of 100,000+ concurrent NPCs:

- **Personality** is read-heavy with small, frequent writes. 100,000 NPCs reading personality every 100-500ms means 200,000-1,000,000 reads per second. This is a hot cache problem.
- **History** is write-heavy with infrequent reads. Events happen, characters participate, records are appended. Reads happen mainly during compression or narrative generation -- batch operations, not real-time queries.
- **Encounters** are read-write with active pruning. Every NPC interaction potentially creates or updates an encounter record AND triggers decay recalculation for existing records. This is a sorted-set problem best solved with Redis ZRANGEBYSCORE operations.

If these were one service, you would need to scale it to handle the combined load of all three patterns simultaneously. Since the read-heavy personality pattern dominates, you would massively over-provision for history writes. Since the pruning pattern of encounters requires different Redis data structures than the simple key-value pattern of personality, you would need both patterns in the same service's state management.

Keeping them separate means each scales according to its own characteristics.

---

## Three Different Variable Providers

All three services feed data into the Actor runtime via the Variable Provider Factory pattern, but they provide different variable namespaces:

- **Character Personality** provides `${personality.aggression}`, `${personality.curiosity}`, `${combat.preferred_range}`, etc.
- **Character History** provides `${backstory.origin}`, `${backstory.occupation}`, `${backstory.trauma_count}`, etc.
- **Character Encounters** provides `${encounters.last_hostile_days}`, `${encounters.sentiment_toward_TARGET}`, `${encounters.times_met_TARGET}`, etc.

Each provider factory is independently registered via DI. Each can fail independently without affecting the others. If the encounter service is temporarily unavailable, NPCs lose their interaction memory but retain their personality and backstory. This graceful degradation is only possible because the providers are separate services.

Merging them into one service means one failure takes out all three variable namespaces simultaneously. An NPC that can't remember its own personality is fundamentally broken. An NPC that temporarily forgets a specific encounter is mildly confused.

---

## The Hierarchy Argument

All three are Layer 4 (Game Features). They all depend on Character (Layer 2) for the entity they describe. None of them depend on each other.

This independence is architecturally significant. Character Personality evolves from experience events. Character History records from world events. Character Encounters form from interaction events. These are three independent event streams with three independent consumers. Merging them into one service creates artificial coupling between event processing pipelines that have no reason to be coupled.

If a bug in encounter pruning logic causes the service to crash, personality evolution and history recording should continue uninterrupted. Service isolation is failure isolation.

---

## The Real Test: Could You Disable One?

Imagine a deployment that wants NPCs with personality and backstory but doesn't need encounter tracking (maybe it's a single-player narrative game, not a social simulation).

With three services: disable `lib-character-encounter`. The Actor runtime's Variable Provider Factory discovers two providers instead of three. NPCs make decisions without encounter data. Everything else works.

With one merged service: you can't disable encounter tracking without also losing personality and history. The "optional Game Features" promise of Layer 4 breaks down.

This is the ultimate validation of the split. Game Features are optional by design. If merging them makes any one of them non-optional, the merge violates the architecture.
