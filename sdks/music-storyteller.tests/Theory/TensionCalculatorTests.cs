using BeyondImmersion.Bannou.MusicStoryteller.Theory;
using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Pitch;
using Xunit;

// ReSharper disable once CheckNamespace - Test files organized by subject

namespace BeyondImmersion.Bannou.MusicStoryteller.Tests.Theory;

/// <summary>
/// Tests for TIS Tension Calculator.
/// T = 0.402×Diss + 0.246×Hier + 0.202×TonalDist + 0.193×VL
/// </summary>
public class TensionCalculatorTests
{
    /// <summary>
    /// TIS weights should sum to approximately 1.0.
    /// </summary>
    [Fact]
    public void Weights_SumToApproximatelyOne()
    {
        var sum = TensionCalculator.Weights.Dissonance +
                  TensionCalculator.Weights.Hierarchical +
                  TensionCalculator.Weights.TonalDistance +
                  TensionCalculator.Weights.VoiceLeading;

        // Weights from Navarro 2020 sum to ~1.043
        Assert.True(sum > 0.9 && sum < 1.1, $"Weights sum ({sum}) should be near 1.0");
    }

    /// <summary>
    /// All zeros should produce zero tension.
    /// </summary>
    [Fact]
    public void Calculate_AllZeros_ReturnsZero()
    {
        var tension = TensionCalculator.Calculate(0, 0, 0, 0);
        Assert.Equal(0.0, tension);
    }

    /// <summary>
    /// All max values should produce tension near 1.0.
    /// </summary>
    [Fact]
    public void Calculate_AllMax_ReturnsNearOne()
    {
        var tension = TensionCalculator.Calculate(1.0, 1.0, 1.0, 1.0);
        Assert.True(tension > 0.9 && tension < 1.2, $"Max tension ({tension}) should be near 1.0");
    }

    /// <summary>
    /// Dissonance is the strongest contributor (weight 0.402).
    /// </summary>
    [Fact]
    public void Calculate_DissonanceHasStrongestWeight()
    {
        var dissonanceOnly = TensionCalculator.Calculate(1.0, 0, 0, 0);
        var hierarchicalOnly = TensionCalculator.Calculate(0, 1.0, 0, 0);
        var tonalOnly = TensionCalculator.Calculate(0, 0, 1.0, 0);
        var vlOnly = TensionCalculator.Calculate(0, 0, 0, 1.0);

        Assert.True(dissonanceOnly > hierarchicalOnly);
        Assert.True(dissonanceOnly > tonalOnly);
        Assert.True(dissonanceOnly > vlOnly);
    }

    /// <summary>
    /// Major triad has low dissonance.
    /// </summary>
    [Fact]
    public void CalculateDissonance_MajorTriad_IsLow()
    {
        var chord = new Chord(PitchClass.C, ChordQuality.Major);
        var dissonance = TensionCalculator.CalculateDissonance(chord);
        Assert.Equal(0.0, dissonance);
    }

    /// <summary>
    /// Dominant seventh has moderate dissonance.
    /// </summary>
    [Fact]
    public void CalculateDissonance_DominantSeventh_IsModerate()
    {
        var chord = new Chord(PitchClass.G, ChordQuality.Dominant7);
        var dissonance = TensionCalculator.CalculateDissonance(chord);
        Assert.True(dissonance > 0.2 && dissonance < 0.5,
            $"Dom7 dissonance ({dissonance}) should be moderate");
    }

    /// <summary>
    /// Diminished seventh has high dissonance.
    /// </summary>
    [Fact]
    public void CalculateDissonance_DiminishedSeventh_IsHigh()
    {
        var chord = new Chord(PitchClass.B, ChordQuality.Diminished7);
        var dissonance = TensionCalculator.CalculateDissonance(chord);
        Assert.True(dissonance > 0.5, $"Dim7 dissonance ({dissonance}) should be high");
    }

    /// <summary>
    /// Tonic chord should have low tension.
    /// </summary>
    [Fact]
    public void CalculateForChord_TonicChord_LowTension()
    {
        var key = new Scale(PitchClass.C, ModeType.Major);
        var tonic = Chord.FromScaleDegree(key, 1, false);

        var tension = TensionCalculator.CalculateForChord(tonic, key);

        Assert.True(tension < TensionCalculator.Reference.Subdominant,
            $"Tonic tension ({tension}) should be < subdominant reference ({TensionCalculator.Reference.Subdominant})");
    }

    /// <summary>
    /// Dominant seventh should have higher tension than tonic.
    /// </summary>
    [Fact]
    public void CalculateForChord_DominantSeventhHigherThanTonic()
    {
        var key = new Scale(PitchClass.C, ModeType.Major);
        var tonic = Chord.FromScaleDegree(key, 1, false);
        var dominantSeventh = new Chord(PitchClass.G, ChordQuality.Dominant7);

        var tonicTension = TensionCalculator.CalculateForChord(tonic, key);
        var domTension = TensionCalculator.CalculateForChord(dominantSeventh, key);

        Assert.True(domTension > tonicTension,
            $"Dom7 tension ({domTension}) should exceed tonic ({tonicTension})");
    }

    /// <summary>
    /// Tension curve should have correct length.
    /// </summary>
    [Fact]
    public void CalculateTensionCurve_ReturnsCorrectLength()
    {
        var key = new Scale(PitchClass.C, ModeType.Major);
        var chords = new List<Chord>
        {
            Chord.FromScaleDegree(key, 1, false),
            Chord.FromScaleDegree(key, 4, false),
            Chord.FromScaleDegree(key, 5, false),
            Chord.FromScaleDegree(key, 1, false)
        };

        var tensions = TensionCalculator.CalculateTensionCurve(chords, key);

        Assert.Equal(4, tensions.Length);
    }

    /// <summary>
    /// I-IV-V-I should show tension increase then decrease.
    /// </summary>
    [Fact]
    public void CalculateTensionCurve_StandardProgression_ShowsArc()
    {
        var key = new Scale(PitchClass.C, ModeType.Major);
        var chords = new List<Chord>
        {
            Chord.FromScaleDegree(key, 1, false), // I
            Chord.FromScaleDegree(key, 4, false), // IV
            Chord.FromScaleDegree(key, 5, false), // V
            Chord.FromScaleDegree(key, 1, false)  // I
        };

        var tensions = TensionCalculator.CalculateTensionCurve(chords, key);

        // V should have higher tension than I
        Assert.True(tensions[2] > tensions[0], "V should have more tension than I");
        Assert.True(tensions[2] > tensions[3], "V should have more tension than final I");
    }

    /// <summary>
    /// Resolution strength should be positive for tension decrease.
    /// </summary>
    [Fact]
    public void CalculateResolutionStrength_TensionDecrease_Positive()
    {
        var strength = TensionCalculator.CalculateResolutionStrength(0.8, 0.2);
        Assert.Equal(0.6, strength, precision: 5);
    }

    /// <summary>
    /// Resolution strength should be clamped to 0-1.
    /// </summary>
    [Fact]
    public void CalculateResolutionStrength_ClampedTo0To1()
    {
        var noChange = TensionCalculator.CalculateResolutionStrength(0.5, 0.5);
        var increase = TensionCalculator.CalculateResolutionStrength(0.3, 0.8);

        Assert.Equal(0.0, noChange);
        Assert.Equal(0.0, increase); // Clamped - no resolution if tension increases
    }

    /// <summary>
    /// FindTensionPeaks should identify local maxima.
    /// </summary>
    [Fact]
    public void FindTensionPeaks_IdentifiesLocalMaxima()
    {
        var tensions = new[] { 0.2, 0.4, 0.7, 0.5, 0.3, 0.6, 0.4 };

        var peaks = TensionCalculator.FindTensionPeaks(tensions, threshold: 0.5);

        Assert.Contains(2, peaks); // 0.7 is a peak
        Assert.Contains(5, peaks); // 0.6 is a peak
    }
}
