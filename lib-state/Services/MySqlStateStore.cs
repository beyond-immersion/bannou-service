#nullable enable

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
    private readonly ILogger<MySqlStateStore<TValue>> _logger;

    /// <summary>
    /// Creates a new MySQL state store.
    /// </summary>
    /// <param name="options">EF Core database context options for creating per-operation contexts.</param>
    /// <param name="storeName">Name of this state store.</param>
    /// <param name="logger">Logger instance.</param>
    public MySqlStateStore(
        DbContextOptions<StateDbContext> options,
        string storeName,
        ILogger<MySqlStateStore<TValue>> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _storeName = storeName ?? throw new ArgumentNullException(nameof(storeName));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new DbContext for an operation.
    /// Each operation gets its own context for thread-safety.
    /// </summary>
    private StateDbContext CreateContext() => new StateDbContext(_options);

    private static string GenerateETag(string json)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(hash)[..12]; // Short ETag
    }

    /// <inheritdoc/>
    public async Task<TValue?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

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
        ArgumentNullException.ThrowIfNull(key);

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
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var json = BannouJson.Serialize(value);
        var etag = GenerateETag(json);
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
    public async Task<bool> TrySaveAsync(
        string key,
        TValue value,
        string etag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(etag);

        using var context = CreateContext();
        var existing = await context.StateEntries
            .Where(e => e.StoreName == _storeName && e.Key == key)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing == null || existing.ETag != etag)
        {
            _logger.LogDebug("ETag mismatch for key '{Key}' in store '{Store}' (expected: {Expected}, actual: {Actual})",
                key, _storeName, etag, existing?.ETag ?? "(not found)");
            return false;
        }

        var json = BannouJson.Serialize(value);
        var newEtag = GenerateETag(json);

        existing.ValueJson = json;
        existing.ETag = newEtag;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.Version++;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Optimistic save succeeded for key '{Key}' in store '{Store}'", key, _storeName);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogDebug("Optimistic save failed (concurrent modification) for key '{Key}' in store '{Store}'",
                key, _storeName);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

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
        ArgumentNullException.ThrowIfNull(key);

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
        ArgumentNullException.ThrowIfNull(keys);

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
    public async Task<IReadOnlyList<TValue>> QueryAsync(
        Expression<Func<TValue, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        // Load all values for this store, deserialize, then filter
        // Note: For true efficiency with large datasets, use SQL JSON functions
        using var context = CreateContext();
        var entries = await context.StateEntries
            .AsNoTracking()
            .Where(e => e.StoreName == _storeName)
            .ToListAsync(cancellationToken);

        var values = entries
            .Select(e => BannouJson.Deserialize<TValue>(e.ValueJson))
            .Where(v => v != null)
            .Cast<TValue>()
            .AsQueryable()
            .Where(predicate)
            .ToList();

        _logger.LogDebug("Query on store '{Store}' returned {Count} results", _storeName, values.Count);

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

        // Load all values for this store
        using var context = CreateContext();
        var entries = await context.StateEntries
            .AsNoTracking()
            .Where(e => e.StoreName == _storeName)
            .ToListAsync(cancellationToken);

        var query = entries
            .Select(e => BannouJson.Deserialize<TValue>(e.ValueJson))
            .Where(v => v != null)
            .Cast<TValue>()
            .AsQueryable();

        // Apply filter
        if (predicate != null)
        {
            query = query.Where(predicate);
        }

        // Get total count before pagination
        var totalCount = query.Count();

        // Apply ordering
        if (orderBy != null)
        {
            query = descending
                ? query.OrderByDescending(orderBy)
                : query.OrderBy(orderBy);
        }

        // Apply pagination
        var items = query
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToList();

        _logger.LogDebug("Paged query on store '{Store}' returned page {Page} with {Count} items (total: {Total})",
            _storeName, page, items.Count, totalCount);

        return new PagedResult<TValue>(items, totalCount, page, pageSize);
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

        // Slow path: load, deserialize, and filter
        var entries = await context.StateEntries
            .AsNoTracking()
            .Where(e => e.StoreName == _storeName)
            .ToListAsync(cancellationToken);

        var count = entries
            .Select(e => BannouJson.Deserialize<TValue>(e.ValueJson))
            .Where(v => v != null)
            .Cast<TValue>()
            .AsQueryable()
            .Where(predicate)
            .LongCount();

        return count;
    }

    #region IJsonQueryableStateStore Implementation

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JsonQueryResult<TValue>>> JsonQueryAsync(
        IReadOnlyList<JsonQueryCondition> conditions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conditions);

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
        IReadOnlyList<JsonQueryCondition>? conditions,
        int offset,
        int limit,
        JsonSortSpec? sortBy = null,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));

        var (whereClauses, parameters) = BuildWhereClause(conditions ?? Array.Empty<JsonQueryCondition>());

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
        IReadOnlyList<JsonQueryCondition>? conditions,
        CancellationToken cancellationToken = default)
    {
        var (whereClauses, parameters) = BuildWhereClause(conditions ?? Array.Empty<JsonQueryCondition>());

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
        IReadOnlyList<JsonQueryCondition>? conditions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        var (whereClauses, parameters) = BuildWhereClause(conditions ?? Array.Empty<JsonQueryCondition>());
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
        IReadOnlyList<JsonQueryCondition>? conditions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        var (whereClauses, parameters) = BuildWhereClause(conditions ?? Array.Empty<JsonQueryCondition>());
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
        IReadOnlyList<JsonQueryCondition> conditions)
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
        JsonQueryCondition condition,
        string escapedPath,
        ref int paramIndex,
        List<object?> parameters)
    {
        var jsonExtract = $"JSON_EXTRACT(`ValueJson`, '{escapedPath}')";
        var jsonUnquote = $"JSON_UNQUOTE({jsonExtract})";

        switch (condition.Operator)
        {
            case JsonOperator.Equals:
                parameters.Add(SerializeValue(condition.Value));
                return $"{jsonUnquote} = @p{paramIndex++}";

            case JsonOperator.NotEquals:
                parameters.Add(SerializeValue(condition.Value));
                return $"{jsonUnquote} != @p{paramIndex++}";

            case JsonOperator.GreaterThan:
                parameters.Add(condition.Value);
                return $"CAST({jsonExtract} AS DECIMAL(20,6)) > @p{paramIndex++}";

            case JsonOperator.GreaterThanOrEqual:
                parameters.Add(condition.Value);
                return $"CAST({jsonExtract} AS DECIMAL(20,6)) >= @p{paramIndex++}";

            case JsonOperator.LessThan:
                parameters.Add(condition.Value);
                return $"CAST({jsonExtract} AS DECIMAL(20,6)) < @p{paramIndex++}";

            case JsonOperator.LessThanOrEqual:
                parameters.Add(condition.Value);
                return $"CAST({jsonExtract} AS DECIMAL(20,6)) <= @p{paramIndex++}";

            case JsonOperator.Contains:
                parameters.Add($"%{condition.Value}%");
                return $"{jsonUnquote} LIKE @p{paramIndex++}";

            case JsonOperator.StartsWith:
                parameters.Add($"{condition.Value}%");
                return $"{jsonUnquote} LIKE @p{paramIndex++}";

            case JsonOperator.EndsWith:
                parameters.Add($"%{condition.Value}");
                return $"{jsonUnquote} LIKE @p{paramIndex++}";

            case JsonOperator.In:
                // For array containment: JSON_CONTAINS(ValueJson, '"value"', '$.path')
                var jsonValue = BannouJson.Serialize(condition.Value);
                parameters.Add(jsonValue);
                return $"JSON_CONTAINS(`ValueJson`, @p{paramIndex++}, '{escapedPath}')";

            case JsonOperator.Exists:
                return $"JSON_CONTAINS_PATH(`ValueJson`, 'one', '{escapedPath}')";

            case JsonOperator.NotExists:
                return $"NOT JSON_CONTAINS_PATH(`ValueJson`, 'one', '{escapedPath}')";

            case JsonOperator.FullText:
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
    /// Serializes a value for SQL parameter.
    /// </summary>
    private static object? SerializeValue(object? value)
    {
        return value switch
        {
            null => null,
            string s => s,
            bool b => b.ToString().ToLowerInvariant(),
            _ => value.ToString()
        };
    }

    #endregion
}
