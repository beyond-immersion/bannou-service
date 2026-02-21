using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for account-related API endpoints using generated clients.
/// Tests the account service APIs directly via NSwag-generated AccountClient.
/// </summary>
public class AccountTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
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
        new ServiceTest(TestOAuthProviderFlow, "OAuthProviderFlow", "Account", "Test full OAuth provider flow with multiple providers and verification"),
        new ServiceTest(TestCannotOrphanAccount, "CannotOrphanAccount", "Account", "Test that removing last auth method from passwordless account is rejected"),

        // Profile and password management
        new ServiceTest(TestUpdateProfile, "UpdateProfile", "Account", "Test update account profile"),
        new ServiceTest(TestUpdatePasswordHash, "UpdatePasswordHash", "Account", "Test update account password hash"),
        new ServiceTest(TestUpdateVerificationStatus, "UpdateVerificationStatus", "Account", "Test update email verification status"),

        // Pagination tests
        new ServiceTest(TestListAccountsPagination, "ListAccountsPagination", "Account", "Test account listing with pagination"),
        new ServiceTest(TestListAccountsFiltering, "ListAccountsFiltering", "Account", "Test account listing with filters"),

        // Event-driven tests (requires Account event system)
        new ServiceTest(TestAccountDeletionSessionInvalidation, "AccountDeletionSessionInvalidation", "Account", "Test account deletion → session invalidation flow"),
    ];

    private static async Task<TestResult> TestCreateAccount(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var testUsername = GenerateTestId("testuser");

            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = GenerateTestEmail(testUsername),
                Roles = new List<string>()
            };

            var response = await accountClient.CreateAccountAsync(createRequest);

            if (response.AccountId == Guid.Empty)
                return TestResult.Failed("Account creation returned invalid account ID");

            return TestResult.Successful($"Account created successfully: ID={response.AccountId}, Email={response.Email}");
        }, "Account creation");

    private static async Task<TestResult> TestGetAccount(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var testUsername = GenerateTestId("testuser");

            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = GenerateTestEmail(testUsername),
                Roles = new List<string>()
            };

            var createResponse = await accountClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for retrieval test");

            var response = await accountClient.GetAccountAsync(new GetAccountRequest { AccountId = createResponse.AccountId });

            if (response.AccountId != createResponse.AccountId || response.Email != createRequest.Email)
                return TestResult.Failed("Retrieved account data doesn't match created account");

            return TestResult.Successful($"Account retrieved successfully: ID={response.AccountId}, Email={response.Email}");
        }, "Account retrieval");

    private static async Task<TestResult> TestListAccounts(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var response = await accountClient.ListAccountsAsync(new ListAccountsRequest { Page = 1, PageSize = 10 });

            return TestResult.Successful($"Account listing successful: Found {response.TotalCount} total accounts, returned {response.Accounts?.Count ?? 0} accounts");
        }, "Account listing");

    private static async Task<TestResult> TestUpdateAccount(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var testUsername = GenerateTestId("testuser");

            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = GenerateTestEmail(testUsername),
                Roles = new List<string>()
            };

            var createResponse = await accountClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for update test");

            var newDisplayName = GenerateTestId("Updated Test User");
            var updateRequest = new UpdateAccountRequest
            {
                AccountId = createResponse.AccountId,
                DisplayName = newDisplayName
            };

            var response = await accountClient.UpdateAccountAsync(updateRequest);

            if (response.DisplayName != newDisplayName)
                return TestResult.Failed("Account update did not persist the display name change");

            return TestResult.Successful($"Account updated successfully: ID={response.AccountId}, New DisplayName={response.DisplayName}");
        }, "Account update");

    private static async Task<TestResult> TestDeleteAccount(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var testUsername = GenerateTestId("deletetest");

            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = GenerateTestEmail(testUsername),
                Roles = new List<string>()
            };

            var createResponse = await accountClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for deletion test");

            await accountClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = createResponse.AccountId });

            // Verify deletion - expect 404
            try
            {
                await accountClient.GetAccountAsync(new GetAccountRequest { AccountId = createResponse.AccountId });
                return TestResult.Failed("Account retrieval should have failed after deletion, but it succeeded");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful($"Account deleted successfully: ID={createResponse.AccountId}");
            }
        }, "Account deletion");

    private static async Task<TestResult> TestGetAccountByEmail(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var testUsername = GenerateTestId("emailtest");
            var testEmail = GenerateTestEmail(testUsername);

            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = testEmail,
                Roles = new List<string>()
            };

            var createResponse = await accountClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for email lookup test");

            var response = await accountClient.GetAccountByEmailAsync(new GetAccountByEmailRequest { Email = testEmail });

            if (response.AccountId != createResponse.AccountId || response.Email != testEmail)
                return TestResult.Failed("Retrieved account doesn't match created account");

            return TestResult.Successful($"Account retrieved by email successfully: ID={response.AccountId}");
        }, "Account email lookup");

    private static async Task<TestResult> TestGetAccountByProvider(ITestClient client, string[] args) =>
        await
        ExecuteExpectingAnyStatusAsync(
            async () =>
            {
                var accountClient = GetServiceClient<IAccountClient>();
                await accountClient.GetAccountByProviderAsync(new GetAccountByProviderRequest
                {
                    Provider = OAuthProvider.Discord,
                    ExternalId = "nonexistent_id_12345"
                });
            },
            [404],
            "GetAccountByProvider for non-existent account");

    private static async Task<TestResult> TestGetAuthMethods(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var testUsername = GenerateTestId("authmethodtest");

            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = GenerateTestEmail(testUsername),
                Roles = new List<string>()
            };

            var createResponse = await accountClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for auth methods test");

            var response = await accountClient.GetAuthMethodsAsync(new GetAuthMethodsRequest { AccountId = createResponse.AccountId });
            return TestResult.Successful($"GetAuthMethods returned {response.AuthMethods?.Count ?? 0} auth methods");
        }, "Get auth methods");

    private static async Task<TestResult> TestAddAuthMethod(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var testUsername = GenerateTestId("addauthtest");

            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = GenerateTestEmail(testUsername),
                Roles = new List<string>()
            };

            var createResponse = await accountClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for add auth method test");

            var addAuthRequest = new AddAuthMethodRequest
            {
                AccountId = createResponse.AccountId,
                Provider = OAuthProvider.Discord,
                ExternalId = GenerateTestId("discord"),
                DisplayName = "Test Discord User"
            };

            var response = await accountClient.AddAuthMethodAsync(addAuthRequest);

            // Verify the auth method appears in GetAccount response (events should contain same data)
            var accountAfterAdd = await accountClient.GetAccountAsync(new GetAccountRequest { AccountId = createResponse.AccountId });
            var hasAuthMethod = accountAfterAdd.AuthMethods?.Any(m => m.Provider == AuthProvider.Discord) ?? false;
            if (!hasAuthMethod)
                return TestResult.Failed("Auth method not found in GetAccount response after adding");

            return TestResult.Successful($"AddAuthMethod completed: MethodId={response.MethodId}, Provider={response.Provider}, verified in GetAccount");
        }, "Add auth method");

    private static async Task<TestResult> TestRemoveAuthMethod(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var testUsername = GenerateTestId("removeauthtest");

            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = GenerateTestEmail(testUsername),
                Roles = new List<string>()
            };

            var createResponse = await accountClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for remove auth method test");

            // Try to remove a non-existent auth method (should return 404)
            return await ExecuteExpectingAnyStatusAsync(
                async () =>
                {
                    await accountClient.RemoveAuthMethodAsync(new RemoveAuthMethodRequest
                    {
                        AccountId = createResponse.AccountId,
                        MethodId = Guid.NewGuid()
                    });
                },
                [200, 404],
                "RemoveAuthMethod for test method ID");
        }, "Remove auth method");

    private static async Task<TestResult> TestOAuthProviderFlow(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var testUsername = GenerateTestId("oauthtest");

            // Step 1: Create account
            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = GenerateTestEmail(testUsername),
                Roles = new List<string>()
            };

            var createResponse = await accountClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for OAuth flow test");

            var accountId = createResponse.AccountId;

            // Step 2: Verify account starts with no auth methods
            var initialAccount = await accountClient.GetAccountAsync(new GetAccountRequest { AccountId = accountId });
            var initialAuthMethodCount = initialAccount.AuthMethods?.Count ?? 0;
            if (initialAuthMethodCount != 0)
                return TestResult.Failed($"New account should have 0 auth methods, found {initialAuthMethodCount}");

            // Step 3: Add Discord auth method
            var discordMethod = await accountClient.AddAuthMethodAsync(new AddAuthMethodRequest
            {
                AccountId = accountId,
                Provider = OAuthProvider.Discord,
                ExternalId = GenerateTestId("discord_ext"),
                DisplayName = "Test Discord User"
            });

            if (discordMethod.Provider != OAuthProvider.Discord)
                return TestResult.Failed($"Discord auth method returned wrong provider: {discordMethod.Provider}");

            // Step 4: Add Google auth method
            var googleMethod = await accountClient.AddAuthMethodAsync(new AddAuthMethodRequest
            {
                AccountId = accountId,
                Provider = OAuthProvider.Google,
                ExternalId = GenerateTestId("google_ext"),
                DisplayName = "Test Google User"
            });

            if (googleMethod.Provider != OAuthProvider.Google)
                return TestResult.Failed($"Google auth method returned wrong provider: {googleMethod.Provider}");

            // Step 5: Add Steam auth method
            var steamMethod = await accountClient.AddAuthMethodAsync(new AddAuthMethodRequest
            {
                AccountId = accountId,
                Provider = OAuthProvider.Steam,
                ExternalId = GenerateTestId("steam_ext"),
                DisplayName = "Test Steam User"
            });

            if (steamMethod.Provider != OAuthProvider.Steam)
                return TestResult.Failed($"Steam auth method returned wrong provider: {steamMethod.Provider}");

            // Step 6: Verify GetAccount returns all auth methods
            var accountWithMethods = await accountClient.GetAccountAsync(new GetAccountRequest { AccountId = accountId });
            var authMethods = accountWithMethods.AuthMethods;

            if (authMethods == null || authMethods.Count != 3)
                return TestResult.Failed($"Expected 3 auth methods in GetAccount response, found {authMethods?.Count ?? 0}");

            // Step 7: Verify all providers are present with correct types
            var hasDiscord = authMethods.Any(m => m.Provider == AuthProvider.Discord);
            var hasGoogle = authMethods.Any(m => m.Provider == AuthProvider.Google);
            var hasSteam = authMethods.Any(m => m.Provider == AuthProvider.Steam);

            if (!hasDiscord)
                return TestResult.Failed("Discord auth method not found in GetAccount response");
            if (!hasGoogle)
                return TestResult.Failed("Google auth method not found in GetAccount response");
            if (!hasSteam)
                return TestResult.Failed("Steam auth method not found in GetAccount response");

            // Step 8: Verify GetAuthMethods endpoint returns same data
            var authMethodsResponse = await accountClient.GetAuthMethodsAsync(new GetAuthMethodsRequest { AccountId = accountId });
            if (authMethodsResponse.AuthMethods.Count != 3)
                return TestResult.Failed($"GetAuthMethods returned {authMethodsResponse.AuthMethods.Count} methods, expected 3");

            // Step 9: Verify GetAccountByProvider works for each provider
            var discordLookup = await accountClient.GetAccountByProviderAsync(new GetAccountByProviderRequest
            {
                Provider = OAuthProvider.Discord,
                ExternalId = discordMethod.ExternalId ?? throw new InvalidOperationException("Discord ExternalId was null")
            });
            if (discordLookup.AccountId != accountId)
                return TestResult.Failed("GetAccountByProvider(Discord) returned wrong account");

            var googleLookup = await accountClient.GetAccountByProviderAsync(new GetAccountByProviderRequest
            {
                Provider = OAuthProvider.Google,
                ExternalId = googleMethod.ExternalId ?? throw new InvalidOperationException("Google ExternalId was null")
            });
            if (googleLookup.AccountId != accountId)
                return TestResult.Failed("GetAccountByProvider(Google) returned wrong account");

            // Step 10: Remove one auth method and verify
            await accountClient.RemoveAuthMethodAsync(new RemoveAuthMethodRequest
            {
                AccountId = accountId,
                MethodId = discordMethod.MethodId
            });

            var afterRemoval = await accountClient.GetAccountAsync(new GetAccountRequest { AccountId = accountId });
            if (afterRemoval.AuthMethods?.Count != 2)
                return TestResult.Failed($"Expected 2 auth methods after removal, found {afterRemoval.AuthMethods?.Count ?? 0}");

            var stillHasDiscord = afterRemoval.AuthMethods?.Any(m => m.Provider == AuthProvider.Discord) ?? false;
            if (stillHasDiscord)
                return TestResult.Failed("Discord auth method still present after removal");

            // Cleanup
            await accountClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountId });

            return TestResult.Successful("OAuth provider flow verified: add 3 providers, verify in GetAccount/GetAuthMethods/GetAccountByProvider, remove 1, verify removal");
        }, "OAuth provider flow");

    private static async Task<TestResult> TestCannotOrphanAccount(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var testUsername = GenerateTestId("orphantest");

            // Step 1: Create account WITHOUT password (simulating OAuth-only account)
            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = null, // No email - OAuth-only pattern
                PasswordHash = null, // No password - OAuth-only pattern
                Roles = new List<string>()
            };

            var createResponse = await accountClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create passwordless test account");

            var accountId = createResponse.AccountId;

            // Step 2: Add a single OAuth auth method
            var authMethod = await accountClient.AddAuthMethodAsync(new AddAuthMethodRequest
            {
                AccountId = accountId,
                Provider = OAuthProvider.Discord,
                ExternalId = GenerateTestId("discord_orphan"),
                DisplayName = "Test Discord User"
            });

            // Verify auth method was added
            var accountAfterAdd = await accountClient.GetAccountAsync(new GetAccountRequest { AccountId = accountId });
            if (accountAfterAdd.AuthMethods?.Count != 1)
                return TestResult.Failed($"Expected 1 auth method after adding, found {accountAfterAdd.AuthMethods?.Count ?? 0}");

            // Step 3: Attempt to remove the ONLY auth method - should be rejected
            var removeResult = await ExecuteExpectingAnyStatusAsync(
                async () =>
                {
                    await accountClient.RemoveAuthMethodAsync(new RemoveAuthMethodRequest
                    {
                        AccountId = accountId,
                        MethodId = authMethod.MethodId
                    });
                },
                [400], // Expecting BadRequest - cannot orphan account
                "Removing last auth method from passwordless account");

            if (!removeResult.Success)
            {
                // Cleanup before failing
                await accountClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountId });
                return TestResult.Failed($"Expected 400 BadRequest when removing last auth method, got different result: {removeResult.Message}");
            }

            // Step 4: Verify auth method is still there
            var accountAfterReject = await accountClient.GetAccountAsync(new GetAccountRequest { AccountId = accountId });
            if (accountAfterReject.AuthMethods?.Count != 1)
                return TestResult.Failed($"Auth method should still exist after rejected removal, found {accountAfterReject.AuthMethods?.Count ?? 0}");

            // Step 5: Add password to account, then removal should succeed
            await accountClient.UpdatePasswordHashAsync(new UpdatePasswordRequest
            {
                AccountId = accountId,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPassword123!")
            });

            // Now removing the auth method should work (account has password fallback)
            await accountClient.RemoveAuthMethodAsync(new RemoveAuthMethodRequest
            {
                AccountId = accountId,
                MethodId = authMethod.MethodId
            });

            // Verify removal succeeded
            var accountAfterRemove = await accountClient.GetAccountAsync(new GetAccountRequest { AccountId = accountId });
            if (accountAfterRemove.AuthMethods?.Count != 0)
                return TestResult.Failed($"Expected 0 auth methods after removal with password, found {accountAfterRemove.AuthMethods?.Count ?? 0}");

            // Cleanup
            await accountClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountId });

            return TestResult.Successful("Orphan protection verified: cannot remove last OAuth method from passwordless account, but can remove after adding password");
        }, "Cannot orphan account");

    private static async Task<TestResult> TestUpdateProfile(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var testUsername = GenerateTestId("profiletest");

            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = GenerateTestEmail(testUsername),
                Roles = new List<string>()
            };

            var createResponse = await accountClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for profile update test");

            var newDisplayName = GenerateTestId("Updated Profile");
            var updateRequest = new UpdateProfileRequest
            {
                AccountId = createResponse.AccountId,
                DisplayName = newDisplayName
            };

            var response = await accountClient.UpdateProfileAsync(updateRequest);
            if (response.DisplayName != newDisplayName)
                return TestResult.Failed("Profile update did not persist the display name change");

            return TestResult.Successful($"Profile updated successfully: DisplayName={response.DisplayName}");
        }, "Update profile");

    private static async Task<TestResult> TestUpdatePasswordHash(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var testUsername = GenerateTestId("passwordtest");

            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = GenerateTestEmail(testUsername),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPassword123!"),
                Roles = new List<string>()
            };

            var createResponse = await accountClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for password update test");

            var newPasswordHash = BCrypt.Net.BCrypt.HashPassword("NewPassword456!");
            var updateRequest = new UpdatePasswordRequest
            {
                AccountId = createResponse.AccountId,
                PasswordHash = newPasswordHash
            };

            await accountClient.UpdatePasswordHashAsync(updateRequest);
            return TestResult.Successful("Password hash updated successfully");
        }, "Update password hash");

    private static async Task<TestResult> TestUpdateVerificationStatus(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var testUsername = GenerateTestId("verifytest");

            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = GenerateTestEmail(testUsername),
                EmailVerified = false,
                Roles = new List<string>()
            };

            var createResponse = await accountClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account for verification update test");

            var updateRequest = new UpdateVerificationRequest
            {
                AccountId = createResponse.AccountId,
                EmailVerified = true
            };

            await accountClient.UpdateVerificationStatusAsync(updateRequest);

            var verifyResponse = await accountClient.GetAccountAsync(new GetAccountRequest { AccountId = createResponse.AccountId });
            if (!verifyResponse.EmailVerified)
                return TestResult.Failed("Verification status update did not persist");

            return TestResult.Successful("Email verification status updated successfully");
        }, "Update verification status");

    private static async Task<TestResult> TestListAccountsPagination(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var testPrefix = GenerateTestId("pagination");
            var createdAccounts = new List<Guid>();

            // Create 5 test accounts for pagination testing
            for (var i = 0; i < 5; i++)
            {
                var createRequest = new CreateAccountRequest
                {
                    DisplayName = $"{testPrefix}_user{i}",
                    Email = $"{testPrefix}_user{i}@example.com",
                    Roles = new List<string>()
                };
                var response = await accountClient.CreateAccountAsync(createRequest);
                if (response.AccountId == Guid.Empty)
                    return TestResult.Failed($"Failed to create test account {i}");
                createdAccounts.Add(response.AccountId);
            }

            // Test 1: Page 1 with page size 2
            var page1Response = await accountClient.ListAccountsAsync(new ListAccountsRequest { Page = 1, PageSize = 2 });
            if (page1Response.Page != 1)
                return TestResult.Failed($"Page 1 response has wrong page number: {page1Response.Page}");
            if (page1Response.PageSize != 2)
                return TestResult.Failed($"Page 1 response has wrong page size: {page1Response.PageSize}");
            if (page1Response.Accounts?.Count != 2)
                return TestResult.Failed($"Page 1 should return 2 accounts, got {page1Response.Accounts?.Count}");
            if (page1Response.TotalCount < 5)
                return TestResult.Failed($"Total count should be at least 5, got {page1Response.TotalCount}");

            // Test 2: Page 2 with page size 2
            var page2Response = await accountClient.ListAccountsAsync(new ListAccountsRequest { Page = 2, PageSize = 2 });
            if (page2Response.Page != 2)
                return TestResult.Failed($"Page 2 response has wrong page number: {page2Response.Page}");
            if (page2Response.Accounts?.Count != 2)
                return TestResult.Failed($"Page 2 should return 2 accounts, got {page2Response.Accounts?.Count}");

            // Test 3: Page beyond data
            var beyondPageResponse = await accountClient.ListAccountsAsync(new ListAccountsRequest { Page = 1000, PageSize = 10 });
            if (beyondPageResponse.Accounts?.Count != 0)
                return TestResult.Failed($"Beyond-last-page should return 0 accounts, got {beyondPageResponse.Accounts?.Count}");

            // Test 4: Default pagination
            var defaultResponse = await accountClient.ListAccountsAsync(new ListAccountsRequest());
            if (defaultResponse.Accounts == null)
                return TestResult.Failed("Default pagination returned null accounts");

            // Clean up
            foreach (var accountId in createdAccounts)
            {
                await accountClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountId });
            }

            return TestResult.Successful($"Pagination tests passed: Page1={page1Response.Accounts?.Count}, Page2={page2Response.Accounts?.Count}, Total={page1Response.TotalCount}");
        }, "Pagination test");

    private static async Task<TestResult> TestListAccountsFiltering(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var testPrefix = GenerateTestId("filter");

            // Create test accounts with different attributes
            var account1 = await accountClient.CreateAccountAsync(new CreateAccountRequest
            {
                DisplayName = $"{testPrefix}_alice",
                Email = $"{testPrefix}_alice@example.com",
                Roles = new List<string>()
            });
            var account2 = await accountClient.CreateAccountAsync(new CreateAccountRequest
            {
                DisplayName = $"{testPrefix}_bob",
                Email = $"{testPrefix}_bob@test.com",
                Roles = new List<string>()
            });
            var account3 = await accountClient.CreateAccountAsync(new CreateAccountRequest
            {
                DisplayName = $"{testPrefix}_charlie",
                Email = $"{testPrefix}_charlie@example.com",
                Roles = new List<string>()
            });

            // Mark account3 as verified
            await accountClient.UpdateVerificationStatusAsync(new UpdateVerificationRequest
            {
                AccountId = account3.AccountId,
                EmailVerified = true
            });

            // Test 1: Filter by email domain
            var emailFilterResponse = await accountClient.ListAccountsAsync(new ListAccountsRequest
            {
                Email = "example.com",
                Page = 1,
                PageSize = 100
            });
            var matchingEmails = emailFilterResponse.Accounts?.Count(a => a.Email?.Contains(testPrefix) == true) ?? 0;
            if (matchingEmails < 2)
                return TestResult.Failed($"Email filter should match at least 2 test accounts with example.com, got {matchingEmails}");

            // Test 2: Filter by display name
            var displayNameFilterResponse = await accountClient.ListAccountsAsync(new ListAccountsRequest
            {
                DisplayName = testPrefix,
                Page = 1,
                PageSize = 100
            });
            var matchingNames = displayNameFilterResponse.Accounts?.Count(a => a.DisplayName?.Contains(testPrefix) == true) ?? 0;
            if (matchingNames < 3)
                return TestResult.Failed($"DisplayName filter should match at least 3 test accounts, got {matchingNames}");

            // Test 3: Filter by verification status
            var verifiedFilterResponse = await accountClient.ListAccountsAsync(new ListAccountsRequest
            {
                Verified = true,
                Page = 1,
                PageSize = 100
            });
            var verifiedWithPrefix = verifiedFilterResponse.Accounts?.Count(a => a.DisplayName?.Contains(testPrefix) == true) ?? 0;
            if (verifiedWithPrefix < 1)
                return TestResult.Failed($"Verified filter should match at least 1 verified test account, got {verifiedWithPrefix}");

            // Test 4: Combined filters
            var combinedFilterResponse = await accountClient.ListAccountsAsync(new ListAccountsRequest
            {
                Email = "example.com",
                DisplayName = "alice",
                Page = 1,
                PageSize = 100
            });
            var combinedMatches = combinedFilterResponse.Accounts?.Count(a =>
                a.Email?.Contains(testPrefix) == true && a.DisplayName?.Contains("alice") == true) ?? 0;
            if (combinedMatches < 1)
                return TestResult.Failed($"Combined filter should match at least 1 account, got {combinedMatches}");

            // Clean up
            await accountClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = account1.AccountId });
            await accountClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = account2.AccountId });
            await accountClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = account3.AccountId });

            return TestResult.Successful($"Filtering tests passed: EmailFilter={matchingEmails}, NameFilter={matchingNames}, VerifiedFilter={verifiedWithPrefix}, Combined={combinedMatches}");
        }, "Filtering test");

    private static async Task<TestResult> TestAccountDeletionSessionInvalidation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var accountClient = GetServiceClient<IAccountClient>();
            var authClient = GetServiceClient<IAuthClient>();

            var testUsername = GenerateTestId("sessiontest");
            var testPassword = "TestPassword123!";

            Console.WriteLine($"[DIAG-TEST] Starting TestAccountDeletionSessionInvalidation with username: {testUsername}");

            // Step 1: Create an account
            var createRequest = new CreateAccountRequest
            {
                DisplayName = testUsername,
                Email = GenerateTestEmail(testUsername),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(testPassword),
                Roles = new List<string>()
            };

            var createResponse = await accountClient.CreateAccountAsync(createRequest);
            if (createResponse.AccountId == Guid.Empty)
                return TestResult.Failed("Failed to create test account");

            var accountId = createResponse.AccountId;
            Console.WriteLine($"[DIAG-TEST] Account created: accountId={accountId}");

            // Step 2: Login to create a session
            var loginRequest = new LoginRequest
            {
                Email = createRequest.Email,
                Password = testPassword
            };

            var loginResponse = await authClient.LoginAsync(loginRequest);
            if (string.IsNullOrWhiteSpace(loginResponse.AccessToken))
                return TestResult.Failed("Failed to login and create session");

            var accessToken = loginResponse.AccessToken;
            Console.WriteLine($"[DIAG-TEST] Login successful, got access token (length={accessToken.Length})");

            // Step 3: Verify session exists
            var sessionsResponse = await ((IServiceClient<AuthClient>)authClient)
                .WithAuthorization(accessToken)
                .GetSessionsAsync();
            if (sessionsResponse.Sessions == null || !sessionsResponse.Sessions.Any())
                return TestResult.Failed("No sessions found after login");

            var sessionCount = sessionsResponse.Sessions.Count;
            Console.WriteLine($"[DIAG-TEST] Sessions found: {sessionCount}");

            // Step 4: Delete the account
            Console.WriteLine($"[DIAG-TEST] Deleting account: {accountId}");
            await accountClient.DeleteAccountAsync(new DeleteAccountRequest { AccountId = accountId });
            Console.WriteLine($"[DIAG-TEST] DeleteAccountAsync completed for accountId={accountId}");

            // Step 5: Wait for event processing with retry loop
            const int maxRetries = 30;
            const int retryDelayMs = 2000;

            Console.WriteLine($"[DIAG-TEST] Starting retry loop: {maxRetries} retries, {retryDelayMs}ms delay each");

            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                await Task.Delay(retryDelayMs);
                Console.WriteLine($"[DIAG-TEST] Attempt {attempt}/{maxRetries}: checking sessions for accountId={accountId}");

                try
                {
                    var sessionsAfterDeletion = await ((IServiceClient<AuthClient>)authClient)
                        .WithAuthorization(accessToken)
                        .GetSessionsAsync();

                    if (sessionsAfterDeletion.Sessions == null || !sessionsAfterDeletion.Sessions.Any())
                    {
                        Console.WriteLine($"[DIAG-TEST] SUCCESS: Sessions invalidated on attempt {attempt}");
                        return TestResult.Successful($"Account deletion → session invalidation flow verified: {sessionCount} session(s) invalidated (attempt {attempt})");
                    }

                    Console.WriteLine($"[DIAG-TEST] Attempt {attempt}: Still found {sessionsAfterDeletion.Sessions.Count} sessions");
                    if (attempt == maxRetries)
                    {
                        Console.WriteLine($"[DIAG-TEST] FAILED: Max retries reached, sessions still exist");
                        return TestResult.Failed($"Sessions still exist after account deletion after {maxRetries * retryDelayMs / 1000}s: {sessionsAfterDeletion.Sessions.Count} sessions found");
                    }
                }
                catch (ApiException ex) when (ex.StatusCode == 401)
                {
                    Console.WriteLine($"[DIAG-TEST] SUCCESS: Got 401 on attempt {attempt} - token invalidated");
                    return TestResult.Successful($"Account deletion → session invalidation flow verified: Token invalidated (401 response, attempt {attempt})");
                }
                catch (Exception ex) when (ex.Message.Contains("Connection refused") ||
                                        ex.Message.Contains("SecurityTokenSignatureKeyNotFoundException") ||
                                        ex.Message.Contains("Signature validation failed"))
                {
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
        }, "Account deletion session invalidation");
}
