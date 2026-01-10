#!/bin/bash
#
# validate-senryu-commit.sh
#
# PreToolUse hook that validates git commit messages start with a senryu.
# A senryu is a 5-7-5 syllable poem about human nature, formatted as:
#   "five syllables here / seven syllables go here / five more syllables"
#
# The hook reads the Bash command from stdin (JSON format) and checks if it's
# a git commit. If so, it validates the first line has the senryu format.

# Read the hook input from stdin
input=$(cat)

# Extract the command from the JSON input
command=$(echo "$input" | jq -r '.tool_input.command // ""' 2>/dev/null)

# If we couldn't parse the input or there's no command, allow it through
if [[ -z "$command" ]]; then
    exit 0
fi

# Check if this is a git commit command
if [[ ! "$command" =~ git[[:space:]]+commit ]]; then
    exit 0  # Not a commit, allow it through
fi

# Check if it's an amend without a new message (reusing previous message)
if [[ "$command" =~ --amend ]] && [[ ! "$command" =~ -m ]] && [[ ! "$command" =~ --message ]] && [[ ! "$command" =~ -F ]] && [[ ! "$command" =~ --file ]]; then
    exit 0  # Amending with previous message, allow it
fi

# Check if this is a merge commit (auto-generated message)
if [[ "$command" =~ --no-edit ]]; then
    exit 0  # Merge commits with auto-message, allow it
fi

# Try to extract the commit message
# Handle various formats: -m "msg", -m 'msg', --message="msg", heredoc, etc.
message=""

# Check for heredoc format: git commit -m "$(cat <<'EOF' ... EOF)"
if [[ "$command" =~ cat[[:space:]]+\<\<[[:space:]]*[\'\"]?EOF[\'\"]? ]]; then
    # Extract content between the heredoc markers
    message=$(echo "$command" | sed -n "/cat <<[[:space:]]*['\"]\\?EOF['\"]\\?/,/^EOF/p" | sed '1d;$d')
fi

# Check for simple -m "message" or -m 'message' format
if [[ -z "$message" ]]; then
    if [[ "$command" =~ -m[[:space:]]+\"([^\"]+)\" ]]; then
        message="${BASH_REMATCH[1]}"
    elif [[ "$command" =~ -m[[:space:]]+\'([^\']+)\' ]]; then
        message="${BASH_REMATCH[1]}"
    elif [[ "$command" =~ --message=\"([^\"]+)\" ]]; then
        message="${BASH_REMATCH[1]}"
    elif [[ "$command" =~ --message=\'([^\']+)\' ]]; then
        message="${BASH_REMATCH[1]}"
    fi
fi

# If we couldn't extract a message, allow it (might be interactive or -F file)
if [[ -z "$message" ]]; then
    exit 0
fi

# Get the first line of the message
first_line=$(echo "$message" | head -n 1)

# Validate senryu format: should have exactly 2 " / " separators
slash_count=$(echo "$first_line" | grep -o ' / ' | wc -l)

if [[ "$slash_count" -ne 2 ]]; then
    # Output JSON to block the commit with a helpful message
    cat <<'ENDJSON'
{
  "decision": "block",
  "reason": "Commit message must start with a senryu!\n\nFormat: \"five syllables here / seven syllables go here / five more syllables\"\n\nTheme: human nature - coding struggles, debugging joy, refactoring satisfaction, etc.\n\nExamples:\n- bugs hide in plain sight / we debug with tired eyes / coffee saves us all\n- one small change they said / now the whole system is down / hubris strikes again\n- tests were passing green / then I touched one single line / red across the board\n\nPlease retry with a senryu as the first line of your commit message."
}
ENDJSON
    exit 0
fi

# Senryu format looks valid, allow the commit
exit 0
