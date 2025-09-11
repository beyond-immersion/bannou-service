namespace BeyondImmersion.BannouService;

/// <summary>
/// Application running states for service lifecycle management.
/// </summary>
public enum AppRunningStates
{
    /// <summary>
    /// Service is starting up and initializing.
    /// </summary>
    Starting,
    /// <summary>
    /// Service is running and ready to process requests.
    /// </summary>
    Running,
    /// <summary>
    /// Service has stopped and is no longer processing requests.
    /// </summary>
    Stopped
}

/// <summary>
/// Enumeration for common/supported HTTP methods.
/// (Not sure why there isn't an enum for this in .NET)
/// </summary>
public enum HttpMethodTypes
{
    /// <summary>
    /// HTTP GET method for retrieving data.
    /// </summary>
    GET = 0,
    /// <summary>
    /// HTTP POST method for creating resources.
    /// </summary>
    POST,
    /// <summary>
    /// HTTP PUT method for updating resources.
    /// </summary>
    PUT,
    /// <summary>
    /// HTTP DELETE method for removing resources.
    /// </summary>
    DELETE,

    // unsupported yet
    /// <summary>
    /// HTTP HEAD method (unsupported).
    /// </summary>
    HEAD,
    /// <summary>
    /// HTTP PATCH method (unsupported).
    /// </summary>
    PATCH,
    /// <summary>
    /// HTTP OPTIONS method (unsupported).
    /// </summary>
    OPTIONS
}

/// <summary>
/// An enumeration of possible API response codes
/// returned by this service.
/// </summary>
public enum StatusCodes
{
    /// <summary>
    /// Request succeeded (200).
    /// </summary>
    OK = 200,
    /// <summary>
    /// Request accepted for processing (202).
    /// </summary>
    Accepted = 202,
    /// <summary>
    /// Bad request due to invalid input (400).
    /// </summary>
    BadRequest = 400,
    /// <summary>
    /// Access forbidden (403).
    /// </summary>
    Forbidden = 403,
    /// <summary>
    /// Requested resource not found (404).
    /// </summary>
    NotFound = 404,
    /// <summary>
    /// Request conflicts with current resource state (409).
    /// </summary>
    Conflict = 409,
    /// <summary>
    /// Request payload too large (413).
    /// </summary>
    MessageTooLarge = 413,
    /// <summary>
    /// Internal server error (500).
    /// </summary>
    InternalServerError = 500
}
