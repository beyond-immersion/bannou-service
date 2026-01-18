using BeyondImmersion.Bannou.MusicStoryteller.Theory;
using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Pitch;
using Xunit;

namespace BeyondImmersion.Bannou.MusicStoryteller.Tests.Theory;

/// <summary>
/// Tests for Lerdahl's melodic attraction formula α = (s₂/s₁) × (1/n²).
/// Higher attraction values indicate stronger pull toward the target.
/// </summary>
public class MelodicAttractionTests
{
    /// <summary>
    /// Leading tone (B) to tonic (C) should have strong attraction.
    /// This is the classic Ti→Do resolution.
    /// </summary>
    [Fact]
    public void Calculate_LeadingToneToTonic_HasHighAttraction()
    {
        // Arrange - B4 (MIDI 71) to C5 (MIDI 72) in C major context
        var context = new BasicSpace(PitchClass.C, ModeType.Major);
        var leadingTone = 71; // B4
        var tonic = 72; // C5

        // Act
        var attraction = MelodicAttraction.Calculate(leadingTone, tonic, context);

        // Assert - should be high because stable target (root) + small distance
        Assert.True(attraction > MelodicAttraction.Reference.StrongAttractionThreshold,
            $"Leading tone to tonic attraction ({attraction}) should be > {MelodicAttraction.Reference.StrongAttractionThreshold}");
    }

    /// <summary>
    /// Same pitch has zero attraction (no movement).
    /// </summary>
    [Fact]
    public void Calculate_SamePitch_ReturnsZero()
    {
        // Arrange
        var context = new BasicSpace(PitchClass.C, ModeType.Major);
        var pitch = 60; // C4

        // Act
        var attraction = MelodicAttraction.Calculate(pitch, pitch, context);

        // Assert
        Assert.Equal(0.0, attraction);
    }

    /// <summary>
    /// Attraction decreases with distance squared (1/n²).
    /// </summary>
    [Fact]
    public void Calculate_AttractionDecreasesWithDistance()
    {
        // Arrange
        var context = new BasicSpace(PitchClass.C, ModeType.Major);
        var source = 64; // E4

        // Calculate attractions to C at different octaves
        var attrToC4 = MelodicAttraction.Calculate(source, 60, context);  // 4 semitones
        var attrToC5 = MelodicAttraction.Calculate(source, 72, context);  // 8 semitones
        var attrToC6 = MelodicAttraction.Calculate(source, 84, context);  // 20 semitones

        // Assert - closer targets should have higher attraction
        Assert.True(attrToC4 > attrToC5, "Closer target should have higher attraction");
        Assert.True(attrToC5 > attrToC6, "Medium target should have higher attraction than far");
    }

    /// <summary>
    /// Attraction is stronger toward more stable targets.
    /// Root (stability 4) should attract more than diatonic non-chord tone (stability 1).
    /// </summary>
    [Fact]
    public void Calculate_MoreStableTargetHasHigherAttraction()
    {
        // Arrange
        var context = new BasicSpace(PitchClass.C, ModeType.Major);
        var source = 62; // D4 (scale degree 2)

        // Same distance but different stability
        var attrToC = MelodicAttraction.Calculate(source, 60, context);  // To root (stability 4)
        var attrToE = MelodicAttraction.Calculate(source, 64, context);  // To third (stability 3/2)

        // Assert - both are same distance (2 semitones), but root should attract more
        Assert.True(attrToC >= attrToE,
            $"Root should attract at least as strongly as third (root={attrToC}, third={attrToE})");
    }

    /// <summary>
    /// GetStrongestAttraction should return the pitch class with maximum attraction.
    /// </summary>
    [Fact]
    public void GetStrongestAttraction_ReturnsMaxAttraction()
    {
        // Arrange - chromatic pitch that should resolve to nearby stable tone
        var context = new BasicSpace(PitchClass.C, ModeType.Major);
        var leadingTone = 71; // B4

        // Act
        var (targetPc, attraction) = MelodicAttraction.GetStrongestAttraction(leadingTone, context);

        // Assert - should strongly attract to C (tonic)
        Assert.Equal(0, targetPc); // C = pitch class 0
        Assert.True(attraction > 0);
    }

    /// <summary>
    /// CalculateAllAttractions returns attractions to all diatonic scale degrees.
    /// </summary>
    [Fact]
    public void CalculateAllAttractions_ReturnsDiatonicCount()
    {
        // Arrange
        var context = new BasicSpace(PitchClass.C, ModeType.Major);
        var source = 64; // E4

        // Act
        var attractions = MelodicAttraction.CalculateAllAttractions(source, context);

        // Assert - should have 7 entries for diatonic scale
        Assert.Equal(7, attractions.Count);
    }

    /// <summary>
    /// Expectedness should be high when resolving to strongest attraction target.
    /// </summary>
    [Fact]
    public void CalculateExpectedness_ToStrongestTarget_ReturnsHigh()
    {
        // Arrange
        var context = new BasicSpace(PitchClass.C, ModeType.Major);
        var source = new Pitch(PitchClass.B, 4); // Leading tone
        var (strongestPc, _) = MelodicAttraction.GetStrongestAttraction(source.MidiNumber, context);

        var target = new Pitch((PitchClass)strongestPc, 5);

        // Act
        var expectedness = MelodicAttraction.CalculateExpectedness(source, target, context);

        // Assert - resolving to strongest target should be expected
        Assert.True(expectedness > 0.5, $"Expected high expectedness but got {expectedness}");
    }

    /// <summary>
    /// Voice leading roughness should be low for stepwise motion.
    /// </summary>
    [Fact]
    public void CalculateVoiceLeadingRoughness_Stepwise_IsLow()
    {
        // Arrange
        var context = new BasicSpace(PitchClass.C, ModeType.Major);
        var stepwiseIntervals = new[] { 1, 2, -1, -2, 1 }; // Small intervals

        // Act
        var roughness = MelodicAttraction.CalculateVoiceLeadingRoughness(stepwiseIntervals, context);

        // Assert - stepwise motion should be smooth (low roughness)
        Assert.True(roughness < 1.0, $"Stepwise motion should be smooth but roughness = {roughness}");
    }

    /// <summary>
    /// Voice leading roughness should be higher for leaps.
    /// </summary>
    [Fact]
    public void CalculateVoiceLeadingRoughness_Leaps_IsHigherThanStepwise()
    {
        // Arrange
        var context = new BasicSpace(PitchClass.C, ModeType.Major);
        var stepwise = new[] { 1, 2, 1 };
        var leaps = new[] { 7, 8, 5 };

        // Act
        var stepwiseRoughness = MelodicAttraction.CalculateVoiceLeadingRoughness(stepwise, context);
        var leapRoughness = MelodicAttraction.CalculateVoiceLeadingRoughness(leaps, context);

        // Assert
        Assert.True(leapRoughness > stepwiseRoughness,
            $"Leaps ({leapRoughness}) should be rougher than steps ({stepwiseRoughness})");
    }
}
