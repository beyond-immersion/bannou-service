#nullable enable

using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.State.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// MySQL-backed state store for durable/queryable data.
/// Uses EF Core for query support.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public sealed class MySqlStateStore<TValue> : IQueryableStateStore<TValue>
    where TValue : class
{
    private readonly StateDbContext _context;
    private readonly string _storeName;
    private readonly ILogger<MySqlStateStore<TValue>> _logger;

    /// <summary>
    /// Creates a new MySQL state store.
    /// </summary>
    /// <param name="context">EF Core database context.</param>
    /// <param name="storeName">Name of this state store.</param>
    /// <param name="logger">Logger instance.</param>
    public MySqlStateStore(
        StateDbContext context,
        string storeName,
        ILogger<MySqlStateStore<TValue>> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _storeName = storeName ?? throw new ArgumentNullException(nameof(storeName));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static string GenerateETag(string json)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(hash)[..12]; // Short ETag
    }

    /// <inheritdoc/>
    public async Task<TValue?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var entry = await _context.StateEntries
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

        var entry = await _context.StateEntries
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

        var existing = await _context.StateEntries
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
            _context.StateEntries.Add(new StateEntry
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

        await _context.SaveChangesAsync(cancellationToken);

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

        var existing = await _context.StateEntries
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
            await _context.SaveChangesAsync(cancellationToken);
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

        var deleted = await _context.StateEntries
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

        return await _context.StateEntries
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

        var entries = await _context.StateEntries
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
        var entries = await _context.StateEntries
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
        var entries = await _context.StateEntries
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
        if (predicate == null)
        {
            // Fast path: just count entries in database
            return await _context.StateEntries
                .AsNoTracking()
                .Where(e => e.StoreName == _storeName)
                .LongCountAsync(cancellationToken);
        }

        // Slow path: load, deserialize, and filter
        var entries = await _context.StateEntries
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
}
