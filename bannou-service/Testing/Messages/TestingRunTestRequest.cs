using BeyondImmersion.BannouService.Controllers.Messages;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Testing.Messages;

/// <summary>
/// The request model for service API calls to `/testing/run-test`.
/// </summary>
[Serializable]
public class TestingRunTestRequest : ServiceRequest
{
    [JsonInclude]
    [JsonPropertyName("id")]
    public string? ID { get; set; }

    [JsonInclude]
    [JsonPropertyName("service")]
    public string? Service { get; set; }
}
