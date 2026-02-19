#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// SQLite-backed state store for local/self-hosted deployments.
/// Provides SQL query support (IQueryableStateStore, IJsonQueryableStateStore)
/// without requiring external MySQL infrastructure. Data IS persisted to file.
/// Creates a new DbContext per operation for thread-safety with concurrent requests.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public sealed class SqliteStateStore<TValue> : IJsonQueryableStateStore<TValue>
    where TValue : class
{
    private readonly DbContextOptions<StateDbContext> _options;
    private readonly string _storeName;
    private readonly int _inMemoryFallbackLimit;
    private readonly ILogger<SqliteStateStore<TValue>> _logger;
    private readonly StateErrorPublisherAsync? _errorPublisher;

    /// <summary>
    /// Creates a new SQLite state store.
    /// </summary>
    /// <param name="options">EF Core database context options for creating per-operation contexts.</param>
    /// <param name="storeName">Name of this state store.</param>
    /// <param name="inMemoryFallbackLimit">Maximum entries to load when falling back to in-memory filtering.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="errorPublisher">Optional callback for publishing state errors with deduplication.</param>
    public SqliteStateStore(
        DbContextOptions<StateDbContext> options,
        string storeName,
        int inMemoryFallbackLimit,
        ILogger<SqliteStateStore<TValue>> logger,
        StateErrorPublisherAsync? errorPublisher = null)
    {
        _options = options;
        _storeName = storeName;
        _inMemoryFallbackLimit = inMemoryFallbackLimit;
        _logger = logger;
        _errorPublisher = errorPublisher;
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
        var input = $"{key}:{json}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash)[..12];
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

        try
        {
            return BannouJson.Deserialize<TValue>(entry.ValueJson);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - data may be corrupted", key, _storeName);
            return null;
        }
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

        try
        {
            return (BannouJson.Deserialize<TValue>(entry.ValueJson), entry.ETag);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - data may be corrupted", key, _storeName);
            return (null, null);
        }
    }

    /// <inheritdoc/>
    public async Task<string> SaveAsync(
        string key,
        TValue value,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // SQLite stores do not support TTL - use Redis for ephemeral data
        if (options?.Ttl != null)
        {
            throw new InvalidOperationException(
                $"TTL is not supported for SQLite stores. Store '{_storeName}' uses SQLite backend. " +
                "For ephemeral data requiring expiration, use a Redis-backed store instead.");
        }

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

        var results = new Dictionary<string, TValue>();
        foreach (var entry in entries)
        {
            try
            {
                var value = BannouJson.Deserialize<TValue>(entry.ValueJson);
                if (value != null)
                {
                    results[entry.Key] = value;
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - skipping corrupted item", entry.Key, _storeName);
            }
        }

        _logger.LogDebug("Bulk get {RequestedCount} keys from store '{Store}', found {FoundCount}",
            keyList.Count, _storeName, results.Count);

        return results;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> SaveBulkAsync(
        IEnumerable<KeyValuePair<string, TValue>> items,
        StateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var itemsList = items as ICollection<KeyValuePair<string, TValue>> ?? items.ToList();

        if (itemsList.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        if (options?.Ttl != null)
        {
            throw new InvalidOperationException(
                $"TTL is not supported for SQLite stores. Store '{_storeName}' uses SQLite backend.");
        }

        var now = DateTimeOffset.UtcNow;
        var etags = new Dictionary<string, string>();

        using var context = CreateContext();
        var existingKeys = itemsList.Select(kvp => kvp.Key).ToList();
        var existingEntries = await context.StateEntries
            .Where(e => e.StoreName == _storeName && existingKeys.Contains(e.Key))
            .ToDictionaryAsync(e => e.Key, cancellationToken);

        foreach (var (key, value) in itemsList)
        {
            var json = BannouJson.Serialize(value);
            var etag = GenerateETag(key, json);

            if (existingEntries.TryGetValue(key, out var existing))
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

            etags[key] = etag;
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Bulk saved {Count} items to store '{Store}'", itemsList.Count, _storeName);

        return etags;
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

    #region IQueryableStateStore Implementation

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TValue>> QueryAsync(
        Expression<Func<TValue, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        // IMPLEMENTATION TENETS: Try SQL-level filtering first to avoid loading entire store
        if (TryConvertExpressionToConditions(predicate, out var conditions))
        {
            _logger.LogDebug(
                "QueryAsync using SQL-level filtering with {ConditionCount} conditions for store '{Store}'",
                conditions.Count, _storeName);

            var (whereClauses, parameters) = BuildWhereClause(conditions);

            var sql = $@"
                SELECT ""StoreName"", ""Key"", ""ValueJson"", ""ETag"", ""CreatedAt"", ""UpdatedAt"", ""Version""
                FROM ""state_entries""
                WHERE ""StoreName"" = @p0
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
                try
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
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - skipping corrupted item", entry.Key, _storeName);
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
            try
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
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - skipping corrupted item", entry.Key, _storeName);
            }
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

            // Build ORDER BY clause - SQLite uses json_extract which returns unquoted values
            var orderByClause = @"ORDER BY ""UpdatedAt"" DESC";
            if (sortPath != null)
            {
                var escapedPath = EscapeJsonPath(sortPath);
                var direction = descending ? "DESC" : "ASC";
                orderByClause = $@"ORDER BY json_extract(""ValueJson"", '{escapedPath}') {direction}";
            }

            // Count query
            var countSql = $@"
                SELECT COUNT(*) AS ""Value""
                FROM ""state_entries""
                WHERE ""StoreName"" = @p0
                {(whereClauses.Length > 0 ? $"AND {whereClauses}" : "")}";

            var allParams = new List<object?> { _storeName };
            allParams.AddRange(parameters);

            using var context = CreateContext();
            var totalCount = await context.Database
                .SqlQueryRaw<long>(countSql, allParams.Where(p => p != null).ToArray()!)
                .FirstOrDefaultAsync(cancellationToken);

            // Data query with pagination
            var dataSql = $@"
                SELECT ""StoreName"", ""Key"", ""ValueJson"", ""ETag"", ""CreatedAt"", ""UpdatedAt"", ""Version""
                FROM ""state_entries""
                WHERE ""StoreName"" = @p0
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
                try
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
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - skipping corrupted item", entry.Key, _storeName);
                }
            }

            _logger.LogDebug("SQL-level paged query on store '{Store}' returned page {Page} with {Count} items (total: {Total})",
                _storeName, page, items.Count, totalCount);

            return new PagedResult<TValue>(items, totalCount, page, pageSize);
        }

        // Fallback to in-memory filtering for complex expressions
        _logger.LogWarning(
            "QueryPagedAsync falling back to in-memory filtering for store '{Store}' - " +
            "expression could not be translated to SQL.",
            _storeName);

        using var fallbackContext = CreateContext();
        var allEntries = await fallbackContext.StateEntries
            .AsNoTracking()
            .Where(e => e.StoreName == _storeName)
            .ToListAsync(cancellationToken);

        if (allEntries.Count > _inMemoryFallbackLimit)
        {
            throw new InvalidOperationException(
                $"QueryPagedAsync in-memory fallback would load {allEntries.Count} entries from store '{_storeName}' " +
                $"(limit: {_inMemoryFallbackLimit}). Use JsonQueryPagedAsync with explicit conditions for large datasets, " +
                "or simplify the predicate to enable SQL translation.");
        }

        var deserializedValues = new List<TValue>();
        foreach (var entry in allEntries)
        {
            try
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
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - skipping corrupted item", entry.Key, _storeName);
            }
        }

        var query = deserializedValues.AsQueryable();
        if (predicate != null)
        {
            query = query.Where(predicate);
        }

        var inMemoryTotalCount = query.Count();

        if (orderBy != null)
        {
            query = descending
                ? query.OrderByDescending(orderBy)
                : query.OrderBy(orderBy);
        }

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

            var sql = $@"
                SELECT COUNT(*) AS ""Value""
                FROM ""state_entries""
                WHERE ""StoreName"" = @p0
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

        if (entries.Count > _inMemoryFallbackLimit)
        {
            throw new InvalidOperationException(
                $"CountAsync in-memory fallback would load {entries.Count} entries from store '{_storeName}' " +
                $"(limit: {_inMemoryFallbackLimit}). Use JsonCountAsync with explicit conditions for large datasets, " +
                "or simplify the predicate to enable SQL translation.");
        }

        var deserializedValues = new List<TValue>();
        foreach (var entry in entries)
        {
            try
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
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - skipping corrupted item", entry.Key, _storeName);
            }
        }

        var filteredCount = deserializedValues
            .AsQueryable()
            .Where(predicate)
            .LongCount();

        return filteredCount;
    }

    #endregion

    #region IJsonQueryableStateStore Implementation

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JsonQueryResult<TValue>>> JsonQueryAsync(
        IReadOnlyList<QueryCondition> conditions,
        CancellationToken cancellationToken = default)
    {
        var (whereClauses, parameters) = BuildWhereClause(conditions);

        var sql = $@"
            SELECT ""StoreName"", ""Key"", ""ValueJson"", ""ETag"", ""CreatedAt"", ""UpdatedAt"", ""Version""
            FROM ""state_entries""
            WHERE ""StoreName"" = @p0
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
            try
            {
                var value = BannouJson.Deserialize<TValue>(entry.ValueJson);
                if (value != null)
                {
                    results.Add(new JsonQueryResult<TValue>(entry.Key, value));
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - skipping corrupted item", entry.Key, _storeName);
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

        // Build ORDER BY clause - SQLite json_extract returns unquoted values
        var orderByStr = @"ORDER BY ""UpdatedAt"" DESC";
        if (sortBy != null)
        {
            var sortPath = EscapeJsonPath(sortBy.Path);
            var direction = sortBy.Descending ? "DESC" : "ASC";
            orderByStr = $@"ORDER BY json_extract(""ValueJson"", '{sortPath}') {direction}";
        }

        // Count query
        var countSql = $@"
            SELECT COUNT(*) AS ""Value""
            FROM ""state_entries""
            WHERE ""StoreName"" = @p0
            {(whereClauses.Length > 0 ? $"AND {whereClauses}" : "")}";

        var allParams = new List<object?> { _storeName };
        allParams.AddRange(parameters);

        using var context = CreateContext();
        var totalCount = await context.Database
            .SqlQueryRaw<long>(countSql, allParams.Where(p => p != null).ToArray()!)
            .FirstOrDefaultAsync(cancellationToken);

        // Data query with pagination
        var dataSql = $@"
            SELECT ""StoreName"", ""Key"", ""ValueJson"", ""ETag"", ""CreatedAt"", ""UpdatedAt"", ""Version""
            FROM ""state_entries""
            WHERE ""StoreName"" = @p0
            {(whereClauses.Length > 0 ? $"AND {whereClauses}" : "")}
            {orderByStr}
            LIMIT @pLimit OFFSET @pOffset";

        var dataParams = new List<object?> { _storeName };
        dataParams.AddRange(parameters);

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
            try
            {
                var value = BannouJson.Deserialize<TValue>(entry.ValueJson);
                if (value != null)
                {
                    results.Add(new JsonQueryResult<TValue>(entry.Key, value));
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization failed for key '{Key}' in store '{Store}' - skipping corrupted item", entry.Key, _storeName);
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

        var sql = $@"
            SELECT COUNT(*) AS ""Value""
            FROM ""state_entries""
            WHERE ""StoreName"" = @p0
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

        // SQLite json_extract already returns unquoted string values
        var sql = $@"
            SELECT DISTINCT json_extract(""ValueJson"", '{escapedPath}') AS ""Value""
            FROM ""state_entries""
            WHERE ""StoreName"" = @p0
            AND json_extract(""ValueJson"", '{escapedPath}') IS NOT NULL
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

        // SQLite uses CAST(... AS REAL) instead of MySQL's CAST(... AS DECIMAL(20,6))
        var extractExpr = aggregation == JsonAggregation.Count
            ? $@"json_extract(""ValueJson"", '{escapedPath}')"
            : $@"CAST(json_extract(""ValueJson"", '{escapedPath}') AS REAL)";

        var sql = $@"
            SELECT {aggFunc}({extractExpr}) AS ""Value""
            FROM ""state_entries""
            WHERE ""StoreName"" = @p0
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
    /// Builds a single condition clause using SQLite JSON functions.
    /// SQLite's json_extract returns unquoted string values, unlike MySQL which requires JSON_UNQUOTE.
    /// </summary>
    private static string BuildConditionClause(
        QueryCondition condition,
        string escapedPath,
        ref int paramIndex,
        List<object?> parameters)
    {
        // SQLite json_extract already returns unquoted values (no JSON_UNQUOTE equivalent needed)
        var jsonExtract = $@"json_extract(""ValueJson"", '{escapedPath}')";

        switch (condition.Operator)
        {
            case QueryOperator.Equals:
                parameters.Add(SerializeValue(condition.Value));
                return $"{jsonExtract} = @p{paramIndex++}";

            case QueryOperator.NotEquals:
                parameters.Add(SerializeValue(condition.Value));
                return $"{jsonExtract} != @p{paramIndex++}";

            case QueryOperator.GreaterThan:
                parameters.Add(condition.Value);
                return $"CAST({jsonExtract} AS REAL) > @p{paramIndex++}";

            case QueryOperator.GreaterThanOrEqual:
                parameters.Add(condition.Value);
                return $"CAST({jsonExtract} AS REAL) >= @p{paramIndex++}";

            case QueryOperator.LessThan:
                parameters.Add(condition.Value);
                return $"CAST({jsonExtract} AS REAL) < @p{paramIndex++}";

            case QueryOperator.LessThanOrEqual:
                parameters.Add(condition.Value);
                return $"CAST({jsonExtract} AS REAL) <= @p{paramIndex++}";

            case QueryOperator.Contains:
                parameters.Add($"%{condition.Value}%");
                return $"{jsonExtract} LIKE @p{paramIndex++}";

            case QueryOperator.StartsWith:
                parameters.Add($"{condition.Value}%");
                return $"{jsonExtract} LIKE @p{paramIndex++}";

            case QueryOperator.EndsWith:
                parameters.Add($"%{condition.Value}");
                return $"{jsonExtract} LIKE @p{paramIndex++}";

            case QueryOperator.In:
                if (condition.Value is IEnumerable enumerable && condition.Value is not string)
                {
                    var inParams = new List<string>();
                    foreach (var item in enumerable)
                    {
                        parameters.Add(SerializeValue(item));
                        inParams.Add($"@p{paramIndex++}");
                    }

                    if (inParams.Count == 0)
                    {
                        return "1 = 0";
                    }

                    return $"{jsonExtract} IN ({string.Join(", ", inParams)})";
                }
                else
                {
                    // Array containment check using json_each for SQLite
                    // Check if the JSON array at path contains the value
                    var jsonValue = BannouJson.Serialize(condition.Value);
                    parameters.Add(condition.Value?.ToString());
                    return $@"EXISTS (SELECT 1 FROM json_each(""ValueJson"", '{escapedPath}') WHERE ""value"" = @p{paramIndex++})";
                }

            case QueryOperator.Exists:
                return $@"json_extract(""ValueJson"", '{escapedPath}') IS NOT NULL";

            case QueryOperator.NotExists:
                return $@"json_extract(""ValueJson"", '{escapedPath}') IS NULL";

            case QueryOperator.FullText:
                parameters.Add($"%{condition.Value}%");
                return $"{jsonExtract} LIKE @p{paramIndex++}";

            default:
                throw new ArgumentOutOfRangeException(nameof(condition.Operator));
        }
    }

    /// <summary>
    /// Escapes a JSON path for use in SQL.
    /// </summary>
    private static string EscapeJsonPath(string path)
    {
        if (!path.StartsWith("$"))
        {
            path = "$." + path;
        }
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
            bool b => b.ToString().ToLowerInvariant(),
            Enum e => e.ToString(),
            Guid g => g.ToString(),
            DateTime dt => dt.ToString("O"),
            DateTimeOffset dto => dto.ToString("O"),
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
            // IMPLEMENTATION TENETS: Infrastructure libs intentionally catch broadly to prevent cascading failures.
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
                false,
            MemberExpression member => TryVisitBooleanMemberExpression(member, parameter, conditions),
            _ => false
        };
    }

    private static bool TryVisitBinaryExpression(
        BinaryExpression binary,
        ParameterExpression parameter,
        List<QueryCondition> conditions)
    {
        if (binary.NodeType == ExpressionType.AndAlso)
        {
            return TryVisitExpression(binary.Left, parameter, conditions) &&
                    TryVisitExpression(binary.Right, parameter, conditions);
        }

        if (binary.NodeType == ExpressionType.OrElse)
        {
            return false;
        }

        if (TryHandleNullComparison(binary, parameter, conditions))
        {
            return true;
        }

        if (!TryGetMemberPath(binary.Left, parameter, out var path))
        {
            if (!TryGetMemberPath(binary.Right, parameter, out path))
            {
                return false;
            }
            var temp = binary.Left;
            binary = Expression.MakeBinary(
                GetReversedOperator(binary.NodeType),
                binary.Right,
                temp) as BinaryExpression ?? binary;
        }

        if (!TryGetConstantValue(binary.Right, out var value))
        {
            if (!TryGetConstantValue(binary.Left, out value))
            {
                return false;
            }
        }

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

    private static bool TryHandleNullComparison(
        BinaryExpression binary,
        ParameterExpression parameter,
        List<QueryCondition> conditions)
    {
        if (binary.NodeType != ExpressionType.Equal && binary.NodeType != ExpressionType.NotEqual)
        {
            return false;
        }

        var leftIsNull = IsNullConstant(binary.Left);
        var rightIsNull = IsNullConstant(binary.Right);

        if (!leftIsNull && !rightIsNull)
        {
            return false;
        }

        var memberExpr = leftIsNull ? binary.Right : binary.Left;
        if (!TryGetMemberPath(memberExpr, parameter, out var path))
        {
            return false;
        }

        var isEqualToNull = binary.NodeType == ExpressionType.Equal;
        conditions.Add(new QueryCondition
        {
            Path = path,
            Operator = isEqualToNull ? QueryOperator.NotExists : QueryOperator.Exists,
            Value = new object()
        });

        return true;
    }

    private static bool IsNullConstant(Expression expression)
    {
        if (expression is ConstantExpression { Value: null })
        {
            return true;
        }

        if (expression is DefaultExpression)
        {
            return true;
        }

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
            _ => type
        };
    }

    private static bool TryVisitMethodCallExpression(
        MethodCallExpression methodCall,
        ParameterExpression parameter,
        List<QueryCondition> conditions)
    {
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

        // Handle Enumerable.Contains(collection, item)
        if (methodCall.Method.Name == "Contains" &&
            methodCall.Method.DeclaringType == typeof(Enumerable) &&
            methodCall.Arguments.Count == 2)
        {
            if (!TryGetConstantValue(methodCall.Arguments[0], out var collection))
            {
                return false;
            }

            if (!TryGetMemberPath(methodCall.Arguments[1], parameter, out var path))
            {
                return false;
            }

            if (collection == null)
            {
                return false;
            }

            conditions.Add(new QueryCondition
            {
                Path = path,
                Operator = QueryOperator.In,
                Value = collection
            });

            return true;
        }

        // Handle List<T>.Contains(item)
        if (methodCall.Method.Name == "Contains" &&
            methodCall.Object != null &&
            methodCall.Arguments.Count == 1 &&
            IsCollectionType(methodCall.Object.Type))
        {
            if (!TryGetConstantValue(methodCall.Object, out var collection))
            {
                return false;
            }

            if (!TryGetMemberPath(methodCall.Arguments[0], parameter, out var path))
            {
                return false;
            }

            if (collection == null)
            {
                return false;
            }

            conditions.Add(new QueryCondition
            {
                Path = path,
                Operator = QueryOperator.In,
                Value = collection
            });

            return true;
        }

        return false;
    }

    private static bool IsCollectionType(Type type)
    {
        if (type.IsArray) return true;
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            return genericDef == typeof(List<>) ||
                    genericDef == typeof(HashSet<>) ||
                    genericDef == typeof(IList<>) ||
                    genericDef == typeof(ICollection<>) ||
                    genericDef == typeof(IEnumerable<>);
        }
        return false;
    }

    private static bool TryVisitBooleanMemberExpression(
        MemberExpression member,
        ParameterExpression parameter,
        List<QueryCondition> conditions)
    {
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
            if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            {
                return TryGetMemberPath(unary.Operand, parameter, out path);
            }
            return false;
        }

        var pathParts = new List<string>();
        var current = member;

        while (current != null)
        {
            pathParts.Insert(0, current.Member.Name);

            if (current.Expression == parameter)
            {
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

        if (expression is ConstantExpression constant)
        {
            value = constant.Value;
            return true;
        }

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

        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            return TryGetConstantValue(unary.Operand, out value);
        }

        return false;
    }

    #endregion
}
