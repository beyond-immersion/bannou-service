using BeyondImmersion.BannouService.ServiceClients;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BeyondImmersion.BannouService.Tests;

/// <summary>
/// Unit tests for the IServiceNavigator infrastructure including:
/// - ServiceRequestContext (AsyncLocal storage)
/// - ServiceRequestContextMiddleware (header extraction)
/// - SessionIdForwardingHandler (outbound header propagation)
/// </summary>
[Collection("unit tests")]
public class ServiceNavigatorTests : IClassFixture<CollectionFixture>, IDisposable
{
    public ServiceNavigatorTests(CollectionFixture _)
    {
        // Ensure clean context for each test
        ServiceRequestContext.Clear();
    }

    public void Dispose()
    {
        // Clean up after each test
        ServiceRequestContext.Clear();
        GC.SuppressFinalize(this);
    }

    #region ServiceRequestContext Tests

    [Fact]
    public void ServiceRequestContext_SessionId_SetAndGet()
    {
        // Arrange & Act
        ServiceRequestContext.SessionId = "test-session-123";

        // Assert
        Assert.Equal("test-session-123", ServiceRequestContext.SessionId);
    }

    [Fact]
    public void ServiceRequestContext_CorrelationId_SetAndGet()
    {
        // Arrange & Act
        ServiceRequestContext.CorrelationId = "corr-456";

        // Assert
        Assert.Equal("corr-456", ServiceRequestContext.CorrelationId);
    }

    [Fact]
    public void ServiceRequestContext_HasClientContext_TrueWhenSessionIdSet()
    {
        // Arrange
        Assert.False(ServiceRequestContext.HasClientContext);

        // Act
        ServiceRequestContext.SessionId = "test-session";

        // Assert
        Assert.True(ServiceRequestContext.HasClientContext);
    }

    [Fact]
    public void ServiceRequestContext_HasClientContext_FalseWhenEmpty()
    {
        // Arrange & Act - nothing set

        // Assert
        Assert.False(ServiceRequestContext.HasClientContext);
    }

    [Fact]
    public void ServiceRequestContext_HasClientContext_FalseForEmptyString()
    {
        // Arrange & Act
        ServiceRequestContext.SessionId = "";

        // Assert
        Assert.False(ServiceRequestContext.HasClientContext);
    }

    [Fact]
    public void ServiceRequestContext_Clear_ResetsAllValues()
    {
        // Arrange
        ServiceRequestContext.SessionId = "session-123";
        ServiceRequestContext.CorrelationId = "corr-456";

        // Act
        ServiceRequestContext.Clear();

        // Assert
        Assert.Null(ServiceRequestContext.SessionId);
        Assert.Null(ServiceRequestContext.CorrelationId);
        Assert.False(ServiceRequestContext.HasClientContext);
    }

    [Fact]
    public async Task ServiceRequestContext_AsyncLocal_IsolatesAcrossTasks()
    {
        // Arrange
        var task1SessionId = "";
        var task2SessionId = "";
        var mainSessionId = "main-session";

        ServiceRequestContext.SessionId = mainSessionId;

        // Act - spawn tasks that set their own values
        var task1 = Task.Run(() =>
        {
            ServiceRequestContext.SessionId = "task1-session";
            Thread.Sleep(50); // Ensure overlap
            task1SessionId = ServiceRequestContext.SessionId ?? "";
        });

        var task2 = Task.Run(() =>
        {
            ServiceRequestContext.SessionId = "task2-session";
            Thread.Sleep(50); // Ensure overlap
            task2SessionId = ServiceRequestContext.SessionId ?? "";
        });

        await Task.WhenAll(task1, task2);

        // Assert - each context is isolated
        Assert.Equal("task1-session", task1SessionId);
        Assert.Equal("task2-session", task2SessionId);
        // Note: Main thread context may or may not be preserved depending on AsyncLocal flow
    }

    #endregion

    #region ServiceRequestContextMiddleware Tests

    [Fact]
    public async Task Middleware_ExtractsSessionIdHeader()
    {
        // Arrange
        string? capturedSessionId = null;
        var middleware = new ServiceRequestContextMiddleware(context =>
        {
            capturedSessionId = ServiceRequestContext.SessionId;
            return Task.CompletedTask;
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[ServiceRequestContextMiddleware.SessionIdHeader] = "ws-session-abc";

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert
        Assert.Equal("ws-session-abc", capturedSessionId);
    }

    [Fact]
    public async Task Middleware_ExtractsCorrelationIdHeader()
    {
        // Arrange
        string? capturedCorrelationId = null;
        var middleware = new ServiceRequestContextMiddleware(context =>
        {
            capturedCorrelationId = ServiceRequestContext.CorrelationId;
            return Task.CompletedTask;
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[ServiceRequestContextMiddleware.CorrelationIdHeader] = "trace-xyz";

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert
        Assert.Equal("trace-xyz", capturedCorrelationId);
    }

    [Fact]
    public async Task Middleware_ExtractsBothHeaders()
    {
        // Arrange
        string? capturedSessionId = null;
        string? capturedCorrelationId = null;
        var middleware = new ServiceRequestContextMiddleware(context =>
        {
            capturedSessionId = ServiceRequestContext.SessionId;
            capturedCorrelationId = ServiceRequestContext.CorrelationId;
            return Task.CompletedTask;
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[ServiceRequestContextMiddleware.SessionIdHeader] = "session-123";
        httpContext.Request.Headers[ServiceRequestContextMiddleware.CorrelationIdHeader] = "corr-456";

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert
        Assert.Equal("session-123", capturedSessionId);
        Assert.Equal("corr-456", capturedCorrelationId);
    }

    [Fact]
    public async Task Middleware_HandlesNoHeaders()
    {
        // Arrange
        string? capturedSessionId = null;
        bool? capturedHasContext = null;
        var middleware = new ServiceRequestContextMiddleware(context =>
        {
            capturedSessionId = ServiceRequestContext.SessionId;
            capturedHasContext = ServiceRequestContext.HasClientContext;
            return Task.CompletedTask;
        });

        var httpContext = new DefaultHttpContext();
        // No headers set

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert
        Assert.Null(capturedSessionId);
        Assert.False(capturedHasContext);
    }

    [Fact]
    public async Task Middleware_ClearsContextAfterRequest()
    {
        // Arrange
        var middleware = new ServiceRequestContextMiddleware(_ => Task.CompletedTask);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[ServiceRequestContextMiddleware.SessionIdHeader] = "temp-session";

        // Act
        await middleware.InvokeAsync(httpContext);

        // Assert - context should be cleared after request
        Assert.Null(ServiceRequestContext.SessionId);
        Assert.False(ServiceRequestContext.HasClientContext);
    }

    [Fact]
    public async Task Middleware_ClearsContextEvenOnException()
    {
        // Arrange
        var middleware = new ServiceRequestContextMiddleware(_ =>
            throw new InvalidOperationException("Test exception"));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[ServiceRequestContextMiddleware.SessionIdHeader] = "exception-session";

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.InvokeAsync(httpContext));

        // Context should still be cleared
        Assert.Null(ServiceRequestContext.SessionId);
    }

    #endregion

    #region SessionIdForwardingHandler Tests

    [Fact]
    public async Task ForwardingHandler_AddsSessionIdHeader()
    {
        // Arrange
        ServiceRequestContext.SessionId = "forward-session-123";

        var mockInnerHandler = new MockHttpMessageHandler();
        using var handler = new SessionIdForwardingHandler(NullLogger<SessionIdForwardingHandler>.Instance)
        {
            InnerHandler = mockInnerHandler
        };
        using var client = new HttpClient(handler);

        // Act
        await client.GetAsync("http://test.local/api/test");

        // Assert
        Assert.True(mockInnerHandler.LastRequest?.Headers.Contains(ServiceRequestContextMiddleware.SessionIdHeader));
        var headerValue = mockInnerHandler.LastRequest?.Headers.GetValues(ServiceRequestContextMiddleware.SessionIdHeader).First();
        Assert.Equal("forward-session-123", headerValue);
    }

    [Fact]
    public async Task ForwardingHandler_AddsCorrelationIdHeader()
    {
        // Arrange
        ServiceRequestContext.CorrelationId = "forward-corr-456";

        var mockInnerHandler = new MockHttpMessageHandler();
        using var handler = new SessionIdForwardingHandler(NullLogger<SessionIdForwardingHandler>.Instance)
        {
            InnerHandler = mockInnerHandler
        };
        using var client = new HttpClient(handler);

        // Act
        await client.GetAsync("http://test.local/api/test");

        // Assert
        Assert.True(mockInnerHandler.LastRequest?.Headers.Contains(ServiceRequestContextMiddleware.CorrelationIdHeader));
        var headerValue = mockInnerHandler.LastRequest?.Headers.GetValues(ServiceRequestContextMiddleware.CorrelationIdHeader).First();
        Assert.Equal("forward-corr-456", headerValue);
    }

    [Fact]
    public async Task ForwardingHandler_DoesNotAddHeaderWhenContextEmpty()
    {
        // Arrange - no context set
        var mockInnerHandler = new MockHttpMessageHandler();
        using var handler = new SessionIdForwardingHandler(NullLogger<SessionIdForwardingHandler>.Instance)
        {
            InnerHandler = mockInnerHandler
        };
        using var client = new HttpClient(handler);

        // Act
        await client.GetAsync("http://test.local/api/test");

        // Assert
        Assert.False(mockInnerHandler.LastRequest?.Headers.Contains(ServiceRequestContextMiddleware.SessionIdHeader));
    }

    [Fact]
    public async Task ForwardingHandler_DoesNotOverwriteExistingHeader()
    {
        // Arrange
        ServiceRequestContext.SessionId = "context-session";

        var mockInnerHandler = new MockHttpMessageHandler();
        using var handler = new SessionIdForwardingHandler(NullLogger<SessionIdForwardingHandler>.Instance)
        {
            InnerHandler = mockInnerHandler
        };
        using var client = new HttpClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://test.local/api/test");
        request.Headers.Add(ServiceRequestContextMiddleware.SessionIdHeader, "existing-session");

        // Act
        await client.SendAsync(request);

        // Assert - should keep existing header value
        var headerValue = mockInnerHandler.LastRequest?.Headers.GetValues(ServiceRequestContextMiddleware.SessionIdHeader).First();
        Assert.Equal("existing-session", headerValue);
    }

    #endregion

    #region Header Constants Tests

    [Fact]
    public void HeaderConstants_SessionIdHeader_IsCorrect()
    {
        Assert.Equal("X-Bannou-Session-Id", ServiceRequestContextMiddleware.SessionIdHeader);
    }

    [Fact]
    public void HeaderConstants_CorrelationIdHeader_IsCorrect()
    {
        Assert.Equal("X-Correlation-Id", ServiceRequestContextMiddleware.CorrelationIdHeader);
    }

    #endregion

    /// <summary>
    /// Mock HTTP handler for testing outbound requests.
    /// Properly manages HttpResponseMessage lifecycle.
    /// </summary>
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _okResponse = new(System.Net.HttpStatusCode.OK);

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_okResponse);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _okResponse.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
