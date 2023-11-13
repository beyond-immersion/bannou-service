namespace BeyondImmersion.BannouService;

public enum AppRunningStates
{
    Starting,
    Running,
    Stopped
}

/// <summary>
/// Enumeration for common/supported HTTP methods.
///
/// (Not sure why there isn't an enum for this in .NET)
/// </summary>
public enum HttpMethodTypes
{
    GET = 0,
    POST,
    PUT,
    DELETE,

    // unsupported yet
    HEAD,
    PATCH,
    OPTIONS
}

/// <summary>
/// An enumeration of possible API response codes
/// returned by this service.
/// </summary>
public enum StatusCodes
{
    OK = 200,
    Accepted = 202,
    BadRequest = 400,
    Forbidden = 403,
    NotFound = 404,
    Conflict = 409,
    MessageTooLarge = 413,
    InternalServerError = 500
}
