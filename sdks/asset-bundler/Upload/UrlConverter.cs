namespace BeyondImmersion.Bannou.AssetBundler.Upload;

/// <summary>
/// Utility methods for converting between HTTP and WebSocket URLs.
/// </summary>
public static class UrlConverter
{
    /// <summary>
    /// Converts an HTTP(S) URL to a WebSocket URL (ws:// or wss://).
    /// </summary>
    /// <param name="url">The URL to convert.</param>
    /// <returns>The WebSocket URL.</returns>
    /// <example>
    /// ToWebSocketUrl("https://example.com") -> "wss://example.com"
    /// ToWebSocketUrl("http://localhost:8080") -> "ws://localhost:8080"
    /// ToWebSocketUrl("wss://already.ws") -> "wss://already.ws"
    /// </example>
    public static string ToWebSocketUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        return url
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts a WebSocket URL to an HTTP URL (http:// or https://).
    /// </summary>
    /// <param name="url">The URL to convert.</param>
    /// <returns>The HTTP URL.</returns>
    /// <example>
    /// ToHttpUrl("wss://example.com") -> "https://example.com"
    /// ToHttpUrl("ws://localhost:8080") -> "http://localhost:8080"
    /// ToHttpUrl("https://already.http") -> "https://already.http"
    /// </example>
    public static string ToHttpUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        return url
            .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
            .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures a URL ends with /connect for WebSocket connections.
    /// </summary>
    /// <param name="url">The base URL.</param>
    /// <returns>The URL with /connect path.</returns>
    public static string EnsureConnectPath(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        var trimmed = url.TrimEnd('/');
        if (trimmed.EndsWith("/connect", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        return trimmed + "/connect";
    }
}
