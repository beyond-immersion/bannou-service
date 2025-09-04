// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Usage", "CA2254:Template should be a static expression",
    Justification = "This rule is useful for production environments where log aggregation can be based on message content (for JSON, especially). " +
                    "Not so much for builds, unit tests, and integration tests.")]
