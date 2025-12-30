using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Testing;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Concrete message type for tap integration tests.
/// Required by T5 - no anonymous types for events.
/// </summary>
public record TapTestMessage(string Message, string Source, DateTimeOffset Timestamp);

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

        // Message Tap Tests
        new ServiceTest(TestCreateTap, "CreateTap", "Messaging", "Test creating a message tap"),
        new ServiceTest(TestTapForwardsMessages, "TapForwardsMessages", "Messaging", "Test that taps forward messages to destination"),
        new ServiceTest(TestMultipleTapsToSameDestination, "MultipleTaps", "Messaging", "Test multiple taps to same destination"),
        new ServiceTest(TestDisposeTapStopsForwarding, "DisposeTap", "Messaging", "Test disposing tap stops forwarding"),
        new ServiceTest(TestTappedEnvelopeMetadata, "TapMetadata", "Messaging", "Test tapped envelope contains correct metadata"),
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

    // =========================================================================
    // Message Tap Integration Tests
    // =========================================================================

    private static Task<TestResult> TestCreateTap(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messageTap = GetServiceClient<IMessageTap>();
            var testId = Guid.NewGuid().ToString("N")[..8];

            var destination = new TapDestination
            {
                Exchange = $"tap-test-dest-{testId}",
                RoutingKey = "tap.test.create",
                ExchangeType = TapExchangeType.Fanout,
                CreateExchangeIfNotExists = true
            };

            await using var tapHandle = await messageTap.CreateTapAsync(
                $"tap.source.create.{testId}",
                destination);

            if (tapHandle == null)
                return TestResult.Failed("CreateTapAsync returned null");

            if (tapHandle.TapId == Guid.Empty)
                return TestResult.Failed("TapHandle has empty TapId");

            if (!tapHandle.IsActive)
                return TestResult.Failed("TapHandle is not active after creation");

            if (tapHandle.SourceTopic != $"tap.source.create.{testId}")
                return TestResult.Failed($"SourceTopic mismatch: expected 'tap.source.create.{testId}', got '{tapHandle.SourceTopic}'");

            if (tapHandle.Destination.Exchange != destination.Exchange)
                return TestResult.Failed($"Destination exchange mismatch");

            return TestResult.Successful($"Created tap {tapHandle.TapId} from {tapHandle.SourceTopic} to {destination.Exchange}");
        }, "Create message tap");

    private static Task<TestResult> TestTapForwardsMessages(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messageTap = GetServiceClient<IMessageTap>();
            var messageBus = GetServiceClient<IMessageBus>();
            var messageSubscriber = GetServiceClient<IMessageSubscriber>();
            var testId = Guid.NewGuid().ToString("N")[..8];

            var receivedMessages = new ConcurrentBag<TappedMessageEnvelope>();
            var messageReceived = new TaskCompletionSource<bool>();

            // Source exchange simulates a character's event stream (fanout exchange)
            var sourceExchange = $"character-events-{testId}";
            var sourceTopic = "events";  // For fanout, topic is just for naming/logging

            // Create destination for tap (session's event queue)
            var destination = new TapDestination
            {
                Exchange = $"session-events-{testId}",
                RoutingKey = $"session.{testId}",
                ExchangeType = TapExchangeType.Fanout,
                CreateExchangeIfNotExists = true
            };

            // Subscribe to destination to receive forwarded messages
            var destinationTopic = $"{destination.Exchange}.{destination.RoutingKey}";
            await using var subscription = await messageSubscriber.SubscribeDynamicAsync<TappedMessageEnvelope>(
                destinationTopic,
                async (envelope, ct) =>
                {
                    receivedMessages.Add(envelope);
                    messageReceived.TrySetResult(true);
                    await Task.CompletedTask;
                });

            // Create tap from character's event exchange to session's destination
            await using var tapHandle = await messageTap.CreateTapAsync(
                sourceTopic,
                destination,
                sourceExchange);  // Explicit source exchange

            // Publish message to the character's event exchange (fanout)
            var testEvent = new TapTestMessage("Tap forward test", "forward-test", DateTimeOffset.UtcNow);
            var publishOptions = new PublishOptions
            {
                Exchange = sourceExchange,
                ExchangeType = PublishOptionsExchangeType.Fanout
            };
            await messageBus.PublishAsync(sourceTopic, testEvent, publishOptions);

            // Wait for message to be forwarded (with timeout)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await messageReceived.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return TestResult.Failed("Timeout waiting for tapped message to arrive at destination");
            }

            if (receivedMessages.IsEmpty)
                return TestResult.Failed("No messages received at destination");

            var received = receivedMessages.First();
            if (received.TapId != tapHandle.TapId)
                return TestResult.Failed($"TapId mismatch: expected {tapHandle.TapId}, got {received.TapId}");

            return TestResult.Successful($"Tap {tapHandle.TapId} successfully forwarded message from {sourceExchange} to {destinationTopic}");
        }, "Tap forwards messages");

    private static Task<TestResult> TestMultipleTapsToSameDestination(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messageTap = GetServiceClient<IMessageTap>();
            var messageBus = GetServiceClient<IMessageBus>();
            var messageSubscriber = GetServiceClient<IMessageSubscriber>();
            var testId = Guid.NewGuid().ToString("N")[..8];

            var receivedMessages = new ConcurrentBag<TappedMessageEnvelope>();
            var messagesReceived = new TaskCompletionSource<bool>();
            var expectedCount = 2;

            // Two character event exchanges (simulating two characters owned by same session)
            var sourceExchange1 = $"character-events-A-{testId}";
            var sourceExchange2 = $"character-events-B-{testId}";

            // Shared destination (session's event queue)
            var destination = new TapDestination
            {
                Exchange = $"session-events-{testId}",
                RoutingKey = $"session.{testId}",
                ExchangeType = TapExchangeType.Fanout,
                CreateExchangeIfNotExists = true
            };

            // Subscribe to destination
            var destinationTopic = $"{destination.Exchange}.{destination.RoutingKey}";
            await using var subscription = await messageSubscriber.SubscribeDynamicAsync<TappedMessageEnvelope>(
                destinationTopic,
                async (envelope, ct) =>
                {
                    receivedMessages.Add(envelope);
                    if (receivedMessages.Count >= expectedCount)
                        messagesReceived.TrySetResult(true);
                    await Task.CompletedTask;
                });

            // Create taps from both character exchanges to session destination
            await using var tap1 = await messageTap.CreateTapAsync("events", destination, sourceExchange1);
            await using var tap2 = await messageTap.CreateTapAsync("events", destination, sourceExchange2);

            // Publish to both character exchanges
            var publishOptions1 = new PublishOptions { Exchange = sourceExchange1, ExchangeType = PublishOptionsExchangeType.Fanout };
            var publishOptions2 = new PublishOptions { Exchange = sourceExchange2, ExchangeType = PublishOptionsExchangeType.Fanout };
            var event1 = new TapTestMessage("Multi-tap test", "charA", DateTimeOffset.UtcNow);
            var event2 = new TapTestMessage("Multi-tap test", "charB", DateTimeOffset.UtcNow);
            await messageBus.PublishAsync("events", event1, publishOptions1);
            await messageBus.PublishAsync("events", event2, publishOptions2);

            // Wait for both messages
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await messagesReceived.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return TestResult.Failed($"Timeout: received {receivedMessages.Count} of {expectedCount} expected messages");
            }

            // Verify we got messages from both taps
            var tapIds = receivedMessages.Select(m => m.TapId).Distinct().ToList();
            if (tapIds.Count < 2)
                return TestResult.Failed($"Expected messages from 2 different taps, got {tapIds.Count}");

            if (!tapIds.Contains(tap1.TapId) || !tapIds.Contains(tap2.TapId))
                return TestResult.Failed("Did not receive messages from both expected taps");

            return TestResult.Successful($"Both character taps ({tap1.TapId}, {tap2.TapId}) forwarded to session {destinationTopic}");
        }, "Multiple taps to same destination");

    private static Task<TestResult> TestDisposeTapStopsForwarding(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messageTap = GetServiceClient<IMessageTap>();
            var messageBus = GetServiceClient<IMessageBus>();
            var messageSubscriber = GetServiceClient<IMessageSubscriber>();
            var testId = Guid.NewGuid().ToString("N")[..8];

            var receivedMessages = new ConcurrentBag<TappedMessageEnvelope>();
            var firstMessageReceived = new TaskCompletionSource<bool>();

            // Source exchange (character's event stream)
            var sourceExchange = $"character-events-dispose-{testId}";

            var destination = new TapDestination
            {
                Exchange = $"session-events-dispose-{testId}",
                RoutingKey = $"session.{testId}",
                ExchangeType = TapExchangeType.Fanout,
                CreateExchangeIfNotExists = true
            };

            var destinationTopic = $"{destination.Exchange}.{destination.RoutingKey}";
            await using var subscription = await messageSubscriber.SubscribeDynamicAsync<TappedMessageEnvelope>(
                destinationTopic,
                async (envelope, ct) =>
                {
                    receivedMessages.Add(envelope);
                    firstMessageReceived.TrySetResult(true);
                    await Task.CompletedTask;
                });

            // Create tap with explicit source exchange
            var tapHandle = await messageTap.CreateTapAsync("events", destination, sourceExchange);
            var tapId = tapHandle.TapId;

            // Publish first message - should be forwarded
            var publishOptions = new PublishOptions { Exchange = sourceExchange, ExchangeType = PublishOptionsExchangeType.Fanout };
            var event1 = new TapTestMessage("Dispose test", "before_dispose", DateTimeOffset.UtcNow);
            await messageBus.PublishAsync("events", event1, publishOptions);

            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await firstMessageReceived.Task.WaitAsync(cts1.Token);
            }
            catch (OperationCanceledException)
            {
                return TestResult.Failed("Timeout waiting for first message before dispose");
            }

            var countBeforeDispose = receivedMessages.Count;
            if (countBeforeDispose == 0)
                return TestResult.Failed("No message received before dispose");

            // Dispose the tap
            await tapHandle.DisposeAsync();

            if (tapHandle.IsActive)
                return TestResult.Failed("TapHandle still active after dispose");

            // Wait a moment for dispose to complete
            await Task.Delay(500);

            // Publish second message - should NOT be forwarded
            var event2 = new TapTestMessage("Dispose test", "after_dispose", DateTimeOffset.UtcNow);
            await messageBus.PublishAsync("events", event2, publishOptions);

            // Wait to see if message arrives (it shouldn't)
            await Task.Delay(2000);

            var countAfterDispose = receivedMessages.Count;

            // We should only have the message from before dispose
            if (countAfterDispose > countBeforeDispose)
                return TestResult.Failed($"Message forwarded after dispose: before={countBeforeDispose}, after={countAfterDispose}");

            return TestResult.Successful($"Tap {tapId} stopped forwarding after dispose (received {countBeforeDispose} before, {countAfterDispose} after)");
        }, "Dispose tap stops forwarding");

    private static Task<TestResult> TestTappedEnvelopeMetadata(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messageTap = GetServiceClient<IMessageTap>();
            var messageBus = GetServiceClient<IMessageBus>();
            var messageSubscriber = GetServiceClient<IMessageSubscriber>();
            var testId = Guid.NewGuid().ToString("N")[..8];

            TappedMessageEnvelope? receivedEnvelope = null;
            var messageReceived = new TaskCompletionSource<bool>();

            // Source exchange (character's event stream)
            var sourceExchange = $"character-events-meta-{testId}";
            var sourceTopic = "events";

            var destination = new TapDestination
            {
                Exchange = $"session-events-meta-{testId}",
                RoutingKey = $"session.{testId}",
                ExchangeType = TapExchangeType.Direct,
                CreateExchangeIfNotExists = true
            };

            var destinationTopic = $"{destination.Exchange}.{destination.RoutingKey}";
            await using var subscription = await messageSubscriber.SubscribeDynamicAsync<TappedMessageEnvelope>(
                destinationTopic,
                async (envelope, ct) =>
                {
                    receivedEnvelope = envelope;
                    messageReceived.TrySetResult(true);
                    await Task.CompletedTask;
                });

            var beforeCreate = DateTimeOffset.UtcNow;

            await using var tapHandle = await messageTap.CreateTapAsync(sourceTopic, destination, sourceExchange);

            // Publish message to source exchange
            var publishOptions = new PublishOptions { Exchange = sourceExchange, ExchangeType = PublishOptionsExchangeType.Fanout };
            var testEvent = new TapTestMessage("Metadata test", "metadata-test", DateTimeOffset.UtcNow);
            await messageBus.PublishAsync(sourceTopic, testEvent, publishOptions);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await messageReceived.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return TestResult.Failed("Timeout waiting for tapped message");
            }

            if (receivedEnvelope == null)
                return TestResult.Failed("Received envelope is null");

            // Verify metadata
            var errors = new List<string>();

            if (receivedEnvelope.TapId != tapHandle.TapId)
                errors.Add($"TapId: expected {tapHandle.TapId}, got {receivedEnvelope.TapId}");

            if (receivedEnvelope.SourceTopic != sourceTopic)
                errors.Add($"SourceTopic: expected {sourceTopic}, got {receivedEnvelope.SourceTopic}");

            if (receivedEnvelope.DestinationExchange != destination.Exchange)
                errors.Add($"DestinationExchange: expected {destination.Exchange}, got {receivedEnvelope.DestinationExchange}");

            if (receivedEnvelope.DestinationRoutingKey != destination.RoutingKey)
                errors.Add($"DestinationRoutingKey: expected {destination.RoutingKey}, got {receivedEnvelope.DestinationRoutingKey}");

            if (receivedEnvelope.DestinationExchangeType != "direct")
                errors.Add($"DestinationExchangeType: expected 'direct', got {receivedEnvelope.DestinationExchangeType}");

            if (receivedEnvelope.TapCreatedAt < beforeCreate)
                errors.Add($"TapCreatedAt too early: {receivedEnvelope.TapCreatedAt}");

            if (receivedEnvelope.ForwardedAt < receivedEnvelope.TapCreatedAt)
                errors.Add($"ForwardedAt ({receivedEnvelope.ForwardedAt}) before TapCreatedAt ({receivedEnvelope.TapCreatedAt})");

            if (errors.Count > 0)
                return TestResult.Failed($"Metadata errors: {string.Join("; ", errors)}");

            return TestResult.Successful($"TappedMessageEnvelope metadata verified: TapId={receivedEnvelope.TapId}, SourceExchange={sourceExchange}");
        }, "Tapped envelope metadata");
}
