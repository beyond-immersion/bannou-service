// ═══════════════════════════════════════════════════════════════════════════
// ABML Type Coercion Helpers
// Static helper methods for type coercion and operations in ABML expressions.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;

namespace BeyondImmersion.Bannou.BehaviorExpressions.Expressions;

/// <summary>
/// Provides static helper methods for type coercion and operations in ABML expressions.
/// </summary>
public static class AbmlTypeCoercion
{
    /// <summary>
    /// Evaluates the truthiness of a value for conditional expressions.
    /// </summary>
    public static bool IsTrue(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            float f => f != 0f,
            double d => d != 0d,
            decimal dec => dec != 0m,
            string s => s.Length > 0,
            ICollection c => c.Count > 0,
            _ => true
        };
    }

    /// <summary>
    /// Adds two values together with type coercion.
    /// </summary>
    public static object Add(object? left, object? right)
    {
        // Handle null cases
        if (left is null && right is null) return 0;
        if (left is null) return right ?? 0;
        if (right is null) return left;

        // Use explicit type checks for reliable boxed value handling
        if (left is int li && right is int ri) return li + ri;
        if (left is long ll && right is long rl) return ll + rl;
        if (left is double ld && right is double rd) return ld + rd;

        // String concatenation
        if (left is string ls) return ls + (right?.ToString() ?? "");
        if (right is string rs) return (left?.ToString() ?? "") + rs;

        return ToDouble(left) + ToDouble(right);
    }

    /// <summary>
    /// Subtracts one value from another.
    /// </summary>
    public static object Subtract(object? left, object? right)
    {
        // Use explicit type checks for reliable boxed value handling
        if (left is int li && right is int ri) return li - ri;
        if (left is long ll && right is long rl) return ll - rl;
        if (left is double ld && right is double rd) return ld - rd;
        return ToDouble(left) - ToDouble(right);
    }

    /// <summary>
    /// Multiplies two values.
    /// </summary>
    public static object Multiply(object? left, object? right)
    {
        // Use explicit type checks for reliable boxed value handling
        if (left is int li && right is int ri) return li * ri;
        if (left is long ll && right is long rl) return ll * rl;
        if (left is double ld && right is double rd) return ld * rd;
        return ToDouble(left) * ToDouble(right);
    }

    /// <summary>
    /// Divides one value by another.
    /// </summary>
    public static object Divide(object? left, object? right)
    {
        // Use explicit type checks for reliable boxed value handling
        if (left is int li && right is int ri)
        {
            if (ri == 0) throw new DivideByZeroException();
            return li / ri;
        }
        if (left is long ll && right is long rl)
        {
            if (rl == 0) throw new DivideByZeroException();
            return ll / rl;
        }
        if (left is double ld && right is double rd) return ld / rd;
        return ToDouble(left) / ToDouble(right);
    }

    /// <summary>
    /// Computes the modulo of two values.
    /// </summary>
    public static object Modulo(object? left, object? right)
    {
        // Use explicit type checks for reliable boxed value handling
        if (left is int li && right is int ri)
        {
            if (ri == 0) throw new DivideByZeroException();
            return li % ri;
        }
        if (left is long ll && right is long rl)
        {
            if (rl == 0) throw new DivideByZeroException();
            return ll % rl;
        }
        return ToDouble(left) % ToDouble(right);
    }

    /// <summary>
    /// Negates a numeric value.
    /// </summary>
    public static object Negate(object? value)
    {
        // Use explicit type checks for reliable boxed value handling
        if (value is null) return 0;
        if (value is int i) return -i;
        if (value is long l) return -l;
        if (value is double d) return -d;
        return -ToDouble(value);
    }

    /// <summary>
    /// Compares two values.
    /// </summary>
    public static int Compare(object? left, object? right)
    {
        return (left, right) switch
        {
            (null, null) => 0,
            (null, _) => -1,
            (_, null) => 1,
            (int l, int r) => l.CompareTo(r),
            (double l, double r) => l.CompareTo(r),
            (string l, string r) => string.Compare(l, r, StringComparison.Ordinal),
            (IComparable l, _) => l.CompareTo(Convert.ChangeType(right, l.GetType())),
            _ => throw new InvalidOperationException($"Cannot compare {left?.GetType().Name} and {right?.GetType().Name}")
        };
    }

    /// <summary>
    /// Tests equality between two values.
    /// </summary>
    public static new bool Equals(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return left is null && right is null;
        if (left.GetType() == right.GetType()) return left.Equals(right);
        if (IsNumeric(left) && IsNumeric(right)) return ToDouble(left) == ToDouble(right);
        return left.Equals(right);
    }

    /// <summary>
    /// Gets a property value from an object.
    /// </summary>
    public static object? GetProperty(object? target, string name)
    {
        if (target is null) return null;
        return target switch
        {
            IDictionary<string, object?> dict => dict.TryGetValue(name, out var v) ? v : null,
            IDictionary dict => dict.Contains(name) ? dict[name] : null,
            _ => target.GetType().GetProperty(name)?.GetValue(target)
        };
    }

    /// <summary>
    /// Gets a value from an indexable object.
    /// </summary>
    public static object? GetIndex(object? target, object? index)
    {
        if (target is null) return null;

        // Convert numeric index to int (parser produces doubles for numbers)
        var intIndex = index switch
        {
            int i => i,
            long l => (int)l,
            double d when d == Math.Floor(d) => (int)d,
            _ => (int?)null
        };

        if (intIndex.HasValue)
        {
            var i = intIndex.Value;
            return target switch
            {
                IList list when i >= 0 && i < list.Count => list[i],
                string s when i >= 0 && i < s.Length => s[i].ToString(),
                _ => null
            };
        }

        // String key for dictionary access
        return target switch
        {
            IDictionary<string, object?> dict when index is string s => dict.TryGetValue(s, out var v) ? v : null,
            _ => null
        };
    }

    /// <summary>
    /// Tests if a collection contains an item.
    /// </summary>
    public static bool Contains(object? collection, object? item)
    {
        if (collection is null) return false;

        // String contains substring
        if (collection is string s && item is string sub)
            return s.Contains(sub);

        // Dictionary key check
        if (collection is IDictionary<string, object?> dict && item is string key)
            return dict.ContainsKey(key);

        // List contains with numeric coercion
        if (collection is IList list)
        {
            // Direct check first
            if (list.Contains(item)) return true;

            // If item is numeric, check for numeric equality with each element
            if (IsNumeric(item))
            {
                var itemValue = ToDouble(item);
                foreach (var element in list)
                {
                    if (IsNumeric(element) && ToDouble(element) == itemValue)
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Concatenates two values as strings.
    /// </summary>
    public static string Concat(object? left, object? right) =>
        (left?.ToString() ?? "") + (right?.ToString() ?? "");

    /// <summary>
    /// Converts a value to double.
    /// </summary>
    public static double ToDouble(object? value)
    {
        return value switch
        {
            null => 0.0,
            int i => i,
            long l => l,
            float f => f,
            double d => d,
            decimal dec => (double)dec,
            string s when double.TryParse(s, out var d) => d,
            _ => Convert.ToDouble(value)
        };
    }

    /// <summary>
    /// Checks if a value is numeric.
    /// </summary>
    public static bool IsNumeric(object? value) =>
        value is int or long or float or double or decimal;

    /// <summary>
    /// Converts a value to double, returning null if the value is null.
    /// </summary>
    public static double? ToDoubleNullable(object? value)
    {
        if (value is null) return null;
        return value switch
        {
            int i => i,
            long l => l,
            float f => f,
            double d => d,
            decimal dec => (double)dec,
            string s when double.TryParse(s, out var d) => d,
            string => null,
            _ => Convert.ToDouble(value)
        };
    }
}
