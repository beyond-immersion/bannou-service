#!/bin/bash
#
# block-destructive-git.sh
#
# PreToolUse hook that blocks destructive git commands that can wipe out work.
# These commands are explicitly forbidden in CLAUDE.md:
#   - git checkout (destroys uncommitted work)
#   - git stash (hides changes that may be lost)
#   - git reset (can destroy commit history)
#
# If you need to undo changes, ASK the user first and use the least
# destructive method possible (usually careful Edit tool reverts).

# Read the hook input from stdin
input=$(cat)

# Extract the command from the JSON input
command=$(echo "$input" | jq -r '.tool_input.command // ""' 2>/dev/null)

# If we couldn't parse the input or there's no command, allow it through
if [[ -z "$command" ]]; then
    exit 0
fi

# Check for git checkout (except for branch switching which is safe)
# Block: git checkout <file>, git checkout -- <file>, git checkout ., git checkout HEAD -- file
# Allow: git checkout <branch-name>, git checkout -b <branch>
if [[ "$command" =~ git[[:space:]]+checkout ]]; then
    # Allow branch operations: git checkout -b, git checkout <branchname>
    # But block file operations:
    #   - git checkout -- file
    #   - git checkout <ref> -- file (e.g., HEAD --)
    #   - git checkout .
    #   - git checkout file.ext
    if [[ "$command" =~ git[[:space:]]+checkout[[:space:]].*-- ]] || \
       [[ "$command" =~ git[[:space:]]+checkout[[:space:]]+\. ]] || \
       [[ "$command" =~ git[[:space:]]+checkout[[:space:]]+[^-][^[:space:]]*\.[a-zA-Z] ]]; then
        cat <<'ENDJSON'
{
  "decision": "block",
  "reason": "🛑 STOP! git checkout is a DESTRUCTIVE command!\n\nThis command can permanently destroy uncommitted work.\n\nPer CLAUDE.md FORBIDDEN DESTRUCTIVE COMMANDS:\n- NEVER use git checkout to undo changes\n- ASK the user first before any undo operation\n- Use careful Edit tool reverts instead\n\nIf you need to undo changes:\n1. STOP and ask the user\n2. Explain what you want to undo and why\n3. Wait for explicit approval\n4. Use Edit tool to carefully revert specific changes\n\nDo NOT proceed with this command."
}
ENDJSON
        exit 0
    fi
fi

# Block git stash
if [[ "$command" =~ git[[:space:]]+stash ]]; then
    cat <<'ENDJSON'
{
  "decision": "block",
  "reason": "🛑 STOP! git stash is a DESTRUCTIVE command!\n\nThis command hides changes that may be lost.\n\nPer CLAUDE.md FORBIDDEN DESTRUCTIVE COMMANDS:\n- NEVER use git stash\n- ASK the user first before any undo operation\n\nIf you need to set aside changes:\n1. STOP and ask the user\n2. Explain what you want to do and why\n3. Wait for explicit approval\n\nDo NOT proceed with this command."
}
ENDJSON
    exit 0
fi

# Block git reset (except --soft which is safer)
if [[ "$command" =~ git[[:space:]]+reset ]] && [[ ! "$command" =~ --soft ]]; then
    cat <<'ENDJSON'
{
  "decision": "block",
  "reason": "🛑 STOP! git reset is a DESTRUCTIVE command!\n\nThis command can destroy commit history.\n\nPer CLAUDE.md FORBIDDEN DESTRUCTIVE COMMANDS:\n- NEVER use git reset --hard or git reset without --soft\n- ASK the user first before any undo operation\n\nIf you need to undo commits:\n1. STOP and ask the user\n2. Explain what you want to undo and why\n3. Wait for explicit approval\n4. Use git revert for safe history-preserving reverts\n\nDo NOT proceed with this command."
}
ENDJSON
    exit 0
fi

# Block git restore (can overwrite files)
if [[ "$command" =~ git[[:space:]]+restore ]]; then
    cat <<'ENDJSON'
{
  "decision": "block",
  "reason": "🛑 STOP! git restore is a DESTRUCTIVE command!\n\nThis command can overwrite uncommitted changes.\n\nPer CLAUDE.md FORBIDDEN DESTRUCTIVE COMMANDS:\n- NEVER use git restore to discard changes without user approval\n- ASK the user first before any undo operation\n\nIf you need to undo file changes:\n1. STOP and ask the user\n2. Explain what you want to undo and why\n3. Wait for explicit approval\n4. Use Edit tool to carefully revert specific changes\n\nDo NOT proceed with this command."
}
ENDJSON
    exit 0
fi

# Block git clean (removes untracked files)
if [[ "$command" =~ git[[:space:]]+clean ]]; then
    cat <<'ENDJSON'
{
  "decision": "block",
  "reason": "🛑 STOP! git clean is a DESTRUCTIVE command!\n\nThis command permanently deletes untracked files.\n\nPer CLAUDE.md FORBIDDEN DESTRUCTIVE COMMANDS:\n- NEVER use git clean without explicit user approval\n- ASK the user first\n\nDo NOT proceed with this command."
}
ENDJSON
    exit 0
fi

# Command is safe, allow it through
exit 0
