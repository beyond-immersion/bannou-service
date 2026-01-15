namespace BeyondImmersion.Bannou.AssetBundler.Godot.Processing;

/// <summary>
/// Exception thrown when Godot asset processing fails.
/// </summary>
public sealed class GodotProcessingException : Exception
{
    /// <summary>
    /// Creates a new Godot processing exception.
    /// </summary>
    /// <param name="message">Error message.</param>
    public GodotProcessingException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates a new Godot processing exception with inner exception.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="innerException">Inner exception.</param>
    public GodotProcessingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a processing exception for a conversion failure.
    /// </summary>
    /// <param name="assetId">Asset ID that failed.</param>
    /// <param name="sourceFormat">Original format.</param>
    /// <param name="targetFormat">Target format.</param>
    /// <param name="reason">Reason for failure.</param>
    /// <returns>Exception with formatted message.</returns>
    public static GodotProcessingException ConversionFailed(
        string assetId,
        string sourceFormat,
        string targetFormat,
        string reason)
    {
        return new GodotProcessingException(
            $"Failed to convert asset '{assetId}' from {sourceFormat} to {targetFormat}: {reason}");
    }

    /// <summary>
    /// Creates a processing exception for a missing converter.
    /// </summary>
    /// <param name="assetId">Asset ID that needs conversion.</param>
    /// <param name="format">Format that needs conversion.</param>
    /// <param name="converterName">Name of the required converter.</param>
    /// <returns>Exception with formatted message.</returns>
    public static GodotProcessingException ConverterNotFound(
        string assetId,
        string format,
        string converterName)
    {
        return new GodotProcessingException(
            $"Asset '{assetId}' ({format}) requires {converterName} converter which is not configured");
    }

    /// <summary>
    /// Gets the asset ID that failed processing, if available.
    /// </summary>
    public string? AssetId { get; init; }

    /// <summary>
    /// Gets the original format of the asset, if available.
    /// </summary>
    public string? SourceFormat { get; init; }

    /// <summary>
    /// Gets the target format for conversion, if available.
    /// </summary>
    public string? TargetFormat { get; init; }
}
