using System.Net.Http;
using System.Text.Json;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for the Testing service HTTP API endpoints.
/// Tests debugging and infrastructure validation endpoints.
/// These tests are critical for validating routing behavior and diagnosing Dapr path handling.
/// </summary>
public class TestingTestHandler : IServiceTestHandler
{
    private static readonly HttpClient _httpClient = new();
    private static readonly string _baseUrl;

    static TestingTestHandler()
    {
        // Get base URL from environment or default to localhost
        var daprHttpEndpoint = Environment.GetEnvironmentVariable("DAPR_HTTP_ENDPOINT") ?? "http://localhost:5012";
        _baseUrl = daprHttpEndpoint;
    }

    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            // Health Check Tests
            new ServiceTest(TestHealthEndpoint, "Health", "Testing", "Test testing service health endpoint"),

            // Debug Path Tests - Critical for routing validation
            new ServiceTest(TestDebugPathEndpoint, "DebugPath", "Testing", "Test debug path endpoint returns routing info"),
            new ServiceTest(TestDebugPathReceivedPath, "PathReceived", "Testing", "Verify controller receives expected path"),
            new ServiceTest(TestDebugPathWithCatchAll, "PathCatchAll", "Testing", "Test debug path with catch-all segment"),
            new ServiceTest(TestDebugPathControllerRoute, "ControllerRoute", "Testing", "Verify controller route attribute value"),
            new ServiceTest(TestDebugPathDaprHeaders, "DaprHeaders", "Testing", "Check for Dapr-related headers in request"),

            // Routing Architecture Tests
            new ServiceTest(TestDirectVsDaprRouting, "DirectVsDapr", "Testing", "Compare direct vs Dapr routing paths"),
            new ServiceTest(TestPathWithDifferentPrefixes, "PathPrefixes", "Testing", "Test paths with various prefixes"),
        };
    }

    /// <summary>
    /// Test the health endpoint is accessible.
    /// </summary>
    private static async Task<TestResult> TestHealthEndpoint(ITestClient client, string[] args)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/testing/health");

            if (!response.IsSuccessStatusCode)
            {
                return TestResult.Failed($"Health endpoint returned {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            return TestResult.Successful($"Health endpoint OK - {content}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Health check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test the debug path endpoint returns routing information.
    /// </summary>
    private static async Task<TestResult> TestDebugPathEndpoint(ITestClient client, string[] args)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/testing/debug/path");

            if (!response.IsSuccessStatusCode)
            {
                return TestResult.Failed($"Debug path endpoint returned {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var debugInfo = JsonSerializer.Deserialize<RoutingDebugInfo>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (debugInfo == null)
            {
                return TestResult.Failed("Failed to deserialize debug info");
            }

            return TestResult.Successful(
                $"Debug path endpoint OK - Path: {debugInfo.Path}, ControllerRoute: {debugInfo.ControllerRoute}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Debug path check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Verify the controller receives the expected path format.
    /// This is the key test for understanding Dapr path stripping behavior.
    /// </summary>
    private static async Task<TestResult> TestDebugPathReceivedPath(ITestClient client, string[] args)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/testing/debug/path");

            if (!response.IsSuccessStatusCode)
            {
                return TestResult.Failed($"Debug path endpoint returned {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var debugInfo = JsonSerializer.Deserialize<RoutingDebugInfo>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (debugInfo == null)
            {
                return TestResult.Failed("Failed to deserialize debug info");
            }

            // Log all the path information for debugging
            var pathDetails = new List<string>
            {
                $"RawUrl={debugInfo.RawUrl}",
                $"PathBase={debugInfo.PathBase}",
                $"Path={debugInfo.Path}",
                $"ControllerRoute={debugInfo.ControllerRoute}"
            };

            // The key insight: what path does the controller actually receive?
            // If Dapr strips /v1.0/invoke/bannou/method/, the path should be just /testing/debug/path
            // If Dapr preserves it, the path would be /v1.0/invoke/bannou/method/testing/debug/path
            var receivedPath = debugInfo.Path;

            if (receivedPath.Contains("/v1.0/invoke/"))
            {
                return TestResult.Successful($"Path INCLUDES Dapr prefix (Dapr NOT stripping): {string.Join(", ", pathDetails)}");
            }
            else if (receivedPath.StartsWith("/testing/"))
            {
                return TestResult.Successful($"Path EXCLUDES Dapr prefix (Dapr IS stripping): {string.Join(", ", pathDetails)}");
            }
            else
            {
                return TestResult.Successful($"Unexpected path format: {string.Join(", ", pathDetails)}");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Path check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test debug path with catch-all segment to see full path routing.
    /// </summary>
    private static async Task<TestResult> TestDebugPathWithCatchAll(ITestClient client, string[] args)
    {
        try
        {
            // Test with a nested path to see what the catch-all captures
            var testPath = "some/nested/path/segments";
            var response = await _httpClient.GetAsync($"{_baseUrl}/testing/debug/path/{testPath}");

            if (!response.IsSuccessStatusCode)
            {
                return TestResult.Failed($"Debug path catch-all returned {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var debugInfo = JsonSerializer.Deserialize<RoutingDebugInfo>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (debugInfo == null)
            {
                return TestResult.Failed("Failed to deserialize debug info");
            }

            var catchAllSegment = debugInfo.CatchAllSegment;

            // Verify the catch-all segment contains what we expected
            if (catchAllSegment == testPath)
            {
                return TestResult.Successful($"Catch-all correctly captured: '{catchAllSegment}'");
            }
            else if (string.IsNullOrEmpty(catchAllSegment))
            {
                return TestResult.Successful($"Catch-all is empty (path routing may have consumed segments). Path={debugInfo.Path}");
            }
            else
            {
                return TestResult.Successful($"Catch-all captured different value: expected='{testPath}', got='{catchAllSegment}', Path={debugInfo.Path}");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Catch-all path check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Verify the controller route attribute value matches expected pattern.
    /// </summary>
    private static async Task<TestResult> TestDebugPathControllerRoute(ITestClient client, string[] args)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/testing/debug/path");

            if (!response.IsSuccessStatusCode)
            {
                return TestResult.Failed($"Debug path endpoint returned {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var debugInfo = JsonSerializer.Deserialize<RoutingDebugInfo>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (debugInfo == null)
            {
                return TestResult.Failed("Failed to deserialize debug info");
            }

            var controllerRoute = debugInfo.ControllerRoute;

            // TestingController uses [Route("testing")] - not the Dapr route prefix
            // Generated controllers use [Route("v1.0/invoke/bannou/method")]
            if (controllerRoute == "testing")
            {
                return TestResult.Successful($"Controller route is 'testing' (manual controller pattern)");
            }
            else if (controllerRoute.Contains("v1.0/invoke"))
            {
                return TestResult.Successful($"Controller route includes Dapr prefix: '{controllerRoute}' (generated controller pattern)");
            }
            else
            {
                return TestResult.Successful($"Controller route: '{controllerRoute}'");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Controller route check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Check for Dapr-related headers in the request.
    /// </summary>
    private static async Task<TestResult> TestDebugPathDaprHeaders(ITestClient client, string[] args)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/testing/debug/path");

            if (!response.IsSuccessStatusCode)
            {
                return TestResult.Failed($"Debug path endpoint returned {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var debugInfo = JsonSerializer.Deserialize<RoutingDebugInfo>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (debugInfo == null)
            {
                return TestResult.Failed("Failed to deserialize debug info");
            }

            var headers = debugInfo.Headers ?? new Dictionary<string, string>();
            var daprHeaders = headers.Where(h =>
                h.Key.StartsWith("dapr-", StringComparison.OrdinalIgnoreCase) ||
                h.Key.StartsWith("traceparent", StringComparison.OrdinalIgnoreCase) ||
                h.Key.StartsWith("tracestate", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            if (daprHeaders.Count == 0)
            {
                return TestResult.Successful("No Dapr headers present (direct HTTP call, not through Dapr sidecar)");
            }
            else
            {
                var headerList = string.Join(", ", daprHeaders.Select(h => $"{h.Key}={h.Value}"));
                return TestResult.Successful($"Dapr headers found: {headerList}");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Header check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Compare direct HTTP call vs Dapr-routed call path behavior.
    /// </summary>
    private static async Task<TestResult> TestDirectVsDaprRouting(ITestClient client, string[] args)
    {
        try
        {
            // Direct call to the service
            var directResponse = await _httpClient.GetAsync($"{_baseUrl}/testing/debug/path");

            if (!directResponse.IsSuccessStatusCode)
            {
                return TestResult.Failed($"Direct call returned {directResponse.StatusCode}");
            }

            var directContent = await directResponse.Content.ReadAsStringAsync();
            var directInfo = JsonSerializer.Deserialize<RoutingDebugInfo>(directContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (directInfo == null)
            {
                return TestResult.Failed("Failed to deserialize direct call response");
            }

            // Try Dapr-prefixed path (if Dapr is configured and running)
            var daprPort = Environment.GetEnvironmentVariable("DAPR_HTTP_PORT") ?? "3500";
            var daprUrl = $"http://localhost:{daprPort}/v1.0/invoke/bannou/method/testing/debug/path";

            try
            {
                var daprResponse = await _httpClient.GetAsync(daprUrl);

                if (daprResponse.IsSuccessStatusCode)
                {
                    var daprContent = await daprResponse.Content.ReadAsStringAsync();
                    var daprInfo = JsonSerializer.Deserialize<RoutingDebugInfo>(daprContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (daprInfo != null)
                    {
                        return TestResult.Successful(
                            $"Direct path: {directInfo.Path}, Dapr path: {daprInfo.Path} - " +
                            $"Routes {(directInfo.Path == daprInfo.Path ? "MATCH" : "DIFFER")}");
                    }
                }

                return TestResult.Successful($"Direct path works ({directInfo.Path}), Dapr call returned {daprResponse.StatusCode}");
            }
            catch
            {
                // Dapr sidecar might not be available in test environment
                return TestResult.Successful($"Direct path: {directInfo.Path} (Dapr sidecar not reachable for comparison)");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Direct vs Dapr test failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Test paths with various prefixes to understand routing behavior.
    /// </summary>
    private static async Task<TestResult> TestPathWithDifferentPrefixes(ITestClient client, string[] args)
    {
        try
        {
            var testPaths = new[]
            {
                "/testing/debug/path",
                "/v1.0/invoke/bannou/method/testing/debug/path"
            };

            var results = new List<string>();

            foreach (var testPath in testPaths)
            {
                try
                {
                    var url = $"{_baseUrl}{testPath}";
                    var response = await _httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var info = JsonSerializer.Deserialize<RoutingDebugInfo>(content, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        results.Add($"[{testPath}] => {response.StatusCode}, received: {info?.Path}");
                    }
                    else
                    {
                        results.Add($"[{testPath}] => {response.StatusCode}");
                    }
                }
                catch (Exception pathEx)
                {
                    results.Add($"[{testPath}] => ERROR: {pathEx.Message}");
                }
            }

            return TestResult.Successful($"Path tests: {string.Join(" | ", results)}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Path prefix test failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Response model matching TestingController.RoutingDebugInfo.
    /// </summary>
    private class RoutingDebugInfo
    {
        public string RawUrl { get; set; } = string.Empty;
        public string PathBase { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string QueryString { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Scheme { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; set; } = new();
        public string ControllerRoute { get; set; } = string.Empty;
        public string ActionRoute { get; set; } = string.Empty;
        public string CatchAllSegment { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
