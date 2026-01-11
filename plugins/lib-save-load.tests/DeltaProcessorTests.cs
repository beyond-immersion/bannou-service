using BeyondImmersion.BannouService.SaveLoad.Delta;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.BannouService.SaveLoad.Tests;

/// <summary>
/// Tests for the DeltaProcessor JSON Patch implementation.
/// </summary>
public class DeltaProcessorTests
{
    private readonly Mock<ILogger<DeltaProcessor>> _loggerMock;
    private readonly DeltaProcessor _processor;

    public DeltaProcessorTests()
    {
        _loggerMock = new Mock<ILogger<DeltaProcessor>>();
        _processor = new DeltaProcessor(_loggerMock.Object, maxPatchOperations: 100);
    }

    #region ComputeDelta Tests

    [Fact]
    public void ComputeDelta_WithJsonPatch_ReturnsValidPatch()
    {
        // Arrange
        var source = Encoding.UTF8.GetBytes("""{"name":"Alice","age":25}""");
        var target = Encoding.UTF8.GetBytes("""{"name":"Alice","age":26}""");

        // Act
        var result = _processor.ComputeDelta(source, target, "JSON_PATCH");

        // Assert
        Assert.NotNull(result);
        var patchJson = Encoding.UTF8.GetString(result);
        Assert.Contains("replace", patchJson);
        Assert.Contains("/age", patchJson);
    }

    [Fact]
    public void ComputeDelta_WithAddOperation_ReturnsValidPatch()
    {
        // Arrange
        var source = Encoding.UTF8.GetBytes("""{"name":"Alice"}""");
        var target = Encoding.UTF8.GetBytes("""{"name":"Alice","email":"alice@example.com"}""");

        // Act
        var result = _processor.ComputeDelta(source, target, "JSON_PATCH");

        // Assert
        Assert.NotNull(result);
        var patchJson = Encoding.UTF8.GetString(result);
        Assert.Contains("add", patchJson);
        Assert.Contains("/email", patchJson);
    }

    [Fact]
    public void ComputeDelta_WithRemoveOperation_ReturnsValidPatch()
    {
        // Arrange
        var source = Encoding.UTF8.GetBytes("""{"name":"Alice","age":25}""");
        var target = Encoding.UTF8.GetBytes("""{"name":"Alice"}""");

        // Act
        var result = _processor.ComputeDelta(source, target, "JSON_PATCH");

        // Assert
        Assert.NotNull(result);
        var patchJson = Encoding.UTF8.GetString(result);
        Assert.Contains("remove", patchJson);
        Assert.Contains("/age", patchJson);
    }

    [Fact]
    public void ComputeDelta_WithIdenticalData_ReturnsEmptyPatch()
    {
        // Arrange
        var source = Encoding.UTF8.GetBytes("""{"name":"Alice","age":25}""");
        var target = Encoding.UTF8.GetBytes("""{"name":"Alice","age":25}""");

        // Act
        var result = _processor.ComputeDelta(source, target, "JSON_PATCH");

        // Assert
        Assert.NotNull(result);
        var patchJson = Encoding.UTF8.GetString(result);
        Assert.Equal("[]", patchJson);
    }

    [Fact]
    public void ComputeDelta_WithInvalidJson_ReturnsNull()
    {
        // Arrange
        var source = Encoding.UTF8.GetBytes("not valid json");
        var target = Encoding.UTF8.GetBytes("""{"name":"Alice"}""");

        // Act
        var result = _processor.ComputeDelta(source, target, "JSON_PATCH");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ComputeDelta_WithUnsupportedAlgorithm_ThrowsArgumentException()
    {
        // Arrange
        var source = Encoding.UTF8.GetBytes("""{"name":"Alice"}""");
        var target = Encoding.UTF8.GetBytes("""{"name":"Bob"}""");

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _processor.ComputeDelta(source, target, "UNKNOWN_ALGO"));
    }

    [Fact]
    public void ComputeDelta_WithBsdiff_ThrowsNotSupportedException()
    {
        // Arrange
        var source = Encoding.UTF8.GetBytes("""{"name":"Alice"}""");
        var target = Encoding.UTF8.GetBytes("""{"name":"Bob"}""");

        // Act & Assert
        Assert.Throws<NotSupportedException>(() =>
            _processor.ComputeDelta(source, target, "BSDIFF"));
    }

    #endregion

    #region ApplyDelta Tests

    [Fact]
    public void ApplyDelta_WithValidPatch_ReturnsReconstructedData()
    {
        // Arrange
        var source = Encoding.UTF8.GetBytes("""{"name":"Alice","age":25}""");
        var target = Encoding.UTF8.GetBytes("""{"name":"Alice","age":26}""");

        var delta = _processor.ComputeDelta(source, target, "JSON_PATCH");
        Assert.NotNull(delta);

        // Act
        var result = _processor.ApplyDelta(source, delta, "JSON_PATCH");

        // Assert
        Assert.NotNull(result);
        var resultJson = Encoding.UTF8.GetString(result);
        var resultObj = JsonDocument.Parse(resultJson);
        Assert.Equal(26, resultObj.RootElement.GetProperty("age").GetInt32());
    }

    [Fact]
    public void ApplyDelta_WithComplexPatch_ReconstructsCorrectly()
    {
        // Arrange
        var source = Encoding.UTF8.GetBytes("""{"items":[{"id":1},{"id":2}],"count":2}""");
        var target = Encoding.UTF8.GetBytes("""{"items":[{"id":1},{"id":2},{"id":3}],"count":3}""");

        var delta = _processor.ComputeDelta(source, target, "JSON_PATCH");
        Assert.NotNull(delta);

        // Act
        var result = _processor.ApplyDelta(source, delta, "JSON_PATCH");

        // Assert
        Assert.NotNull(result);
        var resultJson = Encoding.UTF8.GetString(result);
        var resultObj = JsonDocument.Parse(resultJson);
        Assert.Equal(3, resultObj.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(3, resultObj.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void ApplyDelta_WithInvalidSourceJson_ReturnsNull()
    {
        // Arrange
        var source = Encoding.UTF8.GetBytes("not valid json");
        var delta = Encoding.UTF8.GetBytes("""[{"op":"replace","path":"/name","value":"Bob"}]""");

        // Act
        var result = _processor.ApplyDelta(source, delta, "JSON_PATCH");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ApplyDelta_WithInvalidPatchJson_ReturnsNull()
    {
        // Arrange
        var source = Encoding.UTF8.GetBytes("""{"name":"Alice"}""");
        var delta = Encoding.UTF8.GetBytes("not valid patch");

        // Act
        var result = _processor.ApplyDelta(source, delta, "JSON_PATCH");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region ValidateDelta Tests

    [Fact]
    public void ValidateDelta_WithValidPatch_ReturnsTrue()
    {
        // Arrange
        var patch = Encoding.UTF8.GetBytes("""[{"op":"replace","path":"/name","value":"Bob"}]""");

        // Act
        var result = _processor.ValidateDelta(patch, "JSON_PATCH");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateDelta_WithEmptyPatch_ReturnsTrue()
    {
        // Arrange
        var patch = Encoding.UTF8.GetBytes("[]");

        // Act
        var result = _processor.ValidateDelta(patch, "JSON_PATCH");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateDelta_WithInvalidPatch_ReturnsFalse()
    {
        // Arrange
        var patch = Encoding.UTF8.GetBytes("not a valid patch");

        // Act
        var result = _processor.ValidateDelta(patch, "JSON_PATCH");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ValidateDelta_WithNonJsonAlgorithm_ReturnsTrue()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4 };

        // Act
        var result = _processor.ValidateDelta(data, "BSDIFF");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateDelta_WithEmptyDataForNonJsonAlgorithm_ReturnsFalse()
    {
        // Arrange
        var data = Array.Empty<byte>();

        // Act
        var result = _processor.ValidateDelta(data, "BSDIFF");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetOperationCount Tests

    [Fact]
    public void GetOperationCount_WithValidPatch_ReturnsCorrectCount()
    {
        // Arrange
        var patch = Encoding.UTF8.GetBytes("""[{"op":"replace","path":"/a","value":1},{"op":"add","path":"/b","value":2}]""");

        // Act
        var count = _processor.GetOperationCount(patch, "JSON_PATCH");

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public void GetOperationCount_WithEmptyPatch_ReturnsZero()
    {
        // Arrange
        var patch = Encoding.UTF8.GetBytes("[]");

        // Act
        var count = _processor.GetOperationCount(patch, "JSON_PATCH");

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public void GetOperationCount_WithNonJsonAlgorithm_ReturnsNegativeOne()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4 };

        // Act
        var count = _processor.GetOperationCount(data, "BSDIFF");

        // Assert
        Assert.Equal(-1, count);
    }

    #endregion

    #region MaxPatchOperations Tests

    [Fact]
    public void ComputeDelta_ExceedingMaxOperations_ReturnsNull()
    {
        // Arrange - Use a processor with low max operations
        var limitedProcessor = new DeltaProcessor(_loggerMock.Object, maxPatchOperations: 2);

        // Create source and target that will generate many patch operations
        var source = Encoding.UTF8.GetBytes("""{"a":1,"b":2,"c":3,"d":4}""");
        var target = Encoding.UTF8.GetBytes("""{"a":10,"b":20,"c":30,"d":40}""");

        // Act
        var result = limitedProcessor.ComputeDelta(source, target, "JSON_PATCH");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ValidateDelta_ExceedingMaxOperations_ReturnsFalse()
    {
        // Arrange - Use a processor with low max operations
        var limitedProcessor = new DeltaProcessor(_loggerMock.Object, maxPatchOperations: 1);

        // Create a patch with more than 1 operation
        var patch = Encoding.UTF8.GetBytes("""[{"op":"replace","path":"/a","value":1},{"op":"add","path":"/b","value":2}]""");

        // Act
        var result = limitedProcessor.ValidateDelta(patch, "JSON_PATCH");

        // Assert
        Assert.False(result);
    }

    #endregion
}
