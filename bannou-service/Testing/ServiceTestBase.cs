using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Base class for service unit tests providing common configuration and assertion helpers.
/// Extend this class in your lib-*.tests projects and add mock setup using Moq.
///
/// Example usage:
/// <code>
/// public class MyServiceTests : ServiceTestBase&lt;MyServiceConfiguration&gt;
/// {
///     private readonly Mock&lt;mesh client&gt; _mockmesh client;
///     private readonly Mock&lt;ILogger&lt;MyService&gt;&gt; _mockLogger;
///
///     public MyServiceTests()
///     {
///         _mockmesh client = new Mock&lt;mesh client&gt;();
///         _mockLogger = new Mock&lt;ILogger&lt;MyService&gt;&gt;();
///     }
///
///     private MyService CreateService() =&gt; new MyService(
///         _mockmesh client.Object,
///         _mockLogger.Object,
///         Configuration);
/// }
/// </code>
/// </summary>
/// <typeparam name="TConfig">The service configuration type</typeparam>
public abstract class ServiceTestBase<TConfig> where TConfig : class, IServiceConfiguration, new()
{
    /// <summary>
    /// Gets or sets the configuration instance for tests.
    /// Override CreateConfiguration() to customize the default configuration.
    /// </summary>
    protected TConfig Configuration { get; set; }

    /// <summary>
    /// Initializes the test base with a default configuration.
    /// </summary>
    protected ServiceTestBase()
    {
        Configuration = CreateConfiguration();
    }

    /// <summary>
    /// Creates the default configuration for tests. Override to customize.
    /// </summary>
    protected virtual TConfig CreateConfiguration()
    {
        return new TConfig();
    }

    /// <summary>
    /// Creates a test configuration from environment variables (useful for integration tests).
    /// </summary>
    protected TConfig CreateConfigurationFromEnvironment()
    {
        return IServiceConfiguration.BuildConfiguration<TConfig>();
    }
}

/// <summary>
/// Exception thrown when a test assertion fails.
/// Used by ServiceTestBase assertions without requiring Xunit dependency.
/// </summary>
public class TestAssertionException : Exception
{
    /// <inheritdoc/>
    public TestAssertionException(string message) : base(message) { }
}

/// <summary>
/// Extension methods for common test assertions.
/// These throw TestAssertionException which test frameworks will catch and report.
/// </summary>
public static class TestAssertionExtensions
{
    /// <summary>
    /// Asserts that the status code indicates success (OK).
    /// </summary>
    public static void AssertSuccess(this StatusCodes statusCode)
    {
        if (statusCode != StatusCodes.OK)
        {
            throw new TestAssertionException($"Expected success status code but got {statusCode}");
        }
    }

    /// <summary>
    /// Asserts that the status code matches the expected value.
    /// </summary>
    public static void AssertStatusCode(this StatusCodes actual, StatusCodes expected)
    {
        if (actual != expected)
        {
            throw new TestAssertionException($"Expected status code {expected} but got {actual}");
        }
    }

    /// <summary>
    /// Asserts that the tuple result has the expected status code and non-null response.
    /// </summary>
    public static T AssertOkWithResult<T>(this (StatusCodes status, T? result) tuple) where T : class
    {
        tuple.status.AssertStatusCode(StatusCodes.OK);
        if (tuple.result == null)
        {
            throw new TestAssertionException("Expected non-null result but got null");
        }
        return tuple.result;
    }

    /// <summary>
    /// Asserts that the tuple result has NotFound status and null response.
    /// </summary>
    public static void AssertNotFound<T>(this (StatusCodes status, T? result) tuple) where T : class
    {
        tuple.status.AssertStatusCode(StatusCodes.NotFound);
    }

    /// <summary>
    /// Asserts that the tuple result has BadRequest status.
    /// </summary>
    public static void AssertBadRequest<T>(this (StatusCodes status, T? result) tuple) where T : class
    {
        tuple.status.AssertStatusCode(StatusCodes.BadRequest);
    }

    /// <summary>
    /// Asserts that the tuple result has Unauthorized status.
    /// </summary>
    public static void AssertUnauthorized<T>(this (StatusCodes status, T? result) tuple) where T : class
    {
        tuple.status.AssertStatusCode(StatusCodes.Unauthorized);
    }

    /// <summary>
    /// Asserts that the tuple result has Conflict status.
    /// </summary>
    public static void AssertConflict<T>(this (StatusCodes status, T? result) tuple) where T : class
    {
        tuple.status.AssertStatusCode(StatusCodes.Conflict);
    }

    /// <summary>
    /// Checks if the status code indicates success without throwing.
    /// </summary>
    public static bool IsSuccess(this StatusCodes statusCode)
    {
        return statusCode == StatusCodes.OK;
    }
}

/// <summary>
/// Test data generators for common scenarios.
/// </summary>
public static class TestDataGenerators
{
    /// <summary>
    /// Generates a random valid email address for testing.
    /// </summary>
    public static string GenerateTestEmail(string? prefix = null)
    {
        var uniquePart = Guid.NewGuid().ToString("N")[..8];
        return $"{prefix ?? "test"}-{uniquePart}@test.local";
    }

    /// <summary>
    /// Generates a random account ID for testing.
    /// </summary>
    public static Guid GenerateTestAccountId() => Guid.NewGuid();

    /// <summary>
    /// Generates a random session ID for testing.
    /// </summary>
    public static Guid GenerateTestSessionId() => Guid.NewGuid();

    /// <summary>
    /// Generates a random display name for testing.
    /// </summary>
    public static string GenerateTestDisplayName(string? prefix = null)
    {
        var uniquePart = Guid.NewGuid().ToString("N")[..6];
        return $"{prefix ?? "TestUser"}-{uniquePart}";
    }

    /// <summary>
    /// Generates a test timestamp.
    /// </summary>
    public static DateTimeOffset GenerateTestTimestamp() => DateTimeOffset.UtcNow;
}
