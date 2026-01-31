using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Base class for HTTP test handlers providing common utilities for service testing.
/// Consolidates the repeated try-catch-ApiException pattern and service client access.
///
/// MANDATORY: All test handlers MUST use GetServiceClient&lt;T&gt;() to obtain service clients.
/// DO NOT use raw HttpClient - the only exception is TestingTestHandler which tests
/// low-level routing behavior. If you're copying from an existing handler, use
/// AccountTestHandler or GameServiceTestHandler as your template, NOT TestingTestHandler.
/// </summary>
public abstract class BaseHttpTestHandler : IServiceTestHandler
{
    /// <summary>
    /// Get all service tests provided by this handler.
    /// Override this in derived classes to define test cases.
    /// </summary>
    public abstract ServiceTest[] GetServiceTests();

    /// <summary>
    /// Gets a service client from the dependency injection container.
    /// </summary>
    /// <typeparam name="T">The service client interface type</typeparam>
    /// <returns>The service client instance</returns>
    /// <exception cref="InvalidOperationException">Thrown when service provider is not initialized</exception>
    protected static T GetServiceClient<T>() where T : class
    {
        if (Program.ServiceProvider == null)
            throw new InvalidOperationException("Service provider not initialized");

        return Program.ServiceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// Executes a test action with standardized error handling for ApiException.
    /// Use this to wrap test logic that calls service clients.
    /// </summary>
    /// <param name="testAction">The async test action to execute</param>
    /// <param name="operationName">Name of the operation for error messages</param>
    /// <returns>TestResult indicating success or failure</returns>
    protected static async Task<TestResult> ExecuteTestAsync(
        Func<Task<TestResult>> testAction,
        string operationName)
    {
        try
        {
            return await testAction();
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"{operationName} failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Executes a test action expecting a specific status code (for negative tests).
    /// Use this when testing that an operation correctly fails with expected status.
    /// </summary>
    /// <param name="testAction">The async test action expected to throw ApiException</param>
    /// <param name="expectedStatusCode">The expected HTTP status code</param>
    /// <param name="operationName">Name of the operation for messages</param>
    /// <returns>TestResult indicating if the expected exception was thrown</returns>
    protected static async Task<TestResult> ExecuteExpectingStatusAsync(
        Func<Task> testAction,
        int expectedStatusCode,
        string operationName)
    {
        try
        {
            await testAction();
            return TestResult.Failed($"{operationName} should have failed with {expectedStatusCode} but succeeded");
        }
        catch (ApiException ex) when (ex.StatusCode == expectedStatusCode)
        {
            return TestResult.Successful($"{operationName} correctly returned {expectedStatusCode}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"{operationName} failed with unexpected status: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Executes a test action expecting one of multiple acceptable status codes.
    /// Use this when multiple status codes indicate success (e.g., 400 or 404 for invalid input).
    /// </summary>
    /// <param name="testAction">The async test action expected to throw ApiException</param>
    /// <param name="acceptableStatusCodes">Array of acceptable HTTP status codes</param>
    /// <param name="operationName">Name of the operation for messages</param>
    /// <returns>TestResult indicating if an expected exception was thrown</returns>
    protected static async Task<TestResult> ExecuteExpectingAnyStatusAsync(
        Func<Task> testAction,
        int[] acceptableStatusCodes,
        string operationName)
    {
        try
        {
            await testAction();
            return TestResult.Failed($"{operationName} should have failed but succeeded");
        }
        catch (ApiException ex) when (acceptableStatusCodes.Contains(ex.StatusCode))
        {
            return TestResult.Successful($"{operationName} correctly returned {ex.StatusCode}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"{operationName} failed with unexpected status: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Generates a unique test identifier based on current timestamp.
    /// Uses underscore separator - suitable for usernames (pattern: ^[a-zA-Z0-9_]+$).
    /// </summary>
    /// <param name="prefix">Optional prefix for the identifier</param>
    /// <returns>A unique string identifier with underscore separator</returns>
    protected static string GenerateTestId(string prefix = "test")
    {
        return $"{prefix}_{DateTime.Now.Ticks}";
    }

    /// <summary>
    /// Generates a unique test slug based on current timestamp.
    /// Uses hyphen separator - suitable for slugs/stub names (pattern: ^[a-z0-9-]+$).
    /// </summary>
    /// <param name="prefix">Optional prefix for the slug</param>
    /// <returns>A unique lowercase string with hyphen separator</returns>
    protected static string GenerateTestSlug(string prefix = "test")
    {
        return $"{prefix}-{DateTime.Now.Ticks}".ToLowerInvariant();
    }

    /// <summary>
    /// Generates a unique test email address.
    /// </summary>
    /// <param name="prefix">Optional prefix for the email local part</param>
    /// <returns>A unique email address</returns>
    protected static string GenerateTestEmail(string prefix = "test")
    {
        return $"{GenerateTestId(prefix)}@example.com";
    }

    /// <summary>
    /// Fixed stub name for the shared test game service.
    /// Must match GAME_SESSION_SUPPORTED_GAME_SERVICES in .env.test.http.
    /// </summary>
    private const string TestGameServiceStubName = "http-test";

    /// <summary>
    /// Cached game service ID for test resource creation.
    /// </summary>
    private static Guid? _cachedGameServiceId;

    /// <summary>
    /// Gets or creates a test game service for use in realm creation.
    /// Uses a fixed stub name so it can be added to GAME_SESSION_SUPPORTED_GAME_SERVICES.
    /// </summary>
    protected static async Task<Guid> GetOrCreateTestGameServiceAsync()
    {
        if (_cachedGameServiceId.HasValue)
            return _cachedGameServiceId.Value;

        var gameServiceClient = GetServiceClient<IGameServiceClient>();

        // Try to get existing service first (may exist from previous test run)
        try
        {
            var existing = await gameServiceClient.GetServiceAsync(new GetServiceRequest
            {
                StubName = TestGameServiceStubName
            });
            _cachedGameServiceId = existing.ServiceId;
            return _cachedGameServiceId.Value;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // Service doesn't exist, create it
        }

        var response = await gameServiceClient.CreateServiceAsync(new CreateServiceRequest
        {
            StubName = TestGameServiceStubName,
            DisplayName = "HTTP Test Game Service",
            Description = "Test game service for HTTP integration tests"
        });

        _cachedGameServiceId = response.ServiceId;
        return _cachedGameServiceId.Value;
    }

    /// <summary>
    /// Creates a test realm for tests that need realm-dependent resources.
    /// Consolidates the repeated realm creation pattern used by Location, Species, and Character tests.
    /// </summary>
    /// <param name="prefix">Code prefix for uniqueness (e.g., "LOC", "SPECIES", "CHAR")</param>
    /// <param name="description">Human-readable description for the name (e.g., "Location", "Species")</param>
    /// <param name="suffix">Test-specific suffix for identification</param>
    /// <returns>The created realm response</returns>
    protected static async Task<RealmResponse> CreateTestRealmAsync(string prefix, string description, string suffix)
    {
        var gameServiceId = await GetOrCreateTestGameServiceAsync();
        var realmClient = GetServiceClient<IRealmClient>();
        return await realmClient.CreateRealmAsync(new CreateRealmRequest
        {
            Code = $"{prefix}_{DateTime.Now.Ticks}_{suffix}",
            Name = $"{description} Test Realm {suffix}",
            Category = "TEST",
            GameServiceId = gameServiceId
        });
    }
}
