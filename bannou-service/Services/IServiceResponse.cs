namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// The interface all response models from service endpoints should implement.
/// </summary>
public interface IServiceResponse
{
    /// <summary>
    /// Response code (200|400|403|500).
    /// </summary>
    int Code { get; }

    /// <summary>
    /// Optional message to return to the client.
    /// </summary>
    string? Message { get; }

    /// <summary>
    /// Whether the response has data, or can be discarded.
    /// </summary>
    bool HasData();

    /// <summary>
    /// Set fixed service response, based on a given response code.
    /// </summary>
    void SetResponse(ResponseCodes responseCode, string? message);
}
