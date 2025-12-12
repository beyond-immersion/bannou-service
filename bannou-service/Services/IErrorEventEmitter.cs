using BeyondImmersion.BannouService.Events;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Shared contract for emitting ServiceErrorEvent for unexpected/internal failures.
/// </summary>
public interface IErrorEventEmitter
{
    /// <inheritdoc/>
    Task<bool> TryPublishAsync(
        string serviceId,
        string operation,
        string errorType,
        string message,
        string? dependency = null,
        string? endpoint = null,
        ServiceErrorEventSeverity severity = ServiceErrorEventSeverity.Error,
        object? details = null,
        string? stack = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
