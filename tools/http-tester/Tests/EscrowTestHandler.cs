using BeyondImmersion.BannouService.Escrow;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for Escrow service HTTP API endpoints.
/// Tests escrow creation, deposits, consent, release, and dispute flows.
/// These tests verify basic service operation; detailed validation is in unit tests.
/// </summary>
public class EscrowTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // CRUD Tests
        new ServiceTest(TestCreateEscrow, "CreateEscrow", "Escrow", "Test escrow creation"),
        new ServiceTest(TestGetEscrow, "GetEscrow", "Escrow", "Test escrow retrieval"),
        new ServiceTest(TestListEscrows, "ListEscrows", "Escrow", "Test listing escrows by party"),

        // Token Tests
        new ServiceTest(TestGetMyToken, "GetMyToken", "Escrow", "Test getting party's deposit token"),

        // Deposit Tests
        new ServiceTest(TestDeposit, "Deposit", "Escrow", "Test depositing assets into escrow"),
        new ServiceTest(TestValidateDeposit, "ValidateDeposit", "Escrow", "Test deposit validation"),
        new ServiceTest(TestGetDepositStatus, "GetDepositStatus", "Escrow", "Test deposit status check"),

        // Consent Tests
        new ServiceTest(TestRecordConsent, "RecordConsent", "Escrow", "Test recording release consent"),
        new ServiceTest(TestGetConsentStatus, "GetConsentStatus", "Escrow", "Test consent status retrieval"),

        // Lifecycle Tests
        new ServiceTest(TestCancelEscrow, "Cancel", "Escrow", "Test cancelling unfunded escrow"),
        new ServiceTest(TestDisputeEscrow, "Dispute", "Escrow", "Test disputing a funded escrow"),

        // Handler Tests
        new ServiceTest(TestRegisterHandler, "RegisterHandler", "Escrow", "Test registering a custom asset handler"),
        new ServiceTest(TestListHandlers, "ListHandlers", "Escrow", "Test listing registered handlers"),

        // Validation Tests
        new ServiceTest(TestValidateEscrow, "ValidateEscrow", "Escrow", "Test escrow validation"),

        // Additional Coverage Tests
        new ServiceTest(TestRelease, "Release", "Escrow", "Test releasing a funded escrow"),
        new ServiceTest(TestRefund, "Refund", "Escrow", "Test refunding an escrow"),
        new ServiceTest(TestResolve, "Resolve", "Escrow", "Test resolving a disputed escrow"),
        new ServiceTest(TestVerifyCondition, "VerifyCondition", "Escrow", "Test verifying condition on conditional escrow"),
        new ServiceTest(TestReaffirm, "Reaffirm", "Escrow", "Test party reaffirmation"),
        new ServiceTest(TestDeregisterHandler, "DeregisterHandler", "Escrow", "Test deregistering an asset handler"),
    ];

    /// <summary>
    /// Helper to create a two-party escrow for testing.
    /// </summary>
    private static async Task<CreateEscrowResponse> CreateTestEscrowAsync(
        IEscrowClient client,
        Guid partyA,
        Guid partyB,
        EscrowTrustMode trustMode = EscrowTrustMode.InitiatorTrusted)
    {
        return await client.CreateEscrowAsync(new CreateEscrowRequest
        {
            EscrowType = EscrowType.TwoParty,
            TrustMode = trustMode,
            Parties = new List<CreateEscrowPartyInput>
            {
                new CreateEscrowPartyInput
                {
                    PartyId = partyA,
                    PartyType = EntityType.Account,
                    Role = EscrowPartyRole.Depositor
                },
                new CreateEscrowPartyInput
                {
                    PartyId = partyB,
                    PartyType = EntityType.Account,
                    Role = EscrowPartyRole.Recipient
                }
            },
            ExpectedDeposits = new List<ExpectedDepositInput>
            {
                new ExpectedDepositInput
                {
                    PartyId = partyA,
                    PartyType = EntityType.Account,
                    ExpectedAssets = new List<EscrowAssetInput>
                    {
                        new EscrowAssetInput
                        {
                            AssetType = AssetType.Currency,
                            CurrencyCode = "gold",
                            CurrencyAmount = 100
                        }
                    },
                    Optional = false
                }
            },
            Description = "Test escrow for HTTP tests"
        });
    }

    private static async Task<TestResult> TestCreateEscrow(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();

            var response = await CreateTestEscrowAsync(escrowClient, partyA, partyB);

            if (response.Escrow.Id == Guid.Empty)
                return TestResult.Failed("Escrow creation returned empty ID");

            if (response.Escrow.Status != EscrowStatus.PendingDeposits)
                return TestResult.Failed($"Expected PendingDeposits status, got: {response.Escrow.Status}");

            return TestResult.Successful($"Escrow created: ID={response.Escrow.Id}, Status={response.Escrow.Status}");
        }, "Create escrow");

    private static async Task<TestResult> TestGetEscrow(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();

            var created = await CreateTestEscrowAsync(escrowClient, partyA, partyB);

            var response = await escrowClient.GetEscrowAsync(new GetEscrowRequest
            {
                EscrowId = created.Escrow.Id
            });

            if (response.Escrow.Id != created.Escrow.Id)
                return TestResult.Failed("Escrow ID mismatch");

            return TestResult.Successful($"Escrow retrieved: ID={response.Escrow.Id}");
        }, "Get escrow");

    private static async Task<TestResult> TestListEscrows(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();

            await CreateTestEscrowAsync(escrowClient, partyA, partyB);

            var response = await escrowClient.ListEscrowsAsync(new ListEscrowsRequest
            {
                PartyId = partyA,
                PartyType = EntityType.Account
            });

            if (response.Escrows == null || response.Escrows.Count == 0)
                return TestResult.Failed("No escrows found for party");

            return TestResult.Successful($"Listed {response.Escrows.Count} escrows for party");
        }, "List escrows");

    private static async Task<TestResult> TestGetMyToken(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();

            // Use FullConsent mode to generate tokens
            var created = await escrowClient.CreateEscrowAsync(new CreateEscrowRequest
            {
                EscrowType = EscrowType.TwoParty,
                TrustMode = EscrowTrustMode.FullConsent,
                Parties = new List<CreateEscrowPartyInput>
                {
                    new CreateEscrowPartyInput
                    {
                        PartyId = partyA,
                        PartyType = EntityType.Account,
                        Role = EscrowPartyRole.Depositor
                    },
                    new CreateEscrowPartyInput
                    {
                        PartyId = partyB,
                        PartyType = EntityType.Account,
                        Role = EscrowPartyRole.Recipient
                    }
                },
                ExpectedDeposits = new List<ExpectedDepositInput>
                {
                    new ExpectedDepositInput
                    {
                        PartyId = partyA,
                        PartyType = EntityType.Account,
                        ExpectedAssets = new List<EscrowAssetInput>
                        {
                            new EscrowAssetInput
                            {
                                AssetType = AssetType.Currency,
                                CurrencyCode = "gold",
                                CurrencyAmount = 100
                            }
                        },
                        Optional = false
                    }
                },
                Description = "Test escrow for token test"
            });

            var response = await escrowClient.GetMyTokenAsync(new GetMyTokenRequest
            {
                EscrowId = created.Escrow.Id,
                OwnerId = partyA,
                OwnerType = EntityType.Account,
                TokenType = TokenType.Deposit
            });

            if (string.IsNullOrEmpty(response.Token))
                return TestResult.Failed("Token is empty");

            return TestResult.Successful($"Token retrieved: tokenUsed={response.TokenUsed}");
        }, "Get my token");

    private static async Task<TestResult> TestDeposit(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();

            var created = await CreateTestEscrowAsync(escrowClient, partyA, partyB);

            var response = await escrowClient.DepositAsync(new DepositRequest
            {
                EscrowId = created.Escrow.Id,
                PartyId = partyA,
                PartyType = EntityType.Account,
                Assets = new EscrowAssetBundleInput
                {
                    Assets = new List<EscrowAssetInput>
                    {
                        new EscrowAssetInput
                        {
                            AssetType = AssetType.Currency,
                            CurrencyCode = "gold",
                            CurrencyAmount = 100
                        }
                    }
                },
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            if (response.Escrow == null)
                return TestResult.Failed("Deposit did not return escrow");

            return TestResult.Successful($"Deposit accepted: fullyFunded={response.FullyFunded}");
        }, "Deposit");

    private static async Task<TestResult> TestValidateDeposit(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();

            var created = await CreateTestEscrowAsync(escrowClient, partyA, partyB);

            var response = await escrowClient.ValidateDepositAsync(new ValidateDepositRequest
            {
                EscrowId = created.Escrow.Id,
                PartyId = partyA,
                PartyType = EntityType.Account,
                Assets = new EscrowAssetBundleInput
                {
                    Assets = new List<EscrowAssetInput>
                    {
                        new EscrowAssetInput
                        {
                            AssetType = AssetType.Currency,
                            CurrencyCode = "gold",
                            CurrencyAmount = 100
                        }
                    }
                }
            });

            if (!response.Valid)
                return TestResult.Failed($"Deposit validation failed: errors={response.Errors.Count}");

            return TestResult.Successful("Deposit validated successfully");
        }, "Validate deposit");

    private static async Task<TestResult> TestGetDepositStatus(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();

            var created = await CreateTestEscrowAsync(escrowClient, partyA, partyB);

            var response = await escrowClient.GetDepositStatusAsync(new GetDepositStatusRequest
            {
                EscrowId = created.Escrow.Id,
                PartyId = partyA,
                PartyType = EntityType.Account
            });

            return TestResult.Successful($"Deposit status: fulfilled={response.Fulfilled}");
        }, "Get deposit status");

    private static async Task<TestResult> TestRecordConsent(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();

            var created = await CreateTestEscrowAsync(escrowClient, partyA, partyB);

            // Deposit first to fund the escrow
            await escrowClient.DepositAsync(new DepositRequest
            {
                EscrowId = created.Escrow.Id,
                PartyId = partyA,
                PartyType = EntityType.Account,
                Assets = new EscrowAssetBundleInput
                {
                    Assets = new List<EscrowAssetInput>
                    {
                        new EscrowAssetInput
                        {
                            AssetType = AssetType.Currency,
                            CurrencyCode = "gold",
                            CurrencyAmount = 100
                        }
                    }
                },
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Record consent
            var response = await escrowClient.RecordConsentAsync(new ConsentRequest
            {
                EscrowId = created.Escrow.Id,
                PartyId = partyA,
                PartyType = EntityType.Account,
                ConsentType = EscrowConsentType.Release
            });

            if (!response.ConsentRecorded)
                return TestResult.Failed("Consent was not recorded");

            return TestResult.Successful($"Consent recorded: triggered={response.Triggered}");
        }, "Record consent");

    private static async Task<TestResult> TestGetConsentStatus(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();

            var created = await CreateTestEscrowAsync(escrowClient, partyA, partyB);

            var response = await escrowClient.GetConsentStatusAsync(new GetConsentStatusRequest
            {
                EscrowId = created.Escrow.Id
            });

            return TestResult.Successful($"Consent status: received={response.ConsentsReceived}, required={response.ConsentsRequired}, canRelease={response.CanRelease}");
        }, "Get consent status");

    private static async Task<TestResult> TestCancelEscrow(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();

            var created = await CreateTestEscrowAsync(escrowClient, partyA, partyB);

            var response = await escrowClient.CancelAsync(new CancelRequest
            {
                EscrowId = created.Escrow.Id,
                Reason = "Test cancellation",
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            if (response.Escrow.Status != EscrowStatus.Cancelled)
                return TestResult.Failed($"Escrow not cancelled, status: {response.Escrow.Status}");

            return TestResult.Successful($"Escrow cancelled: ID={created.Escrow.Id}");
        }, "Cancel escrow");

    private static async Task<TestResult> TestDisputeEscrow(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();

            var created = await CreateTestEscrowAsync(escrowClient, partyA, partyB);

            // Deposit to fund the escrow
            await escrowClient.DepositAsync(new DepositRequest
            {
                EscrowId = created.Escrow.Id,
                PartyId = partyA,
                PartyType = EntityType.Account,
                Assets = new EscrowAssetBundleInput
                {
                    Assets = new List<EscrowAssetInput>
                    {
                        new EscrowAssetInput
                        {
                            AssetType = AssetType.Currency,
                            CurrencyCode = "gold",
                            CurrencyAmount = 100
                        }
                    }
                },
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            var response = await escrowClient.DisputeAsync(new DisputeRequest
            {
                EscrowId = created.Escrow.Id,
                PartyId = partyB,
                PartyType = EntityType.Account,
                Reason = "Test dispute"
            });

            if (response.Escrow.Status != EscrowStatus.Disputed)
                return TestResult.Failed($"Escrow not disputed, status: {response.Escrow.Status}");

            return TestResult.Successful($"Escrow disputed: ID={created.Escrow.Id}");
        }, "Dispute escrow");

    private static async Task<TestResult> TestRegisterHandler(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var assetType = $"custom_asset_{DateTime.Now.Ticks}";

            var response = await escrowClient.RegisterHandlerAsync(new RegisterHandlerRequest
            {
                AssetType = assetType,
                PluginId = "test-plugin",
                DepositEndpoint = "/test/deposit",
                ReleaseEndpoint = "/test/release",
                RefundEndpoint = "/test/refund",
                ValidateEndpoint = "/test/validate"
            });

            if (!response.Registered)
                return TestResult.Failed("Handler was not registered");

            return TestResult.Successful($"Handler registered: assetType={assetType}");
        }, "Register handler");

    private static async Task<TestResult> TestListHandlers(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();

            var response = await escrowClient.ListHandlersAsync(new ListHandlersRequest());

            // Should have at least the built-in handlers
            return TestResult.Successful($"Handlers listed: count={response.Handlers.Count}");
        }, "List handlers");

    private static async Task<TestResult> TestValidateEscrow(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();

            var created = await CreateTestEscrowAsync(escrowClient, partyA, partyB);

            var response = await escrowClient.ValidateEscrowAsync(new ValidateEscrowRequest
            {
                EscrowId = created.Escrow.Id
            });

            if (!response.Valid)
                return TestResult.Failed($"Escrow validation failed: failures={response.Failures.Count}");

            return TestResult.Successful($"Escrow validated: valid={response.Valid}");
        }, "Validate escrow");

    // =========================================================================
    // Additional Coverage Tests
    // =========================================================================

    private static async Task<TestResult> TestRelease(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();

            // Create escrow with initiator_trusted mode for easier release
            var created = await CreateTestEscrowAsync(escrowClient, partyA, partyB, EscrowTrustMode.InitiatorTrusted);

            // Deposit to fund the escrow
            await escrowClient.DepositAsync(new DepositRequest
            {
                EscrowId = created.Escrow.Id,
                PartyId = partyA,
                PartyType = EntityType.Account,
                Assets = new EscrowAssetBundleInput
                {
                    Assets = new List<EscrowAssetInput>
                    {
                        new EscrowAssetInput
                        {
                            AssetType = AssetType.Currency,
                            CurrencyCode = "gold",
                            CurrencyAmount = 100
                        }
                    }
                },
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Record consent for release
            await escrowClient.RecordConsentAsync(new ConsentRequest
            {
                EscrowId = created.Escrow.Id,
                PartyId = partyA,
                PartyType = EntityType.Account,
                ConsentType = EscrowConsentType.Release
            });

            // Attempt release
            var response = await escrowClient.ReleaseAsync(new ReleaseRequest
            {
                EscrowId = created.Escrow.Id,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            return TestResult.Successful($"Release called: escrowStatus={response.Escrow.Status}");
        }, "Release escrow");

    private static async Task<TestResult> TestRefund(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();

            var created = await CreateTestEscrowAsync(escrowClient, partyA, partyB, EscrowTrustMode.InitiatorTrusted);

            // Deposit to fund the escrow
            await escrowClient.DepositAsync(new DepositRequest
            {
                EscrowId = created.Escrow.Id,
                PartyId = partyA,
                PartyType = EntityType.Account,
                Assets = new EscrowAssetBundleInput
                {
                    Assets = new List<EscrowAssetInput>
                    {
                        new EscrowAssetInput
                        {
                            AssetType = AssetType.Currency,
                            CurrencyCode = "gold",
                            CurrencyAmount = 100
                        }
                    }
                },
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Record refund consent
            await escrowClient.RecordConsentAsync(new ConsentRequest
            {
                EscrowId = created.Escrow.Id,
                PartyId = partyA,
                PartyType = EntityType.Account,
                ConsentType = EscrowConsentType.Refund
            });

            // Attempt refund
            var response = await escrowClient.RefundAsync(new RefundRequest
            {
                EscrowId = created.Escrow.Id,
                Reason = "Test refund",
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            return TestResult.Successful($"Refund called: escrowStatus={response.Escrow.Status}");
        }, "Refund escrow");

    private static async Task<TestResult> TestResolve(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();
            var arbiterId = Guid.NewGuid();

            // Create escrow with arbiter party
            var created = await escrowClient.CreateEscrowAsync(new CreateEscrowRequest
            {
                EscrowType = EscrowType.TwoParty,
                TrustMode = EscrowTrustMode.InitiatorTrusted,
                Parties = new List<CreateEscrowPartyInput>
                {
                    new CreateEscrowPartyInput
                    {
                        PartyId = partyA,
                        PartyType = EntityType.Account,
                        Role = EscrowPartyRole.Depositor
                    },
                    new CreateEscrowPartyInput
                    {
                        PartyId = partyB,
                        PartyType = EntityType.Account,
                        Role = EscrowPartyRole.Recipient
                    },
                    new CreateEscrowPartyInput
                    {
                        PartyId = arbiterId,
                        PartyType = EntityType.Account,
                        Role = EscrowPartyRole.Arbiter
                    }
                },
                ExpectedDeposits = new List<ExpectedDepositInput>
                {
                    new ExpectedDepositInput
                    {
                        PartyId = partyA,
                        PartyType = EntityType.Account,
                        ExpectedAssets = new List<EscrowAssetInput>
                        {
                            new EscrowAssetInput
                            {
                                AssetType = AssetType.Currency,
                                CurrencyCode = "gold",
                                CurrencyAmount = 100
                            }
                        },
                        Optional = false
                    }
                },
                Description = "Test escrow for resolve"
            });

            // Deposit to fund
            await escrowClient.DepositAsync(new DepositRequest
            {
                EscrowId = created.Escrow.Id,
                PartyId = partyA,
                PartyType = EntityType.Account,
                Assets = new EscrowAssetBundleInput
                {
                    Assets = new List<EscrowAssetInput>
                    {
                        new EscrowAssetInput
                        {
                            AssetType = AssetType.Currency,
                            CurrencyCode = "gold",
                            CurrencyAmount = 100
                        }
                    }
                },
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Raise dispute to get to disputed state
            await escrowClient.DisputeAsync(new DisputeRequest
            {
                EscrowId = created.Escrow.Id,
                PartyId = partyB,
                PartyType = EntityType.Account,
                Reason = "Test dispute for resolution"
            });

            // Attempt resolve
            var response = await escrowClient.ResolveAsync(new ResolveRequest
            {
                EscrowId = created.Escrow.Id,
                ArbiterId = arbiterId,
                ArbiterType = EntityType.Account,
                Resolution = EscrowResolution.Released,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            return TestResult.Successful($"Resolve called: escrowStatus={response.Escrow.Status}");
        }, "Resolve escrow");

    private static async Task<TestResult> TestVerifyCondition(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();
            var verifierId = Guid.NewGuid();

            // Create conditional escrow
            var created = await escrowClient.CreateEscrowAsync(new CreateEscrowRequest
            {
                EscrowType = EscrowType.Conditional,
                TrustMode = EscrowTrustMode.InitiatorTrusted,
                Parties = new List<CreateEscrowPartyInput>
                {
                    new CreateEscrowPartyInput
                    {
                        PartyId = partyA,
                        PartyType = EntityType.Account,
                        Role = EscrowPartyRole.Depositor
                    },
                    new CreateEscrowPartyInput
                    {
                        PartyId = partyB,
                        PartyType = EntityType.Account,
                        Role = EscrowPartyRole.Recipient
                    }
                },
                ExpectedDeposits = new List<ExpectedDepositInput>
                {
                    new ExpectedDepositInput
                    {
                        PartyId = partyA,
                        PartyType = EntityType.Account,
                        ExpectedAssets = new List<EscrowAssetInput>
                        {
                            new EscrowAssetInput
                            {
                                AssetType = AssetType.Currency,
                                CurrencyCode = "gold",
                                CurrencyAmount = 100
                            }
                        },
                        Optional = false
                    }
                },
                Description = "Test conditional escrow"
            });

            // Deposit to fund
            await escrowClient.DepositAsync(new DepositRequest
            {
                EscrowId = created.Escrow.Id,
                PartyId = partyA,
                PartyType = EntityType.Account,
                Assets = new EscrowAssetBundleInput
                {
                    Assets = new List<EscrowAssetInput>
                    {
                        new EscrowAssetInput
                        {
                            AssetType = AssetType.Currency,
                            CurrencyCode = "gold",
                            CurrencyAmount = 100
                        }
                    }
                },
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Verify condition
            var response = await escrowClient.VerifyConditionAsync(new VerifyConditionRequest
            {
                EscrowId = created.Escrow.Id,
                ConditionMet = true,
                VerifierId = verifierId,
                VerifierType = EntityType.Account
            });

            return TestResult.Successful($"VerifyCondition called: triggered={response.Triggered}");
        }, "Verify condition");

    private static async Task<TestResult> TestReaffirm(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var partyA = Guid.NewGuid();
            var partyB = Guid.NewGuid();

            var created = await CreateTestEscrowAsync(escrowClient, partyA, partyB);

            // Attempt reaffirm (may fail if escrow doesn't require reaffirmation, but tests the API call)
            var response = await escrowClient.ReaffirmAsync(new ReaffirmRequest
            {
                EscrowId = created.Escrow.Id,
                PartyId = partyA,
                PartyType = EntityType.Account,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            return TestResult.Successful($"Reaffirm called: allReaffirmed={response.AllReaffirmed}");
        }, "Reaffirm escrow");

    private static async Task<TestResult> TestDeregisterHandler(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var escrowClient = GetServiceClient<IEscrowClient>();
            var assetType = $"deregister_test_{DateTime.Now.Ticks}";

            // First register a handler
            await escrowClient.RegisterHandlerAsync(new RegisterHandlerRequest
            {
                AssetType = assetType,
                PluginId = "test-plugin",
                DepositEndpoint = "/test/deposit",
                ReleaseEndpoint = "/test/release",
                RefundEndpoint = "/test/refund",
                ValidateEndpoint = "/test/validate"
            });

            // Then deregister it
            var response = await escrowClient.DeregisterHandlerAsync(new DeregisterHandlerRequest
            {
                AssetType = assetType
            });

            if (!response.Deregistered)
                return TestResult.Failed("Handler was not deregistered");

            return TestResult.Successful($"Handler deregistered: assetType={assetType}");
        }, "Deregister handler");
}
