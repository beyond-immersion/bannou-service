// ═══════════════════════════════════════════════════════════════════════════
// ABML OpCode Enumeration
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.Bannou.BehaviorExpressions.Compiler;

/// <summary>
/// ABML bytecode operation codes.
/// </summary>
public enum OpCode : byte
{
    // LOADS
    /// <summary>R[A] = constants[B]</summary>
    LoadConst = 0,
    /// <summary>R[A] = scope.GetVariable(constants[B])</summary>
    LoadVar = 1,
    /// <summary>R[A] = null</summary>
    LoadNull = 2,
    /// <summary>R[A] = true</summary>
    LoadTrue = 3,
    /// <summary>R[A] = false</summary>
    LoadFalse = 4,
    /// <summary>R[A] = R[B]</summary>
    Move = 5,

    // PROPERTY ACCESS
    /// <summary>R[A] = R[B].GetProperty(constants[C])</summary>
    GetProp = 10,
    /// <summary>R[A] = R[B]?.GetProperty(constants[C])</summary>
    GetPropSafe = 11,
    /// <summary>R[A] = R[B][R[C]]</summary>
    GetIndex = 12,
    /// <summary>R[A] = R[B]?[R[C]]</summary>
    GetIndexSafe = 13,

    // ARITHMETIC
    /// <summary>R[A] = R[B] + R[C]</summary>
    Add = 20,
    /// <summary>R[A] = R[B] - R[C]</summary>
    Sub = 21,
    /// <summary>R[A] = R[B] * R[C]</summary>
    Mul = 22,
    /// <summary>R[A] = R[B] / R[C]</summary>
    Div = 23,
    /// <summary>R[A] = R[B] % R[C]</summary>
    Mod = 24,
    /// <summary>R[A] = -R[B]</summary>
    Neg = 25,

    // COMPARISON
    /// <summary>R[A] = R[B] == R[C]</summary>
    Eq = 30,
    /// <summary>R[A] = R[B] != R[C]</summary>
    Ne = 31,
    /// <summary>R[A] = R[B] &lt; R[C]</summary>
    Lt = 32,
    /// <summary>R[A] = R[B] &lt;= R[C]</summary>
    Le = 33,
    /// <summary>R[A] = R[B] &gt; R[C]</summary>
    Gt = 34,
    /// <summary>R[A] = R[B] &gt;= R[C]</summary>
    Ge = 35,

    // LOGICAL
    /// <summary>R[A] = !R[B]</summary>
    Not = 40,
    /// <summary>R[A] = R[B] &amp;&amp; R[C] (non-short-circuit, use jumps for short-circuit)</summary>
    And = 41,
    /// <summary>R[A] = R[B] || R[C] (non-short-circuit)</summary>
    Or = 42,
    /// <summary>R[A] = IsTrue(R[B]) - explicit boolean conversion</summary>
    ToBool = 43,

    // CONTROL FLOW
    /// <summary>PC = (B &lt;&lt; 8) | C</summary>
    Jump = 50,
    /// <summary>if R[A] is truthy then PC = (B &lt;&lt; 8) | C</summary>
    JumpIfTrue = 51,
    /// <summary>if R[A] is falsy then PC = (B &lt;&lt; 8) | C</summary>
    JumpIfFalse = 52,
    /// <summary>if R[A] == null then PC = (B &lt;&lt; 8) | C</summary>
    JumpIfNull = 53,
    /// <summary>if R[A] != null then PC = (B &lt;&lt; 8) | C</summary>
    JumpIfNotNull = 54,

    // FUNCTIONS
    /// <summary>Specifies arg count for following Call. A = argCount</summary>
    CallArgs = 59,
    /// <summary>R[A] = Call(constants[B], args starting at R[C])</summary>
    /// <remarks>
    /// Must be preceded by CallArgs instruction that specifies arg count.
    /// B encodes function name index in constants.
    /// Args are in R[C], R[C+1], ..., R[C+argCount-1].
    /// </remarks>
    Call = 60,

    // NULL HANDLING
    /// <summary>R[A] = R[B] ?? R[C]</summary>
    Coalesce = 70,

    // STRING
    /// <summary>R[A] = R[B] in R[C]</summary>
    In = 80,
    /// <summary>R[A] = concat(R[B], R[C])</summary>
    Concat = 81,

    // RESULT
    /// <summary>Return R[A]</summary>
    Return = 255,
}
