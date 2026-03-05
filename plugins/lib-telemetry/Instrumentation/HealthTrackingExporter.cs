#nullable enable

using OpenTelemetry;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Telemetry.Instrumentation;

/// <summary>
/// Decorating exporter that wraps a real trace exporter to passively observe export outcomes.
/// Tracks consecutive failures and last successful export timestamp for health reporting.
/// </summary>
/// <remarks>
/// Uses Interlocked operations for thread safety per IMPLEMENTATION TENETS (multi-instance safety).
/// All reads are instantaneous from cached state — no active probing occurs.
/// </remarks>
internal sealed class HealthTrackingExporter : BaseExporter<Activity>
{
    private readonly BaseExporter<Activity> _innerExporter;

    private long _consecutiveFailures;
    private long _lastSuccessfulExportTicks;

    /// <summary>
    /// Creates a new HealthTrackingExporter wrapping the given inner exporter.
    /// </summary>
    /// <param name="innerExporter">The real exporter to delegate to.</param>
    public HealthTrackingExporter(BaseExporter<Activity> innerExporter)
    {
        _innerExporter = innerExporter ?? throw new ArgumentNullException(nameof(innerExporter));
    }

    /// <summary>
    /// Whether the OTLP exporter's last export batch succeeded.
    /// </summary>
    public bool IsHealthy => Interlocked.Read(ref _consecutiveFailures) == 0;

    /// <summary>
    /// Number of consecutive export failures since the last success.
    /// </summary>
    public int ConsecutiveFailures => (int)Interlocked.Read(ref _consecutiveFailures);

    /// <summary>
    /// UTC timestamp of the last successful export, or null if no export has succeeded yet.
    /// </summary>
    public DateTimeOffset? LastSuccessfulExportAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastSuccessfulExportTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <inheritdoc/>
    public override ExportResult Export(in Batch<Activity> batch)
    {
        var result = _innerExporter.Export(batch);

        if (result == ExportResult.Success)
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            Interlocked.Exchange(ref _lastSuccessfulExportTicks, DateTimeOffset.UtcNow.Ticks);
        }
        else
        {
            Interlocked.Increment(ref _consecutiveFailures);
        }

        return result;
    }

    /// <inheritdoc/>
    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        return _innerExporter.Shutdown(timeoutMilliseconds);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _innerExporter.Dispose();
        }

        base.Dispose(disposing);
    }
}
