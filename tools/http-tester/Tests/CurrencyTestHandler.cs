using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for Currency service HTTP API endpoints.
/// Tests comprehensive currency management scenarios including:
/// - Currency definition CRUD operations
/// - Wallet creation and management
/// - Balance operations (credit, debit, transfer)
/// - Currency conversion with exchange rates
/// - Authorization holds (reserve, capture, release)
/// - Transaction history and analytics
/// </summary>
public class CurrencyTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Definition Tests
        new ServiceTest(TestCreateCurrencyDefinition, "CreateDefinition", "Currency",
            "Test currency definition creation"),
        new ServiceTest(TestGetCurrencyDefinition, "GetDefinition", "Currency",
            "Test currency definition retrieval by ID and code"),
        new ServiceTest(TestListCurrencyDefinitions, "ListDefinitions", "Currency",
            "Test listing currency definitions with filtering"),

        // Wallet Tests
        new ServiceTest(TestCreateWallet, "CreateWallet", "Currency",
            "Test wallet creation for an owner"),
        new ServiceTest(TestGetOrCreateWallet, "GetOrCreateWallet", "Currency",
            "Test idempotent wallet creation"),
        new ServiceTest(TestWalletFreezeUnfreeze, "WalletFreezeUnfreeze", "Currency",
            "Test wallet freeze and unfreeze operations"),

        // Balance Operations
        new ServiceTest(TestCreditCurrency, "CreditCurrency", "Currency",
            "Test crediting currency to a wallet"),
        new ServiceTest(TestDebitCurrency, "DebitCurrency", "Currency",
            "Test debiting currency from a wallet"),
        new ServiceTest(TestTransferCurrency, "TransferCurrency", "Currency",
            "Test transferring currency between wallets"),
        new ServiceTest(TestInsufficientBalance, "InsufficientBalance", "Currency",
            "Test debit fails with insufficient balance"),

        // Hold Operations (Authorization Holds)
        new ServiceTest(TestCreateAndCaptureHold, "CreateCaptureHold", "Currency",
            "Test creating and capturing an authorization hold"),
        new ServiceTest(TestCreateAndReleaseHold, "CreateReleaseHold", "Currency",
            "Test creating and releasing an authorization hold"),
        new ServiceTest(TestPartialHoldCapture, "PartialCapture", "Currency",
            "Test capturing less than the held amount"),

        // Conversion Tests
        new ServiceTest(TestCurrencyConversion, "CurrencyConversion", "Currency",
            "Test currency conversion with exchange rates"),

        // Transaction History
        new ServiceTest(TestTransactionHistory, "TransactionHistory", "Currency",
            "Test transaction history retrieval"),

        // Idempotency Tests
        new ServiceTest(TestIdempotentCredit, "IdempotentCredit", "Currency",
            "Test idempotency key prevents duplicate credits"),

        // Additional Coverage Tests
        new ServiceTest(TestUpdateCurrencyDefinition, "UpdateDefinition", "Currency",
            "Test updating a currency definition"),
        new ServiceTest(TestGetWallet, "GetWallet", "Currency",
            "Test getting wallet by ID and owner"),
        new ServiceTest(TestCloseWallet, "CloseWallet", "Currency",
            "Test closing wallet with balance transfer"),
        new ServiceTest(TestBatchGetBalances, "BatchGetBalances", "Currency",
            "Test batch balance retrieval"),
        new ServiceTest(TestBatchCreditCurrency, "BatchCredit", "Currency",
            "Test batch credit operations"),
        new ServiceTest(TestGetExchangeRate, "GetExchangeRate", "Currency",
            "Test exchange rate retrieval between currencies"),
        new ServiceTest(TestGetTransaction, "GetTransaction", "Currency",
            "Test getting a single transaction by ID"),
        new ServiceTest(TestGetTransactionsByReference, "TransactionsByReference", "Currency",
            "Test getting transactions by reference type and ID"),
        new ServiceTest(TestEscrowDeposit, "EscrowDeposit", "Currency",
            "Test escrow deposit operation"),
        new ServiceTest(TestEscrowRelease, "EscrowRelease", "Currency",
            "Test escrow release operation"),
        new ServiceTest(TestEscrowRefund, "EscrowRefund", "Currency",
            "Test escrow refund operation"),
        new ServiceTest(TestGetHold, "GetHold", "Currency",
            "Test getting authorization hold by ID"),
    ];

    // =========================================================================
    // Helper Methods
    // =========================================================================

    /// <summary>
    /// Creates a test currency definition with unique code.
    /// </summary>
    private static async Task<CurrencyDefinitionResponse> CreateTestCurrencyAsync(
        ICurrencyClient currencyClient,
        string suffix,
        CurrencyScope scope = CurrencyScope.Global,
        CurrencyPrecision precision = CurrencyPrecision.Decimal2,
        bool allowNegative = false)
    {
        // Code must match regex ^[a-z][a-z0-9_]{1,31}$
        var code = $"test_{DateTime.Now.Ticks % 1000000}_{suffix.ToLowerInvariant()}";
        return await currencyClient.CreateCurrencyDefinitionAsync(new CreateCurrencyDefinitionRequest
        {
            Code = code,
            Name = $"Test Currency {suffix}",
            Description = $"Test currency for {suffix} tests",
            Scope = scope,
            Precision = precision,
            AllowNegative = allowNegative
        });
    }

    /// <summary>
    /// Creates a test wallet for an owner.
    /// </summary>
    private static async Task<WalletResponse> CreateTestWalletAsync(
        ICurrencyClient currencyClient,
        string suffix,
        WalletOwnerType ownerType = WalletOwnerType.Account)
    {
        return await currencyClient.CreateWalletAsync(new CreateWalletRequest
        {
            OwnerId = Guid.NewGuid(),
            OwnerType = ownerType
        });
    }

    // =========================================================================
    // Definition Tests
    // =========================================================================

    private static async Task<TestResult> TestCreateCurrencyDefinition(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "CREATE");

            if (definition.DefinitionId == Guid.Empty)
                return TestResult.Failed("Currency definition creation returned empty ID");

            if (!definition.Code.StartsWith("test_"))
                return TestResult.Failed($"Currency code mismatch: '{definition.Code}'");

            return TestResult.Successful(
                $"Currency created: ID={definition.DefinitionId}, Code={definition.Code}");
        }, "Create currency definition");

    private static async Task<TestResult> TestGetCurrencyDefinition(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "GET");

            // Get by ID
            var byId = await currencyClient.GetCurrencyDefinitionAsync(new GetCurrencyDefinitionRequest
            {
                DefinitionId = definition.DefinitionId
            });

            if (byId.DefinitionId != definition.DefinitionId)
                return TestResult.Failed("Get by ID returned wrong definition");

            // Get by code
            var byCode = await currencyClient.GetCurrencyDefinitionAsync(new GetCurrencyDefinitionRequest
            {
                Code = definition.Code
            });

            if (byCode.DefinitionId != definition.DefinitionId)
                return TestResult.Failed("Get by code returned wrong definition");

            return TestResult.Successful(
                $"Currency retrieved by ID and code: {definition.DefinitionId}");
        }, "Get currency definition");

    private static async Task<TestResult> TestListCurrencyDefinitions(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            // Create a few definitions
            await CreateTestCurrencyAsync(currencyClient, "list1");
            await CreateTestCurrencyAsync(currencyClient, "list2");

            var list = await currencyClient.ListCurrencyDefinitionsAsync(new ListCurrencyDefinitionsRequest
            {
                IncludeInactive = false
            });

            if (list.Definitions == null || list.Definitions.Count == 0)
                return TestResult.Failed("List returned no definitions");

            return TestResult.Successful(
                $"Listed {list.Definitions.Count} currency definitions");
        }, "List currency definitions");

    // =========================================================================
    // Wallet Tests
    // =========================================================================

    private static async Task<TestResult> TestCreateWallet(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var wallet = await CreateTestWalletAsync(currencyClient, "CREATE");

            if (wallet.WalletId == Guid.Empty)
                return TestResult.Failed("Wallet creation returned empty ID");

            if (wallet.Status != WalletStatus.Active)
                return TestResult.Failed($"Expected active status, got: {wallet.Status}");

            return TestResult.Successful(
                $"Wallet created: ID={wallet.WalletId}, Status={wallet.Status}");
        }, "Create wallet");

    private static async Task<TestResult> TestGetOrCreateWallet(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();
            var ownerId = Guid.NewGuid();

            // First call creates
            var first = await currencyClient.GetOrCreateWalletAsync(new GetOrCreateWalletRequest
            {
                OwnerId = ownerId,
                OwnerType = WalletOwnerType.Account
            });

            if (!first.Created)
                return TestResult.Failed("First call should indicate created=true");

            // Second call retrieves
            var second = await currencyClient.GetOrCreateWalletAsync(new GetOrCreateWalletRequest
            {
                OwnerId = ownerId,
                OwnerType = WalletOwnerType.Account
            });

            if (second.Created)
                return TestResult.Failed("Second call should indicate created=false");

            if (second.Wallet.WalletId != first.Wallet.WalletId)
                return TestResult.Failed("Second call returned different wallet ID");

            return TestResult.Successful(
                $"GetOrCreate idempotent: WalletId={first.Wallet.WalletId}");
        }, "GetOrCreate wallet");

    private static async Task<TestResult> TestWalletFreezeUnfreeze(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var wallet = await CreateTestWalletAsync(currencyClient, "FREEZE");

            // Freeze
            var frozen = await currencyClient.FreezeWalletAsync(new FreezeWalletRequest
            {
                WalletId = wallet.WalletId,
                Reason = "Test freeze"
            });

            if (frozen.Status != WalletStatus.Frozen)
                return TestResult.Failed($"Expected frozen status, got: {frozen.Status}");

            // Unfreeze
            var unfrozen = await currencyClient.UnfreezeWalletAsync(new UnfreezeWalletRequest
            {
                WalletId = wallet.WalletId
            });

            if (unfrozen.Status != WalletStatus.Active)
                return TestResult.Failed($"Expected active status after unfreeze, got: {unfrozen.Status}");

            return TestResult.Successful(
                $"Wallet freeze/unfreeze: {wallet.WalletId}");
        }, "Wallet freeze/unfreeze");

    // =========================================================================
    // Balance Operations
    // =========================================================================

    private static async Task<TestResult> TestCreditCurrency(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "credit");
            var wallet = await CreateTestWalletAsync(currencyClient, "CREDIT");

            var credit = await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 1000.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            if (credit.NewBalance != 1000.0)
                return TestResult.Failed($"Expected balance 1000, got: {credit.NewBalance}");

            if (credit.Transaction.TransactionId == Guid.Empty)
                return TestResult.Failed("Transaction ID should not be empty");

            return TestResult.Successful(
                $"Credited 1000 to wallet {wallet.WalletId}, balance={credit.NewBalance}");
        }, "Credit currency");

    private static async Task<TestResult> TestDebitCurrency(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "debit");
            var wallet = await CreateTestWalletAsync(currencyClient, "DEBIT");

            // First credit some funds
            await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 500.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Then debit
            var debit = await currencyClient.DebitCurrencyAsync(new DebitCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 200.0,
                TransactionType = TransactionType.Burn,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            if (debit.NewBalance != 300.0)
                return TestResult.Failed($"Expected balance 300, got: {debit.NewBalance}");

            return TestResult.Successful(
                $"Debited 200 from wallet {wallet.WalletId}, balance={debit.NewBalance}");
        }, "Debit currency");

    private static async Task<TestResult> TestTransferCurrency(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "transfer");
            var sourceWallet = await CreateTestWalletAsync(currencyClient, "TRANSFER_SRC");
            var targetWallet = await CreateTestWalletAsync(currencyClient, "TRANSFER_DST");

            // Credit source wallet
            await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = sourceWallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 1000.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Transfer
            var transfer = await currencyClient.TransferCurrencyAsync(new TransferCurrencyRequest
            {
                SourceWalletId = sourceWallet.WalletId,
                TargetWalletId = targetWallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 400.0,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            if (transfer.SourceNewBalance != 600.0)
                return TestResult.Failed($"Expected source balance 600, got: {transfer.SourceNewBalance}");

            if (transfer.TargetNewBalance != 400.0)
                return TestResult.Failed($"Expected target balance 400, got: {transfer.TargetNewBalance}");

            return TestResult.Successful(
                $"Transferred 400: source={transfer.SourceNewBalance}, target={transfer.TargetNewBalance}");
        }, "Transfer currency");

    private static async Task<TestResult> TestInsufficientBalance(ITestClient client, string[] args) =>
        await ExecuteExpectingAnyStatusAsync(
            async () =>
            {
                var currencyClient = GetServiceClient<ICurrencyClient>();

                var definition = await CreateTestCurrencyAsync(currencyClient, "insuff");
                var wallet = await CreateTestWalletAsync(currencyClient, "INSUFF");

                // Credit only 100
                await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
                {
                    WalletId = wallet.WalletId,
                    CurrencyDefinitionId = definition.DefinitionId,
                    Amount = 100.0,
                    TransactionType = TransactionType.Mint,
                    IdempotencyKey = Guid.NewGuid().ToString()
                });

                // Try to debit 500 (should fail)
                await currencyClient.DebitCurrencyAsync(new DebitCurrencyRequest
                {
                    WalletId = wallet.WalletId,
                    CurrencyDefinitionId = definition.DefinitionId,
                    Amount = 500.0,
                    TransactionType = TransactionType.Burn,
                    IdempotencyKey = Guid.NewGuid().ToString()
                });
            },
            [400, 422], // Bad Request or Unprocessable Entity
            "Insufficient balance debit");

    // =========================================================================
    // Hold Operations
    // =========================================================================

    private static async Task<TestResult> TestCreateAndCaptureHold(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "holdcap");
            var wallet = await CreateTestWalletAsync(currencyClient, "HOLD_CAP");

            // Credit funds
            await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 500.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Create hold
            var hold = await currencyClient.CreateHoldAsync(new CreateHoldRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 200.0,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                ReferenceType = "test",
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            if (hold.Hold.HoldId == Guid.Empty)
                return TestResult.Failed("Hold ID should not be empty");

            if (hold.Hold.Status != HoldStatus.Active)
                return TestResult.Failed($"Expected active hold, got: {hold.Hold.Status}");

            // Capture the full amount
            var capture = await currencyClient.CaptureHoldAsync(new CaptureHoldRequest
            {
                HoldId = hold.Hold.HoldId,
                CaptureAmount = 200.0,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            if (capture.Hold.Status != HoldStatus.Captured)
                return TestResult.Failed($"Expected captured status, got: {capture.Hold.Status}");

            if (capture.NewBalance != 300.0)
                return TestResult.Failed($"Expected balance 300 after capture, got: {capture.NewBalance}");

            return TestResult.Successful(
                $"Hold created and captured: HoldId={hold.Hold.HoldId}, NewBalance={capture.NewBalance}");
        }, "Create and capture hold");

    private static async Task<TestResult> TestCreateAndReleaseHold(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "holdrel");
            var wallet = await CreateTestWalletAsync(currencyClient, "HOLD_REL");

            // Credit funds
            await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 500.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Create hold
            var hold = await currencyClient.CreateHoldAsync(new CreateHoldRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 200.0,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Release the hold (no charge)
            var release = await currencyClient.ReleaseHoldAsync(new ReleaseHoldRequest
            {
                HoldId = hold.Hold.HoldId
            });

            if (release.Hold.Status != HoldStatus.Released)
                return TestResult.Failed($"Expected released status, got: {release.Hold.Status}");

            // Verify balance is unchanged (funds released back)
            var balance = await currencyClient.GetBalanceAsync(new GetBalanceRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId
            });

            if (balance.EffectiveAmount != 500.0)
                return TestResult.Failed($"Expected effective balance 500 after release, got: {balance.EffectiveAmount}");

            return TestResult.Successful(
                $"Hold created and released: HoldId={hold.Hold.HoldId}");
        }, "Create and release hold");

    private static async Task<TestResult> TestPartialHoldCapture(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "holdpart");
            var wallet = await CreateTestWalletAsync(currencyClient, "HOLD_PART");

            // Credit funds
            await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 500.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Create hold for 200
            var hold = await currencyClient.CreateHoldAsync(new CreateHoldRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 200.0,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Capture only 150 (partial)
            var capture = await currencyClient.CaptureHoldAsync(new CaptureHoldRequest
            {
                HoldId = hold.Hold.HoldId,
                CaptureAmount = 150.0,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            if (capture.Hold.CapturedAmount != 150.0)
                return TestResult.Failed($"Expected captured amount 150, got: {capture.Hold.CapturedAmount}");

            if (capture.AmountReleased != 50.0)
                return TestResult.Failed($"Expected 50 released, got: {capture.AmountReleased}");

            // Balance should be 500 - 150 = 350
            if (capture.NewBalance != 350.0)
                return TestResult.Failed($"Expected balance 350, got: {capture.NewBalance}");

            return TestResult.Successful(
                $"Partial capture: held=200, captured=150, released=50, balance={capture.NewBalance}");
        }, "Partial hold capture");

    // =========================================================================
    // Conversion Tests
    // =========================================================================

    private static async Task<TestResult> TestCurrencyConversion(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            // Create two currencies with exchange rates to base
            var goldDef = await CreateTestCurrencyAsync(currencyClient, "gold");
            var silverDef = await CreateTestCurrencyAsync(currencyClient, "silver");

            // Set exchange rate: GOLD = 100 base, SILVER = 1 base (so 1 GOLD = 100 SILVER)
            await currencyClient.UpdateExchangeRateAsync(new UpdateExchangeRateRequest
            {
                CurrencyDefinitionId = goldDef.DefinitionId,
                ExchangeRateToBase = 100.0
            });
            await currencyClient.UpdateExchangeRateAsync(new UpdateExchangeRateRequest
            {
                CurrencyDefinitionId = silverDef.DefinitionId,
                ExchangeRateToBase = 1.0
            });

            var wallet = await CreateTestWalletAsync(currencyClient, "CONVERT");

            // Credit gold
            await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = goldDef.DefinitionId,
                Amount = 10.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Calculate conversion preview
            var preview = await currencyClient.CalculateConversionAsync(new CalculateConversionRequest
            {
                FromCurrencyId = goldDef.DefinitionId,
                ToCurrencyId = silverDef.DefinitionId,
                FromAmount = 5.0
            });

            // With 100:1 rate, 5 GOLD should equal 500 SILVER
            if (preview.ToAmount < 400.0 || preview.ToAmount > 600.0)
                return TestResult.Failed($"Expected conversion result ~500, got: {preview.ToAmount}");

            // Execute conversion
            var conversion = await currencyClient.ExecuteConversionAsync(new ExecuteConversionRequest
            {
                WalletId = wallet.WalletId,
                FromCurrencyId = goldDef.DefinitionId,
                ToCurrencyId = silverDef.DefinitionId,
                FromAmount = 5.0,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            return TestResult.Successful(
                $"Converted GOLD to SILVER, debited={conversion.FromDebited}, credited={conversion.ToCredited}");
        }, "Currency conversion");

    // =========================================================================
    // Transaction History
    // =========================================================================

    private static async Task<TestResult> TestTransactionHistory(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "history");
            var wallet = await CreateTestWalletAsync(currencyClient, "HISTORY");

            // Perform several transactions
            await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 1000.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            await currencyClient.DebitCurrencyAsync(new DebitCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 100.0,
                TransactionType = TransactionType.Burn,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            await currencyClient.DebitCurrencyAsync(new DebitCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 50.0,
                TransactionType = TransactionType.Burn,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Get history
            var history = await currencyClient.GetTransactionHistoryAsync(new GetTransactionHistoryRequest
            {
                WalletId = wallet.WalletId
            });

            if (history.Transactions == null || history.Transactions.Count < 3)
                return TestResult.Failed($"Expected at least 3 transactions, got: {history.Transactions?.Count ?? 0}");

            return TestResult.Successful(
                $"Transaction history: {history.Transactions.Count} transactions returned");
        }, "Transaction history");

    // =========================================================================
    // Idempotency Tests
    // =========================================================================

    private static async Task<TestResult> TestIdempotentCredit(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "idemp");
            var wallet = await CreateTestWalletAsync(currencyClient, "IDEMP");

            var idempotencyKey = Guid.NewGuid().ToString();

            // First credit
            var first = await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 100.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = idempotencyKey
            });

            if (first.NewBalance != 100.0)
                return TestResult.Failed($"First credit: expected 100, got {first.NewBalance}");

            // Second credit with same key (should be idempotent)
            var second = await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 100.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = idempotencyKey
            });

            // Balance should still be 100, not 200
            if (second.NewBalance != 100.0)
                return TestResult.Failed($"Idempotent credit: expected 100, got {second.NewBalance}");

            // Same transaction ID should be returned
            if (second.Transaction.TransactionId != first.Transaction.TransactionId)
                return TestResult.Failed("Idempotent call should return same transaction ID");

            return TestResult.Successful(
                $"Idempotency verified: balance stayed at {second.NewBalance}, same txn={second.Transaction.TransactionId}");
        }, "Idempotent credit");

    // =========================================================================
    // Additional Coverage Tests
    // =========================================================================

    private static async Task<TestResult> TestUpdateCurrencyDefinition(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "update");

            // Update mutable fields
            var updated = await currencyClient.UpdateCurrencyDefinitionAsync(new UpdateCurrencyDefinitionRequest
            {
                DefinitionId = definition.DefinitionId,
                Name = "Updated Currency Name",
                Description = "Updated description",
                AllowNegative = true
            });

            if (updated.Name != "Updated Currency Name")
                return TestResult.Failed($"Expected updated name, got: {updated.Name}");

            if (updated.Description != "Updated description")
                return TestResult.Failed($"Expected updated description, got: {updated.Description}");

            if (updated.AllowNegative != true)
                return TestResult.Failed("AllowNegative should be true after update");

            return TestResult.Successful(
                $"Updated currency definition: {updated.DefinitionId}");
        }, "Update currency definition");

    private static async Task<TestResult> TestGetWallet(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var ownerId = Guid.NewGuid();
            var created = await currencyClient.CreateWalletAsync(new CreateWalletRequest
            {
                OwnerId = ownerId,
                OwnerType = WalletOwnerType.Account
            });

            // Get by wallet ID
            var byId = await currencyClient.GetWalletAsync(new GetWalletRequest
            {
                WalletId = created.WalletId
            });

            if (byId.Wallet.WalletId != created.WalletId)
                return TestResult.Failed("Get by ID returned wrong wallet");

            // Get by owner
            var byOwner = await currencyClient.GetWalletAsync(new GetWalletRequest
            {
                OwnerId = ownerId,
                OwnerType = WalletOwnerType.Account
            });

            if (byOwner.Wallet.WalletId != created.WalletId)
                return TestResult.Failed("Get by owner returned wrong wallet");

            return TestResult.Successful(
                $"Got wallet by ID and owner: {created.WalletId}");
        }, "Get wallet");

    private static async Task<TestResult> TestCloseWallet(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "close");
            var sourceWallet = await CreateTestWalletAsync(currencyClient, "CLOSE_SRC");
            var destWallet = await CreateTestWalletAsync(currencyClient, "CLOSE_DST");

            // Credit source wallet
            await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = sourceWallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 500.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Close source wallet, transferring to dest
            var closed = await currencyClient.CloseWalletAsync(new CloseWalletRequest
            {
                WalletId = sourceWallet.WalletId,
                TransferRemainingTo = destWallet.WalletId
            });

            if (closed.Wallet.Status != WalletStatus.Closed)
                return TestResult.Failed($"Expected Closed status, got: {closed.Wallet.Status}");

            // Verify destination received the balance
            var destBalance = await currencyClient.GetBalanceAsync(new GetBalanceRequest
            {
                WalletId = destWallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId
            });

            if (destBalance.Amount != 500.0)
                return TestResult.Failed($"Expected destination balance 500, got: {destBalance.Amount}");

            return TestResult.Successful(
                $"Closed wallet {sourceWallet.WalletId}, transferred 500 to {destWallet.WalletId}");
        }, "Close wallet");

    private static async Task<TestResult> TestBatchGetBalances(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition1 = await CreateTestCurrencyAsync(currencyClient, "batch1");
            var definition2 = await CreateTestCurrencyAsync(currencyClient, "batch2");
            var wallet = await CreateTestWalletAsync(currencyClient, "BATCH_BAL");

            // Credit both currencies
            await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition1.DefinitionId,
                Amount = 100.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = Guid.NewGuid().ToString()
            });
            await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition2.DefinitionId,
                Amount = 200.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Batch get
            var batch = await currencyClient.BatchGetBalancesAsync(new BatchGetBalancesRequest
            {
                Queries =
                [
                    new BalanceQuery { WalletId = wallet.WalletId, CurrencyDefinitionId = definition1.DefinitionId },
                    new BalanceQuery { WalletId = wallet.WalletId, CurrencyDefinitionId = definition2.DefinitionId }
                ]
            });

            if (batch.Balances == null || batch.Balances.Count != 2)
                return TestResult.Failed($"Expected 2 results, got: {batch.Balances?.Count ?? 0}");

            return TestResult.Successful(
                $"Batch retrieved {batch.Balances.Count} balances");
        }, "Batch get balances");

    private static async Task<TestResult> TestBatchCreditCurrency(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "batchcred");
            var wallet1 = await CreateTestWalletAsync(currencyClient, "BATCH_CR1");
            var wallet2 = await CreateTestWalletAsync(currencyClient, "BATCH_CR2");

            var batch = await currencyClient.BatchCreditCurrencyAsync(new BatchCreditRequest
            {
                IdempotencyKey = Guid.NewGuid().ToString(),
                Operations =
                [
                    new BatchCreditOperation
                    {
                        WalletId = wallet1.WalletId,
                        CurrencyDefinitionId = definition.DefinitionId,
                        Amount = 100.0,
                        TransactionType = TransactionType.Mint
                    },
                    new BatchCreditOperation
                    {
                        WalletId = wallet2.WalletId,
                        CurrencyDefinitionId = definition.DefinitionId,
                        Amount = 200.0,
                        TransactionType = TransactionType.Mint
                    }
                ]
            });

            if (batch.Results == null || batch.Results.Count != 2)
                return TestResult.Failed($"Expected 2 results, got: {batch.Results?.Count ?? 0}");

            var successCount = batch.Results.Count(r => r.Success);
            if (successCount != 2)
                return TestResult.Failed($"Expected 2 successes, got: {successCount}");

            return TestResult.Successful(
                $"Batch credited {batch.Results.Count} wallets successfully");
        }, "Batch credit currency");

    private static async Task<TestResult> TestGetExchangeRate(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            // Create two currencies with exchange rates
            var gold = await CreateTestCurrencyAsync(currencyClient, "xrgold");
            var silver = await CreateTestCurrencyAsync(currencyClient, "xrsilver");

            // Set exchange rates: GOLD = 100 base, SILVER = 1 base
            await currencyClient.UpdateExchangeRateAsync(new UpdateExchangeRateRequest
            {
                CurrencyDefinitionId = gold.DefinitionId,
                ExchangeRateToBase = 100.0
            });
            await currencyClient.UpdateExchangeRateAsync(new UpdateExchangeRateRequest
            {
                CurrencyDefinitionId = silver.DefinitionId,
                ExchangeRateToBase = 1.0
            });

            var rate = await currencyClient.GetExchangeRateAsync(new GetExchangeRateRequest
            {
                FromCurrencyId = gold.DefinitionId,
                ToCurrencyId = silver.DefinitionId
            });

            // 100:1 rate means rate should be 100
            if (rate.Rate < 90.0 || rate.Rate > 110.0)
                return TestResult.Failed($"Expected rate ~100, got: {rate.Rate}");

            return TestResult.Successful(
                $"Exchange rate: GOLD->SILVER = {rate.Rate}");
        }, "Get exchange rate");

    private static async Task<TestResult> TestGetTransaction(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "gettx");
            var wallet = await CreateTestWalletAsync(currencyClient, "GET_TX");

            // Create a transaction
            var credit = await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 100.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Get transaction by ID
            var tx = await currencyClient.GetTransactionAsync(new GetTransactionRequest
            {
                TransactionId = credit.Transaction.TransactionId
            });

            if (tx.Transaction.TransactionId != credit.Transaction.TransactionId)
                return TestResult.Failed("Transaction ID mismatch");

            if (tx.Transaction.Amount != 100.0)
                return TestResult.Failed($"Expected amount 100, got: {tx.Transaction.Amount}");

            return TestResult.Successful(
                $"Retrieved transaction: {tx.Transaction.TransactionId}");
        }, "Get transaction");

    private static async Task<TestResult> TestGetTransactionsByReference(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "txref");
            var wallet = await CreateTestWalletAsync(currencyClient, "TX_REF");

            var referenceId = Guid.NewGuid();

            // Create multiple transactions with same reference
            await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 50.0,
                TransactionType = TransactionType.Mint,
                ReferenceType = "test_ref",
                ReferenceId = referenceId,
                IdempotencyKey = Guid.NewGuid().ToString()
            });
            await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 75.0,
                TransactionType = TransactionType.Mint,
                ReferenceType = "test_ref",
                ReferenceId = referenceId,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Get by reference
            var txs = await currencyClient.GetTransactionsByReferenceAsync(new GetTransactionsByReferenceRequest
            {
                ReferenceType = "test_ref",
                ReferenceId = referenceId
            });

            if (txs.Transactions == null || txs.Transactions.Count < 2)
                return TestResult.Failed($"Expected at least 2 transactions, got: {txs.Transactions?.Count ?? 0}");

            return TestResult.Successful(
                $"Retrieved {txs.Transactions.Count} transactions by reference");
        }, "Get transactions by reference");

    private static async Task<TestResult> TestEscrowDeposit(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "escdep");
            var wallet = await CreateTestWalletAsync(currencyClient, "ESC_DEP");

            // Credit funds first
            await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 500.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            var escrowId = Guid.NewGuid();

            // Deposit to escrow
            var deposit = await currencyClient.EscrowDepositAsync(new EscrowDepositRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 200.0,
                EscrowId = escrowId,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            if (deposit.NewBalance != 300.0)
                return TestResult.Failed($"Expected balance 300 after deposit, got: {deposit.NewBalance}");

            if (deposit.Transaction.TransactionType != TransactionType.EscrowDeposit)
                return TestResult.Failed($"Expected EscrowDeposit type, got: {deposit.Transaction.TransactionType}");

            return TestResult.Successful(
                $"Escrow deposit: 200 deposited, balance={deposit.NewBalance}");
        }, "Escrow deposit");

    private static async Task<TestResult> TestEscrowRelease(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "escrel");
            var wallet = await CreateTestWalletAsync(currencyClient, "ESC_REL");

            var escrowId = Guid.NewGuid();

            // Release from escrow (credits the wallet)
            var release = await currencyClient.EscrowReleaseAsync(new EscrowReleaseRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 150.0,
                EscrowId = escrowId,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            if (release.NewBalance != 150.0)
                return TestResult.Failed($"Expected balance 150 after release, got: {release.NewBalance}");

            if (release.Transaction.TransactionType != TransactionType.EscrowRelease)
                return TestResult.Failed($"Expected EscrowRelease type, got: {release.Transaction.TransactionType}");

            return TestResult.Successful(
                $"Escrow release: 150 released, balance={release.NewBalance}");
        }, "Escrow release");

    private static async Task<TestResult> TestEscrowRefund(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "escref");
            var wallet = await CreateTestWalletAsync(currencyClient, "ESC_REF");

            var escrowId = Guid.NewGuid();

            // Refund from escrow (credits the wallet)
            var refund = await currencyClient.EscrowRefundAsync(new EscrowRefundRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 100.0,
                EscrowId = escrowId,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            if (refund.NewBalance != 100.0)
                return TestResult.Failed($"Expected balance 100 after refund, got: {refund.NewBalance}");

            if (refund.Transaction.TransactionType != TransactionType.EscrowRefund)
                return TestResult.Failed($"Expected EscrowRefund type, got: {refund.Transaction.TransactionType}");

            return TestResult.Successful(
                $"Escrow refund: 100 refunded, balance={refund.NewBalance}");
        }, "Escrow refund");

    private static async Task<TestResult> TestGetHold(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var currencyClient = GetServiceClient<ICurrencyClient>();

            var definition = await CreateTestCurrencyAsync(currencyClient, "gethold");
            var wallet = await CreateTestWalletAsync(currencyClient, "GET_HOLD");

            // Credit funds
            await currencyClient.CreditCurrencyAsync(new CreditCurrencyRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 500.0,
                TransactionType = TransactionType.Mint,
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Create a hold
            var created = await currencyClient.CreateHoldAsync(new CreateHoldRequest
            {
                WalletId = wallet.WalletId,
                CurrencyDefinitionId = definition.DefinitionId,
                Amount = 100.0,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                IdempotencyKey = Guid.NewGuid().ToString()
            });

            // Get the hold by ID
            var hold = await currencyClient.GetHoldAsync(new GetHoldRequest
            {
                HoldId = created.Hold.HoldId
            });

            if (hold.Hold.HoldId != created.Hold.HoldId)
                return TestResult.Failed("Hold ID mismatch");

            if (hold.Hold.Amount != 100.0)
                return TestResult.Failed($"Expected hold amount 100, got: {hold.Hold.Amount}");

            if (hold.Hold.Status != HoldStatus.Active)
                return TestResult.Failed($"Expected Active status, got: {hold.Hold.Status}");

            return TestResult.Successful(
                $"Retrieved hold: {hold.Hold.HoldId}, amount={hold.Hold.Amount}");
        }, "Get hold");
}
