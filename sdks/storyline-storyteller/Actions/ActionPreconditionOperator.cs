// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Actions;

/// <summary>
/// Comparison operators for action preconditions.
/// </summary>
public enum ActionPreconditionOperator
{
    /// <summary>
    /// Value must equal the expected value.
    /// </summary>
    Equals,

    /// <summary>
    /// Value must not equal the expected value.
    /// </summary>
    NotEquals,

    /// <summary>
    /// Value must be greater than the expected value.
    /// </summary>
    GreaterThan,

    /// <summary>
    /// Value must be less than the expected value.
    /// </summary>
    LessThan,

    /// <summary>
    /// Value must be greater than or equal to the expected value.
    /// </summary>
    GreaterOrEqual,

    /// <summary>
    /// Value must be less than or equal to the expected value.
    /// </summary>
    LessOrEqual
}
