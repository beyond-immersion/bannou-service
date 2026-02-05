// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Spectrums;

/// <summary>
/// Runtime state container for tracking spectrum values during story generation.
/// Only the active spectrums (primary + any secondary) are tracked; others remain null.
/// </summary>
public sealed class NarrativeState
{
    /// <summary>
    /// Life vs Death spectrum value. Null if not actively tracked.
    /// </summary>
    public double? LifeDeath { get; set; }

    /// <summary>
    /// Honor vs Dishonor spectrum value. Null if not actively tracked.
    /// </summary>
    public double? HonorDishonor { get; set; }

    /// <summary>
    /// Justice vs Injustice spectrum value. Null if not actively tracked.
    /// </summary>
    public double? JusticeInjustice { get; set; }

    /// <summary>
    /// Freedom vs Subjugation spectrum value. Null if not actively tracked.
    /// </summary>
    public double? FreedomSubjugation { get; set; }

    /// <summary>
    /// Love vs Hate spectrum value. Null if not actively tracked.
    /// </summary>
    public double? LoveHate { get; set; }

    /// <summary>
    /// Respect vs Shame spectrum value. Null if not actively tracked.
    /// </summary>
    public double? RespectShame { get; set; }

    /// <summary>
    /// Power vs Impotence spectrum value. Null if not actively tracked.
    /// </summary>
    public double? PowerImpotence { get; set; }

    /// <summary>
    /// Success vs Failure spectrum value. Null if not actively tracked.
    /// </summary>
    public double? SuccessFailure { get; set; }

    /// <summary>
    /// Altruism vs Selfishness spectrum value. Null if not actively tracked.
    /// </summary>
    public double? AltruismSelfishness { get; set; }

    /// <summary>
    /// Wisdom vs Ignorance spectrum value. Null if not actively tracked.
    /// </summary>
    public double? WisdomIgnorance { get; set; }

    /// <summary>
    /// Which spectrum is primary for this story (determined by genre).
    /// </summary>
    public required SpectrumType PrimarySpectrum { get; init; }

    /// <summary>
    /// Gets the value of the primary spectrum.
    /// </summary>
    /// <returns>The primary spectrum value, or 0.5 (neutral) if not set.</returns>
    public double GetPrimaryValue()
    {
        return this[PrimarySpectrum] ?? 0.5;
    }

    /// <summary>
    /// Gets or sets a spectrum value by type.
    /// </summary>
    /// <param name="spectrum">The spectrum type.</param>
    /// <returns>The spectrum value, or null if not tracked.</returns>
    public double? this[SpectrumType spectrum]
    {
        get => spectrum switch
        {
            SpectrumType.LifeDeath => LifeDeath,
            SpectrumType.HonorDishonor => HonorDishonor,
            SpectrumType.JusticeInjustice => JusticeInjustice,
            SpectrumType.FreedomSubjugation => FreedomSubjugation,
            SpectrumType.LoveHate => LoveHate,
            SpectrumType.RespectShame => RespectShame,
            SpectrumType.PowerImpotence => PowerImpotence,
            SpectrumType.SuccessFailure => SuccessFailure,
            SpectrumType.AltruismSelfishness => AltruismSelfishness,
            SpectrumType.WisdomIgnorance => WisdomIgnorance,
            _ => throw new ArgumentOutOfRangeException(nameof(spectrum))
        };
        set
        {
            switch (spectrum)
            {
                case SpectrumType.LifeDeath: LifeDeath = value; break;
                case SpectrumType.HonorDishonor: HonorDishonor = value; break;
                case SpectrumType.JusticeInjustice: JusticeInjustice = value; break;
                case SpectrumType.FreedomSubjugation: FreedomSubjugation = value; break;
                case SpectrumType.LoveHate: LoveHate = value; break;
                case SpectrumType.RespectShame: RespectShame = value; break;
                case SpectrumType.PowerImpotence: PowerImpotence = value; break;
                case SpectrumType.SuccessFailure: SuccessFailure = value; break;
                case SpectrumType.AltruismSelfishness: AltruismSelfishness = value; break;
                case SpectrumType.WisdomIgnorance: WisdomIgnorance = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(spectrum));
            }
        }
    }

    /// <summary>
    /// Calculates the Euclidean distance to a target state.
    /// Only compares spectrums that are active in both states.
    /// Used as a heuristic for GOAP planning.
    /// </summary>
    /// <param name="target">The target state to compare against.</param>
    /// <returns>The average distance across active spectrums, or 0 if no overlap.</returns>
    public double DistanceTo(NarrativeState target)
    {
        double sumSquared = 0;
        int count = 0;

        CompareSpectrum(LifeDeath, target.LifeDeath, ref sumSquared, ref count);
        CompareSpectrum(HonorDishonor, target.HonorDishonor, ref sumSquared, ref count);
        CompareSpectrum(JusticeInjustice, target.JusticeInjustice, ref sumSquared, ref count);
        CompareSpectrum(FreedomSubjugation, target.FreedomSubjugation, ref sumSquared, ref count);
        CompareSpectrum(LoveHate, target.LoveHate, ref sumSquared, ref count);
        CompareSpectrum(RespectShame, target.RespectShame, ref sumSquared, ref count);
        CompareSpectrum(PowerImpotence, target.PowerImpotence, ref sumSquared, ref count);
        CompareSpectrum(SuccessFailure, target.SuccessFailure, ref sumSquared, ref count);
        CompareSpectrum(AltruismSelfishness, target.AltruismSelfishness, ref sumSquared, ref count);
        CompareSpectrum(WisdomIgnorance, target.WisdomIgnorance, ref sumSquared, ref count);

        return count > 0 ? Math.Sqrt(sumSquared / count) : 0;
    }

    private static void CompareSpectrum(double? a, double? b, ref double sumSquared, ref int count)
    {
        if (a.HasValue && b.HasValue)
        {
            var diff = a.Value - b.Value;
            sumSquared += diff * diff;
            count++;
        }
    }

    /// <summary>
    /// Creates a deep copy of this state.
    /// </summary>
    /// <returns>A new NarrativeState with the same values.</returns>
    public NarrativeState Clone()
    {
        return new NarrativeState
        {
            PrimarySpectrum = PrimarySpectrum,
            LifeDeath = LifeDeath,
            HonorDishonor = HonorDishonor,
            JusticeInjustice = JusticeInjustice,
            FreedomSubjugation = FreedomSubjugation,
            LoveHate = LoveHate,
            RespectShame = RespectShame,
            PowerImpotence = PowerImpotence,
            SuccessFailure = SuccessFailure,
            AltruismSelfishness = AltruismSelfishness,
            WisdomIgnorance = WisdomIgnorance
        };
    }

    /// <summary>
    /// Creates a new state with a spectrum value changed.
    /// </summary>
    /// <param name="spectrum">The spectrum to change.</param>
    /// <param name="value">The new value.</param>
    /// <returns>A new NarrativeState with the updated value.</returns>
    public NarrativeState WithSpectrum(SpectrumType spectrum, double? value)
    {
        var clone = Clone();
        clone[spectrum] = value;
        return clone;
    }
}
