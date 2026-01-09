using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.History;

/// <summary>
/// Configuration for creating a DualIndexHelper instance.
/// </summary>
/// <param name="StateStoreFactory">Factory for creating state stores.</param>
/// <param name="StateStoreName">Name of the state store to use.</param>
/// <param name="RecordKeyPrefix">Prefix for record keys (e.g., "participation-").</param>
/// <param name="PrimaryIndexPrefix">Prefix for primary index keys (e.g., "participation-index-").</param>
/// <param name="SecondaryIndexPrefix">Prefix for secondary index keys (e.g., "participation-event-").</param>
public record DualIndexConfiguration(
    IStateStoreFactory StateStoreFactory,
    string StateStoreName,
    string RecordKeyPrefix,
    string PrimaryIndexPrefix,
    string SecondaryIndexPrefix
);

/// <summary>
/// Interface for managing dual-index storage patterns.
/// Dual-index allows efficient querying from two dimensions:
/// - Primary: "Find all records for entity X" (e.g., all participations for a character)
/// - Secondary: "Find all records related to Y" (e.g., all participants in an event)
/// </summary>
/// <typeparam name="TRecord">Type of record being stored.</typeparam>
public interface IDualIndexHelper<TRecord> where TRecord : class
{
    /// <summary>
    /// Adds a record with dual-index entries.
    /// Creates index entries for both primary and secondary keys.
    /// </summary>
    /// <param name="record">The record to store.</param>
    /// <param name="recordId">Unique identifier for the record.</param>
    /// <param name="primaryKey">Primary index key (e.g., character ID).</param>
    /// <param name="secondaryKey">Secondary index key (e.g., event ID).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record ID.</returns>
    Task<string> AddRecordAsync(
        TRecord record,
        string recordId,
        string primaryKey,
        string secondaryKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a record by its ID.
    /// </summary>
    /// <param name="recordId">The record identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record, or null if not found.</returns>
    Task<TRecord?> GetRecordAsync(
        string recordId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all records for a primary key (e.g., all participations for a character).
    /// </summary>
    /// <param name="primaryKey">The primary key to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of records.</returns>
    Task<IReadOnlyList<TRecord>> GetRecordsByPrimaryKeyAsync(
        string primaryKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all records for a secondary key (e.g., all participants in an event).
    /// </summary>
    /// <param name="secondaryKey">The secondary key to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of records.</returns>
    Task<IReadOnlyList<TRecord>> GetRecordsBySecondaryKeyAsync(
        string secondaryKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets record IDs for a primary key.
    /// </summary>
    /// <param name="primaryKey">The primary key to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of record IDs.</returns>
    Task<IReadOnlyList<string>> GetRecordIdsByPrimaryKeyAsync(
        string primaryKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets record IDs for a secondary key.
    /// </summary>
    /// <param name="secondaryKey">The secondary key to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of record IDs.</returns>
    Task<IReadOnlyList<string>> GetRecordIdsBySecondaryKeyAsync(
        string secondaryKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a record and updates both indices.
    /// </summary>
    /// <param name="recordId">The record identifier.</param>
    /// <param name="primaryKey">Primary index key.</param>
    /// <param name="secondaryKey">Secondary index key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if record was found and deleted.</returns>
    Task<bool> RemoveRecordAsync(
        string recordId,
        string primaryKey,
        string secondaryKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all records for a primary key and updates secondary indices.
    /// Used during entity deletion/archival.
    /// </summary>
    /// <param name="primaryKey">The primary key whose records should be deleted.</param>
    /// <param name="getSecondaryKey">Function to extract secondary key from record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of records deleted.</returns>
    Task<int> RemoveAllByPrimaryKeyAsync(
        string primaryKey,
        Func<TRecord, string> getSecondaryKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if any records exist for a primary key.
    /// </summary>
    /// <param name="primaryKey">The primary key to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if at least one record exists.</returns>
    Task<bool> HasRecordsForPrimaryKeyAsync(
        string primaryKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of records for a primary key.
    /// </summary>
    /// <param name="primaryKey">The primary key to count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of records.</returns>
    Task<int> GetRecordCountByPrimaryKeyAsync(
        string primaryKey,
        CancellationToken cancellationToken = default);
}
