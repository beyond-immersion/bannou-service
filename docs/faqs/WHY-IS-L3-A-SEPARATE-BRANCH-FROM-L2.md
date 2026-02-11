# Why Is Layer 3 a Separate Branch From Layer 2 Instead of Stacked On Top?

> **Short Answer**: Because App Features and Game Foundation solve problems in different domains. Stacking them would force a false dependency: either operational tools would need game services to function, or game services would need operational tools. Neither makes sense.

---

## The Expected Hierarchy

Most people, when they first see the service hierarchy, expect a simple stack:

```
L4: Game Features
L3: App Features         <-- depends on L2
L2: Game Foundation      <-- depends on L1
L1: App Foundation
L0: Infrastructure
```

This is the intuitive model. Each layer builds on the one below it. L3 depends on L2 depends on L1 depends on L0. Simple, linear, easy to reason about.

Bannou does not use this model. Instead, L2 and L3 are **sibling branches**:

```
        L4: Game Features
       /
      L2: Game Foundation    L3: App Features
       \                    /
        L1: App Foundation
             |
        L0: Infrastructure
```

L3 cannot depend on L2. L2 cannot depend on L3. They are independent branches that both build on L1. L4 can depend on both.

This is unusual. Why?

---

## The Two Deployment Modes

The branching hierarchy enables two meaningful deployment modes that a linear stack cannot support:

### Non-Game Deployment (L0 + L1 + L3)

```bash
BANNOU_ENABLE_APP_FOUNDATION=true   # L1: account, auth, connect, permission, contract, resource
BANNOU_ENABLE_APP_FEATURES=true     # L3: asset, orchestrator, documentation, website
```

This gives you: authentication, accounts, permissions, WebSocket gateway, binding agreements, resource tracking, binary asset storage, deployment orchestration, a knowledge base API, and a public website.

No characters. No realms. No species. No currencies. No items. No inventories.

This is a complete, functional platform for building any real-time service. A voice communication platform. A document collaboration tool. A real-time data dashboard. None of these need game concepts, and none of these should crash because `ICharacterClient` cannot be resolved.

### Game Deployment Without App Features (L0 + L1 + L2)

```bash
BANNOU_ENABLE_APP_FOUNDATION=true   # L1
BANNOU_ENABLE_GAME_FOUNDATION=true  # L2: character, realm, species, location, currency, ...
```

This gives you: everything above, plus characters, realms, species, locations, currencies, items, inventories, quests, actors, game sessions.

No asset storage. No orchestrator. No documentation service. No website.

This is a minimal game backend. You are managing your own deployment, storing assets externally, and running documentation through some other system. The game services work without operational tooling because operational tooling is not a prerequisite for game logic.

### Full Deployment (L0 + L1 + L2 + L3 + L4)

Everything. This is the common case for production Arcadia deployments. But it is not the only case, and the hierarchy ensures the other cases remain viable.

---

## What Breaks If You Stack Them

### If L3 Depends on L2

Asset storage is L3. If it depends on L2, then storing a texture requires the Character service to be running. That is absurd -- binary asset storage has nothing to do with game characters. But once you allow L3 to depend on L2, every L3 service can import any L2 client, and the non-game deployment mode ceases to exist.

The Documentation service syncs markdown files from git repositories. If it depends on L2, then syncing documentation requires the Realm service to be running. The Orchestrator manages container deployments. If it depends on L2, deploying a new topology requires the Species service to be running.

None of these dependencies make sense. They would only exist because the linear hierarchy forced L3 to sit above L2, implying that L3 can use L2, and eventually some developer would find a reason to import an L2 client "just for this one feature."

### If L2 Depends on L3

The reverse is worse. If Game Foundation depends on App Features, then game services require operational tooling to function. The Character service cannot run without the Asset service. The Realm service cannot start without the Orchestrator.

Game logic should not depend on operational tools. You should be able to run a minimal game backend with just Redis, RabbitMQ, and the game services. Requiring asset storage, deployment orchestration, and a documentation API just to create a character is coupling that serves no architectural purpose.

---

## How L4 Resolves the Split

Layer 4 (Game Features) is where the branches reunite. L4 services can depend on both L2 and L3:

```
L4 depends on: L0, L1, L2 (required), L3* (optional), L4* (optional)
```

This is where game-specific services that need operational tooling live:

- **Behavior** (L4) stores compiled ABML bytecode in the Asset service (L3) and loads behavior documents from it.
- **Save-Load** (L4) uses the Asset service (L3) for durable save data storage in MinIO.
- **Puppetmaster** (L4) bridges Actor (L2) and Asset (L3) for dynamic behavior loading, specifically because Actor at L2 cannot depend on Asset at L3.
- **Mapping** (L4) stores spatial data assets through the Asset service (L3).

These L4 services legitimately need both game data (L2) and operational tooling (L3). That is exactly what L4 is for. The L3 dependencies are optional -- if the Asset service is unavailable, L4 services degrade gracefully (they cannot load dynamic behaviors or store save data in MinIO, but they do not crash).

---

## The Puppetmaster Example

The most instructive example of why the branch matters is the Puppetmaster service.

The Actor service (L2) executes NPC behavior loops. Some behaviors are compiled from ABML source and stored as assets in MinIO via the Asset service (L3). Actor needs to load these behaviors at runtime.

But Actor is L2 and Asset is L3. The hierarchy forbids this dependency. In a linear stack where L3 sits above L2, this would be allowed. In the branching hierarchy, it is not.

The solution: Puppetmaster (L4) implements `IBehaviorDocumentProvider`, an interface defined in shared code. Actor discovers providers via DI collection injection. Puppetmaster loads behaviors from Asset and provides them to Actor. Actor depends on the interface (shared code), not on Asset (L3) or Puppetmaster (L4).

This is the Variable Provider Factory pattern applied to the L2/L3 boundary. It exists specifically because the branches are separate. And it is architecturally better than a direct dependency: Actor can run without Puppetmaster (it just has fewer behavior documents available), and the dependency is explicit and discoverable rather than hidden in a constructor.

---

## The Design Principle

The branching hierarchy encodes a real domain distinction: **game logic** and **operational tooling** are independent concerns that should not force dependencies on each other. Games should work without ops tools. Ops tools should work without games.

This is not theoretical. Bannou's thesis is that it is a **platform**, not just Arcadia's backend. The same codebase powers game deployments and non-game deployments. The service hierarchy is what makes this possible. If L3 and L2 were stacked instead of branched, every deployment would need the full game stack, and Bannou would be "Arcadia's backend" rather than "a platform that Arcadia happens to use."

The branch is the architectural expression of the platform ambition.
