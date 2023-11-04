using BeyondImmersion.BannouService.Accounts.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Accounts.Tests;

/// <summary>
/// Integration tests for the account service.
/// Tests against the account/get endpoint.
/// </summary>
public static class GetAccountTests
{
    private static CreateAccountResponse? _testAccountResponse;
    private static CreateAccountResponse? TestAccountData
    {
        get
        {
            if (_testAccountResponse != null)
                return _testAccountResponse;

            try
            {
                var userID = Guid.NewGuid().ToString();
                var requestModel = new CreateAccountRequest()
                {
                    Email = $"Email_{userID}@arcadia.com",
                    Username = $"Username_{userID}",
                    Password = "SimpleReadablePassword",
                    SteamID = $"SteamID_{userID}",
                    SteamToken = $"SToken_{Guid.NewGuid()}",
                    GoogleID = $"Email_{userID}@arcadia.com",
                    GoogleToken = $"GToken_{Guid.NewGuid()}",
                    RoleClaims = new() { "Doesn't matteer" },
                    AppClaims = new() { "Game:Arcadia" },
                    ScopeClaims = new() { "Arcadia:CanCreateServer" },
                    IdentityClaims = new() { $"Identity_{userID}" },
                    ProfileClaims = new() { "Age:38" }
                };

                if (!requestModel.ExecutePostRequest("account", "create").Result)
                {
                    Program.Logger.Log(LogLevel.Error, "Failed to set up user account for account/get tests.");
                    return null;
                }

                _testAccountResponse = requestModel.Response;
                return _testAccountResponse;
            }
            catch (Exception exc)
            {
                Program.Logger.Log(LogLevel.Error, exc, "Failed to set up user account for account/get tests.");
            }

            return null;
        }
    }

    [ServiceTest(testName: "account/get", serviceType: typeof(IAccountService))]
    public static async Task<bool> Run(TestingService service)
        => await service.RunDelegates("account/get", new List<Func<TestingService, Task<bool>>>()
            {
                GetAccount_ByID,
                GetAccount_ByID_IncludeeClaims,
                GetAccount_ByUsername,
                GetAccount_ByUsername_IncludeClaims,
                GetAccount_ByEmail,
                GetAccount_ByEmail_IncludeClaims,
                GetAccount_ByGoogleID,
                GetAccount_ByGoogleID_IncludeClaims,
                GetAccount_BySteamID,
                GetAccount_BySteamID_IncludeClaims,
                GetAccount_ByIdentityClaim,
                GetAccount_ByIdentityClaim_IncludeClaims
            });

    private static async Task<bool> GetAccount_ByID(TestingService service)
    {
        if (TestAccountData == null)
            return false;

        var requestModel = new GetAccountRequest()
        {
            IncludeClaims = false,
            ID = TestAccountData.ID
        };

        if (!await requestModel.ExecutePostRequest("account", "get"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> GetAccount_ByUsername(TestingService service)
    {
        if (string.IsNullOrWhiteSpace(TestAccountData?.Username))
            return false;

        var requestModel = new GetAccountRequest()
        {
            IncludeClaims = false,
            Username = TestAccountData.Username
        };

        if (!await requestModel.ExecutePostRequest("account", "get"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> GetAccount_ByEmail(TestingService service)
    {
        if (string.IsNullOrWhiteSpace(TestAccountData?.Email))
            return false;

        var requestModel = new GetAccountRequest()
        {
            IncludeClaims = false,
            Email = TestAccountData.Email
        };

        if (!await requestModel.ExecutePostRequest("account", "get"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> GetAccount_BySteamID(TestingService service)
    {
        var steamID = TestAccountData?.IdentityClaims?
            .Where(t => t.StartsWith("SteamID:")).FirstOrDefault()?
            .Remove(0, "SteamID:".Length);

        if (string.IsNullOrWhiteSpace(steamID))
            return false;

        var requestModel = new GetAccountRequest()
        {
            IncludeClaims = false,
            SteamID = steamID
        };

        if (!await requestModel.ExecutePostRequest("account", "get"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> GetAccount_ByGoogleID(TestingService service)
    {
        var googleID = TestAccountData?.IdentityClaims?
            .Where(t => t.StartsWith("GoogleID:")).FirstOrDefault()?
            .Remove(0, "GoogleID:".Length);

        if (string.IsNullOrWhiteSpace(googleID))
            return false;

        var requestModel = new GetAccountRequest()
        {
            IncludeClaims = false,
            GoogleID = googleID
        };

        if (!await requestModel.ExecutePostRequest("account", "get"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> GetAccount_ByIdentityClaim(TestingService service)
    {
        var identityClaim = TestAccountData?.IdentityClaims?
            .Where(t => !t.StartsWith("GoogleID:") && !t.StartsWith("SteamID:")).FirstOrDefault();

        if (string.IsNullOrWhiteSpace(identityClaim))
            return false;

        var requestModel = new GetAccountRequest()
        {
            IncludeClaims = false,
            IdentityClaim = identityClaim
        };

        if (!await requestModel.ExecutePostRequest("account", "get"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> GetAccount_ByID_IncludeeClaims(TestingService service)
    {
        if (TestAccountData == null)
            return false;

        var requestModel = new GetAccountRequest()
        {
            IncludeClaims = true,
            ID = TestAccountData.ID
        };

        if (!await requestModel.ExecutePostRequest("account", "get"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> GetAccount_ByUsername_IncludeClaims(TestingService service)
    {
        if (string.IsNullOrWhiteSpace(TestAccountData?.Username))
            return false;

        var requestModel = new GetAccountRequest()
        {
            IncludeClaims = true,
            Username = TestAccountData.Username
        };

        if (!await requestModel.ExecutePostRequest("account", "get"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> GetAccount_ByEmail_IncludeClaims(TestingService service)
    {
        if (string.IsNullOrWhiteSpace(TestAccountData?.Email))
            return false;

        var requestModel = new GetAccountRequest()
        {
            IncludeClaims = true,
            Email = TestAccountData.Email
        };

        if (!await requestModel.ExecutePostRequest("account", "get"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> GetAccount_BySteamID_IncludeClaims(TestingService service)
    {
        var steamID = TestAccountData?.IdentityClaims?
            .Where(t => t.StartsWith("SteamID:")).FirstOrDefault()?
            .Remove(0, "SteamID:".Length);

        if (string.IsNullOrWhiteSpace(steamID))
            return false;

        var requestModel = new GetAccountRequest()
        {
            IncludeClaims = true,
            SteamID = steamID
        };

        if (!await requestModel.ExecutePostRequest("account", "get"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> GetAccount_ByGoogleID_IncludeClaims(TestingService service)
    {
        var googleID = TestAccountData?.IdentityClaims?
            .Where(t => t.StartsWith("GoogleID:")).FirstOrDefault()?
            .Remove(0, "GoogleID:".Length);

        if (string.IsNullOrWhiteSpace(googleID))
            return false;

        var requestModel = new GetAccountRequest()
        {
            IncludeClaims = true,
            GoogleID = googleID
        };

        if (!await requestModel.ExecutePostRequest("account", "get"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> GetAccount_ByIdentityClaim_IncludeClaims(TestingService service)
    {
        var identityClaim = TestAccountData?.IdentityClaims?
            .Where(t => !t.StartsWith("GoogleID:") && !t.StartsWith("SteamID:")).FirstOrDefault();

        if (string.IsNullOrWhiteSpace(identityClaim))
            return false;

        var requestModel = new GetAccountRequest()
        {
            IncludeClaims = true,
            IdentityClaim = identityClaim
        };

        if (!await requestModel.ExecutePostRequest("account", "get"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static bool ValidateResponse(GetAccountRequest requestModel, GetAccountResponse? responseModel)
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

        if (requestModel.ID != null && requestModel.ID != responseModel.ID)
        {
            Program.Logger.Log(LogLevel.Error, $"Test response Id {responseModel.ID} does not match request Id {requestModel.ID}.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestModel.Username) && !string.Equals(requestModel.Username, responseModel.Username))
        {
            Program.Logger.Log(LogLevel.Error, $"Test response Username {responseModel.Username} does not match request Username {requestModel.Username}.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestModel.Email) && !string.Equals(requestModel.Email, responseModel.Email))
        {
            Program.Logger.Log(LogLevel.Error, $"Test response Email {responseModel.Email} does not match request Email {requestModel.Email}.");
            return false;
        }

        if (requestModel.IncludeClaims)
        {
            var googleID = responseModel.IdentityClaims?.FirstOrDefault(t => t.StartsWith("GoogleID:"))?.Remove(0, "GoogleID:".Length);
            if (!string.IsNullOrWhiteSpace(requestModel.GoogleID) && !string.Equals(requestModel.GoogleID, googleID))
            {
                Program.Logger.Log(LogLevel.Error, $"Test response GoogleID {googleID} does not match request GoogleID {requestModel.GoogleID}.");
                return false;
            }

            var steamID = responseModel.IdentityClaims?.FirstOrDefault(t => t.StartsWith("SteamID:"))?.Remove(0, "SteamID:".Length);
            if (!string.IsNullOrWhiteSpace(requestModel.SteamID) && !string.Equals(requestModel.SteamID, steamID))
            {
                Program.Logger.Log(LogLevel.Error, $"Test response SteamID {steamID} does not match request SteamID {requestModel.SteamID}.");
                return false;
            }

            var identityClaim = responseModel.IdentityClaims?.FirstOrDefault(t => !t.StartsWith("SteamID:") && !t.StartsWith("GoogleID:"));
            if (!string.IsNullOrWhiteSpace(requestModel.IdentityClaim) && !string.Equals(requestModel.IdentityClaim, identityClaim))
            {
                Program.Logger.Log(LogLevel.Error, $"Test response IdentityClaim {identityClaim} does not match request IdentityClaim {requestModel.IdentityClaim}.");
                return false;
            }
        }

        return true;
    }
}
