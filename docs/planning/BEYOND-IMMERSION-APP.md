# Beyond Immersion Platform App: Multi-Game Publisher Shell with Embedded Bannou Instance

> **Status**: Planning — no implementation yet
> **Audience**: Architecture, platform strategy, legal positioning, pre-launch planning
> **Related**: [docs/planning/BANNOU-EMBEDDED.md](BANNOU-EMBEDDED.md), [docs/plugins/AGENCY.md](../plugins/AGENCY.md), [docs/reference/VISION.md](../reference/VISION.md), [docs/reference/PLAYER-VISION.md](../reference/PLAYER-VISION.md)
> **First target consumer**: Defenders of Ba'hava (the first Beyond Immersion game, mobile-first, uses Bannou embedded)
> **Eventual target consumer**: Arcadia (cloud-deployed, 18+ subscription)

---

## Summary

This document specifies a multi-game publisher app architecture for Beyond Immersion that:

1. Ships the Beyond Immersion app as a **Bannou embedded instance** on mobile (not a thin client to a remote server)
2. Redesigns the **Website plugin** from a stubbed browser-CMS into a module-composable CMS where each account registration contributes modules (DI-style assembly), with a first-class `POST /website/navigation/handle` endpoint that routes any deep-link signal (including push notifications)
3. Gates 18+ at the **account tier**, not the **app content tier** — preserves legal simplicity (COPPA avoidance, clean creator contracting, adult-only ToS binding) without forcing an 18+ content rating on the app
4. Delivers **per-game companion modules** (chat, compendiums, message boards, game notifications, mini-games that impact in-game state) via CDN-delivered content packs
5. Unifies **Beyond Immersion as both publisher brand and in-universe Omega hardware manufacturer** — deliberate fourth-wall trick where the real app and the diegetic Omega device are the same product
6. Introduces a dedicated **lib-push plugin (L3)** with publisher/receiver modes using a **two-scope push model**: account-scope (persistent, resolved from a session-registration-seeded interest index — events never carry accountIds per T32) and possession-scope (ephemeral, resolved from Gardener's active character bindings). Two new DI observer interfaces (`IEntityEventObserver` for event dispatch, `IEntityRegistrationObserver` for interest-index population) tap into IEntitySessionRegistry without coupling publishers to push.
7. Uses **platform-native anonymous identity** (Game Center, Play Games, Steam) so individual games work without any Beyond Immersion account; account registration unlocks cross-platform features

The document establishes 18 decisions, their rationale, the architecture that emerges, the plugin-level impact, and the open questions that remain.

---

## Context & Background

### The strategic question

Arcadia's design has an 18+ subscription requirement for legal simplicity (creator marketplace contracting, ToS binding, COPPA avoidance). A "companion app" is typically how MMOs extend their reach — chat, inventory management, news, discovery. If Arcadia is 18+, does that force the companion app to be 18+? If yes, that cuts off mobile distribution (App Store AO policies) and a large slice of the user acquisition surface.

The answer turned out to be "no, if structured correctly":

- **The 18+ requirement is contractual, not content-driven.** Precedent is well-established: Robinhood, Patreon, Coinbase, Kickstarter all require 18+ for contracting while their apps are Teen-rated based on actual content.
- **Defenders of Ba'hava ships first**, is mobile-first by design (Android/iOS/PC), is Teen-rated by content, and uses Bannou in embedded mode. It doesn't need a Beyond Immersion account at all.
- **The companion app is really a publisher app**, not an Arcadia-specific second-screen. It hosts companion modules for all Beyond Immersion games. Arcadia is one of many.

### What existed before this decision

- **Website plugin (L3)**: 14 endpoints declared, 0 implemented. Marked "currently stubbed" in the composition reference. No visual design, no layout decisions, no content model.
- **Agency plugin (L4)**: Pre-implementation (no schema, no code). Includes Potential Extension #10: per-session client event transformation pipeline extending Connect's existing 3-hardcoded-event interception point. This mechanism is relevant to push notifications.
- **Bannou embedded mode**: Fully planned (see [BANNOU-EMBEDDED.md](BANNOU-EMBEDDED.md)). Services dispatch in-process via DI; state uses SQLite/InMemory; messaging uses InMemory.
- **DeviceInfo standard**: Implemented. Covers PCs, mobile devices, tablets, consoles, handhelds.
- **Auth plugin**: OAuth providers exist (email, OAuth, Steam). Platform-native anonymous identity (Game Center, Play Games) is a natural extension.
- **Client Events infrastructure**: `IClientEventPublisher` delivers to WebSocket sessions via the `bannou-client-events` direct exchange. No push notification path exists.

### The Defenders context

The companion Defenders knowledge base (`~/repos/defenders-kb`) describes a mobile-first fantasy castle-defense action-RPG brawler. Content is Teen-rated (fantasy violence, mature narrative themes, no sexual content). Uses Bannou embedded. Needs: cloud save sync, leaderboards, achievements, optional DLC. Does NOT need: player marketplace, cross-game chat, subscription, Beyond Immersion account. The age-rating file in that knowledge base is a stub — content rating has not yet been formally decided but Teen is the expected target.

---

## Decisions

### D1: 18+ is contractual, not content-driven

**Decision**: Beyond Immersion accounts require users to be 18+. This is a contractual/legal simplification, not a content rating. The age requirement exists to enable clean creator contracting, subscription binding, and ToS without minor-consent flows. The content displayed through the Beyond Immersion app is rated for its actual content (Teen/12+/T expected).

**Rationale**:
- Matches established pattern (Patreon, Robinhood, Coinbase, Kickstarter, Stripe Connect all use this model)
- Eliminates COPPA compliance burden entirely
- Eliminates parental consent infrastructure
- Simplifies creator payment (all contractors are adults)
- Aligns with contract law (minors can't enter binding agreements)
- Does NOT force the app into an 18+ content rating
- Does NOT block Apple App Store approval (the test is content-driven, not contract-driven)

**Implications**:
- App rating based on content displayed (Teen expected)
- 18+ verification happens at account creation on the web, not in the app
- Individual games may have their own content ratings independent of the account tier

---

### D2: No 13+ Beyond Immersion accounts

**Decision**: The Beyond Immersion account minimum is 18+. No 13+ tier.

**Rationale**:
- Preserves D1's legal simplicity
- A 13+ tier would reintroduce COPPA compliance, parental consent, minor-protection workflows
- The "teen audience acquisition" concern is handled by individual games shipping standalone without any Beyond Immersion account (see D5)
- Simpler than tiered account age

**Implications**:
- Chat, cross-game messaging, marketplace participation, and cross-platform cloud saves require 18+ verification
- Individual games (Defenders, etc.) must work fully without any Beyond Immersion account
- Platform-native identity (Game Center, Play Games, Steam) provides anonymous persistent player identity where needed

---

### D3: Games and the publisher app are separate store listings

**Decision**: Each Beyond Immersion game ships as its own mobile app on the store. The Beyond Immersion publisher app is a separate listing. Defenders of Ba'hava on the Google Play Store is not "downloaded through" the Beyond Immersion app.

**Rationale**:
- Each game has its own store presence, marketing page, reviews, and discovery surface
- Players can buy Defenders without first installing the Beyond Immersion app
- The Beyond Immersion app is optional for most games (companion features only, never game-required)
- Mirrors the Battle.net / Ubisoft Connect / EA App pattern that app stores already understand
- Avoids "dependent app" complications where one app's deletion breaks another

**Implications**:
- The Beyond Immersion app must add value beyond game purchase (otherwise no reason to install)
- Companion modules are unlocked after registered games are detected (via platform-native APIs or explicit linking)
- Marketing must explain both Beyond Immersion (publisher) and individual games (products)
- Cross-game features (unified chat, cross-platform saves) are the Beyond Immersion app's justifying value

---

### D4: Games are playable without any Beyond Immersion account

**Decision**: Individual games (Defenders first) work standalone — no registration required. Cloud saves, leaderboards, achievements use platform-native identity (Game Center, Play Games, Steam) when available. A Beyond Immersion account is purely additive.

**Rationale**:
- Lowest friction for new players
- Standard mobile game expectation (no sign-up to play)
- Platform-native identity persists through uninstall/reinstall (anchored to Apple ID / Google account / Steam ID)
- Beyond Immersion account becomes the **cross-platform unifier** — its value is unifying identity across iOS, Android, and PC, not providing basic save/leaderboard functionality
- Matches how EA, Ubisoft, Activision handle standalone launches

**Implications**:
- Auth plugin needs Game Center, Play Games Services, and Steam as first-class OAuth providers alongside email/OAuth
- Anonymous → registered account upgrade path must merge platform identities (see D9)
- Defenders gets a full-featured experience with zero account friction
- The Beyond Immersion account's value proposition must be explicit and compelling (cross-platform, cross-game, companion, marketplace)

---

### D5: The Beyond Immersion mobile app is itself a Bannou embedded instance

**Decision**: The Beyond Immersion mobile app is not a thin client calling remote APIs. It is a Bannou embedded instance running on the phone, with its own plugin set loaded locally, its own state (SQLite/InMemory), and its own in-process messaging. It only consults cloud Bannou for things it cannot own locally (active registrations, cloud-authoritative state, server-side game world state, cross-device sync).

**Rationale**:
- Enables mini-games that impact in-game state — companion modules can include ABML behaviors, local actors, local currency, local collection entries
- Offline capability — most companion features work without network; sync is background/opportunistic
- Consistent SDK surface — the same plugins that run Defenders in embedded mode run the companion app
- CDN-delivered content packs naturally slot into the embedded Bannou instance (they're plugin bundles + data)
- Reduces server load — chat history cache, compendium data, local notifications all handled in-process
- Latency — UI feels instant because there's no round-trip to a server for local operations

**Implications**:
- The Beyond Immersion app binary includes a Bannou runtime
- Content packs from the CMS register plugins, state, ABML documents, and UI manifests into the embedded instance at download time
- WebSocket to cloud Bannou is used for realtime updates when the app is open; push notifications handle the app-closed case
- The "what registrations does this account have?" query is the primary thing that requires cloud coordination — everything else is content delivery
- App update size is larger than a thin client, but comparable to other embedded-engine mobile games (Unity, Stride)

---

### D6: Website plugin redesigned as a CMS with module registration

**Decision**: The Website plugin (L3) is redesigned from its current "public CMS for news/profiles/downloads" stub into a **module-composable CMS** where each account registration (game, creator role, subscription tier, etc.) contributes modules that slot into the rendering pipeline. Modules are DI-style — a registration declares the modules it provides, the CMS composes a layout from the modules available to a given account at a given time.

**Rationale**:
- The "Beyond Immersion app lists games and has companion modules per registered game" pattern is exactly this composition model
- CMS-as-module-host is more flexible than CMS-as-page-tree
- Enables "the app looks different per user depending on what they own" without per-user custom builds
- Reuses the existing Bannou DI pattern at a content level — plugins register modules into the CMS the same way they register services into DI
- Platform-level consistency — web-based Beyond Immersion site uses the same module composition; app reuses the same manifests

**Implications**:
- Website plugin schema needs to be rewritten from stub to a module registry + manifest composer + content delivery layer
- Module definitions include: route, required registrations, content type, render target (web vs. mobile vs. embedded), asset bundle URI
- Existing 14 stubbed Website endpoints are replaced by the new module API
- A game service registering with Bannou declares what companion module(s) it contributes; Website composes the manifest per-account

---

### D7: Beyond Immersion is both publisher and in-universe Omega brand

**Decision**: "Beyond Immersion" is simultaneously the real-world publisher (the company making the games) and the in-universe brand of the VR hardware manufacturer in Arcadia's Omega realm. The real mobile app and the in-universe Omega device are the same product — same logo, same visual design, same interaction patterns.

**Rationale**:
- Diegetic marketing: the real app is an in-universe artifact, not just a promotional tool
- Thematic coherence: Omega's cyberpunk premise ("you are a person in 2080 whose VR rig is Beyond Immersion") mirrors the real player's experience ("I'm a person in 2026 whose companion app is Beyond Immersion")
- Fourth-wall trick: when players in Omega see Beyond Immersion branding in-game, they're seeing the same brand they downloaded on their phone
- Differentiator: no other game/platform does this. It's a unique identity.
- Marketing economy: one brand to establish, one visual identity to maintain, two deployment surfaces (real and fictional)

**Implications**:
- The app's visual design is a **lore artifact commitment** — it must lean into cyberpunk aesthetic from day one
- UI patterns, color palette, typography, and sound design are set in Omega's fictional universe and in real-world design reviews simultaneously
- Changing the app's look later means retconning the in-universe brand, or vice versa
- Arcadia's Omega realm must visually render the Beyond Immersion product consistently with the real app
- Marketing assets (screenshots, trailers) can blur the line intentionally — is this gameplay or is this the real app?

---

### D8: Traditional DLC for Defenders, no player marketplace

**Decision**: Defenders of Ba'hava ships with traditional developer-authored DLC only. No player-contributed marketplace, no creator economy for Defenders content. The marketplace is an Arcadia-tier feature.

**Rationale**:
- Defenders is a focused narrative-driven game; curated content preserves the authored experience
- Skill Workshop-style modding doesn't suit a premium narrative castle-defense brawler
- The marketplace infrastructure is complex and doesn't need to ship with the first game
- Arcadia's always-online world is a better fit for user-generated content (the simulation accommodates variety; Defenders has an authored story)

**Implications**:
- Defenders' companion module is simpler: compendium, leaderboards, lore, mini-games, notifications — no marketplace UI
- Marketplace infrastructure can be deferred until Arcadia
- DLC is purchased through the game's own store listing (App Store/Play/Steam), not through the Beyond Immersion app

---

### D9: Anonymous → account upgrade merges platform identities

**Decision**: Players who create a Beyond Immersion account after playing games anonymously have their platform identities (Game Center, Play Games, Steam) merged into the new account. All prior save data, leaderboard entries, and achievements become accessible via the account.

**Rationale**:
- Standard mobile game UX pattern (guest → registered)
- Preserves player investment — no "do I lose my progress if I sign up?" hesitation
- The Auth plugin already supports OAuth provider linking; this is the same machinery

**Implications**:
- Auth plugin exposes an account-linking flow that binds external provider IDs to a Bannou account
- Conflict handling needed: what if a player tries to link a platform ID that's already linked to another account?
- Merge semantics are additive, not destructive — each platform ID remains individually usable even after linking (player can still play from a non-registered device with Game Center)

---

### D10: Push notifications are a new delivery channel, not just push-as-client-event

**Decision**: Push notifications (APNs/FCM) are architected as a new delivery channel with their own batching, deduplication, and rate-limiting semantics. Push notifications are a **summary** of events, not a 1:1 delivery.

**Rationale**:
- 1:1 mapping of client events to push notifications would flood device notification trays
- APNs and FCM have rate limits and quality-of-service expectations
- Not-realtime is acceptable (few seconds is fine) — batching windows are large
- Deduplication matters: 10 chat messages in 30 seconds should be "10 new messages" not 10 pushes
- Push config is platform-wide and per-account-preferences, not per-player-progression — no Agency dependency

**Implications**:
- New infrastructure: APNs/FCM gateway, device token registry, batching runner, rule engine
- Per-event configuration declares push eligibility, batching rules, rate limits
- Per-account preferences allow user-level control (quiet hours, per-game muting)
- See D11–D14 for the specific architectural decisions and Part 4 for the full architecture

---

### D11: Push delivery lives in a dedicated plugin: `lib-push` (L3)

**Decision**: Push notification infrastructure is a new plugin `lib-push`, at layer L3 (App Features). Connect (L1) stays focused on session routing and WebSocket delivery. `lib-push` owns APNs/FCM integration, device token registry, batching, rate limiting, rule evaluation, and (on the receiving side) OS notification payload handling.

**Rationale**:
- L3 matches the pattern for optional, external-backend-dependent plugins (Voice/Kamailio, Asset/MinIO, Broadcast/streaming platforms)
- L1 is always-on core infrastructure; push is optional and can be disabled per deployment
- Clean separation keeps Connect single-responsibility (session routing)
- Session-scope vs. device-scope are genuinely different concerns — device registrations are persistent, accounts, cross-session — not a Connect concept
- Dedicated plugin can own its own state stores, workers, configuration, telemetry, event schemas without entangling Connect
- Matches the "one plugin per external backend dependency" pattern

**Implications**:
- New plugin: `plugins/lib-push/` at L3
- New schemas: `push-api.yaml`, `push-service-events.yaml`, `push-configuration.yaml`, `push-client-events.yaml`
- New state stores for device tokens, push preferences, pending push queues
- Registers as a session-registry observer (see D12) to receive events
- Consumes external infrastructure: APNs HTTP/2 endpoints, FCM HTTP endpoints

---

### D12: Cross-plugin event tap via `IEntityEventObserver` on IEntitySessionRegistry

**Decision**: Introduce a new DI interface `IEntityEventObserver` in `bannou-service/Providers/` that plugins implement to receive copies of entity-scoped client events. `EntitySessionRegistry.PublishToEntitySessionsAsync` dispatches to all registered observers **regardless of session count**, then separately performs session delivery when sessions exist. `lib-push` registers an observer to drive push notification queueing.

**Rationale**:
- The existing IClientEventPublisher tap point misses the no-session case — publishing short-circuits to zero when no WebSocket sessions are registered for the entity. Push notifications care about exactly this case (app is closed = no session = but the user still needs the notification).
- DI observer pattern is the established Bannou pattern for cross-layer push fan-out (ISeedEvolutionListener, ICollectionUnlockListener, ISessionActivityListener, IItemInstanceDestructionListener)
- Observers fire per-node, but must write to distributed state — lib-push observer enqueues into a Redis-backed pending-push queue, worker on any node dispatches
- Extends cleanly to future observers (audit logs, analytics ingestion, third-party relays) without Connect changes

**Distributed safety**: Per SERVICE-HIERARCHY "DI Listener vs Provider" rules, observers are local-fan-out only — they fire on the node that processed the publish. `lib-push` observer MUST write events into a distributed queue (Redis) before returning. A push dispatch worker (potentially on any node) claims and processes queued events. This matches the `EventBatcher`/`DeduplicatingEventBatcher` + `EventBatcherWorker` pattern in `bannou-service/Services/` (reference implementations: `lib-item/Services/ItemInstanceEventBatcher.cs` — 3 batchers + 1 worker; `lib-permission/RegistrationEventBatcher.cs` — single deduplicating batcher).

**Implications**:
- New interface: `bannou-service/Providers/IEntityEventObserver.cs`
- Refactor: `EntitySessionRegistry.PublishToEntitySessionsAsync` splits observer dispatch from session dispatch, removes the zero-sessions short-circuit for observer dispatch
- `lib-push` registers `PushEventObserver : IEntityEventObserver` via `[BannouHelperService]`
- All other entity-scoped publishes automatically reach lib-push without the publisher knowing

---

### D13: Website provides first-class `/website/navigation/handle` endpoint; push is one caller

**Decision**: The Website plugin (redesigned CMS) exposes `POST /website/navigation/handle` as a universal navigation-routing endpoint. It takes a deep-link payload (module code, route, context) and resolves it to a navigation command the client UI executes. This endpoint exists always — whether push is enabled or not. The `lib-push` receiver mode calls it when the user taps a notification; other callers (in-app deep links, external URL schemes, debug tools) use the same endpoint.

**Rationale**:
- Separates navigation handling (Website concern — what module, what route, what context) from notification reception (lib-push concern — OS payload, permission state, device info)
- Website doesn't depend on push: any caller can invoke `/website/navigation/handle` to route within the app
- Push plugin optionally registers as a Website navigation producer at startup (if push plugin is enabled); missing push plugin leaves the endpoint available to manual invocation
- Clean inversion: Website is the receiver's navigation authority, push is one signal source among many
- Future-proofs additional signal sources (QR code deep links, cross-app handoff, URL schemes) without push-specific coupling

**Implications**:
- Website plugin owns: `/website/navigation/handle` endpoint + navigation target resolution logic
- `lib-push` (receiver mode): parses OS notification payload, calls `/website/navigation/handle` locally (in-process, embedded Bannou)
- Navigation payload schema lives in common-api.yaml so both plugins reference the same types
- Fallback: if a notification references a module not installed, Website returns a "module-missing" navigation command that the client handles (offer install prompt, show fallback)

---

### D14: Device token cleanup uses auto-cleanup on delivery signals + reconciliation worker

**Decision**: Dead device tokens are cleaned up via two complementary mechanisms:
1. **Auto-cleanup on delivery signals** — when APNs returns `410 Unregistered` or `400 BadDeviceToken`, or when FCM returns `UNREGISTERED`, `INVALID_ARGUMENT`, or `SENDER_ID_MISMATCH`, `lib-push` immediately deletes the token from the registry.
2. **Reconciliation worker** — a background worker periodically scans for tokens whose `lastConfirmedValidAt` timestamp is older than a configurable threshold (default 90 days). Such tokens are dry-run-pinged; if invalid, removed.

**Rationale**:
- APNs/FCM provide explicit invalidation signals; using them is standard practice and catches most cases at no extra cost
- Some tokens silently die without us attempting to push to them (user reinstalls app without logging in, device factory reset, etc.) — reconciliation catches these
- `lastConfirmedValidAt` updated on every successful push limits worker workload (active tokens need no attention)
- Matches the canonical `BackgroundService` polling loop pattern (see FOUNDATION TENETS § Background Worker Polling Loop)

**Implications**:
- Device token state store includes `lastConfirmedValidAt` field
- `lib-push` dispatch path inspects APNs/FCM response and deletes token on invalidation signals
- `TokenReconciliationWorker` runs on configurable interval (default 24h)
- Also handles the account.deleted cleanup obligation — tokens associated with a deleted account are purged (per Account Deletion Cleanup Obligation)

---

### D15: Two-scope push model — account-persistent and possession-ephemeral

**Decision**: Push notifications operate on two distinct scopes with different lifecycle semantics:

1. **Account scope (persistent)**: Driven by device registrations (which carry accountId). Events on account-adjacent entities (chat rooms, friend lists, seeds, subscriptions, platform announcements) resolve audience from Push's own persistent interest index. Account-scope pushes work regardless of session state — including when all apps are closed. Account-scope pushes work even if Gardener is disabled.

2. **Possession scope (ephemeral, Gardener-managed)**: Driven by Gardener's active character↔account possession bindings. Events on character-adjacent entities (character, inventory, quest, contract, status) push only while an active possession binding exists. When the player isn't playing Arcadia (no possession), character-scoped events do not push — the player discovers what happened when they return.

**Rationale**:
- Maps directly to the existing architectural boundary: seeds are account-owned (persistent link), characters are possessed via Gardener (ephemeral link)
- Aligns with the "world is alive whether or not you're watching" design principle — offline character events are NOT pushed; you find out when you come back
- Account-scope entities (chat, friends, subscription) are meaningful regardless of gameplay session state — pushes for these should always work
- Gardener already manages the possession lifecycle; Push reads its state rather than duplicating it

**The scope boundary principle**: if the entity is account-owned (outlives character lifecycles) → account-scope. If the entity is character-specific (meaningless without active possession) → possession-scope.

**Implications**:
- x-push-config per event declares `scope: account | possession`
- Account-scope audience: resolved from Push's persistent interest index (see D16)
- Possession-scope audience: resolved from Gardener's active possession bindings
- Gardener calls Push's API directly (L4 → L3, hierarchy-permitted) to bind/unbind possession — not events (possession bindings are state-critical; events have no ordering/confirmation guarantee)

---

### D16: Push audience resolved from session-registration-seeded interest index — events never carry accountIds

**Decision**: Push resolves audience entirely from its own persistent state. Events do NOT carry accountIds — ever (per T32: Account Identity Boundary). Push builds its audience knowledge by observing IEntitySessionRegistry registrations and resolving session → account via Connect's session manager.

**Mechanism**: A second observer interface `IEntityRegistrationObserver` in `bannou-service/Providers/` notifies Push when sessions register/unregister for entities. Push resolves `sessionId → accountId` at registration time (via Connect's `BannouSessionManager`) and maintains a persistent interest store: `(entityType, entityId) → Set<accountId>`.

- **On session entity registration**: Push resolves session → account, adds account to entity's interest set
- **On session entity unregistration or disconnect**: Push checks the event's x-push-config scope. Account-scope → interest KEPT (account remains interested beyond session). Possession-scope → interest REMOVED (possession ended)
- **At dispatch time**: observer fires with (entityType, entityId, event) → lookup interest index → accountIds → device tokens → push. No external service calls on the critical dispatch path.

**Staleness handling**: The interest index may become stale (e.g., user leaves a chat room while offline — Push doesn't know until the next session). Eventual consistency is acceptable: a few erroneous pushes to someone who recently left a room are harmless. The index naturally refreshes on the next session (new registrations replace old ones). Entity `*.deleted` lifecycle events clean dead entries. A periodic staleness sweep can verify against owning services if tighter consistency is needed.

**Rationale**:
- Events are published to sessions, not accounts — Push cannot read audience from events (T32)
- External service calls at dispatch time would add latency and failure modes to the critical push path
- Session registrations already carry the entity-interest information — Push just needs to shadow it with account resolution
- DI observer pattern (IEntityRegistrationObserver) matches the established Bannou pattern for cross-plugin notification

**Implications**:
- New interface: `bannou-service/Providers/IEntityRegistrationObserver.cs`
- Refactor: `EntitySessionRegistry.RegisterAsync` and `UnregisterAsync` notify registration observers (parallel to D12's event observers)
- New state store: `push-interest` (Redis) — `(entityType:entityId) → Set<accountId>` — persistent, cleaned on entity deletion and account deletion
- Push resolves session → account via Connect's session manager (Push is in the identity boundary — same justification as Subscription, Game-Session, Analytics, Gardener)

---

### D17: Device tokens are not deprecatable — immediate hard delete

**Decision**: Device token registrations use immediate hard delete per the T31 decision tree. They are instance data (concrete occurrences, not definitions), never referenced by other persistent entities, and have no need for a transition period.

---

### D18: No offline character push; no reconnect summary events

**Decision**: Character-scoped events do NOT push when the player has no active possession. There are no "character lifecycle summary" events on reconnect. The player discovers what happened to their characters through in-game surfaces when they return.

**Rationale**:
- Aligns with "the world is alive whether or not you're watching" — discovery when you return IS the experience
- Characters are not directly linked to accounts (only indirectly via seeds during active possession) — resolving "character X's owner" when no possession exists is architecturally impossible by design
- Reconnect summary events would require maintaining a per-account "what happened while you were away" queue — this is a significant new subsystem with no clear boundary on what qualifies as "important enough to summarize"

---

## Architecture Overview

### The platform stack

```
Beyond Immersion (publisher)
│
├── Beyond Immersion App (iOS/Android/Web)
│ ├── Runtime: Bannou embedded instance
│ │ ├── Plugins loaded: Auth (OAuth providers), Website (CMS),
│ │ │ Chat, Collection, Documentation, Actor (local mini-games),
│ │ │ State (SQLite/InMemory), Messaging (InMemory)
│ │ ├── WebSocket client: Connect (cloud) for realtime updates when open
│ │ └── Push client: APNs/FCM for notifications when closed
│ ├── Content delivery: CDN-delivered module packs
│ ├── Account tier: Anonymous (catalog, trailers, news) OR 18+ Beyond Immersion account
│ └── Brand: Beyond Immersion (same as in-universe Omega brand)
│
├── Defenders of Ba'hava (iOS/Android/PC separate app listings)
│ ├── Runtime: Bannou embedded instance (self-contained)
│ ├── Identity: Game Center / Play Games / Steam (platform-native)
│ ├── Optional: Beyond Immersion account link → cross-platform saves
│ ├── DLC: developer-authored, traditional purchase model
│ └── Companion module: installable via Beyond Immersion app after account link
│
├── Arcadia (PC, likely Steam)
│ ├── Runtime: Bannou cloud (connects to remote services)
│ ├── Identity: Beyond Immersion account (18+ required)
│ ├── Subscription: required
│ ├── Creator marketplace: yes, 18+ creator contracts
│ └── Companion module: full-featured, integrates with live simulation
│
└── Future games (any combination of embedded/cloud, all-ages account-optional or 18+ required)
```

### Account tier matrix

| Tier | Age gate | Where verified | What it unlocks |
|---|---|---|---|
| **Anonymous (device only)** | None | N/A | Browse catalog, watch trailers, read news, play games standalone (with platform-native saves/leaderboards) |
| **Beyond Immersion account** | 18+ | Web, at account creation | Cross-platform cloud saves, cross-game chat, friend list, companion modules, marketplace browsing |
| **Arcadia subscriber** | Already 18+ from account | Web, at subscription purchase | All of above + Arcadia gameplay, full Arcadia companion features |
| **Creator** | Already 18+ + ID verified | Web, via Stripe Connect onboarding | All of above + marketplace publishing, creator payouts |

Age verification happens exactly **once**: at Beyond Immersion account creation. All subsequent tiers inherit it.

### The module registration pattern

Each game service that integrates with Beyond Immersion declares:

1. **What modules it provides** (companion features for the Beyond Immersion app)
2. **What registrations qualify** (purchased game, subscribed, creator tier, etc.)
3. **What content packs exist** (UI bundles, ABML documents, compendium data)
4. **What push notification events it emits** (per-event batching/rate rules)

The Website plugin (as CMS) composes the manifest per-account:

1. Receive "what does account X have access to?" query
2. Enumerate registrations (purchased games, subscriptions, creator status)
3. Discover modules contributed by each registration
4. Resolve content pack URIs (CDN-backed)
5. Return composed manifest to client
6. Client downloads required packs, instantiates modules, renders layout

This is **DI-for-content**: plugins register services into DI containers the same way registrations register modules into the CMS.

---

## Part 1: The Beyond Immersion App (Mobile) as Bannou Embedded Instance

### Why embedded, not thin client

A traditional companion app is a thin client: it makes HTTP requests to a remote server and renders responses. This is simple but limited. An embedded Bannou instance inside the app is more complex but unlocks:

- **Local mini-games** that affect in-game state: a daily scouting mission mini-game can run entirely locally in the app's embedded Bannou, then sync results to the cloud when online
- **Offline capability**: compendium, lore, cached chat history, and most companion features work without network
- **Consistent SDK**: the same Bannou plugins run in Defenders embedded, the Beyond Immersion app embedded, and the cloud — one code surface
- **Low latency**: no server round-trip for local operations
- **Plugin composition**: each game's companion module is a plugin bundle that registers into the embedded instance at install time

### What the embedded instance looks like

```
Beyond Immersion App (mobile)
│
├── Bannou Runtime (embedded)
│ ├── lib-auth (local OAuth provider implementations)
│ ├── lib-website (CMS, locally-cached module manifests)
│ ├── lib-chat (local cache + cloud sync)
│ ├── lib-collection (compendium entries, cached)
│ ├── lib-documentation (lore, cached)
│ ├── lib-actor (local ABML runtime for mini-games)
│ ├── lib-seed (local seed state for mini-game progression)
│ ├── lib-currency (local mini-game currency)
│ ├── [Per-game companion plugins as installed content packs]
│ │ ├── lib-defenders-companion
│ │ ├── lib-arcadia-companion
│ │ └── ...
│ ├── lib-state (SQLite backend)
│ ├── lib-messaging (InMemory backend)
│ └── lib-mesh (DirectDispatch mode)
│
├── Cloud Sync Layer
│ ├── WebSocket client → cloud Connect
│ │ (realtime events when app is open)
│ ├── HTTP client → cloud Bannou API
│ │ (registrations lookup, cross-device sync, cloud-authoritative state)
│ └── Push notification handler → APNs/FCM
│ (app-closed events, batched summaries)
│
├── Content Pack Manager
│ ├── Fetches module bundles from CDN
│ ├── Registers plugins into embedded runtime at install time
│ └── Unregisters on module removal
│
└── Native UI Layer
 ├── Reads Website CMS manifest
 ├── Renders module UIs (native + webview)
 └── Binds to local Bannou events for realtime UI updates
```

### Content pack anatomy

A content pack delivered via CDN contains:

| Component | Purpose |
|---|---|
| **Manifest** | Declares module metadata, required plugins, entry points |
| **Plugin bundle** | One or more Bannou plugin assemblies (DLLs for .NET, or a compiled equivalent) |
| **UI assets** | Native UI fragments (SwiftUI/Jetpack Compose/web) or asset bundles |
| **ABML documents** | Behavior documents for local mini-games |
| **Seed data** | Initial data (compendium entries, lore pages, currency definitions) |
| **Localization** | Translation bundles per supported language |
| **Push config** | Declares push notification events and batching rules for this module |

Content packs are **versioned** and **signed** by Beyond Immersion. Delta updates reduce bandwidth. Uninstalling a game (or unsubscribing, or losing creator status) removes the associated content pack from the app, unregistering its plugins cleanly.

### What syncs with the cloud vs. stays local

| Data | Location | Rationale |
|---|---|---|
| Account registrations | Cloud authoritative, local cached | Changes when player purchases games |
| Compendium entries (unlocked) | Local authoritative (once unlocked) | Once earned, always accessible offline |
| Chat history | Cloud authoritative, local recent cache | Long history may exceed device storage |
| Mini-game state (local) | Local authoritative, cloud backed up | Cheating prevention via server-authoritative checkpoints |
| Mini-game results → main game | Cloud authoritative | Main game trusts cloud, not local app |
| Notifications | Cloud authoritative | Push is a pull model from cloud state |
| User preferences (push settings, mute, etc.) | Cloud authoritative | Syncs across devices |

---

## Part 2: The Website Plugin as CMS

### Current state

The Website plugin (L3) is currently stubbed — 14 endpoints declared in the schema, zero implemented, no visual design or content model. It was originally conceived as a "public-facing browser CMS (news, profiles, downloads) using REST patterns." That framing is abandoned in favor of the module-composable CMS pattern below.

### Module-composable CMS architecture

The redesigned Website plugin owns:

1. **Module registry** — definitions of available modules (by game, by tier, by account state)
2. **Manifest composition** — given an account, produce the set of modules the account sees
3. **Content delivery** — serve module bundles (UI, plugin assemblies, seed data) via CDN
4. **Layout hints** — module metadata for ordering/grouping in the client UI
5. **Version management** — module versioning, delta updates, compatibility resolution

### Module definition

```yaml
# (Illustrative; actual schema shape TBD)
Module:
 code: string # e.g., "defenders.compendium", "arcadia.marketplace"
 version: semver
 displayName: localized string
 description: localized string
 icon: asset URI
 gameServiceId: Guid? # Optional game-service scope
 requiredRegistrations:
 - type: enum # purchased-game | subscribed | creator
 gameId: string? # for purchased-game registrations
 requiredAccountTier: enum # anonymous | basic | creator
 renderTargets:
 - mobile-app
 - web
 - desktop
 entryPoint: string # URI pointing to content pack
 dependencies:
 - otherModuleCode: string
 minVersion: semver
 pushConfig: # Declared events this module may push
 - eventTopic: string
 batchingRule: enum
 rateLimit: per-hour
 category: enum # social | informational | actionable
```

### Manifest composition

When a client (app, web, desktop) requests its manifest:

1. Website plugin resolves the requesting account's registrations (via Auth + game ownership + subscription state)
2. Website plugin filters the module registry by registration match + account tier + render target
3. Dependency graph resolved (modules may depend on other modules)
4. Resulting manifest: ordered list of modules with URIs, versions, layout hints, push configs
5. Client receives manifest, fetches content packs not yet cached, instantiates modules

### What replaces the 14 stub endpoints

The existing stub endpoints (public pages, profiles, downloads) are replaced by:

- `POST /website/manifest/get` — composes per-account module manifest
- `POST /website/module/content` — serves module content pack metadata (URI, signature, version)
- `POST /website/module/version-check` — checks for module updates
- `POST /website/module/list` — admin: list available modules
- `POST /website/module/create|update|deprecate|clean-deprecated` — admin: manage modules (Category B deprecation pattern, since modules are templates that instances — account manifests — reference)
- `POST /website/registration/list` — admin: list registrations that contribute modules

Public content (news, trailers, catalog) is itself delivered via modules — the "catalog" module is one of the base modules every anonymous client receives.

### Frozen decision marker

The Website plugin's current schema (`schemas/website-api.yaml`) should be considered effectively deprecated in favor of the new module-composable design. Before any implementation begins, the schema should be rewritten to match the architecture above.

---

## Part 3: Per-Game Companion Modules

### Module capability categories

Companion modules vary widely in scope. Categories include:

| Category | Example modules | Requires |
|---|---|---|
| **Content display** | Compendium, lore browser, achievement list | Collection + Documentation data |
| **Social** | Chat, friend list, message board, clan features | Chat + account link |
| **Notifications** | Game event notifications, reminders, friend activity | Push infrastructure |
| **Cross-platform sync** | Save file browser, cloud-backed preferences | Save-Load + account link |
| **Mini-games** | Daily scouting, troop training, crafting queue | Local Actor runtime + server sync |
| **Marketplace (Arcadia only)** | Browse, install, manage content packs | Marketplace plugin (future) |
| **Creator tools (Arcadia only)** | Upload, submission status, payouts | Marketplace + creator tier |

### Defenders Companion Module (illustrative)

Defenders is Teen-rated, standalone, uses Bannou embedded in the game itself. Its companion module (optional, unlocked after Beyond Immersion account link + Defenders ownership) could include:

- **Compendium**: bestiary, character lore, region descriptions, boss records
- **Leaderboards**: arcade mode rankings, friend comparisons
- **Troop academy**: daily training mini-game → small XP bonuses in main game
- **Scouting log**: review past scouting missions, upcoming wave forecasts
- **Clan chat**: if multiplayer/clan features ship
- **Notifications**: clan message received, new daily mission, boss challenge of the week

Content is Teen-rated — consistent with the game's rating and the app's overall rating.

### Arcadia Companion Module (illustrative)

Arcadia is 18+ account-required and full MMO. Its companion module is richer:

- **Spirit journal**: twin-spirit pair messaging, shared dream log (PLAYER-VISION)
- **Household overview**: current characters, ongoing generational status, archive highlights
- **Void drift** (limited): browse scenarios, accept mini-scenarios playable in-app
- **Marketplace**: browse playlists, curate, follow creators
- **Creator tools** (creator tier only): upload, manage submissions
- **Notifications**: character lifecycle events, pair-spirit messages, marketplace sales (for creators), subscription reminders

Content is appropriate for Arcadia's content rating (T/M expected) — still not AO. The 18+ requirement is contractual, not content-derived.

### Mini-games with real in-game impact

Local mini-games in the Beyond Immersion app can affect main-game state via these patterns:

1. **Trickle rewards**: Small persistent bonuses for completing mini-games (e.g., +5% XP for next play session)
2. **Queue management**: Schedule crafts/trades/journeys that progress while the main game is not running
3. **Discovery**: Find lore/NPCs/locations in the mini-game that unlock in the main game
4. **Cosmetics**: Earn companion-only cosmetic rewards visible in-game (hats, titles)

Server-authoritative validation prevents cheating: the cloud Bannou reconciles mini-game results against server-stored state (daily caps, rate limits, anti-exploit rules) before applying them to the main game.

---

## Part 4: Push Notification Architecture

### Why pushes are not just client events

Client events via `IClientEventPublisher` deliver to WebSocket sessions: per-session, realtime, per-event. Pushes must be:

1. **Per-device, not per-session** (device may have no active session)
2. **Per-account aggregated** (same device may be logged into multiple games/modules)
3. **Batched** (10 chat messages ≠ 10 pushes)
4. **Deduplicated** (multiple character-update events → one summary)
5. **Rate-limited** (APNs/FCM quotas, user experience limits)
6. **Prioritized** (friend DM ≠ leaderboard change)
7. **Rules-driven** (per-event config determines push eligibility and presentation)
8. **Platform-configurable** (APNs/FCM payload structure, iOS/Android differences)

### The critical gotcha: the no-session short-circuit

`EntitySessionRegistry.PublishToEntitySessionsAsync` has this shortcut:

```csharp
if (sessions.Count == 0) { return 0; }  // nothing published
```

**This is exactly the case push cares about most.** If a player has zero active WebSocket sessions (app closed), no client event reaches RabbitMQ. Tapping at IClientEventPublisher or at the RabbitMQ exchange level both miss this case for the same reason: publish doesn't happen.

The tap must be at the IEntitySessionRegistry level, and the registry must be refactored to fire observer dispatch independently of session dispatch. This is D12.

### Architecture (end-to-end)

```
Plugin publishes entity-scoped client event
 │
 ▼
IEntitySessionRegistry.PublishToEntitySessionsAsync(entityType, entityId, event)
 │ [Connect-hosted, refactored]
 │
 ├──► Observer dispatch (ALWAYS fires, regardless of session count)
 │    │
 │    └──► For each IEntityEventObserver in IEnumerable<T>:
 │         └──► observer.OnEntityEventAsync(entityType, entityId, event, ct)
 │              │
 │              └──► lib-push PushEventObserver:
 │                   ├── Apply push rule engine (event eligible? rate-limited? muted?)
 │                   ├── Determine scope from x-push-config (account | possession)
 │                   ├── If account-scope: lookup push-interest[entityType:entityId] → accountIds
 │                   ├── If possession-scope: lookup Gardener's possession binding → accountId (or none)
 │                   ├── For each account: lookup device tokens, apply preferences, exclude session device
 │                   └── Enqueue per-device into Redis pending-push queue
 │
 └──► Session dispatch (only fires if sessions exist)
      │
      └──► IClientEventPublisher.PublishToSessionsAsync(sessionIds, event)
           └──► RabbitMQ → session queue → Connect → WebSocket

(meanwhile, on any node)
PushDispatchWorker (BackgroundService)
 │
 ├── Claim pending-push entries from Redis queue (distributed-safe via atomic pop or lock)
 ├── Apply batching (EventBatcher / DeduplicatingEventBatcher pattern)
 │    ├── Accumulate events per device over the batching window
 │    ├── Dedup per entity+action
 │    └── On window close or threshold: format summary payload
 │
 ├── Apply per-account preferences (quiet hours, per-game mute, per-category mute)
 │
 └── Dispatch to APNs (iOS) or FCM (Android) gateway
      │
      ├── On success: update device.lastConfirmedValidAt
      └── On invalidation signal: delete device token (D14 auto-cleanup)
```

### Sender mode vs. receiver mode (same plugin, two configs)

`lib-push` operates in two modes, selected by `PUSH_MODE` configuration (`sender` | `receiver` | `both`). Same schemas, same plugin code, different active code paths:

| Mode | Active in | Active code | Consumes | Produces |
|---|---|---|---|---|
| `sender` | Cloud Bannou | PushEventObserver, PushDispatchWorker, APNs/FCM adapter | IEntityEventObserver events | APNs/FCM push messages |
| `receiver` | Beyond Immersion app's embedded Bannou | OS notification payload handler, navigation command emitter | OS push callbacks | Calls to `/website/navigation/handle` |
| `both` | Development/testing | All of the above | — | — |

In production, cloud Bannou runs `sender` mode and the BI app's embedded Bannou runs `receiver` mode. Defenders (embedded) itself could publish events that become pushes (publisher role) IF Defenders is cloud-synced at the time. A Bannou instance can be a publisher AND a receiver independently — these aren't mutually exclusive roles.

### Sender side: dispatch flow in detail

1. **Observer fires** (`PushEventObserver.OnEntityEventAsync`): receives entityType, entityId, event.
2. **Push rule lookup**: per-event config (declared in each plugin's `-service-events.yaml` via a new `x-push-config` extension) determines eligibility, scope, category, priority, batching rule.
3. **Scope-driven audience resolution** (Push resolves entirely from its own state — no external service calls, no accountIds in events per T32):
   - **Account scope**: lookup `push-interest[entityType:entityId]` → Set of accountIds. This index was populated by `IEntityRegistrationObserver` during prior session activity (see D16). If no accounts are interested, no push.
   - **Possession scope**: lookup Gardener's active possession binding for the entity → accountId. If no binding exists (player not actively possessing), no push (see D18).
4. **Preference check**: per-account preferences (quiet hours, muted categories, muted games) filter out ineligible devices.
5. **Session-device exclusion**: for each account, if any of their devices holds the WebSocket session that received this event, exclude that device (WebSocket already delivered it).
6. **Enqueue**: remaining (account, device, event) tuples enqueued into Redis pending-push queue (distributed). Observer returns.
7. **Dispatch worker**: on any node, `PushDispatchWorker` claims entries, applies batching via `EventBatcher` or `DeduplicatingEventBatcher` (from `bannou-service/Services/`), formats summary payload, dispatches to APNs/FCM.
8. **Delivery feedback**: APNs/FCM response parsed. Success → update `lastConfirmedValidAt`. Invalidation signals → delete token (auto-cleanup per D14).

### Receiver side: notification-to-navigation flow

1. **OS delivers push to device** (APNs/FCM delivers notification to iOS/Android).
2. **User taps notification** OR **app receives push while foregrounded** (both paths converge).
3. **Native OS notification handler** extracts the payload and passes it to `lib-push` receiver.
4. **lib-push receiver** (in app's embedded Bannou) calls `POST /website/navigation/handle` locally with the payload.
5. **Website plugin** (embedded CMS) resolves payload → module code + route + context. Handles "module not installed" gracefully (returns fallback / install prompt).
6. **Website plugin** returns a navigation command (or emits a client event) to the native UI layer.
7. **Native UI** renders the target module with the resolved context.

```
APNs/FCM → OS → app native handler
                    │
                    ▼
              lib-push receiver (embedded)
                    │
                    │ POST /website/navigation/handle
                    ▼
              Website plugin (embedded CMS)
                    │
                    │ navigation command
                    ▼
              Native UI layer (renders target module)
```

**Key property**: this entire flow is in-process within the app's embedded Bannou. No cloud call on notification tap. Cloud is only consulted if the notification references data the app doesn't have cached (which the app then lazily fetches after rendering the fallback/loading state).

### Push configuration schema (illustrative)

```yaml
# Declared per-event in service events schema, alongside x-event-publications
# New extension: x-push-config
x-push-config:
  chat.message.received:
    pushable: true
    category: social
    priority: high
    batchingRule: dedup-by-sender
    batchingWindowSeconds: 30
    rateLimit:
      perHourPerDevice: 60
      perDayPerDevice: 200
    payloadTemplate:
      title: "push.chat.message.title"  # localization key
      body: "push.chat.message.body"     # localization key with ${var} substitution
      deepLink: "module://chat?roomId={entityId}"
      category: "SOCIAL_MESSAGE"         # iOS Notification Category, Android Channel
    accountPreferenceOverridable: true
```

Most events default to `pushable: false`. Services opt-in per event.

### Device token registration APIs

Full CRUD — device registrations are persistent, not session data:

| Endpoint | Purpose |
|---|---|
| `POST /push/device/register` | Register device token (platform, token, app version, language, timezone, os version) |
| `POST /push/device/unregister` | Explicit removal (logout, opt-out) |
| `POST /push/device/refresh` | Token rotation (APNs/FCM rotate periodically) |
| `POST /push/device/list` | Account sees all its registered devices |
| `POST /push/device/preferences/get` | Per-device preferences |
| `POST /push/device/preferences/update` | Per-device preferences (quiet hours, muted categories) |
| `POST /push/account/preferences/update` | Account-wide defaults |

### Account preferences model

- Per-game mute ("silence Defenders notifications")
- Per-category mute ("silence leaderboard updates, keep friend DMs")
- Quiet hours ("no pushes 10pm-8am local time")
- Priority override ("only high-priority pushes during work hours")
- Per-device override (e.g., "work phone: only work-hours pushes; personal phone: always")

Stored in `push-account-preferences` and `push-device-preferences` state stores.

### Device token cleanup (D14 in detail)

**Signal-based auto-cleanup** (immediate):

| Platform | Signal | Action |
|---|---|---|
| APNs | `410 Unregistered` | Delete token |
| APNs | `400 BadDeviceToken` | Delete token |
| FCM | `UNREGISTERED` | Delete token |
| FCM | `INVALID_ARGUMENT` | Delete token |
| FCM | `SENDER_ID_MISMATCH` | Delete token (config mismatch) |

**Reconciliation worker** (periodic):
- Scans for tokens with `lastConfirmedValidAt` older than `TokenReconciliationThresholdDays` (default 90)
- Dry-run push (silent/silent-push to APNs, data-only to FCM)
- If invalidation returned, delete; otherwise update `lastConfirmedValidAt`
- Configurable interval (default 24 hours)

**Account deletion cleanup**: `PushServiceEvents.HandleAccountDeletedAsync` removes all device tokens, preferences, and pending queue entries for the account (per Account Deletion Cleanup Obligation).

### New plugin components (lib-push summary)

| Component | Purpose |
|---|---|
| `PushService` (main) | Device registration/preferences APIs |
| `PushEventObserver` | IEntityEventObserver implementation — enqueues events |
| `PushDispatchWorker` | BackgroundService — claims queue entries, batches, dispatches |
| `ApnsGateway` | APNs HTTP/2 adapter (mTLS, p8 auth key) |
| `FcmGateway` | FCM HTTP adapter (OAuth service account auth) |
| `PushBatcher` | Uses `EventBatcher` / `DeduplicatingEventBatcher` from bannou-service |
| `PushRuleEngine` | Evaluates x-push-config per event (scope, category, batching, priority) |
| `PushInterestManager` | Maintains push-interest index (entity→accounts), seeded from IEntityRegistrationObserver |
| `PushRegistrationObserver` | IEntityRegistrationObserver implementation — resolves session→account, updates interest index |
| `TokenReconciliationWorker` | BackgroundService — periodic dead-token cleanup |
| `OsNotificationReceiver` (receiver mode) | OS push payload → /website/navigation/handle |

### State stores

| Store | Backend | Purpose |
|---|---|---|
| `push-device-tokens` | MySQL | Persistent device registrations per account |
| `push-account-preferences` | MySQL | Account-wide push preferences |
| `push-device-preferences` | MySQL | Per-device preference overrides |
| `push-pending-queue` | Redis | Distributed queue of pending push events |
| `push-batcher-state` | Redis | Per-device batch accumulators (if using Redis-backed batchers instead of per-node) |
| `push-rate-limits` | Redis | Per-device rate-limit counters (TTL'd) |
| `push-interest` | Redis | Persistent entity→accounts interest index (seeded from session registrations via IEntityRegistrationObserver, cleaned on entity/account deletion) |

### Distribution and scale considerations

- **Observer fires on publish node only** — distributed safety requires writing to Redis pending queue before observer returns (see D12)
- **Dispatch worker scales horizontally** — multiple nodes can run the worker; Redis atomic pops prevent double-dispatch
- **APNs/FCM connection pooling** — HTTP/2 connections are long-lived; each node maintains its own pool
- **Rate limits are per-device globally** — must be tracked in Redis (not per-node) to avoid exceeding APNs/FCM quotas across fleet

---

## Part 5: The Diegetic Beyond Immersion Brand

### The core mechanism

Arcadia's Omega realm (cyberpunk meta-game) has an in-universe VR hardware manufacturer: **Beyond Immersion**. In-game, characters use Beyond Immersion rigs to "dive" into fantasy simulations (which is how Arcadia frames itself within the fiction — the Fantasia/Arcadia/Omega realm stack).

In reality, Beyond Immersion is the publisher. The mobile app is called Beyond Immersion. The real logo, visual design, and brand identity are shared between:

- The real-world publisher / app
- The in-universe hardware manufacturer / rig / UI that Omega characters interact with

### What this means in practice

- When a player in Omega (in-game) opens their VR rig control panel, it looks like the real Beyond Immersion app
- Advertisements for Beyond Immersion products that appear in-game are functionally real advertisements (the app/product exists)
- Real-world marketing can blur: a trailer for the mobile app could be shot as an in-universe Beyond Immersion commercial (and vice versa)
- Players who notice will experience a subtle fourth-wall pressure: "if I'm holding a Beyond Immersion device right now, and my character in Omega is using a Beyond Immersion device... what does that imply?"

### Design commitments this creates

- **Visual design is locked in** to cyberpunk aesthetic from day one — both the in-universe rendering and the real app must render consistently
- **Brand identity evolves in sync** — a redesign of the app is a retcon of the in-universe brand, or vice versa; they cannot diverge
- **Typography, color palette, sound design** are dual-purpose artifacts
- **Marketing can exploit the blur** — cross-promotional materials that treat the real app as an in-universe product

### What Omega's existing design already supports

PLAYER-VISION.md already describes Omega as "diegetic UX progression" — the player's full-dive VR machine has a literal configuration screen where UX modules are allocated to dive sessions. This is exactly the interface the companion app provides. The real app becomes the diegetic rig interface, not just a marketing skin on top of one.

### The marketing hook

> "Your phone is already a Beyond Immersion device. Have you wondered what else it can do?"

Campaigns that position the real app as a primer or teaser for Omega's cyberpunk premise create a marketing/product feedback loop that no other publisher has.

---

## Part 6: Plugin Impact Matrix

Comprehensive list of changes this architecture requires across Bannou plugins:

### New plugins

| Plugin | Layer | Purpose |
|---|---|---|
| **lib-push** | L3 | Push notification infrastructure — APNs/FCM delivery, device registry, batching, rule engine (sender mode); OS payload handling (receiver mode). See D11, Part 4. |

### New shared infrastructure

| Component | Location | Purpose |
|---|---|---|
| `IEntityEventObserver` interface | `bannou-service/Providers/` | DI interface for plugins to observe entity-scoped client event publishes. Fires regardless of session count. See D12. |
| `IEntityRegistrationObserver` interface | `bannou-service/Providers/` | DI interface for plugins to observe entity-session registrations/unregistrations. Push uses this to build its persistent interest index (see D16). |

### Redesigned plugins

| Plugin | Layer | Change |
|---|---|---|
| **Website** | L3 | Complete redesign from stub to module-composable CMS — new schema, new endpoints, module registration and manifest composition. Adds `POST /website/navigation/handle` endpoint for receiver-side navigation routing (D13). |
| **Connect** | L1 | Refactor `EntitySessionRegistry`: (1) `PublishToEntitySessionsAsync` splits observer dispatch from session dispatch, injects `IEnumerable<IEntityEventObserver>` (D12); (2) `RegisterAsync`/`UnregisterAsync`/`UnregisterSessionAsync` notify `IEnumerable<IEntityRegistrationObserver>` (D16). No push delivery logic added — push lives entirely in lib-push per D11. |
| **Auth** | L1 | Platform-native OAuth providers (Game Center, Play Games Services) added alongside existing Steam/email/OAuth |

### Extended plugins

| Plugin | Layer | Change |
|---|---|---|
| **Chat** | L1 | Content pack compatibility (companion app ships with cached chat UI and message history); declares `x-push-config` for chat events |
| **Collection** | L2 | Compendium entries delivered as content packs; offline-first access to unlocked data |
| **Documentation** | L3 | Lore content delivered as content packs for offline browsing |
| **Actor** | L2 | Local ABML runtime for mini-games in the companion app; sync boundary with cloud for results |
| **Seed** | L2 | Local seed state for mini-game progression; sync boundary |
| **Save-Load** | L4 | Cross-platform cloud save unification when account is linked |
| **Marketplace** (planning, Arcadia) | L4 | Marketplace browse/publish/manage — future work |
| **DeviceInfo standard** | Reference | Extended with push token fields if needed |
| **Any plugin with x-push-config events** | Various | Declares push configuration per event; no code changes required (declarative only) |

### Unchanged plugins

All other plugins (Character, Seed, Currency, etc.) are unaffected — they publish events normally. Whether those events become pushes is determined by declarative config, not by changes to the publishing plugin.

### Embedded-mode implications

The Beyond Immersion app runs Bannou in embedded mode (see [BANNOU-EMBEDDED.md](BANNOU-EMBEDDED.md)). Plugins loaded locally in the app need to function without:

- RabbitMQ (InMemory message bus used instead)
- Redis (InMemory or SQLite state store used instead)
- Cloud mesh (DirectDispatch used instead)

Existing embedded-mode support covers this. Content packs register plugins into the embedded instance at install time. Uninstalling a game unregisters the relevant plugins cleanly.

---

## Part 7: Anonymous → Account Upgrade Flow

### The flow

```
1. Player installs Defenders of Ba'hava on iOS
 └── First launch: Game Center auth (silent if enabled)
 └── Bannou embedded instance uses Game Center player ID as externalId
 └── 20 hours of gameplay, leaderboard entries, cloud saves via Game Center

2. Player installs Beyond Immersion app
 └── Browses catalog anonymously — no account needed
 └── Sees "Connect your games" prompt

3. Player creates Beyond Immersion account (on web)
 └── 18+ age attestation + credit card age signal
 └── Account created, linked to email
 └── Back in the app: login prompt

4. Player logs in to Beyond Immersion app
 └── App detects Game Center is active on device
 └── Prompts: "Link your Game Center identity to this account?"
 └── Player confirms

5. Auth plugin links Game Center player ID → Beyond Immersion account
 └── All prior Defenders save data now accessible via the account
 └── Cross-platform saves enabled
 └── Companion modules for Defenders unlocked
 └── Push notifications begin (if opted in)

6. Later: player installs Defenders on Android
 └── Play Games auth at first launch
 └── Bannou: "Is this the same player?" — prompts to link
 └── Player confirms — Play Games ID also linked to same account
 └── Saves merge, all platforms share state
```

### Identity merge semantics

- **Additive**: linking a platform ID doesn't remove prior access; the player can still play Defenders anonymously on any unlinked device with the same Game Center ID
- **Conflict handling**: if a platform ID is already linked to a different account, resolution prompt required (keep on existing account? transfer? error?)
- **Unlink flow**: account owner can unlink platform IDs (e.g., sold the device, switched accounts) — unlinks do not delete save data, just sever the binding

### What the Auth plugin needs

- Game Center as OAuth provider (Apple's GameKit)
- Play Games Services as OAuth provider (Google's GamesSignIn)
- Provider linking endpoint (already exists in principle)
- Account merge flow (partially new)

Mostly existing machinery; a few new provider implementations and the merge flow are the net-new work.

---

## Part 8: Open Questions

### OQ1: Push delivery — inside Connect or as its own plugin? [RESOLVED]

**Resolution**: New plugin `lib-push` at L3. See D11. Connect's only change is the `EntitySessionRegistry` refactor to support `IEntityEventObserver`; push logic lives entirely in lib-push.

---

### OQ2: Module versioning and compatibility

**Question**: How are content pack versions managed when a game updates? What happens when the embedded Bannou runtime version is behind a required module version?

**Options**:
- Strict compatibility: module declares `minRuntimeVersion`; app refuses to load incompatible modules and prompts for app update
- Loose compatibility: modules downgrade gracefully when runtime lags
- Continuous: app always runs latest module version

**Trade-offs**: strict is safest but forces app updates; loose requires per-module compatibility testing; continuous means every module update triggers a download

**Needs**: module version manifest format + app update prompt UX + CDN rollback capability

---

### OQ3: Content pack signing and distribution

**Question**: Who signs content packs? Where does the CDN live? How does the app verify authenticity?

**Options**:
- Beyond Immersion signs all content packs (centralized)
- Per-game signing (each game studio has its own keys)
- CDN: MinIO/S3-backed via lib-asset infrastructure, or dedicated CDN (Cloudflare, CloudFront)

**Needs**: signing infrastructure, verification at install time, revocation mechanism

---

### OQ4: Agency + push — shared mechanism or parallel systems? [RESOLVED]

**Resolution**: Parallel systems. They operate at different scopes (Agency: per-session event rewriting for UX fidelity; Push: per-device event batching via `IEntityEventObserver`). No shared mechanism required — Agency's eventual per-session event transformation pipeline lives in Connect's session-delivery branch, while push's observer-based tap lives at the registry level and is independent of session state. Consolidation remains an option if future work reveals genuine overlap, but none is mandated by the design.

---

### OQ13: Publisher/receiver mode edge cases

**Question**: Some deployment scenarios have ambiguous or dual roles. A few specific cases to think through when implementing lib-push:

1. **Defenders (embedded, cloud-synced) publishing events**: if Defenders' local Bannou publishes a client event that qualifies for push, does the push originate from Defenders' embedded instance or from the cloud Bannou that receives the sync? Likely the cloud side (to keep APNs/FCM credentials centralized), but the embedded instance needs to know when to forward events to cloud vs. handle them locally.

2. **Development/testing `both` mode**: when is this actually useful, and are there footguns? E.g., self-pushing-to-self during dev if not careful about audience resolution.

3. **BI app sending pushes about itself**: if a user's own action in the BI app (e.g., sending a chat message via the app) generates a push, should that user be excluded from the push audience to avoid self-notifications?

4. **Multi-device same-account**: user has BI app on phone and tablet. Both have device tokens registered. Chat message arrives. Does every device receive a push? (Probably yes, but with quiet hours / priority per-device overrides.) Does reading the message on phone suppress the push on tablet? (iOS supports remote notification management; Android has varying support.)

**Timing**: Think through during lib-push implementation planning, not before.

---

### OQ5: Cross-game creator identity

**Question**: If creators eventually produce content for multiple Beyond Immersion games (Defenders cosmetics, Arcadia marketplace packs), is there one Beyond Immersion creator identity or per-game?

**Rationale for unified**: simpler creator experience, portfolios carry across games, single payout flow
**Rationale for per-game**: each game's content standards and review processes may differ significantly

**Timing**: Arcadia's marketplace is the first real creator economy. Defenders is DLC-only. This question can be deferred until there are two games with creator economies.

---

### OQ6: Mini-game anti-cheat

**Question**: Companion app mini-games affect main-game state. How do we prevent manipulation?

**Sketch**:
- Server-authoritative validation at mini-game result submission
- Rate limits per account per time window (can't spam mini-game wins)
- Reproducibility: same inputs produce same output (deterministic runs serverside if needed)
- Anomaly detection: flag players whose mini-game completion rates exceed statistical norms

**Needs**: per-mini-game validation rules, cross-check infrastructure, anomaly reporting

---

### OQ7: What if a game removes its companion module post-launch?

**Question**: Player has content pack installed. Game pushes a new version that removes some module capabilities. How is this handled?

**Options**:
- Backward compatibility: old module version continues to work until user uninstalls
- Forced update: content pack update removes the module, player sees it disappear
- Deprecation: module marked deprecated, remains usable for N days, then removed

**Recommendation**: Category B deprecation pattern (mirrors tenets for other template entities) — deprecate, clean-deprecated sweep removes it eventually

---

### OQ8: Arcadia-specific vs. Beyond Immersion-wide push preferences

**Question**: Users may want different push preferences per game. UI-wise, is this a per-game setting tree or a global setting with per-game overrides?

**Options**:
- Per-game: clean separation, but complex UI
- Global with overrides: simpler default, power users customize per game

**Recommendation**: global defaults with per-game mute toggles, full per-category customization only at global level

---

### OQ9: How does Defenders learn it has a Beyond Immersion account linked?

**Question**: Defenders runs Bannou embedded, standalone. When the player later links to a Beyond Immersion account via the companion app, how does Defenders (running on a separate device) learn about the link and start syncing?

**Options**:
- On next Defenders launch: check with cloud Bannou via a lightweight "has this Game Center ID been linked to an account?" query
- Push: Beyond Immersion account linking triggers a push to Defenders (if Defenders has push subscribed)
- Explicit: user manually triggers sync in Defenders after linking

**Recommendation**: On-launch check (simplest, works for players who don't enable push)

---

### OQ10: Do we need a web version of the Beyond Immersion app?

**Question**: The app is described as mobile (iOS/Android). What about browser access? Linux users?

**Options**:
- Mobile only (at launch): simplest, focused
- Web from day one: broader reach, but another target to maintain
- Web later: ship mobile first, follow with web

**Recommendation**: Mobile first. Web follows when the module infrastructure is battle-tested. The Website plugin's CMS is web-compatible by design — the web app would render the same manifest differently.

---

### OQ11: Storefront or links-out?

**Question**: Can players purchase games from within the Beyond Immersion app, or does it link out to Apple/Google/Steam store listings?

**Constraints**:
- Apple requires in-app purchases use Apple's IAP for digital goods (30% cut)
- Google similar
- Steam is outside phone store policies

**Options**:
- Store listings only (link out) — no IAP revenue cut, but friction
- In-app purchases for subscriptions/DLC — pays the 30% cut
- Hybrid — games themselves are external, microtransactions via store IAP

**Recommendation**: external links for game purchases; evaluate IAP for Arcadia subscription when that ships (some precedent for linking-out if the app doesn't sell digital goods)

---

### OQ12: Beyond Immersion account number-gating

**Question**: Is the 18+ verification "trust the checkbox + credit card" (Patreon model), or "upload ID" (Persona/Jumio model)?

**Trade-offs**:
- Checkbox + CC: low friction, acceptable for most jurisdictions
- ID upload: strict verification, required in some jurisdictions (Germany, Australia), higher friction

**Recommendation**: Checkbox + CC by default; ID upload for creator tier (required for payment processor compliance anyway via Stripe Connect); region-specific ID upload where legally required

---

## Part 9: Non-Goals / Explicit Exclusions

The following are **explicitly not** part of this architecture:

1. **Not a 13+ account tier.** Beyond Immersion accounts are 18+ only. No parental consent infrastructure.
2. **Not a standalone game portal.** Individual games ship on store listings separately; the Beyond Immersion app is a companion/platform app, not a game launcher.
3. **Not an AO content platform.** The 18+ requirement is contractual; content ratings are determined by actual content (Teen/Mature expected).
4. **Not a marketplace for Defenders.** Defenders DLC is developer-authored only. Marketplace is Arcadia-tier and later.
5. **Not a game engine.** The embedded Bannou instance provides backend services; UI rendering is native/engine-specific (not Bannou's responsibility).
6. **Not a chat platform product.** Chat is a feature; the app isn't trying to compete with Discord.
7. **Not a streaming platform.** Showtime integration exists but the app doesn't host streaming infrastructure (relies on external services).
8. **Not a replacement for store-native saves and achievements on initial launch.** Game Center/Play Games/Steam handle these at the platform level; account-linked sync is additive.
9. **No accountIds in events.** Push audience is resolved entirely from Push's own interest index (D16), never from event payloads. Events are published to sessions, not accounts (T32).
10. **No offline character push.** Character-scoped events do not push when no possession binding exists (D18). The player discovers what happened in-game when they return.
11. **No reconnect summary events.** There is no "here's what happened while you were away" push or event on reconnect (D18). Discovery IS the experience.
12. **Device tokens are not deprecatable.** Immediate hard delete per T31 decision tree (D17). No deprecation lifecycle for device registrations.
13. **Not all of this at once.** Sequencing applies (see Part 10).

---

## Part 10: Sequencing Considerations

Rough priority ordering for when this becomes real work:

### Phase 1 (Before/alongside Defenders launch)

- Platform-native identity providers in Auth (Game Center, Play Games) — **required for Defenders to ship with leaderboards/saves**
- DeviceInfo extensions for mobile if needed
- Bannou embedded mode validation on mobile platforms

### Phase 2 (Early after Defenders launch, before Beyond Immersion app ships)

- Website plugin redesign: schema + module registry + manifest composition endpoints
- Content pack format specification + signing + CDN infrastructure
- Defenders companion module (first content pack)
- Account creation flow on web (with 18+ verification)
- Anonymous → account upgrade flow in Auth

### Phase 3 (Beyond Immersion app v1)

- Mobile app shell with embedded Bannou runtime
- Module loader and content pack manager in app
- Basic chat, compendium, notifications
- WebSocket to cloud Connect for realtime

### Phase 4 (Push notifications)

- `IEntityEventObserver` interface in `bannou-service/Providers/` (D12)
- `IEntityRegistrationObserver` interface in `bannou-service/Providers/` (D16)
- `EntitySessionRegistry` refactor: PublishToEntitySessionsAsync (split observer from session dispatch), RegisterAsync/UnregisterAsync (notify registration observers)
- `lib-push` plugin scaffolding (L3, sender + receiver modes)
- Device token registration APIs + state stores
- Push interest index (`push-interest` Redis store) + PushInterestManager + PushRegistrationObserver (D16)
- Gardener possession-binding integration: Gardener calls Push API at possession start/end (D15)
- Redis pending-push queue
- `PushDispatchWorker` with EventBatcher / DeduplicatingEventBatcher integration
- APNs gateway (HTTP/2, mTLS, p8 auth key)
- FCM gateway (HTTP, OAuth service account)
- Push rule engine + `x-push-config` extension attribute in events schemas
- Per-account and per-device preference state stores
- `TokenReconciliationWorker` for periodic dead-token cleanup
- Account deletion cleanup handler
- `POST /website/navigation/handle` endpoint in Website (D13)
- Receiver-mode OS notification payload handler in `lib-push` (for BI app's embedded Bannou)

### Phase 5 (Arcadia-adjacent work)

- Marketplace plugin (Arcadia tier)
- Creator account onboarding (Stripe Connect)
- Arcadia companion module
- Full compositional cinematics and related player-facing features

### Phase 6 (Post-launch polish)

- Web version of Beyond Immersion app
- Cross-game creator tools (if applicable)
- Advanced mini-game anti-cheat infrastructure
- Localization polish, accessibility

This sequencing is indicative, not prescriptive. The actual phase boundaries will be determined by available resources, dependencies, and strategic priorities.

---

## Appendix A: Relationship to Existing Planning Documents

| Document | Relationship |
|---|---|
| [BANNOU-EMBEDDED.md](BANNOU-EMBEDDED.md) | This doc depends on embedded mode being fully functional on mobile platforms |
| [DEPLOYMENT-MODES.md](DEPLOYMENT-MODES.md) | Beyond Immersion app is an embedded deployment; Arcadia is cloud; Defenders can be either |
| [VISION.md](../reference/VISION.md) | Architecture aligns with the 5 North Stars (Content Flywheel, Ship Games Fast, etc.) |
| [PLAYER-VISION.md](../reference/PLAYER-VISION.md) | Omega's "diegetic UX progression" is realized via the Beyond Immersion app's diegetic brand |
| [AGENCY.md](../plugins/AGENCY.md) | Agency's Potential Extension #10 (per-session event transformation) operates in parallel — Agency handles per-session UX fidelity filtering; Push handles per-device notification batching. No shared mechanism (OQ4 resolved). |
| [defenders-kb](https://github.com/) (external) | Defenders of Ba'hava is the first consumer of this architecture |

---

## Appendix B: Decision Change Log

All decisions above were made across planning sessions. Future changes to decisions should be logged here with date, rationale, and impact on the rest of the document.

| Date | Change | Rationale |
|---|---|---|
| (initial) | Document created with D1–D10 | Baseline strategic + architectural framing for the Beyond Immersion app, module-composable CMS, push notifications as new delivery channel, and platform-native identity |
| (follow-up 1) | Added D11–D14; rewrote Part 4; updated Part 6 plugin impact matrix; resolved OQ1 and OQ4; added OQ13 | Push plugin architecture: (D11) lib-push as new L3 plugin; (D12) IEntityEventObserver DI tap; (D13) Website navigation endpoint; (D14) auto-cleanup on APNs/FCM signals. |
| (follow-up 2) | Added D15–D18; corrected Part 4 audience resolution; updated Part 6, non-goals, Phase 4 sequencing; fixed stale references | Major correction: audience resolution was redesigned after T32 violation identified. Events NEVER carry accountIds. Push resolves audience from its own persistent interest index seeded via new IEntityRegistrationObserver (D16). Two-scope model formalized: account-persistent + possession-ephemeral-via-Gardener (D15). Device tokens confirmed not deprecatable (D17). Offline character push and reconnect summary events explicitly excluded (D18). PushAudienceResolver concept removed entirely — replaced by PushInterestManager + PushRegistrationObserver. Agency reference in Appendix A corrected to reflect resolved OQ4 (parallel systems). Non-goals expanded from 9 to 13 items. Decision count updated to 18. |

---

*This document captures a moment of strategic and architectural clarity. It is not frozen — open questions exist for a reason, and decisions may evolve as implementation reveals constraints the planning couldn't anticipate. It IS an authoritative reference for the decisions that have been made, and deviations from those decisions should be documented in Appendix B, not done silently.*
