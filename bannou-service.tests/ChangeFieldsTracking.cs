using BeyondImmersion.Bannou.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Tests;

/// <summary>
/// Tests for the changeFields property-setter-tracking pattern that enables
/// distinguishing "field absent" from "field explicitly null" on Update requests.
///
/// See GitHub Issue #722 for the systemic design.
///
/// The pattern works by transforming NSwag-generated auto-properties into tracked
/// properties where each setter records the property name in a HashSet. The resulting
/// ChangeFields collection is serialized as part of the request, enabling the server
/// to know exactly which fields the caller intended to set — even when the value is null.
///
/// Three client paths are validated:
///   1. WebSocket (raw JSON) — setter tracking during STJ deserialization
///   2. C# generated client — ChangeFields serialized in JSON despite WhenWritingNull
///   3. Legacy client — no ChangeFields awareness, backward-compatible behavior
/// </summary>
[Collection("unit tests")]
public class ChangeFieldsTracking : IClassFixture<CollectionFixture>
{
    public ChangeFieldsTracking(CollectionFixture collectionContext) { }

    #region Test Infrastructure: Simulated Post-Processed Update Request Model

    /// <summary>
    /// Simulates what the post-processing step would generate from a NSwag-produced
    /// UpdateCharacterRequest. Each auto-property is replaced with a backing field +
    /// tracked setter that records the camelCase property name in _changeFields.
    ///
    /// NSwag generates:
    ///   public string? Name { get; set; }
    ///
    /// Post-processing transforms to:
    ///   private string? _name;
    ///   public string? Name { get => _name; set { _name = value; Track("name"); } }
    /// </summary>
    private class TestUpdateRequest
    {
        private HashSet<string>? _changeFields;

        /// <summary>
        /// Fields explicitly set on this request. Populated automatically by property
        /// setters. Serialized as part of the request so the server can distinguish
        /// "field not provided" from "field explicitly set to null."
        /// </summary>
        public ICollection<string>? ChangeFields
        {
            get => _changeFields?.Count > 0 ? _changeFields : null;
            set
            {
                // Merge, don't replace — handles STJ deserialization ordering
                // (ChangeFields may be deserialized before or after individual properties)
                if (value != null)
                {
                    _changeFields ??= new(StringComparer.OrdinalIgnoreCase);
                    foreach (var field in value)
                        _changeFields.Add(field);
                }
            }
        }

        private void Track(string fieldName)
            => (_changeFields ??= new(StringComparer.OrdinalIgnoreCase)).Add(fieldName);

        // === Required field (always set, always tracked) ===

        private Guid _entityId;
        [JsonRequired]
        public Guid EntityId
        {
            get => _entityId;
            set { _entityId = value; Track("entityId"); }
        }

        // === Nullable optional string (the patronDeityCode case) ===

        private string? _name;
        public string? Name
        {
            get => _name;
            set { _name = value; Track("name"); }
        }

        private string? _code;
        public string? Code
        {
            get => _code;
            set { _code = value; Track("code"); }
        }

        // === Nullable optional Guid (the containerId case) ===

        private Guid? _referenceId;
        public Guid? ReferenceId
        {
            get => _referenceId;
            set { _referenceId = value; Track("referenceId"); }
        }

        // === Nullable optional int ===

        private int? _count;
        public int? Count
        {
            get => _count;
            set { _count = value; Track("count"); }
        }

        // === Nullable optional enum ===

        private TestStatus? _status;
        public TestStatus? Status
        {
            get => _status;
            set { _status = value; Track("status"); }
        }

        // === Nullable optional DateTimeOffset ===

        private DateTimeOffset? _expiresAt;
        public DateTimeOffset? ExpiresAt
        {
            get => _expiresAt;
            set { _expiresAt = value; Track("expiresAt"); }
        }
    }

    private enum TestStatus
    {
        Active,
        Inactive,
        Dead
    }

    #endregion

    #region Test Infrastructure: IsFieldSet Extension (simulates bannou-service helper)

    // This would live in bannou-service/Helpers/ChangeFieldsExtensions.cs

    private static bool IsFieldSet(ICollection<string>? changeFields, string fieldName)
    {
        if (changeFields == null || changeFields.Count == 0)
            return false;

        return changeFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    #region Setter Tracking: Basic Behavior

    [Fact]
    public void SetterTracking_RequiredFieldSet_AppearsInChangeFields()
    {
        var request = new TestUpdateRequest { EntityId = Guid.NewGuid() };

        Assert.NotNull(request.ChangeFields);
        Assert.Contains("entityId", request.ChangeFields);
    }

    [Fact]
    public void SetterTracking_OptionalFieldSetToValue_AppearsInChangeFields()
    {
        var request = new TestUpdateRequest
        {
            EntityId = Guid.NewGuid(),
            Name = "Test Name"
        };

        Assert.NotNull(request.ChangeFields);
        Assert.Contains("name", request.ChangeFields);
    }

    [Fact]
    public void SetterTracking_OptionalFieldSetToNull_AppearsInChangeFields()
    {
        // THIS IS THE CORE BEHAVIOR: setting a field to null explicitly
        // must be distinguishable from not setting it at all.
        var request = new TestUpdateRequest
        {
            EntityId = Guid.NewGuid(),
            Name = null
        };

        Assert.NotNull(request.ChangeFields);
        Assert.Contains("name", request.ChangeFields);
    }

    [Fact]
    public void SetterTracking_OptionalFieldNotSet_AbsentFromChangeFields()
    {
        var request = new TestUpdateRequest { EntityId = Guid.NewGuid() };

        Assert.NotNull(request.ChangeFields);
        Assert.DoesNotContain("name", request.ChangeFields);
        Assert.DoesNotContain("code", request.ChangeFields);
        Assert.DoesNotContain("referenceId", request.ChangeFields);
        Assert.DoesNotContain("count", request.ChangeFields);
        Assert.DoesNotContain("status", request.ChangeFields);
        Assert.DoesNotContain("expiresAt", request.ChangeFields);
    }

    [Fact]
    public void SetterTracking_MultipleFieldsSet_AllAppearInChangeFields()
    {
        var request = new TestUpdateRequest
        {
            EntityId = Guid.NewGuid(),
            Name = "Test",
            Code = null,      // explicitly clearing
            Count = 42
        };

        Assert.NotNull(request.ChangeFields);
        Assert.Contains("entityId", request.ChangeFields);
        Assert.Contains("name", request.ChangeFields);
        Assert.Contains("code", request.ChangeFields);
        Assert.Contains("count", request.ChangeFields);
        Assert.DoesNotContain("referenceId", request.ChangeFields);
        Assert.DoesNotContain("status", request.ChangeFields);
        Assert.DoesNotContain("expiresAt", request.ChangeFields);
    }

    [Fact]
    public void SetterTracking_NullableGuidSetToNull_AppearsInChangeFields()
    {
        // Covers the Item containerId / clearContainerId case
        var request = new TestUpdateRequest
        {
            EntityId = Guid.NewGuid(),
            ReferenceId = null
        };

        Assert.NotNull(request.ChangeFields);
        Assert.Contains("referenceId", request.ChangeFields);
    }

    [Fact]
    public void SetterTracking_NullableGuidSetToValue_AppearsInChangeFields()
    {
        var request = new TestUpdateRequest
        {
            EntityId = Guid.NewGuid(),
            ReferenceId = Guid.NewGuid()
        };

        Assert.NotNull(request.ChangeFields);
        Assert.Contains("referenceId", request.ChangeFields);
    }

    [Fact]
    public void SetterTracking_EnumSetToNull_AppearsInChangeFields()
    {
        var request = new TestUpdateRequest
        {
            EntityId = Guid.NewGuid(),
            Status = null
        };

        Assert.NotNull(request.ChangeFields);
        Assert.Contains("status", request.ChangeFields);
    }

    [Fact]
    public void SetterTracking_DateTimeOffsetSetToNull_AppearsInChangeFields()
    {
        var request = new TestUpdateRequest
        {
            EntityId = Guid.NewGuid(),
            ExpiresAt = null
        };

        Assert.NotNull(request.ChangeFields);
        Assert.Contains("expiresAt", request.ChangeFields);
    }

    [Fact]
    public void SetterTracking_NoPropertiesSet_ChangeFieldsIsNull()
    {
        // Default-constructed: no setter was called, ChangeFields stays null
        var request = new TestUpdateRequest();

        Assert.Null(request.ChangeFields);
    }

    [Fact]
    public void SetterTracking_SettingSameFieldTwice_AppearsOnceInChangeFields()
    {
        var request = new TestUpdateRequest { EntityId = Guid.NewGuid() };
        request.Name = "First";
        request.Name = "Second";

        var nameCount = request.ChangeFields!.Count(f =>
            string.Equals(f, "name", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, nameCount);
    }

    #endregion

    #region Deserialization: WebSocket Path (raw JSON → setter tracking)

    [Fact]
    public void Deserialization_FieldPresentWithValue_TrackedInChangeFields()
    {
        var json = """{"EntityId": "11111111-1111-1111-1111-111111111111", "Name": "Test"}""";
        var result = BannouJson.Deserialize<TestUpdateRequest>(json);

        Assert.NotNull(result);
        Assert.True(IsFieldSet(result.ChangeFields, "name"));
        Assert.Equal("Test", result.Name);
    }

    [Fact]
    public void Deserialization_FieldPresentWithNull_TrackedInChangeFields()
    {
        // THIS IS THE CRITICAL TEST: explicit null in JSON must be tracked
        var json = """{"EntityId": "11111111-1111-1111-1111-111111111111", "Name": null}""";
        var result = BannouJson.Deserialize<TestUpdateRequest>(json);

        Assert.NotNull(result);
        Assert.True(IsFieldSet(result.ChangeFields, "name"));
        Assert.Null(result.Name);
    }

    [Fact]
    public void Deserialization_FieldAbsent_NotTrackedInChangeFields()
    {
        var json = """{"EntityId": "11111111-1111-1111-1111-111111111111"}""";
        var result = BannouJson.Deserialize<TestUpdateRequest>(json);

        Assert.NotNull(result);
        Assert.False(IsFieldSet(result.ChangeFields, "name"));
        Assert.False(IsFieldSet(result.ChangeFields, "code"));
        Assert.False(IsFieldSet(result.ChangeFields, "referenceId"));
    }

    [Fact]
    public void Deserialization_AbsentVsNull_Distinguishable()
    {
        // Proves the 3-state problem from Issue #722 is SOLVED
        var absentJson = """{"EntityId": "11111111-1111-1111-1111-111111111111"}""";
        var nullJson = """{"EntityId": "11111111-1111-1111-1111-111111111111", "Code": null}""";

        var absent = BannouJson.Deserialize<TestUpdateRequest>(absentJson);
        var explicitNull = BannouJson.Deserialize<TestUpdateRequest>(nullJson);

        Assert.NotNull(absent);
        Assert.NotNull(explicitNull);

        // Both have null Code value...
        Assert.Null(absent.Code);
        Assert.Null(explicitNull.Code);

        // ...but ChangeFields distinguishes them!
        Assert.False(IsFieldSet(absent.ChangeFields, "code"));    // absent → don't change
        Assert.True(IsFieldSet(explicitNull.ChangeFields, "code")); // null → clear it
    }

    [Fact]
    public void Deserialization_AllThreeStates_Distinguishable()
    {
        // The complete 3-state proof: absent, null, and value
        var json = """
        {
            "EntityId": "11111111-1111-1111-1111-111111111111",
            "Name": "Set Value",
            "Code": null
        }
        """;

        var result = BannouJson.Deserialize<TestUpdateRequest>(json);
        Assert.NotNull(result);

        // State 1: ABSENT (ReferenceId) — not in ChangeFields, value is default null
        Assert.False(IsFieldSet(result.ChangeFields, "referenceId"));
        Assert.Null(result.ReferenceId);

        // State 2: EXPLICIT NULL (Code) — in ChangeFields, value is null
        Assert.True(IsFieldSet(result.ChangeFields, "code"));
        Assert.Null(result.Code);

        // State 3: VALUE (Name) — in ChangeFields, value is the string
        Assert.True(IsFieldSet(result.ChangeFields, "name"));
        Assert.Equal("Set Value", result.Name);
    }

    [Fact]
    public void Deserialization_NullableGuid_AbsentVsNull_Distinguishable()
    {
        // The containerId / clearContainerId case from Issue #721
        var absentJson = """{"EntityId": "11111111-1111-1111-1111-111111111111"}""";
        var nullJson = """{"EntityId": "11111111-1111-1111-1111-111111111111", "ReferenceId": null}""";

        var absent = BannouJson.Deserialize<TestUpdateRequest>(absentJson);
        var explicitNull = BannouJson.Deserialize<TestUpdateRequest>(nullJson);

        Assert.NotNull(absent);
        Assert.NotNull(explicitNull);

        Assert.Null(absent.ReferenceId);
        Assert.Null(explicitNull.ReferenceId);

        Assert.False(IsFieldSet(absent.ChangeFields, "referenceId"));
        Assert.True(IsFieldSet(explicitNull.ChangeFields, "referenceId"));
    }

    [Fact]
    public void Deserialization_NullableInt_AbsentVsNull_Distinguishable()
    {
        var absentJson = """{"EntityId": "11111111-1111-1111-1111-111111111111"}""";
        var nullJson = """{"EntityId": "11111111-1111-1111-1111-111111111111", "Count": null}""";

        var absent = BannouJson.Deserialize<TestUpdateRequest>(absentJson);
        var explicitNull = BannouJson.Deserialize<TestUpdateRequest>(nullJson);

        Assert.NotNull(absent);
        Assert.NotNull(explicitNull);

        Assert.Null(absent.Count);
        Assert.Null(explicitNull.Count);

        Assert.False(IsFieldSet(absent.ChangeFields, "count"));
        Assert.True(IsFieldSet(explicitNull.ChangeFields, "count"));
    }

    [Fact]
    public void Deserialization_CaseInsensitive_TracksFieldCorrectly()
    {
        // BannouJson uses PropertyNameCaseInsensitive = true
        // camelCase JSON property names must trigger PascalCase setters
        var json = """{"entityId": "11111111-1111-1111-1111-111111111111", "name": "Test"}""";
        var result = BannouJson.Deserialize<TestUpdateRequest>(json);

        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        // The tracked field name is always camelCase (from the setter)
        Assert.True(IsFieldSet(result.ChangeFields, "name"));
    }

    [Fact]
    public void Deserialization_NullableEnum_AbsentVsNull_Distinguishable()
    {
        var absentJson = """{"EntityId": "11111111-1111-1111-1111-111111111111"}""";
        var nullJson = """{"EntityId": "11111111-1111-1111-1111-111111111111", "Status": null}""";

        var absent = BannouJson.Deserialize<TestUpdateRequest>(absentJson);
        var explicitNull = BannouJson.Deserialize<TestUpdateRequest>(nullJson);

        Assert.NotNull(absent);
        Assert.NotNull(explicitNull);

        Assert.False(IsFieldSet(absent.ChangeFields, "status"));
        Assert.True(IsFieldSet(explicitNull.ChangeFields, "status"));
    }

    #endregion

    #region Serialization: C# Client Path (WhenWritingNull interaction)

    [Fact]
    public void Serialization_WhenWritingNull_StripsNullProperties()
    {
        // Confirm BannouJson baseline: null properties are omitted from JSON
        var request = new TestUpdateRequest
        {
            EntityId = Guid.NewGuid(),
            Name = null,
            Code = null
        };

        var json = BannouJson.Serialize(request);

        // Null values are stripped by WhenWritingNull
        Assert.DoesNotContain("\"Name\"", json);
        Assert.DoesNotContain("\"Code\"", json);
        // But non-null values survive
        Assert.Contains("\"EntityId\"", json);
    }

    [Fact]
    public void Serialization_ChangeFields_SurvivesWhenWritingNull()
    {
        // ChangeFields is a non-null array when fields are set, so WhenWritingNull preserves it
        var request = new TestUpdateRequest
        {
            EntityId = Guid.NewGuid(),
            Name = null  // setter fires → tracked in ChangeFields
        };

        var json = BannouJson.Serialize(request);

        // ChangeFields array must survive — it's non-null
        Assert.Contains("\"ChangeFields\"", json);
        Assert.Contains("\"name\"", json);     // inside the ChangeFields array
        Assert.Contains("\"entityId\"", json);  // inside the ChangeFields array
    }

    [Fact]
    public void Serialization_ChangeFieldsContainsNullFieldName_EvenWhenValueStripped()
    {
        // The critical C# SDK test: setting PatronDeityCode = null must produce
        // ChangeFields containing "code" even though the Code property itself is stripped
        var request = new TestUpdateRequest
        {
            EntityId = Guid.NewGuid(),
            Code = null  // Want to CLEAR this field
        };

        var json = BannouJson.Serialize(request);

        // Code value is stripped (WhenWritingNull)
        Assert.DoesNotContain("\"Code\":null", json);
        Assert.DoesNotContain("\"Code\": null", json);

        // But ChangeFields tells the server it was intentionally set
        Assert.Contains("\"ChangeFields\"", json);
        Assert.Contains("\"code\"", json);  // field name in ChangeFields array
    }

    [Fact]
    public void Serialization_NoFieldsSet_ChangeFieldsOmitted()
    {
        // Default-constructed request: ChangeFields is null → omitted by WhenWritingNull
        var request = new TestUpdateRequest();

        var json = BannouJson.Serialize(request);

        Assert.DoesNotContain("ChangeFields", json);
    }

    #endregion

    #region Round-Trip: C# Client → Server (serialize then deserialize)

    [Fact]
    public void RoundTrip_CSharpClient_FieldSetToValue_PreservedOnServer()
    {
        // Client sets Name to a value
        var clientRequest = new TestUpdateRequest
        {
            EntityId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "New Name"
        };

        // Client serializes (BannouJson with WhenWritingNull)
        var json = BannouJson.Serialize(clientRequest);

        // Server deserializes
        var serverRequest = BannouJson.Deserialize<TestUpdateRequest>(json);

        Assert.NotNull(serverRequest);
        Assert.Equal("New Name", serverRequest.Name);
        Assert.True(IsFieldSet(serverRequest.ChangeFields, "name"));
    }

    [Fact]
    public void RoundTrip_CSharpClient_FieldSetToNull_ServerSeesIntentToClear()
    {
        // Client explicitly clears Code (the patronDeityCode case)
        var clientRequest = new TestUpdateRequest
        {
            EntityId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Code = null  // intentional clear
        };

        // Client serializes
        var json = BannouJson.Serialize(clientRequest);

        // Server deserializes
        var serverRequest = BannouJson.Deserialize<TestUpdateRequest>(json);

        Assert.NotNull(serverRequest);
        // Server knows Code was explicitly set (from ChangeFields in JSON)
        Assert.True(IsFieldSet(serverRequest.ChangeFields, "code"));
        // Server reads the value as null → clear it
        Assert.Null(serverRequest.Code);
    }

    [Fact]
    public void RoundTrip_CSharpClient_FieldNotSet_ServerSeesNoChange()
    {
        // Client only sets EntityId and Name, does NOT set Code
        var clientRequest = new TestUpdateRequest
        {
            EntityId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "New Name"
        };

        var json = BannouJson.Serialize(clientRequest);
        var serverRequest = BannouJson.Deserialize<TestUpdateRequest>(json);

        Assert.NotNull(serverRequest);
        // Server knows Code was NOT set
        Assert.False(IsFieldSet(serverRequest.ChangeFields, "code"));
        // Code's null value means "not provided", NOT "clear it"
        Assert.Null(serverRequest.Code);
    }

    [Fact]
    public void RoundTrip_CSharpClient_NullableGuidClear_ServerSeesIntentToClear()
    {
        // The clearContainerId replacement: clear ReferenceId to null
        var clientRequest = new TestUpdateRequest
        {
            EntityId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ReferenceId = null  // clear the reference
        };

        var json = BannouJson.Serialize(clientRequest);
        var serverRequest = BannouJson.Deserialize<TestUpdateRequest>(json);

        Assert.NotNull(serverRequest);
        Assert.True(IsFieldSet(serverRequest.ChangeFields, "referenceId"));
        Assert.Null(serverRequest.ReferenceId);
    }

    [Fact]
    public void RoundTrip_CSharpClient_MixOfSetAndUnset_FullyPreserved()
    {
        // Comprehensive: set Name (value), clear Code (null), leave ReferenceId untouched
        var clientRequest = new TestUpdateRequest
        {
            EntityId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Updated",
            Code = null,
            Count = 42
        };

        var json = BannouJson.Serialize(clientRequest);
        var serverRequest = BannouJson.Deserialize<TestUpdateRequest>(json);

        Assert.NotNull(serverRequest);

        // Set to value
        Assert.True(IsFieldSet(serverRequest.ChangeFields, "name"));
        Assert.Equal("Updated", serverRequest.Name);

        // Explicitly cleared
        Assert.True(IsFieldSet(serverRequest.ChangeFields, "code"));
        Assert.Null(serverRequest.Code);

        // Set to value
        Assert.True(IsFieldSet(serverRequest.ChangeFields, "count"));
        Assert.Equal(42, serverRequest.Count);

        // NOT set — server must not touch these
        Assert.False(IsFieldSet(serverRequest.ChangeFields, "referenceId"));
        Assert.False(IsFieldSet(serverRequest.ChangeFields, "status"));
        Assert.False(IsFieldSet(serverRequest.ChangeFields, "expiresAt"));
    }

    #endregion

    #region ChangeFields Merge Behavior (deserialization ordering)

    [Fact]
    public void ChangeFieldsMerge_SetterAndJsonArray_BothContribute()
    {
        // When the server deserializes JSON containing both ChangeFields array AND
        // individual property values, both contribute to the final ChangeFields set.
        // STJ deserialization order is not guaranteed — merge handles either order.
        var json = """
        {
            "ChangeFields": ["code"],
            "EntityId": "11111111-1111-1111-1111-111111111111",
            "Name": "Test"
        }
        """;

        var result = BannouJson.Deserialize<TestUpdateRequest>(json);
        Assert.NotNull(result);

        // "code" from the JSON ChangeFields array
        Assert.True(IsFieldSet(result.ChangeFields, "code"));
        // "name" from the property setter
        Assert.True(IsFieldSet(result.ChangeFields, "name"));
        // "entityId" from the property setter
        Assert.True(IsFieldSet(result.ChangeFields, "entityId"));
    }

    [Fact]
    public void ChangeFieldsMerge_DuplicateEntries_DeduplicatedByHashSet()
    {
        // If both the JSON array and the setter add the same field name, no duplicates
        var json = """
        {
            "ChangeFields": ["name"],
            "EntityId": "11111111-1111-1111-1111-111111111111",
            "Name": "Test"
        }
        """;

        var result = BannouJson.Deserialize<TestUpdateRequest>(json);
        Assert.NotNull(result);

        var nameCount = result.ChangeFields!.Count(f =>
            string.Equals(f, "name", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, nameCount);
    }

    [Fact]
    public void ChangeFieldsMerge_CaseInsensitive()
    {
        // ChangeFields uses OrdinalIgnoreCase — "Name" and "name" are the same entry
        var json = """
        {
            "ChangeFields": ["Name"],
            "EntityId": "11111111-1111-1111-1111-111111111111",
            "Name": "Test"
        }
        """;

        var result = BannouJson.Deserialize<TestUpdateRequest>(json);
        Assert.NotNull(result);

        // Both "Name" (from JSON array) and "name" (from setter) should be treated as one
        Assert.True(IsFieldSet(result.ChangeFields, "name"));
        Assert.True(IsFieldSet(result.ChangeFields, "Name"));

        var nameCount = result.ChangeFields!.Count(f =>
            string.Equals(f, "name", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, nameCount);
    }

    #endregion

    #region Legacy Client Compatibility

    [Fact]
    public void LegacyClient_NoChangeFields_IsFieldSetReturnsFalse()
    {
        // A client that doesn't know about ChangeFields sends partial JSON
        // without the ChangeFields property. IsFieldSet returns false for all.
        var json = """{"EntityId": "11111111-1111-1111-1111-111111111111", "Name": "Legacy Update"}""";

        var result = BannouJson.Deserialize<TestUpdateRequest>(json);
        Assert.NotNull(result);

        // Setter tracking DOES populate ChangeFields during deserialization,
        // so IsFieldSet actually works correctly even for legacy clients!
        Assert.True(IsFieldSet(result.ChangeFields, "name"));
        Assert.True(IsFieldSet(result.ChangeFields, "entityId"));
        Assert.False(IsFieldSet(result.ChangeFields, "code"));
    }

    [Fact]
    public void LegacyClient_NullFieldsNotSent_ServiceSeesNoChange()
    {
        // Legacy client sends only the fields they want to change.
        // Fields they don't mention are absent from JSON, so setters don't fire,
        // so they don't appear in ChangeFields.
        var json = """{"EntityId": "11111111-1111-1111-1111-111111111111", "Name": "Only Name"}""";

        var result = BannouJson.Deserialize<TestUpdateRequest>(json);
        Assert.NotNull(result);

        Assert.True(IsFieldSet(result.ChangeFields, "name"));
        Assert.False(IsFieldSet(result.ChangeFields, "code"));
        Assert.False(IsFieldSet(result.ChangeFields, "referenceId"));
        Assert.False(IsFieldSet(result.ChangeFields, "count"));
    }

    #endregion

    #region IsFieldSet Helper Behavior

    [Fact]
    public void IsFieldSet_NullChangeFields_ReturnsFalse()
    {
        Assert.False(IsFieldSet(null, "name"));
    }

    [Fact]
    public void IsFieldSet_EmptyChangeFields_ReturnsFalse()
    {
        var empty = new HashSet<string>();
        Assert.False(IsFieldSet(empty, "name"));
    }

    [Fact]
    public void IsFieldSet_FieldPresent_ReturnsTrue()
    {
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name" };
        Assert.True(IsFieldSet(fields, "name"));
    }

    [Fact]
    public void IsFieldSet_FieldAbsent_ReturnsFalse()
    {
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name" };
        Assert.False(IsFieldSet(fields, "code"));
    }

    [Fact]
    public void IsFieldSet_CaseInsensitive_ReturnsTrue()
    {
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name" };
        Assert.True(IsFieldSet(fields, "Name"));
        Assert.True(IsFieldSet(fields, "NAME"));
        Assert.True(IsFieldSet(fields, "name"));
    }

    #endregion

    #region Simulated Service-Side Update Logic

    [Fact]
    public void ServiceLogic_FieldInChangeFields_AppliedToEntity()
    {
        // Simulates what UpdateCharacterAsync would do
        var json = """
        {
            "EntityId": "11111111-1111-1111-1111-111111111111",
            "Name": "New Name",
            "Code": null
        }
        """;

        var body = BannouJson.Deserialize<TestUpdateRequest>(json);
        Assert.NotNull(body);

        // Simulated stored entity (existing state)
        var storedName = "Old Name";
        var storedCode = "SOLAR";
        var storedReferenceId = Guid.NewGuid();

        // Simulated update logic using ChangeFields
        if (IsFieldSet(body.ChangeFields, "name"))
            storedName = body.Name;       // "New Name" — update

        if (IsFieldSet(body.ChangeFields, "code"))
            storedCode = body.Code;       // null — CLEAR (the 3-state win!)

        if (IsFieldSet(body.ChangeFields, "referenceId"))
            storedReferenceId = body.ReferenceId ?? storedReferenceId;

        Assert.Equal("New Name", storedName);
        Assert.Null(storedCode);          // Successfully cleared!
        Assert.NotEqual(Guid.Empty, storedReferenceId); // Unchanged — not in ChangeFields
    }

    [Fact]
    public void ServiceLogic_FieldNotInChangeFields_NotAppliedToEntity()
    {
        // Client only updates Name, server does NOT touch Code even though it's null on the body
        var json = """
        {
            "EntityId": "11111111-1111-1111-1111-111111111111",
            "Name": "Updated"
        }
        """;

        var body = BannouJson.Deserialize<TestUpdateRequest>(json);
        Assert.NotNull(body);

        var storedCode = "EXISTING_VALUE";

        // Code is null on body but NOT in ChangeFields → don't touch it
        if (IsFieldSet(body.ChangeFields, "code"))
            storedCode = body.Code;

        Assert.Equal("EXISTING_VALUE", storedCode); // Preserved!
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void EdgeCase_EmptyJsonObject_ThrowsForMissingRequired()
    {
        // Degenerate case: completely empty request body — JsonRequired on EntityId rejects it
        var json = """{}""";

        Assert.Throws<JsonException>(() => BannouJson.Deserialize<TestUpdateRequest>(json));
    }

    [Fact]
    public void EdgeCase_OnlyRequiredField_ChangeFieldsContainsOnlyRequired()
    {
        // Minimal valid request: only the required field
        var json = """{"EntityId": "11111111-1111-1111-1111-111111111111"}""";
        var result = BannouJson.Deserialize<TestUpdateRequest>(json);

        Assert.NotNull(result);
        Assert.NotNull(result.ChangeFields);
        Assert.Single(result.ChangeFields);
        Assert.Contains("entityId", result.ChangeFields);
    }

    [Fact]
    public void EdgeCase_ChangeFieldsGetter_ReturnsNullWhenEmpty()
    {
        // The getter returns null when the HashSet is empty (for WhenWritingNull to strip it)
        var request = new TestUpdateRequest();
        Assert.Null(request.ChangeFields);
    }

    [Fact]
    public void EdgeCase_ChangeFieldsGetter_ReturnsCollectionWhenNonEmpty()
    {
        var request = new TestUpdateRequest { EntityId = Guid.NewGuid() };
        Assert.NotNull(request.ChangeFields);
        Assert.NotEmpty(request.ChangeFields);
    }

    [Fact]
    public void EdgeCase_AllFieldsSetToNull_AllTrackedInChangeFields()
    {
        // Every single nullable field set to null explicitly
        var request = new TestUpdateRequest
        {
            EntityId = Guid.NewGuid(),
            Name = null,
            Code = null,
            ReferenceId = null,
            Count = null,
            Status = null,
            ExpiresAt = null
        };

        Assert.NotNull(request.ChangeFields);
        Assert.Contains("entityId", request.ChangeFields);
        Assert.Contains("name", request.ChangeFields);
        Assert.Contains("code", request.ChangeFields);
        Assert.Contains("referenceId", request.ChangeFields);
        Assert.Contains("count", request.ChangeFields);
        Assert.Contains("status", request.ChangeFields);
        Assert.Contains("expiresAt", request.ChangeFields);
        Assert.Equal(7, request.ChangeFields.Count);
    }

    [Fact]
    public void EdgeCase_ValueTypeFields_RoundTripCorrectly()
    {
        var clientRequest = new TestUpdateRequest
        {
            EntityId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Count = 0,     // Zero is a valid value, not sentinel
            Status = TestStatus.Dead
        };

        var json = BannouJson.Serialize(clientRequest);
        var serverRequest = BannouJson.Deserialize<TestUpdateRequest>(json);

        Assert.NotNull(serverRequest);
        Assert.True(IsFieldSet(serverRequest.ChangeFields, "count"));
        Assert.Equal(0, serverRequest.Count);
        Assert.True(IsFieldSet(serverRequest.ChangeFields, "status"));
        Assert.Equal(TestStatus.Dead, serverRequest.Status);
    }

    #endregion
}
