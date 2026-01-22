#nullable enable

using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Utilities;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Contract;

/// <summary>
/// Partial class implementing clause validation functionality for ContractService.
/// </summary>
/// <remarks>
/// <para>
/// Clause validation uses lazy checking with configurable staleness to avoid
/// excessive API calls. The default staleness threshold is 15 seconds.
/// </para>
/// <para>
/// Validation outcomes:
/// - Success: All conditions passed
/// - PermanentFailure: Clause condition violated, contract may need breach handling
/// - TransientFailure: Temporary error, will retry on next validation
/// </para>
/// </remarks>
public partial class ContractService
{
    /// <summary>
    /// Cache of recent clause validation results with timestamps.
    /// Key: "{contractId}:{clauseId}"
    /// </summary>
    private readonly ConcurrentDictionary<string, CachedValidationResult> _validationCache = new();

    /// <summary>
    /// Default staleness threshold for cached validation results (15 seconds).
    /// </summary>
    private static readonly TimeSpan DefaultStalenessThreshold = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Validates a contract clause by executing its prebound API and checking the response.
    /// Uses lazy validation with caching to avoid excessive API calls.
    /// </summary>
    /// <param name="contractId">The contract instance ID.</param>
    /// <param name="clause">The clause to validate (must have a prebound API with response validation).</param>
    /// <param name="context">Variable context for template substitution.</param>
    /// <param name="stalenessThreshold">How old a cached result can be before revalidation. Defaults to 15 seconds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result with outcome and details.</returns>
    public async Task<ClauseValidationResult> ValidateClauseAsync(
        string contractId,
        ContractClause clause,
        IReadOnlyDictionary<string, object?> context,
        TimeSpan? stalenessThreshold = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contractId);
        ArgumentNullException.ThrowIfNull(clause);
        ArgumentNullException.ThrowIfNull(context);

        var threshold = stalenessThreshold ?? DefaultStalenessThreshold;
        var cacheKey = $"{contractId}:{clause.ClauseId}";

        // Check cache first
        if (_validationCache.TryGetValue(cacheKey, out var cached))
        {
            var age = DateTimeOffset.UtcNow - cached.Timestamp;
            if (age < threshold)
            {
                _logger.LogDebug("Using cached validation result for clause {ClauseId} (age: {Age}ms)",
                    clause.ClauseId, age.TotalMilliseconds);
                return cached.Result;
            }
        }

        // Execute validation
        var result = await ExecuteClauseValidationAsync(contractId, clause, context, ct);

        // Cache the result
        _validationCache[cacheKey] = new CachedValidationResult
        {
            Result = result,
            Timestamp = DateTimeOffset.UtcNow
        };

        return result;
    }

    /// <summary>
    /// Validates all clauses in a contract that have validation APIs.
    /// </summary>
    /// <param name="contractId">The contract instance ID.</param>
    /// <param name="clauses">The clauses to validate.</param>
    /// <param name="context">Variable context for template substitution.</param>
    /// <param name="stalenessThreshold">How old cached results can be.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of validation results for clauses that have validation APIs.</returns>
    public async Task<IReadOnlyList<ClauseValidationResult>> ValidateAllClausesAsync(
        string contractId,
        IEnumerable<ContractClause> clauses,
        IReadOnlyDictionary<string, object?> context,
        TimeSpan? stalenessThreshold = null,
        CancellationToken ct = default)
    {
        var results = new List<ClauseValidationResult>();

        foreach (var clause in clauses)
        {
            // Skip clauses without validation APIs
            if (clause.ValidationApi?.ResponseValidation == null)
            {
                continue;
            }

            var result = await ValidateClauseAsync(contractId, clause, context, stalenessThreshold, ct);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Clears cached validation results for a contract.
    /// Call this when contract state changes significantly.
    /// </summary>
    /// <param name="contractId">The contract instance ID.</param>
    public void ClearValidationCache(string contractId)
    {
        var keysToRemove = _validationCache.Keys
            .Where(k => k.StartsWith($"{contractId}:", StringComparison.Ordinal))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _validationCache.TryRemove(key, out _);
        }

        _logger.LogDebug("Cleared {Count} cached validation results for contract {ContractId}",
            keysToRemove.Count, contractId);
    }

    /// <summary>
    /// Executes the actual clause validation.
    /// </summary>
    private async Task<ClauseValidationResult> ExecuteClauseValidationAsync(
        string contractId,
        ContractClause clause,
        IReadOnlyDictionary<string, object?> context,
        CancellationToken ct)
    {
        var validationApi = clause.ValidationApi;
        if (validationApi == null)
        {
            return new ClauseValidationResult
            {
                ContractId = contractId,
                ClauseId = clause.ClauseId,
                Outcome = ValidationOutcome.Success,
                Message = "No validation API configured"
            };
        }

        _logger.LogDebug("Validating clause {ClauseId} for contract {ContractId}",
            clause.ClauseId, contractId);

        try
        {
            // Convert to PreboundApiDefinition
            var apiDefinition = new PreboundApiDefinition
            {
                ServiceName = validationApi.ServiceName,
                Endpoint = validationApi.Endpoint,
                PayloadTemplate = validationApi.PayloadTemplate,
                Description = validationApi.Description,
                ExecutionMode = ConvertExecutionMode(validationApi.ExecutionMode)
            };

            // Execute the API
            var apiResult = await _navigator.ExecutePreboundApiAsync(apiDefinition, context, ct);

            // Check for substitution failure
            if (!apiResult.SubstitutionSucceeded)
            {
                _logger.LogWarning("Template substitution failed for clause {ClauseId}: {Error}",
                    clause.ClauseId, apiResult.SubstitutionError);

                return new ClauseValidationResult
                {
                    ContractId = contractId,
                    ClauseId = clause.ClauseId,
                    Outcome = ValidationOutcome.PermanentFailure,
                    Message = $"Template substitution failed: {apiResult.SubstitutionError}",
                    ApiStatusCode = null
                };
            }

            // Validate the response
            var validationResult = ResponseValidator.Validate(
                apiResult.Result.StatusCode,
                apiResult.Result.ResponseBody,
                validationApi.ResponseValidation);

            _logger.LogDebug("Clause {ClauseId} validation result: {Outcome}",
                clause.ClauseId, validationResult.Outcome);

            return new ClauseValidationResult
            {
                ContractId = contractId,
                ClauseId = clause.ClauseId,
                Outcome = validationResult.Outcome,
                Message = validationResult.FailureReason,
                ApiStatusCode = apiResult.Result.StatusCode,
                ApiResponseBody = apiResult.Result.ResponseBody,
                FailedCondition = validationResult.FailedCondition
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating clause {ClauseId} for contract {ContractId}",
                clause.ClauseId, contractId);

            return new ClauseValidationResult
            {
                ContractId = contractId,
                ClauseId = clause.ClauseId,
                Outcome = ValidationOutcome.TransientFailure,
                Message = $"Validation error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Converts the schema execution mode to the ServiceClients execution mode.
    /// </summary>
    private static ExecutionMode ConvertExecutionMode(PreboundApiExecutionMode? mode)
    {
        return mode switch
        {
            PreboundApiExecutionMode.Sync => ExecutionMode.Sync,
            PreboundApiExecutionMode.Async => ExecutionMode.Async,
            PreboundApiExecutionMode.Fire_and_forget => ExecutionMode.FireAndForget,
            _ => ExecutionMode.Sync
        };
    }

    /// <summary>
    /// Cached validation result with timestamp.
    /// </summary>
    private sealed class CachedValidationResult
    {
        public required ClauseValidationResult Result { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
    }
}

/// <summary>
/// Result of validating a contract clause.
/// </summary>
public class ClauseValidationResult
{
    /// <summary>
    /// The contract instance ID.
    /// </summary>
    public required string ContractId { get; init; }

    /// <summary>
    /// The clause ID that was validated.
    /// </summary>
    public required string ClauseId { get; init; }

    /// <summary>
    /// The validation outcome.
    /// </summary>
    public required ValidationOutcome Outcome { get; init; }

    /// <summary>
    /// Description of the result (failure reason if applicable).
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// The HTTP status code from the validation API (if executed).
    /// </summary>
    public int? ApiStatusCode { get; init; }

    /// <summary>
    /// The response body from the validation API (if executed).
    /// </summary>
    public string? ApiResponseBody { get; init; }

    /// <summary>
    /// The specific condition that failed (if applicable).
    /// </summary>
    public ValidationCondition? FailedCondition { get; init; }

    /// <summary>
    /// Whether the validation passed (outcome is Success).
    /// </summary>
    public bool IsSuccess => Outcome == ValidationOutcome.Success;

    /// <summary>
    /// Whether the validation failed permanently (contract may be breached).
    /// </summary>
    public bool IsPermanentFailure => Outcome == ValidationOutcome.PermanentFailure;

    /// <summary>
    /// Whether the validation failed transiently (should retry later).
    /// </summary>
    public bool IsTransientFailure => Outcome == ValidationOutcome.TransientFailure;
}

/// <summary>
/// Represents a contract clause with validation API.
/// </summary>
/// <remarks>
/// This is a local model used by clause validation.
/// The actual clause data comes from the contract schema.
/// </remarks>
public class ContractClause
{
    /// <summary>
    /// Unique identifier for this clause within the contract.
    /// </summary>
    public required string ClauseId { get; init; }

    /// <summary>
    /// Human-readable description of the clause.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional validation API to check clause conditions.
    /// </summary>
    public PreboundApi? ValidationApi { get; init; }
}
