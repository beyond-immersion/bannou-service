using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Asset.Processing;

/// <summary>
/// Registry for asset processors. Manages processor discovery and routing.
/// </summary>
public sealed class AssetProcessorRegistry
{
    private readonly IReadOnlyList<IAssetProcessor> _processors;
    private readonly ILogger<AssetProcessorRegistry> _logger;
    private readonly IReadOnlyDictionary<string, IAssetProcessor> _processorsByPoolType;
    private readonly IReadOnlyDictionary<string, IAssetProcessor> _processorsByContentType;

    /// <summary>
    /// Creates a new AssetProcessorRegistry with the provided processors.
    /// </summary>
    public AssetProcessorRegistry(
        IEnumerable<IAssetProcessor> processors,
        ILogger<AssetProcessorRegistry> logger)
    {
        _processors = processors.ToList();
        _logger = logger;

        var byPoolType = new Dictionary<string, IAssetProcessor>(StringComparer.OrdinalIgnoreCase);
        var byContentType = new Dictionary<string, IAssetProcessor>(StringComparer.OrdinalIgnoreCase);

        // Build lookup tables
        foreach (var processor in _processors)
        {
            byPoolType[processor.PoolType] = processor;

            foreach (var contentType in processor.SupportedContentTypes)
            {
                byContentType[contentType] = processor;
            }
        }

        _processorsByPoolType = byPoolType;
        _processorsByContentType = byContentType;

        _logger.LogInformation(
            "Registered {ProcessorCount} asset processors: {PoolTypes}",
            _processors.Count,
            string.Join(", ", _processorsByPoolType.Keys));
    }

    /// <summary>
    /// Gets a processor for the specified pool type.
    /// </summary>
    /// <param name="poolType">The processor pool type.</param>
    /// <returns>The processor if found, null otherwise.</returns>
    public IAssetProcessor? GetProcessorByPoolType(string poolType)
    {
        return _processorsByPoolType.GetValueOrDefault(poolType);
    }

    /// <summary>
    /// Gets a processor that can handle the specified content type.
    /// </summary>
    /// <param name="contentType">The MIME content type.</param>
    /// <returns>The processor if found, null otherwise.</returns>
    public IAssetProcessor? GetProcessorByContentType(string contentType)
    {
        return _processorsByContentType.GetValueOrDefault(contentType);
    }

    /// <summary>
    /// Gets all registered processors.
    /// </summary>
    public IReadOnlyList<IAssetProcessor> GetAllProcessors() => _processors;

    /// <summary>
    /// Gets all supported pool types.
    /// </summary>
    public IReadOnlyCollection<string> GetPoolTypes() => _processorsByPoolType.Keys.ToList();

    /// <summary>
    /// Gets all supported content types.
    /// </summary>
    public IReadOnlyCollection<string> GetContentTypes() => _processorsByContentType.Keys.ToList();

    /// <summary>
    /// Checks if a content type is supported by any processor.
    /// </summary>
    /// <param name="contentType">The MIME content type to check.</param>
    /// <returns>True if the content type is supported.</returns>
    public bool IsContentTypeSupported(string contentType)
    {
        return _processorsByContentType.ContainsKey(contentType);
    }
}
