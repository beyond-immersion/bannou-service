using BeyondImmersion.BannouService.Authorization.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Authorization.Tests;

/// <summary>
/// Integration tests for the authorization service.
/// </summary>
public static class AuthorizationTests
{
    [ServiceTest(testName: "authorization/register", serviceType: typeof(IAuthorizationService))]
    public static async Task<bool> Run(TestingService service)
        => await service.RunDelegates("authorization/register", new List<Func<TestingService, Task<bool>>>()
            {
                RegisterAccount_UsernamePassword
            });

    private static async Task<bool> RegisterAccount_UsernamePassword(TestingService service)
    {
        var userID = Guid.NewGuid().ToString();
        var requestModel = new CreateAccountRequest()
        {
            Email = null,
            EmailVerified = false,
            TwoFactorEnabled = false,
            Region = null,
            Username = $"TestUser_{userID}",
            Password = "SimpleReadablePassword",
            SteamID = null,
            SteamToken = null,
            GoogleID = null,
            GoogleToken = null,
            RoleClaims = null,
            AppClaims = null,
            ScopeClaims = null,
            IdentityClaims = null,
            ProfileClaims = null,
            RequestIDs = new()
            {
                ["USER_ID"] = userID
            }
        };

        if (!await requestModel.ExecutePostRequest("account", "create"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        if (await requestModel.ExecutePostRequest("account", "create"))
        {
            Program.Logger.Log(LogLevel.Error, $"Duplicate entry was able to be created for user account.");
            return false;
        }

        return true;
    }

    private static bool ValidateResponse(CreateAccountRequest requestModel, CreateAccountResponse? responseModel)
    {
        if (responseModel == null)
        {
            Program.Logger.Log(LogLevel.Error, "Test response not received.");
            return false;
        }

        if (requestModel.RequestIDs != null && requestModel.RequestIDs.Any())
        {
            if (responseModel.RequestIDs == null || !responseModel.RequestIDs.Any())
            {
                Program.Logger.Log(LogLevel.Error, "Test response missing REQUEST_IDS through headers.");
                return false;
            }

            foreach (var requestKVP in requestModel.RequestIDs)
            {
                if (!string.Equals(responseModel.RequestIDs[requestKVP.Key], requestKVP.Value))
                {
                    Program.Logger.Log(LogLevel.Error, "Test response REQUEST_IDS have been transformed through headers.");
                    return false;
                }
            }
        }

        if (!string.Equals(requestModel.Username, responseModel.Username))
        {
            Program.Logger.Log(LogLevel.Error, $"Test response Username {responseModel.Username} does not match request Username {requestModel.Username}.");
            return false;
        }

        if (!string.Equals(requestModel.Email, responseModel.Email))
        {
            Program.Logger.Log(LogLevel.Error, $"Test response Email {responseModel.Email} does not match request Email {requestModel.Email}.");
            return false;
        }

        if (requestModel.EmailVerified != responseModel.EmailVerified)
        {
            Program.Logger.Log(LogLevel.Error, $"Test response EmailVerified {responseModel.EmailVerified} does not match request EmailVerified {requestModel.EmailVerified}.");
            return false;
        }

        if (requestModel.TwoFactorEnabled != responseModel.TwoFactorEnabled)
        {
            Program.Logger.Log(LogLevel.Error, $"Test response TwoFactorEnabled {responseModel.TwoFactorEnabled} does not match request TwoFactorEnabled {requestModel.TwoFactorEnabled}.");
            return false;
        }

        if (!string.Equals(requestModel.Region, responseModel.Region))
        {
            Program.Logger.Log(LogLevel.Error, $"Test response Email {responseModel.Region} does not match request Email {requestModel.Region}.");
            return false;
        }

        if (requestModel.RoleClaims != null)
        {
            foreach (var claimValue in requestModel.RoleClaims)
            {
                if (!responseModel.RoleClaims?.Contains(claimValue) ?? false)
                {
                    Program.Logger.Log(LogLevel.Error, $"Test response Role claim {claimValue} missing.");
                    return false;
                }
            }
        }

        if (requestModel.AppClaims != null)
        {
            foreach (var claimValue in requestModel.AppClaims)
            {
                if (!responseModel.AppClaims?.Contains(claimValue) ?? false)
                {
                    Program.Logger.Log(LogLevel.Error, $"Test response App claim {claimValue} missing.");
                    return false;
                }
            }
        }

        if (requestModel.ScopeClaims != null)
        {
            foreach (var claimValue in requestModel.ScopeClaims)
            {
                if (!responseModel.ScopeClaims?.Contains(claimValue) ?? false)
                {
                    Program.Logger.Log(LogLevel.Error, $"Test response Scope claim {claimValue} missing.");
                    return false;
                }
            }
        }

        if (requestModel.IdentityClaims != null)
        {
            foreach (var claimValue in requestModel.IdentityClaims)
            {
                if (!responseModel.IdentityClaims?.Contains(claimValue) ?? false)
                {
                    Program.Logger.Log(LogLevel.Error, $"Test response Identity claim {claimValue} missing.");
                    return false;
                }
            }
        }

        if (requestModel.ProfileClaims != null)
        {
            foreach (var claimValue in requestModel.ProfileClaims)
            {
                if (!responseModel.ProfileClaims?.Contains(claimValue) ?? false)
                {
                    Program.Logger.Log(LogLevel.Error, $"Test response Profile claim {claimValue} missing.");
                    return false;
                }
            }
        }

        return true;
    }
}
