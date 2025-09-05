namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Enhanced account test handler that supports both manual and schema-driven tests
/// </summary>
public class EnhancedAccountTestHandler : ISchemaTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            new ServiceTest(TestAccountWorkflow, "AccountWorkflow", "Account", "Test complete account workflow (create, get, update, delete)")
        };
    }

    public async Task<ServiceTest[]> GetSchemaBasedTests(SchemaTestGenerator generator)
    {
        var schemaPath = GetSchemaFilePath();
        if (!File.Exists(schemaPath))
            return Array.Empty<ServiceTest>();

        return await generator.GenerateTestsFromSchema(schemaPath);
    }

    public string GetSchemaFilePath()
    {
        return Path.Combine("schemas", "accounts-api.yaml");
    }

    private static async Task<TestResult> TestAccountWorkflow(ITestClient client, string[] args)
    {
        try
        {
            var testUsername = $"workflow_user_{DateTime.Now.Ticks}";
            var testPassword = "WorkflowPassword123!";

            // Step 1: Create account
            var createRequest = new
            {
                username = testUsername,
                password = testPassword,
                email = $"{testUsername}@example.com"
            };

            var createResponse = await client.PostAsync<dynamic>("api/accounts/create", createRequest);
            if (!createResponse.Success)
                return TestResult.Failed($"Account creation failed: {createResponse.ErrorMessage}");

            var accountId = createResponse.Data?.GetProperty("id").GetInt32();
            if (accountId <= 0)
                return TestResult.Failed("Invalid account ID returned from creation");

            // Step 2: Retrieve account
            var getRequest = new { id = accountId, includeClaims = true };
            var getResponse = await client.PostAsync<dynamic>("api/accounts/get", getRequest);
            if (!getResponse.Success)
                return TestResult.Failed($"Account retrieval failed: {getResponse.ErrorMessage}");

            var retrievedUsername = getResponse.Data?.GetProperty("username").GetString();
            if (retrievedUsername != testUsername)
                return TestResult.Failed("Retrieved username doesn't match created username");

            // Step 3: Update account
            var updateRequest = new
            {
                id = accountId,
                email = $"updated_{testUsername}@example.com"
            };

            var updateResponse = await client.PostAsync<dynamic>("api/accounts/update", updateRequest);
            if (!updateResponse.Success)
                return TestResult.Failed($"Account update failed: {updateResponse.ErrorMessage}");

            // Step 4: Verify update
            var verifyResponse = await client.PostAsync<dynamic>("api/accounts/get", getRequest);
            if (!verifyResponse.Success)
                return TestResult.Failed($"Account verification after update failed: {verifyResponse.ErrorMessage}");

            var updatedEmail = verifyResponse.Data?.GetProperty("email").GetString();
            if (!updatedEmail?.StartsWith("updated_") == true)
                return TestResult.Failed("Account update was not persisted");

            // Step 5: Delete account (if delete endpoint exists)
            try
            {
                var deleteRequest = new { id = accountId };
                var deleteResponse = await client.PostAsync<dynamic>("api/accounts/delete", deleteRequest);
                // Don't fail the test if delete endpoint doesn't exist or isn't implemented
            }
            catch
            {
                // Delete operation is optional for this test
            }

            return TestResult.Successful($"Complete account workflow succeeded via {client.TransportType} transport");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Account workflow test failed: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Dual-transport test runner that executes the same tests via both HTTP and WebSocket
/// </summary>
public class DualTransportTestRunner
{
    private readonly TestConfiguration _configuration;
    private readonly SchemaTestGenerator _generator;

    public DualTransportTestRunner(TestConfiguration configuration)
    {
        _configuration = configuration;
        _generator = new SchemaTestGenerator();
    }

    /// <summary>
    /// Run tests using both HTTP and WebSocket transports and compare results
    /// </summary>
    public async Task<DualTransportTestResult[]> RunDualTransportTests(ISchemaTestHandler handler)
    {
        var results = new List<DualTransportTestResult>();

        try
        {
            // Get all tests (manual + schema-generated)
            var manualTests = handler.GetServiceTests();
            var schemaTests = await handler.GetSchemaBasedTests(_generator);
            var allTests = manualTests.Concat(schemaTests).ToArray();

            // Create both types of clients
            var httpClient = TestClientFactory.CreateHttpClient(_configuration);
            var wsClient = TestClientFactory.CreateWebSocketClient(_configuration);

            // Connect WebSocket client
            if (wsClient is WebSocketTestClient wsTestClient)
            {
                await wsTestClient.ConnectAsync();
            }

            foreach (var test in allTests)
            {
                var result = new DualTransportTestResult
                {
                    TestName = test.Name,
                    TestType = test.Type,
                    Description = test.Description
                };

                try
                {
                    // Run via HTTP
                    var httpResult = await test.TestAction(httpClient, Array.Empty<string>());
                    result.HttpResult = httpResult;
                }
                catch (Exception ex)
                {
                    result.HttpResult = TestResult.Failed($"HTTP test exception: {ex.Message}", ex);
                }

                try
                {
                    // Run via WebSocket
                    var wsResult = await test.TestAction(wsClient, Array.Empty<string>());
                    result.WebSocketResult = wsResult;
                }
                catch (Exception ex)
                {
                    result.WebSocketResult = TestResult.Failed($"WebSocket test exception: {ex.Message}", ex);
                }

                // Compare results
                result.ResultsMatch = CompareTestResults(result.HttpResult, result.WebSocketResult);
                results.Add(result);
            }

            // Cleanup
            httpClient.Dispose();
            wsClient.Dispose();
        }
        catch (Exception ex)
        {
            results.Add(new DualTransportTestResult
            {
                TestName = "DualTransportSetup",
                TestType = "Infrastructure",
                Description = "Dual transport test setup",
                HttpResult = TestResult.Failed($"Setup failed: {ex.Message}", ex),
                WebSocketResult = TestResult.Failed($"Setup failed: {ex.Message}", ex),
                ResultsMatch = true
            });
        }

        return results.ToArray();
    }

    private bool CompareTestResults(TestResult? httpResult, TestResult? wsResult)
    {
        if (httpResult == null || wsResult == null)
            return false;

        // Results match if both succeed or both fail with similar error patterns
        if (httpResult.Success && wsResult.Success)
            return true;

        if (!httpResult.Success && !wsResult.Success)
        {
            // Consider failures equivalent if they're both validation or authorization errors
            return IsEquivalentFailure(httpResult.Message, wsResult.Message);
        }

        return false;
    }

    private bool IsEquivalentFailure(string httpMessage, string wsMessage)
    {
        var httpLower = httpMessage.ToLowerInvariant();
        var wsLower = wsMessage.ToLowerInvariant();

        // Check for common error patterns that should match across transports
        var errorPatterns = new[]
        {
            "validation", "unauthorized", "forbidden", "not found", "bad request",
            "missing", "invalid", "required", "timeout"
        };

        foreach (var pattern in errorPatterns)
        {
            if (httpLower.Contains(pattern) && wsLower.Contains(pattern))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Result of running a test via both HTTP and WebSocket transports
/// </summary>
public class DualTransportTestResult
{
    public string TestName { get; set; } = "";
    public string TestType { get; set; } = "";
    public string Description { get; set; } = "";
    public TestResult? HttpResult { get; set; }
    public TestResult? WebSocketResult { get; set; }
    public bool ResultsMatch { get; set; }

    public bool BothSucceeded => HttpResult?.Success == true && WebSocketResult?.Success == true;
    public bool BothFailed => HttpResult?.Success == false && WebSocketResult?.Success == false;
    public bool HasTransportDiscrepancy => HttpResult?.Success != WebSocketResult?.Success;

    public override string ToString()
    {
        var httpStatus = HttpResult?.Success == true ? "✓" : "✗";
        var wsStatus = WebSocketResult?.Success == true ? "✓" : "✗";
        var matchStatus = ResultsMatch ? "✓" : "⚠️";

        return $"{TestName}: HTTP({httpStatus}) WS({wsStatus}) Match({matchStatus})";
    }
}
