using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.Species;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for species service API endpoints.
/// Tests the species service APIs using TYPED PROXIES through the Connect service WebSocket binary protocol.
/// This validates both the service logic AND the typed proxy generation.
/// </summary>
public class SpeciesWebSocketTestHandler : BaseWebSocketTestHandler
{
    private const string CodePrefix = "SPEC";
    private const string Description = "Species";

    public override ServiceTest[] GetServiceTests() =>
    [
        new ServiceTest(TestListSpeciesViaWebSocket, "Species - List (WebSocket)", "WebSocket",
            "Test species listing via typed proxy"),
        new ServiceTest(TestCreateAndGetSpeciesViaWebSocket, "Species - Create and Get (WebSocket)", "WebSocket",
            "Test species creation and retrieval via typed proxy"),
        new ServiceTest(TestSpeciesLifecycleViaWebSocket, "Species - Full Lifecycle (WebSocket)", "WebSocket",
            "Test complete species lifecycle via typed proxy: create -> update -> deprecate -> undeprecate"),
    ];

    private void TestListSpeciesViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Species List Test (WebSocket) ===");
        Console.WriteLine("Testing species list via typed proxy...");

        RunWebSocketTest("Species list test", async adminClient =>
        {
            var response = await adminClient.Species.ListSpeciesAsync(new ListSpeciesRequest());

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to list species: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;
            Console.WriteLine($"   Species array present: {result.Species != null}");
            Console.WriteLine($"   Total Count: {result.TotalCount}");

            return result.Species != null;
        });
    }

    private void TestCreateAndGetSpeciesViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Species Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing species creation and retrieval via typed proxy...");

        RunWebSocketTest("Species create and get test", async adminClient =>
        {
            var uniqueCode = $"TEST{GenerateUniqueCode()}";

            // Create species using typed proxy
            Console.WriteLine("   Creating species via typed proxy...");
            var createResponse = await adminClient.Species.CreateSpeciesAsync(new CreateSpeciesRequest
            {
                Code = uniqueCode,
                Name = $"Test Species {uniqueCode}",
                Description = "Created via WebSocket edge test",
                IsPlayable = true,
                BaseLifespan = 100,
                MaturityAge = 18
            });

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create species: {FormatError(createResponse.Error)}");
                return false;
            }

            var species = createResponse.Result;
            Console.WriteLine($"   Created species: {species.SpeciesId} ({species.Code})");

            // Retrieve it using typed proxy
            Console.WriteLine("   Retrieving species via typed proxy...");
            var getResponse = await adminClient.Species.GetSpeciesAsync(new GetSpeciesRequest
            {
                SpeciesId = species.SpeciesId
            });

            if (!getResponse.IsSuccess || getResponse.Result == null)
            {
                Console.WriteLine($"   Failed to get species: {FormatError(getResponse.Error)}");
                return false;
            }

            var retrieved = getResponse.Result;
            Console.WriteLine($"   Retrieved species: {retrieved.SpeciesId} ({retrieved.Code})");

            return retrieved.SpeciesId == species.SpeciesId && retrieved.Code == uniqueCode;
        });
    }

    private void TestSpeciesLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Species Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete species lifecycle via typed proxy...");

        RunWebSocketTest("Species complete lifecycle test", async adminClient =>
        {
            // Step 1: Create species
            Console.WriteLine("   Step 1: Creating species...");
            var uniqueCode = $"LIFE{GenerateUniqueCode()}";

            var createResponse = await adminClient.Species.CreateSpeciesAsync(new CreateSpeciesRequest
            {
                Code = uniqueCode,
                Name = $"Lifecycle Test {uniqueCode}",
                Description = "Lifecycle test species",
                IsPlayable = false
            });

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create species: {FormatError(createResponse.Error)}");
                return false;
            }

            var species = createResponse.Result;
            Console.WriteLine($"   Created species {species.SpeciesId}");

            // Step 2: Update species
            Console.WriteLine("   Step 2: Updating species...");
            var updateResponse = await adminClient.Species.UpdateSpeciesAsync(new UpdateSpeciesRequest
            {
                SpeciesId = species.SpeciesId,
                Name = $"Updated Lifecycle Test {uniqueCode}",
                Description = "Updated description"
            });

            if (!updateResponse.IsSuccess || updateResponse.Result == null)
            {
                Console.WriteLine($"   Failed to update species: {FormatError(updateResponse.Error)}");
                return false;
            }

            var updated = updateResponse.Result;
            if (!updated.Name.StartsWith("Updated"))
            {
                Console.WriteLine($"   Update didn't apply - name: {updated.Name}");
                return false;
            }
            Console.WriteLine($"   Updated species name to: {updated.Name}");

            // Step 3: Deprecate species
            Console.WriteLine("   Step 3: Deprecating species...");
            var deprecateResponse = await adminClient.Species.DeprecateSpeciesAsync(new DeprecateSpeciesRequest
            {
                SpeciesId = species.SpeciesId,
                DeprecationReason = "WebSocket lifecycle test"
            });

            if (!deprecateResponse.IsSuccess || deprecateResponse.Result == null)
            {
                Console.WriteLine($"   Failed to deprecate species: {FormatError(deprecateResponse.Error)}");
                return false;
            }

            if (!deprecateResponse.Result.IsDeprecated)
            {
                Console.WriteLine("   Species not marked as deprecated");
                return false;
            }
            Console.WriteLine("   Species deprecated successfully");

            // Step 4: Undeprecate species
            Console.WriteLine("   Step 4: Undeprecating species...");
            var undeprecateResponse = await adminClient.Species.UndeprecateSpeciesAsync(new UndeprecateSpeciesRequest
            {
                SpeciesId = species.SpeciesId
            });

            if (!undeprecateResponse.IsSuccess || undeprecateResponse.Result == null)
            {
                Console.WriteLine($"   Failed to undeprecate species: {FormatError(undeprecateResponse.Error)}");
                return false;
            }

            if (undeprecateResponse.Result.IsDeprecated)
            {
                Console.WriteLine("   Species still marked as deprecated");
                return false;
            }
            Console.WriteLine("   Species restored successfully");

            return true;
        });
    }
}
