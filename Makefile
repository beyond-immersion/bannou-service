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
# - Local Dev (Host): base + local + host + [feature-specific] (WSL2 Dapr workaround)
# - Local Test: base + local + test-local + [feature-specific]
# - Local Test (Host): base + local + test-local + host + [feature-specific] (WSL2 workaround)
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
# Stack: base + services + test + test.http (service-to-service via Dapr, no ingress)
# Note: Uses 'up -d' + 'wait' instead of '--exit-code-from' to avoid aborting
#       when orchestrator tests create/destroy containers during the test run.
test-http:
	@if [ "$(PLUGIN)" ]; then \
		echo "🧪 Running HTTP integration tests for plugin: $(PLUGIN)..."; \
	else \
		echo "🧪 Running HTTP integration tests (service-to-service via Dapr)..."; \
	fi
	@SERVICE_DOMAIN=test-http PLUGIN=$(PLUGIN) docker compose -p bannou-test-http \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.http.yml" \
		build --no-cache
	@SERVICE_DOMAIN=test-http PLUGIN=$(PLUGIN) docker compose -p bannou-test-http \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.http.yml" \
		up -d
	@( SERVICE_DOMAIN=test-http PLUGIN=$(PLUGIN) docker compose -p bannou-test-http \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.http.yml" \
		logs -f bannou-http-tester & ); \
	SERVICE_DOMAIN=test-http PLUGIN=$(PLUGIN) docker compose -p bannou-test-http \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.http.yml" \
		wait bannou-http-tester; \
	TEST_EXIT_CODE=$$?; \
	docker compose -p bannou-test-http \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
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
test-edge:
	@echo "🧪 Running Edge/WebSocket integration tests..."
	@docker compose -p bannou-test-edge \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		build --no-cache
	@docker compose -p bannou-test-edge \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		up -d
	@( docker compose -p bannou-test-edge \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		logs -f bannou-edge-tester & ); \
	docker compose -p bannou-test-edge \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		wait bannou-edge-tester; \
	TEST_EXIT_CODE=$$?; \
	docker compose -p bannou-test-edge \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
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
	docker compose -p bannou-test-edge \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
		-f "./provisioning/docker-compose.ingress.yml" \
		-f "./provisioning/docker-compose.test.yml" \
		-f "./provisioning/docker-compose.test.edge.yml" \
		build --no-cache
	docker compose -p bannou-test-edge \
		-f "./provisioning/docker-compose.yml" \
		-f "./provisioning/docker-compose.services.yml" \
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
# The orchestrator runs in standalone mode without Dapr, connecting directly
# to Redis (heartbeats) and RabbitMQ (events). It monitors and manages
# Bannou service deployments across multiple backends.
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
# GIT TAGGING
# =============================================================================

tagname := $(shell date -u +%FT%H-%M-%SZ)
tag:
	git tag $(tagname) -a -m '$(msg)'
	git push origin $(tagname)
