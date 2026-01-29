using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.SaveLoad;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for save/load service API endpoints.
/// Tests the save/load service APIs using TYPED PROXIES through the Connect service WebSocket binary protocol.
/// This validates both the service logic AND the typed proxy generation.
/// </summary>
public class SaveLoadWebSocketTestHandler : BaseWebSocketTestHandler
{
    private const string GameId = "edge-test";

    public override ServiceTest[] GetServiceTests() =>
    [
        new ServiceTest(TestSlotCreateAndGet, "SaveLoad - Slot Create and Get (WebSocket)", "WebSocket",
            "Test slot creation and retrieval via typed proxy"),
        new ServiceTest(TestListSlots, "SaveLoad - List Slots (WebSocket)", "WebSocket",
            "Test listing slots by owner via typed proxy"),
        new ServiceTest(TestSaveAndLoadRoundTrip, "SaveLoad - Save and Load Round-Trip (WebSocket)", "WebSocket",
            "Test saving data and loading it back via typed proxy"),
        new ServiceTest(TestSaveWithVerifyIntegrity, "SaveLoad - Save with Verify Integrity (WebSocket)", "WebSocket",
            "Test saving data and verifying integrity via typed proxy"),
        new ServiceTest(TestVersionManagement, "SaveLoad - Version Management (WebSocket)", "WebSocket",
            "Test version listing, pinning, and unpinning via typed proxy"),
        new ServiceTest(TestFullLifecycle, "SaveLoad - Full Lifecycle (WebSocket)", "WebSocket",
            "Test complete save/load lifecycle via typed proxy"),
        new ServiceTest(TestQuerySaves, "SaveLoad - Query Saves (WebSocket)", "WebSocket",
            "Test querying saves by owner via typed proxy"),
        new ServiceTest(TestAdminStats, "SaveLoad - Admin Stats (WebSocket)", "WebSocket",
            "Test admin stats endpoint via typed proxy"),
    ];

    /// <summary>
    /// Creates a test slot with a unique name and returns the response.
    /// </summary>
    private static async Task<SlotResponse?> CreateTestSlotAsync(
        BannouClient adminClient,
        Guid ownerId,
        string slotName)
    {
        var response = await adminClient.SaveLoad.CreateSlotAsync(new CreateSlotRequest
        {
            GameId = GameId,
            OwnerId = ownerId,
            OwnerType = OwnerType.CHARACTER,
            SlotName = slotName,
            Category = SaveCategory.MANUAL_SAVE,
        }, timeout: TimeSpan.FromSeconds(10));

        if (!response.IsSuccess || response.Result == null)
        {
            Console.WriteLine($"   Failed to create slot: {FormatError(response.Error)}");
            return null;
        }

        return response.Result;
    }

    /// <summary>
    /// Saves test data to a slot and returns the response.
    /// </summary>
    private static async Task<SaveResponse?> SaveTestDataAsync(
        BannouClient adminClient,
        Guid ownerId,
        string slotName,
        string? displayName = null)
    {
        var testData = System.Text.Encoding.UTF8.GetBytes("{\"level\":1,\"score\":100}");

        var response = await adminClient.SaveLoad.SaveAsync(new SaveRequest
        {
            GameId = GameId,
            OwnerId = ownerId,
            OwnerType = OwnerType.CHARACTER,
            SlotName = slotName,
            Data = testData,
            DisplayName = displayName,
        }, timeout: TimeSpan.FromSeconds(10));

        if (!response.IsSuccess || response.Result == null)
        {
            Console.WriteLine($"   Failed to save data: {FormatError(response.Error)}");
            return null;
        }

        return response.Result;
    }

    private void TestSlotCreateAndGet(string[] args)
    {
        Console.WriteLine("=== SaveLoad Slot Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing slot creation and retrieval via typed proxy...");

        RunWebSocketTest("SaveLoad slot create and get test", async adminClient =>
        {
            var ownerId = Guid.NewGuid();
            var slotName = $"slot-{GenerateUniqueCode()}";

            Console.WriteLine("   Creating slot via typed proxy...");
            var slot = await CreateTestSlotAsync(adminClient, ownerId, slotName);
            if (slot == null)
                return false;

            Console.WriteLine($"   Created slot: {slot.SlotId}");

            Console.WriteLine("   Retrieving slot via typed proxy...");
            var getResponse = await adminClient.SaveLoad.GetSlotAsync(new GetSlotRequest
            {
                GameId = GameId,
                OwnerId = ownerId,
                OwnerType = OwnerType.CHARACTER,
                SlotName = slotName,
            }, timeout: TimeSpan.FromSeconds(10));

            if (!getResponse.IsSuccess || getResponse.Result == null)
            {
                Console.WriteLine($"   Failed to get slot: {FormatError(getResponse.Error)}");
                return false;
            }

            var retrieved = getResponse.Result;
            Console.WriteLine($"   Retrieved slot: {retrieved.SlotId}");

            return retrieved.SlotId == slot.SlotId
                && retrieved.OwnerId == ownerId
                && retrieved.SlotName == slotName
                && retrieved.Category == SaveCategory.MANUAL_SAVE;
        });
    }

    private void TestListSlots(string[] args)
    {
        Console.WriteLine("=== SaveLoad List Slots Test (WebSocket) ===");
        Console.WriteLine("Testing slot listing by owner via typed proxy...");

        RunWebSocketTest("SaveLoad list slots test", async adminClient =>
        {
            var ownerId = Guid.NewGuid();
            var slotName = $"slot-{GenerateUniqueCode()}";

            Console.WriteLine("   Creating test slot...");
            var slot = await CreateTestSlotAsync(adminClient, ownerId, slotName);
            if (slot == null)
                return false;

            Console.WriteLine("   Listing slots via typed proxy...");
            var listResponse = await adminClient.SaveLoad.ListSlotsAsync(new ListSlotsRequest
            {
                GameId = GameId,
                OwnerId = ownerId,
                OwnerType = OwnerType.CHARACTER,
            }, timeout: TimeSpan.FromSeconds(10));

            if (!listResponse.IsSuccess || listResponse.Result == null)
            {
                Console.WriteLine($"   Failed to list slots: {FormatError(listResponse.Error)}");
                return false;
            }

            var result = listResponse.Result;
            Console.WriteLine($"   Slots returned: {result.Slots.Count}");

            return result.Slots.Count >= 1;
        });
    }

    private void TestSaveAndLoadRoundTrip(string[] args)
    {
        Console.WriteLine("=== SaveLoad Save and Load Round-Trip Test (WebSocket) ===");
        Console.WriteLine("Testing save and load round-trip via typed proxy...");

        RunWebSocketTest("SaveLoad save and load round-trip test", async adminClient =>
        {
            var ownerId = Guid.NewGuid();
            var slotName = $"slot-{GenerateUniqueCode()}";

            Console.WriteLine("   Saving test data via typed proxy...");
            var saveResult = await SaveTestDataAsync(adminClient, ownerId, slotName);
            if (saveResult == null)
                return false;

            Console.WriteLine($"   Saved version {saveResult.VersionNumber}, hash: {saveResult.ContentHash}");

            Console.WriteLine("   Loading data via typed proxy...");
            var loadResponse = await adminClient.SaveLoad.LoadAsync(new LoadRequest
            {
                GameId = GameId,
                OwnerId = ownerId,
                OwnerType = OwnerType.CHARACTER,
                SlotName = slotName,
            }, timeout: TimeSpan.FromSeconds(10));

            if (!loadResponse.IsSuccess || loadResponse.Result == null)
            {
                Console.WriteLine($"   Failed to load data: {FormatError(loadResponse.Error)}");
                return false;
            }

            var loaded = loadResponse.Result;
            var loadedText = System.Text.Encoding.UTF8.GetString(loaded.Data);
            Console.WriteLine($"   Loaded version {loaded.VersionNumber}, data: {loadedText}");

            return loaded.ContentHash == saveResult.ContentHash
                && loadedText == "{\"level\":1,\"score\":100}";
        });
    }

    private void TestSaveWithVerifyIntegrity(string[] args)
    {
        Console.WriteLine("=== SaveLoad Save with Verify Integrity Test (WebSocket) ===");
        Console.WriteLine("Testing save and verify integrity via typed proxy...");

        RunWebSocketTest("SaveLoad save with verify integrity test", async adminClient =>
        {
            var ownerId = Guid.NewGuid();
            var slotName = $"slot-{GenerateUniqueCode()}";

            Console.WriteLine("   Saving test data...");
            var saveResult = await SaveTestDataAsync(adminClient, ownerId, slotName);
            if (saveResult == null)
                return false;

            Console.WriteLine($"   Saved version {saveResult.VersionNumber}");

            Console.WriteLine("   Verifying integrity via typed proxy...");
            var verifyResponse = await adminClient.SaveLoad.VerifyIntegrityAsync(new VerifyIntegrityRequest
            {
                GameId = GameId,
                OwnerId = ownerId,
                OwnerType = OwnerType.CHARACTER,
                SlotName = slotName,
            }, timeout: TimeSpan.FromSeconds(10));

            if (!verifyResponse.IsSuccess || verifyResponse.Result == null)
            {
                Console.WriteLine($"   Failed to verify integrity: {FormatError(verifyResponse.Error)}");
                return false;
            }

            var result = verifyResponse.Result;
            Console.WriteLine($"   Valid: {result.Valid}, Version: {result.VersionNumber}");

            return result.Valid;
        });
    }

    private void TestVersionManagement(string[] args)
    {
        Console.WriteLine("=== SaveLoad Version Management Test (WebSocket) ===");
        Console.WriteLine("Testing version listing, pinning, and unpinning via typed proxy...");

        RunWebSocketTest("SaveLoad version management test", async adminClient =>
        {
            var ownerId = Guid.NewGuid();
            var slotName = $"slot-{GenerateUniqueCode()}";

            // Save twice to create two versions
            Console.WriteLine("   Saving version 1...");
            var save1 = await SaveTestDataAsync(adminClient, ownerId, slotName, "First Save");
            if (save1 == null)
                return false;

            Console.WriteLine("   Saving version 2...");
            var save2 = await SaveTestDataAsync(adminClient, ownerId, slotName, "Second Save");
            if (save2 == null)
                return false;

            // List versions
            Console.WriteLine("   Listing versions via typed proxy...");
            var listResponse = await adminClient.SaveLoad.ListVersionsAsync(new ListVersionsRequest
            {
                OwnerId = ownerId,
                OwnerType = OwnerType.CHARACTER,
                SlotName = slotName,
            }, timeout: TimeSpan.FromSeconds(10));

            if (!listResponse.IsSuccess || listResponse.Result == null)
            {
                Console.WriteLine($"   Failed to list versions: {FormatError(listResponse.Error)}");
                return false;
            }

            Console.WriteLine($"   Versions found: {listResponse.Result.TotalCount}");
            if (listResponse.Result.TotalCount < 2)
            {
                Console.WriteLine("   Expected at least 2 versions");
                return false;
            }

            // Pin version 1
            Console.WriteLine("   Pinning version 1...");
            var pinResponse = await adminClient.SaveLoad.PinVersionAsync(new PinVersionRequest
            {
                OwnerId = ownerId,
                OwnerType = OwnerType.CHARACTER,
                SlotName = slotName,
                VersionNumber = save1.VersionNumber,
                CheckpointName = "test-checkpoint",
            }, timeout: TimeSpan.FromSeconds(10));

            if (!pinResponse.IsSuccess || pinResponse.Result == null)
            {
                Console.WriteLine($"   Failed to pin version: {FormatError(pinResponse.Error)}");
                return false;
            }

            Console.WriteLine($"   Pinned: {pinResponse.Result.Pinned}");
            if (!pinResponse.Result.Pinned)
            {
                Console.WriteLine("   Expected version to be pinned");
                return false;
            }

            // Unpin version 1
            Console.WriteLine("   Unpinning version 1...");
            var unpinResponse = await adminClient.SaveLoad.UnpinVersionAsync(new UnpinVersionRequest
            {
                OwnerId = ownerId,
                OwnerType = OwnerType.CHARACTER,
                SlotName = slotName,
                VersionNumber = save1.VersionNumber,
            }, timeout: TimeSpan.FromSeconds(10));

            if (!unpinResponse.IsSuccess || unpinResponse.Result == null)
            {
                Console.WriteLine($"   Failed to unpin version: {FormatError(unpinResponse.Error)}");
                return false;
            }

            Console.WriteLine($"   Unpinned: {!unpinResponse.Result.Pinned}");
            return !unpinResponse.Result.Pinned;
        });
    }

    private void TestFullLifecycle(string[] args)
    {
        Console.WriteLine("=== SaveLoad Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete save/load lifecycle via typed proxy...");

        RunWebSocketTest("SaveLoad full lifecycle test", async adminClient =>
        {
            var ownerId = Guid.NewGuid();
            var slotName = $"slot-{GenerateUniqueCode()}";

            // Step 1: Create slot
            Console.WriteLine("   Step 1: Creating slot...");
            var slot = await CreateTestSlotAsync(adminClient, ownerId, slotName);
            if (slot == null)
                return false;
            Console.WriteLine($"   Created slot {slot.SlotId}");

            // Step 2: Save data
            Console.WriteLine("   Step 2: Saving data...");
            var save1 = await SaveTestDataAsync(adminClient, ownerId, slotName, "Lifecycle Save 1");
            if (save1 == null)
                return false;
            Console.WriteLine($"   Saved version {save1.VersionNumber}");

            // Step 3: Load data
            Console.WriteLine("   Step 3: Loading data...");
            var loadResponse = await adminClient.SaveLoad.LoadAsync(new LoadRequest
            {
                GameId = GameId,
                OwnerId = ownerId,
                OwnerType = OwnerType.CHARACTER,
                SlotName = slotName,
            }, timeout: TimeSpan.FromSeconds(10));

            if (!loadResponse.IsSuccess || loadResponse.Result == null)
            {
                Console.WriteLine($"   Failed to load: {FormatError(loadResponse.Error)}");
                return false;
            }
            Console.WriteLine($"   Loaded version {loadResponse.Result.VersionNumber}");

            // Step 4: Save again
            Console.WriteLine("   Step 4: Saving again...");
            var save2 = await SaveTestDataAsync(adminClient, ownerId, slotName, "Lifecycle Save 2");
            if (save2 == null)
                return false;
            Console.WriteLine($"   Saved version {save2.VersionNumber}");

            // Step 5: List versions
            Console.WriteLine("   Step 5: Listing versions...");
            var listResponse = await adminClient.SaveLoad.ListVersionsAsync(new ListVersionsRequest
            {
                OwnerId = ownerId,
                OwnerType = OwnerType.CHARACTER,
                SlotName = slotName,
            }, timeout: TimeSpan.FromSeconds(10));

            if (!listResponse.IsSuccess || listResponse.Result == null)
            {
                Console.WriteLine($"   Failed to list versions: {FormatError(listResponse.Error)}");
                return false;
            }
            Console.WriteLine($"   Found {listResponse.Result.TotalCount} versions");

            // Step 6: Pin version 1
            Console.WriteLine("   Step 6: Pinning version 1...");
            var pinResponse = await adminClient.SaveLoad.PinVersionAsync(new PinVersionRequest
            {
                OwnerId = ownerId,
                OwnerType = OwnerType.CHARACTER,
                SlotName = slotName,
                VersionNumber = save1.VersionNumber,
                CheckpointName = "lifecycle-checkpoint",
            }, timeout: TimeSpan.FromSeconds(10));

            if (!pinResponse.IsSuccess || pinResponse.Result == null)
            {
                Console.WriteLine($"   Failed to pin version: {FormatError(pinResponse.Error)}");
                return false;
            }
            Console.WriteLine($"   Pinned version {save1.VersionNumber}");

            // Step 7: Delete version 2
            Console.WriteLine("   Step 7: Deleting version 2...");
            var deleteVersionResponse = await adminClient.SaveLoad.DeleteVersionAsync(new DeleteVersionRequest
            {
                OwnerId = ownerId,
                OwnerType = OwnerType.CHARACTER,
                SlotName = slotName,
                VersionNumber = save2.VersionNumber,
            }, timeout: TimeSpan.FromSeconds(10));

            if (!deleteVersionResponse.IsSuccess || deleteVersionResponse.Result == null)
            {
                Console.WriteLine($"   Failed to delete version: {FormatError(deleteVersionResponse.Error)}");
                return false;
            }
            Console.WriteLine($"   Deleted: {deleteVersionResponse.Result.Deleted}");

            // Step 8: Rename slot
            var newSlotName = $"renamed-{GenerateUniqueCode()}";
            Console.WriteLine($"   Step 8: Renaming slot to {newSlotName}...");
            var renameResponse = await adminClient.SaveLoad.RenameSlotAsync(new RenameSlotRequest
            {
                GameId = GameId,
                OwnerId = ownerId,
                OwnerType = OwnerType.CHARACTER,
                SlotName = slotName,
                NewSlotName = newSlotName,
            }, timeout: TimeSpan.FromSeconds(10));

            if (!renameResponse.IsSuccess || renameResponse.Result == null)
            {
                Console.WriteLine($"   Failed to rename slot: {FormatError(renameResponse.Error)}");
                return false;
            }
            Console.WriteLine($"   Renamed to: {renameResponse.Result.SlotName}");

            // Step 9: Delete slot
            Console.WriteLine("   Step 9: Deleting slot...");
            var deleteSlotResponse = await adminClient.SaveLoad.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = GameId,
                OwnerId = ownerId,
                OwnerType = OwnerType.CHARACTER,
                SlotName = newSlotName,
            }, timeout: TimeSpan.FromSeconds(10));

            if (!deleteSlotResponse.IsSuccess || deleteSlotResponse.Result == null)
            {
                Console.WriteLine($"   Failed to delete slot: {FormatError(deleteSlotResponse.Error)}");
                return false;
            }

            Console.WriteLine($"   Deleted slot, versions freed: {deleteSlotResponse.Result.VersionsDeleted}");
            return deleteSlotResponse.Result.Deleted;
        });
    }

    private void TestQuerySaves(string[] args)
    {
        Console.WriteLine("=== SaveLoad Query Saves Test (WebSocket) ===");
        Console.WriteLine("Testing query saves by owner via typed proxy...");

        RunWebSocketTest("SaveLoad query saves test", async adminClient =>
        {
            var ownerId = Guid.NewGuid();
            var slotName = $"slot-{GenerateUniqueCode()}";

            Console.WriteLine("   Creating slot and saving data...");
            var saveResult = await SaveTestDataAsync(adminClient, ownerId, slotName);
            if (saveResult == null)
                return false;

            Console.WriteLine("   Querying saves via typed proxy...");
            var queryResponse = await adminClient.SaveLoad.QuerySavesAsync(new QuerySavesRequest
            {
                OwnerId = ownerId,
                OwnerType = OwnerType.CHARACTER,
            }, timeout: TimeSpan.FromSeconds(10));

            if (!queryResponse.IsSuccess || queryResponse.Result == null)
            {
                Console.WriteLine($"   Failed to query saves: {FormatError(queryResponse.Error)}");
                return false;
            }

            var result = queryResponse.Result;
            Console.WriteLine($"   Query returned {result.TotalCount} results");

            return result.TotalCount >= 1 && result.Results.Count >= 1;
        });
    }

    private void TestAdminStats(string[] args)
    {
        Console.WriteLine("=== SaveLoad Admin Stats Test (WebSocket) ===");
        Console.WriteLine("Testing admin stats endpoint via typed proxy...");

        RunWebSocketTest("SaveLoad admin stats test", async adminClient =>
        {
            Console.WriteLine("   Calling admin stats via typed proxy...");
            var statsResponse = await adminClient.SaveLoad.AdminStatsAsync(new AdminStatsRequest(),
                timeout: TimeSpan.FromSeconds(10));

            if (!statsResponse.IsSuccess || statsResponse.Result == null)
            {
                Console.WriteLine($"   Failed to get admin stats: {FormatError(statsResponse.Error)}");
                return false;
            }

            var stats = statsResponse.Result;
            Console.WriteLine($"   Total slots: {stats.TotalSlots}, Total versions: {stats.TotalVersions}, Total size: {stats.TotalSizeBytes} bytes");

            return stats.TotalSlots >= 0;
        });
    }
}
