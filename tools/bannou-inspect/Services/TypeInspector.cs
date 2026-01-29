namespace BeyondImmersion.BannouService.Tools.Inspect.Services;

using System.Reflection;
using SysMethodInfo = System.Reflection.MethodInfo;
using SysPropertyInfo = System.Reflection.PropertyInfo;
using SysEventInfo = System.Reflection.EventInfo;
using SysParameterInfo = System.Reflection.ParameterInfo;
using ModelMethodInfo = BeyondImmersion.BannouService.Tools.Inspect.Models.MethodInfo;
using ModelPropertyInfo = BeyondImmersion.BannouService.Tools.Inspect.Models.PropertyInfo;
using ModelEventInfo = BeyondImmersion.BannouService.Tools.Inspect.Models.EventInfo;
using ModelParameterInfo = BeyondImmersion.BannouService.Tools.Inspect.Models.ParameterInfo;

/// <summary>
/// Inspects types and methods using MetadataLoadContext for safe reflection.
/// </summary>
public sealed class TypeInspector : IDisposable
{
    private readonly MetadataLoadContext _context;
    private readonly Assembly _assembly;
    private readonly DocXmlReader? _docReader;

    /// <summary>
    /// Initializes a new instance of the TypeInspector class.
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly to inspect.</param>
    /// <param name="xmlDocPath">Optional path to the XML documentation file.</param>
    public TypeInspector(string assemblyPath, string? xmlDocPath = null)
    {
        var resolver = new AssemblyResolver(assemblyPath);
        _context = new MetadataLoadContext(resolver);
        _assembly = _context.LoadFromAssemblyPath(assemblyPath);

        if (xmlDocPath is not null && File.Exists(xmlDocPath))
        {
            _docReader = new DocXmlReader(xmlDocPath);
        }
    }

    /// <summary>
    /// Gets all public types in the assembly.
    /// </summary>
    public IReadOnlyList<string> GetPublicTypes()
    {
        return _assembly.GetExportedTypes()
            .Select(t => t.FullName ?? t.Name)
            .OrderBy(n => n)
            .ToList();
    }

    /// <summary>
    /// Searches for types matching a pattern.
    /// </summary>
    /// <param name="pattern">Search pattern (supports * wildcard).</param>
    public IReadOnlyList<string> SearchTypes(string pattern)
    {
        var regex = new Regex(
            "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$",
            RegexOptions.IgnoreCase
        );

        return _assembly.GetExportedTypes()
            .Select(t => t.FullName ?? t.Name)
            .Where(n => regex.IsMatch(n) || regex.IsMatch(n.Split('.').Last()))
            .OrderBy(n => n)
            .ToList();
    }

    /// <summary>
    /// Inspects a type by name.
    /// </summary>
    /// <param name="typeName">The full or simple type name.</param>
    /// <returns>Type information, or null if not found.</returns>
    public Models.TypeInfo? InspectType(string typeName)
    {
        var type = FindType(typeName);
        if (type is null)
        {
            return null;
        }

        var typeComments = GetTypeComments(type);

        return new Models.TypeInfo
        {
            FullName = type.FullName ?? type.Name,
            Kind = GetTypeKind(type),
            BaseType = type.BaseType?.FullName,
            Interfaces = type.GetInterfaces().Select(i => i.FullName ?? i.Name).ToList(),
            Summary = typeComments?.Summary,
            Remarks = typeComments?.Remarks,
            Methods = GetPublicMethods(type),
            Properties = GetPublicProperties(type),
            Events = GetPublicEvents(type),
            GenericParameters = type.GetGenericArguments().Select(t => t.Name).ToList(),
            AssemblyName = _assembly.GetName().Name ?? "Unknown"
        };
    }

    /// <summary>
    /// Inspects a specific method.
    /// </summary>
    /// <param name="typeName">The type containing the method.</param>
    /// <param name="methodName">The method name.</param>
    /// <returns>Method information, or null if not found.</returns>
    public IReadOnlyList<ModelMethodInfo> InspectMethod(string typeName, string methodName)
    {
        var type = FindType(typeName);
        if (type is null)
        {
            return [];
        }

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
            .Select(m => CreateMethodInfo(m))
            .ToList();

        return methods;
    }

    private Type? FindType(string typeName)
    {
        // Try exact match first
        var type = _assembly.GetType(typeName);
        if (type is not null)
        {
            return type;
        }

        // Try to find by simple name
        return _assembly.GetExportedTypes()
            .FirstOrDefault(t =>
                t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                t.FullName?.Equals(typeName, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string GetTypeKind(Type type)
    {
        if (type.IsInterface) return "interface";
        if (type.IsEnum) return "enum";
        if (type.IsValueType) return "struct";
        if (type.BaseType?.FullName == "System.MulticastDelegate") return "delegate";
        return type.IsAbstract && type.IsSealed ? "static class" : "class";
    }

    private IReadOnlyList<ModelMethodInfo> GetPublicMethods(Type type)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName) // Exclude property getters/setters
            .Select(m => CreateMethodInfo(m))
            .ToList();
    }

    private ModelMethodInfo CreateMethodInfo(SysMethodInfo method)
    {
        var methodComments = GetMethodComments(method);

        return new ModelMethodInfo
        {
            Name = method.Name,
            ReturnType = FormatTypeName(method.ReturnType),
            Parameters = method.GetParameters().Select(p => CreateParameterInfo(p, methodComments)).ToList(),
            Summary = methodComments?.Summary,
            Returns = methodComments?.Returns,
            Exceptions = GetExceptionsFromComments(methodComments),
            GenericParameters = method.GetGenericArguments().Select(t => t.Name).ToList(),
            IsStatic = method.IsStatic,
            IsAsync = method.ReturnType.Name.StartsWith("Task") ||
                    method.ReturnType.Name.StartsWith("ValueTask")
        };
    }

    private static IReadOnlyList<Models.ExceptionInfo> GetExceptionsFromComments(MethodComments? methodComments)
    {
        if (methodComments is null)
        {
            return [];
        }

        // Try to access the Exceptions property via reflection since DocXml may vary by version
        var exceptionsProperty = methodComments.GetType().GetProperty("Exceptions");
        if (exceptionsProperty is null)
        {
            return [];
        }

        try
        {
            var exceptionsValue = exceptionsProperty.GetValue(methodComments);
            if (exceptionsValue is IEnumerable<(string Cref, string Text)> exceptions)
            {
                return exceptions
                    .Select(e => new Models.ExceptionInfo { Type = e.Cref ?? "Exception", Description = e.Text })
                    .ToList();
            }
        }
        catch
        {
            // Silently handle reflection failures
        }

        return [];
    }

    private static ModelParameterInfo CreateParameterInfo(SysParameterInfo param, MethodComments? methodComments)
    {
        var paramDoc = methodComments?.Parameters
            .FirstOrDefault(p => p.Name == param.Name);

        // Use RawDefaultValue for MetadataLoadContext compatibility
        string? defaultValue = null;
        if (param.HasDefaultValue)
        {
            try
            {
                defaultValue = param.RawDefaultValue?.ToString();
            }
            catch
            {
                // Ignore if RawDefaultValue is not accessible
            }
        }

        return new ModelParameterInfo
        {
            Name = param.Name ?? "unnamed",
            Type = FormatTypeName(param.ParameterType),
            IsOptional = param.IsOptional,
            DefaultValue = defaultValue,
            Description = paramDoc?.Text
        };
    }

    private IReadOnlyList<ModelPropertyInfo> GetPublicProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(p => new ModelPropertyInfo
            {
                Name = p.Name,
                Type = FormatTypeName(p.PropertyType),
                HasGetter = p.CanRead,
                HasSetter = p.CanWrite,
                Summary = GetPropertyComment(p)
            })
            .ToList();
    }

    private IReadOnlyList<ModelEventInfo> GetPublicEvents(Type type)
    {
        return type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(e => new ModelEventInfo
            {
                Name = e.Name,
                Type = FormatTypeName(e.EventHandlerType ?? typeof(EventHandler)),
                Summary = null // DocXml doesn't support events well
            })
            .ToList();
    }

    private TypeComments? GetTypeComments(Type type)
    {
        if (_docReader is null) return null;
        try
        {
            return _docReader.GetTypeComments(type);
        }
        catch
        {
            return null;
        }
    }

    private MethodComments? GetMethodComments(SysMethodInfo method)
    {
        if (_docReader is null) return null;
        try
        {
            return _docReader.GetMethodComments(method);
        }
        catch
        {
            return null;
        }
    }

    private string? GetPropertyComment(SysPropertyInfo property)
    {
        if (_docReader is null) return null;
        try
        {
            var comments = _docReader.GetMemberComments(property);
            return comments?.Summary;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatTypeName(Type type)
    {
        if (type.IsGenericType)
        {
            var genericName = type.Name.Split('`')[0];
            var args = string.Join(", ", type.GetGenericArguments().Select(FormatTypeName));
            return $"{genericName}<{args}>";
        }

        // Handle common type aliases
        return type.FullName switch
        {
            "System.String" => "string",
            "System.Int32" => "int",
            "System.Int64" => "long",
            "System.Boolean" => "bool",
            "System.Void" => "void",
            "System.Object" => "object",
            "System.Double" => "double",
            "System.Single" => "float",
            "System.Decimal" => "decimal",
            "System.Byte" => "byte",
            "System.Char" => "char",
            _ => type.Name
        };
    }

    /// <summary>
    /// Disposes of the MetadataLoadContext.
    /// </summary>
    public void Dispose()
    {
        _context.Dispose();
    }
}
