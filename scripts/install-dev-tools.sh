#!/usr/bin/env bash
#
# install-dev-tools.sh - Install development/deployment dependencies for Bannou
#
# Usage:
#   ./scripts/install-dev-tools.sh              # Full development setup
#   ./scripts/install-dev-tools.sh --docker-only # Minimal deployment setup (Docker only)
#   sudo ./scripts/install-dev-tools.sh --docker-only # Production deployment
#
# Full development setup installs:
# - Docker and Docker Compose (if not present)
# - .NET 10 SDK (preview) + .NET 9 runtimes (for NSwag compatibility)
# - NSwag CLI for code generation
# - Python with ruamel.yaml for schema processing
# - Node.js 20 with eclint for EditorConfig formatting
#
# Docker-only setup installs:
# - Docker and Docker Compose
# - Basic utilities (curl, git, make, jq)
#
# The script detects already-installed components and skips them.

set -e

# Parse arguments
DOCKER_ONLY=false
for arg in "$@"; do
    case $arg in
        --docker-only)
            DOCKER_ONLY=true
            shift
            ;;
    esac
done

if [ "$DOCKER_ONLY" = true ]; then
    echo "Installing Bannou deployment tools (Docker only)..."
else
    echo "Installing Bannou development tools..."
fi
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Track what was installed
INSTALLED=()
SKIPPED=()

#------------------------------------------------------------------------------
# Helper functions
#------------------------------------------------------------------------------

check_command() {
    command -v "$1" &> /dev/null
}

print_status() {
    if [ "$2" = "installed" ]; then
        echo -e "${GREEN}[OK] $1 installed${NC}"
        INSTALLED+=("$1")
    elif [ "$2" = "skipped" ]; then
        echo -e "${YELLOW}[--] $1 already installed, skipping${NC}"
        SKIPPED+=("$1")
    else
        echo -e "${RED}[!!] $1 failed${NC}"
    fi
}

#------------------------------------------------------------------------------
# Core utilities (curl, git, make, jq)
#------------------------------------------------------------------------------

echo "Checking core utilities..."

NEED_UPDATE=false

for util in curl git make jq; do
    if ! check_command $util; then
        NEED_UPDATE=true
        break
    fi
done

if [ "$NEED_UPDATE" = true ]; then
    if check_command apt-get; then
        if [ "$EUID" -eq 0 ]; then
            apt-get update -qq
            apt-get install -y -qq apt-transport-https ca-certificates curl gnupg lsb-release make git jq > /dev/null
        else
            sudo apt-get update -qq
            sudo apt-get install -y -qq apt-transport-https ca-certificates curl gnupg lsb-release make git jq > /dev/null
        fi
    elif check_command yum; then
        if [ "$EUID" -eq 0 ]; then
            yum install -y curl git make jq
        else
            sudo yum install -y curl git make jq
        fi
    elif check_command brew; then
        brew install curl git make jq
    fi
fi

for util in curl git make jq; do
    if check_command $util; then
        print_status "$util" "skipped"
    fi
done

#------------------------------------------------------------------------------
# Docker and Docker Compose
#------------------------------------------------------------------------------

echo ""
echo "Checking Docker..."

if ! check_command docker; then
    echo "  Installing Docker..."
    if check_command apt-get; then
        curl -fsSL https://get.docker.com | sh
    elif check_command yum; then
        curl -fsSL https://get.docker.com | sh
    elif check_command brew; then
        brew install --cask docker
    fi
    print_status "Docker" "installed"
else
    print_status "Docker" "skipped"
fi

# Docker Compose v2 plugin
if ! docker compose version &> /dev/null 2>&1; then
    echo "  Installing Docker Compose plugin..."
    if check_command apt-get; then
        if [ "$EUID" -eq 0 ]; then
            apt-get install -y -qq docker-compose-plugin > /dev/null
        else
            sudo apt-get install -y -qq docker-compose-plugin > /dev/null
        fi
    fi
    print_status "Docker Compose" "installed"
else
    print_status "Docker Compose" "skipped"
fi

# Add current user to docker group if running with sudo
if [ -n "$SUDO_USER" ] && [ "$EUID" -eq 0 ]; then
    if ! groups "$SUDO_USER" 2>/dev/null | grep -q docker; then
        echo "  Adding $SUDO_USER to docker group..."
        usermod -aG docker "$SUDO_USER"
        echo -e "${YELLOW}  Note: Log out and back in for docker group membership to take effect${NC}"
    fi
fi

# If docker-only mode, stop here
if [ "$DOCKER_ONLY" = true ]; then
    echo ""
    echo "=============================================="
    echo "  Deployment Tools Installation Complete"
    echo "=============================================="
    echo ""
    echo "Installed versions:"
    echo "  Docker:         $(docker --version 2>/dev/null | cut -d' ' -f3 | tr -d ',')"
    echo "  Docker Compose: $(docker compose version 2>/dev/null | cut -d' ' -f4)"
    echo "  Make:           $(make --version 2>/dev/null | head -1 | cut -d' ' -f3)"
    echo "  Git:            $(git --version 2>/dev/null | cut -d' ' -f3)"
    echo "  jq:             $(jq --version 2>/dev/null)"
    echo ""
    echo "Next steps:"
    echo "  1. cp .env.example.minimal .env"
    echo "  2. Edit .env with production values"
    echo "  3. make build-compose"
    echo "  4. make up-external"
    echo ""
    exit 0
fi

#------------------------------------------------------------------------------
# Python and ruamel.yaml (development only)
#------------------------------------------------------------------------------

echo ""
echo "Checking Python dependencies..."

if ! check_command python3; then
    echo "  Installing Python3..."
    if check_command apt-get; then
        sudo apt-get install -y -qq python3 python3-pip
    elif check_command yum; then
        sudo yum install -y python3 python3-pip
    elif check_command brew; then
        brew install python3
    fi
    print_status "python3" "installed"
else
    print_status "python3" "skipped"
fi

# Check for ruamel.yaml
if ! python3 -c "import ruamel.yaml" 2>/dev/null; then
    echo "  Installing ruamel.yaml..."
    if check_command apt-get; then
        sudo apt-get install -y -qq python3-ruamel.yaml 2>/dev/null || pip3 install ruamel.yaml
    else
        pip3 install ruamel.yaml
    fi
    print_status "ruamel.yaml" "installed"
else
    print_status "ruamel.yaml" "skipped"
fi

#------------------------------------------------------------------------------
# .NET SDK (development only)
#------------------------------------------------------------------------------

echo ""
echo "Checking .NET SDK..."

# Set up .NET paths
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools"

NEED_DOTNET_10=false
NEED_DOTNET_9_RUNTIME=false
NEED_DOTNET_9_ASPNET=false

# Check .NET 10 SDK
if check_command dotnet && dotnet --list-sdks 2>/dev/null | grep -q "^10\."; then
    print_status ".NET 10 SDK" "skipped"
else
    NEED_DOTNET_10=true
fi

# Check .NET 9 runtime (needed for NSwag)
if check_command dotnet && dotnet --list-runtimes 2>/dev/null | grep -q "Microsoft.NETCore.App 9\."; then
    print_status ".NET 9 runtime" "skipped"
else
    NEED_DOTNET_9_RUNTIME=true
fi

# Check ASP.NET Core 9 runtime (needed for NSwag)
if check_command dotnet && dotnet --list-runtimes 2>/dev/null | grep -q "Microsoft.AspNetCore.App 9\."; then
    print_status "ASP.NET Core 9 runtime" "skipped"
else
    NEED_DOTNET_9_ASPNET=true
fi

# Install .NET components if needed
if [ "$NEED_DOTNET_10" = true ] || [ "$NEED_DOTNET_9_RUNTIME" = true ] || [ "$NEED_DOTNET_9_ASPNET" = true ]; then
    # Download install script if not present
    if [ ! -f "/tmp/dotnet-install.sh" ]; then
        echo "  Downloading .NET install script..."
        curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
        chmod +x /tmp/dotnet-install.sh
    fi

    if [ "$NEED_DOTNET_10" = true ]; then
        echo "  Installing .NET 10 SDK (preview)..."
        /tmp/dotnet-install.sh --channel 10.0 --quality preview
        print_status ".NET 10 SDK" "installed"
    fi

    if [ "$NEED_DOTNET_9_RUNTIME" = true ]; then
        echo "  Installing .NET 9 runtime..."
        /tmp/dotnet-install.sh --channel 9.0 --runtime dotnet
        print_status ".NET 9 runtime" "installed"
    fi

    if [ "$NEED_DOTNET_9_ASPNET" = true ]; then
        echo "  Installing ASP.NET Core 9 runtime..."
        /tmp/dotnet-install.sh --channel 9.0 --runtime aspnetcore
        print_status "ASP.NET Core 9 runtime" "installed"
    fi
fi

# Ensure PATH is set for remaining commands
export PATH="$PATH:$HOME/.dotnet:$HOME/.dotnet/tools"

#------------------------------------------------------------------------------
# NSwag CLI (development only)
#------------------------------------------------------------------------------

echo ""
echo "Checking NSwag CLI..."

NSWAG_VERSION="14.5.0"

if check_command nswag && nswag version 2>/dev/null | grep -q "$NSWAG_VERSION"; then
    print_status "NSwag $NSWAG_VERSION" "skipped"
else
    echo "  Installing NSwag $NSWAG_VERSION..."
    dotnet tool install --global NSwag.ConsoleCore --version "$NSWAG_VERSION" 2>/dev/null || \
    dotnet tool update --global NSwag.ConsoleCore --version "$NSWAG_VERSION"
    print_status "NSwag $NSWAG_VERSION" "installed"
fi

#------------------------------------------------------------------------------
# Node.js and eclint (development only)
#------------------------------------------------------------------------------

echo ""
echo "Checking Node.js and eclint..."

if ! check_command node || [ "$(node -v | cut -d'v' -f2 | cut -d'.' -f1)" -lt 18 ]; then
    echo "  Installing Node.js 20..."
    if check_command apt-get; then
        curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
        sudo apt-get install -y -qq nodejs
    elif check_command yum; then
        curl -fsSL https://rpm.nodesource.com/setup_20.x | sudo bash -
        sudo yum install -y nodejs
    elif check_command brew; then
        brew install node@20
    fi
    print_status "Node.js 20" "installed"
else
    print_status "Node.js" "skipped"
fi

if ! check_command eclint; then
    echo "  Installing eclint..."
    sudo npm install -g eclint 2>/dev/null || npm install -g eclint
    print_status "eclint" "installed"
else
    print_status "eclint" "skipped"
fi

#------------------------------------------------------------------------------
# Shell configuration (development only)
#------------------------------------------------------------------------------

echo ""
echo "Checking shell configuration..."

SHELL_RC=""
if [ -f "$HOME/.bashrc" ]; then
    SHELL_RC="$HOME/.bashrc"
elif [ -f "$HOME/.zshrc" ]; then
    SHELL_RC="$HOME/.zshrc"
fi

if [ -n "$SHELL_RC" ]; then
    # Add .NET to PATH if not already present
    if ! grep -q "DOTNET_ROOT" "$SHELL_RC" 2>/dev/null; then
        echo "" >> "$SHELL_RC"
        echo "# .NET SDK" >> "$SHELL_RC"
        echo 'export DOTNET_ROOT="$HOME/.dotnet"' >> "$SHELL_RC"
        echo 'export PATH="$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools"' >> "$SHELL_RC"
        print_status "Shell PATH config" "installed"
    else
        print_status "Shell PATH config" "skipped"
    fi
fi

#------------------------------------------------------------------------------
# Summary
#------------------------------------------------------------------------------

echo ""
echo "=============================================="
echo "  Development Tools Installation Complete"
echo "=============================================="

if [ ${#INSTALLED[@]} -gt 0 ]; then
    echo -e "${GREEN}Installed:${NC} ${INSTALLED[*]}"
fi

if [ ${#SKIPPED[@]} -gt 0 ]; then
    echo -e "${YELLOW}Already present:${NC} ${SKIPPED[*]}"
fi

echo ""
echo -e "${GREEN}Development tools ready!${NC}"
echo ""
echo "Next steps:"
echo "  1. Restart your shell or run: source ~/.bashrc"
echo "  2. Run: make quick"
echo ""
