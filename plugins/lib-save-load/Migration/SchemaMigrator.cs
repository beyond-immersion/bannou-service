using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.SaveLoad.Models;
using BeyondImmersion.BannouService.Services;
using Json.Patch;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeyondImmersion.BannouService.SaveLoad.Migration;

/// <summary>
/// Handles schema migration path discovery and application.
/// Uses JSON Patch (RFC 6902) for migrating save data between schema versions.
/// </summary>
public sealed class SchemaMigrator
{
    private readonly ILogger _logger;
    private readonly IQueryableStateStore<SaveSchemaDefinition> _schemaStore;
    private readonly int _maxMigrationSteps;

    /// <summary>
    /// Creates a new SchemaMigrator instance.
    /// </summary>
    public SchemaMigrator(
        ILogger logger,
        IQueryableStateStore<SaveSchemaDefinition> schemaStore,
        int maxMigrationSteps = 10)
    {
        _logger = logger;
        _schemaStore = schemaStore;
        _maxMigrationSteps = maxMigrationSteps;
    }

    /// <summary>
    /// Finds the migration path from source version to target version.
    /// </summary>
    /// <param name="namespace">Schema namespace (e.g., game identifier)</param>
    /// <param name="fromVersion">Source schema version</param>
    /// <param name="toVersion">Target schema version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of versions to migrate through, or null if no path exists</returns>
    public async Task<List<string>?> FindMigrationPathAsync(
        string @namespace,
        string fromVersion,
        string toVersion,
        CancellationToken cancellationToken)
    {
        if (fromVersion == toVersion)
        {
            return new List<string> { fromVersion };
        }

        // Build the version graph by loading all schemas in the namespace
        var schemas = await LoadAllSchemasAsync(@namespace, cancellationToken);
        if (schemas.Count == 0)
        {
            _logger.LogWarning("No schemas found for namespace {Namespace}", @namespace);
            return null;
        }

        // Build adjacency map: version -> next version (forward migration)
        var forwardMap = new Dictionary<string, string>();
        foreach (var schema in schemas.Values)
        {
            if (!string.IsNullOrEmpty(schema.PreviousVersion))
            {
                // This version can be reached from its previous version
                forwardMap[schema.PreviousVersion] = schema.SchemaVersion;
            }
        }

        // BFS to find path from fromVersion to toVersion
        var visited = new HashSet<string>();
        var queue = new Queue<(string version, List<string> path)>();
        queue.Enqueue((fromVersion, new List<string> { fromVersion }));

        while (queue.Count > 0)
        {
            var (current, path) = queue.Dequeue();

            if (current == toVersion)
            {
                return path;
            }

            if (visited.Contains(current) || path.Count > _maxMigrationSteps)
            {
                continue;
            }
            visited.Add(current);

            // Try forward migration
            if (forwardMap.TryGetValue(current, out var nextVersion))
            {
                var newPath = new List<string>(path) { nextVersion };
                queue.Enqueue((nextVersion, newPath));
            }
        }

        _logger.LogWarning(
            "No migration path found from {From} to {To} in namespace {Namespace}",
            fromVersion, toVersion, @namespace);
        return null;
    }

    /// <summary>
    /// Applies a migration path to data, transforming it from source to target version.
    /// </summary>
    /// <param name="namespace">Schema namespace</param>
    /// <param name="data">Data to migrate (JSON bytes)</param>
    /// <param name="migrationPath">List of versions to migrate through</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Migrated data and warnings, or null if migration failed</returns>
    public async Task<MigrationResult?> ApplyMigrationPathAsync(
        string @namespace,
        byte[] data,
        List<string> migrationPath,
        CancellationToken cancellationToken)
    {
        if (migrationPath.Count < 2)
        {
            // No migration needed
            return new MigrationResult
            {
                Data = data,
                Warnings = new List<string>()
            };
        }

        var currentData = data;
        var warnings = new List<string>();

        for (int i = 0; i < migrationPath.Count - 1; i++)
        {
            var fromVersion = migrationPath[i];
            var toVersion = migrationPath[i + 1];

            var schemaKey = SaveSchemaDefinition.GetStateKey(@namespace, toVersion);
            var schema = await _schemaStore.GetAsync(schemaKey, cancellationToken);

            if (schema == null)
            {
                _logger.LogError("Schema not found for migration step: {Version}", toVersion);
                return null;
            }

            if (string.IsNullOrEmpty(schema.MigrationPatchJson))
            {
                _logger.LogWarning(
                    "No migration patch for {From} -> {To}, data will be unchanged",
                    fromVersion, toVersion);
                warnings.Add($"No migration patch for {fromVersion} -> {toVersion}");
                continue;
            }

            var migratedData = ApplyMigrationPatch(currentData, schema.MigrationPatchJson);
            if (migratedData == null)
            {
                _logger.LogError(
                    "Failed to apply migration patch from {From} to {To}",
                    fromVersion, toVersion);
                return null;
            }

            currentData = migratedData;
            _logger.LogDebug(
                "Applied migration step {From} -> {To}",
                fromVersion, toVersion);
        }

        return new MigrationResult
        {
            Data = currentData,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Applies a JSON Patch to data.
    /// </summary>
    private byte[]? ApplyMigrationPatch(byte[] data, string patchJson)
    {
        try
        {
            var dataJson = Encoding.UTF8.GetString(data);
            var dataNode = JsonNode.Parse(dataJson);
            if (dataNode == null)
            {
                _logger.LogError("Failed to parse data JSON for migration");
                return null;
            }

            var patch = BannouJson.Deserialize<JsonPatch>(patchJson);
            if (patch == null)
            {
                _logger.LogError("Failed to parse migration patch JSON");
                return null;
            }

            var result = patch.Apply(dataNode);
            if (!result.IsSuccess)
            {
                _logger.LogError("Migration patch application failed: {Error}", result.Error);
                return null;
            }

            if (result.Result == null)
            {
                _logger.LogError("Migration patch application returned null result");
                return null;
            }

            var resultJson = result.Result.ToJsonString();
            return Encoding.UTF8.GetBytes(resultJson);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to apply migration patch: invalid JSON");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply migration patch");
            return null;
        }
    }

    /// <summary>
    /// Loads all schemas in a namespace.
    /// </summary>
    private async Task<Dictionary<string, SaveSchemaDefinition>> LoadAllSchemasAsync(
        string @namespace,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, SaveSchemaDefinition>();

        // Query all schemas with the namespace
        var schemas = await _schemaStore.QueryAsync(
            s => s.Namespace == @namespace,
            cancellationToken);

        foreach (var schema in schemas)
        {
            result[schema.SchemaVersion] = schema;
        }

        return result;
    }

    /// <summary>
    /// Validates data against a schema.
    /// </summary>
    public bool ValidateAgainstSchema(byte[] data, string schemaJson)
    {
        // For now, just verify the data is valid JSON
        // Full JSON Schema validation could be added with a library like JsonSchema.Net
        try
        {
            var dataJson = Encoding.UTF8.GetString(data);
            var _ = JsonNode.Parse(dataJson);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Result of a migration operation.
/// </summary>
public class MigrationResult
{
    /// <summary>
    /// Migrated data.
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Non-fatal warnings from migration.
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}
