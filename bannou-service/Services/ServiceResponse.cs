namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// The standard service function response.
/// Adoption for all service functions means API controllers
/// can use switches to handle a wider range of standard status
/// reports than just "no object returned", without needing
/// additional out parameters.
/// </summary>
public struct ServiceResponse
{
    /// <inheritdoc/>
    public StatusCodes StatusCode { get; set; }

    /// <inheritdoc/>
    public ServiceResponse(StatusCodes statusCode)
    {
        StatusCode = statusCode;
    }
}
