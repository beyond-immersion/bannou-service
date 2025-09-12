namespace BeyondImmersion.BannouService.Testing.Messages;

/// <summary>
/// The request model for service API calls to `/testing/run-test`.
/// </summary>
[JsonObject]
public class TestingRunTestRequest : ApiRequest
{
    /// <inheritdoc/>
    [JsonProperty("id")]
    public string? ID { get; set; }

    /// <inheritdoc/>
    [JsonProperty("service")]
    public string? Service { get; set; }
}
