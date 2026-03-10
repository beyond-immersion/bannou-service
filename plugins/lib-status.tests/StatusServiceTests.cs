using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Status;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Status.Tests;

public class StatusServiceTests
{
    #region Constructor Validation
    #endregion

    #region Configuration Tests

    [Fact]
    public void StatusServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new StatusServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    #endregion

    #region Enum Boundary Mapping Validation

    /// <summary>
    /// Drift test for EntityType -> ContainerOwnerType mapping via MapByNameOrDefault.
    /// Verifies the exact set of name-matched values between the two enums is stable.
    /// If either enum gains or loses a value that changes the intersection, this test
    /// fails — forcing review of whether the new mapping behavior is intentional.
    /// </summary>
    [Fact]
    public void EntityType_To_ContainerOwnerType_NameIntersection_IsStable()
    {
        // The known set of values that exist by name in BOTH EntityType and ContainerOwnerType.
        // These map by name via MapByNameOrDefault; all other EntityType values fall back to Other.
        var expectedNameMatches = new HashSet<string> { "Character", "Account", "Location", "Guild", "Other" };

        var containerNames = new HashSet<string>(Enum.GetNames<ContainerOwnerType>());
        var actualNameMatches = Enum.GetNames<EntityType>()
            .Where(name => containerNames.Contains(name))
            .ToHashSet();

        Assert.Equal(expectedNameMatches, actualNameMatches);
    }

    /// <summary>
    /// Validates that the MapToContainerOwnerType mapping produces correct results
    /// for all EntityType values: name-matched values map directly, others fall back to Other.
    /// </summary>
    [Fact]
    public void EntityType_To_ContainerOwnerType_MappingIsCorrect()
    {
        // Name-matched values
        Assert.Equal(ContainerOwnerType.Character, StatusService.MapToContainerOwnerType(EntityType.Character));
        Assert.Equal(ContainerOwnerType.Account, StatusService.MapToContainerOwnerType(EntityType.Account));
        Assert.Equal(ContainerOwnerType.Location, StatusService.MapToContainerOwnerType(EntityType.Location));
        Assert.Equal(ContainerOwnerType.Guild, StatusService.MapToContainerOwnerType(EntityType.Guild));
        Assert.Equal(ContainerOwnerType.Other, StatusService.MapToContainerOwnerType(EntityType.Other));

        // Non-matched values fall back to Other
        Assert.Equal(ContainerOwnerType.Other, StatusService.MapToContainerOwnerType(EntityType.System));
        Assert.Equal(ContainerOwnerType.Other, StatusService.MapToContainerOwnerType(EntityType.Actor));
        Assert.Equal(ContainerOwnerType.Other, StatusService.MapToContainerOwnerType(EntityType.Deity));
        Assert.Equal(ContainerOwnerType.Other, StatusService.MapToContainerOwnerType(EntityType.Dungeon));

        // Every EntityType value maps without throwing
        EnumMappingValidator.AssertSwitchCoversAllValues<EntityType>(
            e => StatusService.MapToContainerOwnerType(e));
    }

    #endregion

    #region Key Builder Tests

    [Fact]
    public void BuildTemplateIdKey_FormatsCorrectly()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Assert.Equal("tpl:11111111-1111-1111-1111-111111111111", StatusService.BuildTemplateIdKey(id));
    }

    [Fact]
    public void BuildTemplateCodeKey_FormatsCorrectly()
    {
        var gameServiceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Assert.Equal("tpl:22222222-2222-2222-2222-222222222222:POISON", StatusService.BuildTemplateCodeKey(gameServiceId, "POISON"));
    }

    [Fact]
    public void BuildInstanceIdKey_FormatsCorrectly()
    {
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");
        Assert.Equal("inst:33333333-3333-3333-3333-333333333333", StatusService.BuildInstanceIdKey(id));
    }

    [Fact]
    public void BuildContainerIdKey_FormatsCorrectly()
    {
        var id = Guid.Parse("44444444-4444-4444-4444-444444444444");
        Assert.Equal("ctr:44444444-4444-4444-4444-444444444444", StatusService.BuildContainerIdKey(id));
    }

    [Fact]
    public void BuildContainerEntityKey_FormatsCorrectly()
    {
        var entityId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var gameServiceId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        Assert.Equal(
            "ctr:55555555-5555-5555-5555-555555555555:Character:66666666-6666-6666-6666-666666666666",
            StatusService.BuildContainerEntityKey(entityId, EntityType.Character, gameServiceId));
    }

    [Fact]
    public void BuildActiveCacheKey_FormatsCorrectly()
    {
        var entityId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        Assert.Equal(
            "active:77777777-7777-7777-7777-777777777777:Account",
            StatusService.BuildActiveCacheKey(entityId, EntityType.Account));
    }

    [Fact]
    public void BuildSeedEffectsCacheKey_FormatsCorrectly()
    {
        var entityId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        Assert.Equal(
            "seed:88888888-8888-8888-8888-888888888888:Location",
            StatusService.BuildSeedEffectsCacheKey(entityId, EntityType.Location));
    }

    [Fact]
    public void BuildEntityLockKey_FormatsCorrectly()
    {
        var entityId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        Assert.Equal(
            "entity:Guild:99999999-9999-9999-9999-999999999999",
            StatusService.BuildEntityLockKey(EntityType.Guild, entityId));
    }

    #endregion

    // Note: GrantStatusAsync, RemoveStatusAsync, and other orchestration methods
    // involve 13 constructor dependencies and 8 state stores. Per TESTING-PATTERNS.md
    // decision tree: "If Arrange exceeds 50%, question if this should be a unit test."
    // These are covered by HTTP integration tests (tools/http-tester).
}
