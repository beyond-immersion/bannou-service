using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for Game Service API endpoints.
/// Tests game service registry CRUD operations via NSwag-generated GameServiceClient.
/// </summary>
public class GameServiceTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Core CRUD operations
        new ServiceTest(TestCreateService, "CreateService", "GameService", "Test service creation endpoint"),
        new ServiceTest(TestGetServiceById, "GetServiceById", "GameService", "Test service retrieval by ID"),
        new ServiceTest(TestGetServiceByStubName, "GetServiceByStubName", "GameService", "Test service retrieval by stub name"),
        new ServiceTest(TestListServices, "ListServices", "GameService", "Test service listing endpoint"),
        new ServiceTest(TestUpdateService, "UpdateService", "GameService", "Test service update endpoint"),
        new ServiceTest(TestDeleteService, "DeleteService", "GameService", "Test service deletion endpoint"),

        // Validation tests
        new ServiceTest(TestCreateServiceDuplicateStubName, "CreateServiceDuplicateStubName", "GameService", "Test conflict on duplicate stub name"),
        new ServiceTest(TestListServicesActiveOnly, "ListServicesActiveOnly", "GameService", "Test active-only filter on list"),
    ];

    private static async Task<TestResult> TestCreateService(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var serviceClient = GetServiceClient<IGameServiceClient>();
            var testStubName = GenerateTestSlug("test-service");

            var createRequest = new CreateServiceRequest
            {
                StubName = testStubName,
                DisplayName = "Test Service",
                Description = "A test game service",
                IsActive = true
            };

            var response = await serviceClient.CreateServiceAsync(createRequest);

            if (response.ServiceId == Guid.Empty)
                return TestResult.Failed("Service creation returned invalid service ID");

            if (response.StubName != testStubName.ToLowerInvariant())
                return TestResult.Failed($"Service stub name mismatch: expected {testStubName.ToLowerInvariant()}, got {response.StubName}");

            // Clean up
            await serviceClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = response.ServiceId });

            return TestResult.Successful($"Service created successfully: ID={response.ServiceId}, StubName={response.StubName}");
        }, "Create service");

    private static async Task<TestResult> TestGetServiceById(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var serviceClient = GetServiceClient<IGameServiceClient>();
            var testStubName = GenerateTestSlug("get-by-id");

            // First create a test service
            var createRequest = new CreateServiceRequest
            {
                StubName = testStubName,
                DisplayName = "Get By ID Test",
                Description = "Test service for GetServiceById",
                IsActive = true
            };

            var createResponse = await serviceClient.CreateServiceAsync(createRequest);
            if (createResponse.ServiceId == Guid.Empty)
                return TestResult.Failed("Failed to create test service for retrieval test");

            var serviceId = createResponse.ServiceId;

            // Test retrieval by ID
            var response = await serviceClient.GetServiceAsync(new GetServiceRequest { ServiceId = serviceId });

            if (response.ServiceId != serviceId)
                return TestResult.Failed($"Service ID mismatch: expected {serviceId}, got {response.ServiceId}");

            if (response.StubName != testStubName.ToLowerInvariant())
                return TestResult.Failed($"Stub name mismatch: expected {testStubName.ToLowerInvariant()}, got {response.StubName}");

            // Clean up
            await serviceClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceId });

            return TestResult.Successful($"Service retrieved by ID successfully: ID={response.ServiceId}");
        }, "Get service by ID");

    private static async Task<TestResult> TestGetServiceByStubName(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var serviceClient = GetServiceClient<IGameServiceClient>();
            var testStubName = GenerateTestSlug("get-by-stub");

            // First create a test service
            var createRequest = new CreateServiceRequest
            {
                StubName = testStubName,
                DisplayName = "Get By Stub Test",
                Description = "Test service for GetServiceByStubName",
                IsActive = true
            };

            var createResponse = await serviceClient.CreateServiceAsync(createRequest);
            if (createResponse.ServiceId == Guid.Empty)
                return TestResult.Failed("Failed to create test service for stub name retrieval test");

            var serviceId = createResponse.ServiceId;

            // Test retrieval by stub name
            var response = await serviceClient.GetServiceAsync(new GetServiceRequest { StubName = testStubName.ToLowerInvariant() });

            if (response.ServiceId != serviceId)
                return TestResult.Failed($"Service ID mismatch: expected {serviceId}, got {response.ServiceId}");

            // Clean up
            await serviceClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceId });

            return TestResult.Successful($"Service retrieved by stub name successfully: StubName={response.StubName}");
        }, "Get service by stub name");

    private static async Task<TestResult> TestListServices(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var serviceClient = GetServiceClient<IGameServiceClient>();

            // Create a test service to ensure list is not empty
            var testStubName = GenerateTestSlug("list-test");
            var createResponse = await serviceClient.CreateServiceAsync(new CreateServiceRequest
            {
                StubName = testStubName,
                DisplayName = "List Test Service",
                IsActive = true
            });

            // List all services
            var response = await serviceClient.ListServicesAsync(new ListServicesRequest { ActiveOnly = false });

            if (response.Services == null)
                return TestResult.Failed("ListServices returned null services collection");

            if (response.TotalCount < 1)
                return TestResult.Failed($"ListServices should return at least 1 service, got {response.TotalCount}");

            // Clean up
            await serviceClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = createResponse.ServiceId });

            return TestResult.Successful($"Service listing successful: Found {response.TotalCount} services");
        }, "List services");

    private static async Task<TestResult> TestUpdateService(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var serviceClient = GetServiceClient<IGameServiceClient>();
            var testStubName = GenerateTestSlug("update-test");

            // Create a test service
            var createResponse = await serviceClient.CreateServiceAsync(new CreateServiceRequest
            {
                StubName = testStubName,
                DisplayName = "Update Test Original",
                Description = "Original description",
                IsActive = true
            });

            if (createResponse.ServiceId == Guid.Empty)
                return TestResult.Failed("Failed to create test service for update test");

            var serviceId = createResponse.ServiceId;
            var newDisplayName = $"Updated Service {DateTime.Now.Ticks}";
            var newDescription = "Updated description";

            // Update the service
            var updateRequest = new UpdateServiceRequest
            {
                ServiceId = serviceId,
                DisplayName = newDisplayName,
                Description = newDescription,
                IsActive = false
            };

            var response = await serviceClient.UpdateServiceAsync(updateRequest);

            if (response.DisplayName != newDisplayName)
                return TestResult.Failed($"DisplayName not updated: expected {newDisplayName}, got {response.DisplayName}");

            if (response.Description != newDescription)
                return TestResult.Failed($"Description not updated: expected {newDescription}, got {response.Description}");

            if (response.IsActive != false)
                return TestResult.Failed("IsActive not updated to false");

            // Clean up
            await serviceClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceId });

            return TestResult.Successful($"Service updated successfully: ID={response.ServiceId}, NewDisplayName={response.DisplayName}");
        }, "Update service");

    private static async Task<TestResult> TestDeleteService(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var serviceClient = GetServiceClient<IGameServiceClient>();
            var testStubName = GenerateTestSlug("delete-test");

            // Create a service to delete
            var createResponse = await serviceClient.CreateServiceAsync(new CreateServiceRequest
            {
                StubName = testStubName,
                DisplayName = "Delete Test Service",
                IsActive = true
            });

            if (createResponse.ServiceId == Guid.Empty)
                return TestResult.Failed("Failed to create test service for deletion test");

            var serviceId = createResponse.ServiceId;

            // Delete the service
            await serviceClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceId });

            // Verify deletion by trying to get it (should return 404)
            try
            {
                await serviceClient.GetServiceAsync(new GetServiceRequest { ServiceId = serviceId });
                return TestResult.Failed("Service retrieval should have failed after deletion, but it succeeded");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected behavior
                return TestResult.Successful($"Service deleted successfully: ID={serviceId}");
            }
        }, "Delete service");

    private static async Task<TestResult> TestCreateServiceDuplicateStubName(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var serviceClient = GetServiceClient<IGameServiceClient>();
            var testStubName = GenerateTestSlug("duplicate-test");

            // Create first service
            var createResponse = await serviceClient.CreateServiceAsync(new CreateServiceRequest
            {
                StubName = testStubName,
                DisplayName = "First Service",
                IsActive = true
            });

            if (createResponse.ServiceId == Guid.Empty)
                return TestResult.Failed("Failed to create first test service");

            // Try to create second service with same stub name (should fail with Conflict)
            try
            {
                await serviceClient.CreateServiceAsync(new CreateServiceRequest
                {
                    StubName = testStubName, // Same stub name
                    DisplayName = "Second Service",
                    IsActive = true
                });

                // If we get here, the test failed - it should have thrown
                await serviceClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = createResponse.ServiceId });
                return TestResult.Failed("Duplicate stub name creation should have returned Conflict (409)");
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                // Expected behavior - conflict on duplicate stub name
                await serviceClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = createResponse.ServiceId });
                return TestResult.Successful("Correctly returned Conflict (409) for duplicate stub name");
            }
        }, "Duplicate stub name conflict");

    private static async Task<TestResult> TestListServicesActiveOnly(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var serviceClient = GetServiceClient<IGameServiceClient>();
            var testPrefix = GenerateTestSlug("active-filter");

            // Create an active service
            var activeResponse = await serviceClient.CreateServiceAsync(new CreateServiceRequest
            {
                StubName = $"{testPrefix}-active",
                DisplayName = "Active Service",
                IsActive = true
            });

            // Create an inactive service
            var inactiveResponse = await serviceClient.CreateServiceAsync(new CreateServiceRequest
            {
                StubName = $"{testPrefix}-inactive",
                DisplayName = "Inactive Service",
                IsActive = false
            });

            // List only active services
            var activeOnlyResponse = await serviceClient.ListServicesAsync(new ListServicesRequest { ActiveOnly = true });

            // List all services
            var allResponse = await serviceClient.ListServicesAsync(new ListServicesRequest { ActiveOnly = false });

            // Verify filtering works (active only count should be <= all count)
            if (activeOnlyResponse.TotalCount > allResponse.TotalCount)
                return TestResult.Failed($"ActiveOnly count ({activeOnlyResponse.TotalCount}) should be <= all count ({allResponse.TotalCount})");

            // Verify the inactive service is not in active-only list
            var inactiveInActiveList = activeOnlyResponse.Services?.Any(s => s.ServiceId == inactiveResponse.ServiceId) ?? false;
            if (inactiveInActiveList)
                return TestResult.Failed("Inactive service should not appear in active-only list");

            // Clean up
            await serviceClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = activeResponse.ServiceId });
            await serviceClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = inactiveResponse.ServiceId });

            return TestResult.Successful($"Active-only filter working: ActiveOnly={activeOnlyResponse.TotalCount}, All={allResponse.TotalCount}");
        }, "Active-only filter");
}
