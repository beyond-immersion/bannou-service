using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.ServiceClients;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeyondImmersion.BannouService.Tests;

/// <summary>
/// Tests for <see cref="DirectDispatchHelper"/> — zero-serialization direct dispatch
/// from generated mesh clients to service implementations in embedded/sidecar mode.
/// Uses a test service interface + implementation defined in this file to avoid
/// cross-plugin references (bannou-service.tests cannot reference lib-* plugins).
/// </summary>
public class DirectDispatchHelperTests
{
    private readonly IServiceProvider _serviceProvider;

    public DirectDispatchHelperTests()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITestDispatchService, TestDispatchService>();
        _serviceProvider = services.BuildServiceProvider();
    }

    // =========================================================================
    // InvokeAsync — Success Path
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_WhenServiceReturnsOk_ReturnsResponse()
    {
        var request = new TestRequest { Name = "test-entity" };

        var response = await DirectDispatchHelper.InvokeAsync<TestResponse>(
            _serviceProvider,
            "test-dispatch",
            "CreateEntityAsync",
            request,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("test-entity", response.Name);
        Assert.NotEqual(Guid.Empty, response.EntityId);
    }

    [Fact]
    public async Task InvokeAsync_WhenServiceReturnsBadRequest_ThrowsApiException()
    {
        var request = new TestRequest { Name = "bad-request" };

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            DirectDispatchHelper.InvokeAsync<TestResponse>(
                _serviceProvider,
                "test-dispatch",
                "GetBadRequestEntityAsync",
                request,
                CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }

    // =========================================================================
    // InvokeAsync — Error Path
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_WhenServiceReturnsNotFound_ThrowsApiException()
    {
        var request = new TestRequest { Name = "missing" };

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            DirectDispatchHelper.InvokeAsync<TestResponse>(
                _serviceProvider,
                "test-dispatch",
                "GetMissingEntityAsync",
                request,
                CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WhenServiceReturnsConflict_ThrowsApiException()
    {
        var request = new TestRequest { Name = "conflict" };

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            DirectDispatchHelper.InvokeAsync<TestResponse>(
                _serviceProvider,
                "test-dispatch",
                "GetConflictEntityAsync",
                request,
                CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
    }

    // =========================================================================
    // InvokeVoidAsync — Success & Error
    // =========================================================================

    [Fact]
    public async Task InvokeVoidAsync_WhenServiceReturnsOk_CompletesSuccessfully()
    {
        var request = new TestRequest { Name = "delete-me" };

        await DirectDispatchHelper.InvokeVoidAsync(
            _serviceProvider,
            "test-dispatch",
            "DeleteEntityAsync",
            request,
            CancellationToken.None);

        // No exception = success
    }

    [Fact]
    public async Task InvokeVoidAsync_WhenServiceReturnsError_ThrowsApiException()
    {
        var request = new TestRequest { Name = "missing" };

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            DirectDispatchHelper.InvokeVoidAsync(
                _serviceProvider,
                "test-dispatch",
                "GetMissingEntityAsync",
                request,
                CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
    }

    // =========================================================================
    // Service Resolution — Naming Convention
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_ResolvesServiceByNamingConvention()
    {
        // "test-dispatch" → ITestDispatchService (hyphenated name → PascalCase)
        var request = new TestRequest { Name = "convention-test" };

        var response = await DirectDispatchHelper.InvokeAsync<TestResponse>(
            _serviceProvider,
            "test-dispatch",
            "CreateEntityAsync",
            request,
            CancellationToken.None);

        Assert.Equal("convention-test", response.Name);
    }

    [Fact]
    public async Task InvokeAsync_WhenServiceNotRegistered_ThrowsInvalidOperation()
    {
        var request = new TestRequest { Name = "nope" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DirectDispatchHelper.InvokeAsync<TestResponse>(
                _serviceProvider,
                "nonexistent-service",
                "SomeMethodAsync",
                request,
                CancellationToken.None));
    }

    [Fact]
    public async Task InvokeAsync_WhenMethodNotFound_ThrowsInvalidOperation()
    {
        var request = new TestRequest { Name = "nope" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DirectDispatchHelper.InvokeAsync<TestResponse>(
                _serviceProvider,
                "test-dispatch",
                "NonExistentMethodAsync",
                request,
                CancellationToken.None));
    }

    // =========================================================================
    // Scoping — Each Call Gets Fresh Scope
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_CreatesScopedServicePerCall()
    {
        var request = new TestRequest { Name = "scope-test" };

        var response1 = await DirectDispatchHelper.InvokeAsync<TestResponse>(
            _serviceProvider,
            "test-dispatch",
            "CreateEntityAsync",
            request,
            CancellationToken.None);

        var response2 = await DirectDispatchHelper.InvokeAsync<TestResponse>(
            _serviceProvider,
            "test-dispatch",
            "CreateEntityAsync",
            request,
            CancellationToken.None);

        // Each call creates a new scope → new service instance → different EntityIds
        Assert.NotEqual(response1.EntityId, response2.EntityId);
    }

    // =========================================================================
    // InvokeDirectAsync<TService, TRequest, TResponse> — typed 3-generic dispatch
    // =========================================================================

    [Fact]
    public async Task InvokeDirectAsync_WhenServiceReturnsOk_ReturnsResponse()
    {
        var request = new TestRequest { Name = "typed-entity" };

        var response = await DirectDispatchHelper.InvokeDirectAsync<ITestDispatchService, TestRequest, TestResponse>(
            _serviceProvider,
            request,
            static (svc, req, ct) => svc.CreateEntityAsync(req, ct),
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("typed-entity", response.Name);
        Assert.NotEqual(Guid.Empty, response.EntityId);
    }

    [Fact]
    public async Task InvokeDirectAsync_WhenServiceReturnsNotFound_ThrowsApiException()
    {
        var request = new TestRequest { Name = "missing" };

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            DirectDispatchHelper.InvokeDirectAsync<ITestDispatchService, TestRequest, TestResponse>(
                _serviceProvider,
                request,
                static (svc, req, ct) => svc.GetMissingEntityAsync(req, ct),
                CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task InvokeDirectAsync_WhenServiceReturnsConflict_ThrowsApiException()
    {
        var request = new TestRequest { Name = "conflict" };

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            DirectDispatchHelper.InvokeDirectAsync<ITestDispatchService, TestRequest, TestResponse>(
                _serviceProvider,
                request,
                static (svc, req, ct) => svc.GetConflictEntityAsync(req, ct),
                CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task InvokeDirectAsync_WhenSuccessStatusWithNullPayload_ThrowsInvalidOperation()
    {
        // Contract: success status + null payload is a service bug, not a callable outcome.
        // The helper surfaces it as InvalidOperationException rather than returning null to the caller.
        var request = new TestRequest { Name = "null-ok" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DirectDispatchHelper.InvokeDirectAsync<ITestDispatchService, TestRequest, TestResponse>(
                _serviceProvider,
                request,
                static (svc, req, ct) => svc.CreateEntityOkButNullAsync(req, ct),
                CancellationToken.None));

        Assert.Contains("null response", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeDirectAsync_WhenServiceNotRegistered_ThrowsInvalidOperation()
    {
        // GetRequiredService<T>() throws InvalidOperationException when T is not registered.
        // Use a provider with no registrations to verify the failure path.
        var emptyProvider = new ServiceCollection().BuildServiceProvider();
        var request = new TestRequest { Name = "no-service" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DirectDispatchHelper.InvokeDirectAsync<ITestDispatchService, TestRequest, TestResponse>(
                emptyProvider,
                request,
                static (svc, req, ct) => svc.CreateEntityAsync(req, ct),
                CancellationToken.None));
    }

    [Fact]
    public async Task InvokeDirectAsync_CreatesScopedServicePerCall()
    {
        var request = new TestRequest { Name = "scope-test-typed" };

        var response1 = await DirectDispatchHelper.InvokeDirectAsync<ITestDispatchService, TestRequest, TestResponse>(
            _serviceProvider,
            request,
            static (svc, req, ct) => svc.CreateEntityAsync(req, ct),
            CancellationToken.None);

        var response2 = await DirectDispatchHelper.InvokeDirectAsync<ITestDispatchService, TestRequest, TestResponse>(
            _serviceProvider,
            request,
            static (svc, req, ct) => svc.CreateEntityAsync(req, ct),
            CancellationToken.None);

        // Scoped service → new instance per call → unique EntityId per response.
        Assert.NotEqual(response1.EntityId, response2.EntityId);
    }

    // =========================================================================
    // InvokeDirectVoidAsync<TService, TRequest> — typed void dispatch
    // =========================================================================

    [Fact]
    public async Task InvokeDirectVoidAsync_WhenServiceReturnsOk_CompletesSuccessfully()
    {
        var request = new TestRequest { Name = "void-ok" };

        await DirectDispatchHelper.InvokeDirectVoidAsync<ITestDispatchService, TestRequest>(
            _serviceProvider,
            request,
            static (svc, req, ct) => svc.ModifyEntityAsync(req, ct),
            CancellationToken.None);

        // No exception = success.
    }

    [Fact]
    public async Task InvokeDirectVoidAsync_WhenServiceReturnsError_ThrowsApiException()
    {
        var request = new TestRequest { Name = "void-fail" };

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            DirectDispatchHelper.InvokeDirectVoidAsync<ITestDispatchService, TestRequest>(
                _serviceProvider,
                request,
                static (svc, req, ct) => svc.ModifyEntityFailingAsync(req, ct),
                CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task InvokeDirectVoidAsync_WhenServiceNotRegistered_ThrowsInvalidOperation()
    {
        var emptyProvider = new ServiceCollection().BuildServiceProvider();
        var request = new TestRequest { Name = "no-service" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DirectDispatchHelper.InvokeDirectVoidAsync<ITestDispatchService, TestRequest>(
                emptyProvider,
                request,
                static (svc, req, ct) => svc.ModifyEntityAsync(req, ct),
                CancellationToken.None));
    }

    // =========================================================================
    // InvokeDirectWithAuthAsync<TService, TRequest, TResponse> — typed auth dispatch
    // =========================================================================

    [Fact]
    public async Task InvokeDirectWithAuthAsync_WhenServiceReturnsOk_ReturnsResponse()
    {
        var request = new TestRequest { Name = "auth-entity" };

        var response = await DirectDispatchHelper.InvokeDirectWithAuthAsync<ITestDispatchService, TestRequest, TestResponse>(
            _serviceProvider,
            "the-jwt-token",
            request,
            static (svc, token, req, ct) => svc.CreateEntityWithAuthAsync(token, req, ct),
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("auth-entity", response.Name);
        Assert.NotEqual(Guid.Empty, response.EntityId);
    }

    [Fact]
    public async Task InvokeDirectWithAuthAsync_PassesTokenThroughToServiceMethod()
    {
        // The helper must forward the token argument unchanged — the test service echoes the
        // received token onto the response so we can assert passthrough directly.
        var request = new TestRequest { Name = "token-passthrough" };

        var response = await DirectDispatchHelper.InvokeDirectWithAuthAsync<ITestDispatchService, TestRequest, TestResponse>(
            _serviceProvider,
            "expected-token-value",
            request,
            static (svc, token, req, ct) => svc.CreateEntityWithAuthAsync(token, req, ct),
            CancellationToken.None);

        Assert.Equal("expected-token-value", response.ReceivedToken);
    }

    [Fact]
    public async Task InvokeDirectWithAuthAsync_WhenServiceReturnsConflict_ThrowsApiException()
    {
        var request = new TestRequest { Name = "auth-conflict" };

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            DirectDispatchHelper.InvokeDirectWithAuthAsync<ITestDispatchService, TestRequest, TestResponse>(
                _serviceProvider,
                "the-jwt-token",
                request,
                static (svc, token, req, ct) => svc.GetConflictEntityWithAuthAsync(token, req, ct),
                CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task InvokeDirectWithAuthAsync_WhenSuccessStatusWithNullPayload_ThrowsInvalidOperation()
    {
        var request = new TestRequest { Name = "auth-null-ok" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DirectDispatchHelper.InvokeDirectWithAuthAsync<ITestDispatchService, TestRequest, TestResponse>(
                _serviceProvider,
                "the-jwt-token",
                request,
                static (svc, token, req, ct) => svc.CreateEntityWithAuthOkButNullAsync(token, req, ct),
                CancellationToken.None));

        Assert.Contains("null response", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeDirectWithAuthAsync_WhenServiceNotRegistered_ThrowsInvalidOperation()
    {
        var emptyProvider = new ServiceCollection().BuildServiceProvider();
        var request = new TestRequest { Name = "no-service" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DirectDispatchHelper.InvokeDirectWithAuthAsync<ITestDispatchService, TestRequest, TestResponse>(
                emptyProvider,
                "the-jwt-token",
                request,
                static (svc, token, req, ct) => svc.CreateEntityWithAuthAsync(token, req, ct),
                CancellationToken.None));
    }

    // =========================================================================
    // InvokeDirectWithAuthVoidAsync<TService, TRequest> — typed auth void dispatch
    // =========================================================================

    [Fact]
    public async Task InvokeDirectWithAuthVoidAsync_WhenTokenMatches_CompletesSuccessfully()
    {
        // The test service returns OK iff the received token equals the expected literal,
        // so a clean completion here verifies both void success AND token passthrough.
        var request = new TestRequest { Name = "auth-void-ok" };

        await DirectDispatchHelper.InvokeDirectWithAuthVoidAsync<ITestDispatchService, TestRequest>(
            _serviceProvider,
            "expected-token-value",
            request,
            static (svc, token, req, ct) => svc.DeleteEntityWithAuthAsync(token, req, ct),
            CancellationToken.None);

        // No exception = success, and the service saw the right token.
    }

    [Fact]
    public async Task InvokeDirectWithAuthVoidAsync_WhenTokenMismatches_ThrowsApiException()
    {
        // Complement of the passthrough test: wrong token → service returns BadRequest → helper wraps as ApiException.
        var request = new TestRequest { Name = "auth-void-wrong-token" };

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            DirectDispatchHelper.InvokeDirectWithAuthVoidAsync<ITestDispatchService, TestRequest>(
                _serviceProvider,
                "wrong-token",
                request,
                static (svc, token, req, ct) => svc.DeleteEntityWithAuthAsync(token, req, ct),
                CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task InvokeDirectWithAuthVoidAsync_WhenServiceReturnsError_ThrowsApiException()
    {
        var request = new TestRequest { Name = "auth-void-fail" };

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            DirectDispatchHelper.InvokeDirectWithAuthVoidAsync<ITestDispatchService, TestRequest>(
                _serviceProvider,
                "the-jwt-token",
                request,
                static (svc, token, req, ct) => svc.DeleteEntityWithAuthFailingAsync(token, req, ct),
                CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task InvokeDirectWithAuthVoidAsync_WhenServiceNotRegistered_ThrowsInvalidOperation()
    {
        var emptyProvider = new ServiceCollection().BuildServiceProvider();
        var request = new TestRequest { Name = "no-service" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DirectDispatchHelper.InvokeDirectWithAuthVoidAsync<ITestDispatchService, TestRequest>(
                emptyProvider,
                "the-jwt-token",
                request,
                static (svc, token, req, ct) => svc.DeleteEntityWithAuthAsync(token, req, ct),
                CancellationToken.None));
    }
}

// =========================================================================
// Test Service — defined in test assembly (no cross-plugin dependency)
// =========================================================================

/// <summary>
/// Test request model for direct dispatch testing.
/// </summary>
public class TestRequest
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Test response model for direct dispatch testing.
/// </summary>
public class TestResponse
{
    public Guid EntityId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Token the service observed on the call boundary. Only populated by the auth-dispatch
    /// test methods; existing non-auth tests ignore this field.
    /// </summary>
    public string? ReceivedToken { get; set; }
}

/// <summary>
/// Test service interface following the Bannou naming convention:
/// service name "test-dispatch" → ITestDispatchService.
/// Methods return (StatusCodes, TResponse?) tuples or Task&lt;StatusCodes&gt; (void) matching the
/// two shapes the DirectDispatchHelper overloads expect.
/// </summary>
public interface ITestDispatchService
{
    // Form A — MethodAsync(TRequest, CancellationToken) → Task<(StatusCodes, TResponse?)>
    Task<(StatusCodes, TestResponse?)> CreateEntityAsync(TestRequest body, CancellationToken ct);
    Task<(StatusCodes, TestResponse?)> GetBadRequestEntityAsync(TestRequest body, CancellationToken ct);
    Task<(StatusCodes, TestResponse?)> GetMissingEntityAsync(TestRequest body, CancellationToken ct);
    Task<(StatusCodes, TestResponse?)> GetConflictEntityAsync(TestRequest body, CancellationToken ct);
    Task<(StatusCodes, TestResponse?)> DeleteEntityAsync(TestRequest body, CancellationToken ct);

    // Form A — success status with null payload (for InvalidOperationException path on the typed overload)
    Task<(StatusCodes, TestResponse?)> CreateEntityOkButNullAsync(TestRequest body, CancellationToken ct);

    // Form A (void) — MethodAsync(TRequest, CancellationToken) → Task<StatusCodes>
    Task<StatusCodes> ModifyEntityAsync(TestRequest body, CancellationToken ct);
    Task<StatusCodes> ModifyEntityFailingAsync(TestRequest body, CancellationToken ct);

    // Form B — MethodAsync(string token, TRequest, CancellationToken) → Task<(StatusCodes, TResponse?)>
    Task<(StatusCodes, TestResponse?)> CreateEntityWithAuthAsync(string token, TestRequest body, CancellationToken ct);
    Task<(StatusCodes, TestResponse?)> GetConflictEntityWithAuthAsync(string token, TestRequest body, CancellationToken ct);
    Task<(StatusCodes, TestResponse?)> CreateEntityWithAuthOkButNullAsync(string token, TestRequest body, CancellationToken ct);

    // Form B (void) — MethodAsync(string token, TRequest, CancellationToken) → Task<StatusCodes>
    Task<StatusCodes> DeleteEntityWithAuthAsync(string token, TestRequest body, CancellationToken ct);
    Task<StatusCodes> DeleteEntityWithAuthFailingAsync(string token, TestRequest body, CancellationToken ct);
}

/// <summary>
/// Test service implementation providing predictable responses for dispatch testing.
/// The auth methods echo the received token onto the response (Form B with result) or
/// short-circuit on a known-good literal (Form B void) so tests can verify token passthrough.
/// </summary>
public class TestDispatchService : ITestDispatchService
{
    /// <summary>
    /// Literal the auth-void helper test uses to distinguish "passthrough worked" from "failure".
    /// </summary>
    public const string ExpectedAuthToken = "expected-token-value";

    public async Task<(StatusCodes, TestResponse?)> CreateEntityAsync(TestRequest body, CancellationToken ct)
    {
        await Task.CompletedTask;
        return (StatusCodes.OK, new TestResponse { EntityId = Guid.NewGuid(), Name = body.Name });
    }

    public async Task<(StatusCodes, TestResponse?)> GetBadRequestEntityAsync(TestRequest body, CancellationToken ct)
    {
        await Task.CompletedTask;
        return (StatusCodes.BadRequest, null);
    }

    public async Task<(StatusCodes, TestResponse?)> GetMissingEntityAsync(TestRequest body, CancellationToken ct)
    {
        await Task.CompletedTask;
        return (StatusCodes.NotFound, null);
    }

    public async Task<(StatusCodes, TestResponse?)> GetConflictEntityAsync(TestRequest body, CancellationToken ct)
    {
        await Task.CompletedTask;
        return (StatusCodes.Conflict, null);
    }

    public async Task<(StatusCodes, TestResponse?)> DeleteEntityAsync(TestRequest body, CancellationToken ct)
    {
        await Task.CompletedTask;
        return (StatusCodes.OK, new TestResponse { EntityId = Guid.NewGuid(), Name = body.Name });
    }

    public async Task<(StatusCodes, TestResponse?)> CreateEntityOkButNullAsync(TestRequest body, CancellationToken ct)
    {
        // Deliberately returns OK with a null payload to exercise the typed overload's
        // null-response-on-success guard.
        await Task.CompletedTask;
        return (StatusCodes.OK, null);
    }

    public async Task<StatusCodes> ModifyEntityAsync(TestRequest body, CancellationToken ct)
    {
        await Task.CompletedTask;
        return StatusCodes.OK;
    }

    public async Task<StatusCodes> ModifyEntityFailingAsync(TestRequest body, CancellationToken ct)
    {
        await Task.CompletedTask;
        return StatusCodes.BadRequest;
    }

    public async Task<(StatusCodes, TestResponse?)> CreateEntityWithAuthAsync(string token, TestRequest body, CancellationToken ct)
    {
        await Task.CompletedTask;
        return (StatusCodes.OK, new TestResponse
        {
            EntityId = Guid.NewGuid(),
            Name = body.Name,
            ReceivedToken = token,
        });
    }

    public async Task<(StatusCodes, TestResponse?)> GetConflictEntityWithAuthAsync(string token, TestRequest body, CancellationToken ct)
    {
        await Task.CompletedTask;
        return (StatusCodes.Conflict, null);
    }

    public async Task<(StatusCodes, TestResponse?)> CreateEntityWithAuthOkButNullAsync(string token, TestRequest body, CancellationToken ct)
    {
        // OK with null payload — exercises the WithAuth typed overload's null-response guard.
        await Task.CompletedTask;
        return (StatusCodes.OK, null);
    }

    public async Task<StatusCodes> DeleteEntityWithAuthAsync(string token, TestRequest body, CancellationToken ct)
    {
        // Passthrough verification: OK iff the token matches the expected literal.
        // This lets the void-with-auth test prove the helper forwarded the token correctly
        // without needing a response payload to inspect.
        await Task.CompletedTask;
        return token == ExpectedAuthToken ? StatusCodes.OK : StatusCodes.BadRequest;
    }

    public async Task<StatusCodes> DeleteEntityWithAuthFailingAsync(string token, TestRequest body, CancellationToken ct)
    {
        await Task.CompletedTask;
        return StatusCodes.NotFound;
    }
}
