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
    private static readonly string TestGameId = "arcadia";

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
            ? ContainerConstraintModel.Slot_and_weight
            : ContainerConstraintModel.Slot_only;

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
    /// </summary>
    private static async Task<AddItemResponse> CreateAndAddItemAsync(
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
        return await inventoryClient.AddItemToContainerAsync(new AddItemRequest
        {
            InstanceId = instance.InstanceId,
            ContainerId = containerId
        });
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
                ConstraintModel = ContainerConstraintModel.Slot_only,
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
                ConstraintModel = ContainerConstraintModel.Slot_and_weight,
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
                ConstraintModel = ContainerConstraintModel.Slot_and_weight,
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
                ConstraintModel = ContainerConstraintModel.Slot_only,
                MaxSlots = 20
            });
            await inventoryClient.CreateContainerAsync(new CreateContainerRequest
            {
                OwnerId = characterId,
                OwnerType = ContainerOwnerType.Character,
                ContainerType = "bank",
                ConstraintModel = ContainerConstraintModel.Slot_only,
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

            if (moveResult.InstanceId != addResult.InstanceId)
                return TestResult.Failed("Move returned different instance ID");

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
}
