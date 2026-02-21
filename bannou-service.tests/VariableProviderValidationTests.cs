using BeyondImmersion.BannouService.Services;
using Xunit;

namespace BeyondImmersion.BannouService.Tests;

/// <summary>
/// Validates that VariableProviderDefinitions (generated from schemas/variable-providers.yaml)
/// is well-formed and complete. Runtime validation of actual factory registrations against
/// these definitions is handled by PluginLoader.ValidateVariableProviders at startup.
/// </summary>
public class VariableProviderValidationTests
{
    /// <summary>
    /// Verifies at least one provider is defined (guards against empty generation).
    /// </summary>
    [Fact]
    public void VariableProviderDefinitions_IsNotEmpty()
    {
        Assert.NotEmpty(VariableProviderDefinitions.Metadata);
    }

    /// <summary>
    /// Verifies all constants are present as keys in the Metadata dictionary.
    /// This catches generation bugs where a constant is defined but not added to Metadata.
    /// </summary>
    [Theory]
    [InlineData(VariableProviderDefinitions.Personality)]
    [InlineData(VariableProviderDefinitions.Combat)]
    [InlineData(VariableProviderDefinitions.Encounters)]
    [InlineData(VariableProviderDefinitions.Backstory)]
    [InlineData(VariableProviderDefinitions.Quest)]
    [InlineData(VariableProviderDefinitions.Seed)]
    [InlineData(VariableProviderDefinitions.Obligations)]
    [InlineData(VariableProviderDefinitions.Faction)]
    [InlineData(VariableProviderDefinitions.Location)]
    public void VariableProviderDefinitions_ConstantExistsInMetadata(string providerName)
    {
        Assert.Contains(providerName, VariableProviderDefinitions.Metadata.Keys);
    }

    /// <summary>
    /// Verifies no duplicate constants exist (all values are unique).
    /// </summary>
    [Fact]
    public void VariableProviderDefinitions_NoDuplicateProviderNames()
    {
        var names = new[]
        {
            VariableProviderDefinitions.Personality,
            VariableProviderDefinitions.Combat,
            VariableProviderDefinitions.Encounters,
            VariableProviderDefinitions.Backstory,
            VariableProviderDefinitions.Quest,
            VariableProviderDefinitions.Seed,
            VariableProviderDefinitions.Obligations,
            VariableProviderDefinitions.Faction,
            VariableProviderDefinitions.Location,
        };

        Assert.Equal(names.Length, names.Distinct().Count());
    }

    /// <summary>
    /// Verifies every metadata entry has a non-empty service name and purpose.
    /// </summary>
    [Fact]
    public void VariableProviderDefinitions_AllMetadataHasServiceAndPurpose()
    {
        foreach (var (name, metadata) in VariableProviderDefinitions.Metadata)
        {
            Assert.False(string.IsNullOrWhiteSpace(metadata.Service),
                $"Provider \"{name}\" has empty Service in metadata");
            Assert.False(string.IsNullOrWhiteSpace(metadata.Purpose),
                $"Provider \"{name}\" has empty Purpose in metadata");
        }
    }
}
