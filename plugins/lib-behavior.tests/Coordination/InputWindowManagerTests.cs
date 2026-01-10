// =============================================================================
// Input Window Manager Tests
// Tests for timed input windows (QTEs, choices).
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Behavior.Coordination;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Coordination;

/// <summary>
/// Tests for <see cref="InputWindowManager"/>.
/// </summary>
public sealed class InputWindowManagerTests : IDisposable
{
    private readonly InputWindowManager _manager;
    private readonly Guid _targetEntity;

    public InputWindowManagerTests()
    {
        _manager = new InputWindowManager(TimeSpan.FromSeconds(10));
        _targetEntity = Guid.NewGuid();
    }

    public void Dispose()
    {
        _manager.Dispose();
    }

    // =========================================================================
    // CREATION TESTS
    // =========================================================================

    [Fact]
    public async Task CreateAsync_ReturnsValidWindow()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity,
            WindowType = InputWindowType.Choice,
            Options = new[]
            {
                new InputOption { Value = "a", Label = "Option A" },
                new InputOption { Value = "b", Label = "Option B" }
            }
        };

        // Act
        var window = await _manager.CreateAsync(options);

        // Assert
        Assert.NotNull(window);
        Assert.Equal(_targetEntity, window.TargetEntity);
        Assert.Equal(InputWindowType.Choice, window.WindowType);
        Assert.Equal(InputWindowState.Waiting, window.State);
    }

    [Fact]
    public async Task CreateAsync_WithCustomId_UsesProvidedId()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity,
            WindowId = "custom-id"
        };

        // Act
        var window = await _manager.CreateAsync(options);

        // Assert
        Assert.Equal("custom-id", window.WindowId);
    }

    [Fact]
    public async Task CreateAsync_DuplicateId_ThrowsInvalidOperation()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity,
            WindowId = "duplicate"
        };
        await _manager.CreateAsync(options);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.CreateAsync(options));
    }

    [Fact]
    public async Task CreateAsync_WithDefaultOption_SetsDefaultValue()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity,
            WindowType = InputWindowType.Choice,
            Options = new[]
            {
                new InputOption { Value = "a", Label = "Option A" },
                new InputOption { Value = "b", Label = "Option B", IsDefault = true }
            }
        };

        // Act
        var window = await _manager.CreateAsync(options);

        // Assert
        Assert.Equal("b", window.DefaultValue);
    }

    // =========================================================================
    // SUBMIT TESTS
    // =========================================================================

    [Fact]
    public async Task SubmitAsync_ValidInput_AcceptsAndAdjudicates()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity,
            WindowType = InputWindowType.Choice,
            Options = new[]
            {
                new InputOption { Value = "a", Label = "Option A" },
                new InputOption { Value = "b", Label = "Option B" }
            }
        };
        var window = await _manager.CreateAsync(options);

        // Act
        var result = await _manager.SubmitAsync(window.WindowId, _targetEntity, "a");

        // Assert
        Assert.True(result.Accepted);
        Assert.Equal("a", result.AdjudicatedValue);
        Assert.False(result.WasDefault);
    }

    [Fact]
    public async Task SubmitAsync_InvalidOption_RejectsInput()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity,
            WindowType = InputWindowType.Choice,
            Options = new[]
            {
                new InputOption { Value = "a", Label = "Option A" },
                new InputOption { Value = "b", Label = "Option B" }
            }
        };
        var window = await _manager.CreateAsync(options);

        // Act
        var result = await _manager.SubmitAsync(window.WindowId, _targetEntity, "invalid");

        // Assert
        Assert.False(result.Accepted);
        Assert.NotNull(result.RejectionReason);
    }

    [Fact]
    public async Task SubmitAsync_WrongEntity_RejectsInput()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity
        };
        var window = await _manager.CreateAsync(options);
        var wrongEntity = Guid.NewGuid();

        // Act
        var result = await _manager.SubmitAsync(window.WindowId, wrongEntity, "input");

        // Assert
        Assert.False(result.Accepted);
        Assert.Contains("not the target", result.RejectionReason);
    }

    [Fact]
    public async Task SubmitAsync_NonexistentWindow_RejectsInput()
    {
        // Act
        var result = await _manager.SubmitAsync("nonexistent", _targetEntity, "input");

        // Assert
        Assert.False(result.Accepted);
        Assert.Contains("not found", result.RejectionReason);
    }

    [Fact]
    public async Task SubmitAsync_AlreadySubmitted_RejectsInput()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity
        };
        var window = await _manager.CreateAsync(options);
        await _manager.SubmitAsync(window.WindowId, _targetEntity, "first");

        // Act
        var result = await _manager.SubmitAsync(window.WindowId, _targetEntity, "second");

        // Assert
        Assert.False(result.Accepted);
        Assert.Contains("not accepting", result.RejectionReason);
    }

    // =========================================================================
    // WINDOW STATE TESTS
    // =========================================================================

    [Fact]
    public async Task Window_AfterSubmit_StateIsSubmitted()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity
        };
        var window = await _manager.CreateAsync(options);

        // Act
        await _manager.SubmitAsync(window.WindowId, _targetEntity, "input");

        // Assert
        Assert.Equal(InputWindowState.Submitted, window.State);
        Assert.True(window.IsCompleted);
        Assert.True(window.HasInput);
    }

    [Fact]
    public async Task Window_TimeRemaining_DecreasesOverTime()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity,
            Timeout = TimeSpan.FromSeconds(5)
        };
        var window = await _manager.CreateAsync(options);
        var initialRemaining = window.TimeRemaining;

        // Act
        await Task.Delay(100);
        var laterRemaining = window.TimeRemaining;

        // Assert
        Assert.NotNull(initialRemaining);
        Assert.NotNull(laterRemaining);
        Assert.True(laterRemaining < initialRemaining);
    }

    // =========================================================================
    // CLOSE TESTS
    // =========================================================================

    [Fact]
    public async Task Close_WithUseDefault_SetsDefaultValue()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity,
            DefaultValue = "default"
        };
        var window = await _manager.CreateAsync(options);

        // Act
        _manager.Close(window.WindowId, useDefault: true);

        // Assert
        Assert.Equal(InputWindowState.TimedOut, window.State);
        Assert.Equal("default", window.FinalValue);
    }

    [Fact]
    public async Task Close_WithoutDefault_SetsClosedState()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity
        };
        var window = await _manager.CreateAsync(options);

        // Act
        _manager.Close(window.WindowId, useDefault: false);

        // Assert
        Assert.Equal(InputWindowState.Closed, window.State);
    }

    // =========================================================================
    // GET WINDOWS TESTS
    // =========================================================================

    [Fact]
    public async Task GetWindow_ExistingWindow_ReturnsWindow()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity
        };
        var created = await _manager.CreateAsync(options);

        // Act
        var found = _manager.GetWindow(created.WindowId);

        // Assert
        Assert.NotNull(found);
        Assert.Equal(created.WindowId, found.WindowId);
    }

    [Fact]
    public void GetWindow_NonexistentWindow_ReturnsNull()
    {
        // Act
        var found = _manager.GetWindow("nonexistent");

        // Assert
        Assert.Null(found);
    }

    [Fact]
    public async Task GetWindowsForEntity_ReturnsMatchingWindows()
    {
        // Arrange
        var entity1 = Guid.NewGuid();
        var entity2 = Guid.NewGuid();

        await _manager.CreateAsync(new InputWindowOptions { TargetEntity = entity1 });
        await _manager.CreateAsync(new InputWindowOptions { TargetEntity = entity1 });
        await _manager.CreateAsync(new InputWindowOptions { TargetEntity = entity2 });

        // Act
        var windows = _manager.GetWindowsForEntity(entity1);

        // Assert
        Assert.Equal(2, windows.Count);
        Assert.All(windows, w => Assert.Equal(entity1, w.TargetEntity));
    }

    [Fact]
    public async Task ActiveWindows_ReturnsOnlyWaiting()
    {
        // Arrange
        var window1 = await _manager.CreateAsync(new InputWindowOptions { TargetEntity = _targetEntity });
        var window2 = await _manager.CreateAsync(new InputWindowOptions { TargetEntity = _targetEntity });

        await _manager.SubmitAsync(window1.WindowId, _targetEntity, "input");

        // Act
        var active = _manager.ActiveWindows;

        // Assert
        Assert.Single(active);
        Assert.Equal(window2.WindowId, active.First().WindowId);
    }

    // =========================================================================
    // EVENTS TESTS
    // =========================================================================

    [Fact]
    public async Task WindowCompleted_RaisesOnSubmit()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity
        };
        var window = await _manager.CreateAsync(options);

        InputWindowCompletedEventArgs? receivedArgs = null;
        _manager.WindowCompleted += (_, args) => receivedArgs = args;

        // Act
        await _manager.SubmitAsync(window.WindowId, _targetEntity, "input");

        // Assert
        Assert.NotNull(receivedArgs);
        Assert.Equal(window.WindowId, receivedArgs.WindowId);
        Assert.Equal(_targetEntity, receivedArgs.TargetEntity);
        Assert.False(receivedArgs.WasDefault);
    }

    // =========================================================================
    // WAIT FOR COMPLETION TESTS
    // =========================================================================

    [Fact]
    public async Task WaitForCompletionAsync_ReturnsOnSubmit()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity
        };
        var window = await _manager.CreateAsync(options);

        // Act
        var submitTask = Task.Run(async () =>
        {
            await Task.Delay(50);
            await _manager.SubmitAsync(window.WindowId, _targetEntity, "input");
        });

        var result = await window.WaitForCompletionAsync();

        // Assert
        Assert.Equal("input", result);
        await submitTask;
    }

    // =========================================================================
    // CONFIRMATION TYPE TESTS
    // =========================================================================

    [Fact]
    public async Task SubmitAsync_ConfirmationType_ValidatesBoolean()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity,
            WindowType = InputWindowType.Confirmation
        };
        var window = await _manager.CreateAsync(options);

        // Act
        var result = await _manager.SubmitAsync(window.WindowId, _targetEntity, true);

        // Assert
        Assert.True(result.Accepted);
    }

    [Fact]
    public async Task SubmitAsync_ConfirmationType_AcceptsBooleanString()
    {
        // Arrange
        var options = new InputWindowOptions
        {
            TargetEntity = _targetEntity,
            WindowType = InputWindowType.Confirmation
        };
        var window = await _manager.CreateAsync(options);

        // Act
        var result = await _manager.SubmitAsync(window.WindowId, _targetEntity, "true");

        // Assert
        Assert.True(result.Accepted);
    }
}
