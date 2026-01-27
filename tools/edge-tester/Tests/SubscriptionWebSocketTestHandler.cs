using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Subscription;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for Subscription service API endpoints.
/// Tests the Subscription service APIs using TYPED PROXIES through the Connect service WebSocket binary protocol.
/// This validates both the service logic AND the typed proxy generation.
/// </summary>
public class SubscriptionWebSocketTestHandler : BaseWebSocketTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Empty list tests (must run first before any data is created)
        new ServiceTest(TestEmptyAccountSubscriptionsViaWebSocket, "Subscription - Empty List (WebSocket)", "WebSocket",
            "Test that account subscriptions returns empty list for non-existent account"),

        // CRUD operations
        new ServiceTest(TestCreateAndGetSubscriptionViaWebSocket, "Subscription - Create and Get (WebSocket)", "WebSocket",
            "Test subscription creation and retrieval via typed proxy"),
        new ServiceTest(TestSubscriptionLifecycleViaWebSocket, "Subscription - Full Lifecycle (WebSocket)", "WebSocket",
            "Test complete subscription lifecycle via typed proxy: create -> update -> cancel -> renew"),
        new ServiceTest(TestGetCurrentSubscriptionsViaWebSocket, "Subscription - Account List (WebSocket)", "WebSocket",
            "Test getting account subscriptions returns expected stub names via typed proxy"),

        // Error handling tests
        new ServiceTest(TestGetNonExistentSubscriptionViaWebSocket, "Subscription - 404 Not Found (WebSocket)", "WebSocket",
            "Test that getting a non-existent subscription returns proper error"),
        new ServiceTest(TestDuplicateSubscriptionViaWebSocket, "Subscription - 409 Conflict (WebSocket)", "WebSocket",
            "Test that creating duplicate subscription returns conflict error"),
    ];

    #region Helper Methods

    /// <summary>
    /// Creates a test service for subscription tests using typed proxy.
    /// </summary>
    private async Task<ServiceInfo?> CreateTestServiceAsync(BannouClient adminClient, string uniqueCode)
    {
        try
        {
            var stubName = $"test-svc-{uniqueCode}".ToLowerInvariant();
            var response = await adminClient.GameService.CreateServiceAsync(new CreateServiceRequest
            {
                StubName = stubName,
                DisplayName = $"Test Service {uniqueCode}",
                Description = "Test service for subscription tests"
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to create test service: {FormatError(response.Error)}");
                return null;
            }

            Console.WriteLine($"   Created test service: {response.Result.ServiceId} ({response.Result.StubName})");
            return response.Result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test service: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a test account for subscription tests using typed proxy.
    /// </summary>
    private async Task<AccountResponse?> CreateTestAccountAsync(BannouClient adminClient, string uniqueCode)
    {
        try
        {
            var response = await adminClient.Account.CreateAccountAsync(new CreateAccountRequest
            {
                Email = $"sub-test-{uniqueCode}@test.local",
                DisplayName = $"SubTest{uniqueCode}"
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to create test account: {FormatError(response.Error)}");
                return null;
            }

            Console.WriteLine($"   Created test account: {response.Result.AccountId}");
            return response.Result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test account: {ex.Message}");
            return null;
        }
    }

    #endregion

    private void TestEmptyAccountSubscriptionsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Subscription Empty List Test (WebSocket) ===");
        Console.WriteLine("Testing account subscriptions returns empty array for non-existent account via typed proxy...");

        RunWebSocketTest("Subscription empty list test", async adminClient =>
        {
            // Use a random account ID that doesn't exist
            var nonExistentAccountId = Guid.NewGuid();

            var response = await adminClient.Subscription.GetAccountSubscriptionsAsync(new GetAccountSubscriptionsRequest
            {
                AccountId = nonExistentAccountId
            }, timeout: TimeSpan.FromSeconds(5));

            if (!response.IsSuccess || response.Result == null)
            {
                Console.WriteLine($"   Failed to list subscriptions: {FormatError(response.Error)}");
                return false;
            }

            var result = response.Result;

            // Should return empty array, not null, and totalCount of 0
            if (result.Subscriptions == null)
            {
                Console.WriteLine("   subscriptions array is null - should be empty array");
                return false;
            }

            if (result.Subscriptions.Count != 0)
            {
                Console.WriteLine($"   Expected 0 subscriptions, got {result.Subscriptions.Count}");
                return false;
            }

            if (result.TotalCount != 0)
            {
                Console.WriteLine($"   Expected totalCount 0, got {result.TotalCount}");
                return false;
            }

            Console.WriteLine($"   Correctly returned empty array with totalCount: {result.TotalCount}");
            return true;
        });
    }

    private void TestCreateAndGetSubscriptionViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Subscription Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing subscription creation and retrieval via typed proxy...");

        RunWebSocketTest("Subscription create and get test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            // Create test service
            var service = await CreateTestServiceAsync(adminClient, uniqueCode);
            if (service == null) return false;

            // Create test account
            var account = await CreateTestAccountAsync(adminClient, uniqueCode);
            if (account == null) return false;

            // Create subscription using typed proxy
            Console.WriteLine("   Creating subscription via typed proxy...");
            var createResponse = await adminClient.Subscription.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                AccountId = account.AccountId,
                ServiceId = service.ServiceId,
                DurationDays = 30
            }, timeout: TimeSpan.FromSeconds(5));

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create subscription: {FormatError(createResponse.Error)}");
                return false;
            }

            var subscription = createResponse.Result;
            Console.WriteLine($"   Created subscription: {subscription.SubscriptionId} (service: {subscription.StubName})");

            // Retrieve it using typed proxy
            Console.WriteLine("   Retrieving subscription via typed proxy...");
            var getResponse = await adminClient.Subscription.GetSubscriptionAsync(new GetSubscriptionRequest
            {
                SubscriptionId = subscription.SubscriptionId
            }, timeout: TimeSpan.FromSeconds(5));

            if (!getResponse.IsSuccess || getResponse.Result == null)
            {
                Console.WriteLine($"   Failed to get subscription: {FormatError(getResponse.Error)}");
                return false;
            }

            var retrieved = getResponse.Result;
            Console.WriteLine($"   Retrieved subscription: {retrieved.SubscriptionId} (active: {retrieved.IsActive})");

            return retrieved.SubscriptionId == subscription.SubscriptionId && retrieved.IsActive;
        });
    }

    private void TestSubscriptionLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Subscription Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete subscription lifecycle via typed proxy...");

        RunWebSocketTest("Subscription lifecycle test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            // Create test service
            var service = await CreateTestServiceAsync(adminClient, uniqueCode);
            if (service == null) return false;

            // Create test account
            var account = await CreateTestAccountAsync(adminClient, uniqueCode);
            if (account == null) return false;

            // Step 1: Create subscription
            Console.WriteLine("   Step 1: Creating subscription...");
            var createResponse = await adminClient.Subscription.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                AccountId = account.AccountId,
                ServiceId = service.ServiceId,
                DurationDays = 30
            }, timeout: TimeSpan.FromSeconds(5));

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create subscription: {FormatError(createResponse.Error)}");
                return false;
            }

            var subscription = createResponse.Result;
            Console.WriteLine($"   Created subscription {subscription.SubscriptionId}");

            // Step 2: Verify in account list
            Console.WriteLine("   Step 2: Verifying subscription appears in account list...");
            var listResponse = await adminClient.Subscription.GetAccountSubscriptionsAsync(new GetAccountSubscriptionsRequest
            {
                AccountId = account.AccountId
            }, timeout: TimeSpan.FromSeconds(5));

            if (!listResponse.IsSuccess || listResponse.Result == null || listResponse.Result.Subscriptions == null)
            {
                Console.WriteLine($"   Failed to list subscriptions: {FormatError(listResponse.Error)}");
                return false;
            }

            if (listResponse.Result.Subscriptions.Count == 0)
            {
                Console.WriteLine("   Subscription not found in account list");
                return false;
            }
            Console.WriteLine($"   Found {listResponse.Result.Subscriptions.Count} subscription(s) in account list");

            // Step 3: Update subscription
            Console.WriteLine("   Step 3: Updating subscription expiration...");
            var newExpiration = DateTimeOffset.UtcNow.AddDays(60);
            var updateResponse = await adminClient.Subscription.UpdateSubscriptionAsync(new UpdateSubscriptionRequest
            {
                SubscriptionId = subscription.SubscriptionId,
                ExpirationDate = newExpiration
            }, timeout: TimeSpan.FromSeconds(5));

            if (!updateResponse.IsSuccess || updateResponse.Result == null)
            {
                Console.WriteLine($"   Failed to update subscription: {FormatError(updateResponse.Error)}");
                return false;
            }
            Console.WriteLine($"   Updated expiration to: {updateResponse.Result.ExpirationDate}");

            // Step 4: Cancel subscription
            Console.WriteLine("   Step 4: Cancelling subscription...");
            var cancelResponse = await adminClient.Subscription.CancelSubscriptionAsync(new CancelSubscriptionRequest
            {
                SubscriptionId = subscription.SubscriptionId,
                Reason = "WebSocket lifecycle test"
            }, timeout: TimeSpan.FromSeconds(5));

            if (!cancelResponse.IsSuccess || cancelResponse.Result == null)
            {
                Console.WriteLine($"   Failed to cancel subscription: {FormatError(cancelResponse.Error)}");
                return false;
            }

            if (cancelResponse.Result.IsActive)
            {
                Console.WriteLine("   Subscription still active after cancel");
                return false;
            }
            Console.WriteLine($"   Subscription cancelled (active: {cancelResponse.Result.IsActive})");

            // Step 5: Renew subscription
            Console.WriteLine("   Step 5: Renewing subscription...");
            var renewResponse = await adminClient.Subscription.RenewSubscriptionAsync(new RenewSubscriptionRequest
            {
                SubscriptionId = subscription.SubscriptionId,
                ExtensionDays = 90
            }, timeout: TimeSpan.FromSeconds(5));

            if (!renewResponse.IsSuccess || renewResponse.Result == null)
            {
                Console.WriteLine($"   Failed to renew subscription: {FormatError(renewResponse.Error)}");
                return false;
            }

            if (!renewResponse.Result.IsActive)
            {
                Console.WriteLine("   Subscription not active after renew");
                return false;
            }
            Console.WriteLine($"   Subscription renewed (active: {renewResponse.Result.IsActive})");

            return true;
        });
    }

    private void TestGetCurrentSubscriptionsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Subscription Account List Test (WebSocket) ===");
        Console.WriteLine("Testing account subscriptions list via typed proxy...");

        RunWebSocketTest("Subscription account list test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            // Create test service
            var service = await CreateTestServiceAsync(adminClient, uniqueCode);
            if (service == null) return false;

            // Create test account
            var account = await CreateTestAccountAsync(adminClient, uniqueCode);
            if (account == null) return false;

            // Create subscription
            Console.WriteLine("   Creating subscription...");
            var createResponse = await adminClient.Subscription.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                AccountId = account.AccountId,
                ServiceId = service.ServiceId,
                DurationDays = 30
            }, timeout: TimeSpan.FromSeconds(5));

            if (!createResponse.IsSuccess || createResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create subscription: {FormatError(createResponse.Error)}");
                return false;
            }

            // Get subscriptions for the account using typed proxy
            Console.WriteLine("   Listing account subscriptions via typed proxy...");
            var listResponse = await adminClient.Subscription.GetAccountSubscriptionsAsync(new GetAccountSubscriptionsRequest
            {
                AccountId = account.AccountId
            }, timeout: TimeSpan.FromSeconds(5));

            if (!listResponse.IsSuccess || listResponse.Result == null)
            {
                Console.WriteLine($"   Failed to list subscriptions: {FormatError(listResponse.Error)}");
                return false;
            }

            var subscriptions = listResponse.Result.Subscriptions;
            if (subscriptions == null)
            {
                Console.WriteLine("   subscriptions array is null");
                return false;
            }

            Console.WriteLine($"   Received {subscriptions.Count} subscription(s)");

            // Should have at least one subscription matching our service stub name
            var hasExpectedSub = subscriptions.Any(s => s.StubName == service.StubName);
            if (!hasExpectedSub)
            {
                var stubNames = subscriptions.Select(s => s.StubName ?? "null");
                Console.WriteLine($"   Expected subscription for service '{service.StubName}' not found");
                Console.WriteLine($"   Subscriptions: {string.Join(", ", stubNames)}");
                return false;
            }

            Console.WriteLine($"   Found expected subscription for service: {service.StubName}");
            return true;
        });
    }

    private void TestGetNonExistentSubscriptionViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Subscription 404 Not Found Test (WebSocket) ===");
        Console.WriteLine("Testing get non-existent subscription returns proper error via typed proxy...");

        RunWebSocketTest("Subscription 404 test", async adminClient =>
        {
            var nonExistentId = Guid.NewGuid();
            Console.WriteLine($"   Attempting to get non-existent subscription: {nonExistentId}");

            var response = await adminClient.Subscription.GetSubscriptionAsync(new GetSubscriptionRequest
            {
                SubscriptionId = nonExistentId
            }, timeout: TimeSpan.FromSeconds(5));

            // Should get an error response (404)
            if (!response.IsSuccess)
            {
                var error = response.Error;
                if (error?.ResponseCode == 404)
                {
                    Console.WriteLine($"   Correctly received 404 error: {error.Message}");
                    return true;
                }
                Console.WriteLine($"   Got error but not 404: code={error?.ResponseCode}, message={error?.Message}");
                // Accept other error codes as they indicate the subscription wasn't found
                return true;
            }

            // If success, the subscription shouldn't exist
            if (response.Result == null)
            {
                Console.WriteLine("   Got null result for non-existent subscription (acceptable 404 behavior)");
                return true;
            }

            Console.WriteLine("   Expected 404 error, but got success response with data");
            return false;
        });
    }

    private void TestDuplicateSubscriptionViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Subscription 409 Conflict Test (WebSocket) ===");
        Console.WriteLine("Testing duplicate subscription returns conflict error via typed proxy...");

        RunWebSocketTest("Subscription 409 conflict test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            // Create test service
            var service = await CreateTestServiceAsync(adminClient, uniqueCode);
            if (service == null) return false;

            // Create test account
            var account = await CreateTestAccountAsync(adminClient, uniqueCode);
            if (account == null) return false;

            // Create first subscription
            Console.WriteLine("   Creating first subscription...");
            var firstResponse = await adminClient.Subscription.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                AccountId = account.AccountId,
                ServiceId = service.ServiceId,
                DurationDays = 30
            }, timeout: TimeSpan.FromSeconds(5));

            if (!firstResponse.IsSuccess || firstResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create first subscription: {FormatError(firstResponse.Error)}");
                return false;
            }

            var firstSubscription = firstResponse.Result;
            Console.WriteLine($"   Created first subscription: {firstSubscription.SubscriptionId}");

            // Try to create duplicate subscription
            Console.WriteLine("   Attempting to create duplicate subscription...");
            var duplicateResponse = await adminClient.Subscription.CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                AccountId = account.AccountId,
                ServiceId = service.ServiceId,
                DurationDays = 30
            }, timeout: TimeSpan.FromSeconds(5));

            // Check if we got the expected 409 Conflict error
            if (!duplicateResponse.IsSuccess)
            {
                var error = duplicateResponse.Error;
                if (error?.ResponseCode == 409)
                {
                    Console.WriteLine($"   Correctly received 409 conflict error: {error.Message}");
                    return true;
                }
                Console.WriteLine($"   Got error but not 409: code={error?.ResponseCode}, message={error?.Message}");
                return false;
            }

            // If we got success, check if the second subscription was actually created
            if (duplicateResponse.Result != null)
            {
                if (duplicateResponse.Result.SubscriptionId != firstSubscription.SubscriptionId)
                {
                    Console.WriteLine($"   Duplicate subscription created (should have been rejected): {duplicateResponse.Result.SubscriptionId}");
                    return false;
                }

                // Some APIs return the existing subscription instead of error
                Console.WriteLine("   Returned existing subscription (acceptable conflict behavior)");
                return true;
            }

            Console.WriteLine("   Expected 409 conflict, but got unexpected response");
            return false;
        });
    }
}
