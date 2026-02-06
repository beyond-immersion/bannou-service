// ═══════════════════════════════════════════════════════════════════════════
// ABML Built-in Functions
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using System.Collections;
using System.Globalization;

namespace BeyondImmersion.Bannou.BehaviorExpressions.Functions;

/// <summary>
/// Built-in functions for ABML expressions.
/// </summary>
public static class BuiltinFunctions
{
    private static readonly Random _random = new();

    /// <summary>
    /// Registers all built-in functions.
    /// </summary>
    public static void RegisterAll(IFunctionRegistry registry)
    {
        // Collection functions
        registry.Register("length", Length, 1, 1);
        registry.Register("contains", Contains, 2, 2);
        registry.Register("first", First, 1, 1);
        registry.Register("last", Last, 1, 1);
        registry.Register("keys", Keys, 1, 1);
        registry.Register("values", Values, 1, 1);

        // String functions
        registry.Register("upper", Upper, 1, 1);
        registry.Register("lower", Lower, 1, 1);
        registry.Register("trim", Trim, 1, 1);
        registry.Register("split", Split, 2, 2);
        registry.Register("join", Join, 2, 2);
        registry.Register("format", Format, 1, -1);

        // Math functions
        registry.Register("min", Min, 2, 2);
        registry.Register("max", Max, 2, 2);
        registry.Register("abs", Abs, 1, 1);
        registry.Register("floor", Floor, 1, 1);
        registry.Register("ceil", Ceil, 1, 1);
        registry.Register("round", Round, 1, 2);
        registry.Register("random", RandomFunc, 0, 2);

        // Type functions
        registry.Register("type_of", TypeOf, 1, 1);
        registry.Register("is_null", IsNull, 1, 1);
        registry.Register("is_empty", IsEmpty, 1, 1);

        // Conversion functions
        registry.Register("to_string", ToString, 1, 1);
        registry.Register("to_number", ToNumber, 1, 1);
        registry.Register("to_bool", ToBool, 1, 1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Collection Functions
    // ═══════════════════════════════════════════════════════════════════════

    private static object? Length(object?[] args)
    {
        var value = args[0];
        return value switch
        {
            null => 0,
            string s => s.Length,
            ICollection c => c.Count,
            IEnumerable e => e.Cast<object?>().Count(),
            _ => 0
        };
    }

    private static object? Contains(object?[] args)
    {
        var collection = args[0];
        var item = args[1];

        return collection switch
        {
            null => false,
            string s when item is string sub => s.Contains(sub, StringComparison.Ordinal),
            string s => s.Contains(item?.ToString() ?? "", StringComparison.Ordinal),
            IList list => list.Contains(item),
            IDictionary dict when item is not null => dict.Contains(item),
            IDictionary => false,
            IEnumerable e => e.Cast<object?>().Contains(item),
            _ => false
        };
    }

    private static object? First(object?[] args)
    {
        var value = args[0];
        return value switch
        {
            null => null,
            string s => s.Length > 0 ? s[0].ToString() : null,
            IList list => list.Count > 0 ? list[0] : null,
            IEnumerable e => e.Cast<object?>().FirstOrDefault(),
            _ => null
        };
    }

    private static object? Last(object?[] args)
    {
        var value = args[0];
        return value switch
        {
            null => null,
            string s => s.Length > 0 ? s[^1].ToString() : null,
            IList list => list.Count > 0 ? list[^1] : null,
            IEnumerable e => e.Cast<object?>().LastOrDefault(),
            _ => null
        };
    }

    private static object? Keys(object?[] args)
    {
        var value = args[0];
        return value switch
        {
            null => Array.Empty<object>(),
            IDictionary dict => dict.Keys.Cast<object>().ToArray(),
            _ => Array.Empty<object>()
        };
    }

    private static object? Values(object?[] args)
    {
        var value = args[0];
        return value switch
        {
            null => Array.Empty<object>(),
            IDictionary dict => dict.Values.Cast<object?>().ToArray(),
            _ => Array.Empty<object>()
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // String Functions
    // ═══════════════════════════════════════════════════════════════════════

    private static object? Upper(object?[] args)
    {
        var value = args[0];
        return value?.ToString()?.ToUpperInvariant();
    }

    private static object? Lower(object?[] args)
    {
        var value = args[0];
        return value?.ToString()?.ToLowerInvariant();
    }

    private static object? Trim(object?[] args)
    {
        var value = args[0];
        return value?.ToString()?.Trim();
    }

    private static object? Split(object?[] args)
    {
        var value = args[0]?.ToString();
        var separator = args[1]?.ToString();

        if (value == null) return Array.Empty<string>();
        if (string.IsNullOrEmpty(separator)) return new[] { value };

        return value.Split(separator);
    }

    private static object? Join(object?[] args)
    {
        var separator = args[0]?.ToString() ?? "";
        var collection = args[1];

        return collection switch
        {
            null => "",
            IEnumerable<string> strings => string.Join(separator, strings),
            IEnumerable e => string.Join(separator, e.Cast<object?>().Select(x => x?.ToString() ?? "")),
            _ => collection.ToString()
        };
    }

    private static object? Format(object?[] args)
    {
        if (args.Length == 0) return "";

        var formatString = args[0]?.ToString();
        if (formatString == null) return "";

        if (args.Length == 1) return formatString;

        var formatArgs = new object?[args.Length - 1];
        Array.Copy(args, 1, formatArgs, 0, args.Length - 1);

        return string.Format(CultureInfo.InvariantCulture, formatString, formatArgs!);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Math Functions
    // ═══════════════════════════════════════════════════════════════════════

    private static object? Min(object?[] args)
    {
        var a = AbmlTypeCoercion.ToDoubleNullable(args[0]);
        var b = AbmlTypeCoercion.ToDoubleNullable(args[1]);

        if (!a.HasValue || !b.HasValue) return null;
        return Math.Min(a.Value, b.Value);
    }

    private static object? Max(object?[] args)
    {
        var a = AbmlTypeCoercion.ToDoubleNullable(args[0]);
        var b = AbmlTypeCoercion.ToDoubleNullable(args[1]);

        if (!a.HasValue || !b.HasValue) return null;
        return Math.Max(a.Value, b.Value);
    }

    private static object? Abs(object?[] args)
    {
        var value = AbmlTypeCoercion.ToDoubleNullable(args[0]);
        return value.HasValue ? Math.Abs(value.Value) : null;
    }

    private static object? Floor(object?[] args)
    {
        var value = AbmlTypeCoercion.ToDoubleNullable(args[0]);
        return value.HasValue ? Math.Floor(value.Value) : null;
    }

    private static object? Ceil(object?[] args)
    {
        var value = AbmlTypeCoercion.ToDoubleNullable(args[0]);
        return value.HasValue ? Math.Ceiling(value.Value) : null;
    }

    private static object? Round(object?[] args)
    {
        var value = AbmlTypeCoercion.ToDoubleNullable(args[0]);
        if (!value.HasValue) return null;

        var decimals = 0;
        if (args.Length > 1)
        {
            var d = AbmlTypeCoercion.ToDoubleNullable(args[1]);
            if (d.HasValue) decimals = (int)d.Value;
        }

        return Math.Round(value.Value, decimals, MidpointRounding.AwayFromZero);
    }

    private static object? RandomFunc(object?[] args)
    {
        if (args.Length == 0)
        {
            return _random.NextDouble();
        }

        if (args.Length == 1)
        {
            var max = AbmlTypeCoercion.ToDoubleNullable(args[0]);
            if (!max.HasValue) return null;
            return _random.NextDouble() * max.Value;
        }

        var min = AbmlTypeCoercion.ToDoubleNullable(args[0]);
        var maxVal = AbmlTypeCoercion.ToDoubleNullable(args[1]);
        if (!min.HasValue || !maxVal.HasValue) return null;

        return min.Value + (_random.NextDouble() * (maxVal.Value - min.Value));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Type Functions
    // ═══════════════════════════════════════════════════════════════════════

    private static object? TypeOf(object?[] args)
    {
        var value = args[0];
        return value switch
        {
            null => "null",
            bool => "boolean",
            int or long or float or double or decimal => "number",
            string => "string",
            IList => "array",
            IDictionary => "object",
            _ => value.GetType().Name.ToLowerInvariant()
        };
    }

    private static object? IsNull(object?[] args)
    {
        return args[0] == null;
    }

    private static object? IsEmpty(object?[] args)
    {
        var value = args[0];
        return value switch
        {
            null => true,
            string s => string.IsNullOrEmpty(s),
            ICollection c => c.Count == 0,
            IEnumerable e => !e.Cast<object?>().Any(),
            _ => false
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Conversion Functions
    // ═══════════════════════════════════════════════════════════════════════

    private static object? ToString(object?[] args)
    {
        var value = args[0];
        return value?.ToString() ?? "null";
    }

    private static object? ToNumber(object?[] args)
    {
        var value = args[0];
        return AbmlTypeCoercion.ToDouble(value);
    }

    private static object? ToBool(object?[] args)
    {
        var value = args[0];
        return AbmlTypeCoercion.IsTrue(value);
    }
}
