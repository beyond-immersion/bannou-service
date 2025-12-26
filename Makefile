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

build-compose: ## Build Docker containers (all services)
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
		--project-name bannou build

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
		-f provisioning/docker-compose.ingress.yml \
		-f provisioning/docker-compose.external.yml \
		--project-name bannou-external down --remove-orphans
	@echo "✅ External testing stack stopped"

logs-external: ## View external stack bannou logs
	docker compose \
		-f provisioning/docker-compose.yml \
		-f provisioning/docker-compose.services.yml \
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

# Regenerate all services, SDK, and documentation
generate:
	@echo "🔧 Generating everything: projects, service files, client SDK, documentation"
	scripts/generate-all-services.sh
	scripts/generate-client-sdk.sh
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

# Infrastructure integration testing (matches CI workflow)
# Uses minimal service configuration (TESTING service only) - no databases, no ingress
# Stack: base + test + test.infrastructure (minimal dependencies)
test-infrastructure:
	@echo "🚀 Running infrastructure tests (TESTING service only - minimal deps)..."
	docker compose -p bannou-test-infra \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.infrastructure.yml" \
		build --no-cache
	docker compose -p bannou-test-infra \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.infrastructure.yml" \
		up --exit-code-from=bannou-infra-tester
	docker compose -p bannou-test-infra \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.infrastructure.yml" \
		down --remove-orphans -v
	@echo "✅ Infrastructure tests completed"

# HTTP integration testing (matches CI workflow)
# Usage: make test-http [PLUGIN=plugin-name]
# Stack: base + services + test + test.http (service-to-service via mesh, no ingress)
# Note: Uses 'up -d' + 'wait' instead of '--exit-code-from' to avoid aborting
#       when orchestrator tests create/destroy containers during the test run.
test-http:
	@if [ "$(PLUGIN)" ]; then \
		echo "🧪 Running HTTP integration tests for plugin: $(PLUGIN)..."; \
	else \
		echo "🧪 Running HTTP integration tests (service-to-service via mesh)..."; \
	fi
	@SERVICE_DOMAIN=test-http PLUGIN=$(PLUGIN) docker compose -p bannou-test-http \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.http.yml" \
		build --no-cache
	@SERVICE_DOMAIN=test-http PLUGIN=$(PLUGIN) docker compose -p bannou-test-http \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.http.yml" \
		up -d
	@( SERVICE_DOMAIN=test-http PLUGIN=$(PLUGIN) docker compose -p bannou-test-http \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.http.yml" \
		logs -f bannou-http-tester & ); \
	SERVICE_DOMAIN=test-http PLUGIN=$(PLUGIN) docker compose -p bannou-test-http \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.storage.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.http.yml" \
		wait bannou-http-tester; \
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

test-edge: test-pre-cleanup
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
test-http-dev: test-logs-dir ## HTTP tests: keep containers running, save logs to ./test-logs/
	@echo "🧪 Starting HTTP integration tests (dev mode - containers stay running)..."
	@echo "📁 Logs will be saved to $(TEST_LOG_DIR)/"
	SERVICE_DOMAIN=test-http PLUGIN=$(PLUGIN) docker compose -p bannou-test-http \
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
	@echo "📋 Collecting HTTP tester logs..."
	@docker logs bannou-test-http-bannou-http-tester-1 2>&1 | tee $(TEST_LOG_DIR)/http-tester.log
	@echo "📋 Collecting bannou service logs..."
	@docker logs bannou-test-http-bannou-1 2>&1 | tee $(TEST_LOG_DIR)/http-bannou.log
	@echo ""
	@echo "✅ Logs saved to:"
	@echo "   $(TEST_LOG_DIR)/http-tester.log"
	@echo "   $(TEST_LOG_DIR)/http-bannou.log"

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
	@echo "📋 Collecting Edge tester logs..."
	@docker logs bannou-test-edge-bannou-edge-tester-1 2>&1 | tee $(TEST_LOG_DIR)/edge-tester.log
	@echo "📋 Collecting bannou service logs..."
	@docker logs bannou-test-edge-bannou-1 2>&1 | tee $(TEST_LOG_DIR)/edge-bannou.log
	@echo ""
	@echo "✅ Logs saved to:"
	@echo "   $(TEST_LOG_DIR)/edge-tester.log"
	@echo "   $(TEST_LOG_DIR)/edge-bannou.log"

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
# GIT TAGGING
# =============================================================================

tagname := $(shell date -u +%FT%H-%M-%SZ)
tag:
	git tag $(tagname) -a -m '$(msg)'
	git push origin $(tagname)
