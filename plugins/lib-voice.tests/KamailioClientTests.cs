using BeyondImmersion.BannouService.TestUtilities;
using BeyondImmersion.BannouService.Voice.Clients;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using Xunit;

namespace BeyondImmersion.BannouService.Voice.Tests;

/// <summary>
/// Unit tests for KamailioClient health checking.
/// </summary>
public class KamailioClientTests : IDisposable
{
    private readonly Mock<ILogger<KamailioClient>> _mockLogger;
    private readonly Mock<HttpMessageHandler> _mockHandler;
    private readonly List<HttpClient> _createdHttpClients = new();
    private readonly List<HttpResponseMessage> _createdResponses = new();

    public KamailioClientTests()
    {
        _mockLogger = new Mock<ILogger<KamailioClient>>();
        _mockHandler = new Mock<HttpMessageHandler>();
    }

    public void Dispose()
    {
        foreach (var client in _createdHttpClients)
        {
            client.Dispose();
        }
        _createdHttpClients.Clear();

        foreach (var response in _createdResponses)
        {
            response.Dispose();
        }
        _createdResponses.Clear();
    }

    private HttpClient CreateMockedHttpClient(HttpStatusCode statusCode, string? content = null)
    {
        var response = new HttpResponseMessage(statusCode);
        _createdResponses.Add(response);

        if (content != null)
        {
            response.Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json");
        }

        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var httpClient = new HttpClient(_mockHandler.Object);
        _createdHttpClients.Add(httpClient);
        return httpClient;
    }

    private HttpClient CreateThrowingHttpClient()
    {
        var httpClient = new HttpClient(_mockHandler.Object);
        _createdHttpClients.Add(httpClient);
        return httpClient;
    }

    private KamailioClient CreateClient(HttpClient httpClient)
    {
        return new KamailioClient(httpClient, "localhost", 5080, TimeSpan.FromSeconds(5), _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<KamailioClient>();
        var httpClient = new HttpClient();
        _createdHttpClients.Add(httpClient);
        var client = new KamailioClient(httpClient, "localhost", 5080, TimeSpan.FromSeconds(5), _mockLogger.Object);
        Assert.NotNull(client);
    }

    #endregion

    #region IsHealthyAsync Tests

    [Fact]
    public async Task IsHealthyAsync_WhenHealthEndpointReturnsSuccess_ReturnsTrue()
    {
        // Arrange
        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK, "OK");
        var client = CreateClient(httpClient);

        // Act
        var result = await client.IsHealthyAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsHealthyAsync_WhenHealthEndpointReturnsError_ReturnsFalse()
    {
        // Arrange
        var httpClient = CreateMockedHttpClient(HttpStatusCode.ServiceUnavailable);
        var client = CreateClient(httpClient);

        // Act
        var result = await client.IsHealthyAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsHealthyAsync_WhenExceptionOccurs_ReturnsFalse()
    {
        // Arrange
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var httpClient = CreateThrowingHttpClient();
        var client = CreateClient(httpClient);

        // Act
        var result = await client.IsHealthyAsync();

        // Assert
        Assert.False(result);
    }

    #endregion
}
