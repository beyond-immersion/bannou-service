using System.Text.Json;

namespace BeyondImmersion.EdgeTester.Infrastructure;

/// <summary>
/// Helper class for running parity tests between C# and TypeScript SDKs.
/// Provides simple methods to verify that both SDKs produce identical results.
/// </summary>
public sealed class TypeScriptParityHelper : IAsyncDisposable
{
    private readonly TypeScriptTestRunner _runner;
    private bool _disposed;

    private TypeScriptParityHelper(TypeScriptTestRunner runner)
    {
        _runner = runner;
    }

    /// <summary>
    /// Creates and initializes a TypeScript parity helper.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A ready-to-use parity helper.</returns>
    public static async Task<TypeScriptParityHelper> CreateAsync(CancellationToken cancellationToken = default)
    {
        // Find the harness directory relative to the edge-tester location
        var harnessPath = FindHarnessPath();
        TypeScriptTestRunner? runner = null;
        try
        {
            runner = await TypeScriptTestRunner.StartAsync(harnessPath, cancellationToken);
            var helper = new TypeScriptParityHelper(runner);
            runner = null; // Transfer ownership to helper
            return helper;
        }
        finally
        {
            if (runner != null)
            {
                await runner.DisposeAsync();
            }
        }
    }

    private static string FindHarnessPath()
    {
        // Try to find the TypeScript harness directory
        // The harness is at edge-tester/TypeScript relative to the project
        var currentDir = Directory.GetCurrentDirectory();

        // Check if we're running from edge-tester directory
        var directPath = Path.Combine(currentDir, "TypeScript");
        if (Directory.Exists(directPath) && File.Exists(Path.Combine(directPath, "harness.js")))
        {
            return directPath;
        }

        // Check relative to edge-tester binary output
        var parentPath = Path.Combine(currentDir, "..", "..", "..", "TypeScript");
        var normalizedParent = Path.GetFullPath(parentPath);
        if (Directory.Exists(normalizedParent) && File.Exists(Path.Combine(normalizedParent, "harness.js")))
        {
            return normalizedParent;
        }

        // Try from repo root
        var repoPath = Path.Combine(currentDir, "edge-tester", "TypeScript");
        if (Directory.Exists(repoPath) && File.Exists(Path.Combine(repoPath, "harness.js")))
        {
            return repoPath;
        }

        throw new DirectoryNotFoundException(
            $"Could not find TypeScript harness directory. Tried: {directPath}, {normalizedParent}, {repoPath}. " +
            "Ensure the harness is built by running 'npm install && npm run build' in edge-tester/TypeScript/");
    }

    /// <summary>
    /// Connects to a Bannou server using the TypeScript SDK.
    /// </summary>
    public async Task<bool> ConnectAsync(
        string url,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var result = await _runner.ConnectAsync(url, email, password, cancellationToken);
        if (!result.Success)
        {
            Console.WriteLine($"   [TS SDK] Connection failed: {result.ErrorMessage}");
        }
        else
        {
            Console.WriteLine($"   [TS SDK] Connected with session: {result.SessionId}");
        }
        return result.Success;
    }

    /// <summary>
    /// Registers a new account and connects using the TypeScript SDK.
    /// </summary>
    public async Task<bool> RegisterAndConnectAsync(
        string url,
        string username,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var result = await _runner.RegisterAndConnectAsync(url, username, email, password, cancellationToken);
        if (!result.Success)
        {
            Console.WriteLine($"   [TS SDK] Registration/connection failed: {result.ErrorMessage}");
        }
        else
        {
            Console.WriteLine($"   [TS SDK] Registered and connected with session: {result.SessionId}");
        }
        return result.Success;
    }

    /// <summary>
    /// Invokes an API and returns the raw result for comparison.
    /// </summary>
    public async Task<InvokeResult<JsonElement?>> InvokeRawAsync(
        string path,
        object request,
        CancellationToken cancellationToken = default)
    {
        return await _runner.InvokeRawAsync(path, request, cancellationToken);
    }

    /// <summary>
    /// Invokes an API with typed request/response.
    /// </summary>
    public async Task<InvokeResult<TResult>> InvokeAsync<TRequest, TResult>(
        string path,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _runner.InvokeAsync<TRequest, TResult>(path, request, cancellationToken);
    }

    /// <summary>
    /// Verifies that a C# SDK result matches what the TypeScript SDK produces.
    /// </summary>
    /// <param name="csharpResult">The result from the C# SDK.</param>
    /// <param name="path">API path.</param>
    /// <param name="request">The request that was sent.</param>
    /// <param name="extractComparableData">Optional function to extract comparable data from results.</param>
    /// <returns>True if results match, false otherwise.</returns>
    public async Task<bool> VerifyParityAsync<TRequest, TResult>(
        TResult csharpResult,
        string path,
        TRequest request,
        Func<TResult, object>? extractComparableData = null,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"   [Parity] Verifying {path}...");

        var tsResult = await _runner.InvokeAsync<TRequest, TResult>(path, request, cancellationToken);

        if (!tsResult.IsSuccess)
        {
            Console.WriteLine($"   [Parity] ❌ TypeScript SDK failed: {tsResult.ErrorMessage}");
            return false;
        }

        // Compare results
        var csharpData = extractComparableData != null
            ? extractComparableData(csharpResult)
            : csharpResult;
        var tsData = tsResult.Result != null && extractComparableData != null
            ? extractComparableData(tsResult.Result)
            : tsResult.Result;

        var csharpJson = JsonSerializer.Serialize(csharpData);
        var tsJson = JsonSerializer.Serialize(tsData);

        if (csharpJson == tsJson)
        {
            Console.WriteLine($"   [Parity] ✅ Results match");
            return true;
        }

        Console.WriteLine($"   [Parity] ❌ Results differ!");
        Console.WriteLine($"            C# SDK:  {Truncate(csharpJson, 200)}");
        Console.WriteLine($"            TS SDK:  {Truncate(tsJson, 200)}");
        return false;
    }

    /// <summary>
    /// Verifies that both SDKs produce the same error for a request expected to fail.
    /// </summary>
    public async Task<bool> VerifyErrorParityAsync<TRequest>(
        string path,
        TRequest request,
        string expectedCsharpError,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"   [Parity] Verifying error parity for {path}...");

        var tsResult = await _runner.InvokeAsync<TRequest, object>(path, request, cancellationToken);

        if (tsResult.IsSuccess)
        {
            Console.WriteLine($"   [Parity] ❌ TypeScript SDK unexpectedly succeeded");
            return false;
        }

        // Both should fail - check if error messages are similar
        Console.WriteLine($"   [Parity] ✅ Both SDKs returned errors");
        Console.WriteLine($"            C# error:  {expectedCsharpError}");
        Console.WriteLine($"            TS error:  {tsResult.ErrorMessage}");
        return true;
    }

    /// <summary>
    /// Disconnects the TypeScript client.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _runner.DisconnectAsync(cancellationToken);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;
        return value[..maxLength] + "...";
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _runner.DisposeAsync();
    }
}
