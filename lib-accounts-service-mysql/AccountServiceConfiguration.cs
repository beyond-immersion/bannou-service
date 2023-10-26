using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Accounts;

[ServiceConfiguration(typeof(IAccountService), envPrefix: "account_")]
public class AccountServiceConfiguration :  BaseServiceConfiguration
{
    [ConfigRequired]
    public string? Connection_String { get; set; }
}
