namespace BeyondImmersion.BannouService.Testing.Messages;

/// <summary>
/// The request model for service API calls to `/testing/run-test`.
/// </summary>
[JsonObject]
public class TestingRunTestRequest : ApiRequest
{
    [JsonProperty("id")]
    public string? ID { get; set; }

    [JsonProperty("service")]
    public string? Service { get; set; }
}
