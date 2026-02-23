# Extending Bannou - Building L5 Extension Plugins

> **Version**: 1.0
> **Status**: Reference guide
> **Prerequisites**: [Plugin Development Guide](./PLUGIN-DEVELOPMENT.md), [Service Hierarchy](../reference/SERVICE-HIERARCHY.md), [Schema Rules](../reference/SCHEMA-RULES.md)
> **Related**: [Seed System](./SEED-SYSTEM.md), [Economy System](./ECONOMY-SYSTEM.md), [Behavior System](./BEHAVIOR-SYSTEM.md), [Orchestration Patterns](../reference/ORCHESTRATION-PATTERNS.md)

Bannou provides 48+ services with 690+ endpoints covering everything a multiplayer game backend needs -- authentication, economies, inventories, quests, matchmaking, voice, spatial data, NPC intelligence, and more. These services are deliberately **generic**: there is no "skills plugin," no "magic plugin," no "combat plugin," no "guild plugin." Game concepts like skills, spells, and guilds emerge from the **composition** of lower-level primitives (Seed, License, Status, Collection, Actor, Organization, Faction, Contract, and others).

This design gives Bannou maximum flexibility. But flexibility has a cost: a developer building a fantasy RPG doesn't want to think about "seed domain depths" and "license board adjacency" -- they want to call `SkillsClient.GetLevel("swordsmanship", characterId)` and get an integer back.

**That's what extensions are for.** L5 Extension plugins provide game-specific vocabulary, simplified APIs, and opinionated defaults on top of Bannou's generic primitives. This guide explains how to build them.

---

## Table of Contents

1. [What Extensions Are (And What They Are Not)](#1-what-extensions-are-and-what-they-are-not)
2. [Extension Architecture](#2-extension-architecture)
3. [The Six Extension Patterns](#3-the-six-extension-patterns)
4. [Extension Catalog](#4-extension-catalog)
5. [Genre Kits](#5-genre-kits)
6. [Building Your First Extension](#6-building-your-first-extension)
7. [DI Integration Points](#7-di-integration-points)
8. [Anti-Patterns](#8-anti-patterns)
9. [Testing Extensions](#9-testing-extensions)
10. [Deployment and Configuration](#10-deployment-and-configuration)
11. [Primitive-to-Concept Quick Reference](#11-primitive-to-concept-quick-reference)

---

## 1. What Extensions Are (And What They Are Not)

### The Facade Analogy

An extension is to Bannou what a UI component library is to HTML, CSS, and JavaScript. The rendering primitives are already there. The component library provides named, constrained, documented building blocks with sensible defaults. You gain convenience and vocabulary; you sacrifice some flexibility for clarity.

A "guild extension" does not add guilds to Bannou. Bannou already has everything needed for guilds: Organization (legal entities), Faction (seed-based group growth with norms), Contract (membership agreements), Relationship (member bonds), Permission (role-based access), and Chat (communication channels). What a guild extension does is give developers `GuildClient.PromoteMember(guildId, characterId, "officer")` instead of requiring them to orchestrate six separate service calls themselves.

### What Extensions Do

- **Provide game-specific vocabulary**: API endpoints use domain terms ("spell," "skill level," "guild rank") instead of primitive terms ("seed domain depth," "license node," "faction standing")
- **Compose multi-service operations**: A single extension endpoint can orchestrate calls to 3-6 services that would otherwise require the game server to coordinate
- **Bake game-specific configuration**: Register seed types, currency definitions, relationship types, and status templates on startup so game code never touches generic configuration APIs
- **Aggregate cross-service data**: Combine data from multiple services into game-specific response shapes (e.g., a "character sheet" combining Character, Seed, Status, License, and Inventory data)
- **Register DI providers**: Implement `IVariableProviderFactory` to expose computed game data to NPC behavior expressions, or `IPrerequisiteProviderFactory` to add game-specific quest prerequisites
- **Translate events**: Subscribe to generic events (e.g., `seed.phase.changed`) and re-publish as game-specific events (e.g., `skill.level_up`)

### What Extensions Do Not Do

- **Own authoritative state that belongs in L0-L4 services**: Extensions query primitives for data. They do not mirror or shadow that data in their own stores. The primitive is the single source of truth.
- **Replace or bypass existing primitives**: Extensions call `ISeedClient`, not raw Redis. They publish via `IMessageBus`, not raw RabbitMQ. They persist via `IStateStoreFactory`, not direct MySQL.
- **Create circular dependencies**: Extension A can depend on Extension B (with graceful degradation), but B cannot also depend on A.
- **Add infrastructure concerns**: State backends, message brokers, and service mesh are L0 responsibilities. Extensions use them; they don't extend them.
- **Violate the service hierarchy**: L5 depends downward on L0-L4. No L0-L4 service may depend on an L5 extension.

### When Do You Need an Extension?

**Build an extension when**:

1. Your game server has the same multi-service composition pattern in three or more places
2. Your game's developers find the primitive vocabulary confusing for their domain
3. You want NPC behaviors to reference game-specific computed values (via variable providers)
4. You need game-specific quest prerequisites (via prerequisite providers)
5. You want a simplified, constrained API surface that hides complexity your game doesn't need

**Use primitives directly when**:

1. The composition is truly one-off (used in one place)
2. Your team understands the primitive vocabulary and prefers the flexibility
3. You need full control over composition order and error handling
4. The operation maps cleanly to a single primitive call

### Why Bannou Doesn't Ship Extensions

Bannou's flagship game, Arcadia, uses the content flywheel: every system feeds into every other system, and content emerges from accumulated play history rather than hand-authored definitions. Arcadia doesn't need a "skills plugin" because a character's mastery is a composition of Seed growth, License unlocks, Status effects, Collection discoveries, Character-Personality traits, Ethology instincts, and Obligation constraints -- nine independent systems converging to make a character behave like a mage or a warrior. Applying a concrete "mage" definition on top of that would constrain the emergence, not enhance it.

Not every game needs or wants the full flywheel. A studio building a traditional MMO with fixed class definitions, explicit skill trees, and concrete guild mechanics should absolutely build extensions that provide those constraints. The primitives do the heavy lifting; the extension provides the vocabulary and the guard rails.

**Further reading**: [Why Are There No Skill, Magic, or Combat Plugins?](../faqs/WHY-ARE-THERE-NO-SKILL-MAGIC-OR-COMBAT-PLUGINS.md), [Why Is There No Player Housing Plugin?](../faqs/WHY-IS-THERE-NO-PLAYER-HOUSING-PLUGIN.md)

---

## 2. Extension Architecture

### Layer 5 in the Hierarchy

Extensions live at Layer 5 in the [Service Hierarchy](../reference/SERVICE-HIERARCHY.md):

```
┌─────────────────────────────────────────────────────────────┐
│ L5: EXTENSIONS (Your game-specific plugins)                 │
│ Depends on: L0, L1, L2*, L3*, L4*   (* = graceful degrade) │
├─────────────────────────────────────────────────────────────┤
│ L4: GAME FEATURES (Behavior, Matchmaking, Achievement...)   │
│ L3: APP FEATURES (Asset, Orchestrator, Documentation...)    │
│ L2: GAME FOUNDATION (Character, Realm, Item, Seed, Quest...)│
│ L1: APP FOUNDATION (Account, Auth, Connect, Permission...)  │
│ L0: INFRASTRUCTURE (State, Messaging, Mesh, Telemetry)      │
└─────────────────────────────────────────────────────────────┘
```

**L5 rules**:
- May depend on any layer (L0 through L5)
- Loads after all core plugins (PluginLoader sorts by `ServiceLayer` enum; `Extensions = 500`)
- Must handle absence of L3/L4/L5 dependencies gracefully (`GetService<T>()` + null check)
- Should not be depended upon by L0-L4 services (core services cannot know about extensions)
- When L5 is disabled, no core service should break

### Schema Declaration

Declare your extension's layer in the API schema using `x-service-layer`:

```yaml
# schemas/reputation-api.yaml
openapi: 3.0.0
info:
  title: Reputation Extension API
  version: 1.0.0
x-service-layer: Extensions    # Declares this as L5

servers:
  - url: http://localhost:5012
    description: Bannou service endpoint

paths:
  /reputation/get:
    post:
      operationId: GetReputation
      # ...
```

This generates the `[BannouService]` attribute with `layer: ServiceLayer.Extensions`, ensuring the plugin loads after all L0-L4 services.

### Plugin Structure

Extensions follow the same plugin structure as any Bannou service:

```
plugins/lib-reputation/
├── Generated/                           # Auto-generated (never edit)
│   ├── ReputationController.cs
│   ├── IReputationService.cs
│   ├── ReputationServiceConfiguration.cs
│   └── ReputationPermissionRegistration.cs
├── ReputationService.cs                 # Business logic (facade over primitives)
├── ReputationServiceModels.cs           # Internal models (if needed)
├── ReputationServiceEvents.cs           # Event handlers (if subscribing)
├── ReputationServicePlugin.cs           # Plugin registration
├── Providers/                           # DI provider implementations
│   └── ReputationVariableProvider.cs
└── lib-reputation.csproj

bannou-service/Generated/
├── Models/ReputationModels.cs           # Request/response models
├── Clients/ReputationClient.cs          # Client for other services to call
└── Events/ReputationEventsModels.cs     # Event models
```

### Dependency Injection Patterns

Extensions follow the same DI rules as other services, with one key consideration: L3, L4, and L5 dependencies are optional.

```csharp
[BannouService("reputation", typeof(IReputationService),
    lifetime: ServiceLifetime.Scoped,
    layer: ServiceLayer.Extensions)]
public partial class ReputationService : IReputationService
{
    // Hard dependencies (L0/L1/L2) - constructor injection
    // These are guaranteed available. DI fails at startup if missing.
    private readonly ISeedClient _seedClient;
    private readonly IFactionClient _factionClient;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<ReputationService> _logger;

    // Soft dependencies (L3/L4/L5) - resolved at runtime
    private readonly IServiceProvider _serviceProvider;

    public ReputationService(
        ISeedClient seedClient,          // L2 - always available
        IFactionClient factionClient,    // L4 - see note below
        IMessageBus messageBus,          // L0 - always available
        IServiceProvider serviceProvider,
        ILogger<ReputationService> logger)
    {
        _seedClient = seedClient;
        _factionClient = factionClient;
        _messageBus = messageBus;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private async Task EnrichWithAnalyticsAsync(ReputationResponse response)
    {
        // L4 dependency - may not be enabled
        var analyticsClient = _serviceProvider.GetService<IAnalyticsClient>();
        if (analyticsClient == null)
        {
            _logger.LogDebug("Analytics not enabled, skipping reputation history enrichment");
            return;
        }
        // ... enrich response with analytics data
    }
}
```

> **Note on Faction**: Faction is L4, which normally requires `GetService<T>()` + null check. However, if your extension fundamentally requires Faction to function (a reputation system without factions is meaningless), you can use constructor injection -- but document that your extension requires L4 Game Features to be enabled. Your extension's deep dive document should list this as a hard requirement.

### The Full Technical Details

For the complete plugin creation workflow (schema authoring, code generation, state management, event publishing, service-to-service calls, testing), see [Plugin Development Guide](./PLUGIN-DEVELOPMENT.md). Everything in that guide applies identically to L5 extensions. This document covers only what is **different or additional** for extensions.

---

## 3. The Six Extension Patterns

Extensions combine one or more of these patterns. Most use two or three.

### 3a. Semantic Facade

**What it does**: Translates game-specific vocabulary to primitive API calls. The simplest and most common extension pattern.

**When to use**: When developers want domain-specific names but no new business logic.

**Example**: A skills extension that exposes "get skill level" which internally queries Seed domain depth:

```csharp
public async Task<(StatusCodes, GetSkillLevelResponse?)> GetSkillLevelAsync(
    GetSkillLevelRequest request, CancellationToken ct)
{
    // "Skill level" is actually a seed domain depth for the character's
    // combat/warrior seed. The extension translates vocabulary.
    var (status, growth) = await _seedClient.GetGrowthAsync(
        new GetGrowthRequest
        {
            SeedId = request.CharacterSeedId,
            Domain = $"skills.{request.SkillCode}"
        }, ct);

    if (status != StatusCodes.OK || growth == null)
        return (status, null);

    // Translate floating-point depth to integer skill level
    var level = (int)Math.Floor(growth.Depth);
    var progress = growth.Depth - level;

    return (StatusCodes.OK, new GetSkillLevelResponse
    {
        SkillCode = request.SkillCode,
        Level = level,
        ProgressToNext = progress
    });
}
```

**What the game developer sees**: `POST /skills/get-level` with `{ skill_code, character_seed_id }` returning `{ level: 5, progress_to_next: 0.73 }`.

**What actually happens**: A Seed domain depth query with vocabulary translation.

---

### 3b. Composition Orchestrator

**What it does**: Coordinates multi-service operations that should appear atomic to the caller.

**When to use**: When a game operation requires coordinated changes across 3+ services. Similar to how Quest orchestrates Contract, or Escrow orchestrates Currency+Item.

**Example**: A death penalty extension that applies consequences across multiple services:

```csharp
public async Task<(StatusCodes, ApplyDeathPenaltyResponse?)> ApplyDeathPenaltyAsync(
    ApplyDeathPenaltyRequest request, CancellationToken ct)
{
    var penalties = new List<string>();

    // 1. Apply gold loss via Currency
    if (_configuration.GoldLossPercent > 0)
    {
        var (walletStatus, wallet) = await _currencyClient.GetWalletAsync(
            new GetWalletRequest
            {
                OwnerId = request.CharacterId,
                CurrencyCode = "gold"
            }, ct);

        if (walletStatus == StatusCodes.OK && wallet != null && wallet.Balance > 0)
        {
            var loss = (long)(wallet.Balance * _configuration.GoldLossPercent / 100.0);
            await _currencyClient.DebitAsync(new DebitRequest
            {
                WalletId = wallet.WalletId,
                Amount = loss,
                Reason = "death_penalty",
                IdempotencyKey = $"death-{request.DeathEventId}-gold"
            }, ct);
            penalties.Add($"Lost {loss} gold");
        }
    }

    // 2. Apply debuff via Status
    if (_configuration.DeathDebuffTemplateCode != null)
    {
        await _statusClient.GrantAsync(new GrantStatusRequest
        {
            EntityId = request.CharacterId,
            EntityType = "character",
            TemplateCode = _configuration.DeathDebuffTemplateCode,
            DurationSeconds = _configuration.DeathDebuffDurationSeconds
        }, ct);
        penalties.Add($"Applied {_configuration.DeathDebuffTemplateCode} debuff");
    }

    // 3. Publish game-specific event
    await _messageBus.PublishAsync("death-penalty.applied",
        new DeathPenaltyAppliedEvent
        {
            CharacterId = request.CharacterId,
            Penalties = penalties,
            DeathEventId = request.DeathEventId
        }, ct);

    return (StatusCodes.OK, new ApplyDeathPenaltyResponse { PenaltiesApplied = penalties });
}
```

---

### 3c. Configuration Hardener

**What it does**: Registers game-specific seed types, currency definitions, relationship types, and other configuration on startup, so game code never touches generic configuration APIs.

**When to use**: When your game has a known set of entity types, currencies, and relationships that should be established automatically.

**Example**: An RPG kit extension that registers all game-specific types on startup:

```csharp
protected override async Task OnRunningAsync(CancellationToken ct)
{
    // Register seed types for character classes
    var classTypes = new[] { "warrior", "mage", "ranger", "healer", "thief" };
    foreach (var classType in classTypes)
    {
        await _seedClient.RegisterSeedTypeAsync(new RegisterSeedTypeRequest
        {
            TypeCode = $"class_{classType}",
            OwnerTypes = new[] { "character" },
            MaxPerOwner = 1,
            GrowthPhases = new[]
            {
                new GrowthPhase { Label = "Novice", Threshold = 0.0 },
                new GrowthPhase { Label = "Apprentice", Threshold = 10.0 },
                new GrowthPhase { Label = "Journeyman", Threshold = 25.0 },
                new GrowthPhase { Label = "Expert", Threshold = 50.0 },
                new GrowthPhase { Label = "Master", Threshold = 100.0 }
            }
        }, ct);
    }

    // Register currency definitions
    await _currencyClient.DefineCurrencyAsync(new DefineCurrencyRequest
    {
        Code = "gold",
        DisplayName = "Gold Coins",
        Scope = CurrencyScope.Global
    }, ct);

    // Register relationship types
    await _relationshipClient.CreateTypeAsync(new CreateRelationshipTypeRequest
    {
        Code = "guild_member",
        DisplayName = "Guild Member",
        Bidirectional = false
    }, ct);
}
```

> **Idempotency**: Most registration endpoints use code-based upserts (create-or-update). Calling `RegisterSeedTypeAsync` with the same `TypeCode` twice is safe -- it updates the existing definition. Design your `OnRunningAsync` to be idempotent.

---

### 3d. View Aggregator

**What it does**: Combines data from multiple services into game-specific response shapes. Reduces client round-trips.

**When to use**: When clients need a combined view that would otherwise require N separate API calls.

**Example**: A character sheet extension that assembles data from six services:

```csharp
public async Task<(StatusCodes, CharacterSheetResponse?)> GetCharacterSheetAsync(
    GetCharacterSheetRequest request, CancellationToken ct)
{
    // Parallel queries to independent services
    var characterTask = _characterClient.GetAsync(
        new GetCharacterRequest { CharacterId = request.CharacterId }, ct);
    var seedsTask = _seedClient.ListByOwnerAsync(
        new ListSeedsByOwnerRequest
        {
            OwnerId = request.CharacterId,
            OwnerType = "character"
        }, ct);
    var statusTask = _statusClient.QueryAsync(
        new QueryStatusRequest
        {
            EntityId = request.CharacterId,
            EntityType = "character"
        }, ct);
    var inventoryTask = _inventoryClient.ListContainersAsync(
        new ListContainersRequest { OwnerId = request.CharacterId }, ct);

    await Task.WhenAll(characterTask, seedsTask, statusTask, inventoryTask);

    var (charStatus, character) = characterTask.Result;
    if (charStatus != StatusCodes.OK || character == null)
        return (charStatus, null);

    var (_, seeds) = seedsTask.Result;
    var (_, statuses) = statusTask.Result;
    var (_, containers) = inventoryTask.Result;

    // Assemble game-specific response
    return (StatusCodes.OK, new CharacterSheetResponse
    {
        Name = character.Name,
        SpeciesCode = character.SpeciesCode,
        Level = ComputeLevel(seeds),
        ClassName = DetermineClass(seeds),
        ActiveEffects = MapEffects(statuses),
        EquippedItems = MapEquipment(containers),
        SkillLevels = MapSkills(seeds)
    });
}
```

---

### 3e. Event Translator

**What it does**: Subscribes to generic Bannou events and re-publishes them as game-specific events that clients or other extensions understand.

**When to use**: When game systems need domain-specific event streams derived from primitive events.

**Example**: A skills extension that translates seed phase changes into "level up" events:

```csharp
// In OnRunningAsync or event registration
await _messageSubscriber.SubscribeAsync<SeedPhaseChangedEvent>(
    "seed.phase.changed",
    async (evt, ct) =>
    {
        // Only translate events for class-type seeds
        if (!evt.SeedTypeCode.StartsWith("class_"))
            return;

        var className = evt.SeedTypeCode.Replace("class_", "");

        // Publish game-specific "level up" event
        await _messageBus.PublishAsync("skills.level_up",
            new SkillLevelUpEvent
            {
                CharacterId = evt.OwnerId,
                ClassName = className,
                NewPhase = evt.NewPhase,
                PreviousPhase = evt.PreviousPhase,
                NewLevel = ComputeLevelFromPhase(evt.NewPhase)
            }, ct);

        _logger.LogInformation(
            "Character {CharacterId} leveled up {Class} to {Phase}",
            evt.OwnerId, className, evt.NewPhase);
    });
```

---

### 3f. Variable Provider

**What it does**: Implements `IVariableProviderFactory` to expose game-specific computed data to ABML behavior documents. This is how extension logic becomes available to NPC decision-making.

**When to use**: When your game has computed state that NPC behaviors need to reference in ABML expressions.

**Example**: A reputation variable provider that exposes `${reputation.thieves_guild}` to NPC behaviors:

```csharp
public class ReputationProviderFactory : IVariableProviderFactory
{
    private readonly ISeedClient _seedClient;

    public string ProviderName => "reputation";

    public ReputationProviderFactory(ISeedClient seedClient)
    {
        _seedClient = seedClient;
    }

    public async Task<IVariableProvider> CreateAsync(
        Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        if (!characterId.HasValue)
            return ReputationProvider.Empty;

        // Query all reputation seeds for this character
        var (_, seeds) = await _seedClient.ListByOwnerAsync(
            new ListSeedsByOwnerRequest
            {
                OwnerId = characterId.Value,
                OwnerType = "character",
                TypeCodePrefix = "reputation_"
            }, ct);

        return new ReputationProvider(seeds?.Seeds);
    }
}

public class ReputationProvider : IVariableProvider
{
    public static readonly ReputationProvider Empty = new(null);

    private readonly Dictionary<string, double> _standings;

    public string Name => "reputation";

    public ReputationProvider(IEnumerable<SeedSummary>? seeds)
    {
        _standings = new Dictionary<string, double>();
        if (seeds == null) return;

        foreach (var seed in seeds)
        {
            // "reputation_thieves_guild" -> "thieves_guild" = 45.0
            var factionCode = seed.TypeCode.Replace("reputation_", "");
            _standings[factionCode] = seed.TotalDepth;
        }
    }

    public object? GetVariable(string name)
    {
        return _standings.GetValueOrDefault(name, 0.0);
    }
}
```

**Registration** in ConfigureServices:

```csharp
services.AddSingleton<IVariableProviderFactory, ReputationProviderFactory>();
```

**Usage in ABML behaviors**:

```yaml
# An NPC guard checks the player's reputation before granting entry
conditions:
  - "${reputation.thieves_guild} < 20"      # Not friendly with thieves
  - "${reputation.city_guard} > 50"          # Good standing with guards
actions:
  - call: grant_passage
```

---

### Pattern Summary

| Pattern | Purpose | Complexity | Example |
|---------|---------|------------|---------|
| **Semantic Facade** | Vocabulary translation | Low | `GetSkillLevel` → Seed query |
| **Composition Orchestrator** | Multi-service coordination | Medium | Death penalty across Currency+Status+Inventory |
| **Configuration Hardener** | Startup registration | Low | Register class seed types on boot |
| **View Aggregator** | Cross-service data assembly | Medium | Character sheet from 6 services |
| **Event Translator** | Event stream transformation | Low | Seed phase change → "level up" event |
| **Variable Provider** | ABML behavior integration | Medium | `${reputation.faction_name}` for NPCs |

---

## 4. Extension Catalog

This catalog organizes extension opportunities by gameplay domain. Each entry lists the Bannou primitives being composed, the extension patterns used, and a representative API surface. These are illustrative designs -- your implementation may vary based on your game's specific needs.

### 4a. Character Progression

#### Skills and Abilities

Concrete skill levels, experience tracking, and ability unlocks over Bannou's generic growth and progression primitives.

| Primitive | Role in Extension |
|-----------|-------------------|
| **Seed** | Skill growth tracking (domain depths = skill levels) |
| **License** | Ability tree/skill tree (grid-based progression boards) |
| **Status** | Active ability effects (buffs from skill use) |
| **Collection** | Discovered/learned abilities (content unlock catalog) |
| **Actor + ABML** | NPC skill-based decision making |

**Patterns**: Semantic Facade, Variable Provider, Configuration Hardener

**Representative API**:
- `POST /skills/get-level` - Get a character's level in a specific skill
- `POST /skills/grant-experience` - Award skill experience (translates to seed growth)
- `POST /skills/list` - List all skills with levels for a character
- `POST /skills/use-ability` - Activate an ability (checks prerequisites, applies effects)

**Why Bannou doesn't build this**: A "skill" in Arcadia is nine systems converging -- Seed growth, License unlocks, Status effects, Collection discoveries, Character-Personality preferences, Ethology instincts, Obligation constraints, and more. Putting a concrete "Skills" service on top would constrain the emergence. See [Why No Skill/Magic/Combat Plugins?](../faqs/WHY-ARE-THERE-NO-SKILL-MAGIC-OR-COMBAT-PLUGINS.md)

---

#### Magic System

Spell definitions, mana management, casting mechanics, and cooldown tracking.

| Primitive | Role in Extension |
|-----------|-------------------|
| **Seed** | Magic proficiency growth per school |
| **License** | Spell unlock trees (license boards) |
| **Status** | Active spell effects, cooldowns |
| **Collection** | Discovered spells (spellbook) |
| **Currency** | Mana as a currency with regeneration (autogain worker) |
| **Character-Personality** | Casting preferences (`${combat.preferred_element}`) |

**Patterns**: Semantic Facade, Composition Orchestrator, Variable Provider

**Representative API**:
- `POST /magic/cast` - Cast a spell (check mana, apply effects, start cooldown)
- `POST /magic/spellbook` - List known spells with costs and cooldowns
- `POST /magic/learn` - Learn a new spell (grant collection entry, check prerequisites)

---

#### Class/Job System

Fixed character class definitions with ability progressions and class-specific mechanics.

| Primitive | Role in Extension |
|-----------|-------------------|
| **License** | Class-specific ability boards (one board per class) |
| **Seed** | Class mastery progression |
| **Status** | Class-specific passive effects |
| **Collection** | Unlocked class features |

**Patterns**: Configuration Hardener, View Aggregator, Semantic Facade

**Representative API**:
- `POST /class/assign` - Assign a class to a character (creates license board + seed)
- `POST /class/get-info` - Get class name, level, unlocked abilities, equipped passives
- `POST /class/change` - Class change (configurable: reset or preserve progress)

---

#### Combat System

Concrete damage types, resistance calculations, combat phases, and hit resolution.

| Primitive | Role in Extension |
|-----------|-------------------|
| **Actor + ABML** | Combat behavior scripts (attack patterns, AI) |
| **Character-Personality** | Combat preferences (`${combat.*}` variables) |
| **Ethology** | Species-level combat instincts (`${nature.*}` variables) |
| **Status** | Active buffs/debuffs during combat |
| **Mapping** | Spatial affordances (terrain, cover, elevation) |
| **Analytics** | Combat statistics for balancing |

**Patterns**: Event Translator, Variable Provider, Composition Orchestrator

**Representative API**:
- `POST /combat/calculate-damage` - Resolve a hit (damage type, resistance, modifiers)
- `POST /combat/get-stats` - Get combat stats from all contributing sources
- `POST /combat/log` - Query combat history for a character or encounter

---

### 4b. Social Systems

#### Guild System

Fantasy-flavored guild management over Organization, Faction, and related services.

| Primitive | Role in Extension |
|-----------|-------------------|
| **Organization** | Guild as a legal entity (assets, employees, structure) |
| **Faction** | Seed-based guild growth, norms, territory |
| **Contract** | Membership agreements (guild charter as contract template) |
| **Relationship** | Member-to-guild bonds, role hierarchy |
| **Permission** | Guild rank-based access control |
| **Chat** | Guild chat channels |
| **Currency** | Guild bank (organization-owned wallets) |

**Patterns**: Composition Orchestrator, Configuration Hardener, Semantic Facade

**Representative API**:
- `POST /guild/create` - Create guild (Organization + Faction + Contract template + Chat room)
- `POST /guild/invite` - Invite member (create Contract instance for membership agreement)
- `POST /guild/promote` - Change rank (update Relationship type + Permission role)
- `POST /guild/bank/deposit` - Deposit to guild treasury (Currency transfer)
- `POST /guild/list-members` - Get members with ranks and join dates

---

#### Party/Group System

Temporary adventuring groups with shared loot and communication.

| Primitive | Role in Extension |
|-----------|-------------------|
| **GameSession** | Party as a game session type |
| **Relationship** | Party member bonds (temporary) |
| **Permission** | Party leader permissions |
| **Chat** | Party chat room |

**Patterns**: Composition Orchestrator, Semantic Facade

**Representative API**:
- `POST /party/create` - Create party (GameSession + Chat room + Permission state)
- `POST /party/invite` - Add member with role
- `POST /party/set-loot-rules` - Configure loot distribution mode
- `POST /party/disband` - Clean up all associated resources

---

#### Reputation System

Concrete reputation tiers with faction-specific standings and rewards.

| Primitive | Role in Extension |
|-----------|-------------------|
| **Seed** | Reputation growth per faction (domain depths = standing) |
| **Faction** | Faction definitions and norm data |
| **Relationship** | Character-to-faction standing bonds |

**Patterns**: Semantic Facade, Variable Provider, Configuration Hardener

**Representative API**:
- `POST /reputation/get` - Get standing with a specific faction
- `POST /reputation/modify` - Increase or decrease reputation
- `POST /reputation/list` - All factions with standings and tiers
- `POST /reputation/check-tier` - Check if character has reached a tier threshold

---

#### Dialogue System

Branching NPC dialogue with mood effects and encounter tracking.

| Primitive | Role in Extension |
|-----------|-------------------|
| **Chat** | Dialogue as a typed message channel |
| **Character-Encounter** | Memorable conversation tracking |
| **Character-Personality** | NPC mood affecting dialogue options |
| **Actor** | NPC dialogue behavior scripts |

**Patterns**: Semantic Facade, Variable Provider, View Aggregator

**Representative API**:
- `POST /dialogue/start` - Initiate dialogue with NPC (creates Chat room, checks mood)
- `POST /dialogue/options` - Get available dialogue options (personality-weighted)
- `POST /dialogue/select` - Choose an option (records encounter, may shift relationship)

---

### 4c. Economy Extensions

#### Auction House

Simplified marketplace over the Market service with concrete listing and bidding mechanics.

| Primitive | Role in Extension |
|-----------|-------------------|
| **Market** | Core marketplace orchestration |
| **Currency** | Payment processing |
| **Item** | Listed item management |
| **Escrow** | Bid custody during auctions |

**Patterns**: Semantic Facade (Market already handles most mechanics)

**Representative API**:
- `POST /auction/list-item` - Create an auction listing
- `POST /auction/bid` - Place a bid (escrow funds)
- `POST /auction/buy-now` - Instant purchase
- `POST /auction/search` - Search listings with filters

---

#### Game-Specific Crafting

Concrete recipe definitions, material types, and crafting stations over the generic Craft service.

| Primitive | Role in Extension |
|-----------|-------------------|
| **Craft** | Recipe execution and session management |
| **Item + Inventory** | Material consumption and output placement |
| **Currency** | Crafting costs |
| **Seed** | Crafting proficiency progression |

**Patterns**: Configuration Hardener, Semantic Facade

**Representative API**:
- `POST /crafting/list-recipes` - Available recipes for character's proficiency
- `POST /crafting/start` - Begin crafting (creates Contract session via Craft)
- `POST /crafting/get-requirements` - Materials and station needed for a recipe

---

#### Death Penalty

Concrete consequences for character death across multiple services.

| Primitive | Role in Extension |
|-----------|-------------------|
| **Status** | Death debuffs (e.g., "resurrection sickness") |
| **Currency** | Gold/resource loss |
| **Inventory** | Item dropping on death |
| **Character-Lifecycle** | Death event source and processing |

**Patterns**: Composition Orchestrator, Event Translator

**Representative API**:
- `POST /death-penalty/apply` - Apply all death consequences
- `POST /death-penalty/get-config` - Current penalty configuration

---

### 4d. World and Space

#### Player Housing

Build mode, decoration, and visitor management over Gardener, Seed, and Scene.

| Primitive | Role in Extension |
|-----------|-------------------|
| **Gardener** | Housing as a garden type |
| **Seed** | Housing progression (phases: Humble → Grand) |
| **Scene** | Building layout (node tree with voxel nodes) |
| **Inventory + Item** | Furniture placement |
| **Save-Load** | Housing state persistence |
| **Permission** | Visitor access control |

**Patterns**: Configuration Hardener, Composition Orchestrator, Semantic Facade

**Representative API**:
- `POST /housing/get-home` - Get housing state (layout, furniture, visitors)
- `POST /housing/place-furniture` - Place furniture item (Inventory + Scene update)
- `POST /housing/set-permissions` - Configure visitor access levels
- `POST /housing/upgrade` - Trigger housing expansion (Seed phase check)

**Why Bannou doesn't build this**: Housing is structurally identical to dungeon cores -- both are garden types with seed-backed progression, scene-based layouts, and actor management. See [Why No Player Housing Plugin?](../faqs/WHY-IS-THERE-NO-PLAYER-HOUSING-PLUGIN.md)

---

#### Map and Minimap

Player-facing map discovery, fog of war, and point-of-interest tracking.

| Primitive | Role in Extension |
|-----------|-------------------|
| **Mapping** | Spatial data storage and queries |
| **Location** | Named places in the hierarchy |
| **Transit** | Routes and connections between locations |
| **Collection** | Discovered locations as collection entries |

**Patterns**: View Aggregator, Semantic Facade

**Representative API**:
- `POST /map/get-visible` - Get discovered map data for a character
- `POST /map/discover` - Mark an area as discovered (grants collection entry)
- `POST /map/get-route` - Get path between two discovered locations

---

#### Concrete Weather

Named weather types with game effects instead of raw Environment float axes.

| Primitive | Role in Extension |
|-----------|-------------------|
| **Environment** | Raw weather simulation data |
| **Worldstate** | Season and time-of-day context |

**Patterns**: Configuration Hardener, Event Translator, Variable Provider

**Representative API**:
- `POST /weather/current` - Get current weather as a named type ("thunderstorm", "clear sky")
- `POST /weather/forecast` - Predicted weather for upcoming game-time periods
- `POST /weather/effects` - Active gameplay effects from current weather

---

### 4e. Creatures and Entities

#### Pet/Mount System

Creature taming, bonding, training, and evolution.

| Primitive | Role in Extension |
|-----------|-------------------|
| **Actor** | Pet/mount AI behavior |
| **Seed** | Pet growth and loyalty progression |
| **Relationship** | Owner-pet bonds |
| **Character** | Pet as a system realm character (at Awakened stage) |
| **Ethology** | Species-level pet behavior defaults |
| **Status** | Active pet abilities/buffs granted to owner |

**Patterns**: Composition Orchestrator, Variable Provider, Configuration Hardener

**Representative API**:
- `POST /pets/tame` - Attempt to tame a creature (Seed + Relationship creation)
- `POST /pets/train` - Train a pet in a skill (Seed growth)
- `POST /pets/bond-level` - Get bond strength and unlocked abilities
- `POST /pets/list` - All pets owned by a character

---

#### Quest Journal

Simplified player-facing quest tracking over the Quest and Contract infrastructure.

| Primitive | Role in Extension |
|-----------|-------------------|
| **Quest** | Quest definitions and state |
| **Contract** | Underlying state machine |

**Patterns**: View Aggregator, Semantic Facade

**Representative API**:
- `POST /journal/active-quests` - All active quests with objectives and progress
- `POST /journal/completed` - Quest history
- `POST /journal/track` - Pin a quest for tracking
- `POST /journal/abandon` - Abandon a quest

---

#### Game Portal

Game-specific website that can show character profiles, leaderboards, and realm status. Lives at L5 because it needs L2 game data that L3 Website cannot access.

| Primitive | Role in Extension |
|-----------|-------------------|
| **Character** | Character profiles |
| **Realm** | Realm status and information |
| **Subscription** | Account game access |
| **Leaderboard** | Rankings display |
| **Analytics** | Game statistics |

**Patterns**: View Aggregator

**Why this needs to be L5**: The Website service (L3) intentionally cannot access game data (L2) to preserve the platform's non-game deployment capability. A game portal that shows character profiles, realm maps, and leaderboards needs L2+L4 dependencies, making it an L5 extension. See [Why Can't the Website Show Game Data?](../faqs/WHY-CANT-THE-WEBSITE-SHOW-GAME-DATA.md)

---

## 5. Genre Kits

Extensions naturally cluster into genre-specific bundles. These kits are illustrative -- they show how extensions compose for different game types, not prescriptive builds that must be followed exactly.

### Fantasy RPG Kit

For traditional fantasy RPGs with classes, skills, quests, and guilds.

```
Extensions:  Skills + Magic + Combat + Class/Job + Guild + Party
             + Reputation + Dialogue + Crafting + Death Penalty

Configuration Hardener registrations:
  Seed types:      class_warrior, class_mage, class_ranger, class_healer, class_thief
  Currencies:      gold, silver, mana_crystals, guild_marks
  Relationship:    guild_member, sworn_enemy, mentor, apprentice, party_bond
  Status:          poison, blessing, curse, haste, shield, resurrection_sickness

Variable Providers:
  ${skills.*}      Skill levels from seed domain depths
  ${magic.*}       Spell school proficiency
  ${reputation.*}  Faction standings
  ${class.*}       Class-specific computed data

Prerequisite Providers:
  skill            "Requires Swordsmanship level 5"
  magic            "Requires Fire Magic proficiency 3"
  reputation       "Requires Honored with City Guard"
```

### Sci-Fi Kit

For space games with ships, stations, and fleet management.

```
Extensions:  Ship + Station + Fleet + FTL + Crew

Composition over:
  Ship      = Actor (ship AI) + Seed (ship progression) + Inventory (cargo/modules)
              + Item (ship components) + Status (ship systems online/offline)
  Station   = Organization (station entity) + Location (docking hierarchy)
              + Utility (power/life support networks) + Workshop (manufacturing)
  Fleet     = Organization (fleet entity) + Relationship (ship-to-fleet bonds)
              + Actor (fleet AI coordination)
  FTL       = Transit (route connections) + Currency (fuel) + Worldstate (travel time)
  Crew      = Character (crew members) + Relationship (crew assignments)
              + Seed (crew specialization growth)
```

### Survival Kit

For survival games with needs systems and base building.

```
Extensions:  Needs (Hunger/Thirst/Fatigue) + Base Building + Temperature

Composition over:
  Needs         = Status (hunger/thirst/fatigue as ticking status effects)
                  + Currency (stamina as depletable resource)
                  + Worldstate (time-based decay via game clock)
  Building      = Scene (structure layouts) + Mapping (spatial placement)
                  + Inventory + Item (building materials)
                  + Save-Load (base state persistence)
  Temperature   = Environment (temperature axis) + Status (hypothermia/heatstroke)
                  + Worldstate (season/time-of-day effects)
```

### MMORPG Kit

For massively multiplayer games with traditional MMO features.

```
Extensions:  Class System + Guild + Party + Dungeon Finder + Auction House
             + Reputation + Quest Journal + Death Penalty + Combat

Composition over (in addition to individual extension primitives):
  Dungeon Finder  = Matchmaking (queue management) + GameSession (instance creation)
                    + Analytics (gear score calculation)
  Auction House   = Market + Currency + Item + Escrow
```

### Social Simulation Kit

For life simulation games focused on relationships and community.

```
Extensions:  Housing + Relationships + Jobs/Careers + Economy

Composition over:
  Housing       = Gardener + Seed + Scene + Inventory + Item
  Relationships = Relationship (social bonds) + Chat (communication)
                  + Character-Encounter (memorable moments)
                  + Character-Personality (compatibility)
  Jobs          = Seed (career progression) + Workshop (production)
                  + Currency (salary) + Organization (employer)
  Economy       = Market + Currency + Trade + Workshop
```

---

## 6. Building Your First Extension

This tutorial builds a **Reputation** extension step by step. We chose Reputation because it is small (4 endpoints), composes only a few services (Seed + Faction), and uses three patterns (Semantic Facade, Variable Provider, Configuration Hardener). Follow along to understand the end-to-end workflow.

> **Prerequisites**: You should be familiar with Bannou plugin development. If not, read [Plugin Development Guide](./PLUGIN-DEVELOPMENT.md) first.

### Step 1: Define the API Schema

Create `schemas/reputation-api.yaml`:

```yaml
openapi: 3.0.0
info:
  title: Reputation Extension API
  version: 1.0.0
  description: >
    Game-specific reputation system (L5 Extension) providing named faction
    standings over Seed growth primitives. Translates seed domain depths
    into reputation levels and tiers.
x-service-layer: Extensions

servers:
  - url: http://localhost:5012
    description: Bannou service endpoint

paths:
  /reputation/get:
    post:
      operationId: GetReputation
      summary: Get reputation with a faction
      description: Returns the character's standing with a specific faction as a level and tier.
      x-permissions:
        roles: [user]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetReputationRequest'
      responses:
        '200':
          description: Reputation retrieved
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GetReputationResponse'
        '404':
          description: Character or faction not found

  /reputation/modify:
    post:
      operationId: ModifyReputation
      summary: Modify reputation with a faction
      description: Increases or decreases a character's standing with a faction.
      x-permissions:
        roles: [developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ModifyReputationRequest'
      responses:
        '200':
          description: Reputation modified
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ModifyReputationResponse'

  /reputation/list:
    post:
      operationId: ListReputation
      summary: List all faction standings
      description: Returns all faction standings for a character.
      x-permissions:
        roles: [user]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListReputationRequest'
      responses:
        '200':
          description: Standings listed
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListReputationResponse'

  /reputation/check-tier:
    post:
      operationId: CheckTier
      summary: Check if character has reached a reputation tier
      description: Useful for prerequisite validation and quest gating.
      x-permissions:
        roles: [user]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CheckTierRequest'
      responses:
        '200':
          description: Tier check result
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CheckTierResponse'

components:
  schemas:
    GetReputationRequest:
      type: object
      required: [character_id, faction_code]
      properties:
        character_id:
          type: string
          format: uuid
          description: The character to query.
        faction_code:
          type: string
          minLength: 1
          maxLength: 100
          description: The faction code (e.g., "thieves_guild", "city_guard").

    GetReputationResponse:
      type: object
      required: [faction_code, level, tier, progress_to_next]
      properties:
        faction_code:
          type: string
          description: The queried faction code.
        faction_name:
          type: string
          description: Human-readable faction name (if available from Faction service).
        level:
          type: integer
          description: Integer reputation level (derived from seed domain depth).
        tier:
          type: string
          description: Named tier (e.g., "Hated", "Neutral", "Honored", "Exalted").
        progress_to_next:
          type: number
          format: double
          description: Progress toward next level (0.0 to 1.0).

    ModifyReputationRequest:
      type: object
      required: [character_id, faction_code, amount]
      properties:
        character_id:
          type: string
          format: uuid
          description: The character to modify.
        faction_code:
          type: string
          minLength: 1
          maxLength: 100
          description: The faction code.
        amount:
          type: number
          format: double
          description: Amount to add (positive) or subtract (negative).

    ModifyReputationResponse:
      type: object
      required: [faction_code, new_level, new_tier, tier_changed]
      properties:
        faction_code:
          type: string
          description: The modified faction code.
        new_level:
          type: integer
          description: Level after modification.
        new_tier:
          type: string
          description: Tier after modification.
        tier_changed:
          type: boolean
          description: Whether the modification caused a tier transition.

    ListReputationRequest:
      type: object
      required: [character_id]
      properties:
        character_id:
          type: string
          format: uuid
          description: The character to query.

    ListReputationResponse:
      type: object
      required: [standings]
      properties:
        standings:
          type: array
          items:
            $ref: '#/components/schemas/GetReputationResponse'
          description: All faction standings for the character.

    CheckTierRequest:
      type: object
      required: [character_id, faction_code, required_tier]
      properties:
        character_id:
          type: string
          format: uuid
          description: The character to check.
        faction_code:
          type: string
          description: The faction code.
        required_tier:
          type: string
          description: The tier to check for (e.g., "Honored").

    CheckTierResponse:
      type: object
      required: [meets_requirement]
      properties:
        meets_requirement:
          type: boolean
          description: Whether the character has reached the required tier.
        current_tier:
          type: string
          description: The character's current tier.
        current_level:
          type: integer
          description: The character's current level.
```

### Step 2: Define Events Schema

Create `schemas/reputation-events.yaml`:

```yaml
asyncapi: 2.6.0
info:
  title: Reputation Extension Events
  version: 1.0.0

channels:
  reputation.tier.changed:
    publish:
      message:
        $ref: '#/components/messages/ReputationTierChangedEvent'

components:
  messages:
    ReputationTierChangedEvent:
      payload:
        type: object
        required: [character_id, faction_code, previous_tier, new_tier, new_level]
        properties:
          character_id:
            type: string
            format: uuid
            description: The affected character.
          faction_code:
            type: string
            description: The faction whose standing changed.
          previous_tier:
            type: string
            description: Tier before the change.
          new_tier:
            type: string
            description: Tier after the change.
          new_level:
            type: integer
            description: Level at time of tier change.
```

### Step 3: Define Configuration Schema

Create `schemas/reputation-configuration.yaml`:

```yaml
x-service-configuration:
  properties:
    DefaultSeedTypePrefix:
      type: string
      default: "reputation_"
      description: Prefix for reputation seed type codes.
    TierThresholds:
      type: string
      default: "Hated:-100,Hostile:-50,Unfriendly:-25,Neutral:0,Friendly:25,Honored:50,Revered:75,Exalted:100"
      description: >
        Comma-separated tier definitions as Name:Threshold pairs.
        Thresholds represent minimum seed depth for each tier.
```

### Step 4: Generate the Plugin

```bash
cd scripts && ./generate-service.sh reputation
```

This generates the controller, service interface, models, configuration class, and client.

### Step 5: Implement the Service

Create `plugins/lib-reputation/ReputationService.cs`:

```csharp
namespace BeyondImmersion.Bannou.Reputation;

[BannouService("reputation", typeof(IReputationService),
    lifetime: ServiceLifetime.Scoped,
    layer: ServiceLayer.Extensions)]
public partial class ReputationService : IReputationService
{
    private readonly ISeedClient _seedClient;
    private readonly IMessageBus _messageBus;
    private readonly ReputationServiceConfiguration _configuration;
    private readonly ILogger<ReputationService> _logger;
    private readonly List<(string Name, double Threshold)> _tiers;

    public ReputationService(
        ISeedClient seedClient,
        IMessageBus messageBus,
        ReputationServiceConfiguration configuration,
        ILogger<ReputationService> logger)
    {
        _seedClient = seedClient;
        _messageBus = messageBus;
        _configuration = configuration;
        _logger = logger;
        _tiers = ParseTiers(configuration.TierThresholds);
    }

    public async Task<(StatusCodes, GetReputationResponse?)> GetReputationAsync(
        GetReputationRequest request, CancellationToken ct)
    {
        var seedTypeCode = $"{_configuration.DefaultSeedTypePrefix}{request.FactionCode}";

        var (status, growth) = await _seedClient.GetGrowthAsync(
            new GetGrowthRequest
            {
                OwnerId = request.CharacterId,
                OwnerType = "character",
                SeedTypeCode = seedTypeCode
            }, ct);

        if (status == StatusCodes.NotFound)
        {
            // No reputation yet - return neutral
            return (StatusCodes.OK, new GetReputationResponse
            {
                FactionCode = request.FactionCode,
                Level = 0,
                Tier = GetTierForDepth(0),
                ProgressToNext = 0.0
            });
        }

        if (status != StatusCodes.OK || growth == null)
            return (status, null);

        var level = (int)Math.Floor(growth.TotalDepth);

        return (StatusCodes.OK, new GetReputationResponse
        {
            FactionCode = request.FactionCode,
            Level = level,
            Tier = GetTierForDepth(growth.TotalDepth),
            ProgressToNext = growth.TotalDepth - level
        });
    }

    public async Task<(StatusCodes, ModifyReputationResponse?)> ModifyReputationAsync(
        ModifyReputationRequest request, CancellationToken ct)
    {
        var seedTypeCode = $"{_configuration.DefaultSeedTypePrefix}{request.FactionCode}";

        // Get current standing for tier comparison
        var (_, currentGrowth) = await _seedClient.GetGrowthAsync(
            new GetGrowthRequest
            {
                OwnerId = request.CharacterId,
                OwnerType = "character",
                SeedTypeCode = seedTypeCode
            }, ct);

        var previousTier = GetTierForDepth(currentGrowth?.TotalDepth ?? 0);

        // Record growth
        var (status, result) = await _seedClient.RecordGrowthAsync(
            new RecordGrowthRequest
            {
                OwnerId = request.CharacterId,
                OwnerType = "character",
                SeedTypeCode = seedTypeCode,
                Domain = "standing",
                Amount = request.Amount
            }, ct);

        if (status != StatusCodes.OK || result == null)
            return (status, null);

        var newLevel = (int)Math.Floor(result.NewTotalDepth);
        var newTier = GetTierForDepth(result.NewTotalDepth);
        var tierChanged = previousTier != newTier;

        // Publish tier change event if applicable
        if (tierChanged)
        {
            await _messageBus.PublishAsync("reputation.tier.changed",
                new ReputationTierChangedEvent
                {
                    CharacterId = request.CharacterId,
                    FactionCode = request.FactionCode,
                    PreviousTier = previousTier,
                    NewTier = newTier,
                    NewLevel = newLevel
                }, ct);

            _logger.LogInformation(
                "Character {CharacterId} reputation with {Faction} changed tier: {Old} -> {New}",
                request.CharacterId, request.FactionCode, previousTier, newTier);
        }

        return (StatusCodes.OK, new ModifyReputationResponse
        {
            FactionCode = request.FactionCode,
            NewLevel = newLevel,
            NewTier = newTier,
            TierChanged = tierChanged
        });
    }

    // ... ListReputationAsync and CheckTierAsync follow similar patterns

    private string GetTierForDepth(double depth)
    {
        // Tiers are sorted by threshold descending; return first match
        for (var i = _tiers.Count - 1; i >= 0; i--)
        {
            if (depth >= _tiers[i].Threshold)
                return _tiers[i].Name;
        }
        return _tiers[0].Name;
    }

    private static List<(string Name, double Threshold)> ParseTiers(string config)
    {
        return config.Split(',')
            .Select(pair =>
            {
                var parts = pair.Split(':');
                return (Name: parts[0], Threshold: double.Parse(parts[1]));
            })
            .OrderBy(t => t.Threshold)
            .ToList();
    }
}
```

### Step 6: Implement the Variable Provider

Create `plugins/lib-reputation/Providers/ReputationProviderFactory.cs` following the pattern shown in [Section 3f](#3f-variable-provider). Register it in your plugin's DI setup:

```csharp
services.AddSingleton<IVariableProviderFactory, ReputationProviderFactory>();
```

This makes `${reputation.thieves_guild}`, `${reputation.city_guard}`, etc. available in all ABML behavior documents.

### Step 7: Implement the Prerequisite Provider

Create a prerequisite provider so quests can require reputation tiers:

```csharp
public class ReputationPrerequisiteProviderFactory : IPrerequisiteProviderFactory
{
    private readonly ISeedClient _seedClient;
    private readonly ReputationServiceConfiguration _configuration;

    public string ProviderName => "reputation";

    public ReputationPrerequisiteProviderFactory(
        ISeedClient seedClient,
        ReputationServiceConfiguration configuration)
    {
        _seedClient = seedClient;
        _configuration = configuration;
    }

    public async Task<PrerequisiteResult> CheckAsync(
        Guid characterId,
        string prerequisiteCode,   // e.g., "thieves_guild"
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct)
    {
        var seedTypeCode = $"{_configuration.DefaultSeedTypePrefix}{prerequisiteCode}";

        var (_, growth) = await _seedClient.GetGrowthAsync(
            new GetGrowthRequest
            {
                OwnerId = characterId,
                OwnerType = "character",
                SeedTypeCode = seedTypeCode
            }, ct);

        var currentDepth = growth?.TotalDepth ?? 0;
        var requiredLevel = parameters.TryGetValue("level", out var levelObj) && levelObj != null
            ? Convert.ToDouble(levelObj)
            : 0;

        return currentDepth >= requiredLevel
            ? PrerequisiteResult.Success()
            : PrerequisiteResult.Failure(
                $"Requires {prerequisiteCode} reputation level {requiredLevel}",
                currentDepth,
                requiredLevel);
    }
}
```

Register:

```csharp
services.AddSingleton<IPrerequisiteProviderFactory, ReputationPrerequisiteProviderFactory>();
```

Now quest definitions can include reputation prerequisites:

```json
{
  "prerequisites": [
    { "type": "reputation", "code": "city_guard", "level": 50 }
  ]
}
```

### Step 8: Write Unit Tests

```csharp
[Test]
public async Task GetReputation_NoExistingReputation_ReturnsNeutral()
{
    // Arrange
    var seedClient = Substitute.For<ISeedClient>();
    seedClient.GetGrowthAsync(Arg.Any<GetGrowthRequest>(), Arg.Any<CancellationToken>())
        .Returns((StatusCodes.NotFound, (GetGrowthResponse?)null));

    var service = CreateService(seedClient: seedClient);

    // Act
    var (status, response) = await service.GetReputationAsync(
        new GetReputationRequest
        {
            CharacterId = Guid.NewGuid(),
            FactionCode = "thieves_guild"
        });

    // Assert
    Assert.That(status, Is.EqualTo(StatusCodes.OK));
    Assert.That(response?.Level, Is.EqualTo(0));
    Assert.That(response?.Tier, Is.EqualTo("Neutral"));
}

[Test]
public async Task ModifyReputation_CrossesTierThreshold_PublishesTierChangedEvent()
{
    // Arrange: character at depth 49.5 (Honored starts at 50)
    var seedClient = Substitute.For<ISeedClient>();
    seedClient.GetGrowthAsync(Arg.Any<GetGrowthRequest>(), Arg.Any<CancellationToken>())
        .Returns((StatusCodes.OK, new GetGrowthResponse { TotalDepth = 49.5 }));
    seedClient.RecordGrowthAsync(Arg.Any<RecordGrowthRequest>(), Arg.Any<CancellationToken>())
        .Returns((StatusCodes.OK, new RecordGrowthResponse { NewTotalDepth = 51.0 }));

    var messageBus = Substitute.For<IMessageBus>();
    var service = CreateService(seedClient: seedClient, messageBus: messageBus);

    // Act
    var (status, response) = await service.ModifyReputationAsync(
        new ModifyReputationRequest
        {
            CharacterId = Guid.NewGuid(),
            FactionCode = "city_guard",
            Amount = 1.5
        });

    // Assert
    Assert.That(status, Is.EqualTo(StatusCodes.OK));
    Assert.That(response?.TierChanged, Is.True);
    Assert.That(response?.NewTier, Is.EqualTo("Honored"));

    await messageBus.Received(1).PublishAsync(
        "reputation.tier.changed",
        Arg.Is<ReputationTierChangedEvent>(e =>
            e.NewTier == "Honored" && e.PreviousTier == "Friendly"),
        cancellationToken: Arg.Any<CancellationToken>());
}
```

### Step 9: Enable and Test

```bash
# Build
dotnet build plugins/lib-reputation/lib-reputation.csproj --no-restore

# Run unit tests
dotnet test plugins/lib-reputation.tests/lib-reputation.tests.csproj --no-restore

# Enable in .env
REPUTATION_SERVICE_ENABLED=true
```

---

## 7. DI Integration Points

Extensions integrate with core services through provider interfaces defined in `bannou-service/Providers/`. These are the "hooks" that let L5 extensions influence L0-L4 behavior without hierarchy violations.

| Interface | Consumed By | Purpose | Direction |
|-----------|-------------|---------|-----------|
| `IVariableProviderFactory` | Actor (L2) | Expose computed data to ABML `${namespace.*}` expressions | L5 data → L2 pull |
| `IPrerequisiteProviderFactory` | Quest (L2) | Validate game-specific quest prerequisites | L5 data → L2 pull |
| `ISeededResourceProvider` | Resource (L1) | Ship embedded resources (ABML behaviors, templates) | L5 data → L1 pull |
| `IBehaviorDocumentProvider` | Actor (L2) | Provide runtime-loaded ABML behavior documents | L5 data → L2 pull |
| `ISeedEvolutionListener` | Seed (L2) | React to seed growth and phase changes | L2 notification → L5 push |
| `ICollectionUnlockListener` | Collection (L2) | React to collection entry unlocks | L2 notification → L5 push |
| `ISessionActivityListener` | Connect (L1) | React to session connect/disconnect events | L1 notification → L5 push |

### Registration Pattern

All provider interfaces are registered as singletons in your plugin's `ConfigureServices`:

```csharp
// Providers (pull - always distributed-safe)
services.AddSingleton<IVariableProviderFactory, MyVariableProvider>();
services.AddSingleton<IPrerequisiteProviderFactory, MyPrerequisiteProvider>();
services.AddSingleton<ISeededResourceProvider, MyResourceProvider>();

// Listeners (push - local-only fan-out, see warning below)
services.AddSingleton<ISeedEvolutionListener, MySeedListener>();
services.AddSingleton<ICollectionUnlockListener, MyCollectionListener>();
```

### Distributed Safety Warning for Listeners

**Providers** (pull) are always distributed-safe: the consumer initiates the request on whichever node needs data. The provider reads from distributed state (Redis/MySQL).

**Listeners** (push) fire only on the node that processed the API request. In multi-node deployments, other nodes are NOT notified via the listener. This means:

1. **Listener reactions MUST write to distributed state** (Redis/MySQL), never local in-memory state
2. **If per-node awareness is required** (e.g., invalidating a `ConcurrentDictionary` cache), subscribe to the broadcast event via `IEventConsumer` instead
3. **Listeners are an optimization**, not a replacement for event subscriptions

See the "DI Provider vs Listener: Distributed Safety" section in [Service Hierarchy](../reference/SERVICE-HIERARCHY.md) for the full rules.

---

## 8. Anti-Patterns

### 8a. Shadow State

**Wrong**: Extension maintains its own MySQL table of "skill levels" by subscribing to seed events and mirroring domain depths.

**Why it fails**: Two sources of truth. When they diverge -- network partitions, event ordering, missed events -- the system is inconsistent. Which is authoritative?

**Right**: Extension queries `ISeedClient` on demand. Cache in Redis with short TTL if needed for performance. The Seed store is the single source of truth.

---

### 8b. Hierarchy Bypass

**Wrong**: Extension uses raw Redis connections to read state that belongs to `lib-state`, or directly publishes to RabbitMQ exchanges owned by `lib-messaging`.

**Why it fails**: Breaks infrastructure abstraction. When lib-state migrates backends or lib-messaging changes exchange topology, the extension breaks.

**Right**: Use the provided interfaces: `IStateStoreFactory`, `IMessageBus`, generated service clients.

---

### 8c. Upward Dependency Leakage

**Wrong**: Extension exposes a useful API, then a core L2 service starts calling it, creating an implicit upward dependency.

**Why it fails**: L2 cannot depend on L5. If the extension is disabled, the L2 service breaks. This violates the fundamental hierarchy rule.

**Right**: If core services need your data, implement a DI provider interface (see [Section 7](#7-di-integration-points)). Core services discover providers via `IEnumerable<T>` with graceful degradation.

---

### 8d. God Extension

**Wrong**: A single extension wraps ALL game-specific logic. `lib-my-game` with 200 endpoints covering skills, combat, housing, guilds, quests, and economy.

**Why it fails**: Defeats the purpose of the plugin architecture. Cannot disable parts independently. Cannot scale or deploy selectively. Becomes a monolith within the monoservice.

**Right**: One extension per gameplay domain. They can depend on each other (L5-to-L5 is allowed with graceful degradation).

---

### 8e. Primitive Reimplementation

**Wrong**: Extension implements its own currency system because "we need slightly different transfer semantics."

**Why it fails**: Duplicates L2 functionality. Other services (Escrow, Quest, Market) integrate with `ICurrencyClient`, not your custom currency. You lose the entire composability model.

**Right**: Configure the existing Currency service. If you truly need different semantics, contribute to the primitive or use Currency's extensibility features.

---

### 8f. Event Storm

**Wrong**: Extension subscribes to every event from every service, processes them all, and re-publishes game-specific events for every one.

**Why it fails**: Massive RabbitMQ load. Most events are irrelevant. Creates a bottleneck that scales linearly with world activity.

**Right**: Subscribe only to events your extension needs. Filter early. Batch processing where possible.

---

### 8g. Startup Registration Race

**Wrong**: Extension calls service APIs (e.g., `ISeedClient.RegisterSeedTypeAsync`) in its constructor.

**Why it fails**: The Seed service may not be fully started yet. PluginLoader loads L5 after L2, but DI registration and service startup are separate phases.

**Right**: Use `OnRunningAsync()` for any API calls that depend on other services being available. Constructor should only store injected references.

---

## 9. Testing Extensions

Extensions follow the same testing patterns as all Bannou plugins. See [Testing Guide](../operations/TESTING.md) for the full architecture.

### What to Test

| Category | What to Verify |
|----------|----------------|
| **Facade translation** | Seed depth 5.3 maps to skill level 5 with 0.3 progress |
| **Composition ordering** | All client calls made in correct sequence with proper error handling |
| **Provider implementations** | Variable provider returns correct values for given state |
| **Event translation** | Input event X produces output event Y with correct field mapping |
| **Graceful degradation** | When optional L3/L4 service is null, extension returns reduced (not broken) results |
| **Configuration parsing** | Tier thresholds, defaults, and edge cases handled correctly |
| **Idempotency** | `OnRunningAsync` registrations are safe to call multiple times |

### Hierarchy Validation

Use `ServiceHierarchyValidator` to catch hierarchy violations at test time:

```csharp
[Test]
public void ReputationService_RespectsDependencyHierarchy()
{
    ServiceHierarchyValidator.ValidateServiceHierarchy<ReputationService>();
}
```

This scans constructor parameters for service client injections and verifies each dependency is in an allowed layer.

---

## 10. Deployment and Configuration

### Enabling Extensions

Extensions are enabled via environment variables, just like any Bannou plugin:

```bash
# In .env
REPUTATION_SERVICE_ENABLED=true
GUILD_SERVICE_ENABLED=true
COMBAT_SERVICE_ENABLED=true
```

### Extensions Are Always Optional

No core service (L0-L4) depends on any extension. Disabling an extension never breaks core functionality. This is enforced by the service hierarchy -- L0-L4 services cannot inject L5 clients.

### Extension-to-Extension Dependencies

Extensions can depend on each other with graceful degradation:

```csharp
// A "guild reputation" feature that enriches guild data with reputation standings
var reputationClient = _serviceProvider.GetService<IReputationClient>();
if (reputationClient == null)
{
    _logger.LogDebug("Reputation extension not enabled, skipping reputation enrichment");
    // Return guild data without reputation enrichment
    return baseResponse;
}
// Enrich with reputation data
```

### Within L5, plugins load alphabetically. If Extension B's `OnRunningAsync` needs Extension A to be fully started, use event-based coordination or DI provider discovery rather than relying on load order.

---

## 11. Primitive-to-Concept Quick Reference

The master mapping table for translating game concepts to Bannou primitives:

| Game Concept | Bannou Primitive | What It Answers |
|--------------|-----------------|-----------------|
| Skill levels | **Seed** domain depth | "How good am I at this?" |
| Ability/skill trees | **License** boards | "What can I unlock next?" |
| Buffs and debuffs | **Status** effects | "What's currently affecting me?" |
| Discovered content | **Collection** entries | "What have I found/learned?" |
| Combat preferences | **Character-Personality** | "How do I prefer to fight?" |
| Species instincts | **Ethology** behavioral axes | "What am I naturally inclined to do?" |
| NPC decision-making | **Actor** + **ABML** + **GOAP** | "What should I do next?" |
| Progressive mastery | **Seed** growth phases | "Am I a novice or a master?" |
| Money and resources | **Currency** wallets | "What currency do I have?" |
| Physical objects | **Item** templates and instances | "What items exist?" |
| Containers and bags | **Inventory** containers | "Where are items placed?" |
| Binding agreements | **Contract** state machines | "What have I committed to?" |
| Quests and objectives | **Quest** (over Contract) | "What am I trying to accomplish?" |
| Social bonds | **Relationship** entities | "Who do I know, and how?" |
| Organizations | **Organization** + **Faction** | "What groups exist, and what can they do?" |
| Moral constraints | **Obligation** cost modifiers | "What should I avoid doing?" |
| World time and seasons | **Worldstate** game clock | "What time/season is it?" |
| Physical places | **Location** hierarchy | "Where am I?" |
| Routes and travel | **Transit** connections | "How do I get there?" |
| Weather and ecology | **Environment** condition axes | "What are conditions like here?" |
| Spatial data | **Mapping** 3D index | "What's near me?" |
| 3D layouts | **Scene** node trees | "What does this place look like?" |
| Atomic multi-party trades | **Escrow** custody manager | "How do we exchange safely?" |
| Competitive rankings | **Leaderboard** sorted sets | "Who's the best?" |
| Matchmaking queues | **Matchmaking** ticket system | "Who should I play with?" |
| Game state saves | **Save-Load** versioned persistence | "How do I save my progress?" |
| Binary assets | **Asset** storage + pre-signed URLs | "Where are my textures/models/audio?" |
| Procedural geometry | **Procedural** (Houdini) | "Can I generate 3D content on demand?" |
| NPC orchestration | **Puppetmaster** regional watchers | "Who coordinates NPC groups?" |
| Player experience | **Gardener** garden lifecycle | "Who orchestrates what I encounter?" |
| Voice chat | **Voice** room coordination | "How do I talk to other players?" |

---

## Further Reading

- [Plugin Development Guide](./PLUGIN-DEVELOPMENT.md) - Complete plugin creation workflow
- [Service Hierarchy](../reference/SERVICE-HIERARCHY.md) - Layer rules and dependency patterns
- [Schema Rules](../reference/SCHEMA-RULES.md) - OpenAPI schema authoring rules
- [Testing Guide](../operations/TESTING.md) - Test architecture and placement rules
- [Seed System](./SEED-SYSTEM.md) - How progressive growth works
- [Economy System](./ECONOMY-SYSTEM.md) - Currency, Item, Inventory, and Escrow
- [Behavior System](./BEHAVIOR-SYSTEM.md) - ABML, GOAP, and Actor
- [Orchestration Patterns](../reference/ORCHESTRATION-PATTERNS.md) - How god-actors drive the content flywheel

### Relevant FAQs

- [Why Are There No Skill, Magic, or Combat Plugins?](../faqs/WHY-ARE-THERE-NO-SKILL-MAGIC-OR-COMBAT-PLUGINS.md)
- [Why Is There No Player Housing Plugin?](../faqs/WHY-IS-THERE-NO-PLAYER-HOUSING-PLUGIN.md)
- [Why Can't the Website Show Game Data?](../faqs/WHY-CANT-THE-WEBSITE-SHOW-GAME-DATA.md)
- [What Is a Seed and Why Is It Foundational?](../faqs/WHAT-IS-A-SEED-AND-WHY-IS-IT-FOUNDATIONAL.md)
- [What Is the Difference Between License and Collection?](../faqs/WHAT-IS-THE-DIFFERENCE-BETWEEN-LICENSE-AND-COLLECTION.md)
- [Why Is Quest a Contract Wrapper?](../faqs/WHY-IS-QUEST-A-CONTRACT-WRAPPER.md)

---

*This document is a guide for third-party extension developers. For Bannou's own architectural decisions about why these extensions aren't built into the platform, see the linked FAQs above.*
