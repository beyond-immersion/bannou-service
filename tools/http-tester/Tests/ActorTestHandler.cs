using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for actor service API endpoints.
/// Tests template CRUD, actor lifecycle (spawn/stop), and pool node management.
/// </summary>
public class ActorTestHandler : BaseHttpTestHandler
{
    /// <summary>
    /// Get all actor service tests.
    /// </summary>
    public override ServiceTest[] GetServiceTests() =>
    [
        // Template CRUD
        new ServiceTest(TestCreateTemplate, "CreateTemplate", "Actor", "Create actor template"),
        new ServiceTest(TestGetTemplateById, "GetTemplateById", "Actor", "Get template by ID"),
        new ServiceTest(TestGetTemplateByCategory, "GetTemplateByCategory", "Actor", "Get template by category"),
        new ServiceTest(TestListTemplates, "ListTemplates", "Actor", "List all templates"),
        new ServiceTest(TestUpdateTemplate, "UpdateTemplate", "Actor", "Update template"),
        new ServiceTest(TestDeleteTemplate, "DeleteTemplate", "Actor", "Delete template"),

        // Template validation
        new ServiceTest(TestCreateTemplateMissingCategory, "MissingCategory", "Actor", "Create template fails with missing category"),
        new ServiceTest(TestCreateTemplateMissingBehaviorRef, "MissingBehaviorRef", "Actor", "Create template fails with missing behaviorRef"),
        new ServiceTest(TestGetTemplateNotFound, "TemplateNotFound", "Actor", "Get non-existent template returns 404"),

        // Actor lifecycle
        new ServiceTest(TestSpawnActor, "SpawnActor", "Actor", "Spawn actor from template"),
        new ServiceTest(TestGetActor, "GetActor", "Actor", "Get running actor"),
        new ServiceTest(TestListActors, "ListActors", "Actor", "List actors with filter"),
        new ServiceTest(TestStopActor, "StopActor", "Actor", "Stop running actor"),

        // Actor validation
        new ServiceTest(TestSpawnActorInvalidTemplate, "InvalidTemplate", "Actor", "Spawn with invalid template returns 404"),
        new ServiceTest(TestGetActorNotFound, "ActorNotFound", "Actor", "Get non-existent actor returns 404"),
        new ServiceTest(TestStopActorNotFound, "StopNotFound", "Actor", "Stop non-existent actor returns 404"),

        // Full lifecycle flow
        new ServiceTest(TestFullLifecycleFlow, "FullLifecycle", "Actor", "Complete create->spawn->stop->delete flow"),

        // Auto-spawn tests
        new ServiceTest(TestAutoSpawnOnGetActor, "AutoSpawn", "Actor", "GetActor auto-spawns actor when template matches"),

        // Perception injection tests
        new ServiceTest(TestInjectPerception, "InjectPerception", "Actor", "Inject perception into running actor"),
        new ServiceTest(TestInjectPerceptionActorNotFound, "InjectPerceptionNotFound", "Actor", "Inject perception to non-existent actor returns 404"),

        // Encounter management tests
        new ServiceTest(TestStartEncounter, "StartEncounter", "Actor", "Start encounter on actor"),
        new ServiceTest(TestGetEncounter, "GetEncounter", "Actor", "Get active encounter from actor"),
        new ServiceTest(TestUpdateEncounterPhase, "UpdatePhase", "Actor", "Update encounter phase"),
        new ServiceTest(TestEndEncounter, "EndEncounter", "Actor", "End active encounter"),
        new ServiceTest(TestEncounterFullFlow, "EncounterFlow", "Actor", "Full encounter lifecycle flow"),
        new ServiceTest(TestStartEncounterActorNotFound, "EncounterNotFound", "Actor", "Start encounter on non-existent actor returns 404"),
    ];

    #region Template CRUD Tests

    private static async Task<TestResult> TestCreateTemplate(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("test-category");

            var response = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/test-behavior",
                TickIntervalMs = 500,
                AutoSaveIntervalSeconds = 30
            });

            if (response.TemplateId == Guid.Empty)
                return TestResult.Failed("Template ID should not be empty");

            if (response.Category != category)
                return TestResult.Failed($"Category mismatch: expected {category}, got {response.Category}");

            // Cleanup
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest
            {
                TemplateId = response.TemplateId
            });

            return TestResult.Successful($"Created template {response.TemplateId}");
        }, "CreateTemplate");

    private static async Task<TestResult> TestGetTemplateById(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("get-by-id");

            // Create template
            var created = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/test"
            });

            // Get by ID
            var response = await actorClient.GetActorTemplateAsync(new GetActorTemplateRequest
            {
                TemplateId = created.TemplateId
            });

            if (response.TemplateId != created.TemplateId)
                return TestResult.Failed("Template ID mismatch");

            // Cleanup
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest
            {
                TemplateId = created.TemplateId
            });

            return TestResult.Successful($"Retrieved template by ID");
        }, "GetTemplateById");

    private static async Task<TestResult> TestGetTemplateByCategory(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("get-by-cat");

            // Create template
            var created = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/test"
            });

            // Get by category
            var response = await actorClient.GetActorTemplateAsync(new GetActorTemplateRequest
            {
                Category = category
            });

            if (response.Category != category)
                return TestResult.Failed($"Category mismatch: expected {category}, got {response.Category}");

            // Cleanup
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest
            {
                TemplateId = created.TemplateId
            });

            return TestResult.Successful($"Retrieved template by category");
        }, "GetTemplateByCategory");

    private static async Task<TestResult> TestListTemplates(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category1 = GenerateTestSlug("list-test-1");
            var category2 = GenerateTestSlug("list-test-2");

            // Create two templates
            var created1 = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category1,
                BehaviorRef = "asset://behaviors/test"
            });

            var created2 = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category2,
                BehaviorRef = "asset://behaviors/test"
            });

            // List templates
            var response = await actorClient.ListActorTemplatesAsync(new ListActorTemplatesRequest
            {
                Limit = 100
            });

            if (response.Templates == null || response.Templates.Count < 2)
                return TestResult.Failed("Expected at least 2 templates");

            // Cleanup
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest { TemplateId = created1.TemplateId });
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest { TemplateId = created2.TemplateId });

            return TestResult.Successful($"Listed {response.Templates.Count} templates");
        }, "ListTemplates");

    private static async Task<TestResult> TestUpdateTemplate(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("update-test");

            // Create template
            var created = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/original",
                TickIntervalMs = 1000
            });

            // Update template
            var updated = await actorClient.UpdateActorTemplateAsync(new UpdateActorTemplateRequest
            {
                TemplateId = created.TemplateId,
                BehaviorRef = "asset://behaviors/updated",
                TickIntervalMs = 500
            });

            if (updated.BehaviorRef != "asset://behaviors/updated")
                return TestResult.Failed($"BehaviorRef not updated: {updated.BehaviorRef}");

            if (updated.TickIntervalMs != 500)
                return TestResult.Failed($"TickIntervalMs not updated: {updated.TickIntervalMs}");

            // Cleanup
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest { TemplateId = created.TemplateId });

            return TestResult.Successful("Template updated successfully");
        }, "UpdateTemplate");

    private static async Task<TestResult> TestDeleteTemplate(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("delete-test");

            // Create template
            var created = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/test"
            });

            // Delete template
            var response = await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest
            {
                TemplateId = created.TemplateId
            });

            if (!response.Deleted)
                return TestResult.Failed("Template deletion returned false");

            return TestResult.Successful("Template deleted successfully");
        }, "DeleteTemplate");

    #endregion

    #region Template Validation Tests

    private static async Task<TestResult> TestCreateTemplateMissingCategory(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var actorClient = GetServiceClient<IActorClient>();
                await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
                {
                    Category = "", // Empty category
                    BehaviorRef = "asset://behaviors/test"
                });
            },
            400,
            "CreateTemplateMissingCategory");

    private static async Task<TestResult> TestCreateTemplateMissingBehaviorRef(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var actorClient = GetServiceClient<IActorClient>();
                await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
                {
                    Category = "test-category",
                    BehaviorRef = "" // Empty behavior ref
                });
            },
            400,
            "CreateTemplateMissingBehaviorRef");

    private static async Task<TestResult> TestGetTemplateNotFound(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var actorClient = GetServiceClient<IActorClient>();
                await actorClient.GetActorTemplateAsync(new GetActorTemplateRequest
                {
                    TemplateId = Guid.NewGuid() // Non-existent ID
                });
            },
            404,
            "GetTemplateNotFound");

    #endregion

    #region Actor Lifecycle Tests

    private static async Task<TestResult> TestSpawnActor(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("spawn-test");
            var actorId = GenerateTestSlug("actor");

            // Create template first
            var template = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/test",
                TickIntervalMs = 1000
            });

            // Spawn actor
            var response = await actorClient.SpawnActorAsync(new SpawnActorRequest
            {
                TemplateId = template.TemplateId,
                ActorId = actorId
            });

            if (response.ActorId != actorId)
                return TestResult.Failed($"Actor ID mismatch: expected {actorId}, got {response.ActorId}");

            if (response.Status != ActorStatus.Running && response.Status != ActorStatus.Starting)
                return TestResult.Failed($"Unexpected status: {response.Status}");

            // Cleanup
            await actorClient.StopActorAsync(new StopActorRequest { ActorId = actorId, Graceful = false });
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest { TemplateId = template.TemplateId });

            return TestResult.Successful($"Spawned actor {actorId}");
        }, "SpawnActor");

    private static async Task<TestResult> TestGetActor(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("get-actor-test");
            var actorId = GenerateTestSlug("get-actor");

            // Create template and spawn actor
            var template = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/test"
            });

            await actorClient.SpawnActorAsync(new SpawnActorRequest
            {
                TemplateId = template.TemplateId,
                ActorId = actorId
            });

            // Get actor
            var response = await actorClient.GetActorAsync(new GetActorRequest
            {
                ActorId = actorId
            });

            if (response.ActorId != actorId)
                return TestResult.Failed($"Actor ID mismatch");

            // Cleanup
            await actorClient.StopActorAsync(new StopActorRequest { ActorId = actorId });
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest { TemplateId = template.TemplateId });

            return TestResult.Successful($"Retrieved actor {actorId}");
        }, "GetActor");

    private static async Task<TestResult> TestListActors(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("list-actors-test");
            var actorId1 = GenerateTestSlug("list-actor-1");
            var actorId2 = GenerateTestSlug("list-actor-2");

            // Create template and spawn actors
            var template = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/test"
            });

            await actorClient.SpawnActorAsync(new SpawnActorRequest
            {
                TemplateId = template.TemplateId,
                ActorId = actorId1
            });

            await actorClient.SpawnActorAsync(new SpawnActorRequest
            {
                TemplateId = template.TemplateId,
                ActorId = actorId2
            });

            // List actors for this category
            var response = await actorClient.ListActorsAsync(new ListActorsRequest
            {
                Category = category
            });

            if (response.Actors == null || response.Actors.Count < 2)
                return TestResult.Failed($"Expected at least 2 actors, got {response.Actors?.Count ?? 0}");

            // Cleanup
            await actorClient.StopActorAsync(new StopActorRequest { ActorId = actorId1 });
            await actorClient.StopActorAsync(new StopActorRequest { ActorId = actorId2 });
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest { TemplateId = template.TemplateId });

            return TestResult.Successful($"Listed {response.Actors.Count} actors");
        }, "ListActors");

    private static async Task<TestResult> TestStopActor(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("stop-actor-test");
            var actorId = GenerateTestSlug("stop-actor");

            // Create template and spawn actor
            var template = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/test"
            });

            await actorClient.SpawnActorAsync(new SpawnActorRequest
            {
                TemplateId = template.TemplateId,
                ActorId = actorId
            });

            // Stop actor
            var response = await actorClient.StopActorAsync(new StopActorRequest
            {
                ActorId = actorId,
                Graceful = true
            });

            if (!response.Stopped)
                return TestResult.Failed("Stop returned false");

            if (response.FinalStatus != ActorStatus.Stopped)
                return TestResult.Failed($"Unexpected final status: {response.FinalStatus}");

            // Cleanup
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest { TemplateId = template.TemplateId });

            return TestResult.Successful($"Stopped actor {actorId}");
        }, "StopActor");

    #endregion

    #region Actor Validation Tests

    private static async Task<TestResult> TestSpawnActorInvalidTemplate(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var actorClient = GetServiceClient<IActorClient>();
                await actorClient.SpawnActorAsync(new SpawnActorRequest
                {
                    TemplateId = Guid.NewGuid(), // Non-existent template
                    ActorId = "test-actor"
                });
            },
            404,
            "SpawnActorInvalidTemplate");

    private static async Task<TestResult> TestGetActorNotFound(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var actorClient = GetServiceClient<IActorClient>();
                await actorClient.GetActorAsync(new GetActorRequest
                {
                    ActorId = "nonexistent-actor-12345"
                });
            },
            404,
            "GetActorNotFound");

    private static async Task<TestResult> TestStopActorNotFound(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var actorClient = GetServiceClient<IActorClient>();
                await actorClient.StopActorAsync(new StopActorRequest
                {
                    ActorId = "nonexistent-actor-12345"
                });
            },
            404,
            "StopActorNotFound");

    #endregion

    #region Full Lifecycle Tests

    private static async Task<TestResult> TestFullLifecycleFlow(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("full-lifecycle");
            var actorId = GenerateTestSlug("lifecycle-actor");

            // 1. Create template
            var template = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/test",
                TickIntervalMs = 100,
                AutoSaveIntervalSeconds = 0 // Disable auto-save for test
            });

            if (template.TemplateId == Guid.Empty)
                return TestResult.Failed("Failed to create template");

            // 2. Spawn actor
            var spawned = await actorClient.SpawnActorAsync(new SpawnActorRequest
            {
                TemplateId = template.TemplateId,
                ActorId = actorId
            });

            if (spawned.Status != ActorStatus.Running && spawned.Status != ActorStatus.Starting)
                return TestResult.Failed($"Actor not running: {spawned.Status}");

            // 3. Get actor to verify state
            var retrieved = await actorClient.GetActorAsync(new GetActorRequest
            {
                ActorId = actorId
            });

            if (retrieved.TemplateId != template.TemplateId)
                return TestResult.Failed("Template ID mismatch on retrieved actor");

            // 4. List actors to verify visibility
            var listed = await actorClient.ListActorsAsync(new ListActorsRequest
            {
                Category = category
            });

            if (listed.Actors == null || !listed.Actors.Any(a => a.ActorId == actorId))
                return TestResult.Failed("Actor not found in list");

            // 5. Stop actor gracefully
            var stopped = await actorClient.StopActorAsync(new StopActorRequest
            {
                ActorId = actorId,
                Graceful = true
            });

            if (!stopped.Stopped)
                return TestResult.Failed("Failed to stop actor");

            // 6. Delete template
            var deleted = await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest
            {
                TemplateId = template.TemplateId
            });

            if (!deleted.Deleted)
                return TestResult.Failed("Failed to delete template");

            return TestResult.Successful("Full lifecycle: create template -> spawn -> get -> list -> stop -> delete");
        }, "FullLifecycleFlow");

    #endregion

    #region Auto-Spawn Tests

    private static async Task<TestResult> TestAutoSpawnOnGetActor(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("autospawn-test");
            var actorIdPattern = $"autospawn-{category}-.*";
            var actorId = $"autospawn-{category}-actor-1";

            // Create template with auto-spawn enabled
            var template = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/test",
                TickIntervalMs = 1000,
                AutoSpawn = new AutoSpawnConfig
                {
                    Enabled = true,
                    IdPattern = actorIdPattern,
                    MaxInstances = 10
                }
            });

            if (template.AutoSpawn == null || !template.AutoSpawn.Enabled)
                return TestResult.Failed("AutoSpawn config not saved on template");

            // Get actor that doesn't exist yet - should trigger auto-spawn
            var response = await actorClient.GetActorAsync(new GetActorRequest
            {
                ActorId = actorId
            });

            if (response.ActorId != actorId)
                return TestResult.Failed($"Actor ID mismatch: expected {actorId}, got {response.ActorId}");

            if (response.Status != ActorStatus.Running && response.Status != ActorStatus.Starting && response.Status != ActorStatus.Pending)
                return TestResult.Failed($"Auto-spawned actor not running: {response.Status}");

            // Cleanup
            await actorClient.StopActorAsync(new StopActorRequest { ActorId = actorId, Graceful = false });
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest { TemplateId = template.TemplateId });

            return TestResult.Successful($"Auto-spawned actor {actorId} via GetActor");
        }, "AutoSpawnOnGetActor");

    #endregion

    #region Perception Injection Tests

    private static async Task<TestResult> TestInjectPerception(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("inject-test");
            var actorId = GenerateTestSlug("inject-actor");

            // Create template and spawn actor
            var template = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/test",
                TickIntervalMs = 1000
            });

            await actorClient.SpawnActorAsync(new SpawnActorRequest
            {
                TemplateId = template.TemplateId,
                ActorId = actorId
            });

            // Give the actor a moment to start
            await Task.Delay(100);

            // Inject a perception
            var response = await actorClient.InjectPerceptionAsync(new InjectPerceptionRequest
            {
                ActorId = actorId,
                Perception = new PerceptionData
                {
                    PerceptionType = "test-perception",
                    SourceId = "http-tester",
                    SourceType = PerceptionSourceType.Environment,
                    Urgency = 0.5f,
                    Data = new { message = "Hello from HTTP tester" }
                }
            });

            if (!response.Queued)
                return TestResult.Failed("Perception not queued");

            // Cleanup
            await actorClient.StopActorAsync(new StopActorRequest { ActorId = actorId, Graceful = false });
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest { TemplateId = template.TemplateId });

            return TestResult.Successful($"Injected perception into actor {actorId} (queue depth: {response.QueueDepth})");
        }, "InjectPerception");

    private static async Task<TestResult> TestInjectPerceptionActorNotFound(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var actorClient = GetServiceClient<IActorClient>();
                await actorClient.InjectPerceptionAsync(new InjectPerceptionRequest
                {
                    ActorId = "nonexistent-actor-12345",
                    Perception = new PerceptionData
                    {
                        PerceptionType = "test",
                        SourceId = "test",
                        SourceType = PerceptionSourceType.Environment
                    }
                });
            },
            404,
            "InjectPerceptionActorNotFound");

    #endregion

    #region Encounter Management Tests

    private static async Task<TestResult> TestStartEncounter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("encounter-start");
            var actorId = GenerateTestSlug("event-brain");

            // Create template and spawn actor
            var template = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/event-brain",
                TickIntervalMs = 1000
            });

            await actorClient.SpawnActorAsync(new SpawnActorRequest
            {
                TemplateId = template.TemplateId,
                ActorId = actorId
            });

            // Give the actor a moment to start
            await Task.Delay(100);

            // Start encounter (void return - success = no exception)
            var participantId = Guid.NewGuid();
            var encounterId = Guid.NewGuid();
            await actorClient.StartEncounterAsync(new StartEncounterRequest
            {
                ActorId = actorId,
                EncounterId = encounterId,
                EncounterType = "combat",
                Participants = new List<Guid> { participantId }
            });

            // Cleanup
            await actorClient.EndEncounterAsync(new EndEncounterRequest { ActorId = actorId });
            await actorClient.StopActorAsync(new StopActorRequest { ActorId = actorId, Graceful = false });
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest { TemplateId = template.TemplateId });

            return TestResult.Successful($"Started encounter {encounterId} on actor {actorId}");
        }, "StartEncounter");

    private static async Task<TestResult> TestGetEncounter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("encounter-get");
            var actorId = GenerateTestSlug("event-brain");

            // Create template and spawn actor
            var template = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/event-brain",
                TickIntervalMs = 1000
            });

            await actorClient.SpawnActorAsync(new SpawnActorRequest
            {
                TemplateId = template.TemplateId,
                ActorId = actorId
            });

            await Task.Delay(100);

            // Start encounter
            var participantId = Guid.NewGuid();
            var encounterId = Guid.NewGuid();
            await actorClient.StartEncounterAsync(new StartEncounterRequest
            {
                ActorId = actorId,
                EncounterId = encounterId,
                EncounterType = "dialogue",
                Participants = new List<Guid> { participantId }
            });

            // Get encounter
            var response = await actorClient.GetEncounterAsync(new GetEncounterRequest
            {
                ActorId = actorId
            });

            if (!response.HasActiveEncounter)
                return TestResult.Failed("Expected active encounter but none found");

            if (response.Encounter == null)
                return TestResult.Failed("Encounter object is null");

            if (response.Encounter.EncounterId != encounterId)
                return TestResult.Failed($"Encounter ID mismatch: expected {encounterId}, got {response.Encounter.EncounterId}");

            if (response.Encounter.EncounterType != "dialogue")
                return TestResult.Failed($"Encounter type mismatch: expected dialogue, got {response.Encounter.EncounterType}");

            // Cleanup
            await actorClient.EndEncounterAsync(new EndEncounterRequest { ActorId = actorId });
            await actorClient.StopActorAsync(new StopActorRequest { ActorId = actorId, Graceful = false });
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest { TemplateId = template.TemplateId });

            return TestResult.Successful($"Retrieved encounter {encounterId} from actor {actorId}");
        }, "GetEncounter");

    private static async Task<TestResult> TestUpdateEncounterPhase(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("encounter-phase");
            var actorId = GenerateTestSlug("event-brain");

            // Create template and spawn actor
            var template = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/event-brain",
                TickIntervalMs = 1000
            });

            await actorClient.SpawnActorAsync(new SpawnActorRequest
            {
                TemplateId = template.TemplateId,
                ActorId = actorId
            });

            await Task.Delay(100);

            // Start encounter
            var encounterId = Guid.NewGuid();
            await actorClient.StartEncounterAsync(new StartEncounterRequest
            {
                ActorId = actorId,
                EncounterId = encounterId,
                EncounterType = "combat",
                Participants = new List<Guid> { Guid.NewGuid() }
            });

            // Update phase (success = no exception)
            var response = await actorClient.UpdateEncounterPhaseAsync(new UpdateEncounterPhaseRequest
            {
                ActorId = actorId,
                Phase = "executing"
            });

            if (response.CurrentPhase != "executing")
                return TestResult.Failed($"Phase not updated: expected executing, got {response.CurrentPhase}");

            // Cleanup
            await actorClient.EndEncounterAsync(new EndEncounterRequest { ActorId = actorId });
            await actorClient.StopActorAsync(new StopActorRequest { ActorId = actorId, Graceful = false });
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest { TemplateId = template.TemplateId });

            return TestResult.Successful($"Updated encounter phase to executing");
        }, "UpdateEncounterPhase");

    private static async Task<TestResult> TestEndEncounter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("encounter-end");
            var actorId = GenerateTestSlug("event-brain");

            // Create template and spawn actor
            var template = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/event-brain",
                TickIntervalMs = 1000
            });

            await actorClient.SpawnActorAsync(new SpawnActorRequest
            {
                TemplateId = template.TemplateId,
                ActorId = actorId
            });

            await Task.Delay(100);

            // Start encounter
            var encounterId = Guid.NewGuid();
            await actorClient.StartEncounterAsync(new StartEncounterRequest
            {
                ActorId = actorId,
                EncounterId = encounterId,
                EncounterType = "combat",
                Participants = new List<Guid> { Guid.NewGuid() }
            });

            // End encounter (success = no exception)
            var response = await actorClient.EndEncounterAsync(new EndEncounterRequest
            {
                ActorId = actorId
            });

            if (response.EncounterId != encounterId)
                return TestResult.Failed($"Encounter ID mismatch: expected {encounterId}, got {response.EncounterId}");

            if (response.DurationMs == null || response.DurationMs < 0)
                return TestResult.Failed($"Invalid duration: {response.DurationMs}");

            // Verify no active encounter
            var getResponse = await actorClient.GetEncounterAsync(new GetEncounterRequest { ActorId = actorId });
            if (getResponse.HasActiveEncounter)
                return TestResult.Failed("Encounter still active after ending");

            // Cleanup
            await actorClient.StopActorAsync(new StopActorRequest { ActorId = actorId, Graceful = false });
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest { TemplateId = template.TemplateId });

            return TestResult.Successful($"Ended encounter {encounterId} (duration: {response.DurationMs}ms)");
        }, "EndEncounter");

    private static async Task<TestResult> TestEncounterFullFlow(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var actorClient = GetServiceClient<IActorClient>();
            var category = GenerateTestSlug("encounter-flow");
            var actorId = GenerateTestSlug("event-brain");

            // 1. Create template and spawn actor
            var template = await actorClient.CreateActorTemplateAsync(new CreateActorTemplateRequest
            {
                Category = category,
                BehaviorRef = "asset://behaviors/event-brain",
                TickIntervalMs = 100
            });

            await actorClient.SpawnActorAsync(new SpawnActorRequest
            {
                TemplateId = template.TemplateId,
                ActorId = actorId
            });

            await Task.Delay(100);

            // 2. Verify no active encounter initially
            var initial = await actorClient.GetEncounterAsync(new GetEncounterRequest { ActorId = actorId });
            if (initial.HasActiveEncounter)
                return TestResult.Failed("Unexpected active encounter on fresh actor");

            // 3. Start encounter (void return - throws on failure)
            var participantId = Guid.NewGuid();
            var encounterId = Guid.NewGuid();
            await actorClient.StartEncounterAsync(new StartEncounterRequest
            {
                ActorId = actorId,
                EncounterId = encounterId,
                EncounterType = "combat",
                Participants = new List<Guid> { participantId }
            });

            // 4. Verify encounter started
            var afterStart = await actorClient.GetEncounterAsync(new GetEncounterRequest { ActorId = actorId });
            if (!afterStart.HasActiveEncounter)
                return TestResult.Failed("No active encounter after starting");

            // 5. Update phase: initializing -> executing
            var phase1 = await actorClient.UpdateEncounterPhaseAsync(new UpdateEncounterPhaseRequest
            {
                ActorId = actorId,
                Phase = "executing"
            });
            if (phase1.CurrentPhase != "executing")
                return TestResult.Failed("Phase update to executing failed");

            // 6. Update phase: executing -> complete
            var phase2 = await actorClient.UpdateEncounterPhaseAsync(new UpdateEncounterPhaseRequest
            {
                ActorId = actorId,
                Phase = "complete"
            });
            if (phase2.CurrentPhase != "complete")
                return TestResult.Failed("Phase update to complete failed");

            // 7. End encounter (returns response with encounter details)
            await actorClient.EndEncounterAsync(new EndEncounterRequest { ActorId = actorId });

            // 8. Verify encounter ended
            var afterEnd = await actorClient.GetEncounterAsync(new GetEncounterRequest { ActorId = actorId });
            if (afterEnd.HasActiveEncounter)
                return TestResult.Failed("Encounter still active after ending");

            // Cleanup
            await actorClient.StopActorAsync(new StopActorRequest { ActorId = actorId, Graceful = false });
            await actorClient.DeleteActorTemplateAsync(new DeleteActorTemplateRequest { TemplateId = template.TemplateId });

            return TestResult.Successful($"Full encounter flow: start -> get -> phase updates -> end -> verify");
        }, "EncounterFullFlow");

    private static async Task<TestResult> TestStartEncounterActorNotFound(ITestClient client, string[] args) =>
        await
        ExecuteExpectingStatusAsync(
            async () =>
            {
                var actorClient = GetServiceClient<IActorClient>();
                await actorClient.StartEncounterAsync(new StartEncounterRequest
                {
                    ActorId = "nonexistent-actor-12345",
                    EncounterId = Guid.NewGuid(),
                    EncounterType = "combat",
                    Participants = new List<Guid> { Guid.NewGuid() }
                });
            },
            404,
            "StartEncounterActorNotFound");

    #endregion
}
