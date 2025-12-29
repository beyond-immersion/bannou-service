// ═══════════════════════════════════════════════════════════════════════════
// ABML VM Configuration
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.BannouService.Abml.Compiler;

/// <summary>
/// Configuration constants for the ABML bytecode VM.
/// </summary>
public static class VmConfig
{
    /// <summary>Maximum number of registers (256).</summary>
    public const int MaxRegisters = 256;

    /// <summary>Maximum number of constants (256).</summary>
    public const int MaxConstants = 256;

    /// <summary>Maximum bytecode length.</summary>
    public const int MaxInstructions = 65536;

    /// <summary>Maximum jump offset (16-bit).</summary>
    public const int MaxJumpOffset = 65535;

    /// <summary>Maximum function argument count.</summary>
    public const int MaxFunctionArgs = 16;

    /// <summary>Default LRU cache size.</summary>
    public const int DefaultCacheSize = 10000;

    /// <summary>Maximum nesting depth.</summary>
    public const int MaxNestingDepth = 100;
}
