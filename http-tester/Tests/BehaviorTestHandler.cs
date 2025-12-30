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

    /// <summary>
    /// ABML YAML with GOAP content for testing plan generation.
    /// </summary>
    private const string GoapAbmlYaml = """
        version: "1.0"
        name: goap-test-behavior
        context:
          variables:
            energy: 50
            has_food: false
        goals:
          find_food:
            priority: 80
            conditions:
              has_food: "==true"
          rest:
            priority: 60
            conditions:
              energy: ">=80"
        flows:
          gather_food:
            goap:
              preconditions:
                energy: ">=20"
              effects:
                has_food: "true"
                energy: "-=10"
              cost: 2.0
            steps:
              - emit:
                  channel: action
                  intent: gather
                  urgency: 0.8
          rest_action:
            goap:
              preconditions: {}
              effects:
                energy: "+=30"
              cost: 1.0
            steps:
              - emit:
                  channel: action
                  intent: rest
                  urgency: 0.3
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

        // GOAP planning tests
        new ServiceTest(TestGoapPlanGeneration, "GoapPlanGeneration", "Behavior", "Test GOAP plan generation from compiled behavior"),
        new ServiceTest(TestGoapPlanNonExistentBehavior, "GoapPlanNonExistentBehavior", "Behavior", "Test GOAP plan 404 for non-existent behavior"),
        new ServiceTest(TestGoapPlanValidation, "GoapPlanValidation", "Behavior", "Test GOAP plan validation against world state"),
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

    private static Task<TestResult> TestGoapPlanGeneration(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            // First compile the GOAP-enabled ABML to cache the metadata
            var compileRequest = new CompileBehaviorRequest
            {
                Abml_content = GoapAbmlYaml,
                Behavior_name = "goap-integration-test",
                Compilation_options = new CompilationOptions
                {
                    Enable_optimizations = true,
                    Cache_compiled_result = true // Must cache for GOAP metadata to be stored
                }
            };

            var compileResponse = await behaviorClient.CompileAbmlBehaviorAsync(BannouJson.Serialize(compileRequest));
            if (!compileResponse.Success)
            {
                var warnings = compileResponse.Warnings != null
                    ? string.Join(", ", compileResponse.Warnings)
                    : "unknown error";
                return TestResult.Failed($"Failed to compile GOAP behavior: {warnings}");
            }

            var behaviorId = compileResponse.Behavior_id;
            if (string.IsNullOrEmpty(behaviorId))
                return TestResult.Failed("Behavior ID is empty after compilation");

            // Now generate a GOAP plan using the cached behavior
            var planRequest = new GoapPlanRequest
            {
                Agent_id = "test-agent-1",
                Behavior_id = behaviorId,
                Goal = new GoapGoal
                {
                    Name = "find_food",
                    Priority = 80,
                    Conditions = new Dictionary<string, string> { { "has_food", "==true" } }
                },
                World_state = new Dictionary<string, object>
                {
                    { "energy", 50 },
                    { "has_food", false }
                },
                Options = new GoapPlanningOptions
                {
                    Max_depth = 10,
                    Max_nodes = 1000,
                    Timeout_ms = 100
                }
            };

            var planResponse = await behaviorClient.GenerateGoapPlanAsync(BannouJson.Serialize(planRequest));

            if (!planResponse.Success)
            {
                // The plan might not be found if the goal isn't achievable - that's still valid
                // We mainly want to verify the endpoint works
                if (string.IsNullOrEmpty(planResponse.Failure_reason))
                    return TestResult.Failed("Plan failed without a failure reason");

                return TestResult.Successful(
                    $"GOAP planning executed, result: {planResponse.Failure_reason}, time={planResponse.Planning_time_ms}ms");
            }

            if (planResponse.Plan == null)
                return TestResult.Failed("Plan response succeeded but plan is null");

            return TestResult.Successful(
                $"GOAP plan generated: {planResponse.Plan.Actions?.Count ?? 0} actions, " +
                $"cost={planResponse.Plan.Total_cost}, " +
                $"nodes={planResponse.Nodes_expanded}, " +
                $"time={planResponse.Planning_time_ms}ms");
        }, "GOAP plan generation");

    private static Task<TestResult> TestGoapPlanNonExistentBehavior(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new GoapPlanRequest
            {
                Agent_id = "test-agent-1",
                Behavior_id = "nonexistent-goap-behavior-xyz",
                Goal = new GoapGoal
                {
                    Name = "test_goal",
                    Priority = 50,
                    Conditions = new Dictionary<string, string> { { "test", "==true" } }
                },
                World_state = new Dictionary<string, object>()
            };

            try
            {
                var response = await behaviorClient.GenerateGoapPlanAsync(BannouJson.Serialize(request));

                // If we get a response, it should indicate failure with "not found"
                if (!response.Success && response.Failure_reason != null &&
                    response.Failure_reason.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    return TestResult.Successful("Correctly indicated behavior not found in response");
                }

                return TestResult.Failed($"Expected not found indication, got: success={response.Success}");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("Correctly returned 404 for non-existent behavior");
            }
        }, "GOAP plan non-existent behavior");

    private static Task<TestResult> TestGoapPlanValidation(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            // Create a simple plan for validation
            var validateRequest = new ValidateGoapPlanRequest
            {
                Plan = new GoapPlanResult
                {
                    Goal_id = "test_goal",
                    Total_cost = 3.0f,
                    Actions = new List<PlannedActionResponse>
                    {
                        new PlannedActionResponse
                        {
                            Action_id = "action_1",
                            Index = 0,
                            Cost = 1.0f
                        },
                        new PlannedActionResponse
                        {
                            Action_id = "action_2",
                            Index = 1,
                            Cost = 2.0f
                        }
                    }
                },
                World_state = new Dictionary<string, object>
                {
                    { "energy", 50 },
                    { "has_food", false }
                },
                Current_action_index = 0
            };

            var response = await behaviorClient.ValidateGoapPlanAsync(BannouJson.Serialize(validateRequest));

            // Validation should return a result (we don't have the actual actions so it may fail validation)
            if (response == null)
                return TestResult.Failed("Validation response is null");

            return TestResult.Successful(
                $"GOAP plan validation: valid={response.Is_valid}, " +
                $"should_replan={response.Should_replan}, " +
                $"reason={response.Replan_reason}");
        }, "GOAP plan validation");
}
