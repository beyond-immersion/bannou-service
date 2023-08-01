using BeyondImmersion.BannouService.Services.Configuration;
using Google.Api;
using JWT;
using JWT.Algorithms;
using JWT.Builder;
using JWT.Serializers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;

namespace BeyondImmersion.BannouService.Services;

public static class ExtensionMethods
{
    public static string GenerateHashedSecret(this AccountModel accountData, string secretString)
        => AccountService.GenerateHashedSecret(secretString, accountData.SecretSalt);
}

/// <summary>
/// Service component responsible for account handling.
/// </summary>
[DaprService("account")]
public class AccountService : IDaprService
{
    public AccountServiceConfiguration Configuration { get; set; }

    async Task IDaprService.OnStart()
    {
        Configuration = IServiceConfiguration.BuildConfiguration<AccountServiceConfiguration>();

        // if integration testing, then add default test record to account datastore
        if (Program.Configuration.Integration_Testing)
        {
            var id = Guid.NewGuid().ToString();
            var secretSalt = Guid.NewGuid().ToString();
            var hashedSecret = GenerateHashedSecret(ServiceConstants.TEST_ACCOUNT_SECRET, secretSalt);

            var accountEntry = new AccountModel(id, ServiceConstants.TEST_ACCOUNT_EMAIL, hashedSecret, secretSalt, ServiceConstants.TEST_ACCOUNT_DISPLAY_NAME);
            await Program.DaprClient.SaveStateAsync(ServiceConstants.ACCOUNTS_STORE_NAME, ServiceConstants.TEST_ACCOUNT_EMAIL.ToLower(), accountEntry, cancellationToken: Program.ShutdownCancellationTokenSource.Token);
        }
    }

    async Task IDaprService.OnRunning()
    {
        await Task.CompletedTask;
    }

    async Task IDaprService.OnShutdown()
    {
        await Task.CompletedTask;
    }

    public async Task<AccountModel?> GetAccount(string email)
    {
        return await Program.DaprClient.GetStateAsync<AccountModel>(Program.GetAppByServiceName(ServiceConstants.ACCOUNTS_STORE_NAME), email.ToLower(), cancellationToken: Program.ShutdownCancellationTokenSource.Token);
    }

    public static string GenerateHashedSecret(string secretString, string secretSalt)
    {
        var hashAlgo = SHA512.Create();
        var hashedBytes = hashAlgo.ComputeHash(Encoding.UTF8.GetBytes(secretString + secretSalt));
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < hashedBytes.Length; i++)
        {
            builder.Append(hashedBytes[i].ToString("x2"));
        }
        var hashedSecret = builder.ToString();
        return hashedSecret;
    }
}
