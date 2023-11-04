using BeyondImmersion.BannouService.Accounts.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Accounts.Tests;

/// <summary>
/// Integration tests for the account service.
/// Tests against the account/update endpoint.
/// </summary>
public static class UpdateAccountTests
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
                    IdentityClaims = new() { $"Identity_{userID}" }
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

    [ServiceTest(testName: "account/update", serviceType: typeof(IAccountService))]
    public static async Task<bool> Run(TestingService service)
        => await service.RunDelegates("account/update", new List<Func<TestingService, Task<bool>>>()
            {
                UpdateAccount_Region,
                UpdateAccount_Username,
                UpdateAccount_Email,
                UpdateAccount_GoogleID,
                UpdateAccount_SteamID,
                UpdateAccount_RoleClaims,
                UpdateAccount_AppClaims,
                UpdateAccount_ScopeClaims,
                UpdateAccount_IdentityClaims,
                UpdateAccount_ProfileClaims
            });

    private static async Task<bool> UpdateAccount_Username(TestingService service)
    {
        if (TestAccountData == null)
            return false;

        var requestModel = new UpdateAccountRequest()
        {
            ID = TestAccountData.ID,
            Username = $"Username_{Guid.NewGuid()}"
        };

        if (!await requestModel.ExecutePostRequest("account", "update"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> UpdateAccount_Region(TestingService service)
    {
        if (TestAccountData == null)
            return false;

        var requestModel = new UpdateAccountRequest()
        {
            ID = TestAccountData.ID,
            Region = "Asia"
        };

        if (!await requestModel.ExecutePostRequest("account", "update"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        if (!string.Equals(requestModel.Region, requestModel.Response?.Region))
        {
            Program.Logger.Log(LogLevel.Error, $"Test response Region {requestModel.Response?.Region} does not match request Region {requestModel.Region}.");
            return false;
        }

        return true;
    }

    private static async Task<bool> UpdateAccount_Email(TestingService service)
    {
        if (TestAccountData == null)
            return false;

        var requestModel = new UpdateAccountRequest()
        {
            ID = TestAccountData.ID,
            Email = $"Email_{Guid.NewGuid()}@arcadia.com"
        };

        if (!await requestModel.ExecutePostRequest("account", "update"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> UpdateAccount_SteamID(TestingService service)
    {
        if (TestAccountData == null)
            return false;

        var steamID = TestAccountData.IdentityClaims?.Where(t => t.StartsWith("SteamID:")).Select(t => t.Remove(0, "SteamID:".Length)).FirstOrDefault();
        if (steamID == null)
        {
            Program.Logger.Log(LogLevel.Error, $"Test account missing required property {nameof(steamID)}.");
            return false;
        }

        var requestModel = new UpdateAccountRequest()
        {
            ID = TestAccountData.ID,
            SteamID = $"SteamID_{Guid.NewGuid()}",
            IdentityClaims = new() { [steamID] = null }
        };

        if (!await requestModel.ExecutePostRequest("account", "update"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> UpdateAccount_GoogleID(TestingService service)
    {
        if (TestAccountData == null)
            return false;

        var googleID = TestAccountData.IdentityClaims?.Where(t => t.StartsWith("GoogleID:")).Select(t => t.Remove(0, "GoogleID:".Length)).FirstOrDefault();
        if (googleID == null)
        {
            Program.Logger.Log(LogLevel.Error, $"Test account missing required property {nameof(googleID)}.");
            return false;
        }

        var requestModel = new UpdateAccountRequest()
        {
            ID = TestAccountData.ID,
            GoogleID = $"Email_{Guid.NewGuid()}@arcadia.com",
            IdentityClaims = new() { [googleID] = null }
        };

        if (!await requestModel.ExecutePostRequest("account", "update"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> UpdateAccount_RoleClaims(TestingService service)
    {
        if (TestAccountData == null)
            return false;

        var requestModel = new UpdateAccountRequest()
        {
            ID = TestAccountData.ID,
            RoleClaims = new()
            {
                ["SHOULD_NOT_EXIST"] = null,
                ["BEFORE"] = "AFTER",
                ["ADD"] = "ADD",
                [""] = "EXTRA"
            }
        };

        if (!await requestModel.ExecutePostRequest("account", "update"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        if (requestModel.Response?.RoleClaims?.Contains("SHOULD_NOT_EXIST") ?? true)
        {
            Program.Logger.Log(LogLevel.Error, "Role claim exists which shouldn't.");
            return false;
        }

        if (requestModel.Response?.RoleClaims?.Contains("BEFORE") ?? true)
        {
            Program.Logger.Log(LogLevel.Error, "Role claim 'BEFORE' exists which shouldn't.");
            return false;
        }

        if (!requestModel.Response?.RoleClaims?.Contains("AFTER") ?? false)
        {
            Program.Logger.Log(LogLevel.Error, "Role claim 'AFTER' doesn't exist.");
            return false;
        }

        if (!requestModel.Response?.RoleClaims?.Contains("ADD") ?? false)
        {
            Program.Logger.Log(LogLevel.Error, "Role claim 'ADD' doesn't exist.");
            return false;
        }

        if (!requestModel.Response?.RoleClaims?.Contains("EXTRA") ?? false)
        {
            Program.Logger.Log(LogLevel.Error, "Role claim 'EXTRA' doesn't exist.");
            return false;
        }

        return true;
    }

    private static async Task<bool> UpdateAccount_AppClaims(TestingService service)
    {
        if (TestAccountData == null)
            return false;

        var requestModel = new UpdateAccountRequest()
        {
            ID = TestAccountData.ID,
            AppClaims = new()
            {
                ["SHOULD_NOT_EXIST"] = null,
                ["BEFORE"] = "AFTER",
                ["ADD"] = "ADD",
                [""] = "EXTRA"
            }
        };

        if (!await requestModel.ExecutePostRequest("account", "update"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        if (requestModel.Response?.AppClaims?.Contains("SHOULD_NOT_EXIST") ?? true)
        {
            Program.Logger.Log(LogLevel.Error, "App claim exists which shouldn't.");
            return false;
        }

        if (requestModel.Response?.AppClaims?.Contains("BEFORE") ?? true)
        {
            Program.Logger.Log(LogLevel.Error, "App claim 'BEFORE' exists which shouldn't.");
            return false;
        }

        if (!requestModel.Response?.AppClaims?.Contains("AFTER") ?? false)
        {
            Program.Logger.Log(LogLevel.Error, "App claim 'AFTER' doesn't exist.");
            return false;
        }

        if (!requestModel.Response?.AppClaims?.Contains("ADD") ?? false)
        {
            Program.Logger.Log(LogLevel.Error, "App claim 'ADD' doesn't exist.");
            return false;
        }

        if (!requestModel.Response?.AppClaims?.Contains("EXTRA") ?? false)
        {
            Program.Logger.Log(LogLevel.Error, "App claim 'EXTRA' doesn't exist.");
            return false;
        }

        return true;
    }

    private static async Task<bool> UpdateAccount_ScopeClaims(TestingService service)
    {
        if (TestAccountData == null)
            return false;

        var requestModel = new UpdateAccountRequest()
        {
            ID = TestAccountData.ID,
            ScopeClaims = new()
            {
                ["SHOULD_NOT_EXIST"] = null,
                ["BEFORE"] = "AFTER",
                ["ADD"] = "ADD",
                [""] = "EXTRA"
            }
        };

        if (!await requestModel.ExecutePostRequest("account", "update"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        if (requestModel.Response?.ScopeClaims?.Contains("SHOULD_NOT_EXIST") ?? true)
        {
            Program.Logger.Log(LogLevel.Error, "Scope claim exists which shouldn't.");
            return false;
        }

        if (requestModel.Response?.ScopeClaims?.Contains("BEFORE") ?? true)
        {
            Program.Logger.Log(LogLevel.Error, "Scope claim 'BEFORE' exists which shouldn't.");
            return false;
        }

        if (!requestModel.Response?.ScopeClaims?.Contains("AFTER") ?? false)
        {
            Program.Logger.Log(LogLevel.Error, "Scope claim 'AFTER' doesn't exist.");
            return false;
        }

        if (!requestModel.Response?.ScopeClaims?.Contains("ADD") ?? false)
        {
            Program.Logger.Log(LogLevel.Error, "Scope claim 'ADD' doesn't exist.");
            return false;
        }

        if (!requestModel.Response?.ScopeClaims?.Contains("EXTRA") ?? false)
        {
            Program.Logger.Log(LogLevel.Error, "Scope claim 'EXTRA' doesn't exist.");
            return false;
        }

        return true;
    }

    private static async Task<bool> UpdateAccount_IdentityClaims(TestingService service)
    {
        if (TestAccountData == null)
            return false;

        var requestModel = new UpdateAccountRequest()
        {
            ID = TestAccountData.ID,
            IdentityClaims = new()
            {
                ["SHOULD_NOT_EXIST"] = null,
                ["BEFORE"] = "AFTER",
                ["ADD"] = "ADD",
                [""] = "EXTRA"
            }
        };

        if (!await requestModel.ExecutePostRequest("account", "update"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        if (requestModel.Response?.IdentityClaims?.Contains("SHOULD_NOT_EXIST") ?? true)
        {
            Program.Logger.Log(LogLevel.Error, "Identity claim exists which shouldn't.");
            return false;
        }

        if (requestModel.Response?.IdentityClaims?.Contains("BEFORE") ?? true)
        {
            Program.Logger.Log(LogLevel.Error, "Identity claim 'BEFORE' exists which shouldn't.");
            return false;
        }

        if (!requestModel.Response?.IdentityClaims?.Contains("AFTER") ?? false)
        {
            Program.Logger.Log(LogLevel.Error, "Identity claim 'AFTER' doesn't exist.");
            return false;
        }

        if (!requestModel.Response?.IdentityClaims?.Contains("ADD") ?? false)
        {
            Program.Logger.Log(LogLevel.Error, "Identity claim 'ADD' doesn't exist.");
            return false;
        }

        if (!requestModel.Response?.IdentityClaims?.Contains("EXTRA") ?? false)
        {
            Program.Logger.Log(LogLevel.Error, "Identity claim 'EXTRA' doesn't exist.");
            return false;
        }

        return true;
    }

    private static async Task<bool> UpdateAccount_ProfileClaims(TestingService service)
    {
        if (TestAccountData == null)
            return false;

        var requestModel = new UpdateAccountRequest()
        {
            ID = TestAccountData.ID,
            ProfileClaims = new()
            {
                ["SHOULD_NOT_EXIST"] = null,
                ["BEFORE"] = "AFTER",
                ["ADD"] = "ADD",
                [""] = "EXTRA"
            }
        };

        if (!await requestModel.ExecutePostRequest("account", "update"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        if (requestModel.Response?.ProfileClaims?.Contains("SHOULD_NOT_EXIST") ?? true)
        {
            Program.Logger.Log(LogLevel.Error, "Profile claim exists which shouldn't.");
            return false;
        }

        if (requestModel.Response?.ProfileClaims?.Contains("BEFORE") ?? true)
        {
            Program.Logger.Log(LogLevel.Error, "Profile claim 'BEFORE' exists which shouldn't.");
            return false;
        }

        if (!requestModel.Response?.ProfileClaims?.Contains("AFTER") ?? false)
        {
            Program.Logger.Log(LogLevel.Error, "Profile claim 'AFTER' doesn't exist.");
            return false;
        }

        if (!requestModel.Response?.ProfileClaims?.Contains("ADD") ?? false)
        {
            Program.Logger.Log(LogLevel.Error, "Profile claim 'ADD' doesn't exist.");
            return false;
        }

        if (!requestModel.Response?.ProfileClaims?.Contains("EXTRA") ?? false)
        {
            Program.Logger.Log(LogLevel.Error, "Profile claim 'EXTRA' doesn't exist.");
            return false;
        }

        return true;
    }

    private static bool ValidateResponse(UpdateAccountRequest requestModel, UpdateAccountResponse? responseModel)
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

        if (requestModel.ID != responseModel.ID)
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

        var googleIDs = responseModel.IdentityClaims?.Where(t => t.StartsWith("GoogleID:"))?.Select(t => t.Remove(0, "GoogleID:".Length));
        if (!string.IsNullOrWhiteSpace(requestModel.GoogleID) && !(googleIDs?.Contains(requestModel.GoogleID) ?? false))
        {
            Program.Logger.Log(LogLevel.Error, $"Test response GoogleIDs do not contain request SteamID {requestModel.GoogleID}.");
            return false;
        }

        var steamIDs = responseModel.IdentityClaims?.Where(t => t.StartsWith("SteamID:"))?.Select(t => t.Remove(0, "SteamID:".Length));
        if (!string.IsNullOrWhiteSpace(requestModel.SteamID) && !(steamIDs?.Contains(requestModel.SteamID) ?? false))
        {
            Program.Logger.Log(LogLevel.Error, $"Test response SteamIDs do not contain request SteamID {requestModel.SteamID}.");
            return false;
        }

        return true;
    }
}
