#nullable enable

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
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _keyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
        _defaultTtl = defaultTtl;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _searchCommands = _database.FT();
        _jsonCommands = _database.JSON();
    }

    private string GetFullKey(string key) => $"{_keyPrefix}:{key}";
    private string GetMetaKey(string key) => $"{_keyPrefix}:{key}:meta";

    #region IStateStore<TValue> Implementation

    /// <inheritdoc/>
    public async Task<TValue?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

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
        ArgumentNullException.ThrowIfNull(key);

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
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

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
    public async Task<bool> TrySaveAsync(
        string key,
        TValue value,
        string etag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(etag);

        var fullKey = GetFullKey(key);
        var metaKey = GetMetaKey(key);

        // Check current version
        var currentVersion = await _database.HashGetAsync(metaKey, "version");
        if (currentVersion.ToString() != etag)
        {
            _logger.LogDebug("ETag mismatch for key '{Key}' in store '{Store}' (expected: {Expected}, actual: {Actual})",
                key, _keyPrefix, etag, currentVersion.ToString());
            return false;
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

        _logger.LogDebug("Optimistic save succeeded for key '{Key}' in store '{Store}'", key, _keyPrefix);
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

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
        ArgumentNullException.ThrowIfNull(key);

        var fullKey = GetFullKey(key);
        return await _database.KeyExistsAsync(fullKey);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, TValue>> GetBulkAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

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
        ArgumentNullException.ThrowIfNull(indexName);
        ArgumentNullException.ThrowIfNull(schema);

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
            catch (RedisException ex) when (ex.Message.Contains("Unknown index"))
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
                .On(IndexDataType.JSON)
                .Prefix($"{_keyPrefix}:");

            if (!string.IsNullOrEmpty(options.Prefix))
            {
                ftParams.Prefix(options.Prefix);
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
        ArgumentNullException.ThrowIfNull(indexName);

        try
        {
            await _searchCommands.DropIndexAsync(indexName, deleteDocuments);
            _logger.LogInformation("Dropped search index '{Index}' (deleteDocuments: {DeleteDocs})",
                indexName, deleteDocuments);
            return true;
        }
        catch (RedisException ex) when (ex.Message.Contains("Unknown index"))
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
        ArgumentNullException.ThrowIfNull(indexName);
        ArgumentNullException.ThrowIfNull(query);

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

            if (options.ReturnFields?.Count > 0)
            {
                ftQuery.ReturnFields(options.ReturnFields.ToArray());
            }

            if (!string.IsNullOrEmpty(options.Language))
            {
                ftQuery.SetLanguage(options.Language);
            }

            var result = await _searchCommands.SearchAsync(indexName, ftQuery);

            var items = new List<SearchResult<TValue>>();
            foreach (var doc in result.Documents)
            {
                // Extract the key without prefix
                var fullKey = doc.Id;
                var key = fullKey.StartsWith($"{_keyPrefix}:")
                    ? fullKey[(_keyPrefix.Length + 1)..]
                    : fullKey;

                // Get the JSON value
                var jsonValue = doc["$"];
                if (jsonValue != RedisValue.Null)
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
        ArgumentNullException.ThrowIfNull(indexName);
        ArgumentNullException.ThrowIfNull(prefix);

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
        ArgumentNullException.ThrowIfNull(indexName);

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
        catch (RedisException ex) when (ex.Message.Contains("Unknown index"))
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
}
