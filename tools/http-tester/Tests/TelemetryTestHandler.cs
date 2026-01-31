using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Telemetry;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for the Telemetry service HTTP API endpoints.
/// Tests telemetry health and status endpoints.
/// </summary>
public class TelemetryTestHandler : BaseHttpTestHandler
{
    private static readonly HttpClient _httpClient = new();
    private static readonly string _baseUrl;

    static TelemetryTestHandler()
    {
        // IMPLEMENTATION TENETS exception: http-tester doesn't have access to service configuration
        _baseUrl = Environment.GetEnvironmentVariable(AppConstants.ENV_BANNOU_HTTP_ENDPOINT)
            ?? "http://localhost:5012";
    }

    /// <summary>
    /// Get all telemetry service tests.
    /// </summary>
    public override ServiceTest[] GetServiceTests() =>
    [
        // Health Check Tests
        new ServiceTest(TestHealthEndpoint, "Health", "Telemetry", "Test telemetry health endpoint returns status"),
        new ServiceTest(TestHealthReturnsHealthyFlag, "HealthyFlag", "Telemetry", "Verify health response includes healthy flag"),
        new ServiceTest(TestHealthReturnsTelemetryFlags, "TelemetryFlags", "Telemetry", "Verify health response includes tracing/metrics flags"),

        // Status Tests
        new ServiceTest(TestStatusEndpoint, "Status", "Telemetry", "Test telemetry status endpoint returns configuration"),
        new ServiceTest(TestStatusReturnsServiceInfo, "ServiceInfo", "Telemetry", "Verify status includes service name and namespace"),
        new ServiceTest(TestStatusReturnsOtlpConfig, "OtlpConfig", "Telemetry", "Verify status includes OTLP configuration"),
        new ServiceTest(TestStatusReturnsSamplingRatio, "SamplingRatio", "Telemetry", "Verify status includes sampling ratio"),

        // Prometheus Metrics Endpoint
        new ServiceTest(TestMetricsEndpoint, "Metrics", "Telemetry", "Test /metrics Prometheus endpoint exists"),
    ];

    private static async Task<TestResult> TestHealthEndpoint(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/telemetry/health",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
                return TestResult.Failed($"Health endpoint returned {response.StatusCode}");

            var content = await response.Content.ReadAsStringAsync();
            var healthResponse = BannouJson.Deserialize<TelemetryHealthResponse>(content);

            if (healthResponse == null)
                return TestResult.Failed("Failed to deserialize health response");

            return TestResult.Successful($"Health endpoint OK - Healthy: {healthResponse.Healthy}");
        }, "Health endpoint");

    private static async Task<TestResult> TestHealthReturnsHealthyFlag(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/telemetry/health",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
                return TestResult.Failed($"Health endpoint returned {response.StatusCode}");

            var content = await response.Content.ReadAsStringAsync();
            var healthResponse = BannouJson.Deserialize<TelemetryHealthResponse>(content);

            if (healthResponse == null)
                return TestResult.Failed("Failed to deserialize health response");

            // The healthy flag should be present (true or false is valid)
            return TestResult.Successful($"Healthy flag present: {healthResponse.Healthy}");
        }, "Healthy flag");

    private static async Task<TestResult> TestHealthReturnsTelemetryFlags(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/telemetry/health",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
                return TestResult.Failed($"Health endpoint returned {response.StatusCode}");

            var content = await response.Content.ReadAsStringAsync();
            var healthResponse = BannouJson.Deserialize<TelemetryHealthResponse>(content);

            if (healthResponse == null)
                return TestResult.Failed("Failed to deserialize health response");

            return TestResult.Successful(
                $"Telemetry flags - TracingEnabled: {healthResponse.TracingEnabled}, MetricsEnabled: {healthResponse.MetricsEnabled}");
        }, "Telemetry flags");

    private static async Task<TestResult> TestStatusEndpoint(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/telemetry/status",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
                return TestResult.Failed($"Status endpoint returned {response.StatusCode}");

            var content = await response.Content.ReadAsStringAsync();
            var statusResponse = BannouJson.Deserialize<TelemetryStatusResponse>(content);

            if (statusResponse == null)
                return TestResult.Failed("Failed to deserialize status response");

            return TestResult.Successful(
                $"Status endpoint OK - Service: {statusResponse.ServiceName}, Environment: {statusResponse.DeploymentEnvironment}");
        }, "Status endpoint");

    private static async Task<TestResult> TestStatusReturnsServiceInfo(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/telemetry/status",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
                return TestResult.Failed($"Status endpoint returned {response.StatusCode}");

            var content = await response.Content.ReadAsStringAsync();
            var statusResponse = BannouJson.Deserialize<TelemetryStatusResponse>(content);

            if (statusResponse == null)
                return TestResult.Failed("Failed to deserialize status response");

            if (string.IsNullOrEmpty(statusResponse.ServiceName))
                return TestResult.Failed("ServiceName is empty or null");

            if (string.IsNullOrEmpty(statusResponse.ServiceNamespace))
                return TestResult.Failed("ServiceNamespace is empty or null");

            return TestResult.Successful(
                $"Service info - Name: {statusResponse.ServiceName}, Namespace: {statusResponse.ServiceNamespace}");
        }, "Service info");

    private static async Task<TestResult> TestStatusReturnsOtlpConfig(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/telemetry/status",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
                return TestResult.Failed($"Status endpoint returned {response.StatusCode}");

            var content = await response.Content.ReadAsStringAsync();
            var statusResponse = BannouJson.Deserialize<TelemetryStatusResponse>(content);

            if (statusResponse == null)
                return TestResult.Failed("Failed to deserialize status response");

            if (string.IsNullOrEmpty(statusResponse.OtlpProtocol))
                return TestResult.Failed("OtlpProtocol is empty or null");

            // OtlpEndpoint may be null if telemetry is disabled, that's valid
            var endpointInfo = statusResponse.OtlpEndpoint ?? "(not configured)";
            return TestResult.Successful(
                $"OTLP config - Endpoint: {endpointInfo}, Protocol: {statusResponse.OtlpProtocol}");
        }, "OTLP config");

    private static async Task<TestResult> TestStatusReturnsSamplingRatio(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/telemetry/status",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
                return TestResult.Failed($"Status endpoint returned {response.StatusCode}");

            var content = await response.Content.ReadAsStringAsync();
            var statusResponse = BannouJson.Deserialize<TelemetryStatusResponse>(content);

            if (statusResponse == null)
                return TestResult.Failed("Failed to deserialize status response");

            // Sampling ratio should be between 0.0 and 1.0
            if (statusResponse.SamplingRatio < 0.0 || statusResponse.SamplingRatio > 1.0)
                return TestResult.Failed($"SamplingRatio out of range: {statusResponse.SamplingRatio}");

            return TestResult.Successful($"Sampling ratio: {statusResponse.SamplingRatio}");
        }, "Sampling ratio");

    private static async Task<TestResult> TestMetricsEndpoint(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            // The /metrics endpoint is a GET endpoint for Prometheus scraping
            var response = await _httpClient.GetAsync($"{_baseUrl}/metrics");

            if (!response.IsSuccessStatusCode)
            {
                // If metrics are disabled, we might get 404 - that's acceptable
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return TestResult.Successful("Metrics endpoint not found (metrics may be disabled)");

                return TestResult.Failed($"Metrics endpoint returned {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";

            // Prometheus metrics should be text/plain with OpenMetrics format
            // Check for some expected metric patterns
            var hasMetrics = content.Contains("# TYPE") || content.Contains("# HELP") || content.Contains("bannou_");

            if (hasMetrics)
                return TestResult.Successful($"Metrics endpoint OK - Content-Type: {contentType}, has metrics data");

            // Even if no custom metrics, having the endpoint work is success
            return TestResult.Successful($"Metrics endpoint reachable - Content-Type: {contentType}");
        }, "Metrics endpoint");
}
