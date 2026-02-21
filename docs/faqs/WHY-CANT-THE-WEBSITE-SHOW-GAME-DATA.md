# Why Can't the Website Show Character Profiles or Game Data?

> **Short Answer**: Because Bannou is a platform, not just a game backend. The same codebase deploys as a real-time cloud service with zero game concepts. If the website imports character data, it can't be deployed without the game stack -- and that kills the platform.

---

## The Intuition

Every game with a website shows character profiles. You log in, you see your characters, your guild, your achievements, maybe an armory page. World of Warcraft has had this since 2007. It feels like table stakes.

So when you look at Bannou's Website service and see that it intentionally does NOT access character data, subscription state, realm information, or any other game data, the natural reaction is: "This seems like an oversight. Or maybe excessive architectural purity at the expense of obvious functionality."

It is neither. It is the direct consequence of Bannou being a platform that ships more than games.

---

## Bannou Without Games

Bannou's deployment modes include configurations with no game services at all:

```bash
# Non-game cloud service deployment
BANNOU_ENABLE_APP_FOUNDATION=true   # L1: auth, accounts, permissions, WebSocket, contracts
BANNOU_ENABLE_APP_FEATURES=true     # L3: asset storage, orchestration, documentation, website
# No L2, no L4 -- no game concepts whatsoever
```

This deployment gives you: authentication, account management, RBAC permissions, a WebSocket gateway with binary routing, asset storage with pre-signed URLs, deployment orchestration, a documentation service, and a public website. No characters. No realms. No species. No inventories.

This is a real deployment mode for real use cases. A real-time collaboration tool. A voice communication platform. An IoT device management dashboard. Any application that needs persistent authenticated connections with server-push, asset storage, and role-based access control -- but has nothing to do with games.

The Website service is one of these non-game services. It serves news, company information, download links, account profiles, contact forms, CMS pages. Every one of those makes sense whether you're running a game or not.

---

## What Happens When the Website Imports Game Data

Now imagine the Website service imports `ICharacterClient` to show character profiles. The non-game deployment immediately breaks. The DI container cannot resolve `ICharacterClient` because the Character service isn't loaded -- it's a game service, and this is a non-game deployment.

You have three options, and all of them are bad:

**Make it a soft dependency** (check for null at runtime). Now the website's character profile page shows "Character data unavailable" in a deployment that never had characters. Every page that touches game data needs conditional logic. The UI is a patchwork of "this feature exists if you also run the game stack." The website doesn't know what it is anymore -- is it a game portal or a platform homepage? It tries to be both and does neither well.

**Require the game stack for Website**. Now you cannot deploy a Bannou-powered cloud service with a website unless you also spin up the entire game foundation -- character, realm, species, location, currency, item, inventory, game-session, subscription, relationship, actor, quest, seed. Fourteen services that have no purpose in your collaboration tool, consuming resources and creating deployment complexity, because the website wanted to show character profiles.

**Keep the separation**. The Website shows what every deployment needs. Game-specific portals live elsewhere.

---

## The Domain Leakage Problem

The character profile request is never the last request. The history of every game project's website follows a predictable arc:

1. "Let's just add character profiles to the website."
2. "While we're at it, let's show guild rankings."
3. "We should display realm status -- players want to check before logging in."
4. "The economy dashboard would be great on the web."
5. "Let's add achievement tracking."
6. "Can we show the leaderboards?"

Each step is individually reasonable. Each step adds one more game dependency. By step 6, the website imports half the game services, crashes without them, and is functionally an L4 game feature wearing an L3 label. The non-game deployment mode is dead. Nobody noticed because nobody tested it after step 2.

This is domain leakage -- the slow, insidious process by which application-level services accumulate game-specific dependencies until they cannot function without the full game stack. The service hierarchy catches it at step 1, not because character profiles are bad, but because the dependency direction reveals that this feature belongs somewhere else.

---

## The Right Way: A Game Portal at L4

An L4 Game Features service can depend on L2 Game Foundation services freely -- that's what L4 is for. A hypothetical `lib-game-portal` at L4 could import `ICharacterClient`, `IRealmClient`, `ISubscriptionClient`, `IAchievementClient`, and render character armory pages, realm status dashboards, leaderboard views, and guild profiles.

This service would:

- Live at L4, where game dependencies are expected and guaranteed to be available
- Only be enabled in game deployments (where L2 is running)
- Not exist in non-game deployments (where it would be meaningless)
- Not contaminate the Website service with game-specific logic

The Website handles what every deployment needs: platform identity, news, downloads, account profiles, contact forms. The game portal handles what game deployments need: character profiles, realm status, leaderboards. Different services, different layers, different deployment requirements, same codebase.

---

## The Hierarchy as Mechanism

The service hierarchy's rule -- L3 cannot depend on L2 -- is the mechanism that enforces this separation. But the rule is not the reason. The reason is:

**Bannou is a platform that deploys as both a game backend and a general-purpose real-time service from the same binary.** Every service that crosses the game/non-game boundary weakens that flexibility. The hierarchy exists to keep the boundary sharp, not because sharp boundaries are aesthetically pleasing, but because deployment flexibility is a load-bearing architectural requirement.

The Website can't show game data because the Website isn't a game service. If you need a game service that renders web pages, build one. That's what L4 is for.
