namespace BeyondImmersion.BannouService.Connect.Protocol;

/// <summary>
/// Meta endpoint types encoded in the Channel field when Meta flag is set.
/// When a message has the Meta flag (0x80), the Channel field specifies which
/// type of metadata to return about the endpoint.
/// </summary>
public enum MetaType : ushort
{
    /// <summary>
    /// Human-readable endpoint description including summary, tags, and deprecated status.
    /// </summary>
    EndpointInfo = 0,

    /// <summary>
    /// JSON Schema for the request body.
    /// </summary>
    RequestSchema = 1,

    /// <summary>
    /// JSON Schema for the response body.
    /// </summary>
    ResponseSchema = 2,

    /// <summary>
    /// Complete endpoint schema combining info, request schema, and response schema.
    /// </summary>
    FullSchema = 3
}
