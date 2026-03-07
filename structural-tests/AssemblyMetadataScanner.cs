using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Scans assembly PE metadata to detect type and method references without loading
/// the assembly into the AppDomain. Uses System.Reflection.Metadata for zero-load
/// IL inspection — answers "does assembly X call type Y's methods?" which standard
/// System.Reflection cannot answer.
/// </summary>
internal static class AssemblyMetadataScanner
{
    /// <summary>
    /// Checks whether a compiled assembly contains member references (method calls)
    /// to any of the specified methods on the specified type.
    /// </summary>
    /// <param name="assemblyPath">Path to the .dll file to scan.</param>
    /// <param name="typeName">Simple name of the type to look for (e.g., "EnumMapping").</param>
    /// <param name="methodNames">Method names to match on that type.</param>
    /// <returns>True if the assembly references at least one of the specified methods.</returns>
    internal static bool ReferencesMethodOnType(string assemblyPath, string typeName, params string[] methodNames)
    {
        if (!File.Exists(assemblyPath))
            return false;

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata)
            return false;

        var reader = peReader.GetMetadataReader();
        var methodNameSet = new HashSet<string>(methodNames);

        foreach (var memberRefHandle in reader.MemberReferences)
        {
            var memberRef = reader.GetMemberReference(memberRefHandle);
            if (memberRef.Parent.Kind != HandleKind.TypeReference)
                continue;

            var typeRef = reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
            if (reader.GetString(typeRef.Name) != typeName)
                continue;

            if (methodNameSet.Contains(reader.GetString(memberRef.Name)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the subset of <paramref name="methodNames"/> that ARE referenced by the assembly.
    /// Useful for determining which specific methods are called vs which are missing.
    /// </summary>
    /// <param name="assemblyPath">Path to the .dll file to scan.</param>
    /// <param name="typeName">Simple name of the type to look for (e.g., "QuestEventPublisher").</param>
    /// <param name="methodNames">Method names to check for references.</param>
    /// <returns>Set of method names that are referenced by the assembly.</returns>
    internal static HashSet<string> GetReferencedMethods(string assemblyPath, string typeName, IEnumerable<string> methodNames)
    {
        var result = new HashSet<string>();

        if (!File.Exists(assemblyPath))
            return result;

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata)
            return result;

        var reader = peReader.GetMetadataReader();
        var methodNameSet = new HashSet<string>(methodNames);

        foreach (var memberRefHandle in reader.MemberReferences)
        {
            var memberRef = reader.GetMemberReference(memberRefHandle);
            if (memberRef.Parent.Kind != HandleKind.TypeReference)
                continue;

            var typeRef = reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
            if (reader.GetString(typeRef.Name) != typeName)
                continue;

            var memberName = reader.GetString(memberRef.Name);
            if (methodNameSet.Contains(memberName))
                result.Add(memberName);
        }

        return result;
    }
}
