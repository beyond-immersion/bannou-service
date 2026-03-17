# Divinity Generation Architecture

> **Type**: Design
> **Status**: Draft
> **Created**: 2026-03-16
> **Last Updated**: 2026-03-16
> **North Stars**: #1, #2, #5
> **Related Plugins**: Divine, Seed, Currency, Genesis, Actor, Quest, Analytics
> **Prerequisites**: Genesis implementation (genesis templates, ICurrencyTransactionListener, ISeedEvolutionListener)

## Summary

Redesigns Divine's divinity generation pipeline to use Seed bond propagation and Currency wallets instead of Analytics event subscriptions. Analytics is the most optional plugin in the system and cannot be in the critical path for game mechanics. The new architecture extends Seed's existing bond mechanism with configurable growth propagation (per-bond direction and ratio), so a character's domain seed bonded to a god's domain seed automatically transfers growth at L2 with no Divine involvement. Also introduces the "wallet as multi-consumer accumulation point" pattern where different systems (Quest, Divine, leveling) independently consume from the same wallet without contention. This makes Divine a pure orchestration layer (deity CRUD, blessings, followers) with no divinity generation plumbing.

---

## Problem Statement

### Analytics Is Wrong for Game Mechanics

The original Divine design subscribes to `analytics.score.updated` events to generate divinity when mortals act in a god's domain. This creates a critical dependency on Analytics (L4, optional):

- Analytics is designed to be the MOST optional plugin — frequently disabled, fully expected to be absent
- If Analytics is disabled, gods stop earning divinity from mortal actions entirely
- The same dependency problem extends to any game mechanic that needs "count of things that happened" — Quest objectives, XP/leveling, divine generation all need a foundational (L2, always-on) source

### The Broader Pattern

Multiple systems need to know "a thing happened in this domain":

| Consumer | What It Needs | Current Source | Problem |
|----------|---------------|----------------|---------|
| **Divine** | "Combat happened, credit war gods" | Analytics events | Optional L4 dependency |
| **Quest** | "10 wolves killed, objective complete" | Unclear | No established pattern |
| **Leveling** | "XP from combat actions" | Unclear | No established pattern |
| **Analytics** | "Combat statistics for dashboards" | Direct events | Fine — Analytics is an observer |

All of these except Analytics need a **foundational** primitive, not an optional observer.

---

## Proposed Solution: Wallet-Driven Growth via Genesis

### Core Mechanism: Currency Wallets as Universal Accumulators

The Genesis service already implements a wallet-to-seed growth pipeline via `ICurrencyTransactionListener`. The mechanism:

```
Currency wallet receives credit (from ANY source)
    ↓ ICurrencyTransactionListener (DI, local, L2 co-located)
Genesis looks up: wallet → entity → template → growthMappings[]
    ↓ amount × ratio = growth, filtered by direction (Credit/Debit/Both)
Seed.RecordGrowth() — seed is ENCAPSULATED, never exposed
    ↓ ISeedEvolutionListener (if phase threshold crossed)
Genesis handles cognitive transitions (Dormant → Stirring → Awakened)
```

**Critical property**: The currency stays in the wallet. Growth mapping is a side effect of the credit, not a consumption. The seed grows permanently. The wallet balance remains spendable.

### The "Bag of Kills" Pattern

A currency wallet serves as an accumulation point that multiple systems consume **independently**:

- **Growth** (permanent, non-destructive): Credits trigger growth mappings. Seed growth is permanent and never reversed by debits.
- **Balance** (spendable, destructive): Systems can debit from the wallet to "consume" accumulated resources.

```
Character kills 15 monsters → 15 credited to "combat_experience" wallet

Growth mapping (direction: Credit):
    15 × 1.0 = 15 growth on "combat" seed domain (PERMANENT)
    God's DI listener saw growth → queued divinity credits (INDEPENDENT)

Quest "Kill 10 Wolves":
    Debits 10 from wallet → balance now 5
    Seed growth UNAFFECTED (mapping = Credit direction only)
    God's divinity UNAFFECTED (already derived from growth events)

Quest "Prove Combat Prowess":
    Checks balance ≥ 5 → debits 5 → balance now 0

Total seed growth: 15 (permanent record of everything ever done)
Total wallet debits: 15 (consumed by quests)
Current wallet balance: 0 (spent)
God divinity earned: proportional to 15 growth (independent of wallet debits)
```

No system blocks any other. The wallet is simultaneously a **spendable resource** (balance) and a **growth driver** (cumulative credits → permanent seed growth). Quest doesn't compete with Divine for the same "pool" — they consume different dimensions of the same underlying events.

### Two Divinity Generation Paths

#### Path 1: Direct (Prayer / Offering)

A worshipper directly credits the god's wallet:

```
Worshipper prays → credits god's divinity wallet with "faith" currency
    ↓ ICurrencyTransactionListener → Genesis growth mapping
    God's domain_power seed grows
    God-actor perceives balance via ${currency.divinity.balance}
    God decides to grant blessing (spends faith from wallet)
```

The god is never "triggered" to act. It perceives accumulated resources through variable providers and makes autonomous GOAP decisions. This is the emergent-over-authored principle applied to divine mechanics.

#### Path 2: Indirect (Mortal Domain Actions)

A character's domain activity eventually credits the relevant gods:

```
Character fights → game engine credits character's "combat" wallet
    ↓ Growth mapping → character's combat seed grows
    ↓ DI Listener → Divine service
    Divine identifies gods whose domain matches (via followers, realm, attention)
    Divine credits relevant gods' wallets with proportional divinity
    ↓ Genesis growth mapping on god's wallet
    God's domain_power seed grows
    God perceives accumulated divinity, decides to act
```

Multiple sources can feed character wallets — game engine, quest/contract fulfillment, extension plugins, gods themselves. Seeds are just accumulators driven by wallet credits.

### The God's Perception Model

The god-actor never needs external triggers. It perceives:

| Variable Provider | What the God Sees |
|-------------------|-------------------|
| `${currency.divinity.balance}` | Spendable divinity in wallet |
| `${seed.domain_power.*}` | Accumulated domain influence (permanent) |
| `${personality.*}` | Own personality traits (via character brain in system realm) |
| `${encounters.*}` | Memories of mortal interactions |

The god's ABML behavior checks these variables and autonomously decides: grant a blessing, intervene in a conflict, manifest an avatar, orchestrate narrative events. All funded from the wallet, all informed by seed growth.

---

## Seed Bond Propagation: The Divine Channel

> **Status**: Primary design direction. Extends Seed's existing bond mechanism with growth propagation.

### What Seed Bonds Do Today

Seed has a **bond** system connecting 2+ seeds of the same `SeedTypeCode`:

| Existing Feature | Behavior |
|------------------|----------|
| **Growth amplification** | Recording seed gets `BondSharedGrowthMultiplier` (default 1.5x) |
| **Activity reset** | Growth on one partner resets `LastActivityAt` on matching domains of all partners (prevents decay) |
| **Bond strength** | `BondStrength += amount * BondStrengthGrowthRate` per growth recording (relationship deepens) |
| **Same-type constraint** | Both seeds must have matching `SeedTypeCode` — can't bond combat seed to nature god |
| **One bond per seed** | `BondId == null` required — each seed bonds to exactly one partner |
| **Cardinality control** | `BondCardinality` on seed type definition controls max participants |

### What Needs to Be Added: Growth Propagation

The missing feature: when seed A records growth, **propagate** a ratio of that growth to bonded seed B, firing all normal seed machinery (events, phase checks, capability recomputation) on the receiving end.

**New per-bond properties:**

| Property | Type | Purpose |
|----------|------|---------|
| `PropagationDirection` | Enum | Controls which direction(s) growth flows |
| `PropagationRatio` | double (0.0-1.0) | What fraction of growth propagates to the partner |

**PropagationDirection enum:**

| Value | Meaning | Gameplay Example |
|-------|---------|------------------|
| `None` | No propagation. Bond provides amplification + decay protection only. | Early/weak bond. Follower benefits from bonding but god doesn't receive growth. |
| `AToB` | Initiator's growth propagates to target. | Standard worship — your actions power your deity. |
| `BToA` | Target's growth propagates to initiator. | Divine patronage — the god empowers you directly. |
| `Bidirectional` | Both directions at the bond's ratio. | Deep bond — mutual growth from each other's actions. |
| `Mirrored` | Growth on either is growth on both (effective ratio 1.0). | Full spiritual union — rare, earned through high bond strength. |

**Per-bond ratio** means different gods of the same domain offer different bond characteristics. Ares might offer `AToB` at ratio 0.8 (aggressive, high transfer to the god) while Athena offers `Bidirectional` at ratio 0.3 (balanced, slower but mutual influence). This is a **gameplay lever** — choosing your patron god has mechanical consequences beyond just "which blessings are available."

### Anti-Cascade Safety

**Propagated growth must not re-propagate.** Without this, A→B→A loops infinitely.

The fix: a `propagated: true` flag on the internal growth recording call. When `RecordGrowth` is invoked with `propagated: true`, it skips the bond propagation step entirely. Same pattern as existing cross-pollination (which uses raw amounts before bond multiplier and sets a `crossPollinated` flag).

Safety layers (defense in depth):

| Layer | What It Prevents |
|-------|------------------|
| `propagated` flag | Direct A↔B loop (propagated growth doesn't re-propagate) |
| Max 2 per bond | No chains (A→B→C) — only A↔B exists |
| One bond per seed | Character's war seed → exactly one god's war seed |
| Same-type constraint | Can't accidentally bond unrelated seed types |

### How It Solves the Divine Flow

```
1. Character follows Ares → bond formed between character's "war" seed and Ares' "war" seed
   (PropagationDirection: AToB, PropagationRatio: 0.8)

2. Character kills monsters
   → Game feeds character's "combat" wallet
   → Genesis growth mapping: wallet credit → character's "war" seed grows 10
   → Bond amplification: character gets 15 (1.5x) — existing behavior
   → Bond propagation: Ares' "war" seed gets 10 × 0.8 = 8 growth (NEW)
       → Ares' growth marked propagated: true (no re-propagation)
       → Normal seed events fire for Ares' seed (seed.growth.updated, phase checks)
       → ISeedEvolutionListener fires for Ares' seed
       → Genesis growth mapping on Ares' side: seed growth → domain_power
       → Ares-actor perceives accumulated power, decides autonomously

3. Character changes patron (Ares → Athena)
   → Bond dissolved (#362 — dissolution endpoint needed)
   → New bond formed with Athena's "war" seed
   → Future combat growth propagates to Athena instead

4. Bidirectional influence (if bond is Bidirectional)
   → Athena's "war" seed grows (from her own divine actions)
   → Bond propagation: character's "war" seed gets growth × ratio
   → Character becomes stronger in the god's domain
   → Same mechanism, both directions, same safety constraints
```

### Multi-Domain, Multi-God Bonding

A character can have different domain seeds bonded to different gods. Each seed bonds independently (one bond per seed constraint):

```
Character's seeds:
    "war" seed      ──bond──▶ Ares' "war" seed      (AToB, ratio 0.8)
    "craft" seed    ──bond──▶ Hephaestus' "craft" seed (Bidirectional, ratio 0.4)
    "devotion" seed ──bond──▶ Apollo's "devotion" seed (AToB, ratio 0.6)
    "nature" seed   ──(no bond)──                     (unbonded, no divine patron for nature)
```

Each domain's divine relationship is independent. The character's combat growth only reaches Ares. Their crafting growth only reaches Hephaestus. This is both mechanically clean and narratively rich — "who do you worship in each aspect of your life?"

### Patron vs Follower: Two Distinct Relationships

A **patron deity** and a **follower** are separate, complementary concepts with different directions of favor:

| Relationship | Who Favors Whom | Bond Direction | Growth Flow |
|-------------|----------------|----------------|-------------|
| **Follower only** | Character → God | `AToB` | Character's domain actions power the god |
| **Patron only** | God → Character | `BToA` | God's divine power flows to the character |
| **Both** | Mutual | `Bidirectional` | Deep bond — mutual growth from each other's actions |
| **Bonded, neither** | Neutral | `None` | Amplification + decay protection only |

- **Patron** = a god that favors the character (god → character). Stored as `patronDeityCode` on the character.
- **Follower** = a character that favors a god (character → god). Stored as a `deity_follower` relationship via lib-relationship.
- These can be bidirectional (a worshipper whose god also watches over them), but don't have to be. A god might patronize a character who worships a different deity. A character might follow a god who hasn't noticed them.

The seed bond's `PropagationDirection` maps directly to the relationship combination. Follower bonds flow `AToB` (character actions feed the god). Patron bonds flow `BToA` (god's power reaches the character). Both = `Bidirectional`.

### Patron Deity Field: Two-Level Design

The patron deity code lives at **two levels** that work together:

**Character (L2)** — `patronDeityCode` (opaque string, nullable)
- The **authoritative source of truth** for the character's current patron deity
- What Divine reads to determine bond configuration
- Nullable — not every character has a patron deity
- Can be set/changed by any authorized caller (Divine, Character Lifecycle, game extension)

**Character Lifecycle (L4)** — `patronDeityCode` in generational data (nullable)
- The **generational default** — inherited from parents at birth
- Sets the character-level field during character creation, but doesn't force it
- A bloodline's patron deity passes to children naturally through the lifecycle inheritance system
- Nullable — the lifecycle doesn't guarantee a patron. Orphans, godless cultures, characters born outside divine territory may have no generational patron.

```
Birth:
    Character Lifecycle reads parents' patronDeityCode
    → If present: sets newborn's Character.patronDeityCode (inheritable default)
    → If absent: leaves null (no divine patron by birth)

Coming of Age / Ceremony / Conversion:
    Any authorized caller sets Character.patronDeityCode
    → Divine receives event (character.updated with changedFields: [patronDeityCode])
    → Auto-bond logic triggers (see below)

Override:
    Character.patronDeityCode can diverge from the bloodline
    → "My family worships Ares, but Athena chose me"
    → Old bonds dissolved, new bonds formed
```

### Auto-Bond via Divine Actor Registry

Bond formation is **automatic and configuration-driven**, not manual API choreography:

**1. Seed data loaded at startup**: Each deity's bond template is authored as part of the god's seed data — which domains to bond, with what direction and ratio. This is divine actor personality information: "Ares bonds aggressively in war (`AToB`, 0.8); Athena bonds broadly in war and knowledge (`Bidirectional`, 0.3/0.6)."

**2. Runtime registry built from actor loading**: As gods are loaded into actors via Puppetmaster, Divine builds an in-memory registry mapping deity codes to their bond templates. This registry is dynamic — adding a new god to the world automatically makes patron bonding available. Registry invalidated and rebuilt via self-event-subscription when deity actors change (per T9 multi-instance safety).

**3. Events trigger bond creation**: When Divine receives a character event with a changed `patronDeityCode`:
- Look up the deity code in the runtime registry
- If found: auto-initiate seed bonds per the deity's bond template
- If the character already had bonds to a previous patron: dissolve those first
- The bonds are initiated and auto-confirmed (service-to-service, no user consent needed)

```
Registry (built from deity actor seed data at startup):

"ares" → [
    { seedTypeCode: "war",      direction: AToB,          ratio: 0.8 },
    { seedTypeCode: "strength", direction: AToB,          ratio: 0.5 },
]
"athena" → [
    { seedTypeCode: "war",       direction: Bidirectional, ratio: 0.3 },
    { seedTypeCode: "knowledge", direction: Bidirectional, ratio: 0.6 },
]
"hephaestus" → [
    { seedTypeCode: "craft",     direction: Bidirectional, ratio: 0.4 },
    { seedTypeCode: "forge",     direction: BToA,          ratio: 0.2 },
]

Character created with patronDeityCode: "ares"
    → Registry lookup: "ares" found
    → Auto-bond character's "war" seed ↔ Ares' "war" seed (AToB, 0.8)
    → Auto-bond character's "strength" seed ↔ Ares' "strength" seed (AToB, 0.5)
    → Character may not have a "strength" seed yet → bond deferred or seed created
```

**Additional bond formation triggers** remain possible beyond patron deity:
- **God-actor initiative** — A god's ABML behavior decides to bond with a promising mortal (calls Divine API, which calls Seed bond API)
- **Quest/Contract reward** — Completing a divine quest earns a domain bond
- **Player choice** — The guardian spirit directs the character to seek domain-specific bonds beyond their patron
- These create bonds in addition to the patron's auto-bonds

### Impact on Divine's Architecture

With seed bond propagation, the "Path 2: Indirect" flow from the wallet-driven architecture simplifies dramatically:

**Before (without bond propagation):**
```
Character growth → DI listener → Divine identifies matching gods → Divine credits god wallets
```
Divine needs: DI listener implementation, god-to-domain lookup, wallet credit logic, batch worker.

**After (with bond propagation):**
```
Character growth → Seed bond propagation → God's seed grows automatically
```
Divine needs: nothing for this flow. Seed handles it at L2. Divine is purely the orchestration layer for deity CRUD, blessings, and follower management — not for divinity generation plumbing.

The "Path 1: Direct" (prayer/offering → credit god's wallet directly) remains unchanged.

### Genesis Interaction: External Seed Adoption

Deity entities are Genesis entities ("deity_domain" template). Genesis encapsulates the seed — "callers cannot call Seed APIs directly for genesis-managed seeds." This creates a tension: Divine needs to create Seed bonds on the deity's seed, but Genesis doesn't expose the seedId.

**Solution: nullable `seedId` on `/genesis/entity/create`.** Divine creates the seed externally, then passes it to Genesis for lifecycle management:

```
Divine creates deity:
    1. Create "war" seed via /seed/create → gets seedId (Divine holds this)
    2. Create genesis entity via /genesis/entity/create with seedId → Genesis adopts it
    3. Store seedId on DeityModel (Divine's internal state)

Character gets patronDeityCode: "ares":
    1. Divine looks up Ares' DeityModel → has seedId for each domain
    2. Divine calls /seed/bond/initiate between character's seed and god's seed
    3. Bond formed. Propagation active.
```

**Why this works:**
- Genesis doesn't care who created the seed — it calls `Seed.RecordGrowth(seedId, ...)` the same way regardless
- `ISeedEvolutionListener` fires on growth thresholds regardless of seed origin — phase transitions work normally
- Genesis still doesn't expose the seedId in its API responses — encapsulation preserved from Genesis's side
- Divine holds the seedId because it created the seed, not because Genesis leaked it
- Bond-propagated growth from followers feeds the Genesis lifecycle automatically — growth accumulates from any source

**The emergent awakening:** A god literally awakens from accumulated follower activity. Bond-propagated growth from 100 followers fighting → god's seed crosses Stirring threshold → Genesis spawns actor → crosses Awakened threshold → Genesis creates character in PANTHEON realm, binds actor. No one triggers the awakening — it emerges from sufficient growth.

**Validation rules Genesis should enforce on external seeds:**
- Seed type must match template's `seedTypeCode` (reject mismatched types)
- Seed must not already be genesis-managed (prevent double-adoption)
- Initial phase check — if the provided seed already has growth, compute starting phase from template thresholds (entity might start non-Dormant)

**Pattern precedent:** Genesis already separates external creation from internal adoption for physical forms (`/genesis/entity/bind-physical-form`). The nullable seedId follows the same pattern — "I created this externally, adopt it for your lifecycle management."

### Cross-Service Changes Required

#### Seed (L2) — Bond Propagation

1. **Add `PropagationDirection` and `PropagationRatio` to bond model** — new fields on `SeedBondModel`, settable at initiation time
2. **Add `PropagationDirection` enum** to `seed-api.yaml` — `None`, `AToB`, `BToA`, `Bidirectional`, `Mirrored`
3. **Add propagation logic to `RecordGrowth`** — after recording and amplifying, check for active bond with propagation enabled; record `amount × ratio` on partner with `propagated: true` flag
4. **Add `propagated` flag to internal growth recording** — skip bond propagation step when true
5. **Bond dissolution endpoint (#362)** — required so characters can change patron deities
6. **Growth events need batching** — `seed.growth.updated` is per-domain, not batch; high-frequency growth recording at scale will flood events

#### Genesis (L2) — External Seed Adoption

7. **Add nullable `seedId` to `/genesis/entity/create` request** — if provided, Genesis adopts the external seed instead of creating one. Validates type match, not-already-managed, computes initial phase.

#### Character (L2) — Patron Deity Field

8. **Add `patronDeityCode` to Character schema** — opaque string, nullable. Stored on the character record, included in lifecycle events (`character.updated` with `changedFields`). No Character code changes beyond the field — Character doesn't interpret the code.

#### Character Lifecycle (L4) — Generational Inheritance

9. **Add `patronDeityCode` to generational data** — nullable field in lifecycle template/configuration. Character Lifecycle sets the character-level field during birth/creation from parental data. Standard inheritance logic — same pattern as species, realm, etc.

#### Divine (L4) — Auto-Bond Registry and Event Handling

10. **Bond template registry** — in-memory `ConcurrentDictionary` mapping deity codes to bond templates. Built from deity actor seed data during `OnRunningAsync`. Invalidated via self-event-subscription on deity created/updated/deleted events.
11. **Event handler for `character.updated`** — when `changedFields` includes `patronDeityCode`, look up deity code in registry, dissolve old bonds if any, create new bonds per template.
12. **Event handler for `character.created`** (or Character Lifecycle birth event) — if `patronDeityCode` is set, auto-bond per template.
13. **Deity creation flow** — create seed externally via Seed API, pass seedId to Genesis, store seedId on DeityModel for bond operations.

### Open Questions

- **`Mirrored` semantics**: Is this "ratio 1.0 both directions" (same as `Bidirectional` with ratio 1.0) or "shared growth pool" (both seeds see identical state)? The former is simpler to implement; the latter is architecturally different. Marked with `(?)` — may not be needed for v1.
- **Performance at scale**: 100K characters × ~3 domain seeds × bond propagation per growth recording. Each propagation is one additional `RecordGrowth` call (with lock acquisition on the partner seed). Need to validate this doesn't bottleneck on god-seed locks (many characters bonded to the same god seed). Possible mitigation: batch propagation credits to god seeds via a worker rather than inline.
- **Missing seeds at bond time**: When auto-bonding, the character may not have all the seeds the deity template expects (e.g., no "strength" seed yet). Options: (a) create the seed automatically, (b) defer bond until seed exists, (c) bond what exists, add more bonds later via `ISeedEvolutionListener` or seed creation events.
- **Patron change cost**: Should changing patron deity have a mechanical/narrative cost? (Blessing revocation, bond strength reset, divine displeasure event.) This is gameplay design — the infrastructure supports any policy, but the default behavior needs a decision.

---

## Service Composition

### Primary Flow: Bond-Propagated Growth

```
┌──────────────────────────────────────────────────────────────────────┐
│ Divinity Generation: Seed Bond Architecture                         │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  SOURCES (anything can credit a character's wallet)                  │
│  ┌──────────┐ ┌────────────┐ ┌───────────┐ ┌──────────────────┐    │
│  │ Game     │ │ Quest/     │ │ God-actor  │ │ Currency         │    │
│  │ Engine   │ │ Contract   │ │ behavior   │ │ autogain worker  │    │
│  └────┬─────┘ └─────┬──────┘ └─────┬─────┘ └───────┬──────────┘    │
│       └──────────────┴──────────────┴───────────────┘               │
│                              │                                       │
│              Character's currency wallet credit                      │
│                              │                                       │
│  ┌───────────────────────────┼───────────────────────────────────┐   │
│  │ Genesis (L2)              │                                   │   │
│  │                           ▼                                   │   │
│  │         ICurrencyTransactionListener (DI)                     │   │
│  │              wallet → entity → template → growthMappings[]    │   │
│  │              amount × ratio = growth (direction: Credit)      │   │
│  │                           │                                   │   │
│  │                  Seed.RecordGrowth()                           │   │
│  │                    on character's "war" seed                   │   │
│  └───────────────────────────┼───────────────────────────────────┘   │
│                              │                                       │
│  ┌───────────────────────────┼───────────────────────────────────┐   │
│  │ Seed Bond Propagation (L2)│                                   │   │
│  │                           ▼                                   │   │
│  │     Character's "war" seed ══bond══ God's "war" seed          │   │
│  │     (AToB, ratio: 0.8)                                        │   │
│  │                           │                                   │   │
│  │     1. Amplify character: amount × 1.5 (existing)             │   │
│  │     2. Propagate to god:  amount × 0.8 (NEW)                  │   │
│  │        → RecordGrowth(propagated: true) on god's seed         │   │
│  │        → Normal events fire (seed.growth.updated, phase)      │   │
│  │        → NO re-propagation (propagated flag)                  │   │
│  │     3. Bond strength grows (existing)                         │   │
│  │     4. Partner activity reset (existing, prevents decay)      │   │
│  └───────────────────────────┼───────────────────────────────────┘   │
│                              │                                       │
│  ┌───────────────────────────┼───────────────────────────────────┐   │
│  │ God Perception (L2/L4)    │                                   │   │
│  │                           ▼                                   │   │
│  │  God-actor perceives via variable providers:                  │   │
│  │    ${seed.war.combat.depth}    — domain power (from growth)   │   │
│  │    ${currency.divinity.balance} — spendable resources         │   │
│  │    ${personality.*}             — character brain traits       │   │
│  │  Makes autonomous GOAP decisions:                             │   │
│  │    grant blessing, intervene, manifest avatar                 │   │
│  └───────────────────────────────────────────────────────────────┘   │
│                                                                      │
│  INDEPENDENT CONSUMERS (no contention with divine flow)              │
│  ┌───────────────┐  ┌──────────────────┐  ┌─────────────────────┐   │
│  │ Quest         │  │ Leveling/Status  │  │ Analytics           │   │
│  │ (wallet debit │  │ (seed phase      │  │ (event observer     │   │
│  │  for obj.)    │  │  transitions)    │  │  FULLY OPTIONAL)    │   │
│  └───────────────┘  └──────────────────┘  └─────────────────────┘   │
└──────────────────────────────────────────────────────────────────────┘
```

### Secondary Flow: Direct Prayer / Offering

```
Worshipper prays → credits god's wallet directly (faith currency)
    ↓ ICurrencyTransactionListener → Genesis growth mapping
    God's seed grows from wallet credit
    God-actor perceives balance + growth → decides autonomously
```

### Multi-Domain Bonding

```
Character's seeds:                          God seeds:
    "war" seed      ══bond(AToB, 0.8)══▶  Ares' "war" seed
    "craft" seed    ══bond(Bidir, 0.4)══▶  Hephaestus' "craft" seed
    "devotion" seed ══bond(AToB, 0.6)══▶  Apollo's "devotion" seed
    "nature" seed   ──(no bond)──          (no patron for nature)
```

---

## Impact on Divine Schemas

### Events Schema Changes

**Remove** `analytics.score.updated` subscription from `divine-service-events.yaml`:

```yaml
# REMOVE — Analytics is optional, cannot be in game-mechanic critical path
x-event-subscriptions:
  - topic: analytics.score.updated
    event: AnalyticsScoreUpdatedEvent
    handler: HandleAnalyticsScoreUpdated
```

**No replacement subscription needed.** Seed bond propagation handles the mortal→god growth flow entirely at L2. Divine does not need to listen for growth events, look up matching gods, or credit wallets manually.

### Dependency Changes

| Remove | Add | Reason |
|--------|-----|--------|
| `IAnalyticsClient` (L4 soft) | Nothing | Bond propagation handles divinity generation at L2 |

Divine's remaining dependencies are unchanged — it still needs `ICurrencyClient` (for divinity balance queries, explicit credit/debit), `ISeedClient` (for deity seed operations), etc. But the **divinity generation pipeline** is no longer Divine's responsibility.

### Configuration Changes

**Remove**:
- Any domain-to-analytics mapping config that was planned (#636 dissolved)

**Remove or repurpose**:
- `DivinityGenerationWorkerIntervalSeconds` — the batch worker for analytics-driven generation may no longer be needed. If direct prayer/offering credits go through Currency directly, and domain growth goes through bond propagation, the Redis queue + worker pattern may be unnecessary.

**Keep**:
- `DivinityCostMinor/Standard/Greater/Supreme` — blessing cost tiers (unchanged)
- `DivinityGenerationMultiplier` — could be repurposed as a global modifier if needed, or removed

### Implementation Map Changes

- `HandleAnalyticsScoreUpdatedAsync` → **removed entirely** (no analytics subscription)
- `DivinityEventModel` Redis store → **potentially removed** (no queue needed if bond propagation handles growth)
- `DivineDivinityGenerationWorker` → **potentially removed** (batch worker has no input source)
- `divine-divinity-events` state store → **potentially removed** from `state-stores.yaml`

Divine becomes significantly thinner: deity CRUD, blessing orchestration, follower management, attention mechanics, cleanup. No divinity generation plumbing at all.

---

## Open Design Questions

### Q1: How Does Divine Know Which Gods to Credit? — RESOLVED

**Answer: Seed bond propagation.** The character's domain seed is bonded to a specific god's seed. Bond propagation handles growth transfer automatically at L2. No Divine listener, no god lookup, no manual wallet crediting. See [Seed Bond Propagation](#seed-bond-propagation-the-divine-channel) for full design.

The follower-based, realm-based, and attention-based strategies are no longer candidates for the primary divinity generation flow. They may still serve other purposes (follower = social relationship, attention = god-actor perception, realm = narrative scope).

### Q2: Seed Growth Events — Do They Exist at Sufficient Granularity?

Seed currently publishes `seed.phase.changed` (phase transitions). Divinity generation needs growth-level granularity, not just phase-change spikes. Required:
- A `seed.growth.accumulated` event (or batch equivalent)
- Self-subscription for cache invalidation
- DI listener interface for co-located consumers

If seed growth events don't exist, they need to be added to `seed-service-events.yaml`. If they exist but aren't batch, make them batch.

### Q3: Deity Deprecation Lifecycle (T31) — RESOLVED

**Answer: Category A (deprecate + merge).** Deities are world-building definitions referenced by blessings and followers. When a god "dies" or fades, followers merge to another deity — narratively rich ("Ares fell, his followers scattered to Athena and Apollo").

Schema impact:
- **Add endpoints**: `deprecateDeity`, `undeprecateDeity`, `mergeDeity` (using shared `MergeDeprecatedRequest`/`MergeDeprecatedResponse` from `common-api.yaml`)
- **Add deprecation fields** to deity lifecycle model: `IsDeprecated`, `DeprecatedAt`, `DeprecationReason` (triple-field per T31)
- **Add `includeDeprecated`** boolean param (default: false) to `ListDeitiesRequest`
- **Add `IDeprecateAndMergeEntity`** marker interface to `DivineService`
- **Delete requires deprecated**: `DeleteDeity` returns BadRequest if `IsDeprecated == false`
- **`Archived` enum value**: Likely redundant — deprecation triple-field replaces it. Consider removing from `DeityStatus` enum (leaving `Active | Dormant`). Or keep if Archived serves a different semantic (a god that was deprecated then fully cleaned up).
- **Merge auto-rebonds**: When merging deity A → deity B, the merge should dissolve follower seed bonds to A and create new bonds to B per B's bond template. This is Divine's merge handler responsibility.

### Q4: Batch Lifecycle Events for Blessings (#655) — RESOLVED

**Answer: Yes, use `batch: true`.** Regional watchers grant/revoke blessings continuously. Follows the established pattern (Item instances, Status instances). Affects `divine-service-events.yaml` — add blessing entity to `x-lifecycle` with `batch: true`.

### Q5: Blessing Entity Existence Validation (#675) — RESOLVED

**Answer: Skip validation.** Match the established codebase pattern. All 6 L2 services that accept polymorphic `entityId + entityType` references (Collection, Relationship, Seed, Currency, Status, Escrow) skip entity existence validation — callers are trusted services (`x-permissions: []`). Follower registration validates character existence because it uses typed `characterId` (non-polymorphic, different pattern). Deep dive and implementation map updated. #675 can be closed.

### Q6: DeityPersonalityTraits vs Character Personality — RESOLVED

**Answer: Both exist, they serve different purposes. Rename to `DivineAffectations` (or `DivineAttributes`).**

- **`DivineAffectations`** (renamed from `DeityPersonalityTraits`) = divine-specific behavioral configuration authored by game designers. Controls how the god-actor makes divine decisions: blessing generosity, jealousy triggers, attention bias, temperament. These are seed data — set at creation, potentially updated by designers. Marked `sensitive` in x-lifecycle (excluded from broadcast events).
- **Character Personality** (`${personality.*}`) = emergent personality on bipolar axes that evolves over time via the standard Character Personality service. Activates when the god becomes a character brain in the PANTHEON system realm.

These are complementary. The rename prevents confusion between "personality traits" (the field) and "personality" (the variable provider).

Schema impact: Rename `DeityPersonalityTraits` → `DivineAffectations` in `divine-api.yaml` (type name, field names on request/response models, `sensitive` list in events schema). Consider whether the sub-fields change (`temperament`, `attentionBias`, `generosity`, `jealousy` are all still valid for the renamed type).

### Q7: Avatar Manifestation Endpoints — RESOLVED

**Answer: Defer.** Avatar manifestation is a rich feature that deserves its own implementation phase. Core Divine service (deity CRUD, blessings, followers, attention, auto-bonding) ships first. Avatar endpoints are purely additive — new endpoints, new model fields, no breaking changes. Keep in deep dive as planned Extension #11.

### Q8: `code` Mutability in UpdateDeity — RESOLVED

**Answer: Codes are immutable after creation.** Consistent with species codes, seed type codes, realm codes, and other Bannou entities. Deity codes are referenced by seed data, ABML behaviors, and the patron deity registry — changing them would break configuration references.

Schema impact: None (UpdateDeityRequest already lacks a `code` field). Implementation map impact: Remove the code-change logic from `UpdateDeity` pseudocode (lines 200-203 in current map).

---

## Related Documents

- [DIVINE.md](../plugins/DIVINE.md) — Divine plugin deep dive
- [DIVINE.md (map)](../maps/DIVINE.md) — Divine implementation map
- [SEED.md](../plugins/SEED.md) — Seed deep dive (bonds, growth, propagation)
- [SEED.md (map)](../maps/SEED.md) — Seed implementation map (bond lifecycle, growth recording)
- [GENESIS.md](../plugins/GENESIS.md) — Genesis deep dive (wallet-to-seed growth mechanism)
- [CHARACTER.md](../plugins/CHARACTER.md) — Character deep dive (patronDeityCode field)
- [CHARACTER-LIFECYCLE.md](../plugins/CHARACTER-LIFECYCLE.md) — Character Lifecycle deep dive (generational patron inheritance)
- [ACTOR-BOUND-ENTITIES.md](ACTOR-BOUND-ENTITIES.md) — Unified cognitive progression pattern
- [VISION.md](../reference/VISION.md) — Content flywheel, living worlds, emergent-over-authored
- [PLAYER-VISION.md](../reference/PLAYER-VISION.md) — Generational play, progressive agency
- [SERVICE-HIERARCHY.md](../reference/SERVICE-HIERARCHY.md) — DI listener safety rules

## Issue Tracking

### Divine Issues

| Issue | Status After This Design |
|-------|--------------------------|
| [#636](https://github.com/beyond-immersion/bannou-service/issues/636) | **Dissolves** — domain-to-analytics mapping replaced by seed bond propagation |
| [#655](https://github.com/beyond-immersion/bannou-service/issues/655) | **Unchanged** — batch lifecycle events for blessings still needed |
| [#675](https://github.com/beyond-immersion/bannou-service/issues/675) | **Unchanged** — blessing entity existence validation still open |
| [#415](https://github.com/beyond-immersion/bannou-service/issues/415) | **Still blocks** entity-agnostic Minor/Standard blessings |

### Seed Issues (Prerequisites)

| Issue | Relevance |
|-------|-----------|
| [#362](https://github.com/beyond-immersion/bannou-service/issues/362) | **Required** — bond dissolution endpoint needed for changing patron deities |
| New issue needed | **Required** — add `PropagationDirection` + `PropagationRatio` to bond model |
| New issue needed | **Required** — add `propagated` flag to RecordGrowth for anti-cascade |
| Existing growth events | **Required** — batch `seed.growth.updated` events for scale |

### Genesis Issues (Prerequisites)

| Issue | Relevance |
|-------|-----------|
| New issue needed | **Required** — add nullable `seedId` to `/genesis/entity/create` for external seed adoption |

### Character Issues (Prerequisites)

| Issue | Relevance |
|-------|-----------|
| New issue needed | **Required** — add `patronDeityCode` (opaque string, nullable) to Character schema and lifecycle events |

### Character Lifecycle Issues (Prerequisites)

| Issue | Relevance |
|-------|-----------|
| New issue needed | **Required** — add `patronDeityCode` to generational data with parental inheritance |
