# Why Is Game Session at Layer 2 Instead of Layer 4?

> **Short Answer**: Because "characters are in a game session" is a foundational fact about the game world, not an optional feature. Game Session tracks which characters are actively playing, manages lobby entry points for subscribed accounts, and coordinates permission state transitions. If Game Session were L4, the foundational services (Character, Actor, Quest) could not assume characters have active sessions, and the Matchmaking service (L4) would have no guaranteed session infrastructure to create matches into.

---

## What Game Session Does

The Game Session service manages two types of sessions:

**Lobby sessions**: Persistent, per-game-service entry points that are auto-created when an account subscribes to a game. When you subscribe to Arcadia, a lobby session is created for you. This is your "entry point" into the game -- the persistent context through which you access the world.

**Matchmade sessions**: Temporary sessions pre-created by the Matchmaking service (L4) with reservation tokens and TTL-based expiry. When matchmaking finds a match, it creates a matchmade session that players join using reservation tokens.

Game Session also integrates with:
- **Permission (L1)** for `in_game` state tracking -- when a character joins a session, their permission state changes, which updates their capability manifest.
- **Subscription (L2)** for account eligibility -- only subscribed accounts can have sessions.
- **Connect (L1)** for WebSocket shortcut publishing -- connected clients receive one-click join shortcuts for their available sessions.

---

## The Foundational Argument

### Permission State Depends on It

The Permission service (L1) manages per-session capability manifests compiled from a multi-dimensional matrix: service x state x role -> allowed endpoints. One of those state dimensions is `in_game` -- whether the player is currently in an active game session.

When a character joins a game session, Game Session publishes an event. Permission consumes this event and recompiles the session's capabilities. Endpoints that are only available while in-game (combat actions, trade with nearby NPCs, local chat) become accessible.

If Game Session were L4, this integration would still work (L1 can consume events from L4 without depending on L4). But the semantic problem is deeper: `in_game` is a foundational permission state, and the service that manages it should be at the same layer as other foundational game concepts.

### Matchmaking Needs a Guaranteed Target

The Matchmaking service (L4) creates matchmade sessions when it finds a match. Its workflow is:

1. Match found among queued tickets.
2. Create a matchmade game session via `IGameSessionClient`.
3. Issue reservation tokens to matched players.
4. Players join the session using their tokens.
5. On full acceptance, publish join shortcuts via Connect.

Matchmaking depends on Game Session as a hard dependency -- it calls the Game Session API to create sessions. If Game Session were L4, this would be an L4-to-L4 dependency, which is allowed but must degrade gracefully. Graceful degradation for "the service I create matches into" is not meaningful -- if Game Session is down, matchmaking cannot function at all.

With Game Session at L2, Matchmaking has a guaranteed-available target. When L4 services start, all L2 services are already running. There is no need for null checks or degraded behavior.

### Subscription Integration

Game Session reacts to subscription events from the Subscription service (L2). When an account subscribes to a game, Game Session auto-creates a lobby session. When a subscription expires, Game Session can clean up the associated sessions.

This is an L2-to-L2 integration -- both services are at the same layer, can depend on each other, and are guaranteed to be running together. If Game Session were L4, it would be consuming L2 events (allowed) but would need to handle the case where it starts before subscriptions are fully loaded (an L4 service might start before all its event backlog is processed).

---

## What Game Session Is NOT

Game Session is deliberately minimal. It manages session existence, session membership, and session lifecycle. It does not:

- **Run game logic.** The actual game simulation is a client-side and game-server concern. Game Session tracks "who is in this session," not "what is happening in this session."
- **Handle matchmaking logic.** Queue management, skill windows, party grouping, and match algorithms are in Matchmaking (L4). Game Session just provides the sessions that matchmaking creates.
- **Manage voice channels.** Voice room lifecycle is handled by the Voice service (L4). Game Session integrates with Voice (when a session starts, a voice room may be created) but does not manage the voice infrastructure.
- **Store game state.** Game state persistence is handled by Save-Load (L4). Game Session knows the session exists; Save-Load knows what happened in it.

This minimalism is why it belongs at L2. It provides the foundational fact ("these characters are in a session together") without any optional feature logic.

---

## The Shortcut Publishing Pattern

One of Game Session's most visible features is WebSocket shortcut publishing. When a lobby session is available or a matchmade session is ready, Game Session publishes a "shortcut" to the connected client via Connect's per-session RabbitMQ queue. The client receives a push notification like "Join Arcadia" or "Your match is ready" with a single-click action.

This requires integration with:
- **Subscription (L2)**: to know which accounts have active subscriptions (and thus should receive shortcuts).
- **Connect (L1)**: to push the shortcut to the correct WebSocket session.
- **Permission (L1)**: to ensure the client's capability manifest includes the shortcut.

All of these are L1 or L2 services. The shortcut publishing pattern is a foundational game entry flow, not an optional feature. With Game Session at L2, all of its dependencies are at the same layer or below -- clean, predictable, and guaranteed available.

---

## The Horizontal Scaling Design

Game Session supports per-game horizontal scaling via a `SupportedGameServices` configuration. A Game Session instance can be configured to only manage sessions for specific games:

```bash
# Instance A handles Arcadia sessions
GAME_SESSION_SUPPORTED_GAME_SERVICES=arcadia

# Instance B handles Fantasia sessions
GAME_SESSION_SUPPORTED_GAME_SERVICES=fantasia
```

This partitioning is foundational infrastructure -- it determines how session load is distributed across the cluster. L2 placement ensures this scaling mechanism is available regardless of which L4 features are enabled. You can scale session management for a game that uses matchmaking, a game that only uses lobbies, or a game that uses neither.

---

## Contrast With Matchmaking (L4)

The distinction between Game Session (L2) and Matchmaking (L4) illustrates the layer classification clearly:

| Concern | Game Session (L2) | Matchmaking (L4) |
|---------|-------------------|-------------------|
| What it does | Manages session existence and membership | Finds matches between players |
| Always needed? | Yes -- every game has sessions | No -- not every game has competitive matching |
| Dependencies | L1 (Connect, Permission), L2 (Subscription) | L2 (Game Session, Character), L4 (Analytics for skill ratings) |
| If disabled | Game cannot start -- no sessions to join | Game works -- players join lobbies directly |
| Scaling model | Per-game partitioning | Queue processing intervals |

Matchmaking is optional. A cooperative game does not need it. A single-player game does not need it. A sandbox game where players join worlds directly does not need it. But every game needs sessions -- the concept of "these players are currently playing together" is foundational.

Game Session provides the container. Matchmaking fills it with algorithmically-selected participants. The container is L2. The algorithm is L4.
