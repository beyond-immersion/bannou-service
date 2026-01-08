// =============================================================================
// Entity Lifecycle Integration Tests
// Tests end-to-end entity lifecycle flows including resolution, bindings,
// and control gate integration.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Coordination;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Control;
using System.Numerics;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Integration;

/// <summary>
/// Integration tests for entity lifecycle covering resolution, bindings,
/// and control gate integration.
/// </summary>
public sealed class EntityLifecycleIntegrationTests
{
    private readonly EntityResolver _resolver;
    private readonly ControlGateManager _controlGates;

    public EntityLifecycleIntegrationTests()
    {
        _resolver = new EntityResolver();
        _controlGates = new ControlGateManager();
    }

    // =========================================================================
    // ENTITY BINDING TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ParticipantBinding_ResolvesEntity()
    {
        // Arrange
        var heroId = Guid.NewGuid();
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("hero", EntityReference.Player(heroId, "player_warrior"))
            .Build();

        // Act
        var result = await _resolver.ResolveAsync("hero", bindings);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(heroId, result.EntityId);
        Assert.True(result.IsPlayer);
        Assert.Equal("player_warrior", result.ArchetypeId);
    }

    [Fact]
    public async Task ResolveAsync_PropBinding_ResolvesEntity()
    {
        // Arrange
        var doorId = Guid.NewGuid();
        var bindings = CutsceneBindings.Builder()
            .AddProp("exit_door", doorId)
            .Build();

        // Act
        var result = await _resolver.ResolveAsync("exit_door", bindings);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(doorId, result.EntityId);
        Assert.True(result.IsProp);
    }

    [Fact]
    public async Task ResolveAsync_RoleMapping_ResolvesViaRole()
    {
        // Arrange
        var bossId = Guid.NewGuid();
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("big_boss", EntityReference.Npc(bossId, "npc_boss", "antagonist"))
            .AddRole("villain", "big_boss")
            .Build();

        // Act
        var result = await _resolver.ResolveAsync("villain", bindings);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(bossId, result.EntityId);
    }

    [Fact]
    public async Task ResolveAsync_CaseInsensitive_ResolvesDifferentCase()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("MyCharacter", entityId)
            .Build();

        // Act
        var result = await _resolver.ResolveAsync("mycharacter", bindings);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(entityId, result.EntityId);
    }

    [Fact]
    public async Task ResolveAsync_ContextSelf_ResolvesFromContext()
    {
        // Arrange
        var selfId = Guid.NewGuid();
        var bindings = CutsceneBindings.Empty;
        var context = new EntityResolutionContext { RequestingEntity = selfId };

        // Act
        var result = await _resolver.ResolveAsync("self", bindings, context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(selfId, result.EntityId);
    }

    // =========================================================================
    // MULTI-ENTITY RESOLUTION TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveManyAsync_MultipleBindings_ResolvesAll()
    {
        // Arrange
        var heroId = Guid.NewGuid();
        var villainId = Guid.NewGuid();
        var doorId = Guid.NewGuid();

        var bindings = CutsceneBindings.Builder()
            .AddParticipant("hero", heroId)
            .AddParticipant("villain", villainId)
            .AddProp("door", doorId)
            .Build();

        // Act
        var results = await _resolver.ResolveManyAsync(
            new[] { "hero", "villain", "door", "missing" },
            bindings);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal(heroId, results["hero"].EntityId);
        Assert.Equal(villainId, results["villain"].EntityId);
        Assert.Equal(doorId, results["door"].EntityId);
        Assert.False(results.ContainsKey("missing"));
    }

    [Fact]
    public void CanResolve_ValidBinding_ReturnsTrue()
    {
        // Arrange
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("hero", Guid.NewGuid())
            .AddRole("protagonist", "hero")
            .Build();

        // Assert
        Assert.True(_resolver.CanResolve("hero", bindings));
        Assert.True(_resolver.CanResolve("protagonist", bindings));
        Assert.True(_resolver.CanResolve("self", bindings)); // Context binding
        Assert.False(_resolver.CanResolve("unknown", bindings));
    }

    // =========================================================================
    // ENTITY LIFECYCLE WITH CONTROL GATES
    // =========================================================================

    [Fact]
    public async Task EntityLifecycle_SpawnToCutsceneToDespawn_FullFlow()
    {
        // Arrange
        var npcId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        // Act - Step 1: Create control gates for entities
        var npcGate = _controlGates.GetOrCreate(npcId);
        var playerGate = _controlGates.GetOrCreate(playerId);

        // Assert - Initial state is Behavior controlled
        Assert.Equal(ControlSource.Behavior, npcGate.CurrentSource);
        Assert.Equal(ControlSource.Behavior, playerGate.CurrentSource);

        // Act - Step 2: Cutscene takes control
        await _controlGates.TakeCinematicControlAsync(
            new[] { npcId, playerId },
            "intro-cutscene");

        // Assert - Both under cinematic control
        Assert.Equal(ControlSource.Cinematic, npcGate.CurrentSource);
        Assert.Equal(ControlSource.Cinematic, playerGate.CurrentSource);

        // Act - Step 3: Cutscene ends, return control
        await _controlGates.ReturnCinematicControlAsync(
            new[] { npcId, playerId },
            ControlHandoff.Instant());

        // Assert - Back to behavior control
        Assert.Equal(ControlSource.Behavior, npcGate.CurrentSource);
        Assert.Equal(ControlSource.Behavior, playerGate.CurrentSource);

        // Act - Step 4: Despawn - remove control gates
        _controlGates.Remove(npcId);
        Assert.Null(_controlGates.Get(npcId));
    }

    // =========================================================================
    // CUTSCENE BINDINGS BUILDER TESTS
    // =========================================================================

    [Fact]
    public void CutsceneBindingsBuilder_ComplexBindings_BuildsCorrectly()
    {
        // Arrange & Act
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("hero", EntityReference.Player(Guid.NewGuid()))
            .AddParticipant("companion", EntityReference.Npc(Guid.NewGuid(), "npc_healer"))
            .AddProp("chest", Guid.NewGuid())
            .AddLocation("center", new Vector3(0, 0, 0))
            .AddLocation("exit", new Vector3(10, 0, 5))
            .AddRole("protagonist", "hero")
            .AddRole("support", "companion")
            .AddCustom("quest_id", "main_quest_1")
            .Build();

        // Assert
        Assert.Equal(2, bindings.Participants.Count);
        Assert.Single(bindings.Props);
        Assert.Equal(2, bindings.Locations.Count);
        Assert.Equal(2, bindings.Roles.Count);
        Assert.Equal("main_quest_1", bindings.Custom["quest_id"]);
    }

    // =========================================================================
    // ENTITY REFERENCE FACTORY TESTS
    // =========================================================================

    [Fact]
    public void EntityReference_FactoryMethods_CreateCorrectTypes()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var simple = EntityReference.FromId(id);
        var player = EntityReference.Player(id, "player_mage");
        var npc = EntityReference.Npc(id, "npc_guard", "guard");
        var prop = EntityReference.Prop(id, "object_door");

        // Assert
        Assert.Equal(id, simple.EntityId);
        Assert.False(simple.IsPlayer);
        Assert.False(simple.IsProp);

        Assert.True(player.IsPlayer);
        Assert.Equal("player_mage", player.ArchetypeId);

        Assert.False(npc.IsPlayer);
        Assert.Equal("npc_guard", npc.ArchetypeId);
        Assert.Equal("guard", npc.Role);

        Assert.True(prop.IsProp);
        Assert.Equal("object_door", prop.ArchetypeId);
    }

    // =========================================================================
    // MULTI-SYSTEM INTEGRATION TESTS
    // =========================================================================

    [Fact]
    public async Task EntityResolutionAndControl_IntegratedFlow_WorksTogether()
    {
        // Arrange - Create entities
        var merchantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        // Step 1: Set up control gates
        var merchantGate = _controlGates.GetOrCreate(merchantId);
        var customerGate = _controlGates.GetOrCreate(customerId);

        // Step 2: Create bindings for cutscene
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("merchant", EntityReference.Npc(merchantId, "npc_merchant", "shopkeeper"))
            .AddParticipant("customer", EntityReference.Player(customerId))
            .AddRole("seller", "merchant")
            .AddRole("buyer", "customer")
            .Build();

        // Step 3: Verify resolution works
        var resolvedMerchant = await _resolver.ResolveAsync("merchant", bindings);
        var resolvedBySeller = await _resolver.ResolveAsync("seller", bindings);
        Assert.NotNull(resolvedMerchant);
        Assert.NotNull(resolvedBySeller);
        Assert.Equal(merchantId, resolvedMerchant.EntityId);
        Assert.Equal(merchantId, resolvedBySeller.EntityId);

        // Step 4: Take cinematic control
        await _controlGates.TakeCinematicControlAsync(
            new[] { merchantId, customerId },
            "shop-greeting");

        Assert.Equal(ControlSource.Cinematic, merchantGate.CurrentSource);
        Assert.Equal(ControlSource.Cinematic, customerGate.CurrentSource);

        // Step 5: Return control
        await _controlGates.ReturnCinematicControlAsync(
            new[] { merchantId, customerId },
            ControlHandoff.InstantWithState(new EntityState { Stance = "standing" }));

        Assert.Equal(ControlSource.Behavior, merchantGate.CurrentSource);
        Assert.Equal(ControlSource.Behavior, customerGate.CurrentSource);
    }

    [Fact]
    public async Task ContextResolution_PlayerAndTarget_ResolvesFromVariables()
    {
        // Arrange
        var playerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var bindings = CutsceneBindings.Empty;
        var context = new EntityResolutionContext
        {
            RequestingEntity = playerId,
            Variables = new Dictionary<string, object?>
            {
                ["player"] = playerId,
                ["target"] = targetId
            }
        };

        // Act
        var selfResolved = await _resolver.ResolveAsync("self", bindings, context);
        var playerResolved = await _resolver.ResolveAsync("player", bindings, context);
        var targetResolved = await _resolver.ResolveAsync("target", bindings, context);

        // Assert
        Assert.NotNull(selfResolved);
        Assert.NotNull(playerResolved);
        Assert.NotNull(targetResolved);
        Assert.Equal(playerId, selfResolved.EntityId);
        Assert.Equal(playerId, playerResolved.EntityId);
        Assert.Equal(targetId, targetResolved.EntityId);
    }
}
