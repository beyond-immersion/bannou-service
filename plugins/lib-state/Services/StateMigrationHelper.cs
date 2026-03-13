using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.State.Services;

/// <summary>
/// Encapsulates state store migration logic: dry-run analysis, execution, and verification.
/// Injected into StateService to keep migration tooling separate from core state management.
/// </summary>
public class StateMigrationHelper
{
    private readonly StateStoreFactory _factory;
    private readonly StateServiceConfiguration _configuration;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<StateMigrationHelper> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Parameterless constructor for test mocking (Moq requires a callable constructor).
    /// </summary>
    protected StateMigrationHelper()
    {
        _factory = null!;
        _configuration = null!;
        _messageBus = null!;
        _logger = null!;
        _telemetryProvider = null!;
    }

    public StateMigrationHelper(
        IStateStoreFactory stateStoreFactory,
        StateServiceConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<StateMigrationHelper> logger,
        ITelemetryProvider telemetryProvider)
    {
        // Migration needs internal factory methods (CreateStoreWithBackend, ScanKeysAsync)
        _factory = (StateStoreFactory)stateStoreFactory;
        _configuration = configuration;
        _messageBus = serviceProvider.GetRequiredService<IMessageBus>();
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Analyzes a store for migration feasibility without making any changes.
    /// </summary>
    public virtual async Task<(StatusCodes, MigrateDryRunResponse?)> AnalyzeStoreAsync(
        string storeName,
        MigrationBackend destinationBackend,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.state", "StateMigrationHelper.AnalyzeStore");

        if (!_factory.HasStore(storeName))
        {
            return (StatusCodes.NotFound, null);
        }

        var currentBackend = _factory.GetBackendType(storeName);
        var currentMigrationBackend = ToMigrationBackend(currentBackend);
        var warnings = new List<string>();
        var incompatibleFeatures = new List<string>();
        var canMigrate = true;

        // Check: same backend
        if (currentMigrationBackend == destinationBackend)
        {
            canMigrate = false;
            warnings.Add("Source and destination backends are the same — no migration needed");
        }

        // Check: indirect-only store
        if (StateStoreDefinitions.Metadata.TryGetValue(storeName, out var metadata) && metadata.IndirectOnly)
        {
            canMigrate = false;
            warnings.Add("Store is indirect-only (accessed via IRedisOperations/Lua, not IStateStore) — cannot migrate");
        }

        // Check: search-enabled store
        var storeConfig = _factory.GetStoreConfiguration(storeName);
        if (storeConfig.EnableSearch)
        {
            incompatibleFeatures.Add("RedisSearch index (not migrated — rebuild after migration)");
        }

        // Key count (available for MySQL/Memory, null for Redis)
        long? keyCount = null;
        if (currentBackend != StateBackend.Redis)
        {
            keyCount = await _factory.GetKeyCountAsync(storeName, cancellationToken);
        }
        else
        {
            warnings.Add("Redis key count unavailable in dry-run; use DBSIZE or SCAN externally for estimate");
        }

        // ETag format change warning
        warnings.Add("ETag format will change across backends; in-flight ETags will conflict on first write (expected optimistic concurrency behavior)");

        // If the only data is incompatible features with nothing migratable, set canMigrate false
        if (currentMigrationBackend == null)
        {
            canMigrate = false;
            warnings.Add($"Store uses {currentBackend} backend which is not a migration source (only Redis and Mysql are supported)");
        }

        return (StatusCodes.OK, new MigrateDryRunResponse
        {
            StoreName = storeName,
            CurrentBackend = currentMigrationBackend ?? destinationBackend,
            DestinationBackend = destinationBackend,
            KeyValueEntryCount = keyCount.HasValue ? (int)keyCount.Value : null,
            IncompatibleFeatures = incompatibleFeatures,
            Warnings = warnings,
            CanMigrate = canMigrate
        });
    }

    /// <summary>
    /// Executes a migration, copying all key-value data from source to destination backend.
    /// </summary>
    public virtual async Task<(StatusCodes, MigrateExecuteResponse?)> ExecuteMigrationAsync(
        string storeName,
        MigrationBackend destinationBackend,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.state", "StateMigrationHelper.ExecuteMigration");

        if (!_factory.HasStore(storeName))
        {
            return (StatusCodes.NotFound, null);
        }

        var currentBackend = _factory.GetBackendType(storeName);
        var currentMigrationBackend = ToMigrationBackend(currentBackend);

        if (currentMigrationBackend == destinationBackend)
        {
            _logger.LogWarning("Migration skipped: store {StoreName} already uses {Backend}", storeName, destinationBackend);
            return (StatusCodes.BadRequest, null);
        }

        if (currentMigrationBackend == null)
        {
            _logger.LogWarning("Migration not supported for store {StoreName} with backend {Backend}", storeName, currentBackend);
            return (StatusCodes.BadRequest, null);
        }

        // Check indirect-only
        if (StateStoreDefinitions.Metadata.TryGetValue(storeName, out var metadata) && metadata.IndirectOnly)
        {
            _logger.LogWarning("Migration not supported for indirect-only store {StoreName}", storeName);
            return (StatusCodes.BadRequest, null);
        }

        var destStateBackend = destinationBackend == MigrationBackend.Redis ? StateBackend.Redis : StateBackend.MySql;
        var sourceStore = _factory.GetStore<object>(storeName);
        var destStore = _factory.CreateStoreWithBackend<object>(storeName, destStateBackend);

        var startedAt = DateTimeOffset.UtcNow;
        await _messageBus.PublishStateMigrationStartedAsync(new StateMigrationStartedEvent
        {
            StoreName = storeName,
            SourceBackend = currentMigrationBackend.Value,
            DestinationBackend = destinationBackend,
            StartedAt = startedAt.UtcDateTime
        }, cancellationToken);

        var sw = Stopwatch.StartNew();
        var entriesMigrated = 0;

        try
        {
            var batchSize = _configuration.MigrationBatchSize;

            if (currentBackend == StateBackend.MySql || currentBackend == StateBackend.Sqlite)
            {
                // MySQL/SQLite enumeration: page through via IJsonQueryableStateStore
                // JsonQueryPaged returns key-value pairs needed for migration
                var jsonStore = _factory.GetJsonQueryableStore<object>(storeName);
                var offset = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    var pagedResult = await jsonStore.JsonQueryPagedAsync(
                        conditions: null,
                        offset: offset,
                        limit: batchSize,
                        sortBy: null,
                        cancellationToken: cancellationToken);

                    if (pagedResult.Items.Count == 0)
                    {
                        break;
                    }

                    var items = pagedResult.Items.Select(
                        kv => new KeyValuePair<string, object>(kv.Key, kv.Value));
                    await destStore.SaveBulkAsync(items, cancellationToken: cancellationToken);
                    entriesMigrated += pagedResult.Items.Count;

                    _logger.LogDebug(
                        "Migration batch at offset {Offset}: migrated {BatchCount} entries from {StoreName}",
                        offset, pagedResult.Items.Count, storeName);

                    if (pagedResult.Items.Count < batchSize)
                    {
                        break;
                    }

                    offset += batchSize;
                }
            }
            else if (currentBackend == StateBackend.Redis)
            {
                // Redis enumeration: SCAN via StackExchange.Redis IServer.KeysAsync
                var keyPrefix = _factory.GetKeyPrefix(storeName);
                var pattern = $"{keyPrefix}:*";
                var batch = new List<KeyValuePair<string, object>>();

                await foreach (var redisKey in _factory.ScanKeysAsync(pattern, batchSize).WithCancellation(cancellationToken))
                {
                    var fullKey = redisKey.ToString();

                    // Skip :meta suffix keys (metadata hashes, not value keys)
                    if (fullKey.EndsWith(":meta"))
                    {
                        continue;
                    }

                    // Strip prefix to get the logical key
                    var logicalKey = fullKey.StartsWith(keyPrefix + ":")
                        ? fullKey[(keyPrefix.Length + 1)..]
                        : fullKey;

                    var value = await sourceStore.GetAsync(logicalKey, cancellationToken);
                    if (value != null)
                    {
                        batch.Add(new KeyValuePair<string, object>(logicalKey, value));
                    }

                    if (batch.Count >= batchSize)
                    {
                        await destStore.SaveBulkAsync(batch, cancellationToken: cancellationToken);
                        entriesMigrated += batch.Count;

                        _logger.LogDebug(
                            "Migration batch: migrated {BatchCount} entries from {StoreName}",
                            batch.Count, storeName);

                        batch.Clear();
                    }
                }

                // Final partial batch
                if (batch.Count > 0)
                {
                    await destStore.SaveBulkAsync(batch, cancellationToken: cancellationToken);
                    entriesMigrated += batch.Count;
                }
            }

            sw.Stop();
            var completedAt = DateTimeOffset.UtcNow;

            await _messageBus.PublishStateMigrationCompletedAsync(new StateMigrationCompletedEvent
            {
                StoreName = storeName,
                SourceBackend = currentMigrationBackend.Value,
                DestinationBackend = destinationBackend,
                EntriesMigrated = entriesMigrated,
                DurationMs = sw.ElapsedMilliseconds,
                CompletedAt = completedAt.UtcDateTime
            }, cancellationToken);

            _logger.LogInformation(
                "Migration complete: {EntriesMigrated} entries from {Source} to {Destination} for store {StoreName} in {DurationMs}ms",
                entriesMigrated, currentMigrationBackend, destinationBackend, storeName, sw.ElapsedMilliseconds);

            return (StatusCodes.OK, new MigrateExecuteResponse
            {
                StoreName = storeName,
                EntriesMigrated = entriesMigrated,
                DurationMs = sw.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            var failedAt = DateTimeOffset.UtcNow;

            await _messageBus.PublishStateMigrationFailedAsync(new StateMigrationFailedEvent
            {
                StoreName = storeName,
                SourceBackend = currentMigrationBackend.Value,
                DestinationBackend = destinationBackend,
                EntriesProcessedBeforeFailure = entriesMigrated,
                Error = ex.Message,
                FailedAt = failedAt.UtcDateTime
            }, cancellationToken);

            _logger.LogError(ex,
                "Migration failed after {EntriesProcessed} entries for store {StoreName}: {Error}",
                entriesMigrated, storeName, ex.Message);

            throw; // Let generated controller handle the 500 response
        }
    }

    /// <summary>
    /// Verifies a migration by comparing key counts between backends.
    /// </summary>
    public virtual async Task<(StatusCodes, MigrateVerifyResponse?)> VerifyMigrationAsync(
        string storeName,
        MigrationBackend destinationBackend,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.state", "StateMigrationHelper.VerifyMigration");

        if (!_factory.HasStore(storeName))
        {
            return (StatusCodes.NotFound, null);
        }

        var currentBackend = _factory.GetBackendType(storeName);

        // Get source key count
        long? sourceKeyCount = await _factory.GetKeyCountAsync(storeName, cancellationToken);

        // Get destination key count — create a temporary store with the destination backend
        var destStateBackend = destinationBackend == MigrationBackend.Redis ? StateBackend.Redis : StateBackend.MySql;
        long? destKeyCount = null;

        if (destStateBackend == StateBackend.MySql || destStateBackend == StateBackend.Sqlite)
        {
            // MySQL/SQLite: can count via the factory (if a store exists on that backend)
            // Create temp store and count its entries
            var tempStore = _factory.CreateStoreWithBackend<object>(storeName, destStateBackend);
            if (tempStore is IQueryableStateStore<object> queryable)
            {
                destKeyCount = await queryable.CountAsync(cancellationToken: cancellationToken);
            }
        }
        else
        {
            // Redis destination: can't efficiently count
            destKeyCount = null;
        }

        bool? countsMatch = null;
        if (sourceKeyCount.HasValue && destKeyCount.HasValue)
        {
            countsMatch = sourceKeyCount.Value == destKeyCount.Value;
        }

        return (StatusCodes.OK, new MigrateVerifyResponse
        {
            StoreName = storeName,
            SourceKeyCount = sourceKeyCount.HasValue ? (int)sourceKeyCount.Value : null,
            DestinationKeyCount = destKeyCount.HasValue ? (int)destKeyCount.Value : null,
            CountsMatch = countsMatch
        });
    }

    /// <summary>
    /// Converts internal StateBackend to the API-facing MigrationBackend.
    /// Returns null for backends that don't support migration (Memory, Sqlite).
    /// </summary>
    private static MigrationBackend? ToMigrationBackend(StateBackend backend)
    {
        return backend switch
        {
            StateBackend.Redis => MigrationBackend.Redis,
            StateBackend.MySql => MigrationBackend.Mysql,
            // Sqlite-backed stores are MySQL-configured in state-stores.yaml, migrating "from Sqlite" is really "from MySQL config"
            StateBackend.Sqlite => MigrationBackend.Mysql,
            _ => null
        };
    }
}
