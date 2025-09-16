# =============================================================================
# BANNOU DEVELOPMENT MAKEFILE
# =============================================================================
# Schema-driven microservices platform with WebSocket-first edge gateway
# Run 'make help' or 'make' to see all available commands
# =============================================================================

# Default target - show help
.DEFAULT_GOAL := help

help: ## Show this help message
	@scripts/show-help.sh

all: ## Complete development cycle - clean, generate, format, build, test, docker build, infrastructure test
	@echo "🚀 Running complete development cycle..."
	@$(MAKE) clean
	@$(MAKE) generate
	@$(MAKE) fix
	@$(MAKE) build
	@$(MAKE) test
	@$(MAKE) build-compose
	@$(MAKE) test-infrastructure-openresty
	@echo "✅ Complete development cycle finished successfully"

# =============================================================================
# ENVIRONMENT MANAGEMENT
# =============================================================================
# Standards for environment-specific Docker Compose configurations:
# - docker-compose.yml: Base services (bannou, databases, core infrastructure)
# - docker-compose.local.yml: Local development overrides (env files, local dapr components)
# - docker-compose.ci.yml: CI/CD environment (CI Dapr components, test configurations)
# - docker-compose.ingress.yml: OpenResty edge proxy + routing infrastructure
# - docker-compose.ingress.local.yml: Local ingress overrides (volumes, certificates)
# - docker-compose.elk.yml: Elasticsearch + Kibana logging stack
#
# Environment Patterns:
# - Local Dev: base + local + dev + [feature-specific]
# - Local Test: base + local + test-local + [feature-specific]
# - CI/CD: base + ci + [feature-specific]
# - Production: base + production + [region-specific] + [feature-specific]
# =============================================================================

build: ## Build all .NET projects
	dotnet build

build-compose: ## Build Docker containers (all services)
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml --project-name cl build

up-compose: ## Start services locally
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml --project-name cl up -d

up-openresty: ## Start with OpenResty edge proxy
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.ingress.yml -f provisioning/docker-compose.ingress.local.yml --project-name cl up -d

ci-up-compose: ## Start with CI configuration
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.ci.yml -f provisioning/docker-compose.ingress.yml --project-name cl up -d

elk-up-compose: ## Start with ELK logging stack
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.elk.yml --project-name cl up -d

down-compose: ## Stop and cleanup containers
	docker compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml --project-name cl down --remove-orphans

down-openresty: ## Stop OpenResty setup
	docker compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.ingress.yml -f provisioning/docker-compose.ingress.local.yml --project-name cl down --remove-orphans

elk-down-compose: ## Stop ELK setup
	docker compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.elk.yml --project-name cl down --remove-orphans

clean: ## Clean generated files and caches (add PLUGIN=name for specific plugin)
	@if [ "$(PLUGIN)" ]; then \
		echo "🧹 Cleaning plugin: $(PLUGIN)..."; \
		if [ -d "./lib-$(PLUGIN)/Generated" ]; then \
			rm -rf "./lib-$(PLUGIN)/Generated"; \
			echo "  Removed lib-$(PLUGIN)/Generated"; \
		else \
			echo "  No Generated directory found for lib-$(PLUGIN)"; \
		fi; \
		echo "✅ Clean completed for plugin: $(PLUGIN)"; \
	else \
		echo "🧹 Cleaning all generated files..."; \
		find . -path "./lib-*/Generated" -type d -exec rm -rf {} + 2>/dev/null || true; \
		rm -rf Bannou.Client.SDK 2>/dev/null || true; \
		echo "🧹 Cleaning caches and resources..."; \
		git submodule foreach --recursive git clean -fdx && docker container prune -f && docker image prune -f && docker volume prune -f && dotnet clean; \
		echo "✅ Clean completed"; \
	fi

build-service-libs: ## Build all service plugins for Docker
	@echo "🔧 Building service plugins for docker container"
	bash scripts/build-service-libs.sh
	@echo "✅ Service plugins built for inclusion in docker container"

build-plugins: ## Build specific plugins only (requires SERVICES="name1 name2")
	@if [ "$(SERVICES)" ]; then \
		echo "🔧 Building specific plugins: $(SERVICES)"; \
		bash scripts/build-service-libs.sh $(SERVICES); \
		echo "✅ Service plugins built: $(SERVICES)"; \
	else \
		echo "❌ Error: SERVICES parameter required. Example: make build-plugins SERVICES=\"auth accounts\""; \
		exit 1; \
	fi

# Build Docker image with specific services only
# Usage: make build-compose-services SERVICES="auth accounts connect"
build-compose-services:
	@if [ "$(SERVICES)" ]; then \
		echo "🐳 Building Docker image with specific services: $(SERVICES)"; \
		if [ ! -f .env ]; then touch .env; fi; \
		docker compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml --project-name cl build --build-arg BANNOU_SERVICES="$(SERVICES)"; \
		echo "✅ Docker image built with services: $(SERVICES)"; \
	else \
		echo "❌ Error: SERVICES parameter required. Example: make build-compose-services SERVICES=\"auth accounts\""; \
		exit 1; \
	fi

# Show available services that can be built
list-services:
	@scripts/list-services.sh

# Validate that specific services are included in the latest Docker image
# Usage: make validate-compose-services SERVICES="auth accounts connect"
validate-compose-services:
	@scripts/validate-compose-services.sh $(SERVICES)

# Regenerate all services and SDK
generate:
	@echo "🔧 Generating everything that can be generated: projects, service files, client SDK"
	scripts/generate-all-services.sh
	scripts/generate-client-sdk.sh
	@echo "✅ All generations completed"

# Regenerate all plugins/types but service implementations from schema
generate-services:
	@if [ "$(PLUGIN)" ]; then \
		echo "🔧 Generating plugin: $(PLUGIN)..."; \
		scripts/generate-all-services.sh $(PLUGIN); \
		echo "✅ Service generation completed for plugin: $(PLUGIN)"; \
	else \
		echo "🔧 Generating all services (NSwag + Roslyn)..."; \
		scripts/generate-all-services.sh; \
		echo "✅ Service generation completed"; \
	fi

# Generate Client SDK from generated services
generate-sdk:
	@echo "🔧 Generating Bannou Client SDK..."
	scripts/generate-client-sdk.sh
	@echo "✅ Client SDK generation completed"

# Fast EditorConfig checking (recommended for development)
check:
	@echo "🔧 Running lightweight EditorConfig checks..."
	@echo "💡 For comprehensive validation, use 'make check-ci'"
	@scripts/check-editorconfig.sh
	@echo "✅ Lightweight EditorConfig checks complete"

# EditorConfig validation using Super Linter (matches GitHub Actions exactly, optimized for speed)
check-ci:
	@echo "🔧 Running EditorConfig validation using Super Linter..."
	@echo "📋 This matches the exact validation used in GitHub Actions CI"
	@echo "⚡ Optimized: Only EditorConfig validation enabled for faster execution"
	@docker run --rm \
		-e RUN_LOCAL=true \
		-e USE_FIND_ALGORITHM=true \
		-e VALIDATE_EDITORCONFIG=true \
		-v $(PWD):/tmp/lint \
		ghcr.io/super-linter/super-linter:slim-v5 \
		|| (echo "❌ EditorConfig validation failed. Run 'make fix-config' to fix." && exit 1)
	@echo "✅ EditorConfig validation passed"

# Fix line endings and final newlines for all project files
fix-endings:
	@echo "🔧 Fixing line endings for all project files..."
	scripts/fix-endings.sh
	@echo "✅ Line endings fixed"

# Comprehensive EditorConfig fixing using eclint
fix-config:
	@scripts/fix-config.sh

# Typical dotnet format
fix-format:
	@echo "🔧 Running .NET format..."
	dotnet format
	@echo "✅ .NET formatting completed"

# Enhanced formatting that ensures EditorConfig compliance
fix:
	@echo "🔧 Running complete code formatting..."
	@$(MAKE) fix-format
	@$(MAKE) fix-endings
	@$(MAKE) fix-config
	@echo "✅ All formatting tasks complete"

# Pre-push validation (recommended workflow)
validate:
	@echo "🔧 Running comprehensive pre-push validation..."
	@$(MAKE) check
	@$(MAKE) test > /dev/null
	@echo "✅ All validation passed - safe to push!"

# Bring project and all submodules up to latest
sync:
	@echo "🔧 Syncing project and modules..."
	git pull && git submodule update --init --recursive
	@echo "✅ Project and module syncing complete"

# Service generation consistency check
generate-services-diff:
	@echo "🔍 Testing service generation consistency..."
	$(MAKE) generate-services
	git diff --exit-code || (echo "❌ Service generation created changes" && exit 1)
	@echo "✅ Service generation is consistent"

# Comprehensive unit testing - all service test projects
# Usage: make test [PLUGIN=plugin-name] - if PLUGIN is specified, only tests that plugin
test:
	@scripts/run-tests.sh $(PLUGIN)

# .NET unit testing (matches CI workflow)
test-unit:
	@echo "🧪 Running .NET unit tests..."
	dotnet test
	@echo "✅ .NET unit tests completed"

# Infrastructure integration testing
test-infrastructure:
	@echo "🚀 Running infrastructure integration tests"
	bash scripts/infrastructure-tests.sh
	@echo "✅ Infrastructure integration tests completed"

# Infrastructure integration testing (matches CI workflow)
# Uses minimal service configuration (TESTING service only) to reduce dependencies
test-infrastructure-openresty:
	@echo "🚀 Running OpenResty infrastructure integration tests (TESTING service only)..."
	if [ ! -f .env ]; then touch .env; fi
	@echo "🔧 Building Docker image with TESTING service only..."
	docker compose --env-file .env -p bannou-tests -f "./provisioning/docker-compose.yml" -f "./provisioning/docker-compose.local.yml" -f "./provisioning/docker-compose.ci.yml" -f "./provisioning/docker-compose.ingress.yml" -f "./provisioning/docker-compose.infrastructure.yml" build --build-arg BANNOU_SERVICES="testing"
	@echo "🚀 Starting infrastructure tests..."
	docker compose --env-file .env -p bannou-tests -f "./provisioning/docker-compose.yml" -f "./provisioning/docker-compose.local.yml" -f "./provisioning/docker-compose.ci.yml" -f "./provisioning/docker-compose.ingress.yml" -f "./provisioning/docker-compose.infrastructure.yml" up --exit-code-from=bannou-tester
	@echo "✅ OpenResty infrastructure integration tests completed"

# HTTP integration testing
test-http:
	@echo "🧪 Running HTTP integration tests..."
	dotnet run --project http-tester
	@echo "✅ HTTP integration tests completed"

# HTTP integration testing with daemon mode (matches CI workflow)
test-http-daemon:
	@echo "🧪 Running HTTP integration tests (daemon mode)..."
	DAEMON_MODE=true dotnet run --project http-tester --configuration Release
	@echo "✅ HTTP integration tests (daemon mode) completed"

# WebSocket/edge integration testing
test-edge:
	@echo "🧪 Running WebSocket/Edge integration tests..."
	dotnet run --project edge-tester
	@echo "✅ WebSocket/edge integration tests completed"

# WebSocket/edge testing with daemon mode (matches CI workflow)
test-edge-daemon:
	@echo "🧪 Running WebSocket protocol tests (daemon mode)..."
	DAEMON_MODE=true dotnet run --project edge-tester --configuration Release
	@echo "✅ WebSocket/edge integration tests (daemon mode) completed"

tagname := $(shell date -u +%FT%H-%M-%SZ)
tag:
	git tag $(tagname) -a -m '$(msg)'
	git push origin $(tagname)
