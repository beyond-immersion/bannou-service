using System.Diagnostics;
using System.Security.Cryptography;
using BeyondImmersion.Bannou.AssetBundler.Abstractions;
using BeyondImmersion.Bannou.AssetBundler.Extraction;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.AssetBundler.Stride.Compilation;

/// <summary>
/// Compiles assets through Stride's asset pipeline via batch project generation.
/// </summary>
public sealed class StrideBatchCompiler : IAssetProcessor, IDisposable
{
    private readonly StrideCompilerOptions _options;
    private readonly ILogger<StrideBatchCompiler>? _logger;
    private readonly StrideTypeInferencer _typeInferencer = new();

    /// <summary>
    /// Creates a new Stride batch compiler.
    /// </summary>
    /// <param name="options">Compiler options.</param>
    /// <param name="logger">Optional logger.</param>
    public StrideBatchCompiler(StrideCompilerOptions? options = null, ILogger<StrideBatchCompiler>? logger = null)
    {
        _options = options ?? new StrideCompilerOptions();
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProcessorId => "stride";

    /// <inheritdoc />
    public IReadOnlyList<string> OutputContentTypes =>
    [
        StrideContentTypes.Model,
        StrideContentTypes.Texture,
        StrideContentTypes.Animation,
        StrideContentTypes.Material
    ];

    /// <summary>
    /// Gets the type inferencer for Stride asset types.
    /// </summary>
    public IAssetTypeInferencer? TypeInferencer => _typeInferencer;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, IProcessedAsset>> ProcessAsync(
        IReadOnlyList<ExtractedAsset> assets,
        DirectoryInfo workingDir,
        ProcessorOptions? options = null,
        CancellationToken ct = default)
    {
        if (assets.Count == 0)
            return new Dictionary<string, IProcessedAsset>();

        _logger?.LogInformation("Starting Stride compilation for {AssetCount} assets", assets.Count);

        // 1. Generate Stride project
        var projectGenerator = new StrideBatchProjectGenerator(_options, _logger as ILogger<StrideBatchProjectGenerator>);
        var projectPath = await projectGenerator.GenerateAsync(assets, workingDir, ct);

        _logger?.LogDebug("Generated Stride project at {ProjectPath}", projectPath);

        // 2. Run dotnet build
        var buildResult = await RunBuildAsync(projectPath, ct);

        if (!buildResult.Success)
        {
            _logger?.LogError("Stride build failed with exit code {ExitCode}", buildResult.ExitCode);
            throw StrideBuildException.FromBuildOutput(
                buildResult.ExitCode,
                buildResult.ErrorOutput,
                buildResult.StandardOutput);
        }

        _logger?.LogInformation("Stride build completed successfully");

        // 3. Parse index to find compiled assets
        var projectDir = new DirectoryInfo(Path.GetDirectoryName(projectPath)!);
        var indexParser = new StrideIndexParser(projectDir, _logger as ILogger<StrideIndexParser>);
        var indexEntries = indexParser.ParseIndex();

        _logger?.LogDebug("Found {EntryCount} entries in Stride index", indexEntries.Count);

        // 4. Collect compiled assets
        var results = new Dictionary<string, IProcessedAsset>();

        foreach (var asset in assets)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var compiledAsset = await CollectCompiledAssetAsync(asset, indexEntries, indexParser, ct);
                if (compiledAsset != null)
                {
                    results[asset.AssetId] = compiledAsset;
                }
                else
                {
                    _logger?.LogWarning("No compiled output found for asset {AssetId}", asset.AssetId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to collect compiled asset {AssetId}", asset.AssetId);

                if (options?.FailFast == true)
                    throw;
            }
        }

        _logger?.LogInformation("Collected {CompiledCount} compiled assets from {TotalCount} inputs",
            results.Count, assets.Count);

        return results;
    }

    private async Task<BuildResult> RunBuildAsync(string projectPath, CancellationToken ct)
    {
        var projectDir = Path.GetDirectoryName(projectPath)!;

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.DotnetPath,
            Arguments = $"build \"{projectPath}\" -c {_options.Configuration}",
            WorkingDirectory = projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger?.LogDebug("Running: {FileName} {Arguments}", startInfo.FileName, startInfo.Arguments);

        using var process = new Process { StartInfo = startInfo };
        var stdout = new List<string>();
        var stderr = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stdout.Add(e.Data);
                if (_options.VerboseOutput)
                    _logger?.LogDebug("[stdout] {Line}", e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stderr.Add(e.Data);
                if (_options.VerboseOutput)
                    _logger?.LogDebug("[stderr] {Line}", e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.BuildTimeoutMs);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new StrideBuildException($"Build timed out after {_options.BuildTimeoutMs}ms");
        }

        return new BuildResult
        {
            Success = process.ExitCode == 0,
            ExitCode = process.ExitCode,
            StandardOutput = string.Join('\n', stdout),
            ErrorOutput = string.Join('\n', stderr)
        };
    }

    private async Task<IProcessedAsset?> CollectCompiledAssetAsync(
        ExtractedAsset sourceAsset,
        Dictionary<string, StrideIndexEntry> indexEntries,
        StrideIndexParser indexParser,
        CancellationToken ct)
    {
        // Find matching index entry
        var assetName = Path.GetFileNameWithoutExtension(sourceAsset.Filename);
        var matchingEntry = indexEntries.Values
            .FirstOrDefault(e => e.Url.Contains(assetName, StringComparison.OrdinalIgnoreCase));

        if (matchingEntry == null)
        {
            _logger?.LogDebug("No index entry found for {AssetName}", assetName);
            return null;
        }

        // Read compiled data
        byte[] data;
        try
        {
            data = indexParser.ReadAssetData(matchingEntry);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to read compiled data for {AssetId}", sourceAsset.AssetId);
            return null;
        }

        var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var contentType = GetContentTypeForAsset(sourceAsset);

        // Collect dependencies (buffer files for models, etc.)
        var dependencies = await CollectDependenciesAsync(matchingEntry, indexEntries, indexParser, ct);

        return new StrideProcessedAsset
        {
            AssetId = sourceAsset.AssetId,
            Filename = $"{sourceAsset.AssetId}.{StrideContentTypes.ToExtension(contentType)}",
            ContentType = contentType,
            Data = data,
            ContentHash = hash,
            StrideGuid = matchingEntry.Guid,
            ObjectId = matchingEntry.ObjectId,
            SourceFilename = sourceAsset.Filename,
            StrideAssetType = sourceAsset.AssetType.ToString(),
            Dependencies = dependencies,
            Metadata = new Dictionary<string, object>
            {
                ["strideGuid"] = matchingEntry.Guid.ToString(),
                ["objectId"] = matchingEntry.ObjectId,
                ["sourceFile"] = sourceAsset.Filename
            }
        };
    }

    private async Task<Dictionary<string, ReadOnlyMemory<byte>>> CollectDependenciesAsync(
        StrideIndexEntry mainEntry,
        Dictionary<string, StrideIndexEntry> allEntries,
        StrideIndexParser indexParser,
        CancellationToken ct)
    {
        var dependencies = new Dictionary<string, ReadOnlyMemory<byte>>();

        // For models, look for buffer files
        if (mainEntry.Url.Contains("/Models/", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = mainEntry.Url;
            var bufferPattern = $"{baseUrl}_buffer";

            foreach (var (url, entry) in allEntries)
            {
                if (url.StartsWith(bufferPattern, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var bufferData = indexParser.ReadAssetData(entry);
                        var bufferName = Path.GetFileName(url);
                        dependencies[bufferName] = bufferData;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to read buffer dependency {BufferUrl}", url);
                    }
                }
            }
        }

        await Task.CompletedTask; // Satisfy async signature
        return dependencies;
    }

    private static string GetContentTypeForAsset(ExtractedAsset asset)
    {
        return asset.AssetType switch
        {
            AssetType.Model => StrideContentTypes.Model,
            AssetType.Texture => StrideContentTypes.Texture,
            AssetType.Animation => StrideContentTypes.Animation,
            _ => StrideContentTypes.Binary
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No resources to dispose currently
    }

    private sealed class BuildResult
    {
        public bool Success { get; init; }
        public int ExitCode { get; init; }
        public string StandardOutput { get; init; } = string.Empty;
        public string ErrorOutput { get; init; } = string.Empty;
    }
}
