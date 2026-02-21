using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Telemetry;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for the Telemetry service HTTP API endpoints.
/// Tests telemetry health and status endpoints via NSwag-generated TelemetryClient.
/// </summary>
public class TelemetryTestHandler : BaseHttpTestHandler
{
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
        new ServiceTest(TestStatusReturnsSamplingRatio, "SamplingRatio", "Telemetry", "Verify status includes valid sampling ratio"),
    ];

    private static async Task<TestResult> TestHealthEndpoint(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var telemetryClient = GetServiceClient<ITelemetryClient>();

            var response = await telemetryClient.HealthAsync(new TelemetryHealthRequest());

            return TestResult.Successful($"Health endpoint OK - Healthy: {response.Healthy}");
        }, "Health endpoint");

    private static async Task<TestResult> TestHealthReturnsHealthyFlag(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var telemetryClient = GetServiceClient<ITelemetryClient>();

            var response = await telemetryClient.HealthAsync(new TelemetryHealthRequest());

            // The healthy flag should be present (true or false is valid)
            return TestResult.Successful($"Healthy flag present: {response.Healthy}");
        }, "Healthy flag");

    private static async Task<TestResult> TestHealthReturnsTelemetryFlags(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var telemetryClient = GetServiceClient<ITelemetryClient>();

            var response = await telemetryClient.HealthAsync(new TelemetryHealthRequest());

            return TestResult.Successful(
                $"Telemetry flags - TracingEnabled: {response.TracingEnabled}, MetricsEnabled: {response.MetricsEnabled}");
        }, "Telemetry flags");

    private static async Task<TestResult> TestStatusEndpoint(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var telemetryClient = GetServiceClient<ITelemetryClient>();

            var response = await telemetryClient.StatusAsync(new TelemetryStatusRequest());

            return TestResult.Successful(
                $"Status endpoint OK - Service: {response.ServiceName}, Environment: {response.DeploymentEnvironment}");
        }, "Status endpoint");

    private static async Task<TestResult> TestStatusReturnsServiceInfo(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var telemetryClient = GetServiceClient<ITelemetryClient>();

            var response = await telemetryClient.StatusAsync(new TelemetryStatusRequest());

            if (string.IsNullOrEmpty(response.ServiceName))
                return TestResult.Failed("ServiceName is empty or null");

            if (string.IsNullOrEmpty(response.ServiceNamespace))
                return TestResult.Failed("ServiceNamespace is empty or null");

            return TestResult.Successful(
                $"Service info - Name: {response.ServiceName}, Namespace: {response.ServiceNamespace}");
        }, "Service info");

    private static async Task<TestResult> TestStatusReturnsOtlpConfig(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var telemetryClient = GetServiceClient<ITelemetryClient>();

            var response = await telemetryClient.StatusAsync(new TelemetryStatusRequest());

            if (string.IsNullOrEmpty(response.OtlpProtocol))
                return TestResult.Failed("OtlpProtocol is empty or null");

            // OtlpEndpoint may be null if telemetry is disabled, that's valid
            var endpointInfo = response.OtlpEndpoint ?? "(not configured)";
            return TestResult.Successful(
                $"OTLP config - Endpoint: {endpointInfo}, Protocol: {response.OtlpProtocol}");
        }, "OTLP config");

    private static async Task<TestResult> TestStatusReturnsSamplingRatio(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var telemetryClient = GetServiceClient<ITelemetryClient>();

            var response = await telemetryClient.StatusAsync(new TelemetryStatusRequest());

            // Sampling ratio should be between 0.0 and 1.0
            if (response.SamplingRatio < 0.0 || response.SamplingRatio > 1.0)
                return TestResult.Failed($"SamplingRatio out of range: {response.SamplingRatio}");

            return TestResult.Successful($"Sampling ratio: {response.SamplingRatio}");
        }, "Sampling ratio");
}
