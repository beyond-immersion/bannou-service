using Json.Patch;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.BannouService.SaveLoad.Delta;

/// <summary>
/// Handles delta (patch) computation and application for incremental saves.
/// Currently supports JSON Patch (RFC 6902). BSDIFF/XDELTA support is
/// stubbed for future implementation with binary game state.
/// </summary>
public sealed class DeltaProcessor
{
    private readonly ILogger<DeltaProcessor> _logger;
    private readonly int _maxPatchOperations;

    /// <summary>
    /// Creates a new DeltaProcessor instance.
    /// </summary>
    public DeltaProcessor(ILogger<DeltaProcessor> logger, int maxPatchOperations = 1000)
    {
        _logger = logger;
        _maxPatchOperations = maxPatchOperations;
    }

    /// <summary>
    /// Computes a delta (patch) from source to target data.
    /// </summary>
    /// <param name="sourceData">Original data (base version)</param>
    /// <param name="targetData">New data to derive delta from</param>
    /// <param name="algorithm">Algorithm to use for delta computation</param>
    /// <returns>The computed delta as a byte array, or null if computation failed</returns>
    public byte[]? ComputeDelta(byte[] sourceData, byte[] targetData, string algorithm)
    {
        return algorithm.ToUpperInvariant() switch
        {
            "JSON_PATCH" => ComputeJsonPatch(sourceData, targetData),
            "BSDIFF" => throw new NotSupportedException("BSDIFF algorithm is not yet implemented"),
            "XDELTA" => throw new NotSupportedException("XDELTA algorithm is not yet implemented"),
            _ => throw new ArgumentException($"Unknown delta algorithm: {algorithm}", nameof(algorithm))
        };
    }

    /// <summary>
    /// Applies a delta (patch) to source data to reconstruct target data.
    /// </summary>
    /// <param name="sourceData">Original data (base version)</param>
    /// <param name="deltaData">Delta/patch to apply</param>
    /// <param name="algorithm">Algorithm used to create the delta</param>
    /// <returns>Reconstructed data, or null if application failed</returns>
    public byte[]? ApplyDelta(byte[] sourceData, byte[] deltaData, string algorithm)
    {
        return algorithm.ToUpperInvariant() switch
        {
            "JSON_PATCH" => ApplyJsonPatch(sourceData, deltaData),
            "BSDIFF" => throw new NotSupportedException("BSDIFF algorithm is not yet implemented"),
            "XDELTA" => throw new NotSupportedException("XDELTA algorithm is not yet implemented"),
            _ => throw new ArgumentException($"Unknown delta algorithm: {algorithm}", nameof(algorithm))
        };
    }

    /// <summary>
    /// Computes a JSON Patch (RFC 6902) from source to target JSON.
    /// </summary>
    private byte[]? ComputeJsonPatch(byte[] sourceData, byte[] targetData)
    {
        try
        {
            var sourceJson = Encoding.UTF8.GetString(sourceData);
            var targetJson = Encoding.UTF8.GetString(targetData);

            var sourceNode = JsonNode.Parse(sourceJson);
            var targetNode = JsonNode.Parse(targetJson);

            if (sourceNode == null || targetNode == null)
            {
                _logger.LogError("Failed to parse JSON for delta computation");
                return null;
            }

            var patch = sourceNode.CreatePatch(targetNode);

            if (patch.Operations.Count > _maxPatchOperations)
            {
                _logger.LogWarning(
                    "JSON Patch has {Count} operations, exceeds max of {Max}",
                    patch.Operations.Count,
                    _maxPatchOperations);
                return null;
            }

            var patchJson = JsonSerializer.Serialize(patch);
            return Encoding.UTF8.GetBytes(patchJson);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to compute JSON Patch: invalid JSON");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute JSON Patch");
            return null;
        }
    }

    /// <summary>
    /// Applies a JSON Patch (RFC 6902) to source JSON.
    /// </summary>
    private byte[]? ApplyJsonPatch(byte[] sourceData, byte[] deltaData)
    {
        try
        {
            var sourceJson = Encoding.UTF8.GetString(sourceData);
            var patchJson = Encoding.UTF8.GetString(deltaData);

            var sourceNode = JsonNode.Parse(sourceJson);
            if (sourceNode == null)
            {
                _logger.LogError("Failed to parse source JSON for delta application");
                return null;
            }

            var patch = JsonSerializer.Deserialize<JsonPatch>(patchJson);
            if (patch == null)
            {
                _logger.LogError("Failed to parse JSON Patch");
                return null;
            }

            var result = patch.Apply(sourceNode);

            if (!result.IsSuccess)
            {
                _logger.LogError("JSON Patch application failed: {Error}", result.Error);
                return null;
            }

            if (result.Result == null)
            {
                _logger.LogError("JSON Patch application returned null result");
                return null;
            }

            var resultJson = result.Result.ToJsonString();
            return Encoding.UTF8.GetBytes(resultJson);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to apply JSON Patch: invalid JSON");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply JSON Patch");
            return null;
        }
    }

    /// <summary>
    /// Validates that the provided delta data is a valid JSON Patch.
    /// </summary>
    /// <param name="deltaData">Delta data to validate</param>
    /// <param name="algorithm">Algorithm the delta was created with</param>
    /// <returns>True if valid, false otherwise</returns>
    public bool ValidateDelta(byte[] deltaData, string algorithm)
    {
        if (algorithm.ToUpperInvariant() != "JSON_PATCH")
        {
            // For non-JSON algorithms, just check for non-empty data
            return deltaData.Length > 0;
        }

        try
        {
            var patchJson = Encoding.UTF8.GetString(deltaData);
            var patch = JsonSerializer.Deserialize<JsonPatch>(patchJson);

            if (patch == null)
            {
                return false;
            }

            // Validate operation count
            if (patch.Operations.Count > _maxPatchOperations)
            {
                _logger.LogWarning(
                    "JSON Patch has {Count} operations, exceeds max of {Max}",
                    patch.Operations.Count,
                    _maxPatchOperations);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Invalid JSON Patch data");
            return false;
        }
    }

    /// <summary>
    /// Gets the number of operations in a JSON Patch.
    /// </summary>
    /// <param name="deltaData">Delta data</param>
    /// <param name="algorithm">Algorithm the delta was created with</param>
    /// <returns>Operation count, or -1 if not applicable/invalid</returns>
    public int GetOperationCount(byte[] deltaData, string algorithm)
    {
        if (algorithm.ToUpperInvariant() != "JSON_PATCH")
        {
            return -1;
        }

        try
        {
            var patchJson = Encoding.UTF8.GetString(deltaData);
            var patch = JsonSerializer.Deserialize<JsonPatch>(patchJson);
            return patch?.Operations.Count ?? -1;
        }
        catch
        {
            return -1;
        }
    }
}
