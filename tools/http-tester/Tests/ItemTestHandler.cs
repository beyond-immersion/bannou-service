using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for Item service HTTP API endpoints.
/// Tests item template and instance management including:
/// - Template CRUD and deprecation
/// - Instance creation, modification, binding, and destruction
/// - Container and template-based queries
/// - Batch operations
/// </summary>
public class ItemTestHandler : BaseHttpTestHandler
{
    private static readonly string TestGameId = "test-game";

    public override ServiceTest[] GetServiceTests() =>
    [
        // Template Operations
        new ServiceTest(TestCreateItemTemplate, "CreateTemplate", "Item",
            "Create a new item template"),
        new ServiceTest(TestGetItemTemplate, "GetTemplate", "Item",
            "Get item template by ID and code"),
        new ServiceTest(TestListItemTemplates, "ListTemplates", "Item",
            "List item templates with filtering"),
        new ServiceTest(TestUpdateItemTemplate, "UpdateTemplate", "Item",
            "Update item template properties"),
        new ServiceTest(TestDeprecateItemTemplate, "DeprecateTemplate", "Item",
            "Deprecate an item template"),

        // Instance Operations
        new ServiceTest(TestCreateItemInstance, "CreateInstance", "Item",
            "Create an item instance from template"),
        new ServiceTest(TestGetItemInstance, "GetInstance", "Item",
            "Get item instance by ID"),
        new ServiceTest(TestModifyItemInstance, "ModifyInstance", "Item",
            "Modify item instance quantity and properties"),
        new ServiceTest(TestBindItemInstance, "BindInstance", "Item",
            "Bind item instance to an entity"),
        new ServiceTest(TestDestroyItemInstance, "DestroyInstance", "Item",
            "Destroy an item instance"),

        // Query Operations
        new ServiceTest(TestListItemsByContainer, "ListByContainer", "Item",
            "List items in a container"),
        new ServiceTest(TestListItemsByTemplate, "ListByTemplate", "Item",
            "List instances of a template"),
        new ServiceTest(TestBatchGetItemInstances, "BatchGetInstances", "Item",
            "Batch get multiple item instances"),
    ];

    // =========================================================================
    // Helper Methods
    // =========================================================================

    /// <summary>
    /// Creates a test realm for item scenarios.
    /// </summary>
    private static async Task<RealmResponse> CreateItemTestRealmAsync(string suffix)
    {
        return await CreateTestRealmAsync("ITEM", "Item", suffix);
    }

    /// <summary>
    /// Creates a basic item template.
    /// </summary>
    private static async Task<ItemTemplateResponse> CreateTestTemplateAsync(
        IItemClient itemClient,
        string suffix,
        ItemCategory category = ItemCategory.Material,
        QuantityModel quantityModel = QuantityModel.Discrete,
        int maxStackSize = 99)
    {
        var code = $"item_test_{DateTime.Now.Ticks}_{suffix}".ToLowerInvariant();
        return await itemClient.CreateItemTemplateAsync(new CreateItemTemplateRequest
        {
            Code = code,
            GameId = TestGameId,
            Name = $"Test Item {suffix}",
            Description = $"Test item for {suffix} tests",
            Category = category,
            QuantityModel = quantityModel,
            MaxStackSize = maxStackSize,
            Weight = 1.0,
            Scope = ItemScope.Global,
            Tradeable = true,
            Destroyable = true
        });
    }

    // =========================================================================
    // Template Operations
    // =========================================================================

    private static async Task<TestResult> TestCreateItemTemplate(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var itemClient = GetServiceClient<IItemClient>();

            var template = await CreateTestTemplateAsync(itemClient, "CREATE");

            if (template.TemplateId == Guid.Empty)
                return TestResult.Failed("Template creation returned empty ID");

            if (template.Category != ItemCategory.Material)
                return TestResult.Failed($"Expected Material category, got: {template.Category}");

            return TestResult.Successful(
                $"Template created: ID={template.TemplateId}, code={template.Code}");
        }, "Create item template");

    private static async Task<TestResult> TestGetItemTemplate(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var itemClient = GetServiceClient<IItemClient>();

            var template = await CreateTestTemplateAsync(itemClient, "GET");

            // Get by ID
            var byId = await itemClient.GetItemTemplateAsync(new GetItemTemplateRequest
            {
                TemplateId = template.TemplateId
            });

            if (byId.TemplateId != template.TemplateId)
                return TestResult.Failed("Get by ID returned wrong template");

            // Get by code
            var byCode = await itemClient.GetItemTemplateAsync(new GetItemTemplateRequest
            {
                Code = template.Code,
                GameId = TestGameId
            });

            if (byCode.TemplateId != template.TemplateId)
                return TestResult.Failed("Get by code returned wrong template");

            return TestResult.Successful(
                $"Template retrieved by ID and code: {template.TemplateId}");
        }, "Get item template");

    private static async Task<TestResult> TestListItemTemplates(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var itemClient = GetServiceClient<IItemClient>();

            // Create a few templates
            await CreateTestTemplateAsync(itemClient, "list1", ItemCategory.Weapon);
            await CreateTestTemplateAsync(itemClient, "list2", ItemCategory.Armor);

            // List all for game
            var all = await itemClient.ListItemTemplatesAsync(new ListItemTemplatesRequest
            {
                GameId = TestGameId
            });

            if (all.Templates == null || all.Templates.Count < 2)
                return TestResult.Failed($"Expected at least 2 templates, got: {all.Templates?.Count ?? 0}");

            // Filter by category
            var weapons = await itemClient.ListItemTemplatesAsync(new ListItemTemplatesRequest
            {
                GameId = TestGameId,
                Category = ItemCategory.Weapon
            });

            if (weapons.Templates == null || weapons.Templates.Count == 0)
                return TestResult.Failed("Weapon filter returned no results");

            return TestResult.Successful(
                $"Listed {all.Templates.Count} templates total, {weapons.Templates.Count} weapons");
        }, "List item templates");

    private static async Task<TestResult> TestUpdateItemTemplate(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var itemClient = GetServiceClient<IItemClient>();

            var template = await CreateTestTemplateAsync(itemClient, "UPDATE");

            // Update properties
            var updated = await itemClient.UpdateItemTemplateAsync(new UpdateItemTemplateRequest
            {
                TemplateId = template.TemplateId,
                Name = "Updated Item Name",
                Description = "Updated description",
                Rarity = ItemRarity.Rare
            });

            if (updated.Name != "Updated Item Name")
                return TestResult.Failed($"Expected updated name, got: {updated.Name}");

            if (updated.Rarity != ItemRarity.Rare)
                return TestResult.Failed($"Expected Rare rarity, got: {updated.Rarity}");

            return TestResult.Successful(
                $"Updated template {template.TemplateId}: name, description, rarity");
        }, "Update item template");

    private static async Task<TestResult> TestDeprecateItemTemplate(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var itemClient = GetServiceClient<IItemClient>();

            var template = await CreateTestTemplateAsync(itemClient, "DEPRECATE");

            // Deprecate it
            var deprecated = await itemClient.DeprecateItemTemplateAsync(new DeprecateItemTemplateRequest
            {
                TemplateId = template.TemplateId,
                Reason = "Test deprecation"
            });

            if (!deprecated.IsDeprecated)
                return TestResult.Failed("Template should be deprecated");

            if (deprecated.DeprecatedAt == null)
                return TestResult.Failed("DeprecatedAt should be set");

            return TestResult.Successful(
                $"Deprecated template {template.TemplateId}");
        }, "Deprecate item template");

    // =========================================================================
    // Instance Operations
    // =========================================================================

    private static async Task<TestResult> TestCreateItemInstance(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateItemTestRealmAsync("INST_CREATE");
            var itemClient = GetServiceClient<IItemClient>();
            var containerId = Guid.NewGuid();

            var template = await CreateTestTemplateAsync(itemClient, "instance");

            var instance = await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
            {
                TemplateId = template.TemplateId,
                ContainerId = containerId,
                RealmId = realm.RealmId,
                Quantity = 10
            });

            if (instance.InstanceId == Guid.Empty)
                return TestResult.Failed("Instance creation returned empty ID");

            if (instance.Quantity != 10)
                return TestResult.Failed($"Expected quantity 10, got: {instance.Quantity}");

            if (instance.TemplateId != template.TemplateId)
                return TestResult.Failed("Instance template ID mismatch");

            return TestResult.Successful(
                $"Instance created: ID={instance.InstanceId}, qty=10, template={template.Code}");
        }, "Create item instance");

    private static async Task<TestResult> TestGetItemInstance(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateItemTestRealmAsync("INST_GET");
            var itemClient = GetServiceClient<IItemClient>();
            var containerId = Guid.NewGuid();

            var template = await CreateTestTemplateAsync(itemClient, "getinst");
            var created = await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
            {
                TemplateId = template.TemplateId,
                ContainerId = containerId,
                RealmId = realm.RealmId,
                Quantity = 5
            });

            // Get by ID
            var instance = await itemClient.GetItemInstanceAsync(new GetItemInstanceRequest
            {
                InstanceId = created.InstanceId
            });

            if (instance.InstanceId != created.InstanceId)
                return TestResult.Failed("Get returned wrong instance");

            if (instance.Quantity != 5)
                return TestResult.Failed($"Expected quantity 5, got: {instance.Quantity}");

            return TestResult.Successful(
                $"Got instance {instance.InstanceId}");
        }, "Get item instance");

    private static async Task<TestResult> TestModifyItemInstance(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateItemTestRealmAsync("INST_MOD");
            var itemClient = GetServiceClient<IItemClient>();
            var containerId = Guid.NewGuid();

            var template = await CreateTestTemplateAsync(itemClient, "modify");
            var created = await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
            {
                TemplateId = template.TemplateId,
                ContainerId = containerId,
                RealmId = realm.RealmId,
                Quantity = 20
            });

            // Modify quantity (decrease by 5: 20 - 5 = 15)
            var modified = await itemClient.ModifyItemInstanceAsync(new ModifyItemInstanceRequest
            {
                InstanceId = created.InstanceId,
                QuantityDelta = -5
            });

            if (modified.Quantity != 15)
                return TestResult.Failed($"Expected quantity 15 after modify, got: {modified.Quantity}");

            return TestResult.Successful(
                $"Modified instance {created.InstanceId}: quantity 20→15");
        }, "Modify item instance");

    private static async Task<TestResult> TestBindItemInstance(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateItemTestRealmAsync("INST_BIND");
            var itemClient = GetServiceClient<IItemClient>();
            var containerId = Guid.NewGuid();
            var characterId = Guid.NewGuid();

            // Create a template that supports soulbinding
            var template = await itemClient.CreateItemTemplateAsync(new CreateItemTemplateRequest
            {
                Code = $"item_bind_{DateTime.Now.Ticks}".ToLowerInvariant(),
                GameId = TestGameId,
                Name = "Soulbound Weapon",
                Description = "Binds to character on pickup",
                Category = ItemCategory.Weapon,
                QuantityModel = QuantityModel.Unique,
                MaxStackSize = 1,
                Weight = 5.0,
                Scope = ItemScope.Global,
                SoulboundType = SoulboundType.OnPickup,
                Tradeable = false,
                Destroyable = true
            });

            var created = await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
            {
                TemplateId = template.TemplateId,
                ContainerId = containerId,
                RealmId = realm.RealmId,
                Quantity = 1
            });

            // Bind to character
            var bound = await itemClient.BindItemInstanceAsync(new BindItemInstanceRequest
            {
                InstanceId = created.InstanceId,
                CharacterId = characterId,
                BindType = SoulboundType.OnPickup
            });

            if (bound.BoundToId != characterId)
                return TestResult.Failed("Bound ID mismatch");

            if (bound.BoundAt == null)
                return TestResult.Failed("BoundAt should be set after binding");

            return TestResult.Successful(
                $"Bound instance {created.InstanceId} to character {characterId}");
        }, "Bind item instance");

    private static async Task<TestResult> TestDestroyItemInstance(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateItemTestRealmAsync("INST_DEL");
            var itemClient = GetServiceClient<IItemClient>();
            var containerId = Guid.NewGuid();

            var template = await CreateTestTemplateAsync(itemClient, "destroy");
            var created = await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
            {
                TemplateId = template.TemplateId,
                ContainerId = containerId,
                RealmId = realm.RealmId,
                Quantity = 5
            });

            // Destroy it
            var destroyed = await itemClient.DestroyItemInstanceAsync(new DestroyItemInstanceRequest
            {
                InstanceId = created.InstanceId,
                Reason = DestroyReason.Destroyed
            });

            if (destroyed.TemplateId != created.TemplateId)
                return TestResult.Failed("Destroy returned wrong template ID");

            // Verify it's gone
            return await ExecuteExpectingAnyStatusAsync(
                async () => await itemClient.GetItemInstanceAsync(new GetItemInstanceRequest
                {
                    InstanceId = created.InstanceId
                }),
                [404],
                "Destroy item instance");
        }, "Destroy item instance");

    // =========================================================================
    // Query Operations
    // =========================================================================

    private static async Task<TestResult> TestListItemsByContainer(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateItemTestRealmAsync("LIST_CONT");
            var itemClient = GetServiceClient<IItemClient>();
            var containerId = Guid.NewGuid();

            // Create multiple items in the same container
            var template1 = await CreateTestTemplateAsync(itemClient, "listcont1");
            var template2 = await CreateTestTemplateAsync(itemClient, "listcont2");

            await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
            {
                TemplateId = template1.TemplateId,
                ContainerId = containerId,
                RealmId = realm.RealmId,
                Quantity = 10
            });
            await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
            {
                TemplateId = template2.TemplateId,
                ContainerId = containerId,
                RealmId = realm.RealmId,
                Quantity = 5
            });

            // List items in container
            var items = await itemClient.ListItemsByContainerAsync(new ListItemsByContainerRequest
            {
                ContainerId = containerId
            });

            if (items.Items == null || items.Items.Count < 2)
                return TestResult.Failed($"Expected at least 2 instances, got: {items.Items?.Count ?? 0}");

            return TestResult.Successful(
                $"Listed {items.Items.Count} items in container {containerId}");
        }, "List items by container");

    private static async Task<TestResult> TestListItemsByTemplate(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateItemTestRealmAsync("LIST_TMPL");
            var itemClient = GetServiceClient<IItemClient>();

            var template = await CreateTestTemplateAsync(itemClient, "listtmpl");

            // Create multiple instances of the same template
            var container1 = Guid.NewGuid();
            var container2 = Guid.NewGuid();

            await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
            {
                TemplateId = template.TemplateId,
                ContainerId = container1,
                RealmId = realm.RealmId,
                Quantity = 10
            });
            await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
            {
                TemplateId = template.TemplateId,
                ContainerId = container2,
                RealmId = realm.RealmId,
                Quantity = 20
            });

            // List instances by template
            var instances = await itemClient.ListItemsByTemplateAsync(new ListItemsByTemplateRequest
            {
                TemplateId = template.TemplateId
            });

            if (instances.Items == null || instances.Items.Count < 2)
                return TestResult.Failed($"Expected at least 2 instances, got: {instances.Items?.Count ?? 0}");

            return TestResult.Successful(
                $"Listed {instances.Items.Count} instances of template {template.Code}");
        }, "List items by template");

    private static async Task<TestResult> TestBatchGetItemInstances(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var realm = await CreateItemTestRealmAsync("BATCH_GET");
            var itemClient = GetServiceClient<IItemClient>();
            var containerId = Guid.NewGuid();

            var template = await CreateTestTemplateAsync(itemClient, "batch");

            // Create multiple instances
            var inst1 = await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
            {
                TemplateId = template.TemplateId,
                ContainerId = containerId,
                RealmId = realm.RealmId,
                Quantity = 5
            });
            var inst2 = await itemClient.CreateItemInstanceAsync(new CreateItemInstanceRequest
            {
                TemplateId = template.TemplateId,
                ContainerId = containerId,
                RealmId = realm.RealmId,
                Quantity = 10
            });

            // Batch get
            var batch = await itemClient.BatchGetItemInstancesAsync(new BatchGetItemInstancesRequest
            {
                InstanceIds = [inst1.InstanceId, inst2.InstanceId]
            });

            if (batch.Items == null || batch.Items.Count != 2)
                return TestResult.Failed($"Expected 2 instances, got: {batch.Items?.Count ?? 0}");

            return TestResult.Successful(
                $"Batch retrieved {batch.Items.Count} instances");
        }, "Batch get item instances");
}
