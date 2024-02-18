using BeyondImmersion.BannouService.Accounts.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Accounts.Tests;

/// <summary>
/// Integration tests for the account service.
/// Tests against the account/create endpoint.
/// </summary>
public static class CreateAccountTests
{
    [ServiceTest(testName: "account/create", serviceType: typeof(IAccountService))]
    public static async Task<bool> Run(TestingService service)
        => await service.RunDelegates("account/create", new List<Func<TestingService, Task<bool>>>()
            {
                CreateAccount_UsernamePassword,
                CreateAccount_GoogleOAUTH,
                CreateAccount_SteamOAUTH,
                CreateAccount_AllParameters
            });

    private static async Task<bool> CreateAccount_UsernamePassword(TestingService service)
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

        if (!await requestModel.ExecuteRequest("account", "create"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        if (await requestModel.ExecuteRequest("account", "create"))
        {
            Program.Logger.Log(LogLevel.Error, $"Duplicate entry was able to be created for user account.");
            return false;
        }

        return true;
    }

    private static async Task<bool> CreateAccount_GoogleOAUTH(TestingService service)
    {
        var userID = Guid.NewGuid().ToString();
        var requestModel = new CreateAccountRequest()
        {
            Email = $"TestEmail@{userID}",
            EmailVerified = true,
            TwoFactorEnabled = true,
            Region = null,
            Username = null,
            Password = null,
            SteamID = null,
            SteamToken = null,
            GoogleID = $"TestEmail@{userID}",
            GoogleToken = "SimpleReadablePassword",
            RoleClaims = null,
            AppClaims = null,
            ScopeClaims = null,
            IdentityClaims = null,
            ProfileClaims = null
        };

        if (!await requestModel.ExecuteRequest("account", "create"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        if (await requestModel.ExecuteRequest("account", "create"))
        {
            Program.Logger.Log(LogLevel.Error, $"Duplicate entry was able to be created for user account.");
            return false;
        }

        return true;
    }

    private static async Task<bool> CreateAccount_SteamOAUTH(TestingService service)
    {
        var userID = Guid.NewGuid().ToString();
        var requestModel = new CreateAccountRequest()
        {
            Email = $"TestEmail@{userID}",
            EmailVerified = true,
            TwoFactorEnabled = true,
            Region = null,
            Username = null,
            Password = null,
            SteamID = $"USER_{userID}",
            SteamToken = "SimpleReadablePassword",
            GoogleID = null,
            GoogleToken = null,
            RoleClaims = null,
            AppClaims = null,
            ScopeClaims = null,
            IdentityClaims = null,
            ProfileClaims = null
        };

        if (!await requestModel.ExecuteRequest("account", "create"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        if (await requestModel.ExecuteRequest("account", "create"))
        {
            Program.Logger.Log(LogLevel.Error, $"Duplicate entry was able to be created for user account.");
            return false;
        }

        return true;
    }

    private static async Task<bool> CreateAccount_AllParameters(TestingService service)
    {
        var userID = Guid.NewGuid().ToString();
        var requestModel = new CreateAccountRequest()
        {
            Email = $"TestEmail@{userID}",
            EmailVerified = true,
            TwoFactorEnabled = true,
            Region = "NA",
            Username = $"USER_{userID}",
            Password = "SimpleReadablePassword",
            SteamID = $"USER_{userID}",
            SteamToken = "SimpleReadablePassword",
            GoogleID = $"TestEmail@{userID}",
            GoogleToken = "SimpleReadablePassword",
            RoleClaims = new HashSet<string>() { "Administrator" },
            AppClaims = new HashSet<string>() { "ArcadiaGame" },
            ScopeClaims = new HashSet<string>() { "ArcadiaGame:Admin" },
            IdentityClaims = new HashSet<string>() { "ThirdParty:SomeToken" },
            ProfileClaims = new HashSet<string>() { "ProfilePictureUri:http://some_url.com/test/picture.png" },
            RequestIDs = new()
            {
                ["USER_ID"] = userID
            }
        };

        if (!await requestModel.ExecuteRequest("account", "create"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        if (await requestModel.ExecuteRequest("account", "create"))
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
