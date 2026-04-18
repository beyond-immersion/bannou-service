// =============================================================================
// Channel Coordination Compilation Tests
//
// Tests for ABML channel-coordination action compilation:
//   - emit              (EmitAction)
//   - wait_for          (WaitForAction)
//   - sync              (SyncAction)
//   - continuation_point (ContinuationPointAction)
//
// Part of the Phase 1A bridge validation described in
// docs/plans/CINEMATIC-PHASE-1-COMPOSER-SDK.md. These tests confirm whether
// the ABML compiler recognizes and compiles the channel-coordination action
// nodes that cinematic-composer's AbmlExporter will eventually emit.
//
// Sub-groups:
//   1. Individual action compilation (each action type alone in a document).
//   2. Combined throw-and-dodge scenario compilation (all four in one document).
//   3. Diagnostic: what opcodes do emit/wait_for/sync actually compile to?
//      These tests fail with Assert.Fail and print the actual bytecode so a
//      follow-up session can pin specific opcode assertions.
//   4. Parse-time failure mode probes (undefined default flow, etc.).
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Compiler;
using BeyondImmersion.Bannou.BehaviorCompiler.Runtime;
using Xunit;
using AbmlCompiler = BeyondImmersion.Bannou.BehaviorCompiler.Compiler.BehaviorCompiler;

namespace BeyondImmersion.Bannou.BehaviorCompiler.Tests;

/// <summary>
/// Tests for ABML channel-coordination action compilation.
/// </summary>
/// <remarks>
/// Session 1 of Phase 1A bridge validation. Every test either passes
/// (documenting a confirmed capability) or fails with a message that
/// pinpoints the gap, so failing tests themselves serve as the gap report.
/// </remarks>
public class ChannelCoordinationCompilationTests
{
    private readonly AbmlCompiler _compiler = new();

    // -------------------------------------------------------------------------
    // YAML FIXTURES
    //
    // Indentation shape (spaces):
    //   flows:            col 0
    //     main:           col 2
    //       actions:      col 4
    //         - action:   col 6  (list item; dash at col 6, 'action' at col 8)
    //             key: v  col 12 (nested property of the action)
    // -------------------------------------------------------------------------

    private const string MinimalPreamble = @"version: ""2.0""
metadata:
  id: test_cinematic_participant
flows:
  main:
    actions:
";

    /// <summary>
    /// Wraps a block of YAML action entries in a minimal valid ABML document body.
    /// The caller provides the action list already indented to column 6.
    /// </summary>
    private static string WrapSingleFlow(string indentedActions)
    {
        return MinimalPreamble + indentedActions;
    }

    // -------------------------------------------------------------------------
    // SUB-GROUP 1: INDIVIDUAL ACTION COMPILATION
    //
    // Assert each coordination action node compiles without error. These tests
    // verify the parser and compiler at least recognize the action nodes — they
    // do not assert anything about the resulting bytecode beyond non-emptiness.
    // -------------------------------------------------------------------------

    [Fact]
    public void CompileYaml_EmitAction_ProducesBytecode()
    {
        var yaml = WrapSingleFlow(@"      - emit:
          signal: throw_release
");

        var result = _compiler.CompileYaml(yaml);

        AssertCompiles(result, nameof(CompileYaml_EmitAction_ProducesBytecode));
        var bytecode = result.Bytecode;
        Assert.NotNull(bytecode);
        Assert.NotEmpty(bytecode);
    }

    [Fact]
    public void CompileYaml_WaitForAction_ProducesBytecode()
    {
        var yaml = WrapSingleFlow(@"      - wait_for:
          signal: throw_release
");

        var result = _compiler.CompileYaml(yaml);

        AssertCompiles(result, nameof(CompileYaml_WaitForAction_ProducesBytecode));
        var bytecode = result.Bytecode;
        Assert.NotNull(bytecode);
        Assert.NotEmpty(bytecode);
    }

    [Fact]
    public void CompileYaml_SyncAction_ProducesBytecode()
    {
        var yaml = WrapSingleFlow(@"      - sync:
          point: scene_start
");

        var result = _compiler.CompileYaml(yaml);

        AssertCompiles(result, nameof(CompileYaml_SyncAction_ProducesBytecode));
        var bytecode = result.Bytecode;
        Assert.NotNull(bytecode);
        Assert.NotEmpty(bytecode);
    }

    [Fact]
    public void CompileYaml_ContinuationPointAction_ProducesCpOpcode()
    {
        var yaml = @"version: ""2.0""
metadata:
  id: test_cinematic_participant
flows:
  main:
    actions:
      - continuation_point:
          name: dodge_choice
          timeout: 400ms
          default_flow: dodge_left_default
  dodge_left_default:
    actions:
      - log:
          message: defaulted
";

        var result = _compiler.CompileYaml(yaml);

        AssertCompiles(result, nameof(CompileYaml_ContinuationPointAction_ProducesCpOpcode));
        var bytecode = result.Bytecode;
        Assert.NotNull(bytecode);

        // The compiled bytecode should contain the CONTINUATION_POINT opcode (0x70).
        // Confirmed by existing BehaviorCompilerTests.CompileYaml_ContinuationPoint_GeneratesCpOpcode;
        // this test re-asserts the same capability at the SDK test layer so the
        // composite scenarios below (sub-group 2) have a direct SDK-level reference.
        Assert.Contains((byte)BehaviorOpcode.ContinuationPoint, bytecode);
    }

    // -------------------------------------------------------------------------
    // SUB-GROUP 2: COMBINED THROW-AND-DODGE SCENARIO (ONE PARTICIPANT)
    //
    // Per Session 0 inventory findings, "multi-channel" in the Phase 1 plan
    // maps to SEPARATE ABML documents (one per participant), each compiled
    // independently. This sub-group compiles the DEFENDER participant's
    // document from the Phase 1A throw-and-dodge test scenario, combining
    // wait_for, continuation_point, and emit in one document.
    // -------------------------------------------------------------------------

    /// <summary>
    /// The defender participant's ABML document for the throw-and-dodge scenario.
    /// </summary>
    /// <remarks>
    /// Shape:
    ///   (1) defender waits for throw_release signal from attacker
    ///   (2) hits a continuation_point for the dodge QTE
    ///   (3) on timeout, executes the dodge_left_default flow
    ///   (4) emits dodge_resolved to notify camera/attacker channels
    /// </remarks>
    private const string DefenderDocument = @"version: ""2.0""
metadata:
  id: test_throw_dodge_defender
flows:
  main:
    actions:
      - log:
          message: defender brace
      - wait_for:
          signal: throw_release
      - continuation_point:
          name: dodge_choice
          timeout: 400ms
          default_flow: dodge_left_default
      - emit:
          signal: dodge_resolved
  dodge_left_default:
    actions:
      - log:
          message: rolled left (default)
";

    [Fact]
    public void CompileYaml_ThrowAndDodgeDefenderDocument_Compiles()
    {
        var result = _compiler.CompileYaml(DefenderDocument);

        AssertCompiles(result, nameof(CompileYaml_ThrowAndDodgeDefenderDocument_Compiles));
        var bytecode = result.Bytecode;
        Assert.NotNull(bytecode);
        Assert.NotEmpty(bytecode);
    }

    [Fact]
    public void CompileYaml_ThrowAndDodgeDefenderDocument_ContainsCpOpcode()
    {
        var result = _compiler.CompileYaml(DefenderDocument);

        AssertCompiles(result, nameof(CompileYaml_ThrowAndDodgeDefenderDocument_ContainsCpOpcode));
        var bytecode = result.Bytecode;
        Assert.NotNull(bytecode);

        Assert.Contains((byte)BehaviorOpcode.ContinuationPoint, bytecode);
    }

    // -------------------------------------------------------------------------
    // SUB-GROUP 3: DIAGNOSTIC — WHICH OPCODES DO emit / wait_for / sync EMIT?
    //
    // These tests fail deliberately with Assert.Fail and print the actual
    // distinct opcodes produced by each action. This captures diagnostic
    // output so a follow-up session can pin specific opcode assertions.
    //
    // Known candidate opcodes:
    //   - EmitIntent (0x51): if coordination reuses the intent channel system
    //   - ContinuationPoint (0x70): unlikely but possible
    //   - A yet-undefined sync/signal opcode: would need to be added
    //   - (Nothing — silent no-op or compile failure also reported)
    //
    // NOTE: A compile FAILURE is also diagnostic — the test message
    // explicitly calls this out so the result is unambiguous.
    // -------------------------------------------------------------------------

    [Fact]
    public void CompileYaml_EmitAction_Diagnostic_PrintsOpcodes()
    {
        var yaml = WrapSingleFlow(@"      - emit:
          signal: throw_release
");

        var result = _compiler.CompileYaml(yaml);

        if (!result.Success || result.Bytecode is null)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail(
                "[DIAGNOSTIC] emit compilation failed — the ABML compiler does not currently " +
                "produce bytecode for the emit action. Errors: " + errors);
            return; // unreachable, for compiler flow analysis
        }

        var opcodeSummary = SummarizeOpcodes(result.Bytecode);

        Assert.Fail(
            "[DIAGNOSTIC] emit compiled. Distinct opcodes: " + opcodeSummary +
            ". Bytecode length: " + result.Bytecode.Length.ToString() +
            ". Next session: replace this Assert.Fail with a specific opcode assertion " +
            "based on which opcode in this list represents signal emission.");
    }

    [Fact]
    public void CompileYaml_WaitForAction_Diagnostic_PrintsOpcodes()
    {
        var yaml = WrapSingleFlow(@"      - wait_for:
          signal: throw_release
");

        var result = _compiler.CompileYaml(yaml);

        if (!result.Success || result.Bytecode is null)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail(
                "[DIAGNOSTIC] wait_for compilation failed — the ABML compiler does not " +
                "currently produce bytecode for the wait_for action. Errors: " + errors);
            return;
        }

        var opcodeSummary = SummarizeOpcodes(result.Bytecode);

        Assert.Fail(
            "[DIAGNOSTIC] wait_for compiled. Distinct opcodes: " + opcodeSummary +
            ". Bytecode length: " + result.Bytecode.Length.ToString() +
            ". Next session: replace this Assert.Fail with a specific opcode assertion " +
            "based on which opcode represents waiting on a signal.");
    }

    [Fact]
    public void CompileYaml_SyncAction_Diagnostic_PrintsOpcodes()
    {
        var yaml = WrapSingleFlow(@"      - sync:
          point: scene_start
");

        var result = _compiler.CompileYaml(yaml);

        if (!result.Success || result.Bytecode is null)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail(
                "[DIAGNOSTIC] sync compilation failed — the ABML compiler does not " +
                "currently produce bytecode for the sync action. Errors: " + errors);
            return;
        }

        var opcodeSummary = SummarizeOpcodes(result.Bytecode);

        Assert.Fail(
            "[DIAGNOSTIC] sync compiled. Distinct opcodes: " + opcodeSummary +
            ". Bytecode length: " + result.Bytecode.Length.ToString() +
            ". Next session: replace this Assert.Fail with a specific opcode assertion " +
            "based on which opcode represents the barrier.");
    }

    // -------------------------------------------------------------------------
    // SUB-GROUP 4: PARSE-TIME FAILURE MODE PROBES
    //
    // Verify the compiler enforces the expected semantic rules for these
    // actions. If a probe fails, that itself is diagnostic — the compiler
    // is either stricter or looser than the SDK needs it to be.
    // -------------------------------------------------------------------------

    [Fact]
    public void CompileYaml_ContinuationPoint_WithUndefinedDefaultFlow_FailsSemantic()
    {
        var yaml = WrapSingleFlow(@"      - continuation_point:
          name: dodge_choice
          timeout: 400ms
          default_flow: nonexistent_flow
");

        var result = _compiler.CompileYaml(yaml);

        // A continuation_point that references a nonexistent default flow should fail
        // semantic analysis, mirroring the same rule that catches undefined flow
        // references in GotoAction. Existing reference:
        // BehaviorCompilerTests.CompileYaml_UndefinedFlow_ReturnsError.
        Assert.False(
            result.Success,
            "continuation_point referencing a nonexistent default flow should fail " +
            "semantic analysis. If this test fails (i.e., compilation succeeds), the " +
            "compiler is not validating continuation_point default_flow references — " +
            "a gap for cinematic-composer, which will need to catch this at SDK validation " +
            "rather than relying on the compiler. Errors reported: " +
            string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void CompileYaml_WaitFor_WithoutMatchingEmitInSameDocument_Compiles()
    {
        // Probes the multi-document model: in a real cinematic scenario, a
        // participant's wait_for will often match an emit in ANOTHER participant's
        // document. If the compiler requires emit/wait_for pairing WITHIN a single
        // document, that is a gap for the multi-document bundle shape that
        // cinematic-composer needs.
        //
        // Expected outcome: compiles successfully (compiler does NOT enforce
        // in-document pairing, consistent with the multi-document model).
        var yaml = WrapSingleFlow(@"      - wait_for:
          signal: signal_from_another_participant
");

        var result = _compiler.CompileYaml(yaml);

        if (!result.Success)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail(
                "wait_for without a matching emit in the same document failed to compile. " +
                "This indicates the ABML compiler enforces in-document emit/wait pairing, " +
                "which is a gap for multi-document cinematic scenarios where the matching " +
                "emit lives in another participant's document. Errors: " + errors);
        }
    }

    // -------------------------------------------------------------------------
    // HELPERS
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that a compilation result succeeded. On failure, includes the
    /// test name and all error messages in the assertion message (mirrors the
    /// pattern used in BehaviorCompilerTests for consistent failure output).
    /// </summary>
    private static void AssertCompiles(CompilationResult result, string testName)
    {
        if (!result.Success)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Message));
            Assert.Fail(testName + ": compilation failed: " + errors);
        }
    }

    /// <summary>
    /// Produces a human-readable summary of the distinct opcodes in a bytecode
    /// array, mapping each byte to its BehaviorOpcode enum name where defined.
    /// Used by sub-group 3 diagnostic tests.
    /// </summary>
    private static string SummarizeOpcodes(byte[] bytecode)
    {
        var distinct = bytecode.Distinct().OrderBy(b => b).ToArray();
        return string.Join(", ", distinct.Select(FormatOpcode));
    }

    /// <summary>
    /// Formats a single opcode byte as "0xXX (Name)" or "0xXX (unknown)".
    /// </summary>
    private static string FormatOpcode(byte b)
    {
        var hex = "0x" + b.ToString("X2");
        return Enum.IsDefined(typeof(BehaviorOpcode), b)
            ? hex + " (" + ((BehaviorOpcode)b).ToString() + ")"
            : hex + " (unknown)";
    }
}
