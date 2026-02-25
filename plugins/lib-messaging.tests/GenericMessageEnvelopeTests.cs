using BeyondImmersion.BannouService.Messaging.Services;

namespace BeyondImmersion.BannouService.Messaging.Tests;

/// <summary>
/// Unit tests for GenericMessageEnvelope.
/// Tests verify construction, payload serialization, and deserialization branching.
/// </summary>
public class GenericMessageEnvelopeTests
{
    #region Constructor Tests

    /// <summary>
    /// Verifies that the default constructor creates a valid envelope with expected defaults.
    /// </summary>
    [Fact]
    public void DefaultConstructor_SetsDefaults()
    {
        // Act
        var envelope = new GenericMessageEnvelope();

        // Assert
        Assert.NotEqual(Guid.Empty, envelope.EventId);
        Assert.Equal("messaging.generic", envelope.EventName);
        Assert.Equal("{}", envelope.PayloadJson);
        Assert.Equal("application/json", envelope.ContentType);
        Assert.Equal(string.Empty, envelope.Topic);
    }

    /// <summary>
    /// Verifies that the parameterized constructor serializes the payload and sets the topic.
    /// </summary>
    [Fact]
    public void Constructor_WithPayload_SerializesCorrectly()
    {
        // Arrange
        var payload = new { Name = "test", Value = 42 };

        // Act
        var envelope = new GenericMessageEnvelope("account.created", payload);

        // Assert
        Assert.Equal("account.created", envelope.Topic);
        Assert.Equal("messaging.account.created", envelope.EventName);
        Assert.Contains("\"Name\"", envelope.PayloadJson);
        Assert.Contains("42", envelope.PayloadJson);
    }

    /// <summary>
    /// Verifies that a null payload serializes to "{}".
    /// </summary>
    [Fact]
    public void Constructor_WithNullPayload_SetsEmptyObjectJson()
    {
        // Act
        var envelope = new GenericMessageEnvelope("test.topic", null);

        // Assert
        Assert.Equal("{}", envelope.PayloadJson);
        Assert.Equal("test.topic", envelope.Topic);
    }

    #endregion

    #region GetPayload<T> Tests

    /// <summary>
    /// Verifies that GetPayload with valid JSON returns a deserialized object.
    /// </summary>
    [Fact]
    public void GetPayload_ValidJson_ReturnsDeserializedObject()
    {
        // Arrange
        var envelope = new GenericMessageEnvelope("test", new TestPayload { Name = "hello", Count = 5 });

        // Act
        var result = envelope.GetPayload<TestPayload>();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("hello", result.Name);
        Assert.Equal(5, result.Count);
    }

    /// <summary>
    /// Verifies that GetPayload with null PayloadJson returns default.
    /// </summary>
    [Fact]
    public void GetPayload_NullPayloadJson_ReturnsDefault()
    {
        // Arrange
        var envelope = new GenericMessageEnvelope();
        envelope.PayloadJson = null!;

        // Act
        var result = envelope.GetPayload<TestPayload>();

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that GetPayload with empty string returns default.
    /// </summary>
    [Fact]
    public void GetPayload_EmptyString_ReturnsDefault()
    {
        // Arrange
        var envelope = new GenericMessageEnvelope();
        envelope.PayloadJson = "";

        // Act
        var result = envelope.GetPayload<TestPayload>();

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that GetPayload with "{}" returns default (empty object treated as no payload).
    /// </summary>
    [Fact]
    public void GetPayload_EmptyObjectJson_ReturnsDefault()
    {
        // Arrange
        var envelope = new GenericMessageEnvelope();
        // PayloadJson defaults to "{}"

        // Act
        var result = envelope.GetPayload<TestPayload>();

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetPayloadAsObject Tests

    /// <summary>
    /// Verifies that GetPayloadAsObject with valid JSON returns a non-null result.
    /// </summary>
    [Fact]
    public void GetPayloadAsObject_ValidJson_ReturnsNonNull()
    {
        // Arrange
        var envelope = new GenericMessageEnvelope("test", new { Key = "value" });

        // Act
        var result = envelope.GetPayloadAsObject();

        // Assert
        Assert.NotNull(result);
    }

    /// <summary>
    /// Verifies that GetPayloadAsObject with null returns null.
    /// </summary>
    [Fact]
    public void GetPayloadAsObject_NullPayloadJson_ReturnsNull()
    {
        // Arrange
        var envelope = new GenericMessageEnvelope();
        envelope.PayloadJson = null!;

        // Act
        var result = envelope.GetPayloadAsObject();

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that GetPayloadAsObject with "{}" returns null.
    /// </summary>
    [Fact]
    public void GetPayloadAsObject_EmptyObjectJson_ReturnsNull()
    {
        // Arrange
        var envelope = new GenericMessageEnvelope();

        // Act
        var result = envelope.GetPayloadAsObject();

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region IBannouEvent Interface Tests

    /// <summary>
    /// Verifies that the envelope correctly implements IBannouEvent.
    /// </summary>
    [Fact]
    public void Envelope_ImplementsBannouEvent()
    {
        // Arrange & Act
        var envelope = new GenericMessageEnvelope("test.topic", new { Data = 1 });

        // Assert
        Assert.IsAssignableFrom<BeyondImmersion.Bannou.Core.IBannouEvent>(envelope);
        Assert.NotEqual(Guid.Empty, envelope.EventId);
        Assert.True(envelope.Timestamp <= DateTimeOffset.UtcNow);
    }

    #endregion

    /// <summary>
    /// Simple test payload class for deserialization tests.
    /// </summary>
    private class TestPayload
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}
