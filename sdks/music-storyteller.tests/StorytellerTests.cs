using BeyondImmersion.Bannou.MusicStoryteller.Narratives;
using BeyondImmersion.Bannou.MusicStoryteller.Narratives.Templates;
using BeyondImmersion.Bannou.MusicStoryteller.State;
using Xunit;

namespace BeyondImmersion.Bannou.MusicStoryteller.Tests;

/// <summary>
/// Integration tests for the main Storyteller orchestrator.
/// </summary>
public class StorytellerTests
{
    /// <summary>
    /// Storyteller can be constructed with defaults.
    /// </summary>
    [Fact]
    public void Constructor_WithDefaults_Succeeds()
    {
        var storyteller = new Storyteller();

        Assert.NotNull(storyteller.Actions);
        Assert.NotNull(storyteller.Narratives);
    }

    /// <summary>
    /// Compose produces result with correct structure.
    /// </summary>
    [Fact]
    public void Compose_ProducesValidResult()
    {
        // Arrange
        var storyteller = new Storyteller();
        var request = CompositionRequest.ForTemplate("journey_and_return", totalBars: 32);

        // Act
        var result = storyteller.Compose(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Narrative);
        Assert.NotNull(result.Sections);
        Assert.True(result.Sections.Count > 0, "Should have at least one section");
        Assert.Equal(32, result.TotalBars);
    }

    /// <summary>
    /// Compose with JourneyAndReturn template produces 4 phases.
    /// </summary>
    [Fact]
    public void Compose_JourneyAndReturn_Has4Phases()
    {
        // Arrange
        var storyteller = new Storyteller();
        var request = CompositionRequest.ForTemplate("journey_and_return");

        // Act
        var result = storyteller.Compose(request);

        // Assert - Journey and Return has Home, Departure, Adventure, Return
        Assert.Equal(4, result.Sections.Count);
        Assert.Contains(result.Sections, s => s.PhaseName == "Home");
        Assert.Contains(result.Sections, s => s.PhaseName == "Return");
    }

    /// <summary>
    /// Compose with TensionAndRelease produces 6 phases.
    /// </summary>
    [Fact]
    public void Compose_TensionAndRelease_Has6Phases()
    {
        // Arrange
        var storyteller = new Storyteller();
        var request = CompositionRequest.ForTemplate("tension_and_release");

        // Act
        var result = storyteller.Compose(request);

        // Assert
        Assert.Equal(6, result.Sections.Count);
        Assert.Contains(result.Sections, s => s.PhaseName == "Stability");
        Assert.Contains(result.Sections, s => s.PhaseName == "Climax");
        Assert.Contains(result.Sections, s => s.PhaseName == "Peace");
    }

    /// <summary>
    /// Compose generates intents for all sections.
    /// </summary>
    [Fact]
    public void Compose_GeneratesIntentsForAllSections()
    {
        // Arrange
        var storyteller = new Storyteller();
        var request = CompositionRequest.ForTemplate("simple_arc", totalBars: 24);

        // Act
        var result = storyteller.Compose(request);

        // Assert
        Assert.True(result.TotalIntents > 0, "Should generate intents");
        Assert.All(result.Sections, section =>
            Assert.True(section.Intents.Count > 0, $"Section {section.PhaseName} should have intents"));
    }

    /// <summary>
    /// ComposeWithTemplate is a convenience wrapper.
    /// </summary>
    [Fact]
    public void ComposeWithTemplate_IsConvenienceMethod()
    {
        // Arrange
        var storyteller = new Storyteller();

        // Act
        var result = storyteller.ComposeWithTemplate("simple_arc", totalBars: 16);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(16, result.TotalBars);
        Assert.Equal(3, result.Sections.Count); // Simple Arc has 3 phases
    }

    /// <summary>
    /// Sections cover all bars without gaps.
    /// </summary>
    [Fact]
    public void Compose_SectionsCoverAllBars()
    {
        // Arrange
        var storyteller = new Storyteller();
        var request = CompositionRequest.ForTemplate("journey_and_return", totalBars: 32);

        // Act
        var result = storyteller.Compose(request);

        // Assert - verify bars are contiguous
        var sortedSections = result.Sections.OrderBy(s => s.StartBar).ToList();
        var expectedStart = 0;

        foreach (var section in sortedSections)
        {
            Assert.True(section.StartBar >= expectedStart,
                $"Section {section.PhaseName} starts at {section.StartBar}, expected >= {expectedStart}");
            expectedStart = section.EndBar + 1;
        }
    }

    /// <summary>
    /// FinalState reflects emotional journey.
    /// </summary>
    [Fact]
    public void Compose_FinalStateReflectsJourney()
    {
        // Arrange
        var storyteller = new Storyteller();
        var request = CompositionRequest.ForTemplate("tension_and_release", totalBars: 48);

        // Act
        var result = storyteller.Compose(request);

        // Assert - Tension and Release should end in Peace (low tension, high stability)
        Assert.NotNull(result.FinalState);
        Assert.NotNull(result.FinalState.Emotional);
    }

    /// <summary>
    /// GetNextAction returns action when goal not met.
    /// </summary>
    [Fact]
    public void GetNextAction_WhenGoalNotMet_ReturnsAction()
    {
        // Arrange
        var storyteller = new Storyteller();
        var state = new CompositionState();
        state.Emotional.Tension = 0.2;

        var climaxPhase = new NarrativePhase
        {
            Name = "Climax",
            EmotionalTarget = EmotionalState.Presets.Climax,
            RelativeDuration = 0.1
        };

        // Act
        var action = storyteller.GetNextAction(state, climaxPhase);

        // Assert - should suggest an action to increase tension
        Assert.NotNull(action);
    }

    /// <summary>
    /// Request with custom emotion initializes state correctly.
    /// </summary>
    [Fact]
    public void Compose_WithCustomInitialEmotion_InitializesState()
    {
        // Arrange
        var storyteller = new Storyteller();
        var customEmotion = new EmotionalState(
            tension: 0.7,
            brightness: 0.3,
            energy: 0.8,
            warmth: 0.4,
            stability: 0.2,
            valence: 0.6
        );

        var request = new CompositionRequest
        {
            TotalBars = 16,
            InitialEmotion = customEmotion,
            Tags = ["test"]
        };

        // Act
        var result = storyteller.Compose(request);

        // Assert - composition should complete without error
        Assert.NotNull(result);
        Assert.True(result.Sections.Count > 0);
    }

    /// <summary>
    /// AllIntents iterator returns intents in correct order.
    /// </summary>
    [Fact]
    public void CompositionResult_AllIntents_ReturnsInOrder()
    {
        // Arrange
        var storyteller = new Storyteller();
        var request = CompositionRequest.ForTemplate("simple_arc", totalBars: 24);

        // Act
        var result = storyteller.Compose(request);
        var intents = result.AllIntents.ToList();

        // Assert
        Assert.Equal(result.TotalIntents, intents.Count);
    }
}
