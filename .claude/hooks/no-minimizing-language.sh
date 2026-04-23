#!/bin/bash
#
# no-minimizing-language.sh
#
# PreToolUse hook that fires on ALL tool calls.
# Detects language patterns that signal Claude is rationalizing a
# deviation, minimizing a problem, or making a decision without
# user direction.
#
# All categories use permissionDecision: "allow" with a calm reminder.
# Claude self-assesses whether the warning applies and adjusts.
#
# PATTERN CATEGORIES:
#   1. Minimization — downplaying severity ("semantic", "minor", "trivial")
#   2. Rationalization — justifying deviation ("established pattern", "for consistency")
#   3. Self-authorization — granting itself permission ("I'll go ahead", "safe to assume")
#   4. Exception-invention — creating carve-outs ("in this case", "technically", "borderline")
#   5. Precedent-mining — using violations as justification ("existing code", "other services")
#   6. General minimization — downgrading findings ("quality improvement", "cosmetic")
#   7. Work-avoidance — rationalizing not solving a problem ("harmless", "skip")
#   8. Efficiency corner-cutting — doing less than asked ("sufficient context", "good enough")
#

# Read the hook input from stdin
input=$(cat)

# Stringify the entire tool_input for pattern matching
tool_input=$(echo "$input" | jq -r '.tool_input | tostring' 2>/dev/null)

if [[ -z "$tool_input" ]]; then
    exit 0
fi

# ── Category 1: Minimization (semantic) ──────────────────────────
if echo "$tool_input" | grep -qi '\bsemantic\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "You used the word \"semantic.\" Be specific about what the problem is — if it is a data integrity issue, a logic error, or a tenet violation, name it directly."
        }
    }'
    exit 0
fi

# ── Category 2: Rationalization (established pattern / consistency) ──
if echo "$tool_input" | grep -qiE '\bestablished pattern\b|\bexisting pattern\b|\bfor consistency\b|\bto be consistent\b|\bconsistent with (how|what|the)\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "You appear to be citing an existing pattern to justify behavior. Existing code that contradicts a tenet represents more violations, not justification. Present findings based on what the tenet text says, not what other code does."
        }
    }'
    exit 0
fi

# ── Category 3: Self-authorization ─────────────────────────────────
if echo "$tool_input" | grep -qiE '\bI.ll go ahead\b|\bI.ll just\b|\bthe obvious (choice|approach|answer)\b|\bclearly (the )?intent\b|\bsafe to assume\b|\breasonable to (assume|proceed|conclude)\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "You appear to be making a decision without user direction. Per CLAUDE-PRACTICES.md §4 (Decision Checkpoints): if the implementation requires a behavioral choice not in the instructions, present the options and wait for direction."
        }
    }'
    exit 0
fi

# ── Category 4: Exception-invention ────────────────────────────────
if echo "$tool_input" | grep -qiE '\bin this (particular )?case\b|\btechnically (compliant|acceptable|valid)\b|\barguably\b|\bborderline\b|\bjudgment call\b|\bspirit of (the rule|the tenet)\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "You appear to be creating an exception the tenet does not define. If a tenet does not list an exception for this situation, none exists. If you believe one should exist, present the conflict to the user and wait."
        }
    }'
    exit 0
fi

# ── Category 5: Precedent-mining ───────────────────────────────────
if echo "$tool_input" | grep -qiE '\bother services (do|also|already)\b|\bexisting code (does|also|already)\b|\balready does this\b|\bfollowing the (same|existing) (approach|pattern)\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "You appear to be citing existing code as justification. Existing violations are tech debt to fix, not patterns to replicate. Present findings based on the tenet text, noting all affected services."
        }
    }'
    exit 0
fi

# ── Category 6: Minimization (general) ─────────────────────────────
if echo "$tool_input" | grep -qiE '\bminor (concern|issue|problem)\b|\btrivial\b|\bcosmetic (issue|change|fix)\b|\bstylistic (issue|concern|choice)\b|\bedge case that\b|\bnot (really )?a (big |real )?(deal|concern|problem|issue)\b|\bquality improvement\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "You appear to be minimizing a finding. If the tenet says X and the code does not-X, state the finding directly. The tenet text determines severity, not an assessment of blast radius or practical impact."
        }
    }'
    exit 0
fi

# ── Category 7: Work-avoidance rationalization ────────────────────
if echo "$tool_input" | grep -qiE '\blet me (just )?skip\b|\bis harmless\b|\bare harmless\b|\bthis is a genuine problem\b|\bI need a different approach\b|\bthis is awkward\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "You appear to be rationalizing not solving a problem. Fix the actual problem, or stop and ask for direction. A comment explaining why orphaned data or an unresolved issue is acceptable is not a fix."
        }
    }'
    exit 0
fi

# ── Category 8: Efficiency-driven corner-cutting ─────────────────
if echo "$tool_input" | grep -qiE '\b(sufficient|enough) (context|understanding|information)\b|\bI (can|already) (infer|tell from|understand)\b|\balready (understand|know what|have a good)\b|\bskip (ahead|to the|the rest)\b|\bmove on to\b.*\bwithout\b|\bfor (efficiency|brevity)\b|\bto (save time|be efficient|be more efficient)\b|\bquickly (scan|check|skim|look|read)\b|\bjust (the|read the) (header|docstring|first|opening)\b|\bgood enough\b|\bclose enough\b|\bI.ve (read|seen) enough\b|\bbased on (the header|the docstring|the name|context)\b|\bmore efficient approach\b|\bmost efficient approach\b|\bgiven the session length\b|\blet me be practical\b|\bI need a broader approach\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "You appear to be about to do less than what was asked. If you were told to read a file, read the full file. If you were told to do something for every item, apply the same thoroughness to every item. If you cannot maintain quality, stop and tell the user rather than silently degrading."
        }
    }'
    exit 0
fi

# ── Category 9: File reading avoidance ──────────────────────────
if echo "$tool_input" | grep -qiE '\btoo large to read (fully|in full|completely)\b|\bcannot be read fully\b|\bexceeded.*token limit\b.*\bskip\b|\bfile is too (big|large) to\b|\bread the full file\b.*\bnot (possible|feasible)\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "reject",
            permissionDecisionReason: "There is no such thing as a file that cannot be read fully. The Read tool supports offset and limit parameters — read the file in chunks (e.g., offset=1 limit=2000, then offset=2001 limit=2000, etc.). All files must be fully read. Do not skip or summarize content from files you have not read."
        }
    }'
    exit 0
fi

# No match — allow silently
exit 0
