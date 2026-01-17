using System.Runtime.InteropServices;
using BeyondImmersion.Bannou.AssetBundler.Stride.Platform;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Stride.Tests.Platform;

/// <summary>
/// Tests for WslPathConverter.
/// Note: Full WSL functionality can only be tested in a WSL environment.
/// These tests focus on path conversion logic that works regardless of platform.
/// </summary>
public class WslPathConverterTests
{
    #region ToWindowsPath Tests (Non-WSL behavior)

    [Fact]
    public void ToWindowsPath_Empty_ReturnsEmpty()
    {
        Assert.Equal("", WslPathConverter.ToWindowsPath(""));
    }

    [Theory]
    [InlineData("C:\\Users\\test", "C:\\Users\\test")]
    [InlineData("D:\\Projects\\bannou", "D:\\Projects\\bannou")]
    public void ToWindowsPath_AlreadyWindowsPath_ReturnsUnchanged(string input, string expected)
    {
        // On non-WSL systems, paths should pass through unchanged
        if (!WslPathConverter.IsWsl)
        {
            Assert.Equal(expected, WslPathConverter.ToWindowsPath(input));
        }
    }

    [Fact]
    public void ToWindowsPath_OnNonWsl_ReturnsInputUnchanged()
    {
        // Skip if actually running on WSL
        if (WslPathConverter.IsWsl)
            return;

        // Non-WSL should pass through any path unchanged
        Assert.Equal("/home/user/test", WslPathConverter.ToWindowsPath("/home/user/test"));
        Assert.Equal("/mnt/c/Users", WslPathConverter.ToWindowsPath("/mnt/c/Users"));
    }

    #endregion

    #region ToUnixPath Tests (Non-WSL behavior)

    [Fact]
    public void ToUnixPath_Empty_ReturnsEmpty()
    {
        Assert.Equal("", WslPathConverter.ToUnixPath(""));
    }

    [Fact]
    public void ToUnixPath_OnNonWsl_ReturnsInputUnchanged()
    {
        // Skip if actually running on WSL
        if (WslPathConverter.IsWsl)
            return;

        // Non-WSL should pass through any path unchanged
        Assert.Equal("C:\\Users\\test", WslPathConverter.ToUnixPath("C:\\Users\\test"));
        Assert.Equal("/home/user/test", WslPathConverter.ToUnixPath("/home/user/test"));
    }

    #endregion

    #region IsWsl Detection Tests

    [Fact]
    public void IsWsl_OnWindows_ReturnsFalse()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        Assert.False(WslPathConverter.IsWsl);
    }

    [Fact]
    public void IsWsl_OnMacOS_ReturnsFalse()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        Assert.False(WslPathConverter.IsWsl);
    }

    [Fact]
    public void IsWsl_IsCached()
    {
        // Access twice - should return same value (cached)
        var first = WslPathConverter.IsWsl;
        var second = WslPathConverter.IsWsl;

        Assert.Equal(first, second);
    }

    #endregion

    #region CreateWindowsCommand Tests

    [Fact]
    public void CreateWindowsCommand_OnNonWsl_ReturnsCmdExe()
    {
        if (WslPathConverter.IsWsl)
            return;

        var (executable, arguments) = WslPathConverter.CreateWindowsCommand(
            "/home/user/project",
            "dotnet build");

        Assert.Equal("cmd.exe", executable);
        Assert.Contains("dotnet build", arguments);
    }

    [Fact]
    public void CreateWindowsCommand_IncludesCommand()
    {
        var (_, arguments) = WslPathConverter.CreateWindowsCommand(
            "/test/dir",
            "echo hello");

        Assert.Contains("echo hello", arguments);
    }

    #endregion

    #region CreateWindowsProcessStartInfo Tests

    [Fact]
    public void CreateWindowsProcessStartInfo_SetsCorrectProperties()
    {
        var startInfo = WslPathConverter.CreateWindowsProcessStartInfo(
            "/test/dir",
            "dotnet build");

        Assert.Equal("cmd.exe", startInfo.FileName);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
    }

    [Fact]
    public void CreateWindowsProcessStartInfo_SetsUtf8Encoding()
    {
        var startInfo = WslPathConverter.CreateWindowsProcessStartInfo(
            "/test/dir",
            "dotnet build");

        Assert.Equal(System.Text.Encoding.UTF8, startInfo.StandardOutputEncoding);
        Assert.Equal(System.Text.Encoding.UTF8, startInfo.StandardErrorEncoding);
    }

    #endregion

    #region WSL-Specific Path Conversion (Only runs in WSL)

    [Fact]
    public void ToWindowsPath_MntPath_ConvertsToWindowsDrive()
    {
        // This test only validates behavior when actually running in WSL
        if (!WslPathConverter.IsWsl)
            return;

        // /mnt/c/Users -> C:\Users
        var result = WslPathConverter.ToWindowsPath("/mnt/c/Users/test");
        Assert.StartsWith("C:", result);
        Assert.Contains("Users", result);
    }

    [Fact]
    public void ToWindowsPath_MntPath_HandlesDifferentDrives()
    {
        if (!WslPathConverter.IsWsl)
            return;

        var cPath = WslPathConverter.ToWindowsPath("/mnt/c/test");
        var dPath = WslPathConverter.ToWindowsPath("/mnt/d/test");

        Assert.StartsWith("C:", cPath);
        Assert.StartsWith("D:", dPath);
    }

    [Fact]
    public void ToUnixPath_WindowsDrive_ConvertsToMnt()
    {
        if (!WslPathConverter.IsWsl)
            return;

        // C:\Users -> /mnt/c/Users
        var result = WslPathConverter.ToUnixPath("C:\\Users\\test");
        Assert.StartsWith("/mnt/c", result);
    }

    #endregion
}
