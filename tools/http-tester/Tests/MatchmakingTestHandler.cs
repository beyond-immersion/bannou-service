using BeyondImmersion.BannouService.Matchmaking;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for matchmaking API endpoints using generated clients.
/// Tests the matchmaking service APIs directly via NSwag-generated MatchmakingClient.
/// </summary>
public class MatchmakingTestHandler : BaseHttpTestHandler
{
    private const string STATE_STORE = "matchmaking-statestore";

    public override ServiceTest[] GetServiceTests() =>
    [
        // Queue management tests
        new ServiceTest(TestListQueues, "ListQueues", "Matchmaking", "Test queue listing endpoint"),
        new ServiceTest(TestCreateQueue, "CreateQueue", "Matchmaking", "Test queue creation endpoint"),
        new ServiceTest(TestGetQueue, "GetQueue", "Matchmaking", "Test queue retrieval endpoint"),
        new ServiceTest(TestUpdateQueue, "UpdateQueue", "Matchmaking", "Test queue update endpoint"),
        new ServiceTest(TestDeleteQueue, "DeleteQueue", "Matchmaking", "Test queue deletion endpoint"),

        // Matchmaking lifecycle tests
        new ServiceTest(TestJoinMatchmaking, "JoinMatchmaking", "Matchmaking", "Test joining matchmaking queue"),
        new ServiceTest(TestLeaveMatchmaking, "LeaveMatchmaking", "Matchmaking", "Test leaving matchmaking queue"),
        new ServiceTest(TestGetMatchmakingStatus, "GetMatchmakingStatus", "Matchmaking", "Test matchmaking status retrieval"),

        // Stats test
        new ServiceTest(TestGetMatchmakingStats, "GetMatchmakingStats", "Matchmaking", "Test matchmaking stats retrieval"),

        // Error handling tests
        new ServiceTest(TestGetNonExistentQueue, "GetNonExistentQueue", "Matchmaking", "Test 404 for non-existent queue"),
        new ServiceTest(TestAcceptNonExistentMatch, "AcceptNonExistentMatch", "Matchmaking", "Test 404 for accepting non-existent match"),
        new ServiceTest(TestDeclineNonExistentMatch, "DeclineNonExistentMatch", "Matchmaking", "Test 404 for declining non-existent match"),

        // Complete lifecycle test
        new ServiceTest(TestCompleteMatchmakingLifecycle, "CompleteMatchmakingLifecycle", "Matchmaking", "Test complete matchmaking lifecycle: create queue -> join -> status -> leave -> delete"),
    ];

    private static async Task<TestResult> TestListQueues(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var matchmakingClient = GetServiceClient<IMatchmakingClient>();

            var request = new ListQueuesRequest { GameId = "test-game" };
            var response = await matchmakingClient.ListQueuesAsync(request);

            if (response.Queues == null)
                return TestResult.Failed("ListQueues returned null queues array");

            return TestResult.Successful($"Listed {response.Queues.Count} queues for game 'test-game'");
        }, "List queues");

    private static async Task<TestResult> TestCreateQueue(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var matchmakingClient = GetServiceClient<IMatchmakingClient>();

            var queueId = $"test-queue-{DateTime.Now.Ticks}";
            var request = new CreateQueueRequest
            {
                QueueId = queueId,
                GameId = "test-game",
                DisplayName = "Test Queue",
                Description = "A test queue for HTTP tests",
                MinCount = 2,
                MaxCount = 4,
                UseSkillRating = true,
                MatchAcceptTimeoutSeconds = 30
            };

            var response = await matchmakingClient.CreateQueueAsync(request);

            if (response.QueueId != queueId)
                return TestResult.Failed($"Queue ID mismatch: expected '{queueId}', got '{response.QueueId}'");

            if (response.DisplayName != "Test Queue")
                return TestResult.Failed($"Display name mismatch: expected 'Test Queue', got '{response.DisplayName}'");

            // Cleanup: delete the queue
            await matchmakingClient.DeleteQueueAsync(new DeleteQueueRequest { QueueId = queueId });

            return TestResult.Successful($"Queue created successfully: ID={response.QueueId}, Name={response.DisplayName}");
        }, "Create queue");

    private static async Task<TestResult> TestGetQueue(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var matchmakingClient = GetServiceClient<IMatchmakingClient>();

            // First create a test queue
            var queueId = $"get-test-queue-{DateTime.Now.Ticks}";
            var createRequest = new CreateQueueRequest
            {
                QueueId = queueId,
                GameId = "test-game",
                DisplayName = "Get Test Queue",
                MinCount = 2,
                MaxCount = 4
            };

            await matchmakingClient.CreateQueueAsync(createRequest);

            // Now test retrieving the queue
            var getRequest = new GetQueueRequest { QueueId = queueId };
            var response = await matchmakingClient.GetQueueAsync(getRequest);

            if (response.QueueId != queueId)
                return TestResult.Failed($"Queue ID mismatch: expected '{queueId}', got '{response.QueueId}'");

            if (response.DisplayName != "Get Test Queue")
                return TestResult.Failed($"Display name mismatch: expected 'Get Test Queue', got '{response.DisplayName}'");

            // Cleanup
            await matchmakingClient.DeleteQueueAsync(new DeleteQueueRequest { QueueId = queueId });

            return TestResult.Successful($"Queue retrieved successfully: ID={response.QueueId}, Name={response.DisplayName}");
        }, "Get queue");

    private static async Task<TestResult> TestUpdateQueue(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var matchmakingClient = GetServiceClient<IMatchmakingClient>();

            // First create a test queue
            var queueId = $"update-test-queue-{DateTime.Now.Ticks}";
            var createRequest = new CreateQueueRequest
            {
                QueueId = queueId,
                GameId = "test-game",
                DisplayName = "Original Name",
                MinCount = 2,
                MaxCount = 4
            };

            await matchmakingClient.CreateQueueAsync(createRequest);

            // Update the queue
            var updateRequest = new UpdateQueueRequest
            {
                QueueId = queueId,
                DisplayName = "Updated Name",
                Description = "Updated description",
                MaxCount = 8
            };

            var response = await matchmakingClient.UpdateQueueAsync(updateRequest);

            if (response.DisplayName != "Updated Name")
                return TestResult.Failed($"Display name was not updated: got '{response.DisplayName}'");

            if (response.MaxCount != 8)
                return TestResult.Failed($"MaxCount was not updated: expected 8, got {response.MaxCount}");

            // Cleanup
            await matchmakingClient.DeleteQueueAsync(new DeleteQueueRequest { QueueId = queueId });

            return TestResult.Successful($"Queue updated successfully: ID={response.QueueId}, NewName={response.DisplayName}");
        }, "Update queue");

    private static async Task<TestResult> TestDeleteQueue(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var matchmakingClient = GetServiceClient<IMatchmakingClient>();

            // First create a test queue
            var queueId = $"delete-test-queue-{DateTime.Now.Ticks}";
            var createRequest = new CreateQueueRequest
            {
                QueueId = queueId,
                GameId = "test-game",
                DisplayName = "To Be Deleted",
                MinCount = 2,
                MaxCount = 4
            };

            await matchmakingClient.CreateQueueAsync(createRequest);

            // Delete the queue
            await matchmakingClient.DeleteQueueAsync(new DeleteQueueRequest { QueueId = queueId });

            // Verify it's deleted by trying to get it (should return 404)
            try
            {
                await matchmakingClient.GetQueueAsync(new GetQueueRequest { QueueId = queueId });
                return TestResult.Failed("Queue was not deleted - GetQueue succeeded after delete");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected - queue should not exist
            }

            return TestResult.Successful($"Queue deleted successfully: ID={queueId}");
        }, "Delete queue");

    private static async Task<TestResult> TestJoinMatchmaking(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var matchmakingClient = GetServiceClient<IMatchmakingClient>();

            // First create a test queue
            var queueId = $"join-test-queue-{DateTime.Now.Ticks}";
            var createRequest = new CreateQueueRequest
            {
                QueueId = queueId,
                GameId = "test-game",
                DisplayName = "Join Test Queue",
                MinCount = 2,
                MaxCount = 4
            };

            await matchmakingClient.CreateQueueAsync(createRequest);

            // Join the matchmaking queue
            var accountId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var joinRequest = new JoinMatchmakingRequest
            {
                QueueId = queueId,
                AccountId = accountId,
                WebSocketSessionId = sessionId
            };

            var response = await matchmakingClient.JoinMatchmakingAsync(joinRequest);

            if (response.TicketId == Guid.Empty)
                return TestResult.Failed("JoinMatchmaking returned empty ticket ID");

            if (response.QueueId != queueId)
                return TestResult.Failed($"Queue ID mismatch: expected '{queueId}', got '{response.QueueId}'");

            // Cleanup: leave matchmaking and delete queue
            await matchmakingClient.LeaveMatchmakingAsync(new LeaveMatchmakingRequest
            {
                TicketId = response.TicketId,
                AccountId = accountId
            });
            await matchmakingClient.DeleteQueueAsync(new DeleteQueueRequest { QueueId = queueId });

            return TestResult.Successful($"Joined matchmaking successfully: TicketID={response.TicketId}, QueueID={response.QueueId}");
        }, "Join matchmaking");

    private static async Task<TestResult> TestLeaveMatchmaking(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var matchmakingClient = GetServiceClient<IMatchmakingClient>();

            // First create a test queue
            var queueId = $"leave-test-queue-{DateTime.Now.Ticks}";
            var createRequest = new CreateQueueRequest
            {
                QueueId = queueId,
                GameId = "test-game",
                DisplayName = "Leave Test Queue",
                MinCount = 2,
                MaxCount = 4
            };

            await matchmakingClient.CreateQueueAsync(createRequest);

            // Join the matchmaking queue
            var accountId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var joinRequest = new JoinMatchmakingRequest
            {
                QueueId = queueId,
                AccountId = accountId,
                WebSocketSessionId = sessionId
            };

            var joinResponse = await matchmakingClient.JoinMatchmakingAsync(joinRequest);

            // Leave matchmaking
            var leaveRequest = new LeaveMatchmakingRequest
            {
                TicketId = joinResponse.TicketId,
                AccountId = accountId
            };

            await matchmakingClient.LeaveMatchmakingAsync(leaveRequest);

            // Cleanup
            await matchmakingClient.DeleteQueueAsync(new DeleteQueueRequest { QueueId = queueId });

            return TestResult.Successful($"Left matchmaking successfully: TicketID={joinResponse.TicketId}");
        }, "Leave matchmaking");

    private static async Task<TestResult> TestGetMatchmakingStatus(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var matchmakingClient = GetServiceClient<IMatchmakingClient>();

            // First create a test queue
            var queueId = $"status-test-queue-{DateTime.Now.Ticks}";
            var createRequest = new CreateQueueRequest
            {
                QueueId = queueId,
                GameId = "test-game",
                DisplayName = "Status Test Queue",
                MinCount = 2,
                MaxCount = 4
            };

            await matchmakingClient.CreateQueueAsync(createRequest);

            // Join the matchmaking queue
            var accountId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var joinRequest = new JoinMatchmakingRequest
            {
                QueueId = queueId,
                AccountId = accountId,
                WebSocketSessionId = sessionId
            };

            var joinResponse = await matchmakingClient.JoinMatchmakingAsync(joinRequest);

            // Get matchmaking status
            var statusRequest = new GetMatchmakingStatusRequest
            {
                TicketId = joinResponse.TicketId,
                AccountId = accountId
            };

            var response = await matchmakingClient.GetMatchmakingStatusAsync(statusRequest);

            if (response.TicketId != joinResponse.TicketId)
                return TestResult.Failed($"Ticket ID mismatch: expected '{joinResponse.TicketId}', got '{response.TicketId}'");

            if (response.QueueId != queueId)
                return TestResult.Failed($"Queue ID mismatch: expected '{queueId}', got '{response.QueueId}'");

            // Cleanup
            await matchmakingClient.LeaveMatchmakingAsync(new LeaveMatchmakingRequest
            {
                TicketId = joinResponse.TicketId,
                AccountId = accountId
            });
            await matchmakingClient.DeleteQueueAsync(new DeleteQueueRequest { QueueId = queueId });

            return TestResult.Successful($"Got matchmaking status: TicketID={response.TicketId}, Status={response.Status}");
        }, "Get matchmaking status");

    private static async Task<TestResult> TestGetMatchmakingStats(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var matchmakingClient = GetServiceClient<IMatchmakingClient>();

            // Get stats for all queues in test-game
            var response = await matchmakingClient.GetMatchmakingStatsAsync(new GetMatchmakingStatsRequest
            {
                GameId = "test-game"
            });

            // Stats should return valid response even with no activity
            return TestResult.Successful($"Matchmaking stats retrieved: total queues recorded");
        }, "Get matchmaking stats");

    private static async Task<TestResult> TestGetNonExistentQueue(ITestClient client, string[] args) =>
        await ExecuteExpectingStatusAsync(
            async () =>
            {
                var matchmakingClient = GetServiceClient<IMatchmakingClient>();
                await matchmakingClient.GetQueueAsync(new GetQueueRequest
                {
                    QueueId = "nonexistent-queue-12345"
                });
            },
            404,
            "Get non-existent queue");

    private static async Task<TestResult> TestAcceptNonExistentMatch(ITestClient client, string[] args) =>
        await ExecuteExpectingStatusAsync(
            async () =>
            {
                var matchmakingClient = GetServiceClient<IMatchmakingClient>();
                await matchmakingClient.AcceptMatchAsync(new AcceptMatchRequest
                {
                    WebSocketSessionId = Guid.NewGuid(),
                    AccountId = Guid.NewGuid(),
                    MatchId = Guid.NewGuid() // Non-existent match
                });
            },
            404,
            "Accept non-existent match");

    private static async Task<TestResult> TestDeclineNonExistentMatch(ITestClient client, string[] args) =>
        await ExecuteExpectingStatusAsync(
            async () =>
            {
                var matchmakingClient = GetServiceClient<IMatchmakingClient>();
                await matchmakingClient.DeclineMatchAsync(new DeclineMatchRequest
                {
                    WebSocketSessionId = Guid.NewGuid(),
                    AccountId = Guid.NewGuid(),
                    MatchId = Guid.NewGuid() // Non-existent match
                });
            },
            404,
            "Decline non-existent match");

    private static async Task<TestResult> TestCompleteMatchmakingLifecycle(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var matchmakingClient = GetServiceClient<IMatchmakingClient>();

            // Step 1: Create queue
            var queueId = $"lifecycle-test-queue-{DateTime.Now.Ticks}";
            var createRequest = new CreateQueueRequest
            {
                QueueId = queueId,
                GameId = "test-game",
                DisplayName = "Lifecycle Test Queue",
                MinCount = 2,
                MaxCount = 4,
                UseSkillRating = true,
                MatchAcceptTimeoutSeconds = 30
            };

            var createResponse = await matchmakingClient.CreateQueueAsync(createRequest);
            Console.WriteLine($"  Step 1: Created queue {createResponse.QueueId}");

            // Step 2: Join matchmaking
            var accountId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var joinRequest = new JoinMatchmakingRequest
            {
                QueueId = queueId,
                AccountId = accountId,
                WebSocketSessionId = sessionId
            };

            var joinResponse = await matchmakingClient.JoinMatchmakingAsync(joinRequest);
            Console.WriteLine($"  Step 2: Joined matchmaking with ticket {joinResponse.TicketId}");

            // Step 3: Check status
            var statusRequest = new GetMatchmakingStatusRequest
            {
                TicketId = joinResponse.TicketId,
                AccountId = accountId
            };

            var statusResponse = await matchmakingClient.GetMatchmakingStatusAsync(statusRequest);
            Console.WriteLine($"  Step 3: Status check - {statusResponse.Status}");

            // Step 4: Leave matchmaking
            await matchmakingClient.LeaveMatchmakingAsync(new LeaveMatchmakingRequest
            {
                TicketId = joinResponse.TicketId,
                AccountId = accountId
            });
            Console.WriteLine($"  Step 4: Left matchmaking");

            // Step 5: Delete queue
            await matchmakingClient.DeleteQueueAsync(new DeleteQueueRequest { QueueId = queueId });
            Console.WriteLine($"  Step 5: Deleted queue");

            // Step 6: Verify queue is deleted
            try
            {
                await matchmakingClient.GetQueueAsync(new GetQueueRequest { QueueId = queueId });
                return TestResult.Failed("Queue was not deleted - GetQueue succeeded after delete");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                Console.WriteLine($"  Step 6: Verified queue deleted (404)");
            }

            return TestResult.Successful($"Complete matchmaking lifecycle test passed for queue {queueId}");
        }, "Complete matchmaking lifecycle");
}
