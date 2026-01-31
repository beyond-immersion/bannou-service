using BeyondImmersion.BannouService.State.Services;

namespace BeyondImmersion.BannouService.State.Tests;

/// <summary>
/// Unit tests for RedisLuaScripts loader.
/// Verifies that Lua scripts are correctly embedded and loadable.
/// </summary>
public class RedisLuaScriptsTests
{
    #region Script Loading Tests

    [Fact]
    public void TryCreate_LoadsFromEmbeddedResource()
    {
        // Act
        var script = RedisLuaScripts.TryCreate;

        // Assert
        Assert.NotNull(script);
        Assert.NotEmpty(script);
        Assert.Contains("JSON.SET", script);
        Assert.Contains("EXISTS", script);
    }

    [Fact]
    public void TryUpdate_LoadsFromEmbeddedResource()
    {
        // Act
        var script = RedisLuaScripts.TryUpdate;

        // Assert
        Assert.NotNull(script);
        Assert.NotEmpty(script);
        Assert.Contains("JSON.SET", script);
        Assert.Contains("HGET", script);
        Assert.Contains("version", script);
    }

    [Fact]
    public void GetScript_WithValidName_ReturnsScript()
    {
        // Act
        var script = RedisLuaScripts.GetScript("TryCreate");

        // Assert
        Assert.NotNull(script);
        Assert.Equal(RedisLuaScripts.TryCreate, script);
    }

    [Fact]
    public void GetScript_WithInvalidName_ThrowsInvalidOperationException()
    {
        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RedisLuaScripts.GetScript("NonExistentScript"));

        Assert.Contains("not found", ex.Message);
        Assert.Contains("NonExistentScript", ex.Message);
    }

    [Fact]
    public void GetScript_CachesScripts()
    {
        // Act - Load same script twice
        var script1 = RedisLuaScripts.GetScript("TryCreate");
        var script2 = RedisLuaScripts.GetScript("TryCreate");

        // Assert - Should return same cached instance
        Assert.Same(script1, script2);
    }

    #endregion

    #region Script Listing Tests

    [Fact]
    public void ListAvailableScripts_ReturnsAllScripts()
    {
        // Act
        var scripts = RedisLuaScripts.ListAvailableScripts().ToList();

        // Assert
        Assert.Contains("TryCreate", scripts);
        Assert.Contains("TryUpdate", scripts);
        Assert.Equal(2, scripts.Count); // Update if more scripts added
    }

    #endregion

    #region Script Content Validation Tests

    [Fact]
    public void TryCreate_HasCorrectKeyStructure()
    {
        // Arrange
        var script = RedisLuaScripts.TryCreate;

        // Assert - Script should reference KEYS[1] (fullKey) and KEYS[2] (metaKey)
        Assert.Contains("KEYS[1]", script);
        Assert.Contains("KEYS[2]", script);
    }

    [Fact]
    public void TryCreate_HasCorrectArgStructure()
    {
        // Arrange
        var script = RedisLuaScripts.TryCreate;

        // Assert - Script should reference ARGV[1] (json) and ARGV[2] (timestamp)
        Assert.Contains("ARGV[1]", script);
        Assert.Contains("ARGV[2]", script);
    }

    [Fact]
    public void TryCreate_ReturnsCorrectValues()
    {
        // Arrange
        var script = RedisLuaScripts.TryCreate;

        // Assert - Script should return 1 for success, -1 for failure
        Assert.Contains("return 1", script);
        Assert.Contains("return -1", script);
    }

    [Fact]
    public void TryUpdate_HasCorrectKeyStructure()
    {
        // Arrange
        var script = RedisLuaScripts.TryUpdate;

        // Assert - Script should reference KEYS[1] (fullKey) and KEYS[2] (metaKey)
        Assert.Contains("KEYS[1]", script);
        Assert.Contains("KEYS[2]", script);
    }

    [Fact]
    public void TryUpdate_HasCorrectArgStructure()
    {
        // Arrange
        var script = RedisLuaScripts.TryUpdate;

        // Assert - Script should reference ARGV[1] (etag), ARGV[2] (json), ARGV[3] (timestamp)
        Assert.Contains("ARGV[1]", script);
        Assert.Contains("ARGV[2]", script);
        Assert.Contains("ARGV[3]", script);
    }

    [Fact]
    public void TryUpdate_ReturnsNewVersionOnSuccess()
    {
        // Arrange
        var script = RedisLuaScripts.TryUpdate;

        // Assert - Script should return newVersion on success
        Assert.Contains("return newVersion", script);
    }

    [Fact]
    public void TryUpdate_ReturnsMinusOneOnVersionMismatch()
    {
        // Arrange
        var script = RedisLuaScripts.TryUpdate;

        // Assert - Script should return -1 on version mismatch
        Assert.Contains("return -1", script);
    }

    [Fact]
    public void TryUpdate_HandlesNilVersionAsFalse()
    {
        // Arrange
        var script = RedisLuaScripts.TryUpdate;

        // Assert - Script should treat nil/false as version "0"
        Assert.Contains("currentVersion == false", script);
        Assert.Contains("currentVersion = '0'", script);
    }

    #endregion
}
