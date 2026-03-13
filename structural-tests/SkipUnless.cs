using Xunit;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Runtime conditional skip helper for informational/checklist structural tests.
/// <para>
/// Replaces static <c>[Fact(Skip = "...")]</c> with runtime evaluation, allowing
/// informational tests to be triggered via environment variable without code changes:
/// <code>BANNOU_RUN_INFORMATIONAL_TESTS=true dotnet test --filter "TestName"</code>
/// </para>
/// <para>
/// <strong>IMPLEMENTATION TENETS Exception</strong>: This class intentionally uses
/// <c>Environment.GetEnvironmentVariable</c> directly rather than a generated configuration
/// class. This is an explicit exception to the Configuration-First tenet (T21). Test runner
/// control via raw environment variables is correct here — these variables govern the test
/// execution environment itself, not service behavior. Adding them to service configuration
/// schemas or .env files would be an anti-pattern: service config is for runtime service
/// behavior behind Docker; this is a <c>dotnet test</c>-level concern that never touches
/// containers or service startup.
/// </para>
/// </summary>
internal static class SkipUnless
{
    private const string InformationalTestsEnvVar = "BANNOU_RUN_INFORMATIONAL_TESTS";

    /// <summary>
    /// Skips the current test unless <c>BANNOU_RUN_INFORMATIONAL_TESTS</c> environment
    /// variable is set to <c>true</c> or <c>1</c>. Call at the top of informational/checklist
    /// test methods that are designed to fail with a diagnostic report.
    /// <para>
    /// Usage: <c>BANNOU_RUN_INFORMATIONAL_TESTS=true dotnet test --filter "TestName"</c>
    /// </para>
    /// </summary>
    /// <param name="reason">Description of what the test produces when run.</param>
    internal static void InformationalTest(string reason)
    {
        var value = Environment.GetEnvironmentVariable(InformationalTestsEnvVar);
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1")
            return;

        Assert.Skip(
            $"Informational test skipped (set {InformationalTestsEnvVar}=true to run): {reason}");
    }
}
