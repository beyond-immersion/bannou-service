# Why Is the Contract Service at L1 (App Foundation) Instead of a Game Layer?

> **Short Answer**: Because Contract provides a generic finite state machine with consent flows and milestone-based progression. It knows nothing about games, quests, escrow, or any specific domain. It is reusable infrastructure -- like a database or a message broker, but for multi-party agreements.

---

## What Contract Actually Does

The Contract service manages **binding agreements between entities**. Specifically:

- **Templates** define the structure of an agreement: party roles, milestones, terms, enforcement mode, and prebound API callbacks that execute on state transitions.
- **Instances** track the lifecycle of a specific agreement: consent gathering, sequential milestone progression, breach detection, and completion.
- **Prebound API execution**: When a milestone completes, Contract calls pre-configured HTTP endpoints on other services. Contract does not know what those endpoints do -- it just calls them.

Contract is, at its core, a **reactive finite state machine with multi-party consent**. External systems report condition fulfillment via API calls. Contract stores state, advances the FSM, emits events, and executes callbacks. It has no domain logic of its own.

---

## The Objection

The objection is reasonable: contracts sound like a game concept. Quests have objectives. Trades need escrow agreements. NPCs sign employment contracts. These are all game mechanics. Why would the service that powers them sit at L1 alongside authentication and account management instead of at L2 (Game Foundation) or L4 (Game Features)?

The answer is that Contract's machinery is domain-agnostic. The FSM, consent flow, milestone tracking, and prebound API execution are general-purpose primitives. What makes them "game-like" is the consumers, not the service itself.

---

## How Contract Is Actually Used

### Quest System (L2)

Quest is a thin orchestration layer over Contract. Each quest objective maps to a contract milestone. Quest rewards are prebound API callbacks that execute when milestones complete.

```
Quest defines: "Kill 10 wolves"
    -> Translated to Contract: Milestone with condition reporting
    -> When milestone completes: Prebound API grants XP and items
```

Quest provides the game-flavored API ("accept quest", "track progress"). Contract provides the state machine ("milestone reported", "advance to next", "execute callbacks").

### Escrow System (L4)

Escrow uses Contract for conditional releases -- when a contract is fulfilled, the escrowed assets are released. Contract does not know about currencies, items, or asset types. It just tracks milestone completion and fires callbacks.

```
Escrow creates: Conditional release contract
    -> Contract tracks: Milestone progression
    -> On completion: Prebound API tells Escrow to release assets
```

### License Boards (L4)

License uses Contract for node unlock behavior. Each license node unlock goes through a contract with prebound APIs that handle LP deduction and capability granting.

---

## The L1 Justification

Contract qualifies for L1 (App Foundation) because:

**1. Zero game dependencies.** Contract depends on lib-state, lib-messaging, lib-mesh, and (problematically) lib-location. It does not depend on Character, Realm, Currency, Item, or any game-specific service. It has no concept of "player", "NPC", "world", or "game." Its entities are opaque IDs with types.

**2. Useful outside games.** A SaaS platform could use Contract for: service-level agreements with milestone tracking, multi-party approval workflows, subscription contracts with term enforcement, or vendor agreements with automated payment callbacks on delivery milestones. None of these are game concepts.

**3. Multiple consumers across layers.** Quest (L2), Escrow (L4), and License (L4) all depend on Contract. If Contract were at L2, that would be fine for Quest but would create an unnecessary layer coupling for the L4 consumers. At L1, all layers can depend on it without hierarchy concerns.

**4. The "is it infrastructure?" test.** Would you expect this service to exist in a non-game deployment? Yes. Multi-party agreements with consent flows and state machine tracking are a common business requirement. Just as Auth exists because any deployment needs authentication, Contract exists because any deployment with multi-party workflows needs agreement management.

---

## The Honest Caveat

Contract has a **known L1-to-L2 hierarchy violation**: it depends on lib-location for territory constraint checking. This is documented as a known issue with identified remediation paths (remove the dependency, move territory logic to an L4 extension service, or have Location provide its own validation definition to Contract).

This violation does not change the fundamental justification for Contract being L1. Territory constraints are a single feature that leaked across the boundary. The service's core machinery -- FSM, consent, milestones, prebound APIs -- is entirely layer-agnostic. The violation should be fixed, not used as evidence that Contract belongs in a higher layer.

---

## The Alternative: Contract at L2

If Contract were at L2 (Game Foundation):

- L3 services (App Features) could not use it. Any non-game workflow needing agreement management would be out of luck.
- L4 services (Escrow, License) could still use it, but it would be categorized alongside Character, Realm, and Currency -- game-specific foundational entities. This misrepresents what Contract is. Contract is not a game entity. It is infrastructure that game entities consume.
- The conceptual clarity of L2 ("required for game deployments") would be diluted. Contract is required for ANY deployment that uses multi-party workflows, not just game deployments.

---

## The Pattern

Contract follows the same pattern as other L1 services:

| Service | Domain-Agnostic Machinery | Game-Specific Consumers |
|---------|--------------------------|------------------------|
| **Auth** | JWT generation, OAuth flows, session management | Game sessions, voice calls, matchmaking |
| **Permission** | RBAC matrix, capability manifests, state tracking | in_game state, in_match state, in_call state |
| **Resource** | Reference counting, cleanup coordination, compression | Character archival, NPC lifecycle, encounter cleanup |
| **Contract** | FSM, consent flows, milestone tracking, prebound APIs | Quest objectives, escrow releases, license unlocks |

Each provides general-purpose infrastructure that becomes game-specific only through how higher-layer services use it. Contract is not a game service. It is a state machine service that games happen to need.
