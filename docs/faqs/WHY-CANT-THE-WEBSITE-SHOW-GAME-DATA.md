# Why Can't the Website Show Character Profiles or Game Data?

> **Short Answer**: Because L3 services cannot depend on L2 services. The website is an App Feature. Characters, realms, and subscriptions are Game Foundation. The hierarchy forbids the dependency, and the hierarchy is right.

---

## The Intuition

Every game with a website shows character profiles. You log in, you see your characters, your guild, your achievements, maybe an armory page. World of Warcraft has had this since 2007. It feels like table stakes.

So when you look at Bannou's Website service (L3 AppFeatures) and see that it intentionally does NOT access character data, subscription state, realm information, or any other L2 Game Foundation data, the natural reaction is: "This seems like an oversight. Or maybe excessive architectural purity at the expense of obvious functionality."

It is neither.

---

## Why the Hierarchy Forbids It

The service hierarchy has a specific rule for Layer 3:

```
L3 App Features: May depend on L0, L1, L3*
                 May NOT depend on L2, L4, L5
```

Website is L3. Character, Realm, Subscription, Species -- these are all L2. The dependency is structurally forbidden.

This is not an accident. L3 exists as a separate branch from L2 specifically to maintain a clean distinction between **application-level concerns** (things useful for any cloud service) and **game-level concerns** (things specific to running a game). The hierarchy diagram makes this clear:

```
        L4: Game Features
       /
      L2: Game Foundation    L3: App Features
       \                    /
        L1: App Foundation
             |
        L0: Infrastructure
```

L3 and L2 are sibling branches, not a parent-child relationship. L3 services are useful for ANY Bannou deployment -- even one that has nothing to do with games. Asset storage, deployment orchestration, documentation, and a public website are operational services that make sense for any real-time cloud platform. The moment Website starts importing `ICharacterClient`, it becomes a game service, and it should be reclassified to L4.

---

## What Happens If You Break This Rule

Suppose you let Website depend on Character (L2). Now consider the deployment modes:

```bash
# Non-game cloud service deployment
BANNOU_ENABLE_APP_FOUNDATION=true   # L1
BANNOU_ENABLE_APP_FEATURES=true     # L3
# No game services -- useful for any real-time cloud service
```

This deployment mode is supposed to give you: auth, accounts, permissions, WebSocket gateway, asset storage, orchestration, documentation, and a website. No game concepts. Useful for building a real-time collaboration tool, a chat platform, or a voice communication service.

But if Website depends on Character, this deployment crashes. The DI container cannot resolve `ICharacterClient` because Character (L2) is not enabled. Your non-game website is broken because it imported a game service.

The options are:
1. **Make it a soft dependency** (runtime `GetService<T>()` with null check) -- but then half the website's pages show "Character data unavailable" in a deployment that never had characters. The UI is littered with conditional logic for a feature that does not exist in this deployment mode.
2. **Require L2 for Website** -- but then you cannot deploy a non-game Bannou instance with a website, which is absurd.
3. **Keep the hierarchy** -- Website shows what it can show (news, account profiles, downloads, contact forms, CMS pages) and game-specific portals are a separate concern.

Option 3 is the only one that preserves the deployment flexibility that makes Bannou a platform rather than a single game's backend.

---

## So How Do You Show Character Profiles on the Web?

The answer is: a game-specific portal service at L4.

An L4 Game Features service can depend on L2 Game Foundation services freely. A hypothetical `lib-game-portal` at L4 could import `ICharacterClient`, `IRealmClient`, `ISubscriptionClient`, `IAchievementClient`, and render character armory pages, realm status dashboards, leaderboard views, and guild profiles to its heart's content.

This service would:
- Live at L4 (Game Features), where game dependencies are expected
- Only be enabled in game deployments (where L2 is guaranteed to be running)
- Not exist in non-game deployments (where it would be meaningless anyway)
- Not contaminate the Website service with game-specific logic

The Website service handles what every deployment needs: news, company info, downloads, account profiles, contact forms. The game portal handles what game deployments need: character profiles, realm status, leaderboards. Clean separation, clean deployment modes, clean dependency graph.

---

## The Deeper Principle

The hierarchy is not about preventing useful features. It is about preventing **domain leakage** -- the slow, insidious process by which application-level services accumulate game-specific dependencies until they cannot function without the full game stack.

Every game project starts with "let's just add character profiles to the website." Then it becomes "let's show guild rankings." Then "let's display realm status." Then "let's show the economy dashboard." Eventually, the website imports half the game services, crashes without them, and cannot be deployed independently.

The hierarchy catches this at the first step. Not because character profiles on a website are bad, but because they belong in a service that is explicitly scoped to game deployments. The Website service's job is to be the public face of the platform. The game portal's job is to be the public face of the game. These are different jobs with different deployment requirements and different dependency graphs.

Keeping them separate is not excessive purity. It is what makes Bannou deployable as both a game backend and a general-purpose real-time platform from the same codebase.
