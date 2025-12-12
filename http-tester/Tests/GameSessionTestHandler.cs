using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for game session API endpoints using generated clients.
/// Tests the game session service APIs directly via NSwag-generated GameSessionClient.
///
/// Note: GameSession APIs assume player identity comes from JWT authentication context.
/// These tests use the service-to-service (internal) path which bypasses JWT auth for testing.
/// </summary>
public class GameSessionTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            // Core CRUD operations
            new ServiceTest(TestCreateGameSession, "CreateGameSession", "GameSession", "Test game session creation endpoint"),
            new ServiceTest(TestGetGameSession, "GetGameSession", "GameSession", "Test game session retrieval endpoint"),
            new ServiceTest(TestListGameSessions, "ListGameSessions", "GameSession", "Test game session listing endpoint"),

            // Session lifecycle operations
            new ServiceTest(TestJoinGameSession, "JoinGameSession", "GameSession", "Test joining a game session"),
            new ServiceTest(TestLeaveGameSession, "LeaveGameSession", "GameSession", "Test leaving a game session"),
            new ServiceTest(TestKickPlayer, "KickPlayer", "GameSession", "Test kicking a player from session"),

            // Game actions and chat
            new ServiceTest(TestSendChatMessage, "SendChatMessage", "GameSession", "Test sending chat message to session"),
            new ServiceTest(TestPerformGameAction, "PerformGameAction", "GameSession", "Test performing game action in session"),

            // Error handling tests
            new ServiceTest(TestGetNonExistentSession, "GetNonExistentSession", "GameSession", "Test 404 for non-existent session"),

            // Complete lifecycle test
            new ServiceTest(TestCompleteSessionLifecycle, "CompleteSessionLifecycle", "GameSession", "Test complete session lifecycle: create → join → action → leave"),
        };
    }

    private static async Task<TestResult> TestCreateGameSession(ITestClient client, string[] args)
    {
        try
        {
            var gameSessionClient = new GameSessionClient();

            var createRequest = new CreateGameSessionRequest
            {
                SessionName = $"TestSession_{DateTime.Now.Ticks}",
                GameType = CreateGameSessionRequestGameType.Arcadia,
                MaxPlayers = 4,
                IsPrivate = false
            };

            var response = await gameSessionClient.CreateGameSessionAsync(createRequest);

            if (string.IsNullOrEmpty(response.SessionId))
                return TestResult.Failed("Session creation returned empty session ID");

            if (response.SessionName != createRequest.SessionName)
                return TestResult.Failed($"Session name mismatch: expected '{createRequest.SessionName}', got '{response.SessionName}'");

            return TestResult.Successful($"Game session created successfully: ID={response.SessionId}, Name={response.SessionName}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Session creation failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetGameSession(ITestClient client, string[] args)
    {
        try
        {
            var gameSessionClient = new GameSessionClient();

            // First create a test session
            var createRequest = new CreateGameSessionRequest
            {
                SessionName = $"GetTest_{DateTime.Now.Ticks}",
                GameType = CreateGameSessionRequestGameType.Arcadia,
                MaxPlayers = 4,
                IsPrivate = false
            };

            var createResponse = await gameSessionClient.CreateGameSessionAsync(createRequest);
            if (string.IsNullOrEmpty(createResponse.SessionId))
                return TestResult.Failed("Failed to create test session for retrieval test");

            var sessionIdGuid = Guid.Parse(createResponse.SessionId);

            // Now test retrieving the session
            var getRequest = new GetGameSessionRequest { SessionId = sessionIdGuid };
            var response = await gameSessionClient.GetGameSessionAsync(getRequest);

            if (response.SessionId != createResponse.SessionId)
                return TestResult.Failed($"Session ID mismatch: expected '{createResponse.SessionId}', got '{response.SessionId}'");

            if (response.SessionName != createRequest.SessionName)
                return TestResult.Failed($"Session name mismatch: expected '{createRequest.SessionName}', got '{response.SessionName}'");

            return TestResult.Successful($"Game session retrieved successfully: ID={response.SessionId}, Name={response.SessionName}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Session retrieval failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestListGameSessions(ITestClient client, string[] args)
    {
        try
        {
            var gameSessionClient = new GameSessionClient();

            // Create a few test sessions first
            for (int i = 0; i < 3; i++)
            {
                var createRequest = new CreateGameSessionRequest
                {
                    SessionName = $"ListTest_{DateTime.Now.Ticks}_{i}",
                    GameType = CreateGameSessionRequestGameType.Arcadia,
                    MaxPlayers = 4,
                    IsPrivate = false
                };
                await gameSessionClient.CreateGameSessionAsync(createRequest);
            }

            // Now list sessions - use filter parameters
            var listRequest = new ListGameSessionsRequest
            {
                GameType = ListGameSessionsRequestGameType.Arcadia,
                Status = ListGameSessionsRequestStatus.Waiting
            };

            var response = await gameSessionClient.ListGameSessionsAsync(listRequest);

            if (response.Sessions == null)
                return TestResult.Failed("List response returned null sessions array");

            return TestResult.Successful($"Listed {response.Sessions.Count} game sessions (Total: {response.TotalCount})");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Session listing failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestJoinGameSession(ITestClient client, string[] args)
    {
        try
        {
            var gameSessionClient = new GameSessionClient();

            // First create a test session
            var createRequest = new CreateGameSessionRequest
            {
                SessionName = $"JoinTest_{DateTime.Now.Ticks}",
                GameType = CreateGameSessionRequestGameType.Arcadia,
                MaxPlayers = 4,
                IsPrivate = false
            };

            var createResponse = await gameSessionClient.CreateGameSessionAsync(createRequest);
            if (string.IsNullOrEmpty(createResponse.SessionId))
                return TestResult.Failed("Failed to create test session for join test");

            var sessionIdGuid = Guid.Parse(createResponse.SessionId);

            // Now test joining the session (player identity comes from JWT context)
            var joinRequest = new JoinGameSessionRequest
            {
                SessionId = sessionIdGuid
            };

            var response = await gameSessionClient.JoinGameSessionAsync(joinRequest);

            if (!response.Success)
                return TestResult.Failed("Join response indicated failure");

            return TestResult.Successful($"Successfully joined game session: SessionID={createResponse.SessionId}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Session join failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestLeaveGameSession(ITestClient client, string[] args)
    {
        try
        {
            var gameSessionClient = new GameSessionClient();

            // Create and join a session first
            var createRequest = new CreateGameSessionRequest
            {
                SessionName = $"LeaveTest_{DateTime.Now.Ticks}",
                GameType = CreateGameSessionRequestGameType.Arcadia,
                MaxPlayers = 4,
                IsPrivate = false
            };

            var createResponse = await gameSessionClient.CreateGameSessionAsync(createRequest);
            var sessionIdGuid = Guid.Parse(createResponse.SessionId);

            // Join the session first
            var joinRequest = new JoinGameSessionRequest { SessionId = sessionIdGuid };
            await gameSessionClient.JoinGameSessionAsync(joinRequest);

            // Now test leaving the session (player identity comes from JWT context)
            var leaveRequest = new LeaveGameSessionRequest { SessionId = sessionIdGuid };
            await gameSessionClient.LeaveGameSessionAsync(leaveRequest);

            return TestResult.Successful($"Successfully left game session: SessionID={createResponse.SessionId}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Session leave failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestKickPlayer(ITestClient client, string[] args)
    {
        try
        {
            var gameSessionClient = new GameSessionClient();

            // Create a session
            var createRequest = new CreateGameSessionRequest
            {
                SessionName = $"KickTest_{DateTime.Now.Ticks}",
                GameType = CreateGameSessionRequestGameType.Arcadia,
                MaxPlayers = 4,
                IsPrivate = false
            };

            var createResponse = await gameSessionClient.CreateGameSessionAsync(createRequest);
            var sessionIdGuid = Guid.Parse(createResponse.SessionId);

            // Join the session first - this adds a player that we can kick
            var joinRequest = new JoinGameSessionRequest { SessionId = sessionIdGuid };
            await gameSessionClient.JoinGameSessionAsync(joinRequest);

            // Get the session to find the player's account ID
            var getRequest = new GetGameSessionRequest { SessionId = sessionIdGuid };
            var session = await gameSessionClient.GetGameSessionAsync(getRequest);

            if (session.Players == null || session.Players.Count == 0)
                return TestResult.Failed("No players in session after join");

            var playerToKick = session.Players.First().AccountId;

            // Now test kicking the player
            var kickRequest = new KickPlayerRequest
            {
                SessionId = sessionIdGuid,
                TargetAccountId = playerToKick,
                Reason = "Test kick"
            };

            await gameSessionClient.KickPlayerAsync(kickRequest);

            return TestResult.Successful($"Successfully kicked player: SessionID={createResponse.SessionId}, KickedPlayerID={playerToKick}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Kick player failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestSendChatMessage(ITestClient client, string[] args)
    {
        try
        {
            var gameSessionClient = new GameSessionClient();

            // Create a session first
            var createRequest = new CreateGameSessionRequest
            {
                SessionName = $"ChatTest_{DateTime.Now.Ticks}",
                GameType = CreateGameSessionRequestGameType.Arcadia,
                MaxPlayers = 4,
                IsPrivate = false
            };

            var createResponse = await gameSessionClient.CreateGameSessionAsync(createRequest);
            var sessionIdGuid = Guid.Parse(createResponse.SessionId);

            // Send a chat message (sender identity comes from JWT context)
            var chatRequest = new ChatMessageRequest
            {
                SessionId = sessionIdGuid,
                Message = "Hello, World!",
                MessageType = ChatMessageRequestMessageType.Public
            };

            await gameSessionClient.SendChatMessageAsync(chatRequest);

            return TestResult.Successful($"Chat message sent successfully to session {createResponse.SessionId}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Send chat message failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestPerformGameAction(ITestClient client, string[] args)
    {
        try
        {
            var gameSessionClient = new GameSessionClient();

            // Create a session first
            var createRequest = new CreateGameSessionRequest
            {
                SessionName = $"ActionTest_{DateTime.Now.Ticks}",
                GameType = CreateGameSessionRequestGameType.Arcadia,
                MaxPlayers = 4,
                IsPrivate = false
            };

            var createResponse = await gameSessionClient.CreateGameSessionAsync(createRequest);
            var sessionIdGuid = Guid.Parse(createResponse.SessionId);

            // Join as a player first (to get enhanced permissions)
            var joinRequest = new JoinGameSessionRequest { SessionId = sessionIdGuid };
            await gameSessionClient.JoinGameSessionAsync(joinRequest);

            // Perform a game action (player identity comes from JWT context)
            var actionRequest = new GameActionRequest
            {
                SessionId = sessionIdGuid,
                ActionType = GameActionRequestActionType.Move,
                ActionData = new { x = 10, y = 20 }
            };

            var response = await gameSessionClient.PerformGameActionAsync(actionRequest);

            if (!response.Success)
                return TestResult.Failed($"Game action failed");

            return TestResult.Successful($"Game action performed successfully: ActionID={response.ActionId}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Perform game action failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetNonExistentSession(ITestClient client, string[] args)
    {
        try
        {
            var gameSessionClient = new GameSessionClient();

            var getRequest = new GetGameSessionRequest
            {
                SessionId = Guid.NewGuid() // Non-existent session ID
            };

            try
            {
                await gameSessionClient.GetGameSessionAsync(getRequest);
                return TestResult.Failed("Expected 404 for non-existent session, but request succeeded");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("Correctly returned 404 for non-existent session");
            }
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Unexpected error: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestCompleteSessionLifecycle(ITestClient client, string[] args)
    {
        try
        {
            var gameSessionClient = new GameSessionClient();

            // Step 1: Create session
            var createRequest = new CreateGameSessionRequest
            {
                SessionName = $"LifecycleTest_{DateTime.Now.Ticks}",
                GameType = CreateGameSessionRequestGameType.Arcadia,
                MaxPlayers = 4,
                IsPrivate = false
            };

            var createResponse = await gameSessionClient.CreateGameSessionAsync(createRequest);
            var sessionIdGuid = Guid.Parse(createResponse.SessionId);
            Console.WriteLine($"  Step 1: Created session {createResponse.SessionId}");

            // Step 2: Join as player
            await gameSessionClient.JoinGameSessionAsync(new JoinGameSessionRequest { SessionId = sessionIdGuid });
            Console.WriteLine($"  Step 2: Player joined");

            // Step 3: Perform game action
            var actionResponse = await gameSessionClient.PerformGameActionAsync(new GameActionRequest
            {
                SessionId = sessionIdGuid,
                ActionType = GameActionRequestActionType.Move,
                ActionData = new { testData = "lifecycle_test" }
            });
            Console.WriteLine($"  Step 3: Performed action {actionResponse.ActionId}");

            // Step 4: Send chat message
            await gameSessionClient.SendChatMessageAsync(new ChatMessageRequest
            {
                SessionId = sessionIdGuid,
                Message = "Lifecycle test message",
                MessageType = ChatMessageRequestMessageType.Public
            });
            Console.WriteLine($"  Step 4: Sent chat message");

            // Step 5: Leave session
            await gameSessionClient.LeaveGameSessionAsync(new LeaveGameSessionRequest { SessionId = sessionIdGuid });
            Console.WriteLine($"  Step 5: Player left session");

            // Step 6: Verify session still exists
            var getResponse = await gameSessionClient.GetGameSessionAsync(new GetGameSessionRequest { SessionId = sessionIdGuid });
            Console.WriteLine($"  Step 6: Session verified (Status: {getResponse.Status})");

            return TestResult.Successful($"Complete session lifecycle test passed for session {createResponse.SessionId}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Lifecycle test failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }
}
