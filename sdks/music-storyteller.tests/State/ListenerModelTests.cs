using BeyondImmersion.Bannou.MusicStoryteller.State;
using Xunit;

namespace BeyondImmersion.Bannou.MusicStoryteller.Tests.State;

/// <summary>
/// Tests for the ITPRA-based ListenerModel.
/// </summary>
public class ListenerModelTests
{
    /// <summary>
    /// Default listener has reasonable starting values.
    /// </summary>
    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        var listener = new ListenerModel();

        Assert.Equal(0.8, listener.Attention);
        Assert.Equal(0.5, listener.SurpriseBudget);
        Assert.False(listener.ExpectedResolution);
        Assert.Equal(0.7, listener.PredictionAccuracy);
        Assert.Equal(0, listener.EventsProcessed);
    }

    /// <summary>
    /// ProcessEvent increments event counter.
    /// </summary>
    [Fact]
    public void ProcessEvent_IncrementsEventCounter()
    {
        var listener = new ListenerModel();

        listener.ProcessEvent(0.5, false, 0.3);

        Assert.Equal(1, listener.EventsProcessed);
    }

    /// <summary>
    /// Surprising events boost attention.
    /// </summary>
    [Fact]
    public void ProcessEvent_SurprisingEvent_BoostsAttention()
    {
        var listener = new ListenerModel { Attention = 0.5 };

        listener.ProcessEvent(surprise: 0.8, wasResolution: false, tensionBefore: 0.5);

        Assert.True(listener.Attention > 0.5, "Surprising event should boost attention");
    }

    /// <summary>
    /// Predictable events decay attention.
    /// </summary>
    [Fact]
    public void ProcessEvent_PredictableEvent_DecaysAttention()
    {
        var listener = new ListenerModel { Attention = 0.8 };

        listener.ProcessEvent(surprise: 0.2, wasResolution: false, tensionBefore: 0.5);

        Assert.True(listener.Attention < 0.8, "Predictable event should decay attention");
    }

    /// <summary>
    /// Resolution after expected resolution creates pleasure.
    /// </summary>
    [Fact]
    public void ProcessEvent_ResolutionWithTension_IncreasesPleasure()
    {
        var listener = new ListenerModel
        {
            ExpectedResolution = true,
            AccumulatedPleasure = 0
        };

        listener.ProcessEvent(surprise: 0.2, wasResolution: true, tensionBefore: 0.8);

        Assert.True(listener.AccumulatedPleasure > 0, "Resolution should create pleasure");
        Assert.False(listener.ExpectedResolution, "Resolution should clear expectation");
    }

    /// <summary>
    /// Contrastive valence: higher prior tension = more pleasure.
    /// </summary>
    [Fact]
    public void ProcessEvent_HigherTensionGivesMorePleasure()
    {
        var listener1 = new ListenerModel { ExpectedResolution = true };
        var listener2 = new ListenerModel { ExpectedResolution = true };

        listener1.ProcessEvent(0.2, true, tensionBefore: 0.3);
        listener2.ProcessEvent(0.2, true, tensionBefore: 0.9);

        Assert.True(listener2.AccumulatedPleasure > listener1.AccumulatedPleasure,
            "Higher tension should give more resolution pleasure");
    }

    /// <summary>
    /// RegisterTensionEvent sets expected resolution flag.
    /// </summary>
    [Fact]
    public void RegisterTensionEvent_SetsExpectedResolution()
    {
        var listener = new ListenerModel();

        listener.RegisterTensionEvent();

        Assert.True(listener.ExpectedResolution);
    }

    /// <summary>
    /// RegisterTensionEvent boosts attention.
    /// </summary>
    [Fact]
    public void RegisterTensionEvent_BoostsAttention()
    {
        var listener = new ListenerModel { Attention = 0.5 };

        listener.RegisterTensionEvent();

        Assert.True(listener.Attention > 0.5);
    }

    /// <summary>
    /// Engagement is average of attention and accuracy.
    /// </summary>
    [Fact]
    public void Engagement_IsAverageOfAttentionAndAccuracy()
    {
        var listener = new ListenerModel
        {
            Attention = 0.6,
            PredictionAccuracy = 0.8
        };

        Assert.Equal(0.7, listener.Engagement, precision: 5);
    }

    /// <summary>
    /// IsAnticipating requires both expected resolution and high attention.
    /// </summary>
    [Fact]
    public void IsAnticipating_RequiresBothConditions()
    {
        var listener1 = new ListenerModel { ExpectedResolution = true, Attention = 0.8 };
        var listener2 = new ListenerModel { ExpectedResolution = true, Attention = 0.4 };
        var listener3 = new ListenerModel { ExpectedResolution = false, Attention = 0.8 };

        Assert.True(listener1.IsAnticipating);
        Assert.False(listener2.IsAnticipating); // Low attention
        Assert.False(listener3.IsAnticipating); // No expected resolution
    }

    /// <summary>
    /// GetRecommendedSurprise returns low when expecting resolution.
    /// </summary>
    [Fact]
    public void GetRecommendedSurprise_WhenExpectingResolution_ReturnsLow()
    {
        var listener = new ListenerModel
        {
            ExpectedResolution = true,
            Attention = 0.8
        };

        var recommended = listener.GetRecommendedSurprise();

        Assert.True(recommended < 0.3, $"Should recommend low surprise when expecting resolution: {recommended}");
    }

    /// <summary>
    /// GetRecommendedSurprise returns higher when attention is low.
    /// </summary>
    [Fact]
    public void GetRecommendedSurprise_WhenLowAttention_ReturnsHigher()
    {
        var listener = new ListenerModel
        {
            ExpectedResolution = false,
            Attention = 0.3,
            SurpriseBudget = 0.4
        };

        var recommended = listener.GetRecommendedSurprise();

        Assert.True(recommended > listener.SurpriseBudget,
            "Should recommend higher surprise to re-engage when attention low");
    }

    /// <summary>
    /// Clone creates independent copy.
    /// </summary>
    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var original = new ListenerModel
        {
            Attention = 0.9,
            ExpectedResolution = true,
            EventsProcessed = 10
        };

        var clone = original.Clone();
        clone.Attention = 0.1;
        clone.ExpectedResolution = false;

        Assert.Equal(0.9, original.Attention);
        Assert.True(original.ExpectedResolution);
        Assert.Equal(10, original.EventsProcessed);
    }

    /// <summary>
    /// SchematicExpectations can load style defaults.
    /// </summary>
    [Fact]
    public void SchematicExpectations_LoadStyleDefaults_Updates()
    {
        var expectations = new SchematicExpectations();
        expectations.LoadStyleDefaults("celtic");

        Assert.Equal(0.8, expectations.AuthenticCadenceProbability);
    }

    /// <summary>
    /// DynamicExpectations detects repeating patterns.
    /// </summary>
    [Fact]
    public void DynamicExpectations_DetectsRepeatingPattern()
    {
        var dynamic = new DynamicExpectations();

        // Add a repeating pattern: 2, 3, 2, 3
        dynamic.AddInterval(2);
        dynamic.AddInterval(3);
        dynamic.AddInterval(2);
        dynamic.AddInterval(3);
        dynamic.AddInterval(2);
        dynamic.AddInterval(3);

        Assert.True(dynamic.PatternDetected, "Should detect repeating pattern");
        Assert.Equal(2, dynamic.PatternLength);
    }

    /// <summary>
    /// ConsciousExpectations updates based on harmonic context.
    /// </summary>
    [Fact]
    public void ConsciousExpectations_UpdatesOnDominant()
    {
        var conscious = new ConsciousExpectations();

        conscious.Update(isOnDominant: true, barsIntoPhrase: 3, phraseLength: 4);

        Assert.True(conscious.ExpectingCadence);
        Assert.Equal("I", conscious.ExpectedHarmony);
    }
}
