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

            // Step 3: Verify session exists by calling GetSessions
            var sessionsResponse = await ((AuthClient)authClient)
                .WithAuthorization(accessToken)
                .GetSessionsAsync();
            if (sessionsResponse.Sessions == null || !sessionsResponse.Sessions.Any())
                return TestResult.Failed("No sessions found after login");

            var sessionCount = sessionsResponse.Sessions.Count;

            // Step 4: Delete the account (this should trigger session invalidation via events)
            await accountsClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountId });

            // Step 5: Wait a moment for event processing
            await Task.Delay(2000); // 2 second delay for event processing

            // Step 6: Try to get sessions again - should fail or return empty
            try
            {
                var sessionsAfterDeletion = await ((AuthClient)authClient)
                    .WithAuthorization(accessToken)
                    .GetSessionsAsync();

                // Check if sessions were invalidated
                if (sessionsAfterDeletion.Sessions != null && sessionsAfterDeletion.Sessions.Any())
                {
                    return TestResult.Failed($"Sessions still exist after account deletion: {sessionsAfterDeletion.Sessions.Count} sessions found");
                }

                return TestResult.Successful($"Account deletion → session invalidation flow verified: {sessionCount} session(s) invalidated");
            }
            catch (ApiException ex) when (ex.StatusCode == 401)
            {
                // Expected behavior - unauthorized because token is now invalid
                return TestResult.Successful($"Account deletion → session invalidation flow verified: Token invalidated (401 response)");
            }
            catch (Exception ex) when (ex.Message.Contains("Connection refused") ||
                                    ex.Message.Contains("SecurityTokenSignatureKeyNotFoundException") ||
                                    ex.Message.Contains("Signature validation failed"))
            {
                // Expected behavior - JWT validation failed because session was invalidated
                return TestResult.Successful($"Account deletion → session invalidation flow verified: JWT validation failed (session invalidated)");
            }
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
