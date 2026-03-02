using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for Inventory service HTTP API endpoints.
/// Tests realistic inventory scenarios demonstrating:
/// - Container creation and retrieval with constraint enforcement
/// - Item placement with slot/weight/category validation
/// - Stack splitting and merging across containers
/// - Inter-container transfers and moves
/// - Cross-service integration with lib-item for template resolution
/// </summary>
public class InventoryTestHandler : BaseHttpTestHandler
{
    private static readonly string TestGameId = "test-game";

    public override ServiceTest[] GetServiceTests() =>
    [
        // Container CRUD
        new ServiceTest(TestCreateContainer, "CreateContainer", "Inventory",
            "Create a slot-based inventory container"),
        new ServiceTest(TestGetOrCreateContainer, "GetOrCreateContainer", "Inventory",
            "Test lazy container creation pattern"),
        new ServiceTest(TestListContainers, "ListContainers", "Inventory",
            "List containers for an owner"),

        // Item Operations
        new ServiceTest(TestAddItemToContainer, "AddItem", "Inventory",
            "Create item template, instantiate, and add to container"),
        new ServiceTest(TestSlotLimitEnforcement, "SlotLimit", "Inventory",
            "Verify slot limit prevents adding beyond capacity"),
        new ServiceTest(TestWeightLimitEnforcement, "WeightLimit", "Inventory",
            "Verify weight limit prevents overloading container"),

        // Stack Operations
        new ServiceTest(TestSplitAndMergeStacks, "SplitMerge", "Inventory",
            "Split a stack then merge the halves back together"),

        // Movement
        new ServiceTest(TestMoveItemBetweenContainers, "MoveItem", "Inventory",
            "Move item from one container to another"),
        new ServiceTest(TestTransferItemToAnotherOwner, "TransferItem", "Inventory",
            "Transfer item to a container owned by another entity"),

        // Additional Coverage Tests
        new ServiceTest(TestUpdateContainer, "UpdateContainer", "Inventory",
            "Test updating container constraints"),
        new ServiceTest(TestDeleteContainer, "DeleteContainer", "Inventory",
            "Test deleting an empty container"),
        new ServiceTest(TestRemoveItemFromContainer, "RemoveItem", "Inventory",
            "Test removing an item from a container"),
        new ServiceTest(TestQueryItems, "QueryItems", "Inventory",
            "Test querying items by criteria"),
        new ServiceTest(TestCountItems, "CountItems", "Inventory",
            "Test counting items by template"),
        new ServiceTest(TestHasItems, "HasItems", "Inventory",
            "Test checking item requirements"),
        new ServiceTest(TestFindSpace, "FindSpace", "Inventory",
            "Test finding space for an item"),
    ];

    // =========================================================================
    // Helper Methods
    // =========================================================================

    /// <summary>
    /// Creates a test realm for inventory scenarios.
    /// </summary>
    private static async Task<RealmResponse> CreateInventoryTestRealmAsync(string suffix)
    {
        return await CreateTestRealmAsync("INV", "Inventory", suffix);
    }

    /// <summary>
    /// Creates a stackable item template for testing.
    /// </summary>
    private static async Task<ItemTemplateResponse> CreateStackableTemplateAsync(
        string suffix,
        double weight = 1.0,
        int maxStackSize = 99)
    {
        var itemClient = GetServiceClient<IItemClient>();
        return await itemClient.CreateItemTemplateAsync(new CreateItemTemplateRequest
        {
            Code = $"inv_test_material_{DateTime.Now.Ticks}_{suffix}".ToLowerInvariant(),
            GameId = TestGameId,
            Name = $"Test Material {suffix}",
            Description = "Stackable test material for inventory tests",
            Category = ItemCategory.Material,
            QuantityModel = QuantityModel.Discrete,
            MaxStackSize = maxStackSize,
            Weight = weight,
            Scope = ItemScope.Global,
            Tradeable = true,
            Destroyable = true
        });
    }

    /// <summary>
    /// Creates a unique (non-stackable) weapon template.
    /// </summary>
    private static async Task<ItemTemplateResponse> CreateUniqueWeaponTemplateAsync(
        string suffix,
        double weight = 5.0)
    {
        var itemClient = GetServiceClient<IItemClient>();
        return await itemClient.CreateItemTemplateAsync(new CreateItemTemplateRequest
        {
            Code = $"inv_test_weapon_{DateTime.Now.Ticks}_{suffix}".ToLowerInvariant(),
            GameId = TestGameId,
            Name = $"Test Sword {suffix}",
            Description = "Unique weapon for inventory tests",
            Category = ItemCategory.Weapon,
            QuantityModel = QuantityModel.Unique,
            MaxStackSize = 1,
            Weight = weight,
            Scope = ItemScope.Global,
            Tradeable = true,
            Destroyable = true
        });
    }

    /// <summary>
    /// Creates a slot-based container for a character.
    /// </summary>
    private static async Task<ContainerResponse> CreateSlotContainerAsync(
        Guid ownerId,
        int maxSlots = 10,
        double? maxWeight = null,
        string containerType = "inventory")
    {
        var inventoryClient = GetServiceClient<IInventoryClient>();
        var constraintModel = maxWeight.HasValue
            ? ContainerConstraintModel.SlotAndWeight
            : ContainerConstraintModel.SlotOnly;

        return await inventoryClient.CreateContainerAsync(new CreateContainerRequest
        {
            OwnerId = ownerId,
            OwnerType = ContainerOwnerType.Character,
            ContainerType = containerType,
            ConstraintModel = constraintModel,
            MaxSlots = maxSlots,
            MaxWeight = maxWeight
        });
    }

    /// <summary>
    /// Creates an item instance and adds it to a container.
    /// Returns the instance ID and the add response.
    /// </summary>
    private static async Task<(Guid InstanceId, AddItemResponse Response)> CreateAndAddItemAsync(
        Guid templateId,
        Guid containerId,
        Guid realmId,
        int quantity = 1)
    {
        var itemClient = GetServiceClient<IItemClient>();
        var inventoryClient = GetServiceClient<IInventoryClient>();

        // Create the item instance via lib-item
        var instance = await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
        {
            TemplateId = templateId,
            ContainerId = containerId,
            RealmId = realmId,
            Quantity = quantity
        });

        // Add to container via lib-inventory
        var addResponse = await inventoryClient.AddItemToContainerAsync(new AddItemRequest
        {
            InstanceId = instance.InstanceId,
            ContainerId = containerId
        });

        return (instance.InstanceId, addResponse);
    }

    // =========================================================================
    // Container CRUD Tests
    // =========================================================================

    private static async Task<TestResult> TestCreateContainer(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateInventoryTestRealmAsync("CREATE");
            var characterId = Guid.NewGuid();

            var inventoryClient = GetServiceClient<IInventoryClient>();
            var container = await inventoryClient.CreateContainerAsync(new CreateContainerRequest
            {
                OwnerId = characterId,
                OwnerType = ContainerOwnerType.Character,
                ContainerType = "inventory",
                ConstraintModel = ContainerConstraintModel.SlotOnly,
                MaxSlots = 20
            });

            if (container.ContainerId == Guid.Empty)
                return TestResult.Failed("Container creation returned empty ID");

            if (container.MaxSlots != 20)
                return TestResult.Failed($"Expected maxSlots=20, got: {container.MaxSlots}");

            // Verify we can retrieve it
            var retrieved = await inventoryClient.GetContainerAsync(new GetContainerRequest
            {
                ContainerId = container.ContainerId,
                IncludeContents = false
            });

            if (retrieved.Container.ContainerId != container.ContainerId)
                return TestResult.Failed("Retrieved container ID mismatch");

            return TestResult.Successful(
                $"Container created: ID={container.ContainerId}, type=inventory, slots=20");
        }, "Create container");

    private static async Task<TestResult> TestGetOrCreateContainer(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var characterId = Guid.NewGuid();
            var inventoryClient = GetServiceClient<IInventoryClient>();

            // First call creates
            var first = await inventoryClient.GetOrCreateContainerAsync(new GetOrCreateContainerRequest
            {
                OwnerId = characterId,
                OwnerType = ContainerOwnerType.Character,
                ContainerType = "bank",
                MaxSlots = 50,
                ConstraintModel = ContainerConstraintModel.SlotAndWeight,
                MaxWeight = 500.0
            });

            if (first.ContainerId == Guid.Empty)
                return TestResult.Failed("First GetOrCreate returned empty ID");

            // Second call retrieves the same
            var second = await inventoryClient.GetOrCreateContainerAsync(new GetOrCreateContainerRequest
            {
                OwnerId = characterId,
                OwnerType = ContainerOwnerType.Character,
                ContainerType = "bank",
                MaxSlots = 50,
                ConstraintModel = ContainerConstraintModel.SlotAndWeight,
                MaxWeight = 500.0
            });

            if (first.ContainerId != second.ContainerId)
                return TestResult.Failed(
                    $"GetOrCreate returned different IDs: first={first.ContainerId}, second={second.ContainerId}");

            return TestResult.Successful(
                $"GetOrCreate idempotent: ID={first.ContainerId}, type=bank, slots=50, weight=500");
        }, "GetOrCreate container");

    private static async Task<TestResult> TestListContainers(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var characterId = Guid.NewGuid();
            var inventoryClient = GetServiceClient<IInventoryClient>();

            // Create multiple containers of different types
            await inventoryClient.CreateContainerAsync(new CreateContainerRequest
            {
                OwnerId = characterId,
                OwnerType = ContainerOwnerType.Character,
                ContainerType = "inventory",
                ConstraintModel = ContainerConstraintModel.SlotOnly,
                MaxSlots = 20
            });
            await inventoryClient.CreateContainerAsync(new CreateContainerRequest
            {
                OwnerId = characterId,
                OwnerType = ContainerOwnerType.Character,
                ContainerType = "bank",
                ConstraintModel = ContainerConstraintModel.SlotOnly,
                MaxSlots = 100
            });

            // List all containers for this owner
            var result = await inventoryClient.ListContainersAsync(new ListContainersRequest
            {
                OwnerId = characterId,
                OwnerType = ContainerOwnerType.Character
            });

            if (result.Containers.Count < 2)
                return TestResult.Failed($"Expected at least 2 containers, got: {result.Containers.Count}");

            return TestResult.Successful(
                $"Listed {result.Containers.Count} containers for character {characterId}");
        }, "List containers");

    // =========================================================================
    // Item Operation Tests
    // =========================================================================

    private static async Task<TestResult> TestAddItemToContainer(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateInventoryTestRealmAsync("ADD_ITEM");
            var characterId = Guid.NewGuid();

            // Create container
            var container = await CreateSlotContainerAsync(characterId);

            // Create item template
            var template = await CreateStackableTemplateAsync("add");

            // Create item instance and add to container
            var addResult = await CreateAndAddItemAsync(
                template.TemplateId, container.ContainerId, realm.RealmId, quantity: 10);

            if (addResult.InstanceId == Guid.Empty)
                return TestResult.Failed("AddItem returned empty instance ID");

            // Verify container contents
            var inventoryClient = GetServiceClient<IInventoryClient>();
            var contents = await inventoryClient.GetContainerAsync(new GetContainerRequest
            {
                ContainerId = container.ContainerId,
                IncludeContents = true
            });

            if (contents.Items == null || contents.Items.Count == 0)
                return TestResult.Failed("Container contents empty after adding item");

            return TestResult.Successful(
                $"Item added: template={template.Code}, quantity=10, container={container.ContainerId}");
        }, "Add item to container");

    private static async Task<TestResult> TestSlotLimitEnforcement(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateInventoryTestRealmAsync("SLOT_LIMIT");
            var characterId = Guid.NewGuid();

            // Create a container with only 2 slots
            var container = await CreateSlotContainerAsync(characterId, maxSlots: 2);

            // Create unique weapons (each takes 1 slot)
            var template = await CreateUniqueWeaponTemplateAsync("slot1");

            // Fill both slots
            var itemClient = GetServiceClient<IItemClient>();
            var inventoryClient = GetServiceClient<IInventoryClient>();

            for (int i = 0; i < 2; i++)
            {
                var instance = await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
                {
                    TemplateId = template.TemplateId,
                    ContainerId = container.ContainerId,
                    RealmId = realm.RealmId,
                    Quantity = 1
                });
                await inventoryClient.AddItemToContainerAsync(new AddItemRequest
                {
                    InstanceId = instance.InstanceId,
                    ContainerId = container.ContainerId
                });
            }

            // Third item should fail - slots full
            var thirdInstance = await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
            {
                TemplateId = template.TemplateId,
                ContainerId = container.ContainerId,
                RealmId = realm.RealmId,
                Quantity = 1
            });

            return await ExecuteExpectingAnyStatusAsync(
                async () => await inventoryClient.AddItemToContainerAsync(new AddItemRequest
                {
                    InstanceId = thirdInstance.InstanceId,
                    ContainerId = container.ContainerId
                }),
                new[] { 400, 409 },
                "Slot limit enforcement");
        }, "Slot limit enforcement");

    private static async Task<TestResult> TestWeightLimitEnforcement(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateInventoryTestRealmAsync("WEIGHT_LIMIT");
            var characterId = Guid.NewGuid();

            // Create container with weight limit of 10.0
            var container = await CreateSlotContainerAsync(characterId, maxSlots: 100, maxWeight: 10.0);

            // Create a heavy item (weight = 8.0 each)
            var template = await CreateUniqueWeaponTemplateAsync("heavy", weight: 8.0);

            var itemClient = GetServiceClient<IItemClient>();
            var inventoryClient = GetServiceClient<IInventoryClient>();

            // First item fits (8.0 < 10.0)
            var first = await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
            {
                TemplateId = template.TemplateId,
                ContainerId = container.ContainerId,
                RealmId = realm.RealmId,
                Quantity = 1
            });
            await inventoryClient.AddItemToContainerAsync(new AddItemRequest
            {
                InstanceId = first.InstanceId,
                ContainerId = container.ContainerId
            });

            // Second item should fail (8.0 + 8.0 = 16.0 > 10.0)
            var second = await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
            {
                TemplateId = template.TemplateId,
                ContainerId = container.ContainerId,
                RealmId = realm.RealmId,
                Quantity = 1
            });

            return await ExecuteExpectingAnyStatusAsync(
                async () => await inventoryClient.AddItemToContainerAsync(new AddItemRequest
                {
                    InstanceId = second.InstanceId,
                    ContainerId = container.ContainerId
                }),
                new[] { 400, 409 },
                "Weight limit enforcement");
        }, "Weight limit enforcement");

    // =========================================================================
    // Stack Operation Tests
    // =========================================================================

    private static async Task<TestResult> TestSplitAndMergeStacks(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateInventoryTestRealmAsync("SPLIT_MERGE");
            var characterId = Guid.NewGuid();
            var inventoryClient = GetServiceClient<IInventoryClient>();

            // Create container with room
            var container = await CreateSlotContainerAsync(characterId, maxSlots: 10);

            // Create stackable template and add 20 items
            var template = await CreateStackableTemplateAsync("split");
            var addResult = await CreateAndAddItemAsync(
                template.TemplateId, container.ContainerId, realm.RealmId, quantity: 20);

            // Split the stack: 20 → 12 + 8
            var splitResult = await inventoryClient.SplitStackAsync(new SplitStackRequest
            {
                InstanceId = addResult.InstanceId,
                Quantity = 8
            });

            if (splitResult.NewInstanceId == Guid.Empty)
                return TestResult.Failed("Split returned empty new instance ID");

            if (splitResult.OriginalQuantity != 12)
                return TestResult.Failed(
                    $"Expected original to have 12, got: {splitResult.OriginalQuantity}");

            if (splitResult.NewQuantity != 8)
                return TestResult.Failed(
                    $"Expected new stack to have 8, got: {splitResult.NewQuantity}");

            // Merge them back: 12 + 8 → 20
            var mergeResult = await inventoryClient.MergeStacksAsync(new MergeStacksRequest
            {
                SourceInstanceId = splitResult.NewInstanceId,
                TargetInstanceId = addResult.InstanceId
            });

            if (mergeResult.NewQuantity != 20)
                return TestResult.Failed(
                    $"Expected merged stack of 20, got: {mergeResult.NewQuantity}");

            return TestResult.Successful(
                $"Split 20→12+8, merged back to 20. Template={template.Code}");
        }, "Split and merge stacks");

    // =========================================================================
    // Movement Tests
    // =========================================================================

    private static async Task<TestResult> TestMoveItemBetweenContainers(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateInventoryTestRealmAsync("MOVE");
            var characterId = Guid.NewGuid();
            var inventoryClient = GetServiceClient<IInventoryClient>();

            // Create two containers for the same owner
            var source = await CreateSlotContainerAsync(characterId, containerType: "inventory");
            var dest = await CreateSlotContainerAsync(characterId, containerType: "bank");

            // Add item to source
            var template = await CreateUniqueWeaponTemplateAsync("move");
            var addResult = await CreateAndAddItemAsync(
                template.TemplateId, source.ContainerId, realm.RealmId);

            // Move to destination
            var moveResult = await inventoryClient.MoveItemAsync(new MoveItemRequest
            {
                InstanceId = addResult.InstanceId,
                TargetContainerId = dest.ContainerId
            });

            if (moveResult.SourceContainerId != source.ContainerId)
                return TestResult.Failed("Move returned wrong source container ID");

            // Verify item is in destination
            var destContents = await inventoryClient.GetContainerAsync(new GetContainerRequest
            {
                ContainerId = dest.ContainerId,
                IncludeContents = true
            });

            if (destContents.Items == null || destContents.Items.Count == 0)
                return TestResult.Failed("Destination container empty after move");

            // Verify source is empty
            var sourceContents = await inventoryClient.GetContainerAsync(new GetContainerRequest
            {
                ContainerId = source.ContainerId,
                IncludeContents = true
            });

            if (sourceContents.Items != null && sourceContents.Items.Count > 0)
                return TestResult.Failed("Source container still has items after move");

            return TestResult.Successful(
                $"Moved {template.Code} from inventory to bank for character {characterId}");
        }, "Move item between containers");

    private static async Task<TestResult> TestTransferItemToAnotherOwner(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateInventoryTestRealmAsync("TRANSFER");
            var sellerId = Guid.NewGuid();
            var buyerId = Guid.NewGuid();
            var inventoryClient = GetServiceClient<IInventoryClient>();

            // Create containers for both characters
            var sellerContainer = await CreateSlotContainerAsync(sellerId);
            var buyerContainer = await CreateSlotContainerAsync(buyerId);

            // Add tradeable item to seller
            var template = await CreateStackableTemplateAsync("trade");
            var addResult = await CreateAndAddItemAsync(
                template.TemplateId, sellerContainer.ContainerId, realm.RealmId, quantity: 5);

            // Transfer to buyer's container
            var transferResult = await inventoryClient.TransferItemAsync(new TransferItemRequest
            {
                InstanceId = addResult.InstanceId,
                TargetContainerId = buyerContainer.ContainerId
            });

            if (transferResult.InstanceId == Guid.Empty)
                return TestResult.Failed("Transfer returned empty instance ID");

            // Verify item is in buyer's container
            var buyerContents = await inventoryClient.GetContainerAsync(new GetContainerRequest
            {
                ContainerId = buyerContainer.ContainerId,
                IncludeContents = true
            });

            if (buyerContents.Items == null || buyerContents.Items.Count == 0)
                return TestResult.Failed("Buyer container empty after transfer");

            return TestResult.Successful(
                $"Transferred {template.Code} (qty=5) from seller={sellerId} to buyer={buyerId}");
        }, "Transfer item to another owner");

    // =========================================================================
    // Additional Coverage Tests
    // =========================================================================

    private static async Task<TestResult> TestUpdateContainer(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var characterId = Guid.NewGuid();
            var inventoryClient = GetServiceClient<IInventoryClient>();

            // Create a container
            var container = await inventoryClient.CreateContainerAsync(new CreateContainerRequest
            {
                OwnerId = characterId,
                OwnerType = ContainerOwnerType.Character,
                ContainerType = "inventory",
                ConstraintModel = ContainerConstraintModel.SlotOnly,
                MaxSlots = 10
            });

            // Update constraints
            var updated = await inventoryClient.UpdateContainerAsync(new UpdateContainerRequest
            {
                ContainerId = container.ContainerId,
                MaxSlots = 25,
                MaxWeight = 100.0
            });

            if (updated.MaxSlots != 25)
                return TestResult.Failed($"Expected maxSlots=25, got: {updated.MaxSlots}");

            if (updated.MaxWeight != 100.0)
                return TestResult.Failed($"Expected maxWeight=100, got: {updated.MaxWeight}");

            return TestResult.Successful(
                $"Updated container {container.ContainerId}: slots 10→25, weight null→100");
        }, "Update container");

    private static async Task<TestResult> TestDeleteContainer(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var characterId = Guid.NewGuid();
            var inventoryClient = GetServiceClient<IInventoryClient>();

            // Create an empty container
            var container = await inventoryClient.CreateContainerAsync(new CreateContainerRequest
            {
                OwnerId = characterId,
                OwnerType = ContainerOwnerType.Character,
                ContainerType = "temp",
                ConstraintModel = ContainerConstraintModel.Unlimited
            });

            // Delete it
            var deleted = await inventoryClient.DeleteContainerAsync(new DeleteContainerRequest
            {
                ContainerId = container.ContainerId
            });

            // Empty container, so itemsHandled should be 0
            if (deleted.ItemsHandled != 0)
                return TestResult.Failed($"Expected 0 items handled for empty container, got: {deleted.ItemsHandled}");

            // Verify it's gone - should return 404
            return await ExecuteExpectingAnyStatusAsync(
                async () => await inventoryClient.GetContainerAsync(new GetContainerRequest
                {
                    ContainerId = container.ContainerId
                }),
                [404],
                "Delete container");
        }, "Delete container");

    private static async Task<TestResult> TestRemoveItemFromContainer(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateInventoryTestRealmAsync("REMOVE");
            var characterId = Guid.NewGuid();
            var inventoryClient = GetServiceClient<IInventoryClient>();

            // Create container and add an item
            var container = await CreateSlotContainerAsync(characterId);
            var template = await CreateUniqueWeaponTemplateAsync("remove");
            var addResult = await CreateAndAddItemAsync(
                template.TemplateId, container.ContainerId, realm.RealmId);

            // Remove the item
            var removeResult = await inventoryClient.RemoveItemFromContainerAsync(new RemoveItemRequest
            {
                InstanceId = addResult.InstanceId
            });

            if (removeResult.PreviousContainerId != container.ContainerId)
                return TestResult.Failed("Remove returned wrong previous container ID");

            // Verify container is empty
            var contents = await inventoryClient.GetContainerAsync(new GetContainerRequest
            {
                ContainerId = container.ContainerId,
                IncludeContents = true
            });

            if (contents.Items != null && contents.Items.Count > 0)
                return TestResult.Failed($"Container still has {contents.Items.Count} items after remove");

            return TestResult.Successful(
                $"Removed item from container {container.ContainerId} (previous={removeResult.PreviousContainerId})");
        }, "Remove item from container");

    private static async Task<TestResult> TestQueryItems(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateInventoryTestRealmAsync("QUERY");
            var characterId = Guid.NewGuid();
            var inventoryClient = GetServiceClient<IInventoryClient>();

            // Create container and add multiple items
            var container = await CreateSlotContainerAsync(characterId, maxSlots: 20);
            var template1 = await CreateStackableTemplateAsync("query1");
            var template2 = await CreateStackableTemplateAsync("query2");

            await CreateAndAddItemAsync(template1.TemplateId, container.ContainerId, realm.RealmId, quantity: 10);
            await CreateAndAddItemAsync(template2.TemplateId, container.ContainerId, realm.RealmId, quantity: 5);

            // Query all items for this owner
            var result = await inventoryClient.QueryItemsAsync(new QueryItemsRequest
            {
                OwnerId = characterId,
                OwnerType = ContainerOwnerType.Character
            });

            if (result.Items == null || result.Items.Count < 2)
                return TestResult.Failed($"Expected at least 2 items, got: {result.Items?.Count ?? 0}");

            // Query by specific template
            var filtered = await inventoryClient.QueryItemsAsync(new QueryItemsRequest
            {
                OwnerId = characterId,
                OwnerType = ContainerOwnerType.Character,
                TemplateId = template1.TemplateId
            });

            if (filtered.Items == null || filtered.Items.Count != 1)
                return TestResult.Failed($"Filtered query: expected 1 item, got: {filtered.Items?.Count ?? 0}");

            return TestResult.Successful(
                $"Query returned {result.Items.Count} items total, {filtered.Items.Count} filtered by template");
        }, "Query items");

    private static async Task<TestResult> TestCountItems(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateInventoryTestRealmAsync("COUNT");
            var characterId = Guid.NewGuid();
            var inventoryClient = GetServiceClient<IInventoryClient>();

            // Create container and add stackable items
            var container = await CreateSlotContainerAsync(characterId);
            var template = await CreateStackableTemplateAsync("count");

            // Add 25 items across two stacks
            await CreateAndAddItemAsync(template.TemplateId, container.ContainerId, realm.RealmId, quantity: 15);
            await CreateAndAddItemAsync(template.TemplateId, container.ContainerId, realm.RealmId, quantity: 10);

            // Count total
            var count = await inventoryClient.CountItemsAsync(new CountItemsRequest
            {
                OwnerId = characterId,
                OwnerType = ContainerOwnerType.Character,
                TemplateId = template.TemplateId
            });

            if (count.TotalQuantity != 25)
                return TestResult.Failed($"Expected total 25, got: {count.TotalQuantity}");

            if (count.StackCount < 2)
                return TestResult.Failed($"Expected at least 2 stacks, got: {count.StackCount}");

            return TestResult.Successful(
                $"Counted {count.TotalQuantity} items across {count.StackCount} stacks");
        }, "Count items");

    private static async Task<TestResult> TestHasItems(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateInventoryTestRealmAsync("HAS");
            var characterId = Guid.NewGuid();
            var inventoryClient = GetServiceClient<IInventoryClient>();

            // Create container and add items
            var container = await CreateSlotContainerAsync(characterId);
            var template = await CreateStackableTemplateAsync("has");
            await CreateAndAddItemAsync(template.TemplateId, container.ContainerId, realm.RealmId, quantity: 20);

            // Check if we have enough (should pass)
            var hasEnough = await inventoryClient.HasItemsAsync(new HasItemsRequest
            {
                OwnerId = characterId,
                OwnerType = ContainerOwnerType.Character,
                Requirements =
                [
                    new ItemRequirement { TemplateId = template.TemplateId, Quantity = 15 }
                ]
            });

            if (!hasEnough.HasAll)
                return TestResult.Failed("HasItems returned false when we have 20 (need 15)");

            // Check if we have more than available (should fail)
            var notEnough = await inventoryClient.HasItemsAsync(new HasItemsRequest
            {
                OwnerId = characterId,
                OwnerType = ContainerOwnerType.Character,
                Requirements =
                [
                    new ItemRequirement { TemplateId = template.TemplateId, Quantity = 50 }
                ]
            });

            if (notEnough.HasAll)
                return TestResult.Failed("HasItems returned true when we have 20 (need 50)");

            return TestResult.Successful(
                $"HasItems correctly: have 20, need 15 = true; need 50 = false");
        }, "Has items");

    private static async Task<TestResult> TestFindSpace(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var characterId = Guid.NewGuid();
            var inventoryClient = GetServiceClient<IInventoryClient>();

            // Create a container with space
            await CreateSlotContainerAsync(characterId, maxSlots: 10);

            // Create a template
            var template = await CreateStackableTemplateAsync("space");

            // Find space for the item across owner's containers
            var space = await inventoryClient.FindSpaceAsync(new FindSpaceRequest
            {
                OwnerId = characterId,
                OwnerType = ContainerOwnerType.Character,
                TemplateId = template.TemplateId,
                Quantity = 5
            });

            if (!space.HasSpace)
                return TestResult.Failed("FindSpace returned hasSpace=false on empty container");

            if (space.Candidates == null || space.Candidates.Count == 0)
                return TestResult.Failed("FindSpace returned no candidates");

            var totalCanFit = space.Candidates.Sum(c => c.CanFitQuantity);
            if (totalCanFit < 5)
                return TestResult.Failed($"Expected canFitQuantity >= 5, got: {totalCanFit}");

            return TestResult.Successful(
                $"FindSpace: hasSpace={space.HasSpace}, candidates={space.Candidates.Count}, totalCanFit={totalCanFit}");
        }, "Find space");
}
