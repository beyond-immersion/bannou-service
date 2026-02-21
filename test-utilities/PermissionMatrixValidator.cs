using Xunit;

namespace BeyondImmersion.BannouService.TestUtilities;

/// <summary>
/// Validates that a service's permission matrix is well-formed.
/// Used by plugin unit tests to verify generated permission registration code.
/// </summary>
public static class PermissionMatrixValidator
{
    /// <summary>
    /// Validates that a permission matrix has correct structure and non-empty data.
    /// </summary>
    /// <param name="serviceId">Expected service ID (must be non-empty)</param>
    /// <param name="serviceVersion">Expected service version (must be non-empty)</param>
    /// <param name="matrix">The permission matrix from BuildPermissionMatrix()</param>
    public static void ValidatePermissionMatrix(
        string serviceId,
        string serviceVersion,
        Dictionary<string, IDictionary<string, ICollection<string>>> matrix)
    {
        Assert.False(string.IsNullOrEmpty(serviceId), "serviceId must not be empty");
        Assert.False(string.IsNullOrEmpty(serviceVersion), "serviceVersion must not be empty");
        Assert.NotNull(matrix);
        Assert.NotEmpty(matrix);

        foreach (var (stateKey, roleMap) in matrix)
        {
            Assert.False(string.IsNullOrEmpty(stateKey), "State key must not be empty");
            Assert.NotEmpty(roleMap);

            foreach (var (role, endpoints) in roleMap)
            {
                Assert.False(string.IsNullOrEmpty(role), $"Role must not be empty in state '{stateKey}'");
                Assert.NotEmpty(endpoints);

                foreach (var endpoint in endpoints)
                {
                    Assert.StartsWith("/", endpoint);
                }
            }
        }
    }
}
