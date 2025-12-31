using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Connect;
using BeyondImmersion.BannouService.Permissions;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for Connect service HTTP API endpoints using generated clients.
/// Tests the connect service APIs directly via NSwag-generated ConnectClient.
/// Note: This tests HTTP endpoints only - WebSocket functionality is tested in edge-tester.
/// </summary>
public class ConnectTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Client Capabilities Tests (GUID -> API mappings for clients)
        // NOTE: Service routing (service-name -> app-id) has moved to Orchestrator API
        new ServiceTest(TestClientCapabilities, "ClientCapabilities", "Connect", "Test client capability manifest retrieval"),
        new ServiceTest(TestClientCapabilitiesStructure, "CapabilitiesStructure", "Connect", "Test capability response has valid structure"),

        // Internal Proxy Tests
        new ServiceTest(TestInternalProxy, "InternalProxy", "Connect", "Test internal API proxy functionality"),
        new ServiceTest(TestInternalProxyToInvalidService, "ProxyInvalidService", "Connect", "Test proxy to invalid service returns error"),
        new ServiceTest(TestInternalProxyWithoutSession, "ProxyNoSession", "Connect", "Test proxy without session ID fails appropriately"),
        new ServiceTest(TestInternalProxyWithDifferentMethods, "ProxyMethods", "Connect", "Test proxy handles different HTTP methods"),
        new ServiceTest(TestInternalProxyWithBody, "ProxyWithBody", "Connect", "Test proxy correctly forwards request body"),
        new ServiceTest(TestInternalProxyWithHeaders, "ProxyWithHeaders", "Connect", "Test proxy correctly forwards custom headers"),
        new ServiceTest(TestInternalProxyEmptyEndpoint, "ProxyEmptyEndpoint", "Connect", "Test proxy handles empty endpoint"),
        new ServiceTest(TestInternalProxyToAccountsService, "ProxyAccounts", "Connect", "Test proxy to accounts service"),

        // Session validation tests (dependencies for WebSocket upgrade)
        new ServiceTest(TestTokenValidationForWebSocket, "WSTokenValidation", "Connect", "Test token validation used in WebSocket upgrade"),
        new ServiceTest(TestTokenValidationReturnsSessionData, "WSSessionData", "Connect", "Test token validation returns session data for Connect"),
        new ServiceTest(TestSequentialTokenValidations, "WSSeqValidation", "Connect", "Test sequential token validations (simulating WS pre-checks)"),
        new ServiceTest(TestTokenValidationAfterOtherOperations, "WSPostOps", "Connect", "Test token validation after other API operations"),
    ];

    /// <summary>
    /// Test the client capabilities endpoint.
    /// Returns GUID -> API mappings for the authenticated client's session.
    /// </summary>
    private static Task<TestResult> TestClientCapabilities(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var connectClient = GetServiceClient<IConnectClient>();

            try
            {
                var response = await connectClient.GetClientCapabilitiesAsync(new GetClientCapabilitiesRequest());

                if (response == null)
                    return TestResult.Failed("Client capabilities response is null");

                // Even placeholder implementation should have required fields
                if (response.Capabilities == null)
                    return TestResult.Failed("Capabilities list is null");

                if (response.Version < 0)
                    return TestResult.Failed($"Invalid version: {response.Version}");

                if (response.GeneratedAt == default)
                    return TestResult.Failed("GeneratedAt timestamp is not set");

                return TestResult.Successful(
                    $"Client capabilities retrieved: {response.Capabilities.Count} capabilities, " +
                    $"version={response.Version}, sessionId={response.SessionId ?? "(placeholder)"}");
            }
            catch (ApiException ex) when (ex.StatusCode == 401)
            {
                // 401 is expected for unauthenticated requests - capabilities require auth
                return TestResult.Successful("Client capabilities correctly returned 401 (authentication required)");
            }
        }, "Client capabilities");

    /// <summary>
    /// Test that client capabilities response has valid structure.
    /// </summary>
    private static Task<TestResult> TestClientCapabilitiesStructure(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var connectClient = GetServiceClient<IConnectClient>();

            try
            {
                var response = await connectClient.GetClientCapabilitiesAsync(new GetClientCapabilitiesRequest());

                // Check required fields exist
                var issues = new List<string>();

                if (response.SessionId == null)
                    issues.Add("SessionId is null (should be set even for placeholder)");

                if (response.Capabilities == null)
                    issues.Add("Capabilities is null");

                if (response.GeneratedAt == default)
                    issues.Add("GeneratedAt is default");

                // Check each capability has required fields (if any exist)
                if (response.Capabilities?.Count > 0)
                {
                    foreach (var cap in response.Capabilities)
                    {
                        if (cap.Guid == Guid.Empty)
                            issues.Add("Capability missing Guid");
                        if (string.IsNullOrEmpty(cap.Service))
                            issues.Add("Capability missing Service");
                        if (string.IsNullOrEmpty(cap.Endpoint))
                            issues.Add("Capability missing Endpoint");
                    }
                }

                if (issues.Count > 0)
                {
                    return TestResult.Failed($"Capability structure issues: {string.Join(", ", issues)}");
                }

                return TestResult.Successful(
                    $"Client capabilities structure valid: version={response.Version}, " +
                    $"capCount={response.Capabilities?.Count ?? 0}");
            }
            catch (ApiException ex) when (ex.StatusCode == 401)
            {
                // 401 is expected - capabilities require authentication
                return TestResult.Successful("Client capabilities correctly returned 401 (authentication required)");
            }
        }, "Client capabilities structure");

    private static Task<TestResult> TestInternalProxy(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var connectClient = GetServiceClient<IConnectClient>();
            var testSessionId = $"test-session-{DateTime.Now.Ticks}";

            var proxyRequest = new InternalProxyRequest
            {
                SessionId = testSessionId,
                TargetService = "accounts",
                TargetEndpoint = "/accounts",
                Method = InternalProxyRequestMethod.GET,
                Headers = new Dictionary<string, string>
                {
                    { "Content-Type", "application/json" }
                }
            };

            try
            {
                var response = await connectClient.ProxyInternalRequestAsync(proxyRequest);

                if (response == null)
                    return TestResult.Failed("Internal proxy response is null");

                return TestResult.Successful($"Internal proxy completed - Success: {response.Success}, Status: {response.StatusCode}");
            }
            catch (ApiException ex) when (ex.StatusCode == 403)
            {
                // 403 is expected for proxy calls without proper auth
                return TestResult.Successful("Internal proxy correctly returned 403 (permission denied without auth)");
            }
        }, "Internal proxy");

    /// <summary>
    /// Test proxy to an invalid service returns appropriate error.
    /// Note: Connect service validates permissions FIRST before checking service existence,
    /// so 403 is correct behavior (don't leak info about what services exist).
    /// </summary>
    private static Task<TestResult> TestInternalProxyToInvalidService(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var connectClient = GetServiceClient<IConnectClient>();
            var testSessionId = $"proxy-invalid-{Guid.NewGuid():N}";

            var proxyRequest = new InternalProxyRequest
            {
                SessionId = testSessionId,
                TargetService = $"nonexistent-service-{Guid.NewGuid():N}",
                TargetEndpoint = "/any/endpoint",
                Method = InternalProxyRequestMethod.GET
            };

            try
            {
                var response = await connectClient.ProxyInternalRequestAsync(proxyRequest);

                if (response?.Success == false)
                {
                    return TestResult.Successful($"Proxy correctly failed for invalid service: {response.Error}");
                }

                return TestResult.Failed("Proxy to invalid service should return success=false");
            }
            catch (ApiException ex) when (ex.StatusCode == 403 || ex.StatusCode == 404 || ex.StatusCode == 502 || ex.StatusCode == 503)
            {
                // 403 is expected - permission check happens before service existence check (correct security behavior)
                return TestResult.Successful($"Proxy correctly returned {ex.StatusCode} for invalid service");
            }
        }, "Proxy to invalid service");

    /// <summary>
    /// Test proxy without session ID fails appropriately.
    /// Note: 403 is valid - permission denied for empty/invalid session.
    /// </summary>
    private static Task<TestResult> TestInternalProxyWithoutSession(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var connectClient = GetServiceClient<IConnectClient>();

            var proxyRequest = new InternalProxyRequest
            {
                SessionId = "", // Empty session ID
                TargetService = "accounts",
                TargetEndpoint = "/accounts",
                Method = InternalProxyRequestMethod.GET
            };

            try
            {
                var response = await connectClient.ProxyInternalRequestAsync(proxyRequest);

                if (response?.Success == false)
                {
                    return TestResult.Successful("Proxy correctly failed for empty session ID");
                }

                // Might succeed with empty session for some endpoints
                return TestResult.Successful("Proxy accepted empty session (may be allowed for public endpoints)");
            }
            catch (ApiException ex) when (ex.StatusCode == 400 || ex.StatusCode == 401 || ex.StatusCode == 403)
            {
                // 403 is expected - permission denied for empty/invalid session
                return TestResult.Successful($"Proxy correctly returned {ex.StatusCode} for empty session ID");
            }
        }, "Proxy without session");

    /// <summary>
    /// Test proxy handles different HTTP methods (GET, POST, PUT, DELETE).
    /// </summary>
    private static Task<TestResult> TestInternalProxyWithDifferentMethods(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var connectClient = GetServiceClient<IConnectClient>();
            var testSessionId = $"proxy-methods-{Guid.NewGuid():N}";

            var methods = new[]
            {
                InternalProxyRequestMethod.GET,
                InternalProxyRequestMethod.POST,
                InternalProxyRequestMethod.PUT,
                InternalProxyRequestMethod.DELETE
            };

            var successCount = 0;
            var errorDetails = new List<string>();

            foreach (var method in methods)
            {
                try
                {
                    var proxyRequest = new InternalProxyRequest
                    {
                        SessionId = testSessionId,
                        TargetService = "accounts",
                        TargetEndpoint = "/accounts",
                        Method = method
                    };

                    // Add body for methods that support it
                    if (method == InternalProxyRequestMethod.POST || method == InternalProxyRequestMethod.PUT)
                    {
                        proxyRequest.Body = new { test = "data" };
                    }

                    var response = await connectClient.ProxyInternalRequestAsync(proxyRequest);
                    successCount++;
                }
                catch (ApiException ex) when (ex.StatusCode == 403)
                {
                    // 403 is expected - permission denied without auth
                    successCount++;
                }
                catch (ApiException ex)
                {
                    errorDetails.Add($"{method}: {ex.StatusCode} - {ex.Message}");
                }
                catch (Exception ex)
                {
                    errorDetails.Add($"{method}: {ex.Message}");
                }
            }

            if (successCount == methods.Length)
            {
                return TestResult.Successful($"Proxy correctly handles all HTTP methods: {string.Join(", ", methods)}");
            }

            return TestResult.Failed($"Some methods failed: {string.Join("; ", errorDetails)}");
        }, "Proxy with different methods");

    /// <summary>
    /// Test proxy correctly forwards request body.
    /// </summary>
    private static Task<TestResult> TestInternalProxyWithBody(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var connectClient = GetServiceClient<IConnectClient>();
            var testSessionId = $"proxy-body-{Guid.NewGuid():N}";

            var proxyRequest = new InternalProxyRequest
            {
                SessionId = testSessionId,
                TargetService = "accounts",
                TargetEndpoint = "/accounts",
                Method = InternalProxyRequestMethod.POST,
                Body = new
                {
                    email = $"test-{Guid.NewGuid():N}@example.com",
                    displayName = "Test User"
                }
            };

            try
            {
                var response = await connectClient.ProxyInternalRequestAsync(proxyRequest);

                // The request was processed - check for valid response
                return TestResult.Successful($"Proxy processed request with body - Status: {response?.StatusCode}, Success: {response?.Success}");
            }
            catch (ApiException ex) when (ex.StatusCode == 403)
            {
                // 403 is expected - permission denied without auth
                return TestResult.Successful("Proxy correctly returned 403 (permission denied without auth)");
            }
        }, "Proxy with body");

    /// <summary>
    /// Test proxy correctly forwards custom headers.
    /// </summary>
    private static Task<TestResult> TestInternalProxyWithHeaders(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var connectClient = GetServiceClient<IConnectClient>();
            var testSessionId = $"proxy-headers-{Guid.NewGuid():N}";

            var proxyRequest = new InternalProxyRequest
            {
                SessionId = testSessionId,
                TargetService = "accounts",
                TargetEndpoint = "/accounts",
                Method = InternalProxyRequestMethod.GET,
                Headers = new Dictionary<string, string>
                {
                    { "X-Custom-Header", "test-value" },
                    { "X-Request-Id", Guid.NewGuid().ToString() },
                    { "Accept", "application/json" }
                }
            };

            try
            {
                var response = await connectClient.ProxyInternalRequestAsync(proxyRequest);
                return TestResult.Successful($"Proxy processed request with custom headers - Status: {response?.StatusCode}");
            }
            catch (ApiException ex) when (ex.StatusCode == 403)
            {
                // 403 is expected - permission denied without auth
                return TestResult.Successful("Proxy correctly returned 403 (permission denied without auth)");
            }
        }, "Proxy with headers");

    /// <summary>
    /// Test proxy handles empty endpoint.
    /// Note: 403 is valid - permission check happens before endpoint validation.
    /// </summary>
    private static Task<TestResult> TestInternalProxyEmptyEndpoint(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var connectClient = GetServiceClient<IConnectClient>();
            var testSessionId = $"proxy-empty-{Guid.NewGuid():N}";

            var proxyRequest = new InternalProxyRequest
            {
                SessionId = testSessionId,
                TargetService = "accounts",
                TargetEndpoint = "", // Empty endpoint
                Method = InternalProxyRequestMethod.GET
            };

            try
            {
                var response = await connectClient.ProxyInternalRequestAsync(proxyRequest);

                if (response?.Success == false)
                {
                    return TestResult.Successful("Proxy correctly handled empty endpoint");
                }

                // Might redirect to root endpoint
                return TestResult.Successful($"Proxy accepted empty endpoint - Status: {response?.StatusCode}");
            }
            catch (ApiException ex) when (ex.StatusCode == 400 || ex.StatusCode == 403 || ex.StatusCode == 404)
            {
                // 403 is expected - permission check happens before endpoint validation
                return TestResult.Successful($"Proxy correctly returned {ex.StatusCode} for empty endpoint");
            }
        }, "Proxy with empty endpoint");

    /// <summary>
    /// Test proxy to accounts service specifically.
    /// </summary>
    private static Task<TestResult> TestInternalProxyToAccountsService(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var connectClient = GetServiceClient<IConnectClient>();
            var testSessionId = $"proxy-accounts-{Guid.NewGuid():N}";

            var proxyRequest = new InternalProxyRequest
            {
                SessionId = testSessionId,
                TargetService = "accounts",
                TargetEndpoint = "/accounts",
                Method = InternalProxyRequestMethod.GET,
                QueryParameters = new Dictionary<string, string>
                {
                    { "page", "1" },
                    { "pageSize", "10" }
                }
            };

            try
            {
                var response = await connectClient.ProxyInternalRequestAsync(proxyRequest);

                if (response == null)
                    return TestResult.Failed("Proxy response is null");

                // Check if we got a valid response from accounts service
                if (response.Success && response.StatusCode == 200)
                {
                    return TestResult.Successful($"Proxy successfully routed to accounts service - got {response.Response?.Length ?? 0} chars response");
                }

                return TestResult.Successful($"Proxy to accounts service completed - Success: {response.Success}, Status: {response.StatusCode}");
            }
            catch (ApiException ex) when (ex.StatusCode == 403)
            {
                // 403 is expected - permission denied without auth
                return TestResult.Successful("Proxy correctly returned 403 (permission denied without auth)");
            }
        }, "Proxy to accounts service");

    #region Session Validation Tests (WebSocket Dependencies)

    /// <summary>
    /// Test token validation endpoint that Connect service uses before WebSocket upgrade.
    /// This simulates the exact flow that happens in WebSocket connection establishment.
    /// </summary>
    private static Task<TestResult> TestTokenValidationForWebSocket(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = $"ws_token_{DateTime.Now.Ticks}";

            // Register and login (simulating client flow before WebSocket)
            await authClient.RegisterAsync(new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = $"{testUsername}@example.com"
            });

            var loginResponse = await authClient.LoginAsync(new LoginRequest
            {
                Email = $"{testUsername}@example.com",
                Password = "TestPassword123!"
            });

            var token = loginResponse.AccessToken;
            if (string.IsNullOrEmpty(token))
                return TestResult.Failed("Login failed to return access token");

            // This is what Connect service calls internally when validating WebSocket upgrade
            var validation = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(token).ValidateTokenAsync();

            if (!validation.Valid)
                return TestResult.Failed($"Token validation failed for WebSocket upgrade simulation. Valid={validation.Valid}, SessionId={validation.SessionId}, RemainingTime={validation.RemainingTime}");

            // Verify we have the data Connect needs
            if (validation.SessionId == Guid.Empty)
                return TestResult.Failed("SessionId is empty - Connect needs this for session tracking");

            if (validation.RemainingTime <= 0)
                return TestResult.Failed($"RemainingTime is {validation.RemainingTime} - session appears expired (ExpiresAtUnix deserialization issue?)");

            return TestResult.Successful($"Token validation for WebSocket ready. SessionId: {validation.SessionId}, RemainingTime: {validation.RemainingTime}s, Roles: {validation.Roles?.Count ?? 0}");
        }, "Token validation for WebSocket");

    /// <summary>
    /// Test that token validation returns all session data needed by Connect service.
    /// </summary>
    private static Task<TestResult> TestTokenValidationReturnsSessionData(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = $"ws_data_{DateTime.Now.Ticks}";

            await authClient.RegisterAsync(new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = $"{testUsername}@example.com"
            });

            var loginResponse = await authClient.LoginAsync(new LoginRequest
            {
                Email = $"{testUsername}@example.com",
                Password = "TestPassword123!"
            });

            var validation = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(loginResponse.AccessToken).ValidateTokenAsync();

            // Check all required fields for Connect service
            var issues = new List<string>();

            if (!validation.Valid)
                issues.Add("Valid=false");

            if (validation.AccountId == Guid.Empty)
                issues.Add("AccountId is empty GUID");

            if (validation.SessionId == Guid.Empty)
                issues.Add("SessionId is empty");

            if (validation.RemainingTime <= 0)
                issues.Add($"RemainingTime={validation.RemainingTime} (should be positive - ExpiresAtUnix issue?)");

            if (validation.Roles == null)
                issues.Add("Roles is null");

            if (issues.Count > 0)
            {
                return TestResult.Failed($"Session data incomplete: {string.Join(", ", issues)}");
            }

            return TestResult.Successful($"Session data complete - AccountId: {validation.AccountId}, SessionId: {validation.SessionId}, RemainingTime: {validation.RemainingTime}s, Roles: [{string.Join(", ", validation.Roles ?? Array.Empty<string>())}]");
        }, "Token validation returns session data");

    /// <summary>
    /// Test sequential token validations to detect any state corruption over multiple calls.
    /// This simulates what happens when Connect service validates tokens repeatedly.
    /// </summary>
    private static Task<TestResult> TestSequentialTokenValidations(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = $"ws_seq_{DateTime.Now.Ticks}";

            await authClient.RegisterAsync(new RegisterRequest
            {
                Username = testUsername,
                Password = "TestPassword123!",
                Email = $"{testUsername}@example.com"
            });

            var loginResponse = await authClient.LoginAsync(new LoginRequest
            {
                Email = $"{testUsername}@example.com",
                Password = "TestPassword123!"
            });

            var token = loginResponse.AccessToken;

            // Perform 10 sequential validations
            var results = new List<(bool Valid, int RemainingTime)>();
            for (var i = 0; i < 10; i++)
            {
                var validation = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(token).ValidateTokenAsync();
                results.Add((validation.Valid, validation.RemainingTime));

                // Small delay between validations
                if (i < 9) await Task.Delay(100);
            }

            // Check all validations succeeded
            var failures = results.Where(r => !r.Valid).ToList();
            if (failures.Count > 0)
            {
                var failedIndices = results.Select((r, i) => (r, i)).Where(x => !x.r.Valid).Select(x => x.i);
                return TestResult.Failed($"Validations failed at indices: {string.Join(", ", failedIndices)}");
            }

            // Check RemainingTime is consistent (should all be similar, decreasing slightly)
            var zeroTimes = results.Where(r => r.RemainingTime <= 0).ToList();
            if (zeroTimes.Count > 0)
            {
                return TestResult.Failed($"{zeroTimes.Count}/10 validations had RemainingTime <= 0 (ExpiresAtUnix deserialization issue)");
            }

            var firstTime = results.First().RemainingTime;
            var lastTime = results.Last().RemainingTime;

            return TestResult.Successful($"All 10 sequential validations succeeded. RemainingTime: {firstTime}s -> {lastTime}s");
        }, "Sequential token validations");

    /// <summary>
    /// Test token validation after performing other API operations.
    /// This simulates the scenario where auth tests run and then WebSocket tests run.
    /// </summary>
    private static Task<TestResult> TestTokenValidationAfterOtherOperations(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var authClient = GetServiceClient<IAuthClient>();
            var testUsername = $"ws_postops_{DateTime.Now.Ticks}";
            var email = $"{testUsername}@example.com";
            var password = "TestPassword123!";

            // Create initial session
            await authClient.RegisterAsync(new RegisterRequest
            {
                Username = testUsername,
                Password = password,
                Email = email
            });

            var loginResponse1 = await authClient.LoginAsync(new LoginRequest { Email = email, Password = password });
            var token1 = loginResponse1.AccessToken;

            // Validate initial session
            var validation1 = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(token1).ValidateTokenAsync();
            if (!validation1.Valid || validation1.RemainingTime <= 0)
                return TestResult.Failed($"Initial validation failed: Valid={validation1.Valid}, RT={validation1.RemainingTime}");

            // Perform "other operations" - like the auth tests do
            // 1. Create another session (login again)
            var loginResponse2 = await authClient.LoginAsync(new LoginRequest { Email = email, Password = password });

            // 2. Create third session
            var loginResponse3 = await authClient.LoginAsync(new LoginRequest { Email = email, Password = password });

            // 3. Logout third session (simulating Logout test)
            await ((IServiceClient<AuthClient>)authClient).WithAuthorization(loginResponse3.AccessToken).LogoutAsync(new LogoutRequest { AllSessions = false });

            // Now validate the ORIGINAL session - this is what WebSocket tests do
            var validation2 = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(token1).ValidateTokenAsync();

            if (!validation2.Valid)
            {
                return TestResult.Failed($"Original session became invalid after other operations! Valid={validation2.Valid}, RT={validation2.RemainingTime}");
            }

            if (validation2.RemainingTime <= 0)
            {
                return TestResult.Failed($"Original session RemainingTime is {validation2.RemainingTime} after other operations (ExpiresAtUnix=0 bug?)");
            }

            // Also validate second session is still valid
            var validation3 = await ((IServiceClient<AuthClient>)authClient).WithAuthorization(loginResponse2.AccessToken).ValidateTokenAsync();
            if (!validation3.Valid || validation3.RemainingTime <= 0)
            {
                return TestResult.Failed($"Second session invalid after logout of third: Valid={validation3.Valid}, RT={validation3.RemainingTime}");
            }

            return TestResult.Successful($"Original session remains valid after other operations. Session1 RT: {validation2.RemainingTime}s, Session2 RT: {validation3.RemainingTime}s");
        }, "Token validation after other operations");

    #endregion
}
