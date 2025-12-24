// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

// CS1998: Async method lacks 'await' operators
// Justification: Many methods are named *Async to match interface contracts or indicate
// they return Task, but perform synchronous operations. The async keyword is used
// for consistency with the naming convention and to allow future async implementation.
[assembly: SuppressMessage(
    "Style",
    "CS1998:Async method lacks 'await' operators and will run synchronously",
    Justification = "Method signature requires async for interface compliance or future async implementation")]
