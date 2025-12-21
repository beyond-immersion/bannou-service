using BeyondImmersion.Bannou.Voice;
using BeyondImmersion.Bannou.Voice.Services;
using Xunit;

namespace BeyondImmersion.Bannou.Voice.Tests;

public class SipRegistrationHelperTests
{
    #region Initial State Tests

    [Fact]
    public void InitialState_IsUnregistered()
    {
        // Arrange & Act
        using var helper = new SipRegistrationHelper();

        // Assert
        Assert.Equal(SipRegistrationState.Unregistered, helper.State);
        Assert.False(helper.IsRegistered);
    }

    [Fact]
    public void Username_InitiallyNull()
    {
        // Arrange & Act
        using var helper = new SipRegistrationHelper();

        // Assert
        Assert.Null(helper.Username);
    }

    #endregion

    #region Constants Tests

    [Fact]
    public void Constants_HaveExpectedValues()
    {
        // Assert
        Assert.Equal(5060, SipRegistrationHelper.DefaultSipPort);
        Assert.Equal(300, SipRegistrationHelper.DefaultExpirySeconds);
        Assert.Equal(60, SipRegistrationHelper.ExpirationWarningThresholdSeconds);
    }

    #endregion

    #region RegisterAsync State Tests

    [Fact]
    public async Task RegisterAsync_WhenAlreadyRegistering_ThrowsInvalidOperationException()
    {
        // Arrange
        using var helper = new SipRegistrationHelper();
        var credentials = CreateValidCredentials();

        // Start registration without waiting
        var registrationTask = helper.RegisterAsync(credentials);

        // Act & Assert - second registration attempt should throw
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => helper.RegisterAsync(credentials));

        // Clean up - cancel the first registration
        try
        {
            await registrationTask;
        }
        catch
        {
            // Ignore errors from first registration
        }
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var helper = new SipRegistrationHelper();

        // Act & Assert - should not throw
        helper.Dispose();
        helper.Dispose();
    }

    [Fact]
    public async Task RegisterAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var helper = new SipRegistrationHelper();
        helper.Dispose();

        var credentials = CreateValidCredentials();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => helper.RegisterAsync(credentials));
    }

    [Fact]
    public async Task UnregisterAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var helper = new SipRegistrationHelper();
        helper.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => helper.UnregisterAsync());
    }

    #endregion

    #region UnregisterAsync Tests

    [Fact]
    public async Task UnregisterAsync_WhenNotRegistered_DoesNotThrow()
    {
        // Arrange
        using var helper = new SipRegistrationHelper();

        // Act & Assert - should complete without throwing
        await helper.UnregisterAsync();
        Assert.Equal(SipRegistrationState.Unregistered, helper.State);
    }

    #endregion

    #region RefreshAsync Tests

    [Fact]
    public async Task RefreshAsync_WhenNotRegistered_ThrowsInvalidOperationException()
    {
        // Arrange
        using var helper = new SipRegistrationHelper();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => helper.RefreshAsync());
    }

    #endregion

    #region Event Tests

    [Fact]
    public void OnStateChanged_EventCanBeSubscribed()
    {
        // Arrange
        using var helper = new SipRegistrationHelper();
        SipRegistrationState? capturedState = null;
        helper.OnStateChanged += (state) => capturedState = state;

        // Assert - event handler wired (no invocation yet)
        Assert.Null(capturedState);
    }

    [Fact]
    public void OnRegistered_EventCanBeSubscribed()
    {
        // Arrange
        using var helper = new SipRegistrationHelper();
        var invoked = false;
        helper.OnRegistered += () => invoked = true;

        // Assert - event handler wired (no invocation yet)
        Assert.False(invoked);
    }

    [Fact]
    public void OnRegistrationFailed_EventCanBeSubscribed()
    {
        // Arrange
        using var helper = new SipRegistrationHelper();
        int? capturedCode = null;
        helper.OnRegistrationFailed += (code, msg) => capturedCode = code;

        // Assert - event handler wired
        Assert.Null(capturedCode);
    }

    [Fact]
    public void OnRegistrationExpiring_EventCanBeSubscribed()
    {
        // Arrange
        using var helper = new SipRegistrationHelper();
        int? capturedSeconds = null;
        helper.OnRegistrationExpiring += (secs) => capturedSeconds = secs;

        // Assert - event handler wired
        Assert.Null(capturedSeconds);
    }

    [Fact]
    public void OnUnregistered_EventCanBeSubscribed()
    {
        // Arrange
        using var helper = new SipRegistrationHelper();
        var invoked = false;
        helper.OnUnregistered += () => invoked = true;

        // Assert - event handler wired
        Assert.False(invoked);
    }

    #endregion

    #region Helper Methods

    private static SipConnectionCredentials CreateValidCredentials()
    {
        return new SipConnectionCredentials
        {
            Username = "test-user",
            Password = "test-pass",
            Domain = "voice.test.local",
            ConferenceUri = "sip:room@voice.test.local"
        };
    }

    #endregion
}
