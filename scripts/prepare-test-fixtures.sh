#!/bin/bash
# Prepare test fixtures by copying and renaming dot_git -> .git
# This follows the LibGit2Sharp pattern for storing git repos in source control
# See: https://github.com/libgit2/libgit2sharp/blob/master/LibGit2Sharp.Tests/TestHelpers/DirectoryHelper.cs

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
SOURCE_FIXTURES="$REPO_ROOT/http-tester/fixtures"
DEST_FIXTURES="${1:-/tmp/bannou-test-fixtures}"

# Clean destination if it exists
if [ -d "$DEST_FIXTURES" ]; then
    rm -rf "$DEST_FIXTURES"
fi

mkdir -p "$DEST_FIXTURES"

# Copy fixtures with dot_git -> .git rename
# This is the pattern used by LibGit2Sharp for storing test repos in source control
copy_with_rename() {
    local src="$1"
    local dest="$2"

    for item in "$src"/*; do
        [ -e "$item" ] || continue  # Skip if no matches

        local basename=$(basename "$item")
        local destname="$basename"

        # Apply renames following LibGit2Sharp convention
        case "$basename" in
            dot_git)
                destname=".git"
                ;;
            gitmodules)
                destname=".gitmodules"
                ;;
        esac

        if [ -d "$item" ]; then
            mkdir -p "$dest/$destname"
            copy_with_rename "$item" "$dest/$destname"
        else
            cp "$item" "$dest/$destname"
        fi
    done
}

echo "Preparing test fixtures..."
echo "  Source: $SOURCE_FIXTURES"
echo "  Destination: $DEST_FIXTURES"

# Copy each fixture directory
for fixture_dir in "$SOURCE_FIXTURES"/*/; do
    [ -d "$fixture_dir" ] || continue

    fixture_name=$(basename "$fixture_dir")
    echo "  Copying fixture: $fixture_name"

    mkdir -p "$DEST_FIXTURES/$fixture_name"
    copy_with_rename "$fixture_dir" "$DEST_FIXTURES/$fixture_name"
done

# Verify the git repo is valid
if [ -d "$DEST_FIXTURES/test-docs-repo/.git" ]; then
    echo "  Verified: test-docs-repo/.git exists"
    cd "$DEST_FIXTURES/test-docs-repo"
    git rev-parse HEAD > /dev/null 2>&1 && echo "  Verified: valid git repository ($(git rev-parse --short HEAD))"
else
    echo "  WARNING: test-docs-repo/.git not found!"
    exit 1
fi

echo "Test fixtures prepared at: $DEST_FIXTURES"
