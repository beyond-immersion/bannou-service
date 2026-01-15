using BeyondImmersion.Bannou.AssetBundler.Upload;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Tests.Upload;

/// <summary>
/// Tests for UrlConverter utility methods.
/// </summary>
public class UrlConverterTests
{
    #region ToWebSocketUrl Tests

    [Theory]
    [InlineData("https://example.com", "wss://example.com")]
    [InlineData("http://example.com", "ws://example.com")]
    [InlineData("HTTPS://EXAMPLE.COM", "wss://EXAMPLE.COM")]
    [InlineData("HTTP://EXAMPLE.COM", "ws://EXAMPLE.COM")]
    public void ToWebSocketUrl_ConvertsHttpToWs(string input, string expected)
    {
        Assert.Equal(expected, UrlConverter.ToWebSocketUrl(input));
    }

    [Theory]
    [InlineData("wss://example.com", "wss://example.com")]
    [InlineData("ws://example.com", "ws://example.com")]
    public void ToWebSocketUrl_PreservesExistingWs(string input, string expected)
    {
        Assert.Equal(expected, UrlConverter.ToWebSocketUrl(input));
    }

    [Theory]
    [InlineData("https://example.com:8080/path", "wss://example.com:8080/path")]
    [InlineData("http://localhost:5000", "ws://localhost:5000")]
    [InlineData("https://api.example.com/v1/connect", "wss://api.example.com/v1/connect")]
    public void ToWebSocketUrl_PreservesPortAndPath(string input, string expected)
    {
        Assert.Equal(expected, UrlConverter.ToWebSocketUrl(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ToWebSocketUrl_EmptyOrNull_ReturnsInput(string? input)
    {
        Assert.Equal(input, UrlConverter.ToWebSocketUrl(input!));
    }

    [Fact]
    public void ToWebSocketUrl_NoScheme_ReturnsUnchanged()
    {
        Assert.Equal("example.com", UrlConverter.ToWebSocketUrl("example.com"));
    }

    #endregion

    #region ToHttpUrl Tests

    [Theory]
    [InlineData("wss://example.com", "https://example.com")]
    [InlineData("ws://example.com", "http://example.com")]
    [InlineData("WSS://EXAMPLE.COM", "https://EXAMPLE.COM")]
    [InlineData("WS://EXAMPLE.COM", "http://EXAMPLE.COM")]
    public void ToHttpUrl_ConvertsWsToHttp(string input, string expected)
    {
        Assert.Equal(expected, UrlConverter.ToHttpUrl(input));
    }

    [Theory]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("http://example.com", "http://example.com")]
    public void ToHttpUrl_PreservesExistingHttp(string input, string expected)
    {
        Assert.Equal(expected, UrlConverter.ToHttpUrl(input));
    }

    [Theory]
    [InlineData("wss://example.com:8080/path", "https://example.com:8080/path")]
    [InlineData("ws://localhost:5000", "http://localhost:5000")]
    [InlineData("wss://api.example.com/v1/connect", "https://api.example.com/v1/connect")]
    public void ToHttpUrl_PreservesPortAndPath(string input, string expected)
    {
        Assert.Equal(expected, UrlConverter.ToHttpUrl(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ToHttpUrl_EmptyOrNull_ReturnsInput(string? input)
    {
        Assert.Equal(input, UrlConverter.ToHttpUrl(input!));
    }

    #endregion

    #region EnsureConnectPath Tests

    [Theory]
    [InlineData("https://example.com", "https://example.com/connect")]
    [InlineData("https://example.com/", "https://example.com/connect")]
    [InlineData("wss://example.com", "wss://example.com/connect")]
    public void EnsureConnectPath_AddsConnectPath(string input, string expected)
    {
        Assert.Equal(expected, UrlConverter.EnsureConnectPath(input));
    }

    [Theory]
    [InlineData("https://example.com/connect", "https://example.com/connect")]
    [InlineData("https://example.com/connect/", "https://example.com/connect")]
    [InlineData("wss://example.com/CONNECT", "wss://example.com/CONNECT")]
    public void EnsureConnectPath_PreservesExistingConnect(string input, string expected)
    {
        Assert.Equal(expected, UrlConverter.EnsureConnectPath(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EnsureConnectPath_EmptyOrNull_ReturnsInput(string? input)
    {
        Assert.Equal(input, UrlConverter.EnsureConnectPath(input!));
    }

    [Theory]
    [InlineData("https://example.com///", "https://example.com/connect")]
    [InlineData("https://example.com/api/", "https://example.com/api/connect")]
    public void EnsureConnectPath_HandlesTrailingSlashes(string input, string expected)
    {
        Assert.Equal(expected, UrlConverter.EnsureConnectPath(input));
    }

    #endregion

    #region Roundtrip Tests

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://localhost:8080")]
    [InlineData("https://api.example.com:443/path")]
    public void Roundtrip_HttpToWsToHttp_PreservesUrl(string original)
    {
        var ws = UrlConverter.ToWebSocketUrl(original);
        var back = UrlConverter.ToHttpUrl(ws);
        Assert.Equal(original, back);
    }

    [Theory]
    [InlineData("wss://example.com")]
    [InlineData("ws://localhost:8080")]
    [InlineData("wss://api.example.com:443/path")]
    public void Roundtrip_WsToHttpToWs_PreservesUrl(string original)
    {
        var http = UrlConverter.ToHttpUrl(original);
        var back = UrlConverter.ToWebSocketUrl(http);
        Assert.Equal(original, back);
    }

    #endregion
}
