using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// Test handler for verifying TypeScript SDK parity with the C# SDK.
/// Tests that both SDKs produce identical results for the same API calls.
/// </summary>
public class TypeScriptParityTestHandler : BaseWebSocketTestHandler
{
    public override ServiceTest[] GetServiceTests()
    {
        return
        [
            new ServiceTest(TestTsConnectionAndPing, "TS Parity - Connection", "Parity",
                "Test TypeScript SDK can connect to server"),
            new ServiceTest(TestTsRealmListParity, "TS Parity - Realm List", "Parity",
                "Verify C# and TypeScript SDKs return identical realm list"),
            new ServiceTest(TestTsSpeciesListParity, "TS Parity - Species List", "Parity",
                "Verify C# and TypeScript SDKs return identical species list"),
            new ServiceTest(TestTsCreateAndReadParity, "TS Parity - Create & Read", "Parity",
                "Verify create and read operations produce identical results"),
        ];
    }

    private void TestTsConnectionAndPing(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Connection Test ===");
        Console.WriteLine("Testing TypeScript SDK can connect to server...");

        RunWebSocketTest("TypeScript Connection", async _ =>
        {
            if (Program.Configuration == null)
            {
                Console.WriteLine("❌ Configuration not available");
                return false;
            }

            await using var tsHelper = await TypeScriptParityHelper.CreateAsync();

            // Get the HTTP base URL and construct the connect URL for TypeScript
            var httpUrl = $"http://{Program.Configuration.LoginCredentialsEndpoint}".Replace("/auth/login", "");
            Console.WriteLine($"   [TS SDK] Connecting to: {httpUrl}");

            var connected = await tsHelper.ConnectAsync(
                httpUrl,
                Program.Configuration.ClientUsername ?? throw new InvalidOperationException("ClientUsername not set"),
                Program.Configuration.ClientPassword ?? throw new InvalidOperationException("ClientPassword not set"));

            if (!connected)
            {
                Console.WriteLine("❌ TypeScript SDK failed to connect");
                return false;
            }

            Console.WriteLine("✅ TypeScript SDK connected successfully");
            return true;
        });
    }

    private void TestTsRealmListParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Realm List Parity Test ===");
        Console.WriteLine("Verifying both SDKs return identical realm list...");

        RunWebSocketTest("Realm List Parity", async adminClient =>
        {
            if (Program.Configuration == null)
            {
                Console.WriteLine("❌ Configuration not available");
                return false;
            }

            // Call C# SDK first
            Console.WriteLine("   [C# SDK] Calling /realm/list...");
            var csharpResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/realm/list", new { },
                timeout: TimeSpan.FromSeconds(10));

            if (!csharpResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK failed: {csharpResponse.Error?.Message}");
                return false;
            }

            var csharpRealms = ParseResponse(csharpResponse.Result);
            var csharpCount = csharpRealms?["realms"]?.AsArray()?.Count ?? 0;
            Console.WriteLine($"   [C# SDK] Returned {csharpCount} realms");

            // Now call TypeScript SDK
            await using var tsHelper = await TypeScriptParityHelper.CreateAsync();

            var httpUrl = $"http://{Program.Configuration.LoginCredentialsEndpoint}".Replace("/auth/login", "");
            var connected = await tsHelper.ConnectAsync(
                httpUrl,
                Program.Configuration.ClientUsername ?? throw new InvalidOperationException("ClientUsername not set"),
                Program.Configuration.ClientPassword ?? throw new InvalidOperationException("ClientPassword not set"));

            if (!connected)
            {
                Console.WriteLine("❌ TypeScript SDK failed to connect");
                return false;
            }

            Console.WriteLine("   [TS SDK] Calling /realm/list...");
            var tsResult = await tsHelper.InvokeRawAsync("POST", "/realm/list", new { });

            if (!tsResult.IsSuccess)
            {
                Console.WriteLine($"❌ TypeScript SDK failed: {tsResult.ErrorMessage}");
                return false;
            }

            var tsRealms = tsResult.Result.HasValue
                ? JsonNode.Parse(tsResult.Result.Value.GetRawText())?.AsObject()
                : null;
            var tsCount = tsRealms?["realms"]?.AsArray()?.Count ?? 0;
            Console.WriteLine($"   [TS SDK] Returned {tsCount} realms");

            // Verify parity
            if (csharpCount != tsCount)
            {
                Console.WriteLine($"❌ Parity failure: C# returned {csharpCount} realms, TS returned {tsCount}");
                return false;
            }

            Console.WriteLine($"✅ Parity verified: Both SDKs returned {csharpCount} realms");
            return true;
        });
    }

    private void TestTsSpeciesListParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Species List Parity Test ===");
        Console.WriteLine("Verifying both SDKs return identical species list...");

        RunWebSocketTest("Species List Parity", async adminClient =>
        {
            if (Program.Configuration == null)
            {
                Console.WriteLine("❌ Configuration not available");
                return false;
            }

            // Call C# SDK first
            Console.WriteLine("   [C# SDK] Calling /species/list...");
            var csharpResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/species/list", new { },
                timeout: TimeSpan.FromSeconds(10));

            if (!csharpResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK failed: {csharpResponse.Error?.Message}");
                return false;
            }

            var csharpSpecies = ParseResponse(csharpResponse.Result);
            var csharpArray = csharpSpecies?["species"]?.AsArray();
            var csharpCount = csharpArray?.Count ?? 0;
            Console.WriteLine($"   [C# SDK] Returned {csharpCount} species");

            // Now call TypeScript SDK
            await using var tsHelper = await TypeScriptParityHelper.CreateAsync();

            var httpUrl = $"http://{Program.Configuration.LoginCredentialsEndpoint}".Replace("/auth/login", "");
            var connected = await tsHelper.ConnectAsync(
                httpUrl,
                Program.Configuration.ClientUsername ?? throw new InvalidOperationException("ClientUsername not set"),
                Program.Configuration.ClientPassword ?? throw new InvalidOperationException("ClientPassword not set"));

            if (!connected)
            {
                Console.WriteLine("❌ TypeScript SDK failed to connect");
                return false;
            }

            Console.WriteLine("   [TS SDK] Calling /species/list...");
            var tsResult = await tsHelper.InvokeRawAsync("POST", "/species/list", new { });

            if (!tsResult.IsSuccess)
            {
                Console.WriteLine($"❌ TypeScript SDK failed: {tsResult.ErrorMessage}");
                return false;
            }

            var tsSpecies = tsResult.Result.HasValue
                ? JsonNode.Parse(tsResult.Result.Value.GetRawText())?.AsObject()
                : null;
            var tsCount = tsSpecies?["species"]?.AsArray()?.Count ?? 0;
            Console.WriteLine($"   [TS SDK] Returned {tsCount} species");

            // Verify parity
            if (csharpCount != tsCount)
            {
                Console.WriteLine($"❌ Parity failure: C# returned {csharpCount} species, TS returned {tsCount}");
                return false;
            }

            Console.WriteLine($"✅ Parity verified: Both SDKs returned {csharpCount} species");
            return true;
        });
    }

    private void TestTsCreateAndReadParity(string[] args)
    {
        Console.WriteLine("=== TypeScript SDK Create & Read Parity Test ===");
        Console.WriteLine("Testing create and read operations produce identical results...");

        RunWebSocketTest("Create & Read Parity", async adminClient =>
        {
            if (Program.Configuration == null)
            {
                Console.WriteLine("❌ Configuration not available");
                return false;
            }

            // First, create a realm using C# SDK
            var uniqueCode = GenerateUniqueCode();
            var realmCode = $"PARITY_TEST_{uniqueCode}";

            Console.WriteLine($"   [C# SDK] Creating realm {realmCode}...");
            var createResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/realm/create",
                new
                {
                    code = realmCode,
                    name = $"Parity Test Realm {uniqueCode}",
                    description = "Created for TypeScript parity testing",
                    category = "test"
                },
                timeout: TimeSpan.FromSeconds(10));

            if (!createResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK failed to create realm: {createResponse.Error?.Message}");
                return false;
            }

            var realmId = GetStringProperty(createResponse.Result, "realmId");
            if (string.IsNullOrEmpty(realmId))
            {
                Console.WriteLine("❌ C# SDK create response missing realmId");
                return false;
            }
            Console.WriteLine($"   [C# SDK] Created realm: {realmId}");

            // Now read the realm using C# SDK
            Console.WriteLine("   [C# SDK] Reading realm...");
            var csharpReadResponse = await adminClient.InvokeAsync<object, JsonElement>(
                "POST", "/realm/get",
                new { realmId },
                timeout: TimeSpan.FromSeconds(10));

            if (!csharpReadResponse.IsSuccess)
            {
                Console.WriteLine($"❌ C# SDK failed to read realm: {csharpReadResponse.Error?.Message}");
                return false;
            }

            var csharpRealm = ParseResponse(csharpReadResponse.Result);
            var csharpRealmName = GetStringProperty(csharpRealm, "name");
            Console.WriteLine($"   [C# SDK] Read realm: {csharpRealmName}");

            // Now connect with TypeScript SDK and read the same realm
            await using var tsHelper = await TypeScriptParityHelper.CreateAsync();

            var httpUrl = $"http://{Program.Configuration.LoginCredentialsEndpoint}".Replace("/auth/login", "");
            var connected = await tsHelper.ConnectAsync(
                httpUrl,
                Program.Configuration.ClientUsername ?? throw new InvalidOperationException("ClientUsername not set"),
                Program.Configuration.ClientPassword ?? throw new InvalidOperationException("ClientPassword not set"));

            if (!connected)
            {
                Console.WriteLine("❌ TypeScript SDK failed to connect");
                return false;
            }

            Console.WriteLine("   [TS SDK] Reading same realm...");
            var tsReadResult = await tsHelper.InvokeRawAsync("POST", "/realm/get", new { realmId });

            if (!tsReadResult.IsSuccess)
            {
                Console.WriteLine($"❌ TypeScript SDK failed to read realm: {tsReadResult.ErrorMessage}");
                return false;
            }

            var tsRealm = tsReadResult.Result.HasValue
                ? JsonNode.Parse(tsReadResult.Result.Value.GetRawText())?.AsObject()
                : null;
            var tsRealmName = GetStringProperty(tsRealm, "name");
            Console.WriteLine($"   [TS SDK] Read realm: {tsRealmName}");

            // Verify the realm data matches
            if (csharpRealmName != tsRealmName)
            {
                Console.WriteLine($"❌ Parity failure: Realm names differ (C#: {csharpRealmName}, TS: {tsRealmName})");
                return false;
            }

            // Compare full JSON (excluding dynamic fields like timestamps)
            var csharpCode = GetStringProperty(csharpRealm, "code");
            var tsCode = GetStringProperty(tsRealm, "code");

            if (csharpCode != tsCode)
            {
                Console.WriteLine($"❌ Parity failure: Realm codes differ (C#: {csharpCode}, TS: {tsCode})");
                return false;
            }

            Console.WriteLine($"✅ Parity verified: Both SDKs returned identical realm data");
            return true;
        });
    }
}
