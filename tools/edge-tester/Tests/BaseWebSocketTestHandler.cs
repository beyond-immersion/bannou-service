using System.Text.Json;
using System.Text.Json.Nodes;
using BeyondImmersion.Bannou.Client;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Species;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// Base class for WebSocket test handlers providing common utilities.
/// Consolidates repeated patterns for realm creation, test execution, and typed proxy access.
///
/// IMPORTANT: Edge tests should use TYPED PROXIES (e.g., adminClient.Realm.CreateRealmAsync)
/// instead of raw InvokeAsync calls wherever possible. This ensures:
/// 1. Compile-time validation of request/response types
/// 2. Correct endpoint paths (no typos like /game-service/create vs /game-service/services/create)
/// 3. Correct property names (no mismatches like 'name' vs 'displayName')
/// 4. Correct enum values (no case issues like 'CHARACTER' vs 'character')
/// 5. Testing that the typed proxies themselves work correctly
///
/// Raw InvokeAsync should ONLY be used for:
/// - Testing the raw WebSocket protocol itself (MetaEndpointTestHandler, ConnectWebSocketTestHandler)
/// - Testing SDK parity between TypeScript and C# (TypeScriptParityTestHandler)
/// - Testing error handling for malformed requests
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

    #region Typed Resource Creation Helpers

    /// <summary>
    /// Cached game service for test resource creation.
    /// </summary>
    private static ServiceInfo? _cachedGameService;

    /// <summary>
    /// Gets or creates a test game service for use in realm creation.
    /// Uses typed proxy for compile-time validation.
    /// </summary>
    /// <returns>The game service ID if successful, null otherwise</returns>
    protected static async Task<Guid?> GetOrCreateTestGameServiceAsync(BannouClient adminClient)
    {
        if (_cachedGameService != null)
            return _cachedGameService.ServiceId;

        try
        {
            var uniqueCode = GenerateUniqueCode();
            var response = await adminClient.GameService.CreateServiceAsync(new CreateServiceRequest
            {
                StubName = $"edge-test-{uniqueCode}",
                DisplayName = $"Edge Test Game Service {uniqueCode}",
                Description = "Test game service for edge-tester integration tests"
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to create test game service: {FormatError(response.Error)}");
                return null;
            }

            _cachedGameService = response.Result;
            Console.WriteLine($"   Created test game service: {_cachedGameService.ServiceId}");
            return _cachedGameService.ServiceId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test game service: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a test realm using typed proxy.
    /// </summary>
    /// <param name="adminClient">The admin client to use</param>
    /// <param name="codePrefix">Prefix for the realm code (e.g., "CHAR", "LOC")</param>
    /// <param name="description">Human-readable description (e.g., "Character", "Location")</param>
    /// <param name="uniqueCode">Unique suffix for this test run</param>
    /// <returns>The created realm if successful, null otherwise</returns>
    protected static async Task<RealmResponse?> CreateTestRealmAsync(
        BannouClient adminClient,
        string codePrefix,
        string description,
        string uniqueCode)
    {
        try
        {
            // Ensure we have a game service for realm creation
            var gameServiceId = await GetOrCreateTestGameServiceAsync(adminClient);
            if (gameServiceId == null)
            {
                Console.WriteLine("   Failed to create test realm - no game service available");
                return null;
            }

            var response = await adminClient.Realm.CreateRealmAsync(new CreateRealmRequest
            {
                Code = $"{codePrefix}_REALM_{uniqueCode}",
                Name = $"{description} Test Realm {uniqueCode}",
                Description = $"Test realm for {description.ToLowerInvariant()} tests",
                Category = "test",
                GameServiceId = gameServiceId.Value
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to create test realm: {FormatError(response.Error)}");
                return null;
            }

            Console.WriteLine($"   Created test realm: {response.Result.RealmId}");
            return response.Result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test realm: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a test species using typed proxy.
    /// </summary>
    /// <param name="adminClient">The admin client to use</param>
    /// <param name="codePrefix">Prefix for the species code (e.g., "CHAR")</param>
    /// <param name="description">Human-readable description (e.g., "Character")</param>
    /// <param name="uniqueCode">Unique suffix for this test run</param>
    /// <param name="realmId">The realm ID to associate the species with</param>
    /// <returns>The created species if successful, null otherwise</returns>
    protected static async Task<SpeciesResponse?> CreateTestSpeciesAsync(
        BannouClient adminClient,
        string codePrefix,
        string description,
        string uniqueCode,
        Guid realmId)
    {
        try
        {
            var response = await adminClient.Species.CreateSpeciesAsync(new CreateSpeciesRequest
            {
                Code = $"{codePrefix}_SPECIES_{uniqueCode}",
                Name = $"{description} Test Species {uniqueCode}",
                Description = $"Test species for {description.ToLowerInvariant()} tests",
                Category = "test",
                RealmIds = new List<Guid> { realmId }
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to create test species: {FormatError(response.Error)}");
                return null;
            }

            Console.WriteLine($"   Created test species: {response.Result.SpeciesId}");
            return response.Result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test species: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a test location using typed proxy.
    /// </summary>
    protected static async Task<LocationResponse?> CreateTestLocationAsync(
        BannouClient adminClient,
        string codePrefix,
        string uniqueCode,
        Guid realmId,
        LocationType locationType = LocationType.CITY,
        Guid? parentLocationId = null)
    {
        try
        {
            var response = await adminClient.Location.CreateLocationAsync(new CreateLocationRequest
            {
                Code = $"{codePrefix}_LOC_{uniqueCode}",
                Name = $"Test Location {uniqueCode}",
                Description = "Test location",
                RealmId = realmId,
                LocationType = locationType,
                ParentLocationId = parentLocationId
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to create test location: {FormatError(response.Error)}");
                return null;
            }

            Console.WriteLine($"   Created test location: {response.Result.LocationId}");
            return response.Result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test location: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Error Formatting

    /// <summary>
    /// Formats an API error for logging.
    /// </summary>
    protected static string FormatError(ErrorResponse? error)
    {
        if (error == null)
            return "<null error>";

        return $"{error.ResponseCode}: {error.Message}";
    }

    #endregion

    #region JSON Parsing Helpers (for raw protocol tests only)

    /// <summary>
    /// Parses a JsonElement response into a JsonObject.
    /// Handles empty success responses (default JsonElement) gracefully.
    /// NOTE: Only use for raw InvokeAsync tests - prefer typed proxies.
    /// </summary>
    protected static JsonObject? ParseResponse(JsonElement response)
    {
        // Handle empty success responses (200 OK with no body)
        // default(JsonElement) has ValueKind = Undefined
        if (response.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return JsonNode.Parse(response.GetRawText())?.AsObject();
    }

    /// <summary>
    /// Gets a string property from a JsonElement response.
    /// NOTE: Only use for raw InvokeAsync tests - prefer typed proxies.
    /// </summary>
    protected static string? GetStringProperty(JsonElement response, string propertyName)
    {
        var json = ParseResponse(response);
        return json?[propertyName]?.GetValue<string>();
    }

    /// <summary>
    /// Gets a string property from a JsonObject.
    /// NOTE: Only use for raw InvokeAsync tests - prefer typed proxies.
    /// </summary>
    protected static string? GetStringProperty(JsonObject? json, string propertyName)
    {
        return json?[propertyName]?.GetValue<string>();
    }

    /// <summary>
    /// Gets an int property from a JsonObject.
    /// NOTE: Only use for raw InvokeAsync tests - prefer typed proxies.
    /// </summary>
    protected static int GetIntProperty(JsonObject? json, string propertyName, int defaultValue = 0)
    {
        return json?[propertyName]?.GetValue<int>() ?? defaultValue;
    }

    /// <summary>
    /// Checks if a property exists and is a JsonArray.
    /// NOTE: Only use for raw InvokeAsync tests - prefer typed proxies.
    /// </summary>
    protected static bool HasArrayProperty(JsonObject? json, string propertyName)
    {
        return json?[propertyName] != null && json[propertyName] is JsonArray;
    }

    #endregion

    #region Raw API Invocation (for protocol tests only)

    /// <summary>
    /// Performs a raw API call via the admin WebSocket with standardized error handling.
    /// WARNING: Only use for testing the raw WebSocket protocol itself or SDK parity tests.
    /// For service tests, use typed proxies (e.g., adminClient.Realm.CreateRealmAsync).
    /// </summary>
    protected static async Task<JsonObject?> InvokeApiAsync(
        BannouClient adminClient,
        string path,
        object? body = null,
        TimeSpan? timeout = null)
    {
        var response = (await adminClient.InvokeAsync<object, JsonElement>(
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
