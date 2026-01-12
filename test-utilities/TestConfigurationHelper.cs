using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.TestUtilities;

/// <summary>
/// Helper class for setting up test configuration.
/// Provides access to configure Program.Configuration for unit tests.
/// </summary>
public static class TestConfigurationHelper
{
    /// <summary>
    /// Sets up the global Program.Configuration with JWT settings for testing.
    /// Call this in test constructors or fixtures to ensure JWT-dependent services work correctly.
    /// </summary>
    /// <param name="jwtSecret">JWT secret for token signing (min 32 characters recommended).</param>
    /// <param name="jwtIssuer">JWT issuer claim value.</param>
    /// <param name="jwtAudience">JWT audience claim value.</param>
    public static void ConfigureJwt(
        string jwtSecret = "test-jwt-secret-at-least-32-characters-long-for-security",
        string jwtIssuer = "test-issuer",
        string jwtAudience = "test-audience")
    {
        // Set Program.Configuration with JWT settings
        // Uses internal setter, accessible via InternalsVisibleTo
        Program.Configuration = new AppConfiguration
        {
            JwtSecret = jwtSecret,
            JwtIssuer = jwtIssuer,
            JwtAudience = jwtAudience
        };
    }

    /// <summary>
    /// Resets Program.Configuration to null, forcing it to be rebuilt from environment.
    /// Use in test cleanup if needed.
    /// </summary>
    public static void ResetConfiguration()
    {
        Program.Configuration = null!;
    }
}
