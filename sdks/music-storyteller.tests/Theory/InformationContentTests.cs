using BeyondImmersion.Bannou.MusicStoryteller.Theory;
using Xunit;

namespace BeyondImmersion.Bannou.MusicStoryteller.Tests.Theory;

/// <summary>
/// Tests for Information Content calculations.
/// IC = -log₂(probability) measures surprise in bits.
/// </summary>
public class InformationContentTests
{
    /// <summary>
    /// Probability of 0.5 (50%) should give IC = 1 bit.
    /// </summary>
    [Fact]
    public void Calculate_HalfProbability_Returns1Bit()
    {
        // Act
        var ic = InformationContent.Calculate(0.5);

        // Assert
        Assert.Equal(1.0, ic, precision: 5);
    }

    /// <summary>
    /// Probability of 0.25 (25%) should give IC = 2 bits.
    /// </summary>
    [Fact]
    public void Calculate_QuarterProbability_Returns2Bits()
    {
        // Act
        var ic = InformationContent.Calculate(0.25);

        // Assert
        Assert.Equal(2.0, ic, precision: 5);
    }

    /// <summary>
    /// Probability of 0.125 (12.5%) should give IC = 3 bits.
    /// </summary>
    [Fact]
    public void Calculate_EighthProbability_Returns3Bits()
    {
        // Act
        var ic = InformationContent.Calculate(0.125);

        // Assert
        Assert.Equal(3.0, ic, precision: 5);
    }

    /// <summary>
    /// Certain event (probability = 1) has zero surprise.
    /// </summary>
    [Fact]
    public void Calculate_CertainEvent_ReturnsZero()
    {
        // Act
        var ic = InformationContent.Calculate(1.0);

        // Assert
        Assert.Equal(0.0, ic);
    }

    /// <summary>
    /// Impossible event (probability = 0) has maximum surprise.
    /// </summary>
    [Fact]
    public void Calculate_ImpossibleEvent_ReturnsMaxValue()
    {
        // Act
        var ic = InformationContent.Calculate(0.0);

        // Assert
        Assert.Equal(double.MaxValue, ic);
    }

    /// <summary>
    /// Lower probability = higher IC (more surprise).
    /// </summary>
    [Fact]
    public void Calculate_LowerProbabilityGivesHigherIC()
    {
        // Arrange
        var highProb = 0.8;
        var lowProb = 0.2;

        // Act
        var highProbIC = InformationContent.Calculate(highProb);
        var lowProbIC = InformationContent.Calculate(lowProb);

        // Assert
        Assert.True(lowProbIC > highProbIC,
            $"Lower probability should have higher IC: {lowProbIC} vs {highProbIC}");
    }

    /// <summary>
    /// Small intervals (unison, step) should have low IC (expected).
    /// </summary>
    [Fact]
    public void CalculateForInterval_SmallInterval_LowIC()
    {
        // Act
        var unisonIC = InformationContent.CalculateForInterval(0);
        var stepIC = InformationContent.CalculateForInterval(2);

        // Assert - common intervals should have IC < 3 (>12.5% probability)
        Assert.True(unisonIC < InformationContent.Reference.Moderate,
            $"Unison IC ({unisonIC}) should be < moderate ({InformationContent.Reference.Moderate})");
        Assert.True(stepIC < InformationContent.Reference.Moderate,
            $"Step IC ({stepIC}) should be < moderate");
    }

    /// <summary>
    /// Large intervals should have higher IC (less common).
    /// </summary>
    [Fact]
    public void CalculateForInterval_LargeInterval_HighIC()
    {
        // Act
        var octaveIC = InformationContent.CalculateForInterval(12);
        var seventhIC = InformationContent.CalculateForInterval(11);

        // Assert - rare intervals should have higher IC
        Assert.True(seventhIC > octaveIC,
            $"Major seventh IC ({seventhIC}) should be higher than octave ({octaveIC})");
    }

    /// <summary>
    /// V→I (dominant to tonic) should have low IC (very expected).
    /// </summary>
    [Fact]
    public void CalculateForHarmony_DominantToTonic_LowIC()
    {
        // Act
        var ic = InformationContent.CalculateForHarmony(fromDegree: 5, toDegree: 1);

        // Assert - this is the most expected resolution
        Assert.True(ic < InformationContent.Reference.Common,
            $"V→I IC ({ic}) should be < common threshold ({InformationContent.Reference.Common})");
    }

    /// <summary>
    /// Deceptive cadence (V→vi) should have higher IC than authentic (V→I).
    /// </summary>
    [Fact]
    public void CalculateForHarmony_DeceptiveCadence_HigherThanAuthentic()
    {
        // Act
        var authenticIC = InformationContent.CalculateForHarmony(5, 1);
        var deceptiveIC = InformationContent.CalculateForHarmony(5, 6);

        // Assert
        Assert.True(deceptiveIC > authenticIC,
            $"Deceptive ({deceptiveIC}) should surprise more than authentic ({authenticIC})");
    }

    /// <summary>
    /// NormalizeSurprise should return values in 0-1 range.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(3.0)]
    [InlineData(6.0)]
    [InlineData(10.0)]
    public void NormalizeSurprise_ReturnsValueIn0To1Range(double ic)
    {
        // Act
        var normalized = InformationContent.NormalizeSurprise(ic);

        // Assert
        Assert.True(normalized >= 0 && normalized <= 1,
            $"Normalized surprise ({normalized}) should be in [0,1]");
    }

    /// <summary>
    /// Higher IC should give higher normalized surprise.
    /// </summary>
    [Fact]
    public void NormalizeSurprise_HigherICGivesHigherSurprise()
    {
        // Arrange
        var lowIC = 1.0;
        var highIC = 5.0;

        // Act
        var lowSurprise = InformationContent.NormalizeSurprise(lowIC);
        var highSurprise = InformationContent.NormalizeSurprise(highIC);

        // Assert
        Assert.True(highSurprise > lowSurprise);
    }

    /// <summary>
    /// Reference values should follow expected ordering.
    /// </summary>
    [Fact]
    public void Reference_ValuesAreOrdered()
    {
        Assert.True(InformationContent.Reference.VeryCommon < InformationContent.Reference.Common);
        Assert.True(InformationContent.Reference.Common < InformationContent.Reference.Moderate);
        Assert.True(InformationContent.Reference.Moderate < InformationContent.Reference.Uncommon);
        Assert.True(InformationContent.Reference.Uncommon < InformationContent.Reference.Rare);
        Assert.True(InformationContent.Reference.Rare < InformationContent.Reference.VeryRare);
    }
}
