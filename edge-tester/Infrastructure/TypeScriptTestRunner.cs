using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeyondImmersion.EdgeTester.Infrastructure;

/// <summary>
/// Spawns and communicates with the TypeScript SDK test harness.
/// Sends JSON commands via stdin and reads JSON responses from stdout.
/// </summary>
public sealed class TypeScriptTestRunner : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly StreamReader _stdout;
    private readonly StreamReader _stderr;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Task _stderrTask;
    private bool _disposed;

    private TypeScriptTestRunner(Process process)
    {
        _process = process;
        _stdin = process.StandardInput;
        _stdout = process.StandardOutput;
        _stderr = process.StandardError;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };

        // Start background task to read and output stderr
        _stderrTask = Task.Run(async () =>
        {
            try
            {
                while (!_disposed && !_process.HasExited)
                {
                    var line = await _stderr.ReadLineAsync();
                    if (line != null)
                    {
                        Console.WriteLine($"   [TS DEBUG] {line}");
                    }
                }
            }
            catch
            {
                // Ignore errors when process exits
            }
        });
    }

    /// <summary>
    /// Starts the TypeScript test harness.
    /// </summary>
    /// <param name="harnessPath">Path to the harness directory (contains harness.js).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A running TypeScriptTestRunner instance.</returns>
    public static async Task<TypeScriptTestRunner> StartAsync(
        string harnessPath,
        CancellationToken cancellationToken = default)
    {
        var harnessJsPath = Path.Combine(harnessPath, "harness.js");
        if (!File.Exists(harnessJsPath))
        {
            throw new FileNotFoundException(
                $"TypeScript harness not found. Expected: {harnessJsPath}. " +
                "Run 'npm install && npm run build' in the TypeScript directory first.");
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = harnessJsPath,
                WorkingDirectory = harnessPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        process.Start();

        TypeScriptTestRunner? runner = null;
        try
        {
            runner = new TypeScriptTestRunner(process);

            // Wait for ready signal with timeout
            using var readyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readyCts.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                var readyLine = await runner._stdout.ReadLineAsync(readyCts.Token);

                if (readyLine == null)
                {
                    // Check stderr for error message
                    var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(stderr))
                    {
                        throw new InvalidOperationException($"Harness failed to start. stderr: {stderr}");
                    }
                    throw new InvalidOperationException("Harness did not send ready signal (no output)");
                }

                var ready = JsonSerializer.Deserialize<ReadyResponse>(readyLine, runner._jsonOptions)
                    ?? throw new InvalidOperationException($"Invalid ready response from harness: {readyLine}");

                if (!ready.Ready)
                {
                    throw new InvalidOperationException("Harness reported not ready");
                }

                var result = runner;
                runner = null; // Transfer ownership
                return result;
            }
            catch (OperationCanceledException) when (readyCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Timeout waiting for ready signal - check stderr
                var stderr = await process.StandardError.ReadToEndAsync(CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    throw new InvalidOperationException($"Harness timed out. stderr: {stderr}");
                }
                throw new InvalidOperationException("Harness timed out waiting for ready signal");
            }
        }
        finally
        {
            if (runner != null)
            {
                await runner.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Connects to a Bannou server with email/password.
    /// </summary>
    public async Task<ConnectResult> ConnectAsync(
        string url,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var command = new ConnectCommand
        {
            Cmd = "connect",
            Url = url,
            Email = email,
            Password = password,
        };

        var response = await SendCommandAsync<ConnectCommand, ConnectResponse>(command, cancellationToken);
        return new ConnectResult(response.Ok, response.Result?.SessionId, response.Error?.Message);
    }

    /// <summary>
    /// Registers a new account and connects.
    /// </summary>
    public async Task<ConnectResult> RegisterAndConnectAsync(
        string url,
        string username,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var command = new RegisterAndConnectCommand
        {
            Cmd = "registerAndConnect",
            Url = url,
            Username = username,
            Email = email,
            Password = password,
        };

        var response = await SendCommandAsync<RegisterAndConnectCommand, ConnectResponse>(command, cancellationToken);
        return new ConnectResult(response.Ok, response.Result?.SessionId, response.Error?.Message);
    }

    /// <summary>
    /// Invokes an API endpoint and returns the result.
    /// </summary>
    /// <typeparam name="TRequest">Request type.</typeparam>
    /// <typeparam name="TResult">Expected result type.</typeparam>
    public async Task<InvokeResult<TResult>> InvokeAsync<TRequest, TResult>(
        string method,
        string path,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new InvokeCommand
        {
            Cmd = "invoke",
            Method = method,
            Path = path,
            Request = JsonSerializer.SerializeToElement(request, _jsonOptions),
        };

        var response = await SendCommandAsync<InvokeCommand, JsonElement?>(command, cancellationToken);

        if (response.Ok && response.Result.HasValue)
        {
            var result = response.Result.Value.Deserialize<TResult>(_jsonOptions);
            return InvokeResult<TResult>.Success(result);
        }

        return InvokeResult<TResult>.Failure(
            response.Error?.Message ?? "Unknown error",
            response.Error?.Code,
            response.Error?.ErrorName);
    }

    /// <summary>
    /// Invokes an API endpoint and returns raw JSON.
    /// </summary>
    public async Task<InvokeResult<JsonElement?>> InvokeRawAsync(
        string method,
        string path,
        object request,
        CancellationToken cancellationToken = default)
    {
        var command = new InvokeCommand
        {
            Cmd = "invoke",
            Method = method,
            Path = path,
            Request = JsonSerializer.SerializeToElement(request, _jsonOptions),
        };

        var response = await SendCommandAsync<InvokeCommand, JsonElement?>(command, cancellationToken);

        if (response.Ok)
        {
            return InvokeResult<JsonElement?>.Success(response.Result);
        }

        return InvokeResult<JsonElement?>.Failure(
            response.Error?.Message ?? "Unknown error",
            response.Error?.Code,
            response.Error?.ErrorName);
    }

    /// <summary>
    /// Disconnects from the server.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var command = new DisconnectCommand { Cmd = "disconnect" };
        await SendCommandAsync<DisconnectCommand, EmptyResponse>(command, cancellationToken);
    }

    /// <summary>
    /// Gets the current status of the harness.
    /// </summary>
    public async Task<StatusResult> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var command = new StatusCommand { Cmd = "getStatus" };
        var response = await SendCommandAsync<StatusCommand, StatusResponse>(command, cancellationToken);

        if (response.Ok && response.Result is not null)
        {
            return new StatusResult(
                response.Result.Connected,
                response.Result.SessionId,
                response.Result.ApiCount);
        }

        return new StatusResult(false, null, 0);
    }

    /// <summary>
    /// Pings the harness to verify it's responsive.
    /// </summary>
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        var command = new PingCommand { Cmd = "ping" };
        var response = await SendCommandAsync<PingCommand, PingResponse>(command, cancellationToken);
        return response.Ok && response.Result?.Pong == true;
    }

    private async Task<HarnessResponse<TResult>> SendCommandAsync<TCommand, TResult>(
        TCommand command,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var json = JsonSerializer.Serialize(command, _jsonOptions);
        await _stdin.WriteLineAsync(json.AsMemory(), cancellationToken);
        await _stdin.FlushAsync(cancellationToken);

        var responseLine = await _stdout.ReadLineAsync(cancellationToken)
            ?? throw new InvalidOperationException("Harness closed unexpectedly");

        return JsonSerializer.Deserialize<HarnessResponse<TResult>>(responseLine, _jsonOptions)
            ?? throw new InvalidOperationException("Invalid response from harness");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            // Try graceful shutdown
            _stdin.Close();

            // Wait for process to exit (with timeout)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Force kill if graceful shutdown times out
            _process.Kill();
        }
        finally
        {
            _process.Dispose();
        }
    }

    // Command models
    private sealed class ConnectCommand
    {
        public required string Cmd { get; init; }
        public required string Url { get; init; }
        public required string Email { get; init; }
        public required string Password { get; init; }
    }

    private sealed class RegisterAndConnectCommand
    {
        public required string Cmd { get; init; }
        public required string Url { get; init; }
        public required string Username { get; init; }
        public required string Email { get; init; }
        public required string Password { get; init; }
    }

    private sealed class InvokeCommand
    {
        public required string Cmd { get; init; }
        public required string Method { get; init; }
        public required string Path { get; init; }
        public required JsonElement Request { get; init; }
    }

    private sealed class DisconnectCommand
    {
        public required string Cmd { get; init; }
    }

    private sealed class StatusCommand
    {
        public required string Cmd { get; init; }
    }

    private sealed class PingCommand
    {
        public required string Cmd { get; init; }
    }

    // Response models
    private sealed class ReadyResponse
    {
        public bool Ready { get; init; }
        public string? Version { get; init; }
    }

    private sealed class HarnessResponse<T>
    {
        public bool Ok { get; init; }
        public T? Result { get; init; }
        public HarnessError? Error { get; init; }
    }

    private sealed class HarnessError
    {
        public string? Message { get; init; }
        public int? Code { get; init; }
        public string? ErrorName { get; init; }
    }

    private sealed class ConnectResponse
    {
        public string? SessionId { get; init; }
        public string? AccessToken { get; init; }
        public string? RefreshToken { get; init; }
    }

    private sealed class EmptyResponse
    {
    }

    private sealed class StatusResponse
    {
        public bool Connected { get; init; }
        public string? SessionId { get; init; }
        public int ApiCount { get; init; }
    }

    private sealed class PingResponse
    {
        public bool Pong { get; init; }
    }
}

/// <summary>
/// Result of a connect operation.
/// </summary>
public sealed record ConnectResult(bool Success, string? SessionId, string? ErrorMessage);

/// <summary>
/// Result of a status query.
/// </summary>
public sealed record StatusResult(bool Connected, string? SessionId, int ApiCount);

/// <summary>
/// Result of an API invocation.
/// </summary>
/// <typeparam name="T">The expected result type.</typeparam>
public sealed class InvokeResult<T>
{
    /// <summary>
    /// Whether the invocation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// The result value (if successful).
    /// </summary>
    public T? Result { get; }

    /// <summary>
    /// Error message (if failed).
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Error code (if failed).
    /// </summary>
    public int? ErrorCode { get; }

    /// <summary>
    /// Error name (if failed).
    /// </summary>
    public string? ErrorName { get; }

    private InvokeResult(bool isSuccess, T? result, string? errorMessage, int? errorCode, string? errorName)
    {
        IsSuccess = isSuccess;
        Result = result;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        ErrorName = errorName;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static InvokeResult<T> Success(T? result) =>
        new(true, result, null, null, null);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static InvokeResult<T> Failure(string message, int? code = null, string? errorName = null) =>
        new(false, default, message, code, errorName);
}
