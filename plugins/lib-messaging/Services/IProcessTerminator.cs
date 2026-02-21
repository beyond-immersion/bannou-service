#nullable enable

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Abstraction for process termination to enable testability of crash-fast behavior.
/// </summary>
/// <remarks>
/// <para>
/// The MessageRetryBuffer uses a crash-fast philosophy where the node terminates
/// if the retry buffer exceeds size or age thresholds. This interface abstracts
/// the termination mechanism to allow unit testing without actually killing the
/// test process.
/// </para>
/// <para>
/// Production code uses <see cref="DefaultProcessTerminator"/> which calls
/// <see cref="Environment.FailFast(string)"/>. Test code can substitute a mock
/// that captures the termination request.
/// </para>
/// </remarks>
public interface IProcessTerminator
{
    /// <summary>
    /// Terminates the current process immediately with the specified message.
    /// </summary>
    /// <param name="message">The termination reason message for logging/diagnostics.</param>
    /// <remarks>
    /// This method should not return - it either terminates the process or throws
    /// an exception (in test scenarios) to prevent further execution.
    /// </remarks>
    void TerminateProcess(string message);
}

/// <summary>
/// Default implementation that uses <see cref="Environment.FailFast(string)"/>.
/// </summary>
/// <remarks>
/// This is the production implementation - it immediately terminates the process
/// without running finalizers or exception handlers. Use only when unrecoverable
/// failures have occurred and the node must be restarted by the orchestrator.
/// </remarks>
public sealed class DefaultProcessTerminator : IProcessTerminator
{
    /// <inheritdoc />
    public void TerminateProcess(string message)
    {
        Environment.FailFast(message);
    }
}
