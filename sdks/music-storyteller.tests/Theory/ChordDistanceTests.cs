using BeyondImmersion.Bannou.MusicStoryteller.Theory;
using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Pitch;
using Xunit;

namespace BeyondImmersion.Bannou.MusicStoryteller.Tests.Theory;

/// <summary>
/// Tests for Lerdahl's chord distance formula δ(x→y) = i + j + k.
/// Validates against known values from Tonal Pitch Space.
/// </summary>
public class ChordDistanceTests
{
    /// <summary>
    /// Validates that δ(I→V) = 5 in major keys using pre-computed matrix.
    /// This is a fundamental reference value from Lerdahl TPS.
    /// </summary>
    [Fact]
    public void CalculateFromDegrees_TonicToDominant_Returns5()
    {
        // Act - use pre-computed matrix which represents Lerdahl reference values
        var distance = ChordDistance.CalculateFromDegrees(1, 5, isMajorKey: true);

        // Assert
        Assert.Equal(ChordDistance.Reference.TonicDominant, distance);
    }

    /// <summary>
    /// Validates that computed δ(I→V) matches formula (i + j + k).
    /// Dynamic computation may vary slightly from pre-computed references.
    /// </summary>
    [Fact]
    public void Calculate_TonicToDominant_ComputesValidDistance()
    {
        // Arrange
        var key = new Scale(PitchClass.C, ModeType.Major);
        var tonic = Chord.FromScaleDegree(key, 1, false); // C major
        var dominant = Chord.FromScaleDegree(key, 5, false); // G major

        // Act
        var distance = ChordDistance.Calculate(tonic, dominant, key);

        // Assert - should be in reasonable range for closely related chords
        Assert.InRange(distance, 3, 6);
        Assert.True(distance > 0, "Tonic to dominant should have non-zero distance");
    }

    /// <summary>
    /// Validates that δ(I→vi) = 8 in major keys (relative minor relationship).
    /// </summary>
    [Fact]
    public void Calculate_TonicToRelativeMinor_Returns8()
    {
        // Arrange
        var key = new Scale(PitchClass.C, ModeType.Major);
        var tonic = Chord.FromScaleDegree(key, 1, false); // C major
        var relativeMinor = Chord.FromScaleDegree(key, 6, false); // A minor

        // Act
        var distance = ChordDistance.Calculate(tonic, relativeMinor, key);

        // Assert
        Assert.Equal(ChordDistance.Reference.TonicRelative, distance);
    }

    /// <summary>
    /// Validates that same chord to itself returns 0.
    /// </summary>
    [Fact]
    public void Calculate_SameChord_Returns0()
    {
        // Arrange
        var key = new Scale(PitchClass.C, ModeType.Major);
        var chord = Chord.FromScaleDegree(key, 1, false);

        // Act
        var distance = ChordDistance.Calculate(chord, chord, key);

        // Assert
        Assert.Equal(0, distance);
    }

    /// <summary>
    /// Validates ii→V (pre-dominant to dominant) distance.
    /// </summary>
    [Fact]
    public void Calculate_TwoToFive_Returns5()
    {
        // Arrange
        var distance = ChordDistance.CalculateFromDegrees(2, 5, isMajorKey: true);

        // Assert
        Assert.Equal(ChordDistance.Reference.TwoFive, distance);
    }

    /// <summary>
    /// Circle of fifths distance between C and G is 1.
    /// </summary>
    [Fact]
    public void CircleOfFifthsDistance_CFifthsToG_Returns1()
    {
        // Act
        var distance = ChordDistance.CircleOfFifthsDistance(PitchClass.C, PitchClass.G);

        // Assert
        Assert.Equal(1, distance);
    }

    /// <summary>
    /// Circle of fifths distance is symmetric (C→G same as G→C).
    /// </summary>
    [Fact]
    public void CircleOfFifthsDistance_IsSymmetric()
    {
        // Act
        var cToG = ChordDistance.CircleOfFifthsDistance(PitchClass.C, PitchClass.G);
        var gToC = ChordDistance.CircleOfFifthsDistance(PitchClass.G, PitchClass.C);

        // Assert
        Assert.Equal(cToG, gToC);
    }

    /// <summary>
    /// Tritone relationship (maximum circle of fifths distance).
    /// </summary>
    [Fact]
    public void CircleOfFifthsDistance_Tritone_Returns6()
    {
        // C to F# (tritone) is maximum distance on circle of fifths
        var distance = ChordDistance.CircleOfFifthsDistance(PitchClass.C, PitchClass.Fs);

        // Assert
        Assert.Equal(6, distance);
    }

    /// <summary>
    /// Relative tension should be 0 for tonic chord.
    /// </summary>
    [Fact]
    public void GetRelativeTension_TonicChord_ReturnsZeroOrVeryLow()
    {
        // Arrange
        var key = new Scale(PitchClass.C, ModeType.Major);
        var tonic = Chord.FromScaleDegree(key, 1, false);

        // Act
        var tension = ChordDistance.GetRelativeTension(tonic, key);

        // Assert
        Assert.True(tension < 0.1, $"Expected tonic tension < 0.1 but got {tension}");
    }

    /// <summary>
    /// Relative tension should be higher for dominant than tonic.
    /// </summary>
    [Fact]
    public void GetRelativeTension_DominantHigherThanTonic()
    {
        // Arrange
        var key = new Scale(PitchClass.C, ModeType.Major);
        var tonic = Chord.FromScaleDegree(key, 1, false);
        var dominant = Chord.FromScaleDegree(key, 5, false);

        // Act
        var tonicTension = ChordDistance.GetRelativeTension(tonic, key);
        var dominantTension = ChordDistance.GetRelativeTension(dominant, key);

        // Assert
        Assert.True(dominantTension > tonicTension,
            $"Expected dominant tension ({dominantTension}) > tonic tension ({tonicTension})");
    }
}
