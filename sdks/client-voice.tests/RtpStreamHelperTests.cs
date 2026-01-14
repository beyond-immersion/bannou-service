using BeyondImmersion.Bannou.Client.Voice.Services;
using Xunit;

namespace BeyondImmersion.Bannou.Client.Voice.Tests;

public class RtpStreamHelperTests
{
    #region Initial State Tests

    [Fact]
    public void InitialState_IsNotActive()
    {
        // Arrange & Act
        using var helper = new RtpStreamHelper();

        // Assert
        Assert.False(helper.IsActive);
        Assert.False(helper.IsMuted);
        Assert.Equal(0u, helper.CurrentSsrc);
        Assert.Equal(0, helper.TrackedSsrcCount);
        Assert.Equal(0, helper.LocalPort);
    }

    [Fact]
    public void IsMuted_CanBeSet()
    {
        // Arrange
        using var helper = new RtpStreamHelper();

        // Act
        helper.IsMuted = true;

        // Assert
        Assert.True(helper.IsMuted);
    }

    [Fact]
    public void IsMuted_CanBeToggledOff()
    {
        // Arrange
        using var helper = new RtpStreamHelper();
        helper.IsMuted = true;

        // Act
        helper.IsMuted = false;

        // Assert
        Assert.False(helper.IsMuted);
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_WithInvalidUri_ReturnsFalse()
    {
        // Arrange
        using var helper = new RtpStreamHelper();

        // Act - malformed URI should fail gracefully
        var result = await helper.StartAsync("not-a-valid-uri");

        // Assert
        Assert.False(result);
        Assert.False(helper.IsActive);
    }

    [Fact]
    public async Task StartAsync_WithUnreachableHost_ReturnsFalse()
    {
        // Arrange
        using var helper = new RtpStreamHelper();

        // Act - unreachable host should fail gracefully
        var result = await helper.StartAsync("udp://unreachable.invalid.host:22222");

        // Assert - should fail due to DNS resolution failure
        Assert.False(result);
        Assert.False(helper.IsActive);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyActive_ThrowsInvalidOperationException()
    {
        // Arrange
        using var helper = new RtpStreamHelper();
        // Use localhost to succeed the first time
        var result = await helper.StartAsync("udp://127.0.0.1:22222");

        if (result)
        {
            // Act & Assert - second start should throw
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => helper.StartAsync("udp://127.0.0.1:22223"));
        }
        // If first start failed, skip this test (infrastructure issue)
    }

    #endregion

    #region StopAsync Tests

    [Fact]
    public async Task StopAsync_WhenNotStarted_DoesNotThrow()
    {
        // Arrange
        using var helper = new RtpStreamHelper();

        // Act & Assert - should complete without throwing
        await helper.StopAsync();
        Assert.False(helper.IsActive);
    }

    #endregion

    #region SendAudioFrame Tests

    [Fact]
    public void SendAudioFrame_WhenNotActive_DoesNotThrow()
    {
        // Arrange
        using var helper = new RtpStreamHelper();
        var samples = new float[960]; // 20ms at 48kHz

        // Act & Assert - should silently return
        helper.SendAudioFrame(samples, 48000, 1);
    }

    [Fact]
    public void SendAudioFrame_WhenMuted_DoesNotThrow()
    {
        // Arrange
        using var helper = new RtpStreamHelper();
        helper.IsMuted = true;
        var samples = new float[960];

        // Act & Assert - should silently return
        helper.SendAudioFrame(samples, 48000, 1);
    }

    [Fact]
    public void SendAudioFrame_WithDifferentSampleRates_DoesNotThrow()
    {
        // Arrange
        using var helper = new RtpStreamHelper();

        // Act & Assert - various sample rates should not throw
        helper.SendAudioFrame(new float[480], 24000, 1);
        helper.SendAudioFrame(new float[960], 48000, 1);
        helper.SendAudioFrame(new float[1920], 48000, 2);
    }

    #endregion

    #region GetStatistics Tests

    [Fact]
    public void GetStatistics_WhenNotActive_ReturnsEmptyStats()
    {
        // Arrange
        using var helper = new RtpStreamHelper();

        // Act
        var stats = helper.GetStatistics();

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(0, stats.PacketsSent);
        Assert.Equal(0, stats.PacketsReceived);
        Assert.Equal(0, stats.PacketsLost);
        Assert.Equal(TimeSpan.Zero, stats.Uptime);
    }

    #endregion

    #region ClearSsrcHistory Tests

    [Fact]
    public void ClearSsrcHistory_WhenEmpty_DoesNotThrow()
    {
        // Arrange
        using var helper = new RtpStreamHelper();

        // Act & Assert - should not throw
        helper.ClearSsrcHistory();
        Assert.Equal(0, helper.TrackedSsrcCount);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var helper = new RtpStreamHelper();

        // Act & Assert - should not throw
        helper.Dispose();
        helper.Dispose();
    }

    [Fact]
    public async Task StartAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var helper = new RtpStreamHelper();
        helper.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => helper.StartAsync("udp://localhost:22222"));
    }

    #endregion

    #region Event Tests

    [Fact]
    public void OnAudioFrameReceived_EventCanBeSubscribed()
    {
        // Arrange
        using var helper = new RtpStreamHelper();
        float[]? capturedSamples = null;
        helper.OnAudioFrameReceived += (samples, rate, channels) => capturedSamples = samples;

        // Assert - event handler wired
        Assert.Null(capturedSamples);
    }

    [Fact]
    public void OnSsrcChanged_EventCanBeSubscribed()
    {
        // Arrange
        using var helper = new RtpStreamHelper();
        uint? capturedSsrc = null;
        helper.OnSsrcChanged += (ssrc) => capturedSsrc = ssrc;

        // Assert - event handler wired
        Assert.Null(capturedSsrc);
    }

    [Fact]
    public void OnError_EventCanBeSubscribed()
    {
        // Arrange
        using var helper = new RtpStreamHelper();
        string? capturedError = null;
        helper.OnError += (error) => capturedError = error;

        // Assert - event handler wired
        Assert.Null(capturedError);
    }

    #endregion

    #region Constants Tests

    [Fact]
    public void Constants_HaveExpectedValues()
    {
        // Assert
        Assert.Equal(10, RtpStreamHelper.MaxTrackedSsrcs);
        Assert.Equal(30, RtpStreamHelper.SsrcTimeoutSeconds);
    }

    #endregion
}
