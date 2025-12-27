namespace BeyondImmersion.EdgeTester.Application;

public sealed class ClientConfiguration
{
    public string? ConnectEndpoint { get; set; }

    public string? RegisterEndpoint { get; set; }

    public string? LoginCredentialsEndpoint { get; set; }

    public string? LoginTokenEndpoint { get; set; }

    /// <summary>
    /// OpenResty gateway hostname (internal container name, e.g., "openresty").
    /// Used for health checks before running tests.
    /// </summary>
    public string? OpenRestyHost { get; set; }

    /// <summary>
    /// OpenResty gateway port (internal container port, e.g., 80).
    /// Used for health checks before running tests.
    /// </summary>
    public int? OpenRestyPort { get; set; }

    /// <summary>
    /// Regular user credentials (email format, will be created with user role)
    /// </summary>
    public string? ClientUsername { get; set; }
    public string? ClientPassword { get; set; }

    /// <summary>
    /// Admin user credentials (email format, should match AdminEmails or AdminEmailDomain config)
    /// Used for orchestrator API tests and environment setup.
    /// Defaults to an admin email domain pattern if not specified.
    /// </summary>
    public string? AdminUsername { get; set; }
    public string? AdminPassword { get; set; }

    /// <summary>
    /// Checks if the minimum required configuration is present.
    /// Admin credentials are optional - they default to a pattern based on AdminEmailDomain.
    /// </summary>
    public bool HasRequired()
        => !string.IsNullOrWhiteSpace(ClientUsername) &&
            !string.IsNullOrWhiteSpace(ClientPassword) &&
            !string.IsNullOrWhiteSpace(RegisterEndpoint) &&
            !string.IsNullOrWhiteSpace(LoginCredentialsEndpoint) &&
            !string.IsNullOrWhiteSpace(ConnectEndpoint);

    /// <summary>
    /// Gets the admin username, defaulting to a test admin if not specified.
    /// </summary>
    public string GetAdminUsername()
        => string.IsNullOrWhiteSpace(AdminUsername) ? "admin@admin.test.local" : AdminUsername;

    /// <summary>
    /// Gets the admin password, defaulting to a test password if not specified.
    /// </summary>
    public string GetAdminPassword()
        => string.IsNullOrWhiteSpace(AdminPassword) ? "admin-test-password-2025" : AdminPassword;
}
