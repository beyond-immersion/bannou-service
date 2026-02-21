# Why Don't Characters Belong to Player Accounts?

> **Short Answer**: Because characters are world citizens, not player possessions. The player's guardian spirit possesses and influences characters, but characters exist independently in the world with their own lives, relationships, and agency. Tying characters to accounts would break the living world, the content flywheel, and the guardian spirit model.

---

## The Traditional Model and Why It Doesn't Work Here

In virtually every MMO and RPG backend, the data model looks like this:

```
Account (1) ──owns──▶ (many) Characters
```

The account is the root. Characters are children of the account. If the account is deleted, the characters go with it. If the account is offline, the characters don't exist in any meaningful sense. Characters are save slots with names.

This works fine for games where characters are avatars -- visual representations of the player with no independent existence. World of Warcraft's characters don't do anything when you log off. They stand in the inn, frozen, until you return.

Arcadia is not that kind of game.

---

## Characters Are World Citizens

In Arcadia, a character is not the player's avatar. It is an autonomous entity in the world that the player's guardian spirit can possess and influence. The distinction matters:

- **Characters have their own NPC brain running at all times.** When the player is offline, the character continues living -- going to work, socializing, eating, sleeping, pursuing personal goals. The Actor service (L2) executes their behavior regardless of whether a player is attached.

- **Characters have opinions about being possessed.** If the player pushes a character to act against their personality (a pacifist forced to kill, a coward forced to charge), the character resists. The dual-agency system means the character is a co-pilot, not a puppet.

- **Characters age, marry, have children, and die.** A character's lifespan is finite. When they die, their compressed archive becomes generative input for the content flywheel. New characters in the household may be the previous character's children, inheriting traits through the genetic system.

- **Characters have relationships with other characters.** A character's friendships, rivalries, marriages, and grudges exist in the world graph (the Relationship service at L2). These relationships persist regardless of which accounts are online.

If characters belonged to accounts, all of this would be architecturally awkward or impossible.

---

## What Happens If You Tie Characters to Accounts

Consider the consequences:

**The character table needs a foreign key to the account table.** This means the Character service (L2 Game Foundation) depends on the Account service (L1 App Foundation). That dependency direction is actually allowed by the hierarchy, but the coupling creates problems:

- **Account deletion cascades to characters.** But those characters have relationships, quest progress, encounter history, and economic activity in the world. Deleting them rips holes in the simulation.

- **Offline means non-existent.** If characters are "owned" by accounts, the natural assumption is that they only matter when the account is active. This makes it conceptually confusing to run their NPC brain while the player is offline.

- **Multi-character households become account-level state.** The guardian spirit model requires managing a household of characters across generations. If characters are account children, the household is just "all characters on this account" -- which loses the concept of inheritance, family trees, and generational transfer.

- **Character-to-character data flows through accounts.** If Character A (Account X) marries Character B (Account Y), the marriage is a relationship between accounts, not between world entities. Every query about character relationships must join through the account table. This is both architecturally wrong and semantically wrong.

---

## How Characters Actually Work

Characters in Bannou are independent world assets scoped to realms:

```
Realm (1) ──contains──▶ (many) Characters
Character (1) ──is species──▶ Species
Character (1) ──has──▶ (many) Relationships
Character (1) ──optionally possessed by──▶ Guardian Spirit (Account)
```

The Character service stores characters with realm-based partitioning. A character knows what realm it lives in and what species it is. It does not know or care which account, if any, currently possesses it.

The connection between a player and a character is managed through the guardian spirit model -- which is a Seed (L2). The Seed service tracks the growth and bonding of the guardian spirit to its household of characters. This is a relationship, not an ownership chain.

When an account is deleted, the characters remain in the world. They simply lose their guardian spirit and continue as full NPCs. This is both lore-correct (the divine shard departs) and architecturally clean (no cascade deletion through the world graph).

---

## The Content Flywheel Depends on This

The content flywheel only works because character archives are world data, not account data:

1. Character lives in the world, accumulating history, relationships, encounters, and personality evolution.
2. Character dies. The Resource service (L1) compresses the character and all its dependent data (history, personality, encounters) into a MySQL-backed archive.
3. The Storyline service (L4) uses these compressed archives as generative input -- creating quests, ghost encounters, NPC memories, and legacy mechanics from real play data.
4. New characters (potentially in different accounts' households) experience content generated from the archived character.

If characters belonged to accounts, step 2 would require account-level permissions to archive, step 3 would require cross-account data access, and step 4 would raise privacy concerns about one account's data generating content for another. By making characters world citizens, the entire pipeline operates on world data with no account-level concerns.

---

## The Actual Relationship Between Accounts and the Game World

The path from account to game world is:

```
Account (L1) ──subscribes to──▶ Game Service (L2)
    via Subscription (L2)

Account (L1) ──has guardian spirit──▶ Seed (L2)
    the guardian spirit bonds to characters

Character (L2) ──exists in──▶ Realm (L2)
    independent world entity
```

The Subscription service tracks which accounts have access to which games. The Seed service manages the guardian spirit's growth and household bonds. The Character service manages characters as world entities. These are three separate concerns, cleanly separated, with no foreign key chain from Account to Character.

This separation is why the Character service can do realm-based partitioning for scalable queries, why characters can be compressed and archived independently, and why the NPC brain can run on any character regardless of account attachment. The architecture reflects the game design: characters are citizens, not property.
