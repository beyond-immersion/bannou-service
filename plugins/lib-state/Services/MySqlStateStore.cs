#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.State.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// MySQL-backed state store for durable/queryable data.
/// Uses EF Core for query support and raw SQL for efficient JSON queries.
/// Creates a new DbContext per operation for thread-safety with concurrent requests.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public sealed class MySqlStateStore<TValue> : IJsonQueryableStateStore<TValue>
    where TValue : class
{
    private readonly DbContextOptions<StateDbContext> _options;
    private readonly string _storeName;
    private readonly int _inMemoryFallbackLimit;
    private readonly ILogger<MySqlStateStore<TValue>> _logger;

    /// <summary>
    /// Creates a new MySQL state store.
    /// </summary>
    /// <param name="options">EF Core database context options for creating per-operation contexts.</param>
    /// <param name="storeName">Name of this state store.</param>
    /// <param name="inMemoryFallbackLimit">Maximum entries to load when falling back to in-memory filtering.</param>
    /// <param name="logger">Logger instance.</param>
    public MySqlStateStore(
        DbContextOptions<StateDbContext> options,
        string storeName,
        int inMemoryFallbackLimit,
        ILogger<MySqlStateStore<TValue>> logger)
    {
        _options = options;
        _storeName = storeName;
        _inMemoryFallbackLimit = inMemoryFallbackLimit;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new DbContext for an operation.
    /// Each operation gets its own context for thread-safety.
    /// </summary>
    private StateDbContext CreateContext() => new StateDbContext(_options);

    /// <summary>
    /// Generates an ETag from key + JSON content.
    /// IMPLEMENTATION TENETS: Key is included to prevent cross-key ETag collisions
    /// where different keys with identical content would otherwise match.
    /// </summary>
    private static string GenerateETag(string key, string json)
    {
        // Include key in hash to prevent cross-key collisions
        var input = $"{key}:{json}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash)[..12]; // Short ETag
    }

    /// <inheritdoc/>
    public async Task<TValue?> GetAsync(string key, CancellationToken cancellationToken = default)
    {

        using var context = CreateContext();
        var entry = await context.StateEntries
            .AsNoTracking()
            .Where(e => e.StoreName == _storeName && e.Key == key)
            .FirstOrDefaultAsync(cancellationToken);

        if (entry == null)
        {
            _logger.LogDebug("Key '{Key}' not found in store '{Store}'", key, _storeName);
            return null;
        }

        return BannouJson.Deserialize<TValue>(entry.ValueJson);
    }

    /// <inheritdoc/>
    public async Task<(TValue? Value, string? ETag)> GetWithETagAsync(
        string key,
        CancellationToken cancellationToken = default)
    {

        using var context = CreateContext();
        var entry = await context.StateEntries
            .AsNoTracking()
            .Where(e => e.StoreName == _storeName && e.Key == key)
            .FirstOrDefaultAsync(cancellationToken);

        if (entry == null)
        {
            return (null, null);
        }

        return (BannouJson.Deserialize<TValue>(entry.ValueJson), entry.ETag);
    }

    /// <inheritdoc/>
    public async Task<string> SaveAsync(
        string key,
        TValue value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {

        var json = BannouJson.Serialize(value);
        var etag = GenerateETag(key, json);
        var now = DateTimeOffset.UtcNow;

        using var context = CreateContext();
        var existing = await context.StateEntries
            .Where(e => e.StoreName == _storeName && e.Key == key)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != null)
        {
            existing.ValueJson = json;
            existing.ETag = etag;
            existing.UpdatedAt = now;
            existing.Version++;
        }
        else
        {
            context.StateEntries.Add(new StateEntry
            {
                StoreName = _storeName,
                Key = key,
                ValueJson = json,
                ETag = etag,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Saved key '{Key}' in store '{Store}' (etag: {ETag})",
            key, _storeName, etag);

        return etag;
    }

    /// <inheritdoc/>
    public async Task<string?> TrySaveAsync(
        string key,
        TValue value,
        string etag,
        CancellationToken cancellationToken = default)
    {
        using var context = CreateContext();
        var existing = await context.StateEntries
            .Where(e => e.StoreName == _storeName && e.Key == key)
            .FirstOrDefaultAsync(cancellationToken);

        var json = BannouJson.Serialize(value);
        var newEtag = GenerateETag(key, json);
        var now = DateTimeOffset.UtcNow;

        // Empty etag means "create new entry if it doesn't exist"
        if (string.IsNullOrEmpty(etag))
        {
            if (existing != null)
            {
                _logger.LogDebug("Key '{Key}' already exists in store '{Store}' but empty etag provided (concurrent create)",
                    key, _storeName);
                return null;
            }

            context.StateEntries.Add(new StateEntry
            {
                StoreName = _storeName,
                Key = key,
                ValueJson = json,
                ETag = newEtag,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now
            });

            try
            {
                await context.SaveChangesAsync(cancellationToken);
                _logger.LogDebug("Created new key '{Key}' in store '{Store}'", key, _storeName);
                return newEtag;
            }
            catch (DbUpdateException)
            {
                // Concurrent create - another instance inserted with same PK
                _logger.LogDebug("Concurrent create conflict for key '{Key}' in store '{Store}'", key, _storeName);
                return null;
            }
        }

        // Non-empty etag means "update existing entry with matching etag"
        if (existing == null || existing.ETag != etag)
        {
            _logger.LogDebug("ETag mismatch for key '{Key}' in store '{Store}' (expected: {Expected}, actual: {Actual})",
                key, _storeName, etag, existing?.ETag ?? "(not found)");
            return null;
        }

        existing.ValueJson = json;
        existing.ETag = newEtag;
        existing.UpdatedAt = now;
        existing.Version++;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Optimistic save succeeded for key '{Key}' in store '{Store}'", key, _storeName);
            return newEtag;
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogDebug("Optimistic save failed (concurrent modification) for key '{Key}' in store '{Store}'",
                key, _storeName);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {

        using var context = CreateContext();
        var deleted = await context.StateEntries
            .Where(e => e.StoreName == _storeName && e.Key == key)
            .ExecuteDeleteAsync(cancellationToken);

        _logger.LogDebug("Deleted key '{Key}' from store '{Store}' (existed: {Existed})",
            key, _storeName, deleted > 0);

        return deleted > 0;
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {

        using var context = CreateContext();
        return await context.StateEntries
            .AsNoTracking()
            .AnyAsync(e => e.StoreName == _storeName && e.Key == key, cancellationToken);
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

        using var context = CreateContext();
        var entries = await context.StateEntries
            .AsNoTracking()
            .Where(e => e.StoreName == _storeName && keyList.Contains(e.Key))
            .ToListAsync(cancellationToken);

        var result = new Dictionary<string, TValue>();
        foreach (var entry in entries)
        {
            var deserialized = BannouJson.Deserialize<TValue>(entry.ValueJson);
            if (deserialized != null)
            {
                result[entry.Key] = deserialized;
            }
        }

        _logger.LogDebug("Bulk get {RequestedCount} keys from store '{Store}', found {FoundCount}",
            keyList.Count, _storeName, result.Count);

        return result;
    }

    /// <inheritdoc/>
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

        using var context = CreateContext();
        var keys = itemList.Select(i => i.Key).ToList();
        var existing = await context.StateEntries
            .Where(e => e.StoreName == _storeName && keys.Contains(e.Key))
            .ToDictionaryAsync(e => e.Key, cancellationToken);

        var result = new Dictionary<string, string>();
        var now = DateTimeOffset.UtcNow;

        foreach (var (key, value) in itemList)
        {
            var json = BannouJson.Serialize(value);
            var etag = GenerateETag(key, json);

            if (existing.TryGetValue(key, out var entry))
            {
                entry.ValueJson = json;
                entry.Version++;
                entry.ETag = etag;
                entry.UpdatedAt = now;
            }
            else
            {
                context.StateEntries.Add(new StateEntry
                {
                    StoreName = _storeName,
                    Key = key,
                    ValueJson = json,
                    Version = 1,
                    ETag = etag,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            result[key] = etag;
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Bulk save {Count} items to store '{Store}'", itemList.Count, _storeName);
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

        using var context = CreateContext();
        var existing = await context.StateEntries
            .AsNoTracking()
            .Where(e => e.StoreName == _storeName && keyList.Contains(e.Key))
            .Select(e => e.Key)
            .ToListAsync(cancellationToken);

        _logger.LogDebug("Bulk exists check {RequestedCount} keys from store '{Store}', found {FoundCount}",
            keyList.Count, _storeName, existing.Count);
        return existing.ToHashSet();
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

        using var context = CreateContext();
        var deletedCount = await context.StateEntries
            .Where(e => e.StoreName == _storeName && keyList.Contains(e.Key))
            .ExecuteDeleteAsync(cancellationToken);

        _logger.LogDebug("Bulk delete {RequestedCount} keys from store '{Store}', deleted {DeletedCount}",
            keyList.Count, _storeName, deletedCount);
        return deletedCount;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TValue>> QueryAsync(
        Expression<Func<TValue, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        // IMPLEMENTATION TENETS: Try SQL-level filtering first to avoid loading entire store
        // into memory. Falls back to in-memory for complex expressions that can't be translated.
        if (TryConvertExpressionToConditions(predicate, out var conditions))
        {
            _logger.LogDebug(
                "QueryAsync using SQL-level filtering with {ConditionCount} conditions for store '{Store}'",
                conditions.Count, _storeName);

            var (whereClauses, parameters) = BuildWhereClause(conditions);

            var sql = $@"
                SELECT `StoreName`, `Key`, `ValueJson`, `ETag`, `CreatedAt`, `UpdatedAt`, `Version`
                FROM `state_entries`
                WHERE `StoreName` = @p0
                {(whereClauses.Length > 0 ? $"AND {whereClauses}" : "")}";

            var allParams = new List<object?> { _storeName };
            allParams.AddRange(parameters);

            using var context = CreateContext();
            var entries = await context.StateEntries
                .FromSqlRaw(sql, allParams.ToArray())
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var results = new List<TValue>();
            foreach (var entry in entries)
            {
                var value = BannouJson.Deserialize<TValue>(entry.ValueJson);
                if (value != null)
                {
                    results.Add(value);
                }
                else
                {
                    _logger.LogError(
                        "Failed to deserialize entry {Key} in store '{Store}' - data corruption or schema mismatch detected",
                        entry.Key, _storeName);
                }
            }

            _logger.LogDebug("SQL-level query on store '{Store}' returned {Count} results", _storeName, results.Count);
            return results;
        }

        // Fallback to in-memory filtering for complex expressions
        _logger.LogWarning(
            "QueryAsync falling back to in-memory filtering for store '{Store}' - " +
            "expression could not be translated to SQL. Consider using JsonQueryAsync for large datasets.",
            _storeName);

        using var fallbackContext = CreateContext();
        var allEntries = await fallbackContext.StateEntries
            .AsNoTracking()
            .Where(e => e.StoreName == _storeName)
            .ToListAsync(cancellationToken);

        // Prevent OOM by enforcing configurable limit on in-memory fallback
        if (allEntries.Count > _inMemoryFallbackLimit)
        {
            throw new InvalidOperationException(
                $"QueryAsync in-memory fallback would load {allEntries.Count} entries from store '{_storeName}' " +
                $"(limit: {_inMemoryFallbackLimit}). Use JsonQueryAsync with explicit conditions for large datasets, " +
                "or simplify the predicate to enable SQL translation.");
        }

        var deserializedValues = new List<TValue>();
        foreach (var entry in allEntries)
        {
            var value = BannouJson.Deserialize<TValue>(entry.ValueJson);
            if (value == null)
            {
                _logger.LogError(
                    "Failed to deserialize entry {Key} in store '{Store}' - data corruption or schema mismatch detected",
                    entry.Key, _storeName);
                continue;
            }
            deserializedValues.Add(value);
        }

        var values = deserializedValues
            .AsQueryable()
            .Where(predicate)
            .ToList();

        _logger.LogDebug("In-memory query on store '{Store}' returned {Count} results", _storeName, values.Count);

        return values;
    }

    /// <inheritdoc/>
    public async Task<PagedResult<TValue>> QueryPagedAsync(
        Expression<Func<TValue, bool>>? predicate,
        int page,
        int pageSize,
        Expression<Func<TValue, object>>? orderBy = null,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));

        // IMPLEMENTATION TENETS: Try SQL-level filtering when possible
        // Check if we can translate both predicate and orderBy to SQL
        IReadOnlyList<QueryCondition> conditions = Array.Empty<QueryCondition>();
        var canTranslatePredicate = predicate == null || TryConvertExpressionToConditions(predicate, out conditions);

        string? sortPath = null;
        var canTranslateOrderBy = orderBy == null ||
            TryGetMemberPath(orderBy.Body, orderBy.Parameters[0], out sortPath);

        if (canTranslatePredicate && canTranslateOrderBy)
        {
            _logger.LogDebug(
                "QueryPagedAsync using SQL-level filtering with {ConditionCount} conditions for store '{Store}'",
                conditions.Count, _storeName);

            var (whereClauses, parameters) = BuildWhereClause(conditions);

            // Build ORDER BY clause
            var orderByClause = "ORDER BY `UpdatedAt` DESC"; // Default ordering
            if (sortPath != null)
            {
                var escapedPath = EscapeJsonPath(sortPath);
                var direction = descending ? "DESC" : "ASC";
                orderByClause = $"ORDER BY JSON_UNQUOTE(JSON_EXTRACT(`ValueJson`, '{escapedPath}')) {direction}";
            }

            // Count query
            var countSql = $@"
                SELECT COUNT(*) AS Value
                FROM `state_entries`
                WHERE `StoreName` = @p0
                {(whereClauses.Length > 0 ? $"AND {whereClauses}" : "")}";

            var allParams = new List<object?> { _storeName };
            allParams.AddRange(parameters);

            using var context = CreateContext();
            var totalCount = await context.Database
                .SqlQueryRaw<long>(countSql, allParams.Where(p => p != null).ToArray()!)
                .FirstOrDefaultAsync(cancellationToken);

            // Data query with pagination
            var dataSql = $@"
                SELECT `StoreName`, `Key`, `ValueJson`, `ETag`, `CreatedAt`, `UpdatedAt`, `Version`
                FROM `state_entries`
                WHERE `StoreName` = @p0
                {(whereClauses.Length > 0 ? $"AND {whereClauses}" : "")}
                {orderByClause}
                LIMIT @pLimit OFFSET @pOffset";

            var dataParams = new List<object?> { _storeName };
            dataParams.AddRange(parameters);

            var paramIndex = dataParams.Count;
            dataSql = dataSql.Replace("@pLimit", $"@p{paramIndex}");
            dataParams.Add(pageSize);
            paramIndex++;
            dataSql = dataSql.Replace("@pOffset", $"@p{paramIndex}");
            dataParams.Add(page * pageSize);

            var entries = await context.StateEntries
                .FromSqlRaw(dataSql, dataParams.ToArray())
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var items = new List<TValue>();
            foreach (var entry in entries)
            {
                var value = BannouJson.Deserialize<TValue>(entry.ValueJson);
                if (value != null)
                {
                    items.Add(value);
                }
                else
                {
                    _logger.LogError(
                        "Failed to deserialize entry {Key} in store '{Store}' - data corruption or schema mismatch detected",
                        entry.Key, _storeName);
                }
            }

            _logger.LogDebug("SQL-level paged query on store '{Store}' returned page {Page} with {Count} items (total: {Total})",
                _storeName, page, items.Count, totalCount);

            return new PagedResult<TValue>(items, (int)totalCount, page, pageSize);
        }

        // Fallback to in-memory filtering
        _logger.LogWarning(
            "QueryPagedAsync falling back to in-memory filtering for store '{Store}' - " +
            "expression could not be translated to SQL.",
            _storeName);

        using var fallbackContext = CreateContext();
        var allEntries = await fallbackContext.StateEntries
            .AsNoTracking()
            .Where(e => e.StoreName == _storeName)
            .ToListAsync(cancellationToken);

        // Prevent OOM by enforcing configurable limit on in-memory fallback
        if (allEntries.Count > _inMemoryFallbackLimit)
        {
            throw new InvalidOperationException(
                $"QueryPagedAsync in-memory fallback would load {allEntries.Count} entries from store '{_storeName}' " +
                $"(limit: {_inMemoryFallbackLimit}). Use JsonQueryPagedAsync with explicit conditions for large datasets, " +
                "or simplify the predicate/orderBy to enable SQL translation.");
        }

        var deserializedValues = new List<TValue>();
        foreach (var entry in allEntries)
        {
            var value = BannouJson.Deserialize<TValue>(entry.ValueJson);
            if (value == null)
            {
                _logger.LogError(
                    "Failed to deserialize entry {Key} in store '{Store}' - data corruption or schema mismatch detected",
                    entry.Key, _storeName);
                continue;
            }
            deserializedValues.Add(value);
        }

        var query = deserializedValues.AsQueryable();

        // Apply filter
        if (predicate != null)
        {
            query = query.Where(predicate);
        }

        // Get total count before pagination
        var inMemoryTotalCount = query.Count();

        // Apply ordering
        if (orderBy != null)
        {
            query = descending
                ? query.OrderByDescending(orderBy)
                : query.OrderBy(orderBy);
        }

        // Apply pagination
        var inMemoryItems = query
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToList();

        _logger.LogDebug("In-memory paged query on store '{Store}' returned page {Page} with {Count} items (total: {Total})",
            _storeName, page, inMemoryItems.Count, inMemoryTotalCount);

        return new PagedResult<TValue>(inMemoryItems, inMemoryTotalCount, page, pageSize);
    }

    /// <inheritdoc/>
    public async Task<long> CountAsync(
        Expression<Func<TValue, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        using var context = CreateContext();

        if (predicate == null)
        {
            // Fast path: just count entries in database
            return await context.StateEntries
                .AsNoTracking()
                .Where(e => e.StoreName == _storeName)
                .LongCountAsync(cancellationToken);
        }

        // IMPLEMENTATION TENETS: Try SQL-level counting first
        if (TryConvertExpressionToConditions(predicate, out var conditions))
        {
            _logger.LogDebug(
                "CountAsync using SQL-level filtering with {ConditionCount} conditions for store '{Store}'",
                conditions.Count, _storeName);

            var (whereClauses, parameters) = BuildWhereClause(conditions);

            // EF Core 8 SqlQueryRaw<T> requires column named 'Value'
            var sql = $@"
                SELECT COUNT(*) AS Value
                FROM `state_entries`
                WHERE `StoreName` = @p0
                {(whereClauses.Length > 0 ? $"AND {whereClauses}" : "")}";

            var allParams = new List<object?> { _storeName };
            allParams.AddRange(parameters);

            var count = await context.Database
                .SqlQueryRaw<long>(sql, allParams.Where(p => p != null).ToArray()!)
                .FirstOrDefaultAsync(cancellationToken);

            return count;
        }

        // Fallback: load, deserialize, and filter
        _logger.LogWarning(
            "CountAsync falling back to in-memory filtering for store '{Store}' - " +
            "expression could not be translated to SQL.",
            _storeName);

        var entries = await context.StateEntries
            .AsNoTracking()
            .Where(e => e.StoreName == _storeName)
            .ToListAsync(cancellationToken);

        var deserializedValues = new List<TValue>();
        foreach (var entry in entries)
        {
            var value = BannouJson.Deserialize<TValue>(entry.ValueJson);
            if (value == null)
            {
                _logger.LogError(
                    "Failed to deserialize entry {Key} in store '{Store}' - data corruption or schema mismatch detected",
                    entry.Key, _storeName);
                continue;
            }
            deserializedValues.Add(value);
        }

        var filteredCount = deserializedValues
            .AsQueryable()
            .Where(predicate)
            .LongCount();

        return filteredCount;
    }

    #region IJsonQueryableStateStore Implementation

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JsonQueryResult<TValue>>> JsonQueryAsync(
        IReadOnlyList<QueryCondition> conditions,
        CancellationToken cancellationToken = default)
    {

        var (whereClauses, parameters) = BuildWhereClause(conditions);

        var sql = $@"
            SELECT `Key`, `ValueJson`
            FROM `state_entries`
            WHERE `StoreName` = @p0
            {(whereClauses.Length > 0 ? $"AND {whereClauses}" : "")}";

        var allParams = new List<object?> { _storeName };
        allParams.AddRange(parameters);

        using var context = CreateContext();
        var entries = await context.StateEntries
            .FromSqlRaw(sql, allParams.ToArray())
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var results = new List<JsonQueryResult<TValue>>();
        foreach (var entry in entries)
        {
            var value = BannouJson.Deserialize<TValue>(entry.ValueJson);
            if (value != null)
            {
                results.Add(new JsonQueryResult<TValue>(entry.Key, value));
            }
        }

        _logger.LogDebug("JSON query on store '{Store}' with {ConditionCount} conditions returned {Count} results",
            _storeName, conditions.Count, results.Count);

        return results;
    }

    /// <inheritdoc/>
    public async Task<JsonPagedResult<TValue>> JsonQueryPagedAsync(
        IReadOnlyList<QueryCondition>? conditions,
        int offset,
        int limit,
        JsonSortSpec? sortBy = null,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));

        var (whereClauses, parameters) = BuildWhereClause(conditions ?? Array.Empty<QueryCondition>());

        // Build ORDER BY clause
        var orderBy = "ORDER BY `UpdatedAt` DESC"; // Default ordering
        if (sortBy != null)
        {
            var sortPath = EscapeJsonPath(sortBy.Path);
            var direction = sortBy.Descending ? "DESC" : "ASC";
            orderBy = $"ORDER BY JSON_UNQUOTE(JSON_EXTRACT(`ValueJson`, '{sortPath}')) {direction}";
        }

        // Count query - EF Core 8 SqlQueryRaw<T> requires column named 'Value'
        var countSql = $@"
            SELECT COUNT(*) AS Value
            FROM `state_entries`
            WHERE `StoreName` = @p0
            {(whereClauses.Length > 0 ? $"AND {whereClauses}" : "")}";

        var allParams = new List<object?> { _storeName };
        allParams.AddRange(parameters);

        using var context = CreateContext();
        var totalCount = await context.Database
            .SqlQueryRaw<long>(countSql, allParams.Where(p => p != null).ToArray()!)
            .FirstOrDefaultAsync(cancellationToken);

        // Data query with pagination
        var dataSql = $@"
            SELECT `StoreName`, `Key`, `ValueJson`, `ETag`, `CreatedAt`, `UpdatedAt`, `Version`
            FROM `state_entries`
            WHERE `StoreName` = @p0
            {(whereClauses.Length > 0 ? $"AND {whereClauses}" : "")}
            {orderBy}
            LIMIT @pLimit OFFSET @pOffset";

        var dataParams = new List<object?> { _storeName };
        dataParams.AddRange(parameters);

        // Replace placeholder parameter names
        var paramIndex = dataParams.Count;
        dataSql = dataSql.Replace("@pLimit", $"@p{paramIndex}");
        dataParams.Add(limit);
        paramIndex++;
        dataSql = dataSql.Replace("@pOffset", $"@p{paramIndex}");
        dataParams.Add(offset);

        var entries = await context.StateEntries
            .FromSqlRaw(dataSql, dataParams.ToArray())
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var results = new List<JsonQueryResult<TValue>>();
        foreach (var entry in entries)
        {
            var value = BannouJson.Deserialize<TValue>(entry.ValueJson);
            if (value != null)
            {
                results.Add(new JsonQueryResult<TValue>(entry.Key, value));
            }
        }

        _logger.LogDebug("JSON paged query on store '{Store}' returned {Count}/{Total} results (offset: {Offset}, limit: {Limit})",
            _storeName, results.Count, totalCount, offset, limit);

        return new JsonPagedResult<TValue>(results, totalCount, offset, limit);
    }

    /// <inheritdoc/>
    public async Task<long> JsonCountAsync(
        IReadOnlyList<QueryCondition>? conditions,
        CancellationToken cancellationToken = default)
    {
        var (whereClauses, parameters) = BuildWhereClause(conditions ?? Array.Empty<QueryCondition>());

        // EF Core 8 SqlQueryRaw<T> requires column named 'Value'
        var sql = $@"
            SELECT COUNT(*) AS Value
            FROM `state_entries`
            WHERE `StoreName` = @p0
            {(whereClauses.Length > 0 ? $"AND {whereClauses}" : "")}";

        var allParams = new List<object?> { _storeName };
        allParams.AddRange(parameters);

        using var context = CreateContext();
        var count = await context.Database
            .SqlQueryRaw<long>(sql, allParams.Where(p => p != null).ToArray()!)
            .FirstOrDefaultAsync(cancellationToken);

        return count;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<object?>> JsonDistinctAsync(
        string path,
        IReadOnlyList<QueryCondition>? conditions = null,
        CancellationToken cancellationToken = default)
    {

        var (whereClauses, parameters) = BuildWhereClause(conditions ?? Array.Empty<QueryCondition>());
        var escapedPath = EscapeJsonPath(path);

        // EF Core 8 SqlQueryRaw<T> requires column named 'Value'
        var sql = $@"
            SELECT DISTINCT JSON_UNQUOTE(JSON_EXTRACT(`ValueJson`, '{escapedPath}')) AS Value
            FROM `state_entries`
            WHERE `StoreName` = @p0
            AND JSON_EXTRACT(`ValueJson`, '{escapedPath}') IS NOT NULL
            {(whereClauses.Length > 0 ? $"AND {whereClauses}" : "")}";

        var allParams = new List<object?> { _storeName };
        allParams.AddRange(parameters);

        using var context = CreateContext();
        var results = await context.Database
            .SqlQueryRaw<string?>(sql, allParams.Where(p => p != null).ToArray()!)
            .ToListAsync(cancellationToken);

        _logger.LogDebug("JSON distinct query on store '{Store}' path '{Path}' returned {Count} values",
            _storeName, path, results.Count);

        return results.Cast<object?>().ToList();
    }

    /// <inheritdoc/>
    public async Task<object?> JsonAggregateAsync(
        string path,
        JsonAggregation aggregation,
        IReadOnlyList<QueryCondition>? conditions = null,
        CancellationToken cancellationToken = default)
    {

        var (whereClauses, parameters) = BuildWhereClause(conditions ?? Array.Empty<QueryCondition>());
        var escapedPath = EscapeJsonPath(path);

        var aggFunc = aggregation switch
        {
            JsonAggregation.Count => "COUNT",
            JsonAggregation.Sum => "SUM",
            JsonAggregation.Avg => "AVG",
            JsonAggregation.Min => "MIN",
            JsonAggregation.Max => "MAX",
            _ => throw new ArgumentOutOfRangeException(nameof(aggregation))
        };

        var extractExpr = aggregation == JsonAggregation.Count
            ? $"JSON_EXTRACT(`ValueJson`, '{escapedPath}')"
            : $"CAST(JSON_EXTRACT(`ValueJson`, '{escapedPath}') AS DECIMAL(20,6))";

        // EF Core 8 SqlQueryRaw<T> requires column named 'Value'
        var sql = $@"
            SELECT {aggFunc}({extractExpr}) AS Value
            FROM `state_entries`
            WHERE `StoreName` = @p0
            {(whereClauses.Length > 0 ? $"AND {whereClauses}" : "")}";

        var allParams = new List<object?> { _storeName };
        allParams.AddRange(parameters);

        using var context = CreateContext();
        var result = await context.Database
            .SqlQueryRaw<decimal?>(sql, allParams.Where(p => p != null).ToArray()!)
            .FirstOrDefaultAsync(cancellationToken);

        _logger.LogDebug("JSON {Aggregation} on store '{Store}' path '{Path}' = {Result}",
            aggregation, _storeName, path, result);

        return result;
    }

    #endregion

    #region SQL Builder Helpers

    /// <summary>
    /// Builds WHERE clause conditions from JSON query conditions.
    /// </summary>
    private (string WhereClause, List<object?> Parameters) BuildWhereClause(
        IReadOnlyList<QueryCondition> conditions)
    {
        if (conditions.Count == 0)
        {
            return (string.Empty, new List<object?>());
        }

        var clauses = new List<string>();
        var parameters = new List<object?>();
        var paramIndex = 1; // Start at 1 since p0 is StoreName

        foreach (var condition in conditions)
        {
            var escapedPath = EscapeJsonPath(condition.Path);
            var clause = BuildConditionClause(condition, escapedPath, ref paramIndex, parameters);
            if (!string.IsNullOrEmpty(clause))
            {
                clauses.Add(clause);
            }
        }

        return (string.Join(" AND ", clauses), parameters);
    }

    /// <summary>
    /// Builds a single condition clause.
    /// </summary>
    private static string BuildConditionClause(
        QueryCondition condition,
        string escapedPath,
        ref int paramIndex,
        List<object?> parameters)
    {
        var jsonExtract = $"JSON_EXTRACT(`ValueJson`, '{escapedPath}')";
        var jsonUnquote = $"JSON_UNQUOTE({jsonExtract})";

        switch (condition.Operator)
        {
            case QueryOperator.Equals:
                parameters.Add(SerializeValue(condition.Value));
                return $"{jsonUnquote} = @p{paramIndex++}";

            case QueryOperator.NotEquals:
                parameters.Add(SerializeValue(condition.Value));
                return $"{jsonUnquote} != @p{paramIndex++}";

            case QueryOperator.GreaterThan:
                parameters.Add(condition.Value);
                return $"CAST({jsonExtract} AS DECIMAL(20,6)) > @p{paramIndex++}";

            case QueryOperator.GreaterThanOrEqual:
                parameters.Add(condition.Value);
                return $"CAST({jsonExtract} AS DECIMAL(20,6)) >= @p{paramIndex++}";

            case QueryOperator.LessThan:
                parameters.Add(condition.Value);
                return $"CAST({jsonExtract} AS DECIMAL(20,6)) < @p{paramIndex++}";

            case QueryOperator.LessThanOrEqual:
                parameters.Add(condition.Value);
                return $"CAST({jsonExtract} AS DECIMAL(20,6)) <= @p{paramIndex++}";

            case QueryOperator.Contains:
                parameters.Add($"%{condition.Value}%");
                return $"{jsonUnquote} LIKE @p{paramIndex++}";

            case QueryOperator.StartsWith:
                parameters.Add($"{condition.Value}%");
                return $"{jsonUnquote} LIKE @p{paramIndex++}";

            case QueryOperator.EndsWith:
                parameters.Add($"%{condition.Value}");
                return $"{jsonUnquote} LIKE @p{paramIndex++}";

            case QueryOperator.In:
                // For array containment: JSON_CONTAINS(ValueJson, '"value"', '$.path')
                var jsonValue = BannouJson.Serialize(condition.Value);
                parameters.Add(jsonValue);
                return $"JSON_CONTAINS(`ValueJson`, @p{paramIndex++}, '{escapedPath}')";

            case QueryOperator.Exists:
                // Check if value is non-null (covers both "path exists with non-null value" cases)
                // Uses IS NOT NULL which returns false for missing paths OR JSON null values
                return $"JSON_EXTRACT(`ValueJson`, '{escapedPath}') IS NOT NULL";

            case QueryOperator.NotExists:
                // Check if value is null (covers both "path missing" and "path exists with null value")
                // Uses IS NULL which returns true for missing paths OR JSON null values
                // This matches C# null comparison semantics: x.Field == null
                return $"JSON_EXTRACT(`ValueJson`, '{escapedPath}') IS NULL";

            case QueryOperator.FullText:
                // Full-text search requires a FULLTEXT index on the column
                // This is a simplified implementation using LIKE
                parameters.Add($"%{condition.Value}%");
                return $"{jsonUnquote} LIKE @p{paramIndex++}";

            default:
                throw new ArgumentOutOfRangeException(nameof(condition.Operator));
        }
    }

    /// <summary>
    /// Escapes a JSON path for use in SQL.
    /// </summary>
    private static string EscapeJsonPath(string path)
    {
        // Ensure path starts with $
        if (!path.StartsWith("$"))
        {
            path = "$." + path;
        }
        // Escape single quotes
        return path.Replace("'", "''");
    }

    /// <summary>
    /// Serializes a value for SQL parameter comparison.
    /// Must match BannouJson serialization behavior for consistent query results.
    /// </summary>
    private static object? SerializeValue(object? value)
    {
        return value switch
        {
            null => null,
            string s => s,
            bool b => b.ToString().ToLowerInvariant(), // JSON boolean: true/false
            Enum e => e.ToString(), // Match JsonStringEnumConverter: enum name as string
            Guid g => g.ToString(), // Explicit GUID handling
            DateTime dt => dt.ToString("O"), // ISO 8601 format
            DateTimeOffset dto => dto.ToString("O"), // ISO 8601 format
            _ => value.ToString()
        };
    }

    #endregion

    #region Expression to QueryCondition Translator

    /// <summary>
    /// Attempts to convert a LINQ expression to QueryCondition objects for SQL-level filtering.
    /// Supports simple property comparisons and AND combinations. Returns false for complex
    /// expressions that require in-memory evaluation.
    /// </summary>
    /// <remarks>
    /// IMPLEMENTATION TENETS: This enables SQL-level filtering for common query patterns,
    /// avoiding the O(N) memory issue of loading all entries for large datasets.
    /// Supported patterns:
    /// - Property equality: x => x.Name == "John"
    /// - Null comparisons: x => x.Field == null, x => x.Field != null
    /// - Numeric comparisons: x => x.Age > 25
    /// - Boolean properties: x => x.IsActive (implicitly == true)
    /// - AND combinations: x => x.Name == "John" &amp;&amp; x.Age > 25
    /// - String methods: x.Name.Contains("oh"), x.Name.StartsWith("J")
    /// - Nested properties: x => x.Address.City == "NYC"
    /// Unsupported (falls back to in-memory):
    /// - OR combinations, NOT operators, method calls on non-string types
    /// - Complex expressions involving calculations
    /// </remarks>
    private bool TryConvertExpressionToConditions(
        Expression<Func<TValue, bool>> predicate,
        out IReadOnlyList<QueryCondition> conditions)
    {
        conditions = Array.Empty<QueryCondition>();

        try
        {
            var result = new List<QueryCondition>();
            if (TryVisitExpression(predicate.Body, predicate.Parameters[0], result))
            {
                conditions = result;
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to convert expression to QueryConditions - will use in-memory fallback");
            return false;
        }
    }

    private static bool TryVisitExpression(
        Expression expression,
        ParameterExpression parameter,
        List<QueryCondition> conditions)
    {
        return expression switch
        {
            BinaryExpression binary => TryVisitBinaryExpression(binary, parameter, conditions),
            MethodCallExpression methodCall => TryVisitMethodCallExpression(methodCall, parameter, conditions),
            UnaryExpression unary when unary.NodeType == ExpressionType.Not =>
                false, // NOT operations not supported
            MemberExpression member => TryVisitBooleanMemberExpression(member, parameter, conditions),
            _ => false
        };
    }

    private static bool TryVisitBinaryExpression(
        BinaryExpression binary,
        ParameterExpression parameter,
        List<QueryCondition> conditions)
    {
        // Handle AND combinations
        if (binary.NodeType == ExpressionType.AndAlso)
        {
            return TryVisitExpression(binary.Left, parameter, conditions) &&
                   TryVisitExpression(binary.Right, parameter, conditions);
        }

        // Handle OR - not supported for SQL translation
        if (binary.NodeType == ExpressionType.OrElse)
        {
            return false;
        }

        // Handle null comparisons: x.Field == null or x.Field != null
        // Must check before normal flow since null requires special SQL (IS NULL / IS NOT NULL)
        if (TryHandleNullComparison(binary, parameter, conditions))
        {
            return true;
        }

        // Handle comparison operators
        if (!TryGetMemberPath(binary.Left, parameter, out var path))
        {
            // Try reversed (e.g., "John" == x.Name)
            if (!TryGetMemberPath(binary.Right, parameter, out path))
            {
                return false;
            }
            // Swap for reversed comparison
            var temp = binary.Left;
            binary = Expression.MakeBinary(
                GetReversedOperator(binary.NodeType),
                binary.Right,
                temp) as BinaryExpression ?? binary;
        }

        if (!TryGetConstantValue(binary.Right, out var value))
        {
            // Try reversed
            if (!TryGetConstantValue(binary.Left, out value))
            {
                return false;
            }
        }

        // If value is null at this point, we should have caught it in TryHandleNullComparison
        // but as a safety net, fall back to in-memory filtering
        if (value == null)
        {
            return false;
        }

        var queryOperator = binary.NodeType switch
        {
            ExpressionType.Equal => QueryOperator.Equals,
            ExpressionType.NotEqual => QueryOperator.NotEquals,
            ExpressionType.GreaterThan => QueryOperator.GreaterThan,
            ExpressionType.GreaterThanOrEqual => QueryOperator.GreaterThanOrEqual,
            ExpressionType.LessThan => QueryOperator.LessThan,
            ExpressionType.LessThanOrEqual => QueryOperator.LessThanOrEqual,
            _ => (QueryOperator?)null
        };

        if (queryOperator == null)
        {
            return false;
        }

        conditions.Add(new QueryCondition
        {
            Path = path,
            Operator = queryOperator.Value,
            Value = value
        });

        return true;
    }

    /// <summary>
    /// Handles null comparison expressions: x.Field == null or x.Field != null.
    /// Uses SQL IS NULL / IS NOT NULL semantics via special QueryCondition markers.
    /// </summary>
    private static bool TryHandleNullComparison(
        BinaryExpression binary,
        ParameterExpression parameter,
        List<QueryCondition> conditions)
    {
        // Only handle equality/inequality with null
        if (binary.NodeType != ExpressionType.Equal && binary.NodeType != ExpressionType.NotEqual)
        {
            return false;
        }

        // Check if either side is a null constant
        var leftIsNull = IsNullConstant(binary.Left);
        var rightIsNull = IsNullConstant(binary.Right);

        if (!leftIsNull && !rightIsNull)
        {
            return false; // Neither side is null, not a null comparison
        }

        // Get the member path from the non-null side
        var memberExpr = leftIsNull ? binary.Right : binary.Left;
        if (!TryGetMemberPath(memberExpr, parameter, out var path))
        {
            return false;
        }

        // x.Field == null uses NotExists (JSON_EXTRACT IS NULL covers both missing and JSON null)
        // x.Field != null uses Exists (JSON_EXTRACT IS NOT NULL)
        var isEqualToNull = binary.NodeType == ExpressionType.Equal;
        conditions.Add(new QueryCondition
        {
            Path = path,
            Operator = isEqualToNull ? QueryOperator.NotExists : QueryOperator.Exists,
            Value = new object() // Placeholder, not used by Exists/NotExists operators
        });

        return true;
    }

    /// <summary>
    /// Checks if an expression is a null constant.
    /// </summary>
    private static bool IsNullConstant(Expression expression)
    {
        // Direct null constant
        if (expression is ConstantExpression { Value: null })
        {
            return true;
        }

        // Handle default(T) which compiles to null for reference types
        if (expression is DefaultExpression)
        {
            return true;
        }

        // Handle Convert(null) for nullable value types
        if (expression is UnaryExpression { NodeType: ExpressionType.Convert } unary)
        {
            return IsNullConstant(unary.Operand);
        }

        return false;
    }

    private static ExpressionType GetReversedOperator(ExpressionType type)
    {
        return type switch
        {
            ExpressionType.GreaterThan => ExpressionType.LessThan,
            ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
            ExpressionType.LessThan => ExpressionType.GreaterThan,
            ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
            _ => type // Equal/NotEqual are symmetric
        };
    }

    private static bool TryVisitMethodCallExpression(
        MethodCallExpression methodCall,
        ParameterExpression parameter,
        List<QueryCondition> conditions)
    {
        // Handle string methods: Contains, StartsWith, EndsWith
        if (methodCall.Method.DeclaringType == typeof(string))
        {
            if (methodCall.Object == null) return false;

            if (!TryGetMemberPath(methodCall.Object, parameter, out var path))
            {
                return false;
            }

            if (methodCall.Arguments.Count != 1)
            {
                return false;
            }

            if (!TryGetConstantValue(methodCall.Arguments[0], out var value))
            {
                return false;
            }

            var queryOperator = methodCall.Method.Name switch
            {
                "Contains" => QueryOperator.Contains,
                "StartsWith" => QueryOperator.StartsWith,
                "EndsWith" => QueryOperator.EndsWith,
                _ => (QueryOperator?)null
            };

            if (queryOperator == null)
            {
                return false;
            }

            conditions.Add(new QueryCondition
            {
                Path = path,
                Operator = queryOperator.Value,
                Value = value ?? string.Empty
            });

            return true;
        }

        return false;
    }

    private static bool TryVisitBooleanMemberExpression(
        MemberExpression member,
        ParameterExpression parameter,
        List<QueryCondition> conditions)
    {
        // Handle standalone boolean property access: x => x.IsActive (means x.IsActive == true)
        if (member.Type == typeof(bool) && TryGetMemberPath(member, parameter, out var path))
        {
            conditions.Add(new QueryCondition
            {
                Path = path,
                Operator = QueryOperator.Equals,
                Value = true
            });
            return true;
        }

        return false;
    }

    private static bool TryGetMemberPath(
        Expression expression,
        ParameterExpression parameter,
        out string path)
    {
        path = string.Empty;

        if (expression is not MemberExpression member)
        {
            // Handle conversion expressions (e.g., nullable value types)
            if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            {
                return TryGetMemberPath(unary.Operand, parameter, out path);
            }
            return false;
        }

        // Build the path from inner to outer
        var pathParts = new List<string>();
        var current = member;

        while (current != null)
        {
            pathParts.Insert(0, current.Member.Name);

            if (current.Expression == parameter)
            {
                // We've reached the parameter - build the JSON path
                path = "$." + string.Join(".", pathParts);
                return true;
            }

            current = current.Expression as MemberExpression;
        }

        return false;
    }

    private static bool TryGetConstantValue(Expression expression, out object? value)
    {
        value = null;

        // Direct constant
        if (expression is ConstantExpression constant)
        {
            value = constant.Value;
            return true;
        }

        // Member access on a constant (captured variable)
        if (expression is MemberExpression member && member.Expression is ConstantExpression constExpr)
        {
            var container = constExpr.Value;
            if (container == null) return false;

            value = member.Member switch
            {
                System.Reflection.FieldInfo field => field.GetValue(container),
                System.Reflection.PropertyInfo prop => prop.GetValue(container),
                _ => null
            };
            return true;
        }

        // Handle conversion expressions
        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            return TryGetConstantValue(unary.Operand, out value);
        }

        return false;
    }

    #endregion
}
