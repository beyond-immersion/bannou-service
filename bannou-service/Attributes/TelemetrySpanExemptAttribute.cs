#nullable enable

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Declares that a class or method is exempt from the structural test
/// <c>Services_HelperFiles_ContainTelemetryInstrumentation</c>.
/// </summary>
/// <remarks>
/// <para>
/// T30 (Telemetry Span Instrumentation) requires all async methods in helper files
/// to contain <c>StartActivity</c> calls. This attribute provides a structured opt-out
/// for classes or methods where telemetry spans genuinely add no value — lightweight
/// delegation, in-memory-only operations, or infrastructure code that is already
/// instrumented at a higher level (e.g., <c>WrapStateStore</c> decorators).
/// </para>
/// <para>
/// <b>When to use (class-level):</b> The entire class contains only trivial async
/// methods with zero expectation of latency or bottleneck. Examples: EventBatcher
/// flush callbacks (single publish call, already nested under a worker span),
/// ABML emitter handlers (pure delegation), infrastructure backend implementations
/// (already instrumented via wrapper decorators).
/// </para>
/// <para>
/// <b>When to use (method-level):</b> A single method in an otherwise-instrumented
/// class is trivial delegation that does not warrant its own span.
/// </para>
/// <para>
/// <b>When NOT to use:</b> Do not use this to avoid adding spans to methods that
/// perform state store operations, inter-service calls, or any I/O. Those methods
/// need spans per T30. If a class grows beyond trivial delegation, remove the
/// attribute and add proper instrumentation.
/// </para>
/// <para>
/// <b>Audit visibility:</b> The companion informational structural test
/// <c>TelemetrySpanExempt_AuditInventory</c> produces a complete inventory of all
/// types and methods with this attribute, including assembly, async method count,
/// and the provided reason. Run with <c>BANNOU_RUN_INFORMATIONAL_TESTS=true</c>
/// to audit the full list at any time.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class TelemetrySpanExemptAttribute : Attribute
{
    /// <summary>
    /// Human-readable reason for the exemption. Surfaces in the informational audit test
    /// so reviewers can evaluate whether the exemption is still appropriate as the class evolves.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Creates a new telemetry span exemption declaration.
    /// </summary>
    /// <param name="reason">
    /// Human-readable reason for the exemption. Example:
    /// <c>"EventBatcher flush callback — single publish call nested under worker span"</c>.
    /// </param>
    public TelemetrySpanExemptAttribute(string reason)
    {
        Reason = reason;
    }
}
