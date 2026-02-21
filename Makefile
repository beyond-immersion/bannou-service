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

list: ## List all target names (pipe to grep: make list | grep -i 'test')
	@grep -E '^[a-zA-Z0-9_-]+:' Makefile | cut -d: -f1 | sort

all: ## Complete development cycle - clean, generate, format, build, test, docker build, infrastructure test
	@echo "🚀 Running complete development cycle..."
	@$(MAKE) clean
	@$(MAKE) generate
	@$(MAKE) fix
	@$(MAKE) build
	@$(MAKE) test
	@$(MAKE) test-infrastructure
	@$(MAKE) test-http
	@$(MAKE) test-edge
	@echo "✅ Complete development cycle finished successfully"

quick: ## Quick development cycle - clean, generate, fix, build, unit tests (no Docker)
	@echo "🚀 Running quick development cycle (no Docker tests)..."
	@$(MAKE) clean
	@$(MAKE) generate
	@$(MAKE) fix
	@$(MAKE) build
	@$(MAKE) test-unit
	@echo "✅ Quick development cycle finished successfully"

# =============================================================================
# ENVIRONMENT MANAGEMENT
# =============================================================================
# Standards for environment-specific Docker Compose configurations:
# - docker-compose.yml: Base services (bannou, databases, core infrastructure)
# - docker-compose.local.yml: Local development overrides (env files, local components)
# - docker-compose.ci.yml: CI/CD environment (test configurations)
# - docker-compose.ingress.yml: OpenResty edge proxy + routing infrastructure
# - docker-compose.ingress.local.yml: Local ingress overrides (volumes, certificates)
# - docker-compose.elk.yml: Elasticsearch + Kibana logging stack
#
# Environment Patterns:
# - Local Dev: base + local + dev + [feature-specific]
# - Local Dev (Host): base + local + host + [feature-specific]
# - Local Test: base + local + test-local + [feature-specific]
# - Local Test (Host): base + local + test-local + host + [feature-specific]
# - CI/CD: base + ci + [feature-specific]
# - Production: base + production + [region-specific] + [feature-specific]
# =============================================================================

build: ## Build all .NET projects
	dotnet build

build-sdks: ## Build all SDK projects (separate from server)
	@echo "🔧 Building SDK projects..."
	dotnet build sdks/bannou-sdks.sln
	@echo "✅ SDK build completed"

build-tools: ## Build all development tools (http-tester, edge-tester, bannou-inspect)
	@echo "🔧 Building development tools..."
	dotnet build tools/tools.sln
	@echo "✅ Development tools built"

# =============================================================================
# ASSEMBLY INSPECTOR
# =============================================================================
# IntelliSense-like type/method inspection from the command line.
# Useful for understanding external APIs without leaving the terminal.
# =============================================================================

inspect-type: ## Inspect a type. Usage: make inspect-type TYPE="IChannel" PKG="RabbitMQ.Client"
	@dotnet run --project tools/bannou-inspect -- type "$(TYPE)" --package "$(PKG)"

inspect-method: ## Inspect a method. Usage: make inspect-method METHOD="IChannel.BasicPublishAsync" PKG="RabbitMQ.Client"
	@dotnet run --project tools/bannou-inspect -- method "$(METHOD)" --package "$(PKG)"

inspect-constructor: ## Inspect constructors. Usage: make inspect-constructor TYPE="ConnectionFactory" PKG="RabbitMQ.Client"
	@dotnet run --project tools/bannou-inspect -- constructor "$(TYPE)" --package "$(PKG)"

inspect-search: ## Search for types. Usage: make inspect-search PATTERN="*Connection*" PKG="RabbitMQ.Client"
	@dotnet run --project tools/bannou-inspect -- search "$(PATTERN)" --package "$(PKG)"

inspect-list: ## List all types in a package. Usage: make inspect-list PKG="RabbitMQ.Client"
	@dotnet run --project tools/bannou-inspect -- list-types --package "$(PKG)"

# =============================================================================
# MODEL SHAPE INSPECTOR
# =============================================================================
# Print compact model shapes (~6x smaller than schemas or generated C# code).
# Useful for understanding all models for a service without loading full files.
# =============================================================================

print-models: ## Print compact model shapes. Usage: make print-models PLUGIN="character"
	@python3 scripts/print-model-shapes.py "$(PLUGIN)"

build-compose: ## Build Docker containers (all services)
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
		--project-name bannou build

push-dev: build-compose ## Build and push development image to Docker Hub
	@echo "🏷️  Tagging bannou:latest as beyondimmersion/bannou-service:development"
	docker tag bannou:latest beyondimmersion/bannou-service:development
	@echo "🚀 Pushing beyondimmersion/bannou-service:development"
	docker push beyondimmersion/bannou-service:development
	@echo "✅ Push complete"

# =============================================================================
# ⛔ PRODUCTION DEPLOYMENT - AI AGENTS MUST NEVER RUN THIS COMMAND ⛔
# =============================================================================
# This command promotes the development image to production. It:
#   1. Reads version from VERSION file
#   2. Retags beyondimmersion/bannou-service:development as :{version}
#   3. Also tags as :latest
#   4. Pushes both tags to Docker Hub
#
# AI AGENTS: DO NOT RUN THIS COMMAND. If the developer asks about deploying
# to production, return the command for them to run manually:
#   make push-release
#
# A PreToolUse hook blocks Claude from executing this command.
# =============================================================================
push-release: ## ⛔ PRODUCTION DEPLOY - Promote development image to versioned release + latest (AI: DO NOT RUN)
	@echo ""
	@echo "⚠️  =========================================="
	@echo "⚠️  PRODUCTION DEPLOYMENT"
	@echo "⚠️  =========================================="
	@echo ""
	@VERSION=$$(cat VERSION | tr -d '[:space:]'); \
	echo "📦 Version from VERSION file: $$VERSION"; \
	echo ""; \
	echo "This will:"; \
	echo "  1. Tag beyondimmersion/bannou-service:development as :$$VERSION"; \
	echo "  2. Tag beyondimmersion/bannou-service:development as :latest"; \
	echo "  3. Push both tags to Docker Hub"; \
	echo ""; \
	read -p "Continue with production deployment? [y/N] " confirm; \
	if [ "$$confirm" != "y" ] && [ "$$confirm" != "Y" ]; then \
		echo "❌ Deployment cancelled"; \
		exit 1; \
	fi; \
	echo ""; \
	echo "🏷️  Tagging beyondimmersion/bannou-service:development as :$$VERSION"; \
	docker tag beyondimmersion/bannou-service:development beyondimmersion/bannou-service:$$VERSION; \
	echo "🏷️  Tagging beyondimmersion/bannou-service:development as :latest"; \
	docker tag beyondimmersion/bannou-service:development beyondimmersion/bannou-service:latest; \
	echo ""; \
	echo "🚀 Pushing beyondimmersion/bannou-service:$$VERSION"; \
	docker push beyondimmersion/bannou-service:$$VERSION; \
	echo "🚀 Pushing beyondimmersion/bannou-service:latest"; \
	docker push beyondimmersion/bannou-service:latest; \
	echo ""; \
	echo "✅ Production deployment complete!"; \
	echo "   - beyondimmersion/bannou-service:$$VERSION"; \
	echo "   - beyondimmersion/bannou-service:latest"

up-compose: ## Start services locally (base + services)
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
		--project-name bannou up -d

up-openresty: ## Start with OpenResty edge proxy (base + services + ingress)
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
		-f provisioning/docker-compose.ingress.yml \
		--project-name bannou up -d

down-compose: ## Stop and cleanup containers
	docker compose \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
		--project-name bannou down --remove-orphans

down-openresty: ## Stop OpenResty setup
	docker compose \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
		-f provisioning/docker-compose.ingress.yml \
		--project-name bannou down --remove-orphans

# =============================================================================
# EXTERNAL CLIENT TESTING
# =============================================================================
# Start Bannou with OpenResty exposed on ports 80/443 for external client
# testing via DNS (e.g., beyond-immersion.com pointing to your machine).
#
# Prerequisites:
# - Configure .env file with OPENRESTY_HTTP_PORT=80 and OPENRESTY_HTTPS_PORT=443
# - Ensure ports 80/443 are forwarded to this machine
# - DNS configured to point to your external IP
# =============================================================================

up-external: ## Start external client testing stack (OpenResty on ports 80/443)
	@echo "🌐 Starting external client testing stack..."
	@echo "📋 OpenResty will be exposed on ports defined in .env (default: 80/443)"
	docker compose --env-file .env \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
		-f provisioning/docker-compose.storage.yml \
		-f provisioning/docker-compose.ingress.yml \
		-f provisioning/docker-compose.external.yml \
		--project-name bannou-external up -d
	@echo "⏳ Waiting for services to become healthy..."
	@sleep 45
	@docker ps --format "table {{.Names}}\t{{.Status}}" | grep -E "bannou-external|NAMES"
	@echo ""
	@echo "✅ External testing stack running"
	@echo ""
	@echo "📋 Available endpoints (via your configured domain):"
	@echo "   POST /auth/register  - Register new account"
	@echo "   POST /auth/login     - Login and get JWT"
	@echo "   WS   /connect        - WebSocket connection"
	@echo ""
	@echo "💡 Use 'make external-register' to create a test admin account"
	@echo "💡 Use 'make down-external' to stop the stack"

down-external: ## Stop external client testing stack
	@echo "🛑 Stopping external client testing stack..."
	docker compose \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
		-f provisioning/docker-compose.storage.yml \
		-f provisioning/docker-compose.ingress.yml \
		-f provisioning/docker-compose.external.yml \
		--project-name bannou-external down --remove-orphans
	@echo "✅ External testing stack stopped"

logs-external: ## View external stack bannou logs
	docker compose \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
		-f provisioning/docker-compose.storage.yml \
		-f provisioning/docker-compose.ingress.yml \
		-f provisioning/docker-compose.external.yml \
		--project-name bannou-external logs -f bannou

external-register: ## Register test admin account (admin@admin.test.local)
	@echo "📝 Registering admin test account..."
	@curl -s -X POST http://localhost/auth/register \
		-H "Content-Type: application/json" \
		-d '{"username":"admin","email":"admin@admin.test.local","password":"admin-test-password-2025"}' \
		| jq . 2>/dev/null || echo "Account may already exist (409 Conflict is OK)"
	@echo ""
	@echo "✅ Admin account: admin@admin.test.local / admin-test-password-2025"

external-login: ## Login with test admin account and display JWT
	@echo "🔑 Logging in with admin account..."
	@curl -s -X POST http://localhost/auth/login \
		-H "Content-Type: application/json" \
		-d '{"email":"admin@admin.test.local","password":"admin-test-password-2025"}' \
		| jq .
	@echo ""
	@echo "💡 Use the accessToken above for authenticated requests"

# =============================================================================
# EXTERNAL IMAGE MANAGEMENT
# =============================================================================
# Commands for switching the external stack between local builds and remote images.
# Use these when testing remote registry images (e.g., CI/CD builds).
# =============================================================================

external-use-dev: ## Switch external stack to use beyondimmersion/bannou-service:development
	@echo "🔄 Switching external stack to development image..."
	@docker tag beyondimmersion/bannou-service:development bannou:latest 2>/dev/null || \
		(echo "❌ Image beyondimmersion/bannou-service:development not found" && \
		echo "💡 Pull it with: docker pull beyondimmersion/bannou-service:development" && exit 1)
	@echo "✅ Tagged beyondimmersion/bannou-service:development as bannou:latest"
	@$(MAKE) external-update
	@echo ""
	@echo "✅ External stack now using development image"
	@echo "💡 Use 'make external-use-local' to switch back to local build"

external-use-local: ## Switch external stack back to locally-built image
	@echo "🔄 Switching external stack to local build..."
	@if docker image inspect bannou:local-backup >/dev/null 2>&1; then \
		docker tag bannou:local-backup bannou:latest; \
		echo "✅ Restored bannou:latest from local-backup"; \
	else \
		echo "⚠️  No local-backup found, rebuilding from source..."; \
		docker compose --env-file .env \
			-f provisioning/docker-compose.yml \
			-f provisioning/docker-compose.services.yml \
			-f provisioning/docker-compose.storage.yml \
			-f provisioning/docker-compose.ingress.yml \
			-f provisioning/docker-compose.external.yml \
			--project-name bannou-external build bannou; \
	fi
	@$(MAKE) external-update
	@echo ""
	@echo "✅ External stack now using local build"

external-update: ## Recreate bannou container with current bannou:latest image (no rebuild)
	@echo "🔄 Updating external bannou container..."
	@docker compose --env-file .env \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
		-f provisioning/docker-compose.storage.yml \
		-f provisioning/docker-compose.ingress.yml \
		-f provisioning/docker-compose.external.yml \
		--project-name bannou-external up -d --no-build --force-recreate bannou
	@echo "⏳ Waiting for bannou to become healthy..."
	@timeout 120 bash -c 'while ! docker inspect bannou-external-bannou-1 --format "{{.State.Health.Status}}" 2>/dev/null | grep -q "healthy"; do sleep 3; done' || \
		(echo "⚠️  Timeout waiting for healthy status" && docker logs --tail 50 bannou-external-bannou-1)
	@docker ps --filter "name=bannou-external-bannou-1" --format "table {{.Names}}\t{{.Image}}\t{{.Status}}"

external-image: ## Show which image the external bannou container is using
	@echo "📋 External bannou container image:"
	@docker inspect bannou-external-bannou-1 --format 'Container: {{.Name}}' 2>/dev/null || echo "❌ Container not running"
	@docker inspect bannou-external-bannou-1 --format 'Image: {{.Config.Image}}' 2>/dev/null || true
	@docker inspect bannou-external-bannou-1 --format 'SHA: {{.Image}}' 2>/dev/null || true
	@echo ""
	@echo "📋 Available bannou images:"
	@docker images --format "table {{.Repository}}:{{.Tag}}\t{{.ID}}\t{{.CreatedSince}}" | grep -E "bannou|development" || echo "  (none)"

clean-build-artifacts: ## Remove bin/obj directories and build-output.txt (improves grep results)
	@echo "🧹 Cleaning build artifacts (bin/obj directories)..."
	@rm -f build-output.txt 2>/dev/null || true
	@find . -type d \( -name "bin" -o -name "obj" \) \
		-not -path "./.git/*" \
		-not -path "./node_modules/*" \
		-exec rm -rf {} + 2>/dev/null || true
	@echo "✅ Build artifacts cleaned"

clean: ## Clean generated files, build artifacts, and caches (add PLUGIN=name for specific plugin)
	@if [ "$(PLUGIN)" ]; then \
		echo "🧹 Cleaning plugin: $(PLUGIN)..."; \
		if [ -d "./plugins/lib-$(PLUGIN)/Generated" ]; then \
			rm -rf "./plugins/lib-$(PLUGIN)/Generated"; \
			echo "  Removed plugins/lib-$(PLUGIN)/Generated"; \
		else \
			echo "  No Generated directory found for plugins/lib-$(PLUGIN)"; \
		fi; \
		echo "✅ Clean completed for plugin: $(PLUGIN)"; \
	else \
		$(MAKE) clean-build-artifacts; \
		echo "🧹 Cleaning all generated files..."; \
		find . -path "./plugins/lib-*/Generated" -type d -exec rm -rf {} + 2>/dev/null || true; \
		find . -path "./sdks/*/Generated" -type d -exec rm -rf {} + 2>/dev/null || true; \
		rm -rf bannou-service/Generated 2>/dev/null || true; \
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
		echo "❌ Error: SERVICES parameter required. Example: make build-plugins SERVICES=\"auth account\""; \
		exit 1; \
	fi

# Build Docker image with specific services only
# Usage: make build-compose-services SERVICES="auth account connect"
build-compose-services:
	@if [ "$(SERVICES)" ]; then \
		echo "🐳 Building Docker image with specific services: $(SERVICES)"; \
		if [ ! -f .env ]; then touch .env; fi; \
		docker compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml --project-name cl build --build-arg BANNOU_SERVICES="$(SERVICES)"; \
		echo "✅ Docker image built with services: $(SERVICES)"; \
	else \
		echo "❌ Error: SERVICES parameter required. Example: make build-compose-services SERVICES=\"auth account\""; \
		exit 1; \
	fi

# Show available services that can be built
list-services:
	@scripts/list-services.sh

# Validate that specific services are included in the latest Docker image
# Usage: make validate-compose-services SERVICES="auth account connect"
validate-compose-services:
	@scripts/validate-compose-services.sh $(SERVICES)

# Regenerate all services, SDK, and documentation
generate:
	@echo "🔧 Generating everything: projects, service files, client SDKs, documentation"
	scripts/generate-all-services.sh
	scripts/generate-storyline-archives.sh
	scripts/generate-client-sdk.sh
	@$(MAKE) generate-sdk-ts
	@$(MAKE) generate-unreal-sdk
	scripts/generate-docs.sh
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

# Generate documentation from schemas and components
generate-docs:
	@echo "📚 Generating documentation..."
	scripts/generate-docs.sh
	@echo "✅ Documentation generation completed"

# Fast EditorConfig checking (recommended for development)
check:
	@echo "🔧 Running lightweight EditorConfig checks..."
	@echo "💡 For comprehensive validation, use 'make lint-editorconfig' or 'make check-ci'"
	@scripts/check-editorconfig.sh
	@echo "✅ Lightweight EditorConfig checks complete"

# EditorConfig validation using editorconfig-checker (ec)
# Install: https://github.com/editorconfig-checker/editorconfig-checker
# Common options:
#   -dry-run           Show which files would be checked
#   -exclude REGEX     Exclude files matching regex
#   -disable-indent-size    Skip indent size checks (useful for generated files)
#   -f github-actions  Output format for CI (also: gcc, codeclimate)
#   -verbose           Print debug info
lint-editorconfig:
	@echo "🔧 Running editorconfig-checker..."
	@if command -v ec >/dev/null 2>&1; then \
		ec && echo "✅ EditorConfig validation passed"; \
	else \
		echo "❌ 'ec' not found. Install from: https://github.com/editorconfig-checker/editorconfig-checker"; \
		echo "   Or use 'make check-ci' for Docker-based validation"; \
		exit 1; \
	fi

# EditorConfig validation matching GitHub Actions CI (uses editorconfig-checker)
# Note: CI now uses 'ec' directly instead of super-linter for faster execution
check-ci: lint-editorconfig

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
	@if [ -f "sdks/typescript/package.json" ]; then \
		echo "🔧 Formatting TypeScript SDK..."; \
		cd sdks/typescript && npm install --silent && npm run format; \
	fi
	@echo "✅ All formatting tasks complete"

# Alias for fix (common convention)
format: fix

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

# SDK unit testing (separate from server tests)
test-sdks: ## Run all SDK tests
	@echo "🧪 Running SDK tests..."
	dotnet test sdks/bannou-sdks.sln
	@echo "✅ SDK tests completed"

# Infrastructure integration testing (matches CI workflow)
# Uses minimal service configuration (TESTING service only) - no databases, no ingress
# Stack: base + test + test.infrastructure (minimal dependencies)
test-infrastructure:
	@echo "🚀 Running infrastructure tests (TESTING service only - no external deps)..."
	docker compose -p bannou-test-infra \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.infrastructure.yml" \
		build --no-cache bannou
	docker compose -p bannou-test-infra \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.infrastructure.yml" \
		up --no-deps --exit-code-from=bannou-infra-tester bannou bannou-infra-tester
	docker compose -p bannou-test-infra \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.infrastructure.yml" \
		down --remove-orphans -v
	@echo "✅ Infrastructure tests completed"

# Prepare test fixtures (copies fixtures with dot_git -> .git rename)
# Required for documentation git binding tests - follows LibGit2Sharp pattern
prepare-fixtures:
	@echo "📁 Preparing test fixtures..."
	@./scripts/prepare-test-fixtures.sh

# HTTP integration testing (matches CI workflow)
# Usage: make test-http [PLUGIN=plugin-name]
# Stack: base + services + test + test.http (service-to-service via mesh, no ingress)
# Note: Uses 'up -d' + 'wait' instead of '--exit-code-from' to avoid aborting
#       when orchestrator tests create/destroy containers during the test run.
test-http: prepare-fixtures
	@if [ "$(PLUGIN)" ]; then \
		echo "🧪 Running HTTP integration tests for plugin: $(PLUGIN)..."; \
	else \
		echo "🧪 Running HTTP integration tests (service-to-service via mesh)..."; \
	fi
	@PLUGIN=$(PLUGIN) docker compose -p bannou-test-http \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.http.yml" \
		build --no-cache
	@PLUGIN=$(PLUGIN) docker compose -p bannou-test-http \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.http.yml" \
		up -d
	@( PLUGIN=$(PLUGIN) docker compose -p bannou-test-http \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.http.yml" \
		logs -f bannou-http-tester & ); \
	docker wait bannou-test-http-bannou-http-tester-1; \
	TEST_EXIT_CODE=$$?; \
	docker compose -p bannou-test-http \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.http.yml" \
		down --remove-orphans -v; \
	if [ $$TEST_EXIT_CODE -eq 0 ]; then \
		echo "✅ HTTP integration tests completed successfully"; \
	else \
		echo "❌ HTTP integration tests failed with exit code $$TEST_EXIT_CODE"; \
	fi; \
	exit $$TEST_EXIT_CODE

# WebSocket/Edge integration testing (matches CI workflow)
# Simulates external client connecting through OpenResty edge proxy
# Stack: base + services + ingress + test + test.edge
# Note: Uses 'up -d' + 'wait' instead of '--exit-code-from' for consistency
#       with HTTP tests and to avoid abort issues with container lifecycle.

test-edge: test-pre-cleanup prepare-fixtures
	@echo "🧪 Running Edge/WebSocket integration tests..."
	@docker compose -p bannou-test-edge \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		build --no-cache
	@docker compose -p bannou-test-edge \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		up -d
	@( docker compose -p bannou-test-edge \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		logs -f bannou-edge-tester & ); \
	docker compose -p bannou-test-edge \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		wait bannou-edge-tester; \
	TEST_EXIT_CODE=$$?; \
	docker compose -p bannou-test-edge \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		down --remove-orphans -v; \
	if [ $$TEST_EXIT_CODE -eq 0 ]; then \
		echo "✅ Edge integration tests completed successfully"; \
	else \
		echo "❌ Edge integration tests failed with exit code $$TEST_EXIT_CODE"; \
	fi; \
	exit $$TEST_EXIT_CODE

# =============================================================================
# DEVELOPMENT TEST COMMANDS (keep containers + log files)
# =============================================================================
# These variants keep containers running for inspection and save logs to files
# Logs are saved to ./test-logs/ directory for easy review
# =============================================================================

TEST_LOG_DIR := ./test-logs

# Create test log directory
test-logs-dir:
	@mkdir -p $(TEST_LOG_DIR)

# HTTP testing with container persistence - keeps running for inspection
test-http-dev: test-logs-dir prepare-fixtures ## HTTP tests: keep containers running, save logs to ./test-logs/
	@echo "🧪 Starting HTTP integration tests (dev mode - containers stay running)..."
	@echo "📁 Logs will be saved to $(TEST_LOG_DIR)/"
	PLUGIN=$(PLUGIN) docker compose -p bannou-test-http \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.http.yml" \
		up --build -d
	@echo "⏳ Waiting for test to complete (check logs with 'make test-http-logs')..."
	@echo "💡 Use 'make test-http-down' when done to clean up containers"
	@echo "💡 Use 'make test-http-logs' to view latest output"
	@sleep 5
	@$(MAKE) test-http-logs
	@echo "✅ Dev test containers running. Use 'make test-http-down' to clean up."

# Collect HTTP tester logs to file and display
test-http-logs: test-logs-dir ## Collect HTTP test logs to ./test-logs/http-tester.log
	@if docker container inspect bannou-test-http-bannou-http-tester-1 >/dev/null 2>&1; then \
		echo "📋 Collecting HTTP tester logs..."; \
		docker logs bannou-test-http-bannou-http-tester-1 2>&1 | tee $(TEST_LOG_DIR)/http-tester.log; \
	else \
		echo "⚠️  HTTP tester container not found - skipping (existing logs preserved)"; \
	fi
	@if docker container inspect bannou-test-http-bannou-1 >/dev/null 2>&1; then \
		echo "📋 Collecting bannou service logs..."; \
		docker logs bannou-test-http-bannou-1 2>&1 | tee $(TEST_LOG_DIR)/http-bannou.log; \
	else \
		echo "⚠️  Bannou service container not found - skipping (existing logs preserved)"; \
	fi
	@echo ""
	@echo "✅ Logs collected (existing files preserved if containers not found)"

# Follow HTTP tester logs live
test-http-follow: ## Follow HTTP test logs in real-time
	@docker logs -f bannou-test-http-bannou-http-tester-1

# Cleanup HTTP dev containers
test-http-down: ## Stop HTTP test containers
	@echo "🛑 Stopping HTTP test containers..."
	docker compose -p bannou-test-http \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.http.yml" \
		down --remove-orphans -v
	@echo "✅ HTTP test containers stopped"

# Edge testing with container persistence
test-edge-dev: test-logs-dir ## Edge tests: keep containers running, save logs to ./test-logs/
	@echo "🧪 Starting Edge/WebSocket tests (dev mode - containers stay running)..."
	@echo "📁 Logs will be saved to $(TEST_LOG_DIR)/"
	@docker compose -p bannou-test-edge \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		build --no-cache
	@docker compose -p bannou-test-edge \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		up -d
	@echo "⏳ Waiting for test to start..."
	@sleep 5
	@$(MAKE) test-edge-logs
	@echo "✅ Dev test containers running. Use 'make test-edge-down' to clean up."

# Collect Edge tester logs
test-edge-logs: test-logs-dir ## Collect Edge test logs to ./test-logs/edge-tester.log
	@if docker container inspect bannou-test-edge-bannou-edge-tester-1 >/dev/null 2>&1; then \
		echo "📋 Collecting Edge tester logs..."; \
		docker logs bannou-test-edge-bannou-edge-tester-1 2>&1 | tee $(TEST_LOG_DIR)/edge-tester.log; \
	else \
		echo "⚠️  Edge tester container not found - skipping (existing logs preserved)"; \
	fi
	@if docker container inspect bannou-test-edge-bannou-1 >/dev/null 2>&1; then \
		echo "📋 Collecting bannou service logs..."; \
		docker logs bannou-test-edge-bannou-1 2>&1 | tee $(TEST_LOG_DIR)/edge-bannou.log; \
	else \
		echo "⚠️  Bannou service container not found - skipping (existing logs preserved)"; \
	fi
	@if docker container inspect bannou-test-edge-openresty-1 >/dev/null 2>&1; then \
		echo "📋 Collecting OpenResty logs..."; \
		docker logs bannou-test-edge-openresty-1 2>&1 | tee $(TEST_LOG_DIR)/edge-openresty.log; \
	else \
		echo "⚠️  OpenResty container not found - skipping (existing logs preserved)"; \
	fi
	@echo ""
	@echo "✅ Logs collected (existing files preserved if containers not found)"

# Follow Edge tester logs live
test-edge-follow: ## Follow Edge test logs in real-time
	@docker logs -f bannou-test-edge-bannou-edge-tester-1

# Cleanup Edge dev containers
test-edge-down: ## Stop Edge test containers
	@echo "🛑 Stopping Edge test containers..."
	docker compose -p bannou-test-edge \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		down --remove-orphans -v
	@echo "✅ Edge test containers stopped"

# Infrastructure testing with persistence
test-infra-dev: test-logs-dir ## Infrastructure tests: keep containers running, save logs
	@echo "🧪 Starting infrastructure tests (dev mode)..."
	docker compose -p bannou-test-infra \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.infrastructure.yml" \
		up --build -d
	@sleep 5
	@$(MAKE) test-infra-logs
	@echo "✅ Dev test containers running. Use 'make test-infra-down' to clean up."

test-infra-logs: test-logs-dir ## Collect infrastructure test logs
	@docker logs bannou-test-infra-bannou-infra-tester-1 2>&1 | tee $(TEST_LOG_DIR)/infra-tester.log
	@echo "✅ Logs saved to $(TEST_LOG_DIR)/infra-tester.log"

test-infra-down: ## Stop infrastructure test containers
	docker compose -p bannou-test-infra \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.infrastructure.yml" \
		down --remove-orphans -v

# Clean all test logs
test-logs-clean: ## Remove all saved test logs
	@rm -rf $(TEST_LOG_DIR)
	@echo "✅ Test logs cleaned"

# =============================================================================
# ORCHESTRATOR COMMANDS
# =============================================================================
# The orchestrator connects directly to Redis (heartbeats) and RabbitMQ (events).
# It monitors and manages Bannou service deployments across multiple backends.
# =============================================================================

up-orchestrator: ## Start orchestrator standalone with infrastructure
	@echo "🚀 Starting orchestrator in standalone mode..."
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
		-f provisioning/docker-compose.orchestrator.yml \
		--project-name bannou-orchestrator up -d
	@echo "✅ Orchestrator running at http://localhost:8090"
	@echo "📋 API endpoints:"
	@echo "   GET  /orchestrator/status         - Overall status"
	@echo "   GET  /orchestrator/health         - Infrastructure health"
	@echo "   GET  /orchestrator/services       - Service health"
	@echo "   GET  /orchestrator/backends       - Available backends"
	@echo "   POST /orchestrator/restart        - Restart a service"
	@echo "   GET  /orchestrator/containers     - Container status"

down-orchestrator: ## Stop orchestrator stack
	@echo "🛑 Stopping orchestrator..."
	docker compose \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
		-f provisioning/docker-compose.orchestrator.yml \
		--project-name bannou-orchestrator down --remove-orphans
	@echo "✅ Orchestrator stopped"

logs-orchestrator: ## View orchestrator logs
	docker compose \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
		-f provisioning/docker-compose.orchestrator.yml \
		--project-name bannou-orchestrator logs -f bannou-orchestrator

# Orchestrator API test commands
orchestrator-status: ## Get orchestrator status
	@curl -s http://localhost:8090/orchestrator/status | jq .

orchestrator-health: ## Get infrastructure health
	@curl -s http://localhost:8090/orchestrator/health | jq .

orchestrator-services: ## Get service health report
	@curl -s http://localhost:8090/orchestrator/services | jq .

orchestrator-backends: ## Get available backends
	@curl -s http://localhost:8090/orchestrator/backends | jq .

orchestrator-containers: ## Get container status
	@curl -s http://localhost:8090/orchestrator/containers | jq .

# Test all orchestrator APIs
test-orchestrator: ## Test all orchestrator APIs
	@echo "🧪 Testing orchestrator APIs..."
	@echo ""
	@echo "📋 GET /orchestrator/status"
	@curl -s http://localhost:8090/orchestrator/status | jq . || echo "❌ Failed"
	@echo ""
	@echo "📋 GET /orchestrator/health"
	@curl -s http://localhost:8090/orchestrator/health | jq . || echo "❌ Failed"
	@echo ""
	@echo "📋 GET /orchestrator/services"
	@curl -s http://localhost:8090/orchestrator/services | jq . || echo "❌ Failed"
	@echo ""
	@echo "📋 GET /orchestrator/backends"
	@curl -s http://localhost:8090/orchestrator/backends | jq . || echo "❌ Failed"
	@echo ""
	@echo "📋 GET /orchestrator/containers"
	@curl -s http://localhost:8090/orchestrator/containers | jq . || echo "❌ Failed"
	@echo ""
	@echo "✅ Orchestrator API tests completed"

# =============================================================================
# TEST CONTAINER CLEANUP
# =============================================================================
# Cleanup commands for removing leftover containers from failed/interrupted tests.
# Targets both docker-compose managed containers and dynamically deployed
# containers created by the orchestrator service.
# =============================================================================

test-cleanup: ## Remove leftover test containers (interactive)
	@scripts/cleanup-test-containers.sh

test-cleanup-dry: ## Show what test containers would be removed (dry run)
	@scripts/cleanup-test-containers.sh --dry-run

test-cleanup-force: ## Force remove all test containers without confirmation
	@scripts/cleanup-test-containers.sh --force

# Pre-test cleanup (runs before test targets to ensure clean state)
test-pre-cleanup:
	@echo "🧹 Pre-test cleanup: removing stale test containers..."
	@scripts/cleanup-test-containers.sh --force 2>/dev/null || true

# =============================================================================
# VOICE INFRASTRUCTURE (Kamailio + RTPEngine)
# =============================================================================
# Scaled tier voice infrastructure for conferences with 6+ participants.
# Uses network_mode: host for SIP/RTP traffic handling.
# Kamailio: SIP proxy with JSONRPC control on :5080
# RTPEngine: SFU media relay with ng protocol on UDP :22222
# =============================================================================

up-voice: ## Start voice infrastructure (Kamailio + RTPEngine)
	@echo "🎙️ Starting voice infrastructure..."
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env \
		-f provisioning/docker-compose.voice.yml \
		--project-name bannou-voice up -d
	@echo "✅ Voice infrastructure running"
	@echo "📋 Services:"
	@echo "   Kamailio SIP:      UDP/TCP :5060"
	@echo "   Kamailio JSONRPC:  HTTP :5080/RPC"
	@echo "   RTPEngine ng:      UDP :22222"
	@echo "   RTPEngine CLI:     TCP :9901"

down-voice: ## Stop voice infrastructure
	@echo "🛑 Stopping voice infrastructure..."
	docker compose \
		-f provisioning/docker-compose.voice.yml \
		--project-name bannou-voice down --remove-orphans
	@echo "✅ Voice infrastructure stopped"

logs-voice: ## View voice infrastructure logs
	docker compose \
		-f provisioning/docker-compose.voice.yml \
		--project-name bannou-voice logs -f

logs-kamailio: ## View Kamailio logs only
	docker logs -f bannou-kamailio

logs-rtpengine: ## View RTPEngine logs only
	docker logs -f bannou-rtpengine

voice-status: ## Check voice infrastructure health
	@echo "📋 Voice Infrastructure Status:"
	@echo ""
	@echo "Kamailio:"
	@curl -s http://127.0.0.1:5080/health || echo "❌ Not responding"
	@echo ""
	@echo ""
	@echo "RTPEngine:"
	@echo "list totals" | nc -q1 127.0.0.1 9901 2>/dev/null || echo "❌ Not responding"

# Start full stack with voice infrastructure
up-compose-voice: ## Start services + voice infrastructure
	@echo "🚀 Starting full stack with voice infrastructure..."
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
		-f provisioning/docker-compose.voice.yml \
		--project-name bannou up -d
	@echo "✅ Full stack with voice infrastructure running"

down-compose-voice: ## Stop services + voice infrastructure
	docker compose \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
		-f provisioning/docker-compose.voice.yml \
		--project-name bannou down --remove-orphans

# Voice Scaled Tier integration testing (via Edge/WebSocket tester)
# Tests voice service with Kamailio + RTPEngine infrastructure
# Stack: base + services + ingress + test + edge + voice (integrated)
# Uses VOICE_TESTS_ENABLED=true so edge-tester runs voice test suite
test-voice-scaled: test-pre-cleanup ## Voice scaled tier tests with Kamailio + RTPEngine
	@echo "🎙️ Running Voice Scaled Tier integration tests..."
	@echo "📋 Building test containers with voice infrastructure..."
	@docker compose -p bannou-test-voice \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		-f "./provisioning/docker-compose.test.voice.yml" \
		build --no-cache
	@echo "📋 Starting voice test environment..."
	@docker compose -p bannou-test-voice \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		-f "./provisioning/docker-compose.test.voice.yml" \
		up -d
	@( docker compose -p bannou-test-voice \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		-f "./provisioning/docker-compose.test.voice.yml" \
		logs -f bannou-edge-tester & ); \
	docker compose -p bannou-test-voice \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		-f "./provisioning/docker-compose.test.voice.yml" \
		wait bannou-edge-tester; \
	TEST_EXIT_CODE=$$?; \
	echo "🧹 Cleaning up test containers..."; \
	docker compose -p bannou-test-voice \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		-f "./provisioning/docker-compose.test.voice.yml" \
		down --remove-orphans -v; \
	if [ $$TEST_EXIT_CODE -eq 0 ]; then \
		echo "✅ Voice Scaled Tier tests completed successfully"; \
	else \
		echo "❌ Voice Scaled Tier tests failed with exit code $$TEST_EXIT_CODE"; \
	fi; \
	exit $$TEST_EXIT_CODE

# Voice testing with container persistence (dev mode)
test-voice-dev: test-logs-dir ## Voice tests: keep containers running, save logs
	@echo "🎙️ Starting Voice tests (dev mode - containers stay running)..."
	@echo "📁 Logs will be saved to $(TEST_LOG_DIR)/"
	@docker compose -p bannou-test-voice \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		-f "./provisioning/docker-compose.test.voice.yml" \
		build --no-cache
	@docker compose -p bannou-test-voice \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		-f "./provisioning/docker-compose.test.voice.yml" \
		up -d
	@echo "⏳ Waiting for test to start..."
	@sleep 5
	@$(MAKE) test-voice-logs
	@echo "✅ Dev test containers running. Use 'make test-voice-down' to clean up."

# Collect Voice tester logs
test-voice-logs: test-logs-dir ## Collect Voice test logs
	@echo "📋 Collecting Voice tester logs..."
	@docker logs bannou-test-voice-bannou-edge-tester-1 2>&1 | tee $(TEST_LOG_DIR)/voice-tester.log
	@echo "📋 Collecting bannou service logs..."
	@docker logs bannou-test-voice-bannou-1 2>&1 | tee $(TEST_LOG_DIR)/voice-bannou.log
	@echo ""
	@echo "✅ Logs saved to:"
	@echo "   $(TEST_LOG_DIR)/voice-tester.log"
	@echo "   $(TEST_LOG_DIR)/voice-bannou.log"

# Cleanup Voice dev containers
test-voice-down: ## Stop Voice test containers
	@echo "🛑 Stopping Voice test containers..."
	docker compose -p bannou-test-voice \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		-f "./provisioning/docker-compose.test.voice.yml" \
		down --remove-orphans -v
	@echo "✅ Voice test containers stopped"

# =============================================================================
# VERSIONING & RELEASES
# =============================================================================
# Semantic versioning workflow for platform releases.
# See docs/guides/RELEASING.md for full documentation.
#
# Quick workflow:
#   1. make prepare-release VERSION=x.y.z   (updates VERSION + CHANGELOG)
#   2. Review changes with git diff
#   3. make release-commit VERSION=x.y.z    (commits the changes)
#   4. Create PR to master
#   5. Merge triggers automatic tag + GitHub Release
# =============================================================================

version: ## Show current platform and SDK versions
	@echo "📦 Platform version: $$(cat VERSION | tr -d '[:space:]')"
	@echo "📦 SDK version:      $$(cat sdks/SDK_VERSION | tr -d '[:space:]')"

prepare-release: ## Prepare a release (updates VERSION + CHANGELOG). Usage: make prepare-release VERSION=x.y.z
	@if [ -z "$(VERSION)" ]; then \
		echo "❌ Error: VERSION parameter required"; \
		echo "Usage: make prepare-release VERSION=x.y.z"; \
		echo "Example: make prepare-release VERSION=0.10.0"; \
		exit 1; \
	fi
	@scripts/prepare-release.sh $(VERSION)

release-commit: ## Commit release preparation changes. Usage: make release-commit VERSION=x.y.z
	@if [ -z "$(VERSION)" ]; then \
		echo "❌ Error: VERSION parameter required"; \
		echo "Usage: make release-commit VERSION=x.y.z"; \
		exit 1; \
	fi
	@echo "📝 Committing release preparation for v$(VERSION)..."
	git add VERSION CHANGELOG.md
	git commit -m "chore: prepare release v$(VERSION)"
	@echo ""
	@echo "✅ Release preparation committed"
	@echo ""
	@echo "Next steps:"
	@echo "  1. Push branch:     git push origin HEAD"
	@echo "  2. Create PR to master"
	@echo "  3. Merge to trigger automatic release"

release-status: ## Show release status (last tag, pending changes)
	@echo "📋 Release Status"
	@echo "================="
	@echo ""
	@echo "Current VERSION file: $$(cat VERSION | tr -d '[:space:]')"
	@echo ""
	@echo "Last platform release tag:"
	@git tag -l 'v*' --sort=-v:refname | head -n 1 || echo "  (no tags found)"
	@echo ""
	@echo "Last SDK release tag:"
	@git tag -l 'sdk-v*' --sort=-v:refname | head -n 1 || echo "  (no tags found)"
	@echo ""
	@echo "Unreleased changes in CHANGELOG:"
	@awk '/^## \[Unreleased\]/{found=1; next} /^## \[/{exit} /^---$$/{exit} found && NF {print "  " $$0}' CHANGELOG.md || echo "  (none)"

# Legacy tag command (creates timestamped tags)
tagname := $(shell date -u +%FT%H-%M-%SZ)
tag:
	git tag $(tagname) -a -m '$(msg)'
	git push origin $(tagname)

# =============================================================================
# TYPESCRIPT SDK
# =============================================================================
# TypeScript client SDK for browser and Node.js environments.
# Location: sdks/typescript/
# =============================================================================

TS_SDK_DIR := sdks/typescript

generate-sdk-ts: generate-client-schema ## Generate TypeScript SDK types, proxies, and event registry from schemas
	@echo "🔧 Generating TypeScript SDK code..."
	@if [ ! -d "$(TS_SDK_DIR)/client/node_modules/openapi-typescript" ]; then \
		echo "📦 Installing TypeScript SDK dependencies..."; \
		cd $(TS_SDK_DIR) && npm install; \
	fi
	cd $(TS_SDK_DIR)/client && npm run generate
	@echo "✅ TypeScript SDK code generation completed"

format-sdk-ts: ## Format TypeScript SDK code with Prettier
	@echo "🔧 Formatting TypeScript SDK..."
	@if [ ! -d "$(TS_SDK_DIR)/node_modules" ]; then \
		echo "📦 Installing root dependencies..."; \
		cd $(TS_SDK_DIR) && npm install; \
	fi
	cd $(TS_SDK_DIR) && npm run format
	@echo "✅ TypeScript SDK formatting completed"

build-sdk-ts: generate-sdk-ts format-sdk-ts ## Build TypeScript SDK packages
	@echo "🔧 Building TypeScript SDK..."
	@if [ ! -d "$(TS_SDK_DIR)/node_modules" ]; then \
		echo "📦 Installing root dependencies..."; \
		cd $(TS_SDK_DIR) && npm install; \
	fi
	@if [ ! -d "$(TS_SDK_DIR)/core/node_modules" ]; then \
		echo "📦 Installing core dependencies..."; \
		cd $(TS_SDK_DIR)/core && npm install; \
	fi
	@if [ ! -d "$(TS_SDK_DIR)/client/node_modules" ]; then \
		echo "📦 Installing client dependencies..."; \
		cd $(TS_SDK_DIR)/client && npm install; \
	fi
	cd $(TS_SDK_DIR)/core && npm run build
	cd $(TS_SDK_DIR)/client && npm run build
	@echo "✅ TypeScript SDK build completed"

test-sdk-ts: build-sdk-ts ## Run TypeScript SDK tests
	@echo "🧪 Running TypeScript SDK tests..."
	cd $(TS_SDK_DIR)/client && npm test
	@echo "✅ TypeScript SDK tests completed"

clean-sdk-ts: ## Clean TypeScript SDK build artifacts
	@echo "🧹 Cleaning TypeScript SDK..."
	rm -rf $(TS_SDK_DIR)/core/dist $(TS_SDK_DIR)/core/node_modules
	rm -rf $(TS_SDK_DIR)/client/dist $(TS_SDK_DIR)/client/node_modules
	rm -rf $(TS_SDK_DIR)/client/src/Generated/types/*.ts
	rm -rf $(TS_SDK_DIR)/client/src/Generated/proxies/*.ts
	rm -rf $(TS_SDK_DIR)/client/src/Generated/events/*.ts
	rm -rf $(TS_SDK_DIR)/node_modules
	@echo "✅ TypeScript SDK cleaned"

typecheck-sdk-ts: ## Type-check TypeScript SDK without building
	@echo "🔍 Type-checking TypeScript SDK..."
	cd $(TS_SDK_DIR)/core && npm run typecheck
	cd $(TS_SDK_DIR)/client && npm run typecheck
	@echo "✅ TypeScript SDK type-check completed"

check-sdk-ts: ## Check TypeScript SDK formatting (for CI)
	@echo "🔍 Checking TypeScript SDK formatting..."
	@if [ ! -d "$(TS_SDK_DIR)/node_modules" ]; then \
		echo "📦 Installing root dependencies..."; \
		cd $(TS_SDK_DIR) && npm install; \
	fi
	cd $(TS_SDK_DIR) && npm run format:check
	@echo "✅ TypeScript SDK formatting check passed"

# =============================================================================
# ⛔ NPM PUBLISHING - AI AGENTS MUST NEVER RUN THIS COMMAND ⛔
# =============================================================================
# This command publishes the TypeScript SDK packages to npm. It:
#   1. Builds both core and client packages
#   2. Publishes @beyondimmersion/bannou-core to npm
#   3. Publishes @beyondimmersion/bannou-client to npm
#
# AI AGENTS: DO NOT RUN THIS COMMAND. If the developer asks about publishing
# to npm, return the command for them to run manually:
#   make publish-sdk-ts
#
# A PreToolUse hook blocks Claude from executing this command.
# =============================================================================
publish-sdk-ts: build-sdk-ts ## ⛔ NPM PUBLISH - Publish TypeScript SDK packages to npm (AI: DO NOT RUN)
	@echo ""
	@echo "⚠️  =========================================="
	@echo "⚠️  NPM PACKAGE PUBLISHING"
	@echo "⚠️  =========================================="
	@echo ""
	@echo "This will publish the following packages to npm:"
	@echo "  - @beyondimmersion/bannou-core"
	@echo "  - @beyondimmersion/bannou-client"
	@echo ""
	@read -p "Continue? [y/N] " confirm && [ "$$confirm" = "y" ] || (echo "Aborted."; exit 1)
	@echo ""
	@echo "📦 Publishing @beyondimmersion/bannou-core..."
	cd $(TS_SDK_DIR)/core && npm publish --access public
	@echo "✅ @beyondimmersion/bannou-core published"
	@echo ""
	@echo "📦 Publishing @beyondimmersion/bannou-client..."
	cd $(TS_SDK_DIR)/client && npm publish --access public
	@echo "✅ @beyondimmersion/bannou-client published"
	@echo ""
	@echo "🎉 TypeScript SDK packages published successfully!"

# =============================================================================
# UNREAL ENGINE SDK
# =============================================================================
# Unreal Engine helper files for integrating with Bannou services.
# Location: sdks/unreal/
# Generated: Protocol headers, type definitions, endpoint registry
# =============================================================================

UE_SDK_DIR := sdks/unreal

generate-client-schema: ## Generate consolidated client-facing OpenAPI schema
	@echo "🔧 Generating consolidated client schema..."
	python3 scripts/generate-client-schema.py
	@echo "✅ Client schema generation completed"

generate-unreal-sdk: generate-client-schema ## Generate Unreal Engine SDK helper headers
	@echo "🔧 Generating Unreal Engine SDK..."
	python3 scripts/generate-unreal-sdk.py
	@echo "✅ Unreal Engine SDK generation completed"

clean-unreal-sdk: ## Clean Unreal SDK generated files
	@echo "🧹 Cleaning Unreal SDK..."
	rm -rf $(UE_SDK_DIR)/Generated/*.h
	rm -rf schemas/Generated/bannou-client-api.*
	@echo "✅ Unreal SDK cleaned"
