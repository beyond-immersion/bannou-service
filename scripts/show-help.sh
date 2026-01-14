#!/bin/bash

# show-help.sh
# Displays organized help for all Makefile commands

cat << 'EOF'
🔧 Bannou Development Commands

📋 CORE DEVELOPMENT
  build                     Build all .NET projects
  clean                     Clean generated files and caches
  clean PLUGIN=name         Clean specific plugin only
  generate                  Generate all services and SDK
  generate-services         Generate service controllers/models from schemas
  generate-services PLUGIN=name  Generate specific service only
  generate-sdk              Generate client SDK from services
  sync                      Update project and submodules

🐳 DOCKER & COMPOSE
  build-compose             Build Docker containers (all services)
  build-compose-services SERVICES="auth account"  Build with specific services only
  push-dev                  Build and push development image to Docker Hub
  build-plugins SERVICES="auth account"           Build specific plugins only
  build-service-libs        Build all service plugins for Docker
  up-compose                Start services locally
  up-openresty              Start with OpenResty edge proxy
  ci-up-compose             Start with CI configuration
  elk-up-compose            Start with ELK logging stack
  down-compose              Stop and cleanup containers
  down-openresty            Stop OpenResty setup
  elk-down-compose          Stop ELK setup

🔍 SERVICE MANAGEMENT
  list-services             Show all available services for building
  validate-compose-services Show plugins in latest Docker image
  validate-compose-services SERVICES="auth account"  Validate specific services

🧪 TESTING
  test                      Run all unit tests
  test PLUGIN=name          Run tests for specific plugin only
  test-unit                 Run .NET unit tests only
  test-ci                   Complete CI pipeline (matches GitHub Actions)
  test-infrastructure       Infrastructure integration tests
  test-infrastructure-compose     Infrastructure tests in Docker
  test-infrastructure-openresty   OpenResty infrastructure tests
  test-http                 HTTP integration tests (interactive)
  test-http-daemon          HTTP integration tests (CI mode)
  test-edge                 WebSocket integration tests (interactive)
  test-edge-daemon          WebSocket integration tests (CI mode)

🧹 TEST CLEANUP
  test-cleanup              Remove leftover test containers (interactive)
  test-cleanup-dry          Show what would be removed (dry run)
  test-cleanup-force        Force remove all test containers

🔧 CODE QUALITY
  check                     Fast EditorConfig validation
  check-ci                  Full EditorConfig validation (matches CI)
  fix                       Complete code formatting (format + endings + config)
  fix-format                Run .NET formatter only
  fix-endings               Fix line endings only
  fix-config                Fix EditorConfig issues only
  validate                  Pre-push validation (check + test)

🏷️  GIT & VERSIONING
  tag msg="message"         Create and push git tag
  generate-services-for-consistency  Test service generation consistency

📖 USAGE EXAMPLES
  # Plugin-specific development
  make clean PLUGIN=auth
  make generate-services PLUGIN=auth
  make test PLUGIN=auth

  # Selective service building
  make build-compose-services SERVICES="auth account connect"
  make validate-compose-services SERVICES="auth account connect"

  # Complete development cycle
  make clean && make generate && make build && make test

  # Pre-push workflow
  make validate

  # CI reproduction
  make test-ci

💡 TIPS
  - Use PLUGIN=name for plugin-specific operations
  - Use SERVICES="name1 name2" for selective building/validation
  - Run 'make list-services' to see available services
  - Run 'make validate' before pushing to ensure quality
  - Run 'scripts/show-help-inline.sh' for a simple command list

EOF
