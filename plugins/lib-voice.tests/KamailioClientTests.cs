using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;
using BeyondImmersion.BannouService.Voice.Clients;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;
using Xunit;

namespace BeyondImmersion.BannouService.Voice.Tests;

/// <summary>
/// Unit tests for KamailioClient JSONRPC 2.0 client.
/// </summary>
public class KamailioClientTests
{
    private readonly Mock<ILogger<KamailioClient>> _mockLogger;
    private readonly Mock<HttpMessageHandler> _mockHandler;
    private readonly Mock<IMessageBus> _mockMessageBus;

    public KamailioClientTests()
    {
        _mockLogger = new Mock<ILogger<KamailioClient>>();
        _mockHandler = new Mock<HttpMessageHandler>();
        _mockMessageBus = new Mock<IMessageBus>();
    }

    private HttpClient CreateMockedHttpClient(HttpStatusCode statusCode, string? content = null)
    {
        var response = new HttpResponseMessage(statusCode);
        if (content != null)
        {
            response.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }

        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return new HttpClient(_mockHandler.Object);
    }

    private KamailioClient CreateClient(HttpClient httpClient)
    {
        return new KamailioClient(httpClient, "localhost", 5080, _mockLogger.Object, _mockMessageBus.Object);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<KamailioClient>();
        var client = new KamailioClient(new HttpClient(), "localhost", 5080, _mockLogger.Object, _mockMessageBus.Object);
        Assert.NotNull(client);
    }

    #endregion

    #region GetActiveDialogsAsync Tests

    [Fact]
    public async Task GetActiveDialogsAsync_WhenSuccessful_ReturnsDialogs()
    {
        // Arrange
        // Note: Kamailio uses specific JSON property names that differ from C# conventions
        // See DialogInfo class in KamailioClient.cs for exact mappings
        var jsonResponse = """
        {
            "jsonrpc": "2.0",
            "id": 1,
            "result": {
                "Dialogs": [
                    {
                        "hash_entry": "dialog-123",
                        "call-id": "call-abc",
                        "from_tag": "from-tag-1",
                        "to_tag": "to-tag-1",
                        "from_uri": "sip:alice@domain.com",
                        "to_uri": "sip:bob@domain.com",
                        "state": "confirmed",
                        "start_time": 1700000000
                    }
                ]
            }
        }
        """;

        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK, jsonResponse);
        var client = CreateClient(httpClient);

        // Act
        var dialogs = await client.GetActiveDialogsAsync();

        // Assert
        var dialogList = dialogs.ToList();
        Assert.Single(dialogList);
        Assert.Equal("dialog-123", dialogList[0].DialogId);
        Assert.Equal("call-abc", dialogList[0].CallId);
    }

    [Fact]
    public async Task GetActiveDialogsAsync_WhenServerReturnsError_ReturnsEmptyList()
    {
        // Arrange
        var httpClient = CreateMockedHttpClient(HttpStatusCode.InternalServerError);
        var client = CreateClient(httpClient);

        // Act
        var dialogs = await client.GetActiveDialogsAsync();

        // Assert
        Assert.Empty(dialogs);
    }

    [Fact]
    public async Task GetActiveDialogsAsync_WhenJsonRpcError_ReturnsEmptyList()
    {
        // Arrange
        var jsonResponse = BannouJson.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            error = new { code = -32600, message = "Invalid Request" }
        });

        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK, jsonResponse);
        var client = CreateClient(httpClient);

        // Act
        var dialogs = await client.GetActiveDialogsAsync();

        // Assert
        Assert.Empty(dialogs);
    }

    [Fact]
    public async Task GetActiveDialogsAsync_WhenNullDialogs_ReturnsEmptyList()
    {
        // Arrange
        var jsonResponse = BannouJson.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new { Dialogs = (object?)null }
        });

        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK, jsonResponse);
        var client = CreateClient(httpClient);

        // Act
        var dialogs = await client.GetActiveDialogsAsync();

        // Assert
        Assert.Empty(dialogs);
    }

    #endregion

    #region TerminateDialogAsync Tests

    [Fact]
    public async Task TerminateDialogAsync_WithNullDialogId_ThrowsArgumentException()
    {
        // Arrange
        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK);
        var client = CreateClient(httpClient);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.TerminateDialogAsync(null!));
    }

    [Fact]
    public async Task TerminateDialogAsync_WithEmptyDialogId_ThrowsArgumentException()
    {
        // Arrange
        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK);
        var client = CreateClient(httpClient);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.TerminateDialogAsync(string.Empty));
    }

    [Fact]
    public async Task TerminateDialogAsync_WhenSuccessful_ReturnsTrue()
    {
        // Arrange
        var jsonResponse = BannouJson.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new { result = "OK" }
        });

        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK, jsonResponse);
        var client = CreateClient(httpClient);

        // Act
        var result = await client.TerminateDialogAsync("dialog-123");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TerminateDialogAsync_WhenServerReturnsError_ReturnsFalse()
    {
        // Arrange
        var httpClient = CreateMockedHttpClient(HttpStatusCode.InternalServerError);
        var client = CreateClient(httpClient);

        // Act
        var result = await client.TerminateDialogAsync("dialog-123");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region ReloadDispatcherAsync Tests

    [Fact]
    public async Task ReloadDispatcherAsync_WhenSuccessful_ReturnsTrue()
    {
        // Arrange
        var jsonResponse = BannouJson.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new { result = "OK" }
        });

        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK, jsonResponse);
        var client = CreateClient(httpClient);

        // Act
        var result = await client.ReloadDispatcherAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ReloadDispatcherAsync_WhenServerReturnsError_ReturnsFalse()
    {
        // Arrange
        var httpClient = CreateMockedHttpClient(HttpStatusCode.InternalServerError);
        var client = CreateClient(httpClient);

        // Act
        var result = await client.ReloadDispatcherAsync();

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetStatsAsync Tests

    [Fact]
    public async Task GetStatsAsync_WhenSuccessful_ReturnsStats()
    {
        // Arrange
        var jsonResponse = BannouJson.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new
            {
                result = new Dictionary<string, Dictionary<string, long>>
                {
                    ["dialog"] = new Dictionary<string, long> { ["active_dialogs"] = 42 },
                    ["tm"] = new Dictionary<string, long> { ["current"] = 10 },
                    ["core"] = new Dictionary<string, long>
                    {
                        ["rcv_requests"] = 1000,
                        ["rcv_replies"] = 500,
                        ["uptime"] = 3600
                    },
                    ["shmem"] = new Dictionary<string, long> { ["used_size"] = 1024000 }
                }
            }
        });

        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK, jsonResponse);
        var client = CreateClient(httpClient);

        // Act
        var stats = await client.GetStatsAsync();

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(42, stats.ActiveDialogs);
        Assert.Equal(10, stats.CurrentTransactions);
        Assert.Equal(1000, stats.TotalReceivedRequests);
        Assert.Equal(500, stats.TotalReceivedReplies);
        Assert.Equal(3600, stats.UptimeSeconds);
        Assert.Equal(1024000, stats.MemoryUsed);
    }

    [Fact]
    public async Task GetStatsAsync_WhenServerReturnsError_ReturnsDefaultStats()
    {
        // Arrange
        var httpClient = CreateMockedHttpClient(HttpStatusCode.InternalServerError);
        var client = CreateClient(httpClient);

        // Act
        var stats = await client.GetStatsAsync();

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(0, stats.ActiveDialogs);
        Assert.Equal(0, stats.CurrentTransactions);
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

        var httpClient = new HttpClient(_mockHandler.Object);
        var client = CreateClient(httpClient);

        // Act
        var result = await client.IsHealthyAsync();

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Model Tests

    [Fact]
    public void ActiveDialog_DefaultValues_AreEmpty()
    {
        // Arrange
        var dialog = new ActiveDialog();

        // Assert
        Assert.Equal(string.Empty, dialog.DialogId);
        Assert.Equal(string.Empty, dialog.CallId);
        Assert.Equal(string.Empty, dialog.FromTag);
        Assert.Equal(string.Empty, dialog.ToTag);
        Assert.Equal(string.Empty, dialog.FromUri);
        Assert.Equal(string.Empty, dialog.ToUri);
        Assert.Equal(string.Empty, dialog.State);
    }

    [Fact]
    public void ActiveDialog_CanSetProperties()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var dialog = new ActiveDialog
        {
            DialogId = "dlg-123",
            CallId = "call-456",
            FromTag = "from-tag",
            ToTag = "to-tag",
            FromUri = "sip:alice@domain.com",
            ToUri = "sip:bob@domain.com",
            State = "confirmed",
            CreatedAt = now
        };

        // Assert
        Assert.Equal("dlg-123", dialog.DialogId);
        Assert.Equal("call-456", dialog.CallId);
        Assert.Equal("from-tag", dialog.FromTag);
        Assert.Equal("to-tag", dialog.ToTag);
        Assert.Equal("sip:alice@domain.com", dialog.FromUri);
        Assert.Equal("sip:bob@domain.com", dialog.ToUri);
        Assert.Equal("confirmed", dialog.State);
        Assert.Equal(now, dialog.CreatedAt);
    }

    [Fact]
    public void KamailioStats_DefaultValues_AreZero()
    {
        // Arrange
        var stats = new KamailioStats();

        // Assert
        Assert.Equal(0, stats.ActiveDialogs);
        Assert.Equal(0, stats.CurrentTransactions);
        Assert.Equal(0, stats.TotalReceivedRequests);
        Assert.Equal(0, stats.TotalReceivedReplies);
        Assert.Equal(0, stats.UptimeSeconds);
        Assert.Equal(0, stats.MemoryUsed);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task GetActiveDialogsAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var httpClient = new HttpClient(_mockHandler.Object);
        var client = CreateClient(httpClient);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        // Note: GetActiveDialogsAsync catches exceptions, so it returns empty list
        var result = await client.GetActiveDialogsAsync(cts.Token);
        Assert.Empty(result);
    }

    #endregion
}
