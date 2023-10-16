using BeyondImmersion.BannouService.Services;
/// <summary>
/// Service component responsible for account handling.
/// </summary>
[DaprService("account")]
public class AccountService : IAccountService
{
    public AccountServiceConfiguration Configuration { get; set; }

    public async Task OnStart()
    {
        await Task.CompletedTask;
        Configuration = IServiceConfiguration.BuildConfiguration<AccountServiceConfiguration>();

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
