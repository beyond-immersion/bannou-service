#!/bin/bash
#
# block-backwards-compatibility.sh
#
# PreToolUse hook that fires on ALL tool calls.
# Detects when Claude is hedging decisions by introducing "backwards
# compatibility" concerns that were never requested.
#
# This project is pre-release with zero external consumers. When the
# user makes a decision, the correct action is to execute it completely.
#
# All categories use permissionDecision: "allow" with a calm reminder.
# Claude self-assesses whether the warning applies and adjusts.
#
# PATTERN CATEGORIES:
#   1. Backwards/backward compatibility — the explicit phrase
#   2. Breaking change anxiety — "this would be a breaking change"
#   3. Compatibility shims — "shim", "alias", "re-export", "wrapper for old"
#   4. Soft removal — "keep for now", "leave in place", "retain for"
#   5. Consumer anxiety — "something might depend on", "callers expect"
#

# Read the hook input from stdin
input=$(cat)

# Stringify the entire tool_input for pattern matching
tool_input=$(echo "$input" | jq -r '.tool_input | tostring' 2>/dev/null)

if [[ -z "$tool_input" ]]; then
    exit 0
fi

# ── Category 1: Explicit "backwards compatibility" phrase ─────────
if echo "$tool_input" | grep -qiE '\bbackwards? compatibility\b|\bbackwards?[ -]compatible\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "You used the phrase \"backwards compatibility.\" This project is pre-release with zero external consumers. When the user makes a decision, execute it completely. If you believe a change will break something specific in this codebase, name the file and line."
        }
    }'
    exit 0
fi

# ── Category 2: Breaking change anxiety ───────────────────────────
if echo "$tool_input" | grep -qiE '\b(this|that|it) (would|could|will|might) (be a |cause a |introduce a )?breaking change\b|\bbreaking change\b.*\b(concern|risk|issue|problem)\b|\bavoid(ing)? (a )?breaking change\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "You mentioned \"breaking change.\" Is this based on a specific caller in this codebase that will fail? If so, name it and fix it. If not, proceed — a rename that requires updating callers is not a reason to keep the old name."
        }
    }'
    exit 0
fi

# ── Category 3: Compatibility shims ──────────────────────────────
if echo "$tool_input" | grep -qiE '\bcompatibility (shim|layer|wrapper|bridge)\b|\b(shim|alias|wrapper) for (the )?(old|previous|existing|original)\b|\bre-?export(ing|s)? (the )?(old|previous|removed|original)\b|\btype alias for (the )?(old|previous)\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "You are creating a compatibility shim or alias for something old. If the user renamed X to Y, rename it everywhere. If they removed it, delete it everywhere. Do not leave bridges to the past."
        }
    }'
    exit 0
fi

# ── Category 4: Soft removal / refusal to delete ─────────────────
if echo "$tool_input" | grep -qiE '\bkeep(ing)? (it |this |the )?(around |in place )?(for now|for safety|just in case|until)\b|\bretain(ing|ed)? for\b|\bleav(e|ing) (it |this |the )?(in place|as[- ]is|alone|untouched).*\b(for now|just in case|until|safety)\b|\b\/\/ removed\b|\b\/\/ deprecated\b.*\bkept\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "You appear to be soft-removing instead of actually removing. If the user said to remove it, remove it. If you are uncertain whether removal is correct, ask before editing rather than half-removing."
        }
    }'
    exit 0
fi

# ── Category 5: Consumer anxiety ─────────────────────────────────
if echo "$tool_input" | grep -qiE '\bsomething (might|could|may) (depend|rely) on\b|\bcallers (might |may )?(still )?(expect|need|use|rely)\b|\b(external|other|downstream) (consumers?|callers?|users?) (might|could|may)\b|\bin case (something|someone|anything) (still )?(depends?|relies?|uses?|needs?)\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "You are expressing concern about hypothetical consumers. Name the specific file and call site, or drop the concern. This project has no external consumers, and internal callers can be found with grep."
        }
    }'
    exit 0
fi

# No match — allow silently
exit 0
