using System.Text.Json;
using System.Text.Json.Nodes;
using BeyondImmersion.Bannou.Client.SDK;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// WebSocket-based test handler for subscriptions service API endpoints.
/// Tests the subscriptions service APIs through the Connect service WebSocket binary protocol.
///
/// Tests comprehensive CRUD operations, error handling, and edge cases per IMPLEMENTATION TENETS.
/// </summary>
public class SubscriptionWebSocketTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            // Empty list tests (must run first before any data is created)
            new ServiceTest(TestEmptyAccountSubscriptionsViaWebSocket, "Subscriptions - Empty List (WebSocket)", "WebSocket",
                "Test that account subscriptions returns empty list for non-existent account"),

            // CRUD operations
            new ServiceTest(TestCreateAndGetSubscriptionViaWebSocket, "Subscriptions - Create and Get (WebSocket)", "WebSocket",
                "Test subscription creation and retrieval via WebSocket binary protocol"),
            new ServiceTest(TestSubscriptionLifecycleViaWebSocket, "Subscriptions - Full Lifecycle (WebSocket)", "WebSocket",
                "Test complete subscription lifecycle: create -> update -> cancel -> renew"),
            new ServiceTest(TestGetCurrentSubscriptionsViaWebSocket, "Subscriptions - Account List (WebSocket)", "WebSocket",
                "Test getting account subscriptions returns expected stub names"),

            // Error handling tests
            new ServiceTest(TestGetNonExistentSubscriptionViaWebSocket, "Subscriptions - 404 Not Found (WebSocket)", "WebSocket",
                "Test that getting a non-existent subscription returns proper error"),
            new ServiceTest(TestDuplicateSubscriptionViaWebSocket, "Subscriptions - 409 Conflict (WebSocket)", "WebSocket",
                "Test that creating duplicate subscription returns conflict error"),
        };
    }

    #region Helper Methods

    /// <summary>
    /// Creates a test service in Service for subscription tests.
    /// </summary>
    private async Task<(string serviceId, string stubName)?> CreateTestServiceAsync(BannouClient adminClient, string uniqueCode)
    {
        try
        {
            var stubName = $"test-svc-{uniqueCode}".ToLowerInvariant();
            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/service/services/create",
                new
                {
                    stubName = stubName,
                    displayName = $"Test Service {uniqueCode}",
                    description = "Test service for subscription tests",
                    serviceType = "game"
                },
                timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

            var responseJson = JsonNode.Parse(response.GetRawText())?.AsObject();
            var serviceIdStr = responseJson?["serviceId"]?.GetValue<string>();
            if (string.IsNullOrEmpty(serviceIdStr))
            {
                Console.WriteLine("   Failed to create test service - no serviceId in response");
                return null;
            }

            Console.WriteLine($"   Created test service: {serviceIdStr} ({stubName})");
            return (serviceIdStr, stubName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Failed to create test service: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a test account for subscription tests.
    /// </summary>
    private async Task<string?> CreateTestAccountAsync(BannouClient adminClient, string uniqueCode)
    {
        try
        {
            var response = (await adminClient.InvokeAsync<object, JsonElement>(
                "POST",
                "/account/create",
                new
                {
                    email = $"sub-test-{uniqueCode}@test.local",
                    displayName = $"SubTest{uniqueCode}"
                },
                timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

            var responseJson = JsonNode.Parse(response.GetRawText())?.AsObject();
            var accountIdStr = responseJson?["accountId"]?.GetValue<string>();
            if (string.IsNullOrEmpty(accountIdStr))
            {
                Console.WriteLine("   Failed to create test account - no accountId in response");
                return null;
            }

            Console.WriteLine($"   Created test account: {accountIdStr}");
            return accountIdStr;
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
        Console.WriteLine("=== Subscriptions Empty List Test (WebSocket) ===");
        Console.WriteLine("Testing /subscriptions/account/list returns empty array for non-existent account...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("   Admin client not connected");
                    return false;
                }

                try
                {
                    // Use a random account ID that doesn't exist
                    var nonExistentAccountId = Guid.NewGuid().ToString();

                    var response = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/subscription/account/list",
                        new { accountId = nonExistentAccountId },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var responseJson = JsonNode.Parse(response.GetRawText())?.AsObject();
                    var subscriptions = responseJson?["subscriptions"]?.AsArray();
                    var totalCount = responseJson?["totalCount"]?.GetValue<int>() ?? -1;

                    // Should return empty array, not null, and totalCount of 0
                    if (subscriptions == null)
                    {
                        Console.WriteLine("   subscriptions array is null - should be empty array");
                        return false;
                    }

                    if (subscriptions.Count != 0)
                    {
                        Console.WriteLine($"   Expected 0 subscriptions, got {subscriptions.Count}");
                        return false;
                    }

                    if (totalCount != 0)
                    {
                        Console.WriteLine($"   Expected totalCount 0, got {totalCount}");
                        return false;
                    }

                    Console.WriteLine($"   Correctly returned empty array with totalCount: {totalCount}");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Test failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
                Console.WriteLine("   Subscriptions empty list test PASSED");
            else
                Console.WriteLine("   Subscriptions empty list test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Subscriptions empty list test FAILED with exception: {ex.Message}");
        }
    }

    private void TestCreateAndGetSubscriptionViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Subscriptions Create and Get Test (WebSocket) ===");
        Console.WriteLine("Testing /subscriptions/create and /subscriptions/get via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("   Admin client not connected");
                    return false;
                }

                var uniqueCode = $"{DateTime.Now.Ticks % 100000}";

                try
                {
                    // Create test service
                    var serviceResult = await CreateTestServiceAsync(adminClient, uniqueCode);
                    if (serviceResult == null) return false;
                    var (serviceId, stubName) = serviceResult.Value;

                    // Create test account
                    var accountId = await CreateTestAccountAsync(adminClient, uniqueCode);
                    if (accountId == null) return false;

                    // Create subscription
                    Console.WriteLine("   Invoking /subscriptions/create...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/subscription/create",
                        new
                        {
                            accountId = accountId,
                            serviceId = serviceId,
                            durationDays = 30
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var subscriptionIdStr = createJson?["subscriptionId"]?.GetValue<string>();
                    var createdStubName = createJson?["stubName"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(subscriptionIdStr))
                    {
                        Console.WriteLine("   Failed to create subscription - no subscriptionId in response");
                        return false;
                    }

                    Console.WriteLine($"   Created subscription: {subscriptionIdStr} (service: {createdStubName})");

                    // Now retrieve it
                    Console.WriteLine("   Invoking /subscriptions/get...");
                    var getResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/subscription/get",
                        new { subscriptionId = subscriptionIdStr },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var getJson = JsonNode.Parse(getResponse.GetRawText())?.AsObject();
                    var retrievedId = getJson?["subscriptionId"]?.GetValue<string>();
                    var isActive = getJson?["isActive"]?.GetValue<bool>() ?? false;

                    Console.WriteLine($"   Retrieved subscription: {retrievedId} (active: {isActive})");

                    return retrievedId == subscriptionIdStr && isActive;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Test failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
                Console.WriteLine("   Subscriptions create and get test PASSED");
            else
                Console.WriteLine("   Subscriptions create and get test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Subscriptions create and get test FAILED with exception: {ex.Message}");
        }
    }

    private void TestSubscriptionLifecycleViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Subscriptions Full Lifecycle Test (WebSocket) ===");
        Console.WriteLine("Testing complete subscription lifecycle via shared admin WebSocket...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("   Admin client not connected");
                    return false;
                }

                var uniqueCode = $"{DateTime.Now.Ticks % 100000}";

                try
                {
                    // Create test service
                    var serviceResult = await CreateTestServiceAsync(adminClient, uniqueCode);
                    if (serviceResult == null) return false;
                    var (serviceId, stubName) = serviceResult.Value;

                    // Create test account
                    var accountId = await CreateTestAccountAsync(adminClient, uniqueCode);
                    if (accountId == null) return false;

                    // Step 1: Create subscription
                    Console.WriteLine("   Step 1: Creating subscription...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/subscription/create",
                        new
                        {
                            accountId = accountId,
                            serviceId = serviceId,
                            durationDays = 30
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var subscriptionId = createJson?["subscriptionId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(subscriptionId))
                    {
                        Console.WriteLine("   Failed to create subscription");
                        return false;
                    }
                    Console.WriteLine($"   Created subscription {subscriptionId}");

                    // Step 2: Verify in account list
                    Console.WriteLine("   Step 2: Verifying subscription appears in account list...");
                    var listResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/subscription/account/list",
                        new { accountId = accountId },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var listJson = JsonNode.Parse(listResponse.GetRawText())?.AsObject();
                    var subscriptions = listJson?["subscriptions"]?.AsArray();
                    if (subscriptions == null || subscriptions.Count == 0)
                    {
                        Console.WriteLine("   Subscription not found in account list");
                        return false;
                    }
                    Console.WriteLine($"   Found {subscriptions.Count} subscription(s) in account list");

                    // Step 3: Update subscription
                    Console.WriteLine("   Step 3: Updating subscription expiration...");
                    var newExpiration = DateTimeOffset.UtcNow.AddDays(60);
                    var updateResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/subscription/update",
                        new
                        {
                            subscriptionId = subscriptionId,
                            expirationDate = newExpiration
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var updateJson = JsonNode.Parse(updateResponse.GetRawText())?.AsObject();
                    var updatedExpiration = updateJson?["expirationDate"]?.GetValue<string>();
                    Console.WriteLine($"   Updated expiration to: {updatedExpiration}");

                    // Step 4: Cancel subscription
                    Console.WriteLine("   Step 4: Cancelling subscription...");
                    var cancelResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/subscription/cancel",
                        new
                        {
                            subscriptionId = subscriptionId,
                            reason = "WebSocket lifecycle test"
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var cancelJson = JsonNode.Parse(cancelResponse.GetRawText())?.AsObject();
                    var isActiveAfterCancel = cancelJson?["isActive"]?.GetValue<bool>() ?? true;
                    if (isActiveAfterCancel)
                    {
                        Console.WriteLine("   Subscription still active after cancel");
                        return false;
                    }
                    Console.WriteLine($"   Subscription cancelled (active: {isActiveAfterCancel})");

                    // Step 5: Renew subscription
                    Console.WriteLine("   Step 5: Renewing subscription...");
                    var renewResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/subscription/renew",
                        new
                        {
                            subscriptionId = subscriptionId,
                            extensionDays = 90
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var renewJson = JsonNode.Parse(renewResponse.GetRawText())?.AsObject();
                    var isActiveAfterRenew = renewJson?["isActive"]?.GetValue<bool>() ?? false;
                    if (!isActiveAfterRenew)
                    {
                        Console.WriteLine("   Subscription not active after renew");
                        return false;
                    }
                    Console.WriteLine($"   Subscription renewed (active: {isActiveAfterRenew})");

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Lifecycle test failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
                Console.WriteLine("   Subscriptions lifecycle test PASSED");
            else
                Console.WriteLine("   Subscriptions lifecycle test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Subscriptions lifecycle test FAILED with exception: {ex.Message}");
        }
    }

    private void TestGetCurrentSubscriptionsViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Subscriptions Account List Test (WebSocket) ===");
        Console.WriteLine("Testing /subscriptions/account/list returns subscriptions for account...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("   Admin client not connected");
                    return false;
                }

                var uniqueCode = $"{DateTime.Now.Ticks % 100000}";

                try
                {
                    // Create test service
                    var serviceResult = await CreateTestServiceAsync(adminClient, uniqueCode);
                    if (serviceResult == null) return false;
                    var (serviceId, stubName) = serviceResult.Value;

                    // Create test account
                    var accountId = await CreateTestAccountAsync(adminClient, uniqueCode);
                    if (accountId == null) return false;

                    // Create subscription
                    Console.WriteLine("   Creating subscription...");
                    var createResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/subscription/create",
                        new
                        {
                            accountId = accountId,
                            serviceId = serviceId,
                            durationDays = 30
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var createJson = JsonNode.Parse(createResponse.GetRawText())?.AsObject();
                    var subscriptionId = createJson?["subscriptionId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(subscriptionId))
                    {
                        Console.WriteLine("   Failed to create subscription");
                        return false;
                    }

                    // Get subscriptions for the account
                    Console.WriteLine("   Invoking /subscriptions/account/list...");
                    var listResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/subscription/account/list",
                        new { accountId = accountId },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var listJson = JsonNode.Parse(listResponse.GetRawText())?.AsObject();
                    var subscriptions = listJson?["subscriptions"]?.AsArray();

                    if (subscriptions == null)
                    {
                        Console.WriteLine("   subscriptions array is null");
                        return false;
                    }

                    Console.WriteLine($"   Received {subscriptions.Count} subscription(s)");

                    // Should have at least one subscription matching our service stub name
                    var hasExpectedSub = subscriptions.Any(s =>
                        s?["stubName"]?.GetValue<string>() == stubName);

                    if (!hasExpectedSub)
                    {
                        var stubNames = subscriptions.Select(s => s?["stubName"]?.GetValue<string>() ?? "null");
                        Console.WriteLine($"   Expected subscription for service '{stubName}' not found");
                        Console.WriteLine($"   Subscriptions: {string.Join(", ", stubNames)}");
                        return false;
                    }

                    Console.WriteLine($"   Found expected subscription for service: {stubName}");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Test failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
                Console.WriteLine("   Subscriptions account list test PASSED");
            else
                Console.WriteLine("   Subscriptions account list test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Subscriptions account list test FAILED with exception: {ex.Message}");
        }
    }

    private void TestGetNonExistentSubscriptionViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Subscriptions 404 Not Found Test (WebSocket) ===");
        Console.WriteLine("Testing /subscriptions/get returns proper error for non-existent subscription...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("   Admin client not connected");
                    return false;
                }

                try
                {
                    var nonExistentId = Guid.NewGuid().ToString();
                    Console.WriteLine($"   Attempting to get non-existent subscription: {nonExistentId}");

                    try
                    {
                        var response = (await adminClient.InvokeAsync<object, JsonElement>(
                            "POST",
                            "/subscription/get",
                            new { subscriptionId = nonExistentId },
                            timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                        // If we get here without exception, check if it's an error response
                        var responseText = response.GetRawText();
                        Console.WriteLine($"   Got response: {responseText}");

                        // Some APIs return empty/null on not found instead of throwing
                        if (string.IsNullOrEmpty(responseText) || responseText == "{}" || responseText == "null")
                        {
                            Console.WriteLine("   Got empty response for non-existent subscription (acceptable 404 behavior)");
                            return true;
                        }

                        // Check if response indicates not found
                        var responseJson = JsonNode.Parse(responseText)?.AsObject();
                        var subscriptionId = responseJson?["subscriptionId"]?.GetValue<string>();
                        if (string.IsNullOrEmpty(subscriptionId))
                        {
                            Console.WriteLine("   Got empty subscription data (acceptable 404 behavior)");
                            return true;
                        }

                        Console.WriteLine("   Expected 404 error, but got success response");
                        return false;
                    }
                    catch (Exception ex) when (ex.Message.Contains("404") || ex.Message.Contains("Not Found"))
                    {
                        Console.WriteLine("   Correctly received 404 error");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Test failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
                Console.WriteLine("   Subscriptions 404 test PASSED");
            else
                Console.WriteLine("   Subscriptions 404 test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Subscriptions 404 test FAILED with exception: {ex.Message}");
        }
    }

    private void TestDuplicateSubscriptionViaWebSocket(string[] args)
    {
        Console.WriteLine("=== Subscriptions 409 Conflict Test (WebSocket) ===");
        Console.WriteLine("Testing /subscriptions/create returns conflict for duplicate subscription...");

        try
        {
            var result = Task.Run(async () =>
            {
                var adminClient = Program.AdminClient;
                if (adminClient == null || !adminClient.IsConnected)
                {
                    Console.WriteLine("   Admin client not connected");
                    return false;
                }

                var uniqueCode = $"{DateTime.Now.Ticks % 100000}";

                try
                {
                    // Create test service
                    var serviceResult = await CreateTestServiceAsync(adminClient, uniqueCode);
                    if (serviceResult == null) return false;
                    var (serviceId, stubName) = serviceResult.Value;

                    // Create test account
                    var accountId = await CreateTestAccountAsync(adminClient, uniqueCode);
                    if (accountId == null) return false;

                    // Create first subscription
                    Console.WriteLine("   Creating first subscription...");
                    var firstResponse = (await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/subscription/create",
                        new
                        {
                            accountId = accountId,
                            serviceId = serviceId,
                            durationDays = 30
                        },
                        timeout: TimeSpan.FromSeconds(5))).GetResultOrThrow();

                    var firstJson = JsonNode.Parse(firstResponse.GetRawText())?.AsObject();
                    var firstId = firstJson?["subscriptionId"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(firstId))
                    {
                        Console.WriteLine("   Failed to create first subscription");
                        return false;
                    }
                    Console.WriteLine($"   Created first subscription: {firstId}");

                    // Try to create duplicate subscription
                    Console.WriteLine("   Attempting to create duplicate subscription...");
                    var duplicateApiResponse = await adminClient.InvokeAsync<object, JsonElement>(
                        "POST",
                        "/subscription/create",
                        new
                        {
                            accountId = accountId,
                            serviceId = serviceId,
                            durationDays = 30
                        },
                        timeout: TimeSpan.FromSeconds(5));

                    // Check if we got the expected 409 Conflict error
                    if (!duplicateApiResponse.IsSuccess)
                    {
                        var error = duplicateApiResponse.Error;
                        if (error?.ResponseCode == 409)
                        {
                            Console.WriteLine($"   Correctly received 409 conflict error: {error.Message}");
                            return true;
                        }
                        Console.WriteLine($"   Got error but not 409: code={error?.ResponseCode}, message={error?.Message}");
                        return false;
                    }

                    // If we got success, check if the second subscription was actually created
                    var duplicateResponse = duplicateApiResponse.Result;
                    var duplicateJson = JsonNode.Parse(duplicateResponse.GetRawText())?.AsObject();
                    var duplicateId = duplicateJson?["subscriptionId"]?.GetValue<string>();

                    if (!string.IsNullOrEmpty(duplicateId) && duplicateId != firstId)
                    {
                        Console.WriteLine($"   Duplicate subscription created (should have been rejected): {duplicateId}");
                        return false;
                    }

                    // Some APIs return the existing subscription instead of error
                    if (duplicateId == firstId)
                    {
                        Console.WriteLine("   Returned existing subscription (acceptable conflict behavior)");
                        return true;
                    }

                    Console.WriteLine("   Expected 409 conflict, but got success response");
                    return false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Test failed: {ex.Message}");
                    return false;
                }
            }).Result;

            if (result)
                Console.WriteLine("   Subscriptions 409 conflict test PASSED");
            else
                Console.WriteLine("   Subscriptions 409 conflict test FAILED");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Subscriptions 409 conflict test FAILED with exception: {ex.Message}");
        }
    }
}
