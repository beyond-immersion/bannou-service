namespace BeyondImmersion.BannouService.Broadcast;

/// <summary>
/// FFmpeg process supervision for broadcast outputs.
/// Manages a local process cache (NOT authoritative — Redis is truth).
/// Handles startup reconciliation, fallback cascade, and RTMP validation via FFprobe.
/// </summary>
public interface IBroadcastCoordinator
{
    /// <summary>
    /// Validates that an RTMP URL is reachable via FFprobe.
    /// </summary>
    /// <param name="rtmpUrl">The RTMP URL to validate.</param>
    /// <param name="timeoutSeconds">Probe timeout in seconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the RTMP endpoint is reachable, false otherwise.</returns>
    Task<bool> ValidateRtmpUrlAsync(string rtmpUrl, int timeoutSeconds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts an FFmpeg broadcast process for the given broadcast model.
    /// </summary>
    /// <param name="broadcastId">The broadcast identifier.</param>
    /// <param name="rtmpUrl">The RTMP destination URL.</param>
    /// <param name="sourceUrl">The input source URL (camera RTMP, RTP audio, etc.).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The FFmpeg process ID, or null if start failed.</returns>
    Task<int?> StartBroadcastAsync(Guid broadcastId, string rtmpUrl, string sourceUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops an FFmpeg broadcast process.
    /// </summary>
    /// <param name="broadcastId">The broadcast identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StopBroadcastAsync(Guid broadcastId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restarts an FFmpeg broadcast with new configuration.
    /// Causes a brief interruption (~2-3s) during restart.
    /// </summary>
    /// <param name="broadcastId">The broadcast identifier.</param>
    /// <param name="rtmpUrl">The new RTMP destination URL.</param>
    /// <param name="sourceUrl">The new input source URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new FFmpeg process ID, or null if restart failed.</returns>
    Task<int?> RestartBroadcastAsync(Guid broadcastId, string rtmpUrl, string sourceUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the health status of a locally-owned broadcast process.
    /// </summary>
    /// <param name="broadcastId">The broadcast identifier.</param>
    /// <returns>Health status, or null if this instance doesn't own the broadcast.</returns>
    BroadcastHealth? GetProcessHealth(Guid broadcastId);

    /// <summary>
    /// Gets all broadcast IDs owned by this instance.
    /// </summary>
    IReadOnlyCollection<Guid> LocalBroadcastIds { get; }

    /// <summary>
    /// Removes a local process handle (e.g., when Redis record was deleted externally).
    /// </summary>
    /// <param name="broadcastId">The broadcast identifier.</param>
    void RemoveLocalHandle(Guid broadcastId);
}
