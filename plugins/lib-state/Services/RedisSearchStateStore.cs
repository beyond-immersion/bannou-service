#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using NRedisStack;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using NRedisStack.Search.Literals.Enums;
using StackExchange.Redis;

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// Redis-backed state store with full-text search capabilities via RedisSearch.
/// Uses JSON documents for storage to enable field-level indexing.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public sealed class RedisSearchStateStore<TValue> : ISearchableStateStore<TValue>
    where TValue : class
{
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly TimeSpan? _defaultTtl;
    private readonly ILogger<RedisSearchStateStore<TValue>> _logger;
    private readonly SearchCommands _searchCommands;
    private readonly JsonCommands _jsonCommands;

    /// <summary>
    /// Creates a new Redis search state store.
    /// </summary>
    /// <param name="database">Redis database connection.</param>
    /// <param name="keyPrefix">Key prefix for namespacing.</param>
    /// <param name="defaultTtl">Default TTL for entries (null = no expiration).</param>
    /// <param name="logger">Logger instance.</param>
    public RedisSearchStateStore(
        IDatabase database,
        string keyPrefix,
        TimeSpan? defaultTtl,
        ILogger<RedisSearchStateStore<TValue>> logger)
    {
        _database = database;
        _keyPrefix = keyPrefix;
        _defaultTtl = defaultTtl;
        _logger = logger;
        _searchCommands = _database.FT();
        _jsonCommands = _database.JSON();
    }

    private string GetFullKey(string key) => $"{_keyPrefix}:{key}";
    private string GetMetaKey(string key) => $"{_keyPrefix}:{key}:meta";

    #region IStateStore<TValue> Implementation

    /// <inheritdoc/>
    public async Task<TValue?> GetAsync(string key, CancellationToken cancellationToken = default)
    {

        var fullKey = GetFullKey(key);

        try
        {
            var value = await _jsonCommands.GetAsync(fullKey);
            if (value.IsNull)
            {
                _logger.LogDebug("Key '{Key}' not found in store '{Store}'", key, _keyPrefix);
                return null;
            }

            return BannouJson.Deserialize<TValue>(value.ToString());
        }
        catch (RedisException ex) when (ex.Message.Contains("WRONGTYPE"))
        {
            // Fall back to string storage for backwards compatibility
            var stringValue = await _database.StringGetAsync(fullKey);
            if (stringValue.IsNullOrEmpty)
            {
                return null;
            }
            return BannouJson.Deserialize<TValue>(stringValue!);
        }
    }

    /// <inheritdoc/>
    public async Task<(TValue? Value, string? ETag)> GetWithETagAsync(
        string key,
        CancellationToken cancellationToken = default)
    {

        var fullKey = GetFullKey(key);
        var metaKey = GetMetaKey(key);

        try
        {
            var valueTask = _jsonCommands.GetAsync(fullKey);
            var versionTask = _database.HashGetAsync(metaKey, "version");

            await Task.WhenAll(valueTask, versionTask);

            var value = await valueTask;
            var version = await versionTask;

            if (value.IsNull)
            {
                return (null, null);
            }

            var etag = version.HasValue ? version.ToString() : "0";
            return (BannouJson.Deserialize<TValue>(value.ToString()), etag);
        }
        catch (RedisException ex) when (ex.Message.Contains("WRONGTYPE"))
        {
            // Fall back to string storage
            var stringValue = await _database.StringGetAsync(fullKey);
            var version = await _database.HashGetAsync(metaKey, "version");

            if (stringValue.IsNullOrEmpty)
            {
                return (null, null);
            }

            var etag = version.HasValue ? version.ToString() : "0";
            return (BannouJson.Deserialize<TValue>(stringValue!), etag);
        }
    }

    /// <inheritdoc/>
    public async Task<string> SaveAsync(
        string key,
        TValue value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {

        var fullKey = GetFullKey(key);
        var metaKey = GetMetaKey(key);
        var json = BannouJson.Serialize(value);
        // Convert int? TTL (seconds) to TimeSpan?
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;

        // Store as JSON document for search indexing
        await _jsonCommands.SetAsync(fullKey, "$", json);

        // Set TTL if specified
        if (ttl.HasValue)
        {
            await _database.KeyExpireAsync(fullKey, ttl.Value);
        }

        // Update metadata
        var newVersion = await _database.HashIncrementAsync(metaKey, "version", 1);
        await _database.HashSetAsync(metaKey, new HashEntry[]
        {
            new("updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        });

        if (ttl.HasValue)
        {
            await _database.KeyExpireAsync(metaKey, ttl.Value);
        }

        _logger.LogDebug("Saved key '{Key}' in store '{Store}' as JSON (version: {Version})",
            key, _keyPrefix, newVersion);

        return newVersion.ToString();
    }

    /// <inheritdoc/>
    public async Task<string?> TrySaveAsync(
        string key,
        TValue value,
        string etag,
        CancellationToken cancellationToken = default)
    {

        var fullKey = GetFullKey(key);
        var metaKey = GetMetaKey(key);

        // Check current version
        var currentVersion = await _database.HashGetAsync(metaKey, "version");
        if (currentVersion.ToString() != etag)
        {
            _logger.LogDebug("ETag mismatch for key '{Key}' in store '{Store}' (expected: {Expected}, actual: {Actual})",
                key, _keyPrefix, etag, currentVersion.ToString());
            return null;
        }

        // Perform optimistic update using transaction
        var json = BannouJson.Serialize(value);
        var transaction = _database.CreateTransaction();

        transaction.AddCondition(Condition.HashEqual(metaKey, "version", etag));

        _ = transaction.ExecuteAsync(CommandFlags.FireAndForget);

        // Store as JSON
        await _jsonCommands.SetAsync(fullKey, "$", json);
        await _database.HashIncrementAsync(metaKey, "version", 1);
        await _database.HashSetAsync(metaKey, new HashEntry[]
        {
            new("updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        });

        // New version is original + 1
        var newVersion = long.Parse(etag) + 1;
        _logger.LogDebug("Optimistic save succeeded for key '{Key}' in store '{Store}'", key, _keyPrefix);
        return newVersion.ToString();
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {

        var fullKey = GetFullKey(key);
        var metaKey = GetMetaKey(key);

        var valueDeleted = await _database.KeyDeleteAsync(fullKey);
        await _database.KeyDeleteAsync(metaKey);

        _logger.LogDebug("Deleted key '{Key}' from store '{Store}' (existed: {Existed})",
            key, _keyPrefix, valueDeleted);

        return valueDeleted;
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {

        var fullKey = GetFullKey(key);
        return await _database.KeyExistsAsync(fullKey);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, TValue>> GetBulkAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {

        var keyList = keys.ToList();
        if (keyList.Count == 0)
        {
            return new Dictionary<string, TValue>();
        }

        var result = new Dictionary<string, TValue>();

        // JSON.MGET for bulk retrieval
        var redisKeys = keyList.Select(k => (RedisKey)GetFullKey(k)).ToArray();

        try
        {
            var values = await _jsonCommands.MGetAsync(redisKeys, "$");

            for (var i = 0; i < keyList.Count; i++)
            {
                if (values[i] != null && !values[i].IsNull && values[i].Length > 0)
                {
                    var jsonValue = values[i][0];
                    if (!jsonValue.IsNull)
                    {
                        var deserialized = BannouJson.Deserialize<TValue>(jsonValue.ToString());
                        if (deserialized != null)
                        {
                            result[keyList[i]] = deserialized;
                        }
                    }
                }
            }
        }
        catch (RedisException)
        {
            // Fall back to individual gets
            foreach (var key in keyList)
            {
                var value = await GetAsync(key, cancellationToken);
                if (value != null)
                {
                    result[key] = value;
                }
            }
        }

        _logger.LogDebug("Bulk get {RequestedCount} keys from store '{Store}', found {FoundCount}",
            keyList.Count, _keyPrefix, result.Count);

        return result;
    }

    #endregion

    #region ISearchableStateStore<TValue> Implementation

    /// <inheritdoc/>
    public async Task<bool> CreateIndexAsync(
        string indexName,
        IReadOnlyList<SearchSchemaField> schema,
        SearchIndexOptions? options = null,
        CancellationToken cancellationToken = default)
    {

        if (schema.Count == 0)
        {
            throw new ArgumentException("Schema must contain at least one field", nameof(schema));
        }

        options ??= new SearchIndexOptions();

        try
        {
            // Check if index already exists
            try
            {
                await _searchCommands.InfoAsync(indexName);
                _logger.LogDebug("Index '{Index}' already exists, dropping for recreation", indexName);
                await _searchCommands.DropIndexAsync(indexName, false);
            }
            catch (RedisException ex) when (ex.Message.Contains("Unknown index") || ex.Message.Contains("no such index"))
            {
                // Index doesn't exist, which is fine
            }

            // Build schema
            var ftSchema = new Schema();
            foreach (var field in schema)
            {
                var alias = field.Alias ?? field.Path.TrimStart('$', '.');

                switch (field.Type)
                {
                    case SearchFieldType.Text:
                        ftSchema.AddTextField(new FieldName(field.Path, alias), field.Weight, field.Sortable, field.NoStem);
                        break;
                    case SearchFieldType.Tag:
                        ftSchema.AddTagField(new FieldName(field.Path, alias), sortable: field.Sortable);
                        break;
                    case SearchFieldType.Numeric:
                        ftSchema.AddNumericField(new FieldName(field.Path, alias), field.Sortable);
                        break;
                    case SearchFieldType.Geo:
                        ftSchema.AddGeoField(new FieldName(field.Path, alias));
                        break;
                    case SearchFieldType.Vector:
                        // Vector fields require additional configuration - use default FLAT algorithm
                        ftSchema.AddVectorField(new FieldName(field.Path, alias), Schema.VectorField.VectorAlgo.FLAT);
                        break;
                }
            }

            // Build creation parameters
            var ftParams = new FTCreateParams()
                .On(IndexDataType.JSON);

            // Use custom prefix if specified, otherwise use store's key prefix
            // NOTE: Prefix() appends to list, so we must only call it once
            if (!string.IsNullOrEmpty(options.Prefix))
            {
                ftParams.Prefix(options.Prefix);
            }
            else
            {
                ftParams.Prefix($"{_keyPrefix}:");
            }

            // Create the index
            await _searchCommands.CreateAsync(indexName, ftParams, ftSchema);

            _logger.LogInformation("Created search index '{Index}' for store '{Store}' with {FieldCount} fields",
                indexName, _keyPrefix, schema.Count);

            return true;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to create search index '{Index}' for store '{Store}'",
                indexName, _keyPrefix);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DropIndexAsync(
        string indexName,
        bool deleteDocuments = false,
        CancellationToken cancellationToken = default)
    {

        try
        {
            await _searchCommands.DropIndexAsync(indexName, deleteDocuments);
            _logger.LogInformation("Dropped search index '{Index}' (deleteDocuments: {DeleteDocs})",
                indexName, deleteDocuments);
            return true;
        }
        catch (RedisException ex) when (ex.Message.Contains("Unknown index") || ex.Message.Contains("no such index"))
        {
            _logger.LogDebug("Index '{Index}' not found for deletion", indexName);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<SearchPagedResult<TValue>> SearchAsync(
        string indexName,
        string query,
        SearchQueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {

        options ??= new SearchQueryOptions();

        try
        {
            var ftQuery = new Query(query)
                .Limit(options.Offset, options.Limit);

            if (options.WithScores)
            {
                ftQuery.WithScores = true;
            }

            if (!string.IsNullOrEmpty(options.SortBy))
            {
                ftQuery.SetSortBy(options.SortBy, options.SortDescending);
            }

            // CRITICAL: For JSON indexes, we must explicitly request the full JSON document
            // to be returned using RETURN 1 $ AS json. This follows NRedisStack conventions:
            // https://redis.io/docs/latest/develop/clients/dotnet/queryjson/
            // Without this, the search returns no document content.
            var returnFields = new List<FieldName> { new FieldName("$", "json") };
            if (options.ReturnFields?.Count > 0)
            {
                foreach (var field in options.ReturnFields)
                {
                    returnFields.Add(new FieldName(field, field));
                }
            }
            ftQuery.ReturnFields(returnFields.ToArray());

            if (!string.IsNullOrEmpty(options.Language))
            {
                ftQuery.SetLanguage(options.Language);
            }

            var result = await _searchCommands.SearchAsync(indexName, ftQuery);

            // Log search results for debugging
            _logger.LogDebug("FT.SEARCH on '{Index}' with query '{Query}' found {TotalResults} documents",
                indexName, query, result.TotalResults);

            var items = new List<SearchResult<TValue>>();
            foreach (var doc in result.Documents)
            {
                // Extract the key without prefix
                var fullKey = doc.Id;
                var key = fullKey.StartsWith($"{_keyPrefix}:")
                    ? fullKey[(_keyPrefix.Length + 1)..]
                    : fullKey;

                // Get the JSON value using the "json" alias we specified in ReturnFields
                // Per NRedisStack convention: FieldName("$", "json") returns doc["json"]
                var jsonValue = doc["json"];
                if (jsonValue == RedisValue.Null)
                {
                    // Log what fields ARE available to diagnose the issue
                    var availableFields = string.Join(", ", doc.GetProperties().Select(p => p.Key));
                    _logger.LogWarning("Document {DocId} has no 'json' field. Available fields: [{Fields}]",
                        fullKey, availableFields);
                }
                else
                {
                    var value = BannouJson.Deserialize<TValue>(jsonValue.ToString());
                    if (value != null)
                    {
                        items.Add(new SearchResult<TValue>(key, value, doc.Score));
                    }
                }
            }

            _logger.LogDebug("Search '{Query}' on index '{Index}' returned {Count}/{Total} results",
                query, indexName, items.Count, result.TotalResults);

            return new SearchPagedResult<TValue>(items, result.TotalResults, options.Offset, options.Limit);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Search failed for query '{Query}' on index '{Index}'", query, indexName);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string Suggestion, double Score)>> SuggestAsync(
        string indexName,
        string prefix,
        int maxResults = 5,
        bool fuzzy = false,
        CancellationToken cancellationToken = default)
    {

        try
        {
            // Use FT.SUGGET for autocomplete (requires FT.SUGADD to populate)
            // For now, fall back to prefix search
            var query = $"{prefix}*";
            var ftQuery = new Query(query).Limit(0, maxResults);

            var result = await _searchCommands.SearchAsync(indexName, ftQuery);

            var suggestions = new List<(string, double)>();
            foreach (var doc in result.Documents)
            {
                var key = doc.Id;
                if (key.StartsWith($"{_keyPrefix}:"))
                {
                    key = key[(_keyPrefix.Length + 1)..];
                }
                suggestions.Add((key, doc.Score));
            }

            return suggestions;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Suggest failed for prefix '{Prefix}' on index '{Index}'", prefix, indexName);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<SearchIndexInfo?> GetIndexInfoAsync(
        string indexName,
        CancellationToken cancellationToken = default)
    {

        try
        {
            var info = await _searchCommands.InfoAsync(indexName);

            // Extract field names from attributes
            var fieldNames = new List<string>();
            if (info.Attributes != null)
            {
                foreach (var attr in info.Attributes)
                {
                    // Attribute is a dictionary - try to get the field name
                    if (attr.TryGetValue("identifier", out var identifier))
                    {
                        fieldNames.Add(identifier.ToString());
                    }
                    else if (attr.TryGetValue("attribute", out var attrName))
                    {
                        fieldNames.Add(attrName.ToString());
                    }
                }
            }

            return new SearchIndexInfo
            {
                Name = indexName,
                DocumentCount = info.NumDocs,
                TermCount = info.NumTerms,
                MemoryUsageBytes = 0, // Memory usage info not directly available in InfoResult
                IsIndexing = info.Indexing != 0,
                IndexingProgress = info.PercentIndexed,
                Fields = fieldNames
            };
        }
        catch (RedisException ex) when (ex.Message.Contains("Unknown index") || ex.Message.Contains("no such index"))
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListIndexesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexes = await _searchCommands._ListAsync();
            return indexes.Select(i => i.ToString()).ToList();
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to list search indexes");
            throw;
        }
    }

    #endregion

    // ==================== Set Operations ====================

    private string GetSetKey(string key) => $"{_keyPrefix}:set:{key}";

    /// <inheritdoc/>
    public async Task<bool> AddToSetAsync<TItem>(
        string key,
        TItem item,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {

        var setKey = GetSetKey(key);
        var json = BannouJson.Serialize(item);
        var added = await _database.SetAddAsync(setKey, json);

        // Apply TTL if specified
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;
        if (ttl.HasValue)
        {
            await _database.KeyExpireAsync(setKey, ttl);
        }

        _logger.LogDebug("Added item to set '{Key}' in store '{Store}' (new: {IsNew})",
            key, _keyPrefix, added);

        return added;
    }

    /// <inheritdoc/>
    public async Task<long> AddToSetAsync<TItem>(
        string key,
        IEnumerable<TItem> items,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {

        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            return 0;
        }

        var setKey = GetSetKey(key);
        var values = itemList.Select(item => (RedisValue)BannouJson.Serialize(item)).ToArray();
        var added = await _database.SetAddAsync(setKey, values);

        // Apply TTL if specified
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;
        if (ttl.HasValue)
        {
            await _database.KeyExpireAsync(setKey, ttl);
        }

        _logger.LogDebug("Added {Count} items to set '{Key}' in store '{Store}' (new: {Added})",
            itemList.Count, key, _keyPrefix, added);

        return added;
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveFromSetAsync<TItem>(
        string key,
        TItem item,
        CancellationToken cancellationToken = default)
    {

        var setKey = GetSetKey(key);
        var json = BannouJson.Serialize(item);
        var removed = await _database.SetRemoveAsync(setKey, json);

        _logger.LogDebug("Removed item from set '{Key}' in store '{Store}' (existed: {Existed})",
            key, _keyPrefix, removed);

        return removed;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TItem>> GetSetAsync<TItem>(
        string key,
        CancellationToken cancellationToken = default)
    {

        var setKey = GetSetKey(key);
        var members = await _database.SetMembersAsync(setKey);

        if (members.Length == 0)
        {
            _logger.LogDebug("Set '{Key}' is empty or not found in store '{Store}'", key, _keyPrefix);
            return Array.Empty<TItem>();
        }

        var result = new List<TItem>(members.Length);
        foreach (var member in members)
        {
            if (!member.IsNullOrEmpty)
            {
                var item = BannouJson.Deserialize<TItem>(member!);
                if (item != null)
                {
                    result.Add(item);
                }
            }
        }

        _logger.LogDebug("Retrieved {Count} items from set '{Key}' in store '{Store}'",
            result.Count, key, _keyPrefix);

        return result;
    }

    /// <inheritdoc/>
    public async Task<bool> SetContainsAsync<TItem>(
        string key,
        TItem item,
        CancellationToken cancellationToken = default)
    {

        var setKey = GetSetKey(key);
        var json = BannouJson.Serialize(item);
        return await _database.SetContainsAsync(setKey, json);
    }

    /// <inheritdoc/>
    public async Task<long> SetCountAsync(
        string key,
        CancellationToken cancellationToken = default)
    {

        var setKey = GetSetKey(key);
        return await _database.SetLengthAsync(setKey);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteSetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {

        var setKey = GetSetKey(key);
        var deleted = await _database.KeyDeleteAsync(setKey);

        _logger.LogDebug("Deleted set '{Key}' from store '{Store}' (existed: {Existed})",
            key, _keyPrefix, deleted);

        return deleted;
    }

    /// <inheritdoc/>
    public async Task<bool> RefreshSetTtlAsync(
        string key,
        int ttlSeconds,
        CancellationToken cancellationToken = default)
    {

        var setKey = GetSetKey(key);
        var ttl = TimeSpan.FromSeconds(ttlSeconds);
        var updated = await _database.KeyExpireAsync(setKey, ttl);

        _logger.LogDebug("Refreshed TTL on set '{Key}' in store '{Store}' to {Ttl}s (existed: {Existed})",
            key, _keyPrefix, ttlSeconds, updated);

        return updated;
    }

    // ==================== Sorted Set Operations ====================
    // RedisSearchStateStore is focused on full-text search capabilities.
    // Use RedisStateStore directly for sorted set operations.

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">RedisSearchStateStore does not support sorted sets.</exception>
    public Task<bool> SortedSetAddAsync(
        string key,
        string member,
        double score,
        StateOptions? options = null,
        CancellationToken ct = default)
    {
        throw new NotSupportedException("RedisSearchStateStore does not support sorted set operations. Use RedisStateStore.");
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">RedisSearchStateStore does not support sorted sets.</exception>
    public Task<long> SortedSetAddBatchAsync(
        string key,
        IEnumerable<(string member, double score)> entries,
        StateOptions? options = null,
        CancellationToken ct = default)
    {
        throw new NotSupportedException("RedisSearchStateStore does not support sorted set operations. Use RedisStateStore.");
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">RedisSearchStateStore does not support sorted sets.</exception>
    public Task<bool> SortedSetRemoveAsync(
        string key,
        string member,
        CancellationToken ct = default)
    {
        throw new NotSupportedException("RedisSearchStateStore does not support sorted set operations. Use RedisStateStore.");
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">RedisSearchStateStore does not support sorted sets.</exception>
    public Task<double?> SortedSetScoreAsync(
        string key,
        string member,
        CancellationToken ct = default)
    {
        throw new NotSupportedException("RedisSearchStateStore does not support sorted set operations. Use RedisStateStore.");
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">RedisSearchStateStore does not support sorted sets.</exception>
    public Task<long?> SortedSetRankAsync(
        string key,
        string member,
        bool descending = true,
        CancellationToken ct = default)
    {
        throw new NotSupportedException("RedisSearchStateStore does not support sorted set operations. Use RedisStateStore.");
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">RedisSearchStateStore does not support sorted sets.</exception>
    public Task<IReadOnlyList<(string member, double score)>> SortedSetRangeByRankAsync(
        string key,
        long start,
        long stop,
        bool descending = true,
        CancellationToken ct = default)
    {
        throw new NotSupportedException("RedisSearchStateStore does not support sorted set operations. Use RedisStateStore.");
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">RedisSearchStateStore does not support sorted sets.</exception>
    public Task<long> SortedSetCountAsync(
        string key,
        CancellationToken ct = default)
    {
        throw new NotSupportedException("RedisSearchStateStore does not support sorted set operations. Use RedisStateStore.");
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">RedisSearchStateStore does not support sorted sets.</exception>
    public Task<double> SortedSetIncrementAsync(
        string key,
        string member,
        double increment,
        CancellationToken ct = default)
    {
        throw new NotSupportedException("RedisSearchStateStore does not support sorted set operations. Use RedisStateStore.");
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">RedisSearchStateStore does not support sorted sets.</exception>
    public Task<bool> SortedSetDeleteAsync(
        string key,
        CancellationToken ct = default)
    {
        throw new NotSupportedException("RedisSearchStateStore does not support sorted set operations. Use RedisStateStore.");
    }
}
