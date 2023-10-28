using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Accounts;

[ServiceConfiguration(typeof(AccountService), envPrefix: "account_")]
public class AccountServiceConfiguration :  BaseServiceConfiguration
{
    [ConfigRequired]
    public string Database_Host { get; set; } = "account-db";

    [ConfigRequired]
    public int Database_Port { get; set; } = 3306;

    [ConfigRequired]
    public string Database_User { get; set; } = "";

    [ConfigRequired()]
    public string Database_Password { get; set; } = "";
}
