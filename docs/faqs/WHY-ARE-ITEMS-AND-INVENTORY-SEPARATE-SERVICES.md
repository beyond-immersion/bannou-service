# Why Are Items and Inventory Separate Services?

> **Short Answer**: Because "what a thing is" and "where a thing is" are fundamentally different concerns with different consumers, different scaling characteristics, and different mutation patterns. Item manages definitions and instances. Inventory manages containers and placement. Merging them creates a service that does two unrelated jobs and prevents higher-layer services from reusing the primitives independently.

---

## The Two Concerns

**Item** answers: What exists in the game world?
- Item templates define properties: code, game scope, quantity model, stats, effects, rarity.
- Item instances are individual occurrences: this specific sword with these specific stats, this stack of 47 arrows, this unique artifact with custom enchantments.
- Item supports multiple quantity models: discrete stacks (47 arrows), continuous weights (2.3 kg of iron ore), unique items (one-of-a-kind artifacts).

**Inventory** answers: Where are things placed?
- Containers hold items: a character's backpack, a shop's display shelf, a treasure chest, a guild vault.
- Containers have constraint models: slot-only, weight-only, grid-based, volumetric, unlimited.
- Inventory manages movement: transferring items between containers, stacking, splitting, and placement validation.

These are orthogonal axes. An item instance exists whether or not it is in a container (it might be on the ground, in escrow, or in transit). A container exists whether or not it contains items (an empty backpack is still a backpack).

---

## Why Not Merge Them?

The intuitive argument for merging is: "Items are always in inventories, and inventories always contain items. They're one system." But this is empirically false in Arcadia.

### Items Exist Outside Inventories

- **Escrow (L4)** locks items during multi-party exchanges. The item exists, but it is not "in" an inventory in the normal sense -- it is held by the escrow system until the transaction completes or is refunded.
- **Quest rewards** may reference item templates that will be instantiated on quest completion. The template exists in the Item service; no inventory is involved until the reward is granted.
- **License (L4)** treats license nodes as item instances on a grid board. The "container" is a license board, not an inventory container. License uses Item for the nodes and Inventory for the board containers, but the semantics are completely different from a character's backpack.
- **Collection (L2)** uses the same pattern: entry templates are item definitions, collection instances are inventory containers, and granting an entry creates an item in that container. The container here is a bestiary or music gallery, not a bag.
- **Loot tables** reference item templates. The loot system needs to know what items exist and their probabilities without caring about any specific container.

If Item and Inventory were one service, all of these consumers would need to import the combined service even though they only use half of it. Escrow does not care about container constraint models. Loot tables do not care about grid placement.

### Different Mutation Patterns

Item mutations are about the item itself: changing durability, modifying stats, adjusting quantity. These are fine-grained updates to individual instances.

Inventory mutations are about placement: moving an item from container A to container B, splitting a stack, merging stacks, validating that the destination container can accept the item. These are transactional operations that may involve multiple containers.

A merged service would handle both "reduce this sword's durability by 5" and "move 20 arrows from the quiver to the shop display while validating weight limits." These are different operations with different concurrency concerns, different validation rules, and different event consumers.

---

## The Reuse Argument

The strongest argument for separation is that both services are reused by L4 services in ways the original designers could not have predicted:

| L4 Service | Uses Item For | Uses Inventory For |
|------------|--------------|-------------------|
| **Escrow** | Locking/unlocking item instances | Not used (escrow has its own holding model) |
| **License** | License nodes as item instances | License boards as containers |
| **Collection** | Entry templates as item definitions | Collection instances as containers |
| **Save-Load** | May snapshot item state | May snapshot container state |

This is the "Unix philosophy" applied to game services: Item does one thing (manages what things are), Inventory does one thing (manages where things are), and higher-layer services compose them.

---

## The Schema-First Angle

From a schema-first perspective, merging would mean one API schema covering both item template CRUD, item instance CRUD, container CRUD, and movement operations. That schema would have 32 endpoints (16 from each). More importantly, it would generate a single client (`IItemInventoryClient`) that every consumer must import in full -- even if they only need item templates.

Separate schemas mean separate clients. The Escrow service imports `IItemClient` for locking instances. License imports both `IItemClient` and `IInventoryClient` because it uses both. A hypothetical crafting service might only import `IItemClient` to check material templates.

---

## How They Interact

Inventory delegates to Item for all item-level operations. When you move an item between containers, Inventory validates the container constraints (does the destination have room? does it accept this category?) and then calls Item to update the item's location. This is a clean orchestration boundary:

- Inventory says: "Can this container accept this item? Yes. Execute the move."
- Item says: "This item instance now has a new location reference."

The services are designed as complementary halves -- Inventory is explicitly described as "the placement layer that orchestrates lib-item." They are separate not because they are unrelated, but because separation makes both more useful.
