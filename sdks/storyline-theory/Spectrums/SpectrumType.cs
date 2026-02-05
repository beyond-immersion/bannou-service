// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Spectrums;

/// <summary>
/// The 10 Life Value spectrums from Story Grid's Four Core Framework.
/// Each spectrum represents a fundamental human need and the poles of value
/// that stories explore.
/// </summary>
public enum SpectrumType
{
    /// <summary>
    /// Life vs Death - Survival domain (Maslow Level 1).
    /// Primary for: Action, Thriller, Horror.
    /// </summary>
    LifeDeath = 1,

    /// <summary>
    /// Honor vs Dishonor - Safety domain (Maslow Level 2).
    /// Primary for: War.
    /// </summary>
    HonorDishonor = 2,

    /// <summary>
    /// Justice vs Injustice - Safety domain (Maslow Level 2).
    /// Primary for: Crime.
    /// </summary>
    JusticeInjustice = 3,

    /// <summary>
    /// Freedom vs Subjugation - Safety domain (Maslow Level 2).
    /// Primary for: Western.
    /// </summary>
    FreedomSubjugation = 4,

    /// <summary>
    /// Love vs Hate - Connection domain (Maslow Level 3).
    /// Primary for: Love/Romance.
    /// </summary>
    LoveHate = 5,

    /// <summary>
    /// Respect vs Shame - Esteem domain (Maslow Level 4).
    /// Primary for: Performance.
    /// </summary>
    RespectShame = 6,

    /// <summary>
    /// Power vs Impotence - Esteem domain (Maslow Level 4).
    /// Primary for: Society.
    /// </summary>
    PowerImpotence = 7,

    /// <summary>
    /// Success vs Failure - Esteem domain (Maslow Level 4).
    /// Primary for: Status.
    /// </summary>
    SuccessFailure = 8,

    /// <summary>
    /// Altruism vs Selfishness - Self-transcendence domain (Maslow Level 5).
    /// Primary for: Morality.
    /// </summary>
    AltruismSelfishness = 9,

    /// <summary>
    /// Wisdom vs Ignorance - Self-actualization domain (Maslow Level 5).
    /// Primary for: Worldview.
    /// </summary>
    WisdomIgnorance = 10
}
