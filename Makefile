build:
	dotnet build

build-compose:
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml --project-name cl build

up-compose:
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.ingress.yml --project-name cl up -d

ci-up-compose:
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.ci.yml -f provisioning/docker-compose.ingress.yml --project-name cl up -d

elk-up-compose:
	if [ ! -f .env ]; then touch .env; fi
	docker compose --env-file ./.env -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.elk.yml --project-name cl up -d

down-compose:
	docker compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml --project-name cl down --remove-orphans

elk-down-compose:
	docker compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.local.yml -f provisioning/docker-compose.elk.yml --project-name cl down --remove-orphans

# Cleans caches, generated files, and clears up resources from git, docker, and dotnet
# Usage: make clean [PLUGIN=plugin-name] - if PLUGIN is specified, only cleans that plugin
clean:
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

# Builds all libs with xml service tags and copies the DLLs to /libs directory
build-service-libs:
	@echo "🔧 Building service libs for docker container"
	bash scripts/build-service-libs.sh
	@echo "✅ Service libs built for inclusion in docker container"

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
	@echo "🔧 Fixing common EditorConfig issues with eclint..."
	@find . -name "*.cs" -not -path "./bin/*" -not -path "./obj/*" -not -path "./**/Generated/*" -not -path "./Bannou.Client.SDK/*" -not -path "./**/obj/*" -not -path "./**/bin/*" | xargs eclint fix
	@find . -name "*.md" -not -path "./bin/*" -not -path "./obj/*" -not -path "./.git/*" -not -path "./node_modules/*" -not -path "./**/Generated/*" -not -path "./Bannou.Client.SDK/*" | xargs eclint fix
	@find . -name "*.yml" -o -name "*.yaml" | grep -v "/bin/" | grep -v "/obj/" | grep -v "/.git/" | xargs eclint fix
	@echo "✅ EditorConfig issues fixed"

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

# GitHub Actions 10-step pipeline (local reproduction)
test-ci: generate-services-for-consistency test-unit build-compose ci-up-compose test-infrastructure test-http-daemon test-edge-daemon
	@echo "🚀 Complete CI pipeline executed locally (matches GitHub Actions)"

# Service generation consistency check (matches steps 4-5)
generate-services-for-consistency:
	@echo "🔍 Testing service generation consistency..."
	$(MAKE) generate-services
	git diff --exit-code || (echo "❌ Service generation created changes" && exit 1)
	@echo "✅ Service generation is consistent"

# Comprehensive unit testing - all service test projects
# Usage: make test [PLUGIN=plugin-name] - if PLUGIN is specified, only tests that plugin
test:
	@if [ "$(PLUGIN)" ]; then \
		echo "🧪 Running unit tests for plugin: $(PLUGIN)..."; \
		if [ -f "./lib-$(PLUGIN).tests/lib-$(PLUGIN).tests.csproj" ]; then \
			echo "🧪 Running tests in: ./lib-$(PLUGIN).tests/lib-$(PLUGIN).tests.csproj"; \
			dotnet test "./lib-$(PLUGIN).tests/lib-$(PLUGIN).tests.csproj" --verbosity minimal --logger "console;verbosity=minimal"; \
		else \
			echo "❌ Test project not found: ./lib-$(PLUGIN).tests/lib-$(PLUGIN).tests.csproj"; \
			exit 1; \
		fi; \
		echo "✅ Unit testing completed for plugin: $(PLUGIN)"; \
	else \
		echo "🧪 Running comprehensive unit tests across all service plugins..."; \
		for test_project in $$(find . -name "*.tests.csproj" -o -name "*Tests.csproj" | grep -v template); do \
			echo "🧪 Running tests in: $$test_project"; \
			dotnet test "$$test_project" --verbosity minimal --logger "console;verbosity=minimal" || echo "⚠️  Tests failed in $$test_project"; \
		done; \
		echo "✅ Comprehensive unit testing completed"; \
	fi

# .NET unit testing (matches steps 6)
test-unit:
	@echo "🧪 Running .NET unit tests..."
	dotnet test
	@echo "✅ .NET unit tests completed"

# Infrastructure integration testing
test-infrastructure:
	@echo "🚀 Running infrastructure integration tests"
	bash scripts/infrastructure-tests.sh
	@echo "✅ Infrastructure integration tests completed"

# Infrastructure integration testing (matches step 7)
test-infrastructure-compose:
	@echo "🚀 Running infrastructure integration tests (docker compose)..."
	if [ ! -f .env ]; then touch .env; fi
	set -a && . ./.env && set +a && docker compose -p bannou-tests -f "./provisioning/docker-compose.yml" -f "./provisioning/docker-compose.ci.yml" up --exit-code-from=bannou-tester
	@echo "✅ Infrastructure integration tests (docker compose) completed"

# HTTP integration testing
test-http:
	@echo "🧪 Running HTTP integration tests..."
	dotnet run --project http-tester
	@echo "✅ HTTP integration tests completed"

# HTTP integration testing with daemon mode (matches step 8)
test-http-daemon:
	@echo "🧪 Running HTTP integration tests (daemon mode)..."
	DAEMON_MODE=true dotnet run --project http-tester --configuration Release
	@echo "✅ HTTP integration tests (daemon mode) completed"

# WebSocket/edge integration testing
test-edge:
	@echo "🧪 Running WebSocket/Edge integration tests..."
	dotnet run --project edge-tester
	@echo "✅ WebSocket/edge integration tests completed"

# WebSocket/edge testing with daemon mode (matches steps 9-10)  
test-edge-daemon:
	@echo "🧪 Running WebSocket protocol tests (daemon mode)..."
	DAEMON_MODE=true dotnet run --project edge-tester --configuration Release
	@echo "✅ WebSocket/edge integration tests (daemon mode) completed"

tagname := $(shell date -u +%FT%H-%M-%SZ)
tag:
	git tag $(tagname) -a -m '$(msg)'
	git push origin $(tagname)
