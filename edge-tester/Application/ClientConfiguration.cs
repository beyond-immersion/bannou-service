namespace BeyondImmersion.EdgeTester.Application;

public sealed class ClientConfiguration
{
    public string? Connect_Endpoint { get; set; }

    public string? Register_Endpoint { get; set; }

    public string? Login_Credentials_Endpoint { get; set; }

    public string? Login_Token_Endpoint { get; set; }

    public string? Client_Username { get; set; }

    public string? Client_Password { get; set; }

    public bool HasRequired()
        => !string.IsNullOrWhiteSpace(Client_Username) &&
            !string.IsNullOrWhiteSpace(Client_Password) &&
            !string.IsNullOrWhiteSpace(Register_Endpoint) &&
            !string.IsNullOrWhiteSpace(Login_Credentials_Endpoint) &&
            !string.IsNullOrWhiteSpace(Connect_Endpoint);
}
