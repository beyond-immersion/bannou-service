using System.Text.Json;
using System.Text.Json.Nodes;
using BeyondImmersion.Bannou.Client;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// Base class for WebSocket test handlers providing common utilities.
/// Consolidates repeated patterns for realm creation, test execution, and JSON parsing.
/// </summary>
public abstract class BaseWebSocketTestHandler : IServiceTestHandler
{
    /// <summary>
    /// Get all service tests provided by this handler.
    /// </summary>
    public abstract ServiceTest[] GetServiceTests();

    #region Admin Client Access

    /// <summary>
    /// Gets the admin client, logging an error if not available.
    /// </summary>
    protected static BannouClient? GetAdminClient()
    {
        var adminClient = Program.AdminClient;
        if (adminClient == null || !adminClient.IsConnected)
        {
            Console.WriteLine("❌ Admin client not connected - ensure admin login completed successfully");
            return null;
        }
        return adminClient;
    }

    #endregion

    #region Test Execution Wrapper

    /// <summary>
    /// Executes a WebSocket test with standardized try-catch handling and result logging.
    /// </summary>
    /// <param name="testName">Display name for the test</param>
    /// <param name="testAction">Async test action returning success/failure</param>
    protected static void RunWebSocketTest(string testName, Func<BannouClient, Task<bool>> testAction)
    {
        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = GetAdminClient();
                if (adminClient == null)
                    return false;

                return await testAction(adminClient);
            }).Result;

            if (result)
            {
                Console.WriteLine($"✅ {testName} PASSED");
            }
            else
            {
                Console.WriteLine($"❌ {testName} FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ {testName} FAILED with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    #endregion

    #region Resource Creation Helpers

    /// <summary>
    /// Creates a test realm via WebSocket.
    /// </summary>
    /// <param name="adminClient">The admin client to use</param>
    /// <param name="codePrefix">Prefix for the realm code (e.g., "CHAR", "LOC")</param>
    /// <param name="description">Human-readable description (e.g., "Character", "Location")</param>
    /// <param name="uniqueCode">Unique suffix for this test run</param>
    /// <returns>The realm ID if successful, null otherwise</returns>
    protected static async Task<string?> CreateTestRealmAsync(
        BannouClient adminClient,
        string codePrefix,
        string description,
        string uniqueCode)
    {
        try
        {
            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/realm/create",
                new
                {
                    code = $"{codePrefix}_REALM_{uniqueCode}",
                    name = $"{description} Test Realm {uniqueCode}",
                    description = $"Test realm for {description.ToLowerInvariant()} tests",
                    category = "test"
                },
                timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

            var realmId = GetStringProperty(response, "realmId");
            if (string.IsNullOrEmpty(realmId))
            {
                Console.WriteLine("   Failed to create test realm - no realmId in response");
                return null;
            }

            Console.WriteLine($"   Created test realm: {realmId}");
            return realmId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test realm: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a test species via WebSocket and associates it with the given realm.
    /// </summary>
    /// <param name="adminClient">The admin client to use</param>
    /// <param name="codePrefix">Prefix for the species code (e.g., "CHAR")</param>
    /// <param name="description">Human-readable description (e.g., "Character")</param>
    /// <param name="uniqueCode">Unique suffix for this test run</param>
    /// <param name="realmId">The realm ID to associate the species with</param>
    /// <returns>The species ID if successful, null otherwise</returns>
    protected static async Task<string?> CreateTestSpeciesAsync(
        BannouClient adminClient,
        string codePrefix,
        string description,
        string uniqueCode,
        string realmId)
    {
        try
        {
            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/species/create",
                new
                {
                    code = $"{codePrefix}_SPECIES_{uniqueCode}",
                    name = $"{description} Test Species {uniqueCode}",
                    description = $"Test species for {description.ToLowerInvariant()} tests",
                    category = "test",
                    realmIds = new[] { realmId }
                },
                timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

            var speciesId = GetStringProperty(response, "speciesId");
            if (string.IsNullOrEmpty(speciesId))
            {
                Console.WriteLine("   Failed to create test species - no speciesId in response");
                return null;
            }

            Console.WriteLine($"   Created test species: {speciesId}");
            return speciesId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test species: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a test location via WebSocket.
    /// </summary>
    protected static async Task<string?> CreateTestLocationAsync(
        BannouClient adminClient,
        string codePrefix,
        string uniqueCode,
        string realmId,
        string locationType = "CITY",
        string? parentLocationId = null)
    {
        try
        {
            var request = new Dictionary<string, object?>
            {
                ["code"] = $"{codePrefix}_LOC_{uniqueCode}",
                ["name"] = $"Test Location {uniqueCode}",
                ["description"] = "Test location",
                ["realmId"] = realmId,
                ["locationType"] = locationType
            };

            if (parentLocationId != null)
                request["parentLocationId"] = parentLocationId;

            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/location/create",
                request,
                timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

            var locationId = GetStringProperty(response, "locationId");
            if (string.IsNullOrEmpty(locationId))
            {
                Console.WriteLine("   Failed to create test location - no locationId in response");
                return null;
            }

            Console.WriteLine($"   Created test location: {locationId}");
            return locationId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test location: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region JSON Parsing Helpers

    /// <summary>
    /// Parses a JsonElement response into a JsonObject.
    /// </summary>
    protected static JsonObject? ParseResponse(JsonElement response)
    {
        return JsonNode.Parse(response.GetRawText())?.AsObject();
    }

    /// <summary>
    /// Gets a string property from a JsonElement response.
    /// </summary>
    protected static string? GetStringProperty(JsonElement response, string propertyName)
    {
        var json = ParseResponse(response);
        return json?[propertyName]?.GetValue<string>();
    }

    /// <summary>
    /// Gets a string property from a JsonObject.
    /// </summary>
    protected static string? GetStringProperty(JsonObject? json, string propertyName)
    {
        return json?[propertyName]?.GetValue<string>();
    }

    /// <summary>
    /// Gets an int property from a JsonObject.
    /// </summary>
    protected static int GetIntProperty(JsonObject? json, string propertyName, int defaultValue = 0)
    {
        return json?[propertyName]?.GetValue<int>() ?? defaultValue;
    }

    /// <summary>
    /// Checks if a property exists and is a JsonArray.
    /// </summary>
    protected static bool HasArrayProperty(JsonObject? json, string propertyName)
    {
        return json?[propertyName] != null && json[propertyName] is JsonArray;
    }

    #endregion

    #region API Invocation Helpers

    /// <summary>
    /// Performs an API call via the admin WebSocket with standardized error handling.
    /// </summary>
    protected static async Task<JsonObject?> InvokeApiAsync(
        BannouClient adminClient,
        string path,
        object? body = null,
        TimeSpan? timeout = null)
    {
        var response = (await adminClient.InvokeAsync<object, JsonElement>(
            "POST",
            path,
            body ?? new { },
            timeout: timeout ?? TimeSpan.FromSeconds(10))).GetResultOrThrow();

        return ParseResponse(response);
    }

    /// <summary>
    /// Generates a unique code suffix based on current timestamp.
    /// </summary>
    protected static string GenerateUniqueCode()
    {
        return $"{DateTime.Now.Ticks % 100000}";
    }

    #endregion
}
