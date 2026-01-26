using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace BeyondImmersion.Bannou.Bundle.Format;

/// <summary>
/// Validates uploaded bundles for structure, manifest, and content integrity.
/// </summary>
public sealed class BundleValidator
{
    private readonly ILogger<BundleValidator> _logger;
    private readonly BundleValidatorOptions _options;

    /// <summary>
    /// Creates a new bundle validator.
    /// </summary>
    public BundleValidator(
        ILogger<BundleValidator> logger,
        BundleValidatorOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new BundleValidatorOptions();
    }

    /// <summary>
    /// Validates a bundle stream and returns the validation result.
    /// </summary>
    /// <param name="bundleStream">The bundle stream to validate.</param>
    /// <param name="expectedBundleId">Expected bundle ID for verification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with details.</returns>
    public async Task<BundleValidationResult> ValidateAsync(
        Stream bundleStream,
        string expectedBundleId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Validating bundle: expectedId={BundleId}", expectedBundleId);

        var result = new BundleValidationResult
        {
            BundleId = expectedBundleId,
            ValidatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            // Step 1: Structure validation
            var structureResult = await ValidateStructureAsync(bundleStream, cancellationToken);
            if (!structureResult.IsValid)
            {
                result.Errors.AddRange(structureResult.Errors);
                result.Stage = ValidationStage.Structure;
                return result;
            }
            result.AssetCount = structureResult.AssetCount;

            // Reset stream for manifest validation
            bundleStream.Position = 0;

            // Step 2: Manifest validation
            var manifestResult = await ValidateManifestAsync(
                bundleStream, expectedBundleId, cancellationToken);
            if (!manifestResult.IsValid)
            {
                result.Errors.AddRange(manifestResult.Errors);
                result.Stage = ValidationStage.Manifest;
                return result;
            }
            result.Manifest = manifestResult.Manifest;

            // Reset stream for content validation
            bundleStream.Position = 0;

            // Step 3: Content validation
            var contentResult = await ValidateContentAsync(bundleStream, cancellationToken);
            if (!contentResult.IsValid)
            {
                result.Errors.AddRange(contentResult.Errors);
                result.Warnings.AddRange(contentResult.Warnings);
                result.Stage = ValidationStage.Content;
                return result;
            }
            result.Warnings.AddRange(contentResult.Warnings);
            result.ExtractedAssets = contentResult.ExtractedAssets;

            result.IsValid = true;
            result.Stage = ValidationStage.Complete;

            _logger.LogInformation(
                "Bundle validation complete: bundleId={BundleId}, assetCount={AssetCount}, valid={IsValid}",
                expectedBundleId, result.AssetCount, result.IsValid);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bundle validation failed: bundleId={BundleId}", expectedBundleId);
            result.Errors.Add(new ValidationError
            {
                Code = "VALIDATION_EXCEPTION",
                Message = $"Validation failed: {ex.Message}",
                Severity = ValidationSeverity.Error
            });
            result.Stage = ValidationStage.Failed;
            return result;
        }
    }

    private async Task<StructureValidationResult> ValidateStructureAsync(
        Stream bundleStream,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        var result = new StructureValidationResult { IsValid = true };

        try
        {
            using var reader = new BannouBundleReader(bundleStream, leaveOpen: true);

            // Check bundle can be read - accessing Manifest triggers header read
            var manifest = reader.Manifest;
            var assetIds = manifest.Assets.Select(a => a.AssetId).ToList();
            if (assetIds.Count == 0)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    Code = "EMPTY_BUNDLE",
                    Message = "Bundle contains no assets",
                    Severity = ValidationSeverity.Error
                });
                return result;
            }

            result.AssetCount = assetIds.Count;

            // Check for maximum asset count
            if (assetIds.Count > _options.MaxAssetCount)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    Code = "TOO_MANY_ASSETS",
                    Message = $"Bundle contains {assetIds.Count} assets, maximum is {_options.MaxAssetCount}",
                    Severity = ValidationSeverity.Error
                });
                return result;
            }

            // Check for path traversal in asset IDs
            foreach (var assetId in assetIds)
            {
                if (ContainsPathTraversal(assetId))
                {
                    result.IsValid = false;
                    result.Errors.Add(new ValidationError
                    {
                        Code = "PATH_TRAVERSAL",
                        Message = $"Asset ID contains path traversal: {assetId}",
                        Severity = ValidationSeverity.Error
                    });
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add(new ValidationError
            {
                Code = "INVALID_STRUCTURE",
                Message = $"Invalid bundle structure: {ex.Message}",
                Severity = ValidationSeverity.Error
            });
            return result;
        }
    }

    private async Task<ManifestValidationResult> ValidateManifestAsync(
        Stream bundleStream,
        string expectedBundleId,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        var result = new ManifestValidationResult { IsValid = true };

        try
        {
            using var reader = new BannouBundleReader(bundleStream, leaveOpen: true);
            BundleManifest? manifest;
            try
            {
                manifest = reader.Manifest;
            }
            catch
            {
                manifest = null;
            }

            if (manifest == null)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    Code = "MISSING_MANIFEST",
                    Message = "Bundle manifest is missing",
                    Severity = ValidationSeverity.Error
                });
                return result;
            }

            result.Manifest = manifest;

            // Validate bundle ID matches
            if (manifest.BundleId != expectedBundleId)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    Code = "BUNDLE_ID_MISMATCH",
                    Message = $"Bundle ID mismatch: expected {expectedBundleId}, got {manifest.BundleId}",
                    Severity = ValidationSeverity.Error
                });
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(manifest.Version))
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    Code = "MISSING_VERSION",
                    Message = "Bundle version is required",
                    Severity = ValidationSeverity.Error
                });
            }

            // Check for duplicate asset entries
            var assetIds = manifest.Assets.Select(a => a.AssetId).ToList();
            var duplicates = assetIds.GroupBy(x => x)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicates.Count > 0)
            {
                result.IsValid = false;
                result.Errors.Add(new ValidationError
                {
                    Code = "DUPLICATE_ASSETS",
                    Message = $"Duplicate asset IDs found: {string.Join(", ", duplicates)}",
                    Severity = ValidationSeverity.Error
                });
            }

            // Verify asset hashes match manifest
            foreach (var assetEntry in manifest.Assets)
            {
                var assetData = reader.ReadAsset(assetEntry.AssetId);
                if (assetData == null)
                {
                    result.IsValid = false;
                    result.Errors.Add(new ValidationError
                    {
                        Code = "MISSING_ASSET",
                        Message = $"Asset declared in manifest but missing: {assetEntry.AssetId}",
                        Severity = ValidationSeverity.Error
                    });
                    continue;
                }

                var actualHash = ComputeSha256Hash(assetData);
                if (!string.Equals(actualHash, assetEntry.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    result.IsValid = false;
                    result.Errors.Add(new ValidationError
                    {
                        Code = "HASH_MISMATCH",
                        Message = $"Hash mismatch for asset {assetEntry.AssetId}: expected {assetEntry.ContentHash}, got {actualHash}",
                        Severity = ValidationSeverity.Error
                    });
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add(new ValidationError
            {
                Code = "MANIFEST_ERROR",
                Message = $"Manifest validation failed: {ex.Message}",
                Severity = ValidationSeverity.Error
            });
            return result;
        }
    }

    private async Task<ContentValidationResult> ValidateContentAsync(
        Stream bundleStream,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        var result = new ContentValidationResult { IsValid = true };

        try
        {
            using var reader = new BannouBundleReader(bundleStream, leaveOpen: true);
            var manifest = reader.Manifest;

            foreach (var assetEntry in manifest.Assets)
            {
                var assetData = reader.ReadAsset(assetEntry.AssetId);
                if (assetData == null) continue;

                // Size validation
                if (assetData.Length > _options.MaxAssetSizeBytes)
                {
                    result.IsValid = false;
                    result.Errors.Add(new ValidationError
                    {
                        Code = "ASSET_TOO_LARGE",
                        Message = $"Asset {assetEntry.AssetId} exceeds size limit: {assetData.Length} > {_options.MaxAssetSizeBytes}",
                        Severity = ValidationSeverity.Error
                    });
                    continue;
                }

                // Content type validation
                if (!IsAllowedContentType(assetEntry.ContentType))
                {
                    result.IsValid = false;
                    result.Errors.Add(new ValidationError
                    {
                        Code = "FORBIDDEN_CONTENT_TYPE",
                        Message = $"Content type not allowed for asset {assetEntry.AssetId}: {assetEntry.ContentType}",
                        Severity = ValidationSeverity.Error
                    });
                    continue;
                }

                // Check for executable content
                if (IsExecutableContent(assetData, assetEntry.ContentType))
                {
                    result.IsValid = false;
                    result.Errors.Add(new ValidationError
                    {
                        Code = "EXECUTABLE_CONTENT",
                        Message = $"Executable content detected in asset {assetEntry.AssetId}",
                        Severity = ValidationSeverity.Error
                    });
                    continue;
                }

                // Add to extracted assets
                result.ExtractedAssets.Add(new ExtractedAssetInfo
                {
                    AssetId = assetEntry.AssetId,
                    Filename = assetEntry.Filename,
                    ContentType = assetEntry.ContentType,
                    ContentHash = assetEntry.ContentHash,
                    Size = assetData.Length
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add(new ValidationError
            {
                Code = "CONTENT_ERROR",
                Message = $"Content validation failed: {ex.Message}",
                Severity = ValidationSeverity.Error
            });
            return result;
        }
    }

    private static bool ContainsPathTraversal(string path)
    {
        return path.Contains("..") ||
                path.Contains("./") ||
                path.Contains(".\\") ||
                path.StartsWith("/") ||
                path.StartsWith("\\") ||
                path.Contains("://");
    }

    private bool IsAllowedContentType(string contentType)
    {
        // Allow common asset content types
        var allowed = new[]
        {
            "image/",           // All image types
            "audio/",           // All audio types
            "video/",           // All video types
            "model/",           // 3D models
            "application/json", // JSON data
            "application/xml",  // XML data
            "text/plain",       // Plain text
            "text/csv",         // CSV data
            "application/x-bannou-bundle", // Nested bundles
            "application/x-stride-",       // Stride compiled assets
            "application/x-yaml",          // YAML data
            "application/octet-stream"     // Generic binary
        };

        var forbidden = _options.ForbiddenContentTypes;
        if (forbidden.Contains(contentType))
            return false;

        return allowed.Any(a => contentType.StartsWith(a, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExecutableContent(byte[] data, string contentType)
    {
        // Check for common executable magic bytes
        if (data.Length < 4) return false;

        // PE executable (Windows)
        if (data[0] == 'M' && data[1] == 'Z')
            return true;

        // ELF executable (Linux)
        if (data[0] == 0x7F && data[1] == 'E' && data[2] == 'L' && data[3] == 'F')
            return true;

        // Mach-O executable (macOS)
        if ((data[0] == 0xCF && data[1] == 0xFA && data[2] == 0xED && data[3] == 0xFE) ||
            (data[0] == 0xFE && data[1] == 0xED && data[2] == 0xFA && data[3] == 0xCF))
            return true;

        // Check for script types by content type
        if (contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("vbscript", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("executable", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string ComputeSha256Hash(byte[] data)
    {
        var hashBytes = SHA256.HashData(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

/// <summary>
/// Options for bundle validation.
/// </summary>
public sealed class BundleValidatorOptions
{
    /// <summary>
    /// Maximum number of assets allowed in a bundle.
    /// </summary>
    public int MaxAssetCount { get; set; } = 10000;

    /// <summary>
    /// Maximum size of a single asset in bytes.
    /// </summary>
    public long MaxAssetSizeBytes { get; set; } = 500 * 1024 * 1024; // 500 MB

    /// <summary>
    /// Forbidden content types.
    /// </summary>
    public HashSet<string> ForbiddenContentTypes { get; set; } = new()
    {
        "application/x-executable",
        "application/x-msdos-program",
        "application/x-msdownload",
        "application/x-sharedlib",
        "application/x-shellscript"
    };
}

/// <summary>
/// Result of bundle validation.
/// </summary>
public sealed class BundleValidationResult
{
    /// <summary>
    /// Bundle ID that was validated (human-readable identifier).
    /// </summary>
    public required string BundleId { get; set; }

    /// <summary>
    /// Whether validation passed.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Stage at which validation stopped.
    /// </summary>
    public ValidationStage Stage { get; set; } = ValidationStage.Pending;

    /// <summary>
    /// Number of assets in the bundle.
    /// </summary>
    public int AssetCount { get; set; }

    /// <summary>
    /// Parsed manifest from the bundle.
    /// </summary>
    public BundleManifest? Manifest { get; set; }

    /// <summary>
    /// List of validation errors.
    /// </summary>
    public List<ValidationError> Errors { get; set; } = new();

    /// <summary>
    /// List of validation warnings.
    /// </summary>
    public List<ValidationError> Warnings { get; set; } = new();

    /// <summary>
    /// Extracted asset information (for registration).
    /// </summary>
    public List<ExtractedAssetInfo> ExtractedAssets { get; set; } = new();

    /// <summary>
    /// When validation was performed.
    /// </summary>
    public DateTimeOffset ValidatedAt { get; set; }
}

/// <summary>
/// Validation stages.
/// </summary>
public enum ValidationStage
{
    /// <summary>Validation has not started.</summary>
    Pending,
    /// <summary>Validating bundle structure.</summary>
    Structure,
    /// <summary>Validating bundle manifest.</summary>
    Manifest,
    /// <summary>Validating bundle content.</summary>
    Content,
    /// <summary>Validation completed successfully.</summary>
    Complete,
    /// <summary>Validation failed.</summary>
    Failed
}

/// <summary>
/// A validation error or warning.
/// </summary>
public sealed class ValidationError
{
    /// <summary>
    /// Error code.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Error severity.
    /// </summary>
    public ValidationSeverity Severity { get; set; }
}

/// <summary>
/// Validation error severity.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>Non-fatal warning.</summary>
    Warning,
    /// <summary>Fatal error.</summary>
    Error
}

/// <summary>
/// Information about an extracted asset.
/// </summary>
public sealed class ExtractedAssetInfo
{
    /// <summary>Asset identifier.</summary>
    public string AssetId { get; set; } = string.Empty;
    /// <summary>Original filename.</summary>
    public string Filename { get; set; } = string.Empty;
    /// <summary>MIME content type.</summary>
    public string ContentType { get; set; } = string.Empty;
    /// <summary>SHA256 hash of uncompressed content.</summary>
    public string ContentHash { get; set; } = string.Empty;
    /// <summary>Uncompressed size in bytes.</summary>
    public long Size { get; set; }
}

/// <summary>
/// Internal result for structure validation.
/// </summary>
internal sealed class StructureValidationResult
{
    public bool IsValid { get; set; }
    public int AssetCount { get; set; }
    public List<ValidationError> Errors { get; set; } = new();
}

/// <summary>
/// Internal result for manifest validation.
/// </summary>
internal sealed class ManifestValidationResult
{
    public bool IsValid { get; set; }
    public BundleManifest? Manifest { get; set; }
    public List<ValidationError> Errors { get; set; } = new();
}

/// <summary>
/// Internal result for content validation.
/// </summary>
internal sealed class ContentValidationResult
{
    public bool IsValid { get; set; }
    public List<ValidationError> Errors { get; set; } = new();
    public List<ValidationError> Warnings { get; set; } = new();
    public List<ExtractedAssetInfo> ExtractedAssets { get; set; } = new();
}
