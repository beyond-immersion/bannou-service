using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.Accounts.Client;

namespace BeyondImmersion.BannouService.HttpTester;

/// <summary>
/// Test handler for account-related API endpoints using generated clients.
/// Tests the accounts service APIs directly via NSwag-generated AccountsClient.
/// </summary>
public class AccountTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            new ServiceTest(TestCreateAccount, "CreateAccount", "Account", "Test account creation endpoint"),
            new ServiceTest(TestGetAccount, "GetAccount", "Account", "Test account retrieval endpoint"),
            new ServiceTest(TestListAccounts, "ListAccounts", "Account", "Test account listing endpoint"),
            new ServiceTest(TestUpdateAccount, "UpdateAccount", "Account", "Test account update endpoint"),
        };
    }

    private static async Task<TestResult> TestCreateAccount(ITestClient client, string[] args)
    {
        try
        {
            // Create AccountsClient directly with parameterless constructor
            var accountsClient = new AccountsClient();

            var testUsername = $"testuser_{DateTime.Now.Ticks}";

            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = $"{testUsername}@example.com",
                Provider = Provider.Email,
                ExternalId = testUsername,
                EmailVerified = false,
                Roles = new[] { "user" }
            };

            var response = await accountsClient.CreateAccountAsync(createRequest);

            if (response.AccountId == null || response.AccountId == Guid.Empty)
                return TestResult.Failed("Account creation returned invalid account ID");

            return TestResult.Successful($"Account created successfully: ID={response.AccountId}, Email={response.Email}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Account creation failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetAccount(ITestClient client, string[] args)
    {
        try
        {
            // Create AccountsClient directly with parameterless constructor
            var accountsClient = new AccountsClient();

            // First create a test account
            var testUsername = $"testuser_{DateTime.Now.Ticks}";
            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = $"{testUsername}@example.com",
                Provider = Provider.Email,
                ExternalId = testUsername,
                EmailVerified = false,
                Roles = new[] { "user" }
            };

            var createResponse = await accountsClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == null || createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for retrieval test");

            var accountId = createResponse.AccountId.Value;

            // Now test retrieving the account
            var response = await accountsClient.GetAccountAsync(accountId);

            if (response.AccountId != accountId || response.Email != createRequest.Email)
                return TestResult.Failed("Retrieved account data doesn't match created account");

            return TestResult.Successful($"Account retrieved successfully: ID={response.AccountId}, Email={response.Email}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Account retrieval failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestListAccounts(ITestClient client, string[] args)
    {
        try
        {
            // Create AccountsClient directly with parameterless constructor
            var accountsClient = new AccountsClient();

            var response = await accountsClient.ListAccountsAsync(page: 1, pageSize: 10);

            return TestResult.Successful($"Account listing successful: Found {response.TotalCount} total accounts, returned {response.Accounts?.Count ?? 0} accounts");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Account listing failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestUpdateAccount(ITestClient client, string[] args)
    {
        try
        {
            // Create AccountsClient directly with parameterless constructor
            var accountsClient = new AccountsClient();

            // First create a test account
            var testUsername = $"testuser_{DateTime.Now.Ticks}";
            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = $"{testUsername}@example.com",
                Provider = Provider.Email,
                ExternalId = testUsername,
                EmailVerified = false,
                Roles = new[] { "user" }
            };

            var createResponse = await accountsClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == null || createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for update test");

            var accountId = createResponse.AccountId.Value;
            var newDisplayName = $"Updated Test User {DateTime.Now.Ticks}";

            // Now test updating the account
            var updateRequest = new UpdateAccountRequest
            {
                DisplayName = newDisplayName
            };

            var response = await accountsClient.UpdateAccountAsync(accountId, updateRequest);

            if (response.DisplayName != newDisplayName)
                return TestResult.Failed("Account update did not persist the display name change");

            return TestResult.Successful($"Account updated successfully: ID={response.AccountId}, New DisplayName={response.DisplayName}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Account update failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }
}