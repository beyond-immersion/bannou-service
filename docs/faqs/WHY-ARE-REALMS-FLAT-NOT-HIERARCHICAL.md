# Why Are Realms Flat and Not Hierarchical?

> **Short Answer**: Because realms are parallel worlds, not nested subdivisions. Arcadia and Fantasia are not regions of a larger world -- they are independent universes with different rules, different species, different cultures, and different histories. Hierarchy implies containment and shared context. Flatness reflects the actual relationship: peer worlds with no structural dependency on each other.

---

## What Realms Are

In Bannou, a realm is a top-level persistent world. Examples from the Arcadia vision:

- **Omega**: A cyberpunk meta-dashboard realm. The player's hub between game worlds.
- **Arcadia**: A western RPG/economic simulation. The primary game world.
- **Fantasia**: A primitive fantasy survival world. A different experience from Arcadia.

Each realm operates independently:
- Distinct species populations (Species is realm-scoped -- elves exist in Arcadia, maybe not in Fantasia).
- Distinct cultural contexts and history (Realm History is per-realm).
- Distinct economies (currencies can be realm-restricted).
- Distinct locations (the Location tree is rooted per-realm).

---

## The Temptation to Nest

The hierarchical instinct comes from real-world geography: continents contain countries, countries contain regions, regions contain cities. A world contains sub-worlds, right?

Some game architectures model this as:

```
Universe
  └── World (Arcadia)
        ├── Continent (Aethermoor)
        │     ├── Region (Windshire)
        │     │     └── City (Havenfall)
        │     └── Region (Irondeep)
        └── Continent (Sylvanmere)
```

This conflates two different concepts:
1. **Realms**: Independent worlds with separate rules and contexts.
2. **Locations**: Hierarchical places within a single world.

Bannou separates these cleanly.

---

## Realms vs. Locations

**Realm** (flat, peer-to-peer):
- Manages top-level worlds.
- Each realm is independent with no parent/child relationship.
- Realms do not share game state, species, or economic systems.
- The Realm service provides CRUD with deprecation lifecycle. Simple.

**Location** (hierarchical, tree-structured):
- Manages places WITHIN a single realm.
- Each location belongs to exactly one realm and optionally has a parent location.
- Locations form a tree: Region -> City -> District -> Building -> Room.
- The Location service handles depth tracking, circular reference prevention, cascading depth updates, and bulk seeding with two-pass parent resolution.

The hierarchy that people intuitively want is already there -- it is in the Location service, not the Realm service. Within Arcadia, Havenfall is inside Windshire is inside Aethermoor. That hierarchy is real and modeled. But Arcadia is not "inside" anything, and Fantasia is not a sibling of Arcadia in any structural sense.

---

## Why Flatness Matters

### Independent Scaling

Realms are the natural partition boundary for scaling. Every realm can be served by a different cluster of service instances. Character queries are partitioned by realm. Location trees are rooted per realm. Species are scoped to realms.

If realms were hierarchical, a "parent realm" would need to aggregate data from all child realms, creating a scaling bottleneck. With flat realms, each realm's data is self-contained and independently queryable.

### Independent Rules

Each realm can have fundamentally different game rules. Arcadia is a western RPG with deep economic simulation. Fantasia is a survival game. Omega is a meta-dashboard. These are not variations on a theme -- they are different games running on the same platform.

If realms were hierarchical, the parent realm would imply shared rules or shared context. But there is no meaningful "parent" for worlds that differ this fundamentally. What would the parent of Arcadia and Fantasia be? "All games"? That is not a realm -- that is the platform itself.

### Cross-Realm Transfer Rules

The Arcadia vision specifies that "knowledge transfers between realms, not resources." A character in Arcadia who learns blacksmithing contributes that knowledge to the guardian spirit, which can benefit a character in Fantasia. But the gold in the Arcadia character's wallet does not transfer.

This rule is simple to enforce with flat realms: currencies are realm-scoped, items are realm-scoped, but the guardian spirit (a Seed bonded to the account) is realm-agnostic. With hierarchical realms, the transfer rules would need to account for "same parent" vs. "different parent" relationships, adding complexity for no benefit.

### Deprecation, Not Deletion

Realms support a deprecation lifecycle. If Fantasia is retired, it is deprecated (no new characters, no new sessions) and eventually archived. Flat structure means deprecating one realm has zero impact on any other realm. With hierarchy, deprecating a parent realm would cascade to children, and deprecating a child might have implications for the parent's data.

---

## The Game Service Connection

Realms are closely tied to the Game Service (L2), which maintains a catalog of available games. The relationship is:

```
Game Service (registry of games)
    └── Arcadia (a game)
          └── Realm: Arcadia (the world of that game)
          └── Realm: Fantasia (another world of that game)
    └── SomeOtherGame
          └── Realm: World1
```

A game can have multiple realms (Arcadia the game has Arcadia the realm and Fantasia the realm). Subscriptions (L2) grant account access to a game service. The realms within that game service are then available to the subscribed account.

This relationship is flat at the realm level and hierarchical at the game-to-realm level -- which correctly models reality. Games contain realms. Realms do not contain other realms.

---

## The Multi-Realm Player Experience

The flatness of realms enables the multi-realm experience described in the vision:

1. Player logs in. Guardian spirit is in Omega (the hub realm).
2. Player selects Arcadia. Guardian spirit enters Arcadia, possesses a household character.
3. Player plays in Arcadia. Character accumulates experiences.
4. Player returns to Omega. Experiences feed into the guardian spirit's growth.
5. Player selects Fantasia. Guardian spirit enters Fantasia, possesses a different character.
6. Knowledge from Arcadia enriches the Fantasia experience via the guardian spirit's capabilities.

Each realm transition is a clean context switch. There is no shared state to synchronize, no parent realm to update, no hierarchy to traverse. The guardian spirit (a Seed at L2) is the only entity that crosses realm boundaries, and it does so through its own bonding model, not through realm relationships.

Flat realms make this possible. Hierarchical realms would imply shared context that does not exist and would complicate every realm transition with inheritance resolution.
