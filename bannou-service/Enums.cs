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
/// An enumeration of possible API response codes returned by this service.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPORTANT:</b> Use this enum (<c>BeyondImmersion.BannouService.StatusCodes</c>),
/// NOT <c>Microsoft.AspNetCore.Http.StatusCodes</c> which is a static class with int constants.
/// </para>
/// <para>
/// All service methods return <c>(StatusCodes, TResponse?)</c> tuples.
/// See TENETS.md T8: Return Pattern for usage requirements.
/// </para>
/// <para>
/// <b>WARNING - DO NOT ADD NEW STATUS CODES WITHOUT EXPLICIT APPROVAL.</b>
/// These codes are hand-chosen per TENET T8 to minimize client complexity.
/// Adding new codes requires updating: Controller.liquid template, ErrorResponses.cs,
/// client SDK ResponseCodes, and all consuming code. If you think you need a new
/// status code, the answer is almost always "use an existing code with appropriate
/// payload content to indicate the specific condition."
/// </para>
/// </remarks>
public enum StatusCodes
{
    /// <summary>
    /// Request succeeded (200).
    /// </summary>
    OK = 200,
    /// <summary>
    /// Bad request due to invalid input (400).
    /// </summary>
    BadRequest = 400,
    /// <summary>
    /// Authentication required (401).
    /// </summary>
    Unauthorized = 401,
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
    /// Internal server error (500).
    /// </summary>
    InternalServerError = 500,
    /// <summary>
    /// Method not implemented (501).
    /// Used for stub services that are planned but not yet implemented.
    /// </summary>
    NotImplemented = 501,
    /// <summary>
    /// Service temporarily unavailable (503).
    /// Used when a required dependency or subsystem is not available.
    /// </summary>
    ServiceUnavailable = 503
}
