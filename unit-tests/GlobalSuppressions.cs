// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Usage", "CA1822:Members should be marked static.",
    Justification = "Unit tests involving reflection/attributes which don't directly access properties and methods.")]

[assembly: SuppressMessage("Usage", "IDE0051:No value is ever assigned to member.",
    Justification = "Unit tests involving reflection/attributes which don't directly access properties and methods.")]

[assembly: SuppressMessage("Usage", "IDE0052:Method is never invoked.",
    Justification = "Unit tests involving reflection/attributes which don't directly access properties and methods.")]
