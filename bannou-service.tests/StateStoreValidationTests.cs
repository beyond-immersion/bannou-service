using BeyondImmersion.BannouService.Services;
using System.Reflection;
using Xunit;

namespace BeyondImmersion.BannouService.Tests;

/// <summary>
/// Validates that StateStoreDefinitions (generated from schemas/state-stores.yaml)
/// is well-formed and internally consistent. Catches generation bugs, missing entries,
/// and mismatches between constants, configurations, and metadata.
/// </summary>
public class StateStoreValidationTests
{
    /// <summary>
    /// All public const string fields on StateStoreDefinitions, discovered via reflection.
    /// </summary>
    private static readonly FieldInfo[] ConstantFields = typeof(StateStoreDefinitions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .ToArray();

    /// <summary>
    /// Verifies at least one store is defined (guards against empty generation).
    /// </summary>
    [Fact]
    public void StateStoreDefinitions_IsNotEmpty()
    {
        Assert.NotEmpty(StateStoreDefinitions.Metadata);
    }

    /// <summary>
    /// Verifies the number of constants matches the number of metadata entries.
    /// </summary>
    [Fact]
    public void StateStoreDefinitions_ConstantCountMatchesMetadataCount()
    {
        Assert.Equal(ConstantFields.Length, StateStoreDefinitions.Metadata.Count);
    }

    /// <summary>
    /// Verifies the number of constants matches the number of configuration entries.
    /// </summary>
    [Fact]
    public void StateStoreDefinitions_ConstantCountMatchesConfigurationCount()
    {
        Assert.Equal(ConstantFields.Length, StateStoreDefinitions.Configurations.Count);
    }

    /// <summary>
    /// Verifies every constant's value exists as a key in the Metadata dictionary.
    /// </summary>
    [Fact]
    public void StateStoreDefinitions_AllConstantsExistInMetadata()
    {
        foreach (var field in ConstantFields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.True(
                StateStoreDefinitions.Metadata.ContainsKey(value),
                $"Constant {field.Name} = \"{value}\" is not present in Metadata dictionary");
        }
    }

    /// <summary>
    /// Verifies every constant's value exists as a key in the Configurations dictionary.
    /// </summary>
    [Fact]
    public void StateStoreDefinitions_AllConstantsExistInConfigurations()
    {
        foreach (var field in ConstantFields)
        {
            var value = (string)field.GetValue(null)!;
            Assert.True(
                StateStoreDefinitions.Configurations.ContainsKey(value),
                $"Constant {field.Name} = \"{value}\" is not present in Configurations dictionary");
        }
    }

    /// <summary>
    /// Verifies no duplicate constant values exist.
    /// </summary>
    [Fact]
    public void StateStoreDefinitions_NoDuplicateStoreNames()
    {
        var values = ConstantFields
            .Select(f => (string)f.GetValue(null)!)
            .ToArray();

        var duplicates = values
            .GroupBy(v => v)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            $"Duplicate store names found: {string.Join(", ", duplicates)}");
    }

    /// <summary>
    /// Verifies every metadata entry has a non-empty service name, purpose, and backend.
    /// </summary>
    [Fact]
    public void StateStoreDefinitions_AllMetadataHasRequiredFields()
    {
        foreach (var (name, metadata) in StateStoreDefinitions.Metadata)
        {
            Assert.False(string.IsNullOrWhiteSpace(metadata.Service),
                $"Store \"{name}\" has empty Service in metadata");
            Assert.False(string.IsNullOrWhiteSpace(metadata.Purpose),
                $"Store \"{name}\" has empty Purpose in metadata");
            Assert.False(string.IsNullOrWhiteSpace(metadata.Backend),
                $"Store \"{name}\" has empty Backend in metadata");
        }
    }

    /// <summary>
    /// Verifies all backend values in metadata are valid (redis, mysql, or memory).
    /// </summary>
    [Fact]
    public void StateStoreDefinitions_AllBackendsAreValid()
    {
        var validBackends = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "redis", "mysql", "memory"
        };

        foreach (var (name, metadata) in StateStoreDefinitions.Metadata)
        {
            Assert.True(
                validBackends.Contains(metadata.Backend),
                $"Store \"{name}\" has invalid backend \"{metadata.Backend}\" " +
                $"(expected: {string.Join(", ", validBackends)})");
        }
    }
}
