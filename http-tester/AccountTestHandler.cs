using BeyondImmersion.BannouService.Testing;
using Newtonsoft.Json.Linq;

namespace BeyondImmersion.BannouService.HttpTester;

/// <summary>
/// Test handler for account-related API endpoints
/// </summary>
public class AccountTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            new ServiceTest(TestCreateAccount, "CreateAccount", "Account", "Test account creation endpoint"),
            new ServiceTest(TestGetAccount, "GetAccount", "Account", "Test account retrieval endpoint"),
        };
    }

    private static async Task<TestResult> TestCreateAccount(ITestClient client, string[] args)
    {
        try
        {
            var testUsername = $"testuser_{DateTime.Now.Ticks}";
            var requestBody = new
            {
                username = testUsername,
                password = "TestPassword123!",
                email = $"{testUsername}@example.com"
            };

            var response = await client.PostAsync<JObject>("api/accounts/create", requestBody);

            if (!response.Success)
                return TestResult.Failed($"Account creation failed: {response.ErrorMessage}");

            if (response.Data == null)
                return TestResult.Failed("Account creation returned null data");

            var accountId = response.Data["id"]?.Value<int>();
            var username = response.Data["username"]?.Value<string>();

            if (accountId <= 0 || string.IsNullOrWhiteSpace(username))
                return TestResult.Failed("Account creation returned invalid account data");

            return TestResult.Successful($"Account created successfully: ID={accountId}, Username={username}");
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
            // First create a test account
            var testUsername = $"testuser_{DateTime.Now.Ticks}";
            var createBody = new
            {
                username = testUsername,
                password = "TestPassword123!",
                email = $"{testUsername}@example.com"
            };

            var createResponse = await client.PostAsync<JObject>("api/accounts/create", createBody);
            if (!createResponse.Success)
                return TestResult.Failed($"Failed to create test account: {createResponse.ErrorMessage}");

            var accountId = createResponse.Data!["id"]?.Value<int>();
            if (accountId <= 0)
                return TestResult.Failed("Created account has invalid ID");

            // Now test retrieving the account
            var getBody = new
            {
                id = accountId,
                includeClaims = true
            };

            var response = await client.PostAsync<JObject>("api/accounts/get", getBody);

            if (!response.Success)
                return TestResult.Failed($"Account retrieval failed: {response.ErrorMessage}");

            if (response.Data == null)
                return TestResult.Failed("Account retrieval returned null data");

            var retrievedId = response.Data["id"]?.Value<int>();
            var retrievedUsername = response.Data["username"]?.Value<string>();

            if (retrievedId != accountId || retrievedUsername != testUsername)
                return TestResult.Failed("Retrieved account data doesn't match created account");

            return TestResult.Successful($"Account retrieved successfully: ID={retrievedId}, Username={retrievedUsername}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }
}
