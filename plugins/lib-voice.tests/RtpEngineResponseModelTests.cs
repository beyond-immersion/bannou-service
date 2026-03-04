using BeyondImmersion.BannouService.Voice.Clients;
using Xunit;

namespace BeyondImmersion.BannouService.Voice.Tests;

/// <summary>
/// Additional unit tests for RtpEngine response models.
/// Covers edge cases for IsSuccess, Warning, DeleteResponse timestamps,
/// and QueryResponse stream counting.
/// </summary>
public class RtpEngineResponseModelTests
{
    #region IsSuccess Edge Cases

    [Theory]
    [InlineData("ok", true)]
    [InlineData("OK", true)]
    [InlineData("Ok", true)]
    [InlineData("error", false)]
    [InlineData("ERROR", false)]
    [InlineData("", false)]
    [InlineData("pong", false)]
    [InlineData("timeout", false)]
    public void IsSuccess_VariousResults_ReturnsCorrectValue(string result, bool expected)
    {
        // Arrange
        var response = new RtpEngineOfferResponse { Result = result };

        // Act & Assert
        Assert.Equal(expected, response.IsSuccess);
    }

    [Fact]
    public void IsSuccess_DefaultResult_ReturnsFalse()
    {
        // Arrange - default Result is string.Empty
        var response = new RtpEngineDeleteResponse();

        // Act & Assert
        Assert.False(response.IsSuccess);
        Assert.Equal(string.Empty, response.Result);
    }

    #endregion

    #region Warning Property Tests

    [Fact]
    public void Warning_CanBeSetAndRetrieved()
    {
        // Arrange
        var response = new RtpEngineOfferResponse
        {
            Result = "ok",
            Warning = "codec not supported, falling back"
        };

        // Act & Assert
        Assert.True(response.IsSuccess);
        Assert.Equal("codec not supported, falling back", response.Warning);
    }

    [Fact]
    public void Warning_DefaultsToNull()
    {
        // Arrange
        var response = new RtpEngineOfferResponse { Result = "ok" };

        // Act & Assert
        Assert.Null(response.Warning);
    }

    #endregion

    #region RtpEngineDeleteResponse Tests

    [Fact]
    public void DeleteResponse_TimestampsCanBeSet()
    {
        // Arrange
        var created = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var lastSignal = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var response = new RtpEngineDeleteResponse
        {
            Result = "ok",
            Created = created,
            LastSignal = lastSignal
        };

        // Act & Assert
        Assert.True(response.IsSuccess);
        Assert.Equal(created, response.Created);
        Assert.Equal(lastSignal, response.LastSignal);
    }

    [Fact]
    public void DeleteResponse_TotalsCanBeNull()
    {
        // Arrange
        var response = new RtpEngineDeleteResponse { Result = "ok" };

        // Act & Assert
        Assert.Null(response.Totals);
    }

    [Fact]
    public void DeleteResponse_TotalsCanHaveData()
    {
        // Arrange
        var response = new RtpEngineDeleteResponse
        {
            Result = "ok",
            Totals = new Dictionary<string, object>
            {
                { "bytes", 1024L },
                { "packets", 100L }
            }
        };

        // Act & Assert
        Assert.NotNull(response.Totals);
        Assert.Equal(2, response.Totals.Count);
    }

    #endregion

    #region RtpEngineQueryResponse Tests

    [Fact]
    public void QueryResponse_StreamCount_EmptyDictionary_ReturnsZero()
    {
        // Arrange
        var response = new RtpEngineQueryResponse
        {
            Result = "ok",
            Streams = new Dictionary<string, object>()
        };

        // Act & Assert
        Assert.Equal(0, response.StreamCount);
    }

    [Fact]
    public void QueryResponse_StreamCount_MultipleStreams_ReturnsCorrectCount()
    {
        // Arrange
        var response = new RtpEngineQueryResponse
        {
            Result = "ok",
            Streams = new Dictionary<string, object>
            {
                { "audio-0", new object() },
                { "audio-1", new object() },
                { "video-0", new object() }
            }
        };

        // Act & Assert
        Assert.Equal(3, response.StreamCount);
    }

    [Fact]
    public void QueryResponse_WithErrorAndNoStreams()
    {
        // Arrange
        var response = new RtpEngineQueryResponse
        {
            Result = "error",
            ErrorReason = "Unknown call-id"
        };

        // Act & Assert
        Assert.False(response.IsSuccess);
        Assert.Equal("Unknown call-id", response.ErrorReason);
        Assert.Equal(0, response.StreamCount);
    }

    #endregion

    #region Response Type Defaults Tests

    [Fact]
    public void OfferResponse_DefaultSdp_IsEmptyString()
    {
        // Arrange
        var response = new RtpEngineOfferResponse();

        // Assert
        Assert.Equal(string.Empty, response.Sdp);
    }

    [Fact]
    public void AnswerResponse_DefaultSdp_IsEmptyString()
    {
        // Arrange
        var response = new RtpEngineAnswerResponse();

        // Assert
        Assert.Equal(string.Empty, response.Sdp);
    }

    [Fact]
    public void PublishResponse_DefaultSdp_IsEmptyString()
    {
        // Arrange
        var response = new RtpEnginePublishResponse();

        // Assert
        Assert.Equal(string.Empty, response.Sdp);
    }

    [Fact]
    public void SubscribeResponse_DefaultSdp_IsEmptyString()
    {
        // Arrange
        var response = new RtpEngineSubscribeResponse();

        // Assert
        Assert.Equal(string.Empty, response.Sdp);
    }

    [Fact]
    public void DeleteResponse_DefaultTimestamps_AreZero()
    {
        // Arrange
        var response = new RtpEngineDeleteResponse();

        // Assert
        Assert.Equal(0, response.Created);
        Assert.Equal(0, response.LastSignal);
    }

    #endregion

    #region Error Response Tests

    [Fact]
    public void ErrorResponse_WithReasonAndWarning()
    {
        // Arrange
        var response = new RtpEngineOfferResponse
        {
            Result = "error",
            ErrorReason = "Internal error",
            Warning = "Media proxy overloaded"
        };

        // Act & Assert
        Assert.False(response.IsSuccess);
        Assert.Equal("Internal error", response.ErrorReason);
        Assert.Equal("Media proxy overloaded", response.Warning);
    }

    [Fact]
    public void SuccessResponse_WithNoErrorReason()
    {
        // Arrange
        var response = new RtpEngineOfferResponse
        {
            Result = "ok",
            Sdp = "v=0\r\no=- 123 456 IN IP4 10.0.0.1\r\n"
        };

        // Act & Assert
        Assert.True(response.IsSuccess);
        Assert.Null(response.ErrorReason);
        Assert.Contains("v=0", response.Sdp);
    }

    #endregion
}
