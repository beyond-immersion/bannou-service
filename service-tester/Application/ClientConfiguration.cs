namespace BeyondImmersion.ServiceTester.Application;

[ServiceConfiguration]
public sealed class ClientConfiguration : BaseServiceConfiguration
{
    [ConfigRequired]
    public string? Connect_Endpoint { get; set; }

    [ConfigRequired]
    public string? Register_Endpoint { get; set; }

    [ConfigRequired]
    public string? Login_Credentials_Endpoint { get; set; }

    [ConfigRequired]
    public string? Login_Token_Endpoint { get; set; }

    [ConfigRequired]
    public string? Client_Username { get; set; }

    [ConfigRequired]
    public string? Client_Password { get; set; }
}
