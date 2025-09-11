using System.Text.Json;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Demonstration of schema-driven test generation capabilities
/// </summary>
public class TestGenerationDemo
{
    public static async Task<DemoResult> RunDemo(TestConfiguration configuration)
    {
        var result = new DemoResult();

        try
        {
            Console.WriteLine("=== Bannou Schema-Driven Test Generation Demo ===");
            Console.WriteLine();

            // 1. Initialize schema generator
            var generator = new SchemaTestGenerator();
            var handler = new EnhancedAccountTestHandler();

            Console.WriteLine("1. Loading OpenAPI schema and generating tests...");
            var schemaPath = handler.GetSchemaFilePath();

            if (!File.Exists(schemaPath))
            {
                result.Errors.Add($"Schema file not found: {schemaPath}");
                Console.WriteLine($"   ❌ Schema file not found: {schemaPath}");
                return result;
            }

            // Generate schema-based tests
            var schemaTests = await handler.GetSchemaBasedTests(generator);
            var manualTests = handler.GetServiceTests();

            Console.WriteLine($"   ✅ Generated {schemaTests.Length} schema-based tests");
            Console.WriteLine($"   ✅ Found {manualTests.Length} manual tests");
            result.SchemaTestCount = schemaTests.Length;
            result.ManualTestCount = manualTests.Length;

            // 2. Display test categories
            Console.WriteLine();
            Console.WriteLine("2. Generated Test Categories:");
            var testsByType = schemaTests.GroupBy(t => t.Type).ToList();
            foreach (var group in testsByType)
            {
                Console.WriteLine($"   📋 {group.Key}: {group.Count()} tests");
                foreach (var test in group.Take(3)) // Show first 3 tests as examples
                {
                    Console.WriteLine($"      • {test.Name}: {test.Description}");
                }
                if (group.Count() > 3)
                {
                    Console.WriteLine($"      ... and {group.Count() - 3} more");
                }
            }

            // 3. Test transport availability
            Console.WriteLine();
            Console.WriteLine("3. Testing Transport Availability:");

            var httpAvailable = configuration.HasHttpRequired();
            var wsAvailable = configuration.HasWebSocketRequired();

            Console.WriteLine($"   🌐 HTTP Transport: {(httpAvailable ? "✅ Available" : "❌ Not configured")}");
            Console.WriteLine($"   🔌 WebSocket Transport: {(wsAvailable ? "✅ Available" : "❌ Not configured")}");

            result.HttpTransportAvailable = httpAvailable;
            result.WebSocketTransportAvailable = wsAvailable;

            // 4. Run dual transport tests if both are available
            if (httpAvailable && wsAvailable)
            {
                Console.WriteLine();
                Console.WriteLine("4. Running Dual Transport Test Sample...");

                try
                {
                    var dualRunner = new DualTransportTestRunner(configuration);
                    var dualResults = await dualRunner.RunDualTransportTests(handler);

                    result.DualTransportResults = dualResults;

                    var successCount = dualResults.Count(r => r.BothSucceeded);
                    var discrepancyCount = dualResults.Count(r => r.HasTransportDiscrepancy);

                    Console.WriteLine($"   ✅ Executed {dualResults.Length} tests via both transports");
                    Console.WriteLine($"   ✅ {successCount} tests succeeded on both transports");

                    if (discrepancyCount > 0)
                    {
                        Console.WriteLine($"   ⚠️  {discrepancyCount} tests had transport discrepancies");
                        result.Warnings.Add($"{discrepancyCount} tests had transport discrepancies");
                    }

                    // Show sample results
                    Console.WriteLine();
                    Console.WriteLine("   Sample Results:");
                    foreach (var testResult in dualResults.Take(5))
                    {
                        Console.WriteLine($"      {testResult}");
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Dual transport testing failed: {ex.Message}");
                    Console.WriteLine($"   ❌ Dual transport testing failed: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("4. Skipping dual transport tests (not all transports configured)");
                result.Warnings.Add("Dual transport testing skipped - configuration incomplete");
            }

            // 5. Schema Analysis
            Console.WriteLine();
            Console.WriteLine("5. Schema Analysis Summary:");
            await DisplaySchemaAnalysis(schemaPath);

            result.Success = true;
            Console.WriteLine();
            Console.WriteLine("=== Demo Completed Successfully ===");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Demo failed with exception: {ex.Message}");
            Console.WriteLine($"❌ Demo failed: {ex.Message}");
        }

        return result;
    }

    private static async Task DisplaySchemaAnalysis(string schemaPath)
    {
        try
        {
            var content = await File.ReadAllTextAsync(schemaPath);

            // Basic YAML analysis without full parsing
            var lines = content.Split('\n');
            var pathCount = lines.Count(l => l.Trim().StartsWith("paths:") || l.Contains("/{"));
            var operationCount = lines.Count(l => l.Trim().StartsWith("post:") || l.Trim().StartsWith("get:") || l.Trim().StartsWith("put:") || l.Trim().StartsWith("delete:"));
            var schemaCount = lines.Count(l => l.Trim().StartsWith("schemas:") || l.Contains("$ref:"));

            Console.WriteLine($"   📄 Schema File: {Path.GetFileName(schemaPath)}");
            Console.WriteLine($"   🛣️  Estimated Paths: ~{pathCount}");
            Console.WriteLine($"   ⚡ Estimated Operations: ~{operationCount}");
            Console.WriteLine($"   📦 Schema References: ~{schemaCount}");
            Console.WriteLine($"   📏 File Size: {new FileInfo(schemaPath).Length:N0} bytes");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ Schema analysis failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Result of running the test generation demo
/// </summary>
public class DemoResult
{
    public bool Success { get; set; }
    public int SchemaTestCount { get; set; }
    public int ManualTestCount { get; set; }
    public bool HttpTransportAvailable { get; set; }
    public bool WebSocketTransportAvailable { get; set; }
    public DualTransportTestResult[] DualTransportResults { get; set; } = Array.Empty<DualTransportTestResult>();
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public string GenerateReport()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("# Schema-Driven Test Generation Report");
        report.AppendLine();

        report.AppendLine($"**Status:** {(Success ? "✅ Success" : "❌ Failed")}");
        report.AppendLine($"**Generated Tests:** {SchemaTestCount} (schema) + {ManualTestCount} (manual) = {SchemaTestCount + ManualTestCount} total");
        report.AppendLine($"**HTTP Transport:** {(HttpTransportAvailable ? "Available" : "Not Available")}");
        report.AppendLine($"**WebSocket Transport:** {(WebSocketTransportAvailable ? "Available" : "Not Available")}");
        report.AppendLine();

        if (DualTransportResults.Any())
        {
            var successCount = DualTransportResults.Count(r => r.BothSucceeded);
            var failCount = DualTransportResults.Count(r => r.BothFailed);
            var discrepancyCount = DualTransportResults.Count(r => r.HasTransportDiscrepancy);

            report.AppendLine("## Dual Transport Test Results");
            report.AppendLine($"- **Both Succeeded:** {successCount}");
            report.AppendLine($"- **Both Failed:** {failCount}");
            report.AppendLine($"- **Transport Discrepancies:** {discrepancyCount}");
            report.AppendLine();

            if (discrepancyCount > 0)
            {
                report.AppendLine("### Transport Discrepancies");
                foreach (var result in DualTransportResults.Where(r => r.HasTransportDiscrepancy))
                {
                    report.AppendLine($"- **{result.TestName}:** HTTP({(result.HttpResult?.Success == true ? "✅" : "❌")}) vs WS({(result.WebSocketResult?.Success == true ? "✅" : "❌")})");
                }
                report.AppendLine();
            }
        }

        if (Errors.Any())
        {
            report.AppendLine("## Errors");
            foreach (var error in Errors)
            {
                report.AppendLine($"- ❌ {error}");
            }
            report.AppendLine();
        }

        if (Warnings.Any())
        {
            report.AppendLine("## Warnings");
            foreach (var warning in Warnings)
            {
                report.AppendLine($"- ⚠️ {warning}");
            }
            report.AppendLine();
        }

        return report.ToString();
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
