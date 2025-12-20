namespace BeyondImmersion.BannouService.Voice.Clients;

/// <summary>
/// Client interface for Kamailio SIP proxy control via JSONRPC 2.0 protocol.
/// Enables Bannou VoiceService to manage SIP dialogs and monitor Kamailio health.
/// </summary>
public interface IKamailioClient
{
    /// <summary>
    /// Gets all active SIP dialogs from Kamailio.
    /// Uses dlg.list JSONRPC method.
    /// </summary>
    Task<IEnumerable<ActiveDialog>> GetActiveDialogsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates a specific SIP dialog by ID.
    /// Uses dlg.end_dlg JSONRPC method.
    /// </summary>
    Task<bool> TerminateDialogAsync(string dialogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads the dispatcher list for load balancing configuration.
    /// Uses dispatcher.reload JSONRPC method.
    /// </summary>
    Task<bool> ReloadDispatcherAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets Kamailio statistics for monitoring.
    /// Uses stats.get_statistics JSONRPC method.
    /// </summary>
    Task<KamailioStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Health check for Kamailio JSONRPC endpoint.
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an active SIP dialog in Kamailio.
/// </summary>
public class ActiveDialog
{
    /// <summary>
    /// Unique dialog identifier.
    /// </summary>
    public string DialogId { get; init; } = string.Empty;

    /// <summary>
    /// Call-ID header value.
    /// </summary>
    public string CallId { get; init; } = string.Empty;

    /// <summary>
    /// From tag for the dialog.
    /// </summary>
    public string FromTag { get; init; } = string.Empty;

    /// <summary>
    /// To tag for the dialog.
    /// </summary>
    public string ToTag { get; init; } = string.Empty;

    /// <summary>
    /// From URI (caller).
    /// </summary>
    public string FromUri { get; init; } = string.Empty;

    /// <summary>
    /// To URI (callee).
    /// </summary>
    public string ToUri { get; init; } = string.Empty;

    /// <summary>
    /// Dialog state (e.g., "confirmed", "early").
    /// </summary>
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// When the dialog was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Kamailio statistics for monitoring and health checking.
/// </summary>
public class KamailioStats
{
    /// <summary>
    /// Number of active dialogs.
    /// </summary>
    public int ActiveDialogs { get; init; }

    /// <summary>
    /// Number of current transactions.
    /// </summary>
    public int CurrentTransactions { get; init; }

    /// <summary>
    /// Total received requests.
    /// </summary>
    public long TotalReceivedRequests { get; init; }

    /// <summary>
    /// Total received replies.
    /// </summary>
    public long TotalReceivedReplies { get; init; }

    /// <summary>
    /// Process uptime in seconds.
    /// </summary>
    public long UptimeSeconds { get; init; }

    /// <summary>
    /// Memory usage in bytes.
    /// </summary>
    public long MemoryUsed { get; init; }
}
