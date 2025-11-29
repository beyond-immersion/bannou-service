# EditorConfig and Linting Guide

## Problem

Our GitHub Actions CI uses **Super Linter with EditorConfig validation** which is **more strict** than just `dotnet format` and `make fix-endings`. This causes builds to fail on push even when local formatting appears correct.

## Root Cause

The CI runs:
```yaml
uses: github/super-linter/slim@v5
env:
  VALIDATE_EDITORCONFIG: true
  VALIDATE_CSHARP: false  # Disabled to avoid conflicts with dotnet format
```

Super Linter's EditorConfig checker enforces stricter indentation rules than `dotnet format`, particularly:
- **All indentation must be multiples of 4 spaces** (not 15, 30, etc.)
- **No mixed tabs and spaces**
- **Exact line ending compliance (LF)**
- **Final newline requirements**

## Solution: New Make Commands

### Quick Local Validation
```bash
# Fast lightweight check (finds most issues)
make lint-editorconfig-fast

# Full validation using same Docker image as CI (slower, but exact match)
make lint-editorconfig
```

### Recommended Workflow
```bash
# Enhanced formatting that ensures CI compatibility
make format-strict

# Complete pre-push validation
make validate
```

### Legacy Commands (Still Available)
```bash
# Basic formatting (may not pass CI)
make format
```

## Understanding the Issues

### Common EditorConfig Violations

1. **Non-multiple-of-4 indentation**:
   ```csharp
   return Environment.GetEnvironmentVariable("DAEMON_MODE") == "true" ||
          args.Contains("--daemon") ||  // ❌ 15 spaces (should be 16)
          args.Contains("-d");          // ❌ 15 spaces (should be 16)
   ```

2. **Mixed tabs and spaces** (invisible but breaks CI)

3. **Missing final newlines** in source files

4. **CRLF line endings** instead of LF

### Why `dotnet format` Isn't Enough

- `dotnet format` focuses on **C# code style** (braces, spacing, etc.)
- **EditorConfig validation** focuses on **text formatting** (indentation, line endings, etc.)
- Both are needed for complete compliance

## Implementation Details

### New Makefile Commands

```makefile
# EditorConfig validation using Super Linter (matches GitHub Actions exactly)
lint-editorconfig:
    docker run --rm \
        -e VALIDATE_EDITORCONFIG=true \
        -e VALIDATE_CSHARP=false \
        -v $(PWD):/tmp/lint \
        --workdir /tmp/lint \
        github/super-linter:slim-v5

# Lightweight EditorConfig checking (backup when Docker unavailable)
lint-editorconfig-fast:
    scripts/check-editorconfig.sh

# Enhanced formatting that ensures EditorConfig compliance
format-strict: fix-endings
    dotnet format
    $(MAKE) lint-editorconfig
```

### Lightweight Checker Script

`scripts/check-editorconfig.sh` performs:
- ✅ CRLF line ending detection
- ✅ Missing final newline detection
- ✅ Tab character detection in C# files
- ✅ Non-multiple-of-4 indentation detection
- ✅ Excludes build artifacts (`bin/`, `obj/`)

## Recommended Developer Workflow

### Before Every Push
```bash
make validate  # Runs format-strict + tests
```

### When You Get CI Failures
```bash
make lint-editorconfig-fast  # Quick diagnosis
make format-strict           # Fix the issues
```

### For Quick Checks During Development
```bash
make format                  # Basic formatting
make lint-editorconfig-fast  # Check for CI issues
```

## Technical Details

### GitHub Actions Configuration
```yaml
# .github/workflows/ci.lint.yml
- name: Run EditorConfig Validation
  uses: github/super-linter/slim@v5
  env:
    VALIDATE_EDITORCONFIG: true  # ✅ Strict text formatting
    VALIDATE_CSHARP: false       # ❌ Disabled (dotnet format handles this)
```

### EditorConfig Rules (`.editorconfig`)
```ini
[*.cs]
indent_size = 4        # Must be multiples of 4
indent_style = space   # No tabs
end_of_line = lf      # Unix line endings
insert_final_newline = true  # Required
```

### Why This Approach Works

1. **Exact CI Match**: `make lint-editorconfig` uses the same Docker image as GitHub Actions
2. **Fast Feedback**: `make lint-editorconfig-fast` provides quick local validation
3. **Automated Fix**: `make format-strict` fixes most issues automatically
4. **Pre-push Safety**: `make validate` ensures CI will pass
5. **Backward Compatible**: Existing `make format` still works

## Troubleshooting

### "Docker not available"
Use `make lint-editorconfig-fast` instead of `make lint-editorconfig`.

### "Still failing CI after make format-strict"
1. Check the GitHub Actions log for specific files/lines
2. Run `make lint-editorconfig` locally to see exact errors
3. Manually fix any remaining alignment issues

### "False positive indentation warnings"
The lightweight checker may show false positives for:
- Complex alignment patterns
- Multi-line strings
- Comments with special formatting

Use `make lint-editorconfig` for definitive validation.

## Summary

- **Problem**: CI uses strict EditorConfig validation beyond `dotnet format`
- **Solution**: New `make format-strict` and `make lint-editorconfig` commands
- **Workflow**: Use `make validate` before pushing
- **Diagnosis**: Use `make lint-editorconfig-fast` to find issues quickly
