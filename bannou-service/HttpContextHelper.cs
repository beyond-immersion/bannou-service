using Microsoft.AspNetCore.Http;

namespace BeyondImmersion.BannouService;

/// <summary>
/// Helper methods for extracting data from HTTP context in generated controllers.
/// </summary>
public static class HttpContextHelper
{
    /// <summary>
    /// Extract Bearer token from Authorization header.
    /// </summary>
    /// <param name="context">HTTP context containing the request headers</param>
    /// <returns>Bearer token without "Bearer " prefix, or null if not found</returns>
    public static string? ExtractBearerToken(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
        {
            return authHeader.Substring(7);
        }
        return null;
    }

    /// <summary>
    /// Extract raw header value by name.
    /// </summary>
    /// <param name="context">HTTP context containing the request headers</param>
    /// <param name="headerName">Name of the header to extract</param>
    /// <returns>Header value or null if not found</returns>
    public static string? ExtractHeader(HttpContext context, string headerName)
    {
        return context.Request.Headers[headerName].FirstOrDefault();
    }

    /// <summary>
    /// Extract multiple headers into a dictionary.
    /// </summary>
    /// <param name="context">HTTP context containing the request headers</param>
    /// <param name="headerNames">Names of headers to extract</param>
    /// <returns>Dictionary of header name to value (null if header not found)</returns>
    public static Dictionary<string, string?> ExtractHeaders(HttpContext context, params string[] headerNames)
    {
        var result = new Dictionary<string, string?>();
        foreach (var headerName in headerNames)
        {
            result[headerName] = ExtractHeader(context, headerName);
        }
        return result;
    }
}
