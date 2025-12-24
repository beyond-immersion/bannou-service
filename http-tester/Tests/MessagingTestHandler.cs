using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for the Messaging service HTTP API endpoints.
/// Tests pub/sub messaging infrastructure operations.
/// </summary>
public class MessagingTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            // List Topics Tests
            new ServiceTest(TestListTopics, "ListTopics", "Messaging", "Test listing available topics"),
            new ServiceTest(TestListTopicsWithFilter, "ListTopicsWithFilter", "Messaging", "Test listing topics with filter"),

            // Publish Event Tests
            new ServiceTest(TestPublishEvent, "PublishEvent", "Messaging", "Test publishing an event to a topic"),
            new ServiceTest(TestPublishEventWithOptions, "PublishEventWithOptions", "Messaging", "Test publishing with publish options"),
            new ServiceTest(TestPublishEventWithCorrelationId, "PublishEventWithCorrelation", "Messaging", "Test publishing with correlation ID"),

            // Subscription Lifecycle Tests
            new ServiceTest(TestCreateAndRemoveSubscription, "SubscriptionLifecycle", "Messaging", "Test create and remove subscription"),
            new ServiceTest(TestRemoveNonExistentSubscription, "RemoveNonExistent", "Messaging", "Test removing non-existent subscription"),
        };
    }

    /// <summary>
    /// Test listing topics with no filter returns successfully.
    /// </summary>
    private static async Task<TestResult> TestListTopics(ITestClient client, string[] args)
    {
        try
        {
            var messagingClient = Program.ServiceProvider?.GetRequiredService<IMessagingClient>();
            if (messagingClient == null)
            {
                return TestResult.Failed("Messaging client not available");
            }

            var response = await messagingClient.ListTopicsAsync(null);

            if (response == null)
            {
                return TestResult.Failed("ListTopics returned null");
            }

            return TestResult.Successful($"ListTopics returned {response.Topics?.Count ?? 0} topic(s)");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"ListTopics failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test listing topics with a filter pattern.
    /// </summary>
    private static async Task<TestResult> TestListTopicsWithFilter(ITestClient client, string[] args)
    {
        try
        {
            var messagingClient = Program.ServiceProvider?.GetRequiredService<IMessagingClient>();
            if (messagingClient == null)
            {
                return TestResult.Failed("Messaging client not available");
            }

            var request = new ListTopicsRequest { ExchangeFilter = "test" };
            var response = await messagingClient.ListTopicsAsync(request);

            if (response == null)
            {
                return TestResult.Failed("ListTopics with filter returned null");
            }

            return TestResult.Successful($"ListTopics with filter returned {response.Topics?.Count ?? 0} topic(s)");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"ListTopics with filter failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test publishing an event to a topic.
    /// </summary>
    private static async Task<TestResult> TestPublishEvent(ITestClient client, string[] args)
    {
        try
        {
            var messagingClient = Program.ServiceProvider?.GetRequiredService<IMessagingClient>();
            if (messagingClient == null)
            {
                return TestResult.Failed("Messaging client not available");
            }

            var request = new PublishEventRequest
            {
                Topic = "test.http-integration",
                Payload = new { message = "Integration test message", timestamp = DateTimeOffset.UtcNow }
            };

            var response = await messagingClient.PublishEventAsync(request);

            if (response == null)
            {
                return TestResult.Failed("PublishEvent returned null");
            }

            if (response.MessageId == Guid.Empty)
            {
                return TestResult.Failed("PublishEvent returned empty message ID");
            }

            return TestResult.Successful($"Published event with message ID: {response.MessageId}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"PublishEvent failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test publishing an event with publish options.
    /// </summary>
    private static async Task<TestResult> TestPublishEventWithOptions(ITestClient client, string[] args)
    {
        try
        {
            var messagingClient = Program.ServiceProvider?.GetRequiredService<IMessagingClient>();
            if (messagingClient == null)
            {
                return TestResult.Failed("Messaging client not available");
            }

            var request = new PublishEventRequest
            {
                Topic = "test.http-integration-options",
                Payload = new { message = "Test with options" },
                Options = new PublishOptions
                {
                    Persistent = true,
                    Priority = 5
                }
            };

            var response = await messagingClient.PublishEventAsync(request);

            if (response == null)
            {
                return TestResult.Failed("PublishEvent with options returned null");
            }

            if (response.MessageId == Guid.Empty)
            {
                return TestResult.Failed("PublishEvent with options returned empty message ID");
            }

            return TestResult.Successful($"Published event with options, message ID: {response.MessageId}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"PublishEvent with options failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test publishing an event with correlation ID.
    /// </summary>
    private static async Task<TestResult> TestPublishEventWithCorrelationId(ITestClient client, string[] args)
    {
        try
        {
            var messagingClient = Program.ServiceProvider?.GetRequiredService<IMessagingClient>();
            if (messagingClient == null)
            {
                return TestResult.Failed("Messaging client not available");
            }

            var correlationId = Guid.NewGuid();
            var request = new PublishEventRequest
            {
                Topic = "test.http-integration-correlation",
                Payload = new { message = "Test with correlation" },
                Options = new PublishOptions
                {
                    CorrelationId = correlationId
                }
            };

            var response = await messagingClient.PublishEventAsync(request);

            if (response == null)
            {
                return TestResult.Failed("PublishEvent with correlation ID returned null");
            }

            if (response.MessageId == Guid.Empty)
            {
                return TestResult.Failed("PublishEvent with correlation ID returned empty message ID");
            }

            return TestResult.Successful($"Published event with correlation ID: {correlationId}, message ID: {response.MessageId}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"PublishEvent with correlation ID failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test subscription lifecycle: create and then remove.
    /// </summary>
    private static async Task<TestResult> TestCreateAndRemoveSubscription(ITestClient client, string[] args)
    {
        try
        {
            var messagingClient = Program.ServiceProvider?.GetRequiredService<IMessagingClient>();
            if (messagingClient == null)
            {
                return TestResult.Failed("Messaging client not available");
            }

            // Create subscription
            var createRequest = new CreateSubscriptionRequest
            {
                Topic = "test.subscription-lifecycle",
                CallbackUrl = new Uri("http://localhost:5012/v1.0/invoke/bannou/method/testing/callback")
            };

            var createResponse = await messagingClient.CreateSubscriptionAsync(createRequest);

            if (createResponse == null)
            {
                return TestResult.Failed("CreateSubscription returned null");
            }

            if (createResponse.SubscriptionId == Guid.Empty)
            {
                return TestResult.Failed("CreateSubscription returned empty subscription ID");
            }

            var subscriptionId = createResponse.SubscriptionId;

            // Remove subscription
            var removeRequest = new RemoveSubscriptionRequest
            {
                SubscriptionId = subscriptionId
            };

            var removeResponse = await messagingClient.RemoveSubscriptionAsync(removeRequest);

            if (removeResponse == null)
            {
                return TestResult.Failed("RemoveSubscription returned null");
            }

            if (!removeResponse.Success)
            {
                return TestResult.Failed("RemoveSubscription returned success=false");
            }

            return TestResult.Successful($"Subscription lifecycle complete: created and removed subscription {subscriptionId}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Subscription lifecycle test failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test removing a non-existent subscription returns appropriate response.
    /// </summary>
    private static async Task<TestResult> TestRemoveNonExistentSubscription(ITestClient client, string[] args)
    {
        try
        {
            var messagingClient = Program.ServiceProvider?.GetRequiredService<IMessagingClient>();
            if (messagingClient == null)
            {
                return TestResult.Failed("Messaging client not available");
            }

            var request = new RemoveSubscriptionRequest
            {
                SubscriptionId = Guid.NewGuid()
            };

            try
            {
                var response = await messagingClient.RemoveSubscriptionAsync(request);

                // The service returns 404 for non-existent subscriptions
                // NSwag client throws ApiException for non-200 responses
                return TestResult.Successful($"RemoveSubscription handled non-existent ID gracefully: success={response?.Success}");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("RemoveSubscription correctly returned 404 for non-existent subscription");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"RemoveNonExistentSubscription test failed: {ex.Message}");
        }
    }
}
