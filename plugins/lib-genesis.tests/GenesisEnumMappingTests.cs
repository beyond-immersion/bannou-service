using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.TestUtilities;
using Xunit;

namespace BeyondImmersion.BannouService.Genesis.Tests;

/// <summary>
/// Validates enum boundary mappings used by GenesisService.
/// </summary>
/// <remarks>
/// Required by structural test <c>PluginsUsingEnumMapping_MustHaveEnumMappingValidatorTests</c>:
/// every plugin that calls <c>EnumMapping.MapByName</c>, <c>MapByNameOrDefault</c>, or
/// <c>TryMapByName</c> MUST have corresponding <c>EnumMappingValidator</c> tests so enum
/// value drift is caught at test time rather than at runtime.
/// </remarks>
public class GenesisEnumMappingTests
{
    /// <summary>
    /// Genesis templates declare <c>constraintModel</c> as an opaque string that is passed
    /// through to Inventory's <c>ContainerConstraintModel</c> enum at container creation
    /// (GenesisService.cs <c>CreateEntityAsync</c> and <c>RestoreFromArchiveAsync</c>).
    /// The mapping uses <see cref="EnumMapping.MapByName{TTarget}(string)"/>; this test
    /// verifies every legal template string resolves to a valid enum value and every
    /// enum value has a corresponding template string.
    /// </summary>
    [Fact]
    public void ContainerConstraintModel_TemplateStringVocabulary_CoversAllValues()
    {
        // Values listed in genesis-api.yaml's constraintModel description and defined in
        // inventory-api.yaml's ContainerConstraintModel enum. Adding a new enum value
        // requires updating this list AND the genesis schema description.
        EnumMappingValidator.AssertStringToEnumCoverage<ContainerConstraintModel>(
            "SlotOnly", "WeightOnly", "SlotAndWeight", "Grid", "Volumetric", "Unlimited");
    }
}
