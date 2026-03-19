using Xunit;

namespace BeyondImmersion.Bannou.Core.Tests;

/// <summary>
/// Comprehensive tests for <see cref="ResponseTransformer"/>.
/// Validates the conditional transformation engine that converts raw API responses
/// into caller-defined results based on declarative rules.
/// </summary>
public class ResponseTransformerTests
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Pass-Through (No Transformation)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Null transformation = raw response passes through unchanged.
    /// </summary>
    [Fact]
    public void Transform_NullTransformation_PassesThrough()
    {
        var result = ResponseTransformer.Transform(200, """{"ok": true}""", null);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("""{"ok": true}""", result.Payload);
        Assert.Equal(TransformationOutcome.PassThrough, result.Outcome);
        Assert.True(result.IsSuccess);
        Assert.Null(result.MatchedRuleDescription);
    }

    /// <summary>
    /// Empty rules list = raw response passes through unchanged.
    /// </summary>
    [Fact]
    public void Transform_EmptyRules_PassesThrough()
    {
        var transformation = new ResponseTransformation { Rules = new List<TransformationRule>() };

        var result = ResponseTransformer.Transform(404, null, transformation);

        Assert.Equal(404, result.StatusCode);
        Assert.Null(result.Payload);
        Assert.Equal(TransformationOutcome.PassThrough, result.Outcome);
        Assert.False(result.IsSuccess);
    }

    /// <summary>
    /// No rules match = raw response passes through unchanged.
    /// </summary>
    [Fact]
    public void Transform_NoRulesMatch_PassesThrough()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.StatusCodeIn, StatusCodes = new List<int> { 404 } }
                    },
                    StatusCode = 200,
                    Payload = """{"exists": false}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(200, """{"id": "abc"}""", transformation);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("""{"id": "abc"}""", result.Payload);
        Assert.Equal(TransformationOutcome.PassThrough, result.Outcome);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Transient Failure Detection
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Default transient status codes (408, 429, 502, 503, 504) are detected.
    /// </summary>
    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void Transform_DefaultTransientStatusCode_ReturnsTransientFailure(int statusCode)
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new() { StatusCode = 200, Payload = """{"should": "not reach"}""" }
            }
        };

        var result = ResponseTransformer.Transform(statusCode, """{"error": "timeout"}""", transformation);

        Assert.Equal(statusCode, result.StatusCode);
        Assert.Null(result.Payload);
        Assert.Equal(TransformationOutcome.TransientFailure, result.Outcome);
        Assert.False(result.IsSuccess);
    }

    /// <summary>
    /// Custom transient status codes override the defaults.
    /// </summary>
    [Fact]
    public void Transform_CustomTransientStatusCodes_OverrideDefaults()
    {
        var transformation = new ResponseTransformation
        {
            TransientFailureStatusCodes = new List<int> { 503, 999 }
        };

        // 503 is in custom list — transient
        var result503 = ResponseTransformer.Transform(503, null, transformation);
        Assert.Equal(TransformationOutcome.TransientFailure, result503.Outcome);

        // 999 is in custom list — transient
        var result999 = ResponseTransformer.Transform(999, null, transformation);
        Assert.Equal(TransformationOutcome.TransientFailure, result999.Outcome);

        // 429 is NOT in custom list (was in defaults) — passes through
        var result429 = ResponseTransformer.Transform(429, null, transformation);
        Assert.Equal(TransformationOutcome.PassThrough, result429.Outcome);
        Assert.Equal(429, result429.StatusCode);
    }

    /// <summary>
    /// Transient check runs BEFORE rules — rules are never evaluated for transient codes.
    /// </summary>
    [Fact]
    public void Transform_TransientCheckRunsBeforeRules()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                // Unconditional rule that would match anything
                new() { StatusCode = 200, Payload = """{"overridden": true}""" }
            }
        };

        var result = ResponseTransformer.Transform(502, """{"error": "bad gateway"}""", transformation);

        Assert.Equal(TransformationOutcome.TransientFailure, result.Outcome);
        Assert.Null(result.Payload);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Status Code Condition
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// StatusCodeIn condition matches when the response code is in the list.
    /// </summary>
    [Fact]
    public void Transform_StatusCodeInCondition_MatchesWhenInList()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.StatusCodeIn, StatusCodes = new List<int> { 200, 201 } }
                    },
                    StatusCode = 200,
                    Payload = """{"success": true}""",
                    Description = "Success rule"
                }
            }
        };

        var result = ResponseTransformer.Transform(200, """{"id": "abc"}""", transformation);

        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("""{"success": true}""", result.Payload);
        Assert.Equal("Success rule", result.MatchedRuleDescription);
    }

    /// <summary>
    /// StatusCodeIn condition does not match when the response code is not in the list.
    /// </summary>
    [Fact]
    public void Transform_StatusCodeInCondition_DoesNotMatchWhenNotInList()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.StatusCodeIn, StatusCodes = new List<int> { 200 } }
                    },
                    StatusCode = 200,
                    Payload = """{"matched": true}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(404, null, transformation);

        Assert.Equal(TransformationOutcome.PassThrough, result.Outcome);
        Assert.Equal(404, result.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // JSON Path Equals / NotEquals
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// JsonPathEquals matches when the extracted value equals the expected value (case-insensitive).
    /// </summary>
    [Fact]
    public void Transform_JsonPathEquals_MatchesCaseInsensitive()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathEquals, JsonPath = "$.species", ExpectedValue = "Elf" }
                    },
                    StatusCode = 200,
                    Payload = """{"resistance": "fire", "modifier": 1.5}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(200, """{"species": "elf", "id": "abc"}""", transformation);

        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
        Assert.Equal("""{"resistance": "fire", "modifier": 1.5}""", result.Payload);
    }

    /// <summary>
    /// JsonPathNotEquals matches when the value differs.
    /// </summary>
    [Fact]
    public void Transform_JsonPathNotEquals_MatchesWhenDifferent()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathNotEquals, JsonPath = "$.status", ExpectedValue = "active" }
                    },
                    StatusCode = 400,
                    Payload = """{"error": "entity not active"}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(200, """{"status": "deprecated"}""", transformation);

        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("""{"error": "entity not active"}""", result.Payload);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // JSON Path Exists / NotExists
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// JsonPathExists matches when the path is present in the response.
    /// </summary>
    [Fact]
    public void Transform_JsonPathExists_MatchesWhenPresent()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathExists, JsonPath = "$.error" }
                    },
                    StatusCode = 500,
                    Payload = """{"failed": true}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(200, """{"error": "something broke"}""", transformation);

        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
        Assert.Equal(500, result.StatusCode);
    }

    /// <summary>
    /// JsonPathNotExists matches when the path is absent.
    /// </summary>
    [Fact]
    public void Transform_JsonPathNotExists_MatchesWhenAbsent()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathNotExists, JsonPath = "$.token" }
                    },
                    StatusCode = 401,
                    Payload = """{"error": "no token in response"}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(200, """{"user": "bob"}""", transformation);

        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
        Assert.Equal(401, result.StatusCode);
    }

    /// <summary>
    /// JsonPathExists returns false for null response body.
    /// </summary>
    [Fact]
    public void Transform_JsonPathExists_NullBody_DoesNotMatch()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathExists, JsonPath = "$.anything" }
                    },
                    StatusCode = 200,
                    Payload = """{"found": true}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(200, null, transformation);

        Assert.Equal(TransformationOutcome.PassThrough, result.Outcome);
    }

    /// <summary>
    /// JsonPathNotExists returns true for null response body (path definitely doesn't exist).
    /// </summary>
    [Fact]
    public void Transform_JsonPathNotExists_NullBody_Matches()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathNotExists, JsonPath = "$.anything" }
                    },
                    StatusCode = 200,
                    Payload = """{"empty": true}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(200, null, transformation);

        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // JSON Path Numeric Comparisons
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// JsonPathGreaterThan matches when the numeric value exceeds the threshold.
    /// </summary>
    [Fact]
    public void Transform_JsonPathGreaterThan_MatchesAboveThreshold()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathGreaterThan, JsonPath = "$.balance", ExpectedValue = "10000" }
                    },
                    StatusCode = 200,
                    Payload = """{"tier": "wealthy"}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(200, """{"balance": 15000}""", transformation);

        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
        Assert.Equal("""{"tier": "wealthy"}""", result.Payload);
    }

    /// <summary>
    /// JsonPathLessThan matches when the numeric value is below the threshold.
    /// </summary>
    [Fact]
    public void Transform_JsonPathLessThan_MatchesBelowThreshold()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathLessThan, JsonPath = "$.health", ExpectedValue = "20" }
                    },
                    StatusCode = 200,
                    Payload = """{"critical": true}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(200, """{"health": 15}""", transformation);

        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
        Assert.Equal("""{"critical": true}""", result.Payload);
    }

    /// <summary>
    /// Numeric comparison with non-numeric value does not match (graceful failure).
    /// </summary>
    [Fact]
    public void Transform_NumericComparison_NonNumericValue_DoesNotMatch()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathGreaterThan, JsonPath = "$.name", ExpectedValue = "100" }
                    },
                    StatusCode = 200,
                    Payload = """{"matched": true}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(200, """{"name": "bob"}""", transformation);

        Assert.Equal(TransformationOutcome.PassThrough, result.Outcome);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // JSON Path Contains
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// JsonPathContains matches when the string value contains the expected substring.
    /// </summary>
    [Fact]
    public void Transform_JsonPathContains_MatchesSubstring()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathContains, JsonPath = "$.description", ExpectedValue = "poison" }
                    },
                    StatusCode = 200,
                    Payload = """{"hasPoison": true}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(200, """{"description": "A vial of deadly poison"}""", transformation);

        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
        Assert.Equal("""{"hasPoison": true}""", result.Payload);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AND Logic (Multiple Conditions)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Multiple conditions in a rule use AND logic — all must match.
    /// </summary>
    [Fact]
    public void Transform_MultipleConditions_AllMustMatch()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.StatusCodeIn, StatusCodes = new List<int> { 200 } },
                        new() { Type = TransformationConditionType.JsonPathEquals, JsonPath = "$.species", ExpectedValue = "elf" }
                    },
                    StatusCode = 200,
                    Payload = """{"matched": "both conditions"}"""
                }
            }
        };

        // Both conditions match
        var resultBoth = ResponseTransformer.Transform(200, """{"species": "elf"}""", transformation);
        Assert.Equal(TransformationOutcome.Transformed, resultBoth.Outcome);

        // Status matches but species doesn't
        var resultWrongSpecies = ResponseTransformer.Transform(200, """{"species": "dwarf"}""", transformation);
        Assert.Equal(TransformationOutcome.PassThrough, resultWrongSpecies.Outcome);

        // Species matches but status doesn't
        var resultWrongStatus = ResponseTransformer.Transform(404, """{"species": "elf"}""", transformation);
        Assert.Equal(TransformationOutcome.PassThrough, resultWrongStatus.Outcome);
    }

    /// <summary>
    /// Empty conditions list = unconditional match (always fires).
    /// </summary>
    [Fact]
    public void Transform_EmptyConditions_UnconditionalMatch()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>(),
                    StatusCode = 200,
                    Payload = """{"default": true}""",
                    Description = "Catch-all rule"
                }
            }
        };

        var result = ResponseTransformer.Transform(500, """{"error": "boom"}""", transformation);

        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("""{"default": true}""", result.Payload);
        Assert.Equal("Catch-all rule", result.MatchedRuleDescription);
    }

    /// <summary>
    /// Null conditions list = unconditional match (same as empty).
    /// </summary>
    [Fact]
    public void Transform_NullConditions_UnconditionalMatch()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = null,
                    StatusCode = 200,
                    Payload = """{"fallback": true}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(404, null, transformation);

        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
        Assert.Equal(200, result.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Rule Ordering (First Match Wins)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// First matching rule wins — subsequent rules are not evaluated.
    /// </summary>
    [Fact]
    public void Transform_FirstMatchingRuleWins()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.StatusCodeIn, StatusCodes = new List<int> { 200 } }
                    },
                    StatusCode = 200,
                    Payload = """{"rule": "first"}""",
                    Description = "First rule"
                },
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.StatusCodeIn, StatusCodes = new List<int> { 200 } }
                    },
                    StatusCode = 200,
                    Payload = """{"rule": "second"}""",
                    Description = "Second rule"
                }
            }
        };

        var result = ResponseTransformer.Transform(200, """{}""", transformation);

        Assert.Equal("""{"rule": "first"}""", result.Payload);
        Assert.Equal("First rule", result.MatchedRuleDescription);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Null Payload Pass-Through
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Rule with null payload passes through the raw response body.
    /// </summary>
    [Fact]
    public void Transform_RuleWithNullPayload_PassesThroughRawBody()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.StatusCodeIn, StatusCodes = new List<int> { 200 } }
                    },
                    StatusCode = 200,
                    Payload = null,
                    Description = "Confirm success, pass through body"
                }
            }
        };

        var rawBody = """{"id": "abc123", "fullData": true}""";
        var result = ResponseTransformer.Transform(200, rawBody, transformation);

        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(rawBody, result.Payload);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Real-World Scenarios
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Species branching: different payloads based on species field value.
    /// The prebound API creator knows "call character/get, and depending on species,
    /// give back different modifier data for the consuming system."
    /// </summary>
    [Fact]
    public void Scenario_SpeciesBranching_ReturnsContextualPayload()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathEquals, JsonPath = "$.speciesCode", ExpectedValue = "elf" }
                    },
                    StatusCode = 200,
                    Payload = """{"resistance": "fire", "modifier": 1.5}""",
                    Description = "Elf species modifiers"
                },
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathEquals, JsonPath = "$.speciesCode", ExpectedValue = "dwarf" }
                    },
                    StatusCode = 200,
                    Payload = """{"resistance": "poison", "modifier": 1.2}""",
                    Description = "Dwarf species modifiers"
                },
                new()
                {
                    // Catch-all: unknown species
                    StatusCode = 200,
                    Payload = """{"resistance": "none", "modifier": 1.0}""",
                    Description = "Default species modifiers"
                }
            }
        };

        var elfResult = ResponseTransformer.Transform(200, """{"speciesCode": "elf", "name": "Galadriel"}""", transformation);
        Assert.Equal("""{"resistance": "fire", "modifier": 1.5}""", elfResult.Payload);
        Assert.Equal("Elf species modifiers", elfResult.MatchedRuleDescription);

        var dwarfResult = ResponseTransformer.Transform(200, """{"speciesCode": "dwarf", "name": "Gimli"}""", transformation);
        Assert.Equal("""{"resistance": "poison", "modifier": 1.2}""", dwarfResult.Payload);

        var humanResult = ResponseTransformer.Transform(200, """{"speciesCode": "human", "name": "Aragorn"}""", transformation);
        Assert.Equal("""{"resistance": "none", "modifier": 1.0}""", humanResult.Payload);
        Assert.Equal("Default species modifiers", humanResult.MatchedRuleDescription);
    }

    /// <summary>
    /// Balance tier normalization: numeric thresholds produce tier classifications.
    /// </summary>
    [Fact]
    public void Scenario_BalanceTierNormalization_ClassifiesByThreshold()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathGreaterThan, JsonPath = "$.balance", ExpectedValue = "10000" }
                    },
                    StatusCode = 200,
                    Payload = """{"tier": "wealthy", "discount": 0.2}""",
                    Description = "Wealthy tier"
                },
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathGreaterThan, JsonPath = "$.balance", ExpectedValue = "1000" }
                    },
                    StatusCode = 200,
                    Payload = """{"tier": "comfortable", "discount": 0.1}""",
                    Description = "Comfortable tier"
                },
                new()
                {
                    StatusCode = 200,
                    Payload = """{"tier": "poor", "discount": 0}""",
                    Description = "Default tier"
                }
            }
        };

        var wealthy = ResponseTransformer.Transform(200, """{"balance": 50000}""", transformation);
        Assert.Equal("""{"tier": "wealthy", "discount": 0.2}""", wealthy.Payload);

        var comfortable = ResponseTransformer.Transform(200, """{"balance": 5000}""", transformation);
        Assert.Equal("""{"tier": "comfortable", "discount": 0.1}""", comfortable.Payload);

        var poor = ResponseTransformer.Transform(200, """{"balance": 500}""", transformation);
        Assert.Equal("""{"tier": "poor", "discount": 0}""", poor.Payload);
    }

    /// <summary>
    /// Existence check: 200 = exists, 404 = doesn't exist, different payloads.
    /// </summary>
    [Fact]
    public void Scenario_ExistenceCheck_NormalizesToBooleanPayload()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.StatusCodeIn, StatusCodes = new List<int> { 200 } }
                    },
                    StatusCode = 200,
                    Payload = """{"exists": true}"""
                },
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.StatusCodeIn, StatusCodes = new List<int> { 404 } }
                    },
                    StatusCode = 200,
                    Payload = """{"exists": false}""",
                    Description = "Not found is OK — just means doesn't exist"
                }
            }
        };

        var found = ResponseTransformer.Transform(200, """{"id": "abc", "lots": "of data"}""", transformation);
        Assert.Equal(200, found.StatusCode);
        Assert.Equal("""{"exists": true}""", found.Payload);
        Assert.True(found.IsSuccess);

        var notFound = ResponseTransformer.Transform(404, null, transformation);
        Assert.Equal(200, notFound.StatusCode);
        Assert.Equal("""{"exists": false}""", notFound.Payload);
        Assert.True(notFound.IsSuccess);
    }

    /// <summary>
    /// Error rewriting: the raw API returned 200 but the response body indicates
    /// a logical error. The transformation rewrites it to a 400 for the consumer.
    /// </summary>
    [Fact]
    public void Scenario_ErrorRewriting_200WithErrorFieldBecomesFailure()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathExists, JsonPath = "$.error" }
                    },
                    StatusCode = 400,
                    Payload = """{"failed": true, "reason": "upstream reported error in 200 body"}""",
                    Description = "API returned 200 with error field — treat as failure"
                },
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.StatusCodeIn, StatusCodes = new List<int> { 200 } }
                    },
                    StatusCode = 200,
                    Payload = null,
                    Description = "Clean 200 — pass through"
                }
            }
        };

        var errorIn200 = ResponseTransformer.Transform(200, """{"error": "insufficient funds"}""", transformation);
        Assert.Equal(400, errorIn200.StatusCode);
        Assert.False(errorIn200.IsSuccess);

        var clean200 = ResponseTransformer.Transform(200, """{"balance": 5000}""", transformation);
        Assert.Equal(200, clean200.StatusCode);
        Assert.Equal("""{"balance": 5000}""", clean200.Payload);
        Assert.True(clean200.IsSuccess);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Edge Cases
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Invalid JSON response body is handled gracefully.
    /// Status code rules can still fire; JSON path rules won't match.
    /// </summary>
    [Fact]
    public void Transform_InvalidJsonBody_StatusCodeRulesStillWork()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathEquals, JsonPath = "$.foo", ExpectedValue = "bar" }
                    },
                    StatusCode = 200,
                    Payload = """{"jsonRule": true}"""
                },
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.StatusCodeIn, StatusCodes = new List<int> { 200 } }
                    },
                    StatusCode = 200,
                    Payload = """{"statusRule": true}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(200, "this is not json {{{", transformation);

        // JSON path rule can't match (invalid JSON), but status code rule can
        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
        Assert.Equal("""{"statusRule": true}""", result.Payload);
    }

    /// <summary>
    /// Nested JSON path navigation works correctly.
    /// </summary>
    [Fact]
    public void Transform_NestedJsonPath_NavigatesCorrectly()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathEquals, JsonPath = "$.character.species.code", ExpectedValue = "elf" }
                    },
                    StatusCode = 200,
                    Payload = """{"isElf": true}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(200,
            """{"character": {"species": {"code": "elf", "name": "High Elf"}}}""",
            transformation);

        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
        Assert.Equal("""{"isElf": true}""", result.Payload);
    }

    /// <summary>
    /// Array index in JSON path works correctly.
    /// </summary>
    [Fact]
    public void Transform_ArrayIndexJsonPath_NavigatesCorrectly()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathEquals, JsonPath = "$.items[0].name", ExpectedValue = "sword" }
                    },
                    StatusCode = 200,
                    Payload = """{"firstItemIsSword": true}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(200,
            """{"items": [{"name": "sword", "dmg": 10}, {"name": "shield", "def": 5}]}""",
            transformation);

        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
    }

    /// <summary>
    /// Boolean JSON values are matched as "true"/"false" strings.
    /// </summary>
    [Fact]
    public void Transform_BooleanJsonValue_MatchesAsString()
    {
        var transformation = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new() { Type = TransformationConditionType.JsonPathEquals, JsonPath = "$.isDeprecated", ExpectedValue = "true" }
                    },
                    StatusCode = 400,
                    Payload = """{"error": "template is deprecated"}"""
                }
            }
        };

        var result = ResponseTransformer.Transform(200, """{"isDeprecated": true, "name": "old template"}""", transformation);

        Assert.Equal(TransformationOutcome.Transformed, result.Outcome);
        Assert.Equal(400, result.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // JSON Serialization Round-Trip
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ResponseTransformation types serialize and deserialize correctly via BannouJson.
    /// </summary>
    [Fact]
    public void ResponseTransformation_SerializesAndDeserializesViaBannouJson()
    {
        var original = new ResponseTransformation
        {
            Rules = new List<TransformationRule>
            {
                new()
                {
                    Conditions = new List<TransformationCondition>
                    {
                        new()
                        {
                            Type = TransformationConditionType.JsonPathEquals,
                            JsonPath = "$.species",
                            ExpectedValue = "elf"
                        },
                        new()
                        {
                            Type = TransformationConditionType.StatusCodeIn,
                            StatusCodes = new List<int> { 200 }
                        }
                    },
                    StatusCode = 200,
                    Payload = """{"isElf": true}""",
                    Description = "Elf check"
                }
            },
            TransientFailureStatusCodes = new List<int> { 503, 504 }
        };

        var json = BannouJson.Serialize(original);
        var deserialized = BannouJson.Deserialize<ResponseTransformation>(json);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Rules);
        Assert.Single(deserialized.Rules);
        Assert.Equal(2, deserialized.Rules[0].Conditions!.Count);
        Assert.Equal(TransformationConditionType.JsonPathEquals, deserialized.Rules[0].Conditions![0].Type);
        Assert.Equal("$.species", deserialized.Rules[0].Conditions![0].JsonPath);
        Assert.Equal("elf", deserialized.Rules[0].Conditions![0].ExpectedValue);
        Assert.Equal(TransformationConditionType.StatusCodeIn, deserialized.Rules[0].Conditions![1].Type);
        Assert.Equal(200, deserialized.Rules[0].StatusCode);
        Assert.Equal("""{"isElf": true}""", deserialized.Rules[0].Payload);
        Assert.Equal("Elf check", deserialized.Rules[0].Description);
        Assert.NotNull(deserialized.TransientFailureStatusCodes);
        Assert.Equal(2, deserialized.TransientFailureStatusCodes.Count);
    }

    /// <summary>
    /// PreboundApi with ResponseTransformation round-trips through BannouJson.
    /// </summary>
    [Fact]
    public void PreboundApi_WithTransformation_RoundTrips()
    {
        var original = new PreboundApi
        {
            ServiceName = "character",
            Endpoint = "/character/get",
            PayloadTemplate = """{"characterId": "{{characterId}}"}""",
            Description = "Get character to check species",
            ExecutionMode = PreboundApiExecutionMode.Sync,
            ResponseTransformation = new ResponseTransformation
            {
                Rules = new List<TransformationRule>
                {
                    new()
                    {
                        Conditions = new List<TransformationCondition>
                        {
                            new() { Type = TransformationConditionType.JsonPathEquals, JsonPath = "$.speciesCode", ExpectedValue = "elf" }
                        },
                        StatusCode = 200,
                        Payload = """{"modifier": 1.5}"""
                    }
                }
            }
        };

        var json = BannouJson.Serialize(original);
        var deserialized = BannouJson.Deserialize<PreboundApi>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("character", deserialized.ServiceName);
        Assert.Equal("/character/get", deserialized.Endpoint);
        Assert.Equal(PreboundApiExecutionMode.Sync, deserialized.ExecutionMode);
        Assert.NotNull(deserialized.ResponseTransformation);
        Assert.Single(deserialized.ResponseTransformation.Rules!);
    }
}
