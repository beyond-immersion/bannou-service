using BeyondImmersion.Bannou.Client;
using System.Collections.Concurrent;
using System.Reflection;
using Xunit;

namespace BeyondImmersion.Bannou.Server.Tests;

/// <summary>
/// Unit tests for BannouClient's GetServiceGuid method.
/// These tests verify correct endpoint GUID lookup and prevent regression
/// of the prefix collision bug (e.g., /species/get vs /species/get-by-code).
/// </summary>
public class BannouClientGuidLookupTests
{
    /// <summary>
    /// Helper to populate _apiMappings for testing via reflection.
    /// </summary>
    private static void PopulateApiMappings(BannouClient client, Dictionary<string, Guid> mappings)
    {
        var field = typeof(BannouClient).GetField("_apiMappings", BindingFlags.NonPublic | BindingFlags.Instance);
        var apiMappings = field?.GetValue(client) as ConcurrentDictionary<string, Guid>;
        if (apiMappings != null)
        {
            foreach (var mapping in mappings)
            {
                apiMappings[mapping.Key] = mapping.Value;
            }
        }
    }

    /// <summary>
    /// Tests that GetServiceGuid returns the correct GUID for exact endpoint path.
    /// </summary>
    [Fact]
    public async Task GetServiceGuid_ExactMatch_ReturnsCorrectGuid()
    {
        // Arrange
        await using var client = new BannouClient();
        var expectedGuid = Guid.NewGuid();
        var mappings = new Dictionary<string, Guid>
        {
            { "/account/get", expectedGuid }
        };
        PopulateApiMappings(client, mappings);

        // Act
        var result = client.GetServiceGuid("/account/get");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedGuid, result.Value);
    }

    /// <summary>
    /// Tests that GetServiceGuid returns null for non-existent endpoints.
    /// </summary>
    [Fact]
    public async Task GetServiceGuid_NonExistentEndpoint_ReturnsNull()
    {
        // Arrange
        await using var client = new BannouClient();
        var mappings = new Dictionary<string, Guid>
        {
            { "/account/get", Guid.NewGuid() }
        };
        PopulateApiMappings(client, mappings);

        // Act
        var result = client.GetServiceGuid("/nonexistent/endpoint");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// CRITICAL: Tests that /species/get does NOT match /species/get-by-code.
    /// This was the bug that caused endpoint prefix collision.
    /// </summary>
    [Fact]
    public async Task GetServiceGuid_PrefixCollision_ReturnsCorrectGuid()
    {
        // Arrange
        await using var client = new BannouClient();
        var getGuid = Guid.NewGuid();
        var getByCodeGuid = Guid.NewGuid();
        var mappings = new Dictionary<string, Guid>
        {
            { "/species/get", getGuid },
            { "/species/get-by-code", getByCodeGuid }
        };
        PopulateApiMappings(client, mappings);

        // Act
        var getResult = client.GetServiceGuid("/species/get");
        var getByCodeResult = client.GetServiceGuid("/species/get-by-code");

        // Assert - Each endpoint should return its own GUID, not the other's
        Assert.NotNull(getResult);
        Assert.NotNull(getByCodeResult);
        Assert.Equal(getGuid, getResult.Value);
        Assert.Equal(getByCodeGuid, getByCodeResult.Value);
        Assert.NotEqual(getResult.Value, getByCodeResult.Value);
    }

    /// <summary>
    /// Tests multiple similar endpoint patterns that could cause collision.
    /// </summary>
    [Fact]
    public async Task GetServiceGuid_MultipleSimilarEndpoints_NoCollision()
    {
        // Arrange
        await using var client = new BannouClient();
        var endpoints = new Dictionary<string, Guid>
        {
            { "/location/get", Guid.NewGuid() },
            { "/location/get-by-code", Guid.NewGuid() },
            { "/realm/get", Guid.NewGuid() },
            { "/realm/get-by-code", Guid.NewGuid() },
            { "/character/get", Guid.NewGuid() },
            { "/character/get-by-name", Guid.NewGuid() },
            { "/subscriptions/get", Guid.NewGuid() },
            { "/subscriptions/get-current", Guid.NewGuid() }
        };
        PopulateApiMappings(client, endpoints);

        // Act & Assert - Each endpoint should return its own unique GUID
        foreach (var endpoint in endpoints)
        {
            var result = client.GetServiceGuid(endpoint.Key);

            Assert.NotNull(result);
            Assert.Equal(endpoint.Value, result.Value);
        }
    }

    /// <summary>
    /// Tests that empty mappings return null.
    /// </summary>
    [Fact]
    public async Task GetServiceGuid_EmptyMappings_ReturnsNull()
    {
        // Arrange
        await using var client = new BannouClient();
        // Don't populate any mappings

        // Act
        var result = client.GetServiceGuid("/any/endpoint");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Tests partial path matching (should NOT match).
    /// </summary>
    [Fact]
    public async Task GetServiceGuid_PartialPath_ReturnsNull()
    {
        // Arrange
        await using var client = new BannouClient();
        var mappings = new Dictionary<string, Guid>
        {
            { "/species/get-by-code", Guid.NewGuid() }
        };
        PopulateApiMappings(client, mappings);

        // Act - Try to match a prefix of the full path
        var result = client.GetServiceGuid("/species/get");

        // Assert - Partial match should not find the endpoint
        Assert.Null(result);
    }
}
