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
    /// Tests that GetServiceGuid returns the correct GUID for exact method:path key.
    /// </summary>
    [Fact]
    public void GetServiceGuid_ExactMatch_ReturnsCorrectGuid()
    {
        // Arrange
        using var client = new BannouClient();
        var expectedGuid = Guid.NewGuid();
        var mappings = new Dictionary<string, Guid>
        {
            { "POST:/account/get", expectedGuid }
        };
        PopulateApiMappings(client, mappings);

        // Act
        var result = client.GetServiceGuid("POST", "/account/get");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedGuid, result.Value);
    }

    /// <summary>
    /// Tests that GetServiceGuid returns null for non-existent endpoints.
    /// </summary>
    [Fact]
    public void GetServiceGuid_NonExistentEndpoint_ReturnsNull()
    {
        // Arrange
        using var client = new BannouClient();
        var mappings = new Dictionary<string, Guid>
        {
            { "POST:/account/get", Guid.NewGuid() }
        };
        PopulateApiMappings(client, mappings);

        // Act
        var result = client.GetServiceGuid("POST", "/nonexistent/endpoint");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// CRITICAL: Tests that /species/get does NOT match /species/get-by-code.
    /// This was the bug that caused endpoint prefix collision.
    /// </summary>
    [Fact]
    public void GetServiceGuid_PrefixCollision_ReturnsCorrectGuid()
    {
        // Arrange
        using var client = new BannouClient();
        var getGuid = Guid.NewGuid();
        var getByCodeGuid = Guid.NewGuid();
        var mappings = new Dictionary<string, Guid>
        {
            { "POST:/species/get", getGuid },
            { "POST:/species/get-by-code", getByCodeGuid }
        };
        PopulateApiMappings(client, mappings);

        // Act
        var getResult = client.GetServiceGuid("POST", "/species/get");
        var getByCodeResult = client.GetServiceGuid("POST", "/species/get-by-code");

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
    public void GetServiceGuid_MultipleSimilarEndpoints_NoCollision()
    {
        // Arrange
        using var client = new BannouClient();
        var endpoints = new Dictionary<string, Guid>
        {
            { "POST:/location/get", Guid.NewGuid() },
            { "POST:/location/get-by-code", Guid.NewGuid() },
            { "POST:/realm/get", Guid.NewGuid() },
            { "POST:/realm/get-by-code", Guid.NewGuid() },
            { "POST:/character/get", Guid.NewGuid() },
            { "POST:/character/get-by-name", Guid.NewGuid() },
            { "POST:/subscriptions/get", Guid.NewGuid() },
            { "POST:/subscriptions/get-current", Guid.NewGuid() }
        };
        PopulateApiMappings(client, endpoints);

        // Act & Assert - Each endpoint should return its own unique GUID
        foreach (var endpoint in endpoints)
        {
            var parts = endpoint.Key.Split(':', 2);
            var method = parts[0];
            var path = parts[1];

            var result = client.GetServiceGuid(method, path);

            Assert.NotNull(result);
            Assert.Equal(endpoint.Value, result.Value);
        }
    }

    /// <summary>
    /// Tests case sensitivity of method matching.
    /// </summary>
    [Fact]
    public void GetServiceGuid_MethodCaseSensitive_ReturnsNull()
    {
        // Arrange
        using var client = new BannouClient();
        var mappings = new Dictionary<string, Guid>
        {
            { "POST:/account/get", Guid.NewGuid() }
        };
        PopulateApiMappings(client, mappings);

        // Act - Try with lowercase method (should not match)
        var result = client.GetServiceGuid("post", "/account/get");

        // Assert - Case mismatch should not find the endpoint
        Assert.Null(result);
    }

    /// <summary>
    /// Tests different HTTP methods for the same path.
    /// </summary>
    [Fact]
    public void GetServiceGuid_DifferentMethods_ReturnsCorrectGuid()
    {
        // Arrange
        using var client = new BannouClient();
        var postGuid = Guid.NewGuid();
        var getGuid = Guid.NewGuid();
        var mappings = new Dictionary<string, Guid>
        {
            { "POST:/account/profile", postGuid },
            { "GET:/account/profile", getGuid }
        };
        PopulateApiMappings(client, mappings);

        // Act
        var postResult = client.GetServiceGuid("POST", "/account/profile");
        var getResult = client.GetServiceGuid("GET", "/account/profile");

        // Assert
        Assert.NotNull(postResult);
        Assert.NotNull(getResult);
        Assert.Equal(postGuid, postResult.Value);
        Assert.Equal(getGuid, getResult.Value);
        Assert.NotEqual(postResult.Value, getResult.Value);
    }

    /// <summary>
    /// Tests that empty mappings return null.
    /// </summary>
    [Fact]
    public void GetServiceGuid_EmptyMappings_ReturnsNull()
    {
        // Arrange
        using var client = new BannouClient();
        // Don't populate any mappings

        // Act
        var result = client.GetServiceGuid("POST", "/any/endpoint");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Tests partial path matching (should NOT match).
    /// </summary>
    [Fact]
    public void GetServiceGuid_PartialPath_ReturnsNull()
    {
        // Arrange
        using var client = new BannouClient();
        var mappings = new Dictionary<string, Guid>
        {
            { "POST:/species/get-by-code", Guid.NewGuid() }
        };
        PopulateApiMappings(client, mappings);

        // Act - Try to match a prefix of the full path
        var result = client.GetServiceGuid("POST", "/species/get");

        // Assert - Partial match should not find the endpoint
        Assert.Null(result);
    }
}
