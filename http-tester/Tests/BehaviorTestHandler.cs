using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for behavior service API endpoints using generated clients.
/// Tests ABML compilation, validation, and cache management.
/// </summary>
public class BehaviorTestHandler : BaseHttpTestHandler
{
    /// <summary>
    /// Sample ABML YAML for testing compilation.
    /// </summary>
    private const string ValidAbmlYaml = """
        version: "1.0"
        name: test-behavior
        context:
        variables:
            health: 100
            stamina: 50
        flows:
        main:
            - emit:
                channel: action
                intent: idle
                urgency: 0.5
        """;

    /// <summary>
    /// Invalid ABML YAML for testing error handling.
    /// </summary>
    private const string InvalidAbmlYaml = """
        version: "1.0"
        name: invalid-behavior
        flows:
        main:
            - goto: nonexistent_flow
        """;

    public override ServiceTest[] GetServiceTests() =>
    [
        // Compilation tests
        new ServiceTest(TestCompileValidAbml, "CompileValidAbml", "Behavior", "Test compiling valid ABML YAML"),
        new ServiceTest(TestCompileInvalidAbml, "CompileInvalidAbml", "Behavior", "Test compile error handling for invalid ABML"),
        new ServiceTest(TestCompileEmptyAbml, "CompileEmptyAbml", "Behavior", "Test compile error for empty ABML"),
        new ServiceTest(TestCompileWithBundleId, "CompileWithBundleId", "Behavior", "Test compilation with bundle grouping"),
        new ServiceTest(TestCompileWithNameAndCategory, "CompileWithNameAndCategory", "Behavior", "Test compilation with custom name and category"),

        // Validation tests
        new ServiceTest(TestValidateValidAbml, "ValidateValidAbml", "Behavior", "Test validation of valid ABML"),
        new ServiceTest(TestValidateInvalidAbml, "ValidateInvalidAbml", "Behavior", "Test validation error detection"),

        // Cache tests (require asset service)
        new ServiceTest(TestGetNonExistentBehavior, "GetNonExistentBehavior", "Behavior", "Test 404 for non-existent cached behavior"),
    ];

    private static Task<TestResult> TestCompileValidAbml(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new CompileBehaviorRequest
            {
                Abml_content = ValidAbmlYaml,
                Compilation_options = new CompilationOptions
                {
                    Enable_optimizations = true,
                    Cache_compiled_result = false // Don't cache for test
                }
            };

            var response = await behaviorClient.CompileAbmlBehaviorAsync(BannouJson.Serialize(request));

            if (!response.Success)
            {
                var warnings = response.Warnings != null
                    ? string.Join(", ", response.Warnings)
                    : "no warnings";
                return TestResult.Failed($"Compilation failed: {warnings}");
            }

            if (string.IsNullOrEmpty(response.Behavior_id))
                return TestResult.Failed("Behavior ID is empty");

            if (response.Compilation_time_ms < 0)
                return TestResult.Failed("Invalid compilation time");

            return TestResult.Successful(
                $"Compiled successfully: ID={response.Behavior_id}, Time={response.Compilation_time_ms}ms");
        }, "Compile valid ABML");

    private static Task<TestResult> TestCompileInvalidAbml(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new CompileBehaviorRequest
            {
                Abml_content = InvalidAbmlYaml,
                Compilation_options = new CompilationOptions
                {
                    Enable_optimizations = false,
                    Cache_compiled_result = false
                }
            };

            try
            {
                var response = await behaviorClient.CompileAbmlBehaviorAsync(BannouJson.Serialize(request));

                // Should return success=false with warnings for semantic errors
                if (response.Success)
                    return TestResult.Failed("Expected compilation to fail for invalid ABML");

                if (response.Warnings == null || !response.Warnings.Any())
                    return TestResult.Failed("Expected warnings for invalid ABML");

                return TestResult.Successful(
                    $"Correctly detected errors: {string.Join(", ", response.Warnings)}");
            }
            catch (ApiException<AbmlErrorResponse> ex)
            {
                // 400 response with error details is also acceptable
                return TestResult.Successful($"Correctly returned error: {ex.Result?.Error}");
            }
        }, "Compile invalid ABML");

    private static Task<TestResult> TestCompileEmptyAbml(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new CompileBehaviorRequest
            {
                Abml_content = "",
                Compilation_options = new CompilationOptions
                {
                    Cache_compiled_result = false
                }
            };

            try
            {
                var response = await behaviorClient.CompileAbmlBehaviorAsync(BannouJson.Serialize(request));

                if (response.Success)
                    return TestResult.Failed("Expected compilation to fail for empty ABML");

                return TestResult.Successful("Correctly rejected empty ABML");
            }
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                return TestResult.Successful("Correctly returned 400 for empty ABML");
            }
        }, "Compile empty ABML");

    private static Task<TestResult> TestValidateValidAbml(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new ValidateAbmlRequest
            {
                Abml_content = ValidAbmlYaml,
                Strict_mode = true
            };

            var response = await behaviorClient.ValidateAbmlAsync(BannouJson.Serialize(request));

            if (!response.Is_valid)
            {
                var errors = response.Validation_errors != null
                    ? string.Join(", ", response.Validation_errors.Select(e => e.Message))
                    : "no errors provided";
                return TestResult.Failed($"Validation failed unexpectedly: {errors}");
            }

            return TestResult.Successful($"Validation passed, schema version: {response.Schema_version}");
        }, "Validate valid ABML");

    private static Task<TestResult> TestValidateInvalidAbml(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new ValidateAbmlRequest
            {
                Abml_content = InvalidAbmlYaml,
                Strict_mode = true
            };

            var response = await behaviorClient.ValidateAbmlAsync(BannouJson.Serialize(request));

            if (response.Is_valid)
                return TestResult.Failed("Expected validation to fail for invalid ABML");

            if (response.Validation_errors == null || !response.Validation_errors.Any())
                return TestResult.Failed("Expected validation errors to be returned");

            var errorMessages = response.Validation_errors.Select(e => e.Message);
            return TestResult.Successful(
                $"Correctly detected {response.Validation_errors.Count} error(s): {string.Join(", ", errorMessages)}");
        }, "Validate invalid ABML");

    private static Task<TestResult> TestCompileWithBundleId(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new CompileBehaviorRequest
            {
                Abml_content = ValidAbmlYaml,
                Behavior_name = "test-bundle-behavior",
                Bundle_id = "test-bundle-group",
                Compilation_options = new CompilationOptions
                {
                    Enable_optimizations = true,
                    Cache_compiled_result = true // Cache to test asset storage
                }
            };

            var response = await behaviorClient.CompileAbmlBehaviorAsync(BannouJson.Serialize(request));

            if (!response.Success)
            {
                var warnings = response.Warnings != null
                    ? string.Join(", ", response.Warnings)
                    : "no warnings";
                return TestResult.Failed($"Compilation failed: {warnings}");
            }

            if (string.IsNullOrEmpty(response.Behavior_id))
                return TestResult.Failed("Behavior ID is empty");

            if (response.Behavior_name != "test-bundle-behavior")
                return TestResult.Failed($"Expected behavior_name 'test-bundle-behavior', got '{response.Behavior_name}'");

            if (response.Bundle_id != "test-bundle-group")
                return TestResult.Failed($"Expected bundle_id 'test-bundle-group', got '{response.Bundle_id}'");

            // Asset ID should be set when caching is enabled
            if (string.IsNullOrEmpty(response.Asset_id))
                return TestResult.Failed("Asset ID should be set when caching is enabled");

            return TestResult.Successful(
                $"Compiled with bundle: ID={response.Behavior_id}, Bundle={response.Bundle_id}, Asset={response.Asset_id}");
        }, "Compile with bundle ID");

    private static Task<TestResult> TestCompileWithNameAndCategory(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new CompileBehaviorRequest
            {
                Abml_content = ValidAbmlYaml,
                Behavior_name = "blacksmith-daily-routine",
                Behavior_category = CompileBehaviorRequestBehavior_category.Professional,
                Compilation_options = new CompilationOptions
                {
                    Enable_optimizations = true,
                    Cache_compiled_result = false
                }
            };

            var response = await behaviorClient.CompileAbmlBehaviorAsync(BannouJson.Serialize(request));

            if (!response.Success)
            {
                var warnings = response.Warnings != null
                    ? string.Join(", ", response.Warnings)
                    : "no warnings";
                return TestResult.Failed($"Compilation failed: {warnings}");
            }

            if (response.Behavior_name != "blacksmith-daily-routine")
                return TestResult.Failed($"Expected behavior_name 'blacksmith-daily-routine', got '{response.Behavior_name}'");

            return TestResult.Successful(
                $"Compiled with name/category: Name={response.Behavior_name}, ID={response.Behavior_id}");
        }, "Compile with name and category");

    private static Task<TestResult> TestGetNonExistentBehavior(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new GetCachedBehaviorRequest
            {
                BehaviorId = "nonexistent-behavior-12345"
            };

            try
            {
                await behaviorClient.GetCachedBehaviorAsync(request);
                return TestResult.Failed("Expected 404 for non-existent behavior");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("Correctly returned 404 for non-existent behavior");
            }
        }, "Get non-existent behavior");
}
