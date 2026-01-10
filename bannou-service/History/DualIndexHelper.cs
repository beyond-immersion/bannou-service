using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.History;

/// <summary>
/// Implementation of dual-index storage pattern for history services.
/// Maintains two indices for efficient querying from both primary and secondary dimensions.
/// </summary>
/// <typeparam name="TRecord">Type of record being stored.</typeparam>
public class DualIndexHelper<TRecord> : IDualIndexHelper<TRecord> where TRecord : class
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly string _stateStoreName;
    private readonly string _recordKeyPrefix;
    private readonly string _primaryIndexPrefix;
    private readonly string _secondaryIndexPrefix;

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
    }

    /// <summary>
    /// Creates a new DualIndexHelper with explicit parameters.
    /// </summary>
    public DualIndexHelper(
        IStateStoreFactory stateStoreFactory,
        string stateStoreName,
        string recordKeyPrefix,
        string primaryIndexPrefix,
        string secondaryIndexPrefix)
    {
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _stateStoreName = stateStoreName ?? throw new ArgumentNullException(nameof(stateStoreName));
        _recordKeyPrefix = recordKeyPrefix ?? throw new ArgumentNullException(nameof(recordKeyPrefix));
        _primaryIndexPrefix = primaryIndexPrefix ?? throw new ArgumentNullException(nameof(primaryIndexPrefix));
        _secondaryIndexPrefix = secondaryIndexPrefix ?? throw new ArgumentNullException(nameof(secondaryIndexPrefix));
    }

    /// <inheritdoc />
    public async Task<string> AddRecordAsync(
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

        // Store the record
        var recordStore = _stateStoreFactory.GetStore<TRecord>(_stateStoreName);
        await recordStore.SaveAsync($"{_recordKeyPrefix}{recordId}", record, cancellationToken: cancellationToken);

        // Update primary index
        var indexStore = _stateStoreFactory.GetStore<HistoryIndexData>(_stateStoreName);
        var primaryIndexKey = $"{_primaryIndexPrefix}{primaryKey}";
        var primaryIndex = await indexStore.GetAsync(primaryIndexKey, cancellationToken)
            ?? new HistoryIndexData { EntityId = primaryKey };
        if (!primaryIndex.RecordIds.Contains(recordId))
        {
            primaryIndex.RecordIds.Add(recordId);
            await indexStore.SaveAsync(primaryIndexKey, primaryIndex, cancellationToken: cancellationToken);
        }

        // Update secondary index
        var secondaryIndexKey = $"{_secondaryIndexPrefix}{secondaryKey}";
        var secondaryIndex = await indexStore.GetAsync(secondaryIndexKey, cancellationToken)
            ?? new HistoryIndexData { EntityId = secondaryKey };
        if (!secondaryIndex.RecordIds.Contains(recordId))
        {
            secondaryIndex.RecordIds.Add(recordId);
            await indexStore.SaveAsync(secondaryIndexKey, secondaryIndex, cancellationToken: cancellationToken);
        }

        return recordId;
    }

    /// <inheritdoc />
    public async Task<TRecord?> GetRecordAsync(
        string recordId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(recordId)) return null;

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
        if (string.IsNullOrEmpty(primaryKey)) return Array.Empty<string>();

        var indexStore = _stateStoreFactory.GetStore<HistoryIndexData>(_stateStoreName);
        var index = await indexStore.GetAsync($"{_primaryIndexPrefix}{primaryKey}", cancellationToken);
        return index?.RecordIds ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRecordIdsBySecondaryKeyAsync(
        string secondaryKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(secondaryKey)) return Array.Empty<string>();

        var indexStore = _stateStoreFactory.GetStore<HistoryIndexData>(_stateStoreName);
        var index = await indexStore.GetAsync($"{_secondaryIndexPrefix}{secondaryKey}", cancellationToken);
        return index?.RecordIds ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    /// <inheritdoc />
    public async Task<bool> RemoveRecordAsync(
        string recordId,
        string primaryKey,
        string secondaryKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(recordId)) return false;

        var recordStore = _stateStoreFactory.GetStore<TRecord>(_stateStoreName);
        var recordKey = $"{_recordKeyPrefix}{recordId}";
        var record = await recordStore.GetAsync(recordKey, cancellationToken);

        if (record == null)
        {
            return false;
        }

        // Delete the record
        await recordStore.DeleteAsync(recordKey, cancellationToken);

        // Update primary index
        var indexStore = _stateStoreFactory.GetStore<HistoryIndexData>(_stateStoreName);
        if (!string.IsNullOrEmpty(primaryKey))
        {
            var primaryIndexKey = $"{_primaryIndexPrefix}{primaryKey}";
            var primaryIndex = await indexStore.GetAsync(primaryIndexKey, cancellationToken);
            if (primaryIndex != null)
            {
                primaryIndex.RecordIds.Remove(recordId);
                await indexStore.SaveAsync(primaryIndexKey, primaryIndex, cancellationToken: cancellationToken);
            }
        }

        // Update secondary index
        if (!string.IsNullOrEmpty(secondaryKey))
        {
            var secondaryIndexKey = $"{_secondaryIndexPrefix}{secondaryKey}";
            var secondaryIndex = await indexStore.GetAsync(secondaryIndexKey, cancellationToken);
            if (secondaryIndex != null)
            {
                secondaryIndex.RecordIds.Remove(recordId);
                await indexStore.SaveAsync(secondaryIndexKey, secondaryIndex, cancellationToken: cancellationToken);
            }
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<int> RemoveAllByPrimaryKeyAsync(
        string primaryKey,
        Func<TRecord, string> getSecondaryKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(primaryKey)) return 0;
        if (getSecondaryKey == null) throw new ArgumentNullException(nameof(getSecondaryKey));

        var indexStore = _stateStoreFactory.GetStore<HistoryIndexData>(_stateStoreName);
        var primaryIndexKey = $"{_primaryIndexPrefix}{primaryKey}";
        var primaryIndex = await indexStore.GetAsync(primaryIndexKey, cancellationToken);

        if (primaryIndex == null || primaryIndex.RecordIds.Count == 0)
        {
            return 0;
        }

        var recordStore = _stateStoreFactory.GetStore<TRecord>(_stateStoreName);
        var deletedCount = 0;

        // Process each record
        foreach (var recordId in primaryIndex.RecordIds.ToList())
        {
            var recordKey = $"{_recordKeyPrefix}{recordId}";
            var record = await recordStore.GetAsync(recordKey, cancellationToken);

            if (record != null)
            {
                // Get secondary key and update that index
                var secondaryKey = getSecondaryKey(record);
                if (!string.IsNullOrEmpty(secondaryKey))
                {
                    var secondaryIndexKey = $"{_secondaryIndexPrefix}{secondaryKey}";
                    var secondaryIndex = await indexStore.GetAsync(secondaryIndexKey, cancellationToken);
                    if (secondaryIndex != null)
                    {
                        secondaryIndex.RecordIds.Remove(recordId);
                        await indexStore.SaveAsync(secondaryIndexKey, secondaryIndex, cancellationToken: cancellationToken);
                    }
                }

                // Delete the record
                await recordStore.DeleteAsync(recordKey, cancellationToken);
                deletedCount++;
            }
        }

        // Delete the primary index
        await indexStore.DeleteAsync(primaryIndexKey, cancellationToken);

        return deletedCount;
    }

    /// <inheritdoc />
    public async Task<bool> HasRecordsForPrimaryKeyAsync(
        string primaryKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(primaryKey)) return false;

        var indexStore = _stateStoreFactory.GetStore<HistoryIndexData>(_stateStoreName);
        var index = await indexStore.GetAsync($"{_primaryIndexPrefix}{primaryKey}", cancellationToken);
        return index != null && index.RecordIds.Count > 0;
    }

    /// <inheritdoc />
    public async Task<int> GetRecordCountByPrimaryKeyAsync(
        string primaryKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(primaryKey)) return 0;

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
