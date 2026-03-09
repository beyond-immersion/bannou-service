using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.Bannou.BehaviorCompiler.Parser;

namespace BeyondImmersion.Bannou.Behavior.Compiler;

/// <summary>
/// Merges a LoadedDocument tree into a single flat AbmlDocument.
/// All imported flows are renamed with their namespace prefix.
/// This enables bytecode compilation to produce a single self-contained model.
/// </summary>
public interface IDocumentMerger
{
    /// <summary>
    /// Merges a LoadedDocument tree into a single flat AbmlDocument.
    /// </summary>
    /// <param name="loaded">The loaded document with resolved imports.</param>
    /// <returns>A merged document with all flows flattened and renamed.</returns>
    AbmlDocument Merge(LoadedDocument loaded);
}
