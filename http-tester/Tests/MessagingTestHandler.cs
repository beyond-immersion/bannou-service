using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for the Messaging service HTTP API endpoints.
/// Tests pub/sub messaging infrastructure operations.
/// </summary>
public class MessagingTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
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
    ];

    private static Task<TestResult> TestListTopics(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messagingClient = GetServiceClient<IMessagingClient>();

            var response = await messagingClient.ListTopicsAsync(null);

            if (response == null)
                return TestResult.Failed("ListTopics returned null");

            return TestResult.Successful($"ListTopics returned {response.Topics?.Count ?? 0} topic(s)");
        }, "List topics");

    private static Task<TestResult> TestListTopicsWithFilter(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messagingClient = GetServiceClient<IMessagingClient>();

            var request = new ListTopicsRequest { ExchangeFilter = "test" };
            var response = await messagingClient.ListTopicsAsync(request);

            if (response == null)
                return TestResult.Failed("ListTopics with filter returned null");

            return TestResult.Successful($"ListTopics with filter returned {response.Topics?.Count ?? 0} topic(s)");
        }, "List topics with filter");

    private static Task<TestResult> TestPublishEvent(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messagingClient = GetServiceClient<IMessagingClient>();

            var request = new PublishEventRequest
            {
                Topic = "test.http-integration",
                Payload = new { message = "Integration test message", timestamp = DateTimeOffset.UtcNow }
            };

            var response = await messagingClient.PublishEventAsync(request);

            if (response == null)
                return TestResult.Failed("PublishEvent returned null");

            if (response.MessageId == Guid.Empty)
                return TestResult.Failed("PublishEvent returned empty message ID");

            return TestResult.Successful($"Published event with message ID: {response.MessageId}");
        }, "Publish event");

    private static Task<TestResult> TestPublishEventWithOptions(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messagingClient = GetServiceClient<IMessagingClient>();

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
                return TestResult.Failed("PublishEvent with options returned null");

            if (response.MessageId == Guid.Empty)
                return TestResult.Failed("PublishEvent with options returned empty message ID");

            return TestResult.Successful($"Published event with options, message ID: {response.MessageId}");
        }, "Publish event with options");

    private static Task<TestResult> TestPublishEventWithCorrelationId(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messagingClient = GetServiceClient<IMessagingClient>();

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
                return TestResult.Failed("PublishEvent with correlation ID returned null");

            if (response.MessageId == Guid.Empty)
                return TestResult.Failed("PublishEvent with correlation ID returned empty message ID");

            return TestResult.Successful($"Published event with correlation ID: {correlationId}, message ID: {response.MessageId}");
        }, "Publish event with correlation ID");

    private static Task<TestResult> TestCreateAndRemoveSubscription(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messagingClient = GetServiceClient<IMessagingClient>();

            // Create subscription
            var createRequest = new CreateSubscriptionRequest
            {
                Topic = "test.subscription-lifecycle",
                CallbackUrl = new Uri("http://localhost:5012/testing/callback")
            };

            var createResponse = await messagingClient.CreateSubscriptionAsync(createRequest);

            if (createResponse == null)
                return TestResult.Failed("CreateSubscription returned null");

            if (createResponse.SubscriptionId == Guid.Empty)
                return TestResult.Failed("CreateSubscription returned empty subscription ID");

            var subscriptionId = createResponse.SubscriptionId;

            // Remove subscription
            var removeRequest = new RemoveSubscriptionRequest
            {
                SubscriptionId = subscriptionId
            };

            var removeResponse = await messagingClient.RemoveSubscriptionAsync(removeRequest);

            if (removeResponse == null)
                return TestResult.Failed("RemoveSubscription returned null");

            if (!removeResponse.Success)
                return TestResult.Failed("RemoveSubscription returned success=false");

            return TestResult.Successful($"Subscription lifecycle complete: created and removed subscription {subscriptionId}");
        }, "Subscription lifecycle");

    private static Task<TestResult> TestRemoveNonExistentSubscription(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messagingClient = GetServiceClient<IMessagingClient>();

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
        }, "Remove non-existent subscription");
}
