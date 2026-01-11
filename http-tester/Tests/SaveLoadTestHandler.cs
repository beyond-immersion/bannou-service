using BeyondImmersion.BannouService.SaveLoad;
using BeyondImmersion.BannouService.Testing;
using System.Text;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for the Save-Load service HTTP API endpoints.
/// Tests save slot management, versioning, and data persistence operations.
/// </summary>
public class SaveLoadTestHandler : BaseHttpTestHandler
{
    // Constants for test data
    private const string TEST_GAME_ID = "arcadia";
    private const string TEST_OWNER_TYPE = "ACCOUNT";

    public override ServiceTest[] GetServiceTests() =>
    [
        // Slot Management Tests
        new ServiceTest(TestCreateSlot, "CreateSlot", "SaveLoad", "Test creating a new save slot"),
        new ServiceTest(TestGetSlot, "GetSlot", "SaveLoad", "Test getting slot metadata"),
        new ServiceTest(TestGetSlotNotFound, "GetSlotNotFound", "SaveLoad", "Test getting non-existent slot returns 404"),
        new ServiceTest(TestListSlots, "ListSlots", "SaveLoad", "Test listing slots for owner"),
        new ServiceTest(TestRenameSlot, "RenameSlot", "SaveLoad", "Test renaming a save slot"),
        new ServiceTest(TestDeleteSlot, "DeleteSlot", "SaveLoad", "Test deleting a save slot"),

        // Save/Load Tests
        new ServiceTest(TestSaveAndLoad, "SaveAndLoad", "SaveLoad", "Test saving and loading data"),
        new ServiceTest(TestSaveWithCompression, "SaveWithCompression", "SaveLoad", "Test saving data with compression"),
        new ServiceTest(TestLoadNonExistentSlot, "LoadNotFound", "SaveLoad", "Test loading from non-existent slot returns 404"),
        new ServiceTest(TestSaveMultipleVersions, "SaveMultipleVersions", "SaveLoad", "Test saving multiple versions"),

        // Version Management Tests
        new ServiceTest(TestListVersions, "ListVersions", "SaveLoad", "Test listing versions in a slot"),
        new ServiceTest(TestPinVersion, "PinVersion", "SaveLoad", "Test pinning a version as checkpoint"),
        new ServiceTest(TestUnpinVersion, "UnpinVersion", "SaveLoad", "Test unpinning a version"),
        new ServiceTest(TestDeleteVersion, "DeleteVersion", "SaveLoad", "Test deleting a specific version"),
        new ServiceTest(TestLoadSpecificVersion, "LoadSpecificVersion", "SaveLoad", "Test loading a specific version number"),

        // Query Tests
        new ServiceTest(TestQuerySaves, "QuerySaves", "SaveLoad", "Test querying saves with filters"),

        // Delta Save Tests
        new ServiceTest(TestSaveDelta, "SaveDelta", "SaveLoad", "Test saving delta/incremental changes"),

        // Verify Integrity Tests
        new ServiceTest(TestVerifyIntegrity, "VerifyIntegrity", "SaveLoad", "Test verifying save data integrity"),

        // Bulk Operations
        new ServiceTest(TestBulkDeleteSlots, "BulkDeleteSlots", "SaveLoad", "Test bulk deleting multiple slots"),
    ];

    #region Slot Management Tests

    private static async Task<TestResult> TestCreateSlot(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var slotName = GenerateTestSlug("test-slot");
            var ownerId = Guid.NewGuid().ToString();

            var request = new CreateSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                Category = SaveCategory.MANUAL,
                MaxVersions = 5
            };

            var response = await saveLoadClient.CreateSlotAsync(request);

            if (response == null)
                return TestResult.Failed("CreateSlot returned null");

            if (string.IsNullOrEmpty(response.SlotId))
                return TestResult.Failed("CreateSlot did not return a slot ID");

            // Clean up
            await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            return TestResult.Successful($"CreateSlot successful: {response.SlotId}");
        }, "Create slot");

    private static async Task<TestResult> TestGetSlot(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var slotName = GenerateTestSlug("get-slot");
            var ownerId = Guid.NewGuid().ToString();

            // Create slot first
            await saveLoadClient.CreateSlotAsync(new CreateSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                Category = SaveCategory.MANUAL
            });

            // Get the slot
            var request = new GetSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            };

            var response = await saveLoadClient.GetSlotAsync(request);

            // Clean up
            await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            if (response == null)
                return TestResult.Failed("GetSlot returned null");

            if (response.SlotName != slotName)
                return TestResult.Failed($"GetSlot returned wrong slot name: {response.SlotName}");

            return TestResult.Successful($"GetSlot successful: {response.SlotName}");
        }, "Get slot");

    private static async Task<TestResult> TestGetSlotNotFound(ITestClient client, string[] args) =>
        await ExecuteExpectingStatusAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();

            await saveLoadClient.GetSlotAsync(new GetSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = Guid.NewGuid().ToString(),
                OwnerType = OwnerType.ACCOUNT,
                SlotName = $"nonexistent-{Guid.NewGuid()}"
            });
        }, 404, "Get non-existent slot");

    private static async Task<TestResult> TestListSlots(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var ownerId = Guid.NewGuid().ToString();

            // Create a few slots
            for (var i = 0; i < 3; i++)
            {
                await saveLoadClient.CreateSlotAsync(new CreateSlotRequest
                {
                    GameId = TEST_GAME_ID,
                    OwnerId = ownerId,
                    OwnerType = OwnerType.ACCOUNT,
                    SlotName = $"list-test-{i}",
                    Category = SaveCategory.MANUAL
                });
            }

            // List slots
            var response = await saveLoadClient.ListSlotsAsync(new ListSlotsRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT
            });

            // Clean up
            for (var i = 0; i < 3; i++)
            {
                await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
                {
                    GameId = TEST_GAME_ID,
                    OwnerId = ownerId,
                    OwnerType = OwnerType.ACCOUNT,
                    SlotName = $"list-test-{i}"
                });
            }

            if (response == null)
                return TestResult.Failed("ListSlots returned null");

            var count = response.Slots?.Count ?? 0;
            return TestResult.Successful($"ListSlots returned {count} slots");
        }, "List slots");

    private static async Task<TestResult> TestRenameSlot(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var oldName = GenerateTestSlug("old-name");
            var newName = GenerateTestSlug("new-name");
            var ownerId = Guid.NewGuid().ToString();

            // Create slot
            await saveLoadClient.CreateSlotAsync(new CreateSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = oldName,
                Category = SaveCategory.MANUAL
            });

            // Rename it
            var response = await saveLoadClient.RenameSlotAsync(new RenameSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                OldSlotName = oldName,
                NewSlotName = newName
            });

            // Clean up with new name
            await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = newName
            });

            if (response == null)
                return TestResult.Failed("RenameSlot returned null");

            if (response.SlotName != newName)
                return TestResult.Failed($"RenameSlot returned wrong name: {response.SlotName}");

            return TestResult.Successful($"RenameSlot successful: {oldName} -> {newName}");
        }, "Rename slot");

    private static async Task<TestResult> TestDeleteSlot(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var slotName = GenerateTestSlug("delete-slot");
            var ownerId = Guid.NewGuid().ToString();

            // Create slot
            await saveLoadClient.CreateSlotAsync(new CreateSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                Category = SaveCategory.MANUAL
            });

            // Delete it
            var response = await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            if (response == null)
                return TestResult.Failed("DeleteSlot returned null");

            if (!response.Deleted)
                return TestResult.Failed("DeleteSlot returned deleted=false");

            return TestResult.Successful($"DeleteSlot successful, freed {response.BytesFreed} bytes");
        }, "Delete slot");

    #endregion

    #region Save/Load Tests

    private static async Task<TestResult> TestSaveAndLoad(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var slotName = GenerateTestSlug("save-load");
            var ownerId = Guid.NewGuid().ToString();
            var testData = """{"player":{"name":"TestHero","level":42},"inventory":[1,2,3]}""";

            // Save data
            var saveResponse = await saveLoadClient.SaveAsync(new SaveRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                Category = SaveCategory.MANUAL,
                Data = testData
            });

            if (saveResponse == null)
                return TestResult.Failed("Save returned null");

            // Load data
            var loadResponse = await saveLoadClient.LoadAsync(new LoadRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            // Clean up
            await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            if (loadResponse == null)
                return TestResult.Failed("Load returned null");

            if (loadResponse.Data != testData)
                return TestResult.Failed("Load returned different data than saved");

            return TestResult.Successful($"Save and Load successful, version {saveResponse.VersionNumber}");
        }, "Save and load");

    private static async Task<TestResult> TestSaveWithCompression(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var slotName = GenerateTestSlug("compress");
            var ownerId = Guid.NewGuid().ToString();
            // Large repetitive data compresses well
            var testData = string.Concat(Enumerable.Repeat("""{"x":1234567890}""", 100));

            // Save with GZIP
            var saveResponse = await saveLoadClient.SaveAsync(new SaveRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                Category = SaveCategory.AUTO,
                Data = testData,
                CompressionType = CompressionType.GZIP
            });

            if (saveResponse == null)
                return TestResult.Failed("Save with compression returned null");

            // Load and verify
            var loadResponse = await saveLoadClient.LoadAsync(new LoadRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            // Clean up
            await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            if (loadResponse?.Data != testData)
                return TestResult.Failed("Decompressed data does not match original");

            return TestResult.Successful($"Save with compression successful, size: {saveResponse.SizeBytes} bytes");
        }, "Save with compression");

    private static async Task<TestResult> TestLoadNonExistentSlot(ITestClient client, string[] args) =>
        await ExecuteExpectingStatusAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();

            await saveLoadClient.LoadAsync(new LoadRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = Guid.NewGuid().ToString(),
                OwnerType = OwnerType.ACCOUNT,
                SlotName = $"nonexistent-{Guid.NewGuid()}"
            });
        }, 404, "Load from non-existent slot");

    private static async Task<TestResult> TestSaveMultipleVersions(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var slotName = GenerateTestSlug("multi-ver");
            var ownerId = Guid.NewGuid().ToString();

            // Save multiple versions
            for (var i = 1; i <= 3; i++)
            {
                await saveLoadClient.SaveAsync(new SaveRequest
                {
                    GameId = TEST_GAME_ID,
                    OwnerId = ownerId,
                    OwnerType = OwnerType.ACCOUNT,
                    SlotName = slotName,
                    Category = SaveCategory.MANUAL,
                    Data = $"{{\"version\":{i}}}"
                });
            }

            // List versions
            var versionsResponse = await saveLoadClient.ListVersionsAsync(new ListVersionsRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            // Clean up
            await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            var count = versionsResponse?.Versions?.Count ?? 0;
            return TestResult.Successful($"Multiple versions saved successfully, {count} versions in slot");
        }, "Save multiple versions");

    #endregion

    #region Version Management Tests

    private static async Task<TestResult> TestListVersions(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var slotName = GenerateTestSlug("list-ver");
            var ownerId = Guid.NewGuid().ToString();

            // Create slot with some versions
            for (var i = 0; i < 3; i++)
            {
                await saveLoadClient.SaveAsync(new SaveRequest
                {
                    GameId = TEST_GAME_ID,
                    OwnerId = ownerId,
                    OwnerType = OwnerType.ACCOUNT,
                    SlotName = slotName,
                    Category = SaveCategory.MANUAL,
                    Data = $"{{\"i\":{i}}}"
                });
            }

            var response = await saveLoadClient.ListVersionsAsync(new ListVersionsRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            // Clean up
            await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            if (response == null)
                return TestResult.Failed("ListVersions returned null");

            var count = response.Versions?.Count ?? 0;
            return TestResult.Successful($"ListVersions returned {count} versions");
        }, "List versions");

    private static async Task<TestResult> TestPinVersion(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var slotName = GenerateTestSlug("pin-ver");
            var ownerId = Guid.NewGuid().ToString();

            // Save a version
            var saveResponse = await saveLoadClient.SaveAsync(new SaveRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                Category = SaveCategory.MANUAL,
                Data = """{"checkpoint":"boss-fight"}"""
            });

            // Pin it
            var pinResponse = await saveLoadClient.PinVersionAsync(new PinVersionRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                VersionNumber = saveResponse?.VersionNumber ?? 1,
                CheckpointName = "Boss Fight"
            });

            // Clean up
            await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            if (pinResponse == null)
                return TestResult.Failed("PinVersion returned null");

            if (!pinResponse.IsPinned)
                return TestResult.Failed("PinVersion did not set pinned flag");

            return TestResult.Successful($"PinVersion successful: checkpoint '{pinResponse.CheckpointName}'");
        }, "Pin version");

    private static async Task<TestResult> TestUnpinVersion(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var slotName = GenerateTestSlug("unpin-ver");
            var ownerId = Guid.NewGuid().ToString();

            // Save and pin
            var saveResponse = await saveLoadClient.SaveAsync(new SaveRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                Category = SaveCategory.MANUAL,
                Data = """{"x":1}""",
                Pinned = true,
                CheckpointName = "To Unpin"
            });

            // Unpin it
            var unpinResponse = await saveLoadClient.UnpinVersionAsync(new UnpinVersionRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                VersionNumber = saveResponse?.VersionNumber ?? 1
            });

            // Clean up
            await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            if (unpinResponse == null)
                return TestResult.Failed("UnpinVersion returned null");

            if (unpinResponse.IsPinned)
                return TestResult.Failed("UnpinVersion did not clear pinned flag");

            return TestResult.Successful("UnpinVersion successful");
        }, "Unpin version");

    private static async Task<TestResult> TestDeleteVersion(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var slotName = GenerateTestSlug("del-ver");
            var ownerId = Guid.NewGuid().ToString();

            // Save two versions
            await saveLoadClient.SaveAsync(new SaveRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                Category = SaveCategory.MANUAL,
                Data = """{"v":1}"""
            });

            await saveLoadClient.SaveAsync(new SaveRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                Category = SaveCategory.MANUAL,
                Data = """{"v":2}"""
            });

            // Delete version 1
            var deleteResponse = await saveLoadClient.DeleteVersionAsync(new DeleteVersionRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                VersionNumber = 1
            });

            // Clean up
            await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            if (deleteResponse == null)
                return TestResult.Failed("DeleteVersion returned null");

            if (!deleteResponse.Deleted)
                return TestResult.Failed("DeleteVersion did not delete");

            return TestResult.Successful($"DeleteVersion successful, freed {deleteResponse.BytesFreed} bytes");
        }, "Delete version");

    private static async Task<TestResult> TestLoadSpecificVersion(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var slotName = GenerateTestSlug("load-ver");
            var ownerId = Guid.NewGuid().ToString();

            // Save two versions
            await saveLoadClient.SaveAsync(new SaveRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                Category = SaveCategory.MANUAL,
                Data = """{"version":"first"}"""
            });

            await saveLoadClient.SaveAsync(new SaveRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                Category = SaveCategory.MANUAL,
                Data = """{"version":"second"}"""
            });

            // Load version 1 specifically
            var loadResponse = await saveLoadClient.LoadAsync(new LoadRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                VersionNumber = 1
            });

            // Clean up
            await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            if (loadResponse == null)
                return TestResult.Failed("Load specific version returned null");

            if (!loadResponse.Data?.Contains("first") ?? true)
                return TestResult.Failed("Load returned wrong version data");

            return TestResult.Successful($"Load specific version successful, got version {loadResponse.VersionNumber}");
        }, "Load specific version");

    #endregion

    #region Query Tests

    private static async Task<TestResult> TestQuerySaves(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var ownerId = Guid.NewGuid().ToString();
            var uniqueTag = $"query-tag-{Guid.NewGuid():N}".Substring(0, 20);

            // Create a slot with a tag
            await saveLoadClient.CreateSlotAsync(new CreateSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = "query-test",
                Category = SaveCategory.MANUAL,
                Tags = [uniqueTag]
            });

            // Query by owner
            var response = await saveLoadClient.QuerySavesAsync(new QuerySavesRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT
            });

            // Clean up
            await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = "query-test"
            });

            if (response == null)
                return TestResult.Failed("QuerySaves returned null");

            var count = response.Results?.Count ?? 0;
            return TestResult.Successful($"QuerySaves returned {count} results, total: {response.TotalCount}");
        }, "Query saves");

    #endregion

    #region Delta Save Tests

    private static async Task<TestResult> TestSaveDelta(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var slotName = GenerateTestSlug("delta");
            var ownerId = Guid.NewGuid().ToString();

            // Save base version
            var baseResponse = await saveLoadClient.SaveAsync(new SaveRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                Category = SaveCategory.MANUAL,
                Data = """{"player":{"name":"Hero","level":1,"gold":100}}"""
            });

            if (baseResponse == null)
                return TestResult.Failed("Base save failed");

            // Save delta (incremental changes)
            var deltaResponse = await saveLoadClient.SaveDeltaAsync(new SaveDeltaRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                BaseVersionNumber = baseResponse.VersionNumber,
                Data = """{"player":{"name":"Hero","level":2,"gold":150}}""",
                DeltaAlgorithm = DeltaAlgorithm.JSON_PATCH
            });

            // Load with delta reconstruction
            var loadResponse = await saveLoadClient.LoadWithDeltasAsync(new LoadRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            // Clean up
            await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            if (deltaResponse == null)
                return TestResult.Failed("SaveDelta returned null");

            if (loadResponse == null || !loadResponse.Data?.Contains("level\":2") == true)
                return TestResult.Failed("Delta reconstruction failed");

            return TestResult.Successful($"SaveDelta successful, delta version {deltaResponse.VersionNumber}");
        }, "Save delta");

    #endregion

    #region Verify Integrity Tests

    private static async Task<TestResult> TestVerifyIntegrity(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var slotName = GenerateTestSlug("verify");
            var ownerId = Guid.NewGuid().ToString();
            var testData = """{"integrity":"test"}""";

            // Save data
            await saveLoadClient.SaveAsync(new SaveRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName,
                Category = SaveCategory.MANUAL,
                Data = testData
            });

            // Verify integrity
            var response = await saveLoadClient.VerifyIntegrityAsync(new VerifyIntegrityRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            // Clean up
            await saveLoadClient.DeleteSlotAsync(new DeleteSlotRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotName = slotName
            });

            if (response == null)
                return TestResult.Failed("VerifyIntegrity returned null");

            if (!response.Valid)
                return TestResult.Failed($"VerifyIntegrity failed: {string.Join(", ", response.Errors ?? [])}");

            return TestResult.Successful($"VerifyIntegrity successful, {response.VersionsChecked} versions checked");
        }, "Verify integrity");

    #endregion

    #region Bulk Operations

    private static async Task<TestResult> TestBulkDeleteSlots(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var saveLoadClient = GetServiceClient<ISaveLoadClient>();
            var ownerId = Guid.NewGuid().ToString();
            var slotNames = new[] { "bulk-1", "bulk-2", "bulk-3" };

            // Create slots
            foreach (var name in slotNames)
            {
                await saveLoadClient.CreateSlotAsync(new CreateSlotRequest
                {
                    GameId = TEST_GAME_ID,
                    OwnerId = ownerId,
                    OwnerType = OwnerType.ACCOUNT,
                    SlotName = name,
                    Category = SaveCategory.MANUAL
                });
            }

            // Bulk delete
            var response = await saveLoadClient.BulkDeleteSlotsAsync(new BulkDeleteSlotsRequest
            {
                GameId = TEST_GAME_ID,
                OwnerId = ownerId,
                OwnerType = OwnerType.ACCOUNT,
                SlotNames = slotNames.ToList()
            });

            if (response == null)
                return TestResult.Failed("BulkDeleteSlots returned null");

            return TestResult.Successful($"BulkDeleteSlots successful: {response.SlotsDeleted} slots deleted, {response.TotalBytesFreed} bytes freed");
        }, "Bulk delete slots");

    #endregion
}
