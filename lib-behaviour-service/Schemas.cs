namespace BeyondImmersion.BannouService.Behaviour;

public static class Schemas
{
    public static string Behaviour = @"{
  ""$schema"": ""http://json-schema.org/draft-07/schema#"",
  ""additionalProperties"": false,
  ""definitions"": {
    ""prerequisite"": {
      ""additionalProperties"": false,
      ""properties"": {
        ""name"": {
          ""description"": ""Name of the prerequisite."",
          ""type"": ""string""
        },
        ""quantity"": {
          ""default"": 1,
          ""description"": ""Quantity required (defaults to 1 if not provided)."",
          ""type"": ""integer""
        },
        ""type"": {
          ""description"": ""Type of the prerequisite."",
          ""enum"": [""item"", ""knowledge"", ""status"", ""tool"", ""other""],
          ""type"": ""string""
        }
      },
      ""required"": [""name"", ""type""],
      ""type"": ""object""
    }
  },
  ""properties"": {
    ""contextUpdates"": {
      ""additionalProperties"": false,
      ""description"": ""Context updates resulting from the behavior, allowing only specific value types."",
      ""patternProperties"": {
        "".*"": {
          ""oneOf"": [
            {""type"": ""array"", ""items"": {""type"": [""string"", ""integer"", ""number""]}},
            {""type"": ""integer""},
            {""type"": ""null""},
            {""type"": ""number""},
            {""type"": ""string""}
          ]
        }
      },
      ""type"": [""object"", ""null""]
    },
    ""description"": {
      ""description"": ""Description of the behavior."",
      ""type"": ""string""
    },
    ""name"": {
      ""description"": ""Unique name of the behavior."",
      ""type"": ""string""
    },
    ""outcome"": {
      ""description"": ""Description of the behavior's outcome."",
      ""type"": [""string"", ""null""]
    },
    ""prerequisites"": {
      ""description"": ""List of prerequisites required for the behavior."",
      ""items"": {""$ref"": ""#/definitions/prerequisite""},
      ""type"": ""array""
    },
    ""steps"": {
      ""description"": ""List of steps or behaviors to perform."",
      ""items"": {""$ref"": ""#""},
      ""type"": [""array"", ""null""]
    },
    ""type"": {
      ""description"": ""Type of the behavior (e.g., sequence, terminal, repeat)."",
      ""enum"": [""sequence"", ""terminal"", ""repeat""],
      ""type"": ""string""
    }
  },
  ""required"": [""description"", ""name"", ""type""],
  ""title"": ""AI Behavior Object"",
  ""type"": ""object""
}";



}
