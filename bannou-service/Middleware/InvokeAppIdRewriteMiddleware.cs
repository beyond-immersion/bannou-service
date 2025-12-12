using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Middleware;

/// <summary>
/// Rewrites Dapr invoke URLs so generated controllers can handle requests regardless of app-id.
/// Dapr preserves the /v1.0/invoke/{appId}/method/ prefix when forwarding, while our generated
/// controllers are hardcoded to "/v1.0/invoke/bannou/method". This middleware normalizes the path
/// to use the default app-id so routing continues to work even when sidecars run with different IDs.
/// </summary>
public sealed class InvokeAppIdRewriteMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<InvokeAppIdRewriteMiddleware>? _logger;

    /// <inheritdoc/>
    public InvokeAppIdRewriteMiddleware(RequestDelegate next, ILogger<InvokeAppIdRewriteMiddleware>? logger = null)
    {
        _next = next;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Invoke(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Expected segments: /v1.0/invoke/{appId}/method/{*rest}
        // Split removes leading slash so indexes: 0 = "v1.0", 1 = "invoke", 2 = appId, 3 = "method", 4+ = remainder
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 4 &&
            string.Equals(segments[0], "v1.0", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[1], "invoke", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(segments[3], "method", StringComparison.OrdinalIgnoreCase))
        {
            var originalAppId = segments[2];
            var remainder = segments.Length > 4 ? string.Join('/', segments.Skip(4)) : string.Empty;
            var normalizedPath = "/v1.0/invoke/bannou/method" + (remainder.Length == 0 ? string.Empty : "/" + remainder);

            if (!string.Equals(path, normalizedPath, StringComparison.Ordinal))
            {
                _logger?.LogDebug("Rewriting Dapr invoke path from {Original} (appId={AppId}) to {Normalized}", path, originalAppId, normalizedPath);
                context.Request.Path = normalizedPath;
            }
        }

        await _next(context);
    }
}
