using BeyondImmersion.BannouService.Connect.Protocol;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Connect.Helpers;

/// <summary>
/// Builds capability manifests for WebSocket clients.
/// Extracts and filters endpoint information from service mappings for client consumption.
/// </summary>
public class CapabilityManifestBuilder : ICapabilityManifestBuilder
{
    private readonly ILogger<CapabilityManifestBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of CapabilityManifestBuilder.
    /// </summary>
    public CapabilityManifestBuilder(ILogger<CapabilityManifestBuilder> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ManifestApiEntry> BuildApiList(
        IReadOnlyDictionary<string, Guid> serviceMappings,
        string? serviceFilter = null)
    {
        var apis = new List<ManifestApiEntry>();

        foreach (var mapping in serviceMappings)
        {
            var parsed = ParseEndpointKey(mapping.Key);
            if (parsed == null)
            {
                continue;
            }

            // Apply service filter if provided
            if (!string.IsNullOrEmpty(serviceFilter) &&
                !parsed.ServiceName.StartsWith(serviceFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip template endpoints (zero-copy routing requirement)
            // WebSocket binary protocol requires POST endpoints with JSON body parameters
            // Template paths would require Connect to parse the payload, breaking zero-copy routing
            if (parsed.HasTemplateParams)
            {
                _logger.LogDebug("Skipping template endpoint from capability manifest: {EndpointKey}", mapping.Key);
                continue;
            }

            apis.Add(new ManifestApiEntry
            {
                ServiceGuid = mapping.Value,
                Path = parsed.Path,
                ServiceName = parsed.ServiceName
            });
        }

        return apis;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ManifestShortcutEntry> BuildShortcutList(
        IEnumerable<SessionShortcutData> shortcuts,
        Action<Guid>? onExpiredShortcut = null)
    {
        var result = new List<ManifestShortcutEntry>();

        foreach (var shortcut in shortcuts)
        {
            // Skip expired shortcuts and notify caller for cleanup
            if (shortcut.IsExpired)
            {
                onExpiredShortcut?.Invoke(shortcut.RouteGuid);
                continue;
            }

            // Skip shortcuts with invalid required fields
            if (string.IsNullOrEmpty(shortcut.TargetService) || string.IsNullOrEmpty(shortcut.TargetEndpoint))
            {
                _logger.LogError(
                    "Invalid shortcut {RouteGuid} has null/empty required fields: TargetService={TargetService}, TargetEndpoint={TargetEndpoint}",
                    shortcut.RouteGuid, shortcut.TargetService, shortcut.TargetEndpoint);
                onExpiredShortcut?.Invoke(shortcut.RouteGuid);
                continue;
            }

            result.Add(new ManifestShortcutEntry
            {
                RouteGuid = shortcut.RouteGuid,
                Name = shortcut.Name ?? shortcut.RouteGuid.ToString(),
                TargetService = shortcut.TargetService,
                TargetEndpoint = shortcut.TargetEndpoint,
                Description = shortcut.Description
            });
        }

        return result;
    }

    /// <inheritdoc/>
    public ParsedEndpoint? ParseEndpointKey(string endpointKey)
    {
        // Format: "serviceName:/path"
        var firstColon = endpointKey.IndexOf(':');
        if (firstColon <= 0)
        {
            return null;
        }

        var serviceName = endpointKey[..firstColon];
        var path = endpointKey[(firstColon + 1)..];

        return new ParsedEndpoint
        {
            ServiceName = serviceName,
            Path = path
        };
    }
}
