#!/bin/bash
#
# block-file-moves.sh
#
# PreToolUse hook that blocks mv/rename commands on source files.
# An agent previously renamed files to .tmp instead of fixing build errors,
# which can cause data loss and confusion.
#
# RULE: NEVER move or rename code files without EXPLICIT user instruction.
# If the build is failing, FIX THE CODE, don't rename files.

# Read the hook input from stdin
input=$(cat)

# Extract the command from the JSON input
command=$(echo "$input" | jq -r '.tool_input.command // ""' 2>/dev/null)

# If we couldn't parse the input or there's no command, allow it through
if [[ -z "$command" ]]; then
    exit 0
fi

# List of code file extensions that should not be moved/renamed
code_extensions="\.cs|\.csproj|\.sln|\.yaml|\.yml|\.json|\.ts|\.tsx|\.js|\.jsx|\.py|\.go|\.rs|\.h|\.cpp|\.c|\.sh|\.md"

# Block mv commands that target code files
# Matches: mv <source> <dest>, mv -f <source> <dest>, etc.
if [[ "$command" =~ ^[[:space:]]*mv[[:space:]] ]]; then
    # Check if any argument looks like a code file
    if echo "$command" | grep -qE "($code_extensions)"; then
        cat <<'ENDJSON'
{
  "decision": "block",
  "reason": "🛑 STOP! Moving/renaming code files is FORBIDDEN without explicit user instruction!\n\nAn agent previously renamed files to .tmp instead of fixing build errors, causing confusion and potential data loss.\n\nPer CLAUDE.md FORBIDDEN DESTRUCTIVE COMMANDS:\n- mv (for code files) - Can lose files or break references\n\nIF THE BUILD IS FAILING:\n- FIX THE CODE, don't rename files\n- Debug the actual error\n- Ask the user for help if stuck\n\nIF YOU TRULY NEED TO MOVE A FILE:\n1. STOP and ask the user first\n2. Explain exactly what file and why\n3. Wait for explicit approval: 'Yes, move that file'\n4. Only then proceed\n\nDo NOT proceed with this command."
}
ENDJSON
        exit 0
    fi
fi

# Also block rename patterns via bash (cp then rm, or similar workarounds)
if [[ "$command" =~ cp[[:space:]].*\.cs.*&&.*rm ]] || \
   [[ "$command" =~ cp[[:space:]].*\.csproj.*&&.*rm ]]; then
    cat <<'ENDJSON'
{
  "decision": "block",
  "reason": "🛑 STOP! Attempting to rename code files via cp+rm workaround!\n\nThis is the same as mv and is FORBIDDEN without explicit user instruction.\n\nAsk the user first if you need to reorganize files."
}
ENDJSON
    exit 0
fi

# Command is safe, allow it through
exit 0
