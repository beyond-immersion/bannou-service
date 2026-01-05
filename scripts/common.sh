#!/bin/bash

# =============================================================================
# Common Utilities for Bannou Generation Scripts
# =============================================================================
# This file provides shared functions used across all generation scripts.
# Source this file at the beginning of each script:
#   source "$(dirname "$0")/common.sh"
#
# Available functions:
#   - to_pascal_case <string>     : Convert hyphenated-name to PascalCase
#   - find_nswag_exe              : Find NSwag executable, returns path
#   - ensure_dotnet_root          : Ensure DOTNET_ROOT environment variable is set
#   - log_info <message>          : Print blue info message
#   - log_success <message>       : Print green success message
#   - log_warn <message>          : Print yellow warning message
#   - log_error <message>         : Print red error message
# =============================================================================

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# -----------------------------------------------------------------------------
# Logging Functions
# -----------------------------------------------------------------------------

log_info() {
    echo -e "${BLUE}$1${NC}"
}

log_success() {
    echo -e "${GREEN}$1${NC}"
}

log_warn() {
    echo -e "${YELLOW}$1${NC}"
}

log_error() {
    echo -e "${RED}$1${NC}"
}

# -----------------------------------------------------------------------------
# String Conversion Functions
# -----------------------------------------------------------------------------

# Convert hyphenated names to PascalCase
# Usage: SERVICE_PASCAL=$(to_pascal_case "my-service-name")
# Output: MyServiceName
to_pascal_case() {
    local input="$1"
    echo "$input" | sed 's/-/ /g' | awk '{for(i=1;i<=NF;i++) $i=toupper(substr($i,1,1)) tolower(substr($i,2))} 1' | sed 's/ //g'
}

# Convert hyphenated names to camelCase
# Usage: SERVICE_CAMEL=$(to_camel_case "my-service-name")
# Output: myServiceName
to_camel_case() {
    local input="$1"
    local pascal=$(to_pascal_case "$input")
    echo "$(echo "${pascal:0:1}" | tr '[:upper:]' '[:lower:]')${pascal:1}"
}

# -----------------------------------------------------------------------------
# NSwag Functions
# -----------------------------------------------------------------------------

# Find NSwag executable
# Usage: NSWAG_EXE=$(find_nswag_exe)
# Returns: Path to NSwag executable, or empty string if not found
find_nswag_exe() {
    # On Linux/macOS, prefer the global dotnet tool over Windows executables
    if [[ "$OSTYPE" == "linux-gnu"* ]] || [[ "$OSTYPE" == "darwin"* ]]; then
        local nswag_global=$(which nswag 2>/dev/null)
        if [ -n "$nswag_global" ]; then
            echo "$nswag_global"
            return 0
        fi
    fi

    # Windows or fallback: try MSBuild package paths first
    local possible_paths=(
        "$HOME/.nuget/packages/nswag.msbuild/14.2.0/tools/Net90/dotnet-nswag.exe"
        "$HOME/.nuget/packages/nswag.msbuild/14.1.0/tools/Net90/dotnet-nswag.exe"
        "$HOME/.nuget/packages/nswag.msbuild/14.0.7/tools/Net90/dotnet-nswag.exe"
        "$(find $HOME/.nuget/packages/nswag.msbuild -name "dotnet-nswag.exe" 2>/dev/null | head -1)"
        "$(which nswag 2>/dev/null)"
    )

    for path in "${possible_paths[@]}"; do
        if [ -n "$path" ] && [ -f "$path" ]; then
            # On Linux, skip .exe files as they won't execute
            if [[ "$OSTYPE" == "linux-gnu"* ]] && [[ "$path" == *.exe ]]; then
                continue
            fi
            echo "$path"
            return 0
        fi
    done

    return 1
}

# Require NSwag executable - exits script if not found
# Usage: require_nswag
# Sets: NSWAG_EXE variable
require_nswag() {
    NSWAG_EXE=$(find_nswag_exe)
    if [ -z "$NSWAG_EXE" ]; then
        log_error "NSwag executable not found"
        log_error "Install NSwag with: dotnet tool install -g NSwag.ConsoleCore"
        exit 1
    fi
    log_success "Found NSwag at: $NSWAG_EXE"
}

# -----------------------------------------------------------------------------
# .NET Functions
# -----------------------------------------------------------------------------

# Ensure DOTNET_ROOT is set for NSwag global tool to work properly
# Usage: ensure_dotnet_root
# Sets: DOTNET_ROOT environment variable
ensure_dotnet_root() {
    if [ -z "$DOTNET_ROOT" ]; then
        # Try to find dotnet installation
        if [ -d "/usr/local/share/dotnet" ]; then
            export DOTNET_ROOT="/usr/local/share/dotnet"
        elif [ -d "/usr/share/dotnet" ]; then
            export DOTNET_ROOT="/usr/share/dotnet"
        elif command -v dotnet >/dev/null 2>&1; then
            # Get dotnet installation path
            DOTNET_PATH=$(dirname "$(readlink -f "$(which dotnet)")")
            export DOTNET_ROOT="$DOTNET_PATH"
        fi
    fi
}

# -----------------------------------------------------------------------------
# Schema Validation Functions
# -----------------------------------------------------------------------------

# Validate that a schema file exists
# Usage: validate_schema_file "$SCHEMA_FILE"
# Exits with error if file doesn't exist
validate_schema_file() {
    local schema_file="$1"
    if [ ! -f "$schema_file" ]; then
        log_error "Schema file not found: $schema_file"
        exit 1
    fi
}

# Validate command line arguments for service generation
# Usage: validate_service_args "$@"
# Expects at least 1 argument (service name)
validate_service_args() {
    if [ $# -lt 1 ]; then
        log_error "Usage: $0 <service-name> [schema-file]"
        echo "Example: $0 account"
        echo "Example: $0 account ../schemas/account-api.yaml"
        exit 1
    fi
}

# -----------------------------------------------------------------------------
# Directory Functions
# -----------------------------------------------------------------------------

# Get the directory where this script is located
# Usage: SCRIPT_DIR=$(get_script_dir)
get_script_dir() {
    echo "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
}

# Get the repository root directory
# Usage: REPO_ROOT=$(get_repo_root)
get_repo_root() {
    local script_dir=$(get_script_dir)
    echo "$(cd "$script_dir/.." && pwd)"
}

# -----------------------------------------------------------------------------
# Post-Processing Functions
# -----------------------------------------------------------------------------

# Wrap enum declarations with CS1591 pragma suppressions
# OpenAPI cannot document individual enum members, only the enum type itself.
# This function adds #pragma warning disable/restore CS1591 around enums.
# Usage: postprocess_enum_suppressions "$OUTPUT_FILE"
postprocess_enum_suppressions() {
    local file="$1"
    if [ ! -f "$file" ]; then
        log_error "File not found for enum suppression post-processing: $file"
        return 1
    fi

    # Use awk to wrap enums with pragma suppressions
    # Pattern: [GeneratedCode...] followed by "public enum" on next line
    awk '
    BEGIN { in_enum = 0; prev_line = ""; has_prev = 0 }
    {
        if (has_prev) {
            if (/^public enum /) {
                # Previous line was GeneratedCode, this is enum - wrap it
                print "#pragma warning disable CS1591 // Enum members cannot have XML documentation"
                print prev_line
                in_enum = 1
            } else {
                # Previous line was GeneratedCode but not followed by enum
                print prev_line
            }
            has_prev = 0
        }

        if (/^\[System\.CodeDom\.Compiler\.GeneratedCode/) {
            # Store this line and check next line
            prev_line = $0
            has_prev = 1
            next
        }

        if (in_enum && /^}$/) {
            # End of enum
            print $0
            print "#pragma warning restore CS1591"
            in_enum = 0
            next
        }

        print $0
    }
    END {
        # Handle case where file ends with GeneratedCode line (unlikely but safe)
        if (has_prev) print prev_line
    }
    ' "$file" > "${file}.tmp" && mv "${file}.tmp" "$file"
}

# Add XML documentation comments to AdditionalProperties declarations
# NSwag generates AdditionalProperties for additionalProperties: true schemas
# but doesn't add XML documentation, causing CS1591 warnings.
# Usage: postprocess_additional_properties_docs "$OUTPUT_FILE"
postprocess_additional_properties_docs() {
    local file="$1"
    if [ ! -f "$file" ]; then
        log_error "File not found for AdditionalProperties post-processing: $file"
        return 1
    fi

    # Use awk to add XML doc comment before [JsonExtensionData] AdditionalProperties
    # Pattern: [System.Text.Json.Serialization.JsonExtensionData] followed by AdditionalProperties
    # Matches regardless of leading whitespace
    awk '
    BEGIN { prev_line = ""; has_prev = 0; indent = "" }
    {
        if (has_prev) {
            if (/^[[:space:]]*public System\.Collections\.Generic\.IDictionary<string, object>\?? AdditionalProperties/) {
                # Previous line was JsonExtensionData, this is AdditionalProperties - add XML doc
                # Preserve the indentation from the current line
                match($0, /^[[:space:]]*/)
                indent = substr($0, RSTART, RLENGTH)
                print indent "/// <summary>"
                print indent "/// Gets or sets additional properties not defined in the schema."
                print indent "/// </summary>"
                print prev_line
            } else {
                # Previous line was JsonExtensionData but not followed by AdditionalProperties
                print prev_line
            }
            has_prev = 0
        }

        if (/^[[:space:]]*\[System\.Text\.Json\.Serialization\.JsonExtensionData\]/) {
            # Store this line and check next line
            prev_line = $0
            has_prev = 1
            next
        }

        print $0
    }
    END {
        # Handle case where file ends with JsonExtensionData line (unlikely but safe)
        if (has_prev) print prev_line
    }
    ' "$file" > "${file}.tmp" && mv "${file}.tmp" "$file"
}
