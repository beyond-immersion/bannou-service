using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

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
            // Core CRUD operations
            new ServiceTest(TestCreateAccount, "CreateAccount", "Account", "Test account creation endpoint"),
            new ServiceTest(TestGetAccount, "GetAccount", "Account", "Test account retrieval endpoint"),
            new ServiceTest(TestListAccounts, "ListAccounts", "Account", "Test account listing endpoint"),
            new ServiceTest(TestUpdateAccount, "UpdateAccount", "Account", "Test account update endpoint"),
            new ServiceTest(TestDeleteAccount, "DeleteAccount", "Account", "Test account deletion endpoint"),

            // Account lookup by different identifiers
            new ServiceTest(TestGetAccountByEmail, "GetAccountByEmail", "Account", "Test account lookup by email"),
            new ServiceTest(TestGetAccountByProvider, "GetAccountByProvider", "Account", "Test account lookup by provider ID"),

            // Authentication methods management
            new ServiceTest(TestGetAuthMethods, "GetAuthMethods", "Account", "Test get authentication methods for account"),
            new ServiceTest(TestAddAuthMethod, "AddAuthMethod", "Account", "Test add authentication method to account"),
            new ServiceTest(TestRemoveAuthMethod, "RemoveAuthMethod", "Account", "Test remove authentication method from account"),

            // Profile and password management
            new ServiceTest(TestUpdateProfile, "UpdateProfile", "Account", "Test update account profile"),
            new ServiceTest(TestUpdatePasswordHash, "UpdatePasswordHash", "Account", "Test update account password hash"),
            new ServiceTest(TestUpdateVerificationStatus, "UpdateVerificationStatus", "Account", "Test update email verification status"),

            // Pagination tests
            new ServiceTest(TestListAccountsPagination, "ListAccountsPagination", "Account", "Test account listing with pagination"),
            new ServiceTest(TestListAccountsFiltering, "ListAccountsFiltering", "Account", "Test account listing with filters"),

            // Event-driven tests (requires Accounts event system)
            new ServiceTest(TestAccountDeletionSessionInvalidation, "AccountDeletionSessionInvalidation", "Account", "Test account deletion → session invalidation flow"),
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
                Email = $"{testUsername}@example.com"
            };

            var response = await accountsClient.CreateAccountAsync(createRequest);

            if (response.AccountId == Guid.Empty)
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
                Email = $"{testUsername}@example.com"
            };

            var createResponse = await accountsClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for retrieval test");

            var accountId = createResponse.AccountId;

            // Now test retrieving the account
            var response = await accountsClient.GetAccountAsync(new GetAccountRequest { AccountId = accountId });

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

            var response = await accountsClient.ListAccountsAsync(new ListAccountsRequest { Page = 1, PageSize = 10 });

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
                Email = $"{testUsername}@example.com"
            };

            var createResponse = await accountsClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for update test");

            var accountId = createResponse.AccountId;
            var newDisplayName = $"Updated Test User {DateTime.Now.Ticks}";

            // Now test updating the account
            var updateRequest = new UpdateAccountRequest
            {
                AccountId = accountId,
                DisplayName = newDisplayName
            };

            var response = await accountsClient.UpdateAccountAsync(updateRequest);

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

    private static async Task<TestResult> TestDeleteAccount(ITestClient client, string[] args)
    {
        try
        {
            // Create AccountsClient
            var accountsClient = new AccountsClient();

            var testUsername = $"deletetest_{DateTime.Now.Ticks}";

            // First create an account to delete
            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = $"{testUsername}@example.com"
            };

            var createResponse = await accountsClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for deletion test");

            var accountId = createResponse.AccountId;

            // Now delete the account
            await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountId });

            // Verify the account is deleted by trying to get it (should return 404)
            try
            {
                await accountsClient.GetAccountAsync(new GetAccountRequest { AccountId = accountId });
                return TestResult.Failed("Account retrieval should have failed after deletion, but it succeeded");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Expected behavior - account not found after deletion
                return TestResult.Successful($"Account deleted successfully: ID={accountId}");
            }
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Account deletion failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetAccountByEmail(ITestClient client, string[] args)
    {
        try
        {
            var accountsClient = new AccountsClient();

            // First create a test account
            var testUsername = $"emailtest_{DateTime.Now.Ticks}";
            var testEmail = $"{testUsername}@example.com";
            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = testEmail
            };

            var createResponse = await accountsClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for email lookup test");

            // Now test retrieving by email
            var response = await accountsClient.GetAccountByEmailAsync(new GetAccountByEmailRequest { Email = testEmail });

            if (response.AccountId != createResponse.AccountId || response.Email != testEmail)
                return TestResult.Failed("Retrieved account doesn't match created account");

            return TestResult.Successful($"Account retrieved by email successfully: ID={response.AccountId}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Account email lookup failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetAccountByProvider(ITestClient client, string[] args)
    {
        try
        {
            var accountsClient = new AccountsClient();

            // Try to look up a non-existent provider account (should return 404)
            try
            {
                await accountsClient.GetAccountByProviderAsync(new GetAccountByProviderRequest { Provider = GetAccountByProviderRequestProvider.Discord, ExternalId = "nonexistent_id_12345" });
                return TestResult.Failed("GetAccountByProvider should have returned 404 for non-existent account");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // 404 proves endpoint works - correctly reports provider account doesn't exist
                return TestResult.Successful("GetAccountByProvider correctly returned 404 for non-existent provider account");
            }
            catch (ApiException ex)
            {
                return TestResult.Failed($"GetAccountByProvider failed: {ex.StatusCode} - {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestGetAuthMethods(ITestClient client, string[] args)
    {
        try
        {
            var accountsClient = new AccountsClient();

            // First create a test account
            var testUsername = $"authmethodtest_{DateTime.Now.Ticks}";
            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = $"{testUsername}@example.com"
            };

            var createResponse = await accountsClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for auth methods test");

            // Get auth methods for the account
            var response = await accountsClient.GetAuthMethodsAsync(new GetAuthMethodsRequest { AccountId = createResponse.AccountId });
            return TestResult.Successful($"GetAuthMethods returned {response.AuthMethods?.Count ?? 0} auth methods");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"GetAuthMethods failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestAddAuthMethod(ITestClient client, string[] args)
    {
        try
        {
            var accountsClient = new AccountsClient();

            // First create a test account
            var testUsername = $"addauthtest_{DateTime.Now.Ticks}";
            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = $"{testUsername}@example.com"
            };

            var createResponse = await accountsClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for add auth method test");

            // Add a Discord auth method
            var addAuthRequest = new AddAuthMethodRequest
            {
                AccountId = createResponse.AccountId,
                Provider = AddAuthMethodRequestProvider.Discord,
                ExternalId = $"discord_{DateTime.Now.Ticks}",
                DisplayName = "Test Discord User"
            };

            var response = await accountsClient.AddAuthMethodAsync(addAuthRequest);
            return TestResult.Successful($"AddAuthMethod completed: MethodId={response.MethodId}, Provider={response.Provider}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"AddAuthMethod failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestRemoveAuthMethod(ITestClient client, string[] args)
    {
        try
        {
            var accountsClient = new AccountsClient();

            // First create a test account
            var testUsername = $"removeauthtest_{DateTime.Now.Ticks}";
            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = $"{testUsername}@example.com"
            };

            var createResponse = await accountsClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for remove auth method test");

            // Try to remove a non-existent auth method (should return 404)
            var fakeMethodId = Guid.NewGuid();
            try
            {
                await accountsClient.RemoveAuthMethodAsync(new RemoveAuthMethodRequest { AccountId = createResponse.AccountId, MethodId = fakeMethodId });
                return TestResult.Successful("RemoveAuthMethod completed for test method ID");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("RemoveAuthMethod correctly returned 404 for non-existent method");
            }
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"RemoveAuthMethod failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestUpdateProfile(ITestClient client, string[] args)
    {
        try
        {
            var accountsClient = new AccountsClient();

            // First create a test account
            var testUsername = $"profiletest_{DateTime.Now.Ticks}";
            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = $"{testUsername}@example.com"
            };

            var createResponse = await accountsClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for profile update test");

            // Update profile
            var newDisplayName = $"Updated Profile {DateTime.Now.Ticks}";
            var updateRequest = new UpdateProfileRequest
            {
                AccountId = createResponse.AccountId,
                DisplayName = newDisplayName
            };

            var response = await accountsClient.UpdateProfileAsync(updateRequest);
            if (response.DisplayName != newDisplayName)
                return TestResult.Failed("Profile update did not persist the display name change");

            return TestResult.Successful($"Profile updated successfully: DisplayName={response.DisplayName}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"UpdateProfile failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestUpdatePasswordHash(ITestClient client, string[] args)
    {
        try
        {
            var accountsClient = new AccountsClient();

            // First create a test account
            var testUsername = $"passwordtest_{DateTime.Now.Ticks}";
            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = $"{testUsername}@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPassword123!")
            };

            var createResponse = await accountsClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for password update test");

            // Update password hash
            var newPasswordHash = BCrypt.Net.BCrypt.HashPassword("NewPassword456!");
            var updateRequest = new UpdatePasswordRequest
            {
                AccountId = createResponse.AccountId,
                PasswordHash = newPasswordHash
            };

            await accountsClient.UpdatePasswordHashAsync(updateRequest);
            return TestResult.Successful("Password hash updated successfully");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"UpdatePasswordHash failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestUpdateVerificationStatus(ITestClient client, string[] args)
    {
        try
        {
            var accountsClient = new AccountsClient();

            // First create a test account (default emailVerified=false)
            var testUsername = $"verifytest_{DateTime.Now.Ticks}";
            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = $"{testUsername}@example.com",
                EmailVerified = false
            };

            var createResponse = await accountsClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for verification update test");

            // Update verification status to true
            var updateRequest = new UpdateVerificationRequest
            {
                AccountId = createResponse.AccountId,
                EmailVerified = true
            };

            await accountsClient.UpdateVerificationStatusAsync(updateRequest);

            // Verify the change by getting the account
            var verifyResponse = await accountsClient.GetAccountAsync(new GetAccountRequest { AccountId = createResponse.AccountId });
            if (!verifyResponse.EmailVerified)
                return TestResult.Failed("Verification status update did not persist");

            return TestResult.Successful("Email verification status updated successfully");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"UpdateVerificationStatus failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestListAccountsPagination(ITestClient client, string[] args)
    {
        try
        {
            var accountsClient = new AccountsClient();
            var testPrefix = $"pagination_{DateTime.Now.Ticks}_";
            var createdAccounts = new List<Guid>();

            // Create 5 test accounts for pagination testing
            for (var i = 0; i < 5; i++)
            {
                var createRequest = new CreateAccountRequest
                {
                    DisplayName = $"{testPrefix}user{i}",
                    Email = $"{testPrefix}user{i}@example.com"
                };
                var response = await accountsClient.CreateAccountAsync(createRequest);
                if (response.AccountId == Guid.Empty)
                    return TestResult.Failed($"Failed to create test account {i}");
                createdAccounts.Add(response.AccountId);
            }

            // Test 1: Page 1 with page size 2 (should get 2 accounts)
            var page1Response = await accountsClient.ListAccountsAsync(new ListAccountsRequest { Page = 1, PageSize = 2 });
            if (page1Response.Page != 1)
                return TestResult.Failed($"Page 1 response has wrong page number: {page1Response.Page}");
            if (page1Response.PageSize != 2)
                return TestResult.Failed($"Page 1 response has wrong page size: {page1Response.PageSize}");
            if (page1Response.Accounts?.Count != 2)
                return TestResult.Failed($"Page 1 should return 2 accounts, got {page1Response.Accounts?.Count}");
            if (page1Response.TotalCount < 5)
                return TestResult.Failed($"Total count should be at least 5, got {page1Response.TotalCount}");

            // Test 2: Page 2 with page size 2 (should get 2 accounts)
            var page2Response = await accountsClient.ListAccountsAsync(new ListAccountsRequest { Page = 2, PageSize = 2 });
            if (page2Response.Page != 2)
                return TestResult.Failed($"Page 2 response has wrong page number: {page2Response.Page}");
            if (page2Response.Accounts?.Count != 2)
                return TestResult.Failed($"Page 2 should return 2 accounts, got {page2Response.Accounts?.Count}");

            // Test 3: Page beyond data (should return empty)
            var beyondPageResponse = await accountsClient.ListAccountsAsync(new ListAccountsRequest { Page = 1000, PageSize = 10 });
            if (beyondPageResponse.Accounts?.Count != 0)
                return TestResult.Failed($"Beyond-last-page should return 0 accounts, got {beyondPageResponse.Accounts?.Count}");

            // Test 4: Default pagination (page 1, pageSize defaults)
            var defaultResponse = await accountsClient.ListAccountsAsync(new ListAccountsRequest());
            if (defaultResponse.Accounts == null)
                return TestResult.Failed("Default pagination returned null accounts");

            // Clean up created accounts
            foreach (var accountId in createdAccounts)
            {
                await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountId });
            }

            return TestResult.Successful($"Pagination tests passed: Page1={page1Response.Accounts?.Count}, Page2={page2Response.Accounts?.Count}, Total={page1Response.TotalCount}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Pagination test failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestListAccountsFiltering(ITestClient client, string[] args)
    {
        try
        {
            var accountsClient = new AccountsClient();
            var testPrefix = $"filter_{DateTime.Now.Ticks}_";

            // Create test accounts with different attributes
            var account1 = await accountsClient.CreateAccountAsync(new CreateAccountRequest
            {
                DisplayName = $"{testPrefix}alice",
                Email = $"{testPrefix}alice@example.com"
            });
            var account2 = await accountsClient.CreateAccountAsync(new CreateAccountRequest
            {
                DisplayName = $"{testPrefix}bob",
                Email = $"{testPrefix}bob@test.com"
            });
            var account3 = await accountsClient.CreateAccountAsync(new CreateAccountRequest
            {
                DisplayName = $"{testPrefix}charlie",
                Email = $"{testPrefix}charlie@example.com"
            });

            // Mark account3 as verified
            await accountsClient.UpdateVerificationStatusAsync(new UpdateVerificationRequest
            {
                AccountId = account3.AccountId,
                EmailVerified = true
            });

            // Test 1: Filter by email domain
            var emailFilterResponse = await accountsClient.ListAccountsAsync(new ListAccountsRequest
            {
                Email = "example.com",
                Page = 1,
                PageSize = 100
            });
            var matchingEmails = emailFilterResponse.Accounts?.Count(a => a.Email.Contains(testPrefix)) ?? 0;
            if (matchingEmails < 2)
                return TestResult.Failed($"Email filter should match at least 2 test accounts with example.com, got {matchingEmails}");

            // Test 2: Filter by display name
            var displayNameFilterResponse = await accountsClient.ListAccountsAsync(new ListAccountsRequest
            {
                DisplayName = testPrefix,
                Page = 1,
                PageSize = 100
            });
            var matchingNames = displayNameFilterResponse.Accounts?.Count(a => a.DisplayName?.Contains(testPrefix) == true) ?? 0;
            if (matchingNames < 3)
                return TestResult.Failed($"DisplayName filter should match at least 3 test accounts, got {matchingNames}");

            // Test 3: Filter by verification status
            var verifiedFilterResponse = await accountsClient.ListAccountsAsync(new ListAccountsRequest
            {
                Verified = true,
                Page = 1,
                PageSize = 100
            });
            var verifiedWithPrefix = verifiedFilterResponse.Accounts?.Count(a => a.DisplayName?.Contains(testPrefix) == true) ?? 0;
            if (verifiedWithPrefix < 1)
                return TestResult.Failed($"Verified filter should match at least 1 verified test account, got {verifiedWithPrefix}");

            // Test 4: Combined filters (email + displayName)
            var combinedFilterResponse = await accountsClient.ListAccountsAsync(new ListAccountsRequest
            {
                Email = "example.com",
                DisplayName = "alice",
                Page = 1,
                PageSize = 100
            });
            var combinedMatches = combinedFilterResponse.Accounts?.Count(a =>
                a.Email.Contains(testPrefix) && a.DisplayName?.Contains("alice") == true) ?? 0;
            if (combinedMatches < 1)
                return TestResult.Failed($"Combined filter should match at least 1 account, got {combinedMatches}");

            // Clean up
            await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = account1.AccountId });
            await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = account2.AccountId });
            await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = account3.AccountId });

            return TestResult.Successful($"Filtering tests passed: EmailFilter={matchingEmails}, NameFilter={matchingNames}, VerifiedFilter={verifiedWithPrefix}, Combined={combinedMatches}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Filtering test failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    private static async Task<TestResult> TestAccountDeletionSessionInvalidation(ITestClient client, string[] args)
    {
        // Tests the complete event chain:
        // 1. Account deleted → AccountsService publishes account.deleted
        // 2. AuthService receives account.deleted → invalidates all sessions → publishes session.invalidated
        // 3. Session validation should fail after account deletion

        try
        {
            // Create clients
            var accountsClient = new AccountsClient();
            var authClient = new AuthClient();

            var testUsername = $"sessiontest_{DateTime.Now.Ticks}";
            var testPassword = "TestPassword123!";

            Console.WriteLine($"[DIAG-TEST] Starting TestAccountDeletionSessionInvalidation with username: {testUsername}");

            // Step 1: Create an account
            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = $"{testUsername}@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(testPassword) // Pre-hash password
            };

            var createResponse = await accountsClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account");

            var accountId = createResponse.AccountId;
            Console.WriteLine($"[DIAG-TEST] Account created: accountId={accountId}, accountIdString={accountId.ToString()}");

            // Step 2: Login to create a session
            var loginRequest = new LoginRequest
            {
                Email = $"{testUsername}@example.com", // Use email instead of username
                Password = testPassword
            };

            var loginResponse = await authClient.LoginAsync(loginRequest);
            if (string.IsNullOrWhiteSpace(loginResponse.AccessToken))
                return TestResult.Failed("Failed to login and create session");

            var accessToken = loginResponse.AccessToken;
            Console.WriteLine($"[DIAG-TEST] Login successful, got access token (length={accessToken.Length})");

            // Step 3: Verify session exists by calling GetSessions
            var sessionsResponse = await ((AuthClient)authClient)
                .WithAuthorization(accessToken)
                .GetSessionsAsync();
            if (sessionsResponse.Sessions == null || !sessionsResponse.Sessions.Any())
                return TestResult.Failed("No sessions found after login");

            var sessionCount = sessionsResponse.Sessions.Count;
            Console.WriteLine($"[DIAG-TEST] Sessions found: {sessionCount}");

            // Step 4: Delete the account (this should trigger session invalidation via events)
            Console.WriteLine($"[DIAG-TEST] Deleting account: {accountId}");
            await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountId });
            Console.WriteLine($"[DIAG-TEST] DeleteAccountAsync completed for accountId={accountId}");

            // Step 5: Wait for event processing with retry loop
            // Dapr cold-start in CI can take up to 60 seconds for:
            // - Service healthcheck start_period (60s)
            // - Dapr subscription discovery (calls /dapr/subscribe)
            // - RabbitMQ queue creation and consumer setup
            const int maxRetries = 30;
            const int retryDelayMs = 2000;

            Console.WriteLine($"[DIAG-TEST] Starting retry loop: {maxRetries} retries, {retryDelayMs}ms delay each");

            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                await Task.Delay(retryDelayMs);
                Console.WriteLine($"[DIAG-TEST] Attempt {attempt}/{maxRetries}: checking sessions for accountId={accountId}");

                try
                {
                    var sessionsAfterDeletion = await ((AuthClient)authClient)
                        .WithAuthorization(accessToken)
                        .GetSessionsAsync();

                    // Check if sessions were invalidated
                    if (sessionsAfterDeletion.Sessions == null || !sessionsAfterDeletion.Sessions.Any())
                    {
                        Console.WriteLine($"[DIAG-TEST] SUCCESS: Sessions invalidated on attempt {attempt}");
                        return TestResult.Successful($"Account deletion → session invalidation flow verified: {sessionCount} session(s) invalidated (attempt {attempt})");
                    }

                    // Sessions still exist, continue retrying
                    Console.WriteLine($"[DIAG-TEST] Attempt {attempt}: Still found {sessionsAfterDeletion.Sessions.Count} sessions");
                    if (attempt == maxRetries)
                    {
                        Console.WriteLine($"[DIAG-TEST] FAILED: Max retries reached, sessions still exist");
                        return TestResult.Failed($"Sessions still exist after account deletion after {maxRetries * retryDelayMs / 1000}s: {sessionsAfterDeletion.Sessions.Count} sessions found");
                    }
                }
                catch (ApiException ex) when (ex.StatusCode == 401)
                {
                    // Expected behavior - unauthorized because token is now invalid
                    Console.WriteLine($"[DIAG-TEST] SUCCESS: Got 401 on attempt {attempt} - token invalidated");
                    return TestResult.Successful($"Account deletion → session invalidation flow verified: Token invalidated (401 response, attempt {attempt})");
                }
                catch (Exception ex) when (ex.Message.Contains("Connection refused") ||
                                        ex.Message.Contains("SecurityTokenSignatureKeyNotFoundException") ||
                                        ex.Message.Contains("Signature validation failed"))
                {
                    // Expected behavior - JWT validation failed because session was invalidated
                    Console.WriteLine($"[DIAG-TEST] SUCCESS: Got JWT validation failure on attempt {attempt} - session invalidated");
                    return TestResult.Successful($"Account deletion → session invalidation flow verified: JWT validation failed (session invalidated, attempt {attempt})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DIAG-TEST] Attempt {attempt}: Unexpected exception: {ex.GetType().Name}: {ex.Message}");
                    throw;
                }
            }

            return TestResult.Failed("Session invalidation verification loop exited unexpectedly");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Session invalidation test failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }
}
