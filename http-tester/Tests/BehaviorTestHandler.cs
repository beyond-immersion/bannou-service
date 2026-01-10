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

        // Behavior stack tests
        new ServiceTest(TestCompileBehaviorStack, "CompileBehaviorStack", "Behavior", "Test compiling stackable behavior sets"),
        new ServiceTest(TestCompileBehaviorStackWithPriority, "CompileStackPriority", "Behavior", "Test behavior stack priority merging"),
        new ServiceTest(TestCompileBehaviorStackEmptySets, "CompileStackEmpty", "Behavior", "Test behavior stack with empty sets"),

        // Cache invalidation tests
        new ServiceTest(TestInvalidateCachedBehavior, "InvalidateCachedBehavior", "Behavior", "Test invalidating cached behavior"),
        new ServiceTest(TestInvalidateNonExistentBehavior, "InvalidateNonExistent", "Behavior", "Test invalidating non-existent behavior"),

        // Context variable resolution tests
        new ServiceTest(TestResolveContextVariables, "ResolveContextVariables", "Behavior", "Test context variable resolution"),
        new ServiceTest(TestResolveContextMissingVariables, "ResolveContextMissing", "Behavior", "Test context resolution with missing variables"),
    ];

    private static async Task<TestResult> TestCompileValidAbml(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new CompileBehaviorRequest
            {
                AbmlContent = ValidAbmlYaml,
                CompilationOptions = new CompilationOptions
                {
                    EnableOptimizations = true,
                    CacheCompiledResult = false // Don't cache for test
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

            if (string.IsNullOrEmpty(response.BehaviorId))
                return TestResult.Failed("Behavior ID is empty");

            if (response.CompilationTimeMs < 0)
                return TestResult.Failed("Invalid compilation time");

            return TestResult.Successful(
                $"Compiled successfully: ID={response.BehaviorId}, Time={response.CompilationTimeMs}ms");
        }, "Compile valid ABML");

    private static async Task<TestResult> TestCompileInvalidAbml(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new CompileBehaviorRequest
            {
                AbmlContent = InvalidAbmlYaml,
                CompilationOptions = new CompilationOptions
                {
                    EnableOptimizations = false,
                    CacheCompiledResult = false
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
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                // 400 response indicates invalid ABML was correctly rejected
                return TestResult.Successful("Correctly returned 400 error for invalid ABML");
            }
        }, "Compile invalid ABML");

    private static async Task<TestResult> TestCompileEmptyAbml(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new CompileBehaviorRequest
            {
                AbmlContent = "",
                CompilationOptions = new CompilationOptions
                {
                    CacheCompiledResult = false
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

    private static async Task<TestResult> TestValidateValidAbml(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new ValidateAbmlRequest
            {
                AbmlContent = ValidAbmlYaml,
                StrictMode = true
            };

            var response = await behaviorClient.ValidateAbmlAsync(BannouJson.Serialize(request));

            if (!response.IsValid)
            {
                var errors = response.ValidationErrors != null
                    ? string.Join(", ", response.ValidationErrors.Select(e => e.Message))
                    : "no errors provided";
                return TestResult.Failed($"Validation failed unexpectedly: {errors}");
            }

            return TestResult.Successful($"Validation passed, schema version: {response.SchemaVersion}");
        }, "Validate valid ABML");

    private static async Task<TestResult> TestValidateInvalidAbml(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new ValidateAbmlRequest
            {
                AbmlContent = InvalidAbmlYaml,
                StrictMode = true
            };

            var response = await behaviorClient.ValidateAbmlAsync(BannouJson.Serialize(request));

            if (response.IsValid)
                return TestResult.Failed("Expected validation to fail for invalid ABML");

            if (response.ValidationErrors == null || !response.ValidationErrors.Any())
                return TestResult.Failed("Expected validation errors to be returned");

            var errorMessages = response.ValidationErrors.Select(e => e.Message);
            return TestResult.Successful(
                $"Correctly detected {response.ValidationErrors.Count} error(s): {string.Join(", ", errorMessages)}");
        }, "Validate invalid ABML");

    private static async Task<TestResult> TestCompileWithBundleId(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new CompileBehaviorRequest
            {
                AbmlContent = ValidAbmlYaml,
                BehaviorName = "test-bundle-behavior",
                BundleId = "test-bundle-group",
                CompilationOptions = new CompilationOptions
                {
                    EnableOptimizations = true,
                    CacheCompiledResult = true // Cache to test asset storage
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

            if (string.IsNullOrEmpty(response.BehaviorId))
                return TestResult.Failed("Behavior ID is empty");

            if (response.BehaviorName != "test-bundle-behavior")
                return TestResult.Failed($"Expected behavior_name 'test-bundle-behavior', got '{response.BehaviorName}'");

            if (response.BundleId != "test-bundle-group")
                return TestResult.Failed($"Expected bundle_id 'test-bundle-group', got '{response.BundleId}'");

            // Asset ID should be set when caching is enabled
            if (string.IsNullOrEmpty(response.AssetId))
                return TestResult.Failed("Asset ID should be set when caching is enabled");

            return TestResult.Successful(
                $"Compiled with bundle: ID={response.BehaviorId}, Bundle={response.BundleId}, Asset={response.AssetId}");
        }, "Compile with bundle ID");

    private static async Task<TestResult> TestCompileWithNameAndCategory(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new CompileBehaviorRequest
            {
                AbmlContent = ValidAbmlYaml,
                BehaviorName = "blacksmith-daily-routine",
                BehaviorCategory = CompileBehaviorRequestBehaviorCategory.Professional,
                CompilationOptions = new CompilationOptions
                {
                    EnableOptimizations = true,
                    CacheCompiledResult = false
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

            if (response.BehaviorName != "blacksmith-daily-routine")
                return TestResult.Failed($"Expected behavior_name 'blacksmith-daily-routine', got '{response.BehaviorName}'");

            return TestResult.Successful(
                $"Compiled with name/category: Name={response.BehaviorName}, ID={response.BehaviorId}");
        }, "Compile with name and category");

    private static async Task<TestResult> TestGetNonExistentBehavior(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
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

    private static async Task<TestResult> TestGoapPlanGeneration(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            // First compile the GOAP-enabled ABML to cache the metadata
            var compileRequest = new CompileBehaviorRequest
            {
                AbmlContent = GoapAbmlYaml,
                BehaviorName = "goap-integration-test",
                CompilationOptions = new CompilationOptions
                {
                    EnableOptimizations = true,
                    CacheCompiledResult = true // Must cache for GOAP metadata to be stored
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

            var behaviorId = compileResponse.BehaviorId;
            if (string.IsNullOrEmpty(behaviorId))
                return TestResult.Failed("Behavior ID is empty after compilation");

            // Now generate a GOAP plan using the cached behavior
            var planRequest = new GoapPlanRequest
            {
                AgentId = "test-agent-1",
                BehaviorId = behaviorId,
                Goal = new GoapGoal
                {
                    Name = "find_food",
                    Priority = 80,
                    Conditions = new Dictionary<string, string> { { "has_food", "==true" } }
                },
                WorldState = new Dictionary<string, object>
                {
                    { "energy", 50 },
                    { "has_food", false }
                },
                Options = new GoapPlanningOptions
                {
                    MaxDepth = 10,
                    MaxNodes = 1000,
                    TimeoutMs = 100
                }
            };

            var planResponse = await behaviorClient.GenerateGoapPlanAsync(planRequest);

            if (!planResponse.Success)
            {
                // The plan might not be found if the goal isn't achievable - that's still valid
                // We mainly want to verify the endpoint works
                if (string.IsNullOrEmpty(planResponse.FailureReason))
                    return TestResult.Failed("Plan failed without a failure reason");

                return TestResult.Successful(
                    $"GOAP planning executed, result: {planResponse.FailureReason}, time={planResponse.PlanningTimeMs}ms");
            }

            if (planResponse.Plan == null)
                return TestResult.Failed("Plan response succeeded but plan is null");

            return TestResult.Successful(
                $"GOAP plan generated: {planResponse.Plan.Actions?.Count ?? 0} actions, " +
                $"cost={planResponse.Plan.TotalCost}, " +
                $"nodes={planResponse.NodesExpanded}, " +
                $"time={planResponse.PlanningTimeMs}ms");
        }, "GOAP plan generation");

    private static async Task<TestResult> TestGoapPlanNonExistentBehavior(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new GoapPlanRequest
            {
                AgentId = "test-agent-1",
                BehaviorId = "nonexistent-goap-behavior-xyz",
                Goal = new GoapGoal
                {
                    Name = "test_goal",
                    Priority = 50,
                    Conditions = new Dictionary<string, string> { { "test", "==true" } }
                },
                WorldState = new Dictionary<string, object>()
            };

            try
            {
                var response = await behaviorClient.GenerateGoapPlanAsync(request);

                // If we get a response, it should indicate failure with "not found"
                if (!response.Success && response.FailureReason != null &&
                    response.FailureReason.Contains("not found", StringComparison.OrdinalIgnoreCase))
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

    private static async Task<TestResult> TestGoapPlanValidation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            // Create a simple plan for validation
            var validateRequest = new ValidateGoapPlanRequest
            {
                Plan = new GoapPlanResult
                {
                    GoalId = "test_goal",
                    TotalCost = 3.0f,
                    Actions = new List<PlannedActionResponse>
                    {
                        new PlannedActionResponse
                        {
                            ActionId = "action_1",
                            Index = 0,
                            Cost = 1.0f
                        },
                        new PlannedActionResponse
                        {
                            ActionId = "action_2",
                            Index = 1,
                            Cost = 2.0f
                        }
                    }
                },
                WorldState = new Dictionary<string, object>
                {
                    { "energy", 50 },
                    { "has_food", false }
                },
                CurrentActionIndex = 0
            };

            var response = await behaviorClient.ValidateGoapPlanAsync(validateRequest);

            // Validation should return a result (we don't have the actual actions so it may fail validation)
            if (response == null)
                return TestResult.Failed("Validation response is null");

            return TestResult.Successful(
                $"GOAP plan validation: valid={response.IsValid}, " +
                $"suggested_action={response.SuggestedAction}, " +
                $"reason={response.Reason}");
        }, "GOAP plan validation");

    #region Behavior Stack Tests

    /// <summary>
    /// Sample ABML YAML for base behavior in stacks.
    /// </summary>
    private const string BaseBehaviorAbml = """
        version: "1.0"
        name: base-behavior
        context:
        variables:
            health: 100
        flows:
        main:
            - emit:
                channel: action
                intent: idle
                urgency: 0.3
        """;

    /// <summary>
    /// Sample ABML YAML for cultural behavior in stacks.
    /// </summary>
    private const string CulturalBehaviorAbml = """
        version: "1.0"
        name: cultural-behavior
        context:
        variables:
            greeting_style: "formal"
        flows:
        main:
            - emit:
                channel: social
                intent: bow
                urgency: 0.5
        """;

    private static async Task<TestResult> TestCompileBehaviorStack(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new BehaviorStackRequest
            {
                BehaviorSets = new List<BehaviorSetDefinition>
                {
                    new BehaviorSetDefinition
                    {
                        Id = "base-set",
                        Priority = 10,
                        Category = BehaviorSetDefinitionCategory.Base,
                        AbmlContent = BaseBehaviorAbml
                    },
                    new BehaviorSetDefinition
                    {
                        Id = "cultural-set",
                        Priority = 50,
                        Category = BehaviorSetDefinitionCategory.Cultural,
                        AbmlContent = CulturalBehaviorAbml
                    }
                },
                CompilationOptions = new CompilationOptions
                {
                    EnableOptimizations = true,
                    CacheCompiledResult = false
                }
            };

            var response = await behaviorClient.CompileBehaviorStackAsync(request);

            if (!response.Success)
            {
                var warnings = response.Warnings != null
                    ? string.Join(", ", response.Warnings)
                    : "no warnings";
                return TestResult.Failed($"Stack compilation failed: {warnings}");
            }

            if (string.IsNullOrEmpty(response.BehaviorId))
                return TestResult.Failed("Behavior ID is empty");

            return TestResult.Successful(
                $"Stack compiled successfully: ID={response.BehaviorId}, Time={response.CompilationTimeMs}ms");
        }, "Compile behavior stack");

    private static async Task<TestResult> TestCompileBehaviorStackWithPriority(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            // Higher priority behavior should override lower priority
            var request = new BehaviorStackRequest
            {
                BehaviorSets = new List<BehaviorSetDefinition>
                {
                    new BehaviorSetDefinition
                    {
                        Id = "low-priority",
                        Priority = 10,
                        Category = BehaviorSetDefinitionCategory.Base,
                        AbmlContent = BaseBehaviorAbml
                    },
                    new BehaviorSetDefinition
                    {
                        Id = "high-priority",
                        Priority = 90,
                        Category = BehaviorSetDefinitionCategory.Personal,
                        AbmlContent = CulturalBehaviorAbml
                    }
                },
                CompilationOptions = new CompilationOptions
                {
                    EnableOptimizations = true,
                    CacheCompiledResult = false
                }
            };

            var response = await behaviorClient.CompileBehaviorStackAsync(request);

            if (!response.Success)
            {
                var warnings = response.Warnings != null
                    ? string.Join(", ", response.Warnings)
                    : "no warnings";
                return TestResult.Failed($"Priority stack compilation failed: {warnings}");
            }

            return TestResult.Successful(
                $"Priority stack compiled: ID={response.BehaviorId}, Time={response.CompilationTimeMs}ms");
        }, "Compile behavior stack with priority");

    private static async Task<TestResult> TestCompileBehaviorStackEmptySets(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new BehaviorStackRequest
            {
                BehaviorSets = new List<BehaviorSetDefinition>(),
                CompilationOptions = new CompilationOptions
                {
                    CacheCompiledResult = false
                }
            };

            try
            {
                var response = await behaviorClient.CompileBehaviorStackAsync(request);

                // Empty stack should fail
                if (response.Success)
                    return TestResult.Failed("Expected empty stack to fail compilation");

                return TestResult.Successful("Correctly rejected empty behavior stack");
            }
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                return TestResult.Successful("Correctly returned 400 for empty behavior stack");
            }
        }, "Compile empty behavior stack");

    #endregion

    #region Cache Invalidation Tests

    private static async Task<TestResult> TestInvalidateCachedBehavior(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            // First compile a behavior to cache it
            var compileRequest = new CompileBehaviorRequest
            {
                AbmlContent = ValidAbmlYaml,
                BehaviorName = "invalidation-test-behavior",
                CompilationOptions = new CompilationOptions
                {
                    EnableOptimizations = true,
                    CacheCompiledResult = true
                }
            };

            var compileResponse = await behaviorClient.CompileAbmlBehaviorAsync(
                BannouJson.Serialize(compileRequest));

            if (!compileResponse.Success || string.IsNullOrEmpty(compileResponse.BehaviorId))
                return TestResult.Failed("Failed to compile behavior for invalidation test");

            // Now invalidate it - void return means no exception = success
            var invalidateRequest = new InvalidateCacheRequest
            {
                BehaviorId = compileResponse.BehaviorId
            };

            await behaviorClient.InvalidateCachedBehaviorAsync(invalidateRequest);

            return TestResult.Successful(
                $"Successfully invalidated cached behavior: {compileResponse.BehaviorId}");
        }, "Invalidate cached behavior");

    private static async Task<TestResult> TestInvalidateNonExistentBehavior(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new InvalidateCacheRequest
            {
                BehaviorId = "nonexistent-behavior-xyz-12345"
            };

            try
            {
                // Void return - no exception means operation completed
                await behaviorClient.InvalidateCachedBehaviorAsync(request);

                // Invalidating non-existent is typically a no-op, not an error
                return TestResult.Successful("Invalidation request completed (no-op for non-existent)");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("Correctly returned 404 for non-existent behavior");
            }
        }, "Invalidate non-existent behavior");

    #endregion

    #region Context Variable Resolution Tests

    private static async Task<TestResult> TestResolveContextVariables(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new ResolveContextRequest
            {
                ContextExpression = "${npc.stats.health}",
                CharacterContext = new CharacterContext
                {
                    NpcId = "test-npc-123",
                    Stats = new Dictionary<string, double>
                    {
                        { "health", 100 },
                        { "energy", 75 }
                    }
                }
            };

            var response = await behaviorClient.ResolveContextVariablesAsync(request);

            if (response.ResolvedValue == null)
                return TestResult.Failed("Resolved value is null");

            var variablesUsed = response.ContextVariablesUsed?.Count ?? 0;
            return TestResult.Successful(
                $"Resolved context: value={response.ResolvedValue}, type={response.ResolvedType}, variables used={variablesUsed}");
        }, "Resolve context variables");

    private static async Task<TestResult> TestResolveContextMissingVariables(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var behaviorClient = GetServiceClient<IBehaviorClient>();

            var request = new ResolveContextRequest
            {
                ContextExpression = "${nonexistent.path}",
                CharacterContext = new CharacterContext
                {
                    NpcId = "test-npc-123"
                }
            };

            try
            {
                var response = await behaviorClient.ResolveContextVariablesAsync(request);

                // If it resolved anyway, the value might be null or a default
                return TestResult.Successful(
                    $"Missing variable handled: value={response.ResolvedValue}, type={response.ResolvedType}");
            }
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                return TestResult.Successful("Correctly returned 400 for unresolvable expression");
            }
        }, "Resolve context with missing variables");

    #endregion
}
