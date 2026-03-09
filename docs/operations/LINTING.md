# EditorConfig and Linting Guide

> **Last Updated**: 2026-03-08
> **Scope**: EditorConfig validation, linting commands, and CI compliance for all project files

## Summary

EditorConfig validation and linting procedures for ensuring CI compliance across all project files. Covers the relationship between dotnet format (C# code style) and editorconfig-checker (text formatting: indentation, line endings, final newlines), available Makefile commands for local validation and fixing, and troubleshooting CI failures. Required reading when CI lint checks fail or before pushing changes that touch formatting.

## Problem

GitHub Actions CI uses **editorconfig-checker** (`ec`) for EditorConfig validation, which is **more strict** than just `dotnet format` and `make fix-endings`. This causes builds to fail on push even when local formatting appears correct.

## Root Cause

The CI runs editorconfig-checker directly (see `.github/workflows/ci.lint.yml`):

```yaml
- name: Install editorconfig-checker
  run: |
    EC_VERSION="3.3.0"
    curl -fsSL "https://github.com/editorconfig-checker/editorconfig-checker/releases/download/v${EC_VERSION}/ec-linux-amd64.tar.gz" | tar xz
    sudo mv bin/ec-linux-amd64 /usr/local/bin/ec

- name: Run EditorConfig Validation
  run: ec
```

EditorConfig validation enforces stricter rules than `dotnet format`, particularly:
- **All indentation must be multiples of 4 spaces** (not 15, 30, etc.)
- **No mixed tabs and spaces**
- **Exact line ending compliance (LF)**
- **Final newline requirements**

## Available Makefile Commands

### Validation Commands

```bash
# Lightweight check using scripts/check-editorconfig.sh (fast, finds most issues)
make check

# Full validation using editorconfig-checker (matches CI exactly)
make lint-editorconfig

# Alias for lint-editorconfig (CI-matching validation)
make check-ci
```

### Fixing Commands

```bash
# Complete formatting: dotnet format + fix-endings + fix-config + TypeScript SDK
make fix

# Alias for fix
make format

# Fix line endings and final newlines only
make fix-endings

# Comprehensive EditorConfig fixing using eclint
make fix-config

# dotnet format only (C# code style)
make fix-format
```

### Pre-Push Validation

```bash
# Comprehensive pre-push validation (check + unit tests)
make validate
```

## Recommended Developer Workflow

### Before Every Push
```bash
make validate  # Runs check + tests
```

### When You Get CI Failures
```bash
make check             # Quick diagnosis
make fix               # Fix the issues
make lint-editorconfig  # Verify fix matches CI
```

### For Quick Checks During Development
```bash
make format  # Basic formatting (alias for make fix)
make check   # Lightweight EditorConfig check
```

## Understanding the Issues

### Common EditorConfig Violations

1. **Non-multiple-of-4 indentation**:
   ```csharp
   return Environment.GetEnvironmentVariable("DAEMON_MODE") == "true" ||
          args.Contains("--daemon") ||  // 15 spaces (should be 16)
          args.Contains("-d");          // 15 spaces (should be 16)
   ```

2. **Mixed tabs and spaces** (invisible but breaks CI)

3. **Missing final newlines** in source files

4. **CRLF line endings** instead of LF

### Why `dotnet format` Is Not Enough

- `dotnet format` focuses on **C# code style** (braces, spacing, etc.)
- **EditorConfig validation** focuses on **text formatting** (indentation, line endings, etc.)
- Both are needed for complete compliance

## EditorConfig Rules

The `.editorconfig` file in the repository root defines the rules:
```ini
[*.cs]
indent_size = 4        # Must be multiples of 4
indent_style = space   # No tabs
end_of_line = lf       # Unix line endings
insert_final_newline = true  # Required
```

## Troubleshooting

### "ec not found"
Install editorconfig-checker from https://github.com/editorconfig-checker/editorconfig-checker, or use `make check` for the lightweight script-based alternative.

### "Still failing CI after make fix"
1. Check the GitHub Actions log for specific files and lines
2. Run `make lint-editorconfig` locally to see exact errors
3. Manually fix any remaining alignment issues

### "False positive indentation warnings"
The lightweight checker (`make check`) may show false positives for:
- Complex alignment patterns
- Multi-line strings
- Comments with special formatting

Use `make lint-editorconfig` for definitive validation.
