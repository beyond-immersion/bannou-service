#!/bin/bash
#
# detect-reflection-shortcuts.sh
#
# PreToolUse hook that fires on Edit and Write tools.
# Detects runtime-reflection patterns in bannou-service/ and plugins/*/
# paths that are forbidden by IMPLEMENTATION TENETS (T34 AOT Compatibility).
#
# Does NOT block — presents a calm reminder. Claude self-assesses whether
# the pattern has a legitimate justification against the tenet's exception
# list (tools/, structural-tests/, test-utilities/, *.tests/).
#
# INCIDENT LOG:
# 2026-03-15: DirectDispatchHelper shipped with MethodInfo.Invoke,
#   ValueTuple GetField("Item1/Item2") reflection unpacking, and
#   AppDomain.CurrentDomain.GetAssemblies().GetTypes() scanning —
#   all three AOT-hostile. Functionally equivalent to the spec's
#   statically-typed delegate dispatch (BANNOU-EMBEDDED.md Section 3);
#   structurally opposite. The divergence was never flagged.
#   See INCIDENT-HISTORY.md #19.
#
# PATTERNS DETECTED (all forbidden in shipping code per T34):
#   - Assembly.LoadFrom / Assembly.LoadFile (runtime assembly loading)
#   - Reflection.Emit namespace types (dynamic IL emission)
#   - MakeGenericMethod / MakeGenericType (runtime generic construction)
#   - MethodInfo.Invoke with object[] args (runtime method dispatch)
#   - GetField("Item1") / GetField("Item2") (tuple reflection unpacking)
#   - AppDomain.CurrentDomain.GetAssemblies (runtime assembly scanning)
#   - Expression.Compile (runtime IL emission)
#   - CSharpScript / Microsoft.CodeAnalysis.Scripting (Roslyn scripting)
#

# Read the hook input from stdin
input=$(cat)

# Extract file path and new content
file_path=$(echo "$input" | jq -r '.tool_input.file_path // .tool_input.filePath // ""' 2>/dev/null)
new_content=$(echo "$input" | jq -r '.tool_input.content // .tool_input.new_string // ""' 2>/dev/null)

# Only check C# source files
if [[ ! "$file_path" =~ \.cs$ ]]; then
    exit 0
fi

# Skip Generated/ directories — those are regenerated from schemas/templates
if [[ "$file_path" =~ /Generated/ ]]; then
    exit 0
fi

# Allow-listed paths per T34 exception list
if [[ "$file_path" =~ /tools/ ]] || \
   [[ "$file_path" =~ /structural-tests/ ]] || \
   [[ "$file_path" =~ /test-utilities/ ]] || \
   [[ "$file_path" =~ \.tests/ ]]; then
    exit 0
fi

# Only apply to bannou-service/ and plugins/*/ shipping paths
if [[ ! "$file_path" =~ /bannou-service/ ]] && [[ ! "$file_path" =~ /plugins/ ]]; then
    exit 0
fi

# Accumulate detected patterns
reasons=""

if echo "$new_content" | grep -qE 'Assembly\.(LoadFrom|LoadFile)\('; then
    reasons="${reasons}
- Assembly.LoadFrom / Assembly.LoadFile (runtime assembly loading — iOS blocker)"
fi

if echo "$new_content" | grep -qE '\bReflection\.Emit\b|\b(DynamicMethod|AssemblyBuilder|ModuleBuilder|TypeBuilder|ILGenerator)\b'; then
    reasons="${reasons}
- Reflection.Emit pattern (dynamic IL emission — never AOT-compatible)"
fi

if echo "$new_content" | grep -qE '\.MakeGenericMethod\(|\.MakeGenericType\('; then
    reasons="${reasons}
- MakeGenericMethod / MakeGenericType (runtime generic construction)"
fi

# MethodInfo.Invoke pattern: high-signal match on object[] args OR explicit MethodInfo type reference
if echo "$new_content" | grep -qE '\.Invoke\([^,]+,\s*new(\s+object)?\[\]'; then
    reasons="${reasons}
- MethodInfo.Invoke with object[] args (runtime method dispatch)"
fi

if echo "$new_content" | grep -qE '\bMethodInfo\b[^;]*\.Invoke\('; then
    reasons="${reasons}
- MethodInfo.Invoke (runtime method dispatch via reflected MethodInfo)"
fi

if echo "$new_content" | grep -qE '\.GetField\("Item[0-9]+"\)'; then
    reasons="${reasons}
- ValueTuple GetField(\"ItemN\") reflection unpacking (use tuple deconstruction)"
fi

if echo "$new_content" | grep -qE 'AppDomain\.CurrentDomain\.GetAssemblies'; then
    reasons="${reasons}
- AppDomain.CurrentDomain.GetAssemblies (runtime assembly scanning)"
fi

if echo "$new_content" | grep -qE 'Expression\.Compile\(\)'; then
    reasons="${reasons}
- Expression.Compile() (runtime IL emission)"
fi

if echo "$new_content" | grep -qE '\bCSharpScript\b|Microsoft\.CodeAnalysis\.Scripting'; then
    reasons="${reasons}
- Roslyn scripting (CSharpScript / Microsoft.CodeAnalysis.Scripting)"
fi

# If any patterns matched, present a reminder
if [[ -n "$reasons" ]]; then
    message="You are adding runtime-reflection pattern(s) to shipping code:${reasons}

Per IMPLEMENTATION TENETS (T34 AOT Compatibility), these patterns are FORBIDDEN in bannou-service/ and plugins/*/ paths. Consumer targets include iOS (Mono AOT, no JIT permitted) and NativeAOT (standalone builds).

Self-assess before proceeding:
1. Is there a statically-typed alternative? (Closed-generic delegate Func<TRequest,TResponse>, typeof(T), source-generated dispatch, tuple deconstruction)
2. If this is pre-existing tech debt tracked in issue #724, are you extending the violation or fixing it? Extending is forbidden.
3. If you believe this is justified, cite the specific T34 exception covering it. If no exception covers it, stop and present the conflict.

If a planning doc prescribes static dispatch (e.g., docs/planning/BANNOU-EMBEDDED.md Section 3 prescribing inline typed delegates per client method), shipping reflection-based dispatch ALSO violates QUALITY TENETS (T33 Design Specification Fidelity) — stop and present the divergence to the user before writing more code."

    jq -n --arg msg "$message" '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: $msg
        }
    }'
    exit 0
fi

# No concerns — allow silently
exit 0
