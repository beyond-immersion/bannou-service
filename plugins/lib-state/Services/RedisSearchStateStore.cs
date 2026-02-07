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
/// Implements ICacheableStateStore for Set and Sorted Set operations.
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

            try
            {
                return BannouJson.Deserialize<TValue>(value.ToString());
            }
            catch (System.Text.Json.JsonException ex)
            {
                // IMPLEMENTATION TENETS: Log data corruption as error for monitoring
                _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - data may be corrupted", key, _keyPrefix);
                return null;
            }
        }
        catch (RedisException ex) when (ex.Message.Contains("WRONGTYPE"))
        {
            // Fall back to string storage for backwards compatibility
            var stringValue = await _database.StringGetAsync(fullKey);
            if (stringValue.IsNullOrEmpty)
            {
                return null;
            }
            try
            {
                return BannouJson.Deserialize<TValue>(stringValue!);
            }
            catch (System.Text.Json.JsonException ex2)
            {
                // IMPLEMENTATION TENETS: Log data corruption as error for monitoring
                _logger.LogError(ex2, "JSON deserialization failed for key '{Key}' in store '{Store}' - data may be corrupted", key, _keyPrefix);
                return null;
            }
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
            try
            {
                return (BannouJson.Deserialize<TValue>(value.ToString()), etag);
            }
            catch (System.Text.Json.JsonException ex)
            {
                // IMPLEMENTATION TENETS: Log data corruption as error for monitoring
                _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - data may be corrupted", key, _keyPrefix);
                return (null, null);
            }
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
            try
            {
                return (BannouJson.Deserialize<TValue>(stringValue!), etag);
            }
            catch (System.Text.Json.JsonException ex2)
            {
                // IMPLEMENTATION TENETS: Log data corruption as error for monitoring
                _logger.LogError(ex2, "JSON deserialization failed for key '{Key}' in store '{Store}' - data may be corrupted", key, _keyPrefix);
                return (null, null);
            }
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
        var json = BannouJson.Serialize(value);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Empty etag means "create new entry if it doesn't exist"
        if (string.IsNullOrEmpty(etag))
        {
            // Use Lua script for atomic create-if-not-exists
            // See Scripts/TryCreate.lua for implementation
            var createResult = await _database.ScriptEvaluateAsync(
                RedisLuaScripts.TryCreate,
                keys: [(RedisKey)fullKey, (RedisKey)metaKey],
                values: [(RedisValue)json, (RedisValue)now.ToString()]);

            var createSuccess = (long)createResult;
            if (createSuccess == 1)
            {
                _logger.LogDebug("Created new key '{Key}' in store '{Store}'", key, _keyPrefix);
                return "1";
            }
            else
            {
                _logger.LogDebug("Key '{Key}' already exists in store '{Store}' (concurrent create conflict)",
                    key, _keyPrefix);
                return null;
            }
        }

        // Non-empty etag means "update existing entry with matching version"
        // Use Lua script for atomic optimistic concurrency
        // See Scripts/TryUpdate.lua for implementation
        var result = await _database.ScriptEvaluateAsync(
            RedisLuaScripts.TryUpdate,
            keys: [(RedisKey)fullKey, (RedisKey)metaKey],
            values: [(RedisValue)etag, (RedisValue)json, (RedisValue)now.ToString()]);

        var newVersion = (long)result;

        if (newVersion == -1)
        {
            _logger.LogDebug("ETag mismatch for key '{Key}' in store '{Store}' (expected: {Expected})",
                key, _keyPrefix, etag);
            return null;
        }

        _logger.LogDebug("Optimistic save succeeded for key '{Key}' in store '{Store}' (version: {Version})",
            key, _keyPrefix, newVersion);
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
                        try
                        {
                            var deserialized = BannouJson.Deserialize<TValue>(jsonValue.ToString());
                            if (deserialized != null)
                            {
                                result[keyList[i]] = deserialized;
                            }
                        }
                        catch (System.Text.Json.JsonException ex)
                        {
                            // IMPLEMENTATION TENETS: Log data corruption as error and skip the item
                            _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - skipping corrupted item", keyList[i], _keyPrefix);
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

    /// <inheritdoc/>
    /// <remarks>
    /// IMPORTANT: This bulk operation is NOT atomic. Each item is saved individually because
    /// Redis does not support cross-key transactions for JSON operations. Partial failures can
    /// occur - some items may be saved while others fail. Callers should handle this by checking
    /// returned keys against input keys or implementing their own retry/rollback logic.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, string>> SaveBulkAsync(
        IEnumerable<KeyValuePair<string, TValue>> items,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = new Dictionary<string, string>();

        // NOTE: Each item saved individually - not atomic. See XML docs above.
        foreach (var (key, value) in itemList)
        {
            var fullKey = GetFullKey(key);
            var metaKey = GetMetaKey(key);
            var json = BannouJson.Serialize(value);

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
                new("updated", now)
            });

            if (ttl.HasValue)
            {
                await _database.KeyExpireAsync(metaKey, ttl.Value);
            }

            result[key] = newVersion.ToString();
        }

        _logger.LogDebug("Bulk save {Count} items to store '{Store}'", itemList.Count, _keyPrefix);
        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlySet<string>> ExistsBulkAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0)
        {
            return new HashSet<string>();
        }

        // Pipeline exists checks for efficiency
        var tasks = keyList.Select(k => _database.KeyExistsAsync(GetFullKey(k))).ToArray();
        var results = await Task.WhenAll(tasks);

        var existing = new HashSet<string>();
        for (var i = 0; i < keyList.Count; i++)
        {
            if (results[i])
            {
                existing.Add(keyList[i]);
            }
        }

        _logger.LogDebug("Bulk exists check {RequestedCount} keys from store '{Store}', found {FoundCount}",
            keyList.Count, _keyPrefix, existing.Count);
        return existing;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteBulkAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0)
        {
            return 0;
        }

        // Delete both value keys and metadata keys
        var allKeys = new List<RedisKey>();
        foreach (var key in keyList)
        {
            allKeys.Add(GetFullKey(key));
            allKeys.Add(GetMetaKey(key));
        }

        var totalDeleted = await _database.KeyDeleteAsync(allKeys.ToArray());
        // Each logical delete is 2 keys (value + meta), return logical count
        var deletedCount = (int)(totalDeleted / 2);

        _logger.LogDebug("Bulk delete {RequestedCount} keys from store '{Store}', deleted {DeletedCount}",
            keyList.Count, _keyPrefix, deletedCount);
        return deletedCount;
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
                    try
                    {
                        var value = BannouJson.Deserialize<TValue>(jsonValue.ToString());
                        if (value != null)
                        {
                            items.Add(new SearchResult<TValue>(key, value, doc.Score));
                        }
                    }
                    catch (System.Text.Json.JsonException ex)
                    {
                        // IMPLEMENTATION TENETS: Log data corruption as error and skip the item
                        _logger.LogError(ex, "JSON deserialization failed for search result '{Key}' in store '{Store}' - skipping corrupted item", key, _keyPrefix);
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
                try
                {
                    var item = BannouJson.Deserialize<TItem>(member!);
                    if (item != null)
                    {
                        result.Add(item);
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    // IMPLEMENTATION TENETS: Log data corruption as error and skip the item
                    _logger.LogError(ex, "JSON deserialization failed for set item in '{Key}' in store '{Store}' - skipping corrupted item", key, _keyPrefix);
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
    // Sorted sets use standard Redis sorted sets with separate key prefixes,
    // independent of the JSON document storage used for TValue.

    private string GetSortedSetKey(string key) => $"{_keyPrefix}:zset:{key}";

    /// <inheritdoc/>
    public async Task<bool> SortedSetAddAsync(
        string key,
        string member,
        double score,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sortedSetKey = GetSortedSetKey(key);
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;

        var added = await _database.SortedSetAddAsync(sortedSetKey, member, score);

        if (ttl.HasValue)
        {
            await _database.KeyExpireAsync(sortedSetKey, ttl);
        }

        _logger.LogDebug("Added member '{Member}' to sorted set '{Key}' with score {Score} (new: {IsNew})",
            member, key, score, added);

        return added;
    }

    /// <inheritdoc/>
    public async Task<long> SortedSetAddBatchAsync(
        string key,
        IEnumerable<(string member, double score)> entries,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var entryList = entries.ToList();
        if (entryList.Count == 0)
        {
            return 0;
        }

        var sortedSetKey = GetSortedSetKey(key);
        var redisEntries = entryList
            .Select(e => new SortedSetEntry(e.member, e.score))
            .ToArray();

        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;

        var added = await _database.SortedSetAddAsync(sortedSetKey, redisEntries);

        if (ttl.HasValue)
        {
            await _database.KeyExpireAsync(sortedSetKey, ttl);
        }

        _logger.LogDebug("Batch added {Count} entries to sorted set '{Key}', {NewCount} new",
            entryList.Count, key, added);

        return added;
    }

    /// <inheritdoc/>
    public async Task<bool> SortedSetRemoveAsync(
        string key,
        string member,
        CancellationToken cancellationToken = default)
    {
        var sortedSetKey = GetSortedSetKey(key);
        var removed = await _database.SortedSetRemoveAsync(sortedSetKey, member);

        _logger.LogDebug("Removed member '{Member}' from sorted set '{Key}' (existed: {Existed})",
            member, key, removed);

        return removed;
    }

    /// <inheritdoc/>
    public async Task<double?> SortedSetScoreAsync(
        string key,
        string member,
        CancellationToken cancellationToken = default)
    {
        var sortedSetKey = GetSortedSetKey(key);
        return await _database.SortedSetScoreAsync(sortedSetKey, member);
    }

    /// <inheritdoc/>
    public async Task<long?> SortedSetRankAsync(
        string key,
        string member,
        bool descending = true,
        CancellationToken cancellationToken = default)
    {
        var sortedSetKey = GetSortedSetKey(key);
        return descending
            ? await _database.SortedSetRankAsync(sortedSetKey, member, Order.Descending)
            : await _database.SortedSetRankAsync(sortedSetKey, member, Order.Ascending);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string member, double score)>> SortedSetRangeByRankAsync(
        string key,
        long start,
        long stop,
        bool descending = true,
        CancellationToken cancellationToken = default)
    {
        var sortedSetKey = GetSortedSetKey(key);

        var entries = await _database.SortedSetRangeByRankWithScoresAsync(
            sortedSetKey,
            start,
            stop,
            descending ? Order.Descending : Order.Ascending);

        var result = entries
            .Select(e => (member: e.Element.ToString(), score: e.Score))
            .ToList();

        _logger.LogDebug("Retrieved {Count} entries from sorted set '{Key}' (range: {Start}-{Stop}, descending: {Descending})",
            result.Count, key, start, stop, descending);

        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string member, double score)>> SortedSetRangeByScoreAsync(
        string key,
        double minScore,
        double maxScore,
        int offset = 0,
        int count = -1,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        var sortedSetKey = GetSortedSetKey(key);

        var entries = descending
            ? await _database.SortedSetRangeByScoreWithScoresAsync(
                sortedSetKey,
                minScore,
                maxScore,
                Exclude.None,
                Order.Descending,
                offset,
                count)
            : await _database.SortedSetRangeByScoreWithScoresAsync(
                sortedSetKey,
                minScore,
                maxScore,
                Exclude.None,
                Order.Ascending,
                offset,
                count);

        var result = entries
            .Select(e => (member: e.Element.ToString(), score: e.Score))
            .ToList();

        _logger.LogDebug(
            "Retrieved {Count} entries from sorted set '{Key}' by score (min: {Min}, max: {Max}, offset: {Offset}, count: {RequestedCount}, descending: {Descending})",
            result.Count, key, minScore, maxScore, offset, count, descending);

        return result;
    }

    /// <inheritdoc/>
    public async Task<long> SortedSetCountAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var sortedSetKey = GetSortedSetKey(key);
        return await _database.SortedSetLengthAsync(sortedSetKey);
    }

    /// <inheritdoc/>
    public async Task<double> SortedSetIncrementAsync(
        string key,
        string member,
        double increment,
        CancellationToken cancellationToken = default)
    {
        var sortedSetKey = GetSortedSetKey(key);
        var newScore = await _database.SortedSetIncrementAsync(sortedSetKey, member, increment);

        _logger.LogDebug("Incremented member '{Member}' in sorted set '{Key}' by {Increment} (new score: {NewScore})",
            member, key, increment, newScore);

        return newScore;
    }

    /// <inheritdoc/>
    public async Task<bool> SortedSetDeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var sortedSetKey = GetSortedSetKey(key);
        var deleted = await _database.KeyDeleteAsync(sortedSetKey);

        _logger.LogDebug("Deleted sorted set '{Key}' from store '{Store}' (existed: {Existed})",
            key, _keyPrefix, deleted);

        return deleted;
    }

    // ==================== Atomic Counter Operations ====================
    // Counter operations use standard Redis strings (not JSON), so they work with RedisSearch stores.

    private string GetCounterKey(string key) => $"{_keyPrefix}:counter:{key}";

    /// <inheritdoc/>
    public async Task<long> IncrementAsync(
        string key,
        long increment = 1,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var counterKey = GetCounterKey(key);
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;

        var newValue = await _database.StringIncrementAsync(counterKey, increment);

        if (ttl.HasValue)
        {
            await _database.KeyExpireAsync(counterKey, ttl);
        }

        _logger.LogDebug("Incremented counter '{Key}' in store '{Store}' by {Increment} to {Value}",
            key, _keyPrefix, increment, newValue);

        return newValue;
    }

    /// <inheritdoc/>
    public async Task<long> DecrementAsync(
        string key,
        long decrement = 1,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var counterKey = GetCounterKey(key);
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;

        var newValue = await _database.StringDecrementAsync(counterKey, decrement);

        if (ttl.HasValue)
        {
            await _database.KeyExpireAsync(counterKey, ttl);
        }

        _logger.LogDebug("Decremented counter '{Key}' in store '{Store}' by {Decrement} to {Value}",
            key, _keyPrefix, decrement, newValue);

        return newValue;
    }

    /// <inheritdoc/>
    public async Task<long?> GetCounterAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var counterKey = GetCounterKey(key);
        var value = await _database.StringGetAsync(counterKey);

        if (value.IsNullOrEmpty)
        {
            _logger.LogDebug("Counter '{Key}' not found in store '{Store}'", key, _keyPrefix);
            return null;
        }

        if (long.TryParse(value, out var result))
        {
            return result;
        }

        _logger.LogWarning("Counter '{Key}' in store '{Store}' has non-numeric value: {Value}",
            key, _keyPrefix, value);
        return null;
    }

    /// <inheritdoc/>
    public async Task SetCounterAsync(
        string key,
        long value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var counterKey = GetCounterKey(key);
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;

        if (ttl.HasValue)
        {
            await _database.StringSetAsync(counterKey, value, ttl.Value);
        }
        else
        {
            await _database.StringSetAsync(counterKey, value);
        }

        _logger.LogDebug("Set counter '{Key}' in store '{Store}' to {Value}",
            key, _keyPrefix, value);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteCounterAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var counterKey = GetCounterKey(key);
        var deleted = await _database.KeyDeleteAsync(counterKey);

        _logger.LogDebug("Deleted counter '{Key}' from store '{Store}' (existed: {Existed})",
            key, _keyPrefix, deleted);

        return deleted;
    }

    // ==================== Hash Operations ====================
    // Hash operations use standard Redis hashes (not JSON), so they work with RedisSearch stores.

    private string GetHashKey(string key) => $"{_keyPrefix}:hash:{key}";

    /// <inheritdoc/>
    public async Task<TField?> HashGetAsync<TField>(
        string key,
        string field,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);
        var value = await _database.HashGetAsync(hashKey, field);

        if (value.IsNullOrEmpty)
        {
            _logger.LogDebug("Hash field '{Field}' not found in hash '{Key}' in store '{Store}'",
                field, key, _keyPrefix);
            return default;
        }

        try
        {
            return BannouJson.Deserialize<TField>(value!);
        }
        catch (System.Text.Json.JsonException ex)
        {
            // IMPLEMENTATION TENETS: Log data corruption as error for monitoring
            _logger.LogError(ex, "JSON deserialization failed for hash field '{Field}' in hash '{Key}' in store '{Store}' - data may be corrupted", field, key, _keyPrefix);
            return default;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> HashSetAsync<TField>(
        string key,
        string field,
        TField value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);
        var json = BannouJson.Serialize(value);
        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;

        var isNew = await _database.HashSetAsync(hashKey, field, json);

        if (ttl.HasValue)
        {
            await _database.KeyExpireAsync(hashKey, ttl);
        }

        _logger.LogDebug("Set hash field '{Field}' in hash '{Key}' in store '{Store}' (new: {IsNew})",
            field, key, _keyPrefix, isNew);

        return isNew;
    }

    /// <inheritdoc/>
    public async Task HashSetManyAsync<TField>(
        string key,
        IEnumerable<KeyValuePair<string, TField>> fields,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var fieldList = fields.ToList();
        if (fieldList.Count == 0)
        {
            return;
        }

        var hashKey = GetHashKey(key);
        var entries = fieldList
            .Select(f => new HashEntry(f.Key, BannouJson.Serialize(f.Value)))
            .ToArray();

        var ttl = options?.Ttl != null ? TimeSpan.FromSeconds(options.Ttl.Value) : _defaultTtl;

        await _database.HashSetAsync(hashKey, entries);

        if (ttl.HasValue)
        {
            await _database.KeyExpireAsync(hashKey, ttl);
        }

        _logger.LogDebug("Set {Count} hash fields in hash '{Key}' in store '{Store}'",
            fieldList.Count, key, _keyPrefix);
    }

    /// <inheritdoc/>
    public async Task<bool> HashDeleteAsync(
        string key,
        string field,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);
        var deleted = await _database.HashDeleteAsync(hashKey, field);

        _logger.LogDebug("Deleted hash field '{Field}' from hash '{Key}' in store '{Store}' (existed: {Existed})",
            field, key, _keyPrefix, deleted);

        return deleted;
    }

    /// <inheritdoc/>
    public async Task<bool> HashExistsAsync(
        string key,
        string field,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);
        return await _database.HashExistsAsync(hashKey, field);
    }

    /// <inheritdoc/>
    public async Task<long> HashIncrementAsync(
        string key,
        string field,
        long increment = 1,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);

        var newValue = await _database.HashIncrementAsync(hashKey, field, increment);

        _logger.LogDebug("Incremented hash field '{Field}' in hash '{Key}' in store '{Store}' by {Increment} to {Value}",
            field, key, _keyPrefix, increment, newValue);

        return newValue;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, TField>> HashGetAllAsync<TField>(
        string key,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);
        var entries = await _database.HashGetAllAsync(hashKey);

        var result = new Dictionary<string, TField>();
        foreach (var entry in entries)
        {
            try
            {
                var fieldValue = BannouJson.Deserialize<TField>(entry.Value!);
                if (fieldValue != null)
                {
                    result[entry.Name!] = fieldValue;
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                // IMPLEMENTATION TENETS: Log data corruption as error and skip the field
                _logger.LogError(ex, "JSON deserialization failed for hash field '{Field}' in hash '{Key}' in store '{Store}' - skipping corrupted field", entry.Name, key, _keyPrefix);
            }
        }

        _logger.LogDebug("Retrieved {Count} fields from hash '{Key}' in store '{Store}'",
            result.Count, key, _keyPrefix);

        return result;
    }

    /// <inheritdoc/>
    public async Task<long> HashCountAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);
        return await _database.HashLengthAsync(hashKey);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteHashAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);
        var deleted = await _database.KeyDeleteAsync(hashKey);

        _logger.LogDebug("Deleted hash '{Key}' from store '{Store}' (existed: {Existed})",
            key, _keyPrefix, deleted);

        return deleted;
    }

    /// <inheritdoc/>
    public async Task<bool> RefreshHashTtlAsync(
        string key,
        int ttlSeconds,
        CancellationToken cancellationToken = default)
    {
        var hashKey = GetHashKey(key);
        var updated = await _database.KeyExpireAsync(hashKey, TimeSpan.FromSeconds(ttlSeconds));

        _logger.LogDebug("Refreshed TTL on hash '{Key}' in store '{Store}' to {Ttl}s (existed: {Existed})",
            key, _keyPrefix, ttlSeconds, updated);

        return updated;
    }
}
