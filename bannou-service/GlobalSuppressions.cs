// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Usage", "CA2254:Template should be a static expression",
    Justification = "Bullshit- log aggregators/visualizers don't HAVE to use event grouping based on the message content. " +
        "Also, reflected type issues really need the specific assembly/type name prominently in the message (+other use cases).")]
