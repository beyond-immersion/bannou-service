// =============================================================================
// Entity Resolver Tests
// Tests for semantic binding name resolution in cutscenes.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Coordination;
using BeyondImmersion.BannouService.Behavior;
using System.Numerics;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Coordination;

/// <summary>
/// Tests for <see cref="EntityResolver"/>.
/// </summary>
public sealed class EntityResolverTests
{
    private readonly EntityResolver _resolver = new();

    // =========================================================================
    // PARTICIPANT RESOLUTION TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ParticipantBinding_ReturnsReference()
    {
        // Arrange
        var heroId = Guid.NewGuid();
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("hero", EntityReference.Player(heroId))
            .Build();

        // Act
        var result = await _resolver.ResolveAsync("hero", bindings);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(heroId, result.EntityId);
        Assert.True(result.IsPlayer);
    }

    [Fact]
    public async Task ResolveAsync_MultipleParticipants_ResolvesCorrectOne()
    {
        // Arrange
        var heroId = Guid.NewGuid();
        var villainId = Guid.NewGuid();
        var allyId = Guid.NewGuid();

        var bindings = CutsceneBindings.Builder()
            .AddParticipant("hero", EntityReference.Player(heroId))
            .AddParticipant("villain", EntityReference.Npc(villainId, "humanoid", "antagonist"))
            .AddParticipant("ally", EntityReference.Npc(allyId, "humanoid", "support"))
            .Build();

        // Act
        var hero = await _resolver.ResolveAsync("hero", bindings);
        var villain = await _resolver.ResolveAsync("villain", bindings);
        var ally = await _resolver.ResolveAsync("ally", bindings);

        // Assert
        Assert.Equal(heroId, hero?.EntityId);
        Assert.Equal(villainId, villain?.EntityId);
        Assert.Equal(allyId, ally?.EntityId);
    }

    [Fact]
    public async Task ResolveAsync_CaseInsensitive_Works()
    {
        // Arrange
        var heroId = Guid.NewGuid();
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("Hero", EntityReference.Player(heroId))
            .Build();

        // Act - try different casings
        var result1 = await _resolver.ResolveAsync("hero", bindings);
        var result2 = await _resolver.ResolveAsync("HERO", bindings);
        var result3 = await _resolver.ResolveAsync("Hero", bindings);

        // Assert - all should resolve to same entity
        Assert.Equal(heroId, result1?.EntityId);
        Assert.Equal(heroId, result2?.EntityId);
        Assert.Equal(heroId, result3?.EntityId);
    }

    // =========================================================================
    // PROP RESOLUTION TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_PropBinding_ReturnsReference()
    {
        // Arrange
        var doorId = Guid.NewGuid();
        var bindings = CutsceneBindings.Builder()
            .AddProp("secret_door", doorId)
            .Build();

        // Act
        var result = await _resolver.ResolveAsync("secret_door", bindings);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(doorId, result.EntityId);
        Assert.True(result.IsProp);
    }

    [Fact]
    public async Task ResolveAsync_PropWithMetadata_ReturnsFullReference()
    {
        // Arrange
        var chestId = Guid.NewGuid();
        var chestRef = new EntityReference
        {
            EntityId = chestId,
            ArchetypeId = "treasure_chest",
            IsProp = true,
            Metadata = new Dictionary<string, object> { ["loot_tier"] = "legendary" }
        };

        var bindings = CutsceneBindings.Builder()
            .AddProp("treasure", chestRef)
            .Build();

        // Act
        var result = await _resolver.ResolveAsync("treasure", bindings);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(chestId, result.EntityId);
        Assert.Equal("treasure_chest", result.ArchetypeId);
        Assert.True(result.IsProp);
        Assert.NotNull(result.Metadata);
        Assert.Equal("legendary", result.Metadata["loot_tier"]);
    }

    // =========================================================================
    // ROLE RESOLUTION TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_RoleMapping_ResolvesToBoundParticipant()
    {
        // Arrange
        var heroId = Guid.NewGuid();
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("player_character", EntityReference.Player(heroId))
            .AddRole("protagonist", "player_character")  // "protagonist" → "player_character"
            .Build();

        // Act
        var result = await _resolver.ResolveAsync("protagonist", bindings);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(heroId, result.EntityId);
    }

    [Fact]
    public async Task ResolveAsync_RoleToNonexistentBinding_ReturnsNull()
    {
        // Arrange
        var bindings = CutsceneBindings.Builder()
            .AddRole("protagonist", "missing_binding")
            .Build();

        // Act
        var result = await _resolver.ResolveAsync("protagonist", bindings);

        // Assert
        Assert.Null(result);
    }

    // =========================================================================
    // CONTEXT RESOLUTION TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_SelfBinding_ResolvesFromContext()
    {
        // Arrange
        var selfId = Guid.NewGuid();
        var bindings = CutsceneBindings.Empty;
        var context = new EntityResolutionContext
        {
            RequestingEntity = selfId
        };

        // Act
        var result = await _resolver.ResolveAsync("self", bindings, context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(selfId, result.EntityId);
    }

    [Fact]
    public async Task ResolveAsync_TargetBinding_ResolvesFromContextVariables()
    {
        // Arrange
        var targetId = Guid.NewGuid();
        var bindings = CutsceneBindings.Empty;
        var context = new EntityResolutionContext
        {
            Variables = new Dictionary<string, object?> { ["target"] = targetId }
        };

        // Act
        var result = await _resolver.ResolveAsync("target", bindings, context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(targetId, result.EntityId);
    }

    [Fact]
    public async Task ResolveAsync_PlayerBinding_ResolvesFromContextVariables()
    {
        // Arrange
        var playerId = Guid.NewGuid();
        var bindings = CutsceneBindings.Empty;
        var context = new EntityResolutionContext
        {
            Variables = new Dictionary<string, object?> { ["player_id"] = playerId.ToString() }
        };

        // Act
        var result = await _resolver.ResolveAsync("player", bindings, context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(playerId, result.EntityId);
    }

    [Fact]
    public async Task ResolveAsync_ContextWithEntityReferenceObject_Resolves()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var entityRef = EntityReference.Npc(entityId, "humanoid", "enemy");
        var bindings = CutsceneBindings.Empty;
        var context = new EntityResolutionContext
        {
            Variables = new Dictionary<string, object?> { ["target"] = entityRef }
        };

        // Act
        var result = await _resolver.ResolveAsync("target", bindings, context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(entityId, result.EntityId);
    }

    [Fact]
    public async Task ResolveAsync_ContextWithDictionary_ExtractsId()
    {
        // Arrange
        var entityId = Guid.NewGuid();
        var entityDict = new Dictionary<string, object?>
        {
            ["id"] = entityId,
            ["name"] = "Test Entity"
        };
        var bindings = CutsceneBindings.Empty;
        var context = new EntityResolutionContext
        {
            Variables = new Dictionary<string, object?> { ["target"] = entityDict }
        };

        // Act
        var result = await _resolver.ResolveAsync("target", bindings, context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(entityId, result.EntityId);
    }

    // =========================================================================
    // CUSTOM BINDINGS TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_CustomBinding_EntityReference_Resolves()
    {
        // Arrange
        var customId = Guid.NewGuid();
        var bindings = new CutsceneBindings
        {
            Custom = new Dictionary<string, object>
            {
                ["special_target"] = EntityReference.FromId(customId)
            }
        };

        // Act
        var result = await _resolver.ResolveAsync("special_target", bindings);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(customId, result.EntityId);
    }

    [Fact]
    public async Task ResolveAsync_CustomBinding_GuidValue_Resolves()
    {
        // Arrange
        var customId = Guid.NewGuid();
        var bindings = new CutsceneBindings
        {
            Custom = new Dictionary<string, object>
            {
                ["special_target"] = customId
            }
        };

        // Act
        var result = await _resolver.ResolveAsync("special_target", bindings);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(customId, result.EntityId);
    }

    [Fact]
    public async Task ResolveAsync_CustomBinding_GuidString_Resolves()
    {
        // Arrange
        var customId = Guid.NewGuid();
        var bindings = new CutsceneBindings
        {
            Custom = new Dictionary<string, object>
            {
                ["special_target"] = customId.ToString()
            }
        };

        // Act
        var result = await _resolver.ResolveAsync("special_target", bindings);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(customId, result.EntityId);
    }

    // =========================================================================
    // RESOLUTION PRIORITY TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_ParticipantTakesPriorityOverProp()
    {
        // Arrange
        var participantId = Guid.NewGuid();
        var propId = Guid.NewGuid();
        var bindings = new CutsceneBindings
        {
            Participants = new Dictionary<string, EntityReference>
            {
                ["target"] = EntityReference.Player(participantId)
            },
            Props = new Dictionary<string, EntityReference>
            {
                ["target"] = EntityReference.Prop(propId)
            }
        };

        // Act
        var result = await _resolver.ResolveAsync("target", bindings);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(participantId, result.EntityId);
        Assert.True(result.IsPlayer);
    }

    [Fact]
    public async Task ResolveAsync_ExplicitBindingTakesPriorityOverRole()
    {
        // Arrange
        var directId = Guid.NewGuid();
        var roleTargetId = Guid.NewGuid();
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("hero", EntityReference.Player(directId))
            .AddParticipant("player_char", EntityReference.Player(roleTargetId))
            .AddRole("hero", "player_char")  // This should be ignored since "hero" is directly bound
            .Build();

        // Act
        var result = await _resolver.ResolveAsync("hero", bindings);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(directId, result.EntityId);
    }

    // =========================================================================
    // NEGATIVE TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_UnknownBinding_ReturnsNull()
    {
        // Arrange
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("hero", Guid.NewGuid())
            .Build();

        // Act
        var result = await _resolver.ResolveAsync("unknown_character", bindings);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_EmptyBindings_ReturnsNull()
    {
        // Arrange
        var bindings = CutsceneBindings.Empty;

        // Act
        var result = await _resolver.ResolveAsync("hero", bindings);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_SelfWithNoContext_ReturnsNull()
    {
        // Arrange
        var bindings = CutsceneBindings.Empty;

        // Act
        var result = await _resolver.ResolveAsync("self", bindings);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_SelfWithEmptyGuid_ReturnsNull()
    {
        // Arrange
        var bindings = CutsceneBindings.Empty;
        var context = new EntityResolutionContext
        {
            RequestingEntity = Guid.Empty
        };

        // Act
        var result = await _resolver.ResolveAsync("self", bindings, context);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_EmptyBindingName_Throws()
    {
        // Arrange
        var bindings = CutsceneBindings.Empty;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _resolver.ResolveAsync("", bindings));
    }

    // =========================================================================
    // RESOLVE MANY TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveManyAsync_MultipleBindings_ResolvesAll()
    {
        // Arrange
        var heroId = Guid.NewGuid();
        var villainId = Guid.NewGuid();
        var doorId = Guid.NewGuid();

        var bindings = CutsceneBindings.Builder()
            .AddParticipant("hero", EntityReference.Player(heroId))
            .AddParticipant("villain", EntityReference.Npc(villainId, "humanoid"))
            .AddProp("door", doorId)
            .Build();

        // Act
        var results = await _resolver.ResolveManyAsync(
            new[] { "hero", "villain", "door" },
            bindings);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal(heroId, results["hero"].EntityId);
        Assert.Equal(villainId, results["villain"].EntityId);
        Assert.Equal(doorId, results["door"].EntityId);
    }

    [Fact]
    public async Task ResolveManyAsync_PartialResolution_ReturnsOnlyResolved()
    {
        // Arrange
        var heroId = Guid.NewGuid();
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("hero", EntityReference.Player(heroId))
            .Build();

        // Act
        var results = await _resolver.ResolveManyAsync(
            new[] { "hero", "missing1", "missing2" },
            bindings);

        // Assert
        Assert.Single(results);
        Assert.True(results.ContainsKey("hero"));
        Assert.False(results.ContainsKey("missing1"));
        Assert.False(results.ContainsKey("missing2"));
    }

    [Fact]
    public async Task ResolveManyAsync_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("hero", Guid.NewGuid())
            .Build();

        // Act
        var results = await _resolver.ResolveManyAsync(
            Array.Empty<string>(),
            bindings);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ResolveManyAsync_SkipsNullAndEmpty()
    {
        // Arrange
        var heroId = Guid.NewGuid();
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("hero", EntityReference.Player(heroId))
            .Build();

        // Act
        var results = await _resolver.ResolveManyAsync(
            new[] { "hero", null!, "", "  " },
            bindings);

        // Assert
        Assert.Single(results);
        Assert.True(results.ContainsKey("hero"));
    }

    // =========================================================================
    // CAN RESOLVE TESTS
    // =========================================================================

    [Fact]
    public void CanResolve_ExistingParticipant_ReturnsTrue()
    {
        // Arrange
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("hero", Guid.NewGuid())
            .Build();

        // Act & Assert
        Assert.True(_resolver.CanResolve("hero", bindings));
    }

    [Fact]
    public void CanResolve_ExistingProp_ReturnsTrue()
    {
        // Arrange
        var bindings = CutsceneBindings.Builder()
            .AddProp("door", Guid.NewGuid())
            .Build();

        // Act & Assert
        Assert.True(_resolver.CanResolve("door", bindings));
    }

    [Fact]
    public void CanResolve_ValidRole_ReturnsTrue()
    {
        // Arrange
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("player_char", Guid.NewGuid())
            .AddRole("protagonist", "player_char")
            .Build();

        // Act & Assert
        Assert.True(_resolver.CanResolve("protagonist", bindings));
    }

    [Fact]
    public void CanResolve_ContextBinding_ReturnsTrue()
    {
        // Arrange
        var bindings = CutsceneBindings.Empty;

        // Act & Assert - context bindings are always potentially resolvable
        Assert.True(_resolver.CanResolve("self", bindings));
        Assert.True(_resolver.CanResolve("target", bindings));
        Assert.True(_resolver.CanResolve("player", bindings));
    }

    [Fact]
    public void CanResolve_UnknownBinding_ReturnsFalse()
    {
        // Arrange
        var bindings = CutsceneBindings.Empty;

        // Act & Assert
        Assert.False(_resolver.CanResolve("unknown", bindings));
    }

    [Fact]
    public void CanResolve_EmptyName_ReturnsFalse()
    {
        // Arrange
        var bindings = CutsceneBindings.Empty;

        // Act & Assert
        Assert.False(_resolver.CanResolve("", bindings));
        Assert.False(_resolver.CanResolve("  ", bindings));
    }

    // =========================================================================
    // BINDINGS BUILDER TESTS
    // =========================================================================

    [Fact]
    public void CutsceneBindingsBuilder_BuildsCorrectly()
    {
        // Arrange & Act
        var bindings = CutsceneBindings.Builder()
            .AddParticipant("hero", Guid.NewGuid())
            .AddParticipant("villain", Guid.NewGuid())
            .AddProp("door", Guid.NewGuid())
            .AddLocation("center", new Vector3(0, 0, 0))
            .AddRole("protagonist", "hero")
            .AddCustom("difficulty", "hard")
            .Build();

        // Assert
        Assert.Equal(2, bindings.Participants.Count);
        Assert.Single(bindings.Props);
        Assert.Single(bindings.Locations);
        Assert.Single(bindings.Roles);
        Assert.Single(bindings.Custom);
    }

    [Fact]
    public void CutsceneBindings_FromParticipants_Works()
    {
        // Arrange
        var heroId = Guid.NewGuid();
        var villainId = Guid.NewGuid();
        var participants = new Dictionary<string, EntityReference>
        {
            ["hero"] = EntityReference.Player(heroId),
            ["villain"] = EntityReference.Npc(villainId, "humanoid")
        };

        // Act
        var bindings = CutsceneBindings.FromParticipants(participants);

        // Assert
        Assert.Equal(2, bindings.Participants.Count);
        Assert.Empty(bindings.Props);
        Assert.Empty(bindings.Locations);
    }

    // =========================================================================
    // ENTITY REFERENCE FACTORY TESTS
    // =========================================================================

    [Fact]
    public void EntityReference_FromId_CreatesMinimalReference()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var reference = EntityReference.FromId(id);

        // Assert
        Assert.Equal(id, reference.EntityId);
        Assert.Null(reference.ArchetypeId);
        Assert.Null(reference.Role);
        Assert.False(reference.IsPlayer);
        Assert.False(reference.IsProp);
    }

    [Fact]
    public void EntityReference_Player_SetsIsPlayer()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var reference = EntityReference.Player(id, "humanoid");

        // Assert
        Assert.Equal(id, reference.EntityId);
        Assert.Equal("humanoid", reference.ArchetypeId);
        Assert.True(reference.IsPlayer);
        Assert.False(reference.IsProp);
    }

    [Fact]
    public void EntityReference_Npc_SetsArchetypeAndRole()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var reference = EntityReference.Npc(id, "humanoid", "guard");

        // Assert
        Assert.Equal(id, reference.EntityId);
        Assert.Equal("humanoid", reference.ArchetypeId);
        Assert.Equal("guard", reference.Role);
        Assert.False(reference.IsPlayer);
        Assert.False(reference.IsProp);
    }

    [Fact]
    public void EntityReference_Prop_SetsIsProp()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var reference = EntityReference.Prop(id, "door");

        // Assert
        Assert.Equal(id, reference.EntityId);
        Assert.Equal("door", reference.ArchetypeId);
        Assert.False(reference.IsPlayer);
        Assert.True(reference.IsProp);
    }
}
