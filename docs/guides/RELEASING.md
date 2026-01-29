# Release Process Guide

This guide documents the versioning and release process for Bannou.

## Overview

Bannou uses [Semantic Versioning](https://semver.org/) with two separate version tracks:

| Track | Version File | Tag Format | Published To |
|-------|--------------|------------|--------------|
| **Platform** | `VERSION` | `v{major}.{minor}.{patch}` | GitHub Releases |
| **.NET SDKs** | `sdks/SDK_VERSION` | `sdk-v{major}.{minor}.{patch}` | NuGet.org |
| **TypeScript SDK** | `sdks/typescript/package.json` | (with .NET SDKs) | npm |
| **Unreal SDK** | (generated headers) | N/A | Manual copy to UE project |

## Quick Reference

```bash
# Check current versions
make version

# Check release status (tags, pending changes)
make release-status

# Prepare a new platform release
make prepare-release VERSION=0.10.0

# Review changes
git diff

# Commit the release preparation
make release-commit VERSION=0.10.0

# Push and create PR to master
git push origin HEAD
```

## Platform Release Process

### Step 1: Prepare the Release

Run the prepare-release command with your target version:

```bash
make prepare-release VERSION=0.10.0
```

This script:
1. Validates the version format (must be `X.Y.Z`)
2. Validates the version is greater than current
3. Updates the `VERSION` file
4. Updates `CHANGELOG.md`:
   - Adds date to `[Unreleased]` section, converting it to `[0.10.0] - YYYY-MM-DD`
   - Creates a new empty `[Unreleased]` section above it
   - Updates comparison links at the bottom

### Step 2: Review Changes

Always review the changes before committing:

```bash
git diff
```

Verify:
- `VERSION` contains the correct new version
- `CHANGELOG.md` has the correct date and version header
- All intended changes are in the release notes section

### Step 3: Commit and Push

```bash
make release-commit VERSION=0.10.0
git push origin HEAD
```

Or manually:

```bash
git add VERSION CHANGELOG.md
git commit -m "chore: prepare release v0.10.0"
git push origin HEAD
```

### Step 4: Create Pull Request

Create a pull request to `master` with your release preparation commit.

### Step 5: Merge to Trigger Release

When the PR is merged to `master`, the CI workflow automatically:
1. Detects the `VERSION` file change
2. Creates a git tag `v{VERSION}`
3. Extracts release notes from `CHANGELOG.md`
4. Creates a GitHub Release with the notes
5. Sends Discord notification (if configured)

## Version Numbering Guidelines

Follow semantic versioning:

| Change Type | Version Bump | Example |
|-------------|--------------|---------|
| **Breaking changes** | Major | 0.9.0 → 1.0.0 |
| **New features** (backward compatible) | Minor | 0.9.0 → 0.10.0 |
| **Bug fixes** (backward compatible) | Patch | 0.9.0 → 0.9.1 |

### Breaking Changes Include:
- Removing or renaming API endpoints
- Changing request/response schemas in incompatible ways
- Removing configuration options
- Changing default behaviors significantly

### New Features Include:
- New API endpoints
- New optional parameters
- New services or plugins
- Performance improvements

### Bug Fixes Include:
- Fixing incorrect behavior
- Security patches
- Documentation corrections

## Maintaining the Changelog

The `CHANGELOG.md` follows [Keep a Changelog](https://keepachangelog.com/) format.

### During Development

Add entries to the `[Unreleased]` section as you make changes:

```markdown
## [Unreleased]

### Added
- New matchmaking queue types

### Changed
- Improved connection retry logic

### Fixed
- Race condition in session cleanup
```

### Categories

Use these categories in order:
- **Added** - New features
- **Changed** - Changes to existing functionality
- **Deprecated** - Features that will be removed
- **Removed** - Removed features
- **Fixed** - Bug fixes
- **Security** - Security-related changes

## SDK Releases

SDK releases follow a different process using PR labels. See `.github/workflows/ci.sdk-release.yml`.

### .NET SDK Release Workflow

The workflow publishes 20+ .NET packages in dependency order:

1. **Foundation packages** (build first): `bannou-core`, `bannou-protocol`
2. **Infrastructure**: `bannou-transport`, `bannou-bundle-format`
3. **Runtime packages**: `bannou-server`, `bannou-client`, `bannou-client-voice`
4. **Tool packages**: `music-theory`, `music-storyteller`, scene-composer variants, asset-bundler variants, asset-loader variants

**Process:**

1. Create PRs with appropriate labels:
   - `sdk:major` - Breaking changes
   - `sdk:minor` - New features
   - `sdk:patch` - Bug fixes
   - `sdk:none` - No SDK impact

2. Manually trigger the SDK release workflow in GitHub Actions

3. The workflow:
   - Analyzes PR labels since last release
   - Calculates new version
   - Builds and publishes to NuGet (14 packable projects in dependency order)
   - Creates git tag and GitHub release

### TypeScript SDK

The TypeScript SDK at `sdks/typescript/` is generated from OpenAPI schemas:

```bash
make generate-sdk-ts    # Generate types from schemas
make build-sdk-ts       # Build the package
make test-sdk-ts        # Run parity tests
```

TypeScript SDK versioning is coordinated with .NET SDKs but published to npm separately.

### Unreal SDK

The Unreal SDK at `sdks/unreal/` generates C++ headers:

```bash
make generate-unreal-sdk   # Generate headers from schemas
```

Headers are copied manually into Unreal projects (not published to a package manager).

## Manual Release (Emergency)

If you need to create a release manually without the workflow:

```bash
# Update VERSION file
echo "0.10.0" > VERSION

# Create and push tag
git tag -a "v0.10.0" -m "Release 0.10.0"
git push origin "v0.10.0"

# Create GitHub release via CLI
gh release create "v0.10.0" --title "v0.10.0" --notes "Release notes here"
```

Or trigger the workflow manually with `force=true` if the tag already exists.

## Troubleshooting

### "Tag already exists" Error

If the workflow fails because the tag already exists:
1. Run the workflow manually with `force=true` to re-release
2. Or bump to the next patch version

### Empty Release Notes

If release notes are empty:
1. Ensure `CHANGELOG.md` has content in the version section
2. The workflow extracts content between `## [VERSION]` and the next `## [` or `---`

### Workflow Not Triggering

The release workflow triggers when:
- `VERSION` file changes on `master` branch
- Or manually dispatched

Check that:
1. The PR was actually merged to `master`
2. The `VERSION` file was included in the merge
3. GitHub Actions are enabled for the repository

## File Locations

| File | Purpose |
|------|---------|
| `VERSION` | Platform version (single line) |
| `sdks/SDK_VERSION` | SDK version (single line) |
| `CHANGELOG.md` | Release notes history |
| `.github/workflows/ci.release.yml` | Platform release automation |
| `.github/workflows/ci.sdk-release.yml` | SDK release automation |
| `scripts/prepare-release.sh` | Release preparation script |
