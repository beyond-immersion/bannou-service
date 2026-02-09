using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.History;

/// <summary>
/// Implementation of dual-index storage pattern for history services.
/// Maintains two indices for efficient querying from both primary and secondary dimensions.
/// Write operations acquire a distributed lock on the primary key per IMPLEMENTATION TENETS.
/// </summary>
/// <remarks>
/// <para><strong>Why the secondary index is not locked:</strong></para>
/// <para>
/// The primary index is locked because it maps a single entity (e.g., one realm or character)
/// to its records. Concurrent writes to the same entity would corrupt the read-modify-write
/// sequence, so locking by primary key serializes those writes.
/// </para>
/// <para>
/// The secondary index maps a shared dimension (e.g., an event ID) to records from many
/// entities. Locking the secondary index would serialize ALL writes across unrelated entities
/// that happen to reference the same event — creating a global bottleneck. Since reads bypass
/// indexes entirely via <c>IJsonQueryableStateStore.JsonQueryPagedAsync()</c> (server-side MySQL
/// JSON queries), the secondary index is only a write-path optimization for reverse lookups
/// during deletion. A worst-case race condition produces a stale entry in the secondary index
/// (pointing to a record that was already deleted), which is harmless: the record lookup
/// returns null and the caller filters it out. The index is self-healing because the next
/// write or delete for that secondary key will reconcile the list.
/// </para>
/// <para>
/// Lock owner IDs use the record key prefix (e.g., "character-participation-{guid}" or
/// "realm-participation-{guid}") for traceability when diagnosing lock contention.
/// </para>
/// </remarks>
/// <typeparam name="TRecord">Type of record being stored.</typeparam>
public class DualIndexHelper<TRecord> : IDualIndexHelper<TRecord> where TRecord : class
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly string _stateStoreName;
    private readonly string _recordKeyPrefix;
    private readonly string _primaryIndexPrefix;
    private readonly string _secondaryIndexPrefix;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly int _lockTimeoutSeconds;

    /// <summary>
    /// Creates a new DualIndexHelper with the specified configuration.
    /// </summary>
    /// <param name="config">Configuration for the helper.</param>
    /// <exception cref="ArgumentNullException">Thrown when config or its properties are null.</exception>
    public DualIndexHelper(DualIndexConfiguration config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        _stateStoreFactory = config.StateStoreFactory ?? throw new ArgumentNullException(nameof(config.StateStoreFactory));
        _stateStoreName = config.StateStoreName ?? throw new ArgumentNullException(nameof(config.StateStoreName));
        _recordKeyPrefix = config.RecordKeyPrefix ?? throw new ArgumentNullException(nameof(config.RecordKeyPrefix));
        _primaryIndexPrefix = config.PrimaryIndexPrefix ?? throw new ArgumentNullException(nameof(config.PrimaryIndexPrefix));
        _secondaryIndexPrefix = config.SecondaryIndexPrefix ?? throw new ArgumentNullException(nameof(config.SecondaryIndexPrefix));
        _lockProvider = config.LockProvider ?? throw new ArgumentNullException(nameof(config.LockProvider));
        _lockTimeoutSeconds = config.LockTimeoutSeconds;
    }

    /// <summary>
    /// Creates a new DualIndexHelper with explicit parameters.
    /// </summary>
    public DualIndexHelper(
        IStateStoreFactory stateStoreFactory,
        string stateStoreName,
        string recordKeyPrefix,
        string primaryIndexPrefix,
        string secondaryIndexPrefix,
        IDistributedLockProvider lockProvider,
        int lockTimeoutSeconds)
    {
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _stateStoreName = stateStoreName ?? throw new ArgumentNullException(nameof(stateStoreName));
        _recordKeyPrefix = recordKeyPrefix ?? throw new ArgumentNullException(nameof(recordKeyPrefix));
        _primaryIndexPrefix = primaryIndexPrefix ?? throw new ArgumentNullException(nameof(primaryIndexPrefix));
        _secondaryIndexPrefix = secondaryIndexPrefix ?? throw new ArgumentNullException(nameof(secondaryIndexPrefix));
        _lockProvider = lockProvider ?? throw new ArgumentNullException(nameof(lockProvider));
        _lockTimeoutSeconds = lockTimeoutSeconds;
    }

    /// <inheritdoc />
    public async Task<LockableResult<string>> AddRecordAsync(
        TRecord record,
        string recordId,
        string primaryKey,
        string secondaryKey,
        CancellationToken cancellationToken = default)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (string.IsNullOrEmpty(recordId)) throw new ArgumentNullException(nameof(recordId));
        if (string.IsNullOrEmpty(primaryKey)) throw new ArgumentNullException(nameof(primaryKey));
        if (string.IsNullOrEmpty(secondaryKey)) throw new ArgumentNullException(nameof(secondaryKey));

        // Acquire distributed lock on primary key per IMPLEMENTATION TENETS
        await using var lockResponse = await _lockProvider.LockAsync(
            "history-index", primaryKey, $"{_recordKeyPrefix}{Guid.NewGuid()}", _lockTimeoutSeconds, cancellationToken);

        if (!lockResponse.Success)
        {
            return new LockableResult<string>(false, null);
        }

        // Store the record
        var recordStore = _stateStoreFactory.GetStore<TRecord>(_stateStoreName);
        await recordStore.SaveAsync($"{_recordKeyPrefix}{recordId}", record, cancellationToken: cancellationToken);

        // Update primary index (read-modify-write protected by lock)
        var indexStore = _stateStoreFactory.GetStore<HistoryIndexData>(_stateStoreName);
        var primaryIndexKey = $"{_primaryIndexPrefix}{primaryKey}";
        var primaryIndex = await indexStore.GetAsync(primaryIndexKey, cancellationToken)
            ?? new HistoryIndexData { EntityId = primaryKey };
        if (!primaryIndex.RecordIds.Contains(recordId))
        {
            primaryIndex.RecordIds.Add(recordId);
            await indexStore.SaveAsync(primaryIndexKey, primaryIndex, cancellationToken: cancellationToken);
        }

        // Update secondary index (not locked; concurrent writes to different entities
        // targeting the same secondary index are a known harmless limitation since
        // reads bypass indexes via server-side JSON queries)
        var secondaryIndexKey = $"{_secondaryIndexPrefix}{secondaryKey}";
        var secondaryIndex = await indexStore.GetAsync(secondaryIndexKey, cancellationToken)
            ?? new HistoryIndexData { EntityId = secondaryKey };
        if (!secondaryIndex.RecordIds.Contains(recordId))
        {
            secondaryIndex.RecordIds.Add(recordId);
            await indexStore.SaveAsync(secondaryIndexKey, secondaryIndex, cancellationToken: cancellationToken);
        }

        return new LockableResult<string>(true, recordId);
    }

    /// <inheritdoc />
    public async Task<TRecord?> GetRecordAsync(
        string recordId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(recordId)) throw new ArgumentNullException(nameof(recordId));

        var recordStore = _stateStoreFactory.GetStore<TRecord>(_stateStoreName);
        return await recordStore.GetAsync($"{_recordKeyPrefix}{recordId}", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TRecord>> GetRecordsByPrimaryKeyAsync(
        string primaryKey,
        CancellationToken cancellationToken = default)
    {
        var recordIds = await GetRecordIdsByPrimaryKeyAsync(primaryKey, cancellationToken);
        return await GetRecordsByIdsAsync(recordIds, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TRecord>> GetRecordsBySecondaryKeyAsync(
        string secondaryKey,
        CancellationToken cancellationToken = default)
    {
        var recordIds = await GetRecordIdsBySecondaryKeyAsync(secondaryKey, cancellationToken);
        return await GetRecordsByIdsAsync(recordIds, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRecordIdsByPrimaryKeyAsync(
        string primaryKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(primaryKey)) throw new ArgumentNullException(nameof(primaryKey));

        var indexStore = _stateStoreFactory.GetStore<HistoryIndexData>(_stateStoreName);
        var index = await indexStore.GetAsync($"{_primaryIndexPrefix}{primaryKey}", cancellationToken);
        return index?.RecordIds ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRecordIdsBySecondaryKeyAsync(
        string secondaryKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(secondaryKey)) throw new ArgumentNullException(nameof(secondaryKey));

        var indexStore = _stateStoreFactory.GetStore<HistoryIndexData>(_stateStoreName);
        var index = await indexStore.GetAsync($"{_secondaryIndexPrefix}{secondaryKey}", cancellationToken);
        return index?.RecordIds ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    /// <inheritdoc />
    public async Task<LockableResult<bool>> RemoveRecordAsync(
        string recordId,
        string primaryKey,
        string secondaryKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(recordId)) throw new ArgumentNullException(nameof(recordId));
        if (string.IsNullOrEmpty(primaryKey)) throw new ArgumentNullException(nameof(primaryKey));
        if (string.IsNullOrEmpty(secondaryKey)) throw new ArgumentNullException(nameof(secondaryKey));

        // Acquire distributed lock on primary key per IMPLEMENTATION TENETS
        await using var lockResponse = await _lockProvider.LockAsync(
            "history-index", primaryKey, $"{_recordKeyPrefix}{Guid.NewGuid()}", _lockTimeoutSeconds, cancellationToken);

        if (!lockResponse.Success)
        {
            return new LockableResult<bool>(false, false);
        }

        var recordStore = _stateStoreFactory.GetStore<TRecord>(_stateStoreName);
        var recordKey = $"{_recordKeyPrefix}{recordId}";
        var record = await recordStore.GetAsync(recordKey, cancellationToken);

        if (record == null)
        {
            return new LockableResult<bool>(true, false);
        }

        // Delete the record
        await recordStore.DeleteAsync(recordKey, cancellationToken);

        // Update primary index (read-modify-write protected by lock)
        var indexStore = _stateStoreFactory.GetStore<HistoryIndexData>(_stateStoreName);
        var primaryIndexKey = $"{_primaryIndexPrefix}{primaryKey}";
        var primaryIndex = await indexStore.GetAsync(primaryIndexKey, cancellationToken);
        if (primaryIndex != null)
        {
            primaryIndex.RecordIds.Remove(recordId);
            // Delete empty index documents to avoid accumulating stale data
            if (primaryIndex.RecordIds.Count == 0)
            {
                await indexStore.DeleteAsync(primaryIndexKey, cancellationToken);
            }
            else
            {
                await indexStore.SaveAsync(primaryIndexKey, primaryIndex, cancellationToken: cancellationToken);
            }
        }

        // Update secondary index (not locked; see AddRecordAsync comment)
        var secondaryIndexKey = $"{_secondaryIndexPrefix}{secondaryKey}";
        var secondaryIndex = await indexStore.GetAsync(secondaryIndexKey, cancellationToken);
        if (secondaryIndex != null)
        {
            secondaryIndex.RecordIds.Remove(recordId);
            // Delete empty index documents to avoid accumulating stale data
            if (secondaryIndex.RecordIds.Count == 0)
            {
                await indexStore.DeleteAsync(secondaryIndexKey, cancellationToken);
            }
            else
            {
                await indexStore.SaveAsync(secondaryIndexKey, secondaryIndex, cancellationToken: cancellationToken);
            }
        }

        return new LockableResult<bool>(true, true);
    }

    /// <inheritdoc />
    public async Task<LockableResult<int>> RemoveAllByPrimaryKeyAsync(
        string primaryKey,
        Func<TRecord, string> getSecondaryKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(primaryKey)) throw new ArgumentNullException(nameof(primaryKey));
        if (getSecondaryKey == null) throw new ArgumentNullException(nameof(getSecondaryKey));

        // Acquire distributed lock on primary key per IMPLEMENTATION TENETS
        await using var lockResponse = await _lockProvider.LockAsync(
            "history-index", primaryKey, $"{_recordKeyPrefix}{Guid.NewGuid()}", _lockTimeoutSeconds, cancellationToken);

        if (!lockResponse.Success)
        {
            return new LockableResult<int>(false, 0);
        }

        var indexStore = _stateStoreFactory.GetStore<HistoryIndexData>(_stateStoreName);
        var primaryIndexKey = $"{_primaryIndexPrefix}{primaryKey}";
        var primaryIndex = await indexStore.GetAsync(primaryIndexKey, cancellationToken);

        if (primaryIndex == null || primaryIndex.RecordIds.Count == 0)
        {
            return new LockableResult<int>(true, 0);
        }

        var recordStore = _stateStoreFactory.GetStore<TRecord>(_stateStoreName);

        // Build record key to ID mapping for later extraction
        var recordIdToKey = primaryIndex.RecordIds
            .ToDictionary(id => $"{_recordKeyPrefix}{id}", id => id);
        var recordKeys = recordIdToKey.Keys.ToList();

        // Bulk get all records (1 operation instead of N)
        var records = await recordStore.GetBulkAsync(recordKeys, cancellationToken);

        // Group records by secondary key for efficient index updates
        var recordsBySecondaryKey = new Dictionary<string, List<string>>();
        foreach (var (recordKey, record) in records)
        {
            var secondaryKey = getSecondaryKey(record);
            if (!string.IsNullOrEmpty(secondaryKey))
            {
                if (!recordsBySecondaryKey.TryGetValue(secondaryKey, out var recordIds))
                {
                    recordIds = new List<string>();
                    recordsBySecondaryKey[secondaryKey] = recordIds;
                }
                recordIds.Add(recordIdToKey[recordKey]);
            }
        }

        // Bulk get all secondary indices (1 operation instead of N)
        if (recordsBySecondaryKey.Count > 0)
        {
            var secondaryIndexKeys = recordsBySecondaryKey.Keys
                .Select(sk => $"{_secondaryIndexPrefix}{sk}")
                .ToList();
            var secondaryIndices = await indexStore.GetBulkAsync(secondaryIndexKeys, cancellationToken);

            // Update indices in-memory, separating empty from non-empty
            var updatedIndices = new Dictionary<string, HistoryIndexData>();
            var emptyIndexKeys = new List<string>();
            foreach (var (secondaryKey, recordIdsToRemove) in recordsBySecondaryKey)
            {
                var indexKey = $"{_secondaryIndexPrefix}{secondaryKey}";
                if (secondaryIndices.TryGetValue(indexKey, out var index))
                {
                    foreach (var recordId in recordIdsToRemove)
                    {
                        index.RecordIds.Remove(recordId);
                    }
                    // Track empty indices for deletion, non-empty for update
                    if (index.RecordIds.Count == 0)
                    {
                        emptyIndexKeys.Add(indexKey);
                    }
                    else
                    {
                        updatedIndices[indexKey] = index;
                    }
                }
            }

            // Bulk save non-empty secondary indices
            if (updatedIndices.Count > 0)
            {
                await indexStore.SaveBulkAsync(updatedIndices, cancellationToken: cancellationToken);
            }

            // Bulk delete empty secondary indices to avoid accumulating stale data
            if (emptyIndexKeys.Count > 0)
            {
                await indexStore.DeleteBulkAsync(emptyIndexKeys, cancellationToken);
            }
        }

        // Bulk delete all records (1 operation instead of N)
        var actualDeletedCount = await recordStore.DeleteBulkAsync(recordKeys, cancellationToken);

        // Delete the primary index
        await indexStore.DeleteAsync(primaryIndexKey, cancellationToken);

        return new LockableResult<int>(true, actualDeletedCount);
    }

    /// <inheritdoc />
    public async Task<bool> HasRecordsForPrimaryKeyAsync(
        string primaryKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(primaryKey)) throw new ArgumentNullException(nameof(primaryKey));

        var indexStore = _stateStoreFactory.GetStore<HistoryIndexData>(_stateStoreName);
        var index = await indexStore.GetAsync($"{_primaryIndexPrefix}{primaryKey}", cancellationToken);
        return index != null && index.RecordIds.Count > 0;
    }

    /// <inheritdoc />
    public async Task<int> GetRecordCountByPrimaryKeyAsync(
        string primaryKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(primaryKey)) throw new ArgumentNullException(nameof(primaryKey));

        var indexStore = _stateStoreFactory.GetStore<HistoryIndexData>(_stateStoreName);
        var index = await indexStore.GetAsync($"{_primaryIndexPrefix}{primaryKey}", cancellationToken);
        return index?.RecordIds.Count ?? 0;
    }

    /// <summary>
    /// Retrieves multiple records by their IDs.
    /// </summary>
    private async Task<IReadOnlyList<TRecord>> GetRecordsByIdsAsync(
        IReadOnlyList<string> recordIds,
        CancellationToken cancellationToken)
    {
        if (recordIds.Count == 0)
        {
            return Array.Empty<TRecord>();
        }

        var recordStore = _stateStoreFactory.GetStore<TRecord>(_stateStoreName);
        var keys = recordIds.Select(id => $"{_recordKeyPrefix}{id}");
        var records = await recordStore.GetBulkAsync(keys, cancellationToken);

        return records.Values.ToList();
    }
}
