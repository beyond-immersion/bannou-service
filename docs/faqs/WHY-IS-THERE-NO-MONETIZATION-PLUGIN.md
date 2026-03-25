# Why Is There No Monetization / Premium / MTX Plugin?

> **Last Updated**: 2026-03-25
> **Related Plugins**: Subscription (L2), Status (L4), Collection (L2), Inventory (L2), Relationship (L2), Currency (L2), Item (L2), Permission (L1)
> **Short Answer**: Because monetization is not a domain — it is a business-specific composition of existing primitives. Every studio's monetization model is different, and building explicit MTX infrastructure into Bannou would be simultaneously too restrictive (imposing a specific model) and too complex (spreading billing concerns throughout the service layer). The composable APIs already provide all building blocks with zero blockers.

---

## The Composable Building Blocks Already Exist

Every monetization pattern maps cleanly to existing Bannou primitives:

| Monetization Pattern | Bannou Composition |
|---|---|
| Premium subscription (monthly VIP) | **Subscription** (access control + expiration worker) + **Status** (benefit effects via `sourceId` cascade) |
| Premium currency (gems, crystals) | **Currency** (wallets with transfer/exchange) — just another currency definition |
| Premium inventory slots | **Inventory** (containers with constraint models) — just another container |
| Premium cosmetics | **Item** (templates + instances) + **Inventory** (cosmetic container) + **Collection** (unlock catalog) |
| Battle pass / seasonal track | **Seed** (progressive growth per season) + **License** (grid-based reward board) + **Collection** (content unlocks) |
| Premium status effects (XP boost) | **Status** (grant with TTL, `sourceId` links to subscription for cascade removal) |
| Premium UX modules | **Agency** (UX capability manifest per spirit) — premium modules are just capability grants |
| Account-to-premium-entity binding | **Relationship** (typed entity-to-entity association: `Account → Entity` with `relationshipTypeCode`) |
| Loot box / gacha | **Loot** (weighted drop tables with pity thresholds) + **Item** + **Inventory** |
| Premium permissions (early access) | **Permission** (state-based capability manifest) — premium is a permission state |

No single primitive "is" monetization. These are business-specific compositions — exactly the L5 Extension pattern Bannou is designed for.

---

## Why Not Build a Dedicated MTX Plugin Anyway?

### 1. Every Studio's Model Is Different

One studio sells monthly subscriptions with tiered benefits. Another sells cosmetic-only items with no gameplay advantage. A third has a battle pass with seasonal progression. A fourth uses premium currency with exchange rates. A fifth mixes all four.

A monetization plugin would either:
- **(a)** Try to generalize all models into one schema — resulting in a complex abstraction that fits none of them well
- **(b)** Pick one model (subscriptions, or MTX store, or battle pass) — leaving the others unaddressed

Both outcomes are worse than letting each studio compose their own model from primitives.

### 2. Billing Is External

Payment processing (Stripe, PayPal, Apple IAP, Google Play Billing, Steam Wallet, console store APIs) is inherently external to Bannou. These systems push purchase notifications via webhooks. The game server's billing integration handler calls Bannou's existing APIs:

```
Stripe Webhook → Game Server Billing Handler → Subscription.RenewSubscriptionAsync()
                                              → Status.GrantStatusAsync() (benefits)
                                              → Currency.CreditAsync() (premium currency)
                                              → Inventory operations (premium items)
```

A Bannou MTX plugin would sit between the billing handler and these API calls — adding a layer of indirection with zero added value. The billing handler already knows which APIs to call because it understands the studio's specific monetization model.

### 3. Spreading "Premium" Throughout the System Is an Anti-Pattern

If Bannou had explicit premium support, every service would need to know about it:
- Status would need `isPremium` fields on templates
- Inventory would need "premium container" types
- Collection would need "premium unlock" flags
- Currency would need "premium currency" designators
- Permission would need "premium role" concepts

This multiplies maintenance complexity by spreading a business concept throughout infrastructure services. Instead, premium is just a usage pattern: a subscription grants a status effect, which is just a status effect. A premium inventory is just an inventory. The services don't know or care whether something is "premium" — they manage entities.

### 4. Relationship Already Handles Account→Entity Bindings

The most tempting dedicated plugin would be "account → premium entity associations" (binding premium inventories, premium collections, etc. to an account). But **Relationship (L2)** already provides exactly this:

- `Account` ↔ `Entity` with typed `relationshipTypeCode` (e.g., `"premium_inventory"`, `"battle_pass"`, `"cosmetic_collection"`)
- Bidirectional queries ("what premium things does this account have?" / "which accounts own this premium thing?")
- Full lifecycle (create, update, delete) with event publishing

No new plugin needed for entity bindings.

---

## The L5 Extension Pattern

When a studio finds itself repeatedly composing the same premium primitives, they create an **L5 Extension** — a thin facade providing game-specific vocabulary:

```yaml
# Example: Studio's premium extension
/premium/grant-vip:
  # Internally: Subscription.Create + Status.Grant (XP boost) + Inventory.Create (premium container)
  
/premium/get-status:
  # Internally: Subscription.Get + Status.GetEffects (filter premium category)
  
/premium/purchase-currency:
  # Internally: validates billing receipt + Currency.Credit
```

This extension lives in the studio's codebase, follows the same schema-first development, and composes Bannou primitives into their specific monetization model. It's how studios make Bannou *theirs* — the Extension Pattern from BANNOU-DESIGN.md § Composition Orchestrator.

---

## The Litmus Test

Before proposing a monetization plugin, answer:

1. **Does any Bannou service need to know something is "premium"?** No — Status stores effects, Inventory stores items, Subscription stores access. None care about the commercial context.
2. **Is there new state that no existing store covers?** No — billing receipts belong in external systems; premium entity bindings belong in Relationship; premium access belongs in Subscription.
3. **Would the plugin just be a composition orchestrator?** Yes — and that's exactly what L5 Extensions are for. The orchestration is business-specific by definition.
4. **Can the game server already do this with existing APIs?** Yes — with zero blockers.

The monetization "plugin" is the studio's L5 Extension. Bannou provides the primitives.

---

*See also: [WHY-ARE-THERE-NO-SKILL-MAGIC-OR-COMBAT-PLUGINS.md](WHY-ARE-THERE-NO-SKILL-MAGIC-OR-COMBAT-PLUGINS.md) for the same composability argument applied to gameplay systems.*
