# Why Does the Escrow Service Need a 13-State State Machine?

> **Short Answer**: Because multi-party asset exchanges in a living economy have failure modes at every stage -- creation, deposit, consent, condition verification, release, and refund all need distinct states to prevent asset loss, double-spending, and deadlocks. The 13 states are the minimum needed to handle every real failure scenario, not an exercise in over-specification.

---

## What Escrow Actually Does

The Escrow service (L4) orchestrates multi-party asset exchanges. Not just "player A trades an item to player B." The full scope includes:

- **Two-party trades**: Alice gives 500 gold for Bob's sword.
- **Multi-party exchanges**: Three guild leaders each contribute resources to fund a joint expedition.
- **Conditional escrow**: A bounty held in escrow that releases when a contract milestone is fulfilled (e.g., "kill the dragon" quest completion triggers payment).
- **Auctions**: Multiple bidders deposit competing bids, winner's assets release to seller, losers' deposits refund.

Each of these involves multiple asset types (currency via lib-currency, items via lib-inventory, contracts via lib-contract), multiple parties who must consent independently, and multiple failure points where assets could be lost if the state machine is not precise about what state it is in.

---

## Walking Through the States

Consider a two-party conditional trade: Alice deposits 1,000 gold. Bob deposits a legendary sword. The trade completes when a contract milestone is fulfilled. If the contract is breached, everything refunds.

### Happy Path

1. **Created** -- The escrow exists but nobody has deposited anything yet. Parties have been assigned roles. This is the "lobby" state.

2. **Depositing** -- At least one party has begun depositing assets, but not all required deposits are in. Alice deposited her gold. Bob has not deposited his sword yet. The escrow must track partial deposits because if Bob never shows up, Alice needs her gold back.

3. **Funded** -- All required deposits are in. Both Alice's gold and Bob's sword are locked. But neither party has explicitly consented to the terms yet.

4. **Consenting** -- At least one party has consented, but not all. Alice clicked "accept." Bob is still reviewing. The escrow distinguishes this from Funded because consent can be revoked up until everyone consents.

5. **Consented** -- All parties have consented. The escrow is now binding. If there are conditions (linked contract milestones), we wait for them. If there are no conditions, we proceed directly to release.

6. **Conditional** -- The escrow is bound and waiting for external conditions. The linked contract is being executed. Neither party can unilaterally withdraw. This state exists specifically for conditional escrow types.

7. **Releasing** -- Conditions are met and the escrow is processing the asset transfers. Alice's gold is moving to Bob's wallet. Bob's sword is moving to Alice's inventory. This is an intermediate state because asset transfers are not atomic across services -- the currency transfer might succeed before the item transfer completes.

8. **Completed** -- All assets have been successfully transferred. The escrow is done. This is a terminal state.

### Failure Paths

9. **Cancelling** -- A party requested cancellation before all deposits were in (during Created or Depositing). Assets need to be returned to whoever has already deposited. This is not the same as refunding because the escrow was never fully funded.

10. **Cancelled** -- Cancellation complete. All deposited assets have been returned. Terminal state.

11. **Refunding** -- The escrow was funded and possibly consented, but a condition failed (contract breach), a timeout expired, or an authorized refund was requested. All assets are being returned to their original depositors. This is distinct from Cancelling because ALL parties have deposited and the asset return is from a fully funded state.

12. **Refunded** -- Refund complete. All assets returned. Terminal state.

13. **Disputed** -- Something went wrong during release or refund (partial transfer failure, service unavailability, conflicting state). The escrow is flagged for manual or automated resolution. This is the "we need a human or a recovery process" state.

---

## Why Each State Is Necessary

The tempting simplification is: "just use Created, InProgress, Completed, Failed." Here is what breaks:

### Without Depositing vs. Funded

If you cannot distinguish "some deposits are in" from "all deposits are in," you cannot safely process cancellations. A cancellation during partial deposit must return only the assets that were actually deposited. A refund from a fully funded state must return ALL assets. The refund logic is different because the set of assets to return is different.

### Without Consenting vs. Consented

Consent is per-party and revocable until unanimous. Without tracking partial consent, you cannot determine whether a party who wants to withdraw is revoking consent (allowed) or breaching a binding agreement (different consequences). The escrow needs to know whether it has crossed the "binding" threshold.

### Without Conditional

If the escrow is waiting for a contract milestone and you don't have a distinct state for it, the system cannot distinguish "waiting for conditions" from "waiting for consent" from "waiting for deposits." Each of these has different timeout behavior, different cancellation rules, and different event subscriptions.

### Without Releasing vs. Completed

Asset transfers across services are not atomic. Moving currency from escrow to recipient is a call to lib-currency. Moving an item is a call to lib-inventory. If the currency transfer succeeds but the item transfer fails, the escrow must know it is mid-release so it can retry the item transfer rather than double-release the currency. Without an intermediate Releasing state, a failure during transfer leaves the escrow in an ambiguous state.

### Without Disputed

Disputed is the acknowledgment that distributed systems fail. A network partition during release could leave assets in an inconsistent state. Rather than pretending this cannot happen, the Disputed state explicitly captures it so recovery processes (automated retry, manual intervention, compensating transactions) can address it.

---

## The Trust Mode Dimension

The 13 states interact with three trust modes:

- **Trustless**: The escrow service enforces everything. Conditions must be programmatically verifiable. No party has unilateral control.
- **Trusted Third Party**: A designated arbiter can force-release, force-refund, or resolve disputes.
- **Mutual Trust**: Either party can trigger release after consent, bypassing condition verification.

Each trust mode changes which state transitions are allowed and who can trigger them. In trustless mode, the transition from Conditional to Releasing happens only when the linked contract reports milestone completion. In mutual trust mode, either party can trigger it manually. In trusted third party mode, the arbiter can trigger it.

This means the state machine is not just 13 states -- it is 13 states with conditional transitions based on trust mode, escrow type, and which party is acting. The apparent complexity of 13 states is actually a simplification of the full transition space.

---

## Living Economy Requirements

The Escrow service exists because Arcadia's economy is NPC-driven. NPCs buy, sell, trade, and form commercial agreements autonomously. An NPC merchant purchasing bulk goods from an NPC supplier is a multi-party asset exchange that needs the same safety guarantees as a player-to-player trade.

At 100,000+ concurrent NPCs, economic transactions happen continuously. The state machine must handle:

- An NPC that begins a trade and then dies (cancellation during deposit)
- A conditional sale that depends on a quest completion that never happens (timeout during conditional state)
- A guild trade where one member disconnects (partial consent handling)
- A marketplace auction with 50 bidders (multi-party deposit and selective release)

Each of these is a real scenario that the 13-state machine handles cleanly. Fewer states would mean ambiguous failure modes. More states would mean transitions that never fire in practice.

---

## The Integration Surface

Escrow does not operate in isolation. It calls:

- **lib-currency** for deposit/release/refund of monetary assets
- **lib-inventory** for deposit/release/refund of items
- **lib-contract** for conditional release (contract milestone triggers state transition)

Each of these is a separate service with its own failure modes. The Escrow state machine is the coordination layer that ensures these three services remain consistent with each other across the lifecycle of an exchange. The 13 states are not about the escrow's internal logic -- they are about maintaining transactional consistency across three independent services in a distributed system.
