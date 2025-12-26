using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Servicedata;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for ServiceData service API endpoints.
/// Tests game service registry CRUD operations via NSwag-generated ServicedataClient.
/// </summary>
public class ServicedataTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            // Core CRUD operations
            new ServiceTest(TestCreateService, "CreateService", "ServiceData", "Test service creation endpoint"),
            new ServiceTest(TestGetServiceById, "GetServiceById", "ServiceData", "Test service retrieval by ID"),
            new ServiceTest(TestGetServiceByStubName, "GetServiceByStubName", "ServiceData", "Test service retrieval by stub name"),
            new ServiceTest(TestListServices, "ListServices", "ServiceData", "Test service listing endpoint"),
            new ServiceTest(TestUpdateService, "UpdateService", "ServiceData", "Test service update endpoint"),
            new ServiceTest(TestDeleteService, "DeleteService", "ServiceData", "Test service deletion endpoint"),

            // Validation tests
            new ServiceTest(TestCreateServiceDuplicateStubName, "CreateServiceDuplicateStubName", "ServiceData", "Test conflict on duplicate stub name"),
            new ServiceTest(TestListServicesActiveOnly, "ListServicesActiveOnly", "ServiceData", "Test active-only filter on list"),
        };
    }

    private static async Task<TestResult> TestCreateService(ITestClient client, string[] args)
    {
        try
        {
            var servicedataClient = Program.ServiceProvider!.GetRequiredService<IServicedataClient>();
            var testStubName = $"test-service-{DateTime.Now.Ticks}";

            var createRequest = new CreateServiceRequest
            {
                StubName = testStubName,
                DisplayName = "Test Service",
                Description = "A test game service",
                IsActive = true
            };

            var response = await servicedataClient.CreateServiceAsync(createRequest);

            if (response.ServiceId == Guid.Empty)
                return TestResult.Failed("Service creation returned invalid service ID");

            if (response.StubName != testStubName.ToLowerInvariant())
                return TestResult.Failed($"Service stub name mismatch: expected {testStubName.ToLowerInvariant()}, got {response.StubName}");

            // Clean up
            await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = response.ServiceId });

            return TestResult.Successful($"Service created successfully: ID={response.ServiceId}, StubName={response.StubName}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Service creation failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetServiceById(ITestClient client, string[] args)
    {
        try
        {
            var servicedataClient = Program.ServiceProvider!.GetRequiredService<IServicedataClient>();
            var testStubName = $"get-by-id-{DateTime.Now.Ticks}";

            // First create a test service
            var createRequest = new CreateServiceRequest
            {
                StubName = testStubName,
                DisplayName = "Get By ID Test",
                Description = "Test service for GetServiceById",
                IsActive = true
            };

            var createResponse = await servicedataClient.CreateServiceAsync(createRequest);
            if (createResponse.ServiceId == Guid.Empty)
                return TestResult.Failed("Failed to create test service for retrieval test");

            var serviceId = createResponse.ServiceId;

            // Test retrieval by ID
            var response = await servicedataClient.GetServiceAsync(new GetServiceRequest { ServiceId = serviceId });

            if (response.ServiceId != serviceId)
                return TestResult.Failed($"Service ID mismatch: expected {serviceId}, got {response.ServiceId}");

            if (response.StubName != testStubName.ToLowerInvariant())
                return TestResult.Failed($"Stub name mismatch: expected {testStubName.ToLowerInvariant()}, got {response.StubName}");

            // Clean up
            await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceId });

            return TestResult.Successful($"Service retrieved by ID successfully: ID={response.ServiceId}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Service retrieval by ID failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetServiceByStubName(ITestClient client, string[] args)
    {
        try
        {
            var servicedataClient = Program.ServiceProvider!.GetRequiredService<IServicedataClient>();
            var testStubName = $"get-by-stub-{DateTime.Now.Ticks}";

            // First create a test service
            var createRequest = new CreateServiceRequest
            {
                StubName = testStubName,
                DisplayName = "Get By Stub Test",
                Description = "Test service for GetServiceByStubName",
                IsActive = true
            };

            var createResponse = await servicedataClient.CreateServiceAsync(createRequest);
            if (createResponse.ServiceId == Guid.Empty)
                return TestResult.Failed("Failed to create test service for stub name retrieval test");

            var serviceId = createResponse.ServiceId;

            // Test retrieval by stub name
            var response = await servicedataClient.GetServiceAsync(new GetServiceRequest { StubName = testStubName.ToLowerInvariant() });

            if (response.ServiceId != serviceId)
                return TestResult.Failed($"Service ID mismatch: expected {serviceId}, got {response.ServiceId}");

            // Clean up
            await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceId });

            return TestResult.Successful($"Service retrieved by stub name successfully: StubName={response.StubName}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Service retrieval by stub name failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestListServices(ITestClient client, string[] args)
    {
        try
        {
            var servicedataClient = Program.ServiceProvider!.GetRequiredService<IServicedataClient>();

            // Create a test service to ensure list is not empty
            var testStubName = $"list-test-{DateTime.Now.Ticks}";
            var createResponse = await servicedataClient.CreateServiceAsync(new CreateServiceRequest
            {
                StubName = testStubName,
                DisplayName = "List Test Service",
                IsActive = true
            });

            // List all services
            var response = await servicedataClient.ListServicesAsync(new ListServicesRequest { ActiveOnly = false });

            if (response.Services == null)
                return TestResult.Failed("ListServices returned null services collection");

            if (response.TotalCount < 1)
                return TestResult.Failed($"ListServices should return at least 1 service, got {response.TotalCount}");

            // Clean up
            await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = createResponse.ServiceId });

            return TestResult.Successful($"Service listing successful: Found {response.TotalCount} services");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Service listing failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestUpdateService(ITestClient client, string[] args)
    {
        try
        {
            var servicedataClient = Program.ServiceProvider!.GetRequiredService<IServicedataClient>();
            var testStubName = $"update-test-{DateTime.Now.Ticks}";

            // Create a test service
            var createResponse = await servicedataClient.CreateServiceAsync(new CreateServiceRequest
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

            var response = await servicedataClient.UpdateServiceAsync(updateRequest);

            if (response.DisplayName != newDisplayName)
                return TestResult.Failed($"DisplayName not updated: expected {newDisplayName}, got {response.DisplayName}");

            if (response.Description != newDescription)
                return TestResult.Failed($"Description not updated: expected {newDescription}, got {response.Description}");

            if (response.IsActive != false)
                return TestResult.Failed("IsActive not updated to false");

            // Clean up
            await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceId });

            return TestResult.Successful($"Service updated successfully: ID={response.ServiceId}, NewDisplayName={response.DisplayName}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Service update failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestDeleteService(ITestClient client, string[] args)
    {
        try
        {
            var servicedataClient = Program.ServiceProvider!.GetRequiredService<IServicedataClient>();
            var testStubName = $"delete-test-{DateTime.Now.Ticks}";

            // Create a service to delete
            var createResponse = await servicedataClient.CreateServiceAsync(new CreateServiceRequest
            {
                StubName = testStubName,
                DisplayName = "Delete Test Service",
                IsActive = true
            });

            if (createResponse.ServiceId == Guid.Empty)
                return TestResult.Failed("Failed to create test service for deletion test");

            var serviceId = createResponse.ServiceId;

            // Delete the service
            await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceId });

            // Verify deletion by trying to get it (should return 404)
            try
            {
                await servicedataClient.GetServiceAsync(new GetServiceRequest { ServiceId = serviceId });
                return TestResult.Failed("Service retrieval should have failed after deletion, but it succeeded");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected behavior
                return TestResult.Successful($"Service deleted successfully: ID={serviceId}");
            }
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Service deletion failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestCreateServiceDuplicateStubName(ITestClient client, string[] args)
    {
        try
        {
            var servicedataClient = Program.ServiceProvider!.GetRequiredService<IServicedataClient>();
            var testStubName = $"duplicate-test-{DateTime.Now.Ticks}";

            // Create first service
            var createResponse = await servicedataClient.CreateServiceAsync(new CreateServiceRequest
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
                await servicedataClient.CreateServiceAsync(new CreateServiceRequest
                {
                    StubName = testStubName, // Same stub name
                    DisplayName = "Second Service",
                    IsActive = true
                });

                // If we get here, the test failed - it should have thrown
                await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = createResponse.ServiceId });
                return TestResult.Failed("Duplicate stub name creation should have returned Conflict (409)");
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                // Expected behavior - conflict on duplicate stub name
                await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = createResponse.ServiceId });
                return TestResult.Successful("Correctly returned Conflict (409) for duplicate stub name");
            }
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Duplicate stub name test failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestListServicesActiveOnly(ITestClient client, string[] args)
    {
        try
        {
            var servicedataClient = Program.ServiceProvider!.GetRequiredService<IServicedataClient>();
            var testPrefix = $"active-filter-{DateTime.Now.Ticks}";

            // Create an active service
            var activeResponse = await servicedataClient.CreateServiceAsync(new CreateServiceRequest
            {
                StubName = $"{testPrefix}-active",
                DisplayName = "Active Service",
                IsActive = true
            });

            // Create an inactive service
            var inactiveResponse = await servicedataClient.CreateServiceAsync(new CreateServiceRequest
            {
                StubName = $"{testPrefix}-inactive",
                DisplayName = "Inactive Service",
                IsActive = false
            });

            // List only active services
            var activeOnlyResponse = await servicedataClient.ListServicesAsync(new ListServicesRequest { ActiveOnly = true });

            // List all services
            var allResponse = await servicedataClient.ListServicesAsync(new ListServicesRequest { ActiveOnly = false });

            // Verify filtering works (active only count should be <= all count)
            if (activeOnlyResponse.TotalCount > allResponse.TotalCount)
                return TestResult.Failed($"ActiveOnly count ({activeOnlyResponse.TotalCount}) should be <= all count ({allResponse.TotalCount})");

            // Verify the inactive service is not in active-only list
            var inactiveInActiveList = activeOnlyResponse.Services?.Any(s => s.ServiceId == inactiveResponse.ServiceId) ?? false;
            if (inactiveInActiveList)
                return TestResult.Failed("Inactive service should not appear in active-only list");

            // Clean up
            await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = activeResponse.ServiceId });
            await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = inactiveResponse.ServiceId });

            return TestResult.Successful($"Active-only filter working: ActiveOnly={activeOnlyResponse.TotalCount}, All={allResponse.TotalCount}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Active-only filter test failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }
}
