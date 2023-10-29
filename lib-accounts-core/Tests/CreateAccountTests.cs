using BeyondImmersion.BannouService.Accounts.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

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
        service.ResetTestVars();
        var userID = Guid.NewGuid().ToString();
        var requestModel = new CreateAccountRequest()
        {
            Email = null,
            EmailVerified = false,
            TwoFactorEnabled = false,
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

        HttpRequestMessage apiRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, "bannou", $"account/create", requestModel);
        var apiResponse = await Program.DaprClient.InvokeMethodAsync<CreateAccountResponse>(apiRequest, Program.ShutdownCancellationTokenSource.Token);

        return ValidateCreateResponse(userID, requestModel, apiResponse);
    }

    private static async Task<bool> CreateAccount_GoogleOAUTH(TestingService service)
    {
        service.ResetTestVars();
        var userID = Guid.NewGuid().ToString();
        var requestModel = new CreateAccountRequest()
        {
            Email = $"TestEmail@{userID}",
            EmailVerified = true,
            TwoFactorEnabled = true,
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

        HttpRequestMessage apiRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, "bannou", $"account/create", requestModel);
        var apiResponse = await Program.DaprClient.InvokeMethodAsync<CreateAccountResponse>(apiRequest, Program.ShutdownCancellationTokenSource.Token);

        return ValidateCreateResponse(userID, requestModel, apiResponse);
    }

    private static async Task<bool> CreateAccount_SteamOAUTH(TestingService service)
    {
        service.ResetTestVars();
        var userID = Guid.NewGuid().ToString();
        var requestModel = new CreateAccountRequest()
        {
            Email = $"TestEmail@{userID}",
            EmailVerified = true,
            TwoFactorEnabled = true,
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

        HttpRequestMessage apiRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, "bannou", $"account/create", requestModel);
        var apiResponse = await Program.DaprClient.InvokeMethodAsync<CreateAccountResponse>(apiRequest, Program.ShutdownCancellationTokenSource.Token);

        return ValidateCreateResponse(userID, requestModel, apiResponse);
    }

    private static async Task<bool> CreateAccount_AllParameters(TestingService service)
    {
        service.ResetTestVars();
        var userID = Guid.NewGuid().ToString();
        var requestModel = new CreateAccountRequest()
        {
            Email = $"TestEmail@{userID}",
            EmailVerified = true,
            TwoFactorEnabled = true,
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

        HttpRequestMessage apiRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, "bannou", $"account/create", requestModel);
        var apiResponse = await Program.DaprClient.InvokeMethodAsync<CreateAccountResponse>(apiRequest, Program.ShutdownCancellationTokenSource.Token);

        return ValidateCreateResponse(userID, requestModel, apiResponse);
    }

    private static bool ValidateCreateResponse(string userID, CreateAccountRequest requestModel, CreateAccountResponse responseModel)
    {
        if (responseModel == null)
            return false;

        if (!responseModel.RequestIDs.TryGetValue("USER_ID", out var userIDValue) || !string.Equals(userID, userIDValue))
            return false;

        if (string.IsNullOrWhiteSpace(responseModel.GUID))
            return false;

        if (!string.Equals(requestModel.Username, responseModel.Username))
            return false;

        if (!string.Equals(requestModel.Email, responseModel.Email))
            return false;

        if (requestModel.EmailVerified != responseModel.EmailVerified)
            return false;

        if (requestModel.TwoFactorEnabled != responseModel.TwoFactorEnabled)
            return false;

        if (requestModel.RoleClaims != null)
            foreach (var claimValue in requestModel.RoleClaims)
                if (!responseModel.RoleClaims?.Contains(claimValue) ?? false)
                    return false;

        if (requestModel.AppClaims != null)
            foreach (var claimValue in requestModel.AppClaims)
                if (!responseModel.AppClaims?.Contains(claimValue) ?? false)
                    return false;

        if (requestModel.ScopeClaims != null)
            foreach (var claimValue in requestModel.ScopeClaims)
                if (!responseModel.ScopeClaims?.Contains(claimValue) ?? false)
                    return false;

        if (requestModel.IdentityClaims != null)
            foreach (var claimValue in requestModel.IdentityClaims)
                if (!responseModel.IdentityClaims?.Contains(claimValue) ?? false)
                    return false;

        if (requestModel.ProfileClaims != null)
            foreach (var claimValue in requestModel.ProfileClaims)
                if (!responseModel.ProfileClaims?.Contains(claimValue) ?? false)
                    return false;

        return true;
    }
}
