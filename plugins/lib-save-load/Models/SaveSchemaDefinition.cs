namespace BeyondImmersion.BannouService.SaveLoad.Models;

/// <summary>
/// Internal model for storing schema definitions.
/// Used to track registered schemas and their migration paths.
/// </summary>
public class SaveSchemaDefinition
{
    /// <summary>
    /// Schema namespace (e.g., game identifier).
    /// </summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>
    /// Schema version identifier.
    /// </summary>
    public string SchemaVersion { get; set; } = string.Empty;

    /// <summary>
    /// JSON Schema definition for validation (serialized JSON).
    /// </summary>
    public string SchemaJson { get; set; } = string.Empty;

    /// <summary>
    /// Previous version this migrates from (null for initial version).
    /// </summary>
    public string? PreviousVersion { get; set; }

    /// <summary>
    /// JSON Patch (RFC 6902) operations to migrate from previousVersion (serialized JSON).
    /// </summary>
    public string? MigrationPatchJson { get; set; }

    /// <summary>
    /// When the schema was registered.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Whether migration script is registered.
    /// </summary>
    public bool HasMigration => !string.IsNullOrEmpty(MigrationPatchJson);

    /// <summary>
    /// Gets the state store key for this schema.
    /// </summary>
    public static string GetStateKey(string @namespace, string schemaVersion)
    {
        return $"{@namespace}:{schemaVersion}";
    }
}
