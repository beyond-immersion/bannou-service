using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Helpers;

/// <summary>
/// Standardized error response factory methods for consistent error handling across services.
/// Provides logging, status code assignment, and structured error responses.
/// </summary>
public static class ErrorResponses
{
    /// <summary>
    /// Returns a NotFound response with standardized logging.
    /// </summary>
    /// <typeparam name="T">Response type (nullable).</typeparam>
    /// <param name="logger">Logger instance for recording the not found event.</param>
    /// <param name="entityType">Type of entity not found (e.g., "Account", "Session").</param>
    /// <param name="entityId">Identifier of the missing entity.</param>
    /// <returns>Tuple with NotFound status code and null response.</returns>
    public static (StatusCodes, T?) NotFound<T>(ILogger logger, string entityType, string entityId)
        where T : class
    {
        logger.LogInformation("{EntityType} not found: {EntityId}", entityType, entityId);
        return (StatusCodes.NotFound, null);
    }

    /// <summary>
    /// Returns a NotFound response with standardized logging using a Guid identifier.
    /// </summary>
    /// <typeparam name="T">Response type (nullable).</typeparam>
    /// <param name="logger">Logger instance for recording the not found event.</param>
    /// <param name="entityType">Type of entity not found (e.g., "Account", "Session").</param>
    /// <param name="entityId">Guid identifier of the missing entity.</param>
    /// <returns>Tuple with NotFound status code and null response.</returns>
    public static (StatusCodes, T?) NotFound<T>(ILogger logger, string entityType, Guid entityId)
        where T : class
    {
        return NotFound<T>(logger, entityType, entityId.ToString());
    }

    /// <summary>
    /// Returns a BadRequest response with standardized logging.
    /// </summary>
    /// <typeparam name="T">Response type (nullable).</typeparam>
    /// <param name="logger">Logger instance for recording the validation failure.</param>
    /// <param name="message">Description of what validation failed.</param>
    /// <returns>Tuple with BadRequest status code and null response.</returns>
    public static (StatusCodes, T?) BadRequest<T>(ILogger logger, string message)
        where T : class
    {
        logger.LogWarning("Bad request: {Message}", message);
        return (StatusCodes.BadRequest, null);
    }

    /// <summary>
    /// Returns a Conflict response with standardized logging.
    /// </summary>
    /// <typeparam name="T">Response type (nullable).</typeparam>
    /// <param name="logger">Logger instance for recording the conflict.</param>
    /// <param name="entityType">Type of entity with conflict.</param>
    /// <param name="conflictReason">Description of the conflict.</param>
    /// <returns>Tuple with Conflict status code and null response.</returns>
    public static (StatusCodes, T?) Conflict<T>(ILogger logger, string entityType, string conflictReason)
        where T : class
    {
        logger.LogWarning("{EntityType} conflict: {ConflictReason}", entityType, conflictReason);
        return (StatusCodes.Conflict, null);
    }

    /// <summary>
    /// Returns an Unauthorized response with standardized logging.
    /// </summary>
    /// <typeparam name="T">Response type (nullable).</typeparam>
    /// <param name="logger">Logger instance for recording the auth failure.</param>
    /// <param name="reason">Description of why authorization failed.</param>
    /// <returns>Tuple with Unauthorized status code and null response.</returns>
    public static (StatusCodes, T?) Unauthorized<T>(ILogger logger, string reason)
        where T : class
    {
        logger.LogWarning("Unauthorized: {Reason}", reason);
        return (StatusCodes.Unauthorized, null);
    }

    /// <summary>
    /// Returns a Forbidden response with standardized logging.
    /// </summary>
    /// <typeparam name="T">Response type (nullable).</typeparam>
    /// <param name="logger">Logger instance for recording the forbidden access attempt.</param>
    /// <param name="reason">Description of why access is forbidden.</param>
    /// <returns>Tuple with Forbidden status code and null response.</returns>
    public static (StatusCodes, T?) Forbidden<T>(ILogger logger, string reason)
        where T : class
    {
        logger.LogWarning("Forbidden: {Reason}", reason);
        return (StatusCodes.Forbidden, null);
    }

    /// <summary>
    /// Returns an InternalServerError response with standardized logging.
    /// </summary>
    /// <typeparam name="T">Response type (nullable).</typeparam>
    /// <param name="logger">Logger instance for recording the error.</param>
    /// <param name="ex">Exception that caused the error.</param>
    /// <param name="operation">Description of the operation that failed.</param>
    /// <returns>Tuple with InternalServerError status code and null response.</returns>
    public static (StatusCodes, T?) ServiceError<T>(ILogger logger, Exception ex, string operation)
        where T : class
    {
        logger.LogError(ex, "Error during {Operation}", operation);
        return (StatusCodes.InternalServerError, null);
    }

    /// <summary>
    /// Returns an InternalServerError response with standardized logging (no exception).
    /// </summary>
    /// <typeparam name="T">Response type (nullable).</typeparam>
    /// <param name="logger">Logger instance for recording the error.</param>
    /// <param name="errorMessage">Description of the error.</param>
    /// <param name="operation">Description of the operation that failed.</param>
    /// <returns>Tuple with InternalServerError status code and null response.</returns>
    public static (StatusCodes, T?) ServiceError<T>(ILogger logger, string errorMessage, string operation)
        where T : class
    {
        logger.LogError("Error during {Operation}: {ErrorMessage}", operation, errorMessage);
        return (StatusCodes.InternalServerError, null);
    }

    /// <summary>
    /// Maps an ApiException status code to the appropriate StatusCodes enum.
    /// </summary>
    /// <param name="httpStatusCode">HTTP status code from ApiException.</param>
    /// <returns>Corresponding StatusCodes enum value.</returns>
    public static StatusCodes MapStatusCode(int httpStatusCode)
    {
        return httpStatusCode switch
        {
            200 => StatusCodes.OK,
            201 => StatusCodes.OK, // Created maps to OK - all success codes are OK
            202 => StatusCodes.OK, // Accepted maps to OK - payload indicates async status
            204 => StatusCodes.OK, // NoContent maps to OK - null payload indicates no content
            400 => StatusCodes.BadRequest,
            401 => StatusCodes.Unauthorized,
            403 => StatusCodes.Forbidden,
            404 => StatusCodes.NotFound,
            409 => StatusCodes.Conflict,
            413 => StatusCodes.BadRequest, // MessageTooLarge maps to BadRequest - client error
            422 => StatusCodes.BadRequest, // UnprocessableEntity maps to BadRequest
            429 => StatusCodes.BadRequest, // TooManyRequests maps to BadRequest - client should slow down
            _ => StatusCodes.InternalServerError
        };
    }

    /// <summary>
    /// Handles an ApiException and returns appropriate status code with logging.
    /// </summary>
    /// <typeparam name="T">Response type (nullable).</typeparam>
    /// <param name="logger">Logger instance for recording the error.</param>
    /// <param name="ex">ApiException from a service client call.</param>
    /// <param name="serviceName">Name of the service that was called.</param>
    /// <param name="operation">Description of the operation that failed.</param>
    /// <returns>Tuple with mapped status code and null response.</returns>
    public static (StatusCodes, T?) FromApiException<T>(
        ILogger logger,
        ApiException ex,
        string serviceName,
        string operation)
        where T : class
    {
        var statusCode = MapStatusCode(ex.StatusCode);

        if (ex.StatusCode == 404)
        {
            logger.LogWarning("{Service} returned NotFound during {Operation}", serviceName, operation);
        }
        else if (ex.StatusCode >= 500)
        {
            logger.LogError(ex, "{Service} returned {StatusCode} during {Operation}",
                serviceName, ex.StatusCode, operation);
        }
        else
        {
            logger.LogWarning("{Service} returned {StatusCode} during {Operation}: {Message}",
                serviceName, ex.StatusCode, operation, ex.Message);
        }

        return (statusCode, null);
    }
}
