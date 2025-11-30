namespace BeyondImmersion.EdgeTester.Application;

public sealed class ClientConfiguration
{
    public string? Connect_Endpoint { get; set; }

    public string? Register_Endpoint { get; set; }

    public string? Login_Credentials_Endpoint { get; set; }

    public string? Login_Token_Endpoint { get; set; }

    /// <summary>
    /// Regular user credentials (email format, will be created with user role)
    /// </summary>
    public string? Client_Username { get; set; }
    public string? Client_Password { get; set; }

    /// <summary>
    /// Admin user credentials (email format, should match AdminEmails or AdminEmailDomain config)
    /// Used for orchestrator API tests and environment setup.
    /// Defaults to an admin email domain pattern if not specified.
    /// </summary>
    public string? Admin_Username { get; set; }
    public string? Admin_Password { get; set; }

    /// <summary>
    /// Checks if the minimum required configuration is present.
    /// Admin credentials are optional - they default to a pattern based on AdminEmailDomain.
    /// </summary>
    public bool HasRequired()
        => !string.IsNullOrWhiteSpace(Client_Username) &&
            !string.IsNullOrWhiteSpace(Client_Password) &&
            !string.IsNullOrWhiteSpace(Register_Endpoint) &&
            !string.IsNullOrWhiteSpace(Login_Credentials_Endpoint) &&
            !string.IsNullOrWhiteSpace(Connect_Endpoint);

    /// <summary>
    /// Gets the admin username, defaulting to a test admin if not specified.
    /// </summary>
    public string GetAdminUsername()
        => string.IsNullOrWhiteSpace(Admin_Username) ? "admin@admin.test.local" : Admin_Username;

    /// <summary>
    /// Gets the admin password, defaulting to a test password if not specified.
    /// </summary>
    public string GetAdminPassword()
        => string.IsNullOrWhiteSpace(Admin_Password) ? "admin-test-password-2025" : Admin_Password;
}
