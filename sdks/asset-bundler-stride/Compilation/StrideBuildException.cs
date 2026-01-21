namespace BeyondImmersion.Bannou.AssetBundler.Stride.Compilation;

/// <summary>
/// Exception thrown when Stride asset compilation fails.
/// </summary>
public sealed class StrideBuildException : Exception
{
    /// <summary>
    /// Creates a new Stride build exception.
    /// </summary>
    /// <param name="message">Error message.</param>
    public StrideBuildException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates a new Stride build exception with inner exception.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">Inner exception.</param>
    public StrideBuildException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a build exception from build output.
    /// </summary>
    /// <param name="exitCode">Process exit code.</param>
    /// <param name="errorOutput">Standard error output.</param>
    /// <param name="standardOutput">Standard output (may contain errors).</param>
    /// <returns>Exception with formatted message.</returns>
    public static StrideBuildException FromBuildOutput(int exitCode, string errorOutput, string? standardOutput = null)
    {
        var message = $"Stride build failed with exit code {exitCode}";

        if (!string.IsNullOrWhiteSpace(errorOutput))
        {
            message += $"\n\nError output:\n{errorOutput.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(standardOutput))
        {
            // Extract error lines from stdout (MSBuild sometimes puts errors there)
            var errorLines = standardOutput
                .Split('\n')
                .Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("failed", StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .ToArray();

            if (errorLines.Length > 0)
            {
                message += $"\n\nBuild errors:\n{string.Join('\n', errorLines)}";
            }
        }

        return new StrideBuildException(message);
    }

    /// <summary>
    /// Gets the exit code from the build process, if available.
    /// </summary>
    public int? ExitCode { get; init; }

    /// <summary>
    /// Gets the error output from the build process.
    /// </summary>
    public string? ErrorOutput { get; init; }

    /// <summary>
    /// Gets the list of assets that failed to compile.
    /// </summary>
    public IReadOnlyList<string>? FailedAssets { get; init; }
}
