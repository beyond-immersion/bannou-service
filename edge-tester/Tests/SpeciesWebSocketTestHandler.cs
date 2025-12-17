using BeyondImmersion.Bannou.Client.SDK;
using BeyondImmersion.BannouService.Species;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for species service API endpoints.
/// Tests the species service APIs through the Connect service WebSocket binary protocol.
///
/// IMPORTANT: These tests create dedicated test accounts with their own BannouClient instances.
/// This avoids interfering with Program.Client or Program.AdminClient, and properly tests
/// the user experience from account creation through API usage.
/// </summary>
public class SpeciesWebSocketTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(TestListSpeciesViaWebSocket, "Species - List (WebSocket)", "WebSocket",
                "Test species listing via WebSocket binary protocol"),
            new ServiceTest(TestCreateAndGetSpeciesViaWebSocket, "Species - Create and Get (WebSocket)", "WebSocket",
                "Test species creation and retrieval via WebSocket binary protocol"),
            new ServiceTest(TestSpeciesLifecycleViaWebSocket, "Species - Full Lifecycle (WebSocket)", "WebSocket",
                "Test complete species lifecycle via WebSocket: create -> update -> deprecate -> undeprecate"),
        };
    }

    #region Helper Methods for Test Account Creation

    /// <summary>
    /// Creates a dedicated admin test account and returns the access token and connect URL.
    /// Admin accounts have elevated permissions needed for species management.
    /// </summary>
    private async Task<(string accessToken, string connectUrl)?> CreateAdminTestAccountAsync(string testPrefix)
    {
        if (Program.Configuration == null)
        {
            Console.WriteLine("   Configuration not available");
            return null;
        }

        var openrestyHost = Program.Configuration.OpenResty_Host ?? "openresty";
        var openrestyPort = Program.Configuration.OpenResty_Port ?? 80;
        var uniqueId = Guid.NewGuid().ToString("N")[..12];
        var testEmail = $"{testPrefix}_{uniqueId}@test.local";
        var testPassword = $"{testPrefix}Test123!";

        try
        {
            var registerUrl = $"http://{openrestyHost}:{openrestyPort}/auth/register";
            var registerContent = new { username = $"admin_{testPrefix}_{uniqueId}", email = testEmail, password = testPassword, role = "admin" };

            using var registerRequest = new HttpRequestMessage(HttpMethod.Post, registerUrl);
            registerRequest.Content = new StringContent(
                JsonSerializer.Serialize(registerContent),
                Encoding.UTF8,
                "application/json");

            using var registerResponse = await Program.HttpClient.SendAsync(registerRequest);
            if (!registerResponse.IsSuccessStatusCode)
            {
                var errorBody = await registerResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"   Failed to create admin test account: {registerResponse.StatusCode} - {errorBody}");
                return null;
            }

            var responseBody = await registerResponse.Content.ReadAsStringAsync();
            var responseObj = JsonDocument.Parse(responseBody);
            var accessToken = responseObj.RootElement.GetProperty("accessToken").GetString();
            var connectUrl = responseObj.RootElement.GetProperty("connectUrl").GetString();

            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("   No accessToken in registration response");
                return null;
            }

            if (string.IsNullOrEmpty(connectUrl))
            {
                Console.WriteLine("   No connectUrl in registration response");
                return null;
            }

            Console.WriteLine($"   Created admin test account: {testEmail}");
            return (accessToken, connectUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create admin test account: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a BannouClient connected with the given access token and connect URL.
    /// Returns null if connection fails.
    /// </summary>
    private async Task<BannouClient?> CreateConnectedClientAsync(string accessToken, string connectUrl)
    {
        var client = new BannouClient();

        try
        {
            var connected = await client.ConnectWithTokenAsync(connectUrl, accessToken);
            if (!connected || !client.IsConnected)
            {
                Console.WriteLine("   BannouClient failed to connect");
                await client.DisposeAsync();
                return null;
            }

            Console.WriteLine($"   BannouClient connected, session: {client.SessionId}");
            return client;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   BannouClient connection failed: {ex.Message}");
            await client.DisposeAsync();
            return null;
        }
    }

    #endregion

    private void TestListSpeciesViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Species List Test (WebSocket) ===");
        Console.WriteLine("Testing /species/list via dedicated BannouClient...");

        try
        {
            var result = Task.Run(async () =>
            {
                var authResult = await CreateAdminTestAccountAsync("species_list");
                if (authResult == null)
                {
                    return false;
                }

                await using var client = await CreateConnectedClientAsync(authResult.Value.accessToken, authResult.Value.connectUrl);
                if (client == null)
                {
                    return false;
                }

                var listRequest = new ListSpeciesRequest();

                try
                {
                    Console.WriteLine("   Invoking /species/list...");
                    var response = await client.InvokeAsync<ListSpeciesRequest, JsonElement>(
                        "POST",
                        "/species/list",
                        listRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var hasSpeciesArray = response.TryGetProperty("species", out var speciesProp) &&
                                        speciesProp.ValueKind == JsonValueKind.Array;
                    var totalCount = response.TryGetProperty("totalCount", out var countProp) ? countProp.GetInt32() : 0;

                    Console.WriteLine($"   Species array present: {hasSpeciesArray}");
                    Console.WriteLine($"   Total Count: {totalCount}");

                    return hasSpeciesArray;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED Species list test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED Species list test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Species list test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestCreateAndGetSpeciesViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Species Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /species/create and /species/get via dedicated BannouClient...");

        try
        {
            var result = Task.Run(async () =>
            {
                var authResult = await CreateAdminTestAccountAsync("species_crud");
                if (authResult == null)
                {
                    return false;
                }

                await using var client = await CreateConnectedClientAsync(authResult.Value.accessToken, authResult.Value.connectUrl);
                if (client == null)
                {
                    return false;
                }

                var uniqueCode = $"TEST{DateTime.Now.Ticks % 100000}";
                var createRequest = new CreateSpeciesRequest
                {
                    Code = uniqueCode,
                    Name = $"Test Species {uniqueCode}",
                    Description = "Created via WebSocket edge test",
                    IsPlayable = true,
                    BaseLifespan = 100,
                    MaturityAge = 18
                };

                try
                {
                    Console.WriteLine("   Invoking /species/create...");
                    var createResponse = await client.InvokeAsync<CreateSpeciesRequest, JsonElement>(
                        "POST",
                        "/species/create",
                        createRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var speciesIdStr = createResponse.TryGetProperty("speciesId", out var idProp) ? idProp.GetString() : null;
                    var code = createResponse.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : null;

                    if (string.IsNullOrEmpty(speciesIdStr))
                    {
                        Console.WriteLine("   Failed to create species - no speciesId in response");
                        return false;
                    }

                    Console.WriteLine($"   Created species: {speciesIdStr} ({code})");

                    // Now retrieve it
                    var getRequest = new GetSpeciesRequest
                    {
                        SpeciesId = Guid.Parse(speciesIdStr)
                    };

                    Console.WriteLine("   Invoking /species/get...");
                    var getResponse = await client.InvokeAsync<GetSpeciesRequest, JsonElement>(
                        "POST",
                        "/species/get",
                        getRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var retrievedId = getResponse.TryGetProperty("speciesId", out var retrievedIdProp) ? retrievedIdProp.GetString() : null;
                    var retrievedCode = getResponse.TryGetProperty("code", out var retrievedCodeProp) ? retrievedCodeProp.GetString() : null;

                    Console.WriteLine($"   Retrieved species: {retrievedId} ({retrievedCode})");

                    return retrievedId == speciesIdStr && retrievedCode == uniqueCode;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Invoke failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED Species create and get test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED Species create and get test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Species create and get test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void TestSpeciesLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Species Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete species lifecycle via dedicated BannouClient...");

        try
        {
            var result = Task.Run(async () =>
            {
                var authResult = await CreateAdminTestAccountAsync("species_lifecycle");
                if (authResult == null)
                {
                    return false;
                }

                await using var client = await CreateConnectedClientAsync(authResult.Value.accessToken, authResult.Value.connectUrl);
                if (client == null)
                {
                    return false;
                }

                try
                {
                    // Step 1: Create species
                    Console.WriteLine("   Step 1: Creating species...");
                    var uniqueCode = $"LIFE{DateTime.Now.Ticks % 100000}";
                    var createRequest = new CreateSpeciesRequest
                    {
                        Code = uniqueCode,
                        Name = $"Lifecycle Test {uniqueCode}",
                        Description = "Lifecycle test species",
                        IsPlayable = false
                    };

                    var createResponse = await client.InvokeAsync<CreateSpeciesRequest, JsonElement>(
                        "POST",
                        "/species/create",
                        createRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var speciesIdStr = createResponse.TryGetProperty("speciesId", out var idProp) ? idProp.GetString() : null;
                    if (string.IsNullOrEmpty(speciesIdStr))
                    {
                        Console.WriteLine("   Failed to create species - no speciesId in response");
                        return false;
                    }
                    var speciesId = Guid.Parse(speciesIdStr);
                    Console.WriteLine($"   Created species {speciesId}");

                    // Step 2: Update species
                    Console.WriteLine("   Step 2: Updating species...");
                    var updateRequest = new UpdateSpeciesRequest
                    {
                        SpeciesId = speciesId,
                        Name = $"Updated Lifecycle Test {uniqueCode}",
                        Description = "Updated description"
                    };

                    var updateResponse = await client.InvokeAsync<UpdateSpeciesRequest, JsonElement>(
                        "POST",
                        "/species/update",
                        updateRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var updatedName = updateResponse.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    if (!updatedName?.StartsWith("Updated") ?? true)
                    {
                        Console.WriteLine($"   Failed to update species - name: {updatedName}");
                        return false;
                    }
                    Console.WriteLine($"   Updated species name to: {updatedName}");

                    // Step 3: Deprecate species
                    Console.WriteLine("   Step 3: Deprecating species...");
                    var deprecateRequest = new DeprecateSpeciesRequest
                    {
                        SpeciesId = speciesId,
                        Reason = "WebSocket lifecycle test"
                    };

                    var deprecateResponse = await client.InvokeAsync<DeprecateSpeciesRequest, JsonElement>(
                        "POST",
                        "/species/deprecate",
                        deprecateRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var isDeprecated = deprecateResponse.TryGetProperty("isDeprecated", out var deprecatedProp) && deprecatedProp.GetBoolean();
                    if (!isDeprecated)
                    {
                        Console.WriteLine("   Failed to deprecate species");
                        return false;
                    }
                    Console.WriteLine($"   Species deprecated successfully");

                    // Step 4: Undeprecate species
                    Console.WriteLine("   Step 4: Undeprecating species...");
                    var undeprecateRequest = new UndeprecateSpeciesRequest
                    {
                        SpeciesId = speciesId
                    };

                    var undeprecateResponse = await client.InvokeAsync<UndeprecateSpeciesRequest, JsonElement>(
                        "POST",
                        "/species/undeprecate",
                        undeprecateRequest,
                        timeout: TimeSpan.FromSeconds(15));

                    var isUndeprecated = undeprecateResponse.TryGetProperty("isDeprecated", out var undeprecatedProp) && !undeprecatedProp.GetBoolean();
                    if (!isUndeprecated)
                    {
                        Console.WriteLine("   Failed to undeprecate species");
                        return false;
                    }
                    Console.WriteLine($"   Species restored successfully");

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Lifecycle test failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
            {
                Console.WriteLine("PASSED Species complete lifecycle test via WebSocket");
            }
            else
            {
                Console.WriteLine("FAILED Species complete lifecycle test via WebSocket");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED Species lifecycle test with exception: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
        }
    }
}
