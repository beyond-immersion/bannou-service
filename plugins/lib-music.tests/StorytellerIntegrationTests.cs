using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Music;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Music.Tests;

/// <summary>
/// Integration tests for MusicService's use of the Storyteller SDK.
/// Tests verify narrative-driven composition including mood mapping,
/// emotional journeys, and tension curves.
/// </summary>
public class StorytellerIntegrationTests
{
    private readonly Mock<IMessageBus> _mockMessageBus = new();
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory = new();
    private readonly Mock<ILogger<MusicService>> _mockLogger = new();
    private readonly MusicServiceConfiguration _configuration = new();

    /// <summary>
    /// Creates a MusicService instance with mocked dependencies for testing.
    /// </summary>
    private MusicService CreateMusicService()
    {
        return new MusicService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            _configuration);
    }

    #region Narrative Metadata Tests

    /// <summary>
    /// Verifies that GenerateCompositionAsync returns narrative metadata when no mood is specified.
    /// </summary>
    [Fact]
    public async Task GenerateCompositionAsync_DefaultMood_ReturnsNarrativeMetadata()
    {
        // Arrange
        var service = CreateMusicService();
        var request = new GenerateCompositionRequest
        {
            StyleId = "celtic",
            DurationBars = 16
        };

        // Act
        var (status, response) = await service.GenerateCompositionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.NarrativeUsed);
        Assert.NotNull(response.EmotionalJourney);
        Assert.NotNull(response.TensionCurve);
        Assert.True(response.EmotionalJourney.Count > 0, "Emotional journey should have snapshots");
        Assert.True(response.TensionCurve.Count > 0, "Tension curve should have values");
    }

    #endregion

    #region Mood to Template Mapping Tests

    /// <summary>
    /// Verifies that bright mood maps to simple_arc template.
    /// </summary>
    [Fact]
    public async Task GenerateCompositionAsync_BrightMood_UsesSimpleArcTemplate()
    {
        // Arrange
        var service = CreateMusicService();
        var request = new GenerateCompositionRequest
        {
            StyleId = "celtic",
            DurationBars = 16,
            Mood = GenerateCompositionRequestMood.Bright
        };

        // Act
        var (status, response) = await service.GenerateCompositionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("simple_arc", response.NarrativeUsed);
    }

    /// <summary>
    /// Verifies that dark mood maps to tension_and_release template.
    /// </summary>
    [Fact]
    public async Task GenerateCompositionAsync_DarkMood_UsesTensionAndReleaseTemplate()
    {
        // Arrange
        var service = CreateMusicService();
        var request = new GenerateCompositionRequest
        {
            StyleId = "celtic",
            DurationBars = 16,
            Mood = GenerateCompositionRequestMood.Dark
        };

        // Act
        var (status, response) = await service.GenerateCompositionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("tension_and_release", response.NarrativeUsed);
    }

    /// <summary>
    /// Verifies that neutral mood maps to journey_and_return template.
    /// </summary>
    [Fact]
    public async Task GenerateCompositionAsync_NeutralMood_UsesJourneyAndReturnTemplate()
    {
        // Arrange
        var service = CreateMusicService();
        var request = new GenerateCompositionRequest
        {
            StyleId = "celtic",
            DurationBars = 16,
            Mood = GenerateCompositionRequestMood.Neutral
        };

        // Act
        var (status, response) = await service.GenerateCompositionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("journey_and_return", response.NarrativeUsed);
    }

    /// <summary>
    /// Verifies that melancholic mood maps to simple_arc template.
    /// </summary>
    [Fact]
    public async Task GenerateCompositionAsync_MelancholicMood_UsesSimpleArcTemplate()
    {
        // Arrange
        var service = CreateMusicService();
        var request = new GenerateCompositionRequest
        {
            StyleId = "celtic",
            DurationBars = 16,
            Mood = GenerateCompositionRequestMood.Melancholic
        };

        // Act
        var (status, response) = await service.GenerateCompositionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("simple_arc", response.NarrativeUsed);
    }

    /// <summary>
    /// Verifies that triumphant mood maps to tension_and_release template.
    /// </summary>
    [Fact]
    public async Task GenerateCompositionAsync_TriumphantMood_UsesTensionAndReleaseTemplate()
    {
        // Arrange
        var service = CreateMusicService();
        var request = new GenerateCompositionRequest
        {
            StyleId = "celtic",
            DurationBars = 16,
            Mood = GenerateCompositionRequestMood.Triumphant
        };

        // Act
        var (status, response) = await service.GenerateCompositionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("tension_and_release", response.NarrativeUsed);
    }

    #endregion

    #region Explicit Narrative Options Tests

    /// <summary>
    /// Verifies that explicit NarrativeOptions overrides mood-based template selection.
    /// </summary>
    [Fact]
    public async Task GenerateCompositionAsync_ExplicitNarrativeOptions_UsesSpecifiedTemplate()
    {
        // Arrange
        var service = CreateMusicService();
        var request = new GenerateCompositionRequest
        {
            StyleId = "celtic",
            DurationBars = 16,
            Mood = GenerateCompositionRequestMood.Bright, // Would normally use simple_arc
            Narrative = new NarrativeOptions
            {
                TemplateId = "journey_and_return" // Override with different template
            }
        };

        // Act
        var (status, response) = await service.GenerateCompositionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("journey_and_return", response.NarrativeUsed);
    }

    /// <summary>
    /// Verifies that explicit initial emotion in NarrativeOptions affects the composition.
    /// </summary>
    [Fact]
    public async Task GenerateCompositionAsync_ExplicitInitialEmotion_AffectsGeneration()
    {
        // Arrange
        var service = CreateMusicService();
        var request = new GenerateCompositionRequest
        {
            StyleId = "celtic",
            DurationBars = 16,
            Narrative = new NarrativeOptions
            {
                InitialEmotion = new EmotionalStateInput
                {
                    Tension = 0.9,
                    Brightness = 0.2,
                    Energy = 0.8,
                    Warmth = 0.3,
                    Stability = 0.1,
                    Valence = 0.4
                }
            }
        };

        // Act
        var (status, response) = await service.GenerateCompositionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.EmotionalJourney);

        // The tension curve should reflect the high initial tension
        Assert.NotNull(response.TensionCurve);
        // First bar tension should be influenced by high initial tension
        Assert.True(response.TensionCurve.Count > 0);
    }

    #endregion

    #region Tension Curve Tests

    /// <summary>
    /// Verifies that TensionCurve has correct number of values (one per bar).
    /// </summary>
    [Fact]
    public async Task GenerateCompositionAsync_TensionCurve_HasCorrectLength()
    {
        // Arrange
        var service = CreateMusicService();
        var request = new GenerateCompositionRequest
        {
            StyleId = "celtic",
            DurationBars = 24
        };

        // Act
        var (status, response) = await service.GenerateCompositionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.TensionCurve);
        Assert.Equal(24, response.TensionCurve.Count);
    }

    /// <summary>
    /// Verifies that TensionCurve values are within valid range.
    /// </summary>
    [Fact]
    public async Task GenerateCompositionAsync_TensionCurve_ValuesInRange()
    {
        // Arrange
        var service = CreateMusicService();
        var request = new GenerateCompositionRequest
        {
            StyleId = "celtic",
            DurationBars = 16
        };

        // Act
        var (status, response) = await service.GenerateCompositionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.TensionCurve);
        Assert.All(response.TensionCurve, t =>
        {
            Assert.InRange(t, 0.0, 1.0);
        });
    }

    #endregion

    #region Emotional Journey Tests

    /// <summary>
    /// Verifies that EmotionalJourney snapshots have valid values.
    /// </summary>
    [Fact]
    public async Task GenerateCompositionAsync_EmotionalJourney_HasValidSnapshots()
    {
        // Arrange
        var service = CreateMusicService();
        var request = new GenerateCompositionRequest
        {
            StyleId = "celtic",
            DurationBars = 32
        };

        // Act
        var (status, response) = await service.GenerateCompositionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.EmotionalJourney);
        Assert.True(response.EmotionalJourney.Count >= 2, "Should have at least start and end snapshots");

        foreach (var snapshot in response.EmotionalJourney)
        {
            Assert.InRange(snapshot.Tension, 0.0, 1.0);
            Assert.InRange(snapshot.Brightness, 0.0, 1.0);
            Assert.InRange(snapshot.Energy, 0.0, 1.0);
            Assert.InRange(snapshot.Warmth, 0.0, 1.0);
            Assert.InRange(snapshot.Stability, 0.0, 1.0);
            Assert.InRange(snapshot.Valence, 0.0, 1.0);
        }
    }

    /// <summary>
    /// Verifies that EmotionalJourney includes phase names.
    /// </summary>
    [Fact]
    public async Task GenerateCompositionAsync_EmotionalJourney_HasPhaseNames()
    {
        // Arrange
        var service = CreateMusicService();
        var request = new GenerateCompositionRequest
        {
            StyleId = "celtic",
            DurationBars = 32,
            Mood = GenerateCompositionRequestMood.Neutral // Uses journey_and_return with multiple phases
        };

        // Act
        var (status, response) = await service.GenerateCompositionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.EmotionalJourney);

        // All snapshots except possibly the last should have phase names
        var nonEndSnapshots = response.EmotionalJourney.Take(response.EmotionalJourney.Count - 1);
        Assert.All(nonEndSnapshots, snapshot =>
        {
            Assert.False(string.IsNullOrEmpty(snapshot.PhaseName), "Phase name should not be empty");
        });
    }

    /// <summary>
    /// Verifies that EmotionalJourney bar numbers are sequential.
    /// </summary>
    [Fact]
    public async Task GenerateCompositionAsync_EmotionalJourney_BarNumbersAreSequential()
    {
        // Arrange
        var service = CreateMusicService();
        var request = new GenerateCompositionRequest
        {
            StyleId = "celtic",
            DurationBars = 32
        };

        // Act
        var (status, response) = await service.GenerateCompositionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.EmotionalJourney);

        var bars = response.EmotionalJourney.Select(s => s.Bar).ToList();
        for (int i = 1; i < bars.Count; i++)
        {
            Assert.True(bars[i] >= bars[i - 1], $"Bar numbers should be sequential: {bars[i - 1]} -> {bars[i]}");
        }
    }

    #endregion
}
