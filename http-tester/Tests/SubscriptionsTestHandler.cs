using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Servicedata;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Subscriptions;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for Subscriptions service API endpoints.
/// Tests subscription management operations via NSwag-generated SubscriptionsClient.
/// </summary>
public class SubscriptionsTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Core CRUD operations
        new ServiceTest(TestCreateSubscription, "CreateSubscription", "Subscriptions", "Test subscription creation endpoint"),
        new ServiceTest(TestGetSubscription, "GetSubscription", "Subscriptions", "Test subscription retrieval by ID"),
        new ServiceTest(TestGetAccountSubscriptions, "GetAccountSubscriptions", "Subscriptions", "Test subscription listing for account"),
        new ServiceTest(TestGetCurrentSubscriptions, "GetCurrentSubscriptions", "Subscriptions", "Test active subscription authorization strings"),
        new ServiceTest(TestUpdateSubscription, "UpdateSubscription", "Subscriptions", "Test subscription update endpoint"),
        new ServiceTest(TestCancelSubscription, "CancelSubscription", "Subscriptions", "Test subscription cancellation endpoint"),
        new ServiceTest(TestRenewSubscription, "RenewSubscription", "Subscriptions", "Test subscription renewal endpoint"),

        // Validation tests
        new ServiceTest(TestCreateSubscriptionServiceNotFound, "CreateSubscriptionServiceNotFound", "Subscriptions", "Test 404 when service doesn't exist"),
        new ServiceTest(TestCreateSubscriptionDuplicate, "CreateSubscriptionDuplicate", "Subscriptions", "Test conflict on duplicate active subscription"),
    ];

    /// <summary>
    /// Helper to create a test service for subscription tests.
    /// </summary>
    private static async Task<ServiceInfo?> CreateTestService(IServicedataClient servicedataClient, string stubName)
    {
        var createRequest = new CreateServiceRequest
        {
            StubName = stubName,
            DisplayName = $"Test Service {stubName}",
            Description = "Test service for subscription tests",
            IsActive = true
        };

        return await servicedataClient.CreateServiceAsync(createRequest);
    }

    /// <summary>
    /// Helper to create a test account for subscription tests.
    /// </summary>
    private static async Task<AccountResponse?> CreateTestAccount(IAccountsClient accountsClient, string username)
    {
        var createRequest = new CreateAccountRequest
        {
            DisplayName = username,
            Email = $"{username}@example.com",
            Roles = new List<string>()
        };

        return await accountsClient.CreateAccountAsync(createRequest);
    }

    private static Task<TestResult> TestCreateSubscription(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var subscriptionsClient = GetServiceClient<ISubscriptionsClient>();
            var servicedataClient = GetServiceClient<IServicedataClient>();
            var accountsClient = GetServiceClient<IAccountsClient>();

            var testId = DateTime.Now.Ticks;
            var testStubName = $"sub-create-{testId}";
            var testUsername = $"subtest-{testId}";

            // Create test service
            var serviceResponse = await CreateTestService(servicedataClient, testStubName);
            if (serviceResponse == null || serviceResponse.ServiceId == Guid.Empty)
                return TestResult.Failed("Failed to create test service for subscription test");

            // Create test account
            var accountResponse = await CreateTestAccount(accountsClient, testUsername);
            if (accountResponse == null || accountResponse.AccountId == Guid.Empty)
            {
                await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });
                return TestResult.Failed("Failed to create test account for subscription test");
            }

            // Create subscription
            var createRequest = new CreateSubscriptionRequest
            {
                AccountId = accountResponse.AccountId,
                ServiceId = serviceResponse.ServiceId,
                ExpirationDate = DateTimeOffset.UtcNow.AddDays(30)
            };

            var response = await subscriptionsClient.CreateSubscriptionAsync(createRequest);

            if (response.SubscriptionId == Guid.Empty)
            {
                await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
                await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });
                return TestResult.Failed("Subscription creation returned invalid subscription ID");
            }

            if (response.AccountId != accountResponse.AccountId)
            {
                await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
                await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });
                return TestResult.Failed($"AccountId mismatch: expected {accountResponse.AccountId}, got {response.AccountId}");
            }

            // Clean up
            await subscriptionsClient.CancelSubscriptionAsync(new CancelSubscriptionRequest { SubscriptionId = response.SubscriptionId });
            await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
            await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });

            return TestResult.Successful($"Subscription created successfully: ID={response.SubscriptionId}, Service={response.StubName}");
        }, "Create subscription");

    private static Task<TestResult> TestGetSubscription(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var subscriptionsClient = GetServiceClient<ISubscriptionsClient>();
            var servicedataClient = GetServiceClient<IServicedataClient>();
            var accountsClient = GetServiceClient<IAccountsClient>();

            var testId = DateTime.Now.Ticks;
            var testStubName = $"sub-get-{testId}";
            var testUsername = $"subtest-get-{testId}";

            // Create test service and account
            var serviceResponse = await CreateTestService(servicedataClient, testStubName);
            var accountResponse = await CreateTestAccount(accountsClient, testUsername);

            if (serviceResponse == null || accountResponse == null)
                return TestResult.Failed("Failed to create test data");

            // Create subscription
            var createResponse = await subscriptionsClient.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                AccountId = accountResponse.AccountId,
                ServiceId = serviceResponse.ServiceId
            });

            // Get subscription by ID
            var response = await subscriptionsClient.GetSubscriptionAsync(new GetSubscriptionRequest
            {
                SubscriptionId = createResponse.SubscriptionId
            });

            if (response.SubscriptionId != createResponse.SubscriptionId)
            {
                await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
                await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });
                return TestResult.Failed($"Subscription ID mismatch: expected {createResponse.SubscriptionId}, got {response.SubscriptionId}");
            }

            // Clean up
            await subscriptionsClient.CancelSubscriptionAsync(new CancelSubscriptionRequest { SubscriptionId = createResponse.SubscriptionId });
            await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
            await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });

            return TestResult.Successful($"Subscription retrieved successfully: ID={response.SubscriptionId}");
        }, "Get subscription");

    private static Task<TestResult> TestGetAccountSubscriptions(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var subscriptionsClient = GetServiceClient<ISubscriptionsClient>();
            var servicedataClient = GetServiceClient<IServicedataClient>();
            var accountsClient = GetServiceClient<IAccountsClient>();

            var testId = DateTime.Now.Ticks;
            var testStubName = $"sub-list-{testId}";
            var testUsername = $"subtest-list-{testId}";

            // Create test service and account
            var serviceResponse = await CreateTestService(servicedataClient, testStubName);
            var accountResponse = await CreateTestAccount(accountsClient, testUsername);

            if (serviceResponse == null || accountResponse == null)
                return TestResult.Failed("Failed to create test data");

            // Create subscription
            var createResponse = await subscriptionsClient.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                AccountId = accountResponse.AccountId,
                ServiceId = serviceResponse.ServiceId
            });

            // Get account subscriptions
            var response = await subscriptionsClient.GetAccountSubscriptionsAsync(new GetAccountSubscriptionsRequest
            {
                AccountId = accountResponse.AccountId
            });

            if (response.Subscriptions == null || response.Subscriptions.Count == 0)
            {
                await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
                await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });
                return TestResult.Failed("GetAccountSubscriptions returned empty list");
            }

            // Clean up
            await subscriptionsClient.CancelSubscriptionAsync(new CancelSubscriptionRequest { SubscriptionId = createResponse.SubscriptionId });
            await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
            await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });

            return TestResult.Successful($"Account subscriptions retrieved: {response.Subscriptions.Count} subscription(s)");
        }, "Get account subscriptions");

    private static Task<TestResult> TestGetCurrentSubscriptions(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var subscriptionsClient = GetServiceClient<ISubscriptionsClient>();
            var servicedataClient = GetServiceClient<IServicedataClient>();
            var accountsClient = GetServiceClient<IAccountsClient>();

            var testId = DateTime.Now.Ticks;
            var testStubName = $"sub-current-{testId}";
            var testUsername = $"subtest-current-{testId}";

            // Create test service and account
            var serviceResponse = await CreateTestService(servicedataClient, testStubName);
            var accountResponse = await CreateTestAccount(accountsClient, testUsername);

            if (serviceResponse == null || accountResponse == null)
                return TestResult.Failed("Failed to create test data");

            // Create subscription with future expiration
            var createResponse = await subscriptionsClient.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                AccountId = accountResponse.AccountId,
                ServiceId = serviceResponse.ServiceId,
                ExpirationDate = DateTimeOffset.UtcNow.AddDays(30)
            });

            // Get current subscriptions (should include authorization strings)
            var response = await subscriptionsClient.GetCurrentSubscriptionsAsync(new GetCurrentSubscriptionsRequest
            {
                AccountId = accountResponse.AccountId
            });

            if (response.Authorizations == null || response.Authorizations.Count == 0)
            {
                await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
                await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });
                return TestResult.Failed("GetCurrentSubscriptions returned empty authorizations");
            }

            // Check authorization string format (should be "stubname:authorized")
            var hasExpectedAuth = response.Authorizations.Any(a => a.Contains(testStubName.ToLowerInvariant()));

            // Clean up
            await subscriptionsClient.CancelSubscriptionAsync(new CancelSubscriptionRequest { SubscriptionId = createResponse.SubscriptionId });
            await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
            await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });

            if (!hasExpectedAuth)
                return TestResult.Failed($"Expected authorization containing '{testStubName.ToLowerInvariant()}', got: {string.Join(", ", response.Authorizations)}");

            return TestResult.Successful($"Current subscriptions retrieved: {response.Authorizations.Count} authorization(s)");
        }, "Get current subscriptions");

    private static Task<TestResult> TestUpdateSubscription(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var subscriptionsClient = GetServiceClient<ISubscriptionsClient>();
            var servicedataClient = GetServiceClient<IServicedataClient>();
            var accountsClient = GetServiceClient<IAccountsClient>();

            var testId = DateTime.Now.Ticks;
            var testStubName = $"sub-update-{testId}";
            var testUsername = $"subtest-update-{testId}";

            // Create test service and account
            var serviceResponse = await CreateTestService(servicedataClient, testStubName);
            var accountResponse = await CreateTestAccount(accountsClient, testUsername);

            if (serviceResponse == null || accountResponse == null)
                return TestResult.Failed("Failed to create test data");

            // Create subscription
            var createResponse = await subscriptionsClient.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                AccountId = accountResponse.AccountId,
                ServiceId = serviceResponse.ServiceId,
                ExpirationDate = DateTimeOffset.UtcNow.AddDays(30)
            });

            // Update subscription with new expiration date
            var newExpiration = DateTimeOffset.UtcNow.AddDays(60);
            var response = await subscriptionsClient.UpdateSubscriptionAsync(new UpdateSubscriptionRequest
            {
                SubscriptionId = createResponse.SubscriptionId,
                ExpirationDate = newExpiration
            });

            // Clean up
            await subscriptionsClient.CancelSubscriptionAsync(new CancelSubscriptionRequest { SubscriptionId = createResponse.SubscriptionId });
            await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
            await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });

            return TestResult.Successful($"Subscription updated successfully: ID={response.SubscriptionId}");
        }, "Update subscription");

    private static Task<TestResult> TestCancelSubscription(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var subscriptionsClient = GetServiceClient<ISubscriptionsClient>();
            var servicedataClient = GetServiceClient<IServicedataClient>();
            var accountsClient = GetServiceClient<IAccountsClient>();

            var testId = DateTime.Now.Ticks;
            var testStubName = $"sub-cancel-{testId}";
            var testUsername = $"subtest-cancel-{testId}";

            // Create test service and account
            var serviceResponse = await CreateTestService(servicedataClient, testStubName);
            var accountResponse = await CreateTestAccount(accountsClient, testUsername);

            if (serviceResponse == null || accountResponse == null)
                return TestResult.Failed("Failed to create test data");

            // Create subscription
            var createResponse = await subscriptionsClient.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                AccountId = accountResponse.AccountId,
                ServiceId = serviceResponse.ServiceId
            });

            // Cancel subscription
            var response = await subscriptionsClient.CancelSubscriptionAsync(new CancelSubscriptionRequest
            {
                SubscriptionId = createResponse.SubscriptionId,
                Reason = "Test cancellation"
            });

            if (response.IsActive != false)
            {
                await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
                await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });
                return TestResult.Failed("Cancelled subscription should have IsActive=false");
            }

            // Clean up
            await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
            await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });

            return TestResult.Successful($"Subscription cancelled successfully: ID={response.SubscriptionId}");
        }, "Cancel subscription");

    private static Task<TestResult> TestRenewSubscription(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var subscriptionsClient = GetServiceClient<ISubscriptionsClient>();
            var servicedataClient = GetServiceClient<IServicedataClient>();
            var accountsClient = GetServiceClient<IAccountsClient>();

            var testId = DateTime.Now.Ticks;
            var testStubName = $"sub-renew-{testId}";
            var testUsername = $"subtest-renew-{testId}";

            // Create test service and account
            var serviceResponse = await CreateTestService(servicedataClient, testStubName);
            var accountResponse = await CreateTestAccount(accountsClient, testUsername);

            if (serviceResponse == null || accountResponse == null)
                return TestResult.Failed("Failed to create test data");

            // Create subscription with near expiration
            var createResponse = await subscriptionsClient.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                AccountId = accountResponse.AccountId,
                ServiceId = serviceResponse.ServiceId,
                ExpirationDate = DateTimeOffset.UtcNow.AddDays(1)
            });

            // Renew subscription for another 30 days
            var newExpiration = DateTimeOffset.UtcNow.AddDays(30);
            var response = await subscriptionsClient.RenewSubscriptionAsync(new RenewSubscriptionRequest
            {
                SubscriptionId = createResponse.SubscriptionId,
                NewExpirationDate = newExpiration
            });

            if (!response.IsActive)
            {
                await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
                await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });
                return TestResult.Failed("Renewed subscription should be active");
            }

            // Clean up
            await subscriptionsClient.CancelSubscriptionAsync(new CancelSubscriptionRequest { SubscriptionId = createResponse.SubscriptionId });
            await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
            await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });

            return TestResult.Successful($"Subscription renewed successfully: ID={response.SubscriptionId}");
        }, "Renew subscription");

    private static Task<TestResult> TestCreateSubscriptionServiceNotFound(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var subscriptionsClient = GetServiceClient<ISubscriptionsClient>();
            var accountsClient = GetServiceClient<IAccountsClient>();

            var testId = DateTime.Now.Ticks;
            var testUsername = $"subtest-notfound-{testId}";

            // Create test account
            var accountResponse = await CreateTestAccount(accountsClient, testUsername);

            if (accountResponse == null)
                return TestResult.Failed("Failed to create test account");

            // Try to create subscription with non-existent service
            try
            {
                await subscriptionsClient.CreateSubscriptionAsync(new CreateSubscriptionRequest
                {
                    AccountId = accountResponse.AccountId,
                    ServiceId = Guid.NewGuid() // Non-existent service
                });

                await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
                return TestResult.Failed("CreateSubscription should have returned 404 for non-existent service");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected behavior
                await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
                return TestResult.Successful("Correctly returned 404 for non-existent service");
            }
        }, "Create subscription for non-existent service");

    private static Task<TestResult> TestCreateSubscriptionDuplicate(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var subscriptionsClient = GetServiceClient<ISubscriptionsClient>();
            var servicedataClient = GetServiceClient<IServicedataClient>();
            var accountsClient = GetServiceClient<IAccountsClient>();

            var testId = DateTime.Now.Ticks;
            var testStubName = $"sub-dup-{testId}";
            var testUsername = $"subtest-dup-{testId}";

            // Create test service and account
            var serviceResponse = await CreateTestService(servicedataClient, testStubName);
            var accountResponse = await CreateTestAccount(accountsClient, testUsername);

            if (serviceResponse == null || accountResponse == null)
                return TestResult.Failed("Failed to create test data");

            // Create first subscription
            var createResponse = await subscriptionsClient.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                AccountId = accountResponse.AccountId,
                ServiceId = serviceResponse.ServiceId
            });

            // Try to create duplicate subscription (should fail with Conflict)
            try
            {
                await subscriptionsClient.CreateSubscriptionAsync(new CreateSubscriptionRequest
                {
                    AccountId = accountResponse.AccountId,
                    ServiceId = serviceResponse.ServiceId
                });

                // If we get here, the test failed
                await subscriptionsClient.CancelSubscriptionAsync(new CancelSubscriptionRequest { SubscriptionId = createResponse.SubscriptionId });
                await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
                await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });
                return TestResult.Failed("Duplicate subscription should have returned Conflict (409)");
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                // Expected behavior
                await subscriptionsClient.CancelSubscriptionAsync(new CancelSubscriptionRequest { SubscriptionId = createResponse.SubscriptionId });
                await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountResponse.AccountId });
                await servicedataClient.DeleteServiceAsync(new DeleteServiceRequest { ServiceId = serviceResponse.ServiceId });
                return TestResult.Successful("Correctly returned Conflict (409) for duplicate active subscription");
            }
        }, "Create duplicate subscription");
}
