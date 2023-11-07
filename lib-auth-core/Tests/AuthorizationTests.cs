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
                RegisterAccount_UsernamePassword,
                RegisterAccount_Duplicate,
                RegisterAccount_AllParameters,
                PostLogin_UsernamePassword,
                PostLogin_Token,
                GetLogin_UsernamePassword,
                GetLogin_Token
            });

    private static async Task<bool> RegisterAccount_UsernamePassword(TestingService service)
    {
        var userID = Guid.NewGuid().ToString();
        var requestModel = new RegisterRequest()
        {
            Email = null,
            Username = $"TestUser_{userID}",
            Password = "SimpleReadablePassword",
            RequestIDs = new()
            {
                ["USER_ID"] = userID
            }
        };

        if (!await requestModel.ExecuteRequest("authorization", "register"))
            return false;

        if (string.IsNullOrWhiteSpace(requestModel.Response?.AccessToken))
        {
            Program.Logger.Log(LogLevel.Error, "Registration response missing the access token.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(requestModel.Response?.RefreshToken))
        {
            Program.Logger.Log(LogLevel.Error, "Registration response missing the refresh token.");
            return false;
        }

        return true;
    }

    private static async Task<bool> RegisterAccount_Duplicate(TestingService service)
    {
        var userID = Guid.NewGuid().ToString();
        var requestModel = new RegisterRequest()
        {
            Email = null,
            Username = $"TestUser_{userID}",
            Password = "SimpleReadablePassword",
            RequestIDs = new()
            {
                ["USER_ID"] = userID
            }
        };

        if (!await requestModel.ExecuteRequest("authorization", "register"))
            return false;

        if (await requestModel.ExecuteRequest("authorization", "register") || requestModel.Response?.StatusCode != System.Net.HttpStatusCode.Forbidden)
        {
            Program.Logger.Log(LogLevel.Error, "Duplicate user registration didn't fail as expected.");
            return false;
        }

        return true;
    }

    private static async Task<bool> RegisterAccount_AllParameters(TestingService service)
    {
        var userID = Guid.NewGuid().ToString();
        var requestModel = new RegisterRequest()
        {
            Email = $"$Email_{userID}@arcadia.com",
            Username = $"TestUser_{userID}",
            Password = "SimpleReadablePassword",
            RequestIDs = new()
            {
                ["USER_ID"] = userID
            }
        };

        if (!await requestModel.ExecuteRequest("authorization", "register"))
            return false;

        if (string.IsNullOrWhiteSpace(requestModel.Response?.AccessToken))
        {
            Program.Logger.Log(LogLevel.Error, "Registration response missing the access token.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(requestModel.Response?.RefreshToken))
        {
            Program.Logger.Log(LogLevel.Error, "Registration response missing the refresh token.");
            return false;
        }

        return true;
    }

    private static async Task<bool> PostLogin_UsernamePassword(TestingService service)
    {
        var userID = Guid.NewGuid().ToString();
        var username = $"TestUser_{userID}";
        var password = "SimpleReadablePassword";

        var requestModel = new RegisterRequest()
        {
            Email = null,
            Username = username,
            Password = password,
            RequestIDs = new()
            {
                ["USER_ID"] = userID
            }
        };

        if (!await requestModel.ExecuteRequest("authorization", "register"))
            return false;

        var loginRequest = new LoginRequest() { };
        if (!await loginRequest.ExecuteRequest<LoginResponse>("authorization", "login/credentials", additionalHeaders: new Dictionary<string, string>() { ["username"] = username, ["password"] = password }))
        {
            Program.Logger.Log(LogLevel.Error, "Login with registered username and password failed.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(loginRequest.Response?.AccessToken))
        {
            Program.Logger.Log(LogLevel.Error, "Registration response missing the access token.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(loginRequest.Response?.RefreshToken))
        {
            Program.Logger.Log(LogLevel.Error, "Registration response missing the refresh token.");
            return false;
        }

        return true;
    }

    private static async Task<bool> GetLogin_UsernamePassword(TestingService service)
    {
        var userID = Guid.NewGuid().ToString();
        var username = $"TestUser_{userID}";
        var password = "SimpleReadablePassword";

        var requestModel = new RegisterRequest()
        {
            Email = null,
            Username = username,
            Password = password,
            RequestIDs = new()
            {
                ["USER_ID"] = userID
            }
        };

        if (!await requestModel.ExecuteRequest("authorization", "register"))
            return false;

        var loginRequest = new LoginRequest() { };
        if (!await loginRequest.ExecuteRequest<LoginResponse>("authorization", "login/credentials", additionalHeaders: new Dictionary<string, string>() { ["username"] = username, ["password"] = password }, HttpMethodTypes.GET))
        {
            Program.Logger.Log(LogLevel.Error, "Login with registered username and password failed.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(loginRequest.Response?.AccessToken))
        {
            Program.Logger.Log(LogLevel.Error, "Registration response missing the access token.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(loginRequest.Response?.RefreshToken))
        {
            Program.Logger.Log(LogLevel.Error, "Registration response missing the refresh token.");
            return false;
        }

        return true;
    }

    private static async Task<bool> PostLogin_Token(TestingService service)
    {
        var userID = Guid.NewGuid().ToString();
        var username = $"TestUser_{userID}";
        var password = "SimpleReadablePassword";

        var requestModel = new RegisterRequest()
        {
            Email = null,
            Username = username,
            Password = password,
            RequestIDs = new()
            {
                ["USER_ID"] = userID
            }
        };

        if (!await requestModel.ExecuteRequest("authorization", "register"))
            return false;

        if (string.IsNullOrWhiteSpace(requestModel.Response?.RefreshToken))
        {
            Program.Logger.Log(LogLevel.Error, "Registration response missing the refresh token.");
            return false;
        }

        var loginRequest = new LoginRequest() { };
        if (!await loginRequest.ExecuteRequest<LoginResponse>("authorization", "login/token", additionalHeaders: new Dictionary<string, string>() { ["token"] = requestModel.Response.RefreshToken }))
        {
            Program.Logger.Log(LogLevel.Error, "Login with registered username and password failed.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(loginRequest.Response?.AccessToken))
        {
            Program.Logger.Log(LogLevel.Error, "Registration response missing the access token.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(loginRequest.Response?.RefreshToken))
        {
            Program.Logger.Log(LogLevel.Error, "Registration response missing the refresh token.");
            return false;
        }

        return true;
    }

    private static async Task<bool> GetLogin_Token(TestingService service)
    {
        var userID = Guid.NewGuid().ToString();
        var username = $"TestUser_{userID}";
        var password = "SimpleReadablePassword";

        var requestModel = new RegisterRequest()
        {
            Email = null,
            Username = username,
            Password = password,
            RequestIDs = new()
            {
                ["USER_ID"] = userID
            }
        };

        if (!await requestModel.ExecuteRequest("authorization", "register"))
            return false;

        if (string.IsNullOrWhiteSpace(requestModel.Response?.RefreshToken))
        {
            Program.Logger.Log(LogLevel.Error, "Registration response missing the refresh token.");
            return false;
        }

        var loginRequest = new LoginRequest() { };
        if (!await loginRequest.ExecuteRequest<LoginResponse>("authorization", "login/token", additionalHeaders: new Dictionary<string, string>() { ["token"] = requestModel.Response.RefreshToken }, HttpMethodTypes.GET))
        {
            Program.Logger.Log(LogLevel.Error, "Login with registered username and password failed.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(loginRequest.Response?.AccessToken))
        {
            Program.Logger.Log(LogLevel.Error, "Registration response missing the access token.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(loginRequest.Response?.RefreshToken))
        {
            Program.Logger.Log(LogLevel.Error, "Registration response missing the refresh token.");
            return false;
        }

        return true;
    }
}
