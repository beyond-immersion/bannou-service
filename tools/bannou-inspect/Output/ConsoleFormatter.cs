namespace BeyondImmersion.BannouService.Tools.Inspect.Output;

/// <summary>
/// Formats inspection results for console output.
/// </summary>
public static class ConsoleFormatter
{
    /// <summary>
    /// Formats type information for console display.
    /// </summary>
    public static void WriteTypeInfo(Models.TypeInfo info)
    {
        Console.WriteLine();
        WriteHeader($"{info.Kind} {info.FullName}");

        if (!string.IsNullOrWhiteSpace(info.Summary))
        {
            Console.WriteLine();
            WriteSection("Summary");
            WriteWrapped(info.Summary, "  ");
        }

        if (info.BaseType is not null && info.BaseType != "System.Object")
        {
            Console.WriteLine();
            WriteSection("Base Type");
            Console.WriteLine($"  {info.BaseType}");
        }

        if (info.Interfaces.Count > 0)
        {
            Console.WriteLine();
            WriteSection("Implements");
            foreach (var iface in info.Interfaces)
            {
                Console.WriteLine($"  - {iface}");
            }
        }

        if (info.GenericParameters.Count > 0)
        {
            Console.WriteLine();
            WriteSection("Generic Parameters");
            Console.WriteLine($"  <{string.Join(", ", info.GenericParameters)}>");
        }

        if (info.Properties.Count > 0)
        {
            Console.WriteLine();
            WriteSection($"Properties ({info.Properties.Count})");
            foreach (var prop in info.Properties)
            {
                var accessors = new List<string>();
                if (prop.HasGetter) accessors.Add("get");
                if (prop.HasSetter) accessors.Add("set");
                var accessorStr = accessors.Count > 0 ? $" {{ {string.Join("; ", accessors)}; }}" : "";

                Console.WriteLine($"  {prop.Type} {prop.Name}{accessorStr}");
                if (!string.IsNullOrWhiteSpace(prop.Summary))
                {
                    WriteWrapped(prop.Summary, "    // ");
                }
            }
        }

        if (info.Methods.Count > 0)
        {
            Console.WriteLine();
            WriteSection($"Methods ({info.Methods.Count})");
            foreach (var method in info.Methods)
            {
                WriteMethodSignature(method, "  ");
            }
        }

        if (info.Events.Count > 0)
        {
            Console.WriteLine();
            WriteSection($"Events ({info.Events.Count})");
            foreach (var evt in info.Events)
            {
                Console.WriteLine($"  event {evt.Type} {evt.Name}");
            }
        }

        if (!string.IsNullOrWhiteSpace(info.Remarks))
        {
            Console.WriteLine();
            WriteSection("Remarks");
            WriteWrapped(info.Remarks, "  ");
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"Assembly: {info.AssemblyName}");
        Console.ResetColor();
    }

    /// <summary>
    /// Formats method information for console display.
    /// </summary>
    public static void WriteMethodInfo(IReadOnlyList<Models.MethodInfo> methods, string typeName)
    {
        if (methods.Count == 0)
        {
            Console.WriteLine($"No method found matching the specified name in {typeName}.");
            return;
        }

        Console.WriteLine();
        WriteHeader($"Methods in {typeName}");

        foreach (var method in methods)
        {
            Console.WriteLine();
            WriteMethodSignature(method, "");

            if (!string.IsNullOrWhiteSpace(method.Summary))
            {
                Console.WriteLine();
                WriteWrapped(method.Summary, "  ");
            }

            if (method.Parameters.Count > 0)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  Parameters:");
                Console.ResetColor();
                foreach (var param in method.Parameters)
                {
                    var defaultStr = param.IsOptional ? $" = {param.DefaultValue ?? "default"}" : "";
                    Console.WriteLine($"    {param.Type} {param.Name}{defaultStr}");
                    if (!string.IsNullOrWhiteSpace(param.Description))
                    {
                        WriteWrapped(param.Description, "      // ");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(method.Returns))
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  Returns:");
                Console.ResetColor();
                WriteWrapped(method.Returns, "    ");
            }

            if (method.Exceptions.Count > 0)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  Exceptions:");
                Console.ResetColor();
                foreach (var exc in method.Exceptions)
                {
                    Console.WriteLine($"    {exc.Type}");
                    if (!string.IsNullOrWhiteSpace(exc.Description))
                    {
                        WriteWrapped(exc.Description, "      ");
                    }
                }
            }
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Writes a list of types to the console.
    /// </summary>
    public static void WriteTypeList(IReadOnlyList<string> types, string heading)
    {
        Console.WriteLine();
        WriteHeader(heading);
        Console.WriteLine();

        if (types.Count == 0)
        {
            Console.WriteLine("  No types found.");
            return;
        }

        // Group by namespace
        var grouped = types
            .GroupBy(t =>
            {
                var lastDot = t.LastIndexOf('.');
                return lastDot > 0 ? t[..lastDot] : "(No namespace)";
            })
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  {group.Key}");
            Console.ResetColor();

            foreach (var type in group.OrderBy(t => t))
            {
                var simpleName = type.Split('.').Last();
                Console.WriteLine($"    {simpleName}");
            }

            Console.WriteLine();
        }

        Console.WriteLine($"Total: {types.Count} types");
    }

    private static void WriteHeader(string text)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(text);
        Console.WriteLine(new string('=', Math.Min(text.Length, 80)));
        Console.ResetColor();
    }

    private static void WriteSection(string text)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"{text}:");
        Console.ResetColor();
    }

    private static void WriteMethodSignature(Models.MethodInfo method, string indent)
    {
        var modifiers = new List<string>();
        if (method.IsStatic) modifiers.Add("static");
        if (method.IsAsync) modifiers.Add("async");

        var modifierStr = modifiers.Count > 0 ? string.Join(" ", modifiers) + " " : "";
        var genericStr = method.GenericParameters.Count > 0
            ? $"<{string.Join(", ", method.GenericParameters)}>"
            : "";

        var paramStr = string.Join(", ", method.Parameters.Select(p =>
        {
            var optional = p.IsOptional ? " = " + (p.DefaultValue ?? "default") : "";
            return $"{p.Type} {p.Name}{optional}";
        }));

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{indent}{modifierStr}{method.ReturnType} {method.Name}{genericStr}({paramStr})");
        Console.ResetColor();
    }

    private static void WriteWrapped(string text, string indent)
    {
        // Clean up XML doc whitespace
        text = Regex.Replace(text.Trim(), @"\s+", " ");

        var maxWidth = Math.Max(40, Console.WindowWidth - indent.Length - 2);
        var words = text.Split(' ');
        var currentLine = new StringBuilder();

        foreach (var word in words)
        {
            if (currentLine.Length + word.Length + 1 > maxWidth && currentLine.Length > 0)
            {
                Console.WriteLine($"{indent}{currentLine}");
                currentLine.Clear();
            }

            if (currentLine.Length > 0)
            {
                currentLine.Append(' ');
            }
            currentLine.Append(word);
        }

        if (currentLine.Length > 0)
        {
            Console.WriteLine($"{indent}{currentLine}");
        }
    }
}
