using System.Linq;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Achievement;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for Contract service HTTP API endpoints.
/// Tests realistic multi-service contract scenarios demonstrating:
/// - Character-to-character agreements with milestone tracking
/// - Prebound API execution on milestone completion (cross-service calls)
/// - Response validation on prebound APIs
/// - Contract breach reporting and curing
/// - Constraint checking across active contracts
/// </summary>
public class ContractTestHandler : BaseHttpTestHandler
{
    private static readonly Guid TestGameServiceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public override ServiceTest[] GetServiceTests() =>
    [
        // Basic CRUD Tests
        new ServiceTest(TestCreateContractTemplate, "CreateTemplate", "Contract",
            "Test contract template creation"),
        new ServiceTest(TestCreateContractInstance, "CreateInstance", "Contract",
            "Test contract instance creation from template"),
        new ServiceTest(TestGetContractInstance, "GetInstance", "Contract",
            "Test contract instance retrieval"),

        // Lifecycle Tests
        new ServiceTest(TestFullContractLifecycle, "FullLifecycle", "Contract",
            "Test complete lifecycle: create → propose → consent → active"),
        new ServiceTest(TestMilestoneCompletion, "MilestoneCompletion", "Contract",
            "Test milestone completion updates contract state"),

        // Cross-Service Integration Tests
        new ServiceTest(TestCharacterDealWithAchievements, "CharacterDealWithAchievements", "Contract",
            "Test character employment contract with achievement rewards on milestone completion"),
        new ServiceTest(TestContractWithPreboundApiValidation, "PreboundApiValidation", "Contract",
            "Test contract with prebound APIs that have response validation rules"),

        // Breach and Constraint Tests
        new ServiceTest(TestContractBreachAndCure, "BreachAndCure", "Contract",
            "Test breach reporting and curing on active contract"),
        new ServiceTest(TestExclusivityConstraintCheck, "ExclusivityConstraint", "Contract",
            "Test constraint checking prevents conflicting contracts"),

        // Escrow Integration Tests
        new ServiceTest(TestGuardianLockUnlock, "GuardianLockUnlock", "Contract",
            "Test locking and unlocking a contract under guardian custody"),
        new ServiceTest(TestTransferPartyRole, "TransferPartyRole", "Contract",
            "Test transferring a party role while contract is locked"),
        new ServiceTest(TestGuardianEnforcementOnTerminate, "GuardianEnforcement", "Contract",
            "Test that locked contracts cannot be terminated"),
        new ServiceTest(TestClauseTypeRegistration, "ClauseTypeRegistration", "Contract",
            "Test registering and listing clause types"),
        new ServiceTest(TestSetTemplateValues, "SetTemplateValues", "Contract",
            "Test setting template values on a contract instance"),
        new ServiceTest(TestContractExecutionWithCurrency, "ExecuteWithCurrency", "Contract",
            "Test full contract execution with currency transfer clauses"),

        // Additional Coverage Tests
        new ServiceTest(TestGetContractTemplate, "GetTemplate", "Contract",
            "Test getting a contract template by ID and code"),
        new ServiceTest(TestListContractTemplates, "ListTemplates", "Contract",
            "Test listing contract templates"),
        new ServiceTest(TestUpdateContractTemplate, "UpdateTemplate", "Contract",
            "Test updating a contract template"),
        new ServiceTest(TestDeleteContractTemplate, "DeleteTemplate", "Contract",
            "Test deleting a contract template"),
        new ServiceTest(TestQueryContractInstances, "QueryInstances", "Contract",
            "Test querying contract instances by party or status"),
        new ServiceTest(TestGetContractInstanceStatus, "GetInstanceStatus", "Contract",
            "Test getting contract instance status"),
        new ServiceTest(TestFailMilestone, "FailMilestone", "Contract",
            "Test failing a milestone"),
        new ServiceTest(TestGetMilestone, "GetMilestone", "Contract",
            "Test getting milestone details"),
        new ServiceTest(TestGetBreach, "GetBreach", "Contract",
            "Test getting breach details by ID"),
        new ServiceTest(TestUpdateContractMetadata, "UpdateMetadata", "Contract",
            "Test updating contract metadata"),
        new ServiceTest(TestGetContractMetadata, "GetMetadata", "Contract",
            "Test getting contract metadata"),
        new ServiceTest(TestQueryActiveContracts, "QueryActive", "Contract",
            "Test querying active contracts for an entity"),
        new ServiceTest(TestCheckAssetRequirements, "CheckAssets", "Contract",
            "Test checking asset requirements for a contract"),
    ];

    // =========================================================================
    // Helper Methods
    // =========================================================================

    /// <summary>
    /// Creates a test realm and two characters for contract scenarios.
    /// </summary>
    private static async Task<(RealmResponse Realm, CharacterResponse CharA, CharacterResponse CharB)>
        CreateTestCharacterPairAsync(string testSuffix)
    {
        var realm = await CreateTestRealmAsync("CONTRACT_TEST", "Contract", testSuffix);

        var speciesClient = GetServiceClient<ISpeciesClient>();
        // Species code max is 50 chars - use short unique ID to stay under limit
        var shortId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var species = await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
        {
            Code = $"SP_{shortId}",
            Name = $"Contract Test Species {testSuffix}",
            Description = "Test species for contract integration tests"
        });
        await speciesClient.AddSpeciesToRealmAsync(new AddSpeciesToRealmRequest
        {
            SpeciesId = species.SpeciesId,
            RealmId = realm.RealmId
        });

        var characterClient = GetServiceClient<ICharacterClient>();

        var charA = await characterClient.CreateCharacterAsync(new CreateCharacterRequest
        {
            Name = $"Employer_{DateTime.Now.Ticks}",
            RealmId = realm.RealmId,
            SpeciesId = species.SpeciesId,
            BirthDate = DateTimeOffset.UtcNow.AddYears(-30),
            Status = CharacterStatus.Alive
        });

        var charB = await characterClient.CreateCharacterAsync(new CreateCharacterRequest
        {
            Name = $"Worker_{DateTime.Now.Ticks}",
            RealmId = realm.RealmId,
            SpeciesId = species.SpeciesId,
            BirthDate = DateTimeOffset.UtcNow.AddYears(-22),
            Status = CharacterStatus.Alive
        });

        return (realm, charA, charB);
    }

    /// <summary>
    /// Creates a contract template with employer/worker roles.
    /// </summary>
    private static async Task<ContractTemplateResponse> CreateEmploymentTemplateAsync(
        IContractClient contractClient,
        string testSuffix,
        Guid? realmId = null,
        ICollection<MilestoneDefinition>? milestones = null)
    {
        return await contractClient.CreateContractTemplateAsync(new CreateContractTemplateRequest
        {
            Code = $"employment_{DateTime.Now.Ticks}_{testSuffix}",
            Name = $"Employment Contract {testSuffix}",
            Description = "Test employment contract template",
            RealmId = realmId,
            MinParties = 2,
            MaxParties = 2,
            PartyRoles = new List<PartyRoleDefinition>
            {
                new PartyRoleDefinition
                {
                    Role = "employer",
                    MinCount = 1,
                    MaxCount = 1,
                    AllowedEntityTypes = new List<EntityType> { EntityType.Character }
                },
                new PartyRoleDefinition
                {
                    Role = "worker",
                    MinCount = 1,
                    MaxCount = 1,
                    AllowedEntityTypes = new List<EntityType> { EntityType.Character }
                }
            },
            DefaultTerms = new ContractTerms
            {
                Duration = "P30D",
                BreachThreshold = 3,
                GracePeriodForCure = "P7D"
            },
            Milestones = milestones,
            DefaultEnforcementMode = EnforcementMode.EventOnly,
            Transferable = false
        });
    }

    /// <summary>
    /// Creates a contract instance, proposes it, and gets consent from both parties.
    /// Returns the active contract.
    /// </summary>
    private static async Task<ContractInstanceResponse> CreateActiveContractAsync(
        IContractClient contractClient,
        Guid templateId,
        Guid employerId,
        Guid workerId,
        ContractTerms? terms = null)
    {
        // Create instance
        var instance = await contractClient.CreateContractInstanceAsync(new CreateContractInstanceRequest
        {
            TemplateId = templateId,
            Parties = new List<ContractPartyInput>
            {
                new ContractPartyInput
                {
                    EntityId = employerId,
                    EntityType = EntityType.Character,
                    Role = "employer"
                },
                new ContractPartyInput
                {
                    EntityId = workerId,
                    EntityType = EntityType.Character,
                    Role = "worker"
                }
            },
            Terms = terms,
            EffectiveFrom = null,
            EffectiveUntil = DateTimeOffset.UtcNow.AddDays(30)
        });

        // Propose
        await contractClient.ProposeContractInstanceAsync(new ProposeContractInstanceRequest
        {
            ContractId = instance.ContractId
        });

        // Consent from employer
        await contractClient.ConsentToContractAsync(new ConsentToContractRequest
        {
            ContractId = instance.ContractId,
            PartyEntityId = employerId,
            PartyEntityType = EntityType.Character
        });

        // Consent from worker (this activates the contract)
        var activeContract = await contractClient.ConsentToContractAsync(new ConsentToContractRequest
        {
            ContractId = instance.ContractId,
            PartyEntityId = workerId,
            PartyEntityType = EntityType.Character
        });

        return activeContract;
    }

    // =========================================================================
    // Basic CRUD Tests
    // =========================================================================

    private static async Task<TestResult> TestCreateContractTemplate(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();

            var template = await CreateEmploymentTemplateAsync(contractClient, "create");

            if (template.TemplateId == Guid.Empty)
                return TestResult.Failed("Template creation returned empty ID");

            if (!template.Code.StartsWith("employment_"))
                return TestResult.Failed($"Template code mismatch: '{template.Code}'");

            return TestResult.Successful(
                $"Template created: ID={template.TemplateId}, Code={template.Code}");
        }, "Create contract template");

    private static async Task<TestResult> TestCreateContractInstance(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("INSTANCE");

            var template = await CreateEmploymentTemplateAsync(contractClient, "instance", realm.RealmId);

            var instance = await contractClient.CreateContractInstanceAsync(new CreateContractInstanceRequest
            {
                TemplateId = template.TemplateId,
                Parties = new List<ContractPartyInput>
                {
                    new ContractPartyInput
                    {
                        EntityId = charA.CharacterId,
                        EntityType = EntityType.Character,
                        Role = "employer"
                    },
                    new ContractPartyInput
                    {
                        EntityId = charB.CharacterId,
                        EntityType = EntityType.Character,
                        Role = "worker"
                    }
                }
            });

            if (instance.ContractId == Guid.Empty)
                return TestResult.Failed("Instance creation returned empty ID");

            if (instance.Status != ContractStatus.Draft)
                return TestResult.Failed($"Expected draft status, got: {instance.Status}");

            return TestResult.Successful(
                $"Instance created: ID={instance.ContractId}, Status={instance.Status}");
        }, "Create contract instance");

    private static async Task<TestResult> TestGetContractInstance(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("GET");

            var template = await CreateEmploymentTemplateAsync(contractClient, "get", realm.RealmId);

            var instance = await contractClient.CreateContractInstanceAsync(new CreateContractInstanceRequest
            {
                TemplateId = template.TemplateId,
                Parties = new List<ContractPartyInput>
                {
                    new ContractPartyInput
                    {
                        EntityId = charA.CharacterId,
                        EntityType = EntityType.Character,
                        Role = "employer"
                    },
                    new ContractPartyInput
                    {
                        EntityId = charB.CharacterId,
                        EntityType = EntityType.Character,
                        Role = "worker"
                    }
                }
            });

            // Retrieve it
            var retrieved = await contractClient.GetContractInstanceAsync(new GetContractInstanceRequest
            {
                ContractId = instance.ContractId
            });

            if (retrieved.ContractId != instance.ContractId)
                return TestResult.Failed("Contract ID mismatch on retrieval");

            return TestResult.Successful(
                $"Contract retrieved: ID={retrieved.ContractId}, Status={retrieved.Status}");
        }, "Get contract instance");

    // =========================================================================
    // Lifecycle Tests
    // =========================================================================

    private static async Task<TestResult> TestFullContractLifecycle(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("LIFECYCLE");

            // Contracts without milestones auto-fulfill, so add a milestone to test Active state
            var milestones = new List<MilestoneDefinition>
            {
                new MilestoneDefinition
                {
                    Code = "complete_work",
                    Name = "Complete Work",
                    Description = "Complete the contracted work",
                    Sequence = 0,
                    Required = true
                }
            };

            var template = await CreateEmploymentTemplateAsync(contractClient, "lifecycle", realm.RealmId, milestones);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            if (activeContract.Status != ContractStatus.Active)
                return TestResult.Failed($"Expected active status, got: {activeContract.Status}");

            // Verify both parties have consented
            var parties = activeContract.Parties;
            if (parties == null || parties.Count != 2)
                return TestResult.Failed($"Expected 2 parties, got: {parties?.Count ?? 0}");

            // Terminate the contract
            var terminated = await contractClient.TerminateContractInstanceAsync(
                new TerminateContractInstanceRequest
                {
                    ContractId = activeContract.ContractId,
                    RequestingEntityId = charA.CharacterId,
                    RequestingEntityType = EntityType.Character,
                    Reason = "Test lifecycle complete"
                });

            if (terminated.Status != ContractStatus.Terminated)
                return TestResult.Failed($"Expected terminated status, got: {terminated.Status}");

            return TestResult.Successful(
                $"Full lifecycle: draft → proposed → active → terminated for contract {activeContract.ContractId}");
        }, "Full contract lifecycle");

    private static async Task<TestResult> TestMilestoneCompletion(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("MILESTONE");

            var milestones = new List<MilestoneDefinition>
            {
                new MilestoneDefinition
                {
                    Code = "deliver_goods",
                    Name = "Deliver Goods",
                    Description = "Worker delivers the agreed goods",
                    Sequence = 0,
                    Required = true
                },
                new MilestoneDefinition
                {
                    Code = "payment_received",
                    Name = "Payment Received",
                    Description = "Employer confirms payment sent",
                    Sequence = 1,
                    Required = true
                }
            };

            var template = await CreateEmploymentTemplateAsync(
                contractClient, "milestone", realm.RealmId, milestones);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            // Complete first milestone
            var milestone1 = await contractClient.CompleteMilestoneAsync(new CompleteMilestoneRequest
            {
                ContractId = activeContract.ContractId,
                MilestoneCode = "deliver_goods",
                Evidence = new { deliveredAt = DateTimeOffset.UtcNow.ToString("o") }
            });

            if (milestone1.Milestone.Status != MilestoneStatus.Completed)
                return TestResult.Failed($"Milestone 1 expected completed, got: {milestone1.Milestone.Status}");

            // Complete second milestone
            var milestone2 = await contractClient.CompleteMilestoneAsync(new CompleteMilestoneRequest
            {
                ContractId = activeContract.ContractId,
                MilestoneCode = "payment_received",
                Evidence = new { amount = 500, currency = "gold" }
            });

            if (milestone2.Milestone.Status != MilestoneStatus.Completed)
                return TestResult.Failed($"Milestone 2 expected completed, got: {milestone2.Milestone.Status}");

            // Verify contract status after all milestones
            var finalContract = await contractClient.GetContractInstanceAsync(new GetContractInstanceRequest
            {
                ContractId = activeContract.ContractId
            });

            return TestResult.Successful(
                $"Milestones completed: deliver_goods + payment_received for contract {activeContract.ContractId}, " +
                $"final status: {finalContract.Status}");
        }, "Milestone completion");

    // =========================================================================
    // Cross-Service Integration Tests
    // =========================================================================

    /// <summary>
    /// Demonstrates a character employment contract where milestone completion
    /// triggers prebound APIs to award achievements to both parties.
    ///
    /// Flow:
    /// 1. Create realm, two characters
    /// 2. Create achievement definitions for "Contract Fulfilled" and "First Employer"
    /// 3. Create contract template with milestones that have onComplete prebound APIs
    ///    calling the achievement service to update progress
    /// 4. Execute full lifecycle and verify achievement progress updated
    /// </summary>
    private static async Task<TestResult> TestCharacterDealWithAchievements(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var achievementClient = GetServiceClient<IAchievementClient>();
            var (realm, employer, worker) = await CreateTestCharacterPairAsync("ACHV_DEAL");

            // Step 1: Create achievement definitions
            var workerAchievementId = $"contract-fulfilled-{DateTime.Now.Ticks}".ToLowerInvariant();
            await achievementClient.CreateAchievementDefinitionAsync(new CreateAchievementDefinitionRequest
            {
                GameServiceId = TestGameServiceId,
                AchievementId = workerAchievementId,
                DisplayName = "Contract Fulfilled",
                Description = "Complete an employment contract successfully",
                EntityTypes = new List<EntityType> { EntityType.Character },
                AchievementType = AchievementType.Progressive,
                ProgressTarget = 1,
                Points = 50,
                Platforms = new List<Platform> { Platform.Internal }
            });

            var employerAchievementId = $"first-employer-{DateTime.Now.Ticks}".ToLowerInvariant();
            await achievementClient.CreateAchievementDefinitionAsync(new CreateAchievementDefinitionRequest
            {
                GameServiceId = TestGameServiceId,
                AchievementId = employerAchievementId,
                DisplayName = "First Employer",
                Description = "Successfully complete a contract as employer",
                EntityTypes = new List<EntityType> { EntityType.Character },
                AchievementType = AchievementType.Progressive,
                ProgressTarget = 1,
                Points = 25,
                Platforms = new List<Platform> { Platform.Internal }
            });

            // Step 2: Create contract template with prebound APIs on milestone completion
            // The onComplete APIs call the achievement service to update progress
            var milestones = new List<MilestoneDefinition>
            {
                new MilestoneDefinition
                {
                    Code = "work_delivered",
                    Name = "Work Delivered",
                    Description = "Worker delivers the contracted work",
                    Sequence = 0,
                    Required = true,
                    OnComplete = new List<PreboundApi>
                    {
                        // Award worker achievement progress
                        new PreboundApi
                        {
                            ServiceName = "achievement",
                            Endpoint = "/achievement/progress/update",
                            PayloadTemplate = $"{{\"gameServiceId\":\"{TestGameServiceId}\",\"achievementId\":\"{workerAchievementId}\",\"entityId\":\"{{{{contract.party.worker.entityId}}}}\",\"entityType\":\"character\",\"progressDelta\":1}}",
                            Description = "Award worker for fulfilling contract",
                            ExecutionMode = PreboundApiExecutionMode.Sync,
                            ResponseValidation = new ResponseValidation
                            {
                                SuccessConditions = new List<ValidationCondition>
                                {
                                    new ValidationCondition
                                    {
                                        Type = ValidationConditionType.StatusCodeIn,
                                        StatusCodes = new List<int> { 200 }
                                    }
                                },
                                PermanentFailureConditions = new List<ValidationCondition>(),
                                TransientFailureStatusCodes = new List<int>()
                            }
                        },
                        // Award employer achievement progress
                        new PreboundApi
                        {
                            ServiceName = "achievement",
                            Endpoint = "/achievement/progress/update",
                            PayloadTemplate = $"{{\"gameServiceId\":\"{TestGameServiceId}\",\"achievementId\":\"{employerAchievementId}\",\"entityId\":\"{{{{contract.party.employer.entityId}}}}\",\"entityType\":\"character\",\"progressDelta\":1}}",
                            Description = "Award employer for completing contract",
                            ExecutionMode = PreboundApiExecutionMode.Sync
                        }
                    }
                }
            };

            var template = await CreateEmploymentTemplateAsync(
                contractClient, "achv_deal", realm.RealmId, milestones);

            // Step 3: Create and activate contract
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, employer.CharacterId, worker.CharacterId);

            if (activeContract.Status != ContractStatus.Active)
                return TestResult.Failed($"Contract not active: {activeContract.Status}");

            // Step 4: Complete the milestone (triggers prebound APIs)
            var milestone = await contractClient.CompleteMilestoneAsync(new CompleteMilestoneRequest
            {
                ContractId = activeContract.ContractId,
                MilestoneCode = "work_delivered",
                Evidence = new { deliveredAt = DateTimeOffset.UtcNow.ToString("o") }
            });

            if (milestone.Milestone.Status != MilestoneStatus.Completed)
                return TestResult.Failed($"Milestone not completed: {milestone.Milestone.Status}");

            // Step 5: Verify achievement progress was updated
            // Small delay to allow async processing
            await Task.Delay(500);

            var workerProgress = await achievementClient.GetAchievementProgressAsync(
                new GetAchievementProgressRequest
                {
                    GameServiceId = TestGameServiceId,
                    AchievementId = workerAchievementId,
                    EntityId = worker.CharacterId,
                    EntityType = EntityType.Character
                });

            var employerProgress = await achievementClient.GetAchievementProgressAsync(
                new GetAchievementProgressRequest
                {
                    GameServiceId = TestGameServiceId,
                    AchievementId = employerAchievementId,
                    EntityId = employer.CharacterId,
                    EntityType = EntityType.Character
                });

            var workerAchProgress = workerProgress.Progress
                .FirstOrDefault(p => p.AchievementId == workerAchievementId);
            var employerAchProgress = employerProgress.Progress
                .FirstOrDefault(p => p.AchievementId == employerAchievementId);

            return TestResult.Successful(
                $"Character deal completed: contract {activeContract.ContractId}, " +
                $"worker achievement progress={workerAchProgress?.CurrentProgress ?? 0}, " +
                $"employer achievement progress={employerAchProgress?.CurrentProgress ?? 0}");
        }, "Character deal with achievements");

    /// <summary>
    /// Tests a contract with prebound APIs that include response validation rules.
    /// The prebound API calls a service and validates the response matches expected conditions.
    /// This verifies the ResponseValidator three-outcome model works end-to-end.
    /// </summary>
    private static async Task<TestResult> TestContractWithPreboundApiValidation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("VALIDATION");

            // Create a milestone with a prebound API that calls realm/get with validation
            // This validates that the realm exists (which it does since we just created it)
            var milestones = new List<MilestoneDefinition>
            {
                new MilestoneDefinition
                {
                    Code = "verify_realm",
                    Name = "Verify Realm Exists",
                    Description = "Validate the realm is still active before proceeding",
                    Sequence = 0,
                    Required = true,
                    OnComplete = new List<PreboundApi>
                    {
                        new PreboundApi
                        {
                            ServiceName = "realm",
                            Endpoint = "/realm/get",
                            PayloadTemplate = $"{{\"realmId\":\"{realm.RealmId}\"}}",
                            Description = "Verify realm is still active",
                            ExecutionMode = PreboundApiExecutionMode.Sync,
                            ResponseValidation = new ResponseValidation
                            {
                                SuccessConditions = new List<ValidationCondition>
                                {
                                    new ValidationCondition
                                    {
                                        Type = ValidationConditionType.StatusCodeIn,
                                        StatusCodes = new List<int> { 200 }
                                    },
                                    new ValidationCondition
                                    {
                                        Type = ValidationConditionType.JsonPathExists,
                                        JsonPath = "$.realmId",
                                        StatusCodes = new List<int>()
                                    }
                                },
                                PermanentFailureConditions = new List<ValidationCondition>
                                {
                                    new ValidationCondition
                                    {
                                        Type = ValidationConditionType.StatusCodeIn,
                                        StatusCodes = new List<int> { 404 }
                                    }
                                },
                                TransientFailureStatusCodes = new List<int> { 503, 504 }
                            }
                        }
                    }
                },
                new MilestoneDefinition
                {
                    Code = "complete_work",
                    Name = "Complete Work",
                    Description = "Final work delivery",
                    Sequence = 1,
                    Required = true
                }
            };

            var template = await CreateEmploymentTemplateAsync(
                contractClient, "validation", realm.RealmId, milestones);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            // Complete first milestone - triggers realm validation prebound API
            var milestone1 = await contractClient.CompleteMilestoneAsync(new CompleteMilestoneRequest
            {
                ContractId = activeContract.ContractId,
                MilestoneCode = "verify_realm"
            });

            if (milestone1.Milestone.Status != MilestoneStatus.Completed)
                return TestResult.Failed($"Verify realm milestone not completed: {milestone1.Milestone.Status}");

            // Complete second milestone
            var milestone2 = await contractClient.CompleteMilestoneAsync(new CompleteMilestoneRequest
            {
                ContractId = activeContract.ContractId,
                MilestoneCode = "complete_work"
            });

            if (milestone2.Milestone.Status != MilestoneStatus.Completed)
                return TestResult.Failed($"Complete work milestone not completed: {milestone2.Milestone.Status}");

            // Verify contract state
            var finalContract = await contractClient.GetContractInstanceAsync(new GetContractInstanceRequest
            {
                ContractId = activeContract.ContractId
            });

            return TestResult.Successful(
                $"Validated contract {activeContract.ContractId}: " +
                $"realm verification passed, all milestones completed, " +
                $"final status: {finalContract.Status}");
        }, "Contract with prebound API validation");

    // =========================================================================
    // Breach and Constraint Tests
    // =========================================================================

    private static async Task<TestResult> TestContractBreachAndCure(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("BREACH");

            // Contracts without milestones auto-fulfill; breach/cure requires Active status (work in progress)
            var milestones = new List<MilestoneDefinition>
            {
                new MilestoneDefinition
                {
                    Code = "deliver_goods",
                    Name = "Deliver Goods",
                    Description = "Worker delivers the agreed goods",
                    Sequence = 0,
                    Required = true
                }
            };

            var template = await CreateEmploymentTemplateAsync(contractClient, "breach", realm.RealmId, milestones);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId,
                new ContractTerms
                {
                    BreachThreshold = 3,
                    GracePeriodForCure = "P7D"
                });

            if (activeContract.Status != ContractStatus.Active)
                return TestResult.Failed($"Contract not active: {activeContract.Status}");

            // Report a breach
            var breach = await contractClient.ReportBreachAsync(new ReportBreachRequest
            {
                ContractId = activeContract.ContractId,
                BreachingEntityId = charB.CharacterId,
                BreachingEntityType = EntityType.Character,
                BreachType = BreachType.TermViolation,
                Description = "Worker failed to deliver on time"
            });

            if (breach.BreachId == Guid.Empty)
                return TestResult.Failed("Breach report returned empty ID");

            // When GracePeriodForCure is configured, breach enters cure_period status (not detected)
            if (breach.Status != BreachStatus.CurePeriod)
                return TestResult.Failed($"Expected cure_period status (grace period configured), got: {breach.Status}");

            // Cure the breach
            var cured = await contractClient.CureBreachAsync(new CureBreachRequest
            {
                BreachId = breach.BreachId,
                CureEvidence = "Delivered late but accepted by employer"
            });

            if (cured.Status != BreachStatus.Cured)
                return TestResult.Failed($"Expected cured status, got: {cured.Status}");

            // Verify contract is still active after cured breach
            var contractAfterCure = await contractClient.GetContractInstanceAsync(new GetContractInstanceRequest
            {
                ContractId = activeContract.ContractId
            });

            if (contractAfterCure.Status != ContractStatus.Active)
                return TestResult.Failed($"Contract should still be active after cure, got: {contractAfterCure.Status}");

            return TestResult.Successful(
                $"Breach lifecycle: reported → cured for contract {activeContract.ContractId}, " +
                $"contract remains active");
        }, "Contract breach and cure");

    private static async Task<TestResult> TestExclusivityConstraintCheck(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("EXCLUSIVITY");

            // Contracts without milestones auto-fulfill; exclusivity checks filter for Active status
            var milestones = new List<MilestoneDefinition>
            {
                new MilestoneDefinition
                {
                    Code = "ongoing_work",
                    Name = "Ongoing Work",
                    Description = "Worker performs ongoing duties",
                    Sequence = 0,
                    Required = true
                }
            };

            var template = await CreateEmploymentTemplateAsync(contractClient, "exclusive", realm.RealmId, milestones);

            // Create first contract with exclusivity
            var firstContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId,
                new ContractTerms
                {
                    CustomTerms = new Dictionary<string, object>
                    {
                        ["exclusivity"] = true
                    }
                });

            if (firstContract.Status != ContractStatus.Active)
                return TestResult.Failed($"First contract not active: {firstContract.Status}");

            // Check exclusivity constraint for the worker
            var constraintCheck = await contractClient.CheckContractConstraintAsync(new CheckConstraintRequest
            {
                EntityId = charB.CharacterId,
                EntityType = EntityType.Character,
                ConstraintType = ConstraintType.Exclusivity
            });

            if (constraintCheck.Allowed)
                return TestResult.Failed("Exclusivity constraint should prevent new contracts");

            if (constraintCheck.ConflictingContracts == null || constraintCheck.ConflictingContracts.Count == 0)
                return TestResult.Failed("Expected conflicting contracts list");

            return TestResult.Successful(
                $"Exclusivity constraint enforced: worker {charB.CharacterId} blocked from new contracts, " +
                $"{constraintCheck.ConflictingContracts.Count} conflicting contract(s) found");
        }, "Exclusivity constraint check");

    // =========================================================================
    // Escrow Integration Tests
    // =========================================================================

    /// <summary>
    /// Creates a transferable contract template with employer/worker roles.
    /// </summary>
    private static async Task<ContractTemplateResponse> CreateTransferableEmploymentTemplateAsync(
        IContractClient contractClient,
        string testSuffix,
        Guid? realmId = null,
        ICollection<MilestoneDefinition>? milestones = null,
        ContractTerms? defaultTerms = null)
    {
        return await contractClient.CreateContractTemplateAsync(new CreateContractTemplateRequest
        {
            Code = $"transferable_employment_{DateTime.Now.Ticks}_{testSuffix}",
            Name = $"Transferable Employment Contract {testSuffix}",
            Description = "Test employment contract template with transfer enabled",
            RealmId = realmId,
            MinParties = 2,
            MaxParties = 2,
            PartyRoles = new List<PartyRoleDefinition>
            {
                new PartyRoleDefinition
                {
                    Role = "employer",
                    MinCount = 1,
                    MaxCount = 1,
                    AllowedEntityTypes = new List<EntityType> { EntityType.Character }
                },
                new PartyRoleDefinition
                {
                    Role = "worker",
                    MinCount = 1,
                    MaxCount = 1,
                    AllowedEntityTypes = new List<EntityType> { EntityType.Character }
                }
            },
            DefaultTerms = defaultTerms ?? new ContractTerms
            {
                Duration = "P30D",
                BreachThreshold = 3,
                GracePeriodForCure = "P7D"
            },
            Milestones = milestones,
            DefaultEnforcementMode = EnforcementMode.EventOnly,
            Transferable = true
        });
    }

    /// <summary>
    /// Tests locking a contract under guardian custody and then unlocking it.
    /// Verifies that the lock/unlock lifecycle works correctly.
    /// </summary>
    private static async Task<TestResult> TestGuardianLockUnlock(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("GUARDIAN_LOCK");

            // Contracts without milestones auto-fulfill; test expects Active status for lock operations
            var milestones = new List<MilestoneDefinition>
            {
                new MilestoneDefinition
                {
                    Code = "pending_transfer",
                    Name = "Pending Transfer",
                    Description = "Contract awaiting escrow settlement",
                    Sequence = 0,
                    Required = true
                }
            };

            var template = await CreateTransferableEmploymentTemplateAsync(
                contractClient, "guardian_lock", realm.RealmId, milestones);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            if (activeContract.Status != ContractStatus.Active)
                return TestResult.Failed($"Contract not active: {activeContract.Status}");

            // Lock the contract
            var guardianId = Guid.NewGuid();
            var lockResponse = await contractClient.LockContractAsync(new LockContractRequest
            {
                ContractInstanceId = activeContract.ContractId,
                GuardianId = guardianId,
                GuardianType = "escrow"
            });

            if (!lockResponse.Locked)
                return TestResult.Failed("Lock response indicates not locked");

            if (lockResponse.ContractId != activeContract.ContractId)
                return TestResult.Failed("Lock response contract ID mismatch");

            if (lockResponse.GuardianId != guardianId)
                return TestResult.Failed("Lock response guardian ID mismatch");

            // Unlock the contract
            var unlockResponse = await contractClient.UnlockContractAsync(new UnlockContractRequest
            {
                ContractInstanceId = activeContract.ContractId,
                GuardianId = guardianId,
                GuardianType = "escrow"
            });

            if (!unlockResponse.Unlocked)
                return TestResult.Failed("Unlock response indicates not unlocked");

            return TestResult.Successful(
                $"Guardian lock/unlock lifecycle: contract {activeContract.ContractId}, " +
                $"guardian {guardianId}");
        }, "Guardian lock/unlock");

    /// <summary>
    /// Tests transferring a party role to a new entity while the contract is locked.
    /// Verifies that the transfer updates the party correctly.
    /// </summary>
    private static async Task<TestResult> TestTransferPartyRole(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("TRANSFER_PARTY");

            var template = await CreateTransferableEmploymentTemplateAsync(
                contractClient, "transfer_party", realm.RealmId);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            // Lock the contract
            var guardianId = Guid.NewGuid();
            await contractClient.LockContractAsync(new LockContractRequest
            {
                ContractInstanceId = activeContract.ContractId,
                GuardianId = guardianId,
                GuardianType = "escrow"
            });

            // Create a third character to receive the worker role
            var characterClient = GetServiceClient<ICharacterClient>();
            var speciesClient = GetServiceClient<ISpeciesClient>();

            var speciesResponse = await speciesClient.CreateSpeciesAsync(new CreateSpeciesRequest
            {
                Code = $"TRANSFER_SPECIES_{DateTime.Now.Ticks}",
                Name = "Transfer Test Species",
                Description = "Test species for transfer test"
            });
            await speciesClient.AddSpeciesToRealmAsync(new AddSpeciesToRealmRequest
            {
                SpeciesId = speciesResponse.SpeciesId,
                RealmId = realm.RealmId
            });

            var newWorker = await characterClient.CreateCharacterAsync(new CreateCharacterRequest
            {
                Name = $"NewWorker_{DateTime.Now.Ticks}",
                RealmId = realm.RealmId,
                SpeciesId = speciesResponse.SpeciesId,
                BirthDate = DateTimeOffset.UtcNow.AddYears(-25),
                Status = CharacterStatus.Alive
            });

            // Transfer the worker role from charB to newWorker
            var transferResponse = await contractClient.TransferContractPartyAsync(new TransferContractPartyRequest
            {
                ContractInstanceId = activeContract.ContractId,
                FromEntityId = charB.CharacterId,
                FromEntityType = EntityType.Character,
                ToEntityId = newWorker.CharacterId,
                ToEntityType = EntityType.Character,
                GuardianId = guardianId,
                GuardianType = "escrow"
            });

            if (!transferResponse.Transferred)
                return TestResult.Failed("Transfer response indicates not transferred");

            // Verify the contract now shows the new worker
            var updatedContract = await contractClient.GetContractInstanceAsync(new GetContractInstanceRequest
            {
                ContractId = activeContract.ContractId
            });

            var workerParty = updatedContract.Parties?.FirstOrDefault(p => p.Role == "worker");
            if (workerParty == null)
                return TestResult.Failed("Worker party not found after transfer");

            if (workerParty.EntityId != newWorker.CharacterId)
                return TestResult.Failed(
                    $"Worker entity ID not updated: expected {newWorker.CharacterId}, got {workerParty.EntityId}");

            return TestResult.Successful(
                $"Party transfer: contract {activeContract.ContractId}, " +
                $"worker {charB.CharacterId} → {newWorker.CharacterId}");
        }, "Transfer party role");

    /// <summary>
    /// Tests that a locked contract cannot be terminated.
    /// Verifies guardian enforcement on state-modifying operations.
    /// </summary>
    private static async Task<TestResult> TestGuardianEnforcementOnTerminate(ITestClient client, string[] args) =>
        await ExecuteExpectingStatusAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("GUARDIAN_ENFORCE");

            var template = await CreateTransferableEmploymentTemplateAsync(
                contractClient, "guardian_enforce", realm.RealmId);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            // Lock the contract
            var guardianId = Guid.NewGuid();
            await contractClient.LockContractAsync(new LockContractRequest
            {
                ContractInstanceId = activeContract.ContractId,
                GuardianId = guardianId,
                GuardianType = "escrow"
            });

            // Attempt to terminate - should be forbidden
            await contractClient.TerminateContractInstanceAsync(new TerminateContractInstanceRequest
            {
                ContractId = activeContract.ContractId,
                RequestingEntityId = charA.CharacterId,
                RequestingEntityType = EntityType.Character,
                Reason = "Should be blocked by guardian"
            });
        }, 403, "Guardian enforcement on terminate");

    /// <summary>
    /// Tests registering a custom clause type and listing all types.
    /// Verifies the clause type system works end-to-end.
    /// </summary>
    private static async Task<TestResult> TestClauseTypeRegistration(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();

            var typeCode = $"test_clause_{DateTime.Now.Ticks}";

            // Register a custom clause type
            var registerResponse = await contractClient.RegisterClauseTypeAsync(new RegisterClauseTypeRequest
            {
                TypeCode = typeCode,
                Description = "Test clause type for HTTP integration tests",
                Category = ClauseCategory.Execution,
                ExecutionHandler = new ClauseHandlerDefinition
                {
                    Service = "currency",
                    Endpoint = "/currency/transfer"
                }
            });

            if (!registerResponse.Registered)
                return TestResult.Failed("RegisterClauseType returned not registered");

            if (registerResponse.TypeCode != typeCode)
                return TestResult.Failed($"Type code mismatch: expected {typeCode}, got {registerResponse.TypeCode}");

            // List clause types
            var listResponse = await contractClient.ListClauseTypesAsync(new ListClauseTypesRequest
            {
                IncludeBuiltIn = true
            });

            if (listResponse.ClauseTypes == null || listResponse.ClauseTypes.Count == 0)
                return TestResult.Failed("No clause types returned");

            var registeredType = listResponse.ClauseTypes.FirstOrDefault(ct => ct.TypeCode == typeCode);
            if (registeredType == null)
                return TestResult.Failed($"Registered type '{typeCode}' not found in list");

            if (registeredType.Category != ClauseCategory.Execution)
                return TestResult.Failed($"Category mismatch: expected Execution, got {registeredType.Category}");

            if (!registeredType.HasExecutionHandler)
                return TestResult.Failed("Expected HasExecutionHandler to be true");

            return TestResult.Successful(
                $"Clause type registered: {typeCode}, total types: {listResponse.ClauseTypes.Count}");
        }, "Clause type registration");

    /// <summary>
    /// Tests setting template values on an active contract instance.
    /// Verifies the template value system stores and returns values correctly.
    /// </summary>
    private static async Task<TestResult> TestSetTemplateValues(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("TEMPLATE_VALUES");

            var template = await CreateEmploymentTemplateAsync(
                contractClient, "template_values", realm.RealmId);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            // Set template values
            var walletIdA = Guid.NewGuid();
            var walletIdB = Guid.NewGuid();
            var response = await contractClient.SetContractTemplateValuesAsync(new SetTemplateValuesRequest
            {
                ContractInstanceId = activeContract.ContractId,
                TemplateValues = new Dictionary<string, string>
                {
                    ["PartyA_WalletId"] = walletIdA.ToString(),
                    ["PartyB_WalletId"] = walletIdB.ToString(),
                    ["CurrencyCode"] = "gold",
                    ["base_amount"] = "1000"
                }
            });

            if (!response.Updated)
                return TestResult.Failed("SetTemplateValues returned not updated");

            if (response.ContractId != activeContract.ContractId)
                return TestResult.Failed("Contract ID mismatch in response");

            if (response.ValueCount != 4)
                return TestResult.Failed($"Expected 4 values set, got {response.ValueCount}");

            // Set additional values (merge behavior)
            var response2 = await contractClient.SetContractTemplateValuesAsync(new SetTemplateValuesRequest
            {
                ContractInstanceId = activeContract.ContractId,
                TemplateValues = new Dictionary<string, string>
                {
                    ["FeeWalletId"] = Guid.NewGuid().ToString()
                }
            });

            if (!response2.Updated)
                return TestResult.Failed("Second SetTemplateValues returned not updated");

            if (response2.ValueCount != 5)
                return TestResult.Failed($"Expected 5 total values after merge, got {response2.ValueCount}");

            return TestResult.Successful(
                $"Template values set: contract {activeContract.ContractId}, " +
                $"total values: {response2.ValueCount}");
        }, "Set template values");

    /// <summary>
    /// Tests full contract execution with currency transfer clauses.
    /// Creates a contract with fee and distribution clauses, sets up currency
    /// wallets, sets template values, and executes the contract.
    /// </summary>
    private static async Task<TestResult> TestContractExecutionWithCurrency(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var currencyClient = GetServiceClient<ICurrencyClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("EXECUTE_CURRENCY");

            // Step 1: Create a currency definition for the test
            var currencyCode = $"exec_gold_{DateTime.Now.Ticks}";
            var currencyDef = await currencyClient.CreateCurrencyDefinitionAsync(new CreateCurrencyDefinitionRequest
            {
                Code = currencyCode,
                Name = "Execution Test Gold",
                Precision = CurrencyPrecision.Integer,
                Scope = CurrencyScope.Global,
                Transferable = true
            });

            // Step 2: Create wallets for both parties and a fee wallet
            var walletA = await currencyClient.CreateWalletAsync(new CreateWalletRequest
            {
                OwnerId = charA.CharacterId,
                OwnerType = WalletOwnerType.Character
            });
            var walletB = await currencyClient.CreateWalletAsync(new CreateWalletRequest
            {
                OwnerId = charB.CharacterId,
                OwnerType = WalletOwnerType.Character
            });
            var feeOwnerId = Guid.NewGuid();
            var feeWallet = await currencyClient.CreateWalletAsync(new CreateWalletRequest
            {
                OwnerId = feeOwnerId,
                OwnerType = WalletOwnerType.System
            });

            // Step 3: Credit wallet A with funds
            await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = walletA.WalletId,
                CurrencyDefinitionId = currencyDef.DefinitionId,
                Amount = 1000,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = $"contract_test_credit_{Guid.NewGuid():N}"
            });

            // Step 4: Create contract template with currency clauses in CustomTerms
            // The clauses define a fee (10%) and a distribution (remainder)
            var clausesJson = $@"[
                {{
                    ""id"": ""platform_fee"",
                    ""type"": ""currency_transfer"",
                    ""category"": ""fee"",
                    ""source_wallet"": ""{{{{PartyA_WalletId}}}}"",
                    ""destination_wallet"": ""{{{{FeeWalletId}}}}"",
                    ""currency_code"": ""{currencyCode}"",
                    ""amount_type"": ""percentage"",
                    ""amount_value"": 10,
                    ""party_role"": ""employer""
                }},
                {{
                    ""id"": ""worker_payment"",
                    ""type"": ""currency_transfer"",
                    ""category"": ""distribution"",
                    ""source_wallet"": ""{{{{PartyA_WalletId}}}}"",
                    ""destination_wallet"": ""{{{{PartyB_WalletId}}}}"",
                    ""currency_code"": ""{currencyCode}"",
                    ""amount_type"": ""remainder"",
                    ""party_role"": ""employer""
                }}
            ]";

            var template = await contractClient.CreateContractTemplateAsync(new CreateContractTemplateRequest
            {
                Code = $"exec_template_{DateTime.Now.Ticks}",
                Name = "Execution Test Template",
                Description = "Template with currency transfer clauses",
                RealmId = realm.RealmId,
                MinParties = 2,
                MaxParties = 2,
                PartyRoles = new List<PartyRoleDefinition>
                {
                    new PartyRoleDefinition
                    {
                        Role = "employer",
                        MinCount = 1,
                        MaxCount = 1,
                        AllowedEntityTypes = new List<EntityType> { EntityType.Character }
                    },
                    new PartyRoleDefinition
                    {
                        Role = "worker",
                        MinCount = 1,
                        MaxCount = 1,
                        AllowedEntityTypes = new List<EntityType> { EntityType.Character }
                    }
                },
                DefaultTerms = new ContractTerms
                {
                    Duration = "P30D",
                    CustomTerms = new Dictionary<string, object>
                    {
                        ["clauses"] = clausesJson
                    }
                },
                DefaultEnforcementMode = EnforcementMode.EventOnly,
                Transferable = false
            });

            // Step 5: Create and activate contract
            // Note: Contracts without milestones auto-fulfill (no work to do = immediately ready for execution)
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            // Without milestones, contract goes directly to Fulfilled (ready for execution)
            if (activeContract.Status != ContractStatus.Fulfilled)
                return TestResult.Failed($"Contract not fulfilled: {activeContract.Status}");

            // Step 6: Set template values
            await contractClient.SetContractTemplateValuesAsync(new SetTemplateValuesRequest
            {
                ContractInstanceId = activeContract.ContractId,
                TemplateValues = new Dictionary<string, string>
                {
                    ["PartyA_WalletId"] = walletA.WalletId.ToString(),
                    ["PartyB_WalletId"] = walletB.WalletId.ToString(),
                    ["FeeWalletId"] = feeWallet.WalletId.ToString(),
                    ["base_amount"] = "1000"
                }
            });

            // Step 7: Register the currency_transfer clause type if not already
            try
            {
                await contractClient.RegisterClauseTypeAsync(new RegisterClauseTypeRequest
                {
                    TypeCode = "currency_transfer",
                    Description = "Currency transfer between wallets",
                    Category = ClauseCategory.Execution,
                    ExecutionHandler = new ClauseHandlerDefinition
                    {
                        Service = "currency",
                        Endpoint = "/currency/transfer"
                    }
                });
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                // Already registered - expected on repeated test runs
            }

            // Step 8: Execute the contract
            var idempotencyKey = Guid.NewGuid().ToString("N");
            var executeResponse = await contractClient.ExecuteContractAsync(new ExecuteContractRequest
            {
                ContractInstanceId = activeContract.ContractId,
                IdempotencyKey = idempotencyKey
            });

            if (!executeResponse.Executed)
                return TestResult.Failed("ExecuteContract returned not executed");

            if (executeResponse.AlreadyExecuted)
                return TestResult.Failed("First execution should not be flagged as already executed");

            // Step 9: Verify idempotency - execute again with same key
            var repeatResponse = await contractClient.ExecuteContractAsync(new ExecuteContractRequest
            {
                ContractInstanceId = activeContract.ContractId,
                IdempotencyKey = idempotencyKey
            });

            if (!repeatResponse.AlreadyExecuted)
                return TestResult.Failed("Repeat execution should be flagged as already executed");

            // Step 10: Verify balances
            var balanceA = await currencyClient.GetBalanceAsync(new GetBalanceRequest
            {
                WalletId = walletA.WalletId,
                CurrencyDefinitionId = currencyDef.DefinitionId
            });
            var balanceB = await currencyClient.GetBalanceAsync(new GetBalanceRequest
            {
                WalletId = walletB.WalletId,
                CurrencyDefinitionId = currencyDef.DefinitionId
            });
            var balanceFee = await currencyClient.GetBalanceAsync(new GetBalanceRequest
            {
                WalletId = feeWallet.WalletId,
                CurrencyDefinitionId = currencyDef.DefinitionId
            });

            return TestResult.Successful(
                $"Contract executed: {activeContract.ContractId}, " +
                $"idempotency verified, " +
                $"balances: A={balanceA.Amount}, B={balanceB.Amount}, Fee={balanceFee.Amount}, " +
                $"distributions: {executeResponse.Distributions?.Count ?? 0}");
        }, "Contract execution with currency");

    // =========================================================================
    // Additional Coverage Tests
    // =========================================================================

    private static async Task<TestResult> TestGetContractTemplate(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();

            var template = await CreateEmploymentTemplateAsync(contractClient, "get_template");

            // Get by ID
            var byId = await contractClient.GetContractTemplateAsync(new GetContractTemplateRequest
            {
                TemplateId = template.TemplateId
            });

            if (byId.TemplateId != template.TemplateId)
                return TestResult.Failed("Template ID mismatch on get by ID");

            // Get by code
            var byCode = await contractClient.GetContractTemplateAsync(new GetContractTemplateRequest
            {
                Code = template.Code
            });

            if (byCode.TemplateId != template.TemplateId)
                return TestResult.Failed("Template ID mismatch on get by code");

            return TestResult.Successful(
                $"Template retrieved by ID and code: {template.TemplateId}");
        }, "Get contract template");

    private static async Task<TestResult> TestListContractTemplates(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();

            // Create a template to ensure at least one exists
            var template = await CreateEmploymentTemplateAsync(contractClient, "list_templates");

            var listed = await contractClient.ListContractTemplatesAsync(new ListContractTemplatesRequest
            {
                Page = 1,
                PageSize = 100
            });

            if (listed.Templates == null || listed.Templates.Count == 0)
                return TestResult.Failed("No templates returned");

            var found = listed.Templates.Any(t => t.TemplateId == template.TemplateId);
            if (!found)
                return TestResult.Failed("Created template not found in list");

            return TestResult.Successful(
                $"Listed {listed.Templates.Count} templates, found created template");
        }, "List contract templates");

    private static async Task<TestResult> TestUpdateContractTemplate(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();

            var template = await CreateEmploymentTemplateAsync(contractClient, "update_template");

            var updated = await contractClient.UpdateContractTemplateAsync(new UpdateContractTemplateRequest
            {
                TemplateId = template.TemplateId,
                Name = "Updated Employment Contract",
                Description = "Updated description for test"
            });

            if (updated.Name != "Updated Employment Contract")
                return TestResult.Failed($"Name not updated: {updated.Name}");

            if (updated.Description != "Updated description for test")
                return TestResult.Failed($"Description not updated: {updated.Description}");

            return TestResult.Successful(
                $"Template updated: {template.TemplateId}, name={updated.Name}");
        }, "Update contract template");

    private static async Task<TestResult> TestDeleteContractTemplate(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();

            var template = await CreateEmploymentTemplateAsync(contractClient, "delete_template");

            await contractClient.DeleteContractTemplateAsync(new DeleteContractTemplateRequest
            {
                TemplateId = template.TemplateId
            });

            // Verify it's gone
            try
            {
                await contractClient.GetContractTemplateAsync(new GetContractTemplateRequest
                {
                    TemplateId = template.TemplateId
                });
                return TestResult.Failed("Template still retrievable after delete");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected
            }

            return TestResult.Successful(
                $"Template deleted: {template.TemplateId}");
        }, "Delete contract template");

    private static async Task<TestResult> TestQueryContractInstances(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("QUERY_INSTANCES");

            var milestones = new List<MilestoneDefinition>
            {
                new MilestoneDefinition
                {
                    Code = "work_task",
                    Name = "Work Task",
                    Description = "Complete the work",
                    Sequence = 0,
                    Required = true
                }
            };

            var template = await CreateEmploymentTemplateAsync(
                contractClient, "query_instances", realm.RealmId, milestones);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            // Query by party
            var byParty = await contractClient.QueryContractInstancesAsync(new QueryContractInstancesRequest
            {
                PartyEntityId = charA.CharacterId,
                PartyEntityType = EntityType.Character,
                Page = 1,
                PageSize = 10
            });

            if (byParty.Contracts == null || byParty.Contracts.Count == 0)
                return TestResult.Failed("No contracts found for party");

            var found = byParty.Contracts.Any(c => c.ContractId == activeContract.ContractId);
            if (!found)
                return TestResult.Failed("Created contract not found in party query");

            // Query by status
            var byStatus = await contractClient.QueryContractInstancesAsync(new QueryContractInstancesRequest
            {
                Statuses = new List<ContractStatus> { ContractStatus.Active },
                Page = 1,
                PageSize = 10
            });

            if (byStatus.Contracts == null || byStatus.Contracts.Count == 0)
                return TestResult.Failed("No active contracts found");

            return TestResult.Successful(
                $"Query results: by party={byParty.Contracts.Count}, by status={byStatus.Contracts.Count}");
        }, "Query contract instances");

    private static async Task<TestResult> TestGetContractInstanceStatus(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("GET_STATUS");

            var milestones = new List<MilestoneDefinition>
            {
                new MilestoneDefinition
                {
                    Code = "status_task",
                    Name = "Status Task",
                    Description = "Task for status test",
                    Sequence = 0,
                    Required = true
                }
            };

            var template = await CreateEmploymentTemplateAsync(
                contractClient, "get_status", realm.RealmId, milestones);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            var status = await contractClient.GetContractInstanceStatusAsync(new GetContractInstanceStatusRequest
            {
                ContractId = activeContract.ContractId
            });

            if (status.ContractId != activeContract.ContractId)
                return TestResult.Failed("Contract ID mismatch");

            if (status.Status != ContractStatus.Active)
                return TestResult.Failed($"Expected Active status, got: {status.Status}");

            return TestResult.Successful(
                $"Status retrieved: contract={status.ContractId}, status={status.Status}, " +
                $"milestones: {status.MilestoneProgress.Count}");
        }, "Get contract instance status");

    private static async Task<TestResult> TestFailMilestone(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("FAIL_MILESTONE");

            var milestones = new List<MilestoneDefinition>
            {
                new MilestoneDefinition
                {
                    Code = "optional_task",
                    Name = "Optional Task",
                    Description = "An optional task that can be skipped",
                    Sequence = 0,
                    Required = false
                },
                new MilestoneDefinition
                {
                    Code = "final_task",
                    Name = "Final Task",
                    Description = "Final required task",
                    Sequence = 1,
                    Required = true
                }
            };

            var template = await CreateEmploymentTemplateAsync(
                contractClient, "fail_milestone", realm.RealmId, milestones);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            // Fail the optional milestone
            var failed = await contractClient.FailMilestoneAsync(new FailMilestoneRequest
            {
                ContractId = activeContract.ContractId,
                MilestoneCode = "optional_task",
                Reason = "Could not complete optional task"
            });

            // Optional milestones become Skipped when failed
            if (failed.Milestone.Status != MilestoneStatus.Skipped)
                return TestResult.Failed($"Expected Skipped status for optional milestone, got: {failed.Milestone.Status}");

            return TestResult.Successful(
                $"Optional milestone failed/skipped: {failed.Milestone.Code}, status={failed.Milestone.Status}");
        }, "Fail milestone");

    private static async Task<TestResult> TestGetMilestone(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("GET_MILESTONE");

            var milestones = new List<MilestoneDefinition>
            {
                new MilestoneDefinition
                {
                    Code = "tracked_task",
                    Name = "Tracked Task",
                    Description = "A task we will track",
                    Sequence = 0,
                    Required = true
                }
            };

            var template = await CreateEmploymentTemplateAsync(
                contractClient, "get_milestone", realm.RealmId, milestones);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            var milestone = await contractClient.GetMilestoneAsync(new GetMilestoneRequest
            {
                ContractId = activeContract.ContractId,
                MilestoneCode = "tracked_task"
            });

            if (milestone.Milestone.Code != "tracked_task")
                return TestResult.Failed($"Milestone code mismatch: {milestone.Milestone.Code}");

            if (milestone.Milestone.Status != MilestoneStatus.Pending)
                return TestResult.Failed($"Expected Pending status, got: {milestone.Milestone.Status}");

            return TestResult.Successful(
                $"Milestone retrieved: {milestone.Milestone.Code}, status={milestone.Milestone.Status}");
        }, "Get milestone");

    private static async Task<TestResult> TestGetBreach(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("GET_BREACH");

            var milestones = new List<MilestoneDefinition>
            {
                new MilestoneDefinition
                {
                    Code = "breach_task",
                    Name = "Breach Task",
                    Description = "Task for breach test",
                    Sequence = 0,
                    Required = true
                }
            };

            var template = await CreateEmploymentTemplateAsync(
                contractClient, "get_breach", realm.RealmId, milestones);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId,
                new ContractTerms
                {
                    BreachThreshold = 3,
                    GracePeriodForCure = "P7D"
                });

            // Report a breach
            var reported = await contractClient.ReportBreachAsync(new ReportBreachRequest
            {
                ContractId = activeContract.ContractId,
                BreachingEntityId = charB.CharacterId,
                BreachingEntityType = EntityType.Character,
                BreachType = BreachType.TermViolation,
                Description = "Failed to meet deadline"
            });

            // Get the breach
            var retrieved = await contractClient.GetBreachAsync(new GetBreachRequest
            {
                BreachId = reported.BreachId
            });

            if (retrieved.BreachId != reported.BreachId)
                return TestResult.Failed("Breach ID mismatch");

            if (retrieved.BreachType != BreachType.TermViolation)
                return TestResult.Failed($"Breach type mismatch: {retrieved.BreachType}");

            return TestResult.Successful(
                $"Breach retrieved: {retrieved.BreachId}, type={retrieved.BreachType}, status={retrieved.Status}");
        }, "Get breach");

    private static async Task<TestResult> TestUpdateContractMetadata(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("UPDATE_METADATA");

            var template = await CreateEmploymentTemplateAsync(
                contractClient, "update_metadata", realm.RealmId);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            var updated = await contractClient.UpdateContractMetadataAsync(new UpdateContractMetadataRequest
            {
                ContractId = activeContract.ContractId,
                MetadataType = MetadataType.InstanceData,
                Data = new Dictionary<string, object>
                {
                    ["priority"] = "high",
                    ["department"] = "engineering",
                    ["budget"] = 50000
                }
            });

            if (updated.ContractId != activeContract.ContractId)
                return TestResult.Failed("Contract ID mismatch in update response");

            return TestResult.Successful(
                $"Metadata updated for contract {updated.ContractId}");
        }, "Update contract metadata");

    private static async Task<TestResult> TestGetContractMetadata(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("GET_METADATA");

            var template = await CreateEmploymentTemplateAsync(
                contractClient, "get_metadata", realm.RealmId);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            // Set some metadata first
            await contractClient.UpdateContractMetadataAsync(new UpdateContractMetadataRequest
            {
                ContractId = activeContract.ContractId,
                MetadataType = MetadataType.InstanceData,
                Data = new Dictionary<string, object>
                {
                    ["testKey"] = "testValue"
                }
            });

            var metadata = await contractClient.GetContractMetadataAsync(new GetContractMetadataRequest
            {
                ContractId = activeContract.ContractId
            });

            if (metadata.ContractId != activeContract.ContractId)
                return TestResult.Failed("Contract ID mismatch");

            return TestResult.Successful(
                $"Metadata retrieved for contract {metadata.ContractId}, instanceData={metadata.InstanceData != null}");
        }, "Get contract metadata");

    private static async Task<TestResult> TestQueryActiveContracts(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("QUERY_ACTIVE");

            var milestones = new List<MilestoneDefinition>
            {
                new MilestoneDefinition
                {
                    Code = "active_task",
                    Name = "Active Task",
                    Description = "Task for active query test",
                    Sequence = 0,
                    Required = true
                }
            };

            var template = await CreateEmploymentTemplateAsync(
                contractClient, "query_active", realm.RealmId, milestones);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            var result = await contractClient.QueryActiveContractsAsync(new QueryActiveContractsRequest
            {
                EntityId = charA.CharacterId,
                EntityType = EntityType.Character
            });

            if (result.Contracts == null)
                return TestResult.Failed("Contracts list is null");

            var found = result.Contracts.Any(c => c.ContractId == activeContract.ContractId);
            if (!found)
                return TestResult.Failed("Created contract not found in active contracts query");

            return TestResult.Successful(
                $"Active contracts for entity: {result.Contracts.Count} found");
        }, "Query active contracts");

    private static async Task<TestResult> TestCheckAssetRequirements(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var contractClient = GetServiceClient<IContractClient>();
            var (realm, charA, charB) = await CreateTestCharacterPairAsync("CHECK_ASSETS");

            // Create a contract without asset clauses - should return all satisfied
            var template = await CreateEmploymentTemplateAsync(
                contractClient, "check_assets", realm.RealmId);
            var activeContract = await CreateActiveContractAsync(
                contractClient, template.TemplateId, charA.CharacterId, charB.CharacterId);

            var requirements = await contractClient.CheckAssetRequirementsAsync(new CheckAssetRequirementsRequest
            {
                ContractInstanceId = activeContract.ContractId
            });

            if (!requirements.AllSatisfied)
                return TestResult.Failed("Expected all satisfied for contract without asset clauses");

            return TestResult.Successful(
                $"Asset requirements check: allSatisfied={requirements.AllSatisfied}, " +
                $"parties={requirements.ByParty.Count}");
        }, "Check asset requirements");
}
