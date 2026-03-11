using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace BeyondImmersion.BannouService.StructuralTests;

// ⛔ FROZEN FILE — DO NOT MODIFY WITHOUT EXPLICIT USER PERMISSION ⛔
// Structural test infrastructure. Changes affect IL-level validation across all services.

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
    /// Returns the subset of <paramref name="methodNames"/> that ARE referenced by the assembly,
    /// checking both cross-assembly references (MemberRef table) and same-assembly calls
    /// (IL body scanning for call instructions to MethodDef tokens).
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

        // Phase 1: Check cross-assembly references (MemberRef table)
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

        // Phase 2: Check same-assembly calls by scanning IL bodies for MethodDef tokens.
        // When the target type (e.g., *EventPublisher) is compiled into the same assembly,
        // calls to its methods use MethodDef tokens (not MemberRef), so Phase 1 misses them.
        var remaining = new HashSet<string>(methodNameSet.Except(result));
        if (remaining.Count > 0)
        {
            var sameAssemblyResults = GetCalledMethodsInSameAssembly(peReader, reader, typeName, remaining);
            foreach (var name in sameAssemblyResults)
                result.Add(name);
        }

        return result;
    }

    /// <summary>
    /// Scans IL method bodies for <c>call</c> (0x28) instructions that reference MethodDef
    /// tokens belonging to the specified type within the same assembly. This catches
    /// intra-assembly calls that don't appear in the MemberRef table.
    /// </summary>
    private static HashSet<string> GetCalledMethodsInSameAssembly(
        PEReader peReader, MetadataReader reader, string typeName, HashSet<string> methodNames)
    {
        var result = new HashSet<string>();

        // Build a map of raw metadata tokens -> method names for the target type's methods
        var targetTokens = new Dictionary<int, string>();

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            if (reader.GetString(typeDef.Name) != typeName)
                continue;

            foreach (var methodDefHandle in typeDef.GetMethods())
            {
                var methodDef = reader.GetMethodDefinition(methodDefHandle);
                var name = reader.GetString(methodDef.Name);
                if (methodNames.Contains(name))
                {
                    var token = MetadataTokens.GetToken(reader, methodDefHandle);
                    targetTokens[token] = name;
                }
            }

            break; // Found the type
        }

        if (targetTokens.Count == 0)
            return result;

        // Scan all method bodies for call/callvirt instructions referencing our target tokens
        foreach (var methodDefHandle in reader.MethodDefinitions)
        {
            var methodDef = reader.GetMethodDefinition(methodDefHandle);
            if (methodDef.RelativeVirtualAddress == 0)
                continue;

            MethodBodyBlock body;
            try
            {
                body = peReader.GetMethodBody(methodDef.RelativeVirtualAddress);
            }
            catch
            {
                continue;
            }

            var il = body.GetILBytes();
            if (il == null || il.Length < 5)
                continue;

            // Scan for call (0x28) and callvirt (0x6F) opcodes followed by 4-byte token.
            // These are InlineMethod opcodes: 1 byte opcode + 4 byte metadata token.
            for (var i = 0; i < il.Length - 4; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F)
                    continue;

                var token = il[i + 1]
                    | (il[i + 2] << 8)
                    | (il[i + 3] << 16)
                    | (il[i + 4] << 24);

                if (targetTokens.TryGetValue(token, out var methodName))
                {
                    result.Add(methodName);
                    if (result.Count == methodNames.Count)
                        return result; // Found all, early exit
                }
            }
        }

        return result;
    }
}
