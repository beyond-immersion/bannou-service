// This file is used to suppress code analysis warnings that are applied project-wide.
// Note: Compiler warnings (CS8620, CS8602, etc.) are suppressed via Directory.Build.props
// using the <NoWarn> property, which is the correct MSBuild approach for project-wide suppression.

using System.Diagnostics.CodeAnalysis;

// CA1822: Member does not access instance data and can be marked as static
// Test methods often don't access instance data but should remain instance methods for test framework compatibility
[assembly: SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Test methods should remain instance methods for test framework compatibility")]
