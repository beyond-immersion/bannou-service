# Streaming Economy: Cross-Realm Audience Attention as Economic Substrate

> **Type**: Design
> **Status**: Aspirational
> **Created**: 2026-04-28
> **Last Updated**: 2026-04-28
> **North Stars**: #1, #2, #4, #5
> **Related Plugins**: Audience, Currency, Realm, Item, Inventory, Contract, Escrow, Seed, Agency, Actor, Character, Character-Personality, Disposition, Hearsay, Faction, Storyline, Workshop
> **Prerequisites**: Audience Service (in arcadia-kb planning), Disposition (aspirational), Hearsay (aspirational), Realm cross-bleed model from LEYLINE-COMPOSITION-FRAMEWORK

## Summary

Defines the economic architecture by which in-world streaming activity in primary play realms (Arcadia, Fantasia) generates audience attention that produces economic value — but rewards the *streamer's Omega-side profile and currency*, not the source realm's economy. This is a deliberate cross-realm asymmetry: Omega is the meta-dashboard / cyberpunk audience layer; Arcadia and Fantasia are the content sources whose activity feeds Omega's social and economic substrate. The doc also specifies a constrained cross-realm sponsorship mechanism by which audience-player guardian spirits can carry preferences from Omega-resident perception (where they witnessed a streamed performance) into their own Arcadia/Fantasia character's selection-list UX — highlighting the streamer's name when the audience-player's character is already in a position to make a selection, without ever generating an "actively go help this person" command. Composes entirely from existing and aspirational services (Audience, Currency, Realm, Spirit, Agency, Disposition, Contract); no new plugins required.

---

## Problem Statement

The arcadia-kb design already specifies an Audience Service Architecture and Advanced Audience Dynamics — a substrate of personality-flagged data objects representing streaming audience members, with hype-train mechanics, multi-stream attention competition, and follow/subscribe conversion dynamics. What the existing design does *not* specify is:

1. **Where the economic value lives.** When a Clover-style party in Arcadia successfully streams a dungeon delve, who pays whom in what currency, and who keeps the proceeds?
2. **How audience attention translates to streamer reward.** The Audience Service tracks attention; the economy must convert attention into something the streamer cares about.
3. **The asymmetric realm relationship.** Omega has the VR chair / meta-dashboard / cyberpunk substrate; Arcadia and Fantasia produce content that's consumed in Omega. This is by design — Omega is the audience-side realm — but the existing docs don't formalize the resulting economic asymmetry.
4. **The sponsorship loop.** In real-world streaming and pro sports, audience attention attracts equipment sponsorship (Federer's racket prototypes, LeBron's signature shoes, Star Ocean 2 arena tournaments where vendors sponsor fighters with their gear). This is a well-attested pattern that should be available in Arcadia/Fantasia but is not currently specified.
5. **Cross-realm influence with respect to character autonomy.** A player whose audience-side spirit (in Omega) develops a preference for a streamer cannot directly tell their Arcadia/Fantasia character to "go help this streamer" — that would violate the "characters are independent entities" principle. But some channel of cross-realm preference transmission is needed, or the audience-side preference has no in-game economic consequence.

The framework presented here resolves these by specifying:

> **Streamed activity in Arcadia/Fantasia generates Omega-side currency for the streamer's spirit (the player), not in-realm currency for the streamed character. Cross-realm sponsorship is mediated by the spirit's preference manifesting as UX highlighting in the audience-player's Arcadia/Fantasia character's selection lists — the spirit can star a name, never command a search.**

---

## The Asymmetric Realm-Economic Design

### Why the Asymmetry Is Intentional

Omega is, by design, the cyberpunk meta-dashboard. The VR chair is the in-fiction grounding for the audience layer — players in Omega are explicitly streaming-consumers as a primary mode of engagement. The Audience Service Architecture lives there because that's where the audience IS, diegetically.

Arcadia and Fantasia are primary play realms with their own internal economies (NPC-driven supply and demand, per the VISION.md "Economy as Living System" model). Those economies serve characters living in those realms — merchants trade, craftsmen produce, adventurers spend coin on equipment. They do not, by themselves, have a structural reason to reward streamed-content broadcasting.

The audience-side reward must therefore come from where the audience exists: **Omega**. A streamer whose Arcadia character clears a dungeon and broadcasts the delve is rewarded in Omega-side currency (paid out to the spirit's Omega seed account), accumulating Omega-side reputation/profile. The Arcadia character experiences mild fame in-world (Hearsay propagation, NPC recognition) but does not become wealthy in Arcadia coin from the streaming itself.

This asymmetry preserves the integrity of the in-realm NPC-driven economies (they don't get distorted by an external "audience reward firehose") while giving the streamer-player a genuine progression axis tied to the meta-dashboard layer.

### What Streamers Earn in Omega

| Currency / Reward | Mechanism |
|-------------------|-----------|
| **Omega credits** | Audience Service computes per-stream attention metrics; converts to credit drops with a configurable yield curve |
| **Omega reputation / follower count** | Standard streaming-platform analog; persists across streams |
| **Omega-side equipment unlocks** | Reputation thresholds unlock cosmetic / VR-chair / dashboard customization items in Omega |
| **Subscription tiers** | Audience members can establish recurring micro-payment relationships per the existing Follow/Subscribe Conversion design in Advanced Audience Dynamics |
| **Hype-train burst rewards** | World-first / climactic content generates Omega-credit bursts above the baseline yield |

Omega credits are convertible to Omega's internal economy (cyberpunk dashboard infrastructure, in-Omega cosmetics, social features). They are **not directly convertible** to Arcadia or Fantasia coin — preserving the in-realm economy integrity.

### What the Streamed Character Experiences In-Realm

The Arcadia or Fantasia character whose actions are being streamed experiences:

- **Hearsay propagation**: Their deeds get talked about. NPCs in the local region develop high-confidence DirectObservation beliefs; distant NPCs receive Rumor-level distorted versions per the standard Hearsay model.
- **Recognition events**: Other characters who have witnessed their streams (via in-world broadcast technology) may recognize them in-person — modulated by the Hearsay recognition-confidence threshold.
- **Mild status effect** (optional): A Status entry indicating "Known Streamer" can affect NPC interaction defaults (price adjustments, conversation hooks, faction-disposition modifiers) — but this is in-realm Hearsay-driven, not direct Omega payment.
- **Nothing automatic**. The character does not receive Arcadia coin payouts. They do not magically attract bookings. They do not receive equipment from sponsors automatically.

The character lives in their own realm with their own economy. The fact that another realm's audience finds their adventures entertaining is, from the character's local perspective, irrelevant to their household's grocery bills.

---

## In-World Broadcast Technology

### Broadcast Artifacts

Streaming is mediated by enchanted broadcast artifacts — small flying constructs (the Aparida Camera-kun pattern, but with our own naming and grounding). Per the technology principle "All technology uses sequential magical transformations via enchantment," broadcast artifacts are layered enchantments that:

1. **Capture** local sensory data (visual, auditory, possibly olfactory at higher tiers)
2. **Encode** the captured data into pneuma-channeled information packets
3. **Transmit** via the leyline network to designated sanctuary-equivalent receivers
4. **Project** received content into Omega's VR-chair substrate (Omega is downstream of cross-realm bleed leylines from primary realms; broadcast artifacts ride those leylines)

### Cross-Realm Transmission as Leyline-Enabled

Per LEYLINE-COMPOSITION-FRAMEWORK.md, primary realms (Arcadia, Fantasia) have cross-realm bleed leylines that flow into Omega at low percentages. Broadcast artifacts are explicitly designed to encode information into pneuma packets that travel along these bleed leylines, surfacing in Omega via dedicated reception infrastructure (in Omega, this manifests as the VR-chair audience interface).

The leyline-mediated transmission is itself in-world, observable, and constrained:

- **Bandwidth limits**: A bleed leyline can only carry so much pneuma packet density. High-traffic streaming events (climactic battles, world-first dungeon clears) compete for leyline bandwidth. The Audience Service's adaptive check-in interval scaling (1 minute → 5 minutes during high load) maps to this directly.
- **Geographic constraints**: Broadcast artifacts must operate within range of cross-realm-bleed leyline access points. Some Arcadia regions are poor for streaming because they're leyline-poor; this creates real geography of streaming opportunity.
- **Quality scaling**: Higher-tier broadcast artifacts encode richer pneuma packets (better Omega-side fidelity); lower-tier artifacts produce blurry, low-resolution Omega-side renderings.
- **Tampering vulnerability**: Broadcast artifacts can be jammed, hacked, or counterfeited. This creates plot-relevant intrigue (rival streamers, faction-driven information warfare).

### Broadcast Artifact Item Type

Broadcast artifacts are Item instances composed from existing primitives:

| Property | Source service | Detail |
|----------|----------------|--------|
| Item template | Item (L2) | Category: enchanted device; subcategory: broadcast artifact |
| Enchantment chain | Affix / Enchantment | Multi-stage enchantment per the existing per-stage enchantment design |
| Operating cost | Currency / Resource | Consumes pneuma reagents per stream-time unit (refilled at sanctuary nodes or via ritual) |
| Tier-determined fidelity | Seed / Affix | Broadcast artifact's craftsman's Seed level determines the artifact's quality tier |
| Permission to broadcast | Faction / Contract | Some realms / regions require broadcast licensing (creates faction-adjacent gameplay) |
| Audience targeting | Audience Service | The artifact's broadcast targeting profile determines which Omega audience subsegments receive the stream |

Broadcast artifacts are themselves a craft category. NPCs can specialize in producing them. Higher-tier artifacts are more valuable. A region with master-tier broadcast-artifact craftsmen becomes a streaming hub.

---

## Audience-Side Economics

### Omega Audience Members as Currency-Generating Substrate

The arcadia-kb Audience Service Architecture defines audience members as lightweight data objects with personality flags, follower lists, availability windows, and engagement levels. The streaming economy adds an economic layer:

- Each audience member has an Omega-credit budget (per game-day, per game-week — configurable per realm)
- When an audience member views a stream, their attention is computed per the existing weight-based interest calculation
- Attention duration + interest level → micro-credit drops to the streamer at configurable rates
- Active engagement (subscribe, super-chat-equivalent, follow) produces above-baseline payments

This mirrors real-world streaming platform economics (Twitch bits, YouTube super-chats, Patreon subscriptions) without being identical to any single one. The Audience Service's existing design already supports the input layer; the economy adds the output layer.

### Hype-Train Economics

Per Advanced Audience Dynamics, hype-train events generate exponentially higher excitement and cross-stream pull. The economic layer maps:

| Event type | Payment yield curve |
|------------|--------------------|
| Personal milestone (quest completion, level-up) | Linear yield with engagement |
| World-first dungeon clear | Exponential burst yield; sustained follower growth |
| Cross-stream collaboration | Yield split among collaborating streamers |
| Disaster / failure (entertaining death) | Reduced but non-zero yield (audience watches failure too) |

The yield curves are calibration parameters, not hard rules. Realm administrators (or god-actors representing commerce-domain deities) can tune them per-realm to encourage specific kinds of content production.

### Subscriber Substrate

Subscribers (audience members who have established a Follow/Subscribe relationship per the existing model) provide:

- Predictable baseline payment (analog of a subscription tier)
- Priority audience access during hype-train events
- Higher per-unit attention yield
- Persistent relationship that survives stream gaps

Subscriber relationships are stored per the existing Audience Service design with no economic-layer changes required to the base data model.

### Currency Flow Diagram

```
Arcadia/Fantasia character does interesting thing
      │
      ▼
Broadcast artifact captures and encodes pneuma packet
      │
      ▼ (cross-realm bleed leyline)
      │
Omega audience members receive packet via VR-chair substrate
      │
      ├── Attention compute (Audience Service)
      ├── Hype-train trigger detection (Audience Service)
      └── Subscriber priority resolution (Audience Service)
      │
      ▼
Per-attention micro-credit yield computation
      │
      ▼
Omega-credit drop to streamer's Omega seed account
      │
      ▼
Streamer (player, via Omega seed) accumulates:
  - Omega credits
  - Omega reputation
  - Follower count
  - Subscription pipeline
```

The flow is unidirectional. Omega audience pays Omega-side credits. The Arcadia/Fantasia character does not see Arcadia coin appearing in their pocket from this flow. Their realm-economy participation is entirely separate.

---

## The Cross-Realm Sponsorship Mechanic

### The Constraint

The audience-player's spirit (in Omega) may witness a streamer's content and develop a genuine preference. That preference matters — it represents real player engagement. But the audience-player's *Arcadia or Fantasia character* (a different seed managed by the same spirit) cannot be commanded to "go actively seek out and help the streamer." That would violate the "characters are independent entities" principle and the agency-as-collaboration model from PLAYER-VISION.md.

The mechanism must:
- Carry the spirit's preference across realm boundaries (the spirit perceives both seeds, so this is structurally possible)
- Manifest in the audience-player's Arcadia/Fantasia character's experience without direct command
- Operate only when the character is already in a position to make a selection
- Be observable to the character (the spirit's preference is not a hidden manipulation)
- Be refusable at the character level (autonomy preserved)

### The UX Highlighting Pattern

When the audience-player's Arcadia/Fantasia character is in a scenario where they're choosing from a list of candidates — for example:

- Choosing which adventurer party to hire for an escort job
- Selecting a craftsman to commission a piece of equipment from
- Picking a vendor for a supply contract
- Identifying a recipient for a charitable donation
- Voting for a guild leader from a slate
- Designating an heir or beneficiary

…the spirit's stored preferences influence the UX presentation:

- **Highlighted name**: The streamer's name (or the streamer's character's name) is visually emphasized in the selection list — starred, bolded, presented at the top, given a subtle glow indicating spirit preference
- **Tooltip annotation**: When the character considers the highlighted option, the UX surfaces "your spirit feels drawn to this name" or equivalent in-fiction phrasing
- **No automatic selection**: The character still chooses. The character can choose differently. The spirit cannot override the character's judgment.
- **In-fiction grounding**: From the character's perspective, this is "intuition" or "something feels right about this name." Characters with high spiritual perception (Mapping/Agency mature tiers) understand they're being influenced by their guardian spirit; characters at lower perception just feel drawn.

### What the Spirit Cannot Do

Crucially, the spirit cannot:

- Send the character to actively search for the streamer
- Generate a quest goal "find and help the streamer"
- Override character disposition (a character whose disposition argues against the streamer's interests will choose differently even with spirit preference flagged)
- Affect characters who haven't already encountered the candidate set (no remote influence on people the character has never heard of)
- Persist preference across realm composition shifts that destabilize the spirit's connection (saeculum drift can wash out preferences over centuries)

The constraint is that the spirit operates within selection moments that are *already* under way. The character has already pulled up the list of candidates for a reason of their own. The spirit annotates that list. The character still decides.

### Why This Works Structurally

This pattern composes from existing Bannou primitives:

| Primitive | Role |
|-----------|------|
| **Spirit / Account** | Holds preference data across seeds |
| **Seed (L2)** | Each seed can read spirit-level preference flags |
| **Agency (L4)** | Determines which UX modules render preference annotations and at what fidelity |
| **Audience Service** | Source of preferences (the spirit's audience-side history of stream engagement) |
| **Disposition (L4)** | Character disposition still drives selection; spirit preference is one input weight, not a command |
| **Hearsay (L4)** | If the character has independent Hearsay evidence about the candidate, that combines with spirit preference (the streamer may have a positive Hearsay reputation in regions where their streams reached, layered onto the spirit's annotation) |
| **UX Capability Manifest** | Determines whether the character's UX renders the preference annotation at all — low-fidelity spirits can't communicate preference clearly; high-fidelity spirits surface specific rationale |

Zero new plugins. The mechanism is a UX layer over existing data.

### Audience Members as Cross-Realm Bridges

The structural insight: an audience member is a player whose *Omega seed* is engaged with the stream. That same player has *other seeds* (in Arcadia or Fantasia). The spirit unifies them. So when the spirit forms a preference in Omega context, that preference is structurally available to every other seed the spirit owns.

This means the cross-realm sponsorship mechanism doesn't violate any architecture — it leverages the existing fact that the spirit IS the cross-seed bridge. The spirit always carried preferences across seeds. The streaming economy formalizes one specific preference channel (audience engagement) as a UX-affecting input.

---

## Sponsor Equipment Loop

### The Real-World Pattern

Tennis racket manufacturers sponsor Federer with prototype rackets in exchange for visibility AND for performance data — Federer's actual usage shapes the next consumer model. F1 telemetry is even more explicit: the driver IS the test instrument. NBA shoe deals incorporate athlete feedback into signature lines. Star Ocean 2's arena tournaments allowed vendors to sponsor fighters per-tournament with their gear, with the fighter required to use the sponsor's equipment for the duration.

This is a well-attested visibility-feedback loop:

1. Performer accumulates audience attention (visibility)
2. Equipment maker offers performer free / cheap top-tier equipment
3. Performer uses the equipment publicly (during streams)
4. Equipment maker harvests usage data (performance metrics, fatigue rates, failure modes, performer commentary)
5. Equipment maker incorporates feedback into next-generation product
6. Audience associates the equipment with the performer's success (consumer demand follows)
7. Performer's continued success continues the cycle

### How This Composes In Arcadia/Fantasia

The loop maps cleanly onto existing Bannou primitives:

| Real-world element | Bannou composition |
|--------------------|-------------------|
| Performer | Streamer (player + Arcadia/Fantasia character) |
| Equipment maker | NPC craftsman or workshop with Disposition drive `master_craft` and `gain_wealth` |
| Visibility metric | Audience Service attention metrics (cross-readable from the streamer's stream history) |
| Sponsorship offer | Contract instance offering equipment loan/grant in exchange for usage demonstration |
| Equipment provided | Item instance, optionally with Faction-tagged origin marker (visible during streams as "made by X workshop") |
| Performance data | Equipment-use events flowing back to the workshop's Character History (NPCs can read this) |
| Feedback incorporation | Workshop's next production cycle uses the events as design inputs (Workshop blueprint parameters can reference accumulated event data) |
| Consumer demand | Disposition drives in audience characters develop interest in the equipment after streams |

### Per-Stream-Event vs. Permanent Sponsorships

The Star Ocean 2 wrinkle (per-tournament sponsorship-bound equipment) maps to per-stream-event Contract terms:

| Term type | Contract clause | Constraint mechanic |
|-----------|----------------|---------------------|
| **Tournament-bound** | "Use only sponsor's equipment for events tagged X" | Equipment-swap restriction during tagged events |
| **Time-bound** | "Use sponsor equipment for N game-days" | Status effect blocking equipment swaps |
| **Visibility-bound** | "Display sponsor logo during streams reaching ≥ N viewers" | Cosmetic Item attachment with visibility requirement |
| **Performance-bound** | "Achieve ≥ N% win rate during sponsorship period" | Contract milestone with bonuses or termination on failure |
| **Exclusive** | "Do not accept competing sponsorships during contract" | Faction-disposition exclusivity flag |

These compose from existing Contract clause primitives. Each clause is enforceable through standard Contract-FSM transitions. The Contract service already supports milestone-based fulfillment.

### NPC-Initiated Sponsorships

The pattern naturally generates emergent NPC-initiated sponsorships. An NPC craftsman with the right Disposition drives perceives via Hearsay that a streamer is gaining audience. GOAP planning evaluates: "Sponsoring this streamer costs X equipment + Y opportunity cost; expected gain is Z visibility-driven sales increase + W usage data for next product. Net positive?" If yes, the NPC approaches the streamer with a sponsorship offer.

This means streamers naturally accumulate sponsorship offers as their audience grows, without designer authoring. The economic loop generates content automatically — every sponsorship is a relationship, every relationship can be developed into narrative, every successful product line has a streamer-sponsor pair behind it that the Realm History records.

---

## Service Composition

### Implemented and In-Design Services

| Service | Role |
|---------|------|
| **Audience Service** (in arcadia-kb) | Audience member objects, attention metrics, hype-train detection, follow/subscribe pipeline |
| **Currency (L2)** | Per-realm currency wallets; Omega-credit drops, conversion gating |
| **Realm (L2)** | Per-realm currency definitions; cross-realm conversion rules |
| **Item (L2)** | Broadcast artifact templates and instances |
| **Inventory (L2)** | Broadcast artifact custody |
| **Contract (L1)** | Sponsorship agreements with FSM-enforced terms |
| **Escrow (L4)** | Atomic equipment-for-visibility exchanges |
| **Seed (L2)** | Spirit cross-seed preference storage |
| **Agency (L4)** | UX capability manifest determines whether sponsorship-flag annotations render |
| **Disposition (L4)** | Drives that motivate sponsorship offers; drives that respond to streamer reputation |
| **Hearsay (L4)** | Reputation propagation; provides additional evidence layer beyond direct stream attendance |
| **Faction (L4)** | Sponsorship faction relationships, broadcast licensing authorities |
| **Workshop (L4)** | Broadcast artifact production (lazy evaluation as with all production) |
| **Storyline (L4)** | Generates narrative seeds from sponsorship arcs |
| **Character / Character-Personality / Character-History** | Streamer reputation tracking, NPC sponsorship decision-making, sponsor-streamer relationship history |
| **Actor (L2)** | NPC GOAP planning evaluates sponsorship opportunities |

### What This Framework Adds

| Addition | Service | Type |
|----------|---------|------|
| Omega-credit yield curves per stream-attention pattern | Currency + Audience | Configuration data |
| Cross-realm conversion gating (Omega credits ≠ realm coin) | Currency / Realm | Conversion rule schema |
| Broadcast artifact item template family | Item | Seed data |
| Broadcast artifact pneuma-cost mechanic | Resource | Cost rule |
| Sponsorship Contract clause types | Contract | Clause type schema |
| Spirit-cross-seed preference flag schema | Seed | Schema field |
| UX selection-list highlighting render hook | Agency | UX manifest entry |
| Audience-engagement-to-spirit-preference write hook | Audience Service + Seed | Integration point |
| ABML behaviors for NPC sponsorship offer generation | Actor + content | Behavior authoring |

### What Is NOT Changed

- **No new plugins**. Every component composes from existing or in-design services.
- **No new currency types invented**. Omega already has its own currency (the meta-dashboard layer); this framework just specifies the yield rules.
- **No new character autonomy violations**. The cross-realm sponsorship mechanic operates within existing selection moments. It does not generate quest goals.
- **No new realm primitives**. The cross-realm bleed leyline architecture (per LEYLINE-COMPOSITION-FRAMEWORK) already provides the cosmological substrate for cross-realm transmission.

---

## Failure-Pattern Check

| Pattern | Risk | Status |
|---------|------|--------|
| **Hive-Coordination Response Pattern** | Audience-player characters become structurally unable to want anything except what their spirit/streamer-preference dictates | ✅ **Defended**: spirit preference is one input among many; Disposition + Hearsay + character personality combine; the character can refuse. The mechanism only annotates lists the character is *already* deciding from. |
| **Combat-capability monetization** | Streaming combat directly pays the character in Arcadia coin → combat becomes the way to get rich | ✅ **Defended by realm asymmetry**: streaming pays Omega credits, not Arcadia coin. The streamer-character does not get rich from streaming. Combat is still combat-economy-decoupled in their realm. |
| **Power Creep Eclipse** | Sponsor equipment becomes an arms race that eclipses the underlying skill system | ⚠️ **Watch — design forks needed**: sponsor equipment must have prerequisites (per Logos Resonance Items), must wear out / require maintenance, must not stack with player-crafted equipment in pure power terms. The design needs explicit caps. |
| **Cheat Shortcut** | Audience attention becomes a free-currency firehose | ⚠️ **Watch — yield-curve calibration needed**: the per-attention-unit yield must be calibrated such that Omega credits feel earned. Yield curves can decline at high attention thresholds (audience saturation). Specific calibration belongs in tuning passes. |
| **Juvenile Cast / Hive-Coordination across audience** | Audience members become structurally indistinguishable preference-aggregators | ✅ **Defended**: audience members are personality-flagged data objects per existing design; they have variation in interest, follow patterns, multi-stream behavior. The streaming economy doesn't homogenize them. |
| **Substrate-Degrader Principle** | The streaming economy degrades the realm-internal NPC economy | ✅ **Defended by asymmetry**: Omega credits don't convert to Arcadia coin. The realm-internal economy is not absorbed by the streaming economy; they're parallel systems that interact only via constrained channels (sponsorship-driven equipment provisioning, where the equipment is real and produced in-realm). |
| **Fiona Rule** | Sponsorship secrets / hidden audience preferences are load-bearing for plot | ✅ **Compliant**: spirit preferences are observable (the highlighting is in-fiction visible); sponsorship contracts are diegetic; audience-stream history is queryable to characters with appropriate Hearsay/Mapping fidelity. No hidden mechanism is load-bearing. |
| **Frictionless retrospective narration** | Sponsorship arcs that "happened off-page" with no on-page friction | ⚠️ **Watch — narrative discipline**: Storyline-system narrative seeds from sponsorship arcs should generate genuine on-page beats (the sponsorship offer, the contract negotiation, the first failure to deliver, etc.) rather than retrospective summary. This is content-authoring discipline, not service design. |

The watchpoints around equipment power-creep, yield-curve calibration, and on-page-narrative discipline are real but tractable. The framework structurally passes the immune-system check.

---

## Hierarchy Compliance

The streaming economy spans multiple service layers. Compliance:

| Component | Layer | Dependency direction | Compliance |
|-----------|-------|----------------------|------------|
| Audience Service | (in arcadia-kb; Bannou-side service hierarchy TBD) | — | TBD when Audience Service is implemented in Bannou |
| Currency yield computation | L2 | Reads from Audience Service via interface | OK |
| Spirit preference write from audience engagement | Seed (L2) | Audience Service writes via Seed API | OK (downward) |
| UX highlighting render hook | Agency (L4) | Agency reads from Seed (L2) | OK (downward) |
| Sponsorship Contract clause types | Contract (L1) | Contract clause schema extension | OK |
| NPC GOAP evaluating sponsorship offers | Actor (L2) | Actor reads via existing variable provider chain | OK (composes existing patterns) |
| Storyline narrative seeds from sponsorship arcs | Storyline (L4) | Storyline subscribes to Contract events and Audience hype-train events | OK |
| Broadcast artifact production | Workshop (L4) | Workshop reads Item, Inventory, Resource | OK |

No service depends upward. All flows follow the existing pattern.

---

## Implementation Sequence

### Phase 1: Currency and Basic Yield

1. Define Omega-credit currency entry with cross-realm conversion rules (Omega credits cannot convert to Arcadia/Fantasia coin)
2. Implement basic per-attention yield curve (linear baseline)
3. Hook Audience Service attention events to Currency drop computation

**Result**: Streaming activity in Arcadia/Fantasia generates Omega credits. No bells and whistles yet.

### Phase 2: Broadcast Artifact Item Family

4. Define broadcast artifact item template hierarchy (tiers, fidelity profiles)
5. Define pneuma-reagent operating cost mechanic
6. Workshop blueprints for broadcast artifact production
7. Faction integration for broadcast licensing

**Result**: In-realm streaming requires real artifacts produced by real craftsmen with real resources.

### Phase 3: Hype-Train Yield Curves and Subscriptions

8. Implement hype-train detection thresholds (per Advanced Audience Dynamics design)
9. Define exponential burst yield curves
10. Subscriber baseline payment mechanics
11. Cross-stream collaboration yield split rules

**Result**: The streaming economy has texture — different events produce different yields, subscriber relationships matter.

### Phase 4: Spirit Preference and UX Highlighting

12. Add spirit-cross-seed preference flag schema to Seed
13. Audience engagement → spirit preference write hook
14. UX selection-list highlighting render hook in Agency
15. Disposition integration — spirit preference is one weight in selection scoring, not a command

**Result**: Cross-realm sponsorship preference manifests in selection lists.

### Phase 5: Sponsorship Contracts

16. Define sponsorship Contract clause types
17. NPC GOAP evaluation behaviors for sponsorship offers
18. Equipment-loan / equipment-grant Escrow patterns
19. Storyline subscriptions to sponsorship-arc events

**Result**: NPC-initiated sponsorships emerge organically from streamer audience growth.

### Phase 6: Polish and Calibration

20. Yield curve calibration based on playtest data
21. Equipment-tier balancing for sponsor gear vs. player-crafted gear
22. Anti-abuse measures (sock-puppet audience detection, sponsorship farming patterns)
23. Realm-administrator tuning interfaces

**Result**: The economy is balanced, abuse-resistant, and tunable.

---

## Open Questions

1. **Yield curve calibration**: What's the right baseline per-attention-unit yield? Too low and streaming feels economically irrelevant; too high and Omega credits become a cheat-shortcut firehose. Calibration likely needs playtest data.

2. **Omega-credit purchasing power scope**: Omega credits buy Omega-side cosmetics, dashboard customization, social features. Should they ALSO buy access to Omega's diegetic VR-machine hacking features (per PLAYER-VISION's "hacking" mechanic)? This intersects the broader Omega economy design.

3. **Subscriber recurring payment cadence**: Real-world subscriptions are monthly. Game-time monthly? Real-time monthly? This is a calibration choice with significant design implications (real-time monthly creates pressure to log in; game-time monthly creates strategic stockpiling).

4. **Broadcast artifact black market**: Faction-licensed broadcast vs. unlicensed broadcast creates a natural intrigue layer. How aggressive should the licensing enforcement be? Realm administrators could vary this per realm.

5. **Counter-streaming / jamming dynamics**: Rival streamers, anti-streaming factions, dungeon-cores that resist being streamed — these are all natural consequences. How much does the framework need to specify versus letting it emerge?

6. **Cross-realm sponsorship across primary realms**: A sponsor in Arcadia who watches a streamer's Fantasia content. The framework specifies Omega-as-audience-realm, but bilateral primary-realm streaming (Arcadia audiences watching Fantasia streams via Arcadia VR-equivalent infrastructure) might emerge. Should it be supported or explicitly excluded?

7. **Streamer cooperative dynamics**: The cooperative cross-stream collaboration yield split rule is mentioned but not detailed. How are splits negotiated? Contract-mediated? Equal-share by default? This is a sub-system that could be elaborated.

8. **Sponsorship-driven Hearsay propagation specifics**: When a streamer's sponsor-equipment is visible in streams, how does Hearsay propagate back to the source workshop's reputation? The workshop's Faction reputation should track this. The exact propagation function needs specification.

9. **"Cancel culture" / reputation damage from negative streams**: A stream that goes badly (cruel behavior, hostile party dynamics, audience-offending content) should damage the streamer's audience and sponsorship relationships. The Audience Service's interest decay handles attention loss; the explicit reputation-damage mechanics need design.

10. **Audience-member-as-character self-reference**: An audience member is a player whose Omega seed is engaged. That same player's Arcadia/Fantasia character may interact with the streamer's character in-realm. How does the audience-side relationship affect the in-realm interaction beyond the spirit-preference highlighting? Probably nothing more — the constraint is intentional — but worth verifying.

11. **Spirit preference decay**: How long does an audience-engagement-driven spirit preference persist? Forever (until overwritten)? Decay over real-time / game-time? Saeculum-bounded (a preference formed in saeculum 47 fades by saeculum 50)? The decay function affects gameplay feel.

12. **Multi-spirit per-account interactions**: If a player runs multiple Omega audience-seed instances (which the design may or may not allow), does each independently form preferences? Or does the spirit unify them?

---

## Related Documents

- [VISION.md](../reference/VISION.md) — Multi-realm architecture (Omega/Arcadia/Fantasia), Logos/Pneuma metaphysics, Five North Stars (especially Living Worlds and Content Flywheel)
- [PLAYER-VISION.md](../reference/PLAYER-VISION.md) — Agency progressive-UX-expansion model, Omega Meta-Game with VR-machine config, multi-seed architecture
- [arcadia-kb: Audience Service Architecture](~/repos/arcadia-kb/06%20-%20Technical%20Architecture/Audience%20Service%20Architecture.md) — Audience member data model, attention metrics, follow/subscribe pipeline
- [arcadia-kb: Advanced Audience Dynamics](~/repos/arcadia-kb/06%20-%20Technical%20Architecture/Advanced%20Audience%20Dynamics.md) — Hype-train mechanics, multi-stream engagement, community loyalty vs. excitement
- [LEYLINE-COMPOSITION-FRAMEWORK.md](LEYLINE-COMPOSITION-FRAMEWORK.md) — Cross-realm bleed leylines provide the cosmological substrate for broadcast artifact transmission
- [INFORMATION-ECONOMY.md](INFORMATION-ECONOMY.md) — Related (information as commodity within a realm) but distinct (this doc is about audience attention as cross-realm economic flow)
- [LOGOS-RESONANCE-ITEMS.md](LOGOS-RESONANCE-ITEMS.md) — Sponsor equipment can incorporate resonance-item architecture for prestigious gear
- [DEVELOPER-STREAMS.md](DEVELOPER-STREAMS.md) — Out-of-game developer streaming (related but separate concept)
- [EQUIPMENT-ENCHANTMENT-DUALITY.md](EQUIPMENT-ENCHANTMENT-DUALITY.md) — Broadcast artifacts are enchanted devices following the duality architecture

---

## Design Principles

1. **Realm-economic asymmetry is a feature, not a bug.** Omega is the audience realm. Arcadia and Fantasia are content realms. The audience pays in Omega credits because the audience is in Omega. This protects in-realm NPC economies from streaming-attention firehoses.

2. **Streaming requires real infrastructure.** Broadcast artifacts are real items, produced by real craftsmen, consuming real resources, transmitted via real (leyline-mediated) channels. There is no "free streaming."

3. **Attention is a commodity.** Audience members allocate attention; attention has value; value translates to credits. The yield curve is configurable but the principle is fixed.

4. **Cross-realm preference is real but constrained.** Spirit preferences cross seeds (the spirit unifies them by definition), but they manifest as UX annotations in already-active selection moments — never as commands or quest goals.

5. **The character chooses.** Spirit preference annotates lists; characters select. Disposition still drives. Personality still matters. Spirit preference is one weight among many.

6. **Sponsorships emerge from NPC GOAP.** Designers don't author specific sponsor relationships. NPCs with appropriate drives evaluate streamers as sponsorship targets when the math favors it. Every sponsorship is generated, not scripted.

7. **The visibility-feedback loop is real.** Sponsor equipment use generates real performance data. Workshops use that data to improve products. Audience demand follows. The feedback loop produces emergent narrative arcs around brands and craftsmen, not just gear stat-lines.

8. **No realm dominates.** Omega has the audience layer; Arcadia and Fantasia have the content. Neither realm becomes the universal economy. Each realm's local economy preserves its integrity.

9. **Attention-failure is content too.** The Audience Service supports attention loss, follower migration, and reputation decay. Streaming arcs include failures. The framework structurally supports content where streaming relationships sour, not just where they succeed.

10. **Zero new plugins.** The streaming economy composes from Audience Service + Currency + Realm + Item + Workshop + Contract + Escrow + Seed + Agency + Disposition + Hearsay + Faction + Storyline + Actor — every primitive already exists or is in-design. The framework is integration, not invention.

---

*This document describes the design vision for the cross-realm streaming economy. Implementation details (yield curve calibration, broadcast artifact tier specifications, sponsorship Contract clause types, ABML behavior templates for NPC sponsorship decisions, audience-engagement-to-spirit-preference write semantics) belong in the relevant service deep dives and implementation plans. The framework's load-bearing design decision is the realm-economic asymmetry — that asymmetry is what protects the in-realm NPC economies from being absorbed by the streaming attention firehose.*
