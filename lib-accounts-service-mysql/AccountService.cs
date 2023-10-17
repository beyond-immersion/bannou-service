using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Service component responsible for account handling.
/// </summary>
[DaprService("account", typeof(IAccountService))]
public class AccountService : IAccountService
{
    private AccountServiceConfiguration? _configuration;
    public AccountServiceConfiguration Configuration
    {
        get
        {
            _configuration ??= IServiceConfiguration.BuildConfiguration<AccountServiceConfiguration>();
            return _configuration;
        }

        internal set => _configuration = value;
    }

    public async Task OnStart()
    {
        await Task.CompletedTask;

        // if integration testing, then add default test record to account datastore
        if (Program.Configuration.Integration_Testing)
        {
            var id = Guid.NewGuid().ToString();
            var secretSalt = Guid.NewGuid().ToString();
            var hashedSecret = GenerateHashedSecret(ServiceConstants.TEST_ACCOUNT_SECRET, secretSalt);
            var accountEntry = new AccountData(id, ServiceConstants.TEST_ACCOUNT_EMAIL, hashedSecret, secretSalt, ServiceConstants.TEST_ACCOUNT_DISPLAY_NAME);
        }
    }

    public async Task<AccountData?> GetAccount(string email)
    {
        await Task.CompletedTask;
        return null;
    }

    public static string GenerateHashedSecret(string secretString, string secretSalt)
        => IAccountService.GenerateHashedSecret(secretString, secretSalt);
}
