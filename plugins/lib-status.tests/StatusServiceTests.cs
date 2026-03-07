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

    // TODO: Add service-specific tests based on schema operations
    // Schema file: ../schemas/status-api.yaml
    //
    // Guidelines:
    // - Use the Capture Pattern for event/state verification (see TESTING_PATTERNS.md)
    // - Verify side effects (saves, events, indices), not just response structure
    // - Keep Arrange < 50% of test code; extract helpers if needed
}
