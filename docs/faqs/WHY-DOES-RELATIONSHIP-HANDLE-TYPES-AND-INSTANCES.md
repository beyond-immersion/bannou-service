# Why Does the Relationship Service Handle Both Types and Instances?

> **Short Answer**: Because relationship types and relationship instances are two sides of the same domain, and separating them created more problems than it solved. Relationship was originally two services (Relationship and Relationship-Type) that were consolidated because the type taxonomy is meaningless without instances to classify, and instances are meaningless without the type taxonomy to give them structure. Splitting them forced every consumer to import two clients for what is conceptually one operation.

---

## What the Relationship Service Does

The Relationship service (L2 Game Foundation) manages two tightly coupled concepts:

**Relationship Types** (the taxonomy):
- Hierarchical type definitions: "Bond" -> "Family Bond" -> "Parent-Child" -> "Mother-Daughter".
- Type deprecation with merge (when "Acquaintance" is deprecated, existing instances merge into "Associate").
- Bulk seeding from configuration (the game's relationship taxonomy is defined upfront and loaded on startup).

**Relationship Instances** (the connections):
- Entity-to-entity bonds: Character A is "mother" of Character B, Character C is "rival" of Character D.
- Bidirectional uniqueness enforcement: only one relationship of a given type between any two entities.
- Polymorphic entity types: relationships can connect characters, NPCs, guilds, locations, realms -- any entity type.
- Soft-deletion with recreate capability: a friendship can be broken and later restored.

---

## Why They Were Originally Separate

The initial design followed the principle of single responsibility. Relationship types seemed like a distinct concern from relationship instances:

- **Relationship-Type** would be a reference data service -- managing the taxonomy of possible relationship kinds.
- **Relationship** would be a transactional service -- managing the actual connections between entities.

This is the same pattern as Item (templates vs. instances) or Currency (definitions vs. wallets). Template/definition services manage "what can exist." Instance services manage "what does exist."

---

## Why the Split Failed

In practice, the separation created friction without benefit:

### Every Consumer Needed Both Clients

No consumer of relationships ever wanted just types or just instances. When the Character service retrieves a character's family tree, it needs:
1. The relationship instances (who is this character related to?).
2. The relationship types (what kind of relationship is it? is it a family bond? a professional bond?).

With two services, Character needs `IRelationshipClient` AND `IRelationshipTypeClient`. Every query involves two service calls: "get instances for this character" then "get types for those instances" (or a join that requires coordinating between two services).

This is different from Item and Inventory, where consumers genuinely use one without the other. Escrow uses Item (locking instances) without Inventory. Loot tables reference Item templates without touching Inventory containers. The Item/Inventory split has real consumers on each side. The Relationship/Relationship-Type split did not.

### The Type Taxonomy Is Shallow and Stable

Item templates are a deep, game-specific concept. Thousands of item templates with complex properties (stats, effects, rarity, quantity models) that game designers create and modify throughout the game's lifetime.

Relationship types are a shallow, structural concept. A few dozen types in a fixed taxonomy (family bonds, professional bonds, social bonds, political bonds) that are defined during world setup and rarely change afterward. The taxonomy is more like an enum with hierarchy than a rich data model.

A separate service for managing a shallow, stable taxonomy is overhead without payoff. The type data is small enough to cache in memory. The type API has only a handful of endpoints. It does not warrant its own schema, its own generated client, its own state store, and its own plugin lifecycle.

### Consolidation Simplified the Codebase

When the two services were merged:
- One schema instead of two.
- One generated client instead of two.
- One state store that can efficiently join instances with their types.
- One plugin with one lifecycle.
- Consumers import one client and make one call to get fully typed relationship data.

The merge eliminated cross-service coordination for what was always a single-domain query.

---

## When to Split and When to Consolidate

The Relationship consolidation is not a contradiction of the Item/Inventory split. It is an application of the same principle: split when the concerns have genuinely different consumers and different scaling characteristics; consolidate when they are always consumed together and one is too thin to justify independence.

| Criterion | Item / Inventory | Relationship / Type |
|-----------|-----------------|-------------------|
| Independent consumers? | Yes (Escrow uses Item alone) | No (every consumer needs both) |
| Different scaling? | Yes (item queries vs. movement operations) | No (types are cached, negligible load) |
| Different mutation patterns? | Yes (item stats vs. container placement) | No (types rarely change after seeding) |
| Complexity of each half? | Both substantial (16 endpoints each) | Type half is trivial (~5 endpoints) |
| Verdict | **Keep separate** | **Consolidate** |

---

## The Consumers

The Relationship service is used by multiple services across layers:

- **Character (L2)**: Family tree retrieval. When you query a character, you can get enriched data including their parents, children, siblings, and spouses. Character calls Relationship to get this data.
- **Storyline (L4)**: Narrative generation. Storyline uses relationship data to understand the social graph when generating narrative arcs from compressed archives. A story about a betrayal needs to know who was allied with whom.
- **Actor (L2)**: NPC behavior. An NPC's relationships affect their behavior -- they are more helpful to friends, more hostile to rivals, and protective of family members. Relationship data feeds into the actor's decision-making through the Variable Provider Factory pattern.

All of these consumers need both the instance data (who is connected to whom) and the type data (what kind of connection is it). The consolidated service serves them all through a single client with a unified query model.

---

## Bidirectional Uniqueness and Why It Matters

One design aspect that specifically benefits from consolidation: bidirectional uniqueness enforcement. The Relationship service guarantees that only one relationship of a given type can exist between any two entities, regardless of direction. If A is "friend" of B, then B is also "friend" of A, and attempting to create a second "friend" relationship in either direction is rejected.

This enforcement requires knowing the type taxonomy (to determine which types are bidirectional vs. unidirectional) and the instance data (to check for existing relationships) in a single atomic operation. With split services, this would require a distributed transaction or a coordination protocol between two services. With a consolidated service, it is a single state store query with a single atomic write.

The consolidation is not a compromise. It is the correct modeling of a domain where taxonomy and instances are inseparable.
