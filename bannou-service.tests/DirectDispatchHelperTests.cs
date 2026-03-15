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
}

/// <summary>
/// Test service interface following the Bannou naming convention:
/// service name "test-dispatch" → ITestDispatchService.
/// Methods return (StatusCodes, TResponse?) tuples matching the generated service pattern.
/// </summary>
public interface ITestDispatchService
{
    Task<(StatusCodes, TestResponse?)> CreateEntityAsync(TestRequest body, CancellationToken ct);
    Task<(StatusCodes, TestResponse?)> GetBadRequestEntityAsync(TestRequest body, CancellationToken ct);
    Task<(StatusCodes, TestResponse?)> GetMissingEntityAsync(TestRequest body, CancellationToken ct);
    Task<(StatusCodes, TestResponse?)> GetConflictEntityAsync(TestRequest body, CancellationToken ct);
    Task<(StatusCodes, TestResponse?)> DeleteEntityAsync(TestRequest body, CancellationToken ct);
}

/// <summary>
/// Test service implementation providing predictable responses for dispatch testing.
/// </summary>
public class TestDispatchService : ITestDispatchService
{
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
}
