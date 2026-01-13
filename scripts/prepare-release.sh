#!/bin/bash
#
# prepare-release.sh - Prepare a new platform release
#
# This script:
#   1. Validates the new version is valid semver
#   2. Updates the VERSION file
#   3. Updates CHANGELOG.md (moves [Unreleased] to new version section)
#   4. Shows what to do next
#
# Usage: ./scripts/prepare-release.sh <new-version>
# Example: ./scripts/prepare-release.sh 0.10.0
#

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Get script directory and repo root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

VERSION_FILE="$REPO_ROOT/VERSION"
CHANGELOG_FILE="$REPO_ROOT/CHANGELOG.md"

usage() {
    echo "Usage: $0 <new-version>"
    echo ""
    echo "Prepares a new platform release by:"
    echo "  - Updating VERSION file"
    echo "  - Moving CHANGELOG [Unreleased] section to new version"
    echo ""
    echo "Examples:"
    echo "  $0 0.10.0    # Minor release"
    echo "  $0 1.0.0     # Major release"
    echo "  $0 0.9.1     # Patch release"
    echo ""
    echo "After running, commit the changes and merge to master to trigger release."
    exit 1
}

# Validate semver format (simplified - major.minor.patch)
validate_semver() {
    local version=$1
    if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        echo -e "${RED}Error: Invalid version format '${version}'${NC}"
        echo "Version must be in semver format: MAJOR.MINOR.PATCH (e.g., 1.0.0)"
        exit 1
    fi
}

# Compare versions (returns 0 if v1 > v2, 1 otherwise)
version_gt() {
    local v1=$1
    local v2=$2

    # Split versions into arrays
    IFS='.' read -ra V1 <<< "$v1"
    IFS='.' read -ra V2 <<< "$v2"

    for i in 0 1 2; do
        if (( V1[i] > V2[i] )); then
            return 0
        elif (( V1[i] < V2[i] )); then
            return 1
        fi
    done
    return 1  # Equal versions
}

# Main script
main() {
    if [ $# -ne 1 ]; then
        usage
    fi

    NEW_VERSION=$1

    echo -e "${BLUE}=== Bannou Platform Release Preparation ===${NC}"
    echo ""

    # Validate version format
    validate_semver "$NEW_VERSION"

    # Read current version
    if [ ! -f "$VERSION_FILE" ]; then
        echo -e "${RED}Error: VERSION file not found at $VERSION_FILE${NC}"
        exit 1
    fi

    CURRENT_VERSION=$(cat "$VERSION_FILE" | tr -d '[:space:]')
    echo -e "Current version: ${YELLOW}${CURRENT_VERSION}${NC}"
    echo -e "New version:     ${GREEN}${NEW_VERSION}${NC}"
    echo ""

    # Check if new version is greater than current
    if [ "$NEW_VERSION" = "$CURRENT_VERSION" ]; then
        echo -e "${RED}Error: New version is the same as current version${NC}"
        exit 1
    fi

    if ! version_gt "$NEW_VERSION" "$CURRENT_VERSION"; then
        echo -e "${YELLOW}Warning: New version ${NEW_VERSION} is not greater than current ${CURRENT_VERSION}${NC}"
        read -p "Continue anyway? (y/N) " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            echo "Aborted."
            exit 1
        fi
    fi

    # Check for [Unreleased] section in CHANGELOG
    if ! grep -q "^## \[Unreleased\]" "$CHANGELOG_FILE"; then
        echo -e "${RED}Error: No [Unreleased] section found in CHANGELOG.md${NC}"
        echo "Please add an [Unreleased] section with your changes before preparing a release."
        exit 1
    fi

    # Check if [Unreleased] has content
    UNRELEASED_CONTENT=$(awk '/^## \[Unreleased\]/{found=1; next} /^## \[/{exit} /^---$/{exit} found && NF' "$CHANGELOG_FILE")
    if [ -z "$UNRELEASED_CONTENT" ]; then
        echo -e "${YELLOW}Warning: [Unreleased] section appears to be empty${NC}"
        read -p "Continue anyway? (y/N) " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            echo "Aborted."
            exit 1
        fi
    fi

    echo -e "${BLUE}Updating files...${NC}"
    echo ""

    # Update VERSION file
    echo "$NEW_VERSION" > "$VERSION_FILE"
    echo -e "  ${GREEN}✓${NC} Updated VERSION to ${NEW_VERSION}"

    # Update CHANGELOG.md
    # 1. Replace [Unreleased] header with new version and date
    # 2. Add new empty [Unreleased] section above it
    TODAY=$(date +%Y-%m-%d)

    # Find the current latest version (will become prev_ver for comparison link)
    PREV_VERSION=$(grep -oP '^\[\K[0-9]+\.[0-9]+\.[0-9]+(?=\]:)' "$CHANGELOG_FILE" | head -1)
    if [ -z "$PREV_VERSION" ]; then
        echo -e "${YELLOW}Warning: Could not find previous version for comparison link${NC}"
        PREV_VERSION="0.0.0"
    fi

    # Create temp file with updated changelog
    awk -v new_ver="$NEW_VERSION" -v prev_ver="$PREV_VERSION" -v today="$TODAY" -v repo="beyond-immersion/bannou-service" '
    /^## \[Unreleased\]/ {
        print "## [Unreleased]"
        print ""
        print "---"
        print ""
        print "## [" new_ver "] - " today
        next
    }
    # Update the [Unreleased] comparison link at the bottom
    /^\[Unreleased\]:/ {
        print "[Unreleased]: https://github.com/" repo "/compare/v" new_ver "...HEAD"
        print "[" new_ver "]: https://github.com/" repo "/compare/v" prev_ver "...v" new_ver
        next
    }
    { print }
    ' "$CHANGELOG_FILE" > "$CHANGELOG_FILE.tmp"

    mv "$CHANGELOG_FILE.tmp" "$CHANGELOG_FILE"
    echo -e "  ${GREEN}✓${NC} Updated CHANGELOG.md with version ${NEW_VERSION} dated ${TODAY}"

    echo ""
    echo -e "${GREEN}=== Release Preparation Complete ===${NC}"
    echo ""
    echo "Next steps:"
    echo -e "  1. Review the changes: ${YELLOW}git diff${NC}"
    echo -e "  2. Commit: ${YELLOW}git add VERSION CHANGELOG.md && git commit -m \"chore: prepare release v${NEW_VERSION}\"${NC}"
    echo -e "  3. Push and create PR to master"
    echo -e "  4. Merge PR to trigger automatic release"
    echo ""
    echo "Or use the Makefile shortcut after reviewing:"
    echo -e "  ${YELLOW}make release-commit VERSION=${NEW_VERSION}${NC}"
}

main "$@"
