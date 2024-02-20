namespace BeyondImmersion.BannouService.Behaviour;

public static class Schemas
{
    public const string Behaviour = /*lang=json,strict*/ @"{
    ""$schema"": ""http://json-schema.org/draft-07/schema#"",
    ""additionalProperties"": true,
    ""definitions"": {
        ""prerequisite"": {
            ""additionalProperties"": true,
            ""properties"": {
                ""name"": {
                    ""description"": ""Name of the prerequisite."",
                    ""type"": ""string"",
                    ""minLength"": 1
                },
                ""value"": {
                    ""default"": 1,
                    ""description"": ""Optional value/quantity needed (defaults to 1 if not provided)."",
                    ""type"": ""integer""
                },
                ""type"": {
                    ""description"": ""Type of the prerequisite."",
                    ""enum"": [""item"", ""tool"", ""skill"", ""milestone"", ""knowledge"", ""status"", ""other""],
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
                        {""type"": ""array"", ""items"": {""type"": [""string"", ""boolean"", ""integer"", ""number""]}},
                        {""type"": ""integer""},
                        {""type"": ""boolean""},
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
            ""type"": ""string"",
            ""minLength"": 1
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
            ""type"": [""array"", ""null""],
            ""minItems"": 1
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
